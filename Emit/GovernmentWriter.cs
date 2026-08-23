using System.Text;
using Ck3MapGen.Io;

namespace Ck3MapGen.Emit;

/// <summary>
/// Re-emits vanilla governments whose own rules would throw away the names this tool works out.
///
/// Most of what CK3 calls a title's name is localisation, which is why
/// <see cref="TitleTierWriter"/> can give a people its own words by writing keys. A few governments
/// do not go through localisation at all. <c>nomad_government</c> carries
/// <c>uses_culture_and_house_head_named_realms = yes</c> in its <c>government_rules</c>, and that is
/// an engine rule, not a key: a horde is displayed as its culture and its house head — "the
/// Dulandir", "Bulan's Horde" — and the title's own name and tier word are never consulted. Every
/// name the import borrowed for a nomadic state was being discarded at the last step, with nothing
/// logged, because from the game's point of view nothing was wrong.
///
/// The fix is one word, so the whole definition is lifted from the installed game and re-emitted
/// with that word changed rather than hand-copied. A hand copy is a ninety-line snapshot of a
/// definition Paradox edits every major patch, and the failure mode of a stale one is silent: the
/// mod would keep shipping last year's nomad modifiers and men-at-arms over the top of the current
/// game. Reading the live file means this only ever changes the one rule it means to change.
///
/// Nothing is written when the map has no nomads, which is the usual case.
/// </summary>
public static class GovernmentWriter
{
    /// <summary>The engine rule that makes a nomad realm ignore its title's name.</summary>
    private const string CultureNamedRealms = "uses_culture_and_house_head_named_realms";

    /// <summary>
    /// Ships a nomad government that keeps its title's name, when the map has nomads to ship it for.
    /// </summary>
    /// <returns>True when the override was written.</returns>
    public static bool WriteNomadNaming(string modDir, string gameDir, bool anyNomads)
    {
        if (!anyNomads) return false;

        string source = Path.Combine(gameDir, "common", "governments", "00_government_types.txt");
        if (!File.Exists(source)) return false;

        string? block = Block(File.ReadAllText(source), "nomad_government");
        if (block is null) return false;

        // Only the rule, and only when it is on. A patch that removes it leaves nothing to do, and
        // rewriting the definition anyway would pin a copy of it for no reason.
        if (!block.Contains($"{CultureNamedRealms} = yes", StringComparison.Ordinal)) return false;

        block = block.Replace($"{CultureNamedRealms} = yes", $"{CultureNamedRealms} = no",
                              StringComparison.Ordinal);

        var b = new JominiBuilder();
        b.Comment($"""
                   Vanilla's nomad_government, read from the installed game, with one rule changed:
                   {CultureNamedRealms} is off, so a horde is called what its title is called.

                   That rule is engine-side, not localisation, and it overrides both the title's name
                   and its tier word — every borrowed or generated name on a nomadic realm was being
                   discarded by it. Everything else here is vanilla's, verbatim.

                   The filename sorts after 00_government_types.txt on purpose. common/governments
                   merges by key and the last definition wins, so a file named for vanilla's would
                   replace the whole file rather than this one entry.
                   """);
        b.Blank();

        // Vanilla's own text, one substitution in. Nothing here is ours to re-indent.
        b.Raw(block);
        b.Blank();

        string dir = Path.Combine(modDir, "common", "governments");
        Directory.CreateDirectory(dir);
        ParadoxText.WriteBom(Path.Combine(dir, "zz_generated_nomad_government.txt"), b.ToString());

        Console.WriteLine("  governments: nomad realms keep their own names " +
                          $"({CultureNamedRealms} off)");
        return true;
    }

    /// <summary>
    /// One top-level <c>key = { ... }</c> block, braces balanced, or null.
    ///
    /// Brace-counted rather than regex-matched because the block is ninety lines deep in nested
    /// braces and the closing one has to be the *matching* one. Quotes are not tracked: nothing in
    /// this file puts a brace inside a string, and a comment cannot either.
    /// </summary>
    private static string? Block(string text, string key)
    {
        int start = text.IndexOf($"\n{key} = {{", StringComparison.Ordinal);
        if (start < 0 && text.StartsWith($"{key} = {{", StringComparison.Ordinal)) start = 0;
        else if (start < 0) return null;
        else start++;

        int open = text.IndexOf('{', start);
        if (open < 0) return null;

        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}' && --depth == 0) return text[start..(i + 1)];
        }

        return null;
    }
}
