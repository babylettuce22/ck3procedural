using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.Io;

namespace Ck3MapGen.MapGen;

/// <summary>
/// Builds the de jure hierarchy inside Azgaar's borders instead of across them.
///
/// The generated hierarchy in <see cref="Titles.Build"/> clusters baronies upward by geometry alone,
/// so nothing stops a county straddling a border or a kingdom swallowing half of three states. That
/// is correct when the map is ours to invent and wrong when an export has already decided where the
/// countries are — which is what made the imported names land on realms shaped nothing like the ones
/// they came from.
///
/// The change is not to the clustering but to what it is allowed to cross. The same
/// <see cref="Titles.Cluster"/> and <see cref="Titles.AbsorbUndersized"/> run here, over the same
/// adjacency, and every arity band is still aimed at — but each runs *within* one Azgaar object, so
/// no cluster can span two. Where the bands and Azgaar's shapes disagree, Azgaar wins and the band
/// bends, because a duchy two counties short reads as a small duchy while a duchy straddling a
/// national border reads as a bug.
///
/// The mapping is measured rather than assumed, and the measurement is the reason it looks like this:
/// on a real 20-state export over 1,712 baronies, Azgaar's provinces came out at a median of six
/// baronies each, which is a *county* on our bands, not a duchy. So provinces become counties, states
/// become kingdoms or empires by rank, and the duchy tier — which has no Azgaar object at all — is
/// synthesised by grouping a state's provinces. Mapping provinces to duchies instead, which was the
/// obvious guess, would have produced hundreds of one-county duchies.
/// </summary>
public static class AzgaarHierarchy
{
    /// <summary>
    /// Builds the whole tree, from baronies to empires, inside the export's borders.
    ///
    /// Returns empires, exactly as <see cref="Titles.Build"/> does, so every consumer downstream is
    /// unaware which of the two produced them.
    /// </summary>
    public static List<Title> Build(ProvinceMap map, int baronyCount, int[] order, MapConfig cfg,
                                    Rng rng, AzgaarImport azgaar)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var plan = azgaar.Plan!;

        var adjacency = Titles.BuildAdjacency(map, baronyCount, order);
        int bridge = (int)Math.Round(cfg.Scaled(cfg.SeaBridgePixelsAtVanilla));
        var seaAdjacency = Titles.BuildSeaAdjacency(map, baronyCount, order, bridge);

        var baronies = new List<Title>(baronyCount);
        for (int i = 0; i < baronyCount; i++)
            baronies.Add(new Title { Tier = "b", Index = i, ProvinceId = i + 1 });
        var byProvince = baronies.ToDictionary(b => b.ProvinceId);

        var position = new (double X, double Y)[baronyCount + 1];
        for (int label = 0; label < order.Length; label++)
        {
            int id = order[label];
            if (id >= 1 && id <= baronyCount) position[id] = (map.Seeds[label].X, map.Seeds[label].Y);
        }

        // --- Counties: one per Azgaar province, split when a province holds more than a county's
        //     worth and merged when it holds less than one. -----------------------------------------
        // Keyed on (state, province), not province alone. A county must never span two countries,
        // and grouping by province only *nearly* guarantees that — Azgaar states hold plenty of land
        // outside any province, and lumping all of it together put baronies from different states,
        // and the genuinely ownerless ones, into the same bucket. Ownerless ground then never formed
        // a county of its own, which is why the wilderness pass could not find any.
        var groups = new Dictionary<(int State, int Province), List<int>>();

        for (int id = 1; id <= baronyCount; id++)
        {
            var key = (azgaar.StateOfBarony(id), azgaar.ProvinceOfBarony(id));
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = [];
            list.Add(id);
        }

        int provinceCount = groups.Keys.Where(k => k.Province > 0).Select(k => k.Province).Distinct().Count();
        int stateless = groups.Where(g => g.Key.State == 0).Sum(g => g.Value.Count);

        var countyClusters = new List<List<int>>();
        var countyState = new List<int>();

        foreach (var (key, members) in groups.OrderBy(g => g.Key.State).ThenBy(g => g.Key.Province))
        {
            // A group at or under the county ceiling is one county outright. Splitting it would
            // invent a border the export never drew.
            if (members.Count <= Titles.MaxBaroniesPerCounty)
            {
                countyClusters.Add(members);
                countyState.Add(key.State);
                continue;
            }

            var inside = Restrict(adjacency, members);
            foreach (var cluster in Titles.AbsorbUndersized(
                         Titles.Cluster(members, inside, Titles.MinBaroniesPerCounty,
                                        Titles.MaxBaroniesPerCounty, rng, position),
                         inside, Titles.MinBaroniesPerCounty, Titles.MaxBaroniesPerCounty, position))
            {
                if (cluster.Count == 0) continue;
                countyClusters.Add(cluster);
                countyState.Add(key.State);
            }
        }

        var counties = Titles.Wrap("c", countyClusters, c => c.Select(p => byProvince[p]));
        var countyPosition = Titles.Roll(countyClusters, position);
        var countyAdjacency = Titles.LiftAdjacency(countyClusters, adjacency);
        var countySea = Titles.LiftAdjacency(countyClusters, seaAdjacency);

        // --- Each state's own subtree, rooted at the rank the plan granted it. --------------------
        var granted = plan.States.ToDictionary(p => p.State.I, p => p.Granted);
        var roots = new List<Title>();
        var rootState = new Dictionary<Title, int>();
        var rootPosition = new Dictionary<Title, (double X, double Y)>();

        var byState = new Dictionary<int, List<int>>();
        for (int i = 0; i < counties.Count; i++)
        {
            int state = countyState[i];
            if (!byState.TryGetValue(state, out var list)) byState[state] = list = [];
            list.Add(i);
        }

        foreach (var (state, members) in byState.OrderBy(kv => kv.Key))
        {
            // Ownerless ground has no state to rank, and handing all of it to one title would make
            // a single duchy of every wilderness county on the map. It is clustered into ordinary
            // duchies instead and grouped upward like anything else.
            int tier = state == 0 ? AzgaarTiers.Duchy : granted.GetValueOrDefault(state, AzgaarTiers.Duchy);
            var subtree = state == 0
                ? Ownerless(members, counties, countyAdjacency, countyPosition, cfg, rng)
                : BuildState(members, tier, counties, countyAdjacency, countyPosition, cfg, rng);

            foreach (var root in subtree)
            {
                roots.Add(root);
                rootState[root] = state;
                rootPosition[root] = Centre(root, counties, countyPosition);
            }
        }

        // --- Everything above the state, synthesised until every root is an empire. ----------------
        var affinity = Affinity(azgaar, rootState);
        var current = roots;

        foreach (string tier in (string[])["d", "k", "e"])
            current = RaiseTo(tier, current, affinity, rootState, rootPosition,
                              countyAdjacency, countySea, counties, countyPosition, cfg, rng);

        // Every title made here starts at index 0, which is fine for correctness — AssignNames
        // dedupes keys — and produces a pile of "_2", "_3" suffixes in localisation. Numbering per
        // tier before naming keeps the keys legible.
        foreach (var group in Titles.Flatten(current).GroupBy(t => t.Tier))
        {
            int i = 0;
            foreach (var title in group) title.Index = i++;
        }

        // Nothing may be lost on the way up. The grouping passes above hand every root to a parent,
        // and a root that reaches none of them simply vanishes from the tree — which is exactly what
        // happened to the ownerless counties, silently, until a count downstream did not add up.
        // Cheaper to guarantee coverage here than to trust four passes to never drop anything.
        var reachable = Titles.Flatten(current).ToHashSet();
        var orphans = counties.Where(c => !reachable.Contains(c)).ToList();

        if (orphans.Count > 0)
        {
            var host = new Title { Tier = "d", Index = 0 };
            foreach (var orphan in orphans)
            {
                orphan.Parent = host;
                host.Children.Add(orphan);
            }

            // Into the nearest empire, so it is somebody's de jure ground rather than a root of its own.
            var home = current.OrderBy(e => Titles.Flatten([e]).Count(t => t.Tier == "c")).First();
            var kingdom = Titles.Flatten([home]).FirstOrDefault(t => t.Tier == "k");

            if (kingdom is not null) { host.Parent = kingdom; kingdom.Children.Add(host); }
            else { host.Tier = "e"; current.Add(host); }

            Console.WriteLine($"    recovered {orphans.Count} counties no parent claimed");
        }

        // The title each state ended up as, for realm formation to hang a ruler on. A state that
        // produced several roots is represented by its highest, which is the one that reads as the
        // country — the others are fragments the grouping pass will have parented elsewhere.
        var stateTitles = new Dictionary<int, Title>();
        foreach (var (title, state) in rootState)
        {
            if (state <= 0) continue;
            if (!stateTitles.TryGetValue(state, out var held) || TierOf(title.Tier) > TierOf(held.Tier))
                stateTitles[state] = title;
        }
        azgaar.SetStateTitles(stateTitles);

        Titles.AssignColorsTo(current, rng);

        int kingdoms = Titles.Flatten(current).Count(t => t.Tier == "k");
        int duchies = Titles.Flatten(current).Count(t => t.Tier == "d");
        Console.WriteLine($"  titles (azgaar-constrained): {current.Count} empires, {kingdoms} kingdoms, " +
                          $"{duchies} duchies, {counties.Count} counties, {baronies.Count} baronies " +
                          $"({sw.ElapsedMilliseconds} ms)");
        Console.WriteLine($"    counties cut from {provinceCount} azgaar provinces " +
                          $"({counties.Count - provinceCount:+0;-0;0} against one-per-province)" +
                          (stateless > 0 ? $", {stateless} baronies on ownerless ground" : ""));

        return current;
    }

    /// <summary>
    /// Ownerless counties, clustered into duchies of ordinary size.
    ///
    /// Separate from <see cref="BuildState"/> because there is no country here to keep whole — this
    /// is the ground Azgaar's own state growth never reached, and the only thing that matters is
    /// that it comes out as normal-sized titles rather than as one duchy holding every wilderness
    /// county on the map, which is what routing it through the state path produced.
    /// </summary>
    private static List<Title> Ownerless(List<int> members, List<Title> counties,
        Dictionary<int, HashSet<int>> countyAdjacency, (double X, double Y)[] countyPosition,
        MapConfig cfg, Rng rng)
    {
        if (members.Count == 0) return [];

        var inside = Restrict(countyAdjacency, members);
        var clusters = Titles.AbsorbUndersized(
            Titles.Cluster(members, inside, Titles.MinCountiesPerDuchy,
                           Titles.MaxCountiesPerDuchy, rng, countyPosition),
            inside, cfg.MinChildrenPerTitle, Titles.MaxCountiesPerDuchy, countyPosition);

        clusters = [.. clusters.Where(c => c.Count > 0)];
        if (clusters.Count == 0) clusters = [members];

        return Titles.Wrap("d", clusters, c => c.Select(i => counties[i]));
    }

    /// <summary>
    /// Builds one state's counties up to the tier it was granted, and returns the roots.
    ///
    /// Returns a list rather than one title because a state granted county tier has nothing above its
    /// counties yet — they are roots until the grouping pass wraps them. Every other case returns one.
    /// </summary>
    private static List<Title> BuildState(List<int> members, int tier, List<Title> counties,
        Dictionary<int, HashSet<int>> countyAdjacency, (double X, double Y)[] countyPosition,
        MapConfig cfg, Rng rng)
    {
        if (members.Count == 0) return [];
        if (tier <= AzgaarTiers.County) return [.. members.Select(i => counties[i])];

        var inside = Restrict(countyAdjacency, members);

        // Counties into duchies. A state ranked duchy is one duchy holding all of them, however many
        // that is — the alternative is splitting a country the export drew as one.
        List<List<int>> duchyClusters;
        if (tier == AzgaarTiers.Duchy)
        {
            duchyClusters = [members];
        }
        else
        {
            duchyClusters = Titles.AbsorbUndersized(
                Titles.Cluster(members, inside, Titles.MinCountiesPerDuchy,
                               Titles.MaxCountiesPerDuchy, rng, countyPosition),
                inside, cfg.MinChildrenPerTitle, Titles.MaxCountiesPerDuchy, countyPosition);
            duchyClusters = [.. duchyClusters.Where(c => c.Count > 0)];
            if (duchyClusters.Count == 0) duchyClusters = [members];
        }

        var duchies = Titles.Wrap("d", duchyClusters, c => c.Select(i => counties[i]));
        if (tier == AzgaarTiers.Duchy) return duchies;

        var duchyAdjacency = Titles.LiftAdjacency(duchyClusters, inside);
        var duchyPosition = Titles.Roll(duchyClusters, countyPosition);
        var duchyIndices = Enumerable.Range(0, duchies.Count).ToList();

        // Duchies into kingdoms. A state ranked kingdom is one kingdom over all its duchies — this is
        // the step where the arity band is deliberately ignored, and it is the whole point: Azgaar
        // said this is one country, so it is one title however many duchies that turns out to be.
        List<List<int>> kingdomClusters;
        if (tier == AzgaarTiers.Kingdom)
        {
            kingdomClusters = [duchyIndices];
        }
        else
        {
            kingdomClusters = Titles.AbsorbUndersized(
                Titles.Cluster(duchyIndices, duchyAdjacency, Titles.MinDuchiesPerKingdom,
                               Titles.MaxDuchiesPerKingdom, rng, duchyPosition),
                duchyAdjacency, cfg.MinChildrenPerTitle, Titles.MaxDuchiesPerKingdom, duchyPosition);
            kingdomClusters = [.. kingdomClusters.Where(c => c.Count > 0)];
            if (kingdomClusters.Count == 0) kingdomClusters = [duchyIndices];
        }

        var kingdoms = Titles.Wrap("k", kingdomClusters, c => c.Select(i => duchies[i]));
        if (tier == AzgaarTiers.Kingdom) return kingdoms;

        // An empire over the lot.
        var empire = new Title { Tier = "e", Index = 0 };
        foreach (var kingdom in kingdoms)
        {
            kingdom.Parent = empire;
            empire.Children.Add(kingdom);
        }
        return [empire];
    }

    /// <summary>
    /// Wraps every root still below <paramref name="tier"/> into titles of that tier, and leaves the
    /// ones already at or above it alone.
    ///
    /// Grouping prefers what the export actually says over what geometry suggests: states bound by
    /// suzerainty go together first, then states sharing a culture, and only what is left over is
    /// clustered by adjacency. That ordering is why a vassal ends up inside its suzerain's empire
    /// rather than inside whichever neighbour happened to be nearest.
    /// </summary>
    private static List<Title> RaiseTo(string tier, List<Title> roots,
        Dictionary<int, int> affinity, Dictionary<Title, int> rootState,
        Dictionary<Title, (double X, double Y)> rootPosition,
        Dictionary<int, HashSet<int>> countyAdjacency, Dictionary<int, HashSet<int>> countySea,
        List<Title> counties, (double X, double Y)[] countyPosition,
        MapConfig cfg, Rng rng)
    {
        int target = TierOf(tier);
        var below = roots.Where(r => TierOf(r.Tier) < target).ToList();
        var above = roots.Where(r => TierOf(r.Tier) >= target).ToList();
        if (below.Count == 0) return roots;

        // Group by affinity: everything that shares an affinity key becomes one title.
        var groups = new Dictionary<int, List<Title>>();
        var loose = new List<Title>();

        foreach (var root in below)
        {
            int state = rootState.GetValueOrDefault(root);
            int key = state > 0 ? affinity.GetValueOrDefault(state, -state) : 0;

            if (key == 0) { loose.Add(root); continue; }
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = [];
            list.Add(root);
        }

        var wrapped = new List<Title>();
        var absorbed = new HashSet<Title>();

        foreach (var (key, members) in groups.OrderBy(kv => kv.Key))
        {
            // If a state in this group already stands at the target tier, the rest belong inside it
            // rather than beside it. This is the whole reason suzerainty is read at all: an empire
            // and its vassals should be one empire, not two neighbouring ones.
            var host = above.FirstOrDefault(r => TierOf(r.Tier) == target
                                              && rootState.GetValueOrDefault(r) > 0
                                              && affinity.GetValueOrDefault(rootState[r], -1) == key
                                              && !absorbed.Contains(r));

            if (host is not null)
            {
                foreach (var child in members)
                {
                    child.Parent = host;
                    host.Children.Add(child);
                }
                absorbed.Add(host);
                continue;
            }

            wrapped.Add(Wrap(tier, members));
        }

        // Roots with no affinity at all — ownerless ground, and any state the export left out of its
        // own relations.
        //
        // These join a neighbour rather than founding realms of their own. Left to cluster among
        // themselves they produced fourteen empires on a thirteen-state map, most of them wilderness:
        // de jure, empty ground belongs to whoever it borders, which is also how vanilla treats its
        // own wasteland. Only scraps with no neighbour at all get a title of their own.
        //
        // This block is load-bearing and was briefly deleted by an edit that rewrote the group loop
        // above and took the lines after it with it. Nothing failed: `loose` was still filled, just
        // never read, and seventy counties vanished from the tree with no error anywhere. The guard
        // at the end of Build exists because of exactly this.
        if (loose.Count > 0)
        {
            var hosts = above.Where(r => TierOf(r.Tier) == target).ToList();
            var stillLoose = new List<Title>();

            if (hosts.Count > 0)
            {
                var combined = new List<Title>(loose);
                combined.AddRange(hosts);
                var links = Neighbours(combined, counties, countyAdjacency);

                for (int i = 0; i < loose.Count; i++)
                {
                    // An adjacent host first, then the nearest one, so a scrap on an island still
                    // lands somewhere sensible rather than nowhere.
                    Title? home = null;
                    if (links.TryGetValue(i, out var adjacent))
                        home = adjacent.Where(j => j >= loose.Count)
                                       .Select(j => combined[j])
                                       .OrderBy(h => h.Children.Count)
                                       .FirstOrDefault();

                    home ??= Nearest(loose[i], hosts, rootPosition);
                    if (home is null) { stillLoose.Add(loose[i]); continue; }

                    loose[i].Parent = home;
                    home.Children.Add(loose[i]);
                }
            }
            else stillLoose.AddRange(loose);

            if (stillLoose.Count > 0)
            {
                var index = new Dictionary<int, Title>();
                var positions = new (double X, double Y)[stillLoose.Count];
                for (int i = 0; i < stillLoose.Count; i++)
                {
                    index[i] = stillLoose[i];
                    positions[i] = rootPosition.GetValueOrDefault(stillLoose[i]);
                }

                var neighbours = Neighbours(stillLoose, counties, countyAdjacency);
                foreach (var cluster in Titles.Cluster([.. Enumerable.Range(0, stillLoose.Count)],
                             neighbours, 2, Math.Max(2, cfg.MinChildrenPerTitle), rng, positions))
                {
                    if (cluster.Count == 0) continue;
                    wrapped.Add(Wrap(tier, [.. cluster.Select(i => index[i])]));
                }
            }
        }

        return [.. above, .. wrapped];

        static Title Wrap(string tier, List<Title> children)
        {
            var title = new Title { Tier = tier, Index = 0 };
            foreach (var child in children)
            {
                child.Parent = title;
                title.Children.Add(child);
            }
            return title;
        }
    }

    /// <summary>
    /// Which states belong together above their own rank, as a union-find over the export's own
    /// relations: a vassal joins its suzerain, and states sharing a culture join each other.
    ///
    /// Returns a representative state id per group. A state in no group maps to itself, so it is
    /// wrapped alone rather than dropped.
    /// </summary>
    private static Dictionary<int, int> Affinity(AzgaarImport azgaar, Dictionary<Title, int> rootState)
    {
        var states = azgaar.World.RealStates.ToList();
        var parent = states.ToDictionary(s => s.I, s => s.I);

        int Find(int i)
        {
            while (parent.TryGetValue(i, out int up) && up != i) i = parent[i] = parent[up];
            return i;
        }

        void Join(int a, int b)
        {
            if (!parent.ContainsKey(a) || !parent.ContainsKey(b)) return;
            int ra = Find(a), rb = Find(b);
            if (ra != rb) parent[Math.Max(ra, rb)] = Math.Min(ra, rb);
        }

        // 1. Suzerainty. Azgaar writes this from both ends — the vassal's entry for its suzerain
        //    reads "Vassal", the suzerain's entry for its vassal reads "Suzerain" — so reading one
        //    of the two is enough and reading both is harmless.
        int suzerainties = 0;
        foreach (var state in states)
        {
            var relations = state.Relations;
            for (int other = 0; other < relations.Length; other++)
            {
                if (relations[other] is not ("Vassal" or "Suzerain")) continue;
                Join(state.I, other);
                suzerainties++;
            }
        }

        // 2. Shared culture, but only between states that actually touch.
        //
        //    Joining every state of a culture regardless of where it sits is what a first attempt
        //    did, and on a 20-state export it collapsed the whole map into three groups: cultures
        //    recur all over a map, and union-find makes every such coincidence transitive. Requiring
        //    adjacency turns it back into what it was meant to be — a reason for neighbours who share
        //    a people to end up under one crown, not a global merge.
        var byId = states.ToDictionary(s => s.I);
        int cultural = 0;
        foreach (var state in states)
        {
            if (state.Culture <= 0) continue;
            foreach (int neighbour in state.Neighbors)
            {
                if (!byId.TryGetValue(neighbour, out var other)) continue;
                if (other.Culture != state.Culture) continue;
                if (Find(state.I) == Find(other.I)) continue;
                Join(state.I, other.I);
                cultural++;
            }
        }

        var result = states.ToDictionary(s => s.I, s => Find(s.I));
        int groups = result.Values.Distinct().Count();
        Console.WriteLine($"    state affinity: {groups} groups from {states.Count} states " +
                          $"({suzerainties / 2} suzerainties, {cultural} shared-culture borders)");
        return result;
    }

    // --- Small helpers -------------------------------------------------------------------------

    /// <summary>The candidate whose centre is closest, or null when there are none.</summary>
    private static Title? Nearest(Title of, List<Title> candidates,
        Dictionary<Title, (double X, double Y)> position)
    {
        if (candidates.Count == 0) return null;
        var (x, y) = position.GetValueOrDefault(of);

        Title? best = null;
        double bestCost = double.PositiveInfinity;

        foreach (var candidate in candidates)
        {
            var (cx, cy) = position.GetValueOrDefault(candidate);
            double cost = (cx - x) * (cx - x) + (cy - y) * (cy - y);
            if (cost >= bestCost) continue;
            bestCost = cost;
            best = candidate;
        }

        return best;
    }

    private static int TierOf(string tier) => tier switch
    {
        "e" => AzgaarTiers.Empire, "k" => AzgaarTiers.Kingdom,
        "d" => AzgaarTiers.Duchy, _ => AzgaarTiers.County,
    };

    /// <summary>The adjacency graph with everything outside <paramref name="members"/> cut away.</summary>
    private static Dictionary<int, HashSet<int>> Restrict(
        Dictionary<int, HashSet<int>> full, IReadOnlyCollection<int> members)
    {
        var allowed = new HashSet<int>(members);
        var result = new Dictionary<int, HashSet<int>>(members.Count);
        foreach (int m in members)
            result[m] = full.TryGetValue(m, out var n) ? [.. n.Where(allowed.Contains)] : [];
        return result;
    }

    private static int StateOf(List<int> baronies, AzgaarImport azgaar)
    {
        var votes = new Dictionary<int, int>();
        foreach (int id in baronies)
        {
            int state = azgaar.StateOfBarony(id);
            if (state > 0) votes[state] = votes.GetValueOrDefault(state) + 1;
        }
        return votes.Count == 0 ? 0
             : votes.OrderByDescending(v => v.Value).ThenBy(v => v.Key).First().Key;
    }

    private static (double X, double Y) Centre(Title root, List<Title> counties,
                                               (double X, double Y)[] countyPosition)
    {
        var index = counties.Select((c, i) => (c, i)).ToDictionary(x => x.c, x => x.i);
        double x = 0, y = 0;
        int n = 0;
        foreach (var county in Titles.Flatten([root]).Where(t => t.Tier == "c"))
        {
            if (!index.TryGetValue(county, out int i)) continue;
            x += countyPosition[i].X;
            y += countyPosition[i].Y;
            n++;
        }
        return n == 0 ? (0, 0) : (x / n, y / n);
    }

    /// <summary>
    /// Which of these roots actually touch, by way of the counties underneath them.
    ///
    /// This replaced a distance graph with an arbitrary radius, which was wrong in the way that
    /// matters: roots that no radius happened to join were left unclustered and then dropped from
    /// the tree entirely. On a map with 293 baronies of ownerless ground that silently lost
    /// seventy-three counties. Real adjacency has no tuning constant to get wrong, and two roots
    /// either share a border or they do not.
    /// </summary>
    private static Dictionary<int, HashSet<int>> Neighbours(List<Title> roots, List<Title> counties,
        Dictionary<int, HashSet<int>> countyAdjacency)
    {
        var index = new Dictionary<Title, int>();
        for (int i = 0; i < counties.Count; i++) index[counties[i]] = i;

        var owner = new Dictionary<int, int>();
        for (int r = 0; r < roots.Count; r++)
            foreach (var county in Titles.Flatten([roots[r]]).Where(t => t.Tier == "c"))
                if (index.TryGetValue(county, out int c)) owner[c] = r;

        var result = new Dictionary<int, HashSet<int>>(roots.Count);
        for (int r = 0; r < roots.Count; r++) result[r] = [];

        foreach (var (county, adjacent) in countyAdjacency)
        {
            if (!owner.TryGetValue(county, out int a)) continue;
            foreach (int other in adjacent)
            {
                if (!owner.TryGetValue(other, out int b) || a == b) continue;
                result[a].Add(b);
                result[b].Add(a);
            }
        }

        return result;
    }
}
