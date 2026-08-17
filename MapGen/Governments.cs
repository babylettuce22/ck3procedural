using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.Emit;

namespace Ck3MapGen.MapGen;

public sealed class GovernmentMap
{
    public const string Feudal = "feudal_government";
    public const string Tribal = "tribal_government";
    public const string Clan = "clan_government";
    public const string Republic = "republic_government";
    public const string Theocracy = "theocracy_government";
    public const string Administrative = "administrative_government";
    public const string Nomad = "nomad_government";

    private readonly Dictionary<Title, string> byCounty;
    private readonly HashSet<Title> _adminRealms;
    private readonly HashSet<Title> _nomadRealms;

    internal GovernmentMap(
        Dictionary<Title, string> byCounty,
        HashSet<Title>? adminRealms = null,
        HashSet<Title>? nomadRealms = null)
    {
        this.byCounty = byCounty;
        _adminRealms = adminRealms ?? [];
        _nomadRealms = nomadRealms ?? [];
    }

    public string For(Title county) => byCounty.GetValueOrDefault(county, Feudal);

    public bool IsTribal(Title county) => For(county) == Tribal;
    public bool IsAdministrative(Title county) => For(county) == Administrative;
    public bool IsNomad(Title county) => For(county) == Nomad;

    public bool IsAdminEmpire(Title title) => _adminRealms.Contains(title);
    public bool IsNomadRealm(Title title) => _nomadRealms.Contains(title);

    public IEnumerable<Title> AdminTitles => _adminRealms;
    public IEnumerable<Title> NomadTitles => _nomadRealms;

    public static string SafeFallback(string government, bool isClanEligible = false) => government switch
    {
        Administrative => isClanEligible ? Clan : Feudal,
        Nomad => isClanEligible ? Clan : Tribal,
        _ => government
    };

    public static string CapitalHolding(string government) => government switch
    {
        Tribal or Nomad => "tribal_holding",
        Republic => "city_holding",
        Theocracy => "church_holding",
        _ => "castle_holding",
    };

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

    public static GovernmentMap Build(
        List<Title> empires,
        List<Title> counties,
        RealmMap realms,
        TerrainClass[] provinceTerrain,
        Dictionary<Title, int> development,
        CultureMap? cultures,
        WorldCenterMap? worldCenters,
        MapConfig cfg,
        Rng rng)
    {
        var assigned = new Dictionary<Title, string>();
        var adminTitles = new HashSet<Title>();
        var nomadTitles = new HashSet<Title>();

        int salt = rng.Int(1, int.MaxValue - 1);

        // --- 1. Identify Clan-leaning heritages ---
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

        // --- 2. Group counties by their independent top liege realm ---
        var topLiegeCounties = new Dictionary<Title, List<Title>>();
        foreach (var county in counties)
        {
            var topLiege = TopLiege(county, realms);
            if (!topLiegeCounties.TryGetValue(topLiege, out var list))
                topLiegeCounties[topLiege] = list = [];
            list.Add(county);
        }

        // --- 3. Pre-score and identify Administrative Empires (STRICT QUALITY CRITERIA) ---
        var eligibleAdminRealms = new HashSet<Title>();
        if (cfg.EnableAdministrativeEmpires && !cfg.ShatteredWorld && cfg.AdministrativeEmpireShare > 0)
        {
            var scoredEmpires = new List<(Title TopLiege, double Score)>();

            foreach (var (topLiege, realmCounties) in topLiegeCounties)
            {
                var primary = HistoryWriter.Primary(topLiege, realms);
                if (primary.Tier != "e") continue;

                bool hasImperialWonder = worldCenters is not null && realmCounties.Any(c =>
                {
                    var center = worldCenters.Centers.FirstOrDefault(wc => wc.County == c);
                    return center != null && center.Wonder.Archetype is WonderArchetype.ImperialPalace or WonderArchetype.GreatLibrary;
                });

                // Temporal & Quality Gate:
                // Must be in 800+ AD (or hold an ancient wonder) AND have high development (avgDev >= 11) or wonder
                double avgDev = realmCounties.Average(c => (double)development.GetValueOrDefault(c));
                if (cfg.StartYear < cfg.AdministrativeMinStartYear && !hasImperialWonder) continue;
                if (avgDev < 11.0 && !hasImperialWonder) continue;

                var capitalCulture = cultures?.For(topLiege);
                double score = avgDev;

                if (capitalCulture is not null)
                {
                    if (capitalCulture.Ethos is "ethos_bureaucratic" or "ethos_courtly") score += 6.0;
                    if (capitalCulture.Traditions.Contains("tradition_city_keepers")) score += 3.0;
                }

                if (hasImperialWonder) score += 10.0;
                scoredEmpires.Add((topLiege, score));
            }

            scoredEmpires.Sort((a, b) => b.Score.CompareTo(a.Score));
            int targetAdmin = (int)Math.Round(scoredEmpires.Count * cfg.AdministrativeEmpireShare);
            if (targetAdmin < 1 && scoredEmpires.Count > 0 && cfg.AdministrativeEmpireShare > 0)
            {
                targetAdmin = 1;
            }

            for (int i = 0; i < Math.Min(targetAdmin, scoredEmpires.Count); i++)
            {
                eligibleAdminRealms.Add(scoredEmpires[i].TopLiege);
                adminTitles.Add(HistoryWriter.Primary(scoredEmpires[i].TopLiege, realms));
            }
        }

        // --- 4. Assign Government Realm-by-Realm with Historical Calibration ---
        foreach (var (topLiege, realmCounties) in topLiegeCounties)
        {
            var draw = new Rng(topLiege.Index ^ salt);
            var capitalDomTerrain = Development.DominantTerrain(topLiege, provinceTerrain);
            var capitalCulture = cultures?.For(topLiege);
            double avgDev = realmCounties.Average(c => (double)development.GetValueOrDefault(c));
            double avgAridity = realmCounties.Average(c => Aridity(c, provinceTerrain));

            // Steppe & Arid composition
            int steppeCount = realmCounties.Count(c => Development.DominantTerrain(c, provinceTerrain) == TerrainClass.Steppe);
            int aridCount = realmCounties.Count(c => Development.DominantTerrain(c, provinceTerrain) is TerrainClass.Desert or TerrainClass.Drylands);
            double steppeShare = realmCounties.Count > 0 ? (double)steppeCount / realmCounties.Count : 0.0;
            double aridShare = realmCounties.Count > 0 ? (double)(steppeCount + aridCount) / realmCounties.Count : 0.0;

            // Earlier starts lean harder nomadic: +0.25 at 500, +0.05 at 900, -0.25 at 1250 and
            // later. The floor used to be 0.50, which meant NomadSteppeShare could not express
            // anything below "half of every qualifying realm" — the clamp, not the knob, was
            // setting the outcome. It is low enough now that the setting is honest across its
            // whole range.
            double timeNomadFactor = Math.Clamp((1000 - cfg.StartYear) / 2000.0, -0.25, 0.25);
            double effectiveNomadShare = Math.Clamp(cfg.NomadSteppeShare + timeNomadFactor, 0.0, 0.98);

            string realmGovernment;

            // A. Administrative Empire
            if (eligibleAdminRealms.Contains(topLiege))
            {
                realmGovernment = GovernmentMap.Administrative;
            }
            // B. Nomadic Horde.
            //
            // Steppe is the heartland and qualifies on a modest share. Merely dry ground has to be
            // *mostly* dry: aridShare counts Desert and Drylands as well, so at the old 0.35 nearly
            // every realm on an arid map cleared it and the other two clauses never mattered. The
            // bare "capital is Drylands" clause is gone with it — it carried no share requirement at
            // all, so one dry capital county made a whole settled realm roll for horde.
            else if (cfg.EnableNomadHordes
                     && (steppeShare >= 0.20 || aridShare >= 0.60 || capitalDomTerrain == TerrainClass.Steppe)
                     && draw.Chance(effectiveNomadShare))
            {
                realmGovernment = GovernmentMap.Nomad;
                nomadTitles.Add(HistoryWriter.Primary(topLiege, realms));
            }
            // C. Clan Realm (Arid heritages or dry terrain with decent settlement)
            else if ((capitalCulture is not null && clanHeritage.Contains(capitalCulture.Heritage)) || avgAridity >= ClanAridity)
            {
                realmGovernment = GovernmentMap.Clan;
            }
            // D. Tribal Realm vs Feudal Realm (Historical Calibration)
            //
            // Early and undeveloped is tribal unless the capital sits on fertile ground. Note the
            // escape hatch is narrower than it reads: cultivation makes Farmlands about 2% of
            // provinces by design and nothing assigns Floodplains at all, so at any start before 950
            // this clause — not the avgDev < 7 one below it — is what decides tribal-versus-feudal
            // for nearly every realm the nomad and clan clauses did not already take.
            else if (cfg.StartYear < 950 && avgDev < 12.0
                     && capitalDomTerrain is not (TerrainClass.Farmlands or TerrainClass.Floodplains))
            {
                realmGovernment = GovernmentMap.Tribal;
            }
            else if (capitalDomTerrain is TerrainClass.Taiga or TerrainClass.Arctic or TerrainClass.Jungle or TerrainClass.Wetlands && cfg.StartYear < 1100)
            {
                realmGovernment = GovernmentMap.Tribal;
            }
            else if (avgDev < 7.0)
            {
                realmGovernment = GovernmentMap.Tribal;
            }
            // E. Feudal Realm (Settled core, fertile farmlands, or High/Late Medieval start)
            else
            {
                realmGovernment = GovernmentMap.Feudal;
            }

            // Assign the unified government to all constituent counties
            foreach (var county in realmCounties)
            {
                var countyDraw = new Rng(county.Index ^ salt);
                var countyDomTerrain = Development.DominantTerrain(county, provinceTerrain);
                int cLevel = development.GetValueOrDefault(county);

                // 1. If realm is Nomadic or Administrative, all counties stay unified
                if (realmGovernment is GovernmentMap.Nomad or GovernmentMap.Administrative)
                {
                    assigned[county] = realmGovernment;
                }
                // 2. Peripheral Steppe marches in feudal/clan/tribal realms become Nomadic hordes
                else if (cfg.EnableNomadHordes && countyDomTerrain == TerrainClass.Steppe && countyDraw.Chance(effectiveNomadShare))
                {
                    assigned[county] = GovernmentMap.Nomad;
                    nomadTitles.Add(county);
                }
                // 3. Merchant Republic ports
                else if (realmGovernment is GovernmentMap.Feudal or GovernmentMap.Clan && IsRepublic(county, cLevel, countyDraw))
                {
                    assigned[county] = GovernmentMap.Republic;
                }
                // 4. Default to sovereign's government
                else
                {
                    assigned[county] = realmGovernment;
                }
            }
        }

        return new GovernmentMap(assigned, adminTitles, nomadTitles);

        bool IsRepublic(Title county, int level, Rng draw)
            => cfg.RepublicShare > 0
               && IsCoastal(county, provinceTerrain)
               && level >= 10
               && draw.NextDouble() < cfg.RepublicShare;
    }

    private static Title TopLiege(Title county, RealmMap realms)
    {
        var primary = HistoryWriter.Primary(county, realms);
        var current = primary;

        while (realms.Liege.TryGetValue(current, out var liege))
        {
            current = liege;
        }

        return realms.HolderCounty.TryGetValue(current, out var topHolder) ? topHolder : county;
    }
}