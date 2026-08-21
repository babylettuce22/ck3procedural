using Ck3MapGen.Config;
using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen;

public sealed class RealmMap
{
    public required Dictionary<Title, Title> HolderCounty { get; init; }
    public required Dictionary<Title, Title> Liege { get; init; }
    public required List<Title> Greatest { get; init; }
}

public static class Realms
{
    public static Dictionary<Title, HashSet<Title>> BuildCountyAdjacency(
        List<Title> counties,
        ProvinceMap map,
        int baronyCount,
        int[] order,
        int bridgeDistance)
    {
        var baronyToCounty = new Dictionary<int, Title>();
        foreach (var c in counties)
        {
            foreach (var b in c.Children)
            {
                if (b.ProvinceId >= 1 && b.ProvinceId <= baronyCount)
                    baronyToCounty[b.ProvinceId] = c;
            }
        }

        var landAdj = Titles.BuildAdjacency(map, baronyCount, order);
        var seaAdj = Titles.BuildSeaAdjacency(map, baronyCount, order, bridgeDistance);

        var countyAdj = new Dictionary<Title, HashSet<Title>>();
        foreach (var c in counties) countyAdj[c] = [];

        void AddLinks(Dictionary<int, HashSet<int>> adj)
        {
            foreach (var (bA, neighbors) in adj)
            {
                if (!baronyToCounty.TryGetValue(bA, out var cA)) continue;
                foreach (var bB in neighbors)
                {
                    if (baronyToCounty.TryGetValue(bB, out var cB) && cA != cB)
                    {
                        countyAdj[cA].Add(cB);
                        countyAdj[cB].Add(cA);
                    }
                }
            }
        }

        AddLinks(landAdj);
        AddLinks(seaAdj);

        return countyAdj;
    }

    private static bool IsReachable(
        Title startCounty,
        Title targetCounty,
        Dictionary<Title, HashSet<Title>> countyAdj)
    {
        if (startCounty == targetCounty) return true;
        if (!countyAdj.TryGetValue(startCounty, out _)) return false;

        var visited = new HashSet<Title> { startCounty };
        var queue = new Queue<Title>();
        queue.Enqueue(startCounty);

        while (queue.Count > 0)
        {
            var curr = queue.Dequeue();
            if (curr == targetCounty) return true;

            if (!countyAdj.TryGetValue(curr, out var neighbors)) continue;
            foreach (var n in neighbors)
            {
                if (visited.Add(n)) queue.Enqueue(n);
            }
        }

        return false;
    }

    public static RealmMap Build(
        List<Title> empires,
        Dictionary<Title, int> development,
        WildernessMap wilderness,
        MapConfig cfg,
        Rng rng,
        ProvinceMap? provinces = null,
        int[]? order = null,
        int baronyCount = 0,
        AzgaarImport? azgaar = null)
    {
        var all = Titles.Flatten(empires).ToList();
        var weight = Weigh(empires, development, wilderness);
        var nonWildCounties = all.Where(t => t.Tier == "c" && !wilderness.Contains(t)).ToList();

        int bridge = (int)Math.Round(cfg.Scaled(cfg.SeaBridgePixelsAtVanilla));
        var countyAdj = (provinces != null && order != null && baronyCount > 0)
            ? BuildCountyAdjacency(nonWildCounties, provinces, baronyCount, order, bridge)
            : null;

        var realized = new HashSet<Title>();
        var holderCounty = new Dictionary<Title, Title>();

        if (cfg.ShatteredWorld)
        {
            var shatteredHolders = new Dictionary<Title, Title>();
            var shatteredPrimary = new Dictionary<Title, Title>();

            foreach (var county in nonWildCounties)
            {
                shatteredHolders[county] = county;
                shatteredPrimary[county] = county;
            }

            var shatteredGreatest = shatteredPrimary.Keys
                .OrderByDescending(c => weight.GetValueOrDefault(c))
                .ThenBy(c => c.Index)
                .ToList();

            Console.WriteLine($"  realms: SHATTERED WORLD — {shatteredHolders.Count} independent counts (0 vassals)");

            return new RealmMap
            {
                HolderCounty = shatteredHolders,
                Liege = [],
                Greatest = shatteredGreatest,
            };
        }

        // All non-wilderness counties start with their own count holder
        foreach (var county in nonWildCounties)
            holderCounty[county] = county;

        // --- Step 0: Countries the export drew ---
        //
        // One realm per Azgaar state, which is what makes the realm map read like the export rather
        // than like our own clustering. When there is one, the generated empire and kingdom
        // selection below is skipped entirely: it would hand independence to titles the export never
        // drew and split the countries it did.
        var stateTitles = azgaar?.StateTitles is { Count: > 0 } bound ? bound : null;
        bool fromExport = stateTitles is not null;

        if (stateTitles is not null)
        {
            var countries = stateTitles.Values.ToHashSet();

            // Ascending by tier: a duchy-sized country claims its capital before the kingdom that
            // contains it goes looking for one, and the kingdom then steps around it. Without this a
            // small country inside a larger neighbour's de jure kingdom shares its ruler, and one of
            // the two stops existing.
            foreach (var top in stateTitles.OrderBy(kv => Rank(kv.Value)).ThenBy(kv => kv.Key)
                                           .Select(kv => kv.Value))
            {
                if (weight.GetValueOrDefault(top) <= 0) continue;

                var foreign = countries.Where(t => t != top).ToHashSet();
                RealizeChain(top, realized, holderCounty, weight, foreign,
                             MainBlock(top, countyAdj, weight, foreign));
            }

            // Internal vassals: most duchies inside a country get their own duke, so a kingdom is a
            // realm with a court rather than one character holding everything.
            foreach (var (_, top) in stateTitles.OrderBy(kv => kv.Key))
            {
                holderCounty.TryGetValue(top, out var capital);

                foreach (var duchy in Titles.Flatten([top]).Where(t => t.Tier == "d"))
                {
                    if (weight.GetValueOrDefault(duchy) <= 0) continue;
                    if (holderCounty.TryGetValue(duchy, out var held) && held == capital) continue;
                    if (!rng.Chance(0.85)) continue;

                    RealizeChain(duchy, realized, holderCounty, weight,
                                 avoid: null, MainBlock(duchy, countyAdj, weight));
                }
            }
        }

        // --- Step 1: Realize Empires ---
        var validEmpires = fromExport
            ? []
            : empires.Where(e => weight.GetValueOrDefault(e) > 0).ToList();
        int targetEmpires = cfg.EmpireTitleShare <= 0 ? 0
            : Math.Max(1, (int)Math.Round(validEmpires.Count * cfg.EmpireTitleShare));

        var chosenEmpires = validEmpires
            .OrderByDescending(e => weight.GetValueOrDefault(e))
            .Take(targetEmpires)
            .ToList();

        foreach (var emp in chosenEmpires)
        {
            RealizeChain(emp, realized, holderCounty, weight,
                         avoid: null, MainBlock(emp, countyAdj, weight));
        }

        // --- Step 2: Realize Kingdoms ---
        var realizedKingdoms = new HashSet<Title>();

        // 2A. Subordinate Kingdoms under Empires (Vassal Kings)
        foreach (var emp in chosenEmpires)
        {
            // Not indexed: RealizeChain declines a title with no seat it may hold, so an empire is
            // no longer guaranteed to be in the map by the time this reads it.
            holderCounty.TryGetValue(emp, out var empCap);

            foreach (var k in emp.Children)
            {
                if (weight.GetValueOrDefault(k) <= 0) continue;

                if (holderCounty.TryGetValue(k, out var kHolder) && kHolder == empCap)
                {
                    realizedKingdoms.Add(k);
                    continue;
                }

                if (rng.Chance(0.75))
                {
                    RealizeChain(k, realized, holderCounty, weight,
                                 avoid: null, MainBlock(k, countyAdj, weight));
                    realizedKingdoms.Add(k);
                }
            }
        }

        // 2B. Independent Kingdoms outside of Empires
        var independentKingdoms = fromExport ? [] : empires
            .Where(e => !chosenEmpires.Contains(e))
            .SelectMany(e => e.Children)
            .Where(k => weight.GetValueOrDefault(k) > 0)
            .ToList();

        int targetIndepKingdoms = cfg.KingdomTitleShare <= 0 ? 0
            : Math.Max(1, (int)Math.Round(independentKingdoms.Count * cfg.KingdomTitleShare));

        var chosenIndepKingdoms = independentKingdoms
            .OrderByDescending(k => weight.GetValueOrDefault(k))
            .Take(targetIndepKingdoms)
            .ToList();

        foreach (var king in chosenIndepKingdoms)
        {
            RealizeChain(king, realized, holderCounty, weight,
                         avoid: null, MainBlock(king, countyAdj, weight));
            realizedKingdoms.Add(king);
        }

        // --- Step 3: Feudal Vassal Consolidation (Duchies) ---
        foreach (var emp in chosenEmpires)
        {
            foreach (var k in emp.Children)
            {
                EnsureKingdomDuchiesRealized(k, realized, holderCounty, weight, rng, isUnderActiveRealm: true, countyAdj);
            }
        }

        foreach (var k in chosenIndepKingdoms)
        {
            EnsureKingdomDuchiesRealized(k, realized, holderCounty, weight, rng, isUnderActiveRealm: true, countyAdj);
        }

        var unruledKingdoms = fromExport ? [] : empires
            .Where(e => !chosenEmpires.Contains(e))
            .SelectMany(e => e.Children)
            .Where(k => !chosenIndepKingdoms.Contains(k))
            .ToList();

        foreach (var k in unruledKingdoms)
        {
            EnsureKingdomDuchiesRealized(k, realized, holderCounty, weight, rng, isUnderActiveRealm: false, countyAdj,
                                         cfg.DuchyTitleShare);
        }

        // --- Step 3b: Personal Demesne ---
        //
        // Every county starts as its own count's, and a higher title only ever redirected ONE county
        // to its holder — so a king's personal domain was a single county, exactly what each of his
        // counts held. Nothing in the generation made a liege the strongest man in his own realm.
        //
        // That is invisible under feudalism (it just reads as an unusually factious map) but the
        // nomad rules score it directly: obedience_value docks a vassal 500 for a larger army and
        // another 500 for a larger herd than his overlord, so a coin-flip on troop counts was
        // deciding whether a khan's vassals obeyed him at all.
        //
        // Kept off the imported path for now — the export decides its own realms and this would
        // quietly redraw them.
        if (!fromExport)
        {
            GrantDemesne(holderCounty, weight, countyAdj);
        }

        // --- Step 4: Resolve Primary Titles and Lieges ---
        var primary = new Dictionary<Title, Title>();
        foreach (var (title, county) in holderCounty)
        {
            if (!primary.TryGetValue(county, out var current) || Rank(title) > Rank(current))
                primary[county] = title;
        }

        // Which country each state title is, for the override below.
        var countryOf = new Dictionary<Title, int>();
        if (stateTitles is not null)
            foreach (var (id, title) in stateTitles)
                if (!countryOf.ContainsKey(title)) countryOf[title] = id;

        var liege = new Dictionary<Title, Title>();
        foreach (var (county, top) in primary)
        {
            for (var above = top.Parent; above is not null; above = above.Parent)
            {
                if (!holderCounty.TryGetValue(above, out var lord) || lord == county) continue;

                // The export's own answer about who owns this ground outranks the terrain test.
                //
                // Contiguity below is a heuristic for a world we invented, and it is measured against
                // crossings a medieval realm plausibly spanned — the widest is about a hundred vanilla
                // pixels. Azgaar is under no such rule: it drew Ignisar across an ocean seven times
                // that, and CK3 is perfectly happy with an overseas realm. Refusing the link there
                // did not make the map more plausible, it left nineteen duchies of a country the
                // export had drawn as one with no liege at all.
                //
                // Narrow on purpose. It fires only where the export names *both* ends as the same
                // country, so ground Azgaar left unclaimed is still governed by the terrain test —
                // an empty island is not a province of whoever happens to own it de jure.
                bool sameCountry = azgaar is not null
                                && countryOf.TryGetValue(above, out int state)
                                && state > 0
                                && azgaar.For(county)?.State.Id == state;

                // Ensure realm contiguity: never link across wilderness
                if (!sameCountry && countyAdj != null && !IsReachable(county, lord, countyAdj))
                    continue;

                liege[top] = above;
                break;
            }
        }

        if (stateTitles is not null)
        {
            // Azgaar's own vassalage, which is the only liege relation the export actually states.
            // Applied after the de jure walk so it overrides it rather than competing with it.
            int vassals = 0;
            var suzerained = new HashSet<Title>();

            foreach (var state in azgaar!.World.RealStates)
            {
                var relations = state.Relations;
                int suzerain = Array.IndexOf(relations, "Vassal");
                if (suzerain <= 0) continue;

                if (!stateTitles.TryGetValue(state.I, out var vassalTitle)) continue;
                if (!stateTitles.TryGetValue(suzerain, out var suzerainTitle)) continue;
                if (vassalTitle == suzerainTitle) continue;

                if (!holderCounty.TryGetValue(suzerainTitle, out var suzerainSeat)) continue;
                if (!holderCounty.TryGetValue(vassalTitle, out var vassalSeat)) continue;
                if (vassalSeat == suzerainSeat) continue;
                if (countyAdj is not null && !IsReachable(vassalSeat, suzerainSeat, countyAdj)) continue;

                liege[vassalTitle] = suzerainTitle;
                suzerained.Add(vassalTitle);
                vassals++;
            }

            // Every other country is independent, whatever the de jure tree says. A small state
            // sitting inside a larger neighbour's de jure kingdom is a country in its own right, and
            // leaving the walk's answer in place is what quietly annexed it.
            int freed = 0;
            foreach (var title in stateTitles.Values)
            {
                if (!liege.ContainsKey(title) || suzerained.Contains(title)) continue;
                liege.Remove(title);
                freed++;
            }

            if (freed > 0)
                Console.WriteLine($"  realms: freed {freed} states the de jure tree had made vassals");

            // By distinct holder, not by absent liege: two state titles sharing one character are
            // one realm however the liege table reads, and counting them apart is what made an
            // earlier version report nine independent states on a map that had eight.
            int shared = stateTitles.Values.Count()
                       - stateTitles.Values.Select(t => holderCounty.GetValueOrDefault(t))
                                           .Where(c => c is not null).Distinct().Count();

            int independent = stateTitles.Values.Count(t => !liege.ContainsKey(t)) - shared;

            if (shared > 0)
                Console.WriteLine($"  realms: {shared} states still share a ruler with a neighbour");

            Console.WriteLine($"  realms: bound to {stateTitles.Count} azgaar states — " +
                              $"{independent} independent, {vassals} vassal to a suzerain");
        }

        var greatest = primary
            .OrderByDescending(kv => Rank(kv.Value))
            .ThenByDescending(kv => weight[kv.Value])
            .ThenBy(kv => kv.Key.Index)
            .Select(kv => kv.Key)
            .ToList();

        Report(realized, primary, liege, all);

        return new RealmMap
        {
            HolderCounty = holderCounty,
            Liege = liege,
            Greatest = greatest,
        };
    }

    /// <summary>
    /// Folds a few nearby unclaimed counties into each titled ruler's own hands, so that a liege
    /// holds visibly more than any one of his vassals.
    ///
    /// Counties are taken border-first and only from inside the ruler's own de jure empire, which
    /// keeps a demesne a contiguous block around the capital rather than a scatter of enclaves.
    /// Targets are deliberately small — three counties for a king or emperor, two for a duke — to
    /// stay inside CK3's starting domain limits, which nomads sit one below.
    /// </summary>
    private static void GrantDemesne(
        Dictionary<Title, Title> holderCounty,
        Dictionary<Title, int> weight,
        Dictionary<Title, HashSet<Title>>? countyAdj)
    {
        // The highest title each ruler answers for, and every county that already seats one.
        var topTitle = new Dictionary<Title, Title>();
        var spokenFor = new HashSet<Title>();

        foreach (var (title, county) in holderCounty)
        {
            if (title.Tier == "c") continue;

            spokenFor.Add(county);
            if (!topTitle.TryGetValue(county, out var current) || Rank(title) > Rank(current))
                topTitle[county] = title;
        }

        // Free means the county still holds itself and no higher title seats its ruler there — in
        // other words, an independent count with nothing above him. Those are the only ones a liege
        // may absorb; taking a realized duke's capital would delete the duke.
        var free = holderCounty
            .Where(kv => kv.Key.Tier == "c" && kv.Value == kv.Key && !spokenFor.Contains(kv.Key))
            .Select(kv => kv.Key)
            .ToHashSet();

        int granted = 0;

        // Biggest first, so an emperor settles his capital duchy before the dukes beneath him carve
        // the same counties up.
        foreach (var (capital, title) in topTitle.OrderByDescending(kv => Rank(kv.Value))
                                                 .ThenByDescending(kv => weight.GetValueOrDefault(kv.Value))
                                                 .ThenBy(kv => kv.Key.Index))
        {
            int target = title.Tier switch { "e" or "k" => 3, "d" => 2, _ => 1 };
            var held = new HashSet<Title> { capital };

            while (held.Count < target)
            {
                var pick = NextDemesneCounty(capital, held, free, countyAdj, weight);
                if (pick is null) break;

                free.Remove(pick);
                held.Add(pick);
                holderCounty[pick] = capital;
                granted++;
            }
        }

        if (granted > 0)
            Console.WriteLine($"  realms: {granted} counties folded into their liege's personal demesne");
    }

    private static Title? NextDemesneCounty(
        Title capital,
        HashSet<Title> held,
        HashSet<Title> free,
        Dictionary<Title, HashSet<Title>>? countyAdj,
        Dictionary<Title, int> weight)
    {
        IEnumerable<Title> candidates = free;

        if (countyAdj is not null)
        {
            var bordering = new HashSet<Title>();
            foreach (var owned in held)
            {
                if (!countyAdj.TryGetValue(owned, out var neighbors)) continue;
                foreach (var n in neighbors)
                    if (free.Contains(n)) bordering.Add(n);
            }

            if (bordering.Count == 0) return null;
            candidates = bordering;
        }

        return candidates
            .Select(c => (County: c, Kinship: DeJureKinship(capital, c)))
            .Where(x => x.Kinship > 0)
            .OrderByDescending(x => x.Kinship)
            .ThenByDescending(x => weight.GetValueOrDefault(x.County))
            .ThenBy(x => x.County.Index)
            .Select(x => x.County)
            .FirstOrDefault();
    }

    /// <summary>
    /// How close two counties sit in the de jure tree: 3 for the same duchy, 2 for the same
    /// kingdom, 1 for the same empire, 0 for no shared ancestor at all.
    /// </summary>
    private static int DeJureKinship(Title a, Title b)
    {
        var duchyA = a.Parent;
        var duchyB = b.Parent;
        if (duchyA is null || duchyB is null) return 0;
        if (duchyA == duchyB) return 3;

        var kingdomA = duchyA.Parent;
        var kingdomB = duchyB.Parent;
        if (kingdomA is null || kingdomB is null) return 0;
        if (kingdomA == kingdomB) return 2;

        var empireA = kingdomA.Parent;
        var empireB = kingdomB.Parent;
        if (empireA is null || empireB is null) return 0;

        return empireA == empireB ? 1 : 0;
    }

    private static void EnsureKingdomDuchiesRealized(
        Title kingdom,
        HashSet<Title> realized,
        Dictionary<Title, Title> holderCounty,
        Dictionary<Title, int> weight,
        Rng rng,
        bool isUnderActiveRealm,
        Dictionary<Title, HashSet<Title>>? countyAdj = null,
        double independentDuchyShare = 0.5)
    {
        bool kingdomHeld = holderCounty.TryGetValue(kingdom, out var kingCap);

        foreach (var duchy in kingdom.Children)
        {
            if (weight.GetValueOrDefault(duchy) <= 0) continue;

            if (kingdomHeld && holderCounty.TryGetValue(duchy, out var dHolder) && dHolder == kingCap)
                continue;

            double chance = isUnderActiveRealm ? 0.95 : independentDuchyShare;

            if (rng.Chance(chance))
            {
                RealizeChain(duchy, realized, holderCounty, weight,
                             avoid: null, MainBlock(duchy, countyAdj, weight));
            }
        }
    }

    /// <summary>
    /// Seats a holder of <paramref name="title"/>, and every tier between it and his capital, in the
    /// one county the descent lands on.
    ///
    /// Returns false when the descent cannot reach a county it is allowed to hold — every child
    /// belongs to another country, or none of them reach <paramref name="allowed"/>. Nothing is
    /// realized in that case: a title with no seat of its own is better left unheld than seated in
    /// somebody else's capital.
    /// </summary>
    private static bool RealizeChain(
        Title title,
        HashSet<Title> realized,
        Dictionary<Title, Title> holderCounty,
        Dictionary<Title, int> weight,
        HashSet<Title>? avoid = null,
        HashSet<Title>? allowed = null)
    {
        if (Descend(title, weight, avoid, allowed) is not { } path) return false;

        var capital = path[^1];

        foreach (var step in path)
        {
            if (step.Tier == "c") break;
            realized.Add(step);
            holderCounty[step] = capital;
        }

        return true;
    }

    /// <summary>
    /// The chain from <paramref name="title"/> down to the county its holder sits in — its strongest
    /// child, all the way down — or null when no such county is reachable.
    ///
    /// <paramref name="avoid"/> names children the descent must step around. It exists for imported
    /// maps, where a small country can be a duchy inside a larger neighbour's de jure kingdom: left
    /// alone the descent takes the strongest duchy, which *is* that country, and the neighbour's
    /// king comes out holding it — so the two states share one character and one of them stops
    /// existing. Skipping it takes the second-strongest instead, which is the same rule with one
    /// exception rather than a different rule.
    ///
    /// Returns the whole path rather than just its end so that <see cref="RealizeChain"/> seats the
    /// tiers on the way down from the same walk that chose the capital. Walking it twice was fine
    /// only while both walks agreed, which is a property no future filter here is obliged to keep.
    /// </summary>
    private static List<Title>? Descend(Title title, Dictionary<Title, int> weight,
        HashSet<Title>? avoid = null, HashSet<Title>? allowed = null)
    {
        var path = new List<Title> { title };

        while (path[^1].Tier != "c")
        {
            var next = Strongest(path[^1], weight, avoid, allowed);
            if (next is null) return null;
            path.Add(next);
        }

        return path;
    }

    private static Title? Strongest(Title title, Dictionary<Title, int> weight,
        HashSet<Title>? avoid = null, HashSet<Title>? allowed = null)
    {
        var children = title.Children.AsEnumerable();

        // Refused rather than ignored when it leaves nothing to pick.
        //
        // This used to fall back to the full child list "because a shared capital beats none at all",
        // and the case that triggers it is precisely the one that must not be allowed: a title whose
        // every child is another country has no ground of its own. On the Lumbaris export the single
        // child of Ignisar's kingdom tier was Hauls, so the fallback fired, the emperor of Ignisar
        // was seated in Hauls, and the contiguity test then refused to make anyone in Ignisar proper
        // his vassal — 43% of his own empire, split across fourteen realms.
        if (avoid is { Count: > 0 }) children = children.Where(c => !avoid.Contains(c));

        // Only where the title's own main block reaches. Development alone picks the richest county
        // anywhere in the subtree, and on a country with an island exclave that is the island: Zraz's
        // king was seated on a two-county island and held three per cent of his kingdom.
        if (allowed is not null) children = children.Where(c => Reaches(c, allowed));

        return Strongest([.. children], weight);
    }

    /// <summary>Whether any county under <paramref name="title"/> is in <paramref name="allowed"/>.</summary>
    private static bool Reaches(Title title, HashSet<Title> allowed)
        => title.Tier == "c"
            ? allowed.Contains(title)
            : title.Children.Any(c => Reaches(c, allowed));

    /// <summary>
    /// The counties of <paramref name="title"/> that lie in its largest contiguous block, or null
    /// when there is no adjacency graph to measure against.
    ///
    /// A capital outside the main block is not a cosmetic problem. The liege walk in
    /// <see cref="Build"/> refuses to link a vassal to a lord his county cannot reach, so a ruler
    /// seated in an exclave loses every vassal on his mainland at once — which is how a map with
    /// twelve countries on it came out with a hundred and thirty-six independent realms.
    ///
    /// Counties under an avoided child are left out of the measurement as well as out of the descent,
    /// so a country's main block is its own ground rather than its ground plus a neighbour's.
    /// </summary>
    private static HashSet<Title>? MainBlock(Title title,
        Dictionary<Title, HashSet<Title>>? countyAdj,
        Dictionary<Title, int> weight,
        HashSet<Title>? avoid = null)
    {
        if (countyAdj is null) return null;

        var owned = new List<Title>();
        Collect(title);

        if (owned.Count == 0) return null;

        var members = owned.ToHashSet();
        var seen = new HashSet<Title>();
        HashSet<Title>? best = null;

        // Seeded in tree order rather than in hash order, so that two blocks of equal size always
        // resolve the same way and the same export produces the same map twice.
        foreach (var start in owned)
        {
            if (!seen.Add(start)) continue;

            var block = new HashSet<Title> { start };
            var queue = new Queue<Title>();
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                if (!countyAdj.TryGetValue(queue.Dequeue(), out var near)) continue;

                foreach (var next in near)
                {
                    if (!members.Contains(next) || !block.Add(next)) continue;
                    seen.Add(next);
                    queue.Enqueue(next);
                }
            }

            if (best is null || block.Count > best.Count) best = block;
        }

        return best;

        void Collect(Title node)
        {
            if (node.Tier == "c")
            {
                if (weight.GetValueOrDefault(node) > 0) owned.Add(node);
                return;
            }

            foreach (var child in node.Children)
            {
                if (avoid is not null && avoid.Contains(child)) continue;
                Collect(child);
            }
        }
    }

    private static Title? Strongest(IReadOnlyList<Title> children, Dictionary<Title, int> weight)
        => children.Count == 0
            ? null
            : children.OrderByDescending(c => weight.GetValueOrDefault(c))
                            .ThenBy(c => c.Index).First();

    private static Dictionary<Title, int> Weigh(List<Title> empires,
        Dictionary<Title, int> development, WildernessMap wilderness)
    {
        var weight = new Dictionary<Title, int>();
        foreach (var root in empires) Visit(root);
        return weight;

        int Visit(Title title)
        {
            int total = title.Tier == "c" && !wilderness.Contains(title)
                ? development.GetValueOrDefault(title) + 1
                : 0;
            foreach (var child in title.Children) total += Visit(child);

            weight[title] = total;
            return total;
        }
    }

    private static int Rank(Title title) => title.Tier switch
    {
        "e" => 4,
        "k" => 3,
        "d" => 2,
        "c" => 1,
        _ => 0,
    };

    private static void Report(HashSet<Title> realized, Dictionary<Title, Title> primary,
        Dictionary<Title, Title> liege, List<Title> all)
    {
        int emperors = primary.Values.Count(t => t.Tier == "e");
        int kings = primary.Values.Count(t => t.Tier == "k");
        int dukes = primary.Values.Count(t => t.Tier == "d");
        int counts = primary.Values.Count(t => t.Tier == "c");

        int indepEmperors = primary.Count(kv => kv.Value.Tier == "e" && !liege.ContainsKey(kv.Value));
        int indepKings = primary.Count(kv => kv.Value.Tier == "k" && !liege.ContainsKey(kv.Value));
        int indepDukes = primary.Count(kv => kv.Value.Tier == "d" && !liege.ContainsKey(kv.Value));
        int indepCounts = primary.Count(kv => kv.Value.Tier == "c" && !liege.ContainsKey(kv.Value));

        int totalIndependent = primary.Values.Count(t => !liege.ContainsKey(t));
        int totalVassals = primary.Count - totalIndependent;

        Console.WriteLine($"  rulers: {emperors} emperors ({indepEmperors} indep), " +
                          $"{kings} kings ({indepKings} indep), " +
                          $"{dukes} dukes ({indepDukes} petty kings), " +
                          $"{counts} counts ({indepCounts} indep) " +
                          $"— {totalIndependent} independent realms, {totalVassals} vassals total");
    }
}