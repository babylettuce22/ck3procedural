using Ck3MapGen.Config;
using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen;

/// <summary>
/// Turns the terrain a county sits on into how rich it is and what its baronies hold.
///
/// Every county currently comes out identical — no development is written at all, so CK3 leaves
/// them at zero, and only the capital barony gets a holding. That makes the map uniform in exactly
/// the way a player notices first. The geography needed to fix it is already computed and thrown
/// away: which terrain each province is, whether it reaches the sea, how big it is.
///
/// Development is a *county* property in CK3 (<c>change_development_level</c> in history/titles),
/// while holdings are a *barony* property (history/provinces), so the two halves are written by
/// different emitters and derived together here to keep them consistent — a rich county should be
/// the one with the filled-in holdings.
/// </summary>
public static class Development
{
    /// <summary>
    /// How much settlement each terrain supports, 0..1. These are agricultural carrying capacity
    /// as a medieval map would read it, not beauty: a floodplain feeds a city, a mountain does not.
    /// </summary>
    private static double Support(TerrainClass t) => t switch
    {
        TerrainClass.Farmlands => 1.00,
        TerrainClass.Floodplains => 0.95,
        TerrainClass.Plains => 0.80,
        TerrainClass.Beach => 0.70,   // a coast is a road when roads are bad
        TerrainClass.Forest => 0.55,
        TerrainClass.Hills => 0.45,
        TerrainClass.Jungle => 0.35,
        TerrainClass.Wetlands => 0.35,
        TerrainClass.Steppe => 0.30,
        TerrainClass.Drylands => 0.25,
        TerrainClass.Taiga => 0.25,
        TerrainClass.Desert => 0.08,
        TerrainClass.Mountains => 0.10,
        TerrainClass.DesertMountains => 0.04,
        TerrainClass.Arctic => 0.03,
        _ => 0.30,
    };

    /// <summary>
    /// Development per county title.
    ///
    /// Keyed by the <see cref="Title"/> itself rather than by its key string, because development
    /// is now computed *before* titles are named — a title is named in the language of whoever
    /// lives there, cultures are placed partly on how rich a county is, and a dictionary keyed on
    /// names that do not exist yet cannot express that.
    ///
    /// Counties are ranked against each other and the level comes from the rank, not from the raw
    /// terrain score. That is the same reasoning the terrain classifier uses for its hill, mountain
    /// and moisture thresholds, and for the same reason: an absolute cut-off gives a wildly
    /// different answer per map. Scored absolutely, a map that came out all jungle and desert had a
    /// median development of 2 against vanilla's 8 — not because it was poor, but because nothing
    /// on it scored well on a scale built for temperate farmland. Ranking asks "how good is this
    /// county *for this world*", which is the question that survives a change of climate settings.
    ///
    /// The rank curve is shaped to vanilla's own 867 distribution, measured over the 3,827 counties
    /// that set one: mass between 0 and 16, median near 8, a thin tail to 60 — a handful of
    /// Constantinoples above a great many backwaters.
    /// </summary>
    public static Dictionary<Title, int> ForCounties(List<Title> counties,
        TerrainClass[] provinceTerrain, MapConfig cfg, Rng rng, WorldCenterMap? worldCenters = null)
    {
        var scored = new List<(Title County, double Score)>(counties.Count);

        foreach (var county in counties)
        {
            double total = 0;
            int counted = 0;
            bool coastal = false;

            foreach (var barony in county.Children)
            {
                if (barony.ProvinceId < 0 || barony.ProvinceId >= provinceTerrain.Length) continue;
                var t = provinceTerrain[barony.ProvinceId];
                total += Support(t);
                counted++;
                if (t == TerrainClass.Beach) coastal = true;
            }

            double score = counted == 0 ? 0.3 : total / counted;
            if (coastal) score = Math.Min(1.0, score + cfg.DevelopmentCoastBonus);

            // Per-county variation, so two counties on identical ground still differ. Multiplicative
            // rather than additive: it should not lift a desert into a breadbasket.
            score *= 0.75 + 0.5 * rng.NextDouble();
            scored.Add((county, score));
        }

        // Rank, then read the level off the rank. Ties are broken by the noise already folded into
        // the score, so counties on identical terrain do not all land on the same number.
        scored.Sort((a, b) => a.Score.CompareTo(b.Score));

        double yearsPassed = cfg.StartYear - 867;
        double yearDevBonus = yearsPassed / 50.0; // Subtracts 1 dev level per 50 years prior to 867

        var result = new Dictionary<Title, int>(scored.Count);
        for (int i = 0; i < scored.Count; i++)
        {
            double rank = scored.Count == 1 ? 1.0 : i / (double)(scored.Count - 1);
            double curved = Math.Pow(rank, cfg.DevelopmentSkew);

            double baseLevel = cfg.DevelopmentBase + yearDevBonus;
            int level = (int)Math.Round(baseLevel + curved * cfg.DevelopmentSpread * cfg.DevelopmentScale);

            // Boost World Centers to make them true metropolises
            if (worldCenters is not null && worldCenters.IsCenter(scored[i].County))
            {
                level += cfg.WorldCenterDevBoost;
            }

            result[scored[i].County] = Math.Clamp(level, 0, 100);
        }

        return result;
    }

    /// <summary>The terrain most of a county sits on, which is the terrain the county *is*.</summary>
    public static TerrainClass DominantTerrain(Title county, TerrainClass[] provinceTerrain)
    {
        var counts = new Dictionary<TerrainClass, int>();
        foreach (var barony in county.Children)
        {
            if (barony.ProvinceId < 0 || barony.ProvinceId >= provinceTerrain.Length) continue;
            var t = provinceTerrain[barony.ProvinceId];
            counts[t] = counts.GetValueOrDefault(t) + 1;
        }

        return counts.Count == 0
            ? TerrainClass.Plains
            : counts.OrderByDescending(kv => kv.Value).ThenBy(kv => (int)kv.Key).First().Key;
    }

    /// <summary>
    /// The holding a barony gets.
    ///
    /// The capital is not a choice: it is whatever the county's government seats its ruler in — see
    /// <see cref="GovernmentMap.CapitalHolding"/> — because each government names exactly one
    /// <c>primary_holding</c> and a ruler on anything else cannot hold his own seat.
    ///
    /// At most ONE further barony is settled, with a city or a church depending on development and
    /// local terrain. A tribe can carry that second holding too, but far more rarely than a settled
    /// county would: a tribal ruler's city and temple vassals are how a tribe grows into something
    /// else, so a world with none of them can never start that.
    /// </summary>
    public static string Holding(int indexInCounty, TerrainClass terrain, int development,
        string government, Rng rng)
    {
        string capital = GovernmentMap.CapitalHolding(government);
        if (indexInCounty == 0) return capital;
        if (indexInCounty > 1) return "none";

        // Ramped across the development range rather than switched at a threshold, so there is no
        // single number where counties suddenly all sprout a town.
        double chance = Math.Clamp((development - 6) / 18.0, 0.0, 0.85);
        if (government == GovernmentMap.Tribal) chance = Math.Min(chance, 0.12);
        if (rng.NextDouble() > chance) return "none";

        bool productive = terrain is TerrainClass.Plains or TerrainClass.Farmlands
            or TerrainClass.Floodplains or TerrainClass.Beach;

        string second = rng.NextDouble() < (productive ? 0.65 : 0.30) ? "city_holding" : "church_holding";

        // A republic's capital is already the city and a theocracy's is already the church. Doubling
        // it would spend the county's one extra holding on a second of what it has, so the other is
        // built instead and those counties end up the two-holding ones — which is what a trading
        // city or a cathedral town should be.
        return second != capital ? second
            : capital == "city_holding" ? "church_holding" : "city_holding";
    }
}
