using Ck3MapGen.MapGen;

namespace Ck3MapGen.AppGUI;

/// <summary>
/// A county map that can be repainted many times a second.
///
/// <see cref="PreviewRenderer.RenderByCounty"/> answers "what colour is this county" by walking the
/// full-resolution province raster every time it draws, which is right for a view drawn once and
/// far too slow for one redrawn every frame while history plays. Only the colours change from
/// frame to frame, never the ground, so this does the raster walk once: for every output pixel it
/// keeps which county it shows and, if it sits on a county boundary, which county is across it.
/// A frame is then one lookup per pixel.
///
/// Drawn to match <see cref="PreviewRenderer.RenderByCounty"/> — the same sea, impassable, border
/// and wilderness colours, a border drawn only where the two sides differ in colour, and the same
/// downsampling that keeps a border pixel in preference to the interior around it — so a frame
/// here and the World workspace's realm view of the same map look the same.
/// </summary>
internal sealed class CountyCanvas
{
    private const int Water = -1, Impassable = -2, NoCounty = -3;

    private static readonly (byte R, byte G, byte B) Sea = (38, 62, 96);
    private static readonly (byte R, byte G, byte B) Rock = (92, 92, 100);
    private static readonly (byte R, byte G, byte B) Line = (22, 24, 28);
    private static readonly (byte R, byte G, byte B) Missing = (255, 0, 255);
    private static readonly (byte R, byte G, byte B) WildTint = (168, 120, 48);

    /// <summary>What each output pixel shows: a county index, or one of the negative markers.</summary>
    private readonly int[] _county;

    /// <summary>The county across the boundary this pixel sits on, or the pixel's own when it is interior.</summary>
    private readonly int[] _across;

    private readonly bool[] _wild;

    public CountyCanvas(PreviewRenderer.ProvinceRaster raster)
    {
        Counties = [.. Titles.Flatten(raster.Titles).Where(t => t.Tier == "c")];
        Index = [];
        for (int c = 0; c < Counties.Count; c++) Index[Counties[c]] = c;

        _wild = new bool[Counties.Count];
        for (int c = 0; c < Counties.Count; c++) _wild[c] = raster.IsWild?.Invoke(Counties[c]) == true;

        int width = raster.Width, height = raster.Height;
        int baronyCount = raster.BaronyCount, landCount = raster.LandCount;

        var countyOf = new int[baronyCount + 1];
        Array.Fill(countyOf, NoCounty);
        for (int c = 0; c < Counties.Count; c++)
            foreach (var barony in Counties[c].Children)
                if (barony.ProvinceId >= 1 && barony.ProvinceId <= baronyCount)
                    countyOf[barony.ProvinceId] = c;

        int At(int i)
        {
            int id = raster.IdAt(i);
            return id >= 1 && id <= baronyCount ? countyOf[id] : id > landCount || id < 1 ? Water : Impassable;
        }

        // The county across a boundary from pixel i, looking right and down as the full renderer
        // does, or i's own county when there is none.
        int Across(int i, int here)
        {
            int x = i % width, y = i / width;
            if (x + 1 < width && At(i + 1) is var right && right != here) return right;
            if (y + 1 < height && At(i + width) is var below && below != here) return below;
            return here;
        }

        int step = PreviewRenderer.StepFor(width);
        Width = width / step;
        Height = height / step;
        _county = new int[Width * Height];
        _across = new int[Width * Height];

        Parallel.For(0, Height, y =>
        {
            for (int x = 0; x < Width; x++)
            {
                int source = (y * step) * width + x * step;
                int here = At(source), across = Across(source, here);

                // Prefer a boundary pixel inside the block, so a border survives the downsample
                // however thin it is at full resolution.
                for (int by = 0; by < step && across == here; by++)
                {
                    int row = (y * step + by) * width + x * step;
                    for (int bx = 0; bx < step; bx++)
                    {
                        int h = At(row + bx);
                        if (h < 0) continue;
                        int a = Across(row + bx, h);
                        if (a == h) continue;
                        here = h;
                        across = a;
                        break;
                    }
                }

                int o = y * Width + x;
                _county[o] = here;
                _across[o] = across;
            }
        });
    }

    /// <summary>Every county, in the order the colour arrays passed to <see cref="Render"/> are read in.</summary>
    public IReadOnlyList<Title> Counties { get; }

    /// <summary>Where each county sits in <see cref="Counties"/>.</summary>
    public Dictionary<Title, int> Index { get; }

    public int Width { get; }
    public int Height { get; }

    /// <summary>The county under an output pixel, or null over sea, rock or off the map.</summary>
    public Title? CountyAt(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return null;
        int c = _county[y * Width + x];
        return c >= 0 ? Counties[c] : null;
    }

    /// <summary>
    /// One frame. <paramref name="colours"/> is indexed like <see cref="Counties"/>; a null entry
    /// paints as wilderness, the same as a county the raster marks wild.
    /// </summary>
    public PreviewRenderer.Image Render((byte R, byte G, byte B)?[] colours)
    {
        var rgb = new byte[Width * Height * 3];

        bool Wild(int c) => _wild[c] || colours[c] is null;
        (byte R, byte G, byte B) Fill(int c) => Wild(c) ? WildTint : colours[c]!.Value;

        Parallel.For(0, Height, y =>
        {
            for (int x = 0; x < Width; x++)
            {
                int o = y * Width + x;
                int c = _county[o], a = _across[o];

                var colour = c switch
                {
                    Water => Sea,
                    Impassable => Rock,
                    NoCounty => Missing,
                    _ when a != c && (a < 0 || Wild(a) != Wild(c) || Fill(a) != Fill(c)) => Line,
                    _ => Fill(c),
                };

                rgb[o * 3] = colour.R;
                rgb[o * 3 + 1] = colour.G;
                rgb[o * 3 + 2] = colour.B;
            }
        });

        return new PreviewRenderer.Image(rgb, Width, Height);
    }
}
