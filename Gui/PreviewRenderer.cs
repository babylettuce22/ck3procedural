using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Gui;

public static class PreviewRenderer
{
    public readonly record struct Image(byte[] Rgb, int Width, int Height);
    private const int MaxWidth = 2048;

    /// <summary>
    /// An <see cref="Image"/> as a GDI bitmap. Lives here rather than on the form because the 3D
    /// source view produces the same buffers and would otherwise carry a second copy of the
    /// RGB-to-BGR shuffle.
    /// </summary>
    public static System.Drawing.Bitmap ToBitmap(Image image)
    {
        var bitmap = new System.Drawing.Bitmap(image.Width, image.Height,
            System.Drawing.Imaging.PixelFormat.Format24bppRgb);

        var rect = new System.Drawing.Rectangle(0, 0, image.Width, image.Height);
        var data = bitmap.LockBits(rect, System.Drawing.Imaging.ImageLockMode.WriteOnly,
            System.Drawing.Imaging.PixelFormat.Format24bppRgb);

        try
        {
            var row = new byte[image.Width * 3];
            for (int y = 0; y < image.Height; y++)
            {
                int src = y * image.Width * 3;
                for (int x = 0; x < image.Width; x++)
                {
                    row[x * 3 + 0] = image.Rgb[src + x * 3 + 2];
                    row[x * 3 + 1] = image.Rgb[src + x * 3 + 1];
                    row[x * 3 + 2] = image.Rgb[src + x * 3 + 0];
                }
                System.Runtime.InteropServices.Marshal.Copy(
                    row, 0, data.Scan0 + y * data.Stride, row.Length);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }

    public static int StepFor(int width) => Math.Max(1, (width + MaxWidth - 1) / MaxWidth);

    // --- Progressive Overloads ---

    public static Image RenderRelief(float[] elevation, MapConfig cfg)
    {
        int width = cfg.ProvinceWidth, height = cfg.ProvinceHeight;
        float sea = cfg.Limits.SeaLevelUpper;
        float peak = Math.Max(sea + 1f, cfg.PeakElevation);

        return Downsample(width, height, i =>
        {
            float e = elevation[i];
            if (e <= sea)
            {
                float depth = Math.Clamp((sea - e) / Math.Max(1f, sea - cfg.SeaFloorElevation), 0, 1);
                return ((byte)(38 + 26 * (1 - depth)), (byte)(70 + 44 * (1 - depth)),
                        (byte)(104 + 48 * (1 - depth)));
            }

            int x = i % width, y = i / width;
            float left = elevation[y * width + Math.Max(0, x - 1)];
            float up = elevation[Math.Max(0, y - 1) * width + x];
            double shade = Math.Clamp(0.75 - ((e - left) + (e - up)) * 0.05, 0.25, 1.35);

            double t = Math.Clamp((e - sea) / (peak - sea), 0, 1);
            var (r, g, b) = t < 0.10 ? (116, 146, 86)
                : t < 0.28 ? (92, 124, 68)
                : t < 0.48 ? (140, 128, 84)
                : t < 0.70 ? (128, 112, 98)
                : (232, 234, 238);

            return ((byte)Math.Clamp(r * shade, 0, 255), (byte)Math.Clamp(g * shade, 0, 255),
                    (byte)Math.Clamp(b * shade, 0, 255));
        });
    }

    public static Image RenderClimate(KoppenClass[] climate, MapConfig cfg)
        => Downsample(cfg.ProvinceWidth, cfg.ProvinceHeight, i => Koppen.Colour(climate[i]));

    public static Image RenderClimate(ClimateField climate, MapConfig cfg)
    {
        int n = cfg.ProvinceWidth * cfg.ProvinceHeight;
        var koppen = new KoppenClass[n];
        Parallel.For(0, n, i =>
        {
            koppen[i] = Koppen.Classify(
                climate.WarmC[i],
                climate.ColdC[i],
                climate.MeanC[i],
                climate.AnnualMm[i],
                climate.SummerMm[i],
                climate.WinterMm[i]);
        });

        return Downsample(cfg.ProvinceWidth, cfg.ProvinceHeight, i => Koppen.Colour(koppen[i]));
    }

    public static Image RenderDrainage(Drainage drainage, float[] elevation, MapConfig cfg)
        => Downsample(drainage.Width, drainage.Height,
            i => drainage.Shade(elevation, cfg, i),
            drainage.ViewRank);

    public static Image RenderRivers(byte[] indices, MapConfig cfg)
        => Downsample(cfg.ProvinceWidth, cfg.ProvinceHeight,
            i => Emit.MapDataWriter.RiverColour(indices[i]),
            i => IsCourse(indices[i]) ? 2 + indices[i] : 0);

    private static bool IsCourse(byte index)
        => index != Emit.MapDataWriter.RiverIndexLand
        && index != Emit.MapDataWriter.RiverIndexWater;

    public static Image RenderProvinces(ProvinceMap map, MapConfig cfg)
    {
        var rng = new Rng(cfg.Seed ^ 0x9E37);
        var colours = new (byte R, byte G, byte B)[map.Count];
        for (int i = 0; i < map.Count; i++)
        {
            var seed = map.Seeds[i];
            if (seed.IsLand)
                colours[i] = ((byte)rng.Int(60, 235), (byte)rng.Int(90, 235), (byte)rng.Int(55, 190));
            else if (seed.IsMajorRiver)
                colours[i] = ((byte)rng.Int(0, 40), (byte)rng.Int(130, 200), (byte)rng.Int(200, 255));
            else
                colours[i] = ((byte)rng.Int(20, 70), (byte)rng.Int(45, 105), (byte)rng.Int(90, 170));
        }

        return Downsample(map.Width, map.Height, i => colours[map.Label[i]]);
    }

    public static Image RenderTerrain(TerrainClass[] terrain, MapConfig cfg)
        => Downsample(cfg.ProvinceWidth, cfg.ProvinceHeight, i => Colour(terrain[i]));

    public static Image RenderTitles(
        ProvinceMap map,
        int[] order,
        int baronyCount,
        int landCount,
        List<Title> titles,
        string targetTier)
    {
        int width = map.Width, height = map.Height;
        var targetTitles = Titles.Flatten(titles).Where(t => t.Tier == targetTier).ToList();
        var targetIndexMap = new Dictionary<Title, int>();
        for (int i = 0; i < targetTitles.Count; i++) targetIndexMap[targetTitles[i]] = i;

        var titleIndexOf = new int[baronyCount + 1];
        Array.Fill(titleIndexOf, NoCounty);

        var baronies = Titles.Flatten(titles).Where(t => t.Tier == "b");
        foreach (var b in baronies)
        {
            if (b.ProvinceId >= 1 && b.ProvinceId <= baronyCount)
            {
                Title? ancestor = b;
                while (ancestor != null && ancestor.Tier != targetTier) ancestor = ancestor.Parent;
                if (ancestor != null && targetIndexMap.TryGetValue(ancestor, out int index))
                    titleIndexOf[b.ProvinceId] = index;
            }
        }

        int At(int i)
        {
            int id = order[map.Label[i]];
            return id <= baronyCount ? titleIndexOf[id] : id <= landCount ? Impassable : Water;
        }

        bool Edge(int i, int titleIndex)
        {
            int x = i % width, y = i / width;
            return (x + 1 < width && At(i + 1) != titleIndex)
                || (y + 1 < height && At(i + width) != titleIndex);
        }

        return Downsample(width, height,
            i =>
            {
                int t = At(i);
                if (t == Water) return ((byte)38, (byte)62, (byte)96);
                if (t == Impassable) return ((byte)92, (byte)92, (byte)100);
                if (t == NoCounty) return ((byte)255, (byte)0, (byte)255);

                var color = targetTitles[t].Color;
                return Edge(i, t) ? ((byte)22, (byte)24, (byte)28) : color;
            },
            i =>
            {
                int t = At(i);
                return t >= 0 && Edge(i, t) ? 1 : 0;
            });
    }

    // --- Full GenerationResult Wrappers (kept for on-demand tab switching) ---
    public static Image RenderTerrain(GenerationResult r) => RenderTerrain(r.Terrain.Terrain, r.Config);
    public static Image RenderRelief(GenerationResult r) => RenderRelief(r.ProvinceElevation, r.Config);
    public static Image RenderClimate(GenerationResult r) => RenderClimate(r.Terrain.Climate, r.Config);
    public static Image RenderDrainage(GenerationResult r) => RenderDrainage(r.Drainage, r.ProvinceElevation, r.Config);
    public static Image RenderRivers(GenerationResult r) => RenderRivers(Emit.MapDataWriter.RiverIndices(r.Config, r.Provinces, r.Drainage), r.Config);
    public static Image RenderProvinces(GenerationResult r) => RenderProvinces(r.Provinces, r.Config);
    public static Image RenderCounties(GenerationResult r) => RenderTitles(r.Provinces, r.ProvinceOrder, r.BaronyCount, r.LandCount, r.Titles, "c");
    public static Image RenderDuchies(GenerationResult r) => RenderTitles(r.Provinces, r.ProvinceOrder, r.BaronyCount, r.LandCount, r.Titles, "d");
    public static Image RenderKingdoms(GenerationResult r) => RenderTitles(r.Provinces, r.ProvinceOrder, r.BaronyCount, r.LandCount, r.Titles, "k");
    public static Image RenderEmpires(GenerationResult r) => RenderTitles(r.Provinces, r.ProvinceOrder, r.BaronyCount, r.LandCount, r.Titles, "e");

    public static Image RenderHeightmap(GenerationResult result)
    {
        var cfg = result.Config;
        var full = Emit.MapDataWriter.ShippedHeightmap(
            cfg, result.Provinces, result.ProvinceOrder, result.LandCount, result.Terra);

        return Downsample(cfg.Width, cfg.Height, i =>
        {
            var v = (byte)(full[i] / Emit.MapDataWriter.Step255);
            return (v, v, v);
        });
    }

    private const int NoCounty = -1, Impassable = -2, Water = -3;

    private static Image RenderTitles(GenerationResult result, string targetTier)
        => RenderTitles(result.Provinces, result.ProvinceOrder, result.BaronyCount, result.LandCount, result.Titles, targetTier);

    public static Image RenderRealms(GenerationResult result, MapGen.RealmMap? realms, MapGen.WildernessMap? wilderness)
    {
        if (realms is null) return RenderTitles(result, "c");
        var map = result.Provinces;
        var order = result.ProvinceOrder;
        int width = map.Width, height = map.Height;
        int baronyCount = result.BaronyCount, landCount = result.LandCount;
        var counties = Titles.Flatten(result.Titles).Where(t => t.Tier == "c").ToList();

        var primaryOf = new Dictionary<Title, Title>();
        foreach (var (title, holder) in realms.HolderCounty)
        {
            if (!primaryOf.TryGetValue(holder, out var best)
                || Emit.HistoryWriter.Rank(title) > Emit.HistoryWriter.Rank(best))
                primaryOf[holder] = title;
        }

        var colour = new (byte R, byte G, byte B)[counties.Count];
        var wild = new bool[counties.Count];

        for (int c = 0; c < counties.Count; c++)
        {
            var county = counties[c];
            if (wilderness?.Contains(county) == true) { wild[c] = true; continue; }
            var holder = realms.HolderCounty.GetValueOrDefault(county, county);
            var top = primaryOf.GetValueOrDefault(holder, county);
            while (realms.Liege.TryGetValue(top, out var liege)) top = liege;
            colour[c] = top.Color;
        }

        var countyOf = new int[baronyCount + 1];
        Array.Fill(countyOf, NoCounty);
        for (int c = 0; c < counties.Count; c++)
            foreach (var barony in counties[c].Children)
                if (barony.ProvinceId >= 1 && barony.ProvinceId <= baronyCount)
                    countyOf[barony.ProvinceId] = c;

        int At(int i)
        {
            int id = order[map.Label[i]];
            return id <= baronyCount ? countyOf[id] : id <= landCount ? Impassable : Water;
        }

        bool Edge(int i, int county)
        {
            int x = i % width, y = i / width;
            return (x + 1 < width && Differs(At(i + 1), county))
                || (y + 1 < height && Differs(At(i + width), county));

            bool Differs(int other, int here)
            {
                if (other == here) return false;
                if (other < 0 || here < 0) return true;
                return wild[other] != wild[here] || colour[other] != colour[here];
            }
        }

        return Downsample(width, height,
            i =>
            {
                int c = At(i);
                if (c == Water) return ((byte)38, (byte)62, (byte)96);
                if (c == Impassable) return ((byte)92, (byte)92, (byte)100);
                if (c == NoCounty) return ((byte)255, (byte)0, (byte)255);
                if (Edge(i, c)) return ((byte)22, (byte)24, (byte)28);
                return wild[c] ? ((byte)168, (byte)120, (byte)48) : colour[c];
            },
            i =>
            {
                int c = At(i);
                return c >= 0 && Edge(i, c) ? 1 : 0;
            });
    }

    private static Image RenderByCounty(GenerationResult result, MapGen.WildernessMap? wilderness,
        Func<Title, (byte R, byte G, byte B)?> colourOf)
    {
        var map = result.Provinces;
        var order = result.ProvinceOrder;
        int width = map.Width, height = map.Height;
        int baronyCount = result.BaronyCount, landCount = result.LandCount;
        var counties = Titles.Flatten(result.Titles).Where(t => t.Tier == "c").ToList();

        var colour = new (byte R, byte G, byte B)[counties.Count];
        var wild = new bool[counties.Count];

        for (int c = 0; c < counties.Count; c++)
        {
            if (wilderness?.Contains(counties[c]) == true) { wild[c] = true; continue; }
            if (colourOf(counties[c]) is { } found) colour[c] = found;
            else wild[c] = true;
        }

        var countyOf = new int[baronyCount + 1];
        Array.Fill(countyOf, NoCounty);
        for (int c = 0; c < counties.Count; c++)
            foreach (var barony in counties[c].Children)
                if (barony.ProvinceId >= 1 && barony.ProvinceId <= baronyCount)
                    countyOf[barony.ProvinceId] = c;

        int At(int i)
        {
            int id = order[map.Label[i]];
            return id <= baronyCount ? countyOf[id] : id <= landCount ? Impassable : Water;
        }

        bool Edge(int i, int county)
        {
            int x = i % width, y = i / width;
            return (x + 1 < width && Differs(At(i + 1), county))
                || (y + 1 < height && Differs(At(i + width), county));

            bool Differs(int other, int here)
            {
                if (other == here) return false;
                if (other < 0 || here < 0) return true;
                return wild[other] != wild[here] || colour[other] != colour[here];
            }
        }

        return Downsample(width, height,
            i =>
            {
                int c = At(i);
                if (c == Water) return ((byte)38, (byte)62, (byte)96);
                if (c == Impassable) return ((byte)92, (byte)92, (byte)100);
                if (c == NoCounty) return ((byte)255, (byte)0, (byte)255);
                if (Edge(i, c)) return ((byte)22, (byte)24, (byte)28);
                return wild[c] ? ((byte)168, (byte)120, (byte)48) : colour[c];
            },
            i =>
            {
                int c = At(i);
                return c >= 0 && Edge(i, c) ? 1 : 0;
            });
    }

    public static Image RenderCultures(GenerationResult result, MapGen.CultureMap? cultures, MapGen.WildernessMap? wilderness)
        => cultures is null ? RenderTitles(result, "c") : RenderByCounty(result, wilderness, county => cultures.ByCounty.TryGetValue(county, out var c) ? c.Color : null);

    public static Image RenderFaiths(GenerationResult result, MapGen.FaithMap? faiths, MapGen.WildernessMap? wilderness)
        => faiths is null ? RenderTitles(result, "c") : RenderByCounty(result, wilderness, county => faiths.ByCounty.TryGetValue(county, out var f) ? ((byte)Math.Clamp(f.Color.R * 255, 0, 255), (byte)Math.Clamp(f.Color.G * 255, 0, 255), (byte)Math.Clamp(f.Color.B * 255, 0, 255)) : null);

    public static Image RenderGovernment(GenerationResult result, MapGen.CultureMap? cultures = null, MapGen.WorldCenterMap? worldCenters = null)
    {
        var map = result.Provinces;
        var order = result.ProvinceOrder;
        int width = map.Width, height = map.Height;
        int baronyCount = result.BaronyCount, landCount = result.LandCount;
        var cfg = result.Config;
        var empires = result.Titles;
        var counties = Titles.Flatten(result.Titles).Where(t => t.Tier == "c").ToList();
        var provinceTerrain = Emit.ContentWriter.ProvinceTerrain(cfg, map, order, result.Terrain.Terrain, landCount);
        var development = MapGen.Development.ForCounties(counties, provinceTerrain, cfg, new Rng(cfg.Seed ^ 0x0DE7));
        var wilderness = MapGen.Wilderness.Build(counties, map, order, landCount, provinceTerrain, development, cfg, new Rng(cfg.Seed ^ 0x1D17));
        var realms = MapGen.Realms.Build(empires, development, wilderness, cfg, new Rng(cfg.Seed ^ 0x2E17));
        var governments = MapGen.Governments.Build(empires, counties, realms, provinceTerrain, development, cultures, worldCenters, cfg, new Rng(cfg.Seed ^ 0x6017));

        var government = new string[counties.Count];
        for (int c = 0; c < counties.Count; c++) government[c] = governments.For(counties[c]);

        var countyOf = new int[baronyCount + 1];
        Array.Fill(countyOf, NoCounty);
        for (int c = 0; c < counties.Count; c++)
            foreach (var barony in counties[c].Children)
                if (barony.ProvinceId >= 1 && barony.ProvinceId <= baronyCount)
                    countyOf[barony.ProvinceId] = c;

        int At(int i)
        {
            int id = order[map.Label[i]];
            return id <= baronyCount ? countyOf[id] : id <= landCount ? Impassable : Water;
        }

        bool Edge(int i, int county)
        {
            int x = i % width, y = i / width;
            return (x + 1 < width && At(i + 1) != county) || (y + 1 < height && At(i + width) != county);
        }

        var boundaryColor = ((byte)22, (byte)24, (byte)28);

        (byte, byte, byte) Colour(string g) => g switch
        {
            GovernmentMap.Administrative => ((byte)155, (byte)60, (byte)160),
            GovernmentMap.Nomad => ((byte)210, (byte)160, (byte)65),
            GovernmentMap.Tribal => ((byte)185, (byte)95, (byte)60),
            GovernmentMap.Clan => ((byte)80, (byte)150, (byte)95),
            GovernmentMap.Republic => ((byte)200, (byte)70, (byte)70),
            GovernmentMap.Theocracy => ((byte)205, (byte)205, (byte)200),
            _ => ((byte)65, (byte)110, (byte)160),
        };

        return Downsample(width, height,
            i =>
            {
                int c = At(i);
                if (c == Water) return ((byte)38, (byte)62, (byte)96);
                if (c == Impassable) return ((byte)92, (byte)92, (byte)100);
                if (c == NoCounty) return ((byte)255, (byte)0, (byte)255);
                return Edge(i, c) ? boundaryColor : Colour(government[c]);
            },
            i =>
            {
                int c = At(i);
                return c >= 0 && Edge(i, c) ? 1 : 0;
            });
    }

    public static Image RenderWilderness(GenerationResult result)
    {
        var map = result.Provinces;
        var order = result.ProvinceOrder;
        int width = map.Width, height = map.Height;
        int baronyCount = result.BaronyCount, landCount = result.LandCount;
        var cfg = result.Config;
        var counties = Titles.Flatten(result.Titles).Where(t => t.Tier == "c").ToList();
        var provinceTerrain = Emit.ContentWriter.ProvinceTerrain(cfg, map, order, result.Terrain.Terrain, landCount);
        var development = MapGen.Development.ForCounties(counties, provinceTerrain, cfg, new Rng(cfg.Seed ^ 0x0DE7));
        var wilderness = MapGen.Wilderness.Build(counties, map, order, landCount, provinceTerrain, development, cfg, new Rng(cfg.Seed ^ 0x1D17));

        var isWild = new bool[counties.Count];
        for (int c = 0; c < counties.Count; c++) isWild[c] = wilderness.Contains(counties[c]);

        var countyOf = new int[baronyCount + 1];
        Array.Fill(countyOf, NoCounty);
        for (int c = 0; c < counties.Count; c++)
            foreach (var barony in counties[c].Children)
                if (barony.ProvinceId >= 1 && barony.ProvinceId <= baronyCount)
                    countyOf[barony.ProvinceId] = c;

        int At(int i)
        {
            int id = order[map.Label[i]];
            return id <= baronyCount ? countyOf[id] : id <= landCount ? Impassable : Water;
        }

        bool Edge(int i, int county)
        {
            int x = i % width, y = i / width;
            return (x + 1 < width && At(i + 1) != county) || (y + 1 < height && At(i + width) != county);
        }

        bool Frontier(int i, int county)
        {
            int x = i % width, y = i / width;
            return (x + 1 < width && Neighbour(At(i + 1)))
                || (y + 1 < height && Neighbour(At(i + width)))
                || (x > 0 && Neighbour(At(i - 1)))
                || (y > 0 && Neighbour(At(i - width)));

            bool Neighbour(int other) => other >= 0 && other != county && !isWild[other];
        }

        var boundaryColor = ((byte)22, (byte)24, (byte)28);

        return Downsample(width, height,
            i =>
            {
                int c = At(i);
                if (c == Water) return ((byte)38, (byte)62, (byte)96);
                if (c == Impassable) return ((byte)92, (byte)92, (byte)100);
                if (c == NoCounty) return ((byte)255, (byte)0, (byte)255);

                if (isWild[c])
                {
                    if (Edge(i, c)) return boundaryColor;
                    return Frontier(i, c) ? ((byte)255, (byte)190, (byte)90) : ((byte)168, (byte)120, (byte)48);
                }

                return Edge(i, c) ? ((byte)70, (byte)74, (byte)80) : ((byte)108, (byte)114, (byte)122);
            },
            i =>
            {
                int c = At(i);
                if (c < 0) return 0;
                if (isWild[c] && Frontier(i, c)) return 3;
                if (isWild[c]) return 2;
                return Edge(i, c) ? 1 : 0;
            });
    }

    private static (byte R, byte G, byte B) Colour(TerrainClass terrain)
        => Io.DebugRender.TerrainColour(terrain);

    private static Image Downsample(int width, int height,
        Func<int, (byte R, byte G, byte B)> colour, Func<int, int>? rank = null)
    {
        int step = Math.Max(1, (width + MaxWidth - 1) / MaxWidth);
        int outWidth = width / step, outHeight = height / step;
        var rgb = new byte[outWidth * outHeight * 3];

        Parallel.For(0, outHeight, y =>
        {
            for (int x = 0; x < outWidth; x++)
            {
                int source = (y * step) * width + x * step;

                if (rank is not null && step > 1)
                {
                    int best = rank(source);
                    for (int by = 0; by < step; by++)
                    {
                        int row = (y * step + by) * width + x * step;
                        for (int bx = 0; bx < step; bx++)
                        {
                            int score = rank(row + bx);
                            if (score <= best) continue;
                            best = score;
                            source = row + bx;
                        }
                    }
                }

                var (r, g, b) = colour(source);
                int o = (y * outWidth + x) * 3;
                rgb[o] = r; rgb[o + 1] = g; rgb[o + 2] = b;
            }
        });

        return new Image(rgb, outWidth, outHeight);
    }
}