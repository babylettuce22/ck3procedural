using Ck3MapGen.GameGui;

namespace Ck3MapGen.Emit;

/// <summary>
/// Patches vanilla's county, character, title, council and bookmark windows.
///
/// Patching only. The windows this project writes from nothing live one per file in
/// <c>Emit/GuiWindows/</c> — see <see cref="ArtifactIndex"/> and <see cref="WonderIndex"/> — because
/// authoring grew past patching and keeps growing, while the set of vanilla files worth patching
/// does not.
///
/// ---- House rule: every GUI addition names its related base files ----
///
/// Each method below, and each file under <c>Emit/GuiWindows/</c>, carries a <c>Related base
/// files:</c> block listing the <c>BaseFilesToCopy</c> entries it depends on, and a <c>Related
/// generated files</c> block for anything another writer emits for it. Add one to anything new.
///
/// It is worth the lines because a <c>.gui</c> and the script behind it fail as a PAIR and fail
/// silently: a widget naming a scripted_gui that does not exist logs nothing and evaluates false, so
/// a rename on either side yields a window that never opens rather than an error. Nothing in the
/// build connects the two halves, and neither does ck3-tiger — the comment is the only link there
/// is.
///
/// The blocks also record what a change costs to test. Only the <c>.gui</c> half answers to the
/// console's <c>reload gui</c>; script and localisation are read at game start and need a restart.
/// And <c>--gui-only</c> runs just this writer plus <see cref="FrontendWriter"/>, so it rewrites the
/// <c>.gui</c> files alone — it will not re-copy a base file, and it will not emit a window whose
/// writer needs generator data at all.
///
/// Every edit here is a claim about a widget — "the container called holder_info", "the state that
/// fades the selected bookmark's name" — made against a parsed tree rather than against the file's
/// bytes. What that buys is not tidiness: an anchor that no longer resolves is reported *by name*
/// and the file refuses to ship, where a substring anchor that had drifted onto the wrong match
/// would have patched the wrong widget and said nothing at all. See <see cref="GuiDocument"/> for
/// the policy and <see cref="GuiBuilder"/> for how the spliced widgets are built.
///
/// Each method below opens one vanilla file, patches it, and ships it. A file may be opened once
/// and patched by any number of callers before shipping, which is worth stating because the writer
/// this replaces could not: it read vanilla and wrote the mod once per *target*, so two features
/// touching one file could not both be expressed. The second feature to want window_character.gui
/// had to be folded into the first by a special case, and it is that limit — not the feature — that
/// has gone.
/// </summary>
public static class GuiWriter
{
    public static void WriteAll(string modDir, string gameDir)
    {
        PatchCountyView(modDir, gameDir);
        PatchCharacterWindow(modDir, gameDir);
        PatchTitleWindow(modDir, gameDir);
        PatchCouncilWindow(modDir, gameDir);
        PatchBookmarkTab(modDir, gameDir);
        // The windows this project authors itself live in Emit/GuiWindows. Called from here so a
        // --gui-only run still emits them; the ones that need generator data are called from
        // ContentWriter instead, because this method has none.
        ArtifactIndex.Write(modDir);
    }

    // ===========================================================================================
    // The county window
    // ===========================================================================================

    /// <summary>
    /// Empties the county window for unclaimed land, and gives it the frontier actions.
    ///
    /// Two kinds of edit, and the difference between them is worth keeping visible. A widget that
    /// already carries a <c>visible</c> gets the wilderness condition folded into it with
    /// <c>And</c>, leaving vanilla in charge of every other reason it might hide. A widget with
    /// none gets one written for it, which makes this project responsible for that widget's whole
    /// visibility from then on. The first is cheap; the second is a commitment.
    ///
    /// <code>
    /// Related base files:
    ///   Wilderness/common/scripted_guis/00_wilderness_scripted_gui.txt      every gate below
    ///   Wilderness/common/character_interactions/00_colonization_interactions.txt  the promotions
    ///   Wilderness/common/activities/activity_types/00_oversee_colony_activity.txt oversee opens it
    ///   Wilderness/localization/english/wilderness_colonization_l_english.yml      the button text
    /// </code>
    /// </summary>
    private static void PatchCountyView(string modDir, string gameDir)
    {
        var doc = GuiDocument.Open(gameDir, "gui", "gui", "window_county_view.gui");
        if (doc is null) return;

        var scope = GuiScope.Root("HoldingView.GetProvince");
        var wilderness = new ScriptedGui("wilderness_county", scope);

        // A county that has been settled but has no holding yet. The move-capital button and the
        // build-holding prompt are wrong there for a different reason than in raw wilderness, so
        // they hang off their own question.
        var unfinished = new ScriptedGui("wilderness_unfinished_county", scope);

        doc.Widget("holder", "holder_info")
           .AndVisible(wilderness.IsHidden());
        doc.Widget("move-capital button", "set_realm_capital_button")
           .AndVisible(unfinished.IsHidden());
        doc.Widget("holding taxes", "tutorial_highlight_holding_view_taxes_box")
           .AndVisible(wilderness.IsHidden());
        doc.Widget("holding loot", "tutorial_highlight_holding_view_loot_box")
           .AndVisible(wilderness.IsHidden());

        doc.NameField("stats", "county_stats").InsertVisible(wilderness.IsHidden());
        doc.NameField("modifiers", "county_modifiers_grid").InsertVisible(wilderness.IsHidden());
        doc.NameField("build-holding prompt", "construct_holding").InsertVisible(unfinished.IsHidden());

        doc.NameField("settle button", "county_info").InsertBefore(FrontierActions(wilderness));

        doc.Ship(modDir);
    }

    /// <summary>
    /// The column of frontier actions: settle, oversee, go home, and the three promotions.
    ///
    /// Every button asks the same question it acts on, so a button is on screen exactly when
    /// pressing it would do something. That is worth stating because the failure is silent: a
    /// scripted_gui asked in a scope it does not expect evaluates false rather than erroring, so a
    /// button whose <c>visible</c> and <c>onclick</c> disagree simply goes quiet. Wiring all four
    /// properties from one object is what makes them unable to disagree.
    /// </summary>
    private static GuiNode FrontierActions(ScriptedGui wilderness)
    {
        // Not the same scope the window's own widgets use: the wilderness scripted_guis want the
        // player as root with the province beside it, which is the shape their effects expect.
        var scope = GuiScope.Root("GetPlayer").With("wilderness", "HoldingView.GetProvince");

        var player = GuiExpr.Raw("GetPlayer.IsValid");
        var settle = new ScriptedGui("wilderness_settle", scope);
        var oversee = new ScriptedGui("wilderness_oversee", scope);
        var returnHome = new ScriptedGui("wilderness_return_home", scope);

        return GuiBuilder.VBox("wilderness_buttons")
            .ExpandingH()
            .Margin(5, 10)
            .Spacing(4)
            .Gap().Visible(player)
            .Gap().Add(
                // The only one that also asks whether this is wilderness at all. The others sit
                // behind scripted_guis that already imply it.
                Action("wilderness_settle_button", "WILDERNESS_SETTLE_BUTTON")
                    .Gap().Visible(GuiExpr.And(player, wilderness.IsShown(), settle.IsShown()))
                    .Usable(settle, player)
                    .Tip(settle)
                    .Runs(settle),

                Action("wilderness_oversee_button", "WILDERNESS_OVERSEE_BUTTON")
                    .Gap().Shown(oversee, player)
                    .Usable(oversee, player)
                    .Tip(oversee)
                    // Opens the activity window rather than executing the scripted_gui it asks
                    // about: the scripted_gui is the gate, the activity is the thing.
                    .OnClick(GuiExpr.Raw(
                        "ToggleGameViewData( 'activity_list_detail_host_window', "
                        + "GetActivityType( 'activity_oversee_colony' ).Self )")),

                Action("wilderness_return_home_button", "WILDERNESS_RETURN_HOME_BUTTON")
                    .Gap().Bind(returnHome, player),

                Promotion("wilderness_promote_button",
                    "WILDERNESS_PROMOTE_BUTTON", "promote_colony_interaction"),
                Promotion("wilderness_promote_city_button",
                    "WILDERNESS_PROMOTE_CITY_BUTTON", "promote_colony_to_city_interaction"),
                Promotion("wilderness_promote_temple_button",
                    "WILDERNESS_PROMOTE_TEMPLE_BUTTON", "promote_colony_to_temple_interaction"));
    }

    private static GuiBuilder Action(string name, string text)
        => GuiBuilder.ButtonStandard(name).Gapped().Size(280, 40).Text(text);

    /// <summary>
    /// A promotion button, which goes through a player interaction rather than a scripted_gui.
    ///
    /// The county title has to be in the datacontext, because all four of the interaction functions
    /// aim at <c>Title.Self</c>. Without it every one of them quietly answers no and the button
    /// never appears — the same silent failure as a mismatched scope, from the other direction.
    /// </summary>
    private static GuiBuilder Promotion(string name, string text, string interaction)
        => Action(name, text)
            .DataContext("[HoldingView.GetCountyTitle]")
            .Gap().Bind(new TitleInteraction(interaction));

    // ===========================================================================================
    // The character window
    // ===========================================================================================

    /// <summary>
    /// Empties the character window for the wilderness dummy and puts a closable placeholder over it.
    ///
    /// <code>
    /// Related base files:
    ///   Wilderness/common/scripted_guis/00_wilderness_scripted_gui.txt   wilderness_holder
    ///   Wilderness/localization/english/wilderness_colonist_l_english.yml the placeholder's text
    ///   Wilderness/gfx/portraits/portrait_modifiers/                     the no_portrait morph
    /// </code>
    ///
    /// That last one is why there is no portrait edit here: the dummy renders as nothing at the
    /// MODEL level, which costs no override of vanilla's 3,300-line portraits.gui.
    /// </summary>
    private static void PatchCharacterWindow(string modDir, string gameDir)
    {
        var doc = GuiDocument.Open(gameDir, "gui", "gui", "window_character.gui");
        if (doc is null) return;

        var wilderness = new ScriptedGui("wilderness_holder",
            GuiScope.Root("CharacterWindow.GetCharacter"));

        doc.NameField("window body", "main_content").InsertVisible(wilderness.IsHidden());

        // The placeholder goes in at the window root, above vanilla's first `using`. Root level is
        // the shape the title lore panel and the colony widget also use: a standalone `window` is
        // only instantiated if the engine knows about it, and there is no way to register a new
        // game view from script.
        doc.Leaf("placeholder", "using", "Window_Size_Sidebar")
           .InsertBefore(Placeholder("WILDERNESS_HOLDER_WINDOW", wilderness,
               "[CharacterWindow.Close]"));

        doc.Ship(modDir);
    }

    /// <summary>
    /// What the wilderness dummy's window shows instead of a character: one line of text, and a way
    /// out.
    ///
    /// The close button takes every close call the window has, because a window closed halfway
    /// leaves its sub-panels floating over the map.
    /// </summary>
    private static GuiNode Placeholder(string text, ScriptedGui shown, params string[] onclick)
        => GuiBuilder.Widget("wilderness_placeholder")
            .Visible(shown.IsShown())
            .Size("100%", "100%")
            .Gap().Add(
                GuiBuilder.ButtonClose()
                    .ParentAnchor("top|right")
                    .Position(-18, 18)
                    .Size(30, 30)
                    .Shortcut("close_window")
                    .Add(onclick.Select(call => GuiNode.Leaf("onclick", GuiNode.Quote(call)))),

                GuiBuilder.TextMulti("wilderness_placeholder_text").Gapped()
                    .ParentAnchor("center")
                    .AutoResize()
                    .MaxWidth(320)
                    .Align("center")
                    .Text(text));

    // ===========================================================================================
    // The title window
    // ===========================================================================================

    /// <summary>
    /// The wilderness placeholder, and the realm-lore panel.
    ///
    /// The lore text comes from <see cref="ChronicleWriter"/>, which writes one
    /// <c>gen_lore_&lt;title key&gt;</c> per title into its own localisation file. Nothing else is
    /// in the path: no scripted_gui, no variable, no on_action. The button asks whether that key
    /// resolves to anything and hides itself when it does not, which is what gives baronies and
    /// wilderness no button rather than an empty panel, and what lets the whole feature vanish
    /// cleanly under <c>--no-history</c>.
    ///
    /// <code>
    /// Related base files:
    ///   Wilderness/common/scripted_guis/00_wilderness_scripted_gui.txt   wilderness_title
    ///
    /// Related generated files, written elsewhere:
    ///   Emit/ChronicleWriter.cs   the `gen_lore_&lt;title key&gt;` loc the panel reads
    /// </code>
    /// </summary>
    private static void PatchTitleWindow(string modDir, string gameDir)
    {
        var doc = GuiDocument.Open(gameDir, "gui", "gui", "window_title.gui");
        if (doc is null) return;

        var wilderness = new ScriptedGui("wilderness_title",
            GuiScope.Root("TitleViewWindow.GetTitle"));

        doc.NameField("window body", "title_view_main_tab").InsertVisible(wilderness.IsHidden());

        doc.Leaf("placeholder", "using", "Window_Background_Sidebar")
           .InsertBefore(
                Placeholder("WILDERNESS_TITLE_WINDOW", wilderness,
                    "[TitleViewWindow.Close]",
                    "[TitleViewWindow.CloseHistory]",
                    "[TitleViewWindow.CloseClaimants]"),
                TitleLorePanel(wilderness));

        // One line into vanilla's `_show` state, so the panel starts closed every time the window
        // opens. GetVariableSystem is global to the UI and outlives both the panel and the window,
        // so without this the panel would follow you from title to title once opened. Vanilla
        // clears `display_allegiance` in the same block for the same reason.
        doc.Inline("lore reset", "position", "0", "0")
           .InsertBefore(GuiNode.Leaf("on_start", GuiExpr.VariableClear("gen_title_lore").Quoted));

        doc.Block("lore button", "button_sidepanel_right").InsertBefore(TitleLoreButton());

        doc.Ship(modDir);
    }

    /// <summary>
    /// The button, spliced in above vanilla's "view_claimants" as a third entry in the vertical
    /// flowcontainer that already holds it and "title history".
    ///
    /// No wilderness check of its own. It lives inside <c>title_view_main_tab</c>, which the insert
    /// above stamps a <c>visible</c> onto, so unclaimed land hides it without this having to ask.
    ///
    /// GetVariableSystem rather than a scripted_gui: the panel is pure UI state with nothing to tell
    /// the game, and vanilla toggles its own expandables exactly this way — see
    /// tournament_progress_to_victory_widget.gui.
    /// </summary>
    private static GuiNode TitleLoreButton()
        => GuiBuilder.Of("button_sidepanel_right", "gen_title_lore_button")
            .ParentAnchor("right")
            .Gap().Visible(GuiExpr.Not(GuiExpr.StringIsEmpty(GuiExpr.Localize(LoreKey))))
            .OnClick(GuiExpr.VariableToggle("gen_title_lore"))
            .Tooltip("GEN_TITLE_LORE_TOOLTIP")
            .Gap().Add(GuiBuilder.BlockOverride("button_text")
                .Text("GEN_TITLE_LORE")
                .MaxWidth(110));

    /// <summary>The localisation key ChronicleWriter files this title's lore under.</summary>
    private static GuiExpr LoreKey
        => GuiExpr.Concatenate(GuiExpr.Literal("gen_lore_"), GuiExpr.Raw("Title.GetKey"));

    /// <summary>
    /// The panel the button opens.
    ///
    /// A widget at the window's root rather than a <c>window</c> of its own: a standalone window is
    /// only instantiated if the engine knows about it, and there is no way to register a new game
    /// view from script. A root-level child with <c>allow_outside</c> is the same picture — it is
    /// what vanilla's own pop-outs look like, and what the colony widget in BaseFilesToCopy does.
    ///
    /// Root level costs it the <c>Title</c> datacontext, which is set further down on the main vbox,
    /// so it sets its own. x = 660 clears the 650-wide title window completely; the vanilla pop-outs
    /// sit at 630 and overlap by twenty pixels, which they can afford because they are separate
    /// windows on their own layer and this is a sibling drawn underneath.
    ///
    /// The wilderness half of the <c>visible</c> is not redundant with the button's placement: the
    /// variable outlives the window, so opening the panel on a real title and then clicking
    /// unclaimed land would otherwise leave it up over the placeholder.
    /// </summary>
    private static GuiNode TitleLorePanel(ScriptedGui wilderness)
        => GuiBuilder.Widget("gen_title_lore_panel")
            .DataContext("[TitleViewWindow.GetTitle]")
            .Visible(GuiExpr.And(
                GuiExpr.VariableExists("gen_title_lore"),
                GuiExpr.Not(wilderness.IsShown())))
            .Gap().Position(660, 80)
            .Size("480", "60%")
            .AllowOutside()
            .Gap().Using("Window_Background", "Window_Decoration")
            .Gap().Add(GuiBuilder.VBox()
                .Comment("""
                    The width budget, because getting it wrong clips every line and the failure is
                    silent -- a max_width larger than the space available does not wrap early, it
                    overflows and the scrollbox crops it. Four things take a bite, in this order:

                        480  panel
                       - 36  this vbox's margin (Window_Margins would take 80, which is sized for a
                             full window and leaves a text panel this wide barely 300 usable)
                       - 35  Scrollbox_Margins, inside the scrollbox: 15 left, 20 right
                       - 13  the vertical scrollbar
                       = 396 usable, against a max_width of 370 below

                    Change any of those and the max_width has to move with it.
                    """)
                .Margin(18, 16)
                .Spacing(8)
                .Gap().Add(
                    GuiBuilder.HBox()
                        .ExpandingH()
                        .Gap()
                        .CommentNext("""
                            Matches Scrollbox_Margins' own left inset, which the body text below
                            picks up from inside the scrollbox and this row does not. Without it the
                            title hangs 15px to the left of the paragraphs it heads.
                            """)
                        .MarginLeft(15)
                        .Gap()
                        .CommentNext("""
                            Capped for the same reason as the body, minus the close button's own
                            width: generated realm names run long and this one is a single line, so
                            without it a bad name pushes the close button off the panel entirely.
                            """)
                        .Add(
                            GuiBuilder.TextSingle()
                                .Text(GuiExpr.Raw("Title.GetNameNoTooltip"))
                                .Format("#high")
                                .MaxWidth(370)
                                .Using("Font_Size_Medium"),

                            GuiBuilder.Expand(),

                            GuiBuilder.ButtonClose()
                                .OnClick(GuiExpr.VariableClear("gen_title_lore"))),

                    GuiBuilder.ScrollBox()
                        .ExpandingH()
                        .ExpandingV()
                        .Gap().Add(GuiBuilder.BlockOverride("scrollbox_content")
                            .Add(GuiBuilder.TextMulti()
                                .ExpandingH()
                                .AutoResize()
                                .MaxWidth(370)
                                .Text(GuiExpr.Localize(LoreKey))))));

    // ===========================================================================================
    // The council window
    // ===========================================================================================

    /// <summary>
    /// The colony's council: the one patch here that adds a mechanic rather than emptying a window
    /// for the wilderness dummy.
    ///
    /// It is in this writer because CK3 leaves no alternative. window_council.gui names every seat
    /// it draws, one <c>CouncilWindow.GetCouncillor('councillor_marshal')</c> at a time, with no
    /// datamodel over positions anywhere in it. A council position declared in script and not named
    /// here is invisible — the AI will still fill it, and nothing will report anything. AGOT ships
    /// the same shape for its Castellan and Admiral: new position files plus one window_council.gui
    /// override.
    ///
    /// ---- Three edits for five vanilla seats ----
    ///
    /// The layout already groups them: Chancellor and Steward share an hbox, Marshal and Spymaster
    /// share another, and both hboxes carry the <c>visible</c> that gets narrowed. Only the Court
    /// Chaplain sits loose in the top row and needs naming on its own.
    ///
    /// Its name appears twice in the file — the second is the celestial-ministry layout further
    /// down, which is gated behind HasAccessToMinistry anyway, and which no colonist has. The first
    /// match is the ordinary council. That was true before this was a tree and was a coincidence of
    /// search order; it is now a stated rule, and the second occurrence is the one without a
    /// <c>visible</c> of its own, so aiming at it would be caught rather than silently taken.
    ///
    /// Vanilla's SPOUSE seat is deliberately left alone. A colonist's wife advising him is not a
    /// court office, it is the same person he was already talking to, and it is the one vanilla seat
    /// whose premise survives on a frontier post.
    ///
    /// ---- What happens with --no-wilderness ----
    ///
    /// Nothing, and that is checked rather than hoped for. Without the Wilderness file set there is
    /// no <c>colony_council</c> scripted_gui, a .gui naming a scripted_gui that does not exist
    /// evaluates false, and false is the right answer in both directions here: the colony seats hide
    /// themselves and <c>Not(false)</c> leaves every vanilla row exactly as it was.
    ///
    /// <code>
    /// Related base files:
    ///   Wilderness/common/scripted_guis/00_wilderness_scripted_gui.txt      colony_council
    ///   Wilderness/common/council_positions/00_colony_council_positions.txt every seat named below
    ///   Wilderness/common/council_tasks/00_colony_council_tasks.txt         their default tasks
    ///   Wilderness/localization/english/wilderness_council_l_english.yml    position names
    /// </code>
    ///
    /// The position keys are a contract with that positions file, and getting it wrong is asymmetric:
    /// a position with no seat here is silently invisible, while a seat naming a position that does
    /// not exist draws an empty, nameless panel. A seat also needs its default task to exist, since
    /// the label comes from the active TASK rather than from the office.
    /// </summary>
    private static void PatchCouncilWindow(string modDir, string gameDir)
    {
        var doc = GuiDocument.Open(gameDir, "gui", "gui", "window_council.gui");
        if (doc is null) return;

        var colony = new ScriptedGui("colony_council", GuiScope.Root("CouncilWindow.GetCharacter"));

        doc.BlockWithComment("chancellor/steward row", "hbox = { # Chancellor + Steward")
           .AndVisible(colony.IsHidden());
        doc.BlockWithComment("marshal/spymaster row", "hbox = { # Marshal + Spymaster")
           .AndVisible(colony.IsHidden());
        doc.Widget("court chaplain seat", "tutorial_court_chaplain")
           .AndVisible(colony.IsHidden());

        // Two loose seats rather than a row of their own, because the anchor is itself a seat in
        // vanilla's top row. The spouse stays, the nomad spymaster and the chaplain beside them go
        // invisible for a colonist, and a box container gives invisible children no width — so the
        // row a colonist reads is Spouse, Warden, Quartermaster. That is the same mechanism
        // vanilla's own vizier/spouse swap relies on.
        doc.BlockWithComment("warden and quartermaster",
                "widget_councillor_item = { # Spymaster (If Nomadic it's moved up here)")
           .InsertBefore(
                CouncilSeat(colony, "councillor_colony_warden", "bg_council_marshal.dds"),
                CouncilSeat(colony, "councillor_colony_quartermaster", "bg_council_steward.dds"));

        // A whole new row above vanilla's hidden ones. Two rows of three is vanilla's own shape.
        doc.BlockWithComment("speaker/pathfinder/preacher row", "hbox = { # Chancellor + Steward")
           .InsertBefore(CouncilRow(colony));

        doc.Ship(modDir);
    }

    /// <summary>
    /// One council seat, in the shape vanilla gives its own — four datacontexts walking from the
    /// position to the councillor, then the illustration and the vignette over it.
    ///
    /// The datacontext chain is not decoration and the order is not free: the seat's label comes
    /// from <c>ActiveCouncilTask.GetPositionName</c>, so the widget has to reach the active TASK
    /// before it can name the OFFICE. That is also why every colony position carries a default task
    /// — a seat whose owner has no valid task for it renders as blank as a seat with no position.
    ///
    /// Every position key is a contract with
    /// BaseFilesToCopy/Wilderness/common/council_positions/00_colony_council_positions.txt, and the
    /// asymmetry of getting it wrong is worth knowing before editing either side: a position with no
    /// seat here is silently invisible, while a seat naming a position that does not exist draws an
    /// empty, nameless panel.
    ///
    /// Backgrounds are vanilla's council illustrations, matched by skill rather than by fiction:
    /// there is no frontier art to point at, and a stone chancellery behind the Speaker is a better
    /// wrong answer than an empty frame. The alpha is vanilla's own 0.6.
    /// </summary>
    private static GuiNode CouncilSeat(ScriptedGui colony, string position, string illustration)
        => GuiBuilder.Of("widget_councillor_item")
            .Comment(position)
            .Expanding()
            .DataContext($"[CouncilWindow.GetCouncillor('{position}')]")
            .DataContext("[GuiCouncilPosition.GetActiveCouncilTask]")
            .DataContext("[ActiveCouncilTask.GetPositionType]")
            .DataContext("[ActiveCouncilTask.GetCouncillor]")
            .Gap().Visible(colony.IsShown())
            .Gap().Add(
                GuiBuilder.Background()
                    .Texture($"gfx/interface/skinned/illustrations/council/{illustration}")
                    .FitType("centercrop")
                    .Alpha("0.6")
                    .Using("Mask_Rough_Edges"),

                GuiBuilder.Background().Gapped()
                    .Texture("gfx/interface/component_masks/mask_vignette.dds")
                    .Color("0.15", "0.15", "0.15", "1")
                    .Alpha("0.3"));

    /// <summary>
    /// Speaker, Pathfinder and Camp Preacher, as a row of their own.
    ///
    /// The <c>visible</c> sits on the hbox rather than on each of the three, so the row is one
    /// question asked once. Its margins are copied from the Marshal/Spymaster row it stands in place
    /// of, so a colony council occupies the same space on screen as a privy council does.
    /// </summary>
    private static GuiNode CouncilRow(ScriptedGui colony)
        => GuiBuilder.HBox()
            .Comment("Colony council — Speaker, Pathfinder, Camp Preacher")
            .Expanding()
            .Margin(10, 0)
            .MarginBottom(5)
            .Spacing(5)
            .Gap().Visible(colony.IsShown())
            .Gap().Add(
                CouncilSeat(colony, "councillor_colony_speaker", "bg_council_chancellor.dds"),
                CouncilSeat(colony, "councillor_colony_pathfinder", "bg_council_spymaster.dds"),
                CouncilSeat(colony, "councillor_colony_preacher", "bg_council_chaplain.dds"));

    // ===========================================================================================
    // The bookmark tab
    // ===========================================================================================

    /// <summary>
    /// Adds a line to the frontend's date tab, which vanilla builds as a bare year and nothing else.
    ///
    /// Nothing about vanilla's own widget is retyped: the year block stays exactly as written and
    /// the new line goes in after it, so a CK3 patch that restyles the year carries straight
    /// through.
    ///
    /// <code>
    /// Related base files: NONE.
    ///
    /// Related generated files, written elsewhere:
    ///   Emit/BookmarkWriter.cs   GroupSubtitleKey, and the loc behind it
    /// </code>
    ///
    /// The subtitle asks whether that key resolves to anything before drawing, so a run that wrote
    /// no bookmark wrote no key either and the line simply does not appear — rather than rendering
    /// the key name, which is what a <c>text</c> pointing at nothing does.
    /// </summary>
    private static void PatchBookmarkTab(string modDir, string gameDir)
    {
        var doc = GuiDocument.Open(gameDir, "gui", "gui", "frontend_bookmarks.gui");
        if (doc is null) return;

        // Anchored on what the widget SAYS, not on its name. `name = "year"` looks like the obvious
        // anchor and is the wrong one: the file has two, and the first belongs to the bookmark row
        // in the sidebar, which is a different widget showing the bookmark's own year.
        // `[BookmarkGroup.GetName]` is only ever the tab — asked for as unique rather than assumed
        // to be, so a vanilla change that introduces a second one skips the file instead of
        // patching whichever came first.
        var year = doc.Unique("date tab year",
            n => !n.IsBlock && n.Key == "text" && n.Value == "\"[BookmarkGroup.GetName]\"");

        // The year TEXT is the anchor; the widget holding it is what the new line goes after.
        // Reaching the container by walking up from the anchor is most of why this is a tree: the
        // writer this replaces searched backwards for the nearest `text_single = {` and then
        // brace-matched forward to find where it ended, with its own comment- and string-aware
        // scanner to do it.
        doc.At("date tab subtitle", year.Node?.Parent).InsertAfter(BookmarkTabSubtitle());

        ShowSelectedBookmarkName(doc);

        doc.Ship(modDir);
    }

    /// <summary>
    /// The second line under the year on the bookmark tab — see
    /// <see cref="BookmarkWriter.GroupSubtitleKey"/> for what fills it.
    ///
    /// A sibling of vanilla's "year" text rather than a replacement for it, so vanilla keeps
    /// authoring the year itself: the line this writer owns is only ever the one it adds.
    ///
    /// The <c>visible</c> is what makes the splice safe to ship unconditionally. A run that wrote no
    /// bookmark wrote no subtitle key either, and a <c>text</c> pointing at a key with nothing
    /// behind it renders the key — so the widget asks first, exactly as the title-lore button does.
    /// </summary>
    private static GuiNode BookmarkTabSubtitle()
        => GuiBuilder.TextSingle("gen_bookmark_group_subtitle")
            .Text(BookmarkWriter.GroupSubtitleKey)
            .Format("#weak;glow_color:{0,0,0,1}")
            .Using("Font_Size_Small", "Font_Type_Flavor")
            .MaxWidth(190)
            .Visible(GuiExpr.Not(GuiExpr.StringIsEmpty(
                GuiExpr.Localize(GuiExpr.Literal(BookmarkWriter.GroupSubtitleKey)))));

    /// <summary>
    /// Stops the bookmark's own tab going blank the moment it is selected.
    ///
    /// Vanilla fades the name off the selected row — with three or six bookmarks in a group that
    /// reads as the selected one stepping aside for the panel that now names it. This mod ships
    /// exactly one bookmark, so it is selected from the moment the screen opens and its name is
    /// never once drawn: the tab is the bare ornament and nothing else.
    ///
    /// The state is left in place and its alpha flipped, rather than the state being cut. It is
    /// paired with a <c>bookmark_tab_reset</c> state that fades the name back in, and a widget that
    /// can be animated to 1 but never to 0 is a widget whose two animations disagree.
    ///
    /// Best-effort, and deliberately not a shipping condition: a hidden bookmark name is a cosmetic
    /// loss, and refusing to write the file over it would cost the date-tab subtitle too.
    /// </summary>
    private static void ShowSelectedBookmarkName(GuiDocument doc)
    {
        // Identified by what the state DOES. The writer this replaces walked four IndexOf hops from
        // a texture path and then guarded the result with `alpha - at > 400` — a character distance
        // standing in for "is this still the same block".
        var fades = doc.Nodes()
            .Where(n => n.IsBlock
                && n.Key == "state"
                && n.Field("trigger_when") == "\"[GameSetup.IsBookmarkSelected( Bookmark.Self )]\""
                && n.Field("alpha") == "0")
            .ToList();

        if (fades.Count != 1)
        {
            Console.WriteLine("  gui: frontend_bookmarks.gui — left the selected bookmark's name "
                + $"hidden; vanilla no longer fades it where it used to ({fades.Count} candidates)");
            return;
        }

        fades[0].Set("alpha", "1");
    }
}
