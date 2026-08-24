using System.Text;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// Turns the chronicle into the localisation the title window reads.
///
/// One key per title, named <c>gen_lore_&lt;title key&gt;</c>, which is the whole contract with
/// <see cref="GuiWriter"/>: the panel there resolves
/// <c>Localize( Concatenate( 'gen_lore_', Title.GetKey ) )</c> and gates its button on the result
/// being non-empty. There is no scripted_gui, no variable and no on_action in the path — a title
/// either has a key or it does not, and a title that does not simply shows no button.
///
/// That gate is also what keeps this file honest about titles it has nothing to say for. Baronies
/// get no entry (nothing in the chronicle is recorded below county level) and neither does
/// wilderness, so both correctly show no button at all rather than an empty panel.
///
/// One line in the panel is not chronicle at all: a title inside a struggle region closes with the
/// struggle's name. That is why this runs after <see cref="MapGen.StruggleMap"/> is built even
/// though the chronicle it reads was built before it — see <see cref="MapGen.StruggleMap.Note"/>.
/// </summary>
public static class ChronicleWriter
{
    /// <summary>
    /// How many remembered lines a title's panel gets. The struggle cross-reference below is not
    /// one of them and is appended past this.
    ///
    /// The panel scrolls, so this is a judgement about reading rather than about space: past about
    /// this many the entries stop being a history and start being a list, and the specific ones get
    /// buried by the generic ones. Empires are the pressure case — they roll up from every county
    /// beneath them — which is why the roll-up is capped per child as well.
    /// </summary>
    private const int MaxLines = 9;

    /// <summary>How many times one kind of event may speak for a duchy or above. See the roll-up
    /// comment in <see cref="WriteAll"/> for why a cap is needed at all.</summary>
    private const int MaxPerKind = 2;

    public static void WriteAll(
        string modDir, ChronicleMap chronicle, StruggleMap struggles, List<Title> empires)
    {
        var loc = new LocFile();

        int written = 0;
        int noted = 0;

        // Tree order here, not index order: this file is read by people as often as by the game
        // when something looks wrong, and an empire followed by its kingdoms is far easier to scan
        // than every title of one tier in a block.
        foreach (var title in Titles.Flatten(empires))
        {
            if (title.Tier == "b") continue;

            // Four per child rather than the default two. The roll-up takes the most contested
            // events first, so a narrow budget hands the kind cap below nothing but wars and
            // feuds -- an empire whose every line is a rivalry, with no sense of who lives there.
            // Four is enough that a quiet county still contributes its settlement and its faith.
            var events = chronicle.For(title, perChild: 4);
            if (events.Count == 0) continue;

            // The opening summary is kept whatever else goes. It is the only line that says what
            // the title IS, and it sorts to the front, so trimming from the old end -- which is
            // otherwise the right end to trim, since a reader who loses a settlement line still
            // understands the realm while one who loses the live war does not -- would drop it
            // first on exactly the large titles that need it most.
            var summary = events.Where(e => e.Kind == ChronicleKind.Realm).Take(1).ToList();
            var rest = events.Where(e => e.Kind != ChronicleKind.Realm).ToList();

            // A county says each thing about itself once. A duchy borrows from six counties and an
            // empire from eighty, so without a cap the largest titles read as one sentence with the
            // place name swapped -- eight "the faith reached X in Y" lines, or eight rivalries,
            // depending only on which kind happened to be most numerous underneath. Two per kind is
            // enough to show a pattern exists without letting it crowd out every other kind.
            if (title.Tier != "c")
            {
                rest = rest
                    .GroupBy(e => e.Kind)
                    .SelectMany(g => g
                        .OrderByDescending(e => e.Tension)
                        .ThenByDescending(e => e.Year)
                        .Take(MaxPerKind))
                    .OrderBy(e => e.Year)
                    .ToList();
            }

            var lines = summary
                .Concat(rest.Skip(Math.Max(0, rest.Count - (MaxLines - summary.Count))))
                .Select(e => e.Text)
                .ToList();

            // Over the line budget rather than inside it, and last whatever else the panel holds.
            // It is a cross-reference and not a memory: it dates from now, it is the same sentence
            // every time the panel is reopened, and it is the only line that points at something
            // the player can go and look at in another window. Spending one of the nine remembered
            // things on it would trade a piece of the history for a signpost to a mechanic.
            //
            // Reached only for titles that already had events, which is what keeps wilderness out:
            // a struggle's counties include whatever wilderness its duchies contain, the chronicle
            // records nothing below the settled world, and a wilderness county whose whole panel
            // was one struggle footnote would put a lore button on empty ground.
            if (struggles.Note(title) is { } note)
            {
                lines.Add(note);
                noted++;
            }

            // AddBuilt, not Add: the paragraph breaks between entries are deliberate \n escapes
            // that Chronicle put there, and escaping them again would render them as backslashes.
            loc.AddBuilt($"gen_lore_{title.Key}", string.Join("\\n\\n", lines));
            written++;
        }

        loc.Write(Path.Combine(modDir, "localization", "english", "gen_title_lore_l_english.yml"));

        Console.WriteLine($"  title lore: {written} titles given a chronicle"
                        + (noted > 0 ? $", {noted} of them inside a struggle" : ""));
    }
}
