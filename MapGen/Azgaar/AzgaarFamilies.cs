using Ck3MapGen.Io;

namespace Ck3MapGen.MapGen;

/// <summary>
/// Which of the export's peoples are relatives — the one thing an Azgaar export has cultures for and
/// never says.
///
/// CK3 needs two levels. A heritage owns a language and a look; the cultures inside it can accept
/// each other, hybridise and diverge, and cultures in different heritages effectively cannot. So a
/// world where every culture is its own heritage is a world of permanent strangers, which is the
/// point <see cref="Config.MapConfig.CulturesPerHeritage"/> is documented on. Azgaar has no heritage
/// tier at all: it has cultures and it has <c>origins</c>, an ancestry DAG that ought to be exactly
/// this and on real exports is not.
///
/// <b>Why origins is not enough.</b> It is the field the import was designed around, and on a map
/// whose author drew a culture tree by hand it is the right answer and is still used first. But
/// Azgaar's generator writes <c>origins: [0]</c> — descended from Wildlands, i.e. from nothing — for
/// every culture it makes. Measured on the Fleunland export: eleven cultures, eleven identical
/// origins. Walking that DAG gives each culture its own family and the two-level structure collapses
/// into one, so something else has to carry it.
///
/// <b>What carries it instead.</b> Two signals, in order.
///
/// The first is <c>base</c>, the name corpus a culture draws its people's names from. This is not a
/// heuristic standing in for ancestry — it is the same fact, stated on the axis that matters here.
/// A heritage owns one <see cref="Language"/> and that language is built from the founder's corpus,
/// so two cultures sharing a base *already* produce byte-identical languages; grouping them merges
/// something that was duplicated rather than inventing a relationship. On Fleunland it puts the two
/// Elven cultures together and the two Dwarven ones together, which is plainly what their author
/// meant.
///
/// The second is geography, and it is the one place a config knob is allowed authority over an
/// import. It applies only to what is left over, only while the family count is above
/// <see cref="Config.MapConfig.CulturesPerHeritage"/>, and it only ever groups cultures whose ground
/// touches. That is legitimate where overriding the *culture* count would not be, because the export
/// states its cultures and does not state their families: this is filling a silence, not overruling
/// an answer. Setting the knob to 1 switches the pass off entirely.
///
/// <b>The race gate.</b> Nothing here merges two families the export tagged differently. A
/// fantasy-preset export labels its cultures "Dunirr (Dwarven)", "Quenian (Elfish)", and a geographic
/// pass that put elves and dwarves in one language family would be visibly wrong on a map where
/// every other layer respects the tag. The gate reads the tag itself rather than the race it
/// resolves to — <see cref="AzgaarNaming.ParseRace"/> has no entry for "Arachnid" or "Drakonic", but
/// the export writing those words has still said those peoples are not each other, and taking the
/// absence of an archetype as the absence of a race is what put the spiders in with the elves the
/// first time this ran.
/// </summary>
public static class AzgaarFamilies
{
    /// <summary>
    /// The family each culture belongs to, as the culture id at its head, and a phrase naming the
    /// signal that decided it — which goes straight into the run's console report, because "eleven
    /// peoples in seven families" is only meaningful alongside what made them families.
    /// </summary>
    public sealed record Grouping(Dictionary<int, int> RootOf, string Basis);

    /// <summary>
    /// Groups the export's peoples into families.
    /// </summary>
    /// <param name="live">Every real culture in the export, by id. Wildlands is not one.</param>
    /// <param name="held">The counties each of those cultures actually holds.</param>
    /// <param name="graph">The county adjacency graph, for the geographic pass.</param>
    /// <param name="countyIndex">Where each county sits in <paramref name="graph"/>.</param>
    /// <param name="culturesPerHeritage">
    /// The family size the geographic pass aims at. A target, not a floor: the race gate and the
    /// requirement that families touch can both leave it unmet, and unmet is the correct outcome
    /// rather than a failure — better a world of eight families than one that put spiders and giants
    /// in the same language.
    /// </param>
    public static Grouping Group(
        IReadOnlyDictionary<int, AzgaarCulture> live,
        IReadOnlyDictionary<int, List<Title>> held,
        RegionGrowth.Graph graph,
        IReadOnlyDictionary<Title, int> countyIndex,
        double culturesPerHeritage)
    {
        var ids = held.Keys.Where(live.ContainsKey).OrderBy(i => i).ToList();
        if (ids.Count == 0) return new Grouping([], "nothing");

        // --- 1. Ancestry, where the export actually drew one ---------------------------------------
        //
        // Unchanged from the original import and deliberately first: a hand-drawn culture tree is a
        // direct statement of exactly this, and nothing below can improve on it. It counts as drawn
        // only when at least one culture descends from another — an export where every culture came
        // from Wildlands has said nothing, and saying nothing is not the same as saying "unrelated".
        var ancestral = new Dictionary<int, int>();
        bool drawn = false;

        foreach (int id in ids)
        {
            int root = Ancestor(id, live);
            ancestral[id] = root;
            if (root != id) drawn = true;
        }

        if (drawn) return new Grouping(ancestral, "its own ancestry");

        // --- 2. Shared name corpus -----------------------------------------------------------------
        var parent = ids.ToDictionary(i => i, i => i);

        int Find(int i)
        {
            while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; }
            return i;
        }

        void Union(int a, int b)
        {
            a = Find(a);
            b = Find(b);
            if (a == b) return;
            if (b < a) (a, b) = (b, a);   // lowest id wins, so the result does not depend on order
            parent[b] = a;
        }

        var byCorpus = new Dictionary<int, List<int>>();
        foreach (int id in ids)
        {
            int corpus = live[id].Base;
            if (corpus < 0) continue;
            if (!byCorpus.TryGetValue(corpus, out var sharing)) byCorpus[corpus] = sharing = [];
            sharing.Add(id);
        }

        foreach (var (_, sharing) in byCorpus.OrderBy(kv => kv.Key))
            for (int k = 1; k < sharing.Count; k++) Union(sharing[0], sharing[k]);

        bool byBase = Count(ids, Find) < ids.Count;

        // --- 3. Geography, for whatever the corpus left alone ---------------------------------------
        var race = ids.ToDictionary(i => i, i => Race(live[i].Name));
        var contact = Contact(ids, held, graph, countyIndex);

        int target = Math.Max(1, (int)Math.Round(ids.Count / Math.Max(1.0, culturesPerHeritage)));
        bool byGround = Merge(ids, Find, Union, contact, race, held, target);

        // --- The head of each family ----------------------------------------------------------------
        //
        // The largest member, not the union-find root, which is only an implementation detail. The
        // family takes this culture's name and its corpus, so it should be the one most of the
        // family's ground actually belongs to.
        var head = new Dictionary<int, int>();
        foreach (int id in ids)
        {
            int root = Find(id);
            if (!head.TryGetValue(root, out int best)
                || Bigger(id, best, held)) head[root] = id;
        }

        var rootOf = ids.ToDictionary(i => i, i => head[Find(i)]);

        string basis = (byBase, byGround) switch
        {
            (true, true) => "shared name bases and geography",
            (true, false) => "shared name bases",
            (false, true) => "geography",
            _ => "nothing the export grouped them by",
        };

        return new Grouping(rootOf, basis);
    }

    /// <summary>
    /// What kind of people this culture is, for the gate alone — an archetype name where the tag maps
    /// onto our roster, the raw tag where it does not, null only where there is no tag at all.
    ///
    /// The middle case is the one that matters. "Arago (Arachnid)" has no archetype, and reading that
    /// as "no race stated" let the geographic pass put it in the elves' family on the Fleunland
    /// export, purely because its ground is next to theirs. The export plainly said otherwise. Going
    /// through the archetype where there is one still normalises the synonyms an author might use —
    /// "(Elfish)" and "(Elven)" are one people, not two.
    /// </summary>
    private static string? Race(string name)
    {
        if (AzgaarNaming.ParseRace(name) is { } archetype) return archetype.ToString();
        return AzgaarNaming.Tag(name) is { Length: > 0 } tag ? tag : null;
    }

    /// <summary>True when <paramref name="a"/> should outrank <paramref name="b"/> as family head.</summary>
    private static bool Bigger(int a, int b, IReadOnlyDictionary<int, List<Title>> held)
    {
        int ca = held.TryGetValue(a, out var la) ? la.Count : 0;
        int cb = held.TryGetValue(b, out var lb) ? lb.Count : 0;
        return ca != cb ? ca > cb : a < b;
    }

    private static int Count(List<int> ids, Func<int, int> find)
        => ids.Select(find).Distinct().Count();

    /// <summary>
    /// How much border every pair of cultures shares, counted in touching county pairs.
    ///
    /// A count of adjacencies rather than a measured border length, because the county graph is what
    /// the culture partition is already built on and a pixel-accurate border would be a more precise
    /// answer to a question whose whole purpose is to rank neighbours against each other.
    /// </summary>
    private static Dictionary<(int, int), int> Contact(
        List<int> ids,
        IReadOnlyDictionary<int, List<Title>> held,
        RegionGrowth.Graph graph,
        IReadOnlyDictionary<Title, int> countyIndex)
    {
        var owner = new int[graph.Count];
        foreach (int id in ids)
            foreach (var county in held[id])
                if (countyIndex.TryGetValue(county, out int at)) owner[at] = id;

        var contact = new Dictionary<(int, int), int>();

        for (int at = 0; at < graph.Count; at++)
        {
            int a = owner[at];
            if (a == 0) continue;

            foreach (int near in graph.Neighbours[at])
            {
                int b = owner[near];
                if (b == 0 || b == a || b < a) continue;   // each pair counted once, from the lower id
                var pair = (a, b);
                contact[pair] = contact.GetValueOrDefault(pair) + 1;
            }
        }

        return contact;
    }

    /// <summary>
    /// Folds the smallest families into their largest neighbour until the target count is reached,
    /// or until nothing is left that may legally merge.
    ///
    /// Smallest-first rather than closest-pair-first because the thing being fixed is the lone
    /// culture with no relatives, not the two big families that happen to share a long border.
    /// Every tie is broken on culture id so the result does not depend on dictionary order.
    /// </summary>
    private static bool Merge(
        List<int> ids,
        Func<int, int> find,
        Action<int, int> union,
        Dictionary<(int, int), int> contact,
        Dictionary<int, string?> race,
        IReadOnlyDictionary<int, List<Title>> held,
        int target)
    {
        bool merged = false;

        while (true)
        {
            var families = ids.GroupBy(find)
                              .ToDictionary(g => g.Key, g => g.OrderBy(i => i).ToList());
            if (families.Count <= target) break;

            // Border weight between families, and the races each of them carries.
            var weight = new Dictionary<(int, int), int>();
            foreach (var ((a, b), touching) in contact)
            {
                int ra = find(a), rb = find(b);
                if (ra == rb) continue;
                var pair = ra < rb ? (ra, rb) : (rb, ra);
                weight[pair] = weight.GetValueOrDefault(pair) + touching;
            }

            var races = families.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.Select(i => race[i]).FirstOrDefault(r => r is not null));

            // Smallest family first: fewest cultures, then fewest counties, then lowest id.
            var order = families.Keys
                .OrderBy(f => families[f].Count)
                .ThenBy(f => families[f].Sum(i => held.TryGetValue(i, out var c) ? c.Count : 0))
                .ThenBy(f => f)
                .ToList();

            (int From, int Into)? move = null;

            foreach (int family in order)
            {
                int bestInto = 0, bestWeight = 0;

                foreach (var ((a, b), w) in weight.OrderBy(kv => kv.Key.Item1).ThenBy(kv => kv.Key.Item2))
                {
                    int other = a == family ? b : b == family ? a : 0;
                    if (other == 0) continue;

                    // The race gate: an untagged family joins anything, a tagged one only its own.
                    if (races[family] is { } mine && races[other] is { } theirs && mine != theirs)
                        continue;

                    if (w <= bestWeight) continue;
                    bestWeight = w;
                    bestInto = other;
                }

                if (bestInto == 0) continue;
                move = (family, bestInto);
                break;
            }

            if (move is not { } chosen) break;

            union(chosen.From, chosen.Into);
            merged = true;
        }

        return merged;
    }

    /// <summary>
    /// The culture at the head of <paramref name="id"/>'s line of descent — the first ancestor that
    /// is itself descended from Wildlands, or <paramref name="id"/> when it already is.
    ///
    /// Azgaar writes <c>origins</c> as a list because a culture can be a blend; the first entry is
    /// the one its own generator treats as the parent, so that is the one followed. Guarded against a
    /// cycle, which the map editor makes it perfectly possible to draw by hand.
    /// </summary>
    private static int Ancestor(int id, IReadOnlyDictionary<int, AzgaarCulture> live)
    {
        var seen = new HashSet<int>();

        while (seen.Add(id) && live.TryGetValue(id, out var culture))
        {
            int? parent = culture.Origins.FirstOrDefault(o => o is > 0 && o != id);
            if (parent is not { } up || !live.ContainsKey(up)) break;
            id = up;
        }

        return id;
    }
}
