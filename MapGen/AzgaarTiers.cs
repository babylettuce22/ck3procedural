using Ck3MapGen.Config;
using Ck3MapGen.Io;

namespace Ck3MapGen.MapGen;

/// <summary>
/// Works out what rank each Azgaar state should hold in our title hierarchy, and whether this map is
/// detailed enough to give it to them.
///
/// The problem this exists for: a tier costs baronies. An empire needs kingdoms, which need duchies,
/// which need counties, and a state with nine baronies in it cannot be an empire however much
/// Azgaar wants it to be. Azgaar knows nothing about that, so some exports ask for a hierarchy the
/// map has no room for, and the failure mode worth avoiding is silently ignoring the export and
/// clustering geometrically instead.
///
/// What the cost actually is matters more than it sounds. The floors are
/// <see cref="MapConfig.MinChildrenPerTitle"/>, not the per-tier bands on <see cref="Titles"/> —
/// those bands are growth targets, as the config says where the floor is defined. Pricing a tier at
/// its target rather than its floor overstates the cost by more than tenfold, and the first real
/// export this was run against was told it needed a 12,500-pixel heightmap when the 9,216-pixel map
/// it was already on had room for every state at the rank Azgaar asked for.
///
/// The budget is not something to fix by overriding the map's resolution either. Barony *area* is
/// fixed (<see cref="MapConfig.BaronyPixels"/>) and the count falls out of the land area, so how far
/// the source heightmap was upscaled already decides the budget — the user's decision, made before
/// the tool ran, and the tool's job is to report the consequence rather than overrule it. Where the
/// budget really is short, each state is granted the highest rank its own land supports, so the cost
/// falls on rank rather than on Azgaar's borders.
/// </summary>
public static class AzgaarTiers
{
    public const int County = 1;
    public const int Duchy = 2;
    public const int Kingdom = 3;
    public const int Empire = 4;

    /// <summary>
    /// The fewest baronies a title of each tier can actually be built from.
    ///
    /// Built from <see cref="MapConfig.MinChildrenPerTitle"/>, not from the per-tier bands on
    /// <see cref="Titles"/>. Those bands are growth *targets* — the config says so where the floor
    /// is defined — and taking them for requirements is wrong by more than an order of magnitude: it
    /// prices an empire at a hundred and eighty baronies when CK3 is perfectly happy with an empire
    /// of one kingdom of one duchy, and vanilla ships several near that size.
    ///
    /// Getting this wrong is not a harmless overestimate. It told a real 20-state export that it
    /// needed a 12,500-pixel heightmap to be expressible, when the map it was already on had room
    /// for every state at the rank Azgaar asked for.
    /// </summary>
    public static int Minimum(int tier, int minChildren) => tier switch
    {
        County => 1,
        Duchy => minChildren,
        Kingdom => minChildren * Minimum(Duchy, minChildren),
        Empire => minChildren * Minimum(Kingdom, minChildren),
        _ => 0,
    };

    /// <summary>
    /// What a title of each tier *wants* — the same product taken through the middle of each of the
    /// growth bands, rather than through the floor.
    ///
    /// Reported as a quality note and nothing more. A hierarchy built near the floor is legal and
    /// reads as stunted, so it is worth knowing when a map is heading that way; it is not a reason
    /// to tell someone their map is too small, which is what conflating this with
    /// <see cref="Minimum"/> did.
    /// </summary>
    public static int Comfortable(int tier) => tier switch
    {
        County => Mid(Titles.MinBaroniesPerCounty, Titles.MaxBaroniesPerCounty),
        Duchy => Mid(Titles.MinCountiesPerDuchy, Titles.MaxCountiesPerDuchy) * Comfortable(County),
        Kingdom => Mid(Titles.MinDuchiesPerKingdom, Titles.MaxDuchiesPerKingdom) * Comfortable(Duchy),
        Empire => Mid(Titles.MinKingdomsPerEmpire, Titles.MaxKingdomsPerEmpire) * Comfortable(Kingdom),
        _ => 0,
    };

    private static int Mid(int lo, int hi) => (int)Math.Round((lo + hi) / 2.0);

    public static string Key(int tier) => tier switch
    {
        Empire => "e", Kingdom => "k", Duchy => "d", County => "c", _ => "",
    };

    public static string Word(int tier) => tier switch
    {
        Empire => "empire", Kingdom => "kingdom", Duchy => "duchy", County => "county", _ => "none",
    };

    /// <summary>Spelled out rather than suffixed, because "duchys" is not a word.</summary>
    public static string Plural(int tier) => tier switch
    {
        Empire => "empires", Kingdom => "kingdoms", Duchy => "duchies", County => "counties", _ => "none",
    };

    public static string Count(int n, int tier) => $"{n} {(n == 1 ? Word(tier) : Plural(tier))}";

    /// <summary>The highest tier <paramref name="baronies"/> baronies can be built into.</summary>
    public static int Supports(int baronies, int minChildren)
    {
        for (int tier = Empire; tier >= County; tier--)
            if (baronies >= Minimum(tier, minChildren)) return tier;
        return 0;
    }

    /// <summary>
    /// Azgaar's own rank for a state, 0 to 4, recomputed from area exactly as its states generator
    /// does before it picks a form name.
    ///
    /// <code>
    /// tier = min(floor(area / medianArea * 2.6), 4)
    /// if (tier == 4 &amp;&amp; area &lt; empireMin) tier = 3
    /// monarchy = ["Duchy", "Grand Duchy", "Principality", "Kingdom", "Empire"]
    /// </code>
    ///
    /// Recomputed rather than read back off <see cref="AzgaarState.FormName"/> because that string
    /// only carries the rank for monarchies. A theocracy, a republic or a union draws its form name
    /// from a different vocabulary — Theocracy, Tsardom, Protectorate, Dominion — and none of those
    /// words say where on the ladder the state sits, though the area they were chosen from does.
    /// </summary>
    public static Dictionary<int, int> Ranks(AzgaarWorld world)
    {
        var states = world.RealStates.ToList();
        var ranks = new Dictionary<int, int>(states.Count);
        if (states.Count == 0) return ranks;

        var extents = states.Select(Extent).OrderByDescending(a => a).ToList();

        double median = Median(extents);
        if (median <= 0) median = 1;

        // Azgaar's empire floor: the area of the nth largest state, where n grows very slowly with
        // the state count, so a map has a handful of empires whether it has ten states or a hundred.
        int index = Math.Max((int)Math.Ceiling(Math.Pow(states.Count, 0.4)) - 2, 0);
        double empireMin = extents[Math.Min(index, extents.Count - 1)];

        foreach (var state in states)
        {
            double extent = Extent(state);
            int rank = Math.Min((int)Math.Floor(extent / median * 2.6), 4);
            if (rank == 4 && extent < empireMin) rank = 3;
            ranks[state.I] = rank;
        }

        return ranks;
    }

    /// <summary>
    /// How big a state is, preferring the export's own area and falling back to its cell count.
    ///
    /// <see cref="AzgaarState.Area"/> is filled in by Azgaar's editors rather than by generation, so
    /// an export saved without opening them has it at zero. Cell count is the same measure at a
    /// coarser grain and is always present, and only the ratio between states matters here.
    /// </summary>
    private static double Extent(AzgaarState state) => state.Area > 0 ? state.Area : state.Cells;

    private static double Median(List<double> sorted)
        => sorted.Count == 0 ? 0
         : sorted.Count % 2 == 1 ? sorted[sorted.Count / 2]
         : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2.0;

    /// <summary>The rank Azgaar's 0-4 scale asks for, on our four-tier ladder.</summary>
    public static int DesiredTier(int azgaarRank) => Math.Clamp(Duchy + azgaarRank / 2, Duchy, Empire);
}

/// <summary>One Azgaar state, the rank it asked for, and the rank this map can afford it.</summary>
public sealed class StatePlan
{
    public required AzgaarState State { get; init; }

    /// <summary>Azgaar's own 0-4 rank, from <see cref="AzgaarTiers.Ranks"/>.</summary>
    public required int Rank { get; init; }

    /// <summary>The tier that rank asks for.</summary>
    public required int Desired { get; init; }

    /// <summary>The tier it gets, once its own barony count has had its say.</summary>
    public required int Granted { get; init; }

    /// <summary>Our land baronies whose majority Azgaar state is this one.</summary>
    public required int Baronies { get; init; }

    public bool Demoted => Granted < Desired;

    /// <summary>True when the state has no land here at all — usually one lost to the coastline.</summary>
    public bool Landless => Baronies == 0;
}

/// <summary>
/// What the whole export would cost to express, and what it was granted.
///
/// Produced before the title hierarchy is built and carried on the import so the hierarchy can read
/// it. In this tier nothing consumes it but the report — it exists first so that the tier that does
/// consume it has something already proven against real exports to build on.
/// </summary>
public sealed class HierarchyPlan
{
    public required IReadOnlyList<StatePlan> States { get; init; }

    /// <summary>Every land barony on the map.</summary>
    public required int Baronies { get; init; }

    /// <summary>Land baronies that fell in no state — Azgaar's own unclaimed ground.</summary>
    public required int Unclaimed { get; init; }

    /// <summary>The arity floor these grants were measured against.</summary>
    public required int MinChildren { get; init; }

    /// <summary>Baronies in each of the export's provinces, descending. Empty ones included.</summary>
    public required IReadOnlyList<int> ProvinceBaronies { get; init; }

    public int Demoted => States.Count(s => s.Demoted && !s.Landless);
    public int Landless => States.Count(s => s.Landless);

    /// <summary>What every state's asking rank costs at the arity floors.</summary>
    public int MinimumWanted => States.Sum(s => AzgaarTiers.Minimum(s.Desired, MinChildren));

    /// <summary>Provinces too thin to stand as their own duchy, and so bound to merge.</summary>
    public int ThinProvinces
        => ProvinceBaronies.Count(b => b > 0 && b < AzgaarTiers.Minimum(AzgaarTiers.Duchy, MinChildren));

    public int EmptyProvinces => ProvinceBaronies.Count(b => b == 0);

    /// <summary>The same, through the middle of each band — the number to size a heightmap by.</summary>
    public int ComfortableWanted => States.Sum(s => AzgaarTiers.Comfortable(s.Desired));

    public int Granted(int tier) => States.Count(s => s.Granted == tier);
    public int Wanted(int tier) => States.Count(s => s.Desired == tier);

    /// <summary>True when every state with land here got the rank Azgaar asked for.</summary>
    public bool Honoured => Demoted == 0;

    /// <summary>
    /// Prints the budget, what it bought, and — when it fell short — the map size that would not.
    ///
    /// Deliberately loud when it demotes. Silently handing back a flatter hierarchy than the export
    /// described is the failure this whole class exists to stop being invisible.
    /// </summary>
    public void Report(MapConfig cfg, double landShare)
    {
        Console.WriteLine($"  azgaar hierarchy: {States.Count} states over {Baronies} land baronies" +
                          (Unclaimed > 0 ? $" ({Unclaimed} on unclaimed ground)" : ""));

        Console.WriteLine($"    azgaar asks for {Describe(Wanted)}" +
                          (Honoured
                              ? " — all granted"
                              : $"; this map affords {Describe(Granted)} — {Demoted} demoted") +
                          (Landless > 0 ? $", {Landless} with no land here" : ""));

        if (ProvinceBaronies.Count > 0)
        {
            int median = ProvinceBaronies[ProvinceBaronies.Count / 2];
            int floor = AzgaarTiers.Minimum(AzgaarTiers.Duchy, MinChildren);
            Console.WriteLine($"    {ProvinceBaronies.Count} provinces: median {median} baronies, " +
                              $"{ThinProvinces} under the {floor} a duchy needs" +
                              (EmptyProvinces > 0 ? $", {EmptyProvinces} with none" : "") +
                              " — those merge into a neighbour");
        }

        // Only when a state genuinely could not be granted its rank is map size the problem. Saying
        // it any other time is what sent a perfectly adequate map off to be re-exported at 12,500px.
        if (!Honoured)
        {
            var short_ = States.Where(s => s.Demoted && !s.Landless)
                               .OrderByDescending(s => s.Baronies).FirstOrDefault();
            if (short_ is not null)
                Console.WriteLine(
                    $"    {short_.State.Name} holds {short_.Baronies} baronies; " +
                    $"{AzgaarTiers.Word(short_.Desired)} needs {AzgaarTiers.Minimum(short_.Desired, MinChildren)}");

            if (Suggestion(cfg, landShare) is { } size)
                Console.WriteLine($"    about {size.Width}x{size.Height} would grant every rank " +
                                  "at this land share and county scale");
        }
        else if (Baronies < ComfortableWanted)
        {
            // Legal, and worth knowing about: a hierarchy near its floor has every duchy at the
            // fewest counties allowed, which reads flatter than one the generator grew itself.
            Console.WriteLine($"    every rank granted, but below the growth targets " +
                              $"(~{ComfortableWanted} baronies would fill them; this map has {Baronies}) — " +
                              "expect a flatter hierarchy than a generated map");
        }

        // No trailing disclaimer: the hierarchy below is built from this plan, and the line that
        // used to say otherwise was true only while it was not.
    }

    private string Describe(Func<int, int> count)
    {
        var parts = new List<string>();
        for (int tier = AzgaarTiers.Empire; tier >= AzgaarTiers.County; tier--)
        {
            int n = count(tier);
            if (n > 0) parts.Add(AzgaarTiers.Count(n, tier));
        }
        return parts.Count == 0 ? "nothing" : string.Join(", ", parts);
    }

    /// <summary>
    /// The heightmap size that would buy <see cref="ComfortableWanted"/> baronies, at this map's
    /// land share and county scale.
    ///
    /// Invertible because barony area is fixed rather than barony count: the count is land pixels
    /// over <see cref="MapConfig.BaronyPixels"/>, so the pixels needed follow directly, and the
    /// heightmap is twice the province map on each axis. Returned at the current aspect ratio,
    /// rounded to even numbers because the loader rejects odd ones.
    /// </summary>
    private (int Width, int Height)? Suggestion(MapConfig cfg, double landShare)
    {
        if (landShare <= 0.01 || Baronies >= ComfortableWanted) return null;

        double landPixels = ComfortableWanted * cfg.BaronyPixels;
        double provincePixels = landPixels / landShare;

        double aspect = (double)cfg.Width / Math.Max(1, cfg.Height);
        double provinceHeight = Math.Sqrt(provincePixels / aspect);
        double provinceWidth = provinceHeight * aspect;

        int width = Even((int)Math.Round(provinceWidth * 2));
        int height = Even((int)Math.Round(provinceHeight * 2));

        return width <= cfg.Width && height <= cfg.Height ? null : (width, height);

        static int Even(int v) => v % 2 == 0 ? v : v + 1;
    }

    /// <summary>
    /// Grants every state the highest tier its own land supports, capped by what Azgaar asked for.
    ///
    /// Per state rather than as one uniform shift across the map. A uniform shift keeps the states
    /// ranked relative to each other, which sounds like the more faithful choice and is not: it
    /// demotes states that could have held their rank, so a map short by one empire also loses every
    /// duchy to county tier. Clamping each state against its own land leaves the ones that fit
    /// alone, and the shift falls out where it is actually needed.
    /// </summary>
    public static HierarchyPlan Build(AzgaarWorld world, IReadOnlyDictionary<int, int> baroniesByState,
                                      IReadOnlyDictionary<int, int> baroniesByProvince,
                                      int baronies, int unclaimed, int minChildren)
    {
        var ranks = AzgaarTiers.Ranks(world);
        var plans = new List<StatePlan>();

        foreach (var state in world.RealStates)
        {
            int rank = ranks.GetValueOrDefault(state.I);
            int desired = AzgaarTiers.DesiredTier(rank);
            int owned = baroniesByState.GetValueOrDefault(state.I);

            plans.Add(new StatePlan
            {
                State = state,
                Rank = rank,
                Desired = desired,
                Granted = Math.Min(desired, AzgaarTiers.Supports(owned, minChildren)),
                Baronies = owned,
            });
        }

        var provinces = world.RealProvinces
            .Select(p => baroniesByProvince.GetValueOrDefault(p.I))
            .OrderByDescending(b => b)
            .ToList();

        return new HierarchyPlan
        {
            States = plans.OrderByDescending(p => p.Baronies).ToList(),
            Baronies = baronies,
            Unclaimed = unclaimed,
            MinChildren = minChildren,
            ProvinceBaronies = provinces,
        };
    }
}
