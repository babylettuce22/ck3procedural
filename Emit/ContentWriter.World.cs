using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// The world as <see cref="ContentWriter.BuildWorld"/> decided it: every social layer from the
/// province terrain vote to the water names, before a single file of it is written.
///
/// Split out of <see cref="ContentWriter.WriteAll"/>, where these stages used to sit interleaved
/// with the writers that emit them. Nothing about them changed in the move — every stage still
/// seeds its own stream from <see cref="MapConfig.Seed"/> and runs in the order it always ran —
/// and the four file writes that used to sit between them now run straight after, in the same
/// order relative to every other write. What the split buys is a world that exists without a mod
/// folder: something can now hold it, look at it and change it before anything is written.
///
/// Only the decisions a later stage cannot re-derive are here. Holdings, retinues, the calendar,
/// prehistory and the rulers are still decided inside the write, where they always were.
/// </summary>
public sealed record WorldModel
{
    public required TerrainClass[] ProvinceTerrain { get; init; }
    public required VanillaVocabulary Vocabulary { get; init; }

    /// <summary>Every county, in <see cref="Titles.Flatten"/> order.</summary>
    public required List<Title> Counties { get; init; }

    /// <summary>The export's own government per state, or null without one.</summary>
    public Dictionary<int, string>? StateGovernments { get; init; }

    /// <summary>The final development: the second pass, the one that sees world centres.</summary>
    public required Dictionary<Title, int> Development { get; init; }
    public required WildernessMap Wilderness { get; init; }
    public Dictionary<(string Culture, string Government), string>? TierForms { get; init; }

    /// <summary>Set on a world of vanilla titles; null otherwise.</summary>
    public VanillaTitles.Plan? TitlePlan { get; init; }

    /// <summary>With the unsettled culture already added when there is wilderness.</summary>
    public required CultureMap Cultures { get; init; }
    public required EthnicityMap Ethnicities { get; init; }
    public required WorldCenterMap WorldCenters { get; init; }
    public required RealmMap Realms { get; init; }
    public required GovernmentMap Governments { get; init; }

    /// <summary>
    /// The governments the generated realms have. The same object as <see cref="Governments"/>
    /// unless a history was applied, when those follow the applied realms and this stays what every
    /// cached layer was decided from — cultivation, the steppe, the silk road, faiths, regiments and
    /// the city models. See <see cref="ContentWriter.ApplyRealms"/>.
    /// </summary>
    public required GovernmentMap GeneratedGovernments { get; init; }

    /// <summary>
    /// Under an applied history, the ruling seats that carry a house from the world the history
    /// started from — see <see cref="AppliedHistory.LineageFor"/>. Null for a generated world.
    /// </summary>
    public IReadOnlyDictionary<Title, AppliedHistory.Lineage>? Lineage { get; init; }

    /// <summary>One government map per additional bookmark, or null without them.</summary>
    public Dictionary<int, GovernmentMap>? EraGovernments { get; init; }
    public double? HegemonShare { get; init; }

    /// <summary>With the unsettled faith already added when there is wilderness.</summary>
    public required FaithMap Faiths { get; init; }
    public required SteppeMap Steppe { get; init; }
    public required FrontierMap Frontier { get; init; }
    public required CrossingMap Crossings { get; init; }

    /// <summary>Barony keys by province id as they stood when the crossings were found, which is
    /// what map_data/adjacencies.csv names each crossing's ends by.</summary>
    public required Dictionary<int, string> CrossingNames { get; init; }
    public required RouteNetwork Routes { get; init; }
    public required SilkRoadMap SilkRoad { get; init; }
    public required Dictionary<int, string> WaterNames { get; init; }
}

public static partial class ContentWriter
{
    /// <summary>
    /// Decides the world's social layers — see <see cref="WorldModel"/> — and writes nothing.
    /// Takes no mod folder on purpose, so that it cannot. The game folder is read, for vanilla's
    /// vocabulary and catalogue.
    ///
    /// Mutates what it is given exactly as the write always has: titles are named and seated,
    /// the terrain raster and the province vote are cultivated, and an Azgaar import is bound.
    /// </summary>
    public static WorldModel BuildWorld(string gameDir, MapConfig cfg,
            ProvinceMap provinces, int[] order, int baronyCount, int landCount, int riverCount,
            List<Title> empires, TerrainData terra, TerrainClassifier.Result classified,
            MapGen.Drainage? drainage = null, MapGen.AzgaarImport? azgaar = null,
            AppliedHistory? applied = null)
    {
        var terrain = classified.Terrain;
        var provinceElevation = terra.ProvinceElevation;

        var provinceTerrain = Core.Stage.Time("province terrain vote", () =>
        {
            ReportTerrain(terrain);
            return ProvinceTerrain(cfg, provinces, order, terrain, provinceElevation, landCount,
                baronyCount, report: true);
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

        // Which barony is each county's seat. Before naming, because a seat may take its county's
        // name; after the binding, because on an imported map the seat is where the chief burg is.
        // Everything downstream reads the seat as Children[0]. See MapGen/Capitals.cs.
        Core.Stage.Time("county seats", () =>
        {
            int moved = MapGen.Capitals.SeatCounties(empires, provinces, order, baronyCount, landCount,
                provinceTerrain, drainage, azgaar);
            Console.WriteLine($"  county seats: {moved} of {counties.Count} moved off the cluster seed");
        });

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
                new Rng(cfg.Seed ^ 0x0DE7), null, azgaar);
            ReportDevelopment(levels);
            return levels;
        });

        var wilderness = Core.Stage.Time("wilderness", () => MapGen.Wilderness.Build(counties,
            provinces, order, landCount, provinceTerrain, development, cfg, new Rng(cfg.Seed ^ 0x1D17),
            azgaar));

        Dictionary<(string Culture, string Government), string>? tierForms = null;

        // Set by the culture stage on a world of vanilla titles; read by the realm and faith stages.
        VanillaTitles.Plan? titlePlan = null;

        var cultures = Core.Stage.Time("cultures", () =>
        {
            var map = MapGen.Cultures.Build(empires, provinces, order, landCount, provinceTerrain,
                development, vocabulary, cfg, new Rng(cfg.Seed ^ 0x0C17), azgaar,
                MapGen.ClothingClimate.ByProvince(classified.Field, provinces, order, landCount));

            if (azgaar is not null)
            {
                int renamed = MapGen.AzgaarNaming.RenameCultures(azgaar, map);
                Console.WriteLine($"  azgaar: {renamed} of {map.Cultures.Count} cultures named from the export");
            }

            // A world of vanilla peoples keeps the cultural geography just grown and swaps who lives
            // in it. Here, before anything is named, so titles, rivers and houses come out in those
            // peoples' own languages. See MapGen/VanillaIdentities.cs.
            var grown = map;
            if (cfg.ContentSource == MapConfig.ContentSourceMode.VanillaWorld)
                map = VanillaIdentities.SettleCultures(map, VanillaCatalog.Read(gameDir),
                    CountyPosition(provinces, order, landCount), provinces.Width, provinces.Height,
                    new Rng(cfg.Seed ^ 0x7A11));

            // Which word each culture-and-government will render for a rank, decided once here so
            // the title names below can leave that word out rather than repeating it.
            tierForms = azgaar is null || stateGovernments is null
                ? null
                : TitleTierWriter.FormsByCulture(azgaar, map, stateGovernments);

            // Every culture's words for its realms, and every state's word for its own title,
            // decided here and stored on the objects — before naming, which leaves a state's word
            // out of its name because the tier will say it — and written out by TitleTierWriter
            // further down.
            // Declared cultures only: a vanilla culture already has its words, in vanilla's
            // title-holder flavourization, and a drawn ladder would override them.
            TitleTierWriter.Assign(map.Declared(), new Rng(cfg.Seed ^ 0x7117), tierForms, azgaar);

            // Worked out for the whole hierarchy at once rather than title by title, so each state
            // and burg goes to the title that actually contains most of it — see AzgaarNaming.
            var borrowed = azgaar is null
                ? null
                : MapGen.AzgaarNaming.TitleNames(azgaar, empires, tierForms, map, stateGovernments);

            Titles.AssignNames(empires, map, new Rng(cfg.Seed ^ 0x7171), borrowed,
                               Titles.HegemonyOf(empires));

            if (borrowed is not null)
                Console.WriteLine($"  azgaar: {borrowed.Count} of {Titles.Flatten(empires).Count()} " +
                                  "titles named from the export, the rest from its name bases");

            // Then the real world laid over it: every title becomes a vanilla title, and every
            // county takes the culture its vanilla county had at the start date. After naming,
            // because the generated names are only the fallback for a title vanilla ran out of.
            // See MapGen/VanillaTitles.cs.
            if (cfg.ContentSource == MapConfig.ContentSourceMode.VanillaWorld)
            {
                var catalog = VanillaCatalog.Read(gameDir);
                titlePlan = VanillaTitles.Match(empires, grown, development, catalog,
                    CountyPosition(provinces, order, landCount), cfg.EraYear, MapGen.SilkRoad.ReservedCountyKeys,
                    new Rng(cfg.Seed ^ 0x7A13), cfg.VanillaRegionKeys);

                if (titlePlan is not null)
                {
                    VanillaTitles.Apply(titlePlan, empires);
                    map = VanillaIdentities.RecastCultures(map,
                        titlePlan.State.ToDictionary(kv => kv.Key, kv => kv.Value.Culture),
                        titlePlan.Projected, development, catalog, new Rng(cfg.Seed ^ 0x7A14));
                }
            }

            return map;
        });

        var ethnicities = Core.Stage.Time("ethnicities", () => MapGen.Ethnicities.Build(
            cultures.Heritages, cultures.Cultures, provinceTerrain, cfg, new Rng(cfg.Seed ^ 0x38F1),
            wilderness));

        var worldCenters = Core.Stage.Time("world centers", () => WorldCenterMap.Build(
            counties, provinces, order, landCount, provinceTerrain, cultures, wilderness, cfg, new Rng(cfg.Seed ^ 0x93FA)));

        // A culture's name, heritage, tongue, name lists and traditions are all settled by here, so
        // the peoples are shown now, with the wonders. Read only; nothing when nobody watches.
        // See Showcase.
        Core.Showcase.Publish(() => ShowcaseItems.Cultures(cultures));
        Core.Showcase.Publish(() => ShowcaseItems.Wonders(worldCenters, gameDir));

        development = Core.Stage.Time("development", () =>
        {
            var levels = MapGen.Development.ForCounties(counties, provinceTerrain, cfg,
                new Rng(cfg.Seed ^ 0x0DE7), worldCenters, azgaar);
            ReportDevelopment(levels);
            return levels;
        });

        // Which county is each duchy's capital, and up the tiers from there. After the final
        // development pass so a world centre is its duchy's capital; nothing above county is
        // named from its child order, so nothing is renamed by this. See MapGen/Capitals.cs.
        Core.Stage.Time("realm capitals", () =>
        {
            int moved = MapGen.Capitals.SeatRealms(empires, development, provinces, order, baronyCount,
                worldCenters, azgaar);
            Console.WriteLine($"  realm capitals: {moved} titles above county had their capital moved");

            // A vanilla title is ruled from vanilla's capital when this map has it: France from Paris.
            if (titlePlan is not null)
                Console.WriteLine($"  realm capitals: {VanillaTitles.SeatLikeVanilla(empires, VanillaCatalog.Read(gameDir))} " +
                                  "vanilla titles seated on their own vanilla capital");
        });

        // On a world of vanilla titles the countries are vanilla's own at the start date — unless an
        // Azgaar export drew its own, which is its call to make.
        var vanillaRealms = titlePlan is not null && azgaar is null
            ? VanillaTitles.Countries(titlePlan, VanillaCatalog.Read(gameDir), wilderness, empires, development)
            : null;

        // With a history applied the world is written in a later year, but the realms it replaces
        // are the ones the formation grew for the year the history was run on from — the formation
        // counts its epochs back from the start date, and a moved start would grow a different map.
        var formationCfg = applied is null ? cfg : cfg.AtStartYear(applied.FromYear);
        var realms = Core.Stage.Time("realms", () => Realms.Build(
                    empires, development, wilderness, formationCfg, new Rng(cfg.Seed ^ 0x2E17), provinces, order,
                    baronyCount, azgaar, cultures, vanillaRealms));

        // After the realm pass, never during it — see Realms.CrownHegemon for why granting it any
        // earlier would have made one ruler the liege of the whole map.
        if (cfg.StartingHegemony) Realms.CrownHegemon(realms, empires, wilderness);

        // For anyone watching the run; reads only, and does nothing when nobody is. See Showcase.
        Core.Showcase.Publish(() => ShowcaseItems.Realms(realms));

        if (titlePlan is not null)
            Console.WriteLine($"  vanilla titles: {VanillaTitles.Fragmentation(empires, realms)}");

        var coastal = MapGen.ProvinceSurvey.Take(provinces, order, baronyCount, null).Coastal;
        var governments = Core.Stage.Time("governments", () => MapGen.Governments.Build(
            empires, counties, realms, provinceTerrain, coastal, development, cultures,
            worldCenters, cfg, new Rng(cfg.Seed ^ 0x6017), azgaar, stateGovernments));

        Console.WriteLine("  governments: " + string.Join(", ",
            governments.Tally(counties, wilderness).Select(g => $"{g.Count} {g.Government[..^11]}")));

        // The same cascade for each additional bookmark: its own map, at its own advancement and
        // with the development it had then, on its own stream. Nothing above reads these. A world
        // that starts with a hegemon has one on a later date too, crowned and sworn to the same way.
        Dictionary<int, GovernmentMap>? eraGovernments = null;
        if (cfg.UsesAdditionalBookmarks)
        {
            eraGovernments = [];
            foreach (int year in cfg.AdditionalBookmarkYears)
            {
                var eraRealms = realms.EraMaps?.GetValueOrDefault(year) ?? realms;
                bool crown = cfg.StartingHegemony && year > cfg.StartYear && !ReferenceEquals(eraRealms, realms);
                if (crown) Realms.CrownHegemon(eraRealms, empires, wilderness);

                var eraDevelopment = development.ToDictionary(kv => kv.Key,
                    kv => BookmarkEras.EraDevelopment(kv.Value, cfg, year));
                eraGovernments[year] = MapGen.Governments.Build(empires, counties, eraRealms, provinceTerrain,
                    coastal, eraDevelopment, cultures, worldCenters, cfg.AtAdvancement(cfg.EraYearAt(year)),
                    new Rng(cfg.Seed ^ 0x6017 ^ year), azgaar, stateGovernments);

                if (crown) Realms.ExpandHegemonRealm(eraRealms, empires, wilderness);

                Console.WriteLine($"  governments in {year} (as advanced as {cfg.EraYearAt(year)}): " + string.Join(", ",
                    eraGovernments[year].Tally(counties, wilderness).Select(g => $"{g.Count} {g.Government[..^11]}")));
            }
        }

        // After the governments, never before: this brings whole kingdoms under the hegemon, and
        // governments are decided one per realm grouped by top liege — done first, every absorbed
        // kingdom would have been swept into the hegemon's government. See ExpandHegemonRealm.
        if (cfg.StartingHegemony) Realms.ExpandHegemonRealm(realms, empires, wilderness);

        // Read once the homage pass is done, because it is what the Dynastic Cycle will measure the
        // hegemon against every year — see DynasticCycleWriter for the thresholds it tunes.
        double? hegemonShare = cfg.StartingHegemony
            ? Realms.HegemonDeJureShare(realms, empires, wilderness)
            : null;

        var faiths = Core.Stage.Time("faiths", () => MapGen.Faiths.Build(empires, provinces, order,
            landCount, provinceTerrain, development, governments, vocabulary, wilderness, cfg, worldCenters,
            new Rng(cfg.Seed ^ 0x0FA1), azgaar, cultures));

        // The Tier 1 renamer only runs when the structure is still ours. Built from the export's
        // own tree, the faiths already carry its names, and a rename by majority vote could only
        // disagree with the geography they were cut from.
        if (azgaar is not null && !faiths.ImportedStructure)
        {
            var (namedFaiths, namedReligions) = MapGen.AzgaarNaming.RenameFaiths(azgaar, faiths);
            Console.WriteLine($"  azgaar: {namedFaiths} of {faiths.Faiths.Count} faiths and " +
                              $"{namedReligions} of {faiths.Religions.Count} religions named from the export");
        }

        // The religious geography just grown, settled with vanilla faiths chosen by how the vanilla
        // peoples now living there pray. Before the unsettled faith is added, which stays ours.
        if (cfg.ContentSource == MapConfig.ContentSourceMode.VanillaWorld)
            faiths = Core.Stage.Time("vanilla faiths", () => VanillaIdentities.SettleFaiths(faiths, cultures,
                development, VanillaCatalog.Read(gameDir), vocabulary, new Rng(cfg.Seed ^ 0x7A12),
                titlePlan?.State.ToDictionary(kv => kv.Key, kv => kv.Value.Faith)));

        if (wilderness.Count > 0)
        {
            var unsettledCulture = MapGen.Cultures.CreateUnsettled(
                cultures.Heritages[0], vocabulary, cfg, new Rng(cfg.Seed ^ 0x0C55));

            cultures.Cultures.Add(unsettledCulture);
            foreach (var county in wilderness.Counties) cultures.ByCounty[county] = unsettledCulture;

            var (unsettledReligion, unsettledFaith) = MapGen.Faiths.CreateUnsettled(vocabulary,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase), cfg, new Rng(cfg.Seed ^ 0x0FA5));

            faiths.Religions.Add(unsettledReligion);
            faiths.Faiths.Add(unsettledFaith);
            foreach (var county in wilderness.Counties) faiths.ByCounty[county] = unsettledFaith;
        }

        // Every faith now exists. Each generated one is pointed at its own rendered icon; the
        // vanilla icon Faiths.Build drew stays in place when this is off, so the seed stream and
        // the rest of the world are the same either way. Rendered with the religion files.
        if (cfg.GenerateFaithIcons) MapGen.FaithIcons.Claim(faiths);

        // Who fights and who inherits, settled here because it is the first point at which both
        // halves of the question exist: the culture was drawn before any faith did, and the answer
        // has to be the same one its people's religion gives. See MapGen/Cultures.AlignGender.
        Core.Stage.Time("gender", () => MapGen.Cultures.AlignGender(cultures.Declared(), faiths, vocabulary,
            new Rng(cfg.Seed ^ 0x6E1D)));

        // Farmland and oases, placed from settlement and drainage rather than from climate. Runs
        // here, after every social layer has been decided, so nothing reads a terrain that only
        // exists *because* of the settlement: development, government, culture and faith all see
        // the pre-cultivation map. Both the pixel raster and the province vote are rewritten, so
        // the painted ground and common/province_terrain cannot disagree. See MapGen/Cultivation.cs.
        Core.Stage.Time("cultivation", () => MapGen.Cultivation.Apply(cfg, provinces, order,
            landCount, terrain, provinceTerrain, counties, governments, development, wilderness,
            drainage, provinceElevation, new Rng(cfg.Seed ^ 0x0FA2)));

        // Where the Great Steppe situation lives. After cultivation, so it reads the ground as it
        // will be painted; after governments, so every nomad is inside it whatever its ground —
        // a nomad outside the situation cannot migrate. See MapGen/Steppe.cs.
        var steppe = Core.Stage.Time("great steppe", () => MapGen.Steppe.Build(counties, provinces,
            order, landCount, provinceTerrain, governments, new Rng(cfg.Seed ^ 0x57E9)));

        // Where the Wilds situation lives: one frontier per connected stretch of wilderness, plus
        // the settled counties one hop out. Deterministic from the wilderness map, no rng. See
        // MapGen/Frontier.cs.
        var frontier = Core.Stage.Time("frontier", () => MapGen.Frontier.Build(counties, provinces,
            order, landCount, provinceTerrain, wilderness));

        // Where an army can march across water: straits and major-river crossings, written into
        // map_data/adjacencies.csv by WriteAll once map_data has joined. See MapGen/Crossings.cs.
        Dictionary<int, string> crossingNames = [];
        var crossings = Core.Stage.Time("crossings", () =>
        {
            var found = MapGen.Crossings.Build(empires, provinces, order, baronyCount, cfg);
            // Taken here, where the crossings were found, rather than where the file is written:
            // the silk road below gives some titles vanilla's keys, and the adjacency comments
            // name the baronies as they stood at this point.
            crossingNames = Titles.Flatten(empires)
                .Where(t => t.Tier == "b" && t.ProvinceId > 0)
                .ToDictionary(t => t.ProvinceId, t => t.Key);
            int strait = (int)Math.Round(cfg.Scaled(cfg.StraitPixelsAtVanilla));
            Console.WriteLine($"  crossings: {found.Straits} straits up to {strait} px and " +
                              $"{found.Rivers} river crossings up to {strait * MapGen.Crossings.RiverWidthFactor} px wide");
            return found;
        });

        // The roads and sea lanes between markets. After cultivation and the capitals, since a
        // road runs over the ground as it will be painted and between the seats as they were
        // just chosen; after the crossings, which it walks. See MapGen/Routes.cs.
        var routes = Core.Stage.Time("routes", () => MapGen.Routes.Build(empires, provinces, order,
            baronyCount, provinceTerrain, drainage, wilderness, crossings));

        // The Silk Road, laid along the network. Before the landed titles and localisation are
        // written, and it has to be: six bazaar counties take vanilla's county keys here, so
        // that the base-game script naming them finds this map's markets. See MapGen/SilkRoad.cs.
        var silkRoad = Core.Stage.Time("silk road", () => MapGen.SilkRoad.Build(empires, routes, crossings,
            development, worldCenters, governments, provinces, order, baronyCount));

        // The traced courses go in so each river can be named along its length rather than by the
        // latitude of its provinces — see WaterNaming.GroupRiverProvinces.
        var waterNames = Core.Stage.Time("water naming", () => WaterNaming.Generate(
            provinces, order, landCount, riverCount, cultures, empires, cfg,
            new Rng(cfg.Seed ^ 0x5EAE), terra.MajorRiversList, azgaar));
        Core.Showcase.Publish(() => ShowcaseItems.Waters(waterNames));

        // History run on in the History workspace replaces the realms the formation grew — last,
        // after everything above has been decided from the generated ones. Faiths.Build reads the
        // governments, and a religion's tribal share gates draws that every later faith's names and
        // tenets come off, so faiths built from applied realms would come back renamed; cultivation,
        // the steppe, the silk road and the regiments read them too. All of those are the generated
        // world's, kept as they were written, so that applying a history can re-emit the few files
        // that follow who rules what instead of rewriting the mod — see ApplyHistory, which calls
        // the same ApplyRealms. The start date is moved by the caller: see MapConfig.AtStartYear.
        var generatedGovernments = governments;
        IReadOnlyDictionary<Title, AppliedHistory.Lineage>? lineage = null;
        if (applied is not null)
            (realms, governments, hegemonShare, lineage) = ApplyRealms(applied, realms, cfg, empires, counties,
                provinces, order, baronyCount, provinceTerrain, development, cultures, worldCenters,
                wilderness, azgaar, stateGovernments);

        return new WorldModel
        {
            GeneratedGovernments = generatedGovernments,
            Lineage = lineage,
            ProvinceTerrain = provinceTerrain,
            Vocabulary = vocabulary,
            Counties = counties,
            StateGovernments = stateGovernments,
            Development = development,
            Wilderness = wilderness,
            TierForms = tierForms,
            TitlePlan = titlePlan,
            Cultures = cultures,
            Ethnicities = ethnicities,
            WorldCenters = worldCenters,
            Realms = realms,
            Governments = governments,
            EraGovernments = eraGovernments,
            HegemonShare = hegemonShare,
            Faiths = faiths,
            Steppe = steppe,
            Frontier = frontier,
            Crossings = crossings,
            CrossingNames = crossingNames,
            Routes = routes,
            SilkRoad = silkRoad,
            WaterNames = waterNames,
        };
    }
}
