using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.World;

namespace Ck3MapGen.MapGen;

/// <summary>
/// Places the two terrain classes that are not climate: <see cref="TerrainClass.Farmlands"/> and
/// <see cref="TerrainClass.Oasis"/>.
///
/// Both used to be unreachable, and their materials with them — farmland_01, medi_farmlands,
/// india_farmlands, farm_paddy_01 and oasis were five of the twenty-two materials our detail_index
/// never touched, against a vanilla map that paints 101 of 105.
///
/// The reason they were unreachable is that <see cref="TerrainClassifier"/> runs off elevation,
/// climate and coast distance, and neither of these follows from any of those. Farmland is ground
/// people cleared, so it follows settlement; an oasis is where groundwater surfaces, so it follows
/// drainage. Both are therefore decided here instead, late, once the title hierarchy, the
/// governments and the drainage network all exist.
///
/// Scarcity is the whole design. Measured off vanilla's own detail_index by summed blend weight,
/// the four farmland materials together are 0.33% of everything painted and oasis is 0.02% — the
/// rarest material in the game. Painting either as a biome, which is what an earlier moisture rule
/// did for farmland, puts fields across empty countryside and is the thing this replaces.
/// </summary>
public static class Cultivation
{
    /// <summary>Province ids, not labels — the same space <c>provinceTerrain</c> is indexed in.</summary>
    public sealed record Result(HashSet<int> Farmlands, HashSet<int> Oases);

    /// <summary>Per-province drainage summary, gathered in the one pass over the raster.</summary>
    private readonly record struct Water(float PeakFlow, float LakeDepth, int Area);

    /// <summary>
    /// Choose which provinces are cultivated, then rewrite both the pixel raster and the
    /// per-province vote so the painted ground and <c>common/province_terrain</c> agree.
    ///
    /// Called after governments and faiths are settled, so nothing social is decided from a
    /// terrain that only exists because of the settlement — development, government, culture and
    /// faith all read the pre-cultivation map, which is the right causality: the fields are there
    /// because the county was settled and watered, not the other way round.
    /// </summary>
    public static Result Apply(
        MapConfig cfg,
        ProvinceMap provinces,
        int[] order,
        int landCount,
        TerrainClass[] terrain,
        TerrainClass[] provinceTerrain,
        List<Title> counties,
        GovernmentMap governments,
        Dictionary<Title, int> development,
        WildernessMap wilderness,
        Drainage? drainage,
        float[] provinceElevation,
        Rng rng)
    {
        var water = SurveyWater(cfg, provinces, order, landCount, drainage, provinceElevation);

        var farmlands = ChooseFarmlands(cfg, landCount, provinceTerrain, counties, governments,
            development, wilderness, water, rng);

        var oases = ChooseOases(cfg, landCount, provinceTerrain, water, farmlands, rng);

        if (farmlands.Count == 0 && oases.Count == 0)
        {
            Console.WriteLine("  cultivation: no province qualified");
            return new Result(farmlands, oases);
        }

        foreach (int id in farmlands) provinceTerrain[id] = TerrainClass.Farmlands;
        foreach (int id in oases) provinceTerrain[id] = TerrainClass.Oasis;

        RepaintRaster(cfg, provinces, order, terrain, farmlands, oases);

        Console.WriteLine($"  cultivation: {farmlands.Count} farmland provinces " +
                          $"({100.0 * farmlands.Count / Math.Max(1, landCount):F2}% of land), " +
                          $"{oases.Count} oases");

        return new Result(farmlands, oases);
    }

    /// <summary>
    /// Peak drainage flow and deepest standing water in each province.
    ///
    /// <see cref="Drainage.LakeDepth"/> is the fill depth of a closed depression — how far the
    /// pit-filling had to raise a cell to drain it. Anywhere that is positive, water collects and
    /// has nowhere to go, which in a desert is the definition of an oasis and in temperate country
    /// marks the wet bottom land worth farming.
    /// </summary>
    private static Water[] SurveyWater(MapConfig cfg, ProvinceMap provinces, int[] order,
        int landCount, Drainage? drainage, float[] provinceElevation)
    {
        var water = new Water[provinces.Count + 1];
        int total = cfg.ProvinceWidth * cfg.ProvinceHeight;

        for (int i = 0; i < total; i++)
        {
            int id = order[provinces.Label[i]];
            if (id < 1 || id > landCount) continue;

            var cell = water[id];
            float flow = 0f, depth = 0f;

            if (drainage is not null && i < drainage.Flow.Length && drainage.LandMask[i] != 0)
            {
                flow = drainage.Flow[i];
                depth = drainage.LakeDepth(provinceElevation, i);
            }

            water[id] = new Water(
                Math.Max(cell.PeakFlow, flow),
                Math.Max(cell.LakeDepth, depth),
                cell.Area + 1);
        }

        return water;
    }

    /// <summary>
    /// The best-watered baronies of settled counties, capped at
    /// <see cref="MapConfig.FarmlandShare"/> of all land.
    ///
    /// Ranked globally rather than gated per county, because a per-county threshold delivers
    /// whatever share the thresholds happen to imply on this map's climate — and the share is the
    /// thing being calibrated. Ranking and cutting gives the same answer on a desert world as on a
    /// temperate one.
    /// </summary>
    private static HashSet<int> ChooseFarmlands(
        MapConfig cfg, int landCount, TerrainClass[] provinceTerrain, List<Title> counties,
        GovernmentMap governments, Dictionary<Title, int> development, WildernessMap wilderness,
        Water[] water, Rng rng)
    {
        var chosen = new HashSet<int>();
        int target = (int)Math.Round(landCount * Math.Clamp(cfg.FarmlandShare, 0, 0.25));
        if (target <= 0) return chosen;

        float flowReference = Reference(water);
        var scored = new List<(int Id, double Score)>();

        foreach (var county in counties)
        {
            // Nobody lives here, so nobody cleared it.
            if (wilderness.Contains(county)) continue;

            // Tribes do not terrace and ditch; vanilla's farmland sits under settled government.
            string government = governments.For(county);
            if (government == GovernmentMap.Tribal) continue;

            int dev = development.GetValueOrDefault(county, 0);

            foreach (var barony in county.Children)
            {
                int id = barony.ProvinceId;
                if (id < 1 || id > landCount) continue;
                if (!IsArable(provinceTerrain[id])) continue;

                // Water is the binding constraint on pre-modern farmland, so it dominates the
                // score; development breaks ties toward the counties with the people to work it.
                double wet = flowReference > 0
                    ? Math.Clamp(water[id].PeakFlow / flowReference, 0, 1.5)
                    : 0;

                double score = wet * 3.0
                             + Math.Clamp(dev / 30.0, 0, 1.5)
                             + Bonus(provinceTerrain[id])
                             + rng.NextDouble() * 0.35;

                scored.Add((id, score));
            }
        }

        foreach (var (id, _) in scored.OrderByDescending(s => s.Score).Take(target))
            chosen.Add(id);

        return chosen;
    }

    /// <summary>
    /// Desert provinces holding standing water, capped at <see cref="MapConfig.OasisShare"/> of
    /// the desert.
    /// </summary>
    private static HashSet<int> ChooseOases(MapConfig cfg, int landCount,
        TerrainClass[] provinceTerrain, Water[] water, HashSet<int> farmlands, Rng rng)
    {
        var chosen = new HashSet<int>();

        int desert = 0;
        for (int id = 1; id <= landCount; id++)
            if (provinceTerrain[id] is TerrainClass.Desert) desert++;

        int target = (int)Math.Round(desert * Math.Clamp(cfg.OasisShare, 0, 0.25));
        if (target <= 0) return chosen;

        float flowReference = Reference(water);
        var scored = new List<(int Id, double Score)>();

        for (int id = 1; id <= landCount; id++)
        {
            if (provinceTerrain[id] is not TerrainClass.Desert) continue;
            if (farmlands.Contains(id)) continue;

            // Sand with no water in it is just sand. Standing water is what makes the oasis, so
            // the fill depth of a closed depression leads and through-flow only breaks ties — a
            // wadi that drains away is not an oasis, a pan that holds is.
            var w = water[id];
            if (w.LakeDepth <= 0 && w.PeakFlow <= 0) continue;

            double score = w.LakeDepth * 2.0
                         + (flowReference > 0 ? w.PeakFlow / flowReference : 0)
                         + rng.NextDouble() * 0.25;

            scored.Add((id, score));
        }

        foreach (var (id, _) in scored.OrderByDescending(s => s.Score).Take(target))
            chosen.Add(id);

        return chosen;
    }

    /// <summary>
    /// The flow a well-watered province has, taken as a high percentile of the per-province peaks
    /// rather than as an absolute. Discharge is in cells-drained and so scales with map size and
    /// with how wet the climate model made this particular world; normalising against the map's own
    /// distribution is what keeps the farmland share the same on a rainforest map and a steppe one.
    /// </summary>
    private static float Reference(Water[] water)
    {
        var peaks = new List<float>();
        foreach (var w in water)
            if (w.PeakFlow > 0) peaks.Add(w.PeakFlow);

        if (peaks.Count == 0) return 0f;

        peaks.Sort();
        return peaks[(int)(peaks.Count * 0.90)];
    }

    /// <summary>Ground a plough can be put through.</summary>
    private static bool IsArable(TerrainClass t) => t is TerrainClass.Plains
        or TerrainClass.Floodplains or TerrainClass.Drylands or TerrainClass.Forest
        or TerrainClass.Steppe or TerrainClass.Wetlands or TerrainClass.Hills;

    /// <summary>
    /// How much the existing ground wants to be a field. River bottom land first, then open plain;
    /// hills and marsh are farmable but last in line, which keeps terraces and drained fen as the
    /// exceptions they should be.
    /// </summary>
    private static double Bonus(TerrainClass t) => t switch
    {
        TerrainClass.Floodplains => 1.2,
        TerrainClass.Plains => 1.0,
        TerrainClass.Drylands => 0.4,
        TerrainClass.Forest => 0.3,
        TerrainClass.Steppe => 0.2,
        TerrainClass.Wetlands => 0.0,
        TerrainClass.Hills => 0.0,
        _ => 0.0,
    };

    /// <summary>
    /// Stamp the chosen classes back onto the pixel raster the detail textures are painted from,
    /// so the ground under a farmland province is farmland rather than whatever climate put there.
    /// </summary>
    private static void RepaintRaster(MapConfig cfg, ProvinceMap provinces, int[] order,
        TerrainClass[] terrain, HashSet<int> farmlands, HashSet<int> oases)
    {
        int total = cfg.ProvinceWidth * cfg.ProvinceHeight;

        Parallel.For(0, total, i =>
        {
            // Beach survives cultivation: it is the shoreline material, and a farmland province
            // running down to the sea should still meet the water as a shore.
            if (terrain[i] is TerrainClass.Sea or TerrainClass.Beach) return;

            int id = order[provinces.Label[i]];
            if (farmlands.Contains(id)) terrain[i] = TerrainClass.Farmlands;
            else if (oases.Contains(id)) terrain[i] = TerrainClass.Oasis;
        });
    }
}
