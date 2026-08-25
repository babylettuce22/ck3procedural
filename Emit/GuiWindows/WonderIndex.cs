using Ck3MapGen.GameGui;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// "Wonders of the World": the great works this map placed, where each one stands, and what it is.
///
/// The third authored window, and the first whose CONTENT is generated rather than gathered. The
/// artifact and realm indexes are fixed shapes filled at runtime by a scripted_gui walking the
/// world; this one is a different thing — the wonders are decided when the map is made, so the
/// window is written with one row already in it per wonder, and there is nothing to gather.
///
/// That is not a stylistic choice. There is no way to do it the other way: reaching a specific title
/// from a <c>.gui</c> needs a title-by-key datafunction, and <c>GetTitle('c_foo')</c> does not exist
/// — vanilla never writes it, in any of its 373 files. A datamodel could hold the wonder counties if
/// a scripted_gui gathered them, but then every row would share one template and no row could name
/// the wonder standing in it. Baking the rows is what the engine actually allows.
///
/// The cost is that a row's location text is true at the start of the game and not re-read after.
/// Counties do not move, so what ages is only the wording if a player renames one — which is a fair
/// price for a window that needs no runtime machinery at all.
///
/// Everything else follows <see cref="ArtifactIndex"/>: a zero-sized host for the
/// registry to instantiate, the real window inside it, a decision that sets a variable and a
/// scripted_gui that owns both directions of the open state. All five files are written here rather
/// than shipped in BaseFilesToCopy, because all five vary with the world.
///
/// <code>
/// Related base files: NONE. Everything this window needs is generated beside it:
///   common/scripted_guis/00_gen_wonder_index_gather.txt   province variables, on window open
///   common/scripted_guis/00_gen_wonder_index_guis.txt     the open state
///   common/decisions/00_gen_wonder_index_decision.txt     the way in; sets the open flag
///   localization/english/gen_wonder_index_l_english.yml   the window's own strings
///   gui/scripted_widgets/gen_wonder_index.txt             instantiates the host
///
/// Related generated files, written elsewhere:
///   Emit/WonderWriter.cs   the buildings themselves, and the `building_<key>` name and
///                          description loc the rows and the hover panel both read
/// </code>
///
/// Two consequences worth knowing before testing a change here. <c>--gui-only</c> does NOT emit this
/// window at all — it runs <see cref="GuiWriter"/>, and this writer is called from ContentWriter
/// because it needs the world's centres — so iterating on it means a full generation. And of the
/// files above only the <c>.gui</c> answers to <c>reload gui</c>; the script and the localisation
/// are read at game start, so changing them needs a restart.
/// </summary>
public static class WonderIndex
{
    /// <summary>The width of one wonder's panel, and the geometry that hangs off it.</summary>
    private const int RowWidth = 740;

    private const int IconSize = 84;

    /// <summary>Where the text column starts: past the icon and its margins.</summary>
    private const int TextLeft = 12 + IconSize + 14;

    /// <summary>
    /// Tall enough for two lines of description under the name and the location.
    ///
    /// 10 + 28 + 24 for the two single lines, then two 23px lines of wrapped text and a little
    /// slack. The first draft said 108, which left the description 38px to draw 46px of text in and
    /// cut every one of them off mid-sentence.
    /// </summary>
    private const int RowHeight = 118;

    /// <summary>Where the wrapped description starts, and how much room it gets.</summary>
    private const int DescriptionTop = 62;

    /// <summary>
    /// Wide enough for a row plus the window's own furniture.
    ///
    /// The chrome figure is measured rather than budgeted — see
    /// <see cref="GuiWriter"/>'s realm index for where the 150 comes from and why the arithmetic
    /// that says 128 is wrong.
    /// </summary>
    private const int WindowWidth = RowWidth + 150 + 20;

    /// <summary>
    /// Vanilla's own gold swatch, added over an icon's black art by <c>blend_mode = add</c>.
    ///
    /// A texture rather than a <c>color</c>, because the icons it colours carry no colour of their
    /// own to tint — see the comment on the icon itself.
    /// </summary>
    private const string GoldSwatch = "gfx/interface/colors/gold.dds";

    /// <summary>
    /// The variable on the player holding a wonder's province, filled by the gather this writer
    /// emits when the window opens.
    ///
    /// It exists because a BAKED row has no scope of its own. The rows are written when the map is
    /// generated, so nothing in them is a character or a title the way a datamodel entry would be,
    /// and <c>GetTitle('c_foo')</c> — the obvious way to fetch one by key — is not a datafunction
    /// that exists. A variable is the one bridge from a name known at generation time to a live
    /// scope at runtime: script can set it, and the <c>.gui</c> can read it back.
    /// </summary>
    private static string ProvinceVariable(GeneratedWonder wonder) => $"gen_{wonder.Key}_province";

    /// <summary>
    /// The wonder's province, read back off the player at runtime.
    ///
    /// <c>MakeScope.Var</c> rather than <c>GetGlobalVariable</c>. The latter type-checks, passes
    /// ck3-tiger and resolves to nothing in game; vanilla never writes it once, and reads variables
    /// off a scope this way 117 times instead.
    /// </summary>
    private static GuiExpr Province(GeneratedWonder wonder)
        => GuiExpr.Raw($"GetPlayer.MakeScope.Var('{ProvinceVariable(wonder)}').Province");

    /// <summary>How wide the hover panel's text is allowed to run.</summary>
    private const int TooltipWidth = 360;

    /// <summary>
    /// The hover panel on a wonder's icon: what it is, and — one hover further in — what it does.
    ///
    /// The name line is the important one, and it is <c>GetName</c> rather than
    /// <c>GetNameNoTooltip</c> on purpose. CK3 returns object names as text LINKS, and hovering one
    /// makes the engine draw that object's own full tooltip: for a building, the header, the
    /// description and every modifier it grants, sectioned by holding, county and holder. That is
    /// where the real information comes from, and none of it is reachable any other way — there is
    /// no <c>building_tooltip</c> widget to instantiate (vanilla declares object tooltips for
    /// characters, holdings, faiths, landed titles and dynasties, and nothing for buildings), and
    /// <c>GetEffectDesc</c> renders empty on the building this chain reaches, exactly as ck3-tiger
    /// warns that it will.
    ///
    /// So the panel is deliberately a doorway rather than the room. Reaching the modifiers costs a
    /// second hover, on the name inside it. Putting the link on the row's title instead, to save
    /// that hop, was tried and does not work — the title renders as plain text there.
    ///
    /// The container's four lines are vanilla's recipe for a tooltip carrying its own content, and
    /// all four are load-bearing. <c>preferred</c> especially: it means "be the size of your
    /// content", and without it the tooltip layer offers the whole screen and the panel takes it.
    /// </summary>
    private static GuiBuilder WonderTooltip()
        => GuiBuilder.Of("tooltipwidget")
            .Add(GuiBuilder.Of("container")
                .Using("DefaultTooltipBackground", "GeneralTooltipSetup")
                .LayoutPolicy("horizontal", "preferred")
                .Field("alwaystransparent", "no")

                .Gap().Add(GuiBuilder.VBox()
                    .LayoutPolicy("horizontal", "preferred")
                    .Margin(14, 10)
                    .Spacing(4)

                    // The building actually standing there, so the panel reports what the province
                    // has now rather than what the generator laid down at year zero.
                    .DataContext(GuiExpr.Raw("Province.GetHolding.GetSpecialBuildingType"))

                    .Gap().Add(GuiBuilder.TextSingle()
                        .Format("#high")
                        .Using("Font_Size_Medium")
                        .MaxWidth(TooltipWidth)
                        .Text(GuiExpr.Raw("Building.GetName")))

                    .Gap().Add(GuiBuilder.TextSingle()
                        .Format("#weak")
                        .MaxWidth(TooltipWidth)
                        .Text("GEN_WONDER_INDEX_GOTO"))));

    public static void Write(string modDir, WorldCenterMap worldCenters)
    {
        // No wonders, no window — and no decision, no scripted_gui and no registry entry either. A
        // decision that opens an empty window is worse than no decision, because the only way to
        // find out it is empty is to take it.
        if (worldCenters.Centers.Count == 0) return;

        var wonders = worldCenters.Centers.OrderBy(c => c.Wonder.Name, StringComparer.Ordinal).ToList();

        WriteWindow(modDir, wonders);
        WriteGather(modDir, wonders);
        WriteScriptedGui(modDir);
        WriteDecision(modDir);
        WriteLocalisation(modDir, wonders);
    }

    // -------------------------------------------------------------------------------------------
    // The window
    // -------------------------------------------------------------------------------------------

    private static void WriteWindow(string modDir, List<WorldCenter> wonders)
    {
        var doc = GuiDocument.Create("wonder index", "gui", "gen_wonder_index.gui");

        var player = GuiScope.Root("GetPlayer");
        var window = new ScriptedGui("gen_wonder_index_window", player);
        var gather = new ScriptedGui("gen_wonder_index_gather", player);

        // The rows, one per wonder, already filled in. This is the whole difference between this
        // window and the other two.
        var list = GuiBuilder.VBox()
            .ExpandingH()
            .Spacing(6);

        foreach (var center in wonders) list.Gap().Add(Row(center.Wonder));

        doc.Add(GuiBuilder.Types("gen_wonder_index").Add(

            GuiBuilder.Type("gen_wonder_index_host", "window")
                .Name("gen_wonder_index_host")
                .AllowOutside()
                .ParentAnchor("center")
                .Size(0, 0)
                .Gap().Visible(GuiExpr.Raw(
                    "And( Not( IsPauseMenuShown ), And( Or( Not( IsObserver ), GetPlayer.IsValid ), "
                    + "IsDefaultGUIMode ) )"))
                .Gap().Add(GuiBuilder.Of("gen_wonder_index_window")),

            GuiBuilder.Type("gen_wonder_index_window", "window")
                .Gapped()
                .Name("gen_wonder_index_window")
                .AllowOutside()
                .Movable()
                .ParentAnchor("center")
                .Position(0, -40)
                .Size(WindowWidth, 720)
                .Using("Window_Background", "Window_Decoration_Spike")
                .Gap().Visible(window.IsShown())

                // The gather does not fill the LIST — the rows were written when the map was. It
                // fills the province variables the rows click through to, which have to exist
                // before the first hover and cannot be left to game start: a save begun before this
                // feature existed would never have run that, and a blank tooltip looks exactly like
                // a broken datafunction.
                .Gap().Add(GuiBuilder.State("_show")
                    .Using("Animation_FadeIn_Quick", "Sound_WindowShow_Standard")
                    .Quoted("on_start", gather.Execute().ToString()))

                .Gap().Add(GuiBuilder.State("_hide")
                    .Using("Animation_FadeOut_Quick", "Sound_WindowHide_Standard"))

                .Gap().Add(GuiBuilder.VBox()
                    .Using("Window_Margins")

                    .Gap().Add(GuiBuilder.Of("header_standard")
                        .ExpandingH()
                        .Gap().Add(GuiBuilder.BlockOverride("header_text")
                            .Text("GEN_WONDER_INDEX_TITLE"))
                        .Gap().Add(GuiBuilder.BlockOverride("button_close")
                            .DataContext(GuiExpr.Raw("GetScriptedGui( 'gen_wonder_index_window' )"))
                            .OnClick(GuiExpr.Raw($"ScriptedGui.Execute( {player} )"))))

                    .Gap().Add(GuiBuilder.Of("text_multi")
                        .ExpandingH()
                        .MaxWidth(RowWidth)
                        .Text("GEN_WONDER_INDEX_BLURB"))

                    .Gap().Add(GuiBuilder.ScrollBox()
                        .Expanding()
                        .Gap().Add(GuiBuilder.BlockOverride("scrollbox_content").Add(list))))));

        doc.Add(GuiBuilder.Of("gen_wonder_index_host"));
        doc.Ship(modDir);

        string registry = Path.Combine(modDir, "gui", "scripted_widgets");
        Directory.CreateDirectory(registry);

        ParadoxText.WriteNoBom(
            Path.Combine(registry, "gen_wonder_index.txt"),
            "# Instantiates the wonder index. Written by Emit/GuiWindows/WonderIndex.cs.\n"
            + "#\n"
            + "# Names the HOST type, not the window itself, for the reason spelled out in\n"
            + "# gui/scripted_widgets/gen_artifact_index.txt.\n"
            + "gui/gen_wonder_index.gui = gen_wonder_index_host\n");
    }

    /// <summary>
    /// One wonder's panel: its icon, its name, where it stands, and what it is.
    ///
    /// A <c>widget</c> with everything placed by hand rather than a box that stacks — the same
    /// decision as the realm index card, for the same reason. A box inside a list does not reliably
    /// keep a size it was given, and a row that quietly takes a different height than the one it
    /// asked for takes every row below it with it.
    ///
    /// The name and description are the loc keys <see cref="WonderWriter"/> already writes for the
    /// building itself, rather than the same strings written again here. One source: rename a
    /// wonder and the index follows without anything having to know it exists.
    /// </summary>
    private static GuiBuilder Row(GeneratedWonder wonder)
    {
        // Tier 1's key, but any tier would do — all three rungs of a wonder share one name, because
        // a half-built Great Library is still that library.
        string building = $"building_{wonder.TierKey(1)}";

        return GuiBuilder.Widget()
            .Size(RowWidth, RowHeight)

            .Gap().Add(GuiBuilder.Background()
                .Texture("gfx/interface/component_masks/mask_brushed.dds")
                .Color("0.2", "0.2", "0.31", "0.45"))

            // A transparent clickable wrapper around the icon rather than a `button_icon` carrying
            // the texture itself: a button tints through its own block overrides, while an `icon`
            // takes a plain `color`, and the gold is the point. button_group is vanilla's own answer
            // for "make this thing clickable" — 33 uses — and brings the hover and click sounds with
            // it.
            .Gap().Add(GuiBuilder.Of("button_group")
                .Position(12, 12)
                .Size(IconSize, IconSize)
                // The county the wonder stands in, put in the datacontext so the two handlers below
                // read exactly as vanilla's own do.
                // The province the wonder stands in, for both the click and the tooltip below.
                .DataContext(Province(wonder))

                // Fly the camera there, rather than vanilla's DefaultOnCoatOfArmsClick.
                //
                // The shield handler was tried and reverted. It is the more "correct" answer in the
                // abstract — it is what clicking a coat of arms does everywhere else, and it brings
                // a right-click with it — but this is a list of places, not a list of titles, and
                // going straight to the map is the gesture it invites. Consistency with shields is
                // worth less here than doing the obvious thing.
                .OnClick(GuiExpr.Raw("Province.ZoomCameraTo"))
                .Add(WonderTooltip())

                // Gold ADDED to the icon. This is vanilla's own recipe, and the order matters.
                //
                // The building-type icons are pure black with an alpha mask — every opaque pixel of
                // one is exactly (0,0,0), measured. So `color` cannot tint them: a multiply against
                // black is black, and it fails silently. Painting a gold swatch and masking it to
                // the icon's silhouette fails differently and just as silently — it draws a solid
                // gold rectangle.
                //
                // What works is the icon as the BASE, with the colour added over it: black plus gold
                // is gold, and the base's alpha still cuts the shape. Vanilla does exactly this to
                // its activity icons, which are the same kind of black-on-alpha art.
                //
                // Sized in pixels, not `100%`. A button_group takes its size from its CONTENT, so an
                // icon asking for all of its parent asks a question whose answer depends on the
                // icon — and the engine settles that circle at zero, drawing nothing at all. Same
                // trap as the realm index card, one level further down: state sizes on the leaf and
                // let the container follow.
                .Add(GuiBuilder.Icon()
                    .Size(IconSize, IconSize)
                    // The PATH, not the filename. A building's `type_icon` takes the bare name and
                    // lets the engine resolve it; a .gui takes a path and draws nothing without one.
                    .Texture(wonder.IconTexture)
                    .ModifyTexture(GoldSwatch, "add")))

            // The building's LIVE name, not the static `building_<key>` loc key.
            //
            // Two things come with `GetName` rather than `GetNameNoTooltip`. The plain one: the row
            // reports what is actually standing there, so a wonder renamed in game or replaced by a
            // later tier titles itself correctly instead of showing what the generator wrote at year
            // zero. The better one: CK3 returns object names as text LINKS, so the title is live —
            // and where a link is live, hovering it makes the engine draw that object's own full
            // tooltip, modifiers and all.
            //
            // The datacontext is the building rather than the province, because `Building` is what
            // tiger calls what `GetSpecialBuildingType` yields, and the name hangs off that.
            .Gap().Add(GuiBuilder.TextSingle()
                .Position(TextLeft, 10)
                .MaxWidth(RowWidth - TextLeft - 12)
                .Elide("right")
                .Format("#high")
                .Using("Font_Size_Medium")
                .DataContext(GuiExpr.Raw($"{Province(wonder).Inner}.GetHolding.GetSpecialBuildingType"))
                .Text(GuiExpr.Raw("Building.GetName")))

            .Gap().Add(GuiBuilder.TextSingle()
                .Position(TextLeft, 38)
                .MaxWidth(RowWidth - TextLeft - 12)
                .Elide("right")
                .Format("#weak")
                .Text($"gen_wonder_index_where_{wonder.Key}"))

            // The one wrapping paragraph in any of the three windows. Two lines fit in the height
            // below it; a longer description elides rather than pushing the row out of shape.
            .Gap().Add(GuiBuilder.TextMulti()
                .Position(TextLeft, DescriptionTop)
                .Size(RowWidth - TextLeft - 12, RowHeight - DescriptionTop - 8)
                .MaxWidth(RowWidth - TextLeft - 12)
                .Text($"{building}_desc"));
    }

    // -------------------------------------------------------------------------------------------
    // The script side
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// The open state, and nothing else.
    ///
    /// Half the size of the other two indexes' scripted_gui files, because there is no gather. One
    /// entry answering both directions: the window's <c>visible</c> asks IsShown, the close button
    /// runs Execute.
    /// </summary>
    /// <summary>
    /// Puts each wonder's province into a global variable at game start, so the baked rows have
    /// something live to click through to.
    ///
    /// Once, on the first tick, and never again — a province does not move. The alternative would be
    /// a gather like the other two indexes run, which would be work repeated every time the window
    /// opened to produce the same answer.
    /// </summary>
    private static void WriteGather(string modDir, List<WorldCenter> wonders)
    {
        string dir = Path.Combine(modDir, "common", "scripted_guis");
        Directory.CreateDirectory(dir);

        string entries = string.Join("\n\n", wonders.Select(center =>
            $"\t\t# {center.Wonder.Name}\n"
            + "\t\tset_variable = {\n"
            + $"\t\t\tname = {ProvinceVariable(center.Wonder)}\n"
            + $"\t\t\tvalue = province:{center.Wonder.Barony.ProvinceId}\n"
            + "\t\t}"));

        string text = """
            # Wonder provinces, for the index window's click-to-zoom.
            # Written by Emit/GuiWindows/WonderIndex.cs.
            #
            # The window's rows are baked in when the map is generated and so carry no scope of
            # their own. These variables are the bridge from a key known at generation time to a
            # live scope at runtime, which is the only route there is: GetTitle('c_foo') is not a
            # datafunction that exists.
            #
            # ---- Why variables on the PLAYER, and why a gather ----
            #
            # Both halves of this were something else first, and both were wrong.
            #
            # They were GLOBAL variables read with GetGlobalVariable. That datafunction type-checks
            # and ck3-tiger accepts the whole chain, but it resolved to nothing in game and the
            # tooltip that should have named a province came back blank. Vanilla never writes
            # GetGlobalVariable in any of its 373 .gui files; it reads variables off a scope, with
            # MakeScope.Var, 117 times. So these live on the player and are read the way the game
            # reads its own.
            #
            # And they were set from on_game_start_after_lobby. That on_action does exist, but it
            # only fires when a game STARTS -- so a save begun before this feature existed would
            # never have the variables at all, and there would be no way to tell that from a broken
            # datafunction. Setting them when the window opens costs nothing, works on any save, and
            # is the same shape the artifact and realm indexes already use.

            gen_wonder_index_gather = {
            	scope = character

            	is_shown = {
            		always = yes
            	}

            	effect = {
            ENTRIES
            	}
            }

            """.Replace("ENTRIES", entries);

        ParadoxText.WriteBom(
            Path.Combine(dir, "00_gen_wonder_index_gather.txt"), text);
    }
    /// <summary>
    /// The open state, and nothing else.
    ///
    /// Half the size of the other two indexes' scripted_gui files, because there is no gather. One
    /// entry answering both directions: the window's <c>visible</c> asks IsShown, the close button
    /// runs Execute.
    /// </summary>
    private static void WriteScriptedGui(string modDir)
    {
        string dir = Path.Combine(modDir, "common", "scripted_guis");
        Directory.CreateDirectory(dir);

        ParadoxText.WriteBom(Path.Combine(dir, "00_gen_wonder_index_guis.txt"),
            """
            # The wonder index's open state. Written by Emit/GuiWindows/WonderIndex.cs.
            #
            # No gather entry, unlike the artifact and realm indexes: this window's rows were written
            # into gui/gen_wonder_index.gui when the map was generated, so there is nothing to read
            # out of the world when it opens.
            #
            # The open state is a character variable rather than a GUI VariableSystem flag because a
            # DECISION has to be able to set it, and a decision's effect cannot reach the GUI layer.

            gen_wonder_index_window = {
            	scope = character

            	is_shown = {
            		has_variable = gen_wonder_index_open
            	}

            	effect = {
            		remove_variable = gen_wonder_index_open
            	}
            }

            """);
    }

    private static void WriteDecision(string modDir)
    {
        string dir = Path.Combine(modDir, "common", "decisions");
        Directory.CreateDirectory(dir);

        ParadoxText.WriteBom(Path.Combine(dir, "00_gen_wonder_index_decision.txt"),
            """
            # The front door to the wonder index. Written by Emit/GuiWindows/WonderIndex.cs.
            #
            # Generated rather than shipped in BaseFilesToCopy, because the window it opens is
            # generated: a map with no world centers has no wonders, and gets neither file. The
            # existence of this file IS the emptiness guard, which is why is_shown is unconditional.

            gen_wonder_index_decision = {
            	picture = {
            		reference = "gfx/interface/illustrations/decisions/decision_misc.dds"
            	}

            	desc = gen_wonder_index_decision_desc
            	selection_tooltip = gen_wonder_index_decision_tooltip
            	confirm_text = gen_wonder_index_decision_confirm

            	# With the other two informational decisions, below the ones that change the game.
            	sort_order = 10

            	is_shown = {
            		always = yes
            	}

            	is_valid = {
            		always = yes
            	}

            	effect = {
            		set_variable = {
            			name = gen_wonder_index_open
            			value = yes
            		}
            	}

            	# Never. It costs nothing and does nothing; an AI taking it would spend its yearly
            	# decision slot opening a window nobody is looking at.
            	ai_will_do = {
            		base = 0
            	}

            	ai_check_interval = 0
            }

            """);
    }


    /// <summary>
    /// The window's own strings, and one location line per wonder.
    ///
    /// The location is a generated string and so goes through <see cref="LocFile"/>, which escapes
    /// it — a county name reaching a <c>.gui</c> as a raw quoted value would end the value early on
    /// an apostrophe and leave the rest of the line as something the engine reports against the
    /// file rather than the entry.
    /// </summary>
    private static void WriteLocalisation(string modDir, List<WorldCenter> wonders)
    {
        var loc = new LocFile();

        loc.Add("gen_wonder_index_decision", "Consider the Wonders of the World");
        loc.Add("gen_wonder_index_decision_desc",
            "There are works in this world that outlast the realms that raised them. Have your "
            + "clerks set down what is known of them.");
        loc.Add("gen_wonder_index_decision_tooltip",
            "Review every great work standing in the known world, and where it was raised.");
        loc.Add("gen_wonder_index_decision_confirm", "Send for the account");

        loc.Add("GEN_WONDER_INDEX_TITLE", "Wonders of the World");
        loc.Add("GEN_WONDER_INDEX_GOTO", "Show me where it stands");
        loc.Add("GEN_WONDER_INDEX_BLURB",
            "The great works of this world, raised where the land and the roads happened to favour it.");

        foreach (var center in wonders)
            loc.Add($"gen_wonder_index_where_{center.Wonder.Key}", $"In {center.County.Name}");

        loc.Write(Path.Combine(modDir, "localization", "english", "gen_wonder_index_l_english.yml"));
    }
}
