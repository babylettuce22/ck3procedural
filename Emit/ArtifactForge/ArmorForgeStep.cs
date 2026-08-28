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
    public const string GeneTemplate = "gen_armor_clothes";
    private const int GeneTemplateIndex = 900;

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

    /// <summary>Our own flat swatches, shipped in BaseFilesToCopy and shared with weapons.</summary>
    private const string SwatchDir =
        "gfx/portraits/accessory_variations/textures/patterns/gen";

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
    /// What each armour type is made of. Four channels, because that is what a pattern mask carries.
    ///
    /// The channels are vanilla's and their meaning differs per garment — one may separate mail from
    /// surcoat, another plate from strapping — so a colour cannot be aimed at a known region. What
    /// can be controlled is the *set*: every channel of one type draws from the same material family,
    /// so whichever region each channel turns out to be, the garment reads as one coherent thing.
    /// That is the same reasoning behind the weapon finishes, under a harder constraint.
    /// </summary>
    /// <summary>
    /// The surfaces a channel can be made of, and the swatch each one names.
    ///
    /// A palette says what COLOUR a region is; a swatch says what it is MADE of, because roughness
    /// and metalness live in the swatch's properties map rather than in the palette. That split is
    /// what lets two regions share a colour and still read as different substances — a brigandine's
    /// rivets are not merely a lighter brown than its leather, they are metal against cloth.
    ///
    /// Roughness carries most of it. Vanilla's metal swatches all sit at 0.40; these span 0.10 to
    /// 0.80, which is the distance between a mirror and felt. All are authored by
    /// <c>tools/make_pattern_swatches.py</c> and shipped in BaseFilesToCopy.
    /// </summary>
    private enum Surface { Polished, Steel, RoughIron, Leather, Cloth, Lacquer }

    /// <summary>
    /// The four rarities, in the order the ladders below are indexed by.
    /// </summary>
    private static readonly string[] Rarities = ["common", "masterwork", "famed", "illustrious"];

    /// <summary>
    /// RARITY PICKS THE FINISH; TYPE PICKS THE FAMILY.
    ///
    /// A type's channel no longer names the surface outright — it names a surface whose FAMILY is
    /// what matters, and rarity then walks that family from worked to finished. So a mail channel is
    /// rough iron on a common piece and polished steel on an illustrious one, while a strap stays
    /// leather-ish throughout and merely goes from raw cloth to lacquered.
    ///
    /// Two ladders rather than one ordering by roughness, because a single ordering runs metal into
    /// fabric: polished, lacquer, steel, leather, rough iron, cloth is a valid roughness sort and a
    /// nonsense material sort. Metalness is the thing that must not change with rarity — a fine mail
    /// shirt is still metal, and a common gambeson is still cloth.
    ///
    /// The top two rungs repeat on purpose. Illustrious differs from famed by its palette and by the
    /// icon's own rarity frame, not by inventing a seventh surface.
    /// </summary>
    private static readonly Surface[] MetalLadder =
        [Surface.RoughIron, Surface.Steel, Surface.Polished, Surface.Polished];

    private static readonly Surface[] SoftLadder =
        [Surface.Cloth, Surface.Leather, Surface.Lacquer, Surface.Lacquer];

    private static bool IsMetal(Surface s) =>
        s is Surface.Polished or Surface.Steel or Surface.RoughIron;

    /// <summary>
    /// How rarity enriches the COLOUR, on top of what it does to the surface.
    ///
    /// A second signal, added because the first one on its own was not readable in game: the surface
    /// ladder moves roughness and metalness, both of which are lighting-dependent and can wash out
    /// on a dark garment under a soft portrait key. Brightness and saturation do not depend on the
    /// light at all, so they carry when the finish does not.
    ///
    /// Deliberately gentle. Rarity should say "the same armour, better made" — a ramp large enough
    /// to repaint the piece would fight the type palettes, which are what make plate distinguishable
    /// from mail in the first place.
    /// </summary>
    private static readonly (double Gain, double Sat)[] RarityTint =
    [
        (0.86, 0.80),
        (0.95, 0.92),
        (1.06, 1.04),
        (1.16, 1.12),
    ];

    /// <summary>Applies a rarity's gain and saturation to one channel colour.</summary>
    private static (byte R, byte G, byte B) Enrich(
        (Surface S, byte R, byte G, byte B) channel, double gain, double sat)
    {
        double r = channel.R, g = channel.G, b = channel.B;
        double lum = 0.2126 * r + 0.7152 * g + 0.0722 * b;

        r = (lum + (r - lum) * sat) * gain;
        g = (lum + (g - lum) * sat) * gain;
        b = (lum + (b - lum) * sat) * gain;

        return ((byte)Math.Clamp(r, 0, 255), (byte)Math.Clamp(g, 0, 255), (byte)Math.Clamp(b, 0, 255));
    }

    /// <summary>The surface a channel actually gets, once rarity has had its say.</summary>
    /// <summary>
    /// How rarity enriches the PALETTE, on top of the surface ladder.
    ///
    /// The ladder alone was not enough, and the test that showed it was a good one: six armour types
    /// at one rarity looked clearly different, while one type across four rarities looked identical.
    /// Type moves both palette and surface; rarity moved only surface. So what the eye was actually
    /// reading between plate and scale was COLOUR, and roughness was contributing little — plate's
    /// palette is near-white against mail's mid grey, and that is the whole of the visible gap.
    ///
    /// Portrait lighting is the likely reason. Every metal surface here sits above 0.86 metalness,
    /// and a near-fully metallic material has almost no diffuse response — it is visible as
    /// reflection, which a soft portrait key light supplies very little of. Roughness has little to
    /// act on.
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

    private static Surface SurfaceFor(Surface family, int rarity) =>
        (IsMetal(family) ? MetalLadder : SoftLadder)[Math.Clamp(rarity, 0, Rarities.Length - 1)];

    private static string SwatchOf(Surface s) => s switch
    {
        Surface.Polished => "gen_steel_polished",
        Surface.RoughIron => "gen_iron_rough",
        Surface.Leather => "gen_leather",
        Surface.Cloth => "gen_cloth",
        Surface.Lacquer => "gen_lacquer",
        _ => "gen_steel",
    };

    /// <summary>
    /// What each armour type is made of: four channels, each a surface and a colour.
    ///
    /// The channels are vanilla's and their meaning differs per garment — one may separate mail from
    /// surcoat, another plate from strapping — so a specific region cannot be targeted. What can be
    /// controlled is the SET, so whichever region each channel turns out to be, the garment reads as
    /// one coherent object. Each type therefore mixes surfaces the way the real armour does: a
    /// brigandine is mostly leather with metal rivets, mail is rough iron with leather straps, and
    /// plate is polished almost throughout with a brass trim.
    /// </summary>
    private static readonly (string Type, string Label, (Surface S, byte R, byte G, byte B)[] Channels)[] Materials =
    [
        ("armor_plate",      "polished plate",
            [(Surface.Polished, 238, 240, 246), (Surface.Polished, 214, 218, 226),
             (Surface.Steel, 176, 182, 192),    (Surface.Steel, 208, 176, 104)]),

        ("armor_mail",       "dark mail",
            [(Surface.RoughIron, 150, 154, 162), (Surface.RoughIron, 122, 126, 134),
             (Surface.RoughIron, 96, 100, 108),  (Surface.Leather, 140, 112, 82)]),

        ("armor_scale",      "bronze scale",
            [(Surface.Steel, 206, 154, 92),   (Surface.Steel, 176, 126, 70),
             (Surface.RoughIron, 140, 100, 58), (Surface.Leather, 96, 78, 60)]),

        ("armor_lamellar",   "lacquered lamellar",
            [(Surface.Lacquer, 140, 66, 58),  (Surface.Lacquer, 96, 46, 42),
             (Surface.Steel, 188, 156, 96),   (Surface.Leather, 72, 62, 58)]),

        ("armor_laminar",    "banded laminar",
            [(Surface.Leather, 168, 150, 118), (Surface.Leather, 128, 112, 86),
             (Surface.Steel, 196, 168, 110),   (Surface.Cloth, 88, 78, 66)]),

        ("armor_brigandine", "riveted brigandine",
            [(Surface.Cloth, 84, 62, 48),     (Surface.Leather, 62, 46, 36),
             (Surface.Polished, 198, 202, 210), (Surface.Steel, 150, 122, 88)]),
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
                var garment = Pick(garments, gfx, female);
                if (garment is null) continue;

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
                            culture, type, label, female, Rarities[r], r, garment, channels));
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
    private static ArmorGarment? Pick(List<ArmorGarment> all, string gfx, bool female)
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

        foreach (string family in families.Concat(FallbackFamilies))
        {
            var hit = all.FirstOrDefault(g => g.Female == female
                && g.Family.StartsWith(family, StringComparison.Ordinal));

            if (hit is not null) return hit;
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

        // A garment with no mask of its own still takes a palette; the shader simply applies it
        // across everything the swatch covers rather than per region.
        if (look.Garment.PatternMask is { } mask)
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

        using (block.Block(GeneTemplate))
        {
            block.Field("index", GeneTemplateIndex);

            // Hand-modelled pieces do NOT ride along here any more - they layer over clothes from
            // the cloaks gene instead, and an accessory may only belong to one gene.
            foreach (bool female in new[] { false, true })
            {
                using (block.Block(female ? "female" : "male"))
                {
                    foreach (var look in looks.Where(l => l.Female == female))
                        block.Field("1", look.Name);
                }
            }

            // Present on all 197 of vanilla's clothes templates, without exception. Children fall
            // back to the adult list of their sex; a template that omits them is the only structural
            // difference our first version had from vanilla's, and female lookups were failing.
            block.Field("boy", "male");
            block.Field("girl", "female");
        }

        return GeneSplice.Write(gameDir, modDir, GeneFile, "clothes",
            block.ToString().TrimEnd('\n').Split('\n'),
            "Added by Ck3MapGen: the template artifact armour accessories belong to.\n"
            + "A portrait modifier's accessory must be a member of the template it cites;\n"
            + "the engine enforces that even though ck3-tiger does not.");
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
            + "garment's regions - and only the palette is ours.");

        // Declared here rather than borrowed from the weapon forge. The swatches are the same two
        // files, but naming the weapons' entries would tie armour to whether any parts library
        // happened to load: a checkout with no weaponparts/ forges no weapons, emits no
        // gen_weapon_* names, and would leave every armour variation pointing at nothing.
        //
        // Scale goes unmentioned for the same reason it stopped mattering for weapon metal: both
        // swatches are one flat colour, and a flat texture tiles to itself at any scale.
        b.Blank();

        foreach (Surface surface in Enum.GetValues<Surface>())
        {
            string swatch = SwatchOf(surface);

            using (b.Block("pattern_textures"))
            {
                b.Quoted("name", TextureName(surface));
                b.Quoted("colormask", $"{SwatchDir}/{swatch}_masks.dds");
                b.Quoted("normal", $"{SwatchDir}/{swatch}_normal.dds");
                b.Quoted("properties", $"{SwatchDir}/{swatch}_properties.dds");
            }

            b.Blank();
        }

        using (b.Block("pattern_layout"))
        {
            b.Quoted("name", "gen_armor_layout");
            b.Inline("scale", "min", "=", "1.0", "max", "=", "1.0");
            b.Inline("rotation", "min", "=", "0", "max", "=", "0");
            b.Inline("offset", "x", "=", "{", "min", "=", "0", "max", "=", "0", "}",
                               "y", "=", "{", "min", "=", "0", "max", "=", "0", "}");
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

                    // Each channel names its OWN surface, which is what lets one garment carry
                    // leather and metal at once rather than four shades of the same substance.
                    string[] names = ["r", "g", "b", "a"];

                    for (int c = 0; c < names.Length; c++)
                    {
                        var family = c < look.Channels.Length ? look.Channels[c].S : Surface.Steel;
                        var surface = SurfaceFor(family, look.RarityIndex);

                        b.Inline(names[c],
                            "textures", "=", $"\"{TextureName(surface)}\"",
                            "layout", "=", "\"gen_armor_layout\"");
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
        var (gain, sat) = RarityTint[Math.Clamp(rarity, 0, RarityTint.Length - 1)];

        // A mask channel reads a BLOCK of four columns, not one: the shader indexes the palette at
        // `channel * 4 + <the swatch's own colormask channel>`, and our swatch fires red alone, so
        // channel g lands on column g*4. Writing the four colours into columns 0-3 instead — which
        // is what this did first — tints only the red channel and leaves columns 4, 8 and 12 white.
        // White is not a colour there, it is "no tint", so three quarters of the garment kept its
        // vanilla diffuse and every armour type looked identical.
        for (int y = 0; y < PaletteHeight; y++)
        {
            for (int x = 0; x < PaletteWidth; x++)
            {
                int i = (y * PaletteWidth + x) * 4;
                int block = x / 4;
                bool tinted = block < channels.Length;

                bgra[i + 0] = tinted ? channels[block].B : (byte)255;
                bgra[i + 1] = tinted ? channels[block].G : (byte)255;
                bgra[i + 2] = tinted ? channels[block].R : (byte)255;
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
        b.Comment("Artifact armour: what a character wears while an armour artifact is equipped.\n\n"
            + "Gated on the artifact's CREATOR culture, never the wearer's. A portrait modifier is\n"
            + "evaluated on whoever is being drawn, so a wearer gate would repaint a stolen cuirass in\n"
            + "the thief's colours; the creator never changes, so the piece keeps its own look.\n\n"
            + "Priority 7 sits above vanilla's clothes ladder - armour 4, situational 5, special 6.");

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
                        b.Field("template", GeneTemplate);
                        b.Field("accessory", look.Name);

                        // WITHOUT THIS, A FEMALE ACCESSORY IS NEVER FOUND. The engine assumes an
                        // accessory reference is male and looks it up in the template's `male` list;
                        // a female one is in the `female` list, so the lookup fails and the log says
                        // "Can't find accessory '<name>' in gene template". All 41 female accessory
                        // references in vanilla carry this field and not one omits it.
                        if (look.Female) b.Field("type", "female");
                    }

                    b.Inline("outfit_tags", "military_outfit");

                    using (b.Block("weight"))
                    {
                        b.Field("base", 0);

                        using (b.Block("modifier"))
                        {
                            b.Field("add", 1000);
                            b.Field("is_female", look.Female ? "yes" : "no");

                            using (b.Block("any_equipped_character_artifact"))
                            {
                                b.Field("artifact_type", look.Type);
                                b.Field("rarity", look.Rarity);

                                // Written by hand because `?=` is one token and Block would put the
                                // builder's " = " separator inside it. The safe form matters: an
                                // artifact with no creator - anything made before history - would
                                // otherwise throw on every portrait that evaluates this.
                                b.Raw($"{b.IndentAt(b.Depth)}creator ?= {{\n");
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

        ParadoxText.WriteBom(Path.Combine(dir, "zz_gen_artifact_armor.txt"), b.ToString());
    }
}
