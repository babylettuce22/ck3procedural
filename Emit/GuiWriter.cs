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
    /// <param name="Add">
    /// Whole widgets to splice in after an anchor, rather than conditions to hide one. The text is
    /// indented to match the anchor, and <c>{HIDE}</c> / <c>{SHOW}</c> in it are replaced with the
    /// wilderness test and its negation.
    /// </param>
    private sealed record Target(
        string File,
        string ScriptedGui,
        string Scope,
        (string Anchor, string What)[] Extend,
        (string Anchor, string What)[] Insert,
        (string Anchor, string What, string Block)[] Add);

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
                // Whole panels rather than the individual rows inside them. Nobody has surveyed
                // this country, so nothing should be reported about it — not its development, not
                // its control, and above all not its modifiers. A wilderness county's penalties are
                // meant to be discovered by marching into it, and a tooltip listing them in advance
                // turns a frontier into a spreadsheet.
                ("name = \"county_stats\"", "stats"),
                ("name = \"county_modifiers_grid\"", "modifiers"),
                ("name = \"holding_info\"", "holdings"),
            ],
            Add:
            [
                // Spliced in as the first child of county_info, so it sits where the holder's
                // portrait would be on a settled county — the one place in this window a player
                // already looks to find out whose land this is.
                ("name = \"county_info\"", "settle button", SettleButton),
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
            ],
            Add: []),
    ];

    /// <summary>
    /// The Settle button, spliced into the county view.
    ///
    /// <c>{SHOW}</c> is replaced with the wilderness test — the button exists only on unsettled
    /// land. Its enabled state and tooltip come from the <c>wilderness_settle</c> scripted_gui
    /// rather than from anything here, so the conditions the player reads are the same ones the
    /// effect checks; a GUI that decides for itself when to grey out is how a button ends up
    /// disagreeing with what happens when you press it.
    ///
    /// The province goes across as <c>wilderness</c> via AddScope, which is the scope name
    /// 00_wilderness_scripted_gui.txt reads.
    /// </summary>
    private const string SettleButton = """
        vbox = {
            name = "wilderness_buttons"
            layoutpolicy_horizontal = expanding
            margin = { 5 10 }
            spacing = 4

            # --- Claiming unsettled land -------------------------------------------------------
            button_standard = {
                name = "wilderness_settle_button"
                size = { 280 40 }
                text = "WILDERNESS_SETTLE_BUTTON"

                onclick = "[GetScriptedGui('wilderness_settle').Execute( GuiScope.SetRoot( GetPlayer.MakeScope ).AddScope( 'wilderness', HoldingView.GetProvince.MakeScope ).End )]"
                tooltip = "[GetScriptedGui('wilderness_settle').BuildTooltip( GuiScope.SetRoot( GetPlayer.MakeScope ).AddScope( 'wilderness', HoldingView.GetProvince.MakeScope ).End )]"
                enabled = "[GetScriptedGui('wilderness_settle').IsValid( GuiScope.SetRoot( GetPlayer.MakeScope ).AddScope( 'wilderness', HoldingView.GetProvince.MakeScope ).End )]"
                visible = "[And( {SHOW_RAW}, GetScriptedGui('wilderness_settle').IsShown( GuiScope.SetRoot( GetPlayer.MakeScope ).AddScope( 'wilderness', HoldingView.GetProvince.MakeScope ).End ) )]"
            }

            # --- Promoting a finished colony ---------------------------------------------------
            #
            # These drive the promote_colony_* interactions rather than scripted GUIs of their own.
            # The interactions already carry every condition — can_promote_colony_trigger, the
            # innovation requirements, the government rules — so asking them directly means the
            # button cannot drift from what pressing it does. It is also exactly how vanilla
            # surfaces feudalize_holding_interaction in window_title.gui.
            #
            # There is no separate "make it a tribe" button because there is no separate choice:
            # promote_colony_effect seats a tribal ruler on a tribal holding and everyone else on a
            # castle. The button says "raise a seat"; the realm decides what kind of seat that is.
            button_standard = {
                name = "wilderness_promote_button"
                size = { 280 40 }
                text = "WILDERNESS_PROMOTE_BUTTON"
                datacontext = "[HoldingView.GetCountyTitle]"

                visible = "[GetPlayer.IsPlayerInteractionShownAndCanPickTitle( 'promote_colony_interaction', Title.Self )]"
                enabled = "[GetPlayer.IsPlayerInteractionWithTargetTitleValid( 'promote_colony_interaction', Title.Self )]"
                tooltip = "[GetPlayer.GetPlayerInteractionWithTargetTitleTooltip( 'promote_colony_interaction', Title.Self )]"
                onclick = "[GetPlayer.OpenPlayerInteractionWithTargetTitle( 'promote_colony_interaction', Title.Self )]"
            }

            button_standard = {
                name = "wilderness_promote_city_button"
                size = { 280 40 }
                text = "WILDERNESS_PROMOTE_CITY_BUTTON"
                datacontext = "[HoldingView.GetCountyTitle]"

                visible = "[GetPlayer.IsPlayerInteractionShownAndCanPickTitle( 'promote_colony_to_city_interaction', Title.Self )]"
                enabled = "[GetPlayer.IsPlayerInteractionWithTargetTitleValid( 'promote_colony_to_city_interaction', Title.Self )]"
                tooltip = "[GetPlayer.GetPlayerInteractionWithTargetTitleTooltip( 'promote_colony_to_city_interaction', Title.Self )]"
                onclick = "[GetPlayer.OpenPlayerInteractionWithTargetTitle( 'promote_colony_to_city_interaction', Title.Self )]"
            }

            button_standard = {
                name = "wilderness_promote_temple_button"
                size = { 280 40 }
                text = "WILDERNESS_PROMOTE_TEMPLE_BUTTON"
                datacontext = "[HoldingView.GetCountyTitle]"

                visible = "[GetPlayer.IsPlayerInteractionShownAndCanPickTitle( 'promote_colony_to_temple_interaction', Title.Self )]"
                enabled = "[GetPlayer.IsPlayerInteractionWithTargetTitleValid( 'promote_colony_to_temple_interaction', Title.Self )]"
                tooltip = "[GetPlayer.GetPlayerInteractionWithTargetTitleTooltip( 'promote_colony_to_temple_interaction', Title.Self )]"
                onclick = "[GetPlayer.OpenPlayerInteractionWithTargetTitle( 'promote_colony_to_temple_interaction', Title.Self )]"
            }
        }
        """;

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

        // --- Whole widgets spliced in ----------------------------------------------------------
        string show = $"[GetScriptedGui('{target.ScriptedGui}').IsShown( GuiScope.SetRoot( "
                    + $"{target.Scope}.MakeScope ).End )]";

        foreach (var (anchor, what, block) in target.Add)
        {
            int at = text.IndexOf(anchor, StringComparison.Ordinal);
            if (at < 0) continue;

            int lineStart = text.LastIndexOf('\n', at) + 1;
            string indent = text[lineStart..at];

            int lineEnd = text.IndexOf('\n', at);
            if (lineEnd < 0) continue;

            // Re-indent the block to sit where the anchor sits. Written against a fixed left margin
            // in the constant above, so this shifts the whole thing rather than guessing per line.
            // {SHOW} is the whole bracketed expression; {SHOW_RAW} is its innards, for splicing
            // inside an And(...) that supplies its own brackets.
            string body = string.Join('\n',
                block.Replace("{SHOW_RAW}", Inner(show))
                     .Replace("{SHOW}", show)
                     .Split('\n')
                     .Select(line => line.Length == 0 ? line : indent + line));

            text = text.Insert(lineEnd + 1, body + "\n");
            patched.Add(what);
        }

        // Refuse rather than half-patch. All of them or none.
        int expected = target.Extend.Length + target.Insert.Length + target.Add.Length;
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
