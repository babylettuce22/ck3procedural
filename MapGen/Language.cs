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

    // Standard CK3-safe Latin-1 diacritics for visual flavor
    private static readonly Dictionary<char, char[]> DiacriticMap = new()
    {
        { 'a', ['á', 'ä', 'â', 'å'] },
        { 'e', ['é', 'ë', 'ê'] },
        { 'i', ['í', 'ï', 'î'] },
        { 'o', ['ó', 'ö', 'ô', 'ø'] },
        { 'u', ['ú', 'ü', 'û'] }
    };

    public string Key { get; }
    public string Name { get; private set; } = "";

    private readonly string[] _onsets;
    private readonly string[] _vowels;
    private readonly string[] _codas;

    private readonly double _bareOnsetChance;
    private readonly double _medialCodaChance;
    private readonly double _finalCodaChance;

    // --- NEW: Orthographic and Morphological Flavor Traits ---

    /// <summary>Does this language use prefixes for places (like Caer- or Al-) instead of suffixes?</summary>
    private readonly bool _usesPlacePrefixes;

    /// <summary>Chance to inject an apostrophe between syllables (e.g., M'baku, K'tah).</summary>
    private readonly double _apostropheChance;

    /// <summary>A specific accent this language applies to a specific vowel (e.g., all 'o's become 'ö').</summary>
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

        // 1 in 4 languages use place-name prefixes instead of suffixes.
        _usesPlacePrefixes = rng.Chance(0.25);

        // Very rare chance for apostrophe-heavy languages (alien/ancient feel).
        _apostropheChance = rng.Chance(0.10) ? rng.Decimal(0.1, 0.3) : 0.0;

        // 30% chance for a language to have a signature diacritic (e.g., Norse 'ø' or German 'ü').
        if (rng.Chance(0.30))
        {
            _targetVowelForAccent = rng.Pick(['a', 'e', 'i', 'o', 'u']);
            _accentCharacter = rng.Pick(DiacriticMap[_targetVowelForAccent]);
        }
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

    public string Word(Rng rng, int minSyllables = 2, int maxSyllables = 3)
        => Capitalise(Root(rng, minSyllables, maxSyllables));

    public string MaleName(Rng rng)
        => Capitalise(Join(Root(rng, 1, 2), rng.Chance(0.55) ? rng.Pick(MaleEndings) : ""));

    public string FemaleName(Rng rng)
        => Capitalise(Join(Root(rng, 1, 2), rng.Chance(0.80) ? rng.Pick(FemaleEndings) : ""));

    /// <summary>A place name that adapts to whether the language favors prefixes or suffixes.</summary>
    public string PlaceName(Rng rng, string[] affixes)
    {
        string root = Root(rng, 1, 2);
        if (!rng.Chance(0.75)) return Capitalise(root);

        string affix = rng.Pick(affixes);

        // Some languages use a hyphen for affixes (e.g., Al-Karak vs Alkerek)
        bool useHyphen = rng.Chance(0.20);

        if (_usesPlacePrefixes)
        {
            return Capitalise(useHyphen ? $"{affix}-{root}" : Join(affix, root));
        }

        return Capitalise(useHyphen ? $"{root}-{affix}" : Join(root, affix));
    }

    /// <summary>
    /// Generates a compound name by merging two distinct roots. Excellent for Dynasties or Major cities.
    /// (e.g., "Black-wood", "Gond-wana").
    /// </summary>
    public string CompoundName(Rng rng)
    {
        string root1 = Root(rng, 1, 2);
        string root2 = Root(rng, 1, 2);
        return Capitalise(rng.Chance(0.3) ? $"{root1}-{root2}" : Join(root1, root2));
    }

    private string Root(Rng rng, int minSyllables, int maxSyllables)
    {
        int count = rng.Int(minSyllables, maxSyllables);
        var sb = new StringBuilder();

        for (int i = 0; i < count; i++)
        {
            bool last = i == count - 1;

            // Orthographic flavor: Apostrophes between syllables
            if (i > 0 && rng.Chance(_apostropheChance)) sb.Append('\'');

            if (!rng.Chance(_bareOnsetChance) || i > 0) sb.Append(rng.Pick(_onsets));

            string vowel = rng.Pick(_vowels);
            // Apply language's signature accent if applicable
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

        // Vowel meeting a vowel: drop the first one
        if (firstEndsInVowel && secondStartsInVowel)
            return Tidy(first[..^1] + second);

        // Identical letters meeting: drop one
        if (first[^1] == second[0])
            return Tidy(first + second[1..]);

        // NEW: Smarter Consonant smoothing. 
        // If a heavy coda meets a heavy onset, it creates an unpronounceable seam (e.g. "rk" + "st" -> "rkst").
        // We drop the coda to smooth the transition.
        if (!firstEndsInVowel && !secondStartsInVowel)
        {
            int trailingConsonants = first.Length - Math.Max(0, LastVowelIndex(first) + 1);
            int leadingConsonants = FirstVowelIndex(second);

            if (trailingConsonants + leadingConsonants > 3)
            {
                // Strip the trailing consonants from the first part
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
            else if (c != '\'' && c != '-') // Don't count punctuation as consonants
            {
                if (++consonantRun > 3) { consonantRun--; continue; }
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    private static bool IsVowelOrAccent(char c)
        => c is 'a' or 'e' or 'i' or 'o' or 'u' or 'y' or 'á' or 'ä' or 'â' or 'å' or 'é' or 'ë' or 'ê' or 'í' or 'ï' or 'î' or 'ó' or 'ö' or 'ô' or 'ø' or 'ú' or 'ü' or 'û';

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

        // Handle names that start with an apostrophe or hyphen safely
        int firstChar = 0;
        while (firstChar < word.Length && !char.IsLetter(word[firstChar]))
            firstChar++;

        if (firstChar >= word.Length) return word;

        Span<char> chars = word.ToCharArray();
        chars[firstChar] = char.ToUpperInvariant(chars[firstChar]);

        // Capitalize after hyphens for compound names (e.g. Al-Fariq)
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
        // Note: Distinct() is technically redundant here since your base arrays are already distinct, 
        // but left it in case you modify the source arrays to add weighted probabilities.
        return [.. copy.Take(Math.Min(count, copy.Count)).Distinct()];
    }
}