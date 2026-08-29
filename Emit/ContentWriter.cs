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

            Titles.AssignNames(empires, map, new Rng(cfg.Seed ^ 0x7171), borrowed,
                               Titles.HegemonyOf(empires));

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
                    baronyCount, azgaar, cultures));

        // After the realm pass, never during it — see Realms.CrownHegemon for why granting it any
        // earlier would have made one ruler the liege of the whole map.
        if (cfg.StartingHegemony) Realms.CrownHegemon(realms, empires, wilderness);

        var governments = Core.Stage.Time("governments", () => MapGen.Governments.Build(
            empires, counties, realms, provinceTerrain, development, cultures,
            worldCenters, cfg, new Rng(cfg.Seed ^ 0x6017), azgaar, stateGovernments));

        Console.WriteLine("  governments: " + string.Join(", ",
            governments.Tally(counties.Count).Select(g => $"{g.Count} {g.Government[..^11]}")));

        // After the governments, never before: this brings whole kingdoms under the hegemon, and
        // governments are decided one per realm grouped by top liege — done first, every absorbed
        // kingdom would have been swept into the hegemon's government. See ExpandHegemonRealm.
        if (cfg.StartingHegemony) Realms.ExpandHegemonRealm(realms, empires, wilderness);

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
                cultures.Heritages[0], vocabulary, cfg, new Rng(cfg.Seed ^ 0x0C55));

            cultures.Cultures.Add(unsettledCulture);
            foreach (var county in wilderness.Counties) cultures.ByCounty[county] = unsettledCulture;

            var (unsettledReligion, unsettledFaith) = MapGen.Faiths.CreateUnsettled(vocabulary,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase), cfg, new Rng(cfg.Seed ^ 0x0FA5));

            faiths.Religions.Add(unsettledReligion);
            faiths.Faiths.Add(unsettledFaith);
            foreach (var county in wilderness.Counties) faiths.ByCounty[county] = unsettledFaith;
        }

        // Who fights and who inherits, settled here because it is the first point at which both
        // halves of the question exist: the culture was drawn before any faith did, and the answer
        // has to be the same one its people's religion gives. See MapGen/Cultures.AlignGender.
        Core.Stage.Time("gender", () => MapGen.Cultures.AlignGender(cultures, faiths, vocabulary,
            new Rng(cfg.Seed ^ 0x6E1D)));

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

        // Assigned inside the stage below and kept for WrittenContent — the holdings come off one
        // Rng walked across the whole world, so this is the only chance to see them.
        Dictionary<int, string> holdings = [];

        Core.Stage.Time("titles, history and localisation", () =>
        {
            WriteLandedTitles(modDir, empires, faiths, wilderness);
            WriteProvinceTerrain(modDir, provinceTerrain, landCount);
            holdings = WriteProvinceHistory(modDir, cfg, empires, provinceTerrain, development, cultures, faiths, governments, wilderness, worldCenters, cfg.Seed);
            WriteLocalisation(modDir, empires, waterNames, provinces, order, baronyCount,
                landCount, riverCount);
        });

        Core.Stage.Time("wonders", () => WonderWriter.WriteAll(modDir, worldCenters));
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
        var retinues = cfg.EnableGeneratedRetinues
            ? Core.Stage.Time("retinues", () => MapGen.Retinues.Build(cultures, governments,
                provinceTerrain, vocabulary, cfg, new Rng(cfg.Seed ^ 0x3AA7)))
            : null;

        Core.Stage.Time("culture files",
            () => CultureWriter.WriteAll(modDir, cfg, cultures, ethnicities, vocabulary,
                new Rng(cfg.Seed ^ 0x0C1A), retinues?.Innovations));

        if (retinues is not null)
        {
            Core.Stage.Time("men-at-arms", () => RetinueWriter.WriteAll(modDir, retinues));
            Core.Stage.Time("generated innovations",
                () => InnovationWriter.WriteAll(modDir, retinues.Innovations));
        }

        Core.Stage.Time("compatibility", () =>
        {
            CompatibilityWriter.WriteDefines(modDir, gameDir, cfg);
            CompatibilityWriter.WriteCultureEras(modDir, gameDir, cfg);
            CompatibilityWriter.WriteCalendarLocalisation(modDir, azgaar);
            CompatibilityWriter.WriteGeographicalRegions(modDir, gameDir, empires, cultures);
            CompatibilityWriter.WriteHolySites(modDir, gameDir, empires, faiths);
            CompatibilityWriter.WriteDecisionBlocks(modDir, gameDir);
        });

        Core.Stage.Time("religion files", () => ReligionWriter.WriteAll(modDir, faiths));

        Core.Stage.Time("vanilla titulars",
            () => CompatibilityWriter.WriteVanillaTitulars(modDir, gameDir, empires));

        // Once, for both the locators and the city scatter: a slope field and a distance transform
        // over the whole province raster, which each of them used to run for itself off the same
        // two inputs. Hoisted here rather than memoised inside ProvinceAnchor so the sharing is
        // visible at the call site and the two writers cannot drift apart.
        var anchors = Core.Stage.Time("province anchors",
            () => MapGen.ProvinceAnchor.Compute(provinces, provinceElevation, cfg));

        Core.Stage.Time("locators", () => LocatorWriter.WriteAll(modDir, gameDir, provinces, order, landCount, anchors, cfg));
        Core.Stage.Time("casus belli", () => CasusBelliWriter.WriteAll(modDir, gameDir, cfg));
        Core.Stage.Time("frontend", () => FrontendWriter.WriteFrontend(modDir, gameDir));
        Core.Stage.Time("GUI changes", () => GuiWriter.WriteAll(modDir, gameDir, cfg.EnableSocieties));

        if (cfg.EnableFantasyEthnicities && cfg.RaceMode != MapConfig.FantasyRaceMode.HumanOnly)
        {
            Core.Stage.Time("character interactions",
                () => InteractionWriter.PatchMarriageInteractions(modDir, gameDir));
        }

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
            holdings, development, cultures, provinces, order, anchors, renderedElevation));
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
                prehistory = Core.Stage.Time("prehistory", () => PrehistoryMap.Build(
                    counties, provinces, order, landCount, realms, cultures, faiths,
                    governments, worldCenters, wilderness, cfg, new Rng(cfg.Seed ^ 0x4821)));

                // After prehistory, which it reads the houses and fathers from, and before
                // anything that names a ruler: the bookmarks and the character file both read
                // from this rather than each drawing the man again.
                rulers = Core.Stage.Time("rulers", () => RulerMap.Build(
                    counties, cfg, realms, cultures, faiths, governments, wilderness, prehistory));

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

                // Dresses weapons the *game* creates - inspirations, tournament prizes, adventurer
                // finds - from the same pool. Without it every player-earned weapon would be vanilla
                // art standing next to forged art in the same inventory. Keyed on culture, so it
                // needs this world's culture list rather than just the weapons.
                ForgedVisualOverrides.Write(modDir, forgedWeapons,
                    [.. cultures.Cultures.Select(c => c.Key)]);

                // Armour, which needs no geometry at all: a vanilla war garment already carries the
                // mask and variation hooks a forged weapon does, so a look is a palette and some
                // text. Culture picks the garment, the artifact's type picks the material.
                Core.Stage.Detail("  · armour forge", () => ArmorForgeStep.WriteAll(modDir, gameDir,
                    [.. cultures.Cultures.Select(c => c.Key)],
                    cultures.Cultures.ToDictionary(c => c.Key, c => c.ClothingGfx, StringComparer.Ordinal)));

                // Hand-modelled pieces from assets/armors, worn from a debug flag. After the forge
                // above, because that is what splices the gene template both of them rely on.
                CustomArmorStep.WriteAll(modDir, gameDir);

                // The bone-attachment experiment, behind its own flag and depending on nothing
                // above: it hangs a vanilla prop off a bone to establish whether pauldrons and
                // similar garnish are reachable at all.
                BoneAttachProbe.WriteAll(modDir, gameDir);

                // Rigid pieces hung off portrait bones - pauldrons today, any slot later. After the
                // armour forge because it garnishes what that emits, though it depends on none of it.
                BonePieceStep.WriteAll(modDir, gameDir, [.. cultures.Cultures.Select(c => c.Key)]);
                ArtifactWriter.WriteModifiers(modDir, artifacts);
                ArtifactWriter.WriteLocalisation(modDir, artifacts);
                ArtifactWriter.WriteOnGameStart(modDir, artifacts);
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
                    realms, development, cultures, faiths, governments, wilderness, prehistory,
                    rulers, azgaar));

                // Kept for the editor: re-emitting a ruler means re-emitting the bookmark that
                // describes him, and the cast is the record of who that is.
                bookmarks = bookmarkResult.Cast;

                Core.Stage.Detail("  · character history", () => HistoryWriter.WriteAll(
                    modDir, cfg, empires, realms, development,
                    cultures, ethnicities, faiths, governments, wilderness, prehistory, rulers));

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
                ChronicleWriter.WriteAll(modDir, chronicle, struggles, empires);

                Core.Stage.Detail("  · struggle art",
                    () => StruggleWriter.WriteAll(modDir, gameDir, cfg, struggles, flatmap, provinces, order));
                struggleCount = struggles.Struggles.Count;

                WarWriter.WriteAll(modDir, prehistory);
                Core.Stage.Detail("  · portraits", () => PortraitWriter.WriteAll(
                    modDir, gameDir, bookmarkResult.PortraitRequests, ethnicities, cfg.Seed));
            });
        }
        else Console.WriteLine("  history: SKIPPED (--no-history)");
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
        if (cfg.EnableWilderness) sets.Add(StaticFileWriter.Wilderness);
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
            WorldCenters = worldCenters,
            Realms = realms,
            Rulers = rulers,
            Prehistory = prehistory,
            Governments = governments,
            Bookmarks = bookmarks,
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
    internal static void WriteLandedTitles(string modDir, List<Title> empires, FaithMap faiths,
        WildernessMap wilderness)
    {
        string dir = Path.Combine(modDir, "common", "landed_titles");
        Directory.CreateDirectory(dir);

        // Four spaces, not tabs. That is how this file has always been written and it is the one
        // place in the mod that differs, which is why JominiStyle exists as a parameter at all.
        var jb = new JominiBuilder(JominiStyle.Spaced);

        jb.Comment("Generated de jure hierarchy.");
        jb.Blank();

        // When the world has a hegemony it *is* the root, and writing it writes every empire nested
        // inside — which is all CK3 needs to read a de jure tier above empire. The prefix is the
        // whole declaration: `tier` is documented as not for use in database definitions and appears
        // nowhere in vanilla's own data.
        var hegemony = Titles.HegemonyOf(empires);
        if (hegemony is not null) Write(hegemony);
        else foreach (var empire in empires) Write(empire);

        jb.Comment("Head of faith landless titles.");
        jb.Blank();

        foreach (var faith in faiths.Faiths)
        {
            if (faith.Head is null) continue;
            var (r, g, bl) = faith.Color;

            using (jb.Block(faith.Head.TitleKey))
            {
                jb.Inline("color", F(r), F(g), F(bl));
                jb.Field("capital", faith.Head.Seat.Key);
                jb.Field("landless", "yes");
            }

            jb.Blank();
        }

        var wildCapital = wilderness.Counties.FirstOrDefault();
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
                }
                else
                {
                    if (title.Tier == "c") jb.Field("definite_form", "no");

                    if (title.Tier == "h")
                    {
                        // Vanilla's own shape for a hegemony: it is "the <name>", and it is never
                        // renamed after whoever holds it. This title stands for the world rather
                        // than for a house.
                        jb.Field("definite_form", "yes");
                        jb.Field("can_be_named_after_dynasty", "no");

                        // Creation runs through the generated decision and nowhere else. Left open,
                        // the title-creation UI would sell the world for 2400 gold to anyone
                        // holding two empires — CREATE_TITLE_OR_TIER_HEGEMONY asks for no more than
                        // that. Closing it here costs the decision nothing, because
                        // create_title_and_vassal_change does not consult can_create.
                        jb.Inline("can_create", "always = no");
                        jb.Inline("can_create_on_partition", "always = no");
                    }

                    foreach (var child in title.Children) Write(child);
                }
            }
        }
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

        int faithHeads = faiths.Faiths
            .Where(f => f.Head is not null)
            .Select(f => f.Head!.TitleKey)
            .Distinct(StringComparer.Ordinal)
            .Count();

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
            //   * one kingdom-tier title, k_gen_wilderness, when there is any unsettled land.
            //
            // Distinct rather than a plain count, because two faiths may name the same head and the
            // database keeps one title either way.
            Empires = all.Count(t => t.Tier == "e"),
            Kingdoms = all.Count(t => t.Tier == "k") + (wilderness.Count > 0 ? 1 : 0),
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

    /// <returns>The holding written for every barony, by province id — see
    /// <see cref="WrittenContent.Holdings"/> for why it is kept rather than replayed.</returns>
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

        switch (governments.For(wonder.County))
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

    private static Dictionary<int, string> WriteProvinceHistory(string modDir, MapConfig cfg,
        List<Title> empires,
        TerrainClass[] provinceTerrain, Dictionary<Title, int> development, CultureMap cultures,
        FaithMap faiths, GovernmentMap governments, WildernessMap wilderness,
        WorldCenterMap worldCenters, int cfgSeed)
    {
        string dir = Path.Combine(modDir, "history", "provinces");
        Directory.CreateDirectory(dir);

        var rng = new Rng(cfgSeed ^ 0x8A12);
        var counts = new Dictionary<string, int>();
        var holdings = new Dictionary<int, string>();

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

        // Four spaces again, matching landed_titles above.
        var b = new JominiBuilder(JominiStyle.Spaced);

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
                holdings[barony.ProvinceId] = holding;

                using (b.Block(barony.ProvinceId))
                {
                    b.Field("culture", cultureKey);
                    b.Field("religion", faith);
                    b.Field("holding", holding);

                    if (wondersByBarony.TryGetValue(barony, out var wonder))
                    {
                        // How far up its own ladder this wonder already is on the start date.
                        int built = StartingWonderTier(
                            wonder, development, ordinaryTopDevelopment, governments, cfg,
                            new Rng(barony.ProvinceId ^ 0x5C0E));

                        // The slot is declared either way. Without it a world that rolled "not yet
                        // built" would have nowhere to build it, and the wonder would be a
                        // building nobody could ever construct.
                        b.Field("special_building_slot", wonder.TierKey(1));

                        if (built > 0) b.Field("special_building", wonder.TierKey(built));
                    }
                }
            }
        }

        Console.WriteLine("  holdings: " + string.Join(", ",
            counts.OrderByDescending(k => k.Value).Select(k => $"{k.Value} {k.Key}")));

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_provinces.txt"), b.ToString());
        return holdings;
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

        foreach (var title in named)
        {
            string name = ParadoxText.Loc(title.Name);
            loc.AddBuilt(title.Key, name);

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