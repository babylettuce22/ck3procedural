using Ck3MapGen.GameGui;

namespace Ck3MapGen.Emit;

/// <summary>
/// The artifact index window: every famed and illustrious treasure in the world, and who holds it.
///
/// One of the authored windows under <c>Emit/GuiWindows/</c>. The rule for what lives here rather
/// than in <see cref="GuiWriter"/>: that writer patches vanilla's own windows, these write our own.
///
/// This one needs nothing from the generator — the window is a shape, and what fills it is read out
/// of the world at runtime — so its decision, scripted_guis and localisation are static files in
/// <c>BaseFilesToCopy/Core</c> and only the <c>.gui</c> is written here. Compare
/// <see cref="WonderIndex"/>, which varies per world and so has to write all five.
///
/// <code>
/// Related base files:
///   Core/common/decisions/00_gen_artifact_index_decision.txt      the way in; sets the open flag
///   Core/common/scripted_guis/00_gen_artifact_index_guis.txt      the open state, and the gather
///   Core/localization/english/gen_artifact_index_l_english.yml    every string this window shows
/// </code>
///
/// Those names are a contract in both directions and neither side fails loudly: a <c>.gui</c> naming
/// a scripted_gui that does not exist logs nothing and evaluates false, so a rename on either side
/// produces a window that simply never opens. Note also that <c>--gui-only</c> rewrites the
/// <c>.gui</c> alone — the three files above are copied by the static writer, so a change to any of
/// them needs a full run and a game restart rather than a <c>reload gui</c>.
/// </summary>
public static class ArtifactIndex
{
    /// <summary>
    /// A window listing every famed and illustrious artifact in the world, and who holds it.
    ///
    /// Authored rather than patched, and the first file here that is — <see cref="GuiDocument.Create"/>
    /// rather than <see cref="GuiDocument.Open"/>. It is the same builder and the same printer
    /// either way, which is why this lives beside the patches instead of in a writer of its own.
    ///
    /// Nothing about it varies with the world. The window is a shape; what fills it is decided at
    /// runtime by <c>gen_artifact_index_gather</c> in
    /// <c>BaseFilesToCopy/Core/common/scripted_guis</c>, which walks <c>every_artifact</c> when the
    /// window opens. So the same file ships on every map, works on a map generated before it
    /// existed, and lists artifacts forged during play alongside the ones this generator placed.
    ///
    /// Modelled on AGOT's artifact market, which solves the two problems this shape has:
    ///
    /// The registry needs something to instantiate, so there are two windows. The outer host is
    /// what <c>gui/scripted_widgets</c> names and is always present at zero size; the inner one
    /// carries the real geometry and the visibility gate. A window with no parent is never drawn
    /// otherwise, because nothing in script can create one.
    ///
    /// And the list is refreshed by the window's own <c>_show</c> state rather than by the decision
    /// that opens it. The decision only sets a flag — so the list cannot go stale between opening
    /// the decisions panel and looking at the window, and reopening it is a re-read rather than a
    /// second copy.
    /// </summary>
    public static void Write(string modDir)
    {
        var doc = GuiDocument.Create("artifact index", "gui", "gen_artifact_index.gui");

        // Root is the player for both: the window asks whether *this* player opened it, so two
        // people in a multiplayer game can have it open independently.
        var player = GuiScope.Root("GetPlayer");
        var window = new ScriptedGui("gen_artifact_index_window", player);
        var gather = new ScriptedGui("gen_artifact_index_gather", player);

        var entries = GuiExpr.Raw("GetGlobalList( 'gen_artifact_index_list' )");

        doc.Add(GuiBuilder.Types("gen_artifact_index").Add(

            GuiBuilder.Type("gen_artifact_index_host", "window")
                .Name("gen_artifact_index_host")
                .AllowOutside()
                .ParentAnchor("center")
                .Size(0, 0)
                // The host is always instantiated, so it carries the conditions under which no
                // custom window should be on screen at all.
                .Gap().Visible(GuiExpr.Raw(
                    "And( Not( IsPauseMenuShown ), And( Or( Not( IsObserver ), GetPlayer.IsValid ), "
                    + "IsDefaultGUIMode ) )"))
                .Gap().Add(GuiBuilder.Of("gen_artifact_index_window")),

            GuiBuilder.Type("gen_artifact_index_window", "window")
                .Gapped()
                .Name("gen_artifact_index_window")
                .AllowOutside()
                .Movable()
                .ParentAnchor("center")
                .Position(0, -40)
                .Size(780, 720)
                .Using("Window_Background", "Window_Decoration_Spike")
                .Gap().Visible(window.IsShown())

                // The refresh. A decision cannot reach the GUI layer, so it sets a flag and this
                // does the work when the flag makes the window appear.
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
                            .Text("GEN_ARTIFACT_INDEX_TITLE"))
                        // The same scripted_gui the window's `visible` asks. One entry owns both
                        // directions, so opening and closing cannot disagree about what open means.
                        .Gap().Add(GuiBuilder.BlockOverride("button_close")
                            .DataContext(GuiExpr.Raw("GetScriptedGui( 'gen_artifact_index_window' )"))
                            .OnClick(GuiExpr.Raw(
                                $"ScriptedGui.Execute( {player} )"))))

                    .Gap().Add(GuiBuilder.Of("text_multi")
                        .ExpandingH()
                        .MaxWidth(700)
                        .Text("GEN_ARTIFACT_INDEX_BLURB"))

                    .Gap().Add(GuiBuilder.ScrollBox()
                        .Expanding()
                        .Gap().Add(GuiBuilder.BlockOverride("scrollbox_content")
                            .Add(GuiBuilder.VBox()
                                .ExpandingH()
                                .Spacing(4)
                                .DataModel(entries)
                                .Gap().Add(GuiBuilder.Item().Add(Treasure()))))

                        .Gap().Add(GuiBuilder.BlockOverride("scrollbox_empty")
                            .Visible(GuiExpr.IsDataModelEmpty(entries))
                            .Text("GEN_ARTIFACT_INDEX_EMPTY"))))));

        // THE LINE THE WHOLE FILE HANGS ON.
        //
        // `types` above only DECLARES the host. The scripted_widgets registry resolves a top-level
        // widget INSTANCE, so without this bare instantiation the file parses, loads, reports
        // "Loading ... is complete", and then the registry says
        //
        //     Could not find widget 'gen_artifact_index_host' in file 'gui/gen_artifact_index.gui'
        //
        // and nothing appears. Nothing else in the log distinguishes that from a window whose
        // visibility gate is simply false, which is what makes it worth this many lines.
        doc.Add(GuiBuilder.Of("gen_artifact_index_host"));

        doc.Ship(modDir);

        // The registry entry. Not a .gui file and so not a GuiDocument, but it is written here
        // rather than kept in BaseFilesToCopy because it names the file above by path: the two are
        // one unit, and a registry pointing at a window that moved reports "Could not find widget"
        // and nothing else.
        string registry = Path.Combine(modDir, "gui", "scripted_widgets");
        Directory.CreateDirectory(registry);
        Io.ParadoxText.WriteNoBom(
            Path.Combine(registry, "gen_artifact_index.txt"),
            "# Instantiates the artifact index. Written by Emit/GuiWriter.cs.\n"
            + "#\n"
            + "# Names the HOST type, not the window itself: the host is what exists from startup,\n"
            + "# and the window it contains is what appears when the decision sets the flag.\n"
            + "gui/gen_artifact_index.gui = gen_artifact_index_host\n");
    }

    /// <summary>
    /// One row: what the thing is, and who has it.
    ///
    /// Every string is a single datafunction with no literal beside it, which is not a style
    /// preference — <c>text</c> routes its whole contents through the localizer and logs an
    /// unlocalized-text error per line per load when a literal is mixed in. Two widgets side by
    /// side cost nothing and cannot trip it.
    /// </summary>
    private static GuiBuilder Treasure()
    {
        return GuiBuilder.HBox()
            .DataContext(GuiExpr.Raw("Scope.Artifact"))
            .ExpandingH()
            .Spacing(10)

            .Gap().Add(GuiBuilder.Background()
                .Texture("gfx/interface/component_masks/mask_brushed.dds")
                .Color("0.2", "0.2", "0.31", "0.45"))

            // Vanilla's own artifact icon: rarity frame, unique marker and the full artifact
            // tooltip, all from the datacontext already in scope.
            .Gap().Add(GuiBuilder.Of("icon_artifact").Size(64, 64))

            .Gap().Add(GuiBuilder.VBox()
                .ExpandingH()
                .Align("left")
                .Add(GuiBuilder.TextSingle()
                        .ExpandingH()
                        .Align("left")
                        .Format("#high")
                        .Text(GuiExpr.Raw("Artifact.GetName")),
                     GuiBuilder.TextSingle()
                        .ExpandingH()
                        .Align("left")
                        .Format("#weak")
                        .Text(GuiExpr.Raw("Artifact.GetRarityAndSlotType"))))

            .Gap().Add(GuiBuilder.VBox()
                .Align("right")
                .Add(GuiBuilder.TextSingle()
                        .Align("right")
                        .Text(GuiExpr.Raw("Artifact.GetOwner.GetNameNoTooltip")),
                     GuiBuilder.TextSingle()
                        .Align("right")
                        .Format("#weak")
                        .Text(GuiExpr.Raw("Artifact.GetOwner.GetPrimaryTitle.GetNameNoTooltip"))));
    }
}
