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
        CultureMap cultures, EthnicityMap ethnicities, FaithMap faiths, GovernmentMap governments,
        WildernessMap wilderness, PrehistoryMap prehistory,
        IReadOnlyDictionary<Title, string>? bookmarkDnaMap = null)
    {
        var all = Titles.Flatten(empires).Where(t => t.Tier == "c").ToList();
        if (all.Count == 0) return;

        // One character per RULER, not per county. The two used to be the same thing — every county
        // held itself — but a liege's personal demesne now covers several counties under one man,
        // and writing a count for each of them would put a landless stranger beside every lord.
        var rulers = realms.HolderCounty.Values.ToHashSet();

        var counties = all.Where(c => !wilderness.Contains(c) && rulers.Contains(c)).ToList();
        var wild = all.Where(wilderness.Contains).ToList();

        if (counties.Count == 0) return;

        var dnaMap = bookmarkDnaMap ?? new Dictionary<Title, string>();

        WriteDynasties(modDir, prehistory);
        WriteDynastyHouses(modDir, prehistory);
        CoatOfArmsWriter.WriteAll(modDir, prehistory);
        WriteCharacters(modDir, cfg, counties, cultures, ethnicities, faiths, realms, governments,
            prehistory, dnaMap);
        WriteHeadOfFaithCharacters(modDir, cfg, faiths, cultures, ethnicities, counties);
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
        CultureMap cultures, EthnicityMap ethnicities, FaithMap faiths, RealmMap realms,
        GovernmentMap governments, PrehistoryMap prehistory,
        IReadOnlyDictionary<Title, string> bookmarkDnaMap)
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
            if (ancestor.Female) sb.Append("\tfemale = yes\n");

            // A house and a dynasty are different keys to CK3, and pointing dynasty_house at
            // a dynasty id makes the character landless of no house at all rather than the
            // founder of one.
            if (ancestor.DynastyHouseKey is not null)
                sb.Append($"\tdynasty_house = {ancestor.DynastyHouseKey}\n");
            else
                sb.Append($"\tdynasty = {ancestor.DynastyId}\n");
            sb.Append($"\treligion = {ancestor.FaithKey}\n");
            sb.Append($"\tculture = {ancestor.CultureKey}\n");

            var ancestorCulture = cultures.Cultures.FirstOrDefault(c => c.Key == ancestor.CultureKey);
            if (ancestorCulture is not null)
            {
                string? ancestorTrait = GetPhenotypeTrait(ancestorCulture, ethnicities, cfg);
                if (ancestorTrait is not null)
                    sb.Append($"\ttrait = {ancestorTrait}\n");
            }
            sb.Append($"\t{ancestor.BirthDate} = {{ birth = yes }}\n");
            sb.Append($"\t{ancestor.DeathDate} = {{ death = yes }}\n");
            sb.Append("}\n\n");
        }

        // Which rulers actually have someone answering to them. Only they need the standing that
        // holds a court together, and writing it for a lone count would just be free stats.
        var liegeCounties = realms.Liege.Values
            .Select(t => realms.HolderCounty.GetValueOrDefault(t))
            .Where(c => c is not null)
            .ToHashSet();

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

            // Everything about the man rather than the land — schooling, skills, byname, the
            // standing he starts with. See Emit/RulerProfile.cs for what each number is worth.
            var profile = RulerProfile.Build(
                county, primaryTitle.Tier, governments.For(county), culture.Ethos,
                cfg.StartYear - birthYear, liegeCounties.Contains(county));

            string dynId = prehistory.CharacterDynastyMap.GetValueOrDefault(county, DynastyId(county));
            string houseKey = prehistory.CharacterHouseMap.GetValueOrDefault(county, $"house_gen_{county.Index}");
            string? fatherId = prehistory.DeceasedParents.TryGetValue(county, out var f) ? f.Id : null;

            int gold = primaryTitle.Tier switch
            {
                "e" => rng.Int(850, 1200),
                "k" => rng.Int(480, 700),
                "d" => rng.Int(150, 210),
                _ => rng.Int(60, 90)
            };

            // Prestige is graded against the thresholds, not to taste. Vanilla's defines put
            // LEVELS_PRESTIGE at { 1000 2000 5000 10000 25000 }, and prestige LEVEL is an opinion
            // modifier on everyone — PRESTIGIOUS = { -10 0 5 10 20 30 } — so a starting emperor on
            // 500 prestige was not merely poor, he was standing at the level that pays nothing while
            // his vassals judged him. Kings and emperors are now written above the second threshold
            // under either reading of it, which is worth +5 opinion realm-wide and reads on the
            // character sheet as a crowned ruler rather than a jumped-up count.
            //
            // Counts are left where they were on purpose: the ladder only means something if the
            // bottom of it stays modest.
            int prestige = primaryTitle.Tier switch
            {
                "e" => rng.Int(3400, 4600),
                "k" => rng.Int(2000, 2700),
                "d" => rng.Int(350, 600),
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

            // Base skills, in vanilla's own order. Written rather than left out because an omitted
            // skill is rolled by the engine from RANDOM_CHARACTER_*_MIN/MAX — a flat 0-10 that takes
            // no notice of whether the character is an emperor or a backwater count.
            sb.Append($"\tmartial = {profile.Martial}\n");
            sb.Append($"\tprowess = {profile.Prowess}\n");
            sb.Append($"\tdiplomacy = {profile.Diplomacy}\n");
            sb.Append($"\tintrigue = {profile.Intrigue}\n");
            sb.Append($"\tstewardship = {profile.Stewardship}\n");
            sb.Append($"\tlearning = {profile.Learning}\n");

            sb.Append($"\treligion = {faiths.For(county).Key}\n");
            sb.Append($"\tculture = {culture.Key}\n");

            // The education trait. Left unwritten, the engine picks one at random for every ruler on
            // the map, so a khan was as likely to have been raised a scholar as a soldier and no
            // ruler's schooling had anything to do with the realm he was raised in. Written here it
            // also becomes something the rest of this block can lean on: it names the lifestyle the
            // perk points below are spendable in.
            sb.Append($"\ttrait = {profile.EducationTrait}\n");

            // Exactly 3 non-conflicting Personality traits (brave, greedy, just, etc.)
            foreach (string personalityTrait in profile.PersonalityTraits)
            {
                sb.Append($"\ttrait = {personalityTrait}\n");
            }

            // Other traits (congenitals, commander traits, hobbies, scars, coping mechanisms)
            foreach (string otherTrait in profile.OtherTraits)
            {
                sb.Append($"\ttrait = {otherTrait}\n");
            }

            if (GetPhenotypeTrait(culture, ethnicities, cfg) is { } rulerTrait)
                sb.Append($"\ttrait = {rulerTrait}\n");

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
                    // Every link is stored on both counties; create_alliance is symmetric, so
                    // emitting from both sides created each alliance twice.
                    if (county.Index > allyLink.PartnerCounty.Index) continue;

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

            // --- Sworn Blood Brothers (nomad khans and their anda) ---
            if (prehistory.BloodBrothers.TryGetValue(county, out var bloodBrothers))
            {
                foreach (var brother in bloodBrothers)
                {
                    sb.Append($"\t{brother.Date} = {{\n");
                    sb.Append("\t\teffect = {\n");
                    sb.Append($"\t\t\tset_relation_blood_brother = character:{CharacterId(brother.TargetCounty)}\n");
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

            // Renown only for rulers who answer to nobody. A vassal's house does not gain standing
            // for holding what its liege granted it, and handing it out regardless made every
            // dynasty on the map start equally renowned.
            bool independent = !realms.Liege.ContainsKey(primaryTitle);

            if (renown > 0 && independent)
            {
                sb.Append($"\t\t\tdynasty = {{ add_dynasty_prestige = {renown} }}\n");
            }

            // Lifestyle perk points, in the tree his education belongs to. Vanilla already
            // auto-assigns baseline perks on game start for adult characters based on age and
            // education; these points provide the explicit bonus reflecting high rank, leisure,
            // and top-tier tutors.
            if (profile.PerkPoints > 0)
            {
                sb.Append($"\t\t\tadd_{profile.Lifestyle}_lifestyle_perk_points = {profile.PerkPoints}\n");
            }

            if (profile.SecondLifestyle is not null && profile.SecondPerkPoints > 0)
            {
                sb.Append($"\t\t\tadd_{profile.SecondLifestyle}_lifestyle_perk_points = " +
                          $"{profile.SecondPerkPoints}\n");
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
                    // Written from one side only. add_truce_both_ways already binds both, so
                    // emitting it from each partner in turn set the same truce twice and the second
                    // one silently restarted its clock.
                    if (county.Index >= truceTarget.Index) continue;

                    sb.Append($"\t\t\tadd_truce_both_ways = {{ character = character:{CharacterId(truceTarget)} days = {days} }}\n");
                }
            }


            // The standing of a man who has people to hold, written only for rulers who have any.
            //
            // obedience_value docks a subject 5 for an overlord whose dread is under 10 and 15 for
            // one whose legitimacy has not reached level 3, and pays back half the overlord's dread
            // and a flat 25 once both clear. Left unwritten — as they were — every khan on the map
            // started feared by nobody and legitimate to nobody, which is 40 points of a 100-point
            // obedience threshold given away before anything else is counted. The argument was never
            // specific to nomads: a generated king inherits a realm of strangers on the same terms,
            // so RulerProfile now grades both by tier and hands the khans the same numbers they had.
            //
            // Republics and theocracies are skipped for legitimacy — their government types do not
            // declare `legitimacy = yes`, so there is no currency there to add to.
            if (profile.Dread > 0)
            {
                sb.Append($"\t\t\tadd_dread = {profile.Dread}\n");
            }

            if (profile.Legitimacy is not null)
            {
                sb.Append($"\t\t\tadd_legitimacy = {profile.Legitimacy}\n");
            }

            bool isHigherTier = primaryTitle.Tier is "d" or "k" or "e";

            // The grace period, scaled by how much realm there is to settle. Three years is enough
            // for a duke's handful of vassals to get used to him; an emperor's crown vassals are
            // themselves kings with their own inheritances to digest, and a window that closes on
            // all of them at once, at the same moment as every other realm on the map, is what turns
            // year four of a generated world into a simultaneous continent-wide civil war.
            if (independent || isHigherTier)
            {
                sb.Append("\t\t\tadd_character_modifier = {\n");
                sb.Append("\t\t\t\tmodifier = gen_early_realm_stability\n");
                sb.Append($"\t\t\t\tyears = {profile.StabilityYears}\n");
                sb.Append("\t\t\t}\n");
            }

            sb.Append("\t\t}\n");

            // A byname, for the few who have earned one. Sits beside the effect block rather than
            // inside it because that is where vanilla's own history puts give_nickname.
            if (profile.Nickname is not null)
            {
                sb.Append($"\t\tgive_nickname = {profile.Nickname}\n");
            }

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

            // Same distinction as the ancestors above: a dynasty id is not a house id, and putting
            // one in dynasty_house leaves the character in no house at all.
            if (character.DynastyHouseKey is not null)
                sb.Append($"\tdynasty_house = {character.DynastyHouseKey}\n");
            else
                sb.Append($"\tdynasty = {character.DynastyId}\n");
            sb.Append($"\treligion = {character.FaithKey}\n");
            sb.Append($"\tculture = {character.CultureKey}\n");

            var characterCulture = cultures.Cultures.FirstOrDefault(c => c.Key == character.CultureKey);
            if (characterCulture is not null)
            {
                string? characterTrait = GetPhenotypeTrait(characterCulture, ethnicities, cfg);
                if (characterTrait is not null)
                    sb.Append($"\ttrait = {characterTrait}\n");
            }

            if (character.FatherId is not null) sb.Append($"\tfather = {character.FatherId}\n");
            if (character.MotherId is not null) sb.Append($"\tmother = {character.MotherId}\n");

            sb.Append($"\t{character.BirthDate} = {{ birth = yes }}\n");
            sb.Append("}\n\n");
        }

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_characters.txt"), sb.ToString());
    }
    private static void WriteHeadOfFaithCharacters(string modDir, MapConfig cfg,
        FaithMap faiths, CultureMap cultures, EthnicityMap ethnicities, List<Title> counties)
    {
        string dir = Path.Combine(modDir, "history", "characters");
        Directory.CreateDirectory(dir);

        var sb = new StringBuilder();
        int hofIndex = 0;

        foreach (var faith in faiths.Faiths)
        {
            if (faith.Head is null)
            {
                continue;
            }

            var sampleCounty = counties.FirstOrDefault(c => faiths.For(c) == faith) ?? counties[0];
            var culture = cultures.For(sampleCounty);
            var (firstName, _) = RulerNames(sampleCounty, culture);

            var rng = new Rng(Rng.StableHash(faith.Key) ^ 0x48A1UL);
            int birthYear = cfg.StartYear - rng.Int(35, 60);

            sb.Append($"gen_hof_{hofIndex++} = {{\n");
            sb.Append($"\tname = \"{firstName}\"\n");

            if (culture is not null)
            {
                string? hofTrait = GetPhenotypeTrait(culture, ethnicities, cfg);
                if (hofTrait is not null)
                    sb.Append($"\ttrait = {hofTrait}\n");
            }

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
            if (faith.Head is null)
            {
                continue;
            }

            sb.Append($"{faith.Head.TitleKey} = {{\n");
            sb.Append($"\t{titleGrantDate} = {{\n");
            sb.Append($"\t\tholder = gen_hof_{hofIndex++}\n");
            sb.Append("\t\tgovernment = theocracy_government\n");
            sb.Append("\t}\n");
            sb.Append("}\n");
        }

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_titles.txt"), sb.ToString());
    }

    /// <summary>
    /// The trait that carries a culture's build, or null for the ones that need none.
    ///
    /// Written onto the character rather than left to the portrait alone because a phenotype the
    /// game does not know about is only a look: the trait is what makes a dwarf's height and an
    /// orc's frame survive inheritance, show in the character sheet, and reach the AI.
    ///
    /// On a fantasy map humans are a race among races and get a visible trait of their own —
    /// phenotype_human — which is what lets them take part in the same/opposite-opinion web and
    /// what the culture pulse copies onto engine-generated human courtiers from the culture head,
    /// like any other race. On a realistic map the traits do not exist at all (the Fantasy file
    /// set is not shipped — see <see cref="StaticFileWriter.Fantasy"/>), so human cultures must
    /// map to null there or every history character would reference an undefined trait.
    /// </summary>
    private static string? GetPhenotypeTrait(Culture culture, EthnicityMap ethnicityMap, MapConfig cfg)
    {
        var ethnicity = ethnicityMap.For(culture);

        return ethnicity.Archetype switch
        {
            RaceArchetype.HighElf or RaceArchetype.WoodElf => "phenotype_gracile",
            RaceArchetype.Dwarf => "phenotype_stocky",
            RaceArchetype.Orc => "phenotype_rough_hewn",
            RaceArchetype.Giantkin => "phenotype_towering",
            RaceArchetype.Gnome => "phenotype_diminutive",
            RaceArchetype.Deepkin => "phenotype_dusk_adapted",
            RaceArchetype.Human when cfg.EnableFantasyEthnicities
                && cfg.RaceMode != MapConfig.FantasyRaceMode.HumanOnly => "phenotype_human",
            _ => null,
        };
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