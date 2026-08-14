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
    /// <param name="Add">
    /// Whole widgets to splice in after an anchor, rather than conditions to hide one. The text is
    /// indented to match the anchor, and <c>{SHOW}</c> in it is replaced with the wilderness test
    /// (<c>{SHOW_RAW}</c> with the same thing unbracketed, for nesting inside an <c>And(...)</c>).
    /// </param>
    private sealed record Target(
        string File,
        string ScriptedGui,
        string Scope,
        (string Anchor, string What)[] Extend,
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
                ("name = \"holder_info\"", "holder"),
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

    // Properties rather than fields: `Targets` above is a static field initialised in textual
    // order, so a field declared down here would still be null when that array is built.
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

            # ---- Why the whole block is gated on GetPlayer.IsValid ----
            #
            # Because `GetPlayer` is not always a character. It is invalid in observer mode, across
            # the frames around a load, and while a widget tree is being built before a player is
            # attached — and PdxGui evaluates a widget's expressions whenever it updates it, not
            # only when somebody is looking at it.
            #
            # The three promote buttons below survive that: their promotes are engine-side calls on
            # the player object and the engine null-checks them itself. The settle button does not,
            # because it is the only one that hands a scope INTO script — `SetRoot( GetPlayer... )`
            # — and script has no null to check. An invalid root reached `wilderness_settle`'s
            # is_shown, which is a plain `can_colonize_at_all_trigger`, and every character trigger
            # in it failed with "Scoped object of type 'character' is not valid", once per
            # evaluation, filling the log with thousands of them.
            #
            # Guarding the container rather than each expression is deliberate: an invisible widget
            # is not updated, so nothing inside is evaluated at all, and that covers `tooltip` and
            # `enabled` — neither of which can be wrapped in a boolean guard of its own. It is also
            # exactly what vanilla does in hud.gui, which gates its own player-dependent blocks on
            # `GetPlayer.IsValid` for the same reason.
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
            # --- Going out to a colony yourself ------------------------------------------------
            #
            # Sits above the promote buttons because it belongs to the part of a colony's life
            # before promotion: a lord who is present is what makes an unfinished colony render
            # anything at all.
            #
            # ---- Why this one is unlike every other button here ----
            #
            # The other four drive something directly — a scripted_gui's Execute, or an interaction.
            # This one cannot, because CK3 has no effect that creates an activity: there is no
            # `create_activity` anywhere in the game files, and activities begin only through the
            # planner UI. So the onclick opens the planner instead, with
            # `ToggleGameViewData( 'activity_list_detail_host_window', ... )` — the same call
            # vanilla's own activity list uses to open a host window for a given type.
            #
            # That splits the button in two. Its conditions come from the `wilderness_oversee`
            # scripted_gui, which answers "does this button belong on THIS county, and can the
            # player act on it"; its action comes from the planner, which enforces the ownership
            # rule itself through the activity's `is_location_valid`. The rule is not written twice,
            # which is why the two cannot disagree.
            #
            # The practical cost: the player picks the colony again in the planner rather than the
            # button aiming at the county they are looking at. That is how every activity in the
            # game works, and `is_location_valid` means the list holds only their own colonies.
            #
            # There is no matching "come home" button, and no longer a decision either. The activity
            # ends when the colony is promoted — see promote_colony_effect — or through the activity
            # window, which is where a player already looks for the way out of an activity.
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
        foreach (var (anchor, what, gui) in target.Insert)
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

            // Most entries hide on the target's own question; one asks a different one. The
            // build-holding prompt has to stay hidden through the colony phase, while the panels
            // beside it come back the moment the county is claimed, so it names its own
            // scripted_gui rather than sharing this file's.
            string condition = gui is null
                ? hide
                : $"[Not( GetScriptedGui('{gui}').IsShown( GuiScope.SetRoot( "
                  + $"{target.Scope}.MakeScope ).End ) )]";

            text = text.Insert(lineEnd + 1, $"{indent}visible = \"{condition}\"\n");
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
