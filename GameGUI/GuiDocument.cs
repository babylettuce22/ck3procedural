using System.Text;
using Ck3MapGen.Io;

namespace Ck3MapGen.GameGui;

/// <summary>
/// A whole <c>.gui</c> file, opened from vanilla, patched, and shipped — or not shipped, with a
/// reason.
///
/// This is <see cref="VanillaPatch"/>'s policy over a parsed tree instead of over a string. The
/// policy is the same and is the point: a missing source file is a skip with one wording, an anchor
/// that no longer resolves is a *named* failure rather than a silently absent edit, and if any
/// anchor missed then nothing ships at all. A full-file override that is missing the one thing it
/// was written to add is worse than no override — it replaces vanilla with vanilla, the guard is
/// gone, and neither CK3 nor ck3-tiger says a word.
///
/// What the tree adds over the string is that anchors stop being substrings. "The widget called
/// holder_info" is a claim that survives Paradox reformatting the file, moving it, or adding
/// another widget above it; <c>IndexOf("name = \"holder_info\"")</c> is a claim about byte offsets
/// that happened to hold. It also removes the counting: an insert takes its indentation from the
/// node it stands beside, rather than from a caller measuring the anchor line's prefix and
/// prefixing every line of the block by hand.
///
/// One file is opened once and may be patched by any number of callers before it ships. The writer
/// this replaces read vanilla and wrote the mod once per *target*, so two features touching one
/// file could not both be expressed — the second overwrote the first — and the second feature to
/// want window_character.gui had to be folded into the first by a special case.
///
/// <see cref="Create"/> is the same machinery pointed at a file this project authors outright,
/// which is what a window of its own needs — see <c>gui/scripted_widgets</c> for how the engine is
/// told about one.
/// </summary>
public sealed class GuiDocument
{
    /// <summary>Top-level items, in file order.</summary>
    public List<GuiNode> Roots { get; }

    /// <summary>Whatever follows the last item: the file's final newline, a parting comment.</summary>
    private readonly string _epilogue;

    private readonly List<string> _landed = [];
    private readonly List<string> _missed = [];

    private string _label = "gui";
    private string _relativePath = "";

    /// <summary>Written from scratch rather than read out of the game folder.</summary>
    private bool _authored;

    public GuiDocument(List<GuiNode> roots, string epilogue)
    {
        Roots = roots;
        _epilogue = epilogue;
    }

    /// <summary>Whether every anchor asked for so far resolved.</summary>
    public bool Intact => _missed.Count == 0;

    /// <summary>The path this document will be written to, relative to the mod root.</summary>
    public string RelativePath => _relativePath;

    // -----------------------------------------------------------------------------------------
    // Opening and shipping
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Reads and parses a vanilla file, or returns null having said why.
    ///
    /// The relative path is used twice — to find the source under the game folder and to place the
    /// override at the matching path under the mod — because a CK3 override only works from the
    /// same path, and passing it once removes the chance of the two drifting apart.
    /// </summary>
    public static GuiDocument? Open(string gameDir, string label, params string[] relativePathParts)
    {
        string relativePath = Path.Combine(relativePathParts);
        string source = Path.Combine(gameDir, relativePath);

        if (!File.Exists(source))
        {
            Console.WriteLine($"  {label}: SKIPPED ({relativePath} not found in game folder)");
            return null;
        }

        GuiDocument document;

        try
        {
            document = GuiParser.Parse(File.ReadAllText(source), label);
        }
        catch (FormatException e)
        {
            // A vanilla file this cannot read is a change in the format itself, not a moved anchor.
            // Skipping is the same answer either way, but the wording should not blame an anchor.
            Console.WriteLine($"  {label}: SKIPPED {relativePath} — could not parse it ({e.Message}). "
                + "Not shipping a partial override.");
            return null;
        }

        document._label = label;
        document._relativePath = relativePath;
        return document;
    }

    /// <summary>Parses text directly. For tests and for round-trip checks.</summary>
    public static GuiDocument FromText(string text, string label = "gui")
        => GuiParser.Parse(text, label);

    /// <summary>
    /// An empty document bound to a path, for a file this project authors rather than patches.
    ///
    /// The path is required rather than optional because <see cref="Ship"/> needs one, and a
    /// document without it wrote to the mod root. Authoring and patching share everything else —
    /// the same builder, the same printer, the same refusal to ship a file whose anchors did not
    /// resolve — so they are one type with two entry points rather than two types.
    /// </summary>
    public static GuiDocument Create(string label, params string[] relativePathParts)
        => new([], "\n")
        {
            _label = label,
            _relativePath = Path.Combine(relativePathParts),
            _authored = true,
        };

    /// <summary>
    /// Adds a top-level widget to an authored document.
    ///
    /// Everything after the first gets a blank line above it, because top-level items in a .gui file
    /// are separate widgets rather than properties of one.
    /// </summary>
    public GuiDocument Add(GuiNode node)
    {
        node.BlankBefore |= Roots.Count > 0;
        Roots.Add(node);
        return this;
    }

    public string Print()
    {
        var sb = new StringBuilder();

        bool first = true;
        foreach (var node in Roots)
        {
            node.Print(sb, "", first);
            first = false;
        }

        sb.Append(_epilogue);
        return sb.ToString();
    }

    /// <summary>
    /// Writes the override, or explains why it is not writing one. Returns whether it shipped.
    ///
    /// No BOM, which is how <c>.gui</c> overrides have always been shipped here and matches what
    /// the engine reads back.
    /// </summary>
    public bool Ship(string modDir)
    {
        if (_missed.Count > 0)
        {
            Console.WriteLine($"  {_label}: SKIPPED {_relativePath} — no anchor for "
                + $"{string.Join(", ", _missed)}. Vanilla has changed shape; "
                + "not shipping a partial override.");
            return false;
        }

        string destination = Path.Combine(modDir, _relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        ParadoxText.WriteNoBom(destination, Print());

        // "wrote" for a file this project authored, "patched" for a vanilla file it edited. The
        // distinction matters to whoever reads the log: one of them is a full override of somebody
        // else's file and the other is not.
        Console.WriteLine(_authored
            ? $"  {_label}: {_relativePath} — wrote {Roots.Count} widget(s)"
            : $"  {_label}: {_relativePath} — patched {string.Join(", ", _landed)}");

        return true;
    }

    // -----------------------------------------------------------------------------------------
    // Finding
    // -----------------------------------------------------------------------------------------

    /// <summary>Every node in the file, parents before children — document order.</summary>
    public IEnumerable<GuiNode> Nodes()
    {
        foreach (var root in Roots)
        {
            yield return root;
            foreach (var node in root.Descendants()) yield return node;
        }
    }

    /// <summary>
    /// The first node matching <paramref name="match"/>, as a handle that remembers whether it
    /// found anything.
    ///
    /// <paramref name="what"/> is what an operator reads when this fails, so it names the place
    /// rather than the edit — "holder", "stats", "court chaplain seat".
    ///
    /// A miss is recorded here, at the query, rather than at the edit. That way a caller can chain
    /// edits off the handle without a null check at every step and still have the file refuse to
    /// ship.
    /// </summary>
    public GuiRef Find(string what, Func<GuiNode, bool> match)
    {
        var node = Nodes().FirstOrDefault(match);

        if (node is null) _missed.Add(what);
        else _landed.Add(what);

        return new GuiRef(this, what, node);
    }

    /// <summary>
    /// The only node matching <paramref name="match"/> — a miss if there are none, and equally a
    /// miss if there are two.
    ///
    /// For anchors whose whole claim is uniqueness. The date tab's year is identified by the text
    /// it displays rather than by its name, and that is only a safe anchor while nothing else in
    /// the file displays the same thing; a second match means vanilla grew one, and patching the
    /// first would be a coin toss.
    /// </summary>
    public GuiRef Unique(string what, Func<GuiNode, bool> match)
    {
        var found = Nodes().Where(match).Take(2).ToList();

        if (found.Count == 1)
        {
            _landed.Add(what);
            return new GuiRef(this, what, found[0]);
        }

        _missed.Add($"{what} ({(found.Count == 0 ? "not found" : "no longer unique")})");
        return new GuiRef(this, what, null);
    }

    /// <summary>A handle on a node already in hand, so it can carry the same edits and accounting.</summary>
    public GuiRef At(string what, GuiNode? node)
    {
        if (node is null) _missed.Add($"{what} (not found)");
        else _landed.Add(what);

        return new GuiRef(this, what, node);
    }

    /// <summary>The container that gives itself <c>name = "…"</c>.</summary>
    public GuiRef Widget(string what, string name)
        => Find(what, n => n.IsBlock && n.Name == name);

    /// <summary>The <c>name = "…"</c> leaf itself, for edits that key off where the name sits.</summary>
    public GuiRef NameField(string what, string name)
        => Find(what, n => !n.IsBlock && n.Key == "name" && GuiNode.Unquote(n.Value ?? "") == name);

    /// <summary>The first block of a given type — <c>button_sidepanel_right = {</c>.</summary>
    public GuiRef Block(string what, string key)
        => Find(what, n => n.IsBlock && n.Key == key);

    /// <summary>The first leaf <c>key = value</c>, matched on both halves.</summary>
    public GuiRef Leaf(string what, string key, string value)
        => Find(what, n => !n.IsBlock && n.Key == key && n.Value == value);

    /// <summary>
    /// The first block whose opening line carries <paramref name="text"/>, comment included.
    ///
    /// The anchor of last resort, and named so it reads as one at the call site. See
    /// <see cref="GuiNode.HeadLine"/> for the one file that needs it.
    /// </summary>
    public GuiRef BlockWithComment(string what, string headLine)
        => Find(what, n => n.IsBlock && n.HeadLine.Contains(headLine, StringComparison.Ordinal));

    /// <summary>
    /// An inline block written exactly as <paramref name="tokens"/> — <c>position = { 0 0 }</c>.
    /// </summary>
    public GuiRef Inline(string what, string key, params string[] tokens)
        => Find(what, n => n.IsBlock
            && n.Key == key
            && n.Children.Count == 1
            && n.Children[0].Head.SequenceEqual(tokens));

    /// <summary>
    /// Records a failure that is not a missing anchor — the node was found but could not carry the
    /// edit — so the file refuses to ship.
    ///
    /// The anchor is taken back out of the landed list, because it did not land: an operator
    /// reading the skip message should not see the same widget named on both sides of it.
    /// </summary>
    internal void Miss(string what, string why)
    {
        _landed.Remove(what);
        _missed.Add($"{what} ({why})");
    }
}

/// <summary>
/// A handle on one found node, and the edits that can be made through it.
///
/// Every method is a no-op when the node was not found, because the miss is already recorded and
/// the file is already going to be skipped. That is what lets a caller read as a list of intentions
/// rather than as a list of conditionals — which is most of what the patching code in this project
/// was.
/// </summary>
public sealed class GuiRef(GuiDocument document, string what, GuiNode? node)
{
    public GuiNode? Node { get; } = node;

    public bool Found => Node is not null;

    /// <summary>
    /// Narrows an existing <c>visible</c> with another condition, keeping vanilla's own.
    ///
    /// The condition is joined with <c>And</c> rather than replacing what is there, because what is
    /// there is vanilla's reason for hiding the widget and stays true. A widget with no
    /// <c>visible</c> of its own is a miss, not a place to add one — see <see cref="SetVisible"/>
    /// for that case, and note that the two are different edits with different risks: adding a
    /// <c>visible</c> where vanilla had none makes this project responsible for a widget's whole
    /// visibility, and narrowing one leaves vanilla in charge of everything but the new clause.
    /// </summary>
    public GuiRef AndVisible(GuiExpr condition)
    {
        if (Node is null) return this;

        var visible = Node.Children.FirstOrDefault(c => !c.IsBlock && c.Key == "visible");

        if (visible?.Value is null)
        {
            document.Miss(what, "no `visible` of its own to narrow");
            return this;
        }

        var existing = GuiExpr.Raw(GuiNode.Unquote(visible.Value));
        visible.SetValue(GuiExpr.And(existing, condition).Quoted);
        return this;
    }

    /// <summary>
    /// Gives a widget a <c>visible</c> it did not have, written immediately after this node.
    ///
    /// Anchored on the node rather than appended to the parent so the property lands where a reader
    /// would put it — beside the widget's name — instead of at the bottom of a two-hundred-line
    /// container.
    /// </summary>
    public GuiRef InsertVisible(GuiExpr condition)
    {
        Node?.InsertAfter(GuiNode.Leaf("visible", condition.Quoted));
        return this;
    }

    /// <summary>Sets or replaces a property on the found block.</summary>
    public GuiRef Set(string key, string value)
    {
        Node?.Set(key, value);
        return this;
    }

    public GuiRef Set(string key, GuiExpr value) => Set(key, value.Quoted);

    /// <summary>
    /// Splices a whole widget in as the sibling immediately before this node, with a blank line
    /// between the two.
    ///
    /// The trivia swap is what keeps the file looking hand-written: the spliced block inherits
    /// whatever separated this node from the one above it — a blank line, a comment — and this node
    /// gets a plain blank line in front of it. Insert twice at one anchor and the blocks stack in
    /// call order above it, which is how a single anchor carries four separate additions in
    /// <c>window_title.gui</c>.
    /// </summary>
    public GuiRef InsertBefore(params GuiNode[] blocks)
    {
        if (Node is null) return this;

        foreach (var block in blocks)
        {
            string lead = Node.LeadingTrivia ?? "\n";

            block.LeadingTrivia = lead;
            Node.LeadingTrivia = "\n\n" + IndentIn(lead);
            Node.InsertBefore(block);
        }

        return this;
    }

    /// <summary>
    /// Splices a widget in as the sibling immediately after this node, with a blank line between.
    ///
    /// Written for the case where the anchor is the thing being extended rather than displaced —
    /// vanilla's year text on the bookmark tab, which keeps authoring the year while the line below
    /// it is this project's.
    /// </summary>
    public GuiRef InsertAfter(GuiNode block)
    {
        if (Node is null) return this;

        block.LeadingTrivia = "\n\n" + IndentIn(Node.LeadingTrivia ?? "\n");
        Node.InsertAfter(block);

        return this;
    }

    /// <summary>The indentation a run of trivia ends on.</summary>
    private static string IndentIn(string trivia)
    {
        int newline = trivia.LastIndexOf('\n');
        return newline < 0 ? "" : trivia[(newline + 1)..];
    }

    /// <summary>Adds a child at the top of the found block.</summary>
    public GuiRef InsertFirst(GuiNode child)
    {
        Node?.InsertFirst(child);
        return this;
    }

    /// <summary>Adds a child at the bottom of the found block.</summary>
    public GuiRef Append(GuiNode child)
    {
        Node?.Add(child);
        return this;
    }
}
