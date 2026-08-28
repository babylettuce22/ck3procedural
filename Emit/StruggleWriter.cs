using Ck3MapGen.Config;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// Writes the files a generated struggle needs, and nothing else.
///
/// The reason a struggle is worth generating at all is that CK3 already contains the machinery to
/// run one and does not care whose struggle it is. Vanilla's base-game on_actions fire catalysts
/// with <c>every_character_struggle = { limit = { phase_has_catalyst = X } ... }</c> — into any
/// struggle the actor belongs to — and ~53 phase parameters are read by base-game script through
/// <c>has_struggle_phase_parameter</c>. So the whole of the progression and most of the mechanical
/// payload arrives for free, and what has to be written is only the declaration: who is in it,
/// where it is, and what each mood does.
///
/// None of those files is gated on a DLC. Vanilla's own struggles are — the Iberian one checks
/// <c>has_dlc_feature = the_fate_of_iberia</c> before starting and its ending decisions check
/// <c>has_fp2_dlc_trigger</c> — but the framework underneath is base game: <c>common/struggle</c>,
/// its schema docs and all seven struggle/situation .gui files ship in the base install with no
/// gating on them. A generated struggle simply omits the checks.
///
/// Four ending decisions are written per struggle: three settlements between its members, listed
/// in every phase's <c>ending_decisions</c>, and the outsider's foothold, which is not. See
/// <see cref="WriteEndingDecisions"/>.
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
    /// resolving, but the members it gives them serve other masters — one arbitrary county each,
    /// or, for the graphical ones, whatever provinces share an architecture style. None of those
    /// keys describes a *place* on this map, and a struggle pointed at one would cover somewhere
    /// arbitrary.
    ///
    /// Written into the same directory, which is safe because the filename is ours: that directory
    /// is blanked from vanilla's filenames and then rewritten under those same filenames, so a name
    /// vanilla never used cannot be clobbered by either pass.
    /// </summary>
    private static void WriteRegions(string modDir, StruggleMap struggles)
    {
        var b = new JominiBuilder();
        b.Comment("""
                  Regions for the generated struggles. Separate keys from the re-declared
                  vanilla ones, which carry placeholder members and describe nowhere.
                  """);
        b.Blank();

        foreach (var s in struggles.Struggles)
        {
            b.Comment(s.Name);

            using (b.Block(s.RegionKey))
            using (b.Block("duchies"))
                foreach (var duchy in s.Duchies) b.Token(duchy.Key);

            b.Blank();
        }

        string dir = Path.Combine(modDir, "map_data", "geographical_regions");
        Directory.CreateDirectory(dir);
        ParadoxText.WriteBom(Path.Combine(dir, "zz_gen_struggle_regions.txt"), b.ToString());
    }

    /// <summary>
    /// The struggles themselves.
    /// </summary>
    private static void WriteDefinition(string modDir, StruggleMap struggles)
    {
        var b = new JominiBuilder();
        b.Comment("""
                  Generated struggles.
                  Catalysts and phase parameters are vanilla keys, chosen because base-game
                  script fires and reads them generically for any struggle. See MapGen/Struggles.cs.
                  """);
        b.Blank();

        foreach (var s in struggles.Struggles)
        {
            using (b.Block(s.Key))
            {
                b.Quoted("illustration", s.Illustration);
                b.Blank();

                using (b.Block("cultures"))
                    foreach (var c in s.Cultures) b.Token(c.Key);

                using (b.Block("faiths"))
                    foreach (var f in s.Faiths) b.Token(f.Key);

                b.Inline("regions", s.RegionKey);
                b.Blank();

                // Vanilla's own figure. A culture is pulled in when this share of its counties lies
                // inside the region, which for generated cultures is nearly always all of them --
                // they are built regionally in the first place -- so the number mostly decides whether
                // a culture that spills over the border counts as living here or visiting.
                b.Field("involvement_prerequisite_percentage", "0.8");
                b.Inline("transition_state_duration", "months = 3");
                b.Blank();

                // The drift ticker. Vanilla starts its equivalent from the struggle's own on_start and
                // the event re-queues itself yearly; ours does the same rather than calling vanilla's
                // neutral_struggle.0001, which also runs Persian and Great Pact logic we have no part in.
                using (b.Block("on_start")) b.Field("trigger_event", DriftEvent);
                b.Blank();

                b.Field("start_phase", s.PhaseFor(s.StartMood).Key);
                b.Blank();

                using (b.Block("phase_list"))
                    foreach (var phase in s.Phases) WritePhase(b, s, phase);
            }

            b.Blank();
        }

        string dir = Path.Combine(modDir, "common", "struggle", "struggles");
        Directory.CreateDirectory(dir);
        ParadoxText.WriteBom(Path.Combine(dir, "01_gen_struggles.txt"), b.ToString());
    }

    private static void WritePhase(JominiBuilder b, GeneratedStruggle s, StrugglePhase phase)
    {
        using (b.Block(phase.Key))
        {
            using (b.Block("future_phases"))
                foreach (var (mood, catalysts) in phase.Futures)
                using (b.Block(s.PhaseFor(mood).Key))
                using (b.Block("catalysts"))
                    foreach (var (catalyst, weight) in catalysts) b.Field(catalyst, weight);

            b.Blank();

            // One block per group that has anything in it. An empty war_effects block parses but
            // renders as a heading with nothing under it, which reads as a bug in the struggle window.
            foreach (string group in Groups)
            {
                var parameters = phase.Parameters.Where(p => p.Group == group).ToList();
                var modifiers = phase.Modifiers.Where(m => m.Group == group).ToList();
                if (parameters.Count == 0 && modifiers.Count == 0) continue;

                using (b.Block(BlockName(group)))
                {
                    b.Field("name", group);

                    foreach (string audience in Audiences)
                    {
                        var mine = parameters.Where(p => p.Audience == audience).ToList();
                        if (mine.Count == 0) continue;

                        using (b.Block($"{audience}_parameters"))
                            foreach (var (_, _, parameter) in mine) b.Field(parameter, "yes");
                    }

                    foreach (var block in modifiers.Select(m => m.Block).Distinct())
                        using (b.Block(block))
                            foreach (var (_, _, key, value) in modifiers.Where(m => m.Block == block))
                                b.Field(key, value);
                }

                b.Blank();
            }

            // Every phase lists every settlement, which is what vanilla does and is not redundant: the
            // list is informational, and it is the only place a player can read what finishing this
            // thing would take before any of it is within reach. A phase that listed only its own
            // reachable ending would hide the other two exactly while the player is deciding which to
            // aim for.
            //
            // The foothold is not in the list, again following vanilla, which leaves its own outsider
            // ending out of all four Iberian phases. This panel belongs to the struggle's members, and
            // every reader of it is either involved or hoping to be; the one ending they are all
            // categorically barred from taking would be a goal listed for the wrong audience. The
            // outsider meets it as an ordinary decision instead, in a decision group named for the
            // struggle.
            using (b.Block("ending_decisions"))
                foreach (var ending in s.MemberEndings) b.Token(ending.Key);
        }

        b.Blank();
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
    /// <c>is_invisible = yes</c> is not a mistake: a *settlement* is never listed in the decisions
    /// panel. The struggle window reads the <c>ending_decisions</c> list off the current phase and
    /// renders its own buttons, which is the only place those three are ever seen or taken —
    /// vanilla's three work exactly this way.
    ///
    /// The foothold is the exception in every one of those respects, and has to be, because it is
    /// the ending for somebody the struggle window is not addressing. It is visible, it is filed
    /// under the base game's <c>struggle</c> decision group so it sorts with the big realm
    /// decisions, and its <c>is_shown</c> is the negation of the other three's: never a member,
    /// only somebody who already holds ground in the region. Vanilla's
    /// <c>secure_iberian_foothold_decision</c> is built the same way and for the same reason.
    ///
    /// The struggle is ended in the decision's own effect rather than through a coda event. Vanilla
    /// routes through a fullscreen event so it can offer the ender a choice of reward, and that
    /// event needs its own background art and its own option tree; none of that is reachable for a
    /// generated struggle, and <c>end_struggle</c> works the same from either place.
    /// </summary>
    private static void WriteEndingDecisions(string modDir, StruggleMap struggles)
    {
        var b = new JominiBuilder();
        b.Comment("""
                  Ending decisions for the generated struggles.
                  Invisible by design: the struggle window renders these from the current
                  phase's ending_decisions list, and they appear nowhere else.
                  """);
        b.Blank();

        foreach (var s in struggles.Struggles)
        {
            foreach (var ending in s.Endings)
            {
                bool outsider = ending.Kind == StruggleEnding.Foothold;

                using (b.Block(ending.Key))
                {
                    b.Field("decision_group_type", outsider ? "struggle" : "major");
                    b.Field("title", ending.Key);
                    b.Inline("picture", $"reference = \"{Picture(ending.Kind)}\"");

                    // The struggle-window button art, and so nothing at all for the one ending that
                    // is never drawn in the struggle window.
                    if (ExtraPicture(ending.Kind) is { } extra) b.Quoted("extra_picture", extra);

                    b.Field("desc", $"{ending.Key}_desc");
                    b.Field("selection_tooltip", $"{ending.Key}_tooltip");
                    if (!outsider) b.Field("is_invisible", "yes");
                    b.Field("sort_order", "80");
                    b.Inline("cooldown", "days = 1");
                    b.Blank();

                    // Landless adventurers are excluded the way vanilla excludes them: they hold no
                    // county in the region, so every territorial test below would be vacuous for them.
                    using (b.Block("is_shown"))
                    {
                        b.Field("is_landless_adventurer", "no");
                        b.Field("exists", $"struggle:{s.Key}");

                        if (outsider)
                        {
                            // Uninvolved or interloper, which is the same thing said the only way
                            // the script has of saying it: involvement is a property of belonging
                            // to the struggle, so "not a member" is a NOT around the membership
                            // test rather than an involvement value of its own.
                            using (b.Block("NOT"))
                            using (b.Block("any_character_struggle"))
                            {
                                b.Field("involvement", "involved");
                                b.Field("is_struggle_type", s.Key);
                            }

                            // Cheap first, then this: unlike the settlements, this decision is
                            // visible, so its is_shown is evaluated for every ruler in the world
                            // rather than only for the struggle's members. One county in the region
                            // is the least that can make it any of your business.
                            using (b.Block("any_county_in_region"))
                            {
                                b.Field("region", s.RegionKey);
                                b.Field("holder.top_liege", "root");
                            }
                        }
                        else
                        {
                            using (b.Block("any_character_struggle"))
                            {
                                b.Field("involvement", "involved");
                                b.Field("is_struggle_type", s.Key);
                            }
                        }
                    }

                    b.Blank();

                    using (b.Block("is_valid"))
                    {
                        // Independent rulers only. A vassal ending the struggle their liege is fighting
                        // would settle a question that was never theirs to settle.
                        b.Field("top_liege", "this");
                        b.Field("exists", $"struggle:{s.Key}");
                        b.Token($"prestige_level >= {PrestigeLevel(ending.Kind)}");
                        b.Blank();

                        // Wrapped rather than bare so the window shows a sentence instead of the trigger.
                        // An unwrapped nest of any_involved_ruler blocks renders as a wall of scope names
                        // that tells the player nothing about what to actually do. The sentence itself is
                        // written by WriteLocalisation against the same condition name.
                        foreach (string condition in StruggleMap.Conditions(ending.Kind))
                        {
                            using (b.Block("custom_tooltip"))
                            {
                                b.Field("text", $"{ending.Key}_{condition}_tt");

                                // Already indented to this depth by Condition; see its doc comment.
                                b.Raw(Condition(condition, ending.Kind, s));
                            }

                            b.Blank();
                        }
                    }

                    b.Blank();

                    using (b.Block("effect"))
                    {
                        b.Raw(Reward(ending, s));
                        b.Inline($"struggle:{s.Key}", $"end_struggle = {ending.Key}");
                    }

                    b.Blank();

                    b.Field("cost", "{}");
                    b.Blank();

                    // Vanilla's cadence. Below kingdom the check never runs — a count who somehow
                    // qualified would be ending a struggle they cannot police — and above it the AI
                    // reconsiders twice a year rather than constantly, because the tests walk every
                    // involved ruler and every county in the region.
                    using (b.Block("ai_check_interval_by_tier"))
                    {
                        b.Field("barony", "0");
                        b.Field("county", "0");
                        b.Field("duchy", "0");
                        b.Field("kingdom", "120");
                        b.Field("empire", "120");
                        b.Field("hegemony", "120");
                    }

                    b.Blank();

                    b.Inline("ai_potential", "always = yes");
                    b.Inline("ai_will_do", "base = 100");
                }

                b.Blank();
            }
        }

        string dir = Path.Combine(modDir, "common", "decisions");
        Directory.CreateDirectory(dir);
        ParadoxText.WriteBom(Path.Combine(dir, "01_gen_struggle_endings.txt"), b.ToString());
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

            ("phase", StruggleEnding.Concord) =>
                $"\t\t\tstruggle:{s.Key} = {{ is_struggle_phase = {s.PhaseFor(StruggleMood.Concord).Key} }}\n",

            // Either of the two hostile moods, which is where vanilla puts the parameter that
            // unlocks its own foothold. An outsider is welcome to take the region while its peoples
            // are busy with each other; walking in on a region that has settled down is not a
            // conquest the mechanic has any interest in blessing.
            ("phase", _) =>
                "\t\t\tOR = {\n"
                + $"\t\t\t\tstruggle:{s.Key} = {{ is_struggle_phase = {s.PhaseFor(StruggleMood.Bloodshed).Key} }}\n"
                + $"\t\t\t\tstruggle:{s.Key} = {{ is_struggle_phase = {s.PhaseFor(StruggleMood.Ambition).Key} }}\n"
                + "\t\t\t}\n",

            ("hold", StruggleEnding.Dominance) => Held(s, ">=", "0.5"),

            // Vanilla's outsider bar, and deliberately the lowest of the four: an outsider who had
            // to take half the region would be in a position to take the other half and end it as
            // dominance instead, which is a different ending for a different kind of ruler.
            ("hold", StruggleEnding.Foothold) => Held(s, ">=", StruggleMap.FootholdShare),

            // The two settlements require the opposite of dominance: you have to *not* have won.
            ("hold", _) => Held(s, "<", "0.5"),

            ("rivals", _) => NoRivalHolds(s, kind == StruggleEnding.Dominance ? "0.25" : "0.5"),

            ("peace", _) => NoInvolvedWar(s),

            ("outsider", _) => SeatedElsewhere(s),

            ("seat", _) => LongHeldDuchy(s),

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

    /// <summary>
    /// Your capital is not in the region.
    ///
    /// The line between an outsider and a local, and the reason it is drawn at the capital rather
    /// than at involvement: involvement can lapse — a ruler who loses their last county in the
    /// region stops being involved — while where a dynasty actually sits does not. Without it, a
    /// local who had been squeezed out of the struggle could come back and finish it as a foreigner.
    /// </summary>
    private static string SeatedElsewhere(GeneratedStruggle s) =>
        "\t\t\tNOT = {\n"
        + "\t\t\t\tcapital_province = {\n"
        + $"\t\t\t\t\tgeographical_region = {s.RegionKey}\n"
        + "\t\t\t\t}\n"
        + "\t\t\t}\n";

    /// <summary>
    /// A whole duchy of the region, held long enough to have kept it.
    ///
    /// Vanilla asks for a de jure kingdom of Iberia, completely controlled and held fifteen years;
    /// one tier down is the same test here, because our region *is* a kingdom and its duchies are
    /// what it is divided into. The point of the clause either way is that a third of the region
    /// taken as scattered counties is a raid with a long tail, while a duchy nobody has been able
    /// to take back for fifteen years is a government.
    ///
    /// <c>de_jure_liege</c> rather than a region test on the duchy: the region is built from the
    /// seed kingdom's settled duchies, so a duchy whose de jure liege is that kingdom and which is
    /// not in the region is one with no settled county in it at all — nothing anybody can hold, let
    /// alone completely control.
    /// </summary>
    private static string LongHeldDuchy(GeneratedStruggle s) =>
        "\t\t\tany_held_title = {\n"
        + "\t\t\t\ttier = tier_duchy\n"
        + $"\t\t\t\tde_jure_liege = title:{s.Seed.Key}\n"
        + "\t\t\t\troot = { completely_controls = prev }\n"
        + $"\t\t\t\ttitle_held_years >= {StruggleMap.FootholdYears}\n"
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
    /// Four things, in ascending order of how long they last: a one-off in prestige and renown, a
    /// per-outcome one-off in the currency that outcome was won in, a permanent character modifier,
    /// and a stretch of years on the counties of the region — <see cref="StruggleMap.Aftermath"/>
    /// on the ender's own ground and <see cref="StruggleMap.Settlement"/> on everybody else's.
    ///
    /// The last pair is the only part that marks the map rather than the man, and it is why the
    /// four endings are distinguishable after the fact: a region held down by one crown, a region
    /// that agreed to stop pushing its border, a region that stopped counting itself as two
    /// peoples and a region under a foreigner's garrison do not look alike, and until this was
    /// split per outcome they all wore the same "the struggle is over" county modifier.
    /// </summary>
    private static string Reward(StruggleEndingDef ending, GeneratedStruggle s)
    {
        // Depth two: this is spliced into the decision's effect block, which sits one level inside
        // the decision itself.
        var b = new JominiBuilder(startDepth: 2);

        b.Field("add_prestige_level", "1");

        switch (ending.Kind)
        {
            // Dread, because a region that watched one claimant outlast all the others has learned
            // something about him that outlives the struggle it learned it in.
            case StruggleEnding.Dominance:
                b.Inline("dynasty", "add_dynasty_prestige = 5000");
                b.Field("add_dread", "25");
                break;

            // The one settlement nobody had to be beaten into, and the only one that ends with the
            // ender's own house materially better off rather than merely more respected: the levies
            // that were standing against the border go home and the tax comes in.
            case StruggleEnding.StatusQuo:
                b.Inline("dynasty", "add_dynasty_prestige = 3000");
                b.Field("add_gold", "500");
                break;

            case StruggleEnding.Concord:
                b.Inline("dynasty", "add_dynasty_prestige = 3000");
                b.Field("add_piety_level", "1");
                break;

            // Paid as dominance is paid, because it is dominance done from outside — and in renown
            // and dread rather than piety, since nothing about walking into somebody else's quarrel
            // and ending it by force recommends itself to anybody's god.
            default:
                b.Inline("dynasty", "add_dynasty_prestige = 5000");
                b.Field("add_dread", "25");
                break;
        }

        b.Inline("add_character_modifier", $"modifier = {ending.Modifier}");

        string years = StruggleMap.AftermathYears(ending.Kind).ToString();

        // Two passes over the same region, split on whether the county answers to the ender. The
        // `exists = holder` guard is not decoration: a region can contain an unheld county, and
        // reading `holder.top_liege` on one logs an error every time either limit is evaluated.
        using (b.Block("every_county_in_region"))
        {
            b.Field("region", s.RegionKey);
            b.Inline("limit", "exists = holder holder.top_liege = root");

            using (b.Block("add_county_modifier"))
            {
                b.Field("modifier", StruggleMap.Aftermath(ending.Kind));
                b.Field("years", years);
            }
        }

        using (b.Block("every_county_in_region"))
        {
            b.Field("region", s.RegionKey);
            b.Inline("limit", "exists = holder NOT = { holder.top_liege = root }");

            using (b.Block("add_county_modifier"))
            {
                b.Field("modifier", StruggleMap.Settlement(ending.Kind));
                b.Field("years", years);
            }
        }

        return b.ToString();
    }

    /// <summary>
    /// The reward modifiers, written once however many struggles there are.
    ///
    /// Their keys carry no struggle in them on purpose: the reward for mastering a region is the
    /// same reward whichever region it was, and giving each struggle its own copy would multiply
    /// the definitions without changing a single number in them. They are keyed by *outcome*
    /// instead, and that is the axis the variety lives on — twelve definitions, three per ending:
    /// what the ender keeps, what their own counties become, and what the rest of the region
    /// becomes. No two endings share any of the three.
    ///
    /// Each character modifier is built to be recognisable without reading the numbers: dominance
    /// rules by weight and fear, the status quo pays in coin and defensibility, concord in piety
    /// and reach, and the foothold in garrisons on ground that is not yours. The county pairs
    /// follow the same reading — see <see cref="StruggleMap.Aftermath"/>.
    /// </summary>
    private static void WriteEndingModifiers(string modDir)
    {
        string text =
            $$"""
              # Rewards for ending a generated struggle. Keyed by outcome, not by struggle:
              # mastering one region is worth what mastering another is. Three per outcome —
              # what the ender keeps, what their counties become, what the region becomes.

              # =========================================================================
              # What the ender keeps. Permanent.
              # =========================================================================

              # Won by outlasting every other claimant, and the region knows it.
              {{StruggleMap.Modifier(StruggleEnding.Dominance)}} = {
              	icon = martial_positive

              	same_culture_opinion = 10
              	different_culture_opinion = -5
              	monthly_county_control_growth_add = 0.5
              	monthly_prestige_gain_mult = 0.15
              	dread_baseline_add = 10
              	knight_effectiveness_mult = 0.1
              }

              # The only settlement nobody had to be beaten into. It pays in the things a ruler who
              # has stopped spending on a border gets back: coin, standing armies he can afford,
              # and ground that is expensive to attack.
              {{StruggleMap.Modifier(StruggleEnding.StatusQuo)}} = {
              	icon = stewardship_positive

              	vassal_opinion = 10
              	monthly_prestige_gain_mult = 0.15
              	defender_advantage = 10
              	monthly_income_mult = 0.1
              	army_maintenance_mult = -0.1
              	stewardship = 2
              }

              # Made the peoples of a region into one another's kin, which is a reputation that
              # travels further than the region does.
              {{StruggleMap.Modifier(StruggleEnding.Concord)}} = {
              	icon = diplomacy_positive

              	different_faith_opinion = 15
              	different_culture_opinion = 15
              	monthly_piety_gain_mult = 0.15
              	diplomacy = 2
              	diplomatic_range_mult = 0.25
              	enemy_hostile_scheme_success_chance_add = -10
              }

              # The outsider's. Everything it grants is about holding ground that is not yours by
              # any right the locals recognise, which is the position ending a struggle from
              # outside leaves you in.
              {{StruggleMap.Modifier(StruggleEnding.Foothold)}} = {
              	icon = martial_positive

              	monthly_county_control_growth_add = 0.5
              	different_culture_opinion = -10
              	garrison_size = 0.15
              	monthly_prestige_gain_mult = 0.15
              	dread_baseline_add = 10
              	levy_reinforcement_rate = 0.15
              }

              # =========================================================================
              # What the ender's own counties in the region become.
              # =========================================================================

              # Held, not loved. Garrisoned ground that answers quickly and resents it.
              {{StruggleMap.Aftermath(StruggleEnding.Dominance)}} = {
              	icon = martial_positive

              	monthly_county_control_growth_add = 0.5
              	levy_size = 0.15
              	garrison_size = 0.15
              	county_opinion_add = -5
              }

              # A border nobody is pushing any more is a border you can build behind.
              {{StruggleMap.Aftermath(StruggleEnding.StatusQuo)}} = {
              	icon = county_modifier_development_positive

              	development_growth_factor = 0.25
              	county_opinion_add = 10
              	defender_holding_advantage = 10
              	hostile_raid_time = 0.5
              }

              # The only one that is unambiguously good for the people living on it.
              {{StruggleMap.Aftermath(StruggleEnding.Concord)}} = {
              	icon = county_modifier_opinion_positive

              	development_growth_factor = 0.4
              	county_opinion_add = 15
              	tax_mult = 0.1
              	build_speed = -0.1
              }

              # An occupied third of somebody else's region. It holds because it is held.
              {{StruggleMap.Aftermath(StruggleEnding.Foothold)}} = {
              	icon = martial_mixed

              	monthly_county_control_growth_add = 0.5
              	garrison_size = 0.25
              	county_opinion_add = -15
              	levy_size = -0.1
              }

              # =========================================================================
              # What the rest of the region becomes — the counties the ender does not hold.
              # The neighbours still have to live in whatever was ended.
              # =========================================================================

              # The fighting stopped, which is worth something, under a crown that is not theirs,
              # which is worth rather less.
              {{StruggleMap.Settlement(StruggleEnding.Dominance)}} = {
              	icon = county_modifier_opinion_negative

              	development_growth_factor = 0.1
              	county_opinion_add = -10
              	hostile_raid_time = 0.25
              }

              # Nobody won, so nobody lost, and every county in the region gets the same quiet.
              {{StruggleMap.Settlement(StruggleEnding.StatusQuo)}} = {
              	icon = county_modifier_development_positive

              	development_growth_factor = 0.2
              	county_opinion_add = 5
              	hostile_raid_time = 0.5
              }

              # Concord is the one ending whose neighbours do about as well out of it as its author.
              {{StruggleMap.Settlement(StruggleEnding.Concord)}} = {
              	icon = county_modifier_development_positive

              	development_growth_factor = 0.3
              	county_opinion_add = 10
              	travel_danger = -10
              }

              # A foreigner settled the argument. The argument is settled.
              {{StruggleMap.Settlement(StruggleEnding.Foothold)}} = {
              	icon = county_modifier_opinion_negative

              	development_growth_factor = 0.1
              	county_opinion_add = -10
              	levy_size = -0.1
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
    private static string? ExtraPicture(StruggleEnding kind) => kind switch
    {
        StruggleEnding.Dominance => "gfx/interface/illustrations/struggle_decision_buttons/fp2_decision_hostility.dds",
        StruggleEnding.StatusQuo => "gfx/interface/illustrations/struggle_decision_buttons/fp2_decision_compromise.dds",
        StruggleEnding.Concord => "gfx/interface/illustrations/struggle_decision_buttons/fp2_decision_conciliation.dds",

        // The set exists to fill the buttons the struggle window draws, and the foothold is not one
        // of them. Vanilla's outsider ending declares no extra_picture either.
        _ => null,
    };

    private static string Picture(StruggleEnding kind) => kind switch
    {
        StruggleEnding.Dominance => "gfx/interface/illustrations/decisions/decision_found_kingdom.dds",
        StruggleEnding.StatusQuo => "gfx/interface/illustrations/decisions/decision_legitimacy.dds",
        StruggleEnding.Concord => "gfx/interface/illustrations/decisions/decision_golden_age.dds",

        // Vanilla's own choice for secure_iberian_foothold, and apt beyond that: it is the only
        // one of the four that is a goal rather than a reconciliation.
        _ => "gfx/interface/illustrations/decisions/decision_destiny_goal.dds",
    };

    /// <summary>
    /// How renowned the ender has to be.
    ///
    /// Dominance asks least, because its territorial bar is the hardest of the three and asking for
    /// both would make it the ending nobody reaches. The two settlements ask for Exalted among Men,
    /// which is vanilla's figure and is doing the work its territorial tests deliberately do not:
    /// they are the endings for a ruler who did *not* conquer the region, so something other than
    /// conquest has to have made them the one who gets to close it.
    ///
    /// The foothold asks what dominance asks, on the same reasoning: it is the other conquest, and
    /// its territorial clauses — a third of the region plus a duchy held fifteen years — are
    /// already a long campaign. Vanilla asks for no prestige at all there and demands an empire
    /// instead, which does not survive the move to a generated map: empires are rare on a small one
    /// and the ending would be reachable by at most a single ruler in the world, or nobody.
    /// </summary>
    private static string PrestigeLevel(StruggleEnding kind) => kind switch
    {
        StruggleEnding.Dominance or StruggleEnding.Foothold => "high_prestige_level",
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
        var b = new JominiBuilder();
        b.Comment("Starts the generated struggles on the bookmark date.");
        b.Blank();

        using (b.Block(cfg.StartDate))
        using (b.Block("effect"))
            foreach (var s in struggles.Struggles)
            {
                b.Comment(s.Name);

                using (b.Block("start_struggle"))
                {
                    b.Field("struggle_type", s.Key);
                    b.Field("start_phase", s.PhaseFor(s.StartMood).Key);
                }
            }

        string dir = Path.Combine(modDir, "history", "struggles");
        Directory.CreateDirectory(dir);
        ParadoxText.WriteBom(Path.Combine(dir, "01_gen_struggles.txt"), b.ToString());
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
        var loc = new LocFile();

        foreach (var s in struggles.Struggles)
        {
            loc.Add(s.Key, s.Name);
            loc.Add($"{s.Key}_desc", s.Description);

            foreach (var phase in s.Phases)
            {
                loc.Add(phase.Key, phase.Name);
                loc.Add($"{phase.Key}_desc", phase.Description);
            }

            foreach (var ending in s.Endings)
            {
                loc.Add(ending.Key, ending.Name);
                loc.Add($"{ending.Key}_desc", ending.Description);
                loc.Add($"{ending.Key}_tooltip", ending.Tooltip);

                // The text on the button that actually takes the decision. Derived by convention
                // from the decision key rather than declared, so its absence is a warning rather
                // than an error and the button would simply render its own key.
                loc.Add($"{ending.Key}_confirm", $"End {s.InSentence}");

                // One line per custom_tooltip the decision wraps a condition in. Driven off the
                // same list the decision iterates, so a condition can never be emitted without the
                // sentence that explains it.
                foreach (string condition in StruggleMap.Conditions(ending.Kind))
                {
                    string prose = ending.Conditions.TryGetValue(condition, out string? text)
                        ? text
                        : condition;

                    loc.Add($"{ending.Key}_{condition}_tt", prose);
                }
            }

            loc.Blank();
        }

        // The reward modifiers, named once. They carry no struggle in their keys, so they are
        // written outside the per-struggle loop for the same reason their definitions are.
        loc.Add(StruggleMap.Modifier(StruggleEnding.Dominance), "Master of the Struggle");
        loc.Add(StruggleMap.Modifier(StruggleEnding.StatusQuo), "Keeper of the Peace");
        loc.Add(StruggleMap.Modifier(StruggleEnding.Concord), "Reconciler of Peoples");
        loc.Add(StruggleMap.Modifier(StruggleEnding.Foothold), "Conqueror from Without");

        // The county pair. Named per outcome for the same reason they are defined per outcome: the
        // one thing a player sees on the map after a struggle closes should say which of the four
        // things happened, not merely that something did.
        loc.Add(StruggleMap.Aftermath(StruggleEnding.Dominance), "Held by One Crown");
        loc.Add(StruggleMap.Aftermath(StruggleEnding.StatusQuo), "Behind a Settled Border");
        loc.Add(StruggleMap.Aftermath(StruggleEnding.Concord), "One People Now");
        loc.Add(StruggleMap.Aftermath(StruggleEnding.Foothold), "Under Foreign Rule");

        loc.Add(StruggleMap.Settlement(StruggleEnding.Dominance), "Someone Else's Victory");
        loc.Add(StruggleMap.Settlement(StruggleEnding.StatusQuo), "Nobody Won");
        loc.Add(StruggleMap.Settlement(StruggleEnding.Concord), "Kin Across the Border");
        loc.Add(StruggleMap.Settlement(StruggleEnding.Foothold), "The Foreigner's Peace");

        loc.Write(Path.Combine(modDir, "localization", "english", "gen_struggles_l_english.yml"));
    }
}
