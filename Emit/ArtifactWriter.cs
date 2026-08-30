namespace Ck3MapGen.Emit;

using Ck3MapGen.Io;
using Ck3MapGen.MapGen;
using System.IO;

public static class ArtifactWriter
{
    /// <summary>
    /// Two templates: the ordinary one and the one-of-a-kind one.
    ///
    /// There were five, one per slot, and they were identical apart from a <c>slot</c> field —
    /// which is not a template field at all. <c>templates/_templates.info</c> documents
    /// <c>can_equip</c>, <c>can_benefit</c>, <c>can_reforge</c>, <c>can_repair</c>,
    /// <c>fallback</c>, <c>ai_score</c> and <c>unique</c>, and no vanilla template contains
    /// <c>slot</c>; the slot comes from the artifact's *type*. So the five differed only in a line
    /// the parser does not read, and were one template wearing five names.
    ///
    /// <c>can_benefit</c> is where an artifact stops being a stat block. A child or an incapable
    /// ruler holding the realm's sword gets the <c>fallback</c> instead of the real modifier, which
    /// is the same gate AGOT puts on every piece of Valyrian steel.
    /// </summary>
    public static void WriteTemplates(string modDir)
    {
        string dir = Path.Combine(modDir, "common", "artifacts", "templates");
        Directory.CreateDirectory(dir);

        var b = new JominiBuilder();
        b.Comment("Procedurally generated artifact templates.\n"
            + "No slot field: the slot is a property of the type, not of the template.");
        b.Blank();

        using (b.Block("gen_artifact_template"))
        {
            using (b.Block("can_equip")) b.Field("always", "yes");

            // An artifact does its work for a ruler who can actually wield it. Everyone else keeps
            // it — and keeps the prestige of keeping it — but not the rest.
            using (b.Block("can_benefit")) b.Field("is_capable_adult", "yes");
            using (b.Block("fallback")) b.Field("monthly_prestige", "0.1");

            using (b.Block("ai_score")) b.Field("value", "100");
        }

        b.Blank();

        using (b.Block("gen_legendary_template"))
        {
            using (b.Block("can_equip")) b.Field("always", "yes");
            using (b.Block("can_benefit")) b.Field("is_capable_adult", "yes");
            using (b.Block("fallback")) b.Field("monthly_prestige", "0.25");

            // The AI should want these badly enough to scheme for them.
            using (b.Block("ai_score")) b.Field("value", "250");

            // Marks the artifact as one-of-a-kind in the inventory UI, and blocks reforging it
            // into something ordinary.
            b.Field("unique", "yes");
        }

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_templates.txt"), b.ToString());
    }

    /// <summary>
    /// Binds each weapon look in <see cref="WeaponAssets"/> to a concrete icon and 3D entity.
    ///
    /// This is the file that makes a generated weapon show a *particular* model rather than
    /// whatever vanilla's culture triggers happen to pick. The form is AGOT's, which is the only
    /// worked example of per-artifact models in the wild:
    ///
    /// <code>
    /// vs_blackfyre_visuals = { icon = "vs_blackfyre.dds"  asset = blackfyre_sword_entity }
    /// </code>
    ///
    /// Two fields and no triggers. <c>_visuals.info</c> also allows the conditional form
    /// (<c>asset = { trigger = { ... } reference = ... }</c>), which AGOT uses for Longclaw to
    /// swap the pommel by dynasty — worth knowing when generated weapons start carrying house
    /// marks, but unnecessary while one look means one model.
    ///
    /// <c>default_type</c> is deliberately omitted: it has no gameplay effect and exists only for
    /// the automatic test-artifact generator.
    /// </summary>
    public static void WriteVisuals(string modDir, IReadOnlyList<WeaponAsset>? forgedWeapons = null)
    {
        string dir = Path.Combine(modDir, "common", "artifacts", "visuals");
        Directory.CreateDirectory(dir);

        var b = new JominiBuilder();
        b.Comment("Procedurally generated weapon visuals: one entry per look in WeaponAssets.cs.\n"
            + "Each binds an inventory icon and the entity the portrait draws once the artifact\n"
            + "is equipped, which works because vanilla's equipped-weapon accessory declares\n"
            + "game_entity_override = weapon and lets the engine substitute this entity.");

        foreach (var kind in WeaponAssets.Kinds)
        {
            b.Blank();
            b.Comment(kind);

            foreach (var asset in WeaponAssets.ForKind(kind))
            {
                using (b.Block(asset.VisualKey))
                {
                    b.Quoted("icon", asset.Icon);
                    b.Field("asset", asset.Entity);
                }
            }
        }

        // Forged weapons are emitted alongside rather than inside the loop above: they are not in
        // WeaponAssets, because they do not exist until this world is generated. Their entities are
        // written by ForgedWeaponWriter next to the .mesh files they name.
        if (forgedWeapons is { Count: > 0 })
        {
            b.Blank();
            b.Comment("forged weapons — procedurally assembled meshes, one per pool entry");

            foreach (var asset in forgedWeapons)
            {
                using (b.Block(asset.VisualKey))
                {
                    b.Quoted("icon", asset.Icon);
                    b.Field("asset", asset.Entity);
                }
            }
        }

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_weapon_visuals.txt"), b.ToString());
    }

    /// <summary>
    /// Spawns the starting treasure, with the history that explains it.
    ///
    /// The history is the point of this file. <c>create_artifact</c>'s own <c>history</c> block
    /// takes the first entry; the rest are <c>add_artifact_history</c> calls on the artifact scope,
    /// and a sovereign piece also gets <c>add_artifact_title_history</c> so the panel names the
    /// realm rather than only the man. Without them the artifact panel is blank, which is what
    /// every generated artifact used to ship as.
    /// </summary>
    public static void WriteOnGameStart(string modDir, ArtifactMap artifacts)
    {
        string dir = Path.Combine(modDir, "common", "on_action");
        Directory.CreateDirectory(dir);

        var b = new JominiBuilder();
        b.Comment("Spawn procedural starting artifacts safely on game start.\n"
            + "on_game_start_after_lobby, not on_game_start: an artifact equipped before the\n"
            + "lobby closes never reaches the portrait, so a generated weapon stayed invisible\n"
            + "until its owner equipped something else and forced a rebuild. After the lobby is\n"
            + "the same moment the artifact-window toggle acts, which always worked.");
        b.Blank();

        using (b.Block("on_game_start_after_lobby"))
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
                    // Feature selection reads scope:owner — vanilla saves it before every
                    // create_artifact for exactly this reason, and errors in the log without it.
                    b.Field("save_scope_as", "owner");

                    // THE MAKER IS THE HOLDER'S PARENT, and it is not only flavour.
                    //
                    // These artifacts had no creator at all, on the reasoning that this generator
                    // models no smiths and should not invent one. That was honest and it broke the
                    // portraits: every armour look is gated on `creator ?= { culture = ... }`, so a
                    // piece with no creator matches nothing and a starting armour was never worn.
                    // The debug event minted its own with `CREATOR = root` and worked, which is why
                    // the fault looked like it lived in the modifiers.
                    //
                    // A parent is the right answer rather than a convenient one: they share the
                    // holder's culture, so the gate lands on the same look either way; they are
                    // dead by the start date, so nothing is claimed about a living character; and an
                    // heirloom from one's father reads as inherited rather than as something the
                    // holder had made this morning. Falls back to the holder for a character history
                    // gave no parent, which is the old behaviour for exactly those.
                    using (b.Block("if"))
                    {
                        using (b.Block("limit")) b.Field("exists", "father");
                        using (b.Block("father")) b.Field("save_scope_as", "gen_maker");
                    }

                    using (b.Block("else_if"))
                    {
                        using (b.Block("limit")) b.Field("exists", "mother");
                        using (b.Block("mother")) b.Field("save_scope_as", "gen_maker");
                    }

                    using (b.Block("else"))
                        b.Field("save_scope_as", "gen_maker");

                    foreach (var art in arts)
                    {
                        b.Blank();

                        // A court artifact needs a room. Without Royal Court there are no court
                        // slots at all, and below kingdom tier the holder has no court to put them
                        // in — so the whole creation is skipped rather than producing an artifact
                        // that exists and can never be displayed. Inventory artifacts are written
                        // unguarded, which is why a world without the DLC still gets its treasure.
                        IDisposable? guard = null;

                        if (art.NeedsRoyalCourt)
                        {
                            var gate = b.Block("if");
                            using (b.Block("limit"))
                            {
                                b.Field("has_dlc_feature", "royal_court");
                                b.Field("has_royal_court", "yes");
                            }

                            guard = gate;
                        }

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

                            // A field of create_artifact, not a saved scope: `save_scope_as =
                            // creator` does nothing at all, which is what this used to be.
                            b.Field("creator", "scope:gen_maker");
                            b.Field("save_scope_as", "gen_new_artifact");

                            // The heirlooms of a world are not allowed to crumble before the
                            // player has met them.
                            if (art.Rarity >= ArtifactRarity.Famed) b.Field("decaying", "no");

                            WriteHistory(b, art.Provenance[0]);
                        }

                        using (b.Block("scope:gen_new_artifact"))
                        {
                            foreach (var entry in art.Provenance.Skip(1))
                            {
                                using (b.Block("add_artifact_history")) WriteHistoryFields(b, entry);
                            }

                            if (art.TitleHistory is { } th)
                            {
                                using (b.Block("add_artifact_title_history"))
                                {
                                    b.Field("target", $"title:{th.TitleKey}");
                                    b.Field("date", th.Date);
                                }
                            }

                            // Created is not equipped. Without this the treasure sits in the
                            // inventory on the start date and none of the modifiers are live.
                            b.Field("equip_artifact_to_owner_replace", "yes");
                        }

                        guard?.Dispose();
                    }
                }

                b.Blank();
            }
        }

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_artifacts_on_action.txt"), b.ToString());
    }

    private static void WriteHistory(JominiBuilder b, ArtifactProvenance entry)
    {
        using (b.Block("history")) WriteHistoryFields(b, entry);
    }

    private static void WriteHistoryFields(JominiBuilder b, ArtifactProvenance entry)
    {
        b.Field("type", entry.Type);
        b.Field("date", entry.Date);

        if (entry.ActorId is not null) b.Field("actor", $"character:{entry.ActorId}");
        if (entry.RecipientId is not null) b.Field("recipient", $"character:{entry.RecipientId}");
        if (entry.LocationProvinceId > 0) b.Field("location", $"province:{entry.LocationProvinceId}");
    }

    /// <summary>
    /// The modifier pool: three shared rungs per family, plus one bespoke block per legendary.
    ///
    /// The rungs are keyed by rarity rather than by a quality bucket, because rarity is now the
    /// thing that is decided and quality is the thing derived from it. Magnitudes are read off
    /// vanilla's own ladders in <c>00_artifact_modifiers.txt</c> — prowess runs 1..11 across the
    /// four bands, knight effectiveness 0.02..0.09, monthly piety 0.1..0.8 — so a generated famed
    /// sword is worth about what a vanilla famed sword is worth.
    ///
    /// <c>prowess_no_portrait</c>, not <c>prowess</c>: the plain key visibly rebuilds the
    /// character's portrait, which is why every artifact in the game uses the other one.
    ///
    /// Values are written as strings rather than numbers on purpose. <c>0.10</c> and <c>0.1</c> are
    /// the same number and not the same file, and these were tuned by reading them next to each
    /// other — formatting them from doubles would silently renumber the whole table.
    /// </summary>
    public static void WriteModifiers(string modDir, ArtifactMap artifacts)
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

        Modifier("gen_sovereign_modifier_common", "grandeur_positive",
            ("vassal_opinion", "3"));
        Modifier("gen_sovereign_modifier_masterwork", "grandeur_positive",
            ("vassal_opinion", "5"), ("short_reign_duration_mult", "-0.15"));
        Modifier("gen_sovereign_modifier_famed", "grandeur_positive",
            ("vassal_opinion", "8"), ("short_reign_duration_mult", "-0.25"),
            ("dynasty_opinion", "5"), ("monthly_prestige", "0.15"));

        Modifier("gen_martial_modifier_common", "prowess_positive",
            ("prowess_no_portrait", "1"));
        Modifier("gen_martial_modifier_masterwork", "prowess_positive",
            ("prowess_no_portrait", "4"), ("knight_effectiveness_mult", "0.04"));
        Modifier("gen_martial_modifier_famed", "prowess_positive",
            ("prowess_no_portrait", "7"), ("knight_effectiveness_mult", "0.08"),
            ("monthly_prestige", "0.15"));

        Modifier("gen_sacred_modifier_common", "piety_positive",
            ("monthly_piety", "0.15"), ("same_faith_opinion", "3"));
        Modifier("gen_sacred_modifier_masterwork", "piety_positive",
            ("monthly_piety", "0.35"), ("same_faith_opinion", "6"));
        Modifier("gen_sacred_modifier_famed", "piety_positive",
            ("monthly_piety", "0.6"), ("same_faith_opinion", "10"), ("clergy_opinion", "8"));

        // --- COURT LADDERS ---
        //
        // court_grandeur_baseline_add is the line that makes a court artifact a court artifact.
        // Vanilla's own values cluster tight — forty uses of 3, eighteen each of 1 and 6, with 10
        // and 16 as the only outliers in the game — so these stay inside 1..6 and the banner, which
        // every court gets for free, sits at the bottom of it.
        Modifier("gen_courtrelic_modifier_common", "piety_positive",
            ("court_grandeur_baseline_add", "1"), ("monthly_piety", "0.15"));
        Modifier("gen_courtrelic_modifier_masterwork", "piety_positive",
            ("court_grandeur_baseline_add", "2"), ("monthly_piety", "0.35"), ("clergy_opinion", "5"));
        Modifier("gen_courtrelic_modifier_famed", "piety_positive",
            ("court_grandeur_baseline_add", "4"), ("monthly_piety", "0.6"),
            ("clergy_opinion", "10"), ("same_faith_opinion", "5"));

        // No banner ladder. Vanilla's own game-start pass already hangs a house banner in every
        // royal court, rendered with the house's real coat of arms — see the note in
        // ArtifactCategory. Generating a rival was three wall slots spent on a worse copy.
        Modifier("gen_courtthrone_modifier_common", "grandeur_positive",
            ("court_grandeur_baseline_add", "2"), ("vassal_opinion", "3"));
        Modifier("gen_courtthrone_modifier_masterwork", "grandeur_positive",
            ("court_grandeur_baseline_add", "4"), ("vassal_opinion", "5"),
            ("short_reign_duration_mult", "-0.15"));
        Modifier("gen_courtthrone_modifier_famed", "grandeur_positive",
            ("court_grandeur_baseline_add", "6"), ("vassal_opinion", "8"),
            ("short_reign_duration_mult", "-0.25"), ("monthly_prestige", "0.15"));

        Modifier("gen_scholar_modifier_common", "learning_positive",
            ("learning", "1"));
        Modifier("gen_scholar_modifier_masterwork", "learning_positive",
            ("learning", "2"), ("learning_lifestyle_xp_gain_mult", "0.1"));
        Modifier("gen_scholar_modifier_famed", "learning_positive",
            ("learning", "3"), ("learning_lifestyle_xp_gain_mult", "0.2"),
            ("monthly_prestige", "0.15"));

        // --- ONE BLOCK PER LEGENDARY ---
        //
        // A shared family base plus the single line that distinguishes this one object, which is
        // how AGOT writes Valyrian steel: every named sword is prowess 9 and dynasty prestige, and
        // then Blackfyre alone carries vassal_limit while Longclaw alone carries a forest
        // advantage. Four fixed legendary keys, which is what this pool used to hold, make the
        // world's great treasures four objects in a hundred costumes.

        var signatures = artifacts.Signatures.ToList();

        if (signatures.Count > 0)
        {
            b.Comment("One-off modifiers, one per legendary artifact in this world.");
            b.Blank();

            foreach (var art in signatures)
            {
                b.Comment(art.LocalizedName);

                using (b.Block(art.Modifier))
                {
                    b.Field("icon", art.ModifierIcon);
                    foreach (var (k, v) in art.ModifierFields!) b.Field(k, v);
                }

                b.Blank();
            }
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
