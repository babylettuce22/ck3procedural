using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// Turns a <see cref="TerrainClass"/> into the up-to-four weighted materials CK3 blends per pixel.
///
/// Material values index <c>gfx/map/terrain/materials.settings</c> in file order — vanilla
/// annotates that list "reliant on material index, so don't change the order of these". The
/// indices below were read out of that file, not guessed.
///
/// The important structural find is the <c>gen_*</c> family (indices 55-104, masks in
/// <c>masks_gen/</c>). It is a ready-made **climate x landform matrix**: seven climate families,
/// each with a base, three or four lowland variants, hills, mountain and mountain_transition.
/// Vanilla's own detail_index leans on it heavily — gen_desert_base, gen_central_lowlands and
/// gen_northern_* are all in its top ten materials. Those are exactly the two axes a generator
/// already has, which makes the family a far better fit than ck2rpg's hand-picked older materials.
///
/// Every pixel gets a dominant material plus two or three siblings from the same family at lower
/// weight. That is what produces the continuous variation vanilla has and our old painting did
/// not: measured, vanilla blends 2-4 layers on 98.85% of pixels across ~101 materials, while ours
/// used exactly one layer and seven materials.
/// </summary>
public static class TerrainPalette
{
    /// <summary>An unused layer: material 255, weight 0.</summary>
    public const byte Unused = 255;

    // --- Classic materials, used where a feature is sharper than a climate family ---
    private const byte Beach = 6;
    private const byte DesertCracked = 13;
    private const byte Farmland = 22;
    private const byte Floodplains = 23;
    private const byte ForestJungle = 24;
    private const byte ForestPine = 26;
    private const byte ForestFloor = 27;
    private const byte MudWet = 38;          // seafloor
    private const byte Oasis = 40;
    private const byte Snow = 46;
    private const byte SteppeBushes = 47;
    private const byte SteppeGrass = 48;
    private const byte SteppeRocks = 49;
    private const byte Wetlands = 50;
    private const byte WetlandsMud = 51;
    private const byte CentralMountain = 52;

    /// <summary>
    /// One climate family of the gen_* matrix. <see cref="Lowlands"/> holds the base plus every
    /// lowland variant, which are interchangeable and get mixed by noise.
    /// </summary>
    private readonly record struct Family(byte[] Lowlands, byte Hills, byte Mountain, byte Transition);

    // Index ranges read directly from materials.settings.
    private static readonly Family Tropical = new([55, 56, 57, 58], 59, 60, 61);
    private static readonly Family Central = new([62, 63, 64, 65], 66, 67, 68);
    private static readonly Family Steppe = new([69, 70, 71, 72], 73, 74, 75);
    private static readonly Family Desert = new([76, 77, 78, 79, 80], 81, 82, 83);
    private static readonly Family Drylands = new([84, 85, 86, 87], 88, 89, 90);
    private static readonly Family Northern = new([91, 92, 93, 94], 95, 96, 97);
    private static readonly Family Mediterranean = new([98, 99, 100, 101], 102, 103, 104);

    /// <summary>Four material slots and their blend weights, as CK3 stores them.</summary>
    public struct Blend
    {
        public byte M0, M1, M2, M3;
        public byte W0, W1, W2, W3;
    }

    /// <summary>
    /// Build the blend for one pixel.
    /// </summary>
    /// <param name="terrain">What the ground is.</param>
    /// <param name="relief">0 at sea level, 1 at the mountain line — drives hills/mountain mixing.</param>
    /// <param name="nA">Noise selecting which lowland variant dominates, 0..1.</param>
    /// <param name="nB">Noise selecting the second variant, 0..1.</param>
    /// <param name="nC">Noise setting how strongly the accents show through, 0..1.</param>
    public static Blend For(TerrainClass terrain, double relief, double nA, double nB, double nC)
    {
        switch (terrain)
        {
            // Open water. Deep ocean is a single flat material — it is never visible, and an ocean
            // of beach sand reads as a kilometres-wide shoreline. The shallows *are* visible
            // through the water though, so sand and silt are mixed back in as the floor rises
            // towards the coast. relief is negative below sea level, so -1 is the deep floor and
            // 0 the waterline.
            case TerrainClass.Sea:
            {
                double shallow = Math.Clamp(1.0 + relief * 3.0, 0, 1);
                if (shallow <= 0.02) return Single(MudWet);

                return Mix(MudWet, (byte)(255 - shallow * 110),
                           Beach, (byte)(shallow * 90),
                           WetlandsMud, (byte)(shallow * 45 * nC),
                           Unused, 0);
            }

            // Shore. Sand over the local lowland so the beach fades inland rather than ending on
            // a hard line.
            case TerrainClass.Beach:
                return Mix(Beach, 180, Pick(Mediterranean.Lowlands, nA), 50,
                           Pick(Central.Lowlands, nB), 25, Unused, 0);

            case TerrainClass.Floodplains:
                return Mix(Floodplains, 165, WetlandsMud, 45,
                           Pick(Central.Lowlands, nA), 45, Unused, 0);

            case TerrainClass.Wetlands:
                return Mix(Wetlands, 150, WetlandsMud, 60,
                           Pick(Central.Lowlands, nA), 45, Unused, 0);

            case TerrainClass.Farmlands:
                return Mix(Farmland, 155, Pick(Central.Lowlands, nA), 60,
                           Pick(Mediterranean.Lowlands, nB), 40, Unused, 0);

            case TerrainClass.Forest:
                return Mix(ForestFloor, 120, ForestPine, 80,
                           Pick(Central.Lowlands, nA), 55, Central.Hills, 20);

            case TerrainClass.Jungle:
                return Mix(ForestJungle, 125, Pick(Tropical.Lowlands, nA), 75,
                           Pick(Tropical.Lowlands, nB), 40, Tropical.Hills, 20);

            case TerrainClass.Taiga:
                return Mix(Pick(Northern.Lowlands, nA), 110, ForestPine, 85,
                           Pick(Northern.Lowlands, nB), 45, Northern.Hills, 20);

            case TerrainClass.Arctic:
                // Snow over northern rock, thinning where the noise says the wind scours it.
                return Mix(Snow, (byte)(170 + nC * 60), Pick(Northern.Lowlands, nA), 45,
                           Northern.Hills, 30, Unused, 0);

            case TerrainClass.Steppe:
                return Mix(SteppeGrass, 105, Pick(Steppe.Lowlands, nA), 80,
                           SteppeBushes, (byte)(25 + nC * 45), SteppeRocks, 25);

            case TerrainClass.Drylands:
                return Mix(Pick(Drylands.Lowlands, nA), 115, Pick(Drylands.Lowlands, nB), 70,
                           DesertCracked, (byte)(20 + nC * 40), Drylands.Hills, 25);

            case TerrainClass.Desert:
                // The one place an extra accent is worth it: flat sand reads as a dead surface.
                return Mix(Pick(Desert.Lowlands, nA), 120, Pick(Desert.Lowlands, nB), 75,
                           DesertCracked, (byte)(15 + nC * 35),
                           nC > 0.93 ? Oasis : Desert.Hills, 20);

            case TerrainClass.Plains:
                return Mix(Pick(Central.Lowlands, nA), 115, Pick(Central.Lowlands, nB), 75,
                           Pick(Mediterranean.Lowlands, nC), 45, Central.Hills, 20);

            case TerrainClass.Hills:
                return HillBlend(Central, relief, nA, nB, nC);

            case TerrainClass.Mountains:
                return MountainBlend(Central, relief, nA, nC);

            case TerrainClass.DesertMountains:
                return MountainBlend(Desert, relief, nA, nC);

            default:
                return Mix(Pick(Central.Lowlands, nA), 130, Pick(Central.Lowlands, nB), 70,
                           Central.Hills, 30, Unused, 0);
        }
    }

    /// <summary>
    /// Hills sit between the lowland and mountain materials, so both show through — that gradient
    /// is what stops a hill range from ending on a visible contour line.
    /// </summary>
    private static Blend HillBlend(Family family, double relief, double nA, double nB, double nC)
    {
        byte toMountain = (byte)(30 + Math.Clamp(relief, 0, 1) * 70);
        return Mix(family.Hills, 120, Pick(family.Lowlands, nA), (byte)(80 - toMountain / 3),
                   family.Transition, toMountain, Pick(family.Lowlands, nB), (byte)(20 + nC * 25));
    }

    /// <summary>
    /// Mountains carry rock plus the family's transition material, and gain snow with altitude so
    /// summits whiten instead of every peak being the same grey.
    /// </summary>
    private static Blend MountainBlend(Family family, double relief, double nA, double nC)
    {
        double above = Math.Clamp(relief - 1.0, 0, 1);
        byte snow = (byte)Math.Clamp(above * 240 + nC * 50 - 25, 0, 255);

        return Mix(family.Mountain, 140, CentralMountain, 60,
                   family.Transition, (byte)(45 + nA * 35), Snow, snow);
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

        // Selection sort for the top four — cheaper than sorting all eight.
        var result = new Blend { M0 = Unused, M1 = Unused, M2 = Unused, M3 = Unused };
        Span<byte> outM = stackalloc byte[4];
        Span<byte> outW = stackalloc byte[4];
        outM[0] = outM[1] = outM[2] = outM[3] = Unused;

        for (int slot = 0; slot < 4; slot++)
        {
            int best = -1;
            double bestWeight = 0;
            for (int i = 0; i < 8; i++)
                if (weights[i] > bestWeight) { bestWeight = weights[i]; best = i; }

            if (best < 0) break;
            outM[slot] = materials[best];
            outW[slot] = (byte)Math.Clamp((int)Math.Round(bestWeight), 1, 255);
            weights[best] = 0;
        }

        result.M0 = outM[0]; result.M1 = outM[1]; result.M2 = outM[2]; result.M3 = outM[3];
        result.W0 = outW[0] == Unused ? (byte)0 : outW[0];
        result.W1 = outM[1] == Unused ? (byte)0 : outW[1];
        result.W2 = outM[2] == Unused ? (byte)0 : outW[2];
        result.W3 = outM[3] == Unused ? (byte)0 : outW[3];
        if (outM[0] == Unused) result.W0 = 0;
        return result;
    }

    /// <summary>Choose a lowland variant from a 0..1 noise value.</summary>
    private static byte Pick(byte[] variants, double n)
        => variants[Math.Clamp((int)(n * variants.Length), 0, variants.Length - 1)];

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
