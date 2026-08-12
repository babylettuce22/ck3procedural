namespace Ck3MapGen.Emit;

/// <summary>
/// Builds the packed_heightmap/indirection_heightmap pair CK3 renders terrain from.
///
/// The heightmap the game reads is not heightmap.png. It is a texture atlas of 32-pixel tiles at
/// five levels of detail, plus a lookup that says where each tile of the world lives in that atlas
/// and how far it was decimated. heightmap.png is the authoring format; this is the runtime one.
///
/// Every rule below was measured against vanilla's own three files on 2026-08-11 rather than
/// inferred from the wiki, by addressing vanilla's atlas with a candidate formula and scoring the
/// reconstruction against vanilla's heightmap.png. The figures in brackets are mean absolute error
/// on the 0-255 scale — a correct rule lands near zero, a wrong one scores like two unrelated
/// pieces of terrain.
///
/// **Rows count up from the bottom.** <c>py = atlasHeight - levelOffset - tileSize * (G + 1)</c>
/// [0.77] against the top-down reading [44.73]. Writing G top-down mirrors the atlas vertically so
/// every tile resolves to another tile's terrain, which is what the "strips of missing terrain"
/// from the previous packer actually were.
///
/// **level_offsets participate in the address.** Including them [0.009-0.014] against omitting
/// them [25-47]. They are not decoration and they are not per-level metadata: they are the
/// bottom-up distance from the foot of the atlas to the foot of each level's region.
///
/// **A tile's source window starts one row ABOVE its own grid line** — rows
/// <c>[ty*32 - 1, ty*32 + 32)</c>, columns <c>[tx*32, tx*32 + 33)</c>. Asymmetric, and not a
/// guess: it takes level 0 from 0.700 to 0.005. The previous packer read <c>[ty*32, ty*32 + 33)</c>
/// and so shipped every tile one row south of where CK3 looks for it.
///
/// **Tiles are decimated, not averaged.** Taking every 2^level-th sample [0.005-0.014] against
/// box-averaging the block [0.655-1.950], measured on the highest-relief tiles at each level where
/// the two diverge most. AzgaarToCK3 box-averages; that would have been a fresh bug to import.
///
/// The one thing here that deliberately departs from vanilla is region packing. Vanilla's own
/// regions overlap slightly — its level 0 needs 22 rows but only 1397 of the 1430 pixels they
/// occupy are reserved before level 1 begins, and level 1 overruns into level 2 the same way. It
/// evidently tolerates this because the colliding tiles are duplicates or open ocean, but there is
/// nothing to gain by reproducing it. Regions here are allocated exactly and never overlap.
/// </summary>
public static class HeightmapPacker
{
    /// <summary>Source pixels a tile spans. The tile stores one more sample than this, overlapping
    /// its neighbour so adjacent tiles share an edge and the terrain does not crack between them.</summary>
    public const int TileStep = 32;

    /// <summary>Levels of detail, from 0 (full resolution) to 4 (decimated 16x).</summary>
    public const int Levels = 5;

    /// <summary>
    /// Atlas coordinates live in the indirection's R and G *bytes*, so neither axis may exceed 256
    /// tiles. Silent when broken: the byte wraps and a slice of the map points at the wrong terrain.
    /// </summary>
    private const int MaxAddressable = 256;

    /// <summary>
    /// D3D11 caps a Texture2D at 16384 pixels a side, and the packed heightmap IS a texture. Exceed
    /// it and creation fails, CK3 gets a null back, and it crashes reading the texture description
    /// during heightmap setup — on a worker thread, with nothing in the log.
    /// </summary>
    private const int MaxTextureSide = 16384;

    /// <summary>
    /// Share of tiles at each level, measured off vanilla's indirection_heightmap.png: 1,063 /
    /// 4,948 / 6,100 / 4,838 / 24,523 of 41,472.
    ///
    /// Shares rather than absolute thresholds on the detail metric, so this self-calibrates. An
    /// all-mountain map and an archipelago have wildly different gradient statistics but the same
    /// texture budget to spend, and spending it in vanilla's proportions is what makes a generated
    /// map cost what the base game costs. Note how lopsided vanilla is: nearly 60% of the world is
    /// at the coarsest level, which is the whole reason its atlas is 12.9M pixels and a naive
    /// all-level-0 one is 175.7M.
    /// </summary>
    private static readonly double[] VanillaShare = [0.0256, 0.1193, 0.1471, 0.1167, 0.5913];

    /// <summary>Samples along a tile edge at each level: 33, 17, 9, 5, 3.</summary>
    public static int TileSize(int level) => TileStep / Decimation(level) + 1;

    /// <summary>How far a level decimates its source: 1, 2, 4, 8, 16. Written to the indirection's B.</summary>
    public static int Decimation(int level) => 1 << level;

    /// <summary>
    /// The three files' worth of data, ready to write. <see cref="LevelOffsets"/> and
    /// <see cref="EmptyR"/>/<see cref="EmptyG"/> belong in heightmap.heightmap; without them the
    /// atlas cannot be addressed.
    /// </summary>
    public sealed record Result(
        ushort[] Packed, int PackedWidth, int PackedHeight,
        byte[] Indirection, int TilesX, int TilesY,
        int[] LevelOffsets, int EmptyR, int EmptyG,
        int[] TilesPerLevel, int[] SlotsPerLevel);

    public static Result Pack(ushort[] full, int width, int height)
    {
        int tilesX = width / TileStep, tilesY = height / TileStep;
        int tileCount = tilesX * tilesY;

        var level = AssignLevels(Detail(full, width, height, tilesX, tilesY));

        // ── Deduplicate. Identical tiles share one slot in the atlas ──────────────
        //
        // Vanilla leans on this hard: 14,314 of its 41,472 tiles — 34.5% of the world — point at
        // one single open-ocean tile, the one empty_tile_offset names. Content-hashing every level
        // rather than special-casing ocean subsumes that and costs nothing, since the comparison is
        // over at most 33x33 samples and only ever between tiles at the same level.
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

        // empty_tile_offset has to name a tile that is inert if CK3 ever substitutes it. Vanilla's
        // points at the open ocean every flat tile already shares. Prefer that — the most-referenced
        // uniform tile at or below the water plane — and only mint a dedicated one if this map has
        // no flat water anywhere, so the common case costs no extra slot.
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

    /// <summary>Where a slot's top-left corner sits in the atlas. The one formula everything turns on.</summary>
    private static (int px, int py) SlotPosition(int slot, int level, int cols, int offset, int atlasHeight)
    {
        int s = TileSize(level);
        return (slot % cols * s, atlasHeight - offset - s * (slot / cols + 1));
    }

    /// <summary>
    /// Mean gradient magnitude over a tile — how much relief it carries, and so how much resolution
    /// it is worth spending on.
    ///
    /// Magnitude of the first derivative, not the signed sum of the second. AzgaarToCK3 records
    /// hitting exactly that wall with the upstream metric: a signed sum cancels, so it collapses
    /// towards zero on flat ocean AND on rough interiors alike and cannot tell them apart. A
    /// Euclidean magnitude is single-sided, so it rises monotonically from still water to mountain
    /// face, which is the ordering the bucketing needs.
    ///
    /// Deliberately unnormalised: only the ranking is ever used.
    /// </summary>
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

    /// <summary>
    /// Ranks tiles by relief and spends vanilla's budget on them, steepest first.
    ///
    /// Dead-flat tiles drop to the coarsest level whatever the budget says. A tile with no gradient
    /// at all is open ocean or a plateau interior, and storing it at 33x33 buys literally nothing —
    /// it is the same value 1,089 times. Letting one consume a level-0 slot would displace a
    /// mountain face that needed it.
    /// </summary>
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

    /// <summary>
    /// A tile's samples, decimated from the source.
    ///
    /// The -1 on the row origin is the measured alignment, not an off-by-one: CK3 expects the tile
    /// to lead with the row above its own grid line. Columns have no such shift. Both axes clamp at
    /// the map edge, which duplicates one row along the top — the only place the window falls
    /// outside the raster.
    /// </summary>
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

    /// <summary>
    /// Picks an atlas shape: how many columns each level gets, how many rows that needs, and where
    /// each level's region starts.
    ///
    /// Searched rather than derived, because the total area is nearly fixed — it is the tiles
    /// themselves plus a partial last row per level — so the only real choice is the aspect ratio.
    /// The search takes the squarest atlas that keeps both axes inside the texture limit and every
    /// level inside the byte the indirection addresses it with. Squarest rather than widest because
    /// both limits are per-axis, so a square shape is the furthest from either.
    /// </summary>
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

            // Prefer smaller max dimension; tie-break on aspect ratio closest to square
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

    /// <summary>Atlas height for a candidate width, and whether that candidate is legal at all.</summary>
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

    /// <summary>
    /// The most-referenced flat tile at or below the water plane, or -1 if this map has none.
    /// Uniform because a tile that varies is terrain somewhere, and substituting it for a missing
    /// one would stamp that terrain across the gap.
    /// </summary>
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

    /// <summary>Content equality over a tile's samples, so the dedup can key a dictionary on them.</summary>
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