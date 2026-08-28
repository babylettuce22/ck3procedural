using Ck3MapGen.Config;
using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen;

/// <summary>
/// The four moods a struggle cycles between.
///
/// Deliberately vanilla's wheel rather than an invented one. The expensive half of authoring a
/// struggle is not the phases, it is deciding which of the ~45 catalysts vanilla fires generically
/// pushes toward which mood and how hard — and the Iberian struggle is a shipped, tuned answer to
/// exactly that question. Mirroring its shape means the catalyst tables below are a translation of
/// something Paradox balanced rather than a guess, and it means a player who has played Iberia can
/// read a generated struggle without relearning it.
///
/// The order is the escalation ladder, least to most hostile, and <see cref="StruggleWheel"/>
/// depends on it.
/// </summary>
public enum StruggleMood
{
    /// <summary>Cordial to the point that outsiders find it strange. Iberia's Conciliation.</summary>
    Concord,

    /// <summary>Wary coexistence after too much blood. Iberia's Compromise.</summary>
    Accommodation,

    /// <summary>Complacency that leaves room for opportunists. Iberia's Opportunity.</summary>
    Ambition,

    /// <summary>Open violence. Iberia's Hostility.</summary>
    Bloodshed,
}

/// <summary>One phase of a generated struggle: a mood, the prose for it, and what it does.</summary>
public sealed class StrugglePhase
{
    public required StruggleMood Mood { get; init; }

    /// <summary>The script key, <c>gen_struggle_N_phase_&lt;mood&gt;</c>.</summary>
    public required string Key { get; init; }

    /// <summary>Settable for the same reason <see cref="Culture.Name"/> is: the inspector edits it.</summary>
    public required string Name { get; set; }

    public required string Description { get; set; }

    /// <summary>
    /// The phases this one can turn into, and the catalysts that push it there.
    ///
    /// Keyed by mood rather than by phase key so the wheel can be described once, in
    /// <see cref="StruggleWheel"/>, without every struggle's keys being threaded through it.
    /// </summary>
    public required Dictionary<StruggleMood, List<(string Catalyst, string Weight)>> Futures { get; init; }

    /// <summary>Phase parameters, grouped the way the struggle window groups them. The group name
    /// is a vanilla localisation key (<c>WAR_EFFECTS_NAME</c> and friends), and the audience is
    /// <c>common</c>, <c>involved</c>, <c>interloper</c> or <c>uninvolved</c>.</summary>
    public required List<(string Group, string Audience, string Parameter)> Parameters { get; init; }

    /// <summary>Modifier blocks, as (group, block name, key, value) — <c>involved_character_modifier</c>
    /// and the county equivalents.</summary>
    public required List<(string Group, string Block, string Key, string Value)> Modifiers { get; init; }
}

/// <summary>
/// The four ways a struggle can be finished for good.
///
/// The first three are one per outcome rather than one per mood, because an ending is a
/// *settlement* and there are only three shapes a settlement can take: somebody won, nobody won, or
/// the question stopped mattering. Vanilla's Iberian struggle draws the same three, which is worth
/// following — its conditions are a shipped answer to "what should be hard about each of these",
/// and the shape of that answer survives the move to a generated map even though every one of its
/// geographic tests does not.
///
/// <see cref="Foothold"/> is the odd one out and is deliberately shaped differently: it is not a
/// settlement between the people in the quarrel, it is somebody from outside taking the ground out
/// from under all of them.
/// </summary>
public enum StruggleEnding
{
    /// <summary>One ruler holds the ground and the others live with it.</summary>
    Dominance,

    /// <summary>Nobody won. The borders stand because moving them stopped being worth it.</summary>
    StatusQuo,

    /// <summary>The peoples of the region stopped counting each other as separate.</summary>
    Concord,

    /// <summary>
    /// An outsider — uninvolved or interloper, never involved — has taken enough of the region that
    /// the quarrel is no longer the locals' to have. Vanilla's <c>secure_iberian_foothold</c>.
    /// </summary>
    Foothold,
}

/// <summary>
/// One ending: the decision key it is taken through, the modifier it leaves behind, and its prose.
///
/// The conditions themselves are *not* here. They are pure Jomini — nested
/// <c>any_involved_ruler</c> blocks and region percentages — and belong with the writer that emits
/// them; what belongs here is the sentence the player reads instead of the raw trigger, which is
/// why <see cref="Conditions"/> is prose keyed by the writer's condition names rather than logic.
/// </summary>
public sealed class StruggleEndingDef
{
    public required StruggleEnding Kind { get; init; }

    /// <summary>The decision key, which doubles as its own localisation key and as the value passed
    /// to <c>end_struggle</c> so the game records which ending happened.</summary>
    public required string Key { get; init; }

    /// <summary>The permanent character modifier the ender keeps. Shared across struggles — the
    /// reward for dominance is the same reward wherever it was won.</summary>
    public required string Modifier { get; init; }

    public required string Name { get; set; }
    public required string Description { get; set; }

    /// <summary>The one-line summary under the decision's title in the struggle window.</summary>
    public required string Tooltip { get; set; }

    /// <summary>Prose for each condition, keyed by the names in <see cref="StruggleMap.Conditions"/>.
    /// A condition with no entry here would render its raw trigger, so the two lists have to agree.</summary>
    public required Dictionary<string, string> Conditions { get; init; }
}

/// <summary>
/// One generated struggle: a region, the peoples inside it, and the wheel they turn on.
///
/// Mutable in the same places and for the same reasons as <see cref="Culture"/> — the names and
/// prose are editable, the identity that other files reference is not.
/// </summary>
public sealed class GeneratedStruggle
{
    /// <summary>The struggle key, which is also its localisation key.</summary>
    public required string Key { get; init; }

    /// <summary>The geographical region declared for it. Its own key, in its own file: the vanilla
    /// region keys are re-declared against generated titles by
    /// <see cref="Emit.CompatibilityWriter.WriteGeographicalRegions"/> with members chosen for
    /// script compatibility and building style, so none of them describes a place.</summary>
    public required string RegionKey { get; init; }

    /// <summary>The kingdom the struggle was seeded on. Not written anywhere — the region is
    /// written as a duchy list — but it is what the tension was measured over.</summary>
    public required Title Seed { get; init; }

    /// <summary>The duchies the region is made of: every duchy of <see cref="Seed"/> holding at
    /// least one settled county.</summary>
    public required List<Title> Duchies { get; init; }

    public required string Name { get; set; }
    public required string Description { get; set; }

    /// <summary>Everyone who lives in the region. Read off the counties rather than off the
    /// chronicle: the chronicle names who is *quarrelling*, which is the right question for
    /// choosing a region and the wrong one for populating it — a culture that holds three quiet
    /// counties in the middle of a contested kingdom is in the struggle whether it likes it or
    /// not, and leaving it out would make its rulers permanently uninvolved on their own land.</summary>
    public required List<Culture> Cultures { get; init; }

    /// <inheritdoc cref="Cultures"/>
    public required List<Faith> Faiths { get; init; }

    /// <summary>Chronicle tension the region was selected on. Diagnostic only.</summary>
    public required int Tension { get; init; }

    /// <summary>A vanilla illustration path, chosen from base-game art only — the event_story
    /// illustrations vanilla's own struggles use all ship inside flavour packs.</summary>
    public required string Illustration { get; init; }

    public required List<StrugglePhase> Phases { get; init; }

    /// <summary>The four ways this struggle can be finished. Always all four — which of them is
    /// currently *reachable* is a question for the triggers, not for generation.</summary>
    public required List<StruggleEndingDef> Endings { get; init; }

    /// <summary>Everything except <see cref="StruggleEnding.Foothold"/>, which is the outsider's
    /// ending and is not part of what the struggle window offers its members — it is a visible
    /// decision in its own right instead. This is what a phase's <c>ending_decisions</c> lists.</summary>
    public IEnumerable<StruggleEndingDef> MemberEndings
        => Endings.Where(e => e.Kind != StruggleEnding.Foothold);

    /// <summary>
    /// The name as it reads in the middle of a sentence.
    ///
    /// Every pattern in <see cref="StruggleMap"/>'s name bank opens with a capitalised "The", which
    /// is right on a title bar and wrong after a comma. Lowering it here rather than at each use
    /// keeps "end the Contest for Wessex" and "inside the Contest for Wessex" spelled the same way.
    /// </summary>
    public string InSentence => Name.StartsWith("The ", StringComparison.Ordinal)
        ? "the " + Name[4..]
        : Name;

    public required StruggleMood StartMood { get; init; }

    public StrugglePhase PhaseFor(StruggleMood mood) => Phases.First(p => p.Mood == mood);
}

/// <summary>
/// The struggles the world turned out to have, or an empty list if it had none.
///
/// A world with no struggle is a normal outcome, not a failure: a map whose kingdoms each hold one
/// people has nothing to struggle over, and inventing one anyway would put a mechanic on the map
/// that the map does not support.
/// </summary>
public sealed class StruggleMap
{
    public required List<GeneratedStruggle> Struggles { get; init; }

    /// <summary>Every county inside any struggle region, so the chronicle and later passes can ask
    /// whether a title is caught up in one.</summary>
    public required Dictionary<Title, GeneratedStruggle> ByCounty { get; init; }

    /// <summary>
    /// Finds the contested regions and turns each into a struggle.
    ///
    /// Kingdom grain, not duchy. <see cref="ChronicleMap.Frontier"/> detects contest between the
    /// counties of one duchy, which is the right resolution for *detecting* it and far too small
    /// to *be* a struggle — a struggle names peoples and hands out region-wide modifiers, and a
    /// region of four counties would put half its members outside their own struggle. Rolling the
    /// duchy-level tension up to the kingdom is what <see cref="ChronicleMap.Contested"/> already
    /// does for free.
    /// </summary>
    public static StruggleMap Build(
        List<Title> empires, ChronicleMap chronicle, CultureMap cultures, FaithMap faiths,
        WildernessMap wilderness, MapConfig cfg, Rng rng)
    {
        var chosen = new List<GeneratedStruggle>();
        var byCounty = new Dictionary<Title, GeneratedStruggle>();

        if (cfg.MaxStruggles <= 0)
            return new StruggleMap { Struggles = chosen, ByCounty = byCounty };

        // Score every kingdom, then take the loudest few. Scoring them all first rather than
        // stopping at the first N over the threshold matters: tension is unevenly distributed and
        // the first kingdoms in tree order are not the most contested ones.
        var candidates = new List<(Title Kingdom, int Tension, List<Culture> Cultures, List<Faith> Faiths, List<Title> Duchies)>();

        foreach (var kingdom in Titles.Flatten(empires).Where(t => t.Tier == "k"))
        {
            var counties = kingdom.Children
                .SelectMany(d => d.Children)
                .Where(c => c.Tier == "c" && !wilderness.Contains(c))
                .ToList();

            if (counties.Count < MinCounties) continue;

            var living = counties.Select(cultures.For).Distinct().ToList();
            var creeds = counties.Select(faiths.For).Distinct().ToList();

            // Two peoples, not two cultures. Two cultures of one heritage are neighbours who talk
            // funny -- the same distinction the chronicle's frontier detection draws, and drawing
            // it differently here would select regions whose frontier events do not exist.
            bool split = living.Select(c => c.Heritage).Distinct().Count() >= 2
                      || creeds.Select(f => f.Religion).Distinct().Count() >= 2;
            if (!split) continue;

            int tension = chronicle.Contested(kingdom).Tension;
            if (tension < cfg.StruggleMinTension) continue;

            var duchies = kingdom.Children
                .Where(d => d.Tier == "d" && d.Children.Any(c => c.Tier == "c" && !wilderness.Contains(c)))
                .ToList();
            if (duchies.Count < MinDuchies) continue;

            candidates.Add((kingdom, tension, living, creeds, duchies));
        }

        foreach (var (kingdom, tension, living, creeds, duchies) in candidates
                     .OrderByDescending(c => c.Tension)
                     .ThenBy(c => c.Kingdom.Key, StringComparer.Ordinal)
                     .Take(cfg.MaxStruggles))
        {
            int index = chosen.Count;
            string key = $"gen_struggle_{index}";

            var phases = BuildPhases(key, living, creeds, rng);

            // Starting mood is drawn rather than fixed, weighted by how bad the chronicle says it
            // got. A world that always starts in the same phase reads as scripted the second time
            // somebody generates a map, and the wheel turns either way from wherever it starts.
            var start = tension >= cfg.StruggleMinTension * 2
                ? rng.Pick<StruggleMood>([StruggleMood.Bloodshed, StruggleMood.Ambition, StruggleMood.Ambition])
                : rng.Pick<StruggleMood>([StruggleMood.Ambition, StruggleMood.Accommodation, StruggleMood.Accommodation]);

            var struggle = new GeneratedStruggle
            {
                Key = key,
                RegionKey = $"gen_struggle_region_{index}",
                Seed = kingdom,
                Duchies = duchies,
                Name = Name(kingdom, living, rng),
                Description = Describe(kingdom, living, creeds),
                Cultures = living,
                Faiths = creeds,
                Tension = tension,
                Illustration = Illustration(living, creeds, rng),
                Phases = phases,
                Endings = BuildEndings(key, kingdom, living, rng),
                StartMood = start,
            };

            chosen.Add(struggle);

            foreach (var county in duchies.SelectMany(d => d.Children).Where(c => c.Tier == "c"))
                byCounty[county] = struggle;
        }

        return new StruggleMap { Struggles = chosen, ByCounty = byCounty };
    }

    /// <summary>A kingdom with fewer settled counties than this is a rump, and a struggle over it
    /// would cover less ground than the duchy-level frontier that suggested it.</summary>
    private const int MinCounties = 6;

    /// <summary>
    /// And the same test in the other direction, because the two can disagree.
    ///
    /// A rump kingdom whose counties are all in one duchy passes the county test and still produces
    /// a region one duchy wide — which is exactly the resolution the chronicle detects contest at,
    /// and so exactly the resolution a struggle must not be: every ruler in it would be a
    /// neighbour of every other, and the involved/interloper distinction the whole mechanic turns
    /// on would have nobody on the far side of it.
    /// </summary>
    private const int MinDuchies = 3;

    // =====================================================================================
    // The wheel
    // =====================================================================================

    /// <summary>
    /// Which mood each mood can turn into. Vanilla's Iberian wheel, mood for mood.
    ///
    /// Note that it is not symmetric: violence cools to wariness but never straight to friendship,
    /// and friendship sours to wariness but never straight to violence. That asymmetry is what
    /// stops the struggle from flickering — every swing has to pass through the middle, which
    /// takes a phase's worth of points each way.
    /// </summary>
    private static readonly Dictionary<StruggleMood, StruggleMood[]> StruggleWheel = new()
    {
        [StruggleMood.Bloodshed] = [StruggleMood.Accommodation],
        [StruggleMood.Ambition] = [StruggleMood.Bloodshed, StruggleMood.Concord],
        [StruggleMood.Accommodation] = [StruggleMood.Ambition, StruggleMood.Concord],
        [StruggleMood.Concord] = [StruggleMood.Accommodation],
    };

    /// <summary>
    /// Catalysts that push toward the more hostile of two moods, with vanilla's gain values.
    ///
    /// Every entry was verified to be fired by *base-game* script through the generic
    /// <c>every_character_struggle</c> path — vanilla's own on_actions push these into whatever
    /// struggle the actor belongs to, checking only <c>phase_has_catalyst</c>. That is the whole
    /// reason a generated struggle works at all: naming a catalyst here subscribes it to a pipeline
    /// somebody else already wrote and keeps patched. Anything fired only from a DLC-gated block,
    /// or only for the Persian struggle's supporter/detractor split, is deliberately absent.
    /// </summary>
    private static readonly (string Catalyst, string Weight)[] Escalating =
    [
        ("catalyst_broke_truce_against_important_character", "massive_struggle_catalyst_gain"),
        ("catalyst_becomes_rival_with_involved", "major_struggle_catalyst_gain"),
        ("catalyst_discovery_of_very_important_murder", "major_struggle_catalyst_gain"),
        ("catalyst_execute_important", "major_struggle_catalyst_gain"),
        ("catalyst_reveal_secret_important", "major_struggle_catalyst_gain"),
        ("catalyst_gain_struggle_titles_from_interlopers_uninvolved", "major_struggle_catalyst_gain"),
        ("catalyst_interloper_uninvolved_gain_struggle_titles", "major_struggle_catalyst_gain"),
        ("catalyst_demanding_important_conversion", "medium_struggle_catalyst_gain"),
        ("catalyst_forced_conversion", "medium_struggle_catalyst_gain"),
        ("catalyst_populist_uprise", "medium_struggle_catalyst_gain"),
        ("catalyst_raided_involved", "medium_struggle_catalyst_gain"),
        ("catalyst_revoke_title", "medium_struggle_catalyst_gain"),
        ("catalyst_revoked_powerful_diff_faith_vassal_religious_protection", "medium_struggle_catalyst_gain"),
        ("catalyst_unnatural_death_important_character", "medium_struggle_catalyst_gain"),
        ("catalyst_using_a_hook_on_very_important_character", "medium_struggle_catalyst_gain"),
        ("catalyst_creating_a_holy_order", "minor_struggle_catalyst_gain"),
        ("catalyst_fabricating_duchy_level_claims", "minor_struggle_catalyst_gain"),
        ("catalyst_imprison_important", "minor_struggle_catalyst_gain"),
        ("catalyst_new_building_in_castle", "minor_struggle_catalyst_gain"),
        ("catalyst_win_any_war_within_the_region", "minor_struggle_catalyst_gain"),
    ];

    /// <inheritdoc cref="Escalating"/>
    private static readonly (string Catalyst, string Weight)[] Calming =
    [
        ("catalyst_release_important", "massive_struggle_catalyst_gain"),
        ("catalyst_became_best_friend_soulmate_with_very_important_character", "major_struggle_catalyst_gain"),
        // Vanilla files this under conciliation rather than hostility, and it is worth not
        // "fixing": a county that takes the local culture and faith stops being a frontier, which
        // is a way of ending a quarrel even when it is not a kind one.
        ("catalyst_convert_local_culture_faith", "major_struggle_catalyst_gain"),
        ("catalyst_gave_independence_to_powerful_diff_faith_culture_vassal", "major_struggle_catalyst_gain"),
        ("catalyst_grant_land_local_noble", "major_struggle_catalyst_gain"),
        ("catalyst_independence_from_non_dejure_vassal", "major_struggle_catalyst_gain"),
        ("catalyst_granted_powerful_diff_faith_vassal_religious_protection", "medium_struggle_catalyst_gain"),
        ("catalyst_grant_privilege_to_diff_faith_culture_vassal", "medium_struggle_catalyst_gain"),
        ("catalyst_hybridise_or_diverge_regional_cultures", "medium_struggle_catalyst_gain"),
        ("catalyst_invite_diff_faith_culture_to_feast", "medium_struggle_catalyst_gain"),
        ("catalyst_ransom_important", "medium_struggle_catalyst_gain"),
        ("catalyst_became_friend_lover_with_character", "minor_struggle_catalyst_gain"),
        ("catalyst_formed_interreligious_alliance_with_important_character", "minor_struggle_catalyst_gain"),
        ("catalyst_forming_alliance_between_independent_involved_rulers", "minor_struggle_catalyst_gain"),
        ("catalyst_gift_independent_ruler", "minor_struggle_catalyst_gain"),
        ("catalyst_learned_new_language_important", "minor_struggle_catalyst_gain"),
        ("catalyst_new_building_in_city", "minor_struggle_catalyst_gain"),
        ("catalyst_new_building_in_temple", "minor_struggle_catalyst_gain"),
        ("catalyst_sign_truce_outside_war", "minor_struggle_catalyst_gain"),
        ("catalyst_very_important_child_change_culture_or_faith", "minor_struggle_catalyst_gain"),
        ("catalyst_improve_development_vassal_diff_faith_culture", "minimal_struggle_catalyst_gain"),
    ];

    /// <summary>
    /// The drift catalyst, fired once a year by our own hidden event.
    ///
    /// Present on every branch at the same weight, so time alone never decides which way a struggle
    /// goes — it only guarantees that a region where nothing at all happens still eventually moves,
    /// rather than sitting in one phase for three hundred years because the AI never got around to
    /// ransoming anybody.
    /// </summary>
    public const string DriftCatalyst = "catalyst_passing_of_time";

    private static List<StrugglePhase> BuildPhases(
        string key, List<Culture> cultures, List<Faith> faiths, Rng rng)
    {
        var phases = new List<StrugglePhase>();

        foreach (var mood in Enum.GetValues<StruggleMood>())
        {
            var futures = new Dictionary<StruggleMood, List<(string, string)>>();

            foreach (var next in StruggleWheel[mood])
            {
                // Which bank a branch draws from is decided by direction of travel, not by the
                // destination's own mood: leaving Ambition for Concord is a de-escalation and
                // leaving Concord for Accommodation is an escalation, even though Accommodation is
                // the calmer of those two in absolute terms.
                bool up = next > mood;
                var bank = up ? Escalating : Calming;

                // A subset rather than the whole bank. Twenty catalysts on a branch is a wall of
                // text in the struggle window and makes every struggle's phase read identically;
                // eight leaves each generated struggle with its own idea of what makes things
                // worse, which is the flavour this whole feature exists for.
                var picked = bank.ToList();
                rng.Shuffle(picked);

                var branch = picked.Take(CatalystsPerBranch).ToList();
                branch.Insert(0, (DriftCatalyst, "minimal_struggle_catalyst_over_time_gain"));
                futures[next] = branch;
            }

            phases.Add(new StrugglePhase
            {
                Mood = mood,
                Key = $"{key}_phase_{mood.ToString().ToLowerInvariant()}",
                Name = rng.Pick(PhaseNames[mood]),
                Description = PhaseDescription(mood, cultures, faiths),
                Futures = futures,
                Parameters = Parameters(mood),
                Modifiers = Modifiers(mood),
            });
        }

        return phases;
    }

    /// <summary>How many catalysts one branch of one phase lists, over and above the drift.</summary>
    private const int CatalystsPerBranch = 8;

    // =====================================================================================
    // Endings
    // =====================================================================================

    /// <summary>
    /// The condition names each ending is built from, in the order they are shown.
    ///
    /// Shared between the prose here and the trigger blocks in
    /// <see cref="Emit.StruggleWriter"/>: the writer emits a <c>custom_tooltip</c> per name and
    /// looks the sentence up by it, so a name that appears on one side and not the other means
    /// either a condition with no explanation or an explanation for a condition nobody checks.
    /// Keeping the list here, next to the prose, is what makes that mismatch obvious.
    /// </summary>
    public static IReadOnlyList<string> Conditions(StruggleEnding ending) => ending switch
    {
        StruggleEnding.Dominance => ["phase", "hold", "rivals"],
        StruggleEnding.StatusQuo => ["phase", "hold", "rivals", "peace"],
        StruggleEnding.Concord => ["phase", "hold", "allies"],
        _ => ["phase", "outsider", "hold", "seat"],
    };

    /// <summary>
    /// The snake_case name of an ending, used to build both its decision key and its modifier key.
    ///
    /// Written out rather than taken from <c>ToString().ToLowerInvariant()</c>, which would render
    /// StatusQuo as "statusquo" and leave the decision and the modifier it grants spelled
    /// differently. Both keys turn up in error.log, where reading them as the same thing matters.
    /// </summary>
    public static string Token(StruggleEnding ending) => ending switch
    {
        StruggleEnding.Dominance => "dominance",
        StruggleEnding.StatusQuo => "status_quo",
        StruggleEnding.Concord => "concord",
        _ => "foothold",
    };

    /// <summary>The permanent character modifier each ending leaves its taker.</summary>
    public static string Modifier(StruggleEnding ending) => $"gen_struggle_ending_{Token(ending)}";

    /// <summary>
    /// The county modifier an ending leaves on the ender's *own* ground inside the region.
    ///
    /// One per outcome rather than one shared "the struggle is over", because the ground is where
    /// the difference between the four settlements is actually visible. A region taken by force is
    /// garrisoned and sullen; a region that talked itself out of the quarrel is prosperous and
    /// fond of its lord. A single modifier for all four made every ending read the same on the map
    /// however different the decision that produced it was.
    /// </summary>
    public static string Aftermath(StruggleEnding ending) => $"gen_struggle_aftermath_{Token(ending)}";

    /// <summary>
    /// The county modifier the same ending leaves on everyone *else's* ground in the region.
    ///
    /// The counterpart to <see cref="Aftermath"/>, and the half that makes a finished struggle a
    /// regional event rather than a private prize: the neighbours who did not end it still have to
    /// live in whatever was ended, and what that is worth to them differs by outcome — concord is
    /// good for everybody, an outsider's conquest is good for nobody but the outsider.
    /// </summary>
    public static string Settlement(StruggleEnding ending) => $"gen_struggle_settlement_{Token(ending)}";

    /// <summary>
    /// How long both county modifiers last, in years.
    ///
    /// Varied on the same logic as their contents: a peace held down by one crown lasts as long as
    /// that crown's attention, a border everybody has agreed to stop pushing lasts longer, and
    /// peoples who have stopped counting each other as separate do not start again in a lifetime.
    /// The outsider's is the shortest — it is the settlement with the fewest people invested in it.
    /// </summary>
    public static int AftermathYears(StruggleEnding ending) => ending switch
    {
        StruggleEnding.Dominance => 25,
        StruggleEnding.StatusQuo => 40,
        StruggleEnding.Concord => 60,
        _ => 15,
    };

    /// <summary>
    /// How much of the region an outsider has to hold to close it from outside. Vanilla's figure
    /// for the same ending, and low next to the three settlements on purpose: an outsider who had
    /// to take half the region would simply take the other half and use Dominance instead.
    ///
    /// The prose in <see cref="ConditionProse"/> says "a third" for this; change both together.
    /// </summary>
    public const string FootholdShare = "0.33";

    /// <summary>How long the outsider's duchy has to have been theirs, again vanilla's figure. It
    /// is what separates a conquest that stuck from a border raid that happens to be current.</summary>
    public const int FootholdYears = 15;

    private static List<StruggleEndingDef> BuildEndings(
        string key, Title kingdom, List<Culture> cultures, Rng rng)
    {
        string where = kingdom.Name;
        var peoples = cultures.Select(c => c.Heritage.Name).Distinct().ToList();
        string who = List(peoples);

        var endings = new List<StruggleEndingDef>();

        foreach (var kind in Enum.GetValues<StruggleEnding>())
        {
            endings.Add(new StruggleEndingDef
            {
                Kind = kind,
                Key = $"{key}_ending_{Token(kind)}_decision",
                Modifier = Modifier(kind),
                Name = string.Format(rng.Pick(EndingNames[kind]), where),
                Description = EndingDescription(kind, where, who, peoples.Count),
                Tooltip = EndingTooltips[kind],
                Conditions = ConditionProse(kind, where),
            });
        }

        return endings;
    }

    private static readonly Dictionary<StruggleEnding, string[]> EndingNames = new()
    {
        [StruggleEnding.Dominance] =
            ["Mastery of {0}", "Dominion over {0}", "The Subjugation of {0}", "One Crown over {0}"],
        [StruggleEnding.StatusQuo] =
            ["The Peace of {0}", "The Settled Border", "Let {0} Rest", "The Standing Truce"],
        [StruggleEnding.Concord] =
            ["The Concord of {0}", "The Long Peace of {0}", "One People of {0}", "The Reconciling"],
        [StruggleEnding.Foothold] =
            ["A Foothold in {0}", "The Taking of {0}", "{0} Under Foreign Rule", "The Outsider's Peace"],
    };

    private static readonly Dictionary<StruggleEnding, string> EndingTooltips = new()
    {
        [StruggleEnding.Dominance] = "End the struggle by holding the region against all comers.",
        [StruggleEnding.StatusQuo] = "End the struggle by outlasting it. Nobody wins; the borders stand.",
        [StruggleEnding.Concord] = "End the struggle by making its peoples one another's kin.",
        [StruggleEnding.Foothold] = "End the struggle from outside it, by taking enough of the region "
                                  + "that the quarrel is no longer the locals' to have.",
    };

    private static string EndingDescription(StruggleEnding kind, string where, string who, int peoples)
        => kind switch
        {
            StruggleEnding.Dominance =>
                $"The question of who {where} belongs to has an answer, and it is you. The other "
                + "claimants are not gone — nobody in a quarrel this old is ever quite gone — but "
                + "they answer to your court now, and the arguing has moved indoors. What your "
                + "house does with that is its own business; the struggle is over.",

            StruggleEnding.StatusQuo =>
                $"Nothing was settled. {who} still want what they always wanted, and the borders "
                + $"still run where they ran when {(peoples > 2 ? "their grandfathers" : "both their grandfathers")} "
                + "drew them. What changed is the arithmetic: taking one more valley now costs more "
                + "than the valley is worth, and everybody has quietly worked that out at once.",

            StruggleEnding.Concord =>
                $"A traveller through {where} would need telling that there had ever been a "
                + $"quarrel. {who} marry each other, bury each other, and argue about taxes rather "
                + "than about ancestry. The old grievance has not been forgiven so much as "
                + "forgotten, which lasts considerably longer.",

            _ =>
                $"None of it was ever your quarrel. {who} had been at it for generations before "
                + "your banners came over the border, and the argument does not end because "
                + "anybody was persuaded — it ends because the ground it was about is yours, and "
                + "the men who would carry it on now answer to your sheriffs. They will remember "
                + "this differently than you will.",
        };

    private static Dictionary<string, string> ConditionProse(StruggleEnding kind, string where)
        => kind switch
        {
            StruggleEnding.Dominance => new()
            {
                ["phase"] = $"The struggle is in open enmity, or you completely control {where}",
                ["hold"] = $"You hold at least half the counties of {where}",
                ["rivals"] = $"No other involved ruler holds more than a quarter of {where}",
            },

            StruggleEnding.StatusQuo => new()
            {
                ["phase"] = "The struggle is in wary peace",
                ["hold"] = $"You hold less than half the counties of {where}",
                ["rivals"] = $"No other involved ruler holds more than half of {where}",
                ["peace"] = "No two involved independent rulers are at war with each other",
            },

            StruggleEnding.Concord => new()
            {
                ["phase"] = "The struggle is in concord",
                ["hold"] = $"You hold less than half the counties of {where}",
                ["allies"] = "Every other involved independent ruler is your ally or thinks well of you",
            },

            _ => new()
            {
                ["phase"] = "The struggle is in open enmity or in restless ambition",
                ["outsider"] = $"You take no part in the struggle, and your seat lies outside {where}",
                ["hold"] = $"You hold at least a third of the counties of {where}",
                ["seat"] = $"You completely control a duchy of {where}, and have held it for "
                         + $"{FootholdYears} years",
            },
        };

    // =====================================================================================
    // What each mood does
    // =====================================================================================

    /// <summary>
    /// The phase parameters each mood hands out, as (group, audience, parameter).
    ///
    /// Every parameter here is read by base-game script through <c>has_struggle_phase_parameter</c>
    /// — the same rule the catalyst tables follow, and for the same reason. A parameter nothing
    /// checks is not a bug so much as a lie: it renders in the struggle window as a promise the
    /// game will not keep.
    ///
    /// The groups are vanilla localisation keys and only decide which heading the struggle window
    /// files the line under.
    /// </summary>
    private static List<(string, string, string)> Parameters(StruggleMood mood) => mood switch
    {
        StruggleMood.Bloodshed =>
        [
            ("WAR_EFFECTS_NAME", "involved", "unlocks_forced_vassalization_casus_belli"),
            ("WAR_EFFECTS_NAME", "involved", "struggle_clash_restricted_to_single_county"),
            ("FAITH_EFFECTS_NAME", "common", "cheaper_to_convert_to_struggle_faith"),
            ("FAITH_EFFECTS_NAME", "common", "county_faith_conversion_in_region_proceeds_faster"),
            ("FAITH_EFFECTS_NAME", "involved", "piety_from_converting_involved_rulers"),
            ("FAITH_EFFECTS_NAME", "involved", "piety_from_converting_county"),
            ("CULTURE_EFFECTS_NAME", "common", "cheaper_to_convert_to_struggle_culture"),
            ("CULTURE_EFFECTS_NAME", "common", "county_culture_conversion_in_region_proceeds_faster"),
            ("OTHER_EFFECTS_NAME", "involved", "unlocks_abduct_for_all"),
            ("OTHER_EFFECTS_NAME", "involved", "unlocks_fabricate_hooks_for_all"),
            ("OTHER_EFFECTS_NAME", "involved", "unlocks_claim_throne_for_all"),
            ("OTHER_EFFECTS_NAME", "involved", "unlocks_buy_claim_for_all"),
            ("OTHER_EFFECTS_NAME", "involved", "unlocks_demand_payments_for_all"),
            ("OTHER_EFFECTS_NAME", "involved", "unlocks_expedite_scheme_decision"),
            ("OTHER_EFFECTS_NAME", "involved", "powerful_vassal_can_claim_liege_titles"),
        ],

        StruggleMood.Ambition =>
        [
            ("FAITH_EFFECTS_NAME", "common", "cheaper_to_convert_to_struggle_faith"),
            ("FAITH_EFFECTS_NAME", "common", "county_faith_conversion_in_region_proceeds_faster"),
            ("FAITH_EFFECTS_NAME", "common", "holy_wars_in_region_cannot_be_declared"),
            ("CULTURE_EFFECTS_NAME", "common", "cheaper_to_convert_to_struggle_culture"),
            ("CULTURE_EFFECTS_NAME", "involved", "learning_languages_gives_prestige"),
            ("CULTURE_EFFECTS_NAME", "involved", "granting_title_to_local_noble_gives_prestige"),
            ("CULTURE_EFFECTS_NAME", "involved", "gain_acceptance_when_developing_other_culture_county"),
            ("OTHER_EFFECTS_NAME", "involved", "unlocks_buy_claim_for_all"),
            ("OTHER_EFFECTS_NAME", "involved", "unlocks_demand_payments_for_all"),
            ("OTHER_EFFECTS_NAME", "involved", "unlocks_abduct_for_all"),
            ("OTHER_EFFECTS_NAME", "involved", "unlocks_fabricate_hooks_for_all"),
            ("OTHER_EFFECTS_NAME", "involved", "struggle_unlocks_befriend_schemes_for_everyone"),
            ("OTHER_EFFECTS_NAME", "involved", "unlocks_epic_commission_for_independent_rulers"),
            ("OTHER_EFFECTS_NAME", "involved", "unlocks_sell_minor_title_for_kings_and_higher"),
            ("OTHER_EFFECTS_NAME", "involved", "holy_order_can_be_created_by_dukes"),
        ],

        StruggleMood.Accommodation =>
        [
            ("WAR_EFFECTS_NAME", "common", "invasion_conquest_war_cannot_be_declared"),
            ("WAR_EFFECTS_NAME", "common", "holy_wars_in_region_cannot_be_declared"),
            ("WAR_EFFECTS_NAME", "involved", "struggle_cannot_execute_involved_prisoners"),
            ("WAR_EFFECTS_NAME", "involved", "release_prisoner_diff_faith_gives_prestige"),
            ("WAR_EFFECTS_NAME", "involved", "release_prisoner_diff_culture_gives_prestige"),
            ("FAITH_EFFECTS_NAME", "involved", "interfaith_marriages_available_between_involved_characters"),
            ("FAITH_EFFECTS_NAME", "involved", "interfaith_marriages_between_involved_characters_costs_piety"),
            ("CULTURE_EFFECTS_NAME", "common", "easier_culture_hybridising_for_involved_and_interlopers"),
            ("CULTURE_EFFECTS_NAME", "common", "county_culture_conversion_in_region_proceeds_slower"),
            ("OTHER_EFFECTS_NAME", "involved", "struggle_unlocks_befriend_schemes_for_everyone"),
            ("OTHER_EFFECTS_NAME", "involved", "struggle_becoming_friend_gives_prestige"),
            ("OTHER_EFFECTS_NAME", "involved", "apply_truce_when_sending_ward"),
            ("OTHER_EFFECTS_NAME", "involved", "completing_building_in_castle_gives_development"),
        ],

        _ =>
        [
            ("WAR_EFFECTS_NAME", "common", "invasion_conquest_war_cannot_be_declared"),
            ("WAR_EFFECTS_NAME", "common", "holy_wars_in_region_cannot_be_declared"),
            ("WAR_EFFECTS_NAME", "involved", "struggle_cannot_execute_involved_prisoners"),
            ("FAITH_EFFECTS_NAME", "common", "county_faith_conversion_in_region_proceeds_slower"),
            ("FAITH_EFFECTS_NAME", "involved", "interfaith_marriages_available_between_involved_characters"),
            ("FAITH_EFFECTS_NAME", "involved", "interfaith_marriages_between_involved_characters_gives_piety"),
            ("FAITH_EFFECTS_NAME", "involved", "can_trade_piety_for_marriage_acceptance"),
            ("FAITH_EFFECTS_NAME", "involved", "same_faith_friend_piety_gain"),
            ("FAITH_EFFECTS_NAME", "involved", "completing_building_in_temple_gives_piety"),
            ("CULTURE_EFFECTS_NAME", "common", "easier_culture_hybridising_for_involved_and_interlopers"),
            ("CULTURE_EFFECTS_NAME", "common", "county_culture_conversion_in_region_proceeds_slower"),
            ("CULTURE_EFFECTS_NAME", "involved", "learning_languages_gives_piety"),
            ("CULTURE_EFFECTS_NAME", "involved", "gain_acceptance_when_developing_other_culture_county"),
            ("OTHER_EFFECTS_NAME", "involved", "struggle_prestige_from_feast"),
            ("OTHER_EFFECTS_NAME", "involved", "struggle_grant_titles_diff_faith_culture_gives_prestige"),
            ("OTHER_EFFECTS_NAME", "involved", "granting_independence_to_non_dejure_gives_renown"),
            ("OTHER_EFFECTS_NAME", "involved", "piety_level_affects_vassalage_acceptance"),
            ("OTHER_EFFECTS_NAME", "involved", "offer_vassalization_removes_disloyalty"),
            ("OTHER_EFFECTS_NAME", "involved", "struggle_agents_less_likely_to_join_schemes"),
        ],
    };

    /// <summary>
    /// The modifiers each mood applies, as (group, block, key, value).
    ///
    /// Kept small and kept to modifiers vanilla itself uses on a struggle phase. The interesting
    /// half of a struggle is the parameters above — what you are *allowed* to do — and a phase that
    /// also swings a dozen numbers is one nobody can reason about. The county penalties on
    /// interlopers and the uninvolved are the exception and are load-bearing: they are what makes
    /// an outsider's conquest in the region expensive to hold, and so what stops a struggle from
    /// being scenery.
    /// </summary>
    private static List<(string, string, string, string)> Modifiers(StruggleMood mood) => mood switch
    {
        StruggleMood.Bloodshed =>
        [
            ("WAR_EFFECTS_NAME", "involved_character_modifier", "ai_war_chance", "5"),
            ("WAR_EFFECTS_NAME", "involved_character_modifier", "ai_war_cooldown", "-0.25"),
            ("WAR_EFFECTS_NAME", "involved_character_modifier", "mercenary_hire_cost_mult", "-0.3"),
            ("OTHER_EFFECTS_NAME", "interloper_county_modifier", "county_opinion_add", "-15"),
            ("OTHER_EFFECTS_NAME", "interloper_county_modifier", "tax_mult", "-0.2"),
            ("OTHER_EFFECTS_NAME", "uninvolved_county_modifier", "county_opinion_add", "-20"),
            ("OTHER_EFFECTS_NAME", "uninvolved_county_modifier", "tax_mult", "-0.25"),
        ],

        StruggleMood.Ambition =>
        [
            ("OTHER_EFFECTS_NAME", "interloper_character_modifier", "county_opinion_add", "-5"),
            ("OTHER_EFFECTS_NAME", "interloper_character_modifier", "tax_mult", "-0.1"),
            ("OTHER_EFFECTS_NAME", "uninvolved_county_modifier", "county_opinion_add", "-10"),
            ("OTHER_EFFECTS_NAME", "uninvolved_county_modifier", "tax_mult", "-0.15"),
        ],

        StruggleMood.Accommodation =>
        [
            ("WAR_EFFECTS_NAME", "involved_character_modifier", "ai_war_chance", "-3"),
            ("OTHER_EFFECTS_NAME", "involved_character_modifier", "different_faith_opinion", "5"),
            ("OTHER_EFFECTS_NAME", "involved_character_modifier", "different_culture_opinion", "5"),
            ("OTHER_EFFECTS_NAME", "uninvolved_county_modifier", "county_opinion_add", "-10"),
        ],

        _ =>
        [
            ("WAR_EFFECTS_NAME", "involved_character_modifier", "ai_war_chance", "-5"),
            ("WAR_EFFECTS_NAME", "involved_character_modifier", "ai_war_cooldown", "0.5"),
            ("OTHER_EFFECTS_NAME", "involved_character_modifier", "different_faith_opinion", "10"),
            ("OTHER_EFFECTS_NAME", "involved_character_modifier", "different_culture_opinion", "10"),
            ("OTHER_EFFECTS_NAME", "all_county_modifier", "development_growth_factor", "0.1"),
            ("OTHER_EFFECTS_NAME", "uninvolved_county_modifier", "county_opinion_add", "-10"),
        ],
    };

    // =====================================================================================
    // Prose
    // =====================================================================================

    private static readonly Dictionary<StruggleMood, string[]> PhaseNames = new()
    {
        [StruggleMood.Bloodshed] = ["Bloodshed", "The Burning", "Open Enmity", "Reprisal", "The Long Knife"],
        [StruggleMood.Ambition] = ["Ambition", "The Gathering", "Restlessness", "Opportunity", "The Circling"],
        [StruggleMood.Accommodation] = ["Accommodation", "Uneasy Peace", "Wary Ground", "The Held Breath", "Truce"],
        [StruggleMood.Concord] = ["Concord", "Common Cause", "The Long Calm", "Fellowship", "The Quiet Years"],
    };

    private static readonly string[] NamePatterns =
    [
        "The {0} Struggle",
        "The Contest for {0}",
        "The Quarrel of {0}",
        "The Sundering of {0}",
        "The Long Dispute of {0}",
        "The Strife of {0}",
    ];

    private static string Name(Title kingdom, List<Culture> cultures, Rng rng)
    {
        // Two peoples in the title where there are exactly two, because "The Aeric-Vashan
        // Struggle" says who is fighting and "The Struggle for Wessex" only says where. Above two
        // the name gets unreadable, so it falls back to the place.
        var peoples = cultures.Select(c => c.Heritage).Distinct().ToList();
        if (peoples.Count == 2 && rng.Chance(0.4))
            return $"The {peoples[0].Name}–{peoples[1].Name} Struggle";

        return string.Format(rng.Pick(NamePatterns), kingdom.Name);
    }

    private static string Describe(Title kingdom, List<Culture> cultures, List<Faith> faiths)
    {
        var peoples = cultures.Select(c => c.Heritage.Name).Distinct().ToList();
        var creeds = faiths.Select(f => f.Name).Distinct().ToList();

        string who = List(peoples);
        string what = creeds.Count > 1
            ? $" They do not share a faith either: {List(creeds)} {Every(creeds.Count)} claim ground here."
            : "";

        return $"{kingdom.Name} has never belonged to one people. {who} {Every(peoples.Count)} count it "
             + $"home, and none of them can hold it alone.{what} Every generation decides again "
             + "whether that means killing or living alongside.";
    }

    private static string PhaseDescription(StruggleMood mood, List<Culture> cultures, List<Faith> faiths)
    {
        string who = List(cultures.Select(c => c.Heritage.Name).Distinct().ToList());

        return mood switch
        {
            StruggleMood.Bloodshed =>
                $"Grievance has outrun memory. {who} raid, seize and settle scores in the open, and "
                + "outsiders who take land here find it ungovernable.",
            StruggleMood.Ambition =>
                "The killing has stopped and the coveting has not. Old claims are dusted off, "
                + "quiet arrangements are made, and everyone waits to see who moves first.",
            StruggleMood.Accommodation =>
                "Too much blood, too recently. Nobody trusts anybody, but the wars have grown "
                + "expensive enough that the borders are being respected on purpose.",
            _ =>
                $"For now, {who} are neighbours rather than rivals. Marriages cross the old lines, "
                + "and a stranger would need telling that there was ever a quarrel here.",
        };
    }

    /// <summary>"both" for a pair, "all" for a crowd. Two peoples who "all" count somewhere home is
    /// the single most generated-sounding sentence this file could produce.</summary>
    private static string Every(int count) => count == 2 ? "both" : "all";

    // =====================================================================================
    // The chronicle cross-reference
    // =====================================================================================

    /// <summary>
    /// The closing line the title lore panel puts on a title caught up in a struggle, already
    /// escaped for a .yml value — or null for a title that is not in one.
    ///
    /// This is the only place the two generated histories are joined up, and it is worth the join:
    /// the chronicle explains why a region is at odds with itself and then stops, while the
    /// struggle window states that it is and never says why. A player reading a county's lore has
    /// no way to tell that the frontier they just read about is the mechanic they can see in the
    /// interface, because the two use different words for the same quarrel. One sentence naming it
    /// fixes that, and naming it is all this does — the line is not a chronicle *event*, carries no
    /// year and no tension, and cannot be rolled up into a parent title, which is why it is built
    /// here at write time rather than added to <see cref="ChronicleMap"/>.
    ///
    /// Prose lives with the struggle rather than with the chronicle because the struggle is the
    /// thing being named. It is also the half that was generated second: struggle selection reads
    /// the chronicle, so nothing in the chronicle can be written against a struggle that does not
    /// exist yet.
    /// </summary>
    public string? Note(Title title)
    {
        if (Struggles.Count == 0) return null;

        switch (title.Tier)
        {
            case "c":
                return ByCounty.TryGetValue(title, out var county) ? Fill(CountyNotes, title, county) : null;

            case "d":
            {
                var s = Struggles.FirstOrDefault(x => x.Duchies.Contains(title));
                return s is null ? null : Fill(DuchyNotes, title, s);
            }

            case "k":
            {
                var s = Struggles.FirstOrDefault(x => x.Seed == title);
                return s is null ? null : Fill(KingdomNotes, title, s);
            }

            // An empire is the one tier that can hold more than one struggle, because the region is
            // a kingdom and an empire has several. Naming them together rather than picking the
            // loudest is the honest report: an emperor with two quarrels inside their borders has a
            // different problem from one with a single quarrel, and that is the fact worth stating.
            case "e":
            {
                var inside = Struggles.Where(x => x.Seed.Parent == title).ToList();

                return inside.Count switch
                {
                    0 => null,
                    1 => Io.ParadoxText.Loc(
                        $"The empire's quiet is not everyone's: {inside[0].InSentence} is being "
                        + $"fought out in {inside[0].Seed.Name}."),
                    _ => Io.ParadoxText.Loc(
                        "More than one of its kingdoms cannot agree who it belongs to: "
                        + $"{List(inside.Select(s => s.InSentence).ToList())}."),
                };
            }

            default:
                return null;
        }
    }

    /// <summary>
    /// Picks a template by title index rather than at random.
    ///
    /// A struggle region is a kingdom's worth of counties, so a single sentence would be read a
    /// dozen times over by anybody touring the region — the same reason the chronicle keeps several
    /// templates per event. The index is stable across runs and adjacent counties hold adjacent
    /// indices, so neighbours reliably get different wording rather than merely usually.
    /// </summary>
    private static string Fill(string[] bank, Title title, GeneratedStruggle s)
        => Io.ParadoxText.Loc(bank[title.Index % bank.Length]
            .Replace("{PLACE}", title.Name)
            .Replace("{WHAT}", s.InSentence));

    private static readonly string[] CountyNotes =
    [
        "All of this has a name now: {PLACE} lies inside {WHAT}, and nothing here is read any other way.",
        "{PLACE} is ground {WHAT} is fought over, whatever else it may be.",
        "The quarrel outgrew {PLACE} generations ago. It is a corner of {WHAT} now.",
        "Nothing that happens in {PLACE} is local any more, {WHAT} having swallowed the whole district.",
    ];

    private static readonly string[] DuchyNotes =
    [
        "Every county named above lies within {WHAT}.",
        "{PLACE} sits inside {WHAT} entire, and its lords answer for that whether they take part or not.",
        "None of this is {PLACE}'s own business alone: the whole duchy is inside {WHAT}.",
    ];

    private static readonly string[] KingdomNotes =
    [
        "Taken together, this is {WHAT}, and it covers every duchy named above.",
        "{PLACE} end to end is the ground of {WHAT}.",
        "There is a name for a kingdom that cannot settle who it belongs to. Here it is {WHAT}.",
    ];

    /// <summary>"A", "A and B", "A, B and C" — the human way, so the prose reads as written rather
    /// than as generated.</summary>
    private static string List(List<string> items) => items.Count switch
    {
        0 => "Several peoples",
        1 => items[0],
        2 => $"{items[0]} and {items[1]}",
        _ => $"{string.Join(", ", items.Take(items.Count - 1))} and {items[^1]}",
    };

    /// <summary>
    /// Base-game illustrations only.
    ///
    /// Vanilla's two struggles point at <c>event_story/fp2_*</c> and <c>fp3_*</c> art, which ships
    /// inside the flavour packs — a generated mod that referenced those would show a missing
    /// texture for anybody who does not own them, and this tool cannot know what its user's users
    /// own. Everything below is unprefixed base-game art.
    /// </summary>
    private static string Illustration(List<Culture> cultures, List<Faith> faiths, Rng rng)
    {
        const string dir = "gfx/interface/illustrations/event_scenes";

        // A struggle between religions gets religious art, one between peoples gets civic art. The
        // distinction is the same one the selection test makes, so the picture agrees with the
        // reason the struggle exists.
        bool interfaith = faiths.Select(f => f.Religion).Distinct().Count() >= 2;

        string[] bank = interfaith
            ? ["church.dds", "temple.dds", "mosque.dds", "battlefield.dds"]
            : ["battlefield.dds", "market_west.dds", "market_east.dds", "throneroom_west.dds", "raid_burning.dds"];

        return $"{dir}/{rng.Pick(bank)}";
    }
}
