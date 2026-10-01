using Ck3MapGen.Io;

namespace Ck3MapGen.Emit;

/// <summary>
/// Hover glosses for generated words. A coined word — a native rank, a god's name, a priest's title —
/// means nothing to a player on sight, so its localisation is written as a link to a hidden game
/// concept, <c>[Concept('key','word')|E]</c>: the word shows as written, and hovering it shows the
/// concept's English name as the header and a one-line description under it.
///
/// Built for the native rank titles (<see cref="NativeRankWriter"/>, confirmed in game 2026-09-27)
/// and shared since with the religion glossary (<see cref="ReligionGlossary"/>). Each user owns its
/// own concept file and loc file; this class collects the concepts and writes them.
///
/// The house rule from the rank titles' in-game test: the header IS the gloss, and the description
/// is a single line under it. Never leave the description empty: vanilla's <c>Description</c> textbox
/// (object_tooltip_pop_out in gui/shared/cooltip.gui) has no visibility condition and keeps one
/// line's height with no text, so an empty value shows as a blank gap under the header (seen in game
/// 2026-09-28). A missing key is worse still — it shows the raw key.
/// </summary>
internal sealed class ConceptTooltips
{
    private readonly SortedDictionary<string, (string Name, string Description)> _concepts =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Words with an apostrophe in them ("Groo'ubuq"), each under a loc key of its own; see
    /// <see cref="Linked"/>. Written beside the concepts' own localisation.
    /// </summary>
    private readonly SortedDictionary<string, string> _words = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Concept, string Word), string> _wordKeys = [];

    public int Count => _concepts.Count;

    /// <summary>
    /// Registers <paramref name="key"/> (the first registration of a key wins) and returns
    /// <paramref name="word"/> linked to it.
    /// </summary>
    public string Link(string key, string name, string description, string word)
    {
        _concepts.TryAdd(key, (name, description));
        return Linked(key, word);
    }

    /// <summary>
    /// The word as a link to its concept: shown as written, glossed on hover. Callers that need a
    /// possessive link the bare word and append the "'s" outside the link.
    ///
    /// A word with an apostrophe would end the quoted argument early, so it goes in a loc key of
    /// its own and the link reads it back with <c>Localize</c>, as vanilla does
    /// (<c>Concept('artifact_claim', Localize('game_concept_claimants'))</c>). Until 2026-09-30
    /// such words were left plain, so every coined word with an apostrophe had no tooltip.
    /// </summary>
    public string Linked(string concept, string word)
    {
        if (!word.Contains('\'')) return $"[Concept('{concept}','{word}')|E]";

        if (!_wordKeys.TryGetValue((concept, word), out var wordKey))
        {
            // Numbered within the concept: a word and its plural share one, so need two keys.
            wordKey = $"{concept}_word{_wordKeys.Keys.Count(k => k.Concept == concept)}";
            _wordKeys[(concept, word)] = wordKey;
            _words[wordKey] = word;
        }

        return $"[Concept('{concept}',Localize('{wordKey}'))|E]";
    }

    /// <summary>
    /// Writes the concepts — each hidden from the encyclopedia, since they are glosses, not rules —
    /// and their localisation (<c>game_concept_&lt;key&gt;</c> and <c>_desc</c>).
    /// </summary>
    public void Write(string conceptsPath, string locPath, string comment)
    {
        var b = new JominiBuilder();
        b.Comment(comment);
        b.Blank();

        var loc = new LocFile();
        foreach (var (key, (name, description)) in _concepts)
        {
            using (b.Block(key)) b.Field("shown_in_encyclopedia", "no");
            loc.AddBuilt($"game_concept_{key}", name);
            loc.AddBuilt($"game_concept_{key}_desc", description);
        }

        // The apostrophe words Linked read back with Localize. Through Add, not AddBuilt: they are
        // plain text, and nothing in them is markup.
        foreach (var (key, word) in _words) loc.Add(key, word);

        Directory.CreateDirectory(Path.GetDirectoryName(conceptsPath)!);
        ParadoxText.WriteBom(conceptsPath, b.ToString());
        loc.Write(locPath);
    }

    /// <summary>Removes a previous run's files, for a world written with the glosses off.</summary>
    public static void Delete(params string[] paths)
    {
        foreach (string path in paths)
            if (File.Exists(path)) File.Delete(path);
    }
}
