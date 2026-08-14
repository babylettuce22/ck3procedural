namespace Ck3MapGen.Emit;

/// <summary>
/// Copies the hand-kept files in BaseFilesToCopy/ into the mod.
///
/// That folder is not itself a mod root. Each immediate subfolder is one *file set*, and each set
/// is a mod root, so a file's path below its set folder is its path in the mod. Sets exist so a
/// feature can be switched off wholesale from <see cref="Config.MapConfig"/> without the writer
/// needing to know a single filename — <see cref="Wilderness"/> ships only when the wilderness and
/// colonisation system is enabled, and adding a second optional system means adding a folder and a
/// bool, not editing this class.
///
/// It exists because replace_path is all-or-nothing: declaring gfx/map/map_object_data drops
/// every vanilla file under it, including the ones we do not generate and have no reason to
/// (the map_table_* meshes, say). Those have to come back from somewhere, and copying a file we
/// keep beats teaching a writer to reproduce something it does not own.
///
/// Runs last, and never overwrites: anything the pipeline generated wins over anything kept
/// here, so dropping a file in this folder can add to the mod but cannot silently replace part
/// of it.
/// </summary>
public static class StaticFileWriter
{
    /// <summary>Folder name, both in the repo and beside the built executable.</summary>
    public const string SourceFolder = "BaseFilesToCopy";

    /// <summary>Vanilla files nothing regenerates. Always copied.</summary>
    public const string Core = "Core";

    /// <summary>The static half of the wilderness and colonisation system. Gated.</summary>
    public const string Wilderness = "Wilderness";

    /// <summary>
    /// Files that document a set rather than belong in a mod. Copying README.txt would put it in
    /// the mod root, where CK3 ignores it and the next person to look wonders what wrote it.
    /// </summary>
    private static readonly string[] NotModContent = ["README.txt"];

    /// <summary>
    /// Where a set lives beside the executable. Beside it rather than in the repo because the
    /// csproj copies the folder to the output directory, so a published build carries it too.
    /// </summary>
    public static string SetDirectory(string set)
        => Path.Combine(AppContext.BaseDirectory, SourceFolder, set);

    /// <param name="runStarted">
    /// When this generation run began. It is what separates "a writer produced this file a moment
    /// ago" from "a previous run left this file here", and without it the never-overwrite rule
    /// below quietly becomes never-update.
    ///
    /// The mod directory is not cleaned between runs, so a file copied here on Monday is still
    /// sitting in the mod on Friday. Skipping every destination that merely *exists* therefore
    /// pinned the static sets to whatever they looked like the first time the mod was generated:
    /// editing a script under BaseFilesToCopy changed nothing, forever, with no error and no
    /// warning. That cost a full day of testing against scripts that were never shipped.
    /// </param>
    public static void WriteAll(string modDir, IEnumerable<string> sets, DateTime runStarted)
    {
        int copied = 0, skipped = 0, refreshed = 0;
        var written = new List<string>();

        foreach (string set in sets)
        {
            string sourceDir = SetDirectory(set);

            if (!Directory.Exists(sourceDir))
            {
                Console.WriteLine($"  static files: SKIPPED set '{set}' " +
                                  $"({SourceFolder}/{set} not found beside the executable)");
                continue;
            }

            int setCopied = 0;

            foreach (string sourceFile in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                string relativePath = Path.GetRelativePath(sourceDir, sourceFile);
                if (NotModContent.Contains(Path.GetFileName(relativePath), StringComparer.OrdinalIgnoreCase))
                    continue;

                string targetFile = Path.Combine(modDir, relativePath);

                if (File.Exists(targetFile))
                {
                    // Written during THIS run means a generator owns it, and a generator always
                    // wins — that is the rule this writer exists under. Anything older is debris
                    // from a previous run and must be replaced, or the set can never be edited.
                    if (File.GetLastWriteTimeUtc(targetFile) >= runStarted)
                    {
                        skipped++;
                        continue;
                    }

                    File.Delete(targetFile);
                    refreshed++;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
                File.Copy(sourceFile, targetFile);
                setCopied++;
            }

            copied += setCopied;
            written.Add($"{setCopied} from {set}");
        }

        Console.WriteLine($"  copied {copied} static files ({string.Join(", ", written)}; " +
                          (refreshed > 0 ? $"{refreshed} refreshed from an earlier run; " : "") +
                          $"{skipped} left alone, already generated)");
    }
}
