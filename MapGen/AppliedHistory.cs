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

    /// <summary>One realm, by county index. <see cref="Suzerain"/> is another realm's <see cref="Id"/>.</summary>
    public sealed record Realm(int Id, int Capital, int? Suzerain, string Culture, int Founded, int Peak, int[] Counties);

    /// <summary>
    /// A dynasty and the house of it that rules, carried by value: the history is laid over a world
    /// generated again, whose prehistory knows nothing of the one the history started from.
    /// </summary>
    public sealed record Lineage(string DynastyId, string DynastyNameKey, string DynastyName, string CultureKey,
        string HouseKey, string HouseNameKey, string HouseName, string? Prefix);

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
        RulerMap? rulers = null, PrehistoryMap? prehistory = null)
    {
        var seatLineage = new Dictionary<int, Lineage>();
        if (rulers is not null && prehistory is not null)
            foreach (var ruler in rulers.All)
                if (LineageOf(ruler, prehistory) is { } line) seatLineage[ruler.Seat.Index] = line;

        var realmLineage = new Dictionary<int, Lineage>();
        foreach (var p in sim.Realms)
            if (sim.StartCapitals.TryGetValue(p.Id, out var seat) && seatLineage.TryGetValue(seat.Index, out var line))
                realmLineage[p.Id] = line;

        return new()
        {
            Year = sim.Year,
            FromYear = sim.StartYear,
            Ground = GroundOf(counties),
            NextId = sim.NextId,
            Realms = [.. sim.Realms.OrderBy(p => p.Id).Select(p => new Realm(
                p.Id, p.Capital.Index, p.Suzerain?.Id, p.Culture.Key, p.Founded, p.Peak,
                [.. p.Counties.Select(c => c.Index).Order()]))],
            RealmLineage = realmLineage,
            SeatLineage = seatLineage,
        };
    }

    /// <summary>
    /// A ruler's dynasty, by its senior house — the line a cadet branch came off, as the additional
    /// bookmarks carry it: a branch is one ruler's, the dynasty is what outlasts him.
    /// </summary>
    private static Lineage? LineageOf(Ruler ruler, PrehistoryMap prehistory)
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
    public Dictionary<Title, Lineage> LineageFor(RealmMap realms, FormationHistory history, WildernessMap wilderness)
    {
        var polityAt = history.Polities.ToDictionary(p => p.Capital, p => p.Id);
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
