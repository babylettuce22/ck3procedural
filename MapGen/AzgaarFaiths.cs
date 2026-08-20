using Ck3MapGen.Io;

namespace Ck3MapGen.MapGen;

/// <summary>
/// Turns the export's religions into the plan our faith generator builds from, so the religious map
/// in game *is* the export's — the same traditions, in the same places, under the same names —
/// rather than an invented one relabelled after the fact.
///
/// The mapping rests on Azgaar's own four-way <c>type</c>, which lines up with CK3's structure
/// better than it has any right to:
///
///   - An <b>Organized</b> religion is a church. It becomes a CK3 religion of its own, with itself
///     as the founding faith.
///   - A <b>Folk</b> religion is a people's paganism. It also stands alone — vanilla keeps Slavic
///     and Norse paganism as separate religions, not as faiths of one — with one faith, typically
///     unreformed.
///   - A <b>Heresy</b> or <b>Cult</b> is defined by what it deviates from. It becomes a faith
///     *inside* the religion of its nearest Organized or Folk ancestor, found by walking
///     <c>origins</c> — which is the whole value of that field: it is the ancestry DAG Azgaar drew
///     its religion tree from, and it hands us CK3's religion-to-faith nesting for free.
///
/// What is deliberately *not* imported: doctrines beyond the theism axis, tenets, virtues and sins,
/// holy sites, heads of faith. The export has nothing to say about any of those, and the generator
/// already builds them well; this plan decides the shape and the ground, and the generator dresses
/// it. <c>expansion</c> ("global"/"state"/"culture") is read nowhere yet either — what it describes,
/// how far a religion was allowed to spread, is already baked into the cell assignment.
/// </summary>
public static class AzgaarFaiths
{
    /// <summary>One faith the plan wants: the export religion it is, and the counties it holds.</summary>
    public sealed class PlannedFaith
    {
        public required AzgaarReligion Source { get; init; }
        public required List<Title> Counties { get; init; }
    }

    /// <summary>One CK3 religion the plan wants: its root tradition and the faiths inside it.</summary>
    public sealed class PlannedReligion
    {
        /// <summary>
        /// The tradition the group descends from. Usually also one of the faiths, but not always —
        /// a heresy can outlive its parent church on the map, and the root then only names the
        /// group.
        /// </summary>
        public required AzgaarReligion Root { get; init; }

        /// <summary>Largest first, so the founding faith is the one the religion reads as.</summary>
        public required List<PlannedFaith> Faiths { get; init; }
    }

    /// <summary>
    /// The county-to-religion assignment and the grouping above it, or null when the export has no
    /// religions worth building from — in which case the generated path runs exactly as it always
    /// has.
    /// </summary>
    public static List<PlannedReligion>? BuildPlan(AzgaarImport azgaar, List<Title> counties,
        RegionGrowth.Graph graph)
    {
        if (!azgaar.World.RealReligions.Any()) return null;

        // 1. Every county the export has an opinion about. Majority vote over the county's ground,
        //    same as every other binding — a county is its area, not its centre pixel.
        var religionOf = new int[counties.Count];
        var frontier = new Queue<int>();

        for (int i = 0; i < counties.Count; i++)
        {
            var share = azgaar.For(counties[i])?.Religion ?? AzgaarShare.None;
            religionOf[i] = share.Exists ? share.Id : 0;
            if (religionOf[i] > 0) frontier.Enqueue(i);
        }

        if (frontier.Count == 0) return null;

        // 2. Counties the export left godless — wilderness, misaligned ground, the odd islet —
        //    take the religion of the nearest county that has one, by flood over the county graph.
        //    A plain BFS in index order keeps it deterministic, and adjacency is the right measure:
        //    a faith reaches the valley next door before it reaches across the sea.
        while (frontier.Count > 0)
        {
            int i = frontier.Dequeue();
            foreach (int n in graph.Neighbours[i])
            {
                if (n >= religionOf.Length || religionOf[n] > 0) continue;
                religionOf[n] = religionOf[i];
                frontier.Enqueue(n);
            }
        }

        // 2b. A landmass with no bound county at all — an island chain the export never claimed —
        //     is out of the flood's reach, since adjacency stops at the water. Those counties take
        //     the map's commonest religion: with nothing local to go on, the majority tradition is
        //     the least wrong answer, and a deterministic one.
        if (Array.IndexOf(religionOf, 0) >= 0)
        {
            int commonest = religionOf.Where(r => r > 0)
                .GroupBy(r => r)
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
                .First().Key;

            for (int i = 0; i < religionOf.Length; i++)
                if (religionOf[i] == 0) religionOf[i] = commonest;
        }

        // 3. Which religions actually hold ground. Ones the map lost entirely are not built —
        //    a faith with no counties is not a faith, it is an error the history writer trips over.
        var held = new Dictionary<int, List<Title>>();
        for (int i = 0; i < counties.Count; i++)
        {
            if (religionOf[i] <= 0) continue;
            if (!held.TryGetValue(religionOf[i], out var list)) held[religionOf[i]] = list = [];
            list.Add(counties[i]);
        }

        if (held.Count == 0) return null;

        // 4. Group each present religion under its root tradition.
        var groups = new Dictionary<int, List<PlannedFaith>>();
        foreach (var (id, owned) in held.OrderBy(h => h.Key))
        {
            if (azgaar.World.Religion(id) is not { } source) continue;

            int root = RootOf(azgaar, id);
            if (!groups.TryGetValue(root, out var members)) groups[root] = members = [];
            members.Add(new PlannedFaith { Source = source, Counties = owned });
        }

        var plan = new List<PlannedReligion>();
        foreach (var (rootId, members) in groups.OrderBy(g => g.Key))
        {
            if (azgaar.World.Religion(rootId) is not { } root) continue;

            plan.Add(new PlannedReligion
            {
                Root = root,
                Faiths = members
                    .OrderByDescending(f => f.Counties.Count)
                    .ThenBy(f => f.Source.I)
                    .ToList(),
            });
        }

        return plan.Count > 0 ? plan : null;
    }

    /// <summary>
    /// The Organized or Folk tradition an export religion descends from — itself, when it is one.
    ///
    /// Walks the first listed origin only. A religion with several parents (a syncretic pantheon)
    /// has to live in one group, and Azgaar lists the dominant influence first. The visited set is
    /// not decorative: origins is user-editable in Azgaar, and a cycle there must cost a group
    /// assignment, not the whole run.
    /// </summary>
    private static int RootOf(AzgaarImport azgaar, int id)
    {
        var visited = new HashSet<int> { id };
        int current = id;

        while (azgaar.World.Religion(current) is { } religion)
        {
            if (religion.Type is "Organized" or "Folk") return current;

            int? parent = religion.Origins?.FirstOrDefault(o => o > 0);
            if (parent is not > 0 || !visited.Add(parent.Value)) return current;

            current = parent.Value;
        }

        return current;
    }

    /// <summary>
    /// Whether the form describes one supreme god, which is the one doctrine axis the export can
    /// answer. Everything else on the doctrine sheet stays generated.
    /// </summary>
    public static bool IsMonotheist(AzgaarReligion religion)
        => religion.Form is "Monotheism";

    /// <summary>
    /// Doctrine keys worth trying for the group's theism, most specific first, for the forms that
    /// have a CK3 counterpart beyond the monotheist/polytheist pair. Null means no opinion. The
    /// caller's Prefer() falls back to its own choice when the install has none of these, so a
    /// guess here costs nothing when it misses.
    /// </summary>
    public static string[]? TheismPreference(AzgaarReligion religion) => religion.Form switch
    {
        "Dualism" => ["doctrine_dualist", "doctrine_dualism"],
        _ => null,
    };

    /// <summary>
    /// The deity as CK3 wants to print it. Azgaar writes "Bundushur, The Red Owl"; the part before
    /// the comma is the name, and the epithet after it reads as broken text in the middle of an
    /// event sentence.
    /// </summary>
    public static string? DeityName(AzgaarReligion religion)
    {
        if (religion.Deity is not { Length: > 0 } deity) return null;

        int comma = deity.IndexOf(',');
        string name = (comma > 0 ? deity[..comma] : deity).Trim();
        return name.Length > 0 ? name : null;
    }
}
