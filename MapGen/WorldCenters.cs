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

    /// <summary>
    /// The building key for one rung of the wonder's ladder, numbered as vanilla numbers its own —
    /// <c>hagia_sophia_01</c>, <c>_02</c>, <c>_03</c>.
    ///
    /// <see cref="Key"/> is never a building on its own any more. It is the family name, and every
    /// reference to a wonder in script has to say which rung it means, because a county whose
    /// library has been upgraded no longer has a building called <see cref="Key"/> at all.
    /// </summary>
    public string TierKey(int tier) => $"{Key}_{tier:00}";

    /// <summary>
    /// The icon's filename, with exactly one <c>.dds</c> on the end.
    ///
    /// <see cref="Icon"/> is written by hand in the archetype tables and has arrived both with and
    /// without the extension, and occasionally with it twice.
    /// </summary>
    public string IconFile
    {
        get
        {
            string name = Icon;

            if (name.EndsWith(".dds.dds", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
            if (!name.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)) name += ".dds";

            return name;
        }
    }

    /// <summary>
    /// The icon as a texture PATH, which is a different thing from the filename.
    ///
    /// A building's <c>type_icon</c> takes the bare name and lets the engine find it; a <c>.gui</c>
    /// <c>icon</c> widget takes a path from the game root and silently draws nothing without one.
    /// Both spellings of the same icon are needed, so both live here rather than being reconstructed
    /// at each end.
    /// </summary>
    public string IconTexture => "gfx/interface/icons/building_types/" + IconFile;

    /// <summary>How many rungs the ladder has. Tier one is placed at game start; the rest are built.</summary>
    public const int Tiers = 3;

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

        // These are the values of a FULLY UPGRADED wonder — tier three. WonderWriter scales them
        // down for the two tiers below it, so the numbers a reader tunes are the ones at the top of
        // the ladder rather than the ones a world starts with.
        //
        // Rebalanced against vanilla's own envelope, because the previous table was not merely
        // generous, it was wrong by orders of magnitude in two places:
        //
        //   garrison_size was 1000. Vanilla's building values run 0.01 to 0.2 — it is a MULTIPLIER,
        //   so that was five thousand times the largest garrison bonus in the game.
        //
        //   monthly_income ran to 4.0 flat. The richest ordinary building in vanilla is
        //   good_tax_tier_8, which resolves to 0.5 + 7 x 0.3 = 2.6, and most special buildings sit
        //   far below that.
        //
        // The rest is measured against ORDINARY buildings at the top of their line, which is the
        // comparison that actually matters and the one a first pass here got wrong by only looking
        // at special buildings' character modifiers. `caravanserai_08` — a standard economy
        // building, not a wonder — gives:
        //
        //     monthly_income             3.85   (excellent_tax_tier_8: 0.7 + 7 x 0.45)
        //     development_growth_factor  0.32   (plus 0.16 flat growth on top)
        //     defender_holding_advantage 16     (normal_advantage_tier_8: 2 + 7 x 2)
        //
        // A wonder that gives less income than a caravanserai is not a wonder. So the province and
        // county lines below sit at or above that, and the character lines — which ordinary
        // buildings mostly do not have — are what make the wonder more than a very good building.
        //
        // Character values stay inside the envelope of vanilla's famous things: the Ark of the
        // Covenant's court_grandeur_baseline_add is 6 and the Reichskrone's vassal_limit is 25, so
        // a palace at 8 and 10 is grand without being unprecedented. Hagia Sophia carries
        // monthly_dynasty_prestige_mult 0.05, which is why every prestige archetype here does too.
        //
        // development_growth_factor rather than development_growth throughout: the flat version
        // adds to the county's growth pool and the factor multiplies it, and a wonder is a reason a
        // place grows faster rather than a fixed drip.
        //
        // monthly_dynasty_prestige_mult on the three "prestige" archetypes is deliberate copying —
        // it is the line vanilla puts on nearly every great building and famous artifact, and it is
        // what makes owning one feel like a dynastic fact rather than a stat.
        switch (archetype)
        {
            case WonderArchetype.Sanctuary:
                charMod["monthly_piety"] = "1.0";
                charMod["same_faith_opinion"] = "10";
                charMod["monthly_dynasty_prestige_mult"] = "0.05";
                countyMod["county_opinion_add"] = "20";
                countyMod["development_growth_factor"] = "0.35";
                provMod["monthly_income"] = "2.0";
                break;

            case WonderArchetype.GreatHarbor:
                charMod["monthly_prestige"] = "1.0";
                charMod["monthly_dynasty_prestige_mult"] = "0.05";
                countyMod["development_growth_factor"] = "0.45";
                provMod["monthly_income"] = "4.0";
                provMod["supply_limit_mult"] = "0.5";
                break;

            case WonderArchetype.GreatLibrary:
                charMod["learning"] = "5";
                charMod["cultural_head_fascination_mult"] = "0.3";
                charMod["monthly_lifestyle_xp_gain_mult"] = "0.3";
                countyMod["development_growth_factor"] = "0.45";
                provMod["monthly_income"] = "1.5";
                break;

            case WonderArchetype.Citadel:
                charMod["dread_gain_mult"] = "0.3";
                charMod["advantage"] = "6";
                countyMod["defender_holding_advantage"] = "20";
                countyMod["development_growth_factor"] = "0.2";
                provMod["fort_level"] = "5";
                provMod["garrison_size"] = "0.35";
                provMod["monthly_income"] = "1.0";
                break;

            case WonderArchetype.ImperialPalace:
            default:
                charMod["monthly_prestige"] = "1.0";
                charMod["vassal_limit"] = "10";
                charMod["court_grandeur_baseline_add"] = "8";
                charMod["monthly_dynasty_prestige_mult"] = "0.05";
                countyMod["development_growth_factor"] = "0.45";
                provMod["monthly_income"] = "3.0";
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