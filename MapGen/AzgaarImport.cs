using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.Io;

namespace Ck3MapGen.MapGen;

/// <summary>
/// What one Azgaar object covers of one of our titles, and how much of it.
///
/// <see cref="Share"/> is the guard the whole import leans on. A county that is 90% inside one
/// Azgaar state genuinely belongs to it; one that is 35% inside it happens to overlap a corner,
/// and naming it after that state would put a Zwenish name on ground that is mostly somewhere else.
/// Every consumer is expected to check the share before believing the id.
/// </summary>
public readonly record struct AzgaarShare(int Id, double Share)
{
    public static readonly AzgaarShare None = new(0, 0);

    public bool Exists => Id > 0;

    /// <summary>True when this object covers enough of the title to speak for it.</summary>
    public bool Dominates(double threshold = 0.5) => Id > 0 && Share >= threshold;
}

/// <summary>Everything Azgaar has to say about one of our titles.</summary>
public sealed class AzgaarBinding
{
    public required AzgaarShare State { get; init; }
    public required AzgaarShare Province { get; init; }
    public required AzgaarShare Culture { get; init; }
    public required AzgaarShare Religion { get; init; }
    public required AzgaarShare Biome { get; init; }

    /// <summary>How many province-raster pixels this title covers.</summary>
    public required int Pixels { get; init; }

    /// <summary>Burgs sitting inside this title, largest first.</summary>
    public required IReadOnlyList<AzgaarBurg> Burgs { get; init; }

    /// <summary>The title's centre of mass, in Azgaar canvas coordinates.</summary>
    public required (double X, double Y) Centre { get; init; }
}

/// <summary>
/// An Azgaar export, loaded, rasterised onto our map, and matched up with our titles.
///
/// This is the single object the rest of the generator talks to. Everything downstream asks it
/// questions about a <see cref="Title"/> — which state holds it, which burg sits in it, what its
/// people should be called — and never touches the JSON or the raster directly. That indirection is
/// the point: naming is the only thing reading it today, but the binding it produces is the same
/// table a border-constrained province partition and an imported climate would read, so widening
/// the import later means adding consumers here rather than re-deriving the geometry somewhere else.
///
/// Null means "no import". Every call site is written so that a null import leaves the generator
/// behaving exactly as it did before, which is what keeps one code path rather than two.
/// </summary>
public sealed class AzgaarImport
{
    public required AzgaarWorld World { get; init; }
    public required AzgaarRaster Raster { get; init; }
    public required IReadOnlyList<AzgaarJson.Warning> Warnings { get; init; }
    public required MapConfig Config { get; init; }

    /// <summary>The rank plan for the export's states, once <see cref="PlanHierarchy"/> has run.</summary>
    public HierarchyPlan? Plan { get; private set; }

    /// <summary>
    /// The title each Azgaar state became, once the hierarchy has been built from the plan.
    ///
    /// This is what lets realm formation stop guessing. Each entry is one country the export drew,
    /// and one ruler at the start date; everything below it is that ruler's vassals and everything
    /// above it exists only to satisfy CK3's insistence on a complete de jure tree.
    /// </summary>
    public IReadOnlyDictionary<int, Title> StateTitles { get; private set; }
        = new Dictionary<int, Title>();

    internal void SetStateTitles(Dictionary<int, Title> titles) => StateTitles = titles;

    /// <summary>The alignment check against our own land mask, once one has been run.</summary>
    public AzgaarRaster.Alignment? Alignment { get; private set; }

    private readonly Dictionary<int, AzgaarNames?> _namesByBase = [];
    private Dictionary<Title, AzgaarBinding> _bindings = [];

    /// <summary>
    /// Azgaar cell tallies for every one of our provinces, indexed by province id — sea and river
    /// provinces included, not just the land baronies the titles are built from.
    ///
    /// Kept whole rather than trimmed to the land because water is where several of the borrowed
    /// names live: rivers and named seas have no title to hang off, and this is the only thing that
    /// can say which of our sea provinces sit in Azgaar's Bay of Whatever.
    /// </summary>
    private Dictionary<int, int>?[] _cellsByProvince = [];

    /// <summary>
    /// The single pass over the province raster, shared by everything that needs it.
    ///
    /// Lifted out of <see cref="Bind"/> because the hierarchy plan needs the same tallies and has to
    /// run *before* the title hierarchy exists — it is what decides the shape the hierarchy will be
    /// built in. Reading the raster twice for that would double the most expensive part of the
    /// import, so the read happens once, on whichever of the two asks first.
    /// </summary>
    private sealed class ProvinceSurvey
    {
        public required Dictionary<int, int>?[] Cells { get; init; }
        public required double[] SumX { get; init; }
        public required double[] SumY { get; init; }
        public required int[] Pixels { get; init; }
        public required Dictionary<int, List<AzgaarBurg>> Burgs { get; init; }
        public required int BaronyCount { get; init; }

        /// <summary>Majority Azgaar state for each land barony, indexed by province id. 0 for none.</summary>
        public required int[] StateByBarony { get; init; }

        /// <summary>The same for Azgaar's provinces, which is what a duchy would be built from.</summary>
        public required int[] ProvinceByBarony { get; init; }
    }

    private ProvinceSurvey? _survey;

    /// <summary>
    /// Loads and rasterises an export, or returns null when no path was given.
    ///
    /// Returning null rather than an empty import is deliberate: "no Azgaar file" is the normal
    /// case and should cost nothing, not an 8-million-element array of -1.
    /// </summary>
    public static AzgaarImport? Load(string? path, MapConfig cfg)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        var loaded = Core.Stage.Time("azgaar import", () => AzgaarJson.Load(path));

        AdoptCalendar(loaded.World, cfg);

        var raster = Core.Stage.Time("azgaar raster",
            () => AzgaarRaster.Build(loaded.World, cfg));

        return new AzgaarImport
        {
            World = loaded.World,
            Raster = raster,
            Warnings = loaded.Warnings,
            Config = cfg,
        };
    }

    /// <summary>
    /// Moves the world's calendar onto the export's own.
    ///
    /// Done here rather than downstream because <see cref="MapConfig.StartYear"/> is read by very
    /// nearly everything and this is the last moment before any of it runs —
    /// <see cref="HeightmapSource"/> already sets the map dimensions on <c>cfg</c> the same way and
    /// for the same reason. Adopting the year rather than merely displaying it is what keeps the
    /// game clock and the generated history speaking about the same moment; the alternative leaves
    /// a bookmark in 900 while the chronicle talks about 963, which reads as a bug.
    ///
    /// Nothing is clamped. A world that says it is the year 1448 is allowed to be, because
    /// <see cref="Emit.CompatibilityWriter"/> moves <c>END_DATE</c> out of its way, and because a
    /// tool that quietly relocated somebody's world by four centuries would be worse than one that
    /// took it at its word. What advancement that world has is a separate question and stays with
    /// <see cref="MapConfig.EraAnchorYear"/>.
    /// </summary>
    private static void AdoptCalendar(AzgaarWorld world, MapConfig cfg)
    {
        int year = world.Settings.Options.Year;
        if (year <= 0) return;

        // Pinned before the year moves. Left at zero the era would follow StartYear onto the
        // export's calendar, which is exactly what this feature exists to prevent: a world dated
        // 433 would drop its cultures into the tribal era on the way past. Whatever the user had
        // set is their answer to "how advanced", and it stays their answer.
        if (cfg.EraAnchorYear <= 0) cfg.EraAnchorYear = cfg.StartYear;

        int was = cfg.StartYear;
        cfg.StartYear = year;

        string era = world.Settings.Options.Era;
        Console.WriteLine($"  azgaar calendar: year {year}{(era.Length > 0 ? $" of the {era}" : "")} " +
                          $"(was {was}; advancement still judged as vanilla {cfg.EraYear})");

        // Every generated character is born StartYear-37 and the oldest generated history sits a
        // thousand years back, both floored at year 1. Under about a century those floors start
        // colliding and a whole generation shares one birth year.
        if (year < 120)
            Console.WriteLine(
                $"  ! The export's year ({year}) leaves little room before it. Births, reigns and\n" +
                "    remembered events will bunch up against year 1. Raising the export's\n" +
                "    'Era' year in Azgaar's options spreads them out again.");
    }

    /// <summary>The export's era, long form — "Balistow Era". Empty when it carries none.</summary>
    public string EraName => World.Settings.Options.Era;

    /// <summary>The same, abbreviated — "BE". What a date is suffixed with.</summary>
    public string EraShort => World.Settings.Options.EraShort;

    /// <summary>
    /// Compares the imported land against ours and says so. Called as soon as the heightmap's land
    /// mask exists, which is before anything has been built on either; see
    /// <see cref="AzgaarRaster.CheckAlignment"/> for why this matters.
    /// </summary>
    public AzgaarRaster.Alignment CheckAlignment(byte[] landMask)
    {
        var alignment = Raster.CheckAlignment(landMask);
        Alignment = alignment;

        Console.WriteLine($"  azgaar alignment: {100 * alignment.LandAgreement:F1}% of pixels agree " +
                          $"on land vs sea (azgaar {100 * alignment.AzgaarLandShare:F1}% land, " +
                          $"ours {100 * alignment.OurLandShare:F1}%)");

        if (!alignment.LooksAligned)
            Console.WriteLine(
                "  ! The export and the heightmap do not describe the same view of the same map.\n" +
                "    Names and borders will land in the wrong places, often in the sea.\n" +
                "    The usual cause is a heightmap exported cropped, zoomed, or at a different\n" +
                "    aspect ratio than the JSON's canvas. Re-export both from the same unzoomed view.");

        return alignment;
    }

    // --- Names -----------------------------------------------------------------------------------

    /// <summary>
    /// The Markov generator for one of the export's name bases, built on first use and kept.
    ///
    /// Cached because building a chain walks a few hundred corpus words, and every culture on the
    /// map asks for the same handful of bases.
    /// </summary>
    public AzgaarNames? NamesForBase(int baseIndex)
    {
        if (_namesByBase.TryGetValue(baseIndex, out var cached)) return cached;

        var source = World.NameBases.FirstOrDefault(b => b.I == baseIndex)
                  ?? (baseIndex >= 0 && baseIndex < World.NameBases.Count ? World.NameBases[baseIndex] : null);

        var names = AzgaarNames.FromBase(source);
        _namesByBase[baseIndex] = names;
        return names;
    }

    /// <summary>The generator a given Azgaar culture names its people and places from.</summary>
    public AzgaarNames? NamesForCulture(int cultureId)
        => World.Culture(cultureId) is { } culture ? NamesForBase(culture.Base) : null;

    // --- Binding ---------------------------------------------------------------------------------

    public AzgaarBinding? For(Title title)
        => _bindings.GetValueOrDefault(title);

    /// <summary>
    /// The dominant Azgaar object across a group of titles that share no common parent — the
    /// counties of a culture, say, which are scattered across whatever realms happen to speak it.
    ///
    /// Weighted by area rather than by title count, so one enormous frontier county does not get
    /// the same say as a dense cluster of small ones.
    /// </summary>
    public AzgaarShare Across(IEnumerable<Title> titles, Func<AzgaarBinding, AzgaarShare> pick)
    {
        var totals = new Dictionary<int, double>();
        double area = 0;

        foreach (var title in titles)
        {
            if (For(title) is not { } binding) continue;

            area += binding.Pixels;
            var share = pick(binding);
            if (!share.Exists) continue;

            totals[share.Id] = totals.GetValueOrDefault(share.Id) + share.Share * binding.Pixels;
        }

        if (totals.Count == 0 || area <= 0) return AzgaarShare.None;

        var best = totals.OrderByDescending(t => t.Value).ThenBy(t => t.Key).First();
        return new AzgaarShare(best.Key, best.Value / area);
    }

    /// <summary>
    /// Works out which Azgaar objects cover which of our titles.
    ///
    /// Done by counting pixels rather than by sampling each title's centre. A county wrapped around
    /// a bay has its centroid in the water and a county straddling a border takes its name from
    /// whichever side happens to own the middle pixel; area is the measure a person reading the map
    /// would use, and it costs one pass.
    ///
    /// The counting is over Azgaar *cells*, not over states and cultures separately. A cell already
    /// determines all five attributes, so one tally per barony answers every question, and the
    /// higher tiers are then summed from their children instead of rescanning the raster — which is
    /// what keeps an eight-million-pixel map to a single pass.
    /// </summary>
    /// <summary>
    /// Reads the province raster once and keeps the result, or returns what a previous call kept.
    ///
    /// Idempotent against a given province map, which is what lets both the hierarchy plan and the
    /// binding ask for it without either having to know whether the other ran first.
    /// </summary>
    private ProvinceSurvey Survey(ProvinceMap provinces, int[] order, int baronyCount)
    {
        if (_survey is { } cached && cached.BaronyCount == baronyCount) return cached;

        // Every province, not only the land baronies: the water ones carry the river and sea names.
        int maxId = provinces.Count;
        var tally = new Dictionary<int, int>?[maxId + 1];
        var sumX = new double[maxId + 1];
        var sumY = new double[maxId + 1];
        var pixels = new int[maxId + 1];

        var label = provinces.Label;
        int width = Raster.Width;
        int count = Math.Min(label.Length, Raster.CellByPixel.Length);

        for (int p = 0; p < count; p++)
        {
            int id = order[label[p]];
            if (id < 1 || id > maxId) continue;

            pixels[id]++;
            sumX[id] += (p % width + 0.5) * Raster.ScaleX;
            sumY[id] += (p / width + 0.5) * Raster.ScaleY;

            int cell = Raster.CellByPixel[p];
            if (cell < 0) continue;

            var cells = tally[id] ??= [];
            cells[cell] = cells.GetValueOrDefault(cell) + 1;
        }

        // Burgs land in whichever barony holds the pixel they sit on. A point, not a vote — a burg
        // is at a place, and the place is the answer.
        var burgs = new Dictionary<int, List<AzgaarBurg>>();
        foreach (var burg in World.RealBurgs)
        {
            int pixel = Raster.PixelAt(burg.X, burg.Y);
            if (pixel < 0 || pixel >= label.Length) continue;

            int id = order[label[pixel]];
            if (id < 1 || id > baronyCount) continue;

            if (!burgs.TryGetValue(id, out var list)) burgs[id] = list = [];
            list.Add(burg);
        }

        // Which state each barony belongs to, by weight of Azgaar cells rather than by its centre:
        // a barony straddling a border belongs to whichever state most of it is in, which is the
        // best answer available until the province partition itself respects those borders.
        var stateByBarony = new int[baronyCount + 1];
        var provinceByBarony = new int[baronyCount + 1];
        for (int id = 1; id <= baronyCount; id++)
        {
            if (tally[id] is not { Count: > 0 } cells) continue;
            stateByBarony[id] = Winner(cells, pixels[id], c => c.State).Id;
            provinceByBarony[id] = Winner(cells, pixels[id], c => c.Province).Id;
        }

        _cellsByProvince = tally;
        return _survey = new ProvinceSurvey
        {
            Cells = tally,
            SumX = sumX,
            SumY = sumY,
            Pixels = pixels,
            Burgs = burgs,
            BaronyCount = baronyCount,
            StateByBarony = stateByBarony,
            ProvinceByBarony = provinceByBarony,
        };
    }

    /// <summary>
    /// The Azgaar state a land barony mostly sits in, or 0 for unclaimed ground. Valid once
    /// <see cref="PlanHierarchy"/> has run, which is always before the hierarchy is built.
    /// </summary>
    public int StateOfBarony(int provinceId)
        => _survey is { } s && provinceId >= 1 && provinceId < s.StateByBarony.Length
            ? s.StateByBarony[provinceId] : 0;

    /// <summary>The same for Azgaar's provinces, which are what our counties are cut from.</summary>
    public int ProvinceOfBarony(int provinceId)
        => _survey is { } s && provinceId >= 1 && provinceId < s.ProvinceByBarony.Length
            ? s.ProvinceByBarony[provinceId] : 0;

    /// <summary>
    /// Works out what rank each state can hold on this map and says so. Run before the title
    /// hierarchy, because in a later tier it is what the hierarchy is built from.
    /// </summary>
    public HierarchyPlan? PlanHierarchy(ProvinceMap provinces, int[] order, int baronyCount)
    {
        if (!World.HasCells) return null;

        var survey = Survey(provinces, order, baronyCount);

        var byState = new Dictionary<int, int>();
        var byProvince = new Dictionary<int, int>();
        int unclaimed = 0;

        for (int id = 1; id <= baronyCount; id++)
        {
            int state = survey.StateByBarony[id];
            if (state == 0) unclaimed++;
            else byState[state] = byState.GetValueOrDefault(state) + 1;

            int province = survey.ProvinceByBarony[id];
            if (province > 0) byProvince[province] = byProvince.GetValueOrDefault(province) + 1;
        }

        var plan = HierarchyPlan.Build(World, byState, byProvince, baronyCount, unclaimed,
                                       Config.MinChildrenPerTitle);
        plan.Report(Config, Alignment?.OurLandShare ?? 0);
        return Plan = plan;
    }

    public void Bind(List<Title> empires, ProvinceMap provinces, int[] order, int baronyCount)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var all = Titles.Flatten(empires).ToList();
        _bindings = new Dictionary<Title, AzgaarBinding>(all.Count);

        if (!World.HasCells)
        {
            Console.WriteLine("  azgaar binding: skipped, the export has no cell data");
            return;
        }

        var survey = Survey(provinces, order, baronyCount);
        var tally = survey.Cells;
        var sumX = survey.SumX;
        var sumY = survey.SumY;
        var pixels = survey.Pixels;
        var burgsByProvince = survey.Burgs;
        int maxId = provinces.Count;

        // Baronies bind from their own tally; every tier above sums its children's, so the raster
        //    is read once no matter how deep the hierarchy goes.
        var cellsOf = new Dictionary<Title, Dictionary<int, int>>(all.Count);
        var burgsOf = new Dictionary<Title, List<AzgaarBurg>>(all.Count);
        var pixelsOf = new Dictionary<Title, int>(all.Count);
        var centreOf = new Dictionary<Title, (double X, double Y)>(all.Count);

        foreach (var root in empires) Collect(root);

        foreach (var title in all) _bindings[title] = Describe(title);

        int bound = _bindings.Values.Count(b => b.State.Exists);
        Console.WriteLine($"  azgaar binding: {bound} of {all.Count} titles fall inside a state, " +
                          $"{burgsByProvince.Values.Sum(b => b.Count)} burgs placed ({sw.ElapsedMilliseconds} ms)");

        (Dictionary<int, int> Cells, List<AzgaarBurg> Burgs, int Pixels, double X, double Y) Collect(Title title)
        {
            Dictionary<int, int> cells;
            List<AzgaarBurg> burgs;
            int area;
            double x, y;

            if (title.Children.Count == 0)
            {
                int id = title.ProvinceId;
                cells = id >= 1 && id <= baronyCount ? tally[id] ?? [] : [];
                burgs = id >= 1 && burgsByProvince.TryGetValue(id, out var found) ? found : [];
                area = id >= 1 && id <= baronyCount ? pixels[id] : 0;
                x = id >= 1 && id <= baronyCount ? sumX[id] : 0;
                y = id >= 1 && id <= baronyCount ? sumY[id] : 0;
            }
            else
            {
                cells = [];
                burgs = [];
                area = 0;
                x = y = 0;

                foreach (var child in title.Children)
                {
                    var part = Collect(child);
                    foreach (var (cell, votes) in part.Cells)
                        cells[cell] = cells.GetValueOrDefault(cell) + votes;

                    burgs.AddRange(part.Burgs);
                    area += part.Pixels;
                    x += part.X;
                    y += part.Y;
                }
            }

            cellsOf[title] = cells;
            burgsOf[title] = burgs;
            pixelsOf[title] = area;
            centreOf[title] = (x, y);
            return (cells, burgs, area, x, y);
        }

        AzgaarBinding Describe(Title title)
        {
            var cells = cellsOf[title];
            int area = pixelsOf[title];
            var (x, y) = centreOf[title];

            var burgs = burgsOf[title]
                .OrderByDescending(b => b.IsCapital)
                .ThenByDescending(b => b.Population)
                .ThenBy(b => b.I)
                .ToList();

            return new AzgaarBinding
            {
                State = Winner(cells, area, c => c.State),
                Province = Winner(cells, area, c => c.Province),
                Culture = Winner(cells, area, c => c.Culture),
                Religion = Winner(cells, area, c => c.Religion),
                Biome = Winner(cells, area, c => c.Biome),
                Pixels = area,
                Burgs = burgs,
                Centre = area > 0 ? (x / area, y / area) : (0, 0),
            };
        }
    }

    // --- Water -----------------------------------------------------------------------------------

    /// <summary>
    /// The Azgaar river a run of our river provinces mostly follows, or null.
    ///
    /// Our rivers and Azgaar's are traced by different code from different elevation data, so they
    /// agree on where the water broadly is and not on any individual pixel. Taking the whole system
    /// at once and asking which named river most of it overlaps is robust to that; asking province
    /// by province produces one river wearing four names down its length.
    /// </summary>
    public AzgaarRiver? RiverFor(IEnumerable<int> provinceIds)
    {
        var winner = TallyAcross(provinceIds, c => c.R);
        if (winner <= 0) return null;

        var river = World.Pack.Rivers.FirstOrDefault(r => r.I == winner);
        return string.IsNullOrWhiteSpace(river?.Name) ? null : river;
    }

    /// <summary>
    /// The named sea, ocean or lake a group of our water provinces mostly sits in, or null.
    /// Unnamed features are skipped rather than reported, since an unnamed ocean tells us nothing
    /// our own naming does not already do better.
    /// </summary>
    public AzgaarFeature? WaterBodyFor(IEnumerable<int> provinceIds)
    {
        var winner = TallyAcross(provinceIds, c => c.IsLand ? 0 : c.F);
        if (winner <= 0) return null;

        var feature = World.Pack.Features.FirstOrDefault(f => f.I == winner);
        return string.IsNullOrWhiteSpace(feature?.Name) ? null : feature;
    }

    /// <summary>The commonest non-zero value of an attribute across several of our provinces.</summary>
    private int TallyAcross(IEnumerable<int> provinceIds, Func<AzgaarCell, int> attribute)
    {
        if (_cellsByProvince.Length == 0) return 0;

        var totals = new Dictionary<int, int>();
        foreach (int id in provinceIds)
        {
            if (id < 0 || id >= _cellsByProvince.Length) continue;
            if (_cellsByProvince[id] is not { } cells) continue;

            foreach (var (cellIndex, votes) in cells)
            {
                if (cellIndex < 0 || cellIndex >= World.Pack.Cells.Count) continue;

                int value = attribute(World.Pack.Cells[cellIndex]);
                if (value <= 0) continue;

                totals[value] = totals.GetValueOrDefault(value) + votes;
            }
        }

        if (totals.Count == 0) return 0;
        return totals.OrderByDescending(t => t.Value).ThenBy(t => t.Key).First().Key;
    }

    /// <summary>
    /// Groups a cell tally by one of the cell's attributes and returns the largest group, as a
    /// share of the title's whole area rather than of the cells that had an opinion.
    ///
    /// Measuring against the whole area is what makes the share mean something: a coastal county
    /// that is four-fifths sea would otherwise report a state covering "100%" of it on the strength
    /// of the one inland cell that carried a state id at all.
    /// </summary>
    private AzgaarShare Winner(Dictionary<int, int> cells, int area, Func<AzgaarCell, int> attribute)
    {
        if (cells.Count == 0 || area == 0) return AzgaarShare.None;

        var totals = new Dictionary<int, int>();
        foreach (var (cellIndex, votes) in cells)
        {
            if (cellIndex < 0 || cellIndex >= World.Pack.Cells.Count) continue;

            int value = attribute(World.Pack.Cells[cellIndex]);
            if (value <= 0) continue;

            totals[value] = totals.GetValueOrDefault(value) + votes;
        }

        if (totals.Count == 0) return AzgaarShare.None;

        // Ties break on the lower id, so the same export always imports the same way.
        var best = totals.OrderByDescending(t => t.Value).ThenBy(t => t.Key).First();
        return new AzgaarShare(best.Key, (double)best.Value / area);
    }
}
