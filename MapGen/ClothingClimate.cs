using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen;

/// <summary>
/// Dresses a people for the weather they live in.
///
/// A culture's <c>clothing_gfx</c> used to be drawn blind — one vanilla look per heritage, picked
/// uniformly from every culture vanilla ships — so a people on the tundra was as likely to wear
/// West African robes as furs, and vanilla's thirty-one African looks made that the single likeliest
/// outcome. Players noticed. Vanilla's own chains are regional, and a region is mostly a climate:
/// Sámi dress is cut for a -13 °C January, Abbasid dress for a 34 °C July.
///
/// So every vanilla chain head is given the climate of the ground it was drawn for — coldest-month
/// and warmest-month mean and annual rainfall, which are the three numbers the
/// <see cref="ClimateModel"/> already produces per pixel — and a look is chosen by how close its
/// home is to the ground under it. Rainfall is the weakest of the three on purpose: it is what
/// separates Mongol felt from Norse wool at the same temperature, and Bedouin robes from Bantu
/// wraps at the same heat, but a wet desert is still a desert to a tailor.
///
/// Two grains. A heritage's whole look (coat of arms, buildings, units, dress) is picked for the
/// heritage's mean climate, which keeps a people's look coherent; a culture of that heritage whose
/// own ground the heritage's dress does not suit is then re-dressed alone — only its clothing, the
/// rest stays its kin's. That second pass is the one the feedback asked for: a heritage that runs
/// from a warm coast up into the taiga should have its northern cousins in furs.
///
/// The theme filter (<see cref="Config.MapConfig.CultureAestheticsTheme"/>) still decides the
/// pool; climate only chooses within it. A player who asked for a Norse world gets Norse dress in
/// the desert too, just the least unsuitable one.
/// </summary>
public static class ClothingClimate
{
    /// <summary>The weather a tailor cares about, as coldest month, warmest month, and a year's rain.</summary>
    public readonly record struct Climate(double ColdC, double WarmC, double AnnualMm);

    /// <summary>
    /// Below this fit a culture's heritage dress is considered wrong for its ground and it is
    /// re-dressed. Fit is a Gaussian of a normalised distance, so 0.3 is about 1.55 of the
    /// tolerances below — roughly a 9 °C miss on the coldest month with the rest right.
    /// </summary>
    private const double KeepFit = 0.30;

    /// <summary>How far off a look's home a month may be before it stops fitting, in °C.</summary>
    private const double ColdToleranceC = 6.0;
    private const double WarmToleranceC = 5.0;

    /// <summary>Rainfall tolerance in natural-log units — a factor of about three either way.</summary>
    private const double RainTolerance = 1.1;

    /// <summary>
    /// Where each vanilla clothing gfx was drawn for, keyed by the chain head without its
    /// <c>_clothing_gfx</c> suffix. Figures are rounded climatologies of the historical homeland
    /// the gfx dresses (Scandinavia for northern, the Pontic steppe and Transoxiana for turkic,
    /// Lower Iraq for dde_abbasid, the West African savanna and forest for african), not of the
    /// whole of whatever vanilla culture happens to wear it. Covers every head vanilla's cultures
    /// write; a modded head falls through to the next token of its chain, which is how the engine
    /// reads the chain too.
    /// </summary>
    private static readonly Dictionary<string, Climate> Homes = new(StringComparer.Ordinal)
    {
        // Subarctic and far north.
        ["sami"] = new(-13, 13, 450),
        ["ugro_permian"] = new(-15, 17, 550),
        ["nivkh"] = new(-18, 16, 500),
        ["jurchen"] = new(-18, 23, 550),
        ["ainu"] = new(-7, 20, 1100),
        ["emishi"] = new(-3, 23, 1300),
        ["northern"] = new(-7, 17, 600),
        ["east_slavic"] = new(-8, 19, 600),
        ["fp1_norse"] = new(-2, 15, 900),

        // Cold, dry steppe.
        ["mongol"] = new(-20, 18, 250),
        ["khitan"] = new(-12, 23, 400),
        ["turkic"] = new(-8, 24, 300),
        ["uyghur"] = new(-8, 26, 100),
        ["tangut"] = new(-8, 22, 250),

        // Temperate.
        ["west_slavic"] = new(-3, 18, 550),
        ["pommeranian"] = new(-1, 17, 600),
        ["swabian"] = new(0, 18, 800),
        ["dde_hre"] = new(0, 18, 700),
        ["western"] = new(3, 17, 750),
        ["french"] = new(4, 19, 700),
        ["english"] = new(4, 16, 750),
        ["norman"] = new(5, 17, 800),
        ["welsh"] = new(4, 15, 1300),
        ["cornish"] = new(6, 16, 1100),
        ["breton"] = new(6, 17, 900),
        ["korean"] = new(-3, 25, 1300),
        ["chinese"] = new(2, 27, 900),
        ["japanese"] = new(4, 26, 1500),
        ["dali"] = new(9, 20, 1000),

        // Mediterranean and the dry south.
        ["iberian_christian"] = new(6, 21, 650),
        ["byzantine"] = new(7, 25, 650),
        ["iranian"] = new(2, 28, 250),
        ["iberian_muslim"] = new(10, 26, 550),
        ["afr_berber"] = new(11, 28, 300),
        ["dde_abbasid"] = new(10, 34, 150),
        ["mena"] = new(13, 32, 150),

        // Tropics.
        ["indian"] = new(18, 31, 1100),
        ["viet"] = new(16, 29, 1700),
        ["tai"] = new(22, 30, 1400),
        ["african"] = new(22, 28, 1100),
        ["southeast_asian"] = new(26, 28, 2400),
        ["malay"] = new(26, 28, 2500),
        ["papuan"] = new(26, 27, 3000),
    };

    /// <summary>
    /// The climate a clothing chain was drawn for — its first head this table knows — or null for a
    /// chain made entirely of modded gfx. <paramref name="chain"/> is the value as the culture file
    /// writes it, braces and all.
    /// </summary>
    public static Climate? HomeOf(string chain)
    {
        foreach (string token in chain.Trim().Trim('{', '}').Split(
                     [' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string head = token.EndsWith("_clothing_gfx", StringComparison.Ordinal)
                ? token[..^"_clothing_gfx".Length]
                : token;
            if (Homes.TryGetValue(head, out var home)) return home;
        }

        return null;
    }

    /// <summary>
    /// How well dress made for <paramref name="home"/> suits <paramref name="at"/>, from 1 (made for
    /// exactly this) toward 0. A chain nobody has placed scores a flat, low figure: it is neither
    /// ruled out nor preferred over anything that is known to fit.
    /// </summary>
    public static double Fit(Climate at, Climate? home)
    {
        if (home is not { } h) return 0.05;

        double cold = (at.ColdC - h.ColdC) / ColdToleranceC;
        double warm = (at.WarmC - h.WarmC) / WarmToleranceC;
        double rain = Math.Log((Math.Max(0, at.AnnualMm) + 50) / (h.AnnualMm + 50)) / RainTolerance;

        return Math.Exp(-0.5 * (cold * cold + warm * warm + rain * rain));
    }

    /// <summary>Whether <paramref name="chain"/> is dress a people at <paramref name="at"/> would keep.</summary>
    public static bool Suits(Climate at, string chain) => Fit(at, HomeOf(chain)) >= KeepFit;

    /// <summary>
    /// Every land province's mean climate, indexed by province id — sea and river provinces are left
    /// at default. Averaged over the province's own pixels rather than read at its seed, for the same
    /// reason terrain is voted rather than sampled: a seed on a mountainside is not the province.
    /// </summary>
    public static Climate[] ByProvince(ClimateField field, ProvinceMap provinces, int[] order, int landCount)
    {
        var result = new Climate[landCount + 1];
        if (field.MeanC.Length != provinces.Label.Length) return result;

        var cold = new double[landCount + 1];
        var warm = new double[landCount + 1];
        var rain = new double[landCount + 1];
        var count = new int[landCount + 1];

        for (int i = 0; i < provinces.Label.Length; i++)
        {
            int id = order[provinces.Label[i]];
            if (id < 1 || id > landCount) continue;

            cold[id] += field.ColdC[i];
            warm[id] += field.WarmC[i];
            rain[id] += field.AnnualMm[i];
            count[id]++;
        }

        for (int id = 1; id <= landCount; id++)
            if (count[id] > 0)
                result[id] = new(cold[id] / count[id], warm[id] / count[id], rain[id] / count[id]);

        return result;
    }

    /// <summary>
    /// The mean climate over some counties' baronies, each barony counting once — or null where
    /// there is no climate to read, which every caller treats as "pick as before".
    /// </summary>
    public static Climate? Of(IEnumerable<Title> counties, Climate[]? byProvince)
    {
        if (byProvince is null) return null;

        double cold = 0, warm = 0, rain = 0;
        int n = 0;

        foreach (var county in counties)
            foreach (var barony in county.Children)
            {
                int id = barony.ProvinceId;
                if (id <= 0 || id >= byProvince.Length) continue;

                var c = byProvince[id];
                cold += c.ColdC;
                warm += c.WarmC;
                rain += c.AnnualMm;
                n++;
            }

        return n == 0 ? null : new Climate(cold / n, warm / n, rain / n);
    }

    /// <summary>
    /// A heritage's whole look, chosen for its climate. Takes exactly one draw from
    /// <paramref name="rng"/> whatever the pool, the same as the uniform pick it replaces, so every
    /// later draw in the culture stage — names, traditions, dynasties — is unchanged by it.
    /// </summary>
    public static VanillaVocabulary.Look PickLook(IReadOnlyList<VanillaVocabulary.Look> pool,
        Climate? at, Rng rng)
    {
        if (at is not { } climate) return rng.Pick(pool);

        return Weighted(pool, l => Fit(climate, HomeOf(l.ClothingGfx)), rng.NextDouble());
    }

    /// <summary>
    /// The clothing chain for one culture: its heritage's unless that is wrong for the culture's own
    /// ground, in which case a look from the pool that suits it better lends its dress. Draws from
    /// <paramref name="rng"/> only when it re-dresses.
    ///
    /// Kin dress first: a chain sharing a gfx with the heritage's — <c>{ sami northern }</c> for a
    /// <c>{ fp1_norse northern }</c> heritage — keeps the cousins looking like cousins, and on the
    /// first test world a Norse heritage's dry-cold edge otherwise went to Tangut dress with plain
    /// northern fitting just as well. Then anything that suits; then, where nothing in the pool
    /// suits, anything that is at least an improvement. The heritage's own chain can be the best
    /// the pool has — a Norse-themed world has nothing better for its desert — and then it stays.
    /// </summary>
    public static string ClothingFor(string heritageChain, Climate? at,
        IReadOnlyList<VanillaVocabulary.Look> pool, Rng rng)
    {
        if (at is not { } climate) return heritageChain;

        double kept = Fit(climate, HomeOf(heritageChain));
        if (kept >= KeepFit) return heritageChain;

        var kin = Tokens(heritageChain);
        var better = pool.Select(l => (Look: l, Fit: Fit(climate, HomeOf(l.ClothingGfx))))
                         .Where(e => e.Fit > kept).ToList();

        var candidates = better.Where(e => e.Fit >= KeepFit && Tokens(e.Look.ClothingGfx).Overlaps(kin)).ToList();
        if (candidates.Count == 0) candidates = better.Where(e => e.Fit >= KeepFit).ToList();
        if (candidates.Count == 0) candidates = better;
        if (candidates.Count == 0) return heritageChain;

        return Weighted([.. candidates.Select(e => e.Look)], l => Fit(climate, HomeOf(l.ClothingGfx)),
            rng.NextDouble()).ClothingGfx;
    }

    /// <summary>The gfx names in a chain, braces and whitespace dropped.</summary>
    private static HashSet<string> Tokens(string chain)
        => chain.Trim().Trim('{', '}').Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Roulette over the pool. Weighted per look rather than per chain, so a chain vanilla gives to
    /// many cultures is proportionally likelier — the same bias the uniform pick always had, now
    /// only among looks that suit.
    /// </summary>
    private static VanillaVocabulary.Look Weighted(IReadOnlyList<VanillaVocabulary.Look> pool,
        Func<VanillaVocabulary.Look, double> weight, double roll)
    {
        var weights = new double[pool.Count];
        double total = 0;
        for (int i = 0; i < pool.Count; i++) total += weights[i] = weight(pool[i]);

        // Every weight underflowed to zero, which takes a climate some forty tolerances from every
        // look in the pool. Not reachable on a real map; any look beats none.
        if (total <= 0) return pool[0];

        double target = roll * total;
        for (int i = 0; i < pool.Count; i++)
        {
            target -= weights[i];
            if (target < 0) return pool[i];
        }

        return pool[^1];
    }
}
