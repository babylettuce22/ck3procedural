using Ck3MapGen.Core;

namespace Ck3MapGen.AppGUI;

/// <summary>
/// The Azgaar page's side of the main window: turning its two files and few answers into an
/// ordinary run, the way <see cref="MainForm"/>'s Quick side does for Quick.
///
/// On Create it saves anything changed in Complex as a preset, resets the settings and writes the
/// page's choices over them — the export's path included, exactly where the Azgaar menu puts it —
/// makes the heightmap the source at a size CK3 renders, and runs the ordinary write with the
/// progress going to the page. Afterwards the window holds exactly that world, so "Customize in
/// Complex" is just leaving the launcher.
/// </summary>
public sealed partial class MainForm
{
    private readonly AzgaarPage _azgaarPage = new() { Visible = false };

    private bool _onAzgaar => _launcherPage is not null && ReferenceEquals(_launcherPage, _azgaarPage);

    private AzgaarPage BuildAzgaarPage()
    {
        _azgaarPage.BackToStart += ShowStartPage;
        _azgaarPage.CreateRequested += () => AzgaarCreateAsync().Forget("azgaar world");
        _azgaarPage.CancelRequested += () => { RequestCancel(); _azgaarPage.Run.CancelPending(); };
        _azgaarPage.LaunchRequested += LaunchGame;
        _azgaarPage.OpenFolderRequested += OpenModFolder;
        _azgaarPage.CustomizeRequested += CustomizeQuickWorld;
        _azgaarPage.See3DRequested += () => ShowGroundIn3DAsync().Forget("3D ground");
        _azgaarPage.GameFolderRequested += PickGameFolder;
        _azgaarPage.GuideRequested += ShowAzgaarGuide;
        return _azgaarPage;
    }

    private void ShowAzgaarPage()
    {
        if (_busy) return;

        ShowLauncherPage(_azgaarPage);
        _azgaarPage.Begin(_state.Azgaar, Core.GameLocator.IsGameDir(_options.GameDir), _options.GameDir, _modRoot,
            ComplexSettingsNote());
    }

    private async Task AzgaarCreateAsync()
    {
        if (_busy) return;

        var choices = _azgaarPage.Choices;
        string modDir = _azgaarPage.ModDir;
        string modName = _azgaarPage.ModName;
        string modRoot = _azgaarPage.ModRoot;

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

        _state.Azgaar = choices;
        _state.Save();

        BackupComplexSettings();
        PrepareAzgaarWorld(choices, _azgaarPage.ImageFit, _azgaarPage.HasRaceTags);

        _modRoot = modRoot;
        _modName = modName;
        _options.ModName = modName;

        _azgaarPage.ShowRunning();
        Console.WriteLine($"Azgaar world: {Path.GetFileName(choices.HeightmapPath)} + {Path.GetFileName(choices.ExportPath)}");

        var (completed, took) = await RunFromLauncherAsync(_azgaarPage.Run, modDir);

        if (completed)
        {
            _azgaarPage.ShowDone(modDir, took);
            FinishLauncherRun(_azgaarPage.Run);
        }
        else
        {
            _azgaarPage.ShowFailed(_lastRun == RunOutcome.Cancelled,
                _lastRun == RunOutcome.None ? "The run did not start." : _lastRunError);
        }
    }

    /// <summary>
    /// Makes the window hold the imported world before it is built: the settings, the export and
    /// the heightmap source. The same refreshes Quick makes, for the same reasons.
    /// </summary>
    private void PrepareAzgaarWorld(AzgaarChoices choices, (int Width, int Height)? fit, bool hasRaceTags)
    {
        Preset.Reset(_options.Config);
        choices.ApplyTo(_options.Config, Random.Shared.Next(1, 1_000_000), hasRaceTags);

        _seed.Value = Math.Clamp(_options.Config.Seed, 0, int.MaxValue);
        _advanced.Checked = _options.Config.ShowAdvancedSettings;
        ApplyAzgaarChip();
        RefreshSettings();
        _climate.InvalidateModel();
        _calendar.Bind(_options.Config);
        SyncCalendarSection();

        _options.AppliedHistory = null;
        _history.ShowApplied(null);

        AdoptHeightmapFile(choices.HeightmapPath, fit, unverified: false);
    }
}
