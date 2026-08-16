namespace Ck3MapGen.World;

/// <summary>
/// A flood-filled blob of cells. The JS identifies these with a random "rgb(r, g, b)" string
/// stamped onto every member cell; an integer id is the same thing without the collision risk.
/// </summary>
public class CellGroup
{
    public int Id;
    public List<int> Cells = [];
}

/// <summary>A connected landmass (world.continents), with its bounding box and drift vector.</summary>
public sealed class Continent : CellGroup
{
    public int FarthestWest;
    public int FarthestEast;
    public int FarthestNorth;
    public int FarthestSouth;
    public int MoveX;
    public int MoveY;

    /// <summary>Province ids assigned to this continent, filled in once provinces exist.</summary>
    public List<int> Provinces = [];
}

// WaterBody and River lived here until 2026-08-10. Both were ck2rpg's shapes — a flood-filled
// inland water body that becomes a river if it reaches the sea, and a traced course from a highland
// source down to an outlet — and both were unreferenced by the time they were removed: nothing had
// constructed either since the generator stopped making its own terrain.
