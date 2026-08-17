using Ck3MapGen.Core;
using Ck3MapGen.Emit;
using System.Globalization;

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
                RealmMap realms, WildernessMap wilderness, Rng rng)
    {
        var map = new ArtifactMap();
        var legendaryLogs = new List<string>();

        if (counties.Count == 0) return map;

        var settledCounties = counties.Where(c => !wilderness.Contains(c)).ToList();
        if (settledCounties.Count == 0) return map;

        // Draw a fated bearer among settled counties only
        var fatedCounty = rng.Pick(settledCounties);

        foreach (var county in settledCounties)
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
            int roll = countyRng.Int(0, 100);

            if (isEmperor) targetCount = roll > 50 ? 4 : 3;
            else if (isKing) targetCount = roll > 40 ? 3 : 2;
            else if (isDuke) targetCount = roll > 70 ? 2 : (roll > 15 ? 1 : 0);
            else targetCount = roll > 60 ? 1 : 0;

            if (county == fatedCounty && targetCount == 0)
            {
                targetCount = 1;
            }

            for (int i = 0; i < targetCount; i++)
            {
                var artRng = new Rng((int)(county.Index ^ 0x3D7F ^ (i * 7177)));
                bool isLegendary = (county == fatedCounty && i == 0) || (artRng.Int(0, 100) < 2);

                string type;
                string visuals;
                string template;
                string modifier;
                string localizedName;
                string localizedDescription;
                int quality = isLegendary ? 100 : artRng.Int(30, 95);
                int wealth = isLegendary ? 150 : artRng.Int(30, 95);

                int modifierLevel = quality switch
                {
                    > 75 => 3,
                    > 45 => 2,
                    _ => 1
                };

                ArtifactCategory category;
                if (i == 0 && (isEmperor || isKing))
                {
                    category = ArtifactCategory.SovereignJewels;
                }
                else
                {
                    category = (ArtifactCategory)artRng.Int(1, 3);
                }

                switch (category)
                {
                    case ArtifactCategory.SovereignJewels:
                        if (artRng.Int(0, 100) < 30)
                        {
                            type = "regalia";
                            visuals = "regalia";
                            template = "gen_regalia_template";
                            modifier = isLegendary ? "gen_legendary_sovereign_modifier" : $"gen_sovereign_opinion_modifier_{modifierLevel}";
                            localizedName = isLegendary
                                ? artRng.Pick(new List<string> { "The Scepter of Supreme Dominion", "The Rod of Heaven", $"The Sovereign Star of {primaryTitle.Name}" })
                                : (artRng.Int(0, 1) == 0
                                    ? $"Scepter of {primaryTitle.Name}"
                                    : $"The {culture.Name} Rod of {firstName}");
                            localizedDescription = isLegendary
                                ? $"The ultimate symbol of earthly power over {primaryTitle.Name}. Those who stand before its bearer are filled with uncontrollable awe and absolute obedience."
                                : $"A ceremonial scepter crafted from precious metals, symbolizing de jure lordship over {primaryTitle.Name}.";
                        }
                        else
                        {
                            type = "helmet";
                            visuals = "crown";
                            template = "gen_crown_template";
                            modifier = isLegendary ? "gen_legendary_sovereign_modifier" : $"gen_sovereign_opinion_modifier_{modifierLevel}";
                            localizedName = isLegendary
                                ? artRng.Pick(new List<string> { "The Crown of Eternity", "The Solar Diadem", $"The Imperial Diadem of {primaryTitle.Name}" })
                                : (artRng.Int(0, 1) == 0
                                    ? $"Crown of {primaryTitle.Name}"
                                    : $"The {culture.Name} Diadem of {firstName}");
                            localizedDescription = isLegendary
                                ? $"An awe-inspiring masterpiece, rumored to have been crafted by angelic hands. It radiates an ethereal glow, asserting the divine right to rule over {primaryTitle.Name}."
                                : $"The majestic ceremonial crown of {primaryTitle.Name}, worn by {firstName} to project dynastic authority.";
                        }
                        break;

                    case ArtifactCategory.MartialRelics:
                        if (artRng.Int(0, 100) < 20)
                        {
                            // Add helmets at some point ("helmet" and "helmet_simple" are the types in 00_types.txt)
                            // Change descriptions to match type of armor (plate, mail, scale) next

                            string[] armors = { "armor_plate", "armor_mail" , "armor_scale", "armor_lamellar", "armor_laminar", "armor_brigandine",  };
                            string armorType = armors[artRng.Int(0, armors.Length - 1)];

                            type = armorType;
                            visuals = "armor";
                            template = "gen_armor_template";
                            modifier = isLegendary ? "gen_legendary_martial_modifier" : $"gen_martial_prowess_modifier_{modifierLevel}";
                            localizedName = isLegendary
                                ? artRng.Pick(new List<string> { $"The Aegis of {primaryTitle.Name}", "The Impervious Plate", $"The Sun-Forged Mail of {firstName}" })
                                : (artRng.Int(0, 1) == 0
                                    ? $"The Guard of {primaryTitle.Name}"
                                    : $"The {culture.Name} Armor of {firstName}");
                            localizedDescription = isLegendary
                                ? $"A legendary suit of armor that seems completely untouched by blade or arrow. It was forged in secret fires and bears the eternal protection of the {culture.Name} deities."
                                : $"A fine suit of protective mail designed in the traditional {culture.Name} pattern, bearing the heraldry of {primaryTitle.Name}.";
                        }
                        else
                        {
                            string[] weapons = { "sword", "axe", "mace", "spear", "dagger" };
                            string weaponKind = weapons[artRng.Int(0, weapons.Length - 1)];

                            // Fix: type must match the specific weapon type (sword, axe, mace, etc.)
                            type = weaponKind;
                            visuals = weaponKind;
                            template = "gen_weapon_template";
                            modifier = isLegendary ? "gen_legendary_martial_modifier" : $"gen_martial_prowess_modifier_{modifierLevel}";
                            string weaponName = weaponKind switch
                            {
                                "sword" => "Blade",
                                "axe" => "Cleaver",
                                "mace" => "Mace",
                                "spear" => "Lance",
                                _ => "Dagger"
                            };
                            localizedName = isLegendary
                                ? weaponKind switch
                                {
                                    "sword" => artRng.Pick(new List<string> { "The Sunslayer", "Eternity's Edge", $"The Holy Sword of {firstName}" }),
                                    "axe" => artRng.Pick(new List<string> { "The Earthsplitter", "The Doomcleaver", "Famine" }),
                                    "mace" => artRng.Pick(new List<string> { "The Worldcrusher", "The Skull-Render", "The Starfall Mace" }),
                                    "spear" => artRng.Pick(new List<string> { "The Sky-Piercer", $"The Gungnir of {primaryTitle.Name}", "The Longinus" }),
                                    _ => artRng.Pick(new List<string> { "The Whisperer", "Death's Kiss", "The Nightfall Dagger" })
                                }
                                : (artRng.Int(0, 1) == 0
                                    ? $"{firstName}'s Trusty {weaponName}"
                                    : $"The {culture.Name} {weaponName} of {primaryTitle.Name}");
                            localizedDescription = isLegendary
                                ? $"A mythical {weaponKind} of incomparable balance and terrifying power. The weapon itself hums with the memory of a thousand battlefields."
                                : $"A balanced steel {weaponKind} made for combat, decorated in classic {culture.Name} style.";
                        }
                        break;

                    case ArtifactCategory.SacredScriptures:
                        type = "book";
                        visuals = artRng.Int(0, 1) == 0 ? "pocket_book" : "artifact_scroll";
                        template = "gen_book_template";
                        modifier = isLegendary ? "gen_legendary_sacred_modifier" : $"gen_sacred_piety_modifier_{modifierLevel}";
                        localizedName = isLegendary
                            ? artRng.Pick(new List<string> { "The Codex of Revelation", $"The Celestial Scrolls of the {faith.Name} Faith", "The Words of the Primeval Creator" })
                            : $"A Study of the {faith.Name} Faith";
                        localizedDescription = isLegendary
                            ? $"The pristine, original manuscript containing direct divine revelations. Its holy verses inspire unmatched devotion, and a single page is worth more than a kingdom."
                            : $"A hand-bound volume outlining the holy customs, teachings, and heritage of {primaryTitle.Name}.";
                        break;

                    case ArtifactCategory.ScholarlyWorks:
                    default:
                        type = "book";
                        visuals = artRng.Int(0, 1) == 0 ? "pocket_book" : "artifact_scroll";
                        template = "gen_book_template";
                        modifier = isLegendary ? "gen_legendary_scholar_modifier" : $"gen_scholar_learning_modifier_{modifierLevel}";
                        localizedName = isLegendary
                            ? artRng.Pick(new List<string> { "The Opus of the Universe", "The Grand Compendium of the Stars", "The Chronicles of the First Age" })
                            : $"The Chronicles of {primaryTitle.Name}";
                        localizedDescription = isLegendary
                            ? $"An exhaustive library of universal secrets, ancient lineages, and advanced geometries compiled by legendary scholars. Its pages contain the blueprints of civilization itself."
                            : $"A compilation of local wisdom, records, and philosophical notes commissioned during the reign of {firstName}.";
                        break;
                }

                string id = $"gen_art_{county.Index}_{i}";
                string nameKey = $"gen_art_name_{county.Index}_{i}";
                string descKey = $"gen_art_desc_{county.Index}_{i}";

                var art = new GeneratedArtifact(
                    id, nameKey, descKey, type, visuals, template,
                    wealth, quality, modifier, category, localizedName, localizedDescription);

                if (isLegendary)
                {
                    string charId = HistoryWriter.CharacterId(county);
                    legendaryLogs.Add($"'{localizedName}' ({category}) -> Holder: {firstName} of {primaryTitle.Name} (Primary Title: {primaryTitle.Key}, Character: {charId})");
                }

                list.Add(art);
                map.AllArtifacts.Add(art);
            }

            if (list.Count > 0)
            {
                map.ByCounty[county] = list;
            }
        }

        Console.WriteLine($"  artifacts generated: {map.AllArtifacts.Count} procedural items across {map.ByCounty.Count} rulers");

        if (legendaryLogs.Count > 0)
        {
            Console.WriteLine("  legendary artifacts generated:");
            foreach (var log in legendaryLogs)
            {
                Console.WriteLine($"    {log}");
            }
        }

        return map;
    }
}