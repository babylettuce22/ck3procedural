namespace Ck3MapGen.Emit;

using Ck3MapGen.Io;
using Ck3MapGen.MapGen;
using System.IO;

/// <summary>
/// Makes weapons that *vanilla* creates use the forge too.
///
/// **The problem.** Our own artifacts name a forged look directly — <c>visuals =
/// gen_forged_sword_03_visuals</c> — but nothing else in the game does. Every weapon vanilla mints
/// goes through one chokepoint, <c>create_artifact_weapon_effect</c>, and that hardcodes
/// <c>visuals = sword</c>, <c>visuals = axe</c> and so on. Inspirations, tournament prizes,
/// adventurer finds and court events all funnel through it, so without this every player-earned
/// weapon would be vanilla art sitting beside forged art in the same inventory.
///
/// **A vanilla "visual" is a trigger-gated list** of <c>icon</c> and <c>asset</c> entries, normally
/// gated on the owner's culture. Redeclaring the key replaces vanilla's list with ours, so the same
/// gating machinery picks a forged look instead.
///
/// **The visual is resolved once, inside <c>create_artifact</c> itself.** This is the fact the whole
/// design turns on, and it was established from the game's own error log rather than inferred:
///
/// <code>
/// Failed to fetch variable for 'gen_forge_index' due to not being set
///   common/artifacts/visuals/zz_gen_weapon_visual_overrides.txt line 714 (mace:trigger)
///     common/scripted_effects/00_ep1_artifact_creation_effects.txt line 9814
///       (create_artifact_weapon_effect)
/// </code>
///
/// Every such failure came from inside creation and none from later display. An earlier design rolled
/// a random index onto the artifact from <c>on_artifact_changed_owner</c> and gated on that; it could
/// never work, because the artifact does not exist yet when its look is chosen. Anything this file
/// tests must therefore already be true at the instant of creation.
///
/// **So it gates on culture and rarity**, both of which are. Culture is what vanilla itself uses, and
/// rarity is free variety on top — a famed weapon differing from a common one is a feature rather
/// than a compromise.
/// </summary>
public static class ForgedVisualOverrides
{
    /// <summary>
    /// Which vanilla visual each forged pool dresses.
    ///
    /// Several vanilla keys share one pool, deliberately:
    /// <list type="bullet">
    /// <item><c>hammer</c> takes the mace pool — hammers are cut into the mace library as families
    /// (<c>ep1_western_hammer_01_a</c>, <c>ep1_mena_hammer_01_a</c>) rather than given one of their
    /// own, so a mace look *is* the hammer look. The artifact's <c>type</c> stays hammer, so it keeps
    /// the hammer pose.</item>
    /// <item><c>longsword</c> and <c>dagger_kris</c> are separate vanilla visuals with no pool of
    /// their own; pointing them at the nearest pool stops two weapon classes falling back to vanilla
    /// art for nothing.</item>
    /// </list>
    ///
    /// <c>spear</c> is listed but has no library yet, so it skips itself until one exists — adding
    /// <c>spear_parts.mesh</c> is the only step needed to bring it in.
    /// </summary>
    private static readonly (string VanillaVisual, string Kind)[] Dressed =
    [
        ("sword", "sword"),
        ("longsword", "sword"),
        ("dagger", "dagger"),
        ("dagger_kris", "dagger"),
        ("axe", "axe"),
        ("mace", "mace"),
        ("hammer", "mace"),
        ("spear", "spear"),
    ];

    /// <summary>
    /// Rarity multiplies whatever the culture axis gives, and is worth having because the culture
    /// axis alone can be very thin: cultures are generated, and one measured seed held seven, six of
    /// which shared a single <c>unit_gfx</c>. Rarity is the axis that guarantees a player sees more
    /// than one look for their own people.
    ///
    /// The bands are <see cref="ArtifactRarity"/> itself rather than a list of their own. They used
    /// to be a private array of strings, which was harmless while every forged look was
    /// interchangeable — the rarity axis then only had to *differ*, not to mean anything. It stopped
    /// being harmless when the pool gained bands of its own: a second, independent notion of rarity
    /// here would hand a tournament-prize common sword the look forged for the world's legendaries.
    /// </summary>
    private static string Band(ArtifactRarity tier) => tier.ToString().ToLowerInvariant();

    /// <summary>
    /// Emits the override. Does nothing when no weapons were forged, so a run with no parts
    /// libraries leaves vanilla's own visuals entirely alone rather than replacing them with an
    /// empty list.
    /// </summary>
    public static void Write(
        string modDir, IReadOnlyList<WeaponAsset> forged, IReadOnlyList<string> cultureKeys)
    {
        var byKind = forged
            .GroupBy(w => w.Kind)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<WeaponAsset>)[.. g], StringComparer.Ordinal);

        var dressed = Dressed.Where(d => byKind.ContainsKey(d.Kind)).ToList();
        if (dressed.Count == 0) return;

        string dir = Path.Combine(modDir, "common", "artifacts", "visuals");
        Directory.CreateDirectory(dir);

        var b = new JominiBuilder();
        b.Comment("Redeclares vanilla's weapon visuals so weapons the GAME creates - inspirations,\n"
            + "tournament prizes, adventurer finds - are dressed from the forge rather than from the\n"
            + "vanilla catalogue. Our own artifacts do not pass through here; they name a forged look\n"
            + "directly, and already vary.\n\n"
            + "Gated on culture and rarity because those are the only useful things that are already\n"
            + "true when the look is chosen. The game resolves a visual INSIDE create_artifact, so\n"
            + "anything written onto the artifact afterwards is too late to matter.\n\n"
            + "This is a deliberate database conflict on vanilla's keys. If weapons come out looking\n"
            + "vanilla anyway, the override lost it - database_conflicts.log is the oracle.");

        foreach (var (visual, kind) in dressed)
        {
            var pool = byKind[kind];

            b.Blank();
            b.Comment($"{visual} <- {kind} pool: {cultureKeys.Count} culture(s) x "
                + $"{WeaponAssets.BandCount} rarities over {pool.Count} look(s)");

            using (b.Block(visual))
            {
                Entries(b, "icon", pool, cultureKeys, icon: true);
                Entries(b, "asset", pool, cultureKeys, icon: false);
            }
        }

        ParadoxText.WriteBom(
            Path.Combine(dir, "zz_gen_weapon_visual_overrides.txt"), b.ToString());
    }

    /// <summary>
    /// How far apart consecutive cultures' starting looks sit.
    ///
    /// Must be coprime to <paramref name="poolCount"/>, or the starts fall into a short repeating
    /// cycle and cultures begin sharing look sets. Starts just above the rarity count, so a culture's
    /// own four rarities do not run into the next culture's block, and walks up until it finds one
    /// that is coprime. Falls back to 1 for a pool too small to stride at all, which reduces to
    /// stepping one look per culture — the best available when there is barely a pool.
    ///
    /// It is now handed a **band** rather than the whole pool, and most bands are small enough to
    /// take the fallback. That is correct rather than a loss: the stride existed to stop the four
    /// rarities of one culture colliding with the next culture's, and bands that no longer share a
    /// number line cannot collide in the first place.
    /// </summary>
    private static int Stride(int poolCount)
    {
        for (int k = WeaponAssets.BandCount + 1; k < poolCount; k++)
        {
            if (Gcd(k, poolCount) == 1) return k;
        }

        return 1;
    }

    private static int Gcd(int a, int b) => b == 0 ? a : Gcd(b, a % b);

    /// <summary>
    /// How many looks one culture/rarity cell offers the engine to choose between.
    ///
    /// **The icon list deliberately does not get this.** <c>icon</c> and <c>asset</c> are two
    /// independent lists and the engine rolls each separately, so offering several of both would
    /// decorrelate them and hand a weapon one blade's thumbnail over another blade's model. One icon
    /// per cell keeps the pair honest. That costs nothing today because composed looks all share
    /// their kind's stock icon, and it is the reason to gate a future rendered icon on the lead
    /// rather than on the pairing.
    /// </summary>
    private const int LooksPerCell = 16;

    private static void Entries(
        JominiBuilder b, string field, IReadOnlyList<WeaponAsset> pool,
        IReadOnlyList<string> cultureKeys, bool icon)
    {
        for (int c = 0; c < cultureKeys.Count; c++)
        {
            for (int r = 0; r < WeaponAssets.BandCount; r++)
            {
                // Rarity picks the band, culture picks a look within it.
                //
                // Rarity used to be a step *along* the flat pool, which was reasonable while the
                // looks were interchangeable and is wrong now that they are not: it would dress a
                // common tournament prize in whatever look happened to sit at that index, including
                // one forged for the illustrious band. Asking the pool for the band instead makes
                // this agree with the artifact map, which selects the same way.
                //
                // Within a band the stride still earns its keep, for the reason it always did: a
                // formula like c % count marches cultures through the band one at a time, while a
                // stride coprime to its size spreads the starts across the whole of it. The wrap
                // matters because a band can be a single look — deduplication caps the pool at the
                // number of distinct part combinations, and a library with one family yields one.
                var band = WeaponAssets.AtTier(pool, (ArtifactRarity)r);
                int start = (c * Stride(band.Count)) % band.Count;

                // How many looks this cell offers. CK3 "picks a random valid one" among the entries
                // whose triggers pass (common/artifacts/visuals/_visuals.info), which vanilla itself
                // relies on -- the chest visual gives its _a and _b variants identical triggers and
                // lets the engine choose. One entry per cell therefore meant a character saw exactly
                // one look per band for the whole game, which is what made the debug forge repeat
                // itself however many times it was taken.
                //
                // Capped rather than handed the whole band. A band can hold hundreds of pairings now
                // that a pairing costs only text, and emitting every one into every culture's cell
                // would multiply this file by the pool size for variety no player can perceive past
                // the first dozen.
                int offered = icon ? 1 : Math.Min(LooksPerCell, band.Count);

                for (int n = 0; n < offered; n++)
                {
                    var pick = band[(start + n) % band.Count];

                    using (b.Block(field))
                    {
                        using (b.Block("trigger"))
                        {
                            // Root is the owner, and at creation that is the character the weapon is
                            // being made for. Vanilla prefers the creator's culture and falls back to
                            // the owner's; here the two are the same at the only moment this is read,
                            // so the simpler test is also the accurate one.
                            b.Field("culture", $"culture:{cultureKeys[c]}");

                            using (b.Block("scope:artifact"))
                                b.Field("rarity", Band((ArtifactRarity)r));
                        }

                        b.Field("reference", icon ? pick.Icon : pick.Entity);
                    }
                }
            }
        }

        // The untriggered default, which the format REQUIRES: each list needs one icon and one asset
        // with no trigger, and a list where every entry is conditional is rejected outright. It also
        // catches a weapon whose owner is of no generated culture.
        using (b.Block(field))
        {
            b.Field("reference", icon ? pool[0].Icon : pool[0].Entity);
        }
    }
}
