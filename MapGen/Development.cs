using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.Io;

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
    internal static double Support(TerrainClass t) => t switch
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
    /// The rank curve is shaped to vanilla's own 867 distribution. Measured from
    /// <c>history/titles</c>: of 4,669 county titles, 973 set development at all and the rest are
    /// implicitly 0. Among those that set one the median is 6, p90 is 12, and the ordinary map tops
    /// out at 20 — ten counties sit there. Exactly three counties in the world stand above it:
    /// Chang'an at 30, Rome and Constantinople at 25.
    ///
    /// The tail to 60 that this comment used to cite is 1178's, not 867's; vanilla's peak moves
    /// 30 → 40 → 60 across its three bookmarks, which is what the era bonus below is imitating.
    /// Counties above the ordinary top are world centres, placed by
    /// <see cref="MapConfig.WorldCenterDevPeak"/> rather than by the curve.
    /// </summary>
    public static Dictionary<Title, int> ForCounties(List<Title> counties,
        TerrainClass[] provinceTerrain, MapConfig cfg, Rng rng, WorldCenterMap? worldCenters = null,
        AzgaarImport? azgaar = null)
    {
        var scored = new List<(Title County, double Score)>(counties.Count);

        // Where an export says how many people live on each county, that answer replaces the
        // terrain guess — see PopulationScores. The terrain half still runs, because it is the
        // fallback for any county the export left unclaimed and because its median is what puts
        // the imported half on a comparable scale before the two are ranked against each other.
        var population = PopulationScores(counties, azgaar);
        var terrain = new List<(Title County, double Score, double Jitter, double People)>(counties.Count);

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
            //
            // Exactly one draw per county either way, so an imported map spends the stream the same
            // as a generated one and a run with no export is untouched by any of this.
            double jitter = rng.NextDouble();
            double people = population?.GetValueOrDefault(county) ?? 0;
            terrain.Add((county, score, jitter, people));
        }

        // The two scales meet at their medians. A population density is people per raster pixel —
        // a number in the thousandths — and a terrain score is a support weight between 0 and 1, so
        // ranking the raw values together would sort every unclaimed county above every settled one
        // rather than mixing them. Matching the medians of the counties that HAVE a population
        // against their own terrain scores puts an unclaimed county where its ground says it
        // belongs, on the scale the rest of the map is being read on.
        double scale = ImportedScale(terrain);

        foreach (var (county, score, jitter, people) in terrain)
        {
            scored.Add((county, people > 0
                // A fifth of the terrain jitter. There the noise is doing real work — it is what
                // stops a hundred identical plains counties landing on one number — but a
                // population is already all distinct, and a quarter either way would throw a county
                // past its neighbours for no reason the map shows.
                ? people * scale * (0.95 + 0.10 * jitter)
                : score * (0.75 + 0.5 * jitter)));
        }

        // Rank, then read the level off the rank. Ties are broken by the noise already folded into
        // the score, so counties on identical terrain do not all land on the same number.
        scored.Sort((a, b) => a.Score.CompareTo(b.Score));

        // Against the era year, not the calendar year: 867 here is vanilla's own baseline, so the
        // question being asked is "how far past vanilla's earliest bookmark is this world", which a
        // world with a calendar of its own answers through MapConfig.EraAnchorYear rather than
        // through what its people call the year.
        double yearsPassed = cfg.EraYear - 867;
        double yearDevBonus = yearsPassed / 50.0; // Subtracts 1 dev level per 50 years prior to 867

        double baseLevel = cfg.DevelopmentBase + yearDevBonus;

        // The top of the ordinary curve: what the best-ranked county gets before world centres are
        // considered, since the rank curve reaches exactly 1 at rank 1. Vanilla's equivalent is 20.
        double ordinaryTop = baseLevel + cfg.DevelopmentSpread * cfg.DevelopmentScale;

        // A world centre is placed at the top of the world, not above it.
        //
        // This used to be a flat +32 on top of whatever the curve gave, which put the five centres
        // of a default map at development 44-55 while the rest of it topped out at 22. Vanilla's
        // whole 867 map peaks at 30 in one county and has three above 20 in total, so the boost was
        // writing five 1178-era metropolises into a world dated 867.
        //
        // Now the best-scoring centre is placed at the peak and the others step down linearly
        // toward a little above the ordinary top — the shape vanilla has, of one great city, a few
        // near-peers, and then the pack. The peak rides the era bonus with everything else, so a
        // late-era world still has centres that stand clear of its own richer baseline.
        double centreFloor = ordinaryTop + 2;
        double peak = Math.Max(cfg.WorldCenterDevPeak + yearDevBonus, centreFloor);
        int centreCount = worldCenters?.Centers.Count ?? 0;

        // How much of the map sets no development at all. Capped below 1 so there is always a
        // settled part for the curve to describe, however the knob is set.
        double bareShare = Math.Clamp(cfg.DevelopmentBareShare, 0.0, 0.95);

        var result = new Dictionary<Title, int>(scored.Count);
        for (int i = 0; i < scored.Count; i++)
        {
            double rank = scored.Count == 1 ? 1.0 : i / (double)(scored.Count - 1);

            int level;
            if (rank < bareShare)
            {
                // Bare. Outside the development system rather than at the bottom of it, which is
                // the distinction vanilla draws: a tribal periphery sets no development at all, and
                // HistoryWriter writes nothing for a level of 0.
                //
                // Deliberately NOT lifted by the era bonus. A bare county in a more advanced world
                // is still a bare county — vanilla's own bare share is flat across its bookmarks at
                // 80%, 78% and 77%, while the counties that do set development rise from a median
                // of 6 to 16. Advancement deepens the settled part of the map; it does not colonise
                // the rest of it.
                level = Math.Max(0, cfg.DevelopmentBase);
            }
            else
            {
                // The settled part of the world, re-ranked among itself so the curve spans it fully
                // instead of spending most of its range on counties that are bare anyway.
                double settled = bareShare >= 1.0 ? 1.0 : (rank - bareShare) / (1.0 - bareShare);
                double curved = Math.Pow(Math.Clamp(settled, 0.0, 1.0), cfg.DevelopmentSkew);

                // Still reaches exactly 1 at rank 1, so the cut redistributes the curve without
                // lowering the top of it — ordinaryTop above stays the map's ceiling either way.
                level = (int)Math.Round(baseLevel + curved * cfg.DevelopmentSpread * cfg.DevelopmentScale);
            }

            int centreRank = worldCenters?.RankOf(scored[i].County) ?? -1;
            if (centreRank >= 0)
            {
                double t = centreCount <= 1 ? 0.0 : centreRank / (double)(centreCount - 1);
                int target = (int)Math.Round(peak - t * (peak - centreFloor));

                // Raises, never lowers. A centre that is also the best land on the map keeps the
                // curve's answer if that is already the higher of the two.
                level = Math.Max(level, target);
            }

            result[scored[i].County] = Math.Clamp(level, 0, 100);
        }

        return result;
    }

    /// <summary>
    /// How settled each county is according to the export, or null when there is no export.
    ///
    /// People per pixel, not people: development in CK3 is how developed a county is and not how
    /// large, and our counties vary in area by a factor of five even before an author's own
    /// province sizes are imported on top. Country people and townspeople are added together
    /// because both are the county's — Azgaar counts them separately only because it draws them
    /// separately — and both are already in the same points.
    ///
    /// This is a reanchoring in the sense <see cref="AzgaarClimate"/> is: the export decides where
    /// the people are, and everything else about development stays ours. The rank curve, the era
    /// bonus, the bare share and the world-centre peaks are all still applied on top, so an
    /// imported map has vanilla's distribution of development with the export's geography under it
    /// rather than Azgaar's raw numbers written into history files.
    ///
    /// Counties the export left empty — unclaimed ground, or a county that fell outside the
    /// canvas — get no entry and fall back to the terrain score.
    /// </summary>
    private static Dictionary<Title, double>? PopulationScores(List<Title> counties,
        AzgaarImport? azgaar)
    {
        if (azgaar is null || !azgaar.World.HasCells) return null;

        var scores = new Dictionary<Title, double>(counties.Count);
        double rural = 0, urban = 0;

        foreach (var county in counties)
        {
            if (azgaar.For(county) is not { } binding) continue;

            double density = binding.PopulationDensity;
            if (density <= 0) continue;

            scores[county] = density;
            rural += binding.RuralPopulation;
            urban += binding.UrbanPopulation;
        }

        if (scores.Count == 0) return null;

        // Points, not people: settings.populationRate is the multiplier Azgaar shows them through,
        // and nothing here needs a head count — only which county has more of them than which.
        Console.WriteLine($"  azgaar development: {scores.Count} of {counties.Count} counties " +
                          $"ranked on the export's own population ({rural:N0} country, " +
                          $"{urban:N0} town points)");

        return scores;
    }

    /// <summary>
    /// What one person per pixel is worth in terrain-score units, so the imported counties and the
    /// counties left to the terrain guess can be ranked in one list.
    ///
    /// Medians rather than means, because both distributions have long tails — a capital's county
    /// carries several times the population of an ordinary one — and a mean would let a single
    /// metropolis decide where every unclaimed county sits.
    /// </summary>
    private static double ImportedScale(
        List<(Title County, double Score, double Jitter, double People)> terrain)
    {
        var people = terrain.Where(t => t.People > 0).Select(t => t.People).OrderBy(p => p).ToList();
        if (people.Count == 0) return 1;

        var scores = terrain.Where(t => t.People > 0).Select(t => t.Score).OrderBy(s => s).ToList();

        double medianPeople = people[people.Count / 2];
        double medianScore = scores[scores.Count / 2];

        return medianPeople > 0 ? medianScore / medianPeople : 1;
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
    /// Left to the roll, at most ONE further barony is settled, with a city or a church depending
    /// on development and local terrain. A tribe can carry that second holding too, but far more
    /// rarely than a settled county would: a tribal ruler's city and temple vassals are how a tribe
    /// grows into something else, so a world with none of them can never start that.
    ///
    /// <paramref name="burg"/> is the settlement an Azgaar export drew on this barony, where there
    /// is one. It answers the half of the question the roll is really guessing at — whether
    /// anything is here — so where it stands, no roll decides that. Only the *kind* of place can
    /// still fall to the roll, and only for a burg the export drew no buildings on; see
    /// <see cref="AzgaarSettlement.Holding"/>, where that reservation is the load-bearing part.
    ///
    /// The converse does not hold: a barony with no burg is not a barony Azgaar has called empty,
    /// only one it drew no town on, so it still takes its chances. That asymmetry is the whole
    /// rule, and it is what makes the import safe — an export can add holdings to a map, never
    /// thin it. <paramref name="countySettledByExport"/> is the other side of the same coin: once
    /// a county's second holding has come from a burg, the roll does not add a third on top.
    ///
    /// A burg overrides the tribal cap, deliberately. The cap exists because a *guess* should
    /// rarely hand a tribe the city and temple vassals it needs to stop being one; it is a
    /// statement about tribes in general and not about this county. An author who drew a town in
    /// a tribal realm has made a statement about this county, and it wins.
    /// </summary>
    public static string Holding(int indexInCounty, TerrainClass terrain, int development,
        string government, Rng rng, AzgaarBurg? burg = null, bool countySettledByExport = false)
    {
        string capital = GovernmentMap.CapitalHolding(government);
        if (indexInCounty == 0) return capital;

        if (burg is not null)
        {
            // A nomad's county is the camp and nothing else whatever was drawn on it; see below.
            if (government == GovernmentMap.Nomad) return "none";

            // The burg is the answer to "is anything here". It is only the answer to "what is it"
            // when the export drew something on it — a keep, a market, a harbour, a lone temple.
            // An unflagged town falls through to the same city-or-church question the roll asks,
            // because that is a question about the county, not about the settlement.
            return Distinct(AzgaarSettlement.Holding(burg) ?? SettledKind(terrain, rng), capital);
        }

        // The export has already settled this county's second holding, on a barony it drew a town
        // on. Rolling for another one here would hand every county Azgaar bothered to populate an
        // extra holding on top, which is the one way importing a settlement layer could make the
        // map richer than the map it was imported from.
        if (countySettledByExport) return "none";

        if (indexInCounty > 1) return "none";

        // A horde's county is the camp and nothing else, the way vanilla writes the steppe. The
        // second holding would be a mayor or a bishop answering to a khan — a settled vassal inside
        // a nomadic realm, scored by the obedience rules and failing every one of them.
        if (government == GovernmentMap.Nomad) return "none";

        // Ramped across the development range rather than switched at a threshold, so there is no
        // single number where counties suddenly all sprout a town.
        double chance = Math.Clamp((development - 6) / 18.0, 0.0, 0.85);
        if (government == GovernmentMap.Tribal) chance = Math.Min(chance, 0.12);
        if (rng.NextDouble() > chance) return "none";

        return Distinct(SettledKind(terrain, rng), capital);
    }

    /// <summary>
    /// City or church, on ground that supports one of them.
    ///
    /// An oasis is a market as much as a garden — it is where the caravan stops — so it counts as
    /// productive alongside the fields and the bottom land.
    /// </summary>
    private static string SettledKind(TerrainClass terrain, Rng rng)
    {
        bool productive = terrain is TerrainClass.Plains or TerrainClass.Farmlands
            or TerrainClass.Floodplains or TerrainClass.Beach or TerrainClass.Oasis;

        return rng.NextDouble() < (productive ? 0.65 : 0.30) ? "city_holding" : "church_holding";
    }

    /// <summary>
    /// The county's second holding, made sure to be something other than its first.
    ///
    /// A republic's capital is already the city and a theocracy's is already the church. Doubling
    /// it would spend the county's one extra holding on a second of what it has, so the other is
    /// built instead and those counties end up the two-holding ones — which is what a trading city
    /// or a cathedral town should be. A castle is exempt: a county may hold two, and vanilla's do.
    /// </summary>
    private static string Distinct(string second, string capital)
        => second != capital || second == "castle_holding" ? second
            : capital == "city_holding" ? "church_holding" : "city_holding";
}
