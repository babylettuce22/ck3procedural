using System.Text.Json;
using System.Text.Json.Serialization;
using Ck3MapGen.Config;

namespace Ck3MapGen.Gui;

/// <summary>
/// The bits of the window that ought to survive closing it: where it was, how it was split, and
/// what it was pointed at.
///
/// Deliberately only the window. Settings themselves are *not* remembered here — a map is defined
/// by its heightmap plus its config, and silently reloading the last session's tuning would make
/// the tool's defaults depend on invisible state on disk. Config that is worth keeping is saved
/// explicitly as a preset, where it has a name and a file you can see.
/// </summary>
public sealed class GuiState
{
    public int Left { get; set; }
    public int Top { get; set; }
    public int Width { get; set; } = 1500;
    public int Height { get; set; } = 950;
    public bool Maximized { get; set; }

    public int SettingsWidth { get; set; } = 430;
    public int ViewerHeight { get; set; } = 620;

    public string? HeightmapPath { get; set; }
    public string? View { get; set; }
    public string? PresetDir { get; set; }

    public string? LaunchArgs { get; set; } = "-debug_mode -developer -skip";
    /// <summary>
    /// Where the game and the launcher's mod folder were last found, and what the last mod written
    /// was called.
    ///
    /// Remembered for the same reason the heightmap path is: they are answers about *this machine*
    /// rather than decisions about the map, and re-answering them every launch is work the user has
    /// already done. <see cref="Core.GameLocator"/> searches on every launch regardless — this only
    /// wins over the search when it still points at something real, which is what makes a hand-picked
    /// folder stick even on a machine where the search would have found a different install.
    /// </summary>
    public string? GameDir { get; set; }

    /// <inheritdoc cref="GameDir"/>
    public string? ModRoot { get; set; }

    /// <inheritdoc cref="GameDir"/>
    public string? ModName { get; set; }

    /// <summary>The last mod folder actually written, which is what "Open mod folder" opens.</summary>
    public string? LastModDir { get; set; }

    /// <summary>
    /// What the last run of each kind cost, phase by phase — the whole basis of the progress
    /// estimate. Two of them because writing the mod runs a dozen phases a preview never does, so
    /// one shared profile would over-predict every preview and under-predict every write.
    ///
    /// This <em>is</em> remembered across sessions, unlike settings: it is a measurement of the
    /// machine rather than a decision by the user, so restoring it silently is right, and the worst
    /// a stale one can do is make a bar move at the wrong speed once.
    /// </summary>
    public RunProfile PreviewRun { get; set; } = new();

    /// <inheritdoc cref="PreviewRun"/>
    public RunProfile WriteRun { get; set; } = new();

    private static readonly JsonSerializerOptions Format = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static string Path_ => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Ck3MapGen", "gui.json");

    /// <summary>
    /// Never throws. A corrupt or unreadable state file must cost the window's position and nothing
    /// else — refusing to open because a convenience file is malformed would be absurd.
    /// </summary>
    public static GuiState Load()
    {
        try
        {
            return File.Exists(Path_)
                ? JsonSerializer.Deserialize<GuiState>(File.ReadAllText(Path_)) ?? new GuiState()
                : new GuiState();
        }
        catch (Exception)
        {
            return new GuiState();
        }
    }

    /// <inheritdoc cref="Load"/>
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path_)!);
            File.WriteAllText(Path_, JsonSerializer.Serialize(this, Format));
        }
        catch (Exception)
        {
        }
    }
}

/// <summary>One recorded run: how big the map was, and what each phase cost on it.</summary>
public sealed class RunProfile
{
    /// <summary>Province raster megapixels, which is what phase durations are scaled by.</summary>
    public double Megapixels { get; set; }

    public List<PhaseTime> Phases { get; set; } = [];
}

/// <summary>One phase of a recorded run.</summary>
public sealed class PhaseTime
{
    public string Name { get; set; } = "";
    public double Ms { get; set; }
}

/// <summary>
/// A named settings file: every knob in <see cref="MapConfig"/>, as JSON.
///
/// The map size properties are not written. They are set from the heightmap on every run and are
/// not a user's to choose, so a preset that carried them would either be ignored or — worse — be
/// believed, and a config claiming a size the image disagrees with is a silent CK3 failure.
/// </summary>
internal static class Preset
{
    private static readonly JsonSerializerOptions Format = new() { WriteIndented = true };

    /// <summary>Set from the image every run; see <see cref="MapConfig.Width"/>.</summary>
    private static readonly string[] FromImage =
        [nameof(MapConfig.Width), nameof(MapConfig.Height),
         nameof(MapConfig.WorldWidth), nameof(MapConfig.WorldHeight)];

    /// <summary>
    /// Lives on the config only because the PropertyGrid needs somewhere to put it, and decides
    /// what the grid shows rather than what the map is. Carrying it would let someone else's
    /// preset change your view.
    /// </summary>
    private static readonly string[] ViewOnly = [nameof(MapConfig.ShowAdvancedSettings)];

    private static bool Saved(System.Reflection.PropertyInfo p)
        => !FromImage.Contains(p.Name) && !ViewOnly.Contains(p.Name);

    public static void Save(MapConfig config, string path)
    {
        var values = Settable(config)
            .Where(Saved)
            .ToDictionary(p => p.Name, p => p.GetValue(config));

        File.WriteAllText(path, JsonSerializer.Serialize(values, Format));
    }

    /// <summary>
    /// Applies a preset over the live config in place, so the PropertyGrid keeps pointing at the
    /// same object. Unknown keys are skipped rather than fatal: a preset written before a setting
    /// existed should still load, minus that setting.
    /// </summary>
    /// <returns>How many settings were applied.</returns>
    public static int Load(MapConfig config, string path)
    {
        var document = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            File.ReadAllText(path)) ?? [];

        int applied = 0;
        foreach (var property in Settable(config))
        {
            if (!Saved(property)) continue;
            if (!document.TryGetValue(property.Name, out var element)) continue;

            var value = element.Deserialize(property.PropertyType);
            if (value is null) continue;

            property.SetValue(config, value);
            applied++;
        }

        return applied;
    }

    private static IEnumerable<System.Reflection.PropertyInfo> Settable(MapConfig config)
        => config.GetType().GetProperties()
            .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0);
}
