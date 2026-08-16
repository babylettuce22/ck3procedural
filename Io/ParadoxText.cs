using System.Text;

namespace Ck3MapGen.Io;

/// <summary>
/// Owns the BOM distinction, which is not cosmetic in CK3.
///
/// Core map_data files (definition.csv, default.map, adjacencies.csv, seasons.txt,
/// island_region.txt, heightmap.heightmap) have **no BOM** in vanilla. Script files under
/// common/, history/, gfx/ and map_data/geographical_regions need UTF-8 **with** BOM. Always
/// write through here rather than File.WriteAllText so the choice is explicit at every site.
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

    private static string Normalize(string text) => text.Replace("\r\n", "\n");
}
