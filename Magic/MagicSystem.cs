namespace Ck3MapGen.Magic;

/// <summary>The names a world uses for its practice. Kept as resolved strings rather than as a
/// live <see cref="Lexicon"/>, so the IR is inert data that can be printed, diffed and compared
/// without carrying a random number generator around inside it.</summary>
public sealed record MagicNaming(string Tradition, string Institution, PhonoStyle Style);

/// <summary>
/// One step of the progression ladder.
///
/// <see cref="PowerCeiling"/> is the budget the spell grammar spends at this rank, and the curve
/// across ranks is geometric, so the top of the ladder is two or three things rather than twenty.
/// A flat ladder would let the generator fill every rank with equally interesting options, which
/// sounds generous and reads as having no progression at all.
/// </summary>
public sealed record Rank(
    int Index,
    string Key,
    string Title,
    double PowerCeiling,
    string Gate);

/// <summary>
/// The deliberate cross-subsystem coupling.
///
/// Generated on purpose rather than hoped for. Emergence between two generated systems is what
/// players actually talk about afterwards, and waiting for it to fall out of independent
/// subsystems is how you get two systems that share a map and nothing else.
/// </summary>
public sealed record KeystoneLink(string Subsystem, string Description, string ScriptHint);

/// <summary>What stops a practitioner from simply winning, expressed as something emittable.</summary>
public sealed record CounterplayPlan(
    IReadOnlyList<MagicCounterplay> Kinds,
    string Description,
    string ScriptHint);

/// <summary>
/// The world meter: how casting aggregates into world change.
///
/// The single most important piece of the passive layer. Individual casts are small; the world
/// moves because everyone is casting, including — mostly — the AI. Without this, a generated magic
/// system is a toybox the player opens and nothing in the world ever answers.
/// </summary>
public sealed record LedgerRule(
    bool Enabled,
    double DecayPerYear,
    IReadOnlyList<(double Threshold, string Consequence)> Thresholds,
    string Note);

/// <summary>
/// One world's complete magic system, fully resolved.
///
/// Everything above this line is derivation and everything below it is emission. The split is the
/// same one the map pipeline already draws between <c>Generate</c> and <c>WriteMod</c>, and for the
/// same reason: the expensive, slow, side-effecting half should read a finished description rather
/// than make decisions of its own. It also means the GUI can inspect a world's magic without
/// writing a mod, and that <see cref="MagicReport"/> can judge a thousand seeds in a second.
///
/// Nothing in this type or anything it holds knows what a CK3 file is.
/// </summary>
public sealed record MagicSystem
{
    public required int Seed { get; init; }

    public required Cosmology Myth { get; init; }

    public required MagicNaming Naming { get; init; }

    public required AcquisitionGraph Access { get; init; }

    public required IReadOnlyList<Rank> Ladder { get; init; }

    public required IReadOnlyList<Spell> Spells { get; init; }

    /// <summary>Empty unless <see cref="MagicSource.Entities"/>.</summary>
    public required IReadOnlyList<Entity> Entities { get; init; }

    public required IReadOnlyList<Prophecy> Prophecies { get; init; }

    public required CounterplayPlan Counter { get; init; }

    public required KeystoneLink Keystone { get; init; }

    public required LedgerRule Ledger { get; init; }

    /// <summary>What the coherence pass had to repair, in order. Printed under the axes so that a
    /// world which reads strangely can be traced to a rule rather than blamed on the seed.</summary>
    public required IReadOnlyList<string> CoherenceTrace { get; init; }

    /// <summary>True when the world rolled no magic at all: prophecy without fulfilment, and
    /// superstition that never resolves.</summary>
    public bool IsMundane => Myth.Prevalence == MagicPrevalence.Absent;

    /// <summary>The median power-per-price across the world's spells; the baseline the degeneracy
    /// check measures outliers against.</summary>
    public double MedianRatio()
    {
        if (Spells.Count == 0) return 0;

        var ratios = Spells.Select(s => s.Price > 0 ? s.Power / s.Price : s.Power)
                           .OrderBy(r => r)
                           .ToList();

        return ratios[ratios.Count / 2];
    }
}
