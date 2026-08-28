using System.Text;

namespace Ck3MapGen.Core;

/// <summary>
/// Console capture for two phases that run at the same time.
///
/// The run log is a diagnostic people read top to bottom, and several phases print counts that
/// only make sense next to the line above them — the province report, the tier tallies, the
/// forge's inventory. Two concurrent phases writing to <see cref="Console"/> interleave those
/// into something nobody can read and, worse, something that cannot be diffed against a previous
/// run. Since <see cref="RunLog"/> tees the same stream into proctool.txt, an interleaved console
/// is an interleaved permanent record too.
///
/// So neither branch prints while it runs. Each one's output is collected, and the caller replays
/// them in the order the phases used to run in, which leaves both the terminal and proctool.txt
/// byte-identical to a sequential run. That matters more than it sounds: the log is one of the two
/// regression checks this project actually has.
///
/// Routing is by <see cref="AsyncLocal{T}"/> rather than by passing a writer down, because the
/// Console.WriteLine calls are scattered through several hundred sites in the writers and none of
/// them are going to grow a parameter for this. AsyncLocal flows into the task and does not flow
/// back out, which is exactly the scoping wanted.
/// </summary>
public static class ConsoleFork
{
    private static readonly AsyncLocal<StringWriter?> Diverted = new();
    private static bool _installed;

    /// <summary>
    /// Puts the router in front of whatever <see cref="Console.Out"/> currently is. Call after
    /// <see cref="RunLog.Begin"/>, so the router sits outside the tee and a replayed buffer still
    /// reaches the run log — in replay order, not in the order the threads produced it.
    /// </summary>
    public static void Install()
    {
        if (_installed) return;
        Console.SetOut(new Router(Console.Out));
        _installed = true;
    }

    /// <summary>A branch that is running, and the output it has collected so far.</summary>
    public sealed class Branch(Task work, StringWriter buffer)
    {
        private bool _done;

        /// <summary>
        /// Waits for the branch and replays its output. Faults are rethrown after the replay, so a
        /// branch that died still says what it managed to do first — which is the only way to tell
        /// where it died.
        ///
        /// Idempotent, and the flag is set before the wait rather than after, so a branch that
        /// faulted counts as joined too. Callers pair a join at the point they need the result with
        /// an unconditional one in a finally, and neither the output nor the exception should be
        /// delivered twice: a second replay would duplicate the log, and a second throw from inside
        /// a finally would replace whatever exception was already on its way out.
        /// </summary>
        public void JoinAndReplay()
        {
            if (_done) return;
            _done = true;

            try { work.GetAwaiter().GetResult(); }
            finally { Console.Write(buffer.ToString()); }
        }
    }

    /// <summary>Starts <paramref name="work"/> on the thread pool with its output collected.</summary>
    public static Branch Start(Action work)
    {
        var buffer = new StringWriter();
        var task = Task.Run(() =>
        {
            Diverted.Value = buffer;
            try { work(); }
            finally { Diverted.Value = null; }
        });
        return new Branch(task, buffer);
    }

    /// <summary>
    /// Runs <paramref name="work"/> here and now with its output collected into
    /// <paramref name="buffer"/>. The caller decides when that reaches the console — which is the
    /// whole point, since the branch running on this thread is usually not the one whose lines came
    /// first.
    ///
    /// The buffer is the caller's rather than a return value so that a branch which throws still
    /// leaves behind what it printed before it died. A return value would be lost with the stack,
    /// and the last few lines before a failure are most of the diagnosis.
    /// </summary>
    public static void CaptureInto(StringWriter buffer, Action work)
    {
        var previous = Diverted.Value;
        Diverted.Value = buffer;
        try { work(); }
        finally { Diverted.Value = previous; }
    }

    private sealed class Router(TextWriter inner) : TextWriter
    {
        public override Encoding Encoding => inner.Encoding;

        private TextWriter Target => Diverted.Value ?? inner;

        public override void Write(char value) => Target.Write(value);
        public override void Write(string? value) => Target.Write(value);
        public override void Write(char[] buffer, int index, int count) => Target.Write(buffer, index, count);
        public override void WriteLine() => Target.WriteLine();
        public override void WriteLine(string? value) => Target.WriteLine(value);
        public override void Flush() => inner.Flush();
    }
}
