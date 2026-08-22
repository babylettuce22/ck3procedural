using Microsoft.Win32;

namespace Ck3MapGen.Core;

/// <summary>
/// Finds the two directories this tool cannot work without: the game's own <c>game</c> folder, which
/// every emitter reads vanilla data out of, and the launcher's <c>mod</c> folder, which is the only
/// place CK3 will look for what we write.
///
/// Both used to be constants — a hardcoded <c>C:\Program Files (x86)\Steam\...</c> and Documents —
/// and the first report from anybody who was not the author was that Steam lives on D:. A hardcoded
/// path is not a default, it is a guess about somebody else's disk, and when it is wrong the failure
/// lands three phases into a write as an unreadable-vocabulary exception.
///
/// The search is deliberately cheap: registry reads, one small text file, and a fixed list of
/// <see cref="Directory.Exists"/> probes per drive. Nothing here walks a directory tree, so it costs
/// milliseconds and can run on every launch. It is also allowed to fail — every entry point can be
/// pointed at a directory by hand, and this only decides what it is pointed at to begin with.
/// </summary>
public static class GameLocator
{
    /// <summary>The install folder's name under a Steam library, and under Documents/Paradox.</summary>
    private const string GameName = "Crusader Kings III";

    /// <summary>CK3's Steam app id, which names its workshop folder.</summary>
    private const string AppId = "1158310";

    /// <summary>What the search falls back to when it finds nothing. The old hardcoded path.</summary>
    public static string DefaultGameDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Steam", "steamapps", "common", GameName, "game");

    private static string? _gameDir;
    private static bool _searched;

    private static string? _workshopDir;
    private static bool _searchedWorkshop;

    /// <summary>
    /// The game's <c>game</c> directory, or null if nothing plausible turned up.
    ///
    /// Cached, because it is asked for from several places on one launch and the answer cannot
    /// change while the program runs unless somebody moves the install underneath it —
    /// <see cref="Forget"/> exists for that.
    /// </summary>
    public static string? FindGameDir()
    {
        if (_searched) return _gameDir;

        _searched = true;
        _gameDir = SearchGameDir();
        return _gameDir;
    }

    /// <summary>
    /// Where Steam downloads CK3 workshop subscriptions, or null when there is no such folder —
    /// a GOG or Epic install, or a Steam one with nothing subscribed.
    ///
    /// Only ever a cross-check. The launcher registers every subscription as a <c>.mod</c> stub in
    /// the mod folder, so <see cref="ModLibrary.InFolder"/> already sees the workshop without this;
    /// what this finds is the gap between the two, which is a subscription downloaded since the
    /// launcher was last opened.
    ///
    /// The workshop lives beside <c>common</c> in whichever library holds the game, so the search is
    /// the game search with a different leaf — and independent of it, because a library can hold the
    /// workshop content for a game that has since been uninstalled.
    /// </summary>
    public static string? FindWorkshopRoot()
    {
        if (_searchedWorkshop) return _workshopDir;

        _searchedWorkshop = true;
        _workshopDir = SteamLibraries()
            .Select(library => Path.Combine(library, "steamapps", "workshop", "content", AppId))
            .FirstOrDefault(Directory.Exists);

        return _workshopDir;
    }

    /// <summary>Drops the cached answers so the next call searches again.</summary>
    public static void Forget()
    {
        _searched = false;
        _gameDir = null;
        _searchedWorkshop = false;
        _workshopDir = null;
    }

    /// <summary>
    /// Whether a directory really is a CK3 <c>game</c> folder.
    ///
    /// Two markers rather than one, and both directories this program actually reads: a folder that
    /// merely exists proves nothing — a moved or half-uninstalled Steam install leaves the shell of
    /// one behind, and pointing the emitters at that produces the same wall of missing-vocabulary
    /// errors as pointing them at nothing.
    /// </summary>
    public static bool IsGameDir(string? dir)
        => dir is not null
           && Directory.Exists(Path.Combine(dir, "common", "landed_titles"))
           && Directory.Exists(Path.Combine(dir, "map_data"));

    /// <summary>
    /// Takes whatever the user picked in a folder dialog and returns the <c>game</c> directory it
    /// implies, or null if it implies none.
    ///
    /// People pick the install root — the folder called "Crusader Kings III" — about as often as
    /// they pick <c>game</c> inside it, and rejecting the former on a technicality is the sort of
    /// thing that makes a tool feel broken. The Steam library and its <c>common</c> folder are worth
    /// accepting for the same reason.
    /// </summary>
    public static string? Normalize(string? picked)
    {
        if (string.IsNullOrWhiteSpace(picked)) return null;

        string dir = picked.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        string[] candidates =
        [
            dir,
            Path.Combine(dir, "game"),
            Path.Combine(dir, GameName, "game"),
            Path.Combine(dir, "common", GameName, "game"),
            Path.Combine(dir, "steamapps", "common", GameName, "game"),
        ];

        return candidates.FirstOrDefault(IsGameDir);
    }

    // --- The game ---------------------------------------------------------------------------------

    private static string? SearchGameDir()
    {
        foreach (string library in SteamLibraries())
        {
            string candidate = Path.Combine(library, "steamapps", "common", GameName, "game");
            if (IsGameDir(candidate)) return candidate;
        }

        foreach (string candidate in FixedGuesses())
            if (IsGameDir(candidate)) return candidate;

        return null;
    }

    /// <summary>
    /// Every Steam library folder this machine admits to, most authoritative first.
    ///
    /// Steam keeps its libraries in <c>steamapps/libraryfolders.vdf</c> under the install it was
    /// registered with, which is the only thing that knows about a library on a fourth drive with a
    /// name nobody could guess. The install itself is found through the registry; the per-drive
    /// guesses below cover a Steam whose registration has been lost.
    /// </summary>
    private static IEnumerable<string> SteamLibraries()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string root in SteamRoots())
        {
            if (!seen.Add(root)) continue;
            yield return root;

            foreach (string library in LibrariesIn(root))
                if (seen.Add(library) && Directory.Exists(library))
                    yield return library;
        }
    }

    private static IEnumerable<string> SteamRoots()
    {
        // The registry first. HKCU is written by the running client and is the one that tracks a
        // Steam that has been moved; the HKLM pair are the installer's own record.
        foreach (var (hive, key, value) in new (RegistryKey, string, string)[]
                 {
                     (Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath"),
                     (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
                     (Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath"),
                 })
        {
            string? path = ReadRegistry(hive, key, value);

            // The client writes forward slashes into SteamPath; everything downstream is Path.Combine
            // against it, which does not care, but a path that reads as half-Unix in a log does.
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                yield return path.Replace('/', '\\');
        }

        foreach (string drive in Drives())
        {
            yield return Path.Combine(drive, "Program Files (x86)", "Steam");
            yield return Path.Combine(drive, "Program Files", "Steam");
            yield return Path.Combine(drive, "Steam");
            yield return Path.Combine(drive, "SteamLibrary");
            yield return Path.Combine(drive, "Games", "Steam");
            yield return Path.Combine(drive, "Games", "SteamLibrary");
        }
    }

    /// <summary>
    /// The library paths named in a Steam install's <c>libraryfolders.vdf</c>.
    ///
    /// Parsed by hand rather than properly. The file is Valve's own key-value format and the only
    /// thing wanted from it is the <c>"path"</c> of each entry — older clients wrote the library
    /// under a numeric key instead, which is why both shapes are matched. Anything unparseable is
    /// simply not a library; there is no failure mode here worth reporting.
    /// </summary>
    private static IEnumerable<string> LibrariesIn(string steamRoot)
    {
        string file = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");

        string[] lines;
        try
        {
            if (!File.Exists(file)) yield break;
            lines = File.ReadAllLines(file);
        }
        catch (Exception)
        {
            yield break;
        }

        foreach (string line in lines)
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                line, "^\\s*\"(?:path|\\d+)\"\\s+\"([^\"]+)\"\\s*$");
            if (!match.Success) continue;

            // The file escapes its backslashes, being a C-ish quoted string.
            string path = match.Groups[1].Value.Replace(@"\\", @"\");

            // The numeric-key form is what old clients wrote libraries as, and what current ones
            // write the per-app download sizes as inside each library's `apps` block. Requiring a
            // rooted path is what tells the two apart.
            if (Path.IsPathRooted(path)) yield return path;
        }
    }

    /// <summary>
    /// Installs that are not Steam's, plus the handful of places people put a Steam library without
    /// telling Steam. One <see cref="Directory.Exists"/> each, on every fixed drive.
    /// </summary>
    private static IEnumerable<string> FixedGuesses()
    {
        foreach (string drive in Drives())
        {
            yield return Path.Combine(drive, "Program Files (x86)", "GOG Galaxy", "Games", GameName, "game");
            yield return Path.Combine(drive, "Program Files", "Epic Games", "CrusaderKingsIII", "game");
            yield return Path.Combine(drive, "Program Files (x86)", "Paradox Interactive", GameName, "game");
            yield return Path.Combine(drive, "Program Files", "Paradox Interactive", GameName, "game");
            yield return Path.Combine(drive, "Games", GameName, "game");
            yield return Path.Combine(drive, GameName, "game");
        }
    }

    // --- The mod folder ---------------------------------------------------------------------------

    /// <summary>
    /// Where the launcher looks for mods: <c>Documents/Paradox Interactive/Crusader Kings III/mod</c>.
    ///
    /// Never null, because unlike the game folder this one is ours to create — if the launcher has
    /// never run there is nothing to find and writing the mod into the place it will look is exactly
    /// right. The search matters anyway: Documents is routinely redirected onto OneDrive or onto
    /// another drive entirely, and the launcher follows the redirection while a hardcoded
    /// <c>%USERPROFILE%\Documents</c> does not.
    /// </summary>
    public static string FindModRoot()
    {
        string? found = ModRootGuesses().FirstOrDefault(Directory.Exists);
        if (found is not null) return found;

        // Nothing exists yet. Prefer the shell's own idea of Documents, which is what the launcher
        // will resolve too, and let the writer create the rest.
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Paradox Interactive", GameName, "mod");
    }

    private static IEnumerable<string> ModRootGuesses()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string documents in DocumentFolders())
        {
            string mod = Path.Combine(documents, "Paradox Interactive", GameName, "mod");
            if (seen.Add(mod)) yield return mod;
        }
    }

    private static IEnumerable<string> DocumentFolders()
    {
        // SpecialFolder.MyDocuments resolves the shell's redirection, so this is already the OneDrive
        // path on a machine with Known Folder Move turned on. The rest cover the case where it is
        // not — a OneDrive that syncs Documents without owning the shell folder.
        yield return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        string? personal = ReadRegistry(Registry.CurrentUser,
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders", "Personal");
        if (!string.IsNullOrWhiteSpace(personal)) yield return personal;

        string? oneDrive = Environment.GetEnvironmentVariable("OneDrive")
                           ?? Environment.GetEnvironmentVariable("OneDriveConsumer");
        if (!string.IsNullOrWhiteSpace(oneDrive)) yield return Path.Combine(oneDrive, "Documents");

        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(profile))
        {
            yield return Path.Combine(profile, "Documents");
            yield return Path.Combine(profile, "OneDrive", "Documents");
        }

        // A Documents folder relocated wholesale onto another drive, which is the same move that put
        // Steam on D: in the first place.
        string user = Environment.UserName;
        foreach (string drive in Drives())
        {
            yield return Path.Combine(drive, "Documents");
            yield return Path.Combine(drive, "Users", user, "Documents");
        }
    }

    // --- Plumbing ---------------------------------------------------------------------------------

    /// <summary>Fixed, mounted drives. Anything else is a card reader with no disk in it.</summary>
    private static IEnumerable<string> Drives()
    {
        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch (Exception)
        {
            return [@"C:\"];
        }

        return drives
            .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
            .Select(d => d.RootDirectory.FullName);
    }

    /// <summary>
    /// A registry string, or null for any reason at all. Reads can fail on a locked-down machine and
    /// none of them are worth an exception: every one of these is a hint, not a requirement.
    /// </summary>
    private static string? ReadRegistry(RegistryKey hive, string key, string value)
    {
        try
        {
            using var subkey = hive.OpenSubKey(key);
            return subkey?.GetValue(value) as string;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
