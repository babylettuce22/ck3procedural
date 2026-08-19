using Ck3MapGen.Config;

namespace Ck3MapGen.MapGen;

/// <summary>
/// Which region each pixel belongs to for the purposes of the province partition — the field that
/// stops a barony straddling a border it has no business crossing.
///
/// The partition already had exactly this idea and only ever used it for one distinction. Every
/// neighbour test in <see cref="Provinces.RelaxNode"/> was <c>mask[nk] == domain</c>, where the
/// mask was land-or-sea; growth could therefore never cross a coastline, because a sea province
/// spreading onto land was obviously wrong. An Azgaar border is wrong to cross for the same reason
/// and in the same way, so it belongs in the same field rather than in a new mechanism beside it.
/// Widening that byte into a region id is the whole of the border constraint: the delta-stepping,
/// the relaxation and the tidy-up passes are unchanged, they simply have more edges they may not
/// cross.
///
/// This is deliberately a hard partition rather than a penalty in the cost field. A penalty bends
/// borders towards the export's without ever guaranteeing them, which leaves baronies straddling
/// states, realm borders accurate only to within a barony, and — worst of all — nothing that can be
/// asserted afterwards. A domain either contains a province or it does not, and
/// <see cref="Provinces.VerifyDomains"/> can say which.
///
/// A domain is keyed on the export's province *and* its state, not on the province alone. Azgaar
/// stores the two per cell and they are not nested the way the names suggest: a cell can sit inside
/// a state and belong to no province at all, which is ordinary on frontier ground a state has grown
/// over but never organised. Keyed on province alone every such cell across the whole map falls into
/// one shared domain, and a barony is then free to straddle a *state* border out there — the very
/// border the import exists to reproduce. So state-without-province land gets a domain per state.
///
/// Values: <see cref="Water"/> for everything the land mask calls sea, so rivers and oceans share
/// one domain exactly as they did before; <see cref="UnclaimedLand"/> for land in neither a state
/// nor a province; one domain per state for land in a state but no province; and one per province
/// for the rest. With no import every land pixel is <see cref="UnclaimedLand"/> and every sea pixel
/// is <see cref="Water"/>, which reproduces the old mask value for value — that is what keeps the
/// no-export path on the same code and the same output rather than on a second branch.
///
/// Because a domain fixes both ids, the majority votes in <see cref="AzgaarImport"/> that ask which
/// state or province a barony belongs to come back unanimous rather than merely dominant. They were
/// left in place deliberately: they are the same question, they answer it exactly once the geometry
/// is exact, and they still work if the partition is ever run unconstrained.
/// </summary>
public static class ProvinceDomain
{
    /// <summary>Sea, lakes and carved river corridors — everything the land mask calls water.</summary>
    public const int Water = 0;

    /// <summary>Land no Azgaar province claims, and every land pixel when there is no import.</summary>
    public const int UnclaimedLand = 1;

    /// <summary>How many times to re-run sliver absorption. Merging one sliver can leave its
    /// neighbour still under the floor, so the pass is repeated; it converges almost immediately
    /// and three rounds is well past the point where anything is still moving.</summary>
    private const int AbsorbPasses = 3;

    /// <summary>
    /// The domain of every pixel, at province-raster resolution.
    /// </summary>
    public static int[] Build(byte[] mask, AzgaarImport? azgaar, int width, int height, MapConfig cfg)
    {
        var domain = new int[width * height];

        if (azgaar is null)
        {
            for (int i = 0; i < domain.Length; i++)
                domain[i] = mask[i] == 1 ? UnclaimedLand : Water;
            return domain;
        }

        var raster = azgaar.Raster;
        int limit = Math.Min(domain.Length, raster.CellByPixel.Length);

        // Province ids are pushed clear of the block reserved for states, so the two never collide.
        int stateSpan = azgaar.World.Pack.States.Count + 1;

        Parallel.For(0, height, y =>
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                int i = row + x;
                if (mask[i] != 1) { domain[i] = Water; continue; }
                if (i >= limit) { domain[i] = UnclaimedLand; continue; }

                int province = raster.ProvinceAt(i);
                if (province > 0) { domain[i] = 1 + stateSpan + province; continue; }

                int state = raster.StateAt(i);
                domain[i] = state > 0 ? 1 + state : UnclaimedLand;
            }
        });

        AbsorbSlivers(domain, width, height, cfg);
        Report(domain, cfg);
        return domain;
    }

    /// <summary>
    /// Folds land domain fragments too small to hold a province into whichever neighbour they share
    /// the most border with.
    ///
    /// Necessary rather than cosmetic. Azgaar's borders are drawn on its own Voronoi cells and know
    /// nothing about where our heightmap put the coastline, so the two cut across each other and
    /// leave crumbs — a five-pixel corner of one province stranded the far side of an inlet, an
    /// islet the border happens to clip. Under a hard constraint every one of those crumbs is a
    /// region the partition must place a province in, and each becomes a barony of a few pixels
    /// that CK3 cannot derive a centroid for.
    ///
    /// The floor is applied per *connected component*, not per province, and that distinction is
    /// the whole safety of it: a province's main body is never at risk, only the fragments cut off
    /// from it. Erasing whole small provinces would be throwing away the very data the import
    /// exists to read, so the floor is <see cref="MapConfig.MinProvincePixels"/> — the point below
    /// which a province cannot exist at all — and not a fraction of a barony.
    /// </summary>
    private static void AbsorbSlivers(int[] domain, int width, int height, MapConfig cfg)
    {
        int floor = Math.Max(1, cfg.MinProvincePixels);
        int absorbed = 0;
        long absorbedPixels = 0;

        var component = new int[domain.Length];
        var stack = new Stack<int>();

        for (int pass = 0; pass < AbsorbPasses; pass++)
        {
            Array.Fill(component, -1);

            var members = new List<List<int>>();

            for (int start = 0; start < domain.Length; start++)
            {
                if (component[start] >= 0 || domain[start] == Water) continue;

                int id = members.Count;
                var cells = new List<int>();
                members.Add(cells);

                component[start] = id;
                stack.Push(start);

                while (stack.Count > 0)
                {
                    int cell = stack.Pop();
                    cells.Add(cell);

                    int cx = cell % width, cy = cell / width;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int ny = cy + dy;
                        if (ny < 0 || ny >= height) continue;

                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = cx + dx;
                            if (nx < 0 || nx >= width || (dx == 0 && dy == 0)) continue;

                            int nk = ny * width + nx;
                            if (component[nk] >= 0 || domain[nk] != domain[cell]) continue;

                            component[nk] = id;
                            stack.Push(nk);
                        }
                    }
                }
            }

            // Collected first and applied after, so a component's own reassignment cannot change
            // the border its neighbour is being measured against half way through the pass.
            var reassign = new Dictionary<int, int>();

            for (int id = 0; id < members.Count; id++)
            {
                var cells = members[id];
                if (cells.Count >= floor) continue;

                var votes = new Dictionary<int, int>();
                foreach (int cell in cells)
                {
                    int cx = cell % width, cy = cell / width;
                    foreach (var (dx, dy) in Neighbourhood)
                    {
                        int nx = cx + dx, ny = cy + dy;
                        if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;

                        int other = domain[ny * width + nx];
                        if (other == Water || other == domain[cell]) continue;
                        votes[other] = votes.GetValueOrDefault(other) + 1;
                    }
                }

                if (votes.Count == 0) continue;

                // Ties on the lower domain id, so the same map always cleans up the same way.
                int best = votes.OrderByDescending(v => v.Value).ThenBy(v => v.Key).First().Key;
                reassign[id] = best;
            }

            if (reassign.Count == 0) break;

            foreach (var (id, target) in reassign)
            {
                foreach (int cell in members[id]) domain[cell] = target;
                absorbed++;
                absorbedPixels += members[id].Count;
            }
        }

        if (absorbed > 0)
            Console.WriteLine($"  domain: absorbed {absorbed} land fragments under {floor} px " +
                              $"({absorbedPixels} px) into their surrounding province");
    }

    private static readonly (int Dx, int Dy)[] Neighbourhood = [(-1, 0), (1, 0), (0, -1), (0, 1)];

    private static void Report(int[] domain, MapConfig cfg)
    {
        var sizes = new Dictionary<int, int>();
        foreach (int d in domain)
        {
            if (d == Water) continue;
            sizes[d] = sizes.GetValueOrDefault(d) + 1;
        }

        if (sizes.Count == 0) return;

        int claimed = sizes.Count(s => s.Key != UnclaimedLand);
        var areas = sizes.Where(s => s.Key != UnclaimedLand).Select(s => s.Value).OrderBy(v => v).ToList();
        if (areas.Count == 0) return;

        // How many baronies each domain can hold is what decides whether the hierarchy can be built
        // at the rank Azgaar asked for, so it is worth saying before the partition rather than
        // after the title plan discovers it.
        int starved = areas.Count(a => a < cfg.BaronyPixels);

        Console.WriteLine($"  domain: {claimed} azgaar regions bound, " +
                          $"area p10 {areas[areas.Count / 10]} / median {areas[areas.Count / 2]} / " +
                          $"p90 {areas[areas.Count * 9 / 10]} px " +
                          $"({starved} under one barony of {cfg.BaronyPixels:F0} px)");
    }
}
