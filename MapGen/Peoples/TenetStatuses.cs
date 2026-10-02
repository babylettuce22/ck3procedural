using Ck3MapGen.Config;
using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen;

/// <summary>A faith's starting tenet statuses besides its core three. See <see cref="TenetStatuses"/>.</summary>
public sealed record TenetStatusSet(List<string> Known, List<string> Permitted, List<string> Prohibited);

/// <summary>
/// CK3 1.20 gives every faith a stance on every tenet: core (its own three), permitted, prohibited,
/// known or unknown. Vanilla sets the starting ones in <c>history/faiths</c>, and a playable character
/// knows what their faith knows, so with none set a generated faith's people know only its three
/// cores and every personal tenet they take is one of them (measured 2026-10-01: 760 of 760).
///
/// Shaped on vanilla's 102 faith histories (1.20.0.3):
/// <list type="bullet">
/// <item><b>Known</b>, every faith, median 4: 57% are core tenets of another religion's faiths (the
///   neighbours it meets), 19% its sister faiths' cores, 23% nobody's core. Here: sister cores, then
///   the cores of the faiths it borders most, then sometimes one tenet that fits its doctrines.</item>
/// <item><b>Permitted</b> and <b>prohibited</b>, 17 of 102 faiths, the organised churches. Here only
///   faiths with an Abrahamic shape or sees on the map permit (sister and regional-rite tenets
///   that sit beside their own), and only the Abrahamic-shaped prohibit (known tenets that clash with
///   their cores or doctrines, by the same can_pick table faith generation uses).</item>
/// <item><b>Popularity</b>: vanilla sets none, so neither does this.</item>
/// </list>
///
/// Its own stream per faith, so turning nothing else on or off moves it, and it moves nothing else.
/// </summary>
public static class TenetStatuses
{
    private const int Stream = 0x7E57;
    private const int MaxPermitted = 2;
    private const int MaxProhibited = 3;

    public static void Build(FaithMap faiths, Dictionary<Title, HashSet<Title>>? adjacency,
        VanillaVocabulary vocab, MapConfig cfg)
    {
        var declared = faiths.Faiths.Where(f => !f.Inherited).ToList();

        // How many county borders each faith shares with each other faith, the closest thing the map
        // has to "which neighbours does this faith meet".
        var borders = new Dictionary<Faith, Dictionary<Faith, int>>();
        if (adjacency is not null)
            foreach (var (county, faith) in faiths.ByCounty)
            {
                if (!adjacency.TryGetValue(county, out var next)) continue;
                foreach (var other in next)
                    if (faiths.ByCounty.TryGetValue(other, out var theirs) && theirs != faith)
                    {
                        var row = borders.TryGetValue(faith, out var r) ? r : borders[faith] = [];
                        row[theirs] = row.GetValueOrDefault(theirs) + 1;
                    }
            }

        // Seeded per faith by its key (the project's stream rule; StableHash, not string.GetHashCode).
        foreach (var faith in declared)
            faith.TenetStatus = Decide(faith, faiths, borders.GetValueOrDefault(faith) ?? [], vocab, cfg,
                Rng.For(cfg.Seed, Stream, Rng.StableHash(faith.Key)));
    }

    private static TenetStatusSet Decide(Faith faith, FaithMap faiths, Dictionary<Faith, int> border,
        VanillaVocabulary vocab, MapConfig cfg, Rng rng)
    {
        var own = faith.Tenets.ToHashSet(StringComparer.Ordinal);
        var held = faith.Religion.Doctrines.Values.Concat(own).ToList();
        // A church: Abrahamic-shaped, or holding real sees (HasClericalRegions alone is every faith that
        // could found one, which made over half the map permit where vanilla has 17 faiths of 102).
        bool church = faith.Religion.Abrahamic || faith.Sees.Count > 0;

        var known = new List<string>();
        void Know(string t) { if (!own.Contains(t) && !known.Contains(t) && vocab.Tenets.Contains(t)) known.Add(t); }

        // Sister faiths: their cores, the ones this faith does not share, in faith order.
        var sisterCores = faith.Religion.Faiths.Where(f => f != faith)
            .SelectMany(f => f.Tenets).Where(t => !own.Contains(t)).Distinct().ToList();
        foreach (var t in sisterCores.Take(3)) Know(t);

        // Then the faiths it borders, most-shared border first, to a total vanilla's median brackets.
        int target = Math.Max(known.Count, rng.Int(3, 6));
        foreach (var neighbour in border.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key.Key, StringComparer.Ordinal).Select(kv => kv.Key))
        {
            if (known.Count >= target) break;
            foreach (var t in neighbour.Tenets.Where(t => !own.Contains(t) && !known.Contains(t)).Take(target - known.Count))
                Know(t);
        }

        // Sometimes one more that nobody here holds, from the pool its doctrines allow; always for a
        // faith that met no one, since every vanilla faith knows at least one tenet besides its own.
        if (rng.Chance(0.4) || known.Count == 0)
        {
            var pool = Faiths.TenetPool(faith.Religion, vocab, cfg)
                .Where(t => !own.Contains(t) && !known.Contains(t) && vocab.Compatible(t, held)).ToList();
            if (pool.Count > 0) Know(pool[rng.Int(0, pool.Count - 1)]);
        }

        // Permitted: what a church tolerates among its own, its regional rites' and sister faiths'
        // tenets that sit beside its cores and doctrines. The rites' first: their counties practise them.
        var permitted = new List<string>();
        if (church)
            foreach (var t in faith.Rites.SelectMany(r => r.Tenets).Concat(sisterCores).Distinct())
            {
                if (permitted.Count >= MaxPermitted) break;
                if (!own.Contains(t) && vocab.Tenets.Contains(t) && vocab.Compatible(t, held.Concat(permitted)))
                    permitted.Add(t);
            }

        // Prohibited: an Abrahamic-shaped faith forbids what it knows of and cannot hold.
        var prohibited = new List<string>();
        if (faith.Religion.Abrahamic)
            foreach (var t in known)
            {
                if (prohibited.Count >= MaxProhibited) break;
                if (!permitted.Contains(t) && !vocab.Compatible(t, held)) prohibited.Add(t);
            }

        known.RemoveAll(t => permitted.Contains(t) || prohibited.Contains(t));
        return new TenetStatusSet(known, permitted, prohibited);
    }
}
