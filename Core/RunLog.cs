using System.Reflection;
using System.Text;
using System.Text.Json;
using Ck3MapGen.Config;

namespace Ck3MapGen.Core;

/// <summary>
/// Keeps a copy of everything a run prints, and writes it into the mod folder as
/// <c>proctool.txt</c> together with the settings that produced the mod.
///
/// The tool has no log object — every step talks to <see cref="Console"/>, and the GUI only
/// redirects that into a text box. Rather than thread a logger through the pipeline, this tees
/// <see cref="Console.Out"/>: whatever was already installed (stdout, the GUI's text box) keeps
/// receiving everything, and a buffer gets the same text. The front end then asks for the file
/// once the run is over, which is after <see cref="Stage.Report"/> — writing it from inside
/// <see cref="Generator.WriteMod"/> would cut the log off before the timing table.
///
/// The settings block is the same JSON a saved preset holds, so a mod folder documents how to
/// make it again: copy the block into a .json file and load it as a preset.
/// </summary>
public static class RunLog
{
    public const string FileName = "proctool.txt";

    private static readonly object Gate = new();
    private static readonly StringBuilder Buffer = new();
    private static TextWriter? _installed;
    private static DateTimeOffset _started;

    /// <summary>
    /// Starts a fresh capture. Call beside <see cref="Stage.Begin"/>. Safe to call on every run:
    /// the tee is installed once and re-used, and only the buffer is reset.
    /// </summary>
    public static void Begin()
    {
        lock (Gate)
        {
            Buffer.Clear();
            _started = DateTimeOffset.Now;

            // Console.SetOut wraps what it is given in a synchronised writer, so Console.Out is
            // never our instance itself; what it returns afterwards is what to compare against.
            // If something replaced Console.Out since, wrap that instead of stacking tees.
            if (_installed is null || !ReferenceEquals(Console.Out, _installed))
            {
                Console.SetOut(new TeeWriter(Console.Out));
                _installed = Console.Out;
            }
        }
    }

    /// <summary>Everything printed since <see cref="Begin"/>.</summary>
    public static string Text
    {
        get { lock (Gate) return Buffer.ToString(); }
    }

    /// <summary>
    /// Writes <c>proctool.txt</c> into <paramref name="modDir"/>: a header about the run, the
    /// settings as preset JSON, then the captured log. <paramref name="outcome"/> is a line such
    /// as "completed" or "cancelled", so a half-written folder says why it is half-written.
    ///
    /// Does nothing when the folder does not exist — a run cancelled before anything was written
    /// should not leave a folder containing only a log.
    /// </summary>
    public static void Write(string modDir, GenerationOptions options, string outcome)
    {
        if (!Directory.Exists(modDir)) return;

        var cfg = options.Config;
        var text = new StringBuilder();

        text.AppendLine("CK3 Procedural Map Tool — generation record");
        text.AppendLine($"Tool version:  {ToolVersion()}");
        text.AppendLine($"Started:       {_started:yyyy-MM-dd HH:mm:ss zzz}");
        text.AppendLine($"Finished:      {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        text.AppendLine($"Outcome:       {outcome}");
        text.AppendLine($"Mod name:      {options.ModName}");
        text.AppendLine($"Mod folder:    {modDir}");
        text.AppendLine($"Game folder:   {options.GameDir}");
        text.AppendLine($"Seed:          {cfg.Seed}");
        text.AppendLine($"Heightmap:     {DescribeHeightmap(options)}");
        text.AppendLine($"Azgaar:        {options.AzgaarJsonPath ?? "(none)"}");
        text.AppendLine($"History:       {(options.WriteHistory ? "written" : "skipped")}");
        text.AppendLine($"Packed map:    {(options.WritePacked ? "written" : "skipped")}");
        text.AppendLine($"Command line:  {Environment.CommandLine}");
        text.AppendLine($"Machine:       {Environment.MachineName}, {Environment.OSVersion}, "
                        + $"{Environment.ProcessorCount} cores, .NET {Environment.Version}");
        text.AppendLine();

        text.AppendLine("==== Settings (preset JSON; save as .json and load as a preset) ====");
        text.AppendLine(SettingsJson(cfg));
        text.AppendLine();

        text.AppendLine("==== Generation log ====");
        text.Append(Text);

        // The header is built with AppendLine and the log with bare '\n'; one convention for the file.
        string body = text.ToString().Replace("\r\n", "\n").Replace("\n", Environment.NewLine);

        string path = Path.Combine(modDir, FileName);
        try
        {
            File.WriteAllText(path, body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception ex)
        {
            // The mod is already written; a record that could not be saved is not worth failing
            // the run over, but it is worth a line where the user will see it.
            Console.WriteLine($"Could not write {FileName}: {ex.Message}");
        }
    }

    private static string DescribeHeightmap(GenerationOptions options)
    {
        var source = options.Heightmap;
        if (source is null) return "(none)";
        if (options.HeightmapPath is { } file) return file;

        string detail = source.Detail;
        return string.IsNullOrWhiteSpace(detail) ? source.Label : $"{source.Label} — {detail}";
    }

    /// <summary>
    /// The config as the preset writer lays it out: one key per settable property. The view-only
    /// flag that only exists for the PropertyGrid is left out; the image-derived sizes are kept,
    /// because knowing what size the map was built at is the point of a record (the preset loader
    /// skips them anyway, as it always has).
    /// </summary>
    private static string SettingsJson(MapConfig cfg)
    {
        var values = cfg.GetType().GetProperties()
            .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
            .Where(p => p.Name != nameof(MapConfig.ShowAdvancedSettings))
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToDictionary(p => p.Name, p => p.GetValue(cfg));

        return JsonSerializer.Serialize(values, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// The build this mod came out of. Public because the debug panel reports it inside the game,
    /// and a version printed in two places that disagree is worse than one printed in neither.
    /// </summary>
    public static string ToolVersion()
    {
        var assembly = typeof(RunLog).Assembly;
        string? informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return informational ?? assembly.GetName().Version?.ToString() ?? "unknown";
    }

    /// <summary>
    /// Forwards everything to the writer it replaced and keeps a copy. The copy is taken under
    /// <see cref="Gate"/>; the forward is not, because Console.SetOut already serialises callers.
    /// </summary>
    private sealed class TeeWriter(TextWriter inner) : TextWriter
    {
        public override Encoding Encoding => inner.Encoding;

        public override void Write(char value)
        {
            inner.Write(value);
            lock (Gate) Buffer.Append(value);
        }

        public override void Write(string? value)
        {
            inner.Write(value);
            if (value is null) return;
            lock (Gate) Buffer.Append(value);
        }

        public override void Write(char[] buffer, int index, int count)
        {
            inner.Write(buffer, index, count);
            lock (Gate) Buffer.Append(buffer, index, count);
        }

        public override void WriteLine(string? value)
        {
            inner.WriteLine(value);
            lock (Gate) Buffer.Append(value).Append('\n');
        }

        public override void WriteLine()
        {
            inner.WriteLine();
            lock (Gate) Buffer.Append('\n');
        }

        public override void Flush() => inner.Flush();
    }
}
