using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.Emit;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Tools;

internal static class AdventurerChecks
{
    public static int Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "ck3-adventurer-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            void Require(bool ok, string message)
            {
                if (!ok) throw new InvalidOperationException(message);
            }
            var tongue = Language.CreateAnglic("language_test", new Rng(42));
            var heritage = new Heritage { Key = "heritage_test", Name = "Test", Language = tongue, Look = default! };
            var culture = new Culture
            {
                Key = "culture_test", Name = "Test", Heritage = heritage, Tongue = tongue,
                MeanDevelopment = 5, Color = (100, 100, 100), Ethos = "ethos_communal",
                MartialCustom = "martial_custom_male_only", HeadDetermination = "heritage",
                Traditions = [], CoaGfx = "", BuildingGfx = "", ClothingGfx = "", UnitGfx = "",
                MaleNames = ["Adam", "Bert"], FemaleNames = ["Ada", "Bea"], DynastyNames = ["Oak"],
                PatronymSuffixMale = "", PatronymSuffixFemale = "", LocationPrefix = "", AlwaysUsePatronym = false,
            };
            var religion = new Religion
            {
                Key = "religion_test", Name = "Test", Language = tongue, GraphicalFaith = "pagan_gfx",
                Monotheist = false, Abrahamic = false, LayClergy = false, Doctrines = [], Virtues = [], Sins = [],
                CoronationCrown = false, Localization = [], LocalizationText = [],
            };
            var faith = new Faith { Key = "faith_test", Name = "Test", Religion = religion,
                Color = (0.5, 0.5, 0.5), Icon = "pagan", Tenets = [] };
            var counties = Enumerable.Range(0, 1000).Select(i => new Title
            {
                Tier = "c", Index = i, Key = $"c_test_{i}",
                Children = [new Title { Tier = "b", Index = i, ProvinceId = i + 1, Key = $"b_test_{i}" }],
            }).ToList();
            var cultures = new CultureMap { Cultures = [culture], Heritages = [heritage],
                ByCounty = counties.ToDictionary(c => c, _ => culture) };
            var faiths = new FaithMap { Faiths = [faith], Religions = [religion],
                ByCounty = counties.ToDictionary(c => c, _ => faith) };
            RealmMap Map(IEnumerable<Title> land) => new() { HolderCounty = land.ToDictionary(c => c, c => c), Liege = [] };
            var realms = Map(counties);
            var cfg = new MapConfig { Seed = 42, StartYear = 900 };
            var roster = AdventurerRoster.Build(cfg, counties, realms, cultures, faiths, WildernessMap.Empty, null);
            string Signature(AdventurerRoster r) => string.Join("|", r.All.Select(p =>
                $"{p.Id}:{p.Name}:{p.DynastyName}:{p.Born}:{p.Gold}:{p.Prestige}:{p.Role}"));
            Require(roster.All.Count == 12, "Large worlds must respect the roster cap.");
            var reversed = Map(counties.AsEnumerable().Reverse());
            Require(Signature(roster) == Signature(AdventurerRoster.Build(cfg,
                counties.AsEnumerable().Reverse().ToList(), reversed, cultures, faiths, WildernessMap.Empty, null)),
                "Roster identity must not depend on county/dictionary insertion order.");
            Require(roster.All.Select(p => p.Id).Distinct().Count() == roster.All.Count, "Duplicate character ids.");
            Require(roster.All.Select(p => p.Role).Distinct().Count() == 3, "Missing adventurer role.");
            Require(roster.All.All(p => p.Born >= 1 && p.BookmarkYear - p.Born >= 19), "Invalid adult birth date.");

            var remaining = counties.Take(2).ToList();
            var ruined = WildernessMap.Empty.After(new HashSet<Title>(), new HashSet<Title> { remaining[0] });
            var endpoint = AdventurerRoster.Build(cfg, counties, Map(remaining), cultures, faiths, ruined, null);
            Require(endpoint.All.Count == 1 && endpoint.All[0].Origin == remaining[1],
                "Applied endpoint must exclude removed and ruined counties, including retained stale realm entries.");
            var laterCfg = cfg.AtStartYear(1000);
            var later = AdventurerRoster.Build(laterCfg, counties, realms, cultures, faiths, WildernessMap.Empty, null);
            Require(!later.All.Select(p => p.Id).Intersect(roster.All.Select(p => p.Id)).Any(),
                "An advanced endpoint must derive a new generation, not freeze old characters.");

            var eras = new BookmarkEras { MainYear = 900, Seed = 42, Eras =
                [new BookmarkEra { Year = 1200, Tag = "late", Realms = realms, Rulers = null!, Governments = null! }] };
            var dated = AdventurerRoster.Build(cfg, counties, realms, cultures, faiths, WildernessMap.Empty, eras);
            Require(dated.All.Count == 24 && dated.All.Where(p => p.BookmarkYear == 900)
                .All(p => p.EndYear <= 1200 && p.EndYear - p.Born <= 80 && p.EndYear > 900),
                "Early roster must end before the next bookmark without centuries-long lives.");
            Require(dated.All.Where(p => p.BookmarkYear == 1200).All(p => p.EndYear is null),
                "Latest roster should have no authored death date.");
            Require(Signature(roster) == Signature(new AdventurerRoster(dated.All.Where(p => p.BookmarkYear == 900).ToList())),
                "Adding another bookmark must not redraw the main roster.");
            Require(AdventurerRoster.Build(cfg.AtStartYear(10), counties, realms, cultures, faiths,
                WildernessMap.Empty, null).All.Count == 0, "Earliest dates must not produce impossible adults.");
            Require(AdventurerRoster.Build(cfg, [], Map([]), cultures, faiths,
                WildernessMap.Empty, null).All.Count == 0, "Empty worlds must produce an empty roster.");

            var eth = new EthnicityDef { Key = "test", LocalizedName = "Test", Archetype = RaceArchetype.Human,
                BaseTemplate = "caucasian", LookFamily = "caucasian" };
            var ethnicities = new EthnicityMap { Ethnicities = [], ByCulture = new() { [culture] = eth },
                ByHeritage = [], ByCultureKey = [], ByHeritageKey = [], VariantsByCulture = [], MinorityPlacements = [] };
            AdventurerWriter.WriteAll(root, cfg, dated, ethnicities);
            var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
            Require(files.Length == 6, "Expected six owned output files.");
            var before = files.ToDictionary(f => f, File.ReadAllText);
            AdventurerWriter.WriteAll(root, cfg, dated, ethnicities);
            Require(before.All(p => File.ReadAllText(p.Key) == p.Value), "Re-emission must be byte-stable.");
            Require(File.ReadAllBytes(files.Single(f => f.EndsWith(".yml"))).Take(3)
                .SequenceEqual(new byte[] { 239, 187, 191 }), "Localisation needs UTF-8 BOM.");
            cfg.EnableAdventurers = false;
            var disabled = AdventurerRoster.Build(cfg, counties, realms, cultures, faiths, WildernessMap.Empty, null);
            Require(disabled.All.Count == 0, "Disabled generation must be empty.");
            AdventurerWriter.WriteAll(root, cfg, disabled, ethnicities);
            Require(!Directory.GetFiles(root, "*", SearchOption.AllDirectories).Any(), "Disabled re-emission left stale files.");
            Directory.Delete(root, true);
            Console.WriteLine("Adventurer checks passed: bounds, determinism, endpoint regeneration, bookmark lifespans, emission and cleanup.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            Console.Error.WriteLine($"Adventurer fixture retained: {root}");
            return 1;
        }
    }
}
