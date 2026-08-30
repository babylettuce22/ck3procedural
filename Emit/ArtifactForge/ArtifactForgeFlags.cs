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
    public static readonly bool ArmorOnPortrait = true;

    /// <summary>
    /// Whether armour is painted against a mask we generate rather than vanilla's own.
    ///
    /// Set <c>false</c> to go back to vanilla's mask exactly: <see cref="ArmorMask"/> writes nothing,
    /// entities name the mask they always did, and the coverage, density and material-aiming passes
    /// all read it — so the whole feature reverses at this one line with no other edit.
    ///
    /// **What it buys.** Vanilla's mask marks a war garment's CLOTH and not its plates: every
    /// visible channel measures metalness ~0.00, while 94-98% of each garment's genuinely metal
    /// texels lie outside the mask. So with it off, a plate artifact necessarily paints its metal
    /// onto a surcoat. With it on, the metal region is lifted out of the garment's own properties
    /// map and given the last mask channel, and metal substances land on metal.
    ///
    /// **What it costs.** One 512x512 DXT5 file per distinct garment, ~260 KB each, and only for
    /// garments that are at least 2% metal — the rest keep vanilla's mask either way.
    ///
    /// **OFF, on a judgement call, after seeing both in game (2026-08-28.)** It works exactly as
    /// designed and it looks worse. The reason is in the numbers that justified it: a garment's
    /// genuinely metal area is only **5-7%** of it (28% on the byzantine one), while vanilla's mask
    /// covers **55-76%**. So accurate placement turns a plate artifact into a cloth robe carrying a
    /// small correct steel cuirass, where the inaccurate version made the whole garment read as the
    /// artifact's material. For a legendary object, dramatic beats accurate — the piece is supposed
    /// to announce itself.
    ///
    /// Kept rather than deleted because the measurement behind it stands, and it is the right
    /// mechanism for anything that wants metal specifically — a trim, a gilded edge, a second
    /// material layered on the plates rather than replacing the garment.
    /// </summary>
    public static readonly bool GeneratedArmorMasks = false;

    /// <summary>
    /// Whether rigid pieces are hung off portrait bones — see <see cref="BonePieceStep"/>.
    ///
    /// Pauldrons are the first use; the step is written around SLOTS, so a helm crest or a back
    /// piece is a table row and a mesh rather than new code. Off means the meshes in
    /// <c>assets/attachments</c> are simply not baked or emitted, and nothing else changes.
    ///
    /// This is the only route by which an artifact can add METAL to a cloth war garment, since a
    /// garment's mask marks its cloth and not its plates — see
    /// <see cref="ArtifactForgeFlags.GeneratedArmorMasks"/> for why painting them instead was tried
    /// and abandoned.
    /// </summary>
    public static readonly bool BonePieces = true;
}
