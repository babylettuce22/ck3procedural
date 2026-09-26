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

    /// <summary>The history as it stands, lifted off the objects it runs on.</summary>
    public static AppliedHistory Capture(HistorySim sim, IEnumerable<Title> counties)
        => new()
        {
            Year = sim.Year,
            FromYear = sim.StartYear,
            Ground = GroundOf(counties),
            NextId = sim.NextId,
            Realms = [.. sim.Realms.OrderBy(p => p.Id).Select(p => new Realm(
                p.Id, p.Capital.Index, p.Suzerain?.Id, p.Culture.Key, p.Founded, p.Peak,
                [.. p.Counties.Select(c => c.Index).Order()]))],
        };

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
    public FormationHistory? Resolve(List<Title> counties, CultureMap cultures, FormationHistory generated,
        out string? problem)
    {
        problem = null;

        if (generated.Rules is not { } rules)
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
