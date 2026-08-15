using System.Text.RegularExpressions;
using Ck3MapGen.Io;

namespace Ck3MapGen.Emit;

/// <summary>
/// Patches vanilla's county, character and title windows so unsettled land stops claiming to have
/// a ruler.
///
/// A wilderness county is held by a dummy character, and CK3's UI has no idea that is meant to be a
/// fiction: it draws the dummy's portrait, its name, its government, and the culture and faith the
/// generator had to invent to satisfy the engine. All four are true in the save and all four are
/// noise on screen. Click through to the dummy itself, or to the title it holds, and the engine goes
/// further — a court, a council, a succession law — none of which anybody wrote.
///
/// It also hides two vanilla controls that would let a player act on a colony in ways the rest of
/// the system forbids: the build-holding prompt, and the move-capital button. Those are not lies to
/// be suppressed but actions to be prevented, and the county view is the only place they are
/// offered — the engine promotes behind them run no script, so there is nowhere else to say no.
///
/// **This never writes to the game directory.** It reads the installed <c>gui/*.gui</c> files,
/// edits the text in memory, and writes the result into the mod,
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
    /// The scripted_gui is asked at the file's own <see cref="Scope"/>, which is why the three
    /// entries below name three different questions rather than sharing one.
    /// </summary>
    /// <param name="File">Filename under the game's gui/ folder.</param>
    /// <param name="ScriptedGui">The scripted_gui in BaseFilesToCopy/Wilderness that answers the question.</param>
    /// <param name="Scope">The PdxGui path to the object that scripted_gui expects as its root.</param>
    /// <param name="Extend">Widgets that already have a `visible`; ours is ANDed onto theirs.</param>
    /// <param name="Insert">Widgets with no `visible` of their own; ours is inserted after the anchor.</param>
    /// <remarks>
    /// The <c>Gui</c> on an <see cref="Extend"/> or <see cref="Insert"/> entry names a different
    /// scripted_gui to ask instead of this target's own — null means ask <see cref="ScriptedGui"/>.
    /// Both lists need it because "should this widget be hidden" is not one question: the panels
    /// that lie about unheld land come back the moment a county is claimed, while the two widgets
    /// that would let a player build on or move into a colony stay hidden until it is promoted.
    /// </remarks>
    /// <param name="Add">
    /// Whole widgets to splice in after an anchor, rather than conditions to hide one. The text is
    /// indented to match the anchor, and <c>{SHOW}</c> in it is replaced with the wilderness test
    /// (<c>{SHOW_RAW}</c> with the same thing unbracketed, for nesting inside an <c>And(...)</c>).
    /// </param>
    private sealed record Target(
        string File,
        string ScriptedGui,
        string Scope,
        (string Anchor, string What, string? Gui)[] Extend,
        (string Anchor, string What, string? Gui)[] Insert,
        (string Anchor, string What, string Block)[] Add);

    /// <summary>
    /// The three windows that show an unsettled county as though somebody lived there.
    ///
    /// They are treated differently on purpose. The county view is EDITED — it keeps its shape and
    /// loses the panels that would be lies, because the player still needs it to look at wilderness
    /// and decide whether to settle it. The character and title windows are EMPTIED, because there
    /// is no version of "the dummy's council" or "the wilderness kingdom's succession law" worth
    /// showing; everything in them is bookkeeping wearing the costume of politics.
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
                ("name = \"holder_info\"", "holder", null),

                // The move-capital icon on the holding's title row. Hidden through the colony
                // phase for the same reason move_seat_to_colony_decision was deleted: a realm
                // capital that is a colony makes the whole realm read as colonial, castles and
                // all, because settlement_holding is the primary_holding of exactly one
                // government. See the note at the top of 00_colonization_decisions.txt.
                //
                // This has to be done in the GUI and only in the GUI. `SetRealmCapital` is an
                // engine promote on HoldingView, not a script effect, so no trigger of ours is
                // consulted before it fires and there is nothing to gate in script. What makes
                // that acceptable rather than a papered-over hole is that this file is the ONLY
                // place in vanilla's whole gui/ folder that reaches it — no decision, no
                // interaction, no other window — so hiding the button here closes the route
                // rather than one door onto it.
                //
                // Extended rather than inserted: vanilla's own PotentialSetRealmCapital already
                // decides whether the icon belongs on this holding at all, and replacing that
                // would put a move-capital button on every barony in the game.
                //
                // The county-capital button beside it is deliberately left alone. It moves a
                // county's seat between its own baronies, which cannot reach a colony: the colony
                // is placed on `title_province` — the county capital barony — so it is already
                // the seat, and the empty baronies around it have no holding to move it to.
                ("name = \"set_realm_capital_button\"", "move-capital button",
                    "wilderness_unfinished_county"),
            ],
            Insert:
            [
                // Whole panels rather than the individual rows inside them. Nobody has surveyed
                // this country, so nothing should be reported about it — not its development, not
                // its control, and above all not its modifiers. A wilderness county's penalties are
                // meant to be discovered by marching into it, and a tooltip listing them in advance
                // turns a frontier into a spreadsheet.
                ("name = \"county_stats\"", "stats", null),
                ("name = \"county_modifiers_grid\"", "modifiers", null),
                ("name = \"holding_info\"", "holdings", null),

                // Vanilla draws this prompt on any empty barony whether or not a holding can
                // actually be built, so gating construction in script empties the menu behind it
                // but leaves the button. Hidden on a WIDER condition than the rest of this window:
                // the panels above are stripped only on wilderness, while this stays hidden through
                // the colony phase too, until the county is promoted.
                ("name = \"construct_holding\"", "build-holding prompt", "wilderness_unfinished_county"),
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
                // The whole window body. This used to be four narrower anchors — the portrait box,
                // the faith button, the culture and house rows — which hid the four things that
                // most obviously lied about the dummy and left the rest of the character sheet
                // standing: an empty court, a lifestyle with no focus, a dynasty tree of one. Every
                // one of those is the same lie in a quieter font.
                //
                // Hiding main_content takes the close button with it (its blockoverride lives deep
                // inside the portrait widget), which is why the placeholder below carries its own.
                ("name = \"main_content\"", "window body", null),
            ],
            Add:
            [
                // Anchored on a property of the window itself rather than on anything inside
                // main_content, because it has to be main_content's SIBLING — spliced in as a child
                // of the container we just hid, it would inherit that container's `visible` and
                // never draw. `using = Window_Size_Sidebar` specifically, out of the several unique
                // lines up there, because it sits below the window's two `datacontext` lines and so
                // does not wedge a widget between a container and its own context.
                ("using = Window_Size_Sidebar", "placeholder", CharacterPlaceholder),
            ]),

        new Target(
            File: "window_title.gui",
            ScriptedGui: "wilderness_title",
            Scope: "TitleViewWindow.GetTitle",
            Extend: [],
            Insert:
            [
                // The outer vbox has exactly one child, so hiding it empties the window. Anchoring
                // on the child rather than the parent because the parent's opening line is a bare
                // `vbox = {` with nothing to match on.
                ("name = \"title_view_main_tab\"", "window body", null),
            ],
            Add:
            [
                // Same sibling problem as the character window, solved with a different anchor:
                // `using = Window_Background_Sidebar` is the only unique line that is a direct
                // property of the window here.
                ("using = Window_Background_Sidebar", "placeholder", TitlePlaceholder),
            ]),
        new Target(
            File: Path.Combine("shared", "portraits.gui"),
            ScriptedGui: "wilderness_holder",
            Scope: "Character",
            Extend: [],
            Insert:
            [
                ("pop_out = no", "global portrait base template", null)
            ],
            Add: []),
    ];

    /// <summary>
    /// What an emptied window shows instead: a line of text, and a way out.
    ///
    /// Deliberately almost nothing. The point of blanking these two windows is that the engine has
    /// no vocabulary for "nobody lives here" and fills the silence with invented facts, so replacing
    /// them with a smaller set of invented facts would miss the point. One sentence saying the land
    /// is unheld, and the frame around it, is the whole design until there is something real to add.
    ///
    /// The close button is not decoration. Both windows keep theirs inside the body being hidden —
    /// character: a blockoverride within the portrait widget; title: one within the header — so
    /// without this the player opens a window they cannot shut.
    /// </summary>
    /// <param name="text">Loc key for the one line of body text.</param>
    /// <param name="onclick">
    /// The close button's onclick lines, already written out. A list rather than a single
    /// expression because the title window's close is three calls, not one — see
    /// <see cref="TitlePlaceholder"/>.
    /// </param>
    private static string Placeholder(string text, params string[] onclick) => $$"""
        widget = {
            name = "wilderness_placeholder"
            visible = "{SHOW}"
            size = { 100% 100% }

            button_close = {
                parentanchor = top|right
                position = { -18 18 }
                size = { 30 30 }
                shortcut = "close_window"
        {{string.Join('\n', onclick.Select(c => $"        onclick = \"{c}\""))}}
            }

            text_multi = {
                name = "wilderness_placeholder_text"
                parentanchor = center
                autoresize = yes
                max_width = 320
                align = center
                text = "{{text}}"
            }
        }
        """;

    private static string CharacterPlaceholder
        => Placeholder("WILDERNESS_HOLDER_WINDOW", "[CharacterWindow.Close]");

    /// <summary>
    /// The title window closes three things, not one: vanilla's own close button clears the history
    /// and claimant sub-panels alongside the window, and leaving those open behind a closed window
    /// strands them on screen.
    /// </summary>
    private static string TitlePlaceholder
        => Placeholder("WILDERNESS_TITLE_WINDOW",
            "[TitleViewWindow.Close]",
            "[TitleViewWindow.CloseHistory]",
            "[TitleViewWindow.CloseClaimants]");

    /// <summary>
    /// The Settle, Oversee, Return Home, and Promote buttons spliced into the county view.
    /// </summary>
    private const string SettleButton = """
        vbox = {
            name = "wilderness_buttons"
            layoutpolicy_horizontal = expanding
            margin = { 5 10 }
            spacing = 4

            visible = "[GetPlayer.IsValid]"

            # --- Claiming unsettled land -------------------------------------------------------
            button_standard = {
                name = "wilderness_settle_button"
                size = { 280 40 }
                text = "WILDERNESS_SETTLE_BUTTON"

                onclick = "[GetScriptedGui('wilderness_settle').Execute( GuiScope.SetRoot( GetPlayer.MakeScope ).AddScope( 'wilderness', HoldingView.GetProvince.MakeScope ).End )]"
                tooltip = "[GetScriptedGui('wilderness_settle').BuildTooltip( GuiScope.SetRoot( GetPlayer.MakeScope ).AddScope( 'wilderness', HoldingView.GetProvince.MakeScope ).End )]"
                enabled = "[And( GetPlayer.IsValid, GetScriptedGui('wilderness_settle').IsValid( GuiScope.SetRoot( GetPlayer.MakeScope ).AddScope( 'wilderness', HoldingView.GetProvince.MakeScope ).End ) )]"
                visible = "[And( GetPlayer.IsValid, And( {SHOW_RAW}, GetScriptedGui('wilderness_settle').IsShown( GuiScope.SetRoot( GetPlayer.MakeScope ).AddScope( 'wilderness', HoldingView.GetProvince.MakeScope ).End ) ) )]"
            }

            # --- Going out to a colony yourself (Shown when NOT overseeing here) ----------------
            button_standard = {
                name = "wilderness_oversee_button"
                size = { 280 40 }
                text = "WILDERNESS_OVERSEE_BUTTON"

                visible = "[And( GetPlayer.IsValid, GetScriptedGui('wilderness_oversee').IsShown( GuiScope.SetRoot( GetPlayer.MakeScope ).AddScope( 'wilderness', HoldingView.GetProvince.MakeScope ).End ) )]"
                enabled = "[And( GetPlayer.IsValid, GetScriptedGui('wilderness_oversee').IsValid( GuiScope.SetRoot( GetPlayer.MakeScope ).AddScope( 'wilderness', HoldingView.GetProvince.MakeScope ).End ) )]"
                tooltip = "[GetScriptedGui('wilderness_oversee').BuildTooltip( GuiScope.SetRoot( GetPlayer.MakeScope ).AddScope( 'wilderness', HoldingView.GetProvince.MakeScope ).End )]"
                onclick = "[ToggleGameViewData( 'activity_list_detail_host_window', GetActivityType( 'activity_oversee_colony' ).Self )]"
            }

            # --- Returning home from a colony (Replaces Oversee when currently overseeing here) --
            button_standard = {
                name = "wilderness_return_home_button"
                size = { 280 40 }
                text = "WILDERNESS_RETURN_HOME_BUTTON"

                visible = "[And( GetPlayer.IsValid, GetScriptedGui('wilderness_return_home').IsShown( GuiScope.SetRoot( GetPlayer.MakeScope ).AddScope( 'wilderness', HoldingView.GetProvince.MakeScope ).End ) )]"
                enabled = "[And( GetPlayer.IsValid, GetScriptedGui('wilderness_return_home').IsValid( GuiScope.SetRoot( GetPlayer.MakeScope ).AddScope( 'wilderness', HoldingView.GetProvince.MakeScope ).End ) )]"
                tooltip = "[GetScriptedGui('wilderness_return_home').BuildTooltip( GuiScope.SetRoot( GetPlayer.MakeScope ).AddScope( 'wilderness', HoldingView.GetProvince.MakeScope ).End )]"
                onclick = "[GetScriptedGui('wilderness_return_home').Execute( GuiScope.SetRoot( GetPlayer.MakeScope ).AddScope( 'wilderness', HoldingView.GetProvince.MakeScope ).End )]"
            }

            # --- Promoting a finished colony ---------------------------------------------------
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

        string Hide(string? gui)
            => $"[Not( GetScriptedGui('{gui ?? target.ScriptedGui}').IsShown( GuiScope.SetRoot( "
               + $"{target.Scope}.MakeScope ).End ) )]";

        var patched = new List<string>();

        // --- Widgets that already have a `visible` -------------------------------------------
        foreach (var (anchor, what, gui) in target.Extend)
        {
            var match = Regex.Match(text,
                Regex.Escape(anchor) + @"[\s\S]{0,400}?visible\s*=\s*""(\[[^""]*\])""");

            if (!match.Success) continue;

            var group = match.Groups[1];
            string combined = $"[And( {Inner(group.Value)}, {Inner(Hide(gui))} )]";

            text = text.Remove(group.Index, group.Length).Insert(group.Index, combined);
            patched.Add(what);
        }

        // --- Widgets with none ----------------------------------------------------------------
        foreach (var (anchor, what, gui) in target.Insert)
        {
            int at = text.IndexOf(anchor, StringComparison.Ordinal);
            if (at < 0) continue;

            int lineStart = text.LastIndexOf('\n', at) + 1;
            string indent = text[lineStart..at];

            int lineEnd = text.IndexOf('\n', at);
            if (lineEnd < 0) continue;

            text = text.Insert(lineEnd + 1, $"{indent}visible = \"{Hide(gui)}\"\n");
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

            string body = string.Join('\n',
                block.Replace("{SHOW_RAW}", Inner(show))
                     .Replace("{SHOW}", show)
                     .Split('\n')
                     .Select(line => line.Length == 0 ? line : indent + line));

            text = text.Insert(lineEnd + 1, body + "\n");
            patched.Add(what);
        }

        int expected = target.Extend.Length + target.Insert.Length + target.Add.Length;
        if (patched.Count != expected)
        {
            Console.WriteLine($"  gui: SKIPPED {target.File} — found {patched.Count} of {expected} "
                + $"widgets ({string.Join(", ", patched)}). Vanilla has changed shape; "
                + "not shipping a partial override.");
            return;
        }

        // In GuiWriter.cs inside Patch():
        string dest = Path.Combine(modDir, "gui", target.File);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

        ParadoxText.WriteNoBom(dest, text);

        Console.WriteLine($"  gui: {target.File} — hid {string.Join(", ", patched)} "
            + "(patched a copy; the game folder is untouched)");
    }

    private static string Inner(string expression)
        => expression.StartsWith('[') && expression.EndsWith(']')
            ? expression[1..^1].Trim()
            : expression;
}