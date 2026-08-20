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

            // No hairstyles or beards block, and there is no version of this file that has one.
            //
            // An ethnicity cannot set either. CK3 rejects the attempt outright — "gene category
            // 'hairstyles' cannot be influenced by DNA" (ethnicity.cpp:304), once per category per
            // ethnicity — because those are accessory genes, chosen by portrait modifiers rather
            // than inherited through ethnicity, and no vanilla ethnicity declares one. There is no
            // correct syntax to switch to; this used to emit both blocks and they never did anything.
            //
            // The failure mode was quiet and worth remembering: a rejected block does not fall back
            // to the author's intent, it falls back to the base `template` ethnicity. So every
            // generated culture drew vanilla's default hair while a table of curated per-race
            // weights sat in MapGen/Ethnicities.cs having no effect whatsoever.
            //
            // Nothing replaced it, and nothing needs to. Hair is decided by vanilla's own hairstyle
            // modifiers in gfx/portraits/portrait_modifiers/01_hairstyles_base.txt, which key off
            // clothing_gfx crossed with the character's gene_hair_type — and gene_hair_type IS
            // something an ethnicity may legally set. A generated culture borrows a whole vanilla
            // look (VanillaVocabulary.Look), and vanilla writes clothing_gfx as an ordered fallback
            // chain — `{ afr_berber_clothing_gfx mena_clothing_gfx }`, `{ breton_clothing_gfx
            // western_clothing_gfx }` — so the borrowed chain always contains a gfx some hairstyle
            // modifier gates on. Region-appropriate hair therefore arrives for free.
            //
            // Beard SHAPE was dropped rather than moved. Vanilla beard templates vary only by
            // texture (`<region>_beards_straight`, `_curly`, `sub_saharan_beards_afro`), so "a goatee
            // for gnomes" cannot be said in this game's data at any layer; `no_beard` is the only
            // style-level control that exists.

            sb.Append("}\n\n");
        }

        ParadoxText.WriteBom(Path.Combine(dir, "99_generated_ethnicities.txt"), sb.ToString());
        WriteLocalisation(modDir, ethnicityMap);
        Console.WriteLine($"  ethnicities written: {ethnicityMap.Ethnicities.Count} custom ethnicity templates to 99_generated_ethnicities.txt");
    }

    /// <summary>
    /// Names for the generated ethnicity keys.
    ///
    /// Every ethnicity is a localisation key in its own right — the ruler designer and the
    /// character-creation screens read it — so without this each generated race shows up as the raw
    /// `gen_ethnicity_3`. <see cref="MapGen.EthnicityDef.LocalizedName"/> has always been built and
    /// was never written anywhere; ck3-tiger reports the gap as one missing-localization warning per
    /// ethnicity.
    /// </summary>
    private static void WriteLocalisation(string modDir, EthnicityMap ethnicityMap)
    {
        string dir = Path.Combine(modDir, "localization", "english");
        Directory.CreateDirectory(dir);

        var sb = new StringBuilder();
        sb.Append("l_english:\n");

        foreach (var eth in ethnicityMap.Ethnicities.Values)
            sb.Append($" {eth.Key}:0 \"{ParadoxText.Loc(eth.LocalizedName)}\"\n");

        ParadoxText.WriteBom(Path.Combine(dir, "gen_ethnicities_l_english.yml"), sb.ToString());
    }
}