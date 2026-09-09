using System.Security.Cryptography;
using System.Text;
using Ck3MapGen.Core;
using Ck3MapGen.Io;

namespace Ck3MapGen.Tools;

/// <summary>Exercises real file edits in a disposable fixture. An optional existing mod is opened
/// read-only and no-op saved, checking bytes and timestamps of every loaded file.</summary>
internal static class WorldEditorChecks
{
    public static int Run(string? existing)
    {
        string root = Path.Combine(Path.GetTempPath(), "Ck3MapGen-world-editor-" + Guid.NewGuid().ToString("N"));
        string mod = Path.Combine(root, "fixture");
        try
        {
            void Write(string file, string text, bool bom = true)
            {
                string path = Path.Combine(mod, file);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, text, new UTF8Encoding(bom));
            }
            Write("descriptor.mod", "name=\"Fixture\"\r\n", false);
            Write("common/landed_titles/00_landed_titles.txt", "# Preserve this comment\r\nk_test = { color = { 100 120 140 }\r\n\tcapital = c_test\r\n\tc_test = { color = { 150 160 170 } b_test = { province = 1 color = { 1 2 3 } } } }\r\nk_other = { color = { 1 1 1 } }\r\n");
            Write("common/culture/cultures/00_generated_cultures.txt", "gen_culture_0 = { color = { 10 20 30 } name_list = name_list_gen_culture_0 ethos = ethos_bellicose traditions = { tradition_welcoming # keep me\r\n tradition_forest_folk } unknown_future_field = yes }\r\ngen_culture_1 = { color = { 40 50 60 } ethos = ethos_communal }\r\n");
            Write("history/titles/00_generated_titles.txt", "k_test = {\r\n\t900.1.1 = {\r\n\t\tholder = gen_char_0\r\n\t\tgovernment = feudal_government\r\n\t}\r\n}\r\nc_test = {\r\n\t900.1.1 = {\r\n\t\tholder = gen_char_0\r\n\t\tgovernment = feudal_government\r\n\t\tchange_development_level = 3\r\n\t}\r\n}\r\n");
            Write("common/dynasties/00_generated_dynasties.txt", "gen_dynasty_0 = {\r\n\tname = \"dynn_gen_0\"\r\n\tculture = \"gen_culture_0\"\r\n}\r\n");
            Write("common/dynasty_houses/00_generated_houses.txt", "house_gen_0 = {\r\n\tname = \"dynn_gen_0\"\r\n\tdynasty = gen_dynasty_0\r\n}\r\n");
            Write("common/religion/holy_site_types/01_generated_holy_sites.txt", "gen_hs_c_test = {\r\n\tcounty = c_test\r\n\tcharacter_modifier = { monthly_piety_gain_mult = 0.1 }\r\n}\r\n");
            Write("common/coat_of_arms/coat_of_arms/00_generated_coas.txt", "gen_dynasty_0 = {\r\n\tpattern = \"pattern_solid.dds\"\r\n\tcolor1 = \"red\"\r\n\tcolor2 = \"blue\"\r\n\tcolored_emblem = {\r\n\t\ttexture = \"ce_fleur.dds\"\r\n\t\tcolor1 = \"white\"\r\n\t\tinstance = { position = { 0.5 0.5 } scale = { 0.75 0.75 } }\r\n\t}\r\n}\r\nhouse_gen_0 = {\r\n\tpattern = \"pattern_solid.dds\"\r\n\tcolor1 = \"red\"\r\n\tcolor2 = \"blue\"\r\n\tcolored_emblem = {\r\n\t\ttexture = \"ce_fleur.dds\"\r\n\t\tcolor1 = \"white\"\r\n\t\tinstance = { position = { 0.5 0.5 } scale = { 0.75 0.75 } }\r\n\t}\r\n}\r\n");
            Write("common/culture/name_lists/00_generated_name_lists.txt", "name_list_gen_culture_0 = {\r\n\tmale_names = { cul_a cul_b }\r\n\tfemale_names = { cul_c }\r\n}\r\n");
            Write("map_data/default.map", "definitions = \"definition.csv\"\r\nprovinces = \"provinces.png\"\r\n", false);
            Write("common/religion/religion_types/00_generated_religions.txt", "gen_religion_0 = { doctrine = doctrine_monogamy faiths = { gen_faith_0 = { color = { 0.5 0.2 0.8 } doctrine = tenet_pacifism holy_site = gen_hs_c_test } } }\r\n");
            Write("history/characters/00_generated_characters.txt", "gen_char_0 = { name=\"Álvar\" dynasty_house=house_gen_0 martial=8 culture=gen_culture_0 religion=gen_faith_0 trait=brave 860.1.1={birth=yes} 900.1.1={effect={add_gold=50 add_prestige=75 add_diplomacy_lifestyle_perk_points=2 dynasty={add_dynasty_prestige=10}}} }\r\n");
            Write("history/provinces/00_generated_provinces.txt", "1 = { culture = gen_culture_0 religion = gen_faith_0 holding = castle_holding special_building_slot = wonder_slot_01 }\r\n", false);
            Write("common/bookmarks/bookmarks/00_bookmarks.txt", "bm_generated = { start_date = 900.1.1 character = { name = \"bm_char_test\" history_id = gen_char_0 culture = gen_culture_0 religion = gen_faith_0 birth = 860.1.1 } }\r\n");
            Write("localization/english/gen_test_l_english.yml", "l_english:\r\n k_test:0 \"Old Kingdom\" # retain comment\r\n c_test:0 \"Old County\"\r\n b_test:0 \"Old Castle\"\r\n gen_culture_0:0 \"Old Culture\"\r\n gen_religion_0:0 \"Old Religion\"\r\n gen_faith_0:0 \"Old Faith\"\r\n bm_char_test:0 \"Álvar $nick_the_brave$\"\r\n dynn_gen_0:0 \"Old House\"\r\n cul_a:0 \"Aldo\"\r\n cul_b:0 \"Bram\"\r\n cul_c:0 \"Cara\"\r\n");
            Write("localization/english/gen_title_tiers_l_english.yml", "l_english:\r\n gen_flav_gen_culture_0_0_kingdom:0 \"Tsardom\"\r\n gen_flav_gen_culture_0_0_kingdom_male:0 \"Tsar\"\r\n gen_flav_gen_culture_0_0_kingdom_female:0 \"Tsaritsa\"\r\n gen_flav_gen_culture_0_1_kingdom:0 \"Khanate\"\r\n gen_flav_gen_culture_0_1_kingdom_male:0 \"Khan\"\r\n gen_flav_gen_culture_0_1_kingdom_female:0 \"Khatun\"\r\n gen_flav_k_other_kingdom:0 \"League\"\r\n gen_flav_k_other_kingdom_male:0 \"Doge\"\r\n");
            Write("common/flavorization/zz_generated_flavorization.txt", "gen_flav_gen_culture_0_0_kingdom = {\r\n\ttype = title\r\n\ttier = kingdom\r\n\tgovernments = { feudal_government clan_government }\r\n}\r\ngen_flav_gen_culture_0_1_kingdom = {\r\n\ttype = title\r\n\ttier = kingdom\r\n\tgovernments = { tribal_government }\r\n}\r\n");
            Write("map_data/definition.csv", "0;0;0;0;x;x\n1;10;20;30;test;x\n", false);
            PngWriter.WriteRgb8(Path.Combine(mod, "map_data", "provinces.png"), 2, 1, [10, 20, 30, 10, 20, 30]);
            Write("unrelated.bin", "Never change this file", false);

            var original = Snapshot(mod);
            var world = LoadedWorld.Open(mod);
            Check(world.Save() == 0, "No-op save writes nothing");
            Same(original, Snapshot(mod), "No-op byte/timestamp preservation");
            Check(world.Baronies[1].Parent?.Key == "c_test", "Title hierarchy and province identity");
            var raster = AppGUI.WorldRaster.Read(world);
            Check(raster.Width == 2 && raster.Ids.All(id => id == 1), "Exact province RGB lookup");

            // The presentation layer over the same files: shells, the realm graph from the title
            // history, and the generator-shaped picks and renders the main window drives.
            var view = new AppGUI.LoadedWorldView(world);
            var kingdom = view.Titles["k_test"];
            var county = view.Titles["c_test"];
            Check(view.Realm is not null && view.Realm.SeatOf(kingdom) == county && view.Realm.Primary(county) == kingdom, "Realm graph from title history");
            Check(view.Realm!.LiegeSeat(county) is null && view.Realm.RealmSize(county) == 1, "Independent realm of one county");
            Check(view.HolderOf(kingdom)?.Key == "gen_char_0" && view.PrimaryOf(world.Characters["gen_char_0"]) == kingdom, "Holder and primary title resolve both ways");
            Check(view.BaronyAt(new Point(0, 0)) == view.Titles["b_test"] && view.BaronyAt(new Point(5, 0)) is null, "Map pick resolves the barony");
            Check(view.Probe("Realms", new Point(1, 0)).Contains("Álvar"), "Readout names the holder");
            Check(view.GovernmentOf(county) == "feudal_government" && !view.IsWild(county), "Government read from history");
            Check(view.Probe("Government", new Point(1, 0)).EndsWith("· Feudal"), "Government readout names the government");

            // The two lists over the same fourteen governments: the cascade's order, which decides
            // them, and the display order the dropdown and the map legend both read. Nothing keeps
            // them in step but this.
            Check(MapGen.GovernmentMap.DisplayOrder.Order().SequenceEqual(MapGen.GovernmentMap.Assignable.Order()),
                  "Display order holds every assignable government, once");
            Check(MapGen.GovernmentMap.Assignable.All(g => Emit.TitleTierWriter.Governments.Contains(Emit.TitleTierWriter.Token(g))),
                  "Every assignable government can be given realm words");
            Check(MapGen.GovernmentMap.DisplayName(MapGen.GovernmentMap.JapanAdministrative) == "Ritsuryō"
                  && MapGen.GovernmentMap.DisplayName(MapGen.GovernmentMap.SteppeAdmin) == "Steppe admin",
                  "Government display names");

            // Every capital holding the generator can seat a ruler in has to be a holding this
            // editor will accept back, or opening a generated mod and round-tripping that barony
            // fails on the value the file already held.
            Check(MapGen.GovernmentMap.Assignable.All(g => LoadedWorld.Holdings.Contains(MapGen.GovernmentMap.CapitalHolding(g))),
                  "Editor accepts every government's capital holding");
            using (var bitmap = view.Render("Realms")) Check(bitmap.Width == 2 && bitmap.Height == 1, "Realms render at raster size");
            using (var bitmap = view.RenderRealmsFocused(county)) Check(bitmap.Width == 2, "Focused realm render");

            var title = world.Titles["k_test"];
            Set(title, "Name", "New Kingdom");
            Set(title, "color", "110 120 130");
            view.RefreshShells();
            Check(kingdom.Name == "New Kingdom" && kingdom.Color == (110, 120, 130), "Shells follow edits");
            var ruler = world.Entries.Single(e => e.Kind == "Character");
            Set(ruler, "Name", "Élodie");
            Set(ruler, "martial", "12");
            var titleFields = new AppGUI.TitleInspector.LoadedFields(title, view, view.Realm);
            Check(titleFields.HeldBy == "Élodie — Kingdom New Kingdom" && titleFields.AnswersTo == "(independent)" && titleFields.Culture == "gen_culture_0", "Title inspector fields");
            Check(titleFields.RendersAs == "Tsardom of New Kingdom — Tsar" && titleFields.Form == "—", "Realm words resolve through the top liege's culture and government");
            var cultureEntry = world.Entries.Single(e => e.Kind == "Culture" && e.Key == "gen_culture_0");
            Check(cultureEntry.Value("Kingdom word") == "Tsardom" && cultureEntry.Value("King") == "Tsar" && cultureEntry.Value("Kingdom word (variant 2)") == "Khanate", "Culture realm words bound per variant");
            Check(cultureEntry.Field("Kingdom word (variant 2)")!.Description.Contains("tribal"), "Variant description names its governments");
            Set(cultureEntry, "King", "Tsar of Tsars");
            Check(titleFields.RendersAs == "Tsardom of New Kingdom — Tsar of Tsars", "Renders as follows an edited culture word");
            var otherFields = new AppGUI.TitleInspector.LoadedFields(world.Titles["k_other"], view, view.Realm);
            Check(otherFields.Form == "League" && otherFields.RendersAs == "League of k_other — Doge", "Per-title override outranks the culture");
            otherFields.Form = "Republic";
            Check(world.Localized("gen_flav_k_other_kingdom") == "Republic", "Title realm word writes its loc line");
            Reject(() => titleFields.Form = "Sultanate", "Reject a title word with no line to write");
            titleFields.Culture = "gen_culture_1";
            Check(world.Provinces[1].Value("culture") == "gen_culture_1" && view.CultureOf(county)?.Key == "gen_culture_1", "County culture writes the province");
            var rulerFields = new AppGUI.RulerInspector.LoadedFields(ruler, view, view.Realm);
            Check(rulerFields.PrimaryTitle == "Kingdom New Kingdom" && rulerFields.Martial == 12 && rulerFields.Age == "40", "Ruler inspector fields");
            rulerFields.Traits = ["craven"];
            Reject(() => rulerFields.Traits = ["craven", "brave"], "Reject a trait count the file cannot hold");

            // Standing: leaves inside the dated effect block, and dynasty prestige a block deeper.
            Check(rulerFields.Gold == 50 && rulerFields.Prestige == 75 && rulerFields.DiplomacyPerks == 2 && rulerFields.Renown == 10, "Standing read from the effect block");
            rulerFields.Gold = 99; rulerFields.Prestige = 80; rulerFields.DiplomacyPerks = 3; rulerFields.Renown = 11;
            Reject(() => rulerFields.MartialPerks = 1, "Reject a standing line the file does not carry");
            Reject(() => rulerFields.Gold = -5, "Reject a negative purse");

            // Family names are one loc line shared by the dynasty and its main house; arms are leaves in the coat file.
            Check(rulerFields.HouseName == "Old House" && rulerFields.DynastyName == "Old House", "House and dynasty names bound");
            rulerFields.HouseName = "New House";
            Check(rulerFields.DynastyName == "New House", "Dynasty shares the house's line");
            Check(rulerFields.Pattern == "pattern_solid.dds" && rulerFields.EmblemColor == "white", "Arms read from the coat file");
            rulerFields.FieldColor1 = "green";
            Reject(() => rulerFields.Pattern = "not_a_texture", "Reject a pattern that is not a texture");
            Check(world.Coats["house_gen_0"].Value("color1") == "green" && world.Coats["gen_dynasty_0"].Value("color1") == "red", "Arms edit lands on the house alone");

            // Development and capital on titles, special buildings on a barony's province.
            var countyFields = new AppGUI.TitleInspector.LoadedFields(world.Titles["c_test"], view, view.Realm);
            Check(countyFields.Development == 3 && view.DevelopmentOf(county) == 3, "Development read from the title history");
            countyFields.Development = 7;
            Check(world.Titles["c_test"].Value("Development") == "7", "Development writes its history line");
            Reject(() => countyFields.Development = 200, "Reject development out of range");
            Check(titleFields.Capital == "c_test", "Capital read from landed_titles");
            Reject(() => titleFields.Capital = "c_missing", "Reject a capital outside the title");
            var baronyFields = new AppGUI.TitleInspector.LoadedFields(world.Titles["b_test"], view, view.Realm);
            Check(baronyFields.SpecialSlot == "wonder_slot_01", "Special building slot read from the province");
            baronyFields.SpecialSlot = "wonder_slot_02";
            Reject(() => baronyFields.SpecialBuilding = "some_building", "Reject a building line the province does not carry");

            // Holy sites: the faith lists them, the site definition names the county.
            var faithEntry = world.Entries.Single(e => e.Kind == "Faith");
            Check(view.HolySitesOf(faithEntry).Count == 1 && faithEntry.Field("holy_site")!.Options!().Any(o => o.Key == "gen_hs_c_test"), "Holy sites resolve and offer a dropdown");
            Reject(() => Set(world.HolySites["gen_hs_c_test"], "county", "c_missing"), "Reject a holy site county not in the world");
            Check(world.Provinces[1].Field("holding")!.Options!().Any(o => o.Key == "castle_holding") && world.Titles["k_test"].Field("capital")!.Options!().Single().Key == "c_test", "Reference fields offer this world's keys");
            using (var bitmap = view.Render("Dynasties")) Check(bitmap.Width == 2, "Dynasties render");
            using (var bitmap = view.Render("Development")) Check(bitmap.Width == 2, "Development render");
            Check(view.Probe("Dynasties", new Point(0, 0)).Contains("New House") && view.Probe("Development", new Point(0, 0)).EndsWith("development 7"), "Dynasty and development readouts");
            Reject(() => Set(title, "Name", "bad\"name"), "Reject script injection");
            Reject(() => Set(ruler, "culture", "missing_culture"), "Reject dangling culture");
            Reject(() => Set(ruler, "martial", "-1"), "Reject invalid skill");
            Check(world.Save(Path.Combine(root, "backups")) == 7, "Only seven expected files changed");
            var saved = Snapshot(mod);
            Check(saved.Count == original.Count, "No new files in mod");
            var changed = saved.Where(p => p.Value.Hash != original[p.Key].Hash).Select(p => p.Key).ToHashSet();
            Check(changed.SetEquals(new[] { "common/landed_titles/00_landed_titles.txt", "history/characters/00_generated_characters.txt", "history/provinces/00_generated_provinces.txt", "history/titles/00_generated_titles.txt", "common/coat_of_arms/coat_of_arms/00_generated_coas.txt", "localization/english/gen_test_l_english.yml", "localization/english/gen_title_tiers_l_english.yml" }), "Only intended outputs changed");
            Check(File.ReadAllText(Path.Combine(mod, "history/characters/00_generated_characters.txt")) == "gen_char_0 = { name=\"Élodie\" dynasty_house=house_gen_0 martial=12 culture=gen_culture_0 religion=gen_faith_0 trait=craven 860.1.1={birth=yes} 900.1.1={effect={add_gold=99 add_prestige=80 add_diplomacy_lifestyle_perk_points=3 dynasty={add_dynasty_prestige=11}}} }\r\n", "Edited leaf preserves all surrounding bytes");
            Check(File.ReadAllText(Path.Combine(mod, "common/coat_of_arms/coat_of_arms/00_generated_coas.txt")).Contains("\tcolor1 = \"green\"\r\n\tcolor2 = \"blue\""), "Quoted leaf is written back quoted");
            view.RecolorChildren(kingdom);
            Check(world.Titles["c_test"].Value("color") != "150 160 170" && world.Titles["b_test"].Value("color") != "1 2 3", "Recolour children writes descendants");
            world.Revert();
            view.RefreshShells();
            Check(world.Titles["c_test"].Value("color") == "150 160 170" && county.Color == (150, 160, 170) && !world.ChangedFiles.Any(), "Revert restores recoloured descendants");
            Check(world.Localized("bm_char_test") == "Élodie $nick_the_brave$", "Bookmark name follows character");
            foreach (var name in changed)
                Check(Hash(Path.Combine(world.LastBackup!, name)) == original[name].Hash, "Backup is exact: " + name);
            Check(world.Save() == 0, "Second save is a no-op");
            Check(view.CanReroll(ruler), "Reroll offered from the mod's name list");
            view.RerollName(ruler);
            Check(ruler.Name is "Aldo" or "Bram", "Reroll draws a localized male name");
            Set(title, "Name", "Discard me");
            world.Revert();
            Check(title.Name == "New Kingdom" && !world.ChangedFiles.Any(), "Revert returns to last save");
            var reopened = LoadedWorld.Open(mod);
            Check(reopened.Titles["k_test"].Name == "New Kingdom" && reopened.Save() == 0, "Saved world reopens without regeneration");
            Same(saved, Snapshot(mod), "Reopen preserves bytes and timestamps");

            // Conflict detection includes loaded dependencies, before any changed file is replaced.
            Set(title, "Name", "Conflict edit");
            string culture = Path.Combine(mod, "common/culture/cultures/00_generated_cultures.txt");
            File.AppendAllText(culture, "# external edit\n");
            var conflict = Snapshot(mod);
            Reject(() => world.Save(Path.Combine(root, "backups")), "Reject external changes");
            Same(conflict, Snapshot(mod), "Conflict leaves every file untouched");

            // Let preflight reads succeed but deny replacement of the later localization file.
            world = LoadedWorld.Open(mod);
            Set(world.Titles["k_test"], "Name", "Must roll back");
            Set(world.Titles["k_test"], "color", "5 6 7");
            string titlePath = Path.Combine(mod, "common/landed_titles/00_landed_titles.txt");
            var beforeFailure = Snapshot(mod);
            using (var locked = new FileStream(titlePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                Reject(() => world.Save(Path.Combine(root, "backups")), "Save failure reported");
            var afterFailure = Snapshot(mod);
            Check(beforeFailure.All(p => afterFailure[p.Key].Hash == p.Value.Hash), "Partial save rolls back original bytes");
            Check(world.ChangedFiles.Count() == 2, "Failed save retains pending edits");

            if (existing is not null)
            {
                var actual = LoadedWorld.Open(existing);
                var before = actual.Files.ToDictionary(f => f.Path, f => (Hash(f.Path), File.GetLastWriteTimeUtc(f.Path)));
                Check(actual.Save() == 0, "Existing world no-op save");
                Check(before.All(p => p.Value == (Hash(p.Key), File.GetLastWriteTimeUtc(p.Key))), "Existing world byte/timestamp preservation");
                Console.WriteLine($"Existing world: {actual.Entries.Count:N0} entries, {actual.Titles.Count:N0} titles, {actual.Files.Count} files.");
                var actualView = new AppGUI.LoadedWorldView(actual);
                Check(actualView.Realm is not null, "Existing world has a realm graph");
                var counties = MapGen.Titles.Flatten(actualView.Roots).Where(t => t.Tier == "c").ToList();
                int held = counties.Count(c => actualView.Realm!.SeatOf(c) is { } seat && actual.Holders.ContainsKey(actualView.Realm.Primary(seat).Key));
                Console.WriteLine($"  {counties.Count:N0} counties, {held:N0} with a holder in the title history, "
                    + $"{counties.Count(actualView.IsWild):N0} wilderness, {counties.Select(c => actualView.Realm!.PathFromTop(actualView.Realm.SeatOfCounty(c))[0]).Distinct().Count():N0} independent realms.");
                Console.WriteLine($"  {actual.Dynasties.Count:N0} dynasties, {actual.Houses.Count:N0} houses, {actual.Coats.Count:N0} coats of arms, "
                    + $"{actual.HolySites.Count:N0} holy sites; {actual.Characters.Values.Count(actualView.CanReroll):N0} characters can reroll a name.");
                foreach (string mode in new[] { "Counties", "Realms", "Dynasties", "Development", "Cultures", "Faiths", "Government" })
                    using (var bitmap = actualView.Render(mode))
                    {
                        Check(bitmap.Width > 0, "Existing world renders " + mode);
                        bitmap.Save(Path.Combine(root, mode.ToLowerInvariant() + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                    }
                var largest = counties.Select(c => actualView.Realm!.PathFromTop(actualView.Realm.SeatOfCounty(c))[0]).Distinct()
                    .OrderByDescending(actualView.Realm!.RealmSize).First();
                using (var bitmap = actualView.RenderRealmsFocused(largest))
                    bitmap.Save(Path.Combine(root, "realms-focused.png"), System.Drawing.Imaging.ImageFormat.Png);
                Console.WriteLine($"  Renders written beside the fixture; focused realm: {actualView.Realm.Primary(largest).Name}, {actualView.Realm.RealmSize(largest)} counties.");

                // The main window redirects Console.Out into its log box; keep ours.
                var stdout = Console.Out;
                using var form = new AppGUI.MainForm(new GenerationOptions());
                form.AdoptLoadedWorld(actualView);
                form.RenderLoadedPreview(Path.Combine(root, "editor-preview.png"));
                Console.SetOut(stdout);
                Console.WriteLine("Editor preview: " + Path.Combine(root, "editor-preview.png"));
            }
            Console.WriteLine("World editor checks passed. Fixture: " + root);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); Console.Error.WriteLine("Fixture retained: " + root); return 1; }
    }

    private static void Set(WorldEntry entry, string key, string value) => entry.Fields.Single(f => f.Name == key).Write!(value);
    private static void Check(bool condition, string label) { if (!condition) throw new Exception(label); Console.WriteLine("PASS " + label); }
    private static void Reject(Action action, string label)
    {
        try { action(); }
        catch (Exception ex) when (ex is ArgumentException or IOException) { Console.WriteLine("PASS " + label); return; }
        throw new Exception(label + " did not reject");
    }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static Dictionary<string, (string Hash, DateTime Time)> Snapshot(string path)
        => Directory.GetFiles(path, "*", SearchOption.AllDirectories).ToDictionary(p => Path.GetRelativePath(path, p).Replace('\\', '/'), p => (Hash(p), File.GetLastWriteTimeUtc(p)));
    private static void Same(Dictionary<string, (string Hash, DateTime Time)> a, Dictionary<string, (string Hash, DateTime Time)> b, string label)
        => Check(a.Count == b.Count && a.All(p => b.TryGetValue(p.Key, out var v) && p.Value == v), label);
}
