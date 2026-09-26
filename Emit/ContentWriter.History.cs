using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

public static partial class ContentWriter
{
    /// <summary>The realm map an applied history lays over the generated world, and what follows from it.</summary>
    internal sealed record AppliedRealms(RealmMap Realms, GovernmentMap Governments, double? HegemonShare,
        IReadOnlyDictionary<Title, AppliedHistory.Lineage> Lineage, List<PastRuler> PastRulers,
        IReadOnlyDictionary<Title, (byte R, byte G, byte B)> Colours, int Drifted, WildsLayer Wilds, SimDiplomacy? Diplomacy,
        IReadOnlyDictionary<Title, PastRuler> SeatParents);

    /// <summary>
    /// The wilderness at the applied date and what follows from it: the wilds cut from it, and the
    /// county culture and faith maps with settled land given its settlers' people and fallen land
    /// the unsettled ones. Read by the realm layer only — who holds what, and everything written
    /// about them. Every layer decided before the realms keeps the generated wilderness, cultures
    /// and faiths. The same objects as the generated ones when the history left the frontier alone.
    /// </summary>
    internal sealed record WildsLayer(WildernessMap Wilderness, CultureMap Cultures, FaithMap Faiths,
        FrontierMap Wilds, bool Moved)
    {
        public static WildsLayer Unmoved(WildernessMap wilderness, CultureMap cultures, FaithMap faiths, FrontierMap wilds)
            => new(wilderness, cultures, faiths, wilds, false);
    }

    /// <summary>
    /// Titles an applied history and decides what follows from who rules what: the hegemony and the
    /// governments. Shared by <see cref="BuildWorld"/>, for a full write, and <see cref="ApplyHistory"/>,
    /// for the re-emit, so the two cannot come to different answers. Throws when the history does
    /// not fit the world; see <see cref="AppliedHistory.Resolve"/>.
    /// </summary>
    internal static AppliedRealms ApplyRealms(AppliedHistory applied, RealmMap generated, MapConfig cfg,
        List<Title> empires, List<Title> counties, ProvinceMap provinces, int[] order, int baronyCount,
        TerrainClass[] provinceTerrain, Dictionary<Title, int> development, CultureMap cultures,
        WorldCenterMap worldCenters, WildernessMap generatedWilderness, AzgaarImport? azgaar,
        Dictionary<int, string>? stateGovernments, FaithMap generatedFaiths, int landCount, FrontierMap generatedWilds)
    {
        // The frontier as the history left it. From here on the realms stand on this ground, not
        // the generated one: settled land is somebody's, fallen land is the ruins dummy's.
        var wilderness = applied.WildernessOver(generatedWilderness, counties);

        // The realms' neighbours on that ground, when it is not the ground the realms being
        // replaced stood on — which a re-emit over an earlier history can differ from even when
        // this one ends where the generated world began.
        var ground = counties.Where(c => !wilderness.Contains(c)).ToList();
        bool groundMoved = generated.History?.Owner is { } before && !before.Keys.ToHashSet().SetEquals(ground);
        Dictionary<Title, HashSet<Title>>? adjacency = applied.MovesWilds || groundMoved
            ? Realms.BuildCountyAdjacency(ground, provinces, baronyCount, order,
                (int)Math.Round(cfg.Scaled(cfg.SeaBridgePixelsAtVanilla)))
            : null;

        var history = applied.Resolve(counties, cultures, generated.History, out string? problem, wilderness, adjacency)
            ?? throw new InvalidOperationException(
                $"The history applied from the History workspace does not fit this world: {problem}. "
                + "Discard it in the History workspace, or go back to the settings it was run with.");

        var wilds = applied.MovesWilds
            ? SettleWilds(applied, history, counties, wilderness, cultures, generatedFaiths, generatedWilderness,
                provinces, order, landCount, provinceTerrain)
            : WildsLayer.Unmoved(generatedWilderness, cultures, generatedFaiths, generatedWilds);
        var faiths = wilds.Faiths;
        cultures = wilds.Cultures;

        // Before titling, which folds a realm with no tier left into its lord and drops it from the
        // list: its capital is still a lord's seat, and that lord is still the history's man.
        var capitals = history.Polities.ToDictionary(p => p.Id, p => p.Capital);

        // The de jure tree as drift left it, laid down before titling, which names each realm for
        // the de jure title it covers most of. Here, at the swap and not earlier, so every cached
        // layer above was decided on the tree the world was generated with.
        int drifted = applied.ApplyDeJure(empires, development);
        if (drifted > 0)
            Console.WriteLine($"  applied history: {drifted} duchies and kingdoms drifted to a new de jure parent");

        var realms = Core.Stage.Time("applied history", () => Realms.FromHistory(history, empires, development,
            wilderness, cfg, new Rng(cfg.Seed ^ 0x2E17), adjacency ?? generated.CountyAdjacency!));

        if (cfg.StartingHegemony) Realms.CrownHegemon(realms, empires, wilderness);

        var coastal = MapGen.ProvinceSurvey.Take(provinces, order, baronyCount, null).Coastal;
        var governments = MapGen.Governments.Build(empires, counties, realms, provinceTerrain, coastal,
            development, cultures, worldCenters, cfg, new Rng(cfg.Seed ^ 0x6017), azgaar, stateGovernments);

        if (cfg.StartingHegemony) Realms.ExpandHegemonRealm(realms, empires, wilderness);
        double? hegemonShare = cfg.StartingHegemony ? Realms.HegemonDeJureShare(realms, empires, wilderness) : null;

        var lineage = applied.LineageFor(realms, capitals, wilderness, cfg.Seed);

        // The rulers the history had on the thrones, handed to every per-ruler draw from here on
        // — the character file, the families around them, the chronicle and the artifacts all read
        // the same three draws, so the man on the throne is the one the History workspace showed.
        // On the configuration the world is written with, which is this run's own copy.
        cfg.SeatPeople = applied.PeopleFor(capitals, realms);

        int independent = history.Polities.Count(p => p.Suzerain is null);
        var startDynasties = applied.StartDynasties();
        int enduring = history.Polities.Count(p => p.Suzerain is null
            && lineage.TryGetValue(p.Capital, out var line) && startDynasties.Contains(line.DynastyId));
        Console.WriteLine($"  applied history: the realms of {applied.Year} (run on from {applied.FromYear}) "
            + $"replace the generated start — {history.Polities.Count} realms, {independent} independent, "
            + $"{enduring} of them under the house that ruled them in {applied.FromYear}; "
            + $"{lineage.Count} of {realms.HolderCounty.Values.Distinct().Count()} seats keep a house");
        Console.WriteLine("  governments: " + string.Join(", ",
            governments.Tally(counties, wilderness).Select(g => $"{g.Count} {g.Government[..^11]}")));

        // The predecessors, dated against the grant date HistoryWriter seats every holder on.
        var (pastRulers, seatParents) = applied.PastRulersFor(capitals, realms, faiths, Math.Max(1, cfg.StartYear - 5),
            cfg.StartYear, cfg.Seed);
        Console.WriteLine($"  applied history: {pastRulers.Count} past rulers written into "
            + $"{pastRulers.Where(r => r.TitleKey is not null).Select(r => r.TitleKey).Distinct().Count()} "
            + $"titles' histories (of {applied.Reigns.Count} reigns kept; backstop {AppliedHistory.MaxReignsPerRealm} a realm, "
            + $"{AppliedHistory.MaxReigns} in all)");

        return new AppliedRealms(realms, governments, hegemonShare, lineage, pastRulers, applied.ColoursFor(capitals),
            drifted, wilds, applied.DiplomacyFor(capitals, counties), seatParents);
    }

    /// <summary>
    /// The frontier a history moved: the settled counties given their settlers' culture and that
    /// people's faith, the fallen ones the unsettled people, and the wilds cut
    /// again from what is left wild. Copies — the generated maps are the cached layers' and stay
    /// as they are.
    /// </summary>
    private static WildsLayer SettleWilds(AppliedHistory applied, FormationHistory history, List<Title> counties,
        WildernessMap wilderness, CultureMap cultures, FaithMap faiths, WildernessMap generatedWilderness,
        ProvinceMap provinces, int[] order, int landCount, TerrainClass[] provinceTerrain)
    {
        var unsettledCulture = cultures.Cultures.FirstOrDefault(c => c.Key == MapGen.Cultures.UnsettledKey);
        var unsettledFaith = faiths.Faiths.FirstOrDefault(f => f.Key == MapGen.Faiths.UnsettledFaithKey);
        if (unsettledCulture is null || unsettledFaith is null)
            throw new InvalidOperationException("The history moved the edge of the wilderness on a world written "
                + "without any. Discard it in the History workspace, or turn the wilderness back on.");

        var cultureOf = new Dictionary<Title, Culture>(cultures.ByCounty);
        var faithOf = new Dictionary<Title, Faith>(faiths.ByCounty);
        var settlers = applied.SettlerCulturesOver(counties, cultures);

        // The settlers' faith: the one most of their own people keep on the generated map, which is
        // what they carried out into the wild. Then the faith of the settled ground of the realm
        // holding the county, for settlers whose people hold no ground of their own. Never the
        // unsettled faith — a realm made only of settled land has a capital still marked with it.
        static Faith? Commonest(IEnumerable<Faith> seen)
            => seen.GroupBy(f => f).OrderByDescending(g => g.Count()).ThenBy(g => g.Key.Key, StringComparer.Ordinal)
                   .Select(g => g.Key).FirstOrDefault();
        var faithOfPeople = cultures.ByCounty
            .Where(kv => !generatedWilderness.Contains(kv.Key))
            .GroupBy(kv => kv.Value)
            .ToDictionary(g => g.Key, g => Commonest(g.Select(kv => faiths.For(kv.Key)))!);
        Faith FaithOf(Culture people, Polity realm)
            => faithOfPeople.GetValueOrDefault(people)
               ?? Commonest(realm.Counties.Where(c => !generatedWilderness.Contains(c)).Select(faiths.For))
               ?? Commonest(faithOfPeople.Values)
               ?? faiths.For(realm.Capital);

        int settled = 0, fallen = 0;
        foreach (var county in counties)
        {
            bool wasWild = generatedWilderness.Contains(county), isWild = wilderness.Contains(county);
            if (wasWild && !isWild && history.Owner.TryGetValue(county, out var realm))
            {
                var people = settlers.GetValueOrDefault(county) ?? realm.Culture;
                cultureOf[county] = people;
                faithOf[county] = FaithOf(people, realm);
                settled++;
            }
            else if (!wasWild && isWild)
            {
                cultureOf[county] = unsettledCulture;
                faithOf[county] = unsettledFaith;
                fallen++;
            }
        }

        var wilds = MapGen.Frontier.Build(counties, provinces, order, landCount, provinceTerrain, wilderness);
        Console.WriteLine($"  applied history: {settled} wilderness counties settled, {fallen} fallen to ruin; "
            + $"{wilderness.Count} wild at the start date ({wilderness.RuinCount} of them ruins)");

        return new WildsLayer(wilderness,
            new CultureMap { Heritages = cultures.Heritages, Cultures = cultures.Cultures, ByCounty = cultureOf },
            new FaithMap
            {
                Religions = faiths.Religions, Faiths = faiths.Faiths, ByCounty = faithOf,
                ImportedStructure = faiths.ImportedStructure, Whole = faiths.Whole,
            },
            wilds, true);
    }

    /// <summary>
    /// Lays a history run on in the History workspace over a mod already written, without
    /// generating the world again: the realms and what follows from them are decided afresh and
    /// only the files that carry them are rewritten. Everything else on disk — map data, terrain,
    /// map objects, titles, cultures, faiths, regiments — is the written world's and stays as it is.
    ///
    /// Two layers are rewritten:
    /// <list type="bullet">
    /// <item><b>Realms</b> — who rules what: title and province history, the characters and their
    /// families, noble families, regiments handed to rulers, artifacts, bookmarks, the chronicle and
    /// its struggles, wars, portraits, the hegemony's disaster regions and Dynastic Cycle entry, and
    /// the nomad naming rule.</item>
    /// <item><b>Calendar</b> — every file carrying a date, since the start date moves: culture
    /// history, the culture era thresholds and the defines.</item>
    /// </list>
    /// A simulation that one day changes cultures or faiths makes those layers too; their writers
    /// are already re-emittable (see <see cref="WorldOverwrite"/>).
    ///
    /// Written by the same code a full write runs — <see cref="ApplyRealms"/> and
    /// <see cref="WriteHistoryLayer"/> — and in the same order, so the files come out as a full
    /// write with the same <see cref="AppliedHistory"/> would make them.
    /// </summary>
    /// <returns>The world as now written: the result under the applied configuration, and the
    /// content with the new realms, governments, people and holdings in it.</returns>
    public static (GenerationResult Result, WrittenContent Written) ApplyHistory(string modDir, string gameDir,
        GenerationResult result, WrittenContent written, AppliedHistory applied)
    {
        if (!Directory.Exists(modDir))
            throw new DirectoryNotFoundException($"The mod folder '{modDir}' is no longer there. Write the mod again.");

        if (written.World is not { } world || written.Realms is not { } current || written.Flatmap is not { } flatmap)
            throw new InvalidOperationException(
                "This world was written without the history it would need to be re-emitted. Write the mod again.");

        var cfg = result.Config.AtStartYear(applied.Year);
        var runStarted = DateTime.UtcNow;
        var provinces = result.Provinces;
        var order = result.ProvinceOrder;
        var empires = result.Titles;
        var counties = world.Counties;
        var cultures = world.Cultures;
        var faiths = world.Faiths;
        var wilderness = world.Wilderness;
        var development = world.Development;
        var worldCenters = world.WorldCenters;
        var retinues = written.Retinues;

        var (realms, governments, hegemonShare, lineage, pastRulers, colours, drifted, wilds, diplomacy, seatParents) = ApplyRealms(applied, current,
            cfg, empires, counties, provinces, order, result.BaronyCount, world.ProvinceTerrain, development, cultures,
            worldCenters, wilderness, result.Azgaar, world.StateGovernments, faiths, result.LandCount, world.Frontier);

        // The culture file stays on the generated cultures — it is the calendar layer's, and holds
        // no county. Everything written about who holds what stands on the frontier the history left.
        var generatedCultures = cultures;
        (wilderness, cultures, faiths) = (wilds.Wilderness, wilds.Cultures, wilds.Faiths);

        // --- Realms, in WriteAll's order ---

        // Only ever written, never removed, by the writer itself; a world that no longer has a horde
        // has to lose the rule it wrote when it did, or the re-emit would differ from a full write.
        Core.Stage.Time("government overrides", () =>
        {
            if (!GovernmentWriter.WriteNomadNaming(modDir, gameDir, counties.Any(governments.IsNomad)))
                DeleteIfPresent(modDir, "common", "governments", "zz_generated_nomad_government.txt");
        });

        var (provinceRows, holdings) = Core.Stage.Time("title and province history", () =>
        {
            WriteLandedTitles(modDir, empires, faiths, wilderness, HegemonSeat(empires, realms));
            var built = BuildProvinceHistory(cfg, empires, world.ProvinceTerrain, development, cultures, faiths,
                governments, wilderness, worldCenters, world.SilkRoad, cfg.Seed, result.Azgaar);
            EmitProvinceHistory(modDir, built.Rows, built.Holdings, null);
            return built;
        });

        // --- De jure: only when drift moved a title, or the frontier moved ---
        //
        // Every writer here walks the de jure tree, so a drifted tree changes what they write or the
        // order they write it in; with nothing drifted they would write what is already there. The
        // formation decisions also skip empires with no settled land, and the realm words follow
        // the settlers' culture, so a moved frontier rewrites them too.
        if (drifted > 0 || wilds.Moved)
        {
            Core.Stage.Time("de jure", () =>
            {
                WriteLocalisation(modDir, empires, world.WaterNames, provinces, result.BaronyCount,
                    result.LandCount, result.RiverCount);
                WriteFormationDecisions(modDir, empires, wilderness);
                TitleTierWriter.WriteAll(modDir, cultures, empires);
                HegemonyFlavourWriter.WriteAll(modDir, gameDir, empires);
                CoronationWriter.WriteAll(modDir, gameDir, empires, faiths.Declared());
                CompatibilityWriter.WriteVanillaTitulars(modDir, gameDir, empires,
                    faiths.Faiths.Where(f => f.Head is { Inherited: true }).Select(f => f.Head!.TitleKey));
            });
        }

        // --- Calendar ---

        Core.Stage.Time("culture files", () => CultureWriter.WriteAll(modDir, cfg, generatedCultures.Declared(),
            world.Ethnicities, world.Vocabulary, new Rng(cfg.Seed ^ 0x0C1A), retinues?.Innovations));

        Core.Stage.Time("compatibility", () =>
        {
            CompatibilityWriter.WriteDefines(modDir, gameDir, cfg);

            // Written only when the calendar and advancement differ, so a history applied at an
            // offset of zero has to take away the thresholds an earlier one shifted.
            if (cfg.EraOffset == 0) DeleteDirectoryIfPresent(modDir, "common", "culture", "eras");
            CompatibilityWriter.WriteCultureEras(modDir, gameDir, cfg);

            // The disaster regions keep clear of the hegemon's realm, which has just changed.
            var regionMembers = world.Steppe.RegionMembers();
            foreach (var (key, members) in world.SilkRoad.RegionMembers()) regionMembers[key] = members;
            var everyCountyAdjacency = Realms.BuildCountyAdjacency(counties, provinces, result.BaronyCount, order,
                (int)Math.Round(cfg.Scaled(cfg.SeaBridgePixelsAtVanilla)));
            var riverside = MapGen.MajorRivers.RiversideCounties(result.Terra.MajorRiversList, provinces,
                order, result.BaronyCount, counties, Math.Max(2, (int)Math.Round(cfg.Scaled(4))));
            CompatibilityWriter.WriteGeographicalRegions(modDir, gameDir, empires, cultures, regionMembers,
                wilderness, everyCountyAdjacency, world.ProvinceTerrain,
                Realms.HegemonRealmCounties(realms, empires, wilderness), riverside);
        });

        // The Wilds situation, cut from the wilderness as the history left it. Deterministic from the
        // map alone, so rewriting it for an unmoved frontier writes what is already there.
        Core.Stage.Time("the wilds files", () => FrontierWriter.WriteAll(modDir, cfg, wilds.Wilds));

        Core.Stage.Time("dynastic cycle", () =>
        {
            if (cfg.DynasticCycle) DynasticCycleWriter.WriteAll(modDir, empires, hegemonShare);
        });

        // --- The people, and everything written about them ---

        ClearHistoryLayerFiles(modDir);

        var layer = Core.Stage.Detail("history and bookmarks", () => WriteHistoryLayer(modDir, gameDir, cfg,
            provinces, order, result.LandCount, empires, counties, realms, cultures, world.Ethnicities, faiths,
            governments, worldCenters, wilderness, development, world.TitlePlan, eraGovernments: null,
            retinues, result.Azgaar, written.Calendar, flatmap, wilds.Wilds, cultureAssets: false, lineage,
            pastRulers, diplomacy, seatParents));

        Core.Stage.Time("debug panel", () => DebugPanel.Write(modDir, DebugFacts(
            modDir, cfg, provinces, empires, counties, cultures, faiths, wilderness, worldCenters,
            retinues, result.LandCount, result.RiverCount, result.BaronyCount, layer.ArtifactCount,
            layer.StruggleCount, writeHistory: true, result.Azgaar, runStarted)));

        // Stamps exactly the files rewritten above: everything else still carries its stamp.
        Core.Stage.Time("watermark", () => Generator.ApplyWatermark(modDir, cfg));

        var appliedWorld = world with
        {
            Realms = realms, Governments = governments, HegemonShare = hegemonShare, Lineage = lineage,
            PastRulers = pastRulers,
            RealmColours = colours,
            AppliedWilds = wilds,
            AppliedDiplomacy = diplomacy,
            SeatParents = seatParents,
        };
        var appliedContent = written with
        {
            World = appliedWorld,
            Wilderness = wilderness,
            Cultures = cultures,
            Faiths = faiths,
            Frontier = wilds.Wilds,
            Realms = realms,
            Governments = governments,
            Rulers = layer.Rulers,
            Prehistory = layer.Prehistory,
            Bookmarks = layer.Bookmarks,
            Holdings = holdings,
            ProvinceHistory = provinceRows,
            EraHoldings = null,
        };

        return (result.WithConfig(cfg), appliedContent);
    }

    /// <summary>
    /// The files the history layer writes one per person, treasure or struggle, from the last time
    /// it ran. Every other file it writes is rewritten whole and needs no clearing; these are named
    /// after who or what they are for, so a bookmark character, a forged top-band weapon or a
    /// struggle that the new history does not have would otherwise outlive it. Matched by the
    /// prefixes only the history layer uses — nothing static or cultural shares them.
    /// </summary>
    private static void ClearHistoryLayerFiles(string modDir)
    {
        (string[] Dir, string Pattern)[] owned =
        [
            (["common", "bookmark_portraits"], "bm_char_*.txt"),                          // PortraitWriter
            (["gfx", "models", "artifacts", "gen_weapons"], "gen_hero_*"),                // WeaponForgeStep.FinishTopArtifacts
            (["gfx", "interface", "icons", "artifact"], "gen_hero_*"),                    // the same, icons
            (["gfx", "interface", "illustrations", "struggle_backgrounds"], "gen_struggle_*"), // StruggleArt
        ];

        foreach (var (parts, pattern) in owned)
        {
            string dir = Path.Combine([modDir, .. parts]);
            if (!Directory.Exists(dir)) continue;
            foreach (string file in Directory.EnumerateFiles(dir, pattern)) File.Delete(file);
        }
    }

    /// <summary>The empire-formation decisions, from the de jure tree as it stands. Shared by the full write and the re-emit.</summary>
    internal static (List<DecisionSpec> Decisions, int Written) WriteFormationDecisions(string modDir,
        List<Title> empires, WildernessMap wilderness)
    {
        var decisions = FormationDecisions.Build(empires, wilderness);
        int written = DecisionsWriter.WriteAll(modDir, decisions,
            comment: "Generated decisions. One per de jure empire, plus the hegemony above "
                   + "them, each shown while it has no holder.");
        return (decisions, written);
    }

    private static void DeleteIfPresent(string modDir, params string[] parts)
    {
        string path = Path.Combine([modDir, .. parts]);
        if (File.Exists(path)) File.Delete(path);
    }

    private static void DeleteDirectoryIfPresent(string modDir, params string[] parts)
    {
        string path = Path.Combine([modDir, .. parts]);
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }

    /// <summary>What the history layer decided, for WrittenContent and the debug panel.</summary>
    internal sealed record HistoryLayer(PrehistoryMap? Prehistory, RulerMap? Rulers, BookmarkCast? Bookmarks,
        int ArtifactCount, int StruggleCount);

    /// <summary>
    /// The people of the start date and everything written about them: families and ancestors,
    /// the rulers, their regiments and treasures, the bookmarks, the character and title history,
    /// the chronicle and its struggles, wars and portraits.
    ///
    /// One method, called by both <see cref="WriteAll"/> and <see cref="ApplyHistory"/>, so that
    /// history applied from the History workspace is written by exactly the code a full write uses
    /// — the two cannot drift apart. Moved here verbatim from WriteAll's history block.
    /// </summary>
    internal static HistoryLayer WriteHistoryLayer(string modDir, string gameDir, MapConfig cfg,
        ProvinceMap provinces, int[] order, int landCount, List<Title> empires, List<Title> counties,
        RealmMap realms, CultureMap cultures, EthnicityMap ethnicities, FaithMap faiths,
        GovernmentMap governments, WorldCenterMap worldCenters, WildernessMap wilderness,
        Dictionary<Title, int> development, VanillaTitles.Plan? titlePlan,
        Dictionary<int, GovernmentMap>? eraGovernments, RetinueMap? retinues, AzgaarImport? azgaar,
        WorldCalendar? calendar, Flatmap flatmap, FrontierMap frontier, bool cultureAssets = true,
        IReadOnlyDictionary<Title, AppliedHistory.Lineage>? lineage = null, List<PastRuler>? pastRulers = null,
        SimDiplomacy? diplomacy = null, IReadOnlyDictionary<Title, PastRuler>? seatParents = null)
    {
        PrehistoryMap? prehistory = null;
        RulerMap? rulers = null;
        BookmarkCast? bookmarks = null;
        int artifactCount = 0;
        int struggleCount = 0;

        prehistory = Core.Stage.Time("prehistory", () => PrehistoryMap.Build(
            counties, provinces, order, landCount, realms, cultures, faiths,
            governments, worldCenters, wilderness, cfg, new Rng(cfg.Seed ^ 0x4821 ^ cfg.PeopleSalt), lineage,
            diplomacy, seatParents));

        // An applied history's predecessors, beside the ancestors prehistory invents. Before the
        // rulers and everything that writes about them, so the character file and the title
        // history are written from the same list. Nothing for a generated world.
        if (pastRulers is { Count: > 0 }) prehistory!.AddPastRulers(pastRulers);

        // After prehistory, which it reads the houses and fathers from, and before
        // anything that names a ruler: the bookmarks and the character file both read
        // from this rather than each drawing the man again.
        rulers = Core.Stage.Time("rulers", () => RulerMap.Build(
            counties, cfg, realms, cultures, faiths, governments, wilderness, prehistory));

        // On a world of vanilla titles, the holders are vanilla's own people: whoever held
        // each title in vanilla's history on the start date, with their house and family.
        // Right after the roster and before anything names a ruler. See VanillaCharacters.
        if (titlePlan is not null)
            Core.Stage.Time("vanilla characters", () => VanillaCharacters.Import(titlePlan,
                VanillaCatalog.Read(gameDir), realms, rulers!, prehistory!, cultures, faiths, empires,
                cfg.EraOffset, new Rng(cfg.Seed ^ 0x7A15)));

        // The people of the additional bookmarks, on the maps the formation simulation drew
        // for their dates. Null unless asked for; it redraws nothing above, so the
        // start-date world is unchanged, and it only adds dynasties to prehistory.
        if (cfg.UsesAdditionalBookmarks)
            prehistory!.Eras = Core.Stage.Time("additional bookmarks", () => BookmarkEras.Build(
                cfg, counties, realms, rulers!, prehistory!, cultures, faiths, governments, wilderness,
                eraGovernments));

        // Beside the artifacts rather than beside the roster: both are things the rulers
        // already own on the start date, and both need the rulers to exist first.
        if (retinues is not null)
            Core.Stage.Time("starting retinues",
                () => RetinueWriter.WriteStartingRegiments(modDir, cfg, retinues, rulers));

        // Reads prehistory for the same reason the bookmarks do: an heirloom needs the
        // dead man it was made for and the house it was taken from, and both were decided
        // a few lines up. Without them every artifact ships with an empty history panel.
        // World centres and development are read for placement weighting only, and both are
        // optional there: a map with no wonders scatters its treasure exactly as this did
        // before, rather than needing a branch of its own.
        // Forged before the artifacts that wear them: a generated weapon picks its look
        // from this pool, so the pool has to exist first. One pool per weapon kind that has
        // a parts library; kinds without one fall back to the stock catalogue, which is a
        // supported answer rather than a failure.
        var composed = Core.Stage.Detail("  · weapon forge",
            () => WeaponForgeStep.ComposeWeaponCatalogue(modDir, gameDir, new Rng(cfg.Seed ^ 0x5A0D)));

        var artifacts = Core.Stage.Detail("  · artifacts", () => MapGen.ArtifactMap.Build(
            counties, cultures, faiths, realms, wilderness, prehistory,
            worldCenters, development, cfg, new Rng(cfg.Seed ^ 0x4A1F), composed.Looks));

        // Icons come after the artifacts and not with the catalogue, because which pairings
        // deserve one depends on which the world actually handed out. A thumbnail is the one
        // thing composition does not make cheap — geometry and masks are shared between
        // pairings, a thumbnail belongs to exactly one — so only the upper bands get drawn
        // and everything else keeps its kind's stock art.
        var forgedWeapons = Core.Stage.Detail("  · weapon icons",
            () => WeaponForgeStep.FinishTopArtifacts(modDir, gameDir, composed,
                artifacts.AllArtifacts.Select(a => (a.Visuals, a.Rarity)),
                ArtifactRarity.Famed, ArtifactRarity.Masterwork, new Rng(cfg.Seed ^ 0x4E17)));

        ArtifactWriter.WriteTemplates(modDir);
        Core.Stage.Detail("  · artifact visuals", () => ArtifactWriter.WriteVisuals(modDir, forgedWeapons));

        // The weapon icons exist from here. Read only; nothing when nobody watches. See Showcase.
        Core.Showcase.Publish(() => ShowcaseItems.Treasures(artifacts, forgedWeapons, modDir, gameDir));

        // Dresses weapons the *game* creates - inspirations, tournament prizes, adventurer
        // finds - from the same pool. Without it every player-earned weapon would be vanilla
        // art standing next to forged art in the same inventory. Keyed on culture, so it
        // needs this world's culture list rather than just the weapons.
        ForgedVisualOverrides.Write(modDir, forgedWeapons,
            [.. cultures.Cultures.Select(c => c.Key)]);

        // Armour, which needs no geometry at all: a vanilla war garment already carries the
        // mask and variation hooks a forged weapon does, so a look is a palette and some
        // text. Culture picks the garment, the artifact's type picks the material.
        //
        // These three read nothing about who rules what — only the cultures and the asset
        // libraries — and they patch the mod's own copies of vanilla files, so running them over a
        // mod that already has their patches applies them twice. A re-emit (cultureAssets false)
        // leaves what the write put there, which is exactly what they would produce again.
        if (cultureAssets)
        {
            Core.Stage.Detail("  · armour forge", () => ArmorForgeStep.WriteAll(modDir, gameDir,
                [.. cultures.Cultures.Select(c => c.Key)],
                cultures.Cultures.ToDictionary(c => c.Key, c => c.ClothingGfx, StringComparer.Ordinal)));

            // Hand-modelled pieces from assets/armors, worn from a debug flag. After the forge
            // above, because that is what splices the gene template both of them rely on.
            CustomArmorStep.WriteAll(modDir, gameDir);

            // Rigid pieces hung off portrait bones - pauldrons today, any slot later. After the
            // armour forge because it garnishes what that emits, though it depends on none of it.
            BonePieceStep.WriteAll(modDir, gameDir, [.. cultures.Cultures.Select(c => c.Key)]);
        }
        ArtifactWriter.WriteModifiers(modDir, artifacts);
        ArtifactWriter.WriteLocalisation(modDir, artifacts);
        ArtifactWriter.WriteOnGameStart(modDir, artifacts, cfg);
        artifactCount = artifacts.AllArtifacts.Count;

        if (forgedWeapons.Count > 0)
        {
            Console.WriteLine("  forged weapons: " + string.Join(", ",
                forgedWeapons.GroupBy(a => a.Kind)
                    .Select(g => $"{g.Count()} {g.Key}(s)"))
                + " in the artifact pool");

            // The band split is printed because it is the one part of the forge a config
            // change can quietly move: raise WeaponPoolSizePerKind and it widens, drop it
            // below four and bands start sharing looks. Neither shows in the emitted files
            // without opening them. Counted across every kind rather than per kind, since a
            // library that under-fills its pool gets a shorter ladder than its neighbours.
            Console.WriteLine("    bands: " + string.Join(", ",
                forgedWeapons.Where(a => a.Tier is not null)
                    .GroupBy(a => a.Tier!.Value)
                    .OrderBy(g => g.Key)
                    .Select(g => $"{g.Count()} {g.Key.ToString().ToLowerInvariant()}")));
        }

        var bookmarkResult = Core.Stage.Detail("  · bookmarks", () => BookmarkWriter.WriteAll(
            modDir, gameDir, cfg, provinces, order, empires,
            realms, development, cultures, governments, wilderness, prehistory,
            rulers, azgaar, calendar));

        // Kept for the editor: re-emitting a ruler means re-emitting the bookmark that
        // describes him, and the cast is the record of who that is.
        bookmarks = bookmarkResult.Cast;

        Core.Stage.Detail("  · character history", () => HistoryWriter.WriteAll(
            modDir, cfg, empires, realms, development,
            cultures, ethnicities, faiths, governments, wilderness, prehistory, rulers, calendar));

        // Last of the history block, because it reads everything the rest of it decided.
        // Inside the block rather than beside it: with --no-history there are no houses, no
        // wars and no artifacts, so a chronicle written there could only repeat the map back
        // at the player, and the GUI already treats a missing key as "no button".
        var chronicle = Core.Stage.Time("chronicle", () => ChronicleMap.Build(
            empires, realms, development, cultures, faiths, wilderness, prehistory,
            artifacts, worldCenters, cfg, new Rng(cfg.Seed ^ 0x104E)));

        // After the chronicle, which is the thing that decides where a struggle is. Reads
        // the counties for its membership and the chronicle only for its tension, so it
        // cannot invent a quarrel the lore panel does not also report.
        var struggles = Core.Stage.Time("struggles", () => StruggleMap.Build(
            empires, chronicle, cultures, faiths, wilderness, cfg,
            new Rng(cfg.Seed ^ 0x57A6)));

        // Written after the struggles it reads, not after the chronicle it is made of: the
        // lore panel closes with the name of the struggle a title is caught up in, and that
        // name does not exist until the line above has run.
        //
        // Only this is gated, not the build above: the chronicle is also where struggles
        // come from. The lore it writes is read by the Realm Lore panel and nothing else.
        if (cfg.EnableChronicle)
            ChronicleWriter.WriteAll(modDir, chronicle, struggles, empires);

        Core.Stage.Detail("  · struggle art",
            () => StruggleWriter.WriteAll(modDir, gameDir, cfg, struggles, flatmap, provinces, order));
        struggleCount = struggles.Struggles.Count;

        // The half of the chronicle the game writes. After the struggles because it
        // narrates their phase changes by name, and after the frontier for the same reason.
        Core.Stage.Detail("  · chronicle (runtime)",
            () => ChronicleRuntimeWriter.WriteAll(modDir, cfg, struggles, frontier));

        WarWriter.WriteAll(modDir, prehistory, cfg);
        Core.Stage.Detail("  · portraits", () => PortraitWriter.WriteAll(
            modDir, gameDir, bookmarkResult.PortraitRequests, ethnicities, cfg.Seed));

        return new HistoryLayer(prehistory, rulers, bookmarks, artifactCount, struggleCount);
    }
}
