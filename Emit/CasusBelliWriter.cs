using System.Text;
using Ck3MapGen.Io;

namespace Ck3MapGen.Emit;

/// <summary>
/// Makes the wilderness un-warrable.
///
/// Nothing about the dummy holder stops a war being declared on it. It is landed, it holds counties,
/// and once a neighbour creates a duchy or kingdom title whose de jure area covers unsettled ground,
/// the de jure casus belli points straight at it — so realms conquer the wilderness instead of
/// settling it, which is the one thing the whole system exists to prevent. Conquest, claim and holy
/// war CBs all reach it too.
///
/// **There is no single lever for this.** CK3 has no scripted rule for war validity, no government
/// rule that forbids being attacked, and the 121 casus belli definitions share no common trigger:
/// each one decides for itself, in its own <c>allowed_against_character</c>. AGOT does not solve it
/// either — it enforces its uninteractable flag in activities, interactions, decisions and factions,
/// and never in <c>casus_belli_types</c>. Its wilderness is simply remote enough that few AI realms
/// ever border it.
///
/// So every CB is patched, which is only bearable because it is generated rather than hand-kept:
/// read the installed files, add one condition to each definition, write the copies into the mod.
/// Same read-transform-write shape as <see cref="GuiWriter"/>, and for the same reason — a hand-kept
/// copy of 26 vanilla files would be stale by the next patch.
///
/// Two cases, and getting them the wrong way round is the trap. 110 definitions already have an
/// <c>allowed_against_character</c> and need the condition ADDED INSIDE it; 11 have none — including
/// <c>claim_cb</c>, which is among the most used in the game — and need the whole block. Declaring a
/// second <c>allowed_against_character</c> beside an existing one does not merge with it, it
/// replaces it, which would silently delete the CB's real restrictions and leave the war *more*
/// available than vanilla.
/// </summary>
public static class CasusBelliWriter
{
    private const string Folder = "casus_belli_types";

    /// <summary>
    /// The condition itself. <c>scope:defender</c> rather than <c>root</c> because vanilla's own
    /// blocks qualify their scopes explicitly (<c>scope:attacker = { ... }</c>), which says root is
    /// not reliably either party.
    ///
    /// Asks the government flag rather than the holder's trait so that a second uninteractable
    /// government — ruins, an off-limits region — is covered by the same line without editing this.
    /// </summary>
    private const string Condition =
        "scope:defender = { NOT = { government_has_flag = government_is_wilderness } }";

    public static void WriteAll(string modDir, string gameDir, Config.MapConfig cfg)
    {
        if (!cfg.EnableWilderness)
        {
            Console.WriteLine("  casus belli: SKIPPED (wilderness disabled)");
            return;
        }

        string source = Path.Combine(gameDir, "common", Folder);
        if (!Directory.Exists(source))
        {
            Console.WriteLine($"  casus belli: SKIPPED (no common/{Folder} in the game folder)");
            return;
        }

        string target = Path.Combine(modDir, "common", Folder);
        Directory.CreateDirectory(target);

        int files = 0, extended = 0, added = 0;

        foreach (string path in Directory.GetFiles(source, "*.txt"))
        {
            string name = Path.GetFileName(path);
            if (name.StartsWith('_')) continue;

            string text = File.ReadAllText(path);
            string patched = Patch(text, ref extended, ref added);

            ParadoxText.WriteBom(Path.Combine(target, name), patched);
            files++;
        }

        Console.WriteLine($"  casus belli: {extended + added} war types closed against the wilderness "
            + $"across {files} files ({extended} gates extended, {added} added)");
    }

    /// <summary>
    /// Adds the condition to every top-level casus belli in one file.
    ///
    /// Works back to front so that every insertion offset computed against the original text stays
    /// valid — patching forwards would shift everything after the first edit.
    /// </summary>
    private static string Patch(string text, ref int extended, ref int added)
    {
        var edits = new List<(int At, string Text)>();

        foreach (var (_, bodyStart, bodyEnd) in TopLevelBlocks(text, 0, text.Length))
        {
            // Is there an allowed_against_character directly inside this CB? Nested ones inside
            // some other block are somebody else's and must not be touched.
            var gate = TopLevelBlocks(text, bodyStart, bodyEnd)
                .FirstOrDefault(b => b.Name == "allowed_against_character");

            if (gate.Name is not null)
            {
                edits.Add((gate.BodyStart, $"\n\t\t{Condition}\n"));
                extended++;
            }
            else
            {
                edits.Add((bodyStart,
                    $"\n\tallowed_against_character = {{\n\t\t{Condition}\n\t}}\n"));
                added++;
            }
        }

        var sb = new StringBuilder(text);
        foreach (var (at, insert) in edits.OrderByDescending(e => e.At)) sb.Insert(at, insert);
        return sb.ToString();
    }

    /// <summary>
    /// The <c>name = { ... }</c> blocks sitting directly inside the given span, with the body
    /// bounds of each.
    ///
    /// Comments and quoted strings are skipped rather than parsed, because both can contain braces —
    /// a `#` inside a loc string and a `{` inside a comment would each throw the depth count off,
    /// and a mis-parsed depth here means editing the wrong block.
    /// </summary>
    private static List<(string? Name, int BodyStart, int BodyEnd)> TopLevelBlocks(
        string text, int from, int to)
    {
        var blocks = new List<(string?, int, int)>();

        for (int i = from; i < to; i++)
        {
            char c = text[i];

            if (c == '#')
            {
                while (i < to && text[i] != '\n') i++;
                continue;
            }

            if (c == '"')
            {
                i++;
                while (i < to && text[i] != '"') i++;
                continue;
            }

            if (c != '{') continue;

            // Walk back over `=` and whitespace to the identifier naming this block.
            int j = i - 1;
            while (j >= from && char.IsWhiteSpace(text[j])) j--;

            string? name = null;
            if (j >= from && text[j] == '=')
            {
                j--;
                while (j >= from && char.IsWhiteSpace(text[j])) j--;

                int k = j;
                while (k >= from && (char.IsLetterOrDigit(text[k]) || text[k] == '_')) k--;
                if (k < j) name = text[(k + 1)..(j + 1)];
            }

            int close = MatchBrace(text, i, to);
            blocks.Add((name, i + 1, close));
            i = close;
        }

        return blocks;
    }

    /// <summary>Index of the `}` closing the `{` at <paramref name="open"/>.</summary>
    private static int MatchBrace(string text, int open, int to)
    {
        int depth = 0;

        for (int i = open; i < to; i++)
        {
            char c = text[i];

            if (c == '#') { while (i < to && text[i] != '\n') i++; continue; }
            if (c == '"') { i++; while (i < to && text[i] != '"') i++; continue; }

            if (c == '{') depth++;
            else if (c == '}' && --depth == 0) return i;
        }

        return to - 1;
    }
}
