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
        _realmGraph = null;
        _realmGraphBuilt = false;
        _realmFocus.Clear();
        ClearLoadedFrames();
        _loadedWorld = loaded;
        _lastModDir = loaded.World.DirectoryPath;
        _titles.LoadExisting(loaded.Roots);
        _grid.SelectedObject = new
        {
            World = Path.GetFileName(loaded.World.DirectoryPath),
            Folder = loaded.World.DirectoryPath,
            StartDate = loaded.World.StartDate,
            Titles = loaded.World.Titles.Count,
            Characters = loaded.World.Characters.Count,
            Mode = "Editing existing files. Generation settings are inactive.",
        };
        _tabs.SelectedIndex = 0;
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
        _savePreset.Enabled = _loadPreset.Enabled = _preview.Enabled = _drape.Enabled = false;
        _forge.Enabled = false;
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
            ClearLoadedFrames();
            SelectLoadedView(_view);
            foreach (var inspector in _inspectors.Values) inspector.RefreshLoaded();
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
        _tabs.SelectedIndex = 0;
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
