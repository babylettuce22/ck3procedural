namespace Ck3MapGen.Emit;

/// <summary>
/// TEMPORARY build switches for the artifact forge. Delete this file — and the two guards that
/// read it — once the question each flag stands in for has been answered.
/// </summary>
/// <remarks>
/// These are source constants rather than <see cref="Config.MapConfig"/> fields on purpose: they
/// are development scaffolding, not a choice a map author should be offered. A flag that reaches
/// the config file is a flag that has to be supported.
///
/// <c>static readonly</c> and not <c>const</c>, deliberately. A <c>const</c> lets the compiler fold
/// the guard away and warn CS0162 on everything past it — in whichever direction the flag currently
/// points — so the file that turns a feature off would also fill the build log with noise about it.
/// </remarks>
public static class ArtifactForgeFlags
{
    /// <summary>
    /// Whether generated armour reaches the portrait at all.
    ///
    /// Set <c>false</c> to reserve portrait art for forged weapons: <see cref="ArmorForgeStep"/>
    /// and <see cref="CustomArmorStep"/> both emit nothing, so no accessory, entity, palette,
    /// gene-template splice or portrait modifier for armour is written.
    ///
    /// **Armour artifacts themselves are unaffected.** Neither step writes
    /// <c>common/artifacts/visuals</c> — the six <c>armor_*</c> types keep generating as ordinary
    /// inventory items and simply fall back to vanilla's artifact art, which is what they did
    /// before any of this existed. The only loss is the worn look.
    ///
    /// Both steps are gated by the one flag because they share the clothes gene: the custom step
    /// splices into the same gene the forge does, and half-emitting that is worse than emitting
    /// neither.
    /// </summary>
    public static readonly bool ArmorOnPortrait = false;
}
