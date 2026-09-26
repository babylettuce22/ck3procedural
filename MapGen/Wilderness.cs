using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.World;

namespace Ck3MapGen.MapGen;

/// <summary>
/// Which counties nobody lives in.
///
/// This runs between development and cultures, and that position is forced from both sides: it
/// needs development (the strongest single signal for where people are not) and everything after it
/// needs to know which counties to skip. A wilderness county is not a county with an unusual ruler —
/// it has no ruler, no culture of its own, no government and no holdings, so cultures, faiths,
/// governments, realms and the history writers all have to agree to leave it alone. One set, passed
/// down, is what makes that agreement checkable.
///
/// Placement is deliberately two passes rather than one ranking. Scoring every county and taking the
/// worst N produces single wild counties scattered through settled land, which reads as a generation
/// fault; the second pass grows them into runs and gives back anything too small to look intended.
/// The map should say "the north is empty", not "county 1,447 is empty".
/// </summary>
public sealed class WildernessMap
{
    /// <summary>
    /// The dummy every wilderness county is held by.
    ///
    /// One character for the whole map rather than one per county: the holder exists only so the
    /// titles are not ownerless, and CK3 is happy with a single character holding thousands of
    /// counties as long as its trait says it may (see <c>domain_limit</c> in the wilderness trait).
    /// The scripts in BaseFilesToCopy find it by <c>has_trait = wilderness</c> and never by id, so
    /// this name is ours to choose and is not part of any contract with them.
    /// </summary>
    public const string HolderId = "gen_wilderness_holder";

    /// <summary>
    /// A titular kingdom the dummy holds, purely so its realm has a name.
    ///
    /// Without it the dummy's primary title is whichever county it happens to hold first, so every
    /// unsettled county on the map is labelled with one arbitrary county's name — "Breostdon" over
    /// the whole northern waste. A ruler's realm takes its name from their primary title, and a
    /// titular kingdom outranks every county, so this becomes it.
    ///
    /// Landless, in the vanilla sense: it has no de jure counties and exists only to be held. That
    /// is the same device vanilla uses for head-of-faith titles like <c>k_orthodox</c>, and the
    /// generator already emits those, so this needs no new machinery.
    ///
    /// The key is referenced by the localisation in BaseFilesToCopy/Wilderness — the one place a
    /// static file names something the generator defines. See the note beside it there.
    /// </summary>
    public const string TitleKey = "k_gen_wilderness";

    /// <summary>
    /// The titular kingdom the SECOND dummy holds, for the counties that were settled once.
    ///
    /// Its whole reason to exist is the realm name. A realm is named after its holder's primary
    /// title, so ruins seated on the wilderness dummy would read as "the Wilderness" on the map and
    /// in every tooltip — which is the one thing a ruin is not. A second titular kingdom, and a
    /// second character to hold it, is the cheapest way to make the distinction the player can see.
    ///
    /// The dummy behind it carries the SAME <c>wilderness</c> trait and the SAME
    /// <c>wilderness_government</c> as the first. That is not laziness: roughly thirty triggers,
    /// effects and one portrait modifier in BaseFilesToCopy/Wilderness ask for that trait or that
    /// government's flag, and a ruins holder wearing anything else would fail every one of them —
    /// starting with the portrait hide, which would put a face on the map. The trait file says as
    /// much in its own header: a second dummy can be added "without any of this knowing".
    ///
    /// One consequence, and it is load-bearing: two characters now answer
    /// <c>has_trait = wilderness</c>, so anything that has to find a SPECIFIC dummy must ask which
    /// title it holds. <c>abandon_county_effect</c> does exactly that — see the note there.
    /// </summary>
    public const string RuinsTitleKey = "k_gen_ruins";

    /// <summary>The dummy that holds the ruins. Same trait and government as the wilderness one.</summary>
    public const string RuinsHolderId = "gen_ruins_holder";

    private readonly HashSet<Title> counties;
    private readonly HashSet<Title> ruined;

    internal WildernessMap(HashSet<Title> counties) : this(counties, [], false) { }

    internal WildernessMap(HashSet<Title> counties, HashSet<Title> ruined, bool ruinsEnabled)
    {
        this.counties = counties;
        this.ruined = ruined;
        RuinsEnabled = ruinsEnabled;
    }

    /// <summary>
    /// Whether the ruins system is shipping at all, which is NOT the same question as whether any
    /// county starts ruined.
    ///
    /// <see cref="Config.MapConfig.RuinsShare"/> defaults to zero and that is the intended setting:
    /// the world begins whole and ruins are something that happens in play. The dummy and its
    /// titular title must still exist on day one, because a county that falls in year forty needs
    /// somewhere to go and a landed title cannot be minted at runtime. So the writers ask this,
    /// never <c>RuinCount &gt; 0</c>.
    ///
    /// It lives on the map rather than being read from config at each writer because
    /// <see cref="Emit.WorldOverwrite"/> re-runs the title writer from a stored map long after the
    /// config that produced it is out of scope.
    /// </summary>
    public bool RuinsEnabled { get; }

    /// <summary>
    /// Is this county unsettled — either never settled, or settled once and lost?
    ///
    /// True for ruins as well as wilderness, and every caller but three wants it that way. A ruined
    /// county has no ruler, no culture of its own, no government and no holdings, so cultures,
    /// faiths, governments, realms and the history writers all have to leave it alone for exactly
    /// the reasons they leave wilderness alone. Folding the two together here is what stops each of
    /// those from growing a second case it would only get wrong.
    ///
    /// The three that do care — the two titular titles and the title history that seats counties on
    /// one dummy or the other — read <see cref="Unsettled"/> and <see cref="Ruins"/> instead.
    /// </summary>
    public bool Contains(Title county) => counties.Contains(county) || ruined.Contains(county);

    /// <summary>Was this county settled once? A subset of <see cref="Counties"/>.</summary>
    public bool IsRuin(Title county) => ruined.Contains(county);

    /// <summary>How many counties nobody holds, ruins included.</summary>
    public int Count => counties.Count + ruined.Count;

    /// <summary>How many of those were somebody's once.</summary>
    public int RuinCount => ruined.Count;

    /// <summary>Every county nobody holds, for the writers that have to enumerate them.</summary>
    public IEnumerable<Title> Counties => counties.Concat(ruined);

    /// <summary>Only the counties nobody has ever held. Seated on the wilderness dummy.</summary>
    public IEnumerable<Title> Unsettled => counties;

    /// <summary>Only the counties somebody held once. Seated on the ruins dummy.</summary>
    public IEnumerable<Title> Ruins => ruined;

    /// <summary>Nothing is wilderness. Used when the feature is switched off.</summary>
    public static WildernessMap Empty => new([]);

    /// <summary>
    /// This map after a history: <paramref name="settled"/> taken out of the wild and
    /// <paramref name="fallen"/> put back in as ruins. Order is kept — the survivors in the order
    /// they stood, fallen counties after the old ruins by index — because the writers that name a
    /// titular capital take the first.
    /// </summary>
    public WildernessMap After(IReadOnlySet<Title> settled, IReadOnlySet<Title> fallen)
    {
        var unsettled = new HashSet<Title>(counties.Where(c => !settled.Contains(c) && !fallen.Contains(c)));
        var ruins = new HashSet<Title>(ruined.Where(c => !settled.Contains(c)));
        foreach (var county in fallen.OrderBy(c => c.Index)) ruins.Add(county);
        return new WildernessMap(unsettled, ruins, RuinsEnabled);
    }
}

public static class Wilderness
{
    /// <summary>
    /// How unliveable each terrain is, 0 (prime farmland) to 1 (nobody lives here).
    ///
    /// These are not the same numbers as <see cref="Development"/>'s terrain weighting even though
    /// they rank similarly, and they should not be merged with it. Development asks "how rich is
    /// this county"; this asks "would anyone have bothered at all". Hills are poor but settled
    /// everywhere in the real world; arctic is not poor so much as empty. The two questions
    /// diverge most exactly where this matters.
    /// </summary>
    private static double Hostility(TerrainClass terrain) => terrain switch
    {
        TerrainClass.Arctic => 1.00,
        TerrainClass.Mountains => 0.85,
        TerrainClass.DesertMountains => 0.85,
        TerrainClass.Desert => 0.80,
        TerrainClass.Jungle => 0.65,
        TerrainClass.Wetlands => 0.60,
        TerrainClass.Taiga => 0.55,
        TerrainClass.Steppe => 0.35,
        TerrainClass.Forest => 0.30,
        TerrainClass.Drylands => 0.30,
        TerrainClass.Hills => 0.25,
        TerrainClass.Beach => 0.15,
        TerrainClass.Floodplains => 0.05,
        TerrainClass.Plains => 0.05,
        TerrainClass.Farmlands => 0.00,
        _ => 0.20,
    };

    /// <summary>
    /// Picks the unsettled counties.
    /// </summary>
    /// <summary>
    /// Wilderness straight off the export: the counties standing on ground Azgaar gave to no state.
    ///
    /// Better than the habitability heuristic below whenever there is an export, because it is not a
    /// guess. Azgaar already decided which ground nobody settled, and the heuristic — which picks
    /// remote, poor, edge-of-the-map counties — would otherwise carve wilderness out of the middle
    /// of a country the export drew as inhabited.
    ///
    /// Returns null rather than an empty map when the export claims everything, so the caller falls
    /// back to generating wilderness instead of shipping a map with none.
    /// </summary>
    private static WildernessMap? FromExport(List<Title> counties, AzgaarImport azgaar)
    {
        var unclaimed = new List<Title>();

        foreach (var county in counties)
        {
            int total = 0, ownerless = 0;

            foreach (var barony in county.Children)
            {
                if (barony.ProvinceId < 1) continue;
                total++;
                if (azgaar.StateOfBarony(barony.ProvinceId) <= 0) ownerless++;
            }

            // A simple majority, since a county straddling a border is partly claimed by
            // construction and only the ones mostly outside every state are truly wild.
            if (total > 0 && ownerless * 2 > total) unclaimed.Add(county);
        }

        if (unclaimed.Count == 0) return null;

        Console.WriteLine($"  wilderness: {unclaimed.Count} counties on ground azgaar left unclaimed " +
                          $"({100.0 * unclaimed.Count / counties.Count:F1} % of counties)");

        return new WildernessMap([.. unclaimed]);
    }

    public static WildernessMap Build(List<Title> counties, ProvinceMap provinces, int[] order,
        int landCount, TerrainClass[] provinceTerrain, Dictionary<Title, int> development,
        MapConfig cfg, Rng rng, AzgaarImport? azgaar = null)
    {
        if (!cfg.EnableWilderness || counties.Count == 0) return WildernessMap.Empty;

        var wild = Choose(counties, provinces, order, landCount, provinceTerrain, development,
                          cfg, rng, azgaar);

        return SeedRuins(wild, counties, cfg, rng);
    }

    /// <summary>
    /// Scatters the counties that start already ruined, and folds them into the wilderness map.
    ///
    /// Drawn uniformly from everything the wilderness pass did NOT take, and that is the whole
    /// difference between the two placements. Wilderness is scored and grown into regions because
    /// unsettled land has to look like land nobody wanted; a ruin makes the opposite claim —
    /// somebody wanted this ground enough to build on it — so it belongs wherever people are, and
    /// the terrain bias, the edge bias, the realm-interior guard and the minimum clump size are all
    /// deliberately absent. A lone ruined county inside a settled kingdom is the point of the
    /// feature, where a lone WILD county there would read as a generation fault.
    ///
    /// Running here rather than as its own pipeline stage is what makes the rest of the generator
    /// need no changes at all. A ruin is unsettled, so realms will not seat a ruler on it, world
    /// centres will not pick it and the province writer gives it a wilderness holding — every one of
    /// those for free, because they all ask <see cref="WildernessMap.Contains"/> and it answers yes.
    /// The exclusions this would otherwise have to enumerate are the ones wilderness already gets.
    /// </summary>
    private static WildernessMap SeedRuins(WildernessMap wild, List<Title> counties, MapConfig cfg,
        Rng rng)
    {
        if (!cfg.EnableRuins) return wild;

        var ruined = new HashSet<Title>();

        // Rounds DOWN, so a share too small to reach one county on this map seeds none rather than
        // silently promoting itself to one. Unlike wilderness there is no floor of one: a world
        // that starts whole is the default and is not a broken world, because the dummy and its
        // title are written on EnableRuins alone and stand ready with nothing on them.
        int target = (int)(cfg.RuinsShare * counties.Count);
        var pool = counties.Where(c => !wild.Contains(c)).ToList();

        for (int i = 0; i < target && pool.Count > 0; i++)
        {
            int at = rng.Int(0, pool.Count - 1);
            ruined.Add(pool[at]);
            pool.RemoveAt(at);
        }

        Console.WriteLine(ruined.Count == 0
            ? "  ruins: none at the start date; the system ships and the holder stands empty"
            : $"  ruins: {ruined.Count} counties scattered "
              + $"({(double)ruined.Count / counties.Count:P1} of the map, target {cfg.RuinsShare:P0})");

        return new WildernessMap([.. wild.Unsettled], ruined, ruinsEnabled: true);
    }

    private static WildernessMap Choose(List<Title> counties, ProvinceMap provinces, int[] order,
        int landCount, TerrainClass[] provinceTerrain, Dictionary<Title, int> development,
        MapConfig cfg, Rng rng, AzgaarImport? azgaar = null)
    {
        if (azgaar is not null && FromExport(counties, azgaar) is { } imported) return imported;

        var (neighbours, centroid) = CountyGraph(counties, provinces, order, landCount);

        // --- Pass 1: score every county -------------------------------------------------------
        //
        // How unliveable the ground is comes first, and position only orders counties that are
        // about as unliveable as each other. The two used to be summed at equal weight, and the
        // position half — edge of the map, border of a kingdom — then decided placement on its own:
        // it is highest along thin one-county strips, so the best-scoring counties lay scattered
        // along borders, every clump grown from them was a runt, and a map asked for 12 % delivered
        // 2 % of a wilderness that did not follow the terrain at all. Unliveable ground comes in
        // regions — ice caps, deserts, ranges — so ranking on it first is also what makes clumps.
        double edgeWeight = Math.Abs(cfg.WildernessEdgeBias);
        bool towardEdge = cfg.WildernessEdgeBias >= 0;
        int richest = Math.Max(1, counties.Max(c => development.GetValueOrDefault(c)));

        var score = new double[counties.Count];
        for (int i = 0; i < counties.Count; i++)
        {
            double edgeness = Edgeness(centroid[i]);
            double placement = towardEdge ? edgeness : 1.0 - edgeness;

            // Measures edge distance from both kingdom and empire borders to prevent
            // bisecting multi-kingdom empires.
            double interior = Interiority(i, neighbours, counties);

            double position = edgeWeight * placement - cfg.WildernessAvoidRealmInteriors * interior;

            score[i] = cfg.WildernessTerrainWeight * Unliveable(counties[i], provinceTerrain, development, richest)
                     + PositionWeight * position
                     + rng.Decimal(0.0, 0.05);
        }

        // --- Pass 2: grow clumps from the worst ground ------------------------------------------
        //
        // Clumps grow only through ground inside a pool of the worst-scoring counties, and a runt
        // is given back without counting against the target. When the pool runs out of seeds that
        // grow into regions, it widens and the search runs again, so the share asked for is the
        // share delivered whenever the map has room for it.
        int target = Math.Max(1, (int)Math.Round(cfg.WildernessShare * counties.Count));
        int minClump = Math.Max(1, cfg.WildernessMinClump);
        int budget = Math.Max(target, (int)Math.Round(target * 1.35));

        var ranked = Enumerable.Range(0, counties.Count).OrderByDescending(i => score[i]).ToList();

        var accepted = new List<List<int>>();
        var taken = new HashSet<int>();
        List<int>? largestRunt = null;
        int refused = 0, absorbed = 0, runts = 0;

        for (int pool = Math.Min(counties.Count, target * 2); ; pool = Math.Min(counties.Count, pool * 2))
        {
            double floor = score[ranked[pool - 1]];
            var tried = new HashSet<int>();

            foreach (int seed in ranked.Take(pool))
            {
                if (taken.Count >= target) break;
                if (taken.Contains(seed) || tried.Contains(seed)) continue;

                // Never cut the last region below the smallest one that reads as a region.
                int room = Math.Max(target - taken.Count, minClump);
                var clump = Grow(seed, room, floor, score, neighbours, taken);
                foreach (int i in clump) tried.Add(i);

                // A runt is a small clump with settled land around it. One that fills its island,
                // or touches wilderness already taken, is not one: an empty island reads as
                // nobody's, and a county beside a region is part of it.
                if (clump.Count < minClump && BordersSettled(clump, neighbours, taken))
                {
                    runts++;
                    if (largestRunt is null || clump.Count > largestRunt.Count) largestRunt = clump;
                    continue;
                }

                // --- Give back the ones that cut a realm in half --------------------------------
                var trial = new HashSet<int>(taken);
                foreach (int i in clump) trial.Add(i);

                var stranded = StrandedBy(trial, neighbours, counties);

                // Only refuse a clump if it causes runaway stranding that exceeds budget
                if (stranded.Count > clump.Count * 2 && taken.Count + clump.Count + stranded.Count > budget)
                {
                    refused++;
                    continue;
                }

                accepted.Add(clump);
                foreach (int i in clump) taken.Add(i);

                if (stranded.Count > 0 && taken.Count + stranded.Count <= budget)
                {
                    accepted[^1].AddRange(stranded);
                    foreach (int i in stranded) taken.Add(i);
                    absorbed += stranded.Count;
                }
            }

            if (taken.Count >= target || pool == counties.Count) break;
        }

        if (accepted.Count == 0 && largestRunt is not null)
            accepted = [largestRunt];

        var result = new HashSet<Title>();
        foreach (var clump in accepted)
            foreach (int i in clump)
                result.Add(counties[i]);

        Console.WriteLine($"  wilderness: {result.Count} counties in {accepted.Count} regions "
            + $"({(double)result.Count / counties.Count:P1} of the map, target {cfg.WildernessShare:P0})"
            + (absorbed > 0 ? $", {absorbed} absorbed to keep titles whole" : "")
            + (refused > 0 ? $", {refused} regions refused on budget" : "")
            + (runts > 0 ? $", {runts} runts given back" : ""));
        Console.WriteLine($"    terrain under it: {TerrainShares(result, provinceTerrain)}");

        return new WildernessMap(result);
    }

    /// <summary>
    /// How far position can move a county's score at full bias, against terrain's 0..1 times
    /// <see cref="Config.MapConfig.WildernessTerrainWeight"/>. At the default knobs that is about
    /// one step of <see cref="Hostility"/> — taiga against jungle, forest against steppe — so the
    /// edge of the map and a kingdom's border choose between similar ground and never put
    /// wilderness on farmland while ice or desert is left settled.
    /// </summary>
    private const double PositionWeight = 0.25;

    /// <summary>
    /// How unliveable a county is, 0 to 1: its terrain's <see cref="Hostility"/> mostly, and how
    /// little its ground supports settlement — its development against the richest county's —
    /// for the rest. Development already reads the terrain, but it also reads what terrain does
    /// not: the coast and the rivers that make a hard county a lived-in one.
    /// </summary>
    private static double Unliveable(Title county, TerrainClass[] provinceTerrain,
        Dictionary<Title, int> development, int richest)
        => 0.7 * MeanHostility(county, provinceTerrain)
         + 0.3 * (1.0 - (double)development.GetValueOrDefault(county) / richest);

    /// <summary>
    /// A clump grown from <paramref name="seed"/> through counties scoring at least
    /// <paramref name="floor"/> that nobody has taken, worst ground first, up to
    /// <paramref name="room"/> counties.
    /// </summary>
    private static List<int> Grow(int seed, int room, double floor, double[] score, List<int>[] neighbours,
        HashSet<int> taken)
    {
        var clump = new List<int>();
        var inClump = new HashSet<int>();
        var frontier = new PriorityQueue<int, double>();
        frontier.Enqueue(seed, -score[seed]);

        while (frontier.Count > 0 && clump.Count < room)
        {
            int at = frontier.Dequeue();
            if (!inClump.Add(at)) continue;
            clump.Add(at);

            foreach (int next in neighbours[at])
                if (!taken.Contains(next) && !inClump.Contains(next) && score[next] >= floor)
                    frontier.Enqueue(next, -score[next]);
        }

        return clump;
    }

    /// <summary>Whether any county of <paramref name="clump"/> has a land neighbour that is neither in it nor already wild.</summary>
    private static bool BordersSettled(List<int> clump, List<int>[] neighbours, HashSet<int> taken)
        => clump.Any(i => neighbours[i].Any(n => !taken.Contains(n) && !clump.Contains(n)));

    /// <summary>The terrain classes under the wilderness, by share of its baronies, for the log.</summary>
    private static string TerrainShares(HashSet<Title> wild, TerrainClass[] provinceTerrain)
    {
        var ids = wild.SelectMany(c => c.Children).Select(b => b.ProvinceId)
            .Where(id => id > 0 && id < provinceTerrain.Length).ToList();
        if (ids.Count == 0) return "none";
        return string.Join(", ", ids.GroupBy(id => provinceTerrain[id]).OrderByDescending(g => g.Count())
            .Select(g => $"{g.Key} {100.0 * g.Count() / ids.Count:F0}%"));
    }

    /// <summary>
    /// The share of a county's neighbours that answer to the same kingdom/empire it does.
    /// </summary>
    private static double Interiority(int index, List<int>[] neighbours, List<Title> counties)
    {
        var myKingdom = Kingdom(counties[index]);
        var myEmpire = Empire(counties[index]);
        if (neighbours[index].Count == 0) return 0;

        double kRatio = myKingdom != null
            ? (double)neighbours[index].Count(n => ReferenceEquals(Kingdom(counties[n]), myKingdom)) / neighbours[index].Count
            : 0.0;
        double eRatio = myEmpire != null
            ? (double)neighbours[index].Count(n => ReferenceEquals(Empire(counties[n]), myEmpire)) / neighbours[index].Count
            : 0.0;

        return Math.Max(kRatio, eRatio * 0.85);
    }

    /// <summary>
    /// Would making this clump wilderness leave some de jure title in two disconnected pieces?
    ///
    /// Only pieces the wilderness itself cuts off count. A title that already spans the sea — every
    /// kingdom on an archipelago map does — is in pieces before any county goes wild, and counting
    /// those pieces used to make any clump in such a kingdom "strand" its other islands: regions
    /// were refused for it, and whole settled islands were absorbed as wilderness to keep a title
    /// "whole" that the wilderness had never touched.
    /// </summary>
    private static HashSet<int> StrandedBy(HashSet<int> wild, List<int>[] neighbours,
        List<Title> counties)
    {
        var stranded = new HashSet<int>();

        for (int pass = 0; pass < 8; pass++)
        {
            var before = stranded.Count;

            var titles = new HashSet<Title>();
            foreach (int i in wild.Concat(stranded))
            {
                if (counties[i].Parent is { } duchy) titles.Add(duchy);
                if (Kingdom(counties[i]) is { } kingdom) titles.Add(kingdom);
            }

            foreach (var title in titles)
            {
                var all = new HashSet<int>();
                for (int i = 0; i < counties.Count; i++)
                    if (ReferenceEquals(counties[i].Parent, title)
                        || ReferenceEquals(Kingdom(counties[i]), title))
                        all.Add(i);

                var members = all.Where(i => !wild.Contains(i) && !stranded.Contains(i)).ToHashSet();
                if (members.Count <= 1) continue;

                // Each piece of the title as generated, then the pieces of what is left of it.
                foreach (var whole in Pieces(all, all, neighbours))
                {
                    var left = whole.Where(members.Contains).ToHashSet();
                    var pieces = Pieces(left, left, neighbours);
                    if (pieces.Count <= 1) continue;

                    foreach (var piece in pieces.OrderByDescending(p => p.Count).Skip(1))
                        foreach (int i in piece)
                            stranded.Add(i);
                }
            }

            if (stranded.Count == before) break;
        }

        return stranded;
    }

    /// <summary>The connected pieces of <paramref name="starts"/>, walking only through <paramref name="within"/>.</summary>
    private static List<List<int>> Pieces(IEnumerable<int> starts, HashSet<int> within, List<int>[] neighbours)
    {
        var pieces = new List<List<int>>();
        var seen = new HashSet<int>();

        foreach (int start in starts)
        {
            if (!seen.Add(start)) continue;

            var piece = new List<int> { start };
            var queue = new Queue<int>();
            queue.Enqueue(start);

            while (queue.Count > 0)
                foreach (int next in neighbours[queue.Dequeue()])
                    if (within.Contains(next) && seen.Add(next))
                    {
                        piece.Add(next);
                        queue.Enqueue(next);
                    }

            pieces.Add(piece);
        }

        return pieces;
    }

    /// <summary>A county's de jure kingdom, or null if the tree is shallower than that.</summary>
    private static Title? Kingdom(Title county) => county.Parent?.Parent;

    /// <summary>A county's de jure empire, or null if the tree is shallower than that.</summary>
    private static Title? Empire(Title county) => county.Parent?.Parent?.Parent;

    /// <summary>Mean hostility over a county's baronies.</summary>
    private static double MeanHostility(Title county, TerrainClass[] provinceTerrain)
    {
        double total = 0;
        int counted = 0;

        foreach (var barony in county.Children)
        {
            int id = barony.ProvinceId;
            if (id <= 0 || id >= provinceTerrain.Length) continue;
            total += Hostility(provinceTerrain[id]);
            counted++;
        }

        return counted == 0 ? 0.2 : total / counted;
    }

    /// <summary>
    /// How close to the rim of the map a point sits, 0 at the centre and 1 at any edge.
    /// </summary>
    private static double Edgeness((double X, double Y) position)
        => Math.Max(Math.Abs(position.X - 0.5), Math.Abs(position.Y - 0.5)) * 2.0;

    /// <summary>
    /// County adjacency, plus each county's centre in normalised 0-1 map coordinates.
    /// </summary>
    private static (List<int>[] Neighbours, (double X, double Y)[] Centroid) CountyGraph(
        List<Title> counties, ProvinceMap provinces, int[] order, int landCount)
    {
        var neighbours = CountyNetwork.Neighbours(counties, provinces, order, landCount);
        var seedOfProvince = CountyNetwork.SeedOfProvince(order, landCount);

        var centroid = new (double X, double Y)[counties.Count];
        for (int i = 0; i < counties.Count; i++)
        {
            double x = 0, y = 0;
            int counted = 0;

            foreach (var barony in counties[i].Children)
            {
                if (!CountyNetwork.Covers(seedOfProvince, barony.ProvinceId)) continue;
                var seed = provinces.Seeds[seedOfProvince[barony.ProvinceId]];
                x += seed.X;
                y += seed.Y;
                counted++;
            }

            centroid[i] = counted == 0
                ? (0.5, 0.5)
                : (x / counted / provinces.Width, y / counted / provinces.Height);
        }

        return (neighbours, centroid);
    }
}