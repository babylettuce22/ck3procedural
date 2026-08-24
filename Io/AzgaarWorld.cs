using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ck3MapGen.Io;

/// <summary>
/// A world exported from Azgaar's Fantasy Map Generator, as its "Full" JSON export writes it.
///
/// This models the *whole* export rather than the part the importer currently reads. The extra
/// fields cost nothing to parse and everything to add later: the political and cultural layers
/// (states, provinces, diplomacy, campaigns, religion ancestry) are the point of the integration,
/// and having them already sitting in the model is the difference between "wire up the next layer"
/// and "go re-learn the schema".
///
/// Field names are Azgaar's own single letters. They are unpleasant and they are also the contract
/// — renaming them here would only move the confusion to the attribute above each one — so the
/// short name stays and the summary says what it means.
///
/// Two shapes in the export do not follow their own array's type, and both have bitten every
/// importer that has ever read this format:
///
///   * <c>pack.features[0]</c> is the number <c>0</c>, not a feature. Every array is therefore read
///     element by element and non-objects are skipped; see <see cref="AzgaarJson.ReadArray{T}"/>.
///   * <c>pack.states[0].diplomacy</c> is <c>string[][]</c> — the war chronicle — where every other
///     state's is <c>string[]</c>. Hence <see cref="AzgaarState.Diplomacy"/> being a raw element
///     with two typed accessors rather than an array.
///
/// Index 0 of <c>states</c>, <c>cultures</c>, <c>religions</c> and <c>provinces</c> is a sentinel
/// ("Neutrals", "Wildlands", "No religion", "Province 0") standing for unassigned land, not a real
/// object. <see cref="AzgaarWorld.RealStates"/> and friends filter it out along with anything
/// flagged <c>removed</c>.
/// </summary>
public sealed class AzgaarWorld
{
    [JsonPropertyName("info")] public AzgaarInfo Info { get; set; } = new();
    [JsonPropertyName("settings")] public AzgaarSettings Settings { get; set; } = new();

    /// <summary>Geographic extent, for converting canvas x/y to longitude/latitude.</summary>
    [JsonPropertyName("mapCoordinates")] public AzgaarCoordinates? MapCoordinates { get; set; }

    /// <summary>The packed graph: the one that carries every political and cultural assignment.</summary>
    [JsonPropertyName("pack")] public AzgaarPack Pack { get; set; } = new();

    /// <summary>
    /// The unpacked background grid. Absent from the "Minimal" export. Worth keeping because it is
    /// the only place temperature and precipitation live — <see cref="AzgaarGridCell.Temp"/> and
    /// <see cref="AzgaarGridCell.Prec"/> are what a later tier would hand to our climate model in
    /// place of the one we simulate.
    /// </summary>
    [JsonPropertyName("grid")] public AzgaarGrid? Grid { get; set; }

    /// <summary>Legend prose, keyed by object ("burg12", "state3", "religion7").</summary>
    [JsonPropertyName("notes")] public List<AzgaarNote> Notes { get; set; } = [];

    /// <summary>The name-generator corpora. See <see cref="MapGen.AzgaarNames"/>.</summary>
    [JsonPropertyName("nameBases")] public List<AzgaarNameBase> NameBases { get; set; } = [];

    // --- Convenience views -------------------------------------------------------------------

    /// <summary>Real states, with the "Neutrals" sentinel at index 0 and removed states dropped.</summary>
    public IEnumerable<AzgaarState> RealStates
        => Pack.States.Where(s => s.I > 0 && !s.Removed);

    /// <summary>Real cultures, with the "Wildlands" sentinel at index 0 and removed ones dropped.</summary>
    public IEnumerable<AzgaarCulture> RealCultures
        => Pack.Cultures.Where(c => c.I > 0 && !c.Removed);

    /// <summary>Real religions, with the "No religion" sentinel at index 0 and removed ones dropped.</summary>
    public IEnumerable<AzgaarReligion> RealReligions
        => Pack.Religions.Where(r => r.I > 0 && !r.Removed);

    /// <summary>Real provinces, with the index-0 sentinel and removed ones dropped.</summary>
    public IEnumerable<AzgaarProvince> RealProvinces
        => Pack.Provinces.Where(p => p.I > 0 && !p.Removed);

    /// <summary>Real burgs. Burg 0 is the "no burg" sentinel and has no name.</summary>
    public IEnumerable<AzgaarBurg> RealBurgs
        => Pack.Burgs.Where(b => b.I > 0 && !b.Removed);

    /// <summary>True when the export carried per-cell data, i.e. it was "Full" and not "Minimal".</summary>
    public bool HasCells => Pack.Cells.Count > 0;

    public AzgaarState? State(int i) => i > 0 && i < Pack.States.Count ? Pack.States[i] : null;
    public AzgaarCulture? Culture(int i) => i > 0 && i < Pack.Cultures.Count ? Pack.Cultures[i] : null;
    public AzgaarReligion? Religion(int i) => i > 0 && i < Pack.Religions.Count ? Pack.Religions[i] : null;
    public AzgaarProvince? Province(int i) => i > 0 && i < Pack.Provinces.Count ? Pack.Provinces[i] : null;
    public AzgaarBurg? Burg(int i) => i > 0 && i < Pack.Burgs.Count ? Pack.Burgs[i] : null;

    /// <summary>
    /// The legend text attached to an object, or null. Azgaar keys these as the object kind
    /// followed by its index, with no separator — "burg12", "state3", "religion7", "culture2".
    /// </summary>
    public string? NoteFor(string kind, int index)
        => Notes.FirstOrDefault(n => n.Id == $"{kind}{index}")?.Legend;
}

public sealed class AzgaarInfo
{
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("exportedAt")] public string ExportedAt { get; set; } = "";
    [JsonPropertyName("mapName")] public string MapName { get; set; } = "";

    /// <summary>Canvas width. Every x in <see cref="AzgaarCell.P"/> is on this scale.</summary>
    [JsonPropertyName("width")] public double Width { get; set; }

    /// <summary>Canvas height. Every y in <see cref="AzgaarCell.P"/> is on this scale.</summary>
    [JsonPropertyName("height")] public double Height { get; set; }

    [JsonPropertyName("seed")] public string Seed { get; set; } = "";
    [JsonPropertyName("mapId")] public double MapId { get; set; }
}

public sealed class AzgaarSettings
{
    [JsonPropertyName("distanceUnit")] public string DistanceUnit { get; set; } = "";
    [JsonPropertyName("distanceScale")] public double DistanceScale { get; set; } = 1;
    [JsonPropertyName("areaUnit")] public string AreaUnit { get; set; } = "";
    [JsonPropertyName("heightUnit")] public string HeightUnit { get; set; } = "";
    [JsonPropertyName("temperatureScale")] public string TemperatureScale { get; set; } = "";
    [JsonPropertyName("populationRate")] public double PopulationRate { get; set; } = 1000;
    [JsonPropertyName("urbanization")] public double Urbanization { get; set; } = 1;
    [JsonPropertyName("urbanDensity")] public double UrbanDensity { get; set; }
    [JsonPropertyName("mapName")] public string MapName { get; set; } = "";

    /// <summary>
    /// Generation options. The one that matters downstream is <c>year</c>: every campaign date in
    /// <see cref="AzgaarCampaign"/> is an absolute year on this era's calendar, and turning those
    /// into CK3 dates means knowing what "now" was when the map was made.
    /// </summary>
    [JsonPropertyName("options")] public AzgaarOptions Options { get; set; } = new();
}

public sealed class AzgaarOptions
{
    /// <summary>The map's present year. Campaigns run backwards from here.</summary>
    [JsonPropertyName("year")] public int Year { get; set; }

    [JsonPropertyName("era")] public string Era { get; set; } = "";
    [JsonPropertyName("eraShort")] public string EraShort { get; set; } = "";
}

/// <summary>Latitude and longitude of the canvas edges, for a map that is a piece of a globe.</summary>
public sealed class AzgaarCoordinates
{
    [JsonPropertyName("latT")] public double LatT { get; set; }
    [JsonPropertyName("latN")] public double LatN { get; set; }
    [JsonPropertyName("latS")] public double LatS { get; set; }
    [JsonPropertyName("lonT")] public double LonT { get; set; }
    [JsonPropertyName("lonW")] public double LonW { get; set; }
    [JsonPropertyName("lonE")] public double LonE { get; set; }
}

public sealed class AzgaarPack
{
    [JsonPropertyName("cells")] public List<AzgaarCell> Cells { get; set; } = [];
    [JsonPropertyName("features")] public List<AzgaarFeature> Features { get; set; } = [];
    [JsonPropertyName("biomes")] public AzgaarBiomes? Biomes { get; set; }
    [JsonPropertyName("cultures")] public List<AzgaarCulture> Cultures { get; set; } = [];
    [JsonPropertyName("burgs")] public List<AzgaarBurg> Burgs { get; set; } = [];
    [JsonPropertyName("states")] public List<AzgaarState> States { get; set; } = [];
    [JsonPropertyName("provinces")] public List<AzgaarProvince> Provinces { get; set; } = [];
    [JsonPropertyName("religions")] public List<AzgaarReligion> Religions { get; set; } = [];
    [JsonPropertyName("rivers")] public List<AzgaarRiver> Rivers { get; set; } = [];
    [JsonPropertyName("markers")] public List<AzgaarMarker> Markers { get; set; } = [];
    [JsonPropertyName("routes")] public List<AzgaarRoute> Routes { get; set; } = [];
    [JsonPropertyName("zones")] public List<AzgaarZone> Zones { get; set; } = [];
}

/// <summary>
/// One Voronoi cell of the packed graph, and the reason no polygon code is needed anywhere in this
/// importer: <see cref="P"/> is the cell's Voronoi *site*, so nearest-site lookup over the sites
/// reproduces the partition exactly. See <see cref="MapGen.AzgaarRaster"/>.
/// </summary>
public sealed class AzgaarCell
{
    [JsonPropertyName("i")] public int I { get; set; }

    /// <summary>The cell's site, as [x, y] on the canvas.</summary>
    [JsonPropertyName("p")] public double[] P { get; set; } = [];

    /// <summary>Indices of adjacent cells.</summary>
    [JsonPropertyName("c")] public int[] C { get; set; } = [];

    /// <summary>Indices into the vertex table.</summary>
    [JsonPropertyName("v")] public int[] V { get; set; } = [];

    /// <summary>Index of the matching cell on the background grid — the way to reach temp/prec.</summary>
    [JsonPropertyName("g")] public int G { get; set; }

    /// <summary>Height, 0-100. Azgaar puts sea level at 20.</summary>
    [JsonPropertyName("h")] public int H { get; set; }

    [JsonPropertyName("area")] public double Area { get; set; }

    /// <summary>Index into <see cref="AzgaarPack.Features"/> — the landmass, lake or ocean.</summary>
    [JsonPropertyName("f")] public int F { get; set; }

    /// <summary>Distance field: 1 for coastline, 2 for one cell inland, -1 for coastal water.</summary>
    [JsonPropertyName("t")] public int T { get; set; }

    /// <summary>The water cell a coastal land cell would put a harbour on.</summary>
    [JsonPropertyName("haven")] public int Haven { get; set; }

    /// <summary>How many water features touch this cell. 1 is a sheltered harbour.</summary>
    [JsonPropertyName("harbor")] public int Harbor { get; set; }

    /// <summary>Water flux, for river tracing.</summary>
    [JsonPropertyName("fl")] public double Fl { get; set; }

    /// <summary>Index into <see cref="AzgaarPack.Rivers"/>, or 0.</summary>
    [JsonPropertyName("r")] public int R { get; set; }

    [JsonPropertyName("conf")] public double Conf { get; set; }

    /// <summary>Index into the biome table.</summary>
    [JsonPropertyName("biome")] public int Biome { get; set; }

    /// <summary>Suitability score, Azgaar's own habitability measure.</summary>
    [JsonPropertyName("s")] public double S { get; set; }

    /// <summary>Rural population, in population points (multiply by settings.populationRate).</summary>
    [JsonPropertyName("pop")] public double Pop { get; set; }

    [JsonPropertyName("culture")] public int Culture { get; set; }
    [JsonPropertyName("burg")] public int Burg { get; set; }
    [JsonPropertyName("state")] public int State { get; set; }
    [JsonPropertyName("religion")] public int Religion { get; set; }
    [JsonPropertyName("province")] public int Province { get; set; }

    public double X => P.Length > 0 ? P[0] : 0;
    public double Y => P.Length > 1 ? P[1] : 0;

    /// <summary>Azgaar's sea level is 20 on the 0-100 scale.</summary>
    public bool IsLand => H >= 20;
}

public sealed class AzgaarGrid
{
    [JsonPropertyName("cells")] public List<AzgaarGridCell> Cells { get; set; } = [];
    [JsonPropertyName("cellsX")] public int CellsX { get; set; }
    [JsonPropertyName("cellsY")] public int CellsY { get; set; }
    [JsonPropertyName("spacing")] public double Spacing { get; set; }
    [JsonPropertyName("seed")] public string Seed { get; set; } = "";
}

public sealed class AzgaarGridCell
{
    [JsonPropertyName("i")] public int I { get; set; }
    [JsonPropertyName("h")] public int H { get; set; }

    /// <summary>Mean temperature in degrees Celsius.</summary>
    [JsonPropertyName("temp")] public int Temp { get; set; }

    /// <summary>Precipitation, on Azgaar's own 0-255-ish scale rather than in millimetres.</summary>
    [JsonPropertyName("prec")] public int Prec { get; set; }
}

/// <summary>A landmass, lake or ocean. Named ones are what our water naming can borrow.</summary>
public sealed class AzgaarFeature
{
    [JsonPropertyName("i")] public int I { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }

    /// <summary>"ocean", "island" or "lake".</summary>
    [JsonPropertyName("type")] public string Type { get; set; } = "";

    /// <summary>For lakes: "freshwater", "salt", "dry", "sinkhole", "frozen", "lava".</summary>
    [JsonPropertyName("group")] public string Group { get; set; } = "";

    [JsonPropertyName("land")] public bool Land { get; set; }
    [JsonPropertyName("border")] public bool Border { get; set; }
    [JsonPropertyName("cells")] public int Cells { get; set; }
    [JsonPropertyName("area")] public double Area { get; set; }
}

/// <summary>
/// The biome table, in the columnar shape older Azgaar builds write it: parallel arrays indexed by
/// biome id. Newer builds write the same table as an array of objects instead — see
/// <see cref="AzgaarBiomeEntry"/> and <see cref="From"/>, which folds that shape into this one so
/// there is a single thing to read.
/// </summary>
public sealed class AzgaarBiomes
{
    [JsonPropertyName("i")] public int[] I { get; set; } = [];
    [JsonPropertyName("name")] public string[] Name { get; set; } = [];
    [JsonPropertyName("color")] public string[] Color { get; set; } = [];
    [JsonPropertyName("habitability")] public int[] Habitability { get; set; } = [];

    /// <summary>
    /// The same table built from the row-per-biome shape.
    ///
    /// Indexed by each entry's own <c>i</c> rather than by its position in the array, because the
    /// biome editor can leave gaps in the ids and <c>cells[].biome</c> stores the id, not the row.
    /// </summary>
    public static AzgaarBiomes From(IReadOnlyList<AzgaarBiomeEntry> rows)
    {
        int count = 0;
        foreach (var row in rows) count = Math.Max(count, row.I + 1);

        var table = new AzgaarBiomes
        {
            I = new int[count],
            Name = new string[count],
            Color = new string[count],
            Habitability = new int[count],
        };

        for (int i = 0; i < count; i++) { table.I[i] = i; table.Name[i] = ""; table.Color[i] = ""; }

        foreach (var row in rows)
        {
            if (row.I < 0 || row.I >= count) continue;
            table.Name[row.I] = row.Name ?? "";
            table.Color[row.I] = row.Color ?? "";
            table.Habitability[row.I] = row.Habitability;
        }

        return table;
    }
}

/// <summary>One row of the biome table, as newer Azgaar builds write it.</summary>
public sealed class AzgaarBiomeEntry
{
    [JsonPropertyName("i")] public int I { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("color")] public string? Color { get; set; }
    [JsonPropertyName("habitability")] public int Habitability { get; set; }
}

/// <summary>
/// A culture.
///
/// <see cref="Origins"/> is the ancestry DAG Azgaar draws its culture tree from, and where an author
/// has drawn one by hand it maps directly onto the heritage-then-culture nesting our own generator
/// otherwise invents. It is not, however, the field the import can lean on: Azgaar's *generator*
/// writes <c>[0]</c> — descended from Wildlands — for every culture it makes, so on a generated
/// export the DAG is empty and says only that nothing is related to anything.
/// <see cref="MapGen.AzgaarFamilies"/> is where that is handled, and <see cref="Base"/> is what
/// carries the grouping when this does not.
/// </summary>
public sealed class AzgaarCulture
{
    [JsonPropertyName("i")] public int I { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";

    /// <summary>Index into <see cref="AzgaarWorld.NameBases"/> — the corpus this culture names from.</summary>
    [JsonPropertyName("base")] public int Base { get; set; }

    /// <summary>Short code, two or three letters.</summary>
    [JsonPropertyName("code")] public string? Code { get; set; }

    /// <summary>"Generic", "Hunting", "Highland", "River", "Lake", "Naval" or "Nomadic".</summary>
    [JsonPropertyName("type")] public string Type { get; set; } = "Generic";

    [JsonPropertyName("color")] public string? Color { get; set; }
    [JsonPropertyName("shield")] public string Shield { get; set; } = "";
    [JsonPropertyName("expansionism")] public double Expansionism { get; set; }

    /// <summary>Cell index of the culture's origin point.</summary>
    [JsonPropertyName("center")] public int Center { get; set; }

    /// <summary>Ids of the cultures this one descends from. Nulls appear and mean "no parent".</summary>
    [JsonPropertyName("origins")] public int?[] Origins { get; set; } = [];

    [JsonPropertyName("cells")] public int Cells { get; set; }
    [JsonPropertyName("area")] public double Area { get; set; }
    [JsonPropertyName("rural")] public double Rural { get; set; }
    [JsonPropertyName("urban")] public double Urban { get; set; }
    [JsonPropertyName("removed")] public bool Removed { get; set; }
}

/// <summary>
/// A religion. <see cref="Type"/> and <see cref="Origins"/> together give us CK3's whole religious
/// structure for free: Organized religions are religion heads with faiths under them, Heresies hang
/// off their parent, Folk faiths are the pagan layer.
/// </summary>
public sealed class AzgaarReligion
{
    [JsonPropertyName("i")] public int I { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("code")] public string? Code { get; set; }

    /// <summary>"Folk", "Organized", "Cult" or "Heresy".</summary>
    [JsonPropertyName("type")] public string Type { get; set; } = "";

    /// <summary>The tradition word — "Druidism", "Shamanism", "Monotheism" and so on.</summary>
    [JsonPropertyName("form")] public string Form { get; set; } = "";

    /// <summary>The supreme deity's name, when the form has one.</summary>
    [JsonPropertyName("deity")] public string? Deity { get; set; }

    /// <summary>"global", "culture" or "state" — how far it was allowed to spread.</summary>
    [JsonPropertyName("expansion")] public string Expansion { get; set; } = "";

    [JsonPropertyName("expansionism")] public double Expansionism { get; set; }
    [JsonPropertyName("culture")] public int Culture { get; set; }
    [JsonPropertyName("center")] public int Center { get; set; }
    [JsonPropertyName("color")] public string? Color { get; set; }

    /// <summary>Ids of the religions this one descends from.</summary>
    [JsonPropertyName("origins")] public int[]? Origins { get; set; }

    [JsonPropertyName("cells")] public int Cells { get; set; }
    [JsonPropertyName("area")] public double Area { get; set; }
    [JsonPropertyName("rural")] public double Rural { get; set; }
    [JsonPropertyName("urban")] public double Urban { get; set; }
    [JsonPropertyName("removed")] public bool Removed { get; set; }
}

/// <summary>
/// A sovereign state — the closest thing Azgaar has to a CK3 realm.
///
/// <see cref="Diplomacy"/> and <see cref="Campaigns"/> are the only relational and chronological
/// data in the whole export, and between them they carry everything a later tier needs to seed
/// alliances, rivalries, claims and active wars.
/// </summary>
public sealed class AzgaarState
{
    [JsonPropertyName("i")] public int I { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";

    /// <summary>The name with its form — "Kingdom of Zwenland".</summary>
    [JsonPropertyName("fullName")] public string? FullName { get; set; }

    /// <summary>"Monarchy", "Republic", "Theocracy", "Union" or "Anarchy".</summary>
    [JsonPropertyName("form")] public string? Form { get; set; }

    /// <summary>
    /// How the state lives, inherited from its capital culture: "Generic", "Nomadic", "Hunting",
    /// "Highland", "Naval", "River" or "Lake".
    ///
    /// The only thing in the export that distinguishes a settled monarchy from a horde or a band of
    /// hunters — <see cref="Form"/> calls all three Monarchy — which is what makes it worth reading.
    /// </summary>
    [JsonPropertyName("type")] public string StateType { get; set; } = "Generic";

    /// <summary>The specific form — "Kingdom", "Grand Duchy", "Republic", "Khanate".</summary>
    [JsonPropertyName("formName")] public string? FormName { get; set; }

    /// <summary>Burg id of the capital.</summary>
    [JsonPropertyName("capital")] public int Capital { get; set; }

    [JsonPropertyName("culture")] public int Culture { get; set; }
    [JsonPropertyName("center")] public int Center { get; set; }
    [JsonPropertyName("color")] public string? Color { get; set; }
    [JsonPropertyName("expansionism")] public double Expansionism { get; set; }
    [JsonPropertyName("neighbors")] public int[] Neighbors { get; set; } = [];
    [JsonPropertyName("provinces")] public int[] Provinces { get; set; } = [];
    [JsonPropertyName("cells")] public int Cells { get; set; }
    [JsonPropertyName("area")] public double Area { get; set; }
    [JsonPropertyName("burgs")] public int Burgs { get; set; }
    [JsonPropertyName("rural")] public double Rural { get; set; }
    [JsonPropertyName("urban")] public double Urban { get; set; }

    /// <summary>How alarmed this state is — Azgaar's war alert level.</summary>
    [JsonPropertyName("alert")] public double Alert { get; set; }

    [JsonPropertyName("military")] public List<AzgaarRegiment> Military { get; set; } = [];

    /// <summary>Past and ongoing wars. A campaign with no <c>end</c> is still being fought.</summary>
    [JsonPropertyName("campaigns")] public List<AzgaarCampaign> Campaigns { get; set; } = [];

    [JsonPropertyName("removed")] public bool Removed { get; set; }

    /// <summary>
    /// Raw, because this field has two types. For every real state it is <c>string[]</c>, one
    /// relation per state index. For the index-0 "Neutrals" sentinel it is <c>string[][]</c> — the
    /// war chronicle, each entry a list of prose lines about one war. Read it through
    /// <see cref="Relations"/> or <see cref="Chronicle"/>, never directly.
    /// </summary>
    [JsonPropertyName("diplomacy")] public JsonElement Diplomacy { get; set; }

    /// <summary>
    /// This state's relation to every other, indexed by state id: "Ally", "Friendly", "Neutral",
    /// "Suspicion", "Rival", "Enemy", "Unknown", "Vassal", "Suzerain", or "x" against itself.
    /// Empty for the Neutrals sentinel, whose diplomacy field is the chronicle instead.
    /// </summary>
    public string[] Relations
    {
        get
        {
            if (Diplomacy.ValueKind != JsonValueKind.Array) return [];

            var result = new List<string>();
            foreach (var entry in Diplomacy.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.String) return [];
                result.Add(entry.GetString() ?? "");
            }
            return [.. result];
        }
    }

    /// <summary>
    /// The war chronicle, present only on the index-0 sentinel. Each entry is one war, as the lines
    /// of prose Azgaar wrote about it — who declared on whom, which allies joined, who backed out.
    /// </summary>
    public List<string[]> Chronicle
    {
        get
        {
            var result = new List<string[]>();
            if (Diplomacy.ValueKind != JsonValueKind.Array) return result;

            foreach (var war in Diplomacy.EnumerateArray())
            {
                if (war.ValueKind != JsonValueKind.Array) continue;

                var lines = new List<string>();
                foreach (var line in war.EnumerateArray())
                    if (line.ValueKind == JsonValueKind.String) lines.Add(line.GetString() ?? "");

                if (lines.Count > 0) result.Add([.. lines]);
            }
            return result;
        }
    }
}

/// <summary>
/// A war. Azgaar's only dated record: <see cref="Start"/> and <see cref="End"/> are absolute years
/// on the calendar whose present is <see cref="AzgaarOptions.Year"/>. A campaign with no end is
/// still being fought at the map's present moment, which is exactly a CK3 start-date war.
/// </summary>
public sealed class AzgaarCampaign
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("start")] public int Start { get; set; }
    [JsonPropertyName("end")] public int? End { get; set; }

    /// <summary>State id of the aggressor.</summary>
    [JsonPropertyName("attacker")] public int Attacker { get; set; }

    /// <summary>State id of the defender.</summary>
    [JsonPropertyName("defender")] public int Defender { get; set; }

    /// <summary>True while the war is still running at the map's present year.</summary>
    public bool IsOngoing => End is null;
}

public sealed class AzgaarRegiment
{
    [JsonPropertyName("i")] public int I { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("cell")] public int Cell { get; set; }
    [JsonPropertyName("x")] public double X { get; set; }
    [JsonPropertyName("y")] public double Y { get; set; }
    [JsonPropertyName("a")] public double Total { get; set; }
    [JsonPropertyName("icon")] public string Icon { get; set; } = "";
}

/// <summary>An administrative division of a state — the natural source for a duchy's name.</summary>
public sealed class AzgaarProvince
{
    [JsonPropertyName("i")] public int I { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";

    /// <summary>The form word alone — "County", "Margrave", "Canton".</summary>
    [JsonPropertyName("formName")] public string? FormName { get; set; }

    /// <summary>Name and form together — "County of Aldmoor".</summary>
    [JsonPropertyName("fullName")] public string? FullName { get; set; }

    [JsonPropertyName("state")] public int State { get; set; }
    [JsonPropertyName("center")] public int Center { get; set; }

    /// <summary>Burg id of the provincial seat, or 0.</summary>
    [JsonPropertyName("burg")] public int Burg { get; set; }

    [JsonPropertyName("color")] public string? Color { get; set; }
    [JsonPropertyName("area")] public double Area { get; set; }
    [JsonPropertyName("rural")] public double Rural { get; set; }
    [JsonPropertyName("urban")] public double Urban { get; set; }
    [JsonPropertyName("burgs")] public int[]? Burgs { get; set; }
    [JsonPropertyName("removed")] public bool Removed { get; set; }
}

/// <summary>
/// A settlement. The building flags are not decoration — <see cref="Citadel"/>, <see cref="Walls"/>
/// and <see cref="Temple"/> say what kind of holding this ought to be, and
/// <see cref="Population"/> is a development number waiting to be scaled.
/// </summary>
public sealed class AzgaarBurg
{
    [JsonPropertyName("i")] public int I { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("cell")] public int Cell { get; set; }
    [JsonPropertyName("x")] public double X { get; set; }
    [JsonPropertyName("y")] public double Y { get; set; }
    [JsonPropertyName("state")] public int State { get; set; }
    [JsonPropertyName("culture")] public int Culture { get; set; }
    [JsonPropertyName("feature")] public int Feature { get; set; }

    /// <summary>Population in points; multiply by settings.populationRate for people.</summary>
    [JsonPropertyName("population")] public double Population { get; set; }

    /// <summary>1 when this is its state's capital.</summary>
    [JsonPropertyName("capital")] public int Capital { get; set; }

    /// <summary>Feature id of the water it trades on, or 0 when it is not a port.</summary>
    [JsonPropertyName("port")] public int Port { get; set; }

    [JsonPropertyName("citadel")] public int Citadel { get; set; }
    [JsonPropertyName("plaza")] public int Plaza { get; set; }
    [JsonPropertyName("walls")] public int Walls { get; set; }
    [JsonPropertyName("shanty")] public int Shanty { get; set; }
    [JsonPropertyName("temple")] public int Temple { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("removed")] public bool Removed { get; set; }

    public bool IsCapital => Capital != 0;
    public bool IsPort => Port != 0;
}

public sealed class AzgaarRiver
{
    [JsonPropertyName("i")] public int I { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }

    /// <summary>"River", "Creek", "Stream", "Branch" and so on.</summary>
    [JsonPropertyName("type")] public string? Type { get; set; }

    [JsonPropertyName("source")] public int Source { get; set; }
    [JsonPropertyName("mouth")] public int Mouth { get; set; }

    /// <summary>Id of the river this one flows into, or 0 for a main stem.</summary>
    [JsonPropertyName("parent")] public int Parent { get; set; }

    /// <summary>Id of the main stem of the whole system this river belongs to.</summary>
    [JsonPropertyName("basin")] public int Basin { get; set; }

    /// <summary>The cells the course runs through, in order from source to mouth.</summary>
    [JsonPropertyName("cells")] public int[] Cells { get; set; } = [];

    [JsonPropertyName("discharge")] public double Discharge { get; set; }
    [JsonPropertyName("length")] public double Length { get; set; }
    [JsonPropertyName("width")] public double Width { get; set; }
    [JsonPropertyName("sourceWidth")] public double SourceWidth { get; set; }

    /// <summary>True when nothing flows into this one — the head of its own system.</summary>
    public bool IsMainStem => Parent == 0 || Parent == I;
}

/// <summary>A point of interest. The natural source for wonders and artifacts in a later tier.</summary>
public sealed class AzgaarMarker
{
    [JsonPropertyName("i")] public int I { get; set; }
    [JsonPropertyName("icon")] public string Icon { get; set; } = "";
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("x")] public double X { get; set; }
    [JsonPropertyName("y")] public double Y { get; set; }
    [JsonPropertyName("cell")] public int Cell { get; set; }
}

public sealed class AzgaarRoute
{
    [JsonPropertyName("i")] public int I { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }

    /// <summary>"roads", "trails", "searoutes".</summary>
    [JsonPropertyName("group")] public string Group { get; set; } = "";

    [JsonPropertyName("feature")] public int Feature { get; set; }
    [JsonPropertyName("points")] public double[][]? Points { get; set; }
}

/// <summary>A tagged region — a plague, an invasion route, a disputed border.</summary>
public sealed class AzgaarZone
{
    [JsonPropertyName("i")] public int I { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("cells")] public int[] Cells { get; set; } = [];
    [JsonPropertyName("color")] public string? Color { get; set; }
}

/// <summary>Legend prose the map's author or generator attached to an object.</summary>
public sealed class AzgaarNote
{
    /// <summary>Object kind and index run together — "burg12", "state3".</summary>
    [JsonPropertyName("id")] public string Id { get; set; } = "";

    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("legend")] public string Legend { get; set; } = "";
}

/// <summary>
/// A name-generator corpus: a few hundred comma-separated example names that Azgaar builds a
/// Markov chain over. Importing these is what lets generated names for places Azgaar never named
/// still sound like they came off the same map. See <see cref="MapGen.AzgaarNames"/>.
/// </summary>
public sealed class AzgaarNameBase
{
    [JsonPropertyName("i")] public int I { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";

    /// <summary>Shortest name to generate.</summary>
    [JsonPropertyName("min")] public int Min { get; set; } = 5;

    /// <summary>Longest name to generate.</summary>
    [JsonPropertyName("max")] public int Max { get; set; } = 12;

    /// <summary>Letters allowed to appear doubled, as one string.</summary>
    [JsonPropertyName("d")] public string D { get; set; } = "";

    /// <summary>The corpus itself: names separated by commas.</summary>
    [JsonPropertyName("b")] public string B { get; set; } = "";
}
