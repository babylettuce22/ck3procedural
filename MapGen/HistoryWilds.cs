using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen;

/// <summary>
/// What the frontier history needs that the realm simulation does not have: which counties are
/// wild, and every county's neighbours including the wild ones — the realm simulation's own
/// adjacency is built over settled land only, so it cannot see across a border into the wild.
/// </summary>
/// <param name="Wilderness">The wilderness the history starts from: the written world's, which an
/// earlier applied history may already have moved.</param>
/// <param name="Generated">The wilderness the world was generated with. What an applied history
/// records its frontier against, so a history run on from an applied one still describes the whole
/// change — a full write regenerates the world and has only this to lay it over.</param>
/// <param name="Adjacency">Every county's neighbours, wild and settled, at the bridge distance the
/// realm simulation uses — see <see cref="Realms.BuildCountyAdjacency"/>.</param>
public sealed record WildsGround(WildernessMap Wilderness, WildernessMap Generated,
    IReadOnlyDictionary<Title, HashSet<Title>> Adjacency)
{
    /// <summary>
    /// Whether land can fall to ruin: only on a world written with the ruins system, and only with
    /// some wilderness already on it, since a ruin is seated on the dummy the wilderness brings.
    /// </summary>
    public bool CanRuin => Wilderness.RuinsEnabled && Wilderness.Count > 0;
}

/// <summary>
/// The frontier: realms settling the wilderness on their borders, and neglected land falling out
/// of civilisation as ruins.
///
/// Never by war. The wild is outside the realm simulation — no realm has a wild county as a
/// neighbour — and this is the only door in or out: a settled county joins the realm that settled
/// it and from then on is fought over like any other; a ruin leaves the simulation's ground
/// entirely until someone settles it again.
///
/// Mirrors the mod's own runtime systems, which is what the start date hands over to: colonies are
/// founded from a realm's border (the Wilds situation), and ruination asks for sustained neglect
/// in a failing realm, never for a conquest (see the Ruins file set). The same vetoes
/// apply: no realm capital, no de jure capital, never a realm's last few counties, and never a
/// cut that would leave a realm in pieces.
/// </summary>
public sealed partial class HistorySim
{
    /// <summary>
    /// The chance a year that a wild county on a realm's border is settled, before the dial. A
    /// border county takes about a century and a half on average; land further in waits for its
    /// neighbours to be settled first, so the frontier moves as a front rather than all at once.
    /// </summary>
    private const double SettleChance = 0.006;

    /// <summary>
    /// The chance a year that an eligible county falls, before the dial, at the worst instability —
    /// scaled by instability squared, so a steady realm almost never loses one.
    /// </summary>
    private const double FallChance = 0.004;

    /// <summary>A realm smaller than this keeps all its land: losing a county would be most of it.</summary>
    private const int FallMinimumRealm = 3;

    private WildsGround? _wilds;
    private readonly HashSet<Title> _wild = [];
    private readonly HashSet<Title> _startWild = [];

    /// <summary>Counties settled during the history, with the year, while they stay settled.</summary>
    private readonly Dictionary<Title, int> _settledIn = [];

    /// <summary>Counties that fell to ruin during the history, with the year, while they stay ruined.</summary>
    private readonly Dictionary<Title, int> _fellIn = [];

    /// <summary>Whether this history has a frontier at all — a wilderness to settle and neighbours to reach it by.</summary>
    public bool HasWilds => _wilds is not null && _startWild.Count > 0;

    /// <summary>Whether land can fall to ruin in this history. See <see cref="WildsGround.CanRuin"/>.</summary>
    public bool CanRuin => _wilds?.CanRuin == true;

    /// <summary>Is this county wild now — never settled, or fallen?</summary>
    public bool IsWild(Title county) => _wilds is null ? !_sim.Owner.ContainsKey(county) : _wild.Contains(county);

    /// <summary>Is this county a ruin now: fallen during the history, or a ruin from the start and not resettled?</summary>
    public bool IsRuin(Title county)
        => _fellIn.ContainsKey(county) || (_wild.Contains(county) && _wilds?.Wilderness.IsRuin(county) == true);

    /// <summary>The year a county was settled during the history, or null.</summary>
    public int? SettledIn(Title county) => _settledIn.TryGetValue(county, out int year) ? year : null;

    /// <summary>The year a county fell to ruin during the history, or null.</summary>
    public int? FellIn(Title county) => _fellIn.TryGetValue(county, out int year) ? year : null;

    /// <summary>The culture a county's people have in the simulation: for settled land, its settlers'.</summary>
    public Culture? SettlerCulture(Title county) => _sim.CountyCulture.GetValueOrDefault(county);

    /// <summary>
    /// Wild in the generated world and held now — by this history or one applied before it.
    /// Carried by <see cref="AppliedHistory"/>.
    /// </summary>
    public IEnumerable<Title> Settled
        => _wilds is null ? [] : _wilds.Generated.Counties.Where(c => !_wild.Contains(c)).OrderBy(c => c.Index);

    /// <summary>
    /// Ruins now that were not ruins in the generated world — fallen in this history or one
    /// applied before it. Carried by <see cref="AppliedHistory"/>.
    /// </summary>
    public IEnumerable<Title> Fallen
        => _wilds is null ? [] : _wild.Where(c => IsRuin(c) && !_wilds.Generated.IsRuin(c)).OrderBy(c => c.Index);

    private void SeatWilds(WildsGround? wilds)
    {
        _wilds = wilds;
        if (wilds is null) return;
        foreach (var county in wilds.Wilderness.Counties)
        {
            _wild.Add(county);
            _startWild.Add(county);
        }
    }

    /// <summary>The frontier's year: settling first, then falling, each county at most once.</summary>
    private void WildsYear()
    {
        if (_wilds is null) return;

        // Its own stream, so switching the frontier moves no realm's dice and no ruler's.
        var rng = new Rng(_seed ^ 0x3D11 ^ unchecked((int)((uint)_sim.Year * 0x9E3779B1u)));
        var touched = new HashSet<Title>();

        Settle(rng, touched);
        Fall(rng, touched);
    }

    private void Settle(Rng rng, HashSet<Title> touched)
    {
        bool on = _settings.Rules.HasFlag(RealmRules.Colonisation) && _settings.Colonisation > 0;
        double chance = Math.Min(1.0, SettleChance * _settings.Colonisation);

        // The wilderness realm is named for one unsettled county, and the Wilds for the rest: the
        // last of it is never settled, so the titles the runtime systems name keep something to hold.
        int unsettled = _wild.Count(c => !IsRuin(c));

        foreach (var county in _wild.OrderBy(c => c.Index).ToList())
        {
            // Settled from a realm on its border: the neighbour holding most of that border.
            var colonist = Neighbours(county)
                .Where(n => _sim.Owner.ContainsKey(n))
                .GroupBy(n => _sim.Owner[n])
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key.Capital.Index)
                .Select(g => g.Key)
                .FirstOrDefault();
            if (colonist is null) continue;

            // Rolled whether or not the rule is on, so switching it changes nothing else's dice.
            bool roll = rng.Chance(chance);
            if (!on || !roll) continue;

            bool ruin = IsRuin(county);
            if (!ruin && unsettled <= 1) continue;

            _wild.Remove(county);
            if (!ruin) unsettled--;
            if (_startWild.Contains(county)) _settledIn[county] = _sim.Year;
            _fellIn.Remove(county);
            touched.Add(county);

            colonist.Counties.Add(county);
            _sim.Owner[county] = colonist;
            _sim.CountyCulture[county] = colonist.Culture;
            var near = _sim.Adjacent.TryGetValue(county, out var had) ? had : _sim.Adjacent[county] = [];
            foreach (var n in Neighbours(county))
            {
                if (!_sim.Owner.ContainsKey(n)) continue;
                near.Add(n);
                if (_sim.Adjacent.TryGetValue(n, out var back)) back.Add(county);
                else _sim.Adjacent[n] = [county];
            }

            _sim.Log(FormationKind.Colonised, county, colonist, null, 0,
                ruin ? $"The realm of {colonist.Capital.Name} resettled the ruins of {county.Name}"
                     : $"The realm of {colonist.Capital.Name} settled {county.Name}");
        }
    }

    private void Fall(Rng rng, HashSet<Title> touched)
    {
        if (!_wilds!.CanRuin) return;
        bool on = _settings.Rules.HasFlag(RealmRules.Ruination) && _settings.Ruination > 0;

        foreach (var county in _sim.Owner.Keys.OrderBy(c => c.Index).ToList())
        {
            // Anywhere, as the runtime ruins fall: a lone ruin inside a kingdom is the point, and
            // what decides it is how badly the realm holds together, not where the county lies.
            if (touched.Contains(county)) continue;

            var realm = _sim.Owner[county];
            double instability = 1.0 - Formation.Cohesion(_sim, realm);
            bool roll = rng.Chance(Math.Min(1.0, FallChance * _settings.Ruination * instability * instability));
            if (!on || !roll) continue;

            // The same vetoes the runtime ruins keep: a seat, a de jure capital, a realm's last
            // few counties, and anything that would cut a realm in two.
            if (county == realm.Capital || county.Parent?.Capital == county) continue;
            if (realm.Counties.Count < FallMinimumRealm || !StaysWhole(realm, county)) continue;

            realm.Counties.Remove(county);
            _sim.Owner.Remove(county);
            if (_sim.Adjacent.Remove(county, out var near))
                foreach (var n in near)
                    if (_sim.Adjacent.TryGetValue(n, out var back)) back.Remove(county);

            _wild.Add(county);
            _settledIn.Remove(county);
            _fellIn[county] = _sim.Year;
            touched.Add(county);

            _sim.Log(FormationKind.Ruined, county, realm, null, 0,
                $"{county.Name} was abandoned by the realm of {realm.Capital.Name} and fell to ruin");
        }
    }

    private IEnumerable<Title> Neighbours(Title county)
        => _wilds!.Adjacency.TryGetValue(county, out var near) ? near : [];

    /// <summary>Whether a realm stays in one piece without <paramref name="lost"/>.</summary>
    private bool StaysWhole(Polity realm, Title lost)
    {
        var seen = new HashSet<Title> { realm.Capital };
        var queue = new Queue<Title>();
        queue.Enqueue(realm.Capital);
        while (queue.Count > 0)
        {
            if (!_sim.Adjacent.TryGetValue(queue.Dequeue(), out var near)) continue;
            foreach (var n in near)
                if (n != lost && realm.Counties.Contains(n) && seen.Add(n)) queue.Enqueue(n);
        }
        return seen.Count == realm.Counties.Count - 1;
    }

    private void CheckWilds(List<string> problems)
    {
        if (_wilds is null) return;
        foreach (var county in _wild)
        {
            if (_sim.Owner.ContainsKey(county)) problems.Add($"{county.Name}: wild and held");
            if (_sim.Adjacent.ContainsKey(county)) problems.Add($"{county.Name}: wild but on the realm simulation's ground");
        }
        foreach (var county in _settledIn.Keys)
            if (!_sim.Owner.ContainsKey(county)) problems.Add($"{county.Name}: settled but held by nobody");
    }
}
