using Ck3MapGen.MapGen;

namespace Ck3MapGen.AppGUI;

/// <summary>
/// A colour per ruler for the Realms view, drawn from where the ruler sits in the realm structure
/// rather than from the colour of the title they happen to be called after.
///
/// The distinction used not to matter. While realms were allocated down the de jure tree, the top
/// of every realm was an empire or a kingdom, and <see cref="Titles"/> spaces those a golden angle
/// apart — so painting a realm in its primary title's colour painted it in a hue nothing else on
/// the map was near. <see cref="MapConfig.SimulateFormation"/> ends that. A simulated realm is
/// named for whatever de jure title it covers most of, dropping a tier at a time down to its
/// capital's own county, so half of the independent realms on a generated map are titled by a
/// duchy or a county — and a title's colour is a shade of its parent's, sixteen degrees of hue for
/// a duchy and two for a county.
///
/// What that produced was neighbours painted the same: measured over seeds 4242, 991 and 7, every
/// bordering pair of independent realms shared a de jure ancestor and most were one nested inside
/// the other. Seed 991 put a kingdom, a duchy that had broken off it and a county that had broken
/// off *that* side by side, within 27 of each other in RGB — three sovereign realms reading as one
/// dark green blob. Nothing was wrong with the structure the view drew; the palette simply had no
/// way to say that two titles related on the de jure map are unrelated on the political one.
///
/// So the hue comes from the tree the view is actually about. Independent realms take a
/// golden-angle sequence of their own, ordered by seat index — stable, so a realm keeps its colour
/// when the map is redrawn — and each ruler's direct vassals take another such sequence offset from
/// their liege's hue, which is what makes a focused realm's vassal subtrees separate. Saturation
/// and lightness step alongside, so two hues that land close still differ.
///
/// The cost, and it is the point: a realm's colour no longer says which de jure title it is named
/// after. The De Jure map modes are where that question is asked and they are unchanged.
/// </summary>
public sealed class RealmPalette
{
    /// <summary>
    /// How far a vassal's own sequence starts from its liege's hue. Large enough that the first
    /// vassal is not mistaken for its liege's demesne, which is that hue lifted toward white.
    /// </summary>
    private const float VassalOffset = 47f;

    /// <summary>
    /// Where the sequence starts, chosen against the four colours this view paints that are not
    /// realms — wilderness, impassable, sea, and the boundary line.
    ///
    /// It matters more than a starting angle sounds like it should. Wilderness is a muted orange at
    /// roughly hue 36, and a sequence starting near there hands its *first* realm — so, on a map
    /// with any realms at all — a brown 24 apart from it in RGB, which beside unclaimed ground is
    /// the same confusion this palette exists to remove, one layer over. Swept across all 360
    /// starts, this one keeps the first thirty realms at least 56 from any of the four, and is also
    /// the best of them for separating realms from each other up to fifteen.
    /// </summary>
    private const float BaseHue = 228f;

    private readonly RealmGraph _graph;

    /// <summary>Hue and the position in the sibling run that shades it, per seat. Grown as asked.</summary>
    private readonly Dictionary<Title, (float Hue, int Step)> _tone = [];

    /// <summary>
    /// Seeds the independent realms — the only ones with no liege to take a hue from.
    ///
    /// Ordered by seat index rather than by realm size so that the colours survive an edit: sized
    /// order would hand every realm a new hue the moment one of them gained a county.
    /// </summary>
    public RealmPalette(RealmGraph graph, IEnumerable<Title> counties)
    {
        _graph = graph;

        var tops = counties.Select(c => graph.PathFromTop(graph.SeatOfCounty(c))[0])
                           .Distinct()
                           .OrderBy(seat => seat.Index)
                           .ToList();

        for (int i = 0; i < tops.Count; i++)
            _tone[tops[i]] = (Wrap(BaseHue + i * Titles.GoldenAngle), i);
    }

    /// <summary>
    /// The colour of the realm this ruler heads, whether or not they answer to anyone.
    ///
    /// Saturation and lightness step with the position in the run rather than being fixed, because
    /// the golden angle is only well spread <em>on average</em>: at Fibonacci strides it comes back
    /// close, so with fifteen realms the thirteenth is a dozen degrees off the first. Stepping on a
    /// cycle of three and a cycle of two means any two entries that near each other in hue differ
    /// in both of the others. Measured over the sequence, the closest pair of realms stays 51 apart
    /// in RGB out to fifteen realms and 37 out to thirty, against 30 to 35 with the two held
    /// constant.
    ///
    /// Past about forty independent realms it falls to fifteen and there is no fixing it at these
    /// saturations — a shattered world is more realms than a hue wheel holds, and it is the one
    /// shape of map this view cannot colour legibly.
    /// </summary>
    public (byte R, byte G, byte B) Colour(Title seat)
    {
        var (hue, step) = Tone(seat);
        return Titles.FromHsl(hue, 0.60f + step % 3 * 0.09f, 0.44f + step % 2 * 0.13f);
    }

    /// <summary>
    /// Walks down from the independent ruler rather than up from this seat, because
    /// <see cref="RealmGraph.PathFromTop"/> is where the cycle guard lives — a hue defined in terms
    /// of its liege's is a recursion, and generated liege data is not something to recurse over
    /// unbounded. Each step it passes is memoized, so a whole realm costs one walk.
    /// </summary>
    private (float Hue, int Step) Tone(Title seat)
    {
        if (_tone.TryGetValue(seat, out var known)) return known;

        var path = _graph.PathFromTop(seat);

        // A top the constructor never saw: a seat holding no county, so no county named it. Given a
        // hue from its own index, which is at least stable and spread the same way.
        if (!_tone.TryGetValue(path[0], out var tone))
            _tone[path[0]] = tone = (Wrap(BaseHue + path[0].Index * Titles.GoldenAngle), path[0].Index);

        for (int i = 1; i < path.Count; i++)
        {
            if (_tone.TryGetValue(path[i], out var cached)) { tone = cached; continue; }

            var siblings = _graph.VassalSeats(path[i - 1]);
            int at = 0;
            for (int k = 0; k < siblings.Count; k++)
            {
                if (siblings[k] == path[i]) { at = k; break; }
            }

            tone = (Wrap(tone.Hue + VassalOffset + at * Titles.GoldenAngle), at);
            _tone[path[i]] = tone;
        }

        return tone;
    }

    private static float Wrap(float hue) => (hue % 360f + 360f) % 360f;
}
