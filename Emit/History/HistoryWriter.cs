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
        WildernessMap wilderness, PrehistoryMap prehistory, RulerMap rulers, WorldCalendar? calendar = null)
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

        // The character file grades every ruler from Ruler.PrimaryTitle, and the title history
        // below hands out titles from realms.HolderCounty. RulerMap.Build read the same map, so the
        // two agree unless something handed out or moved a title between the two calls — which
        // would leave a king written with a count's purse and prestige, and nothing in the mod to
        // say so. Fail here instead: a stage that changes the realm map after the rulers exist has
        // to be moved ahead of RulerMap.Build, or rebuild the RulerMap.
        foreach (var seat in counties)
        {
            if (!rulers.TryGet(seat, out var ruler))
                throw new InvalidOperationException(
                    $"{seat.Key} is a seat in the realm map but has no ruler: the realm map changed after RulerMap.Build");

            var primary = Primary(seat, realms);
            if (!ReferenceEquals(primary, ruler.PrimaryTitle))
                throw new InvalidOperationException(
                    $"{ruler.Id} holds {primary.Key} in the title history but was graded as the holder of "
                    + $"{ruler.PrimaryTitle.Key}: the realm map changed after RulerMap.Build");
        }

        WriteDynasties(modDir, prehistory);
        WriteDynastyHouses(modDir, prehistory);
        CoatOfArmsWriter.WriteAll(modDir, prehistory, cfg.Seed, cultures: cultures, faiths: faiths);
        WriteCharacters(modDir, cfg, cultures, ethnicities, prehistory, rulers);
        WriteHeadOfFaithCharacters(modDir, cfg, faiths, cultures, ethnicities, counties, realms, wilderness,
            prehistory.Eras);
        WriteWildernessHolder(modDir, cfg, wild, wilderness);
        WriteHouseRelationsOnAction(modDir, cfg, prehistory);
        ContentWriter.WriteNobleFamilyTitles(modDir, prehistory);
        WriteTitleHistory(modDir, cfg, empires, development, realms, governments, faiths, wilderness, wild, prehistory);
        WriteSees(modDir, cfg, faiths, cultures, ethnicities, realms, governments, wilderness, prehistory.Eras);
        WriteDynastyLocalisation(modDir, prehistory, calendar);
        // Removed when there are none: a re-emit without them would otherwise keep the file an
        // earlier write left, naming rulers the character file no longer has.
        if (prehistory.Eras is { } eras) WriteEraStartEffects(modDir, cfg, eras);
        else
        {
            string stale = Path.Combine(modDir, "common", "on_action", EraStartEffectsFile);
            if (File.Exists(stale)) File.Delete(stale);
        }
    }

    private const string EraStartEffectsFile = "00_generated_additional_bookmarks.txt";

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
    /// <summary>
    /// The ruler draws as a configuration asks for them: the person an applied history put on this
    /// seat when it did (<see cref="MapConfig.SeatPeople"/>), else the draw salted for its year.
    /// Every caller that writes the start date's people goes through these three, so a ruler, his
    /// family and everything written about him agree on who he is.
    /// </summary>
    public static bool RulerIsFemale(Title county, Faith faith, MapConfig cfg)
        => cfg.SeatPeople?.TryGetValue(county.Index, out var person) == true
            ? person.Female
            : RulerIsFemale(county, faith, cfg.Seed, cfg.PeopleSalt);

    /// <inheritdoc cref="RulerIsFemale(Title, Faith, MapConfig)"/>
    public static (string FirstName, string DynastyName) RulerNames(Title county, Culture culture, bool female,
        MapConfig cfg)
        => cfg.SeatPeople?.TryGetValue(county.Index, out var person) == true
            ? (person.Name, culture.DynastyNameFor(county))
            : RulerNames(county, culture, female, cfg.Seed, cfg.PeopleSalt);

    /// <inheritdoc cref="RulerIsFemale(Title, Faith, MapConfig)"/>
    public static int GetRulerBirthYear(Title county, MapConfig cfg)
        => cfg.SeatPeople?.TryGetValue(county.Index, out var person) == true
            ? person.Born
            : GetRulerBirthYear(county.Index, cfg.StartYear, cfg.Seed, cfg.PeopleSalt);

    /// <param name="seed">The world's seed. See <see cref="Rng.For(int, int, int, int)"/>.</param>
    /// <param name="salt">Which ruler of the seat: 0 the start date's, an era or applied year otherwise.</param>
    public static bool RulerIsFemale(Title county, Faith faith, int seed, int salt)
    {
        double share = MapGen.Faiths.GenderOf(faith) switch
        {
            "doctrine_gender_female_dominated" => 0.95,
            "doctrine_gender_equal" => 0.45,
            _ => 0.05,
        };

        // salt 0 is the start-date ruler; an additional bookmark's ruler of the same seat passes its own.
        return Rng.For(seed, 0x6ED5, county.Index, salt).Chance(share);
    }

    /// <summary>
    /// Whether a faith's head is a woman.
    ///
    /// Not flavour: <c>doctrine_clerical_gender_female_only</c> means women are the only clergy,
    /// and every generated head of faith was a man regardless — so a third of the worlds this
    /// generator has ever made crowned a man over a priesthood he could not have joined.
    /// </summary>
    public static bool ClergyIsFemale(Faith faith, int seed)
    {
        string clerical = faith.DoctrineOf("doctrine_clerical_gender");
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

        return Rng.For(seed, 0x48A2, Rng.StableHash(faith.Key)).Chance(share);
    }

    /// <summary>
    /// <see cref="RulerIsFemale(Title, Faith, MapConfig)"/> for a seat whose government is known:
    /// the same answer, except that a theocrat is a cleric and is of the sex the clergy is.
    /// See <see cref="AsClergy"/>.
    ///
    /// For the ruler of the seat and for everything that has to agree with him — his consort, whose
    /// sex is the other one, and which of the pair his children's mother is. NOT for his dead
    /// parent: the line runs through the parent of the sex the land passes to, and a bishop's
    /// father was a lord under the land's law like anyone else's.
    ///
    /// An applied history's own person on the seat still wins, as in the overload it wraps: its
    /// simulation named and dated that person, and a sex changed here would leave the name for the
    /// other one.
    /// </summary>
    public static bool RulerIsFemale(Title county, Faith faith, MapConfig cfg, string government)
        => cfg.SeatPeople?.ContainsKey(county.Index) == true
            ? RulerIsFemale(county, faith, cfg)
            : AsClergy(RulerIsFemale(county, faith, cfg.Seed, cfg.PeopleSalt), faith, government);

    /// <summary>
    /// A ruler's drawn sex, corrected for a theocrat by the faith's
    /// <c>doctrine_clerical_gender</c>: never a woman where only men are clergy, always one where
    /// only women are, and the draw untouched where either may be — or where the ruler is not a
    /// theocrat at all.
    ///
    /// Not flavour on 1.20, whose patch notes put it plainly: theocratic gender succession follows
    /// the Clerical Gender doctrine. The draw it corrects reads the faith's doctrine_gender, which
    /// is about who inherits land, and gave the first Azgaar world tested (Poily, seed 4242) two
    /// bishops of a male-only priesthood out of 49 theocrats.
    ///
    /// Corrects rather than redraws, so the seat's own stream (0x6ED5) is read exactly as before and
    /// every seat that is not a theocrat's comes out the same.
    /// </summary>
    public static bool AsClergy(bool female, Faith faith, string government)
    {
        if (government != GovernmentMap.Theocracy) return female;

        return faith.DoctrineOf("doctrine_clerical_gender") switch
        {
            "doctrine_clerical_gender_male_only" => false,
            "doctrine_clerical_gender_female_only" => true,
            _ => female,
        };
    }

    /// <summary>
    /// Whether the ruler of a seat is a theocrat whose faith forbids its clergy to marry
    /// (<c>doctrine_clerical_marriage_disallowed</c>), and who is therefore written unmarried and
    /// without children, as vanilla writes its own prince-bishops and popes.
    ///
    /// Vanilla goes further: of the 1,559 clerics in its 1.20 history/characters/ecclesiastical.txt
    /// none is married and 25 have a dynasty, and the theocratic succession pool will not seat a
    /// married candidate at all (<c>is_valid_auto_title_holder_clergy</c>). The house is kept here on
    /// purpose. 1.20 builds its playable theocracy on it — Designate Theocratic Heir needs the
    /// theocrat to have a dynasty, and a clergyman of the house can be named heir — and a house is
    /// what the rest of the character's history (parents, brothers, claims, feuds) hangs from.
    /// Where the doctrine lets clergy marry, a theocrat marries like anyone else.
    /// </summary>
    public static bool IsCelibateTheocrat(string government, Faith faith)
        => government == GovernmentMap.Theocracy
           && faith.DoctrineOf("doctrine_clerical_marriage") == "doctrine_clerical_marriage_disallowed";

    public static (string FirstName, string DynastyName) RulerNames(Title county, Culture culture,
        bool female, int seed, int salt)
    {
        // salt 0 is the generated world; an applied history passes its year. See MapConfig.PeopleSalt.
        var rng = Rng.For(seed, 0x5A17, county.Index, salt);

        var names = female ? culture.FemaleNames : culture.MaleNames;

        string first = names.Count > 0
            ? names[rng.Int(0, names.Count - 1)]
            : culture.Name;

        // Allocated, not drawn: see Culture.DynastyNameFor for why a draw here named two houses alike.
        string dynasty = culture.DynastyNameFor(county);

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
        // Above empire, so a crowned hegemon's PRIMARY is the hegemony — as vanilla's Son of
        // Heaven holds h_china first. Left at 0, Primary() picked his empire, and everything
        // graded off the primary (RulerProfile, purses, retinues, headgear) saw an emperor.
        "h" => 5,
        "e" => 4,
        "k" => 3,
        "d" => 2,
        "c" => 1,
        _ => 0,
    };

    public static string CharacterId(Title county) => $"gen_char_{county.Index}";

    /// <summary>
    /// What a character's name is written as: the vanilla localisation key for a name a vanilla
    /// culture's list supplied (<see cref="Culture.NameKeys"/>), else the `cul_` key
    /// <see cref="CultureWriter"/> localises every name-list name under, else the name itself.
    /// Map-wide rather than per culture, because a wife's name comes from her own people's list.
    ///
    /// The engine reads a history `name` as a loc key. Writing the raw name ("Ahadro") displayed
    /// the same text but logged "Missing loc for name" once per character — 5,130 lines in one
    /// session, a large share of error.log's 100,000-entry cap.
    /// </summary>
    internal static Func<string, string> NameTokens(CultureMap cultures)
    {
        var keys = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var culture in cultures.Cultures)
            foreach (var (name, key) in culture.NameKeys) keys.TryAdd(name, key);
        foreach (var culture in cultures.Cultures)
            foreach (string name in culture.MaleNames.Concat(culture.FemaleNames))
                keys.TryAdd(name, CultureWriter.GivenNameKey(name));
        return name => keys.GetValueOrDefault(name, name);
    }

    public static string DynastyId(Title county) => $"gen_dynasty_{county.Index}";

    /// <summary>The description every history truce carries; localised in WriteDynastyLocalisation.</summary>
    internal const string HistoryTruceName = "gen_history_truce_name";

    /// <summary>The <see cref="RulerNames(Title, Culture, bool, int, int)"/> salt for a head of faith named from a seat.</summary>
    internal const int HeadOfFaithSalt = 0x4F48;

    public static int GetRulerBirthYear(int countyIndex, int startYear, int seed, int salt)
    {
        var rng = Rng.For(seed, 0x3E2D, countyIndex, salt);
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

        // Vanilla's own, for the historical characters a world of vanilla titles seats. Verbatim:
        // their names, prefixes and mottos are vanilla's localisation keys. See VanillaCharacters.
        foreach (string block in prehistory.HistoricalDynasties) b.Raw(block.TrimStart() + "\n\n");

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

        foreach (string block in prehistory.HistoricalHouses) b.Raw(block.TrimStart() + "\n\n");

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
        var nameToken = NameTokens(cultures);

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

        // A parent the additional bookmarks' lines gave a start-date character that had none. See
        // BookmarkEras.BuildFamilies.
        void AddedParent(string id)
        {
            if (prehistory.Eras?.Parents.TryGetValue(id, out var parent) == true)
                b.Field(parent.Mother ? "mother" : "father", parent.Id);
        }

        // =========================================================================
        // 1. The earlier additional bookmarks' people — ahead of everyone else
        // =========================================================================
        // Their lines run down to the start date's own dead parents, written next, and a parent has
        // to be read before a character naming it. All of them are dead before the start date.
        foreach (var person in prehistory.Eras?.Before ?? []) WriteEraPerson(person);

        // =========================================================================
        // 2. Deceased Ancestors (Fathers) — Stamped with historical birth and death
        // =========================================================================
        // Kin are written after the past rulers, below: a past ruler can be their parent.
        void WriteDeadAncestor(HistoricalCharacter ancestor)
        {
            using (b.Block(ancestor.Id))
            {
                b.Quoted("name", nameToken(ancestor.Name));
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

                // Only kin have parents among the ancestors — and, with an earlier additional
                // bookmark, the invented parents its houses carry on to. Field skips a null.
                b.Field("father", ancestor.FatherId);
                b.Field("mother", ancestor.MotherId);
                if (ancestor.FatherId is null && ancestor.MotherId is null) AddedParent(ancestor.Id);

                b.Inline(ancestor.BirthDate, "birth = yes");

                // Every ancestor generated here is given a death date, but the field is optional on
                // the record. Writing a missing one would emit a nameless ` = { death = yes }` and
                // CK3 abandons the whole file at that point, taking every later character with it.
                if (ancestor.DeathDate is not null)
                    b.Inline(ancestor.DeathDate, "death = yes");
            }

            b.Blank();
        }

        foreach (var ancestor in prehistory.AllExtraCharacters.Where(c => c.IsDeadAncestor && !c.IsKin))
            WriteDeadAncestor(ancestor);

        // =========================================================================
        // 2b. An applied history's past rulers — the realms' real predecessors
        // =========================================================================
        foreach (var past in prehistory.PastRulers)
        {
            using (b.Block(past.Id))
            {
                b.Quoted("name", nameToken(past.Name));
                if (past.Female) b.Field("female", "yes");
                b.Field("dynasty_house", past.House.HouseKey);
                b.Field("religion", past.FaithKey);
                b.Field("culture", past.House.CultureKey);
                if (cultures.Cultures.FirstOrDefault(c => c.Key == past.House.CultureKey) is { } pastCulture)
                    b.Field("trait", GetPhenotypeTrait(pastCulture, ethnicities, cfg));
                // The dynasty tree: the past ruler this one was the child of, written earlier in
                // this block list whenever the history wrote both. Field skips a null.
                b.Field(past.ParentIsMother ? "mother" : "father", past.ParentId);
                if (past.ParentId is null) AddedParent(past.Id);
                b.Inline(past.BirthDate, "birth = yes");
                b.Inline(past.DeathDate, "death = yes");
            }

            b.Blank();
        }

        // =========================================================================
        // 2c. Kin who died before the start — the children who never ruled
        // =========================================================================
        // After both kinds of parent they can have. The living ones are written with the other
        // living characters in section 4. See PrehistoryMap.AddKin.
        foreach (var kin in prehistory.AllExtraCharacters.Where(c => c.IsDeadAncestor && c.IsKin))
            WriteDeadAncestor(kin);

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
                // A ruler out of vanilla's history is vanilla's own block — traits, skills, parents,
                // marriages, dated events — under this seat's id. Only the name (while unedited it
                // is vanilla's key), the sex and the DNA the bookmark portrait was painted from are
                // written here. See MapGen/Vanilla/VanillaCharacters.cs.
                if (ruler.IsHistorical)
                {
                    b.Quoted("name", ruler.Name == ruler.HistoricalName && ruler.HistoricalNameKey is { } historicalKey
                        ? historicalKey : nameToken(ruler.Name));
                    if (ruler.Female) b.Field("female", "yes");
                    b.Field("dna", ruler.DnaKey);
                    b.Raw(ruler.HistoricalBody!);
                }
                else
                {
                b.Quoted("name", nameToken(ruler.Name));
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
                if (ruler.ParentId is null) AddedParent(ruler.Id);

                // --- Character Birth Date ---
                b.Inline(ruler.BirthDate, "birth = yes");
                }

                // --- Simulated Wedding Date ---
                //
                // A reigning woman marries matrilineally, without exception. Not flavour: her
                // children are already written into HER house by Prehistory (they carry
                // CharacterDynastyMap[ruler]), so a plain add_spouse states the opposite of the
                // character file standing next to it — and every heir born after the start date
                // would leave for the consort's house, which on the fallback match is
                // gen_dynasty_noble_N, a house with nothing behind it. Every queen on the map would
                // then be the last of her line by construction.
                //
                // Written from her own scope, which vanilla does too (khanty.txt 302530 marries
                // 302531 exactly this way); the commoner shape is the husband scoping his wife, and
                // the effect means the same thing from either side.
                if (prehistory.Spouses.TryGetValue(county, out var spouse) && spouse.MarriageDate != null)
                    using (b.Block(spouse.MarriageDate))
                        b.Field(ruler.Female ? "add_matrilineal_spouse" : "add_spouse", spouse.Id);

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

                // Rivalries, friendships and blood brotherhoods are stored on both counties
                // (Prehistory.AddRivalry / AddFriendship / AddRelation) and are mutual, like
                // alliances: written from both sides, the second set_relation_* failed with
                // "Scripted relation already exists" (64 + 6 + 3 error.log lines in one session).
                // Written from the lower county index only.

                // --- Chronologically Dated Rivalries ---
                if (prehistory.Rivals.TryGetValue(county, out var rivals))
                    foreach (var rival in rivals.Where(r => county.Index < r.TargetCounty.Index))
                        DatedEffect(rival.Date, () =>
                            b.Field("set_relation_rival", $"character:{CharacterId(rival.TargetCounty)}"));

                // --- Chronologically Dated Friendships ---
                if (prehistory.Friends.TryGetValue(county, out var friends))
                    foreach (var friend in friends.Where(f => county.Index < f.TargetCounty.Index))
                        DatedEffect(friend.Date, () =>
                            b.Field("set_relation_friend", $"character:{CharacterId(friend.TargetCounty)}"));

                // --- Sworn Blood Brothers (nomad khans and their anda) ---
                if (prehistory.BloodBrothers.TryGetValue(county, out var bloodBrothers))
                    foreach (var brother in bloodBrothers.Where(r => county.Index < r.TargetCounty.Index))
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

                        // Under an applied history, the house's head also carries what its past earned
                        // it, vassal or not (Ruler.Legacy). A lowborn historical ruler has no dynasty to
                        // give it to.
                        if (ruler.DynastyPrestige > 0 && ruler.DynastyId.Length > 0)
                            b.Inline("dynasty", $"add_dynasty_prestige = {ruler.DynastyPrestige}");

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

                                // A truce needs what caused it: a war, a casus belli, or a `name`
                                // (loc key) describing it. History has no war to point at, and
                                // without one the engine rejected the effect outright ("Missing
                                // casus_belli or name field", "Invalid war"), so no history truce
                                // ever existed in game. Vanilla names its script-made truces too.
                                b.Inline("add_truce_both_ways",
                                    $"character = character:{CharacterId(truceTarget)} days = {days} name = {HistoryTruceName}");
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
                    // inside it because that is where vanilla's own history puts give_nickname. A
                    // historical ruler has vanilla's own, dated, in the body already.
                    if (!ruler.IsHistorical) b.Field("give_nickname", profile.Nickname);
                }

                // Living on the start date, so no death — unless a later bookmark needs the seat
                // for someone else, in which case one dated after the start, where it never shows.
                if (prehistory.Eras?.MainDeath(ruler.Id, ruler.BirthYear) is { } death)
                    b.Inline(death, "death = yes");
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
                b.Quoted("name", nameToken(character.Name));
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

                if (prehistory.Eras?.MainDeath(character.Id, int.Parse(character.BirthDate.Split('.')[0])) is { } death)
                    b.Inline(death, "death = yes");
            }

            b.Blank();
        }

        // =========================================================================
        // 5. Vanilla's own people — the families of historical rulers, already cleaned
        // =========================================================================
        foreach (string block in prehistory.HistoricalCharacters) b.Raw(block);

        // =========================================================================
        // 6. The later additional bookmarks' people, and every one's heads of faith
        // =========================================================================
        // Last, because their lines hang from the start date's people written above. The rulers
        // of an earlier bookmark went out with their families in section 1.
        foreach (var person in prehistory.Eras?.After ?? []) WriteEraPerson(person);

        foreach (var era in prehistory.Eras?.Eras ?? [])
        {
            foreach (var priest in era.Priests)
            {
                using (b.Block(priest.Id))
                {
                    b.Quoted("name", nameToken(priest.Name));
                    if (priest.Female) b.Field("female", "yes");
                    b.Field("religion", priest.FaithKey);
                    b.Field("culture", priest.CultureKey);
                    WriteHeadEducation(b, Rng.For(cfg.Seed, 0x48E1, Rng.StableHash(priest.Id)));
                    if (cultures.Cultures.FirstOrDefault(c => c.Key == priest.CultureKey) is { } priestCulture)
                        b.Field("trait", GetPhenotypeTrait(priestCulture, ethnicities, cfg));
                    b.Inline(priest.BirthDate, "birth = yes");
                    if (priest.DeathDate is not null) b.Inline(priest.DeathDate, "death = yes");
                }

                b.Blank();
            }
        }

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_characters.txt"), b.ToString());

        // One of the additional bookmarks' people: a ruler of one of their dates, or someone of the
        // family drawn around them. Every date is on each, so none is given MainDeath.
        void WriteEraPerson(EraPerson person)
        {
            if (person is { Ruler: { } ruler, Era: { } era })
            {
                using (b.Block(ruler.Id))
                {
                    b.Quoted("name", nameToken(ruler.Name));
                    if (ruler.Female) b.Field("female", "yes");
                    b.Field("dynasty_house", ruler.HouseKey);
                    b.Field("religion", ruler.Faith.Key);
                    b.Field("culture", ruler.Culture.Key);
                    WriteProfile(b, ruler);
                    b.Field("trait", GetPhenotypeTrait(ruler.Culture, ethnicities, cfg));
                    AddedParent(ruler.Id);
                    b.Inline(ruler.BirthDate, "birth = yes");

                    // Matrilineal for a reigning woman, as on the start date: her children are
                    // already written into her house.
                    if (prehistory.Eras!.Marriages.TryGetValue(ruler.Id, out var marriage))
                        using (b.Block(marriage.Date))
                            b.Field(ruler.Female ? "add_matrilineal_spouse" : "add_spouse", marriage.SpouseId);

                    if (era.Deaths.TryGetValue(ruler.Id, out var death)) b.Inline(death, "death = yes");
                }

                b.Blank();
                return;
            }

            if (person.Character is not { } c) return;
            using (b.Block(c.Id))
            {
                b.Quoted("name", nameToken(c.Name));
                if (c.Female) b.Field("female", "yes");

                // A lowborn spouse has no dynasty at all, which is what leaving both out means.
                if (c.DynastyHouseKey is not null) b.Field("dynasty_house", c.DynastyHouseKey);
                else if (c.DynastyId.Length > 0) b.Field("dynasty", c.DynastyId);

                b.Field("religion", c.FaithKey);
                b.Field("culture", c.CultureKey);
                if (cultures.Cultures.FirstOrDefault(k => k.Key == c.CultureKey) is { } culture)
                    b.Field("trait", GetPhenotypeTrait(culture, ethnicities, cfg));
                b.Field("father", c.FatherId);
                b.Field("mother", c.MotherId);
                b.Inline(c.BirthDate, "birth = yes");
                if (c.DeathDate is not null) b.Inline(c.DeathDate, "death = yes");
            }

            b.Blank();
        }
    }
    /// <summary>What an additional bookmark's ruler carries beyond an ancestor's name and dates.</summary>
    private static void WriteProfile(JominiBuilder b, Ruler ruler)
    {
        var profile = ruler.Profile;
        b.Field("dna", ruler.DnaKey);
        b.Field("martial", profile.Martial);
        b.Field("prowess", profile.Prowess);
        b.Field("diplomacy", profile.Diplomacy);
        b.Field("intrigue", profile.Intrigue);
        b.Field("stewardship", profile.Stewardship);
        b.Field("learning", profile.Learning);
        b.Field("trait", profile.EducationTrait);
        foreach (string trait in profile.PersonalityTraits) b.Field("trait", trait);
        foreach (string trait in profile.OtherTraits) b.Field("trait", trait);
    }

    /// <summary>
    /// The purse and standing an additional bookmark's ruler starts with, granted only when that date
    /// is the one being played. Written as history effects instead, a dead ruler's gold would be
    /// inherited into every later start.
    /// </summary>
    private static void WriteEraStartEffects(string modDir, MapConfig cfg, BookmarkEras eras)
    {
        string dir = Path.Combine(modDir, "common", "on_action");
        Directory.CreateDirectory(dir);

        var b = new JominiBuilder();
        b.Comment("Purses for the rulers of the additional bookmarks, on their own start date only.");
        b.Blank();

        using (b.Block("on_game_start"))
        using (b.Block("on_actions"))
            b.Token("gen_additional_bookmark_purses");

        b.Blank();

        using (b.Block("gen_additional_bookmark_purses"))
        using (b.Block("effect"))
        {
            // Each era runs from its own date to the next bookmark's, the start date's included.
            var dates = eras.Eras.Select(e => e.Year).Append(cfg.StartYear).Order().ToList();
            for (int i = 0; i < eras.Eras.Count; i++)
            {
                var era = eras.Eras[i];
                int? next = dates.Where(y => y > era.Year).Select(y => (int?)y).FirstOrDefault();

                using (b.Block("if"))
                {
                    using (b.Block("limit"))
                    {
                        b.Token($"current_date >= {era.Date}");
                        if (next is not null) b.Token($"current_date < {next}.1.1");
                    }

                    foreach (var ruler in eras.Eras[i].Rulers.All)
                    using (b.Block($"character:{ruler.Id}"))
                    {
                        b.Field("add_gold", ruler.Gold);
                        b.Field("add_prestige", ruler.Prestige);
                        if (ruler.Renown > 0 && ruler.Independent)
                            b.Inline("dynasty", $"add_dynasty_prestige = {ruler.Renown}");
                        if (ruler.Profile.Dread > 0) b.Field("add_dread", ruler.Profile.Dread);
                        if (ruler.Profile.Legitimacy is not null)
                            b.Field("add_legitimacy", $"{ruler.Profile.Legitimacy}");

                        // The bookmark screen names him by it.
                        b.Field("give_nickname", ruler.Profile.Nickname);

                        if (ruler.Independent || ruler.Tier is "d" or "k" or "e" or "h")
                            using (b.Block("add_character_modifier"))
                            {
                                b.Field("modifier", "gen_early_realm_stability");
                                b.Field("years", ruler.Profile.StabilityYears);
                            }
                    }
                }
            }
        }

        ParadoxText.WriteBom(Path.Combine(dir, EraStartEffectsFile), b.ToString());
    }

    private static void WriteHeadOfFaithCharacters(string modDir, MapConfig cfg,
        FaithMap faiths, CultureMap cultures, EthnicityMap ethnicities, List<Title> counties,
        RealmMap realms, WildernessMap wilderness, BookmarkEras? eras)
    {
        string dir = Path.Combine(modDir, "history", "characters");
        Directory.CreateDirectory(dir);

        var b = new JominiBuilder();
        int hofIndex = 0;

        foreach (var faith in faiths.Faiths)
        {
            // A temporal head's title goes to a landed ruler, so no theocrat is written for it.
            // Same test as WriteTitleHistory, which is what keeps gen_hof_N numbering aligned.
            if (faith.Head is null || HeadRuler(faith, realms, faiths, wilderness) is not null)
            {
                continue;
            }

            var sampleCounty = counties.FirstOrDefault(c => faiths.For(c) == faith) ?? counties[0];
            var culture = cultures.For(sampleCounty);
            bool female = ClergyIsFemale(faith, cfg.Seed);

            // Salted apart from the start-date ruler (salt 0): the sample county is usually a seat,
            // and the same draw gave its ruler and the faith's head one first name between them.
            var (firstName, _) = RulerNames(sampleCounty, culture, female, cfg.Seed, HeadOfFaithSalt);

            var rng = Rng.For(cfg.Seed, 0x48A1, Rng.StableHash(faith.Key));
            int birthYear = cfg.StartYear - rng.Int(35, 60);

            string id = $"gen_hof_{hofIndex++}";
            using (b.Block(id))
            {
                b.Quoted("name", NameTokens(cultures)(firstName));
                if (female) b.Field("female", "yes");

                b.Field("trait", GetPhenotypeTrait(culture, ethnicities, cfg));

                b.Field("religion", faith.Key);
                b.Field("culture", culture.Key);
                WriteHeadEducation(b, Rng.For(cfg.Seed, 0x48E1, Rng.StableHash(faith.Key)));
                b.Inline($"{birthYear}.1.1", "birth = yes");

                using (b.Block(cfg.StartDate))
                using (b.Block("effect"))
                {
                    b.Field("add_gold", "150");
                    b.Field("add_piety", "250");
                }

                if (eras?.MainDeath(id, birthYear) is { } death) b.Inline(death, "death = yes");
            }

            b.Blank();
        }

        if (hofIndex > 0)
        {
            ParadoxText.WriteBom(Path.Combine(dir, "02_generated_head_of_faith.txt"), b.ToString());
        }
    }

    private static void WriteHeadEducation(JominiBuilder b, Rng rng)
    {
        b.Field("trait", rng.Chance(0.5) ? "education_learning_3" : "education_learning_4");
        // Base skill plus education yields 20-28 before personality and other modifiers.
        // The game-start hook corrects any shortfall and also covers landed/temporal heads.
        b.Field("learning", rng.Int(14, 20));
    }

    private static void WriteWildernessHolder(string modDir, MapConfig cfg, List<Title> wild,
        WildernessMap wilderness)
    {
        // A full-claim Azgaar export has no wild county yet still ships the system, so the ruins
        // have their holders to hand counties to (WildernessMap.Ships).
        if (wild.Count == 0 && !wilderness.Ships) return;

        string dir = Path.Combine(modDir, "history", "characters");
        Directory.CreateDirectory(dir);

        var b = new JominiBuilder();
        b.Comment("The holder of every unsettled county. See MapGen/Wilderness.cs.");
        b.Blank();

        Dummy(WildernessMap.HolderId, "wilderness_holder_name");

        // The second dummy, on the ruins system alone rather than on any county being ruined at the
        // start date — the usual world starts whole and the first county falls in play, by which
        // point a character cannot be given a birth in history any more.
        //
        // Identical to the first in every field, and that is the design rather than a shortcut: the
        // `wilderness` trait is what makes it immortal, unkillable, unable to inherit, unable to be
        // schemed against, and — through gfx/portraits/portrait_modifiers — faceless. Roughly thirty
        // triggers and effects in BaseFilesToCopy/Wilderness find their dummy by that trait, and a
        // ruins holder wearing a different one would fail all of them at once.
        //
        // What separates the two is the TITLE each holds, written in landed_titles, because a realm
        // takes its name from its holder's primary title and "the Ruins" is the only difference a
        // player ever needs to see. The cost of two characters answering `has_trait = wilderness` is
        // that anything looking for one specific dummy has to say which title it wants; the one
        // place that does is abandon_county_effect, and there is a note there saying so.
        if (wilderness.RuinsEnabled) Dummy(WildernessMap.RuinsHolderId, "ruins_holder_name");

        ParadoxText.WriteBom(Path.Combine(dir, "01_generated_wilderness.txt"), b.ToString());
        return;

        void Dummy(string id, string nameKey)
        {
            using (b.Block(id))
            {
                b.Quoted("name", nameKey);
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
        }
    }

    private static void WriteHouseRelationsOnAction(string modDir, MapConfig cfg, PrehistoryMap prehistory)
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

        // The start date's feuds and amities, between houses another bookmark may not seat.
        using (b.Block("gen_start_house_relations"))
        using (b.Block("effect"))
        using (StartGate.LatestOnly(b, cfg))
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
    /// <summary>
    /// The one government the base game will not rescue on its own.
    ///
    /// <c>common/on_action/game_start.txt</c> sweeps every other DLC government back to something
    /// the base game has: nomad and herder to tribal without Khans of the Steppe, and wanua,
    /// mandala, celestial, meritocratic, steppe-admin and the two Japanese ones without All Under
    /// Heaven. <c>administrative_government</c> is in none of those lists. Verified against the
    /// installed 1.19 file rather than assumed — it is the sort of thing a patch changes, and the
    /// failure mode is a realm sitting on a government whose machinery is not there.
    ///
    /// So vanilla guards it per title instead, and this is that guard, copied from
    /// <c>e_byzantium</c> in <c>history/titles/00_other_titles.txt</c>: the holder is put back on
    /// feudal and given a succession law feudal can actually hold, since the acclamation an
    /// administrative realm runs on is not one of them. With Roads to Power installed the limit
    /// fails and nothing happens, which is why it is written unconditionally rather than gated on
    /// anything this tool can see about the player's install.
    /// </summary>
    private static void WriteAdministrativeFallback(JominiBuilder b)
    {
        using (b.Block("effect"))
        using (b.Block("if"))
        {
            using (b.Block("limit"))
            {
                // Guards the whole thing: title history runs for a title that may be unheld on the
                // date, and holder = { … } on nobody is an error rather than a no-op.
                b.Field("exists", "holder");
                using (b.Block("NOT")) b.Field("has_dlc_feature", "roads_to_power");
            }

            using (b.Block("holder"))
            {
                b.Field("change_government", GovernmentMap.Feudal);
                b.Field("add_realm_law_skip_effects", "single_heir_succession_law");
            }
        }
    }

    /// <summary>
    /// Re-emits the title history alone, for a government changed after the mod was written — the
    /// <c>government =</c> line beside every holder is the whole of what such an edit says to the
    /// engine.
    ///
    /// The wilderness counties are worked out the same way <see cref="WriteAll"/> does rather than
    /// being carried, because the answer is a filter over the de jure tree and neither the tree nor
    /// the wilderness map is editable. Everything else the file carries — holders, lieges,
    /// development, the heads of faith — is written from the same objects the first write used, so
    /// a re-emit with nothing edited reproduces it exactly.
    /// </summary>
    /// <param name="prehistory">
    /// Read for its noble families, which this file grants. Expected to have been rebuilt against
    /// the edited government map already — the caller does that, because the same rebuild decides
    /// whether two other files need re-emitting and only the caller holds what they need.
    /// </param>
    internal static void ReWriteTitleHistory(string modDir, MapConfig cfg, List<Title> empires,
        Dictionary<Title, int> development, RealmMap realms, GovernmentMap governments,
        FaithMap faiths, WildernessMap wilderness, MapGen.PrehistoryMap? prehistory = null)
    {
        var wild = Titles.Flatten(empires)
            .Where(t => t.Tier == "c" && wilderness.Contains(t))
            .ToList();

        WriteTitleHistory(modDir, cfg, empires, development, realms, governments, faiths,
            wilderness, wild, prehistory);
    }

    /// <summary>A CK3 date string as a sortable (year, month, day).</summary>
    private static (int, int, int) ReignOrder(string date)
    {
        var parts = date.Split('.');
        return (int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]));
    }

    private static void WriteTitleHistory(string modDir, MapConfig cfg, List<Title> empires,
        Dictionary<Title, int> development, RealmMap realms, GovernmentMap governments,
        FaithMap faiths, WildernessMap wilderness, List<Title> wild,
        MapGen.PrehistoryMap? prehistory)
    {
        string dir = Path.Combine(modDir, "history", "titles");
        Directory.CreateDirectory(dir);

        var b = new JominiBuilder();

        int reignStartYear = Math.Max(1, cfg.StartYear - 5);
        string titleGrantDate = $"{reignStartYear}.1.1";
        var eras = prehistory?.Eras;

        // The hegemony stands above the empires, so flattening from them never reaches it. It is
        // only ever in HolderCounty when the map was asked to start with one worn; unheld, the loop
        // skips it exactly as it skips an unformed empire.
        var all = Titles.Flatten(empires).ToList();
        if (Titles.HegemonyOf(empires) is { } crown) all.Insert(0, crown);

        // Greatest tier first, the tree's own order within a tier. CK3 applies the entries of one
        // date in the order the file gives them, so a `liege =` read before the liege title's own
        // block has been has no holder to swear to, and the vassal starts the game independent —
        // silently: nothing reaches error.log. Written in tree order, that was every vassal whose
        // liege sits in a different de jure branch further down: 27 of 173 on a generated world,
        // 207 of 896 after an applied history, whose realms cut across de jure lines far more.
        // A liege always outranks the vassal title that names it, so this order puts it first on
        // every date at once — the additional bookmarks' included. LINQ's OrderBy is stable.
        all = [.. all.OrderByDescending(t => Title.TierRank(t.Tier))];

        // An applied history's predecessors, by the title they held: dated holder entries ahead of
        // the grant, oldest first. Only a holder line — the liege and government lines stay on the
        // grant, so the file-order rule above is untouched. Empty for a generated world.
        var pastByTitle = (prehistory?.PastRulers ?? [])
            .Where(p => p.TitleKey is not null)
            .GroupBy(p => p.TitleKey!)
            .ToDictionary(g => g.Key, g => g.OrderBy(p => ReignOrder(p.ReignDate!)).ToList());

        if (eras is not null)
            WriteEraTitleHistory(b, cfg, all, development, realms, governments, faiths, wilderness, eras, titleGrantDate, pastByTitle);
        else
        foreach (var title in all)
        {
            if (wilderness.Contains(title)) continue;
            if (!realms.HolderCounty.TryGetValue(title, out var holder)) continue;
            if (wilderness.Contains(holder)) continue;

            int level = title.Tier == "c" ? development.GetValueOrDefault(title) : 0;
            realms.Liege.TryGetValue(title, out var liege);
            string government = TitleGovernment(governments.For(holder), holder, faiths);

            using (b.Block(title.Key))
            {
                foreach (var past in pastByTitle.GetValueOrDefault(title.Key) ?? [])
                    using (b.Block(past.ReignDate)) b.Field("holder", past.Id);

                using (b.Block(titleGrantDate))
                {
                    b.Field("holder", CharacterId(holder));

                    // Feudal is the engine's default, so saying so would be noise on most of the map.
                    if (government != GovernmentMap.Feudal) b.Field("government", government);

                    b.Field("liege", liege?.Key);
                    if (level > 0) b.Field("change_development_level", level);

                    // Once per ruler, on the one title that is theirs: the effect acts on the holder,
                    // and a man holding a kingdom and six counties would otherwise run it seven times.
                    if (government == GovernmentMap.Administrative
                        && ReferenceEquals(title, Primary(holder, realms)))
                        WriteAdministrativeFallback(b);
                }
            }
        }

        // The noble families, granted on the same day their holders got their land.
        //
        // Up to 1.19 a second block on the start date ran destroy_landless_title_no_dlc_effect, so a
        // player without Roads to Power opened a world with no landless family titles, as vanilla
        // did beside each of Byzantium's sixty-one. 1.20 (Crozier) deleted that effect and dropped
        // the block from its own noble families; naming it made the engine reject every family's
        // history ("Failed to read title history, not a valid date"), so it is gone here too.
        foreach (var family in prehistory?.NobleFamilies ?? [])
        {
            using (b.Block(family.TitleKey))
            {
                using (b.Block(titleGrantDate))
                {
                    // Omitted for a sovereign, who has nobody to be a vassal of. Vanilla leaves the
                    // stale line on e_byzantium's own family and lets the engine resolve holding a
                    // title under yourself; saying nothing is the same answer without the puzzle.
                    b.Field("liege", family.Liege?.Key);

                    b.Field("holder", CharacterId(family.HolderCounty));
                    b.Field("government", family.Government);
                    b.Inline("succession_laws", "noble_family_succession_law");
                }

                // A family of the start date's bureaucracy, gone by a later bookmark whose realms
                // are not built as one.
                if (eras?.FirstLater is { } later)
                    using (b.Block(later.GrantDate)) b.Field("holder", "0");
            }

            b.Blank();
        }

        // The two unsettled realms and their counties, each held by its own immortal placeholder.
        //
        // Same government for both — the difference between them is the titular title at the head
        // of each list, which is what a realm takes its name from. Ruined counties are unsettled in
        // every other respect and are treated as such by everything upstream of here; this is the
        // one writer that has to know which of the two they belong to.
        //
        // k_gen_ruins is seated whenever the system ships, even with no ruined county under it, so
        // a county that falls in play has a holder to be handed to. A titular title cannot be minted
        // at runtime, and neither can a character be given a birth in history after the game starts.
        if (wild.Count > 0 || wilderness.Ships)
        {
            // A county wild on one bookmark and held on another — the frontier an applied history
            // moved — is written date by date with the realms (WriteEraTitleHistory). These are the
            // counties wild on every date; the two titular titles are seated whatever is left.
            List<Title> always = eras is null ? wild
                : [.. wild.Where(c => eras.Eras.All(e => (e.Realms.Wilderness ?? wilderness).Contains(c)))];

            var unsettled = always.Where(c => !wilderness.IsRuin(c)).Select(c => c.Key).ToList();
            if (wilderness.Ships || wild.Any(c => !wilderness.IsRuin(c))) unsettled.Insert(0, WildernessMap.TitleKey);

            var ruined = wilderness.RuinsEnabled
                ? [WildernessMap.RuinsTitleKey, .. always.Where(wilderness.IsRuin).Select(c => c.Key)]
                : new List<string>();

            Seat(unsettled, WildernessMap.HolderId);
            Seat(ruined, WildernessMap.RuinsHolderId);

            void Seat(List<string> keys, string holder)
            {
                // From the earliest bookmark when there are several, so no start finds them unheld.
                foreach (string key in keys)
                    using (b.Block(key))
                    using (b.Block(eras?.FirstDate ?? cfg.StartDate))
                    {
                        b.Field("holder", holder);
                        b.Field("government", "wilderness_government");
                    }
            }
        }

        int hofIndex = 0;
        foreach (var faith in faiths.Faiths)
        {
            if (faith.Head is null)
            {
                continue;
            }

            using (b.Block(faith.Head.TitleKey))
            {
                // Whoever held it on each additional bookmark, as BookmarkEras seated them — the
                // earlier ones before the start date's holder, the later ones after.
                void Heads(IEnumerable<BookmarkEra> which)
                {
                    foreach (var era in which)
                    {
                        if (!era.FaithHeads.TryGetValue(faith.Head.TitleKey, out var head)) continue;

                        using (b.Block(era.GrantDate))
                        {
                            b.Field("holder", head);
                            if (era.Priests.Any(p => p.Id == head)) b.Field("government", HeadGovernment(faith));
                            // A landed head on this date: the head title is their primary, as on the start date.
                            else if (faith.Head is { Temporal: false } && era.Realms.HeadSeats.TryGetValue(faith, out var eraSeat))
                                b.Field("government", TitleGovernment(GovernmentMap.Theocracy, eraSeat, faiths));
                        }
                    }
                }

                Heads(eras?.Eras.Where(e => e.Year < cfg.StartYear) ?? []);

                using (b.Block(titleGrantDate))
                {
                    // A temporal head is worn by the faith's strongest ruler beside their own titles,
                    // under their own government, the way vanilla's caliphs wear theirs; a landed
                    // spiritual head by the theocrat of its seat (HeadSeats). Any other spiritual
                    // one gets a theocrat of its own from WriteHeadOfFaithCharacters.
                    if (HeadRuler(faith, realms, faiths, wilderness) is { } sovereign)
                    {
                        b.Field("holder", sovereign);

                        // A landed spiritual head's primary title is this landless duchy (it outranks
                        // the seat county), and the engine reads the government off the primary title's
                        // history: without the line, the theocracy heads of seed 303 came out on
                        // landless_adventurer_government with no camp (2026-10-02). Vanilla writes it on
                        // k_papal_state the same way. Matches the seat county's own line.
                        if (faith.Head is { Temporal: false } && realms.HeadSeats.TryGetValue(faith, out var headSeat))
                            b.Field("government", TitleGovernment(GovernmentMap.Theocracy, headSeat, faiths));
                    }
                    else
                    {
                        b.Field("holder", $"gen_hof_{hofIndex++}");
                        b.Field("government", HeadGovernment(faith));
                    }
                }

                Heads(eras?.Eras.Where(e => e.Year > cfg.StartYear) ?? []);
            }
        }

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_titles.txt"), b.ToString());
    }

    /// <summary>
    /// A spiritual head's government: ecclesiastical when the faith has clerical regions, since the
    /// head also holds the primate see (as the Pope holds d_et_roma) and a region's holder is
    /// ecclesiastical; plain theocracy otherwise.
    /// </summary>
    private static string HeadGovernment(Faith faith)
        => faith.Sees.Count > 0 ? "ecclesiastical_government" : "theocracy_government";

    private const string EcclesiasticalGovernment = "ecclesiastical_government";

    /// <summary>
    /// The government a title history line names for a holder: a theocrat of a faith with an
    /// ecclesiastical hierarchy (<see cref="Faith.HasClericalRegions"/>) is ecclesiastical, as
    /// vanilla's prince-bishops of Mainz and Salzburg are, rather than plain theocracy, which has no
    /// treasury or church domicile and which that faith's own grants would never hand out. The
    /// government map keeps <see cref="GovernmentMap.Theocracy"/>, which everything else reads.
    /// </summary>
    private static string TitleGovernment(string government, Title holder, FaithMap faiths)
        => government == GovernmentMap.Theocracy && faiths.For(holder).HasClericalRegions ? EcclesiasticalGovernment : government;

    private const string SeeFile = "01_generated_sees.txt";

    /// <summary>
    /// The archbishops of every generated see (MapGen/Peoples/Sees.cs) and the sees' title history:
    /// each held from the start date's grant by a cleric of the see's rite and seat culture, under
    /// ecclesiastical government, bound to its region with <c>clerical_region</c>, and a vassal of the
    /// top liege of its seat's county. The primate see goes to the faith's head of faith when it has
    /// a spiritual one, as vanilla's Pope holds Rome's see; that holder answers to no one.
    ///
    /// Across the bookmarks (<see cref="See.Eras"/>), one block per date in date order, as vanilla's
    /// 00_ecclesiastical_titles.txt writes its own: the date's holder bound to the date's region, and
    /// <c>holder = 0</c> with <c>clerical_region = none</c> on the first date it no longer stands. Each
    /// date has an archbishop of its own, dead before the next bookmark as its rulers are.
    ///
    /// A see whose seat is a prince-bishopric on a date (<see cref="PrinceBishops"/>) goes to that
    /// county's theocrat, as vanilla's archbishop of Mainz holds both d_et_mainz and c_mainz, under the
    /// county's own liege so the one man never answers to two.
    /// </summary>
    private static void WriteSees(string modDir, MapConfig cfg, FaithMap faiths, CultureMap cultures,
        EthnicityMap ethnicities, RealmMap realms, GovernmentMap governments, WildernessMap wilderness,
        BookmarkEras? eras)
    {
        string charPath = Path.Combine(modDir, "history", "characters", SeeFile);
        string titlePath = Path.Combine(modDir, "history", "titles", SeeFile);

        var sees = faiths.Faiths.SelectMany(f => f.Sees).Concat(faiths.Faiths.SelectMany(f => f.EraSees)).ToList();
        if (sees.Count == 0)
        {
            if (File.Exists(charPath)) File.Delete(charPath);
            if (File.Exists(titlePath)) File.Delete(titlePath);
            return;
        }

        // The head of faith's character id, numbered as WriteHeadOfFaithCharacters numbers them.
        var hofIds = new Dictionary<Faith, string>();
        int hofIndex = 0;
        foreach (var faith in faiths.Faiths)
        {
            if (faith.Head is null) continue;
            // A landed spiritual head holds the primate see from their seat, as the Pope holds d_et_roma
            // beside the Papal States; a temporal head's faith (lay clergy) has no sees to give.
            if (HeadRuler(faith, realms, faiths, wilderness) is { } ruler)
            {
                if (faith.Head is { Temporal: false }) hofIds[faith] = ruler;
                continue;
            }
            hofIds[faith] = $"gen_hof_{hofIndex++}";
        }

        // Every bookmark, the start date's among them, oldest first.
        var dates = (eras?.Eras ?? [])
            .Select(e => new SeeDateView(e.Year, e.GrantDate, e, e.Realms, e.Governments, e.Realms.Wilderness ?? wilderness))
            .Append(new SeeDateView(cfg.StartYear, $"{Math.Max(1, cfg.StartYear - 5)}.1.1", null, realms, governments, wilderness))
            .OrderBy(d => d.Year)
            .ToList();

        var characters = new JominiBuilder();
        var titles = new JominiBuilder();
        characters.Comment("Archbishops of the generated sees. See MapGen/Peoples/Sees.cs.");
        characters.Blank();
        titles.Comment("Generated sees: clerical regions bound to et_gen_N_region. See MapGen/Peoples/Sees.cs.");
        titles.Blank();

        int dissolved = 0, princely = 0;
        foreach (var see in sees)
        {
            var faith = see.Faith;
            bool held = false;
            string? liegeWritten = null;

            // The see's Synod Seat follows it date by date (holder, liege; null holder = unheld).
            var seatDates = new List<(string Grant, string? Holder, string? Liege)>();

            using (titles.Block(see.Key))
            {
                for (int d = 0; d < dates.Count; d++)
                {
                    var date = dates[d];
                    var region = date.Era is null ? (see.Counties.Count > 0 ? see.Counties : null) : see.Eras.GetValueOrDefault(date.Year);

                    // A seat an applied history left wild or in ruins: the see dissolves, as the plan
                    // has it for now (titular sees later). Without this the archbishop was seated as
                    // the wilderness dummy's vassal.
                    if (region is not null && date.Wild.Contains(see.Seat))
                    {
                        if (date.Era is null) dissolved++;
                        region = null;
                    }

                    if (region is null)
                    {
                        if (held)
                        {
                            using (titles.Block(date.Grant))
                            {
                                titles.Field("holder", "0");
                                titles.Field("clerical_region", "none");
                            }
                            seatDates.Add((date.Grant, null, null));
                        }
                        held = false;
                        continue;
                    }

                    string holder;
                    Title? liege = null;
                    bool isPrimate = faith.Head is { Temporal: false } head && see.Seat == head.Seat;

                    if (date.Era is null && see.Rank == SeeRank.Primate && hofIds.TryGetValue(faith, out var hof))
                    {
                        holder = hof;
                    }
                    else if (date.Era is { } era && isPrimate && era.FaithHeads.GetValueOrDefault(faith.Head!.TitleKey) is { } priest
                             && era.Priests.Any(p => p.Id == priest))
                    {
                        holder = priest;
                    }
                    else if (date.Realms.HolderCounty.GetValueOrDefault(see.Seat) == see.Seat
                             && date.Governments.For(see.Seat) == GovernmentMap.Theocracy && faiths.For(see.Seat) == faith
                             && date.Realms.Liege.GetValueOrDefault(see.Seat) is { } bishopLiege
                             && Title.TierRank(bishopLiege.Tier) > Title.TierRank("d"))
                    {
                        // A prince-archbishop: the seat county's own theocrat. Only under a king or
                        // better, since the see makes him a duke and a liege must outrank him.
                        holder = date.Era is { } era2 ? era2.Rulers.For(see.Seat).Id : CharacterId(see.Seat);
                        liege = date.Realms.Liege.GetValueOrDefault(see.Seat);
                        if (date.Era is null) princely++;
                    }
                    else
                    {
                        holder = date.Era is { } era3 ? $"gen_see_{see.Key["d_et_gen_".Length..]}_{era3.Tag}" : $"gen_see_{see.Key["d_et_gen_".Length..]}";
                        liege = SeeLiege(see.Seat, date.Realms);
                        int? next = d + 1 < dates.Count ? dates[d + 1].Year : null;
                        WriteArchbishop(characters, holder, see, date.Year, next, date.Era is null ? eras : null,
                            cfg, cultures, ethnicities);
                    }

                    using (titles.Block(date.Grant))
                    {
                        titles.Field("holder", holder);
                        titles.Field("government", EcclesiasticalGovernment);
                        titles.Field("clerical_region", date.Era is null ? see.RegionKey : see.RegionKeyAt(date.Year));

                        // Under the seat's top liege, as a realm's archbishops are; the head of faith is
                        // no one's. Said whenever it differs from the date before's, and on a title
                        // destroyed in between, which lost it.
                        if (liege is not null) titles.Field("liege", liege.Key);
                        else if (liegeWritten is not null) titles.Field("liege", "0");

                        // Its Synod Seat, named on the see so the hand-over script can find it, from
                        // the date it is first held and again when it comes back after being destroyed.
                        if (!held && faith.HasElectors)
                            using (titles.Block("effect"))
                            using (titles.Block("set_variable"))
                            {
                                titles.Field("name", "gen_synod_seat");
                                titles.Field("value", $"title:{see.SynodSeatKey}");
                            }
                    }
                    seatDates.Add((date.Grant, holder, liege?.Key));
                    liegeWritten = liege?.Key;
                    held = true;
                }
            }

            titles.Blank();

            // The Synod Seat: the same holder on the same dates, an elector title of the faith from
            // the first, as vanilla's cardinalates are made electors in their own title history
            // (00_ecclesiastical_titles.txt). The see itself cannot be one: the engine refuses an
            // elector on a clerical-region title.
            if (faith.HasElectors && seatDates.Count > 0)
            {
                using (titles.Block(see.SynodSeatKey))
                {
                    bool seatHeld = false;
                    string? seatLiege = null;
                    foreach (var (grant, seatHolder, liegeKey) in seatDates)
                    {
                        using (titles.Block(grant))
                        {
                            if (seatHolder is null)
                            {
                                titles.Field("holder", "0");
                                seatHeld = false;
                                continue;
                            }

                            titles.Field("holder", seatHolder);
                            titles.Field("government", EcclesiasticalGovernment);
                            if (liegeKey is not null) titles.Field("liege", liegeKey);
                            else if (seatLiege is not null) titles.Field("liege", "0");

                            if (!seatHeld)
                                using (titles.Block("effect"))
                                {
                                    titles.Field("add_clerical_elector", $"faith:{faith.Key}");
                                    using (titles.Block("set_variable"))
                                    {
                                        titles.Field("name", "gen_synod_see");
                                        titles.Field("value", $"title:{see.Key}");
                                    }
                                    // The faith it votes in: script has no link from a title to its
                                    // elector faith, and a seat never follows its see to a holder of
                                    // another faith (zz_gen_see_electors_on_actions.txt).
                                    using (titles.Block("set_variable"))
                                    {
                                        titles.Field("name", "gen_synod_faith");
                                        titles.Field("value", $"faith:{faith.Key}");
                                    }
                                }
                        }
                        seatLiege = liegeKey;
                        seatHeld = true;
                    }
                }

                titles.Blank();
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(charPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(titlePath)!);
        ParadoxText.WriteBom(charPath, characters.ToString());
        ParadoxText.WriteBom(titlePath, titles.ToString());
        if (dissolved > 0) Console.WriteLine($"  sees: {dissolved} dissolved, their seats wild or in ruins on the start date");
        if (princely > 0) Console.WriteLine($"  sees: {princely} held by the prince-bishop of their seat on the start date");
    }

    /// <summary>One bookmark as the see writer reads it; <see cref="Era"/> is null for the start date.</summary>
    private sealed record SeeDateView(int Year, string Grant, BookmarkEra? Era, RealmMap Realms,
        GovernmentMap Governments, WildernessMap Wild);

    /// <summary>
    /// An archbishop of a see on one bookmark: a learned cleric of the see's rite and its seat's
    /// culture, in his prime on the date, and dead before <paramref name="nextYear"/>'s holders take
    /// their titles, as every bookmark's rulers are. The start date's die as its other people do
    /// (<see cref="BookmarkEras.MainDeath"/>); the ids keep their pre-bookmark form on the start date,
    /// so a world with one bookmark writes what it always did.
    /// </summary>
    private static void WriteArchbishop(JominiBuilder characters, string id, See see, int year, int? nextYear,
        BookmarkEras? startEras, MapConfig cfg, CultureMap cultures, EthnicityMap ethnicities)
    {
        bool start = year == cfg.StartYear;
        int salt = start ? SeeHolderSalt : SeeHolderSalt ^ year;
        var culture = cultures.For(see.Seat);
        bool female = SeeHolderIsFemale(see, cfg.Seed, start ? 0 : year);
        var (firstName, _) = RulerNames(see.Seat, culture, female, cfg.Seed, salt);
        var rng = start ? Rng.For(cfg.Seed, 0x5EE4, Rng.StableHash(see.Key)) : Rng.For(cfg.Seed, 0x5EE4, Rng.StableHash(see.Key), year);

        using (characters.Block(id))
        {
            characters.Quoted("name", NameTokens(cultures)(firstName));
            if (female) characters.Field("female", "yes");
            characters.Field("trait", GetPhenotypeTrait(culture, ethnicities, cfg));
            // Vanilla's clergy are nearly all learned (1,516 of 1,559 in ecclesiastical.txt).
            characters.Field("trait", rng.Chance(0.5) ? "education_learning_3" : "education_learning_4");

            // A regional rite's archbishop keeps it; history accepts `rite =` on characters.
            if (see.Rite is { } rite) characters.Field("rite", rite.Key);
            else characters.Field("religion", see.Faith.Key);
            characters.Field("culture", culture.Key);

            int birthYear = year - rng.Int(35, 62);
            characters.Inline($"{birthYear}.1.1", "birth = yes");

            using (characters.Block(start ? cfg.StartDate : $"{year}.1.1"))
            using (characters.Block("effect"))
                characters.Field("add_piety", see.Rank == SeeRank.Ordinary ? "150" : "300");

            // Gone before the next bookmark's archbishop is seated.
            if (start)
            {
                if (startEras?.MainDeath(id, birthYear) is { } death) characters.Inline(death, "death = yes");
            }
            else if (nextYear is { } next)
            {
                int latest = Math.Max(year + 2, Math.Min(next - 3, birthYear + 85));
                int deathYear = rng.Int(year + 2, latest);
                characters.Inline($"{deathYear}.{rng.Int(1, 12)}.{rng.Int(1, 28)}", "death = yes");
            }
        }

        characters.Blank();
    }

    private const int SeeHolderSalt = 0x5EE5;

    /// <summary>The clergy's sex by <c>doctrine_clerical_gender</c>; an open clergy leans as the faith does, per see and date.</summary>
    private static bool SeeHolderIsFemale(See see, int seed, int year)
    {
        string clerical = see.Faith.DoctrineOf("doctrine_clerical_gender");
        if (clerical == "doctrine_clerical_gender_female_only") return true;
        if (clerical == "doctrine_clerical_gender_male_only") return false;

        double share = MapGen.Faiths.GenderOf(see.Faith) switch
        {
            "doctrine_gender_female_dominated" => 0.85,
            "doctrine_gender_equal" => 0.45,
            _ => 0.10,
        };
        var rng = year == 0 ? Rng.For(seed, 0x5EE3, Rng.StableHash(see.Key)) : Rng.For(seed, 0x5EE3, Rng.StableHash(see.Key), year);
        return rng.Chance(share);
    }

    /// <summary>The primary title of the top liege of the ruler whose realm holds <paramref name="seat"/>.</summary>
    private static Title? SeeLiege(Title seat, RealmMap realms)
    {
        // The county's holder first: Primary reads a ruler's own seat, and a see often sits in a vassal's county.
        if (!realms.HolderCounty.TryGetValue(seat, out var holderSeat)) return null;
        var current = Primary(holderSeat, realms);
        var seen = new HashSet<Title>();
        while (seen.Add(current) && realms.Liege.TryGetValue(current, out var liege)) current = liege;

        // A see is duchy tier, and a liege must outrank its vassal: under a sovereign duke or count
        // the archbishop stands on his own, as vanilla's sees in petty realms do. Never the
        // wilderness or ruins dummy's realm.
        return current.Key.Length > 0 && Title.TierRank(current.Tier) > Title.TierRank("d")
               && current.Key is not (WildernessMap.TitleKey or WildernessMap.RuinsTitleKey)
            ? current : null;
    }

    /// <summary>
    /// The realm titles across every bookmark, the start date's in its place among them: one block
    /// per date a title is held on, and <c>holder = 0</c> on the first date a later map no longer
    /// has it.
    ///
    /// Only the dates matter, not what happens between them. A ruler dies before the next bookmark,
    /// and every title he held is either handed to that bookmark's holder or destroyed on its grant
    /// date, so whatever the engine did with his titles in the meantime is overwritten. A liege is
    /// written when it changes — <c>liege = 0</c> for a realm that walked free — and again whenever
    /// a destroyed title comes back, since destruction may have cleared it. Development is a setter
    /// per date, as vanilla uses it, a level per fifty years either side of the start date's.
    ///
    /// Under an applied history two more things go in. Its past rulers' dated holder lines, in date
    /// order among the bookmarks' blocks, so a title's history in game still lists its real
    /// predecessors. And the frontier it moved: a county wild on one date (<see cref="RealmMap.Wilderness"/>)
    /// and held on another is the wilderness dummy's on the dates it is wild, the ruins dummy's when
    /// somebody held it once. A county wild on every date is left to the wilderness block.
    /// </summary>
    private static void WriteEraTitleHistory(JominiBuilder b, MapConfig cfg, List<Title> all,
        Dictionary<Title, int> development, RealmMap realms, GovernmentMap governments, FaithMap faiths,
        WildernessMap wilderness, BookmarkEras eras, string titleGrantDate,
        Dictionary<string, List<MapGen.PastRuler>> pastByTitle)
    {
        var dates = eras.Eras
            .Select(e => (Date: e.GrantDate, e.Year, e.Realms, e.Governments, Id: (Func<Title, string>)(s => e.Rulers.For(s).Id),
                Wild: e.Realms.Wilderness ?? wilderness))
            .Append((Date: titleGrantDate, Year: cfg.StartYear, Realms: realms, Governments: governments,
                Id: (Func<Title, string>)CharacterId, Wild: wilderness))
            .OrderBy(d => d.Year)
            .ToList();

        foreach (var title in all)
        {
            if (dates.All(d => d.Wild.Contains(title))) continue;

            bool everHeld = dates.Any(d => d.Realms.HolderCounty.TryGetValue(title, out var h) && !d.Wild.Contains(h));
            if (!everHeld) continue;

            int level = title.Tier == "c" ? development.GetValueOrDefault(title) : 0;
            var past = pastByTitle.GetValueOrDefault(title.Key) ?? [];
            int pastWritten = 0;

            using (b.Block(title.Key))
            {
                bool held = false, reheld = false;
                string? liegeWritten = null;
                string governmentWritten = GovernmentMap.Feudal;
                int levelWritten = 0;

                foreach (var (date, year, map, eraGovernments, idOf, wild) in dates)
                {
                    // The history's own predecessors up to this date: a holder line each, as the
                    // title history without bookmarks writes them.
                    for (; pastWritten < past.Count && ReignOrder(past[pastWritten].ReignDate!).CompareTo(ReignOrder(date)) < 0; pastWritten++)
                    {
                        using (b.Block(past[pastWritten].ReignDate)) b.Field("holder", past[pastWritten].Id);
                        held = true;
                        reheld = false;
                    }

                    // Wild on this date and held on another: the dummy's, free of any liege.
                    if (wild.Contains(title))
                    {
                        using (b.Block(date))
                        {
                            b.Field("holder", wild.IsRuin(title) ? WildernessMap.RuinsHolderId : WildernessMap.HolderId);
                            b.Field("government", "wilderness_government");
                            if (liegeWritten is not null) b.Field("liege", "0");
                        }
                        governmentWritten = "wilderness_government";
                        liegeWritten = null;
                        held = true;
                        reheld = false;
                        continue;
                    }

                    if (!map.HolderCounty.TryGetValue(title, out var holder) || wild.Contains(holder))
                    {
                        if (held) using (b.Block(date)) b.Field("holder", "0");
                        reheld |= held;
                        held = false;
                        continue;
                    }

                    string government = TitleGovernment(eraGovernments.For(holder), holder, faiths);
                    string? liege = map.Liege.GetValueOrDefault(title)?.Key;
                    int eraLevel = BookmarkEras.EraDevelopment(level, cfg, year);

                    using (b.Block(date))
                    {
                        b.Field("holder", idOf(holder));

                        // Feudal is the default, but a tribe's successor who is a lord has to say so.
                        if (government != governmentWritten || government != GovernmentMap.Feudal)
                            b.Field("government", government);
                        governmentWritten = government;

                        if (liege != liegeWritten || reheld)
                            b.Field("liege", liege ?? "0");
                        liegeWritten = liege;

                        if (eraLevel > 0 && eraLevel != levelWritten)
                        {
                            b.Field("change_development_level", eraLevel);
                            levelWritten = eraLevel;
                        }

                        if (ReferenceEquals(map, realms) && government == GovernmentMap.Administrative
                            && ReferenceEquals(title, Primary(holder, realms)))
                            WriteAdministrativeFallback(b);
                    }

                    held = true;
                    reheld = false;
                }
            }
        }
    }

    /// <summary>
    /// Who wears a faith's head title when it is not a theocrat of its own: the temporal head's
    /// sovereign (<see cref="TemporalHeadHolder"/>), or a spiritual head's seat theocrat when
    /// <see cref="HeadSeats"/> gave the head land on this map. Null means a gen_hof_N character.
    /// Every writer that numbers gen_hof_N asks this, so the numbering stays aligned.
    /// </summary>
    internal static string? HeadRuler(Faith faith, RealmMap realms, FaithMap faiths, WildernessMap wilderness)
        => TemporalHeadHolder(faith, realms, faiths, wilderness)
           ?? (realms.HeadSeats.TryGetValue(faith, out var seat) && realms.HolderCounty.GetValueOrDefault(seat) == seat
               ? CharacterId(seat) : null);

    /// <summary>
    /// The character id that wears a temporal head-of-faith title: the holder of the highest-tier
    /// title whose holder's seat follows the faith, ties broken by county index so both writers
    /// name the same one. Null for a spiritual head, and for the rare temporal head whose faith has
    /// no ruler of its own to give it to — that title falls back to a theocrat, and the faith keeps
    /// its doctrine, which the engine accepts.
    ///
    /// Read from <see cref="RealmMap.HolderCounty"/> rather than from <see cref="RulerMap"/> on
    /// purpose: it is what the title history above writes every other holder from, so the answer
    /// cannot disagree with it. A ruler's <see cref="Ruler.PrimaryTitle"/> is the same answer
    /// today — <see cref="WriteAll"/> asserts it — but only because nothing touches the realm
    /// map between <see cref="RulerMap.Build"/> and this writer; reading the map directly keeps
    /// this correct even if that changes.
    /// </summary>
    internal static string? TemporalHeadHolder(Faith faith, RealmMap realms, FaithMap faiths,
        WildernessMap wilderness)
    {
        if (faith.Head is not { Temporal: true }) return null;

        var seat = realms.HolderCounty
            .Where(kv => !wilderness.Contains(kv.Value) && faiths.For(kv.Value) == faith)
            .OrderByDescending(kv => Rank(kv.Key))
            .ThenBy(kv => kv.Value.Index)
            .Select(kv => kv.Value)
            .FirstOrDefault();

        return seat is null ? null : CharacterId(seat);
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
    internal static string? GetPhenotypeTrait(Culture culture, EthnicityMap ethnicityMap, MapConfig cfg)
    {
        var ethnicity = ethnicityMap.For(culture);

        return ethnicity.Archetype switch
        {
            RaceArchetype.HighElf => "phenotype_gracile",
            RaceArchetype.WoodElf => "phenotype_sylvan",
            RaceArchetype.Dwarf => "phenotype_stocky",
            RaceArchetype.Orc => "phenotype_rough_hewn",
            RaceArchetype.Giantkin => "phenotype_towering",
            RaceArchetype.Gnome => "phenotype_diminutive",
            RaceArchetype.DuskElf => "phenotype_dusk_adapted",
            RaceArchetype.Hornkin => "phenotype_horned",
            RaceArchetype.Human when cfg.EnableFantasyEthnicities
                && cfg.RaceMode != MapConfig.FantasyRaceMode.HumanOnly => "phenotype_human",
            _ => null,
        };
    }

    /// <param name="calendar">The world's calendar, whose era the relation dates are written in.
    /// Null, or one that keeps vanilla's era, writes "AD" — the suffix the game's own dates show then.</param>
    internal static void WriteDynastyLocalisation(string modDir, PrehistoryMap prehistory, WorldCalendar? calendar = null)
    {
        string dir = Path.Combine(modDir, "localization", "english");
        Directory.CreateDirectory(dir);

        // The relation dates must read like every other date on screen, which the calendar renames
        // (see CompatibilityWriter.WriteCalendarLocalisation): "since 812 AD" beside "1 Jimis, 900 KE".
        string era = calendar?.EraShort.Trim() is { Length: > 0 } own ? ParadoxText.Loc(own) : "AD";

        var loc = new LocFile();

        // Generic fallback descriptions. Not house_relation_reason_preexisting_marriage_desc: that key
        // is vanilla's (a dynamic "X married Y" line its game-start pass fills in), nothing here uses
        // the preexisting_marriage reason, and redefining it outside localization/replace only logged
        // "Duplicate localization key" while hiding vanilla's better text.
        loc.AddBuilt("house_relation_reason_traditional_friendship_desc", "Traditional dynastic friendship enduring across generations");
        loc.AddBuilt("house_relation_reason_ancient_rivalry_desc", "Generational border rivalry and ancestral disputes");
        loc.AddBuilt("house_relation_reason_blood_feud_desc", "Bitter generational blood feud and contested sovereignty");
        loc.Blank();

        // The truce tooltip's description for every truce written into character history.
        loc.AddBuilt(HistoryTruceName, "Peace from an earlier war");
        loc.Blank();

        // Specific house relation descriptions embedding the real prehistory start date
        for (int i = 0; i < prehistory.HouseRelations.Count; i++)
        {
            var rel = prehistory.HouseRelations[i];
            string key = rel.DescriptionKey ?? $"gen_house_relation_{i}_desc";
            string yearStr = !string.IsNullOrEmpty(rel.StartDate) && rel.StartDate.Contains('.')
                ? rel.StartDate.Split('.')[0] + " " + era
                : (!string.IsNullOrEmpty(rel.StartDate) ? rel.StartDate + " " + era : "ancient times");

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

            // A grudge an applied history left says what it is about: the wrong that weighs most in
            // it now, where and when. The county through its key, so a rename follows it.
            if (rel.Cause is { } cause && rel.CauseTitle is { } where && rel.CauseYear > 0)
            {
                string what = rel.Level switch { "feud" => "Blood feud", "rivalry" => "Rivalry", _ => "Quarrel" };
                string place = $"${where}$";
                string when = $"{rel.CauseYear} {era}";
                string? about = cause switch
                {
                    "won" => $"land lost in the war over {place} in {when}",
                    "held" => $"the failed war for {place} in {when}",
                    "fought" => $"the war over {place} that ended in a white peace in {when}",
                    "conquest" => $"the taking of {place} in {when}",
                    "walked" => $"{place} leaving its liege in {when}",
                    "seized" => $"the throne of {place} seized in {when}",
                    "freed" => $"{place} breaking away in {when}",
                    _ => null,
                };
                if (about is not null)
                    desc = rel.CauseYear.ToString() == yearStr.Split(' ')[0]
                        ? $"{what} over {about}"
                        : $"{what} since {yearStr}, now over {about}";
            }

            loc.AddBuilt(key, desc);
        }

        loc.Blank();

        var writtenKeys = new HashSet<string>();

        foreach (var dyn in prehistory.Dynasties.Values)
            if (writtenKeys.Add(dyn.NameKey)) loc.AddUnversioned(dyn.NameKey, dyn.LocalizedName);

        foreach (var house in prehistory.Houses.Values)
            if (writtenKeys.Add(house.NameKey)) loc.AddUnversioned(house.NameKey, house.LocalizedName);

        loc.Blank();

        // The family titles, named through their house rather than after it: `$dynn_gen_7$ Family`
        // resolves at display time, so renaming a house in the editor renames its family too
        // instead of leaving a title carrying the name the house used to have. Vanilla writes its
        // own the same way — `d_nf_ampelas: "$dynn_Ampelas$ Family"`.
        //
        // The _article key beside it is not decoration: the title is definite_form, and without one
        // the game draws the raw DEFAULT_TITLE_NAME_ARTICLE token where "the" belongs.
        foreach (var family in prehistory.NobleFamilies)
        {
            if (!prehistory.Houses.TryGetValue(family.HouseKey, out var house)) continue;
            if (!writtenKeys.Add(family.TitleKey)) continue;

            loc.AddUnversioned(family.TitleKey, $"${house.NameKey}$ Family");
            loc.AddUnversioned($"{family.TitleKey}_article", "$DEFAULT_TITLE_NAME_ARTICLE$");

            // Vanilla ships no _adj for any of its sixty-one and takes a missing-localisation
            // warning for each — which is only invisible because tiger hides findings against the
            // game's own files. One more line is cheaper than a warning per family, and the name
            // reads as an adjective unchanged: "the Ampelas levies".
            loc.AddUnversioned($"{family.TitleKey}_adj", $"${house.NameKey}$");
        }

        loc.Write(Path.Combine(dir, "gen_dynasties_l_english.yml"));
    }
}
