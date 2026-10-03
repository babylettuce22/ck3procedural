using System.Text.RegularExpressions;
using Ck3MapGen.Config;
using Ck3MapGen.Emit;
using Ck3MapGen.GameGui;

namespace Ck3MapGen.Tools;

internal static class CathedralChecks
{
    /// <summary>Fixture checks against the installed game, optionally retained for Tiger/EventFlow.</summary>
    public static int Run(string gameDir, string? output = null)
    {
        string root = output ?? Path.Combine(Path.GetTempPath(), "Ck3MapGen-cathedral-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string vanilla = Path.Combine(root, "vanilla-content");
            CathedralFlavourWriter.WriteAll(vanilla, gameDir,
                new MapConfig { ContentSource = MapConfig.ContentSourceMode.VanillaWorld });
            Check(!Directory.Exists(vanilla), "Vanilla content emits no cathedral adaptation");

            string adapted = Path.Combine(root, "adapted");
            CathedralFlavourWriter.WriteAll(adapted, gameDir, new MapConfig());
            string eventPath = Path.Combine(adapted, CathedralFlavourWriter.EventsPath);
            string script = File.ReadAllText(eventPath);
            string original = File.ReadAllText(Path.Combine(gameDir, CathedralFlavourWriter.EventsPath)).Replace("\r\n", "\n");
            var before = GuiParser.Parse(original).Roots;
            var after = GuiParser.Parse(script).Roots;
            Check(before.Select(n => n.Key).SequenceEqual(after.Select(n => n.Key)), "All native events retained in order");
            foreach (var (native, patched) in before.Zip(after))
            {
                // Compare the source spans of entire option blocks, including nested rewards,
                // conditions, costs, AI weights and follow-up events, rather than individual values.
                var nativeOptions = native.Children.Where(n => n.Key == "option").Select(n => n.ToString()).ToList();
                var patchedOptions = patched.Children.Where(n => n.Key == "option").Select(n => n.ToString()).ToList();
                Check(nativeOptions.SequenceEqual(patchedOptions), $"{native.Key}: choices and rewards preserved");
            }
            Check(!script.Contains("character:saint_0001") && !script.Contains("blessed_mary"), "Mary's historical character dependency removed");
            foreach (string key in new[] { "pam_great_projects_events.0002", "pam_great_projects_events.0003" })
                Check(after.Single(n => n.Key == key).Children.Single(n => n.Key == "immediate").Children
                    .Any(n => n.Key == "faith" && n.Children.Any(c => c.Key == "random_saint")), $"{key}: saint drawn from actor's faith");
            Check(after.Single(n => n.Key == "pam_great_projects_events.0012").Children.Single(n => n.Key == "trigger")
                .Field("gen_institutional_religion_trigger") == "yes", "Sermon available to institutional faiths");
            string custom = File.ReadAllText(Path.Combine(adapted, CathedralFlavourWriter.CustomLocalisationPath));
            Check(custom.Contains("exists = scope:gen_cathedral_saint") && custom.Contains("fallback = yes"), "Saint name is guarded and has an empty-registry fallback");
            string loc = File.ReadAllText(Path.Combine(adapted, CathedralFlavourWriter.LocalisationPath));
            Check(!Regex.IsMatch(loc, @"\b(Christ|Jesus|Mary|Marian|Jesse|Jonah|Goliath|Christendom|Jerome|Augustine|Abraham|Apostles|RandomBibleQuote)\b"), "Adapted stories contain no Christian figures or Bible quote calls");
            Check(loc.Contains("HighGodName") && loc.Contains("GenCathedralSaint"), "Stories use the faith's deity and saints");
            foreach (string path in new[] { CathedralFlavourWriter.EventsPath, CathedralFlavourWriter.LocalisationPath, CathedralFlavourWriter.CustomLocalisationPath })
                Check(File.ReadAllBytes(Path.Combine(adapted, path)).Take(3).SequenceEqual(new byte[] { 239, 187, 191 }), $"UTF-8 BOM: {path}");

            string control = Path.Combine(root, "control", CathedralFlavourWriter.EventsPath);
            Directory.CreateDirectory(Path.GetDirectoryName(control)!);
            File.Copy(Path.Combine(gameDir, CathedralFlavourWriter.EventsPath), control, overwrite: true);
            Console.WriteLine($"Cathedral checks passed. Fixture: {root}");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { if (output is null) Directory.Delete(root, recursive: true); }
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        Console.WriteLine("  PASS " + description);
    }
}
