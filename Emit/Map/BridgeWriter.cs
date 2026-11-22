using System.Globalization;
using System.Text;
using Ck3MapGen.Config;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// Writes gfx/map/map_object_data/bridges.txt: a bridge wherever a road crosses a river.
///
/// <b>Vanilla's rule (CK3 1.21), measured.</b> 2,064 bridges, 1,972 of them (96 %) within six pixels
/// of a land road's spline (median 0.4 px). 1,785 stand where a road crosses a rivers.png line and
/// 157 where it crosses a major river province; only 92 stand off any road. Of the road crossings,
/// 89 % of the rivers.png ones and 59 % of the river-province ones carry a bridge, the rest being the
/// widest water. So bridges follow the roads, not the rivers, and this writer reads the road lines
/// <see cref="SplineNetworkWriter"/> drew. (The old placement, a site every 90–200 px along the major
/// rivers, was the 1.20 picture of 187 bridges and stood mostly beside the roads.)
///
/// <b>Where.</b> Each land road line is walked in quarter-pixel steps. A run of steps on a rivers.png
/// river pixel (index 3–11) or on a major river's water is one crossing; the bridge stands at its
/// middle, along the road, sized to the water the road actually crosses. Major-river channels wider
/// than <see cref="MaxWidth"/> stay unbridged. Two bridges closer than <see cref="MinSeparation"/>
/// are one bridge.
///
/// <b>Meshes and size.</b> Span is each mesh's end-to-end length along its local Z, read off the
/// .mesh files: bridge_01 (1.21's plank crossing) 4.92, western 7.63, mediterranean 7.78, mena 9.09,
/// indian 9.42. A thin river gets vanilla's small bridges, uniform scale 0.4–0.7 (vanilla medians
/// 0.47–0.68), and in temperate and cold climates the plank, which is what vanilla uses for 797 of
/// its 1,785. A major river gets the climate's stone bridge sized as before: span 1.3 x the water,
/// uniform up to <see cref="MaxBodyScale"/>, then stretched along the span alone.
///
/// <b>Orientation.</b> The transform is the vertical-axis quaternion (0, sin t/2, 0, cos t/2), under
/// which the mesh's local +Z lands on world (sin t, cos t). A road heading (dx, dy) in image space
/// heads (dx, -dy) in the world, so t = atan2(dx, -dy).
///
/// <b>Height.</b> <c>clamp_to_water_level=yes</c> lifts an origin under the water to its surface
/// (our major river beds are carved to the sea floor), and <see cref="DeckLift"/> sets the deck
/// above it.
/// </summary>
public static class BridgeWriter
{
    /// <param name="Span">End-to-end length of the mesh along its local Z, in world units.</param>
    private sealed record Style(string Name, string Mesh, float Span);

    private static readonly Style Plank = new("bridge 01", "bridge_01_mesh", 4.92f);
    private static readonly Style Western = new("bridge western", "bridge_western_mesh", 7.63f);
    private static readonly Style Mediterranean = new("bridge mediterranean", "bridge_mediterranean_mesh", 7.78f);
    private static readonly Style Mena = new("bridge mena", "bridge_mena_mesh", 9.09f);
    private static readonly Style Indian = new("bridge indian", "bridge_indian_mesh", 9.42f);
    private static readonly Style[] Styles = [Plank, Western, Mediterranean, Mena, Indian];

    /// <summary>How close two bridges may stand before they are one, in province pixels.</summary>
    private const double MinSeparation = 6;

    /// <summary>
    /// The widest major-river water a road is bridged across, in province pixels along the road.
    /// Set by how far the mesh can be stretched; our carved channels run 10–20 px.
    /// </summary>
    private const double MaxWidth = 21.0;

    /// <summary>
    /// The shortest major-river water run that is a crossing, in pixels along the road. A road skirting
    /// the river province's stepped edge dips into it for single quarter-pixel steps (seen on a 1066
    /// world, 2026-11-20); those would each have got a bridge on dry ground.
    /// </summary>
    private const double MinMajorRun = 2.0;

    /// <summary>Major-river scale limits: uniform up to the body cap, then along the span only.</summary>
    private const float MinScale = 0.85f, MaxBodyScale = 1.8f, MaxSpanScale = 3.0f;

    /// <summary>Major-river span as a multiple of the water crossed.</summary>
    private const double SpanOverWidth = 1.3;

    /// <summary>A thin river's bridge span, in world units: the water plus this, kept to vanilla's range.</summary>
    private const double ThinBanks = 2.5, ThinMinSpan = 2.8, ThinMaxSpan = 5.5;

    /// <summary>A run along a stream longer than this (pixels) is the road following it, not crossing.</summary>
    private const double ThinAlongside = 8;

    /// <summary>Offset above the water surface (or ground) the origin is lifted to.</summary>
    private const float DeckLift = 0.25f;

    /// <summary>
    /// Places and writes the bridges over the road lines (province-map pixels, one list per road
    /// stretch) and returns how many it placed.
    /// </summary>
    /// <param name="riverIndices">rivers.png's palette indices, province resolution.</param>
    public static int WriteAll(string modDir, MapConfig cfg, ProvinceMap provinces,
        IReadOnlyList<List<(double X, double Y)>> roadLines, byte[] riverIndices, KoppenClass[] climate)
    {
        string dir = Path.Combine(modDir, "gfx", "map", "map_object_data");
        Directory.CreateDirectory(dir);

        int width = provinces.Width, height = provinces.Height;
        var placed = Styles.ToDictionary(s => s, _ => new List<(float X, float Z, float Angle, float Body, float Span)>());
        var sites = new List<(double X, double Y)>();
        int thin = 0, major = 0, tooWide = 0, merged = 0, alongside = 0, grazes = 0;

        // 0 dry, 1 a rivers.png river, 2 a major river's water.
        int Kind(double x, double y)
        {
            int px = (int)x, py = (int)y;
            if (px < 0 || py < 0 || px >= width || py >= height) return 0;
            int cell = py * width + px;
            if (provinces.Seeds[provinces.Label[cell]].IsMajorRiver) return 2;
            byte r = riverIndices[cell];
            return r >= 3 && r <= 11 ? 1 : 0;
        }

        const double Step = 0.25;
        foreach (var line in roadLines)
        {
            // Resample the line into quarter-pixel steps, keeping the heading at each.
            var walk = new List<(double X, double Y, double Dx, double Dy)>();
            for (int i = 0; i + 1 < line.Count; i++)
            {
                var (a, b) = (line[i], line[i + 1]);
                double len = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
                if (len < 1e-9) continue;
                double dx = (b.X - a.X) / len, dy = (b.Y - a.Y) / len;
                for (double t = 0; t < len; t += Step) walk.Add((a.X + dx * t, a.Y + dy * t, dx, dy));
            }
            if (walk.Count < 3) continue;

            int k = 0;
            while (k < walk.Count)
            {
                int kind = Kind(walk[k].X, walk[k].Y);
                if (kind == 0) { k++; continue; }
                int start = k;
                while (k < walk.Count && Kind(walk[k].X, walk[k].Y) == kind) k++;
                int end = k - 1;
                // A run touching either end of the line is a hub standing at the water, not a crossing.
                if (start == 0 || k == walk.Count) continue;

                double runLength = (end - start + 1) * Step;
                var (sx, sy, _, _) = walk[start];
                var (ex, ey, _, _) = walk[end];
                double mx = (sx + ex) / 2, my = (sy + ey) / 2;
                double hx = ex - sx, hy = ey - sy, hl = Math.Sqrt(hx * hx + hy * hy);
                if (hl < 1e-6) (hx, hy, hl) = (walk[start].Dx, walk[start].Dy, 1);
                hx /= hl; hy /= hl;

                if (sites.Any(s => (s.X - mx) * (s.X - mx) + (s.Y - my) * (s.Y - my) < MinSeparation * MinSeparation))
                { merged++; continue; }

                var zone = climate[Math.Clamp((int)my, 0, height - 1) * width + Math.Clamp((int)mx, 0, width - 1)];
                Style style; float body, span;
                if (kind == 1)
                {
                    // Longer than a stream is wide: the road is running along it, not across it.
                    if (runLength > ThinAlongside) { alongside++; continue; }
                    style = StyleFor(zone, thin: true);
                    double wanted = Math.Clamp(runLength + ThinBanks, ThinMinSpan, ThinMaxSpan);
                    body = span = (float)(wanted / style.Span);
                    thin++;
                }
                else
                {
                    // A road grazing a river province's ragged edge for a step or two is not crossing it.
                    if (runLength < MinMajorRun) { grazes++; continue; }
                    if (runLength > MaxWidth) { tooWide++; continue; }
                    style = StyleFor(zone, thin: false);
                    float needed = (float)(runLength * SpanOverWidth / style.Span);
                    if (needed > MaxSpanScale) { tooWide++; continue; }
                    span = Math.Max(needed, MinScale);
                    body = Math.Min(span, MaxBodyScale);
                    major++;
                }

                var (wx, wz) = WorldSpace.FromImage(mx, my, height);
                float angle = (float)Math.Atan2(hx, -hy);
                placed[style].Add(((float)wx, (float)wz, angle, body, span));
                sites.Add((mx, my));
            }
        }

        Write(Path.Combine(dir, "bridges.txt"), placed);
        Console.WriteLine($"  bridges: {sites.Count} where roads cross rivers ({thin} over rivers.png streams, " +
                          $"{major} over major rivers; {tooWide} major crossings too wide to span, {grazes} grazing a river's edge, {alongside} stretches " +
                          $"running along a stream, {merged} beside " +
                          $"another bridge) — vanilla bridges 89% of stream and 59% of river-province crossings");
        return sites.Count;
    }

    private static Style StyleFor(KoppenClass climate, bool thin) => climate switch
    {
        KoppenClass.HotDesert or KoppenClass.HotSteppe or KoppenClass.ColdDesert => Mena,
        KoppenClass.TropicalRainforest or KoppenClass.TropicalMonsoon or KoppenClass.TropicalSavanna => Indian,
        KoppenClass.Mediterranean or KoppenClass.HumidSubtropical => Mediterranean,
        _ => thin ? Plank : Western,
    };

    private static void Write(string path, Dictionary<Style, List<(float X, float Z, float Angle, float Body, float Span)>> placed)
    {
        var sb = new StringBuilder(4096);
        var culture = CultureInfo.InvariantCulture;
        foreach (var style in Styles)
        {
            var instances = placed[style];
            if (instances.Count == 0) continue;
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
