using Ck3MapGen.Core;

namespace Ck3MapGen.Magic;

/// <summary>
/// How the player performs a spell — and the slot that does the most work in the whole grammar.
///
/// CK3 hands us six genuinely different interaction shapes, and they produce different games out
/// of identical effects. A world whose magic arrives as schemes is about concealment and timing; a
/// world whose magic arrives as activities is about gathering people and spending a season on it;
/// a world whose magic is passive is about what you are rather than what you do. Rolling delivery
/// per world is therefore the cheapest structural variance available anywhere in this design,
/// because the engine supplies all six and we only have to choose.
/// </summary>
public enum Delivery
{
    /// <summary>A decision in a generated group. Instant, self-directed, cooldown-gated.</summary>
    Decision,

    /// <summary>A targeted interaction. The target gets to refuse, which makes it diplomacy.</summary>
    Interaction,

    /// <summary>A scheme. Contested over time, with agents, secrecy and discovery.</summary>
    Scheme,

    /// <summary>An activity. A ritual with a place, guests, phases and options.</summary>
    Activity,

    /// <summary>A story cycle. Set going once, then it ticks for years on its own.</summary>
    Story,

    /// <summary>Not performed at all. Always on, and the price is paid continuously.</summary>
    Passive,
}

/// <summary>Who or what a spell is pointed at. Derived from the lead atom's scope.</summary>
public enum TargetKind
{
    Self,
    Courtier,
    Kin,
    Rival,
    Vassal,
    Court,
    Province,
    Title,
    Army,
    Realm,
    World,
}

/// <summary>What a cast costs, in the world's own fuel.</summary>
public sealed record CostVector(MagicFuel Fuel, double Amount, string Unit, string Note)
{
    public override string ToString() => $"{Amount:0.#} {Unit}";
}

/// <summary>What goes wrong, how often, and how badly.</summary>
public sealed record BacklashSpec(double Probability, double Severity, MagicPrice Kind, string Note);

/// <summary>
/// Who finds out, and what that costs.
///
/// Separate from backlash because they fail differently: backlash is the spell not working, and
/// exposure is the spell working and someone seeing. A world can price one heavily and the other
/// at nothing, and those are two different games.
/// </summary>
public sealed record Exposure(double Visibility, string Consequence);

/// <summary>Something that must be true before the spell is available.</summary>
public sealed record Precondition(string Kind, string Note);

/// <summary>
/// One generated spell.
///
/// <see cref="Power"/> and <see cref="Price"/> are in the same abstract units by construction: the
/// grammar prices a spell from its power rather than rolling both and hoping. What varies between
/// worlds is not whether they balance, but <em>which component carries the price</em> — a stigma
/// world pays mostly in exposure, a backlash world mostly in risk, a vital world mostly in fuel.
/// That is the part the player feels.
/// </summary>
public sealed record Spell(
    string Key,
    string Name,
    int Rank,
    Delivery Delivery,
    TargetKind Target,
    IReadOnlyList<Precondition> Requires,
    CostVector Cost,
    IReadOnlyList<(EffectAtom Atom, double Strength)> Effects,
    BacklashSpec Backlash,
    Exposure Exposure,
    double Power,
    double Price,
    double AiWeight)
{
    public EffectAtom Lead => Effects[0].Atom;

    public IEnumerable<string> Describe() => Effects.Select(e => e.Atom.Describe(e.Strength));
}

/// <summary>
/// Assembles spells from atoms, and prices them.
///
/// The grammar is the part most likely to produce garbage if left unsupervised, so it is built
/// around a solver rather than around a roll: pick a shape, price it, and check that the shape is
/// actually payable by a character who might want to cast it. The affordability model in
/// <see cref="AnnualBudget"/> is deliberately crude — it only has to catch the failure that
/// matters, which is a world-shaking effect that costs three piety.
/// </summary>
public static class SpellGrammar
{
    /// <summary>
    /// Roughly how much of each fuel a middling ruler can spend per year without changing how they
    /// play. Not a simulation — a smell test. Anything a character can pay seven times a year is
    /// free, whatever the price column says.
    /// </summary>
    public static double AnnualBudget(MagicFuel fuel) => fuel switch
    {
        MagicFuel.Ambient => 8,
        MagicFuel.Vital => 3,
        MagicFuel.Sacrificial => 2,
        MagicFuel.Devotional => 10,
        MagicFuel.Material => 12,
        MagicFuel.Temporal => 4,

        // Unbounded on purpose: the whole point of a debt world is that nothing stops you now.
        // The ledger is what makes this survivable, and the validator checks that one exists.
        MagicFuel.Debt => 999,
        _ => 6,
    };

    public static string FuelUnit(MagicFuel fuel) => fuel switch
    {
        MagicFuel.Ambient => "draw",
        MagicFuel.Vital => "years",
        MagicFuel.Sacrificial => "lives",
        MagicFuel.Devotional => "piety",
        MagicFuel.Material => "gold",
        MagicFuel.Temporal => "days of the window",
        MagicFuel.Debt => "owed",
        _ => "units",
    };

    public static IReadOnlyList<Spell> Build(
        Cosmology myth, IReadOnlyList<Rank> ladder, Lexicon lexicon, MagicOptions options, Rng rng)
    {
        var available = EffectAtoms.Available(myth);
        if (available.Count == 0 || myth.Prevalence == MagicPrevalence.Absent) return [];

        var spells = new List<Spell>();
        var used = new HashSet<string>();

        // Front-loaded: most of a world's spells sit at the bottom of the ladder, so the top is
        // two or three things worth climbing for rather than another full menu.
        var perRank = Distribute(options.SpellBudget, ladder.Count);

        for (int r = 0; r < ladder.Count; r++)
        {
            for (int i = 0; i < perRank[r]; i++)
            {
                var spell = Assemble(myth, ladder[r], available, used, lexicon, options, rng);
                if (spell is null) continue;

                spells.Add(spell);
                used.Add(spell.Lead.Key);
            }
        }

        return spells;
    }

    private static int[] Distribute(int budget, int ranks)
    {
        var counts = new int[ranks];
        // Weights fall off as 1, 0.7, 0.5, 0.35 ... which puts roughly half the world's spells at
        // rank one and leaves the top rank with a handful.
        double[] weights = Enumerable.Range(0, ranks).Select(r => Math.Pow(0.7, r)).ToArray();
        double total = weights.Sum();

        for (int r = 0; r < ranks; r++) counts[r] = Math.Max(1, (int)Math.Round(budget * weights[r] / total));
        return counts;
    }

    private static Spell? Assemble(
        Cosmology myth, Rank rank, IReadOnlyList<EffectAtom> available, HashSet<string> used,
        Lexicon lexicon, MagicOptions options, Rng rng)
    {
        // Two filters, and the second one is the one that matters.
        //
        // The first is arithmetic: an atom whose floor already exceeds the rank's ceiling cannot
        // appear here at any strength. The second is that power and *severity* are not the same
        // axis, and pricing on power alone gets this badly wrong — cutting a life short at a
        // distance scores about the same as a decent health buff once scope is accounted for,
        // because it targets one character and does it once. It is still an assassination, and an
        // assassination available to a first-rank initiate is a world where nothing else matters.
        //
        // So the atom's intrinsic weight — its severity before scope, the number that separates
        // "kill someone" from "heal someone" — is capped by rank as well. Irreversible things live
        // at the top of the ladder because that is what the ladder is for.
        double severityCap = 1.2 + 0.9 * rank.Index;

        var affordable = available
            .Where(a => a.PowerAt(0) <= rank.PowerCeiling && a.Weight <= severityCap)
            .ToList();

        // A world that forbade every gentle domain can leave a low rank with nothing it is allowed
        // to do. Rather than a dead rung, hand it the mildest things the world still permits.
        if (affordable.Count == 0)
            affordable = available.Where(a => a.PowerAt(0) <= rank.PowerCeiling)
                                  .OrderBy(a => a.Weight)
                                  .Take(3)
                                  .ToList();

        if (affordable.Count == 0) return null;

        Spell? best = null;
        double bestMiss = double.MaxValue;

        // Six attempts, keeping the closest fit. Retrying the whole assembly rather than nudging
        // one field is deliberate: the fields interact — delivery constrains target, target
        // constrains scope, scope dominates power — so a local fix tends to break something else.
        for (int attempt = 0; attempt < 6; attempt++)
        {
            var candidate = TryAssemble(myth, rank, affordable, used, lexicon, options, rng);
            if (candidate is null) continue;

            double miss = candidate.Price > 0
                ? Math.Abs(candidate.Power - options.Exchange * candidate.Price) / (options.Exchange * candidate.Price)
                : 1;

            if (miss >= bestMiss) continue;

            best = candidate;
            bestMiss = miss;
            if (miss < 0.05) break;
        }

        return best;
    }

    private static Spell? TryAssemble(
        Cosmology myth, Rank rank, IReadOnlyList<EffectAtom> affordable, HashSet<string> used,
        Lexicon lexicon, MagicOptions options, Rng rng)
    {
        // Prefer an atom the world has not spent yet — twelve spells that are all variations on
        // one atom is the most likely way for this to read as padding.
        var pool = affordable.Where(a => !used.Contains(a.Key)).ToList();
        if (pool.Count == 0) pool = affordable.ToList();

        var lead = Weighted.Pick(rng, pool.Select(a => (a, myth.Domains.Weight(a.Domain))).ToList());

        // The atoms, and how hard each is pushed relative to the spell's overall strength. A rider
        // about a third of the time, and only within the world's emphasis, so compound spells read
        // as one idea done thoroughly rather than two unrelated things stapled together.
        var atoms = new List<(EffectAtom Atom, double Factor)> { (lead, 1.0) };

        if (rng.Chance(0.3))
        {
            var riders = affordable
                .Where(a => a.Key != lead.Key && a.Domain == lead.Domain && a.Scope <= lead.Scope)
                .ToList();

            if (riders.Count > 0) atoms.Add((rng.Pick(riders), 0.6));
        }

        var delivery = PickDelivery(myth, lead, rng);
        var target = PickTarget(lead, rng);
        var backlash = PickBacklash(myth, rank, rng);
        var exposure = PickExposure(myth, lead, delivery, rng);

        // What a cast costs before any fuel is spent: the chance it goes wrong, and the chance of
        // being seen doing it. Neither is negotiable once the delivery is chosen — an activity is a
        // gathering and a gathering is public, whatever the caster would prefer.
        double riskPrice = backlash.Probability * backlash.Severity * 6;
        double exposurePrice = exposure.Visibility * StigmaWeight(myth.Institution);
        double unavoidable = riskPrice + exposurePrice;

        // Which is why a spell has a *power floor*, and why it is different in every world. If
        // being seen practising is ruinous, a trivial effect is not worth the risk of casting it,
        // and a world that offered one anyway would be lying about its own price. So the solver
        // raises the effect until it is worth what it already costs, and gives up if the atom
        // cannot reach that far — which is how a stigma world ends up with fewer, larger spells
        // without anyone writing a rule that says so.
        const double minimumFuel = 0.5;
        double requiredPower = options.Exchange * (unavoidable + minimumFuel);

        // Power is affine in strength, so the floor solves in closed form rather than by search.
        double intercept = atoms.Sum(x => x.Atom.Weight * EffectAtoms.ScopeMultiplier(x.Atom.Scope) * 0.4);
        double slope = atoms.Sum(x => x.Atom.Weight * EffectAtoms.ScopeMultiplier(x.Atom.Scope) * 1.2 * x.Factor);

        double rolled = Math.Clamp(rng.Double(0.2, 0.75) + 0.08 * rank.Index, 0, 1);
        double needed = slope > 0 ? (requiredPower - intercept) / slope : 0;

        // Clamped rather than rejected. An atom that cannot reach its own floor still produces a
        // spell — one that costs more than it is worth — and that is a truthful thing for a harsh
        // world to contain. Rejecting instead left whole rungs of the ladder empty in exactly the
        // worlds where the low rungs matter most, and hid the imbalance rather than pricing it.
        // The miss is measured by the caller, which keeps the best of six attempts, and anything
        // still badly off budget is reported by the validator instead of silently disappearing.
        double strength = Math.Clamp(Math.Max(rolled, needed), 0, 1);

        var effects = atoms.Select(x => (x.Atom, Math.Clamp(strength * x.Factor, 0, 1))).ToList();

        double power = effects.Sum(e => e.Item1.PowerAt(e.Item2));
        if (power > rank.PowerCeiling) return null;

        // Fuel takes the difference, so power and price agree by construction. What varies between
        // worlds is not whether they balance but which column carries the weight: a stigma world
        // pays mostly in exposure and barely in fuel, a backlash world pays mostly in risk, and a
        // vital world pays in years off the caster's life.
        double fuelPrice = Math.Max(0.1, power / options.Exchange - unavoidable);
        double amount = Math.Round(fuelPrice / FuelUnitPrice(myth.Fuel), 1);
        var cost = new CostVector(myth.Fuel, amount, FuelUnit(myth.Fuel), FuelNote(myth.Fuel));

        // Equal to power/exchange whenever the effect cleared its floor, and above it when the
        // atom had to be clamped — which is the case the validator wants to hear about.
        double price = fuelPrice + unavoidable;

        return new Spell(
            Key: $"gen_spell_{rank.Index}_{lead.Key}",
            Name: lexicon.SpellName(lead, myth.Price),
            Rank: rank.Index,
            Delivery: delivery,
            Target: target,
            Requires: Preconditions(myth, rank, delivery),
            Cost: cost,
            Effects: effects,
            Backlash: backlash,
            Exposure: exposure,
            Power: Math.Round(power, 2),
            Price: Math.Round(price, 2),
            AiWeight: AiWeight(lead, rank));
    }

    private static double FuelUnitPrice(MagicFuel fuel) => fuel switch
    {
        MagicFuel.Ambient => 1.0,
        MagicFuel.Vital => 2.2,
        MagicFuel.Sacrificial => 2.6,
        MagicFuel.Devotional => 0.6,
        MagicFuel.Material => 0.25,   // gold is the cheapest thing a ruler has
        MagicFuel.Temporal => 1.4,
        MagicFuel.Debt => 1.8,
        _ => 1.0,
    };

    private static string FuelNote(MagicFuel fuel) => fuel switch
    {
        MagicFuel.Ambient => "drawn from the county it is cast in; the county recovers slowly",
        MagicFuel.Vital => "off the caster's own life",
        MagicFuel.Sacrificial => "someone else pays, and people notice who",
        MagicFuel.Devotional => "spent piety; the faith can close the tap",
        MagicFuel.Material => "gold and components",
        MagicFuel.Temporal => "only inside the window, and the window does not care about your war",
        MagicFuel.Debt => "nothing now; the ledger remembers",
        _ => "",
    };

    /// <summary>How much being seen costs, by who owns the practice.</summary>
    private static double StigmaWeight(MagicInstitution institution) => institution switch
    {
        MagicInstitution.Outlaw => 6.0,
        MagicInstitution.Cult => 5.0,
        MagicInstitution.Crown => 4.0,
        MagicInstitution.Church => 3.0,
        MagicInstitution.Solitary => 2.0,
        MagicInstitution.College => 1.5,
        MagicInstitution.Folk => 1.0,
        _ => 2.0,
    };

    private static Delivery PickDelivery(Cosmology myth, EffectAtom lead, Rng rng)
    {
        var table = new List<(Delivery, double)>();

        // Institution sets the house style.
        switch (myth.Institution)
        {
            case MagicInstitution.Cult:
                table.Add((Delivery.Scheme, 3.0)); table.Add((Delivery.Decision, 2.0));
                table.Add((Delivery.Story, 1.2)); table.Add((Delivery.Interaction, 1.0));
                table.Add((Delivery.Passive, 0.6));
                break;
            case MagicInstitution.College:
                table.Add((Delivery.Decision, 2.5)); table.Add((Delivery.Activity, 1.5));
                table.Add((Delivery.Interaction, 1.5)); table.Add((Delivery.Story, 0.5));
                table.Add((Delivery.Passive, 0.5));
                break;
            case MagicInstitution.Church:
                table.Add((Delivery.Activity, 3.0)); table.Add((Delivery.Decision, 2.0));
                table.Add((Delivery.Interaction, 1.0)); table.Add((Delivery.Passive, 0.8));
                break;
            case MagicInstitution.Crown:
                table.Add((Delivery.Decision, 2.0)); table.Add((Delivery.Interaction, 2.0));
                table.Add((Delivery.Activity, 1.0)); table.Add((Delivery.Story, 0.5));
                break;
            case MagicInstitution.Folk:
                table.Add((Delivery.Decision, 2.5)); table.Add((Delivery.Interaction, 1.5));
                table.Add((Delivery.Passive, 1.0));
                break;
            case MagicInstitution.Outlaw:
                table.Add((Delivery.Scheme, 2.5)); table.Add((Delivery.Decision, 2.0));
                table.Add((Delivery.Story, 0.8)); table.Add((Delivery.Passive, 0.6));
                break;
            default:
                table.Add((Delivery.Decision, 2.5)); table.Add((Delivery.Story, 1.5));
                table.Add((Delivery.Scheme, 1.0)); table.Add((Delivery.Passive, 0.6));
                break;
        }

        // Then the atom vetoes what it cannot be. A scheme needs a character to point at, and an
        // interaction needs one who could refuse, so neither can carry a province or a war.
        bool characterScoped = lead.Scope is EffectScope.Character or EffectScope.Court;
        var filtered = table
            .Where(t => t.Item1 is not (Delivery.Scheme or Delivery.Interaction) || characterScoped)
            .Where(t => t.Item1 != Delivery.Passive || lead.Scope is EffectScope.Self or EffectScope.Court)
            .ToList();

        if (filtered.Count == 0) filtered = [(Delivery.Decision, 1.0)];
        return Weighted.Pick(rng, filtered);
    }

    private static TargetKind PickTarget(EffectAtom lead, Rng rng) => lead.Scope switch
    {
        EffectScope.Self => TargetKind.Self,
        EffectScope.Character => lead.Polarity == EffectPolarity.Harm
            ? TargetKind.Rival
            : rng.Chance(0.5) ? TargetKind.Kin : TargetKind.Courtier,
        EffectScope.Court => TargetKind.Court,
        EffectScope.Province => TargetKind.Province,
        EffectScope.Title => TargetKind.Title,
        EffectScope.Realm => lead.Polarity == EffectPolarity.Harm ? TargetKind.Army : TargetKind.Realm,
        EffectScope.World => TargetKind.World,
        _ => TargetKind.Self,
    };

    private static BacklashSpec PickBacklash(Cosmology myth, Rank rank, Rng rng)
    {
        // Reliability sets how often, the price rule sets what happens, and the rank sets how hard
        // — a rank-four failure should be a story, not a stat.
        double probability = myth.Reliability switch
        {
            MagicReliability.Deterministic => rng.Double(0.0, 0.04),
            MagicReliability.Probabilistic => rng.Double(0.08, 0.22),
            _ => rng.Double(0.15, 0.35),
        };

        if (myth.Price == MagicPrice.Backlash) probability = Math.Min(0.5, probability * 1.8);

        double severity = rng.Double(0.6, 1.4) * (1 + 0.35 * rank.Index);
        var kind = myth.MinorPrice is not null && rng.Chance(0.3) ? myth.MinorPrice.Value : myth.Price;

        return new BacklashSpec(
            Math.Round(probability, 3),
            Math.Round(severity, 2),
            kind,
            BacklashNote(kind));
    }

    private static string BacklashNote(MagicPrice kind) => kind switch
    {
        MagicPrice.Corruption => "a step down the corruption ladder, and it shows",
        MagicPrice.Taint => "it settles in the line rather than in the caster",
        MagicPrice.Depletion => "the county pays for the attempt as if it had worked",
        MagicPrice.Attention => "something now knows the caster's name",
        MagicPrice.Stigma => "a witness, and a secret that will not stay one",
        MagicPrice.Instability => "the world's account takes the difference",
        MagicPrice.Backlash => "it lands on the caster instead",
        _ => "",
    };

    private static Exposure PickExposure(Cosmology myth, EffectAtom lead, Delivery delivery, Rng rng)
    {
        // Schemes hide, activities cannot: an activity is a gathering with guests, and a public
        // ritual is public whatever its effect. That interaction is why delivery is rolled before
        // exposure and not after.
        double baseline = delivery switch
        {
            Delivery.Scheme => 0.15,
            Delivery.Story => 0.3,
            Delivery.Decision => 0.45,
            Delivery.Interaction => 0.6,
            Delivery.Passive => 0.7,
            Delivery.Activity => 0.9,
            _ => 0.5,
        };

        if (lead.Polarity == EffectPolarity.Harm) baseline = Math.Min(1, baseline + 0.15);
        if (lead.Scope >= EffectScope.Province) baseline = Math.Min(1, baseline + 0.2);

        double visibility = Math.Clamp(baseline * rng.Double(0.8, 1.2), 0, 1);

        string consequence = myth.Institution switch
        {
            MagicInstitution.Outlaw => "a hunter's attention, and a trial if it accumulates",
            MagicInstitution.Cult => "the veil slips; the cult minds that more than the victim does",
            MagicInstitution.Crown => "practising without licence is a crime against the crown",
            MagicInstitution.Church => "the office notices, and the office reports",
            MagicInstitution.College => "a black mark on the register",
            MagicInstitution.Folk => "gossip, mostly harmless",
            _ => "rivals learn what you can do, which is its own problem",
        };

        return new Exposure(Math.Round(visibility, 2), consequence);
    }

    private static IReadOnlyList<Precondition> Preconditions(Cosmology myth, Rank rank, Delivery delivery)
    {
        var list = new List<Precondition> { new("rank", $"at least {rank.Title}") };

        switch (myth.Fuel)
        {
            case MagicFuel.Temporal:
                list.Add(new("window", "only while the window is open"));
                break;
            case MagicFuel.Ambient:
                list.Add(new("place", "cast where there is enough to draw on"));
                break;
            case MagicFuel.Sacrificial:
                list.Add(new("victim", "someone in your power to spend"));
                break;
            case MagicFuel.Devotional:
                list.Add(new("standing", "in good standing with the faith"));
                break;
        }

        if (myth.Reliability == MagicReliability.Capricious)
            list.Add(new("favour", "the entity is not currently displeased"));

        if (myth.Institution == MagicInstitution.Crown && delivery != Delivery.Scheme)
            list.Add(new("licence", "licensed, or willing to be caught unlicensed"));

        if (myth.Institution is MagicInstitution.Cult or MagicInstitution.Outlaw)
            list.Add(new("secrecy", "not currently under suspicion"));

        return list;
    }

    /// <summary>
    /// How keen the AI is. Crude on purpose — it exists so that the emitted <c>ai_will_do</c> is
    /// derived from the same numbers as everything else rather than invented at emit time, which
    /// is how an AI ends up ignoring half a generated system.
    /// </summary>
    private static double AiWeight(EffectAtom lead, Rank rank)
    {
        double weight = lead.Polarity switch
        {
            EffectPolarity.Boon => 1.0,
            EffectPolarity.Harm => 0.8,
            _ => 0.5,
        };

        // Effects an AI can evaluate get used; ones needing a plan do not.
        weight *= lead.Domain switch
        {
            MagicDomain.Life or MagicDomain.War or MagicDomain.Craft => 1.2,
            MagicDomain.Mind or MagicDomain.Death => 1.0,
            _ => 0.7,
        };

        return Math.Round(weight / (1 + 0.25 * rank.Index), 2);
    }
}
