namespace Ck3MapGen.Emit;

using Ck3MapGen.Core;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;
using System.IO;

/// <summary>Where a forged weapon's recolour lives: its own mask, and its variation name.</summary>
/// <param name="PrimaryColour">
/// The colour each weapon's inventory icon takes: the most chromatic material on it that covers a
/// meaningful share of its area — in practice the fitting metal, since realistic edges are nearly
/// all steel grey. See <c>ForgedWeaponRecolour.IconMaterial</c> for why that beats picking by area.
/// </param>
/// <param name="PartColour">
/// The colour each part of a weapon ends up wearing, indexed the same way as
/// <see cref="ForgedWeapon.Parts"/>. Parts forced to share a mask channel share an entry, which is
/// exactly what the game will draw. The icon renderer uses this rather than sampling the mask back
/// out of the .dds: same answer, no round trip, and no dependence on a part's triangles being big
/// enough to win a texel.
/// </param>
public sealed record ForgedRecolour(
    IReadOnlyDictionary<string, string> MaskByWeapon,
    IReadOnlyDictionary<string, (byte R, byte G, byte B)> PrimaryColour,
    IReadOnlyDictionary<string, IReadOnlyList<(byte R, byte G, byte B)>> PartColour)
{
    public string VariationFor(string weaponName) => $"{weaponName}_variation";

    public string MaskFor(string weaponName) => MaskByWeapon[weaponName];
}

/// <summary>
/// Gives every forged weapon its colourways, using CK3's own pattern/variation system.
///
/// **How the engine does it** — read out of
/// <c>jomini/gfx/FX/jomini/portrait_accessory_variation.fxh</c>, not inferred:
///
/// <code>
/// float4 Mask = PdxTex2D( PatternMask, Input.UV0 );   // which channel applies where -> map1
/// ApplyPattern( Input.UV1, ... );                     // the tiling swatch           -> map2
/// </code>
///
/// So the mask is read in the model's **existing atlas UV**, and only the swatch tiles over UV2.
/// One <c>pattern_mask</c> per entity, never per material — checked across every vanilla asset.
///
/// **Per-part colour, without rejecting anything.** A forged weapon mixes parts whose map1 layouts
/// often overlap (measured: 53.9% of cross-family slot pairs share texels), so parts cannot simply
/// be handed a channel each. But the constraint has a clean shape: two parts sharing a texel must
/// share a channel, otherwise one would overwrite the other. Grouping parts into connected
/// components of the "overlaps" relation therefore always produces a valid mask — and since a sword
/// has four parts, there are never more than four groups to fit in four channels.
///
/// The result degrades gracefully instead of failing. Measured over the 14-family library, as a
/// count of independently colourable **groups**:
///
/// <code>
///   single-family weapons            12 of 14 get four groups
///   foreign blade + coherent hilt     22% four, 22% three, 37% two, 19% one
///   unrestricted four-family mix       3% four, 10% three, 30% two, 58% one
/// </code>
///
/// A one-group weapon is exactly the old whole-weapon tint, so nothing is lost by trying.
///
/// **Groups are the ceiling on colours, not the count of them.** What each group is actually made
/// of comes from <see cref="PickMaterials"/>, which draws by role from a single coherent finish —
/// so a four-group sword usually lands on three materials (edge, fittings, handle) and only takes a
/// fourth when the accent roll fires. That is deliberate: matching guard and pommel is what a real
/// weapon looks like, and four unrelated metals is what the previous draw-without-replacement
/// palette guaranteed instead.
/// </summary>
public static class ForgedWeaponRecolour
{
    private const string SwatchDir = "gfx/portraits/accessory_variations/textures/patterns";

    /// <summary>
    /// Mask resolution. The mask carries region boundaries rather than detail, and is written
    /// uncompressed because <see cref="DdsWriter"/> writes no block formats — 256 keeps a weapon's
    /// mask at a quarter-megabyte while still resolving the gap between UV islands.
    /// </summary>
    private const int MaskSize = 256;

    /// <summary>Rows are duplicated so the shader's random row choice cannot change the result.</summary>
    private const int PaletteWidth = 16;
    private const int PaletteHeight = 4;

    /// <summary>Byte offset of each mask channel within a BGRA pixel: R, G, B, A in channel order.</summary>
    private static readonly int[] ChannelByte = [2, 1, 0, 3];

    /// <summary>
    /// How often a weapon with separate fitting groups gives one of them a contrasting metal — a
    /// gilded pommel over iron furniture, say. Matching fittings are the realistic default, but
    /// always matching would flatten every four-part weapon to three colours.
    /// </summary>
    private const double AccentChance = 0.25;

    /// <summary>
    /// Least share of a weapon's mask area a group may hold and still decide the inventory icon.
    /// Without it a tiny gilded pommel would paint a whole sword's icon gold.
    /// </summary>
    private const double IconMinAreaShare = 0.10;

    /// <summary>
    /// What a part is *for*, which is what decides the materials it may be made of. Declared in
    /// priority order: where two parts share map1 texels they must share a channel, and the group
    /// takes the lowest role present, because a leather-coloured blade is a far louder failure
    /// than a steel-coloured grip.
    /// </summary>
    private enum PartRole { Edge, Fitting, Handle }

    /// <summary>
    /// Which vanilla swatch supplies a material's surface. The swatch carries grain, normal and
    /// roughness while the palette carries only colour, so a leather grip tinted with *metal* grain
    /// still reads as metal. Picking the swatch per channel is what makes wood look like wood.
    /// </summary>
    private enum Swatch { Metal, Leather, Wood }

    private sealed record Material(string Name, byte R, byte G, byte B, Swatch Swatch);

    // --- edge: blades and axe/mace heads ------------------------------------------------------
    private static readonly Material BrightSteel    = new("bright_steel",     252, 252, 250, Swatch.Metal);
    private static readonly Material PolishedSteel  = new("polished_steel",   240, 242, 246, Swatch.Metal);
    private static readonly Material GreySteel      = new("grey_steel",       206, 210, 216, Swatch.Metal);
    private static readonly Material DarkIron       = new("dark_iron",         138, 144, 152, Swatch.Metal);
    private static readonly Material BluedSteel     = new("blued_steel",       112, 128, 170, Swatch.Metal);
    private static readonly Material BrownedSteel   = new("browned_steel",    172, 144, 116, Swatch.Metal);
    private static readonly Material BlackenedSteel = new("blackened_steel",    96,  96, 104, Swatch.Metal);
    private static readonly Material BronzeEdge     = new("bronze_edge",      228, 172, 108, Swatch.Metal);

    // --- fittings: guard, pommel, butt cap ----------------------------------------------------
    private static readonly Material IronFitting    = new("iron",              148, 152, 158, Swatch.Metal);
    private static readonly Material BlackenedIron  = new("blackened_iron",     98,  98, 106, Swatch.Metal);
    private static readonly Material SteelFitting   = new("bright_fitting",   232, 234, 238, Swatch.Metal);
    private static readonly Material Silver         = new("silver",           248, 248, 242, Swatch.Metal);
    private static readonly Material Brass          = new("brass",            246, 208, 118, Swatch.Metal);
    private static readonly Material Gilt           = new("gilt",             255, 224, 132, Swatch.Metal);
    private static readonly Material BronzeFitting  = new("bronze",           220, 164, 100, Swatch.Metal);
    private static readonly Material Copper         = new("copper",           234, 158, 102, Swatch.Metal);
    private static readonly Material Verdigris      = new("verdigris",        160, 204, 180, Swatch.Metal);

    // --- handles: grip wrap and haft ----------------------------------------------------------
    private static readonly Material DarkLeather    = new("dark_leather",      118,  90,  68, Swatch.Leather);
    private static readonly Material TanLeather     = new("tan_leather",      192, 150, 104, Swatch.Leather);
    private static readonly Material OxbloodLeather = new("oxblood_leather",   150,  74,  66, Swatch.Leather);
    private static readonly Material BlackLeather   = new("black_leather",      86,  78,  78, Swatch.Leather);
    private static readonly Material Walnut         = new("walnut",            140, 104,  74, Swatch.Wood);
    private static readonly Material AshWood        = new("ash_wood",         216, 182, 134, Swatch.Wood);
    private static readonly Material Ebony          = new("ebony",              88,  80,  76, Swatch.Wood);
    private static readonly Material Bone           = new("bone",             246, 236, 208, Swatch.Wood);

    /// <summary>
    /// A coherent set of materials, drawn as a unit so a weapon reads as one deliberate object.
    ///
    /// Rolling each part independently is what made the old palette look wrong: a gold blade over a
    /// verdigris grip is four legitimate metals in an arrangement no smith ever built. Choosing the
    /// *finish* first and then drawing each role from it keeps the combinations plausible, and it
    /// makes the variety more legible rather than less — a gilded sword and a blackened one differ
    /// in a way a player can name, where two random four-colour weapons just differ.
    /// </summary>
    /// <param name="Weights">
    /// How often this finish is drawn, one weight per rarity band in <see cref="ArtifactRarity"/>
    /// order.
    ///
    /// This is what makes a forged weapon's band visible with no new art: gilt fittings are the
    /// signal a player already reads as *expensive*, so gilded climbs from 1 to 7 across the bands
    /// while plain falls from 8 to 1. Nothing is ever zero. A plain illustrious weapon is a real
    /// thing — vanilla's own Excalibur is deliberately common rarity — and a band whose column
    /// summed to zero would silently fall through <see cref="PickWeighted"/> to the last row.
    /// </param>
    private sealed record Finish(
        string Name, int[] Weights,
        (Material M, int W)[] Edge,
        (Material M, int W)[] Fitting,
        (Material M, int W)[] Handle)
    {
        public (Material M, int W)[] For(PartRole role) => role switch
        {
            PartRole.Edge => Edge,
            PartRole.Fitting => Fitting,
            _ => Handle,
        };

        public int WeightAt(ArtifactRarity tier) => Weights[(int)tier];
    }

    //                                    common  masterwork  famed  illustrious
    private static readonly Finish[] Finishes =
    [
        new("plain", [8, 4, 1, 1],
            Edge:    [(GreySteel, 5), (PolishedSteel, 3), (DarkIron, 3), (BrownedSteel, 1)],
            Fitting: [(IronFitting, 5), (SteelFitting, 3), (BronzeFitting, 2)],
            Handle:  [(DarkLeather, 4), (Walnut, 4), (TanLeather, 3), (AshWood, 2)]),

        new("fine", [4, 7, 6, 3],
            Edge:    [(PolishedSteel, 5), (BrightSteel, 4), (GreySteel, 2)],
            Fitting: [(SteelFitting, 4), (Silver, 4), (Brass, 2), (IronFitting, 1)],
            Handle:  [(DarkLeather, 4), (OxbloodLeather, 3), (Walnut, 3), (BlackLeather, 2)]),

        new("gilded", [1, 3, 7, 7],
            Edge:    [(BrightSteel, 5), (PolishedSteel, 4)],
            Fitting: [(Gilt, 5), (Brass, 4), (Silver, 2)],
            Handle:  [(OxbloodLeather, 4), (BlackLeather, 3), (Bone, 2), (Ebony, 2)]),

        new("dark", [4, 4, 4, 3],
            Edge:    [(BlackenedSteel, 4), (BluedSteel, 4), (DarkIron, 3)],
            Fitting: [(BlackenedIron, 5), (IronFitting, 3), (Copper, 1)],
            Handle:  [(BlackLeather, 4), (Ebony, 4), (DarkLeather, 3)]),

        new("archaic", [3, 2, 3, 5],
            Edge:    [(BronzeEdge, 5), (DarkIron, 2), (BrownedSteel, 2)],
            Fitting: [(BronzeFitting, 5), (Copper, 3), (Verdigris, 2)],
            Handle:  [(AshWood, 4), (Walnut, 3), (TanLeather, 3), (Bone, 2)]),
    ];

    /// <summary>
    /// Where each swatch lives and how tightly it tiles.
    ///
    /// Metal and leather are **ours**, authored by <c>tools/make_pattern_swatches.py</c> and shipped
    /// in <c>BaseFilesToCopy</c> — they are fixed assets, so there is no reason to rewrite them on
    /// every run. Authoring them buys control of the two channels vanilla's swatches got wrong for a
    /// weapon: <c>gold_plain_01</c> has roughness 0.40 where a polished blade wants 0.20, and
    /// <c>leather_plain_01</c> carries AO 0.75, quietly costing every grip a quarter of its light on
    /// top of an already-dark tint. Wood keeps vanilla's, because a haft genuinely wants grain and a
    /// flat swatch would erase it.
    ///
    /// Scale is swatch tiles per 1.0 of UV2 and the libraries are authored at 20 world units per UV,
    /// so metal's 1.0 repeats every 20 units along a blade and leather's 4.0 every 5. Note this now
    /// only bites on wood: a swatch of one flat colour tiles to itself at any scale.
    /// </summary>
    private static readonly Dictionary<Swatch, (string Dir, string File, string Scale)> Swatches =
        new()
        {
            [Swatch.Metal]   = ("gen",    "gen_steel",        "1.0"),
            [Swatch.Leather] = ("gen",    "gen_leather",      "4.0"),
            [Swatch.Wood]    = ("statue", "wood_plain_01",    "2.0"),
        };

    private static PartRole RoleOf(WeaponPartSlot slot) => slot switch
    {
        WeaponPartSlot.Blade or WeaponPartSlot.Head => PartRole.Edge,
        WeaponPartSlot.Guard or WeaponPartSlot.Pommel or WeaponPartSlot.Cap => PartRole.Fitting,
        _ => PartRole.Handle,
    };

    /// <summary>Parts forced to share one mask channel, and what they are collectively made of.</summary>
    /// <param name="Members">Indices into the weapon's part list, so a caller can map back.</param>
    private sealed record MaskGroup(List<HashSet<int>> Prints, PartRole Role, List<int> Members)
    {
        public int Area => Prints.Sum(p => p.Count);
    }

    /// <summary>
    /// Writes one mask and one palette per weapon plus the variation database, and returns the
    /// handle the asset writer needs. Null when there is nothing to recolour.
    /// </summary>
    /// <param name="fileTag">
    /// Distinguishes this batch's variation file and its shared keys. Both forge paths — the
    /// artifact pool and the FORGE TEST decisions — call this, and a shared filename would mean
    /// whichever ran second silently deleted the other's variations.
    /// </param>
    /// <param name="tiers">
    /// The rarity band each weapon was forged for, keyed by <see cref="ForgedWeapon.Name"/>. It
    /// decides which finish the weapon is likely to draw — see <c>Finish.Weights</c>. A weapon
    /// missing from the map falls back to <see cref="ArtifactRarity.Common"/>, which is the old
    /// behaviour rather than a failure.
    /// </param>
    public static ForgedRecolour? Write(
        string modDir, IReadOnlyList<ForgedWeapon> weapons, Rng rng, string fileTag,
        IReadOnlyDictionary<string, ArtifactRarity> tiers)
    {
        if (weapons.Count == 0) return null;

        // Without a second UV set there is nothing for the swatch to tile over, so patterning would
        // render against absent coordinates. Say so and emit plain weapons instead.
        if (weapons.Any(w => w.Parts.Any(p => !p.HasPatternUv)))
        {
            Console.WriteLine("  forged weapons: parts library has no UV2 - weapons will not be "
                + "recoloured. Re-export with a second UV map to enable colourways.");
            return null;
        }

        string modelDir = Path.Combine(modDir,
            ForgedWeaponWriter.ModelDir.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(modelDir);

        var masks = new Dictionary<string, string>();
        var primary = new Dictionary<string, (byte, byte, byte)>();
        var parts = new Dictionary<string, IReadOnlyList<(byte, byte, byte)>>();
        var entries = new List<(string Weapon, string Finish, List<Material> Materials)>();

        foreach (var w in weapons)
        {
            var groups = GroupByOverlap(w.Parts);
            var tier = tiers.TryGetValue(w.Name, out var t) ? t : ArtifactRarity.Common;
            var (finish, materials) = PickMaterials(groups, tier, rng);

            WriteMask(Path.Combine(modelDir, $"{w.Name}_mask.dds"), groups, w.Parts);
            WritePalette(Path.Combine(modelDir, $"{w.Name}_palette.dds"), materials);

            masks[w.Name] = $"{ForgedWeaponWriter.ModelDir}/{w.Name}_mask.dds";

            if (IconMaterial(groups, materials) is { } icon) primary[w.Name] = (icon.R, icon.G, icon.B);

            var byPart = new (byte, byte, byte)[w.Parts.Count];

            for (int g = 0; g < groups.Count && g < materials.Count; g++)
            {
                foreach (int member in groups[g].Members)
                {
                    if (member < byPart.Length) byPart[member] = (materials[g].R, materials[g].G, materials[g].B);
                }
            }

            parts[w.Name] = byPart;
            entries.Add((w.Name, finish.Name, materials));
        }

        WriteVariations(modDir, entries, fileTag);

        // Distinct materials per weapon is the number worth watching: it collapses to 1 whenever the
        // parts contest too much atlas to be tinted apart, and a run that suddenly reports mostly
        // ones means MergeThreshold or the libraries' UVs have moved.
        var spread = entries.Select(e => e.Materials.Select(m => m.Name).Distinct().Count()).ToList();
        Console.WriteLine($"  forged weapons ({fileTag}): {spread.Count} recoloured, "
            + $"{spread.Average():0.00} materials mean, {spread.Count(c => c == 1)} single-colour");

        return new ForgedRecolour(masks, primary, parts);
    }

    // -------------------------------------------------------------------------------------
    // Grouping
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// Least share of the smaller part's texels two parts must contest before they are forced to
    /// share a channel.
    ///
    /// Merging on *any* shared texel — which is what this did originally — throws away almost every
    /// chance at per-part colour, because one texel in fifteen thousand is enough. Measured across a
    /// seed's 32 weapons the pairwise overlaps are strongly bimodal: 0.3, 0.7, 1.1, 1.2, 1.5, 2.2,
    /// 4.0 ... 28.2, 29.5, 29.9, then a gap to 40.4, 41.8, 43.5 ... 90.0, 100.0.
    ///
    /// **Why this is 5% and not the 33% the gap suggests.** Whatever a split pair contests has to be
    /// given to one channel or the other, and the loser wears its neighbour's colour there. A
    /// threshold in the gap would let a third of a part be decided that way, which showed in game as
    /// blotching on blades. Since <c>portrait_accessory_variation.fxh</c> makes a patterned surface
    /// sample its normal in UV1 and take the pattern's properties, a mismatched patch is a different
    /// *material*, not merely a different colour — so it reads as a splotch rather than as shading.
    /// **This no longer controls holes, only bleed.** Contested texels are handed to a channel
    /// rather than left unpatterned (see <see cref="WriteMask"/>), so raising it cannot reintroduce
    /// the splotching — it only lets more of a part wear its neighbour's colour, which reads as a
    /// two-tone part rather than as a different material. A quarter is the balance point: measured
    /// over a seed, 0.15 left 14 of 32 weapons single-coloured and 0.33 left 5.
    ///
    /// It is measured on *interior* footprints, never the conservative ones the mask is painted
    /// from: those are inflated by a texel along every triangle edge, which makes parts that merely
    /// sit close in UV space read as overlapping and collapsed 17 of 32 weapons to one colour.
    /// </summary>
    private const double MergeThreshold = 0.25;

    /// <summary>
    /// Splits parts into groups that may each take their own colour: parts contesting more than
    /// <see cref="MergeThreshold"/> of a map1 footprint land in the same group, because one mask
    /// texel can only name one channel.
    /// </summary>
    private static List<MaskGroup> GroupByOverlap(IReadOnlyList<WeaponPart> parts)
    {
        var prints = parts.Select(p => Footprint(p)).ToList();
        var parent = Enumerable.Range(0, prints.Count).ToArray();

        int Find(int a)
        {
            while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; }
            return a;
        }

        for (int i = 0; i < prints.Count; i++)
        {
            for (int j = i + 1; j < prints.Count; j++)
            {
                if (SharedFraction(prints[i], prints[j]) < MergeThreshold) continue;
                int a = Find(i), b = Find(j);
                if (a != b) parent[a] = b;
            }
        }

        var byRoot = new Dictionary<int, MaskGroup>();

        for (int i = 0; i < prints.Count; i++)
        {
            int root = Find(i);
            PartRole role = RoleOf(parts[i].Slot);

            // The group takes the *lowest* role any of its parts has, since PartRole is declared in
            // priority order — see its remarks for why the edge wins a tie.
            byRoot[root] = byRoot.TryGetValue(root, out var g)
                ? g with { Role = (PartRole)Math.Min((int)g.Role, (int)role) }
                : new MaskGroup([], role, []);

            byRoot[root].Prints.Add(prints[i]);
            byRoot[root].Members.Add(i);
        }

        // Four parts means at most four groups, which is exactly the number of mask channels.
        // Ordering by role rather than by discovery keeps channel assignment meaningful — the edge
        // always lands in R — and keeps it independent of dictionary enumeration order.
        return [.. byRoot.Values.OrderBy(g => (int)g.Role).ThenByDescending(g => g.Area)];
    }

    /// <summary>
    /// How much of the smaller footprint the two share. Measured against the smaller because that
    /// is the part at risk: a guard overlapped across half its area is badly damaged by losing
    /// those texels, while the blade it grazes barely notices.
    /// </summary>
    private static double SharedFraction(HashSet<int> a, HashSet<int> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0;

        var (small, large) = a.Count <= b.Count ? (a, b) : (b, a);
        int shared = small.Count(large.Contains);

        return (double)shared / small.Count;
    }

    /// <summary>
    /// Texels a part covers in map1, as flat indices into a MaskSize square.
    ///
    /// Coverage is **conservative**: every texel the triangle touches, not only those whose centre
    /// falls inside it. A centre test alone silently drops any triangle smaller than a texel — on
    /// one test sword that was 37% of them — and each dropped triangle becomes an unpatterned speck
    /// on a patterned surface, which the shader renders as a different material rather than as a
    /// slightly different colour. Vertices and edges are walked first so a sub-texel triangle still
    /// claims something; the interior scan then fills anything larger.
    /// </summary>
    /// <param name="conservative">
    /// False for grouping — interior coverage only, which is what honestly answers "do these two
    /// parts share atlas space". True for painting the mask, where every touched texel must be
    /// claimed or it becomes a hole.
    /// </param>
    private static HashSet<int> Footprint(WeaponPart part, bool conservative = false)
    {
        float[] uv = part.Mesh.Floats("u0");
        int[] tri = part.Mesh.Ints("tri");
        var cells = new HashSet<int>();

        void Mark(float u, float v)
        {
            int x = Math.Clamp((int)(Wrap(u) * MaskSize), 0, MaskSize - 1);
            int y = Math.Clamp((int)(Wrap(v) * MaskSize), 0, MaskSize - 1);
            cells.Add(y * MaskSize + x);
        }

        // Walks a UV-space segment in texel-sized steps, so no texel along an edge is skipped.
        void Edge(float u0, float v0, float u1, float v1)
        {
            float du = Wrap(u1) - Wrap(u0), dv = Wrap(v1) - Wrap(v0);
            int steps = (int)(MathF.Max(MathF.Abs(du), MathF.Abs(dv)) * MaskSize) + 1;

            for (int s = 0; s <= steps; s++)
            {
                float f = (float)s / steps;
                Mark(Wrap(u0) + du * f, Wrap(v0) + dv * f);
            }
        }

        if (conservative)
        {
            for (int t = 0; t + 2 < tri.Length; t += 3)
            {
                for (int k = 0; k < 3; k++)
                {
                    int i0 = tri[t + k], i1 = tri[t + (k + 1) % 3];
                    if (2 * i0 + 1 >= uv.Length || 2 * i1 + 1 >= uv.Length) continue;
                    Edge(uv[2 * i0], uv[2 * i0 + 1], uv[2 * i1], uv[2 * i1 + 1]);
                }
            }
        }

        for (int t = 0; t + 2 < tri.Length; t += 3)
        {
            float ax = Wrap(uv[2 * tri[t]]), ay = Wrap(uv[2 * tri[t] + 1]);
            float bx = Wrap(uv[2 * tri[t + 1]]), by = Wrap(uv[2 * tri[t + 1] + 1]);
            float cx = Wrap(uv[2 * tri[t + 2]]), cy = Wrap(uv[2 * tri[t + 2] + 1]);

            float den = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy);
            if (MathF.Abs(den) < 1e-12f) continue;

            int x0 = (int)(MathF.Min(ax, MathF.Min(bx, cx)) * MaskSize);
            int x1 = Math.Min((int)(MathF.Max(ax, MathF.Max(bx, cx)) * MaskSize), MaskSize - 1);
            int y0 = (int)(MathF.Min(ay, MathF.Min(by, cy)) * MaskSize);
            int y1 = Math.Min((int)(MathF.Max(ay, MathF.Max(by, cy)) * MaskSize), MaskSize - 1);

            for (int x = Math.Max(x0, 0); x <= x1; x++)
            {
                float px = (x + 0.5f) / MaskSize;

                for (int y = Math.Max(y0, 0); y <= y1; y++)
                {
                    float py = (y + 0.5f) / MaskSize;
                    float l1 = ((by - cy) * (px - cx) + (cx - bx) * (py - cy)) / den;
                    float l2 = ((cy - ay) * (px - cx) + (ax - cx) * (py - cy)) / den;
                    if (l1 >= -1e-6f && l2 >= -1e-6f && l1 + l2 <= 1 + 1e-6f)
                        cells.Add(y * MaskSize + x);
                }
            }
        }

        return cells;
    }

    private static float Wrap(float v)
    {
        float w = v % 1f;
        return w < 0 ? w + 1f : w;
    }

    /// <summary>
    /// Draws one material per group: a finish for the whole weapon, then one material per *role*
    /// reused across every group that shares it.
    ///
    /// Reusing the material is the point. Two channels may hold the same colour, so a guard and a
    /// pommel in matching brass costs nothing and is what a real weapon looks like — the old code's
    /// draw-without-replacement guaranteed they differed, which is precisely the wrong guarantee.
    ///
    /// <paramref name="tier"/> only reweights the finish draw; it never picks one outright, and it
    /// touches nothing below the finish. A band is meant to shift the odds of a weapon looking
    /// expensive, not to make every illustrious blade gilt and every common one iron — the pool is
    /// small enough that a hard rule would read as two swords rather than a range.
    /// </summary>
    private static (Finish Finish, List<Material> Materials) PickMaterials(
        List<MaskGroup> groups, ArtifactRarity tier, Rng rng)
    {
        var finish = PickWeighted(Finishes, f => f.WeightAt(tier), rng);
        var byRole = new Dictionary<PartRole, Material>();
        var picked = new List<Material>();

        foreach (var g in groups)
        {
            if (byRole.TryGetValue(g.Role, out var m))
            {
                // A second fitting group may take a contrasting metal; edges and handles never do,
                // since one weapon has only ever one blade and one grip to be made of.
                if (g.Role == PartRole.Fitting && rng.Chance(AccentChance))
                {
                    var alts = finish.Fitting.Where(c => c.M.Name != m.Name).ToArray();
                    if (alts.Length > 0) m = PickWeighted(alts, c => c.W, rng).M;
                }
            }
            else
            {
                m = PickWeighted(finish.For(g.Role), c => c.W, rng).M;
                byRole[g.Role] = m;
            }

            picked.Add(m);
        }

        return (finish, picked);
    }

    private static T PickWeighted<T>(IReadOnlyList<T> pool, Func<T, int> weight, Rng rng)
    {
        int total = pool.Sum(weight);
        int roll = rng.Int(1, Math.Max(total, 1));

        foreach (var item in pool)
        {
            roll -= weight(item);
            if (roll <= 0) return item;
        }

        return pool[^1];
    }

    /// <summary>
    /// The colour the inventory icon wears: the most *chromatic* material on the weapon, not the
    /// one covering the most of it.
    ///
    /// Realistic edges are overwhelmingly steel grey, so keeping the old widest-group rule would
    /// have made every icon the same grey smear at the 30-60 pixels these are drawn at. The fitting
    /// metal is both what distinguishes one weapon from another and — per the icon writer's own
    /// note that the hilt carries the identity — the honest thing to show. Groups below
    /// <see cref="IconMinAreaShare"/> are ineligible so a tiny pommel cannot speak for the weapon,
    /// and an all-grey weapon falls back to plain area.
    /// </summary>
    private static Material? IconMaterial(List<MaskGroup> groups, List<Material> materials)
    {
        int n = Math.Min(groups.Count, materials.Count);
        if (n == 0) return null;

        long total = groups.Take(n).Sum(g => (long)g.Area);
        int best = -1, widest = 0;

        for (int i = 0; i < n; i++)
        {
            if (groups[i].Area > groups[widest].Area) widest = i;
            if (total > 0 && groups[i].Area < total * IconMinAreaShare) continue;

            if (best < 0 || Chroma(materials[i]) > Chroma(materials[best])
                || (Chroma(materials[i]) == Chroma(materials[best])
                    && groups[i].Area > groups[best].Area))
            {
                best = i;
            }
        }

        return materials[best < 0 ? widest : best];
    }

    private static int Chroma(Material m)
        => Math.Max(m.R, Math.Max(m.G, m.B)) - Math.Min(m.R, Math.Min(m.G, m.B));

    // -------------------------------------------------------------------------------------
    // Textures
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// Paints each group's map1 footprint into its own channel.
    ///
    /// Texels no part covers stay zero, which the shader reads as "no pattern here" and leaves the
    /// original diffuse showing — a safe outcome for the thin gaps between UV islands, and the
    /// reason this needs no dilation pass.
    /// </summary>
    private static void WriteMask(string path, List<MaskGroup> groups, IReadOnlyList<WeaponPart> parts)
    {
        var bgra = new byte[MaskSize * MaskSize * 4];
        int n = Math.Min(groups.Count, ChannelByte.Length);

        // Painted from conservative coverage, not from the interior footprints the grouping was
        // decided on: a texel a part touches but does not cover at its centre still has to be
        // claimed, or it renders as unpatterned and reads as a splotch.
        var paint = new List<HashSet<int>>();

        for (int g = 0; g < n; g++)
        {
            var union = new HashSet<int>();

            foreach (int member in groups[g].Members)
                if (member < parts.Count) union.UnionWith(Footprint(parts[member], conservative: true));

            paint.Add(union);
        }

        // One owner per texel, by group order — which is role order, so the edge wins a contest.
        //
        // Contested texels used to be left at zero on the reasoning that "no pattern" shows the
        // part's original diffuse and is therefore safe. It is not: a patterned surface samples its
        // normal in UV1 and wears the pattern's properties, so an unpatterned patch differs in
        // *material*, and the boundary reads as a splotch. Giving the texel away costs at most the
        // 5% of a part that MergeThreshold allows to be contested, and costs it in colour only.
        var owner = new int[MaskSize * MaskSize];
        Array.Fill(owner, -1);

        for (int g = 0; g < n; g++)
            foreach (int cell in paint[g])
                if (owner[cell] < 0) owner[cell] = g;

        // The sampler is Linear (PatternMask in portrait_accessory_variation.fxh), so a texel on the
        // boundary blends with its neighbours. Growing each channel by one texel into unclaimed
        // space keeps that blend between two owned texels instead of fading toward zero, which
        // would otherwise draw a thin unpatterned seam around every UV island.
        var grown = (int[])owner.Clone();

        for (int y = 0; y < MaskSize; y++)
        {
            for (int x = 0; x < MaskSize; x++)
            {
                int i = y * MaskSize + x;
                if (owner[i] >= 0) continue;

                for (int d = 0; d < 4 && grown[i] < 0; d++)
                {
                    int nx = x + (d == 0 ? -1 : d == 1 ? 1 : 0);
                    int ny = y + (d == 2 ? -1 : d == 3 ? 1 : 0);
                    if (nx < 0 || ny < 0 || nx >= MaskSize || ny >= MaskSize) continue;
                    if (owner[ny * MaskSize + nx] >= 0) grown[i] = owner[ny * MaskSize + nx];
                }
            }
        }

        for (int i = 0; i < grown.Length; i++)
            if (grown[i] >= 0) bgra[i * 4 + ChannelByte[grown[i]]] = 255;

        DdsWriter.WriteBgra(path, MaskSize, MaskSize, bgra);
    }

    /// <summary>
    /// One colour per mask channel, written across that channel's **block of four columns**.
    ///
    /// The indexing is not one column per channel, which is the natural guess and was wrong here for
    /// a long time. <c>portrait_accessory_variation.fxh</c> computes
    /// <c>HorizontalSample = MaskIndex * 4 + i</c> over a 16-wide palette, where <c>MaskIndex</c> is
    /// our mask channel and <c>i</c> is the channel of the *swatch's own* colormask. So channel 0
    /// reads columns 0-3, channel 1 reads 4-7, channel 2 reads 8-11 and channel 3 reads 12-15.
    ///
    /// Writing colours at columns 0,1,2,3 therefore tinted only the first group. Channels 1-3 landed
    /// on white columns and kept their original diffuse — while still being flattened, because a
    /// non-zero mask swaps the surface's normal and properties whatever colour comes back. The
    /// symptom was an icon showing a black grip over a model still wearing its original gold.
    ///
    /// All four columns of a block get the same colour rather than only the one the current swatches
    /// use. Every swatch here fires colormask R alone, so column <c>g*4</c> is the one that is read,
    /// but filling the block costs nothing and keeps this correct if a later swatch uses G or B.
    /// Blocks with no group stay white so an unused channel cannot darken anything.
    /// </summary>
    private static void WritePalette(string path, List<Material> colours)
    {
        var bgra = new byte[PaletteWidth * PaletteHeight * 4];

        for (int y = 0; y < PaletteHeight; y++)
        {
            for (int x = 0; x < PaletteWidth; x++)
            {
                int i = (y * PaletteWidth + x) * 4;
                int block = x / 4;
                bool tinted = block < colours.Count;

                bgra[i + 0] = tinted ? colours[block].B : (byte)255;
                bgra[i + 1] = tinted ? colours[block].G : (byte)255;
                bgra[i + 2] = tinted ? colours[block].R : (byte)255;
                bgra[i + 3] = 255;
            }
        }

        DdsWriter.WriteBgra(path, PaletteWidth, PaletteHeight, bgra);
    }

    private static string TextureName(Swatch s, string fileTag)
        => $"gen_weapon_{s.ToString().ToLowerInvariant()}_{fileTag}";

    private static string LayoutName(Swatch s, string fileTag)
        => $"gen_weapon_{s.ToString().ToLowerInvariant()}_layout_{fileTag}";

    private static void WriteVariations(
        string modDir, List<(string Weapon, string Finish, List<Material> Materials)> entries,
        string fileTag)
    {
        string dir = Path.Combine(modDir, "gfx", "portraits", "accessory_variations");
        Directory.CreateDirectory(dir);

        var b = new JominiBuilder();
        b.Comment("Colourways for procedurally forged weapons.\n"
            + "The swatch supplies surface structure - grain, normal, roughness - and the palette\n"
            + "supplies the colour. Each weapon names its own mask and palette, so its parts are\n"
            + "tinted independently wherever their atlas UVs allow it.\n"
            + "\n"
            + "Each channel names its own swatch, so a grip can carry leather or wood grain while\n"
            + "the blade beside it carries metal. Colour alone could not do that: a brown tint over\n"
            + "metal grain still reads as painted metal.");

        // Metal and leather point at our own swatches under patterns/gen, shipped via
        // BaseFilesToCopy; wood still points at vanilla's. Either way the pattern_textures *name* is
        // ours (gen_weapon_*), because reusing a vanilla name would collide with vanilla's own
        // declaration of it — the files are resolved by path here and globally by filename in game.
        foreach (Swatch s in Enum.GetValues<Swatch>())
        {
            var (subDir, file, scale) = Swatches[s];

            b.Blank();

            using (b.Block("pattern_textures"))
            {
                b.Quoted("name", TextureName(s, fileTag));
                b.Quoted("colormask", $"{SwatchDir}/{subDir}/{file}_masks.dds");
                b.Quoted("normal", $"{SwatchDir}/{subDir}/{file}_normal.dds");
                b.Quoted("properties", $"{SwatchDir}/{subDir}/{file}_properties.dds");
            }

            b.Blank();

            // scale is swatch tiles per 1.0 of UV2, and the parts libraries are authored at a
            // measured 20.0 world units per UV - so metal's 1.0 repeats every 20 units along a
            // blade, and leather's 4.0 every 5, which is about one wrap turn on a grip.
            using (b.Block("pattern_layout"))
            {
                b.Quoted("name", LayoutName(s, fileTag));
                b.Inline("scale", "min", "=", scale, "max", "=", scale);
                b.Inline("rotation", "min", "=", "0", "max", "=", "0");
                b.Inline("offset", "x", "=", "{", "min", "=", "0", "max", "=", "0", "}",
                                   "y", "=", "{", "min", "=", "0", "max", "=", "0", "}");
            }
        }

        string[] channels = ["r", "g", "b", "a"];

        foreach (var (weapon, finish, materials) in entries)
        {
            b.Blank();
            b.Comment($"{weapon}: {finish} - {string.Join(" / ", materials.Select(m => m.Name))}");

            using (b.Block("variation"))
            {
                b.Quoted("name", $"{weapon}_variation");

                using (b.Block("pattern"))
                {
                    b.Field("weight", 1);

                    // Every channel names a swatch, whether or not this weapon uses it: a channel
                    // the mask never selects costs nothing, and one the mask *does* select without a
                    // swatch would render untextured.
                    for (int i = 0; i < channels.Length; i++)
                    {
                        Swatch s = i < materials.Count ? materials[i].Swatch : Swatch.Metal;

                        b.Inline(channels[i],
                            "textures", "=", $"\"{TextureName(s, fileTag)}\"",
                            "layout", "=", $"\"{LayoutName(s, fileTag)}\"");
                    }
                }

                using (b.Block("color_palette"))
                {
                    b.Field("weight", 1);
                    b.Quoted("texture", $"{ForgedWeaponWriter.ModelDir}/{weapon}_palette.dds");
                }
            }
        }

        ParadoxText.WriteBom(Path.Combine(dir, $"00_gen_forged_{fileTag}_variations.txt"), b.ToString());
    }
}
