using Ck3MapGen.Core;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Gui;

/// <summary>
/// The preview views, all rendered off the finished province partition.
///
/// Everything here is rendered at province resolution and downsampled by whole pixels on the way
/// out. A vanilla-size province map is 42 million pixels and no screen is going to show it; point
/// sampling keeps class and border boundaries crisp, which is exactly what these views exist to
/// let you judge.
/// </summary>
public static class PreviewRenderer
{
    /// <summary>A rendered view: packed RGB, three bytes per pixel.</summary>
    public readonly record struct Image(byte[] Rgb, int Width, int Height);

    private const int MaxWidth = 2048;

    /// <summary>
    /// The per-pixel terrain classification, in the same colours the map reads as. This is the
    /// view for judging biome blending: it shows the class boundaries the texture writer then has
    /// to blend across.
    /// </summary>
    public static Image RenderTerrain(GenerationResult result)
    {
        var cfg = result.Config;
        int width = cfg.ProvinceWidth, height = cfg.ProvinceHeight;

        // The classification the generation already produced, not a fresh one. Building it here as
        // well meant every preview ran the climate model twice and showed the second answer, which
        // is also not quite the one the mod would ship.
        var terrain = result.Terrain.Terrain;

        return Downsample(width, height, i => Colour(terrain[i]));
    }

    /// <summary>
    /// Hillshaded elevation from the province raster.
    ///
    /// Built from <see cref="GenerationResult.ProvinceElevation"/>, which is the heightmap as it
    /// was read, so this shows the relief the mod will actually ship rather than an intermediate.
    /// </summary>
    public static Image RenderElevation(GenerationResult result)
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

            // Slope from the two neighbours that exist regardless of where we sampled.
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

    /// <summary>
    /// The climate the terrain was painted from, in Koppen-Geiger's own published colours.
    ///
    /// The only view here that can be checked against something outside this program: laid beside
    /// any published Koppen map it should agree feature for feature — deserts on the 30th parallel,
    /// oceanic west coasts, subarctic in the continental interior, tundra at the pole. The terrain
    /// view can look perfectly plausible while the temperatures and rainfall behind it are nonsense.
    /// </summary>
    public static Image RenderClimate(GenerationResult result)
    {
        var cfg = result.Config;
        var climate = result.Terrain.Climate;

        return Downsample(cfg.ProvinceWidth, cfg.ProvinceHeight, i => Koppen.Colour(climate[i]));
    }

    /// <summary>
    /// rivers.png exactly as it will be written — the same indices through the same palette.
    ///
    /// Which, since the hydrology was removed on 2026-08-10, is white land on magenta water and
    /// nothing else. Kept deliberately rather than removed with the rest: it is a view of a file
    /// the mod still ships and CK3 still requires, it costs one array, and it is where a rebuilt
    /// river system will first become visible.
    ///
    /// The rank argument to <see cref="Downsample"/> is what will matter then. A river is a
    /// one-pixel chain and point sampling breaks it up — at full map size the downsample keeps one
    /// pixel of every four to nine, so a course would come through dotted. Ranking course pixels
    /// above the background resolves each block to the course, and to its widest one, so a trunk
    /// is not lost to a tributary sharing the block.
    /// </summary>
    public static Image RenderRivers(GenerationResult result)
    {
        var cfg = result.Config;
        var indices = Emit.MapDataWriter.RiverIndices(cfg, result.Provinces);

        return Downsample(cfg.ProvinceWidth, cfg.ProvinceHeight,
            i => Emit.MapDataWriter.RiverColour(indices[i]),
            i => IsCourse(indices[i]) ? 2 + indices[i] : 0);

        static bool IsCourse(byte index)
            => index != Emit.MapDataWriter.RiverIndexLand
            && index != Emit.MapDataWriter.RiverIndexWater;
    }

    /// <summary>
    /// What a pixel is when it is not part of a county, sharing the county index's number line so
    /// one lookup answers both questions. Impassable land and sea have no barony and so can never
    /// belong to one; <see cref="NoCounty"/> is baronied land the hierarchy somehow left out, which
    /// is a bug rather than a state.
    /// </summary>
    private const int NoCounty = -1, Impassable = -2, Water = -3;

    /// <summary>
    /// Counties — the unions of baronies CK3 actually draws — in the colours the mod gives them,
    /// outlined.
    ///
    /// This is the view that answers "what will the map look like", and provinces.png is not: the
    /// game never renders a barony's outline, so what a player sees is this. A county is several
    /// provinces, so it cannot be read off the partition; it comes from the title hierarchy, which
    /// is why <see cref="GenerationResult.Titles"/> is built during generation rather than while
    /// writing the mod.
    ///
    /// Outlined because the colours are the ones landed_titles ships and those are random, so two
    /// neighbours can land close enough together to read as one county. The outline is drawn on the
    /// land side only, which leaves the sea flat and the coastline crisp.
    /// </summary>
    public static Image RenderCounties(GenerationResult result)
    {
        var map = result.Provinces;
        var order = result.ProvinceOrder;
        int width = map.Width, height = map.Height;
        int baronyCount = result.BaronyCount, landCount = result.LandCount;

        var counties = Titles.Flatten(result.Titles).Where(t => t.Tier == "c").ToList();

        // Province id -> county, which is the whole reason this view needs the title hierarchy.
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

        // Right and down only. Testing all four neighbours would draw the seam twice, once from
        // each side, and double its width for no extra information.
        bool Edge(int i, int county)
        {
            int x = i % width, y = i / width;
            return (x + 1 < width && At(i + 1) != county)
                || (y + 1 < height && At(i + width) != county);
        }

        return Downsample(width, height,
            i =>
            {
                int c = At(i);
                if (c == Water) return ((byte)38, (byte)62, (byte)96);
                if (c == Impassable) return ((byte)92, (byte)92, (byte)100);

                // Land with a barony but no county. Nothing should produce this, so it is painted
                // to be noticed rather than quietly blended into the sea.
                if (c == NoCounty) return ((byte)255, (byte)0, (byte)255);

                return Edge(i, c) ? ((byte)22, (byte)24, (byte)28) : counties[c].Color;
            },
            i =>
            {
                int c = At(i);
                return c >= 0 && Edge(i, c) ? 1 : 0;
            });
    }

    /// <summary>Province cells in randomised colours, land and sea tinted apart.</summary>
    public static Image RenderProvinces(GenerationResult result)
    {
        var map = result.Provinces;
        var rng = new Rng(result.Config.Seed ^ 0x9E37);

        var colours = new (byte R, byte G, byte B)[map.Count];
        for (int i = 0; i < map.Count; i++)
        {
            bool land = map.Seeds[i].IsLand;
            colours[i] = land
                ? ((byte)rng.Int(60, 235), (byte)rng.Int(90, 235), (byte)rng.Int(55, 190))
                : ((byte)rng.Int(20, 70), (byte)rng.Int(45, 105), (byte)rng.Int(90, 170));
        }

        return Downsample(map.Width, map.Height, i => colours[map.Label[i]]);
    }

    private static (byte R, byte G, byte B) Colour(TerrainClass terrain)
        => Io.DebugRender.TerrainColour(terrain);

    /// <summary>
    /// Point-samples the field down to something a screen can show.
    ///
    /// <paramref name="rank"/> makes a view keep features thinner than one output pixel. Without
    /// it a block resolves to its top-left pixel, which is right for the area views — a province or
    /// a climate zone is thousands of pixels and any one of them represents the block — and wrong
    /// for anything drawn as a line, which is a minority inside every block it passes through and
    /// so disappears. Where it is supplied, the block resolves to its highest-ranked pixel instead;
    /// ties keep the top-left one, so a rank that is flat is exactly the plain point sample.
    /// </summary>
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
