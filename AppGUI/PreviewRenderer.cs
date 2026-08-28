using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.AppGUI;

public static class PreviewRenderer
{
    private static readonly Dictionary<RaceArchetype, DdsReader.DecodedImage?> IconCache = [];
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

    /// <summary>
    /// The de facto political map: every county in the colour of the realm that ultimately holds
    /// it, wilderness aside.
    ///
    /// Both the walk to the top of a realm and the colour it arrives at used to be worked out here.
    /// The walk is <see cref="RealmGraph.PathFromTop"/> now — it was always the same derivation,
    /// and having it twice is what the graph exists to stop — and the colour is
    /// <see cref="RealmPalette"/>, for reasons that class states.
    /// </summary>
    public static Image RenderRealms(GenerationResult result, RealmGraph? graph, MapGen.WildernessMap? wilderness)
    {
        if (graph is null) return RenderTitles(result, "c");

        var counties = Titles.Flatten(result.Titles).Where(t => t.Tier == "c").ToList();
        var palette = new RealmPalette(graph, counties);

        return RenderByCounty(result, wilderness,
            county => palette.Colour(graph.PathFromTop(graph.SeatOfCounty(county))[0]));
    }

    /// <summary>
    /// The realms view focused on one ruler: their realm painted by its structure, the rest of the
    /// world receding to grey.
    ///
    /// Inside the focused realm, each direct vassal's subtree wears that vassal's own colour — the
    /// next level of the hierarchy down, exactly what a click on the unfocused view was showing one
    /// level up. The ruler's personal demesne is their own colour lifted toward white, so "held
    /// directly" and "held through a vassal" separate at a glance. Everything outside keeps a ghost
    /// of its realm colour rather than flat grey, because the question a focus answers is "what is
    /// this realm made of", and answering it while erasing where the realm *sits* would throw away
    /// the context that makes the answer readable.
    ///
    /// The vassal colours come from <see cref="RealmPalette"/> rather than from primary titles for
    /// the reason that class gives, and it bites hardest here: a ruler's vassals are mostly titled
    /// by de jure children of his own title, so their title colours were shades of his and of each
    /// other's — the one comparison this frame exists to make was the one the palette was worst at.
    /// </summary>
    public static Image RenderRealmsFocused(GenerationResult result, RealmGraph graph,
        MapGen.WildernessMap? wilderness, Title focusSeat)
    {
        var counties = Titles.Flatten(result.Titles).Where(t => t.Tier == "c").ToList();
        var palette = new RealmPalette(graph, counties);
        var focusColour = palette.Colour(focusSeat);

        // Wilderness recedes with the rest of the unfocused world — at its usual orange it would
        // be the loudest thing on a frame whose whole point is that one realm is loudest.
        return RenderByCounty(result, wilderness, wildColour: Dim(168, 120, 48), colourOf: county =>
        {
            var seat = graph.SeatOfCounty(county);
            var path = graph.PathFromTop(seat);

            int at = -1;
            for (int i = 0; i < path.Count; i++)
            {
                if (path[i] == focusSeat) { at = i; break; }
            }

            if (at < 0)
            {
                // Outside the focused realm: its own realm colour, three quarters of the way to grey.
                var (r, g, b) = palette.Colour(path[0]);
                return Dim(r, g, b);
            }

            if (at == path.Count - 1)
            {
                // The focused ruler's own demesne.
                return Lift(focusColour.R, focusColour.G, focusColour.B);
            }

            return palette.Colour(path[at + 1]);
        });

        static (byte, byte, byte) Dim(byte r, byte g, byte b)
        {
            byte grey = 84;
            return ((byte)(grey + (r - grey) / 4), (byte)(grey + (g - grey) / 4),
                    (byte)(grey + (b - grey) / 4));
        }

        static (byte, byte, byte) Lift(byte r, byte g, byte b)
            => ((byte)(r + (255 - r) / 3), (byte)(g + (255 - g) / 3), (byte)(b + (255 - b) / 3));
    }

    private static Image RenderByCounty(GenerationResult result, MapGen.WildernessMap? wilderness,
        Func<Title, (byte R, byte G, byte B)?> colourOf,
        (byte R, byte G, byte B)? wildColour = null)
    {
        var wildTint = wildColour ?? ((byte)168, (byte)120, (byte)48);

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
                return wild[c] ? wildTint : colour[c];
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

    /// <summary>
    /// The wilderness and the governments a write would produce, worked out for a world that has
    /// not been written yet.
    ///
    /// Every mode that can read <see cref="Emit.WrittenContent"/> does, and both of these do too
    /// once a write exists. This runs only for the estimate the strip offers before then, which is
    /// what <see cref="MapMode.Estimate"/> labels it as.
    ///
    /// It mirrors <see cref="Emit.ContentWriter"/>'s order and seeds rather than approximating
    /// them, arguments included. That is the whole point: an earlier version of this dropped four
    /// of those arguments, and each one changed the answer. Without <c>provinces/order/baronyCount</c>
    /// <see cref="MapGen.Realms.Build"/> has no county adjacency and drops its contiguity
    /// constraint; without <c>azgaar</c> an imported world's wilderness comes from terrain scoring
    /// instead of from the ground the export left unclaimed, and its governments from our own
    /// reasoning instead of from the forms the export drew — not approximations of the written
    /// answers but different answers.
    ///
    /// The wilderness half comes out exact either way: everything it reads — the terrain vote, the
    /// first development pass, the import — exists as soon as the world is generated.
    ///
    /// On an imported world the governments half is exact too, because the export's forms decide
    /// them and <c>StateTitles</c> is populated by the hierarchy build, well before any write.
    ///
    /// On a generated one it cannot be. Cultures and world centers are built inside
    /// <see cref="Emit.ContentWriter.WriteAll"/>, so before one has run the development here is
    /// missing <see cref="MapConfig.WorldCenterDevBoost"/> and the governments are missing their
    /// heritage and wonder inputs. What that costs is almost entirely administrative realms:
    /// without the boost a realm's average development stays under the <c>avgDev &gt;= 11</c> gate
    /// in <see cref="MapGen.Governments.Build"/>, and with no world centers there is no imperial
    /// wonder to waive it, so the counties that would have been administrative fall through to
    /// clan and feudal.
    ///
    /// One further gap, and it is a difference in kind rather than in precision. With
    /// <see cref="MapConfig.SimulateFormation"/> on, the written realms are grown by
    /// <see cref="MapGen.Formation"/> from a culture map that does not exist yet here — so before a
    /// write, this estimate necessarily falls back to the de jure allocation and its realms are not
    /// a rough version of the written ones, they are the other algorithm's answer. Only the
    /// government render reads it, and only until a world has been written; the Realms map mode
    /// takes the written realms directly and is unaffected.
    ///
    /// Measured against the written mod, counties disagreeing, four-argument version → this one:
    /// Fleunland import, 184 counties — wilderness 47 → 0, government 121 → 0, both renders
    /// byte-identical to the written-backed ones. Generated, seed 4242 (111 counties) government
    /// 81 → 45 and seed 991 (106 counties) 86 → 40, with every written administrative county
    /// inside the residue both times. Hence still an estimate on a generated world, and hence the
    /// label the strip puts on it.
    /// </summary>
    private static (List<Title> Counties, TerrainClass[] ProvinceTerrain,
                    Dictionary<Title, int> Development, WildernessMap Wilderness)
        EstimateWilderness(GenerationResult result)
    {
        var cfg = result.Config;
        var counties = Titles.Flatten(result.Titles).Where(t => t.Tier == "c").ToList();

        var provinceTerrain = Emit.ContentWriter.ProvinceTerrain(
            cfg, result.Provinces, result.ProvinceOrder, result.Terrain.Terrain, result.LandCount);

        var development = MapGen.Development.ForCounties(
            counties, provinceTerrain, cfg, new Rng(cfg.Seed ^ 0x0DE7));

        var wilderness = MapGen.Wilderness.Build(counties, result.Provinces, result.ProvinceOrder,
            result.LandCount, provinceTerrain, development, cfg, new Rng(cfg.Seed ^ 0x1D17),
            result.Azgaar);

        return (counties, provinceTerrain, development, wilderness);
    }

    /// <inheritdoc cref="EstimateWilderness"/>
    private static GovernmentMap EstimateGovernments(GenerationResult result,
        MapGen.CultureMap? cultures, MapGen.WorldCenterMap? worldCenters)
    {
        var cfg = result.Config;
        var azgaar = result.Azgaar;
        var empires = result.Titles;
        var (counties, provinceTerrain, development, wilderness) = EstimateWilderness(result);

        // A second development pass, as ContentWriter runs one: wilderness above is decided before
        // the world centers exist, and only realms and governments see their boost. Folding the
        // boost into the first pass instead would change which counties come out wild.
        development = MapGen.Development.ForCounties(
            counties, provinceTerrain, cfg, new Rng(cfg.Seed ^ 0x0DE7), worldCenters);

        var realms = MapGen.Realms.Build(empires, development, wilderness, cfg,
            new Rng(cfg.Seed ^ 0x2E17), result.Provinces, result.ProvinceOrder,
            result.BaronyCount, azgaar, cultures);

        var stateGovernments = azgaar is null ? null : MapGen.AzgaarGovernments.ByState(azgaar, cfg);

        return MapGen.Governments.Build(empires, counties, realms, provinceTerrain,
            development, cultures, worldCenters, cfg, new Rng(cfg.Seed ^ 0x6017),
            azgaar, stateGovernments);
    }

    public static Image RenderGovernment(GenerationResult result, Emit.WrittenContent? written)
        => RenderGovernment(result, written?.Governments
            ?? EstimateGovernments(result, written?.Cultures, written?.WorldCenters));

    private static Image RenderGovernment(GenerationResult result, GovernmentMap governments)
    {
        var map = result.Provinces;
        var order = result.ProvinceOrder;
        int width = map.Width, height = map.Height;
        int baronyCount = result.BaronyCount, landCount = result.LandCount;
        var counties = Titles.Flatten(result.Titles).Where(t => t.Tier == "c").ToList();

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

        return Downsample(width, height,
            i =>
            {
                int c = At(i);
                if (c == Water) return ((byte)38, (byte)62, (byte)96);
                if (c == Impassable) return ((byte)92, (byte)92, (byte)100);
                if (c == NoCounty) return ((byte)255, (byte)0, (byte)255);
                return Edge(i, c) ? boundaryColor : GovernmentColour(government[c]);
            },
            i =>
            {
                int c = At(i);
                return c >= 0 && Edge(i, c) ? 1 : 0;
            });
    }

    public static Image RenderWilderness(GenerationResult result, Emit.WrittenContent? written)
        => RenderWilderness(result, written?.Wilderness ?? EstimateWilderness(result).Wilderness);

    private static Image RenderWilderness(GenerationResult result, WildernessMap wilderness)
    {
        var map = result.Provinces;
        var order = result.ProvinceOrder;
        int width = map.Width, height = map.Height;
        int baronyCount = result.BaronyCount, landCount = result.LandCount;
        var counties = Titles.Flatten(result.Titles).Where(t => t.Tier == "c").ToList();

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

    /// <summary>Shared with the legend, so the key can never drift from the paint.</summary>
    /// <summary>
    /// Counties by the development level the mod ships them at, with a badge on every world center.
    ///
    /// Reads <see cref="Emit.WrittenContent.Development"/> rather than recomputing it. The level a
    /// county actually gets comes from the development pass that sees the world centers, and those
    /// do not exist until the write has built the cultures they are placed from — which is also why
    /// the badges belong here rather than on a mode that can be drawn earlier.
    /// </summary>
    public static Image RenderDevelopment(GenerationResult result, Emit.WrittenContent? written)
    {
        if (written is null) return RenderTitles(result, "c");

        var image = RenderByCounty(result, written.Wilderness,
            county => DevelopmentColour(written.Development.GetValueOrDefault(county)));

        DrawWorldCenters(image, result, written.WorldCenters);
        return image;
    }

    /// <summary>
    /// Counties by what they earn their holder each month, with a badge on every world center.
    ///
    /// CK3 has no wealth map mode to copy; its nearest is <c>economy_buildings</c>, which colours
    /// by a holding's income in gold per month. So this is that number, computed by
    /// <see cref="Economy"/> from the holdings the province history actually wrote and the
    /// development it actually assigned — not a score invented to look like money.
    ///
    /// Nomadic and unsettled counties come out at zero, which is correct rather than missing: a
    /// horde's purse comes from its herds and its camp declares no <c>monthly_income</c> at all.
    /// </summary>
    public static Image RenderWealth(GenerationResult result, Emit.WrittenContent? written)
    {
        if (written is null) return RenderTitles(result, "c");

        var image = RenderByCounty(result, written.Wilderness, county => WealthColour(
            (float)Economy.CountyIncome(county, written.Holdings,
                written.Development.GetValueOrDefault(county))));

        DrawWorldCenters(image, result, written.WorldCenters);
        return image;
    }

    /// <summary>Barony province id to the seed point it grew from, for placing a badge on a county.</summary>
    private static int[] SeedOfProvince(GenerationResult result)
    {
        var order = result.ProvinceOrder;
        var seedOfProvince = new int[result.LandCount + 1];

        for (int label = 0; label < order.Length; label++)
        {
            int id = order[label];
            if (id >= 1 && id <= result.LandCount) seedOfProvince[id] = label;
        }

        return seedOfProvince;
    }

    /// <summary>
    /// A wonder badge on each world center, seated on its capital barony — the province that
    /// carries the <c>special_building</c> line, so the badge sits where the wonder is.
    /// </summary>
    private static void DrawWorldCenters(Image image, GenerationResult result, WorldCenterMap centers)
    {
        int step = StepFor(result.Provinces.Width);
        var seedOf = SeedOfProvince(result);
        var map = result.Provinces;

        foreach (var centre in centers.Centers)
        {
            int provinceId = centre.CapitalBarony.ProvinceId;
            if (provinceId < 1 || provinceId >= seedOf.Length) continue;

            int seedIndex = seedOf[provinceId];
            if (seedIndex < 0 || seedIndex >= map.Seeds.Count) continue;

            var seed = map.Seeds[seedIndex];
            DrawWonderBadge(image, seed.X / step, seed.Y / step, centre.Wonder.Archetype);
        }
    }

    /// <summary>
    /// County development, unlit ground through to a bright capital.
    ///
    /// The stops are placed against what the generator actually produces rather than against CK3's
    /// 0..100 ceiling: <see cref="MapConfig.DevelopmentBase"/> and its spread put a generated world
    /// in the low tens, so a ramp stretched to 100 would paint the whole map the same dark colour.
    /// </summary>
    public static (byte R, byte G, byte B) DevelopmentColour(float level)
        => Ramp(level,
        [
            (0, (46, 42, 58)),
            (8, (82, 68, 92)),
            (15, (132, 100, 92)),
            (22, (190, 152, 80)),
            (30, (240, 214, 128)),
            (40, (255, 248, 214)),
        ]);

    /// <summary>
    /// A county's monthly gold, dark through to bright.
    ///
    /// The stops are placed against the range the generator can actually reach, not against a round
    /// number. <see cref="MapGen.Development.Holding"/> gives a county at most two holdings, so the
    /// richest possible is a city beside a church — 0.80 + 0.75 — lifted by development, and the
    /// top stop sits just above that. Measured across three seeds the median county earns about
    /// 0.42 and the best about 1.5, which is where the middle of this ramp is aimed; an earlier
    /// version topped out at 2.2 and left the whole upper third of the ramp unused.
    /// </summary>
    public static (byte R, byte G, byte B) WealthColour(float goldPerMonth)
        => Ramp(goldPerMonth,
        [
            (0.00, (48, 46, 44)),
            (0.25, (92, 72, 50)),
            (0.55, (146, 108, 54)),
            (0.90, (202, 158, 66)),
            (1.30, (238, 202, 104)),
            (1.85, (255, 242, 198)),
        ]);

    public static (byte R, byte G, byte B) GovernmentColour(string government) => government switch
    {
        GovernmentMap.Administrative => (155, 60, 160),
        GovernmentMap.Nomad => (210, 160, 65),
        GovernmentMap.Tribal => (185, 95, 60),
        GovernmentMap.Clan => (80, 150, 95),
        GovernmentMap.Republic => (200, 70, 70),
        GovernmentMap.Theocracy => (205, 205, 200),
        _ => (65, 110, 160),
    };

    private static readonly (byte R, byte G, byte B) SeaBlue = (38, 62, 96);

    /// <summary>
    /// Mean annual temperature on a cold-to-hot ramp. Piecewise-linear through hand-placed stops
    /// rather than one hue sweep, so the freezing point lands exactly on the blue-to-green break —
    /// the one temperature a reader actually looks for.
    /// </summary>
    public static (byte R, byte G, byte B) TemperatureColour(float meanC)
        => Ramp(meanC,
        [
            (-25, (70, 50, 160)),
            (-10, (60, 100, 200)),
            (0, (100, 180, 220)),
            (10, (120, 190, 120)),
            (20, (230, 200, 90)),
            (30, (220, 110, 60)),
            (38, (170, 40, 40)),
        ]);

    /// <summary>Annual rainfall, parched tan through green to drowned blue.</summary>
    public static (byte R, byte G, byte B) RainfallColour(float annualMm)
        => Ramp(annualMm,
        [
            (0, (210, 180, 120)),
            (250, (190, 190, 110)),
            (500, (140, 180, 100)),
            (1000, (70, 160, 110)),
            (1600, (50, 130, 170)),
            (2400, (30, 80, 160)),
        ]);

    /// <summary>The 0..1 habitability field, barren red-brown to rich green.</summary>
    public static (byte R, byte G, byte B) HabitabilityColour(float t)
        => Ramp(t,
        [
            (0.0, (150, 60, 50)),
            (0.25, (190, 140, 80)),
            (0.5, (200, 190, 100)),
            (0.75, (130, 180, 90)),
            (1.0, (60, 150, 70)),
        ]);

    private static (byte R, byte G, byte B) Ramp(double value,
        (double At, (int R, int G, int B) Colour)[] stops)
    {
        if (value <= stops[0].At) return Byte(stops[0].Colour);
        for (int i = 1; i < stops.Length; i++)
        {
            if (value > stops[i].At) continue;

            var (a, from) = stops[i - 1];
            var (b, to) = stops[i];
            double t = (value - a) / (b - a);

            return ((byte)(from.R + (to.R - from.R) * t),
                    (byte)(from.G + (to.G - from.G) * t),
                    (byte)(from.B + (to.B - from.B) * t));
        }
        return Byte(stops[^1].Colour);

        static (byte, byte, byte) Byte((int R, int G, int B) c) => ((byte)c.R, (byte)c.G, (byte)c.B);
    }

    public static Image RenderTemperature(GenerationResult r)
        => RenderField(r, i => TemperatureColour(r.Terrain.Field.MeanC[i]));

    public static Image RenderRainfall(GenerationResult r)
        => RenderField(r, i => RainfallColour(r.Terrain.Field.AnnualMm[i]));

    /// <summary>
    /// The habitability field the province sizer weighs — rebuilt on demand from inputs the result
    /// already carries, exactly as <see cref="ProvinceSize"/> built it, rather than stored: it is
    /// asked for once per build at most and the arrays are 40MB a copy.
    /// </summary>
    public static Image RenderHabitability(GenerationResult r)
    {
        var field = Habitability.Build(r.LandMask, r.ProvinceElevation, r.Terrain.Field,
            r.Config.ProvinceWidth, r.Config.ProvinceHeight, r.Config);
        return RenderField(r, i => HabitabilityColour(field[i]));
    }

    /// <summary>A per-cell scalar over land, with the sea flattened to one colour.</summary>
    private static Image RenderField(GenerationResult r, Func<int, (byte R, byte G, byte B)> colour)
        => Downsample(r.Config.ProvinceWidth, r.Config.ProvinceHeight,
            i => r.LandMask[i] == 1 ? colour(i) : SeaBlue);

    /// <summary>
    /// Counties by the ethnicity their culture wears. Ethnicities carry no colour of their own —
    /// they are portrait DNA, not map paint — so each gets a stable hue from its position on the
    /// golden-angle wheel, which keeps neighbours apart for any count of ethnicities.
    /// </summary>
    /// <summary>
    /// Counties by the ethnicity their culture wears. Ethnicities carry no colour of their own —
    /// they are portrait DNA, not map paint — so each gets a stable hue from its position on the
    /// golden-angle wheel, which keeps neighbours apart for any count of ethnicities.
    /// </summary>
    public static Image RenderEthnicities(GenerationResult result, Emit.WrittenContent written)
    {
        var keys = written.Ethnicities.Ethnicities.Keys.Order(StringComparer.Ordinal).ToList();
        var hueOf = new Dictionary<string, (byte R, byte G, byte B)>();
        for (int i = 0; i < keys.Count; i++)
            hueOf[keys[i]] = HueColour(i * 137.508, 0.55, 0.82);

        // 1. Render standard base county map
        var image = RenderByCounty(result, written.Wilderness, county =>
            written.Cultures.ByCounty.TryGetValue(county, out var culture)
                ? hueOf.GetValueOrDefault(written.Ethnicities.For(culture).Key)
                : null);

        int step = StepFor(result.Provinces.Width);
        var centroids = CalculateEthnicityCentroids(result, written);

        // 2. Overlay icons onto the downsampled image. Minorities draw smaller — a race seated
        // inside a human culture by the ratio budget is real but holds no land of its own, and the
        // badge size is what tells the two apart at a glance.
        foreach (var (archetype, (cx, cy), minority) in centroids)
        {
            var icon = GetPhenotypeIcon(archetype);
            if (icon is null) continue;

            // Center coordinates in downsampled preview buffer
            int targetX = cx / step;
            int targetY = cy / step;

            DrawIconBadge(image, targetX, targetY, icon.Value, minority ? 20 : 28);
        }

        return image;
    }

    private static List<(RaceArchetype Archetype, (int X, int Y) Position, bool Minority)> CalculateEthnicityCentroids(
        GenerationResult result, Emit.WrittenContent written)
    {
        var map = result.Provinces;
        var order = result.ProvinceOrder;
        int landCount = result.LandCount;

        // Invert ProvinceOrder: map from province ID -> raw seed index in map.Seeds
        var seedOfProvince = new int[landCount + 1];
        for (int label = 0; label < order.Length; label++)
        {
            int id = order[label];
            if (id >= 1 && id <= landCount)
                seedOfProvince[id] = label;
        }

        var centroids = new List<(RaceArchetype Archetype, (int X, int Y) Position, bool Minority)>();

        // The barony seed closest to a culture's geometric centre, or null for a culture with no
        // drawable ground. Shared by the majority badges and the minority ones below.
        (int X, int Y)? CentroidOf(MapGen.Culture culture)
        {
            var points = new List<(int X, int Y)>();

            foreach (var county in culture.Counties)
            {
                if (written.Wilderness?.Contains(county) == true) continue;

                foreach (var barony in county.Children)
                {
                    int provId = barony.ProvinceId;
                    if (provId < 1 || provId > landCount) continue;

                    int seedIdx = seedOfProvince[provId];
                    if (seedIdx >= 0 && seedIdx < map.Seeds.Count)
                    {
                        var seed = map.Seeds[seedIdx];
                        points.Add((seed.X, seed.Y));
                    }
                }
            }

            if (points.Count == 0) return null;

            long sumX = 0, sumY = 0;
            foreach (var p in points) { sumX += p.X; sumY += p.Y; }
            int avgX = (int)(sumX / points.Count);
            int avgY = (int)(sumY / points.Count);

            return points.MinBy(p => {
                long dx = p.X - avgX, dy = p.Y - avgY;
                return dx * dx + dy * dy;
            });
        }

        // Calculate centroid per culture so separate enclaves/cultures of a race get their own badge
        foreach (var culture in written.Cultures.Cultures)
        {
            var eth = written.Ethnicities.For(culture);
            if (eth.Archetype == RaceArchetype.Human) continue;

            if (CentroidOf(culture) is { } pos)
                centroids.Add((eth.Archetype, pos, false));
        }

        // The minorities. These races exist — the ratio budget seated them inside a human culture
        // instead of giving them land — and leaving them unbadged is exactly how "the map only
        // made five races" gets misread off a preview that is actually showing all eight. Drawn at
        // the host culture's centre; a host carrying several gets them fanned out sideways so the
        // badges do not stack into one.
        var perHost = new Dictionary<MapGen.Culture, int>();
        foreach (var (race, host) in written.Ethnicities.MinorityPlacements)
        {
            if (CentroidOf(host) is not { } pos) continue;

            int already = perHost.GetValueOrDefault(host);
            perHost[host] = already + 1;

            centroids.Add((race, (pos.X + already * 24 * StepFor(result.Provinces.Width), pos.Y), true));
        }

        return centroids;
    }

    /// <summary>
    /// The dark disc every badge sits on. Split out of <see cref="DrawIconBadge"/> so the wonder
    /// glyphs below get the same seat without a second copy of it — a badge that reads differently
    /// between two map modes would look like it meant something different.
    /// </summary>
    private static void DrawBadgeCircle(Image dest, int cx, int cy, int targetSize)
    {
        int radius = targetSize / 2 + 3;
        int rSq = radius * radius;

        // A pale rim around the dark disc. Without it the badge is (20,22,26) against a county that
        // can be (46,42,58) at the bottom of the development ramp, and it disappears into exactly
        // the ground it is supposed to be marking.
        int rimSq = (radius - 2) * (radius - 2);

        for (int dy = -radius; dy <= radius; dy++)
        {
            int py = cy + dy;
            if (py < 0 || py >= dest.Height) continue;

            for (int dx = -radius; dx <= radius; dx++)
            {
                int px = cx + dx;
                if (px < 0 || px >= dest.Width) continue;

                int distSq = dx * dx + dy * dy;
                if (distSq > rSq) continue;

                int o = (py * dest.Width + px) * 3;
                bool rim = distSq > rimSq;
                float alpha = rim ? 0.85f : 0.78f;
                var (r, g, b) = rim ? (206, 200, 186) : (20, 22, 26);

                dest.Rgb[o + 0] = (byte)(dest.Rgb[o + 0] * (1 - alpha) + r * alpha);
                dest.Rgb[o + 1] = (byte)(dest.Rgb[o + 1] * (1 - alpha) + g * alpha);
                dest.Rgb[o + 2] = (byte)(dest.Rgb[o + 2] * (1 - alpha) + b * alpha);
            }
        }
    }

    private static void DrawIconBadge(Image dest, int cx, int cy, DdsReader.DecodedImage icon, int targetSize = 28)
    {
        // targetSize: icon display width/height in preview pixels; minorities draw at 20.
        DrawBadgeCircle(dest, cx, cy, targetSize);

        // 2. Alpha-blend the icon on top
        int half = targetSize / 2;
        for (int y = 0; y < targetSize; y++)
        {
            int py = cy - half + y;
            if (py < 0 || py >= dest.Height) continue;

            int srcY = y * icon.Height / targetSize;

            for (int x = 0; x < targetSize; x++)
            {
                int px = cx - half + x;
                if (px < 0 || px >= dest.Width) continue;

                int srcX = x * icon.Width / targetSize;
                int srcOffset = (srcY * icon.Width + srcX) * 4;

                byte b = icon.Bgra[srcOffset + 0];
                byte g = icon.Bgra[srcOffset + 1];
                byte r = icon.Bgra[srcOffset + 2];
                float a = icon.Bgra[srcOffset + 3] / 255.0f;

                if (a > 0.05f)
                {
                    int dstOffset = (py * dest.Width + px) * 3;
                    dest.Rgb[dstOffset + 0] = (byte)(dest.Rgb[dstOffset + 0] * (1 - a) + r * a);
                    dest.Rgb[dstOffset + 1] = (byte)(dest.Rgb[dstOffset + 1] * (1 - a) + g * a);
                    dest.Rgb[dstOffset + 2] = (byte)(dest.Rgb[dstOffset + 2] * (1 - a) + b * a);
                }
            }
        }
    }

    /// <summary>
    /// A world center's badge: the same dark disc the race icons sit on, with a glyph for its
    /// wonder drawn straight into the buffer.
    ///
    /// Drawn rather than loaded on purpose. The race badges read a trait .dds out of the game's
    /// interface icons, which works because a phenotype trait happens to have an icon vanilla
    /// shipped; a generated wonder has no such file, and inventing an asset to preview an asset the
    /// map does not have would be worse than five shapes in code. The shapes are analytic — each
    /// archetype is a predicate over normalised coordinates — so they scale to any badge size and
    /// need no rasteriser.
    /// </summary>
    private static void DrawWonderBadge(Image dest, int cx, int cy, WonderArchetype archetype,
        int targetSize = 24)
    {
        DrawBadgeCircle(dest, cx, cy, targetSize);

        int half = targetSize / 2;
        for (int y = 0; y < targetSize; y++)
        {
            int py = cy - half + y;
            if (py < 0 || py >= dest.Height) continue;

            // Sampled at pixel centres, so an odd size stays symmetric about the badge's middle.
            double v = (y + 0.5) / half - 1.0;

            for (int x = 0; x < targetSize; x++)
            {
                int px = cx - half + x;
                if (px < 0 || px >= dest.Width) continue;

                double u = (x + 0.5) / half - 1.0;
                if (!InWonderGlyph(archetype, u, v)) continue;

                int o = (py * dest.Width + px) * 3;
                dest.Rgb[o + 0] = 240;
                dest.Rgb[o + 1] = 236;
                dest.Rgb[o + 2] = 222;
            }
        }
    }

    /// <summary>
    /// Whether a point is inside an archetype's glyph, in coordinates running −1..1 across the
    /// badge with <paramref name="v"/> pointing down.
    ///
    /// Kept to shapes that survive being 20-odd pixels wide: no feature is thinner than about a
    /// tenth of the badge, because below that the glyphs stop being distinguishable from each other
    /// and a map full of identical smudges is worse than no badges at all.
    /// </summary>
    private static bool InWonderGlyph(WonderArchetype archetype, double u, double v) => archetype switch
    {
        // A domed hall: finial, dome, then a plinth wider than the dome so the two read as
        // separate parts of a building rather than as one bell-shaped blob.
        WonderArchetype.Sanctuary =>
            (u * u + (v + 0.70) * (v + 0.70) < 0.12 * 0.12)
            || (u * u + (v + 0.08) * (v + 0.08) < 0.34 * 0.34 && v < -0.08)
            || (v >= -0.08 && v <= 0.26 && Math.Abs(u) < 0.40)
            || (v > 0.30 && v <= 0.56 && Math.Abs(u) < 0.66),

        // An anchor: ring, shank, stock, and the arc of the flukes.
        WonderArchetype.GreatHarbor =>
            (Math.Abs(u) < 0.10 && v > -0.55 && v < 0.62)
            || (v > -0.38 && v < -0.24 && Math.Abs(u) < 0.44)
            || Ring(u, v + 0.62, 0.19, 0.09)
            || (v > 0.30 && Ring(u, v - 0.10, 0.52, 0.10)),

        // An open book: two pages splaying up and out from the spine, with a clear gap between
        // them. The slant is what makes it a book — squared off it is just two bricks.
        WonderArchetype.GreatLibrary =>
            Math.Abs(u) > 0.09 && Math.Abs(u) < 0.68
            && v > -0.08 - 0.46 * (Math.Abs(u) - 0.09) / 0.59
            && v < 0.40 - 0.14 * (Math.Abs(u) - 0.09) / 0.59,

        // A gatehouse: three merlons over a wall with an arch cut out of it. The gaps between the
        // merlons are deliberately wider than the merlons look like they want to be — at a badge
        // this size anything narrower closes up and the whole glyph reads as a plain brick.
        // The arch is a hole rather than a shape: the predicate simply says no, and the dark disc
        // underneath shows through.
        WonderArchetype.Citadel =>
            (Math.Abs(u) < 0.48 && v > -0.30 && v < 0.62 && !(Math.Abs(u) < 0.17 && v > 0.22))
            || (v >= -0.72 && v <= -0.30
                && (Math.Abs(u) < 0.11 || (Math.Abs(u) > 0.31 && Math.Abs(u) < 0.48))),

        // A crown: a banded base under three tapering points.
        WonderArchetype.ImperialPalace =>
            (v > 0.24 && v < 0.56 && Math.Abs(u) < 0.60)
            || (v >= -0.62 && v <= 0.24
                && (Spike(u, v, -0.42) || Spike(u, v, 0.0) || Spike(u, v, 0.42))),

        _ => false,
    };

    /// <summary>An annulus of half-thickness <paramref name="t"/> at radius <paramref name="r"/>.</summary>
    private static bool Ring(double u, double v, double r, double t)
    {
        double d = Math.Sqrt(u * u + v * v);
        return d > r - t && d < r + t;
    }

    /// <summary>One point of the crown, widest at its base and closing to nothing at the tip.</summary>
    private static bool Spike(double u, double v, double centre)
        => Math.Abs(u - centre) < 0.20 * (v + 0.62) / 0.86;

    private static string? GetPhenotypeIconFilename(RaceArchetype archetype) => archetype switch
    {
        RaceArchetype.HighElf or RaceArchetype.WoodElf => "gracile.dds",
        RaceArchetype.Dwarf => "stocky.dds",
        RaceArchetype.Orc => "rough_hewn.dds",
        RaceArchetype.Giantkin => "towering.dds",
        RaceArchetype.Gnome => "diminutive.dds",
        RaceArchetype.Deepkin => "dusk_adapted.dds",
        _ => null // Humans have no special icon
    };

    private static DdsReader.DecodedImage? GetPhenotypeIcon(RaceArchetype archetype)
    {
        if (IconCache.TryGetValue(archetype, out var cached)) return cached;

        string? filename = GetPhenotypeIconFilename(archetype);
        if (filename is null) return IconCache[archetype] = null;

        string traitDir = Path.Combine(
            Emit.StaticFileWriter.SetDirectory(Emit.StaticFileWriter.Core),
            "gfx", "interface", "icons", "traits");

        string path = Path.Combine(traitDir, filename);
        var loaded = DdsReader.Load(path);
        return IconCache[archetype] = loaded;
    }

    public static (byte R, byte G, byte B) HueColour(double hueDegrees, double s, double v)
    {
        double h = ((hueDegrees % 360) + 360) % 360 / 60.0;
        double c = v * s, x = c * (1 - Math.Abs(h % 2 - 1)), m = v - c;

        var (r, g, b) = (int)h switch
        {
            0 => (c, x, 0.0),
            1 => (x, c, 0.0),
            2 => (0.0, c, x),
            3 => (0.0, x, c),
            4 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };

        return ((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }

    private static (byte R, byte G, byte B) Colour(TerrainClass terrain)
        => Io.DebugRender.TerrainColour(terrain);

    // Impassable diagnostics palette. Shared with the legend in MapModes so the key matches the paint.
    public static readonly (byte R, byte G, byte B)
        ImpassableFill = (204, 44, 44),      // ranked in on relief
        MaskFill = (44, 120, 220),           // painted in the user's impassable mask
        TrappedFill = (196, 64, 200),        // filled by the connectivity pass
        QualifiesFill = (232, 200, 64),      // over the floor, cut by the target share
        SteepTint = (236, 140, 36),          // pixel at or above the steep line
        HighTint = (150, 140, 236),          // pixel at or above the mountain line
        PassableBase = (150, 150, 150);

    /// <summary>
    /// Why the impassable pass chose what it chose. Grey hillshade for ground that counts for
    /// nothing, orange where a pixel is steep enough to count, violet where it is high enough,
    /// both where both; impassable provinces filled red (or magenta when the connectivity pass
    /// filled them), and provinces that cleared the floor but lost to the target share in yellow,
    /// so a quota-limited map shows what the next notch of <c>ImpassableShareOfLand</c> would take.
    /// Province borders are drawn throughout so a near miss can be read against its neighbours.
    /// </summary>
    public static Image RenderImpassable(GenerationResult result)
        => RenderImpassable(result.Provinces, result.ProvinceElevation, result.Config);

    public static Image RenderImpassable(ProvinceMap map, float[] elevation, MapConfig cfg)
    {
        int width = map.Width, height = map.Height;
        var diag = map.Impassability;
        float sea = cfg.Limits.SeaLevelUpper;
        float[]? slope = diag is null ? null : Provinces.Slopes(elevation, width, height);

        bool Steep(int i) => slope is not null && slope[i] >= diag!.SteepLine;
        bool High(int i) => diag is not null && elevation[i] >= diag.MountainLine;

        bool Edge(int i)
        {
            int x = i % width, y = i / width, label = map.Label[i];
            return (x + 1 < width && map.Label[i + 1] != label)
                || (y + 1 < height && map.Label[i + width] != label);
        }

        static (byte, byte, byte) Mix((byte R, byte G, byte B) a, (byte R, byte G, byte B) b, double t)
            => ((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

        return Downsample(width, height,
            i =>
            {
                var seed = map.Seeds[map.Label[i]];
                float e = elevation[i];
                if (!seed.IsLand || e <= sea)
                {
                    float depth = Math.Clamp((sea - e) / Math.Max(1f, sea - cfg.SeaFloorElevation), 0, 1);
                    return ((byte)(38 + 26 * (1 - depth)), (byte)(70 + 44 * (1 - depth)),
                            (byte)(104 + 48 * (1 - depth)));
                }

                int x = i % width, y = i / width;
                float left = elevation[y * width + Math.Max(0, x - 1)];
                float up = elevation[Math.Max(0, y - 1) * width + x];
                double shade = Math.Clamp(0.75 - ((e - left) + (e - up)) * 0.05, 0.25, 1.35);

                bool steep = Steep(i), high = High(i);
                var ground = steep && high ? Mix(SteepTint, HighTint, 0.5)
                    : steep ? SteepTint
                    : high ? HighTint
                    : PassableBase;

                var colour = seed.ImpassableCause switch
                {
                    ImpassableCause.Score => Mix(ground, ImpassableFill, 0.6),
                    ImpassableCause.Mask => Mix(ground, MaskFill, 0.6),
                    ImpassableCause.Trapped => Mix(ground, TrappedFill, 0.6),
                    _ when seed.IsImpassable => Mix(ground, ImpassableFill, 0.6),
                    _ when diag is not null && diag.Qualifies(seed.ImpassableScore)
                        => Mix(ground, QualifiesFill, 0.45),
                    _ => ground,
                };

                if (Edge(i)) shade *= seed.IsImpassable ? 0.3 : 0.55;

                return ((byte)Math.Clamp(colour.Item1 * shade, 0, 255),
                        (byte)Math.Clamp(colour.Item2 * shade, 0, 255),
                        (byte)Math.Clamp(colour.Item3 * shade, 0, 255));
            },
            i =>
            {
                var seed = map.Seeds[map.Label[i]];
                if (!seed.IsLand) return 0;
                if (Edge(i)) return seed.IsImpassable ? 4 : 3;
                if (seed.IsImpassable) return 2;
                return Steep(i) || High(i) ? 1 : 0;
            });
    }

    /// <summary>Hover line for the Impassable mode: the province's score against the floor.</summary>
    public static string? ImpassableProbe(GenerationResult result, int cell)
    {
        var map = result.Provinces;
        var seed = map.Seeds[map.Label[cell]];
        if (!seed.IsLand) return null;

        // The mask pass computes no score, so these maps answer by cause alone and never reach the
        // "no pass ran" line below — a pass did run, it just was not the scored one.
        if (map.ImpassableMaskUsed)
            return seed.ImpassableCause switch
            {
                ImpassableCause.Mask => "impassable — painted in the mask",
                ImpassableCause.Trapped => "impassable — trapped (landlocked behind the painted wall)",
                _ when seed.IsImpassable => "impassable",
                _ => "passable — not painted in the mask",
            };

        var diag = map.Impassability;
        if (diag is null) return "no impassable pass ran";
        if (float.IsNaN(seed.ImpassableScore))
            return seed.IsImpassable ? "impassable, unscored" : "unscored";

        string verdict = seed.ImpassableCause switch
        {
            ImpassableCause.Score => "impassable",
            ImpassableCause.Mask => "impassable — painted in the mask",
            ImpassableCause.Trapped => "impassable — trapped (landlocked behind impassables)",
            _ when seed.IsImpassable => "impassable",
            _ when diag.Qualifies(seed.ImpassableScore) => "clears the floor, cut by target share",
            _ => $"below floor by {diag.Floor - seed.ImpassableScore:F2}",
        };

        return $"score {seed.ImpassableScore:F2} vs floor {diag.Floor:F2} · " +
               $"{seed.HighShare:P0} above {diag.MountainLine:F0} m · " +
               $"{seed.SteepShare:P0} steep (≥{diag.SteepLine:F2}/px) · {verdict}";
    }

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