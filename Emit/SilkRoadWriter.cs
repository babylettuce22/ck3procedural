using Ck3MapGen.Config;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// Binds All Under Heaven's Silk Road situation to the generated map.
///
/// The situation carries over untouched — phases, catalysts, the downstream war-and-peace
/// events, the window and its map mode are vanilla's — and what has to be written is where it
/// is and what feeds it:
///
/// <list type="bullet">
/// <item>the situation type, vanilla's file read from the installed game with only its
/// <c>sub_regions</c> block rewritten, so each stop's bazaar is this map's;</item>
/// <item>the start, on day one of year one behind the All Under Heaven trigger, as vanilla's
/// own (blanked) history file does on 552;</item>
/// <item>a game-start effect that gives the source bazaar's culture the innovations the road
/// carries and seeds each stop's first innovation — vanilla does the seeding only on its own
/// three start dates, and without it the visit-a-market decision stays hidden for the twelve
/// years the first innovation takes to move;</item>
/// <item>names: the stops, their regions, and the six market buildings, whose vanilla names
/// are Chang'an's and Lhasa's and would otherwise appear in the decision.</item>
/// </list>
///
/// The market buildings themselves land through province history, and the region lists through
/// <see cref="CompatibilityWriter.WriteGeographicalRegions"/>, both fed from the same map.
/// </summary>
public static class SilkRoadWriter
{
    private const string TypeKey = "silk_road_situation";

    /// <summary>
    /// The innovations the road carries, by the era they belong to. Everything vanilla marks
    /// <c>silk_road_innovation_parameter</c>; the source culture knows the tribal ones from the
    /// start and the early medieval ones once the world's era reaches them.
    /// </summary>
    private static readonly string[] TribalInnovations =
    [
        "champa_rice", "sericulture", "dragon_kiln", "block_printing", "cupellation",
        "lacquered_armor", "coking", "composite_crossbow", "pharmacopoeia", "waterworks",
    ];

    private static readonly string[] EarlyMedievalInnovations =
    [
        "fire_medicine", "compass", "grenades", "double_entry_bookkeeping", "bulkheads",
    ];

    /// <summary>
    /// What each stop starts with, in stop order: vanilla's own presets for its 867, 1066 and
    /// 1178 starts, chosen by the world's era year.
    /// </summary>
    private static string[] StartingInnovations(int eraYear) => eraYear switch
    {
        < 1000 => ["dragon_kiln", "block_printing", "waterworks", "block_printing", "waterworks", "cupellation"],
        < 1150 => ["compass", "fire_medicine", "champa_rice", "fire_medicine", "champa_rice", "pharmacopoeia"],
        _ => ["grenades", "pharmacopoeia", "coking", "pharmacopoeia", "coking", "bulkheads"],
    };

    public static void WriteAll(string modDir, string gameDir, MapConfig cfg, SilkRoadMap road)
    {
        if (road.IsEmpty)
        {
            Console.WriteLine("  silk road: none (no road system with six reachable markets)");
            return;
        }

        if (!WriteSituationType(modDir, gameDir, road))
        {
            Console.WriteLine("  silk road: WARNING vanilla situation file not found under " +
                              $"'{gameDir}'; the situation is not started");
            return;
        }

        WriteHistory(modDir);
        WriteOnAction(modDir, cfg, road);
        WriteLocalisation(modDir, road);

        int lanes = road.Chain.Count(step => step.Edge.Kind == RouteKind.Sea);
        Console.WriteLine($"  silk road: {road.Stops.Sum(s => s.Counties.Count)} counties in six stops, " +
                          $"{road.Chain.Count} road segments" +
                          (lanes > 0 ? $" ({lanes} by sea, which the map mode cannot draw)" : "") +
                          $"; the source is {road.SourceNote}");
        foreach (var s in road.Stops)
            Console.WriteLine($"    {s.Suffix}: {s.Name} — bazaar at {s.County.Name} ({s.CountyKey}), " +
                              $"{s.Counties.Count} counties, {s.RouteCounties.Count} on the road" +
                              (s.Heartland is not null ? $"; the heartland is all of {s.Heartland.Name}" : ""));
    }

    private static bool WriteSituationType(string modDir, string gameDir, SilkRoadMap road)
    {
        string source = Path.Combine(gameDir, "common", "situation", "situations", "tgp_silk_road.txt");
        if (!File.Exists(source)) return false;

        string text = File.ReadAllText(source).TrimStart((char)0xFEFF);
        if (SteppeWriter.SubRegionsBlock(text, out int start, out int end) is null) return false;

        var block = new JominiBuilder(startDepth: 1);
        using (block.Block("sub_regions"))
        {
            foreach (var s in road.Stops)
            {
                using (block.Block(s.SubRegionKey))
                {
                    block.Inline("geographical_regions", s.RegionKey);
                    block.Field("capital_province", s.MarketBarony.ProvinceId);
                    block.Color("map_color", s.Color.R, s.Color.G, s.Color.B);
                }
            }
        }

        var b = new JominiBuilder();
        b.Comment("""
                  Vanilla's silk_road_situation, read from the installed game, with one block
                  changed: sub_regions points each stop's bazaar at this map's market rather than
                  at Chang'an's. The keys are vanilla's because base-game script names them.
                  Everything else here is vanilla's, verbatim.
                  """);
        b.Blank();
        b.Raw(text[..start].TrimEnd(' ', '\t'));
        b.Raw(block.ToString().TrimEnd('\n'));
        b.Raw(text[end..]);

        string dir = Path.Combine(modDir, "common", "situation", "situations");
        Directory.CreateDirectory(dir);
        ParadoxText.WriteBom(Path.Combine(dir, "tgp_silk_road.txt"), b.ToString());
        return true;
    }

    /// <summary>
    /// Starts the situation. Vanilla's own file lives under history/struggles, oddly, and is
    /// blanked with the rest of that folder; this replaces it under a name of our own.
    /// </summary>
    private static void WriteHistory(string modDir)
    {
        var b = new JominiBuilder();
        b.Comment("Starts the Silk Road situation on this map; see MapGen/SilkRoad.cs.");
        b.Blank();

        using (b.Block("1.1.1"))
        using (b.Block("effect"))
        using (b.Block("if"))
        {
            using (b.Block("limit")) b.Field("has_tgp_dlc_trigger", "yes");
            using (b.Block("start_situation")) b.Field("type", TypeKey);
        }

        string dir = Path.Combine(modDir, "history", "struggles");
        Directory.CreateDirectory(dir);
        ParadoxText.WriteBom(Path.Combine(dir, "zz_gen_silk_road.txt"), b.ToString());
    }

    /// <summary>
    /// The game-start setup vanilla does for its own cultures and dates, done for this map's.
    /// After the lobby, like the artifacts, so the situation started from history exists.
    /// </summary>
    private static void WriteOnAction(string modDir, MapConfig cfg, SilkRoadMap road)
    {
        var b = new JominiBuilder();
        b.Comment("""
                  What the Silk Road needs at game start on a generated map.

                  Vanilla seeds each stop's first innovation only on its own three start dates, and
                  draws every later one from what the culture at Chang'an knows. Here the source
                  bazaar's culture is given the innovations the road carries, by era, and each stop
                  is seeded with vanilla's preset for the nearest vanilla start — otherwise nothing
                  moves for twelve years and the visit-a-market decision stays hidden.
                  """);
        b.Blank();

        using (b.Block("on_game_start_after_lobby"))
            b.Inline("on_actions", "gen_silk_road_setup");
        b.Blank();

        using (b.Block("gen_silk_road_setup"))
        using (b.Block("effect"))
        using (b.Block("if"))
        {
            using (b.Block("limit"))
            {
                b.Field("has_tgp_dlc_trigger", "yes");
                b.Field("exists", $"situation:{TypeKey}");
            }

            // Vanilla's han has heritage_chinese, which alone satisfies silk_road_innovation_trigger,
            // so it can research every silk innovation as its era allows. A generated culture has
            // no such pillar, and the only other way through that trigger is the culture's
            // silk_road_unlocked_innovations list — so every one goes on the list, which is what
            // lets them be researched, and the ones the world's era has reached are known outright.
            // The yearly china effect moves only what the source culture *knows*, so the road
            // grows richer as the source advances, as it does for vanilla's China.
            var source = road.Stops[0];
            bool earlyMedieval = Innovations.EraIndexAt(cfg.EraYear) >= Innovations.IndexOf("culture_era_early_medieval");
            var known = earlyMedieval
                ? TribalInnovations.Concat(EarlyMedievalInnovations)
                : TribalInnovations;

            b.Comment("The source bazaar's people may learn everything the road carries, and know what their era allows.");
            using (b.Block($"title:{source.CountyKey}.culture"))
            {
                foreach (string innovation in TribalInnovations.Concat(EarlyMedievalInnovations))
                    b.Inline("add_to_variable_list",
                        $"name = silk_road_unlocked_innovations target = culture_innovation:innovation_{innovation}");
                foreach (string innovation in known)
                    b.Field("add_innovation", $"innovation_{innovation}");
            }

            b.Comment("Each stop's first innovation, unless the situation already set one.");
            var starting = StartingInnovations(cfg.EraYear);
            using (b.Block($"situation:{TypeKey}"))
            {
                for (int s = 0; s < road.Stops.Count; s++)
                {
                    using (b.Block($"situation_sub_region:{road.Stops[s].SubRegionKey}"))
                    using (b.Block("if"))
                    {
                        using (b.Block("limit"))
                            b.Inline("NOT", "exists = var:innovation");
                        using (b.Block("set_variable"))
                        {
                            b.Field("name", "innovation");
                            b.Field("value", $"culture_innovation:innovation_{starting[s]}");
                        }
                    }
                }
            }
        }

        string dir = Path.Combine(modDir, "common", "on_action");
        Directory.CreateDirectory(dir);
        ParadoxText.WriteBom(Path.Combine(dir, "zz_gen_silk_road_on_actions.txt"), b.ToString());
    }

    /// <summary>
    /// Names. The stops title themselves through <c>silk_road_situation_sub_region_&lt;key&gt;</c>,
    /// the regions through their own keys, and the six market buildings through
    /// <c>building_&lt;key&gt;</c> — which the decision shows, so "Marketplace of Dūnhuáng" would
    /// otherwise name a generated town. Written under <c>replace</c> so the keys win outright.
    /// </summary>
    private static void WriteLocalisation(string modDir, SilkRoadMap road)
    {
        var loc = new LocFile();
        foreach (var s in road.Stops)
        {
            loc.Add($"{TypeKey}_sub_region_{s.SubRegionKey}", s.Name);
            loc.Add(s.RegionKey, s.Name);
            loc.Add(s.RouteRegionKey, $"{s.Name} Road");
        }
        loc.Blank();

        // The tooltips, which vanilla writes about the Tibetan Plateau and the Ganges; the
        // stream is vanilla's, so each stop can say where its goods come from and go to.
        var byIndex = road.Stops.ToList();
        (int Up, int Down)[] stream = [(-1, -1), (0, 2), (1, -1), (0, 4), (3, 5), (4, -1)];
        for (int i = 0; i < byIndex.Count; i++)
        {
            var s = byIndex[i];
            string bazaar = $"the Bazaar of {ParadoxText.Loc(s.County.Name)}";
            var (up, down) = stream[i];
            string body = up < 0
                ? s.Heartland is not null
                    ? $"From {bazaar}, at the heart of {ParadoxText.Loc(s.Heartland.Name)}, silk and new ideas begin their slow travel outward along the road."
                    : $"From {bazaar}, silk and new ideas begin their slow travel outward along the road."
                : down < 0
                    ? $"The far end of the road, beyond {ParadoxText.Loc(byIndex[up].Name)}. Whatever reaches {bazaar} has crossed the whole road to get there."
                    : $"The road between {ParadoxText.Loc(byIndex[up].Name)} and {ParadoxText.Loc(byIndex[down].Name)}. Whatever reaches {bazaar} came by way of the one and will move on to the other.";
            loc.AddBuilt($"{TypeKey}_sub_region_{s.SubRegionKey}_desc",
                $"$SITUATION_SUB_REGION_TOOLTIP_DESC$\\n\\n#weak {body}#!");
        }
        loc.Blank();

        foreach (var s in road.Stops)
        {
            string tier2 = s.Market[..^2] + "02";
            loc.Add($"building_{s.Market}", $"Bazaar of {s.County.Name}");
            loc.Add($"building_{tier2}", $"Great Bazaar of {s.County.Name}");
        }

        loc.Write(Path.Combine(modDir, "localization", "replace", "english", "zz_gen_silk_road_l_english.yml"));
    }
}
