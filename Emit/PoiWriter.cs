using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// The two vanilla points of interest that are lists of Earth province ids, fed from the map.
///
/// ---- What was wrong with vanilla's ----
///
/// poi_grand_city (Baghdad, Rome, Constantinople) and poi_battles_historical (48 dated battles,
/// Yarmuk to Hastings) name literal province ids, and the engine rebuilds both lists for every
/// travel plan. On a generated map each id is whatever county happens to sit there or nothing at
/// all, and every miss was an "add_to_list ... Scoped object is not valid" line in error.log —
/// 4,900 in five minutes of play. A static override emptied both; this writer is its follow-up.
///
/// ---- What this writes ----
///
/// Grand cities: the most developed settled county of each kingdom, best first, a handful of them
/// the way vanilla has three. One per kingdom so they spread over the map instead of piling up
/// round the richest capital.
///
/// Historical battlefields: the chronicle's wars from before the start — what an applied history
/// remembers of wars won and lost, and the live wars' opening years. Each is gated on
/// game_start_date the way vanilla's are, so an earlier bookmark does not show a battle that has
/// not been fought yet. The name and description are province custom loc in vanilla
/// (BattlePoiNameHistorical, BattlePoIDescriptionHistorical), which is also a list of Earth ids —
/// left alone it would call a generated county "The Battle of Hastings" — so both are replaced.
///
/// ---- Traps ----
///
/// The grand-city visit fires on_visited_grand_city, whose three events each check for their own
/// Earth province (location = province:4828, Baghdad). Those three ids are never picked, or a
/// generated city would get Baghdad's sight-seeing event, and the "Will trigger a Sight-Seeing
/// Event" tooltip is dropped because nothing will.
/// </summary>
public static class PoiWriter
{
    private const string PoiFile = "zz_gen_poi_overrides.txt";

    /// <summary>The provinces vanilla's grand-city events test with <c>location = province:N</c>.</summary>
    private static readonly HashSet<int> CityEventProvinces = [4828, 2575, 496];

    private const int MinCities = 3, MaxCities = 8, MaxBattles = 24;

    /// <summary>
    /// Writes both types and the battlefield names. <paramref name="chronicle"/> is null with
    /// --no-history, which leaves no battlefields and still writes the cities.
    /// </summary>
    public static (int Cities, int Battles) WriteAll(string modDir, List<Title> empires,
        Dictionary<Title, int> development, WildernessMap wilderness, ChronicleMap? chronicle, int startYear)
    {
        var cities = GrandCities(empires, development, wilderness);
        var battles = Battlefields(chronicle, wilderness, startYear);

        WritePoiTypes(modDir, cities, battles);
        WriteBattleLoc(modDir, battles);

        Console.WriteLine($"  points of interest: {cities.Count} grand cities, {battles.Count} historical battlefields");
        return (cities.Count, battles.Count);
    }

    private sealed record Battlefield(Title County, int Year, string Text);

    private static List<Title> GrandCities(List<Title> empires, Dictionary<Title, int> development,
        WildernessMap wilderness)
    {
        var best = development
            .Where(kv => kv.Key.Tier == "c" && !wilderness.Contains(kv.Key)
                         && kv.Key.Capital is { ProvinceId: > 0 } seat && !CityEventProvinces.Contains(seat.ProvinceId))
            .GroupBy(kv => kv.Key.Parent?.Parent ?? kv.Key)
            .Select(g => g.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key.Index).First())
            .OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key.Index)
            .ToList();

        int kingdoms = Titles.Flatten(Titles.Roots(empires)).Count(t => t.Tier == "k");
        int count = Math.Clamp(kingdoms / 4, MinCities, MaxCities);
        return best.Take(count).Select(kv => kv.Key).ToList();
    }

    private static List<Battlefield> Battlefields(ChronicleMap? chronicle, WildernessMap wilderness, int startYear)
    {
        if (chronicle is null) return [];

        // Tension 2 and up: a war won, or held against a foreign people. A same-people border
        // squabble that was held is not the stuff battlefields are remembered for.
        return chronicle.All
            .Where(e => e.Kind == ChronicleKind.War && e.Year < startYear && e.Tension >= 2)
            .Select(e => (Event: e, County: CountyOf(e.Subject)))
            .Where(x => x.County is { } c && !wilderness.Contains(c) && c.Capital is { ProvinceId: > 0 })
            .GroupBy(x => x.County!)
            .Select(g => g.OrderByDescending(x => x.Event.Tension).ThenByDescending(x => x.Event.Year).First())
            .OrderByDescending(x => x.Event.Tension).ThenByDescending(x => x.Event.Year).ThenBy(x => x.County!.Index)
            .Take(MaxBattles)
            .OrderBy(x => x.Event.Year).ThenBy(x => x.County!.Index)
            .Select(x => new Battlefield(x.County!, Math.Max(1, x.Event.Year), x.Event.Text))
            .ToList();
    }

    /// <summary>A duchy's war is fought at its capital county.</summary>
    private static Title? CountyOf(Title subject)
    {
        var t = subject;
        while (t is not null && t.Tier != "c") t = t.Capital;
        return t;
    }

    private static int ProvinceOf(Title county) => county.Capital!.ProvinceId;

    private static void WritePoiTypes(string modDir, List<Title> cities, List<Battlefield> battles)
    {
        var b = new JominiBuilder();
        b.Comment("Generated by PoiWriter. Whole-object overrides of two vanilla points of interest");
        b.Comment("whose province lists are Earth ids; the on_visit blocks are vanilla's.");
        b.Blank();

        using (b.Block("poi_grand_city"))
        {
            using (b.Block("build_province_list"))
                foreach (var county in cities)
                {
                    b.Comment(county.Name);
                    using (b.Block($"province:{ProvinceOf(county)}")) b.Field("add_to_list", "provinces");
                }
            b.Raw(GrandCityOnVisit);
        }
        b.Blank();

        using (b.Block("poi_battles_historical"))
        {
            using (b.Block("build_province_list"))
                foreach (var battle in battles)
                {
                    b.Comment($"{battle.County.Name}, {battle.Year}");
                    using (b.Block("if"))
                    {
                        using (b.Block("limit")) b.Token($"game_start_date > {battle.Year}.12.31");
                        using (b.Block($"province:{ProvinceOf(battle.County)}")) b.Field("add_to_list", "provinces");
                    }
                }
            b.Raw(BattlesOnVisit);
        }

        string dir = Path.Combine(modDir, "common", "travel", "point_of_interest_types");
        Directory.CreateDirectory(dir);
        ParadoxText.WriteBom(Path.Combine(dir, PoiFile), b.ToString());
    }

    private static void WriteBattleLoc(string modDir, List<Battlefield> battles)
    {
        var b = new JominiBuilder();
        b.Comment("Generated by PoiWriter. Vanilla's two are Earth province ids; these name the");
        b.Comment("generated battlefields in poi_battles_historical instead.");
        b.Blank();

        foreach (var (key, suffix) in new[] { ("BattlePoiNameHistorical", "name"), ("BattlePoIDescriptionHistorical", "desc") })
        {
            using (b.Block(key))
            {
                b.Field("type", "province");
                foreach (var battle in battles)
                    using (b.Block("text"))
                    {
                        using (b.Block("trigger")) b.Field("this", $"province:{ProvinceOf(battle.County)}");
                        b.Field("localization_key", $"gen_poi_battle_{battle.County.Key}_{suffix}");
                    }
            }
            b.Blank();
        }

        string dir = Path.Combine(modDir, "common", "customizable_localization");
        Directory.CreateDirectory(dir);
        ParadoxText.WriteBom(Path.Combine(dir, "zz_gen_battle_poi_custom_loc.txt"), b.ToString());

        // $c_key$ rather than the name, so a county renamed in the editor renames its battle.
        var loc = new LocFile();
        foreach (var battle in battles)
        {
            loc.AddBuilt($"gen_poi_battle_{battle.County.Key}_name", $"The Battle of ${battle.County.Key}$");
            loc.AddBuilt($"gen_poi_battle_{battle.County.Key}_desc", $"#weak {battle.Text}#!");
        }
        loc.Write(Path.Combine(modDir, "localization", "english", "gen_battle_poi_l_english.yml"));
    }

    // Vanilla's on_visit, less the sight-seeing tooltip: see the class comment.
    private const string GrandCityOnVisit = """
	on_visit = {
		trigger_event = {
			on_action = on_visited_grand_city
		}
		if = {
			limit = {
				NOT = {
					has_trait = lifestyle_traveler
				}
			}
			add_trait = lifestyle_traveler
			traveler_travel_xp_effect = {
				MIN = 1
				MAX = 3
			}
			if = {
				limit = {
					is_landless_adventurer = yes
					has_perk = organized_muster_rolls_perk
				}
				send_interface_toast = {
					title = poi_grand_city.visit
					left_icon = root
					add_gold = minor_gold_laamps_value
				}
			}
		}
		else = {
			send_interface_toast = {
				title = poi_grand_city.visit
				left_icon = root
				traveler_travel_xp_effect = {
					MIN = 3
					MAX = 5
				}
				if = {
					limit = {
						has_government = landless_adventurer_government
						has_perk = organized_muster_rolls_perk
					}
					add_gold = minor_gold_laamps_value
				}
			}
		}
		wanderer_lifestyle_destination_effect = yes
		visiting_poi_effect = yes
	}

""";

    private const string BattlesOnVisit = """
	on_visit = {
		send_interface_toast = {
			title = travel_point_historical_name_visit_message
			left_icon = root
			if = {
				limit = {
					OR = {
						has_variable = battle_poi_trait_gained
						number_of_commander_traits > 1
					}
				}
				add_poi_martial_experience_effect = yes
			}
			else = {
				poi_lifestyle_experience_effect = {
					LIFESTYLE = martial
					VALUE = travel_minor_lifestyle_xp
				}
			}
			traveler_danger_xp_effect = {
				MIN = 1
				MAX = 3
			}
			wanderer_lifestyle_destination_effect = yes
		}
		visiting_poi_effect = yes
	}

""";
}
