using Ck3MapGen.Config;
using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen;

/// <summary>
/// Which government each county's ruler holds at the start date.
///
/// Head of faith titles are landless spiritual titles created directly in history with
/// <c>theocracy_government</c> and seated on their faith's primary holy site, so no landed county
/// starts as a theocracy.
///
/// The four settled governments a generated world can actually support are covered here. Left out
/// on purpose:
///
///   * <c>administrative_government</c> — its rules carry <c>administrative = yes</c>, which needs
///     the <c>admin_gov</c> DLC flag (Roads to Power). History files cannot test for DLC, so
///     writing it would hand anyone without that DLC a realm whose mechanics silently do not exist.
///   * <c>wanua_government</c>, and the nomad and herder families — same problem, gated behind
///     <c>has_tgp_dlc_trigger</c> and Wandering Nobles respectively.
///   * <c>mercenary_government</c> and <c>holy_order_government</c> — landless by design and
///     carrying <c>cannot_be_vassal_or_liege</c>; they belong to title-less companies, not to the
///     de jure counties this generator hands out.
///
/// The choice has to be made *once* and shared, because three separate things have to agree about
/// it: the capital barony's holding, the government written into title history, and the GUI's
/// preview. CK3 does not tolerate the first two disagreeing — a government lists exactly one
/// <c>primary_holding</c>, so a republic on a castle or a feudal count on a tribal capital is a
/// ruler who cannot hold his own seat.
/// </summary>
public sealed class GovernmentMap
{
    public const string Feudal = "feudal_government";
    public const string Tribal = "tribal_government";
    public const string Clan = "clan_government";
    public const string Republic = "republic_government";
    public const string Theocracy = "theocracy_government";

    private readonly Dictionary<Title, string> byCounty;

    internal GovernmentMap(Dictionary<Title, string> byCounty) => this.byCounty = byCounty;

    /// <summary>
    /// The government a county's ruler holds. Feudal for anything unrecorded, which is also CK3's
    /// own answer — <c>feudal_government</c> carries <c>fallback = 1</c>.
    /// </summary>
    public string For(Title county) => byCounty.GetValueOrDefault(county, Feudal);

    public bool IsTribal(Title county) => For(county) == Tribal;

    /// <summary>
    /// The holding a county's capital barony must carry, which is entirely decided by the
    /// government: each one names a single <c>primary_holding</c> and a ruler seated on anything
    /// else cannot hold his own capital.
    /// </summary>
    public static string CapitalHolding(string government) => government switch
    {
        Tribal => "tribal_holding",
        Republic => "city_holding",
        Theocracy => "church_holding",
        _ => "castle_holding",
    };

    /// <summary>Counts by government, commonest first — for the run log.</summary>
    public IEnumerable<(string Government, int Count)> Tally(int total)
    {
        var counts = new Dictionary<string, int> { [Feudal] = total };
        foreach (var government in byCounty.Values)
        {
            counts[government] = counts.GetValueOrDefault(government) + 1;
            counts[Feudal]--;
        }

        return counts.Where(kv => kv.Value > 0)
                     .OrderByDescending(kv => kv.Value)
                     .Select(kv => (kv.Key, kv.Value));
    }
}

public static class Governments
{
    private static int TribalThreshold(MapConfig cfg)
        => Math.Clamp(5 - ((cfg.StartYear - 867) / 50), 1, 20);

    private const double ClanAridity = 0.40;

    private static bool IsArid(TerrainClass t) => t is TerrainClass.Desert or TerrainClass.Drylands
        or TerrainClass.Steppe or TerrainClass.DesertMountains;

    private static double Aridity(Title county, TerrainClass[] provinceTerrain)
    {
        int arid = 0, total = 0;
        foreach (var barony in county.Children)
        {
            if (barony.ProvinceId < 0 || barony.ProvinceId >= provinceTerrain.Length) continue;
            total++;
            if (IsArid(provinceTerrain[barony.ProvinceId])) arid++;
        }

        return total == 0 ? 0 : arid / (double)total;
    }

    private static bool IsCoastal(Title county, TerrainClass[] provinceTerrain)
    {
        foreach (var barony in county.Children)
            if (barony.ProvinceId >= 0 && barony.ProvinceId < provinceTerrain.Length
                && provinceTerrain[barony.ProvinceId] == TerrainClass.Beach)
                return true;

        return false;
    }

    public static GovernmentMap Build(List<Title> counties, TerrainClass[] provinceTerrain,
        Dictionary<Title, int> development, CultureMap? cultures, FaithMap? faiths, MapConfig cfg,
        Rng rng)
    {
        int threshold = TribalThreshold(cfg);
        var assigned = new Dictionary<Title, string>();

        int salt = rng.Int(1, int.MaxValue - 1);

        var clanHeritage = new HashSet<Heritage>();
        if (cultures is not null)
        {
            var aridity = new Dictionary<Heritage, (double Sum, int Count)>();
            foreach (var county in counties)
            {
                var heritage = cultures.For(county).Heritage;
                var (sum, count) = aridity.GetValueOrDefault(heritage);
                aridity[heritage] = (sum + Aridity(county, provinceTerrain), count + 1);
            }

            foreach (var (heritage, (sum, count)) in aridity)
                if (count > 0 && sum / count >= ClanAridity) clanHeritage.Add(heritage);
        }

        foreach (var county in counties)
        {
            int level = development.GetValueOrDefault(county);
            var dominant = Development.DominantTerrain(county, provinceTerrain);
            var culture = cultures?.For(county);

            bool isTribal = level < threshold
                || (dominant is TerrainClass.Steppe or TerrainClass.Arctic or TerrainClass.Taiga
                    && cfg.StartYear < 1100)
                || (culture is not null && cfg.StartYear < 1000
                    && culture.Traditions.Contains("tradition_pastoralists"));

            if (isTribal)
            {
                assigned[county] = GovernmentMap.Tribal;
                continue;
            }

            var draw = new Rng(county.Index ^ salt);

            if (IsRepublic(county, level, draw)) { assigned[county] = GovernmentMap.Republic; continue; }

            bool isClan = culture is not null
                ? clanHeritage.Contains(culture.Heritage)
                : Aridity(county, provinceTerrain) >= ClanAridity;

            if (isClan) assigned[county] = GovernmentMap.Clan;
        }

        return new GovernmentMap(assigned);

        bool IsRepublic(Title county, int level, Rng draw)
            => cfg.RepublicShare > 0
               && IsCoastal(county, provinceTerrain)
               && level >= threshold + 6
               && draw.NextDouble() < cfg.RepublicShare;
    }
}