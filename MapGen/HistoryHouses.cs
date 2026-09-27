namespace Ck3MapGen.MapGen;

/// <summary>
/// What one house holds against another: a score that every new wrong adds to and the years wear
/// down, and the wrong that weighs most in it now — what the relation is said to be about.
/// </summary>
/// <param name="Since">The year the grudge began: its first wrong, or the start date's relation it
/// was carried from. A grudge that fades away entirely and is later rekindled starts again.</param>
/// <param name="Cause">won, held, fought, conquest, walked, seized, freed — or carried, for one the
/// start date's world already had, described by <paramref name="Carried"/>.</param>
/// <param name="Weight">What the chief wrong weighed when it was done.</param>
/// <param name="Where">The county it happened over: the war's target, the seized realm's seat, the
/// seat of the vassal that broke away.</param>
public sealed record SimGrudge(SimHouse A, SimHouse B, double Score, int Since, string Cause, int CauseYear,
    double Weight, Title? Where, HouseRelationDef? Carried = null)
{
    /// <summary>
    /// The house relation level the score stands at, or null below a quarrel. A single war lost
    /// makes a quarrel for a generation; a throne seized, a rivalry; a feud takes wrongs piled on
    /// wrongs faster than a lifetime forgets them.
    /// </summary>
    public string? Level => Score switch
    {
        >= HistorySim.FeudAt => "feud",
        >= HistorySim.RivalryAt => "rivalry",
        >= HistorySim.QuarrelAt => "quarrel",
        _ => null,
    };
}

/// <summary>
/// The houses' long memory: grudges between them, and the standing each has earned.
///
/// Neither rolls a die or moves a county. Grudges are written where a wrong is done — a peace
/// signed, a throne seized, a vassal gone — while the realms and their rulers are still at hand, and
/// the year wears each down by a fixed share; standing is read off who holds what at the end of
/// every year. With <see cref="RealmRules.Feuds"/> off no grudge is recorded; with it on, the realm
/// map plays out exactly as it does off. Standing is always kept and only carried when
/// <see cref="RealmRules.Standing"/> is on at capture.
///
/// The scale is set against what a claim is worth: a war lost leaves a claim for fifty years and a
/// grudge that is a quarrel for about as long; only wrongs repeated inside that span — or a throne
/// taken — reach a rivalry, and a feud needs both. Measured on the worlds in the history harness:
/// see the calibration in memory, history-feuds.
/// </summary>
public sealed partial class HistorySim
{
    /// <summary>The house relation thresholds on a grudge's score. See <see cref="SimGrudge.Level"/>.</summary>
    public const double QuarrelAt = 12, RivalryAt = 30, FeudAt = 60;

    /// <summary>
    /// A grudge's yearly fade: half of it gone in fifty years, a lifetime. Faster than standing's, so
    /// an old wrong is forgiven before an old greatness is forgotten.
    /// </summary>
    private const double GrudgeFade = 0.98623; // 0.5^(1/50)

    /// <summary>Below this a grudge is forgotten outright, so the table does not keep every war ever fought.</summary>
    private const double GrudgeFloor = 2;

    /// <summary>Standing's yearly fade: half in seventy years, about three reigns.</summary>
    private const double StandingFade = 0.99015; // 0.5^(1/70)

    /// <summary>The weight of each wrong. See the class summary for the scale.</summary>
    private const double WarLostBase = 6, WarLostPerCounty = 3, WarLostCap = 30,
        WarHeld = 8, WhitePeace = 3, CountyTaken = 4, ThroneSeized = 40, BrokeAway = 15, WalkedOut = 8;

    private readonly Dictionary<(int, int), SimGrudge> _grudges = [];
    private readonly Dictionary<SimHouse, double> _standing = [];

    /// <summary>Every grudge still remembered, strongest first. Carried by <see cref="AppliedHistory"/>.</summary>
    public IEnumerable<SimGrudge> Grudges
        => _grudges.Values.OrderByDescending(g => g.Score).ThenBy(g => g.A.Id).ThenBy(g => g.B.Id);

    /// <summary>What a house's history has earned it: the land it has held, a year at a time, fading.</summary>
    public double StandingOf(SimHouse house) => _standing.GetValueOrDefault(house);

    /// <summary>The house ruling each realm standing now, with what it has earned.</summary>
    public IEnumerable<(SimHouse House, double Standing)> Standings
        => _standing.Where(kv => kv.Value > 0).OrderBy(kv => kv.Key.Id).Select(kv => (kv.Key, kv.Value));

    /// <summary>
    /// A wrong done by <paramref name="offender"/>'s house to <paramref name="victim"/>'s, weighed
    /// <paramref name="weight"/>. Nothing between a house and itself — a partition's brothers, a war
    /// between two of one dynasty's realms settles no score in the relation CK3 keeps.
    /// </summary>
    private void Grieve(SimHouse? victim, SimHouse? offender, double weight, string cause, Title? where)
    {
        if (!_settings.Rules.HasFlag(RealmRules.Feuds)) return;
        if (victim is null || offender is null || victim == offender || weight <= 0) return;

        var (a, b) = victim.Id < offender.Id ? (victim, offender) : (offender, victim);
        var key = (a.Id, b.Id);
        int year = _sim.Year;

        if (!_grudges.TryGetValue(key, out var had))
        {
            _grudges[key] = new SimGrudge(a, b, weight, year, cause, year, weight, where);
            if (weight >= FeudAt)
                _sim.Log(FormationKind.Feud, where ?? SeatOf(offender) ?? SeatOf(victim)!, RealmOf(offender), RealmOf(victim), 3,
                    $"The houses of {a.Name} and {b.Name} are at feud");
            return;
        }

        // The relation is about whichever wrong weighs most now: the new one, or the old one as the
        // years have worn it down.
        bool newer = weight >= had.Weight * Math.Pow(GrudgeFade, year - had.CauseYear);
        var before = had.Level;
        var now = newer
            ? had with { Score = had.Score + weight, Cause = cause, CauseYear = year, Weight = weight, Where = where, Carried = null }
            : had with { Score = had.Score + weight };
        _grudges[key] = now;

        if (now.Level == "feud" && before != "feud")
            _sim.Log(FormationKind.Feud, where ?? SeatOf(offender) ?? SeatOf(victim)!, RealmOf(offender), RealmOf(victim), 3,
                $"The houses of {a.Name} and {b.Name} are at feud");
    }

    /// <summary>
    /// The start date's quarrels, rivalries and feuds between the houses the history seated, so it
    /// begins from the world as written rather than from goodwill. Their words go with them until a
    /// fresh wrong outweighs the old one.
    /// </summary>
    private void SeatGrudges(PrehistoryMap? prehistory)
    {
        if (prehistory is null) return;
        var byHouseKey = _rulerOf.Values.Select(r => r.House).Where(h => h.Carried is not null)
            .GroupBy(h => h.Carried!.HouseKey, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var rel in prehistory.HouseRelations)
        {
            double score = rel.Level switch
            {
                "feud" => FeudAt + 10,
                "rivalry" => RivalryAt + 5,
                "quarrel" => QuarrelAt + 3,
                _ => 0,
            };
            if (score == 0) continue;
            if (!byHouseKey.TryGetValue(rel.HouseA, out var x) || !byHouseKey.TryGetValue(rel.HouseB, out var y) || x == y) continue;

            var (a, b) = x.Id < y.Id ? (x, y) : (y, x);
            int since = int.TryParse(rel.StartDate?.Split('.')[0], out int year) ? Math.Min(year, _sim.Year) : _sim.Year;
            _grudges.TryAdd((a.Id, b.Id), new SimGrudge(a, b, score, since, "carried", since, score, null, rel));
        }
    }

    /// <summary>
    /// The land each house holds, as standing: every realm's own counties to its ruler's house, and
    /// half its vassals' to the house above them — what the start date's renown ladder grades by
    /// tier. Seeded as though the start date's houses had always held what they hold, which is the
    /// renown the written world already gave them for it.
    /// </summary>
    private void SeatStanding()
    {
        foreach (var (house, held) in Held())
            _standing[house] = held / (1 - StandingFade);
    }

    private Dictionary<SimHouse, double> Held()
    {
        var held = new Dictionary<SimHouse, double>();
        foreach (var p in _sim.Polities)
        {
            if (!p.Alive || RulerOf(p) is not { } ruler) continue;
            held[ruler.House] = held.GetValueOrDefault(ruler.House) + p.Counties.Count;
            if (p.Suzerain is { Alive: true } lord && RulerOf(lord) is { } liege && liege.House != ruler.House)
                held[liege.House] = held.GetValueOrDefault(liege.House) + 0.5 * p.Counties.Count;
        }
        return held;
    }

    /// <summary>
    /// The year's end for the houses: every grudge and every standing worn down a year, the year's
    /// holdings added to standing, and whatever concerns a house that rules nothing any more
    /// forgotten — it has left the stage, and CK3 keeps no relation for a house with no head in power.
    /// </summary>
    private void HousesYear()
    {
        var held = Held();

        foreach (var house in _standing.Keys.ToList())
            if (!held.ContainsKey(house)) _standing.Remove(house);
        foreach (var (house, land) in held)
            _standing[house] = _standing.GetValueOrDefault(house) * StandingFade + land;

        foreach (var (key, g) in _grudges.ToList())
        {
            var faded = g with { Score = g.Score * GrudgeFade };
            if (faded.Score < GrudgeFloor || !held.ContainsKey(g.A) || !held.ContainsKey(g.B))
            {
                _grudges.Remove(key);
                continue;
            }
            if (g.Level == "feud" && faded.Level != "feud" && _settings.Rules.HasFlag(RealmRules.Feuds))
                _sim.Log(FormationKind.Feud, SeatOf(g.A) ?? SeatOf(g.B)!, RealmOf(g.A), RealmOf(g.B), 0,
                    $"The feud between the houses of {g.A.Name} and {g.B.Name} has cooled");
            _grudges[key] = faded;
        }
    }

    /// <summary>
    /// The realm a house is headed from: its largest independent realm, else its largest. Where a
    /// house's grudges and standing are written — the start date keeps one relation per pair of houses.
    /// </summary>
    internal Polity? RealmOf(SimHouse house)
        => _sim.Polities.Where(p => p.Alive && RulerOf(p)?.House == house)
            .OrderBy(p => p.Suzerain is null ? 0 : 1).ThenByDescending(p => p.Counties.Count).ThenBy(p => p.Capital.Index)
            .FirstOrDefault();

    private Title? SeatOf(SimHouse house) => RealmOf(house)?.Capital;

    /// <summary>
    /// The formation's own wrongs this year — counties taken outside a war, vassals walking out of a
    /// realm coming apart — read off the events its step logged, while the realms are as it left them.
    /// </summary>
    private void GrieveStep(int from)
    {
        if (!_settings.Rules.HasFlag(RealmRules.Feuds)) return;
        for (int i = from; i < _sim.Events.Count; i++)
        {
            var e = _sim.Events[i];
            if (e.Kind is not (FormationKind.Conquest or FormationKind.Freed) || e.Actor is null || e.Counterpart is null) continue;
            var actor = OwnerOf(e.Actor);
            var other = OwnerOf(e.Counterpart);
            if (actor is null || other is null || actor == other) continue;

            // A conquest wrongs the realm it was taken from; a vassal walking out of a collapse wrongs
            // the lord it left, who is the one to hold it against him.
            if (e.Kind == FormationKind.Conquest)
                Grieve(RulerOf(other)?.House, RulerOf(actor)?.House, CountyTaken, "conquest", e.Subject);
            else
                Grieve(RulerOf(other)?.House, RulerOf(actor)?.House, WalkedOut, "walked", e.Actor);
        }
    }

    private void CheckHouses(List<string> problems)
    {
        foreach (var ((a, b), g) in _grudges)
        {
            if (a >= b || g.A.Id != a || g.B.Id != b) problems.Add($"grudge {g.A}/{g.B} filed under ({a}, {b})");
            if (!double.IsFinite(g.Score) || g.Score < GrudgeFloor) problems.Add($"grudge {g.A}/{g.B} scores {g.Score}");
            if (g.Since > _sim.Year || g.CauseYear > _sim.Year) problems.Add($"grudge {g.A}/{g.B} dated after {_sim.Year}");
        }
        foreach (var (house, standing) in _standing)
            if (!double.IsFinite(standing) || standing < 0) problems.Add($"{house} stands at {standing}");
    }
}
