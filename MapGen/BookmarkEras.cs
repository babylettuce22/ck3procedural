using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.Emit;

namespace Ck3MapGen.MapGen;

/// <summary>A theocrat who holds a spiritual head-of-faith title on an additional bookmark.</summary>
public sealed record EraPriest(string Id, string Name, bool Female, string CultureKey, string FaithKey,
    string BirthDate, string? DeathDate);

/// <summary>One additional start date: its political map and the people holding it.</summary>
public sealed class BookmarkEra
{
    public required int Year { get; init; }
    public string Date => $"{Year}.1.1";

    /// <summary>When this era's holders take their titles: a year before the bookmark.</summary>
    public string GrantDate => $"{Year - 1}.1.1";

    /// <summary>"early", "middle" or "late" — the vanilla bookmark it stands in for, and the suffix on every key it writes.</summary>
    public required string Tag { get; init; }

    /// <summary>The realms on this date, titled like the start date's.</summary>
    public required RealmMap Realms { get; init; }

    /// <summary>The ruler of every seat in <see cref="Realms"/>.</summary>
    public required RulerMap Rulers { get; init; }

    /// <summary>
    /// What each county is governed as on this date: the government cascade run at this date's
    /// advancement, less the bureaucracies, which need a realm built for them.
    /// </summary>
    public required GovernmentMap Governments { get; init; }

    /// <summary>
    /// Each ruler's death date, by character id: before the next bookmark, or absent for the rulers
    /// of the last one, who are alive when it starts and have nothing after it to make way for.
    /// </summary>
    public Dictionary<string, string> Deaths { get; } = [];

    /// <summary>Who holds each head-of-faith title on this date, by title key.</summary>
    public Dictionary<string, string> FaithHeads { get; } = [];

    /// <summary>The theocrats among <see cref="FaithHeads"/>, who are nobody's ruler otherwise.</summary>
    public List<EraPriest> Priests { get; } = [];

    /// <summary>Rulers on this date who answer to no one.</summary>
    public int Independent { get; set; }

    /// <summary>Of <see cref="Independent"/>, those of a house that also rules on the start date.</summary>
    public int Enduring { get; set; }

    /// <summary>Set by the bookmark writer; kept so an editor re-emit replays the same cast.</summary>
    public BookmarkCast? Cast { get; set; }
}

/// <summary>
/// The two additional bookmarks: with the start date, vanilla's 867 / 1066 / 1178, each on the
/// political map the formation simulation had drawn by its date (<see cref="MapConfig.AdditionalBookmarkDates"/>).
///
/// The simulation grows the world's realms over six centuries; its state at a date before the start
/// is copied as it runs, and one after the start comes from running it on past the start on a copy
/// (<see cref="FormationHistory.Snapshots"/>). Each is titled the same way the start date is
/// (<see cref="RealmMap.EraMaps"/>). Everything here is the people: one ruler per seat, dead before
/// the next bookmark, and a house for each. A realm that is also standing on the start date keeps
/// the house that rules it there — before, its ancestors; after, its descendants — and one that is
/// not gets a house of its own.
///
/// Nothing already generated is redrawn, so the start date's world is the one a run without this
/// writes; the files only gain what is dated before or after it. On a world whose realms were not
/// grown (an export that draws its own countries) the start date's map is reused and only the
/// people change.
/// </summary>
public sealed class BookmarkEras
{
    /// <summary>Oldest first.</summary>
    public required List<BookmarkEra> Eras { get; init; }

    /// <summary>The start date's year, which the eras sit either side of.</summary>
    public required int MainYear { get; init; }

    /// <summary>The world's seed, which every per-person draw here is salted with. See <see cref="Rng.For(int, int, ulong, int)"/>.</summary>
    public required int Seed { get; init; }

    /// <summary>The earliest date anything is seated on: the first bookmark before the start date's, else null.</summary>
    public string? FirstDate => Eras.FirstOrDefault(e => e.Year < MainYear)?.GrantDate;

    /// <summary>The first bookmark after the start date, which its people must be dead by.</summary>
    public BookmarkEra? FirstLater => Eras.FirstOrDefault(e => e.Year > MainYear);

    /// <summary>
    /// When one of the start date's people dies, if a later bookmark needs them gone: after the start
    /// date — so the start date never sees it — and before the later bookmark's holders are seated.
    /// Null when there is no later bookmark and the character file writes no death at all.
    /// </summary>
    public string? MainDeath(string id, int birthYear)
    {
        if (FirstLater is not { } later) return null;

        var rng = Rng.For(Seed, 0xDEAD, Rng.StableHash(id));
        int lo = MainYear + 1;
        int hi = Math.Max(lo, Math.Min(later.Year - 3, birthYear + 90));
        return $"{rng.Int(lo, hi)}.{rng.Int(1, 12)}.{rng.Int(1, 28)}";
    }

    public static BookmarkEras? Build(MapConfig cfg, List<Title> counties, RealmMap realms, RulerMap rulers,
        PrehistoryMap prehistory, CultureMap cultures, FaithMap faiths, GovernmentMap governments,
        WildernessMap wilderness, Dictionary<int, GovernmentMap>? eraGovernmentMaps = null)
    {
        if (!cfg.UsesAdditionalBookmarks) return null;

        var dates = cfg.AdditionalBookmarkDates;
        var result = new BookmarkEras { Eras = [], MainYear = cfg.StartYear, Seed = cfg.Seed };

        // Every bookmark year, the start's included, for "when is the next one".
        var allYears = dates.Select(d => d.Year).Append(cfg.StartYear).Order().ToList();

        // Which start-date seat each realm grew into, by the simulation's polity id: a realm that is
        // standing on the start date is ruled by that seat's house on every date it stands.
        var startSeatOf = new Dictionary<int, Title>();
        foreach (var p in realms.History?.Polities ?? [])
            if (rulers.Contains(p.Capital)) startSeatOf[p.Id] = p.Capital;

        var usedNames = prehistory.Dynasties.Values.Select(d => d.LocalizedName)
            .Concat(prehistory.Houses.Values.Select(h => h.LocalizedName))
            .ToHashSet(StringComparer.Ordinal);
        var minted = new Dictionary<string, (string Dynasty, string House)>();

        // The simulation's realm seated at each capital, and how far the date being built is from
        // the start — both read by HouseFor.
        var polityAt = new Dictionary<Title, int>();
        int yearsAway = 0;

        for (int i = 0; i < dates.Length; i++)
        {
            var (year, tag) = dates[i];
            int? next = allYears.Where(y => y > year).Select(y => (int?)y).FirstOrDefault();
            int salt = (i + 1) * 0x1F3D;
            yearsAway = Math.Abs(cfg.StartYear - year);

            var map = realms.EraMaps?.GetValueOrDefault(year) ?? WithoutHegemony(realms);
            polityAt.Clear();
            foreach (var p in map.History?.Polities ?? []) polityAt[p.Capital] = p.Id;

            // The cascade's answer for this date where one was run, else the start date's.
            var cascade = eraGovernmentMaps?.GetValueOrDefault(year) ?? governments;
            var eraGovernments = new Dictionary<Title, string>();
            foreach (var c in counties) eraGovernments[c] = EraGovernment(cascade.For(c));

            var era = new BookmarkEra
            {
                Year = year,
                Tag = tag,
                Realms = map,
                Rulers = new RulerMap(),
                Governments = new GovernmentMap(eraGovernments),
            };

            var liegeCounties = map.Liege.Values
                .Select(t => map.HolderCounty.GetValueOrDefault(t))
                .Where(c => c is not null)
                .ToHashSet();

            int founded = minted.Count, kept = 0;
            var seats = map.HolderCounty.Values.Where(c => !wilderness.Contains(c)).Distinct().OrderBy(c => c.Index);

            foreach (var seat in seats)
            {
                var rng = Rng.For(cfg.Seed, 0x3E2D, seat.Index, salt);
                var culture = cultures.For(seat);
                var faith = faiths.For(seat);
                var primary = HistoryWriter.Primary(seat, map);
                string government = eraGovernments.GetValueOrDefault(seat, GovernmentMap.Feudal);

                bool female = HistoryWriter.RulerIsFemale(seat, faith, cfg.Seed, salt);
                var names = female ? culture.FemaleNames : culture.MaleNames;
                string name = names.Count > 0 ? names[rng.Int(0, names.Count - 1)] : culture.Name;

                int age = rng.Int(22, 60);
                int birthYear = year - age;
                int birthMonth = rng.Int(1, 12);
                int birthDay = rng.Int(1, 28);

                // Dead before the next bookmark's holders are seated, and at an age a man dies at.
                // Drawn whether or not there is a next one, so the stream does not depend on it.
                int deathYear = rng.Int(year + 2, Math.Min(Math.Min(year + 38, (next ?? year + 40) - 3), birthYear + 85));
                string deathDate = $"{deathYear}.{rng.Int(1, 12)}.{rng.Int(1, 28)}";

                var (dynasty, house, carried) = HouseFor(seat);
                if (carried) kept++;

                bool hasVassals = liegeCounties.Contains(seat);
                var (gold, prestige, renown) = RulerMap.Purse(primary.Tier, government, rng);

                var ruler = new Ruler
                {
                    Seat = seat,
                    PrimaryTitle = primary,
                    Id = $"gen_char_{era.Tag}_{seat.Index}",
                    Culture = culture,
                    Faith = faith,
                    Government = government,
                    DynastyId = dynasty,
                    HouseKey = house,
                    Independent = !map.Liege.ContainsKey(primary),
                    HasVassals = hasVassals,
                    Name = name,
                    Female = female,
                    BirthYear = birthYear,
                    BirthMonth = birthMonth,
                    BirthDay = birthDay,
                    Profile = RulerProfile.Build(seat, primary.Tier, government, culture.Ethos, age, hasVassals,
                        cfg.Seed, salt),
                    Gold = gold,
                    Prestige = prestige,
                    Renown = renown,
                };

                era.Rulers.Seat(seat, ruler);
                if (next is not null) era.Deaths[ruler.Id] = deathDate;

                if (!ruler.Independent) continue;
                era.Independent++;
                if (carried) era.Enduring++;
            }

            SeatFaithHeads(era, next, salt);
            result.Eras.Add(era);

            Console.WriteLine($"  additional bookmark {year} ({tag}, as advanced as {cfg.EraYearAt(year)}): "
                              + $"{era.Independent} independent realms "
                              + $"({era.Enduring} of a house also ruling in {cfg.StartYear}), "
                              + $"{era.Rulers.All.Count} rulers — {kept} of such a house, "
                              + $"{minted.Count - founded} houses founded"
                              + (realms.EraMaps is null ? " (the start date's map: realms were not grown)" : ""));
        }

        return result;

        // The house a seat's ruler belongs to on an additional date, and whether it is one that also
        // rules on the start date.
        (string Dynasty, string House, bool Carried) HouseFor(Title seat)
        {
            // Continuity is by realm for a realm's own ruler, by county for the lords inside one.
            string key;
            if (polityAt.TryGetValue(seat, out int pid))
            {
                if (startSeatOf.TryGetValue(pid, out var now) && Existing(now) is { } line)
                    return (line.Dynasty, line.House, true);
                key = $"p{pid}";
            }
            else
            {
                // A county's lords are less sure to be the start date's family the further the date
                // is from it, either way: most count houses last a century, few last three.
                double endures = Math.Clamp(1.0 - yearsAway / 280.0, 0.1, 0.9);
                if (Rng.For(cfg.Seed, 0x51D1, seat.Index).NextDouble() < endures && Existing(seat) is { } local)
                    return (local.Dynasty, local.House, true);
                key = $"c{seat.Index}";
            }

            if (!minted.TryGetValue(key, out var made))
                minted[key] = made = Mint(key, seat);
            return (made.Dynasty, made.House, false);
        }

        // The start-date ruler's house, by its senior line: the ancestors of a cadet branch belong
        // to the dynasty's main house, which predates the branch.
        (string Dynasty, string House)? Existing(Title startSeat)
        {
            if (!rulers.TryGet(startSeat, out var ruler)) return null;
            if (!prehistory.Dynasties.TryGetValue(ruler.DynastyId, out var dyn)) return null;
            return (dyn.Id, dyn.MainHouseKey);
        }

        (string Dynasty, string House) Mint(string key, Title seat)
        {
            var culture = cultures.For(seat);
            string name = FreshName(culture, seat, key);

            string dynId = $"gen_dynasty_old_{key}";
            string nameKey = $"dynn_gen_old_{key}";
            string houseKey = $"house_gen_old_{key}";

            prehistory.Dynasties[dynId] = new DynastyDef
            {
                Id = dynId, NameKey = nameKey, LocalizedName = name, CultureKey = culture.Key, MainHouseKey = houseKey,
            };
            prehistory.Houses[houseKey] = new DynastyHouseDef
            {
                Key = houseKey, NameKey = nameKey, LocalizedName = name, DynastyId = dynId,
                Prefix = PrehistoryMap.CulturePrefix(culture.Key),
            };

            return (dynId, houseKey);
        }

        // A name from the culture's list that no house on the map carries yet, when one is left.
        string FreshName(Culture culture, Title seat, string key)
        {
            var list = culture.DynastyNames;
            if (list.Count == 0) return $"{culture.Name} {seat.Index}";

            int start = (int)(Rng.StableHash(key) % (ulong)list.Count);
            for (int k = 0; k < list.Count; k++)
            {
                string candidate = list[(start + k) % list.Count];
                if (usedNames.Add(candidate)) return candidate;
            }

            return culture.DynastyNameFor(seat, 3);
        }

        // Temporal heads go to the faith's greatest ruler on the date, as at the start; spiritual
        // ones, and a temporal one with no ruler of its faith, to a theocrat of their own.
        void SeatFaithHeads(BookmarkEra era, int? next, int salt)
        {
            int n = 0;
            foreach (var faith in faiths.Faiths)
            {
                if (faith.Head is null) continue;

                if (faith.Head.Temporal)
                {
                    var seat = era.Realms.HolderCounty
                        .Where(kv => !wilderness.Contains(kv.Value) && faiths.For(kv.Value) == faith)
                        .OrderByDescending(kv => HistoryWriter.Rank(kv.Key))
                        .ThenBy(kv => kv.Value.Index)
                        .Select(kv => kv.Value)
                        .FirstOrDefault();

                    if (seat is not null && era.Rulers.TryGet(seat, out var sovereign))
                    {
                        era.FaithHeads[faith.Head.TitleKey] = sovereign.Id;
                        continue;
                    }
                }

                var sample = counties.FirstOrDefault(c => !wilderness.Contains(c) && faiths.For(c) == faith)
                             ?? counties[0];
                var culture = cultures.For(sample);
                bool female = HistoryWriter.ClergyIsFemale(faith, cfg.Seed);
                var rng = Rng.For(cfg.Seed, 0x48A1, Rng.StableHash(faith.Key), salt);
                var names = female ? culture.FemaleNames : culture.MaleNames;
                string name = names.Count > 0 ? names[rng.Int(0, names.Count - 1)] : culture.Name;
                int birth = era.Year - rng.Int(35, 60);
                int death = rng.Int(era.Year + 2, Math.Min(era.Year + 30, (next ?? era.Year + 40) - 3));

                var priest = new EraPriest($"gen_hof_{era.Tag}_{n++}", name, female, culture.Key, faith.Key,
                    $"{birth}.1.1", next is null ? null : $"{death}.{rng.Int(1, 12)}.{rng.Int(1, 28)}");
                era.Priests.Add(priest);
                era.FaithHeads[faith.Head.TitleKey] = priest.Id;
            }
        }
    }

    /// <summary>
    /// The government an additional bookmark writes, less the bureaucracies. An administrative realm
    /// is a whole realm built for it — governors, noble families, a fallback for players without the
    /// expansion — and the additional maps are not built that way, so their rulers hold the same
    /// castles as feudal lords.
    /// </summary>
    public static string EraGovernment(string government)
        => GovernmentMap.IsAdminFamily(government) ? GovernmentMap.Feudal : government;

    /// <summary>
    /// A county's development on another date: a level per fifty years, the rate
    /// <see cref="Development"/> grows it at — less before the start, more after — and never below 1
    /// for a county that sets any. A bare county stays bare, as vanilla's do across its bookmarks.
    /// </summary>
    public static int EraDevelopment(int level, MapConfig cfg, int year)
    {
        if (year == cfg.StartYear || level <= Math.Max(0, cfg.DevelopmentBase)) return level;
        return Math.Max(1, level + (int)Math.Round((year - cfg.StartYear) / 50.0));
    }

    /// <summary>
    /// The start date's own map, for a world whose realms were not grown: the same realms, less a
    /// hegemony, which is crowned at the start date and on no date the simulation did not draw.
    /// </summary>
    private static RealmMap WithoutHegemony(RealmMap realms) => new()
    {
        HolderCounty = realms.HolderCounty.Where(kv => kv.Key.Tier != "h").ToDictionary(),
        Liege = realms.Liege.Where(kv => kv.Key.Tier != "h" && kv.Value.Tier != "h").ToDictionary(),
        Greatest = realms.Greatest,
        History = realms.History,
    };
}
