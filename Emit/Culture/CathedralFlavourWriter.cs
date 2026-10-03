using System.Text;
using System.Text.RegularExpressions;
using Ck3MapGen.Config;
using Ck3MapGen.Io;

namespace Ck3MapGen.Emit;

/// <summary>
/// Cathedral stories for procedural worlds. Lift the installed event file at its original path
/// so event IDs, contributions, rewards and construction cadence remain vanilla's. VanillaWorld
/// ships neither this override nor its localization. Saints come from the player's faith registry;
/// an old save without registered saints gets a generic subject rather than a missing character.
/// </summary>
public static class CathedralFlavourWriter
{
    internal const string EventsPath = "events/dlc/pam/pam_great_projects_events.txt";
    internal const string LocalisationPath = "localization/replace/english/zz_gen_cathedral_flavour_l_english.yml";
    internal const string CustomLocalisationPath = "common/customizable_localization/zz_gen_cathedral_flavour.txt";

    private static readonly Regex LocLine = new(@"^\s*([\w.\-]+):\d*\s*""(.*)""", RegexOptions.Compiled);

    public static void WriteAll(string modDir, string gameDir, MapConfig cfg)
    {
        if (cfg.ContentSource == MapConfig.ContentSourceMode.VanillaWorld) return;

        var patch = VanillaPatch.Open(gameDir, "cathedral stories", "events", "dlc", "pam", "pam_great_projects_events.txt");
        if (patch is null) return;

        const string saint = "\n\t\tfaith = { random_saint = { save_scope_as = gen_cathedral_saint } }";
        patch.InsertAfter("fresco saint", saint, "pam_great_projects_events.0002 = {", "immediate = {");
        patch.ReplaceBlock("stained glass saint", "character:saint_0001 = {",
            "faith = { random_saint = { save_scope_as = gen_cathedral_saint } }");
        patch.ReplaceEvery("faith event theme", "theme = christian", "theme = faith");
        patch.ReplaceEvery("institutional sermon access", "faith.religion = religion:christianity_religion",
            "gen_institutional_religion_trigger = yes");

        string source = Path.Combine(gameDir, "localization", "english", "great_projects", "pam_great_project_types_l_english.yml");
        var lines = File.ReadLines(source).Select(line => LocLine.Match(line)).Where(m => m.Success)
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value, StringComparer.Ordinal);
        var replacements = Replacements();
        // Catch new general deity references in the installed version as well as the named stories
        // below. Do not rewrite shared Bible/glossary keys: other vanilla events still use them.
        foreach (var (key, text) in lines.Where(p => p.Key.StartsWith("pam_great_projects_events.", StringComparison.Ordinal)))
        {
            string adapted = text.Replace("God's", "[ROOT.Char.GetFaith.HighGodNamePossessive]", StringComparison.Ordinal);
            adapted = Regex.Replace(adapted, @"\b(?:the Lord|our Lord|God)\b", "[ROOT.Char.GetFaith.HighGodName]");
            if (adapted != text) replacements.TryAdd(key, adapted);
        }
        foreach (string key in replacements.Keys)
            if (!lines.ContainsKey(key)) throw new InvalidDataException($"Cathedral story localization changed: missing {key}.");

        // Validate all source anchors before shipping any text that depends on the new saint scope.
        if (!patch.Ship(modDir)) throw new InvalidDataException("Cathedral story event anchors changed; adaptation was not shipped.");
        var loc = new StringBuilder("l_english:\n # Cathedral stories for procedural content only. Choices and rewards retain their native meaning.\n");
        foreach (var (key, text) in replacements.OrderBy(p => p.Key, StringComparer.Ordinal))
            loc.AppendLine($" {key}:0 \"{text}\"");
        loc.AppendLine(" gen_cathedral_saint_name:0 \"[gen_cathedral_saint.GetTitledFirstName]\"");
        loc.AppendLine(" gen_cathedral_saint_fallback:0 \"a revered saint of [ROOT.Char.GetFaith.GetName]\"");
        // Modifiers have no event root when viewed later, so their descriptions are faith-neutral.
        loc.AppendLine(" pam_cathedral_sermon_peace_ruler_modifier_desc:0 \"This ruler was moved by a cathedral sermon on reconciliation and peace.\"");
        loc.AppendLine(" pam_cathedral_sermon_unity_ruler_modifier_desc:0 \"This ruler was moved by a cathedral sermon on fellowship among the faithful.\"");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(modDir, LocalisationPath))!);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(modDir, CustomLocalisationPath))!);
        ParadoxText.WriteBom(Path.Combine(modDir, LocalisationPath), loc.ToString());
        ParadoxText.WriteBom(Path.Combine(modDir, CustomLocalisationPath), """
            # Selected in the fresco/stained-glass event, with a fallback for faiths without saints.
            GenCathedralSaint = {
                type = character
                text = {
                    trigger = { exists = scope:gen_cathedral_saint }
                    localization_key = gen_cathedral_saint_name
                }
                text = {
                    fallback = yes
                    localization_key = gen_cathedral_saint_fallback
                }
            }
            """ + "\n");
    }

    private static Dictionary<string, string> Replacements() => new(StringComparer.Ordinal)
    {
        ["pam_great_projects_events.0002.desc"] = "Our priests teach the traditions of [ROOT.Char.GetFaith.GetName], but sacred images make those teachings visible to all.\\n\\nWith the necessary funds gathered and painters assembled, I must decide which scenes will adorn the vaults, walls, and ceilings.",
        ["pam_great_projects_events.0002.a"] = "Seekers beholding a revelation of [ROOT.Char.GetFaith.HighGodName].",
        ["pam_great_projects_events.0002.b"] = "[ROOT.Char.Custom('GenCathedralSaint')|U] in solitary contemplation.",
        ["pam_great_projects_events.0002.c"] = "The first faithful journeying toward their sacred homeland.",
        ["pam_great_projects_events.0002.d"] = "A vision announcing the blessing of [ROOT.Char.GetFaith.HighGodName].",
        ["pam_great_projects_events.0002.e"] = "[ROOT.Char.Custom('GenCathedralSaint')|U] teaching those who seek wisdom.",
        ["pam_great_projects_events.0002.f"] = "A revered teacher serving the poorest of the faithful.",
        ["pam_great_projects_events.0003.a"] = "A sacred tree honoring the ancestors of [ROOT.Char.GetDynastyNameNoTooltip].",
        ["pam_great_projects_events.0003.a.flavor"] = "Its roots embrace our ancestors, its branches their descendants: a reminder of the duties that bind each generation to the next.",
        ["pam_great_projects_events.0003.d"] = "A window honoring [ROOT.Char.Custom('GenCathedralSaint')], with a perpetual lamp.",
        ["pam_great_projects_events.0003.d.flavor"] = "The lamp and candles will keep the saint's example bright before all who enter.",
        ["pam_great_projects_events.0005.desc.intro"] = "The first stone is laid! Like an acorn preceding an oak, the seat of [owner.GetTitledFirstName] will grow into a monument to [ROOT.Char.GetFaith.HighGodName]. Each stone and timber will speak to the [ROOT.Char.GetFaith.GetAdherentNamePluralNoTooltip] of [owner.GetPrimaryTitle.GetNameNoTier] and beyond.\\n\\n[CharMyFirstNames(founder)|U] generosity in funding the construction will be remembered by every worshiper.",
        ["pam_great_projects_events.0006.desc.intro"] = "a series of wooden stakes, with rope drawn taut between them tracing the sacred halls of the building to come.",
        ["pam_great_projects_events.0007.desc.outro"] = "\\n\\nAs I rest my gaze on a bay of completed vaulting, the stone ribs seem to carry the weight of an entire mountain. Beneath them, the faithful will find shelter and a place to contemplate [ROOT.Char.GetFaith.HighGodName].",
        ["pam_great_projects_events.0007.b"] = "Preach a sermon of repentance and renewal.",
        ["pam_great_projects_events.0008.b.flavor"] = "Our sacred tales are filled with humble souls whose courage overcame impossible odds.",
        ["pam_great_projects_events.0012.t"] = "$game_concept_grand_cathedral$: A Lesson for the Faithful",
        ["pam_great_projects_events.0012.desc"] = "The west face is complete! Its portal stands ready to welcome worshipers. The [cathedral_building.GetName] may take a lifetime to finish, but the [owner.GetFaith.GetAdherentNamePluralNoTooltip] of [owner.GetPrimaryTitle.GetNameNoTier] already gather beneath its arches.\\n\\nI speak of our sacred teachings and the responsibilities we share before [ROOT.Char.GetFaith.HighGodName]. What lesson shall they carry home?",
        ["pam_great_projects_events.0012.a"] = "Let reconciliation overcome hatred.",
        ["pam_great_projects_events.0012.a.flavor"] = "Peace begins with those willing to lay aside a grievance.",
        ["pam_great_projects_events.0012.b"] = "Honor the faithful work of every hand.",
        ["pam_great_projects_events.0012.b.flavor"] = "No great sanctuary rises through idleness; each day's honest labor lays another stone.",
        ["pam_great_projects_events.0012.c"] = "Stand together as one community.",
        ["pam_great_projects_events.0012.c.flavor"] = "Shared purpose binds the faithful more firmly than quarrels divide them.",
        ["pam_great_projects_events.0012.d"] = "Find joy in devotion to [ROOT.Char.GetFaith.HighGodName].",
        ["pam_great_projects_events.0012.d.flavor"] = "Give thanks for what sustains us, and let hope strengthen those who struggle.",
        ["pam_great_projects_events.0012.e"] = "Persevere when the path grows difficult.",
        ["pam_great_projects_events.0012.e.flavor"] = "A worthy undertaking asks patience of every generation that carries it forward.",
        ["pam_great_projects_events.0012.f"] = "Open your hands to those in need.",
        ["pam_great_projects_events.0012.f.flavor"] = "Devotion is made visible when we feed the hungry and shelter the weary.",
        ["pam_great_projects_events.0020.desc"] = "At long last, the sacred emblems are affixed to the spires. They proclaim our devotion to [ROOT.Char.GetFaith.HighGodName] above the roofs of [province.GetName].\\n\\nSince construction began, children became parents, and parents grew old. Their toil, their sweat and sacrifice, are now immortalized in stone.\\n\\nEmpires may fall, but the [cathedral_building.GetName] will stand as a sanctuary for generations of the faithful.",
    };
}
