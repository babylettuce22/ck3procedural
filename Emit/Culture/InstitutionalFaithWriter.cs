using System.Text.RegularExpressions;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// Admits generated institutional religions to vanilla's spiritual-fulfillment mechanics,
/// then adapts the shared Synod/cathedral entry points. Religion tags remain unchanged.
/// Lift individual definitions from the installed game so costs, AI and project lifecycle
/// stay version-correct; refuse an edit when its expected source has changed.
/// </summary>
public static class InstitutionalFaithWriter
{
    public static void WriteAll(string modDir, string gameDir, FaithMap faiths)
    {
        var religions = faiths.Faiths.Where(f => !f.Religion.Inherited
                && (f.HasClericalRegions || f.Sees.Count > 0 || f.EraSees.Count > 0))
            .Select(f => f.Religion.Key).Distinct().Order(StringComparer.Ordinal).ToList();
        if (religions.Count == 0)
        {
            // A reused output directory must not retain an earlier world's religion list.
            foreach (string path in new[] {
                "common/spiritual_fulfillment/zz_gen_institutional_fulfillment.txt",
                "common/decisions/zz_gen_synod_pious_deeds.txt",
                "common/scripted_triggers/zz_gen_senior_clerics.txt",
                "common/character_interactions/zz_gen_clerical_arbitration.txt",
                "common/great_projects/types/zz_gen_cathedrals.txt" })
                File.Delete(Path.Combine(modDir, path));
            return;
        }

        string fulfillment = Definition(gameDir, "common/spiritual_fulfillment/00_spiritual_fulfillment_types.txt", "christian_fulfillment");
        fulfillment = Replace(fulfillment, "religions = { christianity_religion }",
            $"religions = {{ christianity_religion {string.Join(' ', religions)} }}");
        Ship(modDir, "common/spiritual_fulfillment/zz_gen_institutional_fulfillment.txt", fulfillment);

        string decision = Definition(gameDir, "common/decisions/dlc_decisions/pam/pam_decisions.txt",
            "pam_promulgate_word_of_pious_deeds_decision");
        decision = Replace(decision, "is_clergy = yes\n\t\thas_spiritual_fulfillment_type = christian_fulfillment", """
            OR = {
                        AND = { is_clergy = yes has_spiritual_fulfillment_type = christian_fulfillment }
                        gen_synod_promulgation_candidate_trigger = yes
                    }
            """);
        decision = Replace(decision,
            "domicile ?= { has_domicile_parameter = unlocks_pam_promulgate_word_of_pious_deeds_decision }",
            "OR = { gen_synod_promulgation_candidate_trigger = yes domicile ?= { has_domicile_parameter = unlocks_pam_promulgate_word_of_pious_deeds_decision } }");
        decision = Replace(decision, "desc = pam_promulgate_word_of_pious_deeds_decision_desc", """
            desc = {
                first_valid = {
                    triggered_desc = {
                        trigger = { faith = { has_doctrine = special_doctrine_gen_clerical_regions } }
                        desc = gen_synod_promulgate_desc
                    }
                    desc = pam_promulgate_word_of_pious_deeds_decision_desc
                }
            }
            """.Replace("\n", "\n\t", StringComparison.Ordinal));
        decision = Replace(decision, "selection_tooltip = pam_promulgate_word_of_pious_deeds_decision_desc_tooltip",
            "selection_tooltip = gen_synod_promulgate_selection");
        int effect = decision.IndexOf("\n\teffect = {", StringComparison.Ordinal);
        int end = effect < 0 ? -1 : ScriptScan.BlockEnd(decision, effect);
        if (end < 0) throw new InvalidDataException("Promulgation decision effect not found.");
        int open = decision.IndexOf('{', effect);
        string original = decision[(open + 1)..(end - 1)];
        decision = decision[..(open + 1)] + """

                if = {
                    limit = { gen_synod_promulgation_candidate_trigger = yes }
                    add_character_flag = pam_promulgate_word_of_pious_deeds_used
                    trigger_event = gen_synod_pious_deeds.0001
                }
                else = {
            """ + original + "\n\t\t}\n\t}" + decision[end..];
        Ship(modDir, "common/decisions/zz_gen_synod_pious_deeds.txt", decision);

        string clergy = "";
        foreach (string key in new[] { "pam_is_senior_cleric_of_character_trigger", "pam_court_wants_monk_courtiers_trigger", "pam_request_white_peace_enemy_trigger" })
        {
            string trigger = Definition(gameDir, "common/scripted_triggers/pam_scripted_triggers.txt", key);
            clergy += Replace(trigger, "religion = religion:christianity_religion", "gen_institutional_religion_trigger = yes") + "\n\n";
        }
        string legacy = Definition(gameDir, "common/scripted_triggers/pam_scripted_triggers.txt", "eligible_for_pam_legacy_trigger");
        clergy += Replace(legacy, "faith.religion = religion:christianity_religion", "gen_institutional_religion_trigger = yes") + "\n";
        string popularity = Definition(gameDir, "common/scripted_triggers/pam_scripted_triggers.txt", "has_tenet_popularity_trigger");
        // Vanilla calls this on both faiths and characters: religion works in either scope.
        clergy += Replace(popularity, "religion = religion:christianity_religion",
            "OR = { religion = religion:christianity_religion "
            + string.Join(' ', religions.Select(key => $"religion = religion:{key}")) + " }") + "\n";
        Ship(modDir, "common/scripted_triggers/zz_gen_senior_clerics.txt", clergy);

        string arbitration = "";
        foreach (string key in new[] { "request_enforce_succession_interaction", "request_white_peace_interaction", "request_victory_interaction" })
        {
            string interaction = Definition(gameDir, "common/character_interactions/pam_interactions.txt", key);
            arbitration += Replace(interaction, "religion ?= religion:christianity_religion", "gen_institutional_religion_trigger = yes") + "\n\n";
        }
        Ship(modDir, "common/character_interactions/zz_gen_clerical_arbitration.txt", arbitration);

        const string projectPath = "common/great_projects/types/01_pam_projects.txt";
        string source = File.ReadAllText(Path.Combine(gameDir, projectPath));
        // Project definitions refer to file-local @constants: ship those alongside the lifted objects.
        string projects = string.Join('\n', Regex.Matches(source, @"(?m)^@\w+\s*=\s*[^\r\n]+")
            .Select(m => m.Value)) + "\n\n";
        foreach (string key in new[] { "pam_cathedral_01", "pam_cathedral_02", "pam_cathedral_03" })
        {
            string project = Definition(gameDir, projectPath, key);
            project = Replace(project, "holder.religion = religion:christianity_religion",
                "holder = { gen_institutional_religion_trigger = yes }");
            project = Replace(project, "faith.religion = religion:christianity_religion",
                "gen_institutional_religion_trigger = yes");
            projects += project + "\n\n";
        }
        Ship(modDir, "common/great_projects/types/zz_gen_cathedrals.txt", projects);
        Console.WriteLine($"  institutional faiths: {religions.Count} religions gain spiritual fulfillment; Synod publicity and cathedral rewards adapted");
    }

    private static string Definition(string gameDir, string relativePath, string key)
    {
        string source = File.ReadAllText(Path.Combine(gameDir, relativePath)).Replace("\r\n", "\n");
        var match = Regex.Match(source, $@"(?m)^{Regex.Escape(key)}\s*=\s*\{{");
        int end = match.Success ? ScriptScan.BlockEnd(source, match.Index) : -1;
        if (end < 0) throw new InvalidDataException($"Institutional faith adaptation: {key} not found in {relativePath}.");
        return source[match.Index..end];
    }

    private static string Replace(string source, string find, string replacement)
    {
        if (!source.Contains(find, StringComparison.Ordinal))
            throw new InvalidDataException($"Institutional faith adaptation: source changed; missing '{find}'.");
        return source.Replace(find, replacement, StringComparison.Ordinal);
    }

    private static void Ship(string modDir, string path, string text)
    {
        string target = Path.Combine(modDir, path);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        ParadoxText.WriteBom(target, "# Generated institutional-faith adaptation, from the installed CK3 files.\n" + text + "\n");
    }
}
