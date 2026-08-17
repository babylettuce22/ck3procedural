using System.Text;
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

        var sb = new StringBuilder();
        sb.Append("# Generated Wonders / Special Buildings for World Centers\n\n");

        foreach (var center in worldCenters.Centers)
        {
            var wonder = center.Wonder;

            // Strip any accidental double .dds
            string cleanIcon = wonder.Icon;
            if (cleanIcon.EndsWith(".dds.dds", StringComparison.OrdinalIgnoreCase))
                cleanIcon = cleanIcon[..^4];
            if (!cleanIcon.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
                cleanIcon += ".dds";

            sb.Append($"{wonder.Key} = {{\n");
            sb.Append($"\ttype_icon = \"{cleanIcon}\"\n");
            sb.Append("\tconstruction_time = very_slow_construction_time\n");
            sb.Append("\ttype = special\n");
            sb.Append("\tcost_gold = 1000\n\n");

            // Correct Scope Block:
            sb.Append("\tcan_construct_potential = {\n");
            sb.Append("\t\tbarony = {\n");
            sb.Append($"\t\t\tthis = title:{wonder.Barony.Key}\n");
            sb.Append("\t\t}\n");
            sb.Append("\t}\n\n");

            sb.Append("\tis_enabled = {\n");
            sb.Append("\t\talways = yes\n");
            sb.Append("\t}\n\n");

            if (wonder.CharacterModifiers.Count > 0)
            {
                sb.Append("\tcharacter_modifier = {\n");
                foreach (var (k, v) in wonder.CharacterModifiers)
                    sb.Append($"\t\t{k} = {v}\n");
                sb.Append("\t}\n\n");
            }

            if (wonder.CountyModifiers.Count > 0)
            {
                sb.Append("\tcounty_modifier = {\n");
                foreach (var (k, v) in wonder.CountyModifiers)
                    sb.Append($"\t\t{k} = {v}\n");
                sb.Append("\t}\n\n");
            }

            if (wonder.ProvinceModifiers.Count > 0)
            {
                sb.Append("\tprovince_modifier = {\n");
                foreach (var (k, v) in wonder.ProvinceModifiers)
                    sb.Append($"\t\t{k} = {v}\n");
                sb.Append("\t}\n\n");
            }

            sb.Append("\tai_value = {\n");
            sb.Append("\t\tbase = 150\n");
            sb.Append("\t}\n");
            sb.Append("}\n\n");
        }

        ParadoxText.WriteBom(Path.Combine(dir, "01_generated_wonders.txt"), sb.ToString());
    }

    private static void WriteUniversityTriggers(string modDir, WorldCenterMap worldCenters)
    {
        var libraries = worldCenters.Centers
            .Where(c => c.Wonder.Archetype == WonderArchetype.GreatLibrary)
            .ToList();

        if (libraries.Count == 0) return;

        string dir = Path.Combine(modDir, "common", "scripted_triggers");
        Directory.CreateDirectory(dir);

        var sb = new StringBuilder();
        sb.Append("# Overrides has_university_building_trigger to include generated Great Libraries\n\n");
        sb.Append("has_university_building_trigger = {\n");
        sb.Append("\tOR = {\n");
        sb.Append("\t\thas_building_or_higher = generic_university\n");

        foreach (var lib in libraries)
        {
            sb.Append($"\t\thas_building = {lib.Wonder.Key}\n");
        }

        sb.Append("\t}\n");
        sb.Append("}\n");

        ParadoxText.WriteBom(Path.Combine(dir, "zz_generated_university_triggers.txt"), sb.ToString());
    }

    private static void WriteLocalisation(string modDir, WorldCenterMap worldCenters)
    {
        string dir = Path.Combine(modDir, "localization", "english");
        Directory.CreateDirectory(dir);

        var sb = new StringBuilder();
        sb.Append("l_english:\n");

        foreach (var center in worldCenters.Centers)
        {
            var wonder = center.Wonder;

            // Main building name & description
            sb.Append($" building_{wonder.Key}:0 \"{wonder.Name}\"\n");
            sb.Append($" building_{wonder.Key}_desc:0 \"{wonder.Description}\"\n");

            // Building type/category subtitle & description
            sb.Append($" building_type_{wonder.Key}:0 \"{wonder.Name}\"\n");
            sb.Append($" building_type_{wonder.Key}_desc:0 \"{wonder.Description}\"\n");
        }

        ParadoxText.WriteBom(Path.Combine(dir, "gen_wonders_l_english.yml"), sb.ToString());
    }
}