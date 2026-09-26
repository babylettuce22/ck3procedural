using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen;

/// <summary>
/// The world's history, run on past the start date a year at a time.
///
/// Not a second simulation. <see cref="Formation"/> already grows the realms the start date opens
/// on, epoch by epoch, and this picks the same process up from where it left off — the same
/// polities, the same rules in <see cref="Formation.Step"/> — at a finer grain: each tick is one
/// year and every rate tuned per epoch is scaled to a year's share of it. A century here and four
/// epochs there are the same process, sampled differently.
///
/// Resumed from the titled start date, not from the raw end of the formation run: titling folds
/// vassals that had no tier left into their lords (see <see cref="Realms"/>), and it is the folded
/// realms that the mod on disk and the realm view show. Starting anywhere else would open with the
/// map changing under the player on the first tick for no reason in the story.
///
/// The start date's own polity objects are never touched — the written world, the bookmarks and
/// the editor all still read them — so every realm here is a copy, with the same id.
///
/// Deterministic: year Y draws from its own stream, so a history played straight through and one
/// paused, stepped and resumed arrive at the same map. Not thread-safe; the owner serialises.
/// </summary>
public sealed partial class HistorySim
{
    private readonly Formation.Sim _sim;
    private readonly int _seed;

    private readonly double _baseAggression, _baseTurbulence;
    private SimSettings _settings = SimSettings.Default;

    private HistorySim(Formation.Sim sim, int seed, int startYear)
    {
        _sim = sim;
        _seed = seed;
        _baseAggression = sim.Aggression;
        _baseTurbulence = sim.Turbulence;
        StartYear = startYear;
        StartCapitals = sim.Polities.ToDictionary(p => p.Id, p => p.Capital);
    }

    /// <summary>
    /// Where each realm standing at the start date was seated then, by id — the seat whose ruler's
    /// house a surviving realm still belongs to. See <see cref="AppliedHistory.Capture"/>.
    /// </summary>
    public IReadOnlyDictionary<int, Title> StartCapitals { get; }

    /// <summary>The year the history began: the world's start date.</summary>
    public int StartYear { get; }

    /// <summary>The year the map now stands at.</summary>
    public int Year => _sim.Year;

    /// <summary>Years per <see cref="Tick"/>: one, or an epoch's worth when comparing the two grains.</summary>
    public int TickYears => _sim.TickYears;

    /// <summary>
    /// Which of the realm rules are in force. Takes effect from the next <see cref="Tick"/>; the one
    /// under way is never changed part-way. See <see cref="RealmRules"/>.
    /// </summary>
    public RealmRules Rules
    {
        get => _settings.Rules;
        set => Settings = _settings with { Rules = value };
    }

    /// <summary>
    /// The rules and dials in force. Takes effect from the next <see cref="Tick"/>. The dials scale
    /// what the world was generated with — see <see cref="SimSettings"/> — so the defaults leave the
    /// history exactly as it runs without them.
    /// </summary>
    public SimSettings Settings
    {
        get => _settings;
        set
        {
            _settings = value;
            _sim.Rules = value.Rules;
            _sim.Aggression = _baseAggression * value.Aggression;
            _sim.Turbulence = _baseTurbulence * value.Turbulence;
        }
    }

    /// <summary>Everything that has happened since <see cref="StartYear"/>, oldest first.</summary>
    public IReadOnlyList<FormationEvent> Events => _sim.Events;

    /// <summary>Every realm holding ground, vassals included.</summary>
    public IEnumerable<Polity> Realms => _sim.Polities.Where(p => p.Alive);

    /// <summary>The realm holding a county, or null for ground outside the simulation — wilderness.</summary>
    public Polity? OwnerOf(Title county) => _sim.Owner.GetValueOrDefault(county);

    /// <summary>The id the next realm born will take. Carried by <see cref="AppliedHistory"/>.</summary>
    internal int NextId => _sim.NextId;

    /// <summary>
    /// Picks the formation up at the start date. Null when there is nothing to pick up: a world
    /// whose realms were handed out down the de jure tree, or read from an Azgaar export or a
    /// written mod, never ran the formation and has no rules to go on with.
    /// </summary>
    /// <param name="rulers">The start date's rulers and <paramref name="prehistory"/> their families,
    /// which the realms begin under — see <see cref="SeatStartRulers"/>. Without them every realm
    /// starts under a ruler and house drawn here.</param>
    /// <param name="wilds">The wilderness and every county's neighbours, wild ones included, for
    /// colonisation and ruination; without it neither ever happens. See <see cref="WildsGround"/>.</param>
    public static HistorySim? Resume(RealmMap realms, int startYear, int tickYears = 1,
        RulerMap? rulers = null, PrehistoryMap? prehistory = null, WildsGround? wilds = null)
    {
        if (realms.History is not { Rules: { } rules } start) return null;

        var (polities, owner) = Formation.CopyLiving(start.Polities);

        // Settling and abandoning land adds counties to the realm simulation's ground and takes them
        // away, so a history that can do either works on its own copies: the rules are the written
        // world's and are read again by the next Resume. Copies enumerate as the originals do.
        var sim = new Formation.Sim
        {
            Polities = polities,
            Owner = owner,
            Adjacent = wilds is null ? rules.Adjacent
                : rules.Adjacent.ToDictionary(kv => kv.Key, kv => new HashSet<Title>(kv.Value)),
            Development = rules.Development,
            CountyCulture = wilds is null ? rules.CountyCulture : new Dictionary<Title, Culture>(rules.CountyCulture),
            Events = [],
            AvgKingdom = rules.AvgKingdom,
            Reach = rules.Reach,
            Aggression = rules.Aggression,
            Turbulence = rules.Turbulence,
            TickYears = Math.Clamp(tickYears, 1, Formation.EpochYears),
            NextId = rules.NextId,
            Year = startYear,
        };

        var history = new HistorySim(sim, rules.Seed, startYear);
        history.SeatStartRulers(rulers, prehistory);
        history.SeatDeJure();
        history.SeatWilds(wilds);
        history.SeatWars();
        history.SeatIndependence();
        return history;
    }

    /// <summary>Advances the world by <see cref="TickYears"/>.</summary>
    public void Tick()
    {
        _sim.Year += _sim.TickYears;

        // Its own stream per year, as the formation has per epoch, and a different constant from
        // the formation's so the two never replay each other's dice.
        var rng = new Rng(_seed ^ 0x4157 ^ unchecked((int)((uint)_sim.Year * 0x9E3779B1u)));
        Formation.Step(_sim, rng);

        // The wars the year's conquests declared or fought, and the peaces that end them, before the
        // dead are cleared: a peace can take a realm's last county like any conquest.
        WarsYear();

        // What Formation.Run does once at the end, done every tick here because every tick is an
        // end someone may look at. A realm dies only by conquest, and Act hands its vassals on as
        // it dies, so this is a guard rather than a rule.
        foreach (var p in _sim.Polities)
            if (p.Alive && p.Suzerain is { Alive: false }) p.Suzerain = null;

        // The dead are dropped rather than kept. Every rule already skips them, so nothing plays
        // out differently; what changes is that a history centuries long does not scan every
        // realm that ever lived on every tick.
        _sim.Polities.RemoveAll(p => !p.Alive);

        // The people's year, after the realms': rulers for realms born this year, then deaths and
        // successions. On its own stream, so switching Succession changes no realm's dice.
        RulersYear();

        // Vassals breaking away, after the successions whose new and child rulers they press on,
        // and on a stream of their own. See HistoryIndependence.
        IndependenceYear();

        // The frontier's year: land settled and land abandoned, after the wars and successions and
        // on its own stream, so switching either changes nothing else's dice this year.
        WildsYear();

        // Last: drift reads who holds what once the year's conquests and partitions are done, and
        // changes no realm, so nothing after it could depend on it.
        DriftYear();
    }

    /// <summary>
    /// The map as it stands, on polity objects of its own — the same shape as the formation's
    /// bookmark snapshots, so whatever titles those can title this.
    /// </summary>
    public FormationHistory Snapshot() => Formation.Freeze(_sim, StartYear);

    /// <summary>
    /// Every broken invariant, or an empty list. The ones that matter are the ones CK3 or the
    /// titling step would reject: ground held twice or by nobody, a capital outside its realm, a
    /// chain of homage too deep to title or looping on itself, a realm in pieces.
    /// </summary>
    public List<string> Check()
    {
        var problems = new List<string>();
        var alive = _sim.Polities.Where(p => p.Alive).ToList();
        var ids = new HashSet<int>();
        int held = 0;

        foreach (var p in alive)
        {
            if (!ids.Add(p.Id)) problems.Add($"{p}: duplicate id");
            if (!p.Counties.Contains(p.Capital)) problems.Add($"{p}: capital {p.Capital.Name} outside the realm");
            foreach (var c in p.Counties)
            {
                held++;
                if (_sim.Owner.GetValueOrDefault(c) != p) problems.Add($"{p}: holds {c.Name} but the owner table disagrees");
            }

            if (p.Suzerain is { } s && !s.Alive) problems.Add($"{p}: answers to a dead realm {s}");

            int depth = 0;
            for (var q = p.Suzerain; q is not null; q = q.Suzerain)
                if (++depth > Polity.MaxDepth) { problems.Add($"{p}: homage deeper than {Polity.MaxDepth} (or a cycle)"); break; }

            if (!Contiguous(p)) problems.Add($"{p}: in pieces");
        }

        if (_sim.Owner.Count != held)
            problems.Add($"owner table has {_sim.Owner.Count} counties, realms hold {held}");
        foreach (var (c, p) in _sim.Owner)
            if (!p.Alive) problems.Add($"{c.Name} is owned by the dead realm {p}");

        CheckRulers(alive, problems);
        CheckDeJure(problems);
        CheckWilds(problems);
        CheckWars(problems);
        return problems;
    }

    private bool Contiguous(Polity p)
    {
        var seen = new HashSet<Title> { p.Capital };
        var queue = new Queue<Title>();
        queue.Enqueue(p.Capital);
        while (queue.Count > 0)
        {
            if (!_sim.Adjacent.TryGetValue(queue.Dequeue(), out var near)) continue;
            foreach (var n in near)
                if (p.Counties.Contains(n) && seen.Add(n)) queue.Enqueue(n);
        }
        return seen.Count == p.Counties.Count;
    }
}
