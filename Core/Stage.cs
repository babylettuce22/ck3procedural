using System.Diagnostics;

namespace Ck3MapGen.Core;

/// <summary>
/// Wall-clock accounting for the pipeline's phases, printed as a table at the end of a run.
///
/// The tool already prints a timing next to several individual steps, which is enough to see that
/// one step got slower and useless for deciding which step to work on — the numbers are scattered
/// through eighty lines of log and they do not add up to anything. This records every phase against
/// one clock and prints them ranked, so "where does the time go" has an answer that does not involve
/// reading the whole log with a calculator.
///
/// Spans inside other spans go through <see cref="Detail"/>, which prints them but leaves them out
/// of the total — their time already belongs to the parent, and counting it twice is what turns the
/// unaccounted remainder negative and makes the whole table look wrong.
/// </summary>
public static class Stage
{
    private static readonly object Gate = new();
    private static readonly List<(string Name, long Ms, bool Nested)> Recorded = [];
    private static readonly Stopwatch Wall = new();

    /// <summary>
    /// Raised as each phase starts, so a front end can say which one is running. The CLI ignores it;
    /// the GUI puts the name in the status bar, which is the difference between a window that looks
    /// hung for two minutes and one that is visibly working.
    ///
    /// Raised on whatever thread the phase runs on — a handler that touches UI must marshal.
    /// </summary>
    public static event Action<string>? Entering;

    /// <summary>
    /// The same, for spans inside a phase. Kept separate from <see cref="Entering"/> because their
    /// time is already inside their parent's: a progress estimate that counted both would count it
    /// twice. This is for saying <em>which part</em> of a long phase is running, and the province
    /// partition — the longest of them — is the reason it exists.
    /// </summary>
    public static event Action<string>? Detailing;

    /// <summary>
    /// Cancellation for the run in progress, checked at every phase boundary.
    ///
    /// Phase boundaries and not finer, deliberately: the phases are the loops, most of them
    /// Parallel.For over millions of pixels, and threading a token through all of them would be a
    /// large change to the pipeline for a button. The cost is that Cancel takes effect when the
    /// current phase ends rather than instantly — seconds on a big map, and the longest single
    /// phase is the bound.
    ///
    /// A static because there is one run at a time by construction; the GUI locks its controls for
    /// the duration and the CLI is one pass and exits.
    /// </summary>
    public static CancellationToken Cancellation { get; set; } = CancellationToken.None;

    /// <summary>Starts a fresh accounting. Called once at the top of a run.</summary>
    public static void Begin()
    {
        lock (Gate)
        {
            Recorded.Clear();
            Wall.Restart();
        }
    }

    /// <summary>Times <paramref name="work"/> and files it under <paramref name="name"/>.</summary>
    public static T Time<T>(string name, Func<T> work)
    {
        Enter(name);
        var clock = Stopwatch.StartNew();
        var result = work();
        Record(name, clock.ElapsedMilliseconds, nested: false);
        return result;
    }

    /// <inheritdoc cref="Time{T}"/>
    public static void Time(string name, Action work)
    {
        Enter(name);
        var clock = Stopwatch.StartNew();
        work();
        Record(name, clock.ElapsedMilliseconds, nested: false);
    }

    /// <summary>The phase boundary: announce the phase, and give a cancel somewhere to land.</summary>
    private static void Enter(string name)
    {
        Cancellation.ThrowIfCancellationRequested();
        Entering?.Invoke(name);
    }

    /// <summary>
    /// A span *inside* another one. Reported alongside the rest but not counted toward the total,
    /// because its time is already in its parent's — counting both is what makes the unaccounted
    /// remainder come out negative and the whole table untrustworthy.
    /// </summary>
    public static T Detail<T>(string name, Func<T> work)
    {
        Detailing?.Invoke(name);
        var clock = Stopwatch.StartNew();
        var result = work();
        Record(name, clock.ElapsedMilliseconds, nested: true);
        return result;
    }

    /// <inheritdoc cref="Detail{T}"/>
    public static void Detail(string name, Action work)
    {
        Detailing?.Invoke(name);
        var clock = Stopwatch.StartNew();
        work();
        Record(name, clock.ElapsedMilliseconds, nested: true);
    }

    private static void Record(string name, long ms, bool nested)
    {
        lock (Gate)
        {
            for (int i = 0; i < Recorded.Count; i++)
            {
                if (Recorded[i].Name != name) continue;
                Recorded[i] = (name, Recorded[i].Ms + ms, nested);
                return;
            }
            Recorded.Add((name, ms, nested));
        }
    }

    /// <summary>Prints the phases, slowest first, against the wall clock since <see cref="Begin"/>.</summary>
    public static void Report()
    {
        lock (Gate)
        {
            if (Recorded.Count == 0) return;

            long wall = Math.Max(1, Wall.ElapsedMilliseconds);
            long accounted = Recorded.Where(r => !r.Nested).Sum(r => r.Ms);

            Console.WriteLine();
            Console.WriteLine($"Where the time went ({wall / 1000.0:F1} s total):");

            foreach (var (name, ms, _) in Recorded.OrderByDescending(r => r.Ms))
                Console.WriteLine($"  {ms,6} ms  {100.0 * ms / wall,5:F1}%  {name}");

            Console.WriteLine($"  {wall - accounted,6} ms  {100.0 * (wall - accounted) / wall,5:F1}%  " +
                              "(unaccounted — startup, JIT, everything not phased)");
        }
    }
}
