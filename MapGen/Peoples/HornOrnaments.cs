using V = Ck3MapGen.MapGen.OrcTusks.V;

namespace Ck3MapGen.MapGen;

/// <summary>
/// Metal ornaments on horns — thin rings, a wide band with raised lips, or a cap over the tip (on
/// nubs) or over the cut (on filed stumps) — worn by the ranked men and women of horned cultures.
///
/// **One rule fits every horn type.** An ornament sits at a fraction of its horn's own VISIBLE
/// length (0 at the skin, 1 at the tip), on that horn's own centreline and at its own thickness
/// there, so a ring hugs an ibex horn, a ram's spiral and a nub alike. Each style only carries its
/// positions (<see cref="Places"/>). Designed from renders with the user 2026-09-29
/// (<c>Desktop/ck3devtools/horn_rings/rings.py</c>, which this ports).
///
/// **Kinds per style.** Rings and bands suit the long horns; a tip cap only shows on nubs and on the
/// stump (long tips sweep back out of the portrait views), and one thin ring is lost on a stubby
/// horn. So a culture's chosen kind becomes, per style, the nearest that fits (<see cref="ShapeOn"/>).
///
/// **Metal is a texture, not geometry**: one mesh per style and shape, one entity per metal.
/// </summary>
internal static partial class Horns
{
    /// <summary>The accessory gene (generated beside <see cref="Gene"/> in common/genes/gen_horns.txt).</summary>
    public const string OrnamentGene = "gen_horn_ornaments";
    public const string OrnamentNoneTemplate = "gen_horn_ornaments_none";

    /// <summary>What a culture picks. The mesh on a given style is <see cref="ShapeOn"/>.</summary>
    public static readonly string[] OrnamentKinds = ["rings", "band", "cap"];

    /// <summary>Metals, each a diffuse over the same shiny metal properties.</summary>
    public static readonly (string Name, byte R, byte G, byte B)[] Metals =
    [
        ("gold", 221, 188, 131),      // vanilla's gold circlet, measured
        ("bronze", 196, 136, 84),
        ("silver", 208, 208, 214),
    ];

    public static string OrnamentTemplateOf(string kind, string metal) => $"gen_horn_orn_{kind}_{metal}";

    /// <summary>The portrait tag a horn accessory sets naming its style, read by the ornaments.</summary>
    public static string StyleTag(string style) => $"gen_horns_style_{style}";

    /// <summary>The ornament shape a culture's kind becomes on one horn style.</summary>
    public static string ShapeOn(string style, string kind)
    {
        bool stubby = style is "nubs" or Filed;
        return kind switch
        {
            "rings" => stubby ? "band" : "rings",
            "cap" => stubby ? "cap" : "band",
            _ => "band",
        };
    }

    /// <summary>Every (style, shape) mesh that <see cref="ShapeOn"/> can name.</summary>
    public static IEnumerable<(string Style, string Shape)> OrnamentMeshes() =>
        Styles.SelectMany(s => OrnamentKinds.Select(k => (s, ShapeOn(s, k)))).Distinct();

    private sealed record Place(double[] Rings, double Band0, double Band1, double Cap);

    /// <summary>Fractions of the visible horn: ring centres, the band's span, the cap's share of the end.</summary>
    private static readonly Dictionary<string, Place> Places = new()
    {
        ["ibex"] = new([0.26, 0.36], 0.22, 0.34, 0.10),
        ["forward"] = new([0.28, 0.40], 0.24, 0.36, 0.12),
        ["ram"] = new([0.20, 0.30], 0.16, 0.28, 0.09),
        ["nubs"] = new([0.45], 0.30, 0.60, 0.35),
        [Filed] = new([0.60], 0.40, 0.80, 0.30),
    };

    private const double RingTube = 0.16;   // thin ring's own radius
    private const double BandPad = 0.10;    // a band stands this far off the horn
    private const double Lip = 0.09;        // band and cap edge lips
    private const double CapPad = 0.07;
    private const int OrnSeg = 24, OrnSides = 8, OrnSteps = 10;

    /// <summary>One style's ornament shape on both horns, as streams for a mesh node.</summary>
    public static Tube Ornament(string style, string shape, List<Root> roots, double scale, float[] headP, float[] headN, bool flip)
    {
        var p = new List<float>(); var n = new List<float>(); var ta = new List<float>();
        var uv = new List<float>(); var tri = new List<int>(); var sideOf = new List<int>();

        foreach (var root in roots)
        {
            var spine = Spine(style, root, scale, headP, headN);
            var ring = Ring(style, spine, scale).Pos;
            var radii = Enumerable.Range(0, spine.Count)
                .Select(i => Math.Sqrt((ring[i * (Segments + 1)] - spine[i].Point).LengthSquared)).ToArray();

            var part = new Part();
            double length = spine[^1].S, skin = Bury * scale;
            double S(double f) => skin + f * (length - skin);
            var place = Places[style];

            switch (shape)
            {
                case "rings":
                    foreach (double f in place.Rings)
                    {
                        var (c, t, r) = At(spine, radii, S(f));
                        part.Torus(c, t, r + RingTube * 0.4 * scale, RingTube * scale);
                    }
                    break;
                case "band":
                    part.Sleeve(spine, radii, S(place.Band0), S(place.Band1), BandPad * scale, closeTip: false);
                    foreach (double f in new[] { place.Band0, place.Band1 })
                    {
                        var (c, t, r) = At(spine, radii, S(f));
                        part.Torus(c, t, r + BandPad * scale, Lip * scale);
                    }
                    break;
                default:    // cap
                {
                    double f0 = 1 - place.Cap;
                    if (style == Filed)
                    {
                        // A cover over the cut: a sleeve up past the end, closed flat.
                        part.Sleeve(spine, radii, S(f0), length - 1e-3, CapPad * scale, closeTip: false);
                        var (c, t, r) = At(spine, radii, length - 1e-3);
                        part.Disc(c + t * (0.18 * scale), t, r + CapPad * scale, 0.05 * scale);
                    }
                    else
                    {
                        part.Sleeve(spine, radii, S(f0), length - 1e-3, CapPad * scale, closeTip: true);
                    }

                    var (c0, t0, r0) = At(spine, radii, S(f0));
                    part.Torus(c0, t0, r0 + CapPad * scale, Lip * scale);
                    break;
                }
            }

            // Wind each face to agree with its outward normals, then match the head's convention.
            var t2 = part.Tri;
            for (int i = 0; i + 2 < t2.Count; i += 3)
            {
                V fn = V.Cross(part.Pos[t2[i + 1]] - part.Pos[t2[i]], part.Pos[t2[i + 2]] - part.Pos[t2[i]]);
                bool agrees = V.Dot(fn, part.Nrm[t2[i]] + part.Nrm[t2[i + 1]] + part.Nrm[t2[i + 2]]) >= 0;
                if (agrees == flip) (t2[i + 1], t2[i + 2]) = (t2[i + 2], t2[i + 1]);
            }

            int offset = p.Count / 3;
            tri.AddRange(t2.Select(i => i + offset));
            foreach (var v in part.Pos) p.AddRange([(float)v.X, (float)v.Y, (float)v.Z]);
            foreach (var v in part.Nrm) n.AddRange([(float)v.X, (float)v.Y, (float)v.Z]);
            foreach (var v in part.Tan) ta.AddRange([(float)v.X, (float)v.Y, (float)v.Z, 1f]);
            foreach (var (u, vv) in part.Uv) uv.AddRange([u, vv]);
            sideOf.AddRange(Enumerable.Repeat(root.Side > 0 ? 0 : 1, part.Pos.Count));
        }

        return new Tube([.. p], [.. n], [.. ta], [.. uv], [.. tri], [.. sideOf]);
    }

    /// <summary>Point, tangent and horn radius at arc length <paramref name="s"/>.</summary>
    private static (V C, V T, double R) At(List<(V Point, double S)> spine, double[] radii, double s)
    {
        int i = 1;
        while (i < spine.Count - 1 && spine[i].S < s) i++;
        var (p0, s0) = spine[i - 1];
        var (p1, s1) = spine[i];
        double u = s1 == s0 ? 0 : Math.Clamp((s - s0) / (s1 - s0), 0, 1);
        return (p0 + (p1 - p0) * u, (p1 - p0).Normalized, radii[i - 1] + (radii[i] - radii[i - 1]) * u);
    }

    private static (V A, V B) Frame(V t)
    {
        V a = (Math.Abs(t.Y) < 0.95 ? V.Cross(t, new V(0, 1, 0)) : V.Cross(t, new V(1, 0, 0))).Normalized;
        return (a, V.Cross(t, a));
    }

    /// <summary>Ornament geometry for one horn, built piece by piece.</summary>
    private sealed class Part
    {
        public readonly List<V> Pos = [], Nrm = [], Tan = [];
        public readonly List<(float, float)> Uv = [];
        public readonly List<int> Tri = [];

        public void Torus(V c, V t, double major, double minor)
        {
            var (a, b) = Frame(t);
            int off = Pos.Count;
            for (int i = 0; i < OrnSeg; i++)
            {
                double u = 2 * Math.PI * i / OrnSeg;
                V d = a * Math.Cos(u) + b * Math.Sin(u);
                for (int j = 0; j < OrnSides; j++)
                {
                    double w = 2 * Math.PI * j / OrnSides;
                    V nrm = d * Math.Cos(w) + t * Math.Sin(w);
                    Pos.Add(c + d * major + nrm * minor);
                    Nrm.Add(nrm);
                    Tan.Add(V.Cross(t, d));
                    Uv.Add(((float)i / OrnSeg, (float)j / OrnSides));
                }
            }

            for (int i = 0; i < OrnSeg; i++)
                for (int j = 0; j < OrnSides; j++)
                {
                    int a0 = off + i * OrnSides + j, a1 = off + i * OrnSides + (j + 1) % OrnSides;
                    int b0 = off + (i + 1) % OrnSeg * OrnSides + j, b1 = off + (i + 1) % OrnSeg * OrnSides + (j + 1) % OrnSides;
                    Tri.AddRange([a0, b0, a1, a1, b0, b1]);
                }
        }

        public void Sleeve(List<(V Point, double S)> spine, double[] radii, double s0, double s1, double pad, bool closeTip)
        {
            int off = Pos.Count;
            V lastT = default, lastC = default;
            for (int k = 0; k <= OrnSteps; k++)
            {
                var (c, t, r) = At(spine, radii, s0 + (s1 - s0) * k / OrnSteps);
                var (a, b) = Frame(t);
                for (int i = 0; i < OrnSeg; i++)
                {
                    double u = 2 * Math.PI * i / OrnSeg;
                    V d = a * Math.Cos(u) + b * Math.Sin(u);
                    Pos.Add(c + d * (r + pad));
                    Nrm.Add(d);
                    Tan.Add(t);
                    Uv.Add(((float)i / OrnSeg, (float)k / OrnSteps));
                }

                (lastC, lastT) = (c, t);
            }

            for (int k = 0; k < OrnSteps; k++)
                for (int i = 0; i < OrnSeg; i++)
                {
                    int a0 = off + k * OrnSeg + i, a1 = off + k * OrnSeg + (i + 1) % OrnSeg, b0 = a0 + OrnSeg, b1 = a1 + OrnSeg;
                    Tri.AddRange([a0, b0, a1, a1, b0, b1]);
                }

            if (!closeTip) return;
            int tip = Pos.Count, last = off + OrnSteps * OrnSeg;
            Pos.Add(lastC + lastT * pad); Nrm.Add(lastT); Tan.Add(Frame(lastT).A); Uv.Add((0.5f, 1f));
            for (int i = 0; i < OrnSeg; i++) Tri.AddRange([last + i, tip, last + (i + 1) % OrnSeg]);
        }

        /// <summary>A flat disc facing <paramref name="t"/>, domed slightly at its centre.</summary>
        public void Disc(V c, V t, double radius, double dome)
        {
            var (a, b) = Frame(t);
            int off = Pos.Count;
            for (int i = 0; i < OrnSeg; i++)
            {
                double u = 2 * Math.PI * i / OrnSeg;
                V d = a * Math.Cos(u) + b * Math.Sin(u);
                Pos.Add(c + d * radius); Nrm.Add(t); Tan.Add(a);
                Uv.Add((0.5f + 0.5f * (float)Math.Cos(u), 0.5f + 0.5f * (float)Math.Sin(u)));
            }

            int centre = Pos.Count;
            Pos.Add(c + t * dome); Nrm.Add(t); Tan.Add(a); Uv.Add((0.5f, 0.5f));
            for (int i = 0; i < OrnSeg; i++) Tri.AddRange([off + i, centre, off + (i + 1) % OrnSeg]);
        }
    }

    /// <summary>
    /// A metal's diffuse, BGRA top row first (64 × 64): the metal's colour with fine brushed streaks
    /// and a faint engraved line every eighth column. Deterministic hash, like the keratin.
    /// </summary>
    public static (int Width, int Height, byte[] Bgra) MetalDiffuse(byte r, byte g, byte b)
    {
        const int w = 64, h = 64;
        var bgra = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                uint z = (uint)y * 2654435761u ^ 0x85EBCA6Bu;
                z ^= z >> 15; z *= 0x27D4EB2Du; z ^= z >> 13;
                double k = 0.94 + 0.10 * (z / (double)uint.MaxValue);          // brushed along v
                if (x % 8 == 0) k *= 0.80;                                       // engraved groove
                int o = (y * w + x) * 4;
                bgra[o] = Byte(b * k); bgra[o + 1] = Byte(g * k); bgra[o + 2] = Byte(r * k); bgra[o + 3] = 255;
            }

        return (w, h, bgra);
    }

    /// <summary>
    /// The metal properties, BGRA (64 × 64). Channels as vanilla's gold circlet, measured: R 0,
    /// G 65, B 255 (fully metallic), A the roughness — the circlet averages 103; ours sits near 70 for
    /// a polished shine, the grooves a little rougher.
    /// </summary>
    public static (int Width, int Height, byte[] Bgra) MetalProperties()
    {
        const int w = 64, h = 64;
        var bgra = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                uint z = (uint)(y * 131 + x * 7) * 2654435761u;
                z ^= z >> 16;
                int rough = 64 + (int)(z % 14) + (x % 8 == 0 ? 30 : 0);
                int o = (y * w + x) * 4;
                bgra[o] = 255; bgra[o + 1] = 65; bgra[o + 2] = 0; bgra[o + 3] = (byte)rough;
            }

        return (w, h, bgra);
    }
}
