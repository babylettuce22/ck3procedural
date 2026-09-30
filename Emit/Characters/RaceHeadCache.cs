using System.Security.Cryptography;
using System.Text;

namespace Ck3MapGen.Emit;

/// <summary>
/// A local cache of <see cref="RaceHeadWriter"/>'s output. Every file it writes — ears, face shapes,
/// tusked teeth, horns and ornaments with all their follow shapes, the patched head and teeth assets,
/// the crown copies — depends only on the installed game's head and accessory files and on the
/// generator's own code, never on the seed or the map. So it is built once and copied thereafter:
/// ~500 small files, which cost well over a second to build and write.
///
/// **Why a cache and not files in BaseFilesToCopy.** The output is derived from vanilla's heads vertex
/// for vertex, and a copy committed to the repo would go stale silently the day a game patch touches
/// a head. The cache key covers what could change it:
/// <list type="bullet">
/// <item>the generator build (the assembly's MVID — deterministic builds give the same source the
/// same id, so any code change is a new key and nothing needs bumping by hand);</item>
/// <item>path, size and modification time of every file under the game's two head folders and its
/// accessory definitions (a game patch rewrites them).</item>
/// </list>
///
/// Lives in <c>%LOCALAPPDATA%\Ck3MapGen\race_head_cache\&lt;key&gt;</c>, never in the repo. Entries
/// appear by an atomic rename of a finished staging folder, so a parallel run (verify loops, the GUI)
/// never reads half an entry; the newest three are kept. Any cache failure falls back to building
/// straight into the mod — the cache can make a run faster, never break it.
/// </summary>
internal static class RaceHeadCache
{
    private const string Complete = "complete";
    private const int Keep = 3;

    public static void Write(string modDir, string gameDir, Action<string, string, List<string>> build)
    {
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Ck3MapGen", "race_head_cache");
        string key = Key(gameDir);
        string entry = Path.Combine(root, key);

        if (File.Exists(Path.Combine(entry, Complete)))
        {
            try
            {
                int n = CopyTree(Path.Combine(entry, "files"), modDir);
                Directory.SetLastWriteTimeUtc(entry, DateTime.UtcNow);     // recency, for Prune
                foreach (string line in File.ReadAllLines(Path.Combine(entry, "log.txt"))) Console.WriteLine(line);
                Console.WriteLine($"  race head shapes: {n} files from cache ({key})");
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Console.WriteLine($"  race head cache unreadable, rebuilding: {e.Message}");
            }
        }

        string staging = Path.Combine(root, $"tmp_{Guid.NewGuid():N}");
        var log = new List<string>();
        try
        {
            Directory.CreateDirectory(staging);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"  race head cache unavailable, building directly: {e.Message}");
            build(modDir, gameDir, log);
            foreach (string line in log) Console.WriteLine(line);
            return;
        }

        build(Path.Combine(staging, "files"), gameDir, log);
        foreach (string line in log) Console.WriteLine(line);
        int count = CopyTree(Path.Combine(staging, "files"), modDir);

        try
        {
            File.WriteAllLines(Path.Combine(staging, "log.txt"), log);
            File.WriteAllText(Path.Combine(staging, Complete), key);
            if (Directory.Exists(entry)) Directory.Delete(entry, recursive: true);    // an incomplete leftover
            Directory.Move(staging, entry);
            Console.WriteLine($"  race head shapes: {count} files built and cached ({key})");
            Prune(root);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Another run cached the same key first, or the folder is locked: this run is still complete.
            TryDelete(staging);
            Console.WriteLine($"  race head shapes: {count} files built (not cached: {e.Message})");
        }
    }

    /// <summary>The generator build and the metadata of every game file the writer reads.</summary>
    private static string Key(string gameDir)
    {
        var sb = new StringBuilder("race-head-cache v1\n");
        sb.Append(typeof(RaceHeadCache).Assembly.ManifestModule.ModuleVersionId).Append('\n');

        string portraits = Path.Combine(gameDir, "gfx", "models", "portraits");
        var inputs = new List<string>();
        foreach (string sex in new[] { "male", "female" })
        {
            string d = Path.Combine(portraits, $"{sex}_head");
            if (Directory.Exists(d)) inputs.AddRange(Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories));
        }

        string accessories = Path.Combine(gameDir, "gfx", "portraits", "accessories");
        if (Directory.Exists(accessories)) inputs.AddRange(Directory.EnumerateFiles(accessories, "*.txt"));

        foreach (string f in inputs.Order(StringComparer.Ordinal))
        {
            var info = new FileInfo(f);
            sb.Append(Path.GetRelativePath(gameDir, f)).Append('|').Append(info.Length).Append('|')
              .Append(info.LastWriteTimeUtc.Ticks).Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())))[..16].ToLowerInvariant();
    }

    private static int CopyTree(string from, string to)
    {
        if (!Directory.Exists(from)) return 0;
        var files = Directory.GetFiles(from, "*", SearchOption.AllDirectories);
        foreach (string dir in files.Select(f => Path.GetDirectoryName(Path.Combine(to, Path.GetRelativePath(from, f)))!).Distinct())
            Directory.CreateDirectory(dir);
        Parallel.ForEach(files, f => File.Copy(f, Path.Combine(to, Path.GetRelativePath(from, f)), overwrite: true));
        return files.Length;
    }

    /// <summary>Keeps the newest <see cref="Keep"/> complete entries; clears staging older than an hour.</summary>
    private static void Prune(string root)
    {
        try
        {
            var dirs = new DirectoryInfo(root).GetDirectories();
            foreach (var d in dirs.Where(d => d.Name.StartsWith("tmp_", StringComparison.Ordinal)
                                              && d.LastWriteTimeUtc < DateTime.UtcNow.AddHours(-1)))
                TryDelete(d.FullName);
            foreach (var d in dirs.Where(d => !d.Name.StartsWith("tmp_", StringComparison.Ordinal))
                                  .OrderByDescending(d => d.LastWriteTimeUtc).Skip(Keep))
                TryDelete(d.FullName);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Pruning is housekeeping; a locked folder is fine to leave for next time.
        }
    }

    private static void TryDelete(string dir)
    {
        try { Directory.Delete(dir, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
