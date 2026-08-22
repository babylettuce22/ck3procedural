using System.Globalization;
using System.Text;
using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// Writes gfx/map/map_object_data/bridges.txt — stone bridges over the major rivers.
///
/// Vanilla's own bridges.txt is the model: 187 placements, and 182 of them sit on river
/// *provinces* — the Vistula, the Daugava, the Hardangerfjord — not on the rivers.png lines, which
/// render only one to four units wide and need no bridging. So this reads the traced courses in
/// <see cref="MajorRiverPath"/> and nothing else.
///
/// <b>Geometry, measured off the .mesh files.</b> Every bridge mesh is authored with its span
/// along its local Z axis: western 7.6 units end to end, mediterranean 7.8, mena 9.1, indian 9.4,
/// with a road decal on the banks reaching a unit or two further. The deck is ~0.8–1.5 above the
/// origin and the piers reach ~1 below it. One province pixel is one world unit on every map
/// (see <see cref="CompatibilityWriter.WriteDefines"/>), so a channel's width in pixels is the
/// span it needs. Vanilla scales its bridges 0.87–1.5, to a span about 1.8 times the water
/// width at the site; the same ratio is used here.
///
/// <b>Orientation.</b> The transform is the same vertical-axis quaternion TreeWriter writes,
/// (0, sin t/2, 0, cos t/2), and under it the mesh's local +Z lands on world (sin t, cos t). That
/// is not a guess: over vanilla's 125 diagonally-placed bridges, the water run along that axis is
/// the short one (median 4 pixels against 11 for the mirrored reading) in 112 cases and the long
/// one in 3. So the bridge wants t = atan2(dy, dx) for a river heading (dx, dy) in image space —
/// the perpendicular, with the image-to-world row flip folded in.
///
/// <b>Height.</b> The carve takes a major river's bed straight down to <c>SeaFloorElevation</c>,
/// which is heightmap 0 — 3.7 world units under the surface. Y in a transform is an offset from
/// the terrain under the origin (vanilla's trees are all at 0, its bridges at +0.1..+1.0 over a
/// bed ~1.7 under water), so a bridge planted mid-channel with vanilla's offsets would be drowned
/// here. Rather than guess the depth back, <c>clamp_to_water_level=yes</c> — the flag vanilla
/// uses for every stack and building locator so that an army at sea stands on the water rather
/// than the seabed — lifts the origin to the surface, and a small offset sets the deck above it.
///
/// <b>Siting.</b> A candidate every 90–200 pixels of course. At each the rendered heightmap is
/// walked across the channel, perpendicular to the course, to find both banks: the water run
/// through the centre has to be 3–13 pixels — under three it is the tapered head, over thirteen
/// it is a lake, an estuary or the sea — and the ground just beyond each bank has to be dry and
/// within a unit and a half of the other bank, or the bridge would climb out of the river on one
/// side. The mesh is chosen by the climate at the site, the way the holdings pick their look.
/// </summary>
public static class BridgeWriter
{
    /// <param name="Span">End-to-end length of the mesh along its local Z, in world units.</param>
    private sealed record Style(string Name, string Mesh, float Span);

    private static readonly Style Western = new("bridge western", "bridge_western_mesh", 7.63f);
    private static readonly Style Mediterranean = new("bridge mediterranean", "bridge_mediterranean_mesh", 7.78f);
    private static readonly Style Mena = new("bridge mena", "bridge_mena_mesh", 9.09f);
    private static readonly Style Indian = new("bridge indian", "bridge_indian_mesh", 9.42f);
    private static readonly Style[] Styles = [Western, Mediterranean, Mena, Indian];

    /// <summary>Spacing along a course between candidate sites, in province pixels.</summary>
    private const double MinSpacing = 90, MaxSpacing = 200;

    /// <summary>How far two bridges on different courses must stand apart, in province pixels.</summary>
    private const double MinSeparation = 40;

    /// <summary>
    /// Narrowest and widest water run a bridge is put across, in province pixels. The carve makes
    /// our channels 10–20 pixels wide where vanilla's river provinces are 3–7 at a bridge, so the
    /// ceiling is set by how far the mesh can be stretched, not by vanilla's habit; past it the
    /// water is a lake, an estuary or the sea.
    /// </summary>
    private const double MinWidth = 3.0, MaxWidth = 21.0;

    /// <summary>
    /// Scale limits. Vanilla's measured range is 0.87–1.50, uniform. Ours is uniform up to
    /// <see cref="MaxBodyScale"/> and then stretches along the span alone, up to
    /// <see cref="MaxSpanScale"/>: a bridge over a 20-pixel river at uniform 3.0 would be six
    /// units tall beside the holdings, whereas a long low arch is what a long bridge looks like.
    /// </summary>
    private const float MinScale = 0.85f, MaxBodyScale = 1.8f, MaxSpanScale = 3.0f;

    /// <summary>
    /// Span as a multiple of the water width. Vanilla's median is 1.8 over channels a third the
    /// width of ours; here the ends only need to reach the bank, and the road decal carries on.
    /// </summary>
    private const double SpanOverWidth = 1.3;

    /// <summary>
    /// Offset above the water surface the origin is lifted to. The deck sits 0.8–1.5 units
    /// above the origin at scale 1, so this keeps it clear of the water without floating.
    /// </summary>
    private const float DeckLift = 0.25f;

    public static void WriteAll(string modDir, MapConfig cfg, List<MajorRiverPath> rivers,
        KoppenClass[] climate, float[] elevation, Rng rng)
    {
        string dir = Path.Combine(modDir, "gfx", "map", "map_object_data");
        Directory.CreateDirectory(dir);

        var placed = new Dictionary<Style, List<(float X, float Z, float Angle, float Body, float Span)>>();
        foreach (var s in Styles) placed[s] = [];
        var sites = new List<(double X, double Y)>();

        int width = cfg.ProvinceWidth, height = cfg.ProvinceHeight;
        float sea = cfg.Limits.SeaLevelUpper;
        float waterHeight = ScatterGround.WorldHeight(sea, cfg);

        int candidates = 0, dryCentre = 0, tooWide = 0, tooNarrow = 0, unevenBanks = 0, crowded = 0;
        var widths = new List<double>();

        foreach (var river in rivers)
        {
            var pts = river.Points;
            int n = pts.Count;
            if (n < 40) continue;

            // Arc length along the course, in province pixels.
            var arc = new double[n];
            for (int i = 1; i < n; i++)
            {
                double dx = pts[i].X - pts[i - 1].X, dy = pts[i].Y - pts[i - 1].Y;
                arc[i] = arc[i - 1] + Math.Sqrt(dx * dx + dy * dy);
            }

            // The first site is never at the very head — it is the tapered trickle — and never
            // right at the mouth, where the channel is opening into the sea.
            double next = rng.Double(MinSpacing * 0.5, MaxSpacing * 0.5);
            double last = arc[n - 1] - 20;

            for (int i = 3; i < n - 3; i++)
            {
                if (arc[i] < next) continue;
                if (arc[i] > last) break;
                candidates++;

                // Course heading over a few points either side, so a one-pixel kink in the
                // resampled path does not turn the bridge.
                double hx = pts[i + 3].X - pts[i - 3].X, hy = pts[i + 3].Y - pts[i - 3].Y;
                double hl = Math.Sqrt(hx * hx + hy * hy);
                if (hl < 1e-3) continue;
                hx /= hl; hy /= hl;

                // Perpendicular, image frame.
                double nx = -hy, ny = hx;
                double cx = pts[i].X + 0.5, cy = pts[i].Y + 0.5;

                // The centre must be in the water. The heading can leave the resampled path a
                // pixel off the carved bed on a tight bend, so nudge once across the channel.
                if (!(ScatterGround.SampleHeight(elevation, cfg, cx, cy) <= sea))
                {
                    bool found = false;
                    for (double off = 0.5; off <= 3.0 && !found; off += 0.5)
                    {
                        foreach (double sgn in (ReadOnlySpan<double>)[1, -1])
                        {
                            if (ScatterGround.SampleHeight(elevation, cfg, cx + nx * off * sgn, cy + ny * off * sgn) <= sea)
                            { cx += nx * off * sgn; cy += ny * off * sgn; found = true; break; }
                        }
                    }
                    if (!found) { dryCentre++; next = arc[i] + MinSpacing * 0.5; continue; }
                }

                // Walk out to each bank in half-pixel steps.
                double left = WaterRun(elevation, cfg, sea, cx, cy, -nx, -ny);
                double right = WaterRun(elevation, cfg, sea, cx, cy, nx, ny);
                double waterWidth = left + right;
                widths.Add(waterWidth);

                if (left >= MaxWidth || right >= MaxWidth || waterWidth > MaxWidth)
                {
                    tooWide++;
                    next = arc[i] + MinSpacing * 0.5;
                    continue;
                }
                if (waterWidth < MinWidth)
                {
                    tooNarrow++;
                    next = arc[i] + MinSpacing * 0.5;
                    continue;
                }

                // Both banks dry for a few pixels past the waterline, and level with each other.
                if (!Bank(elevation, cfg, sea, cx, cy, -nx, -ny, left, out float hLeft) ||
                    !Bank(elevation, cfg, sea, cx, cy, nx, ny, right, out float hRight) ||
                    Math.Abs(hLeft - hRight) > 1.5f ||
                    Math.Max(hLeft, hRight) - waterHeight > 3.0f)
                {
                    unevenBanks++;
                    next = arc[i] + MinSpacing * 0.5;
                    continue;
                }

                // Centre the bridge on the channel, not on the traced line.
                double mx = cx + nx * (right - left) / 2.0;
                double my = cy + ny * (right - left) / 2.0;

                bool near = false;
                foreach (var (sx, sy) in sites)
                {
                    double ddx = sx - mx, ddy = sy - my;
                    if (ddx * ddx + ddy * ddy < MinSeparation * MinSeparation) { near = true; break; }
                }
                if (near) { crowded++; next = arc[i] + MinSpacing * 0.5; continue; }

                int px = Math.Clamp((int)mx, 0, width - 1), py = Math.Clamp((int)my, 0, height - 1);
                var style = StyleFor(climate[py * width + px]);

                // Uniform while it can be, then stretched along the span only.
                float needed = (float)(waterWidth * SpanOverWidth / style.Span);
                if (needed > MaxSpanScale) { tooWide++; next = arc[i] + MinSpacing * 0.5; continue; }
                float spanScale = Math.Max(needed, MinScale);
                float bodyScale = Math.Min(spanScale, MaxBodyScale);

                var (wx, wz) = WorldSpace.FromImage(mx, my, height);
                float angle = (float)Math.Atan2(hy, hx);

                placed[style].Add(((float)wx, (float)wz, angle, bodyScale, spanScale));
                sites.Add((mx, my));

                next = arc[i] + rng.Double(MinSpacing, MaxSpacing);
            }
        }

        Write(Path.Combine(dir, "bridges.txt"), placed);

        int total = sites.Count;
        widths.Sort();
        string seen = widths.Count == 0 ? "no water measured"
            : $"water {widths[0]:0.0}–{widths[^1]:0.0} px across at the sites, median {widths[widths.Count / 2]:0.0}";
        Console.WriteLine($"  bridges: {total} over {rivers.Count} major river course(s) from {candidates} candidate sites " +
                          $"({dryCentre} off the water, {tooNarrow} too narrow, {tooWide} too wide for a span, " +
                          $"{unevenBanks} with uneven banks, {crowded} too close to another; {seen})");
    }

    /// <summary>
    /// Distance from (cx, cy) along (dx, dy) to the first dry heightmap texel, in province pixels,
    /// capped at <see cref="MaxWidth"/>.
    /// </summary>
    private static double WaterRun(float[] elevation, MapConfig cfg, float sea,
        double cx, double cy, double dx, double dy)
    {
        for (double d = 0.5; d < MaxWidth; d += 0.5)
        {
            float h = ScatterGround.SampleHeight(elevation, cfg, cx + dx * d, cy + dy * d);
            if (float.IsNaN(h) || h > sea) return d;
        }
        return MaxWidth;
    }

    /// <summary>
    /// Whether the ground from just past the waterline to three pixels beyond it is all dry, and
    /// its height in world units at the bridge's abutment.
    /// </summary>
    private static bool Bank(float[] elevation, MapConfig cfg, float sea,
        double cx, double cy, double dx, double dy, double run, out float worldHeight)
    {
        worldHeight = 0;
        float sum = 0; int count = 0;
        for (double d = run + 0.5; d <= run + 3.0; d += 0.5)
        {
            float h = ScatterGround.SampleHeight(elevation, cfg, cx + dx * d, cy + dy * d);
            if (float.IsNaN(h) || h <= sea) return false;
            sum += ScatterGround.WorldHeight(h, cfg);
            count++;
        }
        worldHeight = sum / count;
        return true;
    }

    private static Style StyleFor(KoppenClass climate) => climate switch
    {
        KoppenClass.HotDesert or KoppenClass.HotSteppe or KoppenClass.ColdDesert => Mena,
        KoppenClass.TropicalRainforest or KoppenClass.TropicalMonsoon or KoppenClass.TropicalSavanna => Indian,
        KoppenClass.Mediterranean or KoppenClass.HumidSubtropical => Mediterranean,
        _ => Western,
    };

    private static void Write(string path, Dictionary<Style, List<(float X, float Z, float Angle, float Body, float Span)>> placed)
    {
        var sb = new StringBuilder(4096);
        var culture = CultureInfo.InvariantCulture;

        foreach (var style in Styles)
        {
            var instances = placed[style];

            sb.Append("object={\n");
            sb.Append($"\tname=\"{style.Name}\"\n");
            sb.Append("\trender_pass=MapUnderWater\n");
            sb.Append("\tclamp_to_water_level=yes\n");
            sb.Append("\tgenerated_content=no\n");
            sb.Append("\tlayer=\"building_layer\"\n");
            sb.Append($"\tpdxmesh=\"{style.Mesh}\"\n");
            sb.Append($"\tcount={instances.Count}\n");
            sb.Append("\ttransform=\"");

            for (int i = 0; i < instances.Count; i++)
            {
                var (x, z, angle, body, span) = instances[i];

                double qy = Math.Sin(angle / 2.0);
                double qw = Math.Cos(angle / 2.0);

                // Scale is in the mesh's own frame — X across the deck, Y up, Z along the span —
                // so the stretch goes on the third component whatever the yaw.
                if (i > 0) sb.Append('\n');
                sb.Append(x.ToString("F6", culture)).Append(' ')
                  .Append(DeckLift.ToString("F6", culture)).Append(' ')
                  .Append(z.ToString("F6", culture)).Append(" 0.000000 ")
                  .Append(qy.ToString("F6", culture)).Append(" 0.000000 ")
                  .Append(qw.ToString("F6", culture)).Append(' ')
                  .Append(body.ToString("F6", culture)).Append(' ')
                  .Append(body.ToString("F6", culture)).Append(' ')
                  .Append(span.ToString("F6", culture));
            }

            sb.Append("\"}\n");
        }

        ParadoxText.WriteBom(path, sb.ToString());
    }
}
