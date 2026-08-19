using System.Text.Json;

namespace Ck3MapGen.Io;

/// <summary>
/// Reads Azgaar's JSON export into <see cref="AzgaarWorld"/>.
///
/// Deliberately hand-walked rather than handed wholesale to <c>JsonSerializer.Deserialize</c>. Two
/// reasons, and both are about surviving a format nobody controls:
///
///   * Azgaar's arrays are not homogeneous. <c>pack.features[0]</c> is the number <c>0</c>, and a
///     straight deserialize throws on it and takes the whole file down with it. Reading element by
///     element and skipping anything that is not an object costs one helper and makes every array
///     in the export immune to the same trick.
///   * The schema moves. Azgaar ships often and has rewritten its whole codebase at least once;
///     fields appear, get renamed and get dropped. A missing section here produces a warning and an
///     empty list, not an exception, because a map that imports with no zones is worth far more
///     than one that refuses to load at all.
///
/// What is *not* tolerated is a file that is not an Azgaar export, or one with no cells when cells
/// are what the caller needs — those fail loudly, because silently importing nothing looks exactly
/// like the importer being broken.
/// </summary>
public static class AzgaarJson
{
    /// <summary>
    /// Everything the parse noticed and could not act on: unknown versions, absent sections,
    /// counts that came out at zero. Printed after a load and shown in the GUI.
    /// </summary>
    public sealed record Warning(string Title, string Detail);

    public sealed class LoadResult
    {
        public required AzgaarWorld World { get; init; }
        public required IReadOnlyList<Warning> Warnings { get; init; }
    }

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    private static readonly JsonSerializerOptions ElementOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
                       | System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static LoadResult Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"No Azgaar export at {path}", path);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var warnings = new List<Warning>();

        using var stream = File.OpenRead(path);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(stream, DocumentOptions);
        }
        catch (JsonException e)
        {
            throw new InvalidOperationException(
                $"'{Path.GetFileName(path)}' is not valid JSON ({e.Message}).\n\n" +
                "It has to be Azgaar's JSON export, not the .map save file. In Azgaar: " +
                "Menu > Save/Load > Export to JSON > Full.", e);
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("info", out _))
                throw new InvalidOperationException(
                    $"'{Path.GetFileName(path)}' does not look like an Azgaar export — it has no " +
                    "\"info\" section.\n\nIn Azgaar: Menu > Save/Load > Export to JSON > Full.");

            var world = new AzgaarWorld
            {
                Info = Read<AzgaarInfo>(root, "info") ?? new AzgaarInfo(),
                Settings = Read<AzgaarSettings>(root, "settings") ?? new AzgaarSettings(),
                MapCoordinates = Read<AzgaarCoordinates>(root, "mapCoordinates"),
                Notes = ReadArray<AzgaarNote>(root, "notes"),
                NameBases = ReadArray<AzgaarNameBase>(root, "nameBases"),
            };

            if (root.TryGetProperty("pack", out var pack) && pack.ValueKind == JsonValueKind.Object)
            {
                world.Pack = new AzgaarPack
                {
                    Cells = ReadArray<AzgaarCell>(pack, "cells"),
                    Features = ReadArray<AzgaarFeature>(pack, "features"),
                    Biomes = Read<AzgaarBiomes>(pack, "biomes"),
                    Cultures = ReadArray<AzgaarCulture>(pack, "cultures"),
                    Burgs = ReadArray<AzgaarBurg>(pack, "burgs"),
                    States = ReadArray<AzgaarState>(pack, "states"),
                    Provinces = ReadArray<AzgaarProvince>(pack, "provinces"),
                    Religions = ReadArray<AzgaarReligion>(pack, "religions"),
                    Rivers = ReadArray<AzgaarRiver>(pack, "rivers"),
                    Markers = ReadArray<AzgaarMarker>(pack, "markers"),
                    Routes = ReadArray<AzgaarRoute>(pack, "routes"),
                    Zones = ReadArray<AzgaarZone>(pack, "zones"),
                };
            }
            else
            {
                throw new InvalidOperationException(
                    $"'{Path.GetFileName(path)}' has no \"pack\" section, so it carries no states, " +
                    "cultures or religions.\n\nThis is what a Grid-only or Pack-cells-only export " +
                    "looks like. Re-export with Menu > Save/Load > Export to JSON > Full.");
            }

            if (root.TryGetProperty("grid", out var grid) && grid.ValueKind == JsonValueKind.Object)
            {
                world.Grid = new AzgaarGrid
                {
                    Cells = ReadArray<AzgaarGridCell>(grid, "cells"),
                    CellsX = ReadInt(grid, "cellsX"),
                    CellsY = ReadInt(grid, "cellsY"),
                };
            }

            Validate(world, path, warnings);

            Console.WriteLine($"Azgaar export {Path.GetFileName(path)}: " +
                              $"\"{world.Info.MapName}\" v{world.Info.Version}, " +
                              $"{world.Info.Width:F0}x{world.Info.Height:F0} " +
                              $"({sw.ElapsedMilliseconds} ms)");
            Console.WriteLine($"  {world.Pack.Cells.Count} cells, " +
                              $"{world.RealStates.Count()} states, " +
                              $"{world.RealProvinces.Count()} provinces, " +
                              $"{world.RealBurgs.Count()} burgs, " +
                              $"{world.RealCultures.Count()} cultures, " +
                              $"{world.RealReligions.Count()} religions, " +
                              $"{world.Pack.Rivers.Count} rivers, " +
                              $"{world.NameBases.Count} name bases");

            int ongoing = world.RealStates.SelectMany(s => s.Campaigns).Count(c => c.IsOngoing);
            int fought = world.RealStates.SelectMany(s => s.Campaigns).Count(c => !c.IsOngoing);
            if (fought + ongoing > 0)
                Console.WriteLine($"  year {world.Settings.Options.Year} {world.Settings.Options.EraShort}, " +
                                  $"{fought / 2} wars fought, {ongoing / 2} still running");

            foreach (var warning in warnings)
                Console.WriteLine($"  ! {warning.Title}");

            return new LoadResult { World = world, Warnings = warnings };
        }
    }

    /// <summary>
    /// Checks the things that make an import useless rather than merely incomplete, and turns the
    /// rest into warnings. The version check is a warning by design: a newer Azgaar has never yet
    /// broken a field this importer reads, and refusing to open next month's export would be a
    /// worse failure than reading it and saying so.
    /// </summary>
    private static void Validate(AzgaarWorld world, string path, List<Warning> warnings)
    {
        if (world.Info.Width <= 0 || world.Info.Height <= 0)
            throw new InvalidOperationException(
                $"'{Path.GetFileName(path)}' declares a {world.Info.Width}x{world.Info.Height} " +
                "canvas, so there is no coordinate space to map anything into.");

        if (!world.HasCells)
            warnings.Add(new Warning(
                "The export has no per-cell data, so nothing can be placed on the map.",
                "This is a \"Minimal\" export. Its names can still be borrowed, but which state or " +
                "culture owns which piece of ground is only in the cells.\n\n" +
                "Fix: re-export with Export to JSON > Full."));

        if (world.NameBases.Count == 0)
            warnings.Add(new Warning(
                "The export carries no name bases.",
                "Places Azgaar did not name itself will fall back to the generator's own invented " +
                "languages instead of sounding like the rest of the map."));

        if (!world.RealStates.Any())
            warnings.Add(new Warning(
                "The map has no states.",
                "Every title will be named by the generator. Add states in Azgaar and re-export " +
                "if you wanted its politics."));

        if (!world.RealBurgs.Any())
            warnings.Add(new Warning(
                "The map has no burgs.",
                "Counties and baronies will be named by the generator, since burgs are what they " +
                "are normally named after."));

        // Azgaar's own version string is "1.x.y". Anything else is either very old or a fork, and
        // is worth saying out loud before the caller wonders why half the fields came out empty.
        string version = world.Info.Version;
        if (!string.IsNullOrEmpty(version) && !version.StartsWith("1.", StringComparison.Ordinal))
            warnings.Add(new Warning(
                $"Unfamiliar Azgaar version \"{version}\".",
                "The importer was written against the 1.x schema. It will read what it recognises " +
                "and ignore the rest; check the counts above look right."));
    }

    // --- Tolerant readers --------------------------------------------------------------------

    private static T? Read<T>(JsonElement parent, string name) where T : class
    {
        if (!parent.TryGetProperty(name, out var element)) return null;
        if (element.ValueKind != JsonValueKind.Object) return null;

        try
        {
            return element.Deserialize<T>(ElementOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int ReadInt(JsonElement parent, string name)
        => parent.TryGetProperty(name, out var element) && element.TryGetInt32(out int value) ? value : 0;

    /// <summary>
    /// Reads an array one element at a time, skipping anything that is not an object and anything
    /// that fails to bind.
    ///
    /// The per-element try/catch is the point. <c>pack.features[0]</c> is the number <c>0</c>, and
    /// binding the array as a whole throws on it and loses the other four hundred features with it.
    /// One malformed entry should cost one entry.
    /// </summary>
    internal static List<T> ReadArray<T>(JsonElement parent, string name) where T : class
    {
        var result = new List<T>();
        if (!parent.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object) continue;

            try
            {
                if (element.Deserialize<T>(ElementOptions) is { } item) result.Add(item);
            }
            catch (JsonException)
            {
                // One unreadable entry among thousands is not worth failing the load over.
            }
        }

        return result;
    }
}
