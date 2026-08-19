using System.Text;
using System.Collections.Generic;
using System.Linq;
using System;
using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen;

public sealed class Language
{
    private static readonly string[] SimpleOnsets =
        ["b", "d", "f", "g", "h", "j", "k", "l", "m", "n", "p", "r", "s", "t", "v", "w", "z"];

    private static readonly string[] ClusterOnsets =
    [
        "bl", "br", "dr", "fl", "fr", "gl", "gr", "kl", "kr", "kv", "pl", "pr", "sk", "sl", "sn",
        "sp", "st", "sv", "tr", "th", "sh", "ch", "kh", "hr", "hl", "mj",
    ];

    private static readonly string[] SimpleVowels = ["a", "e", "i", "o", "u", "y"];

    private static readonly string[] ComplexVowels =
        ["ae", "ai", "au", "ea", "ei", "eo", "ia", "ie", "io", "oa", "oi", "ou", "ua", "aa", "ee", "oo"];

    private static readonly string[] SimpleCodas = ["n", "m", "r", "l", "s", "t", "k", "d", "g"];

    private static readonly string[] ComplexCodas =
        ["th", "sh", "st", "nd", "ng", "nt", "rk", "rn", "rd", "ls", "ft", "sk", "lm", "rg", "ts"];

    private static readonly Dictionary<char, char[]> DiacriticMap = new()
    {
        { 'a', ['á', 'ä', 'â', 'å', 'æ'] },
        { 'e', ['é', 'ë', 'ê'] },
        { 'i', ['í', 'ï', 'î'] },
        { 'o', ['ó', 'ö', 'ô', 'ø'] },
        { 'u', ['ú', 'ü', 'û'] }
    };

    // --- Old & Middle English Phoneme & Affix Pools ---
    private static readonly string[] EnglishOnsets =
    [
        "b", "c", "d", "f", "g", "h", "k", "l", "m", "n", "p", "r", "s", "t", "w", "y",
        "br", "cl", "cr", "dr", "fl", "fr", "gr", "pr", "sc", "sh", "st", "sw", "th", "tr", "tw", "wh", "wr"
    ];

    private static readonly string[] EnglishVowels =
        ["a", "e", "i", "o", "u", "ea", "ee", "eo", "ae", "oo", "ou", "ay", "ow"];

    private static readonly string[] EnglishCodas =
    [
        "d", "g", "k", "l", "m", "n", "r", "s", "t", "th", "sh", "ch", "ck", "gh",
        "ft", "ld", "nd", "ng", "nk", "rd", "rk", "rn", "rt", "st"
    ];

    private static readonly string[] EnglishBaronyAffixes =
        ["ton", "ham", "ford", "wick", "bury", "stead", "dale", "by", "ster", "ing", "mere", "wood", "field", "croft"];

    private static readonly string[] EnglishCountyAffixes =
        ["shire", "land", "fold", "march", "sart", "halgh", "worth", "don", "bridge", "mouth"];

    private static readonly string[] EnglishDuchyAffixes =
        ["shire", "land", "reach", "march", "realm", "fold", "sex"];

    private static readonly string[] EnglishKingdomAffixes =
        ["land", "realm", "ia", "cia", "ria"];

    private static readonly string[] EnglishMaleEndings =
        ["ric", "wald", "bert", "gar", "mund", "ed", "red", "ward", "win", "hard", "man"];

    private static readonly string[] EnglishFemaleEndings =
        ["hild", "wen", "gyth", "burg", "fled", "thryth", "eth", "is", "eva"];


    public string Key { get; }
    public string Name { get; private set; } = "";

    private readonly string[] _onsets;
    private readonly string[] _vowels;
    private readonly string[] _codas;

    /// <summary>
    /// Set when this language was built from an imported Azgaar name base, in which case roots come
    /// out of its Markov chain instead of the syllable machinery above and every other member here
    /// keeps working unchanged. See <see cref="FromNameBase"/>.
    /// </summary>
    private readonly AzgaarNames? _markov;

    private readonly double _bareOnsetChance;
    private readonly double _medialCodaChance;
    private readonly double _finalCodaChance;

    private readonly bool _usesPlacePrefixes;
    private readonly double _apostropheChance;
    private readonly char _targetVowelForAccent;
    private readonly char _accentCharacter;

    public string[] MaleEndings { get; private set; } = [];
    public string[] FemaleEndings { get; private set; } = [];
    public string[] BaronyAffixes { get; private set; } = [];
    public string[] CountyAffixes { get; private set; } = [];
    public string[] DuchyAffixes { get; private set; } = [];
    public string[] KingdomAffixes { get; private set; } = [];

    private Language(string key, Rng rng)
    {
        Key = key;

        _onsets = [.. Draw(SimpleOnsets, rng.Int(6, 9), rng), .. Draw(ClusterOnsets, rng.Int(2, 4), rng)];
        _vowels = [.. Draw(SimpleVowels, rng.Int(3, 4), rng), .. Draw(ComplexVowels, rng.Int(0, 2), rng)];
        _codas = [.. Draw(SimpleCodas, rng.Int(3, 5), rng), .. Draw(ComplexCodas, rng.Int(0, 2), rng)];

        _bareOnsetChance = rng.Decimal(0.05, 0.30);
        _medialCodaChance = rng.Decimal(0.10, 0.40);
        _finalCodaChance = rng.Decimal(0.25, 0.70);

        _usesPlacePrefixes = rng.Chance(0.25);
        _apostropheChance = rng.Chance(0.10) ? rng.Decimal(0.1, 0.3) : 0.0;

        if (rng.Chance(0.30))
        {
            _targetVowelForAccent = rng.Pick(['a', 'e', 'i', 'o', 'u']);
            _accentCharacter = rng.Pick(DiacriticMap[_targetVowelForAccent]);
        }
    }

    /// <summary>Constructs a language specifically tuned for Old/Middle English phonology.</summary>
    private Language(string key, Rng rng, bool isAnglic)
    {
        Key = key;

        _onsets = Draw(EnglishOnsets, rng.Int(12, 18), rng);
        _vowels = Draw(EnglishVowels, rng.Int(6, 10), rng);
        _codas = Draw(EnglishCodas, rng.Int(8, 14), rng);

        _bareOnsetChance = 0.10;
        _medialCodaChance = 0.35;
        _finalCodaChance = 0.65;

        // English predominantly uses suffixes (e.g. Oxford, Nottingham)
        _usesPlacePrefixes = rng.Chance(0.10);
        _apostropheChance = 0.0; // English place/person names rarely use inner apostrophes

        // 20% chance for an Old-English 'æ' accent flavor
        if (rng.Chance(0.20))
        {
            _targetVowelForAccent = 'a';
            _accentCharacter = 'æ';
        }
    }

    /// <summary>
    /// Builds a language whose words come from an imported Azgaar name base.
    ///
    /// The phoneme pools are left empty on purpose: nothing reads them once <see cref="_markov"/>
    /// is set, and filling them with plausible-looking values would only hide the fact that this
    /// language does not work that way.
    /// </summary>
    private Language(string key, AzgaarNames markov, Rng rng)
    {
        Key = key;
        _markov = markov;

        _onsets = [];
        _vowels = [];
        _codas = [];

        _bareOnsetChance = 0;
        _medialCodaChance = 0;
        _finalCodaChance = 0;

        // Whether a corpus puts its affixes in front is a property of the corpus, not something to
        // roll for — and the mined endings below are, by construction, endings.
        _usesPlacePrefixes = false;
        _apostropheChance = 0;
    }

    /// <summary>
    /// A language backed by one of Azgaar's name bases, so that places our generator names itself
    /// sound like the places Azgaar named.
    ///
    /// Affixes are mined from the same corpus rather than invented, which is what keeps a generated
    /// county from reading as an Azgaar root with a fantasy suffix bolted on. When the corpus turns
    /// out too thin to yield recurring endings, the invented pools stand in for that one list and
    /// the roots still come from the chain.
    /// </summary>
    public static Language FromNameBase(string key, AzgaarNames markov, Rng rng, string? name = null)
    {
        var language = new Language(key, markov, rng);
        var fallback = new Language(key, rng);

        language.MaleEndings = Pick(markov.CommonEndings(6, 2, 4), () => fallback.Fragments(rng, 4, false));
        language.FemaleEndings = Pick(markov.CommonEndings(6, 2, 3), () => fallback.Fragments(rng, 4, true));
        language.BaronyAffixes = Pick(markov.CommonEndings(8, 3, 6), () => fallback.Fragments(rng, 6, false));
        language.CountyAffixes = Pick(markov.CommonEndings(6, 4, 7), () => fallback.Fragments(rng, 6, false));
        language.DuchyAffixes = Pick(markov.CommonEndings(5, 4, 7), () => fallback.Fragments(rng, 5, false));
        language.KingdomAffixes = Pick(markov.CommonEndings(5, 2, 5), () => fallback.Fragments(rng, 5, false));

        language.Name = name ?? Capitalise(markov.Generate(rng, 4, 9));
        return language;

        // Two is the fewest that reads as a pattern rather than as one word's tail repeated.
        static string[] Pick(string[] mined, Func<string[]> invented)
            => mined.Length >= 2 ? mined : invented();
    }

    public static Language Create(string key, Rng rng)
    {
        var language = new Language(key, rng);

        language.MaleEndings = language.Fragments(rng, 4, false);
        language.FemaleEndings = language.Fragments(rng, 4, true);
        language.BaronyAffixes = language.Fragments(rng, 6, false);
        language.CountyAffixes = language.Fragments(rng, 6, false);
        language.DuchyAffixes = language.Fragments(rng, 5, false);
        language.KingdomAffixes = language.Fragments(rng, 5, false);

        language.Name = Capitalise(language.Root(rng, 2, 3));
        return language;
    }

    /// <summary>Creates an Old/Middle English sound-alike language.</summary>
    public static Language CreateAnglic(string key, Rng rng)
    {
        var language = new Language(key, rng, isAnglic: true);

        language.MaleEndings = EnglishMaleEndings;
        language.FemaleEndings = EnglishFemaleEndings;
        language.BaronyAffixes = EnglishBaronyAffixes;
        language.CountyAffixes = EnglishCountyAffixes;
        language.DuchyAffixes = EnglishDuchyAffixes;
        language.KingdomAffixes = EnglishKingdomAffixes;

        // Names like "Anglish", "Aenglish", "Ealdic", "Aenglisc", "Westran"
        string[] anglicNames = ["Anglish", "Aenglisc", "Ealdic", "Westran", "Seaxan", "Mierce", "Northan"];
        language.Name = rng.Pick(anglicNames);

        return language;
    }

    public string Word(Rng rng, int minSyllables = 2, int maxSyllables = 3)
        => Capitalise(Root(rng, minSyllables, maxSyllables));

    public string MaleName(Rng rng)
        => Capitalise(Join(Root(rng, 1, 2), rng.Chance(0.55) ? rng.Pick(MaleEndings) : ""));

    public string FemaleName(Rng rng)
        => Capitalise(Join(Root(rng, 1, 2), rng.Chance(0.80) ? rng.Pick(FemaleEndings) : ""));

    public string PlaceName(Rng rng, string[] affixes)
    {
        string root = Root(rng, 1, 2);
        if (!rng.Chance(0.75)) return Capitalise(root);

        string affix = rng.Pick(affixes);
        bool useHyphen = rng.Chance(0.10); // Less hyphens for English place names

        if (_usesPlacePrefixes)
        {
            return Capitalise(useHyphen ? $"{affix}-{root}" : Join(affix, root));
        }

        return Capitalise(useHyphen ? $"{root}-{affix}" : Join(root, affix));
    }

    public string CompoundName(Rng rng)
    {
        string root1 = Root(rng, 1, 2);
        string root2 = Root(rng, 1, 2);
        return Capitalise(rng.Chance(0.3) ? $"{root1}-{root2}" : Join(root1, root2));
    }

    private string Root(Rng rng, int minSyllables, int maxSyllables)
    {
        if (_markov is not null)
        {
            // Syllable counts translated into letter counts, because that is what the chain is
            // bounded by. The floor of three is deliberate: a one-syllable request scaled down from
            // a short base otherwise yields names like "As" and "Sa", which read as truncation
            // rather than as words. Azgaar's own bases never go below four.
            int lo = Math.Max(3, (int)Math.Round(_markov.MinLength * minSyllables / 2.0));
            int hi = Math.Max(lo + 1, (int)Math.Round(_markov.MaxLength * maxSyllables / 3.0));
            return _markov.Generate(rng, lo, hi).ToLowerInvariant();
        }

        int count = rng.Int(minSyllables, maxSyllables);
        var sb = new StringBuilder();

        for (int i = 0; i < count; i++)
        {
            bool last = i == count - 1;

            if (i > 0 && rng.Chance(_apostropheChance)) sb.Append('\'');

            if (!rng.Chance(_bareOnsetChance) || i > 0) sb.Append(rng.Pick(_onsets));

            string vowel = rng.Pick(_vowels);
            if (_accentCharacter != '\0')
                vowel = vowel.Replace(_targetVowelForAccent, _accentCharacter);

            sb.Append(vowel);

            double codaChance = last ? _finalCodaChance : _medialCodaChance;
            if (rng.Chance(codaChance)) sb.Append(rng.Pick(_codas));
        }

        return Tidy(sb.ToString());
    }

    private string[] Fragments(Rng rng, int count, bool vowelFinal)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        for (int attempt = 0; attempt < count * 12 && result.Count < count; attempt++)
        {
            var sb = new StringBuilder();
            if (rng.Chance(0.75)) sb.Append(rng.Pick(_onsets));
            sb.Append(rng.Pick(_vowels));
            if (!vowelFinal && rng.Chance(0.6)) sb.Append(rng.Pick(_codas));

            string fragment = Tidy(sb.ToString());
            if (fragment.Length is > 0 and <= 5) result.Add(fragment);
        }

        if (result.Count == 0) result.Add(vowelFinal ? "a" : "en");
        return [.. result];
    }

    private static string Join(string first, string second)
    {
        if (second.Length == 0) return first;
        if (first.Length == 0) return second;

        bool firstEndsInVowel = IsVowelOrAccent(first[^1]);
        bool secondStartsInVowel = IsVowelOrAccent(second[0]);

        if (firstEndsInVowel && secondStartsInVowel)
            return Tidy(first[..^1] + second);

        if (first[^1] == second[0])
            return Tidy(first + second[1..]);

        if (!firstEndsInVowel && !secondStartsInVowel)
        {
            int trailingConsonants = first.Length - Math.Max(0, LastVowelIndex(first) + 1);
            int leadingConsonants = FirstVowelIndex(second);

            if (trailingConsonants + leadingConsonants > 3)
            {
                first = first[..^trailingConsonants];
            }
        }

        return Tidy(first + second);
    }

    private static string Tidy(string word)
    {
        var sb = new StringBuilder(word.Length);
        int consonantRun = 0;

        foreach (char c in word)
        {
            if (sb.Length > 0 && sb[^1] == c && sb.Length >= 2 && sb[^2] == c) continue;

            if (IsVowelOrAccent(c))
                consonantRun = 0;
            else if (c != '\'' && c != '-')
            {
                if (++consonantRun > 3) { consonantRun--; continue; }
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    private static bool IsVowelOrAccent(char c)
        => c is 'a' or 'e' or 'i' or 'o' or 'u' or 'y' or 'á' or 'ä' or 'â' or 'å' or 'æ' or 'é' or 'ë' or 'ê' or 'í' or 'ï' or 'î' or 'ó' or 'ö' or 'ô' or 'ø' or 'ú' or 'ü' or 'û';

    private static int LastVowelIndex(string s)
    {
        for (int i = s.Length - 1; i >= 0; i--)
            if (IsVowelOrAccent(s[i])) return i;
        return -1;
    }

    private static int FirstVowelIndex(string s)
    {
        for (int i = 0; i < s.Length; i++)
            if (IsVowelOrAccent(s[i])) return i;
        return s.Length;
    }

    private static string Capitalise(string word)
    {
        if (word.Length == 0) return word;

        int firstChar = 0;
        while (firstChar < word.Length && !char.IsLetter(word[firstChar]))
            firstChar++;

        if (firstChar >= word.Length) return word;

        Span<char> chars = word.ToCharArray();
        chars[firstChar] = char.ToUpperInvariant(chars[firstChar]);

        for (int i = firstChar + 1; i < chars.Length - 1; i++)
        {
            if (chars[i] == '-' && char.IsLetter(chars[i + 1]))
                chars[i + 1] = char.ToUpperInvariant(chars[i + 1]);
        }

        return new string(chars);
    }

    private static string[] Draw(string[] pool, int count, Rng rng)
    {
        var copy = pool.ToList();
        rng.Shuffle(copy);
        return [.. copy.Take(Math.Min(count, copy.Count)).Distinct()];
    }
}