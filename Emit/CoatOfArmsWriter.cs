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

    /// <summary>
    /// The marks a cadet lays over its father's arms. Every one of them is a real brisure — the
    /// label of Orleans, the bordure of Valois, the bend of Bourbon, the canton — and every one is
    /// drawn by vanilla full frame at the same position and scale as the arms beneath, which is why
    /// none of them needs geometry of its own.
    /// </summary>
    private static readonly string[] VerifiedBrisures =
    [
        "ce_label_03.dds",
        "ce_border_shield.dds",
        "ce_ordinary_bend_dexter_5.dds",
        "ce_ordinary_canton.dds"
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

        // Every house is written arms, including a main house that would inherit its dynasty's
        // anyway. In game the inheritance is real — House Capet flies the Robertian arms in vanilla
        // without defining any — but the bookmark and challenge-character screens do not make that
        // fallback, and draw a blank shield for a house with nothing of its own.
        // Each cadet of a dynasty is numbered, and its mark is read off that number rather than
        // rolled from its own key. Rolled, two branches of one house could draw the same mark in
        // the same colour and come out as the same shield — rare, but the point of a difference is
        // that it cannot happen. Ordered by key rather than by however the dictionary enumerates,
        // so a seed always assigns the same marks to the same branches.
        var cadetNumber = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var branches in prehistory.Houses.Values
                     .Where(h => h.IsCadet)
                     .GroupBy(h => h.DynastyId, StringComparer.Ordinal))
        {
            int n = 0;
            foreach (var branch in branches.OrderBy(h => h.Key, StringComparer.Ordinal))
                cadetNumber[branch.Key] = n++;
        }

        foreach (var house in prehistory.Houses.Values)
        {
            // The parent's arms either way, rolled from the dynasty's key — the same seed the loop
            // above just used — so House Za'go comes out as Dynasty Za'go rather than as a second
            // shield for one family.
            var parent = new Rng(Rng.StableHash(house.DynastyId) ^ 0x51A3UL);

            // A cadet then differences them. Which is how it was done: the head of a house bore the
            // plain coat and bearing it was the claim to be head, so everyone else bore the same
            // arms with a difference. These branches split inside the living memory of the man
            // holding them — there has been no time for arms of their own, only for a mark laid
            // over their father's.
            int? difference = house.IsCadet ? cadetNumber[house.Key] : null;

            AppendCoa(b, house.Key, parent, difference);
        }

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_coas.txt"), b.ToString());
    }

    /// <param name="difference">
    /// A cadet's number within its dynasty, which picks its tinctures and then its brisure, or null
    /// for arms borne plain. A number rather than a stream of its own for two reasons: numbering is
    /// what makes two branches of one dynasty unable to come out alike, and the arms underneath
    /// have to match the parent's, so nothing may walk the stream that produced them.
    ///
    /// Differences run out after a full turn of the palette times the marks — twenty-eight branches
    /// of one dynasty, which no realm on a generated map comes near.
    /// </param>
    /// <summary>The same colour a fixed number of steps along the palette, wrapping.</summary>
    private static string Rotate(string color, int steps)
        => VerifiedColors[(Array.IndexOf(VerifiedColors, color) + steps) % VerifiedColors.Length];

    private static void AppendCoa(JominiBuilder b, string key, Rng rng, int? difference = null)
    {
        string pattern = VerifiedPatterns[rng.Int(0, VerifiedPatterns.Length - 1)];
        string c1 = VerifiedColors[rng.Int(0, VerifiedColors.Length - 1)];
        string c2 = VerifiedColors[rng.Int(0, VerifiedColors.Length - 1)];
        while (c2 == c1) c2 = VerifiedColors[rng.Int(0, VerifiedColors.Length - 1)];

        string emblem = VerifiedEmblems[rng.Int(0, VerifiedEmblems.Length - 1)];
        string emblemColor = VerifiedColors[rng.Int(0, VerifiedColors.Length - 1)];
        while (emblemColor == c1) emblemColor = VerifiedColors[rng.Int(0, VerifiedColors.Length - 1)];

        string? brisure = null;
        string? brisureColor = null;

        if (difference is int n)
        {
            // Tincture first, mark second, and that order is the whole of it. A branch that keeps
            // its parent's colours and differs only by a small charge is a difference you can prove
            // and cannot see: at the size a shield is drawn in the house list, the two read as one
            // house. Changing the colours was a real difference too — arms were differenced by
            // tincture as readily as by a mark — and it is the one that carries across a room.
            //
            // The field division and the charge are left alone, so the branch still reads as the
            // same arms in another livery rather than as an unrelated family.
            int liveries = VerifiedColors.Length - 1;   // every rotation but the identity one

            // Rotating the whole palette by one step preserves every inequality it was built with:
            // if the charge told against the field before, it still does. So the contrast the
            // parent's arms were given survives, and none of it has to be re-checked.
            int livery = 1 + n % liveries;
            c1 = Rotate(c1, livery);
            c2 = Rotate(c2, livery);
            emblemColor = Rotate(emblemColor, livery);

            // Then the mark, which now only has to tell two branches apart from each other — they
            // have already been told apart from their father by the colours.
            brisure = VerifiedBrisures[n / liveries % VerifiedBrisures.Length];

            // Against both halves of the field and against the charge it is laid over. A mark the
            // colour of any of the three disappears into it, and on a divided field it disappears
            // into half of it — which is worse, because it reads as a mark that has been damaged
            // rather than as one that is not there.
            string[] readable = VerifiedColors
                .Where(c => c != c1 && c != c2 && c != emblemColor)
                .ToArray();

            brisureColor = readable[n % readable.Length];
        }

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

            // Last, so it sits over the charge rather than under it.
            if (brisure is not null)
            {
                using (b.Block("colored_emblem"))
                {
                    b.Quoted("texture", brisure);
                    b.Quoted("color1", brisureColor!);
                    b.Inline("instance", "position = { 0.5 0.5 } scale = { 1.0 1.0 }");
                }
            }
        }

        b.Blank();
    }
}