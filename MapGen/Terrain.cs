using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.World;

namespace Ck3MapGen.MapGen;

/// <summary>
/// Port of js/mapgen/cleanup.js, js/mapgen/getFeatures.js and js/mapgen/floodfills.js.
///
/// The flood fills are recursive in the JS, which is fine for a 512x256 grid but blows the
/// stack at any real resolution; here they are iterative with an explicit stack. Connectivity
/// is 4-way, matching the original.
/// </summary>
public static class Terrain
{
    // --- cleanup.js ---

    /// <summary>
    /// Port of cleanupWorld(). Removes specks: land with too few land neighbours is drowned,
    /// water with too few water neighbours is raised, lone mountains are cut down.
    /// </summary>
    public static void CleanupWorld(WorldGrid w, MapConfig cfg)
    {
        int sea = cfg.Limits.SeaLevelUpper;
        int mtn = cfg.Limits.Mountains.Lower;
        int removedCoasts = 0, removedWater = 0, removedMountains = 0;

        for (int y = 0; y < w.Height; y++)
        {
            for (int x = 0; x < w.Width; x++)
            {
                int cell = w.Idx(x, y);

                // These are three sequential `if`s in the JS, not `else if`. A cell drowned by
                // the first check is therefore re-tested by the second in the same pass.
                if (w.Elevation[cell] >= sea)
                    removedCoasts += CleanupStrayCell(w, x, y, sea, -1, isWater: false);

                if (w.Elevation[cell] < sea)
                    removedWater += CleanupStrayCell(w, x, y, sea, sea + 1, isWater: true);

                if (w.Elevation[cell] >= mtn)
                    removedMountains += CleanupStrayCell(w, x, y, mtn, mtn - 1, isWater: false);
            }
        }

        Console.WriteLine($"  cleanup: {removedCoasts} coasts, {removedWater} water cells, {removedMountains} mountains removed");
    }

    /// <summary>Port of cleanupStrayCells(). Fewer than 3 like neighbours means the cell is a speck.</summary>
    private static int CleanupStrayCell(WorldGrid w, int x, int y, int limit, int newElevation, bool isWater)
    {
        Span<int> neighbors = stackalloc int[8];
        int count = w.NeighborsOf(x, y, neighbors);

        int similar = 0;
        for (int k = 0; k < count; k++)
        {
            int e = w.Elevation[neighbors[k]];
            if (isWater ? e < limit : e >= limit) similar++;
        }

        if (similar >= 3) return 0;

        int cell = w.Idx(x, y);
        w.Elevation[cell] = newElevation;
        if (newElevation == -1) w.Beach[cell] = false;
        return 1;
    }

    /// <summary>Port of cleanupAll().</summary>
    public static void CleanupAll(WorldGrid w, MapConfig cfg)
    {
        CleanupWorld(w, cfg);
        GetBeaches(w, cfg);
    }

    // --- getFeatures.js ---

    /// <summary>
    /// Port of getBeaches(). Marks land cells touching water as beach, and the water cells
    /// touching land as coastal.
    /// </summary>
    public static void GetBeaches(WorldGrid w, MapConfig cfg)
    {
        int sea = cfg.Limits.SeaLevelUpper;
        Span<int> neighbors = stackalloc int[8];

        for (int y = 0; y < w.Height; y++)
        {
            for (int x = 0; x < w.Width; x++)
            {
                int cell = w.Idx(x, y);
                w.Beach[cell] = false;
                if (w.Elevation[cell] < sea) continue;

                int count = w.NeighborsOf(x, y, neighbors);
                for (int k = 0; k < count; k++)
                {
                    int n = neighbors[k];
                    if (w.Elevation[n] < sea)
                    {
                        w.Coastal[n] = true;
                        w.Beach[cell] = true;
                        break; // the JS stops at the first water neighbour
                    }
                    w.Coastal[n] = false;
                }
            }
        }
    }

    /// <summary>Port of getMountains(). Note the strict `>` — floodFillMountain uses `>=`.</summary>
    public static List<int> GetMountains(WorldGrid w, MapConfig cfg)
        => Collect(w, i => w.Elevation[i] > cfg.Limits.Mountains.Lower);

    public static List<int> GetLakes(WorldGrid w) => Collect(w, i => w.Lake[i]);

    public static List<int> GetTrees(WorldGrid w) => Collect(w, i => w.Tree[i]);

    public static List<int> GetRiverCells(WorldGrid w) => Collect(w, i => w.River[i]);

    /// <summary>
    /// Port of getLand(). Side effect worth knowing about: it clears FloodFilled on every cell,
    /// which is what makes a following flood-fill pass start clean.
    /// </summary>
    public static List<int> GetLand(WorldGrid w, MapConfig cfg)
    {
        Array.Clear(w.FloodFilled);
        return Collect(w, i => w.Elevation[i] > cfg.Limits.SeaLevelUpper);
    }

    private static List<int> Collect(WorldGrid w, Func<int, bool> predicate)
    {
        var result = new List<int>();
        for (int y = 0; y < w.Height; y++)
            for (int x = 0; x < w.Width; x++)
            {
                int i = w.Idx(x, y);
                if (predicate(i)) result.Add(i);
            }
        return result;
    }

    // --- floodfills.js ---

    /// <summary>
    /// Shared iterative 4-connected flood fill. Replaces the JS's mutual recursion; the
    /// `matches` predicate stands in for each variant's elevation/flag test.
    /// </summary>
    private static List<int> Fill(WorldGrid w, int start, Func<int, bool> matches, int[] groupIds, int groupId)
    {
        var cells = new List<int>();
        if (w.FloodFilled[start] || !matches(start)) return cells;

        var stack = new Stack<int>();
        stack.Push(start);

        while (stack.Count > 0)
        {
            int cell = stack.Pop();
            if (w.FloodFilled[cell] || !matches(cell)) continue;

            w.FloodFilled[cell] = true;
            groupIds[cell] = groupId;
            cells.Add(cell);

            int x = w.X(cell), y = w.Y(cell);
            Push(x + 1, y);
            Push(x - 1, y);
            Push(x, y + 1);
            Push(x, y - 1);
        }

        return cells;

        void Push(int x, int y)
        {
            int i = w.At(x, y);
            if (i >= 0 && !w.FloodFilled[i]) stack.Push(i);
        }
    }

    /// <summary>Port of floodFillMountains().</summary>
    public static void FloodFillMountains(WorldGrid w, MapConfig cfg)
    {
        int mtn = cfg.Limits.Mountains.Lower;
        w.Mountains.Clear();

        foreach (int cell in GetMountains(w, cfg))
        {
            if (w.FloodFilled[cell]) continue;
            var group = new CellGroup { Id = w.Mountains.Count };
            group.Cells = Fill(w, cell, i => w.Elevation[i] >= mtn, w.MountainId, group.Id);
            if (group.Cells.Count > 0) w.Mountains.Add(group);
        }
    }

    /// <summary>Port of floodFillTrees().</summary>
    public static void FloodFillTrees(WorldGrid w)
    {
        w.Forests.Clear();

        foreach (int cell in GetTrees(w))
        {
            if (w.FloodFilled[cell]) continue;
            var group = new CellGroup { Id = w.Forests.Count };
            group.Cells = Fill(w, cell, i => w.Tree[i], w.ForestId, group.Id);
            if (group.Cells.Count > 0) w.Forests.Add(group);
        }
    }

    /// <summary>
    /// Port of floodFillContinents(). GetLand clears FloodFilled first, so this always starts
    /// from a clean slate.
    /// </summary>
    public static void FloodFillContinents(WorldGrid w, MapConfig cfg, Rng rng)
    {
        int sea = cfg.Limits.SeaLevelUpper;
        w.Continents.Clear();
        var land = GetLand(w, cfg);

        foreach (int cell in land)
        {
            if (w.FloodFilled[cell]) continue;
            var continent = new Continent
            {
                Id = w.Continents.Count,
                FarthestWest = int.MaxValue,
                FarthestEast = int.MinValue,
                FarthestNorth = int.MinValue,
                FarthestSouth = int.MaxValue,
                MoveX = rng.Int(-1, 1),
                MoveY = rng.Int(-1, 1),
            };
            continent.Cells = Fill(w, cell, i => w.Elevation[i] > sea, w.ContinentId, continent.Id);
            if (continent.Cells.Count == 0) continue;

            foreach (int c in continent.Cells)
            {
                int x = w.X(c), y = w.Y(c);
                if (x < continent.FarthestWest) continent.FarthestWest = x;
                if (x > continent.FarthestEast) continent.FarthestEast = x;
                if (y < continent.FarthestSouth) continent.FarthestSouth = y;
                if (y > continent.FarthestNorth) continent.FarthestNorth = y;
            }
            w.Continents.Add(continent);
        }
    }

    /// <summary>
    /// Port of floodFillRivers(). Flood-fills lake cells into bodies, then reclassifies each
    /// body: if any cell touches the sea the whole body becomes a river, otherwise it stays a
    /// lake. Land beside a body may be flagged as farmland potential.
    /// </summary>
    public static void FloodFillRivers(WorldGrid w, MapConfig cfg, Rng rng)
    {
        int sea = cfg.Limits.SeaLevelUpper;
        w.Waters.Clear();
        Array.Clear(w.FloodFilled);

        foreach (int cell in GetLakes(w))
        {
            if (w.FloodFilled[cell]) continue;
            var body = new WaterBody { Id = w.Waters.Count };
            body.Cells = Fill(w, cell, i => w.Lake[i], w.WaterGroupId, body.Id);
            if (body.Cells.Count > 0) w.Waters.Add(body);
        }

        Span<int> neighbors = stackalloc int[8];
        foreach (var body in w.Waters)
        {
            foreach (int cell in body.Cells)
            {
                int count = w.NeighborsOf(w.X(cell), w.Y(cell), neighbors);
                for (int k = 0; k < count; k++)
                {
                    int n = neighbors[k];
                    if (!w.Lake[n] && w.Elevation[n] > sea)
                    {
                        body.Coasts.Add(n);
                        if (rng.Int(1, 15) < 5) w.FarmlandPotential[n] = true;
                    }
                    if (w.Elevation[n] <= sea)
                    {
                        body.OceanOutlets.Add(n);
                        body.IsRiver = true;
                    }
                }
            }
        }

        foreach (var body in w.Waters)
        {
            foreach (int cell in body.Cells)
            {
                w.Lake[cell] = !body.IsRiver;
                w.River[cell] = body.IsRiver;
            }
        }
    }
}
