using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen;

/// <summary>A political agreement between the current rulers, not an invented marriage.</summary>
public sealed record SimAlliance(Polity A, Polity B, int Since, int Until,
    int CrownedA, int CrownedB, string HouseA, string HouseB, string Reason);

public sealed partial class HistorySim
{
    private const int MaxAllies = 3, TreatyYears = 12, OfferCooldown = 5;
    private readonly Dictionary<(int, int), SimAlliance> _alliances = [];
    private readonly Dictionary<(int, int), int> _allianceOffers = [];
    private readonly Dictionary<Title, string> _diplomaticFaith = [];
    private Dictionary<Polity, HashSet<Polity>>? _militaryNeighbours;

    public IEnumerable<SimAlliance> Alliances => _alliances.OrderBy(kv => kv.Key).Select(kv => kv.Value);
    internal IEnumerable<(int A, int B, int Until)> AllianceOffers
        => _allianceOffers.OrderBy(kv => kv.Key).Select(kv => (kv.Key.Item1, kv.Key.Item2, kv.Value));
    public IEnumerable<Polity> AlliesOf(Polity p)
        => Alliances.Where(a => a.Until > Year && (a.A == p || a.B == p)).Select(a => a.A == p ? a.B : a.A);

    private bool UsesAlliances => _settings.Rules.HasFlag(RealmRules.Alliances);
    private static bool Sovereign(Polity p) => p.Alive && p.Suzerain is null;
    private bool TreatyRuler(Polity p)
        => Sovereign(p) && RulerOf(p) is { Died: null } ruler && Year - ruler.Born >= 16;
    private string? DiplomaticFaith(Polity p)
        => _faith.GetValueOrDefault(p.Capital)?.Key ?? _diplomaticFaith.GetValueOrDefault(p.Capital);
    private bool SameFaith(Polity a, Polity b)
        => DiplomaticFaith(a) is { } faith && faith == DiplomaticFaith(b);

    private double GrudgeBetween(Polity a, Polity b)
    {
        if (RulerOf(a)?.House is not { } x || RulerOf(b)?.House is not { } y || x == y) return 0;
        return _grudges.GetValueOrDefault(x.Id < y.Id ? (x.Id, y.Id) : (y.Id, x.Id))?.Score ?? 0;
    }

    private bool Enemies(Polity a, Polity b)
        => _wars.Any(w => Opposed(w, a, b));
    private static bool Opposed(SimWar w, Polity a, Polity b)
        => (OnSide(w, a, true) && OnSide(w, b, false)) || (OnSide(w, a, false) && OnSide(w, b, true));
    private static bool OnSide(SimWar w, Polity p, bool attacking)
        => attacking ? w.Attacker == p || w.AttackingAllies.Contains(p)
                     : w.Defender == p || w.DefendingAllies.Contains(p);

    private bool CompatibleTreaty(Polity a, Polity b)
        => a != b && TreatyRuler(a) && TreatyRuler(b) && !Enemies(a, b)
           && GrudgeBetween(a, b) < RivalryAt
           // A bishop's agreement protects his church's interests. No fictitious marriages,
           // dynastic entitlement or automatic cross-faith religious coalition.
           && (!(IsTheocracy(GovernmentOf(a)) || IsTheocracy(GovernmentOf(b))) || SameFaith(a, b));

    private Dictionary<Polity, HashSet<Polity>> RealmNeighbours()
    {
        var neighbours = _sim.Polities.Where(Sovereign).ToDictionary(p => p, _ => new HashSet<Polity>());
        foreach (var (county, holder) in _sim.Owner)
        {
            if (!_sim.Adjacent.TryGetValue(county, out var near)) continue;
            var root = holder.Root;
            foreach (var n in near)
                if (_sim.Owner.TryGetValue(n, out var other) && other.Root != root)
                    neighbours[root].Add(other.Root);
        }
        return neighbours;
    }

    private bool Nearby(Polity a, Polity b, Dictionary<Polity, HashSet<Polity>> neighbours)
        => neighbours.TryGetValue(a, out var near) && (near.Contains(b)
            || near.Any(n => neighbours.TryGetValue(n, out var next) && next.Contains(b)));

    private (double Score, string Reason) TreatyInterest(Polity a, Polity b,
        Dictionary<Polity, HashSet<Polity>> neighbours)
    {
        double score = SameFaith(a, b) ? 0.10 : 0;
        if (a.Culture.Heritage == b.Culture.Heritage) score += 0.05;
        if (RulerOf(a)?.House == RulerOf(b)?.House) score += 0.15;
        score -= Math.Min(0.20, GrudgeBetween(a, b) / 100);
        string reason = "good relations";
        if (neighbours.TryGetValue(a, out var nearA) && neighbours.TryGetValue(b, out var nearB))
        {
            var threat = nearA.Intersect(nearB).Where(p => p != a && p != b)
                .OrderByDescending(p => Formation.Strength(_sim, p)).ThenBy(p => p.Id).FirstOrDefault();
            if (threat is not null)
            {
                double power = Formation.Strength(_sim, threat);
                if (power > 1.25 * Math.Min(Formation.Strength(_sim, a), Formation.Strength(_sim, b)))
                {
                    score += 0.35;
                    reason = $"mutual protection against {threat.Capital.Name}";
                }
                else if (GrudgeBetween(a, threat) >= QuarrelAt && GrudgeBetween(b, threat) >= QuarrelAt)
                {
                    score += 0.25;
                    reason = $"shared hostility toward {threat.Capital.Name}";
                }
            }
        }
        // A powerful neighbour need not accept an agreement that buys it almost nothing.
        double ratio = Formation.Strength(_sim, b) / Math.Max(1, Formation.Strength(_sim, a));
        if (ratio < 0.25) score -= 0.10;
        return (Math.Clamp(score, 0, 0.75), reason);
    }

    private void SeatAlliances(PrehistoryMap? prehistory, AppliedHistory? earlier)
    {
        var byId = _sim.Polities.ToDictionary(p => p.Id);
        if (earlier?.Alliances is { } carried)
        {
            foreach (var a in carried.OrderBy(a => a.A).ThenBy(a => a.B))
                if (a.Until > Year && byId.TryGetValue(a.A, out var x) && byId.TryGetValue(a.B, out var y)
                    && CompatibleTreaty(x, y) && AlliesOf(x).Count() < MaxAllies && AlliesOf(y).Count() < MaxAllies)
                    _alliances[Pair(x, y)] = new SimAlliance(x, y, a.Since, a.Until,
                        a.CrownedA, a.CrownedB, a.HouseA, a.HouseB, a.Reason);
            foreach (var offer in earlier.AllianceOffers)
                if (offer.Until > Year && byId.ContainsKey(offer.A) && byId.ContainsKey(offer.B))
                    _allianceOffers[(Math.Min(offer.A, offer.B), Math.Max(offer.A, offer.B))] = offer.Until;
            return; // An explicitly empty network stays empty.
        }
        if (prehistory is null) return;
        var bySeat = _sim.Polities.Where(TreatyRuler).ToDictionary(p => p.Capital);
        foreach (var (seat, links) in prehistory.Alliances.OrderBy(kv => kv.Key.Index))
        {
            if (!bySeat.TryGetValue(seat, out var a)) continue;
            foreach (var link in links.OrderBy(l => l.PartnerCounty.Index))
                if (bySeat.TryGetValue(link.PartnerCounty, out var b) && !_alliances.ContainsKey(Pair(a, b))
                    && CompatibleTreaty(a, b) && AlliesOf(a).Count() < MaxAllies && AlliesOf(b).Count() < MaxAllies)
                {
                    int since = int.Parse(link.FormationDate.Split('.')[0]);
                    Agree(a, b, Math.Min(Year, since), Year + TreatyYears, "existing agreement", log: false);
                }
        }
    }

    private void Agree(Polity a, Polity b, int since, int until, string reason, bool log = true)
    {
        if (a.Id > b.Id) (a, b) = (b, a);
        var x = RulerOf(a)!; var y = RulerOf(b)!;
        _alliances[Pair(a, b)] = new SimAlliance(a, b, since, until,
            x.Crowned, y.Crowned, x.House.Name, y.House.Name, reason);
        if (!log) return;
        _sim.Log(FormationKind.Allied, a.Capital, a, b, 0,
            $"{a.Capital.Name} and {b.Capital.Name} agreed an alliance for {reason}");
        Remember("allied", a.Capital, a, b);
    }

    private void BreakAlliance(SimAlliance a, string reason)
    {
        _alliances.Remove(Pair(a.A, a.B));
        _allianceOffers[Pair(a.A, a.B)] = Year + OfferCooldown;
        _sim.Log(FormationKind.AllianceEnded, a.A.Capital, a.A.Alive ? a.A : null, a.B.Alive ? a.B : null, 0,
            $"The alliance of {a.A.Capital.Name} and {a.B.Capital.Name} ended: {reason}");
        Remember("unallied", a.A.Capital, a.A, a.B);
    }

    private void AlliancesYear()
    {
        if (!UsesAlliances) return;
        var neighbours = RealmNeighbours();
        foreach (var a in Alliances.ToList())
        {
            if (!CompatibleTreaty(a.A, a.B)) { BreakAlliance(a, "the agreement no longer holds"); continue; }
            var x = RulerOf(a.A)!; var y = RulerOf(a.B)!;
            bool changed = x.Crowned != a.CrownedA || y.Crowned != a.CrownedB;
            if (!changed && a.Until > Year) continue;
            var rng = Rng.For(_seed ^ 0xA111 ^ Year, a.A.Id, a.B.Id);
            double interest = Math.Min(TreatyInterest(a.A, a.B, neighbours).Score,
                TreatyInterest(a.B, a.A, neighbours).Score);
            // A new cleric negotiates in his own right; his predecessor's family conveys no pact.
            bool continuity = !IsTheocracy(GovernmentOf(a.A)) && !IsTheocracy(GovernmentOf(a.B))
                && x.House.Name == a.HouseA && y.House.Name == a.HouseB;
            if (rng.Chance(Math.Clamp(0.25 + interest + (continuity ? 0.25 : 0), 0, 0.9)))
                Agree(a.A, a.B, changed ? Year : a.Since, Year + TreatyYears, a.Reason, log: false);
            else BreakAlliance(a, changed ? "the new ruler declined to renew" : "the rulers declined to renew");
        }
        foreach (var (key, until) in _allianceOffers.ToList())
            if (until <= Year) _allianceOffers.Remove(key);

        foreach (var a in neighbours.Keys.OrderBy(p => p.Id))
        {
            if (!TreatyRuler(a) || AlliesOf(a).Count() >= MaxAllies) continue;
            // At most the nearest twelve candidates; never a scan of all ruler pairs.
            var candidates = neighbours[a].Concat(neighbours[a].SelectMany(n => neighbours[n]))
                .Distinct().Where(b => b.Id > a.Id).OrderBy(b => b.Id).Take(12);
            foreach (var b in candidates)
            {
                var key = Pair(a, b);
                if (AlliesOf(a).Count() >= MaxAllies) break;
                if (_alliances.ContainsKey(key) || _allianceOffers.ContainsKey(key)
                    || AlliesOf(b).Count() >= MaxAllies || !CompatibleTreaty(a, b)) continue;
                var x = TreatyInterest(a, b, neighbours); var y = TreatyInterest(b, a, neighbours);
                if (Math.Min(x.Score, y.Score) <= 0) continue;
                var rng = Rng.For(_seed ^ 0xA112 ^ Year, a.Id, b.Id);
                if (!rng.Chance(1 - Math.Pow(0.88, _sim.TickYears))) continue;
                _allianceOffers[key] = Year + OfferCooldown;
                if (rng.Chance(x.Score) && rng.Chance(y.Score))
                    Agree(a, b, Year, Year + TreatyYears, x.Reason);
            }
        }
    }

    private void PruneDiplomacy()
    {
        _militaryNeighbours = null;
        foreach (var a in Alliances.ToList())
            if (!Sovereign(a.A) || !Sovereign(a.B) || Enemies(a.A, a.B))
                BreakAlliance(a, "a realm lost independence or became an enemy");
        foreach (var war in _wars)
        {
            war.AttackingAllies.RemoveWhere(p => !ValidParticipant(war, p, true));
            war.DefendingAllies.RemoveWhere(p => !ValidParticipant(war, p, false));
        }
    }

    private bool ValidParticipant(SimWar war, Polity p, bool attacking)
    {
        var own = attacking ? war.Attacker : war.Defender;
        var enemy = attacking ? war.Defender : war.Attacker;
        var friends = (attacking ? war.AttackingAllies : war.DefendingAllies).Append(own);
        var foes = (attacking ? war.DefendingAllies : war.AttackingAllies).Append(enemy);
        return Sovereign(p) && p != own && p != enemy
            && !foes.Contains(p)
            && !_wars.Any(w => w != war && (friends.Any(friend => Opposed(w, p, friend))
                || foes.Any(foe => (OnSide(w, p, true) && OnSide(w, foe, true))
                    || (OnSide(w, p, false) && OnSide(w, foe, false)))))
            && !(IsTheocracy(GovernmentOf(p)) && !SameFaith(p, own));
    }

    private double Participation(Polity ally, Polity leader)
    {
        var neighbours = _militaryNeighbours ??= RealmNeighbours();
        if (!Nearby(ally, leader, neighbours)) return 0;
        return neighbours[ally].Contains(leader) ? 0.6 : 0.35;
    }

    private int Commitments(Polity p)
        => Math.Max(1, _wars.Count(w => w.Attacker == p || w.Defender == p
            || UsesAlliances && (w.AttackingAllies.Contains(p) || w.DefendingAllies.Contains(p))));
    private double SideStrength(SimWar war, bool attacking)
    {
        var leader = attacking ? war.Attacker : war.Defender;
        double strength = Formation.Strength(_sim, leader);
        if (!UsesAlliances) return strength;
        strength /= Commitments(leader);
        foreach (var ally in (attacking ? war.AttackingAllies : war.DefendingAllies).OrderBy(p => p.Id))
            if (ValidParticipant(war, ally, attacking))
                strength += Formation.Strength(_sim, ally) * Participation(ally, leader) / Commitments(ally);
        return strength;
    }

    private double ExpectedStrength(Polity p, Polity enemy)
    {
        if (!UsesAlliances || !_settings.Rules.HasFlag(RealmRules.Wars)) return Formation.Strength(_sim, p);
        var leader = p.Root; var opponent = enemy.Root;
        if (_wars.FirstOrDefault(w => Opposed(w, leader, opponent)) is { } war)
            return SideStrength(war, OnSide(war, leader, true));
        double strength = Formation.Strength(_sim, leader) / Commitments(leader);
        if (!UsesAlliances) return strength;
        foreach (var ally in AlliesOf(leader))
            if (TreatyRuler(ally) && ally != opponent && !Enemies(ally, leader) && TruceUntil(ally, opponent) is null
                && !(IsTheocracy(GovernmentOf(ally)) && !SameFaith(ally, leader)))
                strength += Formation.Strength(_sim, ally) * Participation(ally, leader) * 0.65 / Commitments(ally);
        return strength;
    }

    private void CallAllies(SimWar war)
    {
        if (!UsesAlliances) return;
        // Defensive calls go first. A ruler allied to both leaders will not join either side.
        foreach (bool attacking in new[] { false, true })
        {
            var leader = attacking ? war.Attacker : war.Defender;
            var enemy = attacking ? war.Defender : war.Attacker;
            foreach (var ally in AlliesOf(leader).OrderBy(p => p.Id).ToList())
            {
                if (!ValidParticipant(war, ally, attacking) || AlliesOf(ally).Contains(enemy)
                    || TruceUntil(ally, enemy) is not null || Participation(ally, leader) == 0) continue;
                double chance = (attacking ? 0.55 : 0.80) + (SameFaith(ally, leader) ? 0.1 : 0)
                    - 0.15 * (_wars.Count(w => w != war && (OnSide(w, ally, true) || OnSide(w, ally, false))))
                    - GrudgeBetween(ally, leader) / 100;
                // Clerical rulers can defend their allies. Offensive aid is less attractive than
                // defending the church; it never relies on a cleric having a spouse or children.
                if (attacking && IsTheocracy(GovernmentOf(ally))) chance -= 0.25;
                var rng = Rng.For(_seed ^ 0xA113 ^ war.Started, war.Id, ally.Id);
                if (rng.Chance(Math.Clamp(chance, 0.05, 0.95)))
                {
                    (attacking ? war.AttackingAllies : war.DefendingAllies).Add(ally);
                    _sim.Log(FormationKind.WarJoined, war.Target, ally, leader, 0,
                        $"{ally.Capital.Name} joined {leader.Capital.Name} in {war.Name}");
                    Remember("aided", war.Target, ally, leader);
                }
                else
                {
                    Grieve(RulerOf(leader)?.House, RulerOf(ally)?.House, 8, "refused", war.Target);
                    _sim.Log(FormationKind.AidRefused, war.Target, ally, leader, 1,
                        $"{ally.Capital.Name} refused to aid {leader.Capital.Name} in {war.Name}");
                    Remember("refusedaid", war.Target, ally, leader);
                    if (_alliances.TryGetValue(Pair(leader, ally), out var agreement))
                        BreakAlliance(agreement, "a call to war was refused");
                }
            }
        }
    }

    private void CheckAlliances(List<string> problems)
    {
        foreach (var a in Alliances)
            if (a.A == a.B || !Sovereign(a.A) || !Sovereign(a.B) || Enemies(a.A, a.B))
                problems.Add($"Alliance of {a.A} and {a.B}: invalid parties");
        foreach (var p in _sim.Polities.Where(Sovereign))
            if (AlliesOf(p).Count() > MaxAllies) problems.Add($"{p}: too many allies");
        foreach (var war in _wars)
            if (war.AttackingAllies.Overlaps(war.DefendingAllies)
                || war.AttackingAllies.Any(p => !ValidParticipant(war, p, true))
                || war.DefendingAllies.Any(p => !ValidParticipant(war, p, false)))
                problems.Add($"{war.Name}: invalid allied participants");
    }
}
