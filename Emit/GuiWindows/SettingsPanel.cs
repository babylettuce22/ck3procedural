using Ck3MapGen.GameGui;

namespace Ck3MapGen.Emit;

/// <summary>
/// The settings window: the switches a player is allowed to throw on a running game.
///
/// The fifth authored window under <c>Emit/GuiWindows/</c>, and the first that exists to change how
/// the mod behaves rather than to report on it. Everything the others do is read-only.
///
/// <b>Why not a game rule.</b> <c>common/game_rules</c> is CK3's only native settings surface, and
/// it is the right home for anything that must be settled before the world exists — a rule is fixed
/// at the new-game screen and cannot move afterwards. These are the other kind: presentation
/// choices a player forms an opinion about only after looking at the game for an hour. A rule
/// cannot express "actually, no" on turn four hundred.
///
/// <b>Why not the debug panel.</b> That was the first plan and it does not work:
/// <see cref="DebugPanel"/>'s decision is <c>debug_only = yes</c>, so a settings tab inside it is a
/// settings menu only the person who built the generator can reach. This window's decision has no
/// such gate. The debug panel does get a button through to here, in its existing Windows group,
/// because that is where the other windows are listed.
///
/// <code>
/// Related base files:
///   Core/common/decisions/00_gen_settings_panel_decision.txt      the way in; sets the open flag
///   Core/common/scripted_guis/00_gen_settings_panel_guis.txt      the open state, and one entry per switch
///   Core/localization/english/gen_settings_panel_l_english.yml    every string this window shows
/// </code>
///
/// Static rather than generated, on the <see cref="ArtifactIndex"/> model: the window is a shape,
/// every key it names is fixed, and what it reads is global variables the running game owns. So
/// only the <c>.gui</c> and its registry entry are written here, the same file ships on every map,
/// and a map generated before this existed still works — an absent global variable reads as the
/// default, which is what every switch here is written to make true.
///
/// <b>The switches are global, and that is not a shortcut.</b> Each one is a global variable rather
/// than a variable on the player, because of where it gets read: a portrait weight block runs in
/// the scope of the character being DRAWN, not the character doing the looking. There is no
/// expression for "the person whose screen this is" available at that point, so a per-viewer
/// setting is not merely inconvenient here, it is unrepresentable. The consequence is real and
/// worth stating in the window: in multiplayer, one player's switch moves it for the table.
/// </summary>
public static class SettingsPanel
{
    // ===========================================================================================
    // Geometry
    // ===========================================================================================

    private const int WindowWidth = 620;

    /// <summary>How wide a switch's text column is: the window less its margins and the checkbox.</summary>
    private const int TextWidth = 500;

    /// <summary>
    /// A switch's row: the checkbox is 30, and the description under the label is budgeted at two
    /// wrapped lines. Generous on purpose — a description that runs to three lines pushes the note
    /// off the bottom of a window sized exactly, and clipping is one-sided and silent.
    /// </summary>
    private const int RowHeight = 80;

    private const int HeadingHeight = 34;

    /// <summary>
    /// Everything that is not a switch: window chrome, the blurb at the top and the note at the
    /// bottom, each of the latter budgeted at three wrapped lines.
    ///
    /// 150 is the MEASURED chrome figure, not the ~128 the margin arithmetic gives; the missing
    /// twenty pixels have cost this project a silently two-column grid once already. The rest is
    /// slack, in the direction the failure is not.
    /// </summary>
    private const int Furniture = 150 + 54 + 54 + 20;

    /// <summary>The height the switch list actually needs, so adding one cannot silently clip it.</summary>
    private static int WindowHeight()
        => Furniture + Groups.Sum(g => HeadingHeight + g.Switches.Length * RowHeight);

    // ===========================================================================================
    // The switches
    // ===========================================================================================

    /// <summary>
    /// One switch, as this window draws it and as the scripted_gui behind it is named.
    ///
    /// <paramref name="Key"/> is a contract with
    /// <c>Core/common/scripted_guis/00_gen_settings_panel_guis.txt</c> in both directions, and
    /// neither side fails loudly: a <c>.gui</c> naming a scripted_gui that does not exist logs
    /// nothing and evaluates false, which here produces a checkbox that is permanently unticked and
    /// does nothing when clicked. Rename one side and you get that, not an error.
    ///
    /// The entry's <c>is_shown</c> must mean <b>the feature is on</b>, not "the suppression flag is
    /// set". The checkbox binds <c>checked</c> straight to it, so an entry written the other way up
    /// produces a window that is correct about everything except which way the tick goes.
    /// </summary>
    private sealed record Switch(string Key, string Label, string Description);

    /// <summary>
    /// Every switch, in the order the window lists them, grouped under a heading.
    ///
    /// Adding one is this list plus a scripted_gui plus three localisation keys. Nothing else in
    /// this file knows how many there are.
    /// </summary>
    private static readonly (string Heading, Switch[] Switches)[] Groups =
    [
        ("GEN_SETTINGS_HEAD_PORTRAITS",
        [
            new Switch("gen_settings_portrait_weapons",
                "GEN_SETTINGS_PORTRAIT_WEAPONS",
                "GEN_SETTINGS_PORTRAIT_WEAPONS_DESC"),
            new Switch("gen_settings_armor_pieces",
                "GEN_SETTINGS_ARMOR_PIECES",
                "GEN_SETTINGS_ARMOR_PIECES_DESC"),
        ]),
    ];

    // ===========================================================================================
    // The window
    // ===========================================================================================

    public static void Write(string modDir)
    {
        var doc = GuiDocument.Create("settings panel", "gui", "gen_settings_panel.gui");

        // Root is the player, as in every window here: it asks whether *this* player opened it, so
        // two people in a multiplayer game open and close it independently. What the switches then
        // change is global — see the class remarks — but who is looking at the window is not.
        var player = GuiScope.Root("GetPlayer");
        var window = new ScriptedGui("gen_settings_panel_window", player);

        doc.Add(GuiBuilder.Types("gen_settings_panel").Add(

            GuiBuilder.Type("gen_settings_panel_host", "window")
                .Name("gen_settings_panel_host")
                .AllowOutside()
                .ParentAnchor("center")
                .Size(0, 0)
                // The host is always instantiated, so it carries the conditions under which no
                // custom window should be on screen at all.
                .Gap().Visible(GuiExpr.Raw(
                    "And( Not( IsPauseMenuShown ), And( Or( Not( IsObserver ), GetPlayer.IsValid ), "
                    + "IsDefaultGUIMode ) )"))
                .Gap().Add(GuiBuilder.Of("gen_settings_panel_window")),

            GuiBuilder.Type("gen_settings_panel_window", "window")
                .Gapped()
                .Name("gen_settings_panel_window")
                .AllowOutside()
                .Movable()
                .ParentAnchor("center")
                .Position(0, -40)
                // Both figures stated in pixels, and the height COMPUTED from the switch list
                // rather than left to autoresize. A window sized to its own content is the exact
                // shape that has silently mis-rendered here three times -- a size decided relative
                // to something that is itself still being decided. The switches are a fixed set
                // known at write time, so the arithmetic is available and the guess is not needed.
                .Size(WindowWidth, WindowHeight())
                .Using("Window_Background", "Window_Decoration_Spike")
                .Gap().Visible(window.IsShown())

                // No _show state. The index windows rebuild a list when they appear; this one has
                // nothing to rebuild, because every checkbox reads its own scripted_gui live.
                .Gap().Add(GuiBuilder.State("_show")
                    .Using("Animation_FadeIn_Quick", "Sound_WindowShow_Standard"))

                .Gap().Add(GuiBuilder.State("_hide")
                    .Using("Animation_FadeOut_Quick", "Sound_WindowHide_Standard"))

                .Gap().Add(Body(window, player))));

        // The bare instantiation the registry resolves. Without it the file parses, loads, reports
        // that loading is complete, and then the registry says
        //
        //     Could not find widget 'gen_settings_panel_host' in file 'gui/gen_settings_panel.gui'
        //
        // and nothing appears -- indistinguishable in the log from a visibility gate that is simply
        // false. It has now cost this project a day twice; see ArtifactIndex for the long version.
        doc.Add(GuiBuilder.Of("gen_settings_panel_host"));

        doc.Ship(modDir);

        // Written here rather than kept in BaseFilesToCopy because it names the file above BY PATH.
        // The two are one unit, and a registry pointing at a window that moved reports
        // "Could not find widget" and nothing else.
        string registry = Path.Combine(modDir, "gui", "scripted_widgets");
        Directory.CreateDirectory(registry);
        Io.ParadoxText.WriteNoBom(
            Path.Combine(registry, "gen_settings_panel.txt"),
            "# Instantiates the settings window. Written by Emit/GuiWindows/SettingsPanel.cs.\n"
            + "#\n"
            + "# Names the HOST type, not the window itself: the host is what exists from startup,\n"
            + "# and the window it contains is what appears when the decision sets the flag.\n"
            + "gui/gen_settings_panel.gui = gen_settings_panel_host\n");
    }

    /// <summary>Everything inside the window frame: header, blurb, and the switches.</summary>
    private static GuiBuilder Body(ScriptedGui window, GuiScope player)
    {
        var body = GuiBuilder.VBox()
            .Using("Window_Margins")
            .Spacing(4)

            .Gap().Add(GuiBuilder.Of("header_standard")
                .ExpandingH()
                .Gap().Add(GuiBuilder.BlockOverride("header_text")
                    .Text("GEN_SETTINGS_TITLE"))
                // The same scripted_gui the window's `visible` asks. One entry owns both
                // directions, so opening and closing cannot disagree about what open means.
                .Gap().Add(GuiBuilder.BlockOverride("button_close")
                    .DataContext(GuiExpr.Raw("GetScriptedGui( 'gen_settings_panel_window' )"))
                    .OnClick(GuiExpr.Raw($"ScriptedGui.Execute( {player} )"))))

            .Gap().Add(GuiBuilder.Of("text_multi")
                .ExpandingH()
                .AutoResize()
                .MaxWidth(TextWidth)
                .Text("GEN_SETTINGS_BLURB"));

        foreach (var (heading, switches) in Groups)
        {
            body.Gap().Add(Heading(heading));

            foreach (var setting in switches)
                body.Add(Row(setting, player));
        }

        // Said last and said plainly. A player who flips a switch and sees nothing change on the
        // portrait already in front of them will read that as a broken toggle rather than as a
        // portrait that has not been redrawn yet, and go looking for a bug that is not there.
        body.Gap().Add(GuiBuilder.Of("text_multi")
            .ExpandingH()
            .AutoResize()
            .MaxWidth(TextWidth)
            .Format("#weak")
            .Text("GEN_SETTINGS_NOTE"));

        return body;
    }

    /// <summary>
    /// One switch: a checkbox, and the two lines of prose telling you what it does.
    ///
    /// <c>checked</c> and <c>onclick</c> name the SAME scripted_gui, which is the whole shape of the
    /// thing — one entry answering "is it on" with its <c>is_shown</c> and "flip it" with its
    /// <c>effect</c>. Two entries could drift into disagreeing, and a checkbox that reports the
    /// opposite of what it does is worse than no checkbox.
    ///
    /// <c>button_checkbox</c> is vanilla's own (gui/shared/buttons.gui), 30x30 with the tick frame
    /// and background already on it. Nothing here restyles it.
    /// </summary>
    private static GuiBuilder Row(Switch setting, GuiScope player)
    {
        var gui = new ScriptedGui(setting.Key, player);

        return GuiBuilder.HBox()
            .ExpandingH()
            .Align("left")
            .Spacing(10)
            .MarginBottom(4)

            .Gap().Add(GuiBuilder.Of("button_checkbox")
                .Size(30, 30)
                .ParentAnchor("vcenter")
                .Quoted("checked", gui.IsShown().ToString())
                .OnClick(gui.Execute())
                .Tooltip(setting.Description))

            // Label and description are separate widgets rather than one string with a line break,
            // because `text` routes its whole contents through the localizer and a mixed literal
            // logs an unlocalized-text error per line per load.
            .Gap().Add(GuiBuilder.VBox()
                .ExpandingH()
                .Align("left")
                .Add(GuiBuilder.TextSingle()
                        .ExpandingH()
                        .Align("left")
                        .Format("#high")
                        .Text(setting.Label),
                     GuiBuilder.Of("text_multi")
                        .ExpandingH()
                        .AutoResize()
                        .MaxWidth(TextWidth - 40)
                        .Align("left")
                        .Format("#weak")
                        .Text(setting.Description)));
    }

    /// <summary>The same heading shape the debug panel uses, so the two windows read as a set.</summary>
    private static GuiBuilder Heading(string key)
        => GuiBuilder.VBox()
            .ExpandingH()
            .Align("left")
            .Spacing(2)
            .MarginBottom(4)
            .Add(GuiBuilder.TextSingle()
                    .ExpandingH()
                    .Align("left")
                    .Using("Font_Size_Medium")
                    .Format("#high")
                    .Text(key),
                 GuiBuilder.Of("divider_light")
                    .ExpandingH());
}
