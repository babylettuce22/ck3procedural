using Ck3MapGen.GameGui;

namespace Ck3MapGen.Emit;

/// <summary>
/// The scaffold every centred, movable window of ours stands in — the artifact index, the wonder
/// index and the settings panel. Each wrote the same two types out by hand: a zero-size host that
/// is always instantiated, and the window it holds, with the standard background, fade and sounds.
/// What differs between them is the name, the size, whether opening runs a gather, and the body.
///
/// <see cref="ChronicleWindow"/> is not one of these — it is a right-hand panel with its own
/// anchors and layer — but its host asks <see cref="ScreenIsFree"/> too.
/// </summary>
internal static class CentredWindow
{
    /// <summary>
    /// When a custom window may be on screen at all: not under the pause menu, not for an observer
    /// with no character, and only in the default GUI mode. The host carries it, because the host
    /// is always instantiated.
    /// </summary>
    public static GuiExpr ScreenIsFree => GuiExpr.Raw(
        "And( Not( IsPauseMenuShown ), And( Or( Not( IsObserver ), GetPlayer.IsValid ), "
        + "IsDefaultGUIMode ) )");

    /// <summary>
    /// The host type <c>{name}_host</c> and the window type <c>{name}_window</c>, for a
    /// <c>types</c> block. The file still needs the bare <c>{name}_host</c> instantiation after
    /// that block — see <see cref="ArtifactIndex"/> for why its absence fails silently.
    /// </summary>
    /// <param name="window">The scripted_gui whose open state shows the window.</param>
    /// <param name="gather">Run as the window appears, to refresh what it lists; null when every
    /// part of the window reads its state live and there is nothing to rebuild.</param>
    public static GuiBuilder[] Types(string name, int width, int height, ScriptedGui window,
        ScriptedGui? gather, GuiBuilder body)
    {
        var show = GuiBuilder.State("_show").Using("Animation_FadeIn_Quick", "Sound_WindowShow_Standard");
        if (gather is not null) show = show.Quoted("on_start", gather.Execute().ToString());

        return
        [
            GuiBuilder.Type($"{name}_host", "window")
                .Name($"{name}_host")
                .AllowOutside()
                .ParentAnchor("center")
                .Size(0, 0)
                .Gap().Visible(ScreenIsFree)
                .Gap().Add(GuiBuilder.Of($"{name}_window")),

            GuiBuilder.Type($"{name}_window", "window")
                .Gapped()
                .Name($"{name}_window")
                .AllowOutside()
                .Movable()
                .ParentAnchor("center")
                .Position(0, -40)
                .Size(width, height)
                .Using("Window_Background", "Window_Decoration_Spike")
                .Gap().Visible(window.IsShown())
                .Gap().Add(show)
                .Gap().Add(GuiBuilder.State("_hide")
                    .Using("Animation_FadeOut_Quick", "Sound_WindowHide_Standard"))
                .Gap().Add(body),
        ];
    }
}
