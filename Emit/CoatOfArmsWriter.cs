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

    /// <summary>The textures and tinctures the generator is sure of, for the inspectors' dropdowns.</summary>
    public static IReadOnlyList<string> Patterns => VerifiedPatterns;
    public static IReadOnlyList<string> Emblems => VerifiedEmblems;
    public static IReadOnlyList<string> Colors => VerifiedColors;

    /// <summary>
    /// One shield as it is written: the field's pattern and two tinctures, the charge and its
    /// colour, and for a cadet the brisure laid over it. A record so that an edit can hold the
    /// generated one beside the changed one and compare them.
    /// </summary>
    public sealed record Coat(string Pattern, string Color1, string Color2, string Emblem, string EmblemColor,
        string? Brisure = null, string? BrisureColor = null);

    /// <summary>
    /// Every dynasty's and house's arms as the write would roll them, without writing. What the
    /// ruler inspector shows before an edit, and the base an edit is compared against.
    /// </summary>
    public static Dictionary<string, Coat> Compose(PrehistoryMap prehistory)
    {
        var coats = new Dictionary<string, Coat>(StringComparer.Ordinal);
        foreach (var dyn in prehistory.Dynasties.Values)
            coats[dyn.Id] = Compose(new Rng(Rng.StableHash(dyn.Id) ^ 0x51A3UL));

        var cadetNumber = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var branches in prehistory.Houses.Values.Where(h => h.IsCadet).GroupBy(h => h.DynastyId, StringComparer.Ordinal))
        {
            int n = 0;
            foreach (var branch in branches.OrderBy(h => h.Key, StringComparer.Ordinal)) cadetNumber[branch.Key] = n++;
        }
        foreach (var house in prehistory.Houses.Values)
            coats[house.Key] = Compose(new Rng(Rng.StableHash(house.DynastyId) ^ 0x51A3UL), house.IsCadet ? cadetNumber[house.Key] : null);
        return coats;
    }

    /// <param name="overrides">Arms changed in the inspector after the write, by owner key; the rest are rolled as before.</param>
    public static void WriteAll(string modDir, PrehistoryMap prehistory, IReadOnlyDictionary<string, Coat>? overrides = null)
    {
        string dir = Path.Combine(modDir, "common", "coat_of_arms", "coat_of_arms");
        Directory.CreateDirectory(dir);

        var b = new JominiBuilder();
        b.Comment("Generated Dynasty and House Coats of Arms for 3D Court Banners and Shields");
        b.Blank();

        foreach (var dyn in prehistory.Dynasties.Values)
        {
            var rng = new Rng(Rng.StableHash(dyn.Id) ^ 0x51A3UL);
            AppendCoa(b, dyn.Id, rng, overrides: overrides);
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

            AppendCoa(b, house.Key, parent, difference, overrides);
        }

        // The noble family titles, each bearing the arms of the house it is the family of.
        //
        // Written out in full rather than left to inherit, because there is nothing to inherit
        // from: a title takes its arms from its own key or from a dynasty it is named after, and a
        // family title is named after neither. Vanilla gets away with declaring none because
        // noble_family_title_realm_setup_effect calls set_coa on every one at game start — but that
        // effect runs only from the Byzantine and Japanese blocks in game_start.txt, so nothing
        // would call it for a generated realm and every family would fly a blank shield.
        //
        // Same seed and same difference as the loop above, so a family and its house come out as
        // one shield rather than two.
        foreach (var family in prehistory.NobleFamilies)
        {
            if (!prehistory.Houses.TryGetValue(family.HouseKey, out var house)) continue;

            var parent = new Rng(Rng.StableHash(house.DynastyId) ^ 0x51A3UL);
            int? difference = house.IsCadet ? cadetNumber[house.Key] : null;

            AppendCoa(b, family.TitleKey, parent, difference, overrides, overrideKey: house.Key);
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

    /// <param name="overrideKey">
    /// Whose edited arms to honour, when that is not the block being written. A noble family title
    /// bears its house's arms and has none of its own, so an edit to the house has to reach it —
    /// looking the override up under the title key would leave the family flying the rolled coat
    /// while the house it names flew the edited one.
    /// </param>
    private static void AppendCoa(JominiBuilder b, string key, Rng rng, int? difference = null,
        IReadOnlyDictionary<string, Coat>? overrides = null, string? overrideKey = null)
    {
        // Rolled even when overridden, so the stream stays where it was for whoever draws next.
        var rolled = Compose(rng, difference);
        var coat = overrides is not null && overrides.TryGetValue(overrideKey ?? key, out var edited) ? edited : rolled;

        using (b.Block(key))
        {
            b.Quoted("pattern", coat.Pattern);
            b.Quoted("color1", coat.Color1);
            b.Quoted("color2", coat.Color2);

            using (b.Block("colored_emblem"))
            {
                b.Quoted("texture", coat.Emblem);
                b.Quoted("color1", coat.EmblemColor);
                b.Inline("instance", "position = { 0.5 0.5 } scale = { 0.75 0.75 }");
            }

            // Last, so it sits over the charge rather than under it.
            if (coat.Brisure is not null)
            {
                using (b.Block("colored_emblem"))
                {
                    b.Quoted("texture", coat.Brisure);
                    b.Quoted("color1", coat.BrisureColor ?? coat.EmblemColor);
                    b.Inline("instance", "position = { 0.5 0.5 } scale = { 1.0 1.0 }");
                }
            }
        }

        b.Blank();
    }

    /// <summary>
    /// The hegemony's own arms, and the reason it needs any.
    ///
    /// The generated hegemony is keyed <c>h_china</c> (<see cref="Titles.HegemonyKey"/>) so that
    /// All Under Heaven's several hundred <c>title:h_china</c> references resolve to it. Vanilla
    /// arms that key in <c>common/coat_of_arms/coat_of_arms/01_landed_titles.txt</c>: black,
    /// yellow and red under a constellation, two celestial bodies and the imperial dragon. Nothing
    /// here wrote title arms at all — every other generated title takes a random shield from the
    /// engine — so the one title that names a fifth of the world flew China's.
    ///
    /// Written twice on purpose, and the two copies do different jobs:
    ///
    /// * <c>h_china</c> is the override. Its folder has no subdirectories, so this database is
    ///   settled by plain asciibetical filename order (see the note in Emit/ContentWriter on how
    ///   depth beats filename where there ARE subdirectories) and <c>zzz_</c> sorts past vanilla's
    ///   <c>99_</c>. This is the copy the bookmark screen reads, before any effect has run.
    /// * <c>gen_hegemony_coa</c> is the same shield under a key nothing contests, reassigned at
    ///   game start by <c>set_coa</c> — vanilla's own idiom for this, used in game_start.txt for
    ///   the Norse variants of Scandinavia. It costs one line and it does not care about load
    ///   order, so the arms hold even if a future patch reshuffles the folder.
    ///
    /// Seeded from the hegemony's generated NAME rather than its key: the key is the same string
    /// in every world, so seeding from it would give every hegemony ever generated one shield.
    /// </summary>
    public static void WriteHegemonyArms(string modDir, Title hegemony)
    {
        string dir = Path.Combine(modDir, "common", "coat_of_arms", "coat_of_arms");
        Directory.CreateDirectory(dir);

        var b = new JominiBuilder();
        b.Comment($"Arms for the generated hegemony {hegemony.Key} (\"{hegemony.Name}\").");
        b.Comment("Replaces vanilla's Chinese arms on that key; see Emit/CoatOfArmsWriter.cs.");
        b.Blank();

        var coat = ComposeImperial(new Rng(Rng.StableHash(hegemony.Name) ^ 0x9E5EU));

        AppendImperialCoa(b, hegemony.Key, coat);
        AppendImperialCoa(b, HegemonyCoaKey, coat);

        ParadoxText.WriteBom(Path.Combine(dir, "zzz_gen_title_coas.txt"), b.ToString());
    }

    /// <summary>The uncontested key <c>set_coa</c> points at; see <see cref="WriteHegemonyArms"/>.</summary>
    public const string HegemonyCoaKey = "gen_hegemony_coa";

    /// <summary>
    /// A shield built to read as a crown's, not a house's: the charge sits low and a crown rides
    /// above it. Both are drawn from the same verified textures every other coat here uses, so
    /// nothing new has to be proven present in the base game.
    /// </summary>
    private static Coat ComposeImperial(Rng rng)
    {
        var coat = Compose(rng);

        // Not the charge again. A crown over a crown reads as a mistake, and ce_crown_random is in
        // the emblem pool, so it can be rolled as the charge.
        string crown = coat.Emblem == "ce_crown_random.dds" ? "ce_star_06.dds" : "ce_crown_random.dds";

        // Against both halves of the field and against the charge, on the same reasoning as a
        // cadet's brisure: a crown the colour of anything under it is a crown you cannot see.
        string[] readable = VerifiedColors
            .Where(c => c != coat.Color1 && c != coat.Color2 && c != coat.EmblemColor)
            .ToArray();

        return coat with { Brisure = crown, BrisureColor = readable[rng.Int(0, readable.Length - 1)] };
    }

    /// <summary>
    /// The imperial layout. Not <see cref="AppendCoa"/>, which centres its charge and lays a
    /// cadet's mark straight over it at full size — here the two must not overlap, so the charge
    /// drops and shrinks and the crown takes the chief.
    /// </summary>
    private static void AppendImperialCoa(JominiBuilder b, string key, Coat coat)
    {
        using (b.Block(key))
        {
            b.Quoted("pattern", coat.Pattern);
            b.Quoted("color1", coat.Color1);
            b.Quoted("color2", coat.Color2);

            using (b.Block("colored_emblem"))
            {
                b.Quoted("texture", coat.Emblem);
                b.Quoted("color1", coat.EmblemColor);
                b.Inline("instance", "position = { 0.5 0.58 } scale = { 0.6 0.6 }");
            }

            using (b.Block("colored_emblem"))
            {
                b.Quoted("texture", coat.Brisure!);
                b.Quoted("color1", coat.BrisureColor!);
                b.Inline("instance", "position = { 0.5 0.17 } scale = { 0.34 0.34 } depth = 2.0");
            }
        }

        b.Blank();
    }

    private static Coat Compose(Rng rng, int? difference = null)
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

        return new Coat(pattern, c1, c2, emblem, emblemColor, brisure, brisureColor);
    }
}