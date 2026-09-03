using System.Text;
using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen;

/// <summary>
/// The sound tables every generated language is built from.
///
/// A word is a list of phoneme ids for as long as it is being assembled — roots, affixes and name
/// elements are all kept in this form and only spelled once, at the very end, by the language's
/// own <see cref="Orthography"/>. That is what lets two languages share a root and spell it
/// differently, and what lets a compound be repaired at the seam (a coda meeting an onset it
/// cannot precede) before anybody sees it.
/// </summary>
public static class Phonemes
{
    public enum Manner { Stop, Affricate, Fricative, Nasal, Liquid, Glide }

    /// <param name="Sonority">Rises from stops to glides; a syllable rises into its vowel and falls out of it.</param>
    public sealed record Consonant(string Id, Manner Manner, bool Voiced, bool Sibilant, int Sonority)
    {
        public bool Obstruent => Manner is Manner.Stop or Manner.Affricate or Manner.Fricative;
        public bool Sonorant => !Obstruent;
    }

    public sealed record Vowel(string Id, bool Front, bool Long, bool Diphthong)
    {
        /// <summary>The plain letter a long vowel or diphthong is written on.</summary>
        public string Base => Id[..1];
    }

    public static readonly IReadOnlyDictionary<string, Consonant> Consonants = new[]
    {
        C("p", Manner.Stop, false), C("b", Manner.Stop, true),
        C("t", Manner.Stop, false), C("d", Manner.Stop, true),
        C("k", Manner.Stop, false), C("g", Manner.Stop, true),
        C("q", Manner.Stop, false), C("'", Manner.Stop, false),
        C("ch", Manner.Affricate, false), C("j", Manner.Affricate, true),
        C("ts", Manner.Affricate, false), C("dz", Manner.Affricate, true),
        C("f", Manner.Fricative, false), C("v", Manner.Fricative, true),
        C("th", Manner.Fricative, false), C("dh", Manner.Fricative, true),
        C("s", Manner.Fricative, false, sibilant: true), C("z", Manner.Fricative, true, sibilant: true),
        C("sh", Manner.Fricative, false, sibilant: true), C("zh", Manner.Fricative, true, sibilant: true),
        C("kh", Manner.Fricative, false), C("gh", Manner.Fricative, true),
        C("h", Manner.Fricative, false),
        C("m", Manner.Nasal, true), C("n", Manner.Nasal, true),
        C("ng", Manner.Nasal, true), C("ny", Manner.Nasal, true),
        C("l", Manner.Liquid, true), C("r", Manner.Liquid, true),
        C("hl", Manner.Liquid, false), C("hr", Manner.Liquid, false),
        C("ll", Manner.Liquid, false), C("rh", Manner.Liquid, false),
        C("w", Manner.Glide, true), C("y", Manner.Glide, true),
    }.ToDictionary(c => c.Id, c => c);

    public static readonly IReadOnlyDictionary<string, Vowel> Vowels = new[]
    {
        V("a", false), V("e", true), V("i", true), V("o", false), V("u", false),
        // Front rounded (ü/y), ash (æ), oe (ø/ö) and schwa.
        V("ü", true), V("ae", true), V("oe", true), V("eh", true),
        V("aa", false, longV: true), V("ee", true, longV: true), V("ii", true, longV: true),
        V("oo", false, longV: true), V("uu", false, longV: true),
        V("ai", false, diph: true), V("au", false, diph: true), V("ei", true, diph: true),
        V("eu", true, diph: true), V("oi", false, diph: true), V("ou", false, diph: true),
        V("ia", true, diph: true), V("ie", true, diph: true), V("io", true, diph: true),
        V("ua", false, diph: true), V("ue", false, diph: true),
    }.ToDictionary(v => v.Id, v => v);

    /// <summary>Front vowels for harmony and for the c/k spelling rule.</summary>
    public static bool IsFront(string id) => Vowels.TryGetValue(id, out var v) && v.Front;

    public static bool IsVowel(string id) => Vowels.ContainsKey(id);
    public static bool IsConsonant(string id) => Consonants.ContainsKey(id);

    public static int Sonority(string id) => Consonants.TryGetValue(id, out var c) ? c.Sonority : 10;

    private static Consonant C(string id, Manner manner, bool voiced, bool sibilant = false)
        => new(id, manner, voiced, sibilant, manner switch
        {
            Manner.Stop => 1,
            Manner.Affricate => 2,
            Manner.Fricative => 3,
            Manner.Nasal => 4,
            Manner.Liquid => 5,
            _ => 6,
        });

    private static Vowel V(string id, bool front, bool longV = false, bool diph = false)
        => new(id, front, longV, diph);

    private static readonly string[] ByLength = Consonants.Keys.Concat(Vowels.Keys)
        .OrderByDescending(k => k.Length).ToArray();

    /// <summary>
    /// Reads the notation the flavour tables are written in: phoneme ids run together, longest
    /// match first, with a dot wherever two ids would otherwise be misread ("t.h" is t then h;
    /// "th" is the fricative). A hyphen marks a morpheme boundary and is kept as its own token.
    /// </summary>
    public static List<string> Parse(string notation)
    {
        var result = new List<string>();
        int i = 0;
        while (i < notation.Length)
        {
            char c = notation[i];
            if (c == '.') { i++; continue; }
            if (c == '-' || c == ' ') { result.Add(c.ToString()); i++; continue; }

            string? hit = null;
            foreach (string id in ByLength)
                if (string.CompareOrdinal(notation, i, id, 0, id.Length) == 0) { hit = id; break; }

            if (hit is null)
                throw new ArgumentException($"Unreadable phoneme at '{notation[i..]}' in \"{notation}\"");

            result.Add(hit);
            i += hit.Length;
        }
        return result;
    }
}

/// <summary>
/// One language's sound system: which phonemes it has, how often, what shapes a syllable may take,
/// which clusters are legal, and how a seam between two morphemes is mended.
/// </summary>
public sealed class Phonology
{
    public enum Template { V, CV, CVC, CCV, CCVC, VC, CVCC, CCVCC }

    [Flags]
    public enum OnsetFamily
    {
        None = 0,
        ObstruentLiquid = 1,   // pr, bl, tr, kr, fl, thr ...
        SStop = 2,             // sp, st, sk
        SNasal = 4,            // sm, sn
        SStopLiquid = 8,       // str, spr, skr
        ObstruentGlide = 16,   // kw, tw, dw, gw, sw
        Eastern = 32,          // vl, zd, dv, tv, sv, zv, mr, ml, vr, kn, gn, dl, hl
        NasalGlide = 64,       // my, ny, ky (the palatal series of a CV language)
        Prenasal = 128,        // mb, nd, ng, nj, nz (the prenasalised stops of a savanna language)
        Attic = 256,           // kt, pt, mn, gn, kn, ks, sf (the learned clusters of Greek)
        LabioVelar = 512,      // kw, gw (qu- and gu-)
    }

    [Flags]
    public enum CodaFamily
    {
        None = 0,
        NasalStop = 1,         // nt, nd, nk, mp
        LiquidObstruent = 2,   // rt, rd, rk, lt, ld, lk, rs, ls, lf, rf
        LiquidNasal = 4,       // rn, lm, rm
        SStop = 8,             // st, sk, sp
        FricativeStop = 16,    // ft, kt, pt, kht
    }

    public List<(string Id, double Weight)> Onsets { get; } = [];
    public List<(string Id, double Weight)> Codas { get; } = [];
    public List<(string[] Ids, double Weight)> OnsetClusters { get; } = [];
    public List<(string[] Ids, double Weight)> CodaClusters { get; } = [];
    public List<(string Id, double Weight)> Nuclei { get; } = [];
    public double[] Templates { get; } = new double[8];

    public int MaxBoundaryCluster { get; set; } = 2;
    public bool Hiatus { get; set; }
    public bool Gemination { get; set; }
    public bool Harmony { get; set; }
    public bool StopStopOk { get; set; }
    public double InitialVowelChance { get; set; } = 0.15;
    public double FinalOpenBias { get; set; } = 0.4;

    public Orthography Spelling { get; set; } = new();

    public bool Has(string id) => Onsets.Any(o => o.Id == id) || Codas.Any(c => c.Id == id) || Nuclei.Any(n => n.Id == id);

    // --- Building -----------------------------------------------------------------------------

    /// <summary>Realises a flavour as one concrete language, jittering every table so that two
    /// languages of the same flavour are cousins rather than twins.</summary>
    public static Phonology FromFlavour(LanguageFlavour f, Rng rng)
    {
        var p = new Phonology
        {
            MaxBoundaryCluster = f.MaxBoundaryCluster,
            Hiatus = f.Hiatus,
            Gemination = f.Gemination,
            Harmony = f.Harmony,
            StopStopOk = f.StopStopOk,
            InitialVowelChance = Jitter(f.InitialVowel, rng, 0.5),
            FinalOpenBias = Jitter(f.FinalOpen, rng, 0.4),
        };

        foreach (var (id, weight, core) in f.Consonants)
        {
            if (!core && !rng.Chance(0.55)) continue;
            double w = Jitter(weight, rng, 0.5);
            if (id != "ng") p.Onsets.Add((id, w));
            if (f.CodaSet.Contains(id)) p.Codas.Add((id, w));
        }

        // A coda-only phoneme (ng) still has to be in the codas.
        foreach (string id in f.CodaSet)
            if (!p.Codas.Any(c => c.Id == id) && f.Consonants.Any(c => c.Id == id && c.Core))
                p.Codas.Add((id, 1.0));

        foreach (var (id, weight, core) in f.Vowels)
        {
            if (!core && !rng.Chance(0.5)) continue;
            p.Nuclei.Add((id, Jitter(weight, rng, 0.5)));
        }

        for (int i = 0; i < 8; i++) p.Templates[i] = Jitter(f.Templates[i], rng, 0.4);

        p.BuildClusters(f.OnsetFamilies, f.CodaFamilies, f.ClusterKeep, rng);
        p.Spelling = Orthography.FromFlavour(f, rng);
        return p;
    }

    /// <summary>A copy, so a daughter language can shift sounds without touching its parent.</summary>
    public Phonology Clone()
    {
        var p = new Phonology
        {
            MaxBoundaryCluster = MaxBoundaryCluster,
            Hiatus = Hiatus,
            Gemination = Gemination,
            Harmony = Harmony,
            StopStopOk = StopStopOk,
            InitialVowelChance = InitialVowelChance,
            FinalOpenBias = FinalOpenBias,
            Spelling = Spelling.Clone(),
        };
        p.Onsets.AddRange(Onsets);
        p.Codas.AddRange(Codas);
        p.OnsetClusters.AddRange(OnsetClusters.Select(c => ((string[])c.Ids.Clone(), c.Weight)));
        p.CodaClusters.AddRange(CodaClusters.Select(c => ((string[])c.Ids.Clone(), c.Weight)));
        p.Nuclei.AddRange(Nuclei);
        Array.Copy(Templates, p.Templates, 8);
        return p;
    }

    /// <summary>Applies one sound change to the inventory: every table that named <paramref name="from"/>
    /// now names <paramref name="to"/>, merging weights where the target already existed.</summary>
    public void Shift(string from, string to)
    {
        static void Rename(List<(string Id, double Weight)> list, string from, string to)
        {
            int at = list.FindIndex(x => x.Id == from);
            if (at < 0) return;
            double w = list[at].Weight;
            list.RemoveAt(at);
            int target = list.FindIndex(x => x.Id == to);
            if (target >= 0) list[target] = (to, list[target].Weight + w);
            else list.Add((to, w));
        }

        static void RenameClusters(List<(string[] Ids, double Weight)> list, string from, string to)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var ids = list[i].Ids;
                for (int k = 0; k < ids.Length; k++) if (ids[k] == from) ids[k] = to;
                if (ids.Distinct().Count() != ids.Length) list.RemoveAt(i);
            }
        }

        Rename(Onsets, from, to);
        Rename(Codas, from, to);
        Rename(Nuclei, from, to);
        RenameClusters(OnsetClusters, from, to);
        RenameClusters(CodaClusters, from, to);
    }

    private void BuildClusters(OnsetFamily onsets, CodaFamily codas, double keep, Rng rng)
    {
        var present = Onsets.Select(o => o.Id).ToHashSet();
        var codaSet = Codas.Select(c => c.Id).ToHashSet();

        void Onset(string a, string b, string? c = null, double weight = 1.0)
        {
            if (!present.Contains(a) || !present.Contains(b) || (c is not null && !present.Contains(c))) return;
            if (!rng.Chance(keep)) return;
            OnsetClusters.Add((c is null ? [a, b] : [a, b, c], weight));
        }

        void Coda(string a, string b)
        {
            if (!codaSet.Contains(a) || !codaSet.Contains(b)) return;
            if (!rng.Chance(keep)) return;
            CodaClusters.Add(([a, b], 1.0));
        }

        string[] liquids = ["l", "r"];
        string[] obstruentsForLiquid = ["p", "b", "t", "d", "k", "g", "f", "th"];

        if (onsets.HasFlag(OnsetFamily.ObstruentLiquid))
            foreach (string o in obstruentsForLiquid)
                foreach (string l in liquids)
                {
                    // tl and dl are the clusters no European language tolerates at the start of a word.
                    if ((o is "t" or "d" or "th") && l == "l") continue;
                    Onset(o, l);
                }

        if (onsets.HasFlag(OnsetFamily.SStop)) { Onset("s", "p"); Onset("s", "t"); Onset("s", "k"); }
        if (onsets.HasFlag(OnsetFamily.SNasal)) { Onset("s", "m"); Onset("s", "n"); Onset("s", "l"); Onset("s", "w"); }
        if (onsets.HasFlag(OnsetFamily.SStopLiquid)) { Onset("s", "t", "r"); Onset("s", "p", "r"); Onset("s", "k", "r"); }

        if (onsets.HasFlag(OnsetFamily.ObstruentGlide))
        {
            Onset("t", "w"); Onset("d", "w"); Onset("s", "w"); Onset("th", "w");
        }

        if (onsets.HasFlag(OnsetFamily.LabioVelar)) { Onset("k", "w"); Onset("g", "w"); }

        if (onsets.HasFlag(OnsetFamily.Attic))
        {
            // Learned rather than common: a Ptolemy for every twenty Philips.
            foreach (var (a, b) in new[] { ("k", "t"), ("p", "t"), ("m", "n"), ("g", "n"), ("k", "n"), ("k", "s"), ("s", "f"), ("p", "s") })
                Onset(a, b, weight: 0.3);
        }

        if (onsets.HasFlag(OnsetFamily.Eastern))
        {
            Onset("v", "l"); Onset("v", "r"); Onset("z", "d"); Onset("z", "l"); Onset("z", "v"); Onset("z", "n");
            Onset("d", "v"); Onset("t", "v"); Onset("s", "v"); Onset("m", "r"); Onset("m", "l");
            Onset("k", "n"); Onset("g", "n"); Onset("d", "l"); Onset("t", "l"); Onset("h", "l"); Onset("h", "r");
            Onset("kh", "r"); Onset("kh", "l"); Onset("g", "d"); Onset("k", "t"); Onset("p", "t");
        }

        if (onsets.HasFlag(OnsetFamily.NasalGlide))
        {
            Onset("m", "y"); Onset("n", "y"); Onset("k", "y"); Onset("g", "y"); Onset("r", "y"); Onset("h", "y"); Onset("b", "y");
        }

        if (onsets.HasFlag(OnsetFamily.Prenasal))
        {
            Onset("m", "b"); Onset("n", "d"); Onset("n", "g"); Onset("n", "j"); Onset("n", "z"); Onset("m", "p"); Onset("n", "t"); Onset("n", "k");
        }

        if (codas.HasFlag(CodaFamily.NasalStop))
        {
            Coda("n", "t"); Coda("n", "d"); Coda("n", "k"); Coda("n", "g"); Coda("m", "p"); Coda("m", "b");
            Coda("n", "s"); Coda("n", "th"); Coda("n", "ch");
        }

        if (codas.HasFlag(CodaFamily.LiquidObstruent))
        {
            Coda("r", "t"); Coda("r", "d"); Coda("r", "k"); Coda("r", "g"); Coda("r", "s"); Coda("r", "f");
            Coda("r", "th"); Coda("r", "sh"); Coda("l", "t"); Coda("l", "d"); Coda("l", "k"); Coda("l", "s");
            Coda("l", "f"); Coda("l", "th"); Coda("l", "p"); Coda("r", "p"); Coda("r", "b");
        }

        if (codas.HasFlag(CodaFamily.LiquidNasal)) { Coda("r", "n"); Coda("r", "m"); Coda("l", "m"); Coda("l", "n"); }
        if (codas.HasFlag(CodaFamily.SStop)) { Coda("s", "t"); Coda("s", "k"); Coda("s", "p"); Coda("sh", "t"); }
        if (codas.HasFlag(CodaFamily.FricativeStop)) { Coda("f", "t"); Coda("k", "t"); Coda("p", "t"); Coda("kh", "t"); Coda("k", "s"); }
    }

    private static double Jitter(double value, Rng rng, double spread)
        => value * (1.0 - spread + rng.Double() * 2.0 * spread);

    // --- Generating ----------------------------------------------------------------------------

    /// <summary>A word of the given syllable count, as phoneme ids, obeying every rule above.</summary>
    public List<string> Word(Rng rng, int syllables, bool? frontHarmony = null)
    {
        bool? harmony = Harmony ? (frontHarmony ?? rng.Chance(0.5)) : null;
        var word = new List<string>();

        for (int i = 0; i < syllables; i++)
        {
            bool first = i == 0;
            bool last = i == syllables - 1;
            var syllable = Syllable(rng, first, last, harmony, word);
            word = Join(word, syllable, rng);
        }

        return word;
    }

    /// <summary>A short, light word: the shape of an affix or a name element.</summary>
    public List<string> Element(Rng rng, bool? frontHarmony = null)
        => Word(rng, rng.Chance(0.72) ? 1 : 2, frontHarmony);

    /// <summary>One plain syllable, onset-vowel or onset-vowel-coda, no clusters: the shape of a
    /// grammatical suffix, which is never the heaviest word in the sentence.</summary>
    public List<string> Simple(Rng rng)
    {
        var syllable = new List<string> { PickWeighted(Onsets, rng), PickWeighted(Nuclei, rng) };
        if (Codas.Count > 0 && rng.Chance(0.5)) syllable.Add(PickWeighted(Codas, rng));
        return syllable;
    }

    private List<string> Syllable(Rng rng, bool first, bool last, bool? harmony, List<string> soFar)
    {
        var template = PickTemplate(rng, first, last, soFar);
        var syllable = new List<string>();

        bool hasOnset = template is not (Template.V or Template.VC);
        bool doubleOnset = template is Template.CCV or Template.CCVC or Template.CCVCC;
        bool hasCoda = template is Template.CVC or Template.CCVC or Template.VC or Template.CVCC or Template.CCVCC;
        bool doubleCoda = template is Template.CVCC or Template.CCVCC;

        if (last && hasCoda && rng.Chance(FinalOpenBias)) { hasCoda = false; doubleCoda = false; }
        if (!last && doubleCoda && rng.Chance(0.6)) doubleCoda = false;

        if (hasOnset)
        {
            if (doubleOnset && OnsetClusters.Count > 0) syllable.AddRange(PickCluster(OnsetClusters, rng));
            else syllable.Add(PickWeighted(Onsets, rng));
        }

        syllable.Add(PickNucleus(rng, harmony));

        if (hasCoda)
        {
            if (doubleCoda && CodaClusters.Count > 0) syllable.AddRange(PickCluster(CodaClusters, rng));
            else if (Codas.Count > 0) syllable.Add(PickWeighted(Codas, rng));
        }

        return syllable;
    }

    private Template PickTemplate(Rng rng, bool first, bool last, List<string> soFar)
    {
        Span<double> weights = stackalloc double[8];
        double total = 0;

        bool previousOpen = soFar.Count > 0 && Phonemes.IsVowel(soFar[^1]);

        for (int i = 0; i < 8; i++)
        {
            var t = (Template)i;
            double w = Templates[i];
            bool vowelInitial = t is Template.V or Template.VC;

            if (vowelInitial)
            {
                if (first) w *= InitialVowelChance * 4;
                else if (previousOpen && !Hiatus) w = 0;
                else if (previousOpen) w *= 0.5;
                else w *= 0.6;
            }

            weights[i] = w;
            total += w;
        }

        if (total <= 0) return Template.CV;

        double roll = rng.Double() * total;
        for (int i = 0; i < 8; i++)
        {
            roll -= weights[i];
            if (roll < 0) return (Template)i;
        }
        return Template.CV;
    }

    private string PickNucleus(Rng rng, bool? harmony)
    {
        if (harmony is null) return PickWeighted(Nuclei, rng);

        // Neutral vowels (i, e, ii, ee) belong to both classes, as in Finnish.
        var pool = Nuclei.Where(n => harmony.Value
            ? Phonemes.IsFront(n.Id)
            : !Phonemes.IsFront(n.Id) || n.Id is "i" or "e" or "ii" or "ee").ToList();

        return pool.Count > 0 ? PickWeighted(pool, rng) : PickWeighted(Nuclei, rng);
    }

    private static string PickWeighted(List<(string Id, double Weight)> items, Rng rng)
    {
        double total = 0;
        foreach (var item in items) total += item.Weight;
        double roll = rng.Double() * total;
        foreach (var item in items)
        {
            roll -= item.Weight;
            if (roll < 0) return item.Id;
        }
        return items[^1].Id;
    }

    private static string[] PickCluster(List<(string[] Ids, double Weight)> items, Rng rng)
    {
        double total = 0;
        foreach (var item in items) total += item.Weight;
        double roll = rng.Double() * total;
        foreach (var item in items)
        {
            roll -= item.Weight;
            if (roll < 0) return item.Ids;
        }
        return items[^1].Ids;
    }

    // --- Seams ---------------------------------------------------------------------------------

    /// <summary>
    /// Concatenates two phoneme strings and mends the seam. Used between syllables of one root and
    /// between the morphemes of a compound alike, so a place name built from three pieces obeys the
    /// same rules as a root drawn in one go.
    ///
    /// The rules, in order: two vowels meeting are resolved unless the language allows hiatus; a
    /// consonant run longer than the language tolerates is trimmed from the left, because the
    /// onset of the next syllable is what the ear holds on to; and a coda that cannot precede the
    /// onset it meets (rising sonority that is not itself a legal onset) is dropped.
    /// </summary>
    public List<string> Join(List<string> left, List<string> right, Rng rng)
    {
        if (left.Count == 0) return [.. right];
        if (right.Count == 0) return [.. left];

        var result = new List<string>(left);
        var tail = new List<string>(right);

        // Vowel against vowel.
        if (Phonemes.IsVowel(result[^1]) && Phonemes.IsVowel(tail[0]))
        {
            string a = result[^1], b = tail[0];
            bool heavy = Phonemes.Vowels[a].Long || Phonemes.Vowels[a].Diphthong
                      || Phonemes.Vowels[b].Long || Phonemes.Vowels[b].Diphthong;

            if (a == b || heavy || !Hiatus)
            {
                string? glide = Phonemes.IsFront(a) && Has("y") ? "y" : !Phonemes.IsFront(a) && Has("w") ? "w" : null;

                if (a == b || heavy || glide is null || rng.Chance(0.5))
                    result.RemoveAt(result.Count - 1);      // the left vowel yields: "Aldo" + "ard" → "Aldard"
                else
                    result.Add(glide);                       // or a glide bridges them: "Ari" + "el" → "Ariyel"
            }

            result.AddRange(tail);
            return result;
        }

        // Consonant run across the seam.
        int leftRun = 0;
        for (int i = result.Count - 1; i >= 0 && Phonemes.IsConsonant(result[i]); i--) leftRun++;
        int rightRun = 0;
        for (int i = 0; i < tail.Count && Phonemes.IsConsonant(tail[i]); i++) rightRun++;

        // A whole-consonant piece (rare: an affix like "-s") is glued as-is and trimmed below.
        if (leftRun == result.Count || rightRun == tail.Count)
        {
            result.AddRange(tail);
            return Trim(result);
        }

        if (leftRun == 0 || rightRun == 0)
        {
            result.AddRange(tail);
            return result;
        }

        // Trim from the left until the run fits.
        while (leftRun + rightRun > MaxBoundaryCluster && leftRun > 0)
        {
            result.RemoveAt(result.Count - 1);
            leftRun--;
        }
        while (leftRun + rightRun > MaxBoundaryCluster && rightRun > 1)
        {
            tail.RemoveAt(0);
            rightRun--;
        }

        // Three across a seam only in the shapes real words have: a sonorant or sibilant coda
        // before a legal two-consonant onset ("n.dr", "s.tr"), or a coda cluster whose last member
        // makes a legal onset with what follows ("s.t.r"). "l.n.g" is nobody's word.
        if (leftRun > 0 && leftRun + rightRun == 3)
        {
            bool ok;
            if (leftRun == 1)
            {
                var a = Phonemes.Consonants[result[^1]];
                ok = a.Sonorant || a.Sibilant;
            }
            else
            {
                string mid = result[^1], first = tail[0];
                var m = Phonemes.Consonants[mid];
                ok = (m.Sibilant && Phonemes.Consonants[first].Manner == Phonemes.Manner.Stop)
                  || OnsetClusters.Any(k => k.Ids.Length == 2 && k.Ids[0] == mid && k.Ids[1] == first);
            }

            if (!ok) { result.RemoveAt(result.Count - 1); leftRun--; }
        }

        if (leftRun > 0)
        {
            string c1 = result[^1];
            string c2 = tail[0];

            if (c1 == c2)
            {
                if (!Gemination) result.RemoveAt(result.Count - 1);
            }
            else if (!Legal(c1, c2, rightRun))
            {
                result.RemoveAt(result.Count - 1);
            }
            else
            {
                // Nasal assimilation: "n" before a labial is "m", "m" before a dental or velar is "n".
                if (c1 == "n" && c2 is "p" or "b") result[^1] = Has("m") ? "m" : "n";
                else if (c1 == "m" && c2 is "t" or "d" or "k" or "g" or "s") result[^1] = "n";
            }
        }

        result.AddRange(tail);
        return result;
    }

    /// <summary>Whether the coda <paramref name="c1"/> may stand before an onset beginning with <paramref name="c2"/>.</summary>
    private bool Legal(string c1, string c2, int onsetLength)
    {
        var a = Phonemes.Consonants[c1];
        var b = Phonemes.Consonants[c2];

        if (c1 == "h" || c1 == "'" || c1 == "w" || c1 == "y") return false;
        if (c2 == "'" ) return false;

        // Falling sonority across the seam is the natural case: "n.t", "r.k", "l.d", "s.t".
        if (a.Sonority > b.Sonority && a.Sonorant) return true;
        if (a.Sibilant && b.Manner is Phonemes.Manner.Stop or Phonemes.Manner.Nasal or Phonemes.Manner.Liquid) return true;

        // Rising sonority is fine when the pair is itself a legal onset ("t.r" → "tr").
        if (onsetLength == 1 && OnsetClusters.Any(k => k.Ids.Length == 2 && k.Ids[0] == c1 && k.Ids[1] == c2)) return true;

        if (a.Manner == Phonemes.Manner.Stop && b.Manner == Phonemes.Manner.Stop) return StopStopOk;
        if (a.Manner == Phonemes.Manner.Fricative && b.Manner == Phonemes.Manner.Stop) return true;   // "f.t", "kh.t"
        if (a.Manner == Phonemes.Manner.Stop && b.Manner == Phonemes.Manner.Fricative) return StopStopOk; // "k.s" (x)
        if (a.Manner == Phonemes.Manner.Stop && b.Manner == Phonemes.Manner.Nasal) return StopStopOk;    // "t.n"

        return false;
    }

    /// <summary>Reduces any consonant run inside a finished word to the language's limit.</summary>
    public List<string> Trim(List<string> word)
    {
        var result = new List<string>(word.Count);
        int run = 0;
        foreach (string id in word)
        {
            if (Phonemes.IsConsonant(id))
            {
                if (++run > MaxBoundaryCluster) continue;
            }
            else run = 0;
            result.Add(id);
        }
        return result;
    }

    /// <summary>Syllable count, as a reader would hear it.</summary>
    public static int Syllables(List<string> word) => word.Count(Phonemes.IsVowel);
}

/// <summary>
/// How a language writes its sounds. Chosen once per language from the alternatives its flavour
/// allows, so that /k/ is "c" everywhere in one tongue and "k" everywhere in its neighbour.
/// </summary>
public sealed class Orthography
{
    public enum LongStyle { Plain, Double, Macron, Acute, Circumflex, H }

    public Dictionary<string, string> Map { get; } = new(StringComparer.Ordinal);
    public LongStyle Long { get; set; } = LongStyle.Plain;

    /// <summary>What /k/ becomes before a front vowel when the plain spelling is "c" (k, qu, ch).</summary>
    public string KFront { get; set; } = "k";

    /// <summary>What /g/ becomes before a front vowel in the same situation (gh, gu).</summary>
    public string GFront { get; set; } = "g";

    public bool DoubleGeminates { get; set; } = true;

    /// <summary>A short vowel followed by /k/ at the end is written "ck" — the Germanic look.</summary>
    public bool CkFinal { get; set; }

    public static Orthography FromFlavour(LanguageFlavour f, Rng rng)
    {
        var o = new Orthography
        {
            Long = rng.Pick(f.LongVowelStyles),
            KFront = rng.Pick(f.KFrontOptions),
            GFront = rng.Pick(f.GFrontOptions),
            DoubleGeminates = f.Gemination,
            CkFinal = f.CkFinal && rng.Chance(0.5),
        };

        foreach (var (id, options) in f.Spelling)
            o.Map[id] = rng.Pick(options);

        return o;
    }

    public Orthography Clone()
    {
        var o = new Orthography { Long = Long, KFront = KFront, GFront = GFront, DoubleGeminates = DoubleGeminates, CkFinal = CkFinal };
        foreach (var (k, v) in Map) o.Map[k] = v;
        return o;
    }

    /// <summary>Re-rolls one spelling from the flavour's alternatives — a dialect's worth of drift.</summary>
    public void Drift(LanguageFlavour f, Rng rng)
    {
        var keys = f.Spelling.Where(kv => kv.Value.Length > 1).Select(kv => kv.Key).ToList();
        if (keys.Count == 0) return;
        string key = rng.Pick(keys);
        Map[key] = rng.Pick(f.Spelling[key]);
    }

    public string Spell(List<string> ids)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < ids.Count; i++)
        {
            string id = ids[i];
            string? next = i + 1 < ids.Count ? ids[i + 1] : null;
            string? prev = i > 0 ? ids[i - 1] : null;

            if (id is "-" or " ") { sb.Append(id); continue; }

            // Geminates: written once unless the language doubles them.
            if (prev == id && Phonemes.IsConsonant(id))
            {
                if (DoubleGeminates)
                {
                    string once = SpellConsonant(id, next, prev: null);
                    sb.Append(once.Length == 1 ? once : once[0]);
                }
                continue;
            }

            // A pair with its own letter: "kw" as "qu", "ks" as "x".
            if (next is not null && Map.TryGetValue(id + next, out var pair))
            {
                sb.Append(pair);
                i++;
                continue;
            }

            if (Phonemes.IsVowel(id)) sb.Append(SpellVowel(id));
            else sb.Append(SpellConsonant(id, next, prev));
        }

        return Capitalise(Tidy(sb.ToString()));
    }

    private string SpellConsonant(string id, string? next, string? prev)
    {
        string s = Map.TryGetValue(id, out var m) ? m : id;

        bool frontNext = next is not null && Phonemes.IsFront(next);

        if (id == "k")
        {
            if (s == "c" && frontNext) return KFront;
            if (CkFinal && next is null && prev is not null && Phonemes.IsVowel(prev)
                && !Phonemes.Vowels[prev].Long && !Phonemes.Vowels[prev].Diphthong)
                return "ck";
        }

        if (id == "g" && s == "g" && frontNext && GFront != "g") return GFront;

        // A glottal stop at either end of a word is silent on the page.
        if (id == "'" && (prev is null || next is null)) return "";

        return s;
    }

    private string SpellVowel(string id)
    {
        if (Map.TryGetValue(id, out var m)) return m;

        var v = Phonemes.Vowels[id];
        if (!v.Long) return id;

        string b = v.Base;
        return Long switch
        {
            LongStyle.Double => b + b,
            LongStyle.Macron => b switch { "a" => "ā", "e" => "ē", "i" => "ī", "o" => "ō", _ => "ū" },
            LongStyle.Acute => b switch { "a" => "á", "e" => "é", "i" => "í", "o" => "ó", _ => "ú" },
            LongStyle.Circumflex => b switch { "a" => "â", "e" => "ê", "i" => "î", "o" => "ô", _ => "û" },
            LongStyle.H => b + "h",
            _ => b,
        };
    }

    /// <summary>No letter three times running, whatever the tables produced.</summary>
    private static string Tidy(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            if (sb.Length >= 2 && sb[^1] == c && sb[^2] == c) continue;
            sb.Append(c);
        }
        return sb.ToString();
    }

    public static string Capitalise(string word)
    {
        if (word.Length == 0) return word;
        var chars = word.ToCharArray();
        bool start = true;
        for (int i = 0; i < chars.Length; i++)
        {
            if (start && char.IsLetter(chars[i]))
            {
                // Dotless i has no invariant upper case; Turkish writes it as a plain I.
                chars[i] = chars[i] == 'ı' ? 'I' : char.ToUpperInvariant(chars[i]);
                start = false;
            }
            if (chars[i] is '-' or ' ') start = true;
        }
        return new string(chars);
    }
}
