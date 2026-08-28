namespace Ck3MapGen.Emit;

using Ck3MapGen.Io;
using Ck3MapGen.MapGen;
using System.IO;

/// <summary>
/// Draws a forged weapon's inventory icon from its own geometry, instead of tinting a stock one.
///
/// **The composition is vanilla's, measured rather than guessed.** Opening
/// <c>artifact_sword.dds</c> shows the frame is not a whole sword: it is the hilt, upright and
/// centred, with the blade running off the bottom edge. Pommel sits just below the top margin and
/// the guard lands around 40% down. That matters because a whole sword in a 240x240 cell is a thin
/// diagonal that wastes the frame, and because at the <b>30-60 pixels</b> these are actually drawn
/// at, the hilt is the only part carrying identity.
///
/// **Frames are 240 wide, not 238.** The file is 960 across and <c>window_inventory.gui</c> reads it
/// as <c>framesize = { 240 240 }</c>, which divides exactly; the 238 in
/// <c>decision_view_widget_commission_artifact.gui</c> would misalign every frame after the first.
///
/// **Rarity is not baked in here.** <c>icon_artifact</c> stacks three layers — a rarity backing from
/// <c>artifact_bg.dds</c>, the unique overlay, then this icon — so the coloured backing is drawn
/// behind us for free and all we owe it is a clean alpha margin. The four frames of the *item* art
/// do still vary: vanilla walks the hilt metal copper, iron, brass, gold, and haloes the last one.
/// We cannot follow it into hue, because the hilt metal is already chosen by the weapon's finish and
/// overriding it would discard the identity the palette exists to give — so rarity rides on
/// brightness, saturation and a top-tier glow instead. See <see cref="Rarity"/>.
/// </summary>
public static class ForgedWeaponRender
{
    /// <summary>
    /// Whether this kind's identity is at the far end of the haft rather than in the hand.
    ///
    /// Bladed weapons are framed on the hilt with the blade cropped; hafted weapons are the mirror,
    /// framed on the head with the haft cropped. Reading it off the schema's lead slot keeps the
    /// two paths one renderer rather than two.
    /// </summary>
    private static bool HeadUp(WeaponSchema schema) => schema.Lead == WeaponPartSlot.Head;

    /// <summary>Edge of one frame. Four of these make the 960x240 strip.</summary>
    private const int Size = 240;

    /// <summary>Supersample factor. The whole render is 960x960 and box-filters down to 240.</summary>
    private const int Super = 4;

    private const int Big = Size * Super;

    /// <summary>Share of the frame height the hilt occupies, and the margin above the pommel.</summary>
    private const double HiltShare = 0.42;
    private const double TopMargin = 0.05;

    /// <summary>
    /// Bounds on the window, in world units.
    ///
    /// Hilt-proportional framing alone does the wrong thing when a hilt is unusually long: an
    /// African hilt measures 32.6 units against a western one's 20, and dividing by
    /// <see cref="HiltShare"/> zoomed the window out to 77.6 units, rendering the weapon as a
    /// sliver. A longer hilt should fill more of the frame, not push the camera back. Clamping is
    /// legitimate here only because every part is cut from a vanilla weapon at one consistent
    /// scale, so "a sword hilt is 20 to 33 units" is a fact about the libraries rather than a guess
    /// — it would need revisiting before this framed a different kind.
    /// </summary>
    private const double MinWindow = 30.0;
    private const double MaxWindow = 52.0;

    /// <summary>
    /// Framing for a hafted weapon, where the head is the identity and the haft is cropped.
    ///
    /// Vanilla composes these as the mirror of a sword — <c>artifact_axe.dds</c> and
    /// <c>artifact_mace.dds</c> put the head at the top with the haft running off the bottom — so
    /// the geometry is rotated to match and the window is anchored on the head.
    ///
    /// **Width leads, not length.** A hilt has a consistent length and can drive the frame directly;
    /// a head does not. Measured over both libraries, head widths span 14.7–27.3 on axes and
    /// 5.9–17.3 on maces — under 3x — while axial lengths span 5.1–36.4 and 4.2–20.2, up to 7x. So
    /// width sets the frame and length only pulls it wider when a head is unusually long, which is
    /// what stops a 36-unit <c>ep1_mena_axe</c> head overflowing the top.
    /// </summary>
    private const double HeadWidthShare = 0.72;
    private const double HeadAxialShare = 0.50;
    private const double MinWindowHafted = 24.0;

    /// <summary>
    /// Some swords are drawn corner to corner instead of upright, as <c>artifact_longsword.dds</c>
    /// is — measured at 88% of the frame width against <c>artifact_sword.dds</c>'s 37%.
    ///
    /// The blade is still cropped, exactly as in the upright composition — a tilt is a rotation, not
    /// a zoom-out. What it buys is the frame's diagonal, 1.41x its side, so more of the blade reads
    /// before it leaves the frame while the hilt stays the same size. Vanilla mixes both treatments,
    /// and mixing them here gives an inventory some rhythm.
    ///
    /// Swords only. A dagger already fits upright, so tilting one would cost legibility and buy
    /// nothing; an axe or mace reads by its head silhouette, which a tilt muddles.
    /// </summary>
    private const double TiltDegrees = -38.0;

    /// <summary>Roughly one sword in this many is tilted.</summary>
    private const int TiltOneIn = 3;

    /// <summary>
    /// Where the pommel sits in a tilted frame, as a fraction of it: upper right, with the blade
    /// running away to the lower left and cropping at the edge.
    ///
    /// A tilt is the *same* zoom as the upright composition, only rotated — not a zoom-out. Fitting
    /// the whole weapon into the frame instead shrinks the hilt to nothing, which is exactly what
    /// vanilla avoids: <c>artifact_longsword.dds</c> crops its blade at the frame edge and keeps the
    /// hilt large. Anchoring on the pommel rather than centring is what buys the diagonal its extra
    /// room, since the blade then has the full diagonal to run down.
    /// </summary>
    private const double TiltAnchorX = 0.74;
    private const double TiltAnchorY = 0.20;

    /// <summary>
    /// Share of the window a *head* takes when tilted, and the cap that goes with it.
    ///
    /// Both are looser than the upright pair because the head is the whole subject here rather than
    /// one end of it, and because the diagonal is longer than the side. The cap especially: the
    /// longest spear head measures 71 units, and at the upright MaxWindow of 52 it would have been
    /// cropped at the point AND the socket, which is the one outcome worse than a small icon.
    /// </summary>
    private const double TiltHeadShare = 0.78;
    private const double MaxWindowTilted = 96.0;

    /// <param name="kind">
    /// The weapon kind, not <see cref="WeaponSchema.Kind"/> — the schema's own Kind is the family
    /// ("bladed" / "hafted") and is shared by sword and dagger, so testing it here silently tilted
    /// nothing at all.
    /// </param>
    private static bool Tilted(string name, string kind)
        => kind switch
        {
            // Always, never one-in-three. A spear head is long and narrow where an axe head is short
            // and wide - measured across the libraries, spear heads run 9-71 units along the shaft
            // against an axe's 5-36, with a median of 46 against 17 - so upright framing either
            // shrinks it to a sliver or crops the point off. Vanilla reaches the same conclusion:
            // artifact_spear.dds is drawn corner to corner while artifact_axe.dds stands upright.
            "spear" => true,
            "sword" => StableHash(name) % TiltOneIn == 0,
            _ => false,
        };

    /// <summary>
    /// FNV-1a. Deliberately not <c>string.GetHashCode</c>, which .NET randomises per process — a
    /// weapon would then tilt in one run and stand upright in the next from the very same seed,
    /// which would make generation non-reproducible for no reason anybody could see.
    /// </summary>
    private static uint StableHash(string s)
    {
        uint h = 2166136261;

        foreach (char c in s)
        {
            h ^= c;
            h *= 16777619;
        }

        return h;
    }

    /// <summary>Light and ambient. Ambient is low because a flat-lit weapon reads as vector art.</summary>
    private static readonly double[] Light = Normalise([0.62, -0.50, 0.60]);
    private const double Ambient = 0.20;

    /// <summary>Darkening applied at the silhouette edge, falling off inward over Super pixels.</summary>
    private const double Outline = 0.45;

    /// <summary>
    /// Rim light: how strongly a surface turning away from the camera picks up a cool fill.
    ///
    /// This is what stops metal reading as plastic. A single key light gives a form its lit and
    /// unlit halves and nothing else; the rim reintroduces the bounce a real object gets from its
    /// surroundings, and because it peaks exactly where the silhouette is, it also separates the
    /// weapon from the background at icon size.
    /// </summary>
    private const double RimGain = 0.30;
    private const double RimPower = 3.0;
    private static readonly double[] RimColour = [150, 170, 205];

    /// <summary>Fraction of a blurred copy of the specular added back, for a soft glint.</summary>
    private const double BloomGain = 0.35;

    /// <summary>Unsharp amount applied after the box filter, to recover bite lost to downsampling.</summary>
    private const double Sharpen = 0.35;

    /// <summary>
    /// Per-rarity treatment, calibrated against vanilla's own four frames — whose measured mean
    /// channel values are (98,85,83) (93,91,91) (107,98,85) (140,130,113), a relative luminance ramp
    /// of roughly 0.88 / 0.91 / 1.00 / 1.30.
    ///
    /// Saturation carries most of the separation between the lower three: brightness alone left them
    /// nearly indistinguishable, and muting is the one adjustment that cannot damage identity —
    /// it moves a colour toward grey, never toward a different metal.
    /// </summary>
    /// <param name="Glow">Halo strength. Present at every tier, not just the top.</param>
    /// <param name="Warm">
    /// How far the halo shifts from white toward gold, 0 to 1.
    ///
    /// White at the low tiers because the halo is doing a plain job there — separating a dark weapon
    /// from a dark rarity backing, which the silhouette outline alone cannot do once the icon is
    /// down at 48 pixels. Gold is reserved for the top tier, where vanilla uses it and where it
    /// should read as the weapon being special rather than as legibility.
    /// </param>
    private static readonly (double Gain, double Spec, double Sat, double Glow, double Warm)[] Rarity =
    [
        (0.82, 0.30, 0.55, 0.18, 0.00),
        (0.93, 0.45, 0.80, 0.26, 0.00),
        (1.06, 0.62, 1.00, 0.38, 0.35),
        (1.24, 0.85, 1.12, 0.62, 1.00),
    ];

    /// <summary>Halo colour at <c>Warm = 0</c> and at <c>Warm = 1</c>.</summary>
    private static readonly double[] GlowCool = [255, 255, 255];
    private static readonly double[] GlowWarm = [255, 226, 150];

    /// <summary>
    /// Renders and writes <c>&lt;weapon&gt;_icon.dds</c>, returning its bare filename — the
    /// <c>icon</c> field wants a filename, not a path. Null when the weapon cannot be placed, in
    /// which case the caller keeps whatever icon it already had.
    /// </summary>
    public static string? Write(
        string modDir, string gameDir, ForgedWeapon weapon, WeaponSchema schema, string kind,
        IReadOnlyList<(byte R, byte G, byte B)> partColours)
    {
        bool tilt = Tilted(weapon.Name, kind);

        var placed = Gather(gameDir, weapon, schema, partColours, tilt);
        if (placed.Count == 0) return null;

        var (top, height, centre) = Window(placed, schema, tilt);

        var depth = new double[Big * Big];
        var diffuse = new double[Big * Big];
        var specular = new double[Big * Big];
        var colour = new int[Big * Big];
        var rimBuf = new double[Big * Big];
        var texel = new byte[Big * Big * 3];

        Array.Fill(depth, double.NegativeInfinity);
        Array.Fill(colour, -1);

        for (int i = 0; i < placed.Count; i++)
            Raster(placed[i], i, top, height, centre, depth, diffuse, specular, rimBuf, colour, texel);

        var strip = new byte[Size * 4 * Size * 4];

        // Once, not once per frame: the silhouette depends only on coverage, which every rarity
        // frame shares. Recomputing it inside Compose did the same four erosion passes four times.
        var edge = EdgePixels(colour);

        for (int frame = 0; frame < Rarity.Length; frame++)
            Compose(strip, frame, diffuse, specular, rimBuf, colour, texel, edge, placed, Rarity[frame]);

        string dir = Path.Combine(modDir, ForgedWeaponIcon.IconDir.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(dir);

        // Block-compressed: the strip is 960x240, and at the 30-60 pixels an icon is actually drawn
        // the artefacts are far below what survives the downscale. A quarter of the bytes.
        string file = $"{weapon.Name}_icon.dds";
        DdsWriter.WriteDxt5(Path.Combine(dir, file), Size * 4, Size, strip);

        return file;
    }

    // -------------------------------------------------------------------------------------
    // Geometry
    // -------------------------------------------------------------------------------------

    /// <summary>One part, moved to where assembly puts it, with the colour it will wear.</summary>
    private sealed record Placed(
        float[] P, float[] N, float[] Uv, int[] Tri, bool IsLead,
        (byte R, byte G, byte B) Colour, DdsReader.DecodedImage? Texture);

    /// <summary>
    /// Decoded weapon diffuse textures, by bare filename.
    ///
    /// Worth caching hard: a pool shares families heavily, so eight swords reference perhaps five
    /// distinct textures between them, and each is a 1–4 MB DXT decode. Nulls are cached too, so a
    /// texture that cannot be found is looked for once rather than once per weapon.
    /// </summary>
    private static readonly Dictionary<string, DdsReader.DecodedImage?> TextureCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, string>? _textureIndex;

    /// <summary>
    /// Guards both the cache and the index, because icons are rendered in parallel.
    ///
    /// A plain lock rather than a concurrent dictionary on purpose: <c>GetOrAdd</c> would let two
    /// threads racing on the same filename both decode it, and these are multi-megabyte DXT decodes.
    /// Serialising them costs nothing measurable — a pool of 32 weapons shares about five distinct
    /// textures, so this is entered a handful of times and every later call is a dictionary hit.
    /// </summary>
    private static readonly Lock TextureLock = new();

    /// <summary>
    /// Finds a texture by bare filename, which is how CK3 itself resolves them — the exporter writes
    /// only a basename and the engine searches globally. The index is built once over
    /// <c>gfx/models/artifacts</c>, where every weapon texture lives; walking all of
    /// <c>gfx/models</c> would index seven thousand files to find the few dozen that matter.
    /// </summary>
    private static DdsReader.DecodedImage? Texture(string gameDir, string file)
    {
        if (string.IsNullOrWhiteSpace(file)) return null;

        lock (TextureLock)
        {
            if (TextureCache.TryGetValue(file, out var cached)) return cached;

            if (_textureIndex is null)
            {
                _textureIndex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string root = Path.Combine(gameDir, "gfx", "models", "artifacts");

                if (Directory.Exists(root))
                {
                    foreach (string path in Directory.EnumerateFiles(root, "*.dds", SearchOption.AllDirectories))
                        _textureIndex.TryAdd(Path.GetFileName(path), path);
                }
            }

            var image = _textureIndex.TryGetValue(file, out string? found) ? DdsReader.Load(found) : null;
            TextureCache[file] = image;

            return image;
        }
    }

    private static List<Placed> Gather(
        string gameDir, ForgedWeapon weapon, WeaponSchema schema,
        IReadOnlyList<(byte R, byte G, byte B)> colours, bool tilt)
    {
        IReadOnlyDictionary<WeaponPart, float[]> shifts;

        try { shifts = WeaponForge.Placements(weapon.Parts, schema); }
        catch (ArgumentException) { return []; }

        var placed = new List<Placed>();

        for (int i = 0; i < weapon.Parts.Count; i++)
        {
            var part = weapon.Parts[i];
            var mesh = part.Mesh;

            float[] p = mesh.Floats("p");
            int[] tri = mesh.Ints("tri");
            if (p.Length == 0 || tri.Length == 0) continue;

            float[] shift = shifts.TryGetValue(part, out var s) ? s : new float[3];
            var moved = new float[p.Length];

            // A hafted weapon's head sits at negative Z — it mounts at the haft's socket, which is
            // 35 to 64 units down — so it would render at the bottom. Rotating 180 degrees about X
            // puts it at the top the way vanilla draws it. A rotation, not a mirror: negating both
            // Y and Z keeps the determinant positive, so the weapon is not handed the wrong way
            // round. Baked in here so the window and the rasteriser stay orientation-agnostic.
            bool flip = HeadUp(schema);
            float sign = flip ? -1f : 1f;

            // Tilt is a rotation about the view axis, so it is an image-plane rotation: the light
            // stays put in screen space and the weapon turns under it, which is what makes a tilted
            // icon read as the same object photographed differently rather than as one lit oddly.
            double theta = tilt ? TiltDegrees * Math.PI / 180.0 : 0.0;
            float ct = (float)Math.Cos(theta), st = (float)Math.Sin(theta);

            for (int v = 0; v + 2 < p.Length; v += 3)
            {
                float y = (p[v + 1] + shift[1]) * sign;
                float z = (p[v + 2] + shift[2]) * sign;

                moved[v] = p[v] + shift[0];
                moved[v + 1] = y * ct - z * st;
                moved[v + 2] = y * st + z * ct;
            }

            float[] normals = mesh.Floats("n");

            if ((flip || tilt) && normals.Length > 0)
            {
                var turned = new float[normals.Length];

                for (int v = 0; v + 2 < normals.Length; v += 3)
                {
                    float ny = normals[v + 1] * sign;
                    float nz = normals[v + 2] * sign;

                    turned[v] = normals[v];
                    turned[v + 1] = ny * ct - nz * st;
                    turned[v + 2] = ny * st + nz * ct;
                }

                normals = turned;
            }

            var colour = i < colours.Count ? colours[i] : ((byte)170, (byte)172, (byte)176);

            placed.Add(new Placed(
                moved, normals, mesh.Floats("u0"), tri,
                part.Slot == schema.Lead, colour, Texture(gameDir, part.Diffuse)));
        }

        return placed;
    }

    /// <summary>
    /// The orthographic window: upright, hilt-anchored, blade cropped off the bottom.
    ///
    /// The hilt is everything that is not the lead part, which is ground truth from the schema. An
    /// earlier prototype located the guard as the widest lateral extent instead; that works on a
    /// straight sword and fails on a curved one, where the widest point is out on the blade's sweep,
    /// so a sabre's "hilt" became the whole weapon and it rendered as a sliver.
    /// </summary>
    /// <summary>
    /// Window for the tilted composition: the same hilt-anchored zoom as upright, with the pommel
    /// pinned to <see cref="TiltAnchorX"/> / <see cref="TiltAnchorY"/> and the blade cropping away.
    ///
    /// The hilt is measured along the weapon's own axis rather than along screen-vertical. Rotation
    /// is already baked into the geometry, so a vertical measurement would read the hilt as
    /// <c>cos(38 deg)</c> — a fifth — shorter than it is and zoom in that much too far. Projecting
    /// onto the rotated axis recovers the true length, so a tilted weapon and an upright one of the
    /// same build get the same window.
    /// </summary>
    private static (double Top, double Height, double Centre) TiltWindow(
        List<Placed> placed, WeaponSchema schema)
    {
        double theta = TiltDegrees * Math.PI / 180.0;
        double ay = -Math.Sin(theta), az = Math.Cos(theta);
        bool headUp = HeadUp(schema);

        double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
        double py = 0, pz = 0;

        foreach (var part in placed)
        {
            // Frame the feature and crop the rest: the hilt on a bladed weapon, the head on a
            // hafted one. Both are already the "up" end, because the hafted flip in Gather turned
            // the geometry so the head leads.
            if (part.IsLead != headUp) continue;

            for (int v = 0; v + 2 < part.P.Length; v += 3)
            {
                double s = part.P[v + 1] * ay + part.P[v + 2] * az;
                lo = Math.Min(lo, s);

                if (s <= hi) continue;

                // Furthest point up the axis — the pommel end, which is what gets anchored.
                hi = s;
                py = part.P[v + 1];
                pz = part.P[v + 2];
            }
        }

        if (double.IsInfinity(lo)) return (0, MaxWindow, 0);

        // A tilted weapon has the frame's diagonal to run down, 1.41x its side, so the feature can
        // take a larger share than it would upright without the point leaving the frame.
        double share = headUp ? TiltHeadShare : HiltShare;
        double height = Math.Clamp(Math.Max(hi - lo, 1e-3) / share, MinWindow, MaxWindowTilted);

        return (pz + TiltAnchorY * height, height, py - (TiltAnchorX - 0.5) * height);
    }

    private static (double Top, double Height, double Centre) Window(
        List<Placed> placed, WeaponSchema schema, bool tilt)
    {
        if (tilt) return TiltWindow(placed, schema);

        bool headUp = HeadUp(schema);

        double zTop = double.NegativeInfinity, zBot = double.PositiveInfinity;
        double featureBottom = double.PositiveInfinity;
        double headWidth = 0;

        foreach (var part in placed)
        {
            // The feature is whatever the frame is built around: the hilt on a bladed weapon, which
            // is everything except the lead, and the head on a hafted one, which is the lead itself.
            bool isFeature = headUp ? part.IsLead : !part.IsLead;

            for (int v = 0; v + 2 < part.P.Length; v += 3)
            {
                double z = part.P[v + 2];
                zTop = Math.Max(zTop, z);
                zBot = Math.Min(zBot, z);

                if (!isFeature) continue;

                featureBottom = Math.Min(featureBottom, z);
                headWidth = Math.Max(headWidth, Math.Abs(part.P[v + 1]));
            }
        }

        double span = zTop - zBot;

        // A weapon that is all lead, or whose feature somehow spans more than half of it, gets a
        // fixed share of total length instead — never a window derived from a measurement that lies.
        if (double.IsInfinity(featureBottom) || zTop - featureBottom > 0.5 * span)
            featureBottom = zTop - 0.22 * span;

        double featureLength = Math.Max(zTop - featureBottom, 1e-3);

        double height = headUp
            ? Math.Clamp(
                Math.Max(2.0 * headWidth / HeadWidthShare, featureLength / HeadAxialShare),
                MinWindowHafted, MaxWindow)
            : Math.Clamp(featureLength / HiltShare, MinWindow, MaxWindow);

        double top = zTop + height * TopMargin;

        // Centre on what is inside the window rather than on the whole weapon: a cropped-away tip
        // pulling the centre would shove the hilt off to one side.
        double lo = double.PositiveInfinity, hi = double.NegativeInfinity;

        foreach (var part in placed)
        {
            for (int v = 0; v + 2 < part.P.Length; v += 3)
            {
                if (part.P[v + 2] < top - height) continue;
                lo = Math.Min(lo, part.P[v + 1]);
                hi = Math.Max(hi, part.P[v + 1]);
            }
        }

        return (top, height, double.IsInfinity(lo) ? 0 : 0.5 * (lo + hi));
    }

    // -------------------------------------------------------------------------------------
    // Rasteriser
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// Orthographic, looking down -X so the blade shows flat: screen-right is +Y, screen-down is -Z,
    /// depth is +X toward the viewer. Which axis is which was measured, not assumed — on an assembled
    /// sword the blade spans about 1.4 units in X against 6.2 in Y, so X is the thin edge-on axis.
    /// </summary>
    /// <summary>
    /// Writes the part's diffuse texel at this pixel, or a flat mid-grey when the part has no
    /// texture — <see cref="WeaponPart.HasTextures"/> already keeps untextured families out of the
    /// pool, so this only guards the case where the file cannot be found on disk.
    /// </summary>
    private static void Sample(
        Placed part, double l1, double l2, double l3, int a, int b, int c, byte[] texel, int i)
    {
        if (part.Texture is not { } tex || part.Uv.Length < 2)
        {
            texel[i * 3] = texel[i * 3 + 1] = texel[i * 3 + 2] = 160;
            return;
        }

        if (2 * a + 1 >= part.Uv.Length || 2 * b + 1 >= part.Uv.Length || 2 * c + 1 >= part.Uv.Length)
        {
            texel[i * 3] = texel[i * 3 + 1] = texel[i * 3 + 2] = 160;
            return;
        }

        double u = l1 * part.Uv[2 * a] + l2 * part.Uv[2 * b] + l3 * part.Uv[2 * c];
        double v = l1 * part.Uv[2 * a + 1] + l2 * part.Uv[2 * b + 1] + l3 * part.Uv[2 * c + 1];

        u -= Math.Floor(u);
        v -= Math.Floor(v);

        int tx = Math.Clamp((int)(u * tex.Width), 0, tex.Width - 1);
        int ty = Math.Clamp((int)(v * tex.Height), 0, tex.Height - 1);
        int t = (ty * tex.Width + tx) * 4;

        if (t + 2 >= tex.Bgra.Length)
        {
            texel[i * 3] = texel[i * 3 + 1] = texel[i * 3 + 2] = 160;
            return;
        }

        texel[i * 3] = tex.Bgra[t + 2];        // R
        texel[i * 3 + 1] = tex.Bgra[t + 1];    // G
        texel[i * 3 + 2] = tex.Bgra[t];        // B
    }

    private static void Raster(
        Placed part, int index, double top, double height, double centre,
        double[] depth, double[] diffuse, double[] specular, double[] rimBuf, int[] colour,
        byte[] texel)
    {
        double[] half = Normalise([Light[0] + 1.0, Light[1], Light[2]]);

        int n = part.P.Length / 3;
        var sx = new double[n];
        var sy = new double[n];
        var lam = new double[n];
        var spc = new double[n];
        var rim = new double[n];

        for (int v = 0; v < n; v++)
        {
            sx[v] = (part.P[v * 3 + 1] - centre) / height * Big + Big / 2.0;
            sy[v] = (top - part.P[v * 3 + 2]) / height * Big;

            double nx = 1, ny = 0, nz = 0;

            if (part.N.Length >= part.P.Length)
            {
                nx = part.N[v * 3];
                ny = part.N[v * 3 + 1];
                nz = part.N[v * 3 + 2];
            }

            lam[v] = Math.Clamp(nx * Light[0] + ny * Light[1] + nz * Light[2], 0, 1);
            spc[v] = Math.Pow(Math.Clamp(nx * half[0] + ny * half[1] + nz * half[2], 0, 1), 32);

            // How far this surface has turned away from the camera. The view axis is +X, so the
            // facing term is just |nx| and the rim is whatever is left of it.
            rim[v] = Math.Pow(1.0 - Math.Abs(nx), RimPower);
        }

        for (int t = 0; t + 2 < part.Tri.Length; t += 3)
        {
            int a = part.Tri[t], b = part.Tri[t + 1], c = part.Tri[t + 2];
            if (a >= n || b >= n || c >= n) continue;

            double ax = sx[a], ay = sy[a], bx = sx[b], by = sy[b], cx = sx[c], cy = sy[c];

            double den = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy);
            if (Math.Abs(den) < 1e-12) continue;

            int x0 = Math.Max((int)Math.Floor(Math.Min(ax, Math.Min(bx, cx))), 0);
            int x1 = Math.Min((int)Math.Ceiling(Math.Max(ax, Math.Max(bx, cx))) + 1, Big);
            int y0 = Math.Max((int)Math.Floor(Math.Min(ay, Math.Min(by, cy))), 0);
            int y1 = Math.Min((int)Math.Ceiling(Math.Max(ay, Math.Max(by, cy))) + 1, Big);

            for (int y = y0; y < y1; y++)
            {
                double py = y + 0.5;

                for (int x = x0; x < x1; x++)
                {
                    double px = x + 0.5;
                    double l1 = ((by - cy) * (px - cx) + (cx - bx) * (py - cy)) / den;
                    double l2 = ((cy - ay) * (px - cx) + (ax - cx) * (py - cy)) / den;
                    double l3 = 1.0 - l1 - l2;

                    if (l1 < 0 || l2 < 0 || l3 < 0) continue;

                    double d = l1 * part.P[a * 3] + l2 * part.P[b * 3] + l3 * part.P[c * 3];
                    int i = y * Big + x;
                    if (d <= depth[i]) continue;

                    depth[i] = d;
                    diffuse[i] = l1 * lam[a] + l2 * lam[b] + l3 * lam[c];
                    specular[i] = l1 * spc[a] + l2 * spc[b] + l3 * spc[c];
                    rimBuf[i] = l1 * rim[a] + l2 * rim[b] + l3 * rim[c];
                    colour[i] = index;

                    // The part's own diffuse, sampled through interpolated UV0 — the surface detail
                    // that made the difference between a render and flat vector art. Nearest
                    // neighbour is enough: the render is supersampled 4x and box-filtered down, so
                    // the downsample is already doing the averaging a bilinear fetch would.
                    Sample(part, l1, l2, l3, a, b, c, texel, i);
                }
            }
        }
    }

    // -------------------------------------------------------------------------------------
    // Composition
    // -------------------------------------------------------------------------------------

    private static void Compose(
        byte[] strip, int frame, double[] diffuse, double[] specular, double[] rimBuf, int[] colour,
        byte[] texel, double[] edge, List<Placed> placed,
        (double Gain, double Spec, double Sat, double Glow, double Warm) look)
    {
        var rgb = new double[Big * Big * 3];

        for (int i = 0; i < colour.Length; i++)
        {
            if (colour[i] < 0) continue;

            // Texture times tint, because that is what the game does: the variation shader ends on
            // `Diffuse *= PatternDiffuse`, so the palette colour modulates the part's own diffuse
            // rather than replacing it. Matching the multiply here is what keeps the icon agreeing
            // with the model — a near-white steel barely shifts its texture, while ebony darkens it
            // heavily, and using the tint flat would have shown neither.
            var c = placed[colour[i]].Colour;
            double r = c.R * texel[i * 3] / 255.0;
            double g = c.G * texel[i * 3 + 1] / 255.0;
            double b = c.B * texel[i * 3 + 2] / 255.0;

            if (look.Sat != 1.0)
            {
                double lum = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                r = lum + (r - lum) * look.Sat;
                g = lum + (g - lum) * look.Sat;
                b = lum + (b - lum) * look.Sat;
            }

            double lit = Ambient + (1.0 - Ambient) * diffuse[i];
            double shine = 255.0 * specular[i] * look.Spec;
            double dim = 1.0 - Outline * edge[i];
            double rim = rimBuf[i] * RimGain;

            rgb[i * 3] = (r * lit + shine + RimColour[0] * rim) * dim * look.Gain;
            rgb[i * 3 + 1] = (g * lit + shine + RimColour[1] * rim) * dim * look.Gain;
            rgb[i * 3 + 2] = (b * lit + shine + RimColour[2] * rim) * dim * look.Gain;
        }

        // Box-filter down. Colour is divided by coverage rather than by the block size so a
        // partially covered edge pixel keeps full-strength colour and fades through alpha alone —
        // otherwise every silhouette edge darkens toward black.
        var alpha = new double[Size * Size];
        var small = new double[Size * Size * 3];

        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                double cov = 0, sr = 0, sg = 0, sb = 0;

                for (int dy = 0; dy < Super; dy++)
                {
                    for (int dx = 0; dx < Super; dx++)
                    {
                        int i = (y * Super + dy) * Big + x * Super + dx;
                        if (colour[i] < 0) continue;

                        cov++;
                        sr += rgb[i * 3];
                        sg += rgb[i * 3 + 1];
                        sb += rgb[i * 3 + 2];
                    }
                }

                int o = y * Size + x;
                alpha[o] = cov / (Super * Super);

                if (cov <= 0) continue;

                small[o * 3] = sr / cov;
                small[o * 3 + 1] = sg / cov;
                small[o * 3 + 2] = sb / cov;
            }
        }

        Bloom(small, alpha);
        Unsharp(small, alpha);

        if (look.Glow > 0) Glow(small, alpha, look.Glow, look.Warm);

        // Into the strip, which is 960 wide: frame N starts at column N*240.
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                int src = y * Size + x;
                int dst = (y * Size * 4 + frame * Size + x) * 4;

                strip[dst] = Clamp(small[src * 3 + 2]);       // B
                strip[dst + 1] = Clamp(small[src * 3 + 1]);   // G
                strip[dst + 2] = Clamp(small[src * 3]);       // R
                strip[dst + 3] = Clamp(alpha[src] * 255.0);
            }
        }
    }

    /// <summary>
    /// How close each pixel is to the silhouette edge, as 1 at the outermost pixel falling to 0
    /// <see cref="Super"/> pixels in.
    ///
    /// Darkening the edge is what separates the weapon from whatever is behind it at 48 pixels,
    /// which vanilla's painted art has built in and a clean render does not. This started as a
    /// boolean band and drew a hard black keyline that read as an outline rather than as shading;
    /// grading it over the same distance keeps the separation and loses the cartoon.
    /// </summary>
    private static double[] EdgePixels(int[] colour)
    {
        var eroded = new bool[colour.Length];
        for (int i = 0; i < colour.Length; i++) eroded[i] = colour[i] >= 0;

        var depthIn = new int[colour.Length];

        for (int pass = 0; pass < Super; pass++)
        {
            var next = new bool[eroded.Length];

            for (int y = 0; y < Big; y++)
            {
                for (int x = 0; x < Big; x++)
                {
                    int i = y * Big + x;
                    if (!eroded[i]) continue;

                    next[i] = (x == 0 || eroded[i - 1]) && (x == Big - 1 || eroded[i + 1])
                        && (y == 0 || eroded[i - Big]) && (y == Big - 1 || eroded[i + Big]);

                    if (next[i]) depthIn[i]++;
                }
            }

            eroded = next;
        }

        var edge = new double[colour.Length];

        for (int i = 0; i < colour.Length; i++)
            edge[i] = colour[i] < 0 ? 0 : 1.0 - depthIn[i] / (double)Super;

        return edge;
    }

    /// <summary>
    /// Soft glint: blurs the brightest pixels and adds a fraction back.
    ///
    /// Only what is already above mid-grey blooms, so it lifts highlights on polished metal and
    /// leaves a dark leather grip alone — which is the difference between a glint and a haze over
    /// the whole icon. Kept inside the silhouette so it cannot bleed into the alpha margin the
    /// rarity backing shows through.
    /// </summary>
    private static void Bloom(double[] rgb, double[] alpha)
    {
        var bright = new double[Size * Size];

        for (int i = 0; i < bright.Length; i++)
        {
            if (alpha[i] <= 0) continue;
            double lum = 0.2126 * rgb[i * 3] + 0.7152 * rgb[i * 3 + 1] + 0.0722 * rgb[i * 3 + 2];
            bright[i] = Math.Max(0, lum - 128.0) / 127.0;
        }

        var blur = new double[bright.Length];

        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                double acc = 0;

                for (int dy = -2; dy <= 2; dy++)
                {
                    int sy = Math.Clamp(y + dy, 0, Size - 1);
                    for (int dx = -2; dx <= 2; dx++)
                        acc += bright[sy * Size + Math.Clamp(x + dx, 0, Size - 1)];
                }

                blur[y * Size + x] = acc / 25.0;
            }
        }

        for (int i = 0; i < blur.Length; i++)
        {
            if (alpha[i] <= 0) continue;
            double add = 255.0 * blur[i] * BloomGain;
            for (int c = 0; c < 3; c++) rgb[i * 3 + c] += add;
        }
    }

    /// <summary>
    /// Unsharp mask, to put back the bite the 4x box filter takes out.
    ///
    /// These are viewed at 30-60 pixels, so the icon is downsampled twice — once here and again by
    /// the game — and a doubly-filtered render goes soft exactly where the detail matters. Masked by
    /// alpha so the silhouette does not grow a halo.
    /// </summary>
    private static void Unsharp(double[] rgb, double[] alpha)
    {
        var copy = (double[])rgb.Clone();

        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                int i = y * Size + x;
                if (alpha[i] <= 0) continue;

                for (int c = 0; c < 3; c++)
                {
                    double acc = 0;
                    double weight = 0;

                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int sy = Math.Clamp(y + dy, 0, Size - 1);

                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int s = sy * Size + Math.Clamp(x + dx, 0, Size - 1);
                            if (alpha[s] <= 0) continue;
                            acc += copy[s * 3 + c];
                            weight++;
                        }
                    }

                    if (weight <= 0) continue;
                    rgb[i * 3 + c] = copy[i * 3 + c] + Sharpen * (copy[i * 3 + c] - acc / weight);
                }
            }
        }
    }

    /// <summary>
    /// Warm halo outside the silhouette, as vanilla's top rarity frame carries. Composited under the
    /// art so it never washes out the weapon, and left translucent so the rarity backing reads
    /// through it.
    /// </summary>
    private static void Glow(double[] rgb, double[] alpha, double strength, double warmth)
    {
        var blur = (double[])alpha.Clone();

        for (int pass = 0; pass < 3; pass++)
        {
            var next = new double[blur.Length];

            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    double acc = 0;

                    for (int dy = -3; dy <= 3; dy++)
                    {
                        int sy = Math.Clamp(y + dy, 0, Size - 1);

                        for (int dx = -3; dx <= 3; dx++)
                            acc += blur[sy * Size + Math.Clamp(x + dx, 0, Size - 1)];
                    }

                    next[y * Size + x] = acc / 49.0;
                }
            }

            blur = next;
        }

        double[] tint =
        [
            GlowCool[0] + (GlowWarm[0] - GlowCool[0]) * warmth,
            GlowCool[1] + (GlowWarm[1] - GlowCool[1]) * warmth,
            GlowCool[2] + (GlowWarm[2] - GlowCool[2]) * warmth,
        ];

        for (int i = 0; i < alpha.Length; i++)
        {
            double halo = Math.Clamp(blur[i] * 2.2 - alpha[i], 0, 1) * strength;
            if (halo <= 0) continue;

            double a = alpha[i];
            double merged = Math.Clamp(a + halo * (1 - a), 0, 1);
            if (merged <= 1e-6) continue;

            for (int c = 0; c < 3; c++)
                rgb[i * 3 + c] = (rgb[i * 3 + c] * a + tint[c] * halo * (1 - a)) / merged;

            alpha[i] = merged;
        }
    }

    private static byte Clamp(double v) => (byte)Math.Clamp(v, 0, 255);

    private static double[] Normalise(double[] v)
    {
        double len = Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
        return len < 1e-12 ? v : [v[0] / len, v[1] / len, v[2] / len];
    }
}
