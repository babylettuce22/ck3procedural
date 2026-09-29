using System.Globalization;
using Ck3MapGen.Config;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// The Wilds situation: the generated map's frontier as one world-scoped object with a window,
/// a map mode, participants, and an era per frontier that the colonisation flow moves along.
///
/// The Wilderness file set has no global state — every wild county, colony and obstacle acts
/// alone. This is the one thing in vanilla that is global, always reachable from the HUD's
/// Situations tab, and free: phases, catalysts, participant lists and the window all come from
/// the engine once the type is declared. What has to be written is where it is (the sub-regions,
/// one per frontier, bound to generated geographical regions), what moves it (the catalysts the
/// Wilderness set fires from its own effects), and what it counts (script variables on the
/// situation, recounted yearly and after every event that could change them).
///
/// <para>Two files are written even when there is no frontier at all. The Wilderness set's
/// effects call <c>wilds_situation_*_effect</c> and its cost value asks
/// <c>wilds_cheaper_expedition_trigger</c>, and a scripted effect that does not exist is a load
/// error on every map. So the effects and triggers files always ship; without a frontier they
/// are empty shells, and the type, catalysts and history that would give them meaning are not
/// written. The set itself never names the situation type — this file owns that name.</para>
///
/// <para>Phases run per sub-region, as vanilla's do: a taiga frontier can be in its Age of
/// Pioneers while a jungle one is still untamed. When every sub-region has reached
/// <c>wilds_settled</c> the situation fires its closing event to every human participant and
/// ends. A county that goes wild after that is not tracked — the arc is over.</para>
/// </summary>
public static class FrontierWriter
{
    public const string TypeKey = "the_wilds";

    private const string StartPhase = "wilds_untamed";

    private static readonly (string Key, string Name, string Desc)[] Catalysts =
    [
        ("catalyst_wilds_colony_founded", "Colony Founded", "An expedition has taken root in the wild."),
        ("catalyst_wilds_colony_promoted", "Colony Grown", "A colony has grown into a holding in its own right."),
        ("catalyst_wilds_colony_abandoned", "Colony Abandoned", "A colony has failed and its ground returned to the wild."),
        ("catalyst_wilds_obstacle_cleared", "Obstacle Cleared", "Something that kept settlers out has been dealt with."),
        ("catalyst_wilds_frontier_stirs", "The Frontier Stirs", "Colonies stand in this frontier. Every year they stand, the age turns."),
        ("catalyst_wilds_frontier_quiet", "The Frontier Falls Quiet", "No colony stands in this frontier. Every year without one, the wild reclaims the age."),
        ("catalyst_wilds_land_reclaimed", "Land Reclaimed", "A third of this frontier has been won from the wild. Every year it stays won, the frontier draws closer to closing."),
        ("catalyst_wilds_wild_returns", "The Wild Returns", "This frontier has slipped back to being mostly wild. Every year it stays so, the age slips back with it."),

        // Fired on top of Colony Abandoned rather than instead of it, because ruination reaches the
        // Wilds through abandon_county_effect and that effect has no way to know it is being called
        // by a collapse. Teaching it would mean either a parameter on all six of its callers or the
        // Wilds emitter reading a Ruins-set variable, and both are worse than the arithmetic: a
        // ruined county is an abandonment AND something worse, so it is worth both catalysts.
        // Abandonment is 25 of the 100 a phase costs; a ruin is 35.
        ("catalyst_wilds_county_ruined", "County Ruined", "Settled ground has fallen out of civilisation altogether. The people are gone and the stones are still standing."),
    ];

    public static void WriteAll(string modDir, MapConfig cfg, FrontierMap frontier)
    {
        if (!cfg.EnableWilderness) return;

        WriteEffects(modDir, frontier);
        WriteTriggers(modDir, frontier, cfg.EnableRuins);

        if (frontier.IsEmpty)
        {
            Console.WriteLine("  the wilds: none (no unsettled county to make a frontier of)");
            return;
        }

        WriteSituationType(modDir, frontier);
        WriteCatalysts(modDir);
        WriteValues(modDir);
        WriteOnActions(modDir);
        WriteHistory(modDir);
        WriteRegions(modDir, frontier);
        WriteEvents(modDir);
        WriteLocalisation(modDir, frontier);

        Console.WriteLine($"  the wilds: {frontier.SubRegions.Count} " +
                          (frontier.SubRegions.Count == 1 ? "frontier" : "frontiers") +
                          $", {frontier.WildCount} wild counties of {frontier.LandCounties}");
        foreach (var s in frontier.SubRegions)
            Console.WriteLine($"    {s.Key}: {s.Name} — {s.Wild.Count} wild, {s.Ring.Count} on the edge");
    }

    // ------------------------------------------------------------------------------------------
    //  The type
    // ------------------------------------------------------------------------------------------

    private static void WriteSituationType(string modDir, FrontierMap frontier)
    {
        var b = new JominiBuilder();
        b.Comment("""
                  The Wilds: the generated frontier as a situation. One sub-region per connected
                  stretch of wilderness (plus the settled counties one hop out, so the lords on
                  the edge are participants), each running its own era. The catalysts are fired
                  from the Wilderness set's effects; the counters the description shows are
                  recounted in zz_gen_the_wilds_effects.txt. See Emit/FrontierWriter.cs.
                  """);
        b.Blank();

        using (b.Block(TypeKey))
        {
            b.Quoted("illustration", "gfx/interface/illustrations/event_scenes/forest_pine.dds");

            // `Situation.GetIcon` is read in ten places across five vanilla files — the Situations
            // tab list, the HUD twice, the map icon layer and three tooltips — and with nothing
            // here every one of them drew nothing. A situation with no `icon` block falls back to
            // a file at gfx/interface/icons/situation_types/<key>.dds, which every vanilla type
            // ships and which this mod would otherwise have to invent art for.
            //
            // The block form takes the icon from art the Wilderness set already ships instead.
            // ONE entry, always valid: the Dynastic Cycle varies its icon by phase, but it has one
            // sub-region, so "the situation's phase" is a real thing there. Ours has up to six
            // frontiers in different eras at once, and a per-phase icon would have to answer which
            // one speaks for the whole world — and would need a fallback entry whose precedence
            // against the triggered ones is not documented anywhere.
            //
            // The stake-a-claim icon rather than the wilderness-holding one, which the sub-regions
            // already use: the situation is about going in, the sub-regions are the ground.
            using (b.Block("icon"))
            {
                using (b.Block("trigger")) b.Field("always", "yes");
                b.Quoted("reference", "gfx/interface/icons/activities/activity_stake_wilderness.dds");
            }

            b.Field("situation_group_type", "major");
            b.Field("map_mode", "sub_regions");
            b.Field("is_unique", "yes");

            // Default is `yes`, meaning "the phase icons are flat art" — which for vanilla's
            // Steppe means the 1848x399 season BANNERS it hands the field, drawn by its own
            // custom window. Ours are square full-colour icons drawn by the generic
            // window_situation.gui at 40x40 and 30x30, which is exactly what the Silk Road does,
            // and it is the one vanilla type that sets this to `no`. The .info calls a mismatch
            // "weird appearance in tooltips" and says nothing more.
            b.Field("use_situation_phase_flat_icons", "no");
            b.Blank();

            using (b.Block("sub_regions"))
            {
                foreach (var s in frontier.SubRegions)
                {
                    using (b.Block(s.Key))
                    {
                        // NOT a holding_types_tab icon. Every file under that directory is a pure
                        // black alpha mask — the holding tab tints it in GUI — and the situation
                        // window draws `[SituationSubRegion.GetIcon]` untinted at 40x40, so the
                        // wilderness_holding.dds that used to be here rendered as a black
                        // silhouette. Every icon this file names is checked full-colour art.
                        b.Quoted("icon", "gfx/interface/icons/casus_bellis/county_expansion.dds");
                        b.Color("map_color", s.Color.R, s.Color.G, s.Color.B);
                        b.Inline("geographical_regions", s.RegionKey);
                    }
                }
            }
            b.Blank();

            b.Raw(TypeBody);
        }

        string dir = Path.Combine(modDir, "common", "situation", "situations");
        Directory.CreateDirectory(dir);
        ParadoxText.WriteBom(Path.Combine(dir, $"gen_{TypeKey}.txt"), b.ToString());
    }

    /// <summary>
    /// Everything after the sub-regions, static. Written as text rather than through the builder
    /// because a phase is forty lines of nested blocks that are read as a unit, and the builder's
    /// nesting would bury the shape the file is supposed to show.
    ///
    /// <c>modifier_sets</c>, not <c>modifier_named_sets</c>: the shipped <c>_situations.info</c>
    /// documents the latter and every vanilla type uses the former.
    /// </summary>
    private const string TypeBody = """
        	on_start = {
        		wilds_situation_recount_effect = yes
        	}

        	on_yearly = {
        		wilds_situation_yearly_effect = yes
        	}

        	# ---- A note on every icon below, and on the two in WriteSituationType ----
        	#
        	# Full-colour art only, and none of it from gfx/interface/icons/holding_types_tab/.
        	# Every file in that directory — vanilla's and the two the Wilderness set adds — is a
        	# pure black alpha mask that the holding tab tints in GUI. The situation window draws
        	# `[SituationParticipantGroup.GetType.GetIcon]` and the phase icons untinted at 40x40
        	# and 30x30, so the four that pointed there rendered as black silhouettes.
        	#
        	# Nor the Wilderness set's own activity icons: activity_oversee_colony.dds and
        	# activity_stake_wilderness.dds are both copies of vanilla's activity_hunt.dds, so
        	# reaching for them here would have put the same bow in three slots of one window.
        	#
        	# The four phases read as an arc: forest, a surveyor's map, a walled town, ploughed
        	# fields. The two ends are the combat_effects terrain vignettes, which are the only
        	# assets in the game that say "wild ground" and "farmed ground" without a caption.
        	#
        	# Read order is first valid. A colonist's realm is in the region because the colony was
        	# a wild county; a marcher lord's own domain has to touch it, which until somebody
        	# founds a colony means holding a county on the ring. Neither dummy holder joins.
        	participant_groups = {
        		colonists = {
        			icon = "gfx/interface/icons/activities/activity_roaming.dds"
        			auto_add_landless_rulers = no
        			require_realm_in_sub_region = yes
        			map_color = { 214 168 64 }
        			is_character_valid = {
        				government_has_flag = government_is_colony
        			}
        		}
        		marcher_lords = {
        			icon = "gfx/interface/icons/court_position_task_types/grand_guardian_court_position_task_shield_the_realm.dds"
        			auto_add_landless_rulers = no
        			require_realm_in_sub_region = no
        			require_domain_in_sub_region = yes
        			map_color = { 96 120 176 }
        			is_character_valid = {
        				NOT = { government_has_flag = government_is_wilderness }
        				highest_held_title_tier >= tier_county
        			}

        			# The introduction. A marcher lord is enrolled the moment their own domain
        			# touches a frontier, which for most is the day the game starts, and until
        			# now that happened in silence — they held frontier modifiers and a seat in
        			# a situation nobody had mentioned, and two centuries later an event
        			# congratulated them on closing it.
        			#
        			# Guarded twice on purpose. The event carries its own trigger, as vanilla's
        			# steppe intro does, because that is what survives a player joining a
        			# multiplayer game late. The `if` here is the cheap half: this hook fires for
        			# every AI ruler on every frontier at game start, and an event that fails its
        			# own trigger has still been fired.
        			#
        			# The sub-region is saved rather than relied on. `scope:situation_sub_region`
        			# is handed to this hook by the engine, but a named scope is what is certain
        			# to survive into the event, and the event needs it to count the wild ground
        			# in THIS frontier rather than in the world.
        			on_join = {
        				if = {
        					limit = {
        						is_ai = no
        						NOT = { has_character_flag = wilds_intro_seen }
        					}
        					scope:situation_sub_region = { save_scope_as = wilds_region }
        					trigger_event = the_wilds.0001
        				}
        			}
        		}
        	}

        	start_phase = wilds_untamed

        	# Every phase lists wilds_settled with no takeover: the yearly effect moves a frontier
        	# there directly the year its last wild county is claimed, whatever era it was in.
        	phases = {
        		wilds_untamed = {
        			icon = "gfx/interface/icons/combat_effects/defender_forest.dds"
        			illustration = "gfx/interface/illustrations/event_scenes/forest_pine.dds"

        			# ---- The one thing that draws the frontier on the terrain itself ----
        			#
        			# `map_province_effect` is not a map mode and does not wait for one: it paints
        			# the ground on the ORDINARY map, all the time, for every province of the
        			# sub-region. `map_mode = sub_regions` colours the frontiers only while the
        			# situation's map mode is up; this is what makes them visible the rest of the
        			# time, and what makes an era turning something the player can SEE happen.
        			#
        			# `summer` of the four the engine has, and it is not a guess. The shader
        			# (gfx/FX/province_effects.fxh, ApplySummerDiffuseTerrain) blends grass patches
        			# over the terrain through SummerGrassOverlayColor and tints trees through
        			# SummerOverlayTree — vegetation taking the ground, which is precisely what an
        			# unclaimed county is. drought and flood are damage, snow is weather. The four
        			# indices are fixed in the shader, so a fifth would mean overriding a vanilla
        			# .fxh and re-doing it every patch.
        			#
        			# The intensity is the era, and it only ever falls: the wild is thickest where
        			# nobody has gone, thins as colonies stand, and is gone from a settled frontier
        			# (which therefore names no effect at all rather than naming one at 0). The
        			# effect covers the ring counties too, since they are part of the sub-region —
        			# slightly generous, and it reads correctly as the frontier being a zone rather
        			# than a line.
        			map_province_effect = summer
        			map_province_effect_intensity = 0.75

        			on_start = {
        				wilds_phase_announce_effect = { PHASE = wilds_untamed }
        			}

        			future_phases = {
        				wilds_pioneers = {
        					takeover_type = points
        					takeover_points = 100
        					catalysts = {
        						catalyst_wilds_colony_founded = wilds_catalyst_major
        						catalyst_wilds_obstacle_cleared = wilds_catalyst_minor
        						catalyst_wilds_frontier_stirs = wilds_catalyst_medium
        					}
        				}
        				wilds_settled = {
        				}
        			}

        			modifier_sets = {
        				wilds_glory_set = {
        					icon = "gfx/interface/icons/situations/situation_modifier_character.dds"
        					colonists = {
        						character_modifier = {
        							monthly_prestige_gain_mult = 0.15
        						}
        					}
        					marcher_lords = {
        						character_modifier = {
        							monthly_prestige_gain_mult = 0.05
        						}
        					}
        				}
        			}
        		}

        		wilds_pioneers = {
        			icon = "gfx/interface/icons/activities/activity_survey.dds"
        			illustration = "gfx/interface/illustrations/event_scenes/genericcamp.dds"

        			# Colonies stand in it now; the wild is thinner. See wilds_untamed.
        			map_province_effect = summer
        			map_province_effect_intensity = 0.45

        			on_start = {
        				wilds_phase_announce_effect = { PHASE = wilds_pioneers }
        			}

        			future_phases = {
        				wilds_closing = {
        					takeover_type = points
        					takeover_points = 100
        					catalysts = {
        						catalyst_wilds_colony_promoted = wilds_catalyst_major
        						catalyst_wilds_colony_founded = wilds_catalyst_minor
        						catalyst_wilds_land_reclaimed = wilds_catalyst_medium
        					}
        				}
        				wilds_untamed = {
        					takeover_type = points
        					takeover_points = 100
        					catalysts = {
        						catalyst_wilds_colony_abandoned = wilds_catalyst_major
        						catalyst_wilds_county_ruined = wilds_catalyst_medium
        						catalyst_wilds_frontier_quiet = wilds_catalyst_medium
        					}
        				}
        				wilds_settled = {
        				}
        			}

        			modifier_sets = {
        				wilds_glory_set = {
        					icon = "gfx/interface/icons/situations/situation_modifier_character.dds"
        					colonists = {
        						character_modifier = {
        							monthly_prestige_gain_mult = 0.1
        						}
        					}
        				}
        				wilds_settlement_set = {
        					icon = "gfx/interface/icons/situations/situation_modifier_subject.dds"
        					colonists = {
        						character_modifier = {
        							stewardship = 1
        						}
        						county_modifier = {
        							# -0.10, not the -0.25 this was.
        							#
        							# `build_speed` is additive across every source and nothing
        							# caps it — there is no build_speed define in vanilla's
        							# 00_defines.txt to cap. A colonist already reaches -0.40 from
        							# the Quartermaster's task at stewardship 20, -0.30 from
        							# colony_law_corvee, -0.20 from colony_rich_seam and -0.15 from
        							# settlement_overseen. That is -1.05 before this line; at -0.25
        							# this line took it to -1.30, which is construction time past
        							# zero.
        							#
        							# Vanilla's largest single scripted build_speed is -0.5, and the
        							# only vanilla situation that touches construction
        							# (tgp_dynastic_cycle) tops out at -0.2. -0.10 keeps the stack
        							# under -1.0 and makes this a nudge rather than the line that
        							# breaks it.
        							build_speed = -0.10
        							monthly_county_control_growth_add = 0.15
        						}
        					}
        				}
        				wilds_marches_set = {
        					icon = "gfx/interface/icons/situations/situation_modifier_military.dds"
        					marcher_lords = {
        						county_modifier = {
        							development_growth_factor = 0.1
        						}
        						parameters = {
        							wilds_cheaper_expeditions = yes
        						}
        					}
        				}
        			}
        		}

        		wilds_closing = {
        			icon = "gfx/interface/icons/council_task_types/task_develop_county.dds"
        			illustration = "gfx/interface/illustrations/event_scenes/ep2_village_festival_western.dds"

        			# Most of it is fields now. The last trace. See wilds_untamed.
        			map_province_effect = summer
        			map_province_effect_intensity = 0.2

        			on_start = {
        				wilds_phase_announce_effect = { PHASE = wilds_closing }
        			}

        			future_phases = {
        				wilds_pioneers = {
        					takeover_type = points
        					takeover_points = 100
        					catalysts = {
        						catalyst_wilds_colony_abandoned = wilds_catalyst_major
        						catalyst_wilds_county_ruined = wilds_catalyst_medium
        						catalyst_wilds_wild_returns = wilds_catalyst_medium
        					}
        				}
        				wilds_settled = {
        				}
        			}

        			modifier_sets = {
        				# ---- There is no `wilds_cheaper_expeditions` parameter here ----
        				#
        				# There was, and it pointed the wrong way. The discount was largest in the
        				# phase where the frontier is nearly full — a ruler was paid MOST to claim
        				# the last few counties and least to claim the first, which is backwards
        				# for an incentive whose whole job is to get the first expedition out.
        				#
        				# It also ran a loop. Cheaper expeditions bought more colonies, colonies
        				# fed catalyst_wilds_colony_founded, and the phase that granted the
        				# discount was the phase the catalysts advanced into. Once a frontier left
        				# wilds_untamed the price never rose again for the rest of the arc.
        				#
        				# wilds_marches_set still grants it, so the Age of Pioneers is now the one
        				# era that makes expeditions cheap: it turns on when the frontier is still
        				# mostly wild and turns off when it starts closing. See the note on
        				# @base_colonize_cost in 00_colonization_values.txt for what a cheap
        				# expedition does to a frontier if it is left cheap.
        				wilds_prosperity_set = {
        					icon = "gfx/interface/icons/situations/situation_modifier_fertility.dds"
        					colonists = {
        						county_modifier = {
        							monthly_county_control_growth_add = 0.1
        							development_growth_factor = 0.1
        						}
        					}
        					marcher_lords = {
        						county_modifier = {
        							# +0.05, not the +0.15 this was, and the reason is who collects it.
        							#
        							# A `marcher_lords` county_modifier lands on every county the
        							# lord holds INSIDE the sub-region, and the sub-region includes
        							# the ring — see the note on map_province_effect in
        							# wilds_untamed. So this is not paid for colonising anything.
        							# It is paid for holding ordinary, already-developed land that
        							# happens to border the wild, and at +0.15 it paid the man who
        							# never went out more than wilds_settlement_set pays the
        							# colonist who did.
        							#
        							# +0.05 is the figure wilds_peace_set gives a settled frontier,
        							# which is the right band for a bonus that is really about
        							# proximity rather than effort.
        							development_growth_factor = 0.05
        							supply_limit_mult = 0.1
        						}
        					}
        				}
        			}
        		}

        		wilds_settled = {
        			icon = "gfx/interface/icons/combat_effects/defender_farmland.dds"
        			illustration = "gfx/interface/illustrations/event_scenes/ep2_hunt_forest_managed.dds"

        			# No map_province_effect at all — see wilds_untamed. A settled frontier is
        			# ordinary country and should look like it; the ground going back to normal
        			# IS the reward, and it is the only phase transition the player sees from the
        			# map without opening anything.

        			on_start = {
        				wilds_phase_announce_effect = { PHASE = wilds_settled }
        			}

        			# A settled frontier reopens if a county on it is abandoned; the yearly effect
        			# moves it straight back to the Age of Pioneers the year that shows in the count.
        			future_phases = {
        				wilds_pioneers = {
        					takeover_type = points
        					takeover_points = 100
        					catalysts = {
        						catalyst_wilds_colony_abandoned = wilds_catalyst_major
        						catalyst_wilds_county_ruined = wilds_catalyst_medium
        					}
        				}
        			}

        			modifier_sets = {
        				wilds_peace_set = {
        					icon = "gfx/interface/icons/situations/situation_modifier_fertility.dds"
        					marcher_lords = {
        						county_modifier = {
        							development_growth_factor = 0.05
        						}
        					}
        				}
        			}
        		}
        	}

        """;

    // ------------------------------------------------------------------------------------------
    //  Catalysts, values, on_actions, history, regions
    // ------------------------------------------------------------------------------------------

    private static void WriteCatalysts(string modDir)
    {
        var b = new JominiBuilder();
        b.Comment("The Wilds situation's catalysts. A catalyst is only a name; the points it is worth are\n" +
                  "set per future phase in common/situation/situations/gen_the_wilds.txt.");
        b.Blank();
        foreach (var (key, _, _) in Catalysts) b.Inline(key);

        string dir = Path.Combine(modDir, "common", "situation", "catalysts");
        Directory.CreateDirectory(dir);
        ParadoxText.WriteBom(Path.Combine(dir, $"zz_gen_{TypeKey}_catalysts.txt"), b.ToString());
    }

    private static void WriteValues(string modDir)
    {
        var b = new JominiBuilder();
        b.Comment("""
                  How far each catalyst moves a frontier toward its next era. A phase turns over at
                  100 points, so at these weights it takes four colonies founded, or one and a
                  decade of standing, to open the Age of Pioneers.

                  The threshold itself is NOT here. A catalyst's value in a future_phases block
                  reads a script value happily — that is how vanilla's steppe writes its own — but
                  `takeover_points` does not: given a script value it resolves to -1 and the window
                  reads "Takeover points: 0 / -1", with nothing logged and nothing reported by
                  ck3-tiger. Every vanilla takeover_points is a literal, and so is ours.
                  """);
        b.Blank();
        b.Field("wilds_catalyst_minor", 5);
        b.Field("wilds_catalyst_medium", 10);
        b.Field("wilds_catalyst_major", 25);

        string dir = Path.Combine(modDir, "common", "script_values");
        Directory.CreateDirectory(dir);
        ParadoxText.WriteBom(Path.Combine(dir, $"zz_gen_{TypeKey}_values.txt"), b.ToString());
    }

    /// <summary>
    /// The situation starts in year one, before any holder exists, so its own on_start counts
    /// nothing. This recount runs once the bookmark's world is in place.
    /// </summary>
    private static void WriteOnActions(string modDir)
    {
        var b = new JominiBuilder();
        b.Comment("Recounts the Wilds situation once the lobby's world exists. Its on_start ran in year one.");
        b.Blank();
        using (b.Block("on_game_start_after_lobby"))
        using (b.Block("on_actions"))
            b.Token("wilds_situation_on_game_start");
        b.Blank();
        using (b.Block("wilds_situation_on_game_start"))
        using (b.Block("effect"))
        using (b.Block("if"))
        {
            using (b.Block("limit")) b.Field("exists", $"situation:{TypeKey}");
            using (b.Block($"situation:{TypeKey}")) b.Field("wilds_situation_recount_effect", "yes");
        }

        string dir = Path.Combine(modDir, "common", "on_action");
        Directory.CreateDirectory(dir);
        ParadoxText.WriteBom(Path.Combine(dir, $"zz_gen_{TypeKey}_on_actions.txt"), b.ToString());
    }

    /// <summary>
    /// Year one, where vanilla starts its own — see <see cref="SteppeWriter"/> for why. No DLC
    /// gate: the situation system is base game since 1.16.
    /// </summary>
    private static void WriteHistory(string modDir)
    {
        var b = new JominiBuilder();
        b.Comment("Starts the Wilds situation. No DLC is needed; situations are base game.");
        b.Blank();
        using (b.Block("1.1.1"))
        using (b.Block("effect"))
        using (b.Block("start_situation"))
        {
            b.Field("type", TypeKey);
            b.Field("start_phase", StartPhase);
        }

        string dir = Path.Combine(modDir, "history", "situations");
        Directory.CreateDirectory(dir);
        ParadoxText.WriteBom(Path.Combine(dir, $"zz_gen_{TypeKey}.txt"), b.ToString());
    }

    private static void WriteRegions(string modDir, FrontierMap frontier)
    {
        var b = new JominiBuilder();
        b.Comment("""
                  The frontiers behind the Wilds situation's sub-regions: each one's wild counties
                  and the settled counties one hop out. Keys are ours and appear nowhere in vanilla.
                  """);
        b.Blank();

        foreach (var s in frontier.SubRegions)
        {
            using (b.Block(s.RegionKey))
            using (b.Block("counties"))
            {
                var keys = s.Counties.Select(c => c.Key).ToList();
                for (int i = 0; i < keys.Count; i += 10)
                    b.Token(string.Join(' ', keys.Skip(i).Take(10)));
            }
            b.Blank();
        }

        string dir = Path.Combine(modDir, "map_data", "geographical_regions");
        Directory.CreateDirectory(dir);
        ParadoxText.WriteBom(Path.Combine(dir, "zz_gen_wilds_regions.txt"), b.ToString());
    }

    // ------------------------------------------------------------------------------------------
    //  Effects and triggers — always written, so the Wilderness set's calls resolve
    // ------------------------------------------------------------------------------------------

    private static void WriteEffects(string modDir, FrontierMap frontier)
    {
        var b = new JominiBuilder();

        if (frontier.IsEmpty)
        {
            b.Comment("""
                      The Wilds situation's effects, in their empty form: this map has no frontier,
                      so no situation is declared and the Wilderness set's calls land here and do
                      nothing. See Emit/FrontierWriter.cs.
                      """);
            b.Blank();
            // Empty bodies. Vanilla ships six scripted effects like this, so the shape is legal;
            // an effect that is never DEFINED is what breaks a load.
            foreach (string name in new[] { "colony_founded", "colony_promoted", "colony_abandoned", "obstacle_cleared", "county_ruined" })
            {
                using (b.Block($"wilds_situation_{name}_effect")) { }
                b.Blank();
            }
        }
        else
        {
            b.Comment("""
                      The Wilds situation's effects. The four wilds_situation_*_effect entries are the
                      Wilderness set's whole interface to the situation: each takes COUNTY, finds the
                      sub-region that county is in, fires the catalyst there and recounts. The recount
                      and the yearly effect are the situation's own. See Emit/FrontierWriter.cs.
                      """);
            b.Blank();
            b.Raw(HookEffects);
            b.Blank();
            WriteRecount(b, frontier);
            b.Blank();
            WriteYearly(b, frontier);
            b.Blank();
            b.Raw(AnnounceEffect);
        }

        string dir = Path.Combine(modDir, "common", "scripted_effects");
        Directory.CreateDirectory(dir);
        ParadoxText.WriteBom(Path.Combine(dir, $"zz_gen_{TypeKey}_effects.txt"), b.ToString());
    }

    private const string HookEffects = """
        # County scope in, through COUNTY. Each of the four fires one catalyst into the frontier
        # that county sits in. Three of them also recount, and the fourth deliberately does not.
        #
        # ---- Why the obstacle hook does not recount ----
        #
        # Nothing the recount measures changes when an obstacle comes off: the county is neither
        # more nor less wild, no colony was gained or lost, nothing was won from the wild. So the
        # recount there would be a pass over every county in the world to write back the numbers it
        # already held — and it is by far the hottest of the four. The Warden's task_clear_the_wilds
        # carries `restart_on_finish`, so a single colonist with a council clears obstacles on a
        # loop for the whole game, and each clearance would have walked ~1,000 counties twice on a
        # normal map and four times that on a large one, inside the council tick.
        #
        # The other three each change a number the window shows, are player-visible, and happen a
        # handful of times a year at most. They recount so the description is right the moment the
        # player looks at it. See the yearly effect for the other recount.
        wilds_situation_colony_founded_effect = {
        	wilds_situation_catalyst_effect = { CATALYST = catalyst_wilds_colony_founded COUNTY = $COUNTY$ }
        	wilds_situation_recount_if_exists_effect = yes
        }

        # The promotion also keeps the running tally of counties won from the wild — the one counter
        # that cannot be recounted from the map, because a promoted county is indistinguishable from
        # a county that was never wild. Incremented BEFORE the recount, which initialises the
        # variable only when it is missing and so cannot clobber it.
        wilds_situation_colony_promoted_effect = {
        	if = {
        		limit = { exists = situation:the_wilds }
        		situation:the_wilds = {
        			change_variable = { name = wilds_reclaimed add = 1 }
        		}
        	}
        	wilds_situation_catalyst_effect = { CATALYST = catalyst_wilds_colony_promoted COUNTY = $COUNTY$ }
        	wilds_situation_recount_if_exists_effect = yes
        }

        wilds_situation_colony_abandoned_effect = {
        	wilds_situation_catalyst_effect = { CATALYST = catalyst_wilds_colony_abandoned COUNTY = $COUNTY$ }
        	wilds_situation_recount_if_exists_effect = yes
        }

        # A county that has fallen out of civilisation, called at the END of gen_ruin_county_effect —
        # after the county has reached the ruins dummy, not before. Ruination passes through
        # abandon_county_effect on its way here, so the abandoned catalyst above has already fired on
        # this county; this one is the difference between a colony that failed and settled ground
        # lost. See the catalyst table in Emit/FrontierWriter.cs for why both.
        #
        # The recount is the reason the call site is the end rather than beside the abandonment. A
        # ruined county counts as wild (see wilds_is_wild_county_trigger), and it only reaches the
        # holder that makes it so on the last line of the collapse.
        wilds_situation_county_ruined_effect = {
        	wilds_situation_catalyst_effect = { CATALYST = catalyst_wilds_county_ruined COUNTY = $COUNTY$ }
        	wilds_situation_recount_if_exists_effect = yes
        }

        wilds_situation_obstacle_cleared_effect = {
        	wilds_situation_catalyst_effect = { CATALYST = catalyst_wilds_obstacle_cleared COUNTY = $COUNTY$ }
        }

        # Guarded on the situation existing: once the frontier has closed and the situation has
        # ended, a county going wild again is nobody's catalyst.
        wilds_situation_catalyst_effect = {
        	if = {
        		limit = { exists = situation:the_wilds }
        		$COUNTY$ = { save_scope_as = wilds_catalyst_county }
        		situation:the_wilds = {
        			every_situation_sub_region = {
        				limit = { situation_sub_region_has_county = scope:wilds_catalyst_county }
        				trigger_sub_region_catalyst = { catalyst = $CATALYST$ }
        			}
        		}
        	}
        }

        wilds_situation_recount_if_exists_effect = {
        	if = {
        		limit = { exists = situation:the_wilds }
        		situation:the_wilds = { wilds_situation_recount_effect = yes }
        	}
        }
        """;

    private const string AnnounceEffect = """
        # Phase on_start: tell the humans in this frontier that its era has turned.
        wilds_phase_announce_effect = {
        	scope:situation_sub_region = {
        		every_situation_sub_region_participant = {
        			limit = { is_ai = no }
        			send_interface_toast = {
        				type = event_toast_effect_neutral
        				title = wilds_toast_$PHASE$
        				left_icon = this
        			}
        		}

        		# And into the chronicle, for everyone. See Emit/ChronicleRuntimeWriter.cs.
        		gen_chr_wilds_effect = { PHASE = $PHASE$ }
        	}
        }
        """;

    /// <summary>
    /// Situation scope. Counts the world and each frontier into variables on the situation, which
    /// the description reads. Global counts walk every county once; per-frontier counts walk the
    /// sub-region's own list. The variable names carry the sub-region key because a variable
    /// name cannot be built at runtime, and this writer knows the keys.
    /// </summary>
    private static void WriteRecount(JominiBuilder b, FrontierMap frontier)
    {
        using (b.Block("wilds_situation_recount_effect"))
        {
            b.Field("save_scope_as", "wilds_situation");
            b.Blank();
            SetVar(b, "wilds_total", frontier.LandCounties.ToString(CultureInfo.InvariantCulture));
            SetVar(b, "wilds_wild", "0");
            SetVar(b, "wilds_colonies", "0");
            foreach (var s in frontier.SubRegions)
            {
                SetVar(b, $"wilds_wild_{s.Key}", "0");
                SetVar(b, $"wilds_colonies_{s.Key}", "0");
            }
            using (b.Block("if"))
            {
                using (b.Block("limit"))
                using (b.Block("NOT"))
                    b.Field("has_variable", "wilds_reclaimed");
                SetVar(b, "wilds_reclaimed", "0");
            }
            b.Blank();

            Count(b, "every_county", "wilds_is_wild_county_trigger", "wilds_wild");
            Count(b, "every_county", "is_colony_county_trigger", "wilds_colonies");
            foreach (var s in frontier.SubRegions)
            {
                using (b.Block($"situation_sub_region:{s.Key}"))
                {
                    Count(b, "every_situation_sub_region_county", "wilds_is_wild_county_trigger", $"wilds_wild_{s.Key}");
                    Count(b, "every_situation_sub_region_county", "is_colony_county_trigger", $"wilds_colonies_{s.Key}");
                }
            }
            b.Blank();

            using (b.Block("set_variable"))
            {
                b.Field("name", "wilds_share");
                using (b.Block("value"))
                {
                    b.Field("value", "var:wilds_wild");
                    b.Field("multiply", 100);
                    b.Field("divide", frontier.LandCounties);
                }
            }
        }
    }

    private static void SetVar(JominiBuilder b, string name, string value)
    {
        using (b.Block("set_variable"))
        {
            b.Field("name", name);
            b.Field("value", value);
        }
    }

    private static void Count(JominiBuilder b, string iterator, string trigger, string variable)
    {
        using (b.Block(iterator))
        {
            using (b.Block("limit")) b.Field(trigger, "yes");
            using (b.Block("scope:wilds_situation"))
            using (b.Block("change_variable"))
            {
                b.Field("name", variable);
                b.Field("add", 1);
            }
        }
    }

    /// <summary>
    /// Situation scope, once a year. Recounts, then for each frontier: settles it the year its
    /// last wild county is gone, reopens it the year a settled one goes wild again, and otherwise
    /// fires the drift catalysts that the counts earn. Then, if every frontier is settled, the
    /// closing event and the end.
    ///
    /// The reclaimed line is two thirds of the frontier's starting wild count, baked here as a
    /// number because it is a fact about the generated map and not about the game.
    /// </summary>
    private static void WriteYearly(JominiBuilder b, FrontierMap frontier)
    {
        using (b.Block("wilds_situation_yearly_effect"))
        {
            b.Field("wilds_situation_recount_effect", "yes");
            b.Blank();

            foreach (var s in frontier.SubRegions)
            {
                string wild = $"var:wilds_wild_{s.Key}";
                string colonies = $"var:wilds_colonies_{s.Key}";
                int line = s.Wild.Count * 2 / 3;

                using (b.Block($"situation_sub_region:{s.Key}"))
                {
                    using (b.Block("if"))
                    {
                        using (b.Block("limit"))
                        {
                            using (b.Block("NOT")) b.Field("sub_region_current_phase", "wilds_settled");
                            using (b.Block("scope:wilds_situation")) b.Token($"{wild} <= 0");
                        }
                        using (b.Block("change_phase")) b.Field("phase", "wilds_settled");
                    }
                    using (b.Block("else_if"))
                    {
                        using (b.Block("limit"))
                        {
                            b.Field("sub_region_current_phase", "wilds_settled");
                            using (b.Block("scope:wilds_situation")) b.Token($"{wild} > 0");
                        }
                        using (b.Block("change_phase")) b.Field("phase", "wilds_pioneers");
                    }
                    // Drift, one arm per era. Each of the four yearly catalysts is fired ONLY in
                    // the phase whose own future_phases list it, so a catalyst that fires always
                    // means something and always reads true.
                    //
                    // Fired blind, they did neither. A pristine untamed frontier would report "The
                    // Wild Returns" every year — nothing had left — and "The Frontier Falls Quiet"
                    // beside it, and both would land on phases that do not listen and accumulate
                    // nothing. The engine does not object; the catalyst history just fills with
                    // lines the player cannot act on.
                    using (b.Block("else_if"))
                    {
                        using (b.Block("limit")) b.Field("sub_region_current_phase", "wilds_untamed");
                        using (b.Block("if"))
                        {
                            using (b.Block("limit"))
                            using (b.Block("scope:wilds_situation")) b.Token($"{colonies} > 0");
                            Catalyst(b, "catalyst_wilds_frontier_stirs");
                        }
                    }
                    using (b.Block("else_if"))
                    {
                        using (b.Block("limit")) b.Field("sub_region_current_phase", "wilds_pioneers");
                        using (b.Block("if"))
                        {
                            using (b.Block("limit"))
                            using (b.Block("scope:wilds_situation")) b.Token($"{colonies} <= 0");
                            Catalyst(b, "catalyst_wilds_frontier_quiet");
                        }
                        using (b.Block("if"))
                        {
                            using (b.Block("limit"))
                            using (b.Block("scope:wilds_situation")) b.Token($"{wild} <= {line}");
                            Catalyst(b, "catalyst_wilds_land_reclaimed");
                        }
                    }
                    using (b.Block("else_if"))
                    {
                        using (b.Block("limit")) b.Field("sub_region_current_phase", "wilds_closing");
                        using (b.Block("if"))
                        {
                            using (b.Block("limit"))
                            using (b.Block("scope:wilds_situation")) b.Token($"{wild} > {line}");
                            Catalyst(b, "catalyst_wilds_wild_returns");
                        }
                    }
                }
                b.Blank();
            }

            b.Comment("The world is whole: the frontier closes, and the situation with it.");
            using (b.Block("if"))
            {
                using (b.Block("limit"))
                using (b.Block("NOT"))
                using (b.Block("any_situation_sub_region"))
                using (b.Block("NOT"))
                    b.Field("sub_region_current_phase", "wilds_settled");

                using (b.Block("every_situation_participant"))
                {
                    using (b.Block("limit")) b.Field("is_ai", "no");
                    b.Field("trigger_event", $"{TypeKey}.0002");
                }
                b.Field("end_situation", "yes");
            }
        }
    }

    private static void Catalyst(JominiBuilder b, string key)
    {
        using (b.Block("trigger_sub_region_catalyst")) b.Field("catalyst", key);
    }

    /// <param name="ruins">Whether the Ruins set ships, which decides whether
    /// <c>wilds_is_wild_county_trigger</c> may name the ruins dummy's titular kingdom.</param>
    private static void WriteTriggers(string modDir, FrontierMap frontier, bool ruins)
    {
        var b = new JominiBuilder();
        b.Comment("""
                  Triggers the Wilderness set asks of the Wilds situation. Always written; without a
                  frontier the expedition discount simply never applies. See Emit/FrontierWriter.cs.
                  """);
        b.Blank();

        // Found by TITLE and not by the `wilderness` trait, because both dummies wear that trait —
        // the titular kingdom each holds is the only thing that tells them apart. See
        // abandon_county_effect in the Wilderness set for the same discrimination.
        //
        // Ruined counties count as wild when the Ruins set ships. That is a deliberate reading of
        // what "wild" means here: not never-settled, but out of cultivation and holding nobody. The
        // Wilds window measures ground won and lost, and ground lost to ruin is lost the same way.
        // Counting only the wilderness dummy meant a frontier could lose county after county to
        // collapse with every counter it shows frozen.
        //
        // Emitted conditionally because k_gen_ruins exists only when the Ruins set ships, and a
        // trigger naming a title the database has never heard of is an error on every other map.
        if (ruins)
        {
            b.Comment("Title scope. A county nobody is working — the Wilds hold it, or it is a ruin.");
            using (b.Block("wilds_is_wild_county_trigger"))
            using (b.Block("OR"))
            {
                // Two guarded checks rather than one guard around an inner OR. That shape would
                // read better and cannot be built: JominiBuilder.Block writes `key = {`, so a
                // `holder ?=` block comes out as `holder ?= = {`. Splitting it costs one repeated
                // scope change on a trigger that already walks every county twice a year, and is
                // exactly equivalent — `?=` yields false on a county with no holder either way.
                b.Token($"holder ?= {{ has_title = title:{WildernessMap.TitleKey} }}");
                b.Token($"holder ?= {{ has_title = title:{WildernessMap.RuinsTitleKey} }}");
            }
        }
        else
        {
            b.Comment("Title scope. A county the Wilds hold — unsettled, not ruined.");
            using (b.Block("wilds_is_wild_county_trigger"))
                b.Token($"holder ?= {{ has_title = title:{WildernessMap.TitleKey} }}");
        }
        b.Blank();

        b.Comment("Character scope. A marcher lord in a frontier whose era makes expeditions cheaper.");
        using (b.Block("wilds_cheaper_expedition_trigger"))
        {
            if (frontier.IsEmpty)
            {
                b.Field("always", "no");
            }
            else
            {
                using (b.Block("any_character_situation"))
                using (b.Block("any_situation_sub_region"))
                {
                    b.Field("has_sub_region_phase_parameter", "wilds_cheaper_expeditions");
                    using (b.Block("any_situation_sub_region_participant_group"))
                    {
                        b.Field("participant_group_type", "marcher_lords");
                        b.Field("participant_group_has_character", "root");
                    }
                }
            }
        }

        string dir = Path.Combine(modDir, "common", "scripted_triggers");
        Directory.CreateDirectory(dir);
        ParadoxText.WriteBom(Path.Combine(dir, $"zz_gen_{TypeKey}_triggers.txt"), b.ToString());
    }

    // ------------------------------------------------------------------------------------------
    //  The closing event and the words
    // ------------------------------------------------------------------------------------------

    private static void WriteEvents(string modDir)
    {
        var b = new JominiBuilder();
        b.Comment("""
                  The Wilds situation's two events: the frontier introducing itself to a marcher
                  lord, and the frontier closing for good. Both are written by
                  Emit/FrontierWriter.cs. 0001 is fired by the marcher_lords group's on_join, 0002
                  by the yearly effect the year the last frontier is settled.
                  """);
        b.Blank();
        b.Field("namespace", TypeKey);
        b.Blank();

        WriteIntroEvent(b);
        b.Blank();
        WriteClosingEvent(b);

        string dir = Path.Combine(modDir, "events");
        Directory.CreateDirectory(dir);
        ParadoxText.WriteBom(Path.Combine(dir, $"gen_{TypeKey}_events.txt"), b.ToString());
    }

    /// <summary>
    /// The introduction, fired the moment a marcher lord's own domain touches a frontier.
    ///
    /// The embedded widget is <c>event_window_widget_situation_info</c> — vanilla's GENERIC
    /// situation panel, as opposed to the two DLC-specific ones beside it. No vanilla event uses
    /// it, so it is shipped but unexercised; it is structurally complete and drives off the same
    /// <c>situation_info</c> controller the Great Steppe's intro uses. It matters because an event
    /// option cannot open the Situations tab: that view is reachable only from the HUD, which is
    /// exactly why vanilla puts the panel inside the event instead of linking to it.
    ///
    /// <c>setup_scope</c> is what feeds the controller, and it is the half that fails silently —
    /// see the note on text widgets in the project memory. Without it the panel still renders and
    /// shows nothing.
    ///
    /// The wild-county count is taken live off the player's OWN frontier rather than the world,
    /// and rather than off the per-sub-region variables, which would need the writer to bake a key
    /// into the event and would tie the sentence to whichever frontier the writer guessed.
    /// </summary>
    private static void WriteIntroEvent(JominiBuilder b)
    {
        using (b.Block($"{TypeKey}.0001"))
        {
            b.Field("type", "character_event");
            b.Field("title", $"{TypeKey}.0001.t");
            b.Field("desc", $"{TypeKey}.0001.desc");
            b.Field("theme", "realm");
            using (b.Block("override_background")) b.Field("reference", "wilderness");
            b.Blank();

            b.Comment("The same guard the on_join carries, kept here too because this is the half\n" +
                      "that survives a player joining a multiplayer game late.");
            using (b.Block("trigger"))
            {
                b.Field("is_ai", "no");
                using (b.Block("NOT")) b.Field("has_character_flag", "wilds_intro_seen");
            }
            using (b.Block("cooldown")) b.Field("years", 100);
            b.Blank();

            using (b.Block("left_portrait"))
            {
                b.Field("character", "root");
                b.Field("animation", "thinking");
            }
            b.Blank();

            using (b.Block("widgets"))
            using (b.Block("widget"))
            {
                b.Quoted("gui", "event_window_widget_situation_info");
                b.Quoted("container", "dynamic_content_widget");
                b.Field("controller", "situation_info");
                using (b.Block("setup_scope"))
                using (b.Block($"situation:{TypeKey}"))
                    b.Field("save_scope_as", "situation");
            }
            b.Blank();

            using (b.Block("immediate"))
            {
                b.Field("add_character_flag", "wilds_intro_seen");
                b.Blank();
                b.Comment("How much of THIS frontier is still nobody's, counted now rather than read\n" +
                          "from the situation's per-frontier variables, which are named per key.");
                using (b.Block("set_variable"))
                {
                    b.Field("name", "wilds_intro_tally");
                    b.Field("value", "0");
                }
                // `if = { limit = { exists } }` rather than the `?=` safe-scope operator, which the
                // builder cannot write: Block() puts the style separator between key and brace, so
                // `scope:x ?` comes out as `scope:x ? = {`. Vanilla writes `?=` as one token and
                // never with a space, so that form is not something to ship and find out about.
                using (b.Block("if"))
                {
                    using (b.Block("limit")) b.Field("exists", "scope:wilds_region");
                    using (b.Block("scope:wilds_region"))
                    using (b.Block("every_situation_sub_region_county"))
                    {
                        using (b.Block("limit")) b.Field("wilds_is_wild_county_trigger", "yes");
                        using (b.Block("root"))
                        using (b.Block("change_variable"))
                        {
                            b.Field("name", "wilds_intro_tally");
                            b.Field("add", 1);
                        }
                    }
                }
                using (b.Block("save_scope_value_as"))
                {
                    b.Field("name", "wild_here");
                    b.Field("value", "var:wilds_intro_tally");
                }
                b.Field("remove_variable", "wilds_intro_tally");
            }
            b.Blank();

            b.Comment("""
                      The charter is one expedition at half price, spent by
                      colony_launch_expedition_effect. Priced in prestige so it is a decision
                      rather than a gift, and so a lord who wants no part of the frontier keeps
                      something the other one gave away.

                      ---- Why the charter is a modifier and the prices are small ----

                      It shipped first as a character FLAG worth -75 prestige, against a +75
                      prestige decline. CK3 renders costs, modifiers and stat changes in an option
                      tooltip and renders flags NOWHERE, so the charter was invisible at the moment
                      of choosing: the player saw 75 prestige spent for no stated benefit beside 75
                      prestige gained for nothing, a 150-point swing towards the option that does
                      nothing. The discount only ever surfaced afterwards, as
                      `colonize_cost_charter_desc` in the price breakdown of an expedition already
                      decided on — the wrong side of the decision.

                      Three changes, and they depend on each other. The charter is now a character
                      modifier (`wilds_frontier_charter`, in the Wilderness set's own modifiers
                      file so it exists on maps with no frontier), which puts it on the character
                      sheet where a granted benefit belongs and lets it carry an expiry instead of
                      lingering unread until its holder dies. The custom_tooltip states the
                      discount outright. And the price drops to `miniscule` while the decline drops
                      to nothing at all, which is what the original comment below always described:
                      the lord who wants no part of the frontier KEEPS what the other one spends,
                      rather than being paid for declining.

                      Ten years, not a season. The flavour text says "a season's grace", but the
                      charter is only spendable once the holder has a colonisable county in reach
                      and the gold to take it, and a window shorter than that would price 35
                      prestige for a coin flip on circumstance.
                      """);
            using (b.Block("option"))
            {
                b.Field("name", $"{TypeKey}.0001.a");
                b.Field("custom_tooltip", "wilds_charter_tt");
                using (b.Block("add_character_modifier"))
                {
                    b.Field("modifier", "wilds_frontier_charter");
                    b.Field("years", 10);
                }
                b.Field("add_prestige", "miniscule_prestige_loss");
            }
            using (b.Block("option"))
            {
                b.Field("name", $"{TypeKey}.0001.b");
                b.Field("custom_tooltip", "wilds_no_charter_tt");
            }
        }
    }

    private static void WriteClosingEvent(JominiBuilder b)
    {
        using (b.Block($"{TypeKey}.0002"))
        {
            b.Field("type", "character_event");
            b.Field("title", $"{TypeKey}.0002.t");
            b.Field("desc", $"{TypeKey}.0002.desc");
            b.Field("theme", "realm");
            using (b.Block("left_portrait"))
            {
                b.Field("character", "root");
                b.Field("animation", "happiness");
            }
            b.Blank();
            using (b.Block("option"))
            {
                b.Field("name", $"{TypeKey}.0002.a");
                b.Field("add_prestige", "major_prestige_gain");
            }
        }
    }

    private static void WriteLocalisation(string modDir, FrontierMap frontier)
    {
        var loc = new LocFile();

        // Two pairs of keys, as vanilla's Steppe has: situation_type_<key> names the TYPE and
        // situation_<key> names the running instance, which is where a scope is in hand and the
        // counters can be read. Situation.MakeScope.Var reads a variable on the situation; the
        // recount keeps them fresh.
        loc.Add($"situation_type_{TypeKey}", "The Wilds");
        loc.Add($"situation_type_{TypeKey}_desc",
            "The unsettled ground of the known world, and the colonies going into it. Each frontier " +
            "runs its own era: untamed, then an age of pioneers, then the closing of the frontier, " +
            "until no wild county is left in it.");
        loc.AddBuilt($"situation_{TypeKey}", $"$situation_type_{TypeKey}$");
        loc.AddBuilt($"situation_{TypeKey}_desc",
            "Of the [Situation.MakeScope.Var('wilds_total').GetValue|V0] counties of the known world, " +
            "#V [Situation.MakeScope.Var('wilds_wild').GetValue|V0]#! still belong to nobody — " +
            "[Situation.MakeScope.Var('wilds_share').GetValue|V0]% of it — and " +
            "#V [Situation.MakeScope.Var('wilds_colonies').GetValue|V0]#! colonies are finding their feet. " +
            "[Situation.MakeScope.Var('wilds_reclaimed').GetValue|V0] counties have been won from the wild.\\n\\n" +
            string.Join("\\n", frontier.SubRegions.Select(s =>
                $"${TypeKey}_sub_region_{s.Key}$: " +
                $"[Situation.MakeScope.Var('wilds_wild_{s.Key}').GetValue|V0] wild, " +
                $"[Situation.MakeScope.Var('wilds_colonies_{s.Key}').GetValue|V0] colonies")));
        loc.Blank();

        foreach (var s in frontier.SubRegions)
        {
            loc.Add($"{TypeKey}_sub_region_{s.Key}", s.Name);
            loc.AddBuilt($"{TypeKey}_sub_region_{s.Key}_desc",
                "$SITUATION_SUB_REGION_TOOLTIP_DESC$\\n\\n#weak " +
                $"{ParadoxText.Loc(s.Name)} began with {s.Wild.Count} unsettled " +
                (s.Wild.Count == 1 ? "county" : "counties") +
                $" and {s.Ring.Count} settled on its edge.#!");
            loc.Add(s.RegionKey, s.Name);
        }
        loc.Blank();

        loc.Add($"{TypeKey}_participant_group_colonists", "Colonists");
        loc.Add($"{TypeKey}_participant_group_colonists_desc", "Rulers whose only land is a colony in this frontier.");
        loc.Add($"{TypeKey}_participant_group_marcher_lords", "Marcher Lords");
        loc.Add($"{TypeKey}_participant_group_marcher_lords_desc", "Rulers who hold a county in this frontier or on its edge.");
        loc.Blank();

        // TWO name keys per phase, and the window reads a different one in each column.
        //
        // `SituationPhase.GetName` — the "Current" column — reads
        // `<type>_<phase>_situation_phase`. `SituationPhaseType.GetName` — the "Future" column,
        // the possible-next-phases list and every phase tooltip — reads the PHASE KEY ITSELF.
        // With only the first written, the current phase read "The Untamed Wilds" and everything
        // else read a raw `wilds_pioneers`. Vanilla writes the plain key as the real name and
        // makes the prefixed one a `$reference$` to it, which is what is done here.
        //
        // ck3-tiger does not know this pattern and reported nothing; the game logs nothing either.
        var phases = new (string Key, string Name, string Desc, string Toast)[]
        {
            ("wilds_untamed", "The Untamed Wilds",
                "Nobody has gone in. The wild is whole, and the few who reach into it win renown for the reaching.",
                "The wild has the frontier again."),
            ("wilds_pioneers", "The Age of Pioneers",
                "Colonies stand in the wild. Settlers build fast and hold what they take; the lords on the edge grow rich on what comes out, and an expedition is cheaper to raise.",
                "The Age of Pioneers has begun on your frontier."),
            ("wilds_closing", "The Closing of the Frontier",
                "Most of the frontier is won. The settled land pulls the rest in: colonies prosper, the marches thrive, and what is still wild will not be for long.",
                "The frontier is closing."),
            ("wilds_settled", "The Frontier Settled",
                "No wild county remains in this frontier. The marches are marches no longer.",
                "Your frontier is settled. No wild county remains."),
        };
        foreach (var (key, name, desc, toast) in phases)
        {
            loc.Add(key, name);
            loc.Add($"{key}_desc", desc);
            loc.AddBuilt($"{TypeKey}_{key}_situation_phase", $"${key}$");
            loc.AddBuilt($"{TypeKey}_{key}_situation_phase_desc", $"${key}_desc$");
            loc.Add($"wilds_toast_{key}", toast);
        }
        loc.Blank();

        // TWO keys per modifier set, and the second one is not optional. The engine draws the set
        // header as `<set>` followed by `<set>_concept`, and with the second missing it printed
        // the raw key — `wilds_glory_set_concept` sat next to "Renown of the Frontier" in game.
        // Nothing logs it and ck3-tiger does not know the pattern. Vanilla's Steppe writes the
        // same pair for all five of its sets (`situation_steppe_*_effects` / `_concept`), and the
        // value there is always a bare concept link, which is what the header has room for.
        foreach (var (key, name, concept) in new[]
        {
            ("wilds_glory_set", "Renown of the Frontier", "[prestige|E]"),
            ("wilds_settlement_set", "Settling the Wild", "[county_control|E]"),
            ("wilds_marches_set", "The Marches", "[war|E]"),
            ("wilds_prosperity_set", "The Frontier Closes", "[county_development|E]"),
            ("wilds_peace_set", "A Settled Land", "[county|E]"),
        })
        {
            loc.Add(key, name);
            loc.AddBuilt($"{key}_concept", concept);
        }
        loc.Add("situation_parameter_wilds_cheaper_expeditions", "Expeditions into the wild cost 25% less");
        loc.Blank();

        foreach (var (key, name, desc) in Catalysts)
        {
            loc.Add(key, name);
            loc.Add($"{key}_desc", desc);
        }
        loc.Blank();

        // The introduction. `[wild_here|0]` is the scope value the event's immediate saves after
        // counting the player's own frontier; the same read the ruins events use for a year.
        loc.Add($"{TypeKey}.0001.t", "Where the Maps Run Out");
        loc.AddBuilt($"{TypeKey}.0001.desc",
            "Your marches end at a line no one drew. Past the last ditch and the last ploughed strip "
            + "the road gives up, and beyond it lie #V [wild_here|0]#! counties that answer to nobody "
            + "at all — no lord, no tithe, no name on any roll but the one your grandfather's "
            + "cartographer invented for the blank part.\\n\\nThe men who live on that edge have "
            + "opinions about it. Some want a wall. Some want a charter and a season's grace to go "
            + "and see what is out there. Either way they are looking at you, because the frontier "
            + "is yours now whether you asked for it or not.");
        loc.Add($"{TypeKey}.0001.a",
            "Draw up a charter. Someone should go and see.");
        loc.Add($"{TypeKey}.0001.b",
            "My border is a border. Let it stay one.");
        // What option (a) buys, said where the choice is made. The modifier line above it names
        // the charter and its ten years; this is the sentence that says what it is FOR, without
        // making the player hover to find out. The modifier's own name and description live with
        // the modifier, in the Wilderness set's wilderness_colonization_l_english.yml.
        loc.AddBuilt("wilds_charter_tt",
            "Your next expedition into the wild costs #V 50%#! less — @gold_icon! and "
            + "@prestige_icon! both.");
        loc.AddBuilt("wilds_no_charter_tt",
            "#weak No charter, and nothing spent on one. Expeditions into this frontier cost "
            + "you the full price.#!");
        loc.Blank();

        loc.Add($"{TypeKey}.0002.t", "The Frontier Closes");
        loc.Add($"{TypeKey}.0002.desc",
            "The last wild county has been claimed. Where the maps once ran out there are now fields, " +
            "walls and roads, and the marcher lords who held the edge hold the middle of somewhere. " +
            "There is no more frontier. The known world is whole.");
        loc.Add($"{TypeKey}.0002.a", "It was ours to take.");

        loc.Write(Path.Combine(modDir, "localization", "english", $"zz_gen_{TypeKey}_l_english.yml"));
    }
}
