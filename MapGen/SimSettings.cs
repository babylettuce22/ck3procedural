namespace Ck3MapGen.MapGen;

/// <summary>
/// Everything about the History workspace's simulation a user can change while it runs: which
/// rules are in force, and four dials over how hard they bite.
///
/// The dials are multipliers on what the world was generated with, and every default is exactly
/// 1 — multiplying by 1.0 is exact in floating point, so default settings reproduce the history the
/// simulation ran before these existed, dice for dice. A record so two settings compare by value,
/// which is how a schedule of them knows whether anything changed.
/// </summary>
public sealed record SimSettings
{
    public static SimSettings Default { get; } = new();

    public RealmRules Rules { get; init; } = RealmRules.All;

    /// <summary>How readily realms attack and take homage, times the world's own.</summary>
    public double Aggression { get; init; } = 1.0;

    /// <summary>How readily overstretched and unstable realms come apart, times the world's own.</summary>
    public double Turbulence { get; init; } = 1.0;

    /// <summary>How often a partition finds a second and a third heir, times the usual 50% and 25%.</summary>
    public double Heirs { get; init; } = 1.0;

    /// <summary>How likely a succession in an unstable realm goes to a new house, times the usual.</summary>
    public double Crises { get; init; } = 1.0;
}
