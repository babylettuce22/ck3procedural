using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen;

/// <summary>
/// Lays a piece of the real world onto a generated map: every title takes a vanilla title's key,
/// every county the culture and faith its vanilla county had at the start date, and the realms
/// are vanilla's own at that date. Runs on a world of vanilla identities
/// (<see cref="Config.MapConfig.ContentSourceMode.VanillaWorld"/>); nothing here is invented — a
/// title keeps a generated key only when vanilla has run out of titles of its tier, which no map
/// this generator makes comes near.
///
/// <b>Where.</b> A window on vanilla's map is chosen that holds about as many counties as this map
/// has, shaped like this map's land. It is what a small map is limited by — it fits France, not
/// Europe — and the point: the realms that fit are whole, at vanilla's own grain. A few seeded
/// windows are tried and the one whose peoples best suit this map's ground (the traditions, dress
/// and building fit <see cref="VanillaIdentities"/> uses) wins.
///
/// <b>What, top down.</b> This map's own de jure tree is kept — it was grown on this map's land and
/// every title in it is one piece — and vanilla's is laid over it a tier at a time: each empire
/// takes a vanilla empire from the window, each of its kingdoms one of that empire's kingdoms, each
/// duchy one of that kingdom's duchies, each county one of that duchy's counties, chosen within
/// the parent by where each sits and how big it is. So Normandy is one piece and it is inside
/// France. Where the counts do not line up, a title borrows the nearest unused vanilla title of its
/// tier — the one place vanilla's membership bends.
///
/// Two ways of doing it that failed, measured, before this one: projecting counties onto the
/// window and regrouping them under vanilla's tree gave kingdoms that were one piece one time in
/// seven (France in four places), because this map's coasts are not vanilla's; keeping this tree
/// and naming each title by a vote of its counties kept titles whole but put d_romagna in Hungary.
///
/// <b>Who.</b> The realms are vanilla's at the start date (<see cref="Countries"/>), each ruler
/// seated where vanilla's real ruler of that realm would be at home.
/// </summary>
public static class VanillaTitles
{
    /// <summary>Vanilla counties in the window per county of this map.</summary>
    private const double Density = 1.05;

    /// <summary>Seeded windows tried before the best-suited is kept.</summary>
    private const int Windows = 8;

    public sealed class Plan
    {
        public required string Date { get; init; }
        public required string Region { get; init; }

        /// <summary>Each of this map's titles and the vanilla title it becomes.</summary>
        public Dictionary<Title, VanillaCatalog.TitleDef> Borrowed { get; } = [];

        /// <summary>Each county's (culture, faith) as its vanilla county stood at <see cref="Date"/>.</summary>
        public Dictionary<Title, (string Culture, string Faith)> State { get; } = [];

        /// <summary>Where each county of this map lands on vanilla's: its vanilla county's place.</summary>
        public Dictionary<Title, (double X, double Y)> Projected { get; } = [];
    }

    private sealed record Window(List<VanillaCatalog.TitleDef> Counties, (double X, double Y) Centre,
        VanillaCatalog.TitleDef CentreCounty);

    public static Plan? Match(List<Title> empires, CultureMap grown, Dictionary<Title, int> development,
        VanillaCatalog catalog, Func<Title, (double X, double Y)?> position, int year,
        IReadOnlySet<string> reserved, Rng rng)
    {
        string date = $"{Math.Max(1, year)}.1.1";
        var counties = Titles.Flatten(empires).Where(t => t.Tier == "c").ToList();
        var at = new Dictionary<Title, (double X, double Y)>();
        foreach (var county in counties)
            if (position(county) is { } p) at[county] = p;
        if (at.Count == 0) return null;

        var eligible = new Eligible(catalog, reserved);
        if (eligible.Counties.Count < counties.Count) return null;

        double gx0 = at.Values.Min(p => p.X), gx1 = at.Values.Max(p => p.X);
        double gy0 = at.Values.Min(p => p.Y), gy1 = at.Values.Max(p => p.Y);
        double aspect = Math.Max(1, gx1 - gx0) / Math.Max(1, gy1 - gy0);
        int wanted = Math.Min(eligible.Counties.Count, (int)Math.Ceiling(counties.Count * Density));

        // Where a title of this map sits: the mean of its counties. A county with no land province
        // sits at the middle of the map.
        (double X, double Y) middle = ((gx0 + gx1) / 2, (gy0 + gy1) / 2);
        (double X, double Y) Local(Title title)
        {
            var mine = Titles.Flatten([title]).Where(c => c.Tier == "c").Select(c => at.TryGetValue(c, out var p) ? p : middle).ToList();
            return mine.Count == 0 ? middle : (mine.Average(p => p.X), mine.Average(p => p.Y));
        }

        Window MakeWindow(VanillaCatalog.TitleDef centre)
        {
            var (cx, cy) = centre.Home!.Value;

            // The smallest rectangle of this map's shape, centred there, that holds enough counties.
            double lo = 1, hi = 20000;
            for (int i = 0; i < 40; i++)
            {
                double mid = (lo + hi) / 2;
                int inside = eligible.Counties.Count(t => Math.Abs(t.Home!.Value.X - cx) <= mid && Math.Abs(t.Home!.Value.Y - cy) <= mid / aspect);
                if (inside >= wanted) hi = mid; else lo = mid;
            }

            var inWindow = eligible.Counties.Where(t => Math.Abs(t.Home!.Value.X - cx) <= hi
                                                        && Math.Abs(t.Home!.Value.Y - cy) <= hi / aspect).ToList();
            return new Window(inWindow, (cx, cy), centre);
        }

        double FitOf(Dictionary<Title, VanillaCatalog.TitleDef> assignment)
        {
            double total = 0;
            int n = 0;
            foreach (var (county, v) in assignment)
            {
                if (county.Tier != "c" || !grown.ByCounty.TryGetValue(county, out var mine) || mine.Inherited) continue;
                if (catalog.StateOf(v, date) is not { } state || !catalog.Cultures.TryGetValue(state.Culture, out var theirs)) continue;
                total += VanillaIdentities.Fit(mine, theirs);
                n++;
            }
            return n == 0 ? 0 : total / n;
        }

        var centres = new List<VanillaCatalog.TitleDef>();
        var remaining = eligible.Counties.ToList();
        for (int i = 0; i < Windows && remaining.Count > 0; i++)
        {
            int k = rng.Int(0, remaining.Count - 1);
            centres.Add(remaining[k]);
            remaining.RemoveAt(k);
        }

        (Window Window, Dictionary<Title, VanillaCatalog.TitleDef> Titles, double Fit)? best = null;
        foreach (var centre in centres)
        {
            var window = MakeWindow(centre);
            var assignment = TopDown(empires, window, eligible, catalog, Local);
            double fit = FitOf(assignment);
            if (best is null || fit > best.Value.Fit) best = (window, assignment, fit);
        }

        var chosen = best!.Value;
        var plan = new Plan { Date = date, Region = RegionName(catalog, chosen.Window.CentreCounty) };
        foreach (var (title, v) in chosen.Titles)
        {
            plan.Borrowed[title] = v;
            if (title.Tier != "c") continue;
            if (v.Home is { } home) plan.Projected[title] = home;
            if (catalog.StateOf(v, date) is { } state) plan.State[title] = state;
        }

        AssignBaronies(plan, catalog);
        return plan;
    }

    /// <summary>The vanilla titles a map title may become: landed, placed on vanilla's map, holding
    /// at least one county with a history and baronies, and not reserved.</summary>
    private sealed class Eligible
    {
        public List<VanillaCatalog.TitleDef> Counties { get; }
        private readonly Dictionary<string, int> _size = new(StringComparer.Ordinal);
        private readonly VanillaCatalog _catalog;
        private readonly IReadOnlySet<string> _reserved;

        public Eligible(VanillaCatalog catalog, IReadOnlySet<string> reserved)
        {
            _catalog = catalog;
            _reserved = reserved;
            Counties = catalog.Titles.Values
                .Where(t => t.Tier == 'c' && !t.Landless && t.Home is not null && t.Children.Count > 0
                            && t.States.Count > 0 && !reserved.Contains(t.Key))
                .OrderBy(t => t.Key, StringComparer.Ordinal).ToList();
        }

        /// <summary>Eligible counties under a vanilla title — its size, for matching like with like.</summary>
        public int Size(VanillaCatalog.TitleDef def)
        {
            if (_size.TryGetValue(def.Key, out int n)) return n;
            n = def.Tier == 'c'
                ? (Is(def) ? 1 : 0)
                : def.Children.Where(_catalog.Titles.ContainsKey).Sum(k => Size(_catalog.Titles[k]));
            _size[def.Key] = n;
            return n;
        }

        public bool Is(VanillaCatalog.TitleDef def)
            => !def.Landless && def.Home is not null && !_reserved.Contains(def.Key)
               && (def.Tier == 'c' ? def.Children.Count > 0 && def.States.Count > 0 : Size(def) > 0);

        public IEnumerable<VanillaCatalog.TitleDef> OfTier(char tier)
            => _catalog.Titles.Values.Where(t => t.Tier == tier && Is(t));
    }

    /// <summary>
    /// The top-down match, a tier at a time: this map's empires to vanilla empires present in the
    /// window, then within each, its children to the matched title's children. See the class remarks.
    /// </summary>
    private static Dictionary<Title, VanillaCatalog.TitleDef> TopDown(List<Title> empires, Window window,
        Eligible eligible, VanillaCatalog catalog, Func<Title, (double X, double Y)> local)
    {
        var result = new Dictionary<Title, VanillaCatalog.TitleDef>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        var inWindow = window.Counties.ToHashSet();

        int MapSize(Title t) => Titles.Flatten([t]).Count(c => c.Tier == "c");

        var touches = new Dictionary<string, bool>(StringComparer.Ordinal);
        bool Touches(VanillaCatalog.TitleDef d)
        {
            if (touches.TryGetValue(d.Key, out bool known)) return known;
            bool inside = d.Tier == 'c' ? inWindow.Contains(d) : Flatten(catalog, d).Any(inWindow.Contains);
            touches[d.Key] = inside;
            return inside;
        }

        // Where a vanilla empire is, as far as this window is concerned: its counties inside it.
        (double X, double Y) WindowHome(VanillaCatalog.TitleDef empire)
        {
            var mine = Flatten(catalog, empire).Where(inWindow.Contains).Select(c => c.Home!.Value).ToList();
            return mine.Count > 0 ? (mine.Average(p => p.X), mine.Average(p => p.Y)) : empire.Home!.Value;
        }

        void Level(List<Title> mine, List<VanillaCatalog.TitleDef> offered, char tier, (double X, double Y) anchor,
            Func<VanillaCatalog.TitleDef, (double X, double Y)> homeOf)
        {
            if (mine.Count == 0) return;

            var candidates = offered.Where(d => !used.Contains(d.Key) && eligible.Is(d)).ToList();

            // Too few: the nearest unused titles of the tier — inside the window first, so a kingdom
            // short of duchies borrows its neighbour's rather than one from across the world.
            if (candidates.Count < mine.Count)
                candidates.AddRange(eligible.OfTier(tier)
                    .Where(d => !used.Contains(d.Key) && !candidates.Contains(d))
                    .OrderBy(d => Touches(d) ? 0 : 1)
                    .ThenBy(d => Distance2(d.Home!.Value, anchor)).ThenBy(d => d.Key, StringComparer.Ordinal)
                    .Take(mine.Count - candidates.Count));

            var here = VanillaIdentities.Normalise(mine.ToDictionary(t => t, local));
            var there = VanillaIdentities.Normalise(candidates.ToDictionary(d => d, homeOf));

            double Cost(Title t, VanillaCatalog.TitleDef d)
                => VanillaIdentities.Distance(here[t], there[d])
                   + (tier == 'c' ? 0 : 0.5 * Math.Abs(Math.Log((MapSize(t) + 1.0) / (eligible.Size(d) + 1.0))));

            var order = mine.OrderByDescending(MapSize).ThenBy(t => t.Index).ToList();
            foreach (var (title, def) in VanillaIdentities.Assign(order, candidates, Cost))
            {
                if (!used.Add(def.Key)) continue;
                result[title] = def;

                char next = tier switch { 'e' => 'k', 'k' => 'd', 'd' => 'c', _ => ' ' };
                if (next == ' ') continue;

                var children = title.Children.Where(c => c.Tier == next.ToString()).ToList();
                var theirs = def.Children.Where(catalog.Titles.ContainsKey).Select(k => catalog.Titles[k])
                                         .Where(k => k.Tier == next).ToList();
                Level(children, theirs, next, def.Home!.Value, d => d.Home!.Value);
            }
        }

        // The window's empires, the ones most present first; the nearest others if it holds too few.
        var present = eligible.OfTier('e')
            .Select(e => (Empire: e, Inside: Flatten(catalog, e).Count(inWindow.Contains)))
            .Where(x => x.Inside > 0)
            .OrderByDescending(x => x.Inside).ThenBy(x => x.Empire.Key, StringComparer.Ordinal)
            .Select(x => x.Empire).ToList();

        Level(empires.Where(e => e.Tier == "e").ToList(), present, 'e', window.Centre, WindowHome);
        return result;
    }

    /// <summary>The eligible-or-not counties under a vanilla title.</summary>
    private static IEnumerable<VanillaCatalog.TitleDef> Flatten(VanillaCatalog catalog, VanillaCatalog.TitleDef def)
    {
        foreach (string key in def.Children)
        {
            if (!catalog.Titles.TryGetValue(key, out var child)) continue;
            if (child.Tier == 'c') yield return child;
            else foreach (var c in Flatten(catalog, child)) yield return c;
        }
    }

    /// <summary>
    /// A county's baronies take its vanilla county's, the seat first onto vanilla's capital barony.
    /// A county with more baronies than its vanilla one borrows the nearest baronies of vanilla
    /// counties this map did not use, so no key is ever invented and none is used twice.
    /// </summary>
    private static void AssignBaronies(Plan plan, VanillaCatalog catalog)
    {
        var matched = plan.Borrowed.Values.Select(v => v.Key).ToHashSet(StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);
        var spare = catalog.Titles.Values
            .Where(t => t.Tier == 'b' && t.Home is not null && t.Parent is { } parent && !matched.Contains(parent))
            .OrderBy(t => t.Key, StringComparer.Ordinal).ToList();

        foreach (var (county, v) in plan.Borrowed.Where(kv => kv.Key.Tier == "c").OrderBy(kv => kv.Key.Index).ToList())
        {
            var mine = county.SeatFirst().Where(b => b.Tier == "b").ToList();
            var theirs = v.Children.Where(k => catalog.Titles.TryGetValue(k, out var b) && b.Tier == 'b').ToList();

            for (int i = 0; i < mine.Count; i++)
            {
                VanillaCatalog.TitleDef? barony = null;
                if (i < theirs.Count && used.Add(theirs[i])) barony = catalog.Titles[theirs[i]];
                else
                {
                    var home = v.Home!.Value;
                    barony = spare.Where(b => !used.Contains(b.Key))
                                  .OrderBy(b => Distance2(b.Home!.Value, home)).ThenBy(b => b.Key, StringComparer.Ordinal)
                                  .FirstOrDefault();
                    if (barony is not null) used.Add(barony.Key);
                }

                if (barony is not null) plan.Borrowed[mine[i]] = barony;
            }
        }
    }

    /// <summary>Puts the plan onto the titles: vanilla keys, names, colours and declarations.</summary>
    public static void Apply(Plan plan, List<Title> empires)
    {
        foreach (var (title, def) in plan.Borrowed)
        {
            title.Key = def.Key;
            title.Name = def.Name;
            if (def.Color is { } colour) title.Color = colour;
            title.Inherited = true;
            title.InheritedFields = def.Fields;
        }

        var all = Titles.Flatten(empires).Where(t => t.Tier != "h").ToList();
        int kept = all.Count(t => !t.Inherited);
        Console.WriteLine($"  vanilla titles: {plan.Borrowed.Count} titles laid onto the map {plan.Region} as of {plan.Date} " +
                          $"({string.Join(", ", all.Where(t => t.Inherited && t.Tier is "k" or "e").Take(10).Select(t => t.Name))})" +
                          (kept > 0 ? $"; {kept} kept generated keys (vanilla ran out of titles of their tier)" : ""));
    }

    /// <summary>
    /// After <see cref="Capitals.SeatRealms"/> has chosen capitals by development: every vanilla
    /// title whose vanilla capital county is inside it on this map takes that capital instead —
    /// France is ruled from Paris — with each title on the way down seated towards it.
    /// </summary>
    public static int SeatLikeVanilla(List<Title> empires, VanillaCatalog catalog)
    {
        var byKey = Titles.Flatten(empires).Where(t => t.Tier == "c")
                          .GroupBy(t => t.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        int moved = 0;

        // Top down, so a kingdom's choice is not undone by the duchy seated after it — a duchy's
        // own vanilla capital wins inside the duchy, the kingdom's decides which duchy leads.
        foreach (var title in Titles.Flatten(empires).Where(t => t.Inherited && t.Tier is "d" or "k" or "e"))
        {
            if (!catalog.Titles.TryGetValue(title.Key, out var def) || def.Capital is not { } key) continue;
            if (!byKey.TryGetValue(key, out var capital)) continue;

            var path = new List<Title>();
            for (var t = capital; t is not null && t != title; t = t.Parent) path.Add(t);
            if (path.Count == 0 || path[^1].Parent != title) continue;

            if (title.Seat != path[^1]) moved++;
            title.Seat = path[^1];
        }

        return moved;
    }

    /// <summary>
    /// Vanilla's own realms at the plan's date, as countries for <see cref="Realms.Build"/>: every
    /// county whose vanilla county was held belongs to its holder's top liege's realm, and each
    /// realm's top title is the highest title its ruler held that this map has. Titles held in
    /// vanilla by someone under the realm's ruler — a duke under a king — are its vassal titles.
    /// </summary>
    public static StatedCountries? Countries(Plan plan, VanillaCatalog catalog, WildernessMap wilderness,
        List<Title> empires, Dictionary<Title, int> development)
    {
        var (holder, liegeOf) = HoldersAt(catalog, plan.Date);

        var heldBy = holder.GroupBy(kv => kv.Value, StringComparer.Ordinal)
                           .ToDictionary(g => g.Key, g => g.Select(kv => kv.Key).OrderBy(TierRank).Reverse().ToList(),
                                         StringComparer.Ordinal);

        // A character's liege is their primary title's: only the titles of the highest tier they
        // hold count. A lower title's liege is often a stale entry from centuries before — Simeon of
        // Bulgaria holds a county whose history last named a liege in 681, the Byzantine Empire —
        // and reading it would fold a kingdom into an empire it was at war with.
        string? LordOf(string character)
        {
            var held = heldBy.GetValueOrDefault(character) ?? [];
            if (held.Count == 0) return null;
            int top = TierRank(held[0]);
            foreach (string title in held.Where(t => TierRank(t) == top))
                if (liegeOf.TryGetValue(title, out string? above) && holder.TryGetValue(above, out string? lord) && lord != character)
                    return lord;
            return null;
        }

        string TopOf(string character)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal) { character };
            while (LordOf(character) is { } up && seen.Add(up)) character = up;
            return character;
        }

        var ours = plan.Borrowed.ToDictionary(kv => kv.Value.Key, kv => kv.Key, StringComparer.Ordinal);
        var realmOf = new Dictionary<Title, string>();
        foreach (var (title, def) in plan.Borrowed)
            if (title.Tier == "c" && !wilderness.Contains(title) && holder.TryGetValue(def.Key, out string? h))
                realmOf[title] = TopOf(h);
        if (realmOf.Count == 0) return null;

        var realms = realmOf.GroupBy(kv => kv.Value, StringComparer.Ordinal)
                            .Select(g => (Top: g.Key, Counties: g.Select(kv => kv.Key).OrderBy(c => c.Index).ToList()))
                            .OrderBy(r => r.Counties[0].Index).ToList();

        var titles = new Dictionary<int, Title>();
        var seats = new Dictionary<int, Title>();
        var idOf = new Dictionary<string, int>(StringComparer.Ordinal);
        var chosen = new HashSet<Title>();
        var landed = Titles.Flatten(empires).Where(t => t.Tier is "d" or "k" or "e").ToList();

        foreach (var (top, members) in realms)
        {
            var inside = members.ToHashSet();
            int Covered(Title t) => Titles.Flatten([t]).Count(c => c.Tier == "c" && inside.Contains(c));

            // The highest title the ruler held that this map has; else the title this realm covers
            // most of; else its best county.
            var pick = (heldBy.GetValueOrDefault(top) ?? [])
                           .Where(ours.ContainsKey).Select(k => ours[k])
                           .Where(t => !chosen.Contains(t) && Covered(t) > 0)
                           .OrderByDescending(t => Realms.Rank(t)).ThenByDescending(Covered).FirstOrDefault()
                       ?? landed.Where(t => !chosen.Contains(t))
                                .Select(t => (Title: t, Covered: Covered(t),
                                              Share: Covered(t) / (double)Math.Max(1, Titles.Flatten([t]).Count(c => c.Tier == "c" && !wilderness.Contains(c)))))
                                .Where(x => x.Covered > 0 && x.Share >= 0.5)
                                .OrderByDescending(x => Realms.Rank(x.Title)).ThenByDescending(x => x.Covered).ThenBy(x => x.Title.Index)
                                .Select(x => x.Title).FirstOrDefault()
                       ?? members.FirstOrDefault(c => !chosen.Contains(c));
            if (pick is null) continue;

            chosen.Add(pick);
            int id = titles.Count + 1;
            idOf[top] = id;
            titles[id] = pick;

            // Where the ruler sits, and so whose people and faith the ruler is — a ruler here takes
            // the culture and faith of the county they sit in. In order: the title's own vanilla
            // capital when it is in the realm here; else a county of the real ruler's own people
            // and faith (vanilla's history says who that was: the Byzantine emperor is Greek and
            // Orthodox, whatever his demesne in Apulia spoke), those he held in person first; else
            // one of his people; else any he held in person; else the realm's commonest people.
            // Only counties under the top title can be a seat — the realm is realised down its de
            // jure tree.
            var under = members.Where(c => IsUnder(c, pick)).ToList();
            string? capital = catalog.Titles.GetValueOrDefault(pick.Key)?.Capital;
            var (rulerCulture, rulerFaith) = catalog.CharacterAt(top, plan.Date) ?? (null, null);
            string? common = under.Where(plan.State.ContainsKey).GroupBy(c => plan.State[c].Culture)
                                  .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
                                  .Select(g => g.Key).FirstOrDefault();

            bool Held(Title c) => holder.GetValueOrDefault(plan.Borrowed[c].Key) == top;
            bool Of(Title c, string? culture, string? faith)
                => plan.State.TryGetValue(c, out var s) && (culture is null || s.Culture == culture)
                                                        && (faith is null || s.Faith == faith);
            Title? Richest(Func<Title, bool> where)
                => under.Where(where).OrderByDescending(Held).ThenByDescending(c => development.GetValueOrDefault(c))
                        .ThenBy(c => c.Index).FirstOrDefault();

            var seat = under.FirstOrDefault(c => c.Key == capital)
                       ?? (rulerCulture is null ? null : Richest(c => Of(c, rulerCulture, rulerFaith)))
                       ?? (rulerCulture is null ? null : Richest(c => Of(c, rulerCulture, null)))
                       ?? Richest(Held)
                       ?? Richest(c => Of(c, common, null));
            if (seat is not null) seats[id] = seat;
        }

        // A duke under a king, a king under an emperor: held in vanilla by someone other than the
        // ruler of the realm that someone belongs to.
        var vassals = new HashSet<Title>();
        foreach (var (key, title) in ours)
        {
            if (title.Tier is not ("d" or "k") || chosen.Contains(title)) continue;
            if (!holder.TryGetValue(key, out string? h)) continue;
            string top = TopOf(h);
            if (top != h && idOf.ContainsKey(top)) vassals.Add(title);
        }

        Console.WriteLine($"  vanilla realms: {titles.Count} independent realms as of {plan.Date} " +
                          $"({string.Join(", ", titles.Values.OrderByDescending(Realms.Rank).Take(10).Select(t => t.Name))}" +
                          (titles.Count > 10 ? ", ..." : "") + $"), {vassals.Count} vassal titles");

        return new StatedCountries
        {
            Titles = titles,
            CountryOf = county => realmOf.TryGetValue(county, out string? top) && idOf.TryGetValue(top, out int id) ? id : null,
            VassalTitles = vassals,
            Seats = seats,
            Source = "vanilla realms",
        };
    }

    /// <summary>
    /// Who held each vanilla title on <paramref name="date"/>, and the title each was a vassal of.
    /// Holder and liege persist independently until history changes them; <c>0</c> means none.
    /// </summary>
    public static (Dictionary<string, string> Holder, Dictionary<string, string> Liege) HoldersAt(
        VanillaCatalog catalog, string date)
    {
        var holder = new Dictionary<string, string>(StringComparer.Ordinal);
        var liege = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, events) in catalog.TitleHistory)
        {
            string? h = null, l = null;
            foreach (var e in events)
            {
                if (!VanillaCatalog.OnOrBefore(e.Date, date)) break;
                if (e.Holder is not null) h = e.Holder;
                if (e.Liege is not null) l = e.Liege;
            }
            if (h is not null && h != "0") holder[key] = h;
            if (l is not null && l != "0") liege[key] = l;
        }
        return (holder, liege);
    }

    /// <summary>
    /// How whole the real world came out: for vanilla's duchies, kingdoms and empires on this map,
    /// and for the realms, the share that is one connected piece and the mean number of pieces.
    /// The measure the county matching is judged by — a France in four places is the failure it
    /// exists to catch. Counties are joined by the realm pass's adjacency (land, and short sea hops).
    /// </summary>
    public static string Fragmentation(List<Title> empires, RealmMap realms)
    {
        if (realms.CountyAdjacency is not { } adjacency) return "no county adjacency to measure against";

        int Pieces(HashSet<Title> set)
        {
            var seen = new HashSet<Title>();
            int pieces = 0;
            foreach (var start in set)
            {
                if (!seen.Add(start)) continue;
                pieces++;
                var stack = new Stack<Title>([start]);
                while (stack.Count > 0)
                    foreach (var next in adjacency.GetValueOrDefault(stack.Pop()) ?? [])
                        if (set.Contains(next) && seen.Add(next)) stack.Push(next);
            }
            return pieces;
        }

        string Report(string label, IEnumerable<HashSet<Title>> groups)
        {
            var sizes = groups.Where(g => g.Count > 0).Select(Pieces).ToList();
            if (sizes.Count == 0) return $"{label} none";
            return $"{label} {sizes.Count(p => p == 1) * 100 / sizes.Count}% whole, {sizes.Average():F1} pieces";
        }

        HashSet<Title> Counties(Title t) => Titles.Flatten([t]).Where(c => c.Tier == "c" && adjacency.ContainsKey(c)).ToHashSet();

        var all = Titles.Flatten(empires).Where(t => t.Inherited).ToList();

        // A realm is everything whose holder's liege chain ends at the same top title.
        Title TopOf(Title county)
        {
            var title = Emit.HistoryWriter.Primary(realms.HolderCounty.GetValueOrDefault(county, county), realms);
            var seen = new HashSet<Title>();
            while (realms.Liege.TryGetValue(title, out var up) && seen.Add(up)) title = up;
            return title;
        }
        var byRealm = adjacency.Keys.GroupBy(TopOf).Select(g => g.ToHashSet());

        return string.Join("; ",
            Report("duchies", all.Where(t => t.Tier == "d").Select(Counties)),
            Report("kingdoms", all.Where(t => t.Tier == "k").Select(Counties)),
            Report("empires", all.Where(t => t.Tier == "e").Select(Counties)),
            Report("realms", byRealm));
    }

    private static bool IsUnder(Title title, Title ancestor)
    {
        for (var t = title; t is not null; t = t.Parent) if (t == ancestor) return true;
        return false;
    }

    private static int TierRank(string key) => key.Length > 1 ? key[0] switch
    {
        'h' => 5, 'e' => 4, 'k' => 3, 'd' => 2, 'c' => 1, _ => 0,
    } : 0;

    private static string RegionName(VanillaCatalog catalog, VanillaCatalog.TitleDef county)
    {
        for (string? up = county.Parent; up is not null; up = catalog.Titles.GetValueOrDefault(up)?.Parent)
            if (up[0] == 'k' && catalog.Titles.TryGetValue(up, out var kingdom)) return $"around {kingdom.Name}";
        return $"around {county.Name}";
    }

    private static double Distance2((double X, double Y) a, (double X, double Y) b)
        => (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);
}
