using Ck3MapGen.Core;

namespace Ck3MapGen.Emit;

/// <summary>
/// What kind of edit is pending, and so which files an overwrite has to touch.
///
/// A flags enum rather than a single dirty bit because each member owns a different set of files
/// and there is no reason a recolour should rewrite localisation. It also keeps the log honest
/// about what was actually republished, and it is how this grows: a new editable thing is a new
/// member plus a branch in <see cref="WorldOverwrite.Apply"/>.
/// </summary>
[Flags]
public enum WorldAspect
{
    None = 0,

    /// <summary>Title display names — localization/english.</summary>
    TitleNames = 1,

    /// <summary>Title colours — common/landed_titles.</summary>
    TitleColors = 2,

    /// <summary>Culture names, colours, ethos and traditions — common/culture/cultures.</summary>
    Cultures = 4,

    /// <summary>Faith and religion names, colours and tenets — common/religion.</summary>
    Faiths = 8,
}

/// <summary>
/// Pushes edits into a mod that has already been written.
///
/// Nothing here is structural. Every editable thing keeps its <c>Key</c>, which is what the rest of
/// the mod references — landed_titles, province history, holy sites, geographical regions,
/// bookmarks — so the entire cost of an edit is re-emitting the handful of files that carry the
/// values themselves. That is milliseconds against the minutes a write costs.
///
/// Every file is rewritten whole rather than patched. Patching would mean a second, subtly
/// different serialiser to keep in step with the writer; regenerating from the same function the
/// write used cannot drift from it.
///
/// What this deliberately does not fix is prose with a value baked into it. A wonder is called "The
/// Grand Archives of {county}" and an artifact "Crown of {title}", and both were composed as
/// complete strings at generation time — re-emitting their files would faithfully write the old
/// name back out. Correcting those means re-running the generators that composed them, not the
/// writers that emitted them, so they keep the name they were born with. <see cref="Report"/> says
/// so out loud rather than leaving it to be discovered in game.
/// </summary>
public static class WorldOverwrite
{
    /// <summary>The files each aspect owns, for the log.</summary>
    public static IEnumerable<string> FilesFor(WorldAspect aspects)
    {
        if (aspects.HasFlag(WorldAspect.TitleNames)) yield return "gen_titles_l_english.yml";
        if (aspects.HasFlag(WorldAspect.TitleColors)) yield return "00_landed_titles.txt";

        if (aspects.HasFlag(WorldAspect.Cultures))
        {
            yield return "00_generated_cultures.txt";
            yield return "gen_cultures_l_english.yml";
        }

        if (aspects.HasFlag(WorldAspect.Faiths))
        {
            yield return "00_generated_religions.txt";
            yield return "01_generated_holy_sites.txt";
        }

        // Written by a title rename (holy site names read the county's name live) and by any faith
        // edit, so it is named once whichever got there first.
        if (aspects.HasFlag(WorldAspect.TitleNames) || aspects.HasFlag(WorldAspect.Faiths))
            yield return "gen_faiths_l_english.yml";
    }

    /// <summary>
    /// Re-emits whichever files <paramref name="aspects"/> covers, from the current state of the
    /// objects in <paramref name="result"/> and <paramref name="written"/>.
    /// </summary>
    /// <exception cref="DirectoryNotFoundException">
    /// The mod folder has gone away since it was written. Worth failing loudly: the alternative is
    /// silently recreating a directory holding a few orphaned files, which the launcher would list
    /// as a mod and the game would load as an empty one.
    /// </exception>
    public static void Apply(string modDir, GenerationResult result, WrittenContent written,
        WorldAspect aspects)
    {
        if (aspects == WorldAspect.None) return;

        if (!Directory.Exists(modDir))
            throw new DirectoryNotFoundException(
                $"The mod folder '{modDir}' is no longer there. Write the mod again before editing it.");

        if (aspects.HasFlag(WorldAspect.TitleNames))
            ContentWriter.WriteLocalisation(modDir, result.Titles, written.WaterNames,
                result.Provinces, result.ProvinceOrder, written.BaronyCount, written.LandCount,
                written.RiverCount);

        if (aspects.HasFlag(WorldAspect.TitleColors))
            // The whole de jure tree, not just the recoloured title: this file carries the
            // hierarchy itself, and there is no meaningful way to rewrite one colour inside it
            // without reproducing the writer.
            ContentWriter.WriteLandedTitles(modDir, result.Titles, written.Faiths,
                written.Wilderness, written.WorldCenters);

        if (aspects.HasFlag(WorldAspect.Cultures))
        {
            CultureWriter.WriteCultures(modDir, written.Cultures);
            CultureWriter.WriteLocalisation(modDir, written.Cultures);
        }

        // WriteAll covers the faith localisation as well, so a faith edit subsumes the rewrite a
        // title rename would otherwise need. Only when it did not run does that have to happen
        // separately — holy site names are read live off the county title.
        if (aspects.HasFlag(WorldAspect.Faiths)) ReligionWriter.WriteAll(modDir, written.Faiths);
        else if (aspects.HasFlag(WorldAspect.TitleNames))
            ReligionWriter.WriteLocalisation(modDir, written.Faiths);
    }

    /// <summary>What just happened, and the things about it that surprise people.</summary>
    public static void Report(WorldAspect aspects, int edited, string modDir)
    {
        Console.WriteLine($"Rewrote {string.Join(", ", FilesFor(aspects))} in {modDir}");
        Console.WriteLine($"  {edited} edited {(edited == 1 ? "object" : "objects")} applied");

        if (aspects.HasFlag(WorldAspect.TitleNames))
            Console.WriteLine("  wonder and artifact descriptions keep the name they were generated with");

        Console.WriteLine("  CK3 caches these — restart the game, not just the mod, to see it");
    }
}
