using Ck3MapGen.Config;
using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen;

/// <summary>
/// Everything downstream of the heightmap consumes, and the only thing it consumes.
///
/// The province partition, the title hierarchy, rivers.png, the terrain textures and every emitter
/// read this object and nothing else, which is what lets the heightmap come from anywhere — any
/// program that can write a 16-bit greyscale PNG on CK3's height scale is a valid front end for
/// this one.
///
/// Rivers and lakes are part of the contract rather than something callers derive, because they
/// have to agree with the heightmap pixel for pixel. Deriving them twice from the same field in
/// two places is exactly how the coastline and the province map used to drift apart.
/// </summary>
public sealed class TerrainData
{
    /// <summary>Full heightmap resolution, in the simulation's integer elevation scale.</summary>
    public required float[] Elevation { get; init; }

    /// <summary>Province resolution, the same field downsampled 2:1. Drives the partition.</summary>
    public required float[] ProvinceElevation { get; init; }

    /// <summary>Province resolution, CK3 rivers.png palette index; 255 where there is no river.</summary>
    public required byte[] RiverPixels { get; init; }

    /// <summary>Province resolution, 1 on any river pixel. Input to the terrain classifier.</summary>
    public required byte[] RiverMask { get; init; }

    /// <summary>Province resolution, 1 on an inland lake.</summary>
    public required byte[] LakeMask { get; init; }

    public required List<RiverCourse> Courses { get; init; }

    /// <summary>
    /// Derives everything a full-resolution elevation field implies: the province-resolution copy,
    /// the drainage network, river courses and lakes.
    ///
    /// Deliberately does no erosion and no channel carving. The elevation passed in is taken as
    /// authoritative: the heightmap should come out the other end as the map its author drew.
    /// </summary>
    public static TerrainData FromElevation(float[] elevation, MapConfig cfg, Rng rng)
    {
        int pw = cfg.ProvinceWidth, ph = cfg.ProvinceHeight;
        float sea = cfg.Limits.SeaLevelUpper;

        // Raster.ProvinceElevation, not Field.Downsample: the two grids do not line up on the
        // obvious block, and this is the one place that knows where they do.
        var province = Raster.ProvinceElevation(elevation, cfg);
        var flow = FlowField.Compute(province, pw, ph, sea);

        var courses = RiverNetwork.Extract(flow, province, pw, ph, sea, cfg, rng);
        var rivers = RiverRaster.Draw(courses, pw, ph, cfg);
        var lakes = BuildLakeMask(flow, province, pw, ph, sea, cfg, out int lakeCells);

        Console.WriteLine($"  drainage over {flow.LandCells:N0} land cells: {courses.Count} rivers " +
                          $"over {rivers.RiverPixelCount:N0} pixels, {lakeCells:N0} lake cells");

        return new TerrainData
        {
            Elevation = elevation,
            ProvinceElevation = province,
            RiverPixels = rivers.Pixels,
            RiverMask = rivers.Mask,
            LakeMask = lakes,
            Courses = courses,
        };
    }

    /// <summary>
    /// Depressions the fill had to raise are lakes, keeping only the ones big enough to read as
    /// water at map scale. Feeds terrain classification only — deliberately not rivers.png or the
    /// province partition, because those two and the heightmap have to agree pixel for pixel and a
    /// third source of "this is water" is how they stop agreeing.
    /// </summary>
    internal static byte[] BuildLakeMask(FlowField.Result flow, float[] height, int width, int hgt,
        float sea, MapConfig cfg, out int cells)
    {
        int n = width * hgt;
        var candidate = new bool[n];
        float tolerance = cfg.LakeDepth;

        Parallel.For(0, n, i =>
        {
            candidate[i] = height[i] > sea && flow.Filled[i] - height[i] > tolerance;
        });

        var mask = new byte[n];
        var stack = new Stack<int>();
        var component = new List<int>();
        int minCells = cfg.MinLakeCells;
        cells = 0;

        for (int start = 0; start < n; start++)
        {
            if (!candidate[start]) continue;

            component.Clear();
            stack.Push(start);
            candidate[start] = false;

            while (stack.Count > 0)
            {
                int c = stack.Pop();
                component.Add(c);

                int cx = c % width, cy = c / width;
                Push(cx - 1, cy); Push(cx + 1, cy); Push(cx, cy - 1); Push(cx, cy + 1);
            }

            if (component.Count < minCells) continue;
            foreach (int c in component) mask[c] = 1;
            cells += component.Count;
        }

        return mask;

        void Push(int x, int y)
        {
            if (x < 0 || y < 0 || x >= width || y >= hgt) return;
            int i = y * width + x;
            if (!candidate[i]) return;
            candidate[i] = false;
            stack.Push(i);
        }
    }
}
