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
    /// <param name="classified">
    /// The painted terrain *and* the climate behind it. Both are needed rather than just the
    /// terrain classes: the material families in <see cref="TerrainPalette"/> are indexed by
    /// climate, so the painter cannot pick a family without it.
    /// </param>
    public static void WriteAll(string modDir, string gameDir, MapConfig cfg,
            ProvinceMap provinces, int[] order, int landCount, int riverCount, List<Title> empires,
            float[] provinceElevation, TerrainClassifier.Result classified, Rng rng,
            bool writeHistory = true)
    {
        var terrain = classified.Terrain;
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

        var development = Core.Stage.Time("development", () =>
        {
            var levels = MapGen.Development.ForCounties(counties, provinceTerrain, cfg,
                new Rng(cfg.Seed ^ 0x0DE7));
            ReportDevelopment(levels);
            return levels;
        });

        var wilderness = Core.Stage.Time("wilderness", () => MapGen.Wilderness.Build(counties,
            provinces, order, landCount, provinceTerrain, development, cfg, new Rng(cfg.Seed ^ 0x1D17)));

        var cultures = Core.Stage.Time("cultures", () =>
        {
            var map = MapGen.Cultures.Build(empires, provinces, order, landCount, provinceTerrain,
                development, vocabulary, cfg, new Rng(cfg.Seed ^ 0x0C17));
            Titles.AssignNames(empires, map, new Rng(cfg.Seed ^ 0x7171));
            return map;
        });

        var worldCenters = Core.Stage.Time("world centers", () => WorldCenterMap.Build(
            counties, provinces, order, landCount, provinceTerrain, cultures, wilderness, cfg, new Rng(cfg.Seed ^ 0x93FA)));

        development = Core.Stage.Time("development", () =>
        {
            var levels = MapGen.Development.ForCounties(counties, provinceTerrain, cfg,
                new Rng(cfg.Seed ^ 0x0DE7), worldCenters);
            ReportDevelopment(levels);
            return levels;
        });

        var governments = MapGen.Governments.Build(counties, provinceTerrain, development, cultures,
            cfg, new Rng(cfg.Seed ^ 0x6017));
        Console.WriteLine("  governments: " + string.Join(", ",
            governments.Tally(counties.Count).Select(g => $"{g.Count} {g.Government[..^11]}")));

        var faiths = Core.Stage.Time("faiths", () => MapGen.Faiths.Build(empires, provinces, order,
            landCount, provinceTerrain, development, governments, vocabulary, wilderness, cfg, worldCenters,
            new Rng(cfg.Seed ^ 0x0FA1)));

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

        // 5. Generate authentic regional names for Major Rivers and Sea Zones
        var waterNames = Core.Stage.Time("water naming", () => WaterNaming.Generate(
            provinces, order, landCount, riverCount, cultures, empires, cfg, new Rng(cfg.Seed ^ 0x5EAE)));

        Core.Stage.Time("titles, history and localisation", () =>
        {
            WriteLandedTitles(modDir, empires, faiths, wilderness, worldCenters);
            WriteProvinceTerrain(modDir, provinceTerrain, landCount);
            WriteProvinceHistory(modDir, cfg, empires, provinceTerrain, development, cultures, faiths, governments, wilderness, worldCenters, cfg.Seed);
            WriteLocalisation(modDir, empires, waterNames, provinces, order, landCount, riverCount);
        });

        Core.Stage.Time("wonders", () => WonderWriter.WriteAll(modDir, worldCenters));

        Core.Stage.Time("culture files",
            () => CultureWriter.WriteAll(modDir, cfg, cultures, vocabulary, new Rng(cfg.Seed ^ 0x0C1A)));

        Core.Stage.Time("compatibility", () =>
        {
            CompatibilityWriter.WriteDefines(modDir, gameDir, cfg);
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

        Core.Stage.Time("terrain textures", () => TerrainTextureWriter.WriteAll(modDir, cfg, terrain,
            classified.Climate, provinceElevation, rng));

        Core.Stage.Time("map graphics", () => MapGraphicsWriter.WriteAll(modDir, gameDir, cfg, provinces, order, landCount));

        Core.Stage.Time("flatmap", () => FlatmapWriter.WriteAll(
            modDir, cfg, provinces, order, landCount, provinceElevation, provinceTerrain));

        Core.Stage.Time("terrain masks", () => TerrainMaskWriter.WriteAll(modDir, gameDir, cfg));
        Core.Stage.Time("trees", () => TreeWriter.WriteAll(modDir, cfg, terrain, rng));
        Core.Stage.Time("map table", () => MapTableWriter.WriteAll(modDir, cfg));

        if (writeHistory)
        {
            Core.Stage.Time("history and bookmarks", () =>
            {
                var realms = Realms.Build(empires, development, wilderness, cfg, new Rng(cfg.Seed ^ 0x2E17));

                var prehistory = Core.Stage.Time("prehistory", () => PrehistoryMap.Build(
                    counties, provinces, order, landCount, realms, cultures, faiths,
                    governments, worldCenters, wilderness, cfg, new Rng(cfg.Seed ^ 0x4821)));

                var artifacts = MapGen.ArtifactMap.Build(
                    counties, cultures, faiths, realms, wilderness, new Rng(cfg.Seed ^ 0x4A1F));

                ArtifactWriter.WriteTemplates(modDir);
                ArtifactWriter.WriteModifiers(modDir);
                ArtifactWriter.WriteLocalisation(modDir, artifacts);
                ArtifactWriter.WriteOnGameStart(modDir, artifacts);

                var bookmarkResult = BookmarkWriter.WriteAll(
                    modDir, gameDir, cfg, provinces, order, empires,
                    realms, development, cultures, faiths, governments, wilderness, prehistory);

                HistoryWriter.WriteAll(
                    modDir, cfg, empires, realms, development,
                    cultures, faiths, governments, wilderness, prehistory, bookmarkResult.BookmarkDnaMap);

                WarWriter.WriteAll(modDir, prehistory);
                PortraitWriter.WriteAll(modDir, gameDir, bookmarkResult.PortraitRequests, cfg.Seed);
            });
        }
        else Console.WriteLine("  history: SKIPPED (--no-history)");

        List<string> sets = [StaticFileWriter.Core];
        if (cfg.EnableWilderness) sets.Add(StaticFileWriter.Wilderness);
        Core.Stage.Time("static files", () => StaticFileWriter.WriteAll(modDir, sets, runStarted));
    }

    public static void WriteAll(string modDir, string gameDir, MapConfig cfg,
    ProvinceMap provinces, int[] order, int landCount, List<Title> empires,
    float[] provinceElevation, TerrainClassifier.Result classified, Rng rng,
    bool writeHistory = true)
    {
        int riverCount = landCount;
        for (int i = 0; i < provinces.Count; i++)
            if (!provinces.Seeds[i].IsLand && provinces.Seeds[i].IsMajorRiver) riverCount++;

        WriteAll(modDir, gameDir, cfg, provinces, order, landCount, riverCount, empires,
            provinceElevation, classified, rng, writeHistory);
    }

    /// <summary>
    /// The de jure tree. Uses vanilla's filename so it *replaces* 00_landed_titles.txt rather
    /// than adding to it — vanilla's baronies reference province ids up to ~14143, which no
    /// longer exist on our map, so leaving it in place would dangle every one of them.
    /// </summary>
    private static void WriteLandedTitles(string modDir, List<Title> empires, FaithMap faiths,
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

        // The wilderness realm's own title. Landless in vanilla's sense — no de jure counties,
        // held only so the dummy's realm is called something. Its capital has to be a real county,
        // and the first unsettled one is as good as any: nothing reads it except the map's label
        // placement, and every candidate is equally empty.
        var wildCapital = wilderness.Counties.FirstOrDefault();
        if (wildCapital is not null)
        {
            sb.Append("# The wilderness realm. Titular: it exists so unsettled land has a name.\n\n");
            sb.Append($"{WildernessMap.TitleKey} = {{\n");
            sb.Append("    color = { 108 104 96 }\n");
            sb.Append($"    capital = {wildCapital.Key}\n");
            sb.Append("    landless = yes\n");
            sb.Append("    definite_form = yes\n");

            // Stops the holder being announced as "King of the Wilderness" everywhere a ruler's
            // style is printed. Vanilla sets the same flag on k_orthodox for the same reason.
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

    /// <summary>Coverage per terrain class, as a share of land — the quickest read on whether a
    /// climate rule has gone wrong (all desert, no forest, and so on).</summary>
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

    /// <summary>
    /// The terrain class of each province, by majority vote over its pixels.
    ///
    /// This used to sample a single point — the province's seed cell — and apply the result to the
    /// whole province, which is how one river pixel under a seed turned an entire county into
    /// floodplains. A vote cannot do that: a feature has to actually dominate the ground before it
    /// names the province. Beaches are excluded from the vote because a coastal province is not a
    /// beach province; the sand is a material, not a terrain type.
    ///
    /// Indexed by province id, so element 0 is unused.
    /// </summary>
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

            // A province made entirely of coastline has no winner; plains is CK3's default_land.
            result[id] = best < 0 ? TerrainClass.Plains : (TerrainClass)best;
        }

        return result;
    }

    /// <summary>
    /// common/province_terrain. Without this CK3 logs a terrain error for every province.
    /// </summary>
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

    /// <summary>
    /// Minimal province history. Every county needs a culture and faith or CK3 falls back and
    /// complains; holdings are what make a barony playable.
    ///
    /// Culture and faith are read per county rather than per barony, so a county is never split
    /// between two peoples — the partitions are county-grained by construction and this is where
    /// that shows.
    /// </summary>
    /// <summary>
    /// Minimal province history. Every county needs a culture and faith or CK3 falls back and
    /// complains; holdings are what make a barony playable.
    ///
    /// Culture and faith are read per county rather than per barony, so a county is never split
    /// between two peoples — the partitions are county-grained by construction and this is where
    /// that shows.
    ///
    /// World Centers instantiate their generated Wonder / Special Building directly in history.
    /// </summary>
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

        // Index World Centers by their capital barony for fast O(1) lookup
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

                // A wilderness county is one wilderness holding in its capital and nothing at all
                // in the rest. Not "poorer holdings": none. That shape is what the colonisation
                // scripts expect to find and what they undo — colonize_county_effect turns the
                // capital into a settlement and leaves the empty baronies for the colony to grow
                // into, so a wilderness county that shipped with a castle in barony three would
                // strand it there forever.
                string holding = wild
                    ? (i == 0 ? "wilderness_holding" : "none")
                    : MapGen.Development.Holding(i, terrain, level, government, rng);

                counts[holding] = counts.GetValueOrDefault(holding) + 1;

                sb.Append($"{barony.ProvinceId} = {{\n");
                sb.Append($"    culture = {cultureKey}\n");
                sb.Append($"    religion = {faith}\n");
                sb.Append($"    holding = {holding}\n");

                // If this barony is the seat of a World Center, pre-build its Wonder
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

    private static void WriteLocalisation(
        string modDir,
        List<Title> empires,
        Dictionary<int, string> waterNames,
        ProvinceMap provinces,
        int[] order,
        int landCount,
        int riverCount)
    {
        string dir = Path.Combine(modDir, "localization", "english");
        Directory.CreateDirectory(dir);

        var sb = new StringBuilder();
        sb.Append("l_english:\n");

        foreach (var title in Titles.Flatten(empires))
        {
            sb.Append($" {title.Key}: \"{title.Name}\"\n");
            if (title.Tier == "b" && title.ProvinceId > 0)
            {
                sb.Append($" PROV{title.ProvinceId}: \"{title.Name}\"\n");
                sb.Append($" prov_{title.ProvinceId}: \"{title.Name}\"\n");
            }
        }

        for (int id = landCount + 1; id <= provinces.Count; id++)
        {
            string name = waterNames.GetValueOrDefault(id, id <= riverCount ? $"River {id}" : $"Sea of {id}");
            string prefix = id <= riverCount ? "river" : "sea";

            sb.Append($" PROV{id}: \"{name}\"\n");
            sb.Append($" {prefix}_{id}: \"{name}\"\n");
        }

        ParadoxText.WriteBom(Path.Combine(dir, "gen_titles_l_english.yml"), sb.ToString());
    }

    /// <summary>
    /// Vanilla data that is bound to the old map must be neutralised, by shadowing each file
    /// with an empty one of the same name. history/struggles and history/situations matter
    /// especially: they run start_struggle / start_situation during history load and scope into
    /// base-game regions that no longer exist, which kills the load outright.
    ///
    /// This is the blunt approach and it *will* produce script errors, because vanilla and DLC
    /// content hardcodes title keys that now have no declaration. The proper fix is to
    /// re-declare those identifiers rather than blank them — see the notes on
    /// CompatibilityWriter. This gets us to a first load so the error log can be read.
    /// </summary>
    private static void BlankVanillaData(string modDir, string gameDir)
    {
        string[] targets =
        [
            // Vanilla ships ELEVEN landed_titles files, not one. Replacing only
            // 00_landed_titles.txt leaves 02_china.txt and friends declaring thousands of
            // baronies whose `province =` ids no longer exist, which produces
            // "has no province defined" / "can't have a holding" for every one of them.
            Path.Combine("common", "landed_titles"),

            // 01_province_properties.txt assigns terrain to province ids up to ~14k; every one
            // past our count logs "lies outside the maximum number of Provinces available".
            Path.Combine("common", "province_terrain"),

            // These name vanilla counties and duchies that no longer exist.
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

            // Vanilla's 52 challenge characters each name a vanilla title and a portrait, so on a
            // generated map every one logs "has invalid 'title' or 'target_title' scripted" plus
            // "has no portrait in database". They are frontend data, which matters more than the
            // error count: the frontend is built immediately after history loading, and that is
            // exactly where the load dies. Note this is a *third* directory under
            // common/bookmarks — bookmarks, challenge_characters and groups — and only `groups`
            // must survive, because our bookmark attaches to vanilla's bm_group_867.
            Path.Combine("common", "bookmarks", "challenge_characters"),

            Path.Combine("common", "bookmark_portraits"),

            // Dynamic coat of arms definitions name vanilla titles directly, so on a generated
            // map every one logs "Could not find title 'k_england' for dynamic coat of arms
            // definition" and the arms system is left holding a null title while it builds arms
            // for the world. A Game of Thrones — a shipping total conversion on this exact game
            // version — solves it the same way: its 00_dynamic_coas.txt is the single line
            // "#AGOT Disabled", plus a replace_path on the directory.
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
