using System.Globalization;
using System.Text;

namespace Ck3MapGen.Io;

/// <summary>
/// Punctuation and indentation for a Paradox script file, so the writers can stop counting tabs.
///
/// Every emitter in this project used to build its script by appending literal <c>"\t\t\t"</c>
/// strings, which works and is what most of them still do. The cost is not correctness — it is
/// that the *nesting* of the output is invisible in the C# that produces it, so a block opened
/// four levels down is closed by a string literal that has to be counted character by character
/// to check. That is how <c>audio_parameter</c> ended up indented one level too deep in the
/// heritage block for as long as it did: nothing about the code made it wrong-looking.
///
/// Here the nesting is a <c>using</c> scope. The C# indents exactly where the output indents, the
/// closing brace is written by the scope rather than by hand, and a mismatched brace is not
/// something the type system will let you express.
///
/// This is emission only. It does not know what a culture or a faith is, it does not validate
/// keys, and it deliberately has no opinion about *what* goes in a file — the writers keep all of
/// that. Anything it cannot express is reachable through <see cref="Raw"/>, which is what makes a
/// writer convertible one method at a time instead of all at once.
/// </summary>
public sealed class JominiBuilder
{
    private readonly StringBuilder _sb = new();
    private readonly JominiStyle _style;
    private int _depth;

    public JominiBuilder(JominiStyle? style = null) => _style = style ?? JominiStyle.Script;

    /// <summary>How deep the next line will be written. Exposed for <see cref="Raw"/> callers.</summary>
    public int Depth => _depth;

    // -------------------------------------------------------------------------------------
    // Structure
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// Opens <c>key = {</c> and closes it when the returned scope is disposed.
    ///
    /// Always used as <c>using (b.Block("x")) { … }</c>. The scope is a struct and holds only the
    /// builder, so the <c>using</c> costs nothing beyond the brace it guarantees.
    /// </summary>
    public Scope Block(string key)
    {
        Indent();
        _sb.Append(key).Append(_style.Separator).Append("{\n");
        _depth++;
        return new Scope(this);
    }

    /// <summary>
    /// A block keyed by a number rather than an identifier: <c>10 = { … }</c>.
    ///
    /// Ethnicity palettes and gene weights are written this way — the key is a weight, and CK3
    /// reads the whole block as one weighted entry in a list.
    /// </summary>
    public Scope Block(int weight)
    {
        Indent();
        _sb.Append(weight.ToString(CultureInfo.InvariantCulture)).Append(_style.Separator).Append("{\n");
        _depth++;
        return new Scope(this);
    }

    /// <summary>An anonymous block — a bare <c>{ … }</c>, as in a locator instance list.</summary>
    public Scope Block()
    {
        Indent();
        _sb.Append("{\n");
        _depth++;
        return new Scope(this);
    }

    // -------------------------------------------------------------------------------------
    // Leaves
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// <c>key = value</c>, or nothing at all when <paramref name="value"/> is null.
    ///
    /// The null-skip is not a convenience — it is the shape the writers already had, spelled
    /// <c>if (entry.Gender is not null) sb.Append(…)</c> at every optional field. Folding it in
    /// here keeps the conditional out of the emission code, where it reads as structure and
    /// obscures the fields that are always written.
    /// </summary>
    public void Field(string key, string? value)
    {
        if (value is null) return;
        Indent();
        _sb.Append(key).Append(_style.Separator).Append(value).Append('\n');
    }

    public void Field(string key, int value)
        => Field(key, value.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// A floating-point field, always in the invariant culture.
    ///
    /// The format is required rather than defaulted because the files disagree about it and the
    /// disagreement matters: locator positions are written to a fixed six places because the
    /// engine's own files are, while a weight or a range is written as short as it will go. A
    /// default here would silently pick one of those for a caller who wanted the other.
    /// </summary>
    public void Field(string key, double value, string format)
        => Field(key, value.ToString(format, CultureInfo.InvariantCulture));

    /// <summary>A quoted value: <c>key = "value"</c>. The value is not escaped — see
    /// <see cref="ParadoxText.Loc"/> for the one place escaping is done, and note that it is for
    /// localisation values rather than for script.</summary>
    public void Quoted(string key, string value)
    {
        Indent();
        _sb.Append(key).Append(_style.Separator).Append('"').Append(value).Append("\"\n");
    }

    /// <summary>
    /// A line that is a bare token rather than a pair — a tradition inside <c>traditions</c>, an
    /// on_action name inside <c>on_actions</c>, a government string inside a defines list.
    /// </summary>
    public void Token(string token)
    {
        Indent();
        _sb.Append(token).Append('\n');
    }

    /// <summary>
    /// A block written on one line: <c>key = { a b c }</c>.
    ///
    /// Used wherever the contents are short and reading them as a unit beats reading them as a
    /// structure — colours, ranges, virtue and sin lists, government filters.
    /// </summary>
    public void Inline(string key, params string[] tokens)
    {
        Indent();
        _sb.Append(key).Append(_style.Separator).Append("{ ").Append(string.Join(' ', tokens)).Append(" }\n");
    }

    /// <summary>A colour triple, the commonest inline block in the project.</summary>
    public void Color(string key, int r, int g, int b)
        => Inline(key, r.ToString(CultureInfo.InvariantCulture),
                       g.ToString(CultureInfo.InvariantCulture),
                       b.ToString(CultureInfo.InvariantCulture));

    // -------------------------------------------------------------------------------------
    // Layout
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// A comment, indented to the current depth. Multi-line text is split and every line gets its
    /// own <c>#</c>, so a paragraph of reasoning can be passed in as one string — which is how
    /// most of the headers in this project are written, and they are worth keeping.
    /// </summary>
    public void Comment(string text)
    {
        foreach (string line in text.Replace("\r\n", "\n").Split('\n'))
        {
            Indent();
            if (line.Length == 0) _sb.Append("#\n");
            else _sb.Append("# ").Append(line).Append('\n');
        }
    }

    /// <summary>One empty line. Never emitted automatically: the blank lines in these files are
    /// deliberate grouping and belong to the writer, not to the brace structure.</summary>
    public void Blank() => _sb.Append('\n');

    /// <summary>
    /// Text appended exactly as given, with no indentation applied.
    ///
    /// The escape hatch, and the reason a writer can be converted a method at a time: a block that
    /// is already correct as a string literal — a hand-tuned trigger, a chunk lifted from vanilla —
    /// goes through here unchanged while the code around it moves to the builder. Callers own the
    /// tabs and the trailing newline. Anything routed through here is invisible to the brace
    /// checking above, so prefer the structured calls when there is a choice.
    /// </summary>
    public void Raw(string text) => _sb.Append(text);

    /// <summary>The indent string for a given depth, for callers building a line by hand.</summary>
    public string IndentAt(int depth) => string.Concat(Enumerable.Repeat(_style.Indent, depth));

    public override string ToString() => _sb.ToString();

    private void Indent()
    {
        for (int i = 0; i < _depth; i++) _sb.Append(_style.Indent);
    }

    /// <summary>
    /// The open brace's other half. Disposed by the <c>using</c> that opened the block, which is
    /// the whole point — there is no code path that opens a block and forgets to close it.
    /// </summary>
    public readonly struct Scope(JominiBuilder owner) : IDisposable
    {
        public void Dispose()
        {
            owner._depth--;
            owner.Indent();
            owner._sb.Append("}\n");
        }
    }
}

/// <summary>
/// The punctuation the files disagree about.
///
/// Two conventions are in play and neither is negotiable, because both are matched against
/// vanilla files rather than chosen: script under <c>common/</c> and <c>history/</c> is written
/// <c>key = value</c>, and the map object locator files are written <c>key=value</c> with no
/// spaces at all. Indentation is tabs in both.
/// </summary>
public sealed record JominiStyle(string Indent, string Separator)
{
    /// <summary>common/, history/, events/ — spaces around the equals.</summary>
    public static readonly JominiStyle Script = new("\t", " = ");

    /// <summary>gfx/map/map_object_data/ locators — no spaces.</summary>
    public static readonly JominiStyle Compact = new("\t", "=");

    /// <summary>common/landed_titles/ — four spaces, as the existing file is written.</summary>
    public static readonly JominiStyle Spaced = new("    ", " = ");
}
