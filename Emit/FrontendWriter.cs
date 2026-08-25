using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ck3MapGen.Io;

namespace Ck3MapGen.Emit;

/// <summary>
/// Rewrites vanilla's <c>gui/frontend_main.gui</c>: injects the generator's info box, and blanks the
/// main-menu 3D character portrait widgets that cold-boot CTD on a procedural map.
///
/// The suppression was lifted on trial on 2026-08-24 and restored on 2026-08-25 after the game
/// crashed on load. It is not a cosmetic setting — see the block comment in
/// <see cref="WriteFrontend"/> for what a clean ck3-tiger run does and does not prove about it.
/// </summary>
public static class FrontendWriter
{
    /// <summary>
    /// Anything that triggers the frontend to construct a 3D character portrait.
    /// </summary>
    private static readonly string[] Triggers =
        ["ShouldShowChallengeCharacter", "Portrait("];

    /// <summary>Block headers to comment out wholesale.</summary>
    private static readonly string[] Openers = ["widget = {", "portrait_button = {"];

    private const string GeneratorInfoText = """
        flowcontainer = {
            name = "generator_info_box"
            parentanchor = bottom|right
            position = { -15 -60 }
            direction = vertical
            ignoreinvisible = yes

            text_single = {
                parentanchor = right
                fontsize = 13
                raw_text = "CK3 Procedural Generator by BabyLettuce22"
                default_format = "#high"
            }

            text_single = {
                parentanchor = right
                fontsize = 12
                raw_text = "Check regularly for updates:"
                default_format = "#low"
            }

            button_group = {
                parentanchor = right
                tooltip = "https://github.com/babylettuce22/ck3procedural"

                text_single = {
                    fontsize = 12
                    raw_text = "https://github.com/babylettuce22/ck3procedural"
                    default_format = "#clickable"
                }
            }
        }
    """;

    public static void WriteFrontend(string modDir, string gameDir)
    {
        string source = Path.Combine(gameDir, "gui", "frontend_main.gui");
        if (!File.Exists(source))
        {
            Console.WriteLine("  frontend: vanilla gui/frontend_main.gui not found, skipped");
            return;
        }

        var lines = File.ReadAllLines(source).ToList();

        // 1. Comment out all main-menu portrait widgets wholesale.
        //
        // CK3 hard-crashes on the main menu when a bookmark file names a character with no
        // common/bookmark_portraits/<name>.txt — nested companions especially, since they are easy
        // to add without noticing each one needs a record. Blanking the widgets is the only way to
        // boot past it.
        //
        // ---- Turned off 2026-08-24 on trial, RESTORED 2026-08-25 --------------------------------
        // The trial was run on the strength of static checks: every name the bookmark files mention
        // had a portrait record and a dna_data entry on two seeds, and ck3-tiger reported no
        // crash-graded findings. The game crashed on load anyway.
        //
        // Which is the lesson worth keeping: tiger clearing the bookmarks is not evidence the menu
        // will build their portraits. It checks that a record exists for each name, not that the
        // record's genes resolve against the wardrobe this map actually ships — and an unresolvable
        // gene reference is a null the frontend walks straight into. Do not turn this off again
        // without a confirmed cold boot in the running game; the static pass cannot see the failure.
        // ------------------------------------------------------------------------------------------
        var disabled = new List<string>();

        while (true)
        {
            int anchor = lines.FindIndex(IsLiveTrigger);
            if (anchor < 0) break;

            int start = -1;
            for (int i = anchor; i >= 0; i--)
            {
                if (lines[i].TrimStart().StartsWith('#')) continue;
                if (!Openers.Contains(lines[i].Trim())) continue;
                start = i;
                break;
            }

            if (start < 0)
            {
                Console.WriteLine("  frontend: enclosing portrait block not found, stopping scan");
                break;
            }

            int end = -1, depth = 0;
            for (int i = start; i < lines.Count; i++)
            {
                string body = lines[i];
                int hash = body.IndexOf('#');
                if (hash >= 0) body = body[..hash];

                depth += body.Count(c => c == '{') - body.Count(c => c == '}');
                if (depth > 0) continue;
                end = i;
                break;
            }

            if (end < 0)
            {
                Console.WriteLine("  frontend: portrait block never closes, stopping scan");
                break;
            }

            for (int i = start; i <= end; i++) lines[i] = "#" + lines[i];
            disabled.Add($"{start + 1}-{end + 1}");
        }

        // 2. Inject Generator Info Text right before clickable_version_number
        string fullText = string.Join('\n', lines);
        int versionIdx = fullText.IndexOf("clickable_version_number = {", StringComparison.Ordinal);
        if (versionIdx >= 0)
        {
            fullText = fullText.Insert(versionIdx, GeneratorInfoText + "\n\n\t");
        }

        string dir = Path.Combine(modDir, "gui");
        Directory.CreateDirectory(dir);
        ParadoxText.WriteBom(Path.Combine(dir, "frontend_main.gui"), fullText + "\n");

        Console.WriteLine(disabled.Count == 0
            ? "  frontend: no main-menu portrait widgets found to suppress, info box injected"
            : $"  frontend: {disabled.Count} main-menu portrait widgets suppressed "
              + $"(lines {string.Join(", ", disabled)}), info box injected");
        return;

        bool IsLiveTrigger(string line) =>
            !line.TrimStart().StartsWith('#') &&
            Triggers.Any(t => line.Contains(t, StringComparison.Ordinal));
    }
}