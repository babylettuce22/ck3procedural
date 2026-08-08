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

    /// <summary>Seed for every random decision. ck2rpg used unseeded Math.random.</summary>
    [SettingRole(SettingRole.Always)]
    [Category("01 General")]
    [Description("Seed for every random decision. ck2rpg used unseeded Math.random.")]
    public int Seed { get; set; } = 1;

    [SettingRole(SettingRole.GenerationOnly)]
    [Category("99 Legacy (ck2rpg path)")]
    public int TooSmallProvince { get; set; } = 900;
    [SettingRole(SettingRole.GenerationOnly)]
    [Category("99 Legacy (ck2rpg path)")]
    public bool HorizontalSpread { get; set; } = false;
    [SettingRole(SettingRole.GenerationOnly)]
    [Category("99 Legacy (ck2rpg path)")]
    public bool VerticalSpread { get; set; } = true;

    /// <summary>When true, land provinces may override water during province fill.</summary>
    [SettingRole(SettingRole.GenerationOnly)]
    [Category("99 Legacy (ck2rpg path)")]
    [Description("When true, land provinces may override water during province fill.")]
    public bool FixBlockiness { get; set; } = false;

    [SettingRole(SettingRole.GenerationOnly)]
    [Category("99 Legacy (ck2rpg path)")]
    public int RiversDistance { get; set; } = 10;
    [SettingRole(SettingRole.GenerationOnly)]
    [Category("99 Legacy (ck2rpg path)")]
    public int RiverIntoOcean { get; set; } = 1;
    [SettingRole(SettingRole.GenerationOnly)]
    [Category("99 Legacy (ck2rpg path)")]
    public bool VaryElevation { get; set; } = false;
    [SettingRole(SettingRole.GenerationOnly)]
    [Category("99 Legacy (ck2rpg path)")]
    public int LandProvinceLimit { get; set; } = 6000;
    [SettingRole(SettingRole.GenerationOnly)]
    [Category("99 Legacy (ck2rpg path)")]
    public int WaterProvinceLimit { get; set; } = 10000;
    [SettingRole(SettingRole.GenerationOnly)]
    [Category("99 Legacy (ck2rpg path)")]
    public int FillInLimit { get; set; } = 20;
    [SettingRole(SettingRole.GenerationOnly)]
    [Category("99 Legacy (ck2rpg path)")]
    public int MassBrushAdjuster { get; set; } = 1;
    [SettingRole(SettingRole.GenerationOnly)]
    [Category("99 Legacy (ck2rpg path)")]
    public bool OverrideWithFlatmap { get; set; } = false;
    [SettingRole(SettingRole.GenerationOnly)]
    [Category("99 Legacy (ck2rpg path)")]
    public int ElevationToHeightmap { get; set; } = 2;
    [SettingRole(SettingRole.GenerationOnly)]
    [Category("99 Legacy (ck2rpg path)")]
    public string Ethnicities { get; set; } = "vanilla";

    /// <summary>
    /// Fraction of the grid that should end up above sea level. ck2rpg has no such setting:
    /// its startup() sequence leaves an archipelago (~6% land) and the user grows continents by
    /// clicking the "spread" button, which runs three more emit/spread rounds per press. This
    /// automates that loop so the tool can run unattended. Set to 0 to stop after startup().
    /// </summary>
    [SettingRole(SettingRole.GenerationOnly)]
    [Category("04 Continents")]
    [Description("Fraction of the grid that should end up above sea level. ck2rpg has no such setting: its startup() sequence leaves an archipelago (~6% land) and the user grows continents by clicking the \"spread\" button, which runs three more emit/spread rounds per press. This automates that loop so the tool can run unattended. Set to 0 to stop after startup().")]
    public double TargetLandFraction { get; set; } = 0.40;

    /// <summary>Safety cap on the automated growth loop.</summary>
    [SettingRole(SettingRole.GenerationOnly)]
    [Category("99 Legacy (ck2rpg path)")]
    [Description("Safety cap on the automated growth loop.")]
    public int MaxExtraSpreadRounds { get; set; } = 400;

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
    [Description("Reshape land heights onto vanilla's measured hypsometric curve instead of stretching them linearly to whatever the tallest simulated peak happens to be. The linear stretch made the map as mountainous as its most extreme accident: measured on 2026-08-07, it put 18x more land in the top elevation band than vanilla has, and always drove the highest pixel to 255 where vanilla's tops out at 192. The...")]
    public bool MatchVanillaHypsometry { get; set; } = true;

    /// <summary>
    /// Run rainErosion() before rivers. It carves valleys and is the only thing in ck2rpg that
    /// creates lakes, but its UI button is commented out, so a stock ck2rpg run never calls it
    /// and lakes only ever come from hand-painting. Off by default to match that.
    /// </summary>
    [SettingRole(SettingRole.GenerationOnly)]
    [Category("99 Legacy (ck2rpg path)")]
    [Description("Run rainErosion() before rivers. It carves valleys and is the only thing in ck2rpg that creates lakes, but its UI button is commented out, so a stock ck2rpg run never calls it and lakes only ever come from hand-painting. Off by default to match that.")]
    public bool EnableRainErosion { get; set; } = false;

    // --- Terra: the terrain generator (MapGen/Terra) ---
    //
    // Heights inside Terra are normalised to roughly [0, 1] with sea level at TerraSeaLevel, and
    // only converted to the integer scale above on the way out. Lengths are given as a fraction of
    // the map width or as cycles across it, never in pixels, so changing map size resamples the
    // same world instead of generating a different one.

    /// <summary>
    /// Use the tectonics-and-erosion generator instead of the ck2rpg magma simulation. The old
    /// path is kept behind <c>--legacy-terrain</c> for comparison.
    /// </summary>
    [SettingRole(SettingRole.GenerationOnly)]
    [Category("01 General")]
    [Description("Use the tectonics-and-erosion generator instead of the ck2rpg magma simulation. The old path is kept behind --legacy-terrain for comparison.")]
    public bool UseTerra { get; set; } = true;

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
    [SettingRole(SettingRole.GenerationOnly)]
    [Category("07 Erosion (base pass)")]
    [Description("K in the stream power law, against drainage area normalised by land area.")]
    public float TerraErodibility { get; set; } = 3.2f;

    [SettingRole(SettingRole.GenerationOnly)]
    [Category("07 Erosion (base pass)")]
    public float TerraUpliftPerStep { get; set; } = 0.026f;
    [SettingRole(SettingRole.GenerationOnly)]
    [Category("07 Erosion (base pass)")]
    public float TerraDeposition { get; set; } = 0.35f;

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

    /// <summary>Half-width of the channel cut into the heightmap.</summary>
    [SettingRole(SettingRole.Always)]
    [Category("10 Rivers and lakes")]
    [Description("Half-width of the channel cut into the heightmap.")]
    public double ChannelRadiusAtVanilla { get; set; } = 3.0;

    // No longer scaled by map size: a river is the same width and wanders the same distance
    // whatever size map it is on, because a pixel is the same distance on all of them.
    public int TerraMinRiverCells => Math.Max(8, (int)MinRiverPixelsAtVanilla);
    public double TerraRiverSimplify => Math.Max(0.6, RiverSimplifyAtVanilla);
    public double TerraMeanderPixels => Math.Max(0.5, MeanderPixelsAtVanilla);
    public float TerraChannelRadius => (float)Math.Max(1.0, ChannelRadiusAtVanilla);

    /// <summary>Depth of the channel cut into the heightmap under a river, in normalised height.</summary>
    [SettingRole(SettingRole.GenerationOnly)]
    [Category("10 Rivers and lakes")]
    [Description("Depth of the channel cut into the heightmap under a river, in normalised height.")]
    public float TerraChannelDepth { get; set; } = 0.010f;

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

    /// <summary>settings.equator — in raster space, deliberately off-centre.</summary>
    public double Equator => Height - Height / 10.0;

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

    public Limits Limits { get; } = new();

    /// <summary>
    /// Port of resetClimateLimits(). Rescales the climate bands, which are authored against a
    /// 8192-wide map, to the configured raster width.
    /// </summary>
    public void ResetClimateLimits(Core.Rng rng)
    {
        // Referenced to vanilla, not to this map. The bands are authored against an 8192-wide
        // raster, so this is the constant that puts them where vanilla has them — and a smaller map,
        // being a smaller *region*, then spans fewer of them rather than compressing all of them
        // into its height. A `tiny` map sits within one or two bands, which is the point.
        double mod = ReferenceHeightmapWidth / 8192.0;
        Limits.Tropical.Rescale(mod);
        Limits.SubTropical.Rescale(mod);
        Limits.Temperate.Rescale(mod);
        Limits.Cold.Rescale(mod);
        ResetVaryRanges(rng);
    }

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

    public ClimateBand Tropical = new(0, 1007);
    public ClimateBand SubTropical = new(1008, 1520);
    public ClimateBand Temperate = new(1521, 2865);
    // cold.upper starts at 4096 but upperBase is 8000, so rescaling at width 8192 lifts it.
    public ClimateBand Cold = new(2866, 4096, upperBase: 8000, plains: 3300);

    public readonly record struct Range(int Lower, int Upper);
    public readonly record struct MountainRange(int Lower, int Upper, int SnowLine);
}

/// <summary>A latitude band measured as distance-from-equator in raster pixels.</summary>
public sealed class ClimateBand
{
    public int Lower;
    public int Upper;
    public readonly int LowerBase;
    public readonly int UpperBase;
    public int? Plains;
    private readonly int? _plainsBase;

    public int[] VaryRange = [];

    public ClimateBand(int lower, int upper, int? upperBase = null, int? plains = null)
    {
        Lower = LowerBase = lower;
        Upper = upper;
        UpperBase = upperBase ?? upper;
        Plains = plains;
        _plainsBase = plains;
    }

    /// <summary>Port of modifyClimate().</summary>
    public void Rescale(double mod)
    {
        Lower = (int)Math.Floor(LowerBase * mod);
        Upper = (int)Math.Floor(UpperBase * mod);
        if (_plainsBase.HasValue) Plains = (int)Math.Floor(_plainsBase.Value * mod);
    }
}
