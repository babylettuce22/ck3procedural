using Ck3MapGen.Config;
using Ck3MapGen.Core;

namespace Ck3MapGen.World;

/// <summary>
/// The simulation grid. ck2rpg models this as world.map[y][x] holding one object per cell;
/// we use parallel flat arrays instead so the grid stays cache-friendly and can be scaled up
/// well past the JS version's 512x256 default without allocating millions of objects.
///
/// Group ids (mountain/continent/forest/water) are integers here where the JS used random
/// "rgb(r, g, b)" strings as identity — same meaning, cheaper and collision-free.
/// </summary>
public sealed class WorldGrid
{
    public const int NoGroup = -1;

    public readonly int Width;
    public readonly int Height;
    public readonly int Count;

    // --- Terrain ---
    public readonly int[] Elevation;
    public readonly int[] Magma;
    public readonly int[] Moisture;
    public readonly int[] Raindrops;

    // --- Flags ---
    public readonly bool[] Spreading;
    public readonly bool[] Beach;
    public readonly bool[] Coastal;
    public readonly bool[] Desert;
    public readonly bool[] River;
    public readonly bool[] Lake;
    public readonly bool[] Tree;
    public readonly bool[] FloodFilled;
    public readonly bool[] FarmlandPotential;
    public readonly bool[] DrawableRiver;

    // --- Group membership (-1 when unassigned) ---
    public readonly int[] MountainId;
    public readonly int[] ContinentId;
    public readonly int[] WaterGroupId;
    public readonly int[] ForestId;

    // --- River generation state (see MapGen/Rivers.cs) ---
    /// <summary>Distance along its river, or -1 for "not a river cell".</summary>
    public readonly int[] RiverRun;

    /// <summary>
    /// Index into <see cref="Rivers"/>, assigned only once a river is *completed*. Cells of a
    /// river still being traced keep -1, and generateRiver depends on that distinction.
    /// </summary>
    public readonly int[] RiverObject;

    public readonly bool[] RiverStartGreen;
    public readonly bool[] RiverEndRed;
    public readonly bool[] IsTributary;

    /// <summary>Cell indices on a tectonic spreading line (world.tectonics.spreadingLine).</summary>
    public readonly List<int> SpreadingLine = [];

    /// <summary>Column positions of the spreading centres (world.tectonics.spreadingCenters).</summary>
    public readonly List<int> SpreadingCenters = [];

    // --- Flood-filled feature groups (world.mountains / continents / rivers / forests) ---
    public readonly List<CellGroup> Mountains = [];
    public readonly List<Continent> Continents = [];
    public readonly List<WaterBody> Waters = [];
    public readonly List<CellGroup> Forests = [];

    /// <summary>
    /// Generated rivers. The JS stores these in the same `world.rivers` array as the
    /// flood-filled lake bodies above, so whichever pass ran last clobbers the other; keeping
    /// them separate avoids that.
    /// </summary>
    public readonly List<River> Rivers = [];

    // --- Geographical reference lines, in grid space (setGeographicalPoints) ---
    public int Equator;
    public int SteppeTop;
    public int SteppeBottom;
    public int FrostPointTop;
    public int FrostPointBottom;
    public int DesertPointTop;
    public int DesertPointBottom;

    public WorldGrid(int width, int height)
    {
        Width = width;
        Height = height;
        Count = width * height;

        Elevation = new int[Count];
        Magma = new int[Count];
        Moisture = new int[Count];
        Raindrops = new int[Count];

        Spreading = new bool[Count];
        Beach = new bool[Count];
        Coastal = new bool[Count];
        Desert = new bool[Count];
        River = new bool[Count];
        Lake = new bool[Count];
        Tree = new bool[Count];
        FloodFilled = new bool[Count];
        FarmlandPotential = new bool[Count];
        DrawableRiver = new bool[Count];

        MountainId = new int[Count];
        ContinentId = new int[Count];
        WaterGroupId = new int[Count];
        ForestId = new int[Count];
        RiverRun = new int[Count];
        RiverObject = new int[Count];

        RiverStartGreen = new bool[Count];
        RiverEndRed = new bool[Count];
        IsTributary = new bool[Count];

        Array.Fill(MountainId, NoGroup);
        Array.Fill(ContinentId, NoGroup);
        Array.Fill(WaterGroupId, NoGroup);
        Array.Fill(ForestId, NoGroup);
        Array.Fill(RiverRun, -1);
        Array.Fill(RiverObject, NoGroup);
    }

    public int Idx(int x, int y) => y * Width + x;
    public int X(int i) => i % Width;
    public int Y(int i) => i / Width;

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    /// <summary>
    /// Port of xy(x, y). The JS returns the string "edge" out of bounds, which callers then
    /// mutate harmlessly in sloppy mode; here out of bounds is -1 and callers must check.
    /// </summary>
    public int At(int x, int y) => InBounds(x, y) ? y * Width + x : -1;

    /// <summary>
    /// Port of getNeighbors(x, y) — all 8 surrounding cells, in the JS's exact order
    /// (W, E, NE, SW, N, S, NW, SE). Order matters: several ported routines break out of the
    /// loop early. Out-of-bounds neighbours are omitted rather than returned as "edge".
    /// </summary>
    public int NeighborsOf(int x, int y, Span<int> buffer)
    {
        ReadOnlySpan<int> dx = [-1, 1, 1, -1, 0, 0, -1, 1];
        ReadOnlySpan<int> dy = [0, 0, 1, -1, 1, -1, 1, -1];
        int n = 0;
        for (int k = 0; k < 8; k++)
        {
            int i = At(x + dx[k], y + dy[k]);
            if (i >= 0) buffer[n++] = i;
        }
        return n;
    }

    /// <summary>Port of getCardinalNeighbors(x, y) — W, E, S, N.</summary>
    public int CardinalNeighborsOf(int x, int y, Span<int> buffer)
    {
        ReadOnlySpan<int> dx = [-1, 1, 0, 0];
        ReadOnlySpan<int> dy = [0, 0, 1, -1];
        int n = 0;
        for (int k = 0; k < 4; k++)
        {
            int i = At(x + dx[k], y + dy[k]);
            if (i >= 0) buffer[n++] = i;
        }
        return n;
    }

    /// <summary>Port of setGeographicalPoints().</summary>
    public void SetGeographicalPoints()
    {
        Equator = Height / 2;
        SteppeTop = Equator + Height / 8;
        SteppeBottom = Equator - Height / 8;
        FrostPointBottom = Height / 10;
        FrostPointTop = Height - FrostPointBottom;
        DesertPointTop = Height / 2 + Height / 10;
        DesertPointBottom = Height / 2 - Height / 10;
    }

    /// <summary>
    /// Port of createBlankWorld() + createCell(). Every cell starts deep underwater; the
    /// tectonic phase is what raises land.
    /// </summary>
    public static WorldGrid CreateBlank(MapConfig config, Rng rng)
    {
        var world = new WorldGrid(config.WorldWidth, config.WorldHeight);
        config.ResetClimateLimits(rng);
        world.SetGeographicalPoints();
        for (int i = 0; i < world.Count; i++)
            world.Elevation[i] = rng.Int(-254, -200);
        config.ResetVaryRanges(rng);
        return world;
    }
}
