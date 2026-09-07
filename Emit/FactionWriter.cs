using Ck3MapGen.Io;

namespace Ck3MapGen.Emit;

/// <summary>
/// Keeps the county-joined factions from forming against the wilderness and ruins dummies.
///
/// Vanilla has twelve faction types. Eight are joined by CHARACTERS, and those are shut in one
/// object by the `passes_faction_hard_block` override in
/// BaseFilesToCopy/Wilderness/common/scripted_rules/zz_wilderness_faction_rules.txt — the engine
/// calls that rule with `scope:target` set to the faction's target, for both joining and creating.
///
/// The four here are joined by COUNTIES instead — `requires_county = yes`, `requires_character = no`
/// — so no character ever passes through that rule on the way in and it cannot see them at all:
///
///   populist_faction, escalated_peasant_faction   (00_populist_faction.txt)
///   peasant_faction                               (00_peasant_faction_new.txt)
///   nomadic_faction                               (00_nomadic_faction.txt)
///
/// A populist uprising against the wilderness is what surfaced this, and it is one of the four.
///
/// ---- Two fields per type, doing different jobs ----
///
/// * `can_county_create` — root is the title, `scope:target` is the character the faction would be
///   aimed at. This is the one that PREVENTS the faction, and it is the point of the exercise.
/// * `is_valid` — root is the faction. This dissolves one that already exists, which matters only
///   for a save carried across this change, but is two lines and removes the "existing games stay
///   broken" caveat.
///
/// Both fields already exist on all four types, so these are inserts into vanilla's own blocks
/// rather than added fields — no chance of shadowing a definition the way a second
/// `can_county_create` would.
///
/// ---- Why the dummy's counties rise at all ----
///
/// 00_wilderness_government.txt gives them `county_opinion_add = 100` with a note saying its
/// counties never revolt. That was never going to be enough: a populist faction's discontent is
/// computed by its own `discontent_progress` block, not from county opinion, so perfectly content
/// counties join anyway.
/// </summary>
public static class FactionWriter
{
    /// <summary>One vanilla file and the county-joined faction types defined in it.</summary>
    private static readonly (string File, string[] Types)[] Targets =
    [
        ("00_populist_faction.txt", ["populist_faction", "escalated_peasant_faction"]),
        ("00_peasant_faction_new.txt", ["peasant_faction"]),
        ("00_nomadic_faction.txt", ["nomadic_faction"]),
    ];

    /// <summary>
    /// Into `can_county_create`. Root is the title being asked; the character the faction would
    /// target is `scope:target`.
    /// </summary>
    private const string CreateGuard =
        "\n\t\ttrigger_if = {\n"
        + "\t\t\tlimit = { exists = scope:target }\n"
        + "\t\t\tscope:target = {\n"
        + "\t\t\t\tNOT = { has_trait = wilderness }\n"
        + "\t\t\t\tNOT = { government_has_flag = government_is_wilderness }\n"
        + "\t\t\t}\n"
        + "\t\t}\n";

    /// <summary>
    /// Into `is_valid`. Root is the faction, so the target comes from `faction_target`.
    ///
    /// Guarded on the link existing rather than written as `faction_target ?= { ... }`. The `?=`
    /// form is false when the link is missing, and a false `is_valid` dissolves the faction — so a
    /// faction that briefly has no target would be destroyed by our guard rather than left alone.
    /// </summary>
    private const string ValidGuard =
        "\n\t\ttrigger_if = {\n"
        + "\t\t\tlimit = { exists = faction_target }\n"
        + "\t\t\tfaction_target = {\n"
        + "\t\t\t\tNOT = { has_trait = wilderness }\n"
        + "\t\t\t\tNOT = { government_has_flag = government_is_wilderness }\n"
        + "\t\t\t}\n"
        + "\t\t}\n";

    public static void WriteAll(string modDir, string gameDir, Config.MapConfig cfg)
    {
        if (!cfg.EnableWilderness)
        {
            Console.WriteLine("  faction rules: SKIPPED (wilderness disabled)");
            return;
        }

        foreach ((string file, string[] types) in Targets)
        {
            var patch = VanillaPatch.Open(gameDir, "faction rules",
                "common", "factions", file);

            if (patch is null) continue;

            // Two probes per anchor: the faction type by name, then the next field of that name
            // inside it. 00_populist_faction.txt holds two types, so the type name is what keeps
            // escalated_peasant_faction's blocks from being found when populist_faction is meant.
            foreach (string type in types)
            {
                patch.InsertAfter($"{type} can_county_create", CreateGuard,
                    type + " = {", "can_county_create = {");

                patch.InsertAfter($"{type} is_valid", ValidGuard,
                    type + " = {", "is_valid = {");
            }

            patch.Ship(modDir);
        }
    }
}
