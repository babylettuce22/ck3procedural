using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.World;

namespace Ck3MapGen.MapGen;

/// <summary>
/// Port of js/mapgen/tectonics.js.
///
/// The model is deliberately crude and that is what gives ck2rpg maps their look: a handful of
/// wandering "spreading lines" repeatedly dump magma, and the magma diffuses to lower-magma
/// neighbours carrying elevation with it. Continents are the residue of that diffusion rather
/// than anything plate-like.
/// </summary>
public static class Tectonics
{
    /// <summary>Port of createSpreadingCenters() — evenly spaced columns across the map.</summary>
    public static void CreateSpreadingCenters(WorldGrid w, MapConfig cfg, Rng rng)
    {
        int numCenters;
        int spacing;
        if (cfg.VerticalSpread)
        {
            numCenters = rng.Int(5, 45);
            spacing = w.Width / numCenters;
        }
        else if (cfg.HorizontalSpread)
        {
            numCenters = rng.Int(500, 512);
            spacing = 1;
        }
        else
        {
            return;
        }

        for (int i = 0; i < numCenters; i++)
            w.SpreadingCenters.Add(spacing * i);

        w.SpreadingLine.Clear();
    }

    /// <summary>
    /// Port of createSpreadingLine(). A ridge that walks down the map, jittering horizontally
    /// and bouncing off inset margins. Note the JS derives both adjusters from world.height.
    /// </summary>
    public static void CreateSpreadingLine(WorldGrid w, MapConfig cfg, Rng rng, int center)
    {
        int verticalAdjuster = w.Height / rng.Int(1, 15);
        int horizontalAdjuster = w.Height / rng.Int(1, 15);

        int widthStart = horizontalAdjuster;
        int widthEnd = w.Width - horizontalAdjuster;
        int heightStart = verticalAdjuster;
        int heightEnd = w.Height - verticalAdjuster;

        if (cfg.VerticalSpread)
        {
            for (int y = verticalAdjuster; y < w.Height - verticalAdjuster; y++)
            {
                int direction = rng.Int(0, 100) < 50 ? -1 : 1;
                center += direction * rng.Int(1, 20);

                if (center < widthStart) center = widthStart + rng.Int(1, 50);
                else if (center >= widthEnd) center = widthEnd - 1 - rng.Int(1, 50);

                MarkSpreading(w, center, y);
            }
        }

        if (cfg.HorizontalSpread)
        {
            for (int x = horizontalAdjuster; x < w.Width - horizontalAdjuster; x++)
            {
                int direction = rng.Int(0, 100) < 50 ? -1 : 1;
                center += direction * rng.Int(1, 20);

                if (center < heightStart) center = heightStart + rng.Int(1, 50);
                else if (center >= heightEnd) center = heightEnd - 1 - rng.Int(1, 50);

                MarkSpreading(w, x, center);
            }
        }
    }

    /// <summary>Port of createHSpreadLine() — one ridge running the full width of the map.</summary>
    public static void CreateHSpreadLine(WorldGrid w, Rng rng)
    {
        int y = rng.Int(1, w.Height - 1);

        for (int x = 1; x < w.Width; x++)
        {
            // Only one branch's draw is evaluated in the JS ternary, so this is two draws.
            y += rng.Int(0, 100) < 50 ? -rng.Int(1, 20) : rng.Int(1, 20);

            if (y < 1) y = 1;
            else if (y > w.Height - 1) y = w.Height - 1 - rng.Int(1, 20);

            MarkSpreading(w, x, y);
        }
    }

    /// <summary>
    /// The clamps above can still land outside the grid on narrow maps. The JS silently
    /// no-ops there (xy returns the string "edge" and assigning to it does nothing in sloppy
    /// mode), so we skip rather than clamp — clamping would pile ridge cells on the border.
    /// </summary>
    private static void MarkSpreading(WorldGrid w, int x, int y)
    {
        int i = w.At(x, y);
        if (i < 0) return;
        w.Spreading[i] = true;
        w.SpreadingLine.Add(i);
    }

    /// <summary>Port of spreadingCenterEmits() — a large magma pulse along every ridge cell.</summary>
    public static void SpreadingCenterEmits(WorldGrid w, Rng rng)
    {
        foreach (int i in w.SpreadingLine)
        {
            int add = rng.Int(0, 255);
            w.Magma[i] += add;
            w.Elevation[i] += add;
        }
    }

    /// <summary>Port of spreadingCenterEmitsSmall().</summary>
    public static void SpreadingCenterEmitsSmall(WorldGrid w, Rng rng)
    {
        foreach (int i in w.SpreadingLine)
        {
            int add = rng.Int(1, 5);
            w.Magma[i] += add;
            w.Elevation[i] += add;
        }
    }

    /// <summary>
    /// Port of spread(). Magma flows downhill-in-magma to all 8 neighbours, plus one extra
    /// randomly chosen neighbour that therefore gets served twice — that asymmetry is in the
    /// original and is part of why the ridges come out ragged rather than symmetric.
    /// </summary>
    public static void Spread(WorldGrid w, Rng rng)
    {
        Span<int> neighbors = stackalloc int[8];
        for (int y = 0; y < w.Height; y++)
        {
            for (int x = 0; x < w.Width; x++)
            {
                int cell = y * w.Width + x;
                if (w.Magma[cell] <= 0) continue;

                // Fixed 8 slots in the JS's order, with -1 standing in for "edge", because the
                // random index below is drawn over all 8 slots including out-of-bounds ones.
                neighbors[0] = w.At(x - 1, y);
                neighbors[1] = w.At(x + 1, y);
                neighbors[2] = w.At(x + 1, y + 1);
                neighbors[3] = w.At(x - 1, y - 1);
                neighbors[4] = w.At(x, y + 1);
                neighbors[5] = w.At(x, y - 1);
                neighbors[6] = w.At(x - 1, y + 1);
                neighbors[7] = w.At(x + 1, y - 1);

                RollMagma(w, rng, neighbors[rng.Int(0, 7)], cell);
                for (int k = 0; k < 8; k++)
                    RollMagma(w, rng, neighbors[k], cell);
            }
        }
    }

    /// <summary>
    /// Port of rollMagma(). The divisor is drawn before the transfer is tested, so it consumes
    /// a random number even when nothing moves.
    /// </summary>
    private static void RollMagma(WorldGrid w, Rng rng, int newCell, int oldCell)
    {
        int mult = rng.Int(1, 15);
        if (newCell < 0 || oldCell < 0) return;
        if (w.Magma[newCell] >= w.Magma[oldCell]) return;

        int diff = w.Magma[oldCell] - w.Magma[newCell];
        int div = diff / mult; // diff is positive here, so truncation matches Math.floor
        w.Magma[newCell] += div;
        w.Elevation[newCell] += div;
        w.Magma[oldCell] -= div;
        w.Elevation[oldCell] -= div;
    }

    /// <summary>Port of spreadProcess(num).</summary>
    public static void SpreadProcess(WorldGrid w, MapConfig cfg, Rng rng, int iterations)
    {
        Climate.ClearRain(w);
        for (int i = 0; i < iterations; i++)
        {
            SpreadingCenterEmits(w, rng);
            Spread(w, rng);
            Climate.SetMoisture(w, cfg, rng);
        }
    }

    /// <summary>Port of createWorld() — lay out the ridges, ready for the spread iterations.</summary>
    public static void Initialize(WorldGrid w, MapConfig cfg, Rng rng)
    {
        CreateSpreadingCenters(w, cfg, rng);
        foreach (int center in w.SpreadingCenters.ToArray())
            CreateSpreadingLine(w, cfg, rng, center);
        CreateHSpreadLine(w, rng);
    }
}
