using System.Text.RegularExpressions;
using Ck3MapGen.Io;

namespace Ck3MapGen.Emit;

/// <summary>
/// Gives every bookmark character a portrait entry, and the frontend at least one challenge
/// character.
///
/// Both gaps are ours. Blanking common/bookmark_portraits leaves each bookmark character with
/// "has no portrait in database", which the engine answers with a null portrait; blanking
/// common/bookmarks/challenge_characters produces "No Challenge Characters were read". CK3 then
/// crashes on a worker thread reading a field at +0x28 of a null object roughly two seconds
/// after history loading.
///
/// Portraits are cloned from a real vanilla file rather than synthesised: the files are
/// `dump_bookmark_portraits` console output, ~9 KB of gene values apiece, and every gene name
/// and weight has to be valid. Renaming the top-level key of a known-good male template makes
/// that true by construction.
/// </summary>
public static class PortraitWriter
{
    public static void WriteAll(string modDir, string gameDir, IEnumerable<string> characterNames)
    {
        string source = Path.Combine(gameDir, "common", "bookmark_portraits");
        string destination = Path.Combine(modDir, "common", "bookmark_portraits");
        Directory.CreateDirectory(destination);

        string? template = FindMaleTemplate(source);
        if (template is null)
        {
            Console.WriteLine("  portraits: no vanilla male template found, skipped");
            return;
        }

        string body = File.ReadAllText(template);
        int written = 0;

        // Only the FIRST `key={` is the portrait's identity — every gene inside the block has
        // the same shape, so replacing all matches renames ~117 gene entries and destroys the
        // file. The leading lines are comments, which cannot match, so the first hit is the
        // top-level key.
        var identity = new Regex(@"^[ \t]*[A-Za-z_0-9]+[ \t]*=[ \t]*\{", RegexOptions.Multiline);

        foreach (string name in characterNames)
        {
            string renamed = identity.Replace(body, $"{name}={{", 1);

            ParadoxText.WriteBom(Path.Combine(destination, $"{name}.txt"), renamed);
            written++;
        }

        Console.WriteLine($"  portraits: {written} cloned from {Path.GetFileName(template)}");
    }

    /// <summary>Picks a vanilla portrait whose type is male, matching our generated rulers.</summary>
    private static string? FindMaleTemplate(string source)
    {
        if (!Directory.Exists(source)) return null;

        foreach (string path in Directory.GetFiles(source, "*.txt").OrderBy(p => p))
        {
            string text = File.ReadAllText(path);
            if (text.Contains("type=male", StringComparison.Ordinal)) return path;
        }

        return null;
    }
}
