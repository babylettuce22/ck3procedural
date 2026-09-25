using Ck3MapGen.Core;
using Ck3MapGen.Io;

namespace Ck3MapGen.Emit;

/// <summary>
/// Keeps anyone from going to war with the wilderness.
///
/// The wilderness is held by a dummy character standing in for unsettled ground, and CK3 has no
/// notion of ground nobody owns — so left alone, every neighbour sees a weak landless ruler with
/// counties worth taking, and the map is carved up within a decade of play.
///
/// Five layers, because each of the first three was incomplete in a way that only showed up in play,
/// and the last two are wars that are never declared at all — script starts them outright, so no
/// permission field is ever consulted.
///
/// 1. <see cref="WriteCharacterInteraction"/> hides and blocks the declare-war button. That is the
///    player's route, and only the player's — the AI does not declare war through the interaction.
///
/// 2. <see cref="WriteCasusBelliGroups"/> puts an `allowed_against_character` on all 19 casus belli
///    groups. This is the layer that actually stops a war being declared, and it is the one that was
///    missing. `_casus_belli.info` documents the field as running with **root = the defender**, with
///    both scope:attacker and scope:defender set, which is exactly the question being asked; no
///    vanilla group defines it, so there is no field being overwritten.
///
/// 3. <see cref="WriteScriptedTriggers"/> overrides `herders_and_tributary_constraints`, which stays
///    as a backstop and as the attacker-side guard.
///
/// 4. <see cref="WriteNomadDominationDecision"/> guards the nomad Dominate Title decision, which
///    picks its own defenders and calls `start_war` directly, so layers 1-3 are never consulted.
///
/// 5. <see cref="WriteCoronationWarhound"/> does the same for the coronation event where a guest
///    pledges a war on a neighbour, and <see cref="WriteConquerorDecision"/> for the landless
///    adventurer's "Become a Conqueror", which wars whoever holds the ground he is standing on.
///
/// The generalisation from 4 and 5, and the thing to check first the next time the wilderness turns
/// up in somebody's war: a scripted `start_war` asks no permission. `allowed_against_character` and
/// `allowed_for_character` gate the act of DECLARING, so any vanilla content that selects a target
/// itself needs its own selection filter patched, and the search term that finds them is `start_war`
/// in `events/` and `common/decisions/`, not the casus belli files. All 139 such blocks were audited
/// on 2026-09-15; the three patched here are the ones a generated map reaches, and the reasoning for
/// the rest is written up on <see cref="WriteConquerorDecision"/>.
///
/// 6. `wilderness_war_on_dummy` (BaseFilesToCopy/Wilderness/common/on_action) is the layer that does
///    not care how a war started: any war whose primary defender is a dummy ends as `invalidated`
///    the tick it begins, with a yearly sweep behind it for saves already carrying one. It is the
///    backstop for the selecting content this file does not patch — and for whatever the next DLC
///    adds — rather than a replacement for 4 and 5, since an invalidated war still cost the attacker
///    whatever the decision charged him for it.
///
/// ---- Why layer 3 was not enough on its own ----
///
/// It shipped as the only non-interaction guard, and two separate holes in it were found in play:
///
/// * **`migration` and `independence` never call it.** 17 of the 19 groups do; those two do not, so
///   the override was simply absent for them. A nomad migration war against the wilderness ran to
///   completion with nothing to stop it, which is how this was noticed.
///
/// * **For the other 17 it fires too late.** Groups call it from `allowed_for_character`, which is
///   the *attacker* permission check. Vanilla guards its own body on `exists = scope:defender`
///   precisely because that scope is not always set there — so at declaration the defender clause
///   is skipped, the war starts, and only the periodic revalidation (which does have the scope)
///   catches it. The symptom is a war that appears and is then immediately invalidated, which is
///   what a claim war against the wilderness did.
///
/// The lesson is that `allowed_for_character` is the wrong field to ask a question about the target
/// from, however convenient the shared trigger made it. Layer 2 asks it where it belongs.
/// </summary>
public static class CasusBelliWriter
{
    public static void WriteAll(string modDir, string gameDir, Config.MapConfig cfg)
    {
        if (!cfg.EnableWilderness)
        {
            Console.WriteLine("  war rules: SKIPPED (wilderness disabled)");
            return;
        }

        WriteCharacterInteraction(modDir, gameDir);
        WriteMigrationInteraction(modDir, gameDir);
        WriteNomadDominationDecision(modDir, gameDir);
        WriteCoronationWarhound(modDir, gameDir);
        WriteConquerorDecision(modDir, gameDir);
        WriteCasusBelliGroups(modDir, gameDir);
        WriteCasusBelliTypes(modDir, gameDir);
        WriteScriptedTriggers(modDir);
    }

    /// <summary>
    /// The layer that stops playing whack-a-mole: every casus belli TYPE refuses the wilderness as a
    /// target, in `allowed_against_character`.
    ///
    /// ---- Why this exists on top of the group layer ----
    ///
    /// The group's `allowed_for_character` was supposed to be enough. It is not, and three separate
    /// CBs proved it one at a time — claim, then migration, then `mpo_nomad_invasion_cb`. The last
    /// is in group `invasion`, which both has an `allowed_for_character` and calls
    /// `herders_and_tributary_constraints`, with the mod loaded clean (no parse or PostValidate
    /// errors in error.log) — so a guard sitting in that field demonstrably does not stop the war.
    /// The most likely reason is the one vanilla itself hints at by wrapping its own body in
    /// `exists = scope:defender`: at the point the group is asked, there is often no defender yet.
    ///
    /// `allowed_against_character` is the field whose whole job is this question — root is the
    /// defender — and unlike the group experiment it is not a guess: **110 of the 121 vanilla CB
    /// types already use it**, for real constraints like `is_landed = yes` and `is_holy_order = yes`.
    /// A field with 110 vanilla precedents in the exact position we want is a different kind of
    /// claim from one with zero.
    ///
    /// ---- Why every type, and not the ones that look reachable ----
    ///
    /// Three rounds of "this must be the last one" is the argument. Picking the CBs that can
    /// plausibly aim at unsettled ground is a judgement call that has now been wrong three times, and
    /// each wrong guess costs a full generate-load-play cycle to discover. 121 inserts are mechanical
    /// and cost nothing at runtime.
    ///
    /// ---- Discovered rather than listed ----
    ///
    /// The type names are scanned out of the user's own game folder rather than hardcoded, which is
    /// the opposite choice from <see cref="CasusBelliGroups"/> and deliberately so. There are 121 of
    /// them across 26 files and Paradox adds some every DLC; a hardcoded list would leave each new
    /// one silently unguarded, which is exactly the failure being fixed here. The safety a fixed list
    /// would have given is replaced by <see cref="MinimumExpectedTypes"/>: if a parse ever finds
    /// implausibly few types, nothing ships and it says so.
    /// </summary>
    private static void WriteCasusBelliTypes(string modDir, string gameDir)
    {
        string dir = Path.Combine(gameDir, "common", "casus_belli_types");

        if (!Directory.Exists(dir))
        {
            Console.WriteLine("  war rules (types): SKIPPED (casus_belli_types not found in game folder)");
            return;
        }

        int patched = 0, files = 0;

        foreach (string source in Directory.GetFiles(dir, "*.txt").OrderBy(f => f))
        {
            string name = Path.GetFileName(source);
            var types = ScanTypes(File.ReadAllText(source));

            if (types.Count == 0) continue;

            var patch = VanillaPatch.Open(gameDir, $"war rules (types: {name})",
                "common", "casus_belli_types", name);

            if (patch is null) continue;

            foreach ((string type, FieldShape shape, string probe) in types)
            {
                string body = shape switch
                {
                    // Vanilla wrote the block across lines; match it.
                    FieldShape.MultiLine => "\n" + TypeGuardBody,

                    // Vanilla wrote the whole block on one line — 9 of the 110 do, e.g.
                    // `allowed_against_character = { some_trigger = yes }`. Splicing a newline in
                    // there leaves vanilla's own condition stranded on a line of its own with a
                    // leading space. That parses and behaves identically, but the emitted file is
                    // one a person reads when a guard misbehaves, so keep it inline instead.
                    FieldShape.OneLine => " " + TypeGuardInline,

                    // No block at all; add the whole field.
                    _ => $"\n\tallowed_against_character = {{\n{TypeGuardBody}\t}}\n",
                };

                if (shape == FieldShape.Absent)
                    patch.InsertAfter(type, body, probe);
                else
                    patch.InsertAfter(type, body, probe, "allowed_against_character = {");
            }

            if (patch.Ship(modDir)) { files++; patched += types.Count; }
        }

        if (patched < MinimumExpectedTypes)
        {
            Console.WriteLine($"  war rules (types): WARNING only {patched} types guarded across "
                + $"{files} files, expected at least {MinimumExpectedTypes}. "
                + "Vanilla has changed shape — check the casus belli guard before playing.");
        }
        else
        {
            Console.WriteLine($"  war rules (types): {patched} casus belli types guarded "
                + $"across {files} files");
        }
    }

    /// <summary>How vanilla wrote a type's <c>allowed_against_character</c>, if at all.</summary>
    private enum FieldShape { Absent, OneLine, MultiLine }

    /// <summary>
    /// Root is the defender in <c>allowed_against_character</c>, so both tests are unwrapped.
    /// </summary>
    private const string TypeGuardBody =
        "\t\tNOT = { has_trait = wilderness }\n"
        + "\t\tNOT = { government_has_flag = government_is_wilderness }\n";

    /// <summary>The same two tests, for a block vanilla kept on one line.</summary>
    private const string TypeGuardInline =
        "NOT = { has_trait = wilderness } NOT = { government_has_flag = government_is_wilderness }";

    /// <summary>
    /// 121 types ship with CK3 1.19 and every DLC. Well under that means the scan failed rather than
    /// that the game shrank, and shipping a guard over a handful of types while believing the whole
    /// set is covered is worse than shipping none.
    /// </summary>
    private const int MinimumExpectedTypes = 100;

    /// <summary>
    /// Finds the top-level casus belli types in one file, whether each already declares
    /// <c>allowed_against_character</c>, and a probe that anchors uniquely to it.
    ///
    /// The probe is the type name with its opening brace, prefixed by a newline wherever the bare
    /// form is ambiguous — `foo_cb = {` is a substring of `bar_foo_cb = {`, and the CB files are full
    /// of names that extend one another (`mpo_nomad_invasion_cb` and `mpo_nomad_duchy_invasion_cb`
    /// only avoid it by an infix). The first type in a file has no preceding newline, since
    /// <see cref="File.ReadAllText(string)"/> strips the BOM, so that one keeps the bare form.
    /// </summary>
    private static List<(string Type, FieldShape Shape, string Probe)> ScanTypes(string text)
    {
        var result = new List<(string, FieldShape, string)>();

        var matches = System.Text.RegularExpressions.Regex
            .Matches(text, @"(?m)^([a-z_0-9]+) = \{")
            .ToList();

        for (int i = 0; i < matches.Count; i++)
        {
            string type = matches[i].Groups[1].Value;
            int start = matches[i].Index;
            int end = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
            string body = text[start..end];

            // A commented-out declaration must not count — 00_claim.txt ships one, and treating it
            // as present would splice the guard into a comment. Matched from the line start with its
            // tab, so `# allowed_against_character` never hits. Verified across all 26 files: no type
            // has a commented declaration ahead of a real one, so the probe cannot land in a comment.
            int field = body.IndexOf("\n\tallowed_against_character = {", StringComparison.Ordinal);

            FieldShape shape = FieldShape.Absent;

            if (field >= 0)
            {
                int lineEnd = body.IndexOf('\n', field + 1);
                string line = lineEnd < 0 ? body[field..] : body[field..lineEnd];
                shape = line.Contains('}') ? FieldShape.OneLine : FieldShape.MultiLine;
            }

            string bare = type + " = {";
            bool ambiguous = CountOccurrences(text, bare) > 1;
            string probe = ambiguous || start > 0 ? "\n" + bare : bare;

            // A leading newline cannot anchor the first declaration in the file.
            if (start == 0) probe = bare;

            result.Add((type, shape, probe));
        }

        return result;
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0, at = 0;

        while ((at = text.IndexOf(value, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += value.Length;
        }

        return count;
    }

    /// <summary>
    /// Stops a nomad migrating onto the wilderness.
    ///
    /// The casus belli layers cannot reach this one, and the CB itself says why. `migration_cb` in
    /// `common/casus_belli_types/09_mpo_wars.txt` carries `valid_to_start = { always = no }` under a
    /// comment reading "Used in code - the only legal way to start a migration war". A CB whose own
    /// start condition is a flat no is not being validated the way other CBs are: the engine starts
    /// this war itself, so neither the group's `allowed_against_character` nor
    /// `herders_and_tributary_constraints` is consulted on the way in.
    ///
    /// What DOES gate it is the interaction the migration is sent as. `migration_interaction` is
    /// marked `special_interaction = migration` and its target picking is code-driven — the file
    /// says so where `can_be_picked_title` would go: "unused for migration_interaction and
    /// controlled by code". But its `is_valid_showing_failures_only` is ordinary script, and vanilla
    /// already tests the target there (`scope:recipient = { is_ruler = yes ... }`), so that is where
    /// this goes.
    ///
    /// Inserted as a second `scope:recipient` block rather than merged into vanilla's. Triggers are
    /// not fields — two of them in the same implicit AND are both evaluated, which is not true of
    /// two `allowed_for_character` blocks in a casus belli group — so this adds a condition instead
    /// of shadowing one.
    ///
    /// Tooltipped, unlike the group-level guards, because this one greys out a button the player is
    /// looking at rather than filtering an entry out of a list.
    /// </summary>
    private static void WriteMigrationInteraction(string modDir, string gameDir)
    {
        var patch = VanillaPatch.Open(gameDir, "war rules (migration)",
            "common", "character_interactions", "09_mpo_interactions.txt");

        if (patch is null) return;

        string guard =
            "\n\t\tscope:recipient = {\n"
            + "\t\t\tcustom_tooltip = {\n"
            + "\t\t\t\ttext = is_a_wilderness_migration_target_tt\n"
            + "\t\t\t\tNOT = { has_trait = wilderness }\n"
            + "\t\t\t\tNOT = { government_has_flag = government_is_wilderness }\n"
            + "\t\t\t}\n"
            + "\t\t}\n";

        patch.InsertAfter("migration_interaction is_valid_showing_failures_only", guard,
            "migration_interaction = {", "is_valid_showing_failures_only = {");

        patch.Ship(modDir);
    }

    /// <summary>
    /// The nomad Dominate Title decision, which picks its own war targets and so never asks the
    /// casus belli layers anything.
    ///
    /// ---- Why none of the three layers catches this ----
    ///
    /// `nomad_higher_tier_title_decision` takes the de jure liege of the nomad's primary title and,
    /// when that title has no holder, wars *every independent de jure county holder inside it* at
    /// once — `start_war = { cb = domination_cb }` straight out of a `hidden_effect`, with the rest
    /// added through `add_defender`. A scripted `start_war` does not consult
    /// `allowed_against_character`, which is the field the 121-type layer lives in, and it is not
    /// the declare-war interaction either. So the one guard that would have applied is vanilla's own
    /// candidate filter, and its exclusion list is `top_liege`, `top_suzerain`, `is_allied_to` and
    /// `government_is_herder` — none of which the wilderness dummy is.
    ///
    /// The dummy is landed, independent, and holds de jure counties in every half-empty duchy and
    /// kingdom on the map, so it is a candidate in most of them, and being the weakest ruler alive
    /// it is frequently the `order_by = current_military_strength` pick for `main_defender` as well.
    /// Reported from play as a nomad declaring war on the wilds.
    ///
    /// ---- What the guard does, and what it deliberately leaves alone ----
    ///
    /// One more arm on each of the two NOR blocks — the candidate test and the `add_to_list` limit,
    /// which vanilla writes twice and which must stay identical or the decision can select a war it
    /// then cannot populate. Nothing else in the decision is touched.
    ///
    /// A nomad whose target title is *entirely* unsettled now falls through to vanilla's own
    /// last branch, which mints the title and hands it over without a war. That is the correct
    /// reading rather than a hole: it is the same branch vanilla uses when every de jure county is
    /// already yours or your ally's, i.e. when there is nobody to fight, and a crown over empty
    /// steppe is exactly that. The counties themselves stay the dummy's; only the title moves.
    ///
    /// The first branch — the one for a target title that DOES have a holder — is left unguarded on
    /// purpose. The dummies hold counties and two titular kingdoms with no de jure land
    /// (MapGen/Wilderness.cs), and `abandon_county_effect` is the only path that transfers anything
    /// to them, county-tier only. So no county's de jure liege is ever a dummy, and a guard there
    /// would be an untestable branch whose failure mode — falling through to the free-title branch
    /// and taking a held title without war — is worse than the state it guards against.
    /// </summary>
    private static void WriteNomadDominationDecision(string modDir, string gameDir)
    {
        var patch = VanillaPatch.Open(gameDir, "war rules (dominate title)",
            "common", "decisions", "dlc_decisions", "mpo", "mpo_decisions.txt");

        if (patch is null) return;

        // Both splices land INSIDE a NOR, alongside vanilla's `government_has_flag =
        // government_is_herder`, so the arms are the positive tests for what is being excluded —
        // a `NOT` here would read as "must be the wilderness" and select nothing else. Root is the
        // candidate holder in both, so neither test needs a scope, and the trait arm covers the
        // ruins dummy for free (MapGen/Wilderness.cs gives both dummies the same trait).
        patch.InsertAfter("dominate title candidate filter",
            "\n\t\t\t\t\t\t\thas_trait = wilderness"
            + "\n\t\t\t\t\t\t\tgovernment_has_flag = government_is_wilderness",
            "nomad_higher_tier_title_decision = {", "any_de_jure_county_holder = {",
            "government_has_flag = government_is_herder");

        patch.InsertAfter("dominate title war target list",
            "\n\t\t\t\t\t\t\t\thas_trait = wilderness"
            + "\n\t\t\t\t\t\t\t\tgovernment_has_flag = government_is_wilderness",
            "nomad_higher_tier_title_decision = {", "every_de_jure_county_holder = {",
            "government_has_flag = government_is_herder");

        patch.Ship(modDir);
    }

    /// <summary>
    /// The coronation warhound — a guest at your crowning who pledges to go and thrash somebody,
    /// and the second script-driven war the casus belli layers never see.
    ///
    /// `coronation_events.6020` picks a neighbouring ruler for a vassal to swear vengeance on, hands
    /// the vassal an unpressed claim on one of that ruler's counties and starts the war for them.
    /// Reported from play with "The Wilds" in the slot: a queen's coronation where a chieftess
    /// offered to sound her warhorn against the wilderness, and the wilderness lost 30 opinion of
    /// the queen for allowing it.
    ///
    /// Every candidate goes through one scripted trigger — `coronation_events_6020_foe_trigger`,
    /// called at all eight selection sites in the file — so the guard is one insert at the top of
    /// it. Root there is the candidate foe (vanilla's own first test is an unscoped
    /// `government_has_flag = government_is_herder`), which is why these two are unwrapped, and a
    /// plain NOT rather than a NOR arm because this block is a conjunction, not vanilla's exclusion
    /// list further down.
    ///
    /// Note what this does NOT need to be: the trigger already refuses herders and, for a
    /// sedentary chooser, nomads — so the shape "some governments are not somebody you invade at a
    /// party" is vanilla's own, and the wilderness is simply another. The claim half needs no
    /// separate guard either; `abandon_county_effect` strips claims on ground that goes back to a
    /// dummy, and a claim never created is a claim that cannot outlive the county.
    /// </summary>
    private static void WriteCoronationWarhound(string modDir, string gameDir)
    {
        var patch = VanillaPatch.Open(gameDir, "war rules (coronation warhound)",
            "events", "activities", "coronation_activity", "coronation_events_6.txt");

        if (patch is null) return;

        patch.InsertAfter("coronation_events_6020_foe_trigger",
            "\n\tNOT = { has_trait = wilderness }"
            + "\n\tNOT = { government_has_flag = government_is_wilderness }",
            "scripted_trigger coronation_events_6020_foe_trigger = {");

        patch.Ship(modDir);
    }

    /// <summary>
    /// "Become a Conqueror", the landless adventurer's 5,000-prestige decision to war whoever holds
    /// the ground he is standing on — and the one member of the third scripted-war family that a
    /// generated map actually reaches.
    ///
    /// An audit of all 139 `start_war` blocks in the game (2026-09-15) sorts them into four groups.
    /// Most aim at a character the story already holds — actor, recipient, liege, claimant, secret
    /// target — which a dummy can never be. Fourteen aim at a claim's holder, and claims on dummy
    /// ground are stripped at the source. Fourteen name a vanilla title or character outright
    /// (`title:e_byzantium.holder`, Hasan Sabbah) and resolve to nothing on a map that re-declares
    /// those as never-held shims. The remaining family selects a target, and its commonest shape is
    /// `location.county.holder`: whoever holds the ground the character is standing on.
    ///
    /// That is a hole every time an adventurer walks into unsettled land, and this decision is where
    /// it bites hardest — 5,000 prestige for a war on the wilds, plus a pressed claim on a
    /// wilderness county. The guard mirrors the effect's own kingdom / duchy / county ladder as a
    /// `trigger_if` chain in `is_valid_showing_failures_only`, so the decision greys out with a
    /// reason while the adventurer is standing in the wilds and is untouched everywhere else. It is
    /// deliberately not a flat "not in a wilderness county" test: if the duchy above it IS held by a
    /// real ruler, the decision wars that ruler and is perfectly legitimate.
    ///
    /// The event the decision fires (`ep3_laamp_decision_event.1110`) carries the three `start_war`
    /// branches themselves, and is NOT patched — this decision is its only caller, so guarding the
    /// door is enough, and the alternative is copying a 21,000-line vanilla event file for three
    /// lines. `wilderness_war_on_dummy` in 00_colonization_on_actions.txt is the layer that catches
    /// it if that reasoning is ever wrong, along with the rest of the location family.
    /// </summary>
    private static void WriteConquerorDecision(string modDir, string gameDir)
    {
        var patch = VanillaPatch.Open(gameDir, "war rules (conqueror decision)",
            "common", "decisions", "dlc_decisions", "ep_3", "06_ep3_laamp_decisions.txt");

        if (patch is null) return;

        // One tooltip around the whole ladder rather than three: the player does not need to know
        // which tier resolved, only that there is nobody here to conquer.
        patch.InsertAfter("become_conqueror_decision is_valid_showing_failures_only",
            """

                    custom_tooltip = {
                        text = colonize_conqueror_no_foe_tt
                        trigger_if = {
                            limit = { exists = location.kingdom.holder }
                            NOT = { location.kingdom.holder = { has_trait = wilderness } }
                        }
                        trigger_else_if = {
                            limit = { exists = location.duchy.holder }
                            NOT = { location.duchy.holder = { has_trait = wilderness } }
                        }
                        trigger_else = {
                            exists = location.county.holder
                            NOT = { location.county.holder = { has_trait = wilderness } }
                        }
                    }
            """.Replace("    ", "\t"),
            "become_conqueror_decision = {", "is_valid_showing_failures_only = {");

        patch.Ship(modDir);
    }

    /// <summary>
    /// Every casus belli group in the game, by name. Each is a probe for
    /// <see cref="Io.VanillaPatch"/>, and each is a unique substring of the vanilla file — checked
    /// rather than assumed, because `religious` is a prefix of two of the others and would be an
    /// ambiguous anchor if the probe did not carry the ` = {`.
    ///
    /// Listed in full rather than discovered by scanning, so that a group Paradox ADDS in a future
    /// patch does not silently go unguarded — a new group means this list misses nothing that is
    /// here today, and the missing name shows up the next time anyone compares the two.
    /// </summary>
    private static readonly string[] CasusBelliGroups =
    [
        "religious", "religious_disorganised", "religious_script_only", "de_jure", "debug",
        "claim", "civil_war", "invasion", "vassalization", "conquest", "struggle", "subjugation",
        "independence", "event", "artifact", "migration", "humiliation", "mandala", "celestial",
    ];

    /// <summary>
    /// The two groups with no `allowed_for_character` block of their own. Everything else on
    /// <see cref="CasusBelliGroups"/> has one, and gets the guard spliced into it.
    /// </summary>
    private static readonly HashSet<string> GroupsWithoutAllowedForCharacter =
        ["independence", "migration"];

    /// <summary>
    /// Refuses the wilderness as a war target, in every casus belli group.
    ///
    /// ---- `allowed_against_character` is NOT available here, whatever the tooling says ----
    ///
    /// This was first written against `allowed_against_character`, which is the field that asks the
    /// question we want (`_casus_belli.info`: root is the defender, both scopes set) and which
    /// ck3-tiger accepts on a group without a murmur. The engine does not:
    ///
    ///     Error: "Unexpected token: allowed_against_character" in file:
    ///     "common/casus_belli_groups/00_casus_belli_groups.txt"    x19, one per group
    ///
    /// It is a casus belli TYPE field. `_casus_belli.info` documents type fields and says only that
    /// "the group itself can define extra restrictions", which is not the same promise. Groups take
    /// `allowed_for_character`, `should_check_for_interface_availability` and
    /// `can_only_start_via_script`, and nothing else — vanilla uses no other field in this file.
    ///
    /// So the guard goes in `allowed_for_character` after all, which is where vanilla puts its own
    /// defender-side herder test through `herders_and_tributary_constraints`. The scope caveat that
    /// sent this looking elsewhere is real and is handled the way vanilla handles it: the defender
    /// clause is wrapped in `exists = scope:defender`, because that scope is not set when the engine
    /// is asking the plain "may this character use this group at all" question.
    ///
    /// ---- error.log is the oracle, not ck3-tiger ----
    ///
    /// Tiger's schema for this database is wrong, and a tiger-clean run said nothing. The game
    /// reports it plainly on load. Any new field added to a database with no vanilla precedent has
    /// to be checked against `logs/error.log` before it is believed.
    ///
    /// ---- Two shapes of edit ----
    ///
    /// 17 groups already have `allowed_for_character`, so the guard is spliced INTO it — a second
    /// block of the same name would shadow vanilla's constraints rather than add to them.
    /// `independence` (an empty block) and `migration` have none, so they get the whole field.
    /// Those two are also the pair that never call `herders_and_tributary_constraints`, which is why
    /// nothing was guarding them at all.
    /// </summary>
    private static void WriteCasusBelliGroups(string modDir, string gameDir)
    {
        var patch = Io.VanillaPatch.Open(gameDir, "war rules (groups)",
            "common", "casus_belli_groups", "00_casus_belli_groups.txt");

        if (patch is null) return;

        // Root is the attacker in this block, so the wilderness-as-aggressor test is unwrapped and
        // the wilderness-as-target test needs the scope guard.
        const string body =
            "\t\tNOT = { has_trait = wilderness }\n"
            + "\t\tNOT = { government_has_flag = government_is_wilderness }\n"
            + "\t\ttrigger_if = {\n"
            + "\t\t\tlimit = { exists = scope:defender }\n"
            + "\t\t\tscope:defender = {\n"
            + "\t\t\t\tNOT = { has_trait = wilderness }\n"
            + "\t\t\t\tNOT = { government_has_flag = government_is_wilderness }\n"
            + "\t\t\t}\n"
            + "\t\t}\n";

        foreach (string group in CasusBelliGroups)
        {
            if (GroupsWithoutAllowedForCharacter.Contains(group))
            {
                patch.InsertAfter(group, $"\n\tallowed_for_character = {{\n{body}\t}}\n",
                    group + " = {");
            }
            else
            {
                patch.InsertAfter(group, "\n" + body,
                    group + " = {", "allowed_for_character = {");
            }
        }

        patch.Ship(modDir);
    }

    /// <summary>
    /// Copies vanilla's <c>00_war.txt</c> out with the wilderness excluded from both ends of the
    /// interaction.
    ///
    /// Patched by string insertion rather than rewritten, because the file is long, DLC-dependent
    /// and none of our business — the two blocks we care about are found by name and everything
    /// else is carried through untouched.
    ///
    /// Both blocks are needed and they do different jobs: <c>is_shown</c> takes the button off the
    /// screen, while <c>is_valid_showing_failures_only</c> is what stops it being reached another
    /// way and gives the player a reason rather than a dead button — hence the custom tooltips,
    /// which is the only place a wilderness refusal can explain itself.
    ///
    /// Both anchors are scoped to <c>declare_war_interaction</c> by name. They used to be the first
    /// <c>is_shown</c> and the first <c>is_valid_showing_failures_only</c> in the file, which is the
    /// same position only because declare_war happens to be the first block in it — so an
    /// interaction added above would have moved the guard into the wrong one and shipped anyway.
    /// </summary>
    private static void WriteCharacterInteraction(string modDir, string gameDir)
    {
        var patch = VanillaPatch.Open(gameDir, "war rules",
            "common", "character_interactions", "00_war.txt");

        if (patch is null) return;

        // Both ends: the wilderness must be neither a target nor an aggressor.
        string shown = "\n\t\tscope:recipient = {\n\t\t\tNOT = { government_has_flag = government_is_wilderness }\n\t\t\tNOT = { has_trait = wilderness }\n\t\t}\n\t\tscope:actor = {\n\t\t\tNOT = { government_has_flag = government_is_wilderness }\n\t\t\tNOT = { has_trait = wilderness }\n\t\t}";

        string valid = "\n\t\tscope:recipient = {\n\t\t\tcustom_tooltip = {\n\t\t\t\ttext = is_a_wilderness_recipient_tt\n\t\t\t\tNOT = { government_has_flag = government_is_wilderness }\n\t\t\t\tNOT = { has_trait = wilderness }\n\t\t\t}\n\t\t}\n\t\tscope:actor = {\n\t\t\tcustom_tooltip = {\n\t\t\t\ttext = is_a_wilderness_actor_tt\n\t\t\t\tNOT = { government_has_flag = government_is_wilderness }\n\t\t\t\tNOT = { has_trait = wilderness }\n\t\t\t}\n\t\t}";

        patch.InsertAfter("declare_war is_shown", shown,
            "declare_war_interaction = {", "is_shown = {");

        patch.InsertAfter("declare_war is_valid_showing_failures_only", valid,
            "declare_war_interaction = {", "is_valid_showing_failures_only = {");

        patch.Ship(modDir);
    }

    /// <summary>
    /// Overrides one vanilla trigger that every casus belli group already consults.
    ///
    /// Overriding <c>herders_and_tributary_constraints</c> rather than declaring a trigger of our
    /// own is the point: vanilla's casus belli files already call it from every group, so one
    /// override reaches all of them without touching — or having to keep up with — a single CB file.
    /// The herder and tributary clauses it originally carried are reproduced here, because an
    /// override replaces the whole body and dropping them would quietly re-enable herder wars.
    /// </summary>
    private static void WriteScriptedTriggers(string modDir)
    {
        string targetDir = Path.Combine(modDir, "common", "scripted_triggers");
        Directory.CreateDirectory(targetDir);

        string triggers = "# Overrides vanilla trigger to globally forbid CBs against or by the wilderness holder\nherders_and_tributary_constraints = {\n\t# Attacker constraints\n\tNOT = { has_trait = wilderness }\n\tNOT = { government_has_flag = government_is_wilderness }\n\ttrigger_if = {\n\t\tlimit = { government_has_flag = government_is_herder }\n\t\tcustom_tooltip = {\n\t\t\ttext = is_a_herder_actor_cb_tt\n\t\t\talways = no\n\t\t}\n\t}\n\tis_tributary = no\n\n\t# Defender constraints (when evaluated in CB scope)\n\ttrigger_if = {\n\t\tlimit = { exists = scope:defender }\n\t\tscope:defender = {\n\t\t\tNOT = { has_trait = wilderness }\n\t\t\tNOT = { government_has_flag = government_is_wilderness }\n\t\t}\n\t}\n}";

        ParadoxText.WriteBom(Path.Combine(targetDir, "zz_wilderness_war_triggers.txt"), triggers);
        Console.WriteLine("  war rules: wilderness blocked across all Casus Belli groups via scripted_triggers");
    }
}
