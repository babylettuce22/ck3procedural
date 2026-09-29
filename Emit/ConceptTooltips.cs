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
/// The house rule from the rank titles' in-game test: the header IS the gloss. A description is a
/// single line where it adds something, and empty where the header says it all — an empty value is
/// still written, because a missing key shows as the raw key and vanilla's description box has no
/// visibility condition.
/// </summary>
internal sealed class ConceptTooltips
{
    private readonly SortedDictionary<string, (string Name, string Description)> _concepts =
        new(StringComparer.Ordinal);

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
    /// The word as a link to its concept: shown as written, glossed on hover. A word with a quote in
    /// it would end the argument early, so it is left plain — callers that need a possessive link
    /// the bare word and append the "'s" outside the link.
    /// </summary>
    public static string Linked(string concept, string word)
        => word.Contains('\'') ? word : $"[Concept('{concept}','{word}')|E]";

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
