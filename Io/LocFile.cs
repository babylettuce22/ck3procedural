using System.Text;

namespace Ck3MapGen.Io;

/// <summary>
/// One localisation file. Owns the two conventions that a dozen writers each restated by hand:
/// the <c>l_english:</c> header, and the <c>&#160;key:0 "value"</c> line — leading space, version
/// zero, quoted value.
///
/// The escaping split is deliberate and is the reason there are two add methods rather than one.
/// Most values are display names that came out of the generator and go straight to screen, so they
/// want <see cref="ParadoxText.Loc"/>. A few are already-built strings — a chronicle paragraph that
/// has had its <c>\n\n</c> joins put in on purpose — and escaping those a second time would turn
/// the line breaks into visible backslashes. <see cref="Add"/> is the safe default;
/// <see cref="AddBuilt"/> is the one you have to ask for.
///
/// There is deliberately no BOM anywhere in the string this produces. Writing one here is a real
/// bug with a silent failure mode — <see cref="ParadoxText.WriteBom"/> adds the encoder's own, and
/// a second U+FEFF in front of <c>l_english:</c> stops CK3 recognising the header, at which point
/// it skips the entire file without a log line. That happened once already, in the title tier
/// writer, and went unnoticed from the start.
/// </summary>
public sealed class LocFile
{
    private readonly StringBuilder _sb = new("l_english:\n");

    /// <summary>A display value straight from the generator. Escaped on the way in.</summary>
    public void Add(string key, string value)
        => _sb.Append(' ').Append(key).Append(":0 \"").Append(ParadoxText.Loc(value)).Append("\"\n");

    /// <summary>
    /// A value that has already been assembled and escaped by its caller — chronicle prose with
    /// intentional <c>\n</c> joins, or text that carries CK3 loc scopes like
    /// <c>[colony.GetName]</c> which must survive verbatim.
    /// </summary>
    public void AddBuilt(string key, string value)
        => _sb.Append(' ').Append(key).Append(":0 \"").Append(value).Append("\"\n");

    /// <summary>
    /// An entry written without the <c>:0</c> version number.
    ///
    /// CK3 treats the version as optional and the dynasty and house name lines have always been
    /// written this way, so the form is preserved rather than tidied — every one of those keys is
    /// referenced by a generated <c>name = "dynn_…"</c>, and a silent reformat of a working file is
    /// not something a refactor should smuggle in. Prefer <see cref="Add"/> for anything new.
    /// </summary>
    public void AddUnversioned(string key, string value)
        => _sb.Append(' ').Append(key).Append(": \"").Append(value).Append("\"\n");

    /// <summary>A blank line, for the writers that group their entries.</summary>
    public void Blank() => _sb.Append('\n');

    public override string ToString() => _sb.ToString();

    /// <summary>Creates the directory and writes with the BOM CK3 requires for loc.</summary>
    public void Write(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        ParadoxText.WriteBom(path, _sb.ToString());
    }
}
