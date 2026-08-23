using System.Globalization;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

public static class EthnicityWriter
{
    public static void WriteAll(string modDir, EthnicityMap ethnicityMap)
    {
        string dir = Path.Combine(modDir, "common", "ethnicities");
        Directory.CreateDirectory(dir);

        var b = new JominiBuilder();
        b.Comment("Generated Procedural & Fantasy Ethnicities (Bases & Variants)");
        b.Blank();

        static string G(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        // The palette blocks are identical between a base ethnicity and its variants -- the variant
        // simply overrides fewer of them -- so they are written once here.
        void ColorGenes(Dictionary<string, List<ColorPaletteRange>> genes)
        {
            foreach (var (colorType, palettes) in genes)
            {
                using (b.Block(colorType))
                    foreach (var pal in palettes)
                        b.Inline($"{pal.Weight}", G(pal.X1), G(pal.Y1), G(pal.X2), G(pal.Y2));

                b.Blank();
            }
        }

        foreach (var eth in ethnicityMap.Ethnicities.Values)
        {
            // -----------------------------------------------------------------
            // 1. Base Ethnicity (Morphs + Complexion/Skin + Fallback Hair/Eyes)
            // -----------------------------------------------------------------
            using (b.Block(eth.Key))
            {
                b.Quoted("template", eth.BaseTemplate);

                // A base with variants is never picked directly; the variants carry the look.
                if (eth.Variants.Count > 0) b.Field("visible", "no");

                b.Blank();

                // Color overrides (Skin, Base Hair, Eye palette vectors)
                ColorGenes(eth.ColorGenes);

                // Morph overrides (Facial sculpting, height, body composition)
                foreach (var (geneKey, entries) in eth.MorphGenes)
                {
                    using (b.Block(geneKey))
                        foreach (var entry in entries)
                            b.Inline($"{entry.Weight}",
                                $"name = {entry.SubGeneName} range = {{ {G(entry.Min)} {G(entry.Max)} }}");

                    b.Blank();
                }
            }

            b.Blank();

            // -----------------------------------------------------------------
            // 2. Child Palette Variants (Hair/Eye Color leans inheriting base)
            // -----------------------------------------------------------------
            foreach (var variant in eth.Variants)
            {
                using (b.Block(variant.Key))
                {
                    b.Quoted("template", eth.Key);
                    b.Blank();

                    ColorGenes(variant.ColorGenes);
                }

                b.Blank();
            }
        }

        ParadoxText.WriteBom(Path.Combine(dir, "99_generated_ethnicities.txt"), b.ToString());
        WriteLocalisation(modDir, ethnicityMap);

        int totalVariants = ethnicityMap.Ethnicities.Values.Sum(e => e.Variants.Count);
        Console.WriteLine($"  ethnicities written: {ethnicityMap.Ethnicities.Count} base templates and {totalVariants} child variants to 99_generated_ethnicities.txt");
    }

    private static void WriteLocalisation(string modDir, EthnicityMap ethnicityMap)
    {
        var loc = new LocFile();

        foreach (var eth in ethnicityMap.Ethnicities.Values)
        {
            loc.Add(eth.Key, eth.LocalizedName);
            foreach (var variant in eth.Variants) loc.Add(variant.Key, variant.LocalizedName);
        }

        loc.Write(Path.Combine(modDir, "localization", "english", "gen_ethnicities_l_english.yml"));
    }
}