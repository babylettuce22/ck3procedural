namespace Ck3MapGen.Emit;

using Ck3MapGen.Io;
using Ck3MapGen.MapGen;
using System.IO;

public static class ArtifactWriter
{
    /// <summary>
    /// The five equip templates, which differ only in their slot.
    ///
    /// They were five copied blocks; the copies were identical apart from one word, so the slot
    /// list is the honest way to write them. <c>always = yes</c> on both gates is deliberate — the
    /// generated artifacts carry their restrictions in their modifiers, not in their templates.
    /// </summary>
    public static void WriteTemplates(string modDir)
    {
        string dir = Path.Combine(modDir, "common", "artifacts", "templates");
        Directory.CreateDirectory(dir);

        var b = new JominiBuilder();
        b.Comment("Procedurally generated templates ensuring equipability and compatibility");
        b.Blank();

        foreach (string slot in new[] { "weapon", "armor", "crown", "regalia", "book" })
        {
            using (b.Block($"gen_{slot}_template"))
            {
                using (b.Block("can_equip")) b.Field("always", "yes");
                using (b.Block("can_benefit")) b.Field("always", "yes");
                b.Field("slot", slot);
            }

            b.Blank();
        }

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_templates.txt"), b.ToString());
    }

    public static void WriteOnGameStart(string modDir, ArtifactMap artifacts)
    {
        string dir = Path.Combine(modDir, "common", "on_action");
        Directory.CreateDirectory(dir);

        var b = new JominiBuilder();
        b.Comment("Spawn procedural starting artifacts safely on game start");
        b.Blank();

        using (b.Block("on_game_start"))
        using (b.Block("on_actions"))
            b.Token("gen_spawn_startup_artifacts");

        b.Blank();

        using (b.Block("gen_spawn_startup_artifacts"))
        using (b.Block("effect"))
        {
            foreach (var (county, arts) in artifacts.ByCounty)
            {
                using (b.Block($"character:{HistoryWriter.CharacterId(county)}"))
                {
                    // create_artifact reads both scopes; the holder is both for a starting relic,
                    // since nothing in the generated history says who actually made it.
                    b.Field("save_scope_as", "owner");
                    b.Field("save_scope_as", "creator");

                    foreach (var art in arts)
                    {
                        using (b.Block("create_artifact"))
                        {
                            b.Quoted("name", art.NameKey);
                            b.Quoted("description", art.DescriptionKey);
                            b.Field("type", art.Type);
                            b.Field("visuals", art.Visuals);
                            b.Field("template", art.Template);
                            b.Field("wealth", art.Wealth);
                            b.Field("quality", art.Quality);
                            b.Field("modifier", art.Modifier);
                            b.Inline("history", "type = created_before_history");
                        }
                    }
                }

                b.Blank();
            }
        }

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_artifacts_on_action.txt"), b.ToString());
    }

    /// <summary>
    /// The modifier pool the generated artifacts draw from. Fixed content: nothing here varies with
    /// the world, only which artifact ends up pointing at which key.
    ///
    /// Values are written as strings rather than numbers on purpose. <c>0.10</c> and <c>0.1</c> are
    /// the same number and not the same file, and these were tuned by reading them next to each
    /// other — formatting them from doubles would silently renumber the whole table.
    /// </summary>
    public static void WriteModifiers(string modDir)
    {
        string dir = Path.Combine(modDir, "common", "modifiers");
        Directory.CreateDirectory(dir);

        var b = new JominiBuilder();
        b.Comment("Custom generated modifiers for procedural artifacts");
        b.Blank();

        void Modifier(string key, string icon, params (string Key, string Value)[] fields)
        {
            using (b.Block(key))
            {
                b.Field("icon", icon);
                foreach (var (k, v) in fields) b.Field(k, v);
            }

            b.Blank();
        }

        Modifier("gen_sovereign_opinion_modifier_1", "social_positive", ("direct_vassal_opinion", "5"), ("vassal_opinion", "5"));
        Modifier("gen_sovereign_opinion_modifier_2", "social_positive", ("direct_vassal_opinion", "10"), ("vassal_opinion", "10"));
        Modifier("gen_sovereign_opinion_modifier_3", "social_positive", ("direct_vassal_opinion", "15"), ("vassal_opinion", "15"));

        Modifier("gen_martial_prowess_modifier_1", "prowess_positive", ("prowess", "1"), ("knight_effectiveness_mult", "0.05"));
        Modifier("gen_martial_prowess_modifier_2", "prowess_positive", ("prowess", "2"), ("knight_effectiveness_mult", "0.10"));
        Modifier("gen_martial_prowess_modifier_3", "prowess_positive", ("prowess", "4"), ("knight_effectiveness_mult", "0.15"));

        Modifier("gen_sacred_piety_modifier_1", "piety_positive", ("monthly_piety", "0.1"), ("same_faith_opinion", "5"));
        Modifier("gen_sacred_piety_modifier_2", "piety_positive", ("monthly_piety", "0.25"), ("same_faith_opinion", "10"));
        Modifier("gen_sacred_piety_modifier_3", "piety_positive", ("monthly_piety", "0.5"), ("same_faith_opinion", "15"));

        Modifier("gen_scholar_learning_modifier_1", "learning_positive", ("learning", "1"), ("learning_lifestyle_xp_gain_mult", "0.05"));
        Modifier("gen_scholar_learning_modifier_2", "learning_positive", ("learning", "2"), ("learning_lifestyle_xp_gain_mult", "0.10"));
        Modifier("gen_scholar_learning_modifier_3", "learning_positive", ("learning", "3"), ("learning_lifestyle_xp_gain_mult", "0.15"));

        // --- LEGENDARY COMPOSITE MODIFIERS (Truly Incredible Bonuses) ---

        Modifier("gen_legendary_sovereign_modifier", "grandeur_positive",
            ("direct_vassal_opinion", "15"), ("vassal_opinion", "15"), ("dynasty_opinion", "10"),
            ("short_reign_duration_mult", "-0.3"), ("monthly_prestige_gain_mult", "0.15"),
            ("legitimacy_gain_mult", "0.15"), ("vassal_limit", "15"));

        Modifier("gen_legendary_martial_modifier", "prowess_positive",
            ("prowess", "8"), ("knight_effectiveness_mult", "0.25"),
            ("controlled_province_advantage", "10"), ("knight_limit", "3"),
            ("heavy_infantry_toughness_mult", "0.15"), ("heavy_cavalry_toughness_mult", "0.15"));

        Modifier("gen_legendary_sacred_modifier", "piety_positive",
            ("monthly_piety", "1.0"), ("same_faith_opinion", "20"), ("clergy_opinion", "20"),
            ("domain_tax_same_faith_mult", "0.1"), ("learning", "4"));

        // No trailing blank: this is the last block in the file.
        using (b.Block("gen_legendary_scholar_modifier"))
        {
            b.Field("icon", "learning_positive");
            b.Field("learning", "6");
            b.Field("learning_lifestyle_xp_gain_mult", "0.3");
            b.Field("development_growth", "0.3");
            b.Field("build_speed", "-0.2");
            b.Field("health", "0.5");
        }

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_artifact_modifiers.txt"), b.ToString());
    }

    public static void WriteLocalisation(string modDir, ArtifactMap artifacts)
    {
        var loc = new LocFile();

        foreach (var art in artifacts.AllArtifacts)
        {
            loc.Add(art.NameKey, art.LocalizedName);
            loc.Add(art.DescriptionKey, art.LocalizedDescription);
        }

        loc.Write(Path.Combine(modDir, "localization", "english", "gen_artifacts_l_english.yml"));
    }
}
