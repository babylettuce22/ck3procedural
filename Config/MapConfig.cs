using System.ComponentModel;
using System.Globalization;
using System.Windows.Forms.Design;

namespace Ck3MapGen.Config;

[AttributeUsage(AttributeTargets.Property)]
public sealed class AdvancedSettingAttribute : Attribute
{
}


/// <summary>
/// Keeps a setting out of the property grid entirely. Unlike <see cref="AdvancedSettingAttribute"/>,
/// which hides a row only until the user ticks Advanced, this hides unconditionally — there is no
/// toggle that brings it back.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class HideInGeneratorAttribute : Attribute
{
}


/// <summary>
/// Marks a setting an Azgaar export decides instead of the user, so the property grid can say so on
/// the row itself rather than leave a knob on screen that silently does nothing.
///
/// A comment would not do: the settings these mark are the ones a user reaches for first when the
/// hierarchy or the realm map comes out the wrong shape, and turning them while an import is loaded
/// produces no change at all, because the import decides those things from the export.
///
/// These used to be *hidden* while a path was set, which was a lie in both directions. An import
/// can load and then decide nothing — an export with no cell data plans no hierarchy
/// (<see cref="MapGen.AzgaarImport.PlanHierarchy"/> returns null) and binds no states, and every
/// one of these settings is then read at whatever value the grid was no longer showing. Hiding also
/// took the value off screen in exactly the case where knowing it mattered. So the row stays, reads
/// as inert, and carries <see cref="Reason"/> — which is expected to name both what overrides it and
/// when it is still read.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class AzgaarIncompatAttribute(string reason, bool overridden = true) : Attribute
{
    /// <summary>
    /// What the export decides instead, and where the setting is still read. Appended to the
    /// property's own description while an export is loaded.
    /// </summary>
    public string Reason { get; } = reason;

    /// <summary>
    /// True when the export genuinely takes the decision over, which is what makes the row
    /// read-only: the value is not consulted and editing it would only look like it did something.
    ///
    /// False marks the other kind — a setting that still applies and *overrides the export*, which
    /// has to stay editable, since a user who has just loaded an export and finds it half-discarded
    /// needs to be able to turn the thing off.
    /// </summary>
    public bool Overridden { get; } = overridden;
}

/// <summary>
/// The file picker behind <see cref="MapConfig.AzgaarJsonPath"/>.
///
/// The stock <see cref="FileNameEditor"/> opens with no filter at all, so the dialog lands in a
/// folder that also holds the heightmap PNG the same export produced - and the two files are
/// interchangeable to the eye and not at all to the loader. Naming the extension is the whole of
/// this class: Azgaar's other menu entries export .svg and .map, neither of which parses here, and
/// finding that out costs a run.
///
/// "All files" stays on the list, because a user who has renamed their export is better served by a
/// dialog that will still show it than by one that is certain it knows better.
/// </summary>
public sealed class AzgaarJsonFileEditor : FileNameEditor
{
    protected override void InitializeDialog(OpenFileDialog openFileDialog)
    {
        base.InitializeDialog(openFileDialog);
        openFileDialog.Title = "Choose an Azgaar 'Full' JSON export";
        openFileDialog.Filter = "Azgaar export (*.json)|*.json|All files (*.*)|*.*";
    }
}

/// <summary>
/// The file picker behind <see cref="MapConfig.ImpassableMaskPath"/>: a black-and-white PNG
/// painted over provinces.png. Filtered to images so the dialog does not offer the preset JSONs
/// and heightmaps that share the same folders.
/// </summary>
public sealed class ImpassableMaskFileEditor : FileNameEditor
{
    protected override void InitializeDialog(OpenFileDialog openFileDialog)
    {
        base.InitializeDialog(openFileDialog);
        openFileDialog.Title = "Choose an impassable mask (white = impassable, black = passable)";
        openFileDialog.Filter = "Mask image (*.png;*.bmp)|*.png;*.bmp|All files (*.*)|*.*";
    }
}

/// <summary>
/// How <see cref="MapConfig.ImpassableMaskPath"/> is read against the province partition.
/// </summary>
public enum ImpassableMaskMode : byte
{
    /// <summary>
    /// The painted pixels are a region of their own that the partition may not cross (see
    /// <see cref="MapGen.ProvinceDomain"/>), so provinces are cut to the stroke and the wall's edge
    /// in game is the edge that was painted. Every province inside the stroke is impassable.
    /// </summary>
    Snap,

    /// <summary>
    /// Provinces are partitioned as if there were no mask, and then every land province the paint
    /// lands on (by at least <see cref="MapConfig.ImpassableMaskMinShare"/>) turns impassable whole.
    /// Forgiving of a thin scribble, accurate only to the province.
    /// </summary>
    Touch,
}

/// <summary>
/// What <see cref="MapGen.ImpassableAutoCut"/> ranks ground by when it picks the walls.
/// </summary>
public enum ImpassableRanking : byte
{
    /// <summary>
    /// Steepness first: the most rugged ground at a barony's scale, weighted up where it stands
    /// high above the country around it, whatever its absolute height. Escarpments, crater rims and
    /// steep ridges near the sea become walls; smooth high ground does not, and the valleys between
    /// a range's ridges stay passable. See <see cref="MapConfig.ImpassableMinSlope"/> and
    /// <see cref="MapConfig.ImpassableLiftScale"/>.
    /// </summary>
    Slope,

    /// <summary>
    /// Height first, as the cut did before: ground above the gate line ranked by height, grown down
    /// its flanks, with flat ground below the mountain line exempt.
    /// </summary>
    Height,
}

/// <summary>
/// Which way the world's peoples lean on the one question CK3 asks about sex: who inherits, who
/// may be granted a title, who sits on a council, who rides as a knight.
///
/// One knob rather than four, because the four levers CK3 gives are not independent. The faith's
/// <c>doctrine_gender</c> is the one that decides succession; the culture's <c>martial_custom</c>
/// decides who fights; the inheritance traditions override the doctrine; and the sex of the rulers
/// written into history is what a player actually sees on the map. Roll those separately and a
/// world contradicts itself — which is what this generator did before this setting existed, with
/// a third of every world's faiths female-dominated and every last count a man.
///
/// So this only moves one distribution — how the doctrine falls — and everything downstream reads
/// the doctrine that came out. See <see cref="MapGen.Faiths"/> for the roll,
/// <see cref="MapGen.Cultures.AlignGender"/> for the culture that follows it, and
/// <see cref="Emit.HistoryWriter.RulerIsFemale"/> for the ruler.
/// </summary>
public enum GenderPreference
{
    /// <summary>
    /// Overwhelmingly male-dominated, with the rare exception vanilla's own map has. About one
    /// ruler in nine is a woman, nearly all of them under the faiths that allow it.
    /// </summary>
    Historical,

    /// <summary>
    /// A genuine spread — no answer is the world's answer. Each religion is still coherent within
    /// itself, so a matriarchy borders a patriarchy rather than every realm being confused.
    /// </summary>
    Mixed,

    /// <summary>The mirror of <see cref="Historical"/>: women hold the land and the titles.</summary>
    FemaleDominated,

    /// <summary>
    /// Equal everywhere, without exception: every faith is <c>doctrine_gender_equal</c> with an
    /// open priesthood, and every culture's martial custom is equal, so land, councils, clergy and
    /// knighthood are open to both. Rulers follow the doctrine as always, which makes them a little
    /// under half women. Last in the list because presets store the value as a number.
    /// </summary>
    Equal,
}

/// <summary>
/// Spells <see cref="GenderPreference"/> the way the setting is read aloud rather than the way an
/// identifier has to be spelled — "Female-dominated", not the run-together FemaleDominated a
/// PropertyGrid would otherwise print.
///
/// Display only. Presets go through System.Text.Json, which does not consult type converters, so
/// nothing on disk depends on these strings.
/// </summary>
public sealed class GenderPreferenceConverter() : EnumConverter(typeof(GenderPreference))
{
    private static readonly (GenderPreference Value, string Text)[] Names =
    [
        (GenderPreference.Historical, "Historical"),
        (GenderPreference.Mixed, "Mixed"),
        (GenderPreference.FemaleDominated, "Female-dominated"),
        (GenderPreference.Equal, "Equal everywhere"),
    ];

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture,
        object? value, Type destinationType)
        => destinationType == typeof(string) && value is GenderPreference preference
            ? Names.First(n => n.Value == preference).Text
            : base.ConvertTo(context, culture, value, destinationType);

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture,
        object value)
    {
        if (value is string text)
            foreach (var (option, spelling) in Names)
                if (string.Equals(text, spelling, StringComparison.OrdinalIgnoreCase))
                    return option;

        return base.ConvertFrom(context, culture, value);
    }
}

/// <summary>
/// Shows <see cref="MapConfig.EraAnchorYear"/>'s zero as what it means rather than as a number.
///
/// Zero is a sentinel — "however advanced the world's own year would make it" — and a grid row
/// reading "0" says the opposite of that to anyone who has not read the description: it looks like
/// a year, and a year of zero looks like the beginning of time. Spelling it out is the difference
/// between a setting that explains itself and one that has to be explained.
///
/// Not exclusive, so the row stays a normal editable number: the dropdown offers the sentinel and
/// typing a year still works. Every other value renders as itself.
/// </summary>
public sealed class VanillaRegionConverter : StringConverter
{
    public const string Anywhere = "Anywhere";

    /// <summary>Vanilla's top-level world regions and a few popular sub-regions. Not exclusive:
    /// any geographical region key can be typed.</summary>
    private static readonly string[] Common =
    [
        Anywhere,
        "world_europe", "world_europe_west", "world_europe_north", "world_europe_south", "world_europe_east",
        "world_europe_west_britannia", "world_europe_west_francia", "world_europe_west_germania", "world_europe_west_iberia",
        "world_middle_east", "world_asia_minor", "world_middle_east_persia", "world_middle_east_arabia",
        "world_africa", "world_africa_north", "world_africa_west", "world_africa_east",
        "world_india", "world_steppe", "world_tibet", "world_burma",
        "world_asia", "world_asia_china", "world_asia_japan", "world_asia_korea", "world_asia_southeast",
        "world_europe, world_middle_east",
        "world_europe, world_middle_east, world_india, world_steppe, world_asia",
    ];

    public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;
    public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => false;
    public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context) => new(Common);

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture,
        object? value, Type destinationType)
        => destinationType == typeof(string) && value is string s && string.IsNullOrWhiteSpace(s)
            ? Anywhere
            : base.ConvertTo(context, culture, value, destinationType);

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
        => value is string text && (string.IsNullOrWhiteSpace(text) || text.Trim().Equals(Anywhere, StringComparison.OrdinalIgnoreCase))
            ? ""
            : base.ConvertFrom(context, culture, value);
}

/// <summary>
/// <see cref="MapConfig.RaceMixHumanShare"/>'s 0, shown as what it means: the race mode's own share.
/// Built like <see cref="FollowWorldYearConverter"/>.
/// </summary>
public sealed class FollowRaceModeConverter : Int32Converter
{
    public const string Follow = "0 (Race mode's own)";

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture,
        object? value, Type destinationType)
        => destinationType == typeof(string) && value is 0
            ? Follow
            : base.ConvertTo(context, culture, value, destinationType);

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture,
        object value)
    {
        // The picked sentinel starts with its 0, so it is matched whole rather than parsed.
        if (value is string text && (text.Trim().StartsWith("0 (", StringComparison.Ordinal)
                                     || text.Contains("mode", StringComparison.OrdinalIgnoreCase)))
            return 0;

        return base.ConvertFrom(context, culture, value);
    }

    public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;
    public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => false;

    public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context)
        => new(new[] { 0 });
}

public sealed class FollowWorldYearConverter : Int32Converter
{
    public const string Follow = "0 (Follow World Year)";

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture,
        object? value, Type destinationType)
        => destinationType == typeof(string) && value is 0
            ? Follow
            : base.ConvertTo(context, culture, value, destinationType);

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture,
        object value)
    {
        // Matched loosely on purpose. The user did not type this string, they picked it, so the
        // only way it arrives misspelled is if it came back through a saved preset or was edited by
        // hand — and in both cases meaning it is likelier than meaning a parse error.
        if (value is string text && text.Trim().StartsWith("Follow", StringComparison.OrdinalIgnoreCase))
            return 0;

        return base.ConvertFrom(context, culture, value);
    }

    public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;

    /// <summary>False so the row keeps its text box — the list is a shortcut to the sentinel, not
    /// the whole range of years.</summary>
    public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => false;

    public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context)
        => new(new[] { 0 });
}

/// <summary>
/// Every knob the mod is built with.
///
/// Two resolutions matter and they are not the same thing:
///   * <see cref="Width"/>/<see cref="Height"/> are the heightmap's own size, which everything the
///     mod ships is sized against.
///   * <see cref="WorldWidth"/>/<see cref="WorldHeight"/> are the coarse grid climate and the
///     landmass summary run on.
/// Climate limits are expressed in *raster* space.
///
/// All four are set by <see cref="MapGen.HeightmapSource"/> from the image and are not settings a
/// user picks — the heightmap is the only authority on how big the map is, and disagreeing with it
/// is a silent CK3 failure rather than an error.
/// </summary>
public sealed class MapConfig : CustomTypeDescriptor
{
    // =========================================================================
    // Base Raster & Dimensions (Hidden / Non-browsable)
    // =========================================================================

    /// <summary>Heightmap size. Set from the image; not user-editable.</summary>
    [Browsable(false)]
    public int Width { get; set; } = 8192;

    [Browsable(false)]
    public int Height { get; set; } = 4096;

    /// <summary>Coarse climate grid. Derived from the heightmap size; not user-editable.</summary>
    [Browsable(false)]
    public int WorldWidth { get; set; } = 1024;

    [Browsable(false)]
    public int WorldHeight { get; set; } = 512;


    // =========================================================================
    // 01 General
    // =========================================================================

    /// <summary>
    /// Whether the grid shows the fine-tuning knobs. Owned by the settings pane's Advanced toggle
    /// rather than shown as a row of its own — a setting about the settings is chrome, not config —
    /// but it lives here so presets carry it and <see cref="GetProperties()"/> can read it.
    /// </summary>
    [Browsable(false)]
    public bool ShowAdvancedSettings { get; set; } = false;

    /// <summary>Seed for every random decision. The toolbar's seed box owns the row.</summary>
    [Browsable(false)]
    public int Seed { get; set; } = 1;

    /// <summary>
    /// The set-piece the heightmap was built around, as <c>map-type@seed</c> — "crater@115886" —
    /// or empty for none. Written by the Quick page when it draws one (see
    /// AppGUI.QuickFeatures), and read to find the set-piece again after the map is made, so it
    /// can be named, lettered on the flat map and given a region (see <see cref="MapGen.Landmarks"/>).
    /// Hidden: it describes the heightmap rather than choosing anything, and is only right for the
    /// Forge project the Quick page adopted with it.
    /// </summary>
    [Browsable(false)]
    public string SetPiece { get; set; } = "";

    /// <summary>
    /// An Azgaar "Full" JSON export to borrow from, or empty for none.
    ///
    /// Adjunct, never required. Empty is the normal setting and the generator behaves exactly as it
    /// always has; a path makes it borrow the export's names for the places they belong to and
    /// generate the rest from the same name corpora, so the whole map speaks one language.
    ///
    /// It pairs with a heightmap exported from the same Azgaar map — that is still what decides the
    /// coastline and the relief, since Azgaar draws its coast as smoothed isolines and its cells are
    /// far coarser than our provinces. The two are checked against each other at load time and a
    /// mismatch is reported rather than silently imported; see <see cref="MapGen.AzgaarRaster"/>.
    /// </summary>
    [Category("01 General")]
    [RefreshProperties(RefreshProperties.All)]
    [Description("Optional. An Azgaar 'Full' JSON export (Menu > Save/Load > Export to JSON > Full) " +
                 "to take names from. Leave empty to generate every name as usual. Use together " +
                 "with a heightmap exported from the same map and the same unzoomed view.")]
    [Editor(typeof(AzgaarJsonFileEditor), typeof(System.Drawing.Design.UITypeEditor))]
    public string AzgaarJsonPath { get; set; } = "";

    /// <summary>
    /// Whether an import's provinces are cut into one barony per town the export draws in them,
    /// rather than at <see cref="CountyScale"/>'s uniform size. See <see cref="MapGen.AzgaarSeeding"/>
    /// for why no uniform size can be faithful: Azgaar province areas vary far more than their
    /// town counts, so a single size splits the big provinces into several counties and starves the
    /// small ones. Only read when an export is loaded; land outside the export's provinces keeps the
    /// uniform size either way.
    /// </summary>
    [Category("03 Provinces")]
    [DisplayName("Azgaar: Barony Per Town")]
    [Description("With an Azgaar export: cut each of its provinces into one barony per town it holds (at least one), so every province becomes one county holding its own settlements, dense where the export is populous and sparse where it is not. Off cuts them at County Scale's uniform size instead. Only used with an export.")]
    public bool AzgaarBaroniesFromTowns { get; set; } = true;

    /// <summary>
    /// The largest a barony may grow under <see cref="AzgaarBaroniesFromTowns"/>, in vanilla
    /// barony areas. A thinly-settled Azgaar province can be vast; past this it gets more baronies
    /// than towns, so no single barony is a whole region's one siege, terrain and holding.
    /// </summary>
    [Category("03 Provinces")]
    [DisplayName("Azgaar: Largest Barony")]
    [Description("With Azgaar: Barony Per Town, the largest a barony may be, in vanilla barony areas. A thinly-settled province bigger than this gets extra baronies beyond its towns. 8 by default; lower keeps empty land finer-grained, higher follows the export's towns more strictly.")]
    public double AzgaarMaxBaronyArea { get; set; } = 8.0;

    /// <summary>
    /// Ships the chronicle the player can see: the HUD's Chronicle tab and its world feed, the
    /// Realm Lore button in the title window, and the narrators that write entries during play.
    ///
    /// Off removes what the player sees and nothing the world is made of. The generated history
    /// (<see cref="MapGen.ChronicleMap"/>) is still built, because struggles are found in its
    /// tension; and the narrator effects that other systems call — ruin, reclamation, wonders,
    /// frontier eras, struggle phases and endings — are still defined, as empty stubs, so none
    /// of those callers is left pointing at nothing. See
    /// <see cref="Emit.ChronicleRuntimeWriter.WriteStubs"/>.
    /// </summary>
    [Category("01 General")]
    [DisplayName("Chronicle")]
    [Description("Ship the Chronicle: the HUD tab with the world's history feed, the Realm Lore button in the title window, and the entries the game writes as things happen. Off removes all of it from the game. Struggles and every other system still work; they just stop narrating.")]
    public bool EnableChronicle { get; set; } = true;


    // =========================================================================
    // 02 World State
    // =========================================================================

    /// <summary>
    /// The year the world says it is: the bookmark date, and the calendar every generated date is
    /// written on — births, deaths, wars, chronicle entries.
    ///
    /// Deliberately no longer the same question as "how advanced is this world". It used to be
    /// both, which was fine while the only worlds this tool made were medieval-European-shaped, and
    /// stops being fine the moment a world arrives with a calendar of its own: an export whose
    /// present year is 433 wants a bookmark in 433 and does not want its cultures dropped into the
    /// tribal era to get there. <see cref="EraAnchorYear"/> is the other half of that question.
    /// </summary>
    [Category("02 World State")]
    [DisplayName("World Year")]
    [AzgaarIncompat("The export's own present year becomes the world year, so the game clock and the " +
                    "world's history agree. Advancement does not follow it — that stays on Advancement " +
                    "Year, which, left to follow, is read from the export's peoples instead. Still read as set " +
                    "if the export carries no year, which a 'Minimal' export may not.")]
    [Description("What the world calls the year: the bookmark date, and the calendar every date the game renders is on — births, deaths, wars, chronicle entries. How advanced the world is, is a separate question: see Advancement Year.")]
    public int StartYear { get; set; } = 900;

    /// <summary>
    /// Which point on CK3's own timeline this world is judged against: how many innovations its
    /// cultures already hold, the development baseline, and the feudal/tribal/nomad mix.
    ///
    /// Zero means "follow <see cref="StartYear"/>", which is what every run did before this existed
    /// and is why the default costs nothing — with it unset the two years are the same number and
    /// every heuristic reads exactly what it used to.
    ///
    /// Set, it decouples them. A world whose calendar says 433 can still be as developed as vanilla
    /// in 900, and <see cref="EraOffset"/> is what carries that decision through to the culture eras
    /// the game itself reads, so the two do not end up disagreeing about which era it is.
    /// </summary>
    [Category("02 World State")]
    [DisplayName("Advancement Year")]
    [TypeConverter(typeof(FollowWorldYearConverter))]
    [AzgaarIncompat("Left to follow the world year, an import reads it from the export instead — how many of its " +
                    "people are settled rather than tribal, follow organised faiths, and live behind walls — since " +
                    "the world year becomes the export's calendar. A year typed here is used as it stands.",
                    overridden: false)]
    [Description("Which year on CK3's own timeline this world is as advanced as: innovations cultures already hold, the development baseline, and the feudal/tribal/nomad mix. Set it to follow the world year to keep the two together, which is how every map worked before this setting existed.")]
    public int EraAnchorYear { get; set; }

    /// <summary>
    /// Whether the world gets its own calendar: twelve month names and an era suffix after every
    /// year. Each name is the one typed on the Calendar tab (<see cref="CalendarMonths"/>,
    /// <see cref="CalendarEraName"/>, <see cref="CalendarEraShort"/>) where there is one, else
    /// generated in the language of the world's most widespread people. See
    /// <see cref="MapGen.WorldCalendar"/>. A world of vanilla peoples keeps vanilla's calendar.
    /// </summary>
    [Category("02 World State")]
    [DisplayName("World Calendar")]
    [Description("Gives the world its own calendar: month names and a year suffix (\"1 Talvenmoon 900 TR\" instead of \"1 January 900 AD\"). Type names on the Calendar page of the settings list; anything left blank is generated in the language of the world's most widespread people, and an Azgaar export's own era is used before a generated one. Off keeps CK3's months and AD. Not used with Content Source VanillaWorld.")]
    public bool CalendarEnabled { get; set; } = true;

    /// <summary>
    /// The era, typed: "Talvek Reckoning". Blank means the export's era or a generated one. Edited
    /// on the Calendar tab rather than the grid, which has no good shape for fifteen related
    /// strings; still saved with presets, which take every read/write property.
    /// </summary>
    [Browsable(false)]
    public string CalendarEraName { get; set; } = "";

    /// <summary>
    /// What follows every year: "TR". Blank means the initials of <see cref="CalendarEraName"/>
    /// when that is typed, else the export's or a generated one.
    /// </summary>
    [Browsable(false)]
    public string CalendarEraShort { get; set; } = "";

    /// <summary>Twelve month names, January first; a blank one is generated. See
    /// <see cref="CalendarEraName"/> for why it is not on the grid.</summary>
    [Browsable(false)]
    public string[] CalendarMonths { get; set; } = ["", "", "", "", "", "", "", "", "", "", "", ""];

    /// <summary>The year every advancement heuristic reads. See <see cref="EraAnchorYear"/>.</summary>
    [Browsable(false)]
    public int EraYear => EraAnchorYear > 0 ? EraAnchorYear : Math.Max(1, StartYear);

    /// <summary>
    /// How advanced the world is in <paramref name="worldYear"/>: the start's own advancement,
    /// moved by the same number of years. An additional bookmark 199 years on is 199 years more
    /// advanced than the start, whatever the start is.
    /// </summary>
    public int EraYearAt(int worldYear) => Math.Max(1, EraYear + (worldYear - StartYear));

    /// <summary>
    /// This configuration as it would be for a world as advanced as <paramref name="eraYear"/>, for
    /// the passes an additional bookmark re-runs. A shallow copy: nothing it shares is written to.
    /// </summary>
    internal MapConfig AtAdvancement(int eraYear)
    {
        var copy = (MapConfig)MemberwiseClone();
        copy.EraAnchorYear = Math.Max(1, eraYear);
        return copy;
    }

    /// <summary>
    /// This configuration for a world whose start date has been moved to <paramref name="year"/> by
    /// history run on in the History workspace — see <see cref="MapGen.AppliedHistory"/>.
    ///
    /// The calendar moves; how advanced the world is does not. "Follow World Year" is pinned to the
    /// advancement it was following, the same way an Azgaar calendar is adopted, so that running
    /// three centuries of politics does not also hand every culture three centuries of innovations.
    /// Advancement stays the user's own setting to move.
    ///
    /// Additional bookmarks follow the start: their dates are placed around <paramref name="year"/>
    /// like any start's, and an applied history reads each one's map from whichever run covered it
    /// (see <see cref="MapGen.HistoryEras"/>). A shallow copy, as <see cref="AtAdvancement"/> is; the
    /// settings grid keeps showing the user's own values.
    /// </summary>
    /// <param name="salt">The <see cref="PeopleSalt"/> to draw people with: the year by default, or
    /// the one an applied history was first captured at, which stays with it when its dates are
    /// moved (see <c>AppliedHistory.ShiftedBy</c>) so a relabelled year keeps the same people.</param>
    internal MapConfig AtStartYear(int year, int? salt = null)
    {
        var copy = (MapConfig)MemberwiseClone();
        if (copy.EraAnchorYear <= 0) copy.EraAnchorYear = Math.Max(1, EraYear);
        copy.StartYear = year;
        copy.PeopleSalt = salt ?? year;
        return copy;
    }

    /// <summary>
    /// How many epochs the formation runs for a world starting in <paramref name="startYear"/>: the
    /// formation years, or as many as fit after year one. Two start years with the same count grow
    /// the same realms — the formation counts back from the start, never from a fixed date.
    /// </summary>
    internal int FormationEpochs(int startYear)
        => Math.Max(1, Math.Min(FormationYears, startYear - 1) / MapGen.Formation.EpochYears);

    /// <summary>
    /// Mixed into every per-person draw — a ruler's name, sex, birth and upbringing, and the family
    /// stream prehistory walks. Zero for a generated world, which is what keeps it byte for byte the
    /// world it always was. A history applied from the History workspace sets it to the year it
    /// stands at (see <see cref="AtStartYear"/>): those draws are keyed on the seat county alone, so
    /// without it a realm that kept its capital for three centuries would still be ruled by the
    /// same man, with the same dead father.
    /// </summary>
    internal int PeopleSalt { get; set; }

    /// <summary>
    /// Under an applied history, the rulers the History workspace had on the realms' thrones, by the
    /// seat county index they rule from: name, sex and birth year, which the per-ruler draws hand
    /// back in place of drawing (see <c>HistoryWriter.RulerNames</c> and its two siblings). Set by
    /// <c>ContentWriter.ApplyRealms</c>; null for a generated world. Every seat not in it — a lord
    /// inside a realm — is drawn as before.
    /// </summary>
    internal IReadOnlyDictionary<int, (string Name, bool Female, int Born)>? SeatPeople { get; set; }

    /// <summary>
    /// Under an applied history, what each ruling house's history earned it, by the seat county index
    /// of the ruler heading it — one seat a house. <c>RulerMap.Build</c> hands half the start date's
    /// renown out by it (see <c>Rulers.ByStanding</c>). Set by <c>ContentWriter.ApplyRealms</c>; null
    /// for a generated world, or a history captured without standing, when renown goes by tier alone.
    /// </summary>
    internal IReadOnlyDictionary<int, double>? SeatStanding { get; set; }

    /// <summary>
    /// Two more bookmarks around <see cref="StartYear"/>, filling out vanilla's 867 / 1066 / 1178.
    /// The start takes whichever of the three its advancement is nearest, and the other two are
    /// placed by vanilla's gaps from it — before it, after it, or one of each. See
    /// <see cref="MapGen.BookmarkEras"/>. The start date's world is the same either way: this only
    /// adds history dated before and after it.
    /// </summary>
    [Category("02 World State")]
    [DisplayName("Additional Bookmarks")]
    [Description("Adds two more start dates around the World Year, so the three fill out vanilla's 867 / 1066 / 1178. The World Year takes whichever its Advancement Year is nearest — a world at 900 gets bookmarks 199 and 311 years later; one at 1178 or later gets them 311 and 112 years earlier. Each shows the realms, governments, holdings, development and innovations of its own date, ruled by the houses of the time. The World Year's own map is unchanged. Not used with Content Source VanillaWorld.")]
    public bool AdditionalBookmarks { get; set; }

    /// <summary>Whether this run writes the additional bookmarks: the setting, where the world can carry it.</summary>
    [Browsable(false)]
    public bool UsesAdditionalBookmarks =>
        AdditionalBookmarks && ContentSource == ContentSourceMode.Procedural && AdditionalBookmarkDates.Length > 0;

    /// <summary>Vanilla's three bookmark years, whose gaps the additional bookmarks keep.</summary>
    public static readonly int[] VanillaBookmarkYears = [867, 1066, 1178];

    /// <summary>The earliest year a bookmark can sit on: its rulers are born up to 65 years before it.</summary>
    public const int MinBookmarkYear = 70;

    /// <summary>
    /// The additional bookmarks, oldest first, each with the tag of the vanilla bookmark it stands in
    /// for — "early", "middle" or "late" — which every key it writes carries. A date that would fall
    /// before <see cref="MinBookmarkYear"/> is dropped.
    /// </summary>
    [Browsable(false)]
    public (int Year, string Tag)[] AdditionalBookmarkDates
    {
        get
        {
            string[] tags = ["early", "middle", "late"];
            int era = EraYear;
            int main = VanillaBookmarkYears.OrderBy(v => Math.Abs(v - era)).ThenBy(v => v).First();

            return [.. VanillaBookmarkYears
                .Select((v, i) => (Year: StartYear + v - main, Tag: tags[i], Main: v == main))
                .Where(d => !d.Main && d.Year >= MinBookmarkYear)
                .Select(d => (d.Year, d.Tag))];
        }
    }

    /// <summary>The years of <see cref="AdditionalBookmarkDates"/>, oldest first.</summary>
    [Browsable(false)]
    public int[] AdditionalBookmarkYears => [.. AdditionalBookmarkDates.Select(d => d.Year)];

    /// <summary>
    /// The window a game-start effect naming the start date's people must be gated to: from the
    /// start date when an additional bookmark comes before it, until the next one after it when one
    /// does. Both null with a single start date, which needs no gate.
    /// </summary>
    [Browsable(false)]
    public (string? From, string? Until) MainBookmarkWindow
    {
        get
        {
            if (!UsesAdditionalBookmarks) return (null, null);
            var years = AdditionalBookmarkYears;
            return (years.Any(y => y < StartYear) ? StartDate : null,
                    years.Where(y => y > StartYear).Select(y => $"{y}.1.1").FirstOrDefault());
        }
    }

    public enum ContentSourceMode
    {
        /// <summary>Every culture, faith, title and character is generated. The generator's
        /// original behaviour, and byte for byte its original output.</summary>
        Procedural,

        /// <summary>
        /// A region of CK3's real world laid onto the generated map. The cultural and religious
        /// geography is first grown exactly as under <see cref="Procedural"/> and settled with
        /// vanilla peoples and faiths chosen to fit it (<see cref="MapGen.VanillaIdentities"/>).
        /// Then, when vanilla's map can be read, a window of it holding about as many counties as
        /// this map is projected onto it (<see cref="MapGen.VanillaTitles"/>): every title takes a
        /// vanilla title's key inside vanilla's own de jure tree, every county the culture and
        /// faith its vanilla county had at <see cref="EraYear"/>, the realms are vanilla's own at
        /// that date, and holy sites and heads of faith stand where vanilla puts them. Vanilla's
        /// definitions, name lists, innovations, doctrines and localisation are used as they are;
        /// the mod references their keys and writes none of them.
        /// </summary>
        VanillaWorld,
    }

    /// <summary>
    /// Where the world's content comes from. See <see cref="ContentSourceMode"/>. Stored in presets
    /// as its number, so the member names can change without breaking a saved preset.
    ///
    /// A vanilla world needs the installed game's files and cannot carry fantasy races: a vanilla
    /// culture's ethnicities are vanilla's, so a race stamped on its rulers would describe people
    /// the game renders as human. Generation refuses that combination rather than half-doing it.
    /// </summary>
    [Category("02 World State")]
    [DisplayName("Content Source")]
    [RefreshProperties(RefreshProperties.All)]
    [Description("Procedural generates every culture, faith, title and character. VanillaWorld lays a region of CK3's real world onto the generated map, sized to fit it: every title is a vanilla title (the Kingdom of France, the Duchy of Normandy, down to the baronies) inside vanilla's own de jure tree, every county has the culture and faith its vanilla county had at the Advancement Year, the independent realms and the rulers who hold them are vanilla's own historical characters at that date, with their families and dynasties, and holy sites and heads of faith are vanilla's. Needs the installed game; not compatible with fantasy ethnicities.")]
    public ContentSourceMode ContentSource { get; set; } = ContentSourceMode.Procedural;

    /// <summary>
    /// Where on vanilla's map a <see cref="ContentSourceMode.VanillaWorld"/> window may fall: one or
    /// more of vanilla's geographical region keys, comma separated (<c>world_europe</c>,
    /// <c>world_africa, world_middle_east</c>). Empty is anywhere, the original behaviour. A region
    /// larger than the map is sampled from inside; one smaller is taken whole and topped up with
    /// its nearest neighbours. See <see cref="MapGen.VanillaTitles"/>.
    /// </summary>
    [Category("02 World State")]
    [DisplayName("Vanilla Region")]
    [TypeConverter(typeof(VanillaRegionConverter))]
    [Description("With Content Source VanillaWorld: which part of CK3's world the map is laid onto. Pick a region or type any of vanilla's geographical region keys (map_data/geographical_regions), several separated by commas, e.g. \"world_europe, world_middle_east\". A region bigger than the map yields a piece of it; a smaller one is taken whole plus its nearest neighbours. Anywhere lets the seed choose.")]
    public string VanillaRegion { get; set; } = "";

    /// <summary>The region keys of <see cref="VanillaRegion"/>, empty for anywhere.</summary>
    [Browsable(false)]
    public IReadOnlyList<string> VanillaRegionKeys
        => VanillaRegion.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Where(k => !k.Equals(VanillaRegionConverter.Anywhere, StringComparison.OrdinalIgnoreCase))
                        .Distinct(StringComparer.Ordinal).ToList();

    /// <summary>
    /// How far the world's calendar has been slid off vanilla's, and therefore how far every
    /// date-keyed thing the *game* reads has to slide with it.
    ///
    /// Zero on any run that does not set <see cref="EraAnchorYear"/>. Non-zero, it shifts two
    /// things that would otherwise contradict each other: the dates of the innovation history
    /// blocks <see cref="Emit.CultureWriter"/> writes, and the <c>year</c> thresholds in
    /// <c>common/culture/eras</c> that decide which era the game thinks a culture is in. Move one
    /// without the other and a world seeded with early-medieval innovations is told by the game
    /// that it is tribal.
    /// </summary>
    [Browsable(false)]
    public int EraOffset => Math.Max(1, StartYear) - EraYear;

    [Browsable(false)]
    public string StartDate => $"{Math.Max(1, StartYear)}.1.1";

    [Browsable(false)]
    public string BirthDate => $"{Math.Max(1, StartYear - 37)}.1.1";

    [Browsable(false)]
    public string DeathDate => $"{Math.Max(1, StartYear + 33)}.1.1";

    [Category("02 World State")]
    [AzgaarIncompat("Shattered world wins over the export: every county becomes an independent count and the " +
                    "countries Azgaar drew are discarded, though governments still come from its states. " +
                    "Left editable because turning it off is how you get the export's realms back.",
                    overridden: false)]
    [Description("Shatter the world: no empires, kingdoms, or duchies exist at start. Every count is an independent ruler.")]
    public bool ShatteredWorld { get; set; } = false;

    /// <summary>
    /// Whether the hegemony above every empire is worn by somebody at the start date.
    ///
    /// Off, and the world is the ordinary one: the title exists, paints its border, and waits for a
    /// player or the AI to earn it through the formation decision. On, the greatest realm on the map
    /// already holds it, and the world starts under a universal claim.
    ///
    /// It is a claim and not an army. CK3 makes an empire's holder a vassal only where history says
    /// <c>liege =</c>, and this hands out no such line, so the empires below stay exactly as
    /// independent as they were — which is the point. What changes is that one ruler is rightful
    /// sovereign over ground they do not hold, and everyone else can see it.
    /// </summary>
    [Category("02 World State")]
    [DisplayName("Starting Hegemony")]
    [Description("Start the world's hegemony already held by its greatest realm, instead of leaving it "
               + "as an unclaimed title to be won. De jure only: the empires beneath it stay independent.")]
    public bool StartingHegemony { get; set; } = false;

    /// <summary>
    /// Whether All Under Heaven's Dynastic Cycle runs on the hegemony at all.
    ///
    /// On, the situation starts from history the way vanilla's does on China (see
    /// <c>Emit/Gameplay/DynasticCycleWriter.cs</c>): eras, movements, catalysts, the Mandate of Heaven, and
    /// the chaos phase that shatters a hegemon who has lost too much. Off, that one history entry
    /// is not written and nothing else is: the hegemony title, the celestial government and its
    /// nine ministries all stand on <c>title:h_china</c> and work without the situation, the
    /// generated on_actions that touch the cycle guard on its existence, and the Silk Road window's
    /// link to it is hidden at runtime by <c>Situation.IsValid</c>.
    ///
    /// The cost is error.log noise, not behaviour: vanilla reaches <c>situation:dynastic_cycle</c>
    /// without a null check at some 170 sites, and on a map with the DLC but no situation each one
    /// logs a failed context switch when its interaction, project or yearly pulse is evaluated.
    /// Same class as the <c>c_byzantion</c> entries a generated map already lives with.
    /// </summary>
    [Category("02 World State")]
    [DisplayName("Dynastic Cycle")]
    [Description("Run All Under Heaven's Dynastic Cycle on the hegemony: eras, movements, catalysts and the "
               + "Mandate of Heaven. Off, the situation is never started; the hegemony, celestial government "
               + "and ministries still work. Expect some harmless error.log noise from vanilla script that "
               + "assumes the situation exists.")]
    public bool DynasticCycle { get; set; } = true;

    [Category("02 World State")]
    [DisplayName("Gender Preference")]
    [TypeConverter(typeof(GenderPreferenceConverter))]
    [Description("Which way the world leans on inheritance, titles, councils and knighthood. Sets the faiths' gender doctrine, and the cultures' martial custom, inheritance traditions and the sex of the rulers written into history all follow from it, so a matriarchy is ruled by women rather than only legislated by them. Equal everywhere makes every faith gender-equal with open clergy and every culture's martial custom equal, with no exceptions.")]
    public GenderPreference Gender { get; set; } = GenderPreference.Historical;

    [Category("02 World State")]
    [Description("Enable procedural Centers of the World: focal metropolises with monumental wonders, hyper-development, and primary holy sites.")]
    public bool EnableWorldCenters { get; set; } = true;

    [Category("02 World State")]
    [Description("Target number of World Center metropolises across the globe.")]
    public int WorldCentersCount { get; set; } = 5;

    [Category("02 World State")]
    [Description("Enable active wars raging at game start between rival rulers, contested holy sites, and disputed borders.")]
    public bool EnableStartingWars { get; set; } = true;

    [Category("02 World State")]
    [Description("Target number of active ongoing wars on Day 1.")]
    public int StartingWarsCount { get; set; } = 3;

    /// <summary>
    /// Whether the world gets a men-at-arms roster of its own.
    ///
    /// Off, the mod ships vanilla's regiments alone and generated cultures keep whatever vanilla
    /// cultural units their traditions happen to unlock — which is what every run before this
    /// setting existed produced. On, each heritage fields a regiment its cultures can always
    /// raise, martial or wealthy cultures earn an elite behind a generated innovation, and the
    /// traditions that would have handed out vanilla's named units are kept off generated
    /// cultures so nothing on the map is called a Huscarl. See <c>MapGen/Titles/Retinues.cs</c>.
    /// </summary>
    [Category("02 World State")]
    [Description("Generate a men-at-arms roster for the world: one regiment per heritage, plus an elite "
               + "for cultures that earn one. Vanilla's generic units stay recruitable either way.")]
    public bool EnableGeneratedRetinues { get; set; } = true;

    /// <summary>
    /// Whether every independent ruler is handed one regiment of their own people's men-at-arms on
    /// top of whatever the engine has already bought them.
    ///
    /// Off by default, because the hole this was written to fill does not exist. CK3 arms its own
    /// start-date rulers: <c>MAA_STARTING_EXPENSE_MIN</c> and <c>_MAX</c> in
    /// <c>common/defines</c> are 0.2 and 0.35, and the comment on them reads "Rulers at game start
    /// will start out spending this much on men at arms". Every ruler on a generated map therefore
    /// already opens with a roster sized to a fifth of their income — and because the generated
    /// regiments carry an <c>ai_quality</c> of 80 to 100 against vanilla's generic roster's 0 to
    /// 40, that spending goes on the generated units by preference. The world is not raising levies
    /// alone for twenty years whether or not this is on.
    ///
    /// What it still does is *guarantee* the cultural regiment specifically, rather than leaving it
    /// to a budget a poor count may spend elsewhere. That is a real thing to want and the reason
    /// this is still here — but it is one free regiment per realm on top of a full engine
    /// allocation, so it is the one switch in this system that moves the balance of the opening
    /// rather than only its vocabulary, and it should be turned on deliberately.
    /// </summary>
    [Category("02 World State")]
    [Description("Give every independent ruler one guaranteed regiment of their own people's men-at-arms "
               + "on the start date, sized by their rank. This is on top of the roster CK3 already buys "
               + "every start-date ruler, so it raises the opening military balance.")]
    public bool EnableStartingRetinues { get; set; } = false;

    /// <summary>
    /// How many struggles the world may carry at most.
    ///
    /// A ceiling rather than a target: a struggle is only generated where the chronicle already
    /// says one exists, so a world of tidy single-culture kingdoms gets none however high this is
    /// set. Low on purpose — a struggle claims a whole kingdom and hands out region-wide modifiers,
    /// and a map where every kingdom is struggling is one where none of them feels remarkable.
    /// Zero turns the feature off.
    /// </summary>
    [Category("02 World State")]
    [Description("Most struggles the world may carry. Each covers one kingdom whose peoples are already contesting it in the generated chronicle; a world with no such kingdom gets none. 0 disables struggles.")]
    public int MaxStruggles { get; set; } = 2;

    /// <summary>
    /// How much accumulated chronicle tension a kingdom needs before it counts as struggling.
    ///
    /// Measured in <see cref="MapGen.ChronicleEvent.Tension"/>, which runs 0–3 per event, so this is
    /// roughly "four bad frontiers' worth". It also sets the bar for starting in the worst phase:
    /// twice this much and the struggle can open in outright bloodshed.
    /// </summary>
    [Category("02 World State")]
    [Description("How much accumulated tension a kingdom's chronicle needs before it qualifies as a struggle. Lower finds more struggles and weaker ones.")]
    public int StruggleMinTension { get; set; } = 8;

    /// <summary>
    /// How many distinct forged looks each weapon kind gets — swords, daggers, axes and maces, so
    /// the world writes four times this many.
    ///
    /// **It costs disk, and icons dominate.** One weapon is roughly 1.4 MB: a 251 KB mesh, a 262 KB
    /// recolour mask, and a 922 KB inventory icon that is 60% of the total because
    /// <see cref="Io.DdsWriter"/> writes no block compression. Eight per kind is about 45 MB;
    /// sixteen is about 90 MB, and adds a few seconds to generation.
    ///
    /// **A bigger pool spreads looks across cultures, not within one.** Weapons the game itself
    /// creates are dressed by <see cref="Emit.ForgedVisualOverrides"/>, which gates on culture and
    /// rarity — so a single culture gets one look per rarity, four in total, however large the pool
    /// is. Raising this gives *other* realms weapons that differ from yours. Widening what one
    /// culture shows needs another axis in that file, not more looks here.
    ///
    /// The pool is also capped by what the parts libraries can actually build: combinations are
    /// deduplicated, so a kind whose library yields fewer distinct assemblies than this simply
    /// stops early rather than repeating itself.
    /// </summary>
    [Category("02 World State")]
    [Description("Distinct procedurally forged looks per weapon kind (sword, dagger, axe, mace). Costs about 1.4 MB of meshes and icons each. Raising it varies weapons ACROSS cultures; a single culture still shows one look per rarity.")]
    public int WeaponPoolSizePerKind { get; set; } = 16;

    /// <summary>
    /// Whether the mod ships the wilderness and colonisation system.
    ///
    /// Gates the <c>Wilderness</c> file set in BaseFilesToCopy — the government, holdings,
    /// buildings, effects and localisation that let an unsettled county exist and be claimed. Off
    /// leaves a mod with no notion of wilderness at all rather than one with the notion and no
    /// counties in it, because the two halves have to agree: the generated history that seats
    /// counties on the dummy holder is what makes those files mean anything, and those files are
    /// what stop that history from dangling.
    ///
    /// Nothing in the set references a generated culture, faith or title key, so it is safe to ship
    /// with any seed.
    /// </summary>
    [Category("02 World State")]
    [Description("Ship the wilderness and colonisation system: unsettled counties held by nobody, obstacles that have to be cleared, and colonies that grow into real holdings. Off leaves the mod with no notion of wilderness at all.")]
    public bool EnableWilderness { get; set; } = true;

    [HideInGenerator] // Hiding for now until "completed"
    [Category("02 World State")]
    [DisplayName("Magic")]
    public bool EnableMagic { get; set; } = true;

    /// <summary>
    /// Ships the society system — see <see cref="Emit.StaticFileWriter.Societies"/> — and generates
    /// this world's Restorationists and inversion cult. Off by default for now, and hidden in the
    /// generator settings while in development. Includes static society content, generated setup,
    /// and shared GUI integration for the society panels.
    /// </summary>
    /// HIDING FOR THIS BUILD
    [HideInGenerator]
    [Category("02 World State")]
    [DisplayName("Societies")]
    [Description("Generate Restorationist and inversion-cult societies. See BaseFilesToCopy/Societies/README.txt.")]
    public bool EnableSocieties { get; set; } = false;

    /// <summary>
    /// Ships the hand-written prototype the society system grew out of — see
    /// <see cref="Emit.StaticFileWriter.SocietyPrototype"/>. Ignored when <see cref="EnableSocieties"/>
    /// is on: the two define the same panel hooks. CLI flag: <c>--society-prototype</c>, which also
    /// turns EnableSocieties off (<c>--no-societies</c> turns it off alone).
    /// </summary>
    [HideInGenerator]
    [Category("02 World State")]
    [DisplayName("Society prototype")]
    [Description("Ship the hand-written society prototype when EnableSocieties is false. For reference only. See BaseFilesToCopy/SocietyPrototype/README.txt.")]
    public bool EnableSocietyPrototype { get; set; } = false;

    // =========================================================================
    // 03 Provinces
    // =========================================================================

    /// <summary>
    /// How large a barony is, relative to vanilla's. 2 makes each one twice as wide and therefore
    /// a quarter as numerous; the whole title hierarchy follows, because <see cref="MapGen.Titles"/>
    /// clusters by counts rather than by area.
    ///
    /// This is the only knob for map granularity. Province counts used to be given directly, which
    /// meant a map kept the same number of provinces at every resolution and so a barony at
    /// <c>tiny</c> covered 1/81 of the pixels it covered at <c>vanilla</c> — below
    /// <see cref="MinProvincePixels"/>, where CK3 cannot derive a centroid and crashes without
    /// logging. Fixing the *area* instead makes the count fall out of the map size.
    ///
    /// The default is 1.25 rather than vanilla's own density, and it is the other half of
    /// <see cref="ProvinceDownscale"/>. That setting now spends the whole heightmap on world size,
    /// so a 9216-wide source becomes a 9216-wide world — vanilla's province map exactly, and
    /// vanilla's ~11,000 baronies with it. That is a lot of title, character and history generation
    /// per run, and 1.25 takes about a third off it (10,964 to ~7,000) while leaving the raster and
    /// the camera alone.
    ///
    /// It is deliberately *this* knob that carries the compensation and not the raster. Both shrink
    /// the barony count, but shrinking the province map also shrinks the world, which re-steepens
    /// every slope by 1/MapScale and puts the camera corrections back. This only makes counties
    /// larger. The visible cost is that map furniture — holdings, city scatter, trees — is authored
    /// at vanilla's absolute size and does not scale with a county, so at 1.25 it reads about 20 %
    /// small against one; <see cref="HoldingScale"/> is the lever if that lands badly.
    /// </summary>
    [Category("03 Provinces")]
    [Description("How large a barony is, relative to vanilla's. 2 makes each one twice as wide and therefore a quarter as numerous; the whole title hierarchy follows. The default 1.25 is the counterweight to ProvinceDownscale: that setting spends the heightmap on world size, which on a 9216-wide source would otherwise give vanilla's full ~11,000 baronies, and this takes about a third off. Raise it for fewer, larger counties; 1.0 is vanilla's own density. Map furniture does not scale with a county, so above ~1.25 holdings start reading small — see HoldingScale.")]
    public double CountyScale { get; set; } = 1.25;

    /// <summary>
    /// Average land province area in province-map pixels, at <see cref="CountyScale"/> 1.
    ///
    /// Vanilla: 10,966 baronies over roughly 22.4M land pixels of its 9216x4608 province map.
    /// A barony is one province here, so this reproduces vanilla's barony density.
    /// </summary>
    [Category("03 Provinces")]
    [Description("Average land province area in province-map pixels, at CountyScale 1. Vanilla: 10,966 baronies over roughly 22.4M land pixels of its 9216x4608 province map. A barony is one province here, so this reproduces vanilla's barony density.")]
    public double BaronyPixelsAtVanilla { get; set; } = 2043;

    /// <summary>
    /// Average sea zone area, same basis. Vanilla's sea zones are an order of magnitude larger
    /// than its baronies — roughly 800 of them over 20M water pixels.
    /// </summary>
    [AdvancedSetting]
    [Category("03 Provinces")]
    [Description("Average sea zone area, same basis. Vanilla's sea zones are an order of magnitude larger than its baronies — roughly 800 of them over 20M water pixels.")]
    public double SeaZonePixelsAtVanilla { get; set; } = 25000;

    /// <summary>
    /// How much larger a province in the map's coarsest region is than one in its finest.
    ///
    /// <see cref="BaronyPixelsAtVanilla"/> alone gives every barony on the map the same area, which
    /// no real map has: vanilla's Russian and Saharan counties dwarf its north Italian ones. This
    /// scatters that unevenness over the map as low-frequency regions. 1 restores the single
    /// uniform size.
    ///
    /// It does not change how many provinces the map has — see <see cref="MapGen.ProvinceSizeField"/>,
    /// which normalises the field so the count is untouched and this stays a knob for one thing.
    /// </summary>
    [Category("03 Provinces")]
    [Description("How much larger a province in the map's coarsest region is than one in its finest. 1 gives every barony on the map the same area, which no real map has — vanilla's Russian counties dwarf its north Italian ones. The province count is unaffected either way.")]
    public double ProvinceSizeVariance { get; set; } = 6.0;

    /// <summary>
    /// How wide a stretch of map holds provinces of roughly one size, in vanilla province pixels.
    /// Small values make province size change every few provinces, which reads as noise rather than
    /// as regions; the default is about the width of European Russia on vanilla's map.
    /// </summary>
    [AdvancedSetting]
    [Category("03 Provinces")]
    [Description("How wide a stretch of map holds provinces of roughly one size, in vanilla province pixels. The default is about the width of European Russia on vanilla's map; much smaller and the size changes every few provinces, which reads as noise rather than as regions.")]
    public double ProvinceSizeRegionPixels { get; set; } = 2600;

    /// <summary>
    /// How much of where provinces are small is decided by where the map could carry people, as
    /// opposed to by noise. See <see cref="MapGen.Habitability"/>.
    ///
    /// Noise gets the unevenness right and the reasons wrong: the small provinces land nowhere in
    /// particular, which is what reads as patterned rather than settled. This does not change how
    /// much the size varies — <see cref="ProvinceSizeVariance"/> still owns that, and the count
    /// correction still holds the province count fixed — only where the small ones go.
    ///
    /// Not 1 by default. Some noise left in keeps two coasts at the same latitude from coming out
    /// identical, which is its own kind of artificial.
    /// </summary>
    [Category("03 Provinces")]
    [Description("How much of where provinces are small is decided by where the map could carry people — coasts, river valleys, flat ground, kind latitudes — rather than by noise. 0 is pure noise. Does not change how much size varies or how many provinces there are, only where the small ones go.")]
    public double HabitabilitySizeWeight { get; set; } = 0.75;

    /// <summary>
    /// How strongly province growth resists crossing a slope. 0 is a plain geodesic voronoi, whose
    /// boundaries fall wherever seeds happen to be equidistant and cut straight over mountains.
    /// Higher makes the frontier stall at ridgelines so two provinces meet there instead.
    /// </summary>
    [Category("03 Provinces")]
    [Description("How strongly province growth resists crossing a slope. 0 is a plain geodesic voronoi whose boundaries cut straight over mountains; higher makes provinces meet at ridgelines.")]
    public double ProvinceTerrainCost { get; set; } = 1.5;

    /// <summary>
    /// How far the partition's view of the terrain is blurred before it costs a step against it, in
    /// vanilla province pixels. The cost is a first difference, so without this it answers to
    /// pixel-scale roughness and fringes every border at that scale. 0 uses the heightmap as it is.
    /// </summary>
    [AdvancedSetting]
    [Category("03 Provinces")]
    [Description("How far the partition blurs the terrain before growing provinces along it, in vanilla province pixels. Its cost is a first difference, so on an unsmoothed heightmap it answers to every scrap of pixel-scale roughness and frays the border at that scale. 0 uses the heightmap as it is.")]
    public double ProvinceTerrainSmoothPixels { get; set; } = 8;

    /// <summary>
    /// Rounds of a majority filter over the finished province borders. Each one hands a border pixel
    /// to whichever province holds most of the block around it, without moving a coastline or
    /// cutting a province in two.
    /// </summary>
    [Category("03 Provinces")]
    [Description("Rounds of border smoothing over the finished provinces. Each hands a border pixel to whichever province holds most of the block around it, which rounds the staircase a raster flood leaves. Coastlines never move and a province is never cut in two. 0 leaves borders as grown.")]
    public int ProvinceBorderSmoothing { get; set; } = 3;

    /// <summary>
    /// Share of baronies whose <c>common/province_terrain</c> comes out <c>mountains</c> or
    /// <c>desert_mountains</c>. Vanilla's own map runs 0.1215 and 0.0254 against 11,965 provinces.
    ///
    /// A rank rather than a pixel majority, because a majority names a mountain province after the
    /// hills around the range instead of the range — see
    /// <see cref="Emit.ContentWriter.ProvinceTerrain"/>, which records what that cost when measured.
    /// 0 restores the plain majority vote.
    /// </summary>
    [Category("03 Provinces")]
    [Description("Share of baronies whose terrain comes out mountains or desert_mountains. A majority of pixels names these after the hills that ring a range rather than the range itself, so the most mountainous baronies are promoted by rank instead. Vanilla runs 0.147 across both. 0 restores the plain majority vote.")]
    public double MountainProvinceShare { get; set; } = 0.14;

    /// <summary>
    /// Share of baronies whose terrain comes out <c>hills</c>, taken after
    /// <see cref="MountainProvinceShare"/> has had its pick. Vanilla runs 0.192.
    ///
    /// Promoted on the combined hill-and-mountain band, so a province cannot rank above the
    /// mountain cut and below this one. 0 restores the plain majority vote for this tier.
    /// </summary>
    [Category("03 Provinces")]
    [Description("Share of baronies whose terrain comes out hills, taken after the mountain share has had its pick. Vanilla runs 0.192. 0 restores the plain majority vote for this tier.")]
    public double HillProvinceShare { get; set; } = 0.19;

    /// <summary>
    /// The least mountain- or hill-band ground a province may hold and still be promoted for it,
    /// as a share of its own pixels.
    ///
    /// A backstop, not the normal constraint, and the same role
    /// <see cref="ImpassableMinMountainShare"/> plays for the impassable cut. The shares above are
    /// ranks, and a rank always finds a top 14% — on a world with no relief worth the name that
    /// would name its gentlest swells mountains. This is what stops it. On any world with real
    /// ranges it does not bind.
    /// </summary>
    [AdvancedSetting]
    [Category("03 Provinces")]
    [Description("The least mountain- or hill-band ground a province may hold and still be promoted for it, as a share of its own pixels. A backstop for worlds with no real relief, where a rank would otherwise name the gentlest swells mountains. Does not bind on a world with ranges.")]
    public double ReliefPromotionFloor { get; set; } = 0.05;

    /// <summary>
    /// Rounds of Lloyd relaxation on the province seeds: move each to the middle of what it grew,
    /// then grow everything again. This is what stops a province being squeezed to a waist between
    /// two lopsided neighbours. Each round costs a whole repartition, which is the slowest step
    /// here, so this is the setting to drop to 0 when previewing something else.
    /// </summary>
    [AdvancedSetting]
    [Category("03 Provinces")]
    [Description("Rounds of Lloyd relaxation on the province seeds — move each seed to the middle of the province it grew, then grow them all again. Turns a voronoi diagram into a centroidal one, which is what stops provinces being squeezed to a waist between lopsided neighbours. Each round costs a full repartition, the slowest step in the tool.")]
    public int ProvinceRelaxIterations { get; set; } = 1;

    /// <summary>
    /// Share of land provinces cultivated into <c>farmlands</c>.
    ///
    /// Deliberately tiny. Farmland is not a climate — it is ground people have cleared — so it
    /// belongs to a handful of settled, well-watered baronies rather than to a biome. Measured
    /// against vanilla's own detail_index, the entire farmland family (farmland_01, medi_farmlands,
    /// india_farmlands, farm_paddy_01) carries 0.33% of all painted texture weight, which is the
    /// number this default is calibrated to reproduce.
    /// </summary>
    [Category("03 Provinces")]
    [Description("Share of land provinces cultivated into farmlands, taken from the best-watered baronies of settled counties. Vanilla's farmland textures cover about 0.33% of the map and this reproduces that; past a few percent, fields start reading as a biome rather than as settlement.")]
    public double FarmlandShare { get; set; } = 0.02;

    /// <summary>
    /// Share of desert provinces that become <c>oasis</c>. Vanilla spends 0.02% of its painted
    /// weight on the oasis material, the rarest thing it paints, but across about forty small
    /// sites rather than a few large ones.
    ///
    /// Each oasis paints only a small pocket of scrub around its spring (see
    /// <see cref="MapGen.Cultivation"/>), so this sets how many there are rather than how much
    /// ground is green. 6% gives a desert of 160 provinces about ten.
    /// </summary>
    [Category("03 Provinces")]
    [Description("Share of desert provinces that become oases. Only provinces holding a drainage sink — a depression water actually collects in — are eligible, and the wettest of those win. Each oasis paints a small patch of green scrub around its spring, so this sets how many there are, not how much green.")]
    public double OasisShare { get; set; } = 0.06;

    /// <summary>
    /// How far, in vanilla province pixels, one biome's materials bleed across its boundary into
    /// the next. Scaled by <see cref="Scaled"/>, so the band is the same fraction of a continent at
    /// every map size.
    ///
    /// This is the width of the *whole* transition, and the neighbouring palette peaks at half
    /// strength on the boundary itself. Sized against biome edges, which run for hundreds of
    /// pixels. Note that a barony is roughly forty province pixels across, so a reach much above
    /// that dilutes a cultivated province rather than just softening its rim.
    /// </summary>
    [AdvancedSetting]
    [Category("03 Provinces")]
    [Description("Width in vanilla province pixels of the band where one biome's textures fade into the next. Larger is softer; much above 40 starts washing out single-province features like farmland, since a barony is about that wide.")]
    public double TerrainBlendReach { get; set; } = 44;

    /// <summary>
    /// Share of the coastal band steep enough to paint as bare cliff rather than as whatever biome
    /// sits on it.
    ///
    /// A percentile of this map's own gradients, not an absolute rise-over-run, for the same reason
    /// the hill and mountain lines are percentiles: the raw scale depends on how far the tectonic
    /// sim happened to run, and a fixed gradient classifies a wildly different fraction of the map
    /// from one seed to the next. Measured over the coastal band rather than over all land, so an
    /// unusually mountainous interior does not starve the coast of cliffs.
    ///
    /// The default is measured off vanilla's own masks: coastline_cliff_grey covers 1.41% of the
    /// map and coastline_cliff_desert 1.95%, at mean weights of 0.17% and 0.35%. Two percent
    /// carrying some cliff, with the top quarter of that carrying it at full strength, lands in the
    /// same place.
    /// </summary>
    [AdvancedSetting]
    [Category("03 Provinces")]
    [Description("Share of the coastal band steep enough to paint as bare cliff. A percentile of this map's own slopes, so it means the same thing on any heightmap. 0 disables cliff painting entirely.")]
    public double CliffSlopeShare { get; set; } = 0.02;

    /// <summary>
    /// How far inland, in vanilla province pixels, steep ground may still be painted as *coastal*
    /// cliff. Scaled by <see cref="Scaled"/>.
    ///
    /// Gated on the coast because coastline_cliff_grey is a specific sea-cliff texture — stratified
    /// grey rock cut by water — and vanilla only ever uses it at a shoreline. Steep ground inland
    /// already resolves to the family's own mountain and hill rock, which is the right answer there;
    /// letting the sea cliff reach inland would repaint every escarpment on the map.
    /// </summary>
    [AdvancedSetting]
    [Category("03 Provinces")]
    [Description("How far inland, in vanilla province pixels, steep ground still counts as coastal cliff. Beyond this, steep ground gets its climate family's mountain rock instead.")]
    public double CliffCoastReach { get; set; } = 8;

    // =========================================================================
    // Impassable Mountains & Relief Configuration
    // =========================================================================

    /// <summary>
    /// Share of land provinces declared impassable_mountains. 
    /// Vanilla's baseline is ~0.095 (1,188 impassable against 11,301 baronied).
    /// Recommended: 0.08 (8% of land provinces on mountainous worlds; flat worlds will safely deliver fewer).
    /// </summary>
    [Category("03 Provinces")]
    [Description("Share of land provinces declared impassable_mountains, which get no barony and no holder. Vanilla runs ~0.095.")]
    public double ImpassableShareOfLand { get; set; } = 0.08;

    /// <summary>
    /// A hand-painted impassable mask, or empty for none.
    ///
    /// A binary image at provinces.png resolution — white where provinces should be impassable,
    /// black elsewhere. The intended workflow is to generate once, open the written provinces.png
    /// in GIMP, paint a thick white stroke over the wall you want on a black layer, export that
    /// layer as a PNG and point this at it. The painted land becomes impassable_mountains — how
    /// exactly is <see cref="ImpassableMaskMode"/>'s call — and the relief-scored selection (share,
    /// floor, deviations, slope weight) is skipped entirely. The trapped-pocket fill and range
    /// fusing still run afterwards, so a closed ring fills in and long walls are still capped by
    /// ImpassableRangeMaxBaronies.
    ///
    /// An image of another size is nearest-sampled onto the province raster, so a mask painted over
    /// heightmap.png (2x) works too; anything not black-and-white is thresholded at mid grey.
    /// </summary>
    [Category("03 Provinces")]
    [RefreshProperties(RefreshProperties.All)]
    [Description("Optional. A black-and-white PNG at provinces.png resolution: white = impassable, black = passable. " +
                 "The painted land becomes impassable_mountains (see ImpassableMaskMode) and the relief-scored " +
                 "selection below is skipped. Paint it in GIMP over the provinces.png of an earlier run with the same " +
                 "seed and settings. Leave empty for the built-in relief scoring.")]
    [Editor(typeof(ImpassableMaskFileEditor), typeof(System.Drawing.Design.UITypeEditor))]
    public string ImpassableMaskPath { get; set; } = "";

    /// <summary>
    /// Whether the mask cuts provinces (<see cref="Config.ImpassableMaskMode.Snap"/>) or merely
    /// picks them (<see cref="Config.ImpassableMaskMode.Touch"/>). Snap is the default because a
    /// stroke thick enough to be worth painting is thick enough to be a province, and the wall then
    /// has exactly the shape that was drawn; Touch is the fallback for a scribble too thin to hold
    /// one — under Snap a painted fragment below <see cref="MinProvincePixels"/> is folded back into
    /// its surroundings and turns nothing.
    /// </summary>
    [Category("03 Provinces")]
    [Description("Only with ImpassableMaskPath. Snap: the paint is a region the province partition may not cross, " +
                 "so provinces are cut to the stroke and the wall is exactly the shape drawn (paint it at least a " +
                 "barony wide; fragments under MinProvincePixels are absorbed and turn nothing). Touch: provinces " +
                 "are laid out as usual and every land province the paint lands on turns impassable whole.")]
    public ImpassableMaskMode ImpassableMaskMode { get; set; } = ImpassableMaskMode.Snap;

    /// <summary>
    /// Touch mode only. How much of a land province the mask has to cover before the province
    /// counts as painted. 0 means a single white pixel is enough, which is what a drawn stroke
    /// wants: the wall it traces must not break at the provinces it only clips. Raise it towards 1
    /// when the mask is a filled region rather than a line and only provinces mostly inside it
    /// should turn.
    /// </summary>
    [AdvancedSetting]
    [Category("03 Provinces")]
    [Description("Only with ImpassableMaskPath in Touch mode. Share of a land province's pixels that must be white before it turns impassable. 0 means any white pixel is enough (right for a drawn line); raise towards 1 for a painted region that should only take provinces mostly inside it.")]
    public double ImpassableMaskMinShare { get; set; } = 0;

    /// <summary>
    /// The impassability score a province must reach before it may be impassable at all.
    /// Acts as an absolute backstop when terrain variance is low.
    /// Recommended: 0.35 (requires at least 35% mountain/cliff score coverage).
    /// </summary>
    [Category("03 Provinces")]
    [Description("The impassability score a province must reach before it may be impassable at all. A backstop for a map with little to no mountains.")]
    public double ImpassableMinMountainShare { get; set; } = 0.35;

    /// <summary>
    /// How far above the map's own median impassability score a province must stand, 
    /// in median absolute deviations (MAD), before it may be impassable.
    /// Recommended: 1.5.
    /// </summary>
    [Category("03 Provinces")]
    [Description("How far above the map's own median impassability score a province must stand, in median absolute deviations, before it may be impassable.")]
    public double ImpassableScoreDeviations { get; set; } = 1.5;

    /// <summary>
    /// How much of the impassability score comes from steepness rather than elevation.
    /// Recommended: 0.65 (65% slope relief / 35% absolute elevation).
    /// </summary>
    [Category("03 Provinces")]
    [Description("How much of the impassability score comes from steepness rather than height. 0 is height-only, 1 is slope-only.")]
    public double ImpassableSlopeWeight { get; set; } = 0.65;

    /// <summary>
    /// The share of a province's ground that must stand above the gate line before the score may
    /// make it impassable. The gate line is this map's mountain line, capped at
    /// <see cref="ImpassableGateHeight"/>. Steepness still ranks the candidates; this only decides
    /// who may take a slot, and a province passed over does not use one up.
    ///
    /// Without it, steepness alone qualifies. On a flat world the quota then runs past the real
    /// mountains and fills with the eroded sides of low hills. Measured on a Lowlands pangaea, 96
    /// of 218 impassables had no ground above the mountain line, and walls ringed a summit the
    /// score left passable. With the default cap, 0.1 and 0.25 gave 130 and 113 impassables there
    /// and 295 and 281 on the Highlands version of the same map, so the exact value matters
    /// little. 0 turns the gate off.
    /// Recommended: 0.1.
    /// </summary>
    [Category("03 Provinces")]
    [Description("Share of a province's ground that must be above the mountain line (capped at ImpassableGateHeight) before it may be impassable. Stops the steep sides of low hills being walled on flat maps. 0 turns it off.")]
    public double ImpassableMinMountainGround { get; set; } = 0.1;

    /// <summary>
    /// Draws the impassable walls from the terrain before the provinces are partitioned, and cuts
    /// the partition along them, instead of scoring provinces afterwards. See
    /// <see cref="MapGen.ImpassableAutoCut"/>.
    ///
    /// Scoring can only take a province or leave it, and a province grown without regard to the
    /// mountains straddles the foot of the range, so the walls come out as coarse as the provinces:
    /// foothills walled, and summits or plateaus left passable inside rings of flank walls. The cut
    /// puts each wall's edge on the foot of the range, or on a contour where the quota stops partway
    /// up it. Measured on three pangaea worlds (small Lowlands, Lowlands and Highlands), ground that
    /// is not mountain fell from 14.3%, 8.3% and 4.1% of the walls to 0.3%, 2.2% and 0%, and no
    /// summit was left passable beside a wall. It costs about a second on an 8192 map.
    ///
    /// Still read with it on: <see cref="ImpassableShareOfLand"/> (now a share of land area, with
    /// plateaus outside it), <see cref="ImpassableMinMountainGround"/>,
    /// <see cref="ImpassableGateHeight"/>, <see cref="ImpassableMountainPlateaus"/>, the mountain
    /// and steep lines, and the slope weight and score floors, which cap the share where the map's
    /// mountains run out before it does, as they stop the scored pass. A painted
    /// <see cref="ImpassableMaskPath"/> always takes precedence. Moves every province near a
    /// mountain, so a seed made with it off comes out differently with it on.
    /// Recommended: on.
    /// </summary>
    [Category("03 Provinces")]
    [Description("Cut the provinces to the mountains before partitioning, so walls end at the foot of each range instead of wherever a province border fell. Off scores whole provinces after partitioning instead.")]
    public bool ImpassableAutoCut { get; set; } = true;

    /// <summary>
    /// What the auto-cut ranks ground by. Only read with <see cref="ImpassableAutoCut"/> on.
    ///
    /// <see cref="Config.ImpassableRanking.Height"/> walls whatever stands highest, and on a map
    /// whose uplands sit above vanilla's mountain heights that is smooth, gentle ground: on a Rift
    /// world (seed 630583, 8192), 81% of land between 238 and 316 was walled against 20% of
    /// vanilla's, including fans and shoulders as gentle as a typical vanilla barony, while only 20%
    /// of the steep ground below 238 was. <see cref="Config.ImpassableRanking.Slope"/> ranks by
    /// steepness at a barony's scale instead, walls everything past
    /// <see cref="ImpassableSlopeLine"/> up to <see cref="ImpassableMaxShareOfLand"/>, and drops
    /// lone round walls (<see cref="ImpassableKnobBaronies"/>). On that world and a scar world (seed
    /// 288855), smooth high ground walled (238–400, gentler than 1.2) fell from 60% and 66% to 1.2%
    /// and 0.6%, and steep ground below 238 walled (3 or steeper) rose from 20% and 39% to 50% and
    /// 61%. Over nine worlds the walls follow the terrain: 0.7% of land on a lowland pangaea, 5.5–9.6%
    /// on the rest, 15% on a wall world. Valleys between a range's ridges stay passable, as Alpine
    /// valleys are in vanilla, and 2–6% of the walls stand below 119, against 0.2–1.4%, most of it
    /// steep inland hills. Ground above <see cref="ImpassableCeilingHeight"/> is walled either way,
    /// and a Wall set-piece's own body is always wall. Reads
    /// <see cref="ImpassableMaxShareOfLand"/> in place of <see cref="ImpassableShareOfLand"/>.
    /// Recommended: Slope.
    /// </summary>
    [Category("03 Provinces")]
    [Description("What makes ground impassable. Slope: the steepest, most rugged ground, wherever it stands, so escarpments and ridges become walls and smooth high ground stays passable. Height: the highest ground, grown down its flanks (the older behaviour).")]
    public ImpassableRanking ImpassableRanking { get; set; } = ImpassableRanking.Slope;

    /// <summary>
    /// The gentlest ground the slope-ranked cut may wall, in elevation units (sea level 36, heightmap
    /// maximum 520) per vanilla world unit: the slope of the terrain smoothed over 9 vanilla
    /// pixels, averaged over 18, as vanilla's own ground was measured. It keeps the lift from
    /// walling gentle ground that merely stands high above its floor. The default, 1.5, is steeper
    /// than four-fifths of the ground vanilla's passable provinces stand on (its median is 0.87).
    /// Not scaled by <see cref="ReliefScale"/>. Only read with <see cref="ImpassableRanking"/>
    /// Slope. 0 turns it off.
    /// Recommended: 1.5.
    /// </summary>
    [AdvancedSetting]
    [Category("03 Provinces")]
    [Description("The gentlest ground a slope-ranked wall may stand on, in elevation units per world unit at a barony's scale. Stops a gentle map walling rolling hills to fill its share. 1.5 is steeper than 80% of vanilla's passable ground. 0 turns it off.")]
    public double ImpassableMinSlope { get; set; } = 1.5;

    /// <summary>
    /// The slope score at which ground becomes wall, with <see cref="ImpassableRanking"/> Slope:
    /// ruggedness at a barony's scale, read at vanilla's map scale, times the lift of
    /// <see cref="ImpassableLiftScale"/>. Everything at or above it is walled, however much or little
    /// of the map that is, up to <see cref="ImpassableMaxShareOfLand"/>.
    ///
    /// A fixed share walled every world alike: a lowland world spent 8% on rolling hills and a
    /// rugged one left ranges passable. With a line, the terrain decides. 3.5 reproduces about the
    /// 8% the share gave on typical worlds (Rift, scar and inland-sea 8192 worlds: 7–8%), while a
    /// lowland pangaea walls well under 1%, and an archipelago or a 4096 continents world about 10%.
    /// Measured over a fixed stretch of terrain and read at vanilla's scale, so one line serves
    /// every map size: the same pangaea seed at 8192 and 4096 reads alike (99th percentile 3.76 and
    /// 3.69). A kernel fixed in pixels smoothed a 4096 map over twice the terrain, and scaling that
    /// walled Small pangaea worlds a third as much as their 8192 twins.
    /// Recommended: 3.5.
    /// </summary>
    [AdvancedSetting]
    [Category("03 Provinces")]
    [Description("How steep ground must be to become impassable in the slope-ranked cut, as a score at vanilla's map scale. Everything steeper is walled, up to ImpassableMaxShareOfLand, so flat worlds get few walls and rugged ones many. Lower walls more.")]
    public double ImpassableSlopeLine { get; set; } = 3.5;

    /// <summary>
    /// The most land the slope-ranked cut may wall, before the ceiling; past it the line rises
    /// until the walls fit. Only read with <see cref="ImpassableRanking"/> Slope, which reads this
    /// instead of <see cref="ImpassableShareOfLand"/>. Measured on nine worlds, the line alone
    /// walled 0.6–12.4%, so 15% binds only on an unusually rugged map.
    /// Recommended: 0.15.
    /// </summary>
    [AdvancedSetting]
    [Category("03 Provinces")]
    [Description("The most land the slope-ranked cut may make impassable (ground above the ceiling aside). Only binds on unusually rugged maps.")]
    public double ImpassableMaxShareOfLand { get; set; } = 0.15;

    /// <summary>
    /// Slope-ranked walls smaller than this, in baronies, must block something to stay: some trip
    /// between two points just outside the wall has to be at least two barony widths longer for
    /// it. A wall around a lone steep hill keeps nothing apart, and armies walk round it; the same
    /// wall in a neck between the sea and another range closes it. Walls holding ground above
    /// <see cref="ImpassableCeilingHeight"/> are kept, and larger walls are ranges whatever they
    /// block. Only read with <see cref="ImpassableRanking"/> Slope. 0 keeps them all.
    /// Recommended: 4.
    /// </summary>
    [AdvancedSetting]
    [Category("03 Provinces")]
    [Description("Impassable walls smaller than this many baronies are dropped unless they block something, i.e. make some way past them at least two baronies longer. Clears lone steep hills walled for nothing. 0 keeps them all.")]
    public double ImpassableKnobBaronies { get; set; } = 4;

    /// <summary>
    /// How much standing high above the country around counts for, in the slope-ranked cut: ground
    /// this far above its local floor (the lowest ground six barony radii around, smoothed) ranks
    /// as if twice as rugged. In the generator's elevation units, and scaled with
    /// <see cref="ReliefScale"/>. Only read with <see cref="ImpassableRanking"/> Slope.
    ///
    /// Ruggedness alone leaves the upper parts of a massif passable where its crests are finely
    /// ridged, since smoothing to a barony's scale averages the ridges away, while it walls low
    /// coastal bluffs as readily as any range. On the Rift world, lift at 200 kept more of its
    /// massifs walled than ruggedness alone and more than halved the share of the walls standing
    /// on ground below 119 (11% → 5%). 100 cut that further (3%), but left 54% of the steep low
    /// ground passable against 40%.
    /// Relative to the local floor, not sea level, so it does not bring back walls on smooth
    /// uplands: a shoulder standing on a high plain is lifted little. 0 turns it off.
    /// Recommended: 200.
    /// </summary>
    [AdvancedSetting]
    [Category("03 Provinces")]
    [Description("How strongly standing high above the surrounding country counts toward being impassable in the slope-ranked cut: ground this many elevation units above its local floor ranks as if twice as steep. Lower favours massifs over low steep bluffs. 0 turns it off.")]
    public double ImpassableLiftScale { get; set; } = 200;

    /// <summary>
    /// How far down its own slopes an auto-cut wall runs: to where the ground stands this share of
    /// the way from its local floor to its local peak, both measured three baronies around. Only
    /// read with <see cref="ImpassableAutoCut"/> on.
    ///
    /// The walls' cores are chosen by height, which is what keeps a summit or plateau from being
    /// left passable inside its own walls, but a height cut on its own ends every wall at the same
    /// elevation, whatever the range. On a continents map, 80% of wall edges fell between 257 and
    /// 294. With the foot at 0.45 they spanned 198 to 325, winding into valleys and ending where
    /// each range's slopes give out, with the same share of land walled and none of it below 119.
    /// Lower reaches further down the flanks and opens valleys between ranges (0.3 spanned 174 to
    /// 388, and left passable basins inside the largest massifs); 0 turns it off and ends walls on
    /// the height cut alone.
    /// Recommended: 0.45.
    /// </summary>
    [AdvancedSetting]
    [Category("03 Provinces")]
    [Description("How far down its own slopes an auto-cut wall runs, as a share of the local relief from valley floor to peak. Lower walls more of the flanks; 0 ends every wall on one height line.")]
    public double ImpassableFootRelief { get; set; } = 0.45;

    /// <summary>
    /// How rugged ground below the mountain line must be for the auto-cut to wall it, as a multiple
    /// of the map's median ruggedness (the slope of the terrain smoothed to a barony's scale,
    /// averaged over half a barony). Only read with <see cref="ImpassableAutoCut"/> on.
    ///
    /// Without it, height alone decided, and flat tablelands and benches that happened to stand
    /// high were walled while lower but steeper ground beside them was not. On an inland-sea map,
    /// 14.6% of the walls were flat ground below the mountain line; at 1 it was 6.2%, with the same
    /// share of land walled and no passable pocket left standing above its walls. Flat ground mostly
    /// above the mountain line is still taken by <see cref="ImpassableMountainPlateaus"/>. 0 turns
    /// it off.
    /// Recommended: 1.
    /// </summary>
    [AdvancedSetting]
    [Category("03 Provinces")]
    [Description("How rugged ground below the mountain line must be to become impassable, as a multiple of the map's median. Keeps flat tablelands and benches passable up to ImpassableCeilingHeight. 0 turns it off.")]
    public double ImpassableMinRuggedness { get; set; } = 1;

    /// <summary>
    /// The highest the gate line for <see cref="ImpassableMinMountainGround"/> may sit, in the
    /// generator's elevation units (sea level 36, heightmap maximum 520). The default, 238, is
    /// where vanilla's own top 3.5% of land begins on this scale, which is the same share the
    /// mountain line takes.
    ///
    /// The cap exists for high relief. A heightmap that saturates at its ceiling puts its top 3.5%
    /// of land on the flat roof, so a gate on the map's own line would release every real range
    /// below it. On a Highlands pangaea the line was 506 and the uncapped gate released 181
    /// mountain provinces; capped at 238 it released 42 low ones. On a Lowlands map the map's own
    /// line (260 there) is close to the cap anyway. The height above sea scales with
    /// <see cref="ReliefScale"/>. 0 removes the cap.
    /// Recommended: 238.
    /// </summary>
    [AdvancedSetting]
    [Category("03 Provinces")]
    [Description("The highest the mountain-ground gate's line may sit, in elevation units (sea 36, max 520). 238 is where vanilla's top 3.5% of land begins. Stops high, saturated maps reading real ranges as foothills. 0 removes the cap.")]
    public double ImpassableGateHeight { get; set; } = 238;

    /// <summary>
    /// The height above which the auto-cut leaves no mountain ground passable, in the generator's
    /// elevation units (sea level 36, heightmap maximum 520). The default, 400, is about the top of
    /// vanilla's whole map: none of its land stands higher, and none of its passable provinces
    /// stands above about 320. Only read with <see cref="ImpassableAutoCut"/> on.
    ///
    /// <see cref="ImpassableShareOfLand"/> ranks wall cores by height and grows each one down its
    /// own slopes, and on a map whose mountains run far above vanilla's the share runs out first.
    /// On a Standard continents world whose top 3.5% of land began at 475, the cores began at 510,
    /// the tallest ranges spent the share running down their flanks to 230–300, and eleven
    /// highlands peaking at 480–520 were left passable with baronies on their tops, some of them
    /// flat enough for <see cref="ImpassableMinRuggedness"/> to exempt. With the core cut capped
    /// here, and flat ground above it no longer exempt, walls went from 11.6% to 19.3% of land and
    /// passable ground above 400 from 4.4% to 0.1%. A map whose mountains stay below it is
    /// untouched. The height above sea scales with <see cref="ReliefScale"/>. 0 turns it off.
    /// Recommended: 400.
    /// </summary>
    [AdvancedSetting]
    [Category("03 Provinces")]
    [Description("No mountain ground above this height is left passable, in elevation units (sea 36, max 520), whatever the target share. 400 is about the top of vanilla's whole map. Stops tall maps spending the share on their highest ranges and leaving other high peaks and plateaus passable. 0 turns it off.")]
    public double ImpassableCeilingHeight { get; set; } = 400;

    /// <summary>
    /// How steep ground must be for an auto-cut wall to run on down it, in elevation units per
    /// world unit (sea level 36, heightmap maximum 520), measured over about a holding's footprint.
    /// The default, 10, is the steepest ground under any of vanilla's 10,600 holdings (1267 in
    /// 16-bit heightmap units per world unit). Outside <see cref="ImpassableShareOfLand"/>, and
    /// only ground joined to a wall: a cliff with no wall above it stays passable. Only read with
    /// <see cref="ImpassableAutoCut"/> on.
    ///
    /// A wall's foot is measured against its local floor, and beside the sea the floor is the sea,
    /// so the lower half of a sea cliff stayed passable however sheer it was. On a 4096 inland-sea
    /// world (seed 623950) three baronies stood on it, their holdings on ground 2.1–3.3x as steep
    /// as vanilla's steepest, one of them a mountain pass cut straight down the cliff. At 10 the
    /// walls there took 0.9% more land, and the steepest holding went from 4176 to 1292 in 16-bit
    /// units per world unit against vanilla's 1267. Measured on the shipped maps, the same line
    /// would take about 0.06% of land on an 8192 inland-sea world and none on an 8192 continents
    /// world. It can join two walls that stood a cliff apart, which then share
    /// <see cref="MountainPasses"/>' allowance as one wall. Not scaled by
    /// <see cref="ReliefScale"/>: it is vanilla's steepness in world units, which is what that
    /// scaling makes a small map match. 0 turns it off.
    /// Recommended: 10.
    /// </summary>
    [AdvancedSetting]
    [Category("03 Provinces")]
    [Description("Lets an impassable wall run on down any slope steeper than this, in elevation units per world unit, so sea cliffs below a mountain wall are not left as passable ledges with holdings on them. 10 is the steepest ground under any vanilla holding. Only slopes joined to a wall are taken. 0 turns it off.")]
    public double ImpassableCliffSlope { get; set; } = 10;

    /// <summary>
    /// The smallest auto-cut wall kept, in baronies. Only read with <see cref="ImpassableAutoCut"/>
    /// on. Measured after <see cref="ImpassableCrestFollow"/> has had its chance to extend a wall
    /// along its ridge.
    ///
    /// A wall grows from its core only where enough mountain ground lies within a barony's square
    /// window. Around a lone peak poking just above the gate line on an otherwise passable ridge,
    /// that leaves the window's own footprint: one square impassable province on the summit. It
    /// blocks nothing, since the way round it is a province long. On an inland-sea world (seed
    /// 835240), 15 of its 40 walls were under a barony and a half, most filling 80–94% of their
    /// bounding box. At 1.5 with crest-following at 0.8, 10 of them went, 5 grew along their
    /// ridges into walls of their own, and the share of land walled went from 8.0% to 8.1%.
    /// 0.5 is the old floor, the half-barony below which a wall was always dropped.
    /// Recommended: 1.5.
    /// </summary>
    [AdvancedSetting]
    [Category("03 Provinces")]
    [Description("The smallest impassable wall kept, in baronies. A lone peak just over the mountain line otherwise becomes a single square impassable province on its summit. 0.5 is the old floor.")]
    public double ImpassableMinWallBaronies { get; set; } = 1.5;

    /// <summary>
    /// Lets each auto-cut wall run on along its own ridge, over connected ground that stays above
    /// this share of the wall's peak height (measured from sea level), up to
    /// <see cref="ImpassableCrestReachBaronies"/> away, with flanks down to the wall's foot. Only
    /// read with <see cref="ImpassableAutoCut"/> on.
    ///
    /// It is what turns a summit block into the high core of its range instead of dropping it:
    /// on the inland-sea world above, a square of 0.7 baronies became a wall 2.7 baronies long
    /// lying along the crest. 0.7 kept 10 of the 15 small walls rather than 5 and walled 8.8% of
    /// land, but let the walls run down ridges that are not mountains (39% of wall area below the
    /// gate line, against 32% before and 34% at 0.8). 0.85 barely extends anything. 0 turns it off.
    /// Recommended: 0.8.
    /// </summary>
    [AdvancedSetting]
    [Category("03 Provinces")]
    [Description("Lets an impassable wall run on along its ridge over ground above this share of its peak height. Turns a summit block into the high core of its range. 0 turns it off.")]
    public double ImpassableCrestFollow { get; set; } = 0.8;

    /// <summary>
    /// How far <see cref="ImpassableCrestFollow"/> may carry a wall along its ridge, in barony
    /// widths. Bounds the extension, so a wall cannot spread through a whole range system at the
    /// lower line. Measured at 3.
    /// Recommended: 3.
    /// </summary>
    [AdvancedSetting]
    [Category("03 Provinces")]
    [Description("How far an impassable wall may run on along its ridge, in barony widths. Only read with ImpassableCrestFollow above 0.")]
    public double ImpassableCrestReachBaronies { get; set; } = 3;

    /// <summary>
    /// Cuts a pass through an auto-cut wall where it is thin and the way round is long, through its
    /// lowest such neck, as a barony of its own named for the pass and holding a fort and a toll. A
    /// long wall may take another (one per 15 baronies it covers, three at most), but only where
    /// the passes already cut leave the way round long. See <see cref="MapGen.MountainPasses"/>.
    /// Only read with <see cref="ImpassableAutoCut"/> on.
    ///
    /// Vanilla's ranges are broken into chunks with passable mountain provinces between them; the
    /// cut draws a range as one piece, so a long range is a wall its whole length. Surveyed on a
    /// continents world, 7 necks on 6 of its 32 walls were under a barony and a half thick with a
    /// way round at least three times longer — about as many as vanilla's own mountain walls have
    /// (11 on 9), so the walls were no worse than vanilla's; the passes are there to give a range
    /// a door worth holding.
    /// Recommended: on.
    /// </summary>
    [Category("03 Provinces")]
    [Description("Cut a narrow pass through an impassable wall where the wall is thin and the way round is long — one per wall, more on a long one where the first leaves the way round long. The pass is a barony of its own, named for it, with a fort and a toll.")]
    public bool MountainPasses { get; set; } = true;

    /// <summary>
    /// The thickest a wall may be where a pass is cut, in barony widths. The survey's necks ran
    /// 0.45 to 1.44. Only read with <see cref="MountainPasses"/> on.
    /// Recommended: 1.5.
    /// </summary>
    [AdvancedSetting]
    [Category("03 Provinces")]
    [Description("The thickest a wall may be where a pass is cut through it, in barony widths.")]
    public double MountainPassMaxThickness { get; set; } = 1.5;

    /// <summary>
    /// How far round a wall the land route between a pass's two ends must be before the pass is
    /// cut, in barony widths — and at least four times the wall's thickness there. Lower gives more
    /// passes, including through walls a short walk already goes round. On a 4096 inland-sea world,
    /// whose walls cover 2 to 30 baronies, 8 would cut 3 passes, 5 cut 4 and 3.5 cut 6: a thin
    /// strip on a small map is often only 4–7 baronies' walk round.
    /// Only read with <see cref="MountainPasses"/> on.
    /// Recommended: 5.
    /// </summary>
    [AdvancedSetting]
    [Category("03 Provinces")]
    [Description("How long the way round a wall must be, in barony widths, before a pass is cut through it. Lower gives more passes.")]
    public double MountainPassMinDetour { get; set; } = 5;

    /// <summary>
    /// Makes a land province with more than half its ground above the mountain line impassable
    /// outright, outside the target share, as <see cref="ImpassableHeightFraction"/> already does
    /// for the roof of the map.
    ///
    /// The score is mostly steepness, and a plateau top is flat, so the quota fills with the steep
    /// flanks around a massif before it reaches the top. The massif then comes out walled around its
    /// edge with a passable plateau in the middle. On a small Lowlands pangaea this took 6 such
    /// provinces (52 → 58 walls, mountain ground walled 58.5% → 92.6%); on the Highlands pangaea it
    /// took 11, and on a large Lowlands pangaea it took none, because the score had already taken
    /// every one.
    /// Recommended: on.
    /// </summary>
    [AdvancedSetting]
    [Category("03 Provinces")]
    [Description("A province more than half above the mountain line is impassable regardless of the target share. Stops a massif being walled around its edge with a passable plateau on top.")]
    public bool ImpassableMountainPlateaus { get; set; } = true;

    /// <summary>
    /// A land province with most of its ground at or above this fraction of the way from sea level
    /// to the map's highest point is impassable outright, whatever its slope score and whatever the
    /// target share. It exists for high plateaus: flat on top, so the slope-weighted score passes
    /// over them, yet so high no road should cross. Kept near 1 so it only takes the true roof of
    /// the map and the slope ranking still decides nearly everything. 0 turns it off.
    /// Recommended: 0.98.
    /// </summary>
    [Category("03 Provinces")]
    [Description("A land province with most of its ground at or above this fraction of the way from sea level to the map's highest point is impassable regardless of slope or the target share — catches high, flat plateaus the slope score misses. Near 1 it only takes the roof of the map. 0 turns it off.")]
    public double ImpassableHeightFraction { get; set; } = 0.98;

    [AdvancedSetting]
    [Category("03 Provinces")]
    [Description("Minimum gradient per pixel for ground to count as steep, so a flat map does not get its gentlest slopes declared cliffs just because they are its steepest. Authored against vanilla-scale terrain and scaled by the same factor as land relief, so it means the same thing at any map size. It is a floor on the steep line only — it never removes pixels from the percentile that sets that line.")]
    public double MinPhysicalSlope { get; set; } = 0.15;

    /// <summary>
    /// Share of land considered candidate steep ground, as a percentile of the map's slopes.
    /// Recommended: 0.20 (the top 20% steepest relief).
    /// </summary>
    [Category("03 Provinces")]
    [Description("Share of land counted as steep ground, as a percentile of this map's own slopes.")]
    public double SteepLineShare { get; set; } = 0.20;

    /// <summary>
    /// Share of land put above the mountain elevation line. 
    /// Vanilla's own heightmap has ~3.3% in its high mountain band.
    /// Recommended: 0.035.
    /// </summary>
    [AdvancedSetting]
    [Category("7 Height scale")]
    [Description("Share of land put above the mountain line. Vanilla's heightmap has 3.3% of its land in the high mountain band.")]
    public double MountainLineShare { get; set; } = 0.035;

    /// <summary>
    /// Largest a fused impassable mountain range may get, in baronies' worth of area.
    /// Recommended: 8.
    /// </summary>
    [Category("03 Provinces")]
    [Description("Largest a fused impassable mountain range may get, measured in baronies' worth of area. Touching impassable provinces are merged so a range reads as one continuous wall.")]
    public double ImpassableRangeMaxBaronies { get; set; } = 8;
    [AdvancedSetting]
    [Category("03 Provinces")]
    [Description("Smallest allowed province in pixels. Below this CK3 cannot derive borders, a centroid or locator positions and crashes in geometry code without logging anything.")]
    public int MinProvincePixels { get; set; } = 32;

    /// <summary>
    /// Rows and columns of forced ocean around the edge of the province map, in province pixels.
    /// Off by default; see below for what turning it on costs.
    ///
    /// The case for it: vanilla has water along every edge — its top and bottom rows are entirely
    /// sea, and its province map has only a handful of large ocean provinces touching them. A
    /// generated map happily runs land off the poles instead: on seed 1 at vanilla size, 33 land
    /// provinces touched the top edge and 17 the bottom. A province clipped by the map boundary
    /// has an open border, which is the sort of thing a boundary-following walk cannot close.
    ///
    /// The case against, and why it is now 0: a one-pixel ring is not an ocean, it is a channel.
    /// The sea seeds are scattered before the ring exists, so the ring never becomes a province of
    /// its own — it is absorbed into whichever ocean province grows into it. Every land province
    /// that reached the boundary therefore came out COASTAL, sea-connected around the whole rim to
    /// every other one, and an inland region that merely happened to touch the pole could be
    /// invaded by sea. Drowning the edge fixed the geometry by creating a gameplay hole.
    ///
    /// Nothing downstream needs the ring. <c>Drainage.WaterBodies</c> says so in as many words —
    /// if no water body touches the border the largest one stands in for the ocean — and
    /// <c>ProvinceAnchor.DistanceFromEdge</c> already counts the map boundary as a province border,
    /// so a clipped province does not read as infinitely deep when locators are placed.
    ///
    /// The `provinces touching the map edge` line in the province report is the measurement to
    /// watch: it is what this setting moves, and it is how a seed that genuinely runs a continent
    /// off the pole would show up.
    ///
    /// If a future map does need the edge closed, the right shape is not a wider ring — it is a
    /// ring carved into sea provinces of its own and declared `impassable_seas` in default.map,
    /// which is what vanilla does at its own fringe (see the `IMPASSABLE SEA ZONES` block there).
    /// That keeps the water without making it sailable.
    /// </summary>
    [AdvancedSetting]
    [Category("03 Provinces")]
    [Description("Rows and columns of forced ocean around the edge of the province map, in province pixels. 0 (the default) lets land run to the boundary. Raising it drowns the edge so no province is clipped — but the ring is absorbed into the neighbouring ocean rather than becoming its own province, so every land province at the boundary turns coastal and the whole rim becomes one sailable waterway.")]
    public int OceanBorder { get; set; } = 0;


    // =========================================================================
    // 04 Titles
    // =========================================================================

    /// <summary>
    /// Fewest children a duchy, kingdom or empire may have and still stand as its own title.
    /// Anything below this is folded into a neighbour, across a strait if need be.
    ///
    /// Deliberately far below the per-tier minimums, which are growth *targets* rather than floors.
    /// Clustering leaves undersized scraps all over a map, not only on islands, so absorbing
    /// everything under the target cascades — measured at 2, a 370-county map keeps 59 duchies and
    /// 12 kingdoms; at the duchy target of 4 the same map collapses to a single empire holding
    /// seven kingdoms. This is a floor for absurdity, not a lever for realm size.
    /// </summary>
    /// <summary>
    /// Fewest baronies a county is grown towards.
    ///
    /// A target rather than a guarantee: a cluster that runs out of unclaimed neighbours stops where
    /// it is, because an island holds what it holds. Ignored on an imported map, where Azgaar's own
    /// provinces decide where the county borders fall.
    /// </summary>
    [Category("04 Titles")]
    [AzgaarIncompat("Counties follow the export's provinces: baronies are grouped inside one Azgaar province " +
                    "at a fixed 3–7 band, so neither end of this is consulted. Read again only if the export " +
                    "has no cell data and the geometric hierarchy runs instead.")]
    [Description("Fewest baronies a county is grown towards. A cluster that runs out of unclaimed neighbours can still end up under this — an island holds what it holds.")]
    public int MinBaroniesPerCounty { get; set; } = 3;

    /// <summary>
    /// Most baronies a county may hold, and unlike the floor this one is absolute: nothing is ever
    /// moved into a county that has already reached it. Lowering it buys more, smaller counties out
    /// of the same provinces rather than fewer provinces.
    /// </summary>
    [Category("04 Titles")]
    [AzgaarIncompat("Counties follow the export's provinces: baronies are grouped inside one Azgaar province " +
                    "at a fixed 3–7 band, so neither end of this is consulted. Read again only if the export " +
                    "has no cell data and the geometric hierarchy runs instead.")]
    [Description("Most baronies a county may hold. A hard ceiling on the procedural path: nothing is ever moved into a county that has already reached it. Lowering it buys more, smaller counties out of the same provinces rather than fewer provinces.")]
    public int MaxBaroniesPerCounty { get; set; } = 7;

    [Category("04 Titles")]
    [Description("Fewest children a duchy, kingdom or empire may have and still exist. 2 stops one-province islands from founding a duchy, a kingdom and an empire on the way up. Raising it much past 2 cascades and collapses the hierarchy.")]
    public int MinChildrenPerTitle { get; set; } = 4;

    /// <summary>
    /// Whether a title's colour is shaded from its parent's, so a duchy reads as part of its
    /// kingdom in the de jure map modes, or whether every title is given a hue of its own.
    ///
    /// On, the empires are spread as far apart in hue as a golden angle will put them and each tier
    /// below shades away from its parent — legible de jure borders, at the cost of neighbouring
    /// counties inside one realm looking nearly identical. Off, every title from empire down to
    /// barony takes its own place in the same golden-angle sequence, which is the patchwork look:
    /// adjacent counties are always told apart, and nothing about the colour says who their liege
    /// is.
    ///
    /// On an imported map the export still wins at the state tier either way — this only decides
    /// whether the tiers below a state are shaded from that state's colour or keep hues of their
    /// own.
    /// </summary>
    [Category("04 Titles")]
    [Description("Shade each title's colour from its parent's, so a duchy reads as part of its kingdom in the de jure map modes. Off, every title gets a hue of its own and neighbouring counties are always told apart. Azgaar imports paint the state tier from the export either way.")]
    public bool DeJureColorCoding { get; set; } = true;

    /// <summary>
    /// How wide a stretch of water a kingdom or empire may still reach across, in *vanilla*
    /// province pixels. Counties and duchies ignore this and stay on one landmass.
    ///
    /// Measured against vanilla's 9216x4608 province map: the Strait of Dover is about 30 px, the
    /// Irish Sea about 90, the Sicilian narrows about 25, and the Aegean crossings 40-120. The
    /// default therefore reaches the seas real medieval realms actually spanned without letting a
    /// kingdom claim another continent.
    /// </summary>
    [AdvancedSetting]
    [Category("04 Titles")]
    [Description("How wide a stretch of water a kingdom or empire may reach across, in vanilla province pixels. Counties and duchies always stay on one landmass. Vanilla reference: Dover about 30 px, the Irish Sea about 90.")]
    public double SeaBridgePixelsAtVanilla { get; set; } = 110;

    /// <summary>
    /// How wide a strait or river an army may still march across, in *vanilla* province pixels.
    /// This is the map_data/adjacencies.csv threshold — a real crossing, drawn on the map and
    /// blockable by a fleet — and so much narrower than the realm bridge above: vanilla crosses
    /// the Øresund and the Bosporus, about 10 px, and the Sicilian narrows at 25, but never the
    /// Strait of Dover at 30. Zero declares no crossings at all. See MapGen/Provinces/Crossings.cs.
    /// </summary>
    [AdvancedSetting]
    [Category("04 Titles")]
    [Description("How wide a strait or major river an army may march across, in vanilla province pixels. Vanilla reference: the Øresund about 10 px, Messina 25, and Dover at 30 is not crossable. 0 declares no crossings.")]
    public double StraitPixelsAtVanilla { get; set; } = 28;

    /// <summary>
    /// How deep inside its province a holding, army or siege model must stand, as a fraction of
    /// the deepest point that province has. 0 lets a model sit on the border; 1 pins it to the
    /// single deepest pixel and leaves flatness no say.
    /// </summary>
    [AdvancedSetting]
    [Category("04 Titles")]
    [Description("How deep inside its province a holding or army model must stand, as a fraction of that province's deepest point. Raising it keeps models further from coastlines; lowering it lets flatness matter more than position.")]
    public double LocatorInteriorFraction { get; set; } = 0.6;

    /// <summary>
    /// How much a model prefers the middle of its province over flat ground. Measured against the
    /// map's median slope, so 1 means being a province-radius off centre costs as much as standing
    /// on ground one median slope steeper.
    /// </summary>
    [AdvancedSetting]
    [Category("04 Titles")]
    [Description("How much a holding prefers the middle of its province over flat ground. 0 puts it on the flattest eligible pixel wherever that is; higher pulls it toward the centre even if the ground there is steeper.")]
    public double LocatorCentroidPull { get; set; } = 0.75;

    /// <summary>
    /// The steepest ground a holding stands on when its province has anything gentler, in
    /// elevation units per world unit (sea level 36, heightmap maximum 520), measured over about a
    /// holding's footprint. The default, 5, is the 99th percentile of the ground under vanilla's
    /// 10,600 holdings (655 in 16-bit heightmap units per world unit; the steepest is 1267).
    ///
    /// Only where the province's interior core has nothing under the line does the holding move
    /// out of it: to half the core's depth, then anywhere at least five pixels inside the border.
    /// A province with no gentle ground at all keeps its least steep. A province whose holding
    /// already stood on gentle ground keeps exactly the same spot. Armies and sieges stand where
    /// the holding does. On an 8192 scar world 12 holdings moved, 7 of them out of the core, and
    /// holdings steeper than vanilla's 99th percentile went from 14 to 3 (those at the line), with
    /// none past its 99.9th. It cannot help a province that is all slope; that is the partition's
    /// to fix. 0 turns it off.
    /// Recommended: 5.
    /// </summary>
    [AdvancedSetting]
    [Category("04 Titles")]
    [Description("Keeps holdings off ground steeper than this, in elevation units per world unit, unless their province has nothing gentler. 5 is steeper than 99% of the ground vanilla's holdings stand on. 0 turns it off.")]
    public double LocatorMaxSlope { get; set; } = 5;

    /// <summary>
    /// How far a special building stands from the holding it shares a province with, in world
    /// units — which are province pixels, see <see cref="Emit.WorldSpace"/>.
    ///
    /// The default is vanilla's own median. Measured over the 11,297 provinces that carry both
    /// locators, the gap between a holding and its special building runs p25 7.3, median 9.1,
    /// p75 11.6 world units.
    ///
    /// Deliberately **not** scaled by <see cref="MapScale"/>. The thing this distance has to clear
    /// is the holding mesh, and meshes are a fixed size in world units no matter how large the
    /// province map is — a smaller map makes buildings occupy proportionally more of it, which is
    /// an argument for keeping the absolute gap, not for shrinking it. What adapts to small
    /// provinces instead is the fallback in <see cref="MapGen.ProvinceAnchor"/>, which pulls the
    /// offset in when the full distance would leave the province or land in water.
    /// </summary>
    [AdvancedSetting]
    [Category("04 Titles")]
    [Description("How far a special building (a generated wonder) stands from its holding, in world units. Vanilla's median is 9. Reduced automatically where the province is too small or too coastal to fit it.")]
    public double SpecialBuildingOffset { get; set; } = 9.0;


    // =========================================================================
    // 05 Rivers
    // =========================================================================

    [Category("05 Rivers")]
    [Description("Enable navigable major river corridors carved into the heightmap as river provinces.")]
    public bool EnableMajorRivers { get; set; } = true;

    /// <summary>
    /// How much navigable major river the map gets, as world units of river course per thousand
    /// square world units of land — a drainage density, so the same number means the same thing on
    /// any size of map and any shape of land.
    ///
    /// Replaced MajorRiverCount (8), which was not scaled by anything: a 4096 world and a 9216 one,
    /// a Pangaea and an archipelago, all got eight chosen rivers, and qualifying lake outlets came
    /// on top — on a 2026-09-26 archipelago that was 14 lake systems against the 8 the setting
    /// governed. Now rivers are traced from the lake outlets and then the sea outlets by falling
    /// discharge until the courses add up to this much length. Lake outlets count against it but
    /// are carved regardless, and the last river is kept whole rather than cut to fit: how far up a
    /// river is carved stays the business of <see cref="RiverTraceMinFlow"/> and
    /// <see cref="RiverMaxRiseAboveSea"/>.
    ///
    /// Calibrated on vanilla 1.19, whose river provinces run about 0.95 units per thousand square
    /// units of land (22,000 units of river over 23.7 million of land, two estimators agreeing:
    /// area over mean width, and half the perimeter). A map with too little room for long rivers
    /// falls short rather than inventing them, and the log says how short.
    /// </summary>
    [Category("05 Rivers")]
    [Description("How much navigable major river the map gets: world units of river per thousand square units of land, so it scales with the land rather than being a fixed count. Vanilla has about 1. Lake outlets count towards it but are always carved; a map without room for long rivers falls short rather than inventing them.")]
    public double MajorRiverDensity { get; set; } = 1.0;

    /// <summary>
    /// How large a body of water a major river has to empty into, measured in sea zones.
    ///
    /// Expressed in sea zones rather than raw pixels so it tracks <see cref="SeaZonePixels"/> and
    /// therefore <see cref="CountyScale"/> — the question is whether the receiving water reads as a
    /// sea on *this* map, which is the same question as whether it could hold a few sea provinces.
    ///
    /// Gating on size rather than on connectivity to the ocean is deliberate: rivers that end in a
    /// closed basin are real, and the Volga and the Amu Darya should both survive this test. What
    /// should not survive is a river emptying into a three-cell speck left by a couple of
    /// below-sea-level pixels in the heightmap.
    /// </summary>
    [AdvancedSetting]
    [Category("05 Rivers")]
    [Description("How big the water at a major river's mouth must be, counted in sea zones. Stops rivers from draining into tiny inland dips in the heightmap, while still allowing genuine inland seas. Raise it to insist rivers reach open ocean.")]
    public double MinOutletSeaZones { get; set; } = 0.5;

    /// <summary>
    /// How large a lake has to be, in sea zones, before it is given a navigable outlet river
    /// carved from its spill down to the sea.
    ///
    /// Every lake drains — the drainage treats a lake as terrain the flood fills to its rim, so its
    /// whole catchment flows on over the spill — but only a lake of some size earns a carved
    /// corridor, because the channel is a fixed width and a pond narrower than the river leaving
    /// it reads as a mistake. Smaller lakes still get their outlet drawn as a tributary in
    /// rivers.png wherever the discharge merits one. Lakes that qualify are always traced, and
    /// traced first: their courses count towards <see cref="MajorRiverDensity"/> but a lake's
    /// outlet is not a choice, so it is carved even when it overspends the budget.
    /// </summary>
    [AdvancedSetting]
    [Category("05 Rivers")]
    [Description("How big a lake must be, counted in sea zones, before a navigable river is carved from it to the sea. Lakes that qualify are always carved, and their rivers count towards the major river density. Lower to connect smaller lakes; raise to leave them to tributaries in rivers.png.")]
    public double LakeOutletMinSeaZones { get; set; } = 0.1;

    /// <summary>
    /// Width of a major river's carved channel at its source, bank to bank, in world units — which
    /// are province pixels on every map, whatever <see cref="ProvinceDownscale"/> is.
    ///
    /// <b>Not scaled by <see cref="MapScale"/>, deliberately.</b> A river is sized against the
    /// counties it runs between, and barony size does not shrink with the map: a 4096-wide world
    /// has baronies larger in pixels than vanilla's (median 2255 px against 1005, measured
    /// 2026-09-26). Scaling the channel down with the map would thin it against the land it cuts.
    ///
    /// Calibrated on vanilla 1.19. The water CK3 draws over vanilla's river provinces — the
    /// heightmap under the plane at 3932 — is 7 world units across at the median, 6–9 from p10 to
    /// p90; the river provinces themselves are 3–5. The defaults here and in
    /// <see cref="RiverChannelWidthMax"/> aim for one and a half times that.
    ///
    /// Width follows discharge: this is the width at the smallest flow a major river is traced at
    /// (<see cref="RiverTraceMinFlow"/>) and the Max is the width at the largest river mouth on the
    /// map, on a log scale between. Measured 2026-09-26 on an 8192 archipelago world, that put the
    /// channel at 9.0/10.8/13.2 units wide at p10/p50/p90, wobble included.
    ///
    /// These replace RiverChannelRadiusMin/Max (9 and 14), which were radii in *vanilla heightmap*
    /// pixels — two per world unit — and were never converted when ProvinceDownscale went to 1, then
    /// floored at 7 and 16 regardless of setting. Together that carved rivers about three times
    /// vanilla's width and put 8.6% of the land into river provinces against vanilla's 0.45%. The
    /// rename is so a preset holding the old numbers drops them rather than reading them in these
    /// units.
    /// </summary>
    [AdvancedSetting]
    [Category("05 Rivers")]
    [Description("Width of a major river's channel at its source, bank to bank, in world units (province pixels). Not scaled with map size, because counties are not. Width follows how much water a river carries: this is the width of the smallest, the Max that of the biggest river mouth on the map. Vanilla's rivers draw about 7 across. The carve will not go below 5, which keeps the river province chain unbroken.")]
    public double RiverChannelWidthMin { get; set; } = 7.0;

    /// <summary>
    /// Width of the map's biggest major river at its mouth, bank to bank, in world units. Same
    /// convention and calibration as <see cref="RiverChannelWidthMin"/>.
    /// </summary>
    [AdvancedSetting]
    [Category("05 Rivers")]
    [Description("Width of the map's biggest major river at its mouth, bank to bank, in world units (province pixels). Every other river is placed between the Min and this by how much water it carries.")]
    public double RiverChannelWidthMax { get; set; } = 13.0;

    /// <summary>
    /// How much a major river's channel breathes in and out along its length, as a fraction of the
    /// radius it would otherwise have. 0.25 lets it run between three quarters and five quarters of
    /// its nominal width.
    ///
    /// Without this the channel only ever widens downstream, stepping out where tributaries come
    /// in, and never does anything else — which is the one thing a real river never does. The variation is low-frequency — see
    /// <see cref="RiverWidthVariationScale"/> — because per-vertex jitter reads as a ragged edge
    /// rather than as narrows and broads.
    ///
    /// The valley follows the channel, so a wide reach gets a wide flood plain and a narrow one a
    /// gorge. Never allowed to pinch the channel below the navigable floor, except at the head where
    /// the taper is deliberately closing it.
    /// </summary>
    [Category("05 Rivers")]
    [Description("How much a major river widens and narrows along its length, as a fraction of its nominal width. 0 gives a channel that only ever widens downstream as it gathers water; 0.25 lets it run between three quarters and five quarters of that. The flood plain follows, so wide reaches get wide plains.")]
    public double RiverWidthVariation { get; set; } = 0.25;

    /// <summary>
    /// The distance over which a major river's width variation completes one cycle, in vanilla
    /// province pixels. Deliberately long: this decides where a river has narrows and broads, which
    /// happens over tens of kilometres, not between one pixel and the next.
    /// </summary>
    [AdvancedSetting]
    [Category("05 Rivers")]
    [Description("How far a major river runs between one narrowing and the next, in vanilla province pixels. Long values give a few slow swells along a river; short ones make the banks look ragged rather than varied.")]
    public double RiverWidthVariationScale { get; set; } = 150.0;

    /// <summary>
    /// The flat ground either side of a major river, as a multiple of the channel's width, measured
    /// out from each bank. Sits just above the water and rises only slightly before the valley wall
    /// begins, so a wide reach gets a wide plain and a narrow one a narrow strip.
    ///
    /// The valley replaced RiverValleyReach, which ramped from the bank back to the original ground
    /// over a fixed multiple of the channel. A major river has to run at sea level the whole way,
    /// so wherever its course is high the ramp became a trench — the whole height lost over a few
    /// pixels. The wall is now slope-limited instead: see <see cref="RiverValleyWallSlope"/>.
    /// </summary>
    [AdvancedSetting]
    [Category("05 Rivers")]
    [Description("Width of the flat flood plain on each bank of a major river, as a multiple of the channel's width. 0 puts the valley wall right at the water's edge.")]
    public double RiverFloodplainWidth { get; set; } = 0.75;

    /// <summary>
    /// The steepest a major river's valley wall is allowed to be, in elevation units per world
    /// unit, before it is scaled by <see cref="ReliefScale"/> as every authored gradient is.
    ///
    /// The channel is at sea level wherever it runs, so the height of the land beside it has to be
    /// lost somewhere. This spends it over distance: the higher the ground, the wider the valley,
    /// and the wall never gets steeper than this. Where even that would reach past
    /// <see cref="RiverValleyMaxReach"/>, the wall steepens just enough to fit — a gorge only where
    /// the land leaves no room for anything else.
    /// </summary>
    [AdvancedSetting]
    [Category("05 Rivers")]
    [Description("Steepest slope allowed for a major river's valley walls, in elevation units per world unit. Lower gives broad, gentle valleys that spread further into high ground; higher gives narrower valleys with steeper sides.")]
    public double RiverValleyWallSlope { get; set; } = 1.5;

    /// <summary>
    /// The furthest a major river's valley may reach from its centreline, in world units. Bounds
    /// how much land one river can lower; past it the wall steepens rather than spreading.
    /// </summary>
    [AdvancedSetting]
    [Category("05 Rivers")]
    [Description("Furthest a major river's valley may reach from the middle of the river, in world units. Where high ground would need a wider valley than this, the walls steepen instead.")]
    public double RiverValleyMaxReach { get; set; } = 45.0;

    /// <summary>
    /// Spacing of river province seeds along a carved corridor, in vanilla province pixels. This is
    /// what decides how many provinces a river becomes, and so how finely it is named and travelled.
    /// </summary>
    [AdvancedSetting]
    [Category("05 Rivers")]
    [Description("How long each navigable river province is, in vanilla province pixels. Lower chops a river into more, smaller provinces; higher makes each stretch longer.")]
    public double RiverProvinceLength { get; set; } = 35.0;

    /// <summary>
    /// How far above sea level a major river will climb before its trace stops, in elevation units.
    /// Keeps navigable corridors out of the mountains rather than trenching a canyon up to a peak.
    /// </summary>
    [Category("05 Rivers")]
    [Description("How far above sea level a major river will climb before it stops, in elevation units. Keeps navigable corridors in the lowlands instead of trenching up into the mountains.")]
    public double RiverMaxRiseAboveSea { get; set; } = 120.0;

    /// <summary>
    /// The discharge at which a major river's upstream trace gives up, in the same units as
    /// <see cref="MapGen.Drainage.Flow"/>. Lower carries the corridor further into the headwaters.
    /// </summary>
    [AdvancedSetting]
    [Category("05 Rivers")]
    [Description("The discharge at which a major river is declared finished and its trace stops. Lower carries the navigable corridor further upstream into smaller tributaries.")]
    public double RiverTraceMinFlow { get; set; } = 350.0;

    [Category("05 Rivers")]
    [Description("Enable tributary minor rivers drawn onto rivers.png.")]
    public bool EnableMinorRivers { get; set; } = true;

    [Category("05 Rivers")]
    [Description("Density multiplier for minor tributary rivers (1.0 = standard vanilla density, 0.5 = sparser, 2.0 = denser).")]
    public double RiverDensity { get; set; } = 0.1;


    // =========================================================================
    // 06 Map Objects
    // =========================================================================

    [Category("06 Map Objects")]
    [Description("Enable decorative wildlife herds (sheep flocks, grazing/galloping wild horses, and solitary elephants) on unit_layer.")]
    public bool EnableAnimals { get; set; } = true;

    [Category("06 Map Objects")]
    [Description("Density multiplier for wildlife herds across the map (1.0 = standard, 0.5 = sparser, 2.0 = denser).")]
    public double AnimalDensity { get; set; } = 1.0;

    [AdvancedSetting]
    [Category("06 Map Objects")]
    [Description("Scale multiplier for animal models on the 3D map. 1.0 is vanilla size, which the models are drawn at on every map size.")]
    public double AnimalScale { get; set; } = 1.0;

    [AdvancedSetting]
    [Category("06 Map Objects")]
    [Description("Allow wild horses to use the animated galloping variant on wide, flat plains and steppes.")]
    public bool EnableGallopingHorses { get; set; } = true;

    /// <summary>
    /// Size multiplier for the holding models — the castle, city and temple meshes drawn on every
    /// barony.
    ///
    /// 1.0 is vanilla size and is deliberately the default: holdings are the one class of map object
    /// whose size the player reads as "how big is a settlement", and vanilla's meshes are already
    /// tuned against vanilla's own province sizes. It is a knob rather than something derived from
    /// <see cref="MapScale"/> because the right answer depends on <see cref="CountyScale"/> too —
    /// a small map with correspondingly small baronies wants vanilla-sized holdings, while a small
    /// map with vanilla-sized baronies does not.
    ///
    /// Scaling up is the risky direction. Holdings sit at province anchors, so past roughly 1.5x on
    /// a dense map neighbouring baronies start intersecting each other, and armies, sieges and
    /// activity markers keep their own sizes regardless of this — a castle twice its usual size
    /// standing next to a normal army stack reads worse than either error on its own.
    ///
    /// At exactly 1.0 nothing is written and vanilla's own assets are left alone. See
    /// <see cref="Emit.HoldingModelWriter"/>.
    /// </summary>
    [Category("06 Map Objects")]
    [Description("Size multiplier for holding models (castles, cities, temples) on the 3D map. 1.0 is vanilla size and leaves the game's own assets untouched. Above 1 makes settlements read larger; scaling far past 1.5 makes neighbouring baronies overlap on a dense map.")]
    public double HoldingScale { get; set; } = 1.0;

    /// <summary>
    /// PROTOTYPE — the whole feature is <see cref="Emit.CityScatterWriter"/>, this flag and one
    /// call site, on its own Rng stream, so turning it off changes nothing else in the output.
    /// </summary>
    [Category("06 Map Objects")]
    [Description("Scatters small suburb models around settled holdings, sized by development and styled by the local culture, so towns grow visible outskirts and no two look alike. PROTOTYPE - the placements are baked at generation and do not change in-game. Turning it off removes them completely and affects nothing else.")]
    public bool EnableCityScatter { get; set; } = true;

    /// <summary>
    /// Whether the map table keeps its clutter — the candles, goblets, coins, chess pieces and
    /// ground props that dress vanilla's four tabletops.
    ///
    /// On, because every one of these objects is <c>render_pass=MapUnderTerrain</c>: the map itself
    /// occludes the parts of them that lie under it, and only the overhang past the map's edge is
    /// ever drawn, which is exactly where the props are meant to read. Six objects across the four
    /// styles were being dropped, and with them ep3's table lost the only dressing it has.
    ///
    /// Off if the candle flames misbehave. Four of the six hang <c>flame_*_entity</c> and
    /// <c>candle_glow</c> off their bones as attachments; those are separate entities and the
    /// layer's fade does not govern them, so they can outlive the table on the way in. See
    /// <see cref="Emit.MapTableWriter"/>.
    /// </summary>
    [AdvancedSetting]
    [Category("06 Map Objects")]
    [Description("Keep the map table's candles, goblets, coins and ground props. They render under the terrain, so only the parts overhanging the map's edge are visible. Turn off if candle flames show through the map when zooming in — the flames are attached particle entities the layer fade does not reach.")]
    public bool MapTableProps { get; set; } = true;

    /// <summary>
    /// Tucks map-table props under the paper map: the parts of them that would stand on top of
    /// the map are cut from copies of vanilla's tabletop meshes, so props at the edge read as
    /// sliding under the paper. Vanilla's props reach into the strip its torn-paper map edge cuts
    /// away; ours is a full rectangle, so tall props there drew over the map. Nothing moves, so the
    /// props stay on the shadows painted into the tabletop textures. Objects with locators (the
    /// candle flames, ep3's lamps) are left vanilla. See <see cref="Emit.TablePropTucker"/>.
    /// </summary>
    [Category("06 Map Objects")]
    [DisplayName("Map Table Props Off Map")]
    [Description("Tuck map-table props under the paper map: the parts that would sit on top of the map are cut away, so props at the edge look like they slide under it. Candles and lamps are left as they are.")]
    public bool MapTablePropsOffMap { get; set; } = true;

    /// <summary>
    /// Extra world units the whole map-table tableau is pushed down, away from the map.
    ///
    /// Vanilla's tabletops are not entirely below the paper map. Measured from the shipped meshes
    /// against <c>FLAT_MAP_HEIGHT = 3.92</c>, the world Y the flat map is drawn at, the *furniture*
    /// — the map-spanning opaque surfaces the map lies on — straddles the plane:
    ///
    ///     tgp      tgp_tabletop_01              +28.42     24.50 ABOVE the map
    ///     ce1      barsShape                    +11.99      8.08 ABOVE
    ///     western  barsShape                    +11.99      8.08 ABOVE
    ///     ep3      ep3_cloth_patternShape        -5.89      9.81 below
    ///     ep3      ep3_byzantium_cloth          -19.44     23.36 below
    ///     ce1      tablecloth_decorationShape   -21.84     25.76 below
    ///     western  tablecloth_decorationShape   -21.84     25.76 below
    ///     ce1      tableShape                   -39.83     43.75 below
    ///
    /// Everything from ep3's cloth pattern up is close enough to coplanar with the map to fight it
    /// for the depth buffer, and all of it spans the map rather than only the overhang. That is
    /// what the tablecloth flickering through the map is. <c>render_pass=MapUnderTerrain</c> hides
    /// table geometry that is *clearly* above the map — ce1's scrolls sit 149 units up and are
    /// never seen over it — but it cannot separate two surfaces eight units apart seen from two
    /// thousand.
    ///
    /// 50 puts every one of those below the plane by at least 25.5, which is the gap vanilla's own
    /// tablecloth already has and therefore a separation the game demonstrably renders cleanly. It
    /// costs a wider gap between the map and the cloth — 75.8 against 25.8 — which is 0.5% of a
    /// vanilla-width map and reads as nothing at the zoom the table is seen from. 0 restores
    /// vanilla's offsets exactly.
    ///
    /// It does not fix the *props*, and cannot: candles, coins and books are spread continuously
    /// through the same band (vanilla has a candle flame 1.71 above the plane and ep3's eagle 61
    /// above), so any rigid drop that clears one drags another in. They are small sparse objects
    /// rather than map-wide planes, so they shimmer at a point instead of across the map, and
    /// <see cref="MapTableProps"/> is the lever that removes them outright.
    ///
    /// This is separate from the map-size correction in <see cref="Emit.MapTableWriter"/>, which
    /// keeps that gap constant in absolute units as the map shrinks. Both land as one translation.
    /// </summary>
    [AdvancedSetting]
    [Category("06 Map Objects")]
    [Description("World units the map table is pushed down, away from the map. Vanilla's tabletops carry map-spanning surfaces up to 24 units ABOVE the flat map plane, close enough to fight it for the depth buffer — which is the tablecloth flickering through the map. The tableau moves rigidly, so this only opens the gap between the map and the cloth. Does not affect the candles and clutter, which straddle the map plane by design — use Map Table Props for those. 0 restores vanilla's offsets exactly.")]
    public double MapTableClearance { get; set; } = 50.0;

    [Category("06 Map Objects")]
    [Description("Density multiplier for trees and ground foliage. 1.0 is about vanilla's own density per land pixel (trees are drawn at vanilla size on every map, so this is also vanilla's canopy); 0.5 = sparser, 2.0 = denser. Costs load time and memory in the game at high values — every instance is written out individually.")]
    public double TreeDensity { get; set; } = 1.65;

    [Category("06 Map Objects")]
    [Description("Global multiplier for atmospheric environmental visual effects (dust plumes, forest mist, mountain snow clouds). 1.0 matches vanilla density scaled to this map's resolution.")]
    public double EnvEffectDensity { get; set; } = 0.35;

    [AdvancedSetting]
    [Category("06 Map Objects")]
    [Description("Global scale multiplier for environmental VFX billboards. 1.0 is vanilla size, which they are drawn at on every map size.")]
    public double EnvEffectScale { get; set; } = 0.9;

    /// <summary>
    /// Inks the route network onto the parchment flat map: trunk routes heavier, roads into
    /// wilderness dashed, water crossings dotted, sea lanes as faint dots, markets marked.
    /// Drawn by <see cref="Emit.FlatmapInk"/>.
    /// </summary>
    [Category("06 Map Objects")]
    [DisplayName("Paper Map Roads")]
    [Description("Draw the road and sea-lane network onto the flat (paper) map, with trunk roads in a heavier line.")]
    public bool FlatmapRoads { get; set; } = true;

    /// <summary>
    /// A compass rose in the open ocean with rhumb lines ruled from it across the sea, hatching
    /// over unsettled wilderness, and a graduated border around the sheet, on the parchment flat
    /// map. Drawn by <see cref="Emit.FlatmapInk"/>.
    /// </summary>
    [Category("06 Map Objects")]
    [DisplayName("Paper Map Flourishes")]
    [Description("Decorate the flat (paper) map: a compass rose with rhumb lines across the open sea, hatching over the wilderness, and a graduated border.")]
    public bool FlatmapFlourishes { get; set; } = true;

    /// <summary>
    /// Hachures on the parchment flat map: short sepia strokes running down the slope of every
    /// impassable mountain, heavier where it is steep, and a ")(" mark on each mountain pass.
    /// Drawn by <see cref="Emit.FlatmapInk"/>.
    /// </summary>
    [Category("06 Map Objects")]
    [DisplayName("Paper Map Mountain Hachures")]
    [Description("Shade the impassable mountains on the flat (paper) map with engraver's slope strokes, and mark each mountain pass with a )( sign.")]
    public bool FlatmapHachures { get; set; } = true;

    /// <summary>
    /// The coastline and the waterlines off it inked on the parchment flat map by
    /// <see cref="Emit.FlatmapInk"/>, at line widths that scale with the map, instead of the fixed
    /// one-pixel coast and faint pixel rings the base parchment pass paints.
    /// </summary>
    [Category("06 Map Objects")]
    [DisplayName("Paper Map Coast Ink")]
    [Description("Ink the coastline on the flat (paper) map with hand-drawn waterlines running parallel to the shore, widening and fading out to sea.")]
    public bool FlatmapCoastInk { get; set; } = true;

    /// <summary>
    /// Feathers the flat map's edges into the table through the blue channel of
    /// <c>gfx/map/surround_map/surround_mask.dds</c>, which vanilla's terrain shader reads as the
    /// flat map's transparency. Only open sea near the edge fades — land and a buffer round every
    /// coast stay opaque — and the flat map's ink frame moves in to stand just inside the fade.
    /// Written by <see cref="Emit.MapGraphicsWriter"/>. Off while it is a prototype.
    /// </summary>
    [Category("06 Map Objects")]
    [DisplayName("Paper Map Edge Feather")]
    [Description("Softly fade the flat (paper) map's outer edge into the table, over open sea only. Land and coasts are never faded.")]
    public bool FlatmapFeather { get; set; } = false;

    /// <summary>
    /// Ships vanilla's <c>gfx/FX/surroundmap.shader</c> with a depth test on its 3D layers, so the
    /// dark clouds and shadow beyond the map's edge no longer cut off terrain that runs up to it.
    /// Patched from the installed game at each generation by <see cref="Emit.MapGraphicsWriter"/>;
    /// off leaves vanilla's shader in place. The switch exists for a CK3 update that breaks the
    /// patch in a way its anchors cannot detect.
    /// </summary>
    [Category("06 Map Objects")]
    [DisplayName("Map Edge Depth Test")]
    [Description("Stop the dark clouds beyond the map's edge from cutting off hills and mountains that run right up to it. Patches one of the game's shaders from your installed copy of CK3 each time a map is generated. Turn it off if a game update ever makes the map's edge look wrong.")]
    public bool SurroundDepthTest { get; set; } = true;

    /// <summary>
    /// Ships vanilla's <c>gfx/FX/province_effects.fxh</c> with its effect mask averaged over a
    /// jittered disc instead of one bilinear texel, so a situation's drought, summer grass or snow
    /// (the Great Steppe's seasons) fades across a province border rather than stopping on it.
    /// Patched from the installed game at each generation by <see cref="Emit.MapGraphicsWriter"/>;
    /// off leaves vanilla's shader.
    /// </summary>
    [Category("06 Map Objects")]
    [DisplayName("Soft Province Effects")]
    [Description("Fade the ground effects situations paint on whole provinces (the steppe's droughts, snows and green seasons) across province borders instead of ending them in a hard line. Patches one of the game's shaders from your installed copy of CK3 each time a map is generated. Turn it off if a game update ever makes those effects look wrong.")]
    public bool SoftProvinceEffects { get; set; } = true;


    // =========================================================================
    // 11 Height scale
    // =========================================================================

    /// <summary>
    /// How an imported heightmap is rescaled onto CK3's height scale before anything reads it.
    ///
    /// The default is <see cref="Config.HeightmapNormalization.Off"/>, and what each mode costs on a
    /// map that does not need it has been measured rather than assumed:
    ///
    ///   * <b>Stretch is not free.</b> Run over vanilla's own heightmap — already on CK3's scale,
    ///     bottom anchor correctly detected as a no-op — it still applies a 1.113x stretch, because
    ///     <see cref="LandTopPercentile"/> anchors at 173.6/255 where vanilla's top 0.01% runs on to
    ///     191. Land moves by a mean of 3.00/255 and 8,814 px clip.
    ///   * <b>Shift is free.</b> On vanilla it detects the floor at 19.00/255, which is vanilla's own
    ///     lowest land pixel, shifts by 0.00 and clips nothing; the emitted distribution is
    ///     36 / 57 / 86 / 143 / 191 in and out. Same result on this program's own output.
    ///
    /// So Shift is a defensible default where Stretch is not — it is the identity on a correct map
    /// and still removes the shore cliff from a plateau. It is left off only because Off is the one
    /// setting that cannot surprise anybody, and this program's own heightmaps never need it.
    ///
    /// Two different faults want two different modes; see
    /// <see cref="Config.HeightmapNormalization"/> and <see cref="MapGen.HeightmapNormalizer"/>.
    /// </summary>
    [Category("7 Height scale")]
    [Description("Rescale an imported heightmap onto CK3's height scale.")]
    public HeightmapNormalization Normalization { get; set; } = HeightmapNormalization.Shift;

    /// <summary>
    /// Where the source heightmap puts its own sea level, on the 0-255 scale.
    ///
    /// Advisory rather than load-bearing, since the land floor became a detected value: this now
    /// decides only which pixels count as water, not what the land scale is anchored on. Measured
    /// on the playtest map that prompted the change, the detected floor came out at 128/255 under
    /// every source sea level from 0 to 40, which is the whole reason the anchor stopped being a
    /// percentile — see <see cref="MapGen.HeightmapNormalizer"/>.
    ///
    /// It still has to be roughly right for a source that puts its coastline somewhere unusual.
    /// 19 is CK3's own. Azgaar's is 20 on its 0-100 scale, which is 51 here.
    /// </summary>
    [AdvancedSetting]
    [Category("7 Height scale")]
    [Description("Where the source heightmap puts sea level, on the 0-255 scale. CK3's own is 19; This decides only which pixels count as water — the land scale is anchored on a detected floor, so this no longer has to be exactly right for the land side to come out correct.")]
    public double SourceSeaLevel { get; set; } = 19;

    /// <summary>
    /// How far the land density may fall below its own peak before the bottom anchor stops walking
    /// down, as a fraction of the busiest land level.
    ///
    /// This is what replaced taking the bottom anchor as a true minimum, and it is the single
    /// change that turns normalisation from a near-no-op into a vanilla-matching result. The
    /// failure it exists for: on the playtest map the lowest land pixel was 20.00/255, set by 585
    /// pixels out of 2.25 million, so the affine map was anchored on 0.026% of the land and the
    /// continent — which actually starts at 128/255 — was left a plateau with a wall at every
    /// shore. <see cref="LandTopPercentile"/> exists precisely because one stray sample must not
    /// set the scale, and the bottom anchor had no equivalent protection.
    ///
    /// A percentile is the obvious fix and the wrong one. The pixels in the fringe just above the
    /// water plane shuttle between the land and water populations depending on where
    /// <see cref="SourceSeaLevel"/> is put, and near the sparse bottom of a distribution that moves
    /// a percentile a long way: on that map bottom-p1 swung from 33 to 94 over source sea levels 0
    /// to 40. Walking down from the *mode* while density holds is stable — floor 128/255 in all
    /// eighteen combinations of six sea levels and thresholds 0.05, 0.10 and 0.20 — which is the
    /// right way to handle a constant nobody can pin down.
    ///
    /// Raise it towards 1 to cut the fringe harder, lower it towards 0 to keep more of it. 0
    /// disables detection and takes the true minimum, which is the old behaviour.
    /// </summary>
    [AdvancedSetting]
    [Category("7 Height scale")]
    [Description("How far land density may fall below its own peak before the bottom anchor stops walking down. This is what stops a few hundred stray coastal pixels from anchoring the whole land scale and leaving the map a plateau with a cliff at every shore. Measured stable from 0.05 to 0.20; 0 disables detection and takes the true minimum instead.")]
    public double LandFloorDensity { get; set; } = 0.10;

    /// <summary>
    /// Diagnostic. Ships vanilla's zoom ladder, panning bounds and flat-map handoff instead of this
    /// map's, to take every camera override out of the picture while looking at a rendering
    /// artefact.
    ///
    /// The tilt limits stay widened — they are what makes the artefact visible in the first place —
    /// and START_LOOK_AT stays on this map's centre, because vanilla's { 5000 0 2300 } is off the
    /// edge of any smaller map and the game would open looking at nothing, which tests nothing.
    ///
    /// FLAT_MAP_ZOOM_STEP and the map-table layer fades move together, as always: this makes
    /// <see cref="Emit.CompatibilityWriter.ScaleZoomStep"/> the identity so both land on vanilla's
    /// values rather than desyncing.
    ///
    /// Worth knowing before reading anything into the result: nothing in common/defines reaches
    /// CK3's terrain LOD. The vertex shader takes NodeScale, LodDirection, LodLerpFactor,
    /// QuadtreeLeafNodeScale and NormQuadtreeToWorld, all engine-computed, and the only
    /// mod-controlled inputs to the drawn surface are the packed heightmap and its indirection
    /// texture. What this *can* change is how much 3D terrain you are shown before the paper map
    /// takes over, which is a visibility question rather than an LOD one.
    /// </summary>
    [AdvancedSetting]
    [Category("7 Height scale")]
    [Description("Diagnostic: ship vanilla's zoom ladder, panning bounds and flat-map handoff instead of this map's, to rule the camera overrides out while investigating a rendering artefact. Tilt limits stay widened and the start view stays on this map's centre, since vanilla's is off the edge of a smaller map. Not for release builds — vanilla's panning bounds and surround geometry are authored for a 9216-wide map.")]
    public bool VanillaCamera { get; set; }

    /// <summary>
    /// Extra zoom steps of 3D terrain before the paper map takes over — how much further the
    /// camera gets from the ground while still looking at real terrain than vanilla's own handoff
    /// index would allow.
    ///
    /// Read by <see cref="Emit.CompatibilityWriter.ScaleZoomStep"/>, which is the only place it may
    /// be applied, because the handoff is a pair and not a value. The map-table layer fades in
    /// <see cref="Emit.MapTableWriter"/> come through that same function, so biasing there moves
    /// the tabletop's appearance and the map going flat by the same number of steps and keeps them
    /// on one frame. Bias FLAT_MAP_ZOOM_STEP alone and you open a window of zoom where the table is
    /// drawn underneath 3D terrain.
    ///
    /// The ladder is roughly geometric at about 15% a step. Measured on a 9216-wide map, where the
    /// scaled ladder puts vanilla's step 21 at 672 world units against a 4607-unit world:
    ///
    ///     bias  flat step  height  share of world width  vs vanilla's 13.4%
    ///        2         23     804                 17.5%               1.3x
    ///        5         26    1080                 23.4%               1.7x
    ///        8         29    1525                 33.1%               2.5x
    ///
    /// 5 by default: a small map is mostly the part you want to look at, and vanilla's framing was
    /// authored for a map twice as wide.
    ///
    /// Nothing breaks at the top of the range. Vanilla's layers.txt fades on steps 0, 6, 9, 20 and
    /// 21 — its 80s are the format's way of spelling "never" and are returned untouched — and both
    /// halves of the handoff pair go through one function with one input, so they clamp together
    /// and stay in sync even past the end of the ladder. What a large bias does cost is render
    /// time and honesty at distance: the terrain renderer keeps working further out, in the regime
    /// where the engine's own runtime LOD is coarsest and where distant props float. That half is
    /// unreachable from a mod — see <see cref="ScaleReliefWithMapSize"/> and
    /// <see cref="HeightmapSagBudget"/> for the halves that are not. Walk this back first if
    /// far terrain looks mushy.
    /// </summary>
    [AdvancedSetting]
    [Category("7 Height scale")]
    [Description("Extra zoom steps of 3D terrain before the map goes flat to the paper map, past vanilla's own handoff. The zoom ladder is about 15% a step, so 5 is roughly 1.7x vanilla's share of the world visible as terrain — worth having because vanilla's framing was authored for a map twice as wide. The map-table fades move with it automatically. Lower it if terrain at far zoom looks mushy or costs frames; 0 is vanilla's own handoff.")]
    public int FlatMapHandoffBias { get; set; } = 5;

    /// <summary>
    /// Extra zoom steps before the close-up map gives way to the political one: trees, city
    /// scatter, animals and weather effects fade out, and the realm colours start filling in.
    ///
    /// Vanilla does all of that together at step 9 — the tree layers' <c>fade_out=9</c> and
    /// <c>REALM_COLOR_MAP_START_ZOOM_STEP = 9</c> — and this moves the pair together too, in
    /// <see cref="Emit.CompatibilityWriter.DetailFadeStep"/>. Counted from the camera height vanilla
    /// does it at, not from the step index, so it means the same on every map size: a tree is the
    /// same tree on a small map, and the scaled zoom ladder would otherwise fade it at
    /// <see cref="MapScale"/> times vanilla's height.
    ///
    /// Held under the flat-map handoff, so it cannot draw trees onto the paper map. On a small map
    /// the realm colour then finishes filling on the handoff frame rather than six steps before it.
    ///
    /// Grass is deliberately left at vanilla's height. Its meshes are flat ground patches, and
    /// <see cref="Emit.TreeWriter"/> records that seen from any further off they read as debris
    /// strewn over the map.
    ///
    /// The cost is instances drawn at far zoom. The ladder climbs about 12% a step between 9 and
    /// 14, so 5 is 1.8x vanilla's camera height and about 3x the ground in view at the cutoff —
    /// still a small corner of the map, but walk this back first if far zoom costs frames.
    /// </summary>
    [AdvancedSetting]
    [Category("7 Height scale")]
    [Description("Extra zoom steps before trees, city scatter, animals and weather effects fade out and the realm colours start filling in, past the camera height vanilla does it at. 5 matches the default Flat Map Handoff Bias, so vanilla's whole zoom-out sequence is stretched out by the same amount. Held under the flat-map handoff. Grass stays at vanilla's distance. 0 is vanilla's own distance.")]
    public int DetailFadeBias { get; set; } = 5;

    /// <summary>
    /// Whether the flat-map handoff is pulled in on maps below vanilla's size by the terrain
    /// detail those maps cannot supply.
    ///
    /// <see cref="FlatMapHandoffBias"/> and CompatibilityWriter's scaled zoom ladder between them
    /// make a zoom-step *index* frame the same share of the map at any size, which is right for
    /// every visibility index that rides on the ladder. It is not right for the handoff, because
    /// what the handoff decides is how much 3D terrain the player is shown, and terrain has a
    /// detail budget that framing knows nothing about.
    ///
    /// The budget is countable. Heightmap pixels per world unit is invariant at 2 on every map size
    /// — the heightmap is twice the province map and <c>WORLD_EXTENTS_X</c> is the province width
    /// less one — so the finest node the engine will render is 32 world units everywhere, which is
    /// the <c>NodeScale 256</c> measurement recorded in <see cref="Emit.CompatibilityWriter"/>. The
    /// world, though, shrinks with <see cref="MapScale"/>. So the octaves of LOD between "one vertex
    /// per texel" and "the whole map in one node" are <c>log2(world width / 32)</c>, and every
    /// halving of the map costs one:
    ///
    ///     heightmap   world   MapScale   octaves
    ///         18432    9215      1.000      8.17
    ///         12288    6143      0.667      7.58
    ///          8192    4095      0.444      7.00
    ///          6144    3071      0.333      6.58
    ///          4096    2047      0.222      6.00
    ///
    /// A feature survives pulling back until it fits inside one node, so it dies
    /// <c>log2(1/MapScale)</c> octaves earlier on a smaller map — which is the terrain visibly
    /// flattening on zoom-out that this setting exists for. Nothing can put those octaves back
    /// except more pixels. What can be done is to stop handing the player terrain that has run out
    /// of them, which is what pulling the handoff in does.
    ///
    /// Applied in <see cref="Emit.CompatibilityWriter.HandoffOffset"/> and nowhere else, for the
    /// same reason the bias is: the flat map and the map-table layer fades are a pair, and both
    /// come through <see cref="Emit.CompatibilityWriter.ScaleZoomStep"/>. Correcting one alone
    /// opens a window of zoom where the tabletop is drawn under 3D terrain.
    ///
    /// Identity at and above vanilla's size. Extra pixels beyond vanilla's are deliberately not
    /// spent pushing terrain further out — <see cref="FlatMapHandoffBias"/> is the knob for that,
    /// and a correction that ran both ways would fight it.
    ///
    /// The trade is real, it is the whole reason this is a setting, and it is why the setting is
    /// **off**: this shows *less* of a small map as terrain, not more. At the default bias the
    /// handoff lands at 23.4% of the world's width at vanilla size, 13.4% at 8192 — vanilla's own
    /// framing — and 8.5% at 4096. It stops at CompatibilityWriter's MinFlatMapZoomStep, so a very
    /// small <see cref="Width"/> cannot take 3D terrain away entirely.
    ///
    /// So do not reach for this if the complaint is "terrain stops too soon when I zoom out" — it
    /// makes that strictly worse, and it was briefly on by default in error for exactly that
    /// misreading. <see cref="FlatMapHandoffBias"/> is the setting for that complaint, and it is
    /// size-independent: the ladder is scaled, so the *index* that means "terrain all the way to
    /// maximum zoom" is 34 on every map, which is a bias of 13.
    ///
    /// What this is for is the opposite preference — a player who would rather be handed the paper
    /// map than a smeared one. The detail shortfall it corrects for is real either way; which side
    /// of it you want is taste, and neither side is a fix. Only <see cref="Width"/> is.
    ///
    /// Note that <see cref="ScaleReliefWithMapSize"/> pulls against this. Left off, small-map slopes
    /// are 1/<see cref="MapScale"/> steeper in world units, and that over-steepening is masking part
    /// of the fade; turning it on to fix floating props flattens the masking too. The two artefacts
    /// share one knob and it does not have a setting that fixes both.
    /// </summary>
    [Category("7 Height scale")]
    [AdvancedSetting]
    [Description("Hands off to the paper map EARLIER on maps smaller than vanilla's, by the terrain detail those maps cannot supply — so it shows less of the map as terrain, not more. Off by default. If terrain stops too soon when you zoom out, this is the wrong setting: raise Flat Map Handoff Bias instead (13 keeps terrain to maximum zoom on any map size). Turn this on only if you would rather see the paper map than smeared terrain at far zoom.")]
    public bool ScaleHandoffToDetail { get; set; }

    /// <summary>
    /// Whether land relief shrinks with map size, so slopes come out the same in *world units* at
    /// any resolution.
    ///
    /// CK3 takes the world's width from the province map — a 4608-wide map is 4607 world units —
    /// but its height is `WORLD_EXTENTS_Y = 50` on every map, and that has to stay constant
    /// (`WATERLEVEL = 3` is pinned to it at 3/50 = 0.06, which is where the sea is *drawn* —
    /// <see cref="Emit.MapDataWriter.WaterPlane16"/>, and not the 19/255 where land begins in the
    /// file; see <see cref="Emit.CompatibilityWriter"/>). Because a smaller map resamples the
    /// *same world* into fewer pixels — the model the whole config is built on, see
    /// <see cref="Scaled"/> — leaving the height range alone steepens every slope by exactly
    /// 1/<see cref="MapScale"/>. A half-width map has twice vanilla's gradient.
    ///
    /// That is the floating-props cause that survives a perfect heightmap bake. Measured on a
    /// 4608-wide generated map against vanilla, at the coarse grids the engine's runtime terrain
    /// LOD uses at distance, land sag ran 1.8x vanilla's at p90 — and compressing land by
    /// MapScale brought it to 1.1x, right onto vanilla's own figure. See
    /// <see cref="HeightmapSagBudget"/> for the other half, which is ours to fix; this half is
    /// terrain the engine cannot follow.
    ///
    /// Compression is toward the waterline, not toward zero, so sea level and every water depth
    /// are untouched and the set of pixels above water is bit-identical — coastline reconciliation
    /// and terrain classification see exactly what they saw before. Percentile-based settings
    /// (<see cref="SteepLineShare"/>, <see cref="CliffSlopeShare"/>) are rank-invariant under it;
    /// absolute slope thresholds like <see cref="MinPhysicalSlope"/> become *more* correct, since
    /// they are authored against vanilla's gradients.
    ///
    /// Identity at vanilla map size. Above it relief is amplified by the same rule, which can clip
    /// at the top of the range — the clipped count is logged.
    ///
    /// This restores the terrain half of a two-part fix: commit 194178e stopped scaling
    /// WORLD_EXTENTS_Y and deferred the correction to MapConfig.SlopeScaleFor, and 38b5fe8 deleted
    /// that method along with terrain generation, leaving the world-height side reverted and
    /// nothing doing the terrain side.
    /// </summary>
    [Category("7 Height scale")]
    [Description("Shrinks land relief in proportion to map size so slopes match vanilla's in world units at any resolution. TURN ON IF TREES AND OTHER OBJECTS ARE FOUND TO BE FLOATING.")]
    public bool ScaleReliefWithMapSize { get; set; } = false;

    /// <summary>
    /// The scale, in heightmap pixels, at which <see cref="MapGen.HeightmapNormalizer.CompressRelief"/>
    /// divides terrain into "mountain" and "detail". Broader than this keeps its full height; finer
    /// than this is what <see cref="ReliefScale"/> compresses.
    ///
    /// This is what stops relief scaling and normalisation fighting over the same byte. Uniform
    /// compression cannot leave both intact on a small map: to end on <see cref="LandTop"/> 191
    /// after a 0.5 multiply, normalisation would have to hand over a pre-compression top of
    /// 20 + 171/0.5 = 362 on a scale that stops at 255. So one of vanilla's hypsometry and
    /// vanilla's gradient had to go, and it was always hypsometry, because compression ran last.
    ///
    /// The split dissolves that, because the two passes turn out not to want the same thing after
    /// all. Hypsometry is a per-pixel *height* distribution; LOD sag is *curvature* — the drawn
    /// mesh interpolates linearly between vertices, so a long smooth slope costs nothing however
    /// steep it is, and a ridge crest between two vertices is not lowered, it is absent (see
    /// <see cref="HeightmapSagBudget"/>, which fixes the baked half of the same effect). Total
    /// relief is simply the wrong quantity to have been scaling. Compressing only the residual
    /// above this scale aims at the curvature and leaves the height distribution alone.
    ///
    /// 32 px is where the two box passes that build the mean reach ±64 px, and at the invariant
    /// 2 heightmap px per world unit that is ±32 world units — one of the 32-world-unit nodes
    /// <see cref="Emit.CompatibilityWriter"/> records as the finest the engine actually renders,
    /// either side of every pixel. Detail the coarsest node cannot resolve is exactly the part
    /// LOD cannot follow.
    ///
    /// It is also the measured knee. Swept on a 9216x4608 Azgaar import at
    /// <see cref="ReliefScale"/> 0.5, land percentiles against per-tile reconstruction sag in
    /// world units (mean / p99 over land tiles, decimation 4):
    ///
    ///     radius   p50 p75 p90 p99 max   sag mean  sag p99   atlas
    ///     off       29  54  86 128 168      1.738   10.710   11.0M
    ///     16        29  54  85 124 157      1.172    7.947   10.4M
    ///     32        28  54  84 120 153      1.125    6.762   10.6M
    ///     64        29  53  80 115 150      1.119    6.332   11.7M
    ///     128       29  51  74 106 145      1.151    5.853   12.4M
    ///     uniform   24  37  53  74  94      1.000    5.600    9.4M
    ///
    /// Read the sag column first: it is flat from 16 px up, and sag p90 is 2.321 at *every* radius
    /// including uniform. The whole benefit arrives at the smallest split there is, so the radius
    /// is not buying sag — past 32 it only spends hypsometry, a third of the way back to uniform by
    /// 128 for a p99 that moves 0.9 world units. The atlas turns around at the same place, because
    /// a wider mean raises more and deeper hollows and the sag budget then pays for them in tiles.
    ///
    /// Note what the off row means for the pass as a whole: the split gets 88% of uniform's mean
    /// sag reduction and all of its p90, for 2 points of p90 height instead of 33.
    ///
    /// Symmetric, like the sag budget and for the same reason: decimation loses height across a
    /// ridge and gains it across a valley, so a valley floor well below its surroundings is raised
    /// toward them by the same rule that lowers a crest. Land at or below the waterline is still
    /// returned bit for bit, so the land/water split every downstream consumer keys on is
    /// untouched.
    ///
    /// 0 restores the old behaviour — uniform compression of all relief toward the waterline — and
    /// is the way back if the split ever turns out to buy less in game than it does on paper.
    /// </summary>
    [AdvancedSetting]
    [Category("7 Height scale")]
    [Description("The scale, in heightmap pixels, that separates mountains from detail when relief is scaled with map size. Terrain broader than this keeps its full height; only finer detail is compressed. That is the part the engine's terrain LOD cannot follow, because sag comes from curvature rather than from total relief — a long smooth slope costs nothing however steep it is. Splitting this way is what lets a half-size map keep vanilla's height distribution and vanilla's LOD behaviour at once, which uniform compression cannot do at any setting. 32 reaches one LOD node either side of each pixel, and is the measured knee: sag stops improving above it and only height is lost. 0 goes back to compressing all relief uniformly toward the waterline.")]
    public int ReliefDetailRadius { get; set; } = 32;

    /// <summary>
    /// What the highest land pixel becomes after normalisation, on the 0-255 scale.
    ///
    /// 191 is vanilla's own highest land pixel. Vanilla does not use the top of the byte range —
    /// its land sits at p50 36 and runs out well short of 255 — so stretching an imported map onto
    /// all of it produces terrain markedly more dramatic than anything in the base game. Raise it
    /// towards 255 for a deliberately alpine map; that is the knob that decides how flat the
    /// result reads.
    /// </summary>
    [Category("7 Height scale")]
    [Description("What the highest land pixel becomes, on the 0-255 scale. 191 is vanilla's own highest; vanilla never uses the top of the range. Raise towards 255 for a more dramatic map — this is the knob that decides how flat the result reads.")]
    public double LandTop { get; set; } = 255;

    /// <summary>
    /// How far the terrain CK3 draws may depart — in *either* direction — from the heightmap it
    /// snaps props and borders to, in world units, before the packer spends more atlas on that
    /// tile. 0 restores the old behaviour: copy vanilla's level histogram and accept whatever
    /// error follows.
    ///
    /// Both directions, because decimation is linear interpolation between the samples it keeps,
    /// so it loses height across a ridge and gains it across a valley. Terrain below the placement
    /// surface floats a tree; terrain above it comes up through a province border, which is a
    /// ribbon laid on the heightmap and lifted by one engine constant. Budgeting only the
    /// shortfall left the other side free, and on a shipped map that side was the larger one:
    /// worst overshoot 4.11 world units against a worst shortfall of 0.96 under the same 0.50
    /// budget. Two-sided costs about 7% more atlas and 3% more terrain vertices.
    ///
    /// This is the floating-trees knob, and the floating is not a placement bug. Prop instances
    /// are written with <c>y=0</c> — vanilla's too — and the engine snaps them to the ground at
    /// load, from the full-resolution heightmap.png. The terrain *mesh* comes from the decimated
    /// packed_heightmap, where a level-4 tile keeps one sample in sixteen and
    /// <see cref="Emit.HeightmapPacker"/>'s Extract point-samples rather than averages, so a ridge
    /// crest between two samples is not lowered, it is absent. The prop stands at the height the
    /// heightmap promised and the drawn ground sits below it.
    ///
    /// The old rank-against-vanilla assignment is what made that big. Vanilla's shipped level
    /// histogram is the *outcome* of an error budget applied to gentle European terrain, not a rule
    /// worth copying: reproducing the histogram on steeper ground reproduces vanilla's tile counts
    /// and misses vanilla's tile quality by an order of magnitude. Measured on a 9216x4608
    /// generated map against vanilla, land tiles only, the share sagging over half a world unit was
    /// 59.9% here against vanilla's 4.4%.
    ///
    /// 0.5 buys better-than-vanilla fidelity for less than vanilla spends. On that same map it took
    /// tiles over 0.5u from 59.9% to none, in an atlas smaller than vanilla's 3185x4061 and at
    /// 9.5M terrain vertices against the 12.65M vanilla itself ships. Lower it for a sharper mesh
    /// on deliberately alpine maps — 0.25 roughly doubles the atlas over 0.5 — and watch the packed
    /// size in the log, since the hard ceiling is 16384 px a side.
    ///
    /// Only the baked half. The engine also morphs terrain LOD with camera distance, which no mod
    /// can reach, so distant props still float a little and settle as you zoom in.
    /// </summary>
    [AdvancedSetting]
    [Category("7 Height scale")]
    [Description("How far the drawn terrain may depart, in either direction, from the heightmap props and borders are snapped to, in world units. This is the floating-props knob: terrain below that surface floats a tree, terrain above it comes up through a province border. It is also the error that survives zooming in, since the engine's own terrain LOD blending fades out up close and leaves this behind. Lower is sharper and costs atlas space; 0.5 beats vanilla's own fidelity for less than vanilla spends. 0 restores the old copy-vanilla's-histogram behaviour.")]
    public double HeightmapSagBudget { get; set; } = 0.5;

    /// <summary>
    /// The packer's tile step in heightmap pixels, or 0 to take
    /// <see cref="Emit.HeightmapPacker.TileStepFor"/>'s answer for this map's width.
    ///
    /// Only 32, 64 and 128 are legal — CK3 reads <c>tile_size</c> 33, 65 and 129 and nothing else,
    /// and its own map editor offers exactly those three. An override is here because the three
    /// are worth measuring against each other on a real map: the packer reports its atlas size and
    /// worst error on every build, so the comparison is a rerun rather than an argument.
    /// </summary>
    [AdvancedSetting]
    [Category("7 Height scale")]
    [Description("Packer tile step in heightmap pixels: 32, 64, 128, or 0 to choose by map width (64 above 9216 wide, 32 at or below). Smaller tiles let the level assignment follow coastlines instead of committing whole blocks to one level, at the cost of a larger shared edge — 6.4% overhead at 32 against 3.2% at 64. CK3 only accepts these three.")]
    public int HeightmapTileStep { get; set; }

    /// <summary>
    /// Whether the packer refines tiles so no two neighbours differ by more than one level.
    ///
    /// Off, because vanilla does not do it and the cost is large. Measured on vanilla's own
    /// indirection texture: 7,880 adjacent tile pairs differ by more than one level, which a
    /// balance pass would drive to zero by construction. So this is not a rule CK3 enforces and
    /// not one it needs — the shared edge sample that <c>tile_size = step + 1</c> exists for is
    /// what keeps neighbouring tiles meeting at all.
    ///
    /// What it costs when on, measured on a 9216x4608 map against vanilla at the same tile step:
    /// a level-0 coastal tile forces its neighbours to 1, 2, 3, 4 outward, so fine detail cascades
    /// up to four tiles — 128 world units at a step of 64 — into open water. 64.3% of pure-ocean
    /// tiles were held above the coarsest level, reaching a 95th percentile of 96 world units
    /// offshore, where vanilla holds 93.8% of its ocean at the coarsest level and 6.2% above it.
    /// That is atlas spent on flat water instead of on the land the sag budget is for.
    ///
    /// It also cannot be trusted to preserve the budget: refining a tile is not automatically safe
    /// because error is not monotonic in level, which is the whole reason
    /// <c>AssignLevels</c> takes the longest passing *prefix* rather than the coarsest passing
    /// level. With this off, that prefix rule is merely conservative rather than load-bearing.
    ///
    /// Turn on only to test a seam: adjacent tiles at different decimation share edge samples, but
    /// the coarse side draws a straight line between the ones it kept while the fine side follows
    /// the heightmap, and that difference is a crack. Vanilla ships 7,880 such adjacencies, so if
    /// it is visible here and not there, the cause is something else.
    /// </summary>
    [AdvancedSetting]
    [Category("7 Height scale")]
    [Description("Refine tiles so no two neighbours differ by more than one detail level. Off, because vanilla does not do it — its own indirection texture has 7,880 adjacencies a balance pass would forbid — and because it is expensive: it cascades fine detail up to four tiles into open ocean, holding 64% of pure-water tiles above the coarsest level against vanilla's 6%. Turn on only to test whether a visible seam is a tile-boundary crack.")]
    public bool BalanceNeighbourLods { get; set; }

    /// <summary>
    /// Which percentile of land the top anchor is taken at, rather than the maximum.
    ///
    /// Anchoring on the highest pixel lets a single stray sample set the scale for the whole map,
    /// and exported heightmaps are full of stray samples. Everything above the anchor is clipped
    /// flat onto <see cref="LandTop"/>, so the setting trades the tips of the tallest peaks for a
    /// bigger stretch under them. 100 anchors on the true maximum and clips nothing.
    ///
    /// The default is deliberately close to it. Land is counted in *pixels*, and a percentile is a
    /// share of them, so on a map with fifteen million land pixels a seemingly cautious 99.5 clips
    /// seventy-five thousand — an entire mountain range flattened onto one value, which reads in
    /// game as a mesa. Measured on an 8192x4096 Azgaar export: p99.5 anchored at 49/255 and clipped
    /// 75,690 px, p99.9 at 64 and 15,142 px, p99.99 at 76 and 1,514 px, p100 at 84 and none. Only
    /// the last two are outlier rejection; the first two are terrain. Turn it down deliberately,
    /// watching the clipped count in the log, rather than as a matter of course.
    /// </summary>
    [AdvancedSetting]
    [Category("7 Height scale")]
    [Description("Which percentile of land the top anchor is taken at, instead of the maximum. Everything above it is clipped flat, so this trades the tips of the tallest peaks for a bigger stretch under them. Land is counted in pixels: on a large map even 99.5 can flatten a whole mountain range, so watch the clipped count in the log. 100 anchors on the true maximum.")]
    public double LandTopPercentile { get; set; } = 99.99;

    /// <summary>
    /// Share of land put above the mountain line. Vanilla's own heightmap has 3.3% of its land in
    /// the 121-170 band, and that is the number this reproduces.
    /// </summary>

    [AdvancedSetting]
    [Category("7 Height scale")]
    public int PeakElevation { get; set; } = 520;

    [AdvancedSetting]
    [Category("7 Height scale")]
    public int SeaFloorElevation { get; set; } = -250;

    [Category("7 Height scale")]
    [Description("Inward coastal cliff smoothing strength (0.0 = sheer vertical cliffs, 1.0 = full smooth ramp). Softens high land meeting the ocean from the shore inward; water remains strictly untouched.")]
    public double CoastalCliffSmoothing { get; set; } = 0.65;

    [AdvancedSetting]
    [Category("7 Height scale")]
    [Description("How many pixels inland from the shoreline the coastal cliff smoothing reaches (1 to 16 pixels).")]
    public int CoastalCliffReach { get; set; } = 5;


    // =========================================================================
    // 12 Climate
    // =========================================================================

    /// <summary>
    /// Where the equator line sits, as a fraction of map height. Everything about climate is
    /// measured as distance from it, so this slides every band up or down the map together.
    ///
    /// ck2rpg's 0.9 is deliberately off-centre: it puts the equator near the bottom edge, so a map
    /// is mostly one hemisphere and the cold band only appears at the top. 0.5 centres it and gives
    /// a symmetric world with tropics through the middle and cold at both edges.
    /// </summary>
    [Category("8 Climate")]
    [Description("Where the equator sits, as a fraction of map height. Slides every climate band up or down together. 0.9 (ck2rpg's) puts it near the bottom edge so the map is mostly one hemisphere; 0.5 centres it and gives cold at both edges.")]
    public double EquatorPosition { get; set; } = 0.9;

    /// <summary>
    /// How many degrees of latitude the map covers, top edge to bottom edge. With
    /// <see cref="EquatorPosition"/> this is the entire mapping from pixels to latitude, and
    /// therefore the only control over how many climate zones the map crosses.
    /// </summary>
    [AdvancedSetting]
    [Category("8 Climate")]
    [Description("How many degrees of latitude the map covers from top edge to bottom edge. With the equator position this is the whole mapping from pixels to latitude, so it decides how many climate zones the map crosses. 80 degrees with the equator near the bottom is roughly the sweep of vanilla's map.")]
    public double MapLatitudeSpan { get; set; } = 80;

    /// <summary>Annual mean temperature at sea level on the equator. Earth's is about 26.</summary>
    [AdvancedSetting]
    [Category("8 Climate")]
    [Description("Annual mean temperature at sea level on the equator, in Celsius. Earth's is about 26. Raising this and the pole figure together is how to make a hotter world.")]
    public double EquatorTemperatureC { get; set; } = 26;

    /// <summary>Annual mean temperature at sea level at the pole. Earth's northern one is about -20.</summary>
    [AdvancedSetting]
    [Category("8 Climate")]
    [Description("Annual mean temperature at sea level at the pole, in Celsius. Earth's northern one is about -20. Bringing it closer to the equator figure gives a flatter, more uniform world with far less tundra and taiga.")]
    public double PoleTemperatureC { get; set; } = -20;

    /// <summary>
    /// How far the warmest and coldest months sit either side of the annual mean at high latitude.
    /// This decides where Koppen's C/D boundary falls, and therefore where oceanic forest gives way
    /// to continental forest and taiga.
    /// </summary>
    [AdvancedSetting]
    [Category("8 Climate")]
    [Description("How far apart the warmest and coldest months are at high latitude, in Celsius. Decides where temperate gives way to continental and taiga, because Koppen splits those on the coldest month rather than on the average.")]
    public double SeasonalRangeC { get; set; } = 44;

    /// <summary>
    /// How far inland the sea keeps moderating the seasons, in vanilla province pixels. Inside it a
    /// coast has mild winters; past it a continental interior swings freely.
    /// </summary>
    [AdvancedSetting]
    [Category("8 Climate")]
    [Description("How far inland the sea goes on moderating the seasons, in vanilla province pixels. Inside it a coast has mild winters and cool summers; past it an interior swings freely. This is what separates an oceanic climate from a continental one at the same latitude.")]
    public double ContinentalityPixels { get; set; } = 900;

    /// <summary>
    /// Warmth and cold that latitude does not explain, in degrees. On Earth this is what ocean
    /// currents do - Norway and Labrador share a latitude and not a climate. Without it every
    /// isotherm on the map is a parallel.
    /// </summary>
    [AdvancedSetting]
    [Category("8 Climate")]
    [Description("Warmth and cold that latitude cannot explain, in Celsius - what ocean currents do on Earth, where Norway and Labrador share a latitude and not a climate. 0 makes every isotherm a parallel, which is half of what makes a climate map look ruled.")]
    public double TemperatureDriftC { get; set; } = 1;

    /// <summary>
    /// Height of the map's highest land in metres. A heightmap carries no absolute scale, so this is
    /// what gives it one - and the lapse rate needs a real one or a mountain cannot be given a real
    /// temperature.
    /// </summary>
    [AdvancedSetting]
    [Category("8 Climate")]
    [Description("Height of the map's highest land in metres. A heightmap carries no absolute scale and the lapse rate needs one, so this is what decides how cold the mountains are. 4500 makes the tallest peak roughly alpine.")]
    public double PeakElevationMetres { get; set; } = 4000;

    /// <summary>
    /// Yearly rainfall on the middle of the map's land, in millimetres. The model's own output has
    /// no units, so this is what puts it on a scale Koppen's thresholds can be tested against.
    /// Earth's land median is around 650. The median rather than the mean because rainfall is
    /// heavily right-skewed and the mean sits far above ordinary ground.
    /// </summary>
    [Category("8 Climate")]
    [Description("Yearly rainfall on the middle of the map's land, in millimetres - Earth's is around 650. The circulation model has no units of its own, so this is what puts it on a scale Koppen can test. It scales without flattening the spread, so a dry world stays dry relative to itself.")]
    public double MedianRainfallMm { get; set; } = 550;

    /// <summary>
    /// Share of its remaining water an air parcel rains out per 100 vanilla province pixels of land
    /// it crosses. Lower carries rain much further across wide continental interiors.
    /// </summary>
    [Category("8 Climate")]
    [Description("Share of its water an air parcel rains out per 100 vanilla province pixels of land it crosses. Lower carries rain all the way across wide continental landmasses.")]
    public double RainoutPer100Pixels { get; set; } = 0.08;

    /// <summary>
    /// Extra rain a climbing air parcel drops per kilometre it is lifted. The rain shadow behind a
    /// range exists without this - cooling alone squeezes the water out - but this sharpens the
    /// contrast between the windward and leeward sides.
    /// </summary>
    [AdvancedSetting]
    [Category("8 Climate")]
    [Description("Extra rain an air parcel drops per kilometre it is lifted over a range. A rain shadow forms without this, because cooling alone squeezes the water out, but raising it sharpens the contrast between a soaking windward slope and a desert behind it.")]
    public double OrographicRainStrength { get; set; } = 0.85;

    /// <summary>
    /// How strongly the circulation's rising and sinking branches drive rainfall. This is what puts
    /// the wet belt on the equator and the great deserts at 30 degrees; at 0 the subtropical deserts
    /// largely disappear.
    /// </summary>
    [AdvancedSetting]
    [Category("8 Climate")]
    [Description("How strongly the rising and sinking branches of the circulation drive rainfall. This is what puts the wet belt on the equator and the great deserts at 30 degrees; at 0 the subtropical deserts largely disappear.")]
    public double ConvectiveRainStrength { get; set; } = 1.5;


    // =========================================================================
    // 13 Development
    // =========================================================================

    /// <summary>Development every county gets before terrain is considered.</summary>
    [Category("9 Development")]
    [Description("Development every county gets regardless of its terrain — the floor for the poorest backwater.")]
    public int DevelopmentBase { get; set; } = 0;

    /// <summary>
    /// The share of the world's counties that have no development of their own — the curve starts
    /// above them rather than running down through them.
    ///
    /// Vanilla does not spread development thinly over every county: at 867 only 973 of its 4,669
    /// counties set it at all and the other 80% are left at 0, because a tribal or nomadic
    /// periphery is not a poor version of a settled county, it is a place the mechanic does not
    /// describe yet. Ranking every county against every other produced the opposite — a smooth
    /// gradient with nothing at the bottom of it.
    ///
    /// These sit at <see cref="DevelopmentBase"/> and do NOT take the era bonus, so they are a flat
    /// share of the map rather than an early-world feature that advancement grows out of. That is
    /// vanilla's own behaviour and it is worth being explicit about, because the intuition runs the
    /// other way: its bare share is 80% at 867, 78% at 1066 and 77% at 1178 — essentially flat —
    /// while the counties that do set development climb from a median of 6 to 16. Advancement
    /// deepens the settled part of the map instead of colonising the rest of it.
    ///
    /// Raising Advancement Year still thins the tribes out, just not through this: it lifts the
    /// settled curve past the <c>avgDev</c> gates in <see cref="MapGen.Governments"/> and drops
    /// <c>timeNomadFactor</c>. Those are the levers that turn a tribal world feudal.
    ///
    /// Default is well below vanilla's 0.78 deliberately, and the reason is a coupling rather than
    /// timidity: <see cref="MapGen.Governments"/> gates feudal-versus-tribal on a realm's *average*
    /// development, which counts bare counties as poor ones. A bare periphery therefore reads as a
    /// poor realm and pushes the map toward tribal. Measured over three seeds at Advancement Year
    /// 900: at 0.25 the government mix barely moves and the median lands at 5 against vanilla's 6,
    /// while at 0.45 one seed lost 34 of its 35 feudal counties to tribal. Going the whole way to
    /// vanilla's share needs that average to ignore bare counties first.
    /// </summary>
    [Category("9 Development")]
    [Description("The share of counties that set no development at all, as vanilla leaves its tribal and nomadic periphery. Vanilla's own share is about 0.78 and is flat across its three bookmarks; the default here is lower because these counties drag their realm's average down and the government gates read it. 0 spreads development thinly over every county instead.")]
    public double DevelopmentBareShare { get; set; } = 0.25;

    /// <summary>How much development the very best terrain adds on top of the base.</summary>
    [Category("9 Development")]
    [Description("How much development the best possible terrain adds on top of the base. Vanilla's 867 median is about 8 and its mass runs to 16.")]
    public int DevelopmentSpread { get; set; } = 22;

    /// <summary>Overall multiplier on the terrain-derived part. The quick 'richer/poorer' dial.</summary>
    [Category("9 Development")]
    [Description("Overall multiplier on the terrain-derived development. The quick dial for a richer or poorer world without changing how it is distributed.")]
    public double DevelopmentScale { get; set; } = 1.0;

    /// <summary>
    /// How sharply development concentrates on the best land. 1 spreads it evenly across the
    /// ranked counties; higher makes rich counties rarer. 1.5 reproduces vanilla's 867 shape:
    /// median about 8, p90 about 19.
    /// </summary>
    [Category("9 Development")]
    [Description("How sharply development concentrates on the best land. Counties are ranked against each other, so 1 is an even spread and higher makes rich counties rarer. 1.5 reproduces vanilla 867: median about 8, p90 about 19.")]
    public double DevelopmentSkew { get; set; } = 1.5;

    /// <summary>Added to a county's terrain score if any of its baronies reaches the sea.</summary>
    [Category("9 Development")]
    [AzgaarIncompat("Counties are ranked on how many people the export puts on them, and Azgaar's " +
                    "own suitability model already counts a harbour, so the terrain score this " +
                    "adds to is not what development is read from. The rest of the development " +
                    "settings still apply: the export decides the order, the curve below decides " +
                    "the numbers.")]
    [Description("Added to a county's terrain score if any of its baronies reaches the sea, because a coast is a road when roads are bad.")]
    public double DevelopmentCoastBonus { get; set; } = 0.12;

    /// <summary>
    /// Development of the single greatest city in the world, before the era bonus. Not a bonus:
    /// the target the first world centre is placed at, with the rest stepping down toward the top
    /// of the ordinary curve.
    ///
    /// Vanilla's own 867 map is the calibration. Of its 4,669 county titles, 973 set development
    /// at all and exactly three sit above the ordinary top of 20: Chang'an at 30, Rome and
    /// Constantinople at 25. A world centre is meant to be one of those three, not a tier above
    /// them — which is what the flat +32 boost this replaced was producing.
    /// </summary>
    [Category("9 Development")]
    [Description("Development of the greatest city in the world. Vanilla 867 tops out at 30 (Chang'an), with Rome and Constantinople at 25 and the rest of the map at 20 or under; later world centres step down from this toward the top of the ordinary curve. Rises with the era alongside the rest of development.")]
    public int WorldCenterDevPeak { get; set; } = 30;


    // =========================================================================
    // 14 Cultures and faiths
    // =========================================================================

    /// <summary>Counties a generated culture covers on average. Lower makes a more fragmented world.</summary>
    [Category("10 Cultures and faiths")]
    [Description("Counties per generated culture. Vanilla averages about 20; lower values make a more fragmented, more polyglot world.")]
    [AzgaarIncompat("Cultures come from the export — one per people it drew, over the ground it drew them " +
                    "on. How many counties each covers is then a fact about the map rather than a setting.")]
    public double CountiesPerCulture { get; set; } = 18;

    /// <summary>
    /// Cultures sharing one heritage and one language. This is what decides how related neighbours
    /// are: CK3's acceptance, hybridisation and divergence all key off shared heritage, so a world
    /// of one-culture heritages is a world where nobody can ever get along with anybody.
    ///
    /// Still applies to an Azgaar import, unlike the culture count above it, and the difference is
    /// the point: an export states which peoples exist and does not state which of them are
    /// relatives. See <see cref="MapGen.AzgaarFamilies"/> — the export's own signals are read first,
    /// and this is only the target whatever they left ungrouped is grouped towards. Set it to 1 to
    /// switch that pass off and take the export exactly as it stands.
    /// </summary>
    [Category("10 Cultures and faiths")]
    [Description("Cultures sharing one heritage and language. Higher values give large related families like vanilla's Frankish or North Germanic groups; 1 gives a world where no two cultures are relatives. On an Azgaar import this groups only the peoples the export left ungrouped, after its own ancestry and name bases have been read.")]
    public double CulturesPerHeritage { get; set; } = 2;

    public enum CultureLookTheme
    {
        VariedGlobal,       // All vanilla looks
        WesternEuropean,    // Western, Frankish, English, German, Iberian
        NorthernNorse,      // Norse, Scandinavian, Northern
        ByzantineGreek,     // Byzantine, Roman, Greek
        MiddleEasternMena,  // Arabic, Persian, Bedouin, Berber, Egyptian
        SteppeNomadic,      // Steppe, Mongol, Turkic, Cuman
        SubSaharanAfrican,  // West African, Central/East African, Ethiopian
        IndianEastAsian,    // Indian, Tamil, Bengali, Tibetan, Chinese
    }

    [Category("10 Cultures and faiths")]
    [Description("Restricts culture clothing, unit models, holding graphics, and coat-of-arms palettes to a specific visual theme.")]
    public CultureLookTheme CultureAestheticsTheme { get; set; } = CultureLookTheme.VariedGlobal;

    /// <summary>
    /// How strongly culture borders follow the ground. 0 ignores terrain entirely and gives a plain
    /// voronoi; 1 uses the authored crossing costs as written.
    /// </summary>
    [Category("10 Cultures and faiths")]
    [Description("How strongly culture borders follow terrain. 0 ignores the ground and cuts straight over mountains; 1 makes ranges and deserts into language barriers.")]
    public double CultureTerrainWeight { get; set; } = 1.0;

    /// <summary>
    /// Whether every generated faith gets its own rendered icon. Off, each draws one of vanilla's
    /// faith icons at random, as it always did. See <see cref="MapGen.FaithIcons"/>.
    /// </summary>
    [Category("10 Cultures and faiths")]
    [DisplayName("Generated Faith Icons")]
    [Description("Draw each generated faith its own relief icon. Faiths of one religion share a symbol (suns, crescents, a tree) chosen from their tenets; material follows standing, from carved wood for unreformed faiths to gold and enamel for organised ones, and the inlay takes the faith's map colour. Unreformed faiths also get the icon they will wear once reformed. Off picks vanilla icons at random.")]
    public bool GenerateFaithIcons { get; set; } = true;

    /// <summary>
    /// Generated clerical regions (CK3 1.20's archdioceses) for faiths with an institutional clergy,
    /// and the regional rites their great sees found. See <see cref="MapGen.Sees"/>.
    /// </summary>
    [Category("10 Cultures and faiths")]
    [DisplayName("Generated sees")]
    [Description("Give faiths with an institutional clergy (organised, not lay clergy) clerical regions, as 1.20 gives Christianity: archbishops of the faith hold sees over their settled heartland, the head of faith's own see is the primate see, and the largest are great sees. Large faiths also start with regional rites founded by their great sees, about one per hundred counties. The sees' words come from the faith's holy tongue when native rank titles are on. Off leaves every faith with one rite and no sees.")]
    public bool GeneratedSees { get; set; } = true;

    /// <summary>
    /// Keep the main menu's 3D character portraits instead of blanking them. On trial from 2026-09-30;
    /// see <see cref="Emit.FrontendWriter.WriteFrontend"/>. Off until cold boots confirm it.
    /// </summary>
    [Category("99 Experimental")]
    [DisplayName("Main-menu portraits (trial)")]
    [Description("Show the 3D characters on the main menu instead of hiding them. They were hidden because the menu crashed on load; the cause found on 2026-09-30 is now fixed, so this tests whether hiding them is still needed. If the game crashes at the main menu, turn it off.")]
    public bool MenuPortraits { get; set; } = true;

    /// <summary>
    /// Rulers styled in their own people's language: every generated culture's words for its ranks,
    /// coined from its tongue, with variants for sovereigns, special contracts, titles held and
    /// converts. Off by default — the English vocabularies of <see cref="Emit.TitleTierWriter"/>
    /// are what most players read fastest. See <see cref="MapGen.NativeTitles"/>.
    /// </summary>
    [Category("10 Cultures and faiths")]
    [DisplayName("Native rank titles")]
    [Description("Style rulers in their own people's language: each culture's counts, dukes, kings and emperors get words built from its tongue, and sister cultures get related words. Variants follow the ruler's situation: a sovereign duke is a prince; march, palatinate and castellany contracts have their own titles; holding two or more duchies or kingdoms makes a grand duke or high king; a people that converts takes its kings' titles from the holy tongue of its new religion. A realm uses its top liege's culture's words. The church gets words too, coined in each religion's holy tongue: its sees, great sees and primacy (with Generated sees on) and its archbishops, who take the faith's own word for bishop. Off keeps the usual English titles, and the church uses See, Great See, Primacy and Hierarch.")]
    public bool NativeRankTitles { get; set; } = false;

    /// <summary>The realm half of <see cref="NativeRankTitles"/>; either works without the other.</summary>
    [Category("10 Cultures and faiths")]
    [DisplayName("Native realm names")]
    [Description("Name realms in their people's language too: Barony, County, Duchy, Kingdom and Empire become words of the top liege's tongue, with the same variants as the titles (principality, march, palatinate, castellany). Works with or without native rank titles.")]
    public bool NativeRealmNames { get; set; } = false;

    /// <summary>
    /// Wraps every native word in a hidden game concept whose tooltip names its English
    /// equivalent. Its own switch because the words then render in the link colour, which is a
    /// matter of taste, and because a concept link nested inside a character's name link is the one
    /// part of this that could only be judged in game.
    /// </summary>
    [Category("10 Cultures and faiths")]
    [DisplayName("Native rank tooltips")]
    [Description("With native titles or realm names on, hovering one of those words in game shows its real-world equivalent (King, Margrave, Duchy...). The words then show in link colour. Turn off to show them as plain text.")]
    public bool NativeRankTooltips { get; set; } = true;

    /// <summary>
    /// Wraps a generated religion's god and devil names, its head-of-faith title and its words for
    /// priest, bishop and devotee in hidden game concepts that gloss them in English on hover — the
    /// same mechanism as <see cref="NativeRankTooltips"/>. See <see cref="Emit.ReligionGlossary"/>.
    /// </summary>
    [Category("10 Cultures and faiths")]
    [DisplayName("Religion tooltips")]
    [Description("Hovering a generated religion's god, devil or clergy word in game shows what it is (God of War, The Devil, Priest...). The words then show in link colour. Turn off to show them as plain text.")]
    public bool ReligionTooltips { get; set; } = true;

    // Can be way too many nude characters lol
    // Need to extend this to cover "Nudism" cultural pillar
    [Category("10 Cultures and faiths")]
    [Description("Allow generated faiths to roll the Natural Primitivism tenet (which renders character portraits naked).")]
    public bool AllowNaturalPrimitivism { get; set; } = false;

    /// <summary>Counties a generated faith covers on average. Coarser than cultures, as in vanilla.</summary>
    [Category("10 Cultures and faiths")]
    [Description("Counties per generated faith. Deliberately coarser than cultures — vanilla runs about 120 faiths against 193 cultures.")]
    public double CountiesPerFaith { get; set; } = 26;

    /// <summary>Faiths sharing one religion, and therefore its doctrines and its gods.</summary>
    [Category("10 Cultures and faiths")]
    [Description("Faiths sharing one religion. Faiths of a religion are heresies of each other: same gods, different doctrine, and a much smaller penalty for converting between them.")]
    public double FaithsPerReligion { get; set; } = 2.5;

    /// <summary>
    /// The faith equivalent of <see cref="CultureTerrainWeight"/>, and deliberately lower. If the
    /// two matched, faith borders would land on culture borders and the map would have only one
    /// axis of difference on it.
    /// </summary>
    [Category("10 Cultures and faiths")]
    [Description("How strongly faith borders follow terrain. Kept below the culture weight on purpose: matching them puts every faith border on a culture border, and the interesting map is the one where they disagree.")]
    public double FaithTerrainWeight { get; set; } = 0.45;

    /// <summary>Holy sites each faith declares, placed on its highest-development counties.</summary>
    [Category("10 Cultures and faiths")]
    [Description("Holy sites per generated faith, placed on its richest counties. Vanilla faiths carry five.")]
    public int HolySitesPerFaith { get; set; } = 5;

    /// <summary>
    /// Share of generated faiths that start organised under a head of faith. Monotheist faiths are
    /// twice as likely, which is roughly how vanilla splits them.
    ///
    /// This is about faiths, not counties. The summary that used to sit here described a
    /// prince-bishopric share — a theocracy setting that does not exist: nothing in
    /// <see cref="MapGen.Governments"/> ever assigns <c>theocracy_government</c>.
    /// </summary>
    [Category("10 Cultures and faiths")]
    [Description("Share of generated faiths that start with a head of faith title. Monotheist faiths are twice as likely to be organised into one.")]
    public double HeadOfFaithShare { get; set; } = 0.3;

    /// <summary>
    /// How tribal a faith's counties must be before it is written as unreformed.
    ///
    /// Read against the faith's *share* of tribal counties rather than its mean development, and
    /// against the government map rather than development directly. Both choices matter: a mean
    /// over the two dozen counties of a faith regresses to the map's own mean and stops
    /// discriminating, and deriving from government means this tracks <see cref="StartYear"/> for
    /// free — a late start has almost no tribes and therefore almost no unreformed faiths, with no
    /// second threshold to keep in sync.
    /// </summary>
    [Category("10 Cultures and faiths")]
    [Description("How tribal a faith must be to start unreformed, as a share of its counties. 0.34 makes a faith unreformed once a third of its people are tribal; 0 organises everything, 1 leaves the whole map unreformed.")]
    public double UnreformedTribalShare { get; set; } = 0.34;

    /// <summary>
    /// Share of generated religions that worship one god.
    ///
    /// Weighted by how settled the religion's counties are rather than rolled flat, because a flat
    /// roll produces monotheist steppe nomads with a papacy. A fully settled religion lands near
    /// 1.6x this number and a fully tribal one near 0.15x, which leaves the map-wide rate close to
    /// what is set here.
    /// </summary>
    [Category("10 Cultures and faiths")]
    [Description("Share of generated religions that are monotheist. Weighted by how settled the religion's land is, so monotheism clusters in the developed core rather than falling randomly across the map.")]
    public double MonotheistShare { get; set; } = 0.35;

    public enum FaithShape
    {
        /// <summary>
        /// Settled monotheist religions are written Abrahamic-shaped: the <c>rf_abrahamic</c>
        /// family and hostility doctrine, so their heresies are Hostile and every other religion
        /// Evil; a chance of lay clergy and of a temporal (caliph-style) head; a great-holy-war
        /// tenet guaranteed beside a head of faith; temple art matched to theism; and the
        /// syncretism tenets that name vanilla religions filtered out. Everything else stays pagan.
        /// </summary>
        Shaped,

        /// <summary>
        /// Every religion is <c>rf_pagan</c> with the pagan hostility doctrine, as the generator
        /// always wrote them. Nothing on the map is ever Evil to anything, and no faith can holy-war
        /// its own religion's heresies. Kept as the exact old output.
        /// </summary>
        PaganOnly,
    }

    /// <summary>
    /// Whether settled monotheist religions take vanilla's Abrahamic shape or every religion is
    /// written pagan. See <see cref="MapGen.Faiths"/> and the notes on <see cref="FaithShape"/>.
    /// </summary>
    [Category("10 Cultures and faiths")]
    [Description("Shaped writes settled monotheist religions like vanilla's Abrahamic ones: heresies are Hostile and rival religions Evil (so holy wars reach both), some get lay clergy or a temporal head, a great-holy-war tenet comes with every head of faith, and temple art follows theism. PaganOnly is the old output: every religion pagan, every rival merely Hostile, no holy wars between heresies.")]
    public FaithShape FaithShaping { get; set; } = FaithShape.Shaped;


    // =========================================================================
    // 15 Rulers
    // =========================================================================

    /// <summary>Share of duchies whose title is held by somebody at the start date.</summary>
    [Category("11 Rulers")]
    [AzgaarIncompat("Realms come from the export's states — one realm per country, with independence and " +
                    "vassalage taken from Azgaar's own relations rather than from a share of the de jure " +
                    "tree. Read again only if no state bound to a title.")]
    [Description("Share of duchies actually held by a duke at the start date. The rest of their counties stand as independent counts or answer to a king directly.")]
    public double DuchyTitleShare { get; set; } = 0.5;

    /// <summary>Share of kingdoms whose title is held by somebody at the start date.</summary>
    [Category("11 Rulers")]
    [AzgaarIncompat("Realms come from the export's states — one realm per country, with independence and " +
                    "vassalage taken from Azgaar's own relations rather than from a share of the de jure " +
                    "tree. Read again only if no state bound to a title.")]
    [Description("Share of kingdoms actually held by a king at the start date. Realising one also realises its strongest duchy, so a king is always a duke and a count as well.")]
    public double KingdomTitleShare { get; set; } = 0.25;

    /// <summary>
    /// Share of empires whose title is held by somebody. Kept low deliberately: an emperor in 867
    /// should be a rarity the map is built around, not a tier everyone occupies.
    /// </summary>
    [Category("11 Rulers")]
    [AzgaarIncompat("Realms come from the export's states — one realm per country, with independence and " +
                    "vassalage taken from Azgaar's own relations rather than from a share of the de jure " +
                    "tree. Read again only if no state bound to a title.")]
    [Description("Share of empires actually held by an emperor at the start date. Kept low on purpose — an emperor should be a rarity the map is built around.")]
    public double EmpireTitleShare { get; set; } = 0.15;

    /// <summary>
    /// Whether realms are grown by simulating centuries of conquest, or handed out by walking the
    /// de jure tree from the top.
    ///
    /// Off, the political map is the de jure map with some titles left unheld — the two are built
    /// by the same geographic clustering, so every realm border is also a de jure border and no
    /// duchy is ever split between two kingdoms. On, realms are grown across the county adjacency
    /// graph by <see cref="MapGen.Formation"/>, which does not know the de jure tree exists, and the
    /// three shares above stop being quotas: they lean on how hard the simulation consolidates
    /// rather than deciding how many of each tier come out of it.
    /// </summary>
    [Category("11 Rulers")]
    [DisplayName("Simulate Realm Formation")]
    [AzgaarIncompat("The export already states its own countries and which of them are vassals, so there is " +
                    "nothing to simulate — realms come from the states. Read again only if no state bound " +
                    "to a title, which is the case an export with no countries drawn on it falls into.")]
    [Description("Grow realms by simulating centuries of conquest, rather than handing out titles down the de jure tree. On, realm borders cut across de jure lines and the tier shares become an influence rather than a quota. Off, de facto and de jure are the same map.")]
    public bool SimulateFormation { get; set; } = true;

    /// <summary>
    /// How long the formation simulation runs before the start date, in years. Ticked a reign at a
    /// time, so this divided by 25 is how many rounds of conquest the map has been through.
    /// </summary>
    [Category("11 Rulers")]
    [DisplayName("Formation Years")]
    [Description("How many years of conquest to simulate before the start date. Longer runs consolidate further, and give the oldest realms more time to become coherent — but also more time to overreach and come apart.")]
    public int FormationYears { get; set; } = 600;

    /// <summary>
    /// How readily simulated realms fragment and collapse. At zero the simulation only ever
    /// consolidates and the map ends up dominated by a few very large realms; at one, great powers
    /// rise and shatter repeatedly and the start date catches a world of successor states.
    /// </summary>
    [Category("11 Rulers")]
    [DisplayName("Formation Turbulence")]
    [Description("How readily simulated realms overreach, fragment, and collapse. Low leaves a few large stable powers; high leaves a crowded map of successor states with long memories. This is the main control on how much history the world has been through.")]
    public double FormationTurbulence { get; set; } = 0.5;

    /// <summary>
    /// Share of *eligible* counties — settled, coastal and well above the tribal line — that start
    /// as merchant republics. The eligibility is most of the rarity: vanilla's republics are a
    /// handful of ports, not a proportion of the world.
    /// </summary>
    [Category("11 Rulers")]
    [AzgaarIncompat("Governments come from each state's own form and type — a republic is one because Azgaar " +
                    "called it one. Read again only for counties no state claims.")]
    [Description("Share of settled, prosperous coastal counties that start as merchant republics. Their capital is a city rather than a castle, so this also changes what those counties hold.")]
    public double RepublicShare { get; set; } = 0.1;

    [Category("11 Rulers")]
    [AzgaarIncompat("Governments come from each state's own form and type, and no Azgaar form word maps to " +
                    "administrative government, so an imported map has none however this is set. Read again " +
                    "only for counties no state claims.")]
    [Description("Enable centralized Administrative Empires with bureaucratic themes and noble families (requires Roads to Power DLC; safely degrades to Feudal/Clan if DLC is absent).")]
    public bool EnableAdministrativeEmpires { get; set; } = true;

    [Category("11 Rulers")]
    [AzgaarIncompat("Governments come from each state's own form and type, and no Azgaar form word maps to " +
                    "administrative government, so an imported map has none however this is set. Read again " +
                    "only for counties no state claims.")]
    [Description("Share of realized imperial realms that start with an Administrative Government.")]
    public double AdministrativeEmpireShare { get; set; } = 0.25;

    [Category("11 Rulers")]
    [AzgaarIncompat("Governments come from each state's own form and type, and no Azgaar form word maps to " +
                    "administrative government, so an imported map has none however this is set. Read again " +
                    "only for counties no state claims.")]
    [Description("Earliest start year for Administrative Governments to emerge across the realm. Empires before this year default to Feudal/Clan unless they host an Imperial World Center.")]
    public int AdministrativeMinStartYear { get; set; } = 899;

    [Category("11 Rulers")]
    [Description("Enable Nomadic horde realms across steppes and arid plains (requires nomadic DLC; safely degrades to Tribal/Clan if DLC is absent).")]
    public bool EnableNomadHordes { get; set; } = true;

    [Category("11 Rulers")]
    [AzgaarIncompat("Governments come from each state's own form and type — a horde is one because Azgaar " +
                    "called its state Nomadic or named it a Khanate, not because of the ground it stands " +
                    "on. Enable nomad hordes still applies, and turns those states tribal instead. Read " +
                    "again only for counties no state claims.")]
    [Description("Share of qualifying steppe and arid realms that start as Nomads. Qualifying means a real pastoral majority: a fifth of the realm on steppe, or three fifths on steppe/desert/drylands together, or a steppe capital. Early starts add up to +0.25 to this and late starts subtract up to 0.25.")]
    public double NomadSteppeShare { get; set; } = 0.3;

    // All Under Heaven's regional varieties. Each is laid over a realm the cascade has already
    // decided, so these can only redistribute what the settings above produce — a world with no
    // administrative empires has no meritocracies whatever MeritocraticShare says. Every one of
    // them reverts to its base government for players without the expansion. They apply to Azgaar
    // imports too, laid over each state's own government (AzgaarGovernments).

    [Category("11 Rulers")]
    [Description("Share of Administrative empires (other than a crowned hegemon) that run on merit instead: Meritocratic government, with imperial examinations and merit-graded appointments. Requires All Under Heaven; becomes Feudal without it.")]
    public double MeritocraticShare { get; set; } = 0.5;

    [Category("11 Rulers")]
    [Description("Share of eligible steppe powers that start as Steppe Administrations — a bureaucracy grown out of the grasslands. Eligible means an Administrative empire standing on a fifth or more steppe, or a developed horde of kingdom rank or higher after the Administrative start year. Requires All Under Heaven; becomes Feudal without it.")]
    public double SteppeAdminShare { get; set; } = 0.2;

    [Category("11 Rulers")]
    [Description("Share of feudal and tribal realms seated in the jungle that start as Sacral: tributary overlordship and radiance, with temple citadels for capitals. Eligible means a jungle capital or a realm a third or more jungle. Requires All Under Heaven; becomes Feudal without it.")]
    [DisplayName("Sacral share")]
    public double MandalaShare { get; set; } = 0.2;

    [Category("11 Rulers")]
    [Description("Share of seafaring tribal realms that start as Maritime: barter, cheap embarkation and safer seas. Eligible means a coastal capital and at least half the realm's counties on the coast. Requires All Under Heaven; becomes Tribal without it.")]
    [DisplayName("Maritime share")]
    public double WanuaShare { get; set; } = 0.2;

    [Category("11 Rulers")]
    [Description("Share of feudal realms of a martial people (Bellicose ethos), duchy rank or above, that start as Martial: the warrior houses' feudalism, with house aspirations, county noble families, knights and cheaper archer cavalry and heavy infantry. Requires All Under Heaven; becomes Feudal without it.")]
    [DisplayName("Martial share")]
    public double SoryoShare { get; set; } = 0.3;


    // =========================================================================
    // 16 Wilderness
    // =========================================================================

    /// <summary>
    /// Share of counties left unsettled, as a fraction of the whole map.
    ///
    /// A target rather than a guarantee, but a close one: the clumping pass gives back anything too
    /// small to read as a region without counting it, and widens its search until the share is met,
    /// so it lands under this only on a map with no room for it. It can land a little over when a
    /// region strands counties a title needs, which are absorbed. Zero disables placement
    /// while still shipping the scripts — but with <see cref="EnableWilderness"/> on, at least one
    /// county is always placed, because a wilderness system with no wilderness in it is
    /// indistinguishable from a broken one and there would be nothing to test against.
    /// </summary>
    [Category("12 Wilderness")]
    [AzgaarIncompat("Wilderness is the ground the export left unclaimed, which is a statement rather than " +
                    "the habitability guess this scores. An export that gives every county to a state starts " +
                    "with no wilderness at all rather than carving populated states, so this is never read on " +
                    "an import. Ruins and failed colonies still have somewhere to go.")]
    [Description("Share of counties left as unsettled wilderness, placed on the least liveable ground. Clumps too small to read as a region are given back and replaced, so the delivered share lands close to this.")]
    public double WildernessShare { get; set; } = 0.12;

    /// <summary>
    /// How strongly wilderness is pulled toward the map's edges rather than its interior.
    ///
    /// 1 puts it on the rim, 0 ignores position entirely and places purely on how hostile the
    /// ground is, and -1 pulls it inland instead. The default leans outward because a frontier
    /// reads as a frontier when it is at the edge of the known world; set it negative for a map
    /// whose middle is a wasteland and whose coasts are settled.
    /// </summary>
    [Category("12 Wilderness")]
    [AzgaarIncompat("Wilderness is the ground the export left unclaimed, which is a statement rather than " +
                    "the habitability guess this scores. An export that gives every county to a state starts " +
                    "with no wilderness at all rather than carving populated states, so this is never read on " +
                    "an import. Ruins and failed colonies still have somewhere to go.")]
    [Description("Pull wilderness toward the map edges (1), ignore position (0), or pull it inland (-1). Edge-biased reads as a frontier at the rim of the world; inland-biased makes the interior the wasteland.")]
    public double WildernessEdgeBias { get; set; } = 0.75;

    /// <summary>
    /// How strongly hostile ground attracts wilderness, against everything else.
    ///
    /// The main term of the placement score: how unliveable a county is, from its terrain and how
    /// little development its ground supports. Position — <see cref="WildernessEdgeBias"/> and
    /// <see cref="WildernessAvoidRealmInteriors"/> — is scaled down to break ties between similar
    /// ground, so at the default it never outweighs a real difference in terrain. At 0 wilderness
    /// lands wherever the position bias says regardless of terrain, which produces empty farmland.
    /// </summary>
    [AdvancedSetting]
    [Category("12 Wilderness")]
    [AzgaarIncompat("Wilderness is the ground the export left unclaimed, which is a statement rather than " +
                    "the habitability guess this scores. An export that gives every county to a state starts " +
                    "with no wilderness at all rather than carving populated states, so this is never read on " +
                    "an import. Ruins and failed colonies still have somewhere to go.")]
    [Description("How strongly wilderness follows hostile terrain — mountains, ice, desert, marsh, jungle — against the edge bias. At 0 it ignores terrain entirely and can leave empty farmland.")]
    public double WildernessTerrainWeight { get; set; } = 0.75;

    /// <summary>
    /// Smallest run of connected counties kept as wilderness.
    ///
    /// The whole reason placement is a two-pass affair. Ranking counties by score and taking the
    /// worst N speckles single wild counties through settled land, which reads as a generation
    /// fault rather than as a frontier; growing clumps from seeds and discarding the runts is what
    /// makes the result look deliberate. 1 disables the check and restores the speckle.
    ///
    /// A clump with no settled land around it is not a runt at any size: a county that is its
    /// whole island reads as an island nobody settled, and one beside wilderness already placed
    /// is part of that region.
    /// </summary>
    [AdvancedSetting]
    [Category("12 Wilderness")]
    [AzgaarIncompat("Wilderness is the ground the export left unclaimed, which is a statement rather than " +
                    "the habitability guess this scores. An export that gives every county to a state starts " +
                    "with no wilderness at all rather than carving populated states, so this is never read on " +
                    "an import. Ruins and failed colonies still have somewhere to go.")]
    [Description("Smallest connected run of counties kept as wilderness. Lone wild counties surrounded by settled land read as a bug, so runts below this are given back. Set to 1 to allow singletons.")]
    public int WildernessMinClump { get; set; } = 2;

    /// <summary>
    /// How strongly wilderness avoids the middle of a kingdom.
    ///
    /// Placement otherwise seeks exactly the wrong ground. Hostile terrain forms ridges — mountain
    /// ranges, marsh belts — and the clumping pass deliberately follows them, so the same rule that
    /// makes wilderness look intentional also runs it straight through the middle of realms and
    /// leaves starting kingdoms in disconnected halves. <see cref="WildernessEdgeBias"/> does not
    /// help: it measures distance from the middle of the MAP, and a range through the heart of a
    /// kingdom near the map's rim scores high on it.
    ///
    /// This scores a county down by the share of its neighbours belonging to the same kingdom, so
    /// borders and coasts are preferred over interiors. It is a bias, not a guarantee — the
    /// connectivity guard in <see cref="MapGen.Wilderness"/> is what actually refuses to sever a
    /// title. Raising this mostly reduces how often that guard has to throw a region away.
    ///
    /// 0 restores the old behaviour of ignoring realm shape entirely.
    /// </summary>
    [Category("12 Wilderness")]
    [AzgaarIncompat("Wilderness is the ground the export left unclaimed, which is a statement rather than " +
                    "the habitability guess this scores. An export that gives every county to a state starts " +
                    "with no wilderness at all rather than carving populated states, so this is never read on " +
                    "an import. Ruins and failed colonies still have somewhere to go.")]
    [Description("How strongly wilderness avoids the interior of a kingdom, preferring borders and coasts. Placement otherwise follows mountain ranges straight through the middle of realms and splits them in two. 0 ignores realm shape.")]
    public double WildernessAvoidRealmInteriors { get; set; } = 0.6;

    /// <summary>
    /// Share of generated faiths that keep one holy site out in unclaimed wilderness.
    ///
    /// A sacred grove, a peak, a ruin past the last farm — ground the faith reveres and nobody
    /// holds. It is the only thing in the system that makes one wilderness county worth more than
    /// another: without it every unsettled county is interchangeable and you simply take whichever
    /// the adjacency scoring picks.
    ///
    /// Only ever placed in wilderness that shares a de jure duchy or kingdom with the faith's own
    /// land, so it reads as their sacred ground rather than a random assignment, and never as the
    /// faith's primary site — a head of faith cannot be seated on land nobody holds.
    ///
    /// 0 disables it. High values make holy wilderness ordinary, which defeats the point.
    /// </summary>
    [Category("12 Wilderness")]
    [Description("Share of generated faiths with one holy site out in unclaimed wilderness — a grove or peak nobody holds. Gives a reason to want one particular wilderness county rather than any of them. Never the faith's primary site.")]
    public double WildernessHolySiteShare { get; set; } = 0.15;

    /// <summary>
    /// Whether a settled county can fall back out of civilisation.
    ///
    /// Gates the <c>Ruins</c> file set — a second dummy holder, the decay counter that decides a
    /// county is dying, the collapse itself, and the discovery event when somebody digs the stones
    /// out again. Off by default because it is the one system here that can take something away
    /// from the player without being asked to.
    ///
    /// Requires <see cref="EnableWilderness"/>. Ruins are unsettled counties with a history, so
    /// they are held under the same government, marked with the same trait, and reclaimed through
    /// the same colonisation flow; without that flow there is nothing for a ruin to be. The two are
    /// ANDed everywhere rather than this one silently implying the other, so a mod configured with
    /// ruins on and wilderness off ships neither half instead of a broken one.
    /// </summary>
    /// HIDING FOR THIS BUILD
    [Category("12 Wilderness")]
    [Description("HEAVILY WIP: Let settled counties collapse back into wilderness when they are dying — repeated sacking, plague, lost control — leaving ruins that have to be cleared before the land can be settled again. Requires the wilderness system. Note that it can take a county away from a player. No county starts ruined unless Ruins share is above 0.")]
    public bool EnableRuins { get; set; } = true;

    /// <summary>
    /// Share of counties that start already ruined.
    ///
    /// Zero by default, and that is the intended setting rather than a disabled feature: with
    /// <see cref="EnableRuins"/> on and this at zero the world begins whole and ruins are something
    /// that HAPPENS, which is the version of the mechanic that has a story in it. Raising it seeds
    /// a fallen age instead — ground somebody held before the bookmark and does not hold now.
    ///
    /// Placed unlike wilderness, and deliberately so. Wilderness is scored — hostile ground, map
    /// edges, away from the middle of a kingdom — because unsettled land has to look like land
    /// nobody wanted. A ruin is the opposite claim: somebody wanted it enough to build there, so it
    /// belongs wherever people are, including the middle of a settled kingdom. Ruins are therefore
    /// drawn uniformly from every county the wilderness pass did not take, with no terrain bias, no
    /// edge bias, no realm-interior avoidance and no clumping — a lone ruined county surrounded by
    /// settled land reads as a story, where a lone WILD county reads as a generation fault.
    ///
    /// A fraction of the whole map, not of the wilderness, so it means the same thing at any
    /// <see cref="WildernessShare"/>.
    /// </summary>
    /// HIDING FOR THIS BUILD
    [Category("12 Wilderness")]
    [Description("Share of counties that start as ruins — held by nobody, with the stones of whoever was there before still standing. Unlike wilderness these are scattered anywhere, including inside settled kingdoms. 0 means the world starts whole and ruins only ever happen in play.")]
    public double RuinsShare { get; set; } = 0.0;

    // HIGHLY WIP //

    public enum FantasyRaceMode
    {
        HumanOnly,      // Realistic human phenotypes adapted to latitude/climate
        LowFantasy,     // Humans dominant (~85%), with rare Elven, Dwarven, or Orcish realms (~15%)
        HighFantasy,    // Balanced distribution of Elves, Dwarves, Humans, Orcs, Beastfolk, Dusk Elves
        ExoticSurreal   // Wild procedural morphs (exotic skin hues, vibrant hair/eyes, extreme heights)
    }

    [Category("14 Fantasy/Ethnicities")]
    [Description("Enable procedural fantasy racial phenotypes (Elves, Dwarves, Orcs, Giants, Dusk Elves, etc.).")]
    public bool EnableFantasyEthnicities { get; set; } = false;

    /// <summary>
    /// Which real-world looks the world's HUMANS are drawn from.
    ///
    /// This is the only knob over human appearance, and it touches nothing else. A generated
    /// human ethnicity inherits from one vanilla ethnicity — <c>caucasian</c>, <c>asian_malay</c>,
    /// <c>east_african</c> and so on — and that inheritance is the whole of its complexion, because
    /// humans deliberately emit no <c>skin_color</c> of their own. Left at <see cref="HumanLook.Varied"/>
    /// the pick is uniform across all four look families, which is why an unconfigured world reads
    /// as ethnically scrambled: neighbouring cultures inside one heritage can land on opposite sides
    /// of the planet. A preset narrows the draw to one region's templates instead.
    ///
    /// **Fantasy races are not affected, by construction.** A race's look family is fixed in
    /// <c>Ethnicities.CreateEthnicity</c> on a separate switch arm that never consults this, and its
    /// colouring comes from its own <c>gen_race_skin</c> shift rather than from a vanilla template.
    /// So a Sub-Saharan world's elves stay exactly the elves a Varied world would have produced.
    ///
    /// Per-culture exceptions are an edit, not a setting: the Cultures inspector can retemplate one
    /// culture without disturbing its heritage siblings. This decides the world's default, not its
    /// uniformity.
    /// </summary>
    public enum HumanLook
    {
        /// <summary>
        /// Every look family, in an even split, each placed by climate (see MapGen/Peoples/HumanLooks.cs).
        /// Seeds from before 2026-09-28 get different human looks under it; nothing else moves.
        /// </summary>
        Varied,

        /// <summary>Northern and western Europe — the Norse, Irish, English, French end.</summary>
        WesternEuropean,

        /// <summary>The Mediterranean basin, both shores: Iberian and Italian through Greek to Levantine and Maghrebi.</summary>
        Mediterranean,

        /// <summary>Sub-Saharan Africa, west and east.</summary>
        SubSaharan,

        /// <summary>China, Korea, Japan and the steppe.</summary>
        EastAsian,

        /// <summary>Maritime and mainland South East Asia, out into the Pacific.</summary>
        SoutheastAsian,

        /// <summary>All of Europe rather than one corner of it — Atlantic to Urals, north to south.</summary>
        MixedEuropean,

        /// <summary>A Mediterranean world with a real African share rather than a coastal trace.</summary>
        MixedMediterranean,

        /// <summary>Asia broadly — East, South East and South together.</summary>
        MixedAsian,

        /// <summary>Arabia, Egypt and Persia, with the Caucasus and Anatolia at its edge.</summary>
        MiddleEastern,

        /// <summary>The steppe from the Pontic grasslands to Mongolia: Turkic peoples west and east, and the Mongols.</summary>
        Steppe
    }

    [Category("14 Fantasy/Ethnicities")]
    [Description("Which real-world looks the world's humans are drawn from. Varied deals the four look families (European, African, Asian, Middle Eastern & South Asian) out evenly across the heritages and places each where its climate fits — Sámi-like peoples in the taiga, Mediterranean ones on dry-summer coasts, African ones in the tropics; a preset narrows the world to one region's vanilla templates in fixed proportions, placed the same way. Colouring is vanilla's own, with each culture's skin tone shaded for its climate. Affects HUMANS only — fantasy races keep their own colouring either way. A single culture can be moved off its look afterwards in the Cultures inspector without disturbing the rest of its heritage.")]
    public HumanLook DominantLook { get; set; } = HumanLook.Varied;

    [Category("14 Fantasy/Ethnicities")]
    [Description("Race distribution mode across the generated world.")]
    public FantasyRaceMode RaceMode { get; set; } = FantasyRaceMode.LowFantasy;

    [Category("14 Fantasy/Ethnicities")]
    [Description("Tie race strictly to Heritage (all cultures under a heritage share the same core race with regional variations).")]
    public bool TieRaceToHeritage { get; set; } = true;

    [Category("14 Fantasy/Ethnicities")]
    [Description("How many distinct races the world must contain. Raises the heritage count if culture density would not otherwise produce enough regions to hold them, so a high value costs you smaller heritages. 9 races exist (humans included), so no more than 9 can be delivered; a higher value still raises the heritage floor, and the default's one spare heritage is what lets humans spread over two climates without pushing a fantasy race out of a homeland. Require terrain can hold the delivered count below this.")]
    public int GuaranteedRaceCount { get; set; } = 10;

    /// <summary>
    /// How much say the land has over who lives on it. Every race has terrain it wants —
    /// dwarves mountains, wood elves forest, giantkin the arctic — and this decides whether
    /// that is a preference or a precondition.
    /// </summary>
    public enum RaceTerrainRule
    {
        /// <summary>Terrain plays no part. Races land wherever the quota and the dice put them.</summary>
        Ignore,

        /// <summary>
        /// Terrain biases placement but never blocks it, so <see cref="GuaranteedRaceCount"/> is
        /// always delivered. A map with no mountains still gets its dwarves; they just settle on
        /// the least unsuitable ground available.
        /// </summary>
        Prefer,

        /// <summary>
        /// A race may only take land where terrain it wants covers at least a fifth of the
        /// region. Races with nowhere to live are dropped rather than misplaced, so a map with
        /// no forest simply has no wood elves and the delivered race count falls short of
        /// <see cref="GuaranteedRaceCount"/>. Humans are never blocked.
        /// </summary>
        Require
    }

    [Category("14 Fantasy/Ethnicities")]
    [Description("How much say terrain has over where a race may appear. Prefer biases placement but always delivers the requested race count. Require only settles a race where terrain it wants covers at least a fifth of the region, and drops races that have nowhere to live — so a map with no mountains gets no dwarves rather than misplaced ones. Humans are never blocked.")]
    public RaceTerrainRule RaceTerrain { get; set; } = RaceTerrainRule.Prefer;

    [Category("14 Fantasy/Ethnicities")]
    [Description("When the mode's human:fantasy land ratio leaves no room for every guaranteed race to hold territory of its own, let the overflow races live as small minorities (~13% of characters) inside a well-suited human culture instead of not appearing at all. Minority members look their race and carry their race's phenotype trait, resolved at game start from their own genes rather than from their host culture — they are a people living among another, not a faction with land. Off means the guarantee simply wins the land and the ratio is sacrificed with a warning.")]
    public bool AllowMinorityRaces { get; set; } = true;

    /// <summary>
    /// Whether the world's race shares are set by hand rather than by <see cref="RaceMode"/>.
    ///
    /// The mode still decides how pronounced the races look (their morph and skin intensity); this
    /// takes over only how much land each one holds. Off, the rows below are hidden and ignored, and
    /// a seed makes exactly the world it made before the mix existed. See Ethnicities.MixBudget for
    /// how the shares are spent.
    /// </summary>
    [Category("14 Fantasy/Ethnicities")]
    [DisplayName("Custom Race Mix")]
    [RefreshProperties(RefreshProperties.All)]
    [Description("Set how much of the land each race holds, instead of the race mode's own split. The mode still decides how strongly the races look like themselves. Races settle whole peoples, so the shares are targets rather than exact figures, and a small share often turns up as a minority living among another people instead of a realm of its own.")]
    public bool CustomRaceMix { get; set; } = false;

    /// <summary>
    /// The share of counties humans hold under <see cref="CustomRaceMix"/>, in percent. 0 follows the
    /// race mode (85% low fantasy, 35% high, 12% exotic). Anything else is held to
    /// <see cref="MinRaceMixHumanShare"/>..<see cref="MaxRaceMixHumanShare"/>: humans are the
    /// fallback for land no race's terrain allows and the hosts every minority lives among, so a
    /// world cannot be made without some, and a fantasy world with none of the other races is the
    /// None setting, not a mix.
    /// </summary>
    [Category("14 Fantasy/Ethnicities")]
    [DisplayName("Mix: Humans (% of land)")]
    [TypeConverter(typeof(FollowRaceModeConverter))]
    [Description("With Custom Race Mix: the share of counties humans hold, in percent. 0 follows the race mode (85% low fantasy, 35% high, 12% exotic). Kept between 10 and 95: humans are who the land falls to where no race fits, and who minorities live among. The other races divide the rest by the weights below.")]
    public int RaceMixHumanShare { get; set; } = 0;

    public const int MinRaceMixHumanShare = 10;
    public const int MaxRaceMixHumanShare = 95;

    [Category("14 Fantasy/Ethnicities")]
    [DisplayName("Mix: Dwarves")]
    [Description("With Custom Race Mix: how much of the non-human land dwarves get, relative to the other races' weights (0-100). 0 leaves them out of the world.")]
    public int RaceMixDwarves { get; set; } = 50;

    [Category("14 Fantasy/Ethnicities")]
    [DisplayName("Mix: High Elves")]
    [Description("With Custom Race Mix: how much of the non-human land high elves get, relative to the other races' weights (0-100). 0 leaves them out of the world.")]
    public int RaceMixHighElves { get; set; } = 50;

    [Category("14 Fantasy/Ethnicities")]
    [DisplayName("Mix: Wood Elves")]
    [Description("With Custom Race Mix: how much of the non-human land wood elves get, relative to the other races' weights (0-100). 0 leaves them out of the world.")]
    public int RaceMixWoodElves { get; set; } = 50;

    [Category("14 Fantasy/Ethnicities")]
    [DisplayName("Mix: Orcs")]
    [Description("With Custom Race Mix: how much of the non-human land orcs get, relative to the other races' weights (0-100). 0 leaves them out of the world.")]
    public int RaceMixOrcs { get; set; } = 50;

    [Category("14 Fantasy/Ethnicities")]
    [DisplayName("Mix: Gnomes")]
    [Description("With Custom Race Mix: how much of the non-human land gnomes get, relative to the other races' weights (0-100). 0 leaves them out of the world.")]
    public int RaceMixGnomes { get; set; } = 50;

    [Category("14 Fantasy/Ethnicities")]
    [DisplayName("Mix: Giantkin")]
    [Description("With Custom Race Mix: how much of the non-human land giantkin get, relative to the other races' weights (0-100). 0 leaves them out of the world.")]
    public int RaceMixGiantkin { get; set; } = 50;

    [Category("14 Fantasy/Ethnicities")]
    [DisplayName("Mix: Dusk Elves")]
    [Description("With Custom Race Mix: how much of the non-human land dusk elves get, relative to the other races' weights (0-100). 0 leaves them out of the world.")]
    public int RaceMixDuskElves { get; set; } = 50;

    [Category("14 Fantasy/Ethnicities")]
    [DisplayName("Mix: Hornkin")]
    [Description("With Custom Race Mix: how much of the non-human land hornkin get, relative to the other races' weights (0-100). 0 leaves them out of the world.")]
    public int RaceMixHornkin { get; set; } = 50;

    /// <summary>The rows <see cref="GetProperties(Attribute[])"/> shows only under <see cref="CustomRaceMix"/>.</summary>
    private static bool IsRaceMixRow(string name) => name.StartsWith("RaceMix", StringComparison.Ordinal);


    // =========================================================================
    // Derived Calculations, Geometry, Scales & Resolvers
    // =========================================================================

    /// <summary>settings.equator — in raster space.</summary>
    [Browsable(false)]
    public double Equator => Height * Math.Clamp(EquatorPosition, 0.0, 1.0);

    /// <summary>
    /// How many heightmap pixels go to one province pixel, on each axis — and therefore to one
    /// world unit, because camera space *is* province space.
    ///
    /// This is the single knob that decides how much world a given heightmap becomes, and it used
    /// to be welded at vanilla's 2. Vanilla is the outlier: measured 2026-08-28, every total
    /// conversion on this machine ships 1 — AGOT 9216x6144 source and provinces, Elder Kings 2
    /// 8256x5504, Legacy of Valyria 9216x6144. Only the base game spends four heightmap pixels per
    /// world unit.
    ///
    /// Welding it at 2 conflated two independent things: how much terrain detail a map has, and how
    /// big its world is. A 9216-wide heightmap became a 4608-wide world at MapScale 0.5 — which is
    /// the map size where land sag was measured at 1.8x vanilla's p90, because a smaller world
    /// resamples the same relief into fewer pixels and steepens every slope by 1/MapScale. The same
    /// file at 1 is a 9216-wide world: vanilla's province map exactly, MapScale 1, no steepening,
    /// and every correction in CompatibilityWriter reduced to a no-op.
    ///
    /// The default is 1: spend the whole heightmap on world size. A 9216-wide source then becomes a
    /// 9216-wide world, which is vanilla's province map exactly — MapScale 1, no slope steepening,
    /// and every correction in CompatibilityWriter inert. The barony count that comes with it is
    /// vanilla's too, and <see cref="CountyScale"/> is what trims that rather than this.
    ///
    /// <b>Whole numbers only, and that is a constraint rather than a preference.</b>
    /// <see cref="MapGen.Raster.ProvinceBlock"/> is the single definition of how the two rasters line
    /// up, and it box-averages an integer number of heightmap pixels per province pixel. A
    /// fractional ratio does not round there, it truncates: 1.25 on a 9216 heightmap gives a 7372
    /// province map, <c>9216 / 7372</c> is 1 in integer arithmetic, and the whole partition is then
    /// derived from the top-left 7372x3686 *crop* while the engine stretches the full heightmap over
    /// the world. Terrain slides left of its own borders, further the further right you look. That
    /// shipped once, on 2026-08-28, and <see cref="EffectiveProvinceDownscale"/> is why it cannot
    /// again. Fractional ratios need ProvinceBlock to resample rather than block-average first.
    ///
    /// Not a claim that the province raster should match the heightmap shape for shape. That was
    /// checked separately and remains true: CK3 draws land at *county* level, so the player sees the
    /// union of a county's baronies and never a barony on its own — which is why sea zones and
    /// impassable mountains, the two kinds with no county above them, are the two kinds that do line
    /// up. Ragged shapes come from Titles.Cluster, not from here.
    /// </summary>
    [Category("01 General")]
    [Description("Heightmap pixels per province pixel, and so per world unit. 1 (the default) makes the world as large as the heightmap allows — what AGOT and Elder Kings 2 ship — and on a 9216-wide heightmap gives vanilla's own province map. 2 is vanilla's ratio: the finest terrain per county, but half the world and steeper slopes. Whole numbers only; anything else is rounded, because the raster sampler averages a whole number of heightmap pixels per province pixel. Use CountyScale, not this, to trim the barony count.")]
    public double ProvinceDownscale { get; set; } = 1.0;

    /// <summary>
    /// <see cref="ProvinceDownscale"/> as actually applied: a whole number, never below 1, and never
    /// small enough to take the province raster past vanilla's own width.
    ///
    /// Rounded to nearest and then floored by the cap, so a value asked for is honoured where it can
    /// be and only ever moves in the safe direction. Below 1 there is nothing to gain — a province
    /// map larger than the heightmap upsamples detail that is not there — and the cap is
    /// <c>ceil(Width / 9216)</c> because that is the smallest whole divisor that keeps the raster at
    /// or under vanilla's.
    ///
    /// The cap is on the *divisor* rather than on the width so both axes move together. Capping
    /// width alone would stretch the world's aspect away from the heightmap it was drawn from, which
    /// is a silent CK3 failure rather than an error.
    /// </summary>
    [Browsable(false)]
    public int EffectiveProvinceDownscale => Math.Max(
        Math.Max((int)Math.Round(Math.Max(ProvinceDownscale, 1.0)), 1),
        (int)Math.Ceiling((double)Width / ReferenceProvinceWidth));

    /// <summary>
    /// Whether <see cref="EffectiveProvinceDownscale"/> had to move off what was asked for — the
    /// build log says so, because the barony count and the world size both follow it.
    /// </summary>
    [Browsable(false)]
    public bool ProvinceDownscaleAdjusted =>
        Math.Abs(EffectiveProvinceDownscale - ProvinceDownscale) > 1e-9;

    [Browsable(false)]
    public int ProvinceWidth => EvenDown(Width / EffectiveProvinceDownscale);

    [Browsable(false)]
    public int ProvinceHeight => EvenDown(Height / EffectiveProvinceDownscale);

    /// <summary>Rounds down to even — the loader rejects odd raster dimensions.</summary>
    private static int EvenDown(int v) => v - (v & 1);

    // --- Map scale ---
    //
    // Everything measured in pixels is authored against vanilla's province map and scaled from
    // there, so changing map size resamples the same world rather than changing what is on it.

    /// <summary>Vanilla's province-map width. The scale everything pixel-denominated is authored at.</summary>
    public const int ReferenceProvinceWidth = 9216;

    /// <summary>This map's province raster relative to vanilla's, linearly.</summary>
    [Browsable(false)]
    public double MapScale => (double)ProvinceWidth / ReferenceProvinceWidth;

    /// <summary>Scales a length authored in vanilla province pixels onto this map.</summary>
    public double Scaled(double vanillaPixels) => vanillaPixels * MapScale;

    /// <summary>
    /// What land relief is multiplied by on this map — <see cref="MapScale"/> when
    /// <see cref="ScaleReliefWithMapSize"/> is on, 1 when it is off.
    ///
    /// The single definition, and it has to stay that way. Anything authored as a *gradient*
    /// travels with this, not with <see cref="MapScale"/>: compressing heights multiplies every
    /// slope by the same factor, so a threshold in elevation-per-pixel means something different
    /// on every map until it is scaled here too. <see cref="MinPhysicalSlope"/> is the one that
    /// bites; <see cref="MapGen.HeightmapNormalizer.CompressRelief"/> is what applies it.
    /// </summary>
    [Browsable(false)]
    public double ReliefScale => ScaleReliefWithMapSize ? MapScale : 1.0;

    /// <summary>Target area of one land province on *this* map, in province pixels.</summary>
    [Browsable(false)]
    public double BaronyPixels => BaronyPixelsAtVanilla * CountyScale * CountyScale;

    /// <summary>Target area of one sea zone on this map, in province pixels.</summary>
    [Browsable(false)]
    public double SeaZonePixels => SeaZonePixelsAtVanilla * CountyScale * CountyScale;

    [Browsable(false)]
    public Limits Limits { get; } = new();


    // =========================================================================
    // CustomTypeDescriptor Overrides (for PropertyGrid Filtering)
    // =========================================================================

    /// <summary>
    /// Hands the base descriptor a real parent, so every member we do not override below
    /// (attributes, converter, editor, events) still answers for this type instead of returning
    /// the empty defaults a parentless <see cref="CustomTypeDescriptor"/> gives.
    /// </summary>
    public MapConfig()
        : base(TypeDescriptor.GetProvider(typeof(MapConfig)).GetTypeDescriptor(typeof(MapConfig)))
    {
    }

    /// <summary>
    /// The object the grid reads and writes property values on. The parentless base returns null
    /// here, which is what leaves every row blank and uneditable.
    /// </summary>
    public override object GetPropertyOwner(PropertyDescriptor? pd) => this;

    public override PropertyDescriptorCollection GetProperties()
        => GetProperties(null);

    public override PropertyDescriptorCollection GetProperties(Attribute[]? attributes)
    {
        // Get all default properties
        var baseProps = TypeDescriptor.GetProperties(this, attributes, true);

        bool hideAdvanced = !ShowAdvancedSettings;
        bool imported = !string.IsNullOrWhiteSpace(AzgaarJsonPath);

        var shown = new List<PropertyDescriptor>();
        foreach (PropertyDescriptor property in baseProps)
        {
            if (property.Attributes[typeof(HideInGeneratorAttribute)] is not null) continue;
            if (hideAdvanced && property.Attributes[typeof(AdvancedSettingAttribute)] is not null) continue;

            // Only a vanilla world has a region of vanilla's map to choose.
            if (property.Name == nameof(VanillaRegion) && ContentSource != ContentSourceMode.VanillaWorld) continue;

            // The mix's shares mean nothing until the mix is switched on.
            if (!CustomRaceMix && IsRaceMixRow(property.Name)) continue;

            shown.Add(imported && property.Attributes[typeof(AzgaarIncompatAttribute)]
                          is AzgaarIncompatAttribute incompat
                ? new AzgaarOverridden(property, incompat)
                : property);
        }

        return new PropertyDescriptorCollection([.. shown]);
    }

    /// <summary>
    /// One row of the grid while an export is loaded, wearing the reason the export decides it.
    ///
    /// A wrapper rather than a second <see cref="DescriptionAttribute"/> because an attribute
    /// appended to a descriptor does not replace the one already on the property —
    /// <see cref="AttributeCollection"/> answers with the first of a type it finds, so the original
    /// description would have won and the note never appeared. <see cref="Description"/> is what the
    /// grid's help pane reads, so overriding it is both the shortest route and the only one that
    /// cannot lose a race with the attribute the property already carries.
    /// </summary>
    private sealed class AzgaarOverridden(PropertyDescriptor inner, AzgaarIncompatAttribute incompat)
        : PropertyDescriptor(inner)
    {
        /// <summary>
        /// The property's own description with the reason after it, so the row still explains what
        /// the setting *is* before explaining who is deciding it. Prefixed rather than merely
        /// appended so the note survives a help pane too short to show the whole thing.
        /// </summary>
        public override string Description
            => $"[Azgaar export] {incompat.Reason}  ---  {inner.Description}";

        /// <summary>
        /// Read-only only for the settings the export truly takes over, which is also what greys the
        /// row. The value is still shown, because the export deciding it does not stop the fallback
        /// paths from reading it, and a user comparing a run against its settings needs to see it.
        /// </summary>
        public override bool IsReadOnly => incompat.Overridden || inner.IsReadOnly;

        public override Type ComponentType => inner.ComponentType;
        public override Type PropertyType => inner.PropertyType;
        public override bool CanResetValue(object component) => inner.CanResetValue(component);
        public override object? GetValue(object? component) => inner.GetValue(component);
        public override void ResetValue(object component) => inner.ResetValue(component);
        public override void SetValue(object? component, object? value) => inner.SetValue(component, value);
        public override bool ShouldSerializeValue(object component) => inner.ShouldSerializeValue(component);
    }
}

/// <summary>
/// How <see cref="MapGen.HeightmapNormalizer"/> rescales an imported heightmap.
///
/// The two anchors are independent decisions and welding them into one switch made the setting
/// scarier than it needed to be. The *bottom* anchor is what fixes a shore cliff; the *top* anchor
/// is what decides how dramatic the result reads. Shift moves the bottom and leaves the top alone,
/// Stretch does both.
/// </summary>
public enum HeightmapNormalization : byte
{
    /// <summary>The source is already on CK3's scale. Returns it untouched, bit for bit.</summary>
    Off,

    /// <summary>
    /// Subtract the offset between the detected land floor and the lowest land CK3 will render dry.
    ///
    /// Relief is preserved exactly 1:1 — nothing is scaled, so no slope is exaggerated and nothing
    /// clips at the top. For a source whose terrain is already shaped correctly and only sits too
    /// high, this is the honest conversion. It is not the default because it cannot recover a
    /// source that is also *compressed*: on the playtest map it landed p50 24 and a highest pixel
    /// of 147/255 against vanilla's 36 and 191, i.e. correct at the shore and flat everywhere else.
    /// </summary>
    Shift,

    /// <summary>
    /// Map the detected land floor and <see cref="MapConfig.LandTopPercentile"/> affinely onto the
    /// lowest dry value and <see cref="MapConfig.LandTop"/>.
    ///
    /// The default, because it is the one that reaches vanilla: measured on the playtest map it
    /// gives land percentiles 25 / 39 / 74 / 148 / 191 against vanilla's 36 / 57 / 87 / 143 / 191,
    /// where Shift stops at 147. It can exaggerate — a source with a narrow land band is amplified
    /// by whatever it takes to fill the range — so the amplification factor is printed on every
    /// run. 1.40x on that map; watch for anything much above 2.
    /// </summary>
    Stretch,
}

/// <summary>Port of the <c>limits</c> global.</summary>
public sealed class Limits
{
    public Range Hills = new(205, 255);
    public MountainRange Mountains = new(255, 510, 450);

    /// <summary>Sea level. Note the comment in the JS: elevation is halved when written to the heightmap.</summary>
    public int SeaLevelUpper = 36;

    public readonly record struct Range(int Lower, int Upper);
    public readonly record struct MountainRange(int Lower, int Upper, int SnowLine);
}
