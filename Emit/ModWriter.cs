using Ck3MapGen.Io;

namespace Ck3MapGen.Emit;

/// <summary>
/// Writes the launcher plumbing. CK3 needs two files that must agree: descriptor.mod inside the
/// mod folder, and a sibling &lt;name&gt;.mod next to it carrying an absolute path= line.
/// Without the outer one the launcher never lists the mod at all.
/// </summary>
public static class ModWriter
{
    public const string SupportedVersion = "1.19.0.6";

    /// <summary>
    /// Directories where a vanilla file left in place is actively harmful because it is keyed to
    /// the old map. Shadowing them file-by-file with empty copies is not enough: it only covers
    /// the names we happen to enumerate, and it cannot cover binary or non-.txt data at all.
    ///
    /// gfx/map/map_object_data and map_data are the two that matter most. Both hold per-province
    /// data addressed by vanilla province id and vanilla world coordinates (locators up to
    /// ~14000/9216, positions.txt, the 44 MB nodes.dat pathfinding graph). None of it is script,
    /// so a mismatch logs nothing at all — the load just stops after history with a core spinning.
    /// ck2rpg declares the same two paths.
    /// </summary>
    private static readonly string[] ReplacePaths =
    [
        // NOTE: "map_data" is deliberately NOT replaced. Doing so drops vanilla's nodes.dat
        // (the precomputed pathfinding graph), positions.txt and climate.txt, none of which we
        // generate and all of which vanilla *and* ck2rpg's working template ship. With them
        // gone the map-loading worker dereferences a null object at +0x28 about two seconds
        // after history loading. Every file we do generate shares vanilla's filename and so
        // shadows it anyway, which is all the override we actually need — and now that the map
        // is emitted at exact vanilla dimensions, vanilla's leftovers are dimensionally valid.
        "gfx/map/map_object_data",

        // replace_path is NOT recursive. ck2rpg's template declares the generated/ subdirectory
        // separately from its parent, which it would have no reason to do otherwise — and
        // without it vanilla's ~50 MB of foliage instances, positioned in vanilla world
        // coordinates, keep loading onto our map.
        "gfx/map/map_object_data/generated",

        // Vanilla flavorization names titles we no longer declare.
        "common/flavorization/00_flavorization.txt",

        // Dynamic coat of arms definitions hardcode vanilla title keys. AGOT replaces this
        // directory and ships a single disabled file; we do the same.
        "common/coat_of_arms/dynamic_definitions",

        // Per-material terrain masks are per-map data painted for vanilla's continents. We now
        // generate our own for every material vanilla ships, so drop the originals entirely
        // rather than relying on filename-by-filename shadowing.
        "gfx/map/terrain/masks",
        "common/landed_titles",
        "common/province_terrain",
        "common/bookmarks/bookmarks",
        // NOT common/bookmarks/groups — our bookmark attaches to vanilla's bm_group_867.
        "common/bookmarks/challenge_characters",
        "common/bookmark_portraits",
        "history/characters",
        "history/provinces",
        "history/titles",
        "history/wars",
        "history/struggles",
        "history/situations",
    ];

    public static void WriteDescriptors(string modDir, string name = "Procedural Map")
    {
        string folder = Path.GetFileName(modDir.TrimEnd(Path.DirectorySeparatorChar));
        string replacements =
            string.Concat(ReplacePaths.Select(p => $"replace_path=\"{p}\"\n"));

        string descriptor =
            $$"""
              version="1.0.0"
              tags={
              	"Total Conversion"
              	"Map"
              }
              name="{{name}}"
              {{replacements}}supported_version="{{SupportedVersion}}"

              """;

        ParadoxText.WriteBom(Path.Combine(modDir, "descriptor.mod"), descriptor);

        // The launcher-facing copy lives beside the folder and adds an absolute path.
        string? parent = Path.GetDirectoryName(modDir.TrimEnd(Path.DirectorySeparatorChar));
        if (parent is null) return;

        string outer = descriptor.TrimEnd() + $"\npath=\"{modDir.Replace('\\', '/')}\"\n";
        ParadoxText.WriteBom(Path.Combine(parent, $"{folder}.mod"), outer);
    }
}
