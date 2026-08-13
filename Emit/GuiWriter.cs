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
    /// <summary>
    /// One vanilla UI file to patch: which widgets to hide, and how to ask whether to hide them.
    /// </summary>
    /// <param name="File">Filename under the game's gui/ folder.</param>
    /// <param name="ScriptedGui">The scripted_gui in BaseFilesToCopy/Wilderness that answers the question.</param>
    /// <param name="Scope">The PdxGui path to the object that scripted_gui expects as its root.</param>
    /// <param name="Extend">Widgets that already have a `visible`; ours is ANDed onto theirs.</param>
    /// <param name="Insert">Widgets with no `visible` of their own; ours is inserted after the anchor.</param>
    private sealed record Target(
        string File,
        string ScriptedGui,
        string Scope,
        (string Anchor, string What)[] Extend,
        (string Anchor, string What)[] Insert);

    /// <summary>
    /// The two windows that show an unsettled county as though somebody lived there.
    ///
    /// Every anchor below was checked to occur EXACTLY once in the 1.19 files. That matters more
    /// than it looks: a string appearing twice would patch only the first, silently leaving half
    /// the panel visible. `datacontext = "[Character.GetFaith]"` is the worked example — it appears
    /// twice in the character window, so the unique `name = "faith_button"` on the line above is
    /// used instead.
    /// </summary>
    private static readonly Target[] Targets =
    [
        new Target(
            File: "window_county_view.gui",
            ScriptedGui: "wilderness_county",
            Scope: "HoldingView.GetProvince",
            Extend:
            [
                // Vanilla's own Province.IsValid guard has to survive, so this one is extended
                // rather than inserted — overwriting it would show the portrait on every county
                // that currently hides it.
                ("name = \"holder_info\"", "holder"),
            ],
            Insert:
            [
                ("datacontext = \"[County.GetCount.GetGovernment]\"", "government"),
                ("datacontext = \"[County.GetCulture]\"", "culture"),
                ("datacontext = \"[County.GetFaith]\"", "faith"),
            ]),

        new Target(
            File: "window_character.gui",
            ScriptedGui: "wilderness_holder",
            Scope: "CharacterWindow.GetCharacter",
            Extend: [],
            Insert:
            [
                // The portrait box. NOT main_content, which is the whole window body — the close
                // button lives inside it (blockoverride "button_close"), so hiding that would open
                // a window the player cannot shut.
                ("name = \"main_characters\"", "portrait"),
                ("name = \"faith_button\"", "faith"),
                ("datacontext = \"[Character.GetCulture]\"", "culture"),
                ("datacontext = \"[Character.GetHouse]\"", "house"),
            ]),
    ];

    public static void WriteAll(string modDir, string gameDir, Config.MapConfig cfg)
    {
        if (!cfg.EnableWilderness)
        {
            Console.WriteLine("  gui: SKIPPED (wilderness disabled)");
            return;
        }

        foreach (var target in Targets) Patch(modDir, gameDir, target);
    }

    private static void Patch(string modDir, string gameDir, Target target)
    {
        string source = Path.Combine(gameDir, "gui", target.File);
        if (!File.Exists(source))
        {
            Console.WriteLine($"  gui: SKIPPED ({target.File} not found in the game folder)");
            return;
        }

        string text = File.ReadAllText(source);
        string hide = $"[Not( GetScriptedGui('{target.ScriptedGui}').IsShown( GuiScope.SetRoot( "
                    + $"{target.Scope}.MakeScope ).End ) )]";

        var patched = new List<string>();

        // --- Widgets that already have a `visible` -------------------------------------------
        foreach (var (anchor, what) in target.Extend)
        {
            var match = Regex.Match(text,
                Regex.Escape(anchor) + @"[\s\S]{0,400}?visible\s*=\s*""(\[[^""]*\])""");

            if (!match.Success) continue;

            var group = match.Groups[1];
            string combined = $"[And( {Inner(group.Value)}, {Inner(hide)} )]";

            text = text.Remove(group.Index, group.Length).Insert(group.Index, combined);
            patched.Add(what);
        }

        // --- Widgets with none ----------------------------------------------------------------
        foreach (var (anchor, what) in target.Insert)
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

            text = text.Insert(lineEnd + 1, $"{indent}visible = \"{hide}\"\n");
            patched.Add(what);
        }

        // Refuse rather than half-patch. All of them or none.
        int expected = target.Extend.Length + target.Insert.Length;
        if (patched.Count != expected)
        {
            Console.WriteLine($"  gui: SKIPPED {target.File} — found {patched.Count} of {expected} "
                + $"widgets ({string.Join(", ", patched)}). Vanilla has changed shape; "
                + "not shipping a partial override.");
            return;
        }

        string dir = Path.Combine(modDir, "gui");
        Directory.CreateDirectory(dir);

        // No BOM. GUI files are not script files and vanilla's ship without one.
        ParadoxText.WriteNoBom(Path.Combine(dir, target.File), text);

        Console.WriteLine($"  gui: {target.File} — hid {string.Join(", ", patched)} "
            + "(patched a copy; the game folder is untouched)");
    }

    /// <summary>Strips the outer brackets from a PdxGui expression so it can be nested inside one.</summary>
    private static string Inner(string expression)
        => expression.StartsWith('[') && expression.EndsWith(']')
            ? expression[1..^1].Trim()
            : expression;
}
