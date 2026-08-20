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