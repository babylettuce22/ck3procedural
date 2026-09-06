using Ck3MapGen.Io;

namespace Ck3MapGen.Emit;

/// <summary>
/// Keeps vanilla's council out of the wilderness.
///
/// Every other route to the dummy is already shut. Landed rulers are held off by the `incapable`
/// trait, which fails the `is_available` test almost every character interaction puts on its
/// target; landless adventurers by the diplo-range override in the Wilderness base files; war by
/// <see cref="CasusBelliWriter"/>; and its land by `county_can_be_claimed_trigger`.
///
/// Council tasks are the gap in that story, and they are the gap for a structural reason: a
/// county-target task aims at a COUNTY, so nothing ever asks a question about the character holding
/// it. `is_available`, `is_incapable`, the wilderness trait and the government flag are all sitting
/// on a character the task never scopes to.
///
/// Only two vanilla tasks can reach outside the councillor's own realm — the other twenty are
/// `county_target = realm` or `domain` — and both of them reach the wilderness:
///
/// * `task_fabricate_claim` (`county_target = all`, `ai_county_target = neighbor_land`), and its
///   `clone = task_fabricate_claim` twin `task_fabricate_claim_minister_of_rites`, which inherits
///   `potential_county` and so is covered by the same edit. The claim it produces is inert while
///   the county stays wild — CasusBelliWriter blocks the war — and becomes live the moment somebody
///   colonises the county. So the visible symptom is a stream of "A Claim on X!" events at a dummy
///   nobody is meant to be reading, and the real cost is a frontier quietly pre-seeded with claims
///   that activate against whoever settles it.
///
/// * `task_convince_dejure` (`county_target = neighbor_land`), found while looking at the first and
///   the more damaging of the two, because BOTH its outcomes are wrong here. It fires
///   `steward_task.0001` at the county's top liege, and:
///
///   - accept calls `change_title_holder` outright. The dummy holds more than one title, so it
///     takes the else branch and the county is *transferred*, not vassalised — an AI helping itself
///     to wilderness with no war, no cost and no colonisation;
///   - refuse hits the county with `change_county_control = major_county_control_loss` and ten
///     years of `steward_de_jure_denied_modifier`, so the frontier ends up salted with a
///     stewardship penalty that is still sitting there when somebody settles it.
///
///   Both options are `ai_chance = { base = 50 }`, but refuse carries
///   `ai_value_modifier = { ai_greed = 3 }` against accept's `ai_honor = 2`, and the wilderness
///   trait sets `ai_greed = 1000` — so the dummy refuses most of the time and the usual outcome is
///   the salting rather than the theft. That is an argument for blocking the task rather than for
///   tolerating it: the common case is the one that leaves a mark on the map.
///
/// Both are one insert at the top of a `potential_county` block. A failed `potential_county` filters
/// the county out of the pickable list silently, which is what vanilla's own first line in each
/// block does too, so neither edit needs a tooltip or a loc key.
///
/// Patched through <see cref="VanillaPatch"/> rather than by shipping a copy of either task, because
/// each task object is 400-plus lines of DLC-dependent scoring that is none of our business and
/// would go stale at the next patch.
/// </summary>
public static class CouncilTaskWriter
{
    /// <summary>
    /// The guard, in the scope a `potential_county` block runs in.
    ///
    /// Phrased as a NOT around the holder test rather than as a holder test with a NOT inside it, so
    /// that a county with no holder at all passes. `holder ?= { ... }` is false when there is no
    /// holder, and the inner phrasing would turn that into a block — vanilla's own lines here assume
    /// a holder exists, and this is not the place to start disagreeing with it.
    ///
    /// Tests the government flag and the trait both, the same pair CasusBelliWriter uses, for the
    /// same reason: the flag is what a second uninteractable government would carry, and the trait
    /// is what the character actually holds.
    /// </summary>
    private const string Guard =
        "\n\t\tscope:county = {\n"
        + "\t\t\tNOT = {\n"
        + "\t\t\t\tholder ?= {\n"
        + "\t\t\t\t\tOR = {\n"
        + "\t\t\t\t\t\tgovernment_has_flag = government_is_wilderness\n"
        + "\t\t\t\t\t\thas_trait = wilderness\n"
        + "\t\t\t\t\t}\n"
        + "\t\t\t\t}\n"
        + "\t\t\t}\n"
        + "\t\t}\n";

    public static void WriteAll(string modDir, string gameDir, Config.MapConfig cfg)
    {
        if (!cfg.EnableWilderness)
        {
            Console.WriteLine("  council tasks: SKIPPED (wilderness disabled)");
            return;
        }

        Patch(modDir, gameDir, "council tasks (claims)",
            ["common", "council_tasks", "00_court_chaplain_tasks.txt"],
            "task_fabricate_claim potential_county",
            "task_fabricate_claim = {");

        Patch(modDir, gameDir, "council tasks (de jure)",
            ["common", "council_tasks", "00_steward_tasks.txt"],
            "task_convince_dejure potential_county",
            "task_convince_dejure = {");
    }

    /// <summary>
    /// Both anchors are two probes — the task by name, then the next `potential_county` inside it.
    /// Neither file has one occurrence of `potential_county = {`: the chaplain file has two and the
    /// steward file four. Scoping by the task name first is what makes the anchor a claim about the
    /// file rather than a bet on ordering.
    /// </summary>
    private static void Patch(
        string modDir, string gameDir, string label, string[] path, string anchorName, string task)
    {
        var patch = VanillaPatch.Open(gameDir, label, path);
        if (patch is null) return;

        patch.InsertAfter(anchorName, Guard, task, "potential_county = {");
        patch.Ship(modDir);
    }
}
