using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ck3MapGen.Io;

namespace Ck3MapGen.Emit;

/// <summary>
/// Disables vanilla's main-menu 3D character portrait widgets in frontend_main.gui
/// to prevent cold-boot CTDs when running on procedural maps, and injects generator metadata.
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
        var disabled = new List<string>();

        // 1. Comment out all main-menu portrait widgets wholesale
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

        Console.WriteLine($"  frontend: disabled main-menu portraits " +
                          $"(commented lines {string.Join(", ", disabled)} of frontend_main.gui), info box injected");
        return;

        bool IsLiveTrigger(string line) =>
            !line.TrimStart().StartsWith('#') &&
            Triggers.Any(t => line.Contains(t, StringComparison.Ordinal));
    }
}