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
        sb.Append("# Generated Procedural & Fantasy Ethnicities\n\n");

        foreach (var eth in ethnicityMap.Ethnicities.Values)
        {
            sb.Append($"{eth.Key} = {{\n");
            sb.Append($"\ttemplate = \"{eth.BaseTemplate}\"\n\n");

            // 1. Color overrides (Skin, Hair, Eye palette vectors with multiple weighted swatches)
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

            // 2. Morph overrides (Facial sculpting, height, body composition)
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

            // 3. NOT accessories. `eth.AccessoryGenes` is deliberately not emitted.
            //
            // An ethnicity cannot set hairstyles or beards. CK3 rejects the attempt outright —
            // "gene category 'hairstyles' cannot be influenced by DNA" (ethnicity.cpp:304), once per
            // category per ethnicity — because those are accessory genes, chosen by portrait
            // modifiers and DNA rather than inherited through ethnicity. No vanilla ethnicity
            // declares either block; there is no correct syntax to switch to.
            //
            // Writing them anyway was not merely noisy. A rejected block does not fall back to the
            // author's intent, it falls back to the base `template` ethnicity, so every generated
            // culture silently drew vanilla's default hair and beards while the curated weights in
            // MapGen/Ethnicities.cs ApplyAccessoryGenes had no effect at all.
            //
            // Those weights are still built and still carried on the def, because the intent behind
            // them is real and their only legal home is a portrait_modifier group — the same shape
            // as BaseFilesToCopy/Wilderness/gfx/portraits/portrait_modifiers, whose accessory lines
            // are exactly this kind of override. Nothing reads them until that exists.

            sb.Append("}\n\n");
        }

        ParadoxText.WriteBom(Path.Combine(dir, "99_generated_ethnicities.txt"), sb.ToString());
        Console.WriteLine($"  ethnicities written: {ethnicityMap.Ethnicities.Count} custom ethnicity templates to 99_generated_ethnicities.txt");
    }
}