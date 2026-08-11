using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.World;

namespace Ck3MapGen.MapGen;

/// <summary>
/// Port of js/mapgen/moisture.js.
///
/// Moisture is modelled as one cloud per row marching west to east, gaining moisture over sea
/// and losing it climbing terrain.
///
/// The js/mapgen/rain.js half of this — clearRain, shareRain, trackRain, worldRain,
/// erodeFromRaindrops and rainErosion — was removed on 2026-08-10 with the rest of the hydrology.
/// It was a second, unrelated way of deciding where water goes: a raindrop walk that eroded the
/// coarse grid and pooled its own lakes, from back when this tool generated terrain. Nothing had
/// called it since the heightmap became an import, and reinstating it is not the way to get rivers
/// back — the heightmap is authoritative and must not be eroded underneath the map its author drew.
///
/// What remains, <see cref="SetMoisture"/>, is live but narrow: it sets the ck2rpg desert flag that
/// <see cref="Terrain"/> reads. Rainfall for the actual map comes from <see cref="ClimateModel"/>.
/// </summary>
public static class Climate
{
    /// <summary>Port of setMoisture().</summary>
    public static void SetMoisture(WorldGrid w, MapConfig cfg, Rng rng)
    {
        int sea = cfg.Limits.SeaLevelUpper;
        int mtn = cfg.Limits.Mountains.Lower;

        for (int y = 0; y < w.Height; y++)
        {
            int moisture = 50;
            int mountainCount = 0;

            for (int x = 0; x < w.Width - 1; x++)
            {
                int current = w.Idx(x, y);
                int next = w.Idx(x + 1, y);

                // updateMoisture()
                w.Moisture[current] = moisture;
                int elevationDiff = w.Elevation[next] - w.Elevation[current];
                if (elevationDiff > 10) moisture = Math.Max(moisture - 1, 0);
                if (w.Elevation[next] <= sea) moisture += 1;

                // adjustCloudForElevation()
                if (w.Elevation[next] > mtn)
                {
                    mountainCount += 1;
                    if (elevationDiff > 0)
                    {
                        moisture = Math.Max(moisture - 1, 0);
                        w.Moisture[next] = moisture;
                    }
                }
                else
                {
                    mountainCount = Math.Max(mountainCount - 1, 0);
                }

                // adjustCloudForMountains() — rain shadow behind a range.
                if (mountainCount > 0 && w.Elevation[next] < mtn)
                    w.Desert[next] = true;

                // markDesertAreas(). Note this assigns unconditionally, so it overwrites the
                // rain-shadow flag set immediately above — latitude wins over orography in
                // ck2rpg. Kept as-is; changing it would visibly alter desert placement.
                bool inDesertLatitude =
                    y > w.DesertPointBottom + rng.Int(1, 10) &&
                    y < w.DesertPointTop + rng.Int(1, 10);
                w.Desert[next] = inDesertLatitude && moisture < 50;
            }
        }
    }
}
