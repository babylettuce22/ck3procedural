using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
namespace Ck3MapGen.Emit;

public static class HistoryWriter
{
    public static void WriteAll(
        string modDir, MapConfig cfg, List<Title> empires,
        RealmMap realms, Dictionary<Title, int> development,
        CultureMap cultures, FaithMap faiths, GovernmentMap governments,
        WildernessMap wilderness, PrehistoryMap prehistory,
        IReadOnlyDictionary<Title, string>? bookmarkDnaMap = null)
    {
        var all = Titles.Flatten(empires).Where(t => t.Tier == "c").ToList();
        if (all.Count == 0) return;

        var counties = all.Where(c => !wilderness.Contains(c)).ToList();
        var wild = all.Where(wilderness.Contains).ToList();

        if (counties.Count == 0) return;

        var dnaMap = bookmarkDnaMap ?? new Dictionary<Title, string>();

        WriteDynasties(modDir, prehistory);
        WriteDynastyHouses(modDir, prehistory);
        CoatOfArmsWriter.WriteAll(modDir, prehistory);
        WriteCharacters(modDir, cfg, counties, cultures, faiths, realms, governments, prehistory, dnaMap);
        WriteHeadOfFaithCharacters(modDir, cfg, faiths, cultures, counties);
        WriteWildernessHolder(modDir, cfg, wild);
        WriteHouseRelationsOnAction(modDir, prehistory);
        WriteTitleHistory(modDir, cfg, empires, development, realms, governments, faiths, wilderness, wild);
        WriteDynastyLocalisation(modDir, prehistory);
    }

    public static (string FirstName, string DynastyName) RulerNames(Title county, Culture culture)
    {
        var rng = new Rng(county.Index ^ 0x5A17);

        string first = culture.MaleNames.Count > 0
            ? culture.MaleNames[rng.Int(0, culture.MaleNames.Count - 1)]
            : culture.Name;

        string dynasty = culture.DynastyNames.Count > 0
            ? culture.DynastyNames[rng.Int(0, culture.DynastyNames.Count - 1)]
            : culture.Name;

        return (first, dynasty);
    }

    public static Title Primary(Title county, RealmMap realms)
    {
        var best = county;
        foreach (var (title, holder) in realms.HolderCounty)
        {
            if (holder == county && Rank(title) > Rank(best)) best = title;
        }

        return best;
    }

    public static int Rank(Title title) => title.Tier switch
    {
        "e" => 4,
        "k" => 3,
        "d" => 2,
        "c" => 1,
        _ => 0,
    };

    public static string CharacterId(Title county) => $"gen_char_{county.Index}";

    public static string DynastyId(Title county) => $"gen_dynasty_{county.Index}";

    public static int GetRulerBirthYear(int countyIndex, int startYear)
    {
        var rng = new Rng(countyIndex ^ 0x3E2D);
        return startYear - rng.Int(24, 50);
    }

    private static void WriteDynasties(string modDir, PrehistoryMap prehistory)
    {
        string dir = Path.Combine(modDir, "common", "dynasties");
        Directory.CreateDirectory(dir);

        var sb = new StringBuilder();
        sb.Append("# Generated Dynasties\n\n");

        foreach (var dyn in prehistory.Dynasties.Values)
        {
            sb.Append($"{dyn.Id} = {{\n");
            sb.Append($"\tname = \"{dyn.NameKey}\"\n");
            sb.Append($"\tculture = \"{dyn.CultureKey}\"\n");
            sb.Append("}\n\n");
        }

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_dynasties.txt"), sb.ToString());
    }

    private static void WriteDynastyHouses(string modDir, PrehistoryMap prehistory)
    {
        string dir = Path.Combine(modDir, "common", "dynasty_houses");
        Directory.CreateDirectory(dir);

        var sb = new StringBuilder();
        sb.Append("# Generated Dynasty Houses & Cadet Branches\n\n");

        foreach (var house in prehistory.Houses.Values)
        {
            sb.Append($"{house.Key} = {{\n");
            if (house.Prefix is not null)
            {
                sb.Append($"\tprefix = \"{house.Prefix}\"\n");
            }
            sb.Append($"\tname = \"{house.NameKey}\"\n");
            sb.Append($"\tdynasty = {house.DynastyId}\n");
            sb.Append("}\n\n");
        }

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_houses.txt"), sb.ToString());
    }

    private static void WriteCharacters(string modDir, MapConfig cfg, List<Title> counties,
        CultureMap cultures, FaithMap faiths, RealmMap realms, GovernmentMap governments,
        PrehistoryMap prehistory, IReadOnlyDictionary<Title, string> bookmarkDnaMap)
    {
        string dir = Path.Combine(modDir, "history", "characters");
        Directory.CreateDirectory(dir);

        // 1. Clean up old leftover spouse files so CK3-tiger doesn't flag duplicate character IDs
        string oldSpousesFile = Path.Combine(dir, "04_generated_spouses.txt");
        if (File.Exists(oldSpousesFile))
        {
            File.Delete(oldSpousesFile);
        }

        var sb = new StringBuilder();
        sb.Append("# Generated Living Rulers, Ancestors, Spouses & Heirs\n\n");

        // =========================================================================
        // 2. Deceased Ancestors (Fathers) — Stamped with historical birth and death
        // =========================================================================
        foreach (var ancestor in prehistory.AllExtraCharacters.Where(c => c.IsDeadAncestor))
        {
            sb.Append($"{ancestor.Id} = {{\n");
            sb.Append($"\tname = \"{ancestor.Name}\"\n");
            sb.Append($"\tdynasty_house = {ancestor.DynastyHouseKey ?? ancestor.DynastyId}\n");
            sb.Append($"\treligion = {ancestor.FaithKey}\n");
            sb.Append($"\tculture = {ancestor.CultureKey}\n");
            sb.Append($"\t{ancestor.BirthDate} = {{ birth = yes }}\n");
            sb.Append($"\t{ancestor.DeathDate} = {{ death = yes }}\n");
            sb.Append("}\n\n");
        }

        // =========================================================================
        // 3. Living Rulers — Chronological timeline of wedding, alliances, and rivals
        // =========================================================================
        foreach (var county in counties)
        {
            var culture = cultures.For(county);
            var (firstName, _) = RulerNames(county, culture);
            var primaryTitle = Primary(county, realms);
            var rng = new Rng(county.Index ^ 0x3E2D);

            int birthYear = GetRulerBirthYear(county.Index, cfg.StartYear);
            string birthDate = $"{birthYear}.{rng.Int(1, 12)}.{rng.Int(1, 28)}";

            string dynId = prehistory.CharacterDynastyMap.GetValueOrDefault(county, DynastyId(county));
            string houseKey = prehistory.CharacterHouseMap.GetValueOrDefault(county, $"house_gen_{county.Index}");
            string? fatherId = prehistory.DeceasedParents.TryGetValue(county, out var f) ? f.Id : null;

            int gold = primaryTitle.Tier switch
            {
                "e" => rng.Int(450, 650),
                "k" => rng.Int(250, 380),
                "d" => rng.Int(120, 180),
                _ => rng.Int(60, 90)
            };
            int prestige = primaryTitle.Tier switch
            {
                "e" => rng.Int(350, 550),
                "k" => rng.Int(200, 350),
                "d" => rng.Int(90, 130),
                _ => rng.Int(35, 65)
            };
            int renown = primaryTitle.Tier switch
            {
                "e" => rng.Int(4000, 7000),
                "k" => rng.Int(2000, 4000),
                "d" => rng.Int(900, 1600),
                _ => rng.Int(150, 450)
            };

            sb.Append($"{CharacterId(county)} = {{\n");
            sb.Append($"\tname = \"{firstName}\"\n");

            if (bookmarkDnaMap.TryGetValue(county, out string? dnaKey))
            {
                sb.Append($"\tdna = {dnaKey}\n");
            }

            sb.Append($"\tdynasty_house = {houseKey}\n");
            sb.Append($"\treligion = {faiths.For(county).Key}\n");
            sb.Append($"\tculture = {culture.Key}\n");

            if (fatherId is not null)
            {
                sb.Append($"\tfather = {fatherId}\n");
            }

            // --- Character Birth Date ---
            sb.Append($"\t{birthDate} = {{ birth = yes }}\n");

            // --- Simulated Wedding Date ---
            if (prehistory.Spouses.TryGetValue(county, out var spouse) && spouse.MarriageDate != null)
            {
                sb.Append($"\t{spouse.MarriageDate} = {{\n");
                sb.Append($"\t\tadd_spouse = {spouse.Id}\n");
                sb.Append("\t}\n");
            }

            // --- Chronologically Dated Alliances (with explicit marriage scopes) ---
            if (prehistory.Alliances.TryGetValue(county, out var allies))
            {
                foreach (var allyLink in allies)
                {
                    string targetCharId = CharacterId(allyLink.PartnerCounty);
                    string ownerThrough = allyLink.ThroughSpouseId ?? CharacterId(county);
                    string targetThrough = allyLink.ThroughPartnerId ?? targetCharId;

                    sb.Append($"\t{allyLink.FormationDate} = {{\n");
                    sb.Append("\t\teffect = {\n");
                    sb.Append($"\t\t\tcreate_alliance = {{\n");
                    sb.Append($"\t\t\t\ttarget = character:{targetCharId}\n");
                    sb.Append($"\t\t\t\tallied_through_owner = character:{ownerThrough}\n");
                    sb.Append($"\t\t\t\tallied_through_target = character:{targetThrough}\n");
                    sb.Append("\t\t\t}\n");
                    sb.Append("\t\t}\n");
                    sb.Append("\t}\n");
                }
            }

            // --- Chronologically Dated Rivalries ---
            if (prehistory.Rivals.TryGetValue(county, out var rivals))
            {
                foreach (var rival in rivals)
                {
                    sb.Append($"\t{rival.Date} = {{\n");
                    sb.Append("\t\teffect = {\n");
                    sb.Append($"\t\t\tset_relation_rival = character:{CharacterId(rival.TargetCounty)}\n");
                    sb.Append("\t\t}\n");
                    sb.Append("\t}\n");
                }
            }

            // --- Chronologically Dated Friendships ---
            if (prehistory.Friends.TryGetValue(county, out var friends))
            {
                foreach (var friend in friends)
                {
                    sb.Append($"\t{friend.Date} = {{\n");
                    sb.Append("\t\teffect = {\n");
                    sb.Append($"\t\t\tset_relation_friend = character:{CharacterId(friend.TargetCounty)}\n");
                    sb.Append("\t\t}\n");
                    sb.Append("\t}\n");
                }
            }

            // --- Game Start Date (Currencies, Truces, Claims & Modifiers) ---
            sb.Append($"\t{cfg.StartDate} = {{\n");
            sb.Append("\t\teffect = {\n");

            switch (governments.For(county))
            {
                case GovernmentMap.Tribal:
                    gold = (int)(gold * 0.45);
                    prestige = (int)(prestige * 1.6);
                    break;
                case GovernmentMap.Republic:
                    gold = (int)(gold * 1.8);
                    prestige = (int)(prestige * 0.7);
                    break;
            }

            sb.Append($"\t\t\tadd_gold = {gold}\n");
            sb.Append($"\t\t\tadd_prestige = {prestige}\n");

            if (renown > 0)
            {
                sb.Append($"\t\t\tdynasty = {{ add_dynasty_prestige = {renown} }}\n");
            }

            // Claims
            if (prehistory.Claims.TryGetValue(county, out var claims))
            {
                foreach (var (targetTitle, pressed) in claims)
                {
                    string claimCmd = pressed ? "add_pressed_claim" : "add_unpressed_claim";
                    sb.Append($"\t\t\t{claimCmd} = title:{targetTitle.Key}\n");
                }
            }

            // Truces
            if (prehistory.Truces.TryGetValue(county, out var truces))
            {
                foreach (var (truceTarget, days) in truces)
                {
                    sb.Append($"\t\t\tadd_truce_both_ways = {{ character = character:{CharacterId(truceTarget)} days = {days} }}\n");
                }
            }

            bool isIndependent = !realms.Liege.ContainsKey(primaryTitle);
            bool isHigherTier = primaryTitle.Tier is "d" or "k" or "e";

            if (isIndependent || isHigherTier)
            {
                sb.Append("\t\t\tadd_character_modifier = {\n");
                sb.Append("\t\t\t\tmodifier = gen_early_realm_stability\n");
                sb.Append("\t\t\t\tyears = 3\n");
                sb.Append("\t\t\t}\n");
            }

            sb.Append("\t\t}\n");
            sb.Append("\t}\n");

            // Living characters do NOT have death = yes
            sb.Append("}\n\n");
        }

        // =========================================================================
        // 4. Living Spouses & Children — Linked with biological parents & houses
        // =========================================================================
        foreach (var character in prehistory.AllExtraCharacters.Where(c => !c.IsDeadAncestor))
        {
            sb.Append($"{character.Id} = {{\n");
            sb.Append($"\tname = \"{character.Name}\"\n");
            if (character.Female) sb.Append("\tfemale = yes\n");

            sb.Append($"\tdynasty_house = {character.DynastyHouseKey ?? character.DynastyId}\n");
            sb.Append($"\treligion = {character.FaithKey}\n");
            sb.Append($"\tculture = {character.CultureKey}\n");

            if (character.FatherId is not null) sb.Append($"\tfather = {character.FatherId}\n");
            if (character.MotherId is not null) sb.Append($"\tmother = {character.MotherId}\n");

            sb.Append($"\t{character.BirthDate} = {{ birth = yes }}\n");
            sb.Append("}\n\n");
        }

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_characters.txt"), sb.ToString());
    }
    private static void WriteHeadOfFaithCharacters(string modDir, MapConfig cfg,
        FaithMap faiths, CultureMap cultures, List<Title> counties)
    {
        string dir = Path.Combine(modDir, "history", "characters");
        Directory.CreateDirectory(dir);

        var sb = new StringBuilder();
        int hofIndex = 0;

        foreach (var faith in faiths.Faiths)
        {
            if (faith.Head is null) continue;

            var sampleCounty = counties.FirstOrDefault(c => faiths.For(c) == faith) ?? counties[0];
            var culture = cultures.For(sampleCounty);
            var (firstName, _) = RulerNames(sampleCounty, culture);

            var rng = new Rng(faith.Key.GetHashCode() ^ 0x48A1);
            int birthYear = cfg.StartYear - rng.Int(35, 60);

            sb.Append($"gen_hof_{hofIndex++} = {{\n");
            sb.Append($"\tname = \"{firstName}\"\n");
            sb.Append($"\treligion = {faith.Key}\n");
            sb.Append($"\tculture = {culture.Key}\n");
            sb.Append($"\t{birthYear}.1.1 = {{ birth = yes }}\n");
            sb.Append($"\t{cfg.StartDate} = {{\n");
            sb.Append("\t\teffect = {\n");
            sb.Append("\t\t\tadd_gold = 150\n");
            sb.Append("\t\t\tadd_piety = 250\n");
            sb.Append("\t\t}\n");
            sb.Append("\t}\n");
            sb.Append("}\n\n");
        }

        if (hofIndex > 0)
        {
            ParadoxText.WriteBom(Path.Combine(dir, "02_generated_head_of_faith.txt"), sb.ToString());
        }
    }

    private static void WriteWildernessHolder(string modDir, MapConfig cfg, List<Title> wild)
    {
        if (wild.Count == 0) return;

        string dir = Path.Combine(modDir, "history", "characters");
        Directory.CreateDirectory(dir);

        var sb = new StringBuilder();
        sb.Append("# The holder of every unsettled county. See MapGen/Wilderness.cs.\n\n");
        sb.Append($"{WildernessMap.HolderId} = {{\n");
        sb.Append("\tname = \"wilderness_holder_name\"\n");
        sb.Append($"\treligion = {MapGen.Faiths.UnsettledFaithKey}\n");
        sb.Append($"\tculture = {MapGen.Cultures.UnsettledKey}\n");
        sb.Append("\tdisallow_random_traits = yes\n");
        sb.Append("\tsexuality = asexual\n");

        sb.Append($"\t{Math.Max(1, cfg.StartYear - 1000)}.1.1 = {{\n");
        sb.Append("\t\tbirth = yes\n");
        sb.Append("\t\ttrait = wilderness\n");
        sb.Append("\t\ttrait = immortal\n");
        sb.Append("\t}\n");
        sb.Append("}\n\n");

        ParadoxText.WriteBom(Path.Combine(dir, "01_generated_wilderness.txt"), sb.ToString());
    }

    private static void WriteHouseRelationsOnAction(string modDir, PrehistoryMap prehistory)
    {
        if (prehistory.HouseRelations.Count == 0) return;

        string dir = Path.Combine(modDir, "common", "on_action");
        Directory.CreateDirectory(dir);

        var sb = new StringBuilder();
        sb.Append("# Active House Feuds and Dynastic Amities on Day 1\n\n");

        sb.Append("on_game_start_after_lobby = {\n");
        sb.Append("\ton_actions = {\n");
        sb.Append("\t\tgen_start_house_relations\n");
        sb.Append("\t}\n");
        sb.Append("}\n\n");

        sb.Append("gen_start_house_relations = {\n");
        sb.Append("\teffect = {\n");

        for (int i = 0; i < prehistory.HouseRelations.Count; i++)
        {
            var rel = prehistory.HouseRelations[i];
            string descKey = $"gen_house_relation_{i}_desc";
            rel.DescriptionKey = descKey;

            sb.Append($"\t\thouse:{rel.HouseA} = {{\n");
            sb.Append("\t\t\tset_house_relation = {\n");
            sb.Append($"\t\t\t\ttarget = house:{rel.HouseB}\n");
            sb.Append($"\t\t\t\tlevel = {rel.Level}\n");
            sb.Append($"\t\t\t\tdescription = {descKey}\n");
            sb.Append("\t\t\t}\n");
            sb.Append("\t\t}\n");
        }

        sb.Append("\t}\n");
        sb.Append("}\n");

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_house_relations.txt"), sb.ToString());
    }
    private static void WriteTitleHistory(string modDir, MapConfig cfg, List<Title> empires,
        Dictionary<Title, int> development, RealmMap realms, GovernmentMap governments,
        FaithMap faiths, WildernessMap wilderness, List<Title> wild)
    {
        string dir = Path.Combine(modDir, "history", "titles");
        Directory.CreateDirectory(dir);

        var sb = new StringBuilder();

        int reignStartYear = Math.Max(1, cfg.StartYear - 5);
        string titleGrantDate = $"{reignStartYear}.1.1";

        foreach (var title in Titles.Flatten(empires))
        {
            if (wilderness.Contains(title)) continue;
            if (!realms.HolderCounty.TryGetValue(title, out var holder)) continue;
            if (wilderness.Contains(holder)) continue;

            int level = title.Tier == "c" ? development.GetValueOrDefault(title) : 0;
            realms.Liege.TryGetValue(title, out var liege);
            string government = governments.For(holder);

            sb.Append($"{title.Key} = {{\n");
            sb.Append($"\t{titleGrantDate} = {{\n");
            sb.Append($"\t\tholder = {CharacterId(holder)}\n");

            if (government != GovernmentMap.Feudal)
            {
                sb.Append($"\t\tgovernment = {government}\n");
            }

            if (liege is not null)
            {
                sb.Append($"\t\tliege = {liege.Key}\n");
            }

            if (level > 0)
            {
                sb.Append($"\t\tchange_development_level = {level}\n");
            }

            sb.Append("\t}\n");
            sb.Append("}\n");
        }

        if (wild.Count > 0)
        {
            sb.Append($"{WildernessMap.TitleKey} = {{\n");
            sb.Append($"\t{cfg.StartDate} = {{\n");
            sb.Append($"\t\tholder = {WildernessMap.HolderId}\n");
            sb.Append("\t\tgovernment = wilderness_government\n");
            sb.Append("\t}\n");
            sb.Append("}\n");
        }

        foreach (var county in wild)
        {
            sb.Append($"{county.Key} = {{\n");
            sb.Append($"\t{cfg.StartDate} = {{\n");
            sb.Append($"\t\tholder = {WildernessMap.HolderId}\n");
            sb.Append("\t\tgovernment = wilderness_government\n");
            sb.Append("\t}\n");
            sb.Append("}\n");
        }

        int hofIndex = 0;
        foreach (var faith in faiths.Faiths)
        {
            if (faith.Head is null) continue;

            sb.Append($"{faith.Head.TitleKey} = {{\n");
            sb.Append($"\t{titleGrantDate} = {{\n");
            sb.Append($"\t\tholder = gen_hof_{hofIndex++}\n");
            sb.Append("\t\tgovernment = theocracy_government\n");
            sb.Append("\t}\n");
            sb.Append("}\n");
        }

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_titles.txt"), sb.ToString());
    }

    private static void WriteDynastyLocalisation(string modDir, PrehistoryMap prehistory)
    {
        string dir = Path.Combine(modDir, "localization", "english");
        Directory.CreateDirectory(dir);

        var sb = new StringBuilder();
        sb.Append("l_english:\n");

        // Generic fallback descriptions
        sb.Append(" house_relation_reason_preexisting_marriage_desc:0 \"Royal marriage alliance established between dynasties\"\n");
        sb.Append(" house_relation_reason_traditional_friendship_desc:0 \"Traditional dynastic friendship enduring across generations\"\n");
        sb.Append(" house_relation_reason_ancient_rivalry_desc:0 \"Generational border rivalry and ancestral disputes\"\n");
        sb.Append(" house_relation_reason_blood_feud_desc:0 \"Bitter generational blood feud and contested sovereignty\"\n\n");

        // Specific house relation descriptions embedding the real prehistory start date
        for (int i = 0; i < prehistory.HouseRelations.Count; i++)
        {
            var rel = prehistory.HouseRelations[i];
            string key = rel.DescriptionKey ?? $"gen_house_relation_{i}_desc";
            string yearStr = !string.IsNullOrEmpty(rel.StartDate) && rel.StartDate.Contains('.')
                ? rel.StartDate.Split('.')[0] + " AD"
                : (!string.IsNullOrEmpty(rel.StartDate) ? rel.StartDate + " AD" : "ancient times");

            string desc = rel.Level switch
            {
                "feud" => $"Bitter generational blood feud and contested sovereignty (active since {yearStr})",
                "rivalry" => $"Generational border rivalry and ancestral disputes (since {yearStr})",
                "quarrel" => $"Simmering border quarrel and ancestral disputes (since {yearStr})",
                "amity" => $"Royal marriage alliance established between houses (concluded in {yearStr})",
                "friendly" => $"Traditional dynastic friendship enduring across generations (since {yearStr})",
                "cordial" => $"Cordial diplomatic ties and mutual respect (established in {yearStr})",
                _ => $"Traditional dynastic relations (established in {yearStr})"
            };

            sb.Append($" {key}:0 \"{desc}\"\n");
        }
        sb.Append("\n");

        var writtenKeys = new HashSet<string>();

        foreach (var dyn in prehistory.Dynasties.Values)
        {
            if (writtenKeys.Add(dyn.NameKey))
                sb.Append($" {dyn.NameKey}: \"{dyn.LocalizedName}\"\n");
        }

        foreach (var house in prehistory.Houses.Values)
        {
            if (writtenKeys.Add(house.NameKey))
                sb.Append($" {house.NameKey}: \"{house.LocalizedName}\"\n");
        }

        ParadoxText.WriteBom(Path.Combine(dir, "gen_dynasties_l_english.yml"), sb.ToString());
    }
}