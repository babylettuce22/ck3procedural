namespace Ck3MapGen.MapGen;

/// <summary>
/// Decides where every title's capital is.
///
/// CK3 has no separate notion of a capital for a generated title: a county's seat is the first
/// barony it lists, and a duchy's, kingdom's or empire's capital is the first county under it
/// unless <c>capital =</c> says otherwise. Until this existed, "first" meant the cluster seed
/// from <see cref="Titles.Cluster"/>, which is a shuffled draw — so every castle, wonder,
/// bookmark start and market on the map sat on a random barony of a random county.
///
/// The mechanism is deliberately just a reorder. A dozen writers read <c>Children[0]</c> as the
/// capital, and the province-history writer gives the capital holding to index zero; moving the
/// chosen title to the front makes all of them agree without any of them changing. The explicit
/// <c>capital =</c> field the landed-titles writer adds on top is belt and braces, and is what
/// vanilla does on its own duchies.
///
/// Two passes, because they need different inputs. County seats need only the ground and run
/// before naming, since a seat may take its county's name. Realm capitals need development,
/// which needs the world centres, which need the cultures, so they run after all of that; the
/// names above county do not depend on child order, so nothing is renamed by moving them.
/// </summary>
public static class Capitals
{
    /// <summary>What a coast is worth to a town over the same ground inland.</summary>
    private const double CoastBonus = 0.25;

    /// <summary>What a river is worth at full flow; scales down with the flow.</summary>
    private const double RiverBonus = 0.30;

    /// <summary>A nudge toward the county's middle, to break ties between similar ground.</summary>
    private const double CentralBonus = 0.05;

    /// <summary>
    /// On an imported map the chief burg of a county is its town by definition, and outranks any
    /// reading of the ground.
    /// </summary>
    private const double BurgBonus = 2.0;

    /// <summary>A world centre is its duchy's capital whatever the development table says.</summary>
    private const double WorldCentreBonus = 50;

    /// <summary>An imported state capital is its duchy's capital, and its kingdom's.</summary>
    private const double StateCapitalBonus = 100;

    /// <summary>
    /// Puts the best town site first in every county.
    ///
    /// Fertile flat ground scores highest, from the same table development is drawn from, so the
    /// seat is where the people are; a coast adds a port, a river adds water and a road, and a
    /// small pull toward the county's middle settles ties between similar ground. On an imported
    /// map the barony holding the county's chief burg wins outright.
    /// </summary>
    /// <returns>How many counties had their seat moved.</returns>
    public static int SeatCounties(List<Title> empires, ProvinceMap provinces, int[] order,
        int baronyCount, int landCount, TerrainClass[] provinceTerrain, Drainage? drainage,
        AzgaarImport? azgaar)
    {
        var coastal = new bool[baronyCount + 1];
        var riverside = new bool[baronyCount + 1];
        var peakFlow = new float[baronyCount + 1];
        var position = new (double X, double Y)[baronyCount + 1];

        for (int label = 0; label < order.Length; label++)
        {
            int id = order[label];
            if (id >= 1 && id <= baronyCount && label < provinces.Seeds.Count)
                position[id] = (provinces.Seeds[label].X, provinces.Seeds[label].Y);
        }

        // One pass over the raster for the two facts the seeds cannot give: what water a province
        // touches, and how much flow crosses it. Right and down neighbours are enough — every
        // adjacent pair is seen once from one side or the other.
        int w = provinces.Width, h = provinces.Height;
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                int cell = row + x;
                int label = provinces.Label[cell];
                int id = order[label];
                bool land = id >= 1 && id <= baronyCount;

                if (land && drainage is not null && cell < drainage.Flow.Length && drainage.LandMask[cell] != 0)
                    peakFlow[id] = Math.Max(peakFlow[id], drainage.Flow[cell]);

                if (x + 1 < w) Touch(id, label, land, provinces.Label[cell + 1]);
                if (y + 1 < h) Touch(id, label, land, provinces.Label[cell + w]);
            }
        }

        void Touch(int id, int label, bool land, int otherLabel)
        {
            if (label == otherLabel) return;
            int other = order[otherLabel];
            bool otherLand = other >= 1 && other <= baronyCount;
            if (land == otherLand) return;

            // Exactly one side is a barony; the other is water, wasteland or an impassable. A
            // wasteland is land too, so only water counts, and a major river is a river port
            // rather than a coast.
            int barony = land ? id : other;
            var water = provinces.Seeds[land ? otherLabel : label];
            if (water.IsLand) return;
            if (water.IsMajorRiver) riverside[barony] = true;
            else coastal[barony] = true;
        }

        // Flow is normalised against the strong rivers rather than the strongest one, so a single
        // great river does not make every other stream read as a trickle.
        var flows = new List<float>();
        for (int id = 1; id <= baronyCount; id++) if (peakFlow[id] > 0) flows.Add(peakFlow[id]);
        flows.Sort();
        float reference = flows.Count == 0 ? 1f : flows[(int)(0.9 * (flows.Count - 1))];
        if (reference <= 0) reference = 1f;

        int moved = 0;
        foreach (var county in Titles.Flatten(empires).Where(t => t.Tier == "c"))
        {
            if (county.Children.Count < 2) continue;

            var baronies = county.Children.Where(b => b.Tier == "b").ToList();
            if (baronies.Count < 2) continue;

            double cx = 0, cy = 0;
            int counted = 0;
            foreach (var b in baronies)
                if (b.ProvinceId >= 1 && b.ProvinceId <= baronyCount)
                { cx += position[b.ProvinceId].X; cy += position[b.ProvinceId].Y; counted++; }
            if (counted > 0) { cx /= counted; cy /= counted; }

            double farthest = 1;
            foreach (var b in baronies)
                if (b.ProvinceId >= 1 && b.ProvinceId <= baronyCount)
                    farthest = Math.Max(farthest, Distance(position[b.ProvinceId], (cx, cy)));

            int chiefBurg = azgaar?.For(county)?.Burgs.FirstOrDefault()?.I ?? -1;

            double Score(Title b)
            {
                int id = b.ProvinceId;
                if (id < 1 || id > baronyCount) return double.NegativeInfinity;

                var terrain = id < provinceTerrain.Length ? provinceTerrain[id] : TerrainClass.Plains;
                double score = Development.Support(terrain);

                if (coastal[id]) score += CoastBonus;

                double river = riverside[id] ? 1.0 : Math.Clamp(peakFlow[id] / reference, 0, 1);
                score += RiverBonus * river;

                score += CentralBonus * (1 - Distance(position[id], (cx, cy)) / farthest);

                if (chiefBurg >= 0 && azgaar!.For(b)?.Burgs.Any(burg => burg.I == chiefBurg) == true)
                    score += BurgBonus;

                return score;
            }

            var best = baronies
                .Select(b => (Barony: b, Score: Score(b)))
                .OrderByDescending(p => p.Score)
                .ThenBy(p => p.Barony.ProvinceId)
                .First().Barony;

            // Recorded, never reordered — see Title.Seat for why the list itself must not move.
            county.Seat = best;
            if (!ReferenceEquals(best, county.Children[0])) moved++;
        }

        return moved;
    }

    /// <summary>
    /// Puts the capital first in every duchy, kingdom, empire and the hegemony.
    ///
    /// A duchy's capital is its most developed county, with a world centre or an imported state
    /// capital outranking the table; ties go to the county fewest hops from the rest of the
    /// duchy, so a seat sits among its duchy rather than at its edge. A kingdom's capital is the
    /// best of its duchies' capitals, and so on up.
    /// </summary>
    /// <returns>How many titles above county had their capital moved.</returns>
    public static int SeatRealms(List<Title> empires, Dictionary<Title, int> development,
        ProvinceMap provinces, int[] order, int baronyCount, WorldCenterMap? worldCenters,
        AzgaarImport? azgaar)
    {
        var roots = new List<Title>();
        if (Titles.HegemonyOf(empires) is { } hegemony) roots.Add(hegemony);
        else roots.AddRange(empires);

        var centres = new HashSet<Title>(worldCenters?.Centers.Select(c => c.County) ?? []);

        // County-to-county adjacency, lifted from the barony graph, for the centrality tie-break.
        var countyOf = new Dictionary<int, Title>();
        foreach (var county in Titles.Flatten(roots).Where(t => t.Tier == "c"))
            foreach (var b in county.Children)
                if (b.ProvinceId >= 1 && b.ProvinceId <= baronyCount) countyOf[b.ProvinceId] = county;

        var adjacent = new Dictionary<Title, HashSet<Title>>();
        foreach (var (province, others) in Titles.BuildAdjacency(provinces, baronyCount, order))
        {
            if (!countyOf.TryGetValue(province, out var a)) continue;
            foreach (int other in others)
            {
                if (!countyOf.TryGetValue(other, out var b) || ReferenceEquals(a, b)) continue;
                if (!adjacent.TryGetValue(a, out var set)) adjacent[a] = set = [];
                set.Add(b);
            }
        }

        double CountyScore(Title county)
        {
            double score = development.GetValueOrDefault(county);
            if (centres.Contains(county)) score += WorldCentreBonus;
            if (azgaar?.For(county)?.Burgs.Any(b => b.IsCapital) == true) score += StateCapitalBonus;
            return score;
        }

        int moved = 0;

        // Duchies first, then each tier above reads the capital the tier below just settled.
        foreach (var duchy in Titles.Flatten(roots).Where(t => t.Tier == "d"))
        {
            var counties = duchy.Children.Where(c => c.Tier == "c").ToList();
            if (counties.Count < 2) continue;

            var inside = counties.ToHashSet();
            var best = counties
                .OrderByDescending(CountyScore)
                .ThenBy(c => Eccentricity(c, inside, adjacent))
                .ThenBy(c => c.Index)
                .First();

            if (Seat(duchy, best)) moved++;
        }

        foreach (string tier in new[] { "k", "e", "h" })
        {
            foreach (var title in Titles.Flatten(roots).Where(t => t.Tier == tier))
            {
                var children = title.Children.Where(c => c.Tier != "b" && c.Tier != "c").ToList();
                if (children.Count < 2) continue;

                var best = children
                    .OrderByDescending(c => CapitalCounty(c) is { } seat ? CountyScore(seat) : double.NegativeInfinity)
                    .ThenBy(c => c.Index)
                    .First();

                if (Seat(title, best)) moved++;
            }
        }

        return moved;
    }

    /// <summary>The county a title's capital is in: the capital child, followed down to county tier.</summary>
    public static Title? CapitalCounty(Title title)
    {
        var current = title;
        while (current.Tier != "c")
        {
            if (current.Capital is not { } next) return null;
            current = next;
        }
        return current;
    }

    /// <summary>Records a title's capital child. True when it is not the one CK3 would have defaulted to.</summary>
    private static bool Seat(Title title, Title capital)
    {
        title.Seat = capital;
        return !ReferenceEquals(capital, title.Children[0]);
    }

    /// <summary>
    /// The farthest hop count from a county to any other in the same duchy, walking only through
    /// the duchy. A county nothing can reach — across a strait from the rest — sorts last.
    /// </summary>
    private static int Eccentricity(Title start, HashSet<Title> inside,
        Dictionary<Title, HashSet<Title>> adjacent)
    {
        var dist = new Dictionary<Title, int> { [start] = 0 };
        var queue = new Queue<Title>();
        queue.Enqueue(start);
        int farthest = 0;

        while (queue.Count > 0)
        {
            var at = queue.Dequeue();
            if (!adjacent.TryGetValue(at, out var links)) continue;
            foreach (var next in links)
            {
                if (!inside.Contains(next) || dist.ContainsKey(next)) continue;
                dist[next] = dist[at] + 1;
                farthest = Math.Max(farthest, dist[next]);
                queue.Enqueue(next);
            }
        }

        return dist.Count < inside.Count ? int.MaxValue / 2 + farthest : farthest;
    }

    private static double Distance((double X, double Y) a, (double X, double Y) b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
