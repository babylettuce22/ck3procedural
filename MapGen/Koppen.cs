namespace Ck3MapGen.MapGen;

/// <summary>
/// The Koppen-Geiger classes, in the standard letter scheme.
///
/// Only the classes a map can actually produce are listed, and the third letter (the a/b/c/d
/// temperature qualifier) is kept only where CK3 has terrain that depends on it — Dfb is forest and
/// Dfc is taiga, so that distinction earns its place, while Cfa and Cfb both paint as temperate
/// woodland and do not.
/// </summary>
public enum KoppenClass : byte
{
    /// <summary>Af — tropical rainforest.</summary>
    TropicalRainforest,
    /// <summary>Am — tropical monsoon.</summary>
    TropicalMonsoon,
    /// <summary>Aw/As — tropical savanna, with a real dry season.</summary>
    TropicalSavanna,

    /// <summary>BWh — hot desert.</summary>
    HotDesert,
    /// <summary>BWk — cold desert.</summary>
    ColdDesert,
    /// <summary>BSh — hot semi-arid steppe.</summary>
    HotSteppe,
    /// <summary>BSk — cold semi-arid steppe.</summary>
    ColdSteppe,

    /// <summary>Csa/Csb — dry-summer temperate, the Mediterranean climate.</summary>
    Mediterranean,
    /// <summary>Cfa/Cwa — humid subtropical.</summary>
    HumidSubtropical,
    /// <summary>Cfb/Cfc — oceanic.</summary>
    Oceanic,

    /// <summary>Dfa/Dfb and their w/s variants — humid continental.</summary>
    HumidContinental,
    /// <summary>Dfc/Dfd — subarctic.</summary>
    Subarctic,

    /// <summary>ET — tundra.</summary>
    Tundra,
    /// <summary>EF — ice cap.</summary>
    IceCap,

    /// <summary>Not land.</summary>
    Ocean,
}

/// <summary>
/// Koppen-Geiger classification, and the bridge from it to CK3's terrain vocabulary.
///
/// Koppen is the right target here for a reason that is not just realism: it is a classification of
/// *vegetation*, derived from the temperature and rainfall a plant actually experiences, and
/// vegetation is exactly what CK3's terrain types are. Desert, steppe, drylands, jungle, forest,
/// taiga and tundra are Koppen's own top-level distinctions under other names, which is why the
/// mapping at the bottom of this file is nearly one-to-one rather than a judgement call.
///
/// Two approximations are worth being explicit about, because the scheme is normally evaluated on
/// twelve monthly means and this model produces two seasonal ones:
///
///   * "Driest month" is taken as a sixth of the drier half-year. That understates how dry a sharply
///     seasonal place gets in its worst month, so the Af/Am/Aw boundaries sit slightly wetter than
///     they should. It does not affect the A/B/C/D/E tiers at all, which is where the map reads.
///   * The a/b/c qualifiers normally count months above ten degrees. Here they are taken off the
///     warmest month alone, which agrees with the month-counting rule everywhere except a narrow
///     band of highly continental coast.
/// </summary>
public static class Koppen
{
    /// <summary>
    /// Classifies one place from its yearly temperature and rainfall.
    ///
    /// The order is Koppen's own and is not interchangeable: aridity is tested *first*, because a
    /// place dry enough to be desert is desert whatever its temperature, and testing tropical first
    /// would paint the Sahara as savanna.
    /// </summary>
    /// <summary>
    /// Classifies one place from its yearly temperature and rainfall.
    ///
    /// Aridity (B) is evaluated first using smooth Hermite-interpolated seasonal offsets to
    /// prevent artificial step-cliffs and circular desert splotches across procedural noise fields.
    /// </summary>
    public static KoppenClass Classify(double warmestC, double coldestC, double meanC,
            double annualMm, double summerMm, double winterMm)
    {
        // --- 1. E: Polar (Tested FIRST so cold tundra/ice isn't misclassified as sand desert) ---
        // If the warmest month is under 10°C, it's too cold for trees regardless of rain.
        if (warmestC < 10.0)
            return warmestC < 0.0 ? KoppenClass.IceCap : KoppenClass.Tundra;

        // --- 2. B: Arid (Only tested if warm enough for non-polar vegetation) ---
        double summerShare = annualMm <= 0.0 ? 0.5 : Math.Clamp(summerMm / annualMm, 0.0, 1.0);
        double offset = 280.0 * (summerShare * summerShare * (3.0 - 2.0 * summerShare));
        double aridity = 20.0 * Math.Max(0.0, meanC) + offset;

        if (annualMm < aridity)
        {
            bool hot = meanC >= 18.0;
            if (annualMm < aridity * 0.5)
                return hot ? KoppenClass.HotDesert : KoppenClass.ColdDesert;

            return hot ? KoppenClass.HotSteppe : KoppenClass.ColdSteppe;
        }

        // --- 3. A: Tropical. Every month at or above 18°C.
        if (coldestC >= 18.0)
        {
            double driestMonth = Math.Min(summerMm, winterMm) / 6.0;
            if (driestMonth >= 60.0)
                return KoppenClass.TropicalRainforest;

            if (driestMonth >= 100.0 - annualMm / 25.0)
                return KoppenClass.TropicalMonsoon;

            return KoppenClass.TropicalSavanna;
        }

        // --- 4. D: Continental & Subarctic (Taiga) ---
        if (coldestC < -3.0)
        {
            double halfRange = (warmestC - coldestC) * 0.5;
            return meanC < 10.0 - halfRange * 0.5
                ? KoppenClass.Subarctic
                : KoppenClass.HumidContinental;
        }

        // --- 5. C: Temperate ---
        double driestSummerMonth = summerMm / 6.0;
        double wettestWinterMonth = winterMm / 6.0;

        if (driestSummerMonth < 40.0 && wettestWinterMonth >= 3.0 * driestSummerMonth)
            return KoppenClass.Mediterranean;

        return warmestC >= 22.0 ? KoppenClass.HumidSubtropical : KoppenClass.Oceanic;
    }

    public static TerrainClass Terrain(KoppenClass climate, double patch) => climate switch
    {
        // --- 1. TROPICAL (Lush jungle & savanna mosaic) ---
        KoppenClass.TropicalRainforest => TerrainClass.Jungle,
        KoppenClass.TropicalMonsoon => patch > 0.30 ? TerrainClass.Jungle : TerrainClass.Plains,
        KoppenClass.TropicalSavanna => patch > 0.65 ? TerrainClass.Jungle : TerrainClass.Plains,

        // --- 2. ARID & DESERT ---
        // Hot deserts in the subtropics get sand dunes; cold deserts in the north get windswept steppe
        KoppenClass.HotDesert => TerrainClass.Desert,
        KoppenClass.ColdDesert => TerrainClass.Steppe,
        KoppenClass.HotSteppe => TerrainClass.Drylands,
        KoppenClass.ColdSteppe => TerrainClass.Steppe,

        // --- 3. TEMPERATE & MEDITERRANEAN ---
        KoppenClass.Mediterranean => patch > 0.55 ? TerrainClass.Forest
                                        : patch > 0.20 ? TerrainClass.Drylands
                                        : TerrainClass.Plains,

        KoppenClass.HumidSubtropical => patch > 0.85 ? TerrainClass.Jungle
                                        : patch > 0.45 ? TerrainClass.Forest
                                        : TerrainClass.Plains,

        KoppenClass.Oceanic => patch > 0.45 ? TerrainClass.Forest : TerrainClass.Plains,
        KoppenClass.HumidContinental => patch > 0.35 ? TerrainClass.Forest : TerrainClass.Plains,

        // --- 4. BOREAL & POLAR (Clean taiga and arctic snow) ---
        KoppenClass.Subarctic => patch > 0.25 ? TerrainClass.Taiga : TerrainClass.Plains,
        KoppenClass.Tundra => TerrainClass.Arctic,
        KoppenClass.IceCap => TerrainClass.Arctic,

        _ => TerrainClass.Sea,
    };

    /// <summary>True where the ground is dry enough that bare rock, not forest, covers a mountain
    /// in it — which is the only thing the mountain classes need to know.</summary>
    public static bool IsArid(KoppenClass climate) => climate
        is KoppenClass.HotDesert or KoppenClass.ColdDesert
        or KoppenClass.HotSteppe or KoppenClass.ColdSteppe;

    /// <summary>True where a mountain carries permanent snow rather than rock.</summary>
    public static bool IsPolar(KoppenClass climate)
        => climate is KoppenClass.Tundra or KoppenClass.IceCap;

    /// <summary>The standard Koppen-Geiger map colours, so the debug render can be read against any
    /// published Koppen map without a legend.</summary>
    public static (byte R, byte G, byte B) Colour(KoppenClass climate) => climate switch
    {
        KoppenClass.TropicalRainforest => (0, 0, 254),
        KoppenClass.TropicalMonsoon => (0, 119, 255),
        KoppenClass.TropicalSavanna => (70, 169, 250),
        KoppenClass.HotDesert => (254, 0, 0),
        KoppenClass.ColdDesert => (254, 150, 149),
        KoppenClass.HotSteppe => (245, 163, 1),
        KoppenClass.ColdSteppe => (255, 219, 99),
        KoppenClass.Mediterranean => (255, 255, 0),
        KoppenClass.HumidSubtropical => (198, 255, 78),
        KoppenClass.Oceanic => (102, 255, 51),
        KoppenClass.HumidContinental => (0, 255, 255),
        KoppenClass.Subarctic => (55, 200, 255),
        KoppenClass.Tundra => (178, 178, 178),
        KoppenClass.IceCap => (104, 104, 104),
        _ => (28, 42, 66),
    };
}
