using System.Text;
using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen;

/// <summary>
/// One language: everything a people's names are drawn from.
///
/// A generated language is a <see cref="LanguageFlavour"/> realised as a <see cref="MapGen.Phonology"/>
/// (its sounds, syllable shapes and spelling) and a <see cref="MapGen.Lexicon"/> (its roots, name
/// elements and place-words). Names are assembled from lexicon pieces at the phoneme level, mended
/// at every seam by the phonology, and spelled once at the end — which is what makes a barony, the
/// count who holds it and the dynasty he founded all sound like one country.
///
/// Languages come in families. <see cref="Derive"/> makes a sister tongue by carrying a few sound
/// changes through the parent's whole lexicon, and <see cref="Dialect"/> makes the lighter version
/// each culture speaks, so that two cultures of one heritage name their people recognisably alike
/// and distinguishably apart.
///
/// A language built from an imported Azgaar name base (<see cref="FromNameBase"/>) has none of
/// this: its words come out of a Markov chain over the export's corpus and its affixes are mined
/// from the same, exactly as before. Every public member works on both kinds.
/// </summary>
public sealed class Language
{
    public string Key { get; }
    public string Name { get; set; } = "";

    public LanguageFlavour? Flavour { get; }
    public Phonology? Phonology { get; }
    public Lexicon? Lexicon { get; }
    public Language? Parent { get; }

    /// <summary>Set for an imported language; see the class remarks.</summary>
    private readonly AzgaarNames? _markov;

    /// <summary>Spelled pools, kept for the imported path and for anything that inspects a language.</summary>
    public string[] MaleEndings { get; private set; } = [];
    public string[] FemaleEndings { get; private set; } = [];
    public string[] BaronyAffixes { get; private set; } = [];
    public string[] CountyAffixes { get; private set; } = [];
    public string[] DuchyAffixes { get; private set; } = [];
    public string[] KingdomAffixes { get; private set; } = [];

    /// <summary>The patronymic pair and the "of" particle, as CK3 name lists want them: lower case, no padding.</summary>
    public string PatronymMale { get; private set; } = "";
    public string PatronymFemale { get; private set; } = "";
    public bool PatronymIsPrefix { get; private set; }
    public string Particle { get; private set; } = "";

    /// <summary>
    /// Every word this language has spelled, back to its sounds, so a realm can be named after a
    /// people whose name was already handed out as text.
    /// </summary>
    private readonly Dictionary<string, List<string>> _phonemesOf = new(StringComparer.Ordinal);

    private Language(string key, LanguageFlavour flavour, Phonology phonology, Lexicon lexicon, Language? parent)
    {
        Key = key;
        Flavour = flavour;
        Phonology = phonology;
        Lexicon = lexicon;
        Parent = parent;

        MaleEndings = Spelled(lexicon.MaleEndings);
        FemaleEndings = Spelled(lexicon.FemaleEndings);
        BaronyAffixes = Spelled(lexicon.Barony);
        CountyAffixes = Spelled(lexicon.County);
        DuchyAffixes = Spelled(lexicon.Duchy);
        KingdomAffixes = Spelled(lexicon.Kingdom);

        PatronymMale = SpellLower(lexicon.PatronymMale);
        PatronymFemale = SpellLower(lexicon.PatronymFemale);
        PatronymIsPrefix = lexicon.PatronymPrefix;
        Particle = lexicon.ParticleText ?? SpellLower(lexicon.Particle);

        string[] Spelled(List<List<string>> words) => words.Select(phonology.Spelling.Spell).ToArray();
        string SpellLower(List<string> word) => word.Count == 0 ? "" : phonology.Spelling.Spell(word).ToLowerInvariant();
    }

    private Language(string key, AzgaarNames markov)
    {
        Key = key;
        _markov = markov;
    }

    // --- Creation ------------------------------------------------------------------------------

    /// <summary>
    /// A new language. Given a <paramref name="parent"/>, a sister of it — same flavour, shared
    /// roots, a few sounds shifted — otherwise a fresh realisation of <paramref name="flavour"/>,
    /// or of a random real-world flavour when none is named.
    /// </summary>
    public static Language Create(string key, Rng rng, LanguageFlavour? flavour = null, Language? parent = null)
    {
        if (parent is { Phonology: not null, Lexicon: not null })
            return parent.Derive(key, rng, 0.5);

        flavour ??= rng.Pick(LanguageFlavour.All.Where(f => !f.Fantasy).ToList());

        var phonology = Phonology.FromFlavour(flavour, rng);
        var lexicon = Lexicon.Build(flavour, phonology, rng);
        var language = new Language(key, flavour, phonology, lexicon, null);
        language.Name = language.LanguageNameFor(language.FolkName(rng), rng);
        return language;
    }

    /// <summary>The Old English sound-alike, for the first heritage of a western world.</summary>
    public static Language CreateAnglic(string key, Rng rng) => Create(key, rng, LanguageFlavour.Anglic);

    /// <summary>
    /// A daughter of this language. At low <paramref name="strength"/> a dialect: one sound may
    /// shift, one spelling drifts, a few place-words are its own. At high strength a sister
    /// language: several shifts, its own orthography, a third of its place-words renewed.
    /// </summary>
    public Language Derive(string key, Rng rng, double strength)
    {
        if (Phonology is null || Lexicon is null || Flavour is null) return this;

        var phonology = Phonology.Clone();
        var lexicon = Lexicon.Clone();

        // Only shifts that stay inside the flavour's own inventory: a Latin daughter may turn /k/
        // into /kh/ only if some Latin somewhere has a /kh/, or it stops sounding like Latin at all.
        var owned = Flavour.Consonants.Select(c => c.Id).Concat(Flavour.Vowels.Select(v => v.Id)).ToHashSet(StringComparer.Ordinal);
        bool dialect = strength < 0.3;
        int changes = dialect ? (rng.Chance(0.6) ? 1 : 0) : rng.Int(2, 3);
        var available = (dialect ? LightChanges : LightChanges.Concat(HeavyChanges))
            .Where(c => phonology.Has(c.From) && owned.Contains(c.To)).ToList();
        rng.Shuffle(available);

        foreach (var (from, to) in available.Take(changes))
        {
            phonology.Shift(from, to);
            lexicon.Shift(from, to);
        }

        if (strength < 0.3)
        {
            phonology.Spelling.Drift(Flavour, rng);
            lexicon.Renew(0.12, phonology, rng);
        }
        else
        {
            phonology.Spelling = Orthography.FromFlavour(Flavour, rng);
            lexicon.Renew(0.35, phonology, rng);
        }

        var language = new Language(key, Flavour, phonology, lexicon, this);
        language.Name = language.LanguageNameFor(language.FolkName(rng), rng);
        return language;
    }

    /// <summary>The variety one culture speaks. An imported language has no dialects.</summary>
    public Language Dialect(string key, Rng rng) => _markov is not null ? this : Derive(key, rng, 0.2);

    /// <summary>
    /// Regular sound changes a daughter language may apply. Each is a whole-phoneme substitution,
    /// which is crude beside real sound law but is enough to make "Aldric" and "Alrikh" read as the
    /// same name in two countries.
    /// </summary>
    private static readonly (string From, string To)[] LightChanges =
    [
        ("w", "v"), ("v", "w"), ("th", "t"), ("dh", "d"), ("kh", "k"), ("gh", "g"),
        ("hl", "l"), ("hr", "r"), ("ng", "n"), ("ts", "s"), ("q", "k"), ("ch", "sh"), ("j", "y"), ("y", "j"),
        ("ai", "ei"), ("au", "oo"), ("ae", "e"), ("oe", "e"), ("ü", "i"), ("eh", "a"), ("ee", "ii"), ("oo", "uu"),
    ];

    /// <summary>The shifts that make a sister language rather than an accent: whole series moving.</summary>
    private static readonly (string From, string To)[] HeavyChanges =
    [
        ("p", "f"), ("t", "th"), ("k", "kh"), ("b", "v"), ("d", "dh"), ("g", "gh"), ("th", "s"),
        ("a", "o"), ("o", "u"), ("u", "o"), ("e", "i"), ("i", "e"), ("sh", "s"), ("s", "sh"), ("f", "h"),
    ];

    /// <summary>
    /// A language backed by one of Azgaar's name bases, so that places our generator names itself
    /// sound like the places Azgaar named. Affixes are mined from the corpus; where it is too thin
    /// to yield recurring endings, a generated language stands in for that one list.
    /// </summary>
    public static Language FromNameBase(string key, AzgaarNames markov, Rng rng, string? name = null)
    {
        var language = new Language(key, markov);
        var fallback = Create($"{key}_fallback", rng);

        language.MaleEndings = Pick(markov.CommonEndings(6, 2, 4), () => fallback.MaleEndings);
        language.FemaleEndings = Pick(markov.CommonEndings(6, 2, 3), () => fallback.FemaleEndings);
        language.BaronyAffixes = Pick(markov.CommonEndings(8, 3, 6), () => fallback.BaronyAffixes);
        language.CountyAffixes = Pick(markov.CommonEndings(6, 4, 7), () => fallback.CountyAffixes);
        language.DuchyAffixes = Pick(markov.CommonEndings(5, 4, 7), () => fallback.DuchyAffixes);
        language.KingdomAffixes = Pick(markov.CommonEndings(5, 2, 5), () => fallback.KingdomAffixes);

        language.Name = name ?? Orthography.Capitalise(markov.Generate(rng, 4, 9));

        // Short grammatical words out of the corpus, as before.
        language.PatronymMale = Cut(language.Word(rng, 1, 1), 4);
        language.PatronymFemale = Cut(language.Word(rng, 1, 1), 4);
        language.Particle = Cut(language.Word(rng, 1, 1), 3);
        return language;

        // Two is the fewest that reads as a pattern rather than as one word's tail repeated.
        static string[] Pick(string[] mined, Func<string[]> invented)
            => mined.Length >= 2 ? mined : invented();

        static string Cut(string word, int max)
        {
            string lower = word.ToLowerInvariant();
            return lower.Length > max ? lower[..max] : lower;
        }
    }

    // --- Words ---------------------------------------------------------------------------------

    /// <summary>A bare word of the language: a culture, a faith, a river, a thing.</summary>
    public string Word(Rng rng, int minSyllables = 2, int maxSyllables = 3)
    {
        if (_markov is not null) return Orthography.Capitalise(MarkovRoot(rng, minSyllables, maxSyllables));

        string best = "";
        for (int attempt = 0; attempt < 12; attempt++)
        {
            var ids = Phonology!.Word(rng, rng.Int(minSyllables, maxSyllables));
            best = Spell(ids);
            if (best.Length is >= 3 and <= 11 && !Blocked(best)) return best;
        }
        return best;
    }

    public string MaleName(Rng rng)
    {
        if (_markov is not null)
            return Orthography.Capitalise(JoinText(MarkovRoot(rng, 1, 2), rng.Chance(0.55) ? rng.Pick(MaleEndings) : ""));

        string best = "";
        for (int attempt = 0; attempt < 12; attempt++)
        {
            var ids = MaleNameIds(rng);
            best = Spell(ids);
            if (Fits(ids, best, 3, 10)) return best;
        }
        return best;
    }

    public string FemaleName(Rng rng)
    {
        if (_markov is not null)
            return Orthography.Capitalise(JoinText(MarkovRoot(rng, 1, 2), rng.Chance(0.80) ? rng.Pick(FemaleEndings) : ""));

        var f = Flavour!;
        var lex = Lexicon!;
        var p = Phonology!;

        string best = "";
        for (int attempt = 0; attempt < 12; attempt++)
        {
            double roll = rng.Double();
            bool? harmony = p.Harmony ? rng.Chance(0.5) : null;
            List<string> ids;

            if (roll < f.Dithematic && lex.Proto.Count > 0 && lex.FemaleDeutero.Count > 0)
                ids = Compound(lex.Proto, lex.FemaleDeutero, rng);
            else if (roll < f.Dithematic + f.RootEnding)
                ids = p.Join(p.Word(rng, rng.Int(1, 2), harmony), rng.Pick(lex.FemaleEndings), rng);
            else
            {
                ids = p.Word(rng, rng.Int(2, 3), harmony);
                if (rng.Chance(0.7)) ids = p.Join(ids, rng.Pick(lex.FeminineMarkers), rng);
            }

            best = Spell(ids);
            if (Fits(ids, best, 3, 10)) return best;
        }
        return best;
    }

    private List<string> MaleNameIds(Rng rng)
    {
        var f = Flavour!;
        var lex = Lexicon!;
        var p = Phonology!;

        double roll = rng.Double();
        bool? harmony = p.Harmony ? rng.Chance(0.5) : null;

        if (roll < f.Dithematic && lex.Proto.Count > 0 && lex.MaleDeutero.Count > 0)
            return Compound(lex.Proto, lex.MaleDeutero, rng);

        if (roll < f.Dithematic + f.RootEnding)
            return p.Join(p.Word(rng, rng.Int(1, 2), harmony), rng.Pick(lex.MaleEndings), rng);

        return p.Word(rng, rng.Int(2, 3), harmony);
    }

    /// <summary>Two name elements joined, never the same one twice: Devadeva is nobody's name.</summary>
    private List<string> Compound(List<List<string>> first, List<List<string>> second, Rng rng)
    {
        var a = rng.Pick(first);
        var b = rng.Pick(second);
        for (int attempt = 0; attempt < 4 && a.SequenceEqual(b); attempt++) b = rng.Pick(second);
        return Phonology!.Join(a, b, rng);
    }

    /// <summary>A place of the given tier: b, c, d, k or e.</summary>
    public string PlaceName(Rng rng, char tier)
    {
        if (_markov is not null)
            return PlaceName(rng, tier switch
            {
                'k' or 'e' or 'h' => KingdomAffixes,
                'd' => DuchyAffixes,
                'c' => CountyAffixes,
                _ => BaronyAffixes,
            });

        int cap = tier switch { 'b' or 'c' => 12, 'd' => 12, _ => 13 };
        int floor = tier is 'b' or 'c' ? 3 : 4;

        string best = "";
        for (int attempt = 0; attempt < 12; attempt++)
        {
            var ids = PlaceIds(rng, tier);
            best = Spell(ids);
            if (best.Length >= floor && best.Length <= cap && !Blocked(best)) return best;
        }
        return best;
    }

    /// <summary>The old signature, kept for callers that hold one of the spelled pools.</summary>
    public string PlaceName(Rng rng, string[] affixes)
    {
        if (_markov is null)
        {
            char tier = ReferenceEquals(affixes, KingdomAffixes) ? 'k'
                      : ReferenceEquals(affixes, DuchyAffixes) ? 'd'
                      : ReferenceEquals(affixes, CountyAffixes) ? 'c' : 'b';
            return PlaceName(rng, tier);
        }

        string root = MarkovRoot(rng, 1, 2);
        if (!rng.Chance(0.75)) return Orthography.Capitalise(root);
        return Orthography.Capitalise(JoinText(root, rng.Pick(affixes)));
    }

    private List<string> PlaceIds(Rng rng, char tier)
    {
        var lex = Lexicon!;
        var p = Phonology!;
        var suffixes = lex.Tier(tier);
        bool? harmony = p.Harmony ? rng.Chance(0.5) : null;

        int roll = rng.Int(0, 99);

        // A prefixing language leads with its place-word: Aber-, al-, Bally-.
        int prefixShare = !lex.Prefixing ? 0 : tier switch { 'b' => 35, 'c' or 'd' => 25, _ => 15 };
        if (roll < prefixShare)
        {
            var head = rng.Pick(lex.Prefixes);
            var body = rng.Chance(0.6) ? Descriptor(rng, tier) : p.Word(rng, rng.Int(1, 2), harmony);
            return p.Join(head, body, rng);
        }
        roll -= prefixShare;

        // Descriptor plus place-word: Blackford, Stanton, Novgorod.
        if (roll < 45) return p.Join(Descriptor(rng, tier), rng.Pick(suffixes), rng);
        roll -= 45;

        // A person's name, a genitive, a place-word: Wokingham, Alexandria.
        if (roll < 15 && lex.Proto.Count > 0)
        {
            var owner = rng.Pick(lex.Proto);
            var linker = rng.Pick(lex.Linkers);
            return p.Join(p.Join(owner, linker, rng), rng.Pick(suffixes), rng);
        }
        roll -= 15;

        // A people's land: Westseaxe, Norðmenn — for the tiers a people can hold.
        if (roll < 12 && tier != 'b' && lex.Folk.Count > 0)
        {
            string concept = rng.Chance(0.5) ? rng.Pick(Lexicon.Compass) : rng.Pick(Lexicon.Quality);
            return p.Join(lex.Roots[concept], rng.Pick(lex.Folk), rng);
        }
        roll -= 12;

        // A bare word: Kent, Ur, Rome.
        if (roll < 15) return p.Word(rng, rng.Int(2, 3), harmony);

        // Two roots run together: Stanbrycg, Steinberg.
        var first = Descriptor(rng, tier);
        return p.Join(first, Another(rng, tier, first), rng);
    }

    /// <summary>A second descriptor, not the same as the one just drawn.</summary>
    private List<string> Another(Rng rng, char tier, List<string>? first = null)
    {
        var second = Descriptor(rng, tier);
        for (int attempt = 0; attempt < 4 && first is not null && second.SequenceEqual(first); attempt++)
            second = Descriptor(rng, tier);
        return second;
    }

    /// <summary>A root a place is described by: its colour, its beast, its hill.</summary>
    private List<string> Descriptor(Rng rng, char tier)
    {
        int roll = rng.Int(0, 99);
        string[] pool = roll < 35 ? Lexicon.Quality
                      : roll < 65 ? Lexicon.Nature
                      : roll < 80 ? Lexicon.Creature
                      : roll < 92 ? Lexicon.Thing
                      : tier == 'b' ? Lexicon.Built : Lexicon.People;
        return Lexicon!.Roots[rng.Pick(pool)];
    }

    /// <summary>
    /// A kingdom or empire. Named after its people where the people's name is one this language
    /// coined — Francia from the Franks — else as a place of the top tier, and for an empire
    /// sometimes as a compound, which is the one form that reads as a realm rather than a spot.
    /// </summary>
    public string RealmName(Rng rng, string? folk, char tier)
    {
        if (_markov is not null)
            return tier == 'k' && rng.Chance(0.5) ? PlaceName(rng, KingdomAffixes) : CompoundName(rng);

        var p = Phonology!;
        var lex = Lexicon!;

        if (folk is not null && _phonemesOf.TryGetValue(folk, out var stem) && rng.Chance(tier == 'k' ? 0.55 : 0.45))
        {
            for (int attempt = 0; attempt < 6; attempt++)
            {
                string name = Spell(p.Join(stem, rng.Pick(lex.Kingdom), rng));
                if (name.Length <= 13 && !Blocked(name)) return name;
            }
        }

        if (tier != 'k' && rng.Chance(0.5)) return CompoundName(rng);
        return PlaceName(rng, 'k');
    }

    public string CompoundName(Rng rng)
    {
        if (_markov is not null)
        {
            string a = MarkovRoot(rng, 1, 2), b = MarkovRoot(rng, 1, 2);
            return Orthography.Capitalise(rng.Chance(0.3) ? $"{a}-{b}" : JoinText(a, b));
        }

        string best = "";
        for (int attempt = 0; attempt < 8; attempt++)
        {
            var head = Descriptor(rng, 'k');
            var ids = Phonology!.Join(head, rng.Chance(0.5) ? Another(rng, 'k', head) : Phonology.Word(rng, rng.Int(1, 2)), rng);
            best = Spell(ids);
            if (best.Length is >= 4 and <= 13 && !Blocked(best)) return best;
        }
        return best;
    }

    /// <summary>
    /// A house: named for its seat, for its founder, or for something it took as its sign — the
    /// three ways real dynasties got theirs.
    /// </summary>
    public string DynastyName(Rng rng)
    {
        if (_markov is not null) return MaleName(rng);

        var p = Phonology!;
        var lex = Lexicon!;

        string best = "";
        for (int attempt = 0; attempt < 10; attempt++)
        {
            int roll = rng.Int(0, 99);
            List<string> ids;

            if (roll < 35) ids = PlaceIds(rng, 'b');
            else if (roll < 70)
            {
                ids = MaleNameIds(rng);
                if (!lex.PatronymPrefix && lex.PatronymMale.Count > 0 && rng.Chance(0.7))
                    ids = p.Join(ids, lex.PatronymMale, rng);
            }
            else
            {
                string[] signs = rng.Chance(0.5) ? Lexicon.Creature : rng.Chance(0.5) ? Lexicon.Thing : Lexicon.Virtue;
                ids = p.Join(lex.Roots[rng.Pick(Lexicon.Quality)], lex.Roots[rng.Pick(signs)], rng);
            }

            best = Spell(ids);
            if (best.Length is >= 4 and <= 12 && !Blocked(best)) return best;
        }
        return best;
    }

    /// <summary>The name of a people, for a culture or a heritage.</summary>
    public string FolkName(Rng rng)
    {
        if (_markov is not null) return Word(rng, 2, 3);

        var p = Phonology!;
        var lex = Lexicon!;

        string best = "";
        for (int attempt = 0; attempt < 12; attempt++)
        {
            List<string> ids;
            if (rng.Chance(0.6) || lex.Folk.Count == 0)
                ids = p.Word(rng, rng.Int(2, 3));
            else
            {
                string[] pool = rng.Chance(0.5) ? Lexicon.Quality : rng.Chance(0.5) ? Lexicon.Nature : Lexicon.Creature;
                ids = p.Join(lex.Roots[rng.Pick(pool)], rng.Pick(lex.Folk), rng);
            }

            best = Spell(ids);
            if (best.Length is >= 4 and <= 10 && !Blocked(best)) return best;
        }
        return best;
    }

    /// <summary>What the tongue of the <paramref name="folk"/> is called: Frankish for the Franks.</summary>
    public string LanguageNameFor(string folk, Rng rng)
    {
        if (_markov is not null) return Name.Length > 0 ? Name : folk;
        if (Flavour!.LanguageNames is { Length: > 0 } fixedNames) return rng.Pick(fixedNames);

        string[] suffixes = Flavour.Adjectival;
        string suffix = rng.Pick(suffixes);

        // A consonant suffix (-sk) needs a vowel to sit on; after a consonant, take one that begins
        // with a vowel instead, if the flavour offers any.
        if (!IsVowelLetter(suffix[0]) && !IsVowelLetter(folk[^1]))
            suffix = suffixes.FirstOrDefault(s => IsVowelLetter(s[0])) ?? suffix;

        string result = Adjective(folk, suffix);

        if (result.Length > 13)
            result = Adjective(folk, suffixes.OrderBy(s => s.Length).First());

        return result;

        static string Adjective(string stem, string suffix)
        {
            string s = stem;
            bool suffixVowel = IsVowelLetter(suffix[0]);
            if (suffixVowel && s.Length > 3 && IsVowelLetter(s[^1])) s = s[..^1];
            if (s.Length > 0 && char.ToLowerInvariant(s[^1]) == suffix[0]) s = s[..^1];
            return s + suffix;
        }
    }

    // --- Plumbing ------------------------------------------------------------------------------

    private string Spell(List<string> ids)
    {
        string s = Phonology!.Spelling.Spell(ids);
        _phonemesOf.TryAdd(s, ids);
        return s;
    }

    private bool Fits(List<string> ids, string spelled, int min, int max)
        => Phonology.Syllables(ids) <= 4 && spelled.Length >= min && spelled.Length <= max && !Blocked(spelled);

    private string MarkovRoot(Rng rng, int minSyllables, int maxSyllables)
    {
        // Syllable counts translated into letter counts, because that is what the chain is
        // bounded by. The floor of three is deliberate: a one-syllable request scaled down from a
        // short base otherwise yields names like "As" and "Sa", which read as truncation rather
        // than as words. Azgaar's own bases never go below four.
        int lo = Math.Max(3, (int)Math.Round(_markov!.MinLength * minSyllables / 2.0));
        int hi = Math.Max(lo + 1, (int)Math.Round(_markov.MaxLength * maxSyllables / 3.0));
        return _markov.Generate(rng, lo, hi).ToLowerInvariant();
    }

    /// <summary>Text-level joining for the imported path, where words are strings from the start.</summary>
    private static string JoinText(string first, string second)
    {
        if (second.Length == 0) return first;
        if (first.Length == 0) return second;

        bool firstEndsInVowel = IsVowelLetter(first[^1]);
        bool secondStartsInVowel = IsVowelLetter(second[0]);

        if (firstEndsInVowel && secondStartsInVowel) return TidyText(first[..^1] + second);
        if (first[^1] == second[0]) return TidyText(first + second[1..]);

        if (!firstEndsInVowel && !secondStartsInVowel)
        {
            int trailing = 0;
            for (int i = first.Length - 1; i >= 0 && !IsVowelLetter(first[i]); i--) trailing++;
            int leading = 0;
            for (int i = 0; i < second.Length && !IsVowelLetter(second[i]); i++) leading++;
            if (trailing + leading > 3) first = first[..^trailing];
        }

        return TidyText(first + second);
    }

    private static string TidyText(string word)
    {
        var sb = new StringBuilder(word.Length);
        int run = 0;
        foreach (char c in word)
        {
            if (sb.Length >= 2 && sb[^1] == c && sb[^2] == c) continue;
            if (IsVowelLetter(c)) run = 0;
            else if (c != '\'' && c != '-' && ++run > 3) { run--; continue; }
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static bool IsVowelLetter(char c)
        => "aeiouyáäâåæéëêíïîóöôøúüûāēīōū".Contains(char.ToLowerInvariant(c));

    // --- Taste ---------------------------------------------------------------------------------

    private static readonly HashSet<string> BlockedWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "sex", "food", "turd", "cluck", "poo", "pee", "ass", "arse", "cock", "dick", "tit", "tits",
        "cum", "crap", "fart", "butt", "boob", "wank", "anus", "penis", "nazi", "rape", "kill",
        "dung", "puke", "piss", "slut", "whore", "damn", "fag", "cunt", "twat", "porn", "gay",
        "homo", "jew", "god", "dead", "death", "bum", "bums", "willy", "nob", "knob", "prick",
        "bitch", "bastard", "semen", "sperm", "pussy", "vagina", "urine", "snot", "vomit", "poop",
        "crotch", "nipple", "balls", "moron", "idiot", "dumb", "fat", "ugly", "hell", "satan",
        "jesus", "christ", "allah", "loo", "bog", "muck", "scum", "smeg", "wee", "wees", "dong",
        "boner", "hooker", "tramp", "retard", "spaz", "queer", "dyke", "coon", "chink", "gook",
        "kike", "spic", "paki", "negro", "nazis", "hitler", "rapist", "molest", "fanny", "minge",
        "arsch", "kack", "merde", "putain", "puta", "mierda", "cazzo", "scheisse", "shite",
    };

    private static readonly string[] BlockedFragments =
    [
        "fuck", "shit", "cunt", "cock", "dick", "piss", "slut", "whore", "rape", "nazi", "penis",
        "anus", "wank", "boob", "turd", "fart", "poop", "nigg", "twat", "dildo", "porn", "pussy",
        "vagin", "bitch", "sperm", "semen", "fagg", "retard", "hitler", "molest", "arse",
    ];

    /// <summary>
    /// Everyday English that a generator will land on by accident. Not a dictionary — nothing
    /// short of one would catch everything — but the words common enough that a county called
    /// "Home" or a duke called "Male" is read as English first and as a name never.
    /// </summary>
    private static readonly HashSet<string> EnglishWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "home", "bury", "data", "male", "female", "king", "lord", "land", "hill", "lake", "wood", "field",
        "wired", "tired", "hired", "fired", "wire", "tire", "dull", "numb", "weird", "beard", "heard",
        "stone", "rock", "ford", "bridge", "town", "city", "farm", "road", "gate", "hall", "wall", "tower",
        "mill", "fire", "storm", "star", "gold", "iron", "wolf", "bear", "boar", "raven", "eagle", "stag",
        "horse", "hawk", "thorn", "fair", "wild", "dark", "black", "white", "red", "green", "grey", "gray",
        "great", "long", "broad", "cold", "holy", "east", "west", "north", "south", "new", "old", "high",
        "low", "big", "small", "good", "bad", "best", "rest", "test", "nest", "list", "mist", "fist", "post",
        "most", "host", "cost", "lost", "boss", "loss", "mass", "pass", "gas", "has", "was", "are", "been",
        "have", "had", "did", "done", "went", "gone", "come", "came", "get", "got", "give", "gave", "take",
        "took", "make", "made", "see", "saw", "say", "said", "tell", "told", "ask", "know", "knew", "think",
        "want", "need", "like", "love", "hate", "hope", "fear", "wish", "will", "shall", "can", "could",
        "would", "should", "may", "might", "must", "let", "put", "set", "run", "ran", "walk", "talk", "sit",
        "sat", "stand", "stood", "eat", "ate", "drink", "sleep", "live", "died", "life", "man", "men", "boy",
        "girl", "son", "mother", "father", "wife", "child", "baby", "people", "person", "friend", "name",
        "word", "time", "year", "day", "week", "month", "hour", "night", "morning", "noon", "world",
        "water", "earth", "air", "heat", "light", "sound", "noise", "smell", "taste", "touch", "hand",
        "foot", "head", "face", "eye", "ear", "nose", "mouth", "lip", "tooth", "hair", "skin", "bone",
        "blood", "heart", "mind", "soul", "body", "arm", "leg", "back", "side", "top", "end", "start",
        "begin", "middle", "point", "line", "edge", "corner", "part", "whole", "piece", "bit", "lot", "much",
        "many", "some", "none", "all", "any", "each", "every", "few", "more", "less", "least", "own", "other",
        "same", "next", "last", "first", "second", "third", "once", "twice", "again", "here", "there",
        "where", "when", "why", "how", "what", "who", "which", "this", "that", "these", "those", "the",
        "and", "but", "nor", "for", "from", "with", "into", "onto", "upon", "over", "under", "above",
        "below", "between", "among", "through", "across", "along", "around", "about", "after", "before",
        "during", "until", "since", "while", "because", "though", "never", "always", "often", "soon",
        "late", "early", "today", "tonight", "now", "then", "still", "just", "only", "even", "also", "too",
        "very", "quite", "rather", "almost", "enough", "far", "near", "away", "out", "off", "yes", "not",
        "maybe", "okay", "hello", "bye", "thanks", "please", "sorry", "help", "stop", "wait", "look", "find",
        "lose", "keep", "hold", "bring", "carry", "send", "pay", "buy", "sell", "price", "money", "rich",
        "poor", "free", "open", "close", "shut", "cut", "hit", "hurt", "pain", "sick", "well", "ill",
        "safe", "true", "false", "real", "fake", "little", "tall", "short", "thin", "wide", "narrow",
        "deep", "flat", "round", "square", "hot", "warm", "cool", "wet", "dry", "hard", "soft", "loud",
        "quiet", "fast", "slow", "quick", "easy", "simple", "plain", "nice", "kind", "mean", "cruel",
        "brave", "weak", "strong", "young", "odd", "ripe", "raw", "rude", "happy", "sad", "glad", "mad",
        "angry", "calm", "proud", "shy", "bold", "tame", "wise", "dull", "sharp", "blunt", "blank", "clear",
        "clean", "dirty", "neat", "pure", "sweet", "sour", "salt", "bitter", "mild", "fresh", "stale",
        "alive", "sale", "sail", "tale", "tail", "mail", "rail", "nail", "hail", "fail", "jail", "bail",
        "veil", "vale", "gale", "pale", "bale", "dale", "kale", "rate", "late", "fate", "date", "mate",
        "note", "vote", "rose", "hose", "pose", "dose", "chose", "cone", "tone", "zone", "lone", "pine",
        "fine", "mine", "nine", "vine", "wine", "dine", "bind", "wind", "rise", "size", "rice", "mice",
        "vice", "dice", "lice", "tile", "file", "mile", "pile", "vile", "bile", "lime", "dime", "hole",
        "mole", "pole", "role", "sole", "mule", "rule", "tube", "cube", "dude", "nude", "mode", "code",
        "node", "rode", "wave", "cave", "save", "rave", "pave", "bake", "cake", "rake", "sake", "wake",
        "game", "tame", "lame", "fame", "dame", "base", "case", "vase", "lace", "mace", "pace", "race",
        "rope", "cope", "mope", "pope", "dope", "bike", "hike", "pike", "ride", "tide", "hide", "bride",
        "pride", "slide", "glide", "guide", "rife", "knife", "bite", "kite", "mite", "rite", "site",
        "spite", "write", "pipe", "wipe", "type", "hype", "gripe", "stripe", "swipe", "howe", "rota",
        "nile", "rome", "york", "paris", "mars", "venus", "pluto", "moon", "sun", "asia", "india", "china",
        "spain", "wales", "egypt", "nova", "coda", "soda", "toga", "yoga", "tuba", "puma", "lama", "mama",
        "papa", "dada", "nana", "baba", "polo", "solo", "loco", "taco", "cola", "gala", "sofa", "lava",
        "diva", "visa", "ammo", "memo", "demo", "limo", "lino", "silo", "halo", "kilo", "hero", "zero",
        "auto", "veto", "judo", "info", "typo", "logo", "bingo", "tango", "mango", "disco", "motto",
        "ditto", "lotto", "gusto", "pesto", "tempo", "combo", "jumbo", "gumbo", "limbo", "bimbo", "dingo",
        "lingo", "gecko", "salvo", "bravo", "cargo", "largo", "macho", "nacho", "poncho", "rancho",
        "pasta", "salsa", "plaza", "pizza", "opera", "aroma", "coma", "dogma", "karma", "magma", "drama",
        "llama", "sauna", "fauna", "flora", "aura", "tuna", "luna", "mesa", "peso", "sumo", "tofu",
        "menu", "emu", "gnu", "tutu", "zulu", "guru", "haiku", "vodka", "polka", "yucca", "mecca",
        "made", "mode", "mane", "mate", "mine", "mole", "mule", "muse", "mute", "gene", "gate", "gore",
        "bore", "core", "fore", "lore", "more", "pore", "sore", "tore", "wore", "yore", "dire", "hire",
        "mire", "sire", "tire", "wire", "cure", "lure", "pure", "sure", "dune", "june", "rune", "tune",
        "fume", "dome", "nome", "tome", "cede", "wade", "fade", "jade", "bade", "abode", "erode", "evade",
        "elite", "elope", "erase", "evoke", "irate", "inane", "abate", "adore", "amuse", "arose", "aside",
        "atone", "awake", "aware", "alone", "alive", "alike", "arise", "agile", "abide", "abuse", "acute",
        "adage", "alibi", "amiss", "annex", "aorta", "apron", "arena", "argon", "aroma", "arrow", "ashen",
        "aspen", "atlas", "attic", "avian", "avoid", "awoke", "bacon", "badge", "bagel", "baker", "banal",
    };

    /// <summary>
    /// Whether a spelled name is one an English reader would trip over: a real word that means
    /// something rude or silly, a slur, or plain English. Checked on every emitted name, because
    /// the generator has no idea what it is saying and a map is forever.
    /// </summary>
    public static bool Blocked(string name)
    {
        string plain = Fold(name);
        if (BlockedWords.Contains(plain) || EnglishWords.Contains(plain)) return true;
        if (Echoes(plain)) return true;
        foreach (string fragment in BlockedFragments)
            if (plain.Contains(fragment, StringComparison.Ordinal)) return true;
        return false;
    }

    private static string Fold(string s) => Core.Ascii.Fold(s);

    /// <summary>A run of two or three letters repeated back to back — "Kakumeme", "Winewine" —
    /// which reads as a stutter, not a word.</summary>
    private static bool Echoes(string plain)
    {
        for (int k = 2; k <= 3; k++)
            for (int i = 0; i + 2 * k <= plain.Length; i++)
                if (string.CompareOrdinal(plain, i, plain, i + k, k) == 0) return true;
        return false;
    }
}
