using System.Text;

namespace Ck3MapGen.Io;

/// <summary>
/// Owns the BOM distinction, which is not cosmetic in CK3.
///
/// Core map_data files (definition.csv, default.map, adjacencies.csv, seasons.txt,
/// island_region.txt) have **no BOM** in vanilla. Script files under common/, history/, gfx/ and
/// map_data/geographical_regions need UTF-8 **with** BOM. Always write through here rather than
/// File.WriteAllText so the choice is explicit at every site.
///
/// heightmap.heightmap was listed above as no-BOM and is not: vanilla's begins ef bb bf, and so
/// does one written by Clausewitz's own repacker. It only stopped mattering while this project
/// shipped a bare heightmap.png and let the map editor rewrite the file; the moment it ships the
/// packed trio itself, the file it writes is the one CK3 has to parse.
/// </summary>
public static class ParadoxText
{
    private static readonly UTF8Encoding NoBomEncoding = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly UTF8Encoding BomEncoding = new(encoderShouldEmitUTF8Identifier: true);

    /// <summary>For map_data core files.</summary>
    public static void WriteNoBom(string path, string text)
        => File.WriteAllText(path, Normalize(text), NoBomEncoding);

    /// <summary>For script files under common/, history/, gfx/ and geographical_regions.</summary>
    public static void WriteBom(string path, string text)
        => File.WriteAllText(path, Normalize(text), BomEncoding);

    /// <summary>
    /// A string as the value half of a localisation line.
    ///
    /// Every loc line in this codebase is emitted as <c>key:0 "value"</c> with the value
    /// interpolated raw, so a quote inside it ends the string early and leaves the rest of the line
    /// as garbage the game reports as a syntax error on the *file*, not on the entry. Generated
    /// names cannot contain one — every one of them comes out of <see cref="MapGen.Language"/> —
    /// but hand-edited names can, which is what this exists for.
    ///
    /// Line breaks are folded to a space rather than escaped to <c>\n</c>. Everything routed
    /// through here is a single-line display string — a title, a faith, an artifact — and a name
    /// that wraps in the middle is not something a title bar can draw.
    ///
    /// Backslashes are deliberately left alone. Escaping them would be more correct in isolation
    /// and would change how any existing value containing one renders, so the editor rejects them
    /// on input instead.
    /// </summary>
    public static string Loc(string value)
        => value.Replace("\"", "\\\"")
                .Replace("\r\n", " ")
                .Replace('\n', ' ')
                .Replace('\r', ' ');

    private static string Normalize(string text) => text.Replace("\r\n", "\n");
}
