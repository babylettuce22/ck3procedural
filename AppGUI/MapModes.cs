using Ck3MapGen.Core;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.AppGUI;

/// <summary>What a click on the map picks in an editable mode.</summary>
public enum MapPick { Title, Culture, Faith, Realm }

/// <summary>
/// One map mode: how it renders, where it lives in the strip, whether a click edits something,
/// and what the legend and hover readout should say.
///
/// The single source of truth. The strip, the click routing, the edit-invalidation and the legend
/// all read this record — before it existed they were four parallel tables in MainForm that had to
/// be kept in agreement by hand, and adding a mode meant finding all four.
/// </summary>
public sealed record MapMode(
    string Name,
    string Category,
    Func<GenerationResult, Emit.WrittenContent?, PreviewRenderer.Image> Render)
{
    /// <summary>
    /// Renders written content, so it means nothing until a mod has been written. The strip shows
    /// it dimmed until then — dimmed and explained rather than silently falling back to some other
    /// view, which is what these modes used to do and which made a placeholder indistinguishable
    /// from an answer.
    /// </summary>
    public bool AfterWrite { get; init; }

    /// <summary>
    /// Renders written content when there is one and a recomputation of it when there is not — so,
    /// unlike <see cref="AfterWrite"/>, it stays usable before a write, but what it shows until
    /// then is an estimate and the strip says so.
    ///
    /// The distinction is the point. These modes used to recompute unconditionally, ignoring the
    /// written world even once it existed, which made a guess indistinguishable from the answer in
    /// exactly the way <see cref="AfterWrite"/> was introduced to prevent.
    /// </summary>
    public bool Estimate { get; init; }

    /// <summary>Click-to-edit: what a click picks, and at which title tier it lands.</summary>
    public (MapPick Kind, string Tier)? Pick { get; init; }

    /// <summary>
    /// Which kind of pending edit stales this mode's cached render. Defaults to what it picks,
    /// which covers every editable mode; set it explicitly for a mode that paints from edited data
    /// without being clickable itself, like Ethnicities riding on cultures.
    /// </summary>
    public MapPick? Repaints { get; init; }

    public MapPick? RepaintKind => Repaints ?? Pick?.Kind;

    /// <summary>Colour key drawn under the strip. Null for modes whose palette is per-world.</summary>
    public IReadOnlyList<((byte R, byte G, byte B) Colour, string Label)>? Legend { get; init; }

    /// <summary>
    /// Mode-specific line for the hover readout, given the cell under the cursor (province-raster
    /// index) and the county there, if any. The shared part — county, duchy, kingdom — is built by
    /// the form; this adds what only the mode knows.
    /// </summary>
    public Func<GenerationResult, Emit.WrittenContent?, int, Title?, string?>? Probe { get; init; }

    public bool Clickable => Pick is not null;
}

public static class MapModes
{
    public static readonly string[] Categories = ["Physical", "Climate", "De Jure", "World"];

    /// <summary>Does a pending edit of these aspects change what this pick kind paints?</summary>
    public static bool Repaints(MapPick kind, Emit.WorldAspect touched) => kind switch
    {
        MapPick.Title => touched.HasFlag(Emit.WorldAspect.TitleColors),

        // Nothing an edit can touch. The Realms view used to paint each realm in the colour of the
        // title it is named after, and so followed a recolour; it takes its hues from
        // <see cref="RealmPalette"/> now, which reads the shape of the realm tree and not one
        // title's colour. Who holds what is not editable, so this render never goes stale.
        MapPick.Realm => false,

        MapPick.Culture => touched.HasFlag(Emit.WorldAspect.Cultures),
        _ => touched.HasFlag(Emit.WorldAspect.Faiths),
    };

    public static MapMode? Find(string name) => All.FirstOrDefault(m => m.Name == name);

    public static readonly MapMode[] All =
    [
        // --- Physical -------------------------------------------------------------------------
        new("Relief", "Physical", (r, _) => PreviewRenderer.RenderRelief(r))
        {
            Legend =
            [
                ((38, 70, 104), "Sea"),
                ((116, 146, 86), "Lowland"),
                ((92, 124, 68), "Plain"),
                ((140, 128, 84), "Upland"),
                ((128, 112, 98), "Highland"),
                ((232, 234, 238), "Peaks"),
            ],
            Probe = (r, _, cell, _) => $"{r.ProvinceElevation[cell]:F0} m",
        },
        new("Heightmap", "Physical", (r, _) => PreviewRenderer.RenderHeightmap(r))
        {
            Probe = (r, _, cell, _) => $"{r.ProvinceElevation[cell]:F0} m",
        },
        new("Terrain", "Physical", (r, _) => PreviewRenderer.RenderTerrain(r))
        {
            Legend = TerrainLegend(),
            Probe = (r, _, cell, _) => Spaced(r.Terrain.Terrain[cell].ToString()),
        },
        new("Rivers", "Physical", (r, _) => PreviewRenderer.RenderRivers(r)),
        new("Drainage", "Physical", (r, _) => PreviewRenderer.RenderDrainage(r)),
        new("Impassable", "Physical", (r, _) => PreviewRenderer.RenderImpassable(r))
        {
            Legend =
            [
                (PreviewRenderer.ImpassableFill, "Impassable"),
                (PreviewRenderer.MaskFill, "Painted in mask"),
                (PreviewRenderer.TrappedFill, "Trapped fill"),
                (PreviewRenderer.QualifiesFill, "Over floor, cut by share"),
                (PreviewRenderer.SteepTint, "Steep ground"),
                (PreviewRenderer.HighTint, "Above mountain line"),
                (PreviewRenderer.PassableBase, "Passable"),
            ],
            Probe = (r, _, cell, _) => PreviewRenderer.ImpassableProbe(r, cell),
        },

        // --- Climate --------------------------------------------------------------------------
        new("Climate", "Climate", (r, _) => PreviewRenderer.RenderClimate(r))
        {
            Legend = KoppenLegend(),
            Probe = (r, _, cell, _) => Spaced(r.Terrain.Climate[cell].ToString()),
        },
        new("Temperature", "Climate", (r, _) => PreviewRenderer.RenderTemperature(r))
        {
            Legend = RampLegend(PreviewRenderer.TemperatureColour,
                [(-20, "−20 °C"), (-5, "−5 °C"), (10, "10 °C"), (25, "25 °C"), (35, "35 °C")]),
            Probe = (r, _, cell, _) =>
                $"{r.Terrain.Field.MeanC[cell]:F1} °C mean · " +
                $"{r.Terrain.Field.WarmC[cell]:F0} warm / {r.Terrain.Field.ColdC[cell]:F0} cold",
        },
        new("Rainfall", "Climate", (r, _) => PreviewRenderer.RenderRainfall(r))
        {
            Legend = RampLegend(PreviewRenderer.RainfallColour,
                [(0, "0 mm"), (400, "400"), (800, "800"), (1600, "1600"), (2400, "2400+")]),
            Probe = (r, _, cell, _) =>
                $"{r.Terrain.Field.AnnualMm[cell]:F0} mm/year · " +
                $"{r.Terrain.Field.SummerMm[cell]:F0} summer / {r.Terrain.Field.WinterMm[cell]:F0} winter",
        },
        new("Habitability", "Climate", (r, _) => PreviewRenderer.RenderHabitability(r))
        {
            Legend = RampLegend(PreviewRenderer.HabitabilityColour,
                [(0.05, "Barren"), (0.3, "Poor"), (0.55, "Fair"), (0.8, "Good"), (1.0, "Rich")]),
        },

        // --- De Jure --------------------------------------------------------------------------
        new("Provinces", "De Jure", (r, _) => PreviewRenderer.RenderProvinces(r)),
        new("Counties", "De Jure", (r, _) => PreviewRenderer.RenderCounties(r))
            { Pick = (MapPick.Title, "c") },
        new("Duchies", "De Jure", (r, _) => PreviewRenderer.RenderDuchies(r))
            { Pick = (MapPick.Title, "d") },
        new("Kingdoms", "De Jure", (r, _) => PreviewRenderer.RenderKingdoms(r))
            { Pick = (MapPick.Title, "k") },
        new("Empires", "De Jure", (r, _) => PreviewRenderer.RenderEmpires(r))
            { Pick = (MapPick.Title, "e") },

        // --- World ----------------------------------------------------------------------------
        new("Realms", "World", (r, w) => PreviewRenderer.RenderRealms(r, RealmGraph.Build(w, r), w?.Wilderness))
        {
            AfterWrite = true,
            // Realm, not Title: the colours show whole de facto realms, so a click resolves to the
            // realm too, and drills from there. The county underneath stays a Ctrl+click away.
            // MainForm owns the focus stack this drives; the probe lives there too, beside it.
            Pick = (MapPick.Realm, "c"),
        },
        new("Cultures", "World", (r, w) => PreviewRenderer.RenderCultures(r, w?.Cultures, w?.Wilderness))
        {
            AfterWrite = true,
            Pick = (MapPick.Culture, "c"),
            Probe = (_, w, _, county) =>
                county is null || w is null ? null
                : w.Cultures.ByCounty.TryGetValue(county, out var c) ? c.Name : null,
        },
        new("Faiths", "World", (r, w) => PreviewRenderer.RenderFaiths(r, w?.Faiths, w?.Wilderness))
        {
            AfterWrite = true,
            Pick = (MapPick.Faith, "c"),
            Probe = (_, w, _, county) =>
                county is null || w is null ? null
                : w.Faiths.ByCounty.TryGetValue(county, out var f) ? f.Name : null,
        },
        new("Ethnicities", "World", (r, w) => PreviewRenderer.RenderEthnicities(r, w!))
        {
            AfterWrite = true,
            Repaints = MapPick.Culture,
            Probe = (_, w, _, county) =>
                county is null || w is null ? null
                : w.Cultures.ByCounty.TryGetValue(county, out var c)
                    ? w.Ethnicities.For(c).LocalizedName : null,
        },
        new("Development", "World", PreviewRenderer.RenderDevelopment)
        {
            AfterWrite = true,
            Legend = RampLegend(PreviewRenderer.DevelopmentColour,
                [(0, "0"), (8, "8"), (15, "15"), (22, "22"), (30, "30"), (40, "40+")]),
            Probe = (_, w, _, county) => county is null || w is null ? null
                : $"development {w.Development.GetValueOrDefault(county)}"
                  + (w.WorldCenters.IsCenter(county) ? " · world center" : ""),
        },
        new("Wealth", "World", PreviewRenderer.RenderWealth)
        {
            AfterWrite = true,
            Legend = RampLegend(PreviewRenderer.WealthColour,
                [(0.0, "0"), (0.25, "0.25"), (0.55, "0.55"), (0.9, "0.9"), (1.3, "1.3"),
                 (1.85, "1.85+ gold/mth")]),
            Probe = (_, w, _, county) => county is null || w is null ? null : WealthProbe(w, county),
        },
        new("Government", "World", PreviewRenderer.RenderGovernment)
        {
            Estimate = true,
            Legend =
            [
                (PreviewRenderer.GovernmentColour(GovernmentMap.Administrative), "Administrative"),
                (PreviewRenderer.GovernmentColour(GovernmentMap.Nomad), "Nomad"),
                (PreviewRenderer.GovernmentColour(GovernmentMap.Tribal), "Tribal"),
                (PreviewRenderer.GovernmentColour(GovernmentMap.Clan), "Clan"),
                (PreviewRenderer.GovernmentColour(GovernmentMap.Republic), "Republic"),
                (PreviewRenderer.GovernmentColour(GovernmentMap.Theocracy), "Theocracy"),
                (PreviewRenderer.GovernmentColour("feudal"), "Feudal"),
            ],
        },
        // Reads the written wilderness when there is one, and before then reproduces it exactly —
        // no Estimate flag, because there is nothing estimated about it. Wilderness is decided from
        // the terrain vote, the first development pass and the import, all of which exist as soon
        // as the world is generated; nothing it depends on is built inside the write. Measured, not
        // assumed: the recomputation and the written map agree on all 111 counties of seed 4242 and
        // on all 184 of the Fleunland import — where the version that dropped the import argument
        // disagreed on 47 — and render byte-identically in both. If that ever stops being true,
        // this mode wants the flag.
        new("Wilderness", "World", PreviewRenderer.RenderWilderness)
        {
            Legend =
            [
                ((108, 114, 122), "Settled"),
                ((168, 120, 48), "Wilderness"),
                ((255, 190, 90), "Frontier"),
            ],
        },
    ];

    /// <summary>
    /// The Wealth readout: the gold, then the holdings it came from, because "0.86 gold/month"
    /// on its own never answers the question the map raises, which is why this county and not
    /// that one.
    /// </summary>
    private static string WealthProbe(Emit.WrittenContent written, Title county)
    {
        double gold = Economy.CountyIncome(county, written.Holdings,
            written.Development.GetValueOrDefault(county));

        var holdings = Economy.CountyHoldings(county, written.Holdings)
            .Select(h => h.Replace("_holding", "").Replace('_', ' '))
            .ToList();

        string from = holdings.Count == 0 ? "no holdings" : string.Join(" + ", holdings);
        string centre = written.WorldCenters.IsCenter(county) ? " · world center" : "";

        return $"{gold:F2} gold/month · {from}{centre}";
    }

    private static ((byte, byte, byte), string)[] TerrainLegend()
        => [.. Enum.GetValues<TerrainClass>()
               .Where(t => t != TerrainClass.Sea)
               .Select(t => (Io.DebugRender.TerrainColour(t), Spaced(t.ToString())))];

    private static ((byte, byte, byte), string)[] KoppenLegend()
        => [.. Enum.GetValues<KoppenClass>()
               .Select(k => (Koppen.Colour(k), Spaced(k.ToString())))];

    private static ((byte, byte, byte), string)[] RampLegend(
        Func<float, (byte, byte, byte)> colour, (double Value, string Label)[] stops)
        => [.. stops.Select(s => (colour((float)s.Value), s.Label))];

    /// <summary>"HumidSubtropical" → "Humid subtropical".</summary>
    private static string Spaced(string pascal)
    {
        var text = new System.Text.StringBuilder(pascal.Length + 4);
        for (int i = 0; i < pascal.Length; i++)
        {
            if (i > 0 && char.IsUpper(pascal[i]))
            {
                text.Append(' ');
                text.Append(char.ToLowerInvariant(pascal[i]));
            }
            else
            {
                text.Append(pascal[i]);
            }
        }
        return text.ToString();
    }
}
