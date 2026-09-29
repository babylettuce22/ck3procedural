using Ck3MapGen.Core;

namespace Ck3MapGen.AppGUI;

/// <summary>
/// The long set-pieces: features drawn along a line rather than around a point. Each is a path
/// across the map and a cross-section swept along it, written into the same three layers the
/// craters use (see <see cref="QuickFeatures"/>).
/// <list type="bullet">
/// <item><b>The Rift</b>: a continent pulling apart, as East Africa is. A sunken valley floor
/// between two escarpments on raised shoulders, long lakes strung along it, and often a gulf
/// where the sea has already got in at one end, as it has in the Red Sea.</item>
/// <item><b>The Spine</b>: one great unbroken range the length of a continent, like the Andes:
/// close to the sea on one side, falling steeply to it, and a long descent through lower
/// parallel ranges and foothills to the interior on the other.</item>
/// <item><b>The Wall</b>: a sheer, near-straight barrier from coast to coast, flat-topped and even
/// in height, splitting a continent in two, save for a few saddles where it narrows and drops.
/// Those are the ways through: the auto-cut leaves them open, or the pass survey cuts them.</item>
/// </list>
/// </summary>
public static partial class QuickFeatures
{
    // ================================================================ shapes

    /// <summary>A feature drawn along <see cref="Path"/>; <see cref="Width"/> is its core half-width.</summary>
    private abstract record Line(Polyline Path, double Width) : Shape;

    /// <param name="SeaEnd">The share of the rift, from its start, the sea has flooded; 0 for none.</param>
    /// <param name="Lakes">Where along the rift each lake lies, and its length, as shares of the rift.</param>
    /// <param name="Breadth">Slow waves in the rift's width along its length, so it pinches and swells.</param>
    private sealed record Rift(Polyline Path, double Width, double SeaEnd, (double At, double Length)[] Lakes,
        (double K, double Phase, double Amount)[] Breadth) : Line(Path, Width);

    /// <param name="CoastSide">Which side of the path the sea comes close on: +1 left of travel, -1 right.</param>
    private sealed record Spine(Polyline Path, double Width, int CoastSide) : Line(Path, Width);

    /// <param name="Breadth">Slow waves in the wall's width, so it thickens into massifs and thins between.</param>
    /// <param name="Saddles">Where along the wall it pinches and drops, as shares of its length.</param>
    private sealed record Wall(Polyline Path, double Width, (double K, double Phase, double Amount)[] Breadth,
        double[] Saddles) : Line(Path, Width);

    // ================================================================ placement

    /// <summary>
    /// A rift across the middle of the map, on a diagonal, short enough that neither end runs off
    /// the top or bottom. When the sea has come in, it has come in from the end nearer the edge of
    /// the map, and the rift is carried on out to that edge so the gulf meets the open ocean.
    /// </summary>
    private static Rift MakeRift(Rng rng, double aspect)
    {
        double angle = Lerp(0.4, 1.05, rng.NextDouble()) * (rng.NextDouble() < 0.5 ? 1 : -1);
        double length = Math.Min(Lerp(1.1, 1.4, rng.NextDouble()), 0.78 / Math.Abs(Math.Sin(angle)));
        double cx = Lerp(0.42, 0.58, rng.NextDouble()) * aspect;
        double cy = Lerp(0.45, 0.55, rng.NextDouble());
        double dx = Math.Cos(angle), dy = Math.Sin(angle);

        var a = (X: cx - dx * length / 2, Y: cy - dy * length / 2);
        var b = (X: cx + dx * length / 2, Y: cy + dy * length / 2);

        bool flooded = rng.NextDouble() < 0.6;
        if (flooded)
        {
            // The sea end is the start of the path, so it is the one nearer an edge, carried on to it.
            if (EdgeDistance(b.X, b.Y, aspect) < EdgeDistance(a.X, a.Y, aspect)) (a, b) = (b, a);
            double ux = a.X - b.X, uy = a.Y - b.Y, norm = Math.Sqrt(ux * ux + uy * uy);
            ux /= norm; uy /= norm;
            double reach = RayToEdge(a.X, a.Y, ux, uy, aspect) - 0.02;
            if (reach > 0) a = (a.X + ux * reach, a.Y + uy * reach);
        }

        var path = Polyline.Wander(rng, a, b, bow: Lerp(-0.08, 0.08, rng.NextDouble()), wiggle: 0.035);
        double seaEnd = 0;
        if (flooded)
        {
            // The added stretch out to the edge is sea, and so is a share of the rift beyond it.
            double added = 1 - Math.Min(1, length / path.Length);
            seaEnd = Math.Min(0.75, added + Lerp(0.15, 0.3, rng.NextDouble()));
        }

        int lakeCount = rng.Int(1, 3);
        var lakes = new (double, double)[lakeCount];
        for (int i = 0; i < lakeCount; i++)
            lakes[i] = (Lerp(Math.Max(seaEnd + 0.08, 0.12), 0.9, rng.NextDouble()), Lerp(0.03, 0.07, rng.NextDouble()));

        var breadth = new[] { 2.0, 3.0, 7.0 }
            .Select(k => (k, rng.NextDouble() * Math.Tau, Lerp(0.08, 0.16, rng.NextDouble()) * 3 / k))
            .ToArray();
        return new Rift(path, Lerp(0.028, 0.036, rng.NextDouble()), seaEnd, lakes, breadth);
    }

    /// <summary>
    /// A spine along the long way of the map, gently bowed, its ends kept well inside the edges so
    /// the continent can round them off. The sea side is drawn at random.
    /// </summary>
    private static Spine MakeSpine(Rng rng, double aspect)
    {
        double angle = Lerp(-0.45, 0.45, rng.NextDouble());
        double length = Lerp(1.45, 1.75, rng.NextDouble());
        length = Math.Min(length, (aspect - 0.36) / Math.Cos(angle));
        if (Math.Abs(Math.Sin(angle)) > 1e-6) length = Math.Min(length, 0.72 / Math.Abs(Math.Sin(angle)));

        double cx = aspect / 2 + Lerp(-0.12, 0.12, rng.NextDouble());
        double cy = Lerp(0.42, 0.58, rng.NextDouble());
        double dx = Math.Cos(angle), dy = Math.Sin(angle);
        var a = (cx - dx * length / 2, cy - dy * length / 2);
        var b = (cx + dx * length / 2, cy + dy * length / 2);

        var path = Polyline.Wander(rng, a, b, bow: Lerp(0.04, 0.12, rng.NextDouble()) * (rng.NextDouble() < 0.5 ? 1 : -1), wiggle: 0.022);
        return new Spine(path, Lerp(0.03, 0.038, rng.NextDouble()), rng.NextDouble() < 0.5 ? 1 : -1);
    }

    /// <summary>
    /// A wall across the whole map, from beyond one edge to beyond the other, a third of the way
    /// down or so: the lands north of it are the smaller share, as they are beyond every wall
    /// worth building. Nearly straight, as built things are.
    /// </summary>
    private static Wall MakeWall(Rng rng, double aspect)
    {
        double y0 = Lerp(0.27, 0.40, rng.NextDouble());
        double y1 = y0 + Lerp(-0.06, 0.06, rng.NextDouble());
        var path = Polyline.Wander(rng, (-0.1, y0), (aspect + 0.1, y1),
            bow: Lerp(-0.07, 0.07, rng.NextDouble()), wiggle: 0.014);
        var breadth = new[] { 3.0, 5.0, 11.0 }
            .Select(k => (k, rng.NextDouble() * Math.Tau, Lerp(0.06, 0.12, rng.NextDouble()) * 3 / k))
            .ToArray();

        // The saddles, spread along the stretch that is over land (the path runs off both edges),
        // each jittered within its share so they do not fall evenly. At most three, because the
        // pass survey cuts at most three passes through one wall.
        int count = rng.Int(2, 3);
        var saddles = new double[count];
        for (int i = 0; i < count; i++)
            saddles[i] = Lerp(0.18, 0.82, (i + 0.5 + Lerp(-0.3, 0.3, rng.NextDouble())) / count);
        return new Wall(path, Lerp(0.026, 0.032, rng.NextDouble()), breadth, saddles);
    }

    /// <summary>How far a point is from the nearest edge of the map.</summary>
    private static double EdgeDistance(double x, double y, double aspect)
        => Math.Min(Math.Min(x, aspect - x), Math.Min(y, 1 - y));

    /// <summary>How far along a direction a point can go before it leaves the map.</summary>
    private static double RayToEdge(double x, double y, double ux, double uy, double aspect)
    {
        double t = double.MaxValue;
        if (ux > 1e-9) t = Math.Min(t, (aspect - x) / ux);
        if (ux < -1e-9) t = Math.Min(t, -x / ux);
        if (uy > 1e-9) t = Math.Min(t, (1 - y) / uy);
        if (uy < -1e-9) t = Math.Min(t, -y / uy);
        return t;
    }

    // ================================================================ drawing

    private static Sample? Evaluate(Line line, double px, double py)
    {
        var (distance, t, side) = line.Path.Closest(px, py);
        double d = distance / line.Width;
        return line switch
        {
            Rift rift => RiftSection(rift, d, t),
            Spine spine => SpineSection(spine, d * side * spine.CoastSide, t),
            Wall wall => WallSection(wall, d, t),
            _ => null,
        };
    }

    /// <summary>
    /// Across a rift, by distance from its axis in floor half-widths: the flat floor, a smoothed
    /// band where it meets the walls, the escarpments standing on shoulders raised above the
    /// country around, and a broad band of land holding it all.
    /// </summary>
    private static Sample RiftSection(Rift rift, double d, double t)
    {
        // The rift fades out at a dry end; at a flooded one it runs on into the gulf.
        double taper = (rift.SeaEnd > 0 ? 1 : SmoothStep(0, 0.1, t)) * SmoothStep(1, 0.9, t);
        double sea = rift.SeaEnd > 0 ? 1 - SmoothStep(rift.SeaEnd - 0.06, rift.SeaEnd, t) : 0;

        // The rift pinches and swells along its length, and where the sea is in, it has spread
        // wider the nearer the open ocean, as a young ocean does.
        double breadth = 1;
        foreach (var (k, phase, amount) in rift.Breadth) breadth += amount * Math.Sin(k * Math.PI * t + phase);
        if (rift.SeaEnd > 0) breadth *= 1 + 1.3 * sea * (1 - Math.Min(1, t / rift.SeaEnd));
        d /= breadth;

        double land = 0.3 * Gauss(d / 7) * SmoothStep(0, 0.05, t) * SmoothStep(1, 0.95, t);
        // Soft-edged, so the Continents noise draws the actual shores rather than the brush.
        double gulf = -0.75 * (1 - SmoothStep(0.3, 1.25, d)) * sea;
        double lake = 0;
        foreach (var (at, length) in rift.Lakes)
            lake = Math.Max(lake, (1 - SmoothStep(0.05, 0.8, d)) * Gauss((t - at) / length));

        // How much of the floor here is under water. The flat floor is for dry land only: the
        // flatten brush pulls toward a level just above the sea, and on the seabed the waterline
        // rule stops it just under the surface, which left the gulf and the lakes a sheet of
        // barely-submerged shallows. Water gets a trough instead, deepest down the axis and
        // shoaling smoothly to its shores, as a young sea's floor does.
        double water = Math.Max(sea, lake);
        double dry = 1 - water;

        float flatten = (float)(0.9 * (1 - SmoothStep(0.45, 0.85, d)) * taper * dry);
        float smooth = (float)(0.7 * Gauss((d - 0.85) / 0.3) * taper * dry);
        double floor = -0.45 * (1 - SmoothStep(0.6, 1.15, d)) * dry;
        double trough = -0.6 * Gauss(d / 0.9) * water;
        double shoulders = 0.28 * Gauss((d - 1.8) / 1.1);
        float delta = (float)((floor + trough + shoulders) * taper);

        // Steep toward the floor, a long fall away from it; lower where the gulf has opened.
        double scarp = Gauss((d - 1.3) / (d < 1.3 ? 0.2 : 0.55));
        float range = (float)(0.85 * scarp * taper * (1 - 0.7 * sea));

        return new Sample((float)(land + gulf - 0.62 * lake), delta, flatten, smooth, range, 0);
    }

    /// <summary>
    /// Across a spine, by signed distance from its crest in crest half-widths, positive toward the
    /// sea: the main range, a lower parallel range inland for most of its length, foothills that
    /// run far inland and only a little way seaward, and land to match: a narrow coastal strip
    /// before the sea on one side, a whole continent on the other.
    /// </summary>
    private static Sample SpineSection(Spine spine, double d, double t)
    {
        double taper = SmoothStep(0, 0.12, t) * SmoothStep(1, 0.88, t);
        double front = SmoothStep(0.18, 0.32, t) * SmoothStep(0.86, 0.7, t);

        double crest = Gauss(d / 1.15);
        double cordillera = 0.62 * Gauss((d + 2.7) / 0.6) * front;
        float range = (float)(Math.Max(crest, cordillera) * taper);

        float delta = (float)(0.32 * Gauss(d / (d < 0 ? 4.5 : 1.8)) * taper);

        double ends = SmoothStep(0, 0.06, t) * SmoothStep(1, 0.94, t);
        double coast = d < 0
            ? 0.28 * Gauss(d / 14) * ends
            : (0.4 * Gauss(d / 2.2) - 0.42 * SmoothStep(2.6, 4.2, d) * Gauss(d / 14)) * ends;

        return new Sample((float)coast, delta, 0, 0, range, 0);
    }

    /// <summary>
    /// Across the wall, in core half-widths: a flat top at full height, a short steep face, a low
    /// glacis at its feet, and land on both sides of it the whole way.
    ///
    /// At a saddle the wall pinches to about a third of its width but keeps most of its height.
    /// The whole wall is some four baronies thick, and the pass survey only cuts where a wall is at
    /// most 1.5 (MapConfig.MountainPassMaxThickness), so without them an 8192 world came out sealed
    /// coast to coast. Thin and still high, a saddle stays part of the wall, and the survey, which
    /// tries a wall's thinnest necks first, cuts its passes there: the ways through are proper
    /// passes, named and fortified, rather than unmarked gaps. (Low saddles, tried first, left the
    /// wall in pieces with open gaps and a pass through each piece besides: nine ways through.)
    /// </summary>
    private static Sample WallSection(Wall wall, double d, double t)
    {
        double breadth = 1;
        foreach (var (k, phase, amount) in wall.Breadth) breadth += amount * Math.Sin(k * Math.PI * t + phase);
        double saddle = 0;
        foreach (double at in wall.Saddles) saddle = Math.Max(saddle, Gauss((t - at) / 0.012));
        double landReach = d / 12;
        d /= breadth * (1 - 0.65 * saddle);

        float range = (float)((1 - SmoothStep(0.45, 1.05, d)) * (1 - 0.2 * saddle));
        float delta = (float)(0.18 * Gauss(d / 3) * (1 - 0.5 * saddle));
        float coast = (float)(0.25 * Gauss(landReach));
        return new Sample(coast, delta, 0, 0, range, 0);
    }

    // ================================================================ the path

    /// <summary>
    /// A line across the map as a chain of short segments, with the one query the sections need:
    /// how far a point is from it, how far along it that nearest point lies, and on which side.
    /// Segments are grouped into runs with bounding boxes, so a run that cannot hold anything
    /// nearer than what was already found is skipped whole.
    /// </summary>
    private sealed class Polyline
    {
        private const int Points = 257;
        private const int Run = 16;

        private readonly double[] _x, _y, _along;
        private readonly (double X0, double Y0, double X1, double Y1)[] _runs;

        public double Length { get; }

        private Polyline(double[] x, double[] y)
        {
            _x = x;
            _y = y;
            _along = new double[x.Length];
            for (int i = 1; i < x.Length; i++)
                _along[i] = _along[i - 1] + Math.Sqrt(Sq(x[i] - x[i - 1]) + Sq(y[i] - y[i - 1]));
            Length = _along[^1];

            int segments = x.Length - 1;
            _runs = new (double, double, double, double)[(segments + Run - 1) / Run];
            for (int r = 0; r < _runs.Length; r++)
            {
                int from = r * Run, to = Math.Min(segments, from + Run);
                double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
                for (int i = from; i <= to; i++)
                {
                    x0 = Math.Min(x0, x[i]); x1 = Math.Max(x1, x[i]);
                    y0 = Math.Min(y0, y[i]); y1 = Math.Max(y1, y[i]);
                }
                _runs[r] = (x0, y0, x1, y1);
            }
        }

        /// <summary>
        /// From <paramref name="a"/> to <paramref name="b"/>, bowed to one side by
        /// <paramref name="bow"/> at the middle, and wandering by up to about
        /// <paramref name="wiggle"/> either side of that in a few long waves.
        /// </summary>
        public static Polyline Wander(Rng rng, (double X, double Y) a, (double X, double Y) b, double bow, double wiggle)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, length = Math.Sqrt(dx * dx + dy * dy);
            double nx = -dy / length, ny = dx / length;
            var waves = new[] { 2.0, 3.0, 5.0, 8.0 }
                .Select(k => (K: k, Phase: rng.NextDouble() * Math.Tau, Amount: wiggle * Lerp(0.5, 1, rng.NextDouble()) * 2 / k))
                .ToArray();

            var x = new double[Points];
            var y = new double[Points];
            for (int i = 0; i < Points; i++)
            {
                double t = (double)i / (Points - 1);
                double off = bow * Math.Sin(Math.PI * t);
                foreach (var (k, phase, amount) in waves) off += amount * Math.Sin(k * Math.PI * t + phase);
                x[i] = a.X + dx * t + nx * off;
                y[i] = a.Y + dy * t + ny * off;
            }
            return new Polyline(x, y);
        }

        /// <summary>
        /// Distance from the point to the line; how far along the line the nearest point is, from
        /// 0 at the start to 1 at the end; and +1 if the point is to the left of travel, -1 if right.
        /// </summary>
        public (double Distance, double T, int Side) Closest(double px, double py)
        {
            double best = double.MaxValue, bestAlong = 0;
            int bestSide = 1;
            for (int r = 0; r < _runs.Length; r++)
            {
                var (x0, y0, x1, y1) = _runs[r];
                double ox = px < x0 ? x0 - px : px > x1 ? px - x1 : 0;
                double oy = py < y0 ? y0 - py : py > y1 ? py - y1 : 0;
                if (ox * ox + oy * oy >= best) continue;

                int from = r * Run, to = Math.Min(_x.Length - 1, from + Run);
                for (int i = from; i < to; i++)
                {
                    double ax = _x[i], ay = _y[i], sx = _x[i + 1] - ax, sy = _y[i + 1] - ay;
                    double ss = sx * sx + sy * sy;
                    double u = ss > 0 ? Math.Clamp(((px - ax) * sx + (py - ay) * sy) / ss, 0, 1) : 0;
                    double qx = ax + sx * u - px, qy = ay + sy * u - py;
                    double dd = qx * qx + qy * qy;
                    if (dd >= best) continue;
                    best = dd;
                    bestAlong = _along[i] + u * Math.Sqrt(ss);
                    bestSide = sx * (py - ay) - sy * (px - ax) >= 0 ? 1 : -1;
                }
            }
            return (Math.Sqrt(best), Length > 0 ? bestAlong / Length : 0, bestSide);
        }

        /// <summary>
        /// The stretch of the line from share <paramref name="from"/> to share <paramref name="to"/>
        /// of its length, moved <paramref name="distance"/> to one side: positive to the side
        /// <see cref="Closest"/> calls +1. A lettering baseline, so it is only as fine as that needs.
        /// </summary>
        public List<(double X, double Y)> Offset(double distance, double from, double to)
        {
            var points = new List<(double, double)>();
            for (int i = 0; i < _x.Length; i += 4)
            {
                double t = _along[i] / Length;
                if (t < from || t > to) continue;
                int a = Math.Max(0, i - 4), b = Math.Min(_x.Length - 1, i + 4);
                double sx = _x[b] - _x[a], sy = _y[b] - _y[a], norm = Math.Sqrt(sx * sx + sy * sy);
                if (norm <= 0) continue;
                points.Add((_x[i] - sy / norm * distance, _y[i] + sx / norm * distance));
            }
            return points;
        }

        private static double Sq(double v) => v * v;
    }
}
