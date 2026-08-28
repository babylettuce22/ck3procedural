namespace Ck3MapGen.Emit;

using Ck3MapGen.Io;
using System.IO;

/// <summary>
/// Adds a template to one of vanilla's accessory genes, by shipping a spliced copy of the file it
/// lives in.
///
/// **Why a copy and not a second declaration.** Declaring a gene again in a file of our own does not
/// merge — it REPLACES. Tried it on <c>clothes</c>: every vanilla template vanished
/// (<c>most_clothes not found in category clothes</c>, 46 times), which in game would have stripped
/// clothing from every character alive. AGOT suggests otherwise but does not contradict it, because
/// AGOT adds a whole new GENE in its own file and merging happens one level up at
/// <c>accessory_genes</c>, not among the templates inside a single gene.
///
/// **Why a template is needed at all.** A portrait modifier's <c>accessory</c> must be a member of
/// the <c>template</c> it names. The engine enforces this; ck3-tiger does not, so a wrong template
/// passes validation and then fails silently at render time with nothing but a line in
/// <c>error.log</c> to say so.
///
/// Reading the INSTALLED file at generation time means the copy tracks whatever patch is on the
/// machine rather than freezing a snapshot of an older one.
/// </summary>
public static class GeneSplice
{
    /// <summary>
    /// Copies <paramref name="fileName"/> out of the game's <c>common/genes</c> with
    /// <paramref name="block"/> inserted at the end of gene <paramref name="geneName"/>.
    ///
    /// The gene's closing brace is found by counting braces from its opening line rather than by
    /// matching indentation. Indentation differs between these files — <c>clothes</c> sits two tabs
    /// deep, <c>cloaks</c> one — and a patch is free to reformat either.
    /// </summary>
    /// <returns>False, with a reason printed, when the file or the gene is not where expected.</returns>
    /// <param name="relDir">
    /// Where the file lives under the game directory. Defaults to the gene folder, but the same
    /// copy-and-insert is exactly what an artifact visual needs: redeclaring one of those replaces
    /// vanilla's whole trigger-gated list, and <c>armor</c> alone carries 20 asset blocks that would
    /// go with it.
    /// </param>
    public static bool Write(
        string gameDir, string modDir, string fileName, string geneName,
        IEnumerable<string> block, string comment, string relDir = "common/genes",
        bool atStart = false)
    {
        string[] parts = relDir.Split('/');
        string mine = Path.Combine([modDir, .. parts, fileName]);

        // Splices ACCUMULATE. If we have already written a copy of this file, that copy is the
        // source for the next insertion — otherwise the second caller would re-read vanilla and
        // write over the first one's template, and only the last splice into a given file would
        // survive. Two do land in the clothes gene: the artifact armour template and the template
        // for hand-modelled pieces that replace a whole outfit.
        string source = File.Exists(mine) ? mine : Path.Combine([gameDir, .. parts, fileName]);

        if (!File.Exists(source))
        {
            Console.WriteLine($"  splice: {fileName} not found in the game directory - "
                + $"nothing can be added to {geneName}");
            return false;
        }

        var lines = new List<string>(File.ReadAllLines(source));
        int open = lines.FindIndex(l => l.Trim().StartsWith($"{geneName} = {{", StringComparison.Ordinal));

        if (open < 0)
        {
            Console.WriteLine($"  gene splice: no '{geneName}' gene in {fileName} - "
                + "its shape has changed");
            return false;
        }

        int depth = 0, close = -1;

        for (int i = open; i < lines.Count; i++)
        {
            foreach (char c in lines[i])
            {
                if (c == '{') depth++;
                else if (c == '}') depth--;
            }

            if (depth != 0) continue;

            close = i;
            break;
        }

        if (close < 0)
        {
            Console.WriteLine($"  gene splice: '{geneName}' in {fileName} is not closed - "
                + "refusing to guess where it ends");
            return false;
        }

        // Genes take the addition at the end; an artifact visual needs it at the START, because
        // vanilla's `armor` entry opens with an unconditional `icon = "artifact_armor.dds"` and a
        // trigger-gated icon placed after it would never be reached.
        lines.InsertRange(atStart ? open + 1 : close, [.. Comment(comment), .. block]);

        string dir = Path.Combine([modDir, .. parts]);
        Directory.CreateDirectory(dir);
        ParadoxText.WriteBom(Path.Combine(dir, fileName), string.Join('\n', lines) + "\n");

        return true;
    }

    private static IEnumerable<string> Comment(string text) =>
        ["", .. text.Split('\n').Select(l => "\t\t\t# " + l)];
}
