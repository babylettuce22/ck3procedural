using Ck3MapGen.Core;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Gui;

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
    public static readonly string[] Categories = ["Physical", "Climate", "Divisions", "World"];

    /// <summary>Does a pending edit of these aspects change what this pick kind paints?</summary>
    public static bool Repaints(MapPick kind, Emit.WorldAspect touched) => kind switch
    {
        MapPick.Title or MapPick.Realm => touched.HasFlag(Emit.WorldAspect.TitleColors),
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

        // --- Divisions ------------------------------------------------------------------------
        new("Provinces", "Divisions", (r, _) => PreviewRenderer.RenderProvinces(r)),
        new("Counties", "Divisions", (r, _) => PreviewRenderer.RenderCounties(r))
            { Pick = (MapPick.Title, "c") },
        new("Duchies", "Divisions", (r, _) => PreviewRenderer.RenderDuchies(r))
            { Pick = (MapPick.Title, "d") },
        new("Kingdoms", "Divisions", (r, _) => PreviewRenderer.RenderKingdoms(r))
            { Pick = (MapPick.Title, "k") },
        new("Empires", "Divisions", (r, _) => PreviewRenderer.RenderEmpires(r))
            { Pick = (MapPick.Title, "e") },

        // --- World ----------------------------------------------------------------------------
        new("Realms", "World", (r, w) => PreviewRenderer.RenderRealms(r, w?.Realms, w?.Wilderness))
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
        new("Government", "World",
            (r, w) => PreviewRenderer.RenderGovernment(r, w?.Cultures, w?.WorldCenters))
        {
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
        new("Wilderness", "World", (r, _) => PreviewRenderer.RenderWilderness(r))
        {
            Legend =
            [
                ((108, 114, 122), "Settled"),
                ((168, 120, 48), "Wilderness"),
                ((255, 190, 90), "Frontier"),
            ],
        },
    ];

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
