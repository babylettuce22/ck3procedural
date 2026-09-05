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

    /// <summary>
    /// Rulers' names, sex, birth, profile and purse — history/characters, and the bookmarks that
    /// describe the same characters.
    /// </summary>
    Rulers = 16,

    /// <summary>
    /// What realms and their holders are called — a culture's words per government, a title's own
    /// word — common/flavorization and its localisation.
    /// </summary>
    TitleWords = 32,

    /// <summary>
    /// Which vanilla look a culture's people wear — common/ethnicities, and the cultures file that
    /// names the variants. Humans only; a culture's race is fixed at generation.
    /// </summary>
    Ethnicities = 64,

    /// <summary>
    /// What a realm's rulers are — history/titles, and the province history whose capital holdings
    /// have to seat them.
    /// </summary>
    Governments = 128,
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

        if (aspects.HasFlag(WorldAspect.Ethnicities))
        {
            yield return "99_generated_ethnicities.txt";
            yield return "gen_ethnicities_l_english.yml";

            // A retemplate mints new variant keys, and the cultures file is what names them, so it
            // is rewritten even when nothing about the culture itself changed. Named once when a
            // culture edit is pending too.
            if (!aspects.HasFlag(WorldAspect.Cultures)) yield return "00_generated_cultures.txt";
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

        if (aspects.HasFlag(WorldAspect.Rulers))
        {
            yield return "00_generated_characters.txt";
            yield return "00_generated_challenge.txt";
            yield return "gen_history_l_english.yml";
        }

        if (aspects.HasFlag(WorldAspect.Governments))
        {
            yield return "00_generated_titles.txt";
            yield return "00_generated_provinces.txt";
        }

        // Named once by whichever got there first: the screen states each character's government
        // beside their name, so both aspects stale it.
        if (aspects.HasFlag(WorldAspect.Rulers) || aspects.HasFlag(WorldAspect.Governments))
            yield return "00_bookmarks.txt";

        if (aspects.HasFlag(WorldAspect.TitleWords))
        {
            yield return "zz_generated_flavorization.txt";
            yield return "gen_title_tiers_l_english.yml";
        }
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
    /// <param name="gameDir">
    /// The installed game, for the one file an edit can newly require: a realm turned nomadic on a
    /// map that had no hordes when it was written has no <c>zz_generated_nomad_government.txt</c>
    /// beside it, and without that override the engine names the horde after its culture and its
    /// house head instead of after its title — see <see cref="GovernmentWriter"/>. Optional because
    /// nothing else here reads the game, and a missing or wrong path costs only that name.
    /// </param>
    public static void Apply(string modDir, GenerationResult result, WrittenContent written,
        WorldAspect aspects, string? gameDir = null)
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
                written.Wilderness);

        if (aspects.HasFlag(WorldAspect.Cultures))
        {
            CultureWriter.WriteCultures(modDir, written.Cultures, written.Ethnicities);
            CultureWriter.WriteLocalisation(modDir, written.Cultures);
        }

        // The ethnicity file whole, plus the cultures file — a retemplated culture points at
        // variant keys that only exist in the rewritten ethnicity file, so shipping one without the
        // other leaves the culture naming ethnicities CK3 cannot resolve. Only when the culture
        // aspect did not already write it.
        if (aspects.HasFlag(WorldAspect.Ethnicities))
        {
            EthnicityWriter.WriteAll(modDir, written.Ethnicities);

            if (!aspects.HasFlag(WorldAspect.Cultures))
                CultureWriter.WriteCultures(modDir, written.Cultures, written.Ethnicities);
        }

        // WriteAll covers the faith localisation as well, so a faith edit subsumes the rewrite a
        // title rename would otherwise need. Only when it did not run does that have to happen
        // separately — holy site names are read live off the county title.
        if (aspects.HasFlag(WorldAspect.Faiths)) ReligionWriter.WriteAll(modDir, written.Faiths);
        else if (aspects.HasFlag(WorldAspect.TitleNames))
            ReligionWriter.WriteLocalisation(modDir, written.Faiths);

        // The character file whole — ancestors, rulers, spouses and children — from the same
        // function that wrote it, with the rulers' current values. Everything the block around a
        // ruler references (father, spouse, allies, claims) is keyed by ids that no edit can touch,
        // so the rewritten file still points where it did.
        if (aspects.HasFlag(WorldAspect.Rulers)
            && written.Rulers is { } rulers && written.Prehistory is { } prehistory)
        {
            HistoryWriter.WriteCharacters(modDir, result.Config, written.Cultures, written.Ethnicities,
                prehistory, rulers);
        }

        // The title history, for the government line beside every holder, and the province history,
        // for the capital holdings that have to seat them — each government names exactly one
        // primary_holding and a ruler on anything else cannot hold his own seat. The province file
        // is written from the rows the first write captured rather than rebuilt, so nothing but the
        // holdings this edit moved can differ. See WrittenContent.ProvinceHistory.
        if (aspects.HasFlag(WorldAspect.Governments)
            && written.Realms is { } governed && written.Governments is { } edited)
        {
            HistoryWriter.ReWriteTitleHistory(modDir, result.Config, result.Titles,
                written.Development, governed, edited, written.Faiths, written.Wilderness);

            ContentWriter.EmitProvinceHistory(modDir, written.ProvinceHistory, written.Holdings);

            // Only ever adds: the writer is a no-op when the map has no hordes, so a world that has
            // stopped having one keeps a harmless override rather than losing the file mid-edit.
            if (gameDir is not null && Core.GameLocator.IsGameDir(gameDir))
                GovernmentWriter.WriteNomadNaming(modDir, gameDir, edited.AnyNomad);
        }

        // The bookmark screen describes the same men, down to the age beside the name, the byname
        // after it and the government they rule under, so it is stale the moment either the
        // character file or the title history is not. The cast is replayed rather than reselected —
        // see WrittenContent.Bookmarks for why.
        if ((aspects.HasFlag(WorldAspect.Rulers) || aspects.HasFlag(WorldAspect.Governments))
            && written.Bookmarks is { } cast
            && written.Rulers is { } castRulers
            && written.Realms is { } realms
            && written.Governments is { } governments)
        {
            BookmarkWriter.ReWrite(modDir, result.Config, cast, result.Titles, realms,
                written.Cultures, written.Faiths, governments, written.Wilderness, castRulers,
                result.Azgaar);
        }

        // Both files whole, from the words now on the cultures and titles. The writer is pure —
        // the draw happened in Assign at generation — so this cannot reshuffle anyone's vocabulary.
        if (aspects.HasFlag(WorldAspect.TitleWords))
            TitleTierWriter.WriteAll(modDir, written.Cultures, result.Titles);
    }

    /// <summary>What just happened, and the things about it that surprise people.</summary>
    /// <param name="written">
    /// The world as edited, for the one warning that has to be counted rather than stated: a horde
    /// standing outside the Great Steppe. Optional — without it that check is skipped, and nothing
    /// else here reads it.
    /// </param>
    public static void Report(WorldAspect aspects, int edited, string modDir,
        WrittenContent? written = null)
    {
        Console.WriteLine($"Rewrote {string.Join(", ", FilesFor(aspects))} in {modDir}");
        Console.WriteLine($"  {edited} edited {(edited == 1 ? "object" : "objects")} applied");

        if (aspects.HasFlag(WorldAspect.TitleNames))
            Console.WriteLine("  wonder and artifact descriptions keep the name they were generated with");

        if (aspects.HasFlag(WorldAspect.Rulers))
        {
            Console.WriteLine("  artifact and chronicle prose keeps the ruler's generated name; "
                              + "fathers, spouses and children keep their generated dates");
            Console.WriteLine("  the bookmark screen follows the edit, but who is on it does not "
                              + "change — the realm outlines and portraits were drawn for them");
        }

        if (aspects.HasFlag(WorldAspect.Governments))
        {
            Console.WriteLine("  the capital holding of every county in the realm follows the "
                              + "government; a second holding keeps its type, and a barony holding "
                              + "a wonder or a bazaar keeps its holding even under a horde");
            Console.WriteLine("  what the government was at generation still shows in the ground "
                              + "and the men: the rulers' purses, schooling, dread and legitimacy, "
                              + "their culture's men-at-arms, and the farmland cultivation were all "
                              + "decided from it and keep what they were given");

            // Named on its own because it is the one of those a change can make meaningless rather
            // than merely dated: republics and theocracies do not declare legitimacy = yes, so a
            // realm moved onto one carries an add_legitimacy the engine has no currency for. The
            // ruler window is where that is cleared, and its Legitimacy dropdown takes a blank.
            Console.WriteLine("  a realm moved onto a republic or a theocracy keeps a legitimacy "
                              + "its government cannot hold — blank it on the ruler (Ruler…) if the "
                              + "line bothers you; the engine ignores it either way");

            // The one consequence that is a mechanic rather than a flavour: vanilla's Migrate
            // interaction requires the actor to be a participant of a migration situation, and the
            // Great Steppe was bound to the ground and to the hordes as they stood when the mod was
            // written. Counted rather than stated, because on most edits the number is zero.
            if (written is { Steppe: { } steppe, Governments: { } governments })
            {
                int stranded = steppe.IsEmpty
                    ? governments.NomadCounties.Count()
                    : governments.NomadCounties.Count(c => !steppe.Contains(c));

                if (stranded > 0)
                    Console.WriteLine($"  WARNING {stranded} nomadic {(stranded == 1 ? "county is" : "counties are")} "
                                      + "outside the Great Steppe, which was bound to the ground and "
                                      + "the hordes this mod was written with — a horde outside the "
                                      + "situation can never migrate. Revert the government, or "
                                      + "write the mod again to cut the belt around it.");
            }
        }

        if (aspects.HasFlag(WorldAspect.TitleWords))
            Console.WriteLine("  a culture's realm words apply to every realm whose top liege is of "
                              + "that culture; a title's own word outranks them");

        if (aspects.HasFlag(WorldAspect.Ethnicities))
        {
            Console.WriteLine("  only the retemplated cultures change — their heritage siblings keep "
                              + "the look they were generated with");
            Console.WriteLine("  characters already in a save keep their rolled appearance; the new "
                              + "look applies to a new game");
        }

        Console.WriteLine("  CK3 caches these — restart the game, not just the mod, to see it");
    }
}
