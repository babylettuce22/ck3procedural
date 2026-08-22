using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.Emit;

namespace Ck3MapGen.MapGen;

/// <summary>
/// The living ruler of one seat at game start: everything about the character that is decided
/// rather than related.
///
/// This used to be composed inline in <see cref="HistoryWriter"/> as it was written — a name from
/// <see cref="HistoryWriter.RulerNames"/>, a birth year from the county index, a
/// <see cref="RulerProfile"/>, a house from <see cref="PrehistoryMap"/>, a purse from the tier —
/// and existed nowhere else, so a ruler could be read off disk but never pointed at. Lifting it into
/// an object is what lets the bookmarks describe the same man the character file writes, and is the
/// thing a ruler inspector would have to hold.
///
/// Two kinds of fact live here and they are kept distinct on purpose. The <c>init</c> members are
/// identity the rest of the mod references — the character id, the seat, the house, the father —
/// and changing one would dangle a reference in some other file. The settable members are the
/// character's own values, which nothing else points at and which can therefore be edited and
/// re-emitted in place. The relational web around the ruler (spouse, children, allies, rivals,
/// claims, truces) stays on <see cref="PrehistoryMap"/>: it is built between rulers rather than
/// about one, and lives where it was built.
///
/// Mutable, like <see cref="Culture"/> and <see cref="Faith"/>, for the same reason: the editor
/// snapshots and restores objects in place.
/// </summary>
public sealed class Ruler
{
    /// <summary>The county this ruler is seated in — the key every map of rulers is indexed by.</summary>
    public required Title Seat { get; init; }

    /// <summary>The highest title held, which is what the ruler is graded and named by.</summary>
    public required Title PrimaryTitle { get; init; }

    /// <summary>The history id (<c>gen_char_N</c>) every other file refers to this character by.</summary>
    public required string Id { get; init; }

    public required Culture Culture { get; init; }
    public required Faith Faith { get; init; }

    /// <summary>A <see cref="GovernmentMap"/> key.</summary>
    public required string Government { get; init; }

    public required string DynastyId { get; init; }
    public required string HouseKey { get; init; }

    /// <summary>The deceased father's history id, when prehistory gave this ruler one.</summary>
    public string? FatherId { get; init; }

    /// <summary>True when nobody holds the ruler's primary title in fief.</summary>
    public required bool Independent { get; init; }

    /// <summary>True when someone answers to this ruler, which is what dread and legitimacy are for.</summary>
    public required bool HasVassals { get; init; }

    // --- The character's own values -----------------------------------------------------------

    public required string Name { get; set; }

    /// <summary>
    /// Always false as generated — prehistory marries every ruler to a bride and names him from
    /// the male list — but written out wherever the engine asks for a sex, so a ruler made female
    /// later needs nothing in the writers to change.
    /// </summary>
    public bool Female { get; set; }

    public required int BirthYear { get; set; }
    public required int BirthMonth { get; set; }
    public required int BirthDay { get; set; }

    /// <summary>The birth date as CK3 history writes it.</summary>
    public string BirthDate => $"{BirthYear}.{BirthMonth}.{BirthDay}";

    /// <summary>Age in whole years at the start date.</summary>
    public int AgeAt(int year) => year - BirthYear;

    /// <summary>Schooling, skills, traits and standing. See <see cref="RulerProfile"/>.</summary>
    public required RulerProfile Profile { get; set; }

    /// <summary>Starting gold, already scaled for government.</summary>
    public required int Gold { get; set; }

    /// <summary>Starting prestige, already scaled for government.</summary>
    public required int Prestige { get; set; }

    /// <summary>Starting dynasty prestige. Only paid out to an independent ruler.</summary>
    public required int Renown { get; set; }

    /// <summary>
    /// The <c>dna</c> key of a bookmark portrait, stamped on by the bookmark writer after it has
    /// chosen its characters; null for everyone who is not on the bookmark screen.
    /// </summary>
    public string? DnaKey { get; set; }

    public string Tier => PrimaryTitle.Tier;

    public override string ToString() => Name;
}

/// <summary>Every living ruler, by seat.</summary>
public sealed class RulerMap
{
    private readonly Dictionary<Title, Ruler> _bySeat = [];

    /// <summary>
    /// Every ruler in the order the character file writes them — the de jure walk, wilderness and
    /// demesne counties skipped.
    /// </summary>
    public List<Ruler> All { get; } = [];

    public Ruler For(Title seat) => _bySeat[seat];

    public bool TryGet(Title seat, out Ruler ruler) => _bySeat.TryGetValue(seat, out ruler!);

    public bool Contains(Title seat) => _bySeat.ContainsKey(seat);

    /// <summary>
    /// Decides every ruler from the same seeded streams the character writer used to draw them
    /// inline, in the same order, so the file it writes does not move by a byte.
    ///
    /// Runs after <see cref="PrehistoryMap.Build"/> — the house, the dynasty and the father come
    /// from there — and before anything that names a ruler. Prehistory itself still asks
    /// <see cref="HistoryWriter.GetRulerBirthYear"/> for the birth year directly, because it runs
    /// first and is what this is built from; that helper is the definition of the draw, and this is
    /// the one place the result is kept.
    /// </summary>
    public static RulerMap Build(
        List<Title> counties, MapConfig cfg, RealmMap realms, CultureMap cultures, FaithMap faiths,
        GovernmentMap governments, WildernessMap wilderness, PrehistoryMap prehistory)
    {
        var map = new RulerMap();

        // One character per RULER, not per county: a liege's personal demesne covers several
        // counties under one man, and only the seat gets a character.
        var seats = realms.HolderCounty.Values.ToHashSet();

        // Which rulers actually have someone answering to them. Only they need the standing that
        // holds a court together, and writing it for a lone count would just be free stats.
        var liegeCounties = realms.Liege.Values
            .Select(t => realms.HolderCounty.GetValueOrDefault(t))
            .Where(c => c is not null)
            .ToHashSet();

        foreach (var county in counties)
        {
            if (wilderness.Contains(county) || !seats.Contains(county)) continue;

            var culture = cultures.For(county);
            var (firstName, _) = HistoryWriter.RulerNames(county, culture);
            var primaryTitle = HistoryWriter.Primary(county, realms);
            string government = governments.For(county);

            // The writer's own stream. Birth year is drawn from a fresh copy of it by
            // GetRulerBirthYear (prehistory needs the year before any ruler exists); month, day and
            // the purse continue from the one held here, in this order.
            var rng = new Rng(county.Index ^ 0x3E2D);
            int birthYear = HistoryWriter.GetRulerBirthYear(county.Index, cfg.StartYear);
            int birthMonth = rng.Int(1, 12);
            int birthDay = rng.Int(1, 28);

            // Everything about the man rather than the land — schooling, skills, byname, the
            // standing he starts with. See Emit/RulerProfile.cs for what each number is worth.
            var profile = RulerProfile.Build(
                county, primaryTitle.Tier, government, culture.Ethos,
                cfg.StartYear - birthYear, liegeCounties.Contains(county));

            string dynastyId = prehistory.CharacterDynastyMap.GetValueOrDefault(county, HistoryWriter.DynastyId(county));
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

            switch (government)
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

            var ruler = new Ruler
            {
                Seat = county,
                PrimaryTitle = primaryTitle,
                Id = HistoryWriter.CharacterId(county),
                Culture = culture,
                Faith = faiths.For(county),
                Government = government,
                DynastyId = dynastyId,
                HouseKey = houseKey,
                FatherId = fatherId,
                Independent = !realms.Liege.ContainsKey(primaryTitle),
                HasVassals = liegeCounties.Contains(county),
                Name = firstName,
                BirthYear = birthYear,
                BirthMonth = birthMonth,
                BirthDay = birthDay,
                Profile = profile,
                Gold = gold,
                Prestige = prestige,
                Renown = renown,
            };

            map._bySeat[county] = ruler;
            map.All.Add(ruler);
        }

        return map;
    }
}
