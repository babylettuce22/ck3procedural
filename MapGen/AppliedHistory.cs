using System.Security.Cryptography;
using System.Text;

namespace Ck3MapGen.MapGen;

/// <summary>
/// The realms as the History workspace left them in some year, to be written as the world's start
/// in place of the realms the formation grew.
///
/// Holds no <see cref="Title"/> or <see cref="Culture"/> objects, only indices and keys. A write
/// regenerates the whole world from the settings, so the objects the history was simulated on are
/// not the ones it will be laid on; what carries across is the county's index, which the title
/// hierarchy hands out once and never moves, and the ground under it. <see cref="Ground"/> is that
/// ground's fingerprint, and a world whose counties do not match it refuses the history outright
/// rather than laying it over land it was never simulated on.
///
/// Only the realm map is replaced. Titles, cultures, faiths, development and wilderness are the
/// generated world's own — see <see cref="Emit.ContentWriter.BuildWorld"/> for where the swap
/// happens and why the faiths are decided before it.
/// </summary>
public sealed class AppliedHistory
{
    /// <summary>
    /// Kept beside the mod whenever the mod was written with a history, and only then, so that
    /// writing the same mod again — after a restart, say — writes the same history rather than
    /// quietly going back to the generated realms. Spared by <see cref="Emit.ModWriter.ClearModDir"/>.
    /// </summary>
    public const string FileName = "proctool_history.json";

    private static readonly System.Text.Json.JsonSerializerOptions Json = new() { WriteIndented = false };

    /// <summary>Records this history beside the mod in <paramref name="modDir"/>.</summary>
    public void Save(string modDir)
        => File.WriteAllText(Path.Combine(modDir, FileName), System.Text.Json.JsonSerializer.Serialize(this, Json));

    /// <summary>Forgets any history recorded beside the mod: the mod was written without one.</summary>
    public static void Forget(string modDir)
    {
        string path = Path.Combine(modDir, FileName);
        if (File.Exists(path)) File.Delete(path);
    }

    /// <summary>The history recorded beside the mod, or null when there is none or it cannot be read.</summary>
    public static AppliedHistory? Load(string modDir)
    {
        string path = Path.Combine(modDir, FileName);
        if (!File.Exists(path)) return null;
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<AppliedHistory>(File.ReadAllText(path), Json);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException or NotSupportedException)
        {
            Console.WriteLine($"  {FileName}: could not be read ({ex.Message}); the generated realms are used");
            return null;
        }
    }

    /// <summary>
    /// Why this history cannot be laid over a world with these titles, or null when the ground
    /// matches. The check that can be made before anything is written — see
    /// <see cref="Core.Generator.WriteMod"/>; <see cref="Resolve"/> makes the rest.
    /// </summary>
    public string? Mismatch(IEnumerable<Title> titles)
        => GroundOf(Titles.Flatten(titles.ToList())) == Ground
            ? null
            : $"the history of {Year} was run on a different county map — a different heightmap, seed or "
              + "county setting. Discard it in the History workspace, or go back to the settings it was run with";

    /// <summary>The year the realms stand at: the start date the world is written with.</summary>
    public required int Year { get; init; }

    /// <summary>The start date the history was run on from.</summary>
    public required int FromYear { get; init; }

    /// <summary>Which counties, on which baronies, the history was simulated over.</summary>
    public required string Ground { get; init; }

    public required List<Realm> Realms { get; init; }

    /// <summary>The id the next realm born takes, so a history resumed from this one never reuses one.</summary>
    public required int NextId { get; init; }

    /// <summary>
    /// One realm, by county index. <see cref="Suzerain"/> is another realm's <see cref="Id"/>.
    /// <see cref="Ruler"/>, <see cref="RulerFemale"/> and <see cref="RulerBorn"/> are the person the
    /// History workspace had on its throne, written as the realm's ruler; absent in a file saved
    /// before rulers were simulated, when the seat's ruler is drawn as any other.
    /// </summary>
    public sealed record Realm(int Id, int Capital, int? Suzerain, string Culture, int Founded, int Peak, int[] Counties,
        string? Ruler = null, bool RulerFemale = false, int RulerBorn = 0);

    /// <summary>
    /// A dynasty and the house of it that rules, carried by value: the history is laid over a world
    /// generated again, whose prehistory knows nothing of the one the history started from.
    /// </summary>
    public sealed record Lineage(string DynastyId, string DynastyNameKey, string DynastyName, string CultureKey,
        string HouseKey, string HouseNameKey, string HouseName, string? Prefix);

    /// <summary>
    /// The de jure tree as drift left it: every duchy's kingdom and every kingdom's empire the
    /// simulation tracked, by title key — all of them, not just the ones that moved, so laying it
    /// over the generated tree and over an already-drifted one come out the same. Empty in a file
    /// saved before drift was simulated.
    /// </summary>
    public Dictionary<string, string> DeJure { get; init; } = [];

    /// <summary>
    /// Moves the titles <see cref="DeJure"/> says have drifted to their new parents, in the tree
    /// the world is about to be written from. A moved title goes in among its new siblings in index
    /// order — the order the generator made them in — so the tree comes out the same whatever order
    /// the moves happen in. A title that lost the child its capital sat in is re-seated on its most
    /// developed remaining one.
    /// </summary>
    /// <returns>How many titles moved.</returns>
    public int ApplyDeJure(List<Title> empires, Dictionary<Title, int> development)
    {
        if (DeJure.Count == 0) return 0;

        var byKey = Titles.Flatten(empires).GroupBy(t => t.Key).ToDictionary(g => g.Key, g => g.First());
        int moved = 0;

        foreach (var (key, parentKey) in DeJure.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (!byKey.TryGetValue(key, out var title) || !byKey.TryGetValue(parentKey, out var parent)) continue;
            if (title.Parent == parent || title.Parent is not { } old) continue;

            old.Children.Remove(title);
            int at = parent.Children.FindIndex(c => c.Index > title.Index);
            parent.Children.Insert(at < 0 ? parent.Children.Count : at, title);
            title.Parent = parent;
            moved++;

            if (old.Seat == title)
                old.Seat = old.Children
                    .OrderByDescending(c => Capitals.CapitalCounty(c) is { } seat ? development.GetValueOrDefault(seat) : -1)
                    .ThenBy(c => c.Index)
                    .FirstOrDefault();
        }

        return moved;
    }

    /// <summary>
    /// The colour the History workspace had each realm in when the history was captured, by realm id,
    /// packed 0xRRGGBB. Kept so the World workspace, and the History workspace after it, show the
    /// applied world in the colours the user was watching it in. Presentation only.
    /// </summary>
    public Dictionary<int, int> Colours { get; init; } = [];

    /// <summary><see cref="Colours"/> on the titled world, by the seat each realm is ruled from.</summary>
    public Dictionary<Title, (byte R, byte G, byte B)> ColoursFor(IReadOnlyDictionary<int, Title> capitals)
    {
        var result = new Dictionary<Title, (byte R, byte G, byte B)>();
        foreach (var (id, rgb) in Colours)
            if (capitals.TryGetValue(id, out var seat))
                result[seat] = ((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return result;
    }

    /// <summary>
    /// A reign that ended in death before the applied date, in a realm still standing then — the
    /// predecessors a title's history in game lists. See <see cref="PastRulersFor"/>.
    /// </summary>
    public sealed record Reign(int RealmId, string Name, bool Female, int Born, int Crowned, int Died, Lineage House);

    /// <summary>
    /// Past reigns, most recent first within a realm, cut by the backstop in <see cref="Capture"/>.
    /// Empty in a file saved before rulers were simulated.
    /// </summary>
    public List<Reign> Reigns { get; init; } = [];

    /// <summary>
    /// The backstop on how many dead rulers an applied history writes. A long history of partitions
    /// runs through thousands of reigns, and every one would be a character in the file and an
    /// entry in a title's history. Twelve is more predecessors than a title's history window shows
    /// comfortably; the total keeps a millennium-long run of a thousand-county map within the size of
    /// the rest of the character file. Larger realms are served first.
    /// </summary>
    public const int MaxReignsPerRealm = 12, MaxReigns = 1500;

    /// <summary>
    /// The house that ruled each realm when the history began, by realm id — only for realms that
    /// were already standing then. A realm that endures keeps it; one born since founds its own.
    /// </summary>
    public Dictionary<int, Lineage> RealmLineage { get; init; } = [];

    /// <summary>
    /// The house of every ruler seated when the history began, by seat county index — for the lords
    /// inside realms, who keep the local family with a chance that falls with the years passed.
    /// </summary>
    public Dictionary<int, Lineage> SeatLineage { get; init; } = [];

    /// <summary>
    /// The history as it stands, lifted off the objects it runs on. <paramref name="rulers"/> and
    /// <paramref name="prehistory"/> are the people of the world the history started from; without
    /// them every realm founds a new house.
    /// </summary>
    public static AppliedHistory Capture(HistorySim sim, IEnumerable<Title> counties,
        RulerMap? rulers = null, PrehistoryMap? prehistory = null,
        IReadOnlyDictionary<int, (byte R, byte G, byte B)>? colours = null)
    {
        var seatLineage = new Dictionary<int, Lineage>();
        if (rulers is not null && prehistory is not null)
            foreach (var ruler in rulers.All)
                if (LineageOf(ruler, prehistory) is { } line) seatLineage[ruler.Seat.Index] = line;

        // Each realm's house is its ruler's: carried from the start date when it ruled then, minted
        // when the history founded it. A realm with no ruler — none, once the simulation has run a
        // year — falls back to the house its start-date seat had.
        var realmLineage = new Dictionary<int, Lineage>();
        foreach (var p in sim.Realms)
        {
            if (sim.RulerOf(p) is { } ruler)
                realmLineage[p.Id] = ruler.House.Carried ?? Minted(ruler.House, sim.StartYear);
            else if (sim.StartCapitals.TryGetValue(p.Id, out var seat) && seatLineage.TryGetValue(seat.Index, out var line))
                realmLineage[p.Id] = line;
        }

        return new()
        {
            Year = sim.Year,
            FromYear = sim.StartYear,
            Ground = GroundOf(counties),
            NextId = sim.NextId,
            Realms = [.. sim.Realms.OrderBy(p => p.Id).Select(p => sim.RulerOf(p) is { } r
                ? new Realm(p.Id, p.Capital.Index, p.Suzerain?.Id, p.Culture.Key, p.Founded, p.Peak,
                    [.. p.Counties.Select(c => c.Index).Order()], r.Name, r.Female, r.Born)
                : new Realm(p.Id, p.Capital.Index, p.Suzerain?.Id, p.Culture.Key, p.Founded, p.Peak,
                    [.. p.Counties.Select(c => c.Index).Order()]))],
            RealmLineage = realmLineage,
            SeatLineage = seatLineage,
            Reigns = PastReigns(sim),
            DeJure = sim.DeJureMap().ToDictionary(kv => kv.Key.Key, kv => kv.Value.Key),
            Colours = colours?.ToDictionary(kv => kv.Key, kv => kv.Value.R << 16 | kv.Value.G << 8 | kv.Value.B) ?? [],
        };
    }

    /// <summary>
    /// The reigns that ended in death, in realms still standing, under the backstop: larger realms
    /// first, each keeping its <see cref="MaxReignsPerRealm"/> most recent, until
    /// <see cref="MaxReigns"/> are taken. A realm swallowed before the applied date writes no
    /// predecessors — it has no title to have held.
    /// </summary>
    private static List<Reign> PastReigns(HistorySim sim)
    {
        var dead = sim.Reigns
            .Where(r => r.Ruler.Died is not null)
            .GroupBy(r => r.PolityId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.Ruler).OrderByDescending(r => r.Died).ThenByDescending(r => r.Id).ToList());

        var reigns = new List<Reign>();
        foreach (var p in sim.Realms.OrderByDescending(p => p.Counties.Count).ThenBy(p => p.Capital.Index))
        {
            if (!dead.TryGetValue(p.Id, out var rulers)) continue;
            foreach (var ruler in rulers.Take(MaxReignsPerRealm))
            {
                if (reigns.Count >= MaxReigns) return reigns;
                reigns.Add(new Reign(p.Id, ruler.Name, ruler.Female, ruler.Born, ruler.Crowned, ruler.Died!.Value,
                    ruler.House.Carried ?? Minted(ruler.House, sim.StartYear)));
            }
        }
        return reigns;
    }

    /// <summary>
    /// The past rulers of the titled applied world, dated for its title history: each realm's
    /// predecessors on the primary title its capital's holder has now, the first from the year he
    /// was crowned and each after from the day after the one before him died. Only reigns that began
    /// before <paramref name="grantYear"/> — the date every current holder is granted his titles
    /// on — are written: a later one would sit between the grant and the start and contradict it.
    /// </summary>
    public List<PastRuler> PastRulersFor(IReadOnlyDictionary<int, Title> capitals, RealmMap realms,
        FaithMap faiths, int grantYear, int startYear)
    {
        var seats = realms.HolderCounty.Values.ToHashSet();
        var result = new List<PastRuler>();

        foreach (var group in Reigns.GroupBy(r => r.RealmId))
        {
            if (!capitals.TryGetValue(group.Key, out var seat) || !seats.Contains(seat)) continue;
            var title = Emit.HistoryWriter.Primary(seat, realms);
            string faith = faiths.For(seat).Key;

            (int Y, int M, int D)? previousDeath = null;
            int n = 0;
            foreach (var reign in group.OrderBy(r => r.Crowned).ThenBy(r => r.Died))
            {
                var start = previousDeath is { } before ? NextDay(before) : (reign.Crowned, 1, 1);
                if (start.Item1 >= grantYear) break;

                string id = $"gen_char_hist_{reign.RealmId}_{n++}_{FromYear}";
                var rng = new Core.Rng(unchecked((int)Core.Rng.StableHash(id)));
                (int Y, int M, int D) death = (Math.Min(reign.Died, startYear - 1), rng.Int(1, 12), rng.Int(1, 28));
                if (Before(death, NextDay(start))) death = NextDay(start);
                var birth = (Math.Min(reign.Born, start.Item1 - 1), rng.Int(1, 12), rng.Int(1, 28));

                result.Add(new PastRuler(id, reign.Name, reign.Female, reign.House, faith,
                    Date(birth), Date(death), title.Key, Date(start)));
                previousDeath = death;
            }
        }

        return result;

        static (int, int, int) NextDay((int Y, int M, int D) d)
            => d.D < 28 ? (d.Y, d.M, d.D + 1) : d.M < 12 ? (d.Y, d.M + 1, 1) : (d.Y + 1, 1, 1);
        static bool Before((int Y, int M, int D) a, (int Y, int M, int D) b)
            => a.Y != b.Y ? a.Y < b.Y : a.M != b.M ? a.M < b.M : a.D < b.D;
        static string Date((int Y, int M, int D) d) => $"{d.Y}.{d.M}.{d.D}";
    }

    /// <summary>
    /// Keys for a house the history founded. The history's own start year is in them, so a second
    /// history run on from an applied one mints keys the first cannot have used, and none of them
    /// can meet a key the generator writes (<c>gen_dynasty_12</c>, <c>gen_dynasty_12_y1150</c>).
    /// </summary>
    private static Lineage Minted(SimHouse house, int fromYear)
    {
        string tag = $"h{house.Id}_{fromYear}";
        return new Lineage($"gen_dynasty_{tag}", $"dynn_gen_{tag}", house.Name, house.Culture.Key,
            $"house_gen_{tag}", $"dynn_gen_{tag}", house.Name, PrehistoryMap.CulturePrefix(house.Culture.Key));
    }

    /// <summary>
    /// The people the History workspace had on the thrones, by the seat county index they rule from
    /// in the titled world — what <see cref="Config.MapConfig.SeatPeople"/> hands the character draws.
    /// </summary>
    /// <param name="capitals">As for <see cref="LineageFor"/>: before titling, so a folded realm's
    /// ruler is still the lord of his capital.</param>
    public Dictionary<int, (string Name, bool Female, int Born)> PeopleFor(IReadOnlyDictionary<int, Title> capitals,
        RealmMap realms)
    {
        var seats = realms.HolderCounty.Values.ToHashSet();
        var people = new Dictionary<int, (string Name, bool Female, int Born)>();
        // Every current holder is granted his titles five years before the start (HistoryWriter's
        // grant date), so a ruler has to be born by then or CK3 hands a title to nobody. The
        // simulation crowns children, and a child crowned in the last few years is written a
        // little older than he is.
        int bornBy = Year - 6;
        foreach (var realm in Realms)
            if (realm.Ruler is { } name && realm.RulerBorn > 0
                && capitals.TryGetValue(realm.Id, out var capital) && seats.Contains(capital))
                people[capital.Index] = (name, realm.RulerFemale, Math.Min(realm.RulerBorn, bornBy));
        return people;
    }

    /// <summary>The dynasties that ruled somewhere when the history began, for telling an enduring house from a new one.</summary>
    public HashSet<string> StartDynasties() => SeatLineage.Values.Select(l => l.DynastyId).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// A ruler's dynasty, by its senior house — the line a cadet branch came off, as the additional
    /// bookmarks carry it: a branch is one ruler's, the dynasty is what outlasts him.
    /// </summary>
    internal static Lineage? LineageOf(Ruler ruler, PrehistoryMap prehistory)
    {
        if (!prehistory.Dynasties.TryGetValue(ruler.DynastyId, out var dynasty)) return null;
        if (!prehistory.Houses.TryGetValue(dynasty.MainHouseKey, out var house)) return null;
        return new Lineage(dynasty.Id, dynasty.NameKey, dynasty.LocalizedName, dynasty.CultureKey,
            house.Key, house.NameKey, house.LocalizedName, house.Prefix);
    }

    /// <summary>
    /// Which ruling seats of the titled applied world carry a house from the start, and which. A
    /// realm's own seat carries its realm's; any other seat — a lord inside a realm — keeps the house
    /// that held it then with a chance of 1 - years/280 (0.1 to 0.9), the additional bookmarks' rule:
    /// most count houses last a century, few last three.
    /// </summary>
    /// <param name="capitals">Every realm's capital by id, taken before titling folds any: a realm
    /// folded into its lord — a younger brother with no tier left to stand on — still sits in its
    /// capital as a lord inside the realm, and is still of its house.</param>
    public Dictionary<Title, Lineage> LineageFor(RealmMap realms, IReadOnlyDictionary<int, Title> capitals,
        WildernessMap wilderness)
    {
        var polityAt = capitals.ToDictionary(kv => kv.Value, kv => kv.Key);
        double endures = Math.Clamp(1.0 - (Year - FromYear) / 280.0, 0.1, 0.9);
        var result = new Dictionary<Title, Lineage>();

        foreach (var seat in realms.HolderCounty.Values.Distinct().Where(c => !wilderness.Contains(c)))
        {
            if (polityAt.TryGetValue(seat, out int id) && RealmLineage.TryGetValue(id, out var realm))
                result[seat] = realm;
            else if (SeatLineage.TryGetValue(seat.Index, out var local)
                     && new Core.Rng(seat.Index ^ 0x51D1 ^ Year).NextDouble() < endures)
                result[seat] = local;
        }

        return result;
    }

    /// <summary>
    /// A fingerprint of the county map: every county's index and the provinces of its baronies. Two
    /// worlds agree on it exactly when a county index means the same ground in both.
    /// </summary>
    public static string GroundOf(IEnumerable<Title> counties)
    {
        var text = new StringBuilder();
        foreach (var county in counties.Where(c => c.Tier == "c").OrderBy(c => c.Index))
        {
            text.Append(county.Index).Append(':');
            foreach (int id in county.Children.Select(b => b.ProvinceId).Order()) text.Append(id).Append(',');
            text.Append(';');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..24];
    }

    /// <summary>
    /// The history laid onto a freshly generated world, as a formation history the titling step can
    /// dress — or null, with <paramref name="problem"/> saying why, when it does not fit that world.
    /// </summary>
    /// <param name="generated">The formation this world grew on its own. Its rules are kept, so
    /// the History workspace can run on again from the applied date; its realms are what this
    /// replaces, and the ground they cover is the ground the applied realms must cover too.</param>
    public FormationHistory? Resolve(List<Title> counties, CultureMap cultures, FormationHistory? generated,
        out string? problem)
    {
        problem = null;

        if (generated?.Rules is not { } rules)
        {
            problem = "this world's realms were not grown by the formation, so there is nothing to lay a history over";
            return null;
        }

        if (GroundOf(counties) != Ground)
        {
            problem = "the county map has changed since the history was run — a different heightmap, seed or "
                    + "county setting";
            return null;
        }

        var byIndex = counties.Where(c => c.Tier == "c").ToDictionary(c => c.Index);
        var cultureByKey = cultures.Cultures.GroupBy(c => c.Key).ToDictionary(g => g.Key, g => g.First());

        var polities = new Dictionary<int, Polity>();
        var owner = new Dictionary<Title, Polity>();

        foreach (var realm in Realms)
        {
            if (!byIndex.TryGetValue(realm.Capital, out var capital))
            {
                problem = $"realm {realm.Id} is seated in county {realm.Capital}, which this world does not have";
                return null;
            }

            var p = new Polity
            {
                Id = realm.Id,
                Capital = capital,
                Culture = cultureByKey.GetValueOrDefault(realm.Culture)
                          ?? rules.CountyCulture.GetValueOrDefault(capital)
                          ?? cultures.For(capital),
                Founded = realm.Founded,
                Peak = realm.Peak,
            };

            foreach (int index in realm.Counties)
            {
                if (!byIndex.TryGetValue(index, out var county))
                {
                    problem = $"realm {realm.Id} holds county {index}, which this world does not have";
                    return null;
                }
                if (!owner.TryAdd(county, p))
                {
                    problem = $"county {county.Name} is held by two realms";
                    return null;
                }
                p.Counties.Add(county);
            }

            if (!p.Counties.Contains(capital))
            {
                problem = $"realm {realm.Id}'s capital {capital.Name} is outside its own ground";
                return null;
            }

            polities[realm.Id] = p;
        }

        // The same ground the generated realms cover: every settled county, and no wilderness. A
        // county the history never held, or one this world made wild, would leave the titling step
        // with a county nobody rules or a ruler of empty land.
        var expected = generated.Owner.Keys.ToHashSet();
        if (!expected.SetEquals(owner.Keys))
        {
            int missing = expected.Count(c => !owner.ContainsKey(c));
            int extra = owner.Keys.Count(c => !expected.Contains(c));
            problem = $"the history covers different ground from this world's settled counties "
                    + $"({missing} unheld, {extra} not settled here) — the wilderness settings have changed";
            return null;
        }

        foreach (var realm in Realms)
        {
            if (realm.Suzerain is not { } lord) continue;
            if (!polities.TryGetValue(lord, out var suzerain))
            {
                problem = $"realm {realm.Id} answers to realm {lord}, which the history does not have";
                return null;
            }
            polities[realm.Id].Suzerain = suzerain;
        }

        foreach (var p in polities.Values)
        {
            int depth = 0;
            for (var q = p.Suzerain; q is not null; q = q.Suzerain)
                if (++depth > Polity.MaxDepth)
                {
                    problem = $"realm {p.Id}'s chain of homage is deeper than {Polity.MaxDepth}, or loops";
                    return null;
                }
        }

        return new FormationHistory
        {
            Polities = [.. polities.Values.OrderBy(p => p.Capital.Index)],
            Owner = owner,
            Events = [],
            FirstYear = generated.FirstYear,
            Rules = new FormationRules
            {
                Adjacent = rules.Adjacent,
                Development = rules.Development,
                CountyCulture = rules.CountyCulture,
                AvgKingdom = rules.AvgKingdom,
                Reach = rules.Reach,
                Aggression = rules.Aggression,
                Turbulence = rules.Turbulence,
                Seed = rules.Seed,
                NextId = Math.Max(rules.NextId, NextId),
            },
        };
    }
}
