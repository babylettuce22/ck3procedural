namespace Ck3MapGen.Emit;

/// <summary>
/// Seeds the mod from ck2rpg's shipped template before anything generated is written.
///
/// This is the workflow ck2rpg's own tutorial prescribes: "Move the files over from the template
/// (except descriptor.mod)", then let the generator overwrite the map-specific files. The
/// template is a known-working CK3 map mod, so starting from it removes a whole class of unknowns
/// at once rather than rediscovering each missing file through a load that fails silently.
///
/// What it supplies that we do not generate, and that vanilla cannot supply because vanilla's
/// copies are sized for the 9216x4608 map:
///   gfx/map/terrain/  colormap.dds, detail_index.tga, detail_intensity.tga, 70 terrain masks,
///                     masks_gen, flat_maps/flatmap.dds
///   gfx/map/water/    flowmap, foam_map, watercolor
///   gfx/map/surround_map/
///   map_data/         nodes.dat, positions.txt (empty), climate.txt, island_region.txt
///   common/           cultures, name_lists, pillars, ethnicities, religions, holy_sites
///
/// `replacer.py` in the template confirms the split: it maps generated output onto a fixed set of
/// destinations, and anything not in that mapping — nodes.dat and island_region.txt among them —
/// is simply kept from the template.
/// </summary>
public static class TemplateWriter
{
    /// <summary>
    /// Template-only scaffolding that must not end up in the mod. descriptor.mod is excluded
    /// because ours carries the real name and supported_version; replacers/ and content_source/
    /// are authoring inputs for the GIMP script, not game data.
    /// </summary>
    private static readonly string[] SkipTopLevel =
        ["descriptor.mod", "replacers", "content_source", "manifest.json", "thumbnail.png"];

    public static void CopyTemplate(string modDir, string templateDir)
    {
        if (!Directory.Exists(templateDir))
        {
            Console.WriteLine($"  template: {templateDir} not found, skipped");
            return;
        }

        int files = 0;
        long bytes = 0;

        foreach (string source in Directory.GetFiles(templateDir, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(templateDir, source);
            string top = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
            if (SkipTopLevel.Contains(top, StringComparer.OrdinalIgnoreCase)) continue;

            string destination = Path.Combine(modDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);

            files++;
            bytes += new FileInfo(source).Length;
        }

        Console.WriteLine($"  template: copied {files} files ({bytes / 1024 / 1024} MB) as the baseline");
    }
}
