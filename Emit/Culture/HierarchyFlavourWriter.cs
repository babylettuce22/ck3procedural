using System.Text;
using System.Text.RegularExpressions;
using Ck3MapGen.Config;
using Ck3MapGen.Io;

namespace Ck3MapGen.Emit;

/// <summary>
/// The generated half of taking Christianity off the ecclesiastical government, its sees and its
/// clergy. The hand-written half is the Procedural set's
/// <c>localization/replace/english/zz_gen_hierarchical_l_english.yml</c>.
///
/// <code>
/// Related:
///   BaseFilesToCopy/Procedural/...zz_gen_hierarchical_l_english.yml   the renames and rewrites
///   Emit/Culture/ReligionWriter      gives faiths with sees ecclesiastical_government
///   Emit/Culture/SeeWriter           the See / Hierarch words the renames match
/// </code>
///
/// ---- Articles ----
///
/// The hand-written file renames Ecclesiastical to Hierarchical, Archdiocese to See and
/// Archbishop to Hierarch. Every vanilla string that put "an" in front of one of those concepts
/// now reads "an Hierarch" or "an See". About thirty keys do this, most of them tooltips for the
/// archbishops' own interactions. They are rewritten here from the installed game rather than
/// copied by hand, so a patch that rewords one is picked up on the next generate.
///
/// Keys any hand-written file in the set defines are skipped, so no key is declared twice under
/// replace/.
///
/// ---- The Legation Office ----
///
/// An estate building whose only effect is to make College of Cardinals votes easier through
/// <c>influence_papal_vote_interaction</c>. Vanilla offers that vote only where the Papal State's
/// holder heads the faith or rite; the Core set's
/// <c>zz_gen_influence_synod_vote_interaction.txt</c> also opens it to a generated faith with sees
/// (<c>special_doctrine_gen_clerical_regions</c>), whose Synod elects its head. The gate mirrors
/// that interaction's <c>is_available</c>: electors, and either the Papacy or that doctrine. Any
/// other See's estate would pay treasury for nothing, so it does not see the building. If the
/// interaction's gate changes, change this with it.
///
/// Only for <see cref="MapConfig.ContentSourceMode.Procedural"/>, like the set it completes. A
/// VanillaWorld map has the real Christian faiths, and vanilla's text and building are right there.
/// </summary>
public static class HierarchyFlavourWriter
{
    /// <summary>
    /// Where the Procedural set keeps its hand-written replacements. Every file there, not only
    /// zz_gen_hierarchical: zz_gen_see_electors_l_english.yml rewrites the College of Cardinals
    /// keys, and a key declared twice under replace/ is a collision whichever file wins.
    /// </summary>
    private static readonly string[] HandWritten = ["localization", "replace", "english"];

    private const string OutputFile = "zz_gen_hierarchical_articles_l_english.yml";

    /// <summary>
    /// "an" or "An" directly before a concept the renames now start with a consonant. Matches the
    /// concept link (<c>[archbishop|E]</c>, any of its plural or possessive forms) and the
    /// plain-text substitution (<c>$game_concept_archbishop$</c>). Bishop is left out: "a Bishop"
    /// and "a Prelate" both take "a".
    /// </summary>
    private static readonly Regex AnBeforeRenamed = new(
        @"\b([Aa])n (?=\[(?:archdioce|archbishop|ecclesiastical)\w*\|[eE]\]|\$game_concept_(?:archdioce|archbishop|ecclesiastical)\w*\$)",
        RegexOptions.Compiled);

    /// <summary>One vanilla loc line: key, optional version, and the quoted text.</summary>
    private static readonly Regex LocLine = new(@"^\s*([\w.\-]+):\d*\s*""(.*)""", RegexOptions.Compiled);

    public static void WriteAll(string modDir, string gameDir, MapConfig cfg)
    {
        if (cfg.ContentSource == MapConfig.ContentSourceMode.VanillaWorld)
        {
            Console.WriteLine("  hierarchical clergy: not needed (Content Source VanillaWorld)");
            return;
        }

        WriteArticles(modDir, gameDir);
        WriteLegationGate(modDir, gameDir);
    }

    private static void WriteArticles(string modDir, string gameDir)
    {
        string source = Path.Combine(gameDir, "localization", "english");
        if (!Directory.Exists(source))
        {
            Console.WriteLine("  hierarchical clergy: articles SKIPPED (no english localisation in game folder)");
            return;
        }

        var skip = HandWrittenKeys();
        var fixedLines = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (string file in Directory.EnumerateFiles(source, "*.yml", SearchOption.AllDirectories))
        foreach (string line in File.ReadLines(file))
        {
            var m = LocLine.Match(line);
            if (!m.Success || skip.Contains(m.Groups[1].Value)) continue;

            string text = m.Groups[2].Value;
            string rewritten = AnBeforeRenamed.Replace(text, "$1 ");
            if (rewritten != text) fixedLines[m.Groups[1].Value] = rewritten;
        }

        var sb = new StringBuilder();
        sb.Append("l_english:\n");
        sb.Append(" # Vanilla strings with \"an\" before a concept zz_gen_hierarchical_l_english.yml renamed,\n");
        sb.Append(" # rewritten to \"a\". Generated from the installed game; see Emit/Culture/HierarchyFlavourWriter.cs.\n");
        foreach (var (key, text) in fixedLines) sb.Append($" {key}:0 \"{text}\"\n");

        ParadoxText.WriteBom(Path.Combine(modDir, "localization", "replace", "english", OutputFile), sb.ToString());
        Console.WriteLine($"  hierarchical clergy: {fixedLines.Count} article(s) fixed");
    }

    /// <summary>
    /// The keys the hand-written files define. If the folder is missing, the set is not beside the
    /// executable and nothing can collide with it, so the set is empty.
    /// </summary>
    private static HashSet<string> HandWrittenKeys()
    {
        string dir = Path.Combine([StaticFileWriter.SetDirectory(StaticFileWriter.Procedural), .. HandWritten]);
        if (!Directory.Exists(dir)) return [];

        return Directory.EnumerateFiles(dir, "*.yml")
            .SelectMany(File.ReadLines)
            .Select(l => LocLine.Match(l))
            .Where(m => m.Success)
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// <c>can_construct_potential</c> on the first tier only: the higher tiers each need the one
    /// below as <c>previous_building</c>, so they cannot be reached without it. The trigger is
    /// vanilla's own test for the same interaction (<c>influence_papal_vote_interaction</c>), so a
    /// faith with a Pope still gets the building.
    /// </summary>
    private static void WriteLegationGate(string modDir, string gameDir)
    {
        var patch = VanillaPatch.Open(gameDir, "legation office",
            "common", "domiciles", "buildings", "11_ecclesiastical_domicile_buildings.txt");
        if (patch is null) return;

        patch.InsertAfter("chancery_legation_01 previous_building",
            "\n\t# Its only effect is on College of Cardinals votes. Shown only where the vote can be\n" +
            "\t# lobbied: the Papacy, or a generated Synod. See Emit/Culture/HierarchyFlavourWriter.cs.\n" +
            "\tcan_construct_potential = {\n" +
            "\t\tfaith ?= { has_doctrine_parameter = has_clerical_electors }\n" +
            "\t\tOR = {\n" +
            "\t\t\tpam_pope_is_head_of_faith_or_rite_trigger = yes\n" +
            "\t\t\tfaith ?= { has_doctrine = special_doctrine_gen_clerical_regions }\n" +
            "\t\t}\n" +
            "\t}\n",
            "chancery_legation_01 = {", "previous_building = chancery_01\n");

        patch.Ship(modDir);
    }
}
