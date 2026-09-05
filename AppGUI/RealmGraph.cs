using Ck3MapGen.Core;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.AppGUI;

/// <summary>
/// The de facto structure of the written world, navigable: who holds what, who answers to whom,
/// and which counties a realm actually contains.
///
/// <see cref="RealmMap"/> stores this as two raw dictionaries keyed the way the history writer
/// needed them — titles to holder seats, primary titles to liege titles — and every consumer used
/// to re-derive the same walks from them independently (see the TopLiege saga in PreviewRenderer).
/// This class does the derivation once. Rulers are identified by their <b>seat county</b>
/// throughout, because that is the one title every generated character reliably holds; a ruler's
/// primary title is a lookup away.
/// </summary>
public sealed class RealmGraph
{
    private readonly Dictionary<Title, Title> _holderCounty;
    private readonly Dictionary<Title, Title> _primaryOf;   // seat → highest-ranked title held
    private readonly Dictionary<Title, Title> _liegeSeat;   // seat → the liege's seat
    private readonly Dictionary<Title, List<Title>> _vassalSeats;
    private readonly Dictionary<Title, List<Title>> _demesne;   // seat → counties held directly
    private readonly Dictionary<Title, Title> _seatOfCounty;
    private readonly Dictionary<Title, int> _realmSize = [];

    public static RealmGraph? Build(Emit.WrittenContent? written, GenerationResult result)
        => written?.Realms is { } realms
            ? new RealmGraph(realms, Titles.Flatten(result.Titles).Where(t => t.Tier == "c"))
            : null;

    private RealmGraph(RealmMap realms, IEnumerable<Title> counties)
    {
        _holderCounty = realms.HolderCounty;
        _primaryOf = [];
        foreach (var (title, seat) in realms.HolderCounty)
        {
            if (!_primaryOf.TryGetValue(seat, out var best)
                || Emit.HistoryWriter.Rank(title) > Emit.HistoryWriter.Rank(best))
                _primaryOf[seat] = title;
        }

        // One liege step per ruler, changing hands the way TopLiege does: the liege *title* is
        // frequently nobody's primary, so it has to be resolved to the character holding it — the
        // seat — before the next step means anything.
        _liegeSeat = [];
        _vassalSeats = [];
        foreach (var seat in _primaryOf.Keys)
        {
            var primary = Primary(seat);
            if (!realms.Liege.TryGetValue(primary, out var lord)) continue;
            if (!realms.HolderCounty.TryGetValue(lord, out var lordSeat) || lordSeat == seat) continue;

            _liegeSeat[seat] = lordSeat;
            (_vassalSeats.TryGetValue(lordSeat, out var list)
                ? list
                : _vassalSeats[lordSeat] = []).Add(seat);
        }

        _demesne = [];
        _seatOfCounty = [];
        foreach (var county in counties)
        {
            var seat = realms.HolderCounty.GetValueOrDefault(county, county);
            _seatOfCounty[county] = seat;
            (_demesne.TryGetValue(seat, out var held)
                ? held
                : _demesne[seat] = []).Add(county);
        }

        foreach (var list in _vassalSeats.Values)
        {
            list.Sort((a, b) =>
            {
                int rank = Emit.HistoryWriter.Rank(Primary(b)).CompareTo(
                           Emit.HistoryWriter.Rank(Primary(a)));
                return rank != 0 ? rank : RealmSize(b).CompareTo(RealmSize(a));
            });
        }
    }

    /// <summary>The highest-ranked title this seat's ruler holds — the county itself for a count.</summary>
    public Title Primary(Title seat) => _primaryOf.GetValueOrDefault(seat, seat);

    /// <summary>The seat of the ruler this one answers to, or null for an independent ruler.</summary>
    public Title? LiegeSeat(Title seat) => _liegeSeat.GetValueOrDefault(seat);

    /// <summary>The seat of the ruler directly holding this county.</summary>
    public Title SeatOfCounty(Title county) => _seatOfCounty.GetValueOrDefault(county, county);

    /// <summary>
    /// The seat of whoever holds this title, of any tier — null for a title nobody holds, which is
    /// what a de-jure-only duchy or kingdom is. A county falls back to itself: a county absent from
    /// the holder table is held by its own count, seated there.
    /// </summary>
    public Title? SeatOf(Title title)
        => _holderCounty.TryGetValue(title, out var seat) ? seat
            : title.Tier == "c" ? title
            : null;

    public IReadOnlyList<Title> VassalSeats(Title seat)
        => _vassalSeats.GetValueOrDefault(seat) ?? (IReadOnlyList<Title>)[];

    public IReadOnlyList<Title> Demesne(Title seat)
        => _demesne.GetValueOrDefault(seat) ?? (IReadOnlyList<Title>)[];

    /// <summary>
    /// The chain of seats from the independent ruler at the top down to this one, both inclusive.
    /// Bounded by a visited set for the same reason TopLiege is: this runs against generated data,
    /// and a render or a click is not the place to discover a cycle by hanging.
    /// </summary>
    public IReadOnlyList<Title> PathFromTop(Title seat)
    {
        var path = new List<Title> { seat };
        var visited = new HashSet<Title> { seat };

        while (LiegeSeat(path[^1]) is { } above && visited.Add(above)) path.Add(above);

        path.Reverse();
        return path;
    }

    /// <summary>
    /// Every county in this ruler's realm — their demesne and their vassals', all the way down.
    ///
    /// The unit a government is decided in: <see cref="MapGen.Governments.Build"/> assigns one per
    /// independent top liege and lays it over everything inside, so this is the same span an
    /// override has to cover to leave a realm that looks generated rather than half-converted.
    /// Bounded by a visited set for the reason <see cref="RealmSize"/> is.
    /// </summary>
    public IReadOnlyList<Title> RealmCounties(Title seat)
    {
        var counties = new List<Title>();
        var visited = new HashSet<Title> { seat };
        var pending = new Queue<Title>();
        pending.Enqueue(seat);

        while (pending.Count > 0)
        {
            var next = pending.Dequeue();
            counties.AddRange(Demesne(next));

            foreach (var vassal in VassalSeats(next))
                if (visited.Add(vassal)) pending.Enqueue(vassal);
        }

        return counties;
    }

    /// <summary>Counties in this ruler's realm: demesne plus everything their vassals hold.</summary>
    public int RealmSize(Title seat)
    {
        if (_realmSize.TryGetValue(seat, out int known)) return known;

        // Marked before the descent so the memo doubles as a visited set: generated liege data
        // could in principle loop, and a plain recursive sum would follow it forever.
        _realmSize[seat] = 0;
        int total = Demesne(seat).Count;
        foreach (var vassal in VassalSeats(seat)) total += RealmSize(vassal);

        return _realmSize[seat] = total;
    }
}
