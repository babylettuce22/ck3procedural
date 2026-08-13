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
    /// Built from <see cref="GenerationResult.ProvinceElevation"/>. That is the field *after*
    /// <see cref="MapGen.HeightmapNormalizer"/> has run — normalisation happens at decode, so
    /// nothing downstream ever sees the source's own scale — but *before* the three passes between
    /// the elevation field and the file: the 16-bit conversion, the snap onto provinces.png and the
    /// coastline shaping. This is the shape of the terrain, not the bytes that ship.
    ///
    /// <see cref="RenderHeightmap"/> is the one that shows the file. The distinction is not
    /// academic: the seabed shelf and the shoreline snap are invisible here by construction, and
    /// they are precisely what a cliff at the coast is made of.
    /// </summary>
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
    /// heightmap.png itself: the greyscale, at the heightmap's own resolution, with nothing done to
    /// it.
    ///
    /// Every other view here interprets — hillshades, colours by class, outlines. This one refuses
    /// to, and that is its entire purpose. It is the only place the passes between the elevation
    /// field and the file are visible at all: <see cref="RenderRelief"/> is built from
    /// <see cref="GenerationResult.ProvinceElevation"/> and so shows the map *before* the scale
    /// conversion, the snap onto provinces.png and the seabed grade, none of which it can see and
    /// two of which exist to fix things that only appear at this stage.
    ///
    /// Rendered at <see cref="MapConfig.Width"/> rather than province resolution, because half the
    /// artefacts worth catching here are one pixel wide.
    ///
    /// **It will look very dark, and that is the reading.** Vanilla's own land sits at a median of
    /// 36/255 with 40% of the map at exactly 0, so a correct CK3 heightmap is a nearly black image
    /// with faint grey continents. A map that looks comfortably mid-grey is one whose land is too
    /// high, and a map whose land is a single flat tone a few steps off the water is the pancake
    /// <see cref="MapGen.HeightmapNormalizer"/> exists to open back up.
    ///
    /// Point-sampled like every other view, and here that is a deliberate choice rather than an
    /// inherited one. Averaging blocks would give a truer overall impression and destroy the one
    /// thing this view is best at: banding. Quantisation terracing is a pattern in the exact values,
    /// and a box filter smooths it into a gradient that looks fine — so the sampling that keeps
    /// real pixel values is the one that can still show the defect.
    /// </summary>
    public static Image RenderHeightmap(GenerationResult result)
    {
        var cfg = result.Config;

        var full = Emit.MapDataWriter.ShippedHeightmap(
            cfg, result.Provinces, result.ProvinceOrder, result.LandCount, result.Terra);

        return Downsample(cfg.Width, cfg.Height, i =>
        {
            // The 0-255 scale everything else in the tool quotes, so water reads as exactly 19 and
            // a normalised land ceiling as exactly 191. Dividing by 256 instead would be off by a
            // step at the top and make those numbers not quite match anything.
            var v = (byte)(full[i] / Emit.MapDataWriter.Step255);
            return (v, v, v);
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
    /// rivers.png exactly as it will be written — the same indices through the same palette. Not a
    /// second opinion on the file: a view of it.
    ///
    /// Which, with no course generator in the tool, is white land on magenta water and nothing
    /// else. Kept deliberately rather than removed with the rest: it is a view of a file the mod
    /// still ships and CK3 still requires, it costs one array, and it is where a rebuilt river
    /// system will first become visible.
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
    /// The drainage network rivers.png will be selected from: where the water goes, how much of it
    /// there is, and which hollows it stands in rather than leaving.
    ///
    /// Deliberately a view of the network and not of the courses. The two acceptance criteria for
    /// the rebuilt hydrology are that a course reaches an outlet and that the same map produces the
    /// same rivers at any resolution, and both are properties of this rather than of the raster
    /// drawn from it — a finished rivers.png shows only the cells that passed a gate, which is
    /// exactly the picture in which a course that dies inland looks the same as one that was never
    /// long enough to draw.
    /// </summary>
    public static Image RenderDrainage(GenerationResult result)
    {
        var drainage = result.Drainage;
        var elevation = result.ProvinceElevation;
        var cfg = result.Config;

        return Downsample(drainage.Width, drainage.Height,
            i => drainage.Shade(elevation, cfg, i),
            drainage.ViewRank);
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
    /// <summary>
    /// Generalized renderer for land titles of any tier ("c", "d", "k", "e"),
    /// mapping each province up to its ancestor of the target tier.
    /// </summary>
    private static Image RenderTitles(GenerationResult result, string targetTier)
    {
        var map = result.Provinces;
        var order = result.ProvinceOrder;
        int width = map.Width, height = map.Height;
        int baronyCount = result.BaronyCount, landCount = result.LandCount;

        // Flatten hierarchy and isolate titles matching the target tier
        var targetTitles = Titles.Flatten(result.Titles).Where(t => t.Tier == targetTier).ToList();

        // Map Title objects to their list index for rapid comparison
        var targetIndexMap = new Dictionary<Title, int>();
        for (int i = 0; i < targetTitles.Count; i++)
        {
            targetIndexMap[targetTitles[i]] = i;
        }

        // Map Province ID -> Index of the parent title in targetTitles
        var titleIndexOf = new int[baronyCount + 1];
        Array.Fill(titleIndexOf, NoCounty);

        // Map each barony province up to its target ancestor
        var baronies = Titles.Flatten(result.Titles).Where(t => t.Tier == "b");
        foreach (var b in baronies)
        {
            if (b.ProvinceId >= 1 && b.ProvinceId <= baronyCount)
            {
                Title? ancestor = b;
                while (ancestor != null && ancestor.Tier != targetTier)
                {
                    ancestor = ancestor.Parent;
                }

                if (ancestor != null && targetIndexMap.TryGetValue(ancestor, out int index))
                {
                    titleIndexOf[b.ProvinceId] = index;
                }
            }
        }

        int At(int i)
        {
            int id = order[map.Label[i]];
            return id <= baronyCount ? titleIndexOf[id] : id <= landCount ? Impassable : Water;
        }

        // Right and down pixel check for borders
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

                // Land that is assigned a barony but has no parent of the target tier
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

    public static Image RenderCounties(GenerationResult result)
    => RenderTitles(result, "c");

    public static Image RenderDuchies(GenerationResult result)
        => RenderTitles(result, "d");

    public static Image RenderKingdoms(GenerationResult result)
        => RenderTitles(result, "k");

    public static Image RenderEmpires(GenerationResult result)
        => RenderTitles(result, "e");

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


    /// <summary>
    /// Renders the start-date government distribution — feudal, tribal, clan, republic and
    /// theocracy — from development, county terrain and the heritage each county belongs to.
    /// </summary>
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

        // The same rule the mod writer uses, not a copy of it. Cultures do not exist at preview
        // time — they are built while writing the mod — so the pastoralist clause cannot fire, clan
        // falls back to each county's own ground rather than its heritage's, and no theocracy is
        // shown at all. See MapGen.Governments.Build.
        var governments = MapGen.Governments.Build(counties, provinceTerrain, development, null,
            cfg, new Rng(cfg.Seed ^ 0x6017));

        var government = new string[counties.Count];
        for (int c = 0; c < counties.Count; c++) government[c] = governments.For(counties[c]);

        // Map Province ID -> County Index
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

        // Aesthetic colors for the visual preview, near enough to CK3's own government colours to
        // read the same way once the mod is loaded.
        var boundaryColor = ((byte)22, (byte)24, (byte)28);  // Clean dark outlines

        (byte, byte, byte) Colour(string g) => g switch
        {
            GovernmentMap.Tribal => ((byte)185, (byte)95, (byte)60),    // Terracotta
            GovernmentMap.Clan => ((byte)80, (byte)150, (byte)95),      // Green
            GovernmentMap.Republic => ((byte)200, (byte)70, (byte)70),  // Red
            GovernmentMap.Theocracy => ((byte)205, (byte)205, (byte)200), // Bone
            _ => ((byte)65, (byte)110, (byte)160),                      // Slate blue, feudal
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


    /// <summary>
    /// Which counties nobody lives in, against the settled map behind them.
    ///
    /// The view exists because the wilderness stage is otherwise invisible: it writes no history
    /// yet and reports only a count, so there is no way to judge whether a share of 12% has landed
    /// as a frontier along the northern rim or as confetti across the whole map — and those two
    /// results have the same number in the log.
    ///
    /// Settled counties are drawn washed-out on purpose. The question this answers is where the
    /// empty ground is and what shape it makes, so the settled map is context rather than content.
    /// </summary>
    public static Image RenderWilderness(GenerationResult result)
    {
        var map = result.Provinces;
        var order = result.ProvinceOrder;
        int width = map.Width, height = map.Height;
        int baronyCount = result.BaronyCount, landCount = result.LandCount;
        var cfg = result.Config;

        var counties = Titles.Flatten(result.Titles).Where(t => t.Tier == "c").ToList();
        var provinceTerrain = result.Terrain.Terrain;

        // Recomputed here rather than carried on the result, which is how RenderGovernment does it
        // too. The seeds must match ContentWriter's exactly or the preview shows a different world
        // from the one the mod ships — see the calls there.
        var development = MapGen.Development.ForCounties(counties, provinceTerrain, cfg,
            new Rng(cfg.Seed ^ 0x0DE7));

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

        // A wilderness county borders settled land: the frontier itself, and the only line worth
        // picking out, since it is where colonisation can actually happen.
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

                    // Amber against the muted settled ground, and brighter along the frontier edge.
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

                // Rank so the thin things survive downsampling: the frontier line first, then any
                // wilderness at all, then ordinary borders. Without this a one-county clump can
                // vanish entirely at preview scale, which is the exact case worth seeing.
                if (isWild[c] && Frontier(i, c)) return 3;
                if (isWild[c]) return 2;
                return Edge(i, c) ? 1 : 0;
            });
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
