using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// CK3 1.21's roads, <c>common/roads/road_types</c>: each road a chain of province ids from one
/// market to the next, land roads in 00_base_roads.txt and sea roads (<c>spline_asset_id = 1</c>) in
/// 02_sea_roads.txt, as vanilla files them. Vanilla's folder is replaced (ModWriter.ReplacePaths).
///
/// <para><b>Which roads.</b> Vanilla has exactly one land road for each two duchies that touch
/// (1,968 of its 1,975 touching pairs; the other 92 roads cross straits), from capital to capital:
/// 2,060 roads, about 4.6 to a market, over 57 % of land baronies. Its 2,141 sea roads join each of
/// 358 port duchies to 12 others (median) over about 4 water provinces, whether or not a land road
/// joins them too. Measured by <c>ck3devtools\calibration\roads_mine.py</c> and
/// <c>roads_duchy.py</c>. So the land roads here are the land <see cref="RouteNetwork.Candidates"/>,
/// which are that same rule (every touching duchy pair, cheapest barony path, crossings
/// included), and the sea roads are <see cref="RouteNetwork.SeaRoads"/>. Neither is the pruned
/// <see cref="RouteNetwork.Edges"/> the Silk Road arrows draw. Wilderness markets are included, so
/// unsettled land between realms does not cut the network.</para>
///
/// <para><b>Tiers.</b> Vanilla starts 125 of its 2,060 land roads (6 %) at tier 1, and only a road above
/// tier 0 is drawn. Here the routes' primary roads (<see cref="RouteEdge.Primary"/>, the most travelled
/// share of the pruned network) start at tier 1 and everything else at 0, where it still exists,
/// levels with use and carries its tier_0 modifiers. What a drawn road is drawn along is
/// <see cref="SplineNetworkWriter"/>'s file, written here from the same province chains.</para>
/// </summary>
public static class RoadWriter
{
    /// <summary>Writes the road files and their spline network; returns the network's land road lines
    /// for <see cref="BridgeWriter"/>.</summary>
    public static IReadOnlyList<List<(double X, double Y)>> WriteAll(string modDir, RouteNetwork routes, int baronyCount,
        ProvinceMap provinces, int[] order, ProvinceAnchor.Anchors anchors)
    {
        // Wilderness markets get their roads too. They used to be left out ("nobody trades with
        // the empty land"), which cut the network wherever unsettled land lay between two realms:
        // a trade route could not cross it, and a colony founded there sat off every road
        // (reported from a 1.21 beta game, 2026-11-25). They start at tier 0 like most of
        // vanilla's, so nothing is drawn until use levels them.
        bool Usable(RouteEdge e) => e.Provinces.Count >= 2;
        var landRoads = routes.Candidates.Where(e => e.Kind == RouteKind.Land && Usable(e))
            .OrderBy(e => e.A).ThenBy(e => e.B).ToList();
        var seaRoads = routes.SeaRoads.Where(Usable).OrderBy(e => e.A).ThenBy(e => e.B).ToList();

        string dir = Path.Combine(modDir, "common", "roads", "road_types");
        Directory.CreateDirectory(dir);
        Write(Path.Combine(dir, "00_base_roads.txt"), routes, landRoads, sea: false);
        Write(Path.Combine(dir, "02_sea_roads.txt"), routes, seaRoads, sea: true);
        var network = SplineNetworkWriter.Write(modDir,
            landRoads.Select(e => (e.Provinces, false)).Concat(seaRoads.Select(e => (e.Provinces, true))).ToList(),
            provinces, order, anchors);

        var covered = landRoads.SelectMany(e => e.Provinces).Where(p => p >= 1 && p <= baronyCount).ToHashSet();
        int markets = routes.Hubs.Count(h => !h.Wilderness);
        var portDegree = seaRoads.SelectMany(e => new[] { e.A, e.B }).GroupBy(h => h).Select(g => g.Count()).OrderBy(n => n).ToList();
        Console.WriteLine($"  roads: {landRoads.Count} land roads between {markets} markets " +
                          $"({(markets > 0 ? 2.0 * landRoads.Count / markets : 0):F1} per market; vanilla 4.6), " +
                          $"covering {100.0 * covered.Count / Math.Max(1, baronyCount):F0}% of baronies (vanilla 57%); " +
                          $"{seaRoads.Count} sea roads, {(portDegree.Count > 0 ? portDegree[portDegree.Count / 2] : 0)} per port " +
                          $"(median; vanilla 12); {landRoads.Count(e => e.Primary)} start at tier 1 (vanilla 6%); " +
                          $"spline network {network.Hubs} hubs, {network.ControlPoints} control points, {network.Strips} strips" +
                          (network.Straight > 0 ? $", {network.Straight} drawn straight (no path found)" : ""));
        return network.LandLines;
    }

    private static void Write(string path, RouteNetwork routes, IEnumerable<RouteEdge> edges, bool sea)
    {
        var b = new JominiBuilder();
        b.Comment(sea
            ? "Generated sea roads, port to port over this map's water. See Emit/Gameplay/RoadWriter.cs."
            : "Generated land roads, market to market over this map's baronies. See Emit/Gameplay/RoadWriter.cs.");
        b.Blank();

        int n = 0;
        foreach (var e in edges)
        {
            var from = routes.Hubs[e.A];
            var to = routes.Hubs[e.B];
            string key = $"{Short(from.County.Key)}_to_{Short(to.County.Key)}_{(sea ? "sea_road" : "road")}";

            using (b.Block(key))
            {
                b.Field("tier", !sea && e.Primary ? "1" : "0");
                if (sea) b.Field("spline_asset_id", "1");
                using (b.Block("provinces"))
                    foreach (int p in e.Provinces) b.Token(p.ToString());
            }
            b.Blank();
            n++;
        }

        ParadoxText.WriteBom(path, b.ToString());
    }

    private static string Short(string countyKey) => countyKey.StartsWith("c_", StringComparison.Ordinal) ? countyKey[2..] : countyKey;
}
