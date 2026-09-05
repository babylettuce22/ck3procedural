using Ck3MapGen.Config;

namespace Ck3MapGen.MapGen;

public enum CrossingKind { Strait, River }

/// <summary>One entry for map_data/adjacencies.csv: two baronies that face each other across water.</summary>
public sealed class Crossing
{
    public required int From { get; init; }
    public required int To { get; init; }

    /// <summary>The water province the crossing passes through; it touches both shores.</summary>
    public required int Through { get; init; }

    public required CrossingKind Kind { get; init; }

    /// <summary>Shore points either side, in province-raster pixels with y from the top.</summary>
    public required (int X, int Y) Start { get; init; }
    public required (int X, int Y) Stop { get; init; }

    /// <summary>The water's width at the crossing, in pixels.</summary>
    public required int Width { get; init; }
}

/// <summary>
/// The straits and river crossings a generated map has.
///
/// Vanilla's adjacencies.csv declares about 170 sea straits and 180 large-river crossings, and
/// without them two provinces that face each other across a narrow water are not adjacent at
/// all: an army has to sail, and a road cannot exist. The generator shipped an empty stub, so
/// on a generated map nobody has ever crossed the Øresund, and every duchy on the far bank of a
/// major river has been a separate landmass. This finds the crossings the same way the title
/// builder finds its realm bridges — a flood out from every shore — but keeps the two shore
/// points and the water province between them, which is what the file needs.
/// </summary>
public sealed class CrossingMap
{
    public required IReadOnlyList<Crossing> Crossings { get; init; }

    /// <summary>Crossings by either shore province, for a graph that wants to walk them.</summary>
    public required IReadOnlyDictionary<int, List<Crossing>> ByProvince { get; init; }

    public int Straits => Crossings.Count(c => c.Kind == CrossingKind.Strait);
    public int Rivers => Crossings.Count(c => c.Kind == CrossingKind.River);

    public static CrossingMap Empty => new() { Crossings = [], ByProvince = new Dictionary<int, List<Crossing>>() };
}

public static class Crossings
{
    /// <summary>How much wider than a strait a river may be and still be crossed.</summary>
    public const int RiverWidthFactor = 3;

    private static readonly (int Dx, int Dy)[] Neighbourhood =
        [(-1, 0), (1, 0), (0, -1), (0, 1), (-1, -1), (1, -1), (-1, 1), (1, 1)];

    /// <summary>
    /// Finds every crossing narrower than <see cref="MapConfig.StraitPixelsAtVanilla"/> scaled to
    /// this map, one per pair of counties, the narrowest where several baronies face each other.
    /// Pairs that already touch by land are left out: a crossing beside a land border is a
    /// second border.
    /// </summary>
    public static CrossingMap Build(List<Title> empires, ProvinceMap provinces, int[] order,
        int baronyCount, MapConfig cfg)
    {
        int maxWidth = (int)Math.Round(cfg.Scaled(cfg.StraitPixelsAtVanilla));
        if (maxWidth <= 0) return CrossingMap.Empty;

        var countyOf = new Dictionary<int, Title>();
        foreach (var county in Titles.Flatten(empires).Where(t => t.Tier == "c"))
            foreach (var b in county.Children)
                if (b.ProvinceId >= 1 && b.ProvinceId <= baronyCount) countyOf[b.ProvinceId] = county;

        var landAdjacency = Titles.BuildAdjacency(provinces, baronyCount, order);

        // Which water provinces touch which baronies, so a crossing can be checked to pass
        // through a province that borders both its shores — the engine's requirement.
        var touches = new Dictionary<int, HashSet<int>>();
        int w = provinces.Width, h = provinces.Height;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int cell = y * w + x;
                int label = provinces.Label[cell];
                if (x + 1 < w) Touch(label, provinces.Label[cell + 1]);
                if (y + 1 < h) Touch(label, provinces.Label[cell + w]);
            }
        }

        void Touch(int a, int b)
        {
            if (a == b) return;
            int ia = order[a], ib = order[b];
            bool landA = ia >= 1 && ia <= baronyCount, landB = ib >= 1 && ib <= baronyCount;
            if (landA == landB) return;
            int land = landA ? ia : ib;
            var water = provinces.Seeds[landA ? b : a];
            if (water.IsLand) return;
            int waterId = landA ? ib : ia;
            if (!touches.TryGetValue(waterId, out var set)) touches[waterId] = set = [];
            set.Add(land);
        }

        // A river is bridged or forded whatever its width — vanilla crosses the Nile and the
        // Danube — where a strait that wide is a sea voyage. So rivers get a wider allowance,
        // enough for the broad lower reaches a generated river carves.
        var found = new List<Crossing>();
        found.AddRange(Flood(provinces, order, baronyCount, maxWidth, river: false, touches));
        found.AddRange(Flood(provinces, order, baronyCount, maxWidth * RiverWidthFactor, river: true, touches));

        // One per county pair, the narrowest; and none where the two already meet by land.
        var best = new Dictionary<(Title, Title, CrossingKind), Crossing>();
        foreach (var c in found)
        {
            if (landAdjacency.TryGetValue(c.From, out var around) && around.Contains(c.To)) continue;
            if (!countyOf.TryGetValue(c.From, out var ca) || !countyOf.TryGetValue(c.To, out var cb)) continue;

            var key = ca.Index <= cb.Index ? (ca, cb, c.Kind) : (cb, ca, c.Kind);
            if (!best.TryGetValue(key, out var have) || c.Width < have.Width) best[key] = c;
        }

        var crossings = best.Values
            .OrderBy(c => c.Kind)
            .ThenBy(c => c.From)
            .ThenBy(c => c.To)
            .ToList();

        var byProvince = new Dictionary<int, List<Crossing>>();
        foreach (var c in crossings)
        {
            if (!byProvince.TryGetValue(c.From, out var a)) byProvince[c.From] = a = [];
            if (!byProvince.TryGetValue(c.To, out var b)) byProvince[c.To] = b = [];
            a.Add(c);
            b.Add(c);
        }

        return new CrossingMap { Crossings = crossings, ByProvince = byProvince };
    }

    /// <summary>
    /// Floods out from every shore over one kind of water, carrying the shore province and the
    /// shore pixel it started from. Where two floods meet, the water between the shores is as
    /// wide as the two distances added, and the meeting cell's province is the one crossed.
    /// </summary>
    private static List<Crossing> Flood(ProvinceMap map, int[] order, int baronyCount, int maxWidth,
        bool river, Dictionary<int, HashSet<int>> touches)
    {
        int width = map.Width, height = map.Height;
        var owner = new int[width * height];
        var origin = new int[width * height];
        var dist = new int[width * height];
        var frontier = new Queue<int>();

        bool IsWater(int cell)
        {
            var seed = map.Seeds[map.Label[cell]];
            return !seed.IsLand && seed.IsMajorRiver == river;
        }

        int Barony(int cell)
        {
            int id = order[map.Label[cell]];
            return map.Seeds[map.Label[cell]].IsLand && id >= 1 && id <= baronyCount ? id : 0;
        }

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int cell = y * width + x;
                if (!IsWater(cell)) continue;

                foreach (var (dx, dy) in Neighbourhood)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;

                    int shore = ny * width + nx;
                    int id = Barony(shore);
                    if (id == 0) continue;

                    owner[cell] = id;
                    origin[cell] = shore;
                    dist[cell] = 1;
                    frontier.Enqueue(cell);
                    break;
                }
            }
        }

        while (frontier.Count > 0)
        {
            int cell = frontier.Dequeue();
            if (dist[cell] >= maxWidth) continue;

            int x = cell % width, y = cell / width;
            foreach (var (dx, dy) in Neighbourhood)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;

                int next = ny * width + nx;
                if (owner[next] != 0 || !IsWater(next)) continue;

                owner[next] = owner[cell];
                origin[next] = origin[cell];
                dist[next] = dist[cell] + 1;
                frontier.Enqueue(next);
            }
        }

        // The narrowest meeting per barony pair.
        var narrowest = new Dictionary<(int, int), (int Width, int OriginA, int OriginB, int Through)>();

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int cell = y * width + x;
                if (owner[cell] == 0) continue;
                if (x + 1 < width) Meet(cell, cell + 1);
                if (y + 1 < height) Meet(cell, cell + width);
            }
        }

        void Meet(int cell, int other)
        {
            int a = owner[cell], b = owner[other];
            if (b == 0 || a == b) return;
            int span = dist[cell] + dist[other];
            if (span > maxWidth) return;

            // The water province crossed has to border both shores; try the cell's own first.
            int through = 0;
            foreach (int candidate in new[] { order[map.Label[cell]], order[map.Label[other]] })
            {
                if (touches.TryGetValue(candidate, out var shores) && shores.Contains(a) && shores.Contains(b))
                { through = candidate; break; }
            }
            if (through == 0) return;

            var key = a < b ? (a, b) : (b, a);
            var (oa, ob) = a < b ? (origin[cell], origin[other]) : (origin[other], origin[cell]);
            if (!narrowest.TryGetValue(key, out var have) || span < have.Width)
                narrowest[key] = (span, oa, ob, through);
        }

        var result = new List<Crossing>();
        foreach (var ((a, b), (span, oa, ob, through)) in narrowest)
        {
            result.Add(new Crossing
            {
                From = a,
                To = b,
                Through = through,
                Kind = river ? CrossingKind.River : CrossingKind.Strait,
                Start = (oa % width, oa / width),
                Stop = (ob % width, ob / width),
                Width = span,
            });
        }

        return result;
    }
}
