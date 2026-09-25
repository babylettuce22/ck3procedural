using System.Runtime.InteropServices;
using System.Text;

namespace Ck3MapGen.Core;

/// <summary>
/// The last line of defence: anything nothing else caught is written to
/// <c>%LocalAppData%\Ck3MapGen\crash-*.txt</c> before the process goes down.
///
/// The only log the tool otherwise keeps is the GUI's text box and <see cref="RunLog"/>'s buffer,
/// and both die with the process — an unhandled exception used to leave nothing behind at all. The
/// report carries the tail of that buffer, because the exception alone rarely says which stage of
/// a run it came from.
///
/// Three routes reach here: the WinForms UI thread (<see cref="Application.ThreadException"/>,
/// which keeps the app alive and shows where the report went), any other thread
/// (<see cref="AppDomain.UnhandledException"/>, fatal), and a faulted task nobody awaited
/// (<see cref="TaskScheduler.UnobservedTaskException"/>, which only fires at finalisation — so
/// fire-and-forget calls should use <see cref="TaskReporting.Forget"/> instead of <c>_ =</c>).
/// </summary>
public static class CrashLog
{
    private const int TailLines = 300;
    private const int KeepReports = 20;

    public static string Folder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ck3MapGen");

    /// <summary>
    /// Installs the handlers. Call first thing in <c>Main</c>: the WinForms exception mode can only
    /// be set before the first control exists.
    /// </summary>
    public static void Install()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Report(e.Exception, "UI thread", showDialog: true);

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) Report(ex, "unhandled, process terminating", showDialog: false);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Report(e.Exception, "unobserved task", showDialog: false);
            e.SetObserved();
        };
    }

    /// <summary>
    /// Writes a report and returns its path, or null if even that failed. Never throws. With
    /// <paramref name="showDialog"/>, also tells the user — only honoured on a thread that can
    /// own a window.
    /// </summary>
    public static string? Report(Exception ex, string context, bool showDialog)
    {
        string? path = null;
        try
        {
            Directory.CreateDirectory(Folder);
            path = Path.Combine(Folder, $"crash-{DateTime.Now:yyyyMMdd-HHmmss-fff}.txt");

            var text = new StringBuilder();
            text.AppendLine("CK3 Procedural Map Tool — crash report");
            text.AppendLine($"When:          {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
            text.AppendLine($"Context:       {context}");
            text.AppendLine($"Tool version:  {RunLog.ToolVersion()}");
            text.AppendLine($"Command line:  {Environment.CommandLine}");
            text.AppendLine($"Machine:       {Environment.OSVersion}, .NET {Environment.Version}");
            text.AppendLine();
            text.AppendLine("==== Exception ====");
            text.AppendLine(ex.ToString());
            text.AppendLine();
            text.AppendLine($"==== Last {TailLines} lines of the run log ====");
            string[] log = RunLog.Text.Split('\n');
            text.AppendJoin('\n', log.Skip(Math.Max(0, log.Length - TailLines)));

            File.WriteAllText(path, text.ToString());
            Prune();
            Console.Error.WriteLine($"Crash report written to {path}");
        }
        catch
        {
            // Nowhere left to report to. Losing the report must not replace the original failure.
            path = null;
        }

        if (showDialog && Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            try
            {
                MessageBox.Show(
                    $"Something went wrong:\n\n{ex.Message}\n\n"
                    + (path is null ? "A crash report could not be saved." : $"Details were saved to:\n{path}"),
                    "CK3 Procedural Map Tool", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { /* no UI to show it on */ }
        }

        return path;
    }

    private static void Prune()
    {
        foreach (var old in new DirectoryInfo(Folder).GetFiles("crash-*.txt")
                     .OrderByDescending(f => f.Name).Skip(KeepReports))
            old.Delete();
    }

    // --- Console for the command line ------------------------------------------------------------

    private const int AttachParentProcess = -1;
    private const int StdOutputHandle = -11, StdErrorHandle = -12;

    [DllImport("kernel32.dll")] private static extern bool AttachConsole(int processId);
    [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int handle);

    /// <summary>
    /// Makes command-line output visible when the exe is started from cmd or PowerShell. The
    /// project is a WinExe, so Windows gives it no console: output reached a terminal only when it
    /// was redirected (a pipe or a file, which <c>dotnet run</c> and scripts provide). Started
    /// directly, it printed nothing at all. Attaching to the parent's console fixes the printing;
    /// a redirected stream is left alone. The shell still does not wait for a GUI-subsystem exe,
    /// so use <c>start /wait</c> (cmd) or <c>Start-Process -Wait</c> for the exit code.
    /// </summary>
    public static void AttachParentConsole()
    {
        bool outMissing = GetStdHandle(StdOutputHandle) == IntPtr.Zero;
        bool errMissing = GetStdHandle(StdErrorHandle) == IntPtr.Zero;
        if (!outMissing && !errMissing) return;
        if (!AttachConsole(AttachParentProcess)) return;

        // The parent prompt has already been printed; start on a fresh line under it.
        if (outMissing)
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
            Console.WriteLine();
        }
        if (errMissing)
            Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
    }
}

/// <summary>Fire-and-forget without losing the failure.</summary>
public static class TaskReporting
{
    /// <summary>
    /// Use in place of <c>_ = SomethingAsync()</c>. A discarded task's exception is otherwise only
    /// seen when the finaliser gets round to it, long after the user has moved on, if ever. Faults
    /// are reported on the calling synchronisation context, so from the UI thread the user sees
    /// the dialog; cancellation is not a fault and is ignored.
    /// </summary>
    public static void Forget(this Task task, string what)
    {
        var scheduler = SynchronizationContext.Current is null
            ? TaskScheduler.Default
            : TaskScheduler.FromCurrentSynchronizationContext();

        task.ContinueWith(
            t => CrashLog.Report(t.Exception!.GetBaseException(), what, showDialog: true),
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, scheduler);
    }
}
