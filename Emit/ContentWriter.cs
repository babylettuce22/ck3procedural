using System.Text;
using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// Writes the script half of the mod: landed titles, province history, terrain and
/// localisation, plus the overrides that neutralise vanilla data keyed to the old province ids.
/// All of these are script files, so they are written UTF-8 **with** BOM.
/// </summary>
public static class ContentWriter
{
    /// <returns>
    /// The handful of things a later edit needs — see <see cref="WrittenContent"/>. Ignored by the
    /// command line, which writes once and exits.
    /// </returns>
    /// <param name="shippedHeightmap">The heightmap <see cref="MapDataWriter.WriteAll"/> just
    /// wrote. The scatter passes need it, and it is required rather than optional on purpose: the
    /// bug it fixes was a scatter quietly reading a surface the game never renders, and a default
    /// would let that back in with no compile error to catch it.</param>
    public static WrittenContent WriteAll(string modDir, string gameDir, MapConfig cfg,
            ProvinceMap provinces, int[] order, int baronyCount, int landCount, int riverCount,
            List<Title> empires, TerrainData terra, TerrainClassifier.Result classified, Rng rng,
            ushort[] shippedHeightmap,
            bool writeHistory = true, MapGen.Drainage? drainage = null,
            MapGen.AzgaarImport? azgaar = null)
    {
        var terrain = classified.Terrain;
        var provinceElevation = terra.ProvinceElevation;
        var runStarted = DateTime.UtcNow;

        Core.Stage.Time("blank vanilla data", () => BlankVanillaData(modDir, gameDir));

        var provinceTerrain = Core.Stage.Time("province terrain vote", () =>
        {
            var vote = ProvinceTerrain(cfg, provinces, order, terrain, landCount);
            ReportTerrain(terrain);
            return vote;
        });

        var vocabulary = Core.Stage.Time("vanilla vocabulary", () => MapGen.VanillaVocabulary.Read(gameDir));

        if (!vocabulary.IsUsable)
            throw new InvalidOperationException(
                $"Could not read enough of the game's own culture and religion data from '{gameDir}' " +
                "to generate against. Check that the game directory is correct and uncompressed.");

        var counties = Titles.Flatten(empires).Where(t => t.Tier == "c").ToList();

        // Matches every title to whatever Azgaar had on the same ground. Must run before the
        // cultures below, which ask it which culture holds which county.
        if (azgaar is not null)
            Core.Stage.Time("azgaar binding",
                () => azgaar.Bind(empires, provinces, order, baronyCount));

        // A country's government comes from its own form, not from our terrain reasoning — the
        // difference between a Kingdom and a Most Serene Republic is the export's to state.
        var stateGovernments = azgaar is null ? null : MapGen.AzgaarGovernments.ByState(azgaar, cfg);

        if (stateGovernments is not null)
            Console.WriteLine("  azgaar governments: " + string.Join(", ",
                MapGen.AzgaarGovernments.Tally(stateGovernments)
                    .Select(g => $"{g.Count} {TitleTierWriter.Token(g.Government)}")));

        var development = Core.Stage.Time("development", () =>
        {
            var levels = MapGen.Development.ForCounties(counties, provinceTerrain, cfg,
                new Rng(cfg.Seed ^ 0x0DE7));
            ReportDevelopment(levels);
            return levels;
        });

        var wilderness = Core.Stage.Time("wilderness", () => MapGen.Wilderness.Build(counties,
            provinces, order, landCount, provinceTerrain, development, cfg, new Rng(cfg.Seed ^ 0x1D17),
            azgaar));

        Dictionary<(string Culture, string Government), string>? tierForms = null;

        var cultures = Core.Stage.Time("cultures", () =>
        {
            var map = MapGen.Cultures.Build(empires, provinces, order, landCount, provinceTerrain,
                development, vocabulary, cfg, new Rng(cfg.Seed ^ 0x0C17), azgaar);

            if (azgaar is not null)
            {
                int renamed = MapGen.AzgaarNaming.RenameCultures(azgaar, map);
                Console.WriteLine($"  azgaar: {renamed} of {map.Cultures.Count} cultures named from the export");
            }

            // Which word each culture-and-government will render for a rank, decided once here so
            // the title names below can leave that word out rather than repeating it.
            tierForms = azgaar is null || stateGovernments is null
                ? null
                : TitleTierWriter.FormsByCulture(azgaar, map, stateGovernments);

            // Every culture's words for its realms, and every state's word for its own title,
            // decided here and stored on the objects — before naming, which leaves a state's word
            // out of its name because the tier will say it — and written out by TitleTierWriter
            // further down.
            TitleTierWriter.Assign(map, new Rng(cfg.Seed ^ 0x7117), tierForms, azgaar);

            // Worked out for the whole hierarchy at once rather than title by title, so each state
            // and burg goes to the title that actually contains most of it — see AzgaarNaming.
            var borrowed = azgaar is null
                ? null
                : MapGen.AzgaarNaming.TitleNames(azgaar, empires, tierForms, map, stateGovernments);

            Titles.AssignNames(empires, map, new Rng(cfg.Seed ^ 0x7171), borrowed);

            if (borrowed is not null)
                Console.WriteLine($"  azgaar: {borrowed.Count} of {Titles.Flatten(empires).Count()} " +
                                  "titles named from the export, the rest from its name bases");

            return map;
        });

        var ethnicities = Core.Stage.Time("ethnicities", () => MapGen.Ethnicities.Build(
            cultures.Heritages, cultures.Cultures, provinceTerrain, cfg, new Rng(cfg.Seed ^ 0x38F1),
            wilderness));

        Core.Stage.Time("ethnicity files", () => EthnicityWriter.WriteAll(modDir, ethnicities));

        // The render-time enforcement of those ethnicities' races — same table, other end.
        Core.Stage.Time("race morph modifiers", () => RaceMorphWriter.WriteAll(modDir, cfg, ethnicities));

        var worldCenters = Core.Stage.Time("world centers", () => WorldCenterMap.Build(
            counties, provinces, order, landCount, provinceTerrain, cultures, wilderness, cfg, new Rng(cfg.Seed ^ 0x93FA)));

        development = Core.Stage.Time("development", () =>
        {
            var levels = MapGen.Development.ForCounties(counties, provinceTerrain, cfg,
                new Rng(cfg.Seed ^ 0x0DE7), worldCenters);
            ReportDevelopment(levels);
            return levels;
        });

        var realms = Core.Stage.Time("realms", () => Realms.Build(
                    empires, development, wilderness, cfg, new Rng(cfg.Seed ^ 0x2E17), provinces, order,
                    baronyCount, azgaar));

        var governments = Core.Stage.Time("governments", () => MapGen.Governments.Build(
            empires, counties, realms, provinceTerrain, development, cultures,
            worldCenters, cfg, new Rng(cfg.Seed ^ 0x6017), azgaar, stateGovernments));

        Console.WriteLine("  governments: " + string.Join(", ",
            governments.Tally(counties.Count).Select(g => $"{g.Count} {g.Government[..^11]}")));

        Core.Stage.Time("government overrides",
            () => GovernmentWriter.WriteNomadNaming(modDir, gameDir, counties.Any(governments.IsNomad)));

        var faiths = Core.Stage.Time("faiths", () => MapGen.Faiths.Build(empires, provinces, order,
            landCount, provinceTerrain, development, governments, vocabulary, wilderness, cfg, worldCenters,
            new Rng(cfg.Seed ^ 0x0FA1), azgaar));

        // The Tier 1 renamer only runs when the structure is still ours. Built from the export's
        // own tree, the faiths already carry its names, and a rename by majority vote could only
        // disagree with the geography they were cut from.
        if (azgaar is not null && !faiths.ImportedStructure)
        {
            var (namedFaiths, namedReligions) = MapGen.AzgaarNaming.RenameFaiths(azgaar, faiths);
            Console.WriteLine($"  azgaar: {namedFaiths} of {faiths.Faiths.Count} faiths and " +
                              $"{namedReligions} of {faiths.Religions.Count} religions named from the export");
        }

        if (wilderness.Count > 0)
        {
            var unsettledCulture = MapGen.Cultures.CreateUnsettled(
                cultures.Heritages[0], vocabulary, new Rng(cfg.Seed ^ 0x0C55));

            cultures.Cultures.Add(unsettledCulture);
            foreach (var county in wilderness.Counties) cultures.ByCounty[county] = unsettledCulture;

            var (unsettledReligion, unsettledFaith) = MapGen.Faiths.CreateUnsettled(vocabulary,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase), cfg, new Rng(cfg.Seed ^ 0x0FA5));

            faiths.Religions.Add(unsettledReligion);
            faiths.Faiths.Add(unsettledFaith);
            foreach (var county in wilderness.Counties) faiths.ByCounty[county] = unsettledFaith;
        }

        // Farmland and oases, placed from settlement and drainage rather than from climate. Runs
        // here, after every social layer has been decided, so nothing reads a terrain that only
        // exists *because* of the settlement: development, government, culture and faith all see
        // the pre-cultivation map. Both the pixel raster and the province vote are rewritten, so
        // the painted ground and common/province_terrain cannot disagree. See MapGen/Cultivation.cs.
        Core.Stage.Time("cultivation", () => MapGen.Cultivation.Apply(cfg, provinces, order,
            landCount, terrain, provinceTerrain, counties, governments, development, wilderness,
            drainage, provinceElevation, new Rng(cfg.Seed ^ 0x0FA2)));

        // The traced courses go in so each river can be named along its length rather than by the
        // latitude of its provinces — see WaterNaming.GroupRiverProvinces.
        var waterNames = Core.Stage.Time("water naming", () => WaterNaming.Generate(
            provinces, order, landCount, riverCount, cultures, empires, cfg,
            new Rng(cfg.Seed ^ 0x5EAE), terra.MajorRiversList, azgaar));

        Core.Stage.Time("titles, history and localisation", () =>
        {
            WriteLandedTitles(modDir, empires, faiths, wilderness, worldCenters);
            WriteProvinceTerrain(modDir, provinceTerrain, landCount);
            WriteProvinceHistory(modDir, cfg, empires, provinceTerrain, development, cultures, faiths, governments, wilderness, worldCenters, cfg.Seed);
            WriteLocalisation(modDir, empires, waterNames, provinces, order, baronyCount,
                landCount, riverCount);
        });

        Core.Stage.Time("wonders", () => WonderWriter.WriteAll(modDir, worldCenters));

        Core.Stage.Time("title tiers", () => TitleTierWriter.WriteAll(modDir, cultures, empires));

        Core.Stage.Time("culture files",
            () => CultureWriter.WriteAll(modDir, cfg, cultures, ethnicities, vocabulary,
                new Rng(cfg.Seed ^ 0x0C1A)));

        Core.Stage.Time("compatibility", () =>
        {
            CompatibilityWriter.WriteDefines(modDir, gameDir, cfg);
            CompatibilityWriter.WriteCultureEras(modDir, gameDir, cfg);
            CompatibilityWriter.WriteGeographicalRegions(modDir, gameDir, empires);
            CompatibilityWriter.WriteHolySites(modDir, gameDir, empires, faiths);
        });

        Core.Stage.Time("religion files", () => ReligionWriter.WriteAll(modDir, faiths));

        Core.Stage.Time("vanilla titulars",
            () => CompatibilityWriter.WriteVanillaTitulars(modDir, gameDir, empires));

        Core.Stage.Time("locators", () => LocatorWriter.WriteAll(modDir, gameDir, provinces, order, landCount, provinceElevation, cfg));
        Core.Stage.Time("casus belli", () => CasusBelliWriter.WriteAll(modDir, gameDir, cfg));
        Core.Stage.Time("frontend", () => FrontendWriter.WriteFrontend(modDir, gameDir));
        Core.Stage.Time("GUI changes", () => GuiWriter.WriteAll(modDir, gameDir, cfg));

        if (cfg.EnableFantasyEthnicities && cfg.RaceMode != MapConfig.FantasyRaceMode.HumanOnly)
        {
            Core.Stage.Time("character interactions",
                () => InteractionWriter.PatchMarriageInteractions(modDir, gameDir));
        }

        // Full-resolution heightmap elevation passed to detail texture generator
        Core.Stage.Time("terrain textures", () => TerrainTextureWriter.WriteAll(modDir, cfg, terrain,
            classified.Climate, terra.Elevation, rng));

        Core.Stage.Time("map graphics", () => MapGraphicsWriter.WriteAll(modDir, gameDir, cfg, provinces, order, landCount));

        // Kept rather than dropped: StruggleArt cuts each struggle's window background out of this
        // same buffer further down, and re-rendering or re-reading it there would be the same
        // parchment twice.
        var flatmap = Core.Stage.Time("flatmap", () => FlatmapWriter.WriteAll(
            modDir, cfg, provinces, order, landCount, provinceElevation, provinceTerrain));

        Core.Stage.Time("terrain masks", () => TerrainMaskWriter.WriteAll(modDir, gameDir, cfg));

        // The scatters are placed against the heightmap *as the engine will reconstruct it*, not
        // against the one we computed. Two passes move that surface and both have to be in it.
        //
        // First the coastline work MapDataWriter does before writing heightmap.png — forcing
        // the shore to agree with provinces.png, then plunging the shelf and smoothing the land
        // side. That is precisely what moves the shoreline, so this takes the array that writer
        // shipped rather than converting terra.Elevation a second time and missing it.
        //
        // Then the packer, which quantises the terrain into a tile atlas and reassembles it, near
        // a shore by enough to leave trunks standing in water. Reconstruct shares its LOD
        // assignment with Pack, so feeding it the shipped array is also what makes the surface the
        // scatter reads agree with the packed one tile for tile.
        var renderedElevation = Core.Stage.Time("rendered heightmap", () =>
            HeightmapSource.ToSimulationScale(
                HeightmapPacker.Reconstruct(shippedHeightmap, cfg.Width, cfg.Height), cfg));

        // renderedElevation for all three, not terra.Elevation: every one of them seeds from
        // province-resolution terrain and then jitters to a sub-pixel position, and has to ask the
        // heightmap the engine renders whether that spot is dry. See ScatterGround.
        Core.Stage.Time("trees", () => TreeWriter.WriteAll(modDir, cfg, terrain, classified.Climate, renderedElevation, rng));
        Core.Stage.Time("animals", () => AnimalWriter.WriteAll(modDir, cfg, terrain, renderedElevation, rng));
        Core.Stage.Time("env effects", () => EnvEffectWriter.WriteAll(modDir, cfg, terrain, renderedElevation, rng));
        Core.Stage.Time("bridges", () => BridgeWriter.WriteAll(modDir, cfg, terra.MajorRiversList, classified.Climate, renderedElevation, rng));
        Core.Stage.Time("map table", () => MapTableWriter.WriteAll(modDir, cfg));
        Core.Stage.Time("holding models", () => HoldingModelWriter.WriteAll(modDir, gameDir, cfg));

        // Null with --no-history, like Realms: rulers only exist once the history phase decides them,
        // and prehistory is kept beside them because re-emitting a ruler means re-emitting the
        // family and relations written around him.
        RulerMap? rulers = null;
        PrehistoryMap? prehistory = null;

        // --- AFTER (clean and unified) ---
        if (writeHistory)
        {
            Core.Stage.Time("history and bookmarks", () =>
            {
                prehistory = Core.Stage.Time("prehistory", () => PrehistoryMap.Build(
                    counties, provinces, order, landCount, realms, cultures, faiths,
                    governments, worldCenters, wilderness, cfg, new Rng(cfg.Seed ^ 0x4821)));

                // After prehistory, which it reads the houses and fathers from, and before
                // anything that names a ruler: the bookmarks and the character file both read
                // from this rather than each drawing the man again.
                rulers = Core.Stage.Time("rulers", () => RulerMap.Build(
                    counties, cfg, realms, cultures, faiths, governments, wilderness, prehistory));

                var artifacts = MapGen.ArtifactMap.Build(
                    counties, cultures, faiths, realms, wilderness, new Rng(cfg.Seed ^ 0x4A1F));

                ArtifactWriter.WriteTemplates(modDir);
                ArtifactWriter.WriteModifiers(modDir);
                ArtifactWriter.WriteLocalisation(modDir, artifacts);
                ArtifactWriter.WriteOnGameStart(modDir, artifacts);

                var bookmarkResult = BookmarkWriter.WriteAll(
                    modDir, gameDir, cfg, provinces, order, empires,
                    realms, development, cultures, faiths, governments, wilderness, prehistory,
                    rulers);

                HistoryWriter.WriteAll(
                    modDir, cfg, empires, realms, development,
                    cultures, ethnicities, faiths, governments, wilderness, prehistory, rulers);

                // Last of the history block, because it reads everything the rest of it decided.
                // Inside the block rather than beside it: with --no-history there are no houses, no
                // wars and no artifacts, so a chronicle written there could only repeat the map back
                // at the player, and the GUI already treats a missing key as "no button".
                var chronicle = Core.Stage.Time("chronicle", () => ChronicleMap.Build(
                    empires, realms, development, cultures, faiths, wilderness, prehistory,
                    artifacts, worldCenters, cfg, new Rng(cfg.Seed ^ 0x104E)));

                ChronicleWriter.WriteAll(modDir, chronicle, empires);

                // After the chronicle, which is the thing that decides where a struggle is. Reads
                // the counties for its membership and the chronicle only for its tension, so it
                // cannot invent a quarrel the lore panel does not also report.
                var struggles = Core.Stage.Time("struggles", () => StruggleMap.Build(
                    empires, chronicle, cultures, faiths, wilderness, cfg,
                    new Rng(cfg.Seed ^ 0x57A6)));

                StruggleWriter.WriteAll(modDir, gameDir, cfg, struggles, flatmap, provinces, order);

                WarWriter.WriteAll(modDir, prehistory);
                PortraitWriter.WriteAll(modDir, gameDir, bookmarkResult.PortraitRequests, ethnicities, cfg.Seed);
            });
        }
        else Console.WriteLine("  history: SKIPPED (--no-history)");

        List<string> sets = [StaticFileWriter.Core];
        if (cfg.EnableWilderness) sets.Add(StaticFileWriter.Wilderness);
        if (cfg.EnableFantasyEthnicities && cfg.RaceMode != MapConfig.FantasyRaceMode.HumanOnly)
            sets.Add(StaticFileWriter.Fantasy);
        Core.Stage.Time("static files", () => StaticFileWriter.WriteAll(modDir, sets, runStarted));

        // After the write rather than during it: cultures and faiths both gain their unsettled
        // entries above, and a capture taken where each was built would not have them.
        return new WrittenContent
        {
            Cultures = cultures,
            Ethnicities = ethnicities,
            Faiths = faiths,
            WaterNames = waterNames,
            Wilderness = wilderness,
            WorldCenters = worldCenters,
            Realms = realms,
            Rulers = rulers,
            Prehistory = prehistory,
            Governments = governments,
            BaronyCount = baronyCount,
            LandCount = landCount,
            RiverCount = riverCount,
        };
    }

    public static WrittenContent WriteAll(string modDir, string gameDir, MapConfig cfg,
        ProvinceMap provinces, int[] order, int baronyCount, int landCount, List<Title> empires,
        TerrainData terra, TerrainClassifier.Result classified, Rng rng,
        ushort[] shippedHeightmap, bool writeHistory = true)
    {
        int riverCount = landCount;
        for (int i = 0; i < provinces.Count; i++)
            if (!provinces.Seeds[i].IsLand && provinces.Seeds[i].IsMajorRiver) riverCount++;

        return WriteAll(modDir, gameDir, cfg, provinces, order, baronyCount, landCount, riverCount,
            empires, terra, classified, rng, shippedHeightmap, writeHistory);
    }

    /// <summary>
    /// Not private: every title's colour lives in this file, so recolouring one after the mod is
    /// written re-runs exactly this. See <see cref="WorldOverwrite"/>.
    /// </summary>
    internal static void WriteLandedTitles(string modDir, List<Title> empires, FaithMap faiths,
        WildernessMap wilderness, WorldCenterMap? worldCenters)
    {
        string dir = Path.Combine(modDir, "common", "landed_titles");
        Directory.CreateDirectory(dir);

        var sb = new StringBuilder();
        sb.Append("# Generated de jure hierarchy.\n\n");
        foreach (var empire in empires) Write(empire, 0);

        sb.Append("# Head of faith landless titles.\n\n");
        foreach (var faith in faiths.Faiths)
        {
            if (faith.Head is null) continue;
            var (r, g, b) = faith.Color;
            string fr = r.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            string fg = g.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            string fb = b.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

            sb.Append($"{faith.Head.TitleKey} = {{\n");
            sb.Append($"    color = {{ {fr} {fg} {fb} }}\n");
            sb.Append($"    capital = {faith.Head.Seat.Key}\n");
            sb.Append("    landless = yes\n");
            sb.Append("}\n\n");
        }

        var wildCapital = wilderness.Counties.FirstOrDefault();
        if (wildCapital is not null)
        {
            sb.Append("# The wilderness realm. Titular: it exists so unsettled land has a name.\n\n");
            sb.Append($"{WildernessMap.TitleKey} = {{\n");
            sb.Append("    color = { 108 104 96 }\n");
            sb.Append($"    capital = {wildCapital.Key}\n");
            sb.Append("    landless = yes\n");
            sb.Append("    definite_form = yes\n");
            sb.Append("    ruler_uses_title_name = no\n");
            sb.Append("}\n\n");
        }

        ParadoxText.WriteBom(Path.Combine(dir, "00_landed_titles.txt"), sb.ToString());
        return;

        void Write(Title title, int depth)
        {
            string pad = new(' ', depth * 4);
            sb.Append($"{pad}{title.Key} = {{\n");
            sb.Append($"{pad}    color = {{ {title.Color.R} {title.Color.G} {title.Color.B} }}\n");

            if (title.Tier == "b")
            {
                sb.Append($"{pad}    province = {title.ProvinceId}\n");
                var center = worldCenters?.Centers.FirstOrDefault(wc => wc.CapitalBarony == title);
                if (center is not null)
                {
                    sb.Append($"{pad}    special_building = {center.Wonder.Key}\n");
                }
            }
            else
            {
                if (title.Tier == "c") sb.Append($"{pad}    definite_form = no\n");
                foreach (var child in title.Children) Write(child, depth + 1);
            }

            sb.Append($"{pad}}}\n");
        }
    }

    private static void ReportTerrain(TerrainClass[] terrain)
    {
        var counts = new long[Enum.GetValues<TerrainClass>().Length];
        foreach (var t in terrain) counts[(int)t]++;

        long land = 0;
        for (int c = 0; c < counts.Length; c++)
            if ((TerrainClass)c != TerrainClass.Sea) land += counts[c];
        if (land == 0) return;

        var parts = Enumerable.Range(0, counts.Length)
            .Where(c => (TerrainClass)c != TerrainClass.Sea && counts[c] > 0)
            .OrderByDescending(c => counts[c])
            .Select(c => $"{(TerrainClass)c} {100.0 * counts[c] / land:F1}%");

        Console.WriteLine($"  terrain classes (share of land): {string.Join(", ", parts)}");
    }

    public static TerrainClass[] ProvinceTerrain(MapConfig cfg, ProvinceMap provinces,
        int[] order, TerrainClass[] terrain, int landCount)
    {
        int width = cfg.ProvinceWidth, height = cfg.ProvinceHeight;
        int classes = Enum.GetValues<TerrainClass>().Length;

        var votes = new int[(provinces.Count + 1) * classes];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                int id = order[provinces.Label[i]];
                if (id > landCount) continue;

                var t = terrain[i];
                if (t is TerrainClass.Sea or TerrainClass.Beach) continue;
                votes[id * classes + (int)t]++;
            }
        }

        var result = new TerrainClass[provinces.Count + 1];
        for (int id = 1; id <= provinces.Count; id++)
        {
            if (id > landCount) { result[id] = TerrainClass.Sea; continue; }

            int best = -1, bestCount = 0;
            for (int c = 0; c < classes; c++)
            {
                int n = votes[id * classes + c];
                if (n > bestCount) { bestCount = n; best = c; }
            }

            result[id] = best < 0 ? TerrainClass.Plains : (TerrainClass)best;
        }

        return result;
    }

    private static void WriteProvinceTerrain(string modDir, TerrainClass[] terrain, int landCount)
    {
        string dir = Path.Combine(modDir, "common", "province_terrain");
        Directory.CreateDirectory(dir);

        var sb = new StringBuilder();
        sb.Append("default_land=plains\n");
        sb.Append("default_sea=sea\n");
        sb.Append("default_coastal_sea=coastal_sea\n");

        for (int id = 1; id <= landCount; id++)
            sb.Append($"{id}={TerrainClassifier.Name(terrain[id])}\n");

        ParadoxText.WriteBom(Path.Combine(dir, "00_province_terrain.txt"), sb.ToString());
    }

    private static void ReportDevelopment(Dictionary<Title, int> development)
    {
        if (development.Count == 0) return;
        var levels = development.Values.OrderBy(v => v).ToList();
        Console.WriteLine($"  development: min {levels[0]}, median {levels[levels.Count / 2]}, " +
                          $"p90 {levels[(int)(levels.Count * 0.9)]}, max {levels[^1]} " +
                          $"(vanilla 867: median 8, mass 0-16)");
    }

    private static void WriteProvinceHistory(string modDir, MapConfig cfg, List<Title> empires,
        TerrainClass[] provinceTerrain, Dictionary<Title, int> development, CultureMap cultures,
        FaithMap faiths, GovernmentMap governments, WildernessMap wilderness,
        WorldCenterMap worldCenters, int cfgSeed)
    {
        string dir = Path.Combine(modDir, "history", "provinces");
        Directory.CreateDirectory(dir);

        var rng = new Rng(cfgSeed ^ 0x8A12);
        var counts = new Dictionary<string, int>();

        var sb = new StringBuilder();

        var wondersByBarony = worldCenters.Centers
            .ToDictionary(wc => wc.CapitalBarony, wc => wc.Wonder);

        foreach (var county in Titles.Flatten(empires).Where(t => t.Tier == "c"))
        {
            int level = development.GetValueOrDefault(county);
            string cultureKey = cultures.For(county).Key;
            string faith = faiths.For(county).Key;
            string government = governments.For(county);
            bool wild = wilderness.Contains(county);

            for (int i = 0; i < county.Children.Count; i++)
            {
                var barony = county.Children[i];
                var terrain = barony.ProvinceId >= 0 && barony.ProvinceId < provinceTerrain.Length
                    ? provinceTerrain[barony.ProvinceId]
                    : TerrainClass.Plains;

                string holding = wild
                    ? (i == 0 ? "wilderness_holding" : "none")
                    : MapGen.Development.Holding(i, terrain, level, government, rng);

                counts[holding] = counts.GetValueOrDefault(holding) + 1;

                sb.Append($"{barony.ProvinceId} = {{\n");
                sb.Append($"    culture = {cultureKey}\n");
                sb.Append($"    religion = {faith}\n");
                sb.Append($"    holding = {holding}\n");

                if (wondersByBarony.TryGetValue(barony, out var wonder))
                {
                    sb.Append($"    special_building = {wonder.Key}\n");
                }

                sb.Append("}\n");
            }
        }

        Console.WriteLine("  holdings: " + string.Join(", ",
            counts.OrderByDescending(k => k.Value).Select(k => $"{k.Value} {k.Key}")));

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_provinces.txt"), sb.ToString());
    }

    /// <summary>
    /// Every generated name the game displays, in one file.
    ///
    /// Not private, because renaming a title after the mod is written re-runs exactly this — see
    /// <see cref="WorldOverwrite"/>. It is written whole rather than patched, so re-emitting cannot
    /// leave an entry behind pointing at a name that no longer exists.
    ///
    /// The three id ranges are disjoint and the order they are written in does not matter:
    /// 1..<paramref name="baronyCount"/> are the baronies and take their name from the title,
    /// <paramref name="baronyCount"/>+1..<paramref name="landCount"/> are the impassable land that
    /// has no title at all, and everything above is water.
    /// </summary>
    internal static void WriteLocalisation(
            string modDir,
            List<Title> empires,
            Dictionary<int, string> waterNames,
            ProvinceMap provinces,
            int[] order,
            int baronyCount,
            int landCount,
            int riverCount)
    {
        string dir = Path.Combine(modDir, "localization", "english");
        Directory.CreateDirectory(dir);

        var sb = new StringBuilder();
        sb.Append("l_english:\n");

        // Every entry carries the :0 version marker. Paradox treats a missing one as version 0 in
        // most files and as a parse error in some, and the launcher's own validator flags it, so it
        // is written rather than relied upon.
        //
        // The PROV<id> keys are deliberately absent. They are the *vanilla* province name keys, and
        // vanilla declares all 8,000-odd of them; re-declaring one per generated province put a
        // duplicate in the dictionary for every id the base game also uses, and CK3 resolves those
        // by load order rather than by mod. prov_<id> is the key the map actually reads.
        foreach (var title in Titles.Flatten(empires))
        {
            string name = ParadoxText.Loc(title.Name);
            sb.Append($" {title.Key}:0 \"{name}\"\n");

            if (title.Tier == "b" && title.ProvinceId > 0)
                sb.Append($" prov_{title.ProvinceId}:0 \"{name}\"\n");
        }

        // From baronyCount + 1, not from 1. Starting at 1 covered every barony as well, so each one
        // got a second, later entry reading "Wasteland" — the impassable provinces are the ones
        // above the last barony, and they are the only ones without a title to be named after.
        for (int id = baronyCount + 1; id <= landCount; id++)
            sb.Append($" prov_{id}:0 \"Wasteland\"\n");

        for (int id = landCount + 1; id <= provinces.Count; id++)
        {
            string name = ParadoxText.Loc(
                waterNames.GetValueOrDefault(id, id <= riverCount ? $"River {id}" : $"Sea of {id}"));

            // One key or the other, never both: a province is a river or a sea, and writing both
            // left every river also declared as a sea of the same name.
            if (id <= riverCount) sb.Append($" river_{id}:0 \"{name}\"\n");
            else sb.Append($" sea_{id}:0 \"{name}\"\n");
        }

        ParadoxText.WriteBom(Path.Combine(dir, "gen_titles_l_english.yml"), sb.ToString());
    }

    private static void BlankVanillaData(string modDir, string gameDir)
    {
        string[] targets =
        [
            Path.Combine("common", "landed_titles"),
            Path.Combine("common", "province_terrain"),
            Path.Combine("map_data", "geographical_regions"),
            Path.Combine("history", "provinces"),
            Path.Combine("history", "titles"),
            Path.Combine("history", "characters"),
            Path.Combine("history", "struggles"),
            Path.Combine("history", "situations"),
            Path.Combine("history", "wars"),
            Path.Combine("common", "bookmarks", "bookmarks"),
            Path.Combine("common", "dynasty_houses"),
            Path.Combine("common", "dynasties"),
            Path.Combine("common", "bookmarks", "challenge_characters"),
            Path.Combine("common", "bookmark_portraits"),

            // Vanilla's 361 DNA records describe bookmark characters that `history/characters` and
            // `bookmark_portraits` above have already deleted, so they were dead weight even before
            // this. They have to go now rather than merely being ignorable, because a DNA record is
            // validated against the full gene list and the mod adds one: `gen_race_skin`, from
            // BaseFilesToCopy/Core/common/genes. Records written before that gene existed do not
            // mention it, and the engine complains once per record on load. Elder Kings solves the
            // same problem the same way, blanking every stock DNA file down to a comment.
            Path.Combine("common", "dna_data"),
            Path.Combine("common", "coat_of_arms", "dynamic_definitions"),
        ];

        int blanked = 0;
        foreach (string target in targets)
        {
            string source = Path.Combine(gameDir, target);
            if (!Directory.Exists(source)) continue;

            string destination = Path.Combine(modDir, target);
            Directory.CreateDirectory(destination);

            foreach (string file in Directory.GetFiles(source, "*.txt"))
            {
                ParadoxText.WriteBom(Path.Combine(destination, Path.GetFileName(file)), "\n");
                blanked++;
            }
        }

        Console.WriteLine($"  blanked {blanked} vanilla files bound to the old map");
    }
}