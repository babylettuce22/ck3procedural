using Ck3MapGen.Config;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// Writes the per-world half of the inversion cult: which faith it hollows out, whose name it
/// serves, who is sworn, and exactly which of the host's doctrines the Unveiling turns over. The rest
/// is the static set in <c>BaseFilesToCopy/Societies</c> (every <c>cult_*</c> file), which reads this
/// only through global variables and the effects and triggers written here — the same contract as
/// <see cref="RestorationWriter"/>.
///
/// Generated because they are the host's, not the cult's:
/// <list type="bullet">
/// <item><c>cult_sinner_trigger</c> — has one of the host religion's sins. Recruitment and seeding weight on it.</item>
/// <item><c>cult_unveil_doctrines_effect</c> / <c>cult_purify_doctrines_effect</c> — the inversion and its
/// undoing, doctrine by doctrine, from what this host actually holds.</item>
/// <item><c>cult_world_setup_effect</c> — the host, the phase, the sworn, the Hierophant.</item>
/// </list>
/// </summary>
internal static class CultWriter
{
    private static readonly string[] Owned =
    [
        Path.Combine("common", "scripted_effects", "zz_gen_cult_setup_effects.txt"),
        Path.Combine("common", "scripted_triggers", "zz_gen_cult_triggers.txt"),
        Path.Combine("common", "on_action", "zz_gen_cult_on_actions.txt"),
        Path.Combine("localization", "english", "gen_cult_l_english.yml"),
    ];

    public static void WriteAll(string modDir, MapConfig cfg, CultPlan? plan)
    {
        foreach (var rel in Owned)
        {
            var path = Path.Combine(modDir, rel);
            if (File.Exists(path)) File.Delete(path);
        }

        WriteEffects(modDir, cfg, plan);
        WriteTriggers(modDir, plan);
        WriteOnAction(modDir);
        WriteLocalisation(modDir, plan);
    }

    private static void WriteEffects(string modDir, MapConfig cfg, CultPlan? plan)
    {
        var b = new JominiBuilder();
        b.Comment("The inversion cult's starting state and its doctrine inversion, for this world only.\n"
            + "Written by Emit/Societies/CultWriter.cs. Read by the static cult_* set through global\n"
            + "variables; see BaseFilesToCopy/Societies/README.txt.");
        b.Blank();

        using (b.Block("cult_world_setup_effect"))
        {
            if (plan is null)
            {
                b.Comment("No faith in this world fits: none large enough that names a devil.\n"
                    + "Leaving global_var:cult_host_faith unset switches the whole cult off.");
                b.Field("set_global_variable", "cult_absent");
            }
            else
            {
                using (StartGate.LatestOnly(b, cfg))
                {
                    void Global(string name, string value)
                    {
                        using (b.Block("set_global_variable"))
                        {
                            b.Field("name", name);
                            b.Field("value", value);
                        }
                    }

                    Global("cult_host_faith", $"faith:{plan.Host.Key}");
                    Global("cult_phase", "1");

                    foreach (var m in plan.Members)
                        using (b.Block($"character:{m.Id}"))
                        using (b.Block("cult_swear_in_effect"))
                            b.Field("RANK", m.Rank);

                    using (b.Block($"character:{plan.HierophantId}"))
                        b.Field("cult_make_hierophant_effect", "yes");

                    b.Field("cult_world_started_effect", "yes");
                }
            }
        }
        b.Blank();

        // The inversion, and its undoing -- decided at RUN time from what the faith holds then, not
        // baked from what it held at generation. Vanilla moves exactly these groups in play (the
        // gender-equality game rule rewrites adultery at game start; the theology lifestyle moves
        // witchcraft, kinslaying and adultery to accepted), and a baked `remove X / add Y` then either
        // removed a doctrine no longer held and doubled the group, or purified a faith back past a
        // legitimate change. So each group checks what is held, inverts it, and remembers it in
        // global_var:cult_was_<group>; the Purification restores exactly that, and only where the
        // inverted doctrine is still the one held. The groups are the ones this host could invert at
        // generation, so $cult_unveil_list$ stays the promise the Unveiling keeps.
        List<(string Group, string To, string[] Invert)> groups = plan is null ? [] : InversionCult.InversionTable
            .Where(g => plan.Inversions.Any(i => i.Group == g.Group)).ToList();

        using (b.Block("cult_unveil_doctrines_effect"))
        {
            if (groups.Count == 0) b.Field("custom_tooltip", "cult_unveil_nothing_tt");
            else
                using (b.Block("global_var:cult_host_faith"))
                    foreach (var (group, to, invert) in groups)
                    {
                        bool first = true;
                        foreach (string held in invert)
                        {
                            using (b.Block(first ? "if" : "else_if"))
                            {
                                using (b.Block("limit")) b.Field("has_doctrine", held);
                                b.Field("remove_doctrine", held);
                                using (b.Block("set_global_variable"))
                                {
                                    b.Field("name", $"cult_was_{group}");
                                    b.Field("value", $"flag:{held}");
                                }
                                b.Field("add_doctrine", to);
                            }
                            first = false;
                        }
                    }
        }
        b.Blank();

        using (b.Block("cult_purify_doctrines_effect"))
        {
            if (groups.Count == 0) b.Field("custom_tooltip", "cult_unveil_nothing_tt");
            else
                using (b.Block("global_var:cult_host_faith"))
                    foreach (var (group, to, invert) in groups)
                    {
                        using (b.Block("if"))
                        {
                            using (b.Block("limit"))
                            {
                                b.Field("exists", $"global_var:cult_was_{group}");
                                b.Field("has_doctrine", to);
                            }
                            b.Field("remove_doctrine", to);
                            bool first = true;
                            foreach (string held in invert)
                            {
                                using (b.Block(first ? "if" : "else_if"))
                                {
                                    using (b.Block("limit")) b.Field($"global_var:cult_was_{group}", $"flag:{held}");
                                    b.Field("add_doctrine", held);
                                }
                                first = false;
                            }
                        }
                        using (b.Block("if"))
                        {
                            using (b.Block("limit")) b.Field("exists", $"global_var:cult_was_{group}");
                            b.Field("remove_global_variable", $"cult_was_{group}");
                        }
                    }
        }

        Write(modDir, Owned[0], b.ToString());
    }

    private static void WriteTriggers(string modDir, CultPlan? plan)
    {
        var b = new JominiBuilder();
        b.Comment("The host religion's sins, as a trigger. Written by Emit/Societies/CultWriter.cs:\n"
            + "what the host calls sin is what the cult looks for in a recruit.");
        b.Blank();
        using (b.Block("cult_sinner_trigger"))
        {
            if (plan is null || plan.Sins.Count == 0) b.Field("always", "no");
            else
                using (b.Block("OR"))
                    foreach (var sin in plan.Sins) b.Field("has_trait", sin);
        }
        Write(modDir, Owned[1], b.ToString());
    }

    private static void WriteOnAction(string modDir)
    {
        var o = new JominiBuilder();
        o.Comment("Seeds the inversion cult. Written by Emit/Societies/CultWriter.cs. on_game_start, so the\n"
            + "cult's trait is on its members before the player picks a character.");
        o.Blank();
        using (o.Block("on_game_start"))
        using (o.Block("on_actions"))
            o.Token("gen_cult_game_start");
        o.Blank();
        using (o.Block("gen_cult_game_start"))
        using (o.Block("effect"))
            o.Field("cult_world_setup_effect", "yes");
        Write(modDir, Owned[2], o.ToString());
    }

    /// <summary>The cult's clergy keys: the host religion's tag each points at, and the English fallback.</summary>
    private static readonly (string Key, string Tag, string English)[] ClergyWords =
    [
        ("cult_priest", "PriestNeuter", "priest"),
        ("cult_priests", "PriestNeuterPlural", "priests"),
        ("cult_priest_male", "PriestMale", "priest"),
        ("cult_priest_female", "PriestFemale", "priestess"),
        ("cult_bishop", "BishopNeuter", "bishop"),
    ];

    private static void WriteLocalisation(string modDir, CultPlan? plan)
    {
        var loc = new LocFile();
        if (plan is null)
        {
            loc.Add("cult_society_name", "The Cult");
            loc.Add("cult_unveil_list", "");
            loc.Add("cult_unveil_nothing_tt", "The faith's doctrines are unchanged.");
            foreach (var (key, _, english) in ClergyWords) loc.Add(key, english);
            loc.Write(Path.Combine(modDir, Owned[3]));
            return;
        }

        string devil = $"${plan.DevilKey}$";
        string faith = $"${plan.Host.Key}$";

        // The host's own words for its clergy, by loc key like the devil's, so the prose says "Twaitru"
        // where it means priest and carries the religion's hover gloss (Emit/ReligionGlossary.cs)
        // anywhere it is printed: events, decisions, interactions and the cult panel alike.
        foreach (var (key, tag, english) in ClergyWords)
        {
            string? word = plan.Host.Religion.Localization.FirstOrDefault(t => t.Tag == tag).Value;
            if (word is { Length: > 0 } && plan.Host.Religion.LocalizationText.ContainsKey(word))
                loc.AddBuilt(key, $"${word}$");
            else
                loc.Add(key, english);
        }

        loc.AddBuilt("cult_society_name", plan.NamePattern switch
        {
            0 => $"The Children of {devil}",
            1 => $"The {plan.HolyWord}",
            2 => "The Hollow Choir",
            3 => "The Order of the Black Candle",
            _ => $"The Sworn of {devil}",
        });
        loc.AddBuilt("cult_society_name_public", $"The Unveiled Church of {devil}");
        loc.AddBuilt("cult_devil_name", devil);
        loc.AddBuilt("cult_host_name", faith);

        // What the Unveiling does, in words, for its decision and its events.
        var lines = new List<string>();
        foreach (var inv in plan.Inversions)
        {
            string? line = inv.Group switch
            {
                "doctrine_witchcraft" => "Witchcraft becomes holy.",
                "doctrine_deviancy" => "Deviancy becomes a virtue.",
                "doctrine_kinslaying" => "Killing one's kin is no longer a sin.",
                "doctrine_adultery_men" or "doctrine_adultery_women" => "Adultery is no longer a sin.",
                "doctrine_consanguinity" => "Marriage between close kin is allowed.",
                _ => null,
            };
            if (line is not null && !lines.Contains(line)) lines.Add(line);
        }
        loc.AddBuilt("cult_unveil_list", lines.Count == 0 ? "Its doctrines stay as they are." : string.Join(" ", lines));
        loc.Add("cult_unveil_nothing_tt", "The faith's doctrines are unchanged.");

        loc.Write(Path.Combine(modDir, Owned[3]));
    }

    private static void Write(string modDir, string rel, string text)
    {
        var path = Path.Combine(modDir, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        ParadoxText.WriteBom(path, text);
    }
}
