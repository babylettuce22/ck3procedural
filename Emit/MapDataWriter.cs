using Ck3MapGen.Config;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;
using Ck3MapGen.World;

namespace Ck3MapGen.Emit;

/// <summary>
/// Writes the map_data/ half of the mod. Every format here was verified byte-for-byte against
/// vanilla 1.19 rather than taken from documentation, because CK3 fails opaquely on all of it.
/// </summary>
public static class MapDataWriter
{
    /// <summary>
    /// CK3's water plane, from `WATERLEVEL = 3 ### 0.06 in 0-1, 19 in 0-255` in
    /// common/defines/00_defines.txt. The coastline must land exactly here or the rendered sea
    /// will not match the province map.
    /// </summary>
    public const int WaterLevel255 = 19;

    /// <summary>
    /// One whole 0-255 step, expressed in the 16-bit scale the heightmap is actually written in.
    /// 255 * 257 == 65535, so 257 raw units is exactly one unit of the documented 0-255 scale.
    /// </summary>
    public const int Step255 = 257;

    /// <summary>
    /// <see cref="WaterLevel255"/> in 16-bit terms — 19/255 of full scale, exactly.
    /// </summary>
    public const int WaterLevel16 = WaterLevel255 * Step255;

    /// <summary>
    /// Vanilla's rivers.png palette. Reproduced exactly; CK3 keys off the indices.
    ///
    /// Kept in full while no course is drawn. These sixteen entries are measured from vanilla
    /// rather than chosen, they are the format rather than the hydrology, and the file has to
    /// carry a 256-entry palette regardless of how many entries the image uses.
    /// </summary>
    private static readonly (byte R, byte G, byte B)[] RiverPaletteHead =
    [
        (0, 255, 0),     // 0  source
        (255, 0, 0),     // 1  join / tributary merge
        (255, 252, 0),   // 2  split
        (0, 225, 255),   // 3  narrowest
        (0, 200, 255),   // 4
        (0, 150, 255),   // 5
        (0, 100, 255),   // 6
        (0, 0, 255),     // 7
        (0, 0, 225),     // 8
        (0, 0, 200),     // 9
        (0, 0, 150),     // 10
        (0, 0, 100),     // 11 widest
        (0, 85, 0),      // 12
        (0, 125, 0),     // 13
        (0, 158, 0),     // 14
        (24, 206, 0),    // 15
    ];

    public const byte RiverIndexLand = 255;

    /// <summary>
    /// Every sea, ocean and lake pixel. Vanilla's rivers.png is not "white with rivers on it":
    /// it is white *land* over 23.4M pixels and this magenta over 18.8M, which between them
    /// account for the whole map. Filling water with white claims the oceans are dry land.
    /// </summary>
    public const byte RiverIndexWater = 254;

    /// <summary>
    /// Writes map_data from an ordering already decided by <see cref="BuildProvinceOrder"/>.
    ///
    /// The ordering is passed in rather than derived here because everything downstream — the title
    /// hierarchy, the cultures, the GUI's county preview — is expressed in province *ids*, and those
    /// only exist once the ordering does. Deriving it inside the writer made the ids a side effect
    /// of writing files, so nothing could name a province without writing a mod first.
    /// </summary>
    public static void WriteAll(string modDir, MapConfig cfg, ProvinceMap provinces,
        int[] order, int baronyCount, int landCount, bool writePacked, MapGen.TerrainData terra)
    {
        string dir = Path.Combine(modDir, "map_data");
        Directory.CreateDirectory(dir);

        WriteProvincesPng(Path.Combine(dir, "provinces.png"), provinces, order);
        WriteDefinitionCsv(Path.Combine(dir, "definition.csv"), provinces, order);
        WriteRiversPng(Path.Combine(dir, "rivers.png"), cfg, provinces);
        WriteHeightmap(dir, cfg, writePacked, provinces, order, landCount, terra);
        WriteDefaultMap(Path.Combine(dir, "default.map"), provinces.Count, baronyCount, landCount);
        WriteStubs(dir);

        AssertNoEmptyFiles(dir);

        Console.WriteLine($"  map_data written: {baronyCount} baronied + " +
                          $"{landCount - baronyCount} impassable land, " +
                          $"{provinces.Count - landCount} sea zones");
    }

    /// <summary>
    /// Fails the build on any zero-byte file in map_data.
    ///
    /// An empty file that default.map references does not produce an error, a warning or a
    /// crash — CK3 loops on end-of-file forever, burning CPU with no bytes transferred and no
    /// log output, and the whole load stops. It cost a full session to find one such file, so
    /// the check is cheap insurance rather than a nicety.
    /// </summary>
    private static void AssertNoEmptyFiles(string dir)
    {
        var empty = Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
                             .Where(f => new FileInfo(f).Length == 0)
                             .Select(Path.GetFileName)
                             .ToList();

        if (empty.Count == 0) return;

        throw new InvalidOperationException(
            $"map_data contains {empty.Count} empty file(s): {string.Join(", ", empty)}. " +
            "CK3 spins forever reading a zero-byte map_data file.");
    }

    /// <summary>
    /// Label -> province id (1-based), in three contiguous groups: baronied land, then impassable
    /// land, then sea.
    ///
    /// Impassable provinces sit *inside* the land range on purpose. Every downstream test is
    /// `id &lt;= landCount` meaning "is land" — the coastline snap, the terrain mask, the locators,
    /// the water graphics — and an impassable mountain is land. Only the title hierarchy needs the
    /// narrower number, and it gets baronyCount. default.map then needs one RANGE per group, which
    /// is why they have to be contiguous.
    /// </summary>
    public static int[] BuildProvinceOrder(ProvinceMap provinces, out int baronyCount,
        out int landCount)
    {
        var order = new int[provinces.Count];
        int next = 1;

        for (int i = 0; i < provinces.Count; i++)
            if (provinces.Seeds[i].IsLand && !provinces.Seeds[i].IsImpassable) order[i] = next++;
        baronyCount = next - 1;

        for (int i = 0; i < provinces.Count; i++)
            if (provinces.Seeds[i].IsLand && provinces.Seeds[i].IsImpassable) order[i] = next++;
        landCount = next - 1;

        for (int i = 0; i < provinces.Count; i++)
            if (!provinces.Seeds[i].IsLand) order[i] = next++;

        return order;
    }

    /// <summary>
    /// A bijective index -> 24-bit colour map. Random colours collide: 13k provinces in a 24-bit
    /// space collide about six times by the birthday paradox, and CK3 silently merges the
    /// affected regions. Multiplying by an odd constant mod 2^24 cannot collide.
    /// </summary>
    public static (byte R, byte G, byte B) ProvinceColor(int provinceId)
    {
        uint v = (uint)provinceId * 2654435761u % 0x1000000u;
        if (v == 0) v = 0x1000000u - 1; // keep 0,0,0 reserved for the definition.csv header
        return ((byte)(v >> 16), (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF));
    }

    private static void WriteProvincesPng(string path, ProvinceMap provinces, int[] order)
    {
        var rgb = new byte[provinces.Label.Length * 3];
        Parallel.For(0, provinces.Label.Length, i =>
        {
            var (r, g, b) = ProvinceColor(order[provinces.Label[i]]);
            rgb[i * 3] = r;
            rgb[i * 3 + 1] = g;
            rgb[i * 3 + 2] = b;
        });
        PngWriter.WriteRgb8(path, provinces.Width, provinces.Height, rgb);
    }

    /// <summary>Format: `id;r;g;b;NAME;x;`, with a `0;0;0;0;x;x;` header row. No BOM.</summary>
    private static void WriteDefinitionCsv(string path, ProvinceMap provinces, int[] order)
    {
        var byId = new int[provinces.Count + 1];
        for (int label = 0; label < provinces.Count; label++) byId[order[label]] = label;

        var sb = new System.Text.StringBuilder();
        sb.Append("0;0;0;0;x;x;\n");
        for (int id = 1; id <= provinces.Count; id++)
        {
            var (r, g, b) = ProvinceColor(id);
            string name = provinces.Seeds[byId[id]].IsLand ? $"prov_{id}" : $"sea_{id}";
            sb.Append($"{id};{r};{g};{b};{name};x;\n");
        }
        ParadoxText.WriteNoBom(path, sb.ToString());
    }

    /// <summary>
    /// rivers.png as palette indices, at province resolution: index 255 (white) on land, 254
    /// (magenta) on water.
    ///
    /// No courses. A course generator was written against <see cref="MapGen.Drainage"/> on
    /// 2026-08-11 and removed the same day, so what this emits is a valid, riverless rivers.png —
    /// which is what it emitted between the hydrology's removal on 2026-08-10 and that attempt.
    /// The file is **not optional**: default.map names it, CK3 will not load without it, and a map
    /// with no rivers drawn on it is a legal map. This is the correct interim output rather than a
    /// stub, and commenting it out silently costs the mod its ability to load.
    ///
    /// Public because the GUI previews this file, and a preview built from its own reading would be
    /// a second opinion on what ships rather than a view of it.
    /// </summary>
    public static byte[] RiverIndices(MapConfig cfg, ProvinceMap provinces)
    {
        var indices = new byte[cfg.ProvinceWidth * cfg.ProvinceHeight];
        Array.Fill(indices, RiverIndexLand);

        // Water comes from the province partition rather than from a fresh elevation threshold,
        // so rivers.png agrees with provinces.png pixel for pixel by construction.
        Parallel.For(0, provinces.Label.Length, i =>
        {
            if (!provinces.Seeds[provinces.Label[i]].IsLand) indices[i] = RiverIndexWater;
        });

        return indices;
    }

    /// <summary>The colour a rivers.png index renders as — vanilla's palette for the course indices,
    /// magenta for water and white for land. Shared with the GUI preview.</summary>
    public static (byte R, byte G, byte B) RiverColour(byte index)
        => index < RiverPaletteHead.Length ? RiverPaletteHead[index]
            : index == RiverIndexWater ? ((byte)255, (byte)0, (byte)128)
            : index == RiverIndexLand ? ((byte)255, (byte)255, (byte)255)
            : ((byte)2, (byte)0, (byte)1);

    private static void WriteRiversPng(string path, MapConfig cfg, ProvinceMap provinces)
    {
        var indices = RiverIndices(cfg, provinces);

        var palette = new byte[256 * 3];
        for (int i = 0; i < 256; i++)
        {
            var (r, g, b) = RiverColour((byte)i);
            palette[i * 3] = r;
            palette[i * 3 + 1] = g;
            palette[i * 3 + 2] = b;
        }

        PngWriter.WriteIndexed8(path, cfg.ProvinceWidth, cfg.ProvinceHeight, indices, palette);
    }

    /// <summary>
    /// Drags the heightmap's coastline onto the province map's coastline, exactly.
    ///
    /// CK3 draws water wherever the heightmap is at or below <see cref="WaterLevel255"/>, but it
    /// draws borders and decides what a province *is* from provinces.png. We derived those two
    /// from the same elevation field and then changed only one of them: DissolveTinyRegions
    /// flips land and water blobs under the minimum size (198 islands drowned on seed 1) and
    /// ForceOceanBorder drowns the map edge. Both edit the province partition and neither
    /// touches the heightmap.
    ///
    /// The result in game is the two artefacts that look like imprecise borders: a sea province
    /// standing above water and rendering as solid ground, and a sandy shore running past the
    /// county boundary into open sea. Clamping each heightmap pixel to the side of sea level its
    /// province is on removes the disagreement at the source.
    ///
    /// The heightmap is twice the province map's resolution, so four heightmap pixels map to one
    /// province pixel.
    /// </summary>
    private static void ForceCoastlineToMatchProvinces(ushort[] height, MapConfig cfg,
        ProvinceMap provinces, int[] order, int landCount)
    {
        int pw = provinces.Width, ph = provinces.Height;
        int scaleX = cfg.Width / pw, scaleY = cfg.Height / ph;
        long changed = 0;

        Parallel.For(0, cfg.Height, () => 0L, (y, _, local) =>
        {
            int py = Math.Min(y / scaleY, ph - 1);
            for (int x = 0; x < cfg.Width; x++)
            {
                int px = Math.Min(x / scaleX, pw - 1);
                bool isLand = order[provinces.Label[py * pw + px]] <= landCount;

                long i = (long)y * cfg.Width + x;
                ushort v = height[i];

                // Reflected across the water plane rather than clamped to a constant, and this is
                // the whole reason the coastline used to read as a staircase in game.
                //
                // Clamping put every disagreeing pixel on one of two fixed values — measured at
                // 2,794 pixels sitting on exactly one such constant, the second commonest value in
                // the entire heightmap after open ocean. Because provinces.png is half the
                // heightmap's resolution, those pixels arrive in 2x2 blocks, so the result was flat
                // square plateaus separated from the land beside them by a cliff of nearly 3,000
                // raw units. Reflecting instead keeps the pixel's own relief: one unit on the wrong
                // side of the plane comes back one unit on the right side, so it still meets its
                // neighbours, and a drowned island becomes a submerged mound rather than a plate.
                //
                // The extra whole 0-255 step is the minimum crossing: it guarantees the pixel is on
                // the correct side after CK3 quantises to 8 bits, which a single raw unit does not.
                if (isLand)
                {
                    if (v > WaterLevel16) continue;
                    height[i] = (ushort)Math.Min(65535, WaterLevel16 + Step255 + (WaterLevel16 - v));
                    local++;
                    continue;
                }

                if (v <= WaterLevel16) continue;
                height[i] = (ushort)Math.Max(0, WaterLevel16 - Step255 - (v - WaterLevel16));
                local++;
            }
            return local;
        }, local => Interlocked.Add(ref changed, local));

        double pct = 100.0 * changed / height.Length;
        Console.WriteLine($"  coastline: {changed:N0} heightmap pixels snapped to the province " +
                          $"land/water split ({pct:F2}%)");
    }

    /// <summary>
    /// How far, in *vanilla heightmap* pixels, the near-shore seabed is graded away from the coast,
    /// and how far the height field is smoothed either side of the coastline.
    ///
    /// Both measured off vanilla's own heightmap. Its seabed falls from a mean of 18.89/255 one
    /// pixel offshore to 15.05 nine pixels out — very close to linear at -0.45 a pixel — and then
    /// continues into the abyss. Ours arrived flat: 18.85 at one pixel and 18.05 at nine, with a
    /// median of exactly 19 at every distance, i.e. a plate of water sitting precisely on the value
    /// CK3 tests against, then a cliff to black.
    /// </summary>
    private const int SeabedGradeReach = 9;

    private const double SeabedGradePer255 = 0.45;
    private const int CoastSmoothReach = 3;

    /// <summary>
    /// How far offshore, in vanilla heightmap pixels, a *synthesised* shelf runs before it reaches
    /// the abyss — vanilla's own slope carried all the way down from the plane rather than stopped
    /// at <see cref="SeabedGradeReach"/>.
    ///
    /// Checked against vanilla at matched world distance, and it is close enough to be worth
    /// quoting: measured 17.0 / 15.1 / 12.9 / 10.8 / 8.7 at 4.5 / 9 / 13.5 / 18 / 22.5 px offshore,
    /// synthesised 16.96 / 14.78 / 12.67 / 10.56 / 8.44. Vanilla's real profile flattens out past
    /// about 25 px where this stays linear to zero, which is the one place the two part company and
    /// is well beyond anything a coastline cliff is visible at.
    /// </summary>
    private static readonly double SeabedSynthesisReach = WaterLevel255 / SeabedGradePer255;

    /// <summary>
    /// The share of near-shore water the existing grade must admit before its seabed is believed.
    ///
    /// Three measurements, all taken by this pass on real files. Vanilla's own heightmap admits
    /// 19.13%. The playtest map that prompted this admits 0.50%. This program's own output, fed
    /// back in after its shelf has been synthesised, admits 11.14% — and that third number is why
    /// the threshold is 5% rather than the 10% it started at. A synthesised shelf descends at
    /// vanilla's slope, so only the first pixel or so of it survives a gate that asks for water
    /// within one step of the plane, and a threshold that our own well-formed output only just
    /// clears is one bad rounding away from re-synthesising over its own work.
    ///
    /// The obvious test — "is nearly all water one value" — does not work. Vanilla holds 85.08% of
    /// its water at exactly 0 against the playtest map's 99.74%, which is not a gap anything can be
    /// hung on. Asking the gate itself is both sharper and exactly the right question, because what
    /// is being decided is whether the gate has anything left to do.
    /// </summary>
    private const double SeabedAuthoredShare = 0.05;

    /// <summary>
    /// Chamfer distance from every pixel to the nearest pixel on the other side of the water plane,
    /// in whole pixels, capped at <paramref name="cap"/>.
    ///
    /// Two sequential scans rather than one dilation pass per unit of reach: at 170 million pixels
    /// even a nine-pixel reach would be some ten billion neighbour tests. Orthogonal steps cost 3
    /// and diagonal 4, the usual 3-4 chamfer, which is divided back out at the end.
    /// </summary>
    private static byte[] CoastDistance(ushort[] full, int width, int height, int cap)
    {
        const int Orthogonal = 3, Diagonal = 4;
        int capUnits = (cap + 1) * Orthogonal;

        var distance = new ushort[full.Length];

        // Seed: every pixel with a neighbour on the other side of the plane is at distance zero.
        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                long i = (long)y * width + x;
                bool land = full[i] > WaterLevel16;
                distance[i] = (ushort)capUnits;

                for (int dy = -1; dy <= 1; dy++)
                {
                    int yy = y + dy;
                    if (yy < 0 || yy >= height) continue;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int xx = ((x + dx) % width + width) % width;
                        if (full[(long)yy * width + xx] > WaterLevel16 != land)
                        {
                            distance[i] = 0;
                            dy = 2;
                            break;
                        }
                    }
                }
            }
        });

        // Forward scan, then backward. Sequential by nature — each pass depends on the one before.
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                Relax(y, x, -1, 0, Orthogonal);
                Relax(y, x, -1, -1, Diagonal);
                Relax(y, x, 0, -1, Orthogonal);
                Relax(y, x, 1, -1, Diagonal);
            }

        for (int y = height - 1; y >= 0; y--)
            for (int x = width - 1; x >= 0; x--)
            {
                Relax(y, x, 1, 0, Orthogonal);
                Relax(y, x, 1, 1, Diagonal);
                Relax(y, x, 0, 1, Orthogonal);
                Relax(y, x, -1, 1, Diagonal);
            }

        var result = new byte[full.Length];
        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                long i = (long)y * width + x;
                result[i] = (byte)Math.Min(cap + 1, distance[i] / Orthogonal);
            }
        });

        return result;

        void Relax(int y, int x, int dx, int dy, int cost)
        {
            long target = (long)y * width + x;
            if (distance[target] == 0) return;

            int yy = y + dy;
            if (yy < 0 || yy >= height) return;
            int xx = ((x + dx) % width + width) % width;

            int candidate = distance[(long)yy * width + xx] + cost;
            if (candidate < distance[target]) distance[target] = (ushort)candidate;
        }
    }

    /// <summary>
    /// Grades the near-shore seabed away from the coast, and smooths the height field either side
    /// of it.
    ///
    /// Two artefacts, both measured against vanilla, both local to the coast — nothing here reshapes
    /// the map the way <see cref="ElevationTo16"/> deliberately no longer does. Inland and in open
    /// ocean the author's terrain is untouched.
    ///
    /// **The shelf.** 9.77% of every pixel we shipped sat on exactly <see cref="WaterLevel255"/>
    /// against vanilla's 0.90% — thirteen million pixels of dead-flat water resting on the precise
    /// value CK3 tests to decide what is sea, then dropping to black. It is graded here to vanilla's
    /// own profile. Only ever *downward*: an author who drew a real seabed keeps it, because the
    /// grade is a floor the existing depth is taken the minimum against, not a replacement for it.
    ///
    /// **The missing shelf.** That grade admits only water within one step of the plane, and on a
    /// source whose ocean is pure black it therefore admits nothing — measured at 0.50% of
    /// near-shore water on the map that prompted this, against 19.13% on vanilla's own heightmap.
    /// The pass ran and did nothing, and the map shipped with a full-height wall from the water
    /// plane to the seabed at every coastline. When <see cref="HasAuthoredSeabed"/> finds there is
    /// no seabed to preserve, the shelf is synthesised instead of graded: same profile, run all the
    /// way down to the abyss, and taken as a *ceiling* rather than a floor so it can only fill the
    /// plate in and never cut into anything drawn above it.
    ///
    /// **The steps.** <see cref="ForceCoastlineToMatchProvinces"/> reflects disagreeing pixels
    /// across the plane in 2x2 blocks, because provinces.png is half the heightmap's resolution.
    /// That removed the cliff the old clamp produced but left the height field stepped along the
    /// shore. Smoothing is *side-restricted* — a land pixel averages over land neighbours only and a
    /// water pixel over water — so the land/water split itself is untouched and the exact agreement
    /// with provinces.png that the snap exists to guarantee still holds. Blurring across the split
    /// instead would pull both sides toward the plane and rebuild the very plate being removed here.
    /// </summary>
    private static void ShapeCoastline(ushort[] full, MapConfig cfg)
    {
        int width = cfg.Width, height = cfg.Height;
        int grade = Math.Max(1, (int)Math.Round(cfg.Scaled(SeabedGradeReach)));
        int synthesis = Math.Max(1, (int)Math.Round(cfg.Scaled(SeabedSynthesisReach)));
        // EXPERIMENT — coastline smoothing is disabled. Restore by swapping these two lines back.
        // int smooth = Math.Max(1, (int)Math.Round(cfg.Scaled(CoastSmoothReach)));
        int smooth = 0;

        var distance = CoastDistance(full, width, height,
            Math.Max(Math.Max(grade, synthesis), smooth));

        bool authored = HasAuthoredSeabed(full, distance, grade, width, height);
        int reach = authored ? grade : synthesis;

        // The profile as a total drop across the whole reach, rather than a slope per pixel.
        //
        // Both halves have to scale with the map and only the reach used to. SeabedGradePer255 is
        // measured in vanilla heightmap pixels, so applying it to a distance already scaled into
        // this map's pixels graded a small map by the same *depth* over a much shorter run: on a
        // 4096-wide map the reach scales to 2 px and the old form dropped 0.90/255 across it where
        // the world-equivalent is the full 4.05.
        double drop = authored ? SeabedGradeReach * SeabedGradePer255 * Step255 : WaterLevel16;

        // The shelf, first: the smoothing pass below should see the graded seabed, not the plate.
        long deepened = 0;
        Parallel.For(0, height, () => 0L, (y, _, local) =>
        {
            for (int x = 0; x < width; x++)
            {
                long i = (long)y * width + x;
                if (full[i] > WaterLevel16) continue;

                // The ring touching land is distance 0, and it is graded like any other. It used to
                // be skipped, which cost nothing while the only profile started at the plane — the
                // target there is the plane itself, so the pass was a no-op on it either way. A
                // synthesised shelf has to fill that ring, because it is the one pixel of water the
                // coastline is actually drawn against.
                int d = distance[i];
                if (d > reach) continue;

                // Only the plate is graded — water lying within one 8-bit step of the plane, which
                // is the artefact and nothing else. A seabed that already descends is left exactly
                // as its author drew it. Without this gate the floor below is taken against a
                // distribution much like its own target and so biases the whole shelf deeper:
                // measured on a well-formed source, an ungated grade pushed the seabed nine pixels
                // out from 14.00 to 13.48 where vanilla sits at 14.82, i.e. further from vanilla
                // than doing nothing.
                if (authored && full[i] < WaterLevel16 - Step255) continue;

                // Vanilla's own fall-off, in 16-bit units, floored at the abyss.
                var target = (ushort)Math.Max(0, WaterLevel16 - (int)Math.Round(drop * d / reach));

                // Which way the shelf may move the water is the whole difference between the two
                // cases, and each direction is the conservative one for its own source. Grading an
                // authored seabed may only *deepen*, so relief somebody drew is never filled in.
                // Synthesising one may only *shallow*, so it fills the flat plate in front of the
                // coast and still leaves any bathymetry above the shelf line exactly as it was.
                if (authored ? target >= full[i] : target <= full[i]) continue;
                full[i] = target;
                local++;
            }
            return local;
        }, local => Interlocked.Add(ref deepened, local));

        // Side-restricted 3x3 mean over the coastal band, read from a snapshot so the pass is not
        // fed its own output.
        var source = (ushort[])full.Clone();
        long smoothed = 0;

        Parallel.For(0, height, () => 0L, (y, _, local) =>
        {
            for (int x = 0; x < width; x++)
            {
                long i = (long)y * width + x;

                // Distance 0 is the shoreline ring itself, and skipping it left this pass unable to
                // touch the pixels it was written for: ForceCoastlineToMatchProvinces reflects
                // disagreeing pixels in 2x2 blocks, and every one of those blocks lies against the
                // split, which is to say at distance 0. Measured on the playtest map, including it
                // takes the land ring from 27.3/255 to 26.2 against a hinterland of 25.5 — a lip
                // above the shore becoming a continuation of it — and the water ring from 19.0 to
                // 18.2, where vanilla's own sits at 18.89.
                if (smooth <= 0 || distance[i] > smooth) continue;

                bool land = source[i] > WaterLevel16;
                long sum = 0;
                int n = 0;

                for (int dy = -1; dy <= 1; dy++)
                {
                    int yy = y + dy;
                    if (yy < 0 || yy >= height) continue;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int xx = ((x + dx) % width + width) % width;
                        ushort v = source[(long)yy * width + xx];
                        if (v > WaterLevel16 != land) continue;
                        sum += v;
                        n++;
                    }
                }

                if (n == 0) continue;
                var mean = (ushort)(sum / n);

                // The averaging cannot move a pixel across the plane — the whole point of the snap
                // is that this side is already correct — so it is clamped back if it would.
                full[i] = land
                    ? Math.Max(mean, (ushort)(WaterLevel16 + Step255))
                    : Math.Min(mean, (ushort)WaterLevel16);
                local++;
            }
            return local;
        }, local => Interlocked.Add(ref smoothed, local));

        Console.WriteLine($"  coastline shaping: {deepened:N0} seabed pixels " +
                          $"{(authored ? "graded" : "synthesised")} over {reach} px, " +
                          $"{smoothed:N0} smoothed within {smooth} px of the shore");
    }

    /// <summary>
    /// Whether the source drew a seabed at all, or only a coastline with black behind it.
    ///
    /// The question is asked of <see cref="ShapeCoastline"/>'s own gate, by measuring what share of
    /// near-shore water that gate admits. That is not a proxy for the thing being decided, it *is*
    /// the thing: the gate exists to protect an authored seabed from being flattened, so a gate
    /// that admits almost nothing is one with nothing left to protect, and the pass it guards is a
    /// measured no-op rather than a conservative one.
    ///
    /// This matters well beyond one playtest. The common authoring convention for a CK3 heightmap
    /// is sea level at 19/255 and open water at pure black, with nothing in between, and every map
    /// drawn that way arrives with the same ~19-step vertical drop from the water plane to the
    /// seabed at every shoreline — the underwater half of the cliffs, which normalisation provably
    /// cannot reach because it happens entirely below the plane.
    ///
    /// Erring toward <c>true</c> is the safe direction: it means the shelf is left alone.
    /// </summary>
    private static bool HasAuthoredSeabed(ushort[] full, byte[] distance, int grade,
        int width, int height)
    {
        (long Near, long Admitted) totals = (0, 0);

        Parallel.For(0, height, () => (Near: 0L, Admitted: 0L), (y, _, local) =>
        {
            for (int x = 0; x < width; x++)
            {
                long i = (long)y * width + x;
                if (full[i] > WaterLevel16) continue;

                // The same band the grade will act on, distance 0 included, so this measures the
                // gate's hit rate over exactly the pixels the gate is asked about.
                if (distance[i] > grade) continue;

                local.Near++;
                if (full[i] >= WaterLevel16 - Step255) local.Admitted++;
            }
            return local;
        }, local =>
        {
            Interlocked.Add(ref totals.Near, local.Near);
            Interlocked.Add(ref totals.Admitted, local.Admitted);
        });

        // No shoreline to shape — an all-land or all-water map. Nothing to invent, and nothing that
        // would be visible if it were invented.
        if (totals.Near == 0) return true;

        double share = (double)totals.Admitted / totals.Near;
        bool authored = share >= SeabedAuthoredShare;

        Console.WriteLine($"  seabed: {share * 100:F2}% of the {totals.Near:N0} px of near-shore " +
                          $"water lies within one step of the plane (vanilla 19.13%) — " +
                          $"{(authored ? "authored, graded only where it is flat" : "none drawn, synthesising vanilla's shelf")}");

        return authored;
    }

    /// <summary>
    /// The float elevation field back to CK3's 16-bit scale — the exact inverse of
    /// <see cref="MapGen.HeightmapSource"/>'s read, and nothing else.
    ///
    /// This used to reshape what it converted. Land was ranked and remapped onto vanilla's measured
    /// hypsometric curve, and the sea floor was re-derived from a shelf profile keyed on depth,
    /// both because terrain used to be *generated* here and a simulation has no reason to produce a
    /// realistic height distribution on its own. Neither is defensible now that the heightmap is
    /// authored elsewhere and handed to us finished: reshaping it means the map that loads is not
    /// the map its author drew, and the sea floor in particular was thrown away wholesale —
    /// measured at 56% of the map forced to pure black regardless of what came in.
    ///
    /// So both are gone, along with the two measured curves, the rank machinery and the shelf
    /// settings. The conversion is now piecewise-linear about the water plane against the same two
    /// constants the read used, which makes the round trip exact: a pixel that came in at some
    /// 16-bit value leaves at that value.
    ///
    /// It still writes 16 bits rather than 8. That distinction predates the reshaping and is
    /// separate from it: quantising to 256 levels and re-expanding gave our heightmap 253 distinct
    /// values where vanilla's has 31,516, which read in game as terracing on every slope.
    /// </summary>
    private static ushort[] ElevationTo16(float[] elevation, MapConfig cfg)
    {
        float sea = cfg.Limits.SeaLevelUpper;
        float floor = cfg.SeaFloorElevation;
        float peak = cfg.PeakElevation;

        float belowRange = Math.Max(1e-3f, sea - floor);
        float aboveRange = Math.Max(1e-3f, peak - sea - 1f);

        var result = new ushort[elevation.Length];

        Parallel.For(0, elevation.Length, i =>
        {
            float e = elevation[i];
            double v = e <= sea
                ? (e - floor) / belowRange * WaterLevel16
                : WaterLevel16 + (e - sea - 1f) / aboveRange * (65535.0 - WaterLevel16);

            result[i] = (ushort)Math.Clamp(v, 0, 65535);
        });

        return result;
    }

    /// <summary>
    /// Prints the emitted heightmap's distribution next to vanilla's, as information rather than
    /// as a target.
    ///
    /// It used to be a regression check, back when land was actively reshaped onto vanilla's curve
    /// and a drift here meant the remap had broken. Nothing is reshaped now, so these numbers are
    /// simply the *input* heightmap's own distribution surviving the round trip — which makes them
    /// a reading on whatever drew the heightmap, not on this program. Vanilla's figures are kept
    /// alongside because they are still the most useful thing to compare a hand-made map against:
    /// 40.14% of the map exactly 0, 47.18% at or below the water level, land p50 36/255 and a
    /// highest pixel at 191/255 rather than 255.
    /// </summary>
    private static void ReportHypsometry(ushort[] height)
        => Console.WriteLine($"  heightmap as shipped: {MapGen.Hypsometry.Measure(height).Describe()}");

    /// <summary>
    /// What the packer decided, against vanilla's own figures for the same three files.
    ///
    /// The level shares are the check worth reading: they are a budget the packer spends by
    /// ranking tiles, so they should land on vanilla's by construction — if they do not, the
    /// detail metric has collapsed and every tile is tied. The atlas size is the check worth
    /// reading second, because the whole reason for the multi-level layout is that an all-level-0
    /// atlas of a vanilla-sized map is 175.7M pixels and four pixels under the D3D11 limit.
    /// </summary>
    private static void ReportPacking(HeightmapPacker.Result packing)
    {
        long tiles = packing.TilesPerLevel.Sum();
        long slots = packing.SlotsPerLevel.Sum();

        string shares = string.Join(" / ",
            packing.TilesPerLevel.Select(n => $"{100.0 * n / Math.Max(1, tiles):F2}"));

        Console.WriteLine($"  heightmap packed: {packing.TilesX}x{packing.TilesY} tiles into " +
                          $"{packing.PackedWidth}x{packing.PackedHeight} " +
                          $"({packing.PackedWidth * (long)packing.PackedHeight / 1_000_000.0:F1}M px, " +
                          $"vanilla 3185x4061 = 12.9M)");

        Console.WriteLine($"  level shares: {shares} % " +
                          $"(vanilla 2.56 / 11.93 / 14.71 / 11.67 / 59.13)");

        Console.WriteLine($"  {tiles:N0} tiles share {slots:N0} atlas slots " +
                          $"({(double)tiles / Math.Max(1, slots):F2}x reuse, vanilla 1.53x); " +
                          $"empty tile at {packing.EmptyR},{packing.EmptyG}");
    }


    /// <summary>
    /// The height field exactly as heightmap.png will carry it: on CK3's 16-bit scale, reconciled
    /// with provinces.png, and coast-shaped.
    ///
    /// Split out of <see cref="WriteHeightmap"/> so the GUI can show the file itself rather than a
    /// second opinion on it. Everything that happens between the elevation field and the PNG — the
    /// scale conversion, the coastline snap, the seabed grade and the smoothing over it — was
    /// otherwise visible only by writing the mod and loading it, which is a slow way to discover
    /// that a map is a pancake.
    /// </summary>
    public static ushort[] ShippedHeightmap(MapConfig cfg, ProvinceMap provinces, int[] order,
        int landCount, MapGen.TerrainData terra)
    {
        // Always the field the terrain was generated at. There is no upsample-and-embellish path
        // any more: the heightmap is what the erosion produced, at the resolution it produced it.
        var full = ElevationTo16(terra.Elevation, cfg);

        ForceCoastlineToMatchProvinces(full, cfg, provinces, order, landCount);

        // MUST follow the snap: it derives the land/water split from the heightmap itself, which is
        // only the split CK3 will actually render once the snap has reconciled it with provinces.png.
        ShapeCoastline(full, cfg);

        return full;
    }

    /// <summary>
    /// Emits heightmap.png, and optionally the packed/indirection pair CK3 renders from.
    ///
    /// The pair is built by <see cref="HeightmapPacker"/>, which reproduces vanilla's multi-level
    /// layout rather than the flat all-level-0 one that used to be here. See that class for the
    /// measurements each rule rests on.
    ///
    /// Both the CK3 wiki and ck2rpg's tutorial state the pair is generated by the map editor's
    /// repack, and an earlier session's conclusion that repack is avoidable rested only on the
    /// weaker observation that a hand-built pair produced no *heightmap errors in the log* — which
    /// is not the same as the terrain renderer accepting it. That caveat still stands: the packer
    /// matches vanilla's format on every rule that could be measured offline, which is not yet the
    /// same as having been loaded. Pass writePacked:false to ship a bare heightmap.png and repack
    /// in -mapeditor instead.
    /// </summary>
    private static void WriteHeightmap(string dir, MapConfig cfg, bool writePacked,
        ProvinceMap provinces, int[] order, int landCount, MapGen.TerrainData terra)
    {
        var full = ShippedHeightmap(cfg, provinces, order, landCount, terra);

        ReportHypsometry(full);
        PngWriter.WriteGray16(Path.Combine(dir, "heightmap.png"), cfg.Width, cfg.Height, full);

        // Both the CK3 wiki and ck2rpg's tutorial say these two files are produced by the map
        // editor's "repack" button, not by hand: "These will be created by the CK3 map editor
        // when a heightmap is repacked and saved." Skipping them lets us hand the editor a bare
        // heightmap.png and let it build the pair itself, which is the documented workflow.
        if (!writePacked)
        {
            // PRESERVE, never delete. An editor repack is still the reference copy while the
            // packer here is unproven against one, and regenerating must not clobber it —
            // heightmap.heightmap included, since it carries the matching level_offsets and
            // empty_tile_offset.
            bool have = File.Exists(Path.Combine(dir, "packed_heightmap.png"))
                     && File.Exists(Path.Combine(dir, "indirection_heightmap.png"))
                     && File.Exists(Path.Combine(dir, "heightmap.heightmap"));

            Console.WriteLine(have
                ? "  heightmap: kept existing packed/indirection (repacked in -mapeditor)"
                : "  heightmap: no packed/indirection present — open in -mapeditor and repack");
            return;
        }

        var packing = HeightmapPacker.Pack(full, cfg.Width, cfg.Height);

        PngWriter.WriteGray16(Path.Combine(dir, "packed_heightmap.png"),
            packing.PackedWidth, packing.PackedHeight, packing.Packed);
        PngWriter.WriteRgba8(Path.Combine(dir, "indirection_heightmap.png"),
            packing.TilesX, packing.TilesY, packing.Indirection);

        ReportPacking(packing);

        // level_offsets is the bottom-up distance from the foot of the atlas to the foot of each
        // level's region, and is part of the address rather than metadata about it — measured on
        // 2026-08-11 by addressing vanilla's atlas with and without it: 0.009-0.014 of 255 mean
        // error including it, 25-47 omitting it. The leading component of each pair is always 0 in
        // vanilla's own file and appears to be unused.
        string levelOffsets = string.Join(" ", packing.LevelOffsets.Select(n => $"{{ 0 {n} }}"));

        ParadoxText.WriteNoBom(Path.Combine(dir, "heightmap.heightmap"),
            $$"""
              heightmap_file="map_data/packed_heightmap.png"
              indirection_file="map_data/indirection_heightmap.png"
              original_heightmap_size={ {{cfg.Width}} {{cfg.Height}} }
              tile_size={{HeightmapPacker.TileSize(0)}}
              should_wrap_x=no
              level_offsets={ {{levelOffsets}} }
              max_compress_level={{HeightmapPacker.Levels - 1}}
              empty_tile_offset={ {{packing.EmptyR}} {{packing.EmptyG}} }

              """);
    }

    private static void WriteDefaultMap(string path, int provinceCount, int baronyCount,
        int landCount)
    {
        // Impassable provinces are land, so they sit between the baronied land and the sea zones.
        string impassable = landCount > baronyCount
            ? $"impassable_mountains = RANGE {{ {baronyCount + 1} {landCount} }}"
            : "";

        ParadoxText.WriteNoBom(path,
            $$"""
              #max_provinces = {{provinceCount + 1}}
              definitions = "definition.csv"
              provinces = "provinces.png"
              rivers = "rivers.png"
              topology = "heightmap.heightmap"
              continent = "continent.txt"
              adjacencies = "adjacencies.csv"
              island_region = "island_region.txt"
              seasons = "seasons.txt"

              #############
              # SEA ZONES
              #############
              sea_zones = RANGE { {{landCount + 1}} {{provinceCount}} }

              #############
              # MAJOR RIVERS
              #############

              #############
              # LAKES
              #############

              #############
              # IMPASSABLE
              #############
              {{impassable}}

              """);
    }

    /// <summary>Files default.map references that must exist even when effectively empty.</summary>
    private static void WriteStubs(string dir)
    {
        // The `-1` row is a REQUIRED terminator, not a formality. CK3's parser reads rows until
        // it sees that sentinel; a header-only file sends it past end-of-file and it loops there
        // forever — measured at ~437,000 read calls per second with zero bytes transferred, the
        // main thread parked in NtDelayExecution waiting on the worker. Nothing is logged, no
        // memory is allocated and no disk activity registers, so the load simply stops dead
        // after "End loading of history". Both vanilla and ck2rpg's template end the file this
        // way, and the template's entire file is exactly these two lines.
        ParadoxText.WriteNoBom(Path.Combine(dir, "adjacencies.csv"),
            """
            From;To;Type;Through;start_x;start_y;stop_x;stop_y;Comment
            -1;-1;;-1;-1;-1;-1;-1;

            """);

        // NEVER write this file empty. A zero-byte island_region.txt makes CK3 spin forever
        // reading it: measured at ~437,000 read operations per second with the transferred byte
        // count frozen, i.e. a parser looping on end-of-file. The main thread then sits in
        // NtDelayExecution waiting on the worker, so the load stops dead after
        // "End loading of history" with no log output, no allocation and no disk activity —
        // which is why it survived every other change. Vanilla's file is 2147 bytes and is
        // entirely comments plus a few regions; comments alone are enough to keep the parser fed.
        ParadoxText.WriteNoBom(Path.Combine(dir, "island_region.txt"),
            """
            # Island regions - no land path from the continent
            # The AI needs these to optimize path finding
            #
            # NOTE: do not add any regions here that are NOT islands
            #
            # Island regions can be declared with one or more of the following fields:
            #	duchies = { }, takes duchy title names declared in landed_titles.txt
            #	counties = { }, takes county title names declared in landed_titles.txt
            #	provinces = { }, takes province id numbers declared in /history/provinces

            """);

        // No nodes.dat on purpose. It is the precomputed pathfinding graph, 44 MB in vanilla and
        // built for the 9216x4608 map, so it must not reach our world — but the mod now declares
        // replace_path="map_data", which already drops vanilla's copy. Writing an empty one on
        // top would just hand the loader a zero-length graph to parse; leaving the file absent
        // lets CK3 build the graph itself. ck2rpg never emits one either.

        // Vanilla's exact shape. The previous version here was invented — a `winter_and_summer`
        // block using `start_month`/`end_month` — and CK3 has no such key. It parses, defines
        // none of the seasons the engine looks up, and every lookup then returns "not found".
        // The tree_* entries drive foliage rendering, so the damage lands in map setup rather
        // than anywhere that logs a script error. Keys and the start_date/end_date fields must
        // match exactly; AGOT ships no seasons.txt at all and simply uses vanilla's.
        ParadoxText.WriteNoBom(Path.Combine(dir, "seasons.txt"),
            """
            winter = {
            	start_date=00.12.01
            	end_date=00.02.31
            }

            spring = {
            	start_date=00.04.01
            	end_date=00.05.1
            }

            summer = {
            	start_date=00.06.01
            	end_date=00.09.10
            }

            autumn = {
            	start_date=00.10.10
            	end_date=00.10.31
            }

            tree_winter = {
            	start_date=00.11.15
            	end_date=00.12.01
            }
            tree_winter2 = {
            	start_date=00.12.20
            	end_date=00.01.20
            }
            tree_spring = {
            	start_date=00.02.20
            	end_date=00.03.01
            }
            tree_spring2 = {
            	start_date=00.03.20
            	end_date=00.04.20
            }
            tree_summer = {
            	start_date=00.05.20
            	end_date=00.06.01
            }
            tree_summer2 = {
            	start_date=00.06.20
            	end_date=00.09.10
            }
            tree_autumn = {
            	start_date=00.10.01
            	end_date=00.10.10
            }
            tree_autumn2 = {
            	start_date=00.10.25
            	end_date=00.11.01
            }

            """);
    }
}
