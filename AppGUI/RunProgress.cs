using System.Diagnostics;
using Ck3MapGen.Config;
using Ck3MapGen.Core;

namespace Ck3MapGen.AppGUI;

/// <summary>
/// How far through a run we are, and roughly how long is left.
///
/// The estimate is learned from the previous run rather than from weights written down here. Every
/// phase is announced by <see cref="Stage.Entering"/>, so the run before last recorded how long each
/// one took, and this replays that as a plan: the phases in order, each with an expected duration.
/// Nothing about the pipeline is hardcoded, so adding, removing or reordering a phase costs one
/// uncalibrated run and then re-teaches itself. The first run ever has no plan at all, which is why
/// the bar can still fall back to a marquee.
///
/// Two corrections keep the guess honest. Durations scale by the province raster's megapixels, so a
/// profile learned at <c>tiny</c> still predicts <c>vanilla</c> to within its linearity — the map
/// size is only known once the heightmap has been decoded, which is why the scaling is recomputed at
/// every phase boundary rather than at the start. And the phases that have already finished measure
/// how fast this machine is running today against the plan; that ratio is applied to everything
/// still to come, so a run that starts out twice as slow as predicted says so within a phase or two
/// instead of holding to a wrong estimate to the end.
///
/// The fraction never goes backwards. It is assembled from predictions that are revised mid-run, so
/// it genuinely can fall, and a progress bar that retreats is worse than one that pauses.
/// </summary>
internal sealed class RunProgress
{
    private readonly RunProfile _learned;
    private readonly Func<double> _megapixels;

    private readonly List<string> _plan = [];
    private readonly List<string> _finished = [];
    private readonly List<PhaseTime> _actual = [];
    private readonly Dictionary<string, double> _predicted = new(StringComparer.Ordinal);

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private string? _current;
    private long _currentStart;
    private long _finishedAt;
    private double _floor;
    private double _fallback = 1000;

    /// <param name="learned">The previous run of this kind. Empty on the first run ever.</param>
    /// <param name="megapixels">
    /// The province raster's size, read fresh each time: it is a function of the heightmap, which
    /// has not been decoded when the run starts.
    /// </param>
    public RunProgress(RunProfile learned, Func<double> megapixels)
    {
        _learned = learned;
        _megapixels = megapixels;

        foreach (var phase in learned.Phases) _plan.Add(phase.Name);
        Rescale();
    }

    /// <summary>Whether there is a plan to measure against, or only a stopwatch.</summary>
    public bool Calibrated => _plan.Count > 0;

    public TimeSpan Elapsed => _clock.Elapsed;

    /// <summary>The phase now running, for the status line.</summary>
    public string? Phase => _current;

    public void Enter(string name)
    {
        Close();

        // A phase the plan has never seen — the pipeline changed since the profile was recorded.
        // Give it the size of a typical phase and carry on rather than throwing the plan away.
        if (!_plan.Contains(name)) _plan.Add(name);

        _current = name;
        _currentStart = _clock.ElapsedMilliseconds;
        Rescale();
    }

    /// <summary>Ends the run and hands back what it actually cost, to predict the next one with.</summary>
    public RunProfile Finish()
    {
        Close();
        return new RunProfile { Megapixels = _megapixels(), Phases = _actual };
    }

    /// <returns>
    /// How far through, 0 to 1, and how much longer — both null-ish when there is no plan: an
    /// uncalibrated run reports zero and no estimate, and the caller shows elapsed time instead.
    /// </returns>
    public (double Fraction, TimeSpan? Remaining) Sample()
    {
        double total = _plan.Sum(Predicted);
        if (!Calibrated || total <= 0) return (0, null);

        // What the finished phases were predicted to cost against what they did cost. Below 1 the
        // machine is beating the plan; above it, losing to it.
        double done = _finished.Sum(Predicted);
        double speed = done > 0 ? Math.Clamp(_finishedAt / done, 0.1, 10) : 1.0;

        double running = _current is null ? 0 : _clock.ElapsedMilliseconds - _currentStart;

        // A phase that has already outrun its prediction is itself evidence about this machine, and
        // the only evidence there is during the first one. Without this the estimate held whatever
        // the plan said for the whole of that phase — on a machine much slower than the profile came
        // off, that is a wrong number sitting there for the longest stretch of the run.
        if (_current is { } current && Predicted(current) > 0)
            speed = Math.Max(speed, Math.Min(10, running / Predicted(current)));

        // The current phase contributes at most its own prediction, so a phase that overruns stalls
        // the bar at its own boundary instead of running away past the next one.
        double inCurrent = _current is null ? 0 : Math.Min(Predicted(_current), running / speed);

        _floor = Math.Max(_floor, Math.Clamp((done + inCurrent) / total, 0, 0.999));

        double remaining = total * (1 - _floor) * speed;
        return (_floor, TimeSpan.FromMilliseconds(remaining));
    }

    private void Close()
    {
        if (_current is null) return;

        _actual.Add(new PhaseTime { Name = _current, Ms = _clock.ElapsedMilliseconds - _currentStart });
        _finished.Add(_current);
        _finishedAt = _clock.ElapsedMilliseconds;
        _current = null;
    }

    /// <summary>
    /// Restates the plan in this map's size. Called at every boundary because the heightmap decides
    /// the map size and it has not been read yet when the run begins — the first rescale that means
    /// anything is the one after the decode.
    /// </summary>
    private void Rescale()
    {
        double now = _megapixels();
        double factor = _learned.Megapixels > 0 && now > 0 ? now / _learned.Megapixels : 1;

        _predicted.Clear();
        foreach (var phase in _learned.Phases) _predicted[phase.Name] = phase.Ms * factor;

        _fallback = _predicted.Count > 0 ? _predicted.Values.Average() : 1000;
    }

    private double Predicted(string name)
        => _predicted.TryGetValue(name, out double ms) ? ms : _fallback;

    /// <summary>
    /// Province raster megapixels the shipped profiles below were measured at — vanilla's
    /// 9216x4608, which is <see cref="MapConfig.ReferenceProvinceWidth"/> squared off.
    /// </summary>
    private const double ReferenceMegapixels = 9216 * 4608 / 1_000_000.0;

    /// <summary>
    /// What a run costs before this machine has measured one of its own.
    ///
    /// Learning from the previous run is the whole design, but there is no previous run the first
    /// time — and writes are rare enough that "the first time" kept coming round. A user who has
    /// previewed a dozen times and is now writing a mod for the first time is exactly the person who
    /// most wants to know whether this takes one minute or ten, and a marquee tells them nothing.
    ///
    /// These are wall-clock milliseconds from one measured vanilla-size write — 18432x9216 in, a
    /// 176 s run, Debug build — scaled by megapixels like any learned profile. They are a starting
    /// point and nothing more: the first completed run of each kind replaces them wholesale, and the
    /// in-run speed correction covers a machine that is nothing like the one these came off.
    ///
    /// Worth keeping honest if the pipeline changes shape. The province partition alone is 54% of a
    /// run, so these numbers are mostly a statement about it; a phase that grows past it without
    /// these being remeasured would make the bar crawl and then jump.
    /// </summary>
    private static readonly (string Name, double Ms)[] GenerationPhases =
    [
        ("heightmap decode", 4829),
        ("province elevation", 454),
        ("coarse world summary", 154),
        ("land mask", 229),
        ("climate", 11432),
        ("drainage", 17085),
        ("province partition", 95070),
        ("terrain classification", 3329),
        ("title hierarchy", 3704),
    ];

    /// <summary>
    /// The phases only a mod write runs, in the order it runs them — about 39 s against the 136 s
    /// above, so a write is a preview and a bit rather than the different order of magnitude the
    /// old marquee left you assuming.
    /// </summary>
    private static readonly (string Name, double Ms)[] WritePhases =
    [
        ("map_data (heightmap, provinces, rivers)", 16606),
        ("blank vanilla data", 417),
        ("province terrain vote", 429),
        ("vanilla vocabulary", 220),
        ("development", 7),
        ("cultures", 623),
        ("faiths", 356),
        ("titles, history and localisation", 34),
        ("culture files", 227),
        ("compatibility", 38),
        ("religion files", 15),
        ("vanilla titulars", 42),
        ("locators", 5242),
        ("frontend", 5),
        ("terrain textures", 7974),
        ("map graphics", 126),
        ("terrain masks", 3859),
        ("trees", 2568),
        ("map table", 40),
        ("history and portraits", 37),
        ("static files", 2),
    ];

    /// <inheritdoc cref="GenerationPhases"/>
    public static RunProfile Shipped(bool writing) => new()
    {
        Megapixels = ReferenceMegapixels,
        Phases = (writing ? [.. GenerationPhases, .. WritePhases] : GenerationPhases)
            .Select(p => new PhaseTime { Name = p.Name, Ms = p.Ms })
            .ToList(),
    };

    /// <summary>
    /// The shipped profile with this machine's own numbers wherever it has them.
    ///
    /// A first write is not really uncalibrated: a preview runs nine of a write's thirty phases, and
    /// anyone about to write a mod has previewed it first. Those nine are taken from the
    /// measurements made here, and — this is the part that matters — the ratio between them and the
    /// shipped numbers for the same nine says how fast this machine is against the one the profile
    /// was measured on. The writing half is scaled by that.
    ///
    /// Without the scaling the blend was actively worse than not blending. Substituting measured
    /// values for the shared phases makes the plan match reality over exactly the stretch the in-run
    /// speed correction learns from, so it would observe speed 1.0 and leave the shipped writing
    /// half unscaled — a machine at half speed was told its first write would take 78 s when it
    /// takes 87. Applying the ratio up front predicts it to the second.
    /// </summary>
    public static RunProfile Blend(RunProfile prior, RunProfile measured)
    {
        if (measured.Phases.Count == 0 || measured.Megapixels <= 0 || prior.Megapixels <= 0)
            return prior;

        // Restate the measurements at the prior's map size, so one profile has one basis.
        double toPrior = prior.Megapixels / measured.Megapixels;
        var known = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var phase in measured.Phases) known[phase.Name] = phase.Ms * toPrior;

        // This machine against the reference one, over the phases both have run.
        double here = 0, reference = 0;
        foreach (var phase in prior.Phases)
        {
            if (!known.TryGetValue(phase.Name, out double ms)) continue;
            here += ms;
            reference += phase.Ms;
        }

        double speed = reference > 0 && here > 0 ? here / reference : 1.0;

        return new RunProfile
        {
            Megapixels = prior.Megapixels,
            Phases = prior.Phases
                .Select(p => new PhaseTime
                {
                    Name = p.Name,
                    Ms = known.TryGetValue(p.Name, out double ms) ? ms : p.Ms * speed,
                })
                .ToList(),
        };
    }

    /// <summary>"about 2m 10s left", and nothing so precise it invites being believed.</summary>
    public static string Describe(TimeSpan remaining)
    {
        int seconds = (int)Math.Ceiling(remaining.TotalSeconds);
        if (seconds <= 3) return "almost done";
        if (seconds < 60) return $"about {Math.Max(5, RoundTo(seconds, 5))}s left";

        int minutes = seconds / 60;
        int rest = RoundTo(seconds % 60, 15);
        if (rest == 60) { minutes++; rest = 0; }

        return rest == 0 ? $"about {minutes}m left" : $"about {minutes}m {rest}s left";
    }

    private static int RoundTo(int value, int step) => (value + step / 2) / step * step;
}
