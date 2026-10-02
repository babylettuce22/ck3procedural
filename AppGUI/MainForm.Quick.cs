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

    /// <summary>
    /// The run screen of the launcher page whose run is going — Quick's or Azgaar's — or null when
    /// no run was started from a launcher page. Progress, pictures and discoveries go to it.
    /// </summary>
    private RunScreen? _launcherRun;

    /// <summary>How the last run ended, which the Quick page turns into its closing screen.</summary>
    private enum RunOutcome { None, Completed, Cancelled, Failed }

    private RunOutcome _lastRun;
    private string? _lastRunError;

    private QuickPage BuildQuickPage()
    {
        _quick.BackToStart += ShowStartPage;
        _quick.CreateRequested += () => QuickCreateAsync().Forget("quick world");
        _quick.CancelRequested += () => { RequestCancel(); _quick.Run.CancelPending(); };
        _quick.LaunchRequested += LaunchGame;
        _quick.OpenFolderRequested += OpenModFolder;
        _quick.CustomizeRequested += CustomizeQuickWorld;
        _quick.See3DRequested += () => ShowGroundIn3DAsync().Forget("3D ground");
        _quick.GameFolderRequested += PickGameFolder;
        _quick.PlayPauseRequested += () => SetHistoryPlaying(!_historyClock.Enabled);
        _quick.PaceRequested += () =>
        {
            _historyFast = !_historyFast;
            SetHistoryPlaying(_historyClock.Enabled);
        };
        _quick.AcceptRequested += () => AcceptQuickHistoryAsync().Forget("accept history");
        _quick.ContinueHistoryRequested += () => ContinueQuickHistoryAsync().Forget("continue history");
        _historyClock.Tick += (_, _) => OnHistoryYear();
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

        _quick.ShowRunning();

        // Printed by the run itself (RunLog.Begin clears whatever is printed before it), and kept for
        // the history re-write, which is the same world.
        _quickSummary = choices.Summary()
            + string.Concat(switchedOff.Select(stage =>
                $"\n  {stage} switched off: it needs a Direct3D 12 GPU, and none was found."));

        _options.QuickSummary = _quickSummary;
        bool completed;
        TimeSpan took;
        try
        {
            (completed, took) = await RunFromLauncherAsync(_quick.Run, modDir);
        }
        finally
        {
            _options.QuickSummary = null;
        }

        if (completed)
        {
            // The world's history is readied but not started: the done screen asks whether to run
            // it on first. A world with no grown realms to run is simply done.
            (_quickModDir, _quickTook) = (modDir, took);
            _quickHistory = await PrepareQuickHistoryAsync();
            _quickHistoryWritten = false;
            _quick.Run.PaintMark = _quickHistory is { } marks ? marks.Paint : null;
            if (_quickHistory is { } history) _quick.Run.OfferHistory(history.Began);

            _quick.ShowDone(modDir, took);
            FinishLauncherRun(_quick.Run);
        }
        else
        {
            _quick.ShowFailed(_lastRun == RunOutcome.Cancelled,
                _lastRun == RunOutcome.None ? "The run did not start." : _lastRunError);
        }
    }

    /// <summary>
    /// The ordinary write, watched from a launcher page: its progress, pictures and discoveries go
    /// to <paramref name="run"/> for as long as it lasts. Returns whether the mod was written, and
    /// how long it took.
    /// </summary>
    private async Task<(bool Completed, TimeSpan Took)> RunFromLauncherAsync(RunScreen run, string modDir)
    {
        _written = null;
        _lastRun = RunOutcome.None;
        _lastRunError = null;
        var clock = Stopwatch.StartNew();

        // What the generator makes along the way goes to the page's discoveries column. Listened
        // to only for this run: with nobody listening, the generator builds none of it.
        _launcherRun = run;
        Core.Showcase.Published += OnShowcase;
        Core.Showcase.Sketched += OnSketch;
        Core.Showcase.Pictured += OnPicture;
        try
        {
            await WriteModIntoAsync(modDir, carried: null, restoreHistory: false);
        }
        finally
        {
            Core.Showcase.Published -= OnShowcase;
            Core.Showcase.Sketched -= OnSketch;
            Core.Showcase.Pictured -= OnPicture;
            _launcherRun = null;
        }

        bool completed = _lastRun == RunOutcome.Completed && _result is not null && _written is not null && Directory.Exists(modDir);
        return (completed, clock.Elapsed);
    }

    /// <summary>The finished world counted and drawn on a run screen already showing its done view.</summary>
    private void FinishLauncherRun(RunScreen run)
    {
        if (_result is null || _written is null) return;
        run.SetTallies(QuickTallies(_result, _written));
        RenderLauncherResultAsync(run).Forget("launcher result map");
    }

    /// <summary>
    /// The finished world counted for the done screen: its title ladder, its peoples and faiths,
    /// and its regiments. Only what this world has; a zero is left out rather than shown.
    /// </summary>
    private static IReadOnlyList<(string Label, int Count)> QuickTallies(GenerationResult result, Emit.WrittenContent written)
    {
        var titles = MapGen.Titles.Flatten(result.Titles).ToList();
        int Tier(string tier) => titles.Count(t => t.Tier == tier);

        var tallies = new List<(string Label, int Count)>
        {
            ("Counties", Tier("c")),
            ("Duchies", Tier("d")),
            ("Kingdoms", Tier("k")),
            ("Cultures", written.Cultures.ByCounty.Values.Distinct().Count()),
            ("Faiths", written.Faiths.Faiths.Count(f => f.Counties.Count > 0)),
            ("Men-at-arms", written.Retinues?.Regiments.Count ?? 0),
        };

        // Rulers in place of an empty column, when the world has them and no regiments.
        if (tallies[^1].Count == 0 && written.Rulers is { } rulers)
            tallies[^1] = ("Rulers", rulers.All.Count);

        return tallies.Where(t => t.Count > 0).ToList();
    }

    /// <summary>Called on the generator's thread; the item is plain values, so it crosses as it is.</summary>
    private void OnShowcase(Core.ShowcaseItem item) => Post(() => _launcherRun?.OfferShowcase(item));

    /// <summary>The partition forming; plain arrays, so it crosses as it is too.</summary>
    private void OnSketch(Core.PartitionSketch sketch) => Post(() => _launcherRun?.OfferSketch(sketch));

    /// <summary>A picture of a layer the content stages decided: the peoples, the faiths.</summary>
    private void OnPicture(Core.ShowcasePicture picture) => Post(() =>
    {
        if (_launcherRun is { } run) run.OfferLiveImage(picture.View, ToBitmap(picture.Image));
    });

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

        var (width, height) = choices.ForgePixels;
        int upscale = choices.ForgeUpscale;
        var switchedOff = _forge.AdoptPreset(type.PresetPathFor(choices.Mountains), choices.Seed, width, height, upscale);

        // A set-piece map type draws its crater (or whatever it is built around) for this seed
        // over the guide the preset shipped with; the Terrain workspace shows it as paint.
        QuickFeatures.Draw(_forge.Session.Pipeline, type.Feature, choices.Seed);

        // The relief choice bends the preset's own relief stages; the Terrain workspace shows the
        // result as ordinary parameter values, so it can be tuned further from there.
        QuickTerrain.Apply(_forge.Session.Pipeline, choices.Relief);
        _forge.Session.Pipeline.NotifyChanged();

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

        // The workspace holds the choice map-wide, the only form one pipeline can; the world itself
        // is built with it region by region. See RegionalRelief.
        if (choices.RegionalRelief)
            SetSource(new QuickReliefProvider(type.PresetPathFor(choices.Mountains), choices.Seed, width, height,
                type.Feature, choices.Relief, type.Key, upscale));
        return switchedOff;
    }

    /// <summary>
    /// The finished world's picture: its realms as they stand at the start. Drawn off the UI thread,
    /// since it walks the whole realm tree; until it lands the page shows the last live picture.
    /// </summary>
    private async Task RenderLauncherResultAsync(RunScreen run)
    {
        if (_result is not { } result || _written is not { } written) return;
        var mode = MapModes.Find("Realms") ?? MapModes.Find("Kingdoms");
        if (mode is null) return;

        try
        {
            var image = await Task.Run(() => mode.Render(result, written));
            run.SetDoneImage(ToBitmap(image), mode.Name == "Realms" ? "Realms at the start" : mode.Name);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not draw the finished world's map: {ex.Message}");
        }
    }

    // --- History: the Quick world lived on until the player accepts it ---------------------------

    /// <summary>Milliseconds a simulated year takes on screen, at the two paces the page offers.</summary>
    private const int SlowYearMs = 1000, FastYearMs = 200;

    /// <summary>The history of the world just made, running on from its start date. See <see cref="QuickHistory"/>.</summary>
    private QuickHistory? _quickHistory;

    /// <summary>The Quick world's choices as the log states them; see <see cref="GenerationOptions.QuickSummary"/>.</summary>
    private string? _quickSummary;

    /// <summary>
    /// True once the world has been written again from <see cref="_quickHistory"/>: continuing then
    /// picks the history up from the world as written — the History workspace's chaining — rather
    /// than running on a simulation whose titles the write has since moved.
    /// </summary>
    private bool _quickHistoryWritten;

    private readonly System.Windows.Forms.Timer _historyClock = new() { Interval = SlowYearMs };
    private bool _historyFast;
    private string _quickModDir = "";
    private TimeSpan _quickTook;

    /// <summary>
    /// The written world's history, readied to run on from its start date. Null when there is none
    /// to run — the world's realms were not grown — or it could not be prepared.
    /// </summary>
    private async Task<QuickHistory?> PrepareQuickHistoryAsync()
    {
        if (_result is not { } result || _written is not { } written) return null;
        try
        {
            return await QuickHistory.PrepareAsync(result, written, _options.AppliedHistory);
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine($"The world's history could not be prepared: {ex}");
            return null;
        }
    }

    /// <summary>Starts or stops the clock, at the pace chosen, and says so on the page.</summary>
    private void SetHistoryPlaying(bool playing)
    {
        _historyClock.Interval = _historyFast ? FastYearMs : SlowYearMs;
        if (playing && _quickHistory is not null) _historyClock.Start();
        else _historyClock.Stop();
        _quick.Run.SetHistoryState(_historyClock.Enabled, _historyFast, busy: null);
    }

    /// <summary>
    /// A year of history. The clock stops itself when the page has gone — Start, Make another,
    /// or anything else that leaves the history view — so nothing runs where nobody is watching.
    /// </summary>
    private void OnHistoryYear()
    {
        if (_quickHistory is not { } history || _busy || !_quick.Visible || !_quick.Run.InHistory)
        {
            _historyClock.Stop();
            return;
        }

        history.Tick();
        ShowHistoryYear();
    }

    private void ShowHistoryYear()
    {
        if (_quickHistory is not { } history) return;
        _quick.Run.SetHistoryFrame(history.Render(), history.Year, history.Began, history.Standing());
        _quick.Run.AddChronicle(history.TakeHeadlines());
    }

    /// <summary>
    /// The world as it stands is the one the game starts with. Accepted where it began, the mod
    /// on disk already is that world; otherwise the realm and calendar layers are written again
    /// over it (<see cref="Emit.ContentWriter.ApplyHistory"/>, seconds rather than the minutes of
    /// the first run), and the page moves on to the done screen.
    /// </summary>
    private async Task AcceptQuickHistoryAsync()
    {
        if (_quickHistory is not { } history || _busy) return;
        SetHistoryPlaying(false);

        if (history.Moved)
        {
            if (_edits.Target is not { Written.World: not null } target || !Directory.Exists(target.ModDir))
            {
                MessageBox.Show(this, "The world's mod folder is no longer where it was written, so its history cannot be "
                    + "written into it. Make the world again.", "Accept this world", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var applied = history.Capture();
            _quick.Run.SetHistoryState(false, _historyFast, $"Writing the world as it stands in {applied.Year}…");
            _quick.SetWriting(true);
            bool written;
            _options.QuickSummary = _quickSummary;
            try
            {
                written = await ReemitHistoryAsync(target, applied);
            }
            finally
            {
                _options.QuickSummary = null;
                _quick.SetWriting(false);
            }

            if (!written)
            {
                _quick.Run.SetHistoryState(false, _historyFast, busy: null);
                return;
            }

            _options.AppliedHistory = applied;
            _history.ShowApplied(applied);
            _quickHistoryWritten = true;
        }

        string? note = _options.AppliedHistory is { } now
            ? $"It begins in {now.Year}, after {now.Year - now.ChronicleSince} years of history."
            : null;
        _quick.ShowDone(_quickModDir, _quickTook, note);
        FinishLauncherRun(_quick.Run);
    }

    /// <summary>
    /// From the done screen to the history: started for the first time when the player says yes to
    /// it, or carried on from where it was accepted. After a write that is the world as written,
    /// picked up again; otherwise the same simulation simply carries on.
    /// </summary>
    private async Task ContinueQuickHistoryAsync()
    {
        if (_busy || _quickHistory is not { } history) return;

        _quick.ShowHistory(history.Began);
        if (_quickHistoryWritten)
        {
            _quick.Run.SetHistoryState(false, _historyFast, "Picking up the thread…");
            var resumed = await PrepareQuickHistoryAsync();
            if (!_quick.Visible || !_quick.Run.InHistory) return;
            if (resumed is null)
            {
                _quick.Run.SetHistoryState(false, _historyFast, busy: null);
                return;
            }
            _quickHistory = resumed;
            _quickHistoryWritten = false;
            _quick.Run.PaintMark = resumed.Paint;
        }

        ShowHistoryYear();
        SetHistoryPlaying(true);
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

    private GroundViewWindow? _groundWindow;
    private bool _groundPending;

    /// <summary>
    /// The done screen's "See in 3D": the written world's CK3 ground draped over its shipped relief,
    /// in a window of its own. One at a time; opening it again replaces it with the world as it
    /// now stands, since a history accepted in between rewrites the mod.
    /// </summary>
    private async Task ShowGroundIn3DAsync()
    {
        if (_busy || _groundPending || _result is not { } result || _written is not { } written) return;

        _groundPending = true;
        Cursor = Cursors.WaitCursor;
        try
        {
            // The processed fields are usually ready: every build prepares them for the 3D tab.
            var fields = _processedSource is { } s && _processedPacked is { } p
                ? (s, p)
                : await Task.Run(() => ProcessedFields(result));
            var ground = await Task.Run(() => GroundPreview.Render(result, written));

            // The world moved on while this was rendering; what was drawn no longer describes it.
            if (!ReferenceEquals(result, _result)) return;

            _groundWindow?.Close();
            _groundWindow = new GroundViewWindow(_modName ?? "World", fields.Item1, fields.Item2, ground);
            // A modeless window disposes itself on close; only the reference needs letting go.
            _groundWindow.FormClosed += (sender, _) =>
            {
                if (ReferenceEquals(sender, _groundWindow)) _groundWindow = null;
            };
            _groundWindow.Show(this);
        }
        finally
        {
            _groundPending = false;
            Cursor = Cursors.Default;
        }
    }
}
