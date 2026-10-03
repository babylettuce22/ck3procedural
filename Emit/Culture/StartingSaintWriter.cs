using System.Text;
using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// Historical founding saints shared by a generated religion's faiths. The characters die before
/// every bookmark; a one-time startup hook registers their tombs in occupied holdings. This uses
/// the ungated Saint trait and saint registry, leaving DLC decisions and tenets untouched.
/// </summary>
public static class StartingSaintWriter
{
    private const string CharactersPath = "history/characters/00_generated_starting_saints.txt";
    private const string StartupPath = "common/on_action/zz_gen_starting_saints.txt";
    private const string LocalisationPath = "localization/english/zz_gen_starting_saints_l_english.yml";
    private const int Stream = 0x5A17;

    private sealed record Role(string Key, string Epithet, string Trait, string Education);
    private static readonly Role[] Roles =
    [
        new("peacemaker", "the Peacemaker", "forgiving", "education_diplomacy_3"),
        new("teacher", "the Teacher", "patient", "education_learning_3"),
        new("contemplative", "the Contemplative", "temperate", "education_learning_3"),
        new("steward", "the Steward", "generous", "education_stewardship_3"),
        new("defender", "the Defender", "brave", "education_martial_3"),
        new("healer", "the Healer", "compassionate", "education_learning_3")
    ];

    public static void WriteAll(string modDir, MapConfig cfg, FaithMap faiths, CultureMap cultures)
    {
        var characters = new JominiBuilder();
        var startup = new JominiBuilder();
        var loc = new StringBuilder("l_english:\n");
        characters.Comment("Generated founding saints, dead before every bookmark; no dynasty or living claimants.");
        startup.Comment("Registers historical saints once per new campaign, after bookmark holding setup. No DLC gate.");
        using (startup.Block("on_game_start_after_lobby"))
        using (startup.Block("on_actions")) startup.Token("gen_register_starting_saints");
        startup.Blank();
        int count = 0;
        int earliest = cfg.AdditionalBookmarkYears.Append(Math.Max(1, cfg.StartYear)).Min();
        using (startup.Block("gen_register_starting_saints"))
        {
            using (startup.Block("trigger"))
                startup.Inline("NOT", "has_global_variable = gen_starting_saints_initialized");
            using (startup.Block("effect"))
            {
                foreach (var religion in faiths.Religions.Where(r => !r.Inherited
                             && r.Key != Faiths.UnsettledReligionKey).OrderBy(r => r.Key, StringComparer.Ordinal))
                {
                    var members = faiths.Faiths.Where(f => f.Religion == religion && !f.Inherited)
                        .OrderByDescending(f => f.Counties.Count).ThenBy(f => f.Key, StringComparer.Ordinal).ToList();
                    var counties = faiths.ByCounty.Where(p => p.Value.Religion == religion)
                        .Select(p => p.Key).Distinct().OrderBy(c => c.Key, StringComparer.Ordinal).ToList();
                    if (members.Count == 0 || counties.Count == 0) continue;
                    var primary = members[0];
                    // A bounded set: holy sites, county capitals, then other baronies. Runtime
                    // checks account for wilderness and different holdings on other bookmarks.
                    var sites = members.SelectMany(f => f.HolySites).Select(s => s.County.Capital)
                        .Concat(counties.Select(c => c.Capital))
                        .Concat(counties.SelectMany(c => c.SeatFirst()))
                        .Where(b => b is { ProvinceId: > 0 }).Select(b => b!)
                        .DistinctBy(b => b.ProvinceId).Take(24).ToList();
                    if (sites.Count == 0) continue;
                    var culture = cultures.For(counties[0]);
                    var rng = Rng.For(cfg.Seed, Stream, Rng.StableHash(religion.Key));
                    foreach (var role in Roles.Take(Math.Min(Roles.Length, sites.Count)))
                    {
                        string id = $"gen_saint_{religion.Key}_{role.Key}";
                        string nameKey = $"{id}_name";
                        bool female = rng.Chance(0.5);
                        string name = female ? religion.Language.FemaleName(rng) : religion.Language.MaleName(rng);
                        loc.AppendLine($" {nameKey}:0 \"{ParadoxText.Loc(name + ", " + role.Epithet)}\"");
                        using (characters.Block(id))
                        {
                            characters.Quoted("name", nameKey);
                            if (female) characters.Field("female", "yes");
                            characters.Quoted("culture", culture.Key);
                            characters.Quoted("faith", primary.Key);
                            characters.Field("disallow_random_traits", "yes");
                            characters.Field("trait", "saint");
                            // Prefer a genuine virtue of this religion, rather than a personality
                            // that its doctrine condemns; the role still determines education.
                            string virtue = religion.Virtues.Contains(role.Trait) ? role.Trait
                                : religion.Virtues.FirstOrDefault(t => !religion.Sins.Contains(t))
                                  ?? (religion.Sins.Contains(role.Trait) ? "" : role.Trait);
                            if (virtue.Length > 0) characters.Field("trait", virtue);
                            characters.Field("trait", role.Education);
                            // Vanilla historical saints include BC dates. Negative years also
                            // keep these characters dead on a very early generated bookmark.
                            int born = earliest - rng.Int(130, 300);
                            using (characters.Block($"{born}.1.1")) characters.Field("birth", "yes");
                            using (characters.Block($"{born + rng.Int(40, 75)}.1.1")) characters.Field("death", "yes");
                        }
                        characters.Blank();
                        foreach (var site in sites) WriteRegistration(startup, primary, members, id, site);
                        count++;
                    }
                }
                startup.Field("set_global_variable", "gen_starting_saints_initialized");
            }
        }
        if (count == 0)
        {
            foreach (string path in OwnedPaths)
            {
                // A fresh mod folder has no subdirectories yet; File.Delete throws on a missing directory.
                string target = Path.Combine(modDir, path);
                if (File.Exists(target)) File.Delete(target);
            }
            return;
        }
        Ship(modDir, CharactersPath, characters.ToString());
        Ship(modDir, StartupPath, startup.ToString());
        Ship(modDir, LocalisationPath, loc.ToString());
        Console.WriteLine($"  founding saints: {count}; tombs registered at campaign start in occupied holdings");
    }

    private static readonly string[] OwnedPaths = [CharactersPath, StartupPath, LocalisationPath];

    private static void WriteRegistration(JominiBuilder b, Faith primary, List<Faith> members,
        string id, Title site)
    {
        using (b.Block("if"))
        {
            using (b.Block("limit"))
            {
                b.Inline("NOT", $"faith:{primary.Key} = {{ has_saint_character = character:{id} }}");
                using (b.Block($"province:{site.ProvinceId}"))
                {
                    b.Field("has_holding", "yes");
                    using (b.Block("NOR"))
                    {
                        b.Field("has_variable", "hide_location_for_saint");
                        b.Field("has_travel_point_of_interest", "poi_buried_saint");
                    }
                }
            }
            using (b.Block($"faith:{primary.Key}"))
                b.Inline("add_saint", $"character = character:{id} burial_province = province:{site.ProvinceId}");
            // The engine owns the character->province link. Subsequent faith registrations share
            // that tomb, as vanilla does for saints revered by multiple Christian faiths.
            foreach (var member in members.Where(f => f != primary))
                using (b.Block($"faith:{member.Key}"))
                    b.Inline("add_saint", $"character = character:{id}");
            using (b.Block($"province:{site.ProvinceId}"))
            {
                b.Field("set_variable", "pam_preset_buried_saint");
                b.Inline("add_to_global_variable_list", $"name = pam_buried_saint_poi_list target = province:{site.ProvinceId}");
                b.Field("add_travel_point_of_interest", "poi_buried_saint");
            }
        }
    }

    private const string PatronDecisionPath = "common/decisions/dlc_decisions/zzz_generated/zz_gen_patron_saint_decision.txt";
    private const string PatronModifierPath = "common/modifiers/zz_gen_patron_saints.txt";
    private const string PatronLocalisationPath = "localization/english/zz_gen_patron_saints_l_english.yml";
    private const string PatronDecisionKey = "pam_determine_patron_saint_decision";
    private static readonly string[] PatronPaths = [PatronDecisionPath, PatronModifierPath, PatronLocalisationPath];

    // Vanilla's six apostles, by option value, with a neutral name for generated religions.
    private static readonly (string Value, string Name, string Title)[] Patrons =
    [
        ("peter", "the Rock", "Foundation of the Faithful"),
        ("paul", "the Wayfarer", "Teacher of Strangers"),
        ("john", "the Beloved", "Witness of Visions"),
        ("matthew", "the Reckoner", "Who Left the Counting-House"),
        ("mark", "the Lion", "Herald of the Wilderness"),
        ("luke", "the Physician", "Healer of the Faithful")
    ];

    /// <summary>
    /// Vanilla's Select a Patron Saint decision is hidden from every non-Christian
    /// (<c>religion ?= religion:christianity_religion</c> in is_shown), so generated religions
    /// never saw it even with Dulia. The copy shows it to generated-religion members whose rite
    /// permits Dulia or who hold it as a personal tenet. Requirements, DLC gate and AI stay
    /// vanilla. Generated religions get neutral option names and stat-identical gen_ copies of
    /// the six modifiers, since modifier text cannot vary by religion; Christians are unchanged.
    /// The path sits under dlc_decisions/zzz_generated so it loads after pam/pam_decisions.txt.
    /// </summary>
    public static void WritePatronSaints(string modDir, string gameDir, FaithMap faiths)
    {
        var religions = faiths.Religions.Where(r => !r.Inherited && r.Key != Faiths.UnsettledReligionKey)
            .Select(r => r.Key).Distinct().Order(StringComparer.Ordinal).ToList();
        string decisionSource = Path.Combine(gameDir, "common/decisions/dlc_decisions/pam/pam_decisions.txt");
        string modifierSource = Path.Combine(gameDir, "common/modifiers/12_pam_modifiers.txt");
        if (religions.Count == 0 || !File.Exists(decisionSource) || !File.Exists(modifierSource))
        {
            if (religions.Count > 0) Console.WriteLine("  patron saints: SKIPPED (installed PAM decision or modifiers not found)");
            DeletePatronFiles(modDir);
            return;
        }

        string generated = "OR = { " + string.Join(' ', religions.Select(r => $"religion ?= religion:{r}")) + " }";
        string decision;
        var modifiers = new StringBuilder();
        try
        {
            decision = Definition(File.ReadAllText(decisionSource), PatronDecisionKey);
            decision = ReplaceOnce(decision, "religion ?= religion:christianity_religion", $$"""
                OR = {
                			religion ?= religion:christianity_religion
                			AND = {
                				{{generated}}
                				OR = {
                					rite ?= { has_at_least_tenet_status = { tenet = tenet_dulia status = permitted } }
                					has_personal_tenet_flag = tenet_dulia_can_select_patron_saint
                				}
                			}
                		}
                """);
            string modifierText = File.ReadAllText(modifierSource);
            foreach (var (value, _, _) in Patrons)
            {
                decision = ReplaceOnce(decision, $"localization = pam_determine_patron_saint_decision_{value}\n", $$"""
                    localization = {
                    				first_valid = {
                    					triggered_desc = { trigger = { {{generated}} } desc = gen_patron_saint_option_{{value}} }
                    					desc = pam_determine_patron_saint_decision_{{value}}
                    				}
                    			}
                    			current_description = {
                    				first_valid = {
                    					triggered_desc = { trigger = { {{generated}} } desc = gen_patron_saint_{{value}}_tooltip }
                    					desc = {{value}}_tooltip
                    				}
                    			}

                    """);
                decision = ReplaceOnce(decision, $"add_character_modifier = pam_patron_saint_{value}\n", $$"""
                    if = {
                    				limit = { {{generated}} }
                    				add_character_modifier = gen_patron_saint_{{value}}
                    			}
                    			else = { add_character_modifier = pam_patron_saint_{{value}} }

                    """);
                string modifier = Definition(modifierText, $"pam_patron_saint_{value}");
                modifiers.Append("gen").Append(modifier.AsSpan(3)).Append("\n\n");
            }
        }
        catch (InvalidDataException e)
        {
            Console.WriteLine($"  patron saints: SKIPPED ({e.Message})");
            DeletePatronFiles(modDir);
            return;
        }

        var loc = new StringBuilder("l_english:\n");
        foreach (var (value, name, title) in Patrons)
        {
            string full = $"{char.ToUpperInvariant(name[0])}{name[1..]}, {title}";
            loc.AppendLine($" gen_patron_saint_option_{value}:0 \"{ParadoxText.Loc(full)}\"");
            loc.AppendLine($" gen_patron_saint_{value}:0 \"{ParadoxText.Loc("Consecrated to " + name)}\"");
            loc.AppendLine($" gen_patron_saint_{value}_desc:0 \"{ParadoxText.Loc($"This character has chosen {name}, {title}, as their patron saint.")}\"");
            loc.AppendLine($" gen_patron_saint_{value}_tooltip:0 \"[GetModifier('gen_patron_saint_{value}').GetDescWithBulletPointEffects]\\n\\n$CLICK_TO_SELECT$\"");
        }

        Ship(modDir, PatronDecisionPath,
            "# Generated from the installed CK3 decision. Only visibility for generated religions and their\n"
            + "# option text/modifiers differ; Christian behaviour, requirements and DLC gate are vanilla.\n"
            + decision + "\n");
        Ship(modDir, PatronModifierPath,
            "# Stat-identical copies of vanilla's patron saint modifiers, named for generated religions.\n"
            + modifiers);
        Ship(modDir, PatronLocalisationPath, loc.ToString());
        Console.WriteLine($"  patron saints: decision opened to {religions.Count} generated religions with Dulia");
    }

    private static void DeletePatronFiles(string modDir)
    {
        foreach (string path in PatronPaths)
        {
            string target = Path.Combine(modDir, path);
            if (File.Exists(target)) File.Delete(target);
        }
    }

    private static string Definition(string source, string key)
    {
        source = source.Replace("\r\n", "\n");
        var match = System.Text.RegularExpressions.Regex.Match(source,
            $@"(?m)^{System.Text.RegularExpressions.Regex.Escape(key)}\s*=\s*\{{");
        int end = match.Success ? ScriptScan.BlockEnd(source, match.Index) : -1;
        if (end < 0) throw new InvalidDataException($"{key} not found in the installed game");
        return source[match.Index..end];
    }

    private static string ReplaceOnce(string source, string find, string replacement)
    {
        int at = source.IndexOf(find, StringComparison.Ordinal);
        if (at < 0 || source.IndexOf(find, at + find.Length, StringComparison.Ordinal) >= 0)
            throw new InvalidDataException($"installed decision changed; expected one '{find.Trim()}'");
        return source[..at] + replacement + source[(at + find.Length)..];
    }

    private static void Ship(string modDir, string relativePath, string text)
    {
        string path = Path.Combine(modDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        ParadoxText.WriteBom(path, text);
    }
}
