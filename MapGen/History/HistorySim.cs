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

    private readonly List<AppliedHistory.Frame> _frames = [];

    /// <summary>
    /// The map at the start and every <see cref="AppliedHistory.FrameYears"/> years since, for an
    /// additional bookmark dated inside the history. Read-only to the simulation: taken after a
    /// tick, it changes no dice.
    /// </summary>
    public IReadOnlyList<AppliedHistory.Frame> Frames => _frames;

    private void KeepFrame()
        => _frames.Add(new AppliedHistory.Frame(_sim.Year,
            [.. _sim.Polities.Where(p => p.Alive).OrderBy(p => p.Id).Select(AppliedHistory.FrameRealm)]));

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
    /// <param name="earlier">The history the written world was applied from, if any: its houses'
    /// standing carries on rather than being read afresh off the map. See <see cref="SeatStanding"/>.</param>
    /// <param name="peoples">Every county's faith and the world's generated peoples and faiths, for
    /// assimilation and conversion; without it neither ever happens. See <see cref="PeopleGround"/>.</param>
    public static HistorySim? Resume(RealmMap realms, int startYear, int tickYears = 1,
        RulerMap? rulers = null, PrehistoryMap? prehistory = null, WildsGround? wilds = null,
        AppliedHistory? earlier = null, PeopleGround? peoples = null)
    {
        if (realms.History is not { Rules: { } rules } start) return null;

        var (polities, owner) = Formation.CopyLiving(start.Polities);

        // Settling and abandoning land adds counties to the realm simulation's ground and takes them
        // away, and assimilation changes their people, so a history that can do any of it works on
        // its own copies: the rules are the written world's and are read again by the next Resume.
        // Copies enumerate as the originals do.
        var sim = new Formation.Sim
        {
            Polities = polities,
            Owner = owner,
            Adjacent = wilds is null ? rules.Adjacent
                : rules.Adjacent.ToDictionary(kv => kv.Key, kv => new HashSet<Title>(kv.Value)),
            Development = rules.Development,
            CountyCulture = wilds is null && peoples is null ? rules.CountyCulture : new Dictionary<Title, Culture>(rules.CountyCulture),
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
        sim.Fragility = history.Fragility;
        history.SeatStartRulers(rulers, prehistory, earlier);
        history.SeatGrudges(prehistory);
        history.SeatStanding(earlier);
        history.SeatExclaves();
        history.SeatDeJure(earlier);
        history.SeatWilds(wilds);
        history.SeatPeoples(peoples, earlier);
        history.SeatAlliances(prehistory, earlier);
        history.SeatWars(prehistory, earlier);
        history.SeatIndependence(earlier);
        history.KeepFrame();
        return history;
    }

    /// <summary>Advances the world by <see cref="TickYears"/>.</summary>
    public void Tick()
    {
        _sim.Year += _sim.TickYears;

        // Its own stream per year, as the formation has per epoch, and a different constant from
        // the formation's so the two never replay each other's dice.
        var rng = new Rng(_seed ^ 0x4157 ^ unchecked((int)((uint)_sim.Year * 0x9E3779B1u)));
        int logged = _sim.Events.Count;
        int remembered = _memory.Count;
        var blocs = BlocsByCounty();
        PruneDiplomacy();
        Formation.Step(_sim, rng);

        // The step's conquests and walkouts, as grudges, while its realms are as it left them.
        GrieveStep(logged);

        // The wars the year's conquests declared or fought, and the peaces that end them, before the
        // dead are cleared: a peace can take a realm's last county like any conquest.
        WarsYear();

        // What Formation.Run does once at the end, done every tick here because every tick is an
        // end someone may look at. A realm dies only by conquest, and Act hands its vassals on as
        // it dies, so this is a guard rather than a rule.
        foreach (var p in _sim.Polities)
            if (p.Alive && p.Suzerain is { Alive: false }) p.Suzerain = null;

        // How big each realm that fell had been at its height, for the year's highlights (MarkHighlights).
        foreach (var p in _sim.Polities)
            if (!p.Alive) _fallenPeaks[p.Capital] = Math.Max(_fallenPeaks.GetValueOrDefault(p.Capital), p.Peak);

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

        // The peoples' year: counties taking their lords' culture and faith, once the year has
        // settled who their lords are, each on a stream of its own. What it changes, the realm
        // simulation reads next year through cohesion. See HistoryPeoples.
        PeoplesYear();

        // Renew political agreements after succession and changes of independence. New agreements
        // affect the following year's wars, never a battle that has already been fought.
        AlliancesYear();
        PruneDiplomacy();

        // Last: drift reads who holds what once the year's conquests and partitions are done, and
        // changes no realm, so nothing after it could depend on it.
        DriftYear();

        // The houses' year's end, likewise read-only: grudges and standing worn down a year, and
        // the year's holdings added to standing. See HistoryHouses.
        HousesYear();

        // Read-only: how big the powers were in everything the year logged and remembered, so the
        // chronicle can pick the world's headlines out of it. See HistoryMemory.
        MeasureYear(logged, remembered, blocs);

        // Read-only too: the map, whenever the tick crossed into a new stretch of the timeline.
        if ((_sim.Year - StartYear) / AppliedHistory.FrameYears
            != (_sim.Year - _sim.TickYears - StartYear) / AppliedHistory.FrameYears)
            KeepFrame();
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
        CheckHouses(problems);
        CheckPeoples(problems);
        CheckAlliances(problems);
        return problems;
    }

    private bool Contiguous(Polity p) => Whole(p);

    /// <summary>
    /// Whether a realm is in one piece around its capital without <paramref name="without"/> — its
    /// start-date exclaves apart, which it may keep (see <see cref="Formation.Sim.Exclaves"/>). The
    /// one test behind the invariant, a partition's shares and a county's fall to ruin.
    /// </summary>
    private bool Whole(Polity p, IReadOnlySet<Title>? without = null)
    {
        if (without?.Contains(p.Capital) == true) return false;
        var seen = new HashSet<Title> { p.Capital };
        var queue = new Queue<Title>([p.Capital]);
        while (queue.Count > 0)
        {
            if (!_sim.Adjacent.TryGetValue(queue.Dequeue(), out var near)) continue;
            foreach (var n in near)
                if (p.Counties.Contains(n) && without?.Contains(n) != true && seen.Add(n)) queue.Enqueue(n);
        }

        // Whatever the capital cannot reach must lie in pieces that each hold one of the realm's
        // start-date exclaves — an exclave may grow, as ShedIslands allows.
        var apart = p.Counties.Where(c => !seen.Contains(c) && without?.Contains(c) != true).ToHashSet();
        while (apart.Count > 0)
        {
            var start = apart.First();
            var piece = new HashSet<Title> { start };
            var next = new Queue<Title>([start]);
            apart.Remove(start);
            while (next.Count > 0)
            {
                if (!_sim.Adjacent.TryGetValue(next.Dequeue(), out var near)) continue;
                foreach (var n in near)
                    if (apart.Remove(n)) { piece.Add(n); next.Enqueue(n); }
            }
            if (!piece.Any(c => _sim.IsExclave(c, p))) return false;
        }
        return true;
    }

    /// <summary>
    /// Marks every start-date realm's pieces apart from its capital as its exclaves, which it keeps
    /// — the written world's shape, not the simulation's to correct on the first tick.
    /// </summary>
    private void SeatExclaves()
    {
        var exclaves = new Dictionary<Title, Polity>();
        foreach (var p in _sim.Polities.Where(p => p.Alive))
        {
            if (Whole(p)) continue;
            var seen = new HashSet<Title> { p.Capital };
            var queue = new Queue<Title>([p.Capital]);
            while (queue.Count > 0)
            {
                if (!_sim.Adjacent.TryGetValue(queue.Dequeue(), out var near)) continue;
                foreach (var n in near)
                    if (p.Counties.Contains(n) && seen.Add(n)) queue.Enqueue(n);
            }
            foreach (var county in p.Counties.Where(c => !seen.Contains(c))) exclaves[county] = p;
        }
        if (exclaves.Count > 0) _sim.Exclaves = exclaves;
    }
}
