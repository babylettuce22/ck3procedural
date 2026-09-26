using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Ck3MapGen.Config;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SharpImage = SixLabors.ImageSharp.Image;

namespace Ck3MapGen.AppGUI;

/// <summary>
/// What the Azgaar page knows about a Full JSON export once it has read it: enough to say what
/// the world holds and to draw it, without building anything.
/// </summary>
internal sealed class AzgaarExportSummary
{
    public required string Path { get; init; }
    public required AzgaarWorld World { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }

    public string MapName => AzgaarNaming.StripParenthetical(World.Info.MapName.Trim());
    public int Year => World.Settings.Options.Year;
    public string Era => World.Settings.Options.Era;
    public string EraShort => World.Settings.Options.EraShort;
    public int States => World.RealStates.Count();
    public int Provinces => World.RealProvinces.Count();
    public int Burgs => World.RealBurgs.Count();
    public int Cultures => World.RealCultures.Count();
    public int Religions => World.RealReligions.Count();

    /// <summary>The races the export's cultures are tagged with that we can draw, most common first.</summary>
    public IReadOnlyList<string> Races { get; init; } = [];

    /// <summary>How advanced the export's peoples make the world; null when it has no people to read.</summary>
    public AzgaarAdvancement.Reading? Advancement { get; init; }

    /// <summary>"Year 963 of the Balistow Era", or null when the export carries no year.</summary>
    public string? CalendarLine => Year <= 0 ? null
        : Era.Length > 0 ? $"Year {Year} of the {Era}"
        : $"Year {Year}";

    /// <summary>
    /// Reads an export. Throws with a message fit to show when the file is not a Full export —
    /// <see cref="AzgaarJson.Load"/> words those for exactly this.
    /// </summary>
    public static AzgaarExportSummary Load(string path)
    {
        var loaded = AzgaarJson.Load(path);
        if (!loaded.World.HasCells)
            throw new InvalidOperationException(
                "This export carries no map cells, so its countries cannot be placed. Re-export it with "
                + "Menu ▸ Save/Load ▸ Export to JSON ▸ Full.");

        var races = loaded.World.RealCultures
            .Select(c => AzgaarNaming.ParseRace(c.Name))
            .Where(r => r is not null && r != RaceArchetype.Human)
            .GroupBy(r => r!.Value)
            .OrderByDescending(g => g.Count())
            .Select(g => RaceName(g.Key))
            .ToList();

        return new AzgaarExportSummary
        {
            Path = path,
            World = loaded.World,
            Warnings = loaded.Warnings.Select(w => w.Title).ToList(),
            Races = races,
            Advancement = AzgaarAdvancement.Read(loaded.World),
        };
    }

    private static string RaceName(RaceArchetype race) => race switch
    {
        RaceArchetype.HighElf => "Elves",
        RaceArchetype.Deepkin => "Dark elves",
        RaceArchetype.Dwarf => "Dwarves",
        RaceArchetype.Orc => "Orcs",
        RaceArchetype.Gnome => "Halflings",
        RaceArchetype.Giantkin => "Giants",
        _ => race.ToString(),
    };
}

/// <summary>What the page knows about the heightmap image: its size, and the size it will be built at.</summary>
internal sealed record AzgaarImageSummary(string Path, int Width, int Height, (int Width, int Height)? Fit)
{
    public static AzgaarImageSummary Measure(string path)
    {
        var (w, h) = TileFit.Measure(path);
        return new AzgaarImageSummary(path, w, h, TileFit.Fits(w, h) ? null : TileFit.Nearest(w, h));
    }

    /// <summary>"7680 × 3840, built at 8192 × 4096".</summary>
    public string SizeLine => Fit is { } fit
        ? $"{Width} × {Height}, built at {fit.Width} × {fit.Height}"
        : $"{Width} × {Height}";

    /// <summary>How far off 2:1 the image is, which is the shape Azgaar should be set to.</summary>
    public bool OddShape => Math.Abs((double)Width / Math.Max(1, Height) - 2.0) > 0.08;
}

/// <summary>
/// The page's helpers for the two files: finding one from the other, and drawing the pair.
/// </summary>
internal static class AzgaarFiles
{
    /// <summary>
    /// The other half of a pair, if it is lying beside the one chosen. Azgaar names its downloads
    /// after the map and the moment — "Ondrerol 2026-08-25-11-37.png" and
    /// "Ondrerol Full 2026-08-25-11-37.json" — so the partner is the file whose name matches once
    /// " Full" is taken out. Null when there is no such file, or more than one.
    /// </summary>
    public static string? FindPartner(string path)
    {
        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (dir is null || !Directory.Exists(dir)) return null;
            bool isImage = !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
            string key = PairKey(path);

            var candidates = Directory.EnumerateFiles(dir, isImage ? "*.json" : "*.png")
                .Where(f => PairKey(f).Equals(key, StringComparison.OrdinalIgnoreCase))
                .ToList();
            return candidates.Count == 1 ? candidates[0] : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>A file's name with the extension, a browser's " (1)" and Azgaar's " Full" taken out.</summary>
    private static string PairKey(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        name = System.Text.RegularExpressions.Regex.Replace(name, @"\s*\(\d+\)$", "");
        name = System.Text.RegularExpressions.Regex.Replace(name, @"\s+Full(?=\s|$)", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return name.Trim();
    }

    /// <summary>
    /// The picture of a pair, how well its halves agree, and how much of it is land — plus, when
    /// both halves are in, the land area of every (country, province) the export draws, in preview
    /// pixels: the groups the hierarchy turns into counties. Either half may be missing.
    /// </summary>
    public sealed record Picture(Bitmap Image, AzgaarRaster.Alignment? Alignment, double LandShare, int PreviewPixels,
        IReadOnlyDictionary<int, int> ProvinceAreas);

    /// <summary>The most baronies one county holds; a province bigger than this is split. See <see cref="Titles.MaxBaroniesPerCounty"/>.</summary>
    public static int MaxBaroniesPerProvince => Titles.MaxBaroniesPerCounty;

    /// <summary>
    /// What "Azgaar" comes to for provinces on this pair: the baronies the run will cut, how many of
    /// them the cap added beyond the towns, how many provinces will hold more than a county can and
    /// be split, and the uniform barony size for land outside any province.
    /// </summary>
    public sealed record ProvinceFit(double Scale, int Baronies, int FromCap, int Split);

    /// <summary>
    /// The run's own rule (<see cref="AzgaarSeeding"/>), worked out at preview size: every province
    /// gets one barony per town, at least one, and more where a barony would pass
    /// <paramref name="maxBaronyArea"/> vanilla baronies. Areas are measured at preview size and
    /// scaled up to the province raster, so the counts are estimates within a few per cent.
    ///
    /// <see cref="ProvinceFit.Scale"/> is for the land no province covers, which keeps the ordinary
    /// uniform size: the one at which the whole map would hold a barony per town, between vanilla's
    /// own size and four times it.
    /// </summary>
    public static ProvinceFit? ProvinceScale(Picture picture, (int Width, int Height) built, AzgaarWorld world, double maxBaronyArea)
    {
        if (picture.ProvinceAreas.Count == 0 || picture.PreviewPixels <= 0) return null;

        var cfg = new MapConfig { Width = built.Width, Height = built.Height };
        double up = (double)cfg.ProvinceWidth * cfg.ProvinceHeight / picture.PreviewPixels;
        double barony = cfg.BaronyPixelsAtVanilla;
        double cap = Math.Max(1.0, maxBaronyArea) * barony;

        var towns = new Dictionary<int, int>();
        foreach (var burg in world.RealBurgs)
        {
            if (burg.Cell < 0 || burg.Cell >= world.Pack.Cells.Count) continue;
            int province = world.Pack.Cells[burg.Cell].Province;
            if (province > 0) towns[province] = towns.GetValueOrDefault(province) + 1;
        }

        int baronies = 0, fromCap = 0, split = 0;
        double land = 0;
        foreach (var (province, pixels) in picture.ProvinceAreas)
        {
            double area = pixels * up;
            land += area;
            int byTowns = Math.Max(1, towns.GetValueOrDefault(province));
            int count = Math.Max(byTowns, (int)Math.Ceiling(area / cap));
            baronies += count;
            fromCap += count - byTowns;
            if (count > MaxBaroniesPerProvince) split++;
        }

        int townCount = towns.Values.Sum();
        double scale = townCount > 0 ? Math.Sqrt(land / (barony * townCount)) : 1.0;
        return new ProvinceFit(Math.Round(Math.Clamp(scale, 1.0, 4.0), 2), baronies, fromCap, split);
    }

    /// <summary>
    /// Draws the pair at a small size: the heightmap's relief, coloured by the export's countries,
    /// with their borders and capitals. With only the image it is a plain relief map; with only the
    /// export, the countries on the export's own coarse land.
    ///
    /// When both are there it also answers the question the run would otherwise answer minutes
    /// in: do they describe the same view of the same map? That is the same land-against-land
    /// comparison the run makes (<see cref="AzgaarRaster.CheckAlignment"/>), at this size.
    /// </summary>
    public static Picture? Draw(string? imagePath, AzgaarWorld? world, int width, int height, CancellationToken token)
    {
        if (imagePath is null && world is null) return null;

        byte[]? gray = imagePath is null ? null : ReadGray(imagePath, width, height);
        token.ThrowIfCancellationRequested();

        AzgaarRaster? raster = null;
        if (world is not null)
        {
            var cfg = new MapConfig { Width = width, Height = height, ProvinceDownscale = 1 };
            raster = AzgaarRaster.Build(world, cfg);
        }
        token.ThrowIfCancellationRequested();

        int n = width * height;
        double seaLevel = new MapConfig().SourceSeaLevel;
        var land = new bool[n];
        for (int i = 0; i < n; i++)
            land[i] = gray is not null ? gray[i] > seaLevel : raster!.IsLandAt(i);

        AzgaarRaster.Alignment? alignment = null;
        if (gray is not null && raster is not null)
        {
            var mask = new byte[n];
            for (int i = 0; i < n; i++) mask[i] = land[i] ? (byte)1 : (byte)0;
            alignment = raster.CheckAlignment(mask);
        }

        // Relief: the image's own heights, or the export's per-cell heights (0-100) scaled onto it.
        var relief = new float[n];
        for (int i = 0; i < n; i++)
            relief[i] = gray is not null ? gray[i] / 255f : Math.Clamp(raster!.HeightAt(i) / 100f, 0, 1);

        // Country colours, by state id.
        var stateColor = new Dictionary<int, (byte R, byte G, byte B)>();
        if (world is not null)
            foreach (var state in world.RealStates)
                if (AzgaarColors.TryParseColor(state.Color, out var rgb)) stateColor[state.I] = rgb;

        int[]? stateOf = null;
        if (raster is not null)
        {
            stateOf = new int[n];
            for (int i = 0; i < n; i++) stateOf[i] = land[i] ? raster.StateAt(i) : -1;
        }

        var pixels = new int[n];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                if (!land[i])
                {
                    // Sea, a little lighter near the coast.
                    bool shore = Near(land, width, height, x, y, 2);
                    float t = y / (float)height;
                    int r = (int)(24 + 10 * t), g = (int)(58 + 22 * t), b = (int)(108 + 34 * t);
                    if (shore) { r += 16; g += 24; b += 28; }
                    pixels[i] = Argb(r, g, b);
                    continue;
                }

                // Hillshade from the north-west.
                float left = relief[y * width + Math.Max(0, x - 1)], up = relief[Math.Max(0, y - 1) * width + x];
                float shade = Math.Clamp(1f + (relief[i] - (left + up) / 2f) * 9f, 0.72f, 1.22f);

                (float R, float G, float B) baseColor;
                if (stateOf is not null && stateOf[i] > 0 && stateColor.TryGetValue(stateOf[i], out var sc))
                {
                    // The country's colour, softened toward white so the relief reads through it.
                    baseColor = (sc.R * 0.72f + 255 * 0.28f, sc.G * 0.72f + 255 * 0.28f, sc.B * 0.72f + 255 * 0.28f);
                }
                else
                {
                    baseColor = Hypsometric(relief[i]);
                }

                var (cr, cg, cb) = (baseColor.R * shade, baseColor.G * shade, baseColor.B * shade);

                // Borders between countries, and the coastline, drawn as darker pixels.
                if (stateOf is not null)
                {
                    int right = x + 1 < width ? stateOf[i + 1] : stateOf[i];
                    int down = y + 1 < height ? stateOf[i + width] : stateOf[i];
                    if ((right >= 0 && right != stateOf[i]) || (down >= 0 && down != stateOf[i]))
                        (cr, cg, cb) = (cr * 0.45f, cg * 0.45f, cb * 0.45f);
                }
                if (Near(land, width, height, x, y, 1, water: true))
                    (cr, cg, cb) = (cr * 0.7f, cg * 0.7f, cb * 0.7f);

                pixels[i] = Argb((int)cr, (int)cg, (int)cb);
            }
        }
        token.ThrowIfCancellationRequested();

        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < height; y++)
                Marshal.Copy(pixels, y * width, data.Scan0 + y * data.Stride, width);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        // Capitals, as small white dots.
        if (world is not null && world.Info.Width > 0 && world.Info.Height > 0)
        {
            using var g = Graphics.FromImage(bitmap);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var fill = new SolidBrush(Color.White);
            using var ring = new Pen(Color.FromArgb(200, 30, 30, 30), 1.2f);
            float sx = width / (float)world.Info.Width, sy = height / (float)world.Info.Height;
            float r = Math.Max(2.2f, width / 380f);
            foreach (var burg in world.RealBurgs.Where(b => b.Capital == 1))
            {
                float bx = (float)burg.X * sx, by = (float)burg.Y * sy;
                g.FillEllipse(fill, bx - r, by - r, 2 * r, 2 * r);
                g.DrawEllipse(ring, bx - r, by - r, 2 * r, 2 * r);
            }
        }

        // Land area per Azgaar province, the regions the partition seeds one by one
        // (MapGen.AzgaarSeeding). Only where both halves are in: the land is the image's, the
        // provinces the export's.
        var areas = new Dictionary<int, int>();
        if (gray is not null && raster is not null)
        {
            for (int i = 0; i < n; i++)
            {
                if (!land[i]) continue;
                int province = raster.ProvinceAt(i);
                if (province > 0) areas[province] = areas.GetValueOrDefault(province) + 1;
            }
        }

        return new Picture(bitmap, alignment, land.Count(l => l) / (double)Math.Max(1, n), n, areas);
    }

    private static int Argb(int r, int g, int b)
        => unchecked((int)0xFF000000) | (Math.Clamp(r, 0, 255) << 16) | (Math.Clamp(g, 0, 255) << 8) | Math.Clamp(b, 0, 255);

    /// <summary>Whether any pixel within <paramref name="reach"/> is land (or, with <paramref name="water"/>, sea).</summary>
    private static bool Near(bool[] land, int width, int height, int x, int y, int reach, bool water = false)
    {
        for (int dy = -reach; dy <= reach; dy++)
        {
            int yy = y + dy;
            if (yy < 0 || yy >= height) continue;
            for (int dx = -reach; dx <= reach; dx++)
            {
                int xx = x + dx;
                if (xx < 0 || xx >= width || (dx == 0 && dy == 0)) continue;
                if (land[yy * width + xx] != water) return true;
            }
        }
        return false;
    }

    /// <summary>Green lowlands through brown hills to pale peaks, for land no country claims.</summary>
    private static (float R, float G, float B) Hypsometric(float h)
    {
        (float t, float r, float g, float b)[] stops =
        [
            (0.00f, 120, 150, 96), (0.35f, 150, 168, 110), (0.55f, 176, 160, 120),
            (0.75f, 150, 128, 104), (0.90f, 205, 200, 196), (1.00f, 245, 245, 245),
        ];
        for (int s = 1; s < stops.Length; s++)
        {
            if (h > stops[s].t) continue;
            var (t0, r0, g0, b0) = stops[s - 1];
            var (t1, r1, g1, b1) = stops[s];
            float u = (h - t0) / Math.Max(1e-4f, t1 - t0);
            return (r0 + (r1 - r0) * u, g0 + (g1 - g0) * u, b0 + (b1 - b0) * u);
        }
        return (245, 245, 245);
    }

    /// <summary>The image as grey levels at the preview size. Colour images are read as their luminance.</summary>
    private static byte[] ReadGray(string path, int width, int height)
    {
        using var image = SharpImage.Load<L8>(path);
        image.Mutate(c => c.Resize(width, height, KnownResamplers.Box));
        var gray = new byte[width * height];
        image.CopyPixelDataTo(gray);
        return gray;
    }
}
