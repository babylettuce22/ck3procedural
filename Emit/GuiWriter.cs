using System.Text.RegularExpressions;
using Ck3MapGen.Io;

namespace Ck3MapGen.Emit;

/// <summary>
/// Patches vanilla's county, character, title and main menu windows.
/// </summary>
public static class GuiWriter
{
    private sealed record Target(
        string File,
        string ScriptedGui,
        string Scope,
        (string Anchor, string What, string? Gui)[] Extend,
        (string Anchor, string What, string? Gui)[] Insert,
        (string Anchor, string What, string Block)[] Add);

    private static readonly Target[] Targets =
    [
        new Target(
        File: "window_county_view.gui",
        ScriptedGui: "wilderness_county",
        Scope: "HoldingView.GetProvince",
        Extend:
        [
            ("name = \"holder_info\"", "holder", null),
            ("name = \"set_realm_capital_button\"", "move-capital button", "wilderness_unfinished_county"),
            ("name = \"tutorial_highlight_holding_view_taxes_box\"", "holding taxes", null),
            ("name = \"tutorial_highlight_holding_view_loot_box\"", "holding loot", null),
        ],
        Insert:
        [
            ("name = \"county_stats\"", "stats", null),
            ("name = \"county_modifiers_grid\"", "modifiers", null),
            ("name = \"construct_holding\"", "build-holding prompt", "wilderness_unfinished_county"),
        ],
        Add:
        [
            ("name = \"county_info\"", "settle button", SettleButton),
        ]),

    new Target(
        File: "window_character.gui",
        ScriptedGui: "wilderness_holder",
        Scope: "CharacterWindow.GetCharacter",
        Extend: [],
        Insert:
        [
            ("name = \"main_content\"", "window body", null),
        ],
        Add:
        [
            ("using = Window_Size_Sidebar", "placeholder", CharacterPlaceholder),
        ]),

    new Target(
        File: "window_title.gui",
        ScriptedGui: "wilderness_title",
        Scope: "TitleViewWindow.GetTitle",
        Extend: [],
        Insert:
        [
            ("name = \"title_view_main_tab\"", "window body", null),
        ],
        Add:
        [
            ("using = Window_Background_Sidebar", "placeholder", TitlePlaceholder),
            ("using = Window_Background_Sidebar", "lore panel", TitleLorePanel),
            ("position = { 0 0 }", "lore reset", TitleLoreReset),
            ("button_sidepanel_right = {", "lore button", TitleLoreButton),
        ]),

    // No target for gui/shared/portraits.gui, deliberately.
    //
    // Inserting a `visible` after `pop_out = no` — the first line of `template portrait_base`, which
    // every portrait widget in the game uses — would hide the dummy's portrait everywhere in one
    // edit, and the anchor is still unique in 1.19 with `Character` already in datacontext. It is
    // not needed: the dummy renders as nothing at the MODEL level via the no_portrait morph in
    // BaseFilesToCopy/Wilderness/gfx/portraits/portrait_modifiers, which is vanilla's own mechanism
    // and costs no override of a 3,300-line vanilla file.
    //
    // Keep it in reserve rather than in the build. It is the answer if an empty portrait FRAME on
    // map hover is still too much, but it carries a risk this writer cannot check: a `visible` set
    // in a template is overridden by one set at the use site, so coverage would be partial in a way
    // that is invisible from here, and a mistake takes every portrait in the game with it.
];
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

    private static string TitlePlaceholder
        => Placeholder("WILDERNESS_TITLE_WINDOW",
            "[TitleViewWindow.Close]",
            "[TitleViewWindow.CloseHistory]",
            "[TitleViewWindow.CloseClaimants]");

    // --- The realm-lore panel -------------------------------------------------------------------
    //
    // Three pieces, all spliced into window_title.gui, because a fourth Target for the same file
    // would not work: Patch() reads vanilla and writes the mod once per Target, so a second entry
    // naming window_title.gui would overwrite the wilderness placeholder rather than join it.
    //
    // Nothing here is generated yet. The button opens a panel that says so. What it is FOR is a
    // per-title history blurb the generator writes as `gen_lore_<title key>` into its own
    // localisation file, read back with `Localize( Concatenate( 'gen_lore_', Title.GetKey ) )` --
    // that chain is valid, ck3-tiger accepts it, and it needs no scripted_gui, no variable and no
    // on_action, which is why it is the shape the rest of this is built around. When the lore
    // exists, the button gains
    //
    //     visible = "[Not( StringIsEmpty( Localize( Concatenate( 'gen_lore_', Title.GetKey ) ) ) )]"
    //
    // and the panel's text_multi swaps its placeholder key for the same expression. Until then the
    // button shows on every title, which is the point of this step.

    /// <summary>
    /// The button, spliced in above vanilla's "view_claimants" as a third entry in the vertical
    /// flowcontainer that already holds it and "title history".
    ///
    /// No `visible` of its own. It lives inside `title_view_main_tab`, which the wilderness Insert
    /// above stamps a `visible` onto, so unclaimed land hides it without this having to ask.
    ///
    /// GetVariableSystem rather than a scripted_gui: the panel is pure UI state with nothing to
    /// tell the game, and vanilla toggles its own expandables exactly this way (see
    /// tournament_progress_to_victory_widget.gui).
    /// </summary>
    private const string TitleLoreButton = """
        button_sidepanel_right = {
            name = "gen_title_lore_button"
            parentanchor = right

            onclick = "[GetVariableSystem.Toggle( 'gen_title_lore' )]"
            tooltip = "GEN_TITLE_LORE_TOOLTIP"

            blockoverride "button_text"
            {
                text = "GEN_TITLE_LORE"
                max_width = 110
            }
        }
        """;

    /// <summary>
    /// The panel the button opens.
    ///
    /// A widget at the window's root rather than a `window` of its own: a standalone window is only
    /// instantiated if the engine knows about it, and there is no way to register a new game view
    /// from script. A root-level child with `allow_outside` is the same picture — it is what
    /// vanilla's own pop-outs look like, and what the colony widget in BaseFilesToCopy already does.
    ///
    /// Root level costs it the `Title` datacontext, which is set further down on the main vbox, so
    /// it sets its own. x = 660 clears the 650-wide title window completely; the vanilla pop-outs
    /// sit at 630 and overlap by twenty pixels, which they can afford because they are separate
    /// windows on their own layer and this is a sibling drawn underneath.
    ///
    /// The wilderness half of the `visible` is not redundant with the button's placement: the
    /// variable outlives the window, so opening the panel on a real title and then clicking
    /// unclaimed land would otherwise leave it up over the placeholder.
    /// </summary>
    private const string TitleLorePanel = """
        widget = {
            name = "gen_title_lore_panel"
            datacontext = "[TitleViewWindow.GetTitle]"
            visible = "[And( GetVariableSystem.Exists( 'gen_title_lore' ), Not( {SHOW_RAW} ) )]"

            position = { 660 80 }
            size = { 420 60% }
            allow_outside = yes

            using = Window_Background
            using = Window_Decoration

            vbox = {
                using = Window_Margins
                spacing = 8

                hbox = {
                    layoutpolicy_horizontal = expanding

                    text_single = {
                        text = "[Title.GetNameNoTooltip]"
                        default_format = "#high"
                        using = Font_Size_Medium
                    }

                    expand = {}

                    button_close = {
                        onclick = "[GetVariableSystem.Clear( 'gen_title_lore' )]"
                    }
                }

                scrollbox = {
                    layoutpolicy_horizontal = expanding
                    layoutpolicy_vertical = expanding

                    blockoverride "scrollbox_content"
                    {
                        text_multi = {
                            layoutpolicy_horizontal = expanding
                            autoresize = yes
                            max_width = 370
                            text = "GEN_TITLE_LORE_PLACEHOLDER"
                        }
                    }
                }
            }
        }
        """;

    /// <summary>
    /// One line into vanilla's `_show` state, so the panel starts closed every time the title
    /// window opens.
    ///
    /// GetVariableSystem is global to the UI and survives both the panel and the window, so without
    /// this the panel would follow you from title to title once opened. Vanilla clears
    /// `display_allegiance` in the same block for the same reason; this just adds a third on_start
    /// beside the two already there.
    /// </summary>
    private const string TitleLoreReset =
        "on_start = \"[GetVariableSystem.Clear( 'gen_title_lore' )]\"";

    private const string SettleButton = """
        vbox = {
            name = "wilderness_buttons"
            layoutpolicy_horizontal = expanding
            margin = { 5 10 }
            spacing = 4

            visible = "[GetPlayer.IsValid]"

            button_standard = {
                name = "wilderness_settle_button"
                size = { 280 40 }
                text = "WILDERNESS_SETTLE_BUTTON"

                onclick = "[GetScriptedGui('wilderness_settle').Execute( GuiScope.SetRoot( GetPlayer.MakeScope ).AddScope( 'wilderness', HoldingView.GetProvince.MakeScope ).End )]"
                tooltip = "[GetScriptedGui('wilderness_settle').BuildTooltip( GuiScope.SetRoot( GetPlayer.MakeScope ).AddScope( 'wilderness', HoldingView.GetProvince.MakeScope ).End )]"
                enabled = "[And( GetPlayer.IsValid, GetScriptedGui('wilderness_settle').IsValid( GuiScope.SetRoot( GetPlayer.MakeScope ).AddScope( 'wilderness', HoldingView.GetProvince.MakeScope ).End ) )]"
                visible = "[And( GetPlayer.IsValid, And( {SHOW_RAW}, GetScriptedGui('wilderness_settle').IsShown( GuiScope.SetRoot( GetPlayer.MakeScope ).AddScope( 'wilderness', HoldingView.GetProvince.MakeScope ).End ) ) )]"
            }

            button_standard = {
                name = "wilderness_oversee_button"
                size = { 280 40 }
                text = "WILDERNESS_OVERSEE_BUTTON"

                visible = "[And( GetPlayer.IsValid, GetScriptedGui('wilderness_oversee').IsShown( GuiScope.SetRoot( GetPlayer.MakeScope ).AddScope( 'wilderness', HoldingView.GetProvince.MakeScope ).End ) )]"
                enabled = "[And( GetPlayer.IsValid, GetScriptedGui('wilderness_oversee').IsValid( GuiScope.SetRoot( GetPlayer.MakeScope ).AddScope( 'wilderness', HoldingView.GetProvince.MakeScope ).End ) )]"
                tooltip = "[GetScriptedGui('wilderness_oversee').BuildTooltip( GuiScope.SetRoot( GetPlayer.MakeScope ).AddScope( 'wilderness', HoldingView.GetProvince.MakeScope ).End )]"
                onclick = "[ToggleGameViewData( 'activity_list_detail_host_window', GetActivityType( 'activity_oversee_colony' ).Self )]"
            }

            button_standard = {
                name = "wilderness_return_home_button"
                size = { 280 40 }
                text = "WILDERNESS_RETURN_HOME_BUTTON"

                visible = "[And( GetPlayer.IsValid, GetScriptedGui('wilderness_return_home').IsShown( GuiScope.SetRoot( GetPlayer.MakeScope ).AddScope( 'wilderness', HoldingView.GetProvince.MakeScope ).End ) )]"
                enabled = "[And( GetPlayer.IsValid, GetScriptedGui('wilderness_return_home').IsValid( GuiScope.SetRoot( GetPlayer.MakeScope ).AddScope( 'wilderness', HoldingView.GetProvince.MakeScope ).End ) )]"
                tooltip = "[GetScriptedGui('wilderness_return_home').BuildTooltip( GuiScope.SetRoot( GetPlayer.MakeScope ).AddScope( 'wilderness', HoldingView.GetProvince.MakeScope ).End )]"
                onclick = "[GetScriptedGui('wilderness_return_home').Execute( GuiScope.SetRoot( GetPlayer.MakeScope ).AddScope( 'wilderness', HoldingView.GetProvince.MakeScope ).End )]"
            }

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
        foreach (var target in Targets) Patch(modDir, gameDir, target);
    }

    private static void Patch(string modDir, string gameDir, Target target)
    {
        string source = Path.Combine(gameDir, "gui", target.File);

        if (!File.Exists(source))
        {
            Console.WriteLine($"  gui: SKIPPED ({target.File} not found in game or mod folder)");
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
        string show = string.IsNullOrEmpty(target.ScriptedGui)
            ? "yes"
            : $"[GetScriptedGui('{target.ScriptedGui}').IsShown( GuiScope.SetRoot( {target.Scope}.MakeScope ).End )]";

        foreach (var (anchor, what, block) in target.Add)
        {
            int at = text.IndexOf(anchor, StringComparison.Ordinal);
            if (at < 0) continue;

            int lineStart = text.LastIndexOf('\n', at) + 1;
            string indent = text[lineStart..at];

            string body = string.Join('\n',
                block.Replace("{SHOW_RAW}", Inner(show))
                     .Replace("{SHOW}", show)
                     .Split('\n')
                     .Select(line => line.Length == 0 ? line : indent + line)) + "\n\n";

            text = text.Insert(lineStart, body);
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

        string dest = Path.Combine(modDir, "gui", target.File);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

        ParadoxText.WriteNoBom(dest, text);

        Console.WriteLine($"  gui: {target.File} — patched {string.Join(", ", patched)}");
    }

    private static int MatchBrace(string text, int open)
    {
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '#') { while (i < text.Length && text[i] != '\n') i++; continue; }
            if (c == '"') { i++; while (i < text.Length && text[i] != '"') i++; continue; }
            if (c == '{') depth++;
            else if (c == '}' && --depth == 0) return i;
        }
        return -1;
    }

    private static string Inner(string expression)
        => expression.StartsWith('[') && expression.EndsWith(']')
            ? expression[1..^1].Trim()
            : expression;
}