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

    private static readonly int[] OrthoDx = [-1, 1, 0, 0];
    private static readonly int[] OrthoDy = [0, 0, -1, 1];

    /// <summary>
    /// How a traced course ended: in water, or beside another river it now joins.
    /// </summary>
    private enum Ending { Mouth, Join }

    /// <summary>
    /// Draws the tributary rivers of rivers.png, obeying the rules the engine reads them by.
    ///
    /// The engine traces each river as a run of orthogonally connected normal pixels — a
    /// segment — and reads its direction off the one marker that terminates it: a green at its
    /// head, from which water flows away, or a red at its end, towards which water flows. A red
    /// is the last pixel of a tributary, lying beside an interior pixel of the river it joins; a
    /// normal pixel may touch no more than two other normal pixels. Vanilla bears this out: its
    /// rivers.png has 631 greens and 630 reds with 710 rivers starting bare, so a tributary has
    /// no green of its own — only a river that reaches water does. Rivers two pixels wide, or
    /// connected only through a diagonal, fail to render, and a red placed *on* the trunk rather
    /// than beside it ends the trunk's segment there, which is how a river came to fade out in
    /// the middle of a plain with its lower half drawn as a sourceless fragment.
    ///
    /// So the raster is built so that every one of those rules holds by construction, and the
    /// markers are a record of how each course was drawn rather than something read back off a
    /// neighbourhood afterwards:
    ///
    /// - A course is traced down the receivers, made orthogonal, then straightened so that no
    ///   pixel of it touches any pixel of its own except the one before and the one after. That
    ///   rules out the 2x2 blocks and the three-way pixels a corner laid against an earlier bend
    ///   used to make.
    /// - A course ends the moment one of its pixels lies orthogonally beside a river already
    ///   drawn. That pixel is its red; the trunk pixel it touches keeps its own colour and its
    ///   two trunk neighbours, with the red beside it not counting against the limit. Courses
    ///   therefore never run alongside one another, and the last step to the trunk is always
    ///   orthogonal because it is the adjacency itself that ends the course.
    /// - A course ending in water carries on a pixel or two into the water so the engine can
    ///   read its direction at the mouth, as the wiki advises — but only onto water no other
    ///   river is using.
    /// - A join is refused where the pixel it would touch is a marker or the end of a segment —
    ///   another course's red or green, the pixel after a green, a tributary's bare start or the
    ///   last pixel before its red — since a segment with a marker at both ends has no single
    ///   direction; and a trunk pixel is asked to take at most one tributary. The course is
    ///   dropped rather than drawn wrong, and the count is reported.
    ///
    /// Greens go on the heads of courses that reach water, reds on the recorded ends of those
    /// that join. A final audit re-derives the segments and counts anything that still breaks a
    /// rule — the same model ck3-tiger checks, minus a bug in its segment builder that splits
    /// chains at corner-shaped turns and reports the pieces as orphans — so a regression here
    /// shows up in the log rather than in the game.
    /// </summary>
    public static byte[] Generate(MapConfig cfg, ProvinceMap provinces, Drainage drainage)
    {
        int width = cfg.ProvinceWidth;
        int height = cfg.ProvinceHeight;
        int n = width * height;

        var indices = new byte[n];
        Array.Fill(indices, PaletteLand);

        // 1. Mark water from province partition (both open ocean and carved major rivers)
        var isWater = new bool[n];
        Parallel.For(0, n, i =>
        {
            if (!provinces.Seeds[provinces.Label[i]].IsLand)
            {
                indices[i] = PaletteWater;
                isWater[i] = true;
            }
        });

        if (!cfg.EnableMinorRivers)
            return indices;

        // Flow thresholds scaled to map resolution
        float minFlowThreshold = (float)Math.Max(350.0, cfg.Scaled(800.0) / Math.Max(0.1, cfg.RiverDensity));
        float maxFlowThreshold = (float)Math.Max(minFlowThreshold * 10f, cfg.Scaled(25000.0));
        int minLength = (int)Math.Max(10, cfg.Scaled(16.0));
        // A tributary may be shorter than a river that has to reach the sea on its own, but it
        // still needs a head, a body and its red — a two-pixel stub is a green touching a red.
        int minJoinLength = Math.Max(3, minLength / 2);

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
        var isMarker = new bool[n];      // greens and reds: nothing may join beside these
        var isEndpoint = new bool[n];    // first and last normal pixel of a segment: nor beside these
        var hasJoin = new bool[n];       // trunk pixels already taking a tributary
        var widthBand = new byte[n];

        var heads = new List<int>();
        var ends = new List<int>();
        int drawnRivers = 0, mouths = 0, joins = 0, refusedAtMarker = 0, refusedSecondJoin = 0;
        int tooShort = 0, stranded = 0;

        var course = new List<int>();
        var neighbours = new List<int>(4);

        foreach (int source in sourceCandidates)
        {
            if (isRiverPixel[source]) continue;

            // Follow the receivers until water or an already-drawn river; the straightened,
            // orthogonal course is then cut back to wherever it first touches that river.
            course.Clear();
            int curr = source;
            while (curr >= 0 && course.Count < 3000)
            {
                course.Add(curr);
                if (isWater[curr] || isRiverPixel[curr]) break;
                int into = drainage.Receiver[curr];
                if (into == curr) break;
                curr = into;
            }

            course = Straighten(MakeOrthogonal(course, width, height), width);

            // Walk the course forward and decide where it ends.
            int endIndex = -1;
            Ending ending = Ending.Mouth;
            bool refused = false;

            for (int k = 0; k < course.Count; k++)
            {
                int p = course[k];

                if (isWater[p] && !isRiverPixel[p])
                {
                    // The mouth. The shore pixel before it is on land; extend into the water by
                    // up to two pixels if no other river is already using them.
                    endIndex = k - 1;
                    for (int ext = k; ext < course.Count && ext < k + 2; ext++)
                    {
                        int w = course[ext];
                        if (!isWater[w] || isRiverPixel[w]) break;
                        if (TouchesRiver(w, ext > 0 ? course[ext - 1] : -1)) break;
                        endIndex = ext;
                    }
                    ending = Ending.Mouth;
                    break;
                }

                if (isRiverPixel[p])
                {
                    // Stepped onto a river without having touched it first: only possible at
                    // the head, since every later step is orthogonal. Nothing to draw.
                    refused = true;
                    break;
                }

                OrthoRiverNeighbours(p, k > 0 ? course[k - 1] : -1);
                if (neighbours.Count == 0) continue;

                // Beside a river: this pixel is the red, provided the neighbourhood is one the
                // engine can read.
                bool clean = true;
                foreach (int t in neighbours)
                {
                    if (isMarker[t] || isEndpoint[t]) { clean = false; refusedAtMarker++; break; }
                    if (hasJoin[t]) { clean = false; refusedSecondJoin++; break; }
                }
                if (!clean) { refused = true; break; }

                endIndex = k;
                ending = Ending.Join;
                break;
            }

            if (refused) continue;
            if (endIndex < 0) { stranded++; continue; }     // ran out on land, touching nothing

            int length = endIndex + 1;
            if (length < (ending == Ending.Join ? minJoinLength : minLength)) { tooShort++; continue; }

            // Paint the width band; the markers are laid at the end, once every course is in.
            // The band follows the running maximum of discharge down the course: flow only grows
            // downstream along the receivers, and the corner pixels inserted to keep the course
            // orthogonal sit off the drainage path with a flow of their own, which used to make
            // a staircase flicker wide, narrow, wide with every step.
            float f = 0f;
            for (int k = 0; k < length; k++)
            {
                int c = course[k];

                f = Math.Max(f, drainage.Flow[c]);
                double t = Math.Clamp(Math.Log(Math.Max(1f, f / minFlowThreshold)) /
                                      Math.Log(Math.Max(1.01f, maxFlowThreshold / minFlowThreshold)), 0, 1);
                int band = PaletteNarrow + (int)Math.Round(t * (PaletteWide - PaletteNarrow));

                widthBand[c] = (byte)Math.Clamp(band, PaletteNarrow, PaletteWide);
                indices[c] = widthBand[c];
                isRiverPixel[c] = true;
            }

            if (ending == Ending.Join)
            {
                // A tributary: bare start, red end. Its segment runs from course[0] to the pixel
                // before the red.
                int end = course[endIndex];
                ends.Add(end);
                isMarker[end] = true;
                isEndpoint[course[0]] = true;
                isEndpoint[course[endIndex - 1]] = true;
                OrthoRiverNeighbours(end, course[endIndex - 1]);
                foreach (int t in neighbours) hasJoin[t] = true;
                joins++;
            }
            else
            {
                // A river reaching water: green head. Its segment runs from the pixel after the
                // green to the last pixel in the water.
                heads.Add(course[0]);
                isMarker[course[0]] = true;
                isEndpoint[course[1]] = true;
                isEndpoint[course[endIndex]] = true;
                mouths++;
            }

            drawnRivers++;
        }

        // 3. Markers, from the record of how each course was drawn.
        foreach (int e in ends) indices[e] = PaletteJoin;

        int sources = 0;
        foreach (int h in heads)
        {
            int degree = 0, sole = -1;
            for (int k = 0; k < 4; k++)
            {
                int nx = h % width + OrthoDx[k], ny = h / width + OrthoDy[k];
                if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                int nb = ny * width + nx;
                if (!isRiverPixel[nb]) continue;
                degree++;
                sole = nb;
            }
            if (degree != 1 || indices[sole] < PaletteNarrow) continue;
            indices[h] = PaletteSource;
            sources++;
        }

        Console.WriteLine($"  minor rivers: {drawnRivers} streams in rivers.png — {mouths} reaching water ({sources} with a " +
                          $"green source), {joins} tributaries joining another river; skipped {tooShort} too short, " +
                          $"{stranded} stranded on land, {refusedAtMarker} landing beside a marker or segment end, " +
                          $"{refusedSecondJoin} landing on a pixel already taking a tributary");

        Audit(indices, width, height);
        return indices;

        // Orthogonal neighbours of p that are river pixels, other than the course's own previous
        // pixel. Into the shared list to avoid allocating per step.
        void OrthoRiverNeighbours(int p, int previous)
        {
            neighbours.Clear();
            int x = p % width, y = p / width;
            for (int k = 0; k < 4; k++)
            {
                int nx = x + OrthoDx[k], ny = y + OrthoDy[k];
                if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                int nb = ny * width + nx;
                if (nb == previous || !isRiverPixel[nb]) continue;
                neighbours.Add(nb);
            }
        }

        bool TouchesRiver(int p, int previous)
        {
            OrthoRiverNeighbours(p, previous);
            return neighbours.Count > 0;
        }
    }

    /// <summary>
    /// Counts what is left that breaks the engine's rules, so that a regression shows in the log.
    /// Tiger counts a pixel's neighbours among normal river pixels only, and so does this. Then
    /// the segments — the 4-connected runs of normal pixels — are rebuilt and each is asked for
    /// exactly one marker beside one of its two ends, with no marker terminating two segments.
    /// </summary>
    private static void Audit(byte[] indices, int width, int height)
    {
        static bool IsRiver(byte v) => v <= PaletteWide;
        static bool IsNormal(byte v) => v >= PaletteNarrow && v <= PaletteWide;
        static bool IsSpecial(byte v) => v <= PaletteSplit;

        int overConnected = 0, badJoins = 0, badSources = 0, blocks = 0;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                byte v = indices[i];
                if (!IsRiver(v)) continue;

                int normal = 0;
                if (x > 0 && IsNormal(indices[i - 1])) normal++;
                if (x + 1 < width && IsNormal(indices[i + 1])) normal++;
                if (y > 0 && IsNormal(indices[i - width])) normal++;
                if (y + 1 < height && IsNormal(indices[i + width])) normal++;

                if (IsNormal(v) && normal > 2) overConnected++;
                else if (v == PaletteJoin && normal < 2) badJoins++;
                else if (v == PaletteSource && normal != 1) badSources++;

                // A red in the inner corner of a bend touches two trunk pixels and fills a 2x2
                // with them; that is a legal join, not a river two pixels wide. Only a block of
                // four normal pixels is the shape the engine cannot trace.
                if (x + 1 < width && y + 1 < height && IsNormal(v) &&
                    IsNormal(indices[i + 1]) && IsNormal(indices[i + width]) && IsNormal(indices[i + width + 1]))
                    blocks++;
            }
        }

        // Segments: label each 4-connected run of normal pixels, collect its ends (pixels with
        // fewer than two normal neighbours), and count the markers beside those ends.
        int n = width * height;
        var segment = new int[n];
        Array.Fill(segment, -1);
        var terminators = new List<int>();           // per segment: markers beside its ends
        var terminates = new Dictionary<int, int>(); // marker -> segments it terminates
        var stack = new Stack<int>();

        for (int start = 0; start < n; start++)
        {
            if (!IsNormal(indices[start]) || segment[start] >= 0) continue;

            int id = terminators.Count;
            int count = 0;
            segment[start] = id;
            stack.Push(start);

            while (stack.Count > 0)
            {
                int i = stack.Pop();
                int x = i % width, y = i / width;
                int normal = 0;

                for (int k = 0; k < 4; k++)
                {
                    int nx = x + OrthoDx[k], ny = y + OrthoDy[k];
                    if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                    int nb = ny * width + nx;
                    if (!IsNormal(indices[nb])) continue;
                    normal++;
                    if (segment[nb] >= 0) continue;
                    segment[nb] = id;
                    stack.Push(nb);
                }

                // An end of the chain: count the markers beside it.
                if (normal >= 2) continue;
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + OrthoDx[k], ny = y + OrthoDy[k];
                    if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                    int nb = ny * width + nx;
                    if (!IsSpecial(indices[nb])) continue;
                    count++;
                    terminates[nb] = terminates.GetValueOrDefault(nb) + 1;
                }
            }

            terminators.Add(count);
        }

        int orphans = terminators.Count(t => t == 0);
        int doubly = terminators.Count(t => t > 1);
        int overloaded = terminates.Count(kv => kv.Value > 1);

        if (overConnected + badJoins + badSources + blocks + orphans + doubly + overloaded > 0)
            Console.WriteLine($"  minor rivers: audit found {overConnected} over-connected pixel(s), {badJoins} red(s) not " +
                              $"joining, {badSources} green(s) not at a source, {blocks} two-pixel-wide block(s); of " +
                              $"{terminators.Count} segments {orphans} have no marker, {doubly} have two, and " +
                              $"{overloaded} marker(s) terminate more than one segment");
        else
            Console.WriteLine($"  minor rivers: audit clean — {terminators.Count} segments, each with exactly one marker");
    }

    /// <summary>
    /// Removes every pixel of an orthogonal course that a later pixel could reach directly, so
    /// that no pixel touches any pixel of its own course except its two neighbours along it. A
    /// course that doubled back, or whose inserted corner landed beside a later step, would
    /// otherwise hold a 2x2 block or a pixel with three arms, and the engine would stop there.
    /// </summary>
    private static List<int> Straighten(List<int> path, int width)
    {
        if (path.Count < 3) return path;

        var position = new Dictionary<int, int>(path.Count);
        for (int i = 0; i < path.Count; i++) position[path[i]] = i;   // last occurrence wins

        var result = new List<int>(path.Count);
        int k = 0;
        while (k < path.Count)
        {
            int p = path[k];
            result.Add(p);

            // The furthest pixel along the course that is orthogonally beside this one.
            int jump = k + 1;
            int x = p % width, y = p / width;
            for (int d = 0; d < 4; d++)
            {
                int nx = x + OrthoDx[d], ny = y + OrthoDy[d];
                if (nx < 0 || nx >= width || ny < 0) continue;
                if (position.TryGetValue(ny * width + nx, out int j) && j > jump) jump = j;
            }
            k = jump;
        }

        return result;
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
