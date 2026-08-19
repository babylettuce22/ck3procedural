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
/// Two layers, because one is not enough. The interaction override hides and blocks the declare-war
/// button, which covers the player and the AI's use of it; the scripted trigger blocks the casus
/// belli groups themselves, which covers every other route to a war — event-granted CBs, claims,
/// and the AI's own war planning, none of which go through the interaction.
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
        WriteScriptedTriggers(modDir);
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
    /// </summary>
    private static void WriteCharacterInteraction(string modDir, string gameDir)
    {
        string sourceFile = Path.Combine(gameDir, "common", "character_interactions", "00_war.txt");
        if (!File.Exists(sourceFile))
        {
            Console.WriteLine("  war rules: SKIPPED (00_war.txt not found in game folder)");
            return;
        }

        string targetDir = Path.Combine(modDir, "common", "character_interactions");
        Directory.CreateDirectory(targetDir);

        string patched = File.ReadAllText(sourceFile);

        // Both ends: the wilderness must be neither a target nor an aggressor.
        string shown = "\n\t\tscope:recipient = {\n\t\t\tNOT = { government_has_flag = government_is_wilderness }\n\t\t\tNOT = { has_trait = wilderness }\n\t\t}\n\t\tscope:actor = {\n\t\t\tNOT = { government_has_flag = government_is_wilderness }\n\t\t\tNOT = { has_trait = wilderness }\n\t\t}";

        int isShownIndex = patched.IndexOf("is_shown = {", StringComparison.Ordinal);
        if (isShownIndex != -1)
        {
            int insertPos = isShownIndex + "is_shown = {".Length;
            patched = patched.Insert(insertPos, shown);
        }

        string valid = "\n\t\tscope:recipient = {\n\t\t\tcustom_tooltip = {\n\t\t\t\ttext = is_a_wilderness_recipient_tt\n\t\t\t\tNOT = { government_has_flag = government_is_wilderness }\n\t\t\t\tNOT = { has_trait = wilderness }\n\t\t\t}\n\t\t}\n\t\tscope:actor = {\n\t\t\tcustom_tooltip = {\n\t\t\t\ttext = is_a_wilderness_actor_tt\n\t\t\t\tNOT = { government_has_flag = government_is_wilderness }\n\t\t\t\tNOT = { has_trait = wilderness }\n\t\t\t}\n\t\t}";

        int isValidIndex = patched.IndexOf("is_valid_showing_failures_only = {", StringComparison.Ordinal);
        if (isValidIndex != -1)
        {
            int insertPos = isValidIndex + "is_valid_showing_failures_only = {".Length;
            patched = patched.Insert(insertPos, valid);
        }

        ParadoxText.WriteBom(Path.Combine(targetDir, "00_war.txt"), patched);
        Console.WriteLine("  war rules: wilderness protected in declare_war_interaction");
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
