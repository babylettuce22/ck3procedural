using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ck3MapGen.Io;

namespace Ck3MapGen.Emit;

/// <summary>
/// Rewrites vanilla's <c>gui/frontend_main.gui</c>: injects the generator's info box, and — when the
/// suppression below is switched back on — blanks the main-menu 3D character portrait widgets that
/// used to cold-boot CTD on a procedural map.
///
/// As of 2026-08-24 the suppression is off on trial, so the main menu draws its portraits again. See
/// the block comment in <see cref="WriteFrontend"/> for why, and for how to put it back.
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
        // ---- TURNED OFF 2026-08-24, on trial ----------------------------------------------------
        // This suppression existed because CK3 hard-crashes on the main menu when a bookmark file
        // names a character with no common/bookmark_portraits/<name>.txt, and the generated bookmarks
        // used to do exactly that — nested companions especially, since they are easy to add without
        // noticing they need a record each. Blanking the widgets was the only way to boot.
        //
        // Every name the bookmark files mention now has both a portrait record and a dna_data entry,
        // checked on two seeds, and ck3-tiger reports no crash-graded findings. So the portraits are
        // being let through to see whether the menu draws them. Not yet confirmed in the running
        // game — if the cold-boot CTD comes back, this block is the first thing to restore.
        //
        // To restore: delete the /* */ around the loop, and put `disabled` back in the Console line
        // at the end of this method and `IsLiveTrigger` back at the bottom.
        // ------------------------------------------------------------------------------------------
        /*
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
        */

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

        Console.WriteLine("  frontend: main-menu portraits left ENABLED (suppression off, on trial), "
                          + "info box injected");
        return;

        /*
        bool IsLiveTrigger(string line) =>
            !line.TrimStart().StartsWith('#') &&
            Triggers.Any(t => line.Contains(t, StringComparison.Ordinal));
        */
    }
}