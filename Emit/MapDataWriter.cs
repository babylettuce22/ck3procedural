using Ck3MapGen.Config;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;
using Ck3MapGen.World;

namespace Ck3MapGen.Emit;

/// <summary>
/// Writes the map_data/ half of the mod. Every format here was verified byte-for-byte against
/// vanilla 1.19 rather than taken from documentation, because CK3 fails opaquely on all of it.
/// </summary>
public static class MapDataWriter
{
    public const int WaterLevel255 = 19;
    public const int Step255 = 257;
    public const int WaterLevel16 = WaterLevel255 * Step255;

    /// <summary>
    /// Where CK3 actually draws the water surface: <c>_WaterHeight</c> = 3.0 world units, read out
    /// of the water vertex shader's constant buffer under RenderDoc on vanilla 1.19 (2026-08-23)
    /// and used verbatim as that shader's Y, against <c>WORLD_EXTENTS_Y = 50</c>. So 3/50 of full
    /// scale, and equal to <c>NJominiMap.WATERLEVEL</c> — the define appears to feed it directly.
    ///
    /// This sits <b>below</b> <see cref="WaterLevel16"/>, and the two are not interchangeable:
    /// 4883 is where land begins in the file, 3932 is where the sea is drawn, and the 951 units
    /// between them are the beach. Vanilla's own comment on the define ("0.06 in 0-1, 19 in 0-255")
    /// contradicts itself; the 0.06 half is the true one.
    /// </summary>
    public const int WaterPlane16 = 3932;

    private static readonly (byte R, byte G, byte B)[] RiverPaletteHead =
    [
        (0, 255, 0),     // 0  source
        (255, 0, 0),     // 1  join / tributary merge
        (255, 252, 0),   // 2  split
        (0, 225, 255),   // 3  narrowest
        (0, 200, 255),   // 4
        (0, 150, 255),   // 5
        (0, 100, 255),   // 6
        (0, 0, 255),     // 7
        (0, 0, 225),     // 8
        (0, 0, 200),     // 9
        (0, 0, 150),     // 10
        (0, 0, 100),     // 11 widest
        (0, 85, 0),      // 12
        (0, 125, 0),     // 13
        (0, 158, 0),     // 14
        (24, 206, 0),    // 15
    ];

    public const byte RiverIndexLand = 255;
    public const byte RiverIndexWater = 254;

    /// <returns>The heightmap this run shipped — see <see cref="WriteHeightmap"/>. The scatter
    /// passes in <see cref="ContentWriter"/> need it, and recomputing it there would repeat the
    /// coastline work and its console report for an array we already hold.</returns>
    public static ushort[] WriteAll(string modDir, MapConfig cfg, ProvinceMap provinces,
            int[] order, int baronyCount, int landCount, int riverCount, bool writePacked,
            MapGen.TerrainData terra, MapGen.Drainage? drainage = null)
    {
        string dir = Path.Combine(modDir, "map_data");
        Directory.CreateDirectory(dir);

        Core.Stage.Detail("  · provinces.png",
            () => WriteProvincesPng(Path.Combine(dir, "provinces.png"), provinces, order));
        Core.Stage.Detail("  · definition.csv",
            () => WriteDefinitionCsv(Path.Combine(dir, "definition.csv"), provinces, order));

        Core.Stage.Detail("  · rivers.png", () =>
        {
            if (drainage != null)
                WriteRiversPng(Path.Combine(dir, "rivers.png"), cfg, provinces, drainage);
            else
                WriteRiversPng(Path.Combine(dir, "rivers.png"), cfg, provinces, null!);
        });

        var shipped = Core.Stage.Detail("  · heightmap",
            () => WriteHeightmap(dir, cfg, writePacked, provinces, order, landCount, terra));
        WriteDefaultMap(Path.Combine(dir, "default.map"), provinces.Count, baronyCount, landCount, riverCount);
        WriteStubs(dir);

        AssertMapDataComplete(dir, writePacked);

        Console.WriteLine($"  map_data written: {baronyCount} baronied + " +
                          $"{landCount - baronyCount} impassable land, " +
                          $"{riverCount - landCount} major river provinces, " +
                          $"{provinces.Count - riverCount} sea zones");

        return shipped;
    }

    public static ushort[] WriteAll(string modDir, MapConfig cfg, ProvinceMap provinces,
    int[] order, int baronyCount, int landCount, bool writePacked, MapGen.TerrainData terra)
    {
        int riverCount = landCount;
        for (int i = 0; i < provinces.Count; i++)
            if (!provinces.Seeds[i].IsLand && provinces.Seeds[i].IsMajorRiver) riverCount++;

        return WriteAll(modDir, cfg, provinces, order, baronyCount, landCount, riverCount, writePacked, terra);
    }

    /// <summary>
    /// The files <c>default.map</c> declares and this writer produces. They have to be on disk
    /// when map_data is finished: CK3 gives no error for a missing one, the load just stops.
    ///
    /// continent.txt is deliberately not among them, and deliberately not written. Every CK3 map
    /// declares it and none ships it — not vanilla 1.19, and not any of the three total conversions
    /// checked, AGOT included, which replaces the world map outright. It is a legacy declaration
    /// the engine does not consume. Writing one would mean inventing a format with no vanilla file
    /// to verify it against, which is the one thing this emitter does not do; dropping the
    /// declaration would deviate from every shipped CK3 map for no gain. So it stays declared and
    /// unwritten, exactly as vanilla has it.
    /// </summary>
    private static readonly string[] DeclaredFiles =
    [
        "definition.csv", "provinces.png", "rivers.png", "adjacencies.csv",
        "island_region.txt", "seasons.txt", "default.map", "heightmap.png",
    ];

    /// <summary>
    /// Written only when the heightmap is packed here. Without <c>writePacked</c> they are either
    /// left over from an earlier run or absent pending a repack in -mapeditor, and WriteHeightmap
    /// has already said which.
    /// </summary>
    private static readonly string[] PackedFiles =
    [
        "heightmap.heightmap", "packed_heightmap.png", "indirection_heightmap.png",
    ];

    private static void AssertMapDataComplete(string dir, bool writePacked)
    {
        var required = writePacked ? DeclaredFiles.Concat(PackedFiles) : DeclaredFiles.AsEnumerable();

        var missing = required.Where(f => !File.Exists(Path.Combine(dir, f))).ToList();

        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"map_data is missing {missing.Count} file(s) it is supposed to write: " +
                $"{string.Join(", ", missing)}. CK3 logs nothing for a missing map_data file; " +
                "the load stops with a core spinning.");

        // TopDirectoryOnly, and it is not a narrowing: the only subdirectory this mod ever puts
        // under map_data is geographical_regions, and nothing has created it yet when this runs —
        // BlankVanillaData, CompatibilityWriter and StruggleWriter all populate it later. So the
        // recursive flag was scanning a tree with no subdirectories in it.
        //
        // Worth being explicit about, because the recursion is not merely useless here, it is a
        // hazard: those three writers create their files by truncating, so every one of them is
        // momentarily zero bytes, and a scan that ran beside them would fail the run at random.
        // That is what stands between this check and map_data being written concurrently with the
        // rest of the mod.
        var empty = Directory.GetFiles(dir, "*", SearchOption.TopDirectoryOnly)
                             .Where(f => new FileInfo(f).Length == 0)
                             .Select(Path.GetFileName)
                             .ToList();

        if (empty.Count == 0) return;

        throw new InvalidOperationException(
            $"map_data contains {empty.Count} empty file(s): {string.Join(", ", empty)}. " +
            "CK3 spins forever reading a zero-byte map_data file.");
    }

    public static int[] BuildProvinceOrder(ProvinceMap provinces, out int baronyCount,
        out int landCount, out int riverCount)
    {
        var order = new int[provinces.Count];
        int next = 1;

        for (int i = 0; i < provinces.Count; i++)
            if (provinces.Seeds[i].IsLand && !provinces.Seeds[i].IsImpassable)
                order[i] = next++;
        baronyCount = next - 1;

        for (int i = 0; i < provinces.Count; i++)
            if (provinces.Seeds[i].IsLand && provinces.Seeds[i].IsImpassable)
                order[i] = next++;
        landCount = next - 1;

        for (int i = 0; i < provinces.Count; i++)
            if (!provinces.Seeds[i].IsLand && provinces.Seeds[i].IsMajorRiver)
                order[i] = next++;
        riverCount = next - 1;

        for (int i = 0; i < provinces.Count; i++)
            if (!provinces.Seeds[i].IsLand && !provinces.Seeds[i].IsMajorRiver)
                order[i] = next++;

        return order;
    }

    public static (byte R, byte G, byte B) ProvinceColor(int provinceId)
    {
        uint v = (uint)provinceId * 2654435761u % 0x1000000u;
        if (v == 0) v = 0x1000000u - 1;
        return ((byte)(v >> 16), (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF));
    }

    private static void WriteProvincesPng(string path, ProvinceMap provinces, int[] order)
    {
        var rgb = new byte[provinces.Label.Length * 3];
        Parallel.For(0, provinces.Label.Length, i =>
        {
            var (r, g, b) = ProvinceColor(order[provinces.Label[i]]);
            rgb[i * 3] = r;
            rgb[i * 3 + 1] = g;
            rgb[i * 3 + 2] = b;
        });
        PngWriter.WriteRgb8(path, provinces.Width, provinces.Height, rgb);
    }

    private static void WriteDefinitionCsv(string path, ProvinceMap provinces, int[] order)
    {
        var byId = new int[provinces.Count + 1];
        for (int label = 0; label < provinces.Count; label++) byId[order[label]] = label;

        var sb = new System.Text.StringBuilder();
        sb.Append("0;0;0;0;x;x;\n");
        for (int id = 1; id <= provinces.Count; id++)
        {
            var (r, g, b) = ProvinceColor(id);
            var seed = provinces.Seeds[byId[id]];
            string name = seed.IsLand ? $"prov_{id}"
                        : seed.IsMajorRiver ? $"river_{id}"
                        : $"sea_{id}";
            sb.Append($"{id};{r};{g};{b};{name};x;\n");
        }
        ParadoxText.WriteNoBom(path, sb.ToString());
    }

    public static byte[] RiverIndices(MapConfig cfg, ProvinceMap provinces, MapGen.Drainage? drainage = null)
    {
        if (drainage != null)
        {
            return MapGen.RiverMap.Generate(cfg, provinces, drainage);
        }

        var indices = new byte[cfg.ProvinceWidth * cfg.ProvinceHeight];
        Array.Fill(indices, RiverIndexLand);
        Parallel.For(0, provinces.Label.Length, i =>
        {
            if (!provinces.Seeds[provinces.Label[i]].IsLand) indices[i] = RiverIndexWater;
        });
        return indices;
    }

    private static void WriteRiversPng(string path, MapConfig cfg, ProvinceMap provinces, MapGen.Drainage drainage)
    {
        var indices = RiverIndices(cfg, provinces, drainage);

        var palette = new byte[256 * 3];
        for (int i = 0; i < 256; i++)
        {
            var (r, g, b) = RiverColour((byte)i);
            palette[i * 3] = r;
            palette[i * 3 + 1] = g;
            palette[i * 3 + 2] = b;
        }

        PngWriter.WriteIndexed8(path, cfg.ProvinceWidth, cfg.ProvinceHeight, indices, palette);
    }

    public static (byte R, byte G, byte B) RiverColour(byte index)
        => index < RiverPaletteHead.Length ? RiverPaletteHead[index]
            : index == RiverIndexWater ? ((byte)255, (byte)0, (byte)128)
            : index == RiverIndexLand ? ((byte)255, (byte)255, (byte)255)
            : ((byte)2, (byte)0, (byte)1);

    private static void ForceCoastlineToMatchProvinces(ushort[] height, MapConfig cfg,
            ProvinceMap provinces, int[] order, int landCount)
    {
        int pw = provinces.Width, ph = provinces.Height;
        int scaleX = cfg.Width / pw, scaleY = cfg.Height / ph;
        long changed = 0;

        Parallel.For(0, cfg.Height, () => 0L, (y, _, local) =>
        {
            int py = Math.Min(y / scaleY, ph - 1);
            long row = (long)y * cfg.Width;

            for (int x = 0; x < cfg.Width; x++)
            {
                int px = Math.Min(x / scaleX, pw - 1);
                int label = provinces.Label[py * pw + px];
                var seed = provinces.Seeds[label];
                bool provinceIsLand = seed.IsLand;

                long i = row + x;
                ushort v = height[i];
                bool heightmapIsLand = v > WaterLevel16;

                if (heightmapIsLand == provinceIsLand) continue;
                if (seed.IsMajorRiver && heightmapIsLand) continue;

                bool nearNaturalShore = false;
                for (int dy = -scaleY; dy <= scaleY && !nearNaturalShore; dy++)
                {
                    int ny = Math.Clamp(y + dy, 0, cfg.Height - 1);
                    long nrow = (long)ny * cfg.Width;
                    for (int dx = -scaleX; dx <= scaleX; dx++)
                    {
                        int nx = Math.Clamp(x + dx, 0, cfg.Width - 1);
                        if ((height[nrow + nx] > WaterLevel16) != heightmapIsLand)
                        {
                            nearNaturalShore = true;
                            break;
                        }
                    }
                }

                if (nearNaturalShore) continue;

                if (provinceIsLand)
                {
                    height[i] = (ushort)(WaterLevel16 + Step255);
                }
                else
                {
                    height[i] = 0;
                }
                local++;
            }
            return local;
        }, local => Interlocked.Add(ref changed, local));

        double pct = 100.0 * changed / height.Length;
        Console.WriteLine($"  coastline: {changed:N0} macro-mismatch pixels reconciled ({pct:F2}%), 8K sub-pixel contours preserved");
    }

    private static (byte[] LandDist, byte[] WaterDist) MeasureCoastDistances(ushort[] full, int width, int height, int cap)
    {
        const int Orthogonal = 3, Diagonal = 4;
        int capUnits = (cap + 1) * Orthogonal;

        var landDistUnits = new ushort[full.Length];
        var waterDistUnits = new ushort[full.Length];

        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                long i = (long)y * width + x;
                bool land = full[i] > WaterLevel16;
                landDistUnits[i] = (ushort)capUnits;
                waterDistUnits[i] = (ushort)capUnits;

                for (int dy = -1; dy <= 1; dy++)
                {
                    int yy = y + dy;
                    if (yy < 0 || yy >= height) continue;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int xx = ((x + dx) % width + width) % width;
                        if ((full[(long)yy * width + xx] > WaterLevel16) != land)
                        {
                            if (land) landDistUnits[i] = 0;
                            else waterDistUnits[i] = 0;
                            dy = 2;
                            break;
                        }
                    }
                }
            }
        });

        // Two chamfer transforms that only ever shared a loop: neither one reads or writes the
        // other's array, so running them side by side changes nothing about either. Each field
        // still sees its own sweeps in the same order, which is the part that must not move.
        Parallel.Invoke(
            () => Chamfer(landDistUnits, targetLand: true),
            () => Chamfer(waterDistUnits, targetLand: false));

        var landDist = new byte[full.Length];
        var waterDist = new byte[full.Length];

        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                long i = (long)y * width + x;
                landDist[i] = (byte)Math.Min(cap + 1, landDistUnits[i] / Orthogonal);
                waterDist[i] = (byte)Math.Min(cap + 1, waterDistUnits[i] / Orthogonal);
            }
        });

        return (landDist, waterDist);

        // One field's forward and backward sweeps.
        //
        // Sequential over rows, and it has to stay that way: cell (x, y) is fed by distances this
        // same sweep already lowered at (x - 1, y) and along row y - 1. Handing rows to threads
        // reads values that are still being written — a data race in the array that decides where
        // the shelf and the coastal cliffs go.
        void Chamfer(ushort[] dist, bool targetLand)
        {
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    long target = (long)y * width + x;
                    if ((full[target] > WaterLevel16) != targetLand) continue;

                    Relax(dist, targetLand, target, y, x, -1, 0, Orthogonal);
                    Relax(dist, targetLand, target, y, x, -1, -1, Diagonal);
                    Relax(dist, targetLand, target, y, x, 0, -1, Orthogonal);
                    Relax(dist, targetLand, target, y, x, 1, -1, Diagonal);
                }

            for (int y = height - 1; y >= 0; y--)
                for (int x = width - 1; x >= 0; x--)
                {
                    long target = (long)y * width + x;
                    if ((full[target] > WaterLevel16) != targetLand) continue;

                    Relax(dist, targetLand, target, y, x, 1, 0, Orthogonal);
                    Relax(dist, targetLand, target, y, x, 1, 1, Diagonal);
                    Relax(dist, targetLand, target, y, x, 0, 1, Orthogonal);
                    Relax(dist, targetLand, target, y, x, -1, 1, Diagonal);
                }
        }

        // The target's own class is tested once per pixel by the caller rather than eight times
        // here, and the horizontal wrap — two modulos on every one of a few hundred million calls —
        // now only runs on the two edge columns, which are the only places x ± 1 can leave the row.
        // Neither changes which candidates are compared or in what order.
        void Relax(ushort[] distArray, bool targetLand, long target, int y, int x, int dx, int dy, int cost)
        {
            int yy = y + dy;
            if (yy < 0 || yy >= height) return;

            int xx = x + dx;
            if ((uint)xx >= (uint)width) xx = (xx + width) % width;

            long from = (long)yy * width + xx;
            if ((full[from] > WaterLevel16) != targetLand) return;

            int candidate = distArray[from] + cost;
            if (candidate < distArray[target]) distArray[target] = (ushort)candidate;
        }
    }

    private static void ShapeCoastline(ushort[] full, MapConfig cfg)
    {
        int width = cfg.Width, height = cfg.Height;

        int landReach = Math.Max(2, (int)Math.Round(cfg.Scaled(cfg.CoastalCliffReach)));
        float landStrength = (float)Math.Clamp(cfg.CoastalCliffSmoothing, 0.0, 1.0);
        const int lowestLand = WaterLevel16 + Step255;

        // Fast plunge curve matching vanilla 3-4 pixel shelf
        const int shelfReach = 7;

        var (landDistance, waterDistance) = Core.Stage.Detail("        · coast distances",
            () => MeasureCoastDistances(full, width, height, Math.Max(shelfReach, landReach)));

        var source = (ushort[])full.Clone();

        Parallel.For(0, height, y =>
        {
            long row = (long)y * width;
            for (int x = 0; x < width; x++)
            {
                long i = row + x;
                ushort val = source[i];

                if (val > WaterLevel16)
                {
                    // 1. LAND-SIDE GENTLE COASTAL BEVEL
                    int d = landDistance[i];
                    if (d <= landReach && landStrength > 0.0f)
                    {
                        int excess = val - lowestLand;
                        if (excess > 0)
                        {
                            float t = (float)(d + 1) / (landReach + 1);
                            float curve = t * t * (3.0f - 2.0f * t);

                            int ramped = lowestLand + (int)Math.Round(excess * curve);
                            int finalElev = (int)Math.Round(val * (1.0f - landStrength) + ramped * landStrength);
                            full[i] = (ushort)Math.Clamp(finalElev, lowestLand, 65535);
                        }
                    }
                }
                else
                {
                    // 2. WATER-SIDE FAST PLUNGE (3-4 pixels down to deep black bed)
                    // Plunges: d=1 (~16/255), d=2 (~9/255), d=3 (~3/255), d>=4 (0)
                    int d = waterDistance[i];
                    if (d <= shelfReach)
                    {
                        float t = (float)d / shelfReach;
                        float plunge = (1.0f - t) * (1.0f - t); // Quadratic rapid drop
                        int shelfHeight = (int)Math.Round((WaterLevel16 - Step255 * 3) * plunge);
                        full[i] = (ushort)Math.Clamp(shelfHeight, 0, WaterLevel16);
                    }
                    else
                    {
                        // Deep water
                        full[i] = 0;
                    }
                }
            }
        });

        Console.WriteLine($"  coastline shaping: underwater shelf plunged to deep bed over {shelfReach} px, " +
                          $"land coast smoothed over {landReach} px");
    }

    /// <summary>
    /// Simulation elevation back onto the 16-bit heightmap scale — the inverse of
    /// <see cref="MapGen.HeightmapSource.ToSimulationScale"/>.
    ///
    /// Two straight lines meeting at the land threshold rather than one across the whole range,
    /// because that threshold is a fixed value in the file and not a fraction of it: 4883 of 65535
    /// is where land begins, and stretching one line across both halves would move the coastline
    /// whenever the deepest trench or the highest peak changed. (It is not where the sea is drawn
    /// — that is <see cref="WaterPlane16"/>, lower down — but it is what the coastline is cut on.)
    ///
    /// This is the only conversion in the tool, and it is private on purpose. There used to be a
    /// public second copy on HeightmapSource, and the two had already drifted — it rounded where
    /// this truncates. Anything that wants to know what the engine will render wants
    /// <see cref="ShippedHeightmap"/> anyway, which is this plus the two coastline passes.
    /// </summary>
    private static ushort[] ElevationTo16(float[] elevation, MapConfig cfg)
    {
        float sea = cfg.Limits.SeaLevelUpper;
        float floor = cfg.SeaFloorElevation;
        float peak = cfg.PeakElevation;

        float belowRange = Math.Max(1e-3f, sea - floor);
        float aboveRange = Math.Max(1e-3f, peak - sea - 1f);

        var result = new ushort[elevation.Length];

        Parallel.For(0, elevation.Length, i =>
        {
            float e = elevation[i];
            double v = e <= sea
                ? (e - floor) / belowRange * WaterLevel16
                : WaterLevel16 + (e - sea - 1f) / aboveRange * (65535.0 - WaterLevel16);

            result[i] = (ushort)Math.Clamp(v, 0, 65535);
        });

        return result;
    }

    private static void ReportHypsometry(ushort[] height)
        => Console.WriteLine($"  heightmap as shipped: {MapGen.Hypsometry.Measure(height).Describe()}");

    private static void ReportPacking(HeightmapPacker.Result packing)
    {
        long tiles = packing.TilesPerLevel.Sum();
        long slots = packing.SlotsPerLevel.Sum();

        string shares = string.Join(" / ",
            packing.TilesPerLevel.Select(n => $"{100.0 * n / Math.Max(1, tiles):F2}"));

        Console.WriteLine($"  heightmap packed: {packing.TilesX}x{packing.TilesY} tiles into " +
                          $"{packing.PackedWidth}x{packing.PackedHeight} " +
                          $"({packing.PackedWidth * (long)packing.PackedHeight / 1_000_000.0:F1}M px, " +
                          $"vanilla 3185x4061 = 12.9M)");

        Console.WriteLine($"  level shares: {shares} % " +
                          $"(vanilla 2.56 / 11.93 / 14.71 / 11.67 / 59.13)");

        Console.WriteLine($"  {tiles:N0} tiles share {slots:N0} atlas slots " +
                          $"({(double)tiles / Math.Max(1, slots):F2}x reuse, vanilla 1.53x); " +
                          $"empty tile at {packing.EmptyR},{packing.EmptyG}");

        // The number the floating-props work is actually about: how far the terrain CK3 draws
        // departs, either way, from the heightmap it snaps props and borders to. Below it and a
        // tree floats; above it and terrain comes up through a province border. Vanilla's own land
        // tiles run to 19.32u short and 4.78u over.
        Console.WriteLine(packing.SagBudget <= 0
            ? $"  terrain error: worst {packing.WorstError:F2} world units either way, no budget "
              + "set (vanilla level shares, HeightmapSagBudget = 0)"
            : $"  terrain error: worst {packing.WorstError:F2} world units either way, against a "
              + $"{packing.SagBudget:F2}u budget");
    }

    /// <summary>
    /// The heightmap as it goes into heightmap.png: the elevation conversion plus the two passes
    /// that move the shoreline — forcing it to agree with provinces.png, then plunging the
    /// shelf and smoothing the land side.
    ///
    /// Public so a caller can ask what the engine will actually render. Round-tripping this
    /// through <see cref="HeightmapPacker.Reconstruct"/> is the only way to find out where the
    /// shoreline ends up once the terrain has been quantised into a tile atlas and reassembled,
    /// which is what the scatter passes need and what the preview draws.
    /// </summary>
    public static ushort[] ShippedHeightmap(MapConfig cfg, ProvinceMap provinces, int[] order,
        int landCount, MapGen.TerrainData terra)
    {
        var full = Core.Stage.Detail("      · to 16-bit", () => ElevationTo16(terra.Elevation, cfg));
        Core.Stage.Detail("      · match provinces",
            () => ForceCoastlineToMatchProvinces(full, cfg, provinces, order, landCount));
        Core.Stage.Detail("      · shape coastline", () => ShapeCoastline(full, cfg));
        return full;
    }

    /// <returns>The heightmap as shipped, so the caller can hand it to anything that has to
    /// reason about the surface the engine will render rather than the one we computed.</returns>
    private static ushort[] WriteHeightmap(string dir, MapConfig cfg, bool writePacked,
        ProvinceMap provinces, int[] order, int landCount, MapGen.TerrainData terra)
    {
        var full = Core.Stage.Detail("    · coastline + shaping",
            () => ShippedHeightmap(cfg, provinces, order, landCount, terra));

        ReportHypsometry(full);
        Core.Stage.Detail("    · heightmap.png encode",
            () => PngWriter.WriteGray16(Path.Combine(dir, "heightmap.png"), cfg.Width, cfg.Height, full));

        if (!writePacked)
        {
            bool have = File.Exists(Path.Combine(dir, "packed_heightmap.png"))
                     && File.Exists(Path.Combine(dir, "indirection_heightmap.png"))
                     && File.Exists(Path.Combine(dir, "heightmap.heightmap"));

            Console.WriteLine(have
                ? "  heightmap: kept existing packed/indirection (repacked in -mapeditor)"
                : "  heightmap: no packed/indirection present — open in -mapeditor and repack");
            return full;
        }

        int tileStep = HeightmapPacker.TileStepFor(cfg);
        var packing = Core.Stage.Detail("    · pack atlas",
            () => HeightmapPacker.Pack(full, cfg.Width, cfg.Height, cfg.HeightmapSagBudget,
                                       tileStep, cfg.BalanceNeighbourLods));

        Core.Stage.Detail("    · packed + indirection encode", () =>
        {
            PngWriter.WriteGray16(Path.Combine(dir, "packed_heightmap.png"),
                packing.PackedWidth, packing.PackedHeight, packing.Packed);
            PngWriter.WriteRgba8(Path.Combine(dir, "indirection_heightmap.png"),
                packing.TilesX, packing.TilesY, packing.Indirection);
        });

        ReportPacking(packing);

        string levelOffsets = string.Join(" ", packing.LevelOffsets.Select(n => $"{{ 0 {n} }}"));

        // BOM, unlike the rest of map_data. Verified on the bytes of two files CK3 renders
        // correctly: vanilla's own heightmap.heightmap and one written by Clausewitz's repacker
        // both begin ef bb bf, and ours began "heig". It is the only map_data file that wants one.
        ParadoxText.WriteBom(Path.Combine(dir, "heightmap.heightmap"),
            $$"""
              heightmap_file="map_data/packed_heightmap.png"
              indirection_file="map_data/indirection_heightmap.png"
              original_heightmap_size={ {{cfg.Width}} {{cfg.Height}} }
              tile_size={{HeightmapPacker.TileSize(0, tileStep)}}
              should_wrap_x=no
              level_offsets={ {{levelOffsets}} }
              max_compress_level={{HeightmapPacker.Levels - 1}}
              empty_tile_offset={ {{packing.EmptyR}} {{packing.EmptyG}} }

              """);

        return full;
    }


    private static void WriteDefaultMap(string path, int provinceCount, int baronyCount,
        int landCount, int riverCount)
    {
        string impassable = landCount > baronyCount
            ? $"impassable_mountains = RANGE {{ {baronyCount + 1} {landCount} }}"
            : "";

        string rivers = riverCount > landCount
            ? $"river_provinces = RANGE {{ {landCount + 1} {riverCount} }}"
            : "";

        string seaZones = riverCount < provinceCount
            ? $"sea_zones = RANGE {{ {riverCount + 1} {provinceCount} }}"
            : "";

        // continent.txt is declared here and never written, which is correct: no CK3 map ships
        // one. See DeclaredFiles for the evidence and the reasoning.
        ParadoxText.WriteNoBom(path,
            $$"""
          definitions = "definition.csv"
          provinces = "provinces.png"
          rivers = "rivers.png"
          topology = "heightmap.heightmap"
          continent = "continent.txt"
          adjacencies = "adjacencies.csv"
          island_region = "island_region.txt"
          seasons = "seasons.txt"

          #############
          # SEA ZONES
          #############
          {{seaZones}}

          #############
          # MAJOR RIVERS
          #############
          {{rivers}}

          #############
          # LAKES
          #############

          #############
          # IMPASSABLE
          #############
          {{impassable}}

          """);
    }

    private static void WriteStubs(string dir)
    {
        ParadoxText.WriteNoBom(Path.Combine(dir, "adjacencies.csv"),
            """
            From;To;Type;Through;start_x;start_y;stop_x;stop_y;Comment
            -1;-1;;-1;-1;-1;-1;-1;

            """);

        ParadoxText.WriteNoBom(Path.Combine(dir, "island_region.txt"),
            """
            # Island regions - no land path from the continent
            # The AI needs these to optimize path finding
            #
            # NOTE: do not add any regions here that are NOT islands
            #
            # Island regions can be declared with one or more of the following fields:
            #	duchies = { }, takes duchy title names declared in landed_titles.txt
            #	counties = { }, takes county title names declared in landed_titles.txt
            #	provinces = { }, takes province id numbers declared in /history/provinces

            """);

        ParadoxText.WriteNoBom(Path.Combine(dir, "seasons.txt"),
            """
            winter = {
            	start_date=00.12.01
            	end_date=00.02.31
            }

            spring = {
            	start_date=00.04.01
            	end_date=00.05.1
            }

            summer = {
            	start_date=00.06.01
            	end_date=00.09.10
            }

            autumn = {
            	start_date=00.10.10
            	end_date=00.10.31
            }

            tree_winter = {
            	start_date=00.11.15
            	end_date=00.12.01
            }
            tree_winter2 = {
            	start_date=00.12.20
            	end_date=00.01.20
            }
            tree_spring = {
            	start_date=00.02.20
            	end_date=00.03.01
            }
            tree_spring2 = {
            	start_date=00.03.20
            	end_date=00.04.20
            }
            tree_summer = {
            	start_date=00.05.20
            	end_date=00.06.01
            }
            tree_summer2 = {
            	start_date=00.06.20
            	end_date=00.09.10
            }
            tree_autumn = {
            	start_date=00.10.01
            	end_date=00.10.10
            }
            tree_autumn2 = {
            	start_date=00.10.25
            	end_date=00.11.01
            }

            """);
    }
}