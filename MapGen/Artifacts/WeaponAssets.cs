namespace Ck3MapGen.MapGen;

/// <summary>
/// One concrete look a generated weapon artifact can wear: an inventory icon and a 3D entity.
///
/// This is the seam for custom art. A generated weapon's <c>visuals</c> field names an entry
/// here, and <see cref="Emit.ArtifactWriter.WriteVisuals"/> emits one artifact-visual per entry
/// into <c>common/artifacts/visuals/</c>, in exactly the form AGOT uses for Valyrian steel:
///
/// <code>
/// vs_blackfyre_visuals = {
///     icon  = "vs_blackfyre.dds"
///     asset = blackfyre_sword_entity
/// }
/// </code>
///
/// The entity is what the portrait actually draws. Vanilla's equipped-weapon accessory declares
/// <c>game_entity_override = weapon</c>, so once a character equips the artifact the engine
/// substitutes this entity into their hand — no portrait modifier or accessory is needed per
/// weapon. (That override exists only for the <c>weapon</c> slot; crowns and armour are a
/// different, hand-authored mechanism.)
///
/// To test a custom model: export it with io_pdx_mesh, declare a <c>pdxmesh</c> + <c>entity</c>
/// in an <c>.asset</c> file under the mod's <c>gfx/models/</c>, drop the icon into
/// <c>gfx/interface/icons/artifact/</c>, then add a row below naming that entity. Nothing else
/// in the pipeline needs to change.
/// </summary>
/// <param name="VisualKey">
/// Key written into <c>common/artifacts/visuals/</c> and referenced by <c>create_artifact</c>.
/// Must not collide with a vanilla visual name, hence the <c>gen_</c> prefix.
/// </param>
/// <param name="Kind">
/// Artifact type this look belongs to — <c>sword</c>, <c>axe</c>, <c>mace</c>, <c>spear</c> or
/// <c>dagger</c>. The type, not the visual, decides the inventory slot and which idle animation
/// item fires, so a look must be filed under the kind whose pose suits it.
/// </param>
/// <param name="Entity">Entity name as declared in an <c>.asset</c> file.</param>
/// <param name="Icon">Icon filename, resolved under <c>gfx/interface/icons/artifact/</c>.</param>
/// <param name="Tier">
/// The rarity band this look is reserved for, or null for a look that fits any band.
///
/// Forged looks carry a tier so an illustrious sword can be given treatment a common one is not —
/// see <see cref="Emit.WeaponForgeStep"/>, which decides the split. The vanilla rows below carry
/// none, deliberately: they are the fallback for a checkout with no parts library, and grading art
/// that already exists would only shrink the choice without making any band look better.
/// </param>
public readonly record struct WeaponAsset(
    string VisualKey, string Kind, string Entity, string Icon, ArtifactRarity? Tier = null);

/// <summary>
/// The catalogue of weapon looks the generator can hand out.
///
/// Every entry below points at a vanilla entity and a vanilla icon, so the pipeline works on a
/// clean install with no custom art at all. They are deliberately the *portrait* entities: the
/// non-portrait variants exist for the court-scene table and are the wrong mesh for a hand.
/// </summary>
public static class WeaponAssets
{
    public static readonly IReadOnlyList<WeaponAsset> All =
    [
        // ---- swords ----------------------------------------------------------------------
        new("gen_sword_western",      "sword",  "ep1_western_sword_01_a_portrait_entity",     "artifact_sword.dds"),
        new("gen_sword_western_long", "sword",  "ep1_western_sword_02_a_portrait_entity",     "artifact_longsword.dds"),
        new("gen_sword_northern",     "sword",  "ep1_northern_sword_01_a_portrait_entity",    "artifact_northern_sword.dds"),
        new("gen_sword_byzantine",    "sword",  "ep1_byzantine_sword_01_a_portrait_entity",   "artifact_sword.dds"),
        new("gen_sword_mena",         "sword",  "ep1_mena_sword_01_a_portrait_entity",        "artifact_sassanian_sword.dds"),
        new("gen_sword_indian",       "sword",  "ep1_indian_sword_01_a_portrait_entity",      "artifact_sword.dds"),
        new("gen_sword_steppe",       "sword",  "ep1_steppe_sword_01_a_portrait_entity",      "artifact_sword.dds"),
        new("gen_sword_african",      "sword",  "ep1_african_sword_01_a_portrait_entity",     "artifact_african_sword.dds"),

        // ---- axes ------------------------------------------------------------------------
        new("gen_axe_western",        "axe",    "ep1_western_axe_01_a_portrait_entity",       "artifact_axe.dds"),
        new("gen_axe_northern",       "axe",    "ep1_northern_axe_01_a_portrait_entity",      "artifact_axe.dds"),
        new("gen_axe_steppe",         "axe",    "ep1_steppe_axe_01_a_portrait_entity",        "artifact_steppe_axe.dds"),
        new("gen_axe_african",        "axe",    "ep1_african_axe_01_a_portrait_entity",       "artifact_african_axe.dds"),
        new("gen_axe_indian",         "axe",    "ep1_indian_axe_01_a_portrait_entity",        "artifact_axe.dds"),
        new("gen_axe_mena",           "axe",    "ep1_mena_axe_01_a_portrait_entity",          "artifact_axe.dds"),

        // ---- maces -----------------------------------------------------------------------
        new("gen_mace_western",       "mace",   "ep1_western_mace_01_a_portrait_entity",      "artifact_mace.dds"),
        new("gen_mace_byzantine",     "mace",   "ep1_byzantine_mace_01_a_portrait_entity",    "artifact_byzantine_mace.dds"),
        new("gen_mace_steppe",        "mace",   "ep1_steppe_mace_01_a_portrait_entity",       "artifact_steppe_mace.dds"),
        new("gen_mace_indian",        "mace",   "ep1_indian_mace_01_a_portrait_entity",       "artifact_mace.dds"),
        new("gen_mace_african",       "mace",   "ep1_african_mace_01_a_portrait_entity",      "artifact_mace.dds"),

        // ---- spears ----------------------------------------------------------------------
        new("gen_spear_western",      "spear",  "ep1_western_spear_01_a_portrait_entity",     "artifact_spear.dds"),
        new("gen_spear_indian",       "spear",  "ep1_indian_spear_01_a_portrait_entity",      "artifact_spear.dds"),

        // ---- daggers ---------------------------------------------------------------------
        new("gen_dagger_mena",        "dagger", "ep1_mena_dagger_01_a_portrait_entity",       "artifact_dagger.dds"),
        new("gen_dagger_indian",      "dagger", "ep1_indian_dagger_01_a_portrait_entity",     "artifact_dagger.dds"),
    ];

    /// <summary>Every look filed under one weapon kind, in catalogue order.</summary>
    public static IReadOnlyList<WeaponAsset> ForKind(string kind) =>
        _byKind.TryGetValue(kind, out var list) ? list : [];

    /// <summary>
    /// The looks a weapon of this rarity may wear, out of a pool for one kind.
    ///
    /// An untiered pool — the vanilla catalogue — is returned whole, so a checkout with no parts
    /// library behaves exactly as it did before tiers existed.
    ///
    /// A tiered pool cannot be assumed to hold every band. <c>WeaponForgeStep.TierPlan</c> hands
    /// out at most one band per look, so a pool of two covers two of the four, and a library too
    /// small or too self-restricted to fill its pool covers fewer still. The search therefore walks
    /// outward from the wanted band, **downward first**: a famed sword with no famed look should
    /// borrow from the masterworks rather than put on the world's legendary blade. Returning the
    /// whole pool on a miss would do the opposite by including the top band, which is the one case
    /// worth protecting.
    /// </summary>
    public static IReadOnlyList<WeaponAsset> AtTier(IReadOnlyList<WeaponAsset> looks, ArtifactRarity tier)
    {
        if (!looks.Any(l => l.Tier is not null)) return looks;

        for (int distance = 0; distance <= BandCount; distance++)
        {
            var down = looks.Where(l => l.Tier == (ArtifactRarity)((int)tier - distance)).ToList();
            if (down.Count > 0) return down;

            if (distance == 0) continue;

            var up = looks.Where(l => l.Tier == (ArtifactRarity)((int)tier + distance)).ToList();
            if (up.Count > 0) return up;
        }

        return looks;
    }

    /// <summary>How many rarity bands a pool can be split across.</summary>
    public static int BandCount { get; } = Enum.GetValues<ArtifactRarity>().Length;

    /// <summary>The weapon kinds the catalogue can actually dress, in catalogue order.</summary>
    public static IReadOnlyList<string> Kinds { get; } =
        All.Select(a => a.Kind).Distinct().ToList();

    private static readonly Dictionary<string, IReadOnlyList<WeaponAsset>> _byKind =
        All.GroupBy(a => a.Kind)
           .ToDictionary(g => g.Key, g => (IReadOnlyList<WeaponAsset>)g.ToList());
}
