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

    // --- Verified Vanilla Icon Pools (Matching gfx/interface/icons/building_types/) ---

    private static readonly string[] SanctuaryIcons =
    [
        "icon_structure_hagia_sophia.dds",
        "icon_structure_dome_of_the_rock.dds",
        "icon_structure_temple_in_jerusalem.dds",
        "icon_structure_notre_dame.dds",
        "icon_structure_st_peters_basilica.dds",
        "icon_structure_great_mosque_of_mecca.dds",
        "icon_structure_great_mosque_of_cordoba.dds",
        "icon_structure_great_mosque_of_samarra.dds",
        "icon_structure_canterbury_cathedral.dds",
        "icon_structure_cologne_cathedral.dds",
        "icon_structure_angkor_wat.dds",
        "icon_structure_borobudur.dds",
        "icon_structure_mahabodhi_temple.dds",
        "icon_structure_shwedagon_pagoda.dds",
        "icon_structure_temple_of_uppsala.dds",
        "icon_structure_stonehenge.dds",
        "icon_structure_parthenon.dds",
        "icon_structure_buddhas_of_bamiyan.dds",
        "icon_structure_brihadeeswarar_temple.dds",
        "icon_structure_fogong_temple_pagoda.dds",
        "icon_structure_great_fire_temple.dds",
        "icon_structure_imam_ali_mosque.dds",
        "icon_structure_imam_reza_shrine.dds",
        "icon_structure_khajuraho_temples.dds",
        "icon_structure_konark_temple.dds",
        "icon_structure_lingyin_temple.dds",
        "icon_structure_lund_cathedral.dds",
        "icon_structure_mont_st_michel.dds",
        "icon_structure_my_son_sanctuary.dds",
        "icon_structure_sanchi_stupa.dds",
        "icon_structure_shaolin_monastery.dds",
        "icon_structure_sumela_monastery.dds",
        "icon_structure_wawel_cathedral.dds",
        "icon_structure_yazd_mosque.dds",
        "icon_structure_cathedral_pagan.dds",
        "icon_structure_cathedral_muslim.dds",
        "icon_structure_cathedral_indian.dds",
        "icon_structure_cathedral_buddhist.dds",
        "icon_structure_cathedral_zoroastric.dds",
        "icon_building_legendary_shrine.dds",
        "icon_megalith.dds",
        "mezquita_cordoba.dds",
        "compostela.dds"
    ];

    private static readonly string[] GreatHarborIcons =
    [
        "icon_structure_quanzhou_seaport.dds",
        "icon_structure_swahili_port.dds",
        "icon_structure_drassanes.dds",
        "icon_structure_kora_kora_yard.dds",
        "icon_structure_london_bridge.dds",
        "icon_structure_kairouan_basins.dds",
        "icon_structure_colosseum.dds",
        "icon_structure_petra.dds",
        "icon_structure_the_pyramids.dds",
        "icon_structure_pyramid_lingapura.dds",
        "icon_building_tradeport.dds",
        "icon_building_legendary_statue.dds",
        "hercules.dds",
        "gibraltar.dds"
    ];

    private static readonly string[] GreatLibraryIcons =
    [
        "icon_structure_grand_library_of_baghdad.dds",
        "icon_structure_al-azhar_university.dds",
        "icon_structure_al_qarawiyyin_university.dds",
        "icon_structure_the_university_of_sankore.dds",
        "icon_structure_university_of_siena.dds",
        "icon_structure_nalanda.dds",
        "icon_structure_somapura_university.dds",
        "icon_structure_dengfeng_observatory.dds",
        "icon_structure_yuelu_academy.dds",
        "icon_structure_confucius_temple.dds",
        "icon_building_university.dds",
        "icon_building_library.dds",
        "icon_building_examination_hall.dds",
        "icon_building_monastic_schools.dds"
    ];

    private static readonly string[] CitadelIcons =
    [
        "icon_structure_theodosian_walls.dds",
        "icon_structure_the_great_wall.dds",
        "icon_structure_aurelian_walls.dds",
        "icon_structure_hadrians_wall.dds",
        "icon_structure_great_wall_of_gorgan.dds",
        "icon_structure_the_citadel_of_aleppo.dds",
        "icon_structure_alamut_castle.dds",
        "icon_structure_ark_of_bukhara.dds",
        "icon_structure_tower_of_london.dds",
        "icon_structure_hotin_fortress.dds",
        "icon_structure_jaisalmer_fort.dds",
        "icon_structure_kano_walls.dds",
        "icon_structure_kassiopi_castle.dds",
        "icon_structure_patras_castle.dds",
        "icon_structure_visby_ringmur.dds",
        "icon_structure_visegrad_castle.dds",
        "icon_structure_walls_of_benin.dds",
        "icon_structure_walls_of_genoa.dds",
        "icon_structure_york_walls.dds",
        "icon_structure_falak_ol_aflak_citadel.dds",
        "icon_structure_gongsanseong_fortress.dds",
        "icon_structure_idjang_forts.dds",
        "lugo_walls.dds",
        "icon_building_curtain_walls.dds",
        "icon_building_legendary_watchtower.dds"
    ];

    private static readonly string[] ImperialPalaceIcons =
    [
        "icon_structure_forbidden_city.dds",
        "icon_structure_palace_of_achen.dds",
        "icon_structure_palace_of_ctesiphon.dds",
        "icon_structure_doges_palace.dds",
        "icon_structure_alhambra.dds",
        "icon_structure_despot_palace.dds",
        "icon_structure_ghana_palace.dds",
        "icon_structure_heian_palace.dds",
        "icon_structure_hoegyeongjeon.dds",
        "icon_structure_kaifeng_palace.dds",
        "icon_structure_wilwatikta_palace.dds",
        "alcazar_segovia.dds",
        "aljaferia.dds",
        "toledo.dds",
        "icon_structure_citadel_linan.dds",
        "icon_structure_citadel_thang_long.dds",
        "icon_building_legendary_palace.dds",
        "icon_building_leisure_palace.dds",
        "icon_building_royal_gardens.dds",
        "icon_pleasure_dome.dds"
    ];

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

        for (int i = 0; i < chosen.Count; i++)
        {
            var (county, score, _) = chosen[i];
            var barony = county.Children.FirstOrDefault() ?? county;
            var culture = cultures.For(county);
            var centerRng = new Rng(county.Index ^ 0x5C07 ^ (i * 7919));

            var archetype = PickArchetype(county, provinceTerrain, centerRng);
            var wonder = GenerateWonder(county, barony, archetype, culture.Language, centerRng);

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
            Console.WriteLine($"    · {c.County.Name}: {c.Wonder.Name} ({c.Wonder.Archetype}) -> Icon: {c.Wonder.Icon}");
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

    private static WonderArchetype PickArchetype(Title county, TerrainClass[] terrain, Rng rng)
    {
        bool coastal = county.Children.Any(b => b.ProvinceId > 0 && b.ProvinceId < terrain.Length && terrain[b.ProvinceId] == TerrainClass.Beach);
        bool mountainous = county.Children.Any(b => b.ProvinceId > 0 && b.ProvinceId < terrain.Length && terrain[b.ProvinceId] is TerrainClass.Hills or TerrainClass.Mountains);

        int roll = rng.Int(0, 100);

        if (coastal && roll < 45) return WonderArchetype.GreatHarbor;
        if (mountainous && roll < 45) return WonderArchetype.Citadel;
        if (roll < 30) return WonderArchetype.Sanctuary;
        if (roll < 65) return WonderArchetype.ImperialPalace;
        return WonderArchetype.GreatLibrary;
    }

    private static GeneratedWonder GenerateWonder(Title county, Title barony, WonderArchetype archetype, Language lang, Rng rng)
    {
        string key = $"wonder_{county.Key}";
        string word = lang.Word(rng, 2, 3);

        string name;
        string desc;
        string icon;

        var charMod = new Dictionary<string, string>();
        var countyMod = new Dictionary<string, string>();
        var provMod = new Dictionary<string, string>();

        switch (archetype)
        {
            case WonderArchetype.Sanctuary:
                name = rng.Pick([
                    $"The Grand Sanctuary of {county.Name}",
                    $"The Sacred Spire of {word}",
                    $"The Celestial Temple of {county.Name}",
                    $"The High Sanctum of {word}"
                ]);
                desc = $"An awe-inspiring pilgrimage monument towering over {county.Name}. Its hallowed halls resonate with divine majesty.";
                icon = rng.Pick(SanctuaryIcons);
                charMod["monthly_piety"] = "1.5";
                charMod["same_faith_opinion"] = "5";
                countyMod["county_opinion_add"] = "15";
                countyMod["development_growth"] = "0.2";
                provMod["monthly_income"] = "2.0";
                break;

            case WonderArchetype.GreatHarbor:
                name = rng.Pick([
                    $"The Colossus of {county.Name}",
                    $"The Great Pharos of {word}",
                    $"The Grand Haven of {county.Name}",
                    $"The Radiant Beacon of {word}"
                ]);
                desc = $"A monumental harbor and navigational marvel that guides ships from across known waters directly into the bustling markets of {county.Name}.";
                icon = rng.Pick(GreatHarborIcons);
                charMod["monthly_prestige"] = "1.0";
                countyMod["development_growth"] = "0.4";
                countyMod["development_growth_factor"] = "0.25";
                provMod["monthly_income"] = "4.0";
                provMod["supply_limit_mult"] = "0.3";
                break;

            case WonderArchetype.GreatLibrary:
                name = rng.Pick([
                    $"The Grand Archives of {county.Name}",
                    $"The House of Wisdom of {word}",
                    $"The Imperial Academy of {county.Name}",
                    $"The Great Athenaeum of {word}"
                ]);
                desc = $"A vast repository of philosophical scrolls, universal maps, and mathematical treatises drawing the brightest minds of the age.";
                icon = rng.Pick(GreatLibraryIcons);
                charMod["learning"] = "2";
                charMod["cultural_head_fascination_mult"] = "0.2";
                charMod["monthly_lifestyle_xp_gain_mult"] = "0.15";
                countyMod["development_growth"] = "0.3";
                provMod["monthly_income"] = "1.5";
                break;

            case WonderArchetype.Citadel:
                name = rng.Pick([
                    $"The Impregnable Walls of {county.Name}",
                    $"The High Bastion of {word}",
                    $"The Iron Citadel of {county.Name}",
                    $"The Aegis Fortress of {word}"
                ]);
                desc = $"A legendary fortress engineered with concentric battlements and deep granaries, feared by invaders and revered by sovereigns.";
                icon = rng.Pick(CitadelIcons);
                charMod["dread_gain_mult"] = "0.25";
                charMod["advantage"] = "4";
                countyMod["defender_holding_advantage"] = "1.5"; // Corrected modifier scope
                provMod["fort_level"] = "4";
                provMod["garrison_size"] = "1000";
                break;

            case WonderArchetype.ImperialPalace:
            default:
                name = rng.Pick([
                    $"The Golden Palace of {county.Name}",
                    $"The High Throne of {word}",
                    $"The Sunlit Court of {county.Name}",
                    $"The Crown Pavilion of {word}"
                ]);
                desc = $"A magnificent palace complex of gilded columns, ceremonial halls, and lush gardens projecting dynastic supremacy over the realm.";
                icon = rng.Pick(ImperialPalaceIcons);
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