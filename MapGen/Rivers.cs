using Ck3MapGen.Config;
using Ck3MapGen.World;

namespace Ck3MapGen.MapGen;

/// <summary>
/// Port of the live half of js/mapgen/rivers.js: rerunRivers() and generateRiver().
///
/// The other course-tracing routine in that file (drawRiver / riversFromHighPoints / hpRivers)
/// is dead — startup() has its `hpRivers()` call commented out and everything downstream of it
/// only draws to a canvas — so it is deliberately not ported.
///
/// Sources are the highest wet land cells, taken in descending elevation order and spaced at
/// least MapConfig.RiversDistance apart. Each course walks strictly downhill through cardinal
/// neighbours; a course that fails to reach water is rolled back entirely.
/// </summary>
public static class Rivers
{
    /// <summary>Directions in generateRiver's order: right, down, left, up.</summary>
    private static readonly (int Dx, int Dy)[] Directions = [(1, 0), (0, 1), (-1, 0), (0, -1)];

    /// <summary>Port of rerunRivers().</summary>
    public static void Rerun(WorldGrid w, MapConfig cfg)
    {
        int sea = cfg.Limits.SeaLevelUpper;
        int hills = cfg.Limits.Hills.Lower;

        w.Rivers.Clear();
        Array.Fill(w.RiverRun, -1);
        Array.Fill(w.RiverObject, WorldGrid.NoGroup);
        Array.Clear(w.River);
        Array.Clear(w.DrawableRiver);
        Array.Clear(w.RiverStartGreen);
        Array.Clear(w.RiverEndRed);
        Array.Clear(w.IsTributary);

        // Highest cells first, so major rivers claim their courses before minor ones.
        var byElevation = new int[w.Count];
        for (int i = 0; i < w.Count; i++) byElevation[i] = i;
        Array.Sort(byElevation, (a, b) => w.Elevation[b].CompareTo(w.Elevation[a]));

        foreach (int cell in byElevation)
        {
            bool wet = w.Moisture[cell] > 100;
            if (w.Elevation[cell] <= hills || !wet) continue;
            if (w.RiverRun[cell] != -1) continue;
            if (w.Elevation[cell] < sea) continue;

            int x = w.X(cell), y = w.Y(cell);
            if (TooCloseToExistingSource(w, cfg, x, y)) continue;

            Generate(w, cfg, cell);
        }
    }

    /// <summary>Port of the distance check in rerunRivers — keeps sources from bunching up.</summary>
    private static bool TooCloseToExistingSource(WorldGrid w, MapConfig cfg, int x, int y)
    {
        foreach (var river in w.Rivers)
        {
            double dx = x - river.StartX;
            double dy = y - river.StartY;
            // getDistance() floors, so the comparison is against the floored distance.
            if ((int)Math.Sqrt(dx * dx + dy * dy) < cfg.RiversDistance) return true;
        }
        return false;
    }

    /// <summary>
    /// Port of generateRiver(). Walks downhill to the lowest legal cardinal neighbour, refusing
    /// any cell that would run the river alongside itself, and rolls the whole course back if it
    /// never reaches water.
    /// </summary>
    private static void Generate(WorldGrid w, MapConfig cfg, int startCell)
    {
        int sea = cfg.Limits.SeaLevelUpper;

        var river = new River
        {
            StartX = w.X(startCell),
            StartY = w.Y(startCell),
        };

        int current = startCell;
        w.RiverStartGreen[current] = true;
        w.RiverRun[current] = 1;
        int riverRun = 1;
        river.Cells.Add(current);

        var visited = new HashSet<int> { current };
        bool reachedOcean = false;
        (int Dx, int Dy)? fromDirection = null;

        while (true)
        {
            int nextCell = -1;
            int minElevation = int.MaxValue;
            (int Dx, int Dy)? incomingDirection = null;

            foreach (var dir in Directions)
            {
                int nx = w.X(current) + dir.Dx;
                int ny = w.Y(current) + dir.Dy;
                if (!w.InBounds(nx, ny)) continue;

                int neighbor = w.Idx(nx, ny);

                // Never double back.
                if (fromDirection is { } from && from.Dx == -dir.Dx && from.Dy == -dir.Dy) continue;
                if (visited.Contains(neighbor)) continue;

                // Reaching water ends the course immediately.
                if (w.Elevation[neighbor] < sea)
                {
                    nextCell = neighbor;
                    reachedOcean = true;
                    incomingDirection = dir;
                    break;
                }

                // Running into an existing river: merge as a tributary, or give up.
                if (w.RiverRun[neighbor] > -1)
                {
                    if (river.Cells.Count < 5)
                    {
                        Rollback(w, river);
                        return;
                    }

                    if (CountOrthogonalRiverNeighbors(w, neighbor, current) < 3)
                    {
                        w.IsTributary[current] = true;
                        river.EndX = w.X(current);
                        river.EndY = w.Y(current);
                        // The JS pushes currentCell a second time here; kept so cell counts and
                        // any downstream indexing match.
                        river.Cells.Add(current);
                        w.RiverStartGreen[river.Cells[0]] = false;
                        river.IsTributary = true;
                        Commit(w, river);
                        return;
                    }

                    Rollback(w, river);
                    return;
                }

                if (CountOrthogonalRiverNeighbors(w, neighbor, current) <= 1
                    && w.Elevation[neighbor] < minElevation
                    && w.RiverRun[neighbor] == -1)
                {
                    minElevation = w.Elevation[neighbor];
                    nextCell = neighbor;
                    incomingDirection = dir;
                }
            }

            if (nextCell < 0) break;

            riverRun++;
            w.RiverRun[nextCell] = riverRun;
            current = nextCell;
            river.Cells.Add(current);
            visited.Add(current);
            fromDirection = incomingDirection;

            if (reachedOcean)
            {
                river.EndX = w.X(current);
                river.EndY = w.Y(current);
                break;
            }
        }

        if (!reachedOcean)
        {
            Rollback(w, river);
            return;
        }

        Commit(w, river);
    }

    /// <summary>
    /// Port of countOrthogonalRiverNeighbors(). The `riverObject` equality test is the subtle
    /// part: cells of the river currently being traced have no river object yet, and neither
    /// does the origin, so any neighbour belonging to the *current* course compares equal and
    /// forces the count to 10000 — which is what stops a river from snaking against itself.
    /// Cells of an already-completed river do have an object, so they only count as 1.
    /// </summary>
    private static int CountOrthogonalRiverNeighbors(WorldGrid w, int cell, int originCell)
    {
        int count = 0;
        foreach (var dir in Directions)
        {
            int nx = w.X(cell) + dir.Dx;
            int ny = w.Y(cell) + dir.Dy;
            if (!w.InBounds(nx, ny)) continue;

            int neighbor = w.Idx(nx, ny);
            if (neighbor == originCell || w.RiverRun[neighbor] <= -1) continue;

            count++;
            if (w.RiverObject[neighbor] == w.RiverObject[originCell]) count = 10000;
        }
        return count;
    }

    /// <summary>Discards a failed course, restoring every cell it touched.</summary>
    private static void Rollback(WorldGrid w, River river)
    {
        foreach (int cell in river.Cells)
        {
            w.RiverRun[cell] = -1;
            w.RiverObject[cell] = WorldGrid.NoGroup;
            w.RiverStartGreen[cell] = false;
            w.IsTributary[cell] = false;
        }
        river.Cells.Clear();
    }

    /// <summary>Accepts a course: stamps river identity onto its cells and registers it.</summary>
    private static void Commit(WorldGrid w, River river)
    {
        river.Id = w.Rivers.Count;
        w.Rivers.Add(river);

        foreach (int cell in river.Cells)
        {
            w.RiverObject[cell] = river.Id;
            w.River[cell] = true;
            w.DrawableRiver[cell] = true;
        }

        if (!river.IsTributary && river.Cells.Count > 0)
            w.RiverEndRed[river.Cells[^1]] = true;
    }
}
