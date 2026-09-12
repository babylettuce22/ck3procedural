using System.Globalization;
using System.Text;
using Ck3MapGen.Config;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// The chronicle's second half: the one the running game writes.
///
/// <see cref="ChronicleWriter"/> files what a title is remembered for up to the bookmark, as one
/// static localisation key per title. Nothing in that path can change once the game is running,
/// so every remembered thing in the world happened before the player arrived. This writer gives
/// each title a small store of remembered things that script appends to during play — a county
/// falling to ruin, a struggle turning to bloodshed, a frontier closing — and gives the world one
/// more of the same, so the ancient generated entries and the player's own years read as one book.
///
/// ---- Why slots and not a list ----
///
/// A variable list holds scopes or values, one per element, and an entry here is four things: a
/// template, a year, and up to two scopes. There is no generic object in script to hang four
/// variables on that does not belong to somebody who can die, so each title carries a fixed number
/// of numbered slots — <c>chr_0_tmpl</c>, <c>chr_0_year</c>, <c>chr_0_actor</c>, <c>chr_0_other</c>
/// and so on — newest in slot 0, and a push shifts everything down one. Variable names cannot be
/// built at runtime, which would make that unbearable to write by hand; it is trivial for a writer
/// that already unrolls everything else, and the unrolled effect is a few hundred lines that never
/// change. See <see cref="TitleSlots"/> and <see cref="WorldSlots"/>.
///
/// ---- Why the prose is chosen in script and not in the window ----
///
/// The window asks each slot one question: <c>[Title.Custom('gen_chr_line_N')]</c>. That is a
/// <c>customizable_localization</c> entry of <c>type = landed_title</c>, which picks a
/// localisation key from the slot's template flag and falls through to an empty string when the
/// slot is unset — so the row's <c>visible</c> is the same <c>Not( StringIsEmpty( … ) )</c> the lore
/// button already uses, and the <c>.gui</c> reads no variable at all. Vanilla's own <c>gui/</c>
/// folder never calls <c>GetVariable</c>; the trap with a datafunction that does not exist is that
/// it renders nothing and reports nothing, and this shape keeps every fallible piece on the script
/// side where ck3-tiger and error.log both see it.
///
/// ---- What narrates ----
///
/// Three things today, each already a system with its own hook: a county falling to ruin, being
/// bought back from the brink, or being cleared and resettled (the Ruins and Wilderness sets); a
/// generated struggle changing phase (<see cref="StruggleWriter"/>); and a frontier of the Wilds
/// turning an era (<see cref="FrontierWriter"/>). The templates for the last two are written per
/// struggle and per frontier rather than once, because a line that can name <i>the Northern
/// Ostmark Wilds</i> is the whole difference between a chronicle and a log — and naming it costs
/// nothing here, where the names are known.
///
/// <code>
/// Related base files:
///   Ruins/common/scripted_effects/00_ruins_effects.txt           calls gen_chr_push_up_effect
///   Wilderness/events/wilderness_ruins_events.txt                calls gen_chr_push_up_effect
///
/// Related generated files, written elsewhere:
///   Emit/StruggleWriter.cs        on_change_phase and the drift ticker call the struggle check
///   Emit/FrontierWriter.cs        the phase announce effect calls gen_chr_wilds_effect
///   Emit/GuiWriter.cs             the title window's lore panel reads gen_chr_line_N
///   Emit/GuiWindows/ChronicleWindow.cs   the world window reads gen_chw_line_N
/// </code>
/// </summary>
public static class ChronicleRuntimeWriter
{
    /// <summary>
    /// Remembered things per title. Twelve is the panel's reading budget rather than a storage
    /// limit — <see cref="ChronicleWriter"/> makes the same judgement for the static half — and
    /// the oldest entry falls off the end when a thirteenth arrives.
    /// </summary>
    public const int TitleSlots = 12;

    /// <summary>
    /// Remembered things for the world. Larger because the world window is a feed as much as a
    /// history, and because the whole map narrates into it.
    /// </summary>
    public const int WorldSlots = 30;

    /// <summary>
    /// Where the world's entries live. A title, not a global variable: the loc datafunctions that
    /// read a variable back are <c>Title.MakeScope.Var(...)</c> and friends, and vanilla's own gui
    /// never reads a global. <c>h_china</c> is always declared — real when the map supports a
    /// hegemony, a shim when it does not (see <see cref="CompatibilityWriter"/>) — so the world
    /// window can name it with <c>GetTitleByKey</c> and be the title panel with a fixed datacontext.
    /// </summary>
    public const string WorldTitleKey = Titles.HegemonyKey;

    /// <summary>The custom-loc entry that renders a title's slot. The window's whole contract.</summary>
    public static string TitleLine(int slot) => $"gen_chr_line_{slot}";

    /// <summary>The custom-loc entry that renders a world slot.</summary>
    public static string WorldLine(int slot) => $"gen_chw_line_{slot}";

    /// <summary>Title scope. Pushes one entry onto this title alone.</summary>
    public const string PushEffect = "gen_chr_push_effect";

    /// <summary>Title scope. As <see cref="PushEffect"/> with no actor and no other party.</summary>
    public const string PushPlainEffect = "gen_chr_push_plain_effect";

    /// <summary>
    /// County scope. Pushes onto the county and every de jure liege up to the empire, with the
    /// county itself as the entry's other party, and onto the world when <c>WORLD = yes</c>.
    /// </summary>
    public const string PushUpEffect = "gen_chr_push_up_effect";

    /// <summary>
    /// Title scope, with <c>TMPL</c>, <c>ACTOR</c>, <c>OTHER</c> and <c>WORLD</c>. Pushes onto
    /// this title and every de jure liege up to the empire, and onto the world when asked.
    /// </summary>
    public const string PushChainEffect = "gen_chr_push_chain_effect";

    /// <summary>Any scope. Pushes one entry onto the world store.</summary>
    public const string PushWorldEffect = "gen_chr_push_world_effect";

    /// <summary>Any scope. As <see cref="PushWorldEffect"/> with no actor and no other party.</summary>
    public const string PushWorldPlainEffect = "gen_chr_push_world_plain_effect";

    /// <summary>Struggle scope. Narrates a phase change if the phase differs from the remembered one.</summary>
    public const string StruggleCheckEffect = "gen_chr_struggle_check_effect";

    /// <summary>The struggle variable holding the last phase that was narrated, as a flag.</summary>
    public const string StrugglePhaseVar = "gen_chr_phase";

    /// <summary>Situation sub-region scope, with <c>PHASE</c>. Narrates a frontier's era turning.</summary>
    public const string WildsEffect = "gen_chr_wilds_effect";

    /// <summary>The empty string every unset slot renders as.</summary>
    private const string EmptyKey = "gen_chr_none";

    /// <summary>
    /// Landed-title scope. What separates a crown from an office, and the one line that keeps the
    /// world's book from being ministry paperwork.
    ///
    /// The crown narrators used to judge news by tier alone, and the nine seats of the celestial
    /// ministry are landless <c>e_</c> titles — empire tier, so every one of them cleared the
    /// kingdom bar AND the "empires are world news" bar. Vanilla churns them by design: a ministry
    /// is created holding for the hegemon, handed on by <c>appointment</c>, and destroyed outright
    /// whenever a minister leaves with no appointment-succession heir
    /// (<c>fired_minister_position_effect</c> → <c>destroy_and_fire_minister_effect</c>). Measured
    /// on a nine-year save: 28 creations, 19 destructions and 7 other transfers across the nine
    /// seats — around six world-feed lines a year, which is more than everything else the world
    /// does put together. The feed read as "X raised the Ministry of Revenue from lesser crowns"
    /// over and over while conquests and foundings fell off the end of it.
    ///
    /// That churn is vanilla-normal, not a generated-map fault — see the landless-minister note —
    /// so the fix belongs here rather than in the ministry rules.
    ///
    /// <c>is_landless_type_title</c> is the right discriminator and not just a ministry blocklist:
    /// it also covers the noble-family titles and the vanilla titular shims of
    /// <see cref="CompatibilityWriter"/>, whose guard destroys them at game start and would
    /// otherwise open every world's book with a page of foreign kingdoms lapsing. A real crown
    /// owns land by definition; the generated hegemony has a capital and de jure children, so it
    /// is not caught.
    /// </summary>
    private const string OfficeGate = "is_landless_type_title = no";

    private const string TitlePrefix = "chr";
    private const string WorldPrefix = "chw";

    /// <summary>
    /// One kind of remembered thing.
    ///
    /// <see cref="Text"/> is prose with three holes — <c>{year}</c>, <c>{actor}</c>, <c>{other}</c>
    /// — that are filled with loc datafunctions reading the slot the line is written for, so one
    /// template becomes one localisation key per slot. <see cref="TextNoActor"/> is the line for an
    /// entry that had nobody to name; a template without one is only ever pushed with an actor.
    /// </summary>
    private sealed record Template(string Flag, string Text, string? TextNoActor = null);

    /// <summary>
    /// The lines that need nothing baked in: the entry's other party is a title, a faith or a
    /// culture named at runtime, so one template serves every world.
    ///
    /// The crown lines are the backbone of a living book — one per realm per generation — and
    /// they are the lines that decide how noisy the world feed is. Kingdom inheritances stay in
    /// the realm's own books; usurpations, conquests, foundings and anything at empire tier go to
    /// the world as well. See <see cref="WriteNarrators"/> for the policy in script.
    /// </summary>
    private static readonly Template[] FixedTemplates =
    [
        // The Ruins and Wilderness sets.
        new("gen_chr_ruined",
            "In {year}, {other} fell to ruin. {actor} was the last to hold it.",
            "In {year}, {other} fell to ruin, and nobody remembers who held it last."),
        new("gen_chr_reprieved",
            "In {year}, {actor} paid dearly to keep {other} from ruin."),
        new("gen_chr_reclaimed",
            "In {year}, {actor} cleared the stones of {other}, and people returned.",
            "In {year}, the stones of {other} were cleared, and people returned."),

        // Crowns changing hands, by how.
        new("gen_chr_crown_inherited", "In {year}, {actor} inherited {other}."),
        new("gen_chr_crown_usurped", "In {year}, {actor} usurped {other}."),
        new("gen_chr_crown_conquered", "In {year}, {actor} took {other} by force of arms."),
        new("gen_chr_crown_created", "In {year}, {actor} raised {other} from lesser crowns."),
        new("gen_chr_crown_elected", "In {year}, {actor} was elected to {other}."),
        new("gen_chr_crown_passed", "In {year}, {other} passed to {actor}."),
        new("gen_chr_crown_destroyed",
            "In {year}, {actor} let {other} lapse, and it was no more.",
            "In {year}, {other} lapsed, and was no more."),
        new("gen_chr_crowned", "In {year}, {actor} received the crown of {other}."),

        // Wars settled.
        new("gen_chr_war_won", "In {year}, {actor} won {other} by war."),
        new("gen_chr_war_held", "In {year}, {actor} held {other} against a war for it."),

        // New peoples and faiths, and great works.
        new("gen_chr_faith_founded", "In {year}, {actor} broke with the old ways and founded {faith}."),
        new("gen_chr_culture_founded", "In {year}, {actor} gave rise to a new people, {culture}."),
        new("gen_chr_wonder", "In {year}, {actor} finished the great work at {other}, begun by others long before."),

        // The Dynastic Cycle's phases, world news only. `other` is the hegemony title and `actor`
        // the hegemon, who may not exist — a throne in Division can stand empty — hence the pairs.
        new("gen_chr_cycle_stability",
            "In {year}, {other} settled into an age of stability under {actor}.",
            "In {year}, {other} settled into an age of stability."),
        new("gen_chr_cycle_expansion",
            "In {year}, {actor} turned {other} outward. An age of expansion began.",
            "In {year}, {other} turned outward. An age of expansion began."),
        new("gen_chr_cycle_advancement",
            "In {year}, {actor} turned {other} inward. An age of advancement began.",
            "In {year}, {other} turned inward. An age of advancement began."),
        new("gen_chr_cycle_tension",
            "In {year}, tension gathered in {other}, and the court of {actor} grew uneasy.",
            "In {year}, tension gathered in {other}."),
        new("gen_chr_cycle_conquest",
            "In {year}, {other} passed to a conqueror, and {actor} held its throne by the sword.",
            "In {year}, {other} passed to a conqueror."),
        new("gen_chr_cycle_division",
            "In {year}, {other} broke apart under {actor}. An age of division began.",
            "In {year}, {other} broke apart. An age of division began."),
    ];

    /// <summary>
    /// Vanilla's six Dynastic Cycle phases and the template each turns into. The situation is
    /// vanilla's own (<c>tgp_dynastic_cycle.txt</c>), so its phases cannot be given an
    /// <c>on_start</c> without overriding the file; the yearly check reads the phase instead.
    /// </summary>
    private static readonly (string Phase, string Flag)[] CyclePhases =
    [
        ("situation_dynastic_cycle_phase_stability", "gen_chr_cycle_stability"),
        ("situation_dynastic_cycle_phase_stability_expansion", "gen_chr_cycle_expansion"),
        ("situation_dynastic_cycle_phase_stability_advancement", "gen_chr_cycle_advancement"),
        ("situation_dynastic_cycle_phase_instability", "gen_chr_cycle_tension"),
        ("situation_dynastic_cycle_phase_instability_conquest", "gen_chr_cycle_conquest"),
        ("situation_dynastic_cycle_phase_chaos", "gen_chr_cycle_division"),
    ];

    public static void WriteAll(string modDir, MapConfig cfg, StruggleMap? struggles, FrontierMap frontier)
    {
        if (!cfg.EnableChronicle)
        {
            WriteStubs(modDir, struggles);
            return;
        }

        var templates = new List<Template>(FixedTemplates);
        templates.AddRange(StruggleTemplates(struggles));
        templates.AddRange(WildsTemplates(frontier));

        WriteEffects(modDir, cfg, struggles, frontier);
        WriteOnActions(modDir);
        WriteCustomLoc(modDir, templates);
        WriteLocalisation(modDir, templates);

        Console.WriteLine($"  chronicle (runtime): {templates.Count} line templates, "
                        + $"{TitleSlots} slots per title, {WorldSlots} for the world");
    }

    /// <summary>
    /// The chronicle switched off: every effect another system calls, defined and empty.
    ///
    /// The callers are not ours to gate. The Ruins and Wilderness sets are copied verbatim and
    /// call <see cref="PushUpEffect"/> on ruin, reprieve and reclamation; WonderWriter calls it on
    /// completion; FrontierWriter's era announcement calls <see cref="WildsEffect"/>; the struggle
    /// drift event and phase hook call <see cref="StruggleCheckEffect"/>; and every struggle
    /// ending decision calls its own <see cref="StruggleEndingEffect"/>. An undefined scripted
    /// effect is an error at every one of those call sites, so each name keeps a definition and
    /// the definition does nothing — vanilla ships empty effects the same way
    /// (<c>basic_invalidated_camp_officer_effect = {}</c>).
    ///
    /// Empty is not quite enough for the ones that take arguments. A call like
    /// <c>gen_chr_push_up_effect = { TMPL = … ACTOR = holder WORLD = yes }</c> against a body that
    /// names no <c>$TMPL$</c> is "this scripted effect does not need macro arguments" — tiger
    /// logged it at all 16 such call sites on the first try. So those stubs carry a branch that is
    /// never taken (<c>always = no</c>) and names each parameter the way the real effect does,
    /// which makes the arguments legal without running anything. The global variable it would
    /// set is only a vehicle for <c>$TMPL$</c>: a global works in every scope a caller might be in,
    /// including the situation sub-region the wilds effect is called from.
    ///
    /// Every public push name is stubbed, not only the ones called today, so a future caller
    /// written against the constants cannot break the off switch. What is NOT written: the
    /// on_actions (nothing narrates), the custom loc and the localisation (nothing reads them),
    /// so no slot variable is ever set on any title.
    /// </summary>
    internal static void WriteStubs(string modDir, StruggleMap? struggles)
    {
        // Each effect's parameters, exactly as its real definition takes them.
        var stubs = new List<(string Name, string[] Params)>
        {
            (PushEffect, ["TMPL", "ACTOR", "OTHER"]),
            (PushPlainEffect, ["TMPL"]),
            (PushUpEffect, ["TMPL", "ACTOR", "WORLD"]),
            (PushChainEffect, ["TMPL", "ACTOR", "OTHER", "WORLD"]),
            (PushWorldEffect, ["TMPL", "ACTOR", "OTHER"]),
            (PushWorldPlainEffect, ["TMPL"]),
            (StruggleCheckEffect, []),
            (WildsEffect, ["PHASE"]),
        };

        if (struggles is not null)
            foreach (var s in struggles.Struggles)
                foreach (var ending in s.Endings)
                    stubs.Add((StruggleEndingEffect(s, ending), []));

        var sb = new StringBuilder();
        sb.Append("""
            # The runtime chronicle is switched off (Chronicle, in the generator's General settings).
            # Written by Emit/ChronicleRuntimeWriter.cs.
            #
            # These are the effects other systems call -- ruins, wilderness, wonders, frontier eras,
            # struggle phases and endings -- kept defined so those calls stay valid, and left empty
            # so they narrate nothing.


            """);
        foreach (var (name, parameters) in stubs)
        {
            if (parameters.Length == 0)
            {
                sb.Append($"{name} = {{}}\n\n");
                continue;
            }

            // Never taken; here so the arguments the callers pass have somewhere to go.
            string world = parameters.Contains("WORLD") ? " always = $WORLD$" : "";
            sb.Append($"{name} = {{\n\tif = {{\n\t\tlimit = {{ always = no{world} }}\n");
            foreach (string param in parameters)
            {
                sb.Append(param switch
                {
                    "TMPL" => "\t\tset_global_variable = { name = gen_chr_off value = flag:$TMPL$ }\n",
                    "PHASE" => "\t\tset_global_variable = { name = gen_chr_off value = flag:gen_chr_$PHASE$ }\n",
                    "WORLD" => "",
                    _ => $"\t\t${param}$ = {{ save_temporary_scope_as = gen_chr_off }}\n",
                });
            }
            sb.Append("\t}\n}\n\n");
        }

        string dir = Path.Combine(modDir, "common", "scripted_effects");
        Directory.CreateDirectory(dir);
        ParadoxText.WriteBom(Path.Combine(dir, "zz_gen_chronicle_effects.txt"), sb.ToString());

        Console.WriteLine($"  chronicle (runtime): OFF — {stubs.Count} effects stubbed for their callers");
    }

    // ===========================================================================================
    // Templates
    // ===========================================================================================

    private static IEnumerable<Template> StruggleTemplates(StruggleMap? struggles)
    {
        if (struggles is null) yield break;

        foreach (var s in struggles.Struggles)
        {
            foreach (var ending in s.Endings)
                yield return new Template(StruggleEndingFlag(s, ending), EndingText(s, ending));

            foreach (var phase in s.Phases)
            {
            string name = ParadoxText.Loc(s.InSentence);
            string phaseName = ParadoxText.Loc(phase.Name);

            string text = phase.Mood switch
            {
                StruggleMood.Concord =>
                    $"In {{year}}, {name} entered a season of {phaseName}. Old enemies broke bread.",
                StruggleMood.Accommodation =>
                    $"In {{year}}, {name} settled into {phaseName}: a wary peace, after too much blood.",
                StruggleMood.Ambition =>
                    $"In {{year}}, {name} slid into {phaseName}, and the opportunists came out.",
                _ =>
                    $"In {{year}}, {name} fell into {phaseName}. There was open war in every valley.",
            };

            yield return new Template(StruggleFlag(s, phase), text);
            }
        }
    }

    private static string StruggleFlag(GeneratedStruggle s, StrugglePhase phase)
        => $"gen_chr_{s.Key}_{phase.Mood.ToString().ToLowerInvariant()}";

    /// <summary>
    /// The line for a struggle being settled, by how. The actor is whoever took the ending
    /// decision, and the struggle's own name is baked in — there is no "other" to name, so the
    /// slot carries the struggle scope itself, unused by the prose.
    /// </summary>
    private static string EndingText(GeneratedStruggle s, StruggleEndingDef ending)
    {
        string name = ParadoxText.Loc(s.InSentence);
        return ending.Kind switch
        {
            StruggleEnding.Dominance =>
                $"In {{year}}, {{actor}} ended {name} by mastering its ground. The others would live with it.",
            StruggleEnding.StatusQuo =>
                $"In {{year}}, {{actor}} ended {name}. The borders would stand where they were.",
            StruggleEnding.Concord =>
                $"In {{year}}, {{actor}} ended {name}. Its peoples stopped counting each other as strangers.",
            _ =>
                $"In {{year}}, {{actor}} came from outside and took the ground of {name} from under its quarrel.",
        };
    }

    private static string StruggleEndingFlag(GeneratedStruggle s, StruggleEndingDef ending)
        => $"gen_chr_{s.Key}_end_{ending.Kind.ToString().ToLowerInvariant()}";

    /// <summary>
    /// Character scope (the ruler taking the ending decision). Narrates the settlement into the
    /// struggle's titles and the world. <see cref="StruggleWriter"/> calls it from the decision's
    /// effect, before <c>end_struggle</c>, while the struggle scope still exists.
    /// </summary>
    public static string StruggleEndingEffect(GeneratedStruggle s, StruggleEndingDef ending)
        => $"{StruggleEndingFlag(s, ending)}_effect";

    /// <summary>
    /// One line per frontier per era. The era keys are <see cref="FrontierWriter"/>'s phase keys,
    /// passed through as <c>PHASE</c> by the announce effect, so the flag is built from them.
    /// </summary>
    private static IEnumerable<Template> WildsTemplates(FrontierMap frontier)
    {
        foreach (var s in frontier.SubRegions)
        {
            string name = ParadoxText.Loc(InSentence(s.Name));

            yield return new Template(WildsFlag(s, "wilds_pioneers"),
                $"In {{year}}, the first colonies took root in {name}. An age of pioneers began.");
            yield return new Template(WildsFlag(s, "wilds_closing"),
                $"In {{year}}, {name} stood more settled than wild. The frontier was closing.");
            yield return new Template(WildsFlag(s, "wilds_settled"),
                $"In {{year}}, the last wild county of {name} was claimed. The frontier was gone.");
            yield return new Template(WildsFlag(s, "wilds_untamed"),
                $"In {{year}}, the wild took back {name}. No colony stood in it.");
        }
    }

    private static string WildsFlag(FrontierSubRegion s, string phase) => $"gen_chr_{s.Key}_{phase}";

    /// <summary>"The Wilds" → "the Wilds"; "Ostmark Wilds" → "the Ostmark Wilds".</summary>
    private static string InSentence(string name)
        => name.StartsWith("The ", StringComparison.Ordinal) ? "the " + name[4..] : "the " + name;

    // ===========================================================================================
    // Effects
    // ===========================================================================================

    private static void WriteEffects(string modDir, MapConfig cfg, StruggleMap? struggles, FrontierMap frontier)
    {
        var sb = new StringBuilder();
        sb.Append("""
            # The runtime chronicle: a fixed number of remembered things per title, newest first,
            # and the same for the world on title:h_china. Written by Emit/ChronicleRuntimeWriter.cs.
            #
            # Every push shifts the slots down one and writes slot 0. Nothing here is clever; the
            # length comes from variable names not being buildable at runtime, so each slot is
            # spelled out. The window reads the slots back through customizable_localization
            # (gen_chr_line_N), never directly.
            #
            # ACTOR and OTHER are scope expressions valid where the effect is CALLED. The fan-out
            # effects save them as temporary scopes first, because `holder` means something
            # different on every rung of the liege chain.


            """);

        WritePush(sb, PushEffect, TitlePrefix, TitleSlots, withScopes: true);
        WritePush(sb, PushPlainEffect, TitlePrefix, TitleSlots, withScopes: false);
        WritePush(sb, "gen_chr_push_world_inner_effect", WorldPrefix, WorldSlots, withScopes: true);
        WritePush(sb, "gen_chr_push_world_plain_inner_effect", WorldPrefix, WorldSlots, withScopes: false);

        string liege = $"{PushEffect} = {{ TMPL = $TMPL$ ACTOR = scope:gen_chr_actor OTHER = scope:gen_chr_other }}";
        string climb = "exists = de_jure_liege  de_jure_liege = { NOT = { tier = tier_hegemony } }";

        sb.Append($$"""
            # Any scope. Captures the two parties before changing scope to the world title, so a
            # caller's `holder` or `root` still means what it meant at the call site.
            {{PushWorldEffect}} = {
            	if = {
            		limit = { exists = $ACTOR$ }
            		$ACTOR$ = { save_temporary_scope_as = gen_chr_actor }
            	}
            	if = {
            		limit = { exists = $OTHER$ }
            		$OTHER$ = { save_temporary_scope_as = gen_chr_other }
            	}
            	title:{{WorldTitleKey}} ?= {
            		gen_chr_push_world_inner_effect = { TMPL = $TMPL$ ACTOR = scope:gen_chr_actor OTHER = scope:gen_chr_other }
            	}
            }

            {{PushWorldPlainEffect}} = {
            	title:{{WorldTitleKey}} ?= {
            		gen_chr_push_world_plain_inner_effect = { TMPL = $TMPL$ }
            	}
            }

            # Title scope, reading scope:gen_chr_actor and scope:gen_chr_other, which the two
            # effects below it save. This title, then every de jure liege short of the hegemony —
            # that title is the world store, and the world gets its own push when WORLD = yes.
            gen_chr_push_chain_inner_effect = {
            	{{liege}}
            	if = {
            		limit = { {{climb}} }
            		de_jure_liege = {
            			{{liege}}
            			if = {
            				limit = { {{climb}} }
            				de_jure_liege = {
            					{{liege}}
            					if = {
            						limit = { {{climb}} }
            						de_jure_liege = {
            							{{liege}}
            						}
            					}
            				}
            			}
            		}
            	}

            	if = {
            		limit = { always = $WORLD$ }
            		title:{{WorldTitleKey}} ?= {
            			gen_chr_push_world_inner_effect = { TMPL = $TMPL$ ACTOR = scope:gen_chr_actor OTHER = scope:gen_chr_other }
            		}
            	}
            }

            # Title scope. TMPL, ACTOR, OTHER, WORLD. The two parties are saved before the climb
            # because `holder` means somebody else on every rung.
            {{PushChainEffect}} = {
            	if = {
            		limit = { exists = $ACTOR$ }
            		$ACTOR$ = { save_temporary_scope_as = gen_chr_actor }
            	}
            	if = {
            		limit = { exists = $OTHER$ }
            		$OTHER$ = { save_temporary_scope_as = gen_chr_other }
            	}
            	gen_chr_push_chain_inner_effect = { TMPL = $TMPL$ WORLD = $WORLD$ }
            }

            # County scope. TMPL, ACTOR, WORLD; the county is the entry's other party on every
            # rung, which is what lets a kingdom's panel say WHICH county fell.
            {{PushUpEffect}} = {
            	save_temporary_scope_as = gen_chr_other
            	if = {
            		limit = { exists = $ACTOR$ }
            		$ACTOR$ = { save_temporary_scope_as = gen_chr_actor }
            	}
            	gen_chr_push_chain_inner_effect = { TMPL = $TMPL$ WORLD = $WORLD$ }
            }


            """);

        WriteNarrators(sb, cfg);
        WriteStruggleCheck(sb, struggles);
        WriteStruggleEndings(sb, struggles);
        WriteCycleCheck(sb);
        WriteWilds(sb, cfg, frontier);

        string dir = Path.Combine(modDir, "common", "scripted_effects");
        Directory.CreateDirectory(dir);
        ParadoxText.WriteBom(Path.Combine(dir, "zz_gen_chronicle_effects.txt"), sb.ToString());
    }

    /// <summary>
    /// The shift-and-write. Every removal is guarded on <c>has_variable</c>: a bare
    /// <c>remove_variable</c> on an absent variable is harmless, but a bare <c>var:</c> read is
    /// not, and the guard is the same either way.
    /// </summary>
    private static void WritePush(StringBuilder sb, string name, string p, int slots, bool withScopes)
    {
        string[] parts = ["actor", "other"];

        sb.Append($"# Title scope. {slots} slots, prefix `{p}_`. TMPL{(withScopes ? ", ACTOR, OTHER" : " only")}.\n");
        sb.Append($"{name} = {{\n");

        for (int i = slots - 1; i >= 1; i--)
        {
            int from = i - 1;
            sb.Append($"\tif = {{\n\t\tlimit = {{ has_variable = {p}_{from}_tmpl }}\n");
            sb.Append($"\t\tset_variable = {{ name = {p}_{i}_tmpl value = var:{p}_{from}_tmpl }}\n");
            sb.Append($"\t\tset_variable = {{ name = {p}_{i}_year value = var:{p}_{from}_year }}\n");
            foreach (string part in parts)
            {
                sb.Append($"\t\tif = {{ limit = {{ has_variable = {p}_{from}_{part} }} "
                        + $"set_variable = {{ name = {p}_{i}_{part} value = var:{p}_{from}_{part} }} }}\n");
                sb.Append($"\t\telse_if = {{ limit = {{ has_variable = {p}_{i}_{part} }} remove_variable = {p}_{i}_{part} }}\n");
            }
            sb.Append("\t}\n");
            sb.Append($"\telse_if = {{\n\t\tlimit = {{ has_variable = {p}_{i}_tmpl }}\n");
            sb.Append($"\t\tremove_variable = {p}_{i}_tmpl\n");
            sb.Append($"\t\tremove_variable = {p}_{i}_year\n");
            foreach (string part in parts)
                sb.Append($"\t\tif = {{ limit = {{ has_variable = {p}_{i}_{part} }} remove_variable = {p}_{i}_{part} }}\n");
            sb.Append("\t}\n");
        }

        sb.Append('\n');
        sb.Append($"\tset_variable = {{ name = {p}_0_tmpl value = flag:$TMPL$ }}\n");
        sb.Append($"\tset_variable = {{ name = {p}_0_year value = current_year }}\n");
        foreach (string part in parts)
        {
            if (withScopes)
            {
                string macro = part == "actor" ? "$ACTOR$" : "$OTHER$";
                sb.Append($"\tif = {{ limit = {{ exists = {macro} }} set_variable = {{ name = {p}_0_{part} value = {macro} }} }}\n");
                sb.Append($"\telse_if = {{ limit = {{ has_variable = {p}_0_{part} }} remove_variable = {p}_0_{part} }}\n");
            }
            else
            {
                sb.Append($"\tif = {{ limit = {{ has_variable = {p}_0_{part} }} remove_variable = {p}_0_{part} }}\n");
            }
        }
        sb.Append("}\n\n");
    }

    /// <summary>
    /// The narrators that hang off vanilla on_actions, and the coronation sweep. Character or
    /// war scope as the on_action provides; see <see cref="WriteOnActions"/> for the hooks.
    ///
    /// ---- The noise policy, which is the only design decision in here ----
    ///
    /// Every kingdom-or-better title changing hands is a line in that realm's book and its
    /// empire's. The world's book gets it only when it is news to the world: an empire changing
    /// hands at all, or a kingdom changing hands by anything other than plain inheritance. Wars
    /// write one line each, for the foremost prize, and never reach the world: a crown taken is
    /// already on_title_gain's line, and a crown held is the realm's news alone. Coronations reach
    /// the world at empire tier. Without those cuts a dozen realms' successions and wars push a
    /// frontier closing off the end of the world feed inside a generation — which is exactly what
    /// the first version did.
    ///
    /// The wilderness dummies hold kingdom-tier titles and die like anyone else, so their
    /// successions would read as "the Wilds passed to Nobody" — hence the trait guard, emitted
    /// only when the set that defines the trait ships.
    ///
    /// Every one of those tier tests is paired with <see cref="OfficeGate"/>: tier alone counts a
    /// landless office as a crown, and the celestial ministry is nine empire-tier offices that
    /// change hands constantly.
    /// </summary>
    private static void WriteNarrators(StringBuilder sb, MapConfig cfg)
    {
        string notDummy = cfg.EnableWilderness ? "\n\t\t\tNOT = { has_trait = wilderness }" : "";
        string notDummyRuler = cfg.EnableWilderness ? "\n\t\t\tNOT = { has_trait = wilderness }" : "";

        string crownArm(string cond, string tmpl, string world)
            => $"\t\t\tif = {{\n\t\t\t\tlimit = {{ {cond} }}\n"
             + $"\t\t\t\t{PushChainEffect} = {{ TMPL = {tmpl} ACTOR = root OTHER = scope:title WORLD = {world} }}\n\t\t\t}}\n";

        sb.Append($$"""
            # Root = the new holder; scope:title and scope:transfer_type, as on_title_gain gives them.
            gen_chr_on_title_gain_effect = {
            	if = {
            		limit = {
            			scope:title = {
            				tier >= tier_kingdom
            				{{OfficeGate}}
            			}
            			exists = scope:transfer_type{{notDummy}}
            		}
            		scope:title = {
            			# World-worthy: any empire, or a kingdom by anything but plain inheritance.
            			if = {
            				limit = {
            					OR = {
            						tier >= tier_empire
            						NOT = { scope:transfer_type = flag:inheritance }
            					}
            				}
            				gen_chr_title_gain_arms_effect = { WORLD = yes }
            			}
            			else = {
            				gen_chr_title_gain_arms_effect = { WORLD = no }
            			}
            		}
            	}
            }

            # Title scope, from the effect above. One line per way a crown can move.
            gen_chr_title_gain_arms_effect = {
            	if = {
            		limit = { scope:transfer_type = flag:inheritance }
            		{{PushChainEffect}} = { TMPL = gen_chr_crown_inherited ACTOR = root OTHER = scope:title WORLD = $WORLD$ }
            	}
            	else_if = {
            		limit = { scope:transfer_type = flag:usurped }
            		{{PushChainEffect}} = { TMPL = gen_chr_crown_usurped ACTOR = root OTHER = scope:title WORLD = $WORLD$ }
            	}
            	else_if = {
            		limit = {
            			OR = {
            				scope:transfer_type = flag:conquest
            				scope:transfer_type = flag:conquest_holy_war
            				scope:transfer_type = flag:conquest_claim
            				scope:transfer_type = flag:conquest_populist
            			}
            		}
            		{{PushChainEffect}} = { TMPL = gen_chr_crown_conquered ACTOR = root OTHER = scope:title WORLD = $WORLD$ }
            	}
            	else_if = {
            		limit = { scope:transfer_type = flag:created }
            		{{PushChainEffect}} = { TMPL = gen_chr_crown_created ACTOR = root OTHER = scope:title WORLD = $WORLD$ }
            	}
            	else_if = {
            		limit = { scope:transfer_type = flag:election }
            		{{PushChainEffect}} = { TMPL = gen_chr_crown_elected ACTOR = root OTHER = scope:title WORLD = $WORLD$ }
            	}
            	else_if = {
            		limit = {
            			OR = {
            				scope:transfer_type = flag:granted
            				scope:transfer_type = flag:abdication
            				scope:transfer_type = flag:faction_demand
            				scope:transfer_type = flag:independency
            				scope:transfer_type = flag:returned
            				scope:transfer_type = flag:revoked
            				scope:transfer_type = flag:swear_fealty
            				scope:transfer_type = flag:stepped_down
            			}
            		}
            		{{PushChainEffect}} = { TMPL = gen_chr_crown_passed ACTOR = root OTHER = scope:title WORLD = $WORLD$ }
            	}
            	# Leases and lease revocations say nothing: a title on loan has not changed hands.
            }

            # Root = the holder before destruction; scope:landed_title. The title itself keeps its
            # variables but nobody will open it again, so the line goes to whatever stood above it.
            gen_chr_on_title_destroyed_effect = {
            	if = {
            		limit = {
            			scope:landed_title = {
            				tier >= tier_kingdom
            				{{OfficeGate}}
            			}{{notDummy}}
            		}
            		if = {
            			limit = { exists = scope:landed_title.de_jure_liege }
            			scope:landed_title.de_jure_liege = {
            				{{PushChainEffect}} = { TMPL = gen_chr_crown_destroyed ACTOR = root OTHER = scope:landed_title WORLD = yes }
            			}
            		}
            		else = {
            			{{PushWorldEffect}} = { TMPL = gen_chr_crown_destroyed ACTOR = root OTHER = scope:landed_title }
            		}
            	}
            }

            # scope:attacker, scope:defender and the target_titles list, exactly as a casus belli's
            # own on_victory sees them (on_war_won_attacker / _defender).
            #
            # ---- Why this says so much less than it did ----
            #
            # The first version wrote one line per target title, sent every kingdom-tier line to
            # the world, and narrated attacker victories that on_title_gain was narrating anyway as
            # "took X by force of arms" — with the actual new holder named, which this cannot do.
            # The world's book filled with wars inside a generation (seen on screen, 2026-09-09).
            #
            # So: one line per war, for its foremost prize. A crown TAKEN is on_title_gain's line
            # and is not repeated here. A crown HELD is the realm's own news, never the world's.
            # A duchy won stays in the duchy's book and its kingdom's; a duchy held, in the duchy's.
            # Wars that move no title — subjugations, independence — go unrecorded at kingdom
            # tier; that is the price of not writing everything twice.

            # The attacker won. Duchy prizes only; a kingdom taken is on_title_gain's to tell.
            gen_chr_on_war_won_effect = {
            	if = {
            		limit = {
            			NOT = { any_in_list = { list = target_titles  tier >= tier_kingdom  {{OfficeGate}} } }
            			any_in_list = { list = target_titles  tier = tier_duchy  {{OfficeGate}} }
            		}
            		random_in_list = {
            			list = target_titles
            			limit = { tier = tier_duchy  {{OfficeGate}} }
            			save_temporary_scope_as = gen_chr_war_title
            			{{PushEffect}} = { TMPL = gen_chr_war_won ACTOR = scope:attacker OTHER = scope:gen_chr_war_title }
            			de_jure_liege ?= {
            				{{PushEffect}} = { TMPL = gen_chr_war_won ACTOR = scope:attacker OTHER = scope:gen_chr_war_title }
            			}
            		}
            	}
            }

            # The defender held. The foremost prize's own book — and its liege's when it is a crown.
            gen_chr_on_war_held_effect = {
            	if = {
            		limit = { any_in_list = { list = target_titles  tier >= tier_kingdom  {{OfficeGate}} } }
            		random_in_list = {
            			list = target_titles
            			limit = { tier >= tier_kingdom  {{OfficeGate}} }
            			save_temporary_scope_as = gen_chr_war_title
            			{{PushChainEffect}} = { TMPL = gen_chr_war_held ACTOR = scope:defender OTHER = scope:gen_chr_war_title WORLD = no }
            		}
            	}
            	else_if = {
            		limit = { any_in_list = { list = target_titles  tier = tier_duchy  {{OfficeGate}} } }
            		random_in_list = {
            			list = target_titles
            			limit = { tier = tier_duchy  {{OfficeGate}} }
            			save_temporary_scope_as = gen_chr_war_title
            			{{PushEffect}} = { TMPL = gen_chr_war_held ACTOR = scope:defender OTHER = scope:gen_chr_war_title }
            		}
            	}
            }

            # Root = the founder (on_faith_created). Into the founder's realm books and the world;
            # a landless founder is world news only.
            gen_chr_on_faith_created_effect = {
            	if = {
            		limit = { exists = capital_county }
            		capital_county = {
            			{{PushChainEffect}} = { TMPL = gen_chr_faith_founded ACTOR = root OTHER = root.faith WORLD = yes }
            		}
            	}
            	else = {
            		{{PushWorldEffect}} = { TMPL = gen_chr_faith_founded ACTOR = root OTHER = root.faith }
            	}
            }

            # Root = the new culture; scope:founder (on_culture_created).
            gen_chr_on_culture_created_effect = {
            	if = {
            		limit = { exists = scope:founder.capital_county }
            		scope:founder.capital_county = {
            			{{PushChainEffect}} = { TMPL = gen_chr_culture_founded ACTOR = scope:founder OTHER = root WORLD = yes }
            		}
            	}
            	else = {
            		{{PushWorldEffect}} = { TMPL = gen_chr_culture_founded ACTOR = scope:founder OTHER = root }
            	}
            }

            # Global, from yearly_global_pulse. Coronations have no on_action of their own; what a
            # coronation leaves behind is the crowned law on the ruler's realm, and this notices it
            # once per reign. The flag is per character and the law is stripped at succession, so
            # each new ruler's own coronation is a fresh line. Up to a year late, which is fine for
            # a chronicle. World news at empire tier only.
            gen_chr_coronation_sweep_effect = {
            	every_ruler = {
            		limit = {
            			NOT = { has_character_flag = gen_chr_crowned }
            			OR = {
            				has_realm_law = crowned_king
            				has_realm_law = crowned_emperor
            			}
            			exists = primary_title
            			primary_title = { {{OfficeGate}} }{{notDummyRuler}}
            		}
            		add_character_flag = gen_chr_crowned
            		save_temporary_scope_as = gen_chr_crowned_ruler
            		primary_title = {
            			save_temporary_scope_as = gen_chr_crowned_title
            			if = {
            				limit = { tier >= tier_empire }
            				{{PushChainEffect}} = { TMPL = gen_chr_crowned ACTOR = scope:gen_chr_crowned_ruler OTHER = scope:gen_chr_crowned_title WORLD = yes }
            			}
            			else = {
            				{{PushChainEffect}} = { TMPL = gen_chr_crowned ACTOR = scope:gen_chr_crowned_ruler OTHER = scope:gen_chr_crowned_title WORLD = no }
            			}
            		}
            	}
            }


            """);
    }

    /// <summary>
    /// The hooks. Redeclaring a vanilla on_action appends to it (see the on_actions memory note),
    /// so each entry here is a named block of ours listed in the vanilla key, and the work is a
    /// scripted effect the block calls — which keeps this file to the shape vanilla's own use.
    /// </summary>
    private static void WriteOnActions(string modDir)
    {
        var sb = new StringBuilder();
        sb.Append("""
            # The chronicle's narrators. Written by Emit/ChronicleRuntimeWriter.cs; the effects are
            # in common/scripted_effects/zz_gen_chronicle_effects.txt.


            """);

        void Hook(string vanilla, string ours, string effect)
        {
            sb.Append($"{vanilla} = {{\n\ton_actions = {{\n\t\t{ours}\n\t}}\n}}\n\n");
            sb.Append($"{ours} = {{\n\teffect = {{\n\t\t{effect}\n\t}}\n}}\n\n");
        }

        Hook("on_title_gain", "gen_chr_on_title_gain", "gen_chr_on_title_gain_effect = yes");
        Hook("on_title_destroyed", "gen_chr_on_title_destroyed", "gen_chr_on_title_destroyed_effect = yes");
        Hook("on_war_won_attacker", "gen_chr_on_war_won_attacker", "gen_chr_on_war_won_effect = yes");
        Hook("on_war_won_defender", "gen_chr_on_war_won_defender", "gen_chr_on_war_held_effect = yes");
        Hook("on_faith_created", "gen_chr_on_faith_created", "gen_chr_on_faith_created_effect = yes");
        Hook("on_culture_created", "gen_chr_on_culture_created", "gen_chr_on_culture_created_effect = yes");
        Hook("yearly_global_pulse", "gen_chr_yearly",
            "gen_chr_coronation_sweep_effect = yes\n\t\tgen_chr_cycle_check_effect = yes");

        string dir = Path.Combine(modDir, "common", "on_action");
        Directory.CreateDirectory(dir);
        ParadoxText.WriteBom(Path.Combine(dir, "zz_gen_chronicle_on_actions.txt"), sb.ToString());
    }

    /// <summary>
    /// Struggle scope. Which struggle this is and which phase it is in are both runtime facts,
    /// but which duchies, kingdoms and empires a struggle covers is not, so the fan-out is a
    /// literal title list per struggle. Idempotent: it narrates only when the phase differs from
    /// the remembered one, so the yearly ticker and the post-change check can both call it.
    /// </summary>
    private static void WriteStruggleCheck(StringBuilder sb, StruggleMap? struggles)
    {
        sb.Append("# Struggle scope. Narrates a phase change once, into the struggle's titles and the world.\n");
        sb.Append($"{StruggleCheckEffect} = {{\n");

        if (struggles is not null)
        {
            bool first = true;
            foreach (var s in struggles.Struggles)
            {
                sb.Append($"\t{(first ? "if" : "else_if")} = {{\n\t\tlimit = {{ is_struggle_type = {s.Key} }}\n");
                first = false;

                bool firstPhase = true;
                foreach (var phase in s.Phases)
                {
                    string flag = StruggleFlag(s, phase);
                    sb.Append($"\t\t{(firstPhase ? "if" : "else_if")} = {{\n");
                    firstPhase = false;
                    sb.Append($"\t\t\tlimit = {{\n\t\t\t\tis_struggle_phase = {phase.Key}\n");
                    sb.Append($"\t\t\t\ttrigger_if = {{ limit = {{ has_variable = {StrugglePhaseVar} }} NOT = {{ var:{StrugglePhaseVar} = flag:{phase.Key} }} }}\n");
                    sb.Append("\t\t\t}\n");
                    sb.Append($"\t\t\tset_variable = {{ name = {StrugglePhaseVar} value = flag:{phase.Key} }}\n");

                    foreach (var title in Ancestry(s.Duchies))
                        sb.Append($"\t\t\ttitle:{title.Key} = {{ {PushPlainEffect} = {{ TMPL = {flag} }} }}\n");
                    sb.Append($"\t\t\t{PushWorldPlainEffect} = {{ TMPL = {flag} }}\n");
                    sb.Append("\t\t}\n");
                }

                sb.Append("\t}\n");
            }
        }

        sb.Append("}\n\n");
    }

    /// <summary>
    /// Situation sub-region scope. A frontier is recognised by one of its wild counties — a county
    /// belongs to exactly one sub-region — and narrates into the kingdoms and empires its wild
    /// counties lie in.
    ///
    /// Skipped through the bookmark year, strictly. The starting phase's on_start fires as the game
    /// initialises — in <see cref="MapConfig.StartYear"/>, not in the year-one history entry that
    /// declares the situation — and with <c>&gt;=</c> every frontier opened every book with "In
    /// 900, the wild took back …" (seen on screen, 2026-09-09). A genuine era turn inside the first
    /// year is not a thing: the takeover needs colonies to stand for years.
    /// </summary>
    /// <summary>
    /// One effect per struggle per ending, called by that ending's decision. Character scope: the
    /// decision-taker is the actor; the struggle scope rides along as the other party so the
    /// with-actor line is always the one chosen.
    /// </summary>
    private static void WriteStruggleEndings(StringBuilder sb, StruggleMap? struggles)
    {
        if (struggles is null) return;

        sb.Append("# Character scope, from each ending decision, before end_struggle. See Emit/StruggleWriter.cs.\n");
        foreach (var s in struggles.Struggles)
        foreach (var ending in s.Endings)
        {
            string flag = StruggleEndingFlag(s, ending);
            sb.Append($"{StruggleEndingEffect(s, ending)} = {{\n");
            foreach (var title in Ancestry(s.Duchies))
                sb.Append($"\ttitle:{title.Key} = {{ {PushEffect} = {{ TMPL = {flag} ACTOR = root OTHER = struggle:{s.Key} }} }}\n");
            sb.Append($"\t{PushWorldEffect} = {{ TMPL = {flag} ACTOR = root OTHER = struggle:{s.Key} }}\n");
            sb.Append("}\n\n");
        }
    }

    /// <summary>
    /// Global, from the yearly pulse. The Dynastic Cycle is vanilla's situation and cannot be given
    /// phase hooks without overriding its file, so this reads the phase once a year and narrates a
    /// change — the same idempotent shape as the struggle check. The first sight only records:
    /// the situation starts in history and its opening phase is the state the map began in, not
    /// news. The variable lives on the situation, as the Wilds' counters do.
    /// </summary>
    private static void WriteCycleCheck(StringBuilder sb)
    {
        sb.Append("# Global. Narrates a Dynastic Cycle phase change once, into the world's book.\n");
        sb.Append("gen_chr_cycle_check_effect = {\n\tsituation:dynastic_cycle ?= {\n");

        // First sight: remember, say nothing.
        sb.Append($"\t\tif = {{\n\t\t\tlimit = {{ NOT = {{ has_variable = {StrugglePhaseVar} }} }}\n");
        bool first = true;
        foreach (var (phase, _) in CyclePhases)
        {
            sb.Append($"\t\t\t{(first ? "if" : "else_if")} = {{ limit = {{ situation_current_phase = {phase} }} "
                    + $"set_variable = {{ name = {StrugglePhaseVar} value = flag:{phase} }} }}\n");
            first = false;
        }
        sb.Append("\t\t}\n\t\telse = {\n");

        first = true;
        foreach (var (phase, flag) in CyclePhases)
        {
            sb.Append($"\t\t\t{(first ? "if" : "else_if")} = {{\n");
            first = false;
            sb.Append($"\t\t\t\tlimit = {{ situation_current_phase = {phase}  NOT = {{ var:{StrugglePhaseVar} = flag:{phase} }} }}\n");
            sb.Append($"\t\t\t\tset_variable = {{ name = {StrugglePhaseVar} value = flag:{phase} }}\n");
            sb.Append($"\t\t\t\t{PushWorldEffect} = {{ TMPL = {flag} ACTOR = title:{WorldTitleKey}.holder OTHER = title:{WorldTitleKey} }}\n");
            sb.Append("\t\t\t}\n");
        }
        sb.Append("\t\t}\n\t}\n}\n\n");
    }

    private static void WriteWilds(StringBuilder sb, MapConfig cfg, FrontierMap frontier)
    {
        sb.Append("# Sub-region scope, PHASE = the era just entered. Narrates a frontier's era turning.\n");
        sb.Append("# Strictly after the bookmark year: the start phase's on_start fires at game start.\n");
        sb.Append($"{WildsEffect} = {{\n");

        if (!frontier.IsEmpty)
        {
            sb.Append($"\tif = {{\n\t\tlimit = {{ current_year > {cfg.StartYear.ToString(CultureInfo.InvariantCulture)} }}\n");
            bool first = true;
            foreach (var s in frontier.SubRegions)
            {
                if (s.Wild.Count == 0) continue;
                sb.Append($"\t\t{(first ? "if" : "else_if")} = {{\n");
                first = false;
                sb.Append($"\t\t\tlimit = {{ situation_sub_region_has_county = title:{s.Wild[0].Key} }}\n");

                // Kingdoms and empires only. A frontier is cut across whole duchies of wilderness,
                // so the duchy line would be the frontier talking to itself.
                var above = Ancestry(s.Wild.Select(c => c.Parent).OfType<Title>().Distinct().ToList())
                    .Where(t => t.Tier is "k" or "e");
                foreach (var title in above)
                    sb.Append($"\t\t\ttitle:{title.Key} = {{ {PushPlainEffect} = {{ TMPL = gen_chr_{s.Key}_$PHASE$ }} }}\n");
                sb.Append($"\t\t\t{PushWorldPlainEffect} = {{ TMPL = gen_chr_{s.Key}_$PHASE$ }}\n");
                sb.Append("\t\t}\n");
            }
            sb.Append("\t}\n");
        }

        sb.Append("}\n\n");
    }

    /// <summary>The titles themselves and everything above them short of the hegemony, each once.</summary>
    private static List<Title> Ancestry(List<Title> titles)
    {
        var seen = new HashSet<Title>();
        var ordered = new List<Title>();
        foreach (var start in titles)
        {
            for (var t = start; t is not null && t.Tier != "h"; t = t.Parent)
                if (seen.Add(t)) ordered.Add(t);
        }
        return ordered;
    }

    // ===========================================================================================
    // Custom localisation
    // ===========================================================================================

    /// <summary>
    /// One entry per slot, for titles and for the world. Each is a first-match ladder: an unset
    /// slot answers first with the empty key, so no later trigger ever reads a variable that is
    /// not there; then one arm per template, with the with-actor variant ahead of the without.
    /// </summary>
    private static void WriteCustomLoc(string modDir, List<Template> templates)
    {
        var sb = new StringBuilder();
        sb.Append("""
            # How a chronicle slot becomes a sentence. Written by Emit/ChronicleRuntimeWriter.cs.
            #
            # gen_chr_line_N reads slot N of the title it is called on; gen_chw_line_N reads world
            # slot N of title:h_china. The window calls these and nothing else -- an unset slot
            # returns the empty string, and the row's `visible` is StringIsEmpty of the result.
            #
            # First match wins, and the empty arm is first on purpose: it is the only arm that can
            # run on an unset slot, so no arm below it ever fetches a variable that is not there.


            """);

        for (int i = 0; i < TitleSlots; i++) WriteLineEntry(sb, TitleLine(i), TitlePrefix, i, templates);
        for (int i = 0; i < WorldSlots; i++) WriteLineEntry(sb, WorldLine(i), WorldPrefix, i, templates);

        string dir = Path.Combine(modDir, "common", "customizable_localization");
        Directory.CreateDirectory(dir);
        ParadoxText.WriteBom(Path.Combine(dir, "zz_gen_chronicle_loc.txt"), sb.ToString());
    }

    private static void WriteLineEntry(StringBuilder sb, string entry, string p, int slot, List<Template> templates)
    {
        sb.Append($"{entry} = {{\n\ttype = landed_title\n\n");
        sb.Append($"\ttext = {{\n\t\ttrigger = {{ NOT = {{ has_variable = {p}_{slot}_tmpl }} }}\n\t\tlocalization_key = {EmptyKey}\n\t}}\n");

        foreach (var t in templates)
        {
            if (t.TextNoActor is not null)
            {
                sb.Append($"\ttext = {{\n\t\ttrigger = {{ var:{p}_{slot}_tmpl = flag:{t.Flag}  has_variable = {p}_{slot}_actor }}\n");
                sb.Append($"\t\tlocalization_key = {LineKey(t, p, slot, actor: true)}\n\t}}\n");
                sb.Append($"\ttext = {{\n\t\ttrigger = {{ var:{p}_{slot}_tmpl = flag:{t.Flag} }}\n");
                sb.Append($"\t\tlocalization_key = {LineKey(t, p, slot, actor: false)}\n\t}}\n");
            }
            else
            {
                sb.Append($"\ttext = {{\n\t\ttrigger = {{ var:{p}_{slot}_tmpl = flag:{t.Flag} }}\n");
                sb.Append($"\t\tlocalization_key = {LineKey(t, p, slot, actor: true)}\n\t}}\n");
            }
        }

        sb.Append($"\ttext = {{\n\t\tlocalization_key = {EmptyKey}\n\t}}\n}}\n\n");
    }

    private static string LineKey(Template t, string p, int slot, bool actor)
        => $"{t.Flag}_{p}{slot}{(actor ? "_a" : "")}";

    // ===========================================================================================
    // Localisation
    // ===========================================================================================

    /// <summary>
    /// Every template, once per slot, with the holes filled by the datafunctions that read that
    /// slot. The grammar is vanilla's: <c>ROOT.Title.MakeScope.Var('x')</c> is how the Chinese
    /// conquest ordinal reads a title variable from inside a landed_title custom loc, and
    /// <c>.Char</c> / <c>.Title</c> after <c>Var</c> are the casts vanilla writes hundreds of times.
    /// </summary>
    private static void WriteLocalisation(string modDir, List<Template> templates)
    {
        var loc = new LocFile();
        loc.AddBuilt(EmptyKey, "");
        loc.Blank();

        loc.Add("GEN_CHRONICLE_TITLE", "The Chronicle");
        loc.Add("GEN_CHRONICLE_TAB_BUTTON", "#T The Chronicle#!");
        loc.Add("GEN_CHRONICLE_BLURB",
            "What the world remembers, newest first. Every realm keeps its own book as well -- look for it in the title window.");
        loc.Add("GEN_CHRONICLE_EMPTY", "Nothing has happened yet that the world will remember.");
        loc.Add("GEN_CHRONICLE_BEFORE", "Before the bookmark");
        loc.Add("GEN_CHRONICLE_SINCE", "In living memory");
        loc.Blank();

        foreach (var t in templates)
        {
            for (int i = 0; i < TitleSlots; i++) AddLine(loc, t, TitlePrefix, i);
            for (int i = 0; i < WorldSlots; i++) AddLine(loc, t, WorldPrefix, i);
            loc.Blank();
        }

        loc.Write(Path.Combine(modDir, "localization", "english", "gen_chronicle_runtime_l_english.yml"));
    }

    private static void AddLine(LocFile loc, Template t, string p, int slot)
    {
        loc.AddBuilt(LineKey(t, p, slot, actor: true), Fill(t.Text, p, slot));
        if (t.TextNoActor is not null)
            loc.AddBuilt(LineKey(t, p, slot, actor: false), Fill(t.TextNoActor, p, slot));
    }

    private static string Fill(string text, string p, int slot)
    {
        string Var(string part) => $"ROOT.Title.MakeScope.Var('{p}_{slot}_{part}')";
        return text
            .Replace("{year}", $"#weak [{Var("year")}.GetValue|0]#!")
            .Replace("{actor}", $"[{Var("actor")}.Char.GetTitledFirstName]")
            .Replace("{other}", $"[{Var("other")}.Title.GetName]")
            // The same `other` slot, cast differently: a faith or a culture where the template
            // says so. Both casts are vanilla's (`.Faith.GetName` 18 uses, `.Culture.GetName` 8).
            .Replace("{faith}", $"[{Var("other")}.Faith.GetName]")
            .Replace("{culture}", $"[{Var("other")}.Culture.GetName]");
    }
}
