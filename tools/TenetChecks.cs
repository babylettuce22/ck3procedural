using Ck3MapGen.Core;
using Ck3MapGen.Emit;
using Ck3MapGen.GameGui;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Tools;

internal static class TenetChecks
{
    public static int Run(string gameDir)
    {
        string root = Path.Combine(Path.GetTempPath(), "Ck3MapGen-tenets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var emptyMap = new FaithMap { Religions = [], Faiths = [], ByCounty = [] };
            string fresh = Path.Combine(root, "fresh-output");
            InstitutionalFaithWriter.WriteAll(fresh, gameDir, emptyMap);
            TenetWriter.WriteEligibility(fresh, emptyMap);
            Check(!Directory.Exists(fresh), "Fresh outputs with no eligible religions need no cleanup directories");
            string stale = Path.Combine(fresh, "common/spiritual_fulfillment/zz_gen_institutional_fulfillment.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(stale)!);
            File.WriteAllText(stale, "stale");
            InstitutionalFaithWriter.WriteAll(fresh, gameDir, emptyMap);
            Check(!File.Exists(stale), "Institutional cleanup still deletes stale files");
            var vocab = VanillaVocabulary.Read(gameDir);
            var bga = vocab.TenetDefinitions.Where(d => d.Value.DlcFlag == "by_god_alone")
                .Select(d => d.Key).ToList();
            Check(bga.Count > 0 && bga.All(vocab.Tenets.Contains), "Installed BGA tenets are in the pool");
            string[] christologies = ["tenet_adoptionism", "tenet_miaphysitism", "tenet_monophysitism"];
            foreach (string a in christologies)
                foreach (string b in christologies.Where(b => b != a))
                    Check(!vocab.Compatible(a, [b]), $"{a} excludes {b}");

            int branches = 0;
            foreach (string tenet in bga)
                Verify([tenet, "tenet_adaptive", "tenet_ancestor_worship"]);
            for (int i = 0; i < 500; i++)
                Verify(Faiths.SampleCompatible(vocab.Tenets, 3, [], vocab, new Rng(i)));

            void Verify(List<string> tenets)
            {
                var b = new JominiBuilder();
                TenetWriter.WriteTenets(b, "fixture", tenets, []);
                string script = b.ToString();
                var nodes = GuiParser.Parse(script).Roots;
                var plain = nodes.Single(n => n.Key == "tenets").Children.SelectMany(c => c.Head).ToList();
                var pairs = nodes.Where(n => n.Key == "tenet_selection_pair").ToList();
                var flags = pairs.Select(n => n.Field("requires_dlc_flag")!).Distinct().ToList();
                for (int mask = 0; mask < (1 << flags.Count); mask++)
                {
                    var selected = plain.Concat(pairs.Select(n => n.Field(
                        (mask & (1 << flags.IndexOf(n.Field("requires_dlc_flag")!))) != 0
                            ? "tenet" : "fallback_tenet")!)).ToList();
                    Check(selected.Count == tenets.Count && selected.Distinct().Count() == tenets.Count,
                        "Ownership branch retains distinct core slots", quiet: true);
                    Check(selected.All(t => vocab.Compatible(t, selected.Where(o => o != t))),
                        "Ownership branch remains compatible", quiet: true);
                    if (mask == (1 << flags.Count) - 1)
                        Check(selected.Order().SequenceEqual(tenets.Order()), "Owned DLC retains intended tenets", quiet: true);
                    if (mask == 0)
                        Check(selected.All(t => vocab.TenetDefinitions.GetValueOrDefault(t)?.DlcFlag is null),
                            "No DLC branch needs no expansion", quiet: true);
                    branches++;
                }
                var again = new JominiBuilder();
                TenetWriter.WriteTenets(again, "fixture", tenets, []);
                Check(script == again.ToString(), "Deterministic fallback emission", quiet: true);
            }
            Console.WriteLine($"PASS {bga.Count} BGA tenets and {branches} ownership branches");

            var religion = new Religion { Key = "gen_religion_test", Name = "Test", Language = null!,
                GraphicalFaith = "pagan_gfx", Monotheist = true, Abrahamic = true, LayClergy = false,
                Doctrines = [], Virtues = [], Sins = [], CoronationCrown = true,
                Localization = [], LocalizationText = [] };
            var map = new FaithMap { Religions = [religion], Faiths = [], ByCounty = [] };
            TenetWriter.WriteEligibility(root, map);
            string path = Path.Combine(root, "common/religion/tenet_types/zz_gen_bga_tenets.txt");
            var adapted = GuiParser.Parse(File.ReadAllText(path)).Roots.Where(n => n.IsBlock).ToList();
            Check(adapted.Count == bga.Count, "Every installed BGA tenet adapted");
            foreach (var definition in adapted)
            {
                string original = vocab.TenetDefinitions[definition.Key].Script;
                var native = GuiParser.Parse(original).Roots.Single();
                foreach (var nativeField in native.Children.Where(n => n.Key is not ("can_pick" or "can_pick_as_personal_tenet")))
                    Check(definition.ChildrenNamed(nativeField.Key).First().ToString() == nativeField.ToString(),
                        "Untouched native fields stay identical", quiet: true);
                Check(definition.ToString().Contains("religion:gen_religion_test"), "Generated religion admitted", quiet: true);
            }
            Console.WriteLine("PASS Adaptations preserve native DLC gates, costs, parameters and effects");
            var textDefinitions = GuiParser.Parse(File.ReadAllText(Path.Combine(root, TenetWriter.TextPath)))
                .Roots.Where(n => n.IsBlock).ToList();
            string locPath = Path.Combine(root, TenetWriter.LocalisationPath);
            string locText = File.ReadAllText(locPath);
            var locKeys = System.Text.RegularExpressions.Regex.Matches(locText, @"(?m)^\s*(gen_tenet_\w+):0")
                .Select(m => m.Groups[1].Value).ToHashSet();
            Check(textDefinitions.Count == 36 && locKeys.Count == 72, "26 new and 10 older tenets have separate generated text");
            Check(File.ReadAllBytes(locPath).Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }), "Localization has UTF-8 BOM");
            Check(!System.Text.RegularExpressions.Regex.IsMatch(locText,
                @"(?i)\b(Jesus|Christ|Christian|Christianity|Mary|Madonna|Adam|Alexandria|Jerusalem)\b|\$pam_|Glossary\("),
                "Generated text contains no named Christian figures, Bible references or historical links");
            foreach (var definition in textDefinitions)
            {
                var native = adapted.FirstOrDefault(n => n.Key == definition.Key)
                    ?? GuiParser.Parse(vocab.TenetDefinitions[definition.Key].Script).Roots.Single();
                foreach (var nativeField in native.Children.Where(n => n.Key is not ("name" or "desc")))
                    Check(Shape(definition.ChildrenNamed(nativeField.Key).First()) == Shape(nativeField),
                        "Text adaptation preserves effective gameplay fields", quiet: true);
                foreach (string textField in new[] { "name", "desc" })
                {
                    var choices = definition.ChildrenNamed(textField).Single().ChildrenNamed("first_valid").Single();
                    var first = choices.Children.First();
                    Check(first.Key == "triggered_desc" && locKeys.Contains(first.Field("desc")!),
                        "First text choice resolves to a declared generated localization", quiet: true);
                    var checks = first.ChildrenNamed("trigger").Single().ChildrenNamed("OR").Single().Children;
                    Check(checks.Count == 1 && checks[0].Key == "religion"
                        && checks[0].Value == "religion:gen_religion_test", "Text branch admits generated religions only", quiet: true);
                    var nativeChoices = native.ChildrenNamed(textField).Single().ChildrenNamed("first_valid").Single().Children;
                    Check(choices.Children.Skip(1).Select(Shape).SequenceEqual(nativeChoices.Select(Shape)),
                        "Vanilla text alternatives remain intact and in order", quiet: true);
                }
            }
            Console.WriteLine("PASS Generated text changes preserve vanilla fallback choices and gameplay");

            StartingSaintWriter.WritePatronSaints(root, gameDir, map);
            string patronPath = Path.Combine(root, "common/decisions/dlc_decisions/zzz_generated/zz_gen_patron_saint_decision.txt");
            var patron = GuiParser.Parse(File.ReadAllText(patronPath)).Roots.Single(n => n.IsBlock);
            string patronText = patron.ToString();
            var shown = patron.ChildrenNamed("is_shown").Single().ToString();
            Check(shown.Contains("religion:christianity_religion") && shown.Contains("religion:gen_religion_test")
                && shown.Contains("tenet_dulia") && shown.Contains("has_pam_dlc_trigger"),
                "Patron decision shows to Christians and to generated religions with Dulia, DLC gate kept");
            Check(System.Text.RegularExpressions.Regex.Matches(patronText, @"add_character_modifier = gen_patron_saint_\w+").Count == 6
                && System.Text.RegularExpressions.Regex.Matches(patronText, @"add_character_modifier = pam_patron_saint_\w+").Count == 6,
                "Each choice grants a gen_ modifier to generated religions and vanilla's to Christians");
            Check(patron.ChildrenNamed("is_valid_showing_failures_only").Single().ToString().Contains("piety_level"),
                "Patron requirements stay vanilla");
            string patronMods = File.ReadAllText(Path.Combine(root, "common/modifiers/zz_gen_patron_saints.txt"));
            var patronModNodes = GuiParser.Parse(patronMods).Roots.Where(n => n.IsBlock).ToList();
            Check(patronModNodes.Count == 6 && patronModNodes.All(n => n.Key.StartsWith("gen_patron_saint_")),
                "Six stat-identical gen_ patron modifiers");
            string patronLoc = File.ReadAllText(Path.Combine(root, "localization/english/zz_gen_patron_saints_l_english.yml"));
            Check(!System.Text.RegularExpressions.Regex.IsMatch(patronLoc, @"\b(Peter|Paul|John|Matthew|Mark|Luke|Apostle|Gentiles|Evangelist)\b")
                && File.ReadAllBytes(Path.Combine(root, "localization/english/zz_gen_patron_saints_l_english.yml")).Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }),
                "Patron text names no apostle and has a BOM");
            Console.WriteLine("PASS Patron saint decision opened to generated religions with Dulia");
            StartingSaintWriter.WritePatronSaints(root, gameDir, new FaithMap { Religions = [], Faiths = [], ByCounty = [] });
            Check(!File.Exists(patronPath), "Reused outputs remove the stale patron decision");
            // Existing mod adaptations must win over vanilla before text is added.
            WriteFixture("common/religion/tenet_types/zz_gen_great_holy_war_tenets.txt",
                "tenet_armed_pilgrimages = { can_pick = { always = yes } parameters = { retained_mod_parameter } }");
            TenetWriter.WriteEligibility(root, map);
            var layered = GuiParser.Parse(File.ReadAllText(Path.Combine(root, TenetWriter.TextPath)))
                .Roots.Single(n => n.Key == "tenet_armed_pilgrimages");
            Check(layered.ChildrenNamed("parameters").Single().ToString().Contains("retained_mod_parameter"),
                "Existing holy-war override survives text adaptation");
            Check(layered.ChildrenNamed("name").Single().ChildrenNamed("first_valid").Single()
                .Children.Last().Value == "tenet_armed_pilgrimages_name", "Implicit vanilla text gets a fallback");
            try
            {
                TenetWriter.WithTextChoices("changed = { name = { random_valid = { desc = changed_name } } }", "changed", "always = yes");
                throw new Exception("Changed source structure was silently accepted");
            }
            catch (InvalidDataException) { Console.WriteLine("PASS Changed text structures fail with a named error"); }
            TenetWriter.WriteEligibility(root, new FaithMap { Religions = [], Faiths = [], ByCounty = [] });
            Check(!File.Exists(path), "Reused outputs remove stale overrides");
            Check(!File.Exists(Path.Combine(root, TenetWriter.TextPath)), "Reused outputs remove stale text choices");

            void WriteFixture(string relative, string text)
            {
                string target = Path.Combine(root, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllText(target, text);
            }

            void Write(string relative, string text)
            {
                string target = Path.Combine(root, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllText(target, text);
            }
            Write("common/religion/faith_types/00_generated_faiths.txt",
                "fixture = { faith_details = { religion = gen_religion_test color = { 0.1 0.2 0.3 } } main_rite = fixture }");
            var rites = new JominiBuilder();
            using (rites.Block("fixture"))
            {
                rites.Field("faith", "fixture");
                TenetWriter.WriteTenets(rites, "fixture", [bga[0], "tenet_adaptive", "tenet_ancestor_worship"], []);
            }
            Write("common/religion/rite_types/00_generated_rites.txt", rites.ToString());
            Write("map_data/definition.csv", "0;0;0;0;x;x;\n");
            Write("descriptor.mod", "name=\"Tenet fixture\"\n");
            Write("common/landed_titles/00_landed_titles.txt", "k_fixture = { }\n");
            PngWriter.WriteRgb8(Path.Combine(root, "map_data/provinces.png"), 1, 1, [0, 0, 0]);
            var world = LoadedWorld.Open(root);
            var field = world.Entries.Single(e => e.Kind == "Faith" && e.Key == "fixture")
                .Fields.Single(f => f.Name == "tenets");
            Check(field.Read().Contains(bga[0]), "Editor reads intended DLC tenets from the main rite");
            Check(world.Save() == 0, "Opening selection pairs causes no writes");
            field.Write!(bga[1] + " tenet_adaptive tenet_ancestor_worship");
            Check(field.Read().Contains(bga[1]), "Editor updates selection pairs");
            field.Reset!();
            Check(field.Read().Contains(bga[0]) && world.Save() == 0, "Editor reset preserves original selection pairs");
            field.Write!(bga[1] + " tenet_adaptive tenet_ancestor_worship");
            world.Save();
            var reopened = LoadedWorld.Open(root);
            Check(reopened.Entries.Single(e => e.Kind == "Faith").Value("tenets").Contains(bga[1]),
                "Edited DLC tenets survive save and reopen");
            Console.WriteLine("Tenet checks passed. Fixture: " + root);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); Console.Error.WriteLine("Fixture retained: " + root); return 1; }
    }

    private static void Check(bool condition, string label, bool quiet = false)
    {
        if (!condition) throw new Exception(label);
        if (!quiet) Console.WriteLine("PASS " + label);
    }

    private static string Shape(GuiNode node) => string.Join(' ', node.Head) + ":" + node.Value
        + "{" + string.Join(";", node.Children.Select(Shape)) + "}";
}
