using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen;

/// <summary>A ruling house in the History workspace's simulation.</summary>
public sealed class SimHouse
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required Culture Culture { get; init; }
    public required int Founded { get; init; }

    /// <summary>
    /// The written world's dynasty this house is, when it was already ruling at the start; null for
    /// a house founded during the history. What <see cref="AppliedHistory"/> carries across, so that
    /// an enduring house keeps its keys, its name and its arms.
    /// </summary>
    public AppliedHistory.Lineage? Carried { get; init; }

    public override string ToString() => Name;
}

/// <summary>One realm's ruler in the History workspace's simulation.</summary>
public sealed class SimRuler
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required bool Female { get; init; }
    public required int Born { get; init; }
    public required SimHouse House { get; init; }

    /// <summary>The year the reign began.</summary>
    public required int Crowned { get; init; }

    /// <summary>The year the ruler died, once they have.</summary>
    public int? Died { get; set; }

    /// <summary>The year the reign ended without a death — the realm itself was swallowed.</summary>
    public int? Deposed { get; set; }

    public override string ToString() => $"{Name} of {House.Name}";
}

/// <summary>
/// How a realm passes on, by government family: divided among heirs, whole to one, or chosen.
/// </summary>
public enum SuccessionLaw { Partition, Single, Elective }

/// <summary>
/// The people half of the history: a ruler for every realm, who ages and dies, and the heir who
/// takes it after — whole, divided among brothers, or seized by another house.
///
/// Runs after the realms' year on a stream of its own (<c>_seed ^ 0x5CC5</c>), and with
/// <see cref="RealmRules.Succession"/> off its only effect on the map is none at all: rulers still
/// die and are succeeded, always by one heir of the same house, so the realms play out exactly as
/// they do without people in them. With it on, a death can divide a realm or hand it to a new house,
/// which is how realms in CK3 come apart as often as they are conquered.
///
/// Every roll that decides what a succession does is made whether or not Succession is on, as the
/// realm rules' are (<see cref="RealmRules"/>), so switching it moves no one else's dice.
/// </summary>
public sealed partial class HistorySim
{
    private readonly Dictionary<int, SimRuler> _rulerOf = [];
    private readonly Dictionary<int, SuccessionLaw> _law = [];
    private readonly Dictionary<int, double> _femaleShare = [];
    private readonly List<(int PolityId, SimRuler Ruler)> _reigns = [];
    private readonly HashSet<string> _houseNames = new(StringComparer.Ordinal);
    private int _nextRuler, _nextHouse;

    /// <summary>The realm's ruler now, or null for one the simulation has not seated — none, after a tick.</summary>
    public SimRuler? RulerOf(Polity p) => _rulerOf.GetValueOrDefault(p.Id);

    /// <summary>Every reign since the start, the start's own included, oldest first, by realm id.</summary>
    public IReadOnlyList<(int PolityId, SimRuler Ruler)> Reigns => _reigns;

    /// <summary>How a realm passes on.</summary>
    public SuccessionLaw LawOf(Polity p) => _law.GetValueOrDefault(p.Id, SuccessionLaw.Partition);

    /// <summary>
    /// The start date's rulers on the realms they held: each realm under the man the written world
    /// seated at its capital, of that man's dynasty. A realm with none to be had — no written
    /// people, or a seat the roster does not cover — is given a ruler and a house of its own.
    /// </summary>
    private void SeatStartRulers(RulerMap? rulers, PrehistoryMap? prehistory)
    {
        var byDynasty = new Dictionary<string, SimHouse>(StringComparer.Ordinal);
        var rng = new Rng(_seed ^ 0x5EA7);

        foreach (var p in _sim.Polities.OrderBy(p => p.Capital.Index))
        {
            if (rulers is not null && prehistory is not null && rulers.TryGet(p.Capital, out var ruler)
                && AppliedHistory.LineageOf(ruler, prehistory) is { } line)
            {
                if (!byDynasty.TryGetValue(line.DynastyId, out var house))
                {
                    byDynasty[line.DynastyId] = house = new SimHouse
                    {
                        Id = _nextHouse++, Name = line.DynastyName, Culture = ruler.Culture,
                        Founded = StartYear, Carried = line,
                    };
                    _houseNames.Add(line.DynastyName);
                }

                _law[p.Id] = LawFor(ruler.Government);
                _femaleShare[p.Id] = FemaleShareOf(ruler.Faith);
                Seat(p, new SimRuler
                {
                    Id = _nextRuler++, Name = ruler.Name, Female = ruler.Female, Born = ruler.BirthYear,
                    House = house, Crowned = StartYear,
                });
            }
            else
            {
                _law[p.Id] = SuccessionLaw.Partition;
                _femaleShare[p.Id] = 0.05;
                Seat(p, NewRuler(p, FoundHouse(p.Culture, rng), rng, age: rng.Int(20, 55)));
            }
        }
    }

    /// <summary>The rulers' year: seats for realms born in it, then deaths and what follows them.</summary>
    private void RulersYear()
    {
        int year = _sim.Year;
        var rng = new Rng(_seed ^ 0x5CC5 ^ unchecked((int)((uint)year * 0x9E3779B1u)));

        // A realm swallowed this year ends its ruler's reign, not his life.
        var alive = _sim.Polities.Where(p => p.Alive).Select(p => p.Id).ToHashSet();
        foreach (int id in _rulerOf.Keys.Where(id => !alive.Contains(id)).ToList())
        {
            _rulerOf[id].Deposed = year;
            _rulerOf.Remove(id);
        }

        SeatNewRealms(rng);

        bool succession = _sim.Rules.HasFlag(RealmRules.Succession);
        foreach (var p in _sim.Polities.Where(p => p.Alive).OrderBy(p => p.Capital.Index).ToList())
        {
            if (!p.Alive || !_rulerOf.TryGetValue(p.Id, out var ruler)) continue;

            int age = year - ruler.Born;
            if (!rng.Chance(DeathChance(age))) continue;

            Succeed(p, ruler, rng, succession);
        }

        // A partition can leave a remainder in pieces; the pieces are realms, and realms need rulers.
        SeatNewRealms(rng);
    }

    /// <summary>
    /// The chance of dying this year at a given age: Gompertz, about 0.4% at thirty doubling every
    /// eight years — 2% at fifty, 5% at sixty, 12% at seventy, 28% at eighty. Reigns come out a
    /// generation long, which is the pace CK3's own lines change hands at.
    /// </summary>
    private static double DeathChance(int age) => Math.Min(1.0, 0.004 * Math.Exp(0.085 * (age - 30)));

    private void Succeed(Polity p, SimRuler dead, Rng rng, bool succession)
    {
        int year = _sim.Year;
        dead.Died = year;
        var law = LawOf(p);

        // Both rolled on every death, whatever the switch says: see the class summary.
        double instability = 1.0 - Formation.Cohesion(_sim, p);
        // The Heirs and Crises dials scale these; at their default of 1 the numbers are unchanged.
        bool newHouse = rng.Chance(law == SuccessionLaw.Elective
            ? 0.5
            : Math.Min(1.0, (0.02 + 0.25 * instability) * _settings.Crises));
        int younger = (rng.Chance(Math.Min(1.0, 0.5 * _settings.Heirs)) ? 1 : 0)
                    + (rng.Chance(Math.Min(1.0, 0.25 * _settings.Heirs)) ? 1 : 0);

        if (succession && newHouse)
        {
            var house = FoundHouse(p.Culture, rng);
            var usurper = NewRuler(p, house, rng, age: rng.Int(25, 50));
            Seat(p, usurper);
            _sim.Log(FormationKind.Usurped, p.Capital, p, null, 2,
                law == SuccessionLaw.Elective
                    ? $"{dead} died; {usurper.Name} of {house.Name} was chosen to rule {p.Capital.Name}"
                    : $"{dead} died; {usurper.Name} of {house.Name} seized {p.Capital.Name}");
            return;
        }

        var heir = Heir(p, dead, rng);
        Seat(p, heir);

        // Divided only where there is enough to divide: a block for every younger heir and a larger
        // share left for the eldest. A realm too small to share goes to the eldest whole.
        if (succession && law == SuccessionLaw.Partition && younger > 0 && p.Counties.Count >= 3 * (younger + 1))
        {
            Partition(p, dead, heir, younger, rng);
            return;
        }

        _sim.Log(FormationKind.Succeeded, p.Capital, p, null, 0,
            $"{dead} died; {heir.Name} succeeded to {p.Capital.Name}");
    }

    /// <summary>
    /// Divides a realm between the eldest heir and his younger siblings. Each younger heir takes a
    /// connected block off the edge, grown the way a secession's is, and either swears to the
    /// eldest — when the chain of homage has room — or stands alone, as CK3's partition leaves a
    /// younger son who inherited a title of his own.
    /// </summary>
    private void Partition(Polity p, SimRuler dead, SimRuler eldest, int younger, Rng rng)
    {
        int share = Math.Max(2, p.Counties.Count / (younger + 2));

        for (int i = 0; i < younger; i++)
        {
            var block = Formation.PeripheralBlock(_sim, p, share);
            if (block.Count == 0 || block.Count >= p.Counties.Count) break;

            var sibling = Heir(p, dead, rng);
            bool sworn = rng.Chance(0.5);
            string place = block.OrderByDescending(c => _sim.Development.GetValueOrDefault(c)).ThenBy(c => c.Index).First().Name;

            var part = Formation.Secede(_sim, p, block, FormationKind.Partitioned, 1,
                $"{dead} died and the realm was divided: {sibling.Name} took {place}, {eldest.Name} kept {p.Capital.Name}");
            if (part is null) break;

            if (sworn && p.Depth + 1 + Formation.SubtreeDepth(_sim, part) <= Polity.MaxDepth)
                part.Suzerain = p;

            _law[part.Id] = _law[p.Id];
            _femaleShare[part.Id] = _femaleShare[p.Id];
            Seat(part, sibling);
        }

        Formation.ShedIslands(_sim, p);
    }

    /// <summary>The dead ruler's child: of his house and culture, born a generation after him.</summary>
    private SimRuler Heir(Polity p, SimRuler parent, Rng rng)
    {
        int year = _sim.Year;
        bool female = rng.Chance(_femaleShare.GetValueOrDefault(p.Id, 0.05));

        // A generation on from the parent; one who would be unborn yet is a child on the throne.
        int born = parent.Born + rng.Int(18, 40);
        if (born > year - 1) born = year - rng.Int(1, 16);

        return new SimRuler
        {
            Id = _nextRuler++, Name = GivenName(parent.House.Culture, female, rng), Female = female,
            Born = born, House = parent.House, Crowned = year,
        };
    }

    private SimRuler NewRuler(Polity p, SimHouse house, Rng rng, int age)
    {
        bool female = rng.Chance(_femaleShare.GetValueOrDefault(p.Id, 0.05));
        return new SimRuler
        {
            Id = _nextRuler++, Name = GivenName(house.Culture, female, rng), Female = female,
            Born = _sim.Year - age, House = house, Crowned = _sim.Year,
        };
    }

    /// <summary>
    /// Gives a ruler to every realm without one — a secession, a split-off island — as a house of
    /// its own, under the laws of the realm it came out of.
    /// </summary>
    private void SeatNewRealms(Rng rng)
    {
        foreach (var p in _sim.Polities.Where(p => p.Alive && !_rulerOf.ContainsKey(p.Id))
                                       .OrderBy(p => p.Capital.Index).ToList())
        {
            if (ParentOf(p) is { } parent)
            {
                _law[p.Id] = LawOf(parent);
                _femaleShare[p.Id] = _femaleShare.GetValueOrDefault(parent.Id, 0.05);
            }
            else
            {
                _law.TryAdd(p.Id, SuccessionLaw.Partition);
                _femaleShare.TryAdd(p.Id, 0.05);
            }

            Seat(p, NewRuler(p, FoundHouse(p.Culture, rng), rng, age: rng.Int(20, 50)));
        }
    }

    /// <summary>The realm a new one broke away from this year, from the event that says so.</summary>
    private Polity? ParentOf(Polity p)
    {
        for (int i = _sim.Events.Count - 1; i >= 0 && _sim.Events[i].Year == _sim.Year; i--)
        {
            var e = _sim.Events[i];
            if (e.Actor == p.Capital && e.Counterpart is { } from && _sim.Owner.TryGetValue(from, out var parent)
                && parent != p)
                return parent;
        }
        return null;
    }

    /// <summary>A new house with a name from its culture's list that no house in the history has used.</summary>
    private SimHouse FoundHouse(Culture culture, Rng rng)
    {
        int id = _nextHouse++;
        string name = $"{culture.Name} {id}";
        var list = culture.DynastyNames;
        if (list.Count > 0)
        {
            int start = rng.Int(0, list.Count - 1);
            for (int k = 0; k < list.Count; k++)
            {
                string candidate = list[(start + k) % list.Count];
                if (_houseNames.Contains(candidate)) continue;
                name = candidate;
                break;
            }
        }

        _houseNames.Add(name);
        return new SimHouse { Id = id, Name = name, Culture = culture, Founded = _sim.Year };
    }

    private void Seat(Polity p, SimRuler ruler)
    {
        _rulerOf[p.Id] = ruler;
        _reigns.Add((p.Id, ruler));
    }

    private static string GivenName(Culture culture, bool female, Rng rng)
    {
        var names = female ? culture.FemaleNames : culture.MaleNames;
        return names.Count > 0 ? names[rng.Int(0, names.Count - 1)] : culture.Name;
    }

    /// <summary>
    /// Partition for the families CK3 partitions under at these eras; one heir for the bureaucracies;
    /// a choice for republics and theocracies, whose next ruler need not be of the last one's house.
    /// </summary>
    private static SuccessionLaw LawFor(string government) => GovernmentMap.Family(government) switch
    {
        GovernmentMap.Republic or GovernmentMap.Theocracy => SuccessionLaw.Elective,
        GovernmentMap.Administrative => SuccessionLaw.Single,
        _ => SuccessionLaw.Partition,
    };

    /// <summary>The chance an heir is a woman, from the faith's gender doctrine — the shares HistoryWriter.RulerIsFemale uses.</summary>
    private static double FemaleShareOf(Faith faith) => Faiths.GenderOf(faith) switch
    {
        "doctrine_gender_female_dominated" => 0.95,
        "doctrine_gender_equal" => 0.45,
        _ => 0.05,
    };

    private void CheckRulers(List<Polity> alive, List<string> problems)
    {
        var seen = new HashSet<SimRuler>();
        foreach (var p in alive)
        {
            if (!_rulerOf.TryGetValue(p.Id, out var ruler)) { problems.Add($"{p}: no ruler"); continue; }
            if (!seen.Add(ruler)) problems.Add($"{p}: ruler {ruler} also rules another realm");
            if (ruler.Died is not null) problems.Add($"{p}: ruled by the dead {ruler}");
            int age = _sim.Year - ruler.Born;
            if (age < 0 || age > 110) problems.Add($"{p}: ruler {ruler} is {age}");
        }
    }
}
