using System.Diagnostics;
using Ck3MapGen.Core;

namespace Ck3MapGen.AppGUI;

/// <summary>
/// The Quick generator's side of the main window: turning the page's choices into an ordinary run.
///
/// Quick is a way of filling in the normal settings, not a generator of its own. On Create it:
/// <list type="number">
/// <item>makes the chosen Forge preset the Terrain workspace's project, at the chosen seed and size,
/// and the pipeline the heightmap source;</item>
/// <item>resets the settings to their defaults and writes the Quick choices over them, after saving
/// anything changed in Complex this session as a preset, so nothing is lost;</item>
/// <item>runs the ordinary write, with the progress going to the Quick page instead of the status
/// bar, and without switching to the World workspace.</item>
/// </list>
/// Afterwards the window holds exactly the world that was made — settings, terrain pipeline and
/// result — so "Customize in Complex" is just leaving the launcher.
/// </summary>
public sealed partial class MainForm
{
    private readonly QuickPage _quick = new() { Visible = false };

    /// <summary>True while a run started from the Quick page is going.</summary>
    private bool _quickRunning;

    /// <summary>How the last run ended, which the Quick page turns into its closing screen.</summary>
    private enum RunOutcome { None, Completed, Cancelled, Failed }

    private RunOutcome _lastRun;
    private string? _lastRunError;

    private QuickPage BuildQuickPage()
    {
        _quick.BackToStart += ShowStartPage;
        _quick.CreateRequested += () => QuickCreateAsync().Forget("quick world");
        _quick.CancelRequested += () => { RequestCancel(); _quick.CancelPending(); };
        _quick.LaunchRequested += LaunchGame;
        _quick.OpenFolderRequested += OpenModFolder;
        _quick.CustomizeRequested += CustomizeQuickWorld;
        _quick.GameFolderRequested += PickGameFolder;
        return _quick;
    }

    private void ShowQuickPage()
    {
        if (_busy) return;

        ShowLauncherPage(_quick);
        _quick.Begin(_state.Quick, Core.GameLocator.IsGameDir(_options.GameDir), _options.GameDir, _modRoot,
            ComplexSettingsNote());
    }

    /// <summary>
    /// What the review step should say about settings changed in Complex, or null when there are
    /// none. Quick starts from defaults, so anything changed is saved aside first rather than lost.
    /// </summary>
    private string? ComplexSettingsNote()
        => Preset.IsDefault(_options.Config)
            ? null
            : "Settings you changed in Complex are saved as a preset before this world replaces them.";

    private static string ComplexBackupPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Ck3MapGen", "settings-before-quick.json");

    private async Task QuickCreateAsync()
    {
        if (_busy) return;

        var choices = _quick.Choices;
        string modDir = _quick.ModDir;
        string modName = _quick.ModName;
        string modRoot = _quick.ModRoot;

        // An opened mod is edited, not generated; it has to be put down first, the way File ▸
        // Return to generator would.
        if (_loadedWorld is not null)
        {
            CloseLoadedWorld();
            if (_loadedWorld is not null) return;
        }

        if (_edits.HasPending)
        {
            var answer = MessageBox.Show(this,
                $"Making a new world discards {Count(_edits.EditedCount, "unsaved edit")} to the current one.\n\n"
                + "Make the new world anyway?",
                "Unsaved edits", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
            if (answer != DialogResult.OK) return;
        }

        if (!EnsureGameFolder()) return;

        _state.Quick = choices;
        _state.Save();

        BackupComplexSettings();

        IReadOnlyList<string> switchedOff;
        try
        {
            switchedOff = PrepareQuickWorld(choices);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"The map type could not be loaded:\n\n{ex.Message}",
                "Could not start", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        _modRoot = modRoot;
        _modName = modName;
        _options.ModName = modName;

        _quickRunning = true;
        _quick.ShowRunning();
        _written = null;
        _lastRun = RunOutcome.None;
        _lastRunError = null;
        var clock = Stopwatch.StartNew();

        Console.WriteLine($"Quick world: {choices.MapType}, seed {choices.Seed}, {choices.Pixels.Width}x{choices.Pixels.Height}");
        foreach (string stage in switchedOff)
            Console.WriteLine($"  {stage} switched off: it needs a Direct3D 12 GPU, and none was found.");

        try
        {
            await WriteModIntoAsync(modDir, carried: null);
        }
        finally
        {
            _quickRunning = false;
        }

        if (_lastRun == RunOutcome.Completed && _result is not null && _written is not null && Directory.Exists(modDir))
        {
            _quick.ShowDone(modDir, clock.Elapsed);
            RenderQuickResultAsync().Forget("quick result map");
        }
        else
        {
            _quick.ShowFailed(_lastRun == RunOutcome.Cancelled,
                _lastRun == RunOutcome.None ? "The run did not start." : _lastRunError);
        }
    }

    /// <summary>
    /// Anything changed in Complex this session is written to a preset before Quick replaces it,
    /// so it can be loaded back with Load preset. Nothing is written when there is nothing to keep.
    /// </summary>
    private void BackupComplexSettings()
    {
        if (Preset.IsDefault(_options.Config)) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ComplexBackupPath)!);
            Preset.Save(_options.Config, ComplexBackupPath);
            Console.WriteLine($"Your previous settings were saved to {ComplexBackupPath} — Load preset brings them back.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not save the previous settings: {ex.Message}");
        }
    }

    /// <summary>
    /// Makes the window hold the Quick world before it is built: the Forge project, the settings
    /// and the heightmap source. Returns the stages switched off for want of a GPU.
    /// </summary>
    private IReadOnlyList<string> PrepareQuickWorld(QuickChoices choices)
    {
        var type = QuickCatalogue.All().FirstOrDefault(t => t.Key == choices.MapType)
                   ?? throw new InvalidOperationException($"No map type called '{choices.MapType}' is installed.");

        var (width, height) = choices.Pixels;
        var switchedOff = _forge.AdoptPreset(type.PresetPath, choices.Seed, width, height);

        Preset.Reset(_options.Config);
        choices.ApplyTo(_options.Config);

        // What Load preset refreshes after replacing the settings, for the same reasons.
        _seed.Value = Math.Clamp(_options.Config.Seed, 0, int.MaxValue);
        _advanced.Checked = _options.Config.ShowAdvancedSettings;
        ApplyAzgaarChip();
        RefreshSettings();
        _climate.InvalidateModel();
        _calendar.Bind(_options.Config);
        SyncCalendarSection();

        // A history applied in the History workspace belongs to the previous world.
        _options.AppliedHistory = null;
        _history.ShowApplied(null);

        UseForgeForGeneration(allowUnverifiedSize: false);
        return switchedOff;
    }

    /// <summary>
    /// The finished world's picture: its realms as they stand at the start. Drawn off the UI thread,
    /// since it walks the whole realm tree; until it lands the page shows the last live picture.
    /// </summary>
    private async Task RenderQuickResultAsync()
    {
        if (_result is not { } result || _written is not { } written) return;
        var mode = MapModes.Find("Realms") ?? MapModes.Find("Kingdoms");
        if (mode is null) return;

        try
        {
            var image = await Task.Run(() => mode.Render(result, written));
            _quick.SetDoneImage(ToBitmap(image), mode.Name == "Realms" ? "Realms at the start" : mode.Name);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not draw the finished world's map: {ex.Message}");
        }
    }

    /// <summary>
    /// "Customize in Complex": the generator, on the World workspace, holding the world just made —
    /// its settings in the grid, its terrain in the Terrain workspace, its maps in the viewer.
    /// </summary>
    private void CustomizeQuickWorld()
    {
        if (_busy) return;
        SelectWorkspace(Workspace.World);
    }
}
