using System.Globalization;
using System.Text.RegularExpressions;
using Ck3MapGen.GameGui;
using Ck3MapGen.Io;

namespace Ck3MapGen.MapGen;

/// <summary>
/// The base game's peoples and faiths, read whole out of the installed game, for a world settled by
/// vanilla identities rather than generated ones (<see cref="Config.MapConfig.ContentSource"/>).
///
/// Not <see cref="VanillaVocabulary"/>, and deliberately separate from it. The vocabulary harvests
/// <em>ingredients</em> — ethos, traditions, looks — for building new cultures, and loses which
/// culture each one came from on the way in. This keeps every definition as a unit under its own
/// key, because the whole point here is to reference that key and let the game supply the rest.
///
/// Everything the placement pass needs to choose well is read from the install too, never listed
/// by hand:
/// <list type="bullet">
/// <item>which cultures and faiths vanilla actually seats anywhere (province history), so a
/// template culture nobody lives in is never placed;</item>
/// <item>which culture prays which way, as county counts per (culture, faith) pair — the affinity
/// that puts Catholicism with the French rather than with the Somali;</item>
/// <item>each culture's own place names, read off the counties and baronies history seats it in, so
/// the land it settles can be named in a language that sounds like it.</item>
/// </list>
///
/// Read once per game directory and cached: it parses every culture, name list, religion and
/// landed title file plus the whole English localisation, which is a few seconds nobody should pay
/// twice.
/// </summary>
public sealed class VanillaCatalog
{
    public sealed class CultureDef
    {
        public required string Key { get; init; }
        public required string Name { get; init; }
        public required string Heritage { get; init; }
        public required string Language { get; init; }
        public required string Ethos { get; init; }
        public required string MartialCustom { get; init; }
        public required string HeadDetermination { get; init; }
        public required List<string> Traditions { get; init; }
        public required List<string> NameLists { get; init; }
        public required string CoaGfx { get; init; }
        public required string BuildingGfx { get; init; }
        public required string ClothingGfx { get; init; }
        public required string UnitGfx { get; init; }
        public required string Ethnicities { get; init; }
        public (byte R, byte G, byte B)? Color { get; init; }

        /// <summary>Counties vanilla's province history seats this culture in, at any date.</summary>
        public int Counties { get; set; }

        /// <summary>The display names of those counties and their baronies, for its place-name language.</summary>
        public List<string> PlaceNames { get; } = [];

        /// <summary>Counties it holds at vanilla's first date, and where they sit on vanilla's map
        /// (pixel coordinates of provinces.png). Null when the map could not be read.</summary>
        public int HomeCounties { get; set; }
        public (double X, double Y)? Home { get; set; }
    }

    public sealed class NameListDef
    {
        public List<string> Male { get; } = [];
        public List<string> Female { get; } = [];
        public List<string> Dynasties { get; } = [];

        /// <summary>Each given name's display text to the localisation key vanilla lists it by
        /// ("Édouard" to <c>E_douard</c>), which is what character history should carry.</summary>
        public Dictionary<string, string> Keys { get; } = new(StringComparer.Ordinal);
    }

    public sealed class ReligionDef
    {
        public required string Key { get; init; }
        public required string Name { get; init; }
        public required string Family { get; init; }
        public required List<string> Doctrines { get; init; }
        public string? GraphicalFaith { get; init; }
        public required List<string> Virtues { get; init; }
        public required List<string> Sins { get; init; }
        public List<FaithDef> Faiths { get; } = [];
    }

    public sealed class FaithDef
    {
        public required string Key { get; init; }
        public required string Name { get; init; }
        public required ReligionDef Religion { get; init; }
        public required (double R, double G, double B) Color { get; init; }
        public required string Icon { get; init; }
        public string? Head { get; init; }
        public required List<string> HolySites { get; init; }

        /// <summary>Faith-level doctrines, tenets included, as written.</summary>
        public required List<string> Doctrines { get; init; }

        /// <summary>Counties vanilla's province history seats this faith in, at any date.</summary>
        public int Counties { get; set; }

        /// <summary>Where those counties sit on vanilla's map; null when it could not be read.</summary>
        public (double X, double Y)? Home { get; set; }
    }

    /// <summary>A landless vanilla title — the heads of faith are all of this shape.</summary>
    public sealed class LandlessTitleDef
    {
        public required string Key { get; init; }
        public required string Name { get; init; }
        public string? Capital { get; init; }

        /// <summary>Every field but <c>capital</c>, one line each; see <see cref="HeadOfFaith.InheritedFields"/>.</summary>
        public required List<string> Fields { get; init; }
    }

    /// <summary>One title of vanilla's de jure tree, as <see cref="VanillaTitles"/> borrows it.</summary>
    public sealed class TitleDef
    {
        public required string Key { get; init; }
        public required char Tier { get; init; }
        public string? Parent { get; init; }
        public List<string> Children { get; } = [];
        public bool Landless { get; init; }

        /// <summary>A barony's province id on vanilla's map; 0 above barony.</summary>
        public int Province { get; init; }

        /// <summary>The county vanilla names as this title's capital, above county.</summary>
        public string? Capital { get; init; }

        public string Name { get; set; } = "";
        public (byte R, byte G, byte B)? Color { get; init; }

        /// <summary>
        /// Every field but <c>color</c>, <c>capital</c>, <c>province</c> and the child titles — the
        /// part of vanilla's declaration that is about the title rather than about vanilla's map —
        /// one per line, flattened as <see cref="Flatten"/> does.
        /// </summary>
        public List<string> Fields { get; init; } = [];

        /// <summary>Where it sits on vanilla's map: a barony's province, a county's baronies, and so
        /// on up. Null when the map could not be read.</summary>
        public (double X, double Y)? Home { get; set; }

        /// <summary>A county's (culture, faith) through vanilla's province history, dated, the
        /// undated values first as <c>1.1.1</c>. Empty above county.</summary>
        public List<(string Date, string Culture, string Faith)> States { get; } = [];
    }

    /// <summary>A dated entry of vanilla's title history: who held it and whose vassal that made them.</summary>
    public readonly record struct TitleEvent(string Date, string? Holder, string? Liege);

    public Dictionary<string, TitleDef> Titles { get; } = new(StringComparer.Ordinal);

    /// <summary>Vanilla's history/titles, per title key, in date order.</summary>
    public Dictionary<string, List<TitleEvent>> TitleHistory { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, CultureDef> Cultures { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, NameListDef> NameLists { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, ReligionDef> Religions { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, FaithDef> Faiths { get; } = new(StringComparer.Ordinal);

    /// <summary>Holy site key to the vanilla county it sits in.</summary>
    public Dictionary<string, string> HolySiteCounty { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, LandlessTitleDef> LandlessTitles { get; } = new(StringComparer.Ordinal);

    /// <summary>Display names of heritage and language pillars, by pillar key.</summary>
    public Dictionary<string, string> PillarNames { get; } = new(StringComparer.Ordinal);

    /// <summary>County-states per (culture, faith) in vanilla's province history: the affinity table.</summary>
    public Dictionary<(string Culture, string Faith), int> CultureFaith { get; } = [];

    /// <summary>Religions vanilla's coronation triggers crown rather than invest with regalia.</summary>
    public HashSet<string> CrownReligions { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Vanilla's geographical regions (<c>map_data/geographical_regions</c>) as declared: each
    /// field (<c>duchies</c>, <c>regions</c>, <c>provinces</c>…) to its keys. Resolved to counties
    /// on demand by <see cref="RegionCounties"/>.
    /// </summary>
    public Dictionary<string, List<(string Field, string Key)>> Regions { get; } = new(StringComparer.Ordinal);

    private readonly Dictionary<string, HashSet<string>> _regionCounties = new(StringComparer.Ordinal);

    /// <summary>
    /// The county keys inside a geographical region, its sub-regions and titles expanded down the
    /// de jure tree and its provinces taken to the county their barony belongs to. Null when
    /// vanilla declares no such region.
    /// </summary>
    public IReadOnlySet<string>? RegionCounties(string region)
    {
        if (!Regions.ContainsKey(region)) return null;
        var byProvince = Titles.Values.Where(t => t.Tier == 'b' && t.Province > 0 && t.Parent is not null)
                               .GroupBy(t => t.Province).ToDictionary(g => g.Key, g => g.First().Parent!);
        lock (_regionCounties) return Resolve(region, new HashSet<string>(StringComparer.Ordinal));

        HashSet<string> Resolve(string key, HashSet<string> visiting)
        {
            if (_regionCounties.TryGetValue(key, out var known)) return known;
            var counties = new HashSet<string>(StringComparer.Ordinal);
            if (!visiting.Add(key) || !Regions.TryGetValue(key, out var fields)) return counties;

            foreach (var (field, member) in fields)
            {
                if (field == "regions") counties.UnionWith(Resolve(member, visiting));
                else if (field == "provinces")
                {
                    if (int.TryParse(member, out int id) && byProvince.TryGetValue(id, out string? county)) counties.Add(county);
                }
                else if (Titles.TryGetValue(member, out var title)) Collect(title);
            }

            _regionCounties[key] = counties;
            return counties;

            void Collect(TitleDef def)
            {
                if (def.Tier == 'c') { counties.Add(def.Key); return; }
                foreach (string child in def.Children)
                    if (Titles.TryGetValue(child, out var c) && c.Tier != 'b') Collect(c);
            }
        }
    }

    /// <summary>Files the parser could not read. Reported, never fatal: a mod-patched or future
    /// file shape costs the entries in it and nothing else.</summary>
    public List<string> Unreadable { get; } = [];

    private static readonly Dictionary<string, VanillaCatalog> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static VanillaCatalog Read(string gameDir)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(gameDir, out var cached)) return cached;
            var catalog = new VanillaCatalog();
            catalog.Load(gameDir);
            Cache[gameDir] = catalog;
            return catalog;
        }
    }

    private void Load(string gameDir)
    {
        var loc = LocLibrary.Load(gameDir);
        Loc = loc;
        string Text(string key) => Clean(loc.Text(key)) ?? key;

        var provinceState = ReadProvinceHistory(Path.Combine(gameDir, "history", "provinces"));
        var (countyProvinces, titleNodes, constants) = ReadLandedTitles(Path.Combine(gameDir, "common", "landed_titles"));

        ReadCultures(Path.Combine(gameDir, "common", "culture", "cultures"), Text);
        ReadNameLists(Path.Combine(gameDir, "common", "culture", "name_lists"), loc);
        ReadReligions(Path.Combine(gameDir, "common", "religion", "religion_types"), Text);
        ReadHolySites(Path.Combine(gameDir, "common", "religion", "holy_site_types"));

        foreach (var (key, node) in titleNodes)
        {
            if (node.Field("landless") != "yes") continue;

            // Vanilla declares can_use_nomadic_naming twice on k_papal_state and k_orthodox. The
            // engine keeps one either way; copying both only earns the copy a duplicate-field report.
            var leaves = new HashSet<string>(StringComparer.Ordinal);
            LandlessTitles[key] = new LandlessTitleDef
            {
                Key = key,
                Name = Text(key),
                Capital = node.Field("capital"),
                Fields = node.Children
                    .Where(c => c.Key != "capital" && (c.IsBlock || c.Value is not null || c.Head.Count > 0))
                    .Where(c => c.IsBlock || c.Value is null || leaves.Add(c.Key))
                    .Select(c => Flatten(c, constants))
                    .ToList(),
            };
        }

        foreach (string pillar in Cultures.Values.SelectMany(c => new[] { c.Heritage, c.Language }).Distinct())
            PillarNames[pillar] = Clean(loc.Text($"{pillar}_name")) ?? Humanise(pillar);

        ReadRegions(Path.Combine(gameDir, "map_data", "geographical_regions"));

        var centroids = ReadProvinceCentroids(Path.Combine(gameDir, "map_data"));
        ReadTitleHistory(Path.Combine(gameDir, "history", "titles"));
        ReadCharacters(Path.Combine(gameDir, "history", "characters"));

        foreach (var (key, node) in TopLevelBlocks(Path.Combine(gameDir, "common", "dynasties")))
            DynastySource[key] = node.ToString();
        foreach (var (key, node) in TopLevelBlocks(Path.Combine(gameDir, "common", "dynasty_houses")))
        {
            HouseSource[key] = node.ToString();
            if (Unquote(node.Field("dynasty")) is { } dynasty) HouseDynasty[key] = dynasty;
        }
        // Blocks and one-line aliases alike: `1060028 = c_daura` gives a dynasty its county's arms.
        foreach (var document in Documents(Path.Combine(gameDir, "common", "coat_of_arms", "coat_of_arms")))
            foreach (var root in document.Roots.Where(r => r.Key.Length > 0 && !r.Key.StartsWith('@')))
                CoaKeys.Add(root.Key);

        // Title names, and where every title sits: a barony at its province, anything above it at
        // the mean of its children. Children are visited before the title they belong to.
        foreach (var def in Titles.Values) def.Name = Text(def.Key);
        foreach (var def in Titles.Values.Where(d => d.Parent is null)) Place(def);

        void Place(TitleDef def)
        {
            foreach (string child in def.Children) Place(Titles[child]);
            if (def.Tier == 'b')
            {
                if (centroids.TryGetValue(def.Province, out var p)) def.Home = p;
                return;
            }

            var homes = def.Children.Select(c => Titles[c].Home).OfType<(double X, double Y)>().ToList();
            if (homes.Count > 0) def.Home = (homes.Average(h => h.X), homes.Average(h => h.Y));
        }

        var homeSums = new Dictionary<CultureDef, (double X, double Y)>();
        var faithSums = new Dictionary<FaithDef, (double X, double Y, int N)>();

        // Seat every county-state vanilla's history records, and give each culture the names of the
        // land it holds at the first date. Only the capital barony carries the culture line, so a
        // county is read through whichever of its provinces has one.
        foreach (var (county, provinces) in countyProvinces)
        {
            var dated = provinces.Where(provinceState.ContainsKey).Select(p => provinceState[p]).FirstOrDefault();
            if (dated is null) continue;
            if (Titles.TryGetValue(county.Key, out var countyDef)) countyDef.States.AddRange(dated);
            var states = dated.Select(s => (s.Culture, s.Faith)).ToList();

            foreach (var (culture, faith) in states.Distinct())
            {
                if (Cultures.TryGetValue(culture, out var c)) c.Counties++;
                if (Faiths.TryGetValue(faith, out var f)) f.Counties++;
                CultureFaith[(culture, faith)] = CultureFaith.GetValueOrDefault((culture, faith)) + 1;
            }

            // Every people that holds the county at some date, not only the first: Castilian holds
            // nothing in 867 and a great deal by 1066, and should be placeable with a home and a
            // language of its own.
            var at = provinces.Where(centroids.ContainsKey).Select(p => centroids[p]).ToList();
            foreach (string holder in states.Select(s => s.Culture).Distinct())
            {
                if (!Cultures.TryGetValue(holder, out var people)) continue;
                AddPlaceName(people, loc.Text(county.Key));
                foreach (string barony in county.Baronies) AddPlaceName(people, loc.Text(barony));

                if (at.Count == 0) continue;
                people.HomeCounties++;
                homeSums[people] = (homeSums.GetValueOrDefault(people).X + at.Average(p => p.X),
                                    homeSums.GetValueOrDefault(people).Y + at.Average(p => p.Y));
            }

            if (at.Count == 0) continue;
            foreach (string creed in states.Select(s => s.Faith).Distinct())
            {
                if (!Faiths.TryGetValue(creed, out var faith)) continue;
                var (fx, fy, fn) = faithSums.GetValueOrDefault(faith);
                faithSums[faith] = (fx + at.Average(p => p.X), fy + at.Average(p => p.Y), fn + 1);
            }
        }

        foreach (var (culture, (x, y)) in homeSums)
            culture.Home = (x / culture.HomeCounties, y / culture.HomeCounties);
        foreach (var (faith, (x, y, n)) in faithSums)
            faith.Home = (x / n, y / n);

        string triggers = Path.Combine(gameDir, "common", "scripted_triggers", "10_ach_scripted_triggers.txt");
        if (File.Exists(triggers))
            CrownReligions.UnionWith(Emit.CoronationWriter.VanillaReligions(
                File.ReadAllText(triggers), "coronation_proper_artifact_crown_trigger"));

        Console.WriteLine($"  vanilla catalog: {Cultures.Values.Count(c => c.Counties > 0)} of {Cultures.Count} cultures " +
                          $"and {Faiths.Values.Count(f => f.Counties > 0)} of {Faiths.Count} faiths seated in history, " +
                          $"{HolySiteCounty.Count} holy sites, {LandlessTitles.Count} landless titles" +
                          (Unreadable.Count > 0 ? $"; {Unreadable.Count} files unreadable" : ""));
    }

    /// <summary>
    /// <c>--vanilla-catalog [key]</c>: what a vanilla-identities world has to choose from, or one
    /// culture or faith in full. False when nothing usable was read.
    /// </summary>
    public static bool Probe(string gameDir, string? key)
    {
        var catalog = Read(gameDir);
        foreach (string file in catalog.Unreadable) Console.WriteLine($"  unreadable: {file}");

        if (key is not null && catalog.Cultures.TryGetValue(key, out var c))
        {
            Console.WriteLine($"{c.Key} \"{c.Name}\": {c.Heritage} / {c.Language}, {c.Ethos}, {c.Counties} counties");
            Console.WriteLine($"  traditions: {string.Join(' ', c.Traditions)}");
            Console.WriteLine($"  gfx: {c.CoaGfx} {c.BuildingGfx} {c.ClothingGfx} {c.UnitGfx}");
            foreach (string list in c.NameLists)
                if (catalog.NameLists.TryGetValue(list, out var n))
                    Console.WriteLine($"  {list}: {n.Male.Count} male ({string.Join(", ", n.Male.Take(6))}), " +
                                      $"{n.Female.Count} female, {n.Dynasties.Count} houses ({string.Join(", ", n.Dynasties.Take(6))})");
            Console.WriteLine($"  place names ({c.PlaceNames.Count}): {string.Join(", ", c.PlaceNames.Take(20))}");
            Console.WriteLine($"  home: {(c.Home is { } h ? $"{h.X:F0},{h.Y:F0}" : "unknown")} over {c.HomeCounties} counties");
            var faiths = catalog.CultureFaith.Where(kv => kv.Key.Culture == key).OrderByDescending(kv => kv.Value);
            Console.WriteLine($"  prays: {string.Join(", ", faiths.Take(6).Select(kv => $"{kv.Key.Faith} {kv.Value}"))}");
            return true;
        }

        // A geographical region: how many counties a VanillaRegion of it offers, and its kingdoms.
        if (key is not null && catalog.RegionCounties(key) is { } region)
        {
            var kingdoms = region.Select(k => catalog.Titles[k]).Select(c => c.Parent is { } d ? catalog.Titles.GetValueOrDefault(d)?.Parent : null)
                                 .OfType<string>().Distinct().Select(k => catalog.Titles[k].Name).Order().ToList();
            Console.WriteLine($"{key}: {region.Count} counties in {kingdoms.Count} kingdoms");
            Console.WriteLine($"  {string.Join(", ", kingdoms)}");
            return true;
        }

        if (key is not null && catalog.Faiths.TryGetValue(key, out var f))
        {
            Console.WriteLine($"{f.Key} \"{f.Name}\" of {f.Religion.Key} ({f.Religion.Family}), {f.Counties} counties");
            Console.WriteLine($"  head: {f.Head ?? "none"}; holy sites: {string.Join(' ', f.HolySites)}");
            Console.WriteLine($"  doctrines: {string.Join(' ', f.Religion.Doctrines.Concat(f.Doctrines))}");
            if (f.Head is not null && catalog.LandlessTitles.TryGetValue(f.Head, out var t))
                Console.WriteLine($"  {t.Key} \"{t.Name}\" capital {t.Capital}: {string.Join(" | ", t.Fields)}");
            return true;
        }

        if (key is not null && catalog.Titles.TryGetValue(key, out var title))
        {
            Console.WriteLine($"{title.Key} \"{title.Name}\" tier {title.Tier}, parent {title.Parent ?? "none"}, " +
                              $"{title.Children.Count} children, home {(title.Home is { } th ? $"{th.X:F0},{th.Y:F0}" : "unknown")}" +
                              (title.Landless ? ", landless" : ""));
            Console.WriteLine($"  fields: {string.Join(" | ", title.Fields)}");
            foreach (var s in title.States) Console.WriteLine($"  {s.Date}: {s.Culture} / {s.Faith}");
            foreach (var e in catalog.TitleHistory.GetValueOrDefault(key) ?? [])
                Console.WriteLine($"  {e.Date}: holder {e.Holder ?? "-"}, liege {e.Liege ?? "-"}");
            return true;
        }

        var seated = catalog.Cultures.Values.Where(x => x.Counties > 0).ToList();
        foreach (var heritage in seated.GroupBy(x => x.Heritage).OrderByDescending(g => g.Sum(x => x.Counties)).Take(15))
            Console.WriteLine($"  {catalog.PillarNames.GetValueOrDefault(heritage.Key, heritage.Key)}: " +
                              string.Join(", ", heritage.OrderByDescending(x => x.Counties)
                                                        .Select(x => $"{x.Name} {x.Counties} ({x.PlaceNames.Count} names)")));
        return seated.Count > 0 && catalog.Faiths.Values.Any(x => x.Counties > 0);
    }

    private static void AddPlaceName(CultureDef culture, string? raw)
    {
        string? name = Clean(raw);
        if (name is null) return;

        // "Toledo (Tulaytula)" is two names in one; the first is the one the county answers to.
        int paren = name.IndexOf('(');
        if (paren > 0) name = name[..paren].Trim();
        if (name.Length >= 3 && !culture.PlaceNames.Contains(name)) culture.PlaceNames.Add(name);
    }

    // ---------------------------------------------------------------------------------------------
    // Readers
    // ---------------------------------------------------------------------------------------------

    private void ReadCultures(string dir, Func<string, string> text)
    {
        foreach (var (key, node) in TopLevelBlocks(dir))
        {
            string? heritage = node.Field("heritage");
            string? language = node.Field("language");
            string? ethos = node.Field("ethos");
            if (heritage is null || language is null || ethos is null) continue;

            var traditions = node.ChildrenNamed("traditions").Where(t => t.IsBlock)
                .SelectMany(Tokens).Where(t => t.StartsWith("tradition_", StringComparison.Ordinal))
                .Distinct().ToList();

            Cultures[key] = new CultureDef
            {
                Key = key,
                Name = text(key),
                Heritage = heritage,
                Language = language,
                Ethos = ethos,
                MartialCustom = node.Field("martial_custom") ?? "martial_custom_male_only",
                HeadDetermination = node.Field("head_determination") ?? "head_determination_domain",
                Traditions = traditions,
                NameLists = node.ChildrenNamed("name_list").Select(n => n.Value).OfType<string>().ToList(),
                CoaGfx = Gfx(node, "coa_gfx"),
                BuildingGfx = Gfx(node, "building_gfx"),
                ClothingGfx = Gfx(node, "clothing_gfx"),
                UnitGfx = Gfx(node, "unit_gfx"),
                Ethnicities = node.ChildrenNamed("ethnicities").FirstOrDefault(n => n.IsBlock) is { } e
                    ? string.Join(' ', e.Children.Select(c => Flatten(c, null)))
                    : "",
                Color = ParseByteColor(node.ChildrenNamed("color").FirstOrDefault()),
            };
        }
    }

    private static string Gfx(GuiNode culture, string field)
        => culture.ChildrenNamed(field).FirstOrDefault(n => n.IsBlock) is { } block
            ? "{ " + string.Join(' ', Tokens(block)) + " }"
            : culture.Field(field) ?? "";

    private void ReadNameLists(string dir, LocLibrary loc)
    {
        foreach (var (key, node) in TopLevelBlocks(dir))
        {
            var list = new NameListDef();

            void Names(string field, List<string> into)
            {
                foreach (var block in node.ChildrenNamed(field).Where(b => b.IsBlock))
                    foreach (string token in Tokens(block))
                    {
                        if (IsNumber(token)) continue;
                        string name = Clean(loc.Text(token)) ?? token;
                        if (name.Length < 2 || name.Contains('_') || into.Contains(name)) continue;
                        into.Add(name);
                        list.Keys.TryAdd(name, token);
                    }
            }

            Names("male_names", list.Male);
            Names("female_names", list.Female);

            // A dynasty entry is a name key, or a { prefix name } pair whose prefix ("de", "of") is
            // the name list's grammar rather than part of the house's name.
            foreach (string field in new[] { "dynasty_names", "cadet_dynasty_names" })
                foreach (var block in node.ChildrenNamed(field).Where(b => b.IsBlock))
                    foreach (string token in Tokens(block))
                    {
                        if (!token.StartsWith("dynn_", StringComparison.Ordinal)) continue;
                        string name = Clean(loc.Text(token)) ?? "";
                        if (name.Length >= 2 && !list.Dynasties.Contains(name)) list.Dynasties.Add(name);
                    }

            NameLists[key] = list;
        }
    }

    private void ReadReligions(string dir, Func<string, string> text)
    {
        foreach (var (key, node) in TopLevelBlocks(dir))
        {
            string? family = node.Field("family");
            if (family is null) continue;

            var traits = node.ChildrenNamed("traits").FirstOrDefault(t => t.IsBlock);
            var religion = new ReligionDef
            {
                Key = key,
                Name = text(key),
                Family = family,
                Doctrines = node.ChildrenNamed("doctrine").Select(d => d.Value).OfType<string>().ToList(),
                GraphicalFaith = Unquote(node.Field("graphical_faith")),
                Virtues = TraitNames(traits, "virtues"),
                Sins = TraitNames(traits, "sins"),
            };

            Religions[key] = religion;

            foreach (var faiths in node.ChildrenNamed("faiths").Where(f => f.IsBlock))
                foreach (var faith in faiths.Children.Where(f => f.IsBlock && f.Key.Length > 0))
                {
                    var def = new FaithDef
                    {
                        Key = faith.Key,
                        Name = text(faith.Key),
                        Religion = religion,
                        Color = ParseUnitColor(faith.ChildrenNamed("color").FirstOrDefault()),
                        Icon = Unquote(faith.Field("icon")) ?? faith.Key,
                        Head = faith.Field("religious_head"),
                        HolySites = faith.ChildrenNamed("holy_site").Select(h => h.Value).OfType<string>().ToList(),
                        Doctrines = faith.ChildrenNamed("doctrine").Select(d => d.Value).OfType<string>().ToList(),
                    };

                    religion.Faiths.Add(def);
                    Faiths[def.Key] = def;
                }
        }
    }

    private static List<string> TraitNames(GuiNode? traits, string field)
    {
        if (traits is null) return [];
        var names = new List<string>();
        foreach (var block in traits.ChildrenNamed(field).Where(b => b.IsBlock))
            foreach (var child in block.Children)
            {
                // `virtues = { brave just }` is a token run; `{ trait = brave scale = 2 }` names it.
                if (child.IsBlock && child.Key.Length > 0) names.Add(child.Key);
                else if (child.IsBlock) { if (child.Field("trait") is { } t) names.Add(t); }
                else if (child.Value is null) names.AddRange(child.Head.Where(h => h != "="));
            }
        return names;
    }

    private static readonly HashSet<string> RegionFields =
        new(["hegemonies", "empires", "kingdoms", "duchies", "counties", "provinces", "regions"], StringComparer.Ordinal);

    /// <summary>Every geographical region's members, field by field; see <see cref="Regions"/>.</summary>
    private void ReadRegions(string dir)
    {
        foreach (var (key, node) in TopLevelBlocks(dir))
        {
            var members = new List<(string Field, string Key)>();
            foreach (var field in node.Children.Where(c => c.IsBlock && RegionFields.Contains(c.Key)))
                foreach (string member in Tokens(field))
                    members.Add((field.Key, member));
            Regions[key] = members;
        }
    }

    private void ReadHolySites(string dir)
    {
        foreach (var (key, node) in TopLevelBlocks(dir))
            if (node.Field("county") is { } county) HolySiteCounty[key] = county;
    }

    /// <summary>A county and the baronies under it, by key.</summary>
    private sealed record CountyTitle(string Key, List<string> Baronies);

    private (Dictionary<CountyTitle, List<int>> Counties, Dictionary<string, GuiNode> Titles,
        Dictionary<string, string> Constants) ReadLandedTitles(string dir)
    {
        var counties = new Dictionary<CountyTitle, List<int>>();
        var titles = new Dictionary<string, GuiNode>(StringComparer.Ordinal);
        var constants = new Dictionary<string, string>(StringComparer.Ordinal);

        var documents = Documents(dir).ToList();

        // Constants first, from every file: a title may use one declared further down or in another file.
        foreach (var document in documents)
            foreach (var root in document.Roots)
                if (root.Key.StartsWith('@') && root.Value is not null) constants[root.Key] = root.Value;

        foreach (var document in documents)
            foreach (var root in document.Roots)
                Walk(root, null);

        return (counties, titles, constants);

        static bool IsTitle(GuiNode n)
            => n.IsBlock && n.Key.Length >= 3 && n.Key[1] == '_' && n.Key[0] is 'e' or 'k' or 'd' or 'c' or 'b' or 'h';

        void Walk(GuiNode node, string? parent)
        {
            if (!IsTitle(node)) return;
            char tier = node.Key[0];

            if (!titles.TryAdd(node.Key, node)) return;

            var leaves = new HashSet<string>(StringComparer.Ordinal);
            var def = new TitleDef
            {
                Key = node.Key,
                Tier = tier,
                Parent = parent,
                Landless = node.Field("landless") == "yes",
                Province = int.TryParse(node.Field("province"), out int province) ? province : 0,
                Capital = node.Field("capital"),
                Color = ParseByteColor(node.ChildrenNamed("color").FirstOrDefault()),
                Fields = node.Children
                    .Where(c => !IsTitle(c) && c.Key is not ("color" or "capital" or "province")
                                && (c.IsBlock || c.Value is not null || c.Head.Count > 0))
                    .Where(c => c.IsBlock || c.Value is null || leaves.Add(c.Key))
                    .Select(c => Flatten(c, constants))
                    .ToList(),
            };
            Titles[node.Key] = def;
            if (parent is not null && Titles.TryGetValue(parent, out var up)) up.Children.Add(node.Key);

            if (tier == 'c')
            {
                var baronies = node.Children.Where(b => b.IsBlock && b.Key.StartsWith("b_", StringComparison.Ordinal)).ToList();
                var provinces = baronies
                    .Select(b => int.TryParse(b.Field("province"), out int id) ? id : 0)
                    .Where(id => id > 0).ToList();
                counties[new CountyTitle(node.Key, baronies.Select(b => b.Key).ToList())] = provinces;
            }

            foreach (var child in node.Children) Walk(child, node.Key);
        }
    }

    /// <summary>
    /// Every (culture, faith) state each province passes through, dated: its undated values as
    /// <c>1.1.1</c>, then one state per dated block that changes either. Provinces with no culture
    /// line (the non-capital baronies) are absent.
    /// </summary>
    private Dictionary<int, List<(string Date, string Culture, string Faith)>> ReadProvinceHistory(string dir)
    {
        var states = new Dictionary<int, List<(string, string, string)>>();

        foreach (var document in Documents(dir, SearchOption.AllDirectories))
            foreach (var root in document.Roots)
            {
                if (!root.IsBlock || !int.TryParse(root.Key, out int id)) continue;

                string? culture = root.Field("culture");
                string? faith = root.Field("religion");
                var list = new List<(string, string, string)>();
                if (culture is not null && faith is not null) list.Add(("1.1.1", culture, faith));

                foreach (var dated in root.Children.Where(c => c.IsBlock && IsDate(c.Key)).OrderBy(c => c.Key, DateOrder))
                {
                    string? c2 = dated.Field("culture");
                    string? f2 = dated.Field("religion");
                    if (c2 is null && f2 is null) continue;
                    culture = c2 ?? culture;
                    faith = f2 ?? faith;
                    if (culture is not null && faith is not null) list.Add((dated.Key, culture, faith));
                }

                if (list.Count > 0) states[id] = list;
            }

        return states;
    }

    /// <summary>A vanilla character's culture and faith: undated values, then dated changes.</summary>
    public Dictionary<string, List<(string Date, string? Culture, string? Faith)>> Characters { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// One character of vanilla's history/characters, for <see cref="VanillaCharacters"/>: the
    /// family links the import walks, and the block's own source text, which is re-parsed only for
    /// the few hundred characters a world imports rather than kept as a tree for all sixty thousand.
    /// </summary>
    public sealed record CharacterDef(string Id, string? NameKey, bool Female, string? Father, string? Mother,
        IReadOnlyList<string> Spouses, string? Birth, string? Death, string? Dynasty, string? House, string Source);

    public Dictionary<string, CharacterDef> CharacterDefs { get; } = new(StringComparer.Ordinal);

    /// <summary>Every character naming each id as father or mother — the reverse of the family links.</summary>
    public Dictionary<string, List<string>> ChildrenOf { get; } = new(StringComparer.Ordinal);

    /// <summary>English localisation, kept for the names the character import has to display.</summary>
    public LocLibrary? Loc { get; private set; }

    /// <summary>
    /// Vanilla's dynasty and house definitions, as source text by key, and each house's dynasty.
    /// The mod blanks both directories (they describe vanilla's characters, which it removes), so a
    /// world that imports vanilla characters ships the definitions its characters need itself.
    /// </summary>
    public Dictionary<string, string> DynastySource { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> HouseSource { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> HouseDynasty { get; } = new(StringComparer.Ordinal);

    /// <summary>Every key vanilla's coat_of_arms files define arms for.</summary>
    public HashSet<string> CoaKeys { get; } = new(StringComparer.Ordinal);

    /// <summary>A character's (culture, faith) as of <paramref name="date"/>, or null when history never says.</summary>
    public (string? Culture, string? Faith)? CharacterAt(string id, string date)
    {
        if (!Characters.TryGetValue(id, out var states)) return null;
        string? culture = null, faith = null;
        foreach (var (at, c, f) in states)
        {
            if (!OnOrBefore(at, date)) break;
            culture = c ?? culture;
            faith = f ?? faith;
        }
        return (culture, faith);
    }

    private void ReadCharacters(string dir)
    {
        foreach (var document in Documents(dir, SearchOption.AllDirectories))
            foreach (var root in document.Roots.Where(r => r.IsBlock && r.Key.Length > 0))
            {
                var states = new List<(string, string?, string?)>
                {
                    ("1.1.1", Unquote(root.Field("culture")), Unquote(root.Field("religion") ?? root.Field("faith"))),
                };
                foreach (var dated in root.Children.Where(c => c.IsBlock && IsDate(c.Key)).OrderBy(c => c.Key, DateOrder))
                {
                    string? c = Unquote(dated.Field("culture"));
                    string? f = Unquote(dated.Field("religion") ?? dated.Field("faith"));
                    if (c is not null || f is not null) states.Add((dated.Key, c, f));
                }
                Characters[root.Key] = states;

                string? birth = null, death = null;
                var spouses = new List<string>();
                foreach (var dated in root.Children.Where(c => c.IsBlock && IsDate(c.Key)))
                {
                    if (dated.ChildrenNamed("birth").Any()) birth ??= dated.Key;
                    if (dated.ChildrenNamed("death").Any()) death ??= dated.Key;
                    foreach (var marriage in dated.Children.Where(n => n.Key is "add_spouse" or "add_matrilineal_spouse"))
                        if (Unquote(marriage.Value) is { } spouse) spouses.Add(spouse);
                }

                string? father = Unquote(root.Field("father")), mother = Unquote(root.Field("mother"));
                CharacterDefs[root.Key] = new CharacterDef(root.Key, Unquote(root.Field("name")),
                    root.Field("female") == "yes", father, mother, spouses, birth, death,
                    Unquote(root.Field("dynasty")), Unquote(root.Field("dynasty_house")), root.ToString());

                foreach (string? parent in new[] { father, mother })
                    if (parent is not null)
                    {
                        if (!ChildrenOf.TryGetValue(parent, out var list)) ChildrenOf[parent] = list = [];
                        list.Add(root.Key);
                    }
            }
    }

    /// <summary>Holder and liege changes per title, in date order. Quotes stripped; 0 kept as "0".</summary>
    private void ReadTitleHistory(string dir)
    {
        foreach (var document in Documents(dir, SearchOption.AllDirectories))
            foreach (var root in document.Roots.Where(r => r.IsBlock && r.Key.Length > 2 && r.Key[1] == '_'))
            {
                var events = new List<TitleEvent>();
                foreach (var dated in root.Children.Where(c => c.IsBlock && IsDate(c.Key)).OrderBy(c => c.Key, DateOrder))
                {
                    string? holder = Unquote(dated.Field("holder"));
                    string? liege = Unquote(dated.Field("liege"));
                    if (holder is null && liege is null) continue;
                    events.Add(new TitleEvent(dated.Key, holder, liege));
                }

                if (events.Count == 0) continue;
                if (TitleHistory.TryGetValue(root.Key, out var existing)) existing.AddRange(events);
                else TitleHistory[root.Key] = events;
            }

        foreach (var list in TitleHistory.Values) list.Sort((a, b) => DateOrder.Compare(a.Date, b.Date));
    }

    /// <summary>Whether <paramref name="date"/> is on or before <paramref name="limit"/>.</summary>
    public static bool OnOrBefore(string date, string limit) => DateOrder.Compare(date, limit) <= 0;

    /// <summary>A county's (culture, faith) as of <paramref name="date"/>: the last state on or
    /// before it, or its first when all are later. Null for a county history never mentions.</summary>
    public (string Culture, string Faith)? StateOf(TitleDef county, string date)
    {
        if (county.States.Count == 0) return null;
        var at = county.States.LastOrDefault(s => OnOrBefore(s.Date, date));
        var pick = at.Culture is null ? county.States[0] : at;
        return (pick.Culture, pick.Faith);
    }

    /// <summary>
    /// Where every vanilla province sits: the mean pixel of its colour in provinces.png, keyed by
    /// the id definition.csv gives that colour. Used only to say which vanilla peoples are
    /// neighbours, so a failed read costs placement its geography and nothing else.
    /// </summary>
    private Dictionary<int, (double X, double Y)> ReadProvinceCentroids(string mapData)
    {
        var result = new Dictionary<int, (double, double)>();
        string definition = Path.Combine(mapData, "definition.csv");
        string png = Path.Combine(mapData, "provinces.png");
        if (!File.Exists(definition) || !File.Exists(png)) return result;

        var idOf = new Dictionary<int, int>();
        foreach (string line in File.ReadLines(definition))
        {
            var parts = line.Split(';');
            if (parts.Length < 4 || !int.TryParse(parts[0], out int id) || id <= 0) continue;
            if (int.TryParse(parts[1], out int r) && int.TryParse(parts[2], out int g) && int.TryParse(parts[3], out int b))
                idOf[(r << 16) | (g << 8) | b] = id;
        }

        try
        {
            using var image = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgb24>(png);
            int max = idOf.Values.DefaultIfEmpty(0).Max() + 1;
            var sx = new double[max];
            var sy = new double[max];
            var n = new int[max];

            image.ProcessPixelRows(rows =>
            {
                int lastColour = -1, lastId = 0;
                for (int y = 0; y < rows.Height; y++)
                {
                    var row = rows.GetRowSpan(y);
                    for (int x = 0; x < row.Length; x++)
                    {
                        int colour = (row[x].R << 16) | (row[x].G << 8) | row[x].B;
                        if (colour != lastColour)
                        {
                            lastColour = colour;
                            lastId = idOf.GetValueOrDefault(colour);
                        }
                        if (lastId == 0) continue;
                        sx[lastId] += x;
                        sy[lastId] += y;
                        n[lastId]++;
                    }
                }
            });

            for (int id = 1; id < max; id++)
                if (n[id] > 0) result[id] = (sx[id] / n[id], sy[id] / n[id]);
        }
        catch (Exception e) when (e is IOException or SixLabors.ImageSharp.ImageFormatException
                                      or SixLabors.ImageSharp.UnknownImageFormatException)
        {
            Unreadable.Add($"provinces.png: {e.Message}");
        }

        return result;
    }

    // ---------------------------------------------------------------------------------------------
    // Parsing helpers
    // ---------------------------------------------------------------------------------------------

    private IEnumerable<GuiDocument> Documents(string dir, SearchOption option = SearchOption.TopDirectoryOnly)
    {
        if (!Directory.Exists(dir)) yield break;

        foreach (string path in Directory.GetFiles(dir, "*.txt", option).Order(StringComparer.Ordinal))
        {
            GuiDocument? document = null;
            try
            {
                document = GuiParser.Parse(File.ReadAllText(path), path);
            }
            catch (Exception e) when (e is FormatException or InvalidOperationException or ArgumentException)
            {
                Unreadable.Add($"{Path.GetFileName(path)}: {e.Message}");
            }

            if (document is not null) yield return document;
        }
    }

    private IEnumerable<(string Key, GuiNode Node)> TopLevelBlocks(string dir)
    {
        foreach (var document in Documents(dir))
            foreach (var root in document.Roots)
                if (root.IsBlock && root.Key.Length > 0 && !root.Key.StartsWith('@'))
                    yield return (root.Key, root);
    }

    /// <summary>Every bare token inside a block, nested token runs included, quotes removed.</summary>
    private static IEnumerable<string> Tokens(GuiNode block)
    {
        foreach (var child in block.Children)
        {
            if (child.IsBlock)
            {
                foreach (string t in Tokens(child)) yield return t;
            }
            else if (child.Value is null)
            {
                foreach (string t in child.Head) if (t != "=") yield return GuiNode.Unquote(t);
            }
            else
            {
                yield return GuiNode.Unquote(child.Value);
            }
        }
    }

    /// <summary>
    /// One field on one line, comments dropped and <c>@</c> constants resolved. A comment kept on
    /// a single line would swallow the rest of the field, and the file declaring the constants is
    /// replaced along with the rest of common/landed_titles.
    /// </summary>
    internal static string Flatten(GuiNode node, Dictionary<string, string>? constants)
    {
        string Resolve(string token)
            => constants is not null && token.StartsWith('@') && constants.TryGetValue(token, out string? v) ? v : token;

        string head = string.Join(' ', node.Head.Select(Resolve));
        if (node.IsBlock)
        {
            string inner = string.Join(' ', node.Children.Select(c => Flatten(c, constants)));
            return (head.Length > 0 ? head + " " : "") + "{ " + inner + (inner.Length > 0 ? " " : "") + "}";
        }

        return node.Value is null ? head : $"{head} {Resolve(node.Value)}";
    }

    private static readonly Comparer<string> DateOrder = Comparer<string>.Create((a, b) =>
    {
        int[] pa = a.Split('.').Select(s => int.TryParse(s, out int n) ? n : 0).ToArray();
        int[] pb = b.Split('.').Select(s => int.TryParse(s, out int n) ? n : 0).ToArray();
        for (int i = 0; i < Math.Min(pa.Length, pb.Length); i++)
            if (pa[i] != pb[i]) return pa[i].CompareTo(pb[i]);
        return pa.Length.CompareTo(pb.Length);
    });

    private static bool IsDate(string key) => Regex.IsMatch(key, @"^\d+\.\d+\.\d+$");

    private static bool IsNumber(string token)
        => double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

    private static string? Unquote(string? value) => value is null ? null : GuiNode.Unquote(value);

    /// <summary>Display text fit to stand alone as a name, or null for anything carrying markup.</summary>CoA
    private static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim();
        if (text.IndexOfAny(['$', '[', ']', '#', '@', '|']) >= 0) return null;
        return text;
    }

    private static string Humanise(string key)
    {
        string bare = Regex.Replace(key, @"^(heritage|language)_", "");
        return string.Join(' ', bare.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
    }

    private static double[]? Numbers(GuiNode? color)
    {
        if (color is null || !color.IsBlock || color.Head.Any(h => h.StartsWith("hsv", StringComparison.Ordinal))) return null;
        var values = Tokens(color)
            .Select(t => double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : double.NaN)
            .ToArray();
        return values.Length == 3 && values.All(double.IsFinite) ? values : null;
    }

    private static (byte, byte, byte)? ParseByteColor(GuiNode? color)
    {
        if (Numbers(color) is not { } v) return null;
        double scale = v.Any(x => x > 1.0) ? 1.0 : 255.0;
        byte B(double x) => (byte)Math.Clamp(Math.Round(x * scale), 0, 255);
        return (B(v[0]), B(v[1]), B(v[2]));
    }

    private static (double, double, double) ParseUnitColor(GuiNode? color)
    {
        if (Numbers(color) is not { } v) return (0.5, 0.5, 0.5);
        double scale = v.Any(x => x > 1.0) ? 255.0 : 1.0;
        return (v[0] / scale, v[1] / scale, v[2] / scale);
    }
}
