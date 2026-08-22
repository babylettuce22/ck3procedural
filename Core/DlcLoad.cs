using System.Text;
using System.Text.Json;

namespace Ck3MapGen.Core;

/// <summary>
/// The launcher's mod checkbox, as a file. <c>dlc_load.json</c> sits beside the mod folder in
/// Documents and is the list the game itself reads at startup: an entry there is the difference
/// between a mod that exists on disk and a mod that loads.
///
/// Editing it is worth doing because this tool starts the game directly (<c>ck3.exe</c>, with the
/// debug flags) rather than through the Paradox launcher, and a directly started game honours this
/// file and nothing else. The launcher is the caveat rather than the target: it keeps playsets in
/// its own database and rewrites this file from them, so opening it after a write can drop what we
/// wrote.
///
/// What we write is the whole list, not an addition to it. The caller says what the list is — the
/// offer after a write says "just the map", and the Mods dialog says whatever was ticked — but
/// either way the file ends up holding exactly that and nothing inherited from what happened to be
/// enabled last.
///
/// Order is load order, and later wins on a file both mods ship, so a caller that wants the
/// generated map to override puts it last. What order cannot fix is a genuine collision: a
/// generated map is a total conversion — <see cref="Emit.ModWriter"/> declares <c>replace_path</c>
/// over landed_titles, province_terrain, the history folders and the map object data, and every
/// province id, title key and culture in the game is one this tool invented. A second mod loaded
/// beside it is either silently voided by those replace_paths or, worse, still live and addressing
/// vanilla provinces that no longer exist, which is a load that hangs rather than a load that logs.
/// Stacking is therefore something to do deliberately, one mod at a time, and not something this
/// file has an opinion about beyond writing down the answer.
///
/// Everything that is not the mod list survives untouched: disabled DLCs, and any key a later patch
/// adds, are read and written straight back.
/// </summary>
public static class DlcLoad
{
    private const string FileName = "dlc_load.json";
    private const string Key = "enabled_mods";

    /// <summary>
    /// The <c>dlc_load.json</c> that governs <paramref name="modDir"/>, or null when this mod is
    /// not somewhere the file could name.
    ///
    /// The entries in it are paths relative to the folder the file lives in, and the one we would
    /// write is always <c>mod/&lt;folder&gt;.mod</c> — which is only true when the mod was written
    /// into the launcher's own mod folder. A mod written anywhere else is perfectly valid (CK3
    /// finds it through the absolute <c>path=</c> line in the outer .mod file) but cannot be
    /// referred to relatively, so there is nothing honest to add and we add nothing.
    /// </summary>
    public static string? FileFor(string modDir)
        => FileForRoot(Path.GetDirectoryName(modDir.TrimEnd(Path.DirectorySeparatorChar)));

    /// <summary>
    /// The <c>dlc_load.json</c> that governs the mod folder <paramref name="modRoot"/>, or null when
    /// that folder is not one the game reads.
    ///
    /// The same question as <see cref="FileFor"/> asked without a mod in hand, which is what the
    /// mod list needs: it is about the folder as a whole, before anything in it has been picked.
    /// </summary>
    public static string? FileForRoot(string? modRoot)
    {
        if (modRoot is null) return null;

        if (!string.Equals(Path.GetFileName(modRoot), "mod", StringComparison.OrdinalIgnoreCase))
            return null;

        string? userDir = Path.GetDirectoryName(modRoot);
        if (userDir is null) return null;

        string file = Path.Combine(userDir, FileName);

        // An existing file is its own proof that this is the folder CK3 keeps its user data in.
        // Without one — a machine where the game has never been started — fall back on asking the
        // locator, so we only ever create the file in a place the launcher will look at.
        if (File.Exists(file)) return file;

        return Same(modRoot, GameLocator.FindModRoot()) && Directory.Exists(userDir) ? file : null;
    }

    /// <summary>The entry naming <paramref name="modDir"/>, matching what <see cref="Emit.ModWriter"/> calls the outer .mod file.</summary>
    public static string EntryFor(string modDir)
        => $"mod/{Path.GetFileName(modDir.TrimEnd(Path.DirectorySeparatorChar))}.mod";

    /// <summary>
    /// What the game is currently set to load, in file order. Never throws: a file that is missing,
    /// unreadable or malformed is reported as nothing enabled, which is what the caller does
    /// something about anyway.
    /// </summary>
    public static IReadOnlyList<string> Enabled(string file)
    {
        try
        {
            return Mods(Read(file));
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>
    /// Whether the game is already set to load exactly <paramref name="entries"/>, in that order —
    /// the state <see cref="Enable"/> produces, and the only state worth leaving alone. The same
    /// mods in a different order is not it: order is load order, so reordering changes which mod
    /// wins a shared file.
    /// </summary>
    public static bool IsExactly(string file, IReadOnlyList<string> entries)
    {
        var mods = Enabled(file);
        return mods.Count == entries.Count
               && mods.Zip(entries).All(pair => SameEntry(pair.First, pair.Second));
    }

    /// <summary>
    /// Makes <paramref name="entries"/> the whole of <c>enabled_mods</c>, in order, dropping
    /// whatever else was ticked. Every other key in the file is preserved.
    ///
    /// Throws on an unreadable or unwritable file rather than swallowing it — by the time this
    /// runs the mod is written and the user has said yes, so silently doing nothing would leave
    /// them looking for their map in a game that never loaded it.
    /// </summary>
    public static void Enable(string file, IReadOnlyList<string> entries)
    {
        var document = File.Exists(file) ? Read(file) : [];

        // Rebuilt rather than mutated so the untouched keys keep their original shape, whatever
        // the game has put in them, and their original order.
        var list = entries.ToArray();

        var updated = new Dictionary<string, object?>();
        foreach (var (key, value) in document)
            updated[key] = key == Key ? list : value;

        updated.TryAdd(Key, list);
        updated.TryAdd("disabled_dlcs", Array.Empty<string>());

        // The game writes this file minified and without a BOM, unlike every Paradox *script*
        // file. Matching it keeps the diff against a launcher-written file to the mod list itself.
        Write(file, JsonSerializer.Serialize(updated));
    }

    private static Dictionary<string, JsonElement> Read(string file)
        => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(file)) ?? [];

    private static List<string> Mods(Dictionary<string, JsonElement> document)
    {
        if (!document.TryGetValue(Key, out var value) || value.ValueKind != JsonValueKind.Array)
            return [];

        return value.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString()!)
            .ToList();
    }

    /// <summary>
    /// Written beside the original and swapped in, so a crash mid-write cannot leave the game with
    /// half a config — which it would refuse to start on.
    /// </summary>
    private static void Write(string file, string json)
    {
        string temp = file + ".tmp";
        File.WriteAllText(temp, json, new UTF8Encoding(false));

        try
        {
            if (File.Exists(file)) File.Replace(temp, file, null);
            else File.Move(temp, file);
        }
        catch (IOException)
        {
            // Documents is routinely a synced OneDrive folder, where the atomic replace is the one
            // operation the filter driver is liable to refuse. A plain overwrite still beats
            // failing outright, and the window it opens is a few microseconds wide.
            File.Copy(temp, file, overwrite: true);
            File.Delete(temp);
        }
    }

    private static bool SameEntry(string a, string b)
        => string.Equals(a.Replace('\\', '/').Trim(), b.Replace('\\', '/').Trim(),
            StringComparison.OrdinalIgnoreCase);

    private static bool Same(string a, string b)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
