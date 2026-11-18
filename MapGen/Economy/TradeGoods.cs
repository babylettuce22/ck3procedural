using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen;

/// <summary>
/// Which trade goods each county could produce: CK3 1.21's
/// <c>common/trade/county_potential_trade_goods</c>. Village and resource district buildings are
/// gated on <c>has_potential_for_trade_good</c>, so a county with no potential goods can build
/// almost nothing outside its holding, and vanilla's file names only vanilla counties.
///
/// Vanilla gives every county 2 to 6 goods (median 4). Which ones follows the land closely and
/// was measured rather than guessed: <see cref="TradeGoodTable"/> holds, for each good, its share
/// of vanilla counties and how much each terrain, a coast, a major river and each winter level
/// raise or lower its odds (vanilla's own counties, barony terrain mix, province adjacency,
/// winter_severity_bias). A county's odds for a good are the product of those lifts; see
/// <see cref="Draw"/> for how many it gets and which. Applied back to vanilla's own counties the
/// model returns vanilla's shares to within a point or two, and gives the goods a county really
/// has four times the odds of a uniform pick.
///
/// Vanilla's goods also clump far beyond what terrain explains: a county with amber or alum has a
/// neighbour with it 33 times more often than chance, tea 20 times, spices 14. Real goods sit in
/// deposits and regions. So each good gets a seeded, smooth "deposit" field over the map whose
/// strength grows with how clumped vanilla's placement is; a common good like wood barely feels it.
/// </summary>
public static class TradeGoods
{
    public const int MinGoods = 2, MaxGoods = 6;

    /// <summary>
    /// How hard the deposit field pulls, per natural log of vanilla's clumping ratio. Measured on a
    /// 1,033-county world (h121 seed 4242, county scale 0.5) with
    /// <c>ck3devtools\calibration\trade_goods_mine.py &lt;mod folder&gt;</c>, which reports the same
    /// neighbour-has-it ratio for a generated mod. At 1.5 common goods clump as vanilla's do (wood
    /// 1.0 against 1.2, cattle 1.2 against 1.3) and the regional ones about half as much (spices 6.9
    /// against 13.8, silk 11.7 against 12.5); at 1.0 they reached a third. Vanilla's tea and amber
    /// (20, 33) sit in one corner of a real map, which no noise field should copy.
    /// </summary>
    public const double DepositStrength = 1.5;

    /// <summary>Deposit features span about this many county widths.</summary>
    private const double DepositCounties = 5.0;

    private const int Stream = 0x7A6D;

    /// <summary>
    /// Each county's potential goods, keyed in table order (most common first). Counties without a
    /// barony on the map are left out.
    /// </summary>
    /// <param name="provinceTerrain">CK3 terrain per province id, as province_terrain writes it.</param>
    /// <param name="winter">winter_severity_bias per province id, as WinterWriter writes it.</param>
    public static Dictionary<Title, List<string>> Assign(int seed, IReadOnlyList<Title> counties,
        TerrainClass[] provinceTerrain, double[] winter, ProvinceSurvey survey, int mapWidth, int mapHeight)
    {
        var goods = TradeGoodTable.Goods;
        int landCounties = Math.Max(1, counties.Count);
        double countyWidth = Math.Sqrt((double)mapWidth * mapHeight * 0.35 / landCounties);
        double frequency = 1.0 / (DepositCounties * countyWidth);

        var fields = new SimplexNoise[goods.Length];
        for (int g = 0; g < goods.Length; g++)
            fields[g] = new SimplexNoise(Rng.For(seed, Stream, Rng.StableHash(goods[g].Key), 1));

        // First pass: each county's land, and its raw deposit draw for every good.
        var land = new List<(Title County, double[] Terrain, bool Coast, bool River, int Winter, double[] Deposit)>();
        foreach (var county in counties)
        {
            var baronies = county.Children.Where(b => b.ProvinceId >= 1 && b.ProvinceId < provinceTerrain.Length).ToList();
            if (baronies.Count == 0) continue;

            var terrainMix = new double[TradeGoodTable.Terrains.Length];
            bool coast = false, river = false;
            double cold = 0, x = 0, y = 0;
            foreach (var b in baronies)
            {
                int id = b.ProvinceId;
                terrainMix[TradeGoodTable.TerrainIndex(provinceTerrain[id])] += 1.0 / baronies.Count;
                if (id < survey.Coastal.Length) { coast |= survey.Coastal[id]; river |= survey.Riverside[id]; }
                if (id < winter.Length) cold += winter[id] / baronies.Count;
                if (id < survey.Position.Length) { x += survey.Position[id].X / baronies.Count; y += survey.Position[id].Y / baronies.Count; }
            }

            var deposit = new double[goods.Length];
            for (int g = 0; g < goods.Length; g++)
            {
                double z = Math.Clamp(Field.Fbm(fields[g], x * frequency, y * frequency, 3) / 0.25, -3, 3);
                deposit[g] = Math.Exp(DepositStrength * Math.Log(Math.Max(1.0, goods[g].Clumping)) * z);
            }
            land.Add((county, terrainMix, coast, river, TradeGoodTable.WinterBucket(cold), deposit));
        }

        // A deposit field only moves a good around the map. exp() of a symmetric field averages
        // above 1, and more so the stronger it is, so left alone it inflated the clumped goods
        // (amber in 10% of counties against vanilla's 1%); each good's field is rescaled to
        // average 1 over this world's counties.
        var mean = new double[goods.Length];
        foreach (var l in land) for (int g = 0; g < goods.Length; g++) mean[g] += l.Deposit[g] / land.Count;

        var result = new Dictionary<Title, List<string>>();
        var weight = new double[goods.Length];
        foreach (var (county, terrainMix, coast, river, bucket, deposit) in land)
        {
            for (int g = 0; g < goods.Length; g++)
            {
                var good = goods[g];
                double terrainLift = 0;
                for (int t = 0; t < terrainMix.Length; t++) terrainLift += terrainMix[t] * good.Terrain[t];

                weight[g] = good.Share * terrainLift * good.Coast[coast ? 0 : 1] * good.River[river ? 0 : 1]
                          * good.Winter[bucket] * deposit[g] / mean[g];
            }

            var rng = Rng.For(seed, Stream, county.Index);
            result[county] = Draw(weight, rng).Select(g => goods[g].Key).ToList();
        }
        return result;
    }

    /// <summary>Vanilla's goods-per-county counts, for 2 to 6 goods.</summary>
    private static readonly int[] CountFrequency = [189, 743, 1621, 993, 3];

    /// <summary>
    /// How many goods from vanilla's own count distribution, then which ones by systematic
    /// sampling: each good's odds scaled to sum to that count (capped at certainty), laid end to
    /// end in a shuffled order, and read at evenly spaced points. That draws exactly the count
    /// while every good keeps its odds. Independent draws at the same odds gave the right shares
    /// but spread counties from 2 to 6 goods almost evenly; weighted draws without replacement
    /// keep the count but squeeze the common goods (wood, in 64% of vanilla counties, near 45%).
    /// </summary>
    private static List<int> Draw(double[] weight, Rng rng)
    {
        int available = weight.Count(w => w > 0);
        if (available == 0) return [];

        double roll = rng.NextDouble() * CountFrequency.Sum();
        int n = MinGoods;
        for (int k = 0; k < CountFrequency.Length && (roll -= CountFrequency[k]) >= 0; k++) n = MinGoods + k + 1;
        n = Math.Min(Math.Min(n, MaxGoods), available);

        // Scale so the capped odds sum to n; each round moves the uncapped goods by what is left.
        double scale = n / weight.Sum();
        for (int round = 0; round < 12; round++)
        {
            double sum = 0, free = 0;
            foreach (double w in weight)
            {
                double p = w * scale;
                if (p >= 1) sum += 1; else { sum += p; free += w; }
            }
            if (free <= 0 || Math.Abs(sum - n) < 1e-9) break;
            scale += (n - sum) / free;
        }

        var order = Enumerable.Range(0, weight.Length).ToArray();
        for (int i = order.Length - 1; i > 0; i--)
        {
            int j = (int)(rng.NextDouble() * (i + 1));
            (order[i], order[j]) = (order[j], order[i]);
        }

        var picked = new List<int>(n);
        double start = rng.NextDouble(), edge = 0;
        int next = 0;
        foreach (int g in order)
        {
            edge += Math.Min(1, weight[g] * scale);
            while (next < n && start + next < edge) { picked.Add(g); next++; }
        }
        picked = picked.Distinct().ToList();   // only possible at odds of exactly 1 plus rounding
        picked.Sort();
        return picked;
    }
}
