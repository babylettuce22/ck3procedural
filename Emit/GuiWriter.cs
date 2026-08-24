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

        // The colony's council.
        //
        // The one target here that ADDS a mechanic rather than emptying a window for the wilderness
        // dummy, and it is in this file because CK3 leaves no alternative: window_council.gui names
        // every seat it draws, one `CouncilWindow.GetCouncillor('councillor_marshal')` at a time,
        // with no datamodel over positions anywhere in it. A council position declared in script and
        // not named here is invisible — the AI will still fill it, and nothing will report anything.
        // AGOT ships the same shape for its Castellan and Admiral: new position files plus one
        // window_council.gui override.
        //
        // ---- The three Extends, and why they are rows rather than seats ----
        //
        // Vanilla's five council seats are hidden for a colonist by three edits, not five, because
        // the layout already groups them: Chancellor and Steward share an hbox, Marshal and Spymaster
        // share another, and both hboxes carry the `visible` that Extend rewrites. Only the Court
        // Chaplain sits loose in the top row and needs naming on its own.
        //
        // Its anchor, `name = "tutorial_court_chaplain"`, appears twice in the file — the second is
        // the celestial-ministry layout further down. Extend takes the first, which is the one in the
        // ordinary council, and the ministry layout is gated behind HasAccessToMinistry anyway, which
        // no colonist has.
        //
        // Vanilla's SPOUSE seat is deliberately left alone. A colonist's wife advising him is not a
        // court office, it is the same person he was already talking to, and it is the one vanilla
        // seat whose premise survives on a frontier post.
        //
        // ---- Why the two Adds land where they do ----
        //
        // Add splices its block in *before* the anchor line at the anchor's own indentation, so the
        // anchor picks the parent as much as the position. `widget_councillor_item = { # Spymaster
        // (If Nomadic...)` is a seat inside the top row, so the two seats added there become
        // siblings of the spouse — giving a colonist a top row of Spouse, Warden, Quartermaster once
        // the nomad and chaplain seats beside them go invisible. Invisible children take no space in
        // a PdxGui box, which is the same mechanism vanilla's own vizier/spouse swap relies on.
        //
        // `hbox = { # Chancellor + Steward` is a row inside the council vbox, so the block added
        // there is a whole new row of three. Two rows of three, which is vanilla's own shape.
        //
        // ---- What happens with --no-wilderness ----
        //
        // Nothing, and that is checked rather than hoped for. Without the Wilderness file set there
        // is no `colony_council` scripted_gui, a .gui naming a scripted_gui that does not exist
        // evaluates false, and false is the right answer in both directions here: the colony seats
        // hide themselves and `Not(false)` leaves every vanilla row exactly as it was.
        new Target(
        File: "window_council.gui",
        ScriptedGui: "colony_council",
        Scope: "CouncilWindow.GetCharacter",
        Extend:
        [
            ("hbox = { # Chancellor + Steward", "chancellor/steward row", null),
            ("hbox = { # Marshal + Spymaster", "marshal/spymaster row", null),
            ("name = \"tutorial_court_chaplain\"", "court chaplain seat", null),
        ],
        Insert: [],
        Add:
        [
            ("widget_councillor_item = { # Spymaster (If Nomadic it's moved up here)",
                "warden and quartermaster", ColonyCouncilTopRowSeats),
            ("hbox = { # Chancellor + Steward", "speaker/pathfinder/preacher row", ColonyCouncilRow),
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
    // The text comes from Emit/ChronicleWriter.cs, which writes one `gen_lore_<title key>` per
    // title into its own localisation file. Nothing else is in the path: no scripted_gui, no
    // variable, no on_action. The button asks whether that key resolves to anything and hides
    // itself when it does not, which is what gives baronies and wilderness no button rather than
    // an empty panel, and what lets the whole feature vanish cleanly under --no-history.

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

            visible = "[Not( StringIsEmpty( Localize( Concatenate( 'gen_lore_', Title.GetKey ) ) ) )]"
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
            size = { 480 60% }
            allow_outside = yes

            using = Window_Background
            using = Window_Decoration

            # The width budget, because getting it wrong clips every line and the failure is silent
            # -- max_width larger than the space available does not wrap early, it overflows and the
            # scrollbox crops it. Four things take a bite, in this order:
            #
            #     480  panel
            #    - 36  this vbox's margin (Window_Margins would take 80, which is sized for a full
            #          window and leaves a text panel this wide with barely 300 usable)
            #    - 35  Scrollbox_Margins, inside the scrollbox: 15 left, 20 right
            #    - 13  the vertical scrollbar
            #    = 396 usable, against a max_width of 370 below
            #
            # Change any of those and the max_width has to move with it.
            vbox = {
                margin = { 18 16 }
                spacing = 8

                hbox = {
                    layoutpolicy_horizontal = expanding

                    # Matches Scrollbox_Margins' own left inset, which the body text below picks up
                    # from inside the scrollbox and this row does not. Without it the title hangs
                    # 15px to the left of the paragraphs it heads.
                    margin_left = 15

                    # Capped for the same reason as the body, minus the close button's own width:
                    # generated realm names run long and this one is a single line, so without it a
                    # bad name pushes the close button off the panel entirely.
                    text_single = {
                        text = "[Title.GetNameNoTooltip]"
                        default_format = "#high"
                        max_width = 370
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
                            text = "[Localize( Concatenate( 'gen_lore_', Title.GetKey ) )]"
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

    // --- The colony council seats ---------------------------------------------------------------
    //
    // Every position key below is a contract with
    // BaseFilesToCopy/Wilderness/common/council_positions/00_colony_council_positions.txt, and the
    // asymmetry of getting it wrong is worth knowing before editing either side: a position with no
    // seat here is silently invisible, while a seat naming a position that does not exist draws an
    // empty, nameless panel. The second is what a colonist's council looked like before any of this
    // existed, when colony_government still said `council = no`.

    /// <summary>
    /// One council seat, in the shape vanilla gives its own — four datacontexts walking from the
    /// position to the councillor, then the illustration and the vignette over it.
    ///
    /// The datacontext chain is not decoration and the order is not free: the seat's label comes
    /// from <c>ActiveCouncilTask.GetPositionName</c>, so the widget has to reach the active TASK
    /// before it can name the OFFICE. That is also why every colony position carries a default task
    /// — a seat whose owner has no valid task for it renders as blank as a seat with no position.
    ///
    /// Backgrounds are vanilla's council illustrations, matched by skill rather than by fiction:
    /// there is no frontier art to point at, and a stone chancellery behind the Speaker is a better
    /// wrong answer than an empty frame. The alpha is vanilla's own 0.6.
    /// </summary>
    private static string ColonyCouncilSeat(string position, string illustration) => $$"""
        widget_councillor_item = { # {{position}}
            layoutpolicy_horizontal = expanding
            layoutpolicy_vertical = expanding
            datacontext = "[CouncilWindow.GetCouncillor('{{position}}')]"
            datacontext = "[GuiCouncilPosition.GetActiveCouncilTask]"
            datacontext = "[ActiveCouncilTask.GetPositionType]"
            datacontext = "[ActiveCouncilTask.GetCouncillor]"

            visible = "{SHOW}"

            background =  {
                texture = "gfx/interface/skinned/illustrations/council/{{illustration}}"
                fittype = centercrop
                alpha = 0.6
                using = Mask_Rough_Edges
            }

            background = {
                texture = "gfx/interface/component_masks/mask_vignette.dds"
                color = { 0.15 0.15 0.15 1 }
                alpha = 0.3
            }
        }
        """;

    /// <summary>
    /// Warden and Quartermaster, spliced into vanilla's top row as siblings of the spouse seat.
    ///
    /// Two loose widgets rather than a row of their own, because the anchor they are added at is
    /// itself a seat in that row. The spouse stays, the nomad spymaster and the chaplain beside them
    /// go invisible for a colonist, and a box container gives invisible children no width — so the
    /// row a colonist reads is Spouse, Warden, Quartermaster.
    /// </summary>
    private static string ColonyCouncilTopRowSeats
        => ColonyCouncilSeat("councillor_colony_warden", "bg_council_marshal.dds")
           + "\n\n"
           + ColonyCouncilSeat("councillor_colony_quartermaster", "bg_council_steward.dds");

    /// <summary>
    /// Speaker, Pathfinder and Camp Preacher, as a row of their own above vanilla's hidden ones.
    ///
    /// The <c>visible</c> sits on the hbox rather than on each of the three, so the row is one
    /// question asked once. Its margins are copied from the Marshal/Spymaster row it stands in place
    /// of, so a colony council occupies the same space on screen as a privy council does.
    /// </summary>
    private static string ColonyCouncilRow
    {
        get
        {
            string seats = string.Join("\n\n",
                ColonyCouncilSeat("councillor_colony_speaker", "bg_council_chancellor.dds"),
                ColonyCouncilSeat("councillor_colony_pathfinder", "bg_council_spymaster.dds"),
                ColonyCouncilSeat("councillor_colony_preacher", "bg_council_chaplain.dds"));

            string indented = string.Join('\n', seats
                .Split('\n')
                .Select(line => line.Length == 0 ? line : "    " + line));

            return $$"""
                hbox = { # Colony council — Speaker, Pathfinder, Camp Preacher
                    layoutpolicy_horizontal = expanding
                    layoutpolicy_vertical = expanding
                    margin = { 10 0 }
                    margin_bottom = 5
                    spacing = 5

                    visible = "{SHOW}"

                {{indented}}
                }
                """;
        }
    }

    /// <summary>
    /// The second line under the year on the bookmark tab — see
    /// <see cref="BookmarkWriter.GroupSubtitleKey"/> for what fills it.
    ///
    /// A sibling of vanilla's "year" text rather than a replacement for it, and spliced in below
    /// rather than written out here, so vanilla keeps authoring the year itself: the line this
    /// writer owns is only ever the one it adds.
    ///
    /// The <c>visible</c> is what makes the splice safe to ship unconditionally. A run that wrote
    /// no bookmark wrote no subtitle key either, and a `text` pointing at a key with nothing behind
    /// it renders the key — so the widget asks first, exactly as the title-lore button does.
    /// </summary>
    private static string BookmarkTabSubtitle => $$"""
        text_single = {
            name = "gen_bookmark_group_subtitle"
            text = "{{BookmarkWriter.GroupSubtitleKey}}"
            default_format = "#weak;glow_color:{0,0,0,1}"
            using = Font_Size_Small
            using = Font_Type_Flavor
            max_width = 190
            visible = "[Not( StringIsEmpty( Localize( '{{BookmarkWriter.GroupSubtitleKey}}' ) ) )]"
        }
        """;

    public static void WriteAll(string modDir, string gameDir, Config.MapConfig cfg)
    {
        foreach (var target in Targets) Patch(modDir, gameDir, target);
        PatchBookmarkTab(modDir, gameDir);
    }

    /// <summary>
    /// Adds a line to the frontend's date tab, which vanilla builds as a bare year and nothing else.
    ///
    /// Nothing about vanilla's own widget is retyped: the year block is lifted out of the file as
    /// written and put back with the new line after it, so a CK3 patch that restyles the year
    /// carries straight through. If either anchor is gone the file is not written at all — a mod
    /// with no <c>gui/frontend_bookmarks.gui</c> falls back on vanilla's, and the tab is a bare year
    /// again, which is the state this whole method is an improvement on rather than a dependency of.
    /// </summary>
    private static void PatchBookmarkTab(string modDir, string gameDir)
    {
        const string file = "frontend_bookmarks.gui";
        string source = Path.Combine(gameDir, "gui", file);

        if (!File.Exists(source))
        {
            Console.WriteLine($"  gui: SKIPPED ({file} not found in game folder)");
            return;
        }

        string text = File.ReadAllText(source);

        // Anchored on what the widget *says*, not on its name. `name = "year"` looks like the
        // obvious anchor and is the wrong one: the file has two, and the first belongs to the
        // bookmark row in the sidebar, which is a different widget showing the bookmark's own year.
        // `[BookmarkGroup.GetName]` is only ever the tab. Counted rather than assumed, so a vanilla
        // change that introduces a second one skips the file instead of patching the wrong widget.
        const string anchor = "text = \"[BookmarkGroup.GetName]\"";

        int at = text.IndexOf(anchor, StringComparison.Ordinal);
        int open = at < 0 ? -1 : text.LastIndexOf("text_single = {", at, StringComparison.Ordinal);
        int end = open < 0 ? -1 : MatchBrace(text, text.IndexOf('{', open));

        if (end < 0 || text.IndexOf(anchor, at + 1, StringComparison.Ordinal) >= 0)
        {
            Console.WriteLine($"  gui: SKIPPED {file} — the date tab's year text is not where "
                + "vanilla used to keep it. Not shipping a partial override.");
            return;
        }

        int lineStart = text.LastIndexOf('\n', open) + 1;
        string indent = text[lineStart..open];

        string block = string.Join('\n', BookmarkTabSubtitle
            .Split('\n')
            .Select(line => line.Length == 0 ? line : indent + line));

        text = text[..(end + 1)] + "\n\n" + block + text[(end + 1)..];

        text = ShowSelectedBookmarkName(text, file);

        string dest = Path.Combine(modDir, "gui", file);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        ParadoxText.WriteNoBom(dest, text);

        Console.WriteLine($"  gui: {file} — patched date tab subtitle");
    }

    /// <summary>
    /// Stops the bookmark's own tab going blank the moment it is selected.
    ///
    /// Vanilla fades the name off the selected row — with three or six bookmarks in a group that
    /// reads as the selected one stepping aside for the panel that now names it. This mod ships
    /// exactly one bookmark, so it is selected from the moment the screen opens and its name is
    /// never once drawn: the tab is the bare ornament and nothing else, which is what it looked
    /// like in game.
    ///
    /// The state is left in place and its alpha flipped, rather than the state being cut. It is
    /// paired with a `bookmark_tab_reset` state that fades the name back in, and a widget that can
    /// be animated to 1 but never to 0 is a widget whose two animations disagree.
    /// </summary>
    private static string ShowSelectedBookmarkName(string text, string file)
    {
        // Walked in one direction from a unique landmark, because none of these strings is unique
        // on its own: the shadowed widget wrapping the row's name, then the name itself, then the
        // selected-state trigger, then the one alpha that trigger sets.
        int at = text.IndexOf("gfx/interface/bookmarks/bm_shadow.dds", StringComparison.Ordinal);
        if (at >= 0) at = text.IndexOf("text = \"[Bookmark.GetName]\"", at, StringComparison.Ordinal);
        if (at >= 0) at = text.IndexOf("[GameSetup.IsBookmarkSelected( Bookmark.Self )]", at, StringComparison.Ordinal);

        int alpha = at < 0 ? -1 : text.IndexOf("alpha = 0", at, StringComparison.Ordinal);

        // The whole sequence lives inside one state block. A hit further off than that is some
        // other widget's alpha, and dimming the wrong thing is worse than leaving the name hidden.
        if (alpha < 0 || alpha - at > 400)
        {
            Console.WriteLine($"  gui: {file} — left the selected bookmark's name hidden; vanilla "
                + "no longer fades it where it used to");
            return text;
        }

        return text.Remove(alpha, "alpha = 0".Length).Insert(alpha, "alpha = 1");
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