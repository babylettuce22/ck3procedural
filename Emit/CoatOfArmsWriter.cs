// Emit/CoatOfArmsWriter.cs
namespace Ck3MapGen.Emit;

using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ck3MapGen.Core;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

/// <summary>
/// Arms for every generated dynasty and house, written out whole.
///
/// The arms are vanilla's: rolled by <see cref="VanillaHeraldry"/>, which runs the game's own
/// random-heraldry templates and weighted lists against each family's culture and faith. What this
/// writer adds is the family structure the engine cannot express — a main house bearing its
/// dynasty's arms, and a cadet bearing its father's arms differenced — and writing it all down, so
/// the bookmark screen and the ruler inspector show real shields before the game has run anything.
///
/// Without a game install to read, arms fall back to the small fixed set this writer used before
/// the vanilla rules were read (<see cref="LegacyRoll"/>).
/// </summary>
public static class CoatOfArmsWriter
{
    // =========================================================================================
    // Model
    // =========================================================================================

    /// <summary>One placement of a charge: centre, scale (negative mirrors), rotation, depth.</summary>
    public sealed record Instance(double X, double Y, double ScaleX, double ScaleY, double Rotation = 0, double? Depth = null);

    /// <summary>A colored emblem laid on the field one or more times, in up to three tinctures.</summary>
    public sealed record Charge(string Texture, IReadOnlyList<string> Colors, IReadOnlyList<Instance> Instances, IReadOnlyList<int>? Mask = null)
    {
        public bool Equals(Charge? other)
            => other is not null && Texture == other.Texture && Colors.SequenceEqual(other.Colors)
               && Instances.SequenceEqual(other.Instances) && (Mask ?? []).SequenceEqual(other.Mask ?? []);

        public override int GetHashCode() => HashCode.Combine(Texture, Colors.Count, Instances.Count);
    }

    /// <summary>
    /// One shield as it is written: the field's pattern and tinctures, its charges in drawing
    /// order, and for a cadet the mark laid over them. A record with value equality, so an edit can
    /// be held beside the rolled coat and compared with it.
    ///
    /// <see cref="Color1"/>, <see cref="Color2"/>, <see cref="Emblem"/> and <see cref="EmblemColor"/>
    /// are the inspector's handles on it: the field's two tinctures and the first charge's texture
    /// and tincture. Setting them leaves every other part of the coat as it was.
    /// </summary>
    [JsonConverter(typeof(CoatJsonConverter))]
    public sealed record Coat(string Pattern, IReadOnlyList<string> Colors, IReadOnlyList<Charge> Charges)
    {
        /// <summary>The cadet's mark (or the hegemony's crown), drawn over everything else.</summary>
        public Charge? Brisure { get; init; }

        /// <summary>The vanilla template this coat was rolled from; informational, not compared.</summary>
        public string? Template { get; init; }

        public string Color1
        {
            get => Colors[0];
            init => Colors = Replace(Colors, 0, value);
        }

        public string Color2
        {
            get => Colors.Count > 1 ? Colors[1] : Colors[0];
            init => Colors = Replace(Colors.Count > 1 ? Colors : [.. Colors, Colors[0]], 1, value);
        }

        public string Emblem
        {
            get => Charges.Count > 0 ? Charges[0].Texture : "";
            init => Charges = Charges.Count > 0
                ? Replace(Charges, 0, Charges[0] with { Texture = value })
                : [new Charge(value, [DefaultChargeColour(Colors)], [new Instance(0.5, 0.5, 0.75, 0.75)])];
        }

        public string EmblemColor
        {
            get => Charges.Count > 0 ? Charges[0].Colors[0] : "";
            init
            {
                if (Charges.Count > 0) Charges = Replace(Charges, 0, Charges[0] with { Colors = Replace(Charges[0].Colors, 0, value) });
            }
        }

        public bool Equals(Coat? other)
            => other is not null && Pattern == other.Pattern && Colors.SequenceEqual(other.Colors)
               && Charges.SequenceEqual(other.Charges) && Equals(Brisure, other.Brisure);

        public override int GetHashCode() => HashCode.Combine(Pattern, Colors.Count, Charges.Count);

        private static IReadOnlyList<T> Replace<T>(IReadOnlyList<T> list, int index, T value)
        {
            var copy = list.ToList();
            copy[index] = value;
            return copy;
        }

        private static string DefaultChargeColour(IReadOnlyList<string> field)
            => field.Count > 2 ? field[2] : field[0] == "yellow" ? "white" : "yellow";
    }

    // =========================================================================================
    // What the inspectors offer
    // =========================================================================================

    // Used when no game install can be read. Textures verified present in the base game.
    private static readonly string[] LegacyPatterns =
    [
        "pattern_solid.dds", "pattern_vertical_split_01.dds", "pattern_horizontal_split_01.dds",
        "pattern_diagonal_split_01.dds", "pattern_vertical_stripes_01.dds", "pattern_waves_01.dds",
    ];

    private static readonly string[] LegacyEmblems =
    [
        "ce_fleur.dds", "ce_lion_passant.dds", "ce_sword_simple.dds", "ce_castle.dds", "ce_chalice.dds", "ce_chain.dds",
        "ce_circle.dds", "ce_star_06.dds", "ce_heart.dds", "ce_cross_06.dds", "ce_crown_random.dds", "ce_eagle_double.dds",
    ];

    private static readonly string[] LegacyMetals = ["yellow", "white"];
    private static readonly string[] LegacyColours = ["red", "blue", "green", "black", "purple", "orange"];

    /// <summary>
    /// The marks a cadet lays over its father's arms: vanilla's own cadency list (a bendlet, a
    /// canton, labels plain and charged) behind the bordure, which vanilla draws full frame at the
    /// same scale. Every one is a full-frame emblem, which is why none needs placing.
    ///
    /// The plain label is not first. The mark is picked by <c>n / liveries</c>, so whatever sits
    /// first is borne by a dynasty's first several cadets, and a strip across the top of every
    /// cadet's shield on the map is what the old ordering produced.
    /// </summary>
    private static readonly string[] CadencyMarks =
    [
        "ce_border_shield.dds", "ce_bendlet.dds", "ce_ordinary_canton.dds", "ce_label_03.dds", "ce_label_04.dds",
        "ce_label_05.dds", "ce_label_compony.dds", "ce_label_castles.dds", "ce_label_roundely.dds",
    ];

    public static IReadOnlyList<string> Patterns => VanillaHeraldry.CurrentOrLocate()?.Patterns ?? LegacyPatterns;
    public static IReadOnlyList<string> Emblems => VanillaHeraldry.CurrentOrLocate()?.Emblems ?? LegacyEmblems;
    public static IReadOnlyList<string> Colors => [.. Metals, .. Colours];

    private static IReadOnlyList<string> Metals => VanillaHeraldry.CurrentOrLocate()?.AllMetals ?? LegacyMetals;
    private static IReadOnlyList<string> Colours => VanillaHeraldry.CurrentOrLocate()?.AllColours ?? LegacyColours;

    // =========================================================================================
    // Who each family is
    // =========================================================================================

    /// <summary>
    /// The culture and faith every dynasty's arms are rolled for: the dynasty's culture, and the
    /// faith of its first recorded member. A house is rolled for its dynasty — a cadet's arms are
    /// its father's, differenced, so they must come out of the same roll.
    /// </summary>
    public static Func<string, HeraldryScope> ScopesFor(PrehistoryMap prehistory, CultureMap? cultures, FaithMap? faiths)
    {
        var cultureByKey = new Dictionary<string, Culture>(StringComparer.Ordinal);
        foreach (var c in cultures?.Cultures ?? []) cultureByKey.TryAdd(c.Key, c);
        var faithByKey = new Dictionary<string, Faith>(StringComparer.Ordinal);
        foreach (var f in faiths?.Faiths ?? []) faithByKey.TryAdd(f.Key, f);

        var faithOfDynasty = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var ch in prehistory.AllExtraCharacters) faithOfDynasty.TryAdd(ch.DynastyId, ch.FaithKey);

        var cache = new Dictionary<string, HeraldryScope>(StringComparer.Ordinal);
        return dynastyId =>
        {
            if (cache.TryGetValue(dynastyId, out var hit)) return hit;

            IReadOnlySet<string> gfx = prehistory.Dynasties.TryGetValue(dynastyId, out var dyn)
                                       && cultureByKey.TryGetValue(dyn.CultureKey, out var culture)
                ? HeraldryScope.ParseGfx(culture.CoaGfx)
                : new HashSet<string>();

            var scope = faithOfDynasty.TryGetValue(dynastyId, out var fk) && faithByKey.TryGetValue(fk, out var faith)
                ? new HeraldryScope(gfx, faith.Religion.Key, faith.Religion.FamilyKey,
                    faith.Tenets.Concat(faith.Religion.Doctrines.Values).Concat(faith.DoctrineOverrides.Values)
                         .ToHashSet(StringComparer.Ordinal),
                    faith.Icon)
                : HeraldryScope.None with { CoaGfx = gfx };

            return cache[dynastyId] = scope;
        };
    }

    // =========================================================================================
    // Composing and writing
    // =========================================================================================

    private static Rng SeedFor(string key) => new(Rng.StableHash(key) ^ 0x51A3UL);

    /// <summary>
    /// Every dynasty's and house's arms as the write would roll them, without writing. What the
    /// ruler inspector shows before an edit, and the base an edit is compared against.
    /// </summary>
    public static Dictionary<string, Coat> Compose(PrehistoryMap prehistory, CultureMap? cultures = null, FaithMap? faiths = null)
    {
        var scopes = ScopesFor(prehistory, cultures, faiths);
        var coats = new Dictionary<string, Coat>(StringComparer.Ordinal);
        foreach (var dyn in prehistory.Dynasties.Values)
            coats[dyn.Id] = Roll(SeedFor(dyn.Id), scopes(dyn.Id));

        var cadetNumber = CadetNumbers(prehistory);
        foreach (var house in prehistory.Houses.Values)
            coats[house.Key] = Roll(SeedFor(house.DynastyId), scopes(house.DynastyId),
                house.IsCadet ? cadetNumber[house.Key] : null);
        return coats;
    }

    /// <param name="overrides">Arms changed in the inspector after the write, by owner key; the rest are rolled as before.</param>
    public static void WriteAll(string modDir, PrehistoryMap prehistory, IReadOnlyDictionary<string, Coat>? overrides = null,
        CultureMap? cultures = null, FaithMap? faiths = null)
    {
        string dir = Path.Combine(modDir, "common", "coat_of_arms", "coat_of_arms");
        Directory.CreateDirectory(dir);

        var scopes = ScopesFor(prehistory, cultures, faiths);
        var templatesUsed = new HashSet<string>(StringComparer.Ordinal);
        int written = 0;

        var b = new JominiBuilder();
        b.Comment("Generated Dynasty and House Coats of Arms for 3D Court Banners and Shields.");
        b.Comment("Rolled from vanilla's own heraldry templates for each family's culture and faith; see Emit/VanillaHeraldry.cs.");
        b.Blank();

        void Append(string key, Coat rolled, string? overrideKey = null)
        {
            var coat = overrides is not null && overrides.TryGetValue(overrideKey ?? key, out var edited) ? edited : rolled;
            if (coat.Template is { } t) templatesUsed.Add(t);
            AppendCoa(b, key, coat);
            written++;
        }

        foreach (var dyn in prehistory.Dynasties.Values)
            Append(dyn.Id, Roll(SeedFor(dyn.Id), scopes(dyn.Id)));

        // Every house is written arms, including a main house that would inherit its dynasty's
        // anyway. In game the inheritance is real — House Capet flies the Robertian arms in vanilla
        // without defining any — but the bookmark and challenge-character screens do not make that
        // fallback, and draw a blank shield for a house with nothing of its own.
        //
        // Each cadet of a dynasty is numbered, and its difference read off that number rather than
        // rolled from its own key. Rolled, two branches of one house could draw the same difference
        // and come out as the same shield — rare, but the point of a difference is that it cannot
        // happen. Ordered by key, so a seed always assigns the same differences to the same branches.
        var cadetNumber = CadetNumbers(prehistory);

        foreach (var house in prehistory.Houses.Values)
        {
            // The parent's arms either way, rolled from the dynasty's key — the same seed and scope
            // the loop above used — so House Za'go comes out as Dynasty Za'go rather than as a
            // second shield for one family. A cadet then differences them. Which is how it was
            // done: the head of a house bore the plain coat and bearing it was the claim to be head,
            // so everyone else bore the same arms with a difference.
            int? difference = house.IsCadet ? cadetNumber[house.Key] : null;
            Append(house.Key, Roll(SeedFor(house.DynastyId), scopes(house.DynastyId), difference));
        }

        // The noble family titles, each bearing the arms of the house it is the family of.
        //
        // Written out in full rather than left to inherit, because there is nothing to inherit
        // from: a title takes its arms from its own key or from a dynasty it is named after, and a
        // family title is named after neither. Vanilla gets away with declaring none because
        // noble_family_title_realm_setup_effect calls set_coa on every one at game start — but that
        // effect runs only from the Byzantine and Japanese blocks in game_start.txt, so nothing
        // would call it for a generated realm and every family would fly a blank shield.
        foreach (var family in prehistory.NobleFamilies)
        {
            if (!prehistory.Houses.TryGetValue(family.HouseKey, out var house)) continue;
            int? difference = house.IsCadet ? cadetNumber[house.Key] : null;
            Append(family.TitleKey, Roll(SeedFor(house.DynastyId), scopes(house.DynastyId), difference), overrideKey: house.Key);
        }

        // Vanilla houses and dynasties of historical rulers that vanilla leaves to be rolled at game
        // start: the bookmark screen draws a blank shield for a house with no defined arms, so these
        // get arms of their own. See VanillaCharacters.
        foreach (string key in prehistory.HistoricalCoaKeys)
            Append(key, Roll(SeedFor(key), HeraldryScope.None));

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_coas.txt"), b.ToString());

        Console.WriteLine(VanillaHeraldry.CurrentOrLocate() is { } h
            ? $"  arms: {written} coats from {templatesUsed.Count} of vanilla's {h.TemplateCount} heraldry templates"
            : $"  arms: {written} coats from the fallback set (no game install to read vanilla's heraldry from)");
    }

    private static Dictionary<string, int> CadetNumbers(PrehistoryMap prehistory)
    {
        var cadetNumber = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var branches in prehistory.Houses.Values.Where(h => h.IsCadet).GroupBy(h => h.DynastyId, StringComparer.Ordinal))
        {
            int n = 0;
            foreach (var branch in branches.OrderBy(h => h.Key, StringComparer.Ordinal)) cadetNumber[branch.Key] = n++;
        }
        return cadetNumber;
    }

    /// <summary>
    /// The arms a dynasty keyed <paramref name="dynastyKey"/> would be written with, for a family
    /// of <paramref name="scope"/>; its <paramref name="cadet"/>th cadet branch's when given.
    /// </summary>
    public static Coat Roll(string dynastyKey, HeraldryScope scope, int? cadet = null)
        => Roll(SeedFor(dynastyKey), scope, cadet);

    /// <summary>The coat for <paramref name="scope"/> off <paramref name="rng"/>, differenced for a cadet.</summary>
    private static Coat Roll(Rng rng, HeraldryScope scope, int? difference = null)
    {
        var coat = VanillaHeraldry.CurrentOrLocate()?.Compose(rng, scope) ?? LegacyRoll(rng);
        return difference is int n ? Difference(coat, n) : coat;
    }

    /// <summary>
    /// A cadet's arms: the parent's charges and field division in another livery, with a mark.
    ///
    /// <para><b>Tincture first, mark second.</b> A branch that keeps its parent's colours and
    /// differs only by a small mark is a difference you can prove and cannot see: at the size a
    /// shield is drawn in the house list, the two read as one house. Changing the colours was a
    /// real difference too — arms were differenced by tincture as readily as by a mark — and it is
    /// the one that carries across a room.</para>
    ///
    /// <para><b>Within class.</b> Colours rotate among the colours and the metals swap among
    /// themselves, never one into the other. That keeps every contrast vanilla's template built —
    /// a metal charge on a coloured field stays a metal charge on a coloured field — so nothing the
    /// parent could read against, the cadet loses.</para>
    /// </summary>
    private static Coat Difference(Coat parent, int n)
    {
        var metals = Metals;
        var colours = Colours;
        int liveries = 2 * colours.Count - 1;                 // every (metal swap, colour shift) but the identity

        // Nearly always the first livery tried; a coat of one class only (all metals, say) cannot
        // show a colour shift, so later liveries are tried until the colours actually change.
        Coat recoloured = parent;
        for (int step = 0; step < liveries; step++)
        {
            int livery = 1 + (n + step) % liveries;
            bool swapMetals = livery >= colours.Count;
            int shift = livery % colours.Count;

            string Map(string t)
            {
                int ci = IndexOf(colours, t);
                if (ci >= 0) return colours[(ci + shift) % colours.Count];
                int mi = IndexOf(metals, t);
                if (mi >= 0 && swapMetals) return metals[(mi + 1) % metals.Count];
                return t;
            }

            recoloured = parent with
            {
                Colors = parent.Colors.Select(Map).ToList(),
                Charges = parent.Charges.Select(c => c with { Colors = c.Colors.Select(Map).ToList() }).ToList(),
            };
            if (!recoloured.Colors.SequenceEqual(parent.Colors) || !recoloured.Charges.SequenceEqual(parent.Charges)) break;
        }

        // Then the mark, which now only has to tell two branches apart from each other. Against
        // everything already on the shield: a mark the colour of the field, or of the charge it is
        // laid over, disappears into it — on a divided field into half of it, which is worse,
        // because it reads as a mark that has been damaged rather than as one that is not there.
        var available = CadencyMarks.Where(m => VanillaHeraldry.CurrentOrLocate()?.HasEmblem(m) ?? true).ToArray();
        if (available.Length == 0) available = ["ce_border_shield.dds"];
        string mark = available[n / liveries % available.Length];

        var used = recoloured.Colors.Concat(recoloured.Charges.SelectMany(c => c.Colors)).ToHashSet(StringComparer.Ordinal);
        var readable = metals.Concat(colours).Where(t => !used.Contains(t)).ToList();
        if (readable.Count == 0) readable = [.. metals];
        string markColour = readable[n % readable.Count];

        return recoloured with { Brisure = new Charge(mark, [markColour], [new Instance(0.5, 0.5, 1, 1)]) };
    }

    private static int IndexOf(IReadOnlyList<string> list, string value)
    {
        for (int i = 0; i < list.Count; i++) if (list[i] == value) return i;
        return -1;
    }

    /// <summary>The roll used when vanilla's heraldry cannot be read: one charge on a simple field.</summary>
    private static Coat LegacyRoll(Rng rng)
    {
        string[] tinctures = [.. LegacyColours, .. LegacyMetals];
        string pattern = LegacyPatterns[rng.Int(0, LegacyPatterns.Length - 1)];
        string c1 = tinctures[rng.Int(0, tinctures.Length - 1)];
        string c2 = tinctures[rng.Int(0, tinctures.Length - 1)];
        while (c2 == c1) c2 = tinctures[rng.Int(0, tinctures.Length - 1)];

        string emblem = LegacyEmblems[rng.Int(0, LegacyEmblems.Length - 1)];
        string emblemColour = tinctures[rng.Int(0, tinctures.Length - 1)];
        while (emblemColour == c1) emblemColour = tinctures[rng.Int(0, tinctures.Length - 1)];

        return new Coat(pattern, [c1, c2], [new Charge(emblem, [emblemColour], [new Instance(0.5, 0.5, 0.75, 0.75)])]);
    }

    private static string Num(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static void AppendCoa(JominiBuilder b, string key, Coat coat)
    {
        using (b.Block(key))
        {
            b.Quoted("pattern", coat.Pattern);
            for (int i = 0; i < coat.Colors.Count; i++) b.Quoted($"color{i + 1}", coat.Colors[i]);

            foreach (var charge in coat.Charges) AppendCharge(b, charge);

            // Last, so it sits over the charges rather than under them.
            if (coat.Brisure is { } mark) AppendCharge(b, mark);
        }

        b.Blank();
    }

    private static void AppendCharge(JominiBuilder b, Charge charge)
    {
        using (b.Block("colored_emblem"))
        {
            b.Quoted("texture", charge.Texture);
            for (int i = 0; i < charge.Colors.Count; i++) b.Quoted($"color{i + 1}", charge.Colors[i]);
            if (charge.Mask is { Count: > 0 } mask) b.Inline("mask", string.Join(' ', mask));

            foreach (var inst in charge.Instances)
            {
                string line = $"position = {{ {Num(inst.X)} {Num(inst.Y)} }} scale = {{ {Num(inst.ScaleX)} {Num(inst.ScaleY)} }}";
                if (inst.Rotation != 0) line += $" rotation = {Num(inst.Rotation)}";
                if (inst.Depth is { } depth) line += $" depth = {Num(depth)}";
                b.Inline("instance", line);
            }
        }
    }

    // =========================================================================================
    // The hegemony
    // =========================================================================================

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

        AppendCoa(b, hegemony.Key, coat);
        AppendCoa(b, HegemonyCoaKey, coat);

        ParadoxText.WriteBom(Path.Combine(dir, "zzz_gen_title_coas.txt"), b.ToString());
    }

    /// <summary>The uncontested key <c>set_coa</c> points at; see <see cref="WriteHegemonyArms"/>.</summary>
    public const string HegemonyCoaKey = "gen_hegemony_coa";

    /// <summary>
    /// A shield built to read as a crown's, not a house's: one charge set low and a crown riding
    /// above it in the chief. The field and the charge are vanilla's roll; the rest of whatever
    /// template it came from is dropped, because a crown laid over three charges or an ordinary
    /// reads as a mistake.
    /// </summary>
    private static Coat ComposeImperial(Rng rng)
    {
        var rolled = Roll(rng, HeraldryScope.None);
        var charge = rolled.Charges.FirstOrDefault()
                     ?? new Charge("ce_star_06.dds", [Metals[0]], [new Instance(0.5, 0.5, 0.75, 0.75)]);

        // Not the charge again. A crown over a crown reads as a mistake.
        string crown = charge.Texture == "ce_crown_random.dds" ? "ce_star_06.dds" : "ce_crown_random.dds";

        // Against both halves of the field and against the charge, on the same reasoning as a
        // cadet's mark: a crown the colour of anything under it is a crown you cannot see.
        var used = rolled.Colors.Take(2).Concat(charge.Colors.Take(1)).ToHashSet(StringComparer.Ordinal);
        var readable = Metals.Concat(Colours).Where(c => !used.Contains(c)).ToList();
        string crownColour = readable.Count > 0 ? readable[rng.Int(0, readable.Count - 1)] : Metals[0];

        return new Coat(rolled.Pattern, rolled.Colors,
            [charge with { Instances = [new Instance(0.5, 0.58, 0.6, 0.6)] }])
        {
            Brisure = new Charge(crown, [crownColour], [new Instance(0.5, 0.17, 0.34, 0.34, 0, 2.0)]),
            Template = rolled.Template,
        };
    }

    // =========================================================================================
    // Saved edits
    // =========================================================================================

    /// <summary>
    /// Reads a saved coat in either shape, and writes the current one.
    ///
    /// Edits are saved in <c>proctool_edits.json</c> beside the mod, and one written before the
    /// arms were vanilla's has the old flat shape — <c>Pattern</c>, <c>Color1</c>, <c>Color2</c>,
    /// <c>Emblem</c>, <c>EmblemColor</c>, <c>Brisure</c>, <c>BrisureColor</c>. A plain record
    /// deserialiser would throw on it, and <see cref="AppGUI.EditOverlay.Load"/> treats any throw as
    /// "no edits", so one old coat would have cost the user every edit in the file.
    /// </summary>
    private sealed class CoatJsonConverter : JsonConverter<Coat>
    {
        private sealed record InstanceDto(double X, double Y, double ScaleX, double ScaleY, double Rotation, double? Depth);
        private sealed record ChargeDto(string Texture, List<string> Colors, List<InstanceDto> Instances, List<int>? Mask);
        private sealed record CoatDto(string Pattern, List<string> Colors, List<ChargeDto> Charges, ChargeDto? Brisure, string? Template);

        public override Coat? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;

            if (root.TryGetProperty("Colors", out _))
            {
                var dto = root.Deserialize<CoatDto>()!;
                return new Coat(dto.Pattern, dto.Colors, dto.Charges.Select(FromDto).ToList())
                {
                    Brisure = dto.Brisure is { } m ? FromDto(m) : null,
                    Template = dto.Template,
                };
            }

            // The old flat shape: one centred charge on a two-tincture field, perhaps a mark.
            string S(string name) => root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString()! : "";
            var coat = new Coat(S("Pattern"), [S("Color1"), S("Color2")],
                [new Charge(S("Emblem"), [S("EmblemColor")], [new Instance(0.5, 0.5, 0.75, 0.75)])]);
            if (S("Brisure") is { Length: > 0 } brisure)
                coat = coat with { Brisure = new Charge(brisure, [S("BrisureColor") is { Length: > 0 } bc ? bc : S("EmblemColor")], [new Instance(0.5, 0.5, 1, 1)]) };
            return coat;
        }

        public override void Write(Utf8JsonWriter writer, Coat value, JsonSerializerOptions options)
            => JsonSerializer.Serialize(writer, new CoatDto(value.Pattern, [.. value.Colors], value.Charges.Select(ToDto).ToList(),
                value.Brisure is { } m ? ToDto(m) : null, value.Template));

        private static Charge FromDto(ChargeDto d)
            => new(d.Texture, d.Colors, d.Instances.Select(i => new Instance(i.X, i.Y, i.ScaleX, i.ScaleY, i.Rotation, i.Depth)).ToList(), d.Mask);

        private static ChargeDto ToDto(Charge c)
            => new(c.Texture, [.. c.Colors], c.Instances.Select(i => new InstanceDto(i.X, i.Y, i.ScaleX, i.ScaleY, i.Rotation, i.Depth)).ToList(),
                c.Mask is null ? null : [.. c.Mask]);
    }
}
