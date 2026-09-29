using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// The road network's consumers.
///
/// Two today. The connection-arrows file is what vanilla's Silk Road map mode draws — 22 chains
/// of province ids under <c>common/connection_arrows</c>, which the generator used to leave in
/// place with vanilla's province ids on them. It now carries this map's trunk routes under the
/// same filename, so vanilla's file is shadowed rather than merged with. And a debug overlay,
/// because a road network is judged by eye or not at all: the log can say how many roads there
/// are, but not whether they follow the valleys.
/// </summary>
public static class RouteWriter
{
    private const string ArrowType = "silkRoadArrow";

    public static void WriteAll(string modDir, RouteNetwork routes, CrossingMap crossings, SilkRoadMap silkRoad,
        ProvinceMap provinces, int[] order, int baronyCount, TerrainClass[] provinceTerrain)
    {
        int arrows = WriteArrows(modDir, routes, silkRoad, baronyCount);
        WriteOverlay(modDir, routes, crossings, provinces, order, baronyCount, provinceTerrain);

        if (routes.Hubs.Count == 0)
        {
            Console.WriteLine("  routes: none (fewer than two markets)");
            return;
        }

        int primary = routes.Edges.Count(e => e.Primary);
        int backbone = routes.Edges.Count(e => e.Backbone);
        int lanes = routes.Edges.Count(e => e.Kind == RouteKind.Sea);
        int ferried = routes.Edges.Count(e => e.Crossings > 0);
        int ports = routes.Hubs.Count(h => h.Port);
        Console.WriteLine($"  routes: {routes.Hubs.Count} markets ({ports} ports), " +
                          $"{routes.Edges.Count - lanes} roads and {lanes} sea lanes " +
                          $"({backbone} backbone, {routes.Edges.Count - backbone} shortcuts, {primary} trunk, " +
                          $"{ferried} roads use a crossing) in {routes.Components} " +
                          (routes.Components == 1 ? "system" : "systems") +
                          (routes.Unreachable > 0 ? $"; {routes.Unreachable} touching pairs still cut off" : "") +
                          $"; {arrows} drawn as map-mode arrows");
    }

    /// <summary>
    /// The Silk Road itself as the primary arrows, then the trunk routes that join it. Only
    /// those: the map mode draws every arrow in the file, and a few hundred secondary routes
    /// would bury the map under them.
    ///
    /// Land routes only, each written in the direction the caravans travel, and none of them
    /// off on a continent of its own — see <see cref="JoiningTheRoad"/>. Returns how many arrows
    /// were written.
    /// </summary>
    private static int WriteArrows(string modDir, RouteNetwork routes, SilkRoadMap silkRoad, int baronyCount)
    {
        var b = new JominiBuilder();
        b.Comment("""
                  Generated routes for the Silk Road map mode, replacing vanilla's file of the
                  same name, whose province ids belong to the old map. The Silk Road's own
                  segments come first and are primary; the other trunk routes follow. Top
                  province is the start point, bottom the end; see MapGen/Routes.cs and
                  MapGen/SilkRoad.cs for how they are chosen.
                  """);
        b.Blank();

        // Sea lanes are not drawn at all. An arrow runs through each listed province's map
        // position and a sea province has none on this map — every water node of a lane landed on
        // the map's origin — so a lane could only ever be written as its two land ends, and the
        // engine then drew one dead-straight line across the open ocean between them. That is
        // what made trunk routes look like they were climbing out of the sea. A road that
        // ferries over a strait or a river is RouteKind.Land carrying a crossing and still
        // draws, which is the only water vanilla's own arrows ever cross.
        var road = silkRoad.Chain.Where(step => step.Edge.Kind == RouteKind.Land).ToList();
        var onRoad = road.Select(step => step.Edge).ToHashSet();

        var drawn = road
            .Select(step => (step.Edge, step.From))
            .Concat(JoiningTheRoad(routes, road, onRoad)
                .OrderByDescending(e => e.Traffic)
                .Select(e => (Edge: e, From: e.A)));

        int n = 0;
        foreach (var (e, fromHub) in drawn)
        {
            var from = routes.Hubs[fromHub];
            var to = routes.Hubs[fromHub == e.A ? e.B : e.A];

            using (b.Block($"gen_road_{n++}"))
            {
                b.Comment($"{from.County.Name} to {to.County.Name}, traffic {e.Traffic}");
                if (onRoad.Contains(e)) b.Field("is_primary", "yes");
                b.Quoted("arrow_type", ArrowType);

                // Vanilla: "Top province in list is start point; bottom province is end point."
                // An edge holds its provinces in its own A-to-B order, which is however the
                // pathfinder happened to store it and not the direction of travel, so a road the
                // Silk Road walks the other way has to be reversed here or its arrowheads point
                // back up the route. Off the road there is no direction to honour.
                using (b.Block("provinces"))
                    foreach (int p in (fromHub == e.A ? e.Provinces : Enumerable.Reverse(e.Provinces))
                                 .Where(p => p >= 1 && p <= baronyCount))
                        b.Token(p.ToString());
            }
            b.Blank();
        }

        string dir = Path.Combine(modDir, "common", "connection_arrows");
        Directory.CreateDirectory(dir);
        ParadoxText.WriteBom(Path.Combine(dir, "silk_road_arrows.txt"), b.ToString());
        return n;
    }

    /// <summary>
    /// The trunk roads that join the Silk Road, walking outward from it over the trunk itself.
    ///
    /// Vanilla's file is the road and nothing else: six primary legs, then fifteen feeders and
    /// alternates — the southern Tarim route, Lahur to Debul, Dvin to Constantinople — every one
    /// of them reachable from the road. Trunk here is chosen by traffic across the whole network
    /// instead, so on a world of several landmasses it also picks the busiest roads of continents
    /// the road never reaches. There is only one <c>silkRoadArrow</c> asset, with no variant for
    /// <c>is_primary</c>, so those drew exactly like the road, in the Silk Road's own map mode,
    /// on land that has no Silk Road on it.
    ///
    /// With no road to join — a map that could not carry the six stops — the whole trunk is drawn,
    /// since there is then no road for it to be mistaken for.
    /// </summary>
    private static List<RouteEdge> JoiningTheRoad(RouteNetwork routes, List<RouteStep> road,
        HashSet<RouteEdge> onRoad)
    {
        var candidates = routes.Edges
            .Where(e => e.Primary && e.Kind == RouteKind.Land && !onRoad.Contains(e))
            .ToList();
        if (road.Count == 0) return candidates;

        var joins = new List<RouteEdge>[routes.Hubs.Count];
        for (int i = 0; i < joins.Length; i++) joins[i] = [];
        foreach (var e in candidates) { joins[e.A].Add(e); joins[e.B].Add(e); }

        var reached = new bool[routes.Hubs.Count];
        var walk = new Queue<int>();
        void Reach(int hub)
        {
            if (reached[hub]) return;
            reached[hub] = true;
            walk.Enqueue(hub);
        }

        foreach (var step in road) { Reach(step.Edge.A); Reach(step.Edge.B); }
        while (walk.Count > 0)
        {
            int at = walk.Dequeue();
            foreach (var e in joins[at]) Reach(e.A == at ? e.B : e.A);
        }

        return candidates.Where(e => reached[e.A] && reached[e.B]).ToList();
    }

    /// <summary>
    /// The network over the terrain, at most 2048 wide: dim ground colours, secondary roads in
    /// thin yellow, secondary lanes in thin cyan, trunk routes in thick red, crossings as short
    /// white bars, markets as white squares with kingdom seats larger and wilderness grey.
    /// </summary>
    private static void WriteOverlay(string modDir, RouteNetwork routes, CrossingMap crossings,
        ProvinceMap provinces, int[] order, int baronyCount, TerrainClass[] provinceTerrain)
    {
        int step = Math.Max(1, provinces.Width / 2048);
        int outW = provinces.Width / step, outH = provinces.Height / step;
        var rgb = new byte[outW * outH * 3];

        for (int y = 0; y < outH; y++)
        {
            for (int x = 0; x < outW; x++)
            {
                int label = provinces.Label[(y * step) * provinces.Width + x * step];
                int di = (y * outW + x) * 3;
                if (label < 0) continue;

                int id = order[label];
                var seed = provinces.Seeds[label];
                (byte R, byte G, byte B) colour;
                if (!seed.IsLand) colour = seed.IsMajorRiver ? ((byte)22, (byte)40, (byte)80) : ((byte)18, (byte)28, (byte)60);
                else if (id >= 1 && id <= baronyCount && id < provinceTerrain.Length)
                {
                    var c = DebugRender.TerrainColour(provinceTerrain[id]);
                    colour = ((byte)(c.R / 3 + 40), (byte)(c.G / 3 + 40), (byte)(c.B / 3 + 40));
                }
                else colour = (50, 50, 50);

                rgb[di] = colour.R;
                rgb[di + 1] = colour.G;
                rgb[di + 2] = colour.B;
            }
        }

        var position = new (double X, double Y)[provinces.Count + 1];
        for (int label = 0; label < order.Length; label++)
        {
            int id = order[label];
            if (id >= 1 && id <= provinces.Count && label < provinces.Seeds.Count)
                position[id] = (provinces.Seeds[label].X / (double)step, provinces.Seeds[label].Y / (double)step);
        }

        void Plot(int x, int y, (byte R, byte G, byte B) c)
        {
            if (x < 0 || y < 0 || x >= outW || y >= outH) return;
            int di = (y * outW + x) * 3;
            rgb[di] = c.R; rgb[di + 1] = c.G; rgb[di + 2] = c.B;
        }

        void Line((double X, double Y) a, (double X, double Y) b, (byte R, byte G, byte B) c, int thickness)
        {
            int x0 = (int)a.X, y0 = (int)a.Y, x1 = (int)b.X, y1 = (int)b.Y;
            int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            int half = thickness / 2;

            while (true)
            {
                for (int oy = -half; oy <= half; oy++)
                    for (int ox = -half; ox <= half; ox++)
                        Plot(x0 + ox, y0 + oy, c);

                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        void Route(RouteEdge e, (byte R, byte G, byte B) c, int thickness)
        {
            for (int i = 1; i < e.Provinces.Count; i++)
                Line(position[e.Provinces[i - 1]], position[e.Provinces[i]], c, thickness);
        }

        foreach (var e in routes.Edges.Where(e => !e.Primary && e.Kind == RouteKind.Land)) Route(e, (230, 200, 60), 1);
        foreach (var e in routes.Edges.Where(e => !e.Primary && e.Kind == RouteKind.Sea)) Route(e, (70, 190, 255), 1);
        foreach (var e in routes.Edges.Where(e => e.Primary)) Route(e, (240, 60, 50), 3);

        foreach (var c in crossings.Crossings)
            Line((c.Start.X / (double)step, c.Start.Y / (double)step),
                 (c.Stop.X / (double)step, c.Stop.Y / (double)step),
                 c.Kind == CrossingKind.Strait ? ((byte)255, (byte)255, (byte)255) : ((byte)200, (byte)230, (byte)255), 2);

        foreach (var hub in routes.Hubs)
        {
            int size = hub.KingdomSeat ? 4 : 2;
            var (hx, hy) = position[hub.ProvinceId];
            var colour = hub.Wilderness ? ((byte)140, (byte)140, (byte)140) : ((byte)255, (byte)255, (byte)255);
            for (int oy = -size; oy <= size; oy++)
                for (int ox = -size; ox <= size; ox++)
                    Plot((int)hx + ox, (int)hy + oy, colour);
        }

        string dir = Path.Combine(modDir, "gen_debug");
        Directory.CreateDirectory(dir);
        PngWriter.WriteRgb8(Path.Combine(dir, "routes.png"), outW, outH, rgb);
    }
}
