using System.Text;
using System.Text.RegularExpressions;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// Keeps vanilla and DLC script working against a map that shares none of its identifiers.
///
/// The rule learned the hard way: **do not blank vanilla data — re-declare its identifiers.**
/// A missing key is a hard script error, not a warning, because base-game and DLC content
/// hardcodes region and title keys everywhere.
/// </summary>
public static partial class CompatibilityWriter
{
    private static readonly System.Globalization.CultureInfo Invariant =
        System.Globalization.CultureInfo.InvariantCulture;

    /// <summary>
    /// Vanilla's camera extents, and the province map they are authored against. Camera space is
    /// provinces space, so all four scale with <see cref="Config.MapConfig.MapScale"/>.
    ///
    /// Neither panning bound is the map's own size, which is why they are copied rather than
    /// assumed: 9090 is inside a 9216-wide map while 4696 is outside a 4608-tall one. The bound is
    /// on the camera's centre, so the horizontal one stops short of the edge and the vertical one
    /// overshoots to let the view sit past the poles. Scaling vanilla's numbers keeps that
    /// asymmetry instead of inventing a model for it.
    /// </summary>
    private const double VanillaPanningWidth = 9090;
    private const double VanillaPanningHeight = 4696;
    private const int VanillaProvinceHeight = 4608;

    /// <summary>
    /// Vanilla's ZOOM_STEPS ladder, purely so a starting step can be chosen from it. The ladder
    /// itself is left alone — it is shared with five parallel tilt arrays that have to stay the
    /// same length, and camera height is absolute, so the steps mean the same thing on any map.
    /// </summary>
    private static readonly int[] ZoomSteps =
    [
        70, 90, 114, 142, 174, 210, 250, 295, 344, 396, 453, 513, 576, 643, 713, 787, 865, 948,
        1036, 1130, 1233, 1345, 1470, 1609, 1768, 1949, 2159, 2406, 2699, 3050, 3477, 4000, 4649,
        5464, 6500
    ];

    /// <summary>
    /// Vanilla's ZOOM_STEPS_MIN_TILT and ZOOM_STEPS_MAX_TILT — how far the player is allowed to
    /// tilt the camera at each step, in degrees from horizontal, so 90 is straight down and 0 is
    /// looking at the horizon. Both are parallel to <see cref="ZoomSteps"/> and must stay exactly
    /// as long: the engine indexes them by zoom step, and a short array is not a narrower range,
    /// it is whatever is past the end.
    ///
    /// Copied rather than computed because the curves are hand-authored, not a formula — the min
    /// climbs 40 to 55 and then holds, and both drop back at step 34, which is the map-table view
    /// rather than a further-out map view.
    ///
    /// Unlike the panning bounds these are map-size independent. A tilt limit is an angle, and the
    /// angle that looks right at a given camera height does not change because the world under it
    /// got smaller.
    /// </summary>
    private static readonly int[] VanillaMinTilt =
    [
        40, 41, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 52, 53, 54, 54, 54, 55, 55, 55, 55, 55, 55,
        55, 55, 55, 55, 55, 55, 55, 55, 55, 55, 55, 50
    ];

    private static readonly int[] VanillaMaxTilt =
    [
        70, 73, 76, 78, 80, 82, 84, 85, 86, 87, 88, 88, 89, 89, 89, 89, 89, 89, 89, 89, 89, 89, 89,
        89, 89, 89, 89, 89, 89, 89, 89, 89, 89, 89, 89
    ];

    /// <summary>
    /// How far past vanilla each tilt bound is pushed, in degrees, to give the camera more freedom
    /// at both ends than the base game allows.
    ///
    /// Only the bounds move. ZOOM_STEPS_TILT — the angle the camera rests at, and returns to — is
    /// deliberately not written, which leaves vanilla's. That is what makes this change purely
    /// permissive: a widened range still contains vanilla's resting angle at every step, so nothing
    /// looks different until the player actually tilts, and the engine never has to clamp a resting
    /// angle that fell outside its own bounds.
    /// </summary>
    private const int TiltWidening = 15;

    /// <summary>
    /// Floor on the oblique end. Below roughly this the camera is flat enough to look past the map
    /// entirely, and on a generated map — which is smaller than vanilla's — that means the far edge
    /// and the surround plane in frame.
    ///
    /// Slack at the current widening: vanilla's tightest min is 40, so 25 is the real floor. It is
    /// here so that raising <see cref="TiltWidening"/> runs into a documented limit rather than
    /// walking the near steps down to zero.
    /// </summary>
    private const int MinTiltFloor = 20;

    /// <summary>
    /// Ceiling on the top-down end, and it is 89 rather than 90 on purpose. At exactly 90 the view
    /// direction is parallel to the world up axis and the camera basis is degenerate — vanilla caps
    /// every step at 89 for the same reason. This bound binds hard: vanilla is already at 89 from
    /// step 12 out, so widening the top end only buys anything at close zoom.
    /// </summary>
    private const int MaxTiltCeiling = 89;

    // The handoff bias moved to MapConfig.FlatMapHandoffBias, which carries the whole argument for
    // it — most of all that ScaleZoomStep is the only place it may be applied, because the map
    // going flat and the map table fading in are a pair that has to move together.

    /// <summary>Vanilla's START_ZOOM_STEP.</summary>
    private const int VanillaStartZoomStep = 33;

    /// <summary>
    /// Vanilla's ZOOM_AUDIO_PARAMETER_SCALE. The audio system is handed camera height times this,
    /// so scaling the ladder without scaling this inversely would tell it the camera is lower than
    /// vanilla ever reports.
    /// </summary>
    private const double VanillaZoomAudioScale = 0.1;

    /// <summary>
    /// The zoom ladder moved onto this map — vanilla's heights times <see cref="MapConfig.MapScale"/>.
    ///
    /// **Why this is here: the indices.** Vanilla ships around twenty-five
    /// <c>*_VISIBLE_ZOOM_STEPS</c> settings — forts, units, combats, holdings, realm capitals — plus
    /// <c>MAX_PAN_TO_ZOOM_STEP</c> and <c>WATER_BORDERS_ZOOM_STEP</c>, all of them *indices* into
    /// this ladder, and none of them overridden here. While the ladder kept vanilla's absolute
    /// heights on a smaller map, every one of those was mis-framed: things appeared and vanished at
    /// the wrong share of the zoom. Moving the heights fixes all of them at once, for free, and is
    /// why <c>NearestZoomStep</c>, <c>ViewScale</c> and the height-rescaling half of
    /// <see cref="ScaleZoomStep"/> could be deleted rather than fixed.
    ///
    /// **What it is not here for.** It was built to fix terrain visibly stopping mid-morph at
    /// maximum zoom on a small map, and it did not. That theory came from real measurements —
    /// on a 4607-wide map the live pipeline reports <c>NormQuadtreeToWorld</c> 8191 and
    /// <c>QuadtreeLeafNodeScale</c> 0.00195, exactly 1/512, so the quadtree leaf is 16 world units
    /// where vanilla's 9215-wide map gets 16384/512 = 32 — and the inference that LOD therefore
    /// ran out of ladder was wrong. Captured in game afterwards: the finest node actually rendered
    /// is <c>NodeScale</c> 256, a 32 world-unit node, which at 2 heightmap px per world unit puts
    /// one vertex on every texel; the engine declines to go finer because there is nothing left to
    /// resolve. And <c>LodLerpFactor</c> read 2526 of 65535 — the morph was 96% settled.
    ///
    /// The artefact turned out to be resolution-dependent and largely CK3's own: it is faintly
    /// present on vanilla's map under the same widened tilt, and absent from this generator's
    /// output at vanilla dimensions. Everything in the renderer that produces it works in absolute
    /// world units — 32-unit nodes, a 0.8-unit normal probe, a 6-unit border ribbon lifted 0.02 —
    /// while a smaller map resamples the same world into fewer pixels, so terrain features shrink
    /// toward those fixed scales. <see cref="MapConfig.ScaleReliefWithMapSize"/> corrects the
    /// vertical half of that and nothing can correct the horizontal half but more pixels.
    ///
    /// All 35 entries, and strictly increasing: the five tilt and stick arrays are indexed in
    /// parallel with it, and two steps landing on the same height after rounding would give the
    /// zoom two rungs it cannot tell apart.
    /// </summary>
    private static int[] ScaledZoomSteps(Config.MapConfig cfg)
    {
        var ladder = new int[ZoomSteps.Length];
        int last = 0;

        for (int i = 0; i < ZoomSteps.Length; i++)
        {
            int height = Math.Max(MinNearZoomHeight, (int)Math.Round(ZoomSteps[i] * cfg.MapScale));
            ladder[i] = last = Math.Max(height, last + 1);
        }

        return ladder;
    }

    /// <summary>
    /// Floor on the near end of the scaled ladder, in world units, so the camera cannot be zoomed
    /// into the ground on a small map.
    ///
    /// <c>NCamera.ZNEAR</c> is 10, and at the most oblique angle this writer allows — step 0's
    /// widened minimum tilt, 25 degrees — a step of S puts the camera S*sin(25) = 0.42*S above the
    /// point it looks at. 40 gives 16.9 world units, clear of the near plane with terrain relief to
    /// spare. Unfloored, a 0.222-scale map would open step 0 at 16 world units, which is 6.8 above
    /// ground: the near plane would start cutting away the hillside in front of the camera.
    ///
    /// It binds only below half scale — at MapScale 0.5 step 0 lands on 35 and is nudged to 40, at
    /// vanilla size it never applies. And it costs nothing this scaling is kept for: the flat-map
    /// handoff, the start view, and the two dozen vanilla <c>*_VISIBLE_ZOOM_STEPS</c> indices that
    /// ride on the ladder all live at the far end, well above the floor.
    ///
    /// The monotonic pass below is what absorbs it. Several near steps can clamp to the same height
    /// and then get pushed apart by one unit each, which is a squashed close end rather than a
    /// broken one — the alternative, clamping the *scale factor*, would drag the far end off
    /// vanilla's framing to protect the near end, and the far end is the half that matters.
    /// </summary>
    private const int MinNearZoomHeight = 40;

    /// <summary>
    /// Vanilla's FLAT_MAP_ZOOM_STEP — the step at which the terrain gives way to the paper map on
    /// the tabletop.
    ///
    /// This has to be overridden for the same reason every other zoom step does: a step is an
    /// absolute camera height, and on a smaller map the whole world is in view far below step 21.
    ///
    /// It is specifically load-bearing for <see cref="MapTableWriter"/>. Vanilla's map-table layers
    /// fade in at exactly 21, so the tabletop appears on the same frame the map goes flat. Scaling
    /// the layer fade — which MapTableWriter does — while leaving this at 21 pulls the two apart and
    /// leaves a window of nine-odd zoom steps where the physical table is drawn under a map that is
    /// still 3D terrain. That is worse than either error alone, so the pair moves together.
    /// </summary>
    private const int VanillaFlatMapZoomStep = 21;

    /// <summary>
    /// Vanilla's SURROUND_MAP_INNER_RECT, in the order the define writes it:
    /// <c>x-start, z-start, x-end, z-end</c>.
    ///
    /// World-space XZ, on vanilla's 9216x4608 map. gfx/FX/surroundmap.shader settles that: the
    /// surround is a flat plane whose vertices arrive as <c>float2 position</c> and are lifted
    /// straight to <c>float3( position.x, FlatMapHeight, position.y )</c>, with the mask sampled at
    /// <c>position / MapSize</c>. The rect is not in the shader's constant buffer, so it is what
    /// builds that mesh on the CPU — and the inner one is the hole in the middle where the map
    /// shows through.
    ///
    /// Left at vanilla's numbers the hole is cut for a world twice ours across: on a 5760x2848 map
    /// the z-end of 3700 is 850 units past the southern edge. The surround plane then overlaps the
    /// map plane — both at FLAT_MAP_HEIGHT — and the overlap has hard, axis-aligned rectangular
    /// edges, which is what the clipped map corners in the flat map view actually are.
    ///
    /// The component semantics are genuinely unclear and are not worth guessing at: 500 and 500 on
    /// x read as insets from either edge, while 1000 and 3700 on z read as absolute coordinates
    /// spanning the middle 59% of the map. It does not matter. Every reading is a length along one
    /// axis of a 9216x4608 world, linear in that dimension with no constant term, so scaling the x
    /// pair by the width ratio and the z pair by the height ratio reproduces vanilla's geometry
    /// proportionally under all of them — and reduces to vanilla's own numbers exactly when the map
    /// is vanilla-sized.
    /// </summary>
    private static readonly double[] VanillaSurroundInnerRect = [500.0, 1000.0, 500.0, 3700.0];

    /// <summary>
    /// Vanilla's government list, plus ours.
    ///
    /// A government type declared in <c>common/governments</c> is NOT registered until its key also
    /// appears in <c>NGovernment.GOVERNMENT_TYPES</c>. Miss it and the game logs a wall of
    /// "Could not find the preregistered modifier type 'x_government_opinion'" — one per contract
    /// modifier — and the government half-exists thereafter. ck3-tiger does not catch this: the
    /// script files are all valid, and the missing piece is an engine registration list.
    ///
    /// Read from the installed game rather than hardcoded. The list is thirty-odd entries that
    /// Paradox adds to every major patch, and a stale copy would silently *remove* whichever
    /// governments were added since — a far worse failure than the one it fixes.
    /// </summary>
    private static string GovernmentTypes(string gameDir, Config.MapConfig cfg)
    {
        string source = Path.Combine(gameDir, "common", "defines", "00_defines.txt");
        if (!cfg.EnableWilderness || !File.Exists(source)) return "";

        string text = File.ReadAllText(source);

        int start = text.IndexOf("GOVERNMENT_TYPES", StringComparison.Ordinal);
        if (start < 0) return "";

        int open = text.IndexOf('{', start);
        int close = text.IndexOf('}', open);
        if (open < 0 || close < 0) return "";

        var entries = System.Text.RegularExpressions.Regex
            .Matches(text[(open + 1)..close], "\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value)
            .ToList();

        if (entries.Count == 0) return "";

        // Both of ours, and both from BaseFilesToCopy/Wilderness/common/governments — which is why
        // they are gated on EnableWilderness above. colony_government was missing here while its
        // file shipped, so setup.log carried twelve "Could not find the preregistered modifier
        // type 'colony_government_opinion'" errors and the government half-existed.
        entries.Add("wilderness_government");
        entries.Add("colony_government");

        var b = new JominiBuilder();
        b.Blank();
        b.Comment("""
                  Vanilla's list, read from the installed game, plus the governments we add.
                  A government absent from here is never registered, whatever common/governments says.
                  """);

        using (b.Block("NGovernment"))
        using (b.Block("GOVERNMENT_TYPES"))
            foreach (string entry in entries) b.Token($"\"{entry}\"");

        Console.WriteLine($"  defines: GOVERNMENT_TYPES {entries.Count} entries "
                          + $"({entries.Count - 2} vanilla + wilderness + colony)");

        return b.ToString();
    }

    /// <summary>
    /// Overrides NJominiMap so the engine's world size matches the province map we actually
    /// ship. This is not optional and it is easy to miss.
    ///
    /// WORLD_EXTENTS_X/Z are in *provinces-map* space and vanilla's values (9215 / 4607) are
    /// size-minus-one for its 9216x4608 map. Leaving them alone means CK3 addresses a world
    /// several times larger than our provinces.png, so every province centroid, locator,
    /// pathfinding node and terrain lookup lands in the wrong place — with nothing logged,
    /// because none of it is a script error.
    /// </summary>
    public static void WriteDefines(string modDir, string gameDir, Config.MapConfig cfg)
    {
        string dir = Path.Combine(modDir, "common", "defines");
        Directory.CreateDirectory(dir);

        // Sorts last on purpose. Defines are merged across every file in the directory and the
        // last one loaded wins, so a baseline file like ck2rpg's 01_gen_defines.txt would
        // otherwise silently override our world size with the template map's.

        // WORLD_EXTENTS_Y and WATERLEVEL stay at vanilla's values on every map size.
        //
        // A heightmap value has to mean the same height everywhere, so the engine-side pair stays
        // put and the map-size correction is applied to the terrain instead. These were briefly
        // scaled by map size, which was an attempt to cancel out terrain that came out too steep
        // on small maps — two errors pointing opposite ways rather than one fix.
        //
        // The terrain side is MapGen.HeightmapNormalizer.CompressRelief, reached through
        // MapConfig.ScaleReliefWithMapSize. It used to be MapConfig.SlopeScaleFor, which 38b5fe8
        // deleted along with terrain generation — so between then and 2026-08-22 this comment
        // promised a correction that no longer existed, and nothing was scaling the terrain at
        // all. That is worth knowing if the pair here is ever revisited: the argument for keeping
        // them constant only holds while something else is doing the scaling.
        //
        // The ratio is load-bearing either way, though not for the reason this comment gave until
        // 2026-08-23. Vanilla's own note on the define reads `WATERLEVEL = 3 ### 0.06 in 0-1, 19 in
        // 0-255`, and its two halves contradict each other: 3/50 is 0.06, which is 15.3/255, not 19.
        // RenderDoc settled which half is true — the water vertex shader's `_WaterHeight` reads 3.0,
        // so the sea is drawn at MapDataWriter.WaterPlane16 (3932), *below* MapDataWriter.WaterLevel16
        // (4883). 19/255 is a separate convention, for where land begins in the file, and the gap
        // between the two is what vanilla renders as beach.
        //
        // So what this pair fixes is where the sea is drawn against every height in the map. Move one
        // without the other and the waterline slides against terrain both hypsometric curves have
        // already placed.
        const string extentY = "50";
        const string waterLevel = "3";

        ParadoxText.WriteBom(Path.Combine(dir, "zz_generated_defines.txt"),
            $$"""
              # World size must match map_data/provinces.png, not vanilla's map.
              NJominiMap = {
              	WORLD_EXTENTS_X = {{cfg.ProvinceWidth - 1}}
              	WORLD_EXTENTS_Y = {{extentY}}
              	WORLD_EXTENTS_Z = {{cfg.ProvinceHeight - 1}}
              	WATERLEVEL = {{waterLevel}}
              }
              {{EndDate(cfg)}}{{GovernmentTypes(gameDir, cfg)}}
              """);

        Console.WriteLine($"  defines: WORLD_EXTENTS {cfg.ProvinceWidth - 1} x {extentY} x {cfg.ProvinceHeight - 1}, " +
                          $"WATERLEVEL {waterLevel} (vanilla 9215 x 50 x 4607, 3)");

        WriteCameraDefines(modDir, cfg);
    }

    /// <summary>
    /// Overrides the two blocks that place the map and the camera in world space, so both are
    /// bounded by the map we ship rather than by vanilla's.
    ///
    /// NGraphics carries the pair that decide where the flat map begins:
    /// <c>FLAT_MAP_ZOOM_STEP</c>, which has to travel with the map-table layer fades
    /// <see cref="MapTableWriter"/> moves, and <c>SURROUND_MAP_INNER_RECT</c>, the world-space
    /// hole the surround plane leaves for the map to show through.
    ///
    /// Written into <c>common/defines/graphic/</c>, next to vanilla's own 00_graphics.txt, rather
    /// than alongside our NJominiMap override one directory up. Defines merge across the whole
    /// tree and the last file loaded wins, so being in the same directory is what makes "sorts
    /// after 00_graphics.txt" a fact about one directory listing instead of an assumption about
    /// how the loader walks subdirectories.
    ///
    /// START_LOOK_AT is the reason this matters beyond tidiness. Vanilla opens the camera at
    /// { 5000 0 2300 }, which is the middle of a 9216x4608 map and *off* every smaller one — at
    /// the standard 3072x1536 province raster it is past the eastern edge by more than half the
    /// map's width. It is set to the centre here rather than scaled from vanilla's, whose 0.54
    /// along x is Europe rather than anything a generated map has.
    /// </summary>
    private static void WriteCameraDefines(string modDir, Config.MapConfig cfg)
    {
        string dir = Path.Combine(modDir, "common", "defines", "graphic");
        Directory.CreateDirectory(dir);

        // START_LOOK_AT is deliberately not reverted under VanillaCamera — see that setting.
        double panWidth = cfg.VanillaCamera
            ? VanillaPanningWidth
            : Math.Round(cfg.Scaled(VanillaPanningWidth));
        double panHeight = cfg.VanillaCamera
            ? VanillaPanningHeight
            : Math.Round(VanillaPanningHeight * cfg.ProvinceHeight / VanillaProvinceHeight);

        double lookX = cfg.ProvinceWidth / 2.0;
        double lookZ = cfg.ProvinceHeight / 2.0;

        // Vanilla's own indices now, both of them. With the ladder scaled they already mean the
        // same share of the map in view that they mean in vanilla — which is what the old
        // height-rescaling was trying and failing to reproduce.
        int startStep = VanillaStartZoomStep;
        int flatStep = ScaleZoomStep(VanillaFlatMapZoomStep, cfg);

        var ladder = cfg.VanillaCamera ? ZoomSteps : ScaledZoomSteps(cfg);

        string zoomBlock = cfg.VanillaCamera
            ? ""
            : $$"""

                	# The ladder scaled onto this map, because every zoom-step *index* in the game rides on
                	# it — the two dozen *_VISIBLE_ZOOM_STEPS, MAX_PAN_TO_ZOOM_STEP, WATER_BORDERS_ZOOM_STEP,
                	# the flat-map handoff. Left at vanilla's absolute heights on a smaller map, all of them
                	# frame the wrong share of the zoom. The near end is floored so the camera cannot end up
                	# inside the ground.
                	ZOOM_STEPS = { {{string.Join(" ", ladder)}} }
                	ZOOM_AUDIO_PARAMETER_SCALE = {{(VanillaZoomAudioScale / cfg.MapScale).ToString("F4", Invariant)}}
                """;

        // Two blocks, because the four keys do not live in one. FLAT_MAP_ZOOM_STEP and
        // SURROUND_MAP_INNER_RECT are NGraphics; the panning bounds and the start view are NCamera.
        // Writing either into the other block is silently ignored — it parses, it merges, and it
        // governs nothing.
        double widthRatio = cfg.MapScale;
        double heightRatio = (double)cfg.ProvinceHeight / VanillaProvinceHeight;

        string innerRect = string.Join(" ", VanillaSurroundInnerRect.Select((v, i) =>
            (v * (i % 2 == 0 ? widthRatio : heightRatio)).ToString("F1", Invariant)));

        // Array defines are replaced whole, not merged element by element, so both rows go out at
        // full length even though the max row only changes at its near end.
        string minTilt = string.Join(" ", VanillaMinTilt.Select(v => Math.Max(v - TiltWidening, MinTiltFloor)));
        string maxTilt = string.Join(" ", VanillaMaxTilt.Select(v => Math.Min(v + TiltWidening, MaxTiltCeiling)));

        ParadoxText.WriteBom(Path.Combine(dir, "zz_generated_graphics.txt"),
            $$"""
              # Map geometry and camera extents must match map_data/provinces.png, not vanilla's map.
              NGraphics = {
              	FLAT_MAP_ZOOM_STEP = {{flatStep}}
              	SURROUND_MAP_INNER_RECT = { {{innerRect}} }
              }

              NCamera = {
              	PANNING_WIDTH = {{panWidth.ToString(Invariant)}}
              	PANNING_HEIGHT = {{panHeight.ToString(Invariant)}}
              	START_LOOK_AT = { {{lookX.ToString("F1", Invariant)}} 0 {{lookZ.ToString("F1", Invariant)}} }
              	START_ZOOM_STEP = {{startStep}}
              {{zoomBlock}}
              	# Tilt bounds widened {{TiltWidening}} degrees past vanilla at both ends. Degrees from
              	# horizontal: 90 is straight down, 0 is the horizon. ZOOM_STEPS_TILT is left at
              	# vanilla's, so the camera still rests where the base game puts it.
              	ZOOM_STEPS_MIN_TILT = { {{minTilt}} }
              	ZOOM_STEPS_MAX_TILT = { {{maxTilt}} }
              }

              """);

        if (cfg.VanillaCamera)
            Console.WriteLine("  camera: VANILLA CAMERA DIAGNOSTIC — vanilla zoom ladder, panning "
                              + "and flat-map handoff. Tilt stays widened, start view stays on this "
                              + "map's centre. Not a release setting.");

        Console.WriteLine($"  camera: panning {panWidth} x {panHeight}, look at " +
                          $"{lookX:F0},{lookZ:F0}, zoom step {startStep} ({ladder[startStep]}), " +
                          $"flat map at step {flatStep} ({ladder[flatStep]}) " +
                          $"(vanilla 9090 x 4696, 5000,2300, 33, 21)");

        if (!cfg.VanillaCamera)
            Console.WriteLine($"  camera: zoom ladder scaled to {ladder[0]}..{ladder[^1]} world units "
                              + $"(vanilla {ZoomSteps[0]}..{ZoomSteps[^1]}), so every vanilla "
                              + "zoom-step index — visibility ranges, pan-to, flat map — frames the "
                              + $"same share of the map it does there{(ladder[0] == MinNearZoomHeight ? "; near end floored" : "")}");
        Console.WriteLine($"  surround: inner rect {innerRect} (vanilla 500.0 1000.0 500.0 3700.0)");
        Console.WriteLine($"  camera: tilt widened {TiltWidening} deg, "
                          + $"closest step {Math.Max(VanillaMinTilt[0] - TiltWidening, MinTiltFloor)}"
                          + $"-{Math.Min(VanillaMaxTilt[0] + TiltWidening, MaxTiltCeiling)}, "
                          + $"furthest {Math.Max(VanillaMinTilt[^1] - TiltWidening, MinTiltFloor)}"
                          + $"-{Math.Min(VanillaMaxTilt[^1] + TiltWidening, MaxTiltCeiling)} "
                          + $"(vanilla {VanillaMinTilt[0]}-{VanillaMaxTilt[0]}, "
                          + $"{VanillaMinTilt[^1]}-{VanillaMaxTilt[^1]})");
    }

    /// <summary>
    /// A zoom-ladder index authored against vanilla's map, moved onto this one — which, now that
    /// <see cref="ScaledZoomSteps"/> moves the ladder itself, is just
    /// <see cref="MapConfig.FlatMapHandoffBias"/> and nothing else. An index already means the same
    /// share of the map in view here that it means in vanilla.
    ///
    /// It used to convert the index to a camera height, scale that, and find the nearest step back.
    /// That reproduced vanilla's *framing* while leaving vanilla's absolute heights in place, which
    /// is exactly the mistake this whole area was built on: it is the heights that have to move,
    /// because the terrain LOD reads them against a quadtree that scales with the map.
    ///
    /// Indices outside the ladder come back untouched. That is not defensiveness: vanilla's map
    /// table layers use <c>fade_out=80</c> against a 35-step ladder, which is how the format spells
    /// "never", and biasing it would land it on a real step and start fading the table out.
    /// </summary>
    internal static int ScaleZoomStep(int step, Config.MapConfig cfg)
        => step < 0 || step >= ZoomSteps.Length || cfg.VanillaCamera
            ? step
            : Math.Clamp(step + cfg.FlatMapHandoffBias, 0, ZoomSteps.Length - 1);

    /// <summary>
    /// Re-declares every vanilla empire, kingdom, duchy and holy-order title as a landless
    /// titular, so base-game and DLC script that hardcodes those keys still resolves.
    ///
    /// A missing title key is not a warning. It produces `title_links.cpp:214 Failed to fetch a
    /// valid landed title` once per reference (~12,900 of them) and, more dangerously,
    /// `coat_of_arms_dynamic_definitions.cpp:44 Could not find title 'k_england'` — the coat of
    /// arms system then holds a null title while it builds arms for the world.
    ///
    /// Only e_/k_/d_/h_ are emitted. Counties and baronies **cannot** be titular: they must own
    /// land, so the only way to satisfy a hardcoded c_/b_ reference is to name a real generated
    /// title after it, which is a separate piece of work.
    ///
    /// Every landless title needs `capital`, or CK3 logs "has no capital defined. Needed to
    /// ensure proper on-map location".
    /// </summary>
    public static void WriteVanillaTitulars(string modDir, string gameDir, List<Title> empires)
    {
        string source = Path.Combine(gameDir, "common", "landed_titles");
        if (!Directory.Exists(source)) return;

        var counties = Titles.Flatten(empires).Where(t => t.Tier == "c").ToList();
        if (counties.Count == 0) return;

        var generated = Titles.Flatten(empires).Select(t => t.Key).ToHashSet(StringComparer.Ordinal);

        // Paradox identifiers are not [a-z_0-9]: title keys carry hyphens and uppercase
        // (e_caspian-pontic_steppe, c_SUM_bangka-belitung, b_al-fayyum). A stricter pattern
        // silently drops keys, and every dropped key stays dangling.
        var keyPattern = new Regex(@"^\s*([ekdh]_[A-Za-z_0-9&-]+)\s*=\s*\{", RegexOptions.Multiline);

        var keys = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (string path in Directory.GetFiles(source, "*.txt"))
            foreach (Match m in keyPattern.Matches(File.ReadAllText(path)))
            {
                string key = m.Groups[1].Value;
                if (generated.Contains(key) || !seen.Add(key)) continue;
                keys.Add(key);
            }

        if (keys.Count == 0) return;

        // One county carries every shim rather than 1,459 counties carrying one each. `capital`
        // confers no ownership — it is only where CK3 draws the title and roots its arms — so
        // spreading them did nothing but decorate real counties with foreign titles in tooltips
        // and in the coat of arms system. Concentrating them makes the shims visibly inert.
        string shimCapital = counties[0].Key;

        var jb = new JominiBuilder();
        jb.Comment("""
                   Vanilla e_/k_/d_/h_ keys re-declared as landless titulars.
                   Base-game and DLC content hardcodes these; a missing key is a hard error,
                   and the coat of arms system dereferences the null it gets back.

                   These exist to be *referenced*, never to be held. The creation gates below are
                   the first half of that; common/on_action/00_generated_titular_guard.txt is the
                   half that actually guarantees it.
                   """);
        jb.Blank();

        for (int i = 0; i < keys.Count; i++)
        {
            var (r, g, b) = MapDataWriter.ProvinceColor(i + 1);

            using (jb.Block(keys[i]))
            {
                jb.Field("landless", "yes");
                jb.Field("capital", shimCapital);
                jb.Color("color", r, g, b);

                // A shim owns no de jure territory, which is exactly what makes it cheap to create:
                // the engine's "do you hold enough of the de jure land" test runs against an empty set.
                // These four close every route into a domain that does not go through a scripted
                // effect — the creation UI, partition succession, inherited claims, and the AI's
                // primary-title pick for the frame a shim might exist before the guard strips it.
                jb.Inline("can_create", "always = no");
                jb.Inline("can_create_on_partition", "always = no");
                jb.Field("no_automatic_claims", "yes");
                jb.Inline("ai_primary_priority", "add = -1000");

                // Deliberately absent: delete_on_destroy. It defaults to no and has to stay no — the
                // guard destroys these titles, and deleting the object would take the key with it,
                // undoing the ~12,900 references this whole file exists to resolve.
            }
        }

        string dir = Path.Combine(modDir, "common", "landed_titles");
        Directory.CreateDirectory(dir);
        ParadoxText.WriteBom(Path.Combine(dir, "zz_vanilla_titulars.txt"), jb.ToString());

        WriteTitularGuard(modDir, keys);

        Console.WriteLine($"  titulars: {keys.Count} vanilla e_/k_/d_/h_ keys re-declared as landless");
    }

    /// <summary>
    /// The half of the shim problem the landed_titles file cannot solve: a guarantee that a
    /// re-declared vanilla title never stays in a ruler's domain.
    ///
    /// The creation gates in <see cref="WriteVanillaTitulars"/> only close the routes the engine
    /// owns. Script walks straight past them — `create_title_and_vassal_change` and
    /// `set_title_holder` hand a title over without consulting `can_create` — and vanilla is full
    /// of decisions that do exactly that once a region check passes. Those checks pass for free
    /// against a shim: it has no de jure territory, so `completely_controls = title:d_latium` and
    /// the fourteen siblings that make up restore_roman_empire_decision's entire `is_valid` are
    /// each asking whether a ruler holds every county in an empty set.
    ///
    /// Chasing that through 430 vanilla decisions plus every future DLC one is a losing game, so
    /// the guard keys on the title instead of the grant: every shim is stamped with a variable at
    /// game start, and any gain of a stamped title destroys it. Paths nobody has read yet are
    /// covered by construction.
    ///
    /// `destroy_title` is safe for what the shims exist to do. `delete_on_destroy` defaults to no,
    /// so the title is unlanded while the database object survives — `title:e_byzantium` still
    /// resolves for the ~12,900 hardcoded references and for the coat of arms system.
    /// </summary>
    private static void WriteTitularGuard(string modDir, List<string> keys)
    {
        string dir = Path.Combine(modDir, "common", "on_action");
        Directory.CreateDirectory(dir);

        var b = new JominiBuilder();
        b.Comment("""
                  Vanilla e_/k_/d_/h_ shims may be referenced. They may never be held.
                  See common/landed_titles/zz_vanilla_titulars.txt for what they are and why.
                  """);
        b.Blank();

        // on_title_gain already covers inheritance and usurpation — vanilla's own copy branches on
        // flag:inheritance and flag:usurped inside it — but the two specialised on_actions are
        // hooked as well. The check is one variable read on one title; a missed strip costs more
        // than a redundant one.
        foreach (string hook in new[] { "on_title_gain", "on_title_gain_inheritance", "on_title_gain_usurpation" })
        {
            using (b.Block(hook))
            using (b.Block("on_actions"))
                b.Token("gen_strip_vanilla_titular");

            b.Blank();
        }

        using (b.Block("on_game_start"))
        using (b.Block("on_actions"))
            b.Token("gen_mark_vanilla_titulars");

        b.Blank();

        b.Comment("root = the new holder, scope:title = the title that changed hands.");

        using (b.Block("gen_strip_vanilla_titular"))
        using (b.Block("effect"))
        using (b.Block("if"))
        {
            using (b.Block("limit"))
                b.Inline("scope:title", "has_variable = gen_vanilla_titular");

            b.Field("destroy_title", "scope:title");
        }

        b.Blank();

        // The holder clause is not redundant with the strip hook. Ordering between on_game_start
        // on_actions from different files is not defined, so a shim could in principle be granted
        // before this runs and never be seen by a gain hook again. Checking each title's holder as
        // it is stamped closes that window without iterating a list while mutating it.
        using (b.Block("gen_mark_vanilla_titulars"))
        using (b.Block("effect"))
            foreach (string key in keys)
                b.Inline($"title:{key}", "set_variable = gen_vanilla_titular "
                    + "if = { limit = { exists = holder } holder = { destroy_title = prev } }");

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_titular_guard.txt"), b.ToString());

        Console.WriteLine($"  titular guard: {keys.Count} shims stamped, stripped on any title gain");
    }

    /// <summary>
    /// The gates a player can see a decision through, in the order they are reported.
    ///
    /// <c>ai_potential</c> is deliberately not one of them: it decides whether the AI bothers
    /// evaluating a decision, and an AI that evaluates one it can never pass costs nothing the
    /// player can see.
    /// </summary>
    private static readonly string[] DecisionGates =
        ["is_shown", "is_valid", "is_valid_showing_failures_only"];

    /// <summary>
    /// The illustration every stub declares.
    ///
    /// ck3-tiger treats <c>picture</c> as required even though nine vanilla decisions ship without
    /// one, and a stub that shows to nobody never draws it, so the generic vanilla illustration is
    /// as good as the right one and costs no artwork.
    /// </summary>
    private const string StubPicture = "gfx/interface/illustrations/decisions/decision_misc.dds";

    /// <summary>A vanilla decision hidden by <see cref="WriteDecisionBlocks"/>, and why.</summary>
    private readonly record struct BlockedDecision(
        string Key, string Origin, string Gate,
        string? Title, string? Desc, string? SelectionTooltip, string? ConfirmText);

    /// <summary>
    /// Hides the vanilla decisions whose gate is <c>completely_controls</c>.
    ///
    /// <see cref="WriteVanillaTitulars"/> re-declares every vanilla e_/k_/d_/h_ key as a landless
    /// shim, which is what keeps ~12,900 hardcoded references from being hard errors. The cost is
    /// that a shim owns no de jure territory, and <c>completely_controls = title:d_sardinia</c>
    /// against an empty set of counties is *vacuously true*. So is every sibling in
    /// secure_mediterranean_decision's is_shown, so the decision offers itself to every landed
    /// character on a map that has no Mediterranean.
    ///
    /// <see cref="WriteTitularGuard"/> does not help here and never could: it guarantees a shim is
    /// never *held*, and completely_controls does not ask who holds the title.
    /// <see cref="WriteGeographicalRegions"/> leaves the same hole one size smaller — a
    /// non-graphical region is re-declared with exactly one county because it needs at least one
    /// member to register at all, so completely_controls_region is true for whoever holds that one.
    ///
    /// The hand-kept blanks in BaseFilesToCopy/Core/common/decisions cover ten files, all of them
    /// top level. A CK3 full-file override only fires at the matching relative path, so they can
    /// never reach common/decisions/dlc_decisions/, where FP1, FP2, FP3, EP3 and TGP keep theirs.
    /// Blanking is too blunt for the mixed files anyway: 80_major_decisions.txt is six
    /// geography-locked decisions and seventeen the generated map wants — found_kingdom,
    /// found_empire, found_duchy, the government conversions.
    ///
    /// So this is a single-object override instead: one file re-declaring just the offending keys
    /// as stubs that show to nobody, leaving every other decision in the same vanilla file alone.
    /// Generated from the installed game folder rather than hand-kept, so a patch that adds,
    /// renames or moves one is picked up on the next run.
    ///
    /// **Where that file goes is the whole trick, and "last asciibetical file wins" is not the
    /// rule.** CK3 walks a database folder level by level: every file in a directory in
    /// asciibetical order, *then* every subdirectory in asciibetical order, recursively. Depth
    /// beats filename, so a top-level `zzz_` file loads before `dlc_decisions/03_fp2_decisions.txt`
    /// and vanilla wins. Measured, not assumed — the first cut of this shipped at the top level and
    /// database_conflicts.log reported vanilla overriding *us* for all fourteen decisions under
    /// dlc_decisions/, secure_mediterranean_decision among them, while the four it beat were the
    /// four whose vanilla file sits at the top level.
    ///
    /// Hence <c>dlc_decisions/zzz_generated/</c>: vanilla's only subdirectory here is
    /// dlc_decisions, its own subdirectories end at `tgp`, and it never nests deeper than two, so
    /// a `zzz_`-named folder at that depth is the last thing the walker reaches. If a future patch
    /// ever adds one that sorts later, database_conflicts.log is what says so — it names the winner
    /// for every contested key, and it is written on every launch.
    ///
    /// Deliberately narrow: completely_controls only. A vanilla <c>title:</c> reference anywhere
    /// in is_shown looks like the same signal and is not — found_university_decision names
    /// twenty-four baronies so that founding at a famous one reads differently, and
    /// recruit_terrain_specialist_decision names two TGP office titles. Both are decisions a
    /// generated map wants. Vacuity is the bug; mentioning a vanilla key is not.
    /// </summary>
    public static void WriteDecisionBlocks(string modDir, string gameDir)
    {
        string source = Path.Combine(gameDir, "common", "decisions");
        if (!Directory.Exists(source)) return;

        var blocked = new List<BlockedDecision>();
        int scanned = 0;

        foreach (string path in Directory.GetFiles(source, "*.txt", SearchOption.AllDirectories))
        {
            string origin = Path.GetRelativePath(source, path).Replace('\\', '/');

            foreach (var (key, body) in ScanDecisions(File.ReadAllText(path)))
            {
                scanned++;

                string? gate = DecisionGates.FirstOrDefault(
                    g => SubBlock(body, g).Contains("completely_controls", StringComparison.Ordinal));

                if (gate is null) continue;

                // Vanilla's own loc keys, carried so the stub keeps them. Without them the
                // defaults apply — <key>, <key>_desc, <key>_tooltip, <key>_confirm — and for the
                // decisions that named their own, those defaults are keys nothing ever wrote.
                blocked.Add(new BlockedDecision(key, origin, gate,
                    LocField(body, "title"),
                    LocField(body, "desc"),
                    LocField(body, "selection_tooltip"),
                    LocField(body, "confirm_text")));
            }
        }

        if (blocked.Count == 0) return;

        var jb = new JominiBuilder();
        jb.Comment($"{blocked.Count} of {scanned} vanilla decisions, re-declared as stubs that show to nobody.");
        jb.Comment("""

                   Each one gates on completely_controls against a title with no de jure counties
                   (common/landed_titles/zz_vanilla_titulars.txt) or a region re-declared with one
                   (map_data/geographical_regions). Both make the check vacuously true, so the
                   decision offers itself on a map that has none of the places it names.

                   This file's PATH is load-bearing, not just its name. CK3 loads every file in a
                   directory asciibetically, then recurses into its subdirectories the same way, so
                   depth beats filename: a top-level zzz_ file loses to dlc_decisions/*. Sitting in
                   the last-sorting folder of the deepest level vanilla uses is what makes this the
                   last definition loaded. database_conflicts.log names the winner for every key.
                   """);
        jb.Blank();

        foreach (var d in blocked)
        {
            jb.Comment($"{d.Origin}: {d.Gate}");

            using (jb.Block(d.Key))
            {
                using (jb.Block("picture"))
                    jb.Quoted("reference", StubPicture);

                jb.Field("title", d.Title);
                jb.Field("desc", d.Desc);
                jb.Field("selection_tooltip", d.SelectionTooltip);
                jb.Field("confirm_text", d.ConfirmText);

                jb.Inline("is_shown", "always = no");
            }

            jb.Blank();
        }

        // The path is the fix, not the filename. See the remarks above before moving this.
        string dir = Path.Combine(modDir, "common", "decisions", "dlc_decisions", "zzz_generated");
        Directory.CreateDirectory(dir);
        ParadoxText.WriteBom(Path.Combine(dir, "zzz_generated_decision_blocks.txt"), jb.ToString());

        Console.WriteLine($"  decision blocks: {blocked.Count} of {scanned} vanilla decisions hidden " +
                          "(completely_controls against a landless shim)");
    }

    /// <summary>
    /// Every top-level <c>key = {</c> block in a decisions file, with its body, comments stripped.
    ///
    /// All 431 vanilla decisions open at column 0 with the brace on the same line, and the only
    /// other thing at column 0 is an <c>@constant</c> definition, so the column test is the whole
    /// parser. The key is not filtered on a <c>_decision</c> suffix: thirty-six vanilla decisions
    /// do not have one — convert_to_confucianism, change_state_faith, legendary_holy_war — and in
    /// a decisions file every top-level object is a decision anyway.
    /// </summary>
    private static IEnumerable<(string Key, string Body)> ScanDecisions(string text)
    {
        var lines = text.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (line.Length == 0 || char.IsWhiteSpace(line[0]) || line[0] is '#' or '@') continue;

            int equals = line.IndexOf('=');
            if (equals <= 0 || !line.Contains('{')) continue;

            string key = line[..equals].Trim();
            if (key.Length == 0 || !key.All(c => char.IsLetterOrDigit(c) || c is '_' or '-')) continue;

            var body = new StringBuilder();
            int depth = 0;

            for (int j = i; j < lines.Length; j++)
            {
                string code = lines[j];
                int hash = code.IndexOf('#');
                if (hash >= 0) code = code[..hash];

                body.Append(code).Append('\n');

                depth += code.Count(c => c == '{') - code.Count(c => c == '}');
                if (depth <= 0) { i = j; break; }
            }

            yield return (key, body.ToString());
        }
    }

    /// <summary>
    /// Index of the first character of <paramref name="name"/>'s value, or -1 when the block has
    /// no such field.
    ///
    /// Matched at brace depth 1 only, so a <c>widget</c>'s per-item <c>is_shown</c> can never be
    /// taken for the decision's own, and on a token boundary, so <c>is_valid</c> does not match
    /// the front of <c>is_valid_showing_failures_only</c>.
    ///
    /// Written as a scan rather than an anchored regex because the obvious regex is wrong in a way
    /// that reads as correct: in <c>^\s*is_shown\s*=\s*\{</c> the leading <c>\s*</c> will start the
    /// match on the blank line above, a brace walk from there closes on that empty first line, and
    /// the caller gets an empty block instead of the real one. That is not hypothetical — it is
    /// what made the first pass of this audit report two hits instead of eighty-one.
    /// </summary>
    private static int FieldValueAt(string body, string name)
    {
        int depth = 0;

        for (int i = 0; i < body.Length; i++)
        {
            char c = body[i];

            if (c == '{') { depth++; continue; }
            if (c == '}') { depth--; continue; }

            if (depth != 1 || c != name[0] || i + name.Length > body.Length) continue;
            if (string.CompareOrdinal(body, i, name, 0, name.Length) != 0) continue;
            if (i > 0 && (char.IsLetterOrDigit(body[i - 1]) || body[i - 1] == '_')) continue;

            int at = i + name.Length;
            while (at < body.Length && char.IsWhiteSpace(body[at])) at++;
            if (at >= body.Length || body[at] != '=') continue;

            at++;
            while (at < body.Length && char.IsWhiteSpace(body[at])) at++;
            if (at < body.Length) return at;
        }

        return -1;
    }

    /// <summary>
    /// The text of <paramref name="name"/>'s block, braces included, or empty if it has none.
    /// A scalar field of the same name — <c>is_shown = trigger</c> inside a widget item — is not
    /// a block and reads as absent.
    /// </summary>
    private static string SubBlock(string body, string name)
    {
        int at = FieldValueAt(body, name);
        if (at < 0 || body[at] != '{') return "";

        int depth = 0;
        for (int j = at; j < body.Length; j++)
        {
            if (body[j] == '{') depth++;
            else if (body[j] == '}' && --depth == 0) return body[at..(j + 1)];
        }

        return body[at..];
    }

    /// <summary>
    /// The localisation key <paramref name="name"/> resolves to, or null when it has no field.
    ///
    /// A one-line field is the key itself. A block is one of vanilla's fifty dynamic descriptions
    /// — <c>desc = { first_valid = { triggered_desc = { … } desc = &lt;fallback&gt; } }</c> — and
    /// what is taken from that is the last plain <c>desc = &lt;key&gt;</c> inside it, which is the
    /// unconditional branch a first_valid chain ends with.
    ///
    /// Collapsing a dynamic description to its fallback is exactly right for text that never
    /// renders, and it is why the block is read rather than carried: carrying it would drag the
    /// triggers along, and their references to vanilla titles, into a stub whose whole point is to
    /// reference nothing.
    /// </summary>
    private static string? LocField(string body, string name)
    {
        int at = FieldValueAt(body, name);
        if (at < 0) return null;

        if (body[at] == '{')
        {
            var fallbacks = DynamicDescFallback().Matches(SubBlock(body, name));
            return fallbacks.Count == 0 ? null : fallbacks[^1].Groups[1].Value;
        }

        int end = body.IndexOf('\n', at);
        string value = (end < 0 ? body[at..] : body[at..end]).Trim();

        return value.Length == 0 ? null : value;
    }

    /// <summary>
    /// A <c>desc = &lt;key&gt;</c> inside a dynamic description. The lookbehind is what keeps it
    /// off <c>triggered_desc</c>, and the key pattern is what keeps it off <c>desc = {</c>.
    /// </summary>
    [System.Text.RegularExpressions.GeneratedRegex(@"(?<![A-Za-z0-9_])desc\s*=\s*([A-Za-z0-9_.]+)")]
    private static partial System.Text.RegularExpressions.Regex DynamicDescFallback();

    /// <summary>
    /// Rebinds vanilla's 322 holy sites onto generated counties.
    ///
    /// Every faith names its holy sites, so a holy site whose county does not exist leaves the
    /// faith holding an object with no county — "No county found for holy site 'jerusalem'",
    /// once per site. Blanking the file is not an option either: faiths would then reference
    /// holy sites that do not exist at all, and the character modifiers declared here are
    /// referenced by name elsewhere.
    ///
    /// The rewrite is deliberately line-based so every modifier, parameter and flag survives
    /// untouched — only the `county` target changes, and `barony` lines are dropped because our
    /// barony keys never match vanilla's.
    /// </summary>
    public static void WriteHolySites(string modDir, string gameDir, List<Title> empires, FaithMap? faiths = null)
    {
        string source = Path.Combine(gameDir, "common", "religion", "holy_site_types");
        string destination = Path.Combine(modDir, "common", "religion", "holy_site_types");
        if (!Directory.Exists(source)) return;
        Directory.CreateDirectory(destination);

        var counties = Titles.Flatten(empires).Where(t => t.Tier == "c").ToList();
        if (counties.Count == 0) return;

        // Collect all counties already chosen as holy sites for generated faiths.
        // If none exist, fallback to the first few counties instead of spreading across all of them.
        var targetCounties = faiths?.Faiths
            .SelectMany(f => f.HolySites.Select(hs => hs.County))
            .Distinct()
            .ToList();

        if (targetCounties is null || targetCounties.Count == 0)
        {
            targetCounties = counties.Take(Math.Min(5, counties.Count)).ToList();
        }

        int rebound = 0, sites = 0;

        foreach (string path in Directory.GetFiles(source, "*.txt"))
        {
            var output = new StringBuilder();

            foreach (string line in File.ReadAllLines(path))
            {
                string code = line;
                int hash = code.IndexOf('#');
                if (hash >= 0) code = code[..hash];

                // Drop barony targets outright; ours never share vanilla's keys.
                if (Regex.IsMatch(code, @"^\s*barony\s*=")) continue;

                var match = Regex.Match(code, @"^(\s*)county\s*=\s*[A-Za-z_0-9&-]+");
                if (match.Success)
                {
                    // Target only the designated holy site counties
                    output.Append($"{match.Groups[1].Value}county = {targetCounties[rebound++ % targetCounties.Count].Key}\n");
                    continue;
                }

                if (Regex.IsMatch(code, @"^[A-Za-z_0-9&-]+\s*=\s*\{")) sites++;
                output.Append(line).Append('\n');
            }

            ParadoxText.WriteBom(Path.Combine(destination, Path.GetFileName(path)), output.ToString());
        }

        Console.WriteLine($"  holy sites: {sites} re-declared, {rebound} rebound onto {targetCounties.Count} holy site counties");
    }

    /// <summary>
    /// What this world calls its era, short form — "BE", "AC", whatever the export named it.
    /// Empty when the world is on vanilla's calendar and its years want no suffix at all.
    ///
    /// The full name is the fallback because an export may fill one field and not the other, and a
    /// long era after a year still reads as a date; nothing after a year does not.
    /// </summary>
    public static string EraSuffix(MapGen.AzgaarImport? azgaar)
    {
        if (azgaar is null) return "";

        string era = azgaar.EraShort.Trim();
        if (era.Length == 0) era = azgaar.EraName.Trim();
        return era;
    }

    /// <summary>
    /// The era's full name — "the Cladian Era", not "CE" — or empty when the export left it blank.
    /// <see cref="BookmarkWriter"/> puts this on the bookmark tab, where there is room for it and
    /// where the export naming its own age beats anything this generator would invent.
    /// </summary>
    public static string EraFullName(MapGen.AzgaarImport? azgaar)
        => azgaar?.EraName.Trim() ?? "";

    /// <summary>
    /// <summary>
    /// Puts the world's own era on the game clock.
    ///
    /// The year itself needs no arithmetic — <see cref="MapGen.AzgaarImport"/> already moved the
    /// bookmark onto the export's calendar, so the engine is counting the right number and only
    /// calls it the wrong thing. All that is left is the suffix: vanilla renders " AD" and this
    /// world wants " BE".
    ///
    /// AGOT needs a great deal more than this, and the difference is worth knowing. Its dates are
    /// offset by a constant (bookmarks at 8082-8282 for years 82-282 After the Conquest) across five
    /// stacked eras, so each of its date strings is a nest of <c>Select_int32</c> subtracting a
    /// different base per era, repeated once per context because the year is reached through a
    /// different datafunction in each. Adopting the year outright costs none of that.
    ///
    /// Written into <c>localization/replace/</c>, which loads after the ordinary pass — these three
    /// are vanilla keys and have to win. The folder replaces *same-named* vanilla files, so a name
    /// of our own shadows nothing; it only buys the later slot.
    /// </summary>
    public static void WriteCalendarLocalisation(string modDir, MapGen.AzgaarImport? azgaar)
    {
        if (azgaar is null) return;

        string era = EraSuffix(azgaar);
        if (era.Length == 0) return;

        // Literal rather than through vanilla's $ERA$ token. That token resolves to
        // GAME_DATE_STRING_ERA_CE or _BCE depending on sign, and only two of the three date strings
        // reference it at all -- the plain one carries no era and the short one asks for the BCE
        // form. Writing the suffix in directly makes all three agree without depending on which
        // token the engine happens to supply where.
        string text =
            $$"""
              l_english:
               GAME_DATE_STRING:0 "$DAY$ $MONTH$, $YEAR$ {{Io.ParadoxText.Loc(era)}}"
               GAME_DATE_STRING_SHORT:0 "$DAY$ $MONTH_SHORT$ $YEAR$ {{Io.ParadoxText.Loc(era)}}"
               GAME_DATE_STRING_LONG:0 "$DAY|O$ of $MONTH$, $YEAR$ {{Io.ParadoxText.Loc(era)}}"

              """;

        string dir = Path.Combine(modDir, "localization", "replace", "english");
        Directory.CreateDirectory(dir);
        ParadoxText.WriteBom(Path.Combine(dir, "zz_gen_calendar_l_english.yml"), text);

        Console.WriteLine($"  calendar: dates suffixed \"{era}\"" +
                          (azgaar.EraName.Length > 0 ? $" ({azgaar.EraName})" : ""));
    }

    /// <summary>
    /// Pushes the end of the world out when the world's calendar would otherwise run into it.
    ///
    /// Vanilla's <c>END_DATE</c> is a hard 1453.1.1, and a world that calls the present year 1448
    /// would get five years of game. The years are the world's own and are not going to be argued
    /// with, so the wall moves instead — AGOT does the same thing for the same reason, taking it to
    /// 9999.1.1 to fit a calendar that counts from Aegon's Conquest.
    ///
    /// Emitted only when it is actually needed. A default run starts in 900 and has five centuries
    /// in hand; overriding the define there would be a silent change to how long every ordinary
    /// generated world lasts, which is not this feature's business.
    /// </summary>
    private static string EndDate(Config.MapConfig cfg)
    {
        const int vanillaEnd = 1453;

        // Enough room that the extension is worth making at all — a world that ends in a century is
        // short, but it is recognisably a game, and the define is vanilla's own balance decision.
        const int wanted = 400;

        int end = cfg.StartYear + wanted;
        if (end <= vanillaEnd) return "";

        Console.WriteLine($"  defines: END_DATE {end}.1.1 (vanilla 1453.1.1, world starts {cfg.StartYear})");

        return $$"""
                 # A world whose calendar starts near or past vanilla's end date needs the wall moved.
                 NGame = {
                 	END_DATE = "{{end}}.1.1"
                 }

                 """;
    }

    /// <summary>
    /// Moves the culture eras onto the world's own calendar.
    ///
    /// <c>common/culture/eras</c> is the only place in the game where advancement is gated on a
    /// year: an innovation belongs to an era by key and carries no date of its own, so these four
    /// <c>year</c> thresholds decide, alone, which era the game thinks a culture is in. On a world
    /// whose calendar has been slid off vanilla's they have to slide with it, or
    /// <see cref="CultureWriter"/> seeds a people with early-medieval innovations and the game —
    /// still reading vanilla's <c>year = 900</c> against a bookmark in 433 — calls them tribal and
    /// refuses the buildings those innovations unlock.
    ///
    /// AGOT does exactly this and is the reason to trust it: its bookmarks sit at 8082–8282 and it
    /// ships this file with 900/1050/1200 moved to 4300/7898/8260, the vanilla values left beside
    /// them as comments.
    ///
    /// Writes nothing at all when the offset is zero, which is every run that has not set
    /// <see cref="MapConfig.EraAnchorYear"/> — vanilla's own file is then already correct, and
    /// shadowing it with an identical copy would only be one more file to keep in step with a patch.
    /// </summary>
    public static void WriteCultureEras(string modDir, string gameDir, Config.MapConfig cfg)
    {
        if (cfg.EraOffset == 0) return;

        string source = Path.Combine(gameDir, "common", "culture", "eras");
        if (!Directory.Exists(source)) return;

        string destination = Path.Combine(modDir, "common", "culture", "eras");
        Directory.CreateDirectory(destination);

        int moved = 0;

        foreach (string path in Directory.GetFiles(source, "*.txt"))
        {
            var lines = File.ReadAllText(path).Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                // Depth one only. An era's `year` is its own top-level field; matching the bare
                // word anywhere would also catch any nested trigger that happens to mention one.
                var match = YearField().Match(lines[i]);
                if (!match.Success) continue;

                int vanilla = int.Parse(match.Groups[2].Value);

                // Zero is not a date, it is "from the beginning" — the tribal era's way of saying
                // there is nothing before it. Sliding it forward with the rest opens a gap where
                // the world has no era at all, which a positive offset makes visible immediately:
                // shift by +548 and every culture is era-less until the year 548.
                if (vanilla == 0) continue;

                int shifted = Math.Max(1, vanilla + cfg.EraOffset);

                lines[i] = $"{match.Groups[1].Value}year = {shifted}\t# was {vanilla}";
                moved++;
            }

            ParadoxText.WriteBom(Path.Combine(destination, Path.GetFileName(path)),
                string.Join('\n', lines));
        }

        Console.WriteLine($"  culture eras: {moved} thresholds moved by {cfg.EraOffset:+#;-#;0} years " +
                          $"(world year {cfg.StartYear}, as advanced as {cfg.EraYear})");
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^([ \t]+)year\s*=\s*(-?\d+)\s*$")]
    private static partial System.Text.RegularExpressions.Regex YearField();

    /// <summary>
    /// Re-declares every vanilla geographical region against generated titles.
    ///
    /// Blanking these files does not work: CK3 then reports "no visual geographical region" once
    /// per province (observed as exactly one error per land province) and every script_value
    /// that scopes into a region fails with "Invalid geographical region". An *empty* region
    /// block is no better — it parses but never registers in CGeographicalRegionDatabase, which
    /// breaks the geographical_region trigger and the region-derived modifiers, surfacing as a
    /// baffling "Unexpected token" error in an unrelated file. Every region needs a member.
    /// </summary>
    public static void WriteGeographicalRegions(string modDir, string gameDir, List<Title> empires)
    {
        string source = Path.Combine(gameDir, "map_data", "geographical_regions");
        string destination = Path.Combine(modDir, "map_data", "geographical_regions");
        if (!Directory.Exists(source)) return;
        Directory.CreateDirectory(destination);

        var all = Titles.Flatten(empires).ToList();
        var counties = all.Where(t => t.Tier == "c").ToList();
        var provinceIds = all.Where(t => t.Tier == "b" && t.ProvinceId > 0)
                             .Select(t => t.ProvinceId).ToList();
        if (counties.Count == 0 || provinceIds.Count == 0) return;

        // Pass 1: read every region key and the properties that must survive re-declaration.
        var files = new Dictionary<string, List<Region>>();
        var graphical = new List<Region>();

        foreach (string path in Directory.GetFiles(source, "*.txt"))
        {
            var regions = ScanRegions(File.ReadAllText(path));
            files[Path.GetFileName(path)] = regions;
            // Detect by the flag, not the key name: `graphical = yes` is what makes a region
            // visual, and it is the property CK3 actually looks for.
            graphical.AddRange(regions.Where(r => r.Graphical));
        }

        // Every province must belong to exactly one graphical region or CK3 complains about it
        // individually, so split them evenly across the graphical keys.
        var graphicalProvinces = new Dictionary<string, List<int>>();
        if (graphical.Count > 0)
        {
            foreach (var region in graphical) graphicalProvinces[region.Key] = [];
            for (int i = 0; i < provinceIds.Count; i++)
                graphicalProvinces[graphical[i % graphical.Count].Key].Add(provinceIds[i]);
        }

        int written = 0;
        foreach (var (fileName, regions) in files)
        {
            var b = new JominiBuilder();
            b.Comment("""
                      Vanilla region keys re-declared against generated titles.
                      Keys are preserved because base-game and DLC script hardcodes them.
                      """);
            b.Blank();

            int counter = 0;
            foreach (var region in regions)
            {
                string key = region.Key;

                using (b.Block(key))
                {
                    if (region.GenerateModifiers) b.Field("generate_modifiers", "yes");

                    // Without these two a graphical region is not a visual region, and every land
                    // province ends up unassigned.
                    if (region.Graphical) b.Field("graphical", "yes");
                    if (region.Color is not null) b.Inline("color", region.Color);

                    if (graphicalProvinces.TryGetValue(key, out var provinces))
                    {
                        // Twenty ids to a line. These lists run to thousands of entries and one id
                        // per line would make the file unreadable and enormous.
                        using (b.Block("provinces"))
                            for (int i = 0; i < provinces.Count; i += 20)
                                b.Token(string.Concat(provinces.Skip(i).Take(20).Select(p => $"{p} ")));
                    }
                    else
                    {
                        // One real member is the minimum for the region to register at all.
                        b.Inline("counties", counties[counter++ % counties.Count].Key);
                    }
                }

                b.Blank();
                written++;
            }

            ParadoxText.WriteBom(Path.Combine(destination, fileName), b.ToString());
        }

        Console.WriteLine($"  re-declared {written} geographical regions " +
                          $"({graphical.Count} graphical covering {provinceIds.Count} provinces)");
    }

    /// <summary>
    /// Finds top-level `key = {` blocks and reports whether each declares generate_modifiers.
    ///
    /// That flag must be preserved exactly: it is what creates the
    /// &lt;region&gt;_development_growth[_factor] modifiers that
    /// common/modifier_definition_formats/00_region_definitions.txt declares. Dropping it makes
    /// those modifier types unknown, which then breaks 00_traits.txt, common/modifiers/* and
    /// holy_site_types with errors pointing at completely unrelated files.
    ///
    /// Paradox identifiers are not [a-z_0-9] — region keys contain ampersands
    /// (ghw_region_finland_&amp;_estonia), so a stricter pattern silently drops keys and every
    /// dropped key becomes a dangling reference.
    /// </summary>
    private static List<Region> ScanRegions(string text)
    {
        var result = new List<Region>();
        var lines = text.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            // Top-level blocks start at column 0.
            if (line.Length == 0 || char.IsWhiteSpace(line[0]) || line[0] == '#') continue;

            int equals = line.IndexOf('=');
            if (equals <= 0 || !line.Contains('{')) continue;

            string key = line[..equals].Trim();
            if (key.Length == 0 || !key.All(c => char.IsLetterOrDigit(c) || c is '_' or '-' or '&')) continue;

            // Walk the block to its closing brace, noting the flags we must preserve.
            bool generateModifiers = false;
            bool graphical = false;
            string? color = null;
            int depth = 0;
            for (int j = i; j < lines.Length; j++)
            {
                string body = lines[j];
                int hash = body.IndexOf('#');
                if (hash >= 0) body = body[..hash];

                if (body.Contains("generate_modifiers")) generateModifiers = true;
                if (body.Contains("graphical") && body.Contains("yes")) graphical = true;

                int colorAt = body.IndexOf("color", StringComparison.Ordinal);
                if (colorAt >= 0)
                {
                    int open = body.IndexOf('{', colorAt);
                    int close = open >= 0 ? body.IndexOf('}', open) : -1;
                    if (close > open) color = body[(open + 1)..close].Trim();
                }

                depth += body.Count(c => c == '{') - body.Count(c => c == '}');
                if (depth <= 0) { i = j; break; }
            }

            result.Add(new Region(key, generateModifiers, graphical, color));
        }

        return result;
    }

    /// <summary>
    /// A vanilla region key and the properties that must survive re-declaration.
    ///
    /// <paramref name="Graphical"/> is the one that bites: a region is only a *visual* region if
    /// it carries `graphical = yes`. Re-declaring the seven graphical_* keys with province lists
    /// but without the flag leaves CK3 with no visual regions at all, and it then logs
    /// "Province N has no visual geographical region assigned" once for every land province.
    /// </summary>
    private readonly record struct Region(
        string Key, bool GenerateModifiers, bool Graphical, string? Color);
}
