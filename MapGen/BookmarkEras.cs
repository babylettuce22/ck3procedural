using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.Emit;

namespace Ck3MapGen.MapGen;

/// <summary>One earlier start date and who holds the land on it.</summary>
public sealed class BookmarkEra
{
    public required int Year { get; init; }
    public string Date => $"{Year}.1.1";

    /// <summary>"early" or "middle": the suffix on every key this bookmark writes.</summary>
    public required string Tag { get; init; }

    /// <summary>The rulers on this date, by the start-date seat whose land they hold.</summary>
    public required RulerMap Rulers { get; init; }

    /// <summary>Who inherits from each ruler of this era, by that ruler's id: the next generation.</summary>
    public Dictionary<string, Ruler> Heirs { get; } = [];

    /// <summary>Set by the bookmark writer; kept so an editor re-emit replays the same cast.</summary>
    public BookmarkCast? Cast { get; set; }
}

/// <summary>
/// The two generations before the start date, on the same political map.
///
/// Built from what prehistory already decided: every start-date ruler has a dead parent, and that
/// parent is who held the land twenty years earlier. One more generation — the parent's parent,
/// made here — holds it at the earliest date. Nothing already generated is redrawn, so the
/// start-date world is the one a run without this writes.
///
/// Dates are chosen so the chain always works: a parent dies between the middle bookmark and the
/// start (prehistory draws start − 2..16), and an elder dies between the two earlier bookmarks,
/// after the parent is sixteen.
/// </summary>
public sealed class BookmarkEras
{
    /// <summary>Oldest first.</summary>
    public required List<BookmarkEra> Eras { get; init; }

    /// <summary>The grandparents, who hold the land at the earliest date.</summary>
    public List<HistoricalCharacter> Elders { get; } = [];

    /// <summary>The parent of each prehistory ancestor who now has one, by the ancestor's id.</summary>
    public Dictionary<string, HistoricalCharacter> ParentOf { get; } = [];

    /// <summary>The ruler behind each ancestor who held land, by id: skills, traits and portrait.</summary>
    public Dictionary<string, Ruler> Profiles { get; } = [];

    /// <summary>
    /// Per start-date seat, every holder of its titles from the earliest date on, oldest first,
    /// with the date each took them. The last entry is the start-date ruler.
    /// </summary>
    public Dictionary<Title, List<(string Date, string HolderId)>> Succession { get; } = [];

    public string FirstDate => Eras[0].Date;

    public static BookmarkEras? Build(MapConfig cfg, RulerMap rulers, PrehistoryMap prehistory)
    {
        if (!cfg.UsesEarlierBookmarks) return null;

        int[] years = cfg.EarlierBookmarkYears;
        var early = new BookmarkEra { Year = years[0], Tag = "early", Rulers = new RulerMap() };
        var middle = new BookmarkEra { Year = years[1], Tag = "middle", Rulers = new RulerMap() };
        var eras = new BookmarkEras { Eras = [early, middle] };

        var middleById = new Dictionary<string, Ruler>();
        var earlyById = new Dictionary<string, Ruler>();
        int skipped = 0;

        // Highest title first, so a father shared by two brothers is seated where the elder holds.
        var ordered = rulers.All
            .Select((r, i) => (Ruler: r, Order: i))
            .OrderByDescending(x => HistoryWriter.Rank(x.Ruler.PrimaryTitle))
            .ThenByDescending(x => x.Ruler.Independent)
            .ThenBy(x => x.Order)
            .Select(x => x.Ruler);

        foreach (var now in ordered)
        {
            if (!prehistory.DeceasedParents.TryGetValue(now.Seat, out var parent)
                || parent.DeathDate is null || now.IsHistorical
                || YearOf(parent.DeathDate) <= middle.Year || YearOf(parent.BirthDate) + 16 > middle.Year)
            {
                skipped++;
                continue;
            }

            if (!middleById.TryGetValue(parent.Id, out var father))
            {
                var elder = MakeElder(parent, now.Culture, early.Year, middle.Year);
                eras.Elders.Add(elder);
                eras.ParentOf[parent.Id] = elder;

                father = Past(parent, now, middle.Year, 0x51D1, elder);
                var grandfather = Past(elder, now, early.Year, 0x7A3E, null);

                middleById[parent.Id] = father;
                earlyById[elder.Id] = grandfather;
                eras.Profiles[parent.Id] = father;
                eras.Profiles[elder.Id] = grandfather;

                middle.Heirs[father.Id] = now;
                early.Heirs[grandfather.Id] = father;
            }

            middle.Rulers.Seat(now.Seat, father);
            early.Rulers.Seat(now.Seat, earlyById[eras.ParentOf[parent.Id].Id]);

            var elderOf = eras.ParentOf[parent.Id];
            eras.Succession[now.Seat] =
            [
                (early.Date, elderOf.Id),
                (elderOf.DeathDate!, parent.Id),
                (parent.DeathDate, now.Id),
            ];
        }

        Console.WriteLine($"  earlier bookmarks: {early.Year} and {middle.Year} — "
                          + $"{middleById.Count} parents and {eras.Elders.Count} elders seated"
                          + (skipped > 0 ? $", {skipped} seats with no line to trace" : ""));

        return eras;
    }

    /// <summary>The parent's parent: same house, adult on the earliest date, dead before the middle one.</summary>
    private static HistoricalCharacter MakeElder(HistoricalCharacter parent, Culture culture, int earlyYear,
        int middleYear)
    {
        var rng = new Rng(Rng.StableHash(parent.Id) ^ 0xE1DE5UL);

        int parentBirth = YearOf(parent.BirthDate);
        int birth = parentBirth - rng.Int(20, 30);
        int lo = Math.Max(earlyYear + 1, parentBirth + 16);
        int hi = Math.Max(lo, Math.Min(middleYear - 1, birth + 80));
        int death = rng.Int(lo, hi);

        // Same sex as the parent: the line runs the way the land does, as prehistory draws it.
        var names = parent.Female ? culture.FemaleNames : culture.MaleNames;
        string name = names.Count > 0 ? names[rng.Int(0, names.Count - 1)] : parent.Name;

        return new HistoricalCharacter
        {
            Id = parent.Id.Replace("gen_char_parent_", "gen_char_elder_"),
            Name = name,
            Female = parent.Female,
            DynastyId = parent.DynastyId,
            DynastyHouseKey = parent.DynastyHouseKey,
            CultureKey = parent.CultureKey,
            FaithKey = parent.FaithKey,
            BirthDate = $"{birth}.{rng.Int(1, 12)}.{rng.Int(1, 28)}",
            DeathDate = $"{death}.{rng.Int(1, 12)}.{rng.Int(1, 28)}",
            AssociatedCounty = parent.AssociatedCounty,
            IsDeadAncestor = true,
        };
    }

    /// <summary>An ancestor as the ruler of <paramref name="now"/>'s seat on an earlier date.</summary>
    private static Ruler Past(HistoricalCharacter who, Ruler now, int year, int salt, HistoricalCharacter? parent)
    {
        var birth = who.BirthDate.Split('.').Select(int.Parse).ToArray();

        return new Ruler
        {
            Seat = now.Seat,
            PrimaryTitle = now.PrimaryTitle,
            Id = who.Id,
            Culture = now.Culture,
            Faith = now.Faith,
            Government = now.Government,
            DynastyId = who.DynastyId,
            HouseKey = who.DynastyHouseKey ?? "",
            ParentId = parent?.Id,
            ParentIsMother = parent?.Female ?? false,
            Independent = now.Independent,
            HasVassals = now.HasVassals,
            Name = who.Name,
            Female = who.Female,
            BirthYear = birth[0],
            BirthMonth = birth[1],
            BirthDay = birth[2],
            Profile = RulerProfile.Build(now.Seat, now.Tier, now.Government, now.Culture.Ethos,
                year - birth[0], now.HasVassals, salt),
            Gold = now.Gold,
            Prestige = now.Prestige,
            Renown = now.Renown,
        };
    }

    private static int YearOf(string date) => int.Parse(date.Split('.')[0]);
}
