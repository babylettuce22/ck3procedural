using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.World;

namespace Ck3MapGen.MapGen;

public enum WonderArchetype
{
    Sanctuary,      // Sacred Temple / Grand Cathedral / Holy Spire
    GreatHarbor,    // Colossus / Pharos / Grand Port
    GreatLibrary,   // House of Wisdom / Imperial Academy / Great Archives
    Citadel,        // Legendary Fortress / Theodosian Walls / High Bastion
    ImperialPalace  // Golden Palace / High Seat of Sovereigns
}

public sealed class GeneratedWonder
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required WonderArchetype Archetype { get; init; }
    public required Title County { get; init; }
    public required Title Barony { get; init; }
    public required string Icon { get; init; }

    /// <summary>
    /// The pdxmesh CK3 draws at the province's special_building locator. Without this the wonder
    /// has modifiers and a build-menu entry but nothing at all on the map — see
    /// <see cref="WonderAssets"/>.
    /// </summary>
    public required string Mesh { get; init; }

    public required Dictionary<string, string> CharacterModifiers { get; init; }
    public required Dictionary<string, string> CountyModifiers { get; init; }
    public required Dictionary<string, string> ProvinceModifiers { get; init; }
}

public sealed class WorldCenter
{
    public required Title County { get; init; }
    public required Title CapitalBarony { get; init; }
    public required GeneratedWonder Wonder { get; init; }
    public required double GeographicScore { get; init; }
}

public sealed class WorldCenterMap
{
    public List<WorldCenter> Centers { get; } = [];
    private readonly HashSet<Title> _centerCounties = [];

    public bool IsCenter(Title county) => _centerCounties.Contains(county);


    public static WorldCenterMap Build(
        List<Title> counties,
        ProvinceMap provinces,
        int[] order,
        int landCount,
        TerrainClass[] provinceTerrain,
        CultureMap cultures,
        WildernessMap wilderness,
        MapConfig cfg,
        Rng rng)
    {
        var map = new WorldCenterMap();
        if (!cfg.EnableWorldCenters || counties.Count == 0) return map;

        var (neighbours, positions) = BuildGraph(counties, provinces, order, landCount);

        var scored = new List<(Title County, double Score, (double X, double Y) Pos)>();
        for (int i = 0; i < counties.Count; i++)
        {
            var county = counties[i];
            if (wilderness.Contains(county)) continue;

            double score = ScoreCounty(county, provinceTerrain, neighbours[i].Count, positions[i], cfg, rng);
            scored.Add((county, score, positions[i]));
        }

        if (scored.Count == 0) return map;
        scored.Sort((a, b) => b.Score.CompareTo(a.Score));

        int targetCount = Math.Max(1, Math.Min(cfg.WorldCentersCount, counties.Count / 10));
        var chosen = new List<(Title County, double Score, (double X, double Y) Pos)>();

        // Multi-pass spacing & de jure separation check
        for (int relaxationPass = 0; relaxationPass < 3 && chosen.Count < targetCount; relaxationPass++)
        {
            double minDistance = relaxationPass switch
            {
                0 => 0.20,
                1 => 0.12,
                _ => 0.06
            };
            double minDistanceSq = minDistance * minDistance;

            foreach (var candidate in scored)
            {
                if (chosen.Count >= targetCount) break;
                if (chosen.Any(c => c.County == candidate.County)) continue;

                bool tooClose = false;
                foreach (var existing in chosen)
                {
                    if (candidate.County.Parent is not null && candidate.County.Parent == existing.County.Parent)
                    {
                        tooClose = true;
                        break;
                    }

                    if (relaxationPass == 0 && candidate.County.Parent?.Parent is not null
                        && candidate.County.Parent.Parent == existing.County.Parent?.Parent)
                    {
                        tooClose = true;
                        break;
                    }

                    double dx = candidate.Pos.X - existing.Pos.X;
                    double dy = candidate.Pos.Y - existing.Pos.Y;
                    if (dx * dx + dy * dy < minDistanceSq)
                    {
                        tooClose = true;
                        break;
                    }
                }

                if (!tooClose)
                {
                    chosen.Add(candidate);
                }
            }
        }

        // Shared across centres so no two wonders on one map wear the same model.
        var usedMeshes = new HashSet<string>();

        for (int i = 0; i < chosen.Count; i++)
        {
            var (county, score, _) = chosen[i];
            var barony = county.Children.FirstOrDefault() ?? county;
            var culture = cultures.For(county);
            var centerRng = new Rng(county.Index ^ 0x5C07 ^ (i * 7919));

            var (coastal, mountainous) = Relief(county, provinceTerrain);
            var archetype = PickArchetype(coastal, mountainous, cfg, centerRng);
            var wonder = GenerateWonder(county, barony, archetype, mountainous,
                culture.Language, centerRng, usedMeshes);

            var center = new WorldCenter
            {
                County = county,
                CapitalBarony = barony,
                Wonder = wonder,
                GeographicScore = score
            };

            map.Centers.Add(center);
            map._centerCounties.Add(county);
        }

        Console.WriteLine($"  world centers: {map.Centers.Count} great metropolises established across the realm");
        foreach (var c in map.Centers)
        {
            Console.WriteLine($"    · {c.County.Name}: {c.Wonder.Name} ({c.Wonder.Archetype}) -> {c.Wonder.Mesh}");
        }

        return map;
    }

    private static double ScoreCounty(Title county, TerrainClass[] terrain, int neighborCount, (double X, double Y) pos, MapConfig cfg, Rng rng)
    {
        double score = 0;
        bool coastal = false;

        foreach (var b in county.Children)
        {
            if (b.ProvinceId <= 0 || b.ProvinceId >= terrain.Length) continue;
            var t = terrain[b.ProvinceId];
            score += t switch
            {
                TerrainClass.Floodplains => 3.0,
                TerrainClass.Farmlands => 2.5,
                TerrainClass.Plains => 1.8,
                TerrainClass.Beach => 2.0,
                TerrainClass.Hills => 1.0,
                TerrainClass.Forest => 0.8,
                TerrainClass.Drylands => 0.6,
                _ => 0.2
            };
            if (t == TerrainClass.Beach) coastal = true;
        }

        score /= Math.Max(1, county.Children.Count);

        if (coastal) score += 1.5;
        score += Math.Min(3.0, neighborCount * 0.4);

        double edgeness = Math.Max(Math.Abs(pos.X - 0.5), Math.Abs(pos.Y - 0.5)) * 2.0;
        score += (1.0 - edgeness) * 1.5;

        score += rng.Double(0.0, 0.5);
        return score;
    }

    /// <summary>
    /// Whether the county touches the sea and whether it has any relief. Both drive the archetype;
    /// relief additionally decides whether a sacred-peak model is on the table, since a mountain
    /// mesh dropped on flat ground reads as a glitch rather than a wonder.
    /// </summary>
    private static (bool Coastal, bool Mountainous) Relief(Title county, TerrainClass[] terrain)
    {
        bool coastal = false, mountainous = false;
        foreach (var b in county.Children)
        {
            if (b.ProvinceId <= 0 || b.ProvinceId >= terrain.Length) continue;
            var t = terrain[b.ProvinceId];
            if (t == TerrainClass.Beach) coastal = true;
            if (t is TerrainClass.Hills or TerrainClass.Mountains) mountainous = true;
        }
        return (coastal, mountainous);
    }

    private static WonderArchetype PickArchetype(bool coastal, bool mountainous, MapConfig cfg, Rng rng)
    {
        int roll = rng.Int(0, 100);

        if (coastal && roll < 45) return WonderArchetype.GreatHarbor;
        if (mountainous && roll < 45) return WonderArchetype.Citadel;

        // GreatLibrary and ImperialPalace imply a literate, centralized administration the world
        // has not developed yet while it is still in the tribal era. Ancient worlds keep the
        // monumental archetypes such societies actually raised: temples, fortresses, harbors.
        if (Innovations.EraIndexAt(cfg.EraYear) == 0)
        {
            if (roll < 60) return WonderArchetype.Sanctuary;
            return coastal ? WonderArchetype.GreatHarbor : WonderArchetype.Citadel;
        }

        if (roll < 30) return WonderArchetype.Sanctuary;
        if (roll < 65) return WonderArchetype.ImperialPalace;
        return WonderArchetype.GreatLibrary;
    }

    private static GeneratedWonder GenerateWonder(Title county, Title barony, WonderArchetype archetype,
        bool mountainous, Language lang, Rng rng, HashSet<string> usedMeshes)
    {
        string key = $"wonder_{county.Key}";
        string word = lang.Word(rng, 2, 3);

        // Model first. Everything the player reads is derived from it, so that the pyramids on the
        // map are never captioned as a lighthouse — the archetype now only supplies the modifiers.
        var asset = WonderAssets.Pick(archetype, mountainous, rng, usedMeshes);
        string name = string.Format(rng.Pick(asset.Names), county.Name, word);
        string desc = string.Format(asset.Blurb, county.Name);
        string icon = asset.Icon;

        var charMod = new Dictionary<string, string>();
        var countyMod = new Dictionary<string, string>();
        var provMod = new Dictionary<string, string>();

        switch (archetype)
        {
            case WonderArchetype.Sanctuary:
                charMod["monthly_piety"] = "1.5";
                charMod["same_faith_opinion"] = "5";
                countyMod["county_opinion_add"] = "15";
                countyMod["development_growth"] = "0.2";
                provMod["monthly_income"] = "2.0";
                break;

            case WonderArchetype.GreatHarbor:
                charMod["monthly_prestige"] = "1.0";
                countyMod["development_growth"] = "0.4";
                countyMod["development_growth_factor"] = "0.25";
                provMod["monthly_income"] = "4.0";
                provMod["supply_limit_mult"] = "0.3";
                break;

            case WonderArchetype.GreatLibrary:
                charMod["learning"] = "2";
                charMod["cultural_head_fascination_mult"] = "0.2";
                charMod["monthly_lifestyle_xp_gain_mult"] = "0.15";
                countyMod["development_growth"] = "0.3";
                provMod["monthly_income"] = "1.5";
                break;

            case WonderArchetype.Citadel:
                charMod["dread_gain_mult"] = "0.25";
                charMod["advantage"] = "4";
                countyMod["defender_holding_advantage"] = "1.5"; // Corrected modifier scope
                provMod["fort_level"] = "4";
                provMod["garrison_size"] = "1000";
                break;

            case WonderArchetype.ImperialPalace:
            default:
                charMod["monthly_prestige"] = "2.0";
                charMod["vassal_limit"] = "10";
                charMod["court_grandeur_baseline_add"] = "15";
                countyMod["development_growth"] = "0.35";
                provMod["monthly_income"] = "3.5";
                break;
        }

        return new GeneratedWonder
        {
            Key = key,
            Name = name,
            Description = desc,
            Archetype = archetype,
            County = county,
            Barony = barony,
            Icon = icon,
            Mesh = asset.Mesh,
            CharacterModifiers = charMod,
            CountyModifiers = countyMod,
            ProvinceModifiers = provMod
        };
    }

    private static (List<int>[] Neighbours, (double X, double Y)[] Positions) BuildGraph(
        List<Title> counties, ProvinceMap provinces, int[] order, int landCount)
    {
        var countyOfProvince = new Dictionary<int, int>();
        for (int i = 0; i < counties.Count; i++)
            foreach (var b in counties[i].Children)
                if (b.ProvinceId > 0) countyOfProvince[b.ProvinceId] = i;

        var seedOfProvince = new Dictionary<int, int>();
        for (int label = 0; label < order.Length; label++)
        {
            int id = order[label];
            if (id >= 1 && id <= landCount) seedOfProvince[id] = label;
        }

        var neighbours = new List<int>[counties.Count];
        for (int i = 0; i < neighbours.Length; i++) neighbours[i] = [];

        var linked = new HashSet<(int, int)>();
        foreach (var (province, others) in Titles.BuildAdjacency(provinces, landCount, order))
        {
            if (!countyOfProvince.TryGetValue(province, out int a)) continue;
            foreach (int other in others)
            {
                if (!countyOfProvince.TryGetValue(other, out int b) || a == b) continue;
                var pair = a < b ? (a, b) : (b, a);
                if (!linked.Add(pair)) continue;
                neighbours[a].Add(b);
                neighbours[b].Add(a);
            }
        }

        var positions = new (double X, double Y)[counties.Count];
        for (int i = 0; i < counties.Count; i++)
        {
            double x = 0, y = 0;
            int counted = 0;
            foreach (var b in counties[i].Children)
            {
                if (!seedOfProvince.TryGetValue(b.ProvinceId, out int label)) continue;
                x += provinces.Seeds[label].X;
                y += provinces.Seeds[label].Y;
                counted++;
            }
            positions[i] = counted == 0 ? (0.5, 0.5) : (x / counted / provinces.Width, y / counted / provinces.Height);
        }

        return (neighbours, positions);
    }
}