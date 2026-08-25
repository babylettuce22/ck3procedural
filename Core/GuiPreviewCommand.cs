using Ck3MapGen.GameGui;
using Ck3MapGen.GameGui.Preview;
using Ck3MapGen.Io;

namespace Ck3MapGen.Core;

/// <summary>
/// The <c>--preview</c> command: draw a <c>.gui</c> widget as a page you can look at.
///
/// Wiring, and nothing more. Everything it does is done by the three pieces in
/// <c>GameGUI/Preview</c> plus <see cref="GuiTextures"/>; what lives here is the decision about
/// where to look for files, which is the one thing those pieces deliberately do not know.
///
/// It exists because the alternative way to check a generated window is to launch CK3, load a save,
/// and open it — several minutes per look, and impossible to do at all for a widget behind a
/// scripted_gui that has not fired. This takes about a second and is wrong in known ways rather
/// than unknown ones: see <see cref="GuiLayout"/> for what is approximated.
/// </summary>
public static class GuiPreviewCommand
{
    /// <summary>
    /// Renders one widget to HTML. <paramref name="target"/> is a widget name, or a path to a
    /// <c>.gui</c> file whose last top-level widget is taken.
    /// </summary>
    public static int Run(string target, string gameDir, string? modDir, string outPath, int rows = 1)
    {
        string gameGui = Path.Combine(gameDir, "gui");

        if (!Directory.Exists(gameGui))
        {
            Console.Error.WriteLine($"preview: no gui folder under '{gameDir}'. "
                + "Point --game at the CK3 'game' folder.");
            return 1;
        }

        // Vanilla first, the mod second: a redeclared type resolves the way the game resolves it.
        var roots = new List<string> { gameGui };
        if (modDir is not null && Directory.Exists(Path.Combine(modDir, "gui")))
            roots.Add(Path.Combine(modDir, "gui"));

        Console.WriteLine($"preview: indexing {string.Join(", ", roots)}");

        var library = GuiLibrary.Load([.. roots]);
        library.ItemRows = rows;

        Console.WriteLine($"preview: {library.TypeCount} types, {library.TemplateCount} templates");

        var (node, source, authored) = Find(library, target);

        if (node is null)
        {
            Console.Error.WriteLine($"preview: no widget called '{target}'. "
                + "Pass a widget name or a .gui file path.");
            return 1;
        }

        var resolved = library.Resolve(node);

        // The mod's own art wins over vanilla's, which matters for anything this project ships.
        var textures = new GuiTextures([
            .. modDir is not null ? new[] { modDir } : [],
            gameDir,
        ]);

        // Vanilla first, the mod second, so a key this project redefines reads the way it will
        // in game.
        var loc = LocLibrary.Load([
            gameDir,
            .. modDir is not null ? new[] { modDir } : [],
        ]);

        var preview = new GuiPreview
        {
            Title = $"{resolved.Label} — {source}",
            Textures = textures.DataUri,
            Localise = loc.Text,
        };

        string html = preview.Render(resolved);

        // Written after the render, because the counts are only known once every texture the tree
        // asks for has been asked for.
        preview.Report.Add($"source: {source}");
        preview.Report.Add($"resolved: {library.TypeCount} types, {library.TemplateCount} templates indexed");
        preview.Report.Add($"textures: {textures.Loaded} drawn, {textures.Missing.Count} unavailable");
        preview.Report.Add($"localisation: {loc.Count} keys");

        if (rows > 1) preview.Report.Add($"datamodel rows simulated: {rows}");

        // Calls this file makes that vanilla never does. A question, not a failure — see
        // GuiVocabulary — but it is the only check in the toolchain that catches a datafunction
        // name that is merely WRONG rather than malformed.
        var unknown = library.Vocabulary.Unknown(authored);

        if (unknown.Count > 0)
        {
            preview.Report.Add($"calls vanilla never makes ({unknown.Count}) — check the spelling:");

            foreach (var (call, uses) in unknown.Take(10))
                preview.Report.Add($"  {call} ×{uses}");
        }

        foreach (string missing in textures.Missing.Take(8))
            preview.Report.Add($"  no texture: {missing}");

        // Re-rendered so the report panel carries the counts. Cheap, and the alternative is
        // threading a second pass through the renderer for no gain.
        html = preview.Render(resolved);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        File.WriteAllText(outPath, html);

        Console.WriteLine($"preview: {Count(resolved)} widgets, {textures.Loaded} textures "
            + $"-> {outPath}");

        return 0;
    }

    /// <summary>
    /// The widget to draw: a file's last top-level widget, or a name from the indexed library.
    ///
    /// "Last" rather than first because that is where a scripted_widgets file keeps the line that
    /// matters — a file of <c>type</c> declarations ends with the bare instantiation that makes one
    /// of them real, and that instance is the thing worth looking at.
    /// </summary>
    /// <summary>
    /// The widget to draw, where it came from, and the nodes this project actually wrote.
    ///
    /// The third of those is for the vocabulary check, and it is not the same as the first. What
    /// gets DRAWN is one instance — for a scripted_widgets file, a bare `host = {}` with nothing in
    /// it — while everything the file says lives in the `type` declarations beside it. Checking the
    /// drawn node alone finds no calls at all.
    /// </summary>
    private static (GuiNode? Node, string Source, IReadOnlyList<GuiNode> Authored) Find(
        GuiLibrary library, string target)
    {
        if (File.Exists(target))
        {
            var document = GuiParser.Parse(File.ReadAllText(target), Path.GetFileName(target));

            // Indexed as well as read, so a file declaring its own types can resolve against them.
            library.Index(document, Path.GetFileName(target));

            var instance = document.Roots.LastOrDefault(r => r.IsBlock && r.Key != "types");
            return (instance, Path.GetFileName(target), document.Roots);
        }

        var named = library.Instance(target);
        return (named, "library", named is null ? [] : [named]);
    }

    private static int Count(ResolvedWidget widget)
        => 1 + widget.Children.Sum(Count);
}
