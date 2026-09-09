using Ck3MapGen.Emit;
using Ck3MapGen.Core;

namespace Ck3MapGen.AppGUI;

/// <summary>
/// The shared machinery behind every inspector: a small modeless window holding whatever was last
/// clicked.
///
/// One window per *kind* of thing rather than per thing. A window per instance becomes window soup
/// within a minute of use, and a single window that navigates back and forth loses what the split
/// is actually for — having a county and the culture that lives in it visible at once. Four
/// windows, each reused, gives that without either failure.
///
/// The editor is a <see cref="PropertyGrid"/>, for the reason the settings pane is one: it derives
/// an editor from an object's properties, so a new editable field is a property on a subclass's
/// wrapper and no UI code at all. It also takes several objects at once, which is what makes
/// selecting a duchy and recolouring all its counties work without a bulk-edit dialog.
///
/// Subclasses supply three things: the window's name, the wrappers for a selection, and any buttons
/// beyond Revert.
/// </summary>
public abstract class InspectorForm : Form
{
    protected readonly WorldEdits Edits;

    private readonly PropertyGrid _grid = new()
    {
        Dock = DockStyle.Fill,
        PropertySort = PropertySort.Categorized,
        HelpVisible = true,
        ToolbarVisible = false,
    };

    // Auto-sized in height rather than fixed: the bar wraps when a window is narrower than its
    // buttons, and a fixed 36px strip clipped every wrapped row out of sight — which is how the
    // Title inspector's Ruler… button went missing the moment it was added.
    private readonly FlowLayoutPanel _actions = new()
    {
        Dock = DockStyle.Bottom,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        MinimumSize = new Size(0, 36),
        Padding = new Padding(4, 4, 4, 4),
        BackColor = Theme.Surface,
    };

    private readonly Label _heading = new()
    {
        Dock = DockStyle.Top,
        Height = 26,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Theme.Text,
        Font = Theme.Ui,
        Padding = new Padding(8, 0, 0, 0),
        BackColor = Theme.Surface,
    };

    private readonly Button _revert = Theme.MakeButton("Revert", 68);

    /// <summary>What is being inspected. Subclasses read this to build their wrappers.</summary>
    protected IReadOnlyList<object> Selection { get; private set; } = [];

    /// <summary>
    /// Raised when the user asks to look at something related — a county's culture, a faith's
    /// religion. The window that owns the inspectors routes it by type; an inspector deliberately
    /// knows nothing about the others.
    /// </summary>
    public event Action<object>? Navigate;
    /// <summary>
    /// The opened mod when this inspector shows file-backed entries, else null. In that mode the
    /// selection holds <see cref="WorldEntry"/> objects and every edit goes to the files through
    /// them; the generator's <see cref="WorldEdits"/> is detached and never consulted.
    /// </summary>
    protected LoadedWorldView? Loaded { get; private set; }
    private Action? _loadedChanged;

    /// <summary>Whether there is a world to edit at all — generated and written, or opened from disk.</summary>
    protected bool Live => Edits.IsLoaded || Loaded is not null;

    protected InspectorForm(WorldEdits edits, string title, Size size)
    {
        Edits = edits;

        Text = title;
        StartPosition = FormStartPosition.Manual;
        MinimumSize = new Size(300, 320);
        Size = size;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Ui;

        // No taskbar entry and no minimise: this is a palette belonging to the main window, not a
        // second place the application lives.
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;

        Theme.ApplyLight(_grid);

        _revert.Click += (_, _) =>
        {
            if (Loaded is { } loaded)
            {
                loaded.Revert(Selection.OfType<WorldEntry>());
                _grid.Refresh();
                _loadedChanged?.Invoke();
                return;
            }
            foreach (var target in Selection) Edits.Revert(target);
            Rebuild();
        };

        Controls.Add(_grid);
        Controls.Add(_actions);
        Controls.Add(_heading);

        AddAction(_revert);
        Edits.Changed += OnEditsChanged;
        _grid.PropertyValueChanged += (_, _) => _loadedChanged?.Invoke();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Theme.ApplyLightTitleBar(this);
    }

    /// <summary>
    /// Closing hides rather than disposes, so the next click brings the same window back where it
    /// was left. It only really dies with the window that owns it.
    /// </summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        Edits.Changed -= OnEditsChanged;
        base.OnFormClosing(e);
    }

    /// <summary>Adds a button to the action bar. Buttons appear in the order they are added.</summary>
    protected void AddAction(Button button) => _actions.Controls.Add(button);

    /// <summary>Asks the owning window to open another inspector on a related object.</summary>
    protected void GoTo(object target) => Navigate?.Invoke(target);

    // --- Selection ----------------------------------------------------------------------------

    /// <summary>Points this inspector at one or more objects. An empty list clears it.</summary>
    public void Inspect(IReadOnlyList<object> targets)
    {
        Loaded = null;
        _loadedChanged = null;
        Selection = [.. targets];
        Rebuild();
    }

    /// <summary>
    /// Points this inspector at file-backed entries of an opened mod. The same window and the
    /// same buttons as for a generated world; only what backs the grid changes.
    /// </summary>
    internal void InspectLoaded(IReadOnlyList<WorldEntry> entries, LoadedWorldView world, Action changed)
    {
        Loaded = world;
        _loadedChanged = changed;
        Selection = [.. entries];
        Rebuild();
    }

    /// <summary>After an edit elsewhere in the opened mod: values and the caption follow the files.</summary>
    internal void RefreshLoaded()
    {
        if (Loaded is null) return;
        _grid.Refresh();
        Text = Selection.Count == 1 && Selection[0] is WorldEntry entry ? entry.Name : $"{Selection.Count} selected";
        Refreshed();
    }

    /// <summary>Commits a value still being typed in the grid, before a save reads the files.</summary>
    internal void CommitGrid() => _revert.Focus();

    /// <summary>For a subclass action that edited the opened mod outside the grid.</summary>
    protected void LoadedChanged()
    {
        _grid.Refresh();
        _loadedChanged?.Invoke();
    }

    /// <summary>The editable faces of file-backed entries. Generic unless a subclass knows better.</summary>
    protected virtual IEnumerable<object> WrapLoaded(IReadOnlyList<WorldEntry> entries)
        => entries.Select(e => new LoadedEntryProperties(e));

    /// <summary>The single selected entry of an opened mod, when there is exactly one.</summary>
    protected WorldEntry? LoadedOne => Loaded is not null && Selection.Count == 1 ? Selection[0] as WorldEntry : null;

    protected void Rebuild()
    {
        if (Selection.Count == 0)
        {
            _heading.Text = "Nothing selected";
            _grid.SelectedObjects = [];
            foreach (Control c in _actions.Controls) c.Enabled = false;
            return;
        }

        if (Loaded is not null)
        {
            var entries = Selection.OfType<WorldEntry>().ToList();
            _heading.Text = entries.Count == 1 ? $"{entries[0].Kind} — {entries[0].Key}" : $"{entries.Count} selected";
            Text = entries.Count == 1 ? entries[0].Name : $"{entries.Count} selected";
            _grid.SelectedObjects = [.. WrapLoaded(entries)];
            foreach (Control c in _actions.Controls) c.Enabled = true;
            Refreshed();
            return;
        }

        _heading.Text = Describe(Selection);
        Text = Selection.Count == 1 ? Title(Selection[0]) : $"{Selection.Count} selected";

        // PropertyGrid merges the wrappers itself, showing a shared value where the selection
        // agrees and a blank where it does not, and writing to every one of them.
        _grid.SelectedObjects = [.. Wrap(Selection)];

        foreach (Control c in _actions.Controls) c.Enabled = Edits.IsLoaded;
        _revert.Enabled = Edits.IsLoaded && Selection.Any(Edits.CanRevert);

        Refreshed();
    }

    /// <summary>The editable faces of a selection, one per object, all of the same type.</summary>
    protected abstract IEnumerable<object> Wrap(IReadOnlyList<object> targets);

    /// <summary>The line above the grid.</summary>
    protected abstract string Describe(IReadOnlyList<object> targets);

    /// <summary>The window's caption for a single selection.</summary>
    protected abstract string Title(object target);

    /// <summary>Hook for subclasses to enable their own buttons against the current selection.</summary>
    protected virtual void Refreshed() { }

    private void OnEditsChanged(WorldAspect touched)
    {
        if (Loaded is not null) return;
        if (!Edits.IsLoaded)
        {
            Inspect([]);
            Hide();
            return;
        }

        if (Selection.Count == 1) Text = Title(Selection[0]);
        _revert.Enabled = Selection.Any(Edits.CanRevert);

        // Deferred: an edit made *in* this grid raises Changed from inside the grid's own value
        // commit, and refreshing it there re-enters a control that has not finished writing the
        // value yet. Posting lets the commit unwind before the redraw.
        if (IsHandleCreated && !IsDisposed) BeginInvoke(_grid.Refresh);
    }
}
