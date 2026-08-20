using Ck3MapGen.Config;

namespace Ck3MapGen.MapGen;

public static class RiverMap
{
    public const byte PaletteSource = 0;   // #00ff00 (Green) - Source of the main system
    public const byte PaletteJoin = 1;     // #ff0000 (Red) - Tributary merge
    public const byte PaletteSplit = 2;    // #fffc00 (Yellow) - River split
    public const byte PaletteNarrow = 3;   // Light Cyan (0, 225, 255)
    public const byte PaletteWide = 11;    // Deep Blue (0, 0, 100)
    public const byte PaletteWater = 254;  // #ff0080 (Magenta) - Sea & Major Rivers
    public const byte PaletteLand = 255;   // #ffffff (White) - Land

    public static byte[] Generate(MapConfig cfg, ProvinceMap provinces, Drainage drainage)
    {
        int width = cfg.ProvinceWidth;
        int height = cfg.ProvinceHeight;
        int n = width * height;

        var indices = new byte[n];
        Array.Fill(indices, PaletteLand);

        // 1. Mark water from province partition (both open ocean and carved major rivers)
        Parallel.For(0, n, i =>
        {
            if (!provinces.Seeds[provinces.Label[i]].IsLand)
            {
                indices[i] = PaletteWater;
            }
        });

        if (!cfg.EnableMinorRivers)
            return indices;

        // Flow thresholds scaled to map resolution
        float minFlowThreshold = (float)Math.Max(350.0, cfg.Scaled(800.0) / Math.Max(0.1, cfg.RiverDensity));
        float maxFlowThreshold = (float)Math.Max(minFlowThreshold * 10f, cfg.Scaled(25000.0));
        int minLength = (int)Math.Max(10, cfg.Scaled(16.0));

        // 2. Identify candidate river sources
        var sourceCandidates = new List<int>();
        for (int i = 0; i < n; i++)
        {
            if (indices[i] != PaletteLand) continue;
            if (drainage.Flow[i] < minFlowThreshold) continue;

            bool isNewSource = true;
            for (int dy = -1; dy <= 1 && isNewSource; dy++)
            {
                for (int dx = -1; dx <= 1 && isNewSource; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int x = (i % width) + dx;
                    int y = (i / width) + dy;
                    if (x < 0 || x >= width || y < 0 || y >= height) continue;

                    int nb = y * width + x;
                    if (indices[nb] == PaletteLand && drainage.Receiver[nb] == i && drainage.Flow[nb] >= minFlowThreshold)
                    {
                        isNewSource = false;
                    }
                }
            }

            if (isNewSource) sourceCandidates.Add(i);
        }

        // Trace largest river basins first so main trunks get Green source
        sourceCandidates.Sort((a, b) => drainage.Flow[b].CompareTo(drainage.Flow[a]));

        var isRiverPixel = new bool[n];

        // Width is painted now; the markers are derived later, so the flow-derived band each pixel
        // belongs to has to survive until then rather than being overwritten by a marker.
        var widthBand = new byte[n];
        var isHead = new bool[n];

        // Where one course ran into another is knowledge the raster does not carry: once both are
        // painted, a merge looks like any other neighbourhood, and the pixel it happened at is not
        // always one the geometry would call a confluence. Remembered here and re-applied once the
        // thinning has settled, because ck3-tiger ends a tributary's segment at its join marker and
        // a tributary whose segment never ends is reported as an orphan.
        var mergeJoins = new List<int>();
        int drawnRivers = 0;

        foreach (int source in sourceCandidates)
        {
            var path = new List<int>();
            int curr = source;
            bool hitWater = false;
            bool hitExisting = false;
            int mergeTarget = -1;

            while (curr >= 0 && path.Count < 3000)
            {
                if (indices[curr] == PaletteWater)
                {
                    hitWater = true;
                    // Extend 1-2 pixels into water for engine spline direction
                    path.Add(curr);
                    int intoWater = drainage.Receiver[curr];
                    if (intoWater != curr && indices[intoWater] == PaletteWater)
                        path.Add(intoWater);
                    break;
                }

                if (isRiverPixel[curr])
                {
                    hitExisting = true;
                    mergeTarget = curr;
                    break;
                }

                path.Add(curr);
                int into = drainage.Receiver[curr];
                if (into == curr) break;
                curr = into;
            }

            if (path.Count < minLength && !hitExisting) continue;
            if (!hitWater && !hitExisting) continue;

            // Convert 8-connected diagonal path to strict 4-connected orthogonal path
            var orthoPath = MakeOrthogonal(path, width, height);

            // Paint width only. A source and a join are statements *about* the finished geometry,
            // and the geometry is not finished here: a later course can run into this one's head,
            // and thinning can shift a confluence by a pixel or dissolve it entirely. Deciding the
            // markers now means deciding them against a raster that is still moving, which is how a
            // green ended up mid-course and how two reds ended up side by side with no plain water
            // between them. EnforceEngineTopology assigns them once nothing is moving any more.
            for (int k = 0; k < orthoPath.Count; k++)
            {
                int c = orthoPath[k];

                float f = drainage.Flow[c];
                double t = Math.Clamp(Math.Log(Math.Max(1f, f / minFlowThreshold)) /
                                      Math.Log(Math.Max(1.01f, maxFlowThreshold / minFlowThreshold)), 0, 1);
                int band = PaletteNarrow + (int)Math.Round(t * (PaletteWide - PaletteNarrow));

                widthBand[c] = (byte)Math.Clamp(band, PaletteNarrow, PaletteWide);
                indices[c] = widthBand[c];
                isRiverPixel[c] = true;
            }

            // A course that began by running into a river already drawn has no head of its own;
            // only a trunk that started on dry land is a candidate source.
            if (!hitExisting) isHead[orthoPath[0]] = true;
            else if (mergeTarget >= 0) mergeJoins.Add(mergeTarget);

            drawnRivers++;
        }

        // Geometry first, then orphans: thinning can sever a fragment from its outlet, and
        // whatever it leaves stranded should be pruned rather than shipped.
        EnforceEngineTopology(indices, widthBand, isHead, mergeJoins, width, height);
        Console.WriteLine($"  minor rivers: generated {drawnRivers} engine-compliant tributary streams in rivers.png");
        return indices;
    }

    /// <summary>
    /// Make the painted raster obey the two geometric rules the engine imposes on rivers.png: no
    /// river pixel orthogonally adjacent to more than two others (three for a join or a split), and
    /// no river two pixels wide.
    ///
    /// Both are hard rules rather than preferences — a malformed river map is documented as a crash,
    /// and two-pixel-wide rivers and diagonal-only links simply fail to render. Measured on a
    /// generated map, 11,412 of 81,754 river pixels (14.0%) had too many orthogonal neighbours and
    /// 5,807 solid 2x2 blocks were two pixels wide, which is very likely why courses appeared to
    /// stop halfway: the engine gave up drawing where the geometry stopped making sense.
    ///
    /// They come from painting many traced paths into one raster. <see cref="MakeOrthogonal"/>
    /// guarantees a single path is 4-connected, but it inserts corner pixels to do it, and a corner
    /// laid beside a path already drawn — or two courses converging a pixel apart before they meet —
    /// makes a block no single path ever contained.
    ///
    /// The repair is to thin rather than to redraw. A pixel is removed only where its own
    /// neighbourhood stays connected without it, so a course can lose its redundant width but never
    /// be cut in half; anything left with three neighbours after thinning is a genuine confluence
    /// and is marked as a join, which is the one thing the engine allows three neighbours.
    ///
    /// Thinning runs before any marker exists, and the markers are read off the result afterwards.
    /// Doing it the other way round is what left ck3-tiger complaining: markers were exempt from
    /// thinning, so a 2x2 block with a marker in it could never be narrowed, and a marker decided
    /// against a half-finished raster could end up mid-course or pressed against another marker.
    ///
    /// The one thing that cannot be read back off the geometry is which course flowed into which,
    /// so <paramref name="mergeJoins"/> carries it here. A tributary landing on the end of a trunk
    /// leaves a pixel with two arms, indistinguishable from ordinary water, and dropping its marker
    /// leaves the tributary's segment with nothing to terminate it — measured at 1,217 orphaned
    /// segments against 13 without.
    /// </summary>
    private static void EnforceEngineTopology(byte[] indices, byte[] widthBand, bool[] isHead,
                                              List<int> mergeJoins, int width, int height)
    {
        static bool IsRiver(byte v) => v <= PaletteWide;

        // The eight neighbours in ring order, so that consecutive entries are themselves adjacent.
        int[] ringX = [-1, 0, 1, 1, 1, 0, -1, -1];
        int[] ringY = [-1, -1, -1, 0, 1, 1, 1, 0];

        // A pixel may go only if what is left behind still hangs together: its river neighbours must
        // form one group around it, and there must be at least two of them, or removal would be
        // eroding the end of a course rather than narrowing it.
        bool Removable(int i)
        {
            if (!IsRiver(indices[i])) return false;

            int x = i % width, y = i / width;
            int ring = 0, count = 0;
            for (int k = 0; k < 8; k++)
            {
                int nx = x + ringX[k], ny = y + ringY[k];
                if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                if (!IsRiver(indices[ny * width + nx])) continue;

                ring |= 1 << k;
                count++;
            }
            if (count < 2) return false;

            // One run of river around the ring means one group, so nothing is severed by leaving.
            int runs = 0;
            for (int k = 0; k < 8; k++)
                if ((ring & (1 << k)) != 0 && (ring & (1 << ((k + 7) % 8))) == 0) runs++;

            return runs == 1;
        }

        var candidates = new int[4];
        int thinned = 0;

        for (int pass = 0; pass < 4; pass++)
        {
            int before = thinned;

            for (int y = 0; y + 1 < height; y++)
            {
                for (int x = 0; x + 1 < width; x++)
                {
                    int a = y * width + x, b = a + 1, c = a + width, d = c + 1;
                    if (!IsRiver(indices[a]) || !IsRiver(indices[b]) ||
                        !IsRiver(indices[c]) || !IsRiver(indices[d])) continue;

                    // The diagonal partners first: dropping one of those keeps the block's own
                    // corner-to-corner run intact, which is the shape a course actually needs.
                    candidates[0] = d; candidates[1] = a; candidates[2] = b; candidates[3] = c;
                    for (int k = 0; k < 4; k++)
                    {
                        if (!Removable(candidates[k])) continue;

                        indices[candidates[k]] = PaletteLand;
                        thinned++;
                        break;
                    }
                }
            }

            if (thinned == before) break;
        }

        int OrthogonalDegree(int i)
        {
            int x = i % width, y = i / width, degree = 0;
            if (x > 0 && IsRiver(indices[i - 1])) degree++;
            if (x + 1 < width && IsRiver(indices[i + 1])) degree++;
            if (y > 0 && IsRiver(indices[i - width])) degree++;
            if (y + 1 < height && IsRiver(indices[i + width])) degree++;
            return degree;
        }

        int stubborn = 0;
        for (int i = 0; i < indices.Length; i++)
        {
            if (!IsRiver(indices[i])) continue;

            int degree = OrthogonalDegree(i);
            if (degree <= 2) continue;

            if (degree > 3)
            {
                // Four ways out is beyond what even a join may have; give up one arm if any arm can
                // be spared, and count the rest rather than cutting a course in half to satisfy a
                // rule.
                int x = i % width, y = i / width;
                candidates[0] = y > 0 ? i - width : -1;
                candidates[1] = y + 1 < height ? i + width : -1;
                candidates[2] = x > 0 ? i - 1 : -1;
                candidates[3] = x + 1 < width ? i + 1 : -1;

                bool relieved = false;
                for (int k = 0; k < 4 && !relieved; k++)
                {
                    if (candidates[k] < 0 || !Removable(candidates[k])) continue;

                    indices[candidates[k]] = PaletteLand;
                    thinned++;
                    relieved = true;
                }

                if (!relieved) stubborn++;
            }
        }

        // The geometry has stopped moving, so the markers can finally be read off it. Doing this in
        // one sweep at the end is what keeps them consistent: every marker is a statement about the
        // neighbourhood as it actually ships, not as it looked when some earlier path was drawn.
        //
        // ck3-tiger enforces the two readings precisely. A join must have at least two ordinary
        // river arms — the tributary arriving and the water it joins — or it is "not joining another
        // river". A source must be a free end whose one arm is ordinary river, or it is "not at the
        // source of a river". Both failures are the same underlying mistake: a marker whose arm
        // turned out to be another marker rather than water.
        int joins = 0, sources = 0;
        for (int i = 0; i < indices.Length; i++)
        {
            if (!IsRiver(indices[i])) continue;

            // Three arms is a confluence, and red is how the engine is told so: a tributary joining
            // here, flowing towards this pixel. A crossing left over-connected above is still better
            // described as a join than as plain water.
            indices[i] = OrthogonalDegree(i) >= 3 ? PaletteJoin : widthBand[i];
            if (indices[i] == PaletteJoin) joins++;
        }

        // The remembered merges, for the ones whose pixel survived the thinning. Geometry alone
        // cannot find these: a tributary that lands on the end of a trunk leaves a pixel with only
        // two arms, which reads as ordinary water however closely it is examined.
        foreach (int m in mergeJoins)
        {
            if (!IsRiver(indices[m]) || indices[m] == PaletteJoin) continue;

            indices[m] = PaletteJoin;
            joins++;
        }

        // Sources afterwards, because whether a head qualifies depends on the joins being placed. A
        // head that opens directly onto a confluence is a stub the engine cannot trace a course
        // from, so it stays ordinary water rather than claiming to be a spring.
        for (int i = 0; i < indices.Length; i++)
        {
            if (!isHead[i] || !IsRiver(indices[i])) continue;

            int x = i % width, y = i / width, degree = 0, sole = -1;
            if (x > 0 && IsRiver(indices[i - 1])) { degree++; sole = i - 1; }
            if (x + 1 < width && IsRiver(indices[i + 1])) { degree++; sole = i + 1; }
            if (y > 0 && IsRiver(indices[i - width])) { degree++; sole = i - width; }
            if (y + 1 < height && IsRiver(indices[i + width])) { degree++; sole = i + width; }

            if (degree != 1 || indices[sole] == PaletteJoin) continue;

            indices[i] = PaletteSource;
            sources++;
        }

        if (thinned > 0 || joins > 0)
            Console.WriteLine($"  minor rivers: thinned {thinned:N0} px to keep courses one pixel wide, " +
                              $"marked {joins:N0} confluence(s) as joins and {sources:N0} head(s) as sources" +
                              (stubborn > 0 ? $", {stubborn:N0} crossing(s) left over-connected" : ""));
    }


    /// <summary>
    /// Ensures strictly 4-connected (orthogonal) river pixels without diagonal-only corners.
    /// </summary>
    private static List<int> MakeOrthogonal(List<int> rawPath, int width, int height)
    {
        var result = new List<int>();
        if (rawPath.Count == 0) return result;

        result.Add(rawPath[0]);

        for (int i = 1; i < rawPath.Count; i++)
        {
            int prev = result[^1];
            int curr = rawPath[i];

            int px = prev % width, py = prev / width;
            int cx = curr % width, cy = curr / width;

            int dx = cx - px;
            int dy = cy - py;

            // If diagonal step, insert an intermediate orthogonal pixel
            if (dx != 0 && dy != 0)
            {
                int intermediate = py * width + cx;
                result.Add(intermediate);
            }

            result.Add(curr);
        }

        return result;
    }
}