using Ck3MapGen.Config;
using Ck3MapGen.Emit;

namespace Ck3MapGen.MapGen;

/// <summary>
/// Landed churchmen: counties inside a see's region held by a theocrat of the faith as a vassal of
/// the realm's top liege, as vanilla's prince-bishops of Mainz, Cologne, Salzburg and Liège are. CK3
/// 1.20 builds its clerical layer around them (an archbishop with landed rulers in his region is the
/// rite growth's preferred candidate, a landed theocrat can be granted an ecclesiastical title), and
/// a see with none is only a map colour.
///
/// How many: vanilla holds 25 in 867 over 849 counties of clerical regions, 48 in 1066 and 63 in 1178,
/// about 3 % of a see's counties rising to 6 %. <see cref="Share"/> follows that by how advanced the
/// date is, so an earlier bookmark has fewer.
///
/// Where: the see's seat first (vanilla's archbishop of Mainz holds both d_et_mainz and c_mainz, and
/// so does a generated one whose seat is carved, see HistoryWriter.WriteSees), then holy sites, world
/// centres and development, under a settled (feudal or clan) lord. Two ways in: a lone vassal count
/// (one county, nobody under him, sworn to a duke or better) becomes the churchman under the same
/// liege, which is how most of a settled realm's counties are held; or a county is taken out of a
/// lord's demesne of three or more and sworn to the realm's top liege. One per lord either way. A
/// county carved on one date is preferred on the others, so a bishopric outlasts its bishop.
///
/// The carve is made on the realm map and the government map before the rulers are drawn, so every
/// writer after it (rulers, houses, title history, holdings, bookmarks) sees an ordinary theocratic
/// count, the same one an Azgaar theocracy already gets. HistoryWriter writes such a theocrat's
/// government as ecclesiastical when its faith has an institutional clergy.
/// </summary>
public static class PrinceBishops
{
    /// <summary>The share of a see's counties a bishop holds on a date as advanced as <paramref name="eraYear"/>.</summary>
    public static double Share(int eraYear) => 0.03 + 0.03 * Math.Clamp((eraYear - 867) / 311.0, 0, 1);

    /// <summary>
    /// Carves this date's bishoprics into <paramref name="realms"/> and <paramref name="governments"/>.
    /// </summary>
    /// <param name="regionOf">Each see's counties on this date, or null where it does not stand.</param>
    /// <param name="previous">Counties carved on another date, which this one prefers; gains this date's.</param>
    /// <returns>The counties carved.</returns>
    public static List<Title> Carve(FaithMap faiths, RealmMap realms, GovernmentMap governments,
        WildernessMap wilderness, Dictionary<Title, int> development, WorldCenterMap? worldCenters,
        int eraYear, Func<See, List<Title>?> regionOf, HashSet<Title> previous)
    {
        var carved = new List<Title>();
        var seats = realms.HolderCounty.Values.ToHashSet();
        var demesne = realms.HolderCounty.Where(kv => kv.Key.Tier == "c")
            .GroupBy(kv => kv.Value).ToDictionary(g => g.Key, g => g.Count());
        var lordsUsed = new HashSet<Title>();

        // Seats someone answers to, who are lords rather than lone counts.
        var vassalLords = realms.Liege.Values
            .Select(t => realms.HolderCounty.GetValueOrDefault(t))
            .Where(c => c is not null)
            .Select(c => c!)
            .ToHashSet();

        foreach (var faith in faiths.Faiths)
        {
            var regions = faith.AllSees.Select(s => (See: s, Region: regionOf(s))).Where(p => p.Region is { Count: > 0 }).ToList();
            if (regions.Count == 0) continue;

            int covered = regions.Sum(p => p.Region!.Count);
            int wanted = (int)Math.Round(covered * Share(eraYear));
            if (wanted == 0) continue;

            var holySites = faith.HolySites.Select(h => h.County).ToHashSet();
            var seeSeats = regions.Select(p => p.See.Seat).ToHashSet();

            double Score(Title c) => (previous.Contains(c) ? 1e4 : 0) + (seeSeats.Contains(c) ? 100 : 0)
                + (holySites.Contains(c) ? 60 : 0) + (worldCenters?.IsCenter(c) == true ? 40 : 0)
                + development.GetValueOrDefault(c);

            var candidates = regions.SelectMany(p => p.Region!).Distinct()
                .Where(c => !wilderness.Contains(c) && faiths.For(c) == faith)
                .OrderByDescending(Score).ThenBy(c => c.Index);

            int taken = 0;
            foreach (var county in candidates)
            {
                if (taken == wanted) break;
                if (!realms.HolderCounty.TryGetValue(county, out var lord) || wilderness.Contains(lord)) continue;
                if (GovernmentMap.Family(governments.For(lord)) is not (GovernmentMap.Feudal or GovernmentMap.Clan)) continue;

                if (lord == county)
                {
                    // A lone vassal count: one county, no vassals of his own, sworn to a duke or
                    // better. He becomes the churchman, under the same liege, and nothing else on
                    // the map moves; most of a settled realm's counties are held this way.
                    if (!seats.Contains(county) || demesne.GetValueOrDefault(county) != 1) continue;
                    if (!ReferenceEquals(HistoryWriter.Primary(county, realms), county) || vassalLords.Contains(county)) continue;
                    if (!realms.Liege.TryGetValue(county, out var liege) || Title.TierRank(liege.Tier) < Title.TierRank("d")
                        || liege.Key is WildernessMap.TitleKey or WildernessMap.RuinsTitleKey) continue;
                    if (!realms.HolderCounty.TryGetValue(liege, out var liegeSeat) || lordsUsed.Contains(liegeSeat)) continue;

                    governments.Set(county, GovernmentMap.Theocracy);
                    lordsUsed.Add(liegeSeat);
                }
                else
                {
                    // A county out of a lord's demesne, made a seat of its own under the realm's top liege.
                    if (lordsUsed.Contains(lord) || demesne.GetValueOrDefault(lord) < 3) continue;
                    if (Overlord(lord, realms) is not { } overlord) continue;

                    realms.HolderCounty[county] = county;
                    realms.SetLiege(county, overlord, LiegeOrigin.Church);
                    governments.Set(county, GovernmentMap.Theocracy);
                    seats.Add(county);
                    demesne[lord]--;
                    lordsUsed.Add(lord);
                }

                previous.Add(county);
                carved.Add(county);
                taken++;
            }
        }

        return carved;
    }

    /// <summary>
    /// The highest title above the lord's below a hegemony, as vanilla's prince-bishops answer to the
    /// king or emperor of the realm; null when that is under duchy tier, which a county cannot answer to.
    /// </summary>
    private static Title? Overlord(Title lord, RealmMap realms)
    {
        var current = HistoryWriter.Primary(lord, realms);
        var seen = new HashSet<Title>();
        while (seen.Add(current) && realms.Liege.TryGetValue(current, out var liege) && liege.Tier != "h") current = liege;

        return Title.TierRank(current.Tier) >= Title.TierRank("d")
               && current.Key is not (WildernessMap.TitleKey or WildernessMap.RuinsTitleKey)
            ? current : null;
    }
}
