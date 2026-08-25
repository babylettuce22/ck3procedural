using System.Text;

namespace Ck3MapGen.GameGui;

/// <summary>
/// One item in a <c>.gui</c> file, and the whole of the format's structure.
///
/// CK3's interface language is a far smaller grammar than the script under <c>common/</c>, and the
/// shape below covers all 373 vanilla files without a special case (measured: 190,731 nodes, every
/// file round-tripping byte-identically). Every item is one of:
///
/// <code>
///     key = value                 Head ["key","="]                    Value "value"
///     key = { … }                 Head ["key","="]                    IsBlock
///     100 100                     Head ["100","100"]                  Value null
///     blockoverride "icon" { }    Head ["blockoverride","\"icon\""]    IsBlock
///     type x = window { }         Head ["type","x","=","window"]       IsBlock
///     types Group { }             Head ["types","Group"]               IsBlock
/// </code>
///
/// <c>=</c> is a head token rather than a flag, because the files disagree about where it goes and
/// both spellings are valid: vanilla writes <c>blockoverride "x"</c> 6,636 times and
/// <c>blockoverride = "x"</c> another 26, and the engine takes either. A token list is the only
/// model that does not have to know which is which.
///
/// ---- Losslessness ----
///
/// A node parsed from a file keeps its own source text and the trivia around it, and reprints that
/// text verbatim unless something changed it. That is what makes patching a vanilla file safe: the
/// override differs from vanilla in exactly the places the patch touched and nowhere else, so a
/// CK3 update to an untouched corner of the file carries straight through.
///
/// <see cref="Dirty"/> is set on a node and on every ancestor when anything is mutated, so a
/// container that gains one child rebuilds its own braces while its untouched children still print
/// from source. A node built by <see cref="GuiBuilder"/> has no source at all and is always
/// rebuilt, indented by the printer rather than by the caller.
/// </summary>
public sealed class GuiNode
{
    /// <summary>Head tokens, <c>=</c> included, exactly as written.</summary>
    public List<string> Head { get; }

    /// <summary>The value half of a <c>key = value</c> leaf. Null for blocks and bare token runs.</summary>
    public string? Value { get; private set; }

    public bool IsBlock { get; }

    /// <summary>Children of a block. Always empty for a leaf.</summary>
    public List<GuiNode> Children { get; } = [];

    /// <summary>
    /// Written on one line — <c>size = { 100 100 }</c> — rather than opened out.
    ///
    /// Only consulted when a node is rebuilt, which for a parsed node means it was mutated. It is
    /// still recorded at parse time so a node lifted out of vanilla and re-emitted elsewhere keeps
    /// the shape it had.
    /// </summary>
    public bool Inline { get; set; }

    /// <summary>
    /// Whitespace and comments between the previous sibling and this node, as found.
    ///
    /// Null on a synthesized node, which is the signal to the printer to generate a newline and the
    /// right indent instead. Comments live here — a <c>#</c> line above a widget belongs to the
    /// widget below it, which is where a reader puts it and where an insert has to preserve it.
    /// </summary>
    public string? LeadingTrivia { get; set; }

    /// <summary>Whitespace and comments between the last child and the closing brace.</summary>
    public string? TrailingTrivia { get; set; }

    /// <summary>
    /// A comment written above this node, on its own lines, at this node's indentation.
    ///
    /// For built nodes only — a parsed node's comments are already in its
    /// <see cref="LeadingTrivia"/> and reprint with it. This is how the reasoning that currently
    /// lives inside the emitted <c>.gui</c> files survives the move off raw strings: the width
    /// budget on the lore panel and the notes on the debug panel are addressed to whoever opens the
    /// generated file, not to whoever reads this project, so they belong in the output.
    /// </summary>
    public string? Comment { get; set; }

    /// <summary>
    /// A blank line above this node when it is written out.
    ///
    /// For built nodes only, and purely for the reader of the generated file. The properties of a
    /// real widget group — identity, then geometry, then behaviour — and a forty-line widget
    /// printed as one undifferentiated run is measurably harder to scan than the same widget with
    /// three gaps in it. The files this project overrides are read by people debugging them.
    /// </summary>
    public bool BlankBefore { get; set; }

    /// <summary>The parent, so a mutation can mark the whole chain dirty. Null at file level.</summary>
    public GuiNode? Parent { get; private set; }

    /// <summary>Whether this node must be rebuilt rather than reprinted from source.</summary>
    public bool Dirty { get; private set; }

    /// <summary>
    /// Source text for this node: the whole leaf, or a block's head up to but excluding its <c>{</c>.
    /// Null when the node was built rather than parsed.
    /// </summary>
    private readonly string? _source;

    public GuiNode(IEnumerable<string> head, bool isBlock)
    {
        Head = [.. head];
        IsBlock = isBlock;
        Dirty = true;
    }

    /// <summary>The parser's constructor: everything the node needs to reprint itself untouched.</summary>
    internal GuiNode(IEnumerable<string> head, bool isBlock, string? value, string source, string leading)
    {
        Head = [.. head];
        IsBlock = isBlock;
        Value = value;
        _source = source;
        LeadingTrivia = leading;
        Dirty = false;
    }

    // -----------------------------------------------------------------------------------------
    // Reading
    // -----------------------------------------------------------------------------------------

    /// <summary>The first head token: the widget type, the property name, or the keyword.</summary>
    public string Key => Head.Count > 0 ? Head[0] : "";

    /// <summary>
    /// The <c>name = "…"</c> a container gives itself, unquoted, or null.
    ///
    /// This is what almost every anchor in this project is really asking about, so it is a property
    /// rather than a search: a widget's name is how vanilla identifies it to itself, and it is far
    /// more stable across CK3 patches than the widget's position in a file.
    /// </summary>
    public string? Name => Field("name") is { } v ? Unquote(v) : null;

    /// <summary>The value of a direct child leaf with this key, as written (quotes included).</summary>
    public string? Field(string key)
        => Children.FirstOrDefault(c => !c.IsBlock && c.Key == key && c.Value is not null)?.Value;

    /// <summary>Direct children with this key, blocks and leaves alike.</summary>
    public IEnumerable<GuiNode> ChildrenNamed(string key) => Children.Where(c => c.Key == key);

    /// <summary>Every node beneath this one, parents before children.</summary>
    public IEnumerable<GuiNode> Descendants()
    {
        foreach (var child in Children)
        {
            yield return child;
            foreach (var deeper in child.Descendants()) yield return deeper;
        }
    }

    /// <summary>
    /// The node's own source text, comments included — what an <c>IndexOf</c> anchor used to match.
    ///
    /// Kept for anchors with nothing structural to grab: vanilla marks its two council rows with
    /// nothing but a trailing <c>#</c> comment, and matching that comment is at least honest about
    /// what it is. Empty for a synthesized node.
    /// </summary>
    public string SourceHead => _source ?? "";

    /// <summary>Whatever followed the opening brace on its own line — vanilla's row comments.</summary>
    internal string HeadTail { get; set; } = "";

    /// <summary>
    /// The node's first line as it appears in the file, trailing comment included.
    ///
    /// The anchor of last resort. <c>window_council.gui</c> distinguishes its two council rows by
    /// nothing but <c>hbox = { # Chancellor + Steward</c> — no name, no distinguishing property —
    /// so matching the comment is the only handle there is, and doing it through a named property
    /// at least says so out loud instead of burying it in an <c>IndexOf</c>.
    /// </summary>
    public string HeadLine => IsBlock ? SourceHead + "{" + HeadTail : SourceHead;

    // -----------------------------------------------------------------------------------------
    // Writing
    // -----------------------------------------------------------------------------------------

    /// <summary>Replaces a leaf's value and marks the chain dirty.</summary>
    public void SetValue(string value)
    {
        Value = value;
        Touch();
    }

    /// <summary>
    /// Sets — or replaces — a direct child leaf <c>key = value</c>.
    ///
    /// Replaces in place so the property keeps its position among its siblings, which matters more
    /// than it sounds: a <c>visible</c> rewritten where it stood and a <c>visible</c> appended at
    /// the bottom are the same to the engine and different to every future diff.
    /// </summary>
    public GuiNode Set(string key, string value)
    {
        var existing = Children.FirstOrDefault(c => !c.IsBlock && c.Key == key);
        if (existing is not null) existing.SetValue(value);
        else Add(Leaf(key, value));
        return this;
    }

    public GuiNode Add(GuiNode child)
    {
        child.Parent = this;
        Children.Add(child);
        Touch();
        return this;
    }

    /// <summary>
    /// Takes a child that is already where it belongs, without marking anything dirty.
    ///
    /// The parser's own attachment. <see cref="Add"/> would dirty the whole chain and cost the
    /// tree its verbatim reprint before a caller had changed a thing.
    /// </summary>
    internal void Adopt(GuiNode child)
    {
        child.Parent = this;
        Children.Add(child);
    }

    public GuiNode AddRange(IEnumerable<GuiNode> children)
    {
        foreach (var child in children) Add(child);
        return this;
    }

    /// <summary>Inserts <paramref name="node"/> as a sibling immediately before this one.</summary>
    public void InsertBefore(GuiNode node) => InsertAt(0, node);

    /// <summary>Inserts <paramref name="node"/> as a sibling immediately after this one.</summary>
    public void InsertAfter(GuiNode node) => InsertAt(1, node);

    private void InsertAt(int offset, GuiNode node)
    {
        if (Parent is null) throw new InvalidOperationException("node has no parent to insert into");

        int at = Parent.Children.IndexOf(this) + offset;
        node.Parent = Parent;

        // The inserted node stands where this one stands, so it takes this one's indentation rather
        // than guessing at it. That is the whole of what the old writers did by measuring the anchor
        // line's prefix, and it now happens in one place.
        node.LeadingTrivia ??= IndentOf(this);

        Parent.Children.Insert(at, node);
        Parent.Touch();
    }

    /// <summary>Inserts a child as the first entry of this block.</summary>
    public void InsertFirst(GuiNode node)
    {
        node.Parent = this;
        node.LeadingTrivia ??= Children.Count > 0 ? IndentOf(Children[0]) : null;
        Children.Insert(0, node);
        Touch();
    }

    /// <summary>The newline-plus-indent a sibling of <paramref name="node"/> should be written at.</summary>
    private static string? IndentOf(GuiNode node)
    {
        if (node.LeadingTrivia is not { } trivia) return null;

        int nl = trivia.LastIndexOf('\n');
        return nl < 0 ? null : "\n" + trivia[(nl + 1)..];
    }

    private void Touch()
    {
        Dirty = true;
        for (var n = Parent; n is not null; n = n.Parent) n.Dirty = true;
    }

    // -----------------------------------------------------------------------------------------
    // Construction
    // -----------------------------------------------------------------------------------------

    public static GuiNode Leaf(string key, string value)
        => new([key, "="], isBlock: false) { Value = value };

    /// <summary>A bare token run, as found inside an inline block: <c>{ 100 100 }</c>.</summary>
    public static GuiNode Tokens(params string[] tokens) => new(tokens, isBlock: false);

    public static GuiNode Block(params string[] head) => new(head, isBlock: true);

    /// <summary>A one-line block: <c>size = { 100 100 }</c>.</summary>
    public static GuiNode InlineBlock(string key, params string[] tokens)
    {
        var node = new GuiNode([key, "="], isBlock: true) { Inline = true };
        node.Add(Tokens(tokens));
        return node;
    }

    /// <summary>
    /// A deep copy with no source and no parent, so it can be planted anywhere.
    ///
    /// Source and trivia are dropped deliberately rather than carried: a node lifted out of one
    /// file and put into another at a different depth would otherwise reprint its old indentation
    /// verbatim, which is the failure that makes a spliced block look pasted in.
    ///
    /// Comments are the exception, and are promoted out of the trivia into <see cref="Comment"/> so
    /// the printer can re-indent them with the node they belong to. Dropping them would be the
    /// quietest possible loss — the file still compiles, still runs, and the reasoning is gone.
    /// </summary>
    public GuiNode Clone()
    {
        var copy = new GuiNode(Head, IsBlock)
        {
            Value = Value,
            Inline = Inline,
            Comment = Comment ?? CommentsIn(LeadingTrivia),
        };

        foreach (var child in Children) copy.Add(child.Clone());
        return copy;
    }

    /// <summary>The <c>#</c> lines in a run of trivia, as comment text with the markers stripped.</summary>
    private static string? CommentsIn(string? trivia)
    {
        if (trivia is null || !trivia.Contains('#')) return null;

        var lines = trivia.Replace("\r\n", "\n").Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith('#'))
            .Select(line => line[1..].TrimStart())
            .ToList();

        return lines.Count == 0 ? null : string.Join('\n', lines);
    }

    // -----------------------------------------------------------------------------------------
    // Printing
    // -----------------------------------------------------------------------------------------

    internal void Print(StringBuilder sb, string indent, bool first)
    {
        sb.Append(LeadingTrivia
            ?? (first ? "" : BlankBefore ? "\n\n" + indent : "\n" + indent));

        if (Comment is not null)
        {
            foreach (string line in Comment.Replace("\r\n", "\n").Split('\n'))
                sb.Append(line.Length == 0 ? "#" : "# " + line).Append('\n').Append(indent);
        }

        // A parsed block prints its head from source whether or not anything below it moved. Only a
        // leaf's value is ever rewritten, so a container's own punctuation has no reason to be
        // retyped — and retyping it would quietly normalise spacing a CK3 author chose, turning a
        // one-line patch into a diff against every line of the container.
        if (_source is not null && (IsBlock || !Dirty))
        {
            PrintFromSource(sb, indent);
            return;
        }

        sb.Append(string.Join(' ', Head));

        if (!IsBlock)
        {
            if (Value is not null) sb.Append(' ').Append(Value);
            return;
        }

        // An empty block is always written closed up. `expand = {}` and `blockoverride "icon" {}`
        // are how vanilla spells them, and an empty pair of braces opened across two lines reads as
        // an unfinished widget rather than a deliberate one.
        if (Children.Count == 0)
        {
            sb.Append(" {}");
            return;
        }

        if (Inline)
        {
            sb.Append(" { ").Append(string.Join(' ', Children.Select(InlineText))).Append(" }");
            return;
        }

        sb.Append(" {");

        string inner = indent + "\t";
        foreach (var child in Children) child.Print(sb, inner, first: false);

        sb.Append(TrailingTrivia ?? "\n" + indent).Append('}');
    }

    /// <summary>
    /// A node nothing has touched, printed exactly as it was read.
    ///
    /// A block still walks its children — it is here because something below it changed — but its
    /// own head and closing brace come from the file, so spacing a CK3 author chose survives even
    /// in a container the patch reached into.
    /// </summary>
    private void PrintFromSource(StringBuilder sb, string indent)
    {
        sb.Append(_source);

        if (!IsBlock) return;

        sb.Append('{');

        string inner = indent + "\t";
        foreach (var child in Children) child.Print(sb, inner, first: false);

        sb.Append(TrailingTrivia ?? "\n" + indent).Append('}');
    }

    private static string InlineText(GuiNode node)
    {
        string head = string.Join(' ', node.Head);
        return node.Value is null ? head : head + " " + node.Value;
    }

    public static string Unquote(string value)
        => value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;

    public static string Quote(string value) => '"' + value + '"';

    public override string ToString()
    {
        var sb = new StringBuilder();
        Print(sb, "", first: true);
        return sb.ToString();
    }
}
