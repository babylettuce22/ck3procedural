namespace Ck3MapGen.Core;

/// <summary>
/// Finds things under the repo's <c>assets</c> folder, which ships copied beside the built exe.
///
/// Checked beside the binary first: that copy is what a packaged build has, and it is what a run
/// of the built exe should see. A <c>dotnet run</c> from the repo resolves the working directory
/// instead, and <c>bin/Debug/&lt;tfm&gt;/../../..</c> reaches the checkout from a stale build.
/// The flip side of binary-first is that an asset added since the last build is invisible to that
/// binary until it is rebuilt.
/// </summary>
public static class AssetPaths
{
    /// <summary>The asset file at <paramref name="relPath"/> ('/' or '\' separated), or null.</summary>
    public static string? File(string relPath) => Candidates(relPath).FirstOrDefault(System.IO.File.Exists);

    /// <summary>The asset folder at <paramref name="relPath"/>, or null.</summary>
    public static string? Directory(string relPath) => Candidates(relPath).FirstOrDefault(System.IO.Directory.Exists);

    private static IEnumerable<string> Candidates(string relPath)
    {
        string rel = relPath.Replace('/', Path.DirectorySeparatorChar);

        yield return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "assets", rel));
        yield return Path.GetFullPath(Path.Combine(System.IO.Directory.GetCurrentDirectory(), "assets", rel));
        yield return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "assets", rel));
    }
}
