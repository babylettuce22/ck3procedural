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
        WildernessMap wilderness, PrehistoryMap prehistory, RulerMap rulers)
    {
        var all = Titles.Flatten(empires).Where(t => t.Tier == "c").ToList();
        if (all.Count == 0) return;

        // One character per RULER, not per county. The two used to be the same thing — every county
        // held itself — but a liege's personal demesne now covers several counties under one man,
        // and writing a count for each of them would put a landless stranger beside every lord.
        // RulerMap.Build made the same cut, so every county kept here has a ruler to look up.
        var seats = realms.HolderCounty.Values.ToHashSet();

        var counties = all.Where(c => !wilderness.Contains(c) && seats.Contains(c)).ToList();
        var wild = all.Where(wilderness.Contains).ToList();

        if (counties.Count == 0) return;

        WriteDynasties(modDir, prehistory);
        WriteDynastyHouses(modDir, prehistory);
        CoatOfArmsWriter.WriteAll(modDir, prehistory);
        WriteCharacters(modDir, cfg, cultures, ethnicities, prehistory, rulers);
        WriteHeadOfFaithCharacters(modDir, cfg, faiths, cultures, ethnicities, counties);
        WriteWildernessHolder(modDir, cfg, wild);
        WriteHouseRelationsOnAction(modDir, prehistory);
        WriteTitleHistory(modDir, cfg, empires, development, realms, governments, faiths, wilderness, wild);
        WriteDynastyLocalisation(modDir, prehistory);
    }

    /// <summary>
    /// Whether the ruler of a seat is a woman.
    ///
    /// Read from the faith rather than from <see cref="MapConfig.Gender"/> directly, so that the
    /// map agrees with its own laws county by county: the one realm in fifty whose religion leans
    /// the other way is ruled the other way too, instead of the setting's global average being
    /// sprayed evenly over a world whose doctrines vary.
    ///
    /// Its own stream, seeded from the seat, because it is asked twice — once by
    /// <see cref="MapGen.PrehistoryMap.Build"/>, which needs to know whose mother to bury and whom
    /// to marry the ruler to before any ruler object exists, and once by
    /// <see cref="MapGen.RulerMap.Build"/> afterwards. Both must get the same answer, and neither
    /// may disturb the draw the other is walking.
    /// </summary>
    public static bool RulerIsFemale(Title county, Faith faith)
    {
        double share = MapGen.Faiths.GenderOf(faith) switch
        {
            "doctrine_gender_female_dominated" => 0.95,
            "doctrine_gender_equal" => 0.45,
            _ => 0.05,
        };

        return new Rng(county.Index ^ 0x6ED5).Chance(share);
    }

    /// <summary>
    /// Whether a faith's head is a woman.
    ///
    /// Not flavour: <c>doctrine_clerical_gender_female_only</c> means women are the only clergy,
    /// and every generated head of faith was a man regardless — so a third of the worlds this
    /// generator has ever made crowned a man over a priesthood he could not have joined.
    /// </summary>
    public static bool ClergyIsFemale(Faith faith)
    {
        string clerical = faith.Religion.Doctrines.GetValueOrDefault("doctrine_clerical_gender", "");
        if (clerical == "doctrine_clerical_gender_female_only") return true;
        if (clerical == "doctrine_clerical_gender_male_only") return false;

        // An open priesthood still leans the way the faith does about everything else, rather than
        // being decided by a coin that knows nothing about the religion it is being flipped for.
        double share = MapGen.Faiths.GenderOf(faith) switch
        {
            "doctrine_gender_female_dominated" => 0.85,
            "doctrine_gender_equal" => 0.45,
            _ => 0.10,
        };

        return new Rng(Rng.StableHash(faith.Key) ^ 0x48A2UL).Chance(share);
    }

    public static (string FirstName, string DynastyName) RulerNames(Title county, Culture culture,
        bool female = false)
    {
        var rng = new Rng(county.Index ^ 0x5A17);

        var names = female ? culture.FemaleNames : culture.MaleNames;

        string first = names.Count > 0
            ? names[rng.Int(0, names.Count - 1)]
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

    /// <summary>
    /// Tier as a number, for <see cref="Primary"/> and everything that asks it which title stands
    /// for a ruler.
    ///
    /// **The hegemony is deliberately absent, and must stay absent.** Ranking it above empire makes
    /// it the hegemon's primary title, and `Primary` is what `Governments.TopLiege` groups realms by
    /// and what the government cascade then reads — so a crowned hegemon's realm stopped being
    /// scored as an empire and fell out of the administrative branch, taking 144 counties from
    /// administrative to tribal. Faiths are built after governments and read the tribal share, so
    /// two faiths lost their heads on top of it. None of that is what putting a title on a character
    /// should do. Falling through to 0 leaves the hegemon represented by their empire exactly as
    /// before, which is the whole point: the crown is additive.
    ///
    /// CK3 decides a real primary title at runtime and does not read this.
    /// </summary>
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

        var b = new JominiBuilder();
        b.Comment("Generated Dynasties");
        b.Blank();

        foreach (var dyn in prehistory.Dynasties.Values)
        {
            using (b.Block(dyn.Id))
            {
                b.Quoted("name", dyn.NameKey);
                b.Quoted("culture", dyn.CultureKey);
            }

            b.Blank();
        }

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_dynasties.txt"), b.ToString());
    }

    private static void WriteDynastyHouses(string modDir, PrehistoryMap prehistory)
    {
        string dir = Path.Combine(modDir, "common", "dynasty_houses");
        Directory.CreateDirectory(dir);

        var b = new JominiBuilder();
        b.Comment("Generated Dynasty Houses & Cadet Branches");
        b.Blank();

        foreach (var house in prehistory.Houses.Values)
        {
            using (b.Block(house.Key))
            {
                if (house.Prefix is not null) b.Quoted("prefix", house.Prefix);
                b.Quoted("name", house.NameKey);
                b.Field("dynasty", house.DynastyId);
            }

            b.Blank();
        }

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_houses.txt"), b.ToString());
    }

    /// <summary>
    /// Not private: a ruler edit after the write re-runs exactly this. See <see cref="WorldOverwrite"/>.
    /// </summary>
    internal static void WriteCharacters(string modDir, MapConfig cfg,
        CultureMap cultures, EthnicityMap ethnicities, PrehistoryMap prehistory, RulerMap rulers)
    {
        string dir = Path.Combine(modDir, "history", "characters");
        Directory.CreateDirectory(dir);

        // 1. Clean up old leftover spouse files so CK3-tiger doesn't flag duplicate character IDs
        string oldSpousesFile = Path.Combine(dir, "04_generated_spouses.txt");
        if (File.Exists(oldSpousesFile))
        {
            File.Delete(oldSpousesFile);
        }

        var b = new JominiBuilder();
        b.Comment("Generated Living Rulers, Ancestors, Spouses & Heirs");
        b.Blank();

        // A dated block wrapping a single effect body, which is how every relation below is stamped
        // onto its character.
        void DatedEffect(string date, Action body)
        {
            using (b.Block(date))
            using (b.Block("effect"))
                body();
        }

        // =========================================================================
        // 2. Deceased Ancestors (Fathers) — Stamped with historical birth and death
        // =========================================================================
        foreach (var ancestor in prehistory.AllExtraCharacters.Where(c => c.IsDeadAncestor))
        {
            using (b.Block(ancestor.Id))
            {
                b.Quoted("name", ancestor.Name);
                if (ancestor.Female) b.Field("female", "yes");

                // A house and a dynasty are different keys to CK3, and pointing dynasty_house at
                // a dynasty id makes the character landless of no house at all rather than the
                // founder of one.
                if (ancestor.DynastyHouseKey is not null)
                    b.Field("dynasty_house", ancestor.DynastyHouseKey);
                else
                    b.Field("dynasty", ancestor.DynastyId);

                b.Field("religion", ancestor.FaithKey);
                b.Field("culture", ancestor.CultureKey);

                var ancestorCulture = cultures.Cultures.FirstOrDefault(c => c.Key == ancestor.CultureKey);
                if (ancestorCulture is not null)
                    b.Field("trait", GetPhenotypeTrait(ancestorCulture, ethnicities, cfg));

                b.Inline(ancestor.BirthDate, "birth = yes");

                // Every ancestor generated here is given a death date, but the field is optional on
                // the record. Writing a missing one would emit a nameless ` = { death = yes }` and
                // CK3 abandons the whole file at that point, taking every later character with it.
                if (ancestor.DeathDate is not null)
                    b.Inline(ancestor.DeathDate, "death = yes");
            }

            b.Blank();
        }

        // =========================================================================
        // 3. Living Rulers — Chronological timeline of wedding, alliances, and rivals
        // =========================================================================
        foreach (var ruler in rulers.All)
        {
            // Everything decided about the man — name, birth, house, purse, and the profile of
            // schooling, skills and standing — was settled by RulerMap.Build. This block only
            // writes it, beside the relations prehistory built between him and everyone else.
            var county = ruler.Seat;
            var culture = ruler.Culture;
            var primaryTitle = ruler.PrimaryTitle;
            var profile = ruler.Profile;

            using (b.Block(ruler.Id))
            {
                b.Quoted("name", ruler.Name);
                if (ruler.Female) b.Field("female", "yes");

                b.Field("dna", ruler.DnaKey);
                b.Field("dynasty_house", ruler.HouseKey);

                // Base skills, in vanilla's own order. Written rather than left out because an omitted
                // skill is rolled by the engine from RANDOM_CHARACTER_*_MIN/MAX — a flat 0-10 that takes
                // no notice of whether the character is an emperor or a backwater count.
                b.Field("martial", profile.Martial);
                b.Field("prowess", profile.Prowess);
                b.Field("diplomacy", profile.Diplomacy);
                b.Field("intrigue", profile.Intrigue);
                b.Field("stewardship", profile.Stewardship);
                b.Field("learning", profile.Learning);

                b.Field("religion", ruler.Faith.Key);
                b.Field("culture", culture.Key);

                // The education trait. Left unwritten, the engine picks one at random for every ruler on
                // the map, so a khan was as likely to have been raised a scholar as a soldier and no
                // ruler's schooling had anything to do with the realm he was raised in. Written here it
                // also becomes something the rest of this block can lean on: it names the lifestyle the
                // perk points below are spendable in.
                b.Field("trait", profile.EducationTrait);

                // Exactly 3 non-conflicting Personality traits (brave, greedy, just, etc.)
                foreach (string personalityTrait in profile.PersonalityTraits) b.Field("trait", personalityTrait);

                // Other traits (congenitals, commander traits, hobbies, scars, coping mechanisms)
                foreach (string otherTrait in profile.OtherTraits) b.Field("trait", otherTrait);

                b.Field("trait", GetPhenotypeTrait(culture, ethnicities, cfg));
                b.Field(ruler.ParentIsMother ? "mother" : "father", ruler.ParentId);

                // --- Character Birth Date ---
                b.Inline(ruler.BirthDate, "birth = yes");

                // --- Simulated Wedding Date ---
                if (prehistory.Spouses.TryGetValue(county, out var spouse) && spouse.MarriageDate != null)
                    using (b.Block(spouse.MarriageDate))
                        b.Field("add_spouse", spouse.Id);

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

                        DatedEffect(allyLink.FormationDate, () =>
                        {
                            using (b.Block("create_alliance"))
                            {
                                b.Field("target", $"character:{targetCharId}");
                                b.Field("allied_through_owner", $"character:{ownerThrough}");
                                b.Field("allied_through_target", $"character:{targetThrough}");
                            }
                        });
                    }
                }

                // --- Chronologically Dated Rivalries ---
                if (prehistory.Rivals.TryGetValue(county, out var rivals))
                    foreach (var rival in rivals)
                        DatedEffect(rival.Date, () =>
                            b.Field("set_relation_rival", $"character:{CharacterId(rival.TargetCounty)}"));

                // --- Chronologically Dated Friendships ---
                if (prehistory.Friends.TryGetValue(county, out var friends))
                    foreach (var friend in friends)
                        DatedEffect(friend.Date, () =>
                            b.Field("set_relation_friend", $"character:{CharacterId(friend.TargetCounty)}"));

                // --- Sworn Blood Brothers (nomad khans and their anda) ---
                if (prehistory.BloodBrothers.TryGetValue(county, out var bloodBrothers))
                    foreach (var brother in bloodBrothers)
                        DatedEffect(brother.Date, () =>
                            b.Field("set_relation_blood_brother", $"character:{CharacterId(brother.TargetCounty)}"));

                // --- Game Start Date (Currencies, Truces, Claims & Modifiers) ---
                using (b.Block(cfg.StartDate))
                {
                    using (b.Block("effect"))
                    {
                        b.Field("add_gold", ruler.Gold);
                        b.Field("add_prestige", ruler.Prestige);

                        // Renown only for rulers who answer to nobody. A vassal's house does not gain standing
                        // for holding what its liege granted it, and handing it out regardless made every
                        // dynasty on the map start equally renowned.
                        bool independent = ruler.Independent;

                        if (ruler.Renown > 0 && independent)
                            b.Inline("dynasty", $"add_dynasty_prestige = {ruler.Renown}");

                        // Lifestyle perk points, in the tree his education belongs to. Vanilla already
                        // auto-assigns baseline perks on game start for adult characters based on age and
                        // education; these points provide the explicit bonus reflecting high rank, leisure,
                        // and top-tier tutors.
                        if (profile.PerkPoints > 0)
                            b.Field($"add_{profile.Lifestyle}_lifestyle_perk_points", profile.PerkPoints);

                        if (profile.SecondLifestyle is not null && profile.SecondPerkPoints > 0)
                            b.Field($"add_{profile.SecondLifestyle}_lifestyle_perk_points", profile.SecondPerkPoints);

                        // Claims
                        if (prehistory.Claims.TryGetValue(county, out var claims))
                            foreach (var (targetTitle, pressed) in claims)
                                b.Field(pressed ? "add_pressed_claim" : "add_unpressed_claim", $"title:{targetTitle.Key}");

                        // Truces
                        if (prehistory.Truces.TryGetValue(county, out var truces))
                        {
                            foreach (var (truceTarget, days) in truces)
                            {
                                // Written from one side only. add_truce_both_ways already binds both, so
                                // emitting it from each partner in turn set the same truce twice and the second
                                // one silently restarted its clock.
                                if (county.Index >= truceTarget.Index) continue;

                                b.Inline("add_truce_both_ways",
                                    $"character = character:{CharacterId(truceTarget)} days = {days}");
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
                        if (profile.Dread > 0) b.Field("add_dread", profile.Dread);
                        if (profile.Legitimacy is not null) b.Field("add_legitimacy", $"{profile.Legitimacy}");

                        bool isHigherTier = primaryTitle.Tier is "d" or "k" or "e";

                        // The grace period, scaled by how much realm there is to settle. Three years is enough
                        // for a duke's handful of vassals to get used to him; an emperor's crown vassals are
                        // themselves kings with their own inheritances to digest, and a window that closes on
                        // all of them at once, at the same moment as every other realm on the map, is what turns
                        // year four of a generated world into a simultaneous continent-wide civil war.
                        if (independent || isHigherTier)
                            using (b.Block("add_character_modifier"))
                            {
                                b.Field("modifier", "gen_early_realm_stability");
                                b.Field("years", profile.StabilityYears);
                            }
                    }

                    // A byname, for the few who have earned one. Sits beside the effect block rather than
                    // inside it because that is where vanilla's own history puts give_nickname.
                    b.Field("give_nickname", profile.Nickname);
                }

                // Living characters do NOT have death = yes
            }

            b.Blank();
        }

        // =========================================================================
        // 4. Living Spouses & Children — Linked with biological parents & houses
        // =========================================================================
        foreach (var character in prehistory.AllExtraCharacters.Where(c => !c.IsDeadAncestor))
        {
            using (b.Block(character.Id))
            {
                b.Quoted("name", character.Name);
                if (character.Female) b.Field("female", "yes");
                b.Field("dna", character.DnaKey);

                // Same distinction as the ancestors above: a dynasty id is not a house id, and putting
                // one in dynasty_house leaves the character in no house at all.
                if (character.DynastyHouseKey is not null)
                    b.Field("dynasty_house", character.DynastyHouseKey);
                else
                    b.Field("dynasty", character.DynastyId);

                b.Field("religion", character.FaithKey);
                b.Field("culture", character.CultureKey);

                var characterCulture = cultures.Cultures.FirstOrDefault(c => c.Key == character.CultureKey);
                if (characterCulture is not null)
                    b.Field("trait", GetPhenotypeTrait(characterCulture, ethnicities, cfg));

                b.Field("father", character.FatherId);
                b.Field("mother", character.MotherId);

                b.Inline(character.BirthDate, "birth = yes");
            }

            b.Blank();
        }

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_characters.txt"), b.ToString());
    }
    private static void WriteHeadOfFaithCharacters(string modDir, MapConfig cfg,
        FaithMap faiths, CultureMap cultures, EthnicityMap ethnicities, List<Title> counties)
    {
        string dir = Path.Combine(modDir, "history", "characters");
        Directory.CreateDirectory(dir);

        var b = new JominiBuilder();
        int hofIndex = 0;

        foreach (var faith in faiths.Faiths)
        {
            if (faith.Head is null)
            {
                continue;
            }

            var sampleCounty = counties.FirstOrDefault(c => faiths.For(c) == faith) ?? counties[0];
            var culture = cultures.For(sampleCounty);
            bool female = ClergyIsFemale(faith);
            var (firstName, _) = RulerNames(sampleCounty, culture, female);

            var rng = new Rng(Rng.StableHash(faith.Key) ^ 0x48A1UL);
            int birthYear = cfg.StartYear - rng.Int(35, 60);

            using (b.Block($"gen_hof_{hofIndex++}"))
            {
                b.Quoted("name", firstName);
                if (female) b.Field("female", "yes");

                b.Field("trait", GetPhenotypeTrait(culture, ethnicities, cfg));

                b.Field("religion", faith.Key);
                b.Field("culture", culture.Key);
                b.Inline($"{birthYear}.1.1", "birth = yes");

                using (b.Block(cfg.StartDate))
                using (b.Block("effect"))
                {
                    b.Field("add_gold", "150");
                    b.Field("add_piety", "250");
                }
            }

            b.Blank();
        }

        if (hofIndex > 0)
        {
            ParadoxText.WriteBom(Path.Combine(dir, "02_generated_head_of_faith.txt"), b.ToString());
        }
    }

    private static void WriteWildernessHolder(string modDir, MapConfig cfg, List<Title> wild)
    {
        if (wild.Count == 0) return;

        string dir = Path.Combine(modDir, "history", "characters");
        Directory.CreateDirectory(dir);

        var b = new JominiBuilder();
        b.Comment("The holder of every unsettled county. See MapGen/Wilderness.cs.");
        b.Blank();

        using (b.Block(WildernessMap.HolderId))
        {
            b.Quoted("name", "wilderness_holder_name");
            b.Field("religion", MapGen.Faiths.UnsettledFaithKey);
            b.Field("culture", MapGen.Cultures.UnsettledKey);
            b.Field("disallow_random_traits", "yes");
            b.Field("sexuality", "asexual");

            using (b.Block($"{Math.Max(1, cfg.StartYear - 1000)}.1.1"))
            {
                b.Field("birth", "yes");
                b.Field("trait", "wilderness");
                b.Field("trait", "immortal");
            }
        }

        b.Blank();

        ParadoxText.WriteBom(Path.Combine(dir, "01_generated_wilderness.txt"), b.ToString());
    }

    private static void WriteHouseRelationsOnAction(string modDir, PrehistoryMap prehistory)
    {
        if (prehistory.HouseRelations.Count == 0) return;

        string dir = Path.Combine(modDir, "common", "on_action");
        Directory.CreateDirectory(dir);

        var b = new JominiBuilder();
        b.Comment("Active House Feuds and Dynastic Amities on Day 1");
        b.Blank();

        using (b.Block("on_game_start_after_lobby"))
        using (b.Block("on_actions"))
            b.Token("gen_start_house_relations");

        b.Blank();

        using (b.Block("gen_start_house_relations"))
        using (b.Block("effect"))
            for (int i = 0; i < prehistory.HouseRelations.Count; i++)
            {
                var rel = prehistory.HouseRelations[i];
                string descKey = $"gen_house_relation_{i}_desc";
                rel.DescriptionKey = descKey;

                using (b.Block($"house:{rel.HouseA}"))
                using (b.Block("set_house_relation"))
                {
                    b.Field("target", $"house:{rel.HouseB}");
                    b.Field("level", rel.Level);
                    b.Field("description", descKey);
                }
            }

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_house_relations.txt"), b.ToString());
    }
    private static void WriteTitleHistory(string modDir, MapConfig cfg, List<Title> empires,
        Dictionary<Title, int> development, RealmMap realms, GovernmentMap governments,
        FaithMap faiths, WildernessMap wilderness, List<Title> wild)
    {
        string dir = Path.Combine(modDir, "history", "titles");
        Directory.CreateDirectory(dir);

        var b = new JominiBuilder();

        int reignStartYear = Math.Max(1, cfg.StartYear - 5);
        string titleGrantDate = $"{reignStartYear}.1.1";

        // The hegemony stands above the empires, so flattening from them never reaches it. It is
        // only ever in HolderCounty when the map was asked to start with one worn; unheld, the loop
        // skips it exactly as it skips an unformed empire.
        var all = Titles.Flatten(empires).ToList();
        if (Titles.HegemonyOf(empires) is { } crown) all.Insert(0, crown);

        foreach (var title in all)
        {
            if (wilderness.Contains(title)) continue;
            if (!realms.HolderCounty.TryGetValue(title, out var holder)) continue;
            if (wilderness.Contains(holder)) continue;

            int level = title.Tier == "c" ? development.GetValueOrDefault(title) : 0;
            realms.Liege.TryGetValue(title, out var liege);
            string government = governments.For(holder);

            using (b.Block(title.Key))
            using (b.Block(titleGrantDate))
            {
                b.Field("holder", CharacterId(holder));

                // Feudal is the engine's default, so saying so would be noise on most of the map.
                if (government != GovernmentMap.Feudal) b.Field("government", government);

                b.Field("liege", liege?.Key);
                if (level > 0) b.Field("change_development_level", level);
            }
        }

        // The wilderness realm and its counties, all held by the same immortal placeholder.
        var unsettled = wild.Count > 0 ? [WildernessMap.TitleKey, .. wild.Select(c => c.Key)] : new List<string>();

        foreach (string key in unsettled)
            using (b.Block(key))
            using (b.Block(cfg.StartDate))
            {
                b.Field("holder", WildernessMap.HolderId);
                b.Field("government", "wilderness_government");
            }

        int hofIndex = 0;
        foreach (var faith in faiths.Faiths)
        {
            if (faith.Head is null)
            {
                continue;
            }

            using (b.Block(faith.Head.TitleKey))
            using (b.Block(titleGrantDate))
            {
                b.Field("holder", $"gen_hof_{hofIndex++}");
                b.Field("government", "theocracy_government");
            }
        }

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_titles.txt"), b.ToString());
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
    ///
    /// **Per culture is as fine-grained as this can get, and that is not always right.** A culture
    /// hosting a minority (see <c>MinorityPlacements</c>) is human here while ~13% of the people
    /// written under it will roll the minority's ethnicity — and which ones is not knowable at emit
    /// time, because history characters carry no <c>dna</c> and the engine rolls their ethnicity out
    /// of the culture's weighted list when the save is created. Those characters are written
    /// phenotype_human and corrected at game start from their own genome by
    /// <c>gen_reconcile_phenotype_with_genes_effect</c> in BaseFilesToCopy/Fantasy.
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

        var loc = new LocFile();

        // Generic fallback descriptions
        loc.AddBuilt("house_relation_reason_preexisting_marriage_desc", "Royal marriage alliance established between dynasties");
        loc.AddBuilt("house_relation_reason_traditional_friendship_desc", "Traditional dynastic friendship enduring across generations");
        loc.AddBuilt("house_relation_reason_ancient_rivalry_desc", "Generational border rivalry and ancestral disputes");
        loc.AddBuilt("house_relation_reason_blood_feud_desc", "Bitter generational blood feud and contested sovereignty");
        loc.Blank();

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

            loc.AddBuilt(key, desc);
        }

        loc.Blank();

        var writtenKeys = new HashSet<string>();

        foreach (var dyn in prehistory.Dynasties.Values)
            if (writtenKeys.Add(dyn.NameKey)) loc.AddUnversioned(dyn.NameKey, dyn.LocalizedName);

        foreach (var house in prehistory.Houses.Values)
            if (writtenKeys.Add(house.NameKey)) loc.AddUnversioned(house.NameKey, house.LocalizedName);

        loc.Write(Path.Combine(dir, "gen_dynasties_l_english.yml"));
    }
}