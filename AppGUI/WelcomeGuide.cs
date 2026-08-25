namespace Ck3MapGen.AppGUI;

/// <summary>
/// The whole workflow on one page: heightmap in, preview, read, tune, write, play.
///
/// Shown once, uninvited, on the very first launch — the moment the tool most resembles a wall of
/// 121 settings and an empty map — and afterwards lives behind the ? at the far right of the
/// toolbar, where returning users can ignore it forever.
/// </summary>
public sealed class WelcomeGuide : GuideForm
{
    /// <summary>Raised by "Choose heightmap…", so the first step of the text is also a button.</summary>
    public event Action? ChooseHeightmap;

    /// <summary>Raised by "Azgaar walkthrough…" — the import recipe is its own page.</summary>
    public event Action? OpenAzgaarGuide;

    public WelcomeGuide() : base("Making a world", 700)
    {
        Heading("Your first world");
        Step(1, "Choose a heightmap — a 16-bit greyscale PNG with even dimensions, ideally CK3's "
              + "own 2:1 shape. No heightmap handy? The vanilla one works: map_data\\heightmap.png "
              + "inside your CK3 install. The ▾ beside the button remembers recent picks.");
        Step(2, "Preview (F5). Everything generates without writing a file — provinces, titles, "
              + "rivers, climate, cultures. A big map takes a few minutes the first time; the "
              + "progress bar learns your machine's pace as it goes.");
        Step(3, "Read the result. Map modes group into Physical, Climate, De Jure and World; "
              + "the [ and ] keys flip through a group. Hover the map for what is under the "
              + "cursor, and check the 3D render tab — its Surface picker can drape any map mode "
              + "over the relief.");
        Step(4, "Tune and go again. Settings sit in sections on the left, searchable, with an "
              + "Advanced toggle for the deep knobs. Roll the seed for a different world from the "
              + "same settings, and save a preset when you like one.");
        Step(5, "Write mod (Ctrl+S). This writes a playable mod to your Paradox mod folder. The "
              + "written-world modes light up — Realms, Cultures, Faiths, Ethnicities — and "
              + "clicking the map in any ✎ mode opens an editor. The Realms mode drills: click a "
              + "realm to focus it, keep clicking to descend, Esc to back out.");
        Step(6, "Launch CK3. The first time, enable the mod in a playset in the game's launcher; "
              + "after that the Launch button takes you straight in.");

        Heading("Shortcuts");
        Shortcut("F5", "Preview");
        Shortcut("Ctrl+S", "Write mod");
        Shortcut("Ctrl+E", "Export the current view as a PNG");
        Shortcut("[  ]", "Previous / next map mode");
        Shortcut("Ctrl+[  ]", "Previous / next mode group");
        Shortcut("Esc", "Cancel a run · step out of a focused realm");
        Shortcut("Ctrl+click", "In Realms mode: jump straight to a county and its holder");

        Note("Importing a map from Azgaar (azgaar.github.io) has its own walkthrough — the "
           + "Azgaar button in the toolbar, or right here:");

        var azgaar = Theme.MakeButton("Azgaar walkthrough…", 140);
        azgaar.Click += (_, _) => OpenAzgaarGuide?.Invoke();

        var choose = Theme.MakeButton("Choose heightmap…", 136, primary: true);
        choose.Click += (_, _) => ChooseHeightmap?.Invoke();

        AddAction(azgaar);
        AddAction(choose);
        AddCloseAction();
    }
}
