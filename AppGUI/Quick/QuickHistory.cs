using Ck3MapGen.Core;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.AppGUI;

/// <summary>
/// One line of the Quick page's chronicle: the year, what happened, and the colour of the realm
/// it happened to as the map shows it, for a swatch.
/// </summary>
internal readonly record struct ChronicleLine(int Year, string Text, (byte R, byte G, byte B)? Colour);

/// <summary>
/// The written world's history, run on from its start date for the Quick page's last step: the
/// same simulation as the History workspace (<see cref="HistorySim"/>), with none of its controls.
/// Every rule and dial stays at the world's own, and the chronicle keeps only the headlines.
///
/// It holds the model and draws frames; the main window owns the clock and the page shows what
/// this draws. Nothing here touches the written world: the simulation copies the start date's
/// realms, and <see cref="Capture"/> is what the main window lays onto the mod when the player
/// accepts the world as it stands.
///
/// A world written with a history already applied is run on from that history, which
/// <see cref="Capture"/> then carries forward — the History workspace's chaining, so accepting,
/// continuing and accepting again reads as one unbroken history.
/// </summary>
internal sealed class QuickHistory
{
    private readonly HistorySim _sim;
    private readonly CountyCanvas _canvas;
    private readonly RulerMap? _rulers;
    private readonly PrehistoryMap? _prehistory;
    private readonly AppliedHistory? _earlier;

    /// <summary>Colour per realm id, handed out once and kept for the realm's whole life.</summary>
    private readonly Dictionary<int, (byte R, byte G, byte B)> _colourOf = [];
    private int _nextColour;
    private int _shownEvents;

    private QuickHistory(HistorySim sim, CountyCanvas canvas, RealmMap realms, RulerMap? rulers, PrehistoryMap? prehistory,
        AppliedHistory? earlier, IReadOnlyDictionary<Title, (byte R, byte G, byte B)>? keptColours)
    {
        _sim = sim;
        _canvas = canvas;
        _rulers = rulers;
        _prehistory = prehistory;
        _earlier = earlier;
        Began = earlier?.ChronicleSince ?? sim.StartYear;

        // Year zero wears the realm colours the done screen and the World workspace use, asked of
        // their palette (see HistoryPanel.Reset for why it is asked rather than imitated).
        var counties = canvas.Counties;
        var graph = RealmGraph.From(realms, counties, keptColours);
        var palette = new RealmPalette(graph, counties);
        foreach (var root in sim.Realms.Where(p => p.Suzerain is null))
            _colourOf[root.Id] = palette.Colour(graph.PathFromTop(graph.SeatOfCounty(root.Capital))[0]);
        _nextColour = counties.Select(c => graph.PathFromTop(graph.SeatOfCounty(c))[0]).Distinct().Count();

        // Whatever the start date already logged is the past, not news.
        _shownEvents = sim.Events.Count;
    }

    /// <summary>
    /// Readies the history of a written world, off the UI thread: the county canvas walks the
    /// whole province raster once. Null when the world has no grown realms to run on — an
    /// Azgaar export's, a shattered world's, or one written without history.
    /// </summary>
    /// <param name="applied">The history the world was written with, if any; it is run on from.</param>
    public static async Task<QuickHistory?> PrepareAsync(GenerationResult result, Emit.WrittenContent written, AppliedHistory? applied)
    {
        if (written.Realms is not { History.Rules: not null } realms) return null;

        var raster = PreviewRenderer.ProvinceRaster.From(result, written.Wilderness);
        var wilderness = written.Wilderness;
        var generated = written.World?.Wilderness ?? wilderness;
        var rulers = written.Rulers;
        var prehistory = written.Prehistory;
        var kept = written.World?.RealmColours;
        int startYear = result.Config.StartYear;

        return await Task.Run(() =>
        {
            var canvas = new CountyCanvas(raster);

            // The frontier's neighbours, wild counties included, for a world with land to settle.
            var wilds = wilderness is { Count: > 0 } && generated is not null
                ? new WildsGround(wilderness, generated, Realms.BuildCountyAdjacency(
                    [.. Titles.Flatten(result.Titles).Where(t => t.Tier == "c")], result.Provinces, result.BaronyCount,
                    result.ProvinceOrder, (int)Math.Round(result.Config.Scaled(result.Config.SeaBridgePixelsAtVanilla))))
                : null;

            var sim = HistorySim.Resume(realms, startYear, rulers: rulers, prehistory: prehistory, wilds: wilds, earlier: applied);
            return sim is null ? null : new QuickHistory(sim, canvas, realms, rulers, prehistory, applied, kept);
        });
    }

    /// <summary>The year the world stands in now.</summary>
    public int Year => _sim.Year;

    /// <summary>The year this history started from: the generated start, however many times it was accepted since.</summary>
    public int Began { get; }

    /// <summary>Whether anything has happened since the world was last written.</summary>
    public bool Moved => _sim.Year > _sim.StartYear;

    /// <summary>One year on.</summary>
    public void Tick()
    {
        _sim.Tick();
#if DEBUG
        // The one place a broken rule would otherwise go unseen until the mod failed to load.
        var problems = _sim.Check();
        if (problems.Count > 0)
        {
            Console.WriteLine($"quick history: invariant broken in {_sim.Year}:");
            foreach (var problem in problems.Take(10)) Console.WriteLine($"  {problem}");
        }
#endif
    }

    /// <summary>The map as it stands: every county in its independent realm's colour.</summary>
    public Bitmap Render()
    {
        var colours = new (byte R, byte G, byte B)?[_canvas.Counties.Count];
        for (int c = 0; c < colours.Length; c++)
            if (_sim.OwnerOf(_canvas.Counties[c]) is { } owner) colours[c] = ColourOf(owner.Root);
        return PreviewRenderer.ToBitmap(_canvas.Render(colours));
    }

    /// <summary>
    /// How the world stands, in a line: how many realms, and the greatest of them.
    /// </summary>
    public string Standing()
    {
        var realms = _sim.Realms.ToList();
        var vassalsOf = realms.Where(p => p.Suzerain is not null).ToLookup(p => p.Suzerain!);
        int Size(Polity p) => p.Counties.Count + vassalsOf[p].Sum(Size);
        var roots = realms.Where(p => p.Suzerain is null).ToList();
        var largest = roots.OrderByDescending(Size).ThenBy(p => p.Capital.Index).FirstOrDefault();

        return $"{roots.Count} independent realms"
               + (largest is null ? "" : $" · the greatest, {largest.Capital.Name}, holds {Size(largest)} counties");
    }

    /// <summary>
    /// The headlines since the last call, oldest first. The History workspace lists every war
    /// declared and every heir; this keeps what changes the map or the dynasties — a war won,
    /// a realm divided, a throne seized, a realm sworn, freed or fallen, a title drifting, a county
    /// lost to ruin — so the column can be read while the years pass.
    /// </summary>
    public List<ChronicleLine> TakeHeadlines()
    {
        var lines = new List<ChronicleLine>();
        var events = _sim.Events;
        for (; _shownEvents < events.Count; _shownEvents++)
        {
            var e = events[_shownEvents];
            if (Headline(e) is not { } text) continue;

            // The realm the line is about, as it now stands, for its swatch.
            // A fallen house has no realm left to colour it by; its old seat is someone else's now.
            var about = e.Actor ?? e.Subject;
            bool fallen = e.Kind == FormationKind.Standing && e.Tension == 0;
            var colour = !fallen && _sim.OwnerOf(about) is { } owner ? ColourOf(owner.Root) : ((byte, byte, byte)?)null;
            lines.Add(new ChronicleLine(e.Year, text, colour));
        }
        return lines;
    }

    private static string? Headline(FormationEvent e)
    {
        string subject = e.Subject.Name;
        string actor = e.Actor?.Name ?? "a realm";
        string other = e.Counterpart?.Name ?? "its lord";

        return e.Kind switch
        {
            FormationKind.Vassalized => $"{subject} swore fealty to {other}",
            FormationKind.Freed => $"{subject} broke free of {other}",
            FormationKind.Fragmented => $"{subject} broke away from {other}",
            FormationKind.Collapsed => $"The realm of {subject} came apart; its vassals went their own ways",
            FormationKind.Absorbed => $"The realm of {actor} fell to {other}",
            // Only wars someone won: one abandoned, ended in a white peace or won for no land is
            // left out.
            FormationKind.WarEnded => e.Note is { } note && note.Contains(" won ") && !note.Contains("none of it") ? note : null,
            FormationKind.Partitioned or FormationKind.Usurped or FormationKind.Drifted or FormationKind.Ruined => e.Note,
            // A feud begun is a headline; one cooling is detail.
            // Houses: a rivalry or a feud begun (not a feud cooling, nor a quarrel), a house become
            // the greatest, one of the greatest fallen.
            FormationKind.Feud => e.Tension > 0 ? e.Note : null,
            FormationKind.Standing => e.Note,
            _ => null,
        };
    }

    /// <summary>
    /// The realms as they stand, to be made the world's start. Carries on from the history the
    /// world was written with, if any.
    /// </summary>
    public AppliedHistory Capture()
        => AppliedHistory.Capture(_sim, _canvas.Counties, _rulers, _prehistory, _colourOf, _earlier);

    private (byte R, byte G, byte B) ColourOf(Polity root)
    {
        if (_colourOf.TryGetValue(root.Id, out var known)) return known;

        // RealmPalette's sequence carried on past the start date's, as the History workspace does.
        int i = _nextColour++;
        float hue = ((228f + i * Titles.GoldenAngle) % 360f + 360f) % 360f;
        var colour = Titles.FromHsl(hue, 0.60f + i % 3 * 0.09f, 0.44f + i % 2 * 0.13f);
        _colourOf[root.Id] = colour;
        return colour;
    }
}
