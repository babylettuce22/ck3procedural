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
    }
}
