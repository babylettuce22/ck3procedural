namespace Ck3MapGen.MapGen;

using Ck3MapGen.Io;
using System.IO;

/// <summary>
/// One material batch of a forged weapon, in the order its <c>mesh</c> node appears in the file.
///
/// The order is the contract with the emitted <c>.asset</c>: <c>meshsettings</c> blocks address
/// their material by <c>index</c>, so batch N here must be written as index N there.
/// </summary>
public sealed record ForgedMaterial(string Diffuse, string Normal, string Specular);

/// <summary>A finished weapon: the mesh tree to write, and the materials its asset must declare.</summary>
/// <param name="Name">Base name — the mesh file, the pdxmesh and the entity all derive from it.</param>
/// <param name="ShapeName">Shape node inside the mesh, which <c>meshsettings.name</c> must match.</param>
/// <param name="Parts">
/// The parts this weapon was built from, kept so the recolour step can read their map1 (UV0)
/// footprints — the space the pattern mask is sampled in.
/// Placement shifts geometry only, never UVs, so these are the atlas coordinates the finished mesh
/// uses.
/// </param>
public sealed record ForgedWeapon(
    string Name, string ShapeName, PdxNode Root, IReadOnlyList<ForgedMaterial> Materials,
    IReadOnlyList<WeaponPart> Parts);

/// <summary>
/// A shareable lump of geometry — one <c>.mesh</c> file and the materials its <c>.asset</c> must
/// declare. Either a whole base assembly or a single lead part.
/// </summary>
public sealed record WeaponPiece(
    string Name, string ShapeName, PdxNode Root, IReadOnlyList<ForgedMaterial> Materials);

/// <summary>
/// Everything a weapon has except its lead — the hilt of a sword, the haft of an axe — merged into
/// one shareable mesh, plus where the lead attaches to it.
/// </summary>
/// <param name="LeadLocator">
/// Where the lead hangs, in this base's own space. It depends only on the base's own geometry: the
/// lead mounts on a part of this same family, so the socket and that part's placement are both
/// fixed here. That is what lets one base serve every lead.
/// </param>
/// <param name="LeadMountable">
/// False when the join to the lead is missing a socket on one side, which makes the locator a guess.
/// Reported rather than emitted, because the result is a blade floating beside its hilt.
/// </param>
/// <param name="Parts">
/// The parts merged into <paramref name="Piece"/>, kept because the recolour reads their map1 (UV0)
/// footprints to work out which of them can be coloured independently. Placement shifts geometry
/// only, never UVs, so these are the atlas coordinates the merged base actually uses.
/// </param>
public sealed record WeaponBase(
    WeaponPiece Piece, float[] LeadLocator, bool LeadMountable, IReadOnlyList<WeaponPart> Parts);

/// <summary>
/// One pairing: which base, which lead, and nothing else. Both pieces are shared, so a pairing costs
/// only the text that names them.
///
/// **The base is the root entity and the lead is attached to it**, which is the opposite of what the
/// art would prefer and is forced by two measured facts. An attached child receives no
/// <c>portrait_accessory</c> binding — not its own and not its parent's — so only the root can carry
/// a procedural palette. And only the anchor's mesh is the same file in every combination, because
/// <see cref="WeaponForge.Place"/> gives the anchor a shift of exactly zero. The anchor is the held
/// part, so the held part is the root, so the fittings are what get recoloured.
///
/// Anchoring on the lead instead was measured and rejected: it slides the weapon along the hand by
/// however much the hilts differ, which costs a quarter of the pairings on swords and nearly two
/// thirds on hafted weapons, where haft lengths vary so much that a pinned spear head swings the
/// grip 148 units. <c>--verify-compose</c> prints the table.
/// </summary>
public sealed record ComposedWeapon(string Name, WeaponBase Base, WeaponPiece Lead);

/// <summary>
/// Where a part sits on its weapon. Not every kind uses every slot — see <see cref="WeaponSchema"/>.
/// </summary>
public enum WeaponPartSlot
{
    // bladed: sword, dagger
    Blade,
    Guard,
    Grip,
    Pommel,

    // hafted: axe
    Head,
    Haft,
    Cap,
}

/// <summary>
/// Which slots a weapon kind has, which one anchors it, and what mounts on what.
///
/// Weapon kinds are not all four-slot stacks. An axe is head + haft with an optional butt cap, and
/// the haft plays the grip's role: it is what the hand holds and what everything else is placed
/// against. Expressing that as data rather than as branches keeps <see cref="WeaponForge.Place"/>
/// a chain walk, and means a new kind is a table entry rather than new code.
/// </summary>
/// <param name="Anchor">Held part. Stays where it is; every other part moves onto it.</param>
/// <param name="Lead">
/// The business end — the part that gives the weapon its character, and the one a mixed recipe
/// takes from a *second* family. Named explicitly rather than inferred from the chain: deriving it
/// as "the last link" happened to be right for a sword (blade) and was wrong for an axe, where the
/// chain ends on the optional cap, so every mixed axe silently took its head from the base family
/// and the two test axes came out identical.
/// </param>
/// <param name="Optional">
/// Slots a family may legitimately lack. Half the vanilla axes have no butt cap — the haft simply
/// ends — so requiring one would throw away four of eight families for something vanilla never
/// modelled. Distinct from a *degenerate* part, which should be excluded when the parts are cut.
/// </param>
/// <param name="Chain">
/// Mount order, walked front to back. A part is placed against its mount's **final** position, so a
/// mount must appear earlier in the chain than anything hanging off it.
/// </param>
public sealed record WeaponSchema(
    string Kind,
    WeaponPartSlot Anchor,
    WeaponPartSlot Lead,
    IReadOnlyList<WeaponPartSlot> Required,
    IReadOnlyList<WeaponPartSlot> Optional,
    IReadOnlyList<(WeaponPartSlot Part, WeaponPartSlot MountsOn)> Chain)
{
    /// <summary>Sword and dagger: a blade through a guard, on a grip, capped by a pommel.</summary>
    public static readonly WeaponSchema Bladed = new(
        "bladed", WeaponPartSlot.Grip, WeaponPartSlot.Blade,
        [WeaponPartSlot.Blade, WeaponPartSlot.Guard, WeaponPartSlot.Grip, WeaponPartSlot.Pommel],
        [],
        [
            (WeaponPartSlot.Guard, WeaponPartSlot.Grip),
            (WeaponPartSlot.Pommel, WeaponPartSlot.Grip),
            // after the guard, because the tang's depth is measured into the guard's placed position
            (WeaponPartSlot.Blade, WeaponPartSlot.Guard),
        ]);

    /// <summary>Axe: a head on a haft, optionally capped.</summary>
    public static readonly WeaponSchema Hafted = new(
        "hafted", WeaponPartSlot.Haft, WeaponPartSlot.Head,
        [WeaponPartSlot.Head, WeaponPartSlot.Haft],
        [WeaponPartSlot.Cap],
        [
            (WeaponPartSlot.Head, WeaponPartSlot.Haft),
            (WeaponPartSlot.Cap, WeaponPartSlot.Haft),
        ]);

    /// <summary>
    /// Note the default: an unrecognised kind gets <see cref="Bladed"/>, which is silent and wrong
    /// for anything on a shaft. A spear listed here as hafted is the difference between forging and
    /// failing outright, because a spear library has no grip for the bladed schema to anchor on.
    /// </summary>
    public static WeaponSchema For(string kind) => kind switch
    {
        "axe" or "mace" or "hammer" or "spear" => Hafted,
        _ => Bladed,
    };

    public IEnumerable<WeaponPartSlot> AllSlots => Required.Concat(Optional);

    /// <summary>What <paramref name="slot"/> mounts onto, or null if it is the anchor.</summary>
    public WeaponPartSlot? MountOf(WeaponPartSlot slot)
    {
        foreach (var (part, mountsOn) in Chain)
            if (part == slot) return mountsOn;

        return null;
    }
}

/// <summary>
/// One harvested component of a real weapon: its geometry, its material, and the bounds it
/// occupied on the weapon it came from.
/// </summary>
/// <param name="Name">Shape name in the parts file, e.g. <c>..._LOD0_bladeShape</c>.</param>
/// <param name="Family">Source weapon it was cut from — parts of one family always fit together.</param>
/// <param name="Slot">Which of the four positions it fills.</param>
/// <param name="Mesh">The <c>mesh</c> node, carrying p / n / ta / u0 / tri and a material child.</param>
public sealed record WeaponPart(string Name, string Family, WeaponPartSlot Slot, PdxNode Mesh)
{
    /// <summary>
    /// Authored join points, keyed by the slot they mate with: <c>Sockets[Guard]</c> on a grip is
    /// where that grip meets a guard. Positions are file space, the same space as <c>p</c>.
    ///
    /// These come from locator empties in the parts file and supersede the bounding-box rules
    /// below wherever both sides of a join have one. A bbox is only ever a guess at the join
    /// surface: it is thrown off by a decorative spike, a swept guard, or a curved blade, and on the
    /// katana it put the join 2.27 units off-axis because the blade's bbox centre is nowhere near
    /// its tang. A socket is the join, placed by hand, and it carries X and Y as well — so aligning
    /// on it also fixes the sideways offset that the axis-only bbox rule could not see.
    /// </summary>
    public IReadOnlyDictionary<WeaponPartSlot, float[]> Sockets { get; init; }
        = new Dictionary<WeaponPartSlot, float[]>();

    /// <summary>Extent along the blade axis, taken from the part's own aabb.</summary>
    public (float Lo, float Hi) Span
    {
        get
        {
            var aabb = Mesh.Child("aabb");
            float[] mn = aabb.Floats("min"), mx = aabb.Floats("max");
            return mn.Length == 3 && mx.Length == 3
                ? (mn[WeaponForge.AxisIndex], mx[WeaponForge.AxisIndex])
                : (0f, 0f);
        }
    }

    /// <summary>The part's material node, or null. Never <c>Child()</c> — that *creates* a missing
    /// node, which would quietly mutate the tree from inside a property getter.</summary>
    private PdxNode? Material => Mesh.Children.FirstOrDefault(c => c.Name == "material");

    /// <summary>Diffuse texture, which is what makes two parts belong to the same material batch.</summary>
    public string Diffuse => Material?.Prop("diff")?.Text ?? "";

    /// <summary>
    /// Whether this part names all three textures the emitted <c>.asset</c> has to declare.
    ///
    /// Not a given: a <c>.mesh</c> may carry no texture names at all (the source <c>.asset</c> is
    /// the authoritative place, and vanilla does not always duplicate them into the mesh), or carry
    /// names whose files the exporter could not find beside the mesh and so wrote as empty strings.
    /// Either way the forged weapon renders untextured, which on a portrait reads as an invisible
    /// weapon punching a hole through the character — so this is checked before forging rather than
    /// discovered in-game.
    /// </summary>
    /// <summary>
    /// Whether this part carries the second UV set that material patterns tile over.
    ///
    /// Checked before recolouring rather than assumed: a parts library cut before UV2 existed still
    /// assembles perfectly, and patterning it would sample a UV stream that is not there — a silent
    /// visual fault rather than a missing feature.
    /// </summary>
    public bool HasPatternUv => Mesh.Floats("u1").Length == Mesh.Floats("u0").Length
                                && Mesh.Floats("u1").Length > 0;

    public bool HasTextures =>
        Material is { } m
        && !string.IsNullOrEmpty(m.Prop("diff")?.Text)
        && !string.IsNullOrEmpty(m.Prop("n")?.Text)
        && !string.IsNullOrEmpty(m.Prop("spec")?.Text);
}

/// <summary>
/// Builds complete weapons out of harvested parts, entirely in code.
///
/// **Why this can exist.** A <c>.mesh</c> is a plain hierarchical binary container
/// (<see cref="PdxMesh"/>), and a weapon is four rigid, unskinned lumps stacked along one axis.
/// So assembly is arithmetic on float arrays, not modelling: translate each part along the blade
/// axis, concatenate the vertex streams, offset the triangle indices, recompute the bounds. No
/// Blender at generation time — Blender is only ever used offline to *cut* the parts.
///
/// **The axis.** In file space the blade runs along **Z**, with the tip at negative Z and the
/// pommel at positive Z. (Blender shows this as Y because io_pdx_mesh swaps coordinate space on
/// import; work in file space here and no swap is needed.)
///
/// **How parts align.** Measured on the two source swords, the joins deliberately overlap — the
/// blade's tang runs *through* the guard rather than butting against it, and the pommel sinks a
/// little into the grip. Preserving those overlaps is what stops assembled weapons showing seams,
/// so alignment is expressed relative to the grip rather than as a butt-joint:
///
/// <code>
///   want:  part.hi_new = grip.lo_new + (part.hi_src - grip.lo_src)
///   hence: dz         = grip.lo_new - grip.lo_src
/// </code>
///
/// The translation for a guard or blade is simply the difference between the chosen grip's lower
/// end and the lower end of the grip *that part was cut from*; a pommel uses the upper ends. Every
/// number comes from measured geometry, so no socket offsets have to be hand-authored, and a part
/// recombined with its own family lands exactly where it started.
/// </summary>
public static class WeaponForge
{
    /// <summary>Index of the blade axis within a 3-float vector. Z in file space.</summary>
    public const int AxisIndex = 2;

    // -------------------------------------------------------------------------------------
    // Loading a parts library
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// Reads a parts file produced by cutting real weapons up in Blender and exporting the pieces.
    ///
    /// Classification is by shape-name suffix (<c>_bladeShape</c>, <c>_gripShape</c>, ...), and the
    /// family is whatever precedes that suffix — so the naming used when cutting the parts is the
    /// contract. A shape whose name matches no slot is ignored rather than guessed at.
    /// </summary>
    public static List<WeaponPart> LoadParts(string meshPath, WeaponSchema schema)
    {
        var root = PdxMesh.Read(meshPath);
        var sockets = ReadSockets(root);
        var parts = new List<WeaponPart>();

        foreach (var shape in root.Child("object").Children)
        {
            WeaponPartSlot? slot = SlotOf(shape.Name);
            if (slot is null) continue;

            // One mesh node per material; a harvested part is a single lump, so take the first.
            PdxNode? mesh = shape.Children.FirstOrDefault(c => c.Name == "mesh");
            if (mesh is null) continue;

            string family = FamilyOf(shape.Name, slot.Value);

            parts.Add(new WeaponPart(shape.Name, family, slot.Value, mesh)
            {
                Sockets = sockets.GetValueOrDefault((family, slot.Value))
                    ?? new Dictionary<WeaponPartSlot, float[]>(),
            });
        }

        // Placement is socket-driven, so a part with none cannot be positioned at all — it is exactly
        // as useless as a part that was never cut. It is therefore dropped and named, rather than
        // thrown on.
        //
        // This used to throw. That was right when it replaced a *silent* fallback to bounding-box
        // placement, because guessing a join quietly is worse than failing loudly. Dropping is
        // neither silent nor a guess: the part is named along with the fix. And the cost of throwing
        // has risen now that partial families are legitimate — one part missing its locators takes
        // down an entire world generation, which is a harsh price for a library that is otherwise
        // completely fine.
        var unsocketed = parts.Where(p => p.Sockets.Count == 0).Select(p => p.Name).ToList();

        if (unsocketed.Count > 0)
        {
            Console.WriteLine($"  forged weapons: {Path.GetFileName(meshPath)} — dropped "
                + $"{unsocketed.Count} part(s) with no sockets: {string.Join(", ", unsocketed)}. "
                + "Export with chk_locs = True and locators named <family>_<slot>_socket_<target>.");

            parts.RemoveAll(p => p.Sockets.Count == 0);
        }

        // The anchor check belongs to the *library*, not to each family.
        //
        // It used to reject any family without one, reasoning that every other part is placed
        // against the anchor. That holds for a family supplying a weapon's body and is false for one
        // supplying only the lead: a blade cut from a weapon whose hilt was not worth keeping is
        // positioned against somebody else's grip and never needs a grip of its own. Rejecting it
        // threw away a legitimate donor — and threw the whole run with it, because this is a throw.
        //
        // A library where *nothing* carries the anchor is still a broken export: it can assemble no
        // weapon at all, and failing loudly beats forging nothing and not saying why.
        var byFamily = parts.GroupBy(p => p.Family).ToList();
        string anchorName = schema.Anchor.ToString().ToLowerInvariant();

        if (!parts.Any(p => p.Slot == schema.Anchor))
        {
            throw new InvalidDataException(
                $"{Path.GetFileName(meshPath)}: no {anchorName} anywhere in the library. At least "
                + $"one family needs a shape named <family>_{anchorName}Shape — it is the part the "
                + "hand holds, and every other part is positioned against it, so without one nothing "
                + "can be assembled.");
        }

        // Named rather than dropped in silence. A family that can neither supply the lead nor form a
        // complete body contributes to no weapon, and the only symptom would be a slightly thinner
        // pool — which is exactly the kind of dead weight that is impossible to trace back to a cut.
        var bodySlots = schema.Required.Where(s => s != schema.Lead).ToList();

        var inert = byFamily
            .Where(g => !g.Any(p => p.Slot == schema.Lead) && !bodySlots.All(s => g.Any(p => p.Slot == s)))
            .Select(g => g.Key)
            .ToList();

        if (inert.Count > 0)
        {
            Console.WriteLine($"  forged weapons: {Path.GetFileName(meshPath)} — "
                + $"{string.Join(", ", inert)} contribute(s) nothing: no "
                + $"{schema.Lead.ToString().ToLowerInvariant()} to donate, and not a complete body "
                + $"({string.Join(" + ", bodySlots.Select(s => s.ToString().ToLowerInvariant()))}).");
        }

        return parts;
    }

    /// <summary>
    /// Reads the file's <c>locator</c> section into join points, keyed by (family, slot).
    ///
    /// Locators are named <c>&lt;family&gt;_&lt;slot&gt;_socket_&lt;target&gt;</c> and sit in one
    /// flat namespace, so the name is the only thing tying a socket to its part — parenting does
    /// not survive export, and neither does local space: a locator's <c>p</c> is always baked world
    /// position, the same space the geometry is written in. A locator whose name does not parse is
    /// ignored rather than guessed at.
    /// </summary>
    private static Dictionary<(string Family, WeaponPartSlot Slot), Dictionary<WeaponPartSlot, float[]>>
        ReadSockets(PdxNode root)
    {
        var byPart = new Dictionary<(string, WeaponPartSlot), Dictionary<WeaponPartSlot, float[]>>();

        foreach (var loc in root.Children.FirstOrDefault(c => c.Name == "locator")?.Children ?? [])
        {
            int marker = loc.Name.LastIndexOf("_socket_", StringComparison.Ordinal);
            if (marker < 0) continue;

            string owner = loc.Name[..marker];
            if (!Enum.TryParse(loc.Name[(marker + "_socket_".Length)..], true, out WeaponPartSlot target))
                continue;

            WeaponPartSlot? ownerSlot = null;
            foreach (WeaponPartSlot s in Enum.GetValues<WeaponPartSlot>())
            {
                if (!owner.EndsWith($"_{s.ToString().ToLowerInvariant()}", StringComparison.OrdinalIgnoreCase))
                    continue;

                ownerSlot = s;
                break;
            }

            if (ownerSlot is null) continue;

            float[] p = loc.Floats("p");
            if (p.Length != 3) continue;

            string family = owner[..^(ownerSlot.Value.ToString().Length + 1)];
            var key = (family, ownerSlot.Value);

            if (!byPart.TryGetValue(key, out var map))
            {
                map = [];
                byPart[key] = map;
            }

            map[target] = p;
        }

        return byPart;
    }

    private static WeaponPartSlot? SlotOf(string shapeName)
    {
        foreach (WeaponPartSlot s in Enum.GetValues<WeaponPartSlot>())
        {
            if (shapeName.EndsWith($"_{s.ToString().ToLowerInvariant()}Shape", StringComparison.OrdinalIgnoreCase))
                return s;
        }

        return null;
    }

    private static string FamilyOf(string shapeName, WeaponPartSlot slot) =>
        shapeName[..^($"_{slot.ToString().ToLowerInvariant()}Shape".Length)];

    // -------------------------------------------------------------------------------------
    // Assembly
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// Stacks four parts into one weapon and returns a <c>File</c> root ready for
    /// <see cref="PdxMesh.Write"/>.
    ///
    /// Parts sharing a diffuse texture are merged into one <c>mesh</c> node; parts from different
    /// source weapons keep their own. That is how a mixed weapon stays renderable — a shape may
    /// hold several mesh nodes, exactly as vanilla's multi-material meshes do — but it also means
    /// the emitted <c>.asset</c> needs one <c>meshsettings</c> block per material, in this order,
    /// which is why the returned <see cref="ForgedWeapon"/> carries the material list.
    ///
    /// Textures keep whatever bare filenames the source parts used, and a mixed weapon therefore
    /// names textures from two different vanilla folders. That resolves: the engine looks textures
    /// up by filename globally, not relative to the asset — vanilla's own
    /// <c>tgp_japanese_dagger_01_a.asset</c> sits in <c>artifacts/ep4/…</c> and happily references
    /// <c>ep1_western_weaponrack_01_diffuse.dds</c> from <c>artifacts/objects/</c>. So nothing has
    /// to be copied alongside the generated mesh.
    /// </summary>
    public static ForgedWeapon Assemble(
        string name, IReadOnlyList<WeaponPart> parts, WeaponSchema schema)
    {
        if (parts.Count == 0) throw new ArgumentException("No parts supplied", nameof(parts));

        var anchor = parts.FirstOrDefault(p => p.Slot == schema.Anchor)
            ?? throw new ArgumentException(
                $"A {schema.Kind} weapon needs a {schema.Anchor}: it is the part the hand "
                + "holds and the anchor every other part is placed against.", nameof(parts));

        var piece = BuildPiece(name, parts, Place(parts, anchor, schema));

        return new ForgedWeapon(
            piece.Name, piece.ShapeName, piece.Root, piece.Materials, [.. parts]);
    }
    /// <summary>
    /// The base assembly — everything but the lead — merged into one shareable mesh.
    ///
    /// **Why this is the root.** Two measured facts pin it. An attached child receives no
    /// <c>portrait_accessory</c> binding of its own and none from its parent, so only the root can
    /// carry a procedural palette. And only the anchor's mesh is the same file in every combination,
    /// because <see cref="Place"/> gives the anchor a shift of exactly zero. The anchor is the part
    /// the hand holds, so the held assembly is the root, and the fittings are what get recoloured.
    ///
    /// Everything here depends on this family alone. The guard and pommel mount on the grip, which
    /// is the anchor and never moves, and the lead mounts on a part of this same family — so the
    /// lead's locator is fixed by the base too. One base mesh serves every lead.
    /// </summary>
    public static WeaponBase BuildBase(
        string name, IReadOnlyList<WeaponPart> parts, WeaponSchema schema)
    {
        var baseParts = parts.Where(p => p.Slot != schema.Lead).ToList();

        if (baseParts.Count == 0)
            throw new ArgumentException("No base parts supplied", nameof(parts));

        var anchor = baseParts.FirstOrDefault(p => p.Slot == schema.Anchor)
            ?? throw new ArgumentException(
                $"A {schema.Kind} base needs a {schema.Anchor}: it is the part the hand holds and "
                + "the anchor every other part is placed against.", nameof(parts));

        var shifts = Place(baseParts, anchor, schema);

        // Where the lead will hang. Its mount is a base part, so both the socket and that part's
        // placement are known here without ever seeing the lead.
        var mountSlot = schema.MountOf(schema.Lead);
        var mount = mountSlot is { } ms ? baseParts.FirstOrDefault(p => p.Slot == ms) : null;

        bool mountable = mount is not null && mount.Sockets.ContainsKey(schema.Lead);
        float[] locator = new float[3];

        if (mountable)
        {
            float[] socket = mount!.Sockets[schema.Lead];
            float[] shift = shifts[mount];
            locator = [socket[0] + shift[0], socket[1] + shift[1], socket[2] + shift[2]];
        }

        return new WeaponBase(
            BuildPiece(name, baseParts, shifts), locator, mountable, baseParts);
    }

    /// <summary>
    /// The lead on its own, moved so the socket it mates on sits at the origin.
    ///
    /// That normalisation is what makes the file shareable: everything about a pairing has moved
    /// into the base's locator, so this same blade mesh serves every hilt. The arithmetic is
    /// <see cref="Mate"/>'s read backwards — a part's shift is
    /// <c>mount.socket + mount.shift - own.socket</c>, so subtracting <c>own.socket</c> from the
    /// geometry and letting the locator supply <c>mount.socket + mount.shift</c> moves the same
    /// total. <c>--verify-compose</c> checks that numerically against <see cref="Assemble"/> for
    /// every pairing the libraries can make.
    ///
    /// Returns null when the lead carries no socket toward its mount, which is the one case where
    /// the geometry cannot be shared and the caller must fall back rather than emit a guess.
    /// </summary>
    public static WeaponPiece? BuildLead(string name, WeaponPart lead, WeaponSchema schema)
    {
        var mountSlot = schema.MountOf(lead.Slot);

        if (mountSlot is not { } ms || !lead.Sockets.TryGetValue(ms, out float[]? socket))
            return null;

        var shifts = new Dictionary<WeaponPart, float[]>
        {
            [lead] = [-socket[0], -socket[1], -socket[2]],
        };

        return BuildPiece(name, [lead], shifts);
    }

    /// <summary>
    /// Merges parts into one mesh tree, one <c>mesh</c> node per distinct diffuse.
    ///
    /// The batch order is the contract with the emitted <c>.asset</c>: <c>meshsettings</c> blocks
    /// address their material by <c>index</c>, so batch N here must be written as index N there.
    /// </summary>
    private static WeaponPiece BuildPiece(
        string name, IReadOnlyList<WeaponPart> parts,
        IReadOnlyDictionary<WeaponPart, float[]> shifts)
    {
        string shapeName = name + "Shape";

        var root = new PdxNode("File");
        root.Set("pdxasset", PdxProp.Of(1, 0));

        var shape = new PdxNode(shapeName);
        shape.Set("lod", PdxProp.Of(0));
        root.Child("object").Children.Add(shape);

        var materials = new List<ForgedMaterial>();

        foreach (var batch in parts.GroupBy(p => p.Diffuse))
        {
            var merged = new MeshBuilder();
            PdxNode? material = null;

            foreach (var part in batch)
            {
                material ??= part.Mesh.Child("material");
                merged.Append(part.Mesh, shifts[part]);
            }

            var meshNode = merged.ToNode();

            if (material is not null)
            {
                meshNode.Children.Add(material);
                materials.Add(new ForgedMaterial(
                    material.Prop("diff")?.Text ?? "",
                    material.Prop("n")?.Text ?? "",
                    material.Prop("spec")?.Text ?? ""));
            }

            shape.Children.Add(meshNode);
        }

        return new WeaponPiece(name, shapeName, root, materials);
    }


    /// <summary>
    /// Where each part ends up once assembled, as a translation in file space.
    ///
    /// <see cref="Assemble"/> merges parts into one mesh node per material and the per-part
    /// identity is gone by the time it returns, so anything that needs to treat parts separately —
    /// the icon renderer draws the hilt and crops the blade, and colours each part on its own —
    /// asks for the placement here and applies it to <see cref="WeaponPart.Mesh"/> itself.
    /// </summary>
    public static IReadOnlyDictionary<WeaponPart, float[]> Placements(
        IReadOnlyList<WeaponPart> parts, WeaponSchema schema)
    {
        var anchor = parts.FirstOrDefault(p => p.Slot == schema.Anchor)
            ?? throw new ArgumentException(
                $"A {schema.Kind} weapon needs a {schema.Anchor}.", nameof(parts));

        return Place(parts, anchor, schema);
    }

    /// <summary>
    /// Works out how far each part slides along the blade axis.
    ///
    /// Order matters, because the chain is grip → guard → blade: a blade is placed against the
    /// guard's *final* position, so the guard has to be placed first. Each reference is the part's
    /// own source measurement, captured when the library loaded, so a part borrowed from another
    /// weapon still moves by the right amount — using only the parts in the chosen set would leave
    /// every foreign part sitting at its original coordinates.
    /// </summary>
    private static Dictionary<WeaponPart, float[]> Place(
        IReadOnlyList<WeaponPart> parts, WeaponPart anchor, WeaponSchema schema)
    {
        // The anchor defines the weapon's frame; everything starts unmoved and is then snapped onto
        // whatever it mounts on, in chain order so a mount is already placed when its dependants
        // are resolved.
        var shifts = parts.ToDictionary(p => p, _ => new float[3]);

        foreach (var (slot, mountsOn) in schema.Chain)
        {
            var mount = parts.FirstOrDefault(p => p.Slot == mountsOn);
            if (mount is null) continue;

            Mate(shifts, mount, parts, slot);
        }

        return shifts;
    }

    /// <summary>
    /// Snaps every part filling <paramref name="slot"/> onto <paramref name="anchor"/>'s socket for
    /// that slot, leaving the bbox-derived shift in place when either side lacks the socket.
    /// </summary>
    private static void Mate(
        Dictionary<WeaponPart, float[]> shifts, WeaponPart anchor,
        IReadOnlyList<WeaponPart> parts, WeaponPartSlot slot)
    {
        if (!anchor.Sockets.TryGetValue(slot, out float[]? target)) return;

        float[] anchorShift = shifts[anchor];

        foreach (var part in parts.Where(p => p.Slot == slot))
        {
            if (!part.Sockets.TryGetValue(anchor.Slot, out float[]? own)) continue;

            shifts[part] =
            [
                target[0] + anchorShift[0] - own[0],
                target[1] + anchorShift[1] - own[1],
                target[2] + anchorShift[2] - own[2],
            ];
        }
    }

    // -------------------------------------------------------------------------------------
    // Vertex stream merging
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// Concatenates vertex streams from several parts into one mesh node.
    ///
    /// Triangle indices are per-mesh, so each appended part's indices shift by the running vertex
    /// count. Normals and tangents are direction vectors and a pure translation leaves them alone,
    /// which is why assembly can stay this cheap — the moment parts need rotating or scaling,
    /// normals have to be transformed too.
    /// </summary>
    private sealed class MeshBuilder
    {
        private readonly List<float> _p = [], _n = [], _ta = [], _u0 = [], _u1 = [];
        private readonly List<int> _tri = [];
        private int _vertices;

        public void Append(PdxNode mesh, float[] shift)
        {
            float[] p = mesh.Floats("p");
            int added = p.Length / 3;

            for (int i = 0; i < p.Length; i++) _p.Add(p[i] + shift[i % 3]);

            _n.AddRange(mesh.Floats("n"));
            _ta.AddRange(mesh.Floats("ta"));
            _u0.AddRange(mesh.Floats("u0"));

            // u1 is the pattern UV the recolour swatch tiles over, and it used to be dropped here:
            // every stream but this one was copied, so an assembled weapon carried a mask (sampled
            // in u0) and no coordinates for the swatch. It survived review because the generated
            // palettes are flat colour swatches, where sampling the same texel everywhere looks
            // identical to tiling correctly — the fault would only have shown on a swatch with
            // detail in it. WeaponPart.HasPatternUv gates recolouring on the SOURCE part having it,
            // so the check was reading a stream the output then discarded.
            //
            // A part missing u1 pads to zero rather than being skipped, because the streams are
            // parallel arrays indexed by vertex: appending nothing would slide every later part's
            // pattern coordinates onto the wrong vertices.
            float[] u1 = mesh.Floats("u1");

            if (u1.Length == added * 2) _u1.AddRange(u1);
            else _u1.AddRange(new float[added * 2]);

            foreach (int idx in mesh.Ints("tri")) _tri.Add(idx + _vertices);
            _vertices += added;
        }

        public PdxNode ToNode()
        {
            var mesh = new PdxNode("mesh");
            mesh.Set("p", PdxProp.Of([.. _p]));
            mesh.Set("n", PdxProp.Of([.. _n]));
            if (_ta.Count > 0) mesh.Set("ta", PdxProp.Of([.. _ta]));
            if (_u0.Count > 0) mesh.Set("u0", PdxProp.Of([.. _u0]));

            // Only when something real is in it. A weapon built entirely from parts with no pattern
            // UV would otherwise ship an all-zero stream, which claims a capability the geometry
            // does not have and would defeat any later check for its absence.
            if (_u1.Count > 0 && _u1.Exists(v => v != 0f)) mesh.Set("u1", PdxProp.Of([.. _u1]));
            mesh.Set("tri", PdxProp.Of([.. _tri]));

            var (min, max) = Bounds();
            mesh.Set("boundingsphere", PdxProp.Of(
                (min[0] + max[0]) / 2f,
                (min[1] + max[1]) / 2f,
                (min[2] + max[2]) / 2f,
                Radius(min, max)));

            var aabb = mesh.Child("aabb");
            aabb.Set("min", PdxProp.Of(min));
            aabb.Set("max", PdxProp.Of(max));

            return mesh;
        }

        private (float[] Min, float[] Max) Bounds()
        {
            if (_p.Count == 0) return (new float[3], new float[3]);

            var min = new[] { float.MaxValue, float.MaxValue, float.MaxValue };
            var max = new[] { float.MinValue, float.MinValue, float.MinValue };

            for (int i = 0; i < _p.Count; i += 3)
            {
                for (int a = 0; a < 3; a++)
                {
                    min[a] = Math.Min(min[a], _p[i + a]);
                    max[a] = Math.Max(max[a], _p[i + a]);
                }
            }

            return (min, max);
        }

        private static float Radius(float[] min, float[] max)
        {
            float dx = (max[0] - min[0]) / 2f, dy = (max[1] - min[1]) / 2f, dz = (max[2] - min[2]) / 2f;
            return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }
}
