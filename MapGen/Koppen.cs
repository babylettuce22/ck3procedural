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
    public static KoppenClass Classify(double warmestC, double coldestC, double meanC,
        double annualMm, double summerMm, double winterMm)
    {
        // --- B: arid. The threshold is a temperature, because warm places lose more to evaporation
        // and so need more rain to be anything other than desert. The seasonal offset is Koppen's:
        // rain that falls in the hot half of the year is worth less than rain that falls in the cold
        // half, because more of it leaves again.
        double summerShare = annualMm <= 0 ? 0.5 : summerMm / annualMm;
        double offset = summerShare >= 0.7 ? 280 : summerShare >= 0.3 ? 140 : 0;
        double aridity = 20 * meanC + offset;

        if (annualMm < aridity)
        {
            bool hot = meanC >= 18;
            if (annualMm < aridity / 2) return hot ? KoppenClass.HotDesert : KoppenClass.ColdDesert;
            return hot ? KoppenClass.HotSteppe : KoppenClass.ColdSteppe;
        }

        // --- E: polar. No month warm enough for trees.
        if (warmestC < 10) return warmestC < 0 ? KoppenClass.IceCap : KoppenClass.Tundra;

        // --- A: tropical. Every month above 18, so nothing is ever limited by cold.
        if (coldestC >= 18)
        {
            double driestMonth = Math.Min(summerMm, winterMm) / 6.0;
            if (driestMonth >= 60) return KoppenClass.TropicalRainforest;

            // Am is the monsoon case: a genuinely dry season, but a wet season heavy enough to carry
            // rainforest through it. Koppen's own sliding threshold.
            if (driestMonth >= 100 - annualMm / 25.0) return KoppenClass.TropicalMonsoon;
            return KoppenClass.TropicalSavanna;
        }

        if (coldestC < -3)
        {
            // Dfc/Dfd against Dfa/Dfb — boreal forest against broadleaf, and the line where taiga
            // starts. Koppen's own rule counts months at or above ten degrees and calls it subarctic
            // below four of them, which two seasonal means cannot answer directly. It can be
            // answered exactly, though, rather than guessed at: for a year that runs as a sine of
            // mean m and half-range A, the months above ten come to (12/pi)*acos((10-m)/A), and
            // setting that to four gives (10-m)/A = 1/2. So four months is m = 10 - A/2, and the
            // test below is Koppen's rule rearranged, not an approximation of it.
            //
            // It matters. Yakutsk's warmest month is +19, so a warmest-month test calls it
            // temperate-continental; its annual mean of -9 against a half-range of 27 puts it a
            // long way under this line, which is why it is taiga.
            double halfRange = (warmestC - coldestC) / 2.0;
            return meanC < 10 - halfRange / 2.0
                ? KoppenClass.Subarctic
                : KoppenClass.HumidContinental;
        }

        // --- C: temperate. The s/w/f split, and it matters: a dry-summer temperate climate carries
        // scrub and olive groves where a wet-summer one at the same temperature carries oak.
        double driestSummerMonth = summerMm / 6.0;
        double wettestWinterMonth = winterMm / 6.0;

        if (driestSummerMonth < 40 && wettestWinterMonth >= 3 * driestSummerMonth)
            return KoppenClass.Mediterranean;

        return warmestC >= 22 ? KoppenClass.HumidSubtropical : KoppenClass.Oceanic;
    }

    /// <summary>
    /// The CK3 terrain a Koppen class carries, before elevation has its say.
    ///
    /// <paramref name="patch"/> is a 0-1 noise sample, and is what keeps a climate zone from being
    /// painted one flat colour: real vegetation inside one climate is a mosaic of woodland and open
    /// ground, and the share of each is what differs between climates rather than the presence of
    /// one at all. So each class names two terrains and a share, and the noise decides per pixel.
    /// </summary>
    public static TerrainClass Terrain(KoppenClass climate, double patch) => climate switch
    {
        // Closed canopy either way; the monsoon belt thins out at its edges.
        KoppenClass.TropicalRainforest => TerrainClass.Jungle,
        KoppenClass.TropicalMonsoon => patch > 0.25 ? TerrainClass.Jungle : TerrainClass.Plains,

        // Savanna is grass with trees in it, which is CK3's plains with jungle through the wetter
        // parts and drylands where the dry season bites.
        KoppenClass.TropicalSavanna => patch > 0.72 ? TerrainClass.Jungle
            : patch < 0.28 ? TerrainClass.Drylands
            : TerrainClass.Plains,

        KoppenClass.HotDesert => TerrainClass.Desert,
        KoppenClass.ColdDesert => patch > 0.7 ? TerrainClass.Steppe : TerrainClass.Desert,
        KoppenClass.HotSteppe => patch > 0.45 ? TerrainClass.Drylands : TerrainClass.Desert,
        KoppenClass.ColdSteppe => patch > 0.3 ? TerrainClass.Steppe : TerrainClass.Drylands,

        // Mediterranean scrub: open, dry-looking ground with woodland in the folds.
        KoppenClass.Mediterranean => patch > 0.68 ? TerrainClass.Forest
            : patch > 0.35 ? TerrainClass.Drylands
            : TerrainClass.Plains,

        KoppenClass.HumidSubtropical => patch > 0.88 ? TerrainClass.Jungle
            : patch > 0.5 ? TerrainClass.Forest
            : TerrainClass.Plains,

        // Oceanic is the most cleared climate on Earth and the most wooded by default; the split is
        // deliberately even.
        KoppenClass.Oceanic => patch > 0.5 ? TerrainClass.Forest : TerrainClass.Plains,

        KoppenClass.HumidContinental => patch > 0.35 ? TerrainClass.Forest : TerrainClass.Plains,
        KoppenClass.Subarctic => patch > 0.2 ? TerrainClass.Taiga : TerrainClass.Plains,

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
