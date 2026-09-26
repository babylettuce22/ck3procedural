using System.Diagnostics;
using Ck3MapGen.Emit.Relief;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// Renders every generated faith's icon, and the reformed icon of every unreformed faith, into
/// <c>gfx/interface/icons/faith</c>. The designs come from <see cref="FaithIcons"/>; the renderer
/// is <see cref="Relief"/>.
///
/// Vanilla's faith icons are 100×100 uncompressed BGRA with no mips, and so are these. They are
/// drawn on an 800-pixel canvas and Lanczos-downsampled, which is what keeps thin engraving
/// crisp at the sizes the game actually shows them (30-60 pixels in most places).
///
/// Faiths that share a design (the same motif in the same frame) share one heightfield and one
/// surface; only the shading runs per faith. Designs render in parallel, each independently, so
/// the output does not depend on scheduling.
///
/// Called from <see cref="ReligionWriter.WriteAll"/>, so an editor save that rewrites the faiths
/// redraws the icons too: a changed faith colour changes the enamel.
/// </summary>
public static class FaithIconWriter
{
    public const string IconDir = "gfx/interface/icons/faith";
    public const int IconSize = 100;

    /// <summary>Supersampling canvas: 8x the icon, enough for engraving to survive the downsample.</summary>
    public const int CanvasSize = 800;

    public static void WriteAll(string modDir, FaithMap faiths, int seed)
    {
        var recipes = FaithIcons.Recipes(faiths, seed).Where(r => FaithIcons.HasGeneratedIcon(r.Faith)).ToList();
        if (recipes.Count == 0) return;

        var sw = Stopwatch.StartNew();
        string dir = Path.Combine(modDir, IconDir.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(dir);

        var designs = recipes.GroupBy(r => (r.Motif, r.Frame)).ToList();
        int written = 0;

        // One design per thread; the raster operations underneath are sequential on purpose. A
        // design's canvas and surface run to ~70 MB at 800 px, which bounds the fan-out too.
        var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount - 2, 1, 12) };
        try
        {
            Parallel.ForEach(designs, options, design =>
            {
                var (canvas, fieldInlay) = ReliefMotifs.Build(design.Key.Motif, design.Key.Frame, CanvasSize);
                var surface = ReliefShading.Surface(canvas, fieldInlay);

                foreach (var recipe in design)
                {
                    var faith = recipe.Faith;
                    var (main, inlay) = MaterialsFor(recipe.Material, faith.Color);
                    DdsWriter.WriteBgra(Path.Combine(dir, faith.Icon + ".dds"), IconSize, IconSize,
                        ReliefShading.RenderIcon(surface, main, inlay, IconSize));
                    Interlocked.Increment(ref written);

                    if (recipe.ReformedMaterial is { } upgraded && faith.ReformedIcon is { } reformedKey)
                    {
                        var (rMain, rInlay) = MaterialsFor(upgraded, faith.Color);
                        DdsWriter.WriteBgra(Path.Combine(dir, reformedKey + ".dds"), IconSize, IconSize,
                            ReliefShading.RenderIcon(surface, rMain, rInlay, IconSize));
                        Interlocked.Increment(ref written);
                    }
                }
            });
        }
        finally
        {
            ReliefShading.ClearCache();
        }

        int families = recipes.Select(r => r.Family).Distinct().Count();
        Console.WriteLine($"  faith icons: {written} rendered for {recipes.Count} faiths " +
                          $"({designs.Count} designs, {families} motif families) in {sw.ElapsedMilliseconds} ms");
    }

    /// <summary>
    /// Finished preview icons by everything that decides their pixels, so a repaint after a faith
    /// edit re-shades only the faiths whose recipe or colour actually changed.
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte[]> PreviewCache = new(StringComparer.Ordinal);

    /// <summary>
    /// Every generated faith's icon, rendered in memory at <paramref name="size"/> pixels for an
    /// on-screen preview: BGRA, the same design, materials and colours the mod gets, from the
    /// faiths as they stand now — so an edit shows before it is saved. Faiths showing a vanilla
    /// icon are absent; the caller loads those from the game.
    /// </summary>
    public static Dictionary<Faith, byte[]> RenderPreview(FaithMap faiths, int seed, int size)
    {
        var recipes = FaithIcons.Recipes(faiths, seed).Where(r => FaithIcons.HasGeneratedIcon(r.Faith)).ToList();
        var result = new Dictionary<Faith, byte[]>();
        var missing = new List<(FaithIconRecipe Recipe, string Key)>();

        foreach (var r in recipes)
        {
            var (cr, cg, cb) = r.Faith.Color;
            string key = $"{r.Motif}|{r.Frame}|{r.Material}|{cr:F3},{cg:F3},{cb:F3}|{size}";
            if (PreviewCache.TryGetValue(key, out var cached)) result[r.Faith] = cached;
            else missing.Add((r, key));
        }

        var rendered = new System.Collections.Concurrent.ConcurrentDictionary<Faith, byte[]>();
        Parallel.ForEach(missing.GroupBy(m => (m.Recipe.Motif, m.Recipe.Frame)), design =>
        {
            var (canvas, fieldInlay) = ReliefMotifs.Build(design.Key.Motif, design.Key.Frame, size * 8);
            var surface = ReliefShading.Surface(canvas, fieldInlay);
            foreach (var (recipe, key) in design)
            {
                var (main, inlay) = MaterialsFor(recipe.Material, recipe.Faith.Color);
                var bgra = ReliefShading.RenderIcon(surface, main, inlay, size);
                PreviewCache[key] = bgra;
                rendered[recipe.Faith] = bgra;
            }
        });
        ReliefShading.ClearCache();

        foreach (var (faith, bgra) in rendered) result[faith] = bgra;
        return result;
    }

    /// <summary>
    /// The main material and the inlay for one faith. The inlay is enamel in the faith's colour:
    /// glossy on metal, matt on wood, stone and iron, and bone where the colour is too grey to
    /// read as enamel at all. An all-enamel icon and an obsidian one take gold.
    /// </summary>
    private static (Material Main, Material? Inlay) MaterialsFor(string name, (double R, double G, double B) colour)
    {
        var shading = ReliefShading.Materials;
        if (name == "enamel") return (ReliefShading.Enamel(colour), shading["gold"]);
        if (name == "obsidian") return (shading["obsidian"], shading["gold"]);
        if (name == "bone") return (shading["bone"], shading["obsidian"]);

        if (name is "wood" or "stone" or "iron")
        {
            double spread = Math.Max(colour.R, Math.Max(colour.G, colour.B)) - Math.Min(colour.R, Math.Min(colour.G, colour.B));
            return (shading[name], spread * 255 > 40 ? ReliefShading.Enamel(colour, glossy: false) : shading["bone"]);
        }
        return (shading[name], ReliefShading.Enamel(colour));
    }
}
