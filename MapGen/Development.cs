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
    /// Development per county title, keyed by title key.
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
    public static Dictionary<string, int> ForCounties(List<Title> counties,
        TerrainClass[] provinceTerrain, MapConfig cfg, Rng rng)
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

        var result = new Dictionary<string, int>(scored.Count);
        for (int i = 0; i < scored.Count; i++)
        {
            double rank = scored.Count == 1 ? 1.0 : i / (double)(scored.Count - 1);
            double curved = Math.Pow(rank, cfg.DevelopmentSkew);
            int level = (int)Math.Round(cfg.DevelopmentBase + curved * cfg.DevelopmentSpread
                                        * cfg.DevelopmentScale);

            result[scored[i].County.Key] = Math.Clamp(level, 0, 100);
        }

        return result;
    }

    /// <summary>
    /// The holding a barony gets. The first barony of a county is its capital and must hold a
    /// castle — CK3 needs somewhere for the count to live, and a county whose capital is empty is
    /// not playable.
    ///
    /// At most ONE further barony is ever settled, and only in a county developed enough to deserve
    /// it. That cap is deliberate: filling every slot makes each county a dense cluster of
    /// holdings, which is neither what vanilla looks like nor what the map should read as at 867 —
    /// most of a county is countryside, and a second settlement is a mark of a place doing well.
    /// Every barony past the second is empty however rich the county is.
    ///
    /// So development shows on the ground and not only in a number: a poor county is a lone castle,
    /// a prosperous one has a town or an abbey beside it. Cities want flat productive land or a
    /// coast; where the ground will not carry one, the second holding is a church instead.
    /// </summary>
    public static string Holding(int indexInCounty, TerrainClass terrain, int development, Rng rng)
    {
        if (indexInCounty == 0) return "castle_holding";
        if (indexInCounty > 1) return "none";

        // Ramped across the development range rather than switched at a threshold, so there is no
        // single number where counties suddenly all sprout a town.
        double chance = Math.Clamp((development - 6) / 18.0, 0.0, 0.85);
        if (rng.NextDouble() > chance) return "none";

        bool productive = terrain is TerrainClass.Plains or TerrainClass.Farmlands
            or TerrainClass.Floodplains or TerrainClass.Beach;

        return rng.NextDouble() < (productive ? 0.65 : 0.30) ? "city_holding" : "church_holding";
    }
}
