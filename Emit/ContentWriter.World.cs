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

    /// <summary>Under an applied history, the realms' dated predecessors; null for a generated world.
    /// See <see cref="AppliedHistory.PastRulersFor"/>.</summary>
    public List<PastRuler>? PastRulers { get; init; }

    /// <summary>
    /// Under an applied history, the colour the History workspace gave each realm, by the seat it is
    /// ruled from — see <see cref="AppliedHistory.Colours"/>. Presentation only: nothing written
    /// reads it. Null for a generated world.
    /// </summary>
    public IReadOnlyDictionary<Title, (byte R, byte G, byte B)>? RealmColours { get; init; }

    /// <summary>
    /// Under an applied history, the wilderness it left and the county cultures, faiths and wilds
    /// that follow — what the realm layer writes from. <see cref="Wilderness"/>, <see cref="Cultures"/>,
    /// <see cref="Faiths"/> and <see cref="Frontier"/> stay the generated ones every cached layer
    /// was decided from. Null for a generated world, whose realm layer reads those.
    /// </summary>
    internal ContentWriter.WildsLayer? AppliedWilds { get; init; }

    /// <summary>
    /// Under an applied history, the wars, truces and claims it left, which the prehistory writes
    /// in place of the ones it invents. Null for a generated world, or a history that carried none.
    /// </summary>
    internal SimDiplomacy? AppliedDiplomacy { get; init; }

    /// <summary>
    /// Under an applied history, the past ruler each seat's ruler is the child of — the dynasty tree's
    /// link to the living. Null for a generated world, whose parents the prehistory invents.
    /// </summary>
    internal IReadOnlyDictionary<Title, PastRuler>? SeatParents { get; init; }

    /// <summary>Under an applied history, what the chronicle remembers of it; null for a generated world.</summary>
    internal ContentWriter.RememberedPast? AppliedPast { get; init; }

    /// <summary>What the realm layer writes from: <see cref="AppliedWilds"/>, or the generated maps.</summary>
    internal ContentWriter.WildsLayer RealmLayer
        => AppliedWilds ?? ContentWriter.WildsLayer.Unmoved(Wilderness, Cultures, Faiths, Frontier);

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

    /// <summary>The named places shaped by the land, for the modifiers, regions and flat-map
    /// lettering. See MapGen/Terrain/Landmarks.cs.</summary>
    public IReadOnlyList<Landmark> Landmarks { get; init; } = [];
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
        // Everything downstream reads the seat as Children[0]. See MapGen/Titles/Capitals.cs.
        Core.Stage.Time("county seats", () =>
        {
            int moved = MapGen.Capitals.SeatCounties(empires, provinces, order, baronyCount,
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

        bool racesOn = cfg.EnableFantasyEthnicities && cfg.RaceMode != MapConfig.FantasyRaceMode.HumanOnly;

        // Each culture's race as the culture stage's preliminary ethnicity pass saw it, kept to prove
        // the real pass agrees. Null when that pass did not run.
        Dictionary<MapGen.Culture, MapGen.RaceArchetype>? preliminaryRaces = null;

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

            // Fantasy peoples speak their race's tongue. The race has to be known before anything is
            // named in these languages, so the ethnicity pass runs once here, quietly, to say who is
            // who; the real pass below repeats it with the same seed and inputs and reaches the same
            // races (nothing it decides reads a name), this time with the new names in hand. Not on
            // an Azgaar map, whose peoples already speak as the export drew them. See
            // Cultures.SpeakAsRace.
            if (azgaar is null && racesOn)
            {
                var races = MapGen.Ethnicities.Build(map.Heritages, map.Cultures, provinceTerrain, cfg,
                    new Rng(cfg.Seed ^ 0x38F1), wilderness, quiet: true);
                preliminaryRaces = map.Cultures.ToDictionary(c => c, c => races.For(c).Archetype);

                var (h, c) = MapGen.Cultures.SpeakAsRace(map, culture => races.For(culture).Archetype,
                    new Rng(cfg.Seed ^ 0x70A6));
                Console.WriteLine($"  cultures: {h} heritage(s) and {c} culture(s) now speak their race's tongue");
            }

            // A world of vanilla peoples keeps the cultural geography just grown and swaps who lives
            // in it. Here, before anything is named, so titles, rivers and houses come out in those
            // peoples' own languages. See MapGen/Vanilla/VanillaIdentities.cs.
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
                : MapGen.AzgaarNaming.TitleNames(azgaar, empires);

            Titles.AssignNames(empires, map, new Rng(cfg.Seed ^ 0x7171), borrowed,
                               Titles.HegemonyOf(empires));

            if (borrowed is not null)
                Console.WriteLine($"  azgaar: {borrowed.Count} of {Titles.Flatten(empires).Count()} " +
                                  "titles named from the export, the rest from its name bases");

            // Then the real world laid over it: every title becomes a vanilla title, and every
            // county takes the culture its vanilla county had at the start date. After naming,
            // because the generated names are only the fallback for a title vanilla ran out of.
            // See MapGen/Vanilla/VanillaTitles.cs.
            if (cfg.ContentSource == MapConfig.ContentSourceMode.VanillaWorld)
            {
                var catalog = VanillaCatalog.Read(gameDir);
                titlePlan = VanillaTitles.Match(empires, grown, catalog,
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

        // A pass barony is named for its pass. After the names above and the real-world overlay,
        // so it carries its final name and a vanilla title keeps its own. See MapGen/Terrain/MountainPasses.cs.
        int passesNamed = MapGen.MountainPasses.Name(empires, provinces, order, baronyCount, cfg);
        if (passesNamed > 0) Console.WriteLine($"  mountain passes: {passesNamed} barony(ies) named for their pass");

        // Humans are placed by the same climate the cultures were dressed for, and dressed over
        // vanilla's own ethnicities — see MapGen/Peoples/HumanLooks.cs.
        var humanLooks = new MapGen.HumanLooks.Inputs(
            MapGen.ClothingClimate.ByProvince(classified.Field, provinces, order, landCount),
            CountyPosition(provinces, order, landCount),
            MapGen.HumanLooks.Read(gameDir, vocabulary));

        var ethnicities = Core.Stage.Time("ethnicities", () => MapGen.Ethnicities.Build(
            cultures.Heritages, cultures.Cultures, provinceTerrain, cfg, new Rng(cfg.Seed ^ 0x38F1),
            wilderness, humanLooks: humanLooks));

        // The re-voicing above is only right if this pass agrees with the one it was based on. It
        // should by construction; a future change that let a name steer the race roll would break
        // that silently, leaving dwarves speaking Sylvan, so it is checked rather than assumed.
        if (preliminaryRaces is not null)
        {
            int drifted = preliminaryRaces.Count(kv => ethnicities.For(kv.Key).Archetype != kv.Value);
            if (drifted > 0)
                Console.WriteLine($"  WARNING: {drifted} culture(s) changed race between the naming pass and " +
                                  "the ethnicity pass — their tongue no longer matches their race");
        }

        // What each race brings to its culture regardless of ground — see Cultures.RaceTraditions.
        // Before the showcase, which shows traditions.
        if (racesOn)
        {
            int granted = MapGen.Cultures.GiveRaceTraditions(cultures.Declared().Cultures,
                c => ethnicities.For(c).Archetype, vocabulary, cfg, new Rng(cfg.Seed ^ 0x7AD1));
            Console.WriteLine($"  cultures: {granted} race tradition(s) granted");
        }

        var worldCenters = Core.Stage.Time("world centers", () => WorldCenterMap.Build(
            counties, provinces, order, landCount, provinceTerrain, cultures, wilderness, cfg, new Rng(cfg.Seed ^ 0x93FA)));

        // A culture's name, heritage, tongue, name lists and traditions are all settled by here, so
        // the peoples are shown now, with the wonders: their map first, so the pins land on it.
        // Read only; nothing when nobody watches. See Showcase.
        Core.Showcase.Picture("Cultures", () => ShowcaseItems.CulturePicture(cultures, provinces, order,
            baronyCount, landCount, empires, wilderness));
        Core.Showcase.Publish(() => ShowcaseItems.Cultures(cultures, new(provinces, order), ethnicities, wilderness));
        Core.Showcase.Publish(() => ShowcaseItems.Wonders(worldCenters, gameDir, new(provinces, order)));

        development = Core.Stage.Time("development", () =>
        {
            var levels = MapGen.Development.ForCounties(counties, provinceTerrain, cfg,
                new Rng(cfg.Seed ^ 0x0DE7), worldCenters, azgaar);
            ReportDevelopment(levels);
            return levels;
        });

        // Which county is each duchy's capital, and up the tiers from there. After the final
        // development pass so a world centre is its duchy's capital; nothing above county is
        // named from its child order, so nothing is renamed by this. See MapGen/Titles/Capitals.cs.
        Core.Stage.Time("realm capitals", () =>
        {
            int moved = MapGen.Capitals.SeatRealms(empires, development, provinces, order, baronyCount,
                worldCenters, azgaar, wilderness);
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

        // Nor does it place the additional bookmarks: they sit around the applied year, not the one
        // the formation runs to, and are read from its frames and the history's (see HistoryEras).
        if (applied is not null) formationCfg.AdditionalBookmarks = false;
        var realms = Core.Stage.Time("realms", () => Realms.Build(
                    empires, development, wilderness, formationCfg, new Rng(cfg.Seed ^ 0x2E17), provinces, order,
                    baronyCount, azgaar, cultures, vanillaRealms));

        // After the realm pass, never during it — see Realms.CrownHegemon for why granting it any
        // earlier would have made one ruler the liege of the whole map.
        if (cfg.StartingHegemony) Realms.CrownHegemon(realms, empires, wilderness);

        // For anyone watching the run; reads only, and does nothing when nobody is. See Showcase.
        Core.Showcase.Publish(() => ShowcaseItems.Realms(realms, new(provinces, order)));

        if (titlePlan is not null)
            Console.WriteLine($"  vanilla titles: {VanillaTitles.Fragmentation(empires, realms)}");

        var coastal = MapGen.ProvinceSurvey.Take(provinces, order, baronyCount, null).Coastal;
        var governments = Core.Stage.Time("governments", () => MapGen.Governments.Build(
            empires, counties, realms, provinceTerrain, coastal, development, cultures,
            worldCenters, cfg, new Rng(cfg.Seed ^ 0x6017), azgaar, stateGovernments));

        Console.WriteLine("  governments: " + string.Join(", ",
            governments.Tally(counties, wilderness).Select(g => $"{g.Count} {g.Government[..^11]}")));

        // The same cascade for each additional bookmark. An applied history's bookmarks are placed
        // around its own year and drawn from its own runs, so they get theirs in ApplyRealms.
        var eraGovernments = cfg.UsesAdditionalBookmarks && applied is null
            ? EraGovernments(cfg, realms, empires, counties, provinceTerrain, coastal, development, cultures,
                worldCenters, wilderness, azgaar, stateGovernments)
            : null;

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

        // On the system shipping, not on a county being wild: both dummies are born with this culture
        // and faith, and county_has_unsettled_identity_trigger reads them off the k_gen_wilderness
        // holder, on a world that starts with no wild county too (an Azgaar export that claims
        // everything). Each draws from its own stream, so adding them moves nothing else.
        if (wilderness.Count > 0 || wilderness.Ships)
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
            new Rng(cfg.Seed ^ 0x6E1D), cfg.Gender));

        // Each people's own words for its ranks, when asked for. Here because a people's home
        // religion is part of the decision and this is the first point every faith exists; off
        // every stream the rest of the world draws from, so it moves no other name.
        Core.Stage.Time("native ranks", () => MapGen.NativeTitles.Assign(cultures, faiths, cfg));

        // Clerical regions and the rites their great sees found. After the native ranks, whose
        // toggle decides whether a religion coins its see words; after the governments, since only
        // settled land gets a see. Its own streams: turning sees off moves nothing else.
        Core.Stage.Time("sees", () =>
        {
            var seeCounties = Titles.Flatten(empires).Where(t => t.Tier == "c").ToList();
            var seeGraph = MapGen.CountyNetwork.Graph(seeCounties, provinces, order, landCount, provinceTerrain,
                _ => 1.0, 0.0);
            BuildSees(faiths, seeCounties, seeGraph, realms, governments, eraGovernments, development, worldCenters,
                cultures, vocabulary, cfg, wilderness);
        });

        // Farmland and oases, placed from settlement and drainage rather than from climate. Runs
        // here, after every social layer has been decided, so nothing reads a terrain that only
        // exists *because* of the settlement: development, government, culture and faith all see
        // the pre-cultivation map. Both the pixel raster and the province vote are rewritten, so
        // the painted ground and common/province_terrain cannot disagree. See MapGen/Climate/Cultivation.cs.
        Core.Stage.Time("cultivation", () => MapGen.Cultivation.Apply(cfg, provinces, order,
            landCount, terrain, provinceTerrain, counties, governments, development, wilderness,
            drainage, provinceElevation, new Rng(cfg.Seed ^ 0x0FA2)));

        // Where the Great Steppe situation lives. After cultivation, so it reads the ground as it
        // will be painted; after governments, so every nomad is inside it whatever its ground —
        // a nomad outside the situation cannot migrate. See MapGen/Situations/Steppe.cs.
        var steppe = Core.Stage.Time("great steppe", () => MapGen.Steppe.Build(counties, provinces,
            order, landCount, provinceTerrain, governments, new Rng(cfg.Seed ^ 0x57E9)));

        // Where the Wilds situation lives: one frontier per connected stretch of wilderness, plus
        // the settled counties one hop out. Deterministic from the wilderness map, no rng. See
        // MapGen/Titles/Frontier.cs.
        var frontier = Core.Stage.Time("frontier", () => MapGen.Frontier.Build(counties, provinces,
            order, landCount, provinceTerrain, wilderness));

        // Where an army can march across water: straits and major-river crossings, written into
        // map_data/adjacencies.csv by WriteAll once map_data has joined. See MapGen/Provinces/Crossings.cs.
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
        // just chosen; after the crossings, which it walks. See MapGen/Provinces/Routes.cs.
        var routes = Core.Stage.Time("routes", () => MapGen.Routes.Build(empires, provinces, order,
            baronyCount, provinceTerrain, drainage, wilderness, crossings));

        // The Silk Road, laid along the network. Before the landed titles and localisation are
        // written, and it has to be: six bazaar counties take vanilla's county keys here, so
        // that the base-game script naming them finds this map's markets. See MapGen/Situations/SilkRoad.cs.
        var silkRoad = Core.Stage.Time("silk road", () => MapGen.SilkRoad.Build(empires, routes, crossings,
            development, worldCenters, governments, provinces, order, baronyCount));

        // The traced courses go in so each river can be named along its length rather than by the
        // latitude of its provinces — see WaterNaming.GroupRiverProvinces.
        var seaBodies = new List<(string Name, List<int> Zones)>();
        var waterNames = Core.Stage.Time("water naming", () => WaterNaming.Generate(
            provinces, order, landCount, riverCount, cultures, empires,
            new Rng(cfg.Seed ^ 0x5EAE), terra.MajorRiversList, azgaar, seaBodies));
        Core.Showcase.Publish(() => ShowcaseItems.Waters(waterNames, new(provinces, order)));

        // After the water, because a drowned crater renames the sea inside its rim after itself;
        // its own stream, so naming a landmark moves no other name. See MapGen/Terrain/Landmarks.cs.
        var landmarks = MapGen.Landmarks.Build(cfg, provinces, order, baronyCount, riverCount,
            empires, cultures, waterNames, new Rng(cfg.Seed ^ 0x1A4D), seaBodies);
        foreach (var landmark in landmarks)
            Console.WriteLine($"  landmark: {landmark.Name} ({landmark.Kind}, {landmark.Baronies.Length} baronies)");

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
        List<PastRuler>? pastRulers = null;
        IReadOnlyDictionary<Title, (byte R, byte G, byte B)>? realmColours = null;
        WildsLayer? appliedWilds = null;
        SimDiplomacy? appliedDiplomacy = null;
        IReadOnlyDictionary<Title, PastRuler>? seatParents = null;
        RememberedPast? appliedPast = null;
        if (applied is not null)
            (realms, governments, hegemonShare, lineage, pastRulers, realmColours, _, appliedWilds, appliedDiplomacy, seatParents, appliedPast, eraGovernments) = ApplyRealms(applied,
                realms, cfg, empires, counties, provinces, order, baronyCount, provinceTerrain, development, cultures,
                worldCenters, wilderness, azgaar, stateGovernments, faiths, landCount, frontier);

        return new WorldModel
        {
            GeneratedGovernments = generatedGovernments,
            Lineage = lineage,
            PastRulers = pastRulers,
            RealmColours = realmColours,
            AppliedWilds = appliedWilds,
            AppliedDiplomacy = appliedDiplomacy,
            SeatParents = seatParents,
            AppliedPast = appliedPast,
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
            Landmarks = landmarks,
        };
    }

    /// <summary>
    /// The government cascade for each additional bookmark: its own map, at its own advancement and
    /// with the development it had then, on its own stream. A world that starts with a hegemon has
    /// one on a later date too, crowned and sworn to the same way. An era map that carries its own
    /// wilderness (<see cref="RealmMap.Wilderness"/>) is crowned and tallied on it.
    /// </summary>
    internal static void BuildSees(FaithMap faiths, List<Title> seeCounties, MapGen.RegionGrowth.Graph seeGraph,
        RealmMap realms, GovernmentMap governments, Dictionary<int, GovernmentMap>? eraGovernments,
        Dictionary<Title, int> development, WorldCenterMap worldCenters, CultureMap cultures,
        VanillaVocabulary vocabulary, MapConfig cfg, WildernessMap wilderness)
    {
        // An era map that is the start date's own (a world whose realms were not grown) is copied
        // from it later by BookmarkEras, bishoprics and all, so it is not carved again.
        RealmMap? EraMap(int year) => realms.EraMaps?.GetValueOrDefault(year) is { } map && !ReferenceEquals(map, realms) ? map : null;

        // Heads of Faith on land, before the prince-bishops (who then pass these counties by) and
        // before any ruler is drawn; not gated on sees, a head is a head either way. Before the sees
        // too, so the primate see is seated in the head's own county as d_et_roma is in the Pope's:
        // its church estate then stands where the head builds, and a Grand Cathedral at his capital
        // raises it (qw 2026-10-03: estate and land in different counties, cathedral did nothing).
        // The carve reads nothing the sees decide, and its theocracies still count as settled land.
        // The start date for now: a bookmark sharing its map shares its seats, so its governments follow.
        var headPreferred = new Dictionary<MapGen.Faith, Title>();
        var headCarved = MapGen.HeadSeats.Carve(faiths, realms, governments, wilderness, development, headPreferred);
        // Every other bookmark: its own map carved (preferring the start date's seat), or the start date's
        // seats where it shares the start map (BookmarkEras copies HeadSeats with it).
        foreach (var (year, eraGovernment) in (eraGovernments ?? []).OrderBy(kv => Math.Abs(kv.Key - cfg.StartYear)).ThenBy(kv => kv.Key))
        {
            if (EraMap(year) is { } eraMap)
                MapGen.HeadSeats.Carve(faiths, eraMap, eraGovernment, eraMap.Wilderness ?? wilderness, development,
                    headPreferred, isStartDate: false);
            else
                foreach (var county in headCarved) eraGovernment.Set(county, GovernmentMap.Theocracy);
        }
        Console.WriteLine($"  heads of faith: {realms.HeadSeats.Count} seated on land, {headCarved.Count} counties, "
                          + $"{realms.HeadSeats.Values.Count(realms.Liege.ContainsKey)} under a protector");

        var dates = eraGovernments?.OrderBy(kv => kv.Key)
            .Select(kv => new MapGen.SeeDate(kv.Key, kv.Value, EraMap(kv.Key)?.Wilderness))
            .ToList();
        MapGen.Sees.Build(faiths, seeCounties, seeGraph, governments, development, worldCenters, cultures,
            vocabulary, cfg, wilderness, dates);

        // After the sees, whose regional rites a church permits the tenets of; here rather than at
        // the two call sites so the generated world and the history's world decide them the same way.
        MapGen.TenetStatuses.Build(faiths, realms.CountyAdjacency, vocabulary, cfg);

        if (!cfg.GeneratedSees) return;

        // The prince-bishops, on each date's own realm map before any ruler is drawn from it. The
        // start date first and then outward, each preferring the counties the dates before it carved.
        var previous = new HashSet<Title>();
        var start = MapGen.PrinceBishops.Carve(faiths, realms, governments, wilderness, development, worldCenters,
            cfg.EraYearAt(cfg.StartYear), s => s.Counties, previous);
        var tally = new List<string> { $"{start.Count} on the start date" };

        foreach (var (year, eraGovernment) in (eraGovernments ?? []).OrderBy(kv => Math.Abs(kv.Key - cfg.StartYear)).ThenBy(kv => kv.Key))
        {
            if (EraMap(year) is not { } map)
            {
                foreach (var county in start) eraGovernment.Set(county, GovernmentMap.Theocracy);
                tally.Add($"{start.Count} in {year}");
                continue;
            }

            var carved = MapGen.PrinceBishops.Carve(faiths, map, eraGovernment, map.Wilderness ?? wilderness,
                development, worldCenters, cfg.EraYearAt(year), s => s.Eras.GetValueOrDefault(year), previous);
            tally.Add($"{carved.Count} in {year}");
        }

        Console.WriteLine($"  prince-bishops: {string.Join(", ", tally)}");
    }

    internal static Dictionary<int, GovernmentMap> EraGovernments(MapConfig cfg, RealmMap realms, List<Title> empires,
        List<Title> counties, TerrainClass[] provinceTerrain, bool[] coastal, Dictionary<Title, int> development,
        CultureMap cultures, WorldCenterMap worldCenters, WildernessMap wilderness, AzgaarImport? azgaar,
        Dictionary<int, string>? stateGovernments)
    {
        var eraGovernments = new Dictionary<int, GovernmentMap>();
        foreach (int year in cfg.AdditionalBookmarkYears)
        {
            var eraRealms = realms.EraMaps?.GetValueOrDefault(year) ?? realms;
            var wild = eraRealms.Wilderness ?? wilderness;
            bool crown = cfg.StartingHegemony && year > cfg.StartYear && !ReferenceEquals(eraRealms, realms);
            if (crown) Realms.CrownHegemon(eraRealms, empires, wild);

            var eraDevelopment = development.ToDictionary(kv => kv.Key,
                kv => BookmarkEras.EraDevelopment(kv.Value, cfg, year));
            eraGovernments[year] = MapGen.Governments.Build(empires, counties, eraRealms, provinceTerrain,
                coastal, eraDevelopment, cultures, worldCenters, cfg.AtAdvancement(cfg.EraYearAt(year)),
                new Rng(cfg.Seed ^ 0x6017 ^ year), azgaar, stateGovernments);

            if (crown) Realms.ExpandHegemonRealm(eraRealms, empires, wild);

            Console.WriteLine($"  governments in {year} (as advanced as {cfg.EraYearAt(year)}): " + string.Join(", ",
                eraGovernments[year].Tally(counties, wild).Select(g => $"{g.Count} {g.Government[..^11]}")));
        }
        return eraGovernments;
    }
}
