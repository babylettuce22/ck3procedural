using Ck3MapGen.Config;

namespace Ck3MapGen.MapGen;

/// <summary>
/// Seeds each Azgaar province with as many baronies as it has towns, so the export's provinces
/// come out as counties holding exactly its own settlements.
///
/// The ordinary partition scatters seeds at one barony size over all the land
/// (<see cref="MapConfig.BaronyPixels"/>), and on an import that cannot be faithful to the export.
/// Azgaar province areas vary far more than their town counts — measured on Ondrerol, areas span
/// 3.6 to 62 vanilla baronies (p10 to p90) while towns span 1 to 8 — so any single size either
/// gives a big province more baronies than a county can hold, which <see cref="AzgaarHierarchy"/>
/// then splits into several counties along borders the export never drew, or leaves a small
/// province one barony however many towns it has.
///
/// So each province is reseeded on its own count: one barony per town the export puts in it, at
/// least one, and more where the barony would otherwise exceed
/// <see cref="MapConfig.AzgaarMaxBaronyArea"/> times vanilla's size. The cap is the one place this
/// departs from the export, and deliberately: a thinly-settled province is often enormous, and one
/// barony across it (up to 87 vanilla baronies' worth on Ondrerol) would be a single siege, a single
/// terrain type and a single holding for a whole region. On Ondrerol the cap takes 1,658 baronies to
/// 2,072 and leaves 73 of 398 provinces over the county ceiling, almost all of them large and empty.
///
/// Land outside any Azgaar province (a state's unprovinced ground, or no state's) keeps the ordinary
/// scatter, and so does anything a painted impassable mask carved out. Seeds within a province are
/// spread by farthest-point sampling on a coarse grid and then settled by the partition's own Lloyd
/// relaxation, which never moves a seed out of its domain. Nothing here draws on an Rng: the result
/// is a pure function of the export, the heightmap and the config.
/// </summary>
public static class AzgaarSeeding
{
    /// <summary>
    /// Replaces the scattered land seeds inside every Azgaar province with the province's own count.
    /// Call after each seed's <see cref="ProvinceSeed.Domain"/> is set and before seed coverage.
    /// </summary>
    public static void Reseed(List<ProvinceSeed> seeds, int[] domain, int width, int height,
                              AzgaarImport azgaar, MapConfig cfg)
    {
        var world = azgaar.World;
        int stateSpan = world.Pack.States.Count + 1;
        int firstProvinceDomain = 2 + stateSpan;
        int maxDomain = firstProvinceDomain + world.Pack.Provinces.Count;

        bool IsProvince(int d) => !ProvinceDomain.IsPainted(d) && d >= firstProvinceDomain && d < maxDomain;

        // Area of every province domain, exactly, and its towns.
        var area = new long[maxDomain];
        foreach (int d in domain)
            if (IsProvince(d)) area[d]++;

        var towns = new int[maxDomain];
        foreach (var burg in world.RealBurgs)
        {
            if (burg.Cell < 0 || burg.Cell >= world.Pack.Cells.Count) continue;
            int province = world.Pack.Cells[burg.Cell].Province;
            if (province <= 0) continue;
            int d = 1 + stateSpan + province;
            if (d < maxDomain) towns[d]++;
        }

        double capPixels = Math.Max(1.0, cfg.AzgaarMaxBaronyArea) * cfg.BaronyPixelsAtVanilla;
        var target = new int[maxDomain];
        int fromTowns = 0, fromCap = 0, provinces = 0;
        for (int d = firstProvinceDomain; d < maxDomain; d++)
        {
            if (area[d] == 0) continue;
            provinces++;
            int byTowns = Math.Max(1, towns[d]);
            int byCap = (int)Math.Ceiling(area[d] / capPixels);
            target[d] = Math.Max(byTowns, byCap);
            fromTowns += byTowns;
            fromCap += target[d] - byTowns;
        }

        // Candidates on a coarse grid, fine enough that the smallest barony still gets several.
        double smallestBarony = double.MaxValue;
        for (int d = firstProvinceDomain; d < maxDomain; d++)
            if (target[d] > 0) smallestBarony = Math.Min(smallestBarony, (double)area[d] / target[d]);
        int step = Math.Clamp((int)Math.Sqrt(Math.Max(1, smallestBarony) / 16), 1, 8);

        var candidates = new List<int>?[maxDomain];
        for (int y = step / 2; y < height; y += step)
        {
            int row = y * width;
            for (int x = step / 2; x < width; x += step)
            {
                int d = domain[row + x];
                if (IsProvince(d) && target[d] > 0) (candidates[d] ??= []).Add(row + x);
            }
        }

        int removed = seeds.RemoveAll(s => s.IsLand && !s.IsMajorRiver && IsProvince(s.Domain));

        // Each province independently, in domain order, so the result does not depend on scheduling.
        var placed = new List<ProvinceSeed>?[maxDomain];
        Parallel.For(firstProvinceDomain, maxDomain, d =>
        {
            if (candidates[d] is not { Count: > 0 } cells) return;
            placed[d] = Spread(cells, Math.Min(target[d], cells.Count), width, d);
        });

        int added = 0;
        for (int d = firstProvinceDomain; d < maxDomain; d++)
        {
            if (placed[d] is not { } list) continue;
            seeds.AddRange(list);
            added += list.Count;
        }

        Console.WriteLine($"  azgaar seeding: {provinces} provinces reseeded with {added} baronies " +
                          $"({fromTowns} for their towns, {fromCap} more where a barony would pass " +
                          $"{cfg.AzgaarMaxBaronyArea:0.#}x vanilla's size); {removed} scattered seeds replaced");
    }

    /// <summary>
    /// <paramref name="count"/> points spread over a province's candidate cells by farthest-point
    /// sampling, starting from the cell nearest its centroid.
    /// </summary>
    private static List<ProvinceSeed> Spread(List<int> cells, int count, int width, int d)
    {
        double cx = 0, cy = 0;
        foreach (int c in cells) { cx += c % width; cy += c / width; }
        cx /= cells.Count;
        cy /= cells.Count;

        int first = 0;
        double best = double.MaxValue;
        for (int i = 0; i < cells.Count; i++)
        {
            double dx = cells[i] % width - cx, dy = cells[i] / width - cy;
            double dist = dx * dx + dy * dy;
            if (dist < best) { best = dist; first = i; }
        }

        // Distance from each candidate to the nearest point chosen so far.
        var nearest = new double[cells.Count];
        Array.Fill(nearest, double.MaxValue);
        var chosen = new List<ProvinceSeed>(count);
        int next = first;

        for (int k = 0; k < count; k++)
        {
            int cell = cells[next];
            int px = cell % width, py = cell / width;
            chosen.Add(new ProvinceSeed { X = px, Y = py, IsLand = true, Domain = d });

            int far = -1;
            double farthest = -1;
            for (int i = 0; i < cells.Count; i++)
            {
                double dx = cells[i] % width - px, dy = cells[i] / width - py;
                double dist = dx * dx + dy * dy;
                if (dist < nearest[i]) nearest[i] = dist;
                if (nearest[i] > farthest) { farthest = nearest[i]; far = i; }
            }
            if (far < 0 || farthest <= 0) break;
            next = far;
        }

        return chosen;
    }
}
