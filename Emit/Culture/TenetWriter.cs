using System.Text.RegularExpressions;
using Ck3MapGen.Core;
using Ck3MapGen.GameGui;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>Adapts installed tenets and keeps DLC ownership decisions in CK3, not the generator.</summary>
public static class TenetWriter
{
    private const string AdaptationPath = "common/religion/tenet_types/zz_gen_bga_tenets.txt";
    internal const string TextPath = "common/religion/tenet_types/zzz_gen_tenet_text.txt";
    internal const string LocalisationPath = "localization/english/gen_tenets_l_english.yml";

    public static void WriteEligibility(string modDir, FaithMap faiths)
    {
        var religions = faiths.Religions.Where(r => !r.Inherited).Select(r => r.Key)
            .Distinct().Order(StringComparer.Ordinal).ToList();
        var definitions = VanillaVocabulary.Current?.TenetDefinitions;
        string target = Path.Combine(modDir, AdaptationPath);
        if (religions.Count == 0 || definitions is null)
        {
            if (File.Exists(target)) File.Delete(target);
            string textTarget = Path.Combine(modDir, TextPath);
            if (File.Exists(textTarget)) File.Delete(textTarget);
            return;
        }

        string eligibility = "OR = { religion = religion:christianity_religion "
            + string.Join(' ', religions.Select(r => $"religion = religion:{r}")) + " }";
        var adapted = new List<string>();
        foreach (var (_, definition) in definitions.OrderBy(d => d.Key, StringComparer.Ordinal))
        {
            if (definition.DlcFlag != "by_god_alone") continue;
            int changes = 0;
            string script = string.Join('\n', definition.Script.Split('\n').Select(line =>
            {
                // Match script only: examples in comments must never become live eligibility.
                string code = ScriptScan.StripComment(line);
                string rewritten = Regex.Replace(code, @"\breligion\s*=\s*religion:christianity_religion\b", _ =>
                {
                    changes++;
                    return eligibility;
                });
                return rewritten + line[code.Length..];
            }));
            // A future tenet already open to other religions needs no override.
            if (changes > 0) adapted.Add(script);
        }

        if (adapted.Count == 0) { if (File.Exists(target)) File.Delete(target); }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            ParadoxText.WriteBom(target,
                "# Generated from installed CK3 tenets. Only Christian religion checks are widened.\n"
                + "# DLC gates, costs, parameters, personal-tenet restrictions and effects remain vanilla.\n"
                + string.Join("\n\n", adapted) + "\n");
        }
        WriteText(modDir, religions, definitions);
        Console.WriteLine($"  tenets: {adapted.Count} installed By God Alone definitions opened to {religions.Count} generated religions (DLC gates preserved)");
    }

    private static void WriteText(string modDir, List<string> religions,
        Dictionary<string, VanillaVocabulary.TenetDefinition> definitions)
    {
        string localisation = File.ReadAllText(Path.Combine(StaticFileWriter.SetDirectory(StaticFileWriter.Core), LocalisationPath));
        var keys = Regex.Matches(localisation, @"(?m)^\s*gen_(tenet_\w+)_name:0")
            .Select(m => m.Groups[1].Value).Where(definitions.ContainsKey).ToHashSet(StringComparer.Ordinal);
        var effective = keys.ToDictionary(k => k, k => definitions[k].Script, StringComparer.Ordinal);
        string directory = Path.Combine(modDir, "common/religion/tenet_types");
        if (Directory.Exists(directory))
            foreach (string path in Directory.GetFiles(directory, "*.txt").Order(StringComparer.Ordinal))
            {
                if (Path.GetFileName(path) == Path.GetFileName(TextPath)) continue;
                // Layer our text onto the winning mod definition, including the existing holy-war
                // adaptations, rather than accidentally restoring vanilla gameplay restrictions.
                foreach (var node in GuiParser.Parse(File.ReadAllText(path)).Roots)
                    if (node.IsBlock && keys.Contains(node.Key)) effective[node.Key] = node.ToString();
            }

        string trigger = "OR = { " + string.Join(' ', religions.Select(r => $"religion = religion:{r}")) + " }";
        var adapted = new List<string>();
        foreach (var (key, source) in effective.OrderBy(d => d.Key, StringComparer.Ordinal))
            adapted.Add(WithTextChoices(source, key, trigger));
        Directory.CreateDirectory(directory);
        ParadoxText.WriteBom(Path.Combine(modDir, TextPath),
            "# Generated-religion text choices, layered onto the effective installed/mod tenets.\n"
            + "# Vanilla text remains the fallback; gameplay and DLC requirements are unchanged.\n"
            + string.Join("\n\n", adapted) + "\n");
        string locTarget = Path.Combine(modDir, LocalisationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(locTarget)!);
        ParadoxText.WriteBom(locTarget, localisation);
        Console.WriteLine($"  tenet text: {adapted.Count} generated-religion names and descriptions");
    }

    internal static string WithTextChoices(string source, string key, string trigger)
    {
        var document = GuiParser.Parse(source);
        var definition = document.Roots.Single(n => n.IsBlock && n.Key == key);
        foreach (string field in new[] { "name", "desc" })
        {
            var fields = definition.ChildrenNamed(field).ToList();
            if (fields.Count > 1)
                throw new InvalidDataException($"Tenet text adaptation: {key} has multiple {field} fields.");
            var existing = fields.SingleOrDefault();
            if (existing is null)
            {
                existing = GuiParser.Parse($"{field} = {{ first_valid = {{ desc = {key}_{field} }} }}").Roots.Single();
                definition.Add(existing);
            }
            var choices = existing.ChildrenNamed("first_valid").ToList();
            if (!existing.IsBlock || choices.Count != 1 || !choices[0].IsBlock)
                throw new InvalidDataException($"Tenet text adaptation: {key}.{field} no longer has the expected first_valid structure.");
            var condition = GuiParser.Parse(trigger).Roots.Single();
            condition.LeadingTrivia = null;
            var choice = GuiNode.Block("triggered_desc", "=")
                .Add(GuiNode.Block("trigger", "=").Add(condition))
                .Add(GuiNode.Leaf("desc", $"gen_{key}_{field}"));
            choices[0].InsertFirst(choice);
        }
        return document.Print();
    }

    /// <summary>
    /// Vanilla's selection pairs work on both faiths and rites. Fallbacks are distinct, require
    /// no DLC, and coexist with every selected tenet and earlier fallback, so mixed ownership
    /// also yields a compatible set. Never mutate the intended tenets in the world model.
    /// </summary>
    internal static void WriteTenets(JominiBuilder b, string key, IReadOnlyList<string> tenets,
        IEnumerable<string> doctrines)
    {
        var vocab = VanillaVocabulary.Current;
        string? Flag(string t) => vocab?.TenetDefinitions.GetValueOrDefault(t)?.DlcFlag;
        b.Inline("tenets", tenets.Where(t => Flag(t) is null).ToArray());
        if (vocab is null) return;

        var held = doctrines.Concat(tenets).ToList();
        var pool = vocab.Tenets.Where(t => Flag(t) is null
                && t != "tenet_natural_primitivism" && !t.EndsWith("_syncretism", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal).ToList();
        var rng = Rng.For(0, 0x7E4E, Rng.StableHash(key));
        rng.Shuffle(pool);
        foreach (string tenet in tenets.Where(t => Flag(t) is not null))
        {
            string? fallback = pool.FirstOrDefault(t => !held.Contains(t) && vocab.Compatible(t, held));
            if (fallback is null)
                throw new InvalidDataException($"No compatible base-game fallback for {tenet} on {key}.");
            held.Add(fallback);
            using (b.Block("tenet_selection_pair"))
            {
                b.Field("requires_dlc_flag", Flag(tenet)!);
                b.Field("tenet", tenet);
                b.Field("fallback_tenet", fallback);
            }
        }
    }
}
