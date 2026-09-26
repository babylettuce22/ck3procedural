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
public sealed record SimMemory(string What, int Year, Title Subject,
    Title? Actor = null, Culture? ActorCulture = null, Title? Counterpart = null, Culture? CounterpartCulture = null,
    string? Person = null, bool Female = false, string? Other = null, Title? Into = null,
    IReadOnlyList<Title>? Counties = null);

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
}
