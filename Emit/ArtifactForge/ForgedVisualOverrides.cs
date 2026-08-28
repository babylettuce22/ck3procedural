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
    /// Rarity, low to high. Four buckets that multiply whatever the culture axis gives.
    ///
    /// Worth having because the culture axis alone can be very thin. Cultures are generated, and a
    /// world may hold only a handful — one measured seed had seven, six of which shared a single
    /// <c>unit_gfx</c>. Rarity is the axis that guarantees a player sees more than one look for their
    /// own people.
    /// </summary>
    private static readonly string[] Rarities = ["common", "masterwork", "famed", "illustrious"];

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
            b.Comment($"{visual} <- {kind} pool: {cultureKeys.Count} culture(s) x {Rarities.Length} "
                + $"rarities over {pool.Count} look(s)");

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
    /// </summary>
    private static int Stride(int poolCount)
    {
        for (int k = Rarities.Length + 1; k < poolCount; k++)
        {
            if (Gcd(k, poolCount) == 1) return k;
        }

        return 1;
    }

    private static int Gcd(int a, int b) => b == 0 ? a : Gcd(b, a % b);

    private static void Entries(
        JominiBuilder b, string field, IReadOnlyList<WeaponAsset> pool,
        IReadOnlyList<string> cultureKeys, bool icon)
    {
        for (int c = 0; c < cultureKeys.Count; c++)
        {
            for (int r = 0; r < Rarities.Length; r++)
            {
                // Culture picks a starting look, rarity steps along from there.
                //
                // The stride is what makes a big pool worth paying for. Two obvious formulas both
                // waste it:
                //
                //   c * 4 + r   every culture's block starts at a multiple of four, so with eight
                //               looks cultures 0, 2 and 4 come out identical.
                //   c + r       starts only ever span cultures + rarities - 1, so a pool of
                //               sixteen against seven cultures reaches ten looks and no more.
                //
                // A stride coprime to the pool size spreads the starts across all of it, and the
                // four rarities then run consecutively from each start, so they stay distinct.
                //
                // The wrap still matters: a pool can be shorter than the combination count, because
                // deduplication caps it at the number of distinct part combinations and a library
                // with one family yields one look.
                var pick = pool[(c * Stride(pool.Count) + r) % pool.Count];

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
                            b.Field("rarity", Rarities[r]);
                    }

                    b.Field("reference", icon ? pick.Icon : pick.Entity);
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
