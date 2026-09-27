using Ck3MapGen.Config;
using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen;

/// <summary>
/// Draws the <see cref="PartitionSketch"/>es <see cref="Provinces.Build"/> publishes as it
/// partitions, for a screen that wants to show the provinces forming. One per run, and only when
/// someone is watching: <see cref="Start"/> returns null otherwise, and the partition skips every
/// line that would have fed it.
///
/// Read only, like everything published to <see cref="Showcase"/>: it looks at the seeds, the label
/// map and the partition's distances and changes none of them, and its colours come off an Rng of
/// its own, so a watched run partitions exactly as an unwatched one.
///
/// Colours belong to the seed, not to its index. The tidy-up passes compact the seed list, which
/// renumbers every province after the first one dissolved; coloured by index, the settled sketch
/// would repaint half the map at the moment it should only be tidying borders.
/// </summary>
internal sealed class PartitionSketcher
{
    /// <summary>The preview's width cap, as <see cref="AppGUI.PreviewRenderer"/> draws every view.</summary>
    private const int MaxWidth = 2048;

    private readonly int _width, _height, _step, _outW, _outH;
    private readonly Rng _rng;
    private readonly Dictionary<ProvinceSeed, int> _slot = new(ReferenceEqualityComparer.Instance);
    private readonly List<(byte R, byte G, byte B)> _colour = [];

    private PartitionSketcher(int width, int height, MapConfig cfg)
    {
        _width = width;
        _height = height;
        _step = Math.Max(1, (width + MaxWidth - 1) / MaxWidth);
        _outW = width / _step;
        _outH = height / _step;
        _rng = new Rng(cfg.Seed ^ 0x5CE7);
    }

    public static PartitionSketcher? Start(int width, int height, MapConfig cfg)
        => Showcase.Sketching ? new PartitionSketcher(width, height, cfg) : null;

    public PartitionSketch Seeded(List<ProvinceSeed> seeds)
    {
        var (x, y, kind) = Positions(seeds);
        return new PartitionSketch { Step = PartitionStep.Seeded, Width = _outW, Height = _outH, SeedX = x, SeedY = y, SeedKind = kind };
    }

    public PartitionSketch Draw(ProvinceMap map, PartitionStep step, ushort[]? arrival = null, int pass = 0, int passes = 0)
    {
        var (x, y, kind) = Positions(map.Seeds);

        // Settled is the first sketch that knows which provinces are impassable; they go to stone,
        // which is the one change a watcher can see the mountains pass make.
        bool stone = step == PartitionStep.Settled;
        var colourOf = new (byte R, byte G, byte B)[map.Count];
        for (int i = 0; i < map.Count; i++)
        {
            var seed = map.Seeds[i];
            var c = _colour[SlotOf(seed)];
            colourOf[i] = stone && seed.IsLand && seed.IsImpassable
                ? ((byte)(c.R * 0.2 + 118 * 0.8), (byte)(c.G * 0.2 + 112 * 0.8), (byte)(c.B * 0.2 + 104 * 0.8))
                : c;
        }

        var label = map.Label;
        var rgb = new byte[_outW * _outH * 3];
        Parallel.For(0, _outH, oy =>
        {
            int row = oy * _step * _width;
            int below = Math.Min(_height - 1, (oy + 1) * _step) * _width;
            for (int ox = 0; ox < _outW; ox++)
            {
                int sx = ox * _step;
                int here = label[row + sx];
                var c = here < 0 ? ((byte)38, (byte)62, (byte)96) : colourOf[here];

                // A darker line where the next sample belongs to another province, so the cells
                // read as cells at preview size.
                int right = ox + 1 < _outW ? label[row + sx + _step] : here;
                if (right != here || label[below + sx] != here)
                    c = ((byte)(c.Item1 * 0.62), (byte)(c.Item2 * 0.62), (byte)(c.Item3 * 0.62));

                int o = (oy * _outW + ox) * 3;
                rgb[o] = c.Item1; rgb[o + 1] = c.Item2; rgb[o + 2] = c.Item3;
            }
        });

        return new PartitionSketch
        {
            Step = step, Width = _outW, Height = _outH, SeedX = x, SeedY = y, SeedKind = kind,
            Rgb = rgb, Arrival = arrival, Pass = pass, Passes = passes,
        };
    }

    /// <summary>
    /// When the growth reached each preview pixel, read off the partition's own distances: the
    /// wavefront is one global Dijkstra, every seed starting at nought, so replaying the pixels in
    /// distance order is replaying the growth exactly as it happened. Called with the partition's
    /// state before it is thrown away, while every distance in it is final.
    ///
    /// Runs inside the partition rather than inside <see cref="Showcase"/>'s guard, so it guards
    /// itself: a sketch that could not be made costs the watcher the growth, never the run.
    /// </summary>
    public ushort[]? Arrival(ulong[] state, List<ProvinceSeed> seeds)
    {
        try
        {
            return ReadArrival(state, seeds);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  (partition sketch: {ex.GetType().Name}: {ex.Message})");
            return null;
        }
    }

    private ushort[] ReadArrival(ulong[] state, List<ProvinceSeed> seeds)
    {
        int n = _outW * _outH;
        var distance = new float[n];
        var land = new bool[n];
        float maxLand = 0, maxAll = 0;

        for (int oy = 0; oy < _outH; oy++)
        {
            int row = oy * _step * _width;
            for (int ox = 0; ox < _outW; ox++)
            {
                ulong s = state[row + ox * _step];
                uint label = (uint)s;
                float d = BitConverter.UInt32BitsToSingle((uint)(s >> 32));
                int i = oy * _outW + ox;

                if (label >= (uint)seeds.Count || !float.IsFinite(d))
                {
                    distance[i] = float.NaN;
                    continue;
                }

                distance[i] = d;
                land[i] = seeds[(int)label].IsLand;
                if (land[i]) maxLand = Math.Max(maxLand, d);
                maxAll = Math.Max(maxAll, d);
            }
        }

        // The land is where the eye is, so it gets most of the time: the pace is set so that 98%
        // of the land has grown four fifths of the way through, and the open sea that is still
        // going finishes in the last fifth.
        float full = maxLand > 0 ? Percentile(distance, land, maxLand, 0.98) : maxAll;
        if (full <= 0) full = Math.Max(1, maxAll);
        float tail = Math.Max(1e-3f, maxAll - full);

        var arrival = new ushort[n];
        for (int i = 0; i < n; i++)
        {
            float d = distance[i];
            double t = float.IsNaN(d) ? 1
                : d <= full ? 0.8 * d / full
                : 0.8 + 0.2 * Math.Min(1, (d - full) / tail);
            arrival[i] = (ushort)Math.Round(t * ushort.MaxValue);
        }
        return arrival;
    }

    private static float Percentile(float[] distance, bool[] land, float max, double share)
    {
        const int Bins = 4096;
        var histogram = new int[Bins];
        int count = 0;
        for (int i = 0; i < distance.Length; i++)
        {
            if (!land[i] || float.IsNaN(distance[i])) continue;
            histogram[Math.Min(Bins - 1, (int)(distance[i] / max * (Bins - 1)))]++;
            count++;
        }

        int want = (int)(count * share), seen = 0;
        for (int b = 0; b < Bins; b++)
        {
            seen += histogram[b];
            if (seen >= want) return (b + 1) * max / (Bins - 1);
        }
        return max;
    }

    private (float[] X, float[] Y, SketchSeed[] Kind) Positions(List<ProvinceSeed> seeds)
    {
        foreach (var seed in seeds) SlotOf(seed);

        int slots = _colour.Count;
        var x = new float[slots];
        var y = new float[slots];
        var kind = new SketchSeed[slots];
        Array.Fill(x, float.NaN);
        Array.Fill(y, float.NaN);

        foreach (var seed in seeds)
        {
            int slot = _slot[seed];
            x[slot] = (seed.X + 0.5f) / _step;
            y[slot] = (seed.Y + 0.5f) / _step;
            kind[slot] = seed.IsMajorRiver ? SketchSeed.River
                : !seed.IsLand ? SketchSeed.Sea
                : seed.IsImpassable ? SketchSeed.Impassable
                : SketchSeed.Land;
        }
        return (x, y, kind);
    }

    /// <summary>A seed's slot, given one — and a colour — the first time it is seen.</summary>
    private int SlotOf(ProvinceSeed seed)
    {
        if (_slot.TryGetValue(seed, out int slot)) return slot;

        // The ranges the Provinces view draws in, so the settled sketch looks like that view.
        (byte, byte, byte) colour = seed.IsLand
            ? ((byte)_rng.Int(60, 235), (byte)_rng.Int(90, 235), (byte)_rng.Int(55, 190))
            : seed.IsMajorRiver
                ? ((byte)_rng.Int(0, 40), (byte)_rng.Int(130, 200), (byte)_rng.Int(200, 255))
                : ((byte)_rng.Int(20, 70), (byte)_rng.Int(45, 105), (byte)_rng.Int(90, 170));

        slot = _colour.Count;
        _slot[seed] = slot;
        _colour.Add(colour);
        return slot;
    }
}
