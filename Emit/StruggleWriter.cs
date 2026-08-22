using System.Text;
using Ck3MapGen.Config;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// Writes the five files a generated struggle needs, and nothing else.
///
/// The reason a struggle is worth generating at all is that CK3 already contains the machinery to
/// run one and does not care whose struggle it is. Vanilla's base-game on_actions fire catalysts
/// with <c>every_character_struggle = { limit = { phase_has_catalyst = X } ... }</c> — into any
/// struggle the actor belongs to — and ~53 phase parameters are read by base-game script through
/// <c>has_struggle_phase_parameter</c>. So the whole of the progression and most of the mechanical
/// payload arrives for free, and what has to be written is only the declaration: who is in it,
/// where it is, and what each mood does.
///
/// None of the five files is gated on a DLC. Vanilla's own struggles are — the Iberian one checks
/// <c>has_dlc_feature = the_fate_of_iberia</c> before starting and its ending decisions check
/// <c>has_fp2_dlc_trigger</c> — but the framework underneath is base game: <c>common/struggle</c>,
/// its schema docs and all seven struggle/situation .gui files ship in the base install with no
/// gating on them. A generated struggle simply omits the checks.
///
/// Pass one deliberately writes no ending decisions, so a generated struggle turns forever. See
/// <see cref="WriteDefinition"/>.
/// </summary>
public static class StruggleWriter
{
    /// <summary>The event namespace and the id of the yearly drift ticker.</summary>
    private const string DriftEvent = "gen_struggle.1";

    public static void WriteAll(string modDir, string gameDir, MapConfig cfg, StruggleMap struggles,
            Flatmap flatmap, ProvinceMap provinces, int[] order)
    {
        if (struggles.Struggles.Count == 0)
        {
            Console.WriteLine("  struggles: none (no kingdom contested enough)");
            return;
        }

        WriteRegions(modDir, struggles);
        WriteDefinition(modDir, struggles);
        WriteHistory(modDir, cfg, struggles);
        WriteDriftEvent(modDir);
        WriteEndingDecisions(modDir, struggles);
        WriteEndingModifiers(modDir);
        WritePhaseIcons(modDir, gameDir, struggles);
        WriteHeaderBackgrounds(modDir, gameDir, struggles);
        WriteLocalisation(modDir, struggles);

        int art = StruggleArt.WriteBackgrounds(modDir, struggles, flatmap, provinces, order);
        if (art > 0) Console.WriteLine($"  struggle backgrounds: {art} cut from the flatmap");

        foreach (var s in struggles.Struggles)
        {
            Console.WriteLine($"  struggle: {s.Name} — {Count(s.Duchies.Count, "duchy")}, "
                            + $"{Count(s.Cultures.Count, "culture")}, {Count(s.Faiths.Count, "faith")}, "
                            + $"tension {s.Tension}, starting in {s.PhaseFor(s.StartMood).Name}");
        }
    }

    private static string Count(int n, string noun) => n == 1
        ? $"1 {noun}"
        : $"{n} {(noun.EndsWith('y') ? noun[..^1] + "ies" : noun + "s")}";

    /// <summary>
    /// The region each struggle covers, in its own file.
    ///
    /// A new key rather than a vanilla one. <see cref="CompatibilityWriter.WriteGeographicalRegions"/>
    /// re-declares every vanilla region key against generated titles so that hardcoded script keeps
    /// resolving, but the members it gives them are arbitrary — one county each, or an even slice of
    /// the province list for the graphical ones. Those keys therefore describe nowhere, and a
    /// struggle pointed at one would cover nowhere.
    ///
    /// Written into the same directory, which is safe because the filename is ours: that directory
    /// is blanked from vanilla's filenames and then rewritten under those same filenames, so a name
    /// vanilla never used cannot be clobbered by either pass.
    /// </summary>
    private static void WriteRegions(string modDir, StruggleMap struggles)
    {
        var sb = new StringBuilder();
        sb.Append("# Regions for the generated struggles. Separate keys from the re-declared\n");
        sb.Append("# vanilla ones, which carry placeholder members and describe nowhere.\n\n");

        foreach (var s in struggles.Struggles)
        {
            sb.Append($"# {s.Name}\n");
            sb.Append($"{s.RegionKey} = {{\n");
            sb.Append("\tduchies = {\n");
            foreach (var duchy in s.Duchies) sb.Append($"\t\t{duchy.Key}\n");
            sb.Append("\t}\n}\n\n");
        }

        string dir = Path.Combine(modDir, "map_data", "geographical_regions");
        Directory.CreateDirectory(dir);
        ParadoxText.WriteBom(Path.Combine(dir, "zz_gen_struggle_regions.txt"), sb.ToString());
    }

    /// <summary>
    /// The struggles themselves.
    ///
    /// No <c>ending_decisions</c>. The schema notes that at least one phase should carry one, and a
    /// struggle without them can only ever turn — which is the honest state of pass one rather than
    /// an oversight: an ending decision has to name a win condition, and deciding what "winning"
    /// means on a generated map is a design question this pass does not answer. The cost is that
    /// the struggle window's "how do I end this" list is empty, and the game may log a complaint
    /// about it. Both are visible, neither breaks the phase machinery.
    /// </summary>
    private static void WriteDefinition(string modDir, StruggleMap struggles)
    {
        var sb = new StringBuilder();
        sb.Append("# Generated struggles.\n");
        sb.Append("# Catalysts and phase parameters are vanilla keys, chosen because base-game\n");
        sb.Append("# script fires and reads them generically for any struggle. See MapGen/Struggles.cs.\n\n");

        foreach (var s in struggles.Struggles)
        {
            sb.Append($"{s.Key} = {{\n");
            sb.Append($"\tillustration = \"{s.Illustration}\"\n\n");

            sb.Append("\tcultures = {\n");
            foreach (var c in s.Cultures) sb.Append($"\t\t{c.Key}\n");
            sb.Append("\t}\n");

            sb.Append("\tfaiths = {\n");
            foreach (var f in s.Faiths) sb.Append($"\t\t{f.Key}\n");
            sb.Append("\t}\n");

            sb.Append($"\tregions = {{ {s.RegionKey} }}\n\n");

            // Vanilla's own figure. A culture is pulled in when this share of its counties lies
            // inside the region, which for generated cultures is nearly always all of them --
            // they are built regionally in the first place -- so the number mostly decides whether
            // a culture that spills over the border counts as living here or visiting.
            sb.Append("\tinvolvement_prerequisite_percentage = 0.8\n");
            sb.Append("\ttransition_state_duration = { months = 3 }\n\n");

            // The drift ticker. Vanilla starts its equivalent from the struggle's own on_start and
            // the event re-queues itself yearly; ours does the same rather than calling vanilla's
            // neutral_struggle.0001, which also runs Persian and Great Pact logic we have no part in.
            sb.Append("\ton_start = {\n");
            sb.Append($"\t\ttrigger_event = {DriftEvent}\n");
            sb.Append("\t}\n\n");

            sb.Append($"\tstart_phase = {s.PhaseFor(s.StartMood).Key}\n\n");
            sb.Append("\tphase_list = {\n");

            foreach (var phase in s.Phases) WritePhase(sb, s, phase);

            sb.Append("\t}\n");
            sb.Append("}\n\n");
        }

        string dir = Path.Combine(modDir, "common", "struggle", "struggles");
        Directory.CreateDirectory(dir);
        ParadoxText.WriteBom(Path.Combine(dir, "01_gen_struggles.txt"), sb.ToString());
    }

    private static void WritePhase(StringBuilder sb, GeneratedStruggle s, StrugglePhase phase)
    {
        sb.Append($"\t\t{phase.Key} = {{\n");

        sb.Append("\t\t\tfuture_phases = {\n");
        foreach (var (mood, catalysts) in phase.Futures)
        {
            sb.Append($"\t\t\t\t{s.PhaseFor(mood).Key} = {{\n");
            sb.Append("\t\t\t\t\tcatalysts = {\n");
            foreach (var (catalyst, weight) in catalysts)
                sb.Append($"\t\t\t\t\t\t{catalyst} = {weight}\n");
            sb.Append("\t\t\t\t\t}\n");
            sb.Append("\t\t\t\t}\n");
        }
        sb.Append("\t\t\t}\n\n");

        // One block per group that has anything in it. An empty war_effects block parses but
        // renders as a heading with nothing under it, which reads as a bug in the struggle window.
        foreach (string group in Groups)
        {
            var parameters = phase.Parameters.Where(p => p.Group == group).ToList();
            var modifiers = phase.Modifiers.Where(m => m.Group == group).ToList();
            if (parameters.Count == 0 && modifiers.Count == 0) continue;

            sb.Append($"\t\t\t{BlockName(group)} = {{\n");
            sb.Append($"\t\t\t\tname = {group}\n");

            foreach (string audience in Audiences)
            {
                var mine = parameters.Where(p => p.Audience == audience).ToList();
                if (mine.Count == 0) continue;

                sb.Append($"\t\t\t\t{audience}_parameters = {{\n");
                foreach (var (_, _, parameter) in mine)
                    sb.Append($"\t\t\t\t\t{parameter} = yes\n");
                sb.Append("\t\t\t\t}\n");
            }

            foreach (var block in modifiers.Select(m => m.Block).Distinct())
            {
                sb.Append($"\t\t\t\t{block} = {{\n");
                foreach (var (_, _, key, value) in modifiers.Where(m => m.Block == block))
                    sb.Append($"\t\t\t\t\t{key} = {value}\n");
                sb.Append("\t\t\t\t}\n");
            }

            sb.Append("\t\t\t}\n\n");
        }

        // Every phase lists every ending, which is what vanilla does and is not redundant: the list
        // is informational, and it is the only place a player can read what finishing this thing
        // would take before any of it is within reach. A phase that listed only its own reachable
        // ending would hide the other two exactly while the player is deciding which to aim for.
        sb.Append("\t\t\tending_decisions = {\n");
        foreach (var ending in s.Endings) sb.Append($"\t\t\t\t{ending.Key}\n");
        sb.Append("\t\t\t}\n");

        sb.Append("\t\t}\n\n");
    }

    /// <summary>The four effect groups, in the order the struggle window lists them.</summary>
    private static readonly string[] Groups =
        ["WAR_EFFECTS_NAME", "FAITH_EFFECTS_NAME", "CULTURE_EFFECTS_NAME", "OTHER_EFFECTS_NAME"];

    private static readonly string[] Audiences = ["common", "involved", "interloper", "uninvolved"];

    private static string BlockName(string group) => group switch
    {
        "WAR_EFFECTS_NAME" => "war_effects",
        "FAITH_EFFECTS_NAME" => "faith_effects",
        "CULTURE_EFFECTS_NAME" => "culture_effects",
        _ => "other_effects",
    };

    /// <summary>
    /// The decisions that finish a struggle.
    ///
    /// <c>is_invisible = yes</c> is not a mistake: an ending decision is never listed in the
    /// decisions panel. The struggle window reads the <c>ending_decisions</c> list off the current
    /// phase and renders its own buttons, which is the only place these are ever seen or taken —
    /// vanilla's three work exactly this way.
    ///
    /// The struggle is ended in the decision's own effect rather than through a coda event. Vanilla
    /// routes through a fullscreen event so it can offer the ender a choice of reward, and that
    /// event needs its own background art and its own option tree; none of that is reachable for a
    /// generated struggle, and <c>end_struggle</c> works the same from either place.
    /// </summary>
    private static void WriteEndingDecisions(string modDir, StruggleMap struggles)
    {
        var sb = new StringBuilder();
        sb.Append("# Ending decisions for the generated struggles.\n");
        sb.Append("# Invisible by design: the struggle window renders these from the current\n");
        sb.Append("# phase's ending_decisions list, and they appear nowhere else.\n\n");

        foreach (var s in struggles.Struggles)
        {
            foreach (var ending in s.Endings)
            {
                sb.Append($"{ending.Key} = {{\n");
                sb.Append("\tdecision_group_type = major\n");
                sb.Append($"\ttitle = {ending.Key}\n");
                sb.Append($"\tpicture = {{ reference = \"{Picture(ending.Kind)}\" }}\n");
                sb.Append($"\textra_picture = \"{ExtraPicture(ending.Kind)}\"\n");
                sb.Append($"\tdesc = {ending.Key}_desc\n");
                sb.Append($"\tselection_tooltip = {ending.Key}_tooltip\n");
                sb.Append("\tis_invisible = yes\n");
                sb.Append("\tsort_order = 80\n");
                sb.Append("\tcooldown = { days = 1 }\n\n");

                // Landless adventurers are excluded the way vanilla excludes them: they hold no
                // county in the region, so every territorial test below would be vacuous for them.
                sb.Append("\tis_shown = {\n");
                sb.Append("\t\tis_landless_adventurer = no\n");
                sb.Append($"\t\texists = struggle:{s.Key}\n");
                sb.Append("\t\tany_character_struggle = {\n");
                sb.Append("\t\t\tinvolvement = involved\n");
                sb.Append($"\t\t\tis_struggle_type = {s.Key}\n");
                sb.Append("\t\t}\n");
                sb.Append("\t}\n\n");

                sb.Append("\tis_valid = {\n");
                // Independent rulers only. A vassal ending the struggle their liege is fighting
                // would settle a question that was never theirs to settle.
                sb.Append("\t\ttop_liege = this\n");
                sb.Append($"\t\texists = struggle:{s.Key}\n");
                sb.Append($"\t\tprestige_level >= {PrestigeLevel(ending.Kind)}\n\n");

                // Wrapped rather than bare so the window shows a sentence instead of the trigger.
                // An unwrapped nest of any_involved_ruler blocks renders as a wall of scope names
                // that tells the player nothing about what to actually do. The sentence itself is
                // written by WriteLocalisation against the same condition name.
                foreach (string condition in StruggleMap.Conditions(ending.Kind))
                {
                    sb.Append("\t\tcustom_tooltip = {\n");
                    sb.Append($"\t\t\ttext = {ending.Key}_{condition}_tt\n");
                    sb.Append(Condition(condition, ending.Kind, s));
                    sb.Append("\t\t}\n\n");
                }

                sb.Append("\t}\n\n");

                sb.Append("\teffect = {\n");
                sb.Append(Reward(ending, s));
                sb.Append($"\t\tstruggle:{s.Key} = {{ end_struggle = {ending.Key} }}\n");
                sb.Append("\t}\n\n");

                sb.Append("\tcost = {}\n\n");

                // Vanilla's cadence. Below kingdom the check never runs — a count who somehow
                // qualified would be ending a struggle they cannot police — and above it the AI
                // reconsiders twice a year rather than constantly, because the tests walk every
                // involved ruler and every county in the region.
                sb.Append("\tai_check_interval_by_tier = {\n");
                sb.Append("\t\tbarony = 0\n\t\tcounty = 0\n\t\tduchy = 0\n");
                sb.Append("\t\tkingdom = 120\n\t\tempire = 120\n\t\thegemony = 120\n");
                sb.Append("\t}\n\n");
                sb.Append("\tai_potential = { always = yes }\n");
                sb.Append("\tai_will_do = { base = 100 }\n");
                sb.Append("}\n\n");
            }
        }

        string dir = Path.Combine(modDir, "common", "decisions");
        Directory.CreateDirectory(dir);
        ParadoxText.WriteBom(Path.Combine(dir, "01_gen_struggle_endings.txt"), sb.ToString());
    }

    /// <summary>
    /// The trigger behind one named condition.
    ///
    /// Every territorial test goes through <c>any_county_in_region</c> with a <c>percent</c>, which
    /// is the one thing about vanilla's endings that transfers to a generated map unchanged: the
    /// percentage is of the counties in the region, so "half the region" means the same thing over
    /// five duchies as over nine and no threshold has to be scaled per seed.
    /// </summary>
    private static string Condition(string condition, StruggleEnding kind, GeneratedStruggle s)
        => (condition, kind) switch
        {
            // The control clause is an override, not a second requirement: a ruler who has taken
            // the whole region has plainly settled the question whatever mood the region is in,
            // and making them wait for a phase they can no longer influence would be perverse.
            ("phase", StruggleEnding.Dominance) =>
                "\t\t\tOR = {\n"
                + $"\t\t\t\tstruggle:{s.Key} = {{ is_struggle_phase = {s.PhaseFor(StruggleMood.Bloodshed).Key} }}\n"
                + $"\t\t\t\tcompletely_controls_region = {s.RegionKey}\n"
                + "\t\t\t}\n",

            ("phase", StruggleEnding.StatusQuo) =>
                $"\t\t\tstruggle:{s.Key} = {{ is_struggle_phase = {s.PhaseFor(StruggleMood.Accommodation).Key} }}\n",

            ("phase", _) =>
                $"\t\t\tstruggle:{s.Key} = {{ is_struggle_phase = {s.PhaseFor(StruggleMood.Concord).Key} }}\n",

            ("hold", StruggleEnding.Dominance) => Held(s, ">=", "0.5"),

            // The two settlements require the opposite of dominance: you have to *not* have won.
            ("hold", _) => Held(s, "<", "0.5"),

            ("rivals", _) => NoRivalHolds(s, kind == StruggleEnding.Dominance ? "0.25" : "0.5"),

            ("peace", _) => NoInvolvedWar(s),

            _ => AllAllied(s),
        };

    private static string Held(GeneratedStruggle s, string comparator, string percent) =>
        "\t\t\tany_county_in_region = {\n"
        + $"\t\t\t\tregion = {s.RegionKey}\n"
        + $"\t\t\t\tpercent {comparator} {percent}\n"
        + "\t\t\t\tholder.top_liege = root\n"
        + "\t\t\t}\n";

    /// <summary>
    /// Nobody else holds more than <paramref name="percent"/> of the region.
    ///
    /// The double negative is vanilla's and is load-bearing: there is no "every involved ruler
    /// holds less than X" form, so it has to be written as the absence of one who holds more. The
    /// <c>prev</c> in the innermost block is the involved ruler being tested, not root — reading it
    /// as root inverts the whole test into "nobody else's counties are mine", which is trivially
    /// true and would make the ending always available.
    /// </summary>
    private static string NoRivalHolds(GeneratedStruggle s, string percent) =>
        $"\t\t\tstruggle:{s.Key} = {{\n"
        + "\t\t\t\tNOT = {\n"
        + "\t\t\t\t\tany_involved_ruler = {\n"
        + "\t\t\t\t\t\texists = primary_title\n"
        + "\t\t\t\t\t\tthis != root\n"
        + "\t\t\t\t\t\ttop_liege = this\n"
        + "\t\t\t\t\t\tprimary_title = { is_mercenary_company = no }\n"
        + "\t\t\t\t\t\tany_county_in_region = {\n"
        + $"\t\t\t\t\t\t\tregion = {s.RegionKey}\n"
        + $"\t\t\t\t\t\t\tpercent > {percent}\n"
        + "\t\t\t\t\t\t\tholder.top_liege = prev\n"
        + "\t\t\t\t\t\t}\n"
        + "\t\t\t\t\t}\n"
        + "\t\t\t\t}\n"
        + "\t\t\t}\n";

    private static string NoInvolvedWar(GeneratedStruggle s) =>
        "\t\t\tNOT = {\n"
        + $"\t\t\t\tstruggle:{s.Key} = {{\n"
        + "\t\t\t\t\tany_involved_ruler = {\n"
        + "\t\t\t\t\t\ttop_liege = this\n"
        + "\t\t\t\t\t\tis_landless_adventurer = no\n"
        + "\t\t\t\t\t\tprimary_title = { is_mercenary_company = no }\n"
        + "\t\t\t\t\t\tany_primary_war_enemy = {\n"
        + "\t\t\t\t\t\t\ttop_liege = this\n"
        + "\t\t\t\t\t\t\tis_landless_adventurer = no\n"
        + "\t\t\t\t\t\t\tprimary_title = { is_mercenary_company = no }\n"
        + "\t\t\t\t\t\t\tany_character_struggle = {\n"
        + "\t\t\t\t\t\t\t\tinvolvement = involved\n"
        + $"\t\t\t\t\t\t\t\tis_struggle_type = {s.Key}\n"
        + "\t\t\t\t\t\t\t}\n"
        + "\t\t\t\t\t\t}\n"
        + "\t\t\t\t\t}\n"
        + "\t\t\t\t}\n"
        + "\t\t\t}\n";

    /// <summary>
    /// Every other involved independent ruler is an ally or thinks well of you.
    ///
    /// Vanilla's conciliation ending demands alliances from *everyone*, which works over Iberia's
    /// handful of independent realms and would be close to unreachable over a generated kingdom
    /// that can hold a dozen. The opinion clause is the deliberate relaxation: friendship counted
    /// as well as treaty.
    /// </summary>
    private static string AllAllied(GeneratedStruggle s) =>
        $"\t\t\tstruggle:{s.Key} = {{\n"
        + "\t\t\t\tNOT = {\n"
        + "\t\t\t\t\tany_involved_ruler = {\n"
        + "\t\t\t\t\t\ttop_liege = this\n"
        + "\t\t\t\t\t\tis_landless_adventurer = no\n"
        + "\t\t\t\t\t\tprimary_title = { is_mercenary_company = no }\n"
        + "\t\t\t\t\t\tprimary_title = { is_holy_order = no }\n"
        + "\t\t\t\t\t\tNOR = {\n"
        + "\t\t\t\t\t\t\tthis = root\n"
        + "\t\t\t\t\t\t\tis_allied_to = root\n"
        + "\t\t\t\t\t\t\topinion = { target = root value >= 60 }\n"
        + "\t\t\t\t\t\t}\n"
        + "\t\t\t\t\t}\n"
        + "\t\t\t\t}\n"
        + "\t\t\t}\n";

    /// <summary>
    /// What taking an ending is worth.
    ///
    /// Three things, in ascending order of how long they last: a one-off in prestige and renown, a
    /// permanent character modifier, and twenty years of growth on the ender's own counties inside
    /// the region. The last is the only one that marks the map rather than the man, and it is the
    /// reason a finished struggle leaves a visibly better-off region behind rather than simply
    /// disappearing out of the interface.
    /// </summary>
    private static string Reward(StruggleEndingDef ending, GeneratedStruggle s)
    {
        var sb = new StringBuilder();

        sb.Append("\t\tadd_prestige_level = 1\n");

        switch (ending.Kind)
        {
            case StruggleEnding.Dominance:
                sb.Append("\t\tdynasty = { add_dynasty_prestige = 5000 }\n");
                break;
            case StruggleEnding.StatusQuo:
                sb.Append("\t\tdynasty = { add_dynasty_prestige = 3000 }\n");
                break;
            default:
                sb.Append("\t\tdynasty = { add_dynasty_prestige = 3000 }\n");
                sb.Append("\t\tadd_piety_level = 1\n");
                break;
        }

        sb.Append($"\t\tadd_character_modifier = {{ modifier = {ending.Modifier} }}\n");

        sb.Append("\t\tevery_county_in_region = {\n");
        sb.Append($"\t\t\tregion = {s.RegionKey}\n");
        sb.Append("\t\t\tlimit = { holder.top_liege = root }\n");
        sb.Append("\t\t\tadd_county_modifier = {\n");
        sb.Append($"\t\t\t\tmodifier = {StruggleMap.PeaceDividend}\n");
        sb.Append("\t\t\t\tyears = 20\n");
        sb.Append("\t\t\t}\n");
        sb.Append("\t\t}\n");

        return sb.ToString();
    }

    /// <summary>
    /// The reward modifiers, written once however many struggles there are.
    ///
    /// Their keys carry no struggle in them on purpose: the reward for mastering a region is the
    /// same reward whichever region it was, and giving each struggle its own copy would multiply
    /// the definitions without changing a single number in them.
    /// </summary>
    private static void WriteEndingModifiers(string modDir)
    {
        string text =
            $$"""
              # Rewards for ending a generated struggle. Keyed by outcome, not by struggle:
              # mastering one region is worth what mastering another is.

              {{StruggleMap.Modifier(StruggleEnding.Dominance)}} = {
              	icon = martial_positive

              	same_culture_opinion = 10
              	different_culture_opinion = -5
              	monthly_county_control_growth_add = 0.5
              	monthly_prestige_gain_mult = 0.15
              }

              {{StruggleMap.Modifier(StruggleEnding.StatusQuo)}} = {
              	icon = stewardship_positive

              	vassal_opinion = 10
              	monthly_prestige_gain_mult = 0.15
              	defender_advantage = 10
              }

              {{StruggleMap.Modifier(StruggleEnding.Concord)}} = {
              	icon = diplomacy_positive

              	different_faith_opinion = 15
              	different_culture_opinion = 15
              	monthly_piety_gain_mult = 0.15
              	diplomacy = 2
              }

              # Left on the ender's own counties inside the region for twenty years.
              {{StruggleMap.PeaceDividend}} = {
              	icon = county_modifier_development_positive

              	development_growth_factor = 0.25
              	county_opinion_add = 10
              }

              """;

        string dir = Path.Combine(modDir, "common", "modifiers");
        Directory.CreateDirectory(dir);
        ParadoxText.WriteBom(Path.Combine(dir, "01_gen_struggle_modifiers.txt"), text);
    }

    /// <summary>
    /// Base-game decision art, as with everything else this feature points at.
    ///
    /// The <c>struggle_decision_buttons</c> set is prefixed fp2/fp3 but lives in the game's own gfx
    /// tree rather than inside a DLC folder, so it ships with every install — the same argument
    /// that made the Iberian phase icons safe to copy, now confirmed in-game.
    /// </summary>
    private static string ExtraPicture(StruggleEnding kind) => kind switch
    {
        StruggleEnding.Dominance => "gfx/interface/illustrations/struggle_decision_buttons/fp2_decision_hostility.dds",
        StruggleEnding.StatusQuo => "gfx/interface/illustrations/struggle_decision_buttons/fp2_decision_compromise.dds",
        _ => "gfx/interface/illustrations/struggle_decision_buttons/fp2_decision_conciliation.dds",
    };

    private static string Picture(StruggleEnding kind) => kind switch
    {
        StruggleEnding.Dominance => "gfx/interface/illustrations/decisions/decision_found_kingdom.dds",
        StruggleEnding.StatusQuo => "gfx/interface/illustrations/decisions/decision_legitimacy.dds",
        _ => "gfx/interface/illustrations/decisions/decision_golden_age.dds",
    };

    /// <summary>
    /// How renowned the ender has to be.
    ///
    /// Dominance asks least, because its territorial bar is the hardest of the three and asking for
    /// both would make it the ending nobody reaches. The two settlements ask for Exalted among Men,
    /// which is vanilla's figure and is doing the work its territorial tests deliberately do not:
    /// they are the endings for a ruler who did *not* conquer the region, so something other than
    /// conquest has to have made them the one who gets to close it.
    /// </summary>
    private static string PrestigeLevel(StruggleEnding kind) => kind switch
    {
        StruggleEnding.Dominance => "high_prestige_level",
        _ => "very_high_prestige_level",
    };

    /// <summary>
    /// Starts each struggle.
    ///
    /// On the bookmark date itself rather than centuries before it, which is where vanilla puts
    /// its own. The early date buys nothing here — history effects are applied, not simulated, so
    /// an earlier start does not mean the struggle has been *turning* since then, and the sense
    /// that it has is carried by the chronicle and the phase prose instead. It does cost
    /// something: <c>on_start</c> queues the yearly drift event, and queueing an event out of
    /// history processing a hundred years before the game begins is a good deal less obviously
    /// safe than queueing it on day one.
    /// </summary>
    private static void WriteHistory(string modDir, MapConfig cfg, StruggleMap struggles)
    {
        var sb = new StringBuilder();
        sb.Append("# Starts the generated struggles on the bookmark date.\n\n");
        sb.Append($"{cfg.StartDate} = {{\n");
        sb.Append("\teffect = {\n");

        foreach (var s in struggles.Struggles)
        {
            sb.Append($"\t\t# {s.Name}\n");
            sb.Append("\t\tstart_struggle = {\n");
            sb.Append($"\t\t\tstruggle_type = {s.Key}\n");
            sb.Append($"\t\t\tstart_phase = {s.PhaseFor(s.StartMood).Key}\n");
            sb.Append("\t\t}\n");
        }

        sb.Append("\t}\n}\n");

        string dir = Path.Combine(modDir, "history", "struggles");
        Directory.CreateDirectory(dir);
        ParadoxText.WriteBom(Path.Combine(dir, "01_gen_struggles.txt"), sb.ToString());
    }

    /// <summary>
    /// The yearly drift ticker.
    ///
    /// <c>catalyst_passing_of_time</c> is the one catalyst no on_action fires — vanilla drives it
    /// from a hidden self-requeueing event started by the struggle itself. This is that event, cut
    /// down to the part that is ours. Without it a struggle whose region happens to be quiet never
    /// changes phase at all, because every other catalyst needs somebody to do something.
    /// </summary>
    private static void WriteDriftEvent(string modDir)
    {
        string text =
            $$"""
              namespace = gen_struggle

              # Ticks the passage-of-time catalyst into every generated struggle, once a year, and
              # queues itself again. Started from each struggle's on_start.
              {{DriftEvent}} = {
              	hidden = yes
              	scope = struggle

              	immediate = {
              		if = {
              			limit = { phase_has_catalyst = {{StruggleMap.DriftCatalyst}} }
              			activate_struggle_catalyst = {{StruggleMap.DriftCatalyst}}
              		}

              		trigger_event = {
              			id = {{DriftEvent}}
              			years = 1
              		}
              	}
              }

              """;

        string dir = Path.Combine(modDir, "events");
        Directory.CreateDirectory(dir);
        ParadoxText.WriteBom(Path.Combine(dir, "gen_struggle_events.txt"), text);
    }

    /// <summary>
    /// Gives every phase the icon the struggle window looks for.
    ///
    /// The path is derived from the phase key — <c>gfx/interface/icons/struggle_types/&lt;key&gt;.dds</c>
    /// — and is not declarable, so a generated key means a file that has to exist under a name only
    /// this run knows. Vanilla's four Iberian phase icons are copied under our names, which works
    /// because <see cref="StruggleMood"/> is Iberia's wheel: each mood has exactly one vanilla
    /// counterpart, and the art for "hostility" is the right art for Bloodshed by construction.
    ///
    /// Base-game files, not DLC ones. They sit in the game's own gfx tree rather than inside
    /// dlc005_fp2, so they are present for anyone who owns CK3 at all.
    /// </summary>
    private static void WritePhaseIcons(string modDir, string gameDir, StruggleMap struggles)
    {
        string source = Path.Combine(gameDir, "gfx", "interface", "icons", "struggle_types");
        if (!Directory.Exists(source)) return;

        string dir = Path.Combine(modDir, "gfx", "interface", "icons", "struggle_types");
        Directory.CreateDirectory(dir);

        int copied = 0;
        foreach (var phase in struggles.Struggles.SelectMany(s => s.Phases))
        {
            string from = Path.Combine(source, $"{VanillaIcon(phase.Mood)}.dds");
            if (!File.Exists(from)) continue;

            File.Copy(from, Path.Combine(dir, $"{phase.Key}.dds"), overwrite: true);
            copied++;
        }

        if (copied > 0) Console.WriteLine($"  struggle phase icons: {copied} copied from vanilla");
    }

    /// <summary>
    /// The narrow decorative strip beside a struggle's name, which is keyed on the struggle rather
    /// than the phase: <c>struggle_header_backgrounds/&lt;struggle key&gt;.dds</c>.
    ///
    /// Copied from vanilla rather than generated. It is 64x256 with an alpha channel — an ornament,
    /// not a picture — and a map crop at that shape would be an unreadable sliver. Unlike the phase
    /// background there is no <c>_default</c> beside vanilla's two, so a missing one has nothing to
    /// fall back to; this is cheap insurance against the magenta that would imply.
    /// </summary>
    private static void WriteHeaderBackgrounds(string modDir, string gameDir, StruggleMap struggles)
    {
        string from = Path.Combine(gameDir, "gfx", "interface", "illustrations",
            "struggle_header_backgrounds", "iberian_struggle.dds");
        if (!File.Exists(from)) return;

        string dir = Path.Combine(modDir, "gfx", "interface", "illustrations", "struggle_header_backgrounds");
        Directory.CreateDirectory(dir);

        foreach (var s in struggles.Struggles)
            File.Copy(from, Path.Combine(dir, $"{s.Key}.dds"), overwrite: true);
    }

    private static string VanillaIcon(StruggleMood mood) => mood switch
    {
        StruggleMood.Bloodshed => "struggle_iberia_phase_hostility",
        StruggleMood.Ambition => "struggle_iberia_phase_opportunity",
        StruggleMood.Accommodation => "struggle_iberia_phase_compromise",
        _ => "struggle_iberia_phase_conciliation",
    };

    /// <summary>
    /// Names and prose.
    ///
    /// Short, because vanilla already localises the expensive half: every phase parameter has a
    /// <c>struggle_parameter_&lt;name&gt;</c> line and the four effect-group headings are vanilla
    /// keys, so the tooltips that explain what a phase actually *does* are written for us. What is
    /// left is the four things nobody else can know — the struggle's name, its description, and a
    /// name and description per phase.
    /// </summary>
    private static void WriteLocalisation(string modDir, StruggleMap struggles)
    {
        var sb = new StringBuilder();
        sb.Append("l_english:\n");

        foreach (var s in struggles.Struggles)
        {
            sb.Append($" {s.Key}:0 \"{ParadoxText.Loc(s.Name)}\"\n");
            sb.Append($" {s.Key}_desc:0 \"{ParadoxText.Loc(s.Description)}\"\n");

            foreach (var phase in s.Phases)
            {
                sb.Append($" {phase.Key}:0 \"{ParadoxText.Loc(phase.Name)}\"\n");
                sb.Append($" {phase.Key}_desc:0 \"{ParadoxText.Loc(phase.Description)}\"\n");
            }

            foreach (var ending in s.Endings)
            {
                sb.Append($" {ending.Key}:0 \"{ParadoxText.Loc(ending.Name)}\"\n");
                sb.Append($" {ending.Key}_desc:0 \"{ParadoxText.Loc(ending.Description)}\"\n");
                sb.Append($" {ending.Key}_tooltip:0 \"{ParadoxText.Loc(ending.Tooltip)}\"\n");

                // The text on the button that actually takes the decision. Derived by convention
                // from the decision key rather than declared, so its absence is a warning rather
                // than an error and the button would simply render its own key.
                // Names generated by MapGen.StruggleMap.Name all open with a capitalised "The",
                // which is right on a title bar and wrong in the middle of a sentence.
                string named = s.Name.StartsWith("The ", StringComparison.Ordinal)
                    ? "the " + s.Name[4..]
                    : s.Name;

                sb.Append($" {ending.Key}_confirm:0 \"{ParadoxText.Loc($"End {named}")}\"\n");

                // One line per custom_tooltip the decision wraps a condition in. Driven off the
                // same list the decision iterates, so a condition can never be emitted without the
                // sentence that explains it.
                foreach (string condition in StruggleMap.Conditions(ending.Kind))
                {
                    string prose = ending.Conditions.TryGetValue(condition, out string? text)
                        ? text
                        : condition;

                    sb.Append($" {ending.Key}_{condition}_tt:0 \"{ParadoxText.Loc(prose)}\"\n");
                }
            }

            sb.Append('\n');
        }

        // The reward modifiers, named once. They carry no struggle in their keys, so they are
        // written outside the per-struggle loop for the same reason their definitions are.
        sb.Append($" {StruggleMap.Modifier(StruggleEnding.Dominance)}:0 \"Master of the Struggle\"\n");
        sb.Append($" {StruggleMap.Modifier(StruggleEnding.StatusQuo)}:0 \"Keeper of the Peace\"\n");
        sb.Append($" {StruggleMap.Modifier(StruggleEnding.Concord)}:0 \"Reconciler of Peoples\"\n");
        sb.Append($" {StruggleMap.PeaceDividend}:0 \"The Struggle Is Over\"\n");

        string dir = Path.Combine(modDir, "localization", "english");
        Directory.CreateDirectory(dir);
        ParadoxText.WriteBom(Path.Combine(dir, "gen_struggles_l_english.yml"), sb.ToString());
    }
}
