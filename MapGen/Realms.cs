using Ck3MapGen.Config;
using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen;

/// <summary>
/// Who actually holds what at the start date, and who owes whom.
/// </summary>
public sealed class RealmMap
{
    /// <summary>
    /// Every held title, mapped to the county whose ruler holds it. Counties map to themselves.
    ///
    /// Keyed this way because there is exactly one generated character per county, so a county is
    /// the identity of its ruler — two titles pointing at the same county are two titles on one
    /// head, which is how a duke also holds his capital and an emperor also holds his duchy.
    /// </summary>
    public required Dictionary<Title, Title> HolderCounty { get; init; }

    /// <summary>
    /// Liege per title, set only on each ruler's highest title.
    ///
    /// A character has one liege in CK3, so writing <c>liege</c> on every title they hold would
    /// state the same fact several times and contradict itself as soon as two of those titles sit
    /// under different lords. Vanilla sets it on the primary title and so do we.
    /// </summary>
    public required Dictionary<Title, Title> Liege { get; init; }

    /// <summary>Counties whose rulers are the greatest in the world, strongest first. The bookmark
    /// and the challenge character come off the front of this.</summary>
    public required List<Title> Greatest { get; init; }
}

/// <summary>
/// Decides which of the de jure titles are actually worn by somebody in 867.
///
/// The de jure hierarchy exists from the moment <see cref="Titles"/> draws it, but that is a map of
/// claims rather than of power: leaving every duchy, kingdom and empire vacant gives a world of
/// several hundred equal, independent counts, which is not a start date any Crusader Kings game has
/// ever shipped and reads as an unfinished map. Handing them all out is worse — a world with no
/// independent counts has nowhere for the game's own ambition mechanics to point.
///
/// So a share of each tier is realised, and the chain below a realised title is realised with it:
/// an emperor is also a king, a duke and a count, holding his capital all the way down. That is
/// both what CK3 expects and what stops an emperor owning a continent while personally holding one
/// county in the corner of it.
/// </summary>
public static class Realms
{
    /// <param name="wilderness">
    /// Counties nobody lives in. They are held by the wilderness dummy rather than by a generated
    /// ruler, so they must not appear here at all: not as their own holder, not as the seat of a
    /// duchy, and not as anybody's liege. Pass <see cref="WildernessMap.Empty"/> when the feature
    /// is off.
    /// </param>
    public static RealmMap Build(List<Title> empires, Dictionary<Title, int> development,
        WildernessMap wilderness, MapConfig cfg, Rng rng)
    {
        var all = Titles.Flatten(empires).ToList();
        var weight = Weigh(empires, development, wilderness);

        // Which titles somebody wears. Iterated in the fixed order Flatten produces so the same
        // seed always promotes the same titles.
        var realized = new HashSet<Title>();
        foreach (var title in all)
        {
            double share = title.Tier switch
            {
                "e" => cfg.EmpireTitleShare,
                "k" => cfg.KingdomTitleShare,
                "d" => cfg.DuchyTitleShare,
                _ => 0,
            };

            // Weight 0 means every county beneath it is wilderness — a duchy of empty mountains.
            // Realising it would seat a duke on land nobody lives on, so it stays unheld and
            // becomes something a coloniser can eventually claim by filling the counties in.
            if (share > 0 && weight.GetValueOrDefault(title) > 0 && rng.Chance(share))
                Realize(title, realized, weight);
        }

        // A title's holder is the ruler of the richest county under it, found by following the
        // strongest child down. Two titles that pick the same county are two titles on one ruler.
        //
        // Wilderness counties are skipped rather than given a holder: the dummy holds them, and
        // HistoryWriter writes that separately. A county missing from here produces no generated
        // character, no dynasty and no title history — which is exactly right for empty ground.
        var holderCounty = new Dictionary<Title, Title>();
        foreach (var county in all.Where(t => t.Tier == "c" && !wilderness.Contains(t)))
            holderCounty[county] = county;
        foreach (var title in realized) holderCounty[title] = Capital(title, weight);

        // The highest title each ruler wears, which is where their liege is recorded.
        var primary = new Dictionary<Title, Title>();
        foreach (var (title, county) in holderCounty)
            if (!primary.TryGetValue(county, out var current) || Rank(title) > Rank(current))
                primary[county] = title;

        var liege = new Dictionary<Title, Title>();
        foreach (var (county, top) in primary)
        {
            // The nearest held title above, skipping any held by this same ruler — those are their
            // own titles, not their lord's.
            for (var above = top.Parent; above is not null; above = above.Parent)
            {
                if (!holderCounty.TryGetValue(above, out var lord) || lord == county) continue;
                liege[top] = above;
                break;
            }
        }

        var greatest = primary
            .OrderByDescending(kv => Rank(kv.Value))
            .ThenByDescending(kv => weight[kv.Value])
            .ThenBy(kv => kv.Key.Index)
            .Select(kv => kv.Key)
            .ToList();

        Report(realized, primary, liege, all);

        return new RealmMap
        {
            HolderCounty = holderCounty,
            Liege = liege,
            Greatest = greatest,
        };
    }

    /// <summary>
    /// Marks a title held, and with it the strongest title beneath it, all the way to a county.
    ///
    /// Stopping at the first title already realised is what keeps this cheap and also correct: if
    /// the chain below was realised by an earlier call it is the same chain, because the strongest
    /// child does not depend on who asked.
    /// </summary>
    private static void Realize(Title title, HashSet<Title> realized, Dictionary<Title, int> weight)
    {
        while (title.Tier != "c")
        {
            if (!realized.Add(title)) return;

            var next = Strongest(title, weight);
            if (next is null) return;
            title = next;
        }
    }

    /// <summary>The county a title is ruled from: the richest one under it.</summary>
    private static Title Capital(Title title, Dictionary<Title, int> weight)
    {
        while (title.Tier != "c")
        {
            var next = Strongest(title, weight);
            if (next is null) break;
            title = next;
        }

        return title;
    }

    private static Title? Strongest(Title title, Dictionary<Title, int> weight)
        => title.Children.Count == 0
            ? null
            : title.Children.OrderByDescending(c => weight.GetValueOrDefault(c))
                            .ThenBy(c => c.Index).First();

    /// <summary>
    /// How much a title is worth: the development of every county beneath it.
    ///
    /// Counted with a floor of one per county so that a large poor duchy still outweighs a tiny
    /// one — otherwise a map whose development came out flat would pick capitals arbitrarily.
    /// </summary>
    /// <remarks>
    /// A wilderness county weighs nothing, and that one value carries the whole exclusion: it keeps
    /// <see cref="Strongest"/> from ever descending into empty ground while a settled sibling
    /// exists, and it makes a title's weight zero exactly when everything under it is wilderness,
    /// which is the test <see cref="Build"/> uses to leave such titles unheld. A *settled* county
    /// with no development still weighs 1, so the two cases stay distinguishable.
    /// </remarks>
    private static Dictionary<Title, int> Weigh(List<Title> empires,
        Dictionary<Title, int> development, WildernessMap wilderness)
    {
        var weight = new Dictionary<Title, int>();

        foreach (var root in empires) Visit(root);
        return weight;

        int Visit(Title title)
        {
            int total = title.Tier == "c" && !wilderness.Contains(title)
                ? development.GetValueOrDefault(title) + 1
                : 0;
            foreach (var child in title.Children) total += Visit(child);

            weight[title] = total;
            return total;
        }
    }

    private static int Rank(Title title) => title.Tier switch
    {
        "e" => 4,
        "k" => 3,
        "d" => 2,
        "c" => 1,
        _ => 0,
    };

    private static void Report(HashSet<Title> realized, Dictionary<Title, Title> primary,
        Dictionary<Title, Title> liege, List<Title> all)
    {
        int emperors = realized.Count(t => t.Tier == "e");
        int kings = realized.Count(t => t.Tier == "k");
        int dukes = realized.Count(t => t.Tier == "d");

        // A ruler with no liege answers to nobody, which is the number that decides whether the
        // map plays as a patchwork or as a handful of great powers.
        int independent = primary.Values.Count(t => !liege.ContainsKey(t));
        int counties = all.Count(t => t.Tier == "c");

        Console.WriteLine($"  realms: {emperors} empires, {kings} kingdoms and {dukes} duchies held " +
                          $"over {counties} counties — {independent} independent rulers, " +
                          $"{primary.Count - independent} vassals");
    }
}
