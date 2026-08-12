using System.Globalization;
using System.Text.RegularExpressions;
using Ck3MapGen.Io;

namespace Ck3MapGen.Emit;

/// <summary>
/// Rescales vanilla's map_table_*.txt onto the map we actually ship.
///
/// These are the physical tabletop the map sits on — the slab, the cloth, the candles and props,
/// one entity each, placed in world coordinates. Vanilla authors them against its 9216x4608
/// provinces map: around x 4500, z 2560, at scale 5.
///
/// They were copied verbatim, which on any smaller map is wrong twice over. The table keeps
/// vanilla's absolute size while the world shrinks around it, so the map reads as a small thing on
/// a big table — and worse, the position is off the map entirely: on a 4096x2048 provinces raster
/// the world runs to 4095 x 2047 and x 4500 is past its eastern edge. Nothing is logged, because
/// nothing here is script.
///
/// Written rather than copied for that reason, and written *before*
/// <see cref="StaticFileWriter"/>, which never overwrites — so these win and the copies stay as the
/// fallback for anything this cannot parse.
/// </summary>
public static class MapTableWriter
{
    /// <summary>
    /// <c>transform="px py pz  rx ry rz rw  sx sy sz"</c>, ten floats, and vanilla puts a newline
    /// before the closing quote. Matched rather than reconstructed so everything else in the file —
    /// entity names, layers, render passes — survives untouched.
    /// </summary>
    private static readonly Regex Transform =
        new("transform=\"([^\"]*)\"", RegexOptions.Singleline | RegexOptions.Compiled);

    public static void WriteAll(string modDir, Config.MapConfig cfg)
    {
        string relative = Path.Combine("gfx", "map", "map_object_data");
        string sourceDir = Path.Combine(AppContext.BaseDirectory, StaticFileWriter.SourceFolder, relative);

        if (!Directory.Exists(sourceDir))
        {
            Console.WriteLine("  map tables: SKIPPED (no map_table_*.txt to rescale)");
            return;
        }

        string targetDir = Path.Combine(modDir, relative);
        Directory.CreateDirectory(targetDir);

        double scale = cfg.MapScale;
        int written = 0, objects = 0;

        foreach (string source in Directory.GetFiles(sourceDir, "map_table_*.txt"))
        {
            string text = File.ReadAllText(source);
            string rescaled = Transform.Replace(text, match =>
            {
                string? scaled = Rescale(match.Groups[1].Value, scale);
                if (scaled is null) return match.Value;
                objects++;
                return $"transform=\"{scaled}\"";
            });

            // BOM: vanilla's map_table files carry one, and these are gfx script rather than
            // map_data — see the BOM table in the file-formats notes.
            ParadoxText.WriteBom(Path.Combine(targetDir, Path.GetFileName(source)), rescaled);
            written++;
        }

        Console.WriteLine($"  map tables: {objects} objects in {written} files rescaled to " +
                          $"{scale:F3}x vanilla's world");

        RescaleLayerFades(targetDir, cfg);
    }

    /// <summary>A <c>layer={ ... }</c> block. No nesting in this file, so a brace-to-brace match is
    /// the whole block.</summary>
    private static readonly Regex LayerBlock =
        new(@"layer=\{[^}]*\}", RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex FadeStep =
        new(@"(fade_in|fade_out)=(\d+)", RegexOptions.Compiled);

    /// <summary>
    /// Moves the map-table layers' fade thresholds onto this map's zoom ladder.
    ///
    /// This is the other half of the rescale and the one that shows up as a complaint rather than
    /// as a wrong-looking table: <c>fade_in</c>/<c>fade_out</c> in layers.txt are **zoom-step
    /// indices**, and a zoom step is an absolute camera height that does not know how big the map
    /// is. Vanilla's table fades in at step 21 — exactly <c>FLAT_MAP_ZOOM_STEP</c>, so the tabletop
    /// appears precisely when the map goes flat. On a map 0.44x vanilla's the whole world fits at a
    /// much lower camera height, so that same step 21 sits far closer to the top of the useful
    /// range and the table and its surroundings cut out after only a few steps of zooming in.
    ///
    /// Scaling the threshold as a height keeps the ratio between "table appears" and "whole map in
    /// view" at vanilla's, which is what makes the fade happen at the same point of the zoom rather
    /// than at the same absolute altitude.
    ///
    /// Only the map-table layers are touched. Foliage and the rest are objects we ship at vanilla's
    /// absolute size — a tree is the same tree on every map — so their thresholds are already right
    /// and scaling them would fade the trees out too early.
    ///
    /// The units are inferred rather than documented: step 21 matching FLAT_MAP_ZOOM_STEP exactly,
    /// and foliage sitting at 0-9 where it is only ever seen up close, are what pin it. Strong, but
    /// if the tabletop behaves oddly this is the first thing to revert.
    /// </summary>
    private static void RescaleLayerFades(string dir, Config.MapConfig cfg)
    {
        // Copied out of the game folder by LocatorWriter, which runs earlier; there is nothing to
        // do if that has not happened.
        string path = Path.Combine(dir, "layers.txt");
        if (!File.Exists(path)) return;

        int changed = 0;
        string text = LayerBlock.Replace(File.ReadAllText(path), block =>
        {
            if (!block.Value.Contains("\"map_table_layer_", StringComparison.Ordinal))
                return block.Value;

            return FadeStep.Replace(block.Value, fade =>
            {
                int step = int.Parse(fade.Groups[2].Value, CultureInfo.InvariantCulture);
                int scaled = CompatibilityWriter.ScaleZoomStep(step, cfg);
                if (scaled != step) changed++;
                return $"{fade.Groups[1].Value}={scaled}";
            });
        });

        ParadoxText.WriteBom(path, text);
        Console.WriteLine($"  map table layers: {changed} fade thresholds moved onto this map's zoom ladder");
    }

    /// <summary>
    /// One transform onto this map. Null if it is not the ten floats expected, which leaves the
    /// original in place rather than guessing at a layout we do not recognise.
    ///
    /// Position X and Z, and all three mesh scales, are in provinces space — the same space
    /// WORLD_EXTENTS_X/Z and the camera bounds are in, and all of those already scale — so the
    /// table keeps the fraction of the map it covers in vanilla rather than a fixed size.
    ///
    /// Position Y deliberately does not scale. Y is the absolute elevation axis:
    /// <c>WORLD_EXTENTS_Y</c> stays 50 and <c>WATERLEVEL</c> stays 3 on every map, because a
    /// heightmap value has to mean the same height everywhere. These offsets are how far below the
    /// water plane the slab sits, so they mean the same thing at any map size.
    ///
    /// The rotation quaternion is left alone for the obvious reason — it is not a length.
    /// </summary>
    private static string? Rescale(string transform, double scale)
    {
        var parts = transform.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 10) return null;

        var v = new double[10];
        for (int i = 0; i < 10; i++)
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i]))
                return null;

        v[0] *= scale;              // position X
        v[2] *= scale;              // position Z
        for (int i = 7; i < 10; i++) v[i] *= scale;   // mesh scale

        // Vanilla's own layout: ten values on one line, then a newline before the closing quote.
        return string.Join(' ', v.Select(f => f.ToString("F6", CultureInfo.InvariantCulture))) + "\n";
    }
}
