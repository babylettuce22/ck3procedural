using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen;

/// <summary>
/// Vassals that throw off their lieges: the History workspace's way for a bloc to lose members.
///
/// The formation has two such ways and neither reaches a great empire. Collapse and secession are
/// driven by <see cref="Formation.Cohesion"/>, which measures a realm's OWN counties — and an empire
/// built of vassals keeps a compact core, which partition carves smaller still, so its cohesion sits
/// at 1 and its instability at 0 for centuries. Wars then make belonging protective: every war is
/// overlord against overlord, so a member of a large bloc is defended by the whole of it. Measured,
/// a bloc under those rules only ever grew, from a third of the Inland Sea world to all of it.
///
/// Here each vassal carries a standing yearly chance of breaking away, the way CK3's independence
/// factions do, which rises with:
/// <list type="bullet">
/// <item>how far the whole bloc overreaches — its counties against twice a realm's reach;</item>
/// <item>how strong the vassal is against its liege's own land, so a one-county emperor over great
/// vassals is the most fragile thing on the map rather than the most secure;</item>
/// <item>a people who are not the overlord's;</item>
/// <item>a new ruler on the liege's throne, or a child — when CK3's factions press their demands.</item>
/// </list>
/// A realm that breaks free takes its own vassals with it, is held to a truce with its old overlord,
/// and will not swear to that overlord's bloc again for a generation.
///
/// On its own stream (<c>_seed ^ 0x1DE9</c>), and rolled for every vassal whether the rule is on or
/// not, so switching it moves no one else's dice; off, the history is the one it was before.
/// </summary>
public sealed partial class HistorySim
{
    /// <summary>The chance a year that a vassal of a bloc within its reach, of middling strength and the overlord's people, breaks away.</summary>
    private const double IndependenceBase = 0.003;

    /// <summary>How many realms' reach a whole bloc spans before it strains: an empire of vassals holds more than one realm's worth of ground.</summary>
    private const double BlocReaches = 2.0;

    /// <summary>How long a realm that broke free refuses to swear to its old overlord's bloc again: about a generation.</summary>
    private const int SovereignYears = 25;

    /// <summary>The chance a year is capped here: a vassal is never certain to walk out.</summary>
    private const double IndependenceCap = 0.25;

    /// <summary>Realms that broke free, by id: the overlord they broke from, and until when they will not swear to its bloc.</summary>
    private readonly Dictionary<int, (int FromRoot, int Until)> _sovereign = [];

    private void SeatIndependence() => _sim.Submits = Submits;

    /// <summary>
    /// Whether <paramref name="vassal"/> will swear to <paramref name="suzerain"/>: not while it is
    /// holding out against the bloc it broke from. Always yes with the rule off.
    /// </summary>
    private bool Submits(Polity vassal, Polity suzerain)
        => !_settings.Rules.HasFlag(RealmRules.Independence)
           || !_sovereign.TryGetValue(vassal.Id, out var held)
           || held.Until <= _sim.Year
           || suzerain.Root.Id != held.FromRoot;

    /// <summary>
    /// The chance this year that <paramref name="v"/> breaks away from its liege, or 0 for a realm
    /// that answers to no one. Read by the History workspace's hover, too.
    /// </summary>
    public double IndependenceChance(Polity v) => v.Suzerain is null ? 0 : IndependenceChance(v, BlocCounties());

    private double IndependenceChance(Polity v, Dictionary<Polity, int> blocCounties)
    {
        if (v.Suzerain is not { } liege) return 0;
        var root = v.Root;

        double strain = blocCounties.GetValueOrDefault(root) / (_sim.Reach * BlocReaches);
        double overreach = 1.0 + 3.0 * Math.Max(0.0, strain - 1.0);

        double power = Math.Pow(Math.Clamp(Land(v, withVassals: true) / Math.Max(1.0, Land(liege, withVassals: false)), 0.2, 5.0), 0.75);

        double foreign = v.Culture == root.Culture ? 1.0 : 1.5;

        double reign = 1.0;
        if (RulerOf(liege) is { } ruler)
        {
            if (_sim.Year - ruler.Crowned <= 2) reign = 3.0;
            else if (_sim.Year - ruler.Born < 16) reign = 2.0;
        }

        // The world's own turbulence leans on it as it leans on collapse: 1 at the default of 0.5.
        double turbulence = 0.5 + _sim.Turbulence;

        return Math.Min(IndependenceCap,
            IndependenceBase * overreach * power * foreign * reign * turbulence * _settings.Independence);
    }

    /// <summary>The year's breakaways, decided on the year's start and then applied, after the successions they feed on.</summary>
    private void IndependenceYear()
    {
        int year = _sim.Year;
        var rng = new Rng(_seed ^ 0x1DE9 ^ unchecked((int)((uint)year * 0x9E3779B1u)));

        foreach (int id in _sovereign.Where(kv => kv.Value.Until <= year).Select(kv => kv.Key).ToList())
            _sovereign.Remove(id);

        bool on = _settings.Rules.HasFlag(RealmRules.Independence);
        var blocs = BlocCounties();

        // Everyone's chance is read off the map as the year found it, so the order the rolls are made
        // in cannot decide whose breakaway made whose easier.
        var breaking = new List<(Polity Vassal, Polity Liege, SimRuler? Ruler)>();
        foreach (var v in _sim.Polities.Where(p => p.Alive && p.Suzerain is not null)
                                       .OrderBy(p => p.Capital.Index).ToList())
        {
            if (rng.Chance(IndependenceChance(v, blocs)) && on)
                breaking.Add((v, v.Suzerain!, RulerOf(v.Suzerain!)));
        }

        foreach (var (v, liege, ruler) in breaking)
        {
            if (!v.Alive || v.Suzerain != liege) continue;

            var root = liege.Root;
            v.Suzerain = null;
            v.Founded = year;
            _sovereign[v.Id] = (root.Id, year + SovereignYears);
            _truces[Pair(v, root)] = year + TruceYears;

            string why = ruler is null ? ""
                : year - ruler.Crowned <= 2 ? $", as {ruler.Name} came to the throne"
                : year - ruler.Born < 16 ? $", under the child {ruler.Name}"
                : "";
            _sim.Log(FormationKind.Freed, v.Capital, v, liege, v.Culture == root.Culture ? 1 : 2,
                $"{v.Capital.Name} threw off the rule of {liege.Capital.Name}{why}");
        }
    }

    /// <summary>How many counties each bloc holds, by the realm at its head.</summary>
    private Dictionary<Polity, int> BlocCounties()
    {
        var blocs = new Dictionary<Polity, int>();
        foreach (var p in _sim.Polities)
            if (p.Alive) blocs[p.Root] = blocs.GetValueOrDefault(p.Root) + p.Counties.Count;
        return blocs;
    }

    /// <summary>A realm's land as <see cref="Formation.Strength"/> weighs it, before cohesion: one plus development a county, its vassals' in full when asked.</summary>
    private double Land(Polity p, bool withVassals)
    {
        double land = 0;
        foreach (var c in p.Counties) land += _sim.Development.GetValueOrDefault(c) + 1;
        if (withVassals)
            foreach (var v in _sim.Polities)
                if (v.Alive && v.Suzerain == p) land += Land(v, withVassals: true);
        return land;
    }
}
