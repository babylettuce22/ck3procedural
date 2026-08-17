using Ck3MapGen.Core;
using Ck3MapGen.Emit;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Gui;

/// <summary>
/// Browses the de jure hierarchy of a written mod, and hands whatever is picked to the inspector.
///
/// Navigation only. Every edit lives in <see cref="TitleInspector"/>, whether it was reached from
/// here or from a click on the map, so there is one editor rather than two that have to agree.
/// What this adds over the map is the two things a map cannot do: find a title by name anywhere in
/// the world, and select several at once.
///
/// Only useful after a write. A title has no name until one is generated, and that happens inside
/// <see cref="ContentWriter"/> — after a mere preview every title here would be blank.
///
/// The tree fills in as it is opened. Baronies are one per land province, thousands at vanilla
/// size, and building that many <see cref="TreeNode"/>s up front is several seconds of frozen
/// window for a tier most sessions never expand.
/// </summary>
public sealed class TitleEditor : UserControl
{
    private readonly WorldEdits _edits;

    private readonly TreeView _tree = new()
    {
        Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.None,
        BackColor = Theme.Surface,
        ForeColor = Theme.Text,
        Font = Theme.Ui,
        HideSelection = false,
        FullRowSelect = true,

        // Ctrl and shift click to build a selection, which is what makes "recolour every county in
        // this duchy" one action instead of six.
        CheckBoxes = false,
    };

    private readonly TextBox _search = new()
    {
        Width = 160,
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = Theme.SurfaceHigh,
        ForeColor = Theme.Text,
        Font = Theme.Ui,
        Margin = new Padding(6, 5, 3, 3),
    };

    private readonly Label _count = new()
    {
        AutoSize = true,
        ForeColor = Theme.TextDim,
        Font = Theme.Ui,
        Margin = new Padding(10, 8, 0, 0),
    };

    private readonly Label _empty = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = Theme.TextDim,
        Font = Theme.Ui,
        Text = "Write a mod to name its titles, then edit them here or by clicking the map.",
    };

    private readonly Dictionary<Title, TreeNode> _nodes = [];
    private List<Title> _flat = [];

    /// <summary>
    /// Titles picked here, in tree order. Multi-select is kept by hand: TreeView has no built-in
    /// multi-selection, so ctrl and shift clicks are folded into this list and painted onto the
    /// nodes as a highlight.
    /// </summary>
    private readonly List<Title> _selected = [];

    private Title? _anchor;

    /// <summary>Raised when the picked titles change, for whoever is showing the inspector.</summary>
    public event Action<IReadOnlyList<Title>>? SelectionChanged;

    private const string EditedMark = "• ";

    public TitleEditor(WorldEdits edits)
    {
        _edits = edits;
        BackColor = Theme.Background;

        _tree.BeforeExpand += (_, e) => Populate(e.Node);
        _tree.NodeMouseClick += (_, e) => Pick(e.Node, ModifierKeys);

        _search.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            SearchNext();
        };

        Controls.Add(BuildTree());
        Controls.Add(BuildHeader());

        _edits.Changed += OnEditsChanged;
        Sync();
    }

    // --- Layout -------------------------------------------------------------------------------
    //
    // Fill first, then edge-docked, for the reason MainForm documents: WinForms lays docking out in
    // reverse z-order and a fill added last claims the whole client area.

    private Control BuildTree()
    {
        var host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface };
        host.Controls.Add(_tree);
        host.Controls.Add(_empty);
        return host;
    }

    private Control BuildHeader()
    {
        var header = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 32,
            Padding = new Padding(4, 3, 4, 0),
            BackColor = Theme.Surface,
        };

        var find = Theme.MakeButton("Find next", 84);
        find.Click += (_, _) => SearchNext();

        header.Controls.Add(Caption("Search"));
        header.Controls.Add(_search);
        header.Controls.Add(find);
        header.Controls.Add(_count);
        return header;
    }

    private static Label Caption(string text)
        => new()
        {
            Text = text,
            AutoSize = true,
            ForeColor = Theme.TextDim,
            Font = Theme.Ui,
            Margin = new Padding(4, 9, 2, 0),
        };

    // --- Loading ------------------------------------------------------------------------------

    /// <summary>Rebuilds the tree for whatever <see cref="WorldEdits"/> is now pointing at.</summary>
    private void OnEditsChanged(WorldAspect touched)
    {
        if (!_edits.IsLoaded)
        {
            if (_nodes.Count > 0) Clear();
            Sync();
            return;
        }

        // Already built for this run: an edit changed, so only the labels and the count can differ.
        if (_nodes.Count > 0)
        {
            foreach (var (title, node) in _nodes) node.Text = Label(title);
            Sync();
            return;
        }

        Build(_edits.Target!.Value.Result.Titles);
    }

    private void Build(List<Title> roots)
    {
        _nodes.Clear();
        _selected.Clear();
        _anchor = null;
        _flat = [.. Titles.Flatten(roots)];

        _tree.BeginUpdate();
        _tree.Nodes.Clear();
        foreach (var empire in roots) _tree.Nodes.Add(MakeNode(empire));
        _tree.EndUpdate();

        _empty.Visible = false;
        _tree.Visible = true;

        Sync();
        SelectionChanged?.Invoke(_selected);
    }

    private void Clear()
    {
        _nodes.Clear();
        _selected.Clear();
        _flat = [];
        _anchor = null;

        _tree.Nodes.Clear();
        _tree.Visible = false;
        _empty.Visible = true;

        SelectionChanged?.Invoke(_selected);
    }

    // --- The tree -----------------------------------------------------------------------------

    private TreeNode MakeNode(Title title)
    {
        var node = new TreeNode(Label(title)) { Tag = title };
        _nodes[title] = node;

        // A placeholder, so the node shows an expander without its children being built. Replaced
        // by the real ones the first time it is opened.
        if (title.Children.Count > 0) node.Nodes.Add(new TreeNode("…"));

        return node;
    }

    private void Populate(TreeNode? node)
    {
        if (node?.Tag is not Title title) return;

        // Real children rather than the placeholder: nothing to do.
        if (node.Nodes.Count != 1 || node.Nodes[0].Tag is not null) return;

        _tree.BeginUpdate();
        node.Nodes.Clear();
        foreach (var child in title.Children) node.Nodes.Add(MakeNode(child));
        _tree.EndUpdate();
    }

    private string Label(Title title)
    {
        string mark = _edits.WasEdited(title) ? EditedMark : "";

        return title.Tier == "b"
            ? $"{mark}{title.Name}   #{title.ProvinceId}"
            : $"{mark}{title.Name}";
    }

    /// <summary>
    /// Brings a title into view, building whatever part of the tree it needs on the way.
    ///
    /// Needed by both search and by a click on the map: the target is usually several tiers down in
    /// a branch that has never been opened, so there is no node to select until one is made.
    /// </summary>
    public void Reveal(Title title)
    {
        if (!_edits.IsLoaded) return;

        var chain = new List<Title>();
        for (var t = title; t is not null; t = t.Parent) chain.Add(t);
        chain.Reverse();

        foreach (var step in chain)
        {
            if (!_nodes.TryGetValue(step, out var node)) return; // Not part of this tree
            Populate(node);
            if (step != title) node.Expand();
        }

        if (!_nodes.TryGetValue(title, out var found)) return;

        _tree.SelectedNode = found;
        found.EnsureVisible();

        SetSelection([title]);
    }

    // --- Selection ----------------------------------------------------------------------------

    private void Pick(TreeNode? node, Keys modifiers)
    {
        if (node?.Tag is not Title title) return;

        if (modifiers.HasFlag(Keys.Control))
        {
            var next = _selected.ToList();
            if (!next.Remove(title)) next.Add(title);
            SetSelection(next);
        }
        else if (modifiers.HasFlag(Keys.Shift) && _anchor is not null)
        {
            SetSelection(Between(_anchor, title));
            return; // Keeps the anchor where the range started
        }
        else
        {
            SetSelection([title]);
        }

        _anchor = title;
    }

    /// <summary>
    /// Every title between two picks, in the order the tree lists them.
    ///
    /// Taken over the flattened hierarchy rather than over visible nodes, so a shift-click spanning
    /// a collapsed branch reaches inside it — which is what makes selecting a duchy's whole county
    /// list one gesture.
    ///
    /// Restricted to the tier that was shift-clicked. Flatten is depth-first, so the raw range
    /// between two counties also contains every barony that happens to lie between them; selecting
    /// counties and silently getting their baronies too would make a bulk recolour do something
    /// quite different from what it looked like.
    /// </summary>
    private List<Title> Between(Title a, Title b)
    {
        int from = _flat.IndexOf(a), to = _flat.IndexOf(b);
        if (from < 0 || to < 0) return [b];
        if (from > to) (from, to) = (to, from);

        return [.. _flat.GetRange(from, to - from + 1).Where(t => t.Tier == b.Tier)];
    }

    private void SetSelection(IReadOnlyList<Title> titles)
    {
        _selected.Clear();
        _selected.AddRange(titles);

        // A set, not the list: this runs over every node in the tree, and a bulk shift-select can
        // hold thousands of titles — a linear Contains against it makes selecting a kingdom's
        // counties quadratic in the size of the map.
        var picked = _selected.ToHashSet();

        // TreeView paints only its own SelectedNode, so a multi-selection has to be shown by hand.
        foreach (var (title, node) in _nodes)
        {
            bool on = picked.Contains(title);
            node.BackColor = on ? Theme.Accent : Color.Empty;
            node.ForeColor = on ? Theme.AccentText : Color.Empty;
        }

        Sync();
        SelectionChanged?.Invoke(_selected);
    }

    // --- Status -------------------------------------------------------------------------------

    private void Sync()
    {
        int edited = _edits.EditedCount;

        _count.Text = !_edits.IsLoaded ? ""
            : edited == 0 && !_edits.HasPending ? "No changes"
            : $"{edited} edited" + (_edits.HasPending ? " — not yet written" : " — written");
    }

    // --- Search -------------------------------------------------------------------------------

    /// <summary>
    /// The next title whose name contains the query, wrapping at the end.
    ///
    /// Runs over the flattened hierarchy rather than the tree, so it finds titles in branches that
    /// have never been opened; <see cref="Reveal"/> then builds the path down to the hit.
    /// </summary>
    private void SearchNext()
    {
        string query = _search.Text.Trim();
        if (query.Length == 0 || _flat.Count == 0) return;

        var current = _selected.Count == 1 ? _selected[0] : null;
        int start = current is null ? 0 : _flat.IndexOf(current) + 1;

        for (int i = 0; i < _flat.Count; i++)
        {
            var candidate = _flat[(start + i) % _flat.Count];
            if (candidate.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                Reveal(candidate);
                return;
            }
        }
    }
}
