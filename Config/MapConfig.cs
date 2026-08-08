using System.ComponentModel;

namespace Ck3MapGen.Config;

/// <summary>
/// Port of the <c>settings</c> and <c>limits</c> globals in js/all/initialState.js.
///
/// Two resolutions matter and they are not the same thing:
///   * <see cref="Width"/>/<see cref="Height"/> are the exported raster size (settings.width/height).
///   * <see cref="WorldWidth"/>/<see cref="WorldHeight"/> are the tectonic simulation grid
///     (world.width/height), which ck2rpg runs coarse and upsamples on export.
/// Climate limits are expressed in *raster* space; hotspot radii in *world* space.
/// </summary>
public sealed class MapConfig
{
    // --- Export raster (settings.width / settings.height) ---
    [SettingRole(SettingRole.GenerationOnly)]
    [Category("02 Map size")]
    public int Width { get; set; } = 8192;
    [SettingRole(SettingRole.GenerationOnly)]
    [Category("02 Map size")]
    public int Height { get; set; } = 4096;

    // --- Simulation grid (world.width / world.height) ---
    [SettingRole(SettingRole.Always)]
    [Category("02 Map size")]
    public int WorldWidth { get; set; } = 1024;
    [SettingRole(SettingRole.Always)]
    [Category("02 Map size")]
    public int WorldHeight { get; set; } = 512;

    /// <summary>Seed for every random decision.</summary>
    [SettingRole(SettingRole.Always)]
    [Category("01 General")]
    [Description("Seed for every random decision.")]
    public int Seed { get; set; } = 1;

    /// <summary>
    /// Reshape land heights onto vanilla's measured hypsometric curve instead of stretching them
    /// linearly to whatever the tallest simulated peak happens to be.
    ///
    /// The linear stretch made the map as mountainous as its most extreme accident: measured on
    /// 2026-08-07, it put 18x more land in the top elevation band than vanilla has, and always
    /// drove the highest pixel to 255 where vanilla's tops out at 192. The remap is monotonic, so
    /// it changes only the height scale, never where anything is. Off restores the old behaviour
    /// for bisecting.
    /// </summary>
    [SettingRole(SettingRole.Always)]
    [Category("11 Height scale")]
    [Description("Reshape land heights onto vanilla's measured hypsometric curve instead of stretching them linearly to whatever the tallest simulated peak happens to be, which made the map as mountainous as its most extreme accident. Monotonic: it changes the height scale, never where anything is.")]
    public bool MatchVanillaHypsometry { get; set; } = true;

    /// <summary>Fraction of the map that should end up above sea level.</summary>
    [SettingRole(SettingRole.GenerationOnly)]
    [Category("04 Continents")]
    [Description("Fraction of the map that should end up above sea level.")]
    public double TargetLandFraction { get; set; } = 0.40;


    // --- Terra: the terrain generator (MapGen/Terra) ---
    //
    // Heights inside Terra are normalised to roughly [0, 1] with sea level at TerraSeaLevel, and
    // only converted to the integer scale above on the way out. Lengths are given as a fraction of
    // the map width or as cycles across it, never in pixels, so changing map size resamples the
    // same world instead of generating a different one.

    /// <summary>How much coarser the tectonics and the main erosion run than the heightmap.</summary>
    [SettingRole(SettingRole.GenerationOnly)]
    [Category("02 Map size")]
    [Description("How much coarser the tectonics and the main erosion run than the heightmap.")]
    public int TerraBaseDivisor { get; set; } = 4;

    [SettingRole(SettingRole.GenerationOnly)]
    [Category("04 Continents")]
    public float TerraSeaLevel { get; set; } = 0.30f;

    /// <summary>How far a continent interior rises above the waterline before any uplift.</summary>
    [SettingRole(SettingRole.GenerationOnly)]
    [Category("04 Continents")]
    [Description("How far a continent interior rises above the waterline before any uplift.")]
    public float TerraContinentRise { get; set; } = 0.075f;

    /// <summary>Depth of the abyssal plain below sea level.</summary>
    [SettingRole(SettingRole.GenerationOnly)]
    [Category("05 Coast and sea floor")]
    [Description("Depth of the abyssal plain below sea level.")]
    public float TerraOceanDepth { get; set; } = 0.26f;

    /// <summary>
    /// How sharply the sea floor falls away from the coast. Higher is a narrower continental shelf.
    /// Tuned against vanilla's measured offshore profile: at 20 px offshore vanilla's heightmap
    /// reads 4.5/255, well under the 19/255 water plane. Too low and shallow water hugs every
    /// coastline, letting the sea-floor material show through at coastal province borders.
    /// </summary>
    [SettingRole(SettingRole.GenerationOnly)]
    [Category("05 Coast and sea floor")]
    [Description("How sharply the sea floor falls away from the coast. Higher is a narrower continental shelf. Tuned against vanilla's measured offshore profile: at 20 px offshore vanilla's heightmap reads 4.5/255, well under the 19/255 water plane. Too low and shallow water hugs every coastline, letting the sea-floor material show through at coastal province borders.")]
    public double TerraShelfSteepness { get; set; } = 10.0;

    /// <summary>
    /// Depth, in simulation elevation units below sea level, at which the sea floor reaches pure
    /// black. Everything deeper is 0. This is what sets the continental shelf's *width*, since the
    /// generator's depth grows with distance offshore.
    /// </summary>
    [SettingRole(SettingRole.Always)]
    [Category("05 Coast and sea floor")]
    [Description("Depth, in simulation elevation units below sea level, at which the sea floor reaches pure black. Everything deeper is 0. This is what sets the continental shelf's *width*, since the generator's depth grows with distance offshore.")]
    public double TerraShelfDepth { get; set; } = 24.0;

    /// <summary>
    /// Shape of the shelf falloff. Above 1 it falls away fast just offshore and then flattens,
    /// which is what vanilla's profile does (18.4/255 at 2 px, 4.5 at 20 px, black by 40).
    /// </summary>
    [SettingRole(SettingRole.Always)]
    [Category("05 Coast and sea floor")]
    [Description("Shape of the shelf falloff. Above 1 it falls away fast just offshore and then flattens, which is what vanilla's profile does (18.4/255 at 2 px, 4.5 at 20 px, black by 40).")]
    public double TerraShelfCurve { get; set; } = 2.4;

    /// <summary>Continent-sized features across the map width. Lower means fewer, bigger landmasses.</summary>
    [SettingRole(SettingRole.GenerationOnly)]
    [Category("04 Continents")]
    [Description("Continent-sized features across the map width. Lower means fewer, bigger landmasses.")]
    public double TerraContinentScale { get; set; } = 3.1;

    /// <summary>Amplitude of the broad relief that makes continent interiors hilly rather than flat.</summary>
    [SettingRole(SettingRole.GenerationOnly)]
    [Category("04 Continents")]
    [Description("Amplitude of the broad relief that makes continent interiors hilly rather than flat.")]
    public float TerraInteriorRelief { get; set; } = 0.035f;

    [SettingRole(SettingRole.GenerationOnly)]
    [Category("06 Tectonics")]
    public int TerraPlateCount { get; set; } = 24;

    /// <summary>
    /// Share of plates carrying continental crust. Not the same as land fraction — a continental
    /// plate is mostly land but carries shelf and margin too, and the coastline is still solved to
    /// hit TargetLandFraction exactly. Raising this makes fewer, larger oceans.
    /// </summary>
    [SettingRole(SettingRole.GenerationOnly)]
    [Category("06 Tectonics")]
    [Description("Share of plates carrying continental crust. Not the same as land fraction - the coastline is still solved to hit TargetLandFraction exactly. Raising this makes fewer, larger oceans.")]
    public double TerraContinentalPlateFraction { get; set; } = 0.45;

    /// <summary>
    /// How strongly plate crust type biases where land ends up, against the coastline noise. At 0
    /// continents ignore the plates entirely, which is how this worked before. Too high and the
    /// coastline starts tracing plate outlines, which look like polygons.
    /// </summary>
    [SettingRole(SettingRole.GenerationOnly)]
    [Category("06 Tectonics")]
    [Description("How strongly plate crust type biases where land ends up, against the coastline noise. At 0 continents ignore the plates entirely. Too high and coastlines start tracing plate outlines, which look like polygons.")]
    public double TerraPlateInfluence { get; set; } = 0.35;

    /// <summary>
    /// How far either side of a plate boundary continentality takes to reach that plate's own
    /// value, as a fraction of map width. This feather is the main defence against polygonal
    /// coastlines; narrowing it sharpens continental margins onto the plate outline.
    /// </summary>
    [SettingRole(SettingRole.GenerationOnly)]
    [Category("06 Tectonics")]
    [Description("How far either side of a plate boundary continentality takes to reach that plate's own value, as a fraction of map width. The main defence against polygonal coastlines.")]
    public double TerraCratonFeather { get; set; } = 0.055;

    /// <summary>
    /// Radius, as a fraction of map width, of the blur applied to the continentality field. This
    /// is what dissolves the Voronoi tessellation into continental mass; without it the field is a
    /// flat plateau per plate whose only gradient is the plate outline, and coastlines trace it.
    /// </summary>
    [SettingRole(SettingRole.GenerationOnly)]
    [Category("06 Tectonics")]
    [Description("Radius, as a fraction of map width, of the blur applied to the continentality field. Dissolves the Voronoi tessellation into continental mass; without it coastlines trace plate outlines.")]
    public double TerraCratonBlur { get; set; } = 0.030;

    /// <summary>
    /// Cycles across the map width of the field that decides which plates are continental. Low
    /// values clump all continental crust into one supercontinent; high values scatter it into
    /// unconnected one-plate islands.
    /// </summary>
    [SettingRole(SettingRole.GenerationOnly)]
    [Category("06 Tectonics")]
    [Description("Cycles across the map width of the field deciding which plates are continental. Low values clump all continental crust into one supercontinent; high values scatter it into one-plate islands.")]
    public double TerraCratonClustering { get; set; } = 3.2;

    /// <summary>
    /// Width of the uplift belt at a converging plate boundary, as a fraction of map width. This is
    /// the single number that decides whether mountains read as strips or as regions — vanilla's
    /// ranges are a couple of hundred pixels wide on an 18k-wide map.
    /// </summary>
    [SettingRole(SettingRole.GenerationOnly)]
    [Category("06 Tectonics")]
    [Description("Width of the uplift belt at a converging plate boundary, as a fraction of map width. This is the single number that decides whether mountains read as strips or as regions — vanilla's ranges are a couple of hundred pixels wide on an 18k-wide map.")]
    public double TerraRangeWidth { get; set; } = 0.0065;

    /// <summary>Cycles across the map width of the along-belt modulation that gives ranges passes.</summary>
    [SettingRole(SettingRole.GenerationOnly)]
    [Category("06 Tectonics")]
    [Description("Cycles across the map width of the along-belt modulation that gives ranges passes.")]
    public double TerraRangeRoughness { get; set; } = 26.0;

    [SettingRole(SettingRole.GenerationOnly)]
    [Category("07 Erosion (base pass)")]
    public int TerraErosionIterations { get; set; } = 34;

    /// <summary>K in the stream power law, against drainage area normalised by land area.</summary>
    /// <summary>
    /// Broad uplift applied across all land, as a fraction of peak boundary uplift. Without it a
    /// continent interior receives no uplift at all and relaxes into a smooth dome, because stream
    /// power plus deposition only smooths once nothing is rising.
    /// </summary>
    [SettingRole(SettingRole.GenerationOnly)]
    [Category("07 Erosion (base pass)")]
    [Description("Broad uplift across all land, as a fraction of peak boundary uplift. Without it continent interiors get no uplift and relax into smooth domes with no valleys.")]
    public double TerraIntraplateUplift { get; set; } = 0.14;

    [SettingRole(SettingRole.GenerationOnly)]
    [Category("07 Erosion (base pass)")]
    [Description("K in the stream power law, against drainage area normalised by land area.")]
    public float TerraErodibility { get; set; } = 3.2f;

    [SettingRole(SettingRole.GenerationOnly)]
    [Category("07 Erosion (base pass)")]
    public float TerraUpliftPerStep { get; set; } = 0.026f;
    [SettingRole(SettingRole.GenerationOnly)]
    [Category("07 Erosion (base pass)")]
    public float TerraDeposition { get; set; } = 0.18f;

    /// <summary>Steepest slope the coarse terrain will hold, in height per base cell.</summary>
    [SettingRole(SettingRole.GenerationOnly)]
    [Category("07 Erosion (base pass)")]
    [Description("Steepest slope the coarse terrain will hold, in height per base cell.")]
    public float TerraTalus { get; set; } = 0.045f;

    // --- Slope scale ---
    //
    // Every parameter above that is a *slope* is expressed as height per grid cell, and a grid cell
    // is a different fraction of the world at every map size: the base grid is Width/4, so a cell
    // at `vanilla` spans 1/4608 of the map where one at `small` spans 1/1024. A talus angle of
    // 0.045 per cell therefore permits a map-relative slope 4.5x steeper at `vanilla` than at
    // `small`, and the stream power law's S term is correspondingly smaller, so valleys are carved
    // 4.5x more weakly against the same uplift. Both push the same way: taller, steeper, less
    // dissected ranges the larger the map. That is the "mountains are much steeper on vanilla than
    // on small" symptom, and it is why tuning at one size did not carry to another.
    //
    // Slopes are converted to the reference size below so a value tuned once holds everywhere.

    /// <summary>
    /// Heightmap width the slope parameters are authored against — the <c>small</c> preset, which
    /// is the size they were tuned at.
    /// </summary>
    [SettingRole(SettingRole.GenerationOnly)]
    [Category("02 Map size")]
    [Description("Heightmap width the slope parameters are authored against — the small preset, which is the size they were tuned at.")]
    public int TerraSlopeReferenceWidth { get; set; } = 4096;

    /// <summary>
    /// Per-cell slope conversion for a grid of the given width. The reference is the *base* grid at
    /// the reference map width, so this is 1 for the base grid at <c>small</c>.
    /// </summary>
    public double SlopeScaleFor(int gridWidth)
        => TerraSlopeReferenceWidth / 4.0 / Math.Max(1, gridWidth);

    /// <summary>Cell size at the reference width relative to this map's. 1 at <c>small</c>.</summary>
    public double TerraSlopeScale => SlopeScaleFor(Width / TerraBaseDivisor);

    /// <summary>
    /// Erosion iterations for the refinement pass at province resolution.
    ///
    /// This is the pass that makes dendritic drainage visible in the exported heightmap. The main
    /// erosion runs on the base grid, a quarter of the heightmap's width, so its finest channels
    /// are four export pixels across and everything below that came from noise. Zero disables it
    /// and restores the previous behaviour.
    /// </summary>
    [SettingRole(SettingRole.GenerationOnly)]
    [Category("08 Erosion (refinement pass)")]
    [Description("Erosion iterations for the refinement pass at province resolution. This is the pass that makes dendritic drainage visible in the exported heightmap. The main erosion runs on the base grid, a quarter of the heightmap's width, so its finest channels are four export pixels across and everything below that came from noise. Zero disables it and restores the previous behaviour.")]
    public int TerraRefineIterations { get; set; } = 10;

    /// <summary>Erodibility for the refinement pass. Independent of the base pass's.</summary>
    [SettingRole(SettingRole.GenerationOnly)]
    [Category("08 Erosion (refinement pass)")]
    [Description("Erodibility for the refinement pass. Independent of the base pass's.")]
    public float TerraRefineErodibility { get; set; } = 3.2f;

    /// <summary>
    /// Drainage-area exponent for the refinement pass. Well below the base pass's 0.5, because
    /// dendritic texture is made by headwater streams and a high exponent puts nearly all the
    /// incision into the handful of largest rivers, leaving the hillsides between them smooth.
    /// </summary>
    [SettingRole(SettingRole.GenerationOnly)]
    [Category("08 Erosion (refinement pass)")]
    [Description("Drainage-area exponent for the refinement pass. Well below the base pass's 0.5, because dendritic texture is made by headwater streams and a high exponent puts nearly all the incision into the handful of largest rivers, leaving the hillsides between them smooth.")]
    public float TerraRefineAreaExponent { get; set; } = 0.30f;

    /// <summary>Talus angle converted to this map's cell spacing.</summary>
    public float TerraTalusScaled => (float)(TerraTalus * TerraSlopeScale);

    /// <summary>Full-resolution relaxation limit, converted the same way.</summary>
    public float TerraDetailTalusScaled => (float)(TerraDetailTalus * TerraSlopeScale);

    /// <summary>Detail slope reference, converted the same way.</summary>
    public float TerraDetailSlopeRefScaled => (float)(TerraDetailSlopeRef * TerraSlopeScale);

    /// <summary>
    /// Deposition cut-off slope, converted the same way. Authored at the reference width.
    /// </summary>
    public float TerraDepositionSlopeScaled => (float)(0.03 * TerraSlopeScale);

    /// <summary>
    /// Erodibility, compensated for the slope term shrinking with cell size. The stream power law
    /// uses S^n, so holding incision constant across map sizes means dividing K by the same factor
    /// the slope was multiplied by, raised to n.
    /// </summary>
    public float TerraErodibilityScaled
        => (float)(TerraErodibility / Math.Pow(Math.Max(1e-6, TerraSlopeScale), 1.0));

    /// <summary>
    /// Per-iteration incision cap, scaled the same way erodibility is.
    ///
    /// Left absolute it silently undoes the erodibility scaling exactly where it matters most: at
    /// `vanilla` K is 4.5x larger, so the cap is reached 4.5x more readily, and the cells that
    /// reach it are the steep high-drainage ones on a range's flanks — the ones whose erosion is
    /// what stops a mountain reading as a cliff.
    /// </summary>
    public float TerraMaxIncisionScaled
        => (float)(0.02 / Math.Max(1e-6, TerraSlopeScale));

    /// <summary>Cycles across the map width of the finest detail added at heightmap resolution.</summary>
    [SettingRole(SettingRole.GenerationOnly)]
    [Category("09 Detail")]
    [Description("Cycles across the map width of the finest detail added at heightmap resolution.")]
    public double TerraDetailScale { get; set; } = 900.0;

    [SettingRole(SettingRole.GenerationOnly)]
    [Category("09 Detail")]
    public float TerraDetailAmplitude { get; set; } = 0.045f;

    /// <summary>Coarse slope at which detail is at full strength.</summary>
    [SettingRole(SettingRole.GenerationOnly)]
    [Category("09 Detail")]
    [Description("Coarse slope at which detail is at full strength.")]
    public float TerraDetailSlopeRef { get; set; } = 0.022f;

    // TerraDetailIncision is gone. It scaled each full-resolution pixel by its own drainage area
    // in a single pass, which deepens a valley that already exists but cannot branch — drainage
    // networks are emergent from routing and incising repeatedly. TerraRefineIterations replaces it
    // with real erosion iterations at province resolution.

    /// <summary>Slope limit for the full-resolution relaxation, in height per heightmap pixel.</summary>
    [SettingRole(SettingRole.GenerationOnly)]
    [Category("09 Detail")]
    [Description("Slope limit for the full-resolution relaxation, in height per heightmap pixel.")]
    public float TerraDetailTalus { get; set; } = 0.012f;

    /// <summary>Fraction of land cells that carry enough drainage to be drawn as a river.</summary>
    /// <summary>
    /// Catchment, in province cells, above which a watercourse is drawn as a river. Absolute
    /// rather than a share of the map: a cell is the same area at every map size, so this is a
    /// fixed catchment in square kilometres.
    /// </summary>
    [SettingRole(SettingRole.Always)]
    [Category("10 Rivers and lakes")]
    [Description("Catchment, in province cells, above which a watercourse is drawn as a river. Absolute rather than a share of the map, so the same stream is a river on any size map.")]
    public double RiverMinCatchmentCells { get; set; } = 900;

    // River geometry is authored in vanilla province pixels and scaled by MapScale, so a river is
    // the same fraction of a continent at every map size rather than nine times wider at `tiny`.

    /// <summary>Shortest course kept.</summary>
    [SettingRole(SettingRole.Always)]
    [Category("10 Rivers and lakes")]
    [Description("Shortest course kept.")]
    public double MinRiverPixelsAtVanilla { get; set; } = 30;

    /// <summary>Douglas-Peucker tolerance. This is what removes the D8 staircase.</summary>
    [SettingRole(SettingRole.Always)]
    [Category("10 Rivers and lakes")]
    [Description("Douglas-Peucker tolerance. This is what removes the D8 staircase.")]
    public double RiverSimplifyAtVanilla { get; set; } = 1.6;

    /// <summary>Largest perpendicular meander offset.</summary>
    [SettingRole(SettingRole.Always)]
    [Category("10 Rivers and lakes")]
    [Description("Largest perpendicular meander offset.")]
    public double MeanderPixelsAtVanilla { get; set; } = 2.5;

    // No longer scaled by map size: a river is the same width and wanders the same distance
    // whatever size map it is on, because a pixel is the same distance on all of them.
    public int TerraMinRiverCells => Math.Max(8, (int)MinRiverPixelsAtVanilla);
    public double TerraRiverSimplify => Math.Max(0.6, RiverSimplifyAtVanilla);
    public double TerraMeanderPixels => Math.Max(0.5, MeanderPixelsAtVanilla);


    /// <summary>How deep a filled depression must be to count as a lake.</summary>
    [SettingRole(SettingRole.Always)]
    [Category("10 Rivers and lakes")]
    [Description("How deep a filled depression must be to count as a lake.")]
    public float TerraLakeDepth { get; set; } = 0.0015f;

    [SettingRole(SettingRole.Always)]
    [Category("10 Rivers and lakes")]
    public int TerraMinLakeCells { get; set; } = 400;

    /// <summary>
    /// Share of land put above the mountain line. Vanilla's own heightmap has 3.3% of its land in
    /// the 121-170 band, and that is the number this reproduces.
    /// </summary>
    [SettingRole(SettingRole.GenerationOnly)]
    [Category("11 Height scale")]
    [Description("Share of land put above the mountain line. Vanilla's own heightmap has 3.3% of its land in the 121-170 band, and that is the number this reproduces.")]
    public double TerraMountainShare { get; set; } = 0.035;

    [SettingRole(SettingRole.Always)]
    [Category("11 Height scale")]
    public int TerraTopElevation { get; set; } = 520;
    [SettingRole(SettingRole.Always)]
    [Category("11 Height scale")]
    public int TerraFloorElevation { get; set; } = -250;

    /// <summary>
    /// Where the equator line sits, as a fraction of map height. Everything about climate is
    /// measured as distance from it, so this slides every band up or down the map together.
    ///
    /// ck2rpg's 0.9 is deliberately off-centre: it puts the equator near the bottom edge, so a map
    /// is mostly one hemisphere and the cold band only appears at the top. 0.5 centres it and gives
    /// a symmetric world with tropics through the middle and cold at both edges.
    /// </summary>
    [SettingRole(SettingRole.Always)]
    [Category("12 Climate")]
    [Description("Where the equator sits, as a fraction of map height. Slides every climate band up or down together. 0.9 (ck2rpg's) puts it near the bottom edge so the map is mostly one hemisphere; 0.5 centres it and gives cold at both edges.")]
    public double EquatorPosition { get; set; } = 0.9;

    /// <summary>settings.equator — in raster space.</summary>
    public double Equator => Height * Math.Clamp(EquatorPosition, 0.0, 1.0);

    /// <summary>settings.pixelSize — raster pixels per simulation cell.</summary>
    public double PixelSize => (double)Height / WorldHeight;

    // --- Province map. CK3's provinces.png and rivers.png are half the heightmap resolution
    // (vanilla: heightmap 18432x9216, provinces 9216x4608). ---
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

    /// <summary>
    /// Vanilla's base grid width. Terrain feature wavelengths and amplitudes are authored
    /// against these three constants rather than against this map's own width, so a feature is
    /// the same number of pixels — and therefore the same physical size — at every map size.
    /// Dividing by the live width instead made a small map a shrunken world: the same three
    /// continents and the same mountain ranges squeezed into a quarter of the pixels, four times
    /// steeper per cell. A smaller map should be a smaller region at full detail.
    /// </summary>
    public int ReferenceBaseWidth => ReferenceHeightmapWidth / Math.Max(1, TerraBaseDivisor);

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
    [SettingRole(SettingRole.Always)]
    [Category("03 Provinces")]
    [Description("How large a barony is, relative to vanilla's. 2 makes each one twice as wide and therefore a quarter as numerous; the whole title hierarchy follows, because MapGen.Titles clusters by counts rather than by area. This is the only knob for map granularity. Province counts used to be given directly, which meant a map kept the same number of provinces at every resolution and so a barony at tiny cove...")]
    public double CountyScale { get; set; } = 1.0;

    /// <summary>
    /// Average land province area in province-map pixels, at <see cref="CountyScale"/> 1.
    ///
    /// Vanilla: 10,966 baronies over roughly 22.4M land pixels of its 9216x4608 province map.
    /// A barony is one province here, so this reproduces vanilla's barony density.
    /// </summary>
    [SettingRole(SettingRole.Always)]
    [Category("03 Provinces")]
    [Description("Average land province area in province-map pixels, at CountyScale 1. Vanilla: 10,966 baronies over roughly 22.4M land pixels of its 9216x4608 province map. A barony is one province here, so this reproduces vanilla's barony density.")]
    public double BaronyPixelsAtVanilla { get; set; } = 2043;

    /// <summary>
    /// Average sea zone area, same basis. Vanilla's sea zones are an order of magnitude larger
    /// than its baronies — roughly 800 of them over 20M water pixels.
    /// </summary>
    [SettingRole(SettingRole.Always)]
    [Category("03 Provinces")]
    [Description("Average sea zone area, same basis. Vanilla's sea zones are an order of magnitude larger than its baronies — roughly 800 of them over 20M water pixels.")]
    public double SeaZonePixelsAtVanilla { get; set; } = 25000;

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
    [SettingRole(SettingRole.Always)]
    [Category("13 Development")]
    [Description("Development every county gets regardless of its terrain — the floor for the poorest backwater.")]
    public int DevelopmentBase { get; set; } = 0;

    /// <summary>How much development the very best terrain adds on top of the base.</summary>
    [SettingRole(SettingRole.Always)]
    [Category("13 Development")]
    [Description("How much development the best possible terrain adds on top of the base. Vanilla's 867 median is about 8 and its mass runs to 16.")]
    public int DevelopmentSpread { get; set; } = 22;

    /// <summary>Overall multiplier on the terrain-derived part. The quick 'richer/poorer' dial.</summary>
    [SettingRole(SettingRole.Always)]
    [Category("13 Development")]
    [Description("Overall multiplier on the terrain-derived development. The quick dial for a richer or poorer world without changing how it is distributed.")]
    public double DevelopmentScale { get; set; } = 1.0;

    /// <summary>
    /// How sharply development concentrates on the best land. 1 spreads it evenly across the
    /// ranked counties; higher makes rich counties rarer. 1.5 reproduces vanilla's 867 shape:
    /// median about 8, p90 about 19.
    /// </summary>
    [SettingRole(SettingRole.Always)]
    [Category("13 Development")]
    [Description("How sharply development concentrates on the best land. Counties are ranked against each other, so 1 is an even spread and higher makes rich counties rarer. 1.5 reproduces vanilla 867: median about 8, p90 about 19.")]
    public double DevelopmentSkew { get; set; } = 1.5;

    /// <summary>Added to a county's terrain score if any of its baronies reaches the sea.</summary>
    [SettingRole(SettingRole.Always)]
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
    [SettingRole(SettingRole.Always)]
    [Category("04 Titles")]
    [Description("How wide a stretch of water a kingdom or empire may reach across, in vanilla province pixels. Counties and duchies always stay on one landmass. Vanilla reference: Dover about 30 px, the Irish Sea about 90.")]
    public double SeaBridgePixelsAtVanilla { get; set; } = 110;


    /// <summary>
    /// How deep inside its province a holding, army or siege model must stand, as a fraction of
    /// the deepest point that province has. 0 lets a model sit on the border; 1 pins it to the
    /// single deepest pixel and leaves flatness no say.
    /// </summary>
    [SettingRole(SettingRole.Always)]
    [Category("04 Titles")]
    [Description("How deep inside its province a holding or army model must stand, as a fraction of that province's deepest point. Raising it keeps models further from coastlines; lowering it lets flatness matter more than position.")]
    public double LocatorInteriorFraction { get; set; } = 0.6;

    /// <summary>
    /// How much a model prefers the middle of its province over flat ground. Measured against the
    /// map's median slope, so 1 means being a province-radius off centre costs as much as standing
    /// on ground one median slope steeper.
    /// </summary>
    [SettingRole(SettingRole.Always)]
    [Category("04 Titles")]
    [Description("How much a holding prefers the middle of its province over flat ground. 0 puts it on the flattest eligible pixel wherever that is; higher pulls it toward the centre even if the ground there is steeper.")]
    public double LocatorCentroidPull { get; set; } = 0.75;


    // --- Cultures and faiths ---
    //
    // Vanilla's own proportions, for calibration: ~193 cultures in ~40 heritages, and ~120 faiths
    // in ~48 religions, over 3,827 counties. That is roughly one culture per 20 counties, five
    // cultures to a heritage, and a religious map a little coarser than the cultural one.

    /// <summary>Counties a generated culture covers on average. Lower makes a more fragmented world.</summary>
    [SettingRole(SettingRole.Always)]
    [Category("14 Cultures and faiths")]
    [Description("Counties per generated culture. Vanilla averages about 20; lower values make a more fragmented, more polyglot world.")]
    public double CountiesPerCulture { get; set; } = 18;

    /// <summary>
    /// Cultures sharing one heritage and one language. This is what decides how related neighbours
    /// are: CK3's acceptance, hybridisation and divergence all key off shared heritage, so a world
    /// of one-culture heritages is a world where nobody can ever get along with anybody.
    /// </summary>
    [SettingRole(SettingRole.Always)]
    [Category("14 Cultures and faiths")]
    [Description("Cultures sharing one heritage and language. Higher values give large related families like vanilla's Frankish or North Germanic groups; 1 gives a world where no two cultures are relatives.")]
    public double CulturesPerHeritage { get; set; } = 4;

    /// <summary>
    /// How strongly culture borders follow the ground. 0 ignores terrain entirely and gives a plain
    /// voronoi; 1 uses the authored crossing costs as written.
    /// </summary>
    [SettingRole(SettingRole.Always)]
    [Category("14 Cultures and faiths")]
    [Description("How strongly culture borders follow terrain. 0 ignores the ground and cuts straight over mountains; 1 makes ranges and deserts into language barriers.")]
    public double CultureTerrainWeight { get; set; } = 1.0;

    /// <summary>Counties a generated faith covers on average. Coarser than cultures, as in vanilla.</summary>
    [SettingRole(SettingRole.Always)]
    [Category("14 Cultures and faiths")]
    [Description("Counties per generated faith. Deliberately coarser than cultures — vanilla runs about 120 faiths against 193 cultures.")]
    public double CountiesPerFaith { get; set; } = 26;

    /// <summary>Faiths sharing one religion, and therefore its doctrines and its gods.</summary>
    [SettingRole(SettingRole.Always)]
    [Category("14 Cultures and faiths")]
    [Description("Faiths sharing one religion. Faiths of a religion are heresies of each other: same gods, different doctrine, and a much smaller penalty for converting between them.")]
    public double FaithsPerReligion { get; set; } = 2.5;

    /// <summary>
    /// The faith equivalent of <see cref="CultureTerrainWeight"/>, and deliberately lower. If the
    /// two matched, faith borders would land on culture borders and the map would have only one
    /// axis of difference on it.
    /// </summary>
    [SettingRole(SettingRole.Always)]
    [Category("14 Cultures and faiths")]
    [Description("How strongly faith borders follow terrain. Kept below the culture weight on purpose: matching them puts every faith border on a culture border, and the interesting map is the one where they disagree.")]
    public double FaithTerrainWeight { get; set; } = 0.45;

    /// <summary>Holy sites each faith declares, placed on its highest-development counties.</summary>
    [SettingRole(SettingRole.Always)]
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
    [SettingRole(SettingRole.Always)]
    [Category("03 Provinces")]
    [Description("How strongly province growth resists crossing a slope. 0 is a plain geodesic voronoi whose boundaries cut straight over mountains; higher makes provinces meet at ridgelines.")]
    public double ProvinceTerrainCost { get; set; } = 1.5;

    /// <summary>
    /// Share of land provinces declared impassable_mountains. Vanilla's ratio is 1,188 impassable
    /// against 11,301 baronied. Impassable provinces get no barony and no holder.
    /// </summary>
    [SettingRole(SettingRole.Always)]
    [Category("03 Provinces")]
    [Description("Share of land provinces declared impassable_mountains, which get no barony and no holder. Vanilla runs 1,188 impassable against 11,301 baronied.")]
    public double ImpassableShareOfLand { get; set; } = 0.095;

    /// <summary>
    /// How much of a province must stand above the mountain line before it may be impassable. Stops
    /// a map with little high ground being given impassable provinces just to hit the target count.
    /// </summary>
    [SettingRole(SettingRole.Always)]
    [Category("03 Provinces")]
    [Description("How much of a province must stand above the mountain line before it may be impassable. Stops a flat map being given impassable provinces just to hit the target count.")]
    public double ImpassableMinMountainShare { get; set; } = 0.45;

    [SettingRole(SettingRole.Always)]
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
    [SettingRole(SettingRole.Always)]
    [Category("03 Provinces")]
    [Description("Rows and columns of forced ocean around the edge of the province map, in province pixels. Vanilla has water along every edge — its top and bottom rows are entirely sea, and its province map has only a handful of large ocean provinces touching them. A generated map happily runs land off the poles instead: on seed 1 at vanilla size, 33 land provinces touched the top edge and 17 the bottom. A prov...")]
    public int OceanBorder { get; set; } = 1;

    // --- Climate ---
    //
    // The latitude bands below are a straight port of ck2rpg, and on their own they draw the
    // climate as a horizontal stripe: a band edge is a single-valued function of x, so however much
    // it is jittered per column it is still one continuous seam running the width of the map.
    // ck2rpg's own jitter is a +/-1 random walk indexed by *simulation* column, which wanders
    // slowly and only ever produces a gently wavy line.
    //
    // These two turn the edge into a contour of a 2D field instead, which is what gives it inlets,
    // peninsulas and outliers. Both are in the same raster units as the band limits themselves —
    // absolute, referenced to vanilla, not scaled to this map.

    /// <summary>
    /// How far the climate boundary wanders from its latitude, in raster pixels. Warped fBm, so it
    /// wanders at several scales at once rather than as one smooth sine.
    /// </summary>
    [SettingRole(SettingRole.Always)]
    [Category("12 Climate")]
    [Description("How far a climate boundary wanders from its latitude, in raster pixels. 0 restores ck2rpg's straight horizontal bands.")]
    public double ClimateWanderPixels { get; set; } = 420;

    /// <summary>
    /// How much altitude counts as latitude, in raster pixels per full mountain height. Without it
    /// a range crossing a band edge is simply cut in half by it, because ck2rpg's climate is a
    /// function of y alone — the tropics run straight over a 4,000 m massif. With it the boundary
    /// bends around terrain and high ground carries its own colder climate, which is both what real
    /// climate does and what stops the band reading as a ruled line.
    /// </summary>
    [SettingRole(SettingRole.Always)]
    [Category("12 Climate")]
    [Description("How much altitude counts as latitude, in raster pixels per full mountain height. Makes high ground colder than the lowland at the same latitude, so climate boundaries bend around ranges instead of cutting through them.")]
    public double ClimateLapsePixels { get; set; } = 900;

    // Band widths, authored as widths rather than as edges. ck2rpg stores each band's inner and
    // outer edge as a separate constant, which means any change has to update the neighbour too or
    // the bands stop tiling and leave a gap that classifies as nothing. Scaling widths and
    // accumulating them, as ResetClimateLimits does, cannot produce a gap.
    //
    // The base widths are ck2rpg's, at its 8192-wide authoring scale: tropical 1007, subtropical
    // 513, temperate 1345, then cold to the pole.

    /// <summary>
    /// Stretches every climate band at once. Above 1 the bands are wider, so a map of a given size
    /// spans fewer of them; below 1 it crosses more of them over the same distance.
    /// </summary>
    [SettingRole(SettingRole.Always)]
    [Category("12 Climate")]
    [Description("Stretches every climate band at once. Above 1 the bands are wider and the map spans fewer of them; below 1 it crosses more of them over the same distance.")]
    public double ClimateBandScale { get; set; } = 1.0;

    /// <summary>Width of the tropical band, relative to ck2rpg's.</summary>
    [SettingRole(SettingRole.Always)]
    [Category("12 Climate")]
    [Description("Width of the tropical band, relative to ck2rpg's. Bands above it shift outward to keep tiling.")]
    public double TropicalWidthScale { get; set; } = 1.0;

    /// <summary>Width of the subtropical band, relative to ck2rpg's.</summary>
    [SettingRole(SettingRole.Always)]
    [Category("12 Climate")]
    [Description("Width of the subtropical band, relative to ck2rpg's. Bands above it shift outward to keep tiling.")]
    public double SubTropicalWidthScale { get; set; } = 1.0;

    /// <summary>Width of the temperate band, relative to ck2rpg's. Cold is whatever is left.</summary>
    [SettingRole(SettingRole.Always)]
    [Category("12 Climate")]
    [Description("Width of the temperate band, relative to ck2rpg's. Cold is simply everything beyond it, so widening this pushes the cold band toward the pole.")]
    public double TemperateWidthScale { get; set; } = 1.0;

    public Limits Limits { get; } = new();

    /// <summary>
    /// Places the climate bands, in raster pixels of distance from the equator.
    ///
    /// Edges are accumulated from widths, so the bands tile by construction however they are
    /// scaled — there is no way to set a gap between two of them that would classify as nothing.
    ///
    /// The base scale is referenced to vanilla, not to this map. The widths are authored against an
    /// 8192-wide raster, so this constant is what puts them where vanilla has them — and a smaller
    /// map, being a smaller *region*, then spans fewer of them rather than compressing all of them
    /// into its height. A `tiny` map sits within one or two bands, which is the point.
    /// </summary>
    public void ResetClimateLimits(Core.Rng rng)
    {
        double mod = ReferenceHeightmapWidth / 8192.0 * Math.Max(0.01, ClimateBandScale);

        double tropical = TropicalBaseWidth * Math.Max(0, TropicalWidthScale) * mod;
        double subTropical = tropical + SubTropicalBaseWidth * Math.Max(0, SubTropicalWidthScale) * mod;
        double temperate = subTropical + TemperateBaseWidth * Math.Max(0, TemperateWidthScale) * mod;

        Limits.Tropical.Upper = (int)Math.Floor(tropical);
        Limits.SubTropical.Upper = (int)Math.Floor(subTropical);
        Limits.Temperate.Upper = (int)Math.Floor(temperate);

        // The far-polar cut-off, a fixed distance into the cold band rather than an absolute
        // latitude, so it follows the bands instead of being overtaken by them.
        Limits.Cold.Plains = (int)Math.Floor(temperate + PolarBaseDepth * mod);

        ResetVaryRanges(rng);
    }

    // ck2rpg's own band widths, at its 8192-wide authoring scale.
    private const double TropicalBaseWidth = 1007;
    private const double SubTropicalBaseWidth = 513;
    private const double TemperateBaseWidth = 1345;
    private const double PolarBaseDepth = 435;

    /// <summary>Port of resetVaryRanges() — per-column jitter so climate bands are not straight lines.</summary>
    public void ResetVaryRanges(Core.Rng rng)
    {
        Limits.Tropical.VaryRange = CreateVaryRange(rng);
        Limits.SubTropical.VaryRange = CreateVaryRange(rng);
        Limits.Temperate.VaryRange = CreateVaryRange(rng);
        Limits.Cold.VaryRange = CreateVaryRange(rng);
    }

    /// <summary>Port of createVaryRange() — a bounded random walk of length world.width.</summary>
    private int[] CreateVaryRange(Core.Rng rng)
    {
        var arr = new int[WorldWidth];
        int last = 0;
        for (int i = 0; i < WorldWidth; i++)
        {
            last += rng.Int(-1, 1);
            if (last > 15) last = 15;
            if (last < -15) last = -15;
            arr[i] = last;
        }
        return arr;
    }
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

    // Placed by MapConfig.ResetClimateLimits; cold is simply everything past temperate.
    public ClimateBand Tropical = new();
    public ClimateBand SubTropical = new();
    public ClimateBand Temperate = new();
    public ClimateBand Cold = new();

    public readonly record struct Range(int Lower, int Upper);
    public readonly record struct MountainRange(int Lower, int Upper, int SnowLine);
}

/// <summary>
/// A latitude band, as a distance-from-equator in raster pixels.
///
/// Only the outer edge is stored. The inner edge is the band below's outer edge by construction —
/// <see cref="MapConfig.ResetClimateLimits"/> accumulates them — and <c>Biome.ZoneOf</c> tests them
/// as an ordered cascade, so a band never needs to know where it starts.
/// </summary>
public sealed class ClimateBand
{
    /// <summary>Outer edge. Set by <see cref="MapConfig.ResetClimateLimits"/>.</summary>
    public int Upper;

    /// <summary>The far-polar cut-off. Only the cold band has one.</summary>
    public int? Plains;

    /// <summary>Per-column jitter of this band's outer edge, in simulation columns.</summary>
    public int[] VaryRange = [];
}



