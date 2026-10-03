using Ck3MapGen.Config;
using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen;

public enum AdventurerRole { Captain, Scholar, Wanderer }

/// <summary>
/// A bookmark's camp leader, separate from landed rulers. Identity and provenance are stable;
/// current location and resources can later be advanced by a simulation. No simulator reads this
/// yet: each history write derives a fresh roster from its endpoint, never preserves frozen people.
/// </summary>
public sealed class Adventurer
{
    public required string Id { get; init; }
    public required string TitleKey { get; init; }
    public required string DynastyId { get; init; }
    public required string HouseKey { get; init; }
    public required int BookmarkYear { get; init; }
    public int? UntilYear { get; init; }
    public required Title Origin { get; init; }
    public required Culture Culture { get; init; }
    public required Faith Faith { get; init; }
    public required AdventurerRole Role { get; init; }
    public required string Name { get; init; }
    public required string DynastyName { get; init; }
    public required bool Female { get; init; }
    public required int Born { get; init; }
    /// <summary>Historical retirement/death before the next roster, capped to a plausible lifespan.</summary>
    public int? EndYear => UntilYear is { } until
        ? Math.Min(until, Math.Max(BookmarkYear + 1, Born + 80)) : null;
    public required Title Location { get; set; }
    public required int Gold { get; set; }
    public required int Prestige { get; set; }
}

public sealed record AdventurerRoster(IReadOnlyList<Adventurer> All)
{
    public static AdventurerRoster Empty { get; } = new(Array.Empty<Adventurer>());

    /// <summary>
    /// A bounded roster for every supported bookmark, on that date's settled ground. Separate
    /// per-county streams leave all existing people and the realm simulation's dice untouched.
    /// </summary>
    public static AdventurerRoster Build(MapConfig cfg, List<Title> counties, RealmMap realms,
        CultureMap cultures, FaithMap faiths, WildernessMap wilderness, BookmarkEras? eras)
    {
        if (!cfg.EnableAdventurers) return Empty;
        var dates = (eras?.Eras.Select(e => (e.Year, e.Realms)) ?? [])
            .Append((cfg.StartYear, realms)).OrderBy(e => e.Item1).ToList();
        var all = new List<Adventurer>();
        for (int d = 0; d < dates.Count; d++)
        {
            var (year, map) = dates[d];
            // A playable adult must fit before the date, including the generator's earliest dates.
            if (year < 20) continue;
            int? until = d + 1 < dates.Count ? dates[d + 1].Item1 : null;
            var eligible = counties.Where(c => map.HolderCounty.ContainsKey(c)
                && !wilderness.Contains(c) && c.Capital?.ProvinceId > 0).ToList();
            int target = Math.Min(eligible.Count, Math.Clamp((eligible.Count + 79) / 80, 3, 12));
            // Round-robin across heritages before giving a region another company. Hash ordering
            // keeps selection independent of dictionary order and other generation stages.
            var regions = eligible.GroupBy(c => cultures.For(c).Heritage.Key)
                .OrderBy(g => Rng.For(cfg.Seed, 0xAD71, Rng.StableHash(g.Key), year).NextUInt64())
                .Select(g => new Queue<Title>(g.OrderBy(c =>
                    Rng.For(cfg.Seed, 0xAD72, c.Index, year).NextUInt64()))).ToList();
            var selected = new List<Title>();
            while (selected.Count < target)
                foreach (var region in regions)
                {
                    if (selected.Count == target) break;
                    if (region.TryDequeue(out var county)) selected.Add(county);
                }
            for (int i = 0; i < selected.Count; i++)
            {
                var origin = selected[i];
                var culture = cultures.For(origin);
                var rng = Rng.For(cfg.Seed, 0xAD73, origin.Index, year ^ cfg.PeopleSalt);
                bool female = rng.Chance(0.35);
                var names = female ? culture.FemaleNames : culture.MaleNames;
                string key = $"{year}_{origin.Index}";
                all.Add(new Adventurer
                {
                    Id = $"gen_adv_{key}", TitleKey = $"d_gen_adv_{key}",
                    DynastyId = $"gen_adv_dyn_{key}", HouseKey = $"gen_adv_house_{key}",
                    BookmarkYear = year, UntilYear = until, Origin = origin, Location = origin,
                    Culture = culture, Faith = faiths.For(origin), Role = (AdventurerRole)(i % 3),
                    Name = names.Count > 0 ? rng.Pick(names) : culture.Name,
                    DynastyName = culture.Tongue.DynastyName(rng), Female = female,
                    Born = Math.Max(1, year - rng.Int(19, 48)),
                    Gold = rng.Int(30, 90), Prestige = rng.Int(100, 300),
                });
            }
        }
        return new AdventurerRoster(all);
    }
}
