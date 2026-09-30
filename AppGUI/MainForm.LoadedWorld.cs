using Ck3MapGen.Core;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.AppGUI;

public sealed partial class MainForm
{
    private LoadedWorldView? _loadedWorld;

    internal void AdoptLoadedWorld(LoadedWorldView loaded)
    {
        _sourceCts?.Cancel();
        _sourceGeneration++;
        foreach (var inspector in _inspectors.Values) { inspector.Inspect([]); inspector.Hide(); }
        _titles.UnloadExisting();
        _edits.Detach();
        _result = null;
        _written = null;
        _history.Attach(null, null);
        _calendar.ShowGenerated(null);
        _realmGraph = null;
        _realmGraphBuilt = false;
        _realmFocus.Clear();
        ClearLoadedFrames();
        _loadedWorld = loaded;
        _loadedFieldShown = null;
        _sourceShown = false;   // nothing reloads the generator's source over the mod's heightmap
        _solid.SetField(null, null, "Nothing to show.");
        // Cleared first so a drape chosen for the last world is not rendered for this one on open.
        _drape.Items.Clear();
        RefreshDrapeChoices();
        _lastModDir = loaded.World.DirectoryPath;
        _titles.LoadExisting(loaded.Roots);

        // The grid carries the loaded world's summary, so it has to be the thing on show.
        if (_calendar.Visible) _sections.SelectedIndex = 0;
        _grid.SelectedObject = new
        {
            World = Path.GetFileName(loaded.World.DirectoryPath),
            Folder = loaded.World.DirectoryPath,
            StartDate = loaded.World.StartDate,
            Titles = loaded.World.Titles.Count,
            Characters = loaded.World.Characters.Count,
            Mode = "Editing existing files. Generation settings are inactive.",
        };
        // Terrain and Climate make inputs to generation, which an opened mod does not have. They
        // leave the bar rather than sitting there refusing clicks.
        _workspaceBar.SetSingle(true);
        SelectWorkspace(Workspace.World);
        SelectWorldView(WorldView.Map);
        Text = $"CK3 Procedural Map Tool — editing {Path.GetFileName(loaded.World.DirectoryPath)}";
        SelectLoadedView("Counties");
        ConfigureLoadedControls(true);
        ShowLoadedPending();
        Console.WriteLine($"Opened existing world: {loaded.World.DirectoryPath}");
        Console.WriteLine($"  {loaded.World.Entries.Count:N0} entries; original files are unchanged.");
        foreach (string note in loaded.World.Notes.Distinct()) Console.WriteLine("  " + note);
    }

    private void ConfigureLoadedControls(bool enabled)
    {
        _grid.Enabled = true; // The loaded-world summary itself has read-only properties.
        _sections.Enabled = _settingsSearch.Enabled = _advanced.Enabled = false;
        _seed.Enabled = _roll.Enabled = _browse.Enabled = _recent.Enabled = _azgaar.Enabled = false;
        _savePreset.Enabled = _loadPreset.Enabled = _preview.Enabled = false;
        _forge.Enabled = false;

        // The 3D view shows the mod's own heightmap.png (ShowLoadedHeightmapAsync), wearing any view
        // the loaded world can draw.
        _drape.Enabled = enabled;
        _writeMod.Text = "Save edits";
        _writeMod.Enabled = enabled;
        _browse.Text = "Existing world";
        _tips.SetToolTip(_browse, "File → Return to generator to create a different world.");
        _tips.SetToolTip(_preview, "Existing world: generation is disabled to preserve the original output.");
        _titles.Enabled = enabled;
        _openMod.Enabled = enabled;
    }

    private void SelectLoadedView(string name)
    {
        if (_loadedWorld is null) return;
        if (!_loadedWorld.Available(name))
        {
            _status.Text = "That simulation layer was not saved in this mod. Available maps show the original exported world.";
            return;
        }
        var mode = MapModes.Find(name)!;
        bool switched = _view != mode.Name;
        _view = mode.Name;
        _category = mode.Category;
        _lastInCategory[_category] = _view;
        RestyleStrip();
        ShowLegend(mode);
        _viewer.ViewName = mode.Name;
        _viewer.Cursor = mode.Clickable ? Cursors.Hand : Cursors.Default;

        if (switched)
        {
            _status.Text = mode.Pick?.Kind switch
            {
                MapPick.Culture => "Click a county to inspect and edit its culture",
                MapPick.Faith => "Click a county to inspect and edit its faith",
                MapPick.Realm => "Click a realm to focus it · Ctrl+click jumps to a county · Esc steps back",
                MapPick.Title => $"Click a {TierWord(mode.Pick.Value.Tier)} to inspect and edit it",
                _ => $"Loaded world · {name}",
            };
        }

        // Focused realm frames bypass the cache, the same way the generator's do.
        var oldFocus = _focusFrame;
        _focusFrame = null;
        Bitmap? bitmap;
        if (mode.Name == "Realms" && _realmFocus.Count > 0 && Realm is not null)
        {
            using (new WaitCursorFor(this)) bitmap = _loadedWorld.RenderRealmsFocused(_realmFocus[^1]);
            _focusFrame = bitmap;
        }
        else if (!_rendered.TryGetValue(name, out bitmap))
        {
            using (new WaitCursorFor(this)) bitmap = _loadedWorld.Render(name);
            _rendered[name] = bitmap;
        }
        _viewer.SetImage(bitmap);
        oldFocus?.Dispose();
        ShowReadout(_viewer.Zoom, null);
    }

    /// <summary>The loaded world whose heightmap the 3D view holds or is reading, so it is read once per world.</summary>
    private LoadedWorldView? _loadedFieldShown;

    /// <summary>
    /// The 3D view for an opened mod: its own map_data/heightmap.png, the heightmap the game draws.
    /// Read when the 3D view is first opened, as the generator's source is, and not at open time —
    /// a full-size heightmap is seconds to decode and pack, for a view nobody may ask for.
    /// </summary>
    private async Task ShowLoadedHeightmapAsync(LoadedWorldView loaded)
    {
        if (ReferenceEquals(_loadedFieldShown, loaded)) return;
        _loadedFieldShown = loaded;

        int generation = ++_sourceGeneration;
        _sourceCts?.Cancel();
        _solid.SetField(null, null, "Reading the mod's heightmap…");
        try
        {
            var (source, packed) = await Task.Run(loaded.ReadHeightfields);
            if (generation != _sourceGeneration || !ReferenceEquals(loaded, _loadedWorld)) return;

            const string label = "Heightmap as shipped";
            if (!label.Equals(_sourceMode.Items[0])) _sourceMode.Items[0] = label;
            _solid.SetField(source, packed, "Nothing to show.");
        }
        catch (Exception error)
        {
            if (generation != _sourceGeneration) return;
            _solid.SetField(null, null, $"Could not read the mod's heightmap: {error.Message}");
        }
    }

    /// <summary>
    /// A pixel of the view on show, moved onto the grid the loaded world's probes read. Every
    /// political view is drawn on that grid already; the CK3 ground is drawn at its own, finer one.
    /// </summary>
    private Point OnLoadedGrid(Point pixel)
    {
        if (_loadedWorld is not { } loaded || !_rendered.TryGetValue(_view, out var shown)) return pixel;
        int cols = Math.Max(1, loaded.Raster.Width / loaded.Step), rows = Math.Max(1, loaded.Raster.Height / loaded.Step);
        if (shown.Width == cols) return pixel;
        return new Point(pixel.X * cols / Math.Max(1, shown.Width), pixel.Y * rows / Math.Max(1, shown.Height));
    }

    private void LoadedEditsChanged()
    {
        if (_loadedWorld is null) return;
        _loadedWorld.RefreshShells();
        _titles.RefreshExisting();
        ShowLoadedPending();
        // Wait until PropertyGrid's commit returns before refreshing inspectors and the map.
        Post(() =>
        {
            if (_loadedWorld is null) return;
            // The ground is drawn from the terrain files, which no edit here touches: kept rather
            // than redrawn, which is seconds on a full-size map.
            _rendered.Remove("CK3 ground", out var ground);
            ClearLoadedFrames();
            if (ground is not null) _rendered["CK3 ground"] = ground;
            SelectLoadedView(_view);
            foreach (var inspector in _inspectors.Values) inspector.RefreshLoaded();
            // The 3D view's drape too; not the ground, which no edit here changes and is seconds to redraw.
            if (_drape.SelectedItem is string drape && drape != "CK3 ground") UpdateDrape();
        });
    }

    private void ShowLoadedPending()
    {
        if (_loadedWorld is null) return;
        int count = _loadedWorld.World.ChangedFiles.Count();
        _pendingBar.Visible = count > 0;
        _pendingText.Text = $"{count} changed files in the loaded world — only edited values will be saved";
        _overwrite.Text = "Save edits";
        _overwrite.Enabled = !_busy;
        _revertAll.Enabled = !_busy && count > 0;
    }

    private bool SaveLoadedWorld()
    {
        if (_loadedWorld is null || _busy) return false;
        foreach (var inspector in _inspectors.Values.Where(i => i.Visible)) inspector.CommitGrid();
        try
        {
            int count;
            using (new WaitCursorFor(this)) count = _loadedWorld.World.Save();
            ShowLoadedPending();
            _status.Text = count == 0 ? "No changes to save" : $"Saved {count} files in the loaded world";
            if (count > 0) Console.WriteLine($"Saved {count} files. Original files backed up to: {_loadedWorld.World.LastBackup}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            MessageBox.Show(this, ex.Message, "Could not save world", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private bool ConfirmLoadedEdits()
    {
        foreach (var inspector in _inspectors.Values.Where(i => i.Visible)) inspector.CommitGrid();
        if (_loadedWorld?.World.ChangedFiles.Any() != true) return true;
        var choice = MessageBox.Show(this, "Save the loaded world's edits before leaving it?", "Unsaved edits", MessageBoxButtons.YesNoCancel);
        return choice == DialogResult.No || choice == DialogResult.Yes && SaveLoadedWorld();
    }

    private void CloseLoadedWorld()
    {
        if (_loadedWorld is null || !ConfirmLoadedEdits()) return;
        foreach (var inspector in _inspectors.Values) { inspector.Inspect([]); inspector.Hide(); }
        _loadedWorld = null;
        _realmFocus.Clear();

        // The 3D view goes back to the generator's heightmap source the next time it is opened.
        _loadedFieldShown = null;
        _sourceGeneration++;
        _sourceShown = false;
        _solid.SetField(null, null, "Nothing to show.");
        RefreshDrapeChoices();
        Text = "CK3 Procedural Map Tool";
        ClearLoadedFrames();
        _titles.UnloadExisting();
        _writeMod.Text = "Write mod";
        _overwrite.Text = "Overwrite mod";
        _tips.SetToolTip(_preview, "Generate and preview without writing anything (F5)");
        _pendingBar.Visible = false;
        RefreshSettings();
        // ApplySource restores the heightmap button's label and tooltip and re-enables every
        // control the loaded mode switched off, through SetEnabled.
        ApplySource();
        _workspaceBar.SetSingle(false);
        SelectWorldView(WorldView.Map);
        SelectView("Relief");
        _status.Text = "Generator ready — choose a heightmap or build a preview";
    }

    private void ClearLoadedFrames()
    {
        _viewer.SetImage(null);
        foreach (var bitmap in _rendered.Values) bitmap.Dispose();
        _rendered.Clear();
        _focusFrame?.Dispose();
        _focusFrame = null;
    }

    /// <summary>
    /// The window as the verification tool sees it. Shown, because a form that has never been
    /// shown has no laid-out children to draw — but at zero opacity, and hidden again rather
    /// than closed, so nothing flashes and the closing handler never saves window state.
    /// </summary>
    internal void RenderLoadedPreview(string path)
    {
        double opacity = Opacity;
        Opacity = 0;
        Show();
        Application.DoEvents();
        try
        {
            using var bitmap = new Bitmap(Width, Height);
            DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height));
            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
        finally
        {
            Hide();
            Opacity = opacity;
        }
    }
}
