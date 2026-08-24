using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.Emit;

namespace Ck3MapGen.MapGen;

/// <summary>
/// What a title is remembered for.
///
/// The kind is not decoration. It is the half of an event that a *reader other than the lore panel*
/// can act on: <see cref="ChronicleMap.Contested"/> selects on it, and struggle generation — when it
/// exists — will select on it too. Adding prose means adding a template; adding a new KIND means
/// something new can be reasoned about, so keep the list short and keep each entry a real
/// distinction rather than a shade of an existing one.
/// </summary>
public enum ChronicleKind
{
    /// <summary>A people arrived and stayed.</summary>
    Settlement,

    /// <summary>Two peoples want the same ground. The load-bearing one for struggles.</summary>
    Frontier,

    /// <summary>A faith took hold, peacefully or otherwise.</summary>
    Faith,

    /// <summary>A house took a seat and has not let go of it.</summary>
    Seat,

    /// <summary>Two houses fell out, and it stuck.</summary>
    Feud,

    /// <summary>Somebody marched. Drawn from the wars that are live at the bookmark.</summary>
    War,

    /// <summary>An object of significance came to rest here.</summary>
    Relic,

    /// <summary>Something was built that outlived whoever built it.</summary>
    Wonder,

    /// <summary>Ground a faith holds sacred.</summary>
    Sanctity,

    /// <summary>The summary line a duchy or above opens with. Never contested, never dated.</summary>
    Realm,
}

/// <summary>
/// One remembered thing, in two halves.
///
/// <see cref="Text"/> is the half the player reads. Everything else is the half a generator reads:
/// who was involved, on which side, and how badly it went. The structured half is deliberately not
/// derivable from the prose — parsing generated English back into facts is the failure mode this
/// type exists to prevent, and it is why the lore panel and struggle generation must both hang off
/// this record rather than off two independent passes that can quietly disagree with each other.
/// </summary>
public sealed class ChronicleEvent
{
    public required ChronicleKind Kind { get; init; }

    /// <summary>
    /// Absolute year. Frequently earlier than <see cref="MapConfig.StartYear"/> — most of what a
    /// realm is remembered for happened before the bookmark, which is the point.
    /// </summary>
    public required int Year { get; init; }

    /// <summary>One sentence, already in English, already escaped for a .yml value.</summary>
    public required string Text { get; init; }

    /// <summary>The title this is remembered about.</summary>
    public required Title Subject { get; init; }

    /// <summary>The other party, where there was one.</summary>
    public Title? Counterpart { get; init; }

    public Culture? Culture { get; init; }
    public Culture? CounterpartCulture { get; init; }
    public Faith? Faith { get; init; }
    public Faith? CounterpartFaith { get; init; }

    /// <summary>
    /// How much bad blood this left, 0 to 3.
    ///
    /// Struggle generation is a search for concentrations of this: a region whose chronicle carries
    /// sustained tension between two identifiable culture or faith blocs is, definitionally, a
    /// struggle. A single feud is not one, which is why this is a weight and not a flag.
    /// </summary>
    public int Tension { get; init; }
}

/// <summary>
/// The generated history of the world, indexed by who it happened to.
///
/// Built once, late, from everything the pipeline has already decided — it invents dates and prose
/// but never invents a culture, a faith, a house or a war, because a chronicle that disagrees with
/// the map is worse than no chronicle. Two things read it today: nothing, and
/// <see cref="Emit.ChronicleWriter"/>. The second reader is the point of the structured half.
/// </summary>
public sealed class ChronicleMap
{
    /// <summary>Events belonging to a title directly. A duchy's own entry does not contain its
    /// counties' events — <see cref="For"/> does that roll-up at read time, so the same event is
    /// never stored twice and a query over <see cref="All"/> cannot double-count it.</summary>
    public Dictionary<Title, List<ChronicleEvent>> ByTitle { get; } = [];

    public List<ChronicleEvent> All { get; } = [];

    /// <summary>
    /// Everything remembered about a title, oldest first: its own events, plus those of everything
    /// beneath it that was worth carrying up.
    ///
    /// A county's chronicle is its own. A duchy's is its own summary plus the loudest thing each of
    /// its counties has to say, because a duchy with six counties has thirty events and a panel can
    /// hold about eight. <paramref name="perChild"/> is that budget.
    /// </summary>
    public List<ChronicleEvent> For(Title title, int perChild = 2)
    {
        var gathered = new List<ChronicleEvent>();
        if (ByTitle.TryGetValue(title, out var own)) gathered.AddRange(own);

        if (title.Tier != "c")
        {
            foreach (var child in title.Children)
            {
                var childEvents = For(child, perChild);
                gathered.AddRange(childEvents
                    .OrderByDescending(e => e.Tension)
                    .ThenBy(e => e.Year)
                    .Take(perChild));
            }
        }

        return gathered
            .OrderBy(e => e.Kind == ChronicleKind.Realm ? 0 : 1)
            .ThenBy(e => e.Year)
            .ToList();
    }

    /// <summary>
    /// The cultures and faiths pulling against each other inside a title, and how hard.
    ///
    /// This is the query struggle generation is going to be built on, written now while the shape
    /// of the data is still negotiable. A generated struggle needs exactly three things —
    /// <c>cultures = { }</c>, <c>faiths = { }</c> and <c>regions = { }</c> — and the first two are
    /// what this returns for a candidate region. It is used by nothing yet; it is here so that the
    /// event fields above are chosen against a real consumer rather than against a guess about one.
    /// </summary>
    public (HashSet<Culture> Cultures, HashSet<Faith> Faiths, int Tension) Contested(Title title)
    {
        var cultures = new HashSet<Culture>();
        var faiths = new HashSet<Faith>();
        int tension = 0;

        foreach (var e in For(title, perChild: int.MaxValue).Where(e => e.Tension > 0))
        {
            tension += e.Tension;
            if (e.Culture is not null) cultures.Add(e.Culture);
            if (e.CounterpartCulture is not null) cultures.Add(e.CounterpartCulture);
            if (e.Faith is not null) faiths.Add(e.Faith);
            if (e.CounterpartFaith is not null) faiths.Add(e.CounterpartFaith);
        }

        return (cultures, faiths, tension);
    }

    private void Add(ChronicleEvent e)
    {
        if (!ByTitle.TryGetValue(e.Subject, out var list)) ByTitle[e.Subject] = list = [];
        list.Add(e);
        All.Add(e);
    }

    public static ChronicleMap Build(
        List<Title> empires,
        RealmMap realms,
        Dictionary<Title, int> development,
        CultureMap cultures,
        FaithMap faiths,
        WildernessMap wilderness,
        PrehistoryMap prehistory,
        ArtifactMap artifacts,
        WorldCenterMap worldCenters,
        MapConfig cfg,
        Rng rng)
    {
        var map = new ChronicleMap();

        var all = Titles.Flatten(empires).ToList();

        // Index order, not tree order. The tree is rebuilt from scratch on every run and its
        // iteration order is stable only by accident; the index is assigned once and never moves,
        // so seeding the prose off it keeps a county's history the same when an unrelated part of
        // the map changes.
        var counties = all
            .Where(t => t.Tier == "c" && !wilderness.Contains(t))
            .OrderBy(t => t.Index)
            .ToList();

        if (counties.Count == 0) return map;

        var rulerCounties = realms.HolderCounty.Values.ToHashSet();

        // Reverse of prehistory's county-to-house map, so a house relation can be told which county
        // to hang itself on. Houses are one-per-ruler, so this does not collide.
        var houseSeat = new Dictionary<string, Title>();
        foreach (var (county, house) in prehistory.CharacterHouseMap)
            houseSeat.TryAdd(house, county);

        var holySite = new Dictionary<Title, Faith>();
        foreach (var faith in faiths.Faiths)
            foreach (var (_, county) in faith.HolySites)
                holySite.TryAdd(county, faith);

        var wonderAt = new Dictionary<Title, GeneratedWonder>();
        foreach (var center in worldCenters.Centers)
            wonderAt.TryAdd(center.County, center.Wonder);

        foreach (var county in counties)
        {
            var culture = cultures.For(county);
            var faith = faiths.For(county);
            int dev = development.TryGetValue(county, out int d) ? d : 0;

            Settlement(map, county, culture, dev, cfg, rng);
            Frontier(map, county, cultures, faiths, wilderness, cfg, rng);
            FaithTook(map, county, culture, faith, faiths, wilderness, cfg, rng);

            if (rulerCounties.Contains(county))
            {
                Seat(map, county, prehistory, cfg, rng);
                Feud(map, county, prehistory, houseSeat, cultures, faiths, cfg);
            }

            War(map, county, prehistory, cultures, faiths, cfg);

            if (holySite.TryGetValue(county, out var siteFaith))
                Sanctity(map, county, siteFaith, cfg, rng);

            if (wonderAt.TryGetValue(county, out var wonder))
                Wonder(map, county, wonder, cfg, rng);

            if (artifacts.ByCounty.TryGetValue(county, out var relics) && relics.Count > 0)
                Relic(map, county, relics[0], cfg, rng);
        }

        // Duchies and up get an opening line of their own. Everything else they show is borrowed
        // upward from their counties by ChronicleMap.For.
        foreach (var title in all.Where(t => t.Tier is "d" or "k" or "e").OrderBy(t => t.Index))
        {
            if (title.Children.Count == 0) continue;
            if (Titles.Flatten([title]).Where(t => t.Tier == "c").All(wilderness.Contains)) continue;

            Realm(map, title, cultures, faiths, wilderness, cfg, rng);
        }

        int contested = map.All.Count(e => e.Tension > 0);
        Console.WriteLine($"  chronicle: {map.All.Count} events across {map.ByTitle.Count} titles "
            + $"({contested} contested)");

        return map;
    }

    // ---------------------------------------------------------------------------------------
    // Event builders. Each one owns its own dates and its own prose bank.
    // ---------------------------------------------------------------------------------------

    private static void Settlement(
        ChronicleMap map, Title county, Culture culture, int dev, MapConfig cfg, Rng rng)
    {
        int year = cfg.StartYear - rng.Int(180, 420);

        string[] bank = dev >= 12 ? SettledRich : dev >= 6 ? SettledPlain : SettledHard;

        map.Add(new ChronicleEvent
        {
            Kind = ChronicleKind.Settlement,
            Year = year,
            Subject = county,
            Culture = culture,
            Text = Fill(rng.Pick(bank), county, culture.Name, year),
        });
    }

    /// <summary>
    /// The contested-ground event, and the one struggle generation is actually waiting for.
    ///
    /// Contest is read off the de jure tree rather than off province adjacency: a county whose
    /// duchy contains another heritage is a frontier county, full stop. That is cheaper than a
    /// neighbour walk and — more to the point — it is at the grain a struggle is written at. A CK3
    /// struggle covers a region and names the peoples inside it, so a contest detected at duchy
    /// level converts to `cultures = { }` with no further work, while a contest detected between two
    /// individual provinces would have to be aggregated back up to this level anyway.
    /// </summary>
    private static void Frontier(
        ChronicleMap map, Title county, CultureMap cultures, FaithMap faiths,
        WildernessMap wilderness, MapConfig cfg, Rng rng)
    {
        var duchy = county.Parent;
        if (duchy is null) return;

        var mine = cultures.For(county);

        // The nearest county in the same duchy that belongs to a different PEOPLE, not merely a
        // different culture. Two cultures of one heritage are neighbours who talk funny; two
        // heritages are the thing wars get written about.
        //
        // Wilderness is excluded rather than treated as a party. Unclaimed land carries a
        // placeholder culture and faith so the engine has something to read, and letting those
        // stand as a counterpart produces a frontier against nobody -- and, worse, would hand
        // struggle generation a bloc made of the placeholder.
        var rival = duchy.Children
            .Where(c => c.Tier == "c" && c != county && !wilderness.Contains(c))
            .Select(c => (County: c, Culture: cultures.For(c)))
            .FirstOrDefault(x => x.Culture.Heritage != mine.Heritage);

        if (rival.County is null) return;

        int year = cfg.StartYear - rng.Int(60, 240);

        map.Add(new ChronicleEvent
        {
            Kind = ChronicleKind.Frontier,
            Year = year,
            Subject = county,
            Counterpart = rival.County,
            Culture = mine,
            CounterpartCulture = rival.Culture,
            Faith = faiths.For(county),
            CounterpartFaith = faiths.For(rival.County),
            Tension = 2,
            Text = Fill(rng.Pick(Frontiers), county, mine.Name, year, rival.Culture.Name),
        });
    }

    private static void FaithTook(
        ChronicleMap map, Title county, Culture culture, Faith faith, FaithMap faiths,
        WildernessMap wilderness, MapConfig cfg, Rng rng)
    {
        var duchy = county.Parent;
        int year = cfg.StartYear - rng.Int(40, 200);

        // A faith that disagrees with its neighbours about the RELIGION, not merely the faith, is a
        // schism; anything narrower is a local variation and gets the quiet template. Wilderness is
        // skipped for the reason given in Frontier -- its placeholder faith is not a dissenter.
        var dissenter = duchy?.Children
            .Where(c => c.Tier == "c" && c != county && !wilderness.Contains(c))
            .Select(faiths.For)
            .FirstOrDefault(f => f.Religion != faith.Religion);

        bool contested = dissenter is not null;

        map.Add(new ChronicleEvent
        {
            Kind = ChronicleKind.Faith,
            Year = year,
            Subject = county,
            Culture = culture,
            Faith = faith,
            CounterpartFaith = dissenter,
            Tension = contested ? 2 : 0,
            Text = contested
                ? Fill(rng.Pick(FaithsContested), county, faith.Name, year, dissenter!.Name)
                : Fill(rng.Pick(FaithsQuiet), county, faith.Name, year),
        });
    }

    private static void Seat(
        ChronicleMap map, Title county, PrehistoryMap prehistory, MapConfig cfg, Rng rng)
    {
        if (!prehistory.CharacterHouseMap.TryGetValue(county, out var houseKey)) return;
        if (!prehistory.Houses.TryGetValue(houseKey, out var house)) return;

        // Anchored on the ruler's own birth year so the house cannot be said to have taken the seat
        // after the man holding it was born into it.
        int born = HistoryWriter.GetRulerBirthYear(county.Index, cfg.StartYear);
        int year = born - rng.Int(10, 90);

        map.Add(new ChronicleEvent
        {
            Kind = ChronicleKind.Seat,
            Year = year,
            Subject = county,
            Text = Fill(rng.Pick(Seats), county, house.LocalizedName, year),
        });
    }

    private static void Feud(
        ChronicleMap map, Title county, PrehistoryMap prehistory,
        Dictionary<string, Title> houseSeat, CultureMap cultures, FaithMap faiths, MapConfig cfg)
    {
        if (!prehistory.CharacterHouseMap.TryGetValue(county, out var mine)) return;

        foreach (var relation in prehistory.HouseRelations)
        {
            if (relation.Level is not ("feud" or "rivalry")) continue;

            string? otherKey =
                relation.HouseA == mine ? relation.HouseB :
                relation.HouseB == mine ? relation.HouseA : null;

            if (otherKey is null) continue;
            if (!prehistory.Houses.TryGetValue(otherKey, out var other)) continue;

            int year = YearOf(relation.StartDate) ?? cfg.StartYear - 20;
            houseSeat.TryGetValue(otherKey, out var otherSeat);

            map.Add(new ChronicleEvent
            {
                Kind = ChronicleKind.Feud,
                Year = year,
                Subject = county,
                Counterpart = otherSeat,
                Culture = cultures.For(county),
                CounterpartCulture = otherSeat is null ? null : cultures.For(otherSeat),
                Faith = faiths.For(county),
                CounterpartFaith = otherSeat is null ? null : faiths.For(otherSeat),
                Tension = relation.Level == "feud" ? 3 : 1,
                Text = Fill(
                    relation.Level == "feud" ? FeudLine : RivalryLine,
                    county, other.LocalizedName, year),
            });
        }
    }

    private static void War(
        ChronicleMap map, Title county, PrehistoryMap prehistory,
        CultureMap cultures, FaithMap faiths, MapConfig cfg)
    {
        foreach (var war in prehistory.ActiveWars)
        {
            bool attacking = war.AttackerCounty == county;
            bool defending = war.DefenderCounty == county;
            if (!attacking && !defending) continue;

            var other = attacking ? war.DefenderCounty : war.AttackerCounty;
            int year = YearOf(war.StartDate) ?? cfg.StartYear;

            map.Add(new ChronicleEvent
            {
                Kind = ChronicleKind.War,
                Year = year,
                Subject = county,
                Counterpart = other,
                Culture = cultures.For(county),
                CounterpartCulture = cultures.For(other),
                Faith = faiths.For(county),
                CounterpartFaith = faiths.For(other),
                Tension = 3,
                Text = Fill(attacking ? WarAttack : WarDefend, county, string.Empty, year, other.Name),
            });
        }
    }

    private static void Sanctity(
        ChronicleMap map, Title county, Faith faith, MapConfig cfg, Rng rng)
    {
        int year = cfg.StartYear - rng.Int(150, 500);

        map.Add(new ChronicleEvent
        {
            Kind = ChronicleKind.Sanctity,
            Year = year,
            Subject = county,
            Faith = faith,
            Text = Fill(rng.Pick(Sanctities), county, faith.Name, year),
        });
    }

    private static void Wonder(
        ChronicleMap map, Title county, GeneratedWonder wonder, MapConfig cfg, Rng rng)
    {
        int year = cfg.StartYear - rng.Int(80, 350);

        map.Add(new ChronicleEvent
        {
            Kind = ChronicleKind.Wonder,
            Year = year,
            Subject = county,
            Text = Fill(rng.Pick(Wonders), county, string.Empty, year, wonder.Name),
        });
    }

    private static void Relic(
        ChronicleMap map, Title county, GeneratedArtifact relic, MapConfig cfg, Rng rng)
    {
        int year = cfg.StartYear - rng.Int(30, 260);

        map.Add(new ChronicleEvent
        {
            Kind = ChronicleKind.Relic,
            Year = year,
            Subject = county,
            Text = Fill(rng.Pick(Relics), county, string.Empty, year, relic.LocalizedName),
        });
    }

    /// <summary>
    /// The opening line of a duchy, kingdom or empire: what it is made of and who lives in it.
    ///
    /// Undated on purpose. A de jure title is not an event — it is a claim about how the ground is
    /// meant to be divided, and giving it a founding year would assert something the generator has
    /// not decided and the map cannot support.
    /// </summary>
    private static void Realm(
        ChronicleMap map, Title title, CultureMap cultures, FaithMap faiths,
        WildernessMap wilderness, MapConfig cfg, Rng rng)
    {
        var held = Titles.Flatten([title])
            .Where(t => t.Tier == "c" && !wilderness.Contains(t))
            .ToList();

        if (held.Count == 0) return;

        var peoples = held.Select(cultures.For).Select(c => c.Heritage).Distinct().Count();
        var religions = held.Select(faiths.For).Select(f => f.Religion).Distinct().Count();

        // A title down to its last settled county is not described by counting it: "1 counties"
        // is wrong, and "one county" is barely better, because the interesting fact about such a
        // title is which county it is rather than how many there are. So the singular bank names
        // it instead, and {OTHER} carries the name rather than the tally.
        //
        // One bank rather than a pair. The two below split on whether more than one people or
        // faith lives in the title, and a single county cannot manage either, so a divided variant
        // would be unreachable.
        bool remnant = held.Count == 1;

        string[] bank = remnant ? RealmRemnant
            : peoples > 1 || religions > 1 ? RealmDivided
            : RealmWhole;

        map.Add(new ChronicleEvent
        {
            Kind = ChronicleKind.Realm,
            Year = cfg.StartYear,
            Subject = title,
            Culture = cultures.For(title),
            Faith = faiths.For(title),
            Text = Fill(rng.Pick(bank), title, cultures.For(title).Name, cfg.StartYear,
                remnant ? held[0].Name : held.Count.ToString()),
        });
    }

    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Pulls the year out of a Paradox <c>Y.M.D</c> date. Null rather than throwing: these strings
    /// come from another generator's output, and a chronicle is not worth failing a run over.
    /// </summary>
    private static int? YearOf(string? date)
        => int.TryParse(date?.Split('.').FirstOrDefault(), out int y) ? y : null;

    /// <summary>
    /// Fills a template and escapes the result once, at the end.
    ///
    /// Escaping here rather than at the call sites is deliberate: every value that reaches a
    /// template is a generated name, and generated names are the only thing in this file that can
    /// contain a quote. One choke point means one place to be wrong.
    /// </summary>
    private static string Fill(string template, Title subject, string who, int year, string? other = null)
        => Io.ParadoxText.Loc(template
            .Replace("{PLACE}", subject.Name)
            .Replace("{WHO}", who)
            .Replace("{OTHER}", other ?? string.Empty)
            .Replace("{YEAR}", year.ToString()));

    // ---------------------------------------------------------------------------------------
    // Prose banks. Several per event so a kingdom's worth of counties does not read as one
    // sentence repeated, and none of them naming anything the map has not already decided.
    // ---------------------------------------------------------------------------------------

    private static readonly string[] SettledRich =
    [
        "The {WHO} have worked {PLACE} since {YEAR}, and the fields have not gone fallow once.",
        "{PLACE} was settled by the {WHO} in {YEAR}. They came for the soil and found rather more than soil.",
        "Since {YEAR} the {WHO} have held {PLACE}, long enough that nobody alive remembers it otherwise.",
        "The {WHO} put down roots at {PLACE} in {YEAR}, and the roots took.",
    ];

    private static readonly string[] SettledPlain =
    [
        "The {WHO} came to {PLACE} in {YEAR} and stayed.",
        "{PLACE} has been {WHO} ground since {YEAR}, without much fuss about it either way.",
        "Settlement at {PLACE} dates to {YEAR}, when the {WHO} arrived and found no one arguing.",
        "The {WHO} have counted {PLACE} as theirs since {YEAR}.",
    ];

    private static readonly string[] SettledHard =
    [
        "The {WHO} reached {PLACE} in {YEAR}. It has never been generous to them.",
        "{PLACE} was settled in {YEAR} by {WHO} who had run out of better options.",
        "The {WHO} have endured {PLACE} since {YEAR}, which is the most that can be said for it.",
        "Since {YEAR} the {WHO} have scratched a living from {PLACE}, and it has never come easily.",
    ];

    private static readonly string[] Frontiers =
    [
        "{PLACE} has been a border since {YEAR}, when the {WHO} and the {OTHER} first disagreed about where it ran.",
        "The {WHO} and the {OTHER} have both called {PLACE} theirs since {YEAR}. Neither has stopped.",
        "In {YEAR} the {OTHER} came as far as {PLACE} and got no further. The {WHO} have not forgotten how close it was.",
        "{PLACE} changed hands between the {WHO} and the {OTHER} more than once after {YEAR}, and the memory has outlasted the fighting.",
    ];

    private static readonly string[] FaithsQuiet =
    [
        "{PLACE} has kept to {WHO} since {YEAR}.",
        "The {WHO} faith reached {PLACE} in {YEAR} and met no resistance worth recording.",
        "Since {YEAR} the people of {PLACE} have followed {WHO}, and have not been asked to reconsider.",
    ];

    private static readonly string[] FaithsContested =
    [
        "{WHO} took {PLACE} in {YEAR}, and the {OTHER} across the valley have never accepted it.",
        "The people of {PLACE} turned to {WHO} in {YEAR}. Their neighbours kept to {OTHER}, and say so loudly.",
        "Since {YEAR} {PLACE} has held to {WHO} while {OTHER} is preached a day's ride away. Both sides consider this a temporary arrangement.",
    ];

    private static readonly string[] Seats =
    [
        "House {WHO} has held the seat at {PLACE} since {YEAR}.",
        "The {WHO} took {PLACE} in {YEAR} and have not been dislodged.",
        "{PLACE} has answered to House {WHO} since {YEAR}, through better lords and worse.",
    ];

    private const string FeudLine =
        "The feud with House {WHO} opened in {YEAR} at {PLACE}, and has never once been settled.";

    private const string RivalryLine =
        "{PLACE} and House {WHO} have been on poor terms since {YEAR}, though it has not yet come to blood.";

    private const string WarAttack =
        "In {YEAR} the levies of {PLACE} marched on {OTHER}. They have not come home.";

    private const string WarDefend =
        "{OTHER} marched on {PLACE} in {YEAR}, and the matter is still being settled.";

    private static readonly string[] Sanctities =
    [
        "{PLACE} has been holy to {WHO} since {YEAR}, and pilgrims have worn the road smooth.",
        "Something happened at {PLACE} in {YEAR}. {WHO} has considered the ground sacred ever since.",
        "The {WHO} have counted {PLACE} among their holy places since {YEAR}.",
    ];

    private static readonly string[] Wonders =
    [
        "{OTHER} was raised at {PLACE} around {YEAR}, and has outlasted everyone who argued against it.",
        "Work on {OTHER} at {PLACE} finished in {YEAR}. Nobody has built its equal since.",
        "{PLACE} has been known for {OTHER} since {YEAR}, and for very little else.",
    ];

    private static readonly string[] Relics =
    [
        "{OTHER} came to {PLACE} in {YEAR} and has not left.",
        "Since {YEAR} {OTHER} has been kept at {PLACE}, guarded rather more carefully than the people are.",
        "{PLACE} has held {OTHER} since {YEAR}. Several parties would prefer it did not.",
    ];

    private static readonly string[] RealmWhole =
    [
        "{PLACE} gathers {OTHER} counties, and the {WHO} are at home in all of them.",
        "{OTHER} counties answer to {PLACE}. They share a tongue and mostly share a god.",
        "{PLACE} is {OTHER} counties of largely {WHO} ground, which has made it easier to govern than most.",
    ];

    private static readonly string[] RealmDivided =
    [
        "{PLACE} gathers {OTHER} counties that have never agreed on much, the {WHO} loudest among them.",
        "{OTHER} counties answer to {PLACE}, and they do not answer to the same god or in the same tongue.",
        "{PLACE} holds {OTHER} counties together on paper. The {WHO} are the largest party to the argument, not the only one.",
    ];

    /// <summary>
    /// A title with one settled county in it, where <c>{OTHER}</c> is that county's name rather
    /// than a count. Reachable at any tier: the wilderness eats counties, not titles, so a duchy
    /// drawn with six of them can arrive here with one, and so, more rarely, can a kingdom.
    /// </summary>
    private static readonly string[] RealmRemnant =
    [
        "Only {OTHER} answers to {PLACE}. The rest of it is ground nobody has got round to settling.",
        "{PLACE} comes to {OTHER} and a great deal of country nobody lives in, which makes the {WHO} there easy to govern and hard to tax.",
        "Whatever {PLACE} was drawn to be, in practice it is {OTHER}, the {WHO} who live there, and wilderness for the rest.",
    ];
}
