using System.Globalization;
using System.Text;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

public static class EthnicityWriter
{
    public static void WriteAll(string modDir, EthnicityMap ethnicityMap)
    {
        string dir = Path.Combine(modDir, "common", "ethnicities");
        Directory.CreateDirectory(dir);

        var sb = new StringBuilder();
        sb.Append("# Generated Procedural & Fantasy Ethnicities (Bases & Variants)\n\n");

        foreach (var eth in ethnicityMap.Ethnicities.Values)
        {
            // -----------------------------------------------------------------
            // 1. Base Ethnicity (Morphs + Complexion/Skin + Fallback Hair/Eyes)
            // -----------------------------------------------------------------
            sb.Append($"{eth.Key} = {{\n");
            sb.Append($"\ttemplate = \"{eth.BaseTemplate}\"\n");
            if (eth.Variants.Count > 0)
            {
                sb.Append("\tvisible = no\n");
            }
            sb.Append('\n');

            // Color overrides (Skin, Base Hair, Eye palette vectors)
            foreach (var (colorType, palettes) in eth.ColorGenes)
            {
                sb.Append($"\t{colorType} = {{\n");
                foreach (var pal in palettes)
                {
                    string x1 = pal.X1.ToString("0.###", CultureInfo.InvariantCulture);
                    string y1 = pal.Y1.ToString("0.###", CultureInfo.InvariantCulture);
                    string x2 = pal.X2.ToString("0.###", CultureInfo.InvariantCulture);
                    string y2 = pal.Y2.ToString("0.###", CultureInfo.InvariantCulture);
                    sb.Append($"\t\t{pal.Weight} = {{ {x1} {y1} {x2} {y2} }}\n");
                }
                sb.Append("\t}\n\n");
            }

            // Morph overrides (Facial sculpting, height, body composition)
            foreach (var (geneKey, entries) in eth.MorphGenes)
            {
                sb.Append($"\t{geneKey} = {{\n");
                foreach (var entry in entries)
                {
                    string min = entry.Min.ToString("0.###", CultureInfo.InvariantCulture);
                    string max = entry.Max.ToString("0.###", CultureInfo.InvariantCulture);
                    sb.Append($"\t\t{entry.Weight} = {{ name = {entry.SubGeneName} range = {{ {min} {max} }} }}\n");
                }
                sb.Append("\t}\n\n");
            }

            sb.Append("}\n\n");

            // -----------------------------------------------------------------
            // 2. Child Palette Variants (Hair/Eye Color leans inheriting base)
            // -----------------------------------------------------------------
            foreach (var variant in eth.Variants)
            {
                sb.Append($"{variant.Key} = {{\n");
                sb.Append($"\ttemplate = \"{eth.Key}\"\n\n");

                foreach (var (colorType, palettes) in variant.ColorGenes)
                {
                    sb.Append($"\t{colorType} = {{\n");
                    foreach (var pal in palettes)
                    {
                        string x1 = pal.X1.ToString("0.###", CultureInfo.InvariantCulture);
                        string y1 = pal.Y1.ToString("0.###", CultureInfo.InvariantCulture);
                        string x2 = pal.X2.ToString("0.###", CultureInfo.InvariantCulture);
                        string y2 = pal.Y2.ToString("0.###", CultureInfo.InvariantCulture);
                        sb.Append($"\t\t{pal.Weight} = {{ {x1} {y1} {x2} {y2} }}\n");
                    }
                    sb.Append("\t}\n\n");
                }

                sb.Append("}\n\n");
            }
        }

        ParadoxText.WriteBom(Path.Combine(dir, "99_generated_ethnicities.txt"), sb.ToString());
        WriteLocalisation(modDir, ethnicityMap);

        int totalVariants = ethnicityMap.Ethnicities.Values.Sum(e => e.Variants.Count);
        Console.WriteLine($"  ethnicities written: {ethnicityMap.Ethnicities.Count} base templates and {totalVariants} child variants to 99_generated_ethnicities.txt");
    }

    private static void WriteLocalisation(string modDir, EthnicityMap ethnicityMap)
    {
        string dir = Path.Combine(modDir, "localization", "english");
        Directory.CreateDirectory(dir);

        var sb = new StringBuilder();
        sb.Append("l_english:\n");

        foreach (var eth in ethnicityMap.Ethnicities.Values)
        {
            sb.Append($" {eth.Key}:0 \"{ParadoxText.Loc(eth.LocalizedName)}\"\n");

            foreach (var variant in eth.Variants)
            {
                sb.Append($" {variant.Key}:0 \"{ParadoxText.Loc(variant.LocalizedName)}\"\n");
            }
        }

        ParadoxText.WriteBom(Path.Combine(dir, "gen_ethnicities_l_english.yml"), sb.ToString());
    }
}