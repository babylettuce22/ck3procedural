using Ck3MapGen.Config;
using Ck3MapGen.Io;

namespace Ck3MapGen.MapGen;

/// <summary>
/// Puts an Azgaar map onto our province raster, so every pixel of our world knows which Azgaar cell
/// it fell in and therefore which state, province, culture, religion and biome Azgaar had there.
///
/// The whole thing rests on one observation that saves an enormous amount of code: Azgaar's cells
/// are a Voronoi diagram, and <c>cells.p</c> holds the *sites*. Nearest-site lookup over those
/// points does not approximate the partition — it reproduces it exactly. So there is no polygon
/// scan conversion here, no GeoJSON dependency, no winding rules and no shared-edge tie-breaking;
/// there is a bucketed nearest-neighbour query and nothing else. (The one thing this does not
/// reproduce is Azgaar's rendered *coastline*, which it draws as smoothed isolines rather than as
/// cell edges. That is why elevation still comes from the exported heightmap and only the political
/// layers come from here.)
///
/// Built once and shared. Tier 1 reads it only to decide where a borrowed name belongs; the field
/// it produces is the same one a border-constrained province partition and an imported climate
/// would need, which is why it computes the whole per-pixel raster rather than just sampling the
/// handful of points naming happens to want.
/// </summary>
public sealed class AzgaarRaster
{
    /// <summary>Returned by <see cref="CellAtPixel"/> where the export had nothing to say.</summary>
    public const int NoCell = -1;

    private readonly AzgaarWorld _world;

    // The nearest-site index: a uniform bucket grid over the canvas, in CSR form. `_start[b]` is
    // where bucket b's members begin in `_items`, which holds cell indices.
    private readonly int[] _start;
    private readonly int[] _items;
    private readonly double[] _siteX;
    private readonly double[] _siteY;
    private readonly int _cols;
    private readonly int _rows;
    private readonly double _bucket;

    /// <summary>Canvas x per province-raster pixel.</summary>
    public double ScaleX { get; }

    /// <summary>Canvas y per province-raster pixel.</summary>
    public double ScaleY { get; }

    public int Width { get; }
    public int Height { get; }

    /// <summary>
    /// The Azgaar cell index under every province-raster pixel, row-major. <see cref="NoCell"/>
    /// only where the export had no cells at all.
    /// </summary>
    public int[] CellByPixel { get; }

    private AzgaarRaster(AzgaarWorld world, MapConfig cfg)
    {
        _world = world;
        Width = cfg.ProvinceWidth;
        Height = cfg.ProvinceHeight;

        var cells = world.Pack.Cells;
        _siteX = new double[cells.Count];
        _siteY = new double[cells.Count];
        for (int i = 0; i < cells.Count; i++)
        {
            _siteX[i] = cells[i].X;
            _siteY[i] = cells[i].Y;
        }

        // One bucket per cell on average: the query then touches a handful of candidates whatever
        // the map's size, and the index costs the same memory as the sites themselves.
        double area = Math.Max(1.0, world.Info.Width * world.Info.Height);
        _bucket = Math.Max(1.0, Math.Sqrt(area / Math.Max(1, cells.Count)));
        _cols = Math.Max(1, (int)Math.Ceiling(world.Info.Width / _bucket));
        _rows = Math.Max(1, (int)Math.Ceiling(world.Info.Height / _bucket));

        (_start, _items) = BuildBuckets();

        ScaleX = world.Info.Width / Math.Max(1, Width);
        ScaleY = world.Info.Height / Math.Max(1, Height);

        CellByPixel = new int[Width * Height];
        if (cells.Count == 0)
        {
            Array.Fill(CellByPixel, NoCell);
            return;
        }

        Parallel.For(0, Height, y =>
        {
            double cy = (y + 0.5) * ScaleY;
            int row = y * Width;
            for (int x = 0; x < Width; x++)
                CellByPixel[row + x] = NearestCell((x + 0.5) * ScaleX, cy);
        });
    }

    public static AzgaarRaster Build(AzgaarWorld world, MapConfig cfg)
        => new(world, cfg);

    private (int[] Start, int[] Items) BuildBuckets()
    {
        int count = _siteX.Length;
        var start = new int[_cols * _rows + 1];
        var bucketOf = new int[count];

        for (int i = 0; i < count; i++)
        {
            int b = BucketIndex(_siteX[i], _siteY[i]);
            bucketOf[i] = b;
            start[b + 1]++;
        }

        for (int b = 0; b < _cols * _rows; b++) start[b + 1] += start[b];

        var items = new int[count];
        var cursor = (int[])start.Clone();
        for (int i = 0; i < count; i++) items[cursor[bucketOf[i]]++] = i;

        return (start, items);
    }

    private int BucketIndex(double x, double y)
    {
        int bx = Math.Clamp((int)(x / _bucket), 0, _cols - 1);
        int by = Math.Clamp((int)(y / _bucket), 0, _rows - 1);
        return by * _cols + bx;
    }

    /// <summary>
    /// The cell whose site is closest to a point in Azgaar canvas coordinates — which, the sites
    /// being Voronoi sites, is the cell the point lies in.
    ///
    /// Rings of buckets are searched outward from the query's own. The stopping rule is what makes
    /// it exact rather than approximate: ring <c>r</c> cannot contain anything nearer than
    /// <c>(r - 1) * bucket</c>, so once the best distance so far is inside that the remaining rings
    /// cannot beat it and the search is finished. Stopping at the first non-empty ring instead —
    /// the obvious version — is wrong wherever cell density changes, which on an Azgaar map is
    /// every coastline.
    /// </summary>
    public int NearestCell(double x, double y)
    {
        if (_siteX.Length == 0) return NoCell;

        int bx = Math.Clamp((int)(x / _bucket), 0, _cols - 1);
        int by = Math.Clamp((int)(y / _bucket), 0, _rows - 1);

        int best = NoCell;
        double bestSq = double.MaxValue;
        int maxRing = Math.Max(_cols, _rows);

        for (int r = 0; r <= maxRing; r++)
        {
            if (best != NoCell)
            {
                double reach = (r - 1) * _bucket;
                if (reach > 0 && reach * reach >= bestSq) break;
            }

            int x0 = bx - r, x1 = bx + r, y0 = by - r, y1 = by + r;

            for (int gy = y0; gy <= y1; gy++)
            {
                if (gy < 0 || gy >= _rows) continue;

                if (gy == y0 || gy == y1)
                {
                    for (int gx = Math.Max(0, x0); gx <= Math.Min(_cols - 1, x1); gx++)
                        Scan(gx, gy);
                }
                else
                {
                    if (x0 >= 0) Scan(x0, gy);
                    if (x1 != x0 && x1 < _cols) Scan(x1, gy);
                }
            }
        }

        return best;

        void Scan(int gx, int gy)
        {
            int b = gy * _cols + gx;
            for (int k = _start[b]; k < _start[b + 1]; k++)
            {
                int cell = _items[k];
                double dx = _siteX[cell] - x;
                double dy = _siteY[cell] - y;
                double sq = dx * dx + dy * dy;
                if (sq >= bestSq) continue;
                bestSq = sq;
                best = cell;
            }
        }
    }

    /// <summary>The Azgaar cell under a province-raster pixel.</summary>
    public int CellAtPixel(int x, int y)
        => x < 0 || y < 0 || x >= Width || y >= Height ? NoCell : CellByPixel[y * Width + x];

    private AzgaarCell? Cell(int index)
        => index >= 0 && index < _world.Pack.Cells.Count ? _world.Pack.Cells[index] : null;

    // --- Per-pixel attribute lookups ---------------------------------------------------------
    //
    // Each returns 0 for "none", matching Azgaar's own sentinel, so a caller can treat unassigned
    // ground and off-map the same way without a null check at every site.

    public int StateAt(int pixel) => Cell(CellByPixel[pixel])?.State ?? 0;
    public int ProvinceAt(int pixel) => Cell(CellByPixel[pixel])?.Province ?? 0;
    public int CultureAt(int pixel) => Cell(CellByPixel[pixel])?.Culture ?? 0;
    public int ReligionAt(int pixel) => Cell(CellByPixel[pixel])?.Religion ?? 0;
    public int BurgAt(int pixel) => Cell(CellByPixel[pixel])?.Burg ?? 0;
    public int BiomeAt(int pixel) => Cell(CellByPixel[pixel])?.Biome ?? 0;
    public int RiverAt(int pixel) => Cell(CellByPixel[pixel])?.R ?? 0;
    public int HeightAt(int pixel) => Cell(CellByPixel[pixel])?.H ?? 0;
    public bool IsLandAt(int pixel) => Cell(CellByPixel[pixel])?.IsLand ?? false;

    /// <summary>Province-raster pixel index for a point in Azgaar canvas coordinates.</summary>
    public int PixelAt(double canvasX, double canvasY)
    {
        int x = (int)(canvasX / ScaleX);
        int y = (int)(canvasY / ScaleY);
        if (x < 0 || y < 0 || x >= Width || y >= Height) return -1;
        return y * Width + x;
    }

    // --- Alignment -----------------------------------------------------------------------------

    public sealed record Alignment(double LandAgreement, double AzgaarLandShare, double OurLandShare)
    {
        /// <summary>
        /// Below this the two maps are not the same map. Chosen well clear of the disagreement a
        /// correct import actually produces: Azgaar draws its coast as smoothed isolines and we
        /// resample a heightmap, so a few per cent of shoreline pixels differ even when everything
        /// is right, but nothing legitimate gets near ten.
        /// </summary>
        public bool LooksAligned => LandAgreement >= 0.90;
    }

    /// <summary>
    /// Compares the land Azgaar says is there against the land our heightmap gave us.
    ///
    /// This exists because the two halves of the import arrive by different routes — relief through
    /// an exported PNG, politics through the JSON — and nothing but this check can tell that they
    /// came from the same view of the same map. Export the image cropped, or zoomed, or at a
    /// different aspect, and every border still lands somewhere; it just lands in the sea. A number
    /// at load time is the difference between noticing that immediately and noticing it after
    /// wondering for an hour why a kingdom has no counties.
    /// </summary>
    public Alignment CheckAlignment(byte[] provinceLandMask)
    {
        long agreed = 0, azgaarLand = 0, ourLand = 0;
        long total = Math.Min(provinceLandMask.LongLength, CellByPixel.LongLength);

        for (long i = 0; i < total; i++)
        {
            bool theirs = IsLandAt((int)i);
            bool ours = provinceLandMask[i] != 0;
            if (theirs) azgaarLand++;
            if (ours) ourLand++;
            if (theirs == ours) agreed++;
        }

        if (total == 0) return new Alignment(0, 0, 0);
        return new Alignment((double)agreed / total, (double)azgaarLand / total, (double)ourLand / total);
    }

    /// <summary>
    /// The commonest non-zero value of <paramref name="attribute"/> over a set of pixels, and how
    /// much of the set it covers.
    ///
    /// Majority rather than centroid sampling because a title's shape is not its middle: a county
    /// wrapped around a bay has a centroid in the water, and one straddling a border takes its name
    /// from whichever side happens to hold the centre pixel. Counting is barely more expensive and
    /// gives the answer a person would give looking at the map.
    /// </summary>
    public (int Value, double Share) Majority(IEnumerable<int> pixels, Func<int, int> attribute)
    {
        var votes = new Dictionary<int, int>();
        int counted = 0;

        foreach (int pixel in pixels)
        {
            if (pixel < 0 || pixel >= CellByPixel.Length) continue;
            counted++;
            int value = attribute(pixel);
            if (value == 0) continue;
            votes[value] = votes.GetValueOrDefault(value) + 1;
        }

        if (votes.Count == 0 || counted == 0) return (0, 0);

        // Ties break on the lower id so the same map always imports the same way.
        var winner = votes.OrderByDescending(v => v.Value).ThenBy(v => v.Key).First();
        return (winner.Key, (double)winner.Value / counted);
    }
}
