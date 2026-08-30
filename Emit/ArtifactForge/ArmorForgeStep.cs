namespace Ck3MapGen.Emit;

using Ck3MapGen.Io;
using System.IO;

/// <summary>
/// Dresses an equipped armour artifact on the portrait, in the wearer's culture and the artifact's
/// own material.
///
/// **Armour is the mirror image of a weapon.** A weapon costs nothing per artifact — the engine
/// substitutes its entity through <c>game_entity_override = weapon</c> — but needs an animation hook
/// to be drawn at all. Armour has no such override: <c>game_entity_override</c> has exactly one
/// value in the whole game and it is <c>weapon</c>. So armour is built the way AGOT builds it, as a
/// portrait modifier plus an accessory per look, which needs no animation hook (a <c>usage = game</c>
/// group runs on every portrait) but does cost emitted text per look.
///
/// **Nothing here is modelled.** A worn garment already declares a <c>pattern_mask</c> and a
/// <c>variation</c>, exactly as a forged weapon does, so recolouring one needs no geometry, no new
/// UV set and not even a mask of our own — vanilla's mask already marks the garment's regions and we
/// supply a different palette against it. What we emit per look is an entity, an accessory, a
/// variation and a 16x4 palette, all of it text plus a few hundred bytes.
///
/// **Culture picks the silhouette, artifact type picks the material.** That split is forced. Vanilla
/// ships war garments organised by culture and era with no material in any name, and collapses all
/// six armour types onto two visuals of its own — so type cannot drive the shape. It can drive the
/// palette, and at portrait scale a bright articulated steel reads as plate against a dark mail in a
/// way the silhouette barely does.
///
/// **The look is frozen against the creator, not the wearer.** A portrait modifier is evaluated on
/// whoever is being drawn, so gating on their culture would repaint a stolen cuirass in the thief's
/// colours. Reading <c>creator</c> instead keeps a piece looking like itself for good — and it is
/// how vanilla's own artifact triggers do it, <c>creator ?= { ... }</c> with the owner as fallback.
/// This is a real difference from weapons, whose visual is frozen anyway because it resolves once
/// inside <c>create_artifact</c>.
/// </summary>
public static class ArmorForgeStep
{
    private const string EntityDir = "gfx/models/artifacts/gen_armor";

    /// <summary>The one piece of armour art vanilla ships, which every armour artifact shows today.</summary>
    private const string StockArmorIcon = "artifact_armor.dds";

    /// <summary>Vanilla's armour visual entries, and the files they live in.</summary>
    private static readonly (string File, string Key)[] ArmorVisuals =
    [
        ("00_personal_misc.txt", "armor"),
        ("04_ep2_artifacts.txt", "plate"),
    ];

    /// <summary>
    /// Our own template inside vanilla's <c>clothes</c> accessory gene, and its index.
    ///
    /// A portrait modifier's <c>accessory</c> must be a member of the <c>template</c> it cites — the
    /// engine enforces it even though ck3-tiger does not — so our accessories need a template of
    /// their own, and vanilla's cannot be borrowed.
    ///
    /// Spliced into a copy of vanilla's gene file — see <see cref="GeneFile"/> — because a separate
    /// file declaring <c>clothes</c> a second time replaces the gene instead of extending it. AGOT
    /// adding a whole new gene in its own file suggested otherwise, but that merges one level up, at
    /// <c>accessory_genes</c>; the templates inside a single gene do not.
    ///
    /// Indices must be unique within the gene; vanilla's run 0 to 202, so this sits clear of them
    /// and of room for a DLC to grow into.
    /// </summary>
    /// <summary>
    /// **A GENE TEMPLATE'S WEIGHT SUM MUST FIT IN A BYTE.** One template held every accessory until
    /// a 33-culture world produced 792 entries per sex and the engine refused the lot:
    ///
    /// <code>
    /// [E][genedatabase.cpp:932]: The following error(s) occurred when initializing accessory gene [clothes]
    /// [E][genedatabase.cpp:935]: weight sum exceeds 255. The system can't guarantee that all entries can be picked.
    /// </code>
    ///
    /// The failure is near-silent in the worst way: the gene still loads, so nothing crashes and
    /// ck3-tiger is happy, but accessories past the cut are unreachable. Every character then falls
    /// back to their culture's ordinary war dress — which is the same art the recolour is painted
    /// onto — so it reads as "the recolour did nothing" rather than as an error. Partial overflow
    /// reads as "only some cultures change colour".
    ///
    /// Vanilla's largest clothes list is 202 entries summing to 202, measured across all 398 lists
    /// in the file we splice into, so 200 is both under the cap and inside vanilla's own practice.
    ///
    /// **This is invisible on a small world.** A 7-culture test world yields 168 per sex and passes;
    /// the cap is only crossed above ten cultures. Test the gene against a world with many cultures
    /// or not at all.
    /// </summary>
    private const int GeneTemplateChunk = 200;

    /// <summary>
    /// Indices must be unique within the gene. Vanilla's run 0 to 202, and
    /// <see cref="CustomArmorStep"/> already holds 901 for its own clothes template, so this starts
    /// clear of both with room for as many chunks as any world will ever need.
    /// </summary>
    private const int GeneTemplateIndexBase = 910;

    /// <summary>The name of one chunk's template.</summary>
    private static string GeneTemplateName(int chunk) => $"gen_armor_clothes_{chunk}";

    /// <summary>
    /// Which template each look belongs to, chunked per sex.
    ///
    /// Each sex is counted separately because a template carries a <c>male</c> and a <c>female</c>
    /// list and the cap applies to each list, not to the template. Ordered by name so the mapping is
    /// stable between the two places that need it — the template writer and the portrait modifier
    /// writer — which must agree exactly or every modifier cites a template its accessory is not in.
    /// </summary>
    private static Dictionary<string, string> TemplateByLook(List<Look> looks)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var bySex in looks.GroupBy(l => l.Female))
        {
            int i = 0;

            foreach (var look in bySex.OrderBy(l => l.Name, StringComparer.Ordinal))
                map[look.Name] = GeneTemplateName(i++ / GeneTemplateChunk);
        }

        return map;
    }

    /// <summary>
    /// Vanilla's clothes gene file, which we ship a spliced COPY of.
    ///
    /// Declaring `clothes` a second time in a file of our own does not merge, it REPLACES:
    /// tried that, and every vanilla template vanished - `most_clothes not found in category
    /// clothes` - which in game strips clothing from every character alive. Copying the file
    /// and inserting one template is the only route, and reading the INSTALLED copy at
    /// generation time means it tracks whatever patch is on the machine rather than freezing
    /// a snapshot of some earlier one.
    /// </summary>
    private const string GeneFile = "05_genes_special_accessories_clothes.txt";

    /// <summary>
    /// Vanilla's own variation shader, minus the coat of arms.
    ///
    /// 42 war garments ship with <c>portrait_attachment_with_coa_and_variations</c>, which is what
    /// paints the wearer's dynasty on the tabard — the probe wore one and showed it. On an artifact
    /// that is wrong twice over: the arms belong to whoever holds it rather than to the piece, so a
    /// stolen cuirass would re-badge itself, and an heirloom showing its thief's house is the
    /// opposite of what an artifact is. <c>portrait_attachment_pattern</c> is the same shader
    /// without that stage, and is what 574 vanilla assets already use.
    ///
    /// **This only applies to garments whose entity declares <c>meshsettings</c>.** Many do not —
    /// none of the six a default world currently selects — and take their shader from the
    /// <c>.mesh</c> material instead, which cannot be overridden from here. That is not a gap worth
    /// closing: a garment with no meshsettings is already using a pattern shader
    /// (<c>portrait_attachment_pattern_alpha_to_coverage</c> on the byzantine one, checked in the
    /// mesh itself), because the coat-of-arms variants are exactly the ones vanilla configures
    /// explicitly. The swap is here for when one of those is picked.
    /// </summary>
    private const string Shader = "portrait_attachment_pattern";
    private const string ShaderFile = "gfx/FX/jomini/portrait.shader";

    /// <summary>
    /// Which garment families suit which clothing gfx.
    ///
    /// Generated cultures carry a vanilla <c>clothing_gfx</c>, which is the one honest bridge
    /// between an invented people and vanilla's art: it is already what decides their ordinary
    /// clothes, so matching war dress to it keeps a realm looking like itself. Families are listed
    /// best-first and matched as name stems, so <c>ep2_byzantine</c> and <c>ep3_byzantine_era1</c>
    /// both answer to <c>byzantine</c>.
    ///
    /// Anything unlisted falls back to <see cref="FallbackFamilies"/> rather than failing — vanilla
    /// has 43 clothing gfx families and 32 garment stems, so some will never match.
    /// </summary>
    private static readonly (string Gfx, string[] Families)[] GarmentsByGfx =
    [
        ("northern",      ["norse", "fp1", "ep2_western_era1"]),
        ("fp1_norse",     ["fp1", "norse"]),
        ("east_slavic",   ["ccp2_west_slavic", "ep2_steppe"]),
        ("west_slavic",   ["ccp2_west_slavic", "ep2_steppe"]),
        ("byzantine",     ["ep3_byzantine_era2", "ep2_byzantine", "byzantine"]),
        ("mongol",        ["mpo_mongol", "ep2_steppe", "steppe"]),
        ("turkic",        ["ep2_steppe", "steppe", "mpo_mongol"]),
        ("mena",          ["ep2_mena", "mena", "crusades_mena"]),
        ("dde_abbasid",   ["dde_abbasid", "ep2_mena", "mena"]),
        ("iranian",       ["fp3_iranian", "ep2_mena", "mena"]),
        ("indian",        ["ep2_indian", "indian"]),
        ("chinese",       ["tgp_chinese"]),
        ("japanese",      ["tgp_japanese"]),
        ("emishi",        ["ccp_emishi", "tgp_japanese"]),
        ("african",       ["ccp1_african"]),
        ("afr_berber",    ["ccp1_african"]),
        ("iberian_christian", ["fp2_iberian_christian", "crusades_western"]),
        ("iberian_muslim",    ["fp2_iberian_muslim", "ep2_mena"]),
        ("english",       ["ccp5_english", "ep2_western_era4"]),
        ("french",        ["ccp5_french", "ep2_western_era2"]),
        ("norman",        ["ccp5_french", "crusades_western"]),
        ("swabian",       ["ccp5_german", "dde_hre"]),
        ("dde_hre",       ["dde_hre", "ccp5_german"]),
        ("western",       ["ep2_western_era2", "western", "crusades_western"]),
    ];

    private static readonly string[] FallbackFamilies = ["ep2_western_era2", "western"];

    /// <summary>
    /// The substances a channel can be made of — each one a VANILLA pattern swatch, not a colour
    /// chip of our own.
    ///
    /// **This is the fix for "recoloured armour looks odd rather than legendary".** Read the shader
    /// (<c>jomini/gfx/FX/jomini/portrait_accessory_variation.fxh</c>) rather than the script and the
    /// reason is plain. Inside a masked region it does three different things:
    ///
    /// <code>
    /// Diffuse *= PatternDiffuse;             // tints - the garment's painted detail SURVIVES
    /// Diffuse.rgb *= PatternProperties.rrr;  // the swatch's ambient occlusion carves shading in
    /// Properties = PatternProperties;        // roughness/metalness/AO REPLACED wholesale
    /// </code>
    ///
    /// and the normal becomes <c>lerp(garmentNormal, swatchNormal, NormalUVChannel)</c>, where
    /// <c>NormalUVChannel</c> is driven to 1.0 exactly where the mask applies. So the swatch's normal
    /// map REPLACES the garment's relief in the region we tint.
    ///
    /// The nine <c>gen_*</c> swatches this used to name are flat by construction. Measured against
    /// the shipped files, as the standard deviation of the DXT5nm normal's G and A channels:
    ///
    /// | swatch | relief | AO |
    /// |---|---|---|
    /// | every <c>gen_*</c> | **0.00** | 1.00 +- 0.00 |
    /// | <c>chainmail_plain_01</c> | 69.5 | 0.49 +- 0.22 |
    /// | <c>lamellar_leather_01</c> | 62.4 | 0.70 +- 0.17 |
    /// | <c>metal_scales_01</c> | 46.8 | 0.67 +- 0.19 |
    ///
    /// So the old recolour did not restate the armour in a new material, it SANDED IT SMOOTH: mail
    /// rings, rivets and quilting were erased precisely where the tint landed, and AO went to 1.0 so
    /// the contact shading went with them. The result was a flat coloured panel abutting untouched
    /// regions that still had full relief — which is what read as uncanny. Colour was never the
    /// problem.
    ///
    /// **The colour comes from the palette; the SUBSTANCE comes from the swatch's normal and AO.**
    /// Every swatch's colormask is a solid 1.0 across channel 0 (measured on all of them, ours
    /// included), so it is an "applies here" flag rather than a tonal map — nothing about the
    /// material arrives through colour. Only the lamellar swatches fire further channels, and those
    /// are the only place <see cref="WritePalette"/>'s per-column tones have anything to act on.
    ///
    /// Scale is the one cost this brings. A flat colour tiles to itself at any scale, so the old
    /// layout hardcoded 1.0; a relief swatch at 1.0 gives one mail ring the size of a torso. Every
    /// figure below is vanilla's own modal pairing, counted across all its variation files.
    /// </summary>
    private enum Surface { Plate, Mail, Scale, LamellarMetal, LamellarLeather, Leather, Cloth, Linen }

    /// <summary>Where the swatch lives, its basename, its tiling scale, and how many tones it has.</summary>
    /// <param name="Tones">
    /// How many of the swatch's four colormask channels actually fire, measured as the fraction of
    /// texels above 0.03. Channel 0 is solid on every swatch; the extra channels exist only on the
    /// lamellar pair. It sets how many palette columns of the block are meaningful — see
    /// <see cref="WritePalette"/>.
    /// </param>
    /// <param name="Scale">
    /// Tiling scale at <see cref="ReferenceDensity"/>. Multiplied by the garment channel's measured
    /// density before it is written — see <see cref="ChannelDensity"/>.
    /// </param>
    /// <param name="Ao">
    /// The swatch's mean ambient occlusion, measured from its properties map.
    ///
    /// The shader does <c>Diffuse.rgb *= PatternProperties.rrr</c>, so this halves or does not halve
    /// the diffuse BEFORE the palette is judged. It is not a small effect: mail measures 0.487
    /// against plate's 1.000, so identical palette entries render 2.05x apart. That is why an
    /// illustrious mail piece still read as dark — the rarity ramp tops out at 1.24 gain, nowhere
    /// near enough to climb out of a 0.49 multiplier. <see cref="AoLift"/> cancels it.
    /// </param>
    private sealed record Swatch(string Dir, string File, double Scale, int Tones, double Ao);

    /// <summary>
    /// The vanilla swatch behind each substance.
    ///
    /// <c>metal_plain_01</c> is the obvious pick for plate and is a TRAP: it ships a normal and a
    /// properties map but NO colormask, so it cannot be named in a <c>pattern_textures</c> block at
    /// all. Vanilla never registers it as one. <c>statue/gold_plain_01</c> is the usable equivalent —
    /// metalness 1.00, roughness 0.40, near-flat relief, which is exactly what a plate cuirass wants —
    /// and it is not inherently gold: a pattern swatch has no diffuse, so its colour is whatever the
    /// palette says. The name describes vanilla's usage, not ours.
    /// </summary>
    private static readonly Dictionary<Surface, Swatch> Swatches = new()
    {
        // metal 1.00, roughness 0.40, relief 1.8 - smooth, as plate should be.
        [Surface.Plate]           = new("statue",  "gold_plain_01",             0.25, 1, 1.000),
        // metal 0.75, roughness 0.62, relief 69.5 - the strongest relief in the library.
        [Surface.Mail]            = new("all",     "chainmail_plain_01",        0.25, 1, 0.487),
        // NOT metal_scales_01, which is the obvious pick and reads as flat dull metal on a portrait.
        // Three measurements against it, all in this swatch's favour: metalness 0.58 against 0.90 —
        // and a near-fully metallic surface has almost no DIFFUSE response, so under a soft portrait
        // key it goes dark and loses its own relief; feature size 73 texels against 51, so the
        // scales are coarse enough to still read at portrait resolution rather than aliasing into
        // noise; and it tiles cleanly (seam error 0.88x the interior gradient, against 1.38x).
        // Relief is higher too, 58.9 against 46.8.
        [Surface.Scale]           = new("all",     "all_pangolier_scales_01",   0.25, 1, 0.498),
        // metal 0.87, roughness 0.49, relief 45.7, and three tones rather than one.
        [Surface.LamellarMetal]   = new("all",     "lamellar_metal_01",         0.13, 3, 0.817),
        // metal 0.00, roughness 0.53, relief 62.4, three tones.
        [Surface.LamellarLeather] = new("all",     "lamellar_leather_01",       0.13, 3, 0.697),
        // metal 0.00, roughness 0.46, relief 28.8.
        [Surface.Leather]         = new("all",     "leather_plain_01",          0.25, 1, 0.750),
        // metal 0.00, roughness 0.87, relief 40.0 - the matte end of the range.
        [Surface.Cloth]           = new("western", "western_wool_plain_01",     0.25, 1, 0.750),
        // metal 0.00, roughness ~0.8, relief 33.3 - a finer weave than wool.
        [Surface.Linen]           = new("western", "western_linen_plain_01",    0.25, 1, 0.803),
    };

    /// <summary>Where vanilla's pattern swatches live.</summary>
    private const string VanillaSwatchDir = "gfx/portraits/accessory_variations/textures/patterns";

    /// <summary>
    /// The four rarities, in the order the ramps below are indexed by.
    /// </summary>
    private static readonly string[] Rarities = ["common", "masterwork", "famed", "illustrious"];

    /// <summary>
    /// How rarity enriches the palette.
    ///
    /// **This ramp used to be dead code.** Two of these tables and two <c>Enrich</c> overloads
    /// existed; neither overload was ever called, and <see cref="WritePalette"/> destructured the
    /// table into a <c>(gain, sat)</c> pair it then never used, writing the base channel colours
    /// raw. So rarity reached the palette not at all, and the only thing it moved was the surface
    /// ladder — which, over flat swatches with no relief for roughness to act on, moved nothing
    /// visible either.
    ///
    /// That is the real explanation for the observation recorded here previously: "six armour types
    /// at one rarity looked clearly different, while one type across four rarities looked
    /// identical". It was read at the time as portrait lighting washing out metalness. It was not —
    /// rarity was simply never applied. Both halves are now live, and roughness has real relief to
    /// work against.
    ///
    /// Gain and saturation are the same two knobs the weapon icons ramp across rarity, where they
    /// demonstrably read. Saturation carries most of it: a common piece is dull rather than merely
    /// darker, which is a difference in kind rather than in exposure.
    /// </summary>
    private static readonly (double Gain, double Sat)[] RarityLook =
    [
        (0.76, 0.62),
        (0.92, 0.86),
        (1.08, 1.06),
        (1.24, 1.20),
    ];

    /// <summary>
    /// How far apart a swatch's tones are driven, per rarity.
    ///
    /// Only the lamellar swatches have more than one tone, so this is a narrow effect by design —
    /// on everything else the block is one colour repeated and the spread does nothing. Where it
    /// does apply it reads as "better finished": wider spread means deeper recesses and brighter
    /// crowns, which is what separates a polished plate from a dull one.
    /// </summary>
    private static readonly double[] RaritySpread = [0.08, 0.14, 0.20, 0.26];

    /// <summary>One channel colour, enriched for its rarity.</summary>
    private static (byte R, byte G, byte B) Enrich((byte R, byte G, byte B) c, int rarity)
    {
        var (gain, sat) = RarityLook[Math.Clamp(rarity, 0, RarityLook.Length - 1)];
        double lum = 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;

        static byte Clamp(double v) => (byte)Math.Clamp(v, 0, 255);

        return (Clamp((lum + (c.R - lum) * sat) * gain),
                Clamp((lum + (c.G - lum) * sat) * gain),
                Clamp((lum + (c.B - lum) * sat) * gain));
    }

    /// <summary>
    /// The pattern layout a substance is tiled at.
    ///
    /// Keyed on the SCALE rather than on the garment, so two garment channels that measure to the
    /// same density share one layout instead of emitting a duplicate. Quantised to four decimals,
    /// which is also the precision the value is written at — so the name and the contents cannot
    /// disagree.
    /// </summary>
    private static string LayoutName(Surface s, double scale) =>
        $"gen_armor_layout_{s.ToString().ToLowerInvariant()}_{(int)Math.Round(scale * 10000)}";

    /// <summary>The scale as it is written, and as the layout name is keyed on.</summary>
    private static string ScaleText(double scale) =>
        scale.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Per-channel mask coverage per mask texture, as a fraction of texels.</summary>
    private static readonly Dictionary<string, double[]> CoverageCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// DdsReader hands back BGRA; the shader's <c>Mask[0..3]</c> is RGBA, so channel <c>i</c> lives
    /// at these byte offsets. Getting this wrong swaps red and blue and silently ranks the wrong
    /// regions.
    /// </summary>
    private static readonly int[] MaskByte = [2, 1, 0, 3];

    /// <summary>
    /// How much of the surface each channel is actually SEEN on, which is not how much of the mask
    /// it occupies.
    ///
    /// **The shader is a lerp chain, so a later channel paints over an earlier one.**
    /// <c>ApplyVariationPatterns</c> walks the channels in order and each one does
    /// <c>lerp(sofar, mine, Mask[i])</c>, so channel <c>i</c> only survives where every higher
    /// channel is absent. Its visible share is therefore
    /// <c>Mask[i] * PROD over j &gt; i of (1 - Mask[j])</c>, not <c>Mask[i]</c>.
    ///
    /// The difference is not academic — on these garments the red channel is largely a base coat
    /// that the others cover:
    ///
    /// | garment | raw r | visible r | lead by raw | lead by visible |
    /// |---|---|---|---|---|
    /// | `ep2_steppe` | 0.68 | **0.17** | r b a g | **b a r g** |
    /// | `ep2_western_era2` | 0.76 | **0.20** | r a g b | **a r g b** |
    /// | `ep3_byzantine_era2` | 0.54 | **0.16** | r a b g | **a r b g** |
    ///
    /// Ranking on the raw figure aimed each type's defining substance at a region that is mostly
    /// painted over, so the type read weakly however good the material was.
    /// </summary>
    private static double[] Coverage(ArmorGarment garment)
    {
        double[] even = [0.25, 0.25, 0.25, 0.25];

        if (MaskDisk(garment) is not { } path) return even;
        if (CoverageCache.TryGetValue(path, out double[]? cached)) return cached;

        double[] cov = even;

        if (DdsReader.Load(path) is { } img && img.Bgra.Length >= 4)
        {
            var sum = new double[4];
            long texels = img.Bgra.Length / 4;

            for (int p = 0; p + 3 < img.Bgra.Length; p += 4)
            {
                Visible(img.Bgra, p, out double[] w);
                for (int c = 0; c < 4; c++) sum[c] += w[c];
            }

            cov = [.. sum.Select(s => s / texels)];
        }

        CoverageCache[path] = cov;
        return cov;
    }

    /// <summary>
    /// The share of a texel each channel actually shows, after the higher channels have painted
    /// over it.
    /// </summary>
    private static void Visible(byte[] bgra, int at, out double[] weight)
    {
        var m = new double[4];
        for (int c = 0; c < 4; c++) m[c] = bgra[at + MaskByte[c]] / 255.0;

        weight = new double[4];

        for (int c = 0; c < 4; c++)
        {
            double survives = m[c];
            for (int higher = c + 1; higher < 4; higher++) survives *= 1.0 - m[higher];
            weight[c] = survives;
        }
    }

    /// <summary>
    /// The garment's mask channels, ranked by how much of the garment each one actually paints.
    ///
    /// **Why a type cannot simply use channels 0-3 in order.** Vanilla's mask channels carry no
    /// fixed meaning, which was known — but they are also wildly UNEVEN, and unevenly in a different
    /// way per garment, which was not. Measured as the fraction of texels above 0.15:
    ///
    /// | garment | R | G | B | A |
    /// |---|---|---|---|---|
    /// | `ep2_steppe` | **0.679** | 0.021 | 0.282 | 0.214 |
    /// | `ep2_western_era2` | **0.762** | 0.144 | 0.083 | 0.374 |
    /// | `ep3_byzantine_era2` (female) | 0.227 | 0.115 | 0.094 | **0.295** |
    ///
    /// Pinning a type's defining material to channels 0 and 1 therefore misfires twice. On the
    /// steppe garment channel 1 paints 2% of the surface, so half the type's identity is invisible.
    /// On the female byzantine garment the LARGEST region is channel 3 — which every type treats as
    /// trim, and every type's trim is a brown within a few values of every other's — so that garment
    /// barely changes colour between armour types at all.
    ///
    /// What a channel IS cannot be known from here. How much it COVERS can, and prominence is the
    /// property that actually decides whether a garment reads as plate or as mail. So the type lists
    /// its materials most-important-first and they are dealt onto the garment's channels in
    /// coverage order.
    /// </summary>
    private static int[] ChannelOrder(ArmorGarment garment)
    {
        double[] cov = Coverage(garment);

        // ThenBy keeps the tie-break stable, so an unreadable mask's even split degrades to exactly
        // the fixed 0,1,2,3 order this replaced rather than to an arbitrary one.
        return [.. Enumerable.Range(0, 4).OrderByDescending(c => cov[c]).ThenBy(c => c)];
    }

    /// <summary>
    /// Deals a type's materials onto one garment's channels, most prominent material to most
    /// covered channel.
    /// </summary>
    private static (Surface S, byte R, byte G, byte B)[] AimAtGarment(
        (Surface S, byte R, byte G, byte B)[] byImportance, ArmorGarment garment)
    {
        int[] order = ChannelOrder(garment);
        var placed = new (Surface S, byte R, byte G, byte B)[4];
        int metal = MetalChannel(garment);

        // WITHOUT A GENERATED MASK there is no region known to be metal, so prominence is the only
        // thing to go on: deal the type's materials onto the channels by how much they cover.
        if (metal < 0)
        {
            for (int rank = 0; rank < 4; rank++)
                placed[order[rank]] = byImportance[Math.Min(rank, byImportance.Length - 1)];

            return placed;
        }

        // WITH ONE, the garment's actual plates are known, and that beats prominence: the type's
        // leading metal goes there and nothing else does. This is the whole point of generating a
        // mask - a plate artifact should put its steel on the cuirass, not on the surcoat.
        var remaining = new List<(Surface S, byte R, byte G, byte B)>(byImportance);
        int lead = remaining.FindIndex(c => IsMetal(c.S));

        // A type with no metal at all - none today, but the table is editable - simply keeps its
        // most important material there, which is still the right region to lead with.
        placed[metal] = remaining[lead >= 0 ? lead : 0];
        remaining.RemoveAt(lead >= 0 ? lead : 0);

        // The rest fill the cloth channels by prominence, skipping the one metal now owns.
        int next = 0;

        foreach (int channel in order)
        {
            if (channel == metal) continue;
            placed[channel] = remaining[Math.Min(next++, remaining.Count - 1)];
        }

        return placed;
    }

    /// <summary>Set once per run so the mask reader can resolve a game-relative path.</summary>
    private static string GameDir = "";

    /// <summary>
    /// The mask we generated for each garment, by accessory name. Empty when
    /// <see cref="ArtifactForgeFlags.GeneratedArmorMasks"/> is off, or for garments with too little
    /// metal to be worth redirecting.
    /// </summary>
    private static readonly Dictionary<string, GeneratedMask> Masks = new(StringComparer.Ordinal);

    private static GeneratedMask? MaskOf(ArmorGarment g) =>
        Masks.TryGetValue(g.Accessory, out var m) ? m : null;

    /// <summary>Generates this garment's mask once, if the flag allows and the garment earns one.</summary>
    private static void EnsureMask(string modDir, string gameDir, ArmorGarment garment)
    {
        if (!ArtifactForgeFlags.GeneratedArmorMasks) return;
        if (Masks.ContainsKey(garment.Accessory)) return;

        if (ArmorMask.Build(modDir, gameDir, garment) is { } made)
            Masks[garment.Accessory] = made;
    }

    /// <summary>
    /// The mask file to READ when measuring coverage and density.
    ///
    /// Every measuring pass must read the mask that will actually ship, not vanilla's — a generated
    /// mask has different channels in different places, so measuring the wrong one would rank and
    /// scale against regions that are not there.
    /// </summary>
    private static string? MaskDisk(ArmorGarment g) =>
        MaskOf(g)?.DiskPath
        ?? (g.PatternMask is { } rel ? Path.Combine(GameDir, rel.Replace('/', Path.DirectorySeparatorChar)) : null);

    /// <summary>The mask path an entity names.</summary>
    private static string? MaskRef(ArmorGarment g) => MaskOf(g)?.ModPath ?? g.PatternMask;

    /// <summary>Which channel holds the garment's metal, or -1 when nothing does.</summary>
    private static int MetalChannel(ArmorGarment g) => MaskOf(g)?.MetalChannel ?? -1;

    /// <summary>The substances that read as metal, for aiming them at a metal region.</summary>
    private static bool IsMetal(Surface s) =>
        s is Surface.Plate or Surface.Mail or Surface.Scale or Surface.LamellarMetal;

    /// <summary>
    /// The UV1 texel density vanilla's own modal scales are correct at.
    ///
    /// Measured as the area-weighted median over the MASKED regions of all 230 vanilla war garments
    /// that ship both a mesh and a pattern mask. The distribution is p5 0.00513, p50 0.00825,
    /// p95 0.01402 — a 2.7x spread, which is why vanilla tunes its own layouts per case rather than
    /// reusing one scale (chainmail appears at both 0.25 and 0.2, leather from 0.08 to 0.4).
    /// </summary>
    private const double ReferenceDensity = 0.00825;

    /// <summary>
    /// How far a garment's scale may be driven from vanilla's base figure.
    ///
    /// The measurement is trusted, but a channel covering a sliver of a garment can yield a density
    /// from very few triangles, and an unclamped outlier would tile a swatch either to a smear or to
    /// a single feature across the chest. The band is a little wider than vanilla's own 2.7x spread
    /// so genuine low-density regions are still corrected.
    /// </summary>
    private const double MinScaleFactor = 0.30;
    private const double MaxScaleFactor = 2.60;

    /// <summary>Per-channel UV1 density per garment, keyed by mesh path — meshes are megabytes.</summary>
    private static readonly Dictionary<string, double[]> DensityCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// UV1 texel density — UV1 units per world unit — for each of a garment's four mask channels.
    ///
    /// **This is what decides how big a mail ring or a scale comes out, and the UV1 SPAN this
    /// replaced was a bad proxy for it.** A pattern samples <c>UV1 / scale</c>, so a feature's size
    /// on the body is <c>swatchFeature * scale / density</c>: to keep it constant, scale must track
    /// density. Span only measures the bounding box, which says nothing about how much body area is
    /// packed inside it. Measured against span on the garments a default world picks:
    ///
    /// | garment | span said | density says |
    /// |---|---|---|
    /// | `ep2_steppe` | 1.00 | 0.59 |
    /// | `ep2_western_era2` | **3.05** | **0.73** |
    /// | `ep3_byzantine_era2` | 1.08 | **1.52** |
    ///
    /// So span over-corrected the western garment by more than 4x — its patterns came out coarse —
    /// and missed the byzantine one entirely in the other direction.
    ///
    /// **Per channel, not per garment**, because the two differ enormously. The byzantine mesh
    /// measures 0.00188 overall but ~0.0125 across its MASKED regions: most of that garment is
    /// unmasked low-density geometry, and averaging it in would have made every pattern on it 6.6x
    /// too large. Within the steppe garment, channel a is half the density of channel r, so one
    /// shared scale gives those two regions visibly different feature sizes. A layout is already
    /// declared per channel, so nothing is lost by measuring per channel.
    ///
    /// Each triangle is assigned to whichever mask channel is strongest at its UV0 centroid — the
    /// mask is sampled in UV0, per the shader — and each channel's density is the area-weighted
    /// median over its own triangles. Median rather than mean because UV packing leaves a tail of
    /// degenerate slivers that would drag a mean anywhere.
    ///
    /// Returns <see cref="ReferenceDensity"/> in every slot when the mesh or mask cannot be read,
    /// which yields vanilla's base scale unchanged — the behaviour before any of this existed.
    /// </summary>
    private static double[] ChannelDensity(ArmorGarment garment)
    {
        double[] fallback = [ReferenceDensity, ReferenceDensity, ReferenceDensity, ReferenceDensity];

        if (garment.MeshPath is not { } path) return fallback;
        if (MaskDisk(garment) is not { } maskPath) return fallback;

        // Keyed on both, because density is measured PER MASK CHANNEL - the same mesh against a
        // generated mask yields different figures, and reusing a cached one would scale against
        // regions that moved.
        string key = $"{path}|{maskPath}";
        if (DensityCache.TryGetValue(key, out double[]? cached)) return cached;

        if (DdsReader.Load(maskPath) is not { } mk) return fallback;

        // One (density, area) sample per triangle, bucketed by channel, plus a whole-garment bucket
        // used when a channel is too thin to measure on its own.
        var perChannel = new List<(double D, double W)>[4];
        for (int c = 0; c < 4; c++) perChannel[c] = [];
        var overall = new List<(double D, double W)>();

        try
        {
            Walk(PdxMesh.Read(path));
        }
        catch (Exception e) when (e is IOException or InvalidDataException or NotSupportedException)
        {
            return fallback;
        }

        double whole = Median(overall) is { } w and > 0 ? w : ReferenceDensity;
        var result = new double[4];

        for (int c = 0; c < 4; c++)
        {
            // A channel needs a real sample of the surface before its own figure is trusted; below
            // that it takes the garment's masked median, which is still far better than the
            // reference because it is at least this garment's own packing.
            result[c] = perChannel[c].Count >= 32 && Median(perChannel[c]) is { } d and > 0 ? d : whole;
        }

        DensityCache[key] = result;
        return result;

        void Walk(PdxNode node)
        {
            float[] p = node.Floats("p"), u0 = node.Floats("u0"), u1 = node.Floats("u1");
            int[] tri = node.Ints("tri");

            if (p.Length > 0 && u0.Length > 0 && u1.Length > 0 && tri.Length > 0)
                Accumulate(p, u0, u1, tri);

            foreach (var kid in node.Children) Walk(kid);
        }

        void Accumulate(float[] p, float[] u0, float[] u1, int[] tri)
        {
            int verts = Math.Min(p.Length / 3, Math.Min(u0.Length / 2, u1.Length / 2));

            for (int t = 0; t + 2 < tri.Length; t += 3)
            {
                int a = tri[t], b = tri[t + 1], c = tri[t + 2];
                if (a >= verts || b >= verts || c >= verts) continue;

                // World area, from the cross product of two edges.
                double ax = p[b * 3] - p[a * 3], ay = p[b * 3 + 1] - p[a * 3 + 1], az = p[b * 3 + 2] - p[a * 3 + 2];
                double bx = p[c * 3] - p[a * 3], by = p[c * 3 + 1] - p[a * 3 + 1], bz = p[c * 3 + 2] - p[a * 3 + 2];

                double cx = ay * bz - az * by, cy = az * bx - ax * bz, cz = ax * by - ay * bx;
                double worldArea = 0.5 * Math.Sqrt(cx * cx + cy * cy + cz * cz);
                if (worldArea <= 1e-9) continue;

                // UV1 area, the 2D cross product.
                double uvArea = 0.5 * Math.Abs(
                    (u1[b * 2] - u1[a * 2]) * (u1[c * 2 + 1] - u1[a * 2 + 1]) -
                    (u1[c * 2] - u1[a * 2]) * (u1[b * 2 + 1] - u1[a * 2 + 1]));

                if (uvArea <= 1e-12) continue;

                double density = Math.Sqrt(uvArea / worldArea);

                // Which channel paints this triangle, sampled at its UV0 centroid.
                double su = (u0[a * 2] + u0[b * 2] + u0[c * 2]) / 3.0;
                double sv = (u0[a * 2 + 1] + u0[b * 2 + 1] + u0[c * 2 + 1]) / 3.0;

                int px = (int)Math.Clamp(Wrap(su) * mk.Width, 0, mk.Width - 1);
                int py = (int)Math.Clamp(Wrap(sv) * mk.Height, 0, mk.Height - 1);
                int at = (py * mk.Width + px) * 4;

                if (at + 3 >= mk.Bgra.Length) continue;

                // Which channel is SEEN here, after the higher ones have painted over it - the same
                // lerp-chain weighting Coverage uses. Taking the raw maximum instead put nearly
                // every triangle on the red channel, because red is a base coat under the others,
                // and every channel then fell back to one shared garment-wide density.
                Visible(mk.Bgra, at, out double[] w);

                int best = 0;
                for (int ch = 1; ch < 4; ch++) if (w[ch] > w[best]) best = ch;

                if (w[best] <= 0.15) continue;   // unmasked: keeps its vanilla art, so it sets no scale

                perChannel[best].Add((density, worldArea));
                overall.Add((density, worldArea));
            }
        }

        static double Wrap(double v) { v %= 1.0; return v < 0 ? v + 1.0 : v; }

        static double? Median(List<(double D, double W)> xs)
        {
            if (xs.Count == 0) return null;

            xs.Sort((x, y) => x.D.CompareTo(y.D));
            double total = xs.Sum(x => x.W), run = 0;

            foreach (var (d, w) in xs)
            {
                run += w;
                if (run >= total / 2) return d;
            }

            return xs[^1].D;
        }
    }

    /// <summary>
    /// The ambient occlusion a palette entry is written against, so every substance lands at the
    /// brightness its colour asks for.
    ///
    /// Roughly where the fabrics and leathers already sit (0.750 to 0.803), so the substances that
    /// looked right are left alone and only the dark ones are lifted.
    /// </summary>
    private const double ReferenceAo = 0.78;

    /// <summary>
    /// How much a substance's palette is brightened to cancel its own ambient occlusion.
    ///
    /// **Lift only, never darken.** Normalising in both directions would pull plate down by a
    /// quarter, and plate is one of the substances that already reads correctly — there is no reason
    /// to spend its brightness making a table symmetric. Capped at 1.7 so a hypothetical very dark
    /// swatch cannot drive a palette entry off the top of the range on its own.
    /// </summary>
    private static double AoLift(Surface s) =>
        Math.Clamp(ReferenceAo / Math.Max(Swatches[s].Ao, 0.05), 1.0, 1.7);

    /// <summary>The tiling scale a substance gets on one channel of one garment.</summary>
    private static double ScaleFor(Surface surface, ArmorGarment garment, int channel)
    {
        double density = ChannelDensity(garment)[Math.Clamp(channel, 0, 3)];
        double factor = Math.Clamp(density / ReferenceDensity, MinScaleFactor, MaxScaleFactor);

        return Swatches[surface].Scale * factor;
    }

    /// <summary>
    /// What each armour type is made of: four channels, each a substance and a colour.
    ///
    /// **THE ORDER OF THESE FOUR IS LOAD-BEARING.** They are listed most important first — the
    /// substance the type is named for, then its supporting materials, then trim — and
    /// <see cref="AimAtGarment"/> deals them onto whichever of the garment's mask channels actually
    /// covers the most surface. So a plate harness is plate over mail voiders on leather straps, and
    /// the plate lands on the biggest region of whatever garment the culture supplies rather than on
    /// channel 0 regardless. Reordering a row silently changes which material dominates.
    ///
    /// What a channel IS still cannot be targeted — its meaning differs per garment, one separating
    /// mail from surcoat and another plate from strapping. What can be controlled is the SET and now
    /// the PROMINENCE, so whichever region each channel turns out to be, the garment reads as one
    /// coherent object led by the right substance.
    ///
    /// **A channel's substance no longer changes with rarity.** It used to: a "metal family" channel
    /// walked rough iron to polished steel as the piece got rarer. Over real swatches that would
    /// mean an illustrious mail shirt rendering as plate — the material would change, not its
    /// finish. Mail stays mail at every rarity now, and rarity acts through
    /// <see cref="RarityLook"/> and <see cref="RaritySpread"/> alone.
    /// </summary>
    private static readonly (string Type, string Label, (Surface S, byte R, byte G, byte B)[] Channels)[] Materials =
    [
        ("armor_plate",      "polished plate",
            [(Surface.Plate, 238, 240, 246),  (Surface.Plate, 214, 218, 226),
             (Surface.Mail, 176, 182, 192),   (Surface.Leather, 208, 176, 104)]),

        ("armor_mail",       "dark mail",
            [(Surface.Mail, 150, 154, 162),   (Surface.Mail, 122, 126, 134),
             (Surface.Cloth, 96, 100, 108),   (Surface.Leather, 140, 112, 82)]),

        ("armor_scale",      "bronze scale",
            [(Surface.Scale, 206, 154, 92),   (Surface.Scale, 176, 126, 70),
             (Surface.Leather, 140, 100, 58), (Surface.Cloth, 96, 78, 60)]),

        ("armor_lamellar",   "lacquered lamellar",
            [(Surface.LamellarMetal, 140, 66, 58),  (Surface.LamellarMetal, 96, 46, 42),
             (Surface.Leather, 188, 156, 96),       (Surface.Linen, 72, 62, 58)]),

        ("armor_laminar",    "banded laminar",
            [(Surface.LamellarLeather, 168, 150, 118), (Surface.Leather, 128, 112, 86),
             (Surface.LamellarMetal, 196, 168, 110),   (Surface.Cloth, 88, 78, 66)]),

        ("armor_brigandine", "riveted brigandine",
            [(Surface.Leather, 84, 62, 48),   (Surface.Leather, 62, 46, 36),
             (Surface.Plate, 198, 202, 210),  (Surface.Cloth, 150, 122, 88)]),
    ];

    private const int PaletteWidth = 16;
    private const int PaletteHeight = 4;

    /// <summary>
    /// Emits every look and the portrait modifiers that summon them. Returns how many were written,
    /// or zero when the catalogue came up empty — in which case nothing is emitted at all and the
    /// probe's behaviour, or vanilla's, is what remains.
    /// </summary>
    public static int WriteAll(string modDir, string gameDir, IReadOnlyList<string> cultureKeys,
        IReadOnlyDictionary<string, string> clothingGfxByCulture)
    {
        // Temporary: portrait art is reserved for forged weapons. See ArtifactForgeFlags.
        if (!ArtifactForgeFlags.ArmorOnPortrait)
        {
            Console.WriteLine("  forged armour: disabled (ArtifactForgeFlags.ArmorOnPortrait) - "
                + "armour artifacts keep vanilla art");
            return 0;
        }

        GameDir = gameDir;

        // Cleared rather than merely reused: the editor generates repeatedly in one process, and a
        // measurement cached against a mask from a previous run - or from a run with the generated
        // masks flag the other way - would be silently wrong.
        Masks.Clear();
        CoverageCache.Clear();
        DensityCache.Clear();

        var garments = ArmorCatalogue.Read(gameDir);

        if (garments.Count == 0)
        {
            Console.WriteLine("  forged armour: no war garments found in the game directory - none forged");
            return 0;
        }

        var looks = new List<Look>();

        foreach (string culture in cultureKeys)
        {
            string gfx = clothingGfxByCulture.TryGetValue(culture, out string? g) ? g : "western";

            foreach (bool female in new[] { false, true })
            {
                var garment = Pick(garments, gfx, female, culture);
                if (garment is null) continue;

                // Before anything measures or aims at this garment, because a generated mask moves
                // the regions those passes read.
                EnsureMask(modDir, gameDir, garment);

                foreach (var (type, label, channels) in Materials)
                {
                    // The gender goes in the PREFIX, not the suffix. CK3 infers an accessory's
                    // gender from the start of its name: across all 630 entries in vanilla's clothes
                    // gene there is not one exception - every name in a `male` block starts with m_
                    // or male_, every name in a `female` block with f_ or female_. Named
                    // gen_armor_..._f, all 42 female accessories were read as male and then not
                    // found when a female portrait looked for them, while all 42 male ones worked.
                    // One look per rarity as well: the surface is what rarity changes, and a
                    // surface lives in the variation, which lives on the entity - so a piece that
                    // finishes differently is a different accessory the whole way down.
                    for (int r = 0; r < Rarities.Length; r++)
                    {
                        looks.Add(new Look(
                            $"{(female ? "f" : "m")}_gen_armor_{culture}_{type}_{Rarities[r]}",
                            culture, type, label, female, Rarities[r], r, garment,
                            AimAtGarment(channels, garment)));
                    }
                }
            }
        }

        if (looks.Count == 0)
        {
            Console.WriteLine("  forged armour: no garment matched any culture - none forged");
            return 0;
        }

        // First, because it is the one step that can fail: without the gene template every
        // accessory is unusable, and emitting the rest would leave modifiers pointing at
        // nothing while looking, in game, exactly like a recolour that did not take.
        if (!WriteGeneTemplate(modDir, gameDir, looks)) return 0;

        WriteEntities(modDir, looks);
        WriteAccessories(modDir, looks);
        WriteVariations(modDir, looks);
        WriteModifiers(modDir, looks);
        int icons = WriteIcons(modDir, gameDir);

        int distinctGarments = looks.Select(l => l.Garment.Accessory).Distinct().Count();

        Console.WriteLine($"  forged armour: {looks.Count} look(s) over {distinctGarments} garment(s), "
            + $"{cultureKeys.Count} culture(s) x {Materials.Length} material(s) x "
            + $"{Rarities.Length} rarit(ies), {icons} icon(s)");

        // Per garment, because a fault seen in game is reported against a CULTURE and has to be
        // traced back to a garment before it can be acted on. Both numbers here have already caused
        // one: uneven channel coverage made some cultures barely change colour between armour types,
        // and a UV1 span other than 1.00 tiled every pattern that much denser.
        foreach (var group in looks.GroupBy(l => l.Garment.Accessory).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var garment = group.First().Garment;
            double[] cov = Coverage(garment);
            int[] order = ChannelOrder(garment);
            string cultures = string.Join(", ", group.Select(l => l.Culture).Distinct().Order(StringComparer.Ordinal));

            double[] den = ChannelDensity(garment);
            var made = MaskOf(garment);
            string mask = made is null ? "vanilla mask" : $"gen mask, metal {made.MetalArea:P0} on {"rgba"[made.MetalChannel]}";

            Console.WriteLine($"    {garment.Accessory}  [{mask}]"
                + $"  cover r{cov[0]:0.00} g{cov[1]:0.00} b{cov[2]:0.00} a{cov[3]:0.00}"
                + $"  scale x{den[0] / ReferenceDensity:0.00}/{den[1] / ReferenceDensity:0.00}"
                + $"/{den[2] / ReferenceDensity:0.00}/{den[3] / ReferenceDensity:0.00}"
                + $"  lead {"rgba"[order[0]]}{"rgba"[order[1]]}{"rgba"[order[2]]}{"rgba"[order[3]]}  <- {cultures}");
        }

        return looks.Count;
    }

    /// <summary>
    /// Gives each armour TYPE its own inventory icon.
    ///
    /// Vanilla ships exactly one piece of armour art — <c>artifact_armor.dds</c> — so every armour
    /// artifact in the game, of every type and every culture, shows the same picture. Tinting it per
    /// type is the cheapest possible fix and the one that reads: at the 30-60 pixels an icon is drawn
    /// at, colour is most of what survives, which is the same reasoning the weapon icons started from
    /// before they earned a renderer.
    ///
    /// Type rather than culture-and-type on purpose. The 3D garment already carries the culture; an
    /// icon at that size cannot, and 6 icons beat 42 that a player could not tell apart.
    ///
    /// The icons are spliced into copies of vanilla's own visual entries rather than replacing them,
    /// because redeclaring <c>armor</c> would take its 20 asset blocks with it — those are the court
    /// display models, and losing them would trade an icon for a missing statue.
    /// </summary>
    private static int WriteIcons(string modDir, string gameDir)
    {
        var lines = new List<string>();
        int made = 0;

        foreach (var (type, label, channels) in Materials)
        {
            string name = $"gen_armor_{type}";

            // Channel 0 is the type's dominant material - the one its palette leads with.
            var lead = channels[0];

            if (ForgedWeaponIcon.Write(modDir, gameDir, name, StockArmorIcon,
                    (lead.R, lead.G, lead.B)) is not { } file)
                continue;

            made++;
            lines.Add("		icon = {");
            lines.Add($"			trigger = {{ scope:artifact = {{ artifact_type = {type} }} }}   # {label}");
            lines.Add($"			reference = \"{file}\"");
            lines.Add("		}");
        }

        if (made == 0) return 0;

        const string comment =
            "Ck3MapGen: one icon per armour type, placed ahead of vanilla's single unconditional\n"
            + "one so that a trigger-gated entry is actually reached. Vanilla's own icon stays below\n"
            + "as the fallback, and every asset block it declares is left untouched.";

        foreach (var (file, key) in ArmorVisuals)
            GeneSplice.Write(gameDir, modDir, file, key, lines, comment, "common/artifacts/visuals", atStart: true);

        return made;
    }

    private sealed record Look(
        string Name, string Culture, string Type, string Label, bool Female,
        string Rarity, int RarityIndex,
        ArmorGarment Garment, (Surface S, byte R, byte G, byte B)[] Channels);

    /// <summary>
    /// Best garment for a clothing gfx, by name stem, for the right body.
    ///
    /// Falls through the family list in order and then to western, because a missing garment must
    /// not mean a culture with no artifact armour at all — vanilla's coverage is uneven, and the
    /// female set is one garment shorter than the male.
    /// </summary>
    /// <summary>
    /// A stable index into a list, derived from the culture's key.
    ///
    /// FNV-1a rather than <c>string.GetHashCode</c>, which .NET randomises per process — the same
    /// world would dress its cultures differently on every run, and two runs of one seed would stop
    /// being comparable.
    /// </summary>
    private static int StableIndex(string key, int count)
    {
        uint h = 2166136261;

        foreach (char c in key)
        {
            h ^= c;
            h *= 16777619;
        }

        return count <= 0 ? 0 : (int)(h % (uint)count);
    }

    /// <summary>
    /// The garment behind a set of accessory variants — the name with its quality and coat-of-arms
    /// suffixes taken off, so <c>..._war_nob_01_hi</c> and <c>..._war_nob_01_lo</c> collapse to one.
    /// </summary>
    private static string Stem(ArmorGarment g)
    {
        string n = g.Accessory;

        foreach (string suffix in (string[])["_no_coa", "_hi", "_lo"])
            if (n.EndsWith(suffix, StringComparison.Ordinal)) n = n[..^suffix.Length];

        return n;
    }

    private static ArmorGarment? Pick(List<ArmorGarment> all, string gfx, bool female, string culture)
    {
        // A culture stores its clothing gfx the way the file wants to read it — braces and all,
        // `{ east_slavic_clothing_gfx northern_clothing_gfx }` — because CultureWriter passes the
        // value straight through. The first entry is the culture's own; the rest are fallbacks the
        // engine walks, so it is the one worth matching on.
        string stem = gfx.Trim().Trim('{', '}').Trim().Split(
            [' ', '\t'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "western";

        if (stem.EndsWith("_clothing_gfx", StringComparison.Ordinal))
            stem = stem[..^"_clothing_gfx".Length];

        var families = GarmentsByGfx.FirstOrDefault(e => e.Gfx == stem).Families ?? FallbackFamilies;

        // ONE GARMENT PER FAMILY WAS LEAVING MOST OF THE LIBRARY UNUSED. Vanilla ships 148 war
        // garments across 62 family-and-sex groups, and 50 of those 62 hold more than one - English
        // alone has 8. Taking the first match meant every culture sharing a clothing gfx wore the
        // same garment, and an 18-culture world collapsed to 12 distinct looks.
        //
        // Keyed on the culture rather than rolled, so a given world always dresses a given culture
        // the same way and two cultures that share a gfx still differ. Ordered by name first,
        // because the catalogue's order follows directory enumeration and would otherwise drift.
        foreach (string family in families.Concat(FallbackFamilies))
        {
            // GROUPED BY STEM, because _hi and _lo are not different garments. They are the same
            // pdxmesh - checked in the .asset, both entities name it - differing only in the
            // variation vanilla assigns, and we replace the variation anyway. Choosing between them
            // looks like variety in the logs and produces the identical silhouette in game.
            var stems = all
                .Where(g => g.Female == female && g.Family.StartsWith(family, StringComparison.Ordinal))
                .GroupBy(Stem)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .ToList();

            if (stems.Count == 0) continue;

            // Prefer the _hi variant: it is the noble-tier art, and where the two differ at all it
            // is the more ornamented of them.
            var chosen = stems[StableIndex(culture, stems.Count)];

            return chosen.FirstOrDefault(g => g.Accessory.EndsWith("_hi", StringComparison.Ordinal))
                ?? chosen.OrderBy(g => g.Accessory, StringComparer.Ordinal).First();
        }

        return all.FirstOrDefault(g => g.Female == female);
    }

    // -------------------------------------------------------------------------------------

    /// <summary>
    /// One entity per look: vanilla's mesh and blend shapes, our variation.
    ///
    /// The body is copied verbatim from the vanilla entity rather than rebuilt, because it carries
    /// the blend-shape attributes that let a garment follow a fat, gaunt or dwarf body. Two things
    /// are then rewritten — the shader, to drop the coat of arms, and the whole
    /// <c>portrait_accessory</c> block, to point at our palette instead of vanilla's.
    /// </summary>
    private static void WriteEntities(string modDir, List<Look> looks)
    {
        string dir = Path.Combine(modDir, EntityDir.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(dir);

        var b = new JominiBuilder();
        b.Comment("Artifact armour entities: vanilla geometry, our colours.\n\n"
            + "Each reuses a vanilla garment's pdxmesh and blend shapes untouched and changes only\n"
            + "how it is painted - the coat-of-arms shader is swapped for the plain variation one,\n"
            + "and the pattern mask stays vanilla's while the palette becomes ours.");

        foreach (var look in looks)
        {
            b.Blank();
            b.Comment($"{look.Culture} / {look.Label} / {(look.Female ? "female" : "male")} "
                + $"<- {look.Garment.Accessory}");

            b.Raw("entity = {\n");
            b.Raw($"\tname = \"{look.Name}_entity\"\n");
            b.Raw(Rewrite(look.Garment.EntityBody, look));
            b.Raw("}\n");
        }

        ParadoxText.WriteBom(Path.Combine(dir, "00_gen_armor.asset"), b.ToString());
    }

    /// <summary>
    /// Swaps the shader and replaces the <c>portrait_accessory</c> block.
    ///
    /// Textual rather than parsed, in the same spirit as the catalogue: the two things being changed
    /// are single lines in machine-formatted files, and a parser for the whole entity grammar would
    /// be far more machinery than the job needs.
    /// </summary>
    private static string Rewrite(string body, Look look)
    {
        var kept = new List<string>();
        int skipDepth = 0;

        foreach (string raw in body.Split('\n'))
        {
            string line = raw.TrimEnd();
            string t = line.TrimStart();

            // Drop vanilla's whole game_data block; ours replaces it wholesale.
            if (skipDepth > 0)
            {
                skipDepth += t.Count(c => c == '{') - t.Count(c => c == '}');
                continue;
            }

            if (t.StartsWith("game_data", StringComparison.Ordinal))
            {
                skipDepth = 1 + t.Count(c => c == '{') - t.Count(c => c == '}');
                continue;
            }

            if (t.StartsWith("shader = ", StringComparison.Ordinal))
            {
                kept.Add($"\t\tshader = \"{Shader}\"");
                continue;
            }

            if (t.StartsWith("shader_file = ", StringComparison.Ordinal))
            {
                kept.Add($"\t\tshader_file = \"{ShaderFile}\"");
                continue;
            }

            if (line.Trim().Length > 0) kept.Add(line);
        }

        var b = new System.Text.StringBuilder();
        foreach (string line in kept) b.Append(line).Append('\n');

        b.Append("\n\tgame_data = {\n\t\tportrait_entity_user_data = {\n\t\t\tportrait_accessory = {\n");

        // Ours where one was generated, vanilla's otherwise. A garment with neither still takes a
        // palette; the shader simply applies it across everything the swatch covers rather than per
        // region.
        if (MaskRef(look.Garment) is { } mask)
            b.Append($"\t\t\t\tpattern_mask = \"{mask}\"\n");

        b.Append($"\t\t\t\tvariation = \"{look.Name}_variation\"\n");
        b.Append("\t\t\t}\n\t\t}\n\t}\n");

        return b.ToString();
    }

    private static void WriteAccessories(string modDir, List<Look> looks)
    {
        string dir = Path.Combine(modDir, "gfx", "portraits", "accessories");
        Directory.CreateDirectory(dir);

        var b = new JominiBuilder();
        b.Comment("Artifact armour accessories, one per look.\n\n"
            + "Each is ALSO listed in the gene template in common/genes, and has to be. An earlier\n"
            + "version cited vanilla's most_clothes and defined the accessories only here, reasoning\n"
            + "that ck3-tiger checks an accessory exists but never checks which template it belongs\n"
            + "to. Tiger does not check it; the ENGINE does, and said so:\n"
            + "  Can't find accessory 'gen_armor_..._plate_m' in gene template most_clothes\n"
            + "The garment then falls back to the culture's ordinary war dress - which is the same\n"
            + "art - so the failure reads as 'the recolour did nothing' rather than as an error.");

        foreach (var look in looks)
        {
            b.Blank();

            using (b.Block(look.Name))
            {
                // Vanilla's own tags, kept: they tell the body to shrink arms, chest and belly so a
                // garment sits on the character instead of intersecting them.
                if (look.Garment.SetTags.Length > 0) b.Quoted("set_tags", look.Garment.SetTags);

                b.Inline("entity", "required_tags", "=", "\"\"",
                    "shared_pose_entity", "=", "torso",
                    "entity", "=", $"{look.Name}_entity");
            }
        }

        ParadoxText.WriteBom(Path.Combine(dir, "00_gen_armor.txt"), b.ToString());
    }

    /// <summary>
    /// The gene template our accessories belong to.
    ///
    /// Weights are all 1 and never actually rolled: nothing selects from this template at random,
    /// because every portrait modifier names its accessory outright. The list exists purely so the
    /// engine's membership check passes.
    ///
    /// **If this file is what breaks, it breaks loudly.** Should a second declaration of
    /// <c>clothes</c> replace vanilla's gene rather than merge with it, every character in the game
    /// loses their clothing rather than just their armour — so a world where everyone is suddenly
    /// unclothed means delete this one file, not hunt through the armour code.
    /// </summary>
    private static bool WriteGeneTemplate(string modDir, string gameDir, List<Look> looks)
    {
        var block = new JominiBuilder(startDepth: 3);
        var byLook = TemplateByLook(looks);

        var chunks = byLook.Values.Distinct()
            .OrderBy(n => int.Parse(n[(n.LastIndexOf('_') + 1)..]))
            .ToList();

        foreach (string template in chunks)
        {
            using (block.Block(template))
            {
                block.Field("index", GeneTemplateIndexBase + chunks.IndexOf(template));

                // Hand-modelled pieces do NOT ride along here any more - they layer over clothes
                // from the cloaks gene instead, and an accessory may only belong to one gene.
                foreach (bool female in new[] { false, true })
                {
                    using (block.Block(female ? "female" : "male"))
                    {
                        foreach (var look in looks
                            .Where(l => l.Female == female && byLook[l.Name] == template)
                            .OrderBy(l => l.Name, StringComparer.Ordinal))
                        {
                            block.Field("1", look.Name);
                        }
                    }
                }

                // Present on all 197 of vanilla's clothes templates, without exception. Children
                // fall back to the adult list of their sex; a template that omits them is the only
                // structural difference our first version had from vanilla's, and female lookups
                // were failing.
                block.Field("boy", "male");
                block.Field("girl", "female");
            }
        }

        Console.WriteLine($"    gene templates: {chunks.Count} x <={GeneTemplateChunk} per sex "
            + $"({looks.Count(l => !l.Female)} male, {looks.Count(l => l.Female)} female accessories) "
            + "- one list per template must stay under the engine's 255 weight sum");

        return GeneSplice.Write(gameDir, modDir, GeneFile, "clothes",
            block.ToString().TrimEnd('\n').Split('\n'),
            "Added by Ck3MapGen: the templates artifact armour accessories belong to.\n"
            + "A portrait modifier's accessory must be a member of the template it cites;\n"
            + "the engine enforces that even though ck3-tiger does not.\n\n"
            + "Split across several templates because a single list's WEIGHTS MUST SUM UNDER 255.\n"
            + "Past that the gene still loads and tiger stays quiet, but the entries beyond the cut\n"
            + "are unreachable and their wearers silently fall back to ordinary war dress.");
    }

    /// <summary>The pattern_textures entry a surface is declared under.</summary>
    private static string TextureName(Surface s) => $"gen_armor_{s.ToString().ToLowerInvariant()}";

    /// <summary>
    /// One variation and one palette per look, against vanilla's existing mask.
    ///
    /// Six surfaces are declared and each channel names the one its material calls for, so a single
    /// garment can be leather with metal rivets rather than four shades of one substance. Still no
    /// textures of our own beyond a 384-byte palette — the swatches are shipped in BaseFilesToCopy
    /// and shared across every world.
    /// </summary>
    private static void WriteVariations(string modDir, List<Look> looks)
    {
        string dir = Path.Combine(modDir, "gfx", "portraits", "accessory_variations");
        Directory.CreateDirectory(dir);

        string paletteDir = Path.Combine(modDir, EntityDir.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(paletteDir);

        var b = new JominiBuilder();
        b.Comment("Artifact armour colourways. The mask is vanilla's - it already marks the\n"
            + "garment's regions - and only the palette is ours.\n\n"
            + "The swatches are vanilla's too, and that is the whole point: a swatch's normal map\n"
            + "and ambient occlusion REPLACE the garment's own inside the mask, so a flat swatch\n"
            + "sands the armour smooth. These carry real relief - see the Surface enum for the\n"
            + "measurements.");

        // Declared here rather than borrowed from the weapon forge, which registers its own entries
        // under gen_weapon_* names: naming those would tie armour to whether any parts library
        // happened to load, and a checkout with no weaponparts/ would leave every armour variation
        // pointing at nothing.
        b.Blank();

        foreach (Surface surface in Enum.GetValues<Surface>())
        {
            var swatch = Swatches[surface];
            string path = $"{VanillaSwatchDir}/{swatch.Dir}/{swatch.File}";

            using (b.Block("pattern_textures"))
            {
                b.Quoted("name", TextureName(surface));
                b.Quoted("colormask", $"{path}_masks.dds");
                b.Quoted("normal", $"{path}_normal.dds");
                b.Quoted("properties", $"{path}_properties.dds");
            }

            b.Blank();
        }

        // ONE LAYOUT PER SUBSTANCE PER GARMENT UV1 SPAN, because scale now matters twice over.
        //
        // A flat colour tiles to itself at any scale, so this was a single shared entry at 1.0; a
        // relief swatch at 1.0 gives one mail ring the size of a torso. The base figures are
        // vanilla's own modal pairings for that swatch, counted across its variation files and
        // restricted to torso garments - chainmail 0.25 (63 uses against 47 at 0.2), lamellar 0.13,
        // the fabrics 0.25.
        //
        // Those figures are only right at vanilla's own typical UV1 density, and the garments differ
        // from it by up to 3.5x in either direction. Each channel is therefore scaled by its own
        // measured density - see ChannelDensity, which also records why the UV1 SPAN this used
        // first was the wrong measure and over-corrected the western garment fourfold.
        var wanted = looks
            .SelectMany(l => l.Channels.Select((c, i) => (c.S, Scale: ScaleFor(c.S, l.Garment, i))))
            .Select(x => (x.S, Name: LayoutName(x.S, x.Scale), x.Scale))
            .DistinctBy(x => x.Name)
            .OrderBy(x => x.S).ThenBy(x => x.Scale);

        foreach (var (surface, name, scale) in wanted)
        {
            using (b.Block("pattern_layout"))
            {
                b.Quoted("name", name);
                b.Inline("scale", "min", "=", ScaleText(scale), "max", "=", ScaleText(scale));
                b.Inline("rotation", "min", "=", "0", "max", "=", "0");
                b.Inline("offset", "x", "=", "{", "min", "=", "0", "max", "=", "0", "}",
                                   "y", "=", "{", "min", "=", "0", "max", "=", "0", "}");
            }

            b.Blank();
        }

        foreach (var look in looks)
        {
            WritePalette(Path.Combine(paletteDir, $"{look.Name}_palette.dds"),
                look.Channels, look.RarityIndex);

            b.Blank();
            b.Comment($"{look.Culture}: {look.Label}, {look.Rarity}");

            using (b.Block("variation"))
            {
                b.Quoted("name", $"{look.Name}_variation");

                using (b.Block("pattern"))
                {
                    b.Field("weight", 1);

                    // Each channel names its OWN substance, which is what lets one garment carry
                    // leather and metal at once rather than four shades of the same thing. The
                    // layout travels with the substance, because each one tiles at its own scale.
                    string[] names = ["r", "g", "b", "a"];

                    for (int c = 0; c < names.Length; c++)
                    {
                        var surface = c < look.Channels.Length ? look.Channels[c].S : Surface.Plate;

                        b.Inline(names[c],
                            "textures", "=", $"\"{TextureName(surface)}\"",
                            "layout", "=", $"\"{LayoutName(surface, ScaleFor(surface, look.Garment, c))}\"");
                    }
                }

                using (b.Block("color_palette"))
                {
                    b.Field("weight", 1);
                    b.Quoted("texture", $"{EntityDir}/{look.Name}_palette.dds");
                }
            }
        }

        ParadoxText.WriteBom(Path.Combine(dir, "00_gen_armor_variations.txt"), b.ToString());
    }

    private static void WritePalette(string path, (Surface S, byte R, byte G, byte B)[] channels, int rarity)
    {
        var bgra = new byte[PaletteWidth * PaletteHeight * 4];
        double spread = RaritySpread[Math.Clamp(rarity, 0, RaritySpread.Length - 1)];

        // A mask channel reads a BLOCK of four columns, not one: the shader indexes the palette at
        // `channel * 4 + <the swatch's own colormask channel>`, so mask channel g lands on columns
        // 4-7. Writing the four colours into columns 0-3 instead — which is what this did first —
        // tints only the red channel and leaves columns 4, 8 and 12 white. White is not a colour
        // there, it is "no tint", so three quarters of the garment kept its vanilla diffuse and
        // every armour type looked identical.
        //
        // WITHIN a block the columns are the swatch's own colormask channels. Only the lamellar
        // swatches fire more than one, so for everything else this writes one colour four times and
        // the ramp is inert — which is correct, not wasteful: the column is never sampled.
        for (int y = 0; y < PaletteHeight; y++)
        {
            for (int x = 0; x < PaletteWidth; x++)
            {
                int i = (y * PaletteWidth + x) * 4;
                int block = x / 4, tone = x % 4;

                if (block >= channels.Length)
                {
                    bgra[i + 0] = bgra[i + 1] = bgra[i + 2] = bgra[i + 3] = 255;
                    continue;
                }

                var ch = channels[block];
                var enriched = Enrich((ch.R, ch.G, ch.B), rarity);
                double r = enriched.R, g = enriched.G, bl = enriched.B;

                // Cancel the substance's own ambient occlusion, hue preserved.
                //
                // Scaling the three components independently and clamping each at 255 would drive a
                // lifted colour toward WHITE - mail's illustrious entry is (185,191,202) and a 1.60
                // lift puts all three over the top, so the blue-grey would clip away to flat grey
                // exactly where the piece is supposed to look its best. Scaling the whole triple by
                // the headroom of its largest component keeps the hue and simply saturates.
                double lift = AoLift(ch.S);
                double peak = Math.Max(r, Math.Max(g, bl)) * lift;
                if (peak > 255) lift *= 255 / peak;

                r *= lift; g *= lift; bl *= lift;

                // Tones run dark to light across the block. Which of the swatch's channels is the
                // recess and which the crown is not knowable from here, so the ramp is symmetric
                // about the base colour: either ordering gives the same material read, because what
                // reads is the CONTRAST between tones, not their direction.
                int tones = Math.Max(1, Swatches[ch.S].Tones);
                double t = tones == 1 ? 0.5 : (double)Math.Min(tone, tones - 1) / (tones - 1);
                double f = 1.0 - spread + 2 * spread * t;

                static byte Clamp(double v) => (byte)Math.Clamp(v, 0, 255);

                bgra[i + 0] = Clamp(bl * f);
                bgra[i + 1] = Clamp(g * f);
                bgra[i + 2] = Clamp(r * f);
                bgra[i + 3] = 255;
            }
        }

        DdsWriter.WriteBgra(path, PaletteWidth, PaletteHeight, bgra);
    }

    /// <summary>
    /// The portrait modifiers that put a look on a character.
    ///
    /// Priority 7 puts artifact armour above vanilla's whole clothes ladder — ordinary armour at 4,
    /// situational at 5, special at 6 — because an artifact should outrank the dress a character
    /// would otherwise have chosen.
    ///
    /// Each entry tests three things and all of them are stable: the artifact's own type, its
    /// CREATOR's culture rather than the wearer's, and the per-artifact hide toggle. Reading the
    /// creator is what keeps a stolen piece looking like itself.
    /// </summary>
    private static void WriteModifiers(string modDir, List<Look> looks)
    {
        string dir = Path.Combine(modDir, "gfx", "portraits", "portrait_modifiers");
        Directory.CreateDirectory(dir);

        var b = new JominiBuilder();
        var byLook = TemplateByLook(looks);

        b.Comment("Artifact armour: what a character wears while an armour artifact is equipped.\n\n"
            + "Gated on the artifact's CREATOR culture, never the wearer's. A portrait modifier is\n"
            + "evaluated on whoever is being drawn, so a wearer gate would repaint a stolen cuirass in\n"
            + "the thief's colours; the creator never changes, so the piece keeps its own look.\n\n"
            + "Priority 7 sits above vanilla's clothes ladder - armour 4, situational 5, special 6.\n\n"
            + "Each entry names the gene template ITS OWN accessory was written into. The accessories\n"
            + "are split across several templates to keep each list under the engine's 255 weight sum,\n"
            + "so a single shared template name here would cite the wrong one for most of them - and\n"
            + "that failure is silent, showing as ordinary war dress rather than as an error.");

        b.Blank();

        using (b.Block("gen_artifact_armor"))
        {
            b.Field("usage", "game");
            b.Field("selection_behavior", "max");
            b.Field("priority", 7);

            foreach (var look in looks)
            {
                b.Blank();

                using (b.Block(look.Name))
                {
                    using (b.Block("dna_modifiers"))
                    using (b.Block("accessory"))
                    {
                        b.Field("mode", "add");
                        b.Field("gene", "clothes");

                        // Our own template, declared in common/genes. Citing a vanilla one fails:
                        // the engine checks that the accessory is a member of the template named.
                        b.Field("template", byLook[look.Name]);
                        b.Field("accessory", look.Name);

                        // WITHOUT THIS, A FEMALE ACCESSORY IS NEVER FOUND. The engine assumes an
                        // accessory reference is male and looks it up in the template's `male` list;
                        // a female one is in the `female` list, so the lookup fails and the log says
                        // "Can't find accessory '<name>' in gene template". All 41 female accessory
                        // references in vanilla carry this field and not one omits it.
                        if (look.Female) b.Field("type", "female");
                    }

                    b.Inline("outfit_tags", "military_outfit");

                    // TWO WAYS TO MATCH, AND THE WEIGHTS ARE WHAT ORDER THEM.
                    //
                    // Gating on the creator alone means an artifact with NO creator matches nothing
                    // and is never worn. That is not an edge case: **62% of vanilla's own 665
                    // create_artifact blocks set no creator at all**, so most armour the game hands
                    // out through inspirations, tournaments and events was silently invisible. Our
                    // own startup artifacts had the same hole until they began naming a maker.
                    //
                    // The fallback is the artifact's OWNER, which on a portrait is the character
                    // being drawn. That reintroduces the problem the creator gate exists to prevent -
                    // a stolen piece re-dressing itself in the thief's colours - so the two are
                    // weighted rather than OR'd: with `selection_behavior = max` the group applies
                    // its heaviest entry, so a creator match at 1000 beats an owner match at 600 and
                    // a piece with a known maker still keeps its own look. Only a piece with no
                    // maker at all falls through to the wearer.
                    //
                    // Expressed as two modifiers rather than one OR because `exists = creator`
                    // appears nowhere in vanilla and is not worth relying on; separate weights say
                    // the same thing in a form vanilla demonstrably uses.
                    using (b.Block("weight"))
                    {
                        b.Field("base", 0);

                        foreach (bool byCreator in new[] { true, false })
                        {
                            using (b.Block("modifier"))
                            {
                                b.Field("add", byCreator ? 1000 : 600);
                                b.Field("is_female", look.Female ? "yes" : "no");

                                using (b.Block("any_equipped_character_artifact"))
                                {
                                    b.Field("artifact_type", look.Type);
                                    b.Field("rarity", look.Rarity);

                                    // Written by hand because `?=` is one token and Block would put
                                    // the builder's " = " separator inside it. The safe form matters
                                    // for its own sake too: an artifact with no creator would
                                    // otherwise throw on every portrait that evaluates this.
                                    //
                                    // The owner link is `artifact_owner`, not `owner` - vanilla's
                                    // artifact triggers use that name throughout.
                                    string link = byCreator ? "creator" : "artifact_owner";

                                    b.Raw($"{b.IndentAt(b.Depth)}{link} ?= {{\n");
                                    b.Raw($"{b.IndentAt(b.Depth + 1)}culture = culture:{look.Culture}\n");
                                    b.Raw($"{b.IndentAt(b.Depth)}}}\n");

                                    using (b.Block("NOT"))
                                        b.Field("has_variable", "gen_artifact_hide_on_portrait");
                                }
                            }
                        }
                    }
                }
            }
        }

        ParadoxText.WriteBom(Path.Combine(dir, "zz_gen_artifact_armor.txt"), b.ToString());
    }
}
