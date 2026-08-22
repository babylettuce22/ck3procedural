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
public sealed class FollowWorldYearConverter : Int32Converter
{
    public const string Follow = "Follow World Year";

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
                    "Year, which is pinned to this value when an export is loaded. Still read as set " +
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
    [Description("Which year on CK3's own timeline this world is as advanced as: innovations cultures already hold, the development baseline, and the feudal/tribal/nomad mix. Set it to follow the world year to keep the two together, which is how every map worked before this setting existed.")]
    public int EraAnchorYear { get; set; }

    /// <summary>The year every advancement heuristic reads. See <see cref="EraAnchorYear"/>.</summary>
    [Browsable(false)]
    public int EraYear => EraAnchorYear > 0 ? EraAnchorYear : Math.Max(1, StartYear);

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

    [Category("02 World State")]
    [Description("Enable procedural Centers of the World: focal metropolises with monumental wonders, hyper-development, and primary holy sites.")]
    public bool EnableWorldCenters { get; set; } = true;

    [Category("02 World State")]
    [Description("Target number of World Center metropolises across the globe.")]
    public int WorldCentersCount { get; set; } = 5;

    [AdvancedSetting]
    [Category("02 World State")]
    [Description("Minimum spacing between World Centers in approximate county units.")]
    public int MinCenterDistanceCounties { get; set; } = 12;

    [Category("02 World State")]
    [Description("Enable active wars raging at game start between rival rulers, contested holy sites, and disputed borders.")]
    public bool EnableStartingWars { get; set; } = true;

    [Category("02 World State")]
    [Description("Target number of active ongoing wars on Day 1.")]
    public int StartingWarsCount { get; set; } = 3;

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
    /// </summary>
    [Category("03 Provinces")]
    [Description("How large a barony is, relative to vanilla's. 2 makes each one twice as wide and therefore a quarter as numerous; the whole title hierarchy follows, because MapGen.Titles clusters by counts rather than by area. This is the only knob for map granularity. Province counts used to be given directly, which meant a map kept the same number of provinces at every resolution and so a barony at tiny cove...")]
    public double CountyScale { get; set; } = 1.0;

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
    /// weight on the oasis material — the rarest thing it paints — so this gate is tighter still
    /// than the farmland one.
    /// </summary>
    [Category("03 Provinces")]
    [Description("Share of desert provinces that become oases. Only provinces holding a drainage sink — a depression water actually collects in — are eligible, and the wettest of those win. Oasis is vanilla's least-painted material; keep this small.")]
    public double OasisShare { get; set; } = 0.005;

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

    [AdvancedSetting]
    [Category("03 Provinces")]
    [Description("Absolute minimum gradient per pixel required for ground to count as steep. Prevents flat plains from being declared cliffs on low-relief maps.")]
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
    ///
    /// Vanilla has water along every edge — its top and bottom rows are entirely sea, and its
    /// province map has only a handful of large ocean provinces touching them. A generated map
    /// happily runs land off the poles instead: on seed 1 at vanilla size, 33 land provinces
    /// touched the top edge and 17 the bottom. A province clipped by the map boundary has an
    /// open border, which is the sort of thing a boundary-following walk cannot close.
    /// </summary>
    [AdvancedSetting]
    [Category("03 Provinces")]
    [Description("Rows and columns of forced ocean around the edge of the province map, in province pixels. Vanilla has water along every edge — its top and bottom rows are entirely sea, and its province map has only a handful of large ocean provinces touching them. A generated map happily runs land off the poles instead: on seed 1 at vanilla size, 33 land provinces touched the top edge and 17 the bottom. A prov...")]
    public int OceanBorder { get; set; } = 1;


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

    [Category("05 Rivers")]
    [Description("Target number of navigable major river systems across the map.")]
    public int MajorRiverCount { get; set; } = 8;

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
    /// rivers.png wherever the discharge merits one. Lakes that qualify are traced over and above
    /// <see cref="MajorRiverCount"/>: that cap is for rivers the generator chooses, and a lake's
    /// outlet is not a choice.
    /// </summary>
    [AdvancedSetting]
    [Category("05 Rivers")]
    [Description("How big a lake must be, counted in sea zones, before a navigable river is carved from it to the sea. Lakes that qualify come on top of the major river count. Lower to connect smaller lakes; raise to leave them to tributaries in rivers.png.")]
    public double LakeOutletMinSeaZones { get; set; } = 0.1;

    /// <summary>
    /// Half-width of a major river's carved channel at its source, in vanilla *heightmap* pixels.
    ///
    /// A radius, measured perpendicular from the centreline — the carve tests
    /// <c>dist &lt;= curChanR</c> — so the channel is twice this across.
    ///
    /// Floored at 7 in the carve regardless of scale. Navigability depends on the channel surviving
    /// the 2:1 downsample into the province raster, which calls a cell water only when three of its
    /// four pixels are under sea level, so a thinner channel stops reading as water at province
    /// resolution and the navigable province chain breaks.
    /// </summary>
    [AdvancedSetting]
    [Category("05 Rivers")]
    [Description("Half-width of a major river's carved channel at its source, in heightmap pixels measured from the centreline — the channel is twice this across. Below about 7 it stops surviving the downsample into the province map and the river ceases to be navigable.")]
    public double RiverChannelRadiusMin { get; set; } = 9.0;

    /// <summary>
    /// Half-width of a major river's carved channel at its mouth, in vanilla heightmap pixels.
    /// Same radius convention as <see cref="RiverChannelRadiusMin"/>.
    /// </summary>
    [AdvancedSetting]
    [Category("05 Rivers")]
    [Description("Half-width of a major river's carved channel at its mouth, in heightmap pixels from the centreline. The channel opens from the source radius to this along its length.")]
    public double RiverChannelRadiusMax { get; set; } = 14.0;

    /// <summary>
    /// How much a major river's channel breathes in and out along its length, as a fraction of the
    /// radius it would otherwise have. 0.25 lets it run between three quarters and five quarters of
    /// its nominal width.
    ///
    /// Without this the channel opens on a fixed curve from source to mouth and never does anything
    /// else, which is the one thing a real river never does. The variation is low-frequency — see
    /// <see cref="RiverWidthVariationScale"/> — because per-vertex jitter reads as a ragged edge
    /// rather than as narrows and broads.
    ///
    /// The valley follows the channel, so a wide reach gets a wide flood plain and a narrow one a
    /// gorge. Never allowed to pinch the channel below the navigable floor, except at the head where
    /// the taper is deliberately closing it.
    /// </summary>
    [Category("05 Rivers")]
    [Description("How much a major river widens and narrows along its length, as a fraction of its nominal width. 0 gives a channel that only ever opens from source to mouth; 0.25 lets it run between three quarters and five quarters of that. The valley follows, so wide reaches get flood plains and narrow ones get gorges.")]
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
    /// How far the carved valley reaches beyond the channel itself, as a multiple of channel width.
    /// The channel is the water; this is the shoulder of lower ground either side of it.
    /// </summary>
    [AdvancedSetting]
    [Category("05 Rivers")]
    [Description("How far a major river's valley shoulders reach beyond the water itself, as a multiple of channel width. Higher carves a broad flood plain; 1 leaves the river in a trench with no valley around it.")]
    public double RiverValleyReach { get; set; } = 4.0;

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
    /// Whether land relief shrinks with map size, so slopes come out the same in *world units* at
    /// any resolution.
    ///
    /// CK3 takes the world's width from the province map — a 4608-wide map is 4607 world units —
    /// but its height is `WORLD_EXTENTS_Y = 50` on every map, and that has to stay constant
    /// (`WATERLEVEL = 3` is pinned to it at 3/50 = 0.06, which is what puts the waterline on
    /// 19/255; see <see cref="Emit.CompatibilityWriter"/>). Because a smaller map resamples the
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
    [AdvancedSetting]
    [Category("7 Height scale")]
    [Description("Shrinks land relief in proportion to map size so slopes match vanilla's in world units at any resolution. CK3 fixes world *height* at 50 units on every map but takes world *width* from the province map, and a smaller map resamples the same world into fewer pixels — so without this, a half-width map has exactly twice vanilla's gradient everywhere. Steep terrain is what makes trees and province borders float: the engine's distance LOD cannot follow slopes that steep, so the terrain it draws sits below the full-resolution heightmap props are snapped to. Land is compressed toward the waterline, so sea level, water depths and the land/water split are untouched. Identity at vanilla map size. Turn off to keep the source heightmap's relief exactly as authored, and expect proportionally more floating the smaller you generate.")]
    public bool ScaleReliefWithMapSize { get; set; } = true;

    /// <summary>
    /// What the highest land pixel becomes after normalisation, on the 0-255 scale.
    ///
    /// 191 is vanilla's own highest land pixel. Vanilla does not use the top of the byte range —
    /// its land sits at p50 36 and runs out well short of 255 — so stretching an imported map onto
    /// all of it produces terrain markedly more dramatic than anything in the base game. Raise it
    /// towards 255 for a deliberately alpine map; that is the knob that decides how flat the
    /// result reads.
    /// </summary>
    [AdvancedSetting]
    [Category("7 Height scale")]
    [Description("What the highest land pixel becomes, on the 0-255 scale. 191 is vanilla's own highest; vanilla never uses the top of the range. Raise towards 255 for a more dramatic map — this is the knob that decides how flat the result reads.")]
    public double LandTop { get; set; } = 191;

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
    [Description("Added to a county's terrain score if any of its baronies reaches the sea, because a coast is a road when roads are bad.")]
    public double DevelopmentCoastBonus { get; set; } = 0.12;

    [Category("9 Development")]
    [Description("Bonus development granted to World Center metropolises.")]
    public int WorldCenterDevBoost { get; set; } = 32;


    // =========================================================================
    // 14 Cultures and faiths
    // =========================================================================

    /// <summary>Counties a generated culture covers on average. Lower makes a more fragmented world.</summary>
    [Category("10 Cultures and faiths")]
    [Description("Counties per generated culture. Vanilla averages about 20; lower values make a more fragmented, more polyglot world.")]
    public double CountiesPerCulture { get; set; } = 18;

    /// <summary>
    /// Cultures sharing one heritage and one language. This is what decides how related neighbours
    /// are: CK3's acceptance, hybridisation and divergence all key off shared heritage, so a world
    /// of one-culture heritages is a world where nobody can ever get along with anybody.
    /// </summary>
    [Category("10 Cultures and faiths")]
    [Description("Cultures sharing one heritage and language. Higher values give large related families like vanilla's Frankish or North Germanic groups; 1 gives a world where no two cultures are relatives.")]
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

    // Can be way too many nude characters lol
    [Category("10 Cultures and faiths")]
    [Description("Allow generated faiths to roll the Natural Primitivism tenet (which renders character portraits naked).")]
    public bool AllowNaturalPrimitivism { get; set; } = true;

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
    public int AdministrativeMinStartYear { get; set; } = 800;

    [Category("11 Rulers")]
    [Description("Enable Nomadic horde realms across steppes and arid plains (requires nomadic DLC; safely degrades to Tribal/Clan if DLC is absent).")]
    public bool EnableNomadHordes { get; set; } = true;

    [Category("11 Rulers")]
    [AzgaarIncompat("Governments come from each state's own form and type — a horde is one because Azgaar " +
                    "called its state Nomadic or named it a Khanate, not because of the ground it stands " +
                    "on. Enable nomad hordes still applies, and turns those states tribal instead. Read " +
                    "again only for counties no state claims.")]
    [Description("Share of qualifying steppe and arid realms that start as Nomads. Qualifying means a real pastoral majority: a fifth of the realm on steppe, or three fifths on steppe/desert/drylands together, or a steppe capital. Early starts add up to +0.25 to this and late starts subtract up to 0.25.")]
    public double NomadSteppeShare { get; set; } = 0.45;


    // =========================================================================
    // 16 Wilderness
    // =========================================================================

    /// <summary>
    /// Share of counties left unsettled, as a fraction of the whole map.
    ///
    /// A target rather than a guarantee: the clumping pass below discards anything too small to
    /// read as a region, so the delivered share lands a little under this. Zero disables placement
    /// while still shipping the scripts — but with <see cref="EnableWilderness"/> on, at least one
    /// county is always placed, because a wilderness system with no wilderness in it is
    /// indistinguishable from a broken one and there would be nothing to test against.
    /// </summary>
    [Category("12 Wilderness")]
    [AzgaarIncompat("Wilderness is the ground the export left unclaimed, which is a statement rather than " +
                    "the habitability guess this scores. Read again only if the export claims every county.")]
    [Description("Share of counties left as unsettled wilderness. A target, not a guarantee — clumps too small to read as a region are discarded, so the delivered share lands slightly under this.")]
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
                    "the habitability guess this scores. Read again only if the export claims every county.")]
    [Description("Pull wilderness toward the map edges (1), ignore position (0), or pull it inland (-1). Edge-biased reads as a frontier at the rim of the world; inland-biased makes the interior the wasteland.")]
    public double WildernessEdgeBias { get; set; } = 0.75;

    /// <summary>
    /// How strongly hostile ground attracts wilderness, against everything else.
    ///
    /// The other half of the placement score. At 0 wilderness lands wherever the position bias
    /// says regardless of terrain, which produces empty farmland; at 1 it follows the mountains,
    /// ice and marsh and ignores where they are.
    /// </summary>
    [AdvancedSetting]
    [Category("12 Wilderness")]
    [AzgaarIncompat("Wilderness is the ground the export left unclaimed, which is a statement rather than " +
                    "the habitability guess this scores. Read again only if the export claims every county.")]
    [Description("How strongly wilderness follows hostile terrain — mountains, ice, desert, marsh, jungle — against the edge bias. At 0 it ignores terrain entirely and can leave empty farmland.")]
    public double WildernessTerrainWeight { get; set; } = 0.75;

    /// <summary>
    /// Smallest run of connected counties kept as wilderness.
    ///
    /// The whole reason placement is a two-pass affair. Ranking counties by score and taking the
    /// worst N speckles single wild counties through settled land, which reads as a generation
    /// fault rather than as a frontier; growing clumps from seeds and discarding the runts is what
    /// makes the result look deliberate. 1 disables the check and restores the speckle.
    /// </summary>
    [AdvancedSetting]
    [Category("12 Wilderness")]
    [AzgaarIncompat("Wilderness is the ground the export left unclaimed, which is a statement rather than " +
                    "the habitability guess this scores. Read again only if the export claims every county.")]
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
                    "the habitability guess this scores. Read again only if the export claims every county.")]
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

    // HIGHLY WIP //

    public enum FantasyRaceMode
    {
        HumanOnly,      // Realistic human phenotypes adapted to latitude/climate
        LowFantasy,     // Humans dominant (~85%), with rare Elven, Dwarven, or Orcish realms (~15%)
        HighFantasy,    // Balanced distribution of Elves, Dwarves, Humans, Orcs, Beastfolk, Deepkin
        ExoticSurreal   // Wild procedural morphs (exotic skin hues, vibrant hair/eyes, extreme heights)
    }

    [Category("14 Fantasy/Ethnicities")]
    [Description("Enable procedural fantasy racial phenotypes (Elves, Dwarves, Orcs, Giants, Deepkin, etc.).")]
    public bool EnableFantasyEthnicities { get; set; } = false;

    [Category("14 Fantasy/Ethnicities")]
    [Description("Race distribution mode across the generated world.")]
    public FantasyRaceMode RaceMode { get; set; } = FantasyRaceMode.LowFantasy;

    [Category("14 Fantasy/Ethnicities")]
    [Description("Tie race strictly to Heritage (all cultures under a heritage share the same core race with regional variations).")]
    public bool TieRaceToHeritage { get; set; } = true;

    [Category("14 Fantasy/Ethnicities")]
    [Description("How many distinct races the world must contain. Raises the heritage count if culture density would not otherwise produce enough regions to hold them, so a high value costs you smaller heritages. Capped at 8, the number of races that exist. Require terrain can hold the delivered count below this.")]
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
    [Description("When the mode's human:fantasy land ratio leaves no room for every guaranteed race to hold territory of its own, let the overflow races live as small minorities (~13% of characters) inside a well-suited human culture instead of not appearing at all. Minority members look their race but carry the Human trait like the rest of their culture, not their race's trait or bonuses — they are flavour, not a faction. Off means the guarantee simply wins the land and the ratio is sacrificed with a warning.")]
    public bool AllowMinorityRaces { get; set; } = true;


    // =========================================================================
    // Derived Calculations, Geometry, Scales & Resolvers
    // =========================================================================

    /// <summary>settings.equator — in raster space.</summary>
    [Browsable(false)]
    public double Equator => Height * Math.Clamp(EquatorPosition, 0.0, 1.0);

    /// <summary>settings.pixelSize — raster pixels per simulation cell.</summary>
    [Browsable(false)]
    public double PixelSize => (double)Height / WorldHeight;

    // --- Province map. CK3's provinces.png and rivers.png are half the heightmap resolution
    // (vanilla: heightmap 18432x9216, provinces 9216x4608). ---
    //
    // Half resolution is right, and it was checked: the shapes in game not matching this raster is
    // not a resolution problem. CK3 draws land at *county* level, so the player sees the union of
    // a county's baronies and never a barony on its own — which is why sea zones and impassable
    // mountains, the two kinds with no county above them, are the two kinds that do line up.
    // The ragged shapes come from Titles.Cluster, not from here.
    [Browsable(false)]
    public int ProvinceWidth => Width / 2;

    [Browsable(false)]
    public int ProvinceHeight => Height / 2;

    // --- Map scale ---
    //
    // Everything measured in pixels is authored against vanilla's province map and scaled from
    // there, so changing map size resamples the same world rather than changing what is on it.

    /// <summary>Vanilla's province-map width. The scale everything pixel-denominated is authored at.</summary>
    public const int ReferenceProvinceWidth = 9216;

    /// <summary>Vanilla's heightmap width.</summary>
    public const int ReferenceHeightmapWidth = 18432;

    /// <summary>This map's province raster relative to vanilla's, linearly.</summary>
    [Browsable(false)]
    public double MapScale => (double)ProvinceWidth / ReferenceProvinceWidth;

    /// <summary>Scales a length authored in vanilla province pixels onto this map.</summary>
    public double Scaled(double vanillaPixels) => vanillaPixels * MapScale;

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
    public Range PineTree = new(10, 255);
    public Range Hills = new(205, 255);
    public MountainRange Mountains = new(255, 510, 450);

    /// <summary>Sea level. Note the comment in the JS: elevation is halved when written to the heightmap.</summary>
    public int SeaLevelUpper = 36;

    public readonly record struct Range(int Lower, int Upper);
    public readonly record struct MountainRange(int Lower, int Upper, int SnowLine);
}