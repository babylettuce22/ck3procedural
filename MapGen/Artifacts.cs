using Ck3MapGen.Core;
using Ck3MapGen.Emit;

namespace Ck3MapGen.MapGen;

public enum ArtifactCategory
{
    SovereignJewels,
    MartialRelics,
    SacredScriptures,
    ScholarlyWorks
}

public sealed class GeneratedArtifact
{
    public string Id { get; }
    public string NameKey { get; }
    public string DescriptionKey { get; }
    public string Type { get; }
    public string Visuals { get; }
    public string Template { get; }
    public int Wealth { get; }
    public int Quality { get; }
    public string Modifier { get; }
    public ArtifactCategory Category { get; }
    public string LocalizedName { get; }
    public string LocalizedDescription { get; }

    public GeneratedArtifact(
        string id, string nameKey, string descriptionKey, string type,
        string visuals, string template, int wealth, int quality,
        string modifier, ArtifactCategory category, string localizedName, string localizedDescription)
    {
        Id = id;
        NameKey = nameKey;
        DescriptionKey = descriptionKey;
        Type = type;
        Visuals = visuals;
        Template = template;
        Wealth = wealth;
        Quality = quality;
        Modifier = modifier;
        Category = category;
        LocalizedName = localizedName;
        LocalizedDescription = localizedDescription;
    }
}

public sealed class ArtifactMap
{
    public Dictionary<Title, List<GeneratedArtifact>> ByCounty { get; } = new();
    public List<GeneratedArtifact> AllArtifacts { get; } = new();

    public static ArtifactMap Build(
        List<Title> counties, CultureMap cultures, FaithMap faiths,
        RealmMap realms, Rng rng)
    {
        var map = new ArtifactMap();

        foreach (var county in counties)
        {
            var culture = cultures.For(county);
            var faith = faiths.For(county);
            var primaryTitle = HistoryWriter.Primary(county, realms);

            var countyRng = new Rng(county.Index ^ 0x7E1A);
            var (firstName, _) = HistoryWriter.RulerNames(county, culture);

            var list = new List<GeneratedArtifact>();

            bool isEmperor = primaryTitle.Tier == "e";
            bool isKing = primaryTitle.Tier == "k";
            bool isDuke = primaryTitle.Tier == "d";

            int targetCount = 0;
            if (isEmperor)
            {
                // Emperors always receive exactly 2 artifacts
                targetCount = 2;
            }
            else if (isKing)
            {
                // Kings have a 75% chance of getting 1 or 2 artifacts
                int roll = countyRng.Int(0, 100);
                targetCount = roll > 80 ? 2 : (roll > 25 ? 1 : 0);
            }
            else if (isDuke)
            {
                // Dukes have a 35% chance of getting 1 artifact
                targetCount = countyRng.Int(0, 100) < 35 ? 1 : 0;
            }
            else
            {
                // Counts have a 10% chance of getting 1 artifact
                targetCount = countyRng.Int(0, 100) < 10 ? 1 : 0;
            }

            for (int i = 0; i < targetCount; i++)
            {
                string type;
                string visuals;
                string template;
                string modifier;
                string localizedName;
                string localizedDescription;
                int quality = countyRng.Int(30, 95);
                int wealth = countyRng.Int(30, 95);

                int modifierLevel = quality switch
                {
                    > 75 => 3,
                    > 45 => 2,
                    _ => 1
                };

                // Assign categories selectively based on iteration and ruler tier
                ArtifactCategory category;
                if (i == 0 && (isEmperor || isKing))
                {
                    category = ArtifactCategory.SovereignJewels;
                }
                else
                {
                    category = (ArtifactCategory)countyRng.Int(1, 3);
                }

                switch (category)
                {
                    case ArtifactCategory.SovereignJewels:
                        type = "helmet";
                        visuals = "gen_crown_visual";
                        template = "gen_crown_template";
                        modifier = $"gen_sovereign_opinion_modifier_{modifierLevel}";
                        localizedName = countyRng.Int(0, 1) == 0
                            ? $"Crown of {primaryTitle.Name}"
                            : $"The {culture.Name} Diadem of {firstName}";
                        localizedDescription = $"The majestic ceremonial crown of {primaryTitle.Name}, worn by {firstName} to project dynastic authority.";
                        break;

                    case ArtifactCategory.MartialRelics:
                        string[] weapons = { "sword", "axe", "mace", "spear", "dagger" };
                        string weaponKind = weapons[countyRng.Int(0, weapons.Length - 1)];
                        type = weaponKind; // <-- Uses "sword", "axe", "mace", "spear", "dagger" from 000_placeholder.txt
                        visuals = "gen_weapon_visual";
                        template = "gen_weapon_template";
                        modifier = $"gen_martial_prowess_modifier_{modifierLevel}";
                        string weaponName = weaponKind switch
                        {
                            "sword" => "Blade",
                            "axe" => "Cleaver",
                            "mace" => "Mace",
                            "spear" => "Lance",
                            _ => "Dagger"
                        };
                        localizedName = countyRng.Int(0, 1) == 0
                            ? $"{firstName}'s Trusty {weaponName}"
                            : $"The {culture.Name} {weaponName} of {primaryTitle.Name}";
                        localizedDescription = $"A balanced steel {weaponKind} made for combat, decorated in classic {culture.Name} style.";
                        break;

                    case ArtifactCategory.SacredScriptures:
                        type = "book";
                        visuals = "gen_book_visual";
                        template = "gen_book_template";
                        modifier = $"gen_sacred_piety_modifier_{modifierLevel}";
                        localizedName = $"A Study of the {faith.Key.Replace("_religion", "").Replace("_faith", "")} Faith";
                        localizedDescription = $"A hand-bound volume outlining the holy customs, teachings, and heritage of {primaryTitle.Name}.";
                        break;

                    case ArtifactCategory.ScholarlyWorks:
                    default:
                        type = "book";
                        visuals = "gen_book_visual";
                        template = "gen_book_template";
                        modifier = $"gen_scholar_learning_modifier_{modifierLevel}";
                        localizedName = $"The Chronicles of {primaryTitle.Name}";
                        localizedDescription = $"A compilation of local wisdom, records, and philosophical notes commissioned during the reign of {firstName}.";
                        break;
                }

                string id = $"gen_art_{county.Index}_{i}";
                string nameKey = $"gen_art_name_{county.Index}_{i}";
                string descKey = $"gen_art_desc_{county.Index}_{i}";

                var art = new GeneratedArtifact(
                    id, nameKey, descKey, type, visuals, template,
                    wealth, quality, modifier, category, localizedName, localizedDescription);

                list.Add(art);
                map.AllArtifacts.Add(art);
            }

            if (list.Count > 0)
            {
                map.ByCounty[county] = list;
            }
        }

        Console.WriteLine($"  artifacts generated: {map.AllArtifacts.Count} procedural items across {map.ByCounty.Count} rulers");
        return map;
    }
}