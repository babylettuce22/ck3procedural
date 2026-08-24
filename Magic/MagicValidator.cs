namespace Ck3MapGen.Magic;

public enum FindingSeverity
{
    /// <summary>A property of the world worth stating, not a defect. Hereditary worlds are closed
    /// to outsiders; that is the design, and the report should say so rather than hide it.</summary>
    Note,

    /// <summary>Probably playable, probably not what was intended.</summary>
    Warning,

    /// <summary>Would ship a broken or unwinnable world. Nothing should emit past one of these.</summary>
    Error,
}

public sealed record MagicFinding(FindingSeverity Severity, string Rule, string Detail);

/// <summary>
/// The invariants, checked before anything is emitted.
///
/// Two categories, and the split matters. Some of these are backstops for the coherence pass — if
/// they ever fire, a rule up there is wrong, and the finding is a bug report against the sampler.
/// The rest cannot be expressed as coherence rules at all, because they are properties of the
/// *assembled* system rather than of the axes: whether the spells that came out are affordable,
/// whether every rung of the ladder has something on it, whether the AI can use any of it. Those
/// only exist once the grammar has run, which is why validation is a separate pass and not a
/// constraint on the roll.
/// </summary>
public static class MagicValidator
{
    public static IReadOnlyList<MagicFinding> Check(MagicSystem system, MagicOptions options)
    {
        var findings = new List<MagicFinding>();
        var myth = system.Myth;

        if (system.IsMundane)
        {
            findings.Add(new(FindingSeverity.Note, "mundane world",
                "No practice at all. Prophecy without fulfilment and superstition that never "
                + "resolves — a legitimate roll, and the thing that makes magical worlds feel rare."));
            return findings;
        }

        // ---------------------------------------------------------------- backstops
        if (myth.Counterplay.Count == 0)
            findings.Add(new(FindingSeverity.Error, "counterplay",
                "Nothing checks the practice. The player becomes unassailable and the campaign ends."));

        if (myth.Ceiling == MagicCeiling.World
            && myth.Price is not (MagicPrice.Instability or MagicPrice.Depletion)
            && myth.MinorPrice is not MagicPrice.Instability)
            findings.Add(new(FindingSeverity.Error, "ceiling/price coupling",
                "World-scale reach priced at personal scale. Coherence should have caught this."));

        if (myth.Domains.Allowed.Count() < 2)
            findings.Add(new(FindingSeverity.Error, "domains",
                "Fewer than two domains survive. Coherence should have widened it."));

        if (myth.Fuel == MagicFuel.Debt && !system.Ledger.Enabled)
            findings.Add(new(FindingSeverity.Error, "debt without a collector",
                "Casting is free at the point of use and nothing accumulates. This world has no "
                + "price at all."));

        // ---------------------------------------------------------------- assembled system
        if (system.Spells.Count == 0)
        {
            findings.Add(new(FindingSeverity.Error, "empty grammar",
                "The world allows a practice but no spell could be assembled for it."));
            return findings;
        }

        if (system.Spells.Count < options.SpellBudget * 0.6)
            findings.Add(new(FindingSeverity.Warning, "thin grammar",
                $"{system.Spells.Count} spells against a budget of {options.SpellBudget}. The "
                + "palette left after the prohibitions is probably too narrow for the ceiling."));

        foreach (var rank in system.Ladder)
            if (system.Spells.All(s => s.Rank != rank.Index))
                findings.Add(new(FindingSeverity.Warning, "dead rung",
                    $"Rank {rank.Index} ({rank.Title}) has nothing on it — climbing to it gains "
                    + "the player nothing."));

        // Affordability, which is the check that actually catches free power. A price column can
        // balance perfectly and still be meaningless if the number in it is one a character earns
        // back in a month.
        double topCeiling = system.Ladder.Count > 0 ? system.Ladder[^1].PowerCeiling : 1;
        foreach (var spell in system.Spells)
        {
            double annual = SpellGrammar.AnnualBudget(spell.Cost.Fuel);
            double share = annual > 0 ? spell.Cost.Amount / annual : 1;

            if (share < 0.15 && spell.Power > topCeiling * 0.5 && spell.Cost.Fuel != MagicFuel.Debt)
                findings.Add(new(FindingSeverity.Warning, "free power",
                    $"\"{spell.Name}\" reaches {spell.Power:0.#} power for {spell.Cost}, which a "
                    + $"middling ruler can pay {1 / Math.Max(share, 0.001):0} times a year."));

            if (spell.Lead.Polarity == EffectPolarity.Harm
                && spell.Lead.Scope >= EffectScope.Province
                && spell.Exposure.Visibility < 0.25)
                findings.Add(new(FindingSeverity.Warning, "undetectable at scale",
                    $"\"{spell.Name}\" harms at {spell.Lead.Scope} scale and is effectively "
                    + "invisible. Nobody can retaliate against what nobody can attribute."));

            double miss = spell.Price > 0
                ? Math.Abs(spell.Power - options.Exchange * spell.Price) / (options.Exchange * spell.Price)
                : 1;

            if (miss > options.DegeneracyTolerance - 1)
                findings.Add(new(FindingSeverity.Warning, "off budget",
                    $"\"{spell.Name}\" misses its price by {miss:P0}; the solver could not fit it."));
        }

        // Long life without a counter-pressure stops the dynasty simulation, which is the actual
        // game the magic was added to.
        if (system.Spells.Any(s => s.Effects.Any(e => e.Atom.Key == "death_deathless"))
            && myth.Price is not (MagicPrice.Corruption or MagicPrice.Taint or MagicPrice.Instability))
            findings.Add(new(FindingSeverity.Warning, "succession pressure",
                "The world grants deathlessness without a price that reaches the line. Successions "
                + "stop happening and CK3 stops being played."));

        // ---------------------------------------------------------------- properties worth stating
        if (!system.Access.OpenToOutsiders)
            findings.Add(new(FindingSeverity.Note, "closed world",
                "No route in for a character not already carrying it. Legitimate — hereditary "
                + "worlds are like this — but the player either starts inside the system or never "
                + "touches it."));

        if (myth.Prevalence == MagicPrevalence.Hidden && !system.Access.OpenToOutsiders)
            findings.Add(new(FindingSeverity.Warning, "practically absent",
                "Closed to outsiders and under a percent of characters carry it. Most campaigns "
                + "will finish without meeting the system at all."));

        // Delivery concentration, not "the AI cannot use schemes" — which was the first version of
        // this rule and was not defensible: CK3's AI schemes, hosts activities and takes decisions
        // perfectly happily. Whether the AI participates is decided by the ai_will_do weights at
        // emit time, not by the shape. What *is* a property of the generated system is monotony: a
        // world where nearly every spell arrives the same way has thrown away the structural
        // variance that made delivery worth rolling.
        var dominant = system.Spells.GroupBy(s => s.Delivery)
                                    .OrderByDescending(g => g.Count())
                                    .First();

        double dominantShare = dominant.Count() / (double)system.Spells.Count;

        if (dominantShare > 0.65)
            findings.Add(new(FindingSeverity.Warning, "delivery monotony",
                $"{dominantShare:P0} of spells arrive as {dominant.Key}. The world is not using the "
                + "variance that delivery exists to provide."));

        // What the AI genuinely cannot do is act on something with no self-directed entry point.
        if (!system.Spells.Any(s => s.Delivery is Delivery.Decision or Delivery.Passive))
            findings.Add(new(FindingSeverity.Warning, "no self-directed entry",
                "Nothing here is a decision or a passive. Every spell needs a target or an "
                + "occasion, so a practitioner with no immediate quarrel has nothing to do."));

        if (system.Prophecies.Count > 0)
        {
            int trueCount = system.Prophecies.Count(p => p.CanEverFire);
            if (trueCount == system.Prophecies.Count)
                findings.Add(new(FindingSeverity.Note, "prophecy",
                    "Every prophecy in this world is satisfiable, so prophetic text is a reliable "
                    + "instruction here."));
        }

        return findings;
    }

    public static FindingSeverity Worst(IReadOnlyList<MagicFinding> findings) =>
        findings.Count == 0 ? FindingSeverity.Note : findings.Max(f => f.Severity);
}
