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
public static partial class ContentWriter
{
    /// <returns>
    /// The handful of things a later edit needs — see <see cref="WrittenContent"/>. Ignored by the
    /// command line, which writes once and exits.
    /// </returns>
    /// <param name="shippedHeightmap">The heightmap <see cref="MapDataWriter.WriteAll"/> wrote.
    /// The scatter passes need it, and it is required rather than optional on purpose: the bug it
    /// fixes was a scatter quietly reading a surface the game never renders, and a default would
    /// let that back in with no compile error to catch it.
    ///
    /// Asked for rather than handed over, because map_data may still be writing it on another
    /// thread when this method starts. It is read at exactly one place — the rendered heightmap,
    /// which comes after the terrain textures and masks — so by the time this is called the work
    /// behind it has had eleven seconds to finish, and the caller decides whether that is a join
    /// or just a field read.</param>
    public static WrittenContent WriteAll(string modDir, string gameDir, MapConfig cfg,
            ProvinceMap provinces, int[] order, int baronyCount, int landCount, int riverCount,
            List<Title> empires, TerrainData terra, TerrainClassifier.Result classified, Rng rng,
            Func<ushort[]> shippedHeightmap,
            bool writeHistory = true, MapGen.Drainage? drainage = null,
            MapGen.AzgaarImport? azgaar = null,
            AppliedHistory? applied = null)
    {
        var terrain = classified.Terrain;
        var provinceElevation = terra.ProvinceElevation;
        var runStarted = DateTime.UtcNow;

        Core.Stage.Time("blank vanilla data", () => BlankVanillaData(modDir, gameDir));

        // Every decision up to the water names, taken before any of it is written. The four files
        // that used to be written between those stages follow straight after, in the order they
        // always went out in. See WorldModel.
        var world = BuildWorld(gameDir, cfg, provinces, order, baronyCount, landCount, riverCount,
            empires, terra, classified, drainage, azgaar, applied);

        var provinceTerrain = world.ProvinceTerrain;
        var vocabulary = world.Vocabulary;
        var counties = world.Counties;
        var development = world.Development;
        var wilderness = world.Wilderness;
        var titlePlan = world.TitlePlan;
        var cultures = world.Cultures;
        var ethnicities = world.Ethnicities;
        var worldCenters = world.WorldCenters;
        var realms = world.Realms;
        var governments = world.Governments;
        var eraGovernments = world.EraGovernments;
        var hegemonShare = world.HegemonShare;
        var faiths = world.Faiths;
        var steppe = world.Steppe;
        var frontier = world.Frontier;
        var crossings = world.Crossings;
        var routes = world.Routes;
        var silkRoad = world.SilkRoad;
        var waterNames = world.WaterNames;

        Core.Stage.Time("ethnicity files", () => EthnicityWriter.WriteAll(modDir, ethnicities));

        // The render-time enforcement of those ethnicities' races — same table, other end.
        Core.Stage.Time("race morph modifiers", () => RaceMorphWriter.WriteAll(modDir, cfg, ethnicities));

        Core.Stage.Time("government overrides",
            () => GovernmentWriter.WriteNomadNaming(modDir, gameDir, counties.Any(governments.IsNomad)));

        // Assigned inside the stage below and kept for WrittenContent — the holdings come off one
        // Rng walked across the whole world, so this is the only chance to see them.
        Dictionary<int, string> holdings = [];
        List<ProvinceRow> provinceRows = [];
        EraHoldings? eraHoldings = null;

        Core.Stage.Time("titles, history and localisation", () =>
        {
            WriteLandedTitles(modDir, empires, faiths, wilderness, HegemonSeat(empires, realms));
            WriteProvinceTerrain(modDir, provinceTerrain, landCount);
            (provinceRows, holdings) = BuildProvinceHistory(cfg, empires, provinceTerrain, development, cultures, faiths, governments, wilderness, worldCenters, silkRoad, cfg.Seed, azgaar);
            eraHoldings = BuildEraHoldings(cfg, empires, wilderness, eraGovernments, holdings);
            EmitProvinceHistory(modDir, provinceRows, holdings, eraHoldings);
            WriteLocalisation(modDir, empires, waterNames, provinces, order, baronyCount,
                landCount, riverCount);
        });

        Core.Stage.Time("wonders", () => WonderWriter.WriteAll(modDir, gameDir, worldCenters));
        Core.Stage.Time("wonder index", () => WonderIndex.Write(modDir, worldCenters));

        Core.Stage.Time("title tiers", () => TitleTierWriter.WriteAll(modDir, cultures, empires));

        // After the de jure tree and the wilderness pass, because the formation decisions read
        // both, and before nothing in particular: no other writer reads what this one produces.
        Core.Stage.Time("decisions", () =>
        {
            var decisions = FormationDecisions.Build(empires, wilderness);
            int written = DecisionsWriter.WriteAll(modDir, decisions,
                comment: "Generated decisions. One per de jure empire, plus the hegemony above "
                       + "them, each shown while it has no holder.");

            // Whether an empire is held is a runtime question and the decision asks it at runtime,
            // so every empire gets one. The start-date count is reported anyway because it is the
            // only number that says how many a player could take on day one.
            int openAtStart = empires.Count(e => FormationDecisions.HasFormation(decisions, e)
                                              && !realms.HolderCounty.ContainsKey(e));

            bool crowned = Titles.HegemonyOf(empires) is { } crown
                        && FormationDecisions.HasFormation(decisions, crown);

            Console.WriteLine(written == 0
                ? "  decisions: none (no empire with enough settled land to be worth forming)"
                : $"  decisions: {written - (crowned ? 1 : 0)} empire formations of "
                + $"{empires.Count} empires, {openAtStart} unformed at the start date"
                + (crowned ? ", and the hegemony above them" : ""));
        });

        // The world's way of war. Runs here because it is the last social layer and reads all of
        // the others — the ground a people holds *after* cultivation has moved the farmland, the
        // government most of them live under, the temperament their ethos gave them — and because
        // the culture files below have to carry the innovations it invents. Nothing downstream of
        // it changes a culture, so this is the earliest point at which its inputs are all final.
        //
        // The generated governments, not an applied history's: a culture's regiments are part of
        // the world that applying a history keeps as it was written. See WorldModel.GeneratedGovernments.
        var retinues = cfg.EnableGeneratedRetinues
            ? Core.Stage.Time("retinues", () => MapGen.Retinues.Build(cultures.Declared(), world.GeneratedGovernments,
                provinceTerrain, vocabulary, cfg, new Rng(cfg.Seed ^ 0x3AA7)))
            : null;
        if (retinues is not null) Core.Showcase.Publish(() => ShowcaseItems.Regiments(retinues, gameDir));

        Core.Stage.Time("culture files",
            () => CultureWriter.WriteAll(modDir, cfg, cultures.Declared(), ethnicities, vocabulary,
                new Rng(cfg.Seed ^ 0x0C1A), retinues?.Innovations));

        if (retinues is not null)
        {
            Core.Stage.Time("men-at-arms", () => RetinueWriter.WriteAll(modDir, retinues));
            Core.Stage.Time("generated innovations",
                () => InnovationWriter.WriteAll(modDir, retinues.Innovations));
        }

        // After naming, so the calendar speaks the language the world's peoples ended up with.
        var calendar = WorldCalendar.Build(cfg, azgaar, cultures);
        Core.Showcase.Publish(() => ShowcaseItems.Calendar(calendar));

        Core.Stage.Time("compatibility", () =>
        {
            CompatibilityWriter.WriteDefines(modDir, gameDir, cfg);
            CompatibilityWriter.WriteCultureEras(modDir, gameDir, cfg);
            CompatibilityWriter.WriteCalendarLocalisation(modDir, calendar);
            var regionMembers = steppe.RegionMembers();
            foreach (var (key, members) in silkRoad.RegionMembers()) regionMembers[key] = members;
            // The natural-disaster regions need terrain to sit on, neighbours to spread over, and
            // the hegemon's realm to stay mostly clear of; see WriteGeographicalRegions. Not
            // realms.CountyAdjacency — that one is built over the non-wilderness counties only
            // (Realms.Build), so a wilderness county has no entry in it and cannot be tested
            // against. Built once more here over every county, at the bridge distance Realms uses.
            var everyCountyAdjacency = Realms.BuildCountyAdjacency(counties, provinces, baronyCount, order,
                (int)Math.Round(cfg.Scaled(cfg.SeaBridgePixelsAtVanilla)));
            // Flood regions want actual riverbank rather than a terrain proxy; a few pixels either
            // side of the traced course is the land a river takes. See PlaceDisasterRegions.
            var riverside = MapGen.MajorRivers.RiversideCounties(terra.MajorRiversList, provinces,
                order, baronyCount, counties, Math.Max(2, (int)Math.Round(cfg.Scaled(4))));
            CompatibilityWriter.WriteGeographicalRegions(modDir, gameDir, empires, cultures, regionMembers,
                wilderness, everyCountyAdjacency, provinceTerrain,
                Realms.HegemonRealmCounties(realms, empires, wilderness), riverside);
            CompatibilityWriter.WriteHolySites(modDir, gameDir, empires, faiths);
            CompatibilityWriter.WriteDecisionBlocks(modDir, gameDir);
        });

        // After the regions it points at, and nothing reads what it writes.
        Core.Stage.Time("great steppe files", () => SteppeWriter.WriteAll(modDir, gameDir, steppe));
        Core.Stage.Time("the wilds files", () => FrontierWriter.WriteAll(modDir, cfg, frontier));
        Core.Stage.Time("silk road files", () => SilkRoadWriter.WriteAll(modDir, gameDir, cfg, silkRoad));
        // The situation is started from one history entry and nothing else (see the writer), so
        // MapConfig.DynasticCycle off is simply that entry not being written; the guarded
        // on_actions and the Silk Road link handle its absence at runtime.
        Core.Stage.Time("dynastic cycle", () =>
        {
            if (cfg.DynasticCycle) DynasticCycleWriter.WriteAll(modDir, empires, hegemonShare);
            else Console.WriteLine("  dynastic cycle: not started (MapConfig.DynasticCycle is off)");
        });
        // Independent of the situation above: the hegemony wears China's arms, robes and throne
        // room whether or not the Dynastic Cycle is running, because all of it keys off the title
        // rather than off the situation. Not gated on MapConfig.DynasticCycle for that reason.
        Core.Stage.Time("hegemony flavour",
            () => HegemonyFlavourWriter.WriteAll(modDir, gameDir, empires));
        // Mood music pools are gated on vanilla heritages a generated culture never has, plus a
        // Dynastic Cycle branch that every independent satisfies. MusicWriter keys them on dress.
        Core.Stage.Time("music pools", () => MusicWriter.WriteAll(modDir, gameDir));

        Core.Stage.Time("route files", () => RouteWriter.WriteAll(modDir, routes, crossings, silkRoad,
            provinces, order, baronyCount, provinceTerrain));

        Core.Stage.Time("religion files", () => ReligionWriter.WriteAll(modDir, faiths.Declared()));

        // After the religion files rather than with the faiths: a generated faith's icon is drawn
        // by the writer above, and this is the first moment it exists to be shown. See Showcase.
        Core.Showcase.Publish(() => ShowcaseItems.Faiths(faiths, modDir, gameDir));

        // After the religions, whose crown-or-regalia answer it writes into CK3's triggers, and
        // after the titles it reads seats off. It writes nothing anything else reads.
        Core.Stage.Time("coronation files",
            () => CoronationWriter.WriteAll(modDir, gameDir, empires, faiths.Declared()));

        // Vanilla heads of faith a vanilla faith kept are declared in landed_titles above, as real
        // titles with a seat and a holder; shimming them too would declare each one twice.
        Core.Stage.Time("vanilla titulars",
            () => CompatibilityWriter.WriteVanillaTitulars(modDir, gameDir, empires,
                faiths.Faiths.Where(f => f.Head is { Inherited: true }).Select(f => f.Head!.TitleKey)));

        // Once, for both the locators and the city scatter: a slope field and a distance transform
        // over the whole province raster, which each of them used to run for itself off the same
        // two inputs. Hoisted here rather than memoised inside ProvinceAnchor so the sharing is
        // visible at the call site and the two writers cannot drift apart.
        var anchors = Core.Stage.Time("province anchors", () =>
        {
            var computed = MapGen.ProvinceAnchor.Compute(provinces, provinceElevation, cfg);

            // Before either writer reads them: a wall-circuit wonder stands on its holding, not
            // beside it, and the city scatter has to know the ring is there.
            MapGen.ProvinceAnchor.EncloseHoldings(computed, worldCenters, provinces, order,
                provinceElevation, cfg);
            return computed;
        });

        Core.Stage.Time("locators", () => LocatorWriter.WriteAll(modDir, gameDir, provinces, order, landCount, anchors, cfg));
        Core.Stage.Time("casus belli", () => CasusBelliWriter.WriteAll(modDir, gameDir, cfg));
        Core.Stage.Time("council tasks", () => CouncilTaskWriter.WriteAll(modDir, gameDir, cfg));
        Core.Stage.Time("faction rules", () => FactionWriter.WriteAll(modDir, gameDir, cfg));
        Core.Stage.Time("frontend", () => FrontendWriter.WriteFrontend(modDir, gameDir));
        Core.Stage.Time("GUI changes",
            () => GuiWriter.WriteAll(modDir, gameDir, cfg.EnableSocieties, cfg.EnableWilderness,
                cfg.EnableChronicle));

        if (cfg.EnableFantasyEthnicities && cfg.RaceMode != MapConfig.FantasyRaceMode.HumanOnly)
        {
            Core.Stage.Time("character interactions",
                () => InteractionWriter.PatchMarriageInteractions(modDir, gameDir));
        }

        if (cfg.EnableWilderness)
            Core.Stage.Time("poetry interactions",
                () => InteractionWriter.PatchPoetryInteractions(modDir, gameDir));

        // These two come before the split below rather than in either half of it, because both
        // halves need them: the history branch reads `flatmap` for the struggle art and reads
        // flatmap.dds back off disk for the bookmark background, and neither can be racing the
        // writer that produces them. Neither takes the shared Rng, so hoisting them past the
        // terrain textures leaves that stream's order untouched.
        Core.Stage.Time("map graphics", () => MapGraphicsWriter.WriteAll(modDir, gameDir, cfg, provinces, order, landCount));

        // Kept rather than dropped: StruggleArt cuts each struggle's window background out of this
        // same buffer further down, and re-rendering or re-reading it there would be the same
        // parchment twice.
        var flatmap = Core.Stage.Time("flatmap", () => FlatmapWriter.WriteAll(
            modDir, cfg, provinces, order, landCount, provinceElevation, provinceTerrain));

        // Everything from here to the holding models is the raster and scatter half of the run:
        // about eighteen seconds on a large map, and it shares nothing with the history half that
        // follows. It writes gfx/map/terrain and gfx/map/map_object_data; history writes common,
        // history, localization, gui and gfx/interface. It owns the shared Rng — terrain textures
        // draws from it and so do the four scatter writers, in this order — and the history half
        // never touches that instance, every part of it seeding its own stream from cfg.Seed.
        //
        // So it runs on its own thread while the main one gets on with the history. Its console
        // output is collected rather than printed, and replayed at the join in the order the
        // phases used to run in; see ConsoleFork for why the log order is worth the trouble.
        // Asked for here rather than where it is used, and this is the join with map_data when the
        // caller runs that concurrently. Here because it is the last point on the main thread
        // before the two branches start: joining inside the raster branch would work, but it would
        // bury map_data's console block — the river audit, the coastline report, the packing
        // figures — in the middle of that branch's output. This puts it between the content
        // section and the branches, where it reads as its own phase.
        //
        // It costs about nothing: map_data and the content section above are within milliseconds
        // of each other in length, so by the time this runs the branch has essentially finished.
        var shipped = shippedHeightmap();

        // The city models stand where the generated world's holdings are. An applied history moves
        // the holdings in the province history with its governments, but the map objects are part
        // of the written world that applying keeps — so a full write and the re-emit agree.
        var scatterHoldings = ReferenceEquals(world.GeneratedGovernments, governments)
            ? holdings
            : BuildProvinceHistory(cfg, empires, provinceTerrain, development, cultures, faiths,
                world.GeneratedGovernments, wilderness, worldCenters, silkRoad, cfg.Seed, azgaar).Holdings;

        // Where an army can march across water: straits and major-river crossings, written into
        // map_data/adjacencies.csv over the stub the map writer left. See MapGen/Crossings.cs.
        //
        // After the join, not straight after BuildWorld where the crossings are ready: map_data
        // writes that stub on its own thread, after the heightmap, so written any earlier this
        // file lands first only by timing, and a slow heightmap would put the stub back over it.
        // Past the join the map_data branch has finished, which also keeps this truncating write
        // clear of AssertMapDataComplete's scan for zero-byte files. Nothing reads it back before
        // here.
        Core.Stage.Time("adjacencies", () => MapDataWriter.WriteAdjacencies(modDir, crossings,
            provinces.Height, id => world.CrossingNames.GetValueOrDefault(id, id.ToString())));

        var rasterBranch = Core.ConsoleFork.Start(() =>
        {
        // Full-resolution heightmap elevation passed to detail texture generator
        Core.Stage.Time("terrain textures", () => TerrainTextureWriter.WriteAll(modDir, cfg, terrain,
            classified.Climate, terra.Elevation, rng));

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
                HeightmapPacker.Reconstruct(
                    shipped, cfg.Width, cfg.Height, cfg.HeightmapSagBudget,
                    HeightmapPacker.TileStepFor(cfg), cfg.BalanceNeighbourLods), cfg));

        // renderedElevation for all three, not terra.Elevation: every one of them seeds from
        // province-resolution terrain and then jitters to a sub-pixel position, and has to ask the
        // heightmap the engine renders whether that spot is dry. See ScatterGround.
        Core.Stage.Time("trees", () => TreeWriter.WriteAll(modDir, cfg, terrain, classified.Climate, renderedElevation, rng));
        Core.Stage.Time("animals", () => AnimalWriter.WriteAll(modDir, cfg, terrain, renderedElevation, rng));
        Core.Stage.Time("env effects", () => EnvEffectWriter.WriteAll(modDir, cfg, terrain, renderedElevation, rng));
        Core.Stage.Time("bridges", () => BridgeWriter.WriteAll(modDir, cfg, terra.MajorRiversList, classified.Climate, renderedElevation, rng));
        // Prototype, deliberately severable: its own Rng stream and its own output file, so
        // MapConfig.EnableCityScatter (--no-city-scatter) removes it without moving anything else.
        Core.Stage.Time("city scatter", () => CityScatterWriter.WriteAll(modDir, cfg, empires,
            scatterHoldings, development, cultures, provinces, order, anchors, renderedElevation));
        Core.Stage.Time("map table", () => MapTableWriter.WriteAll(modDir, cfg));
        Core.Stage.Time("holding models", () => HoldingModelWriter.WriteAll(modDir, gameDir, cfg));
        });

        // Null with --no-history, like Realms: rulers only exist once the history phase decides them,
        // and prehistory is kept beside them because re-emitting a ruler means re-emitting the
        // family and relations written around him.
        RulerMap? rulers = null;
        PrehistoryMap? prehistory = null;
        BookmarkCast? bookmarks = null;

        // Hoisted out of the block below for the debug panel, which reports them, and for the same
        // reason the three above are: nothing outside the history phase can see what it decided.
        // Counts rather than the maps themselves — the panel wants a number, and keeping a whole
        // ArtifactMap alive past the block that used it to say "312" would be the wrong trade.
        int artifactCount = 0;
        int struggleCount = 0;

        // The other half of the split. Runs on this thread while the raster branch runs on its
        // own; its output is collected the same way and replayed second, which is the order these
        // two printed in when they were sequential.
        //
        // Detail rather than Time on the span itself: Stage sums the un-nested spans against the
        // wall clock to say what is unaccounted for, and a span overlapping another one would be
        // counted twice and drive that remainder negative — the exact failure the nesting
        // distinction exists to prevent. The raster branch holds the wall time for this stretch;
        // this one is reported beside it rather than added to it.
        var historyLog = new StringWriter();
        try
        {
        Core.ConsoleFork.CaptureInto(historyLog, () =>
        {
        if (writeHistory)
        {
            Core.Stage.Detail("history and bookmarks", () =>
            {
                var layer = WriteHistoryLayer(modDir, gameDir, cfg, provinces, order, landCount, empires,
                    counties, realms, cultures, ethnicities, faiths, governments, worldCenters, wilderness,
                    development, titlePlan, eraGovernments, retinues, azgaar, calendar, flatmap, frontier,
                    lineage: world.Lineage, pastRulers: world.PastRulers);

                prehistory = layer.Prehistory;
                rulers = layer.Rulers;
                bookmarks = layer.Bookmarks;
                artifactCount = layer.ArtifactCount;
                struggleCount = layer.StruggleCount;
            });
        }
        else
        {
            Console.WriteLine("  history: SKIPPED (--no-history)");

            // Still written: the Ruins and Wilderness sets call its effects, and the title panel
            // and world window read its custom loc, whether or not there is a prehistory. Without
            // struggles it narrates ruin and the frontier only.
            Core.Stage.Time("chronicle (runtime)",
                () => ChronicleRuntimeWriter.WriteAll(modDir, cfg, null, frontier));
        }
        });
        }
        finally
        {
            // In a finally because the raster branch is a live thread writing into the mod folder:
            // if the history half throws, letting the exception past this point would leave that
            // thread still running while the caller reports a failed run and, in the GUI, while the
            // user starts another one into the same directory. Joined even on the way out.
            //
            // Before the static files on purpose: StaticFileWriter skips any target written during
            // this run, which is a question about files both branches are still creating until
            // this line. And the log goes out in phase order rather than in whatever order the two
            // threads happened to reach it.
            rasterBranch.JoinAndReplay();
            Console.Write(historyLog.ToString());
        }

        List<string> sets = [StaticFileWriter.Core];
        if (cfg.EnableWilderness)
        {
            sets.Add(StaticFileWriter.Wilderness);

            // abandon_county_effect in the set calls gen_strip_buildings_effect by name, and the
            // effect's body is the game's building list, so it is emitted beside the set every time
            // the set ships. See BuildingStripWriter for why it cannot be a static file.
            Core.Stage.Time("building strip", () => Console.WriteLine(
                $"  buildings: strip effect covers {BuildingStripWriter.Write(modDir, gameDir)} keys"));
        }

        // ANDed, never implied. Ruins hand counties to a dummy under wilderness_government and
        // expect the colonisation flow to be the way back, so shipping them without the wilderness
        // set would ship a system whose every reference dangles.
        if (cfg.EnableWilderness && cfg.EnableRuins) sets.Add(StaticFileWriter.Ruins);
        if (cfg.EnableFantasyEthnicities && cfg.RaceMode != MapConfig.FantasyRaceMode.HumanOnly)
            sets.Add(StaticFileWriter.Fantasy);
        if (cfg.EnableSocieties) sets.Add(StaticFileWriter.Societies);
        Core.Stage.Time("static files", () => StaticFileWriter.WriteAll(modDir, sets, runStarted));

        // DEAD LAST, and both halves of that matter.
        //
        // It is last among the writers that read the world because it reports what every one of
        // them decided, and a number gathered before a phase that could still change it would be a
        // debug panel that lies.
        //
        // It is also after StaticFileWriter, which is newer and easier to undo by accident: the
        // panel's Events tab is built by SCANNING the mod's own events/ folder, and most of the
        // events live in BaseFilesToCopy and arrive on the line above. Moved back before it, the
        // scan finds only the generated handful and the tab quietly loses fifty buttons — no error,
        // no warning, just a shorter list than there should be. See Emit/GuiWindows/ShippedEvents.cs.
        Core.Stage.Time("debug panel", () => DebugPanel.Write(modDir, DebugFacts(
            modDir, cfg, provinces, empires, counties, cultures, faiths, wilderness, worldCenters,
            retinues, landCount, riverCount, baronyCount, artifactCount, struggleCount,
            writeHistory, azgaar, runStarted)));

        // After the write rather than during it: cultures and faiths both gain their unsettled
        // entries above, and a capture taken where each was built would not have them.
        return new WrittenContent
        {
            Cultures = cultures,
            Ethnicities = ethnicities,
            Faiths = faiths,
            WaterNames = waterNames,
            Wilderness = wilderness,
            Development = development,
            Holdings = holdings,
            EraHoldings = eraHoldings,
            ProvinceHistory = provinceRows,
            WorldCenters = worldCenters,
            Realms = realms,
            Rulers = rulers,
            Prehistory = prehistory,
            Governments = governments,
            Steppe = steppe,
            Frontier = frontier,
            Bookmarks = bookmarks,
            Calendar = calendar,
            World = world,
            Retinues = retinues,
            Flatmap = flatmap,
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

        // Still takes the array: this overload is for callers that already hold one — the editor
        // harness re-emitting part of a world — and have no branch to wait on.
        return WriteAll(modDir, gameDir, cfg, provinces, order, baronyCount, landCount, riverCount,
            empires, terra, classified, rng, () => shippedHeightmap, writeHistory);
    }

    /// <summary>
    /// Not private: every title's colour lives in this file, so recolouring one after the mod is
    /// written re-runs exactly this. See <see cref="WorldOverwrite"/>.
    /// </summary>
    /// <summary>The county the crowned hegemon rules from, or null when nobody wears the hegemony.</summary>
    internal static Title? HegemonSeat(List<Title> empires, RealmMap realms)
        => Titles.HegemonyOf(empires) is { } crown ? realms.HolderCounty.GetValueOrDefault(crown) : null;

    /// <param name="hegemonSeat">
    /// The county a hegemon crowned at the start rules from, when there is one. It becomes the
    /// hegemony's <c>capital</c> instead of the de jure default, because vanilla script reads
    /// <c>title:h_china.title_capital_county</c> as "where the Son of Heaven sits" — a Mandate claim
    /// moves the claimant's realm capital there — and the de jure default is whatever county the
    /// first empire happens to be seated in, nomad camp included.
    /// </param>
    /// <summary>Where a county sits on the map: the mean of its baronies' province seeds, in pixels.
    /// Null for a county with no land province.</summary>
    private static Func<Title, (double X, double Y)?> CountyPosition(ProvinceMap provinces, int[] order, int landCount)
    {
        var seedOf = new int[landCount + 1];
        for (int label = 0; label < order.Length; label++)
            if (order[label] is >= 1 and var id && id <= landCount) seedOf[id] = label;

        return county =>
        {
            double x = 0, y = 0;
            int n = 0;
            foreach (var barony in county.Children)
            {
                int id = barony.ProvinceId;
                if (id < 1 || id > landCount) continue;
                var seed = provinces.Seeds[seedOf[id]];
                x += seed.X;
                y += seed.Y;
                n++;
            }
            return n == 0 ? null : (x / n, y / n);
        };
    }

    internal static void WriteLandedTitles(string modDir, List<Title> empires, FaithMap faiths,
        WildernessMap wilderness, Title? hegemonSeat = null)
    {
        string dir = Path.Combine(modDir, "common", "landed_titles");
        Directory.CreateDirectory(dir);

        // Four spaces, not tabs. That is how this file has always been written and it is the one
        // place in the mod that differs, which is why JominiStyle exists as a parameter at all.
        var jb = new JominiBuilder(JominiStyle.Spaced);

        jb.Comment("Generated de jure hierarchy.");
        jb.Blank();

        // The hegemony first when there is one, then every empire it does not cover — the crown is a
        // region rather than the map, so writing it alone would drop the empires outside it. Nesting
        // is the whole declaration of the tier: `tier` is documented as not for use in database
        // definitions and appears nowhere in vanilla's own data.
        foreach (var root in Titles.Roots(empires)) Write(root);

        jb.Comment("Head of faith landless titles.");
        jb.Blank();

        foreach (var faith in faiths.Faiths)
        {
            if (faith.Head is null) continue;
            var (r, g, bl) = faith.Color;

            using (jb.Block(faith.Head.TitleKey))
            {
                // A vanilla head keeps vanilla's own declaration — its colour, its regnal names, its
                // succession fields — and only the capital is ours. See HeadOfFaith.InheritedFields.
                if (faith.Head.Inherited)
                {
                    jb.Field("capital", faith.Head.Seat.Key);
                    foreach (string field in faith.Head.InheritedFields) jb.Token(field);
                }
                else
                {
                    jb.Inline("color", F(r), F(g), F(bl));
                    jb.Field("capital", faith.Head.Seat.Key);
                    jb.Field("landless", "yes");
                }
            }

            jb.Blank();
        }

        var wildCapital = wilderness.Unsettled.FirstOrDefault();
        if (wildCapital is not null)
        {
            jb.Comment("The wilderness realm. Titular: it exists so unsettled land has a name.");
            jb.Blank();

            using (jb.Block(WildernessMap.TitleKey))
            {
                jb.Inline("color", "108", "104", "96");
                jb.Field("capital", wildCapital.Key);
                jb.Field("landless", "yes");
                jb.Field("definite_form", "yes");
                jb.Field("ruler_uses_title_name", "no");
            }

            jb.Blank();
        }

        // Written on the SYSTEM being on, not on any county being ruined at the start date. The
        // usual setting seeds none: the world begins whole and counties fall during play, and the
        // one thing script cannot do at runtime is mint a landed title — so it has to be here on
        // day one, holder and all, waiting.
        //
        // Its capital is whichever county comes to hand, ruined or wild. A landless titular title's
        // capital is a de jure pointer and nothing else; it is never anybody's seat, and vanilla
        // does exactly this with head-of-faith titles like k_orthodox.
        var ruinCapital = wilderness.Ruins.FirstOrDefault() ?? wildCapital;
        if (wilderness.RuinsEnabled && ruinCapital is not null)
        {
            jb.Comment("The ruins. A second titular realm so fallen ground is not labelled wilderness.");
            jb.Blank();

            using (jb.Block(WildernessMap.RuinsTitleKey))
            {
                // Darker and browner than the wilderness grey: on a realm-coloured map mode the two
                // read as the same kind of absence, which they are, without being the same realm.
                jb.Inline("color", "82", "72", "62");
                jb.Field("capital", ruinCapital.Key);
                jb.Field("landless", "yes");
                jb.Field("definite_form", "yes");
                jb.Field("ruler_uses_title_name", "no");
            }

            jb.Blank();
        }

        ParadoxText.WriteBom(Path.Combine(dir, "00_landed_titles.txt"), jb.ToString());
        return;

        static string F(double v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

        // The depth argument is gone: the builder tracks it, so the de jure tree is walked without
        // the recursion also having to carry its own indentation.
        void Write(Title title)
        {
            using (jb.Block(title.Key))
            {
                jb.Inline("color", $"{title.Color.R}", $"{title.Color.G}", $"{title.Color.B}");

                if (title.Tier == "b")
                {
                    // `province` is the only barony key here. Wonders are placed by province history
                    // (see WriteProvinceHistory) — landed_titles has no special_building key.
                    jb.Field("province", title.ProvinceId);
                    foreach (string field in title.InheritedFields) jb.Token(field);
                }
                else
                {
                    // A vanilla county says what it says about itself; vanilla's default is no.
                    if (title.Tier == "c" && !title.Inherited) jb.Field("definite_form", "no");

                    // Stated as vanilla states it on its own duchies and above, though the engine
                    // would take the first county anyway: the two agree because MapGen/Capitals
                    // put the capital first, and saying so keeps a hand edit of the order from
                    // silently moving the seat.
                    if (title.Tier == "h" && hegemonSeat is not null)
                        jb.Field("capital", hegemonSeat.Key);
                    else if (title.Tier is "d" or "k" or "e" or "h"
                        && MapGen.Capitals.CapitalCounty(title) is { } seat)
                        jb.Field("capital", seat.Key);

                    if (title.Tier == "h")
                    {
                        // Vanilla's own shape for a hegemony: it is "the <name>", and it is never
                        // renamed after whoever holds it. This title stands for the world rather
                        // than for a house.
                        jb.Field("definite_form", "yes");
                        jb.Field("can_be_named_after_dynasty", "no");
                        jb.Field("disable_regnal_numbers", "yes");

                        // Creation runs through the generated decision and nowhere else. Left open,
                        // the title-creation UI would sell the world for 2400 gold to anyone
                        // holding two empires — CREATE_TITLE_OR_TIER_HEGEMONY asks for no more than
                        // that. Closing it here costs the decision nothing, because
                        // create_title_and_vassal_change does not consult can_create.
                        jb.Inline("can_create", "always = no");
                        jb.Inline("can_create_on_partition", "always = no");
                    }

                    // A vanilla title's own declaration — cultural names, succession flags, its
                    // creation trigger — ahead of the children this map gives it. Empty for a
                    // generated title. See VanillaTitles.
                    foreach (string field in title.InheritedFields) jb.Token(field);

                    // The capital first: for a county that is the whole declaration of its seat,
                    // since baronies carry no capital field. Write-time order only.
                    foreach (var child in title.SeatFirst()) Write(child);

                    // The nine seats of the celestial ministry, as vanilla declares them inside
                    // h_china (02_china.txt:8603): landless de jure children, granted by script to
                    // whoever the hegemon appoints. Inside this block because vanilla's own script
                    // tests `de_jure_liege = title:h_china` on them; after the empires because a
                    // Title child's position is load-bearing and these are not Title children.
                    // Fields match the no-hegemony shim in CompatibilityWriter, which owns the list.
                    if (title.Tier == "h"
                        && (hegemonSeat ?? MapGen.Capitals.CapitalCounty(title)) is { } ministrySeat)
                    {
                        int i = 0;
                        foreach (string minister in CompatibilityWriter.MinistryTitles)
                        {
                            var (r, g, bl) = MapDataWriter.ProvinceColor(1000 + i++);
                            using (jb.Block(minister))
                            {
                                jb.Inline("color", $"{r}", $"{g}", $"{bl}");
                                jb.Field("landless", "yes");
                                jb.Field("capital", ministrySeat.Key);
                                jb.Inline("can_create", "always = no");
                                jb.Inline("can_create_on_partition", "always = no");
                                jb.Field("no_automatic_claims", "yes");
                                jb.Inline("ai_primary_priority", "add = -1000");
                                jb.Field("allow_domicile", "no");
                                jb.Field("destroy_if_invalid_heir", "yes");
                                jb.Field("de_jure_drift_disabled", "yes");
                                jb.Field("can_use_nomadic_naming", "no");
                                jb.Field("can_be_named_after_dynasty", "no");
                                jb.Field("definite_form", "yes");
                                jb.Field("ruler_uses_title_name", "no");
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// The landless family titles, in a file of their own.
    ///
    /// Separate from 00_landed_titles.txt for a scheduling reason rather than a stylistic one: that
    /// file is written in the titles stage, and the houses these are named for do not exist until
    /// the history stage several steps later. A second file in the same directory is how vanilla
    /// carries its own additions (02_china.txt, 02_japan.txt) and the engine loads the directory
    /// whole, so the `capital` pointers into 00_landed_titles.txt resolve either way.
    ///
    /// Every field is vanilla's, from the sixty-one d_nf_ blocks at 00_landed_titles.txt:76374.
    /// The two that would be easy to leave off are the two that matter most: `landless` is what
    /// keeps the title off the map, and `noble_family` is the whole point — without it the engine
    /// sees a titular duchy and none of the administrative machinery looks at it.
    /// </summary>
    internal static void WriteNobleFamilyTitles(string modDir, MapGen.PrehistoryMap prehistory)
    {
        string dir = Path.Combine(modDir, "common", "landed_titles");
        Directory.CreateDirectory(dir);

        var jb = new JominiBuilder(JominiStyle.Spaced);

        jb.Comment("Generated noble families. One landless title per house head under a government");
        jb.Comment("that appoints from houses; see MapGen/Prehistory.RebuildNobleFamilies.");
        jb.Blank();

        foreach (var family in prehistory.NobleFamilies)
        {
            using (jb.Block(family.TitleKey))
            {
                // Vanilla's flat grey for every one of them. The colour of a landless title is only
                // ever seen in a list beside its name, and a family is not a place.
                jb.Inline("color", "100", "100", "100");

                // A de jure pointer and nothing else — the holder's own seat rather than the realm
                // capital vanilla uses, so a family reads as being FROM somewhere.
                jb.Field("capital", family.HolderCounty.Key);

                jb.Field("definite_form", "yes");
                jb.Field("landless", "yes");
                jb.Field("noble_family", "yes");

                // The family follows the heir who takes the land, rather than being partitioned off
                // to a second son as an inheritance in its own right.
                jb.Field("always_follows_primary_heir", "yes");
                jb.Field("no_automatic_claims", "yes");
                jb.Field("destroy_if_invalid_heir", "yes");

                // He is styled for his land, not for his family: a duke who also holds one of these
                // is still called duke.
                jb.Field("ruler_uses_title_name", "no");
                jb.Inline("ai_primary_priority", "add = -1000");
            }

            jb.Blank();
        }

        ParadoxText.WriteBom(Path.Combine(dir, "01_generated_noble_families.txt"), jb.ToString());
    }

    /// <summary>
    /// Everything the debug panel bakes into the mod: what this run decided, gathered in one place
    /// so the window can report it back from inside the game.
    ///
    /// Deliberately a plain projection of things already computed above — it counts, it does not
    /// decide. A number here that disagreed with what was written would be worse than no panel at
    /// all, because the panel's whole use is as the thing you trust when the game disagrees with
    /// the log.
    /// </summary>
    private static DebugPanel.Facts DebugFacts(string modDir, MapConfig cfg, ProvinceMap provinces,
        List<Title> empires, List<Title> counties, MapGen.CultureMap cultures,
        MapGen.FaithMap faiths, MapGen.WildernessMap wilderness, WorldCenterMap worldCenters,
        MapGen.RetinueMap? retinues, int landCount, int riverCount, int baronyCount,
        int artifactCount, int struggleCount, bool writeHistory, MapGen.AzgaarImport? azgaar,
        DateTime runStarted)
    {
        var all = Titles.Flatten(empires);

        // By tier prefix: a generated head is always a duchy, but a vanilla one kept by a world of
        // vanilla faiths can be a kingdom (k_papal_state, k_orthodox).
        var headKeys = faiths.Faiths
            .Where(f => f.Head is not null)
            .Select(f => f.Head!.TitleKey)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        int faithHeads = headKeys.Count(k => k.StartsWith("d_", StringComparison.Ordinal));
        int faithHeadKingdoms = headKeys.Count(k => k.StartsWith("k_", StringComparison.Ordinal));

        return new DebugPanel.Facts
        {
            // The folder, not the mod's display name: this method never sees GenerationOptions, and
            // the folder is what a person looking for the files on disk would actually type.
            ModName = Path.GetFileName(modDir.TrimEnd(Path.DirectorySeparatorChar,
                                                      Path.AltDirectorySeparatorChar)),
            // The informational version carries the whole commit SHA, which is forty characters of
            // a row that has two hundred and forty pixels. Seven is what every git UI shows and is
            // enough to find the commit.
            ToolVersion = ShortVersion(Core.RunLog.ToolVersion()),
            Generated = runStarted.ToString("yyyy-MM-dd HH:mm") + " UTC",
            Seed = cfg.Seed,
            StartYear = cfg.StartYear,

            Width = cfg.Width,
            Height = cfg.Height,
            LandProvinces = landCount,
            // The three ranges default.map is written from, read the same way MapDataWriter reads
            // them, so the panel and default.map cannot disagree about where the sea starts.
            Rivers = riverCount - landCount,
            WaterProvinces = provinces.Count - riverCount,
            Baronies = baronyCount,

            // The de jure hierarchy is not everything WriteLandedTitles emits, and the panel has to
            // count what SHIPPED or its two columns are measuring different things. Two additions,
            // both landless and both invisible to Titles.Flatten:
            //
            //   * one duchy-tier title per faith with a head of faith;
            //   * one kingdom-tier title, k_gen_wilderness, when there is any unsettled land;
            //   * a second, k_gen_ruins, whenever the ruins system ships — including on a world
            //     that starts with no ruined county at all, which is the default.
            //
            // Distinct rather than a plain count, because two faiths may name the same head and the
            // database keeps one title either way.
            Empires = all.Count(t => t.Tier == "e"),
            Kingdoms = all.Count(t => t.Tier == "k")
                     + faithHeadKingdoms
                     + (wilderness.Unsettled.Any() ? 1 : 0)
                     + (wilderness.RuinsEnabled && wilderness.Counties.Any() ? 1 : 0),
            Duchies = all.Count(t => t.Tier == "d") + faithHeads,
            LandlessDuchies = faithHeads,
            Counties = counties.Count,

            Cultures = cultures.Cultures.Count,
            Heritages = cultures.Heritages.Count,
            Faiths = faiths.Faiths.Count,
            Religions = faiths.Religions.Count,

            Wonders = worldCenters.Centers.Count,
            WildernessCounties = wilderness.Count,
            Artifacts = artifactCount,
            Struggles = struggleCount,
            MenAtArms = retinues?.Regiments.Count ?? 0,

            Source = azgaar is null ? "procedural" : "Azgaar import",
            Races = cfg.EnableFantasyEthnicities
                ? cfg.RaceMode.ToString()
                : "human only",
            Wilderness = cfg.EnableWilderness,
            Magic = cfg.EnableMagic,
            Retinues = retinues is not null,
            History = writeHistory,

            // The same condition WonderIndex.Write returns early on. Said twice rather than shared,
            // because what this asks is "was that file written" and what that asks is "is there
            // anything to put in it" — they agree today and are not the same question.
            HasWonderIndex = worldCenters.Centers.Count > 0,
        };
    }

    /// <summary>
    /// <c>1.2.3+abcdef0123…</c> cut down to <c>1.2.3+abcdef0</c>. Anything without a <c>+</c> is
    /// already short and passes through.
    /// </summary>
    private static string ShortVersion(string version)
    {
        int plus = version.IndexOf('+');
        if (plus < 0 || version.Length - plus <= 8) return version;

        return version[..(plus + 8)];
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

    /// <summary>
    /// The terrain each province is written as, from the pixels inside it.
    ///
    /// A plurality of those pixels for vegetation, which is the right answer there — a province
    /// that is mostly jungle is a jungle province — and the wrong one for the two relief tiers. A
    /// range is a linear feature, and the hill band ringing it is three times its size by
    /// construction: <see cref="TerrainClassifier"/> gives mountains the top 3.3% of land by
    /// elevation and hills the 9.4% below. So in any province a ridge crosses, the hills outnumber
    /// the peaks and take the vote, and the taller the ground the more reliably they do it.
    ///
    /// Measured before this, on a 5,400-barony run: <c>mountains</c> won 0.00% of baronies and
    /// <c>hills</c> 5.48%, against vanilla's 12.15% and 19.20%. All 35 mountain provinces the vote
    /// produced were inside the impassable range — that is, the pass that takes provinces out of
    /// play had already claimed every one of them, so no county on the map sat on mountains at all.
    /// Even among the 128 steepest provinces there, the vote still returned hills for 64 and
    /// mountains for 24.
    ///
    /// The fix is not a bigger pixel budget. Those pixels are what the ground is *painted* from,
    /// and widening the band paints mountain rock over ground the heightmap renders flat — the
    /// regression recorded on <see cref="TerrainClassifier"/>'s MountainShareOfLand, which is why
    /// that constant is 3.3% and should stay there. The two consumers want different things:
    /// terrain in common/province_terrain is a claim about a province's *character*, which is what
    /// the movement and combat rules read and what a hand-authored map states, while the texture
    /// underneath stays free to show the valley floor that is really there. Vanilla is exactly this
    /// — an Alpine province is named for its range whatever share of it is pasture.
    ///
    /// So the pixels keep the relief and the province is promoted on rank instead: the baronies
    /// carrying the most mountain-band ground are named for the range they hold. Nothing repaints
    /// the raster to match, deliberately — that would reintroduce the rock-on-flat-ground
    /// regression, and is the one way this differs from <see cref="MapGen.Cultivation"/>, which
    /// does repaint because a farmland province genuinely has to look like fields.
    ///
    /// Ranked over baronies alone and then applied to every land province, so the impassable ones —
    /// which sit at the top of the relief distribution and are not counties — cannot eat a target
    /// share that is meant to describe playable ground.
    /// </summary>
    public static TerrainClass[] ProvinceTerrain(MapConfig cfg, ProvinceMap provinces,
        int[] order, TerrainClass[] terrain, float[] provinceElevation, int landCount,
        int baronyCount, bool report = false)
    {
        int width = cfg.ProvinceWidth, height = cfg.ProvinceHeight;
        int classes = Enum.GetValues<TerrainClass>().Length;

        var votes = new int[(provinces.Count + 1) * classes];
        var counted = new int[provinces.Count + 1];

        // Land pixels, counted separately from votable ones because beach is land that must dilute a
        // coastal province's relief share while still being kept out of the plurality below.
        var land = new int[provinces.Count + 1];
        var landMask = new byte[width * height];

        // The relief lines are drawn over barony ground only, never over all land. The impassable
        // provinces are the highest ground on the map by the very score that selected them, and
        // they are not counties — measured against a line that counts them, the top
        // MountainProvinceShare of *land* is a far smaller share of the ground baronies actually
        // hold, and the tier cannot fill however the rank is set. Against a line that does not,
        // the setting means what it says: a share of playable ground.
        var baronyMask = new byte[width * height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                int id = order[provinces.Label[i]];
                if (id > landCount) continue;

                landMask[i] = 1;
                if (id <= baronyCount) baronyMask[i] = 1;
                land[id]++;

                var t = terrain[i];
                if (t is TerrainClass.Sea or TerrainClass.Beach) continue;
                votes[id * classes + (int)t]++;
                counted[id]++;
            }
        }

        int Pixels(int id, TerrainClass t) => votes[id * classes + (int)t];

        // Relief is read off the elevation, not off the painted class, and that is the whole point
        // of this pass rather than an implementation detail.
        //
        // The painted mountain class is the top 3.3% of land by height and has to stay there — it
        // is what the ground is *textured* from, and widening it paints rock over ground the
        // heightmap renders flat (the regression recorded on TerrainClassifier.MountainShareOfLand).
        // Measured, that band reaches almost nothing: on a 4,169-barony run only 0.9% of baronies
        // held a single mountain pixel and 6.5% a single hill pixel, because the impassable pass
        // takes the steepest provinces first and fuses them, and because Arctic relabels high
        // ground in polar latitudes before Hills ever sees it. A rank over that band can only
        // promote what survives it, which is why ranking the painted class moved 0.3% to mountains
        // and left the tier as broken as it found it.
        //
        // Elevation has none of those holes. Every land pixel has a height, whatever was painted on
        // top of it, so a polar range and a range the impassable pass declined both still read as
        // high ground here.
        //
        // The lines are drawn at the same shares as the tiers they feed, which makes the two
        // settings one idea rather than two: a province is mountains when it holds more than its
        // even share of the highest MountainProvinceShare of land. Uniform relief would give every
        // province exactly that share and the ranking would mean nothing — which is what
        // ReliefPromotionFloor is there to catch — but relief is never uniform, and the ranking is
        // the whole signal on any world with ranges in it.
        double mountainTarget = Math.Clamp(cfg.MountainProvinceShare, 0, 1);
        double hillTarget = Math.Clamp(cfg.HillProvinceShare, 0, 1);
        double reliefTarget = Math.Clamp(mountainTarget + hillTarget, 0, 1);

        float mountainLine = mountainTarget <= 0 ? float.MaxValue
            : TerrainClassifier.LandPercentile(provinceElevation, baronyMask, 1.0 - mountainTarget);
        float hillLine = reliefTarget <= 0 ? float.MaxValue
            : TerrainClassifier.LandPercentile(provinceElevation, baronyMask, 1.0 - reliefTarget);

        var above = new int[provinces.Count + 1];   // pixels over the mountain line
        var high = new int[provinces.Count + 1];    // pixels over the hill line, mountains included

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                if (landMask[i] == 0) continue;

                int id = order[provinces.Label[i]];
                float e = provinceElevation[i];
                if (e >= mountainLine) above[id]++;
                if (e >= hillLine) high[id]++;
            }
        }

        // Nested by construction — the mountain line is above the hill line — so a province can
        // never rank above the mountain cut and below the hill one, and the tiers stay ordered
        // however the two cuts happen to fall.
        double MountainBand(int id) => land[id] == 0 ? 0 : above[id] / (double)land[id];
        double HillBand(int id) => land[id] == 0 ? 0 : high[id] / (double)land[id];

        // Which kind of range, for a province promoted on height alone: it may hold no painted
        // mountain pixel at all, so the question is what the ground around it is, not what the peak
        // was painted as.
        bool Arid(int id) => counted[id] > 0 &&
            Pixels(id, TerrainClass.Desert) + Pixels(id, TerrainClass.Drylands)
            + Pixels(id, TerrainClass.DesertMountains) + Pixels(id, TerrainClass.Oasis)
            > counted[id] / 2;

        double floor = Math.Clamp(cfg.ReliefPromotionFloor, 0, 1);
        int baronies = Math.Clamp(baronyCount, 0, landCount);

        // The band share at the target rank, floored. Returns infinity — promoting nothing — when
        // the tier is switched off or there is nothing to rank, which leaves the plain vote below.
        double ReliefCut(Func<int, double> band, double target)
        {
            if (target <= 0 || baronies <= 0) return double.PositiveInfinity;

            var ranked = new List<double>(baronies);
            for (int id = 1; id <= baronies; id++)
                if (land[id] > 0) ranked.Add(band(id));
            if (ranked.Count == 0) return double.PositiveInfinity;

            ranked.Sort(static (a, b) => b.CompareTo(a));

            int want = (int)Math.Round(ranked.Count * Math.Clamp(target, 0, 1));
            if (want <= 0) return double.PositiveInfinity;

            return Math.Max(ranked[Math.Min(want, ranked.Count) - 1], floor);
        }

        double mountainCut = ReliefCut(MountainBand, mountainTarget);

        // Cut on the combined band at the combined share, so this names the provinces left once the
        // mountains above have taken theirs rather than competing with them for the same rank.
        double hillCut = ReliefCut(HillBand, reliefTarget);

        var result = new TerrainClass[provinces.Count + 1];
        for (int id = 1; id <= provinces.Count; id++)
        {
            if (id > landCount) { result[id] = TerrainClass.Sea; continue; }

            // The `> 0` is load-bearing, not belt and braces: a floor of 0 on a world whose ranked
            // cut is also 0 would otherwise promote every province, mountain pixels or not.
            double mountain = MountainBand(id);
            if (mountain > 0 && mountain >= mountainCut)
            {
                result[id] = Arid(id) ? TerrainClass.DesertMountains : TerrainClass.Mountains;
                continue;
            }

            double hill = HillBand(id);
            if (hill > 0 && hill >= hillCut) { result[id] = TerrainClass.Hills; continue; }

            int best = -1, bestCount = 0;
            for (int c = 0; c < classes; c++)
            {
                int n = votes[id * classes + c];
                if (n > bestCount) { bestCount = n; best = c; }
            }

            result[id] = best < 0 ? TerrainClass.Plains : (TerrainClass)best;
        }

        if (report)
        {
            ReportBand("mountain", MountainBand);
            ReportBand("hill", HillBand);
            ReportProvinceTerrain(result, baronies, mountainCut, hillCut);
        }

        return result;

        // How much relief the baronies actually hold, which is the only thing that decides whether
        // the ranks above can be met at all. Worth printing rather than inferring from the outcome:
        // a cut sitting on its floor means either the world is flat or something upstream took the
        // ground before the vote saw it, and the percentiles tell those two apart at a glance.
        void ReportBand(string name, Func<int, double> band)
        {
            var ranked = new List<double>(baronies);
            for (int id = 1; id <= baronies; id++)
                if (land[id] > 0) ranked.Add(band(id));
            if (ranked.Count == 0) return;

            ranked.Sort();
            string At(double q) => $"{ranked[Math.Clamp((int)(ranked.Count * q), 0, ranked.Count - 1)]:P1}";
            int any = ranked.Count(v => v > 0);

            Console.WriteLine($"    {name} band over baronies: p50 {At(0.50)}, p75 {At(0.75)}, " +
                              $"p90 {At(0.90)}, p99 {At(0.99)}, max {ranked[^1]:P1}; " +
                              $"{any} of {ranked.Count} hold any ({100.0 * any / ranked.Count:F1}%) — " +
                              "a tier cannot fill past that last figure");
        }
    }

    /// <summary>
    /// What the vote came out with, over baronies only — the impassable provinces sit at the top of
    /// the relief distribution and are not counties, so counting them would flatter exactly the two
    /// tiers worth watching. Vanilla's own shares are quoted beside it because these are calibrated
    /// against them and a drift is otherwise invisible without opening its files.
    /// </summary>
    private static void ReportProvinceTerrain(TerrainClass[] provinceTerrain, int baronyCount,
        double mountainCut, double hillCut)
    {
        if (baronyCount <= 0) return;

        static string Cut(double c) => double.IsPositiveInfinity(c) ? "off" : $"{c:P1}";
        Console.WriteLine($"  relief promotion: mountains from {Cut(mountainCut)} of a province " +
                          $"above the mountain line, hills from {Cut(hillCut)} above the hill line");

        var counts = new Dictionary<string, int>();
        for (int id = 1; id <= baronyCount && id < provinceTerrain.Length; id++)
        {
            string name = TerrainClassifier.Name(provinceTerrain[id]);
            counts[name] = counts.GetValueOrDefault(name) + 1;
        }

        var parts = counts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key} {100.0 * kv.Value / baronyCount:F1}%");

        Console.WriteLine($"  province terrain (share of {baronyCount} baronies): {string.Join(", ", parts)}");
        Console.WriteLine("    vanilla for comparison: plains 19.6%, hills 19.2%, mountains 12.2%, " +
                          "drylands 9.5%, forest 8.0%, desert 6.8%, jungle 5.3%, taiga 5.2%, " +
                          "steppe 3.2%, wetlands 2.8%, desert_mountains 2.5%, farmlands 2.2%, " +
                          "floodplains 2.2%, oasis 0.5%");
    }

    private static void WriteProvinceTerrain(string modDir, TerrainClass[] terrain, int landCount)
    {
        string dir = Path.Combine(modDir, "common", "province_terrain");
        Directory.CreateDirectory(dir);

        // Compact style: province_terrain is written id=terrain with no spaces, as vanilla's is.
        var b = new JominiBuilder(JominiStyle.Compact);
        b.Field("default_land", "plains");
        b.Field("default_sea", "sea");
        b.Field("default_coastal_sea", "coastal_sea");

        for (int id = 1; id <= landCount; id++)
            b.Field($"{id}", TerrainClassifier.Name(terrain[id]));

        ParadoxText.WriteBom(Path.Combine(dir, "00_province_terrain.txt"), b.ToString());
    }

    private static void ReportDevelopment(Dictionary<Title, int> development)
    {
        if (development.Count == 0) return;
        var levels = development.Values.OrderBy(v => v).ToList();
        Console.WriteLine($"  development: min {levels[0]}, median {levels[levels.Count / 2]}, " +
                          $"p90 {levels[(int)(levels.Count * 0.9)]}, max {levels[^1]} " +
                          $"(vanilla 867 counties that set one: median 6, p90 12, ordinary top 20, peak 30)");
    }

    /// <summary>
    /// How much of a wonder already stands on the start date: 0 for an empty slot, up to
    /// <see cref="GeneratedWonder.Tiers"/> for one finished long ago.
    ///
    /// A monument is a claim about how long a place has been important and how much surplus it had
    /// to spend on something that is not a wall or a granary. Three things argue about that, and
    /// none of them is decisive on its own:
    ///
    /// The era. A world that starts a century into its own history has had time to finish things a
    /// world starting at its dawn has not. Read from <see cref="MapConfig.EraYear"/> rather than
    /// StartYear, because a fictional calendar can put the same era at any number.
    ///
    /// The wealth around it. Development is the generator's own measure of how much a place could
    /// afford, and every centre is already placed at the top of the world by
    /// <see cref="MapGen.Development.ForCounties"/>, so this asks whether the county is rich even
    /// by the standard of the other centres.
    ///
    /// And who holds it. A tribal or nomadic realm builds differently — not worse, but a permanent
    /// monument in stone is a settled people's answer, and a horde's capital having a finished
    /// palace on day one reads wrong in a way the other two do not.
    ///
    /// Deliberately a weighting rather than a table: every combination stays possible, so a rich
    /// late-era feudal centre is usually well along and occasionally has nothing but foundations,
    /// which is the more interesting world to be handed.
    /// </summary>
    private static int StartingWonderTier(
        GeneratedWonder wonder, Dictionary<Title, int> development, int ordinaryTopDevelopment,
        GovernmentMap governments, MapConfig cfg, Rng rng)
    {
        int score = 0;

        // Era. The anchor is 1000 because that is roughly where vanilla's own start dates put a
        // settled, building world; earlier is a younger age, later a more finished one.
        int era = cfg.EraYear;
        if (era >= 1200) score += 3;
        else if (era >= 1000) score += 2;
        else if (era >= 850) score += 1;

        // Wealth RELATIVE to the world, which is the only way this term says anything. Absolute
        // thresholds were tried first and were worthless: every centre is rich by construction, so
        // any fixed number scores all of them identically. What varies — and what a monument
        // actually reflects — is how far above its neighbours a place stands.
        //
        // Measured against the best ORDINARY county rather than the world median, because the
        // median moves with the era and the centre band moves with it, so the ratio to the median
        // drifts down as the world gets richer and the bands would have to be retuned per era. The
        // top of the ordinary curve moves in step with the centres instead, which keeps this
        // stable: a centre runs about 1.1x to 1.35x the best ordinary county at any era.
        double ratio = development.GetValueOrDefault(wonder.County)
                     / (double)Math.Max(1, ordinaryTopDevelopment);

        // Bands chosen from that measured spread rather than from what "rich" sounds like. With
        // Development.ForCounties placing centres between ordinaryTop + 2 and WorldCenterDevPeak,
        // the default five land at roughly 1.09, 1.14, 1.23, 1.27 and 1.36 — so these thirds split
        // the population instead of scoring it all the same. The question being asked is
        // "exceptional among world centres", which is the only one with an answer that varies.
        if (ratio >= 1.30) score += 3;
        else if (ratio >= 1.20) score += 2;
        else if (ratio >= 1.10) score += 1;

        switch (GovernmentMap.Family(governments.For(wonder.County)))
        {
            case GovernmentMap.Administrative: score += 2; break;
            case GovernmentMap.Feudal:
            case GovernmentMap.Republic:
            case GovernmentMap.Theocracy: score += 1; break;

            // Tribal and clan build, but not in this idiom. Nomads least of all.
            case GovernmentMap.Nomad: score -= 2; break;
            case GovernmentMap.Tribal: score -= 1; break;
        }

        // The roll. Score shifts the odds; it never picks the answer.
        //
        // Bands set so a middling centre — score around four — is usually a tier or two along and
        // occasionally bare ground: roughly 10% nothing, 50% tier one, 30% tier two, 10% finished.
        // A primitive one (early era, tribal, barely above its neighbours) scores zero and cannot
        // reach tier two at all; a rich late administrative capital scores eight and finishes half
        // the time.
        int roll = rng.Int(0, 9) + score;

        return roll switch
        {
            >= 13 => 3,
            >= 10 => 2,
            >= 5 => 1,
            _ => 0,
        };
    }

    /// <summary>
    /// One barony's line in the province history, minus its holding.
    ///
    /// Everything here was decided by a draw or by a generator that cannot be run again on its own
    /// — the wonder tier comes off the world centres and the era, the market off the Silk Road
    /// route — so it is captured rather than replayed, and a later re-emit writes back exactly what
    /// the first write did. The holding is the one thing an edit can move, and it lives in
    /// <see cref="WrittenContent.Holdings"/> so that there is a single place to move it.
    /// </summary>
    public sealed record ProvinceRow(
        int ProvinceId, string Culture, string Faith, string? SpecialSlot, string? SpecialBuilding);

    /// <summary>
    /// The seat holdings the additional bookmarks' governments need, oldest date first, by province
    /// id and only where they differ from the start date's; and the date the start date's holding is
    /// restored on after an earlier one, which is the day its holders take their titles.
    /// </summary>
    public sealed record EraHoldings(List<(int Year, string Date, Dictionary<int, string> Capitals)> Dates,
        int MainYear, string RevertDate);

    /// <summary>
    /// Which seats an additional bookmark sits in under a different kind of holding than today: a
    /// county its ruler governs as a tribe needs a tribal hall, one he governs as a lord a castle.
    /// The seat only — a county's other holdings are ones any government may keep — and never a
    /// holding no government seats in, such as a wonder's or the wilderness's.
    /// </summary>
    private static EraHoldings? BuildEraHoldings(MapConfig cfg, List<Title> empires, WildernessMap wilderness,
        Dictionary<int, GovernmentMap>? eraGovernments, IReadOnlyDictionary<int, string> holdings)
    {
        if (eraGovernments is null) return null;

        string[] seatHoldings = ["castle_holding", "tribal_holding", "city_holding", "church_holding",
            "nomad_holding", "temple_citadel_holding"];

        var dates = new List<(int, string, Dictionary<int, string>)>();
        foreach (var (year, governments) in eraGovernments.OrderBy(kv => kv.Key))
        {
            var capitals = new Dictionary<int, string>();
            foreach (var county in Titles.Flatten(empires).Where(t => t.Tier == "c" && !wilderness.Contains(t)))
            {
                if (county.Capital is not { ProvinceId: > 0 } seat) continue;
                if (!holdings.TryGetValue(seat.ProvinceId, out var today) || !seatHoldings.Contains(today)) continue;

                string then = GovernmentMap.CapitalHolding(BookmarkEras.EraGovernment(governments.For(county)));
                if (then != today) capitals[seat.ProvinceId] = then;
            }

            dates.Add((year, $"{year - 1}.1.1", capitals));
            Console.WriteLine($"  additional bookmark {year}: {capitals.Count} seats held as "
                + string.Join(", ", capitals.Values.GroupBy(h => h).OrderByDescending(g => g.Count())
                    .Select(g => $"{g.Count()} {g.Key.Replace("_holding", "")}")));
        }

        return new EraHoldings(dates, cfg.StartYear, $"{Math.Max(1, cfg.StartYear - 5)}.1.1");
    }

    /// <summary>
    /// Writes the province history from rows already decided.
    ///
    /// Split from <see cref="BuildProvinceHistory"/> so that changing a realm's government after
    /// the mod is written can re-emit this file without re-rolling anything: the holdings come off
    /// a single <see cref="Rng"/> walked across every barony in the world in one pass, and a county
    /// whose government had changed would desync the rest of that stream. One serialiser either
    /// way — the generator calls Build then this, an overwrite calls this alone.
    /// </summary>
    internal static void EmitProvinceHistory(string modDir, IReadOnlyList<ProvinceRow> rows,
        IReadOnlyDictionary<int, string> holdings, EraHoldings? eras = null)
    {
        string dir = Path.Combine(modDir, "history", "provinces");
        Directory.CreateDirectory(dir);

        // Four spaces again, matching landed_titles above.
        var b = new JominiBuilder(JominiStyle.Spaced);

        foreach (var row in rows)
        {
            using (b.Block(row.ProvinceId))
            {
                string holding = holdings.GetValueOrDefault(row.ProvinceId, "none");
                b.Field("culture", row.Culture);
                b.Field("religion", row.Faith);
                b.Field("holding", holding);

                if (row.SpecialSlot is { } slot) b.Field("special_building_slot", slot);
                if (row.SpecialBuilding is { } building) b.Field("special_building", building);

                // A seat an additional bookmark's ruler sits in under another government: that
                // government's holding from its date, and the start date's back when its holder
                // takes it — the way vanilla's tribal halls become castles between its bookmarks.
                if (eras is null) continue;

                string state = holding;
                void Apply(IEnumerable<(int Year, string Date, Dictionary<int, string> Capitals)> which)
                {
                    foreach (var (_, date, capitals) in which)
                    {
                        string then = capitals.GetValueOrDefault(row.ProvinceId, holding);
                        if (then == state) continue;
                        using (b.Block(date)) b.Field("holding", then);
                        state = then;
                    }
                }

                Apply(eras.Dates.Where(d => d.Year < eras.MainYear));

                if (state != holding)
                {
                    using (b.Block(eras.RevertDate)) b.Field("holding", holding);
                    state = holding;
                }

                Apply(eras.Dates.Where(d => d.Year > eras.MainYear));
            }
        }

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_provinces.txt"), b.ToString());
    }

    /// <returns>
    /// Every barony's line, in the order it is written, and the holding written for each of them by
    /// province id — see <see cref="WrittenContent.Holdings"/> for why the second is kept rather
    /// than replayed.
    /// </returns>
    private static (List<ProvinceRow> Rows, Dictionary<int, string> Holdings) BuildProvinceHistory(
        MapConfig cfg,
        List<Title> empires,
        TerrainClass[] provinceTerrain, Dictionary<Title, int> development, CultureMap cultures,
        FaithMap faiths, GovernmentMap governments, WildernessMap wilderness,
        WorldCenterMap worldCenters, SilkRoadMap silkRoad, int cfgSeed, AzgaarImport? azgaar)
    {
        var rng = new Rng(cfgSeed ^ 0x8A12);
        var counts = new Dictionary<string, int>();
        var importedHoldings = new Dictionary<string, int>();
        var holdings = new Dictionary<int, string>();
        var rows = new List<ProvinceRow>();

        // The richest county that is NOT a world centre, for the wonder roll below to measure its
        // centres against. The counties being judged are excluded on purpose: the yardstick has to
        // be the world they stand above, and every centre is placed above the ordinary curve by
        // construction, so leaving them in would measure them partly against themselves.
        //
        // The world median was the yardstick before and drifts with the era — see
        // StartingWonderTier — whereas this moves in step with the centres and keeps the bands
        // meaningful at any start date.
        int ordinaryTopDevelopment = development
            .Where(kv => !worldCenters.IsCenter(kv.Key))
            .Select(kv => kv.Value)
            .DefaultIfEmpty(1)
            .Max();

        var wondersByBarony = worldCenters.Centers
            .ToDictionary(wc => wc.CapitalBarony, wc => wc.Wonder);

        foreach (var county in Titles.Flatten(empires).Where(t => t.Tier == "c"))
        {
            int level = development.GetValueOrDefault(county);
            string cultureKey = cultures.For(county).Key;
            string faith = faiths.For(county).Key;
            string government = governments.For(county);
            bool wild = wilderness.Contains(county);

            // Seat first: index zero is the capital holding, and the seat is the capital. The
            // list itself is not reordered — see Title.Seat.
            var baronies = county.SeatFirst().ToList();

            // The town an export drew on each barony, where it drew one big enough to be a holding
            // in its own right. The seat is skipped: its holding is whatever its ruler's government
            // seats him in, so a burg there says nothing new and must not count toward whether the
            // export has already settled a second barony of this county.
            var settlements = new AzgaarBurg?[baronies.Count];
            bool settledByExport = false;

            // A horde's county is its camp and nothing else whatever was drawn on it, so a burg
            // there settles nothing — and counting one would make the log claim holdings the world
            // does not have.
            if (azgaar is not null && !wild && government != GovernmentMap.Nomad)
            {
                for (int i = 1; i < baronies.Count; i++)
                {
                    var burg = azgaar.For(baronies[i])?.Burgs
                        .FirstOrDefault(b => AzgaarSettlement.IsHolding(b, azgaar.MedianBurgPopulation));
                    if (burg is null) continue;

                    settlements[i] = burg;
                    settledByExport = true;

                    string kind = AzgaarSettlement.Describe(burg);
                    importedHoldings[kind] = importedHoldings.GetValueOrDefault(kind) + 1;
                }
            }

            for (int i = 0; i < baronies.Count; i++)
            {
                var barony = baronies[i];
                var terrain = barony.ProvinceId >= 0 && barony.ProvinceId < provinceTerrain.Length
                    ? provinceTerrain[barony.ProvinceId]
                    : TerrainClass.Plains;

                string holding = wild
                    ? (i == 0 ? "wilderness_holding" : "none")
                    : MapGen.Development.Holding(i, terrain, level, government, rng,
                                                 settlements[i], settledByExport);

                // A Silk Road bazaar displaced from the seat by a wonder lands on whatever barony
                // is next, which the roll above usually leaves empty. A market is a town, so it
                // gets one; the roll is still made, so nothing else on the map moves.
                if (holding == "none" && silkRoad.MarketAt(barony) is not null) holding = "city_holding";

                counts[holding] = counts.GetValueOrDefault(holding) + 1;
                holdings[barony.ProvinceId] = holding;

                string? slot = null, building = null;

                if (wondersByBarony.TryGetValue(barony, out var wonder))
                {
                    // How far up its own ladder this wonder already is on the start date.
                    int built = StartingWonderTier(
                        wonder, development, ordinaryTopDevelopment, governments, cfg,
                        new Rng(barony.ProvinceId ^ 0x5C0E));

                    // The slot is declared either way. Without it a world that rolled "not yet
                    // built" would have nowhere to build it, and the wonder would be a
                    // building nobody could ever construct.
                    slot = wonder.TierKey(1);

                    if (built > 0) building = wonder.TierKey(built);
                }
                else if (silkRoad.MarketAt(barony) is { } market)
                {
                    // A Silk Road bazaar: vanilla's market building, standing from the start,
                    // since the visit-a-market decision requires one to be there. Never on a
                    // wonder's barony — a province has one slot — which SilkRoad.Build
                    // guarantees by moving the market a barony over.
                    slot = market;
                    building = market;
                }

                rows.Add(new ProvinceRow(barony.ProvinceId, cultureKey, faith, slot, building));
            }
        }

        Console.WriteLine("  holdings: " + string.Join(", ",
            counts.OrderByDescending(k => k.Value).Select(k => $"{k.Value} {k.Key}")));

        if (importedHoldings.Count > 0)
            Console.WriteLine($"  azgaar: {importedHoldings.Values.Sum()} of those are burgs the " +
                              "export drew — " + string.Join(", ", importedHoldings
                                  .OrderByDescending(k => k.Value).Select(k => $"{k.Value} {k.Key}")));

        return (rows, holdings);
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

        var loc = new LocFile();

        // Every entry carries the :0 version marker. Paradox treats a missing one as version 0 in
        // most files and as a parse error in some, and the launcher's own validator flags it, so it
        // is written rather than relied upon.
        //
        // The PROV<id> keys are deliberately absent. They are the *vanilla* province name keys, and
        // vanilla declares all 8,000-odd of them; re-declaring one per generated province put a
        // duplicate in the dictionary for every id the base game also uses, and CK3 resolves those
        // by load order rather than by mod. prov_<id> is the key the map actually reads.
        // The hegemony stands above the empires, so flattening from them alone would leave the one
        // title that names the whole world showing its raw key in game.
        var named = Titles.Flatten(empires).ToList();
        if (Titles.HegemonyOf(empires) is { } crown) named.Add(crown);

        // The hegemony's key is vanilla's (Titles.HegemonyKey), and vanilla already localises it —
        // "China", with an adjective "Chinese" that a hundred datafunctions read. A mod's copy of a
        // vanilla key in an ordinary loc file is logged as "Duplicate localization key" on every
        // launch; localization/replace/ is the sanctioned place for one, so the hegemony's two keys
        // go there, in a file of their own. Every other generated title falls back on its name for
        // lack of an adjective; this one has to say so, or it stays "Chinese".
        var crownLoc = new LocFile();

        foreach (var title in named)
        {
            string name = ParadoxText.Loc(title.Name);

            if (title.Tier == "h")
            {
                crownLoc.AddBuilt(title.Key, name);
                crownLoc.AddBuilt($"{title.Key}_adj", name);
                continue;
            }

            // A vanilla title's name and adjective are vanilla's own localisation, which this mod does
            // not replace; a copy here would log as a duplicate key on every launch.
            if (!title.Inherited) loc.AddBuilt(title.Key, name);

            if (title.Tier == "b" && title.ProvinceId > 0)
                loc.AddBuilt($"prov_{title.ProvinceId}", name);
        }

        // From baronyCount + 1, not from 1. Starting at 1 covered every barony as well, so each one
        // got a second, later entry reading "Wasteland" — the impassable provinces are the ones
        // above the last barony, and they are the only ones without a title to be named after.
        for (int id = baronyCount + 1; id <= landCount; id++)
            loc.AddBuilt($"prov_{id}", "Wasteland");

        for (int id = landCount + 1; id <= provinces.Count; id++)
        {
            string name = ParadoxText.Loc(
                waterNames.GetValueOrDefault(id, id <= riverCount ? $"River {id}" : $"Sea of {id}"));

            // One key or the other, never both: a province is a river or a sea, and writing both
            // left every river also declared as a sea of the same name.
            if (id <= riverCount) loc.AddBuilt($"river_{id}", name);
            else loc.AddBuilt($"sea_{id}", name);
        }

        loc.Write(Path.Combine(dir, "gen_titles_l_english.yml"));

        if (Titles.HegemonyOf(empires) is not null)
            crownLoc.Write(Path.Combine(modDir, "localization", "replace", "english", "gen_hegemony_l_english.yml"));
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