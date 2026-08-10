using System.ComponentModel;

namespace Ck3MapGen.Config;

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
public sealed class MapConfig
{
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

    /// <summary>Seed for every random decision.</summary>
    [Category("01 General")]
    [Description("Seed for every random decision.")]
    public int Seed { get; set; } = 1;


    /// <summary>
    /// Catchment, in province cells, above which a watercourse is drawn as a river. Absolute
    /// rather than a share of the map: a cell is the same area at every map size, so this is a
    /// fixed catchment in square kilometres.
    /// </summary>
    [Category("10 Rivers and lakes")]
    [Description("Catchment, in province cells, above which a watercourse is drawn as a river. Absolute rather than a share of the map, so the same stream is a river on any size map.")]
    public double RiverMinCatchmentCells { get; set; } = 900;

    // River geometry is authored in vanilla province pixels and scaled by MapScale, so a river is
    // the same fraction of a continent at every map size rather than nine times wider at `tiny`.

    /// <summary>Shortest course kept.</summary>
    [Category("10 Rivers and lakes")]
    [Description("Shortest course kept.")]
    public double MinRiverPixelsAtVanilla { get; set; } = 30;

    /// <summary>Douglas-Peucker tolerance. This is what removes the D8 staircase.</summary>
    [Category("10 Rivers and lakes")]
    [Description("Douglas-Peucker tolerance. This is what removes the D8 staircase.")]
    public double RiverSimplifyAtVanilla { get; set; } = 1.6;

    /// <summary>Largest perpendicular meander offset.</summary>
    [Category("10 Rivers and lakes")]
    [Description("Largest perpendicular meander offset.")]
    public double MeanderPixelsAtVanilla { get; set; } = 2.5;

    // No longer scaled by map size: a river is the same width and wanders the same distance
    // whatever size map it is on, because a pixel is the same distance on all of them.
    public int TerraMinRiverCells => Math.Max(8, (int)MinRiverPixelsAtVanilla);
    public double TerraRiverSimplify => Math.Max(0.6, RiverSimplifyAtVanilla);
    public double TerraMeanderPixels => Math.Max(0.5, MeanderPixelsAtVanilla);


    /// <summary>How deep a filled depression must be to count as a lake.</summary>
    [Category("10 Rivers and lakes")]
    [Description("How deep a filled depression must be to count as a lake.")]
    public float LakeDepth { get; set; } = 0.0015f;
    [Category("10 Rivers and lakes")]
    public int MinLakeCells { get; set; } = 400;

    /// <summary>
    /// Share of land put above the mountain line. Vanilla's own heightmap has 3.3% of its land in
    /// the 121-170 band, and that is the number this reproduces.
    /// </summary>
    [Category("11 Height scale")]
    [Description("Share of land put above the mountain line. Vanilla's own heightmap has 3.3% of its land in the 121-170 band, and that is the number this reproduces.")]
    public double MountainLineShare { get; set; } = 0.035;
    [Category("11 Height scale")]
    public int PeakElevation { get; set; } = 520;
    [Category("11 Height scale")]
    public int SeaFloorElevation { get; set; } = -250;

    /// <summary>
    /// Where the equator line sits, as a fraction of map height. Everything about climate is
    /// measured as distance from it, so this slides every band up or down the map together.
    ///
    /// ck2rpg's 0.9 is deliberately off-centre: it puts the equator near the bottom edge, so a map
    /// is mostly one hemisphere and the cold band only appears at the top. 0.5 centres it and gives
    /// a symmetric world with tropics through the middle and cold at both edges.
    /// </summary>
    [Category("12 Climate")]
    [Description("Where the equator sits, as a fraction of map height. Slides every climate band up or down together. 0.9 (ck2rpg's) puts it near the bottom edge so the map is mostly one hemisphere; 0.5 centres it and gives cold at both edges.")]
    public double EquatorPosition { get; set; } = 0.9;

    /// <summary>settings.equator — in raster space.</summary>
    public double Equator => Height * Math.Clamp(EquatorPosition, 0.0, 1.0);

    /// <summary>settings.pixelSize — raster pixels per simulation cell.</summary>
    public double PixelSize => (double)Height / WorldHeight;

    // --- Province map. CK3's provinces.png and rivers.png are half the heightmap resolution
    // (vanilla: heightmap 18432x9216, provinces 9216x4608). ---
    //
    // Half resolution is right, and it was checked: the shapes in game not matching this raster is
    // not a resolution problem. CK3 draws land at *county* level, so the player sees the union of
    // a county's baronies and never a barony on its own — which is why sea zones and impassable
    // mountains, the two kinds with no county above them, are the two kinds that do line up.
    // The ragged shapes come from Titles.Cluster, not from here.
    public int ProvinceWidth => Width / 2;
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
    public double MapScale => (double)ProvinceWidth / ReferenceProvinceWidth;

    /// <summary>Scales a length authored in vanilla province pixels onto this map.</summary>
    public double Scaled(double vanillaPixels) => vanillaPixels * MapScale;

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

    /// <summary>Target area of one land province on *this* map, in province pixels.</summary>
    public double BaronyPixels => BaronyPixelsAtVanilla * CountyScale * CountyScale;

    /// <summary>Target area of one sea zone on this map, in province pixels.</summary>
    public double SeaZonePixels => SeaZonePixelsAtVanilla * CountyScale * CountyScale;


    // --- Development ---
    //
    // Vanilla's own 867 start, measured over the 3,827 counties that set one: mass between 0 and
    // 16, median near 8, and a thin tail to 60. A handful of Constantinoples above a great many
    // backwaters. The defaults here aim at that shape rather than at a flat spread.

    /// <summary>Development every county gets before terrain is considered.</summary>
    [Category("13 Development")]
    [Description("Development every county gets regardless of its terrain — the floor for the poorest backwater.")]
    public int DevelopmentBase { get; set; } = 0;

    /// <summary>How much development the very best terrain adds on top of the base.</summary>
    [Category("13 Development")]
    [Description("How much development the best possible terrain adds on top of the base. Vanilla's 867 median is about 8 and its mass runs to 16.")]
    public int DevelopmentSpread { get; set; } = 22;

    /// <summary>Overall multiplier on the terrain-derived part. The quick 'richer/poorer' dial.</summary>
    [Category("13 Development")]
    [Description("Overall multiplier on the terrain-derived development. The quick dial for a richer or poorer world without changing how it is distributed.")]
    public double DevelopmentScale { get; set; } = 1.0;

    /// <summary>
    /// How sharply development concentrates on the best land. 1 spreads it evenly across the
    /// ranked counties; higher makes rich counties rarer. 1.5 reproduces vanilla's 867 shape:
    /// median about 8, p90 about 19.
    /// </summary>
    [Category("13 Development")]
    [Description("How sharply development concentrates on the best land. Counties are ranked against each other, so 1 is an even spread and higher makes rich counties rarer. 1.5 reproduces vanilla 867: median about 8, p90 about 19.")]
    public double DevelopmentSkew { get; set; } = 1.5;

    /// <summary>Added to a county's terrain score if any of its baronies reaches the sea.</summary>
    [Category("13 Development")]
    [Description("Added to a county's terrain score if any of its baronies reaches the sea, because a coast is a road when roads are bad.")]
    public double DevelopmentCoastBonus { get; set; } = 0.12;


    /// <summary>
    /// How wide a stretch of water a kingdom or empire may still reach across, in *vanilla*
    /// province pixels. Counties and duchies ignore this and stay on one landmass.
    ///
    /// Measured against vanilla's 9216x4608 province map: the Strait of Dover is about 30 px, the
    /// Irish Sea about 90, the Sicilian narrows about 25, and the Aegean crossings 40-120. The
    /// default therefore reaches the seas real medieval realms actually spanned without letting a
    /// kingdom claim another continent.
    /// </summary>
    [Category("04 Titles")]
    [Description("How wide a stretch of water a kingdom or empire may reach across, in vanilla province pixels. Counties and duchies always stay on one landmass. Vanilla reference: Dover about 30 px, the Irish Sea about 90.")]
    public double SeaBridgePixelsAtVanilla { get; set; } = 110;


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
    [Category("04 Titles")]
    [Description("Fewest children a duchy, kingdom or empire may have and still exist. 2 stops one-province islands from founding a duchy, a kingdom and an empire on the way up. Raising it much past 2 cascades and collapses the hierarchy.")]
    public int MinChildrenPerTitle { get; set; } = 2;

    /// <summary>
    /// Largest a fused impassable mountain range may get, in baronies' worth of area. 0 disables
    /// fusing and leaves every impassable province separate, as vanilla does.
    /// </summary>
    [Category("03 Provinces")]
    [Description("Largest a fused impassable mountain range may get, measured in baronies' worth of area. Touching impassable provinces are merged so a range reads as one wall of rock instead of a scatter of provinces; 0 leaves them separate, as vanilla does.")]
    public double ImpassableRangeMaxBaronies { get; set; } = 8;

    /// <summary>
    /// How deep inside its province a holding, army or siege model must stand, as a fraction of
    /// the deepest point that province has. 0 lets a model sit on the border; 1 pins it to the
    /// single deepest pixel and leaves flatness no say.
    /// </summary>
    [Category("04 Titles")]
    [Description("How deep inside its province a holding or army model must stand, as a fraction of that province's deepest point. Raising it keeps models further from coastlines; lowering it lets flatness matter more than position.")]
    public double LocatorInteriorFraction { get; set; } = 0.6;

    /// <summary>
    /// How much a model prefers the middle of its province over flat ground. Measured against the
    /// map's median slope, so 1 means being a province-radius off centre costs as much as standing
    /// on ground one median slope steeper.
    /// </summary>
    [Category("04 Titles")]
    [Description("How much a holding prefers the middle of its province over flat ground. 0 puts it on the flattest eligible pixel wherever that is; higher pulls it toward the centre even if the ground there is steeper.")]
    public double LocatorCentroidPull { get; set; } = 0.75;


    // --- Rulers ---
    //
    // The de jure hierarchy exists from the moment the titles are drawn, but that is a map of
    // claims rather than of power. These decide how much of it anybody is actually wearing in 867,
    // which is what turns several hundred equal counts into a world with great powers in it.

    /// <summary>Share of duchies whose title is held by somebody at the start date.</summary>
    [Category("15 Rulers")]
    [Description("Share of duchies actually held by a duke at the start date. The rest of their counties stand as independent counts or answer to a king directly.")]
    public double DuchyTitleShare { get; set; } = 0.5;

    /// <summary>Share of kingdoms whose title is held by somebody at the start date.</summary>
    [Category("15 Rulers")]
    [Description("Share of kingdoms actually held by a king at the start date. Realising one also realises its strongest duchy, so a king is always a duke and a count as well.")]
    public double KingdomTitleShare { get; set; } = 0.25;

    /// <summary>
    /// Share of empires whose title is held by somebody. Kept low deliberately: an emperor in 867
    /// should be a rarity the map is built around, not a tier everyone occupies.
    /// </summary>
    [Category("15 Rulers")]
    [Description("Share of empires actually held by an emperor at the start date. Kept low on purpose — an emperor should be a rarity the map is built around.")]
    public double EmpireTitleShare { get; set; } = 0.15;


    // --- Cultures and faiths ---
    //
    // Vanilla's own proportions, for calibration: ~193 cultures in ~40 heritages, and ~120 faiths
    // in ~48 religions, over 3,827 counties. That is roughly one culture per 20 counties, five
    // cultures to a heritage, and a religious map a little coarser than the cultural one.

    /// <summary>Counties a generated culture covers on average. Lower makes a more fragmented world.</summary>
    [Category("14 Cultures and faiths")]
    [Description("Counties per generated culture. Vanilla averages about 20; lower values make a more fragmented, more polyglot world.")]
    public double CountiesPerCulture { get; set; } = 18;

    /// <summary>
    /// Cultures sharing one heritage and one language. This is what decides how related neighbours
    /// are: CK3's acceptance, hybridisation and divergence all key off shared heritage, so a world
    /// of one-culture heritages is a world where nobody can ever get along with anybody.
    /// </summary>
    [Category("14 Cultures and faiths")]
    [Description("Cultures sharing one heritage and language. Higher values give large related families like vanilla's Frankish or North Germanic groups; 1 gives a world where no two cultures are relatives.")]
    public double CulturesPerHeritage { get; set; } = 4;

    /// <summary>
    /// How strongly culture borders follow the ground. 0 ignores terrain entirely and gives a plain
    /// voronoi; 1 uses the authored crossing costs as written.
    /// </summary>
    [Category("14 Cultures and faiths")]
    [Description("How strongly culture borders follow terrain. 0 ignores the ground and cuts straight over mountains; 1 makes ranges and deserts into language barriers.")]
    public double CultureTerrainWeight { get; set; } = 1.0;

    /// <summary>Counties a generated faith covers on average. Coarser than cultures, as in vanilla.</summary>
    [Category("14 Cultures and faiths")]
    [Description("Counties per generated faith. Deliberately coarser than cultures — vanilla runs about 120 faiths against 193 cultures.")]
    public double CountiesPerFaith { get; set; } = 26;

    /// <summary>Faiths sharing one religion, and therefore its doctrines and its gods.</summary>
    [Category("14 Cultures and faiths")]
    [Description("Faiths sharing one religion. Faiths of a religion are heresies of each other: same gods, different doctrine, and a much smaller penalty for converting between them.")]
    public double FaithsPerReligion { get; set; } = 2.5;

    /// <summary>
    /// The faith equivalent of <see cref="CultureTerrainWeight"/>, and deliberately lower. If the
    /// two matched, faith borders would land on culture borders and the map would have only one
    /// axis of difference on it.
    /// </summary>
    [Category("14 Cultures and faiths")]
    [Description("How strongly faith borders follow terrain. Kept below the culture weight on purpose: matching them puts every faith border on a culture border, and the interesting map is the one where they disagree.")]
    public double FaithTerrainWeight { get; set; } = 0.45;

    /// <summary>Holy sites each faith declares, placed on its highest-development counties.</summary>
    [Category("14 Cultures and faiths")]
    [Description("Holy sites per generated faith, placed on its richest counties. Vanilla faiths carry five.")]
    public int HolySitesPerFaith { get; set; } = 5;

    /// <summary>
    /// Smallest allowed province in pixels. Below this CK3 cannot derive borders, a centroid or
    /// locator positions and crashes in geometry code without logging anything.
    /// </summary>
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
    [Category("03 Provinces")]
    [Description("Rounds of Lloyd relaxation on the province seeds — move each seed to the middle of the province it grew, then grow them all again. Turns a voronoi diagram into a centroidal one, which is what stops provinces being squeezed to a waist between lopsided neighbours. Each round costs a full repartition, the slowest step in the tool.")]
    public int ProvinceRelaxIterations { get; set; } = 1;

    /// <summary>
    /// Share of land provinces declared impassable_mountains. Vanilla's ratio is 1,188 impassable
    /// against 11,301 baronied. Impassable provinces get no barony and no holder.
    /// </summary>
    [Category("03 Provinces")]
    [Description("Share of land provinces declared impassable_mountains, which get no barony and no holder. Vanilla runs 1,188 impassable against 11,301 baronied.")]
    public double ImpassableShareOfLand { get; set; } = 0.095;

    /// <summary>
    /// How much of a province must stand above the mountain line before it may be impassable. Stops
    /// a map with little high ground being given impassable provinces just to hit the target count.
    /// </summary>
    [Category("03 Provinces")]
    [Description("How much of a province must stand above the mountain line before it may be impassable. Stops a flat map being given impassable provinces just to hit the target count.")]
    public double ImpassableMinMountainShare { get; set; } = 0.45;
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
    [Category("03 Provinces")]
    [Description("Rows and columns of forced ocean around the edge of the province map, in province pixels. Vanilla has water along every edge — its top and bottom rows are entirely sea, and its province map has only a handful of large ocean provinces touching them. A generated map happily runs land off the poles instead: on seed 1 at vanilla size, 33 land provinces touched the top edge and 17 the bottom. A prov...")]
    public int OceanBorder { get; set; } = 1;

    // --- Climate ---
    //
    // Climate is a physical model now, not a set of latitude bands: MapGen.ClimateModel advects
    // moisture along the surface winds of a three-cell circulation and MapGen.Koppen classifies the
    // temperature and rainfall that come out. The settings below are that model's parameters, and
    // they are deliberately in real units - degrees Celsius, millimetres of rain, degrees of
    // latitude - so they can be checked against a real climate atlas rather than tuned blind.
    //
    // What went: TropicalWidthScale, SubTropicalWidthScale, TemperateWidthScale, ClimateBandScale,
    // ClimateWanderPixels and ClimateLapsePixels. All six described a band edge that was a function
    // of y, which is exactly what made the old maps come out in stripes. ClimateBandScale in
    // particular had a failure mode worth not repeating: the widths were authored in raster pixels
    // against vanilla's 18432-wide map, so on any smaller map a single band could be wider than the
    // whole map, and then no climate setting changed anything at all. MapLatitudeSpan has no such
    // mode - a map is however much of a world its author says it is, at any resolution.

    /// <summary>
    /// How many degrees of latitude the map covers, top edge to bottom edge. With
    /// <see cref="EquatorPosition"/> this is the entire mapping from pixels to latitude, and
    /// therefore the only control over how many climate zones the map crosses.
    /// </summary>
    [Category("12 Climate")]
    [Description("How many degrees of latitude the map covers from top edge to bottom edge. With the equator position this is the whole mapping from pixels to latitude, so it decides how many climate zones the map crosses. 80 degrees with the equator near the bottom is roughly the sweep of vanilla's map.")]
    public double MapLatitudeSpan { get; set; } = 80;

    /// <summary>Annual mean temperature at sea level on the equator. Earth's is about 26.</summary>
    [Category("12 Climate")]
    [Description("Annual mean temperature at sea level on the equator, in Celsius. Earth's is about 26. Raising this and the pole figure together is how to make a hotter world.")]
    public double EquatorTemperatureC { get; set; } = 26;

    /// <summary>Annual mean temperature at sea level at the pole. Earth's northern one is about -20.</summary>
    [Category("12 Climate")]
    [Description("Annual mean temperature at sea level at the pole, in Celsius. Earth's northern one is about -20. Bringing it closer to the equator figure gives a flatter, more uniform world with far less tundra and taiga.")]
    public double PoleTemperatureC { get; set; } = -20;

    /// <summary>
    /// How far the warmest and coldest months sit either side of the annual mean at high latitude.
    /// This decides where Koppen's C/D boundary falls, and therefore where oceanic forest gives way
    /// to continental forest and taiga.
    /// </summary>
    [Category("12 Climate")]
    [Description("How far apart the warmest and coldest months are at high latitude, in Celsius. Decides where temperate gives way to continental and taiga, because Koppen splits those on the coldest month rather than on the average.")]
    public double SeasonalRangeC { get; set; } = 44;

    /// <summary>
    /// How far inland the sea keeps moderating the seasons, in vanilla province pixels. Inside it a
    /// coast has mild winters; past it a continental interior swings freely.
    /// </summary>
    [Category("12 Climate")]
    [Description("How far inland the sea goes on moderating the seasons, in vanilla province pixels. Inside it a coast has mild winters and cool summers; past it an interior swings freely. This is what separates an oceanic climate from a continental one at the same latitude.")]
    public double ContinentalityPixels { get; set; } = 900;

    /// <summary>
    /// Warmth and cold that latitude does not explain, in degrees. On Earth this is what ocean
    /// currents do - Norway and Labrador share a latitude and not a climate. Without it every
    /// isotherm on the map is a parallel.
    /// </summary>
    [Category("12 Climate")]
    [Description("Warmth and cold that latitude cannot explain, in Celsius - what ocean currents do on Earth, where Norway and Labrador share a latitude and not a climate. 0 makes every isotherm a parallel, which is half of what makes a climate map look ruled.")]
    public double TemperatureDriftC { get; set; } = 2;

    /// <summary>
    /// Height of the map's highest land in metres. A heightmap carries no absolute scale, so this is
    /// what gives it one - and the lapse rate needs a real one or a mountain cannot be given a real
    /// temperature.
    /// </summary>
    [Category("12 Climate")]
    [Description("Height of the map's highest land in metres. A heightmap carries no absolute scale and the lapse rate needs one, so this is what decides how cold the mountains are. 4500 makes the tallest peak roughly alpine.")]
    public double PeakElevationMetres { get; set; } = 4000;

    /// <summary>
    /// Yearly rainfall on the middle of the map's land, in millimetres. The model's own output has
    /// no units, so this is what puts it on a scale Koppen's thresholds can be tested against.
    /// Earth's land median is around 650. The median rather than the mean because rainfall is
    /// heavily right-skewed and the mean sits far above ordinary ground.
    /// </summary>
    [Category("12 Climate")]
    [Description("Yearly rainfall on the middle of the map's land, in millimetres - Earth's is around 650. The circulation model has no units of its own, so this is what puts it on a scale Koppen can test. It scales without flattening the spread, so a dry world stays dry relative to itself.")]
    public double MedianRainfallMm { get; set; } = 1450;

    /// <summary>
    /// Share of its remaining water an air parcel rains out per 100 vanilla province pixels of land
    /// it crosses. The continental-interior dial: higher dries the far side of a landmass faster,
    /// lower carries rain further inland.
    /// </summary>
    [Category("12 Climate")]
    [Description("Share of its water an air parcel rains out per 100 vanilla province pixels of land it crosses. The continental-interior dial: higher leaves the far side of a landmass a desert, lower carries rain all the way across it.")]
    public double RainoutPer100Pixels { get; set; } = 0.05;

    /// <summary>
    /// Extra rain a climbing air parcel drops per kilometre it is lifted. The rain shadow behind a
    /// range exists without this - cooling alone squeezes the water out - but this sharpens the
    /// contrast between the windward and leeward sides.
    /// </summary>
    [Category("12 Climate")]
    [Description("Extra rain an air parcel drops per kilometre it is lifted over a range. A rain shadow forms without this, because cooling alone squeezes the water out, but raising it sharpens the contrast between a soaking windward slope and a desert behind it.")]
    public double OrographicRainStrength { get; set; } = 0.8;

    /// <summary>
    /// How strongly the circulation's rising and sinking branches drive rainfall. This is what puts
    /// the wet belt on the equator and the great deserts at 30 degrees; at 0 the subtropical deserts
    /// largely disappear.
    /// </summary>
    [Category("12 Climate")]
    [Description("How strongly the rising and sinking branches of the circulation drive rainfall. This is what puts the wet belt on the equator and the great deserts at 30 degrees; at 0 the subtropical deserts largely disappear.")]
    public double ConvectiveRainStrength { get; set; } = 1.3;

    public Limits Limits { get; } = new();

}

/// <summary>Port of the <c>limits</c> global.</summary>
public sealed class Limits
{
    public Range PineTree = new(10, 255);
    public Range Hills = new(205, 255);
    public MountainRange Mountains = new(255, 510, 450);
    public int RaindropsLower = 600;

    /// <summary>Sea level. Note the comment in the JS: elevation is halved when written to the heightmap.</summary>
    public int SeaLevelUpper = 36;

    public readonly record struct Range(int Lower, int Upper);
    public readonly record struct MountainRange(int Lower, int Upper, int SnowLine);
}
