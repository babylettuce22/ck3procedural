using Ck3MapGen.Config;

namespace Ck3MapGen.Emit;

/// <summary>
/// Brings a world written by an older build up to date with this build's hand-kept files, without
/// regenerating it: the start page's "Update an old world…".
///
/// What it touches is only what does not depend on the world. That is the file sets in
/// BaseFilesToCopy, the building strip effect (written from the game's own building list), and the
/// GUI patches <c>--gui-only</c> already rewrites (written from the game's GUI files). The map,
/// titles, people, cultures, faiths and history are left exactly as they were, so a feature
/// whose content is generated per world (sees, tenets, saints) does not arrive this way; that
/// needs a regenerate.
///
/// It is careful in three ways. Only sets the mod already ships are refreshed, so an old world
/// never gains a system whose generated half it lacks, such as societies with no society written
/// for it. Paths a full write generates over their static copies are never replaced
/// (<see cref="StaticFileWriter.WrittenPerWorld"/>). And every file it replaces is copied first to
/// LocalAppData\Ck3MapGen\WorldBackups, beside the loaded-world editor's backups.
/// </summary>
public static class StaticRefresh
{
    public sealed record Result(
        IReadOnlyList<string> Sets, int Updated, int Added, int Unchanged, bool GuiRefreshed, string? BackupDir);

    /// <summary>
    /// Refreshes <paramref name="modDir"/> in place. <paramref name="cfg"/> is the world's own
    /// settings, read from its record: they decide which sets it was meant to have.
    /// </summary>
    public static Result Run(string modDir, string gameDir, MapConfig cfg)
    {
        if (!Core.RunLog.WroteFolder(modDir))
            throw new InvalidOperationException($"{modDir} was not written by this tool (it has no generation record), so it was left alone.");

        var started = DateTime.UtcNow;
        string backup = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ck3MapGen",
            "WorldBackups", Path.GetFileName(modDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            started.ToString("yyyyMMdd-HHmmss") + "-refresh");
        bool backedUp = false;

        void Backup(string target)
        {
            string copy = Path.Combine(backup, Path.GetRelativePath(modDir, target));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(target, copy, overwrite: true);
            backedUp = true;
        }

        // GUI first, as in a full write: the static copy below then skips anything written here.
        bool game = Core.GameLocator.IsGameDir(gameDir);
        if (game)
        {
            string gui = Path.Combine(modDir, "gui");
            if (Directory.Exists(gui))
                foreach (string file in Directory.GetFiles(gui, "*", SearchOption.AllDirectories)) Backup(file);

            FrontendWriter.WriteFrontend(modDir, gameDir, cfg.MenuPortraits);
            GuiWriter.WriteAll(modDir, gameDir, cfg.EnableSocieties || cfg.EnableSocietyPrototype, cfg.EnableWilderness,
                cfg.EnableChronicle);
            if (File.Exists(Path.Combine(gui, "gen_debug_panel.gui"))) DebugPanel.WriteEventsTab(modDir, gameDir);
        }
        else
        {
            Console.WriteLine("  game folder not found: GUI patches and the building strip effect were not refreshed");
        }

        var sets = StaticFileWriter.SetsFor(cfg).Where(set => Ships(modDir, set)).ToList();
        if (game && sets.Contains(StaticFileWriter.Wilderness)) BuildingStripWriter.Write(modDir, gameDir);

        // Which files differ from this build's copy, so only those are rewritten (and backed up).
        // A file's set ignore rules are the writer's business, so the counts come back from it;
        // this pass only decides which existing copies are out of date.
        var stale = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int unchanged = 0;
        foreach (string set in sets)
        {
            foreach (var (source, relative) in StaticFileWriter.FilesOf(set))
            {
                string target = Path.Combine(modDir, relative);
                if (!File.Exists(target) || StaticFileWriter.WrittenPerWorld(relative)
                    || File.GetLastWriteTimeUtc(target) >= started.AddMinutes(-1)) continue;
                if (SameContent(source, target)) { unchanged++; continue; }
                stale.Add(set + "|" + relative);
                Backup(target);
            }
        }

        var (copied, refreshed) = StaticFileWriter.WriteAll(modDir, sets, started,
            (set, relative) => StaticFileWriter.WrittenPerWorld(relative) || !stale.Contains(set + "|" + relative));

        // Stamps (and BOMs) what was just copied; files that already carry a stamp are left alone.
        Core.Generator.ApplyWatermark(modDir, cfg);

        var result = new Result(sets, refreshed, copied - refreshed, unchanged, game, backedUp ? backup : null);
        Core.RunLog.Append(modDir, "Updated to this build's static files",
            $"Was written by: {Core.RunLog.RecordedVersion(modDir) ?? "(unknown)"}\n"
            + $"Sets:           {string.Join(", ", sets)}\n"
            + $"Files:          {result.Updated} updated, {result.Added} added, {result.Unchanged} already current\n"
            + $"GUI patches:    {(game ? "rewritten" : "skipped, game folder not found")}\n"
            + $"Backup:         {result.BackupDir ?? "(nothing replaced)"}");
        return result;
    }

    /// <summary>
    /// Whether the mod already ships <paramref name="set"/>: any of its files is there, not counting
    /// the descriptor every set carries or the paths a full write generates per world.
    /// </summary>
    private static bool Ships(string modDir, string set)
        => set == StaticFileWriter.Core
           || StaticFileWriter.FilesOf(set).Any(f => !StaticFileWriter.WrittenPerWorld(f.Relative)
                                                     && File.Exists(Path.Combine(modDir, f.Relative)));

    /// <summary>
    /// The same file but for what the watermark pass does to a copy: a BOM, LF line ends and the
    /// stamp's comment block on top. Anything else compares byte for byte.
    /// </summary>
    private static bool SameContent(string source, string target)
    {
        string ext = Path.GetExtension(source).ToLowerInvariant();
        if (ext is not (".txt" or ".yml" or ".gui"))
            return File.ReadAllBytes(source).AsSpan().SequenceEqual(File.ReadAllBytes(target));
        return Normalise(File.ReadAllText(source)) == Normalise(File.ReadAllText(target));
    }

    private static string Normalise(string text)
    {
        text = text.TrimStart('﻿').Replace("\r\n", "\n");
        if (!text.StartsWith("# ===", StringComparison.Ordinal) || !text[..Math.Min(400, text.Length)].Contains("Generated by Ck3MapGen"))
            return text;
        int end = text.IndexOf(new string('=', 57) + "\n\n", 60, StringComparison.Ordinal);
        return end < 0 ? text : text[(end + 59)..];
    }
}
