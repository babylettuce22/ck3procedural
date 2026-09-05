using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// The parts of a finished write that outlive it, so the mod on disk can be amended without
/// generating it again.
///
/// <see cref="ContentWriter.WriteAll"/> derives all of this and used to drop it on the floor, which
/// was fine while writing was the last thing that ever happened to a mod. Editing title names after
/// the fact changes that: re-emitting the two localisation files those names reach means having the
/// same water names and the same faiths the write used, and regenerating them would be both slower
/// and — for anything downstream of a <see cref="Rng"/> that has since moved — not necessarily the
/// same answer.
///
/// Deliberately small. This is not a snapshot of the run; it is the minimum needed by
/// <see cref="WorldOverwrite"/>, plus <see cref="Cultures"/>, which is what lets a title be renamed
/// from the language of the people who actually live there rather than from a global pool.
///
/// Captured at the *end* of the write rather than as each piece is built: both the culture and the
/// faith maps gain their unsettled entries partway through, so an early capture would be missing
/// the wilderness.
/// </summary>
public sealed record WrittenContent
{
    public required CultureMap Cultures { get; init; }

    /// <summary>The generated ethnicities, needed to rewrite the culture files after an edit.</summary>
    public required EthnicityMap Ethnicities { get; init; }
    public required FaithMap Faiths { get; init; }

    /// <summary>Province id to name, for the rivers and sea zones that share the title
    /// localisation file. Untouched by renaming — water is named from a culture's phonology
    /// rather than from any title — but needed in full to re-emit that file.</summary>
    public required Dictionary<int, string> WaterNames { get; init; }

    /// <summary>Needed to re-emit landed_titles, which carries every title's colour.</summary>
    public required WildernessMap Wilderness { get; init; }

    /// <summary>
    /// Every county's development level, as the province history was written from it.
    ///
    /// Kept rather than recomputed because it cannot be recomputed on its own: the level a county
    /// ships with comes from the second <see cref="MapGen.Development.ForCounties"/> pass, the one
    /// that sees <see cref="WorldCenters"/>, and world centers do not exist until the write has
    /// built the cultures they are placed from.
    /// </summary>
    public required Dictionary<Title, int> Development { get; init; }

    /// <summary>
    /// The holding each barony was given, by province id — the <c>holding =</c> line in
    /// <c>history/provinces</c>, verbatim.
    ///
    /// Baronies written <c>none</c> are in here as <c>none</c> rather than absent, so a reader can
    /// tell an empty slot from a barony this map never covered.
    ///
    /// Captured rather than replayed for the same reason as <see cref="Development"/>, and a
    /// sharper one: the holdings come off a single <see cref="Rng"/> walked across every barony in
    /// the world in one pass, so reproducing them means reproducing every draw before them in
    /// order. Any county whose government differed would desync the rest of the stream.
    /// </summary>
    public required Dictionary<int, string> Holdings { get; init; }

    /// <summary>
    /// The rest of every barony's province-history line, in the order it was written — culture,
    /// faith, and the special building slot a wonder or a bazaar claimed.
    ///
    /// Kept beside <see cref="Holdings"/> rather than folded into it because the two answer
    /// different questions: this is what an edit may never change, that is the one field it can.
    /// Together they are enough to write <c>00_generated_provinces.txt</c> again from
    /// <see cref="ContentWriter.EmitProvinceHistory"/> — which is what a government changed after
    /// the fact needs, since each government seats its ruler in a different holding.
    /// </summary>
    public required IReadOnlyList<ContentWriter.ProvinceRow> ProvinceHistory { get; init; }

    /// <inheritdoc cref="Wilderness"/>
    public required WorldCenterMap WorldCenters { get; init; }

    /// <summary>
    /// Who holds what at the bookmark date, for the landed-realm view.
    ///
    /// Null when the mod was written with history skipped: realms are built inside that phase and
    /// there is nothing to show without it. Every reader has to cope, which is why this is the one
    /// nullable member here.
    /// </summary>
    public RealmMap? Realms { get; init; }

    /// <summary>
    /// The living ruler of every seat — name, birth, house, profile, purse — as the character file
    /// and bookmarks were written from them. Null for the same reason <see cref="Realms"/> is: built
    /// inside the history phase, absent when it was skipped. What the ruler inspector holds.
    /// </summary>
    public RulerMap? Rulers { get; init; }

    /// <summary>
    /// The family and relations written around every ruler — ancestors, spouses, children,
    /// alliances, claims — which the character file carries beside the rulers themselves and so
    /// has to be re-emitted from. Null exactly when <see cref="Rulers"/> is.
    /// </summary>
    public PrehistoryMap? Prehistory { get; init; }

    /// <summary>Needed to re-emit the bookmarks, which name each character's government.</summary>
    public GovernmentMap? Governments { get; init; }

    /// <summary>
    /// Where the Great Steppe situation was bound, so that an edit can tell whether a realm it has
    /// just turned nomadic is standing inside it.
    ///
    /// Not re-derivable and not re-emitted. The belt is cut from the ground <em>and</em> from where
    /// the hordes were when the mod was written, and its sub-regions are partitioned by a growth
    /// pass off its own <see cref="Rng"/>; rebuilding it for one changed realm would redraw every
    /// seam on the map. So it is kept only to be asked a question — <see cref="SteppeMap.Contains"/>
    /// — and the answer goes in the overwrite report rather than into a file.
    /// </summary>
    public SteppeMap? Steppe { get; init; }

    /// <summary>
    /// Who stands on the bookmark screen and what it says about them.
    ///
    /// Kept whole rather than reselected: the realm highlights and the portrait DNA were written
    /// against these six characters, so a re-emit that picked afresh would leave a bookmark
    /// pointing at a face and a map outline belonging to somebody else. Null exactly when
    /// <see cref="Rulers"/> is, and also on a world too small to have produced a cast at all.
    /// </summary>
    public BookmarkCast? Bookmarks { get; init; }

    /// <summary>The province id boundaries the title localisation file is written against.</summary>
    public required int BaronyCount { get; init; }

    public required int LandCount { get; init; }
    public required int RiverCount { get; init; }
}
