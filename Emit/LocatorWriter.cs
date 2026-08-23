using System.Globalization;
using Ck3MapGen.Core;
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
/// </summary>
public static class LocatorWriter
{
    /// <summary>
    /// The eight locator files CK3 expects, with the exact header vanilla uses.
    ///
    /// <para><b>Yaw</b> and <b>Jitter</b> (degrees) describe the rotation each layer's instances
    /// get: a base heading plus a uniform ±Jitter. Identity everywhere is wrong — it points every
    /// holding and every wonder due north, and a map of identical compass-aligned castles reads as
    /// stamped rather than built. Vanilla does not do that, and it does not do one thing either:
    /// measured over its own 11k-instance files (circular mean, and the half-width of the uniform
    /// arc with the same resultant length R), each layer has its own convention.</para>
    ///
    /// <code>
    ///   buildings          R=0.011  uniform over the full circle
    ///   special_building   R=0.279  mean  85 deg, arc ±137
    ///   combat             R=0.354  mean 226 deg, arc ±127
    ///   siege              R=0.231  mean 186 deg, arc ±144
    ///   unit_stack         R=1.000  identity, every instance
    ///   ..._player_owned   R=0.678  mean 339 deg, arc ±84
    ///   ..._other_owner    R=0.887  mean  75 deg, arc ±48
    ///   activities         R=0.530  mean  11 deg, arc ±105
    /// </code>
    ///
    /// <para>So holdings are placed at any heading at all, while the unit and activity markers sit
    /// near a per-layer default and only wander around it — those are army and marker models that
    /// are meant to read the same way in every province. <c>unit_stack</c> keeps its identity
    /// rotation because vanilla's file is uniformly identity: the two owner-specific stack layers
    /// carry the authored headings and the plain one was never turned.</para>
    ///
    /// <para>Nothing here is derived from the province: vanilla's building yaw does not correlate
    /// with its special-building yaw (median 90° apart), with the siege or combat yaw, or with the
    /// direction between the two points. There is no geometry to recover, only a distribution to
    /// match, so the angle is drawn from a per-layer <see cref="Rng"/>.</para>
    /// </summary>
    private static readonly (string File, string Name, string Layer, bool Clamp, bool Sea,
        double Yaw, double Jitter)[] Kinds =
    [
        // file                            name                       layer              clamp  sea    yaw  jitter
        ("building_locators.txt",          "buildings",               "building_layer",   true,  false,   0,  180),
        ("special_building_locators.txt",  "special_building",        "building_layer",   true,  false,  85,  137),
        ("combat_locators.txt",            "combat",                  "unit_layer",       true,  true,  226,  127),
        ("siege_locators.txt",             "siege",                   "unit_layer",       false, false, 186,  144),
        ("stack_locators.txt",             "unit_stack",              "unit_layer",       true,  false,   0,    0),
        ("player_stack_locators.txt",      "unit_stack_player_owned", "unit_layer",       true,  true,  339,   84),
        ("other_stack_locators.txt",       "unit_stack_other_owner",  "unit_layer",       true,  true,   75,   48),
        ("activities.txt",                 "activities",              "activities_layer", false, false,  11,  105),
    ];

    /// <summary>
    /// Layer files that declare structure rather than per-province data. They are copied
    /// verbatim from vanilla because replace_path removes the originals.
    /// </summary>
    private static readonly string[] LayerFiles =
        ["layers.txt", "game_object_layers.txt", "effect_layers.txt"];

    public static void WriteAll(string modDir, string gameDir, ProvinceMap provinces,
        int[] order, int landCount, float[] provinceElevation, Config.MapConfig cfg)
    {
        string dir = Path.Combine(modDir, "gfx", "map", "map_object_data");
        Directory.CreateDirectory(dir);

        CopyLayerFiles(dir, gameDir);

        // order maps label -> province id; invert it so ids can be walked in order.
        var byId = new int[provinces.Count + 1];
        for (int label = 0; label < provinces.Count; label++) byId[order[label]] = label;

        // Not the province seed: that is wherever the partitioner started growing, which is as
        // likely to be a coastline pixel as anything else. See ProvinceAnchor.
        var anchors = ProvinceAnchor.Compute(provinces, provinceElevation, cfg);

        int seaCount = provinces.Count - landCount;
        foreach (var kind in Kinds)
        {
            // The special building is the one thing that shares a province with the holding rather
            // than replacing it, so it is the one thing that needs its own point to stand on.
            var points = kind.Name == "special_building" ? anchors.Special : anchors.Holding;
            WriteLocators(dir, kind, provinces, byId, points, cfg.Seed);
        }

        Console.WriteLine($"  locators: {Kinds.Length} files, {landCount} land " +
                          $"(+{seaCount} sea on combat/stack layers)");
    }

    private static void WriteLocators(string dir,
        (string File, string Name, string Layer, bool Clamp, bool Sea, double Yaw, double Jitter) kind,
        ProvinceMap provinces, int[] byId, (double X, double Y)[] anchors, int seed)
    {
        // Seeded off the layer name rather than shared across the eight files, so which provinces
        // a file skips cannot shift the angles in the next one, and the same world seed always
        // turns the same castle the same way.
        var rng = new Rng(Rng.StableHash(kind.Name) ^ (ulong)(uint)seed);

        // Compact style: these files are written key=value with no spaces, which is how the
        // engine's own map_object_data files are written and what a diff against them expects.
        var b = new JominiBuilder(JominiStyle.Compact);

        using (b.Block("game_object_locator"))
        {
            b.Quoted("name", kind.Name);
            b.Field("render_pass", "Map");
            b.Field("clamp_to_water_level", kind.Clamp ? "yes" : "no");
            b.Field("generated_content", "no");
            b.Quoted("layer", kind.Layer);

            using (b.Block("instances"))
            {
                // Instance 0 is required even though province ids start at 1. Without it CK3 reports the
                // locator "is incomplete", writes a corrected copy to Documents/.../generated/, and then
                // fails with "Failed to get transform for locator type '...' instance id 0". Its own
                // regenerated file starts with exactly this: id 0 parked at the origin, then 1..N.
                using (b.Block())
                {
                    b.Field("id", "0");
                    b.Inline("position", "0.000000", "0.000000", "0.000000");
                    b.Inline("rotation", "-0.000000", "-0.000000", "-0.000000", "1.000000");
                    b.Inline("scale", "1.000000", "1.000000", "1.000000");
                }

                for (int id = 1; id <= provinces.Count; id++)
                {
                    int label = byId[id];

                    // Sea zones only carry the locators that can actually happen at sea. Skipping them
                    // elsewhere is what vanilla does and keeps the files a third smaller.
                    if (!provinces.Seeds[label].IsLand && !kind.Sea) continue;

                    // Image rows run top-down, the map's Z axis runs bottom-up — see WorldSpace, which
                    // every scatter pass shares with this one.
                    var (x, z) = WorldSpace.FromImage(anchors[label].X, anchors[label].Y, provinces.Height);

                    // Rotation about the vertical axis only, so the quaternion has no X or Z part — the
                    // same (0, sin t/2, 0, cos t/2) form TreeWriter and BridgeWriter write, under which
                    // the mesh's local +Z lands on world (sin t, cos t).
                    double angle = (kind.Yaw + rng.Double(-kind.Jitter, kind.Jitter)) * Math.PI / 180.0;
                    double qy = Math.Sin(angle / 2.0);
                    double qw = Math.Cos(angle / 2.0);

                    using (b.Block())
                    {
                        b.Field("id", id);
                        b.Inline("position", F(x), "0.000000", F(z));
                        b.Inline("rotation", "0.000000", F(qy), "0.000000", F(qw));
                        b.Inline("scale", "1.000000", "1.000000", "1.000000");
                    }
                }
            }
        }

        ParadoxText.WriteBom(Path.Combine(dir, kind.File), b.ToString());
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

    /// <summary>
    /// Six decimal places, invariant culture — the precision the engine's own locator files use.
    /// </summary>
    private static string F(double v) => v.ToString("F6", CultureInfo.InvariantCulture);
}
