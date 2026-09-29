using Ck3MapGen.Core;
using NoiseTool.Pipeline;
using NoiseTool.Stages;

namespace Ck3MapGen.AppGUI;

/// <summary>
/// A set-piece a map type is built around, drawn fresh for every seed. None for the map types
/// whose shape is the noise's own.
/// </summary>
public enum QuickFeature { None, Crater, FloodedCrater, Scar, Rift, Spine, Wall }

/// <summary>
/// Draws a map type's set-piece into its preset's paint layers, placed by the world seed.
///
/// A guide saved with a preset is fixed: the seed moves every coastline and ridge but never the
/// painted shape, so a crater preset on its own would put its crater in the same place on every
/// world. These presets instead leave their shapes to be drawn here, after the preset is loaded
/// and before it runs, so the crater's position, size, tilt, breach and central peak all come
/// from the seed. The presets still ship a guide drawn at their own master seed, so opening one
/// in the Terrain workspace shows the feature and it can be repainted by hand from there.
///
/// Three layers carry a crater, one per stage the preset lines up for it:
/// <list type="bullet">
/// <item><b>Continents, coast.</b> A bias on where land is: land under the crater and its
/// ejecta so the set-piece is not lost to the sea, and for a flooded crater a sea inside the rim,
/// an island on the central peak and a strait through the breach. Only this stage may move the
/// coastline, so everything about land and sea is decided here.</item>
/// <item><b>Hand Paint (relief).</b> The bowl: lowered inside the rim, its floor flattened toward
/// one level, and the ejecta apron raised around the outside.</item>
/// <item><b>Mountain Range (painted).</b> The rim as a ring of mountains, broken where the breach
/// is, and the central peak. The generator then treats the rim as any other range: tall enough
/// and it becomes an impassable wall, and the pass survey cuts its way through.</item>
/// </list>
/// </summary>
public static partial class QuickFeatures
{
    /// <summary>The Rng stream for a feature's placement; see <see cref="Rng.For(int,int,int,int)"/>.</summary>
    private const int Stream = 0x4C7A;

    /// <summary>
    /// Height of a crater's floor above the sea, in 0..1 map units: a low plain a little above
    /// the coast, so the bowl reads as sunken without dropping below the waterline, which the
    /// Hand Paint stage would not allow anyway.
    /// </summary>
    private const float FloorAboveSea = 0.022f;

    /// <summary>One crater, in map-height units with x stretched by the map's aspect.</summary>
    /// <param name="Tilt">Direction of the long axis of an oblique impact's ellipse, in radians.</param>
    /// <param name="Squash">Short axis over long axis: 1 is round.</param>
    /// <param name="Breaches">Directions in which the rim is broken, in radians.</param>
    /// <param name="Wobble">Phases and strengths of the rim's low-order unevenness.</param>
    /// <param name="Secondary">One of a crater field's small thrown-out craters, not part of the
    /// field's main line.</param>
    private sealed record Crater(
        double X, double Y, double Radius, double Tilt, double Squash,
        double[] Breaches, bool Peak, bool Flooded, (double Order, double Phase, double Amount)[] Wobble,
        bool Secondary = false) : Shape;

    /// <summary>Anything drawn: a crater here, or one of the long features in QuickFeatures.Lines.</summary>
    private abstract record Shape;

    /// <summary>
    /// Draws <paramref name="feature"/> into the pipeline's paint layers for this seed. Stages the
    /// pipeline lacks are skipped, so a preset missing one simply draws without it.
    /// </summary>
    public static void Draw(HeightPipeline pipeline, QuickFeature feature, int seed)
    {
        double aspect = pipeline.BaseHeight > 0 ? (double)pipeline.BaseWidth / pipeline.BaseHeight : 2.0;
        Render(pipeline, Shapes(feature, seed, aspect), aspect);
    }

    /// <summary>
    /// The shapes <see cref="Draw"/> paints for this feature and seed. The same every time, which is
    /// what lets <see cref="Landmarks"/> find them again after the map is made.
    /// </summary>
    private static Shape[] Shapes(QuickFeature feature, int seed, double aspect)
    {
        if (feature == QuickFeature.None) return [];
        var rng = Rng.For(seed, Stream, (int)feature);
        return feature switch
        {
            QuickFeature.Crater => [GreatCrater(rng, aspect, flooded: false)],
            QuickFeature.FloodedCrater => [GreatCrater(rng, aspect, flooded: true)],
            QuickFeature.Scar => Scar(rng, aspect),
            QuickFeature.Rift => [MakeRift(rng, aspect)],
            QuickFeature.Spine => [MakeSpine(rng, aspect)],
            QuickFeature.Wall => [MakeWall(rng, aspect)],
            _ => [],
        };
    }

    /// <summary>
    /// The places a set-piece makes, for the generator to name (see MapGen.Landmarks): what kind of
    /// place each is, whether a point lies in it, and a line to letter its name along. Points are
    /// shares of the map's width and height, y down. Rebuilt from the feature and the seed it was
    /// drawn with, so it describes what <see cref="Draw"/> painted without reading the paint back.
    /// <list type="bullet">
    /// <item><c>basin</c>: the great crater, floor and rim; lettered in an arch across its floor.</item>
    /// <item><c>sea</c>: the drowned crater, its shores and island; the arch is over the water.</item>
    /// <item><c>scar</c>: every crater of the field; lettered alongside its main line.</item>
    /// <item><c>rift</c>: the valley and its escarpments; lettered down the dry floor.</item>
    /// <item><c>range</c>: the spine and its foothills; lettered along its inland flank.</item>
    /// <item><c>wall</c>: the wall and the ground at its feet; lettered along its southern foot.</item>
    /// </list>
    /// </summary>
    public static List<(string Kind, Func<double, double, bool> Contains, (double X, double Y)[] Label)> Landmarks(
        QuickFeature feature, int seed, double aspect)
    {
        var shapes = Shapes(feature, seed, aspect);
        var found = new List<(string, Func<double, double, bool>, (double, double)[])>();
        if (shapes.Length == 0) return found;

        // Everything below works in the shapes' own units, x stretched by the aspect.
        (double, double) Out(double x, double y) => (x / aspect, y);

        if (feature is QuickFeature.Crater or QuickFeature.FloodedCrater && shapes[0] is Crater great)
        {
            // An arch over the upper floor, left to right: across a dry floor, or over the water
            // clear of the island.
            double arch = great.Radius * (great.Flooded ? 0.56 : 0.6) * (1 + great.Squash) / 2;
            var label = Enumerable.Range(0, 33)
                .Select(i => Lerp(Math.PI * 1.14, Math.PI * 1.86, i / 32.0))
                .Select(a => Out(great.X + arch * Math.Cos(a), great.Y + arch * Math.Sin(a)))
                .ToArray();
            found.Add((great.Flooded ? "sea" : "basin",
                (x, y) => Frame(great, x * aspect, y) is { R: < 1.15 }, label));
        }
        else if (feature == QuickFeature.Scar)
        {
            var craters = shapes.OfType<Crater>().ToArray();
            var chain = craters.Where(c => !c.Secondary).ToArray();

            // Beside the chain rather than over it, on the side toward the bottom of the map, clear
            // of the largest rim.
            double off = chain.Max(c => c.Radius) * 1.45;
            var (first, last) = (chain[0], chain[^1]);
            double dx = last.X - first.X, dy = last.Y - first.Y, len = Math.Max(1e-9, Math.Sqrt(dx * dx + dy * dy));
            double nx = -dy / len, ny = dx / len;
            if (ny < 0) (nx, ny) = (-nx, -ny);
            var label = chain.Select(c => Out(c.X + nx * off, c.Y + ny * off)).ToArray();
            found.Add(("scar", (x, y) => craters.Any(c => Frame(c, x * aspect, y) is { R: < 1.1 }), label));
        }
        else if (shapes[0] is Line line)
        {
            // How far out from the line its land runs, in half-widths: the rift's floor and walls,
            // the spine's foothills, and only the ground at the wall's foot, since the wall itself is
            // impassable and a band as wide as the spine's took in a fifth of the map's baronies.
            // The lettering stands just off the high ground, on the side away from the sea.
            var (kind, reach, from, to, offset) = line switch
            {
                Rift r => ("rift", 1.9, Math.Max(r.SeaEnd, 0.1), 0.9, 0.0),
                Spine s => ("range", 2.6, 0.1, 0.9, -3.2 * s.CoastSide),
                _ => ("wall", 1.35, 0.1, 0.9, 1.9),
            };
            var label = line.Path.Offset(offset * line.Width, from, to).Select(p => Out(p.X, p.Y)).ToArray();
            found.Add((kind, (x, y) =>
            {
                var (distance, t, _) = line.Path.Closest(x * aspect, y);
                return distance < reach * line.Width && t > 0.03 && t < 0.97;
            }, label));
        }
        return found;
    }

    // ================================================================ placement

    /// <summary>
    /// The one great crater: close to half the map's height across its rim, kept clear of the
    /// poles and the map's edges, where the Continents stage pulls everything toward the sea.
    /// </summary>
    private static Crater GreatCrater(Rng rng, double aspect, bool flooded)
    {
        double radius = Lerp(0.21, 0.26, rng.NextDouble());
        double x = Lerp(0.32, 0.68, rng.NextDouble()) * aspect;
        double y = Lerp(0.40, 0.60, rng.NextDouble());

        // A flooded crater's breach is the strait to the open sea, so it faces the nearer edge
        // of the map, where the ocean is; a dry one's can go anywhere.
        double breach = flooded ? TowardNearestEdge(x, y, aspect) + Lerp(-0.5, 0.5, rng.NextDouble())
                                : rng.NextDouble() * Math.Tau;
        var breaches = !flooded && rng.NextDouble() < 0.35
            ? new[] { breach, breach + Math.PI + Lerp(-0.9, 0.9, rng.NextDouble()) }
            : new[] { breach };

        return new Crater(x, y, radius, rng.NextDouble() * Math.PI, Lerp(0.9, 1.0, rng.NextDouble()),
            breaches, Peak: true, flooded, Wobble(rng));
    }

    /// <summary>
    /// Cratered ground, as on the Moon or Mars: a chain of craters along one line, as if one body
    /// broke up on the way down, and a spatter of small secondary craters thrown out around it.
    /// Sizes follow the power law real crater fields do, many small for every large one, and
    /// neighbours overlap freely, each younger crater cutting the older rim it lands on.
    /// </summary>
    private static Crater[] Scar(Rng rng, double aspect)
    {
        int count = rng.Int(5, 8);

        // Mostly east-west, which is the long way across a 2:1 map, so the chain has room.
        double heading = Lerp(-0.6, 0.6, rng.NextDouble()) + (rng.NextDouble() < 0.5 ? 0 : Math.PI);
        double bend = Lerp(-0.3, 0.3, rng.NextDouble());

        // The leading fragment was the largest; the rest are drawn from the power law.
        var radii = new double[count];
        var gaps = new double[count];
        double length = 0;
        for (int i = 0; i < count; i++)
        {
            radii[i] = i == 0 ? Lerp(0.07, 0.09, rng.NextDouble()) : PowerLaw(rng, 0.025, 0.065);
            if (i > 0)
            {
                gaps[i] = (radii[i - 1] + radii[i]) * Lerp(0.75, 1.25, rng.NextDouble());
                length += gaps[i];
            }
        }

        // Centre the chain on a point near the middle of the map, then walk it out from there.
        double midX = Lerp(0.40, 0.60, rng.NextDouble()) * aspect;
        double midY = Lerp(0.40, 0.60, rng.NextDouble());
        double x = midX - Math.Cos(heading) * length / 2;
        double y = midY - Math.Sin(heading) * length / 2;
        double direction = heading - bend / 2;

        var craters = new List<Crater>();
        var path = new List<(double X, double Y, double Direction)>();
        for (int i = 0; i < count; i++)
        {
            if (i > 0)
            {
                direction += bend / Math.Max(1, count - 1);
                x += Math.Cos(direction) * gaps[i];
                y += Math.Sin(direction) * gaps[i];
            }
            path.Add((x, y, direction));

            double sideways = Lerp(-0.35, 0.35, rng.NextDouble()) * radii[i];
            double cx = x - Math.Sin(direction) * sideways;
            double cy = y + Math.Cos(direction) * sideways;

            // Stretched along the line of flight, as a low-angle impact is.
            craters.Add(new Crater(cx, cy, radii[i], direction, Lerp(0.82, 0.97, rng.NextDouble()),
                rng.NextDouble() < 0.4 ? [rng.NextDouble() * Math.Tau] : [],
                Peak: radii[i] >= ComplexRadius, Flooded: false, Wobble(rng, radii[i])));
        }

        // The secondaries: small, simple bowls scattered in a band along the chain, thickest near
        // it and thinning outward. Drawn after the chain, so they are the younger craters and cut
        // the rims and floors they land on, as they do on the Moon.
        int secondaries = rng.Int(18, 28);
        for (int i = 0; i < secondaries; i++)
        {
            var (px, py, pd) = path[rng.Int(0, path.Count - 1)];
            double along = Lerp(-0.12, 0.12, rng.NextDouble());
            double across = (rng.NextDouble() - 0.5) * 2;
            across = Math.Sign(across) * Math.Pow(Math.Abs(across), 0.7) * 0.24;
            double r = PowerLaw(rng, 0.01, 0.028);
            craters.Add(new Crater(
                px + Math.Cos(pd) * along - Math.Sin(pd) * across,
                py + Math.Sin(pd) * along + Math.Cos(pd) * across,
                r, rng.NextDouble() * Math.PI, Lerp(0.88, 1.0, rng.NextDouble()),
                [], Peak: false, Flooded: false, Wobble(rng, r), Secondary: true));
        }
        return craters.ToArray();
    }

    /// <summary>
    /// Above this radius a crater is complex: a flat floor and a central peak, like the great
    /// crater. Below it, a simple bowl, as small craters on the Moon are.
    /// </summary>
    private const double ComplexRadius = 0.05;

    /// <summary>A radius drawn from a crater field's power law: many small for every large one.</summary>
    private static double PowerLaw(Rng rng, double min, double max)
    {
        // Cumulative count above r falls as r^-2, near what the lunar maria show; inverted here.
        double u = rng.NextDouble();
        double a = 1 / (min * min), b = 1 / (max * max);
        return 1 / Math.Sqrt(a + (b - a) * u);
    }

    /// <summary>
    /// Low-order unevenness in the rim's radius, so it is never a drawn circle. Small craters are
    /// rounder, as they are in life: their rims have less room to slump.
    /// </summary>
    private static (double, double, double)[] Wobble(Rng rng, double radius = 0.2)
    {
        double scale = Math.Clamp(radius / 0.12, 0.4, 1.0);
        return
        [
            (2, rng.NextDouble() * Math.Tau, scale * Lerp(0.015, 0.035, rng.NextDouble())),
            (3, rng.NextDouble() * Math.Tau, scale * Lerp(0.02, 0.045, rng.NextDouble())),
            (5, rng.NextDouble() * Math.Tau, scale * Lerp(0.01, 0.025, rng.NextDouble())),
            (8, rng.NextDouble() * Math.Tau, scale * Lerp(0.005, 0.015, rng.NextDouble())),
        ];
    }

    /// <summary>The direction from a point to the closest edge of the map.</summary>
    private static double TowardNearestEdge(double x, double y, double aspect)
    {
        var edges = new (double Distance, double Angle)[]
        {
            (x, Math.PI), (aspect - x, 0), (y, -Math.PI / 2), (1 - y, Math.PI / 2),
        };
        return edges.MinBy(e => e.Distance).Angle;
    }

    // ================================================================ drawing

    /// <summary>What one crater asks of each layer at one point.</summary>
    /// <param name="Cover">How much of what older craters left here this one replaces.</param>
    private readonly record struct Sample(float Coast, float Delta, float Flatten, float Smooth, float Range, float Cover);

    private static void Render(HeightPipeline pipeline, Shape[] shapes, double aspect)
    {
        if (shapes.Length == 0) return;

        PaintLayer? coast = null, delta = null, smooth = null, flatten = null, flattenTarget = null, range = null;
        foreach (var stage in pipeline.Stages)
        {
            if (stage is not IPaintable paintable) continue;
            switch (stage)
            {
                case ContinentStage when coast is null:
                    coast = Channel(paintable, "coast")?.Layer;
                    break;
                case HeightPaintStage when delta is null:
                    delta = Channel(paintable, "delta")?.Layer;
                    smooth = Channel(paintable, "smooth")?.Layer;
                    var channel = Channel(paintable, "flatten");
                    flatten = channel?.Layer;
                    flattenTarget = channel?.Aux;
                    break;
                case RangePaintStage when range is null:
                    range = Channel(paintable, "range")?.Layer;
                    break;
            }
        }

        // Every layer is the same size (Channel makes them so), so the craters are evaluated once
        // per cell and each layer takes its own part.
        if (new[] { coast, delta, smooth, flatten, flattenTarget, range }.FirstOrDefault(l => l is not null) is not { } any)
            return;
        int w = any.Width, h = any.Height;
        var samples = new Sample[w * h];
        Parallel.For(0, h, y =>
        {
            double py = (y + 0.5) / h;
            for (int x = 0; x < w; x++)
                samples[y * w + x] = Combine(shapes, (x + 0.5) / w * aspect, py);
        });

        // The floor's level, as the flatten brush records it: a height h in 0..1 is kept as 2h - 1.
        float floor = (pipeline.SeaLevel + FloorAboveSea) * 2f - 1f;

        if (coast is not null) Fill(coast, samples, s => s.Coast);
        if (delta is not null) Fill(delta, samples, s => s.Delta);
        if (smooth is not null) Fill(smooth, samples, s => s.Smooth);
        if (flatten is not null) Fill(flatten, samples, s => s.Flatten);
        if (flattenTarget is not null) Fill(flattenTarget, samples, s => s.Flatten > 0 ? floor : 0f);
        if (range is not null) Fill(range, samples, s => s.Range);
    }

    private static PaintChannel? Channel(IPaintable stage, string key)
    {
        // A stage fresh from a preset has no layers until asked; the Terrain workspace resamples
        // them to its own authoring size when it paints, so the size here only sets smoothness.
        stage.EnsureLayers(2048, 1024);
        return stage.Channels.FirstOrDefault(c => c.Key == key);
    }

    /// <summary>
    /// Replaces a layer's contents with one value per cell. Written tile by tile through
    /// <see cref="PaintLayer.SwapTile"/>, which is how the layer learns it has been painted on; a
    /// layer written behind its back reports itself empty and its stage skips it.
    /// </summary>
    private static void Fill(PaintLayer layer, Sample[] samples, Func<Sample, float> pick)
    {
        int w = layer.Width;
        for (int tile = 0; tile < layer.TilesX * layer.TilesY; tile++)
        {
            var r = layer.TileRect(tile);
            var contents = new float[r.Width * r.Height];
            for (int y = 0; y < r.Height; y++)
                for (int x = 0; x < r.Width; x++)
                    contents[y * r.Width + x] = Math.Clamp(pick(samples[(r.Y + y) * w + r.X + x]), -1f, 1f);
            layer.SwapTile(tile, contents);
        }
        layer.Bump();
    }

    /// <summary>
    /// Every crater's say at one point, merged the way overlapping craters merge. They come in
    /// order, each younger than the last, so inside a crater's rim its own bowl, floor and peak
    /// replace whatever older rims and aprons stood there; outside it, aprons add up and the
    /// tallest rim wins. Land bias takes the strongest opinion.
    /// </summary>
    private static Sample Combine(Shape[] shapes, double px, double py)
    {
        float coast = 0, delta = 0, smooth = 0, flatten = 0, range = 0;
        foreach (var shape in shapes)
        {
            var sample = shape switch
            {
                Crater crater => Evaluate(crater, px, py),
                Line line => Evaluate(line, px, py),
                _ => null,
            };
            if (sample is not { } s) continue;
            float keep = 1f - s.Cover;
            coast = Math.Abs(s.Coast) > Math.Abs(coast) ? s.Coast : coast;
            delta = delta * keep + s.Delta;
            smooth = Math.Max(smooth * keep, s.Smooth);
            flatten = Math.Max(flatten * keep, s.Flatten);
            range = Math.Max(range * keep, s.Range);
        }
        return new Sample(coast, delta, flatten, smooth, range, 0);
    }

    /// <summary>One crater's say at a point, or null when the point is beyond its apron.</summary>
    /// <summary>
    /// A point in the crater's own frame: its distance from the centre in radii (<c>Rho</c>), the
    /// same with the rim's unevenness taken off so the rim is exactly 1 (<c>R</c>), and its
    /// direction from the centre. Null beyond the apron, where the crater has no say.
    /// </summary>
    private static (double Rho, double R, double Angle)? Frame(Crater c, double px, double py)
    {
        double dx = px - c.X, dy = py - c.Y;

        // Nothing reaches past the apron, so most craters are done here, before any trigonometry.
        double reach = 3.2 * c.Radius;
        if (Math.Abs(dx) > reach || Math.Abs(dy) > reach) return null;

        // Out to the crater's own frame: along and across the tilt, the short axis stretched back
        // to round, then the rim's unevenness taken off the radius.
        double cos = Math.Cos(c.Tilt), sin = Math.Sin(c.Tilt);
        double u = (dx * cos + dy * sin) / c.Radius;
        double v = (-dx * sin + dy * cos) / (c.Radius * c.Squash);
        double rho = Math.Sqrt(u * u + v * v);
        if (rho > 3.2) return null;

        double angle = Math.Atan2(dy, dx);
        double wobble = 1;
        foreach (var (order, phase, amount) in c.Wobble) wobble += amount * Math.Sin(order * angle + phase);
        return (rho, rho / wobble, angle);
    }

    private static Sample? Evaluate(Crater c, double px, double py)
    {
        if (Frame(c, px, py) is not var (rho, r, angle)) return null;

        // How open the rim is here: 1 in the middle of a breach, 0 away from it.
        double open = 0;
        foreach (double breach in c.Breaches)
            open = Math.Max(open, Gauss(AngleBetween(angle, breach) / 0.16));

        // How much of what older craters left here this one replaces: all of it inside its rim.
        float cover = (float)(1 - SmoothStep(0.8, 1.0, r));

        // Smaller craters stand lower and are shallower: the range's height is one number for the
        // whole stage, so a small crater's rim is painted weaker instead, and its bowl with it.
        double size = Math.Clamp(0.3 + 0.7 * (c.Radius - 0.015) / 0.1, 0.3, 1.0);
        bool complex = c.Radius >= ComplexRadius;

        // ---- the rim and the central peak, as range strength
        // Steep on the inside, where the wall slumped into the bowl, and a long gentle fall on the
        // outside, where the ejecta piled up against it.
        double rim = Gauss((r - 1) / (r < 1 ? 0.11 : 0.21)) * (1 - 0.97 * open);
        double peak = c.Peak ? Gauss(r / (c.Flooded ? 0.14 : 0.13)) : 0;

        float range = (float)(Math.Max(rim, peak) * size);

        // ---- the bowl and the apron, as raise/lower
        double apron = 0.3 * size * Apron(r);
        float delta, flatten = 0, smooth = 0;
        if (c.Flooded)
        {
            // The inside is sea, which the relief brush cannot touch; only the apron is left to it.
            delta = (float)apron;
        }
        else if (complex)
        {
            // A wide flat floor, walled in. The smoothing band where floor meets wall keeps the
            // flatten brush's edge from standing as a terrace step once erosion has been over it.
            double bowl = -0.5 * (1 - SmoothStep(0.62, 0.92, r));
            delta = (float)(bowl + apron);
            flatten = (float)(0.95 * (1 - SmoothStep(0.5, 0.82, r)));
            smooth = (float)(0.8 * Gauss((r - 0.72) / 0.16));
        }
        else
        {
            // A simple bowl: a parabola down to the middle, with no floor to speak of.
            double bowl = r < 1 ? -0.3 * size * (1 - r * r) : 0;
            delta = (float)(bowl + apron);
        }

        // ---- land and sea
        double land = 0.3 * Gauss(r / 1.9);
        double coast;
        if (c.Flooded)
        {
            double sea = -0.85 * (1 - SmoothStep(0.62, 0.88, r));
            double shore = 0.25 * Gauss((r - 1.02) / 0.22) * (1 - open);
            // Big enough to hold a few counties: land out to about a sixth of the radius.
            double island = 1.0 * Gauss(r / 0.22);

            // The strait: a channel out through the breach, fading once it is clear of the apron.
            double strait = 0;
            foreach (double breach in c.Breaches)
            {
                double along = rho * Math.Cos(angle - breach);
                double across = rho * Math.Sin(angle - breach);
                if (along > 0)
                    strait = Math.Min(strait, -0.7 * Gauss(across / 0.1) * SmoothStep(0.55, 0.8, along)
                                                    * (1 - SmoothStep(2.2, 2.8, along)));
            }
            coast = land + shore + sea + island + strait;
            // The rim cannot stand in the strait.
            if (strait < -0.2) range *= (float)(1 + strait);
        }
        else
        {
            // A dry crater is dry: firm land under the bowl and the rim, so no stray arm of the
            // sea reaches the floor. Small ones take their chances with the coast, and a few end
            // up as round bays.
            coast = land + (complex ? 0.3 * (1 - SmoothStep(0.9, 1.35, r)) : 0);
        }

        return new Sample((float)coast, delta, flatten, smooth, range, cover);
    }

    /// <summary>
    /// The ejecta apron outside the rim: rising from nothing at the crest to its thickest just
    /// beyond it, then thinning with the cube of distance, as ejecta does, until it is gone.
    /// </summary>
    private static double Apron(double r)
    {
        if (r <= 1) return 0;
        return SmoothStep(1.0, 1.12, r) * Math.Pow(1.08 / Math.Max(r, 1.08), 3) * (1 - SmoothStep(2.0, 2.8, r));
    }

    private static double Gauss(double t) => Math.Exp(-t * t);

    private static double SmoothStep(double a, double b, double x)
    {
        double t = Math.Clamp((x - a) / (b - a), 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static double AngleBetween(double a, double b)
    {
        double d = Math.IEEERemainder(a - b, Math.Tau);
        return Math.Abs(d);
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}
