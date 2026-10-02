using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Ck3MapGenLauncher;

// Starts app\Ck3MapGen.exe and exits. The working directory is set to app\ so the app sees
// exactly what it saw when it was double-clicked from its own folder; it looks its files up
// beside its own exe anyway, and settings live in AppData. Arguments are passed through, but
// the launcher does not wait, so for CLI use run app\Ck3MapGen.exe directly.
static class Program
{
    const string AppFolder = "app";
    const string AppExe = "Ck3MapGen.exe";
    const string Title = "CK3 Procedural Generator";

    [STAThread]
    static int Main(string[] args)
    {
        string root = AppDomain.CurrentDomain.BaseDirectory;
        string exe = Path.Combine(root, AppFolder, AppExe);

        if (!File.Exists(exe))
        {
            Show($"Could not find {AppFolder}\\{AppExe} next to this launcher.\n\n" +
                 "Extract the whole download, keeping the app folder beside this file.");
            return 1;
        }

        try
        {
            Process.Start(new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(exe),
                Arguments = string.Join(" ", args.Select(Quote)),
            });
            return 0;
        }
        catch (Exception e)
        {
            Show($"Could not start {AppFolder}\\{AppExe}:\n\n{e.Message}");
            return 1;
        }
    }

    // Windows command-line quoting (the CommandLineToArgvW rules), so arguments arrive intact.
    static string Quote(string arg)
    {
        if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return arg;
        var sb = new StringBuilder("\"");
        int slashes = 0;
        foreach (char c in arg)
        {
            if (c == '\\') { slashes++; continue; }
            sb.Append('\\', c == '"' ? slashes * 2 + 1 : slashes).Append(c);
            slashes = 0;
        }
        return sb.Append('\\', slashes * 2).Append('"').ToString();
    }

    static void Show(string text) => MessageBoxW(IntPtr.Zero, text, Title, 0x10 /* MB_ICONERROR */);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
