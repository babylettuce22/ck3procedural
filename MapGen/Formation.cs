using Ck3MapGen.Config;
using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen;

/// <summary>
/// What happened to a polity, in the epoch it happened.
///
/// The kind is the half a later reader acts on, the same split <see cref="ChronicleEvent"/> makes.
/// Nothing consumes this log yet — the chronicle still invents its own past — but the simulation
/// below is the only place in the generator that knows what actually happened, so it records it
/// rather than throwing it away and leaving the chronicle to guess a second time.
/// </summary>
public enum FormationKind
{
    /// <summary>One county changed hands by force.</summary>
    Conquest,

    /// <summary>A whole realm submitted to a stronger neighbour and kept its land.</summary>
    Vassalized,

    /// <summary>A vassal realm stopped answering to anyone.</summary>
    Freed,

    /// <summary>A realm shed a block it could no longer hold.</summary>
    Fragmented,

    /// <summary>A realm's vassals all walked out at once.</summary>
    Collapsed,

    /// <summary>A realm lost its last county and stopped existing.</summary>
    Absorbed,
}

/// <summary>
/// The rules of the realm simulation that can be switched off, for the History workspace. All on
/// is the formation as it has always run, and what generation uses.
///
/// A rule that is off still rolls its dice and ignores the result, so switching one off never
/// changes which way another's fall that year: turning off homage takes the homage out of a
/// year's events and leaves its conquests where they were.
///
/// Not here, and not switchable: a realm cut in two by a conquest always splits along the cut
/// (<c>ShedIslands</c>). Titling and CK3 both need every realm in one piece.
/// </summary>
[Flags]
public enum RealmRules
{
    None = 0,

    /// <summary>A realm takes a county from a neighbour by force.</summary>
    Conquest = 1,

    /// <summary>A realm several times a neighbour's size takes it as a vassal, whole.</summary>
    Homage = 2,

    /// <summary>An overstretched realm loses a block of its edge, which becomes a realm of its own.</summary>
    Secession = 4,

    /// <summary>An unstable realm's vassals all walk out at once.</summary>
    Collapse = 8,

    All = Conquest | Homage | Secession | Collapse,
}

/// <summary>One thing the simulation did, dated, with both parties named.</summary>
public sealed class FormationEvent
{
    public required FormationKind Kind { get; init; }
    public required int Year { get; init; }

    /// <summary>The county the event happened to, or the actor's capital when it happened to a realm.</summary>
    public required Title Subject { get; init; }

    /// <summary>The capital of the other party, where there was one.</summary>
    public Title? Counterpart { get; init; }

    /// <summary>The acting realm's capital when the event happened: the conqueror, the new
    /// suzerain, the realm that fragmented. Null for an event with no actor.</summary>
    public Title? Actor { get; init; }

    public Culture? Culture { get; init; }
    public Culture? CounterpartCulture { get; init; }

    /// <summary>How much bad blood this left, 0 to 3. Graded the way <see cref="ChronicleEvent.Tension"/> is.</summary>
    public int Tension { get; init; }
}

/// <summary>
/// One realm during the simulation: a blob of counties, a capital, and whoever it answers to.
///
/// Deliberately not a <see cref="Title"/> and deliberately unaware of the de jure tree. A polity
/// owns whatever ground it took, which is the entire point — the de jure tree is a separate
/// geographic clustering, and the two agree only by coincidence. Titles are handed out once the
/// simulation is over, by <see cref="Realms.FromFormation"/>.
/// </summary>
public sealed class Polity
{
    public required int Id { get; init; }

    /// <summary>The seat. Always a member of <see cref="Counties"/> while the polity is alive.</summary>
    public required Title Capital { get; set; }

    public HashSet<Title> Counties { get; } = [];

    /// <summary>Whom this realm answers to, or null when it is independent.</summary>
    public Polity? Suzerain { get; set; }

    /// <summary>The capital's culture, which is what the realm's own people are.</summary>
    public required Culture Culture { get; set; }

    /// <summary>The year this realm came into being, which is what its cohesion matures from.</summary>
    public required int Founded { get; set; }

    /// <summary>The most counties it ever held. The high-water mark a later tier snapshots de jure from.</summary>
    public int Peak { get; set; }

    public bool Alive => Counties.Count > 0;

    /// <summary>The independent realm at the top of this one's chain of homage.</summary>
    public Polity Root
    {
        get
        {
            var p = this;
            // Bounded rather than while(true). A cycle here would hang the generator, and the
            // vassalage rule is written so one cannot form; this is what makes that cheap to trust.
            for (int i = 0; i < MaxDepth + 2 && p.Suzerain is not null; i++) p = p.Suzerain;
            return p;
        }
    }

    /// <summary>How many steps of homage sit above this realm. 0 for an independent one.</summary>
    public int Depth
    {
        get
        {
            int d = 0;
            for (var p = Suzerain; p is not null && d <= MaxDepth + 2; p = p.Suzerain) d++;
            return d;
        }
    }

    /// <summary>
    /// The deepest chain of homage the simulation will build: emperor, king, duke.
    ///
    /// CK3 has four tiers and the bottom one is spent on the counts that make up a duke's own
    /// vassals, so a chain longer than this has nowhere to land when titles are handed out.
    /// </summary>
    public const int MaxDepth = 2;

    public override string ToString() => $"#{Id} {Capital.Name} ({Counties.Count})";
}

/// <summary>Everything the simulation produced: who is standing at the bookmark, and how they got there.</summary>
public sealed class FormationHistory
{
    /// <summary>Every realm still holding ground at the start date, capital index order.</summary>
    public required List<Polity> Polities { get; init; }

    /// <summary>Which realm holds each county.</summary>
    public required Dictionary<Title, Polity> Owner { get; init; }

    /// <summary>Everything that happened, oldest first. Read by nothing yet — see <see cref="FormationKind"/>.</summary>
    public required List<FormationEvent> Events { get; init; }

    /// <summary>The year the simulation began. Every event is dated between here and the start date.</summary>
    public required int FirstYear { get; init; }

    /// <summary>
    /// The map at each additional bookmark (<see cref="MapConfig.AdditionalBookmarkYears"/>), keyed
    /// by that year: a copy of the last epoch at or before it, with its own polity objects. One
    /// after the start date comes from the simulation run on past it on a copy. Empty unless
    /// additional bookmarks are on.
    /// </summary>
    public Dictionary<int, FormationHistory> Snapshots { get; init; } = [];

    /// <summary>
    /// What the simulation ran on, kept so it can be picked up again from where it stopped — see
    /// <see cref="HistorySim"/>. Set on the history <see cref="Formation.Run"/> returns and null on
    /// every snapshot. Reading it changes nothing about the run that produced it.
    /// </summary>
    public FormationRules? Rules { get; init; }
}

/// <summary>
/// The constants of one formation run: the ground it moves counties across and the three dials it
/// was tuned with. Everything <see cref="HistorySim"/> needs to go on from the start date that the
/// polities themselves do not carry.
/// </summary>
public sealed class FormationRules
{
    public required Dictionary<Title, HashSet<Title>> Adjacent { get; init; }
    public required Dictionary<Title, int> Development { get; init; }
    public required Dictionary<Title, Culture> CountyCulture { get; init; }
    public required double AvgKingdom { get; init; }
    public required double Reach { get; init; }
    public required double Aggression { get; init; }
    public required double Turbulence { get; init; }
    public required int Seed { get; init; }

    /// <summary>The id the next polity born will take.</summary>
    public required int NextId { get; init; }
}

/// <summary>
/// Grows realms out of a world of single-county chiefdoms, over centuries, and hands the result to
/// <see cref="Realms"/> to be dressed in titles.
///
/// This exists because the de jure tree and the realm map used to be the same computation run
/// twice: <see cref="Titles.Build"/> clusters baronies geographically, and the old allocation then
/// walked that same clustering top-down handing out titles. Every border it produced was therefore
/// a de jure border, and the political map was the de jure map with some titles left unheld. The
/// simulation does not know the de jure tree exists. It moves counties across the adjacency graph,
/// which is geography, so the borders it draws cut de jure lines wherever the fighting went.
///
/// Four things happen each epoch, and the last two are what make the result read as history rather
/// than as a land grab: realms expand, realms subordinate each other, overstretched realms shed
/// their edges, and large realms occasionally come apart entirely. Without the last two, every run
/// ends in one world empire. With them a world can just as easily finish as forty duchies, which is
/// the variation the old share-based allocation could not produce.
/// </summary>
public static class Formation
{
    /// <summary>Years per tick. Roughly a reign, which is the rate realms actually change hands at.</summary>
    internal const int EpochYears = 25;

    /// <summary>
    /// Everything the simulation needs that does not change from epoch to epoch, in one place so
    /// the rules below read as rules rather than as parameter lists.
    ///
    /// <see cref="Owner"/> and the polities' own county sets are the two halves of one fact and are
    /// only ever changed together, by <see cref="Transfer"/>. Nothing else may write either.
    /// </summary>
    internal sealed class Sim
    {
        public required List<Polity> Polities { get; init; }
        public required Dictionary<Title, Polity> Owner { get; init; }
        public required Dictionary<Title, HashSet<Title>> Adjacent { get; init; }
        public required Dictionary<Title, int> Development { get; init; }
        public required Dictionary<Title, Culture> CountyCulture { get; init; }
        public required List<FormationEvent> Events { get; init; }

        /// <summary>Counties per de jure kingdom, which is what a realm's action rate scales with.</summary>
        public required double AvgKingdom { get; init; }

        /// <summary>
        /// How many years one <see cref="Step"/> stands for. <see cref="EpochYears"/> is what
        /// generation runs and what every rate below was tuned at; anything shorter is the live
        /// simulation, which scales each per-epoch rate down to its share of the epoch.
        /// </summary>
        public int TickYears { get; init; } = EpochYears;

        /// <summary>Which rules are in force. Settable between ticks; see <see cref="RealmRules"/>.</summary>
        public RealmRules Rules { get; set; } = RealmRules.All;

        /// <summary>How big a realm gets before it starts to strain.</summary>
        public required double Reach { get; init; }

        /// <summary>How readily realms attack and subordinate each other.</summary>
        public required double Aggression { get; init; }

        /// <summary>How readily they fall apart.</summary>
        public required double Turbulence { get; init; }

        public int NextId;
        public int Year;

        public void Log(FormationKind kind, Title subject, Polity? actor, Polity? other, int tension)
            => Events.Add(new FormationEvent
            {
                Kind = kind,
                Year = Year,
                Subject = subject,
                Counterpart = other?.Capital,
                Actor = actor?.Capital,
                Culture = actor?.Culture,
                CounterpartCulture = other?.Culture,
                Tension = tension,
            });
    }

    public static FormationHistory Run(
        List<Title> counties,
        Dictionary<Title, HashSet<Title>> countyAdj,
        Dictionary<Title, int> development,
        CultureMap cultures,
        MapConfig cfg,
        int deJureKingdoms)
    {
        // Capped so the simulation never begins before year 1. CK3 history files cannot carry a
        // negative date, and the event log is dated from here — nothing emits it yet, but a run
        // configured with more centuries than the calendar has would otherwise hand the first
        // consumer of it a pile of unwritable dates.
        int epochs = Math.Max(1, Math.Min(cfg.FormationYears, cfg.StartYear - 1) / EpochYears);
        int firstYear = cfg.StartYear - epochs * EpochYears;

        // Index order, not tree order, for the reason the chronicle gives: the tree is rebuilt every
        // run and its iteration order is stable only by accident, while an index is assigned once
        // and never moves. Seeding the whole simulation off it is what makes a map reproducible.
        var ordered = counties.OrderBy(c => c.Index).ToList();

        if (ordered.Count == 0)
            return new FormationHistory { Polities = [], Owner = [], Events = [], FirstYear = firstYear };

        // How big a realm has to get before it is straining, scaled off the de jure kingdom rather
        // than set as a constant, so the numbers mean the same thing on a hundred-county map and on
        // a two-thousand-county one.
        double avgKingdom = deJureKingdoms > 0
            ? (double)ordered.Count / deJureKingdoms
            : Math.Max(6.0, ordered.Count / 12.0);

        // The share knobs no longer decide anything directly. They lean on the simulation: a world
        // configured to want emperors fights harder and holds together longer, and one that wants
        // none frays. What comes out still varies from seed to seed, which is the point of running
        // it at all — see Realms.FitTiers for the other half, which corrects systematic bias
        // without truncating the distribution.
        double consolidation = Math.Clamp(
            cfg.EmpireTitleShare * 1.6 + cfg.KingdomTitleShare * 0.8 + cfg.DuchyTitleShare * 0.3,
            0.05, 1.0);

        var sim = new Sim
        {
            Polities = [],
            Owner = [],
            Adjacent = countyAdj,
            Development = development,
            CountyCulture = ordered.ToDictionary(c => c, cultures.For),
            Events = [],
            AvgKingdom = avgKingdom,
            // Tighter than a de jure kingdom at the low end, so that realms of kingdom size and up
            // are the ones under strain. Set generously, nothing on a real map ever reaches it.
            Reach = Math.Max(4.0, avgKingdom * (0.70 + 0.80 * consolidation)),
            Aggression = 0.55 + 0.5 * consolidation,
            Turbulence = Math.Clamp(cfg.FormationTurbulence, 0.0, 1.0),
        };

        foreach (var county in ordered)
        {
            var p = new Polity
            {
                Id = sim.NextId++,
                Capital = county,
                Culture = sim.CountyCulture[county],
                Founded = firstYear,
                Peak = 1,
            };
            p.Counties.Add(county);
            sim.Polities.Add(p);
            sim.Owner[county] = p;
        }

        // The additional bookmarks read the map as it stood on their date. Copied as the loop passes
        // each one, and never fed back, so the dice below are the same with or without them.
        int[] snapshotYears = cfg.UsesAdditionalBookmarks ? cfg.AdditionalBookmarkYears : [];
        var snapshots = new Dictionary<int, FormationHistory>();

        void Capture(Sim at, int from)
        {
            foreach (int year in snapshotYears)
                if (!snapshots.ContainsKey(year) && year < from + EpochYears)
                    snapshots[year] = Freeze(at, firstYear);
        }

        Capture(sim, firstYear);

        for (int epoch = 0; epoch < epochs; epoch++)
        {
            Epoch(sim, epoch);
            Capture(sim, sim.Year);
        }

        var survivors = sim.Polities.Where(p => p.Alive).OrderBy(p => p.Capital.Index).ToList();

        // Homage to a realm that died mid-epoch would dangle. Cleared at the end rather than at each
        // death: a suzerain can lose its last county and still be standing again two lines later.
        foreach (var p in survivors)
            if (p.Suzerain is { Alive: false }) p.Suzerain = null;

        int independent = survivors.Count(p => p.Suzerain is null);
        int biggest = survivors.Count == 0 ? 0 : survivors.Max(p => p.Counties.Count);

        Console.WriteLine($"  formation: {epochs} epochs, {firstYear} to {cfg.StartYear} — " +
                          $"{ordered.Count} chiefdoms became {survivors.Count} realms " +
                          $"({independent} independent, largest {biggest} counties), " +
                          $"{sim.Events.Count} events");

        // Bookmarks after the start date need a future. It is run on a copy, from where the start
        // date left off and on the epoch streams that come next, so the start date's realms —
        // the objects returned below — are never touched by it.
        if (snapshotYears.Any(y => !snapshots.ContainsKey(y)))
        {
            var future = Continue(sim);
            for (int epoch = epochs; snapshotYears.Any(y => !snapshots.ContainsKey(y)); epoch++)
            {
                Epoch(future, epoch);
                Capture(future, future.Year);
            }

            Console.WriteLine($"  formation: run on to {future.Year} for the additional bookmarks — "
                              + $"{future.Polities.Count(p => p.Alive)} realms by then");
        }

        return new FormationHistory
        {
            Polities = survivors,
            Owner = sim.Owner,
            Events = sim.Events,
            FirstYear = firstYear,
            Snapshots = snapshots,
            Rules = new FormationRules
            {
                Adjacent = sim.Adjacent,
                Development = sim.Development,
                CountyCulture = sim.CountyCulture,
                AvgKingdom = sim.AvgKingdom,
                Reach = sim.Reach,
                Aggression = sim.Aggression,
                Turbulence = sim.Turbulence,
                Seed = cfg.Seed,
                NextId = sim.NextId,
            },
        };

        // One tick of the simulation.
        void Epoch(Sim s, int epoch)
        {
            s.Year = firstYear + (epoch + 1) * EpochYears;
            // Each epoch draws from its own stream, so a change to one tick's rules cannot shift
            // every later tick's dice. The golden-ratio constant is there to keep consecutive
            // epochs far apart in the seed space rather than one bit apart.
            var rng = new Rng(cfg.Seed ^ 0x5A17 ^ unchecked((int)(epoch * 0x9E3779B1u)));

            Step(s, rng);
        }
    }

    /// <summary>
    /// One tick of the simulation, of <see cref="Sim.TickYears"/> years, with the year already set.
    ///
    /// At <see cref="EpochYears"/> this is exactly the epoch generation has always run: the scaling
    /// below is skipped outright rather than computed as a factor of one, so neither the arithmetic
    /// nor the dice drawn differ by a bit. At a shorter tick every rate that was tuned per epoch
    /// becomes that tick's share of it — see <see cref="PerTick"/> — so a century of one-year ticks
    /// and four epochs are the same process sampled at different grains.
    /// </summary>
    internal static void Step(Sim s, Rng rng)
    {
        bool epoch = s.TickYears == EpochYears;

        // Strongest first, so a great power picks its target before its neighbours pick theirs.
        // Tie-broken on the capital index, which never moves, so equal strength always resolves
        // the same way twice.
        var actors = s.Polities
            .Where(p => p.Alive)
            .OrderByDescending(p => Strength(s, p))
            .ThenBy(p => p.Capital.Index)
            .ToList();

        foreach (var p in actors)
        {
            if (!p.Alive) continue;

            // Big realms act more often, and this is the single number that decides whether the
            // world consolidates at all. One action per epoch caps a realm's lifetime growth at
            // the epoch count however strong it gets, which on a six-century run is about
            // twenty counties — so every world came out a scatter of duchies with no great
            // power in it, whatever the odds per fight said. Scaled against a quarter of a
            // kingdom so the snowball engages on a small map as well as a large one.
            int actions = Math.Clamp(
                1 + (int)(p.Counties.Count / Math.Max(2.0, s.AvgKingdom * 0.25)), 1, 5);

            // A shorter tick gets its share of the epoch's attempts: the whole part always, the
            // remainder as a chance. Each attempt then plays out exactly as an epoch's would, so
            // the odds inside Act are per attempt and need no scaling of their own.
            if (!epoch)
            {
                double expected = actions * (double)s.TickYears / EpochYears;
                actions = (int)expected;
                if (rng.Chance(expected - actions)) actions++;
            }

            for (int a = 0; a < actions && p.Alive; a++) Act(s, p, rng);
        }

        foreach (var p in Snapshot(s)) if (p.Alive) Strain(s, p, rng);

        // Anything that came apart on the map rather than in the rules. A realm that loses a
        // county in its middle is two realms whatever the ownership table says, and leaving it
        // whole is what produces a ruler seated in an exclave with vassals he cannot reach.
        foreach (var p in Snapshot(s)) if (p.Alive) ShedIslands(s, p);

        foreach (var p in s.Polities) if (p.Alive) p.Peak = Math.Max(p.Peak, p.Counties.Count);
    }

    /// <summary>
    /// A chance tuned as "per epoch", as the chance per tick that compounds to it over an epoch.
    /// Returned untouched at an epoch's length — not computed as a power of one — so generation
    /// draws against the identical number it always has.
    /// </summary>
    private static double PerTick(Sim s, double perEpoch)
        => s.TickYears == EpochYears
            ? perEpoch
            : 1.0 - Math.Pow(1.0 - Math.Clamp(perEpoch, 0.0, 1.0), (double)s.TickYears / EpochYears);

    /// <summary>
    /// The simulation as it stands, on polity objects of its own, to be run on past the start date
    /// without moving the start date's realms. Ids carry over, as in <see cref="Freeze"/>.
    /// </summary>
    private static Sim Continue(Sim sim)
    {
        var now = Freeze(sim, 0);
        return new Sim
        {
            Polities = now.Polities,
            Owner = now.Owner,
            Adjacent = sim.Adjacent,
            Development = sim.Development,
            CountyCulture = sim.CountyCulture,
            Events = [],
            AvgKingdom = sim.AvgKingdom,
            Reach = sim.Reach,
            Aggression = sim.Aggression,
            Turbulence = sim.Turbulence,
            NextId = sim.NextId,
            Year = sim.Year,
        };
    }

    /// <summary>
    /// The living realms as they stand now, as new objects: titling a snapshot folds and frees
    /// polities in place, and must not touch the simulation's own. Ids are kept, which is how a
    /// realm is recognised from one bookmark to the next.
    /// </summary>
    internal static FormationHistory Freeze(Sim sim, int firstYear)
    {
        var copies = new Dictionary<Polity, Polity>();
        foreach (var p in sim.Polities.Where(p => p.Alive).OrderBy(p => p.Capital.Index))
        {
            var q = new Polity
            {
                Id = p.Id, Capital = p.Capital, Culture = p.Culture, Founded = p.Founded, Peak = p.Peak,
            };
            q.Counties.UnionWith(p.Counties);
            copies[p] = q;
        }

        var owner = new Dictionary<Title, Polity>();
        foreach (var (p, q) in copies)
        {
            q.Suzerain = p.Suzerain is { } s && copies.TryGetValue(s, out var sq) ? sq : null;
            foreach (var c in q.Counties) owner[c] = q;
        }

        return new FormationHistory
        {
            Polities = [.. copies.Values],
            Owner = owner,
            Events = [.. sim.Events.Where(e => e.Year <= sim.Year)],
            FirstYear = firstYear,
        };
    }

    /// <summary>
    /// The living polities in capital order, copied.
    ///
    /// Copied because the passes that use it add polities as they go, and because a fragment created
    /// halfway down the list must not be asked to fragment again in the same epoch.
    /// </summary>
    private static List<Polity> Snapshot(Sim sim)
        => sim.Polities.Where(p => p.Alive).OrderBy(p => p.Capital.Index).ToList();

    // --- Strength and cohesion ------------------------------------------------------------------

    /// <summary>
    /// What a realm can bring to bear: everything it owns, discounted by how well it holds together.
    /// A sprawling realm of mixed peoples fields less than its acreage suggests, which is what stops
    /// the biggest polity on the map simply going on being the biggest.
    /// </summary>
    private static double Strength(Sim sim, Polity p)
    {
        double land = 0;
        foreach (var c in p.Counties) land += sim.Development.GetValueOrDefault(c) + 1;

        // Vassals count, at a discount, so that taking homage is worth something rather than being
        // strictly worse than annexing the same ground county by county.
        foreach (var v in sim.Polities)
        {
            if (!v.Alive || v.Suzerain != p) continue;
            foreach (var c in v.Counties) land += (sim.Development.GetValueOrDefault(c) + 1) * 0.4;
        }

        return land * Cohesion(sim, p);
    }

    /// <summary>
    /// How well a realm holds together, 0.15 to 1. Capped at 1 rather than above it so that
    /// <c>1 - cohesion</c> is a usable measure of how close it is to coming apart — see
    /// <see cref="Strain"/>, which is the only reason the number exists.
    ///
    /// The constants are chosen so a compact realm of one people is flatly stable however old it
    /// gets, and everything else carries some standing risk. An earlier version had a higher
    /// ceiling and a gentler slope, which read fine and meant nothing: no realm on a real map ever
    /// came within reach of the thresholds that fragmentation and collapse were gated on, so both
    /// rules were dead and every world came out the same.
    /// </summary>
    private static double Cohesion(Sim sim, Polity p)
    {
        if (p.Counties.Count == 0) return 0;

        double strain = p.Counties.Count / sim.Reach;

        int same = 0;
        foreach (var c in p.Counties)
            if (sim.CountyCulture.TryGetValue(c, out var culture) && culture == p.Culture) same++;

        double homogeneity = (double)same / p.Counties.Count;
        double age = Math.Min(1.0, (sim.Year - p.Founded) / 150.0);

        return Math.Clamp(
            0.95
            - 0.70 * Math.Max(0.0, strain - 1.0)
            - 0.40 * (1.0 - homogeneity)
            + 0.15 * age,
            0.15, 1.0);
    }

    // --- Actions --------------------------------------------------------------------------------

    private static void Act(Sim sim, Polity p, Rng rng)
    {
        var root = p.Root;

        // Every county on our border held by somebody outside our own chain of homage, and how many
        // of our counties touch it.
        var border = new Dictionary<Title, int>();
        foreach (var mine in p.Counties)
        {
            if (!sim.Adjacent.TryGetValue(mine, out var near)) continue;
            foreach (var n in near)
            {
                if (p.Counties.Contains(n)) continue;
                if (!sim.Owner.TryGetValue(n, out var q) || !q.Alive || q.Root == root) continue;
                border[n] = border.GetValueOrDefault(n) + 1;
            }
        }

        if (border.Count == 0) return;

        // How exposed a county is to us stands in for how far it lies from our capital: one we touch
        // at four points is inside our reach in a way one we touch at a single point is not. That
        // keeps the simulation on the adjacency graph and off a distance metric it has no
        // coordinates to compute. Index order breaks ties so the same state picks the same target.
        var target = border
            .OrderByDescending(kv => kv.Value * (sim.Development.GetValueOrDefault(kv.Key) + 1)
                                   / (1.0 + Strength(sim, sim.Owner[kv.Key])))
            .ThenBy(kv => kv.Key.Index)
            .First().Key;

        var defender = sim.Owner[target];

        double atk = Strength(sim, p);
        double def = Strength(sim, defender);

        // Submission before conquest. A realm several times a neighbour's size would rather have it
        // whole and paying homage than spend four reigns eating it county by county — and this is
        // the only action that hands a realm a vassal whose borders nobody drew, which is most of
        // what makes the finished map look unplanned.
        // The whole subtree has to fit, not just the defender. Testing p.Depth alone let a realm
        // that already had clients of its own submit as a unit, which pushed every one of those
        // clients a rung past MaxDepth — and a chain that deep has no tier left to stand on when
        // titles are handed out, so the titling step simply cut it loose again.
        // Rolled whether or not homage is in force, and acted on only when it is: see RealmRules.
        // A roll that comes up with homage switched off falls through to the attack, as a roll
        // that failed would.
        if (defender.Suzerain is null
            && defender != root
            && p.Depth + 1 + SubtreeDepth(sim, defender) <= Polity.MaxDepth
            && defender.Counties.Count >= 2
            && atk > def * 2.2
            && rng.Chance(0.45 * sim.Aggression)
            && sim.Rules.HasFlag(RealmRules.Homage))
        {
            defender.Suzerain = p;
            sim.Log(FormationKind.Vassalized, defender.Capital, defender, p,
                    defender.Culture == p.Culture ? 1 : 2);
            return;
        }

        if (!rng.Chance(sim.Aggression * atk / (atk + def))) return;
        if (!sim.Rules.HasFlag(RealmRules.Conquest)) return;

        bool wasCapital = defender.Capital == target;
        Transfer(sim, target, defender, p);

        sim.Log(FormationKind.Conquest, target, p, defender,
                defender.Culture == p.Culture ? 1 : 3);

        if (!defender.Alive)
        {
            // Whoever answered to the dead realm now answers to its conqueror, up to the tier
            // limit; past that they are simply free. A realm that swallowed an overlord inherits
            // its clients, which is how a three-tier structure gets built without anyone planning
            // one.
            foreach (var v in sim.Polities.Where(v => v.Alive && v.Suzerain == defender)
                                          .OrderBy(v => v.Capital.Index).ToList())
            {
                v.Suzerain = p.Depth + 1 + SubtreeDepth(sim, v) <= Polity.MaxDepth ? p : null;
                if (v.Suzerain is null)
                {
                    v.Founded = sim.Year;
                    sim.Log(FormationKind.Freed, v.Capital, v, defender, 2);
                }
            }

            defender.Suzerain = null;
            sim.Log(FormationKind.Absorbed, wasCapital ? target : defender.Capital, defender, p, 3);
        }
    }

    /// <summary>
    /// Fragmentation and collapse — the two rules that keep the map from converging.
    ///
    /// Expansion on its own is monotonic: every epoch moves counties from the weak to the strong and
    /// nothing ever moves them back, so a long enough run ends with one realm holding everything.
    /// These push the other way, and because they fire on cohesion rather than on a dice roll
    /// against a target count, a world that overreached comes apart and a compact one does not.
    /// </summary>
    private static void Strain(Sim sim, Polity p, Rng rng)
    {
        // Continuous, not a threshold. Gating these on "cohesion below 0.55" made both rules fire
        // never or always depending on constants elsewhere, and left the turbulence dial with
        // nothing to move: every setting produced a byte-identical world. As a rate, a slightly
        // strained realm carries a small standing risk each reign and a badly strained one rarely
        // survives two, which is the behaviour the thresholds were reaching for.
        double instability = 1.0 - Cohesion(sim, p);

        // A realm's vassals walk out together. This is the event that makes de jure and de facto
        // diverge for good: the ground the empire held goes on being drawn the way the empire drew
        // it, while the realms standing on it are its former clients.
        var vassals = sim.Polities.Where(v => v.Alive && v.Suzerain == p)
                                  .OrderBy(v => v.Capital.Index).ToList();

        if (vassals.Count > 0 && rng.Chance(PerTick(sim, instability * sim.Turbulence * 0.9))
            && sim.Rules.HasFlag(RealmRules.Collapse))
        {
            foreach (var v in vassals)
            {
                v.Suzerain = null;
                v.Founded = sim.Year;
                sim.Log(FormationKind.Freed, v.Capital, v, p, 2);
            }

            sim.Log(FormationKind.Collapsed, p.Capital, p, null, 3);
            return;
        }

        // Overstretched, so the edge goes its own way. Taken as a connected block grown from the
        // most weakly attached county rather than as a scatter, so what breaks off is a realm and
        // not a handful of enclaves. The size floor stays a hard gate: a realm of five counties
        // shedding two is not a fragmenting empire, it is noise.
        if (p.Counties.Count < 6) return;
        if (!rng.Chance(PerTick(sim, instability * sim.Turbulence * 1.5))) return;
        if (!sim.Rules.HasFlag(RealmRules.Secession)) return;

        var block = PeripheralBlock(sim, p, Math.Max(2, p.Counties.Count / 3));
        if (block.Count == 0 || block.Count >= p.Counties.Count) return;

        Secede(sim, p, block, FormationKind.Fragmented, 2);
    }

    /// <summary>
    /// A connected block of about <paramref name="want"/> counties grown from the realm's weakest
    /// attachment, never including the capital.
    /// </summary>
    private static List<Title> PeripheralBlock(Sim sim, Polity p, int want)
    {
        int Inward(Title c) => sim.Adjacent.TryGetValue(c, out var near)
            ? near.Count(p.Counties.Contains)
            : 0;

        var seed = p.Counties
            .Where(c => c != p.Capital)
            .OrderBy(Inward)
            .ThenBy(c => c.Index)
            .FirstOrDefault();

        if (seed is null) return [];

        var block = new List<Title> { seed };
        var taken = new HashSet<Title> { seed };

        while (block.Count < want)
        {
            Title? next = null;
            int bestInward = int.MaxValue;

            foreach (var c in block)
            {
                if (!sim.Adjacent.TryGetValue(c, out var near)) continue;
                foreach (var n in near.OrderBy(n => n.Index))
                {
                    if (n == p.Capital || taken.Contains(n) || !p.Counties.Contains(n)) continue;

                    int inward = Inward(n);
                    if (next is null || inward < bestInward)
                    {
                        next = n;
                        bestInward = inward;
                    }
                }
            }

            if (next is null) break;
            taken.Add(next);
            block.Add(next);
        }

        return block;
    }

    /// <summary>
    /// Spins off every part of a realm that no longer touches its capital.
    ///
    /// Conquest can cut a realm in half by taking the county that joined its two ends, and the
    /// ownership table happily goes on saying one realm owns both. The liege rules downstream do
    /// not: a ruler whose vassals cannot reach him loses every one of them at once, which is how an
    /// earlier version of this generator turned twelve countries into a hundred and thirty-six
    /// realms. Cheaper to hold the invariant here than to find it in the emitted map.
    /// </summary>
    private static void ShedIslands(Sim sim, Polity p)
    {
        if (p.Counties.Count < 2) return;

        var mainland = Reachable(sim, p, p.Capital, within: p.Counties);
        if (mainland.Count == p.Counties.Count) return;

        var stranded = p.Counties.Where(c => !mainland.Contains(c)).OrderBy(c => c.Index).ToList();

        while (stranded.Count > 0)
        {
            // One new realm per connected island, not one per county.
            var island = Reachable(sim, p, stranded[0], within: p.Counties, except: mainland);
            Secede(sim, p, [.. island.OrderBy(c => c.Index)], FormationKind.Fragmented, 1);
            stranded.RemoveAll(island.Contains);
        }
    }

    private static HashSet<Title> Reachable(
        Sim sim, Polity p, Title from, HashSet<Title> within, HashSet<Title>? except = null)
    {
        var seen = new HashSet<Title> { from };
        var queue = new Queue<Title>();
        queue.Enqueue(from);

        while (queue.Count > 0)
        {
            if (!sim.Adjacent.TryGetValue(queue.Dequeue(), out var near)) continue;
            foreach (var n in near)
            {
                if (!within.Contains(n)) continue;
                if (except is not null && except.Contains(n)) continue;
                if (seen.Add(n)) queue.Enqueue(n);
            }
        }

        return seen;
    }

    // --- Bookkeeping ----------------------------------------------------------------------------

    /// <summary>Breaks <paramref name="block"/> off <paramref name="from"/> as a realm of its own.</summary>
    private static void Secede(
        Sim sim, Polity from, List<Title> block, FormationKind kind, int tension)
    {
        if (block.Count == 0) return;

        var seat = block
            .OrderByDescending(c => sim.Development.GetValueOrDefault(c))
            .ThenBy(c => c.Index)
            .First();

        var broken = new Polity
        {
            Id = sim.NextId++,
            Capital = seat,
            Culture = sim.CountyCulture[seat],
            Founded = sim.Year,
            Peak = block.Count,

            // A piece that breaks off an imperial vassal answers to whoever the parent answered to,
            // not to the parent it just walked out on.
            Suzerain = from.Suzerain,
        };

        foreach (var c in block) Transfer(sim, c, from, broken);
        sim.Polities.Add(broken);
        sim.Log(kind, seat, broken, from, tension);
    }

    /// <summary>
    /// Moves one county between realms. The only writer of <see cref="Sim.Owner"/> and of any
    /// polity's county set, so the two halves cannot drift apart.
    /// </summary>
    private static void Transfer(Sim sim, Title county, Polity from, Polity to)
    {
        from.Counties.Remove(county);
        to.Counties.Add(county);
        sim.Owner[county] = to;

        // A capital that changed hands has to be replaced before anything asks the realm where it
        // is seated. Best remaining county, index order breaking ties.
        if (from.Capital == county && from.Counties.Count > 0)
        {
            from.Capital = from.Counties
                .OrderByDescending(c => sim.Development.GetValueOrDefault(c))
                .ThenBy(c => c.Index)
                .First();

            // And its people with it. Culture is defined as the capital's, and Cohesion measures a
            // realm's counties against it — so a realm driven out of its homeland and left holding
            // only foreign ground would otherwise score zero homogeneity for ever: permanently
            // unstable, with no way back however long it then held what it had.
            from.Culture = sim.CountyCulture[from.Capital];
        }
    }

    /// <summary>
    /// How many rungs of homage hang below <paramref name="p"/>. 0 when nobody answers to it.
    /// </summary>
    private static int SubtreeDepth(Sim sim, Polity p)
    {
        int deepest = 0;
        foreach (var v in sim.Polities)
            if (v.Alive && v.Suzerain == p) deepest = Math.Max(deepest, 1 + SubtreeDepth(sim, v));
        return deepest;
    }
}
