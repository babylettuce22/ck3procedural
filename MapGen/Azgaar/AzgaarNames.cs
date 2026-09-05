using System.Text;
using Ck3MapGen.Core;
using Ck3MapGen.Io;

namespace Ck3MapGen.MapGen;

/// <summary>
/// Azgaar's name generator, ported.
///
/// Azgaar names places with a Markov chain over pseudo-syllables drawn from a corpus of a few
/// hundred real names — its "name bases", which travel in the export. Porting the chain rather
/// than merely borrowing the finished names is what makes the import hold together: an Azgaar map
/// names maybe two hundred burgs and thirty states, and our generator wants names for thousands of
/// baronies. Without this, every place Azgaar happened to name would sound like one map and every
/// place it did not would sound like another.
///
/// This is a deliberate transliteration of <c>calculateChain</c> and <c>getBase</c> from Azgaar's
/// <c>names-generator.ts</c>, quirks included, because matching its output is the entire point. The
/// one line not carried over is its <c>isVowel(that) === next</c> test, which compares a boolean to
/// a string and so can never be true; Azgaar's own source marks it as preserved-by-accident
/// behaviour. Reproducing a no-op would only invite someone to "fix" it later into a real
/// comparison that changes every name on the map.
/// </summary>
public sealed class AzgaarNames
{
    /// <summary>
    /// Azgaar's vowel set, including the accented and Cyrillic characters its non-Latin bases use.
    /// Copied whole from its <c>languageUtils.ts</c>: a narrower set silently changes where
    /// syllables break, and therefore every name a non-English base produces.
    /// </summary>
    private const string Vowels =
        "aeiouyɑ'əøɛœæɶɒɨɪɔɐʊɤɯаоиеёэыуюяàèìòùỳẁȁȅȉȍȕáéíóúýẃőűâêîôûŷŵäëïöüÿẅãẽĩõũỹąęįǫųāēīōūȳăĕĭŏŭǎěǐǒǔȧėȯẏẇạẹịọụỵẉḛḭṵṳ";

    /// <summary>Syllables that can start a word — Azgaar's <c>chain[""]</c>.</summary>
    private readonly List<string> _start = [];

    /// <summary>Syllables that can follow a given letter — the rest of <c>chain</c>.</summary>
    private readonly Dictionary<char, List<string>> _next = [];

    /// <summary>The corpus itself, kept for the last-resort fallback Azgaar also falls back to.</summary>
    private readonly string[] _corpus;

    public string BaseName { get; }
    public int MinLength { get; }
    public int MaxLength { get; }

    /// <summary>Letters this base allows to appear doubled.</summary>
    public string Duplicates { get; }

    /// <summary>False when the corpus was empty or unusable and nothing can be generated from it.</summary>
    public bool IsUsable => _start.Count > 0;

    private AzgaarNames(AzgaarNameBase source)
    {
        BaseName = source.Name;
        MinLength = Math.Max(2, source.Min);
        MaxLength = Math.Max(MinLength + 1, source.Max);
        Duplicates = source.D ?? "";

        _corpus = (source.B ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (string word in _corpus) Absorb(word);
    }

    public static AzgaarNames? FromBase(AzgaarNameBase? source)
    {
        if (source is null) return null;
        var names = new AzgaarNames(source);
        return names.IsUsable ? names : null;
    }

    private static bool IsVowel(char c) => Vowels.Contains(c);

    /// <summary>
    /// Port of <c>calculateChain</c>: splits one corpus word into pseudo-syllables and files each
    /// under the letter that preceded it.
    ///
    /// The empty syllable pushed at the end of a word is not a bug to tidy away — it is how the
    /// chain encodes "a word may stop here", and <see cref="Generate"/> reads it as the terminator.
    /// </summary>
    private void Absorb(string raw)
    {
        string name = raw.Trim().ToLowerInvariant();
        if (name.Length == 0) return;

        // Azgaar applies its English digraph rules only to plain printable ASCII, on the grounds
        // that "ch" and "ee" mean nothing in a Cyrillic or Chinese corpus.
        bool basic = name.All(c => c is >= ' ' and <= '~');

        for (int i = -1; i < name.Length;)
        {
            char prev = i >= 0 ? name[i] : '\0';
            var syllable = new StringBuilder();
            bool sawVowel = false;

            for (int c = i + 1; c < name.Length && syllable.Length < 5; c++)
            {
                char that = name[c];
                char next = c + 1 < name.Length ? name[c + 1] : '\0';

                syllable.Append(that);

                if (syllable.Length == 1 && (that == ' ' || that == '-')) break;
                if (next is '\0' or ' ' or '-') break;

                if (IsVowel(that)) sawVowel = true;

                // Diphthongs that must not be split.
                if (that == 'y' && next == 'e') continue;
                if (basic && ((that == 'o' && next == 'o')
                           || (that == 'e' && next == 'e')
                           || (that == 'a' && next == 'e')
                           || (that == 'c' && next == 'h'))) continue;

                // The syllable already has its vowel and another is coming: break before it.
                if (sawVowel && c + 2 < name.Length && IsVowel(name[c + 2])) break;
            }

            string part = syllable.ToString();
            Add(prev, part);
            i += part.Length > 0 ? part.Length : 1;
        }
    }

    private void Add(char prev, string syllable)
    {
        if (prev == '\0') { _start.Add(syllable); return; }

        if (!_next.TryGetValue(prev, out var list))
            _next[prev] = list = [];

        list.Add(syllable);
    }

    /// <summary>
    /// Port of <c>getBase</c>. Walks the chain until the word terminates or runs past
    /// <paramref name="max"/>, restarting if it came out shorter than <paramref name="min"/>, then
    /// applies Azgaar's cleanup pass — collapsing disallowed doubles, capitalising after spaces and
    /// hyphens, and reducing "ae" to "e".
    /// </summary>
    public string Generate(Rng rng, int min, int max)
    {
        if (!IsUsable) return "";

        min = Math.Max(2, min);
        max = Math.Max(min + 1, max);

        var options = _start;
        string current = rng.Pick(options);
        string word = "";

        // Azgaar's own cap. It is a chain, not a grammar, and without a bound a bad corpus can
        // wander indefinitely.
        for (int step = 0; step < 20; step++)
        {
            if (current.Length == 0)
            {
                if (word.Length >= min) break;
                word = "";
                options = _start;
            }
            else if (word.Length + current.Length > max)
            {
                if (word.Length < min) word += current;
                break;
            }
            else
            {
                options = _next.TryGetValue(current[^1], out var following) && following.Count > 0
                    ? following
                    : _start;
            }

            word += current;
            current = rng.Pick(options);
        }

        return Clean(word, rng);
    }

    /// <summary>A shorter name, the way Azgaar draws one for a state out of a culture's base.</summary>
    public string GenerateShort(Rng rng)
    {
        int min = Math.Max(2, MinLength - 1);
        return Generate(rng, min, Math.Max(min + 1, MaxLength - 2));
    }

    public string Generate(Rng rng) => Generate(rng, MinLength, MaxLength);

    private string Clean(string word, Rng rng)
    {
        if (word.Length > 0 && word[^1] is '\'' or ' ' or '-') word = word[..^1];

        var sb = new StringBuilder(word.Length);
        for (int i = 0; i < word.Length; i++)
        {
            char c = word[i];
            char next = i + 1 < word.Length ? word[i + 1] : '\0';

            if (c == next && !Duplicates.Contains(c)) continue;

            if (sb.Length == 0) { sb.Append(char.ToUpperInvariant(c)); continue; }

            char last = sb[^1];
            if (last == '-' && c == ' ') continue;
            if (last is ' ' or '-') { sb.Append(char.ToUpperInvariant(c)); continue; }

            if (c == 'a' && next == 'e') continue;
            if (i + 2 < word.Length && c == next && c == word[i + 2]) continue;

            sb.Append(c);
        }

        string name = sb.ToString();

        // A stray one-letter word is a chain artefact, not a name. Azgaar runs the parts together.
        var parts = name.Split(' ');
        if (parts.Length > 1 && parts.Any(p => p.Length < 2))
            name = string.Concat(parts.Select((p, i) => i == 0 ? p : p.ToLowerInvariant()));

        if (name.Length < 2)
            return _corpus.Length > 0 ? rng.Pick(_corpus) : name;

        return name;
    }

    // --- Affixes ---------------------------------------------------------------------------------

    /// <summary>
    /// The endings that recur in this corpus, commonest first — "ingen", "bach" and "heim" out of
    /// the German base, "thorpe" and "wick" out of the English one.
    ///
    /// Our place names are a root plus an affix (see <see cref="Language.PlaceName"/>), and the
    /// chain alone only supplies roots. Mining the affixes from the same corpus keeps both halves
    /// in one voice; inventing them from the syllable generator instead is what made early builds
    /// read as an Azgaar root welded to a fantasy suffix.
    /// </summary>
    public string[] CommonEndings(int wanted, int minLength, int maxLength)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (string raw in _corpus)
        {
            string word = raw.Trim().ToLowerInvariant();
            for (int length = minLength; length <= maxLength; length++)
            {
                // The ending has to be a tail, not the whole word, or every short name in the
                // corpus becomes an "affix" and place names come out as two names stuck together.
                if (word.Length <= length + 1) continue;
                string ending = word[^length..];
                if (!ending.All(char.IsLetter)) continue;
                counts[ending] = counts.GetValueOrDefault(ending) + 1;
            }
        }

        var ranked = counts.Where(kv => kv.Value >= 2)
                           .OrderByDescending(kv => kv.Value)
                           .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                           .Select(kv => kv.Key)
                           .ToList();

        // Endings that are suffixes of one another ("gen" inside "ingen") add no variety, so keep
        // the commonest of each family.
        var chosen = new List<string>();
        foreach (string ending in ranked)
        {
            if (chosen.Count >= wanted) break;
            if (chosen.Any(k => k.EndsWith(ending, StringComparison.Ordinal)
                             || ending.EndsWith(k, StringComparison.Ordinal))) continue;
            chosen.Add(ending);
        }

        return [.. chosen];
    }
}
