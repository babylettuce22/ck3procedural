using Ck3MapGen.Config;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

internal static class AdventurerWriter
{
    private static readonly string[] Owned =
    [
        "common/landed_titles/zz_gen_adventurers.txt",
        "common/dynasties/zz_gen_adventurers.txt",
        "common/dynasty_houses/zz_gen_adventurers.txt",
        "history/characters/zz_gen_adventurers.txt",
        "history/titles/zz_gen_adventurers.txt",
        "localization/english/gen_adventurers_l_english.yml",
    ];

    public static void WriteAll(string modDir, MapConfig cfg, AdventurerRoster roster,
        EthnicityMap ethnicities)
    {
        // Applied histories replace this layer in place, including when the feature is disabled
        // or every previously eligible county is now wilderness.
        foreach (string rel in Owned)
            if (File.Exists(Path.Combine(modDir, rel))) File.Delete(Path.Combine(modDir, rel));
        if (roster.All.Count == 0) return;

        var titles = new JominiBuilder();
        var dynasties = new JominiBuilder();
        var houses = new JominiBuilder();
        var characters = new JominiBuilder();
        var history = new JominiBuilder();
        var loc = new LocFile();
        foreach (var p in roster.All)
        {
            string purpose = p.Role switch
            {
                AdventurerRole.Captain => "camp_purpose_mercenaries",
                AdventurerRole.Scholar => "camp_purpose_scholars",
                _ => "camp_purpose_wanderers",
            };
            string houseName = $"{p.HouseKey}_name";
            using (titles.Block(p.TitleKey))
            {
                titles.Field("color", "{ 100 100 100 }");
                titles.Field("capital", p.Location.Key);
                titles.Field("definite_form", "yes");
                titles.Field("landless", "yes");
                titles.Field("require_landless", "yes");
                titles.Field("ruler_uses_title_name", "no");
                titles.Field("no_automatic_claims", "yes");
                titles.Field("destroy_if_invalid_heir", "yes");
                titles.Field("dlc_feature", "roads_to_power");
            }
            using (dynasties.Block(p.DynastyId))
            {
                dynasties.Quoted("name", houseName);
                dynasties.Field("culture", p.Culture.Key);
            }
            using (houses.Block(p.HouseKey))
            {
                houses.Quoted("name", houseName);
                houses.Field("dynasty", p.DynastyId);
            }
            using (characters.Block(p.Id))
            {
                // Own name keys also cover inherited cultures without generated name localisation.
                characters.Quoted("name", $"{p.Id}_name");
                if (p.Female) characters.Field("female", "yes");
                characters.Field("dynasty_house", p.HouseKey);
                characters.Field("culture", p.Culture.Key);
                characters.Field("religion", p.Faith.Key);
                characters.Field("trait", HistoryWriter.GetPhenotypeTrait(p.Culture, ethnicities, cfg));
                characters.Field("trait", p.Role switch
                {
                    AdventurerRole.Captain => "education_martial_3",
                    AdventurerRole.Scholar => "education_learning_3",
                    _ => "education_diplomacy_3",
                });
                characters.Field("trait", "lifestyle_traveler");
                characters.Field("martial", p.Role == AdventurerRole.Captain ? 12 : 5);
                characters.Field("learning", p.Role == AdventurerRole.Scholar ? 12 : 5);
                characters.Field("diplomacy", p.Role == AdventurerRole.Wanderer ? 12 : 5);
                characters.Inline($"{p.Born}.1.1", "birth = yes");
                using (characters.Block($"{p.BookmarkYear}.1.1"))
                using (characters.Block("effect"))
                {
                    characters.Field("add_gold", p.Gold);
                    characters.Field("add_prestige", p.Prestige);
                }
                if (p.EndYear is { } until)
                    characters.Inline($"{until}.1.1", "death = yes");
            }
            using (history.Block(p.TitleKey))
            {
                using (history.Block($"{p.BookmarkYear}.1.1"))
                {
                    history.Field("liege", "0");
                    history.Field("holder", p.Id);
                    history.Field("government", "landless_adventurer_government");
                    history.Field("succession_laws", "{ landless_adventurer_succession_law }");
                    using (history.Block("effect"))
                    {
                        history.Field("create_landless_adventurer_title_history_effect", "yes");
                        using (history.Block("holder"))
                            history.Field("add_realm_law_skip_effects", purpose);
                        using (history.Block("set_variable"))
                        {
                            history.Field("name", "adventurer_creation_reason");
                            history.Field("value", "flag:historical");
                        }
                    }
                }
                if (p.EndYear is { } until)
                    history.Inline($"{until}.1.1", "holder = 0");
            }
            string company = p.Role switch
            {
                AdventurerRole.Captain => $"{p.Name}'s Company",
                AdventurerRole.Scholar => $"{p.Name}'s Fellowship",
                _ => $"{p.Name}'s Wanderers",
            };
            loc.Add(p.TitleKey, company);
            loc.Add(p.TitleKey + "_adj", p.DynastyName);
            loc.Add(houseName, p.DynastyName);
            loc.Add($"{p.Id}_name", p.Name);
        }
        string[] scripts = [titles.ToString(), dynasties.ToString(), houses.ToString(),
            characters.ToString(), history.ToString()];
        for (int i = 0; i < scripts.Length; i++)
        {
            string path = Path.Combine(modDir, Owned[i]);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            ParadoxText.WriteBom(path, scripts[i]);
        }
        loc.Write(Path.Combine(modDir, Owned[5]));
    }
}
