using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen;

/// <summary>
/// One language's stock of morphemes: a root for every concept a place or a person is named
/// from, the elements its personal names are compounded of, the place-words each tier of title
/// ends in, and the grammar words (patronymic, "of") its name lists need.
///
/// Everything is held as phoneme ids, never as text, so that a daughter language can inherit the
/// whole stock and shift a sound through all of it at once — which is how two heritages come out
/// related rather than merely neighbouring.
///
/// The point of a lexicon over a random affix is reuse: real maps are full of the same twenty
/// place-words, and a region where every fourth village ends in the same "-ford" reads as one
/// country. Random syllables cannot do that, because nothing ever recurs.
/// </summary>
public sealed class Lexicon
{
    public static readonly string[] Nature =
    [
        "hill", "mountain", "valley", "river", "ford", "lake", "sea", "marsh", "wood", "field",
        "stone", "rock", "spring", "island", "cliff", "meadow", "moor", "bay", "mouth", "ridge",
    ];

    public static readonly string[] Built =
    [
        "fort", "town", "farm", "home", "bridge", "wall", "tower", "temple", "market", "mill",
        "harbour", "road", "gate", "hall",
    ];

    public static readonly string[] Quality =
    [
        "high", "low", "new", "old", "black", "white", "red", "green", "grey", "great", "little",
        "long", "broad", "cold", "holy", "fair", "wild", "bright", "dark", "middle",
    ];

    public static readonly string[] Compass = ["east", "west", "north", "south"];

    public static readonly string[] People = ["king", "folk", "lord", "priest", "warrior"];

    public static readonly string[] Creature =
        ["wolf", "bear", "boar", "raven", "eagle", "stag", "horse", "hawk", "serpent"];

    public static readonly string[] Thing =
        ["oak", "ash", "thorn", "iron", "gold", "silver", "fire", "storm", "sun", "moon", "star", "sword", "spear", "shield"];

    public static readonly string[] Virtue =
        ["peace", "war", "gift", "friend", "guard", "glory", "wisdom", "victory", "strength", "love", "blessing", "honour", "luck"];

    public static readonly string[] Grammar = ["son", "daughter", "of"];

    /// <summary>What a woman's name tends to end in, when a tradition genders its second elements.</summary>
    private static readonly string[] FeminineConcepts =
        ["gift", "love", "blessing", "fair", "bright", "moon", "star", "peace", "wisdom", "sun", "luck"];

    /// <summary>What a man's name tends to end in, likewise.</summary>
    private static readonly string[] MasculineConcepts =
        ["guard", "king", "wolf", "spear", "bright", "friend", "peace", "war", "victory", "strength", "honour", "lord", "bear", "sword", "shield", "iron"];

    public Dictionary<string, List<string>> Roots { get; } = new(StringComparer.Ordinal);

    public List<List<string>> Proto { get; } = [];
    public List<List<string>> MaleDeutero { get; } = [];
    public List<List<string>> FemaleDeutero { get; } = [];
    public List<List<string>> MaleEndings { get; } = [];
    public List<List<string>> FemaleEndings { get; } = [];
    public List<List<string>> FeminineMarkers { get; } = [];

    public List<List<string>> Barony { get; } = [];
    public List<List<string>> County { get; } = [];
    public List<List<string>> Duchy { get; } = [];
    public List<List<string>> Kingdom { get; } = [];
    public List<List<string>> Folk { get; } = [];
    public List<List<string>> Prefixes { get; } = [];
    public List<List<string>> Linkers { get; } = [];

    public bool Prefixing { get; set; }

    public List<string> PatronymMale { get; set; } = [];
    public List<string> PatronymFemale { get; set; } = [];
    public bool PatronymPrefix { get; set; }

    public List<string> Particle { get; set; } = [];

    /// <summary>An English particle ("of") used as-is, outside the language's spelling.</summary>
    public string? ParticleText { get; set; }

    public List<List<string>> Tier(char tier) => tier switch
    {
        'k' or 'e' or 'h' => Kingdom,
        'd' => Duchy,
        'c' => County,
        _ => Barony,
    };

    // --- Building ------------------------------------------------------------------------------

    public static Lexicon Build(LanguageFlavour f, Phonology p, Rng rng)
    {
        var lex = new Lexicon();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // Every concept gets a root: the flavour's real word where it has one, else a new one.
        foreach (var group in new[] { Nature, Built, Quality, Compass, People, Creature, Thing, Virtue, Grammar })
            foreach (string concept in group)
            {
                if (f.Roots is not null && f.Roots.TryGetValue(concept, out string? real))
                {
                    lex.Roots[concept] = Phonemes.Parse(real);
                    seen.Add(p.Spelling.Spell(lex.Roots[concept]));
                    continue;
                }

                bool heavy = group == Nature || group == Built;
                lex.Roots[concept] = Fresh(() => heavy ? p.Word(rng, rng.Chance(0.6) ? 1 : 2) : p.Element(rng), p, seen, rng);
            }

        // Name elements: the flavour's own, padded out with concept roots where they run thin.
        foreach (string e in f.Proto) lex.Proto.Add(Phonemes.Parse(e));
        foreach (string e in f.MaleDeutero) lex.MaleDeutero.Add(Phonemes.Parse(e));
        foreach (string e in f.FemaleDeutero) lex.FemaleDeutero.Add(Phonemes.Parse(e));
        foreach (string e in f.MaleEndings) lex.MaleEndings.Add(Phonemes.Parse(e));
        foreach (string e in f.FemaleEndings) lex.FemaleEndings.Add(Phonemes.Parse(e));
        foreach (string e in f.FeminineMarkers) lex.FeminineMarkers.Add(Phonemes.Parse(e));

        var protoPool = Quality.Concat(Creature).Concat(Thing).Concat(Virtue).Concat(People).ToList();
        rng.Shuffle(protoPool);
        foreach (string concept in protoPool)
        {
            if (lex.Proto.Count >= 16) break;
            var root = lex.Roots[concept];
            if (Phonology.Syllables(root) <= 2 && !lex.Proto.Any(x => x.SequenceEqual(root))) lex.Proto.Add(root);
        }

        var masc = MasculineConcepts.ToList();
        rng.Shuffle(masc);
        foreach (string concept in masc)
        {
            if (lex.MaleDeutero.Count >= 10) break;
            var root = lex.Roots[concept];
            // A second element has to be short, or the compound will not fit on a name.
            if (root.Count <= 4 && !lex.MaleDeutero.Any(x => x.SequenceEqual(root))) lex.MaleDeutero.Add(root);
        }

        if (lex.FeminineMarkers.Count == 0)
            lex.FeminineMarkers.Add(OpenElement(p, rng, 1));

        var fem = FeminineConcepts.ToList();
        rng.Shuffle(fem);
        foreach (string concept in fem)
        {
            if (lex.FemaleDeutero.Count >= 9) break;
            var root = lex.Roots[concept];
            if (root.Count > 4) continue;

            // A feminine second element is the root worn with the language's feminine mark, half the
            // time; the rest are bare, as -hild and -run are.
            var element = rng.Chance(0.5) ? p.Join(root, rng.Pick(lex.FeminineMarkers), rng) : root;
            if (!lex.FemaleDeutero.Any(x => x.SequenceEqual(element))) lex.FemaleDeutero.Add(element);
        }

        while (lex.MaleEndings.Count < 3) lex.MaleEndings.Add(p.Word(rng, 1));
        while (lex.FemaleEndings.Count < 3) lex.FemaleEndings.Add(OpenElement(p, rng, 1));

        // Place-words by tier: most of the flavour's, and enough invented ones to carry the load.
        // A barony pool has to be large because a language names a hundred of them; a kingdom pool
        // can be tiny because it names three.
        Fill(lex.Barony, f.Barony, rng.Int(12, 16), () => rng.Chance(0.6) ? lex.Roots[rng.Pick(Built)] : p.Element(rng), p, rng);
        Fill(lex.County, f.County, rng.Int(5, 7), () => p.Simple(rng), p, rng);
        Fill(lex.Duchy, f.Duchy, rng.Int(4, 5), () => p.Simple(rng), p, rng);
        Fill(lex.Kingdom, f.Kingdom, rng.Int(3, 4), () => p.Simple(rng), p, rng);
        Fill(lex.Folk, f.Folk, 2, () => p.Simple(rng), p, rng);

        lex.Prefixing = f.Prefixes.Length > 0 && rng.Chance(f.PrefixingChance);
        if (lex.Prefixing)
            Fill(lex.Prefixes, f.Prefixes, rng.Int(8, 11), () => lex.Roots[rng.Pick(Built)], p, rng);

        foreach (string linker in f.Linkers) lex.Linkers.Add(linker.Length == 0 ? [] : Phonemes.Parse(linker));

        // Grammar. A flavour with a real patronymic supplies a matched pair; an invented one is the
        // language's own word for son and daughter, cut down to something that can hang off a name.
        if (f.Patronym != LanguageFlavour.PatronymStyle.None)
        {
            lex.PatronymPrefix = f.Patronym == LanguageFlavour.PatronymStyle.Prefix;
            if (f.PatronymMale.Length > 0)
            {
                int i = rng.Int(0, f.PatronymMale.Length - 1);
                lex.PatronymMale = Phonemes.Parse(f.PatronymMale[i]);
                lex.PatronymFemale = Phonemes.Parse(f.PatronymFemale[Math.Min(i, f.PatronymFemale.Length - 1)]);
            }
            else
            {
                lex.PatronymMale = Shorten(lex.Roots["son"], 4);
                lex.PatronymFemale = Shorten(lex.Roots["daughter"], 4);
            }
        }

        if (f.Particles.Length > 0)
        {
            string particle = rng.Pick(f.Particles);
            if (particle.StartsWith('=')) lex.ParticleText = particle[1..];
            else lex.Particle = Phonemes.Parse(particle);
        }
        else lex.Particle = Shorten(lex.Roots["of"], 3);

        return lex;
    }

    private static void Fill(List<List<string>> into, string[] authentic, int target,
        Func<List<string>> invent, Phonology p, Rng rng)
    {
        var pool = authentic.ToList();
        rng.Shuffle(pool);

        // Keep three quarters of the real ones, and never fewer than two where there are two. A
        // flavour with a rich stock of its own invents at most one word beside it.
        int keep = Math.Max(Math.Min(2, pool.Count), (int)Math.Round(pool.Count * 0.75));
        foreach (string a in pool.Take(keep)) into.Add(Phonemes.Parse(a));
        if (pool.Count >= target) target = into.Count + 1;

        var seen = new HashSet<string>(into.Select(p.Spelling.Spell), StringComparer.Ordinal);
        for (int attempt = 0; attempt < target * 6 && into.Count < target; attempt++)
        {
            var candidate = invent();
            if (candidate.Count == 0 || candidate.Count > 6) continue;
            string spelled = p.Spelling.Spell(candidate);
            if (spelled.Length < 2 || !seen.Add(spelled)) continue;
            into.Add(candidate);
        }
    }

    private static List<string> Fresh(Func<List<string>> make, Phonology p, HashSet<string> seen, Rng rng)
    {
        List<string> word = make();
        for (int attempt = 0; attempt < 8; attempt++)
        {
            string spelled = p.Spelling.Spell(word);
            if (spelled.Length >= 2 && seen.Add(spelled)) return word;
            word = make();
        }
        return word;
    }

    /// <summary>A syllable or two that ends in a vowel: the shape of a feminine mark.</summary>
    private static List<string> OpenElement(Phonology p, Rng rng, int syllables)
    {
        var word = p.Word(rng, syllables);
        if (word.Count > 0 && Phonemes.IsVowel(word[^1])) return word;

        // Take the coda off; if that leaves nothing vowel-final, add the commonest vowel.
        while (word.Count > 0 && Phonemes.IsConsonant(word[^1])) word.RemoveAt(word.Count - 1);
        if (word.Count == 0 || !Phonemes.IsVowel(word[^1]))
            word.Add(p.Nuclei.OrderByDescending(n => n.Weight).First().Id);
        return word;
    }

    private static List<string> Shorten(List<string> word, int maxPhonemes)
    {
        if (word.Count <= maxPhonemes) return [.. word];
        var cut = word.Take(maxPhonemes).ToList();
        // Do not end on the first half of a cluster.
        while (cut.Count > 1 && Phonemes.IsConsonant(cut[^1]) && Phonemes.IsConsonant(cut[^2])) cut.RemoveAt(cut.Count - 1);
        return cut;
    }

    // --- Inheritance ---------------------------------------------------------------------------

    public Lexicon Clone()
    {
        var lex = new Lexicon
        {
            Prefixing = Prefixing,
            PatronymMale = [.. PatronymMale],
            PatronymFemale = [.. PatronymFemale],
            PatronymPrefix = PatronymPrefix,
            Particle = [.. Particle],
            ParticleText = ParticleText,
        };

        foreach (var (k, v) in Roots) lex.Roots[k] = [.. v];
        Copy(Proto, lex.Proto); Copy(MaleDeutero, lex.MaleDeutero); Copy(FemaleDeutero, lex.FemaleDeutero);
        Copy(MaleEndings, lex.MaleEndings); Copy(FemaleEndings, lex.FemaleEndings); Copy(FeminineMarkers, lex.FeminineMarkers);
        Copy(Barony, lex.Barony); Copy(County, lex.County); Copy(Duchy, lex.Duchy); Copy(Kingdom, lex.Kingdom);
        Copy(Folk, lex.Folk); Copy(Prefixes, lex.Prefixes); Copy(Linkers, lex.Linkers);
        return lex;

        static void Copy(List<List<string>> from, List<List<string>> to)
        {
            foreach (var w in from) to.Add([.. w]);
        }
    }

    /// <summary>One sound change, carried through every word at once.</summary>
    public void Shift(string from, string to)
    {
        foreach (var list in Lists())
            foreach (var word in list)
                for (int i = 0; i < word.Count; i++)
                    if (word[i] == from) word[i] = to;

        foreach (var word in Roots.Values)
            for (int i = 0; i < word.Count; i++)
                if (word[i] == from) word[i] = to;

        Replace(PatronymMale); Replace(PatronymFemale); Replace(Particle);

        void Replace(List<string> word)
        {
            for (int i = 0; i < word.Count; i++) if (word[i] == from) word[i] = to;
        }
    }

    /// <summary>
    /// Replaces a share of the place-words and endings with fresh ones. A sister language shares
    /// its parent's roots — that is what makes it a sister — but names its villages with words of
    /// its own, the way -by is Danish where -ton is English.
    /// </summary>
    public void Renew(double share, Phonology p, Rng rng)
    {
        foreach (var list in new[] { Barony, County, Duchy })
            for (int i = 0; i < list.Count; i++)
                if (rng.Chance(share)) list[i] = p.Element(rng);

        for (int i = 0; i < MaleEndings.Count; i++) if (rng.Chance(share * 0.5)) MaleEndings[i] = p.Word(rng, 1);
    }

    private IEnumerable<List<List<string>>> Lists()
    {
        yield return Proto; yield return MaleDeutero; yield return FemaleDeutero;
        yield return MaleEndings; yield return FemaleEndings; yield return FeminineMarkers;
        yield return Barony; yield return County; yield return Duchy; yield return Kingdom;
        yield return Folk; yield return Prefixes; yield return Linkers;
    }
}
