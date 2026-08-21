using System.Diagnostics;

namespace Ck3MapGen.Gui;

/// <summary>
/// The Azgaar walkthrough, in the window instead of the Discord.
///
/// The pairing rules — one map, one unzoomed view, a PNG *and* a Full JSON, black oceans, Stretch
/// normalization — are exactly the kind of knowledge that reads as obvious once known and costs a
/// wasted run per rule to discover. This keeps the whole recipe one click from the toolbar chip
/// that consumes its output, with the actions (open Azgaar, choose the export) on the same page as
/// the words.
/// </summary>
public sealed class AzgaarGuide : GuideForm
{
    /// <summary>Raised by the "Choose export…" button, so the guide ends where the import starts.</summary>
    public event Action? ChooseExport;

    public AzgaarGuide() : base("Importing a map from Azgaar", 640)
    {
        Heading("In Azgaar (azgaar.github.io)");
        Step(1, "Options tab — set the canvas to 1920 × 960. That 2:1 shape is CK3's own; the "
              + "export scale in step 5 is what buys back the resolution.");
        Step(2, "Layers tab — show the Heightmap layer. Turn Vignette off. Rivers may stay on if "
              + "you like the look, but only with a 5× export.");
        Step(3, "Styles tab — press +, then paste in the ck3style.json style (shared in the "
              + "Discord's links channel) and name it anything.");
        Step(4, "Sanity check: oceans pure black, land in smooth grey gradients. The map should "
              + "read as a heightmap, not a painting — Azgaar has more knobs if it doesn't yet.");
        Step(5, "Export — drag the PNG/JPEG scale to 4–5×, then press the .png button.");
        Step(6, "Still under Export — press Full under \"Export to JSON\". You leave with two "
              + "files from the same view of the same map: the PNG and the JSON.");

        Heading("In this tool");
        Step(1, "Choose the exported PNG with the heightmap button, top left.");
        Step(2, "Choose the Full JSON from the Azgaar menu beside it (or the AzgaarJsonPath row "
              + "in the settings).");
        Step(3, "Say yes when offered Stretch normalization — Azgaar's heights sit compressed "
              + "against CK3's scale, and Stretch is what makes the relief land right in game. "
              + "It lives under Height scale if you want it back.");
        Step(4, "Tune anything else. Settings the export takes over grey out, with the reason on "
              + "the row.");
        Step(5, "Preview (F5), then Write mod.");

        Note("The PNG and the JSON must come from the same map and the same unzoomed view — the "
           + "loader checks the pair against each other and reports a mismatch rather than "
           + "silently importing it.");

        var open = Theme.MakeButton("Open Azgaar ↗", 110);
        open.Click += (_, _) => Process.Start(
            new ProcessStartInfo("https://azgaar.github.io/Fantasy-Map-Generator/")
                { UseShellExecute = true });

        var choose = Theme.MakeButton("Choose export…", 118, primary: true);
        choose.Click += (_, _) => ChooseExport?.Invoke();

        AddAction(open);
        AddAction(choose);
        AddCloseAction();
    }
}
