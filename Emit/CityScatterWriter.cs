using System.Globalization;
using System.Text;
using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// Writes gfx/map/map_object_data/city_scatter.txt — suburb clusters around settled holdings, so
/// no two towns on the map look alike. PROTOTYPE: the whole feature is one file, one config flag
/// (<see cref="MapConfig.EnableCityScatter"/> / <c>--no-city-scatter</c>) and one call site, and
/// it draws from its own Rng stream, so switching it off changes nothing else in the output.
///
/// <b>The route.</b> Vanilla's Constantinople sprawl is the model: a buildings mesh placed by a
/// map_object_data file, exactly the fields <see cref="BridgeWriter"/> writes. Y is 0 — the
/// engine snaps props to the full-resolution heightmap at load — and the building meshes use a
/// snap_to_terrain shader besides, so height is not this writer's problem; staying off water,
/// steep ground and other objects is.
///
/// <b>What is placed.</b> Tier-1 city meshes at 0.38–0.55 scale, hugging the holding anchor —
/// from 4.6 units out on an unwalled holding, 6.0 on a walled one, to 10.0 (see
/// <see cref="RingInnerWalled"/> for why those numbers and not the holding AABB's). Pieces mostly
/// bud off one another rather than taking independent positions, so a cluster grows lobes and
/// short rows on one side of the holding instead of ringing it evenly. The count follows
/// development and the holding type: a rich
/// city holding reads as a town with outskirts, a poor church holding gets nothing. Tribal and
/// nomad holdings get nothing at all — stone suburbs on a palisade camp is the one obvious way
/// for this to look wrong. cfg.HoldingScale multiplies both the piece scale and the ring, because
/// <see cref="HoldingModelWriter"/> applies it through the *entity* and a map_object_data
/// placement bypasses the entity — without it here, resized holdings and their suburbs drift
/// apart.
///
/// <b>What is avoided.</b> The special-building anchor (a wonder may stand there, see
/// <see cref="ProvinceAnchor"/>), water within a piece's footprint, cross-slopes over
/// <see cref="SlopeLimit"/>, other pieces of the same cluster, and everything outside the
/// province's own pixels — which is also what keeps a cluster off a neighbour's holding, off the
/// unit locators of other provinces, and out of the sea.
///
/// <b>Style.</b> The county culture's building_gfx fallback chain, walked the same way
/// <see cref="CompatibilityWriter.WriteGeographicalRegions"/> walks it, so the suburbs match the
/// walls and the holding the engine itself will draw. Families without a city mesh of their own
/// borrow the one vanilla's regions pair them with (steppe shares mena's walls, so it shares
/// mena's suburbs).
/// </summary>
public static class CityScatterWriter
{
    private const string OutputFile = "city_scatter.txt";

    /// <summary>
    /// How close a piece may stand to the holding anchor, world units at HoldingScale 1.
    ///
    /// These were calibrated against the wrong number until 2026-08-27: a holding mesh's AABB is
    /// dominated by its flat ground <c>decal_plane</c> (radius 4.5–6.1), while the *buildings*
    /// inside it only reach radius 2.3–3.3 — measured across the western/mediterranean/mena city
    /// meshes, and less again for castles (1.6) and temples (2.0). Overlapping a decal is
    /// harmless and in fact desirable, since the dirt blends the settlement together; overlapping
    /// geometry is not. So the old 6.5 inner edge stood a full holding-width too far out and read
    /// as satellite hamlets rather than as outskirts.
    ///
    /// The binding constraint on a walled holding is the WALL, not the holding: wall rings run
    /// radius 3.2–4.1 across every tier and culture (decal skirt 7.7, again ignorable). Suburbs
    /// belong outside the wall, so walled holdings keep a wider berth.
    /// </summary>
    private const double RingInnerWalled = 6.0, RingInnerOpen = 4.6, RingOuter = 10.0;

    /// <summary>Instance scale range before HoldingScale. Tier-1 city meshes are ~9 units
    /// across, so this renders pieces 3.4–5 units wide — hamlet-sized beside the holding.</summary>
    private const double ScaleMin = 0.38, ScaleMax = 0.55;

    /// <summary>Minimum spacing between two piece centres in one cluster, world units. A piece's
    /// building geometry is ~1.2 wide at these scales, so this leaves a clear gap while letting
    /// the ground decals merge — which is what makes neighbouring pieces read as one settlement
    /// instead of separate hamlets.</summary>
    private const double Separation = 2.6;

    /// <summary>Chance a piece buds off an already-placed neighbour rather than taking a fresh
    /// ring position. This is the whole of the "organic" behaviour: budding grows lobes and rows
    /// out of the first few pieces, where independent ring draws spread evenly into an annulus no
    /// real settlement ever formed.</summary>
    private const double BudChance = 0.65;

    /// <summary>Keep-out radius around the special-building anchor, where a wonder may stand.</summary>
    private const double SpecialClearance = 5.0;

    /// <summary>Largest tolerated height difference across a piece's footprint, world units over
    /// ~3 px. The building shader conforms to terrain, so a slope does not gap — it deforms, and
    /// past roughly 17 degrees the deformation is what you see.</summary>
    private const float SlopeLimit = 0.9f;

    private sealed record Family(string Name, string[] Meshes);

    // One palette per family, as many silhouettes as vanilla lets that palette have. The
    // cross-region variants (building_india_city_01_mena_mesh and kin) are the load-bearing
    // trick: the same shape re-textured onto another region's atlas, shipped by vanilla for
    // holdings that sit outside their culture's home region — which is exactly a suburb pool's
    // problem. Two shapes per pool read as a stamp; five to eleven read as a town.
    private static readonly Family Western = new("western", [
        "western_city_01_a_mesh", "western_city_01_b_mesh", "western_city_01_c_mesh",
        "building_western_city_02_mesh",
        "building_mediterranean_city_01_western_mesh",
        "building_mena_city_01_western_mesh",
        "building_india_city_01_western_mesh"]);
    private static readonly Family Norse = new("norse", [
        "fp1_building_norse_city_01_a_mesh", "fp1_building_norse_city_02_a_mesh",
        "western_city_01_a_mesh", "western_city_01_b_mesh", "western_city_01_c_mesh"]);
    private static readonly Family Mediterranean = new("mediterranean", [
        "building_mediterranean_city_01_mesh", "building_mediterranean_city_02_mesh",
        "western_city_01_mediterranean_a_mesh", "western_city_01_mediterranean_b_mesh",
        "western_city_01_mediterranean_c_mesh",
        "building_mena_city_01_mediterranean_mesh", "building_mena_city_02_mediterranean_mesh",
        "building_india_city_01_mediterranean_mesh", "building_india_city_02_mediterranean_mesh"]);
    private static readonly Family Byzantine = new("byzantine", [
        "ep3_byzantine_city_01_mesh", "ep3_byzantine_city_02_mesh",
        "building_mediterranean_city_01_mesh", "building_mediterranean_city_02_mesh",
        "western_city_01_mediterranean_a_mesh", "western_city_01_mediterranean_b_mesh",
        "western_city_01_mediterranean_c_mesh"]);
    private static readonly Family Iberian = new("iberian", [
        "fp2_building_iberian_city_01_mesh", "fp2_building_iberian_city_02_mesh",
        "building_mediterranean_city_01_mesh", "building_mediterranean_city_02_mesh",
        "western_city_01_mediterranean_a_mesh", "western_city_01_mediterranean_b_mesh",
        "western_city_01_mediterranean_c_mesh"]);
    private static readonly Family Mena = new("mena", [
        "building_mena_city_01_mesh", "building_mena_city_02_mesh",
        "western_city_01_mena_a_mesh", "western_city_01_mena_b_mesh",
        "western_city_01_mena_c_mesh",
        "building_mediterranean_city_01_mena_mesh", "building_mediterranean_city_02_mena_mesh",
        "building_india_city_01_mena_mesh", "building_india_city_02_mena_mesh"]);
    private static readonly Family Persian = new("persian", [
        "fp3_building_persian_city_01_a_01_mesh", "fp3_building_persian_city_02_a_01_mesh",
        "building_mena_city_01_mesh", "building_mena_city_02_mesh",
        "western_city_01_mena_a_mesh", "western_city_01_mena_b_mesh",
        "western_city_01_mena_c_mesh"]);
    private static readonly Family India = new("india", [
        "building_india_city_01_mesh", "building_india_city_02_mesh",
        "western_city_01_indian_a_mesh", "western_city_01_indian_b_mesh",
        "western_city_01_indian_c_mesh",
        "building_mediterranean_city_01_indian_mesh", "building_mediterranean_city_02_indian_mesh",
        "building_mena_city_01_indian_mesh", "building_mena_city_02_indian_mesh"]);
    private static readonly Family Chinese = new("chinese", [
        "tgp_building_chinese_city_01_mesh", "tgp_building_chinese_city_02_mesh"]);
    private static readonly Family Japanese = new("japanese", [
        "tgp_building_japanese_city_01_mesh", "tgp_building_japanese_city_02_mesh"]);
    private static readonly Family SeAsia = new("se_asia", [
        "tgp_building_se_asia_city_01_a_mesh", "tgp_building_se_asia_city_02_a_mesh"]);

    /// <summary>
    /// building_gfx token to suburb family. Steppe shares mena's pool the way vanilla's regions
    /// share mena's walls; caucasian rides with byzantine, matching the asset blocks vanilla
    /// pairs them in. Every mesh here ships in the base game — DLC gating is script-side and
    /// map_object_data has none, the Constantinople sprawl being vanilla's own precedent.
    /// </summary>
    private static readonly Dictionary<string, Family> FamilyByGfx = new(StringComparer.Ordinal)
    {
        ["western_building_gfx"] = Western,
        ["norse_building_gfx"] = Norse,
        ["east_slavic_building_gfx"] = Western,
        ["mediterranean_building_gfx"] = Mediterranean,
        ["byzantine_building_gfx"] = Byzantine,
        ["caucasian_building_gfx"] = Byzantine,
        ["iberian_building_gfx"] = Iberian,
        ["mena_building_gfx"] = Mena,
        ["arabic_group_building_gfx"] = Mena,
        ["berber_group_building_gfx"] = Mena,
        ["african_building_gfx"] = Mena,
        ["iranian_building_gfx"] = Persian,
        ["steppe_building_gfx"] = Mena,
        ["indian_building_gfx"] = India,
        ["tibetan_building_gfx"] = India,
        ["southeast_asian_building_gfx"] = SeAsia,
        ["chinese_building_gfx"] = Chinese,
        ["japanese_building_gfx"] = Japanese,
        ["emishi_building_gfx"] = Japanese,
        ["amuric_building_gfx"] = Chinese,
    };

    public static void WriteAll(string modDir, MapConfig cfg, List<Title> empires,
        Dictionary<int, string> holdings, Dictionary<Title, int> development, CultureMap cultures,
        ProvinceMap provinces, int[] order, ProvinceAnchor.Anchors anchors, float[] renderedElevation)
    {
        string dir = Path.Combine(modDir, "gfx", "map", "map_object_data");
        string path = Path.Combine(dir, OutputFile);

        // Off is really off: clear anything a previous run left, the way HoldingModelWriter
        // does, because the mod directory is not wiped between runs.
        if (!cfg.EnableCityScatter)
        {
            if (File.Exists(path)) File.Delete(path);
            Console.WriteLine("  city scatter: disabled, nothing written");
            return;
        }
        Directory.CreateDirectory(dir);

        // Own stream, not the shared scatter Rng: disabling this feature must not shift the
        // draws any other writer sees.
        var rng = new Rng(cfg.Seed ^ 0xC17F);

        // The same Anchors object LocatorWriter is given, so the ring here is centred on the exact
        // point the holding model is drawn at — the same array, now, rather than a second run of a
        // deterministic function.
        var labelById = new Dictionary<int, int>(provinces.Count);
        for (int label = 0; label < provinces.Count; label++) labelById[order[label]] = label;

        float sea = cfg.Limits.SeaLevelUpper;
        double ringScale = Math.Max(cfg.HoldingScale, 0.5);

        var placedByMesh = new Dictionary<string, List<(float X, float Z, float Angle, float Scale)>>();
        int clusters = 0, pieces = 0, starved = 0;
        var familyCount = new Dictionary<string, int>();

        foreach (var county in Titles.Flatten(empires).Where(t => t.Tier == "c"))
        {
            int dev = development.GetValueOrDefault(county);
            Family? family = null;

            foreach (var barony in county.Children)
            {
                int pid = barony.ProvinceId;
                if (pid <= 0 || !labelById.TryGetValue(pid, out int label)) continue;

                int want = holdings.GetValueOrDefault(pid) switch
                {
                    "city_holding" => 2 + dev / 8,
                    "castle_holding" => 1 + dev / 10,
                    "church_holding" => dev / 12,
                    // Tribes, nomads, wilderness, empty baronies: no stone outskirts.
                    _ => 0,
                };
                if (want <= 0) continue;
                want = Math.Min(want + rng.Int(-1, 1), 5);
                if (want <= 0) continue;

                family ??= FamilyFor(cultures.For(county).BuildingGfx);
                var (ax, ay) = anchors.Holding[label];
                var (sx, sy) = anchors.Special[label];
                var mine = new List<(double X, double Y)>();
                string? lastMesh = null;

                // A castle builds curtain walls, ramparts, hill forts or watchtowers, and a county
                // capital of any type draws vanilla's palisade ring (walls_01_tribal fires on
                // is_county_capital alone). Everything else — a plain city or church barony — is
                // never walled by vanilla's ladder, so its outskirts may come right up to it.
                bool walled = holdings.GetValueOrDefault(pid) == "castle_holding"
                              || ReferenceEquals(barony, county.Capital);
                double inner = (walled ? RingInnerWalled : RingInnerOpen) * ringScale;
                double outer = RingOuter * ringScale;

                // Settlements grow to one side — downhill, along the road, toward the water — not
                // evenly around the keep. One preferred bearing per cluster, with the spread wide
                // enough that it still reads as a town rather than a line.
                double bearing = rng.Double(0, Math.PI * 2);

                for (int piece = 0; piece < want; piece++)
                {
                    // Generous, because the inward bias concentrates draws into a narrow band that
                    // a wonder's keep-out, a slope or the province edge can block most of.
                    for (int attempt = 0; attempt < 80; attempt++)
                    {
                        double px, py;
                        if (mine.Count > 0 && rng.Double(0, 1) < BudChance)
                        {
                            // Bud off a neighbour: one separation away in any direction, so pieces
                            // arrive in touching clumps.
                            var (bx, by) = mine[rng.Int(0, mine.Count - 1)];
                            double bt = rng.Double(0, Math.PI * 2);
                            double bd = Separation * ringScale * rng.Double(1.0, 1.5);
                            px = bx + Math.Cos(bt) * bd;
                            py = by + Math.Sin(bt) * bd;

                            // A bud must still respect the holding's own keep-out and the cluster
                            // radius, or clumps would creep into the walls and across the province.
                            double rx = px - ax, ry = py - ay;
                            double rr = Math.Sqrt(rx * rx + ry * ry);
                            if (rr < inner || rr > outer + Separation * ringScale) continue;
                        }
                        else
                        {
                            // Squared, so fresh pieces land near the inner edge and the far half of
                            // the band is reached mostly by budding outward from them. An
                            // area-uniform draw (the sqrt form) is the *opposite* bias — an
                            // annulus has more room at its rim — and that is what made the first
                            // pass read as satellites parked at arm's length.
                            double u = rng.Double(0, 1);
                            double r = inner + (outer - inner) * u * u;

                            // The FIRST piece takes any bearing: it is the seed the rest buds
                            // from, and restricting it to the preferred arc only starves clusters
                            // whose arc happens to face water, a slope or the province edge.
                            double t = mine.Count == 0
                                ? rng.Double(0, Math.PI * 2)
                                : bearing + rng.Double(-1.5, 1.5);
                            px = ax + Math.Cos(t) * r;
                            py = ay + Math.Sin(t) * r;
                        }

                        if (!Fits(provinces, renderedElevation, cfg, sea, label, px, py)) continue;

                        double dsx = px - sx, dsy = py - sy;
                        if (dsx * dsx + dsy * dsy < SpecialClearance * SpecialClearance * ringScale * ringScale)
                            continue;

                        bool near = false;
                        foreach (var (ox, oy) in mine)
                        {
                            double dx = ox - px, dy = oy - py;
                            if (dx * dx + dy * dy < Separation * Separation * ringScale * ringScale)
                            { near = true; break; }
                        }
                        if (near) continue;

                        // Face the holding, roughly: local +Z lands on world (sin t, cos t) under
                        // the (0, sin t/2, 0, cos t/2) quaternion every writer here uses.
                        var (wx, wz) = WorldSpace.FromImage(px, py, provinces.Height);
                        var (awx, awz) = WorldSpace.FromImage(ax, ay, provinces.Height);
                        float angle = (float)(Math.Atan2(awx - wx, awz - wz)
                                              + rng.Double(-0.6, 0.6));

                        float scale = (float)(rng.Double(ScaleMin, ScaleMax) * cfg.HoldingScale);

                        // Never the same silhouette twice running in one cluster — with pools
                        // this size a repeat is what reads as a stamp.
                        string mesh = family.Meshes[rng.Int(0, family.Meshes.Length - 1)];
                        if (mesh == lastMesh && family.Meshes.Length > 1)
                            mesh = family.Meshes[rng.Int(0, family.Meshes.Length - 1)];
                        lastMesh = mesh;

                        if (!placedByMesh.TryGetValue(mesh, out var list))
                            placedByMesh[mesh] = list = [];
                        list.Add(((float)wx, (float)wz, angle, scale));

                        // The seed piece sets the quarter the rest of the town leans into, so the
                        // bias follows ground that actually accepted a building.
                        if (mine.Count == 0) bearing = Math.Atan2(py - ay, px - ax);
                        mine.Add((px, py));
                        break;
                    }
                }

                if (mine.Count > 0)
                {
                    clusters++;
                    pieces += mine.Count;
                    familyCount[family.Name] = familyCount.GetValueOrDefault(family.Name) + mine.Count;
                }
                if (mine.Count < want) starved++;
            }
        }

        Write(path, placedByMesh);

        string spread = familyCount.Count == 0 ? "nothing placed"
            : string.Join(", ", familyCount.OrderByDescending(kv => kv.Value)
                .Select(kv => $"{kv.Value} {kv.Key}"));
        Console.WriteLine($"  city scatter: {pieces} pieces around {clusters} holdings ({spread}; " +
                          $"{starved} holdings placed fewer than rolled — cramped or steep ground)");
    }

    /// <summary>
    /// Whether a piece can stand at (px, py): its footprint's own province, dry, and near-level.
    /// Samples the centre and four offsets ~1.5 px out — roughly the footprint of a piece at the
    /// scales used here — against the same rendered heightmap the dry-land tests all use.
    /// </summary>
    private static bool Fits(ProvinceMap provinces, float[] elevation, MapConfig cfg, float sea,
        int label, double px, double py)
    {
        int ix = (int)px, iy = (int)py;
        if (ix < 0 || iy < 0 || ix >= provinces.Width || iy >= provinces.Height) return false;
        if (provinces.Label[iy * provinces.Width + ix] != label) return false;

        float centre = ScatterGround.SampleHeight(elevation, cfg, px, py);
        if (float.IsNaN(centre) || centre <= sea) return false;

        float lo = centre, hi = centre;
        foreach (var (dx, dy) in (ReadOnlySpan<(double, double)>)[(1.5, 0), (-1.5, 0), (0, 1.5), (0, -1.5)])
        {
            float h = ScatterGround.SampleHeight(elevation, cfg, px + dx, py + dy);
            if (float.IsNaN(h) || h <= sea) return false;
            lo = Math.Min(lo, h); hi = Math.Max(hi, h);
        }
        return ScatterGround.WorldHeight(hi, cfg) - ScatterGround.WorldHeight(lo, cfg) <= SlopeLimit;
    }

    /// <summary>First token of the culture's building_gfx fallback chain that names a family —
    /// the same walk WriteGeographicalRegions does, so suburbs and walls always agree.</summary>
    private static Family FamilyFor(string buildingGfx)
    {
        foreach (var token in buildingGfx.Split([' ', '{', '}', '\t'],
                     StringSplitOptions.RemoveEmptyEntries))
            if (FamilyByGfx.TryGetValue(token, out var family)) return family;
        return Western;
    }

    private static void Write(string path,
        Dictionary<string, List<(float X, float Z, float Angle, float Scale)>> placedByMesh)
    {
        var sb = new StringBuilder(16384);
        var culture = CultureInfo.InvariantCulture;

        // Same block shape as vanilla's Constantinople sprawl, which is this exact route:
        // a buildings-atlas mesh addressed by pdxmesh name from a map_object_data file.
        foreach (var (mesh, instances) in placedByMesh.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            sb.Append("object={\n");
            sb.Append($"\tname=\"city scatter {mesh}\"\n");
            sb.Append("\trender_pass=MapUnderWater\n");
            sb.Append("\tclamp_to_water_level=no\n");
            sb.Append("\tgenerated_content=no\n");
            sb.Append("\tlayer=\"building_layer\"\n");
            sb.Append($"\tpdxmesh=\"{mesh}\"\n");
            sb.Append($"\tcount={instances.Count}\n");
            sb.Append("\ttransform=\"");

            for (int i = 0; i < instances.Count; i++)
            {
                var (x, z, angle, scale) = instances[i];
                double qy = Math.Sin(angle / 2.0);
                double qw = Math.Cos(angle / 2.0);

                // Y 0: the engine snaps map objects to the full-resolution heightmap at load.
                if (i > 0) sb.Append('\n');
                sb.Append(x.ToString("F6", culture)).Append(" 0.000000 ")
                  .Append(z.ToString("F6", culture)).Append(" 0.000000 ")
                  .Append(qy.ToString("F6", culture)).Append(" 0.000000 ")
                  .Append(qw.ToString("F6", culture)).Append(' ')
                  .Append(scale.ToString("F6", culture)).Append(' ')
                  .Append(scale.ToString("F6", culture)).Append(' ')
                  .Append(scale.ToString("F6", culture));
            }

            sb.Append("\"}\n");
        }

        ParadoxText.WriteBom(path, sb.ToString());
    }
}
