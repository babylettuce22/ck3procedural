using System.ComponentModel;

namespace Ck3MapGen.Magic;

/// <summary>
/// The knobs a user turns. Deliberately small: nine settings, none of which is a spell.
///
/// Everything that could be a setting is not one. The axes in <see cref="Cosmology"/> are rolled
/// rather than chosen because the point of the feature is that the world surprises you; what
/// belongs here is the envelope — how present magic is, how harshly it is priced, how much of it
/// there is to read — plus the overrides needed to reproduce a world someone liked.
///
/// Attributed for the PropertyGrid the same way <see cref="Config.MapConfig"/> is, so this can be
/// folded into the settings tree unchanged when the system is wired up.
/// </summary>
public sealed class MagicOptions
{
    [Category("01 Presence")]
    [DisplayName("Presence")]
    [Description("How much of the population practises. Leave unset to roll it. Absent is a real "
                 + "outcome and worth allowing: a world of false prophecy and superstition makes "
                 + "the magical worlds feel rarer.")]
    public MagicPrevalence? Presence { get; set; }

    [Category("01 Presence")]
    [DisplayName("Allow mundane worlds")]
    [Description("Whether a rolled presence may come out Absent. Turning this off guarantees "
                 + "magic exists without forcing how much of it there is.")]
    public bool AllowMundane { get; set; } = true;

    [Category("01 Presence")]
    [DisplayName("Ceiling cap")]
    [Description("Clamps the largest thing magic may touch. Set it to Court to keep a generated "
                 + "world's magic out of wars and provinces entirely.")]
    public MagicCeiling? CeilingCap { get; set; }

    [Category("02 Balance")]
    [DisplayName("Exchange rate")]
    [Description("How much power a unit of price buys. Below 1 makes magic expensive, safe and "
                 + "rare-feeling; above 1 makes it cheap, nasty and ubiquitous. One number that "
                 + "retunes every generated spell in the world.")]
    public double Exchange { get; set; } = 1.0;

    [Category("02 Balance")]
    [DisplayName("Degeneracy tolerance")]
    [Description("How far a single spell may exceed the world's median power-per-price before the "
                 + "validator rejects it. Raise it to allow deliberate outliers.")]
    public double DegeneracyTolerance { get; set; } = 1.5;

    [Category("03 Volume")]
    [DisplayName("Spell budget")]
    [Description("How many spells the world gets in total, across all ranks. This is a legibility "
                 + "budget rather than a content budget — past roughly twenty, a player stops "
                 + "reading them and starts using the same three.")]
    public int SpellBudget { get; set; } = 12;

    [Category("03 Volume")]
    [DisplayName("Ranks")]
    [Description("How many steps the progression ladder has. Zero rolls it (three or four).")]
    public int RankCount { get; set; }

    [Category("03 Volume")]
    [DisplayName("Prophecies")]
    [Description("How many prophecies to seed. Some of them are generated unsatisfiable on "
                 + "purpose — a world where every prophecy comes true is a quest log.")]
    public int ProphecyCount { get; set; } = 3;

    [Category("04 Structure")]
    [DisplayName("Traditions")]
    [Description("One magic system per world, or two incompatible ones. Two is a much richer "
                 + "world and roughly double the emitted content, plus the interaction surface "
                 + "between them.")]
    public int Traditions { get; set; } = 1;
}
