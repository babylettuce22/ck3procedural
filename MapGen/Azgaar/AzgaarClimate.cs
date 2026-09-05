using Ck3MapGen.Io;

namespace Ck3MapGen.MapGen;

/// <summary>
/// The export's own temperature and rainfall, resampled onto our climate grid.
///
/// Azgaar carries exactly two numbers per background-grid cell — <c>temp</c> and <c>prec</c> — and
/// that is the whole of what it knows about climate. There is no seasonality in an export at all:
/// no summer, no winter, no annual range. So this cannot be a substitution for
/// <see cref="ClimateModel"/> and is not written as one. It is a *reanchoring*: the export decides
/// where it is hot and where it rains, and our model keeps everything the export has no opinion
/// about — the seasonal swing, the summer/winter split, and the sub-grid relief detail.
///
/// That division is not a compromise reached for want of data, it is the right one. Azgaar's temp
/// is a function of latitude and height, which is precisely the part a player looks at and expects
/// to recognise; the seasonal range is a function of continentality and latitude, which our model
/// already derives and the export could not disagree with even if it stored it.
///
/// <b>Units.</b> <c>grid.cells[].temp</c> is Celsius, always. <c>settings.temperatureScale</c> says
/// "°F" on every export measured and means nothing here — it is the unit the *user interface*
/// displays, exactly as <c>settings.heightUnit</c> reads "ft" beside heights stored 0-100. The
/// values settle it: Champsia spans -24..22 and Oreia -29..30, which are Celsius world maps and
/// would be nonsense as Fahrenheit. Converting on the strength of that setting would cool every
/// imported map by about thirty degrees and turn its temperate belt arctic.
///
/// <c>prec</c> has no unit at all — it is Azgaar's own 0-255-ish scale, relative to the settings the
/// map was generated with. Nothing here tries to invent millimetres for it, because
/// <see cref="ClimateModel"/> already rescales the finished rainfall field so its land median is
/// <see cref="Config.MapConfig.MedianRainfallMm"/>. Handing that step a differently-scaled field
/// costs nothing and the absolute numbers come out right anyway.
/// </summary>
public static class AzgaarClimate
{
    /// <summary>How the export maps rows of the canvas onto latitude.</summary>
    /// <param name="Span">Degrees from the top edge to the bottom edge.</param>
    /// <param name="EquatorFraction">Where latitude zero sits, as a fraction of the height.</param>
    public readonly record struct Framing(double Span, double EquatorFraction);

    /// <summary>
    /// The latitudes the export was generated under, from <c>mapCoordinates</c>.
    ///
    /// Importing the temperature without this is the trap. Azgaar derives its temperature from its
    /// own latitudes, and a default export spans the whole globe — 90N to 90S, equator halfway down
    /// — while our own default is ck2rpg's deliberately lopsided 80 degrees with the equator at 90%
    /// down, which makes a map that is nearly all one hemisphere. Take the temperatures and leave
    /// the framing and the two disagree about where the world even is: the imported field is cold at
    /// both edges and warm through the middle, which is correct, while everything still derived from
    /// latitude puts the tropics near the bottom edge. The seasonal range is then computed for the
    /// wrong distance from the equator, and worse, <see cref="ClimateModel"/> decides which half of
    /// the year is summer from the sign of the latitude — so a whole hemisphere gets its seasons
    /// inverted, and January rain lands on the July side of the map.
    ///
    /// So the framing travels with the temperature or neither travels. Null when the export omits
    /// the block, in which case the configured framing stands.
    /// </summary>
    public static Framing? ReadFraming(AzgaarImport azgaar)
    {
        if (azgaar.World.MapCoordinates is not { } coordinates) return null;

        double north = coordinates.LatN;
        double south = coordinates.LatS;

        // Taken from the two edges rather than from latT, which is the same number stated twice and
        // so is worth cross-checking rather than trusting.
        double span = north - south;
        if (double.IsNaN(span) || span <= 0 || span > 180) return null;
        if (north > 90.001 || south < -90.001) return null;

        // Where latitude zero falls, measured down from the top edge in map heights. Deliberately
        // *not* clamped to 0..1: a map that does not straddle the equator has its zero off the
        // canvas, and that is ordinary — a band from 10N to 50N puts it 1.25 map-heights down, a
        // southern-hemisphere map puts it above the top edge at a negative fraction. Both are what
        // the arithmetic in ClimateModel.Latitudes wants, and it reproduces latN at the top row and
        // latS at the bottom for any fraction at all. Rejecting them, as an earlier guard here did,
        // was worse than useless: the temperatures would still be imported while the framing quietly
        // fell back to the configured one, which is the exact mismatch this method exists to stop.
        return new Framing(span, north / span);
    }

    /// <summary>The export's climate at one resolution, or nothing when it carries none.</summary>
    public sealed class Samples
    {
        /// <summary>Mean temperature in degrees Celsius.</summary>
        public required float[] TempC { get; init; }

        /// <summary>Precipitation on Azgaar's own relative scale.</summary>
        public required float[] Prec { get; init; }
    }

    /// <summary>
    /// Resamples the export's background grid onto a <paramref name="width"/> by
    /// <paramref name="height"/> field, or returns null when there is nothing to read.
    ///
    /// Sampled off the regular lattice rather than through the Voronoi cells, even though the pack
    /// cells carry an index into it. The lattice is what the data actually lives on, so reading it
    /// directly interpolates smoothly instead of inheriting the blockiness of a cell diagram that
    /// has nothing to do with climate — and it still has values where the packed graph dropped its
    /// deep-ocean cells.
    ///
    /// Null is the normal answer for a "Minimal" export, which omits <c>grid</c> entirely. The
    /// caller keeps its own climate in that case rather than failing.
    /// </summary>
    public static Samples? Sample(AzgaarImport azgaar, int width, int height)
    {
        var grid = azgaar.World.Grid;
        if (grid is null || grid.Cells.Count == 0) return null;

        int cellsX = grid.CellsX, cellsY = grid.CellsY;
        if (cellsX <= 0 || cellsY <= 0) return null;

        double canvasWidth = Math.Max(1.0, azgaar.World.Info.Width);
        double canvasHeight = Math.Max(1.0, azgaar.World.Info.Height);

        // The exports all carry a spacing, but deriving it from the lattice is both a fallback for
        // one that does not and a check that the two agree.
        double spacingX = grid.Spacing > 0 ? grid.Spacing : canvasWidth / cellsX;
        double spacingY = grid.Spacing > 0 ? grid.Spacing : canvasHeight / cellsY;

        var temp = new float[width * height];
        var prec = new float[width * height];
        var cells = grid.Cells;

        Parallel.For(0, height, y =>
        {
            double canvasY = (y + 0.5) / height * canvasHeight;

            // Lattice points sit at the centre of their cell, so a half-step comes off before the
            // fractional index is taken; without it every sample is skewed half a cell north-west.
            double gy = canvasY / spacingY - 0.5;

            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                double canvasX = (x + 0.5) / width * canvasWidth;
                double gx = canvasX / spacingX - 0.5;

                temp[row + x] = Bilinear(cells, cellsX, cellsY, gx, gy, static c => c.Temp);
                prec[row + x] = Bilinear(cells, cellsX, cellsY, gx, gy, static c => c.Prec);
            }
        });

        return new Samples { TempC = temp, Prec = prec };
    }

    private static float Bilinear(List<AzgaarGridCell> cells, int cellsX, int cellsY,
        double gx, double gy, Func<AzgaarGridCell, int> pick)
    {
        int x0 = (int)Math.Floor(gx), y0 = (int)Math.Floor(gy);
        double fx = gx - x0, fy = gy - y0;

        float c00 = At(x0, y0), c10 = At(x0 + 1, y0);
        float c01 = At(x0, y0 + 1), c11 = At(x0 + 1, y0 + 1);

        double top = c00 + (c10 - c00) * fx;
        double bottom = c01 + (c11 - c01) * fx;
        return (float)(top + (bottom - top) * fy);

        float At(int cx, int cy)
        {
            cx = Math.Clamp(cx, 0, cellsX - 1);
            cy = Math.Clamp(cy, 0, cellsY - 1);

            int index = cy * cellsX + cx;
            return index >= 0 && index < cells.Count ? pick(cells[index]) : 0f;
        }
    }

    /// <summary>
    /// Replaces the modelled mean temperature with the export's.
    ///
    /// Wholesale rather than blended. Azgaar's temperature is already a function of latitude and
    /// height, so it carries the same structure ours does and averaging the two would only smear
    /// two plausible maps into one that matches neither. What it does not carry is sub-grid relief,
    /// and that is added back later: <see cref="ClimateModel"/> applies a lapse-rate correction for
    /// the difference between fine and coarse elevation, which is a *differential* and so does not
    /// count the mountains twice.
    ///
    /// The seasonal range is deliberately untouched — the export has none to give.
    /// </summary>
    public static void ApplyTemperature(float[] mean, Samples samples)
    {
        int n = Math.Min(mean.Length, samples.TempC.Length);
        Array.Copy(samples.TempC, mean, n);
    }

    /// <summary>
    /// Repaints where the rain falls from the export, keeping our own seasonal split.
    ///
    /// Each cell keeps the summer/winter *ratio* the circulation model gave it and takes its total
    /// from Azgaar. That is the only way to combine them: the export has one precipitation number
    /// per cell and no notion of a wet or dry season, while the two sweeps in
    /// <see cref="ClimateModel"/> know which half of the year the rain arrives in but drew the map
    /// of it themselves. Keeping the ratio and replacing the magnitude takes each from whichever
    /// actually knows.
    ///
    /// A cell our model left completely dry has no ratio to keep and splits its new rainfall evenly
    /// rather than dividing by zero — an even split being the honest answer where the only thing
    /// known about the cell is its annual total.
    /// </summary>
    public static void ApplyPrecipitation(float[] july, float[] january, Samples samples)
    {
        int n = Math.Min(july.Length, Math.Min(january.Length, samples.Prec.Length));

        for (int i = 0; i < n; i++)
        {
            float total = samples.Prec[i];
            if (total < 0f) total = 0f;

            float ours = july[i] + january[i];
            float share = ours > 0f ? july[i] / ours : 0.5f;

            july[i] = total * share;
            january[i] = total * (1f - share);
        }
    }
}
