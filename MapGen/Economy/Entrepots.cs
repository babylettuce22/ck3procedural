namespace Ck3MapGen.MapGen;

/// <summary>
/// Where CK3 1.21's entrepôts stand: the counties and provinces of vanilla's
/// <c>map_data/geographical_regions/entrepot_regions.txt</c>.
///
/// An entrepôt district cannot be built. Its slot exists only where the
/// <c>entrepot_district_slot</c> province history key or the <c>add_entrepot_district_slot</c>
/// effect puts one, and the base game does neither: no vanilla province history carries the key
/// and no base-game script calls the effect. What the base game does ship is the region file:
/// two lists of 42, a county and one province of it (its capital barony in 36 of them). Nothing in
/// base script or ck3.exe names either region, so the Silk and Silver DLC reads them to place the
/// slots. Owners of the DLC get their entrepôts from these two regions, and a player without the
/// DLC gets none, on a generated map as in vanilla.
///
/// Vanilla's 42 are the great trading cities. Measured against vanilla's 3,549 counties
/// (<c>ck3devtools\calibration\entrepot_mine.py</c>): their prosperity sits at the 89th percentile
/// (median), 64 % touch the sea against 36 % of all counties, and 29 % a major river against 14 %.
/// So each settled county is scored by its prosperity rank times the coast and river lifts those
/// shares give, and the best are taken in turn, each kept clear of the ones already chosen so the
/// hubs spread over the map as vanilla's spread over the world.
/// </summary>
public static class Entrepots
{
    /// <summary>Vanilla's count: 42 entrepôts over 3,549 counties.</summary>
    private const double PerCounty = 42.0 / 3549.0;

    // Lift = share among entrepôts / share among all counties, for having and for lacking it.
    private const double CoastLift = 0.64 / 0.36, InlandLift = 0.36 / 0.64;
    private const double RiverLift = 0.29 / 0.14, DryLift = 0.71 / 0.86;

    public static List<(Title County, Title Barony)> Pick(IReadOnlyList<Title> counties,
        Dictionary<Title, int> prosperity, ProvinceSurvey survey, WildernessMap? wilderness,
        int mapWidth, int mapHeight)
    {
        var candidates = new List<(Title County, Title Barony, double Prosperity, bool Coast, bool River, (double X, double Y) Pos)>();
        foreach (var county in counties)
        {
            if (wilderness?.Contains(county) == true) continue;
            // The county's seat, which the landed_titles writer puts first (SeatFirst).
            var capital = county.Capital;
            if (capital is null || capital.Tier != "b" || capital.ProvinceId <= 0
                || capital.ProvinceId >= survey.Position.Length) continue;

            bool coast = false, river = false;
            foreach (var b in county.Children)
            {
                if (b.Tier != "b" || b.ProvinceId <= 0 || b.ProvinceId >= survey.Coastal.Length) continue;
                coast |= survey.Coastal[b.ProvinceId];
                river |= survey.Riverside[b.ProvinceId];
            }
            candidates.Add((county, capital, prosperity.GetValueOrDefault(county), coast, river,
                survey.Position[capital.ProvinceId]));
        }
        if (candidates.Count == 0) return [];

        int want = Math.Min(candidates.Count, Math.Max(2, (int)Math.Round(candidates.Count * PerCounty)));

        // Prosperity as a rank, ties sharing their middle rank, so the score means the same on a
        // rich map as on a poor one.
        var levels = candidates.Select(c => c.Prosperity).OrderBy(v => v).ToList();
        double Percentile(double v)
        {
            int below = levels.Count(l => l < v), equal = levels.Count(l => l == v);
            return (below + 0.5 * equal) / levels.Count;
        }

        var ranked = candidates
            .Select(c => (c.County, c.Barony, c.Pos,
                Score: Percentile(c.Prosperity) * (c.Coast ? CoastLift : InlandLift) * (c.River ? RiverLift : DryLift)))
            .OrderByDescending(c => c.Score).ThenBy(c => c.County.Index)
            .ToList();

        // Clearance: half the spacing the hubs would have if laid evenly over the land, in county
        // widths (the same land estimate TradeGoods uses). Halved again whenever it leaves the
        // map short, so a cramped world still gets its count.
        double countyWidth = Math.Sqrt((double)mapWidth * mapHeight * 0.35 / candidates.Count);
        double clearance = 0.5 * Math.Sqrt((double)candidates.Count / want) * countyWidth;

        var picked = new List<(Title County, Title Barony, (double X, double Y) Pos)>();
        while (picked.Count < want && clearance > 1e-6)
        {
            foreach (var c in ranked)
            {
                if (picked.Count >= want) break;
                if (picked.Any(p => p.County == c.County)) continue;
                if (picked.Any(p => Math.Pow(p.Pos.X - c.Pos.X, 2) + Math.Pow(p.Pos.Y - c.Pos.Y, 2) < clearance * clearance)) continue;
                picked.Add((c.County, c.Barony, c.Pos));
            }
            clearance /= 2;
        }

        return picked.Select(p => (p.County, p.Barony)).OrderBy(p => p.County.Index).ToList();
    }
}
