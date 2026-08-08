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

/// <summary>
/// A connected body of inland water (world.rivers). ck2rpg flood-fills lakes and then decides
/// per body whether it reaches the sea: if it does the whole body becomes a river, otherwise it
/// stays a lake.
/// </summary>
public sealed class WaterBody : CellGroup
{
    /// <summary>Land cells bordering the body.</summary>
    public List<int> Coasts = [];

    /// <summary>Sea cells the body drains into.</summary>
    public List<int> OceanOutlets = [];

    public bool IsRiver;
}

/// <summary>
/// A traced river course, from a highland source down to the sea or to a confluence with an
/// existing river. Produced by generateRiver(); only rivers that actually reach water survive.
/// </summary>
public sealed class River
{
    public int Id;

    /// <summary>Cells in flow order. Tributaries repeat their final cell, as the JS does.</summary>
    public List<int> Cells = [];

    public int StartX, StartY;
    public int EndX, EndY;

    /// <summary>True when this river ends by merging into another rather than reaching the sea.</summary>
    public bool IsTributary;
}
