using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// Turns a <see cref="TerrainClass"/> and the climate under it into the up-to-four weighted
/// materials CK3 blends per pixel.
///
/// Material values index <c>gfx/map/terrain/materials.settings</c> in file order — vanilla
/// annotates that list "reliant on material index, so don't change the order of these". The
/// indices below were read out of that file, not guessed. Ten <c>mountain_02*</c> entries in the
/// middle of it are commented out and so consume no index, which is why the numbers here run ten
/// behind a naive count of the <c>name =</c> lines.
///
/// The <c>gen_*</c> family (indices 55-104, masks in <c>masks_gen/</c>) is a ready-made
/// **climate x landform matrix**: seven climate families, each with a base, three or four lowland
/// variants, hills, mountain and mountain_transition. Both axes are used here — the climate picks
/// the family and the terrain class picks the row — because using only one of them is what made
/// the map look flat. Measured against vanilla's own detail_index, painting every non-arid
/// landform out of the Central family put <c>gen_central_base</c> on 44% of all pixels and left
/// 70 of 105 materials untouched, where vanilla's heaviest single material is 10.7% and it uses
/// 101. Deserts and the far north suffered worst: four materials each, repeating.
///
/// Every pixel gets a dominant material plus two or three siblings, and the interchangeable
/// lowland variants are *rotated by noise* rather than fixed — vanilla runs its northern lowlands
/// at 9.1/8.2/7.3%, a genuine three-way mix, and that rotation is what reads as ground rather than
/// as tiling.
/// </summary>
public static class TerrainPalette
{
    /// <summary>An unused layer: material 255, weight 0.</summary>
    public const byte Unused = 255;

    /// <summary>
    /// What a pixel is painted from, packed into one byte: the terrain class in the low five bits
    /// and the climate family in the high three.
    ///
    /// The two are packed together because the transition band has to be drawn around *either*
    /// changing. Once the climate picks the material family, two stretches of the same terrain
    /// class in different climates are as different on the ground as two terrain classes are — a
    /// plain running from mediterranean into central scrub swaps its whole palette — and a band
    /// that only knew about terrain classes would leave that edge as a hard seam.
    ///
    /// Five bits, not four: <see cref="TerrainClass"/> reached seventeen members when
    /// <see cref="TerrainClass.Oasis"/> was added, and a nibble tops out at sixteen. Seven climate
    /// families fit the remaining three bits with one to spare.
    /// </summary>
    public static byte Label(TerrainClass terrain, KoppenClass zone)
        => (byte)((int)terrain | ((int)ClimateOf(zone) << 5));

    public static TerrainClass TerrainOf(byte label) => (TerrainClass)(label & 0x1F);

    public static Climate ClimateFromLabel(byte label) => (Climate)(label >> 5);

    // --- Classic materials, used where a feature is sharper than a climate family, and as the
    // --- per-climate accents that keep a biome from being only its four gen_ textures.
    private const byte Beach = 6;
    private const byte BeachMediterranean = 7;
    private const byte BeachPebbles = 8;
    private const byte DesertFlat = 14;
    private const byte DesertRocky = 15;
    private const byte DesertWavy = 16;
    private const byte DesertWavyLarger = 17;
    private const byte Desert01 = 11;
    private const byte Desert02 = 12;
    private const byte DesertCracked = 13;
    private const byte Drylands01 = 18;
    private const byte DrylandsCracked = 19;
    private const byte DrylandsGrassy = 20;
    private const byte FarmPaddy = 21;
    private const byte Farmland = 22;
    private const byte Floodplains = 23;
    private const byte ForestJungle = 24;
    private const byte ForestLeaf = 25;
    private const byte ForestPine = 26;
    private const byte ForestFloor = 27;
    private const byte Hills01 = 28;
    private const byte HillsRocks = 29;
    private const byte HillsRocksMedi = 30;
    private const byte HillsRocksSmall = 31;
    private const byte IndiaFarmlands = 32;
    private const byte MediDryMud = 33;
    private const byte MediFarmlands = 34;
    private const byte MediGrass = 35;
    private const byte MediLumpyGrass = 36;
    private const byte MediNoisyGrass = 37;
    private const byte MudWet = 38;          // seafloor
    private const byte NorthernPlains = 39;
    private const byte Plains01 = 41;
    private const byte PlainsDry = 42;
    private const byte PlainsDryMud = 43;
    private const byte PlainsNoisy = 44;
    private const byte PlainsRough = 45;
    private const byte Snow = 46;
    private const byte SteppeBushes = 47;
    private const byte SteppeGrass = 48;
    private const byte SteppeRocks = 49;
    private const byte Wetlands = 50;
    private const byte WetlandsMud = 51;
    private const byte CentralMountain = 52;
    private const byte CentralLowlands02 = 53;
    private const byte CentralLowlands03 = 54;
    private const byte Oasis = 40;

    /// <summary>
    /// Which of the seven <c>gen_*</c> climate families a pixel's ground belongs to. This is the
    /// axis that used to be collapsed onto Central for everything that was not sand.
    /// </summary>
    public enum Climate : byte
    {
        Tropical, Central, Steppe, Desert, Drylands, Northern, Mediterranean,
    }

    /// <summary>
    /// One climate family of the gen_* matrix. <see cref="Lowlands"/> holds the base plus every
    /// lowland variant; they are interchangeable by design and get rotated by noise.
    /// </summary>
    private readonly record struct Family(byte[] Lowlands, byte Hills, byte Mountain, byte Transition);

    // Index ranges read directly from materials.settings, ordered to match Climate.
    private static readonly Family[] Families =
    [
        new([55, 56, 57, 58], 59, 60, 61),        // Tropical
        new([62, 63, 64, 65], 66, 67, 68),        // Central
        new([69, 70, 71, 72], 73, 74, 75),        // Steppe
        new([76, 77, 78, 79, 80], 81, 82, 83),    // Desert
        new([84, 85, 86, 87], 88, 89, 90),        // Drylands
        new([91, 92, 93, 94], 95, 96, 97),        // Northern
        new([98, 99, 100, 101], 102, 103, 104),   // Mediterranean
    ];

    /// <summary>
    /// The older hand-made ground textures that suit each climate. Mixed in under the gen_ set as a
    /// fourth layer: they are what vanilla still leans on for its plains, its dunes and its dry
    /// grass, and none of them were reachable while every landform resolved to one family.
    /// </summary>
    private static readonly byte[][] Accents =
    [
        [ForestJungle, ForestFloor, ForestLeaf],                        // Tropical
        [Plains01, PlainsNoisy, PlainsRough, ForestFloor,
         CentralLowlands02, CentralLowlands03],                         // Central
        [SteppeGrass, SteppeBushes, SteppeRocks],                       // Steppe
        [DesertWavy, DesertWavyLarger, DesertFlat, DesertRocky,
         Desert01, Desert02],                                           // Desert
        [Drylands01, DrylandsGrassy, DrylandsCracked, MediDryMud],      // Drylands
        [NorthernPlains, ForestPine, PlainsRough],                      // Northern
        [MediGrass, MediLumpyGrass, MediNoisyGrass, PlainsDry],         // Mediterranean
    ];

    /// <summary>
    /// The climate family a Koppen zone paints in.
    ///
    /// Koppen is already a vegetation classification and the gen_ families are already vegetation
    /// textures, so this is close to a rename. The two judgement calls: hot semi-arid takes the
    /// drylands set rather than the steppe set (BSh is Sahel scrub, not Eurasian grass), and humid
    /// continental takes the northern set rather than the central one, because vanilla paints
    /// Poland and Russia out of gen_northern.
    /// </summary>
    public static Climate ClimateOf(KoppenClass zone) => zone switch
    {
        KoppenClass.TropicalRainforest or KoppenClass.TropicalMonsoon
            or KoppenClass.TropicalSavanna => Climate.Tropical,

        KoppenClass.HotDesert or KoppenClass.ColdDesert => Climate.Desert,
        KoppenClass.HotSteppe => Climate.Drylands,
        KoppenClass.ColdSteppe => Climate.Steppe,

        KoppenClass.Mediterranean => Climate.Mediterranean,
        KoppenClass.HumidSubtropical or KoppenClass.Oceanic => Climate.Central,

        KoppenClass.HumidContinental or KoppenClass.Subarctic
            or KoppenClass.Tundra or KoppenClass.IceCap => Climate.Northern,

        _ => Climate.Central,
    };

    /// <summary>Four material slots and their blend weights, as CK3 stores them.</summary>
    public struct Blend
    {
        public byte M0, M1, M2, M3;
        public byte W0, W1, W2, W3;
    }

    /// <summary>
    /// Two *different* lowland variants from a family, chosen by noise.
    ///
    /// Guaranteed distinct: the second is an offset from the first rather than an independent draw,
    /// so a pixel never spends two of its four slots on the same texture and the pair still walks
    /// the whole set as the noise moves. Picking both independently collapses them together often
    /// enough to leave visible patches of single-texture ground.
    /// </summary>
    private static (byte First, byte Second) LowlandPair(in Family family, double nA, double nB)
    {
        var set = family.Lowlands;
        int count = set.Length;
        int a = (int)(Math.Clamp(nA, 0, 0.999999) * count);
        int b = (a + 1 + (int)(Math.Clamp(nB, 0, 0.999999) * (count - 1))) % count;
        return (set[a], set[b]);
    }

    private static byte Accent(Climate climate, double n)
    {
        var set = Accents[(int)climate];
        return set[(int)(Math.Clamp(n, 0, 0.999999) * set.Length)];
    }

    /// <summary>
    /// Build the blend for one pixel.
    /// </summary>
    /// <param name="terrain">What the ground is — which row of the matrix.</param>
    /// <param name="climate">What the weather is — which family.</param>
    /// <param name="relief">0 at sea level, 1 at the mountain line — drives hills/mountain mixing.</param>
    /// <param name="nA">Noise selecting which lowland variant dominates, 0..1.</param>
    /// <param name="nB">Noise selecting the second variant, 0..1.</param>
    /// <param name="nC">Noise selecting the accent and setting how strongly it shows through, 0..1.</param>
    public static Blend For(TerrainClass terrain, Climate climate, double relief,
        double nA, double nB, double nC)
    {
        ref readonly var family = ref Families[(int)climate];

        switch (terrain)
        {
            case TerrainClass.Sea:
                {
                    double shallow = Math.Clamp(1.0 + relief * 3.0, 0, 1);
                    if (shallow <= 0.02) return Single(MudWet);

                    return Mix(
                        MudWet, (byte)(255 - shallow * 110),
                        Beach, (byte)(shallow * 90),
                        WetlandsMud, (byte)(shallow * 45 * nC),
                        Unused, 0
                    );
                }

            case TerrainClass.Beach:
                {
                    // Sand is not the same colour the world over, and vanilla has three shores.
                    byte sand = climate switch
                    {
                        Climate.Mediterranean or Climate.Drylands => BeachMediterranean,
                        Climate.Northern => BeachPebbles,
                        _ => Beach,
                    };
                    var (lowA, lowB) = LowlandPair(family, nA, nB);

                    return Mix(
                        sand, 160,
                        lowA, (byte)(40 + nA * 30),
                        lowB, (byte)(30 + nB * 25),
                        Accent(climate, nC), (byte)(15 + nC * 20)
                    );
                }

            case TerrainClass.Floodplains:
                {
                    var (lowA, _) = LowlandPair(family, nA, nB);
                    return Mix(
                        Floodplains, (byte)(110 + nA * 40),
                        WetlandsMud, (byte)(50 + nB * 30),
                        lowA, (byte)(40 + (1.0 - nA) * 30),
                        PlainsDryMud, (byte)(20 + nC * 20)
                    );
                }

            case TerrainClass.Wetlands:
                {
                    var (lowA, _) = LowlandPair(family, nA, nB);
                    return Mix(
                        Wetlands, (byte)(120 + nA * 40),
                        WetlandsMud, (byte)(70 + nB * 30),
                        lowA, (byte)(40 + (1.0 - nA) * 20),
                        ForestFloor, (byte)(15 + nC * 15)
                    );
                }

            case TerrainClass.Farmlands:
                {
                    // Fields are built, not grown, so they follow the people farming them.
                    byte fields = climate switch
                    {
                        Climate.Tropical => nC < 0.5 ? FarmPaddy : IndiaFarmlands,
                        Climate.Mediterranean or Climate.Drylands => MediFarmlands,
                        _ => Farmland,
                    };
                    var (lowA, lowB) = LowlandPair(family, nA, nB);

                    return Mix(
                        fields, (byte)(100 + nA * 50),
                        lowA, (byte)(60 + (1.0 - nA) * 40),
                        lowB, (byte)(50 + nB * 30),
                        Accent(climate, nC), (byte)(20 + nC * 20)
                    );
                }

            case TerrainClass.Oasis:
                {
                    // The oasis material is the green itself, so it leads, and the desert it sits
                    // in shows through the other three slots — an oasis reads as an oasis only
                    // against sand. Wet mud at the waterline, dune and cracked pan around it.
                    ref readonly var around = ref Families[(int)Climate.Desert];
                    var (lowA, _) = LowlandPair(around, nA, nB);

                    return Mix(
                        Oasis, (byte)(130 + nA * 50),
                        lowA, (byte)(55 + (1.0 - nA) * 35),
                        WetlandsMud, (byte)(35 + nB * 30),
                        nC < 0.5 ? DesertWavy : DesertCracked, (byte)(25 + nC * 25)
                    );
                }

            case TerrainClass.Forest:
                {
                    // Needleleaf in the cold, broadleaf in the warm, and the litter under both.
                    byte canopy = climate is Climate.Northern ? ForestPine
                                : climate is Climate.Tropical ? ForestJungle
                                : nC < 0.45 ? ForestPine : ForestLeaf;
                    var (lowA, _) = LowlandPair(family, nA, nB);

                    return Mix(
                        ForestFloor, (byte)(100 + nA * 40),
                        canopy, (byte)(80 + (1.0 - nA) * 40),
                        lowA, (byte)(40 + nB * 30),
                        family.Hills, (byte)(20 + nC * 20)
                    );
                }

            case TerrainClass.Jungle:
                {
                    var (lowA, lowB) = LowlandPair(Families[(int)Climate.Tropical], nA, nB);
                    return Mix(
                        ForestJungle, (byte)(100 + nA * 40),
                        lowA, (byte)(70 + (1.0 - nA) * 40),
                        lowB, (byte)(50 + nB * 30),
                        nC < 0.5 ? ForestFloor : Families[(int)Climate.Tropical].Hills,
                            (byte)(20 + nC * 20)
                    );
                }

            case TerrainClass.Taiga:
                {
                    // Always the northern set regardless of the Koppen call — taiga *is* the
                    // northern family's own biome, and a warm-side subarctic pixel painted out of
                    // Central was one of the seams in the far north.
                    ref readonly var north = ref Families[(int)Climate.Northern];
                    var (lowA, lowB) = LowlandPair(north, nA, nB);

                    return Mix(
                        lowA, (byte)(90 + nA * 40),
                        ForestPine, (byte)(75 + (1.0 - nA) * 40),
                        lowB, (byte)(50 + nB * 35),
                        nC < 0.35 ? Snow : nC < 0.7 ? NorthernPlains : north.Hills,
                            (byte)(25 + nC * 30)
                    );
                }

            case TerrainClass.Arctic:
                {
                    ref readonly var north = ref Families[(int)Climate.Northern];
                    var (lowA, lowB) = LowlandPair(north, nA, nB);

                    // Heavy snow over northern ground, exposing what the wind scours bare.
                    return Mix(
                        Snow, (byte)(120 + (1.0 - nC) * 80),
                        lowA, (byte)(60 + nA * 40),
                        lowB, (byte)(35 + nB * 30),
                        nC < 0.5 ? north.Hills : north.Mountain, (byte)(20 + nC * 30)
                    );
                }

            case TerrainClass.Steppe:
                {
                    ref readonly var steppe = ref Families[(int)Climate.Steppe];
                    var (lowA, lowB) = LowlandPair(steppe, nA, nB);

                    return Mix(
                        lowA, (byte)(90 + nA * 40),
                        SteppeGrass, (byte)(75 + (1.0 - nA) * 40),
                        lowB, (byte)(45 + nB * 30),
                        nC < 0.45 ? SteppeBushes : nC < 0.8 ? SteppeRocks : steppe.Hills,
                            (byte)(25 + nC * 25)
                    );
                }

            case TerrainClass.Drylands:
                {
                    ref readonly var dry = ref Families[(int)Climate.Drylands];
                    var (lowA, lowB) = LowlandPair(dry, nA, nB);

                    // medi_dry_mud and plains_01_dry_mud are the sun-baked flats between the scrub,
                    // and together are 0.70% of vanilla's painted weight — more than the entire
                    // farmland family. Both were previously unreachable: medi_dry_mud sat in the
                    // drylands accent list, which this case never consults, and plains_01_dry_mud
                    // only appeared under Floodplains, which nothing assigns.
                    return Mix(
                        lowA, (byte)(90 + nA * 40),
                        lowB, (byte)(75 + (1.0 - nA) * 40),
                        nB < 0.4 ? DrylandsGrassy : nB < 0.75 ? Drylands01 : DrylandsCracked,
                            (byte)(50 + nB * 30),
                        nC < 0.3 ? DesertCracked : nC < 0.5 ? MediDryMud
                            : nC < 0.68 ? PlainsDryMud : dry.Hills, (byte)(25 + nC * 25)
                    );
                }

            case TerrainClass.Desert:
                {
                    ref readonly var desert = ref Families[(int)Climate.Desert];
                    var (lowA, lowB) = LowlandPair(desert, nA, nB);

                    // Dunes are the thing a desert is missing without them. desert_wavy is one of
                    // vanilla's twenty heaviest materials and we shipped none of it.
                    byte dune = nB < 0.55 ? DesertWavy : DesertWavyLarger;

                    // desert_01 and desert_02 are vanilla's plain sand — 0.35% of its painted
                    // weight between them — and were unreachable while the fourth slot only ever
                    // offered cracked/flat/rocky/hills.
                    byte grain = nC < 0.25 ? DesertCracked
                               : nC < 0.42 ? Desert02
                               : nC < 0.55 ? DesertFlat
                               : nC < 0.68 ? Desert01
                               : nC < 0.85 ? DesertRocky
                               : desert.Hills;

                    return Mix(
                        lowA, (byte)(100 + nA * 40),
                        lowB, (byte)(75 + (1.0 - nA) * 40),
                        dune, (byte)(55 + nB * 40),
                        grain, (byte)(25 + nC * 25)
                    );
                }

            case TerrainClass.Plains:
                {
                    var (lowA, lowB) = LowlandPair(family, nA, nB);
                    return Mix(
                        lowA, (byte)(80 + nA * 40),
                        lowB, (byte)(70 + (1.0 - nA) * 40),
                        Accent(climate, nB), (byte)(50 + nB * 30),
                        nC < 0.6 ? family.Hills : Accent(climate, 1.0 - nC), (byte)(30 + nC * 20)
                    );
                }

            case TerrainClass.Hills:
                return HillBlend(family, climate, relief, nA, nB, nC);

            case TerrainClass.Mountains:
                return MountainBlend(family, relief, nA, nC);

            case TerrainClass.DesertMountains:
                return MountainBlend(Families[(int)Climate.Desert], relief, nA, nC);

            default:
                {
                    var (lowA, lowB) = LowlandPair(family, nA, nB);
                    return Mix(
                        lowA, (byte)(90 + nA * 50),
                        lowB, (byte)(70 + (1.0 - nA) * 40),
                        family.Hills, (byte)(50 + nB * 30),
                        Accent(climate, nC), (byte)(25 + nC * 20)
                    );
                }
        }
    }

    /// <summary>The bare rock a family's hills break out into.</summary>
    private static byte HillRock(Climate climate, double n) => climate switch
    {
        Climate.Mediterranean => HillsRocksMedi,
        Climate.Desert or Climate.Drylands => n < 0.5 ? HillsRocks : DesertRocky,
        _ => n < 0.4 ? HillsRocks : n < 0.75 ? HillsRocksSmall : Hills01,
    };

    private static Blend HillBlend(in Family family, Climate climate, double relief,
        double nA, double nB, double nC)
    {
        byte toMountain = (byte)(30 + Math.Clamp(relief, 0, 1) * 70);
        var (lowA, _) = LowlandPair(family, nA, nB);

        return Mix(
            family.Hills, (byte)(100 + nA * 30),
            lowA, (byte)(70 - toMountain / 3 + (1.0 - nA) * 20),
            family.Transition, toMountain,
            HillRock(climate, nC), (byte)(30 + nB * 25)
        );
    }

    private static Blend MountainBlend(in Family family, double relief, double nA, double nC)
    {
        double above = Math.Clamp(relief - 1.0, 0, 1);
        byte snow = (byte)Math.Clamp(above * 240 + nC * 50 - 25, 0, 255);

        return Mix(
            family.Mountain, 140,
            CentralMountain, 60,
            family.Transition, (byte)(45 + nA * 35),
            Snow, snow
        );
    }

    /// <summary>
    /// Combine two blends, giving <paramref name="b"/> a share of <paramref name="t"/>.
    ///
    /// This is what makes one biome fade into the next. Inside a biome every pixel resolves to the
    /// same palette and the four layers only vary by noise, which looks right; at a *boundary* the
    /// palette switched wholesale from one pixel to the next, so desert met steppe on a clean line
    /// no amount of within-biome blending could soften. Mixing the two neighbouring palettes gives
    /// a real transition, in which materials from both are simultaneously on the ground.
    ///
    /// Duplicate materials are summed rather than given two slots, and only the four heaviest
    /// survive — CK3 blends exactly four layers per pixel (materials_limit in detail_data.settings).
    ///
    /// That truncation is the reason a transition band used to look mottled rather than graded.
    /// Eight candidate materials compete for four slots, and as the mix strength walks across the
    /// band the fourth and fifth swap places — one texture vanishing and another appearing, both at
    /// whatever weight the cut happened to fall on. The swap traces a closed contour, so the band
    /// filled with hard-edged patches. The fix is to fade the fourth slot out as it approaches the
    /// fifth: at the moment they swap it carries no weight, so which of the two won stops mattering
    /// and the seam has nothing to draw. The weight it gives up goes to the three slots above it,
    /// which keeps the pixel's total intensity where the unmerged blends put it.
    /// </summary>
    public static Blend Merge(Blend a, Blend b, double t)
    {
        t = Math.Clamp(t, 0, 1);

        Span<byte> materials = stackalloc byte[8];
        Span<double> weights = stackalloc double[8];

        materials[0] = a.M0; weights[0] = a.W0 * (1 - t);
        materials[1] = a.M1; weights[1] = a.W1 * (1 - t);
        materials[2] = a.M2; weights[2] = a.W2 * (1 - t);
        materials[3] = a.M3; weights[3] = a.W3 * (1 - t);
        materials[4] = b.M0; weights[4] = b.W0 * t;
        materials[5] = b.M1; weights[5] = b.W1 * t;
        materials[6] = b.M2; weights[6] = b.W2 * t;
        materials[7] = b.M3; weights[7] = b.W3 * t;

        // Fold duplicates down onto their first occurrence.
        for (int i = 0; i < 8; i++)
        {
            if (weights[i] <= 0 || materials[i] == Unused) { weights[i] = 0; continue; }
            for (int j = 0; j < i; j++)
                if (weights[j] > 0 && materials[j] == materials[i])
                {
                    weights[j] += weights[i];
                    weights[i] = 0;
                    break;
                }
        }

        // Selection sort for the top five — four to keep, and the fifth only to know how close the
        // fourth came to losing its slot. Cheaper than sorting all eight.
        var result = new Blend { M0 = Unused, M1 = Unused, M2 = Unused, M3 = Unused };
        Span<byte> outM = stackalloc byte[4];
        Span<byte> outW = stackalloc byte[4];
        Span<double> kept = stackalloc double[4];
        outM[0] = outM[1] = outM[2] = outM[3] = Unused;
        outW[0] = outW[1] = outW[2] = outW[3] = 0;
        kept[0] = kept[1] = kept[2] = kept[3] = 0;

        double runnerUp = 0;
        for (int slot = 0; slot < 5; slot++)
        {
            int best = -1;
            double bestWeight = 0;
            for (int i = 0; i < 8; i++)
                if (weights[i] > bestWeight) { bestWeight = weights[i]; best = i; }

            if (best < 0) break;
            if (slot == 4) { runnerUp = bestWeight; break; }

            outM[slot] = materials[best];
            kept[slot] = bestWeight;
            weights[best] = 0;
        }

        // Fade the fourth slot out as the fifth catches up, and hand what it gives up to the slots
        // above so the pixel keeps the same total intensity.
        if (outM[3] != Unused && runnerUp > 0)
        {
            double surrendered = Math.Min(runnerUp, kept[3]);
            double above = kept[0] + kept[1] + kept[2];
            kept[3] -= surrendered;

            if (above > 0)
            {
                double scale = 1.0 + surrendered / above;
                kept[0] *= scale; kept[1] *= scale; kept[2] *= scale;
            }

            // Nothing left to draw with: drop the slot rather than write a floor weight of 1, which
            // would put a texture on the ground at exactly the strength this is trying to remove.
            if (kept[3] <= 0.5) outM[3] = Unused;
        }

        for (int slot = 0; slot < 4; slot++)
            if (outM[slot] != Unused)
                outW[slot] = (byte)Math.Clamp((int)Math.Round(kept[slot]), 1, 255);

        // Each slot's weight is gated on its *material* being real. Testing outW[0] against Unused
        // here compared a weight against 255 and so silently zeroed the dominant layer of any pixel
        // whose top weight saturated — which is reachable, the merge path being where weights are
        // summed.
        result.M0 = outM[0]; result.M1 = outM[1]; result.M2 = outM[2]; result.M3 = outM[3];
        result.W0 = outM[0] == Unused ? (byte)0 : outW[0];
        result.W1 = outM[1] == Unused ? (byte)0 : outW[1];
        result.W2 = outM[2] == Unused ? (byte)0 : outW[2];
        result.W3 = outM[3] == Unused ? (byte)0 : outW[3];
        return result;
    }

    private static Blend Single(byte material) => new()
    {
        M0 = material, M1 = Unused, M2 = Unused, M3 = Unused,
        W0 = 255, W1 = 0, W2 = 0, W3 = 0,
    };

    /// <summary>
    /// Assemble a blend, dropping zero-weight layers and collapsing duplicate materials so a
    /// pixel never spends two of its four slots on the same texture.
    /// </summary>
    private static Blend Mix(byte m0, byte w0, byte m1, byte w1, byte m2, byte w2, byte m3, byte w3)
    {
        Span<byte> materials = [m0, m1, m2, m3];
        Span<int> weights = [w0, w1, w2, w3];

        for (int i = 0; i < 4; i++)
        {
            if (weights[i] <= 0 || materials[i] == Unused) { weights[i] = 0; continue; }
            for (int j = 0; j < i; j++)
                if (weights[j] > 0 && materials[j] == materials[i])
                {
                    weights[j] += weights[i];
                    weights[i] = 0;
                    break;
                }
        }

        var blend = new Blend { M0 = Unused, M1 = Unused, M2 = Unused, M3 = Unused };
        Span<byte> outM = [Unused, Unused, Unused, Unused];
        Span<byte> outW = [0, 0, 0, 0];

        int slot = 0;
        for (int i = 0; i < 4 && slot < 4; i++)
        {
            if (weights[i] <= 0) continue;
            outM[slot] = materials[i];
            outW[slot] = (byte)Math.Clamp(weights[i], 1, 255);
            slot++;
        }

        blend.M0 = outM[0]; blend.M1 = outM[1]; blend.M2 = outM[2]; blend.M3 = outM[3];
        blend.W0 = outW[0]; blend.W1 = outW[1]; blend.W2 = outW[2]; blend.W3 = outW[3];
        return blend;
    }
}
