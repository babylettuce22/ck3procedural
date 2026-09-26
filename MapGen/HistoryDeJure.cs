namespace Ck3MapGen.MapGen;

/// <summary>
/// De jure drift, as CK3 runs it: a duchy held for a century by a realm based in another de jure
/// kingdom becomes part of that kingdom, and a kingdom held so by a realm of another empire becomes
/// part of that empire.
///
/// Kept as data beside the tree, never written into it: the <see cref="Title"/> objects are the
/// written world's, which the World workspace and its editor go on reading while history runs. The
/// tree changes only when a history is applied — see <see cref="AppliedHistory.ApplyDeJure"/>.
///
/// Reads the realm map and changes nothing on it, so it moves no realm's dice whether on or off.
/// </summary>
public sealed partial class HistorySim
{
    /// <summary>Each duchy's kingdom and each kingdom's empire, as drift has left them.</summary>
    private readonly Dictionary<Title, Title> _deJure = [];

    /// <summary>How many de jure children each kingdom and empire has, wilderness ones included.</summary>
    private readonly Dictionary<Title, int> _children = [];

    /// <summary>
    /// A title under way to drifting: toward which parent, how many years of progress it has, and
    /// the year the progress began.
    /// </summary>
    private readonly Dictionary<Title, (Title Toward, double Progress, int Since)> _driftClock = [];

    private List<Title> _driftDuchies = [];
    private List<Title> _driftKingdoms = [];

    /// <summary>
    /// The progress a title needs to drift, in years: vanilla's DRIFT_PROGRESS_LIMIT of 1200 months,
    /// over the pace dial. Progress grows a year a year while the title is held toward the same
    /// parent and decays at half that while it is not — DRIFT_MONTHLY_PROGRESS_INCREASE 1 and
    /// _DECREASE 0.5 — so a title lost for a few years does not start from nothing.
    /// </summary>
    private const double DriftYears = 100;

    /// <summary>The share of a title's counties one realm has to hold for the title to drift toward it.</summary>
    private const double DriftHold = 0.8;

    /// <summary>A de jure parent as drift has left it: a duchy's kingdom, a kingdom's empire.</summary>
    public Title? DeJureParent(Title title) => _deJure.GetValueOrDefault(title) ?? title.Parent;

    /// <summary>A county's de jure title of the given tier — "d", "k" or "e" — as drift has left it.</summary>
    public Title? DeJureOf(Title county, string tier)
    {
        var title = county.Parent;
        while (title is not null && title.Tier != tier) title = DeJureParent(title);
        return title;
    }

    /// <summary>Every duchy and kingdom the drift tracks, with the parent it has now — moved or not.</summary>
    public IReadOnlyDictionary<Title, Title> DeJureMap() => _deJure;

    /// <summary>Every duchy and kingdom whose parent drift has changed, with the parent it has now.</summary>
    public IEnumerable<(Title Title, Title Parent)> Drifted
        => _deJure.Where(kv => kv.Value != kv.Key.Parent).Select(kv => (kv.Key, kv.Value));

    /// <summary>Takes the tree as the written world has it, for the counties the simulation runs over.</summary>
    private void SeatDeJure()
    {
        var duchies = new HashSet<Title>();
        foreach (var county in _sim.Owner.Keys)
            if (county.Parent is { Tier: "d" } duchy) duchies.Add(duchy);

        _driftDuchies = [.. duchies.Where(d => d.Parent is { Tier: "k" }).OrderBy(d => d.Index)];
        _driftKingdoms = [.. _driftDuchies.Select(d => d.Parent!).Distinct()
                              .Where(k => k.Parent is { Tier: "e" }).OrderBy(k => k.Index)];

        foreach (var d in _driftDuchies) _deJure[d] = d.Parent!;
        foreach (var k in _driftKingdoms) _deJure[k] = k.Parent!;

        foreach (var parent in _driftDuchies.Select(d => d.Parent!).Concat(_driftKingdoms.Select(k => k.Parent!)).Distinct())
            _children[parent] = parent.Children.Count;
    }

    /// <summary>The year's drift: duchies toward their holders' kingdoms, then kingdoms toward their empires.</summary>
    private void DriftYear()
    {
        if (!_settings.Rules.HasFlag(RealmRules.DeJureDrift) || _settings.DriftPace <= 0) return;
        int need = Math.Max(1, (int)Math.Round(DriftYears / _settings.DriftPace));

        // Every county's independent realm, and the de jure kingdom and empire each realm covers most
        // of — its home, which the titles it holds elsewhere drift toward.
        var root = new Dictionary<Title, Polity>();
        foreach (var (county, owner) in _sim.Owner) root[county] = owner.Root;

        Dictionary<Polity, Title> Home(string tier)
        {
            var counts = new Dictionary<Polity, Dictionary<Title, int>>();
            foreach (var (county, realm) in root)
            {
                if (DeJureOf(county, tier) is not { } title) continue;
                if (!counts.TryGetValue(realm, out var tally)) counts[realm] = tally = [];
                tally[title] = tally.GetValueOrDefault(title) + 1;
            }
            return counts.ToDictionary(kv => kv.Key,
                kv => kv.Value.OrderByDescending(t => t.Value).ThenBy(t => t.Key.Index).First().Key);
        }

        Drift(_driftDuchies, "k", Home("k"), need, root);
        Drift(_driftKingdoms, "e", Home("e"), need, root);
    }

    private void Drift(List<Title> titles, string parentTier, Dictionary<Polity, Title> home, int need,
        Dictionary<Title, Polity> root)
    {
        // Each title's simulated counties, through the tree as it stands this year.
        var countiesOf = new Dictionary<Title, List<Title>>();
        foreach (var county in root.Keys)
        {
            var title = DeJureOf(county, parentTier == "k" ? "d" : "k");
            if (title is null) continue;
            if (!countiesOf.TryGetValue(title, out var list)) countiesOf[title] = list = [];
            list.Add(county);
        }

        foreach (var title in titles)
        {
            if (!countiesOf.TryGetValue(title, out var counties) || counties.Count == 0)
            {
                Decay(title);
                continue;
            }

            var (holder, held) = counties.GroupBy(c => root[c])
                .Select(g => (Realm: g.Key, Count: g.Count()))
                .OrderByDescending(g => g.Count).ThenBy(g => g.Realm.Capital.Index)
                .First();

            var current = _deJure[title];
            if (held < DriftHold * counties.Count || !home.TryGetValue(holder, out var toward)
                || toward == current || toward.Tier != parentTier)
            {
                Decay(title);
                continue;
            }

            // Progress is toward one parent; held toward another, it starts over.
            (Title Toward, double Progress, int Since) clock = _driftClock.TryGetValue(title, out var running) && running.Toward == toward
                ? running with { Progress = running.Progress + 1 }
                : (toward, 1.0, _sim.Year);
            _driftClock[title] = clock;

            // A kingdom or empire keeps its last child: a de jure title with nothing under it would
            // have no capital to write.
            if (clock.Progress < need || _children.GetValueOrDefault(current) <= 1) continue;

            _deJure[title] = toward;
            _children[current] = _children.GetValueOrDefault(current) - 1;
            _children[toward] = _children.GetValueOrDefault(toward, toward.Children.Count) + 1;
            _driftClock.Remove(title);

            string kind = title.Tier == "d" ? "duchy" : "kingdom";
            string into = toward.Tier == "k" ? "kingdom" : "empire";
            _sim.Log(FormationKind.Drifted, title.Capital ?? counties[0], null, null, 0,
                $"The {kind} of {title.Name} became de jure part of the {into} of {toward.Name}, "
                + $"after {_sim.Year - clock.Since} years under its rule");
        }
    }

    /// <summary>A year the title was not held toward anything: its progress falls at half the rate it grows.</summary>
    private void Decay(Title title)
    {
        if (!_driftClock.TryGetValue(title, out var clock)) return;
        if (clock.Progress <= 0.5) _driftClock.Remove(title);
        else _driftClock[title] = clock with { Progress = clock.Progress - 0.5 };
    }

    private void CheckDeJure(List<string> problems)
    {
        foreach (var (title, parent) in _deJure)
        {
            string want = title.Tier == "d" ? "k" : "e";
            if (parent.Tier != want) problems.Add($"{title.Key}: drifted under a {parent.Tier}, not a {want}");
        }
        foreach (var (parent, count) in _children)
            if (count < 1) problems.Add($"{parent.Key}: drifted down to {count} de jure children");
    }
}
