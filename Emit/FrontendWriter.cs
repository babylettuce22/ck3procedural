using System.Text;
using Ck3MapGen.Io;

namespace Ck3MapGen.Emit;

/// <summary>
/// Disables the main menu's 3D character portraits.
///
/// CK3's frontend renders live portraits for the bookmark's main character, its secondary
/// character, its heir and the challenge character. That happens in the step immediately after
/// history loading — `gameapplication.cpp:558 Setting idler 'Frontend'` — which is exactly where
/// a generated map stops with no log output and two worker threads spinning.
///
/// ck2rpg's shipped template does the same thing: its `gui/frontend_main.gui` has the whole
/// portrait widget commented out, leaving 2 active portrait-related lines where vanilla has 102.
/// A working generator would not carry that edit by accident.
///
/// Rather than ship a copy of vanilla's file (which would pin us to one game version), we read
/// the installed one and comment out the offending block, anchored on a distinctive expression
/// instead of a line number so it survives patches.
/// </summary>
public static class FrontendWriter
{
    /// <summary>
    /// Anything that makes the frontend build a character portrait. There are two such places in
    /// 1.19: the widget positioned by whether a challenge character is shown (which holds the
    /// main, secondary and heir portraits plus their drop shadows), and a standalone
    /// portrait_button for the challenge character itself.
    /// </summary>
    private static readonly string[] Triggers =
        ["ShouldShowChallengeCharacter", "Portrait("];

    /// <summary>Block headers we are willing to comment out wholesale.</summary>
    private static readonly string[] Openers = ["widget = {", "portrait_button = {"];

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

        // Each pass disables one block. Re-scanning from the top afterwards is what lets a later
        // portrait block be found once an earlier one is commented out.
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
                Console.WriteLine("  frontend: enclosing block not found, stopping");
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
                Console.WriteLine("  frontend: block never closes, stopping");
                break;
            }

            for (int i = start; i <= end; i++) lines[i] = "#" + lines[i];
            disabled.Add($"{start + 1}-{end + 1}");
        }

        string dir = Path.Combine(modDir, "gui");
        Directory.CreateDirectory(dir);
        ParadoxText.WriteBom(Path.Combine(dir, "frontend_main.gui"),
            string.Join('\n', lines) + "\n");

        Console.WriteLine($"  frontend: disabled main-menu portraits " +
                          $"(commented lines {string.Join(", ", disabled)} of frontend_main.gui)");
        return;

        bool IsLiveTrigger(string line) =>
            !line.TrimStart().StartsWith('#') &&
            Triggers.Any(t => line.Contains(t, StringComparison.Ordinal));
    }
}