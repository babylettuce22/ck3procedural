using System.Text;
using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen;

/// <summary>
/// A generated sound system, and the only source of invented words in the tool.
///
/// Every name the mod ships — people, dynasties, counties, kingdoms, cultures, gods — comes out of
/// one of these, so that names cluster the way real ones do. A language is owned by a *heritage*,
/// not by a culture, which is what makes sibling cultures sound related without any extra work:
/// they draw from the same inventory, so they come out as Norwegian and Swedish rather than as
/// Norwegian and Tamil. Crossing a heritage border is meant to be audible.
///
/// The inventory is a subset of the pools below rather than the whole of them. That is the entire
/// trick: a language that can use every sound produces mush, because every word is equally likely
/// and nothing is characteristic. Picking eight onsets out of forty means this language has a /kv/
/// and no /th/, and after a dozen words a reader can feel it.
/// </summary>
public sealed class Language
{
    // Deliberately romanised and conservative — these are read by an English-speaking player, and a
    // name they cannot pronounce reads as noise rather than as foreign.
    //
    // Split simple from complex, and drawn mostly-simple, because drawing freely from one combined
    // pool is what produces Kveikveochky: nothing stops a language taking six consonant clusters
    // and five diphthongs, and then every syllable it can build is four letters before the coda.
    // A real language has a handful of marked sounds against a plain background, and it is the
    // contrast that makes them characteristic.
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

    public string Key { get; }

    public string Name { get; private set; } = "";

    private readonly string[] _onsets;
    private readonly string[] _vowels;
    private readonly string[] _codas;

    /// <summary>How often a syllable opens with no consonant at all. Low for most languages.</summary>
    private readonly double _bareOnsetChance;

    /// <summary>How often a non-final syllable closes. High values give a consonant-heavy language.</summary>
    private readonly double _medialCodaChance;

    private readonly double _finalCodaChance;

    /// <summary>Word-final fragments that mark a name as a man's or a woman's in this language.</summary>
    public string[] MaleEndings { get; private set; } = [];

    public string[] FemaleEndings { get; private set; } = [];

    /// <summary>Title-name suffixes per tier — this language's answers to -by, -mark and -land.</summary>
    public string[] BaronySuffixes { get; private set; } = [];

    public string[] CountySuffixes { get; private set; } = [];
    public string[] DuchySuffixes { get; private set; } = [];
    public string[] KingdomSuffixes { get; private set; } = [];

    private Language(string key, Rng rng)
    {
        Key = key;

        // Mostly plain sounds with a few marked ones. The vowel ratio matters most: a language with
        // more diphthongs than plain vowels has no short words in it at all.
        _onsets = [.. Draw(SimpleOnsets, rng.Int(6, 9), rng), .. Draw(ClusterOnsets, rng.Int(2, 4), rng)];
        _vowels = [.. Draw(SimpleVowels, rng.Int(3, 4), rng), .. Draw(ComplexVowels, rng.Int(0, 2), rng)];
        _codas = [.. Draw(SimpleCodas, rng.Int(3, 5), rng), .. Draw(ComplexCodas, rng.Int(0, 2), rng)];

        _bareOnsetChance = rng.Decimal(0.05, 0.30);
        _medialCodaChance = rng.Decimal(0.10, 0.40);
        _finalCodaChance = rng.Decimal(0.25, 0.70);
    }

    /// <summary>
    /// Builds a language by drawing an inventory out of the pools. Sizes are small on purpose;
    /// see the class remarks.
    ///
    /// Two phases, because the affixes are themselves words in the language and so cannot be
    /// produced until the inventory they are drawn from exists.
    /// </summary>
    public static Language Create(string key, Rng rng)
    {
        var language = new Language(key, rng);

        // Endings are one syllable: they must read as an inflection stuck onto a name, not as a
        // second name.
        language.MaleEndings = language.Fragments(rng, 4, false);
        language.FemaleEndings = language.Fragments(rng, 4, true);
        language.BaronySuffixes = language.Fragments(rng, 6, false);
        language.CountySuffixes = language.Fragments(rng, 6, false);
        language.DuchySuffixes = language.Fragments(rng, 5, false);
        language.KingdomSuffixes = language.Fragments(rng, 5, false);

        language.Name = Capitalise(language.Root(rng, 2, 3));
        return language;
    }

    /// <summary>
    /// A word in this language, capitalised and ready to be a name.
    /// </summary>
    public string Word(Rng rng, int minSyllables = 2, int maxSyllables = 3)
        => Capitalise(Root(rng, minSyllables, maxSyllables));

    /// <summary>A man's name: a root plus, sometimes, a masculine ending.</summary>
    public string MaleName(Rng rng)
        => Capitalise(Join(Root(rng, 1, 2), rng.Chance(0.55) ? rng.Pick(MaleEndings) : ""));

    /// <summary>A woman's name. The feminine ending is applied more often, so the two sets stay
    /// distinguishable to a player who is skimming rather than reading.</summary>
    public string FemaleName(Rng rng)
        => Capitalise(Join(Root(rng, 1, 2), rng.Chance(0.80) ? rng.Pick(FemaleEndings) : ""));

    /// <summary>A place name: a root plus this language's suffix for the tier.</summary>
    public string PlaceName(Rng rng, string[] suffixes)
        => Capitalise(Join(Root(rng, 1, 2), rng.Chance(0.75) ? rng.Pick(suffixes) : ""));

    /// <summary>
    /// Bare syllable string, uncapitalised. Everything above is a wrapper on this.
    /// </summary>
    private string Root(Rng rng, int minSyllables, int maxSyllables)
    {
        int count = rng.Int(minSyllables, maxSyllables);
        var sb = new StringBuilder();

        for (int i = 0; i < count; i++)
        {
            bool last = i == count - 1;

            if (!rng.Chance(_bareOnsetChance) || i > 0) sb.Append(rng.Pick(_onsets));
            sb.Append(rng.Pick(_vowels));

            double codaChance = last ? _finalCodaChance : _medialCodaChance;
            if (rng.Chance(codaChance)) sb.Append(rng.Pick(_codas));
        }

        return Tidy(sb.ToString());
    }

    /// <summary>
    /// Short word-final fragments, used for grammatical endings and place-name suffixes. Feminine
    /// ones are forced to end on a vowel, which is the one sound-symbolic convention worth keeping:
    /// it is near-universal across the languages a player will have names from.
    /// </summary>
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

        // A language that drew an unlucky inventory still has to return something usable.
        if (result.Count == 0) result.Add(vowelFinal ? "a" : "en");
        return [.. result];
    }

    private static string Join(string stem, string ending)
    {
        if (ending.Length == 0) return stem;

        // Do not let the seam produce a sound the language never uses: a vowel meeting a vowel, or
        // the same consonant twice. Dropping one character is enough and keeps the ending readable.
        bool stemVowel = IsVowel(stem[^1]);
        bool endingVowel = IsVowel(ending[0]);

        if (stemVowel && endingVowel) return Tidy(stem[..^1] + ending);
        if (stem[^1] == ending[0]) return Tidy(stem + ending[1..]);
        return Tidy(stem + ending);
    }

    /// <summary>
    /// Collapses the runs that syllable concatenation produces and nothing else. Three of the same
    /// letter is always a mistake, and a four-consonant pile-up is unreadable however plausible the
    /// pieces were individually.
    /// </summary>
    private static string Tidy(string word)
    {
        var sb = new StringBuilder(word.Length);
        int consonantRun = 0;

        foreach (char c in word)
        {
            if (sb.Length > 0 && sb[^1] == c && sb.Length >= 2 && sb[^2] == c) continue;

            if (IsVowel(c)) consonantRun = 0;
            else if (++consonantRun > 3) { consonantRun--; continue; }

            sb.Append(c);
        }

        return sb.ToString();
    }

    private static bool IsVowel(char c) => c is 'a' or 'e' or 'i' or 'o' or 'u' or 'y';

    private static string Capitalise(string word)
        => word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..];

    /// <summary>Distinct draw without replacement, so an inventory never lists a sound twice.</summary>
    private static string[] Draw(string[] pool, int count, Rng rng)
    {
        var copy = pool.ToList();
        rng.Shuffle(copy);
        return [.. copy.Take(Math.Min(count, copy.Count)).Distinct()];
    }
}
