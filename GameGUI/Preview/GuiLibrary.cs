namespace Ck3MapGen.GameGui.Preview;

/// <summary>
/// Every widget type and template CK3 knows about, indexed by name, and the flattening that turns
/// one written widget into the full set of properties the engine would actually give it.
///
/// This is the half of a preview that cannot be guessed at. A widget as written in a <c>.gui</c>
/// file is almost never the widget that gets drawn: <c>button_standard</c> carries nothing of its
/// own here, and everything about how it looks comes from a chain of <c>type</c> declarations in
/// <c>gui/shared/buttons.gui</c> ending at an engine primitive, plus whatever <c>template</c>s it
/// pulls in through <c>using</c>. Vanilla declares 2,177 such names. Without resolving them a
/// preview shows empty rectangles where the game shows a window.
///
/// Three mechanisms, all of which have to work together:
///
/// * <c>type X = base { … }</c> — X is base's contents followed by its own. Chains bottom out at a
///   self-referential declaration (<c>type widget = widget</c> in preload/defaults.gui), which is
///   how the files spell "this one is built into the engine".
/// * <c>using = T</c> — splice template T's contents in at that point. Order matters, because a
///   later property of the same name wins.
/// * <c>block "n" { … }</c> and <c>blockoverride "n" { … }</c> — a hole with default contents, and
///   the filling for it supplied at the use site. Overrides travel *down* from the instance into
///   whatever the type chain expands to, which is why they are threaded through the recursion
///   rather than applied afterwards.
///
/// Deliberately not a validator, exactly like <see cref="GuiParser"/>. A name it cannot resolve is
/// recorded in <see cref="Unresolved"/> and the widget is flattened as far as it goes, because a
/// preview that renders most of a window and says what it missed is worth more than one that
/// refuses.
/// </summary>
public sealed class GuiLibrary
{
    private readonly Dictionary<string, TypeDecl> _types = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GuiNode> _templates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GuiNode> _instances = new(StringComparer.OrdinalIgnoreCase);

    private readonly SortedSet<string> _unresolved = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// How many times to draw the <c>item</c> of a <c>datamodel</c> container.
    ///
    /// A list widget is written once and drawn once per entry, so a preview that draws it once
    /// shows a list of one — which is the shape least likely to reveal the bug you are looking for.
    /// Row height, spacing, whether the scrollbox actually scrolls and whether the window is tall
    /// enough for a realistic number of entries are all invisible at one row.
    ///
    /// The entries are identical, because the preview has no game state to draw them from. What it
    /// is showing is the LAYOUT of a list, not its contents.
    /// </summary>
    public int ItemRows { get; set; } = 1;

    private sealed record TypeDecl(string Name, string Base, GuiNode Body, string Source);

    /// <summary>Names referenced but never declared. Engine primitives land here too.</summary>
    public IReadOnlyCollection<string> Unresolved => _unresolved;

    public int TypeCount => _types.Count;

    public int TemplateCount => _templates.Count;

    /// <summary>Top-level widget instances, by name — what a preview can be pointed at.</summary>
    public IEnumerable<string> Instances => _instances.Keys.Order();

    /// <summary>
    /// The datafunction calls vanilla makes, learned from the FIRST root only.
    ///
    /// First root only because the question it answers is "does vanilla ever say this" — letting the
    /// mod's own files teach it would make every generated window vouch for itself.
    /// </summary>
    public GuiVocabulary Vocabulary { get; } = new();

    /// <summary>
    /// Indexes every <c>.gui</c> file under each root, in order, later roots winning.
    ///
    /// That ordering is CK3's own: a mod's <c>gui/</c> replaces the game's file by file, so pass the
    /// game folder first and the mod second and a redeclared type resolves the way it will in game.
    /// </summary>
    public static GuiLibrary Load(params string[] guiRoots)
    {
        var library = new GuiLibrary();

        for (int i = 0; i < guiRoots.Length; i++)
        {
            string root = guiRoots[i];
            if (!Directory.Exists(root)) continue;

            foreach (string file in Directory.GetFiles(root, "*.gui", SearchOption.AllDirectories).Order())
            {
                GuiDocument document;

                try
                {
                    document = GuiParser.Parse(File.ReadAllText(file), Path.GetFileName(file));
                }
                catch (FormatException)
                {
                    continue;
                }

                if (i == 0) library.Vocabulary.Learn(document.Roots);

                library.Index(document.Roots, Path.GetFileName(file));
            }
        }

        return library;
    }

    /// <summary>Indexes one already-parsed document — for previewing a file before it is written.</summary>
    public void Index(GuiDocument document, string source) => Index(document.Roots, source);

    private void Index(IEnumerable<GuiNode> nodes, string source)
    {
        foreach (var node in nodes)
        {
            switch (node.Key)
            {
                // `types Group { … }` is a namespace for declarations and nothing else — the group
                // name is never referenced, so its contents are indexed flat.
                case "types" when node.IsBlock:
                    Index(node.Children, source);
                    break;

                case "type" when node.IsBlock && node.Head.Count >= 4:
                    _types[node.Head[1]] = new TypeDecl(node.Head[1], node.Head[3], node, source);

                    // Also walked for named widgets inside it. Most of a .gui file's real content
                    // lives inside type declarations — frontend_bookmarks.gui is nothing but types —
                    // so skipping them leaves the majority of named widgets unpreviewable.
                    IndexNamed(node);
                    break;

                // local_template is file-scoped in the engine. Indexed globally here, which is a
                // deliberate simplification: name collisions across files would be a vanilla bug,
                // and the preview would rather resolve one than none.
                case "template" or "local_template" when node.IsBlock && node.Head.Count >= 2:
                    _templates[GuiNode.Unquote(node.Head[1])] = node;
                    break;

                default:
                    IndexNamed(node);
                    break;
            }
        }
    }

    /// <summary>
    /// Records every named widget in a tree, however deep, so any one of them can be previewed on
    /// its own.
    ///
    /// Nested and not just top level, because the widgets worth looking at are nested: everything
    /// this project splices into a vanilla window is a named block a dozen levels down, and a
    /// preview that could only open whole files could not show any of them.
    ///
    /// First occurrence wins, which is the same rule the patching code follows for the same reason
    /// — <c>tutorial_court_chaplain</c> appears twice in window_council.gui and the first is the one
    /// that matters. A widget previewed alone is measured against the viewport rather than against
    /// the parent it normally sits in, so its size can differ from the same widget seen in its file.
    /// </summary>
    private void IndexNamed(GuiNode node)
    {
        if (!node.IsBlock) return;

        if (node.Name is { } name && !_instances.ContainsKey(name)) _instances[name] = node;

        foreach (var child in node.Children) IndexNamed(child);
    }

    // -------------------------------------------------------------------------------------------
    // Flattening
    // -------------------------------------------------------------------------------------------

    /// <summary>A widget instance by name, from the indexed files.</summary>
    public GuiNode? Instance(string name) => _instances.GetValueOrDefault(name);

    /// <summary>
    /// Expands one written widget into everything the engine would give it.
    ///
    /// The result is a plain tree of <see cref="ResolvedWidget"/>: properties already merged in
    /// declaration order with later winning, children already spliced, blocks already filled.
    /// Nothing in it refers back to a type or a template, which is what lets the layout pass be
    /// about geometry alone.
    /// </summary>
    public ResolvedWidget Resolve(GuiNode node, int depth = 0)
        => Resolve(node, inherited: null, depth);

    /// <summary>
    /// <paramref name="inherited"/> carries the block fillings supplied further up.
    ///
    /// They have to travel down rather than being applied where they are written, because the hole
    /// and its filling are almost never in the same widget: <c>blockoverride "header_text"</c> is
    /// written on a <c>header_standard</c>, and the <c>block "header_text"</c> it fills is inside a
    /// text widget several links down that template's expansion. Collecting them per widget and not
    /// passing them on is why the title bar came out blank.
    /// </summary>
    private ResolvedWidget Resolve(GuiNode node, Dictionary<string, GuiNode>? inherited, int depth)
    {
        var widget = new ResolvedWidget(node.Key);

        if (depth > 40)
        {
            widget.Notes.Add("stopped: type chain deeper than 40");
            return widget;
        }

        // The nearer use site wins: a widget's own override replaces one inherited for the same
        // hole, which is what lets a template be reused with different fillings at each depth.
        var overrides = inherited is null
            ? new Dictionary<string, GuiNode>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, GuiNode>(inherited, StringComparer.OrdinalIgnoreCase);

        CollectOverrides(node, overrides);

        var chain = new List<string>();

        // The type chain first, base outwards, and then the instance's own contents on top of all
        // of it — which is the order that makes last-write-wins mean what it should.
        ExpandType(node.Key, overrides, widget, chain, depth);
        Apply(node, overrides, widget, depth);

        widget.TypeChain = chain;
        PushButtonText(widget);
        return widget;
    }

    /// <summary>
    /// Hands a button's <c>text</c> down to the label inside its <c>buttonText</c> block.
    ///
    /// The engine does this and the files rely on it: every button in the game is written
    /// <c>button_standard = { text = "SOME_KEY" }</c>, and the widget that actually draws the string
    /// is a <c>text_single</c> several links down the type's expansion, inside a block called
    /// <c>buttonText</c> that declares no text of its own. Nothing in the source says the two are
    /// connected — the button type's declaration and the instance that fills it never mention each
    /// other — so a resolver that only folds properties into the widget they were written on leaves
    /// the label empty.
    ///
    /// Which is exactly what it did: every button in every previewed window drew as bare furniture,
    /// and the whole tab strip of the debug panel came out as three blank slabs. That reads as a
    /// layout problem and is not one, which is the kind of wrongness a preview must not have.
    ///
    /// Only fills a label that has none of its own, so a <c>blockoverride "buttonText"</c> supplying
    /// real text still wins.
    /// </summary>
    private static void PushButtonText(ResolvedWidget widget)
    {
        string? text = widget.Prop("raw_text") ?? widget.Prop("text");
        if (text is null) return;

        string key = widget.Props.ContainsKey("raw_text") ? "raw_text" : "text";

        foreach (var child in widget.Children)
        {
            if (!child.WrittenType.Equals("buttonText", StringComparison.OrdinalIgnoreCase))
                continue;

            // Searched from the block's CHILDREN, not from the block. "buttonText" itself matches
            // any test for a text widget by name — it is the one container in the game whose name
            // contains the word — and filling it instead of the label inside it sets the property on
            // something that never draws a string.
            foreach (var candidate in child.Children)
                if (FirstEmptyLabel(candidate) is { } label)
                {
                    label.Set(key, text);
                    return;
                }

            return;
        }
    }

    /// <summary>The first text widget at or under here that has nothing to say, or null.</summary>
    private static ResolvedWidget? FirstEmptyLabel(ResolvedWidget widget)
    {
        // The primitive, not the written type: what makes a widget draw a string is what it bottoms
        // out at, and a type called `header_text` that resolves to a container draws nothing.
        bool textual = (widget.Primitive ?? widget.WrittenType)
            .Contains("text", StringComparison.OrdinalIgnoreCase);

        if (textual && widget.Prop("text") is null && widget.Prop("raw_text") is null)
            return widget;

        foreach (var child in widget.Children)
            if (FirstEmptyLabel(child) is { } found) return found;

        return null;
    }

    /// <summary>Walks one link of the type chain, base first, then that type's own contents.</summary>
    private void ExpandType(string typeName, Dictionary<string, GuiNode> overrides,
        ResolvedWidget widget, List<string> chain, int depth)
    {
        if (!_types.TryGetValue(typeName, out var declaration))
        {
            // Not declared anywhere: either an engine primitive with no stub, or a genuine typo.
            // The preview cannot tell the two apart, so it records the name and carries on.
            widget.Primitive ??= typeName;
            _unresolved.Add(typeName);
            chain.Add(typeName + " (undeclared)");
            return;
        }

        chain.Add(typeName);

        // A self-referential declaration is the files' way of naming an engine primitive. It is the
        // base case, and following it would be an infinite loop.
        if (!declaration.Base.Equals(typeName, StringComparison.OrdinalIgnoreCase) && chain.Count < 40)
            ExpandType(declaration.Base, overrides, widget, chain, depth);
        else
            widget.Primitive ??= declaration.Base;

        Apply(declaration.Body, overrides, widget, depth);
    }

    /// <summary>Folds one block's children into the widget: properties, usings, blocks, children.</summary>
    private void Apply(GuiNode body, Dictionary<string, GuiNode> overrides, ResolvedWidget widget,
        int depth)
    {
        foreach (var child in body.Children)
        {
            if (!child.IsBlock)
            {
                if (child.Key == "using" && child.Value is { } template)
                {
                    if (_templates.TryGetValue(template, out var found)) Apply(found, overrides, widget, depth);
                    else _unresolved.Add(template);

                    continue;
                }

                if (child.Value is not null) widget.Set(child.Key, child.Value);
                continue;
            }

            switch (child.Key)
            {
                // Already gathered; they are instructions about holes, not content in themselves.
                case "blockoverride":
                    break;

                // A hole. The override wins if there is one, otherwise the block's own contents are
                // the default — which is how a vanilla template renders when nothing fills it.
                case "block":
                {
                    string name = child.Head.Count >= 2 ? GuiNode.Unquote(child.Head[1]) : "";

                    Apply(overrides.TryGetValue(name, out var filling) ? filling : child,
                        overrides, widget, depth);
                    break;
                }

                // A property whose value is a list of numbers — `size = { 100 100 }`.
                //
                // Told apart from a child widget by its CONTENTS, not by whether it fits on one
                // line. `expand = {}` and `gen_artifact_index_window = {}` are widgets written
                // closed up, and keying off the one-line-ness of the source made every one of them
                // vanish into the property bag — a window that drew nothing, with nothing to say
                // about why.
                case var _ when IsValueList(child):
                    widget.SetInline(child.Key, child.Children.SelectMany(c => c.Head).ToArray());
                    break;

                // Animation and visibility states. Recorded rather than expanded: a preview draws
                // one moment, and a state is about change over time.
                case "state":
                    widget.States.Add(child.Field("name") is { } n ? GuiNode.Unquote(n) : "state");
                    break;

                default:
                {
                    // `item` is the per-entry template of whatever datamodel the parent names.
                    int copies = child.Key.Equals("item", StringComparison.OrdinalIgnoreCase)
                        ? Math.Max(1, ItemRows)
                        : 1;

                    for (int i = 0; i < copies; i++)
                        widget.Children.Add(Resolve(child, overrides, depth + 1));

                    break;
                }
            }
        }
    }

    /// <summary>
    /// Whether a block is a property's value rather than a child widget.
    ///
    /// True only when it holds nothing but bare tokens — the shape of <c>{ 100 100 }</c> and
    /// <c>{ 1 1 1 1 }</c>. An empty block holds no tokens and so is a widget, which is the case
    /// that matters: it is how every instantiation in a scripted_widgets file is written.
    /// </summary>
    private static bool IsValueList(GuiNode block)
        => block.Children.Count > 0
           && block.Children.All(c => !c.IsBlock && c.Value is null);

    private static void CollectOverrides(GuiNode node, Dictionary<string, GuiNode> into)
    {
        foreach (var child in node.Children)
        {
            if (child is { IsBlock: true, Key: "blockoverride" } && child.Head.Count >= 2)
                into[GuiNode.Unquote(child.Head[^1])] = child;
        }
    }
}
