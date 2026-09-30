using Ck3MapGen.Config;
using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen;

/// <summary>
/// One character of the additional bookmarks: a ruler of one of their dates, or one of the people
/// <see cref="BookmarkEras.BuildFamilies"/> draws around them. <see cref="Born"/> is what the character
/// file orders them by, which is what puts every parent ahead of its children.
/// </summary>
public sealed record EraPerson(int Born, string Id, HistoricalCharacter? Character = null,
    Ruler? Ruler = null, BookmarkEra? Era = null);

/// <summary>
/// The families of the additional bookmarks: who each of their rulers descends from, whom they
/// married, and the children and kin that make the dynasty trees trees.
///
/// Without this every ruler on an additional date was a single character — a name, two dates and a
/// house — so a dynasty tree on those dates was a row of strangers with nothing between them, and a
/// house that ruled on all three dates did not descend from itself.
///
/// The dates are walked oldest first, the start date among them, and every person any of them holds
/// goes into one pool per house. A ruler on an additional date descends from an earlier member of
/// their house through as many generated generations as the years need, twenty to forty-five years
/// apart; one whose house has nobody earlier gets a dead father of their own and founds it. Whichever
/// way the dates lie from the start — the start takes the vanilla slot nearest its advancement year,
/// so both, one or neither of the others can come before it — the start date's world is left alone:
///
/// <list type="bullet">
/// <item>An earlier date's people are all dead before the start. Its houses' lines are carried down to
/// the start date's rulers by giving the parents prehistory invented for them (and a ruler it gave
/// none) a father out of those lines — ancestry only, nobody new alive on the start date.</item>
/// <item>A later date's people are all born after the start, so the start date never sees them; its
/// lines hang from the start date's own people, whose deaths <see cref="MainDeath"/> already puts
/// after the start and before the later date.</item>
/// </list>
///
/// Everything here draws on streams of its own keyed by character id, after every other draw, so no
/// ruler, title or map moves.
/// </summary>
public sealed partial class BookmarkEras
{
    /// <summary>The characters of the dates before the start date, written ahead of its own.</summary>
    public List<EraPerson> Before { get; } = [];

    /// <summary>The characters of the dates after it, written after its own.</summary>
    public List<EraPerson> After { get; } = [];

    /// <summary>
    /// A parent by character id, for an additional date's ruler and for a start-date character that
    /// had none — a ruler prehistory gave no parent, or one of the dead parents it invented, when an
    /// earlier date's house now carries on to them.
    /// </summary>
    public Dictionary<string, (string Id, bool Mother)> Parents { get; } = new(StringComparer.Ordinal);

    /// <summary>Each additional date's married ruler: the spouse and the wedding date.</summary>
    public Dictionary<string, (string SpouseId, string Date)> Marriages { get; } = new(StringComparer.Ordinal);

    private sealed class Member
    {
        public required string Id { get; init; }
        public required int Born { get; init; }
        public int? Died { get; init; }
        public bool Female { get; init; }
        public required string House { get; init; }
        public required string Dynasty { get; init; }
        public required string Culture { get; init; }
        public required string Faith { get; init; }
        public Title? Seat { get; init; }

        /// <summary>May carry the house on: a man, or a woman who rules, whose children are hers.</summary>
        public bool Line { get; init; }

        /// <summary>Free to be married to an additional date's ruler.</summary>
        public bool Single { get; set; }

        public int Children { get; set; }
    }

    /// <summary>What a date's people may be: born no earlier than one year and dead by another.</summary>
    private readonly record struct Window(int MinBorn, int MaxDeath, List<EraPerson> Into);

    /// <summary>A character a line is drawn down to.</summary>
    private sealed record Target(string Id, int Born, string House, string Dynasty, string Culture,
        string Faith, Title? Seat);

    /// <summary>The generations between two people: the years each is born in, oldest first.</summary>
    private sealed record Plan(Member Anchor, int Lo, int Hi, bool Direct);

    private const int MinGap = 20, MaxGap = 45;

    internal string Summary { get; private set; } = "";

    internal void BuildFamilies(RulerMap rulers, PrehistoryMap prehistory, CultureMap cultures)
    {
        var cultureByKey = cultures.Cultures.GroupBy(c => c.Key)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var pool = new Dictionary<string, List<Member>>(StringComparer.Ordinal);
        var singles = new List<Member>();

        var before = new Window(int.MinValue, MainYear - 1, Before);
        var after = new Window(MainYear + 1, int.MaxValue, After);

        int descended = 0, founded = 0, generations = 0, kin = 0, matched = 0, lowborn = 0, born = 0, carried = 0;

        foreach (int year in Eras.Select(e => e.Year).Append(MainYear).Order())
        {
            if (year == MainYear)
            {
                if (Eras.Any(e => e.Year < MainYear)) CarryToStart();
                if (Eras.Any(e => e.Year > MainYear)) AddStartPeople();
                continue;
            }

            var era = Eras.First(e => e.Year == year);
            var window = year < MainYear ? before : after;
            var seated = era.Rulers.All.OrderBy(r => r.Seat.Index).ToList();

            foreach (var ruler in seated) Descend(ruler, era, window);
            foreach (var ruler in seated) Marry(ruler, era, window);
        }

        // Written oldest first, so a parent is always ahead of its children: the engine resolves
        // `father =` against the characters already read.
        Sort(Before);
        Sort(After);

        Summary = $"{descended} rulers descend from an earlier member of their house, {founded} found one; "
                  + $"{generations} generations between, {kin} kin, {matched} married into another house, "
                  + $"{lowborn} to a lowborn spouse, {born} children"
                  + (carried > 0 ? $"; {carried} start-date lines carried back to an earlier date" : "");
        return;

        static void Sort(List<EraPerson> people)
        {
            var ordered = people.OrderBy(p => p.Born).ThenBy(p => p.Id, StringComparer.Ordinal).ToList();
            people.Clear();
            people.AddRange(ordered);
        }

        // --- The additional dates' rulers --------------------------------------------------------

        void Descend(Ruler ruler, BookmarkEra era, Window window)
        {
            var rng = Rng.For(Seed, 0xEFA7, Rng.StableHash(ruler.Id));
            var target = new Target(ruler.Id, ruler.BirthYear, ruler.HouseKey, ruler.DynastyId,
                ruler.Culture.Key, ruler.Faith.Key, ruler.Seat);

            if (Anchor(target, window) is { } plan)
            {
                Parents[ruler.Id] = Chain(plan, target, window, rng);
                descended++;
            }
            else if (RootFather(target, era.Year, window, rng) is { } father)
            {
                Parents[ruler.Id] = (father, false);
                founded++;
            }

            int? died = era.Deaths.TryGetValue(ruler.Id, out var death) ? Year(death) : null;
            Add(new Member
            {
                Id = ruler.Id, Born = ruler.BirthYear, Died = died, Female = ruler.Female,
                House = ruler.HouseKey, Dynasty = ruler.DynastyId, Culture = ruler.Culture.Key,
                Faith = ruler.Faith.Key, Seat = ruler.Seat, Line = true,
            });
            window.Into.Add(new EraPerson(ruler.BirthYear, ruler.Id, Ruler: ruler, Era: era));
        }

        // The house's earliest known member on a date nobody earlier held: a father dead before the
        // bookmark, so the ruler is not the first of his line in the tree.
        string? RootFather(Target target, int eraYear, Window window, Rng rng)
        {
            int fatherBorn = Math.Max(target.Born - rng.Int(22, 38), window.MinBorn);
            if (target.Born - fatherBorn < 18) return null;

            int lo = target.Born + 1;
            int hi = Math.Min(Math.Min(eraYear - 1, fatherBorn + 80), window.MaxDeath);
            if (hi < lo) return null;

            var father = new Member
            {
                Id = $"{target.Id}_anc1", Born = fatherBorn, Died = rng.Int(lo, hi),
                House = target.House, Dynasty = target.Dynasty, Culture = target.Culture,
                Faith = target.Faith, Seat = target.Seat, Line = true, Children = 1,
            };
            Write(father, null, false, window, rng);
            Add(father);
            generations++;
            Kin(father, window);
            return father.Id;
        }

        // --- Lines between dates -----------------------------------------------------------------

        // The earlier member of the house a target can descend from: someone of the same seat first,
        // then the most recent — the fewest invented generations between.
        Plan? Anchor(Target target, Window window)
        {
            if (!pool.TryGetValue(target.House, out var members)) return null;

            Plan? best = null;
            foreach (var m in members)
            {
                if (!m.Line || m.Id == target.Id || Feasible(m, target.Born, window) is not { } plan) continue;
                if (best is null || Better(plan.Anchor, best.Anchor)) best = plan;
            }
            return best;

            bool Better(Member a, Member b)
            {
                bool sameA = a.Seat == target.Seat, sameB = b.Seat == target.Seat;
                if (sameA != sameB) return sameA;
                if (a.Born != b.Born) return a.Born > b.Born;
                return string.CompareOrdinal(a.Id, b.Id) < 0;
            }
        }

        // Whether a line can run from this member to a child born in `targetBorn`: directly, or
        // through generations born inside the window and dead by its end.
        static Plan? Feasible(Member m, int targetBorn, Window window)
        {
            int first = m.Born + 18;
            int last = Math.Min(m.Born + (m.Female ? 44 : 55), m.Died ?? int.MaxValue);
            if (targetBorn >= first && targetBorn <= last) return new Plan(m, 0, 0, Direct: true);

            // The last generation must still be alive when the target is born.
            if (targetBorn > window.MaxDeath) return null;

            int lo = Math.Max(first, window.MinBorn);
            int hi = Math.Min(last, targetBorn - MinGap);
            return lo <= hi ? new Plan(m, lo, hi, Direct: false) : null;
        }

        (string Id, bool Mother) Chain(Plan plan, Target target, Window window, Rng rng)
        {
            var anchor = plan.Anchor;
            anchor.Children++;
            if (plan.Direct) return (anchor.Id, anchor.Female);

            // The first generation about thirty years after the anchor, then the rest spread evenly
            // down to the target, as near thirty years apart as the span allows.
            int first = Math.Clamp(anchor.Born + rng.Int(22, 34), plan.Lo, plan.Hi);
            int span = target.Born - first;
            int count = Math.Clamp((int)Math.Round(span / 30.0), (span + MaxGap - 1) / MaxGap, span / MinGap);

            var births = new int[count];
            births[0] = first;
            for (int i = 1; i < count; i++)
            {
                int nominal = first + (int)Math.Round((double)span * i / count);
                int next = i + 1 < count ? first + (int)Math.Round((double)span * (i + 1) / count) : target.Born;
                int jittered = nominal + rng.Int(-3, 3);
                births[i] = Fits(jittered - births[i - 1]) && Fits(next - jittered) ? jittered : nominal;
            }

            string parent = anchor.Id;
            bool parentIsMother = anchor.Female;
            for (int i = 0; i < count; i++)
            {
                int childBorn = i + 1 < count ? births[i + 1] : target.Born;
                int hi = Math.Min(births[i] + 80, window.MaxDeath);
                int lo = Math.Min(childBorn + 1, hi);
                int died = Math.Clamp(births[i] + Lifespan(rng, childBorn - births[i] + 1), lo, hi);

                var member = new Member
                {
                    Id = $"{target.Id}_anc{count - i}", Born = births[i], Died = died,
                    House = target.House, Dynasty = target.Dynasty, Culture = target.Culture,
                    Faith = target.Faith, Seat = target.Seat, Line = true, Children = 1,
                };
                Write(member, parent, parentIsMother, window, rng);
                Add(member);
                generations++;
                Kin(member, window);

                parent = member.Id;
                parentIsMother = false;
            }

            return (parent, parentIsMother);

            static bool Fits(int gap) => gap >= MinGap && gap <= MaxGap;
        }

        // --- The start date ----------------------------------------------------------------------

        // An earlier date's houses carried down to the start date: every start-date character with
        // no parent — prehistory's invented dead parents, and any ruler it gave none — whose house
        // held something earlier descends from it. The new generations are all dead by the start.
        void CarryToStart()
        {
            var roots = new List<Target>();
            foreach (var c in prehistory.AllExtraCharacters)
            {
                if (!c.IsDeadAncestor || c.IsKin || c.FatherId is not null || c.MotherId is not null) continue;
                if (HouseOf(c) is not { } house) continue;
                roots.Add(new Target(c.Id, Year(c.BirthDate), house, c.DynastyId, c.CultureKey, c.FaithKey,
                    c.AssociatedCounty));
            }

            foreach (var r in rulers.All)
                if (r.ParentId is null && r.HistoricalBody is null)
                    roots.Add(new Target(r.Id, r.BirthYear, r.HouseKey, r.DynastyId, r.Culture.Key, r.Faith.Key, r.Seat));

            // An applied history's predecessors whose line it did not keep: the oldest of each.
            foreach (var p in prehistory.PastRulers)
                if (p.ParentId is null)
                    roots.Add(new Target(p.Id, Year(p.BirthDate), p.House.HouseKey, p.House.DynastyId, p.House.CultureKey,
                        p.FaithKey, null));

            foreach (var root in roots.OrderBy(t => t.Born).ThenBy(t => t.Id, StringComparer.Ordinal))
            {
                if (Anchor(root, before) is not { } plan) continue;
                Parents[root.Id] = Chain(plan, root, before, Rng.For(Seed, 0xEFA9, Rng.StableHash(root.Id)));
                carried++;
            }
        }

        // The start date's people, as the ancestors a later date's lines hang from. Each dies where
        // MainDeath puts them: after the start, before the next bookmark.
        void AddStartPeople()
        {
            foreach (var r in rulers.All)
            {
                if (r.HistoricalBody is not null) continue;
                Add(new Member
                {
                    Id = r.Id, Born = r.BirthYear, Died = MainDeathYear(r.Id, r.BirthYear), Female = r.Female,
                    House = r.HouseKey, Dynasty = r.DynastyId, Culture = r.Culture.Key, Faith = r.Faith.Key,
                    Seat = r.Seat, Line = true,
                });
            }

            // An applied history's predecessors, all dead before the start.
            foreach (var p in prehistory.PastRulers)
                Add(new Member
                {
                    Id = p.Id, Born = Year(p.BirthDate), Died = Year(p.DeathDate), Female = p.Female,
                    House = p.House.HouseKey, Dynasty = p.House.DynastyId, Culture = p.House.CultureKey,
                    Faith = p.FaithKey, Line = !p.Female,
                });

            foreach (var c in prehistory.AllExtraCharacters)
            {
                if (HouseOf(c) is not { } house) continue;
                int bornYear = Year(c.BirthDate);
                Add(new Member
                {
                    Id = c.Id, Born = bornYear,
                    Died = c.DeathDate is { } died ? Year(died) : MainDeathYear(c.Id, bornYear),
                    Female = c.Female, House = house, Dynasty = c.DynastyId, Culture = c.CultureKey,
                    Faith = c.FaithKey, Seat = c.AssociatedCounty, Line = !c.Female,
                });
            }
        }

        int? MainDeathYear(string id, int bornYear) => MainDeath(id, bornYear) is { } d ? Year(d) : null;

        string? HouseOf(HistoricalCharacter c)
        {
            if (c.DynastyHouseKey is { } house) return house;
            return c.DynastyId.Length > 0 && prehistory.Dynasties.TryGetValue(c.DynastyId, out var dyn)
                ? dyn.MainHouseKey : null;
        }

        // --- Families ----------------------------------------------------------------------------

        // Brothers and sisters for a generation of a line, as PrehistoryMap.AddKin draws them: a
        // family of one to five counting the child the line runs through, a fifth dead as children.
        void Kin(Member parent, Window window)
        {
            var rng = Rng.For(Seed, 0xEFAA, Rng.StableHash(parent.Id));
            double roll = rng.NextDouble();
            int family = roll < 0.15 ? 1 : roll < 0.40 ? 2 : roll < 0.65 ? 3 : roll < 0.85 ? 4 : 5;

            int lo = Math.Max(parent.Born + 17, window.MinBorn);
            int hi = Math.Min(parent.Born + (parent.Female ? 44 : 55), (parent.Died ?? int.MaxValue) - 1);
            hi = Math.Min(hi, window.MaxDeath - 1);
            if (hi < lo) return;

            for (int i = 0; parent.Children < family; i++)
            {
                bool female = rng.Chance(0.5);
                int childBorn = rng.Int(lo, hi);
                var child = Child($"{parent.Id}_kin{i}", female, childBorn, parent, window, rng);
                Write(child, parent.Id, parent.Female, window, rng);
                Add(child);
                parent.Children++;
                kin++;
            }
        }

        void Marry(Ruler ruler, BookmarkEra era, Window window)
        {
            // A bishop of a faith whose clergy may not marry stays single and childless, as on the
            // start date (see HistoryWriter.IsCelibateTheocrat). Before the ruler's own draw; the
            // single he would have wed stays free for someone else.
            if (Emit.HistoryWriter.IsCelibateTheocrat(ruler.Government, ruler.Faith)) return;

            var rng = Rng.For(Seed, 0xEFAB, Rng.StableHash(ruler.Id));
            if (!rng.Chance(0.85)) return;

            int? rulerDied = era.Deaths.TryGetValue(ruler.Id, out var death) ? Year(death) : null;
            int wedLo = ruler.BirthYear + 17;
            int wedHi = Math.Min(Math.Min(era.Year - 1, ruler.BirthYear + 30), (rulerDied ?? int.MaxValue) - 1);
            if (wedHi < wedLo) return;
            int wed = rng.Int(wedLo, wedHi);

            // Someone of another house on the same date, the right age, alive at the wedding: the
            // same faith and people where there is one, as a match between two houses would be.
            bool wantFemale = !ruler.Female;
            var candidates = singles
                .Where(m => m.Single && m.Female == wantFemale && m.House != ruler.HouseKey
                            && m.Born >= ruler.BirthYear - (wantFemale ? 6 : 10)
                            && m.Born <= ruler.BirthYear + (wantFemale ? 12 : 6)
                            && m.Born <= wed - 16 && (m.Died ?? int.MaxValue) > wed + 2
                            && (!wantFemale || m.Born + 40 > wed))
                .OrderByDescending(m => (m.Faith == ruler.Faith.Key ? 2 : 0) + (m.Culture == ruler.Culture.Key ? 1 : 0))
                .ThenBy(m => Math.Abs(m.Born - ruler.BirthYear))
                .ThenBy(m => m.Id, StringComparer.Ordinal)
                .Take(4)
                .ToList();

            string spouseId;
            int spouseBorn;
            int? spouseDied;
            if (candidates.Count > 0)
            {
                var spouse = candidates[rng.Int(0, candidates.Count - 1)];
                spouse.Single = false;
                spouseId = spouse.Id;
                spouseBorn = spouse.Born;
                spouseDied = spouse.Died;
                matched++;
            }
            else
            {
                spouseBorn = Math.Min(ruler.BirthYear + rng.Int(wantFemale ? -3 : -8, wantFemale ? 8 : 3), wed - 16);
                spouseBorn = Math.Max(spouseBorn, window.MinBorn);
                int hi = Math.Min(spouseBorn + 85, window.MaxDeath);
                int lo = Math.Min(wed + 3, hi);
                spouseDied = Math.Clamp(spouseBorn + Lifespan(rng, wed - spouseBorn + 3), lo, hi);
                spouseId = $"{ruler.Id}_spouse";

                var character = new HistoricalCharacter
                {
                    Id = spouseId, Name = GivenName(ruler.Culture, wantFemale, rng), Female = wantFemale,
                    DynastyId = "", CultureKey = ruler.Culture.Key, FaithKey = ruler.Faith.Key,
                    BirthDate = Date(spouseBorn, rng), DeathDate = Date(spouseDied.Value, rng),
                };
                window.Into.Add(new EraPerson(spouseBorn, spouseId, Character: character));
                lowborn++;
            }

            Marriages[ruler.Id] = (spouseId, $"{wed}.{rng.Int(1, 12)}.{rng.Int(1, 28)}");

            // Their children, in the ruler's house — a reigning woman marries matrilineally, as on the
            // start date. Within the mother's childbearing years and the parents' lives.
            int motherBorn = ruler.Female ? ruler.BirthYear : spouseBorn;
            int motherDied = (ruler.Female ? rulerDied : spouseDied) ?? int.MaxValue;
            int fatherDied = (ruler.Female ? spouseDied : rulerDied) ?? int.MaxValue;
            int childHi = Math.Min(Math.Min(motherBorn + 44, motherDied - 1), Math.Min(fatherDied, window.MaxDeath - 1));

            var self = new Member
            {
                Id = ruler.Id, Born = ruler.BirthYear, Died = rulerDied, Female = ruler.Female,
                House = ruler.HouseKey, Dynasty = ruler.DynastyId, Culture = ruler.Culture.Key,
                Faith = ruler.Faith.Key, Seat = ruler.Seat, Line = true,
            };

            double roll = rng.NextDouble();
            int count = roll < 0.2 ? 1 : roll < 0.5 ? 2 : roll < 0.8 ? 3 : 4;
            int childBorn = wed + 1 + rng.Int(0, 1);
            for (int i = 0; i < count && childBorn <= childHi; i++)
            {
                var child = Child($"{ruler.Id}_child{i}", rng.Chance(0.48), childBorn, self, window, rng);
                window.Into.Add(new EraPerson(childBorn, child.Id, Character: new HistoricalCharacter
                {
                    Id = child.Id, Name = GivenName(ruler.Culture, child.Female, rng), Female = child.Female,
                    DynastyId = ruler.DynastyId, DynastyHouseKey = ruler.HouseKey,
                    CultureKey = ruler.Culture.Key, FaithKey = ruler.Faith.Key,
                    BirthDate = Date(childBorn, rng), DeathDate = child.Died is { } d ? Date(d, rng) : null,
                    FatherId = ruler.Female ? spouseId : ruler.Id,
                    MotherId = ruler.Female ? ruler.Id : spouseId,
                }));
                Add(child);
                born++;
                childBorn += rng.Int(2, 4);
            }
        }

        // A child of a line or a ruler, as a member of the pool: in the parent's house and people.
        Member Child(string id, bool female, int childBorn, Member parent, Window window, Rng rng)
        {
            int died = rng.Chance(0.2) ? childBorn + rng.Int(1, 12) : childBorn + Lifespan(rng, 16);
            died = Math.Clamp(died, childBorn + 1, Math.Max(childBorn + 1, window.MaxDeath));
            return new Member
            {
                Id = id, Born = childBorn, Died = died, Female = female, House = parent.House,
                Dynasty = parent.Dynasty, Culture = parent.Culture, Faith = parent.Faith, Seat = parent.Seat,
                Line = !female, Single = true,
            };
        }

        void Add(Member m)
        {
            if (!pool.TryGetValue(m.House, out var list)) pool[m.House] = list = [];
            list.Add(m);
            if (m.Single) singles.Add(m);
        }

        // A pool member written as a character of its window.
        void Write(Member m, string? parentId, bool parentIsMother, Window window, Rng rng)
        {
            var culture = cultureByKey.GetValueOrDefault(m.Culture);
            window.Into.Add(new EraPerson(m.Born, m.Id, Character: new HistoricalCharacter
            {
                Id = m.Id, Name = culture is null ? "Nullbert" : GivenName(culture, m.Female, rng), Female = m.Female,
                DynastyId = m.Dynasty, DynastyHouseKey = m.House, CultureKey = m.Culture, FaithKey = m.Faith,
                BirthDate = Date(m.Born, rng), DeathDate = m.Died is { } d ? Date(d, rng) : null,
                FatherId = parentIsMother ? null : parentId,
                MotherId = parentIsMother ? parentId : null,
            }));
        }
    }

    /// <summary>
    /// An age at death, from <paramref name="from"/> on, on the curve PrehistoryMap.AddKin and the
    /// History workspace use — about 0.4% a year at thirty, doubling every eight — capped at 85.
    /// </summary>
    private static int Lifespan(Rng rng, int from)
    {
        int age = Math.Max(from, 1);
        while (age < 85 && !rng.Chance(Math.Min(1.0, 0.004 * Math.Exp(0.085 * (age - 30))))) age++;
        return age;
    }

    private static string GivenName(Culture culture, bool female, Rng rng)
    {
        var names = female ? culture.FemaleNames : culture.MaleNames;
        return names.Count > 0 ? names[rng.Int(0, names.Count - 1)] : female ? "Nullberta" : "Nullbert";
    }

    private static string Date(int year, Rng rng) => $"{year}.{rng.Int(1, 12)}.{rng.Int(1, 28)}";

    private static int Year(string date) => int.Parse(date.Split('.')[0]);
}
