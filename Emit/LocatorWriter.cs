using System.Globalization;
using System.Text;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// Writes gfx/map/map_object_data — the per-province anchor points CK3 uses to place holdings,
/// armies, sieges and activity markers on the 3D map.
///
/// This is not optional and it is not script. Vanilla ships ~15 MB of locators whose instance
/// ids run to ~14000 and whose positions run to vanilla's 9216x4608 world. Against a smaller
/// generated map every one of those is out of range, and because none of it is a script error
/// **nothing is logged** — the load simply stops after history with one core spinning.
///
/// The mod therefore declares replace_path="gfx/map/map_object_data" (see <see cref="ModWriter"/>)
/// which drops vanilla's whole directory, and we rebuild it from scratch here. Dropping the
/// directory also removes the layer declarations the locators reference, so those are re-emitted
/// verbatim; without them every locator names a layer that does not exist.
///
/// Format matches vanilla field-for-field, and the instance list matches ck2rpg's
/// writeLocators.js — one instance per province, id = province id, position = province seed with
/// the Z axis flipped, since the map's Z runs bottom-up while image rows run top-down.
/// </summary>
public static class LocatorWriter
{
    /// <summary>The eight locator files CK3 expects, with the exact header vanilla uses.</summary>
    private static readonly (string File, string Name, string Layer, bool Clamp, bool Sea)[] Kinds =
    [
        // file                            name                       layer              clamp  sea
        ("building_locators.txt",          "buildings",               "building_layer",   true,  false),
        ("special_building_locators.txt",  "special_building",        "building_layer",   true,  false),
        ("combat_locators.txt",            "combat",                  "unit_layer",       true,  true),
        ("siege_locators.txt",             "siege",                   "unit_layer",       false, false),
        ("stack_locators.txt",             "unit_stack",              "unit_layer",       true,  false),
        ("player_stack_locators.txt",      "unit_stack_player_owned", "unit_layer",       true,  true),
        ("other_stack_locators.txt",       "unit_stack_other_owner",  "unit_layer",       true,  true),
        ("activities.txt",                 "activities",              "activities_layer", false, false),
    ];

    /// <summary>
    /// Layer files that declare structure rather than per-province data. They are copied
    /// verbatim from vanilla because replace_path removes the originals.
    /// </summary>
    private static readonly string[] LayerFiles =
        ["layers.txt", "game_object_layers.txt", "effect_layers.txt"];

    public static void WriteAll(string modDir, string gameDir, ProvinceMap provinces,
        int[] order, int landCount)
    {
        string dir = Path.Combine(modDir, "gfx", "map", "map_object_data");
        Directory.CreateDirectory(dir);

        CopyLayerFiles(dir, gameDir);

        // order maps label -> province id; invert it so ids can be walked in order.
        var byId = new int[provinces.Count + 1];
        for (int label = 0; label < provinces.Count; label++) byId[order[label]] = label;

        int seaCount = provinces.Count - landCount;
        foreach (var kind in Kinds) WriteLocators(dir, kind, provinces, byId);

        Console.WriteLine($"  locators: {Kinds.Length} files, {landCount} land " +
                          $"(+{seaCount} sea on combat/stack layers)");
    }

    private static void WriteLocators(string dir,
        (string File, string Name, string Layer, bool Clamp, bool Sea) kind,
        ProvinceMap provinces, int[] byId)
    {
        var sb = new StringBuilder();
        sb.Append("game_object_locator={\n");
        sb.Append($"\tname=\"{kind.Name}\"\n");
        sb.Append("\trender_pass=Map\n");
        sb.Append($"\tclamp_to_water_level={(kind.Clamp ? "yes" : "no")}\n");
        sb.Append("\tgenerated_content=no\n");
        sb.Append($"\tlayer=\"{kind.Layer}\"\n");
        sb.Append("\tinstances={\n");

        // Instance 0 is required even though province ids start at 1. Without it CK3 reports the
        // locator "is incomplete", writes a corrected copy to Documents/.../generated/, and then
        // fails with "Failed to get transform for locator type '...' instance id 0". Its own
        // regenerated file starts with exactly this: id 0 parked at the origin, then 1..N.
        sb.Append("\t\t{\n");
        sb.Append("\t\t\tid=0\n");
        sb.Append("\t\t\tposition={ 0.000000 0.000000 0.000000 }\n");
        sb.Append("\t\t\trotation={ -0.000000 -0.000000 -0.000000 1.000000 }\n");
        sb.Append("\t\t\tscale={ 1.000000 1.000000 1.000000 }\n");
        sb.Append("\t\t}\n");

        for (int id = 1; id <= provinces.Count; id++)
        {
            var seed = provinces.Seeds[byId[id]];

            // Sea zones only carry the locators that can actually happen at sea. Skipping them
            // elsewhere is what vanilla does and keeps the files a third smaller.
            if (!seed.IsLand && !kind.Sea) continue;

            // Image rows run top-down, the map's Z axis runs bottom-up.
            int z = provinces.Height - seed.Y;

            sb.Append("\t\t{\n");
            sb.Append($"\t\t\tid={id}\n");
            sb.Append(string.Create(CultureInfo.InvariantCulture,
                $"\t\t\tposition={{ {seed.X:F6} 0.000000 {z:F6} }}\n"));
            sb.Append("\t\t\trotation={ -0.000000 -0.000000 -0.000000 1.000000 }\n");
            sb.Append("\t\t\tscale={ 1.000000 1.000000 1.000000 }\n");
            sb.Append("\t\t}\n");
        }

        sb.Append("\t}\n");
        sb.Append("}\n");

        ParadoxText.WriteBom(Path.Combine(dir, kind.File), sb.ToString());
    }

    private static void CopyLayerFiles(string dir, string gameDir)
    {
        string source = Path.Combine(gameDir, "gfx", "map", "map_object_data");
        foreach (string name in LayerFiles)
        {
            string from = Path.Combine(source, name);
            if (File.Exists(from)) File.Copy(from, Path.Combine(dir, name), overwrite: true);
        }
    }
}
