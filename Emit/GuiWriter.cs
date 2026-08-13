using System.Text.RegularExpressions;
using Ck3MapGen.Io;

namespace Ck3MapGen.Emit;

/// <summary>
/// Patches vanilla's county view so an unsettled county stops claiming to have a ruler.
///
/// A wilderness county is held by a dummy character, and CK3's county window has no idea that is
/// meant to be a fiction: it draws the dummy's portrait, its name, its government, and the culture
/// and faith the generator had to invent to satisfy the engine. All four are true in the save and
/// all four are noise on screen.
///
/// **This never writes to the game directory.** It reads the installed
/// <c>gui/window_county_view.gui</c>, edits the text in memory, and writes the result into the mod,
/// where CK3 loads it in preference to vanilla's. Disabling the mod restores vanilla with nothing to
/// undo. Same read-transform-write shape as <see cref="MapTableWriter"/> and the three
/// <see cref="CompatibilityWriter"/> methods that re-declare vanilla data.
///
/// **Why generate the override rather than ship one.** AGOT solves this by shipping a hand-copied
/// window_county_view.gui — the whole ~3,700-line file, kept by hand, one line different from
/// vanilla's. That copy is frozen at whatever patch it was taken from, so every Paradox change to
/// the county view is silently reverted for anyone running the mod. Reading the player's own file
/// at generation time means the override is always against their exact patch. What it does NOT fix
/// is mod-vs-mod conflict: GUI files are first-loaded-wins, so another mod overriding this same file
/// still collides. That is a smaller surface than staleness, but it is not nothing.
///
/// **Failure is refusal.** If any anchor below is missing — a patch renamed a widget, another tool
/// already rewrote the file — this writes nothing at all and says so. A partially patched UI file is
/// worse than an unpatched one: PdxGui reports a malformed window by drawing nothing, so the county
/// view would simply vanish with no error naming this writer.
/// </summary>
public static class GuiWriter
{
    private const string SourceFile = "window_county_view.gui";

    /// <summary>
    /// The scripted_gui in BaseFilesToCopy/Wilderness that answers "is this county unsettled".
    /// Contract with common/scripted_guis/00_wilderness_scripted_gui.txt — the name must match.
    /// </summary>
    private const string ScriptedGui = "wilderness_county";

    /// <summary>
    /// The PdxGui expression for "this county is NOT wilderness".
    ///
    /// <c>HoldingView</c> is the county window's own data context and is reachable from every widget
    /// in the file, which is why the province is fetched through it rather than through whatever
    /// local datacontext the patched widget happens to sit under — several of them rebind to a
    /// character or a faith, where <c>Province</c> is not in scope.
    /// </summary>
    private const string NotWilderness =
        "[Not( GetScriptedGui('" + ScriptedGui + "').IsShown( GuiScope.SetRoot( "
        + "HoldingView.GetProvince.MakeScope ).End ) )]";

    /// <summary>
    /// The widgets to hide, each keyed by a line that occurs exactly once in vanilla's file.
    ///
    /// Anchored on text rather than line numbers because line numbers move on every patch and a
    /// wrong one edits an unrelated widget. Each of these is a <c>datacontext</c> naming the very
    /// thing being hidden, so a patch that renames one has almost certainly restructured the panel
    /// anyway — in which case refusing to patch is the right outcome.
    /// </summary>
    private static readonly (string Anchor, string What)[] InsertAfter =
    [
        ("datacontext = \"[County.GetCount.GetGovernment]\"", "government"),
        ("datacontext = \"[County.GetCulture]\"", "culture"),
        ("datacontext = \"[County.GetFaith]\"", "faith"),
    ];

    public static void WriteAll(string modDir, string gameDir, Config.MapConfig cfg)
    {
        if (!cfg.EnableWilderness)
        {
            Console.WriteLine("  county view: SKIPPED (wilderness disabled)");
            return;
        }

        string source = Path.Combine(gameDir, "gui", SourceFile);
        if (!File.Exists(source))
        {
            Console.WriteLine($"  county view: SKIPPED ({SourceFile} not found in the game folder)");
            return;
        }

        string text = File.ReadAllText(source);
        var patched = new List<string>();

        // --- The holder block, which already has a `visible` to extend ------------------------
        //
        // Handled separately because overwriting its condition would show the portrait on every
        // county that currently hides it. `Province.IsValid` is vanilla's own guard and has to
        // survive; ours is ANDed onto it.
        var holder = new Regex(
            "(name\\s*=\\s*\"holder_info\"[\\s\\S]{0,400}?visible\\s*=\\s*\")(\\[[^\"]*\\])(\")",
            RegexOptions.Compiled);

        var holderMatch = holder.Match(text);
        if (holderMatch.Success)
        {
            string existing = holderMatch.Groups[2].Value;
            string combined = $"[And( {Inner(existing)}, {Inner(NotWilderness)} )]";

            text = text.Remove(holderMatch.Groups[2].Index, holderMatch.Groups[2].Length)
                       .Insert(holderMatch.Groups[2].Index, combined);

            patched.Add("holder");
        }

        // --- The three panels with no `visible` of their own ----------------------------------
        foreach (var (anchor, what) in InsertAfter)
        {
            int at = text.IndexOf(anchor, StringComparison.Ordinal);
            if (at < 0) continue;

            // Reuse the anchor's own indentation so the emitted file still reads like the original
            // if anybody diffs it against vanilla — which, given this is a generated override of a
            // hand-written file, somebody eventually will.
            int lineStart = text.LastIndexOf('\n', at) + 1;
            string indent = text[lineStart..at];

            int lineEnd = text.IndexOf('\n', at);
            if (lineEnd < 0) continue;

            text = text.Insert(lineEnd + 1, $"{indent}visible = \"{NotWilderness}\"\n");
            patched.Add(what);
        }

        // Refuse rather than half-patch. Four targets or none.
        if (patched.Count != InsertAfter.Length + 1)
        {
            Console.WriteLine($"  county view: SKIPPED — found {patched.Count} of "
                + $"{InsertAfter.Length + 1} widgets ({string.Join(", ", patched)}). "
                + $"Vanilla's {SourceFile} has changed shape; not shipping a partial override.");
            return;
        }

        string dir = Path.Combine(modDir, "gui");
        Directory.CreateDirectory(dir);

        // No BOM. GUI files are not script files and vanilla's ship without one.
        ParadoxText.WriteNoBom(Path.Combine(dir, SourceFile), text);

        Console.WriteLine($"  county view: hid {string.Join(", ", patched)} on wilderness counties "
            + $"(patched a copy of vanilla's {SourceFile}; the game folder is untouched)");
    }

    /// <summary>Strips the outer brackets from a PdxGui expression so it can be nested inside one.</summary>
    private static string Inner(string expression)
        => expression.StartsWith('[') && expression.EndsWith(']')
            ? expression[1..^1].Trim()
            : expression;
}
