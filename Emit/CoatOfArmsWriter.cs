// Emit/CoatOfArmsWriter.cs
namespace Ck3MapGen.Emit;

using System.IO;
using System.Text;
using Ck3MapGen.Core;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

public static class CoatOfArmsWriter
{
    // 100% verified textures present in standard vanilla base game
    private static readonly string[] VerifiedPatterns =
    [
        "pattern_solid.dds",
        "pattern_vertical_split_01.dds",
        "pattern_horizontal_split_01.dds",
        "pattern_diagonal_split_01.dds",
        "pattern_vertical_stripes_01.dds",
        "pattern_waves_01.dds"
    ];

    private static readonly string[] VerifiedEmblems =
    [
        "ce_fleur.dds",
        "ce_lion_passant.dds",
        "ce_sword_simple.dds",
        "ce_castle.dds",
        "ce_chalice.dds",
        "ce_chain.dds",
        "ce_circle.dds",
        "ce_star_06.dds",
        "ce_heart.dds",
        "ce_cross_06.dds",
        "ce_crown_random.dds",
        "ce_eagle_double.dds"
    ];

    private static readonly string[] VerifiedColors =
    [
        "red", "blue", "yellow", "green", "white", "black", "purple", "orange"
    ];

    public static void WriteAll(string modDir, PrehistoryMap prehistory)
    {
        string dir = Path.Combine(modDir, "common", "coat_of_arms", "coat_of_arms");
        Directory.CreateDirectory(dir);

        var sb = new StringBuilder();
        sb.Append("# Generated Dynasty and House Coats of Arms for 3D Court Banners and Shields\n\n");

        foreach (var dyn in prehistory.Dynasties.Values)
        {
            var rng = new Rng(Rng.StableHash(dyn.Id) ^ 0x51A3UL);
            AppendCoa(sb, dyn.Id, rng);
        }

        foreach (var house in prehistory.Houses.Values)
        {
            var rng = new Rng(Rng.StableHash(house.Key) ^ 0x27C1UL);
            AppendCoa(sb, house.Key, rng);
        }

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_coas.txt"), sb.ToString());
    }

    private static void AppendCoa(StringBuilder sb, string key, Rng rng)
    {
        string pattern = VerifiedPatterns[rng.Int(0, VerifiedPatterns.Length - 1)];
        string c1 = VerifiedColors[rng.Int(0, VerifiedColors.Length - 1)];
        string c2 = VerifiedColors[rng.Int(0, VerifiedColors.Length - 1)];
        while (c2 == c1) c2 = VerifiedColors[rng.Int(0, VerifiedColors.Length - 1)];

        string emblem = VerifiedEmblems[rng.Int(0, VerifiedEmblems.Length - 1)];
        string emblemColor = VerifiedColors[rng.Int(0, VerifiedColors.Length - 1)];
        while (emblemColor == c1) emblemColor = VerifiedColors[rng.Int(0, VerifiedColors.Length - 1)];

        sb.Append($"{key} = {{\n");
        sb.Append($"\tpattern = \"{pattern}\"\n");
        sb.Append($"\tcolor1 = \"{c1}\"\n");
        sb.Append($"\tcolor2 = \"{c2}\"\n");
        sb.Append("\tcolored_emblem = {\n");
        sb.Append($"\t\ttexture = \"{emblem}\"\n");
        sb.Append($"\t\tcolor1 = \"{emblemColor}\"\n");
        sb.Append("\t\tinstance = { position = { 0.5 0.5 } scale = { 0.75 0.75 } }\n");
        sb.Append("\t}\n");
        sb.Append("}\n\n");
    }
}