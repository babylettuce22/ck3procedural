namespace Ck3MapGen.MapGen;

/// <summary>
/// One thing the History workspace's simulation saw happen that a title's chronicle should
/// remember — structured, not prose: who, what, where and when. The words are written when the
/// history is applied (<see cref="HistoryChronicle"/>), from the names the world has then.
/// </summary>
/// <param name="What">won, held, divided, seized, drifted, settled, ruined — and, read off the event
/// log at capture, swore, freed, collapsed.</param>
/// <param name="Subject">The title it is remembered about.</param>
/// <param name="Actor">The seat of the realm that did it, and its people then.</param>
/// <param name="Counterpart">The seat of the realm it was done to, where there was one.</param>
/// <param name="Person">Who: the heir who took a share, the usurper and his house.</param>
/// <param name="Other">Whose death it followed.</param>
/// <param name="Into">For a drift: the kingdom or empire the title passed into.</param>
/// <param name="Counties">The counties that changed hands, where it was not one whole title.</param>
/// <param name="Scale">How big the powers in it were — see <see cref="FormationEvent.Scale"/>.</param>
public sealed record SimMemory(string What, int Year, Title Subject,
    Title? Actor = null, Culture? ActorCulture = null, Title? Counterpart = null, Culture? CounterpartCulture = null,
    string? Person = null, bool Female = false, string? Other = null, Title? Into = null,
    IReadOnlyList<Title>? Counties = null, int Scale = 0);

/// <summary>
/// The simulation's memory: what the chronicle of an applied history is written from. Recorded
/// where each thing happens, beside the event log the History workspace shows, because the log
/// is prose and the chronicle must not be written by parsing prose back into facts.
/// </summary>
public sealed partial class HistorySim
{
    private readonly List<SimMemory> _memory = [];

    /// <summary>The peak size of each realm that died this year, by its last seat; emptied by <see cref="MarkHighlights"/>.</summary>
    private readonly Dictionary<Title, int> _fallenPeaks = [];

    /// <summary>Everything worth a line of history, oldest first. See <see cref="AppliedHistory.Chronicle"/>.</summary>
    public IReadOnlyList<SimMemory> Memory => _memory;

    private void Remember(string what, Title subject, Polity? actor = null, Polity? counterpart = null,
        string? person = null, bool female = false, string? other = null, Title? into = null,
        IReadOnlyList<Title>? counties = null)
        => _memory.Add(new SimMemory(what, _sim.Year, subject, actor?.Capital, actor?.Culture,
            counterpart?.Capital, counterpart?.Culture, person, female, other, into, counties));

    /// <summary>Every county's bloc size, by the county: what <see cref="MeasureYear"/> compares against.</summary>
    private Dictionary<Title, int> BlocsByCounty()
    {
        var blocs = new Dictionary<Polity, int>();
        foreach (var p in _sim.Polities)
            if (p.Alive) blocs[p.Root] = blocs.GetValueOrDefault(p.Root) + p.Counties.Count;

        var byCounty = new Dictionary<Title, int>();
        foreach (var p in _sim.Polities)
            if (p.Alive)
                foreach (var c in p.Counties) byCounty[c] = blocs[p.Root];
        return byCounty;
    }

    /// <summary>
    /// Sizes the year's events and memories: the larger bloc holding any of the places each names,
    /// at the start of the year or its end. Both, because a realm that came apart or fell was large
    /// only before, and one that broke away exists only after. Read-only: it rolls nothing.
    /// </summary>
    private void MeasureYear(int events, int memories, Dictionary<Title, int> before)
    {
        var after = BlocsByCounty();
        int Of(Title? t) => t is null ? 0 : Math.Max(before.GetValueOrDefault(t), after.GetValueOrDefault(t));

        for (int i = events; i < _sim.Events.Count; i++)
        {
            var e = _sim.Events[i];
            e.Scale = Math.Max(Of(e.Subject), Math.Max(Of(e.Actor), Of(e.Counterpart)));
        }
        for (int i = memories; i < _memory.Count; i++)
        {
            var m = _memory[i];
            _memory[i] = m with { Scale = Math.Max(Of(m.Subject), Math.Max(Of(m.Actor), Of(m.Counterpart))) };
        }

        MarkHighlights(events, memories, before, after);
    }

    /// <summary>
    /// Marks the year's biggest news (<see cref="FormationEvent.Highlight"/>), by how much changed
    /// rather than by <see cref="FormationEvent.Scale"/>: an empire taking one county is not news
    /// because the empire is large. Sizes are shares of the world's held counties, so a small map
    /// and a large one read alike: sizable a 25th of them, great an 8th, vast a quarter.
    /// Read-only, like <see cref="MeasureYear"/>.
    /// </summary>
    private void MarkHighlights(int events, int memories, Dictionary<Title, int> before, Dictionary<Title, int> after)
    {
        var fallen = new Dictionary<Title, int>(_fallenPeaks);
        _fallenPeaks.Clear();

        int world = Math.Max(1, _sim.Owner.Count);
        int sizable = Math.Max(5, world / 25), great = Math.Max(12, world / 8), vast = Math.Max(24, world / 4);
        int Before(Title? t) => t is null ? 0 : before.GetValueOrDefault(t);
        int After(Title? t) => t is null ? 0 : after.GetValueOrDefault(t);

        // A war's land, as the memory of it counts it: the peace's note is prose.
        var taken = new Dictionary<Title, int>();
        for (int i = memories; i < _memory.Count; i++)
            if (_memory[i] is { What: "won", Actor: { } attacker, Counties: { } counties })
                taken[attacker] = taken.GetValueOrDefault(attacker) + counties.Count;

        for (int i = events; i < _sim.Events.Count; i++)
        {
            var e = _sim.Events[i];
            e.Highlight = e.Kind switch
            {
                // A realm of some size going its own way, or bending the knee, with its vassals.
                FormationKind.Freed => After(e.Subject) >= sizable,
                FormationKind.Fragmented => e.Tension >= 2 && After(e.Subject) >= sizable,
                FormationKind.Vassalized => Before(e.Subject) >= sizable,
                FormationKind.Collapsed => Before(e.Subject) >= sizable,
                // The end of a realm that once mattered, however little was left of it at the last.
                FormationKind.Absorbed => e.Actor is { } seat && fallen.GetValueOrDefault(seat) >= sizable,
                // The attacker's peace and a good share of land with it; a defence held is not news.
                FormationKind.WarEnded => e.Actor is { } a && taken.GetValueOrDefault(a) >= (sizable + 1) / 2,
                FormationKind.Usurped => Before(e.Subject) >= great,
                // Partition is how most realms pass on; only the breaking of a giant is news.
                FormationKind.Partitioned => Before(e.Subject) >= vast,
                FormationKind.Feud => e.Tension >= 3,
                FormationKind.Standing => true,
                // A kingdom changing empire (logged at its capital duchy); a duchy changing kingdom is not.
                FormationKind.Drifted => e.Subject.Tier == "d",
                _ => false,
            };
        }

        // A war that ended a realm is told once, as the fall: the peace logs no defender when the
        // defender is gone, so it is a fall the same year to the winner's bloc — often to the
        // vassal the war was fought for, which took the last county.
        Polity? Bloc(Title? t) => t is null ? null : _sim.Owner.GetValueOrDefault(t)?.Root;
        for (int i = events; i < _sim.Events.Count; i++)
        {
            var war = _sim.Events[i];
            if (war is { Kind: FormationKind.WarEnded, Highlight: true, Counterpart: null } && Bloc(war.Actor) is { } winner
                && _sim.Events.Skip(events).Any(f => f is { Kind: FormationKind.Absorbed, Highlight: true }
                                                     && Bloc(f.Counterpart) == winner))
                war.Highlight = false;
        }
    }
}
