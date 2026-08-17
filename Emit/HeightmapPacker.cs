namespace Ck3MapGen.Emit;

/// <summary>
/// Builds the packed_heightmap/indirection_heightmap pair CK3 renders terrain from.
/// </summary>
public static class HeightmapPacker
{
    public const int TileStep = 64;
    public const int Levels = 5;
    private const int MaxAddressable = 256;
    private const int MaxTextureSide = 16384;

    private static readonly double[] VanillaShare = [0.0256, 0.1193, 0.1471, 0.1167, 0.5913];

    public static int TileSize(int level) => TileStep / Decimation(level) + 1;
    public static int Decimation(int level) => 1 << level;

    public sealed record Result(
        ushort[] Packed, int PackedWidth, int PackedHeight,
        byte[] Indirection, int TilesX, int TilesY,
        int[] LevelOffsets, int EmptyR, int EmptyG,
        int[] TilesPerLevel, int[] SlotsPerLevel);

    public static Result Pack(ushort[] full, int width, int height)
    {
        int tilesX = width / TileStep, tilesY = height / TileStep;
        int tileCount = tilesX * tilesY;

        // 1. Initial level assignment by gradient relief metric
        var level = AssignLevels(Detail(full, width, height, tilesX, tilesY));

        // 2. Enforce 2:1 LOD Neighbor Balance (Seam & T-junction protection)
        EnforceNeighborLodBalance(level, tilesX, tilesY);

        // ── Deduplicate. Identical tiles share one slot in the atlas ──────────────
        var slotOf = new int[tileCount];
        var slots = new List<ushort[]>[Levels];
        var tilesPerLevel = new int[Levels];

        for (int l = 0; l < Levels; l++)
        {
            var seen = new Dictionary<ushort[], int>(SampleComparer.Instance);
            slots[l] = [];

            for (int ty = 0; ty < tilesY; ty++)
                for (int tx = 0; tx < tilesX; tx++)
                {
                    int t = ty * tilesX + tx;
                    if (level[t] != l) continue;
                    tilesPerLevel[l]++;

                    var samples = Extract(full, width, height, tx, ty, l);
                    if (!seen.TryGetValue(samples, out int slot))
                    {
                        slot = slots[l].Count;
                        seen[samples] = slot;
                        slots[l].Add(samples);
                    }
                    slotOf[t] = slot;
                }
        }

        int emptySlot = FindFlatWaterSlot(slots[Levels - 1], slotOf, level, tileCount);
        if (emptySlot < 0)
        {
            emptySlot = slots[Levels - 1].Count;
            slots[Levels - 1].Add(new ushort[TileSize(Levels - 1) * TileSize(Levels - 1)]);
        }

        var slotsPerLevel = new int[Levels];
        for (int l = 0; l < Levels; l++) slotsPerLevel[l] = slots[l].Count;

        // ── Lay the atlas out ─────────────────────────────────────────────────────
        var (atlasWidth, atlasHeight, cols, rows, offsets) = Layout(slotsPerLevel);

        var packed = new ushort[(long)atlasWidth * atlasHeight];
        for (int l = 0; l < Levels; l++)
        {
            int s = TileSize(l);
            for (int slot = 0; slot < slots[l].Count; slot++)
            {
                var (px, py) = SlotPosition(slot, l, cols[l], offsets[l], atlasHeight);
                var samples = slots[l][slot];

                for (int y = 0; y < s; y++)
                    for (int x = 0; x < s; x++)
                        packed[(long)(py + y) * atlasWidth + px + x] = samples[y * s + x];
            }
        }

        // ── Indirection: R = atlas column, G = atlas row, B = decimation, A = level ─
        var indirection = new byte[tileCount * 4];
        for (int t = 0; t < tileCount; t++)
        {
            int l = level[t];
            int i = t * 4;
            indirection[i] = (byte)(slotOf[t] % cols[l]);
            indirection[i + 1] = (byte)(slotOf[t] / cols[l]);
            indirection[i + 2] = (byte)Decimation(l);
            indirection[i + 3] = (byte)l;
        }

        int emptyCols = cols[Levels - 1];
        return new Result(
            packed, atlasWidth, atlasHeight,
            indirection, tilesX, tilesY,
            offsets, emptySlot % emptyCols, emptySlot / emptyCols,
            tilesPerLevel, slotsPerLevel);
    }

    /// <summary>
    /// The heightmap as CK3 will actually render it: every tile decimated to the level this packer
    /// would assign it, then filtered back up the way the GPU samples it.
    ///
    /// This is the geometry the game draws. <see cref="Pack"/> throws detail away — a tile that
    /// lands on level 4 keeps one sample in sixteen — and which tiles lose what is decided by the
    /// relief metric and the vanilla level shares, not by anything the author controls. That is
    /// where a gentle slope turns into a staircase and a lone ridge quietly disappears, and it is
    /// invisible in the source PNG. Running the same assignment and reversing the sampling is the
    /// only way to see it without launching the game.
    ///
    /// Deliberately shares <see cref="Detail"/>, <see cref="AssignLevels"/>,
    /// <see cref="EnforceNeighborLodBalance"/> and <see cref="Extract"/> with <see cref="Pack"/>
    /// rather than reading the atlas back through the indirection texture. Going through the atlas
    /// would be a second implementation of the same decision, free to drift from the one that ships.
    /// </summary>
    public static ushort[] Reconstruct(ushort[] full, int width, int height)
    {
        int tilesX = width / TileStep, tilesY = height / TileStep;
        if (tilesX == 0 || tilesY == 0) return full;

        var level = AssignLevels(Detail(full, width, height, tilesX, tilesY));
        EnforceNeighborLodBalance(level, tilesX, tilesY);

        // Copied rather than allocated blank: a map whose height is not a whole number of tiles has
        // a strip at the bottom that no tile covers, and it should read as itself, not as sea.
        var result = new ushort[full.Length];
        Array.Copy(full, result, full.Length);

        Parallel.For(0, tilesY, ty =>
        {
            for (int tx = 0; tx < tilesX; tx++)
            {
                int l = level[ty * tilesX + tx];
                int step = Decimation(l), s = TileSize(l);
                var samples = Extract(full, width, height, tx, ty, l);

                for (int y = 0; y < TileStep; y++)
                {
                    int gy = ty * TileStep + y;
                    if (gy >= height) break;

                    // Extract starts its rows one pixel above the tile, so the sample grid is
                    // offset by one; undo that here rather than resampling on a different origin.
                    double v = (y + 1.0) / step;
                    int v0 = Math.Clamp((int)v, 0, s - 2);
                    double fv = v - v0;

                    long row = (long)gy * width;

                    for (int x = 0; x < TileStep; x++)
                    {
                        int gx = tx * TileStep + x;
                        if (gx >= width) break;

                        double u = (double)x / step;
                        int u0 = Math.Clamp((int)u, 0, s - 2);
                        double fu = u - u0;

                        int a = v0 * s + u0, b = a + s;
                        double top = samples[a] + (samples[a + 1] - samples[a]) * fu;
                        double bottom = samples[b] + (samples[b + 1] - samples[b]) * fu;

                        result[row + gx] = (ushort)Math.Clamp(
                            Math.Round(top + (bottom - top) * fv), 0, 65535);
                    }
                }
            }
        });

        return result;
    }

    /// <summary>
    /// Enforces that no two adjacent tiles differ by more than 1 level of detail (|L1 - L2| <= 1).
    /// Prevents T-junction mesh cracks and mip-level boundary tears when viewing the map in 3D.
    /// </summary>
    private static void EnforceNeighborLodBalance(int[] level, int tilesX, int tilesY)
    {
        bool changed = true;
        int maxIterations = Levels;

        while (changed && maxIterations-- > 0)
        {
            changed = false;
            for (int ty = 0; ty < tilesY; ty++)
            {
                for (int tx = 0; tx < tilesX; tx++)
                {
                    int t = ty * tilesX + tx;
                    int curLevel = level[t];

                    // Check 4-connected neighbors
                    for (int k = 0; k < 4; k++)
                    {
                        int nx = tx + Dx4[k], ny = ty + Dy4[k];
                        if (nx < 0 || nx >= tilesX || ny < 0 || ny >= tilesY) continue;

                        int nt = ny * tilesX + nx;
                        // If neighbor is more than 1 step coarser, pull neighbor to curLevel + 1
                        if (level[nt] > curLevel + 1)
                        {
                            level[nt] = curLevel + 1;
                            changed = true;
                        }
                    }
                }
            }
        }
    }

    private static readonly int[] Dx4 = [1, -1, 0, 0];
    private static readonly int[] Dy4 = [0, 0, 1, -1];

    private static (int px, int py) SlotPosition(int slot, int level, int cols, int offset, int atlasHeight)
    {
        int s = TileSize(level);
        return (slot % cols * s, atlasHeight - offset - s * (slot / cols + 1));
    }

    private static float[] Detail(ushort[] full, int width, int height, int tilesX, int tilesY)
    {
        var metric = new float[tilesX * tilesY];

        Parallel.For(0, tilesY, ty =>
        {
            for (int tx = 0; tx < tilesX; tx++)
            {
                double sum = 0;

                for (int y = ty * TileStep; y < ty * TileStep + TileStep; y++)
                {
                    int y0 = Math.Min(y, height - 1);
                    int y1 = Math.Min(y + 1, height - 1);

                    for (int x = tx * TileStep; x < tx * TileStep + TileStep; x++)
                    {
                        int x0 = Math.Min(x, width - 1);
                        int x1 = Math.Min(x + 1, width - 1);

                        double here = full[(long)y0 * width + x0];
                        double gx = full[(long)y0 * width + x1] - here;
                        double gy = full[(long)y1 * width + x0] - here;
                        sum += Math.Sqrt(gx * gx + gy * gy);
                    }
                }

                metric[ty * tilesX + tx] = (float)(sum / (TileStep * TileStep));
            }
        });

        return metric;
    }

    private static int[] AssignLevels(float[] metric)
    {
        int count = metric.Length;

        var order = new int[count];
        for (int i = 0; i < count; i++) order[i] = i;
        var keys = (float[])metric.Clone();
        Array.Sort(keys, order);
        Array.Reverse(order);

        var level = new int[count];
        int at = 0;

        for (int l = 0; l < Levels - 1 && at < count; l++)
        {
            int take = Math.Clamp((int)Math.Round(count * VanillaShare[l]), 0, count - at);
            for (int k = 0; k < take; k++) level[order[at + k]] = l;
            at += take;
        }
        for (; at < count; at++) level[order[at]] = Levels - 1;

        for (int i = 0; i < count; i++)
            if (metric[i] <= 0f) level[i] = Levels - 1;

        return level;
    }

    private static ushort[] Extract(ushort[] full, int width, int height, int tx, int ty, int level)
    {
        int step = Decimation(level), s = TileSize(level);
        var samples = new ushort[s * s];

        for (int y = 0; y < s; y++)
        {
            int sy = Math.Clamp(ty * TileStep - 1 + y * step, 0, height - 1);

            for (int x = 0; x < s; x++)
            {
                int sx = Math.Clamp(tx * TileStep + x * step, 0, width - 1);
                samples[y * s + x] = full[(long)sy * width + sx];
            }
        }

        return samples;
    }

    private static (int width, int height, int[] cols, int[] rows, int[] offsets) Layout(int[] slotsPerLevel)
    {
        int best = -1;
        long bestMaxDim = long.MaxValue;
        long bestDiff = long.MaxValue;
        int maxCols = Math.Min(MaxAddressable, MaxTextureSide / TileSize(0));

        for (int c = 1; c <= maxCols; c++)
        {
            int candidateWidth = c * TileSize(0);
            var (h, ok) = Measure(candidateWidth, slotsPerLevel);
            if (!ok) continue;

            long maxDim = Math.Max((long)candidateWidth, h);
            long diff = Math.Abs((long)candidateWidth - h);

            if (maxDim < bestMaxDim || (maxDim == bestMaxDim && diff < bestDiff))
            {
                bestMaxDim = maxDim;
                bestDiff = diff;
                best = c;
            }
        }

        if (best < 0) best = maxCols;

        int width = best * TileSize(0);
        var cols = new int[Levels];
        var rows = new int[Levels];
        var offsets = new int[Levels];
        int height = 0;

        for (int l = 0; l < Levels; l++)
        {
            cols[l] = Math.Max(1, Math.Min(width / TileSize(l), MaxAddressable));
            rows[l] = (slotsPerLevel[l] + cols[l] - 1) / cols[l];
            offsets[l] = height;
            height += rows[l] * TileSize(l);
        }

        return (width, height, cols, rows, offsets);
    }

    private static (long height, bool ok) Measure(int width, int[] slotsPerLevel)
    {
        long height = 0;

        for (int l = 0; l < Levels; l++)
        {
            int cols = Math.Min(width / TileSize(l), MaxAddressable);
            if (cols <= 0) return (0, false);

            int rows = (slotsPerLevel[l] + cols - 1) / cols;
            if (rows > MaxAddressable) return (0, false);
            height += (long)rows * TileSize(l);
        }

        return (height, height <= MaxTextureSide && width <= MaxTextureSide);
    }

    private static int FindFlatWaterSlot(List<ushort[]> slots, int[] slotOf, int[] level, int tileCount)
    {
        var references = new int[slots.Count];
        for (int t = 0; t < tileCount; t++)
            if (level[t] == Levels - 1) references[slotOf[t]]++;

        int best = -1, bestReferences = -1;

        for (int slot = 0; slot < slots.Count; slot++)
        {
            var samples = slots[slot];
            if (samples[0] > MapDataWriter.WaterLevel16) continue;

            bool uniform = true;
            for (int i = 1; i < samples.Length && uniform; i++)
                uniform = samples[i] == samples[0];

            if (!uniform || references[slot] <= bestReferences) continue;
            bestReferences = references[slot];
            best = slot;
        }

        return best;
    }

    private sealed class SampleComparer : IEqualityComparer<ushort[]>
    {
        public static readonly SampleComparer Instance = new();

        public bool Equals(ushort[]? a, ushort[]? b)
            => ReferenceEquals(a, b) || (a is not null && b is not null && a.AsSpan().SequenceEqual(b));

        public int GetHashCode(ushort[] samples)
        {
            var hash = new HashCode();
            hash.AddBytes(System.Runtime.InteropServices.MemoryMarshal.AsBytes(samples.AsSpan()));
            return hash.ToHashCode();
        }
    }
}