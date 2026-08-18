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

    // The only two materials in the file that are about a *landform* rather than a climate, and
    // both were unreachable: the constant list used to run 8 -> 11 and nothing else named them, so
    // UsedMaterials never saw them and TerrainMaskWriter emitted both masks fully black. Vanilla
    // paints them on 1.4% and 2.0% of its map respectively. coastline_cliff_grey carries
    // tile_factor = 500, which is what makes it read as rock on a face steep enough to stretch the
    // UVs rather than as a smear.
    private const byte CoastlineCliffDesert = 9;
    private const byte CoastlineCliffGrey = 10;

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
    private static (byte First, byte Second, double ConfA, double ConfB)
        LowlandPair(in Family family, double nA, double nB)
    {
        var set = family.Lowlands;
        int count = set.Length;
        int a = (int)(Math.Clamp(nA, 0, 0.999999) * count);
        int b = (a + 1 + (int)(Math.Clamp(nB, 0, 0.999999) * (count - 1))) % count;
        return (set[a], set[b], BucketConfidence(nA, count), BucketConfidence(nB, count - 1));
    }

    private static byte Accent(Climate climate, double n)
    {
        var set = Accents[(int)climate];
        return set[(int)(Math.Clamp(n, 0, 0.999999) * set.Length)];
    }

    private static double AccentConfidence(Climate climate, double n)
        => BucketConfidence(n, Accents[(int)climate].Length);

    private static double HillRockConfidence(Climate climate, double n) => climate switch
    {
        Climate.Mediterranean => 1.0,
        Climate.Desert or Climate.Drylands => CutConfidence(n, 0.5),
        _ => Math.Min(CutConfidence(n, 0.4), CutConfidence(n, 0.75)),
    };

    /// <summary>
    /// How sure a bucket pick is, 1 in the middle of a bucket falling to 0 at either edge.
    ///
    /// This exists because of how CK3's terrain shader combines neighbouring texels. It samples
    /// detail_index with a point sampler at the four corners of a 2x2, and accumulates each
    /// neighbour's mask into the centre's slots *only where the material index matches* — a
    /// neighbour carrying a material the centre does not have contributes nothing at all, and the
    /// centre's accumulated mask comes up short. Anything under 0.1 is then zeroed outright by a
    /// smoothstep, so the layer vanishes and the texel visibly pops away from its neighbours.
    ///
    /// Measured against a detail_index Clausewitz recompiled from our own masks, its accumulated
    /// mask totals 0.988 of a possible 1.0 with not one texel under 0.75; ours managed 0.903 with
    /// 11.8% of texels under 0.75. The difference is not that its material sets never change — it
    /// is that when they change, the departing material was carrying nothing. A bucket pick does
    /// the opposite: it swaps a material at full weight the instant the selector crosses an edge,
    /// so two adjacent pixels either side of that edge disagree about a layer that matters.
    ///
    /// Fading the weight to nothing across the edge restores the property. The freed weight is not
    /// lost: <see cref="Normalized"/> rescales what remains back to 255, so the pixel simply reads
    /// as more of the ground it is already standing on for the few pixels the selector spends near
    /// a boundary.
    /// </summary>
    private static double BucketConfidence(double n, int count)
    {
        if (count <= 1) return 1.0;

        double p = Math.Clamp(n, 0, 0.999999) * count;
        double f = p - (int)p;
        double edge = Math.Min(f, 1.0 - f) / BucketFadeBand;
        return Math.Clamp(edge, 0, 1);
    }

    /// <summary>
    /// A triangular bump: 1 at <paramref name="centre"/>, falling to 0 at <paramref name="halfWidth"/>
    /// either side. Used to carve regimes out of a selector without thresholding it — a regime that
    /// switches on at a threshold puts its whole palette in at once along a contour, which is the
    /// hard material swap the rest of this file exists to avoid.
    /// </summary>
    private static double Bump(double n, double centre, double halfWidth)
        => Math.Clamp(1.0 - Math.Abs(n - centre) / halfWidth, 0, 1);

    /// <summary>Smoothly 0 below <paramref name="cut"/> and 1 above, over a band either side.</summary>
    private static double Ramp(double n, double cut, double band)
    {
        double t = Math.Clamp((n - cut) / (2 * band) + 0.5, 0, 1);
        return t * t * (3.0 - 2.0 * t);
    }

    /// <summary>The same, for a pick made on a bare threshold rather than an even set of buckets.</summary>
    private static double CutConfidence(double n, double cut)
        => Math.Clamp(Math.Abs(n - cut) / CutFadeBand, 0, 1);

    /// <summary>
    /// Fade width for a bare threshold, on the selector's own 0-1 scale — unlike
    /// <see cref="BucketFadeBand"/>, which is a fraction of a bucket. The selectors move about 0.02
    /// per pixel, so 0.15 spreads the crossfade over roughly seven texels.
    /// </summary>
    private const double CutFadeBand = 0.15;

    /// <summary>
    /// How far either side of a bucket edge the fade runs, as a fraction of the bucket's own width.
    ///
    /// 0.5 makes it a triangle across the whole bucket: zero at both edges, full only at the centre.
    /// That is deliberate, and the previous 0.06 was the bug it replaces. A bucket is 1/count of the
    /// selector's range — a quarter of it for four lowland variants — so 6% of a bucket is 0.015 of
    /// the selector, and the selectors move about 0.02 per pixel. The fade therefore resolved in
    /// under a single texel: a hard edge with a fade painted on it, which is exactly how it looked.
    /// Simulated side by side against a Clausewitz recompile of the same map, ours drew the same
    /// materials in the same regions with stair-stepped boundaries where its were smooth curves.
    ///
    /// At 0.5 the pick never changes abruptly anywhere: its weight rises and falls continuously as
    /// the selector crosses, so the boundary between two variants is a gradient several texels wide
    /// rather than a step. The material is rarely at full strength, but <see cref="Normalized"/>
    /// rescales what remains, so the pixel simply leans harder on the layers it already has.
    /// </summary>
    private const double BucketFadeBand = 0.5;

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
                    var (lowA, lowB, confA, confB) = LowlandPair(family, nA, nB);

                    return Mix(
                        sand, 160,
                        lowA, (byte)((40 + nA * 30) * confA),
                        lowB, (byte)((30 + nB * 25) * confB),
                        Accent(climate, nC), (byte)((15 + nC * 20) * AccentConfidence(climate, nC))
                    );
                }

            case TerrainClass.Floodplains:
                {
                    var (lowA, _, confA, _) = LowlandPair(family, nA, nB);
                    return Mix(
                        Floodplains, (byte)(110 + nA * 40),
                        WetlandsMud, (byte)(50 + nB * 30),
                        lowA, (byte)((40 + (1.0 - nA) * 30) * confA),
                        PlainsDryMud, (byte)(20 + nC * 20)
                    );
                }

            case TerrainClass.Wetlands:
                {
                    var (lowA, _, confA, _) = LowlandPair(family, nA, nB);
                    return Mix(
                        Wetlands, (byte)(120 + nA * 40),
                        WetlandsMud, (byte)(70 + nB * 30),
                        lowA, (byte)((40 + (1.0 - nA) * 20) * confA),
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
                    var (lowA, lowB, confA, confB) = LowlandPair(family, nA, nB);

                    return Mix(
                        fields, (byte)(100 + nA * 50),
                        lowA, (byte)((60 + (1.0 - nA) * 40) * confA),
                        lowB, (byte)((50 + nB * 30) * confB),
                        Accent(climate, nC), (byte)((20 + nC * 20) * AccentConfidence(climate, nC))
                    );
                }

            case TerrainClass.Oasis:
                {
                    // The oasis material is the green itself, so it leads, and the desert it sits
                    // in shows through the other three slots — an oasis reads as an oasis only
                    // against sand. Wet mud at the waterline, dune and cracked pan around it.
                    ref readonly var around = ref Families[(int)Climate.Desert];
                    var (lowA, _, confA, _) = LowlandPair(around, nA, nB);

                    return Mix(
                        Oasis, (byte)(130 + nA * 50),
                        lowA, (byte)((55 + (1.0 - nA) * 35) * confA),
                        WetlandsMud, (byte)(35 + nB * 30),
                        nC < 0.5 ? DesertWavy : DesertCracked, (byte)((25 + nC * 25) * CutConfidence(nC, 0.5))
                    );
                }

            case TerrainClass.Forest:
                {
                    // Needleleaf in the cold, broadleaf in the warm, and the litter under both.
                    bool mixedCanopy = climate is not (Climate.Northern or Climate.Tropical);
                    byte canopy = climate is Climate.Northern ? ForestPine
                                : climate is Climate.Tropical ? ForestJungle
                                : nC < 0.45 ? ForestPine : ForestLeaf;

                    // The heaviest threshold pick on the map, and the one the residual
                    // stair-stepping concentrated on: pine and leaf between them accounted for 17%
                    // of every place a neighbour lost 64 of 255 or more to an unmatched material.
                    // It carries too much weight to fade away, so both candidates are drawn and
                    // crossfaded — an even split at the threshold, one of them alone away from it.
                    byte canopyAlt = mixedCanopy ? (nC < 0.45 ? ForestLeaf : ForestPine) : canopy;
                    double canopyConf = mixedCanopy ? CutConfidence(nC, 0.45) : 1.0;
                    double canopyWeight = 80 + (1.0 - nA) * 40;

                    var (lowA, _, confA, _) = LowlandPair(family, nA, nB);

                    return Mix(
                        ForestFloor, (byte)(100 + nA * 40),
                        canopy, (byte)(canopyWeight * (0.5 + 0.5 * canopyConf)),
                        lowA, (byte)((40 + nB * 30) * confA),
                        family.Hills, (byte)(20 + nC * 20),
                        canopyAlt, (byte)(canopyWeight * 0.5 * (1.0 - canopyConf))
                    );
                }

            case TerrainClass.Jungle:
                {
                    var (lowA, lowB, confA, confB) = LowlandPair(Families[(int)Climate.Tropical], nA, nB);
                    return Mix(
                        ForestJungle, (byte)(100 + nA * 40),
                        lowA, (byte)((70 + (1.0 - nA) * 40) * confA),
                        lowB, (byte)((50 + nB * 30) * confB),
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
                    var (lowA, lowB, confA, confB) = LowlandPair(north, nA, nB);

                    return Mix(
                        lowA, (byte)((90 + nA * 40) * confA),
                        ForestPine, (byte)(75 + (1.0 - nA) * 40),
                        lowB, (byte)((50 + nB * 35) * confB),
                        nC < 0.35 ? Snow : nC < 0.7 ? NorthernPlains : north.Hills,
                            (byte)(25 + nC * 30)
                    );
                }

            case TerrainClass.Arctic:
                {
                    ref readonly var north = ref Families[(int)Climate.Northern];
                    var (lowA, lowB, confA, confB) = LowlandPair(north, nA, nB);

                    // Heavy snow over northern ground, exposing what the wind scours bare.
                    return Mix(
                        Snow, (byte)(120 + (1.0 - nC) * 80),
                        lowA, (byte)((60 + nA * 40) * confA),
                        lowB, (byte)((35 + nB * 30) * confB),
                        nC < 0.5 ? north.Hills : north.Mountain, (byte)((20 + nC * 30) * CutConfidence(nC, 0.5))
                    );
                }

            case TerrainClass.Steppe:
                {
                    ref readonly var steppe = ref Families[(int)Climate.Steppe];
                    byte steppeBase = steppe.Lowlands[0];
                    byte low1 = steppe.Lowlands[1];
                    byte low2 = steppe.Lowlands[2];
                    byte low3 = steppe.Lowlands[3];

                    // Steppe read as uniform because it was drawn the same way everywhere: one
                    // rotating lowland pair under steppe_grass. Vanilla varies it by *region* —
                    // mostly a shifting mix of the base and its three lowland variants with the
                    // base generally leading, but with occasional large patches where one variant
                    // takes half the ground, and rarer stretches where steppe_bushes takes about
                    // three quarters of it. All four gen_ variants are named here rather than
                    // rotated through LowlandPair, so the mix varies in *proportion* everywhere
                    // instead of swapping which two materials are on show.
                    //
                    // The regimes ride the coarsest selector, so they arrive as patches a hundred
                    // pixels across, and they are bumps rather than thresholds: a regime that
                    // switched on at a cut would put its whole palette in along one contour.
                    double bushy = Bump(nA, 0.09, 0.15);
                    double patchy = Bump(nA, 0.42, 0.20);
                    double side = Ramp(nB, 0.5, 0.15);
                    double quiet = 1.0 - 0.75 * bushy;

                    // The patch is a *boost* to one variant rather than a new layer, so it costs no
                    // slot and cannot evict anything on its way in or out.
                    double toLow1 = 150 * patchy * side;
                    double toLow3 = 150 * patchy * (1.0 - side);

                    byte tail = nC < 0.45 ? SteppeGrass : nC < 0.8 ? SteppeRocks : steppe.Hills;
                    double tailConf = Math.Min(CutConfidence(nC, 0.45), CutConfidence(nC, 0.8));

                    return Mix(
                        steppeBase, (byte)Math.Min(255, (105 + nA * 30) * quiet),
                        low1, (byte)Math.Min(255, (65 + nB * 30 + toLow1) * quiet),
                        low2, (byte)Math.Min(255, (55 + nC * 30) * quiet),
                        low3, (byte)Math.Min(255, (50 + (1.0 - nB) * 30 + toLow3) * quiet),
                        SteppeBushes, (byte)Math.Min(255, 235 * bushy),
                        tail, (byte)Math.Min(255, (40 + nC * 25) * quiet * tailConf)
                    );
                }

            case TerrainClass.Drylands:
                {
                    ref readonly var dry = ref Families[(int)Climate.Drylands];
                    var (lowA, lowB, confA, confB) = LowlandPair(dry, nA, nB);

                    // medi_dry_mud and plains_01_dry_mud are the sun-baked flats between the scrub,
                    // and together are 0.70% of vanilla's painted weight — more than the entire
                    // farmland family. Both were previously unreachable: medi_dry_mud sat in the
                    // drylands accent list, which this case never consults, and plains_01_dry_mud
                    // only appeared under Floodplains, which nothing assigns.
                    return Mix(
                        lowA, (byte)((90 + nA * 40) * confA),
                        lowB, (byte)((75 + (1.0 - nA) * 40) * confB),
                        nB < 0.4 ? DrylandsGrassy : nB < 0.75 ? Drylands01 : DrylandsCracked,
                            (byte)(50 + nB * 30),
                        nC < 0.3 ? DesertCracked : nC < 0.5 ? MediDryMud
                            : nC < 0.68 ? PlainsDryMud : dry.Hills,
                        (byte)((25 + nC * 25) * Math.Min(CutConfidence(nC, 0.3),
                            Math.Min(CutConfidence(nC, 0.5), CutConfidence(nC, 0.68))))
                    );
                }

            case TerrainClass.Desert:
                {
                    ref readonly var desert = ref Families[(int)Climate.Desert];
                    var (lowA, lowB, confA, confB) = LowlandPair(desert, nA, nB);

                    // Dunes are the thing a desert is missing without them. desert_wavy is one of
                    // vanilla's twenty heaviest materials and we shipped none of it.
                    byte dune = nB < 0.55 ? DesertWavy : DesertWavyLarger;
                    byte duneAlt = nB < 0.55 ? DesertWavyLarger : DesertWavy;
                    double duneConf = CutConfidence(nB, 0.55);
                    double duneWeight = 55 + nB * 40;

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
                        lowA, (byte)((100 + nA * 40) * confA),
                        lowB, (byte)((75 + (1.0 - nA) * 40) * confB),
                        dune, (byte)(duneWeight * (0.5 + 0.5 * duneConf)),
                        // 45..90 rather than 25..50. Measured against the map editor's own
                        // recompile, its desert holds a fourth layer at 12-13% of the pixel where
                        // ours managed 5.3% — present on 92% of desert texels but too faint to
                        // read, and faint layers are also the ones the shader's 0.1 cutoff drops.
                        // Vanilla's fourth slot averages 6% across all land, so this is a desert
                        // correction rather than a global one.
                        grain, (byte)((45 + nC * 45) * Math.Min(
                            Math.Min(CutConfidence(nC, 0.25), CutConfidence(nC, 0.42)),
                            Math.Min(Math.Min(CutConfidence(nC, 0.55), CutConfidence(nC, 0.68)),
                                     CutConfidence(nC, 0.85)))),
                        duneAlt, (byte)(duneWeight * 0.5 * (1.0 - duneConf))
                    );
                }

            case TerrainClass.Plains:
                {
                    var (lowA, lowB, confA, confB) = LowlandPair(family, nA, nB);
                    return Mix(
                        lowA, (byte)((80 + nA * 40) * confA),
                        lowB, (byte)((70 + (1.0 - nA) * 40) * confB),
                        Accent(climate, nB), (byte)((50 + nB * 30) * AccentConfidence(climate, nB)),
                        nC < 0.6 ? family.Hills : Accent(climate, 1.0 - nC), (byte)((30 + nC * 20) * CutConfidence(nC, 0.6))
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
                    var (lowA, lowB, confA, confB) = LowlandPair(family, nA, nB);
                    return Mix(
                        lowA, (byte)((90 + nA * 50) * confA),
                        lowB, (byte)((70 + (1.0 - nA) * 40) * confB),
                        family.Hills, (byte)(50 + nB * 30),
                        Accent(climate, nC), (byte)((25 + nC * 20) * AccentConfidence(climate, nC))
                    );
                }
        }
    }

    /// <summary>
    /// A sea cliff: bare rock, the climate's own hill stone under it, and the family's mountain
    /// face to keep it from being one flat texture at every scale.
    ///
    /// Merged over whatever biome the pixel resolved to rather than replacing it, so the foot of
    /// the cliff keeps the sand or scrub it stands in and only the face itself goes to rock. That
    /// is also why this is not a <see cref="TerrainClass"/>: a cliff is a slope, and slope is
    /// measurable at heightmap resolution but averaged away at province resolution, which is where
    /// the terrain classes are decided.
    /// </summary>
    public static Blend CliffFace(Climate climate, double nA, double nC)
    {
        ref readonly var family = ref Families[(int)climate];

        // Sandstone in the dry world, grey stratified rock everywhere else — the same split
        // vanilla draws between its two cliff textures.
        byte face = climate is Climate.Desert or Climate.Drylands
            ? CoastlineCliffDesert
            : CoastlineCliffGrey;

        return Mix(
            face, (byte)(170 + nA * 50),
            HillRock(climate, nC), (byte)((45 + nC * 30) * HillRockConfidence(climate, nC)),
            family.Mountain, (byte)(35 + (1.0 - nA) * 30),
            family.Transition, (byte)(20 + nC * 20)
        );
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
        var (lowA, _, confA, _) = LowlandPair(family, nA, nB);

        return Mix(
            family.Hills, (byte)(100 + nA * 30),
            lowA, (byte)((70 - toMountain / 3 + (1.0 - nA) * 20) * confA),
            family.Transition, toMountain,
            HillRock(climate, nC), (byte)((30 + nB * 25) * HillRockConfidence(climate, nC))
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

    /// <summary>
    /// The four weights rescaled to sum to exactly 255, which is the only total CK3 accepts.
    ///
    /// Vanilla's detail_intensity.tga sums to 255 on every one of its 42 million pixels; the
    /// terrain shader takes the four weights as a partition of the pixel and does not normalise
    /// them itself. Everything above authors weights as *relative* strengths — this much hill rock
    /// against this much lowland — and nothing was making them add up, so a cliff pixel shipped a
    /// total near 470 and a wet-mud pixel one near 175. Both are wrong in the same way: the blend
    /// is driven at whatever gain the numbers happened to land on.
    ///
    /// Largest-remainder rather than four independent roundings, which miss 255 as often as not.
    ///
    /// <paramref name="contrast"/> above 1 sharpens the blend before it is scaled, raising each
    /// weight to that power. Measured on land, vanilla runs a clear primary at 133 of 255 falling
    /// away to 68, 39 and 15, while ours ran four near-equal textures at 100, 80, 56 and 19 — a
    /// mush, in which no material is really the ground and every pixel looks like every other
    /// pixel's average. Raising the exponent restores the falloff. It is scale-invariant, so it can
    /// be applied to the raw authored weights whatever they happen to total, but it must be applied
    /// exactly once: normalising twice with it would square it.
    /// </summary>
    public static Blend Normalized(Blend b, double contrast = 1.0)
    {
        Span<byte> materials = [b.M0, b.M1, b.M2, b.M3];
        Span<int> weights = [b.W0, b.W1, b.W2, b.W3];

        for (int i = 0; i < 4; i++)
            if (materials[i] == Unused) weights[i] = 0;

        Span<double> curved = stackalloc double[4];
        double total = 0;

        for (int i = 0; i < 4; i++)
        {
            curved[i] = weights[i] <= 0 ? 0
                      : contrast == 1.0 ? weights[i]
                      : Math.Pow(weights[i], contrast);
            total += curved[i];
        }

        if (total <= 0) return b;

        Span<int> scaled = stackalloc int[4];
        Span<double> remainder = stackalloc double[4];
        int assigned = 0;

        for (int i = 0; i < 4; i++)
        {
            double exact = curved[i] * 255.0 / total;
            scaled[i] = (int)exact;
            remainder[i] = exact - scaled[i];
            assigned += scaled[i];
        }

        // Truncation always undershoots, so hand the shortfall to the slots that lost the most.
        for (int give = 255 - assigned; give > 0; give--)
        {
            int best = -1;
            for (int i = 0; i < 4; i++)
                if (weights[i] > 0 && (best < 0 || remainder[i] > remainder[best])) best = i;

            if (best < 0) break;
            scaled[best]++;
            remainder[best] = double.NegativeInfinity;
        }

        // A slot the blend chose must not be rounded out of existence — it was picked over the
        // materials that lost, and dropping it here would put bare ground where a texture belongs.
        // Its one unit comes off the dominant slot, which keeps the total on 255.
        for (int i = 0; i < 4; i++)
        {
            if (weights[i] <= 0 || scaled[i] > 0) continue;

            int fattest = 0;
            for (int j = 1; j < 4; j++) if (scaled[j] > scaled[fattest]) fattest = j;
            if (scaled[fattest] <= 1) continue;

            scaled[fattest]--;
            scaled[i] = 1;
        }

        b.W0 = (byte)scaled[0];
        b.W1 = (byte)scaled[1];
        b.W2 = (byte)scaled[2];
        b.W3 = (byte)scaled[3];
        return b;
    }

    /// <summary>
    /// Drop blend slots carrying less than <paramref name="floor"/> of 255, so a pixel spends its
    /// four slots on materials it can actually be seen to be made of.
    ///
    /// Expects an already-normalised blend, since the floor is stated on the 255 scale; renormalise
    /// afterwards to give the dropped weight back to the layers that survived. Never empties a
    /// pixel: the dominant slot is kept whatever it weighs, because something has to be drawn.
    /// </summary>
    public static Blend Pruned(Blend b, int floor)
    {
        Span<byte> materials = [b.M0, b.M1, b.M2, b.M3];
        Span<int> weights = [b.W0, b.W1, b.W2, b.W3];

        int dominant = 0;
        for (int i = 1; i < 4; i++)
            if (materials[i] != Unused && weights[i] > weights[dominant]) dominant = i;

        for (int i = 0; i < 4; i++)
        {
            if (i == dominant || materials[i] == Unused || weights[i] >= floor) continue;
            materials[i] = Unused;
            weights[i] = 0;
        }

        b.M0 = materials[0]; b.M1 = materials[1]; b.M2 = materials[2]; b.M3 = materials[3];
        b.W0 = (byte)weights[0]; b.W1 = (byte)weights[1];
        b.W2 = (byte)weights[2]; b.W3 = (byte)weights[3];
        return b;
    }

    /// <summary>
    /// Scatter the four weights per pixel without touching which materials they are.
    ///
    /// The two halves of a blend are not alike to the shader. Weights from neighbouring texels are
    /// simply summed, so they may disagree as violently as we like at no cost; the material *set*
    /// has to agree or the neighbour's contribution is discarded. Dithering the set — which is what
    /// perturbing the selectors does — breaks the accumulation and isolates every texel. Dithering
    /// the weights breaks up flat ground and costs nothing.
    ///
    /// That distinction is the whole difference between our detail_index and one Clausewitz
    /// recompiles from the same masks. Simulating the shader over both, ours resolves into broad
    /// fields of constant blend divided by stair-stepped boundaries — a hard edge between two flat
    /// colours at texel resolution, which is precisely the artefact. Its blend is stippled
    /// everywhere and never settles long enough for an edge to form, at a *higher* per-pixel
    /// discontinuity than ours (0.211 against 0.129) that reads as ground texture rather than as a
    /// seam.
    ///
    /// Applied after pruning on purpose: pruning decides set membership, so it has to run on the
    /// smooth weights where neighbouring pixels agree about which layers are worth a slot.
    /// </summary>
    public static Blend WeightJitter(Blend b, double j0, double j1, double j2, double j3, double amount)
    {
        Span<double> j = [j0, j1, j2, j3];
        Span<byte> materials = [b.M0, b.M1, b.M2, b.M3];
        Span<int> weights = [b.W0, b.W1, b.W2, b.W3];

        for (int i = 0; i < 4; i++)
        {
            if (materials[i] == Unused || weights[i] <= 0) continue;
            double scaled = weights[i] * (1.0 + (j[i] - 0.5) * amount);

            // Never to zero: a layer that survived the floor is part of this pixel's set, and
            // dropping it here would be a set change dressed up as a weight change.
            weights[i] = Math.Max(1, (int)Math.Round(scaled));
        }

        b.W0 = (byte)Math.Min(255, weights[0]); b.W1 = (byte)Math.Min(255, weights[1]);
        b.W2 = (byte)Math.Min(255, weights[2]); b.W3 = (byte)Math.Min(255, weights[3]);
        return b;
    }

    private static Blend Single(byte material) => new()
    {
        M0 = material,
        M1 = Unused,
        M2 = Unused,
        M3 = Unused,
        W0 = 255,
        W1 = 0,
        W2 = 0,
        W3 = 0,
    };

    /// <summary>
    /// Assemble a blend, dropping zero-weight layers and collapsing duplicate materials so a
    /// pixel never spends two of its four slots on the same texture.
    /// </summary>
    /// <summary>
    /// Six candidates in, the four heaviest out. The extra pair exists so a threshold pick can
    /// hand in *both* of its candidates crossfaded rather than swapping one for the other at full
    /// weight — see the canopy and dune picks. Away from a threshold the alternate weighs nothing
    /// and drops out, so the ordinary case is unchanged.
    /// </summary>
    private static Blend Mix(byte m0, byte w0, byte m1, byte w1, byte m2, byte w2, byte m3, byte w3,
        byte m4 = Unused, byte w4 = 0, byte m5 = Unused, byte w5 = 0)
    {
        Span<byte> materials = [m0, m1, m2, m3, m4, m5];
        Span<int> weights = [w0, w1, w2, w3, w4, w5];

        for (int i = 0; i < 6; i++)
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

        for (int slot = 0; slot < 4; slot++)
        {
            int best = -1, bestWeight = 0;
            for (int i = 0; i < 6; i++)
                if (weights[i] > bestWeight) { bestWeight = weights[i]; best = i; }

            if (best < 0) break;
            outM[slot] = materials[best];
            outW[slot] = (byte)Math.Clamp(weights[best], 1, 255);
            weights[best] = 0;
        }

        blend.M0 = outM[0]; blend.M1 = outM[1]; blend.M2 = outM[2]; blend.M3 = outM[3];
        blend.W0 = outW[0]; blend.W1 = outW[1]; blend.W2 = outW[2]; blend.W3 = outW[3];
        return blend;
    }
}