using Ck3MapGen.Config;
using Ck3MapGen.Emit;

namespace Ck3MapGen.Gui;

/// <summary>
/// A heightmap prepared for 3D preview: box-filtered down to preview resolution, still on CK3's own
/// 16-bit height scale so <see cref="MapDataWriter.WaterLevel16"/> means what it means everywhere
/// else.
/// </summary>
public sealed class Heightfield
{
    public required ushort[] Samples { get; init; }
    public required int Cols { get; init; }
    public required int Rows { get; init; }

    /// <summary>Highest land sample. Sets how far the camera has to clear the terrain.</summary>
    public required ushort LandMax { get; init; }

    /// <summary>
    /// The 98th percentile of land. Reported, not drawn with — see
    /// <see cref="HeightfieldRenderer.RampTop"/> for why the colour ramp stopped using it.
    ///
    /// A percentile and not <see cref="LandMax"/>: one freak peak — and an imported heightmap
    /// usually has one — would otherwise stand for the map's high ground.
    /// </summary>
    public required ushort LandTop { get; init; }

    /// <summary>Share of samples above the water plane. Drives nothing; reported to the user.</summary>
    public required double LandShare { get; init; }

    /// <summary>
    /// How wide the field is built, in samples.
    ///
    /// Deliberately far above the width of any window it will be drawn in. The renderer's cost is
    /// set by screen pixels, not by field size, so the only thing a bigger field costs is memory —
    /// 16MB here — and it buys the two things that matter. Zooming in reaches real detail instead
    /// of magnifying the preview's own downsampling into terraces. And the packer's 64-pixel tiles
    /// survive: at 1400 samples a vanilla-sized map is decimated 13:1 before it is drawn, which
    /// averages away the exact difference the "as CK3 renders it" comparison exists to show.
    /// </summary>
    public const int PreviewCols = 4096;

    /// <summary>
    /// Box-filter down to at most <paramref name="maxCols"/> columns.
    ///
    /// Averaged rather than point-sampled. CK3's own packer decimates by taking every nth sample,
    /// and matching that here would be defensible — but a preview that shimmers as it turns is
    /// harder to judge than one that is slightly soft, and the honest reproduction of the packer's
    /// decimation is <see cref="HeightmapPacker.Reconstruct"/>, which this can be fed the output of.
    /// </summary>
    public static Heightfield Downsample(ushort[] raw, int width, int height, int maxCols)
    {
        int step = Math.Max(1, (width + maxCols - 1) / maxCols);
        int cols = Math.Max(1, width / step);
        int rows = Math.Max(1, height / step);

        var samples = new ushort[(long)cols * rows];
        var histogram = new long[65536];
        int landMax = 0;
        long land = 0;
        object gate = new();

        Parallel.For(0, rows, () => (Max: 0, Land: 0L, Bins: new long[65536]), (ry, _, local) =>
        {
            // Flipped: row 0 of the field is the *bottom* of the image. The renderer's far
            // direction is +Y, so leaving the image's own top-down order in place put south at the
            // top of the screen with east still on the right — a vertically mirrored map. Flipping
            // here rather than in the renderer keeps the field in the orientation its consumers
            // expect, +Y northward, which is also the axis the lighting below is written against.
            int y0 = (rows - 1 - ry) * step, y1 = Math.Min(height, y0 + step);
            for (int rx = 0; rx < cols; rx++)
            {
                int x0 = rx * step, x1 = Math.Min(width, x0 + step);

                long sum = 0;
                int count = 0;
                for (int y = y0; y < y1; y++)
                {
                    long row = (long)y * width;
                    for (int x = x0; x < x1; x++) { sum += raw[row + x]; count++; }
                }

                ushort v = (ushort)(count == 0 ? 0 : sum / count);
                samples[(long)ry * cols + rx] = v;

                if (v > MapDataWriter.WaterLevel16)
                {
                    local.Land++;
                    local.Bins[v]++;
                    if (v > local.Max) local.Max = v;
                }
            }
            return local;
        }, local =>
        {
            lock (gate)
            {
                if (local.Max > landMax) landMax = local.Max;
                land += local.Land;
                for (int b = 0; b < histogram.Length; b++) histogram[b] += local.Bins[b];
            }
        });

        int floor = MapDataWriter.WaterLevel16 + MapDataWriter.Step255;
        long target = (long)(land * 0.98);
        long running = 0;
        int landTop = floor;

        for (int b = 0; b < histogram.Length; b++)
        {
            running += histogram[b];
            if (running >= target) { landTop = b; break; }
        }

        return new Heightfield
        {
            Samples = samples,
            Cols = cols,
            Rows = rows,
            LandMax = (ushort)Math.Max(landMax, floor),
            LandTop = (ushort)Math.Max(landTop, floor),
            LandShare = (double)land / Math.Max(1, (long)cols * rows),
        };
    }
}

/// <summary>
/// Where the camera is looking from, and how hard the relief is pushed.
///
/// <see cref="PanX"/> and <see cref="PanY"/> slide the point the camera orbits. Both are in units
/// of the field's *width* — one isotropic unit, not one per axis — so that <see cref="Panned"/> can
/// rotate them by the yaw. Scaling X by the field's width and Y by its height would make the two
/// axes different lengths, and rotating a vector whose components mean different things shears it:
/// on a 2:1 map a straight vertical drag came out diagonal, which reads on screen as the view
/// stretching rather than sliding. The bounds are applied in map space by the renderer instead,
/// where the two axes are allowed to differ.
/// </summary>
public readonly record struct HeightfieldView(
    double Yaw, double Pitch, double Distance, double Exaggeration, double PanX, double PanY)
{
    /// <summary>
    /// The pitch the orbit radius is framed for. Tilting away from it is free — the camera swings
    /// around a fixed radius rather than being re-fitted — so this only decides how much of the
    /// window the map fills when it first appears.
    /// </summary>
    public const double ReferencePitch = 0.55;

    /// <summary>
    /// Looking north-east, down at 31 degrees. <see cref="Distance"/> is a multiplier on the orbit
    /// radius that frames the map, not an absolute, so 1 suits whatever was loaded into whatever
    /// the window happens to be.
    /// </summary>
    public static HeightfieldView Default => new(0.6, ReferencePitch, 0.92, 1.0, 0, 0);

    public HeightfieldView Orbited(double dYaw, double dPitch) => this with
    {
        Yaw = Yaw + dYaw,
        // Stops just short of straight down and of grazing the ground. The projection handles both
        // extremes now, but at exactly vertical the yaw stops meaning anything to the eye and an
        // orbit drag becomes a spin around a point the user cannot see.
        Pitch = Math.Clamp(Pitch + dPitch, 0.12, 1.55),
    };

    public HeightfieldView Zoomed(double factor) => this with
    {
        Distance = Math.Clamp(Distance * factor, 0.10, 3.0),
    };

    /// <summary>
    /// Slides the focus, in the ground plane, along the axes the camera is currently looking down —
    /// so dragging right always moves the map right whatever the yaw happens to be.
    /// </summary>
    public HeightfieldView Panned(double dRight, double dForward)
    {
        double sin = Math.Sin(Yaw), cos = Math.Cos(Yaw);

        // Generous rails only, so this never binds before the renderer's own clamp to the map.
        return this with
        {
            PanX = Math.Clamp(PanX + dRight * cos + dForward * sin, -3, 3),
            PanY = Math.Clamp(PanY - dRight * sin + dForward * cos, -3, 3),
        };
    }

    public HeightfieldView Recentred() => this with { PanX = 0, PanY = 0, Distance = 0.92 };
}

/// <summary>
/// Renders a <see cref="Heightfield"/> in 3D, in software, into the same
/// <see cref="PreviewRenderer.Image"/> every other preview produces.
///
/// The technique is the front-to-back column march — for each screen column, walk the ground away
/// from the camera, project each sample's height to a screen row, and paint the span down to
/// whatever that column has already covered. Occlusion falls out of the walk order, so there is no
/// depth buffer, no triangles and no clipping. It is trivially parallel over columns and it emits a
/// flat RGB buffer, which is what let it drop in beside the existing 2D views rather than dragging
/// in a GPU context and a second rendering path.
///
/// The camera is a genuinely pitched perspective camera, not the shifted-principal-point shortcut
/// column marchers usually take. Shifting the principal point keeps columns exactly vertical, but
/// its depth scale grows with tan(pitch) while the width scale stays put, so the steeper the view
/// the more the map stretched into a tall smear. Projecting every sample through the rotated camera
/// instead gives the correct sin(pitch) foreshortening at every angle, up to and including nearly
/// straight down. The one approximation left is the column's ground track, whose lateral spread is
/// scaled by the depth of the water plane rather than of each sample — that is what keeps a screen
/// column a single march. Its error is proportional to the relief, about one percent of the map's
/// width at the screen edges, and reads as nothing at all.
/// </summary>
public static class HeightfieldRenderer
{
    /// <summary>
    /// Vertical world units per unit of 16-bit height, expressed as a fraction of the map's width
    /// for the full 0..65535 range at exaggeration 1.
    ///
    /// Calibration, not physics: CK3 does not publish the world scale it renders heightmaps at, so
    /// this was set by eye against vanilla's own heightmap with the game open beside it.
    ///
    /// Chosen so that a slider reading of 1.00x *is* the game — not so that 1.00x is some neutral
    /// default the game happens to sit near. A relief slider is only useful if the reader knows
    /// which end of it is the truth, and the number that says so should be the round one.
    /// </summary>
    private const double ReliefFraction = 0.011;

    /// <summary>
    /// The height the colour ramp treats as the top of the scale, on the 0-255 scale: vanilla's own
    /// highest land pixel, the same number <see cref="MapConfig.LandTop"/> defaults to.
    ///
    /// A fixed reference, and that is the whole point of it. The ramp used to be normalised against
    /// the field's own 98th percentile, which made every setting under Height scale invisible here:
    /// <see cref="MapConfig.LandTop"/> multiplies every land pixel by one factor, so the percentile
    /// moves by that factor too and every band lands on exactly the same terrain. A world built to
    /// top out at 100/255 was painted with the same snow caps as one built to top out at 255 —
    /// in the one view the program has for judging relief.
    ///
    /// It also settles a disagreement inside a single frame. The mesh is on CK3's absolute scale
    /// (<see cref="ReliefFraction"/> divides the full 16-bit range), so a low world was already
    /// being drawn flat while being painted alpine — a pancake wearing snow caps. Both halves now
    /// answer to the same ruler, and against vanilla's ruler at that, so two maps' previews are
    /// comparable and 191 reads exactly as high as vanilla's own mountains do.
    ///
    /// The cost is a heightmap on a foreign scale — Normalization Off over an export that never
    /// climbs past 40/255 — which now renders uniformly green rather than showing its shape through
    /// the bands. That map ships as a plateau, this view exists to say so, and the Lambert shading
    /// still carries the shape.
    /// </summary>
    private const double RampTop = 191;

    private const double HorizontalFov = 1.15;

    /// <summary>
    /// The slant orbit radius, in field samples, that frames the whole map at the reference pitch,
    /// before the user's zoom.
    ///
    /// A slant radius and not a ground distance: a pitched camera draws the focus at a scale of
    /// <c>focal</c> over the slant range, so holding the radius fixed while the camera tilts is
    /// what keeps a tilt from reading as an unasked-for zoom.
    ///
    /// Solved once, at a *reference* pitch, then held — a fit evaluated live changes the radius
    /// every time the camera tilts. Yaw-independent too, by taking the map's half-diagonal — the
    /// largest extent any yaw can present. It frames a little loosely at yaws where the map is not
    /// presenting its diagonal, which the wheel is for, and in exchange spinning the map does not
    /// pump the zoom in and out.
    /// </summary>
    private static double OrbitRadius(double extent, double focal, int width, int height)
    {
        // The binding constraint is the near edge: the closest the map comes to the camera must
        // still project above the bottom of the screen. With a the vertical half-angle of the
        // screen and p the pitch, that solves to extent * sin(p + a) / sin(a).
        double a = Math.Atan2(height * 0.5, focal);
        double near = Math.Sin(HeightfieldView.ReferencePitch + a) / Math.Sin(a);

        // And it has to fit sideways at the focus, the widest point of the diagonal.
        double lateral = focal / (width * 0.5);

        return extent * Math.Max(near, lateral);
    }

    /// <summary>How far below the water plane the block is cut, as a fraction of the map's width.</summary>
    private const double BlockThickness = 0.03;

    /// <param name="supersample">
    /// Render at this multiple of the requested size, then box-filter back down. 1 is the draft
    /// path; 2 is what smooths the staircase off ridgelines and coasts on the settled frame.
    /// </param>
    /// <param name="drape">
    /// A rendered map mode to wear as the surface texture instead of the built-in hypsometric
    /// tints — realms over the mountains they sit on, climate over the relief that causes it. Any
    /// size; it is assumed to cover the same extent as the field, which every mode render does.
    /// Sampled nearest-neighbour, because these rasters are classifications and blending across a
    /// realm border invents a realm that does not exist.
    /// </param>
    public static PreviewRenderer.Image Render(Heightfield field, HeightfieldView view,
        int width, int height, int supersample = 1, PreviewRenderer.Image? drape = null)
    {
        width = Math.Max(16, width);
        height = Math.Max(16, height);
        int sw = width * supersample, sh = height * supersample;

        var rgb = new byte[(long)sw * sh * 3];

        int cols = field.Cols, rows = field.Rows;
        var samples = field.Samples;

        const double water = MapDataWriter.WaterLevel16;
        double zScale = field.Cols * ReliefFraction / 65535.0 * Math.Max(0.05, view.Exaggeration);
        double landSpan = Math.Max(1.0, RampTop * MapDataWriter.Step255 - water);

        // Both offsets are in units of the field's width — see HeightfieldView — and the map's own
        // extent is what bounds them, a quarter of a map's overscroll past each edge.
        double focusX = Math.Clamp(cols * 0.5 + cols * view.PanX, -cols * 0.25, cols * 1.25);
        double focusY = Math.Clamp(rows * 0.5 + cols * view.PanY, -rows * 0.25, rows * 1.25);

        double focusZ = water * zScale;
        double baseZ = focusZ - cols * BlockThickness;

        double dirX = Math.Sin(view.Yaw), dirY = Math.Cos(view.Yaw);
        double rightX = Math.Cos(view.Yaw), rightY = -Math.Sin(view.Yaw);

        double sinP = Math.Sin(view.Pitch), cosP = Math.Cos(view.Pitch);
        double focal = sw * 0.5 / Math.Tan(HorizontalFov * 0.5);
        double cy = sh * 0.5;

        double extent = 0.5 * Math.Sqrt((double)cols * cols + (double)rows * rows);
        double radius = OrbitRadius(extent, focal, sw, sh) * Math.Max(0.05, view.Distance);

        double camHoriz = radius * cosP;
        double camZ = focusZ + radius * sinP;
        double camX = focusX - dirX * camHoriz;
        double camY = focusY - dirY * camHoriz;

        // Camera height over the water plane. Sets the marching pace below, and the lateral spread
        // of each column's ground track.
        double above = Math.Max(1.0, camZ - focusZ);

        // Nothing nearer than this can be on screen even at the map's highest point: solve the
        // projection for the screen's bottom row at the height of the tallest land. Negative at a
        // steep pitch, and deliberately so — a camera looking nearly straight down sees ground
        // *behind* its own footprint, so the march has to be allowed to start back there. How far
        // back is bounded by the map itself, there being nothing beyond it to hit.
        double topClear = camZ - field.LandMax * zScale;
        double bottomHalf = sh - 1 - cy;
        double zNear = topClear <= 0 ? 0.5 : Math.Max(-(camHoriz + extent * 2.0),
            topClear * (focal * cosP - bottomHalf * sinP) / (focal * sinP + bottomHalf * cosP));

        // And past the farthest corner the map can reach there is nothing left to hit. When the
        // pitch is steep enough to push the horizon off the top of the screen, the water plane
        // leaves the screen earlier than that — and everything above it leaves even sooner.
        double zFar = camHoriz + extent * 2.0;
        double steep = focal * sinP - cy * cosP;
        if (steep > 0)
            zFar = Math.Min(zFar, above * (focal * cosP + cy * sinP) / steep);

        // Haze by distance from the *camera*, not by march position: the march can start behind
        // the camera's footprint, and at a steep pitch the whole map is roughly equidistant — it
        // should read uniformly crisp from above, and recede only where it actually recedes.
        double fogNear = radius;
        double fogSpan = Math.Max(1.0, extent * 1.6);

        Sky(rgb, sw, sh);

        Parallel.For(0, sw, sx =>
        {
            double lateral = (sx - sw * 0.5) / focal;

            int floorRow = sh;

            for (double z = zNear; z < zFar && floorRow > 0;)
            {
                // The column's ground track. The lateral spread is scaled by the depth of the
                // water plane — the approximation that keeps one screen column one march.
                double depth = z * cosP + above * sinP;

                double px = camX + dirX * z + rightX * lateral * depth;
                double py = camY + dirY * z + rightY * lateral * depth;

                double zs = z;

                // One screen row per step near the camera, coarsening with distance because that
                // is exactly how fast the projection stops resolving it — but never coarser than a
                // hundredth of the distance, or a low camera would step clean over whole ridges.
                z += Math.Clamp(depth * depth / (focal * above), 0.35, Math.Max(2.0, z * 0.01));

                if (px < 0 || py < 0 || px >= cols - 1 || py >= rows - 1) continue;

                double h = Sample(samples, cols, px, py);
                bool sea = h <= water;
                double worldZ = (sea ? water : h) * zScale;

                double dF = zs * cosP + (camZ - worldZ) * sinP;
                if (dF < 1.0) continue;

                int row = (int)(cy - focal * (zs * sinP + (worldZ - camZ) * cosP) / dF);
                if (row >= floorRow) continue;
                if (row < 0) row = 0;

                // The first thing a column hits is the near boundary of the map, and painting it in
                // surface colour smears grass or sea down the whole screen in vertical stripes,
                // because neighbouring columns cross the boundary at different depths. Draw it as
                // what it is: the cut side of a solid block.
                bool cut = floorRow >= sh;

                (byte r, byte g, byte b) = cut ? Earth
                    : drape is { } tex ? Draped(tex, samples, cols, rows, px, py, zScale, sea)
                    : sea ? SeaColour(h, water)
                    : LandColour(h, water, landSpan, Slope(samples, cols, rows, px, py, zScale));

                // Distance haze. Without it the far edge of the map is as saturated as the near
                // edge and the whole thing reads flat.
                double fog = Math.Clamp((dF - fogNear) / fogSpan, 0, 1);
                fog *= fog;

                r = (byte)(r + (SkyTop.R - r) * fog * 0.75);
                g = (byte)(g + (SkyTop.G - g) * fog * 0.75);
                b = (byte)(b + (SkyTop.B - b) * fog * 0.75);

                // The block is cut off at a fixed depth rather than run to the bottom of the screen,
                // so it reads as a slab of world with a bottom to it.
                int bottom = floorRow;
                if (cut)
                {
                    double bF = zs * cosP + (camZ - baseZ) * sinP;
                    int baseRow = bF < 1.0 ? floorRow
                        : (int)(cy - focal * (zs * sinP + (baseZ - camZ) * cosP) / bF);
                    if (baseRow < bottom) bottom = baseRow;
                }

                // The face of the column, shaded down so a tall step reads as a wall rather than as
                // a band of flat colour. Only a genuinely tall step, though: near the camera the
                // march's minimum stride covers a few rows even on flat ground, and putting the
                // gradient on those spans turns calm water into a field of sawtooth scanlines.
                int face = bottom - row;
                bool wall = face > 3;
                for (int y = row; y < bottom; y++)
                {
                    double drop = wall ? (double)(y - row) / face : 0;
                    double shade = 1.0 - drop * 0.45;

                    long o = ((long)y * sw + sx) * 3;
                    rgb[o] = (byte)(r * shade);
                    rgb[o + 1] = (byte)(g * shade);
                    rgb[o + 2] = (byte)(b * shade);
                }

                floorRow = row;
            }
        });

        return supersample <= 1
            ? new PreviewRenderer.Image(rgb, sw, sh)
            : Downscale(rgb, sw, width, height, supersample);
    }

    private static PreviewRenderer.Image Downscale(byte[] src, int srcWidth,
        int width, int height, int ss)
    {
        var dst = new byte[(long)width * height * 3];
        int area = ss * ss;

        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                int r = 0, g = 0, b = 0;
                for (int oy = 0; oy < ss; oy++)
                {
                    long o = (((long)y * ss + oy) * srcWidth + (long)x * ss) * 3;
                    for (int ox = 0; ox < ss; ox++)
                    {
                        r += src[o];
                        g += src[o + 1];
                        b += src[o + 2];
                        o += 3;
                    }
                }

                long d = ((long)y * width + x) * 3;
                dst[d] = (byte)(r / area);
                dst[d + 1] = (byte)(g / area);
                dst[d + 2] = (byte)(b / area);
            }
        });

        return new PreviewRenderer.Image(dst, width, height);
    }

    private static readonly (byte R, byte G, byte B) SkyTop = (26, 32, 44);
    private static readonly (byte R, byte G, byte B) SkyBottom = (58, 68, 86);

    /// <summary>The cut side of the block, where the map runs out.</summary>
    private static readonly (byte R, byte G, byte B) Earth = (78, 66, 56);

    private static void Sky(byte[] rgb, int width, int height)
    {
        Parallel.For(0, height, y =>
        {
            double t = (double)y / Math.Max(1, height - 1);
            byte r = (byte)(SkyTop.R + (SkyBottom.R - SkyTop.R) * t);
            byte g = (byte)(SkyTop.G + (SkyBottom.G - SkyTop.G) * t);
            byte b = (byte)(SkyTop.B + (SkyBottom.B - SkyTop.B) * t);

            long row = (long)y * width * 3;
            for (int x = 0; x < width; x++)
            {
                rgb[row + x * 3] = r;
                rgb[row + x * 3 + 1] = g;
                rgb[row + x * 3 + 2] = b;
            }
        });
    }

    private static double Sample(ushort[] samples, int cols, double px, double py)
    {
        int x0 = (int)px, y0 = (int)py;
        double fx = px - x0, fy = py - y0;

        long a = (long)y0 * cols + x0;
        long b = a + cols;

        double top = samples[a] + (samples[a + 1] - samples[a]) * fx;
        double bottom = samples[b] + (samples[b + 1] - samples[b]) * fx;
        return top + (bottom - top) * fy;
    }

    /// <summary>
    /// The drape's colour under this sample, lit by the terrain. Land keeps the same Lambert
    /// shading the hypsometric tints get, so ridges and valleys still read through a flat realm
    /// colour; the sea is left as the drape painted it — the drape's water is already the water.
    /// </summary>
    private static (byte, byte, byte) Draped(PreviewRenderer.Image tex, ushort[] samples,
        int cols, int rows, double px, double py, double zScale, bool sea)
    {
        int tx = Math.Min(tex.Width - 1, (int)(px * tex.Width / cols));

        // The field runs +Y northward — Downsample flips the image's top-down rows on the way in —
        // but a mode render is still a top-down image, so the drape flips back here or the world
        // wears its arctic on the equator.
        int ty = tex.Height - 1 - Math.Min(tex.Height - 1, (int)(py * tex.Height / rows));

        long at = ((long)ty * tex.Width + tx) * 3;
        byte r = tex.Rgb[at], g = tex.Rgb[at + 1], b = tex.Rgb[at + 2];
        if (sea) return (r, g, b);

        double lit = Slope(samples, cols, rows, px, py, zScale);
        return ((byte)Math.Min(255, r * lit), (byte)Math.Min(255, g * lit),
                (byte)Math.Min(255, b * lit));
    }

    /// <summary>Lambert term from the local gradient, lit from the north-west as maps always are.</summary>
    private static double Slope(ushort[] samples, int cols, int rows, double px, double py,
        double zScale)
    {
        // Bilinear central differences rather than the nearest cell's: a point-sampled gradient
        // snaps once per cell, and the lighting cracks into tiles as soon as the camera gets close.
        double x = Math.Clamp(px, 1.0, cols - 2.001);
        double y = Math.Clamp(py, 1.0, rows - 2.001);

        double dx = (Sample(samples, cols, x + 1, y) - Sample(samples, cols, x - 1, y)) * 0.5 * zScale;
        double dy = (Sample(samples, cols, x, y + 1) - Sample(samples, cols, x, y - 1)) * 0.5 * zScale;

        // Normal is (-dx, -dy, 1) unnormalised. The field runs +Y northward, so a light in the
        // north-west sits at (-0.55, +0.55, 0.63) and the dot product turns the Y term negative.
        double nz = 1.0 / Math.Sqrt(dx * dx + dy * dy + 1.0);
        double lambert = (dx * 0.55 - dy * 0.55 + 0.63) * nz;

        return Math.Clamp(0.55 + lambert * 0.75, 0.30, 1.35);
    }

    /// <summary>
    /// The same bands <see cref="PreviewRenderer.RenderRelief"/> paints, so the 3D view and the
    /// Relief view agree about what is high ground.
    ///
    /// Normalised against <see cref="RampTop"/>, a fixed height on CK3's own scale, so that a
    /// lower world reads as a lower world instead of being restretched back to snow.
    /// </summary>
    private static (byte R, byte G, byte B) LandColour(double h, double water, double landSpan,
        double shade)
    {
        double t = Math.Clamp((h - water) / landSpan, 0, 1);

        var (r, g, b) = t < 0.10 ? (116, 146, 86)
            : t < 0.28 ? (92, 124, 68)
            : t < 0.48 ? (140, 128, 84)
            : t < 0.70 ? (128, 112, 98)
            : (232, 234, 238);

        return ((byte)Math.Clamp(r * shade, 0, 255),
                (byte)Math.Clamp(g * shade, 0, 255),
                (byte)Math.Clamp(b * shade, 0, 255));
    }

    /// <summary>
    /// Flat water at exactly the plane CK3 floods to, tinted by how deep the floor under it is.
    ///
    /// Flat is the point. CK3 draws one sea plane at 19/255 and everything below it is invisible, so
    /// a preview that draws the bathymetry as visible relief is showing geometry the game will never
    /// render — and, worse, hides the thing worth checking, which is where the plane cuts the land.
    /// </summary>
    private static (byte R, byte G, byte B) SeaColour(double h, double water)
    {
        double depth = Math.Clamp((water - h) / Math.Max(1.0, water), 0, 1);
        return ((byte)(64 - 26 * depth), (byte)(96 - 38 * depth), (byte)(132 - 44 * depth));
    }
}
