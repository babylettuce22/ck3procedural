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
    /// The 98th percentile of land, which the colour ramp is normalised against.
    ///
    /// A percentile and not <see cref="LandMax"/>: one freak peak — and an imported heightmap
    /// usually has one — would otherwise set the top of the ramp and push every real mountain range
    /// down into the green.
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
        // Stops short of straight down: the projection is an off-axis one, and the principal point
        // runs away to infinity as the pitch approaches vertical.
        Pitch = Math.Clamp(Pitch + dPitch, 0.12, 1.15),
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
/// The camera is off-axis rather than rotated: pitch is applied by sliding the principal point up
/// the screen instead of turning the view vector. That is a real camera — a tilt-shift lens — not
/// an approximation, and it is what keeps every terrain column vertical and one pixel wide. Its one
/// limit is that it cannot look straight down, which <see cref="HeightfieldView.Orbited"/> clamps.
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

    private const double HorizontalFov = 1.15;

    /// <summary>
    /// How far back over the ground the camera sits, in field samples, before the user's zoom.
    ///
    /// Ground distance, not slant range. In this projection <c>z</c> is horizontal depth, so a
    /// point's scale is <c>focal / z</c> and it is the *horizontal* distance that decides how big
    /// the map draws. Swinging the camera at a constant slant radius shortens that horizontal leg
    /// as the pitch rises, which magnifies the map instead of leaving it alone. Holding the ground
    /// distance fixed and letting the height follow from the pitch keeps the scale put — and the
    /// focus lands on the middle row of the screen at every pitch, because the height and the
    /// horizon offset both scale with tan(pitch) and cancel.
    ///
    /// Solved once, at a *reference* pitch, then held: an off-axis camera sees a band of ground
    /// whose edges both move with pitch, so a fit evaluated live changes the distance every time
    /// the camera tilts and tilting reads as an unasked-for zoom.
    ///
    /// Yaw-independent for the same reason, by taking the map's half-diagonal — the largest value
    /// the extent along or across the view can reach — for both axes. It frames a little loosely at
    /// yaws where the map is not presenting its diagonal, which the wheel is for, and in exchange
    /// spinning the map does not pump the zoom in and out.
    /// </summary>
    private static double GroundDistance(int cols, int rows, double focal, int width, int height)
    {
        double extent = 0.5 * Math.Sqrt((double)cols * cols + (double)rows * rows);

        double half = height * 0.5;
        double k = focal * Math.Tan(HeightfieldView.ReferencePitch);

        // Near edge: the map's nearest corner must still project above the bottom of the screen.
        double near = extent * (half + k) / half;

        // Far edge: its furthest corner must stay below the horizon. Only binds once the horizon is
        // off the top of the screen, which is what k > half means.
        double far = k > half ? extent * (k - half) / half : 0;

        // And it has to fit sideways.
        double lateral = extent * focal / (width * 0.5);

        return Math.Max(Math.Max(near, far), lateral);
    }

    /// <summary>How far below the water plane the block is cut, as a fraction of the map's width.</summary>
    private const double BlockThickness = 0.03;

    public static PreviewRenderer.Image Render(Heightfield field, HeightfieldView view,
        int width, int height)
    {
        width = Math.Max(16, width);
        height = Math.Max(16, height);

        var rgb = new byte[(long)width * height * 3];

        int cols = field.Cols, rows = field.Rows;
        var samples = field.Samples;

        const double water = MapDataWriter.WaterLevel16;
        double zScale = field.Cols * ReliefFraction / 65535.0 * Math.Max(0.05, view.Exaggeration);
        double landSpan = Math.Max(1.0, field.LandTop - water);

        // Both offsets are in units of the field's width — see HeightfieldView — and the map's own
        // extent is what bounds them, a quarter of a map's overscroll past each edge.
        double focusX = Math.Clamp(cols * 0.5 + cols * view.PanX, -cols * 0.25, cols * 1.25);
        double focusY = Math.Clamp(rows * 0.5 + cols * view.PanY, -rows * 0.25, rows * 1.25);

        double focusZ = water * zScale;
        double baseZ = focusZ - cols * BlockThickness;

        double dirX = Math.Sin(view.Yaw), dirY = Math.Cos(view.Yaw);
        double rightX = Math.Cos(view.Yaw), rightY = -Math.Sin(view.Yaw);

        double focal = width * 0.5 / Math.Tan(HorizontalFov * 0.5);

        // Pitch as a principal-point offset. Looking down pushes the horizon off the top of the
        // screen, which is why this is negative and why the far plane below is finite.
        double k = focal * Math.Tan(view.Pitch);
        double horizon = height * 0.5 - k;

        double camHoriz = GroundDistance(cols, rows, focal, width, height)
                          * Math.Max(0.05, view.Distance);

        double camZ = focusZ + camHoriz * Math.Tan(view.Pitch);
        double camX = focusX - dirX * camHoriz;
        double camY = focusY - dirY * camHoriz;

        double span = camHoriz;

        double topZ = camZ - field.LandMax * zScale;
        double zNear = Math.Max(1.0, topZ * focal / Math.Max(1.0, height - 1 - horizon));
        double zFar = horizon < -1.0
            ? camZ * focal / -horizon
            : Math.Max(cols, rows) * 4.0;
        zFar = Math.Min(zFar, (Math.Max(cols, rows) + span) * 2.5);

        Sky(rgb, width, height);

        Parallel.For(0, width, sx =>
        {
            double u = sx - width * 0.5;
            double lateral = u / focal;

            int floorRow = height;

            for (double z = zNear; z < zFar && floorRow > 0;)
            {
                double px = camX + dirX * z + rightX * lateral * z;
                double py = camY + dirY * z + rightY * lateral * z;

                // One screen row per step near the camera, coarsening with distance because that
                // is exactly how fast the projection stops resolving it.
                double dz = Math.Max(0.5, z * z / Math.Max(1.0, camZ * focal));
                z += dz;

                if (px < 0 || py < 0 || px >= cols - 1 || py >= rows - 1) continue;

                double h = Sample(samples, cols, px, py);
                bool sea = h <= water;
                double worldZ = (sea ? water : h) * zScale;

                int row = (int)(horizon + (camZ - worldZ) * focal / z);
                if (row >= floorRow) continue;
                if (row < 0) row = 0;

                // The first thing a column hits is the near boundary of the map, and painting it in
                // surface colour smears grass or sea down the whole screen in vertical stripes,
                // because neighbouring columns cross the boundary at different depths. Draw it as
                // what it is: the cut side of a solid block.
                bool cut = floorRow >= height;

                var (r, g, b) = cut ? Earth
                    : sea ? SeaColour(h, water)
                    : LandColour(h, water, landSpan, Slope(samples, cols, rows, px, py, zScale));

                // Distance haze. Without it the far edge of the map is as saturated as the near
                // edge and the whole thing reads flat.
                double fog = Math.Clamp((z - zNear) / Math.Max(1.0, zFar - zNear), 0, 1);
                fog *= fog;

                r = (byte)(r + (SkyTop.R - r) * fog * 0.75);
                g = (byte)(g + (SkyTop.G - g) * fog * 0.75);
                b = (byte)(b + (SkyTop.B - b) * fog * 0.75);

                // The block is cut off at a fixed depth rather than run to the bottom of the screen,
                // so it reads as a slab of world with a bottom to it.
                int bottom = floorRow;
                if (cut)
                {
                    int baseRow = (int)(horizon + (camZ - baseZ) * focal / z);
                    if (baseRow < bottom) bottom = baseRow;
                }

                // The face of the column, shaded down so a tall step reads as a wall rather than as
                // a band of flat colour.
                int face = bottom - row;
                for (int y = row; y < bottom; y++)
                {
                    double drop = face <= 1 ? 0 : (double)(y - row) / face;
                    double shade = 1.0 - drop * 0.45;

                    long o = ((long)y * width + sx) * 3;
                    rgb[o] = (byte)(r * shade);
                    rgb[o + 1] = (byte)(g * shade);
                    rgb[o + 2] = (byte)(b * shade);
                }

                floorRow = row;
            }
        });

        return new PreviewRenderer.Image(rgb, width, height);
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

    /// <summary>Lambert term from the local gradient, lit from the north-west as maps always are.</summary>
    private static double Slope(ushort[] samples, int cols, int rows, double px, double py,
        double zScale)
    {
        int x = Math.Clamp((int)px, 1, cols - 2);
        int y = Math.Clamp((int)py, 1, rows - 2);

        long i = (long)y * cols + x;
        double dx = (samples[i + 1] - samples[i - 1]) * 0.5 * zScale;
        double dy = (samples[i + cols] - samples[i - cols]) * 0.5 * zScale;

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
    /// Normalised against the map's own highest land rather than against the full 16-bit range: a
    /// heightmap that only climbs to a quarter of the scale is not a flat green world, it is a
    /// world with lower mountains, and the ramp has to show its shape either way.
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
