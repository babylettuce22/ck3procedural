using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen;

/// <summary>
/// What the peoples' history needs that the realm simulation does not have: every county's faith,
/// the cultures and faiths the world was generated with — the measure no people or faith may
/// shrink or grow too far from, however many histories are run on from one another — and each
/// culture's race, where the world has races.
/// </summary>
/// <param name="Faiths">The faiths of the written world, which an applied history may already have moved.</param>
/// <param name="GeneratedCultures">The county cultures the world was generated with.</param>
/// <param name="GeneratedFaiths">The county faiths the world was generated with.</param>
/// <param name="RaceOf">Each culture's race, or null on a world of one race.</param>
public sealed record PeopleGround(FaithMap Faiths, CultureMap GeneratedCultures, FaithMap GeneratedFaiths,
    Func<Culture, RaceArchetype>? RaceOf = null);

/// <summary>A county's people or faith changing under its lords. Either may be null where only the other changed.</summary>
public sealed record SimConversion(Title County, int Year, Culture? Culture, Faith? Faith);

/// <summary>
/// The peoples: counties slowly taking the culture and faith of the lords who hold them.
///
/// The target is always the county's own realm — the lord who holds it, not the emperor above him —
/// whose people and faith are its capital's. So an empire of sworn kingdoms of many peoples stays
/// many peoples, and a realm's own seat never changes. Change moves as a front: a county
/// surrounded by its lords' people turns many times faster than one on the far side of a foreign
/// province, it waits a generation after a conquest before it starts, and a realm coming apart
/// assimilates nothing.
///
/// Held back on purpose from the ratchet a world-conquering people would be: nobody takes the
/// culture of another race; no culture or faith may lose more than half the land it was generated
/// with, slowing as it nears that; none may grow past twice its land (or six counties more, for a
/// small one) nor, by conversion, past a third of the settled world. What is left over for a
/// later stage is new peoples and faiths — divergence, hybrids, heresy.
///
/// Feeds back through the realm simulation's cohesion (<see cref="Formation.Cohesion"/>), which
/// measures a realm's counties against its people: a realm that assimilates its conquests holds
/// together better. Faith is not read by the realm simulation.
/// </summary>
public sealed partial class HistorySim
{
    /// <summary>
    /// The chance a year that a county takes its lords' culture, before the dial, when it has been
    /// held a century, every neighbour already has, and the realm is wholly cohesive: about two
    /// and a half centuries on average. Vanilla's culture conversion is a count's long project.
    /// </summary>
    private const double AssimilateChance = 0.004;

    /// <summary>The same for faith: about a century and a half. A faith spreads faster than a tongue.</summary>
    private const double ConvertChance = 0.006;

    /// <summary>Held this many years, a county begins to change; by <see cref="RootedAt"/> it changes at the full rate.</summary>
    private const int RootedFrom = 20, RootedAt = 100;

    /// <summary>
    /// How long the start date's holdings count as held already. The formation grew them over
    /// centuries, but the start date is where the story is told from, so none is fully rooted yet.
    /// </summary>
    private const int HeldBeforeStart = 50;

    /// <summary>A county with none of its lords' people or faith beside it still changes, at this share of the rate.</summary>
    private const double NoContact = 0.15;

    /// <summary>Kin — a sister culture of one heritage, a sister faith of one religion — are taken up this much sooner.</summary>
    private const double Kin = 1.5;

    /// <summary>A county holy to the faith it keeps holds to it this much longer.</summary>
    private const double HolyGround = 0.25;

    /// <summary>No culture or faith falls below this share of the land it was generated with.</summary>
    private const double LossFloor = 0.5;

    /// <summary>
    /// None grows past this many times its generated land, or <see cref="GrowthFloor"/> counties
    /// more for a small one, or — unless it started there — <see cref="WorldShare"/> of the
    /// settled world.
    /// </summary>
    private const double GrowthCap = 2.0, WorldShare = 0.35;
    private const int GrowthFloor = 6;

    private PeopleGround? _peoples;

    /// <summary>Every held county's faith. Settled counties take their settlers'.</summary>
    private readonly Dictionary<Title, Faith> _faith = [];

    /// <summary>Who holds each county, and since when — what a county's rooting is measured from.</summary>
    private readonly Dictionary<Title, (int Realm, int Since)> _heldSince = [];

    /// <summary>How much land each culture and faith was generated with. See <see cref="PeopleGround"/>.</summary>
    private readonly Dictionary<Culture, int> _cultureAtFirst = [];
    private readonly Dictionary<Faith, int> _faithAtFirst = [];

    /// <summary>The faiths each county is holy to.</summary>
    private readonly Dictionary<Title, HashSet<Faith>> _holyTo = [];

    /// <summary>Each duchy's majority people and faith as last seen, for telling when one turns.</summary>
    private readonly Dictionary<Title, Culture?> _duchyCulture = [];
    private readonly Dictionary<Title, Faith?> _duchyFaith = [];

    private readonly List<SimConversion> _changes = [];

    /// <summary>Counties settled or lost to ruin during the history, by index, whose people that decided.</summary>
    private readonly HashSet<int> _repeopled = [];

    /// <summary>
    /// Whether a county was settled or lost to ruin during this history — its people are then its
    /// settlers' or the wild's, and an earlier history's changes to it no longer hold.
    /// </summary>
    internal bool Resettled(int county) => _repeopled.Contains(county);

    /// <summary>Whether this history has peoples to move — a world written with its faiths at hand.</summary>
    public bool HasPeoples => _peoples is not null;

    /// <summary>A county's culture now, or null for wild land.</summary>
    public Culture? CultureOf(Title county) => _sim.Owner.ContainsKey(county) ? _sim.CountyCulture.GetValueOrDefault(county) : null;

    /// <summary>A county's faith now, or null for wild land or a history without peoples.</summary>
    public Faith? FaithOf(Title county) => _sim.Owner.ContainsKey(county) ? _faith.GetValueOrDefault(county) : null;

    /// <summary>
    /// Every county's people or faith changed during the history, oldest first, for the counties
    /// still held — a county settled or fallen since was given its people by that instead. Carried
    /// by <see cref="AppliedHistory"/>.
    /// </summary>
    public IReadOnlyList<SimConversion> PeopleChanges => _changes;

    /// <summary>When each held county last changed hands, with its holder's id. Carried by <see cref="AppliedHistory"/>.</summary>
    internal IReadOnlyDictionary<Title, (int Realm, int Since)> HeldSince => _heldSince;

    /// <param name="earlier">The history the written world was applied from, if any: a county it saw
    /// change hands is rooted from then, not counted as long held.</param>
    private void SeatPeoples(PeopleGround? peoples, AppliedHistory? earlier = null)
    {
        _peoples = peoples;
        if (peoples is null) return;

        foreach (var county in _sim.Owner.Keys)
        {
            _faith[county] = peoples.Faiths.For(county);
            int holder = _sim.Owner[county].Id;
            _heldSince[county] = (holder,
                earlier is not null && earlier.Holdings.TryGetValue(county.Index, out var held) && held.Realm == holder
                    ? held.Since : StartYear - HeldBeforeStart);
        }

        foreach (var (county, culture) in peoples.GeneratedCultures.ByCounty)
            if (county.Tier == "c" && culture.Key != Cultures.UnsettledKey)
                _cultureAtFirst[culture] = _cultureAtFirst.GetValueOrDefault(culture) + 1;
        foreach (var (county, faith) in peoples.GeneratedFaiths.ByCounty)
            if (county.Tier == "c" && faith.Key != Faiths.UnsettledFaithKey)
                _faithAtFirst[faith] = _faithAtFirst.GetValueOrDefault(faith) + 1;

        foreach (var faith in peoples.Faiths.Faiths)
            foreach (var (_, county) in faith.HolySites)
                (_holyTo.TryGetValue(county, out var set) ? set : _holyTo[county] = []).Add(faith);

        foreach (var duchy in _sim.Owner.Keys.Select(c => c.Parent).OfType<Title>().Where(d => d.Tier == "d").Distinct())
        {
            _duchyCulture[duchy] = Majority(duchy, CultureOf);
            _duchyFaith[duchy] = Majority(duchy, FaithOf);
        }
    }

    /// <summary>A settled county takes its settlers' faith, and whatever it was before is forgotten.</summary>
    private void PeoplesSettled(Title county, Polity colonist)
    {
        _repeopled.Add(county.Index);
        if (_peoples is null) return;
        _faith[county] = _faith.GetValueOrDefault(colonist.Capital) ?? _peoples.Faiths.For(colonist.Capital);
        _heldSince[county] = (colonist.Id, _sim.Year);
        _changes.RemoveAll(c => c.County == county);
    }

    /// <summary>A fallen county's people are the unsettled ones, which no change carries.</summary>
    private void PeoplesFell(Title county)
    {
        _repeopled.Add(county.Index);
        if (_peoples is null) return;
        _heldSince.Remove(county);
        _changes.RemoveAll(c => c.County == county);
    }

    /// <summary>
    /// The peoples' year: every held county that is not of its lords' culture or faith may take it.
    /// Culture and faith each on a stream of their own, rolled whether or not their rule is on, so
    /// switching either moves no other dice.
    /// </summary>
    private void PeoplesYear()
    {
        if (_peoples is null) return;

        uint year = unchecked((uint)_sim.Year * 0x9E3779B1u);
        var cultureRng = new Rng(_seed ^ 0x5A11 ^ unchecked((int)year));
        var faithRng = new Rng(_seed ^ 0x7E3B ^ unchecked((int)year));
        bool assimilate = _settings.Rules.HasFlag(RealmRules.Assimilation) && _settings.Assimilation > 0;
        bool convert = _settings.Rules.HasFlag(RealmRules.Conversion) && _settings.Conversion > 0;

        // How much land each holds now, kept up to date as the year's changes land.
        var cultureNow = new Dictionary<Culture, int>();
        var faithNow = new Dictionary<Faith, int>();
        foreach (var county in _sim.Owner.Keys)
        {
            if (_sim.CountyCulture.TryGetValue(county, out var c)) cultureNow[c] = cultureNow.GetValueOrDefault(c) + 1;
            if (_faith.TryGetValue(county, out var f)) faithNow[f] = faithNow.GetValueOrDefault(f) + 1;
        }
        int held = _sim.Owner.Count;

        var cohesion = new Dictionary<Polity, double>();
        var touched = new Dictionary<Title, Polity>();
        int changesBefore = _changes.Count;

        foreach (var county in _sim.Owner.Keys.OrderBy(c => c.Index).ToList())
        {
            var realm = _sim.Owner[county];

            // Rooting restarts whenever the county changes hands.
            if (!_heldSince.TryGetValue(county, out var since) || since.Realm != realm.Id)
                _heldSince[county] = since = (realm.Id, _sim.Year);
            if (county == realm.Capital) continue;

            double rooted = Math.Clamp((_sim.Year - since.Since - RootedFrom) / (double)(RootedAt - RootedFrom), 0, 1);
            if (!cohesion.TryGetValue(realm, out double holds)) cohesion[realm] = holds = Formation.Cohesion(_sim, realm);
            double pace = rooted * holds;

            // The people.
            if (_sim.CountyCulture.TryGetValue(county, out var people) && people != realm.Culture
                && people.Key != Cultures.UnsettledKey && realm.Culture.Key != Cultures.UnsettledKey
                && (_peoples.RaceOf is null || _peoples.RaceOf(people) == _peoples.RaceOf(realm.Culture)))
            {
                double p = AssimilateChance * _settings.Assimilation * pace
                           * Contact(county, n => _sim.CountyCulture.GetValueOrDefault(n) == realm.Culture)
                           * (people.Heritage == realm.Culture.Heritage ? Kin : 1)
                           * Losing(cultureNow.GetValueOrDefault(people), _cultureAtFirst.GetValueOrDefault(people))
                           * Gaining(cultureNow.GetValueOrDefault(realm.Culture), _cultureAtFirst.GetValueOrDefault(realm.Culture), held);
                bool roll = cultureRng.NextDouble() < p;
                if (assimilate && roll)
                {
                    _sim.CountyCulture[county] = realm.Culture;
                    cultureNow[people]--;
                    cultureNow[realm.Culture] = cultureNow.GetValueOrDefault(realm.Culture) + 1;
                    _changes.Add(new SimConversion(county, _sim.Year, realm.Culture, null));
                    if (county.Parent is { Tier: "d" } duchy) touched[duchy] = realm;
                }
            }

            // The faith: the realm's is its seat's.
            if (_faith.TryGetValue(county, out var faith) && _faith.TryGetValue(realm.Capital, out var lords)
                && faith != lords && faith.Key != Faiths.UnsettledFaithKey && lords.Key != Faiths.UnsettledFaithKey)
            {
                double p = ConvertChance * _settings.Conversion * pace
                           * Contact(county, n => _faith.GetValueOrDefault(n) == lords)
                           * (faith.Religion == lords.Religion ? Kin : 1)
                           * (_holyTo.TryGetValue(county, out var holy) && holy.Contains(faith) ? HolyGround : 1)
                           * Losing(faithNow.GetValueOrDefault(faith), _faithAtFirst.GetValueOrDefault(faith))
                           * Gaining(faithNow.GetValueOrDefault(lords), _faithAtFirst.GetValueOrDefault(lords), held);
                bool roll = faithRng.NextDouble() < p;
                if (convert && roll)
                {
                    _faith[county] = lords;
                    faithNow[faith]--;
                    faithNow[lords] = faithNow.GetValueOrDefault(lords) + 1;
                    _changes.Add(new SimConversion(county, _sim.Year, null, lords));
                    if (county.Parent is { Tier: "d" } duchy) touched[duchy] = realm;
                }
            }
        }

        // A duchy whose people or faith has now mostly turned to its lords' is told as the duchy's
        // news; every other county that changed, as its own.
        var turnedCulture = new HashSet<Title>();
        var turnedFaith = new HashSet<Title>();
        foreach (var (duchy, realm) in touched.OrderBy(kv => kv.Key.Index))
        {
            var culture = Majority(duchy, CultureOf);
            if (culture == realm.Culture && _duchyCulture.GetValueOrDefault(duchy) != culture) turnedCulture.Add(duchy);
            _duchyCulture[duchy] = culture;

            var faith = Majority(duchy, FaithOf);
            if (faith is not null && faith == _faith.GetValueOrDefault(realm.Capital) && _duchyFaith.GetValueOrDefault(duchy) != faith)
                turnedFaith.Add(duchy);
            _duchyFaith[duchy] = faith;
        }

        for (int i = changesBefore; i < _changes.Count; i++)
        {
            var (county, _, culture, faith) = _changes[i];
            var realm = _sim.Owner[county];
            if (culture is not null && !(county.Parent is { } d && turnedCulture.Contains(d)))
            {
                _sim.Log(FormationKind.Assimilated, county, realm, null, 0,
                    $"The people of {county.Name} took up {culture.Name} ways under the realm of {realm.Capital.Name}");
                Remember("assimilated", county, realm, other: culture.Key);
            }
            if (faith is not null && !(county.Parent is { } f && turnedFaith.Contains(f)))
            {
                _sim.Log(FormationKind.Converted, county, realm, null, 0,
                    $"{county.Name} turned to {faith.Name} under the realm of {realm.Capital.Name}");
                Remember("converted", county, realm, other: faith.Key);
            }
        }

        foreach (var duchy in turnedCulture)
        {
            var realm = touched[duchy];
            _sim.Log(FormationKind.Assimilated, duchy.Capital ?? duchy, realm, null, 1,
                $"Most of the duchy of {duchy.Name} took up {realm.Culture.Name} ways under the realm of {realm.Capital.Name}");
            Remember("assimilated", duchy, realm, other: realm.Culture.Key);
        }
        foreach (var duchy in turnedFaith)
        {
            var realm = touched[duchy];
            var faith = _faith[realm.Capital];
            _sim.Log(FormationKind.Converted, duchy.Capital ?? duchy, realm, null, 1,
                $"Most of the duchy of {duchy.Name} turned to {faith.Name} under the realm of {realm.Capital.Name}");
            Remember("converted", duchy, realm, other: faith.Key);
        }
    }

    /// <summary>
    /// How much the county's neighbours already share what it would take: <see cref="NoContact"/>
    /// with none of them, the full rate with all.
    /// </summary>
    private double Contact(Title county, Func<Title, bool> shares)
    {
        if (!_sim.Adjacent.TryGetValue(county, out var near) || near.Count == 0) return NoContact;
        int alike = 0;
        foreach (var n in near) if (shares(n)) alike++;
        return NoContact + (1 - NoContact) * alike / near.Count;
    }

    /// <summary>
    /// How freely a culture or faith with <paramref name="now"/> counties may lose one: fully at the
    /// land it was generated with or more, not at all at half of it — and never its last.
    /// </summary>
    private static double Losing(int now, int atFirst)
    {
        int floor = Math.Max(1, (int)Math.Ceiling(atFirst * LossFloor));
        if (now <= floor) return 0;
        return atFirst <= floor ? 1 : Math.Clamp((now - floor) / (double)(atFirst - floor), 0, 1);
    }

    /// <summary>
    /// How freely a culture or faith with <paramref name="now"/> counties may take one more: fully
    /// up to the land it was generated with, then less and less up to its cap. See <see cref="GrowthCap"/>.
    /// </summary>
    private static double Gaining(int now, int atFirst, int held)
    {
        if (now < atFirst) return 1;
        int cap = Math.Min(Math.Max((int)(atFirst * GrowthCap), atFirst + GrowthFloor),
                           Math.Max(atFirst, (int)(held * WorldShare)));
        if (cap <= atFirst) return 0;
        return Math.Clamp((cap - now) / (double)(cap - atFirst), 0, 1);
    }

    /// <summary>What more than half a duchy's held counties share, or null when nothing does.</summary>
    private T? Majority<T>(Title duchy, Func<Title, T?> of) where T : class
    {
        var counts = new Dictionary<T, int>();
        int held = 0;
        foreach (var county in duchy.Children)
        {
            if (county.Tier != "c" || of(county) is not { } x) continue;
            held++;
            counts[x] = counts.GetValueOrDefault(x) + 1;
        }
        foreach (var (x, n) in counts) if (n * 2 > held) return x;
        return null;
    }

    private void CheckPeoples(List<string> problems)
    {
        if (_peoples is null) return;
        foreach (var county in _sim.Owner.Keys)
            if (!_faith.ContainsKey(county)) problems.Add($"{county.Name}: held but has no faith");
        foreach (var change in _changes)
            if (!_sim.Owner.ContainsKey(change.County)) problems.Add($"{change.County.Name}: changed its people but is held by nobody");
    }
}
