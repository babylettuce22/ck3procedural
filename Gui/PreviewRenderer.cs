using Ck3MapGen.Core;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Gui;

public static class PreviewRenderer
{
    public readonly record struct Image(byte[] Rgb, int Width, int Height);
    private const int MaxWidth = 2048;

    public static Image RenderTerrain(GenerationResult result)
    {
        var cfg = result.Config;
        int width = cfg.ProvinceWidth, height = cfg.ProvinceHeight;
        var terrain = result.Terrain.Terrain;
        return Downsample(width, height, i => Colour(terrain[i]));
    }

    public static Image RenderRelief(GenerationResult result)
    {
        var cfg = result.Config;
        int width = cfg.ProvinceWidth, height = cfg.ProvinceHeight;
        var elevation = result.ProvinceElevation;

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

    public static Image RenderClimate(GenerationResult result)
    {
        var cfg = result.Config;
        var climate = result.Terrain.Climate;
        return Downsample(cfg.ProvinceWidth, cfg.ProvinceHeight, i => Koppen.Colour(climate[i]));
    }

    public static Image RenderRivers(GenerationResult result)
    {
        var cfg = result.Config;
        var indices = Emit.MapDataWriter.RiverIndices(cfg, result.Provinces, result.Drainage);

        return Downsample(cfg.ProvinceWidth, cfg.ProvinceHeight,
            i => Emit.MapDataWriter.RiverColour(indices[i]),
            i => IsCourse(indices[i]) ? 2 + indices[i] : 0);

        static bool IsCourse(byte index)
            => index != Emit.MapDataWriter.RiverIndexLand
            && index != Emit.MapDataWriter.RiverIndexWater;
    }

    public static Image RenderDrainage(GenerationResult result)
    {
        var drainage = result.Drainage;
        var elevation = result.ProvinceElevation;
        var cfg = result.Config;

        return Downsample(drainage.Width, drainage.Height,
            i => drainage.Shade(elevation, cfg, i),
            drainage.ViewRank);
    }

    private const int NoCounty = -1, Impassable = -2, Water = -3;

    private static Image RenderTitles(GenerationResult result, string targetTier)
    {
        var map = result.Provinces;
        var order = result.ProvinceOrder;
        int width = map.Width, height = map.Height;
        int baronyCount = result.BaronyCount, landCount = result.LandCount;

        var targetTitles = Titles.Flatten(result.Titles).Where(t => t.Tier == targetTier).ToList();
        var targetIndexMap = new Dictionary<Title, int>();
        for (int i = 0; i < targetTitles.Count; i++) targetIndexMap[targetTitles[i]] = i;

        var titleIndexOf = new int[baronyCount + 1];
        Array.Fill(titleIndexOf, NoCounty);

        var baronies = Titles.Flatten(result.Titles).Where(t => t.Tier == "b");
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

    public static Image RenderCounties(GenerationResult result) => RenderTitles(result, "c");
    public static Image RenderDuchies(GenerationResult result) => RenderTitles(result, "d");
    public static Image RenderKingdoms(GenerationResult result) => RenderTitles(result, "k");
    public static Image RenderEmpires(GenerationResult result) => RenderTitles(result, "e");

    public static Image RenderProvinces(GenerationResult result)
    {
        var map = result.Provinces;
        var rng = new Rng(result.Config.Seed ^ 0x9E37);

        var colours = new (byte R, byte G, byte B)[map.Count];
        for (int i = 0; i < map.Count; i++)
        {
            var seed = map.Seeds[i];
            if (seed.IsLand)
            {
                colours[i] = ((byte)rng.Int(60, 235), (byte)rng.Int(90, 235), (byte)rng.Int(55, 190));
            }
            else if (seed.IsMajorRiver)
            {
                // Distinct cyan/aquamarine shades for major river provinces
                colours[i] = ((byte)rng.Int(0, 40), (byte)rng.Int(130, 200), (byte)rng.Int(200, 255));
            }
            else
            {
                // Deeper marine blue for open sea zones
                colours[i] = ((byte)rng.Int(20, 70), (byte)rng.Int(45, 105), (byte)rng.Int(90, 170));
            }
        }

        return Downsample(map.Width, map.Height, i => colours[map.Label[i]]);
    }

    public static Image RenderGovernment(GenerationResult result)
    {
        var map = result.Provinces;
        var order = result.ProvinceOrder;
        int width = map.Width, height = map.Height;
        int baronyCount = result.BaronyCount, landCount = result.LandCount;
        var cfg = result.Config;

        var counties = Titles.Flatten(result.Titles).Where(t => t.Tier == "c").ToList();
        var provinceTerrain = result.Terrain.Terrain;

        var development = MapGen.Development.ForCounties(counties, provinceTerrain, cfg, new Rng(cfg.Seed ^ 0x0DE7));
        var governments = MapGen.Governments.Build(counties, provinceTerrain, development, null, cfg, new Rng(cfg.Seed ^ 0x6017));

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
            return (x + 1 < width && At(i + 1) != county)
                || (y + 1 < height && At(i + width) != county);
        }

        var boundaryColor = ((byte)22, (byte)24, (byte)28);

        (byte, byte, byte) Colour(string g) => g switch
        {
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
        var provinceTerrain = result.Terrain.Terrain;

        var development = MapGen.Development.ForCounties(counties, provinceTerrain, cfg, new Rng(cfg.Seed ^ 0x0DE7));
        var wilderness = MapGen.Wilderness.Build(counties, map, order, landCount, provinceTerrain,
            development, cfg, new Rng(cfg.Seed ^ 0x1D17));

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
            return (x + 1 < width && At(i + 1) != county)
                || (y + 1 < height && At(i + width) != county);
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
                    return Frontier(i, c) ? ((byte)255, (byte)190, (byte)90)
                                          : ((byte)168, (byte)120, (byte)48);
                }

                return Edge(i, c) ? ((byte)70, (byte)74, (byte)80)
                                  : ((byte)108, (byte)114, (byte)122);
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