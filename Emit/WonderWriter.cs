using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

// GREATLY NEEDS BALANCING
public static class WonderWriter
{
    public static void WriteAll(string modDir, WorldCenterMap worldCenters)
    {
        if (worldCenters.Centers.Count == 0) return;

        WriteBuildingDefinitions(modDir, worldCenters);
        WriteLocalisation(modDir, worldCenters);
        WriteUniversityTriggers(modDir, worldCenters);
    }

    // Emit/WonderWriter.cs -> WriteBuildingDefinitions()

    private static void WriteBuildingDefinitions(string modDir, WorldCenterMap worldCenters)
    {
        string dir = Path.Combine(modDir, "common", "buildings");
        Directory.CreateDirectory(dir);

        var b = new JominiBuilder();
        b.Comment("Generated Wonders / Special Buildings for World Centers");
        b.Blank();

        // A modifier block, written only when the wonder actually carries that kind of modifier.
        void ModifierBlock(string name, Dictionary<string, string> entries)
        {
            if (entries.Count == 0) return;

            using (b.Block(name))
                foreach (var (k, v) in entries) b.Field(k, v);

            b.Blank();
        }

        foreach (var center in worldCenters.Centers)
        {
            var wonder = center.Wonder;

            // Strip any accidental double .dds
            string cleanIcon = wonder.Icon;
            if (cleanIcon.EndsWith(".dds.dds", StringComparison.OrdinalIgnoreCase))
                cleanIcon = cleanIcon[..^4];
            if (!cleanIcon.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
                cleanIcon += ".dds";

            using (b.Block(wonder.Key))
            {
                // The map model. CK3 draws this at the province's special_building locator, which is a
                // separate anchor from the holding's own (see LocatorWriter). No filters on the block:
                // graphical_regions/cultures/faiths only narrow *when* an asset is eligible, and a
                // generated world has no guarantee that any particular one of them matches, so an
                // unfiltered block is the one that always resolves. Meshes are chosen in WonderAssets
                // and are all reachable without DLC.
                using (b.Block("asset"))
                {
                    b.Field("type", "pdxmesh");
                    b.Quoted("name", wonder.Mesh);
                }

                b.Blank();

                b.Quoted("type_icon", cleanIcon);
                b.Field("construction_time", "very_slow_construction_time");
                b.Field("type", "special");
                b.Field("cost_gold", "1000");
                b.Blank();

                using (b.Block("can_construct_potential"))
                using (b.Block("barony"))
                    b.Field("this", $"title:{wonder.Barony.Key}");

                b.Blank();

                using (b.Block("is_enabled")) b.Field("always", "yes");
                b.Blank();

                ModifierBlock("character_modifier", wonder.CharacterModifiers);
                ModifierBlock("county_modifier", wonder.CountyModifiers);
                ModifierBlock("province_modifier", wonder.ProvinceModifiers);

                using (b.Block("ai_value")) b.Field("base", "150");
            }

            b.Blank();
        }

        ParadoxText.WriteBom(Path.Combine(dir, "01_generated_wonders.txt"), b.ToString());
    }

    private static void WriteUniversityTriggers(string modDir, WorldCenterMap worldCenters)
    {
        var libraries = worldCenters.Centers
            .Where(c => c.Wonder.Archetype == WonderArchetype.GreatLibrary)
            .ToList();

        if (libraries.Count == 0) return;

        string dir = Path.Combine(modDir, "common", "scripted_triggers");
        Directory.CreateDirectory(dir);

        var b = new JominiBuilder();
        b.Comment("Overrides has_university_building_trigger to include generated Great Libraries");
        b.Blank();

        using (b.Block("has_university_building_trigger"))
        using (b.Block("OR"))
        {
            b.Field("has_building_or_higher", "generic_university");
            foreach (var lib in libraries) b.Field("has_building", lib.Wonder.Key);
        }

        ParadoxText.WriteBom(Path.Combine(dir, "zz_generated_university_triggers.txt"), b.ToString());
    }

    private static void WriteLocalisation(string modDir, WorldCenterMap worldCenters)
    {
        var loc = new LocFile();

        foreach (var center in worldCenters.Centers)
        {
            var wonder = center.Wonder;

            // Main building name & description
            loc.AddBuilt($"building_{wonder.Key}", wonder.Name);
            loc.AddBuilt($"building_{wonder.Key}_desc", wonder.Description);

            // Building type/category subtitle & description
            loc.AddBuilt($"building_type_{wonder.Key}", wonder.Name);
            loc.AddBuilt($"building_type_{wonder.Key}_desc", wonder.Description);
        }

        loc.Write(Path.Combine(modDir, "localization", "english", "gen_wonders_l_english.yml"));
    }
}