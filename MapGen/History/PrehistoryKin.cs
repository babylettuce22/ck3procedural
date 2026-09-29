using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.Emit;

namespace Ck3MapGen.MapGen;

/// <summary>
/// The brothers and sisters nobody needed: children who never ruled, drawn after everything else so
/// the dynasty trees fan out instead of running as a single line of heirs.
///
/// Every past generation this generator writes exists because it had to — an invented parent so a
/// ruler's house does not begin with him, an applied history's past ruler because he held the title
/// — and each had exactly the children something else needed: the heir, a partition's younger son, a
/// daughter married off to seal an alliance. So a tree read as a pole, and the living ruler had no
/// uncles, no cousins and usually no brothers, which is to say no claimants and no kin to scheme with.
///
/// Each parent gets a family of one to five, counting the children it already has; the rest are
/// drawn here, inside the years the parent could have had them. A fifth die as children. Those whose
/// lives run past the start date are alive on it — landless, and seated by the engine in a relative's
/// court as it seats every living child the character file writes.
///
/// On a stream of its own keyed by the parent's id, and run after every other draw, so nothing else
/// in the world moves: the rulers, marriages and titles are exactly what they were, with more people
/// in the trees beside them.
/// </summary>
public sealed partial class PrehistoryMap
{
    private sealed record Parent(string Id, bool Female, int Born, int Died,
        string DynastyId, string? HouseKey, string CultureKey, string FaithKey);

    /// <summary>
    /// Draws the non-ruling children for every dead parent — the invented ancestors, and an applied
    /// history's past rulers — so call it after <see cref="AddPastRulers"/>.
    /// </summary>
    /// <returns>How many were added, and how many of those are alive on the start date.</returns>
    public (int Added, int Living) AddKin(CultureMap cultures, MapConfig cfg)
    {
        var cultureByKey = cultures.Cultures.GroupBy(c => c.Key)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        static int Year(string date) => int.Parse(date.Split('.')[0]);

        // Only parents that are written, so a sibling's `father =` always names somebody. A seat
        // parent out of an applied history sits in DeceasedParents without being written there — the
        // past-ruler block writes it — and is reached through PastRulers instead.
        var parents = new List<Parent>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in AllExtraCharacters)
            if (c is { IsDeadAncestor: true, IsKin: false, DeathDate: { } died } && seen.Add(c.Id))
                parents.Add(new Parent(c.Id, c.Female, Year(c.BirthDate), Year(died),
                    c.DynastyId, c.DynastyHouseKey, c.CultureKey, c.FaithKey));
        foreach (var p in PastRulers)
            if (seen.Add(p.Id))
                parents.Add(new Parent(p.Id, p.Female, Year(p.BirthDate), Year(p.DeathDate),
                    p.House.DynastyId, p.House.HouseKey, p.House.CultureKey, p.FaithKey));

        // The children each parent already has — rulers seated on its death, past rulers after it,
        // daughters married out — by birth year and name, so the new ones are counted against them,
        // spaced from them and named unlike them.
        var existing = new Dictionary<string, List<(int Born, string? Name)>>(StringComparer.Ordinal);
        void Child(string? parentId, int born, string? name)
        {
            if (parentId is null) return;
            if (!existing.TryGetValue(parentId, out var list)) existing[parentId] = list = [];
            list.Add((born, name));
        }
        foreach (var (seat, parent) in DeceasedParents) Child(parent.Id, HistoryWriter.GetRulerBirthYear(seat, cfg), null);
        foreach (var c in AllExtraCharacters)
        {
            Child(c.FatherId, Year(c.BirthDate), c.Name);
            Child(c.MotherId, Year(c.BirthDate), c.Name);
        }
        foreach (var p in PastRulers) Child(p.ParentId, Year(p.BirthDate), p.Name);

        int added = 0, living = 0;
        foreach (var parent in parents)
        {
            if (!cultureByKey.TryGetValue(parent.CultureKey, out var culture)) continue;

            // The years this parent could have had a child: sixteen on, a mother to forty-four, a
            // father to fifty-five, and never in or after the year they died.
            int lo = parent.Born + 17;
            int hi = Math.Min(Math.Min(parent.Died - 1, cfg.StartYear - 1), parent.Born + (parent.Female ? 44 : 55));
            if (hi < lo) continue;

            var rng = Rng.For(cfg.Seed, 0xF4A1, Rng.StableHash(parent.Id), cfg.PeopleSalt);
            var siblings = existing.GetValueOrDefault(parent.Id) ?? [];

            // A family of one to five, most often two to four.
            double roll = rng.NextDouble();
            int family = roll < 0.15 ? 1 : roll < 0.40 ? 2 : roll < 0.65 ? 3 : roll < 0.85 ? 4 : 5;
            int extra = Math.Max(0, family - siblings.Count);

            var names = siblings.Select(s => s.Name).OfType<string>().ToHashSet(StringComparer.Ordinal);
            var births = siblings.Select(s => s.Born).ToList();

            for (int i = 0; i < extra; i++)
            {
                bool female = rng.Chance(0.5);

                // A year clear of the others' where there is one; twins now and then where there is not.
                int born = rng.Int(lo, hi);
                for (int tries = 0; tries < 4 && births.Any(b => Math.Abs(b - born) < 1); tries++)
                    born = rng.Int(lo, hi);
                births.Add(born);

                string name = GivenName(culture, female, rng);
                for (int tries = 0; tries < 3 && names.Contains(name); tries++)
                    name = GivenName(culture, female, rng);
                names.Add(name);

                // A fifth die as children. The rest live out a life on the same curve the History
                // workspace's rulers die on — about 0.4% a year at thirty, doubling every eight.
                int died;
                if (rng.Chance(0.2)) died = born + rng.Int(1, 12);
                else
                {
                    int age = 16;
                    while (age < 95 && !rng.Chance(Math.Min(1.0, 0.004 * Math.Exp(0.085 * (age - 30))))) age++;
                    died = born + age;
                }

                // Past the start date means alive on it — unless that would make them older than
                // anyone the start date should open on.
                bool alive = died >= cfg.StartYear && cfg.StartYear - born <= 85;
                if (!alive && died >= cfg.StartYear) died = cfg.StartYear - rng.Int(1, 5);

                AllExtraCharacters.Add(new HistoricalCharacter
                {
                    Id = $"{parent.Id}_kin{i}",
                    Name = name,
                    Female = female,
                    DynastyId = parent.DynastyId,
                    DynastyHouseKey = parent.HouseKey,
                    CultureKey = parent.CultureKey,
                    FaithKey = parent.FaithKey,
                    BirthDate = $"{born}.{rng.Int(1, 12)}.{rng.Int(1, 28)}",
                    DeathDate = alive ? null : $"{died}.{rng.Int(1, 12)}.{rng.Int(1, 28)}",
                    FatherId = parent.Female ? null : parent.Id,
                    MotherId = parent.Female ? parent.Id : null,
                    IsDeadAncestor = !alive,
                    IsKin = true,
                });

                added++;
                if (alive) living++;
            }
        }

        return (added, living);
    }
}
