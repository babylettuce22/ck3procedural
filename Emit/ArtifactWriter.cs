namespace Ck3MapGen.Emit;

using Ck3MapGen.Io;
using Ck3MapGen.MapGen;
using System.IO;
using System.Text;

public static class ArtifactWriter
{
    public static void WriteTemplates(string modDir)
    {
        string dir = Path.Combine(modDir, "common", "artifacts", "templates");
        Directory.CreateDirectory(dir);

        var sb = new StringBuilder();
        sb.Append("# Procedurally generated templates ensuring equipability and compatibility\n\n");

        sb.Append("gen_weapon_template = {\n");
        sb.Append("\tcan_equip = {\n\t\talways = yes\n\t}\n");
        sb.Append("\tcan_benefit = {\n\t\talways = yes\n\t}\n");
        sb.Append("\tslot = weapon\n");
        sb.Append("}\n\n");

        sb.Append("gen_armor_template = {\n");
        sb.Append("\tcan_equip = {\n\t\talways = yes\n\t}\n");
        sb.Append("\tcan_benefit = {\n\t\talways = yes\n\t}\n");
        sb.Append("\tslot = armor\n");
        sb.Append("}\n\n");

        sb.Append("gen_crown_template = {\n");
        sb.Append("\tcan_equip = {\n\t\talways = yes\n\t}\n");
        sb.Append("\tcan_benefit = {\n\t\talways = yes\n\t}\n");
        sb.Append("\tslot = crown\n");
        sb.Append("}\n\n");

        sb.Append("gen_regalia_template = {\n");
        sb.Append("\tcan_equip = {\n\t\talways = yes\n\t}\n");
        sb.Append("\tcan_benefit = {\n\t\talways = yes\n\t}\n");
        sb.Append("\tslot = regalia\n");
        sb.Append("}\n\n");

        sb.Append("gen_book_template = {\n");
        sb.Append("\tcan_equip = {\n\t\talways = yes\n\t}\n");
        sb.Append("\tcan_benefit = {\n\t\talways = yes\n\t}\n");
        sb.Append("\tslot = book\n");
        sb.Append("}\n\n");

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_templates.txt"), sb.ToString());
    }

    public static void WriteOnGameStart(string modDir, ArtifactMap artifacts)
    {
        string dir = Path.Combine(modDir, "common", "on_action");
        Directory.CreateDirectory(dir);

        var sb = new StringBuilder();
        sb.Append("# Spawn procedural starting artifacts safely on game start\n\n");
        sb.Append("on_game_start = {\n");
        sb.Append("\ton_actions = {\n");
        sb.Append("\t\tgen_spawn_startup_artifacts\n");
        sb.Append("\t}\n");
        sb.Append("}\n\n");

        sb.Append("gen_spawn_startup_artifacts = {\n");
        sb.Append("\teffect = {\n");

        foreach (var (county, arts) in artifacts.ByCounty)
        {
            string charId = HistoryWriter.CharacterId(county);

            sb.Append($"\t\tcharacter:{charId} = {{\n");
            sb.Append("\t\t\tsave_scope_as = owner\n");
            sb.Append("\t\t\tsave_scope_as = creator\n");

            foreach (var art in arts)
            {
                sb.Append("\t\t\tcreate_artifact = {\n");
                sb.Append($"\t\t\t\tname = \"{art.NameKey}\"\n");
                sb.Append($"\t\t\t\tdescription = \"{art.DescriptionKey}\"\n");
                sb.Append($"\t\t\t\ttype = {art.Type}\n");
                sb.Append($"\t\t\t\tvisuals = {art.Visuals}\n");
                sb.Append($"\t\t\t\ttemplate = {art.Template}\n");
                sb.Append($"\t\t\t\twealth = {art.Wealth}\n");
                sb.Append($"\t\t\t\tquality = {art.Quality}\n");
                sb.Append($"\t\t\t\tmodifier = {art.Modifier}\n");
                sb.Append("\t\t\t\thistory = { type = created_before_history }\n");
                sb.Append("\t\t\t}\n");
            }

            sb.Append("\t\t}\n\n");
        }

        sb.Append("\t}\n");
        sb.Append("}\n");

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_artifacts_on_action.txt"), sb.ToString());
    }

    public static void WriteModifiers(string modDir)
    {
        string dir = Path.Combine(modDir, "common", "modifiers");
        Directory.CreateDirectory(dir);

        var sb = new StringBuilder();
        sb.Append("# Custom generated modifiers for procedural artifacts\n\n");

        sb.Append("gen_sovereign_opinion_modifier_1 = {\n\ticon = social_positive\n\tdirect_vassal_opinion = 5\n\tvassal_opinion = 5\n}\n\n");
        sb.Append("gen_sovereign_opinion_modifier_2 = {\n\ticon = social_positive\n\tdirect_vassal_opinion = 10\n\tvassal_opinion = 10\n}\n\n");
        sb.Append("gen_sovereign_opinion_modifier_3 = {\n\ticon = social_positive\n\tdirect_vassal_opinion = 15\n\tvassal_opinion = 15\n}\n\n");

        sb.Append("gen_martial_prowess_modifier_1 = {\n\ticon = prowess_positive\n\tprowess = 1\n\tknight_effectiveness_mult = 0.05\n}\n\n");
        sb.Append("gen_martial_prowess_modifier_2 = {\n\ticon = prowess_positive\n\tprowess = 2\n\tknight_effectiveness_mult = 0.10\n}\n\n");
        sb.Append("gen_martial_prowess_modifier_3 = {\n\ticon = prowess_positive\n\tprowess = 4\n\tknight_effectiveness_mult = 0.15\n}\n\n");

        sb.Append("gen_sacred_piety_modifier_1 = {\n\ticon = piety_positive\n\tmonthly_piety = 0.1\n\tsame_faith_opinion = 5\n}\n\n");
        sb.Append("gen_sacred_piety_modifier_2 = {\n\ticon = piety_positive\n\tmonthly_piety = 0.25\n\tsame_faith_opinion = 10\n}\n\n");
        sb.Append("gen_sacred_piety_modifier_3 = {\n\ticon = piety_positive\n\tmonthly_piety = 0.5\n\tsame_faith_opinion = 15\n}\n\n");

        sb.Append("gen_scholar_learning_modifier_1 = {\n\ticon = learning_positive\n\tlearning = 1\n\tlearning_lifestyle_xp_gain_mult = 0.05\n}\n\n");
        sb.Append("gen_scholar_learning_modifier_2 = {\n\ticon = learning_positive\n\tlearning = 2\n\tlearning_lifestyle_xp_gain_mult = 0.10\n}\n\n");
        sb.Append("gen_scholar_learning_modifier_3 = {\n\ticon = learning_positive\n\tlearning = 3\n\tlearning_lifestyle_xp_gain_mult = 0.15\n}\n\n");

        // --- LEGENDARY COMPOSITE MODIFIERS (Truly Incredible Bonuses) ---

        // Sovereign Jewelry Legendary Mod
        sb.Append("gen_legendary_sovereign_modifier = {\n");
        sb.Append("\ticon = grandeur_positive\n");
        sb.Append("\tdirect_vassal_opinion = 15\n");
        sb.Append("\tvassal_opinion = 15\n");
        sb.Append("\tdynasty_opinion = 10\n");
        sb.Append("\tshort_reign_duration_mult = -0.3\n");
        sb.Append("\tmonthly_prestige_gain_mult = 0.15\n");
        sb.Append("\tlegitimacy_gain_mult = 0.15\n");
        sb.Append("\tvassal_limit = 15\n");
        sb.Append("}\n\n");

        // Martial Relic Legendary Mod
        sb.Append("gen_legendary_martial_modifier = {\n");
        sb.Append("\ticon = prowess_positive\n");
        sb.Append("\tprowess = 8\n");
        sb.Append("\tknight_effectiveness_mult = 0.25\n");
        sb.Append("\tcontrolled_province_advantage = 10\n");
        sb.Append("\tknight_limit = 3\n");
        sb.Append("\theavy_infantry_toughness_mult = 0.15\n");
        sb.Append("\theavy_cavalry_toughness_mult = 0.15\n");
        sb.Append("}\n\n");

        // Sacred Scripture Legendary Mod
        sb.Append("gen_legendary_sacred_modifier = {\n");
        sb.Append("\ticon = piety_positive\n");
        sb.Append("\tmonthly_piety = 1.0\n");
        sb.Append("\tsame_faith_opinion = 20\n");
        sb.Append("\tclergy_opinion = 20\n");
        sb.Append("\tdomain_tax_same_faith_mult = 0.1\n");
        sb.Append("\tlearning = 4\n");
        sb.Append("}\n\n");

        // Scholarly Work Legendary Mod
        sb.Append("gen_legendary_scholar_modifier = {\n");
        sb.Append("\ticon = learning_positive\n");
        sb.Append("\tlearning = 6\n");
        sb.Append("\tlearning_lifestyle_xp_gain_mult = 0.3\n");
        sb.Append("\tdevelopment_growth = 0.3\n");
        sb.Append("\tbuild_speed = -0.2\n");
        sb.Append("\thealth = 0.5\n");
        sb.Append("}\n");

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_artifact_modifiers.txt"), sb.ToString());
    }

    public static void WriteLocalisation(string modDir, ArtifactMap artifacts)
    {
        string dir = Path.Combine(modDir, "localization", "english");
        Directory.CreateDirectory(dir);

        var sb = new StringBuilder();
        sb.Append("l_english:\n");

        foreach (var art in artifacts.AllArtifacts)
        {
            string safeName = ParadoxText.Loc(art.LocalizedName);
            string safeDesc = ParadoxText.Loc(art.LocalizedDescription);
            sb.Append($" {art.NameKey}:0 \"{safeName}\"\n");
            sb.Append($" {art.DescriptionKey}:0 \"{safeDesc}\"\n");
        }

        ParadoxText.WriteBom(Path.Combine(dir, "gen_artifacts_l_english.yml"), sb.ToString());
    }
}