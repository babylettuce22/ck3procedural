namespace Ck3MapGen.Emit;

/// <summary>
/// Copies the hand-kept files in VanillaFilesToCopy/ into the mod, laid out as a mod root so a
/// file's path in that folder is its path in the mod.
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
    public const string SourceFolder = "VanillaFilesToCopy";

    /// <summary>
    /// Files that document the folder rather than belong in a mod. Copying README.txt would put
    /// it in the mod root, where CK3 ignores it and the next person to look wonders what wrote it.
    /// </summary>
    private static readonly string[] NotModContent = ["README.txt"];

    public static void WriteAll(string modDir)
    {
        // Beside the executable, not in the repo: the csproj copies the folder to the output
        // directory, so a published build carries it too.
        string sourceDir = Path.Combine(AppContext.BaseDirectory, SourceFolder);

        if (!Directory.Exists(sourceDir))
        {
            Console.WriteLine($"  static files: SKIPPED ({SourceFolder} not found beside the executable)");
            return;
        }

        int copied = 0, skipped = 0;

        foreach (string sourceFile in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourceDir, sourceFile);
            if (NotModContent.Contains(relativePath, StringComparer.OrdinalIgnoreCase)) continue;

            string targetFile = Path.Combine(modDir, relativePath);

            if (File.Exists(targetFile))
            {
                skipped++;
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            File.Copy(sourceFile, targetFile);
            copied++;
        }

        Console.WriteLine($"  copied {copied} static files ({skipped} left alone, already generated)");
    }
}
