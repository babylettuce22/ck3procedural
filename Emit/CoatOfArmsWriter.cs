// Emit/CoatOfArmsWriter.cs
namespace Ck3MapGen.Emit;

using System.IO;
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

        var b = new JominiBuilder();
        b.Comment("Generated Dynasty and House Coats of Arms for 3D Court Banners and Shields");
        b.Blank();

        foreach (var dyn in prehistory.Dynasties.Values)
        {
            var rng = new Rng(Rng.StableHash(dyn.Id) ^ 0x51A3UL);
            AppendCoa(b, dyn.Id, rng);
        }

        foreach (var house in prehistory.Houses.Values)
        {
            var rng = new Rng(Rng.StableHash(house.Key) ^ 0x27C1UL);
            AppendCoa(b, house.Key, rng);
        }

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_coas.txt"), b.ToString());
    }

    private static void AppendCoa(JominiBuilder b, string key, Rng rng)
    {
        string pattern = VerifiedPatterns[rng.Int(0, VerifiedPatterns.Length - 1)];
        string c1 = VerifiedColors[rng.Int(0, VerifiedColors.Length - 1)];
        string c2 = VerifiedColors[rng.Int(0, VerifiedColors.Length - 1)];
        while (c2 == c1) c2 = VerifiedColors[rng.Int(0, VerifiedColors.Length - 1)];

        string emblem = VerifiedEmblems[rng.Int(0, VerifiedEmblems.Length - 1)];
        string emblemColor = VerifiedColors[rng.Int(0, VerifiedColors.Length - 1)];
        while (emblemColor == c1) emblemColor = VerifiedColors[rng.Int(0, VerifiedColors.Length - 1)];

        using (b.Block(key))
        {
            b.Quoted("pattern", pattern);
            b.Quoted("color1", c1);
            b.Quoted("color2", c2);

            using (b.Block("colored_emblem"))
            {
                b.Quoted("texture", emblem);
                b.Quoted("color1", emblemColor);
                b.Inline("instance", "position = { 0.5 0.5 } scale = { 0.75 0.75 }");
            }
        }

        b.Blank();
    }
}