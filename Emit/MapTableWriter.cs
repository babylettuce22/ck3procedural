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

    /// <summary>Vanilla's province-map height, the denominator for the Z and mesh-Z ratio.</summary>
    private const int VanillaProvinceHeight = 4608;

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

        // Z is a distance down the *height* axis, and a generated map is not obliged to be 2:1.
        // Scaling it by MapScale, which is a width ratio, puts the table off-centre by the whole
        // aspect difference on anything squarer or wider than vanilla. WriteCameraDefines already
        // treats PANNING_WIDTH and PANNING_HEIGHT as two separate ratios for exactly this reason.
        double heightScale = (double)cfg.ProvinceHeight / VanillaProvinceHeight;

        // The mesh takes whichever ratio is *larger*, uniformly on all three axes.
        //
        // The table has to cover the whole map, and one ratio per axis is not an option: these are
        // props, and a tabletop stretched to a non-vanilla aspect is a visibly distorted tabletop
        // with oval candles on it. So it scales uniformly and has to be big enough for the demanding
        // axis. A square 5000x5000 map is the case that shows it — the width ratio is 0.271 and the
        // height ratio 0.543, so scaling by width leaves the table covering the map's width and
        // stopping half way down it.
        //
        // Erring large is free: vanilla's table already overhangs its map on every side, and more
        // overhang on the slack axis just reads as more tablecloth.
        double meshScale = Math.Max(scale, heightScale);

        int written = 0, objects = 0, dropped = 0;

        foreach (string source in Directory.GetFiles(sourceDir, "map_table_*.txt"))
        {
            string text = DropProps(File.ReadAllText(source), ref dropped);
            string rescaled = Transform.Replace(text, match =>
            {
                string? scaled = Rescale(match.Groups[1].Value, scale, heightScale, meshScale);
                if (scaled is null) return match.Value;
                objects++;
                return $"transform=\"{scaled}\"";
            });

            // BOM: vanilla's map_table files carry one, and these are gfx script rather than
            // map_data — see the BOM table in the file-formats notes.
            ParadoxText.WriteBom(Path.Combine(targetDir, Path.GetFileName(source)), rescaled);
            written++;
        }

        Console.WriteLine($"  map tables: {objects} objects in {written} files placed at " +
                          $"{scale:F3}x / {heightScale:F3}x vanilla's world, mesh {meshScale:F3}x " +
                          $"({dropped} prop objects dropped)");

        RescaleLayerFades(targetDir, cfg);
    }

    /// <summary>
    /// The <c>entity=</c> substrings that mark an object as clutter rather than furniture.
    ///
    /// Matched on the entity rather than the object name because the entity is the thing being
    /// drawn and it is spelled consistently across the four styles, where the names are not:
    /// ep3's props object is called <c>tabletop_props</c> with no style prefix at all, and ce1
    /// spells its ground props <c>groundprops</c> in the entity but <c>ground_props</c> in the
    /// name. Both substrings below catch every variant, and neither appears in any of the nine
    /// objects we keep — the tabletops, the tablecloths and the floors.
    /// </summary>
    private static readonly string[] PropEntities = ["prop", "candle"];

    /// <summary>
    /// Removes the candles, goblets, coins, chess pieces and ground clutter, keeping the tabletop,
    /// the cloth and the floor.
    ///
    /// These share one layer with the table — <c>map_table_layer_western</c> and its three
    /// siblings — so they share its single <c>fade_in</c>, and there is no per-object fade to tune.
    /// They nonetheless outlive the table on the way in, because the props mesh carries its own
    /// <c>cull_distance = 50000</c> and the candles hang <c>flame_*_entity</c> and
    /// <c>candle_glow</c> off their bones as attachments, which are their own entities and not
    /// governed by the layer at all. Giving them a layer of their own would fix the first and not
    /// reliably the second, so they come out instead: on a generated map they are scenery with no
    /// content riding on them.
    ///
    /// A file that would end up with no objects is left whole rather than emptied — an empty
    /// map_table file means that style has no table, which is worse than keeping its clutter.
    /// </summary>
    private static string DropProps(string text, ref int dropped)
    {
        var kept = new System.Text.StringBuilder();
        int removed = 0;

        int at = 0;
        while (at < text.Length)
        {
            int start = text.IndexOf("object={", at, StringComparison.Ordinal);
            if (start < 0) break;

            int end = BlockEnd(text, start);
            if (end < 0) break;

            string block = text[start..end];
            bool isProp = Entity.Match(block) is { Success: true } m &&
                          PropEntities.Any(p => m.Groups[1].Value.Contains(p, StringComparison.Ordinal));

            if (isProp) removed++;
            else kept.Append(text, at, start - at).Append(block);

            at = end;
        }

        // No objects matched at all: not a file we recognise, so leave it exactly as it was.
        if (removed == 0) return text;
        kept.Append(text, at, text.Length - at);

        string result = kept.ToString();
        if (!result.Contains("object={", StringComparison.Ordinal)) return text;

        dropped += removed;
        return result;
    }

    private static readonly Regex Entity =
        new("entity=\"([^\"]*)\"", RegexOptions.Compiled);

    /// <summary>
    /// The index one past the <c>object={ ... }</c> block starting at <paramref name="start"/>.
    /// Plain brace counting is enough: the only quoted values in these files are names, layers,
    /// entities and the transform, and none of them contains a brace.
    /// </summary>
    private static int BlockEnd(string text, int start)
    {
        int depth = 0;
        for (int i = start; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}' && --depth == 0) return i + 1;
        }
        return -1;
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
    /// This is only half of the fade, and moving it alone was its own bug. The step it has to match
    /// is <c>FLAT_MAP_ZOOM_STEP</c>, and that is a define rather than a layer, so it is scaled by
    /// the same call in <see cref="CompatibilityWriter.WriteCameraDefines"/>. Change one and the
    /// other has to follow, or the tabletop is drawn under a map that has not gone flat yet.
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
    /// Position X and Z are in provinces space — the same space WORLD_EXTENTS_X/Z and the camera
    /// bounds are in, and all of those already scale — so the table keeps the fraction of the map
    /// it covers in vanilla rather than a fixed size. They take separate ratios because the two
    /// axes resize independently.
    ///
    /// Y scales too, and this is a reversal: it used to be held fixed on the grounds that
    /// <c>WORLD_EXTENTS_Y</c> stays 50 and <c>WATERLEVEL</c> stays 3 on every map, so a height has
    /// to mean the same thing everywhere. That argument is about *terrain*, and the tabletop is not
    /// terrain — it sits at y -1 to -20, below the world's own floor at 0, in the separate vertical
    /// regime bounded by <c>MAPTABLE_FLOOR_LEVEL</c> and <c>MAPTABLE_CEILING_LEVEL</c>.
    ///
    /// What made the old split actively wrong is that the mesh scale was being scaled while the
    /// position was not. The offsets are measured from the water plane at y 0, which is where the
    /// map surface is, and the mesh scales about its own origin — so shrinking the slab without
    /// shrinking its offset leaves a thinner slab parked the full vanilla 15 units below a map it
    /// used to meet, with the candles and props floating off it. Scaling every Y uniformly with the
    /// mesh keeps the whole tableau self-similar about the map surface.
    ///
    /// Mesh scale takes one ratio on all three axes rather than one ratio per axis, and takes the
    /// larger of the two so the table covers the map on both — see <c>meshScale</c> in WriteAll.
    /// Y position rides that same ratio, since it is the mesh it has to stay attached to.
    ///
    /// The rotation quaternion is left alone for the obvious reason — it is not a length.
    /// </summary>
    private static string? Rescale(string transform, double scale, double heightScale, double meshScale)
    {
        var parts = transform.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 10) return null;

        var v = new double[10];
        for (int i = 0; i < 10; i++)
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i]))
                return null;

        v[0] *= scale;                                    // position X
        v[1] *= meshScale;                                // position Y, with the mesh
        v[2] *= heightScale;                              // position Z
        for (int i = 7; i < 10; i++) v[i] *= meshScale;   // mesh scale, uniform

        // Vanilla's own layout: ten values on one line, then a newline before the closing quote.
        return string.Join(' ', v.Select(f => f.ToString("F6", CultureInfo.InvariantCulture))) + "\n";
    }
}
