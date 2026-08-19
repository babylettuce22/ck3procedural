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
                RealizeChain(top, realized, holderCounty, weight, [.. countries.Where(t => t != top)]);
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

                    RealizeChain(duchy, realized, holderCounty, weight);
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
            RealizeChain(emp, realized, holderCounty, weight);
        }

        // --- Step 2: Realize Kingdoms ---
        var realizedKingdoms = new HashSet<Title>();

        // 2A. Subordinate Kingdoms under Empires (Vassal Kings)
        foreach (var emp in chosenEmpires)
        {
            var empCap = holderCounty[emp];

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
                    RealizeChain(k, realized, holderCounty, weight);
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
            RealizeChain(king, realized, holderCounty, weight);
            realizedKingdoms.Add(king);
        }

        // --- Step 3: Feudal Vassal Consolidation (Duchies) ---
        foreach (var emp in chosenEmpires)
        {
            foreach (var k in emp.Children)
            {
                EnsureKingdomDuchiesRealized(k, realized, holderCounty, weight, rng, isUnderActiveRealm: true);
            }
        }

        foreach (var k in chosenIndepKingdoms)
        {
            EnsureKingdomDuchiesRealized(k, realized, holderCounty, weight, rng, isUnderActiveRealm: true);
        }

        var unruledKingdoms = fromExport ? [] : empires
            .Where(e => !chosenEmpires.Contains(e))
            .SelectMany(e => e.Children)
            .Where(k => !chosenIndepKingdoms.Contains(k))
            .ToList();

        foreach (var k in unruledKingdoms)
        {
            EnsureKingdomDuchiesRealized(k, realized, holderCounty, weight, rng, isUnderActiveRealm: false, cfg.DuchyTitleShare);
        }

        // --- Step 4: Resolve Primary Titles and Lieges ---
        var primary = new Dictionary<Title, Title>();
        foreach (var (title, county) in holderCounty)
        {
            if (!primary.TryGetValue(county, out var current) || Rank(title) > Rank(current))
                primary[county] = title;
        }

        var liege = new Dictionary<Title, Title>();
        foreach (var (county, top) in primary)
        {
            for (var above = top.Parent; above is not null; above = above.Parent)
            {
                if (!holderCounty.TryGetValue(above, out var lord) || lord == county) continue;

                // Ensure realm contiguity: never link across wilderness
                if (countyAdj != null && !IsReachable(county, lord, countyAdj))
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

    private static void EnsureKingdomDuchiesRealized(
        Title kingdom,
        HashSet<Title> realized,
        Dictionary<Title, Title> holderCounty,
        Dictionary<Title, int> weight,
        Rng rng,
        bool isUnderActiveRealm,
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
                RealizeChain(duchy, realized, holderCounty, weight);
            }
        }
    }

    private static void RealizeChain(
        Title title,
        HashSet<Title> realized,
        Dictionary<Title, Title> holderCounty,
        Dictionary<Title, int> weight,
        HashSet<Title>? avoid = null)
    {
        var capital = Capital(title, weight, avoid);
        var current = title;

        while (current.Tier != "c")
        {
            realized.Add(current);
            holderCounty[current] = capital;

            var next = Strongest(current, weight, avoid);
            if (next is null) break;
            current = next;
        }
    }

    /// <summary>
    /// The county a holder of <paramref name="title"/> sits in — its strongest child, all the way
    /// down.
    ///
    /// <paramref name="avoid"/> names children the descent must step around. It exists for imported
    /// maps, where a small country can be a duchy inside a larger neighbour's de jure kingdom: left
    /// alone the descent takes the strongest duchy, which *is* that country, and the neighbour's
    /// king comes out holding it — so the two states share one character and one of them stops
    /// existing. Skipping it takes the second-strongest instead, which is the same rule with one
    /// exception rather than a different rule.
    /// </summary>
    private static Title Capital(Title title, Dictionary<Title, int> weight,
        HashSet<Title>? avoid = null)
    {
        while (title.Tier != "c")
        {
            var next = Strongest(title, weight, avoid);
            if (next is null) break;
            title = next;
        }
        return title;
    }

    private static Title? Strongest(Title title, Dictionary<Title, int> weight,
        HashSet<Title>? avoid = null)
    {
        // Only while something is left to pick. A duchy whose every child is spoken for still needs
        // a capital, and a shared one beats none at all.
        if (avoid is { Count: > 0 } && title.Children.Any(c => !avoid.Contains(c)))
            return Strongest(title.Children.Where(c => !avoid.Contains(c)).ToList(), weight);

        return Strongest(title.Children, weight);
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