using Ck3MapGen.Core;

namespace Ck3MapGen.Magic;

/// <summary>
/// What magic fundamentally <em>is</em> in this world.
///
/// This is the first axis rolled and the one the rest lean on, because it decides what kind of
/// thing a practitioner is even doing. Under <see cref="Force"/> they are a scholar; under
/// <see cref="Entities"/> they are a supplicant; under <see cref="Substance"/> they are a miner.
/// Those are different games before a single spell has been generated, which is the whole point.
///
/// Kept to six because every value here has to pull downstream weight — a source that does not
/// change what the acquisition graph or the price rule look like is decoration, and decoration
/// belongs in <see cref="Lexicon"/>.
/// </summary>
public enum MagicSource
{
    /// <summary>An impersonal law. Nobody to bargain with, so knowledge is the bottleneck and
    /// teachers and books are the chokepoints worth fighting over.</summary>
    Force,

    /// <summary>Beings grant it. Requires a roster, favour tracking and wrath; negotiation
    /// replaces study, and the world gains actors that are not characters.</summary>
    Entities,

    /// <summary>A material with a location. Extractable, tradeable and <em>exhaustible</em>, which
    /// is the only source that turns magic into a land war on its own.</summary>
    Substance,

    /// <summary>It is in the blood. The deepest CK3-native coupling there is: the marriage market
    /// becomes the magic economy without a single bespoke system.</summary>
    Inheritance,

    /// <summary>Words and true names have power. Knowledge becomes an object — stealable,
    /// burnable, copyable — so artifacts carry the system rather than characters.</summary>
    Language,

    /// <summary>Magic is damage left behind by something that already happened. Every use widens
    /// the tear. Wants a chronicle entry for the founding injury and an epicentre on the map.</summary>
    Wound,
}

/// <summary>
/// How a character gets in at all. Edges of the acquisition graph; see <see cref="Acquisition"/>.
///
/// A world may hold two of these, which is the cheapest way to manufacture faction structure:
/// two populations with the same powers and incompatible politics, with no extra emitters.
/// </summary>
public enum MagicAccess
{
    /// <summary>Congenital. Nothing you do gets you in, which makes the whole system hereditary
    /// politics and pushes the interesting play into marriage and succession.</summary>
    Born,

    /// <summary>Learned from someone who already has it. Creates teacher/pupil relations and makes
    /// the institution a real chokepoint.</summary>
    Taught,

    /// <summary>Granted by an entity in exchange for an obligation that outlives the grant.</summary>
    Bargained,

    /// <summary>Stumbled on: a place, a book, a buried thing. Puts acquisition on the map.</summary>
    Found,

    /// <summary>Survived into. Plague, maiming, near-death. The only edge whose precondition is
    /// something the player would otherwise be trying to avoid.</summary>
    Suffered,

    /// <summary>Purchased with wealth or rank. Makes magic an aristocratic amenity and removes
    /// most of the tension unless the price rule supplies it elsewhere.</summary>
    Bought,

    /// <summary>Taken from another practitioner. Zero-sum: the AI will hunt the player for it with
    /// no bespoke AI work, which makes this the highest-pressure edge in the list.</summary>
    Stolen,
}

/// <summary>
/// The resource a practitioner husbands. This is the moment-to-moment loop, and it is the axis
/// most responsible for whether two worlds feel different while being played rather than while
/// being read about.
/// </summary>
public enum MagicFuel
{
    /// <summary>A per-province scalar that depletes and regenerates. Arcane value becomes land
    /// value, so it argues for a ley field and for war over specific counties.</summary>
    Ambient,

    /// <summary>Health, lifespan and fertility of the caster. Every cast is a bite out of your own
    /// campaign, which is self-limiting without any further machinery.</summary>
    Vital,

    /// <summary>Other people. Cheap in resource and ruinous in opinion, tyranny and secrets.</summary>
    Sacrificial,

    /// <summary>Piety. The faith owns the tap and can close it, so excommunication bites twice.</summary>
    Devotional,

    /// <summary>Gold and components. Flat on its own; excellent under <see cref="MagicSource.Substance"/>
    /// where the components are finite and located.</summary>
    Material,

    /// <summary>Only inside windows — a season, a conjunction, a situation phase. Converts play
    /// from spending into planning, and couples naturally to a generated world clock.</summary>
    Temporal,

    /// <summary>Free at the point of use. The ledger collects later, with interest, at a moment
    /// the caster does not choose. The only fuel that makes the player's past a threat.</summary>
    Debt,
}

/// <summary>
/// The failure mode: what a practitioner is actually afraid of.
///
/// One dominant price per world, optionally one minor. The dominant one is the strongest single
/// determinant of how a world feels, and it has to become legible to the player inside the first
/// couple of decades or the system reads as free power with extra steps.
/// </summary>
public enum MagicPrice
{
    /// <summary>A trait ladder that ends in something that is no longer a person. Renders on the
    /// portrait through the same gene machinery the generated races already use.</summary>
    Corruption,

    /// <summary>Heritable. Your children pay for what you did, which puts the price on the one
    /// timescale CK3 actually simulates.</summary>
    Taint,

    /// <summary>The land pays. Terrain, supply and harvest degrade around use.</summary>
    Depletion,

    /// <summary>Something notices. Wrath, incursion, a hunter arriving. Wants an entity roster or
    /// a counterplay faction to be the thing doing the noticing.</summary>
    Attention,

    /// <summary>Purely social: opinion, secrets, exposure, trials. The cheapest to emit and the
    /// most dependent on the institution being hostile.</summary>
    Stigma,

    /// <summary>A world meter. Catastrophes land on everyone, including people who never cast, and
    /// that is the point — it makes non-practitioners care.</summary>
    Instability,

    /// <summary>Immediate random misfire. Weakest as a dominant price because it prices risk
    /// rather than consequence, but it pairs with anything.</summary>
    Backlash,
}

/// <summary>Who owns the practice, socially. Decides most of what gets emitted beyond spells.</summary>
public enum MagicInstitution
{
    /// <summary>Open, chartered, ranked. Court positions, buildings, a visible ladder.</summary>
    College,

    /// <summary>A secret society. Secrets, exposure, schemes — the loop is concealment.</summary>
    Cult,

    /// <summary>The faith owns it. Doctrines, holy orders, and a clergy that can revoke you.</summary>
    Church,

    /// <summary>A royal monopoly. Legal for the crown, criminal for everyone else, which makes
    /// magic a vassal-relations problem rather than a personal one.</summary>
    Crown,

    /// <summary>Unregulated and ordinary. Everywhere, low ceiling, nobody in charge.</summary>
    Folk,

    /// <summary>Actively hunted. Wants a hunter faction, a casus belli, and a hidden trait.</summary>
    Outlaw,

    /// <summary>No institution at all. Every practitioner a rival; no shared ladder to climb.</summary>
    Solitary,
}

/// <summary>What magic can reach. The palette in <see cref="EffectAtoms"/> is gated on it.</summary>
public enum MagicDomain
{
    Life,
    Death,
    War,
    Mind,
    Nature,
    Fate,
    Craft,
}

/// <summary>
/// The largest thing magic may touch. Gates which effect atoms are legal, and — through the
/// coherence rules — how expensive the world's price has to be.
/// </summary>
public enum MagicCeiling
{
    /// <summary>The caster's own body and mind.</summary>
    Personal,

    /// <summary>People within reach: courtiers, guests, rivals, kin.</summary>
    Court,

    /// <summary>Provinces, armies, titles, laws.</summary>
    Realm,

    /// <summary>The map, the weather, the dead, the calendar.</summary>
    World,
}

/// <summary>How much of the population is in on it. Feeds the runtime trait assignment hook.</summary>
public enum MagicPrevalence
{
    /// <summary>No magic. Superstition, false prophecy, and nothing that resolves. A legitimate
    /// and cheap roll that makes the magical worlds feel rarer by contrast.</summary>
    Absent,

    /// <summary>Under a percent. Most players will finish a campaign never meeting one.</summary>
    Hidden,

    /// <summary>A few percent. Present at the top of realms, notable when it appears.</summary>
    Rare,

    /// <summary>Around a sixth of characters. A known feature of courts.</summary>
    Common,

    /// <summary>Nobility-wide. Forces the ceiling down: everyone with realm-scale power is soup.</summary>
    Universal,
}

/// <summary>Whether the player is engineering or supplicating.</summary>
public enum MagicReliability
{
    /// <summary>It does what it says. Magic is a tool and play is planning.</summary>
    Deterministic,

    /// <summary>It usually does what it says. Play is risk management.</summary>
    Probabilistic,

    /// <summary>Something else decides, and it has opinions about you. Requires entities.</summary>
    Capricious,
}

/// <summary>
/// What stops a practitioner from simply winning. Never empty — <see cref="MagicValidator"/>
/// rejects a world without one, because unanswered magic ends the campaign it was added to.
/// </summary>
public enum MagicCounterplay
{
    /// <summary>Buildings and provinces where it does not work.</summary>
    Wards,

    /// <summary>People whose job is you. A faction, a casus belli, a scheme.</summary>
    Hunters,

    /// <summary>Non-practitioners get a structural advantage that practitioners cannot have.</summary>
    Compensation,

    /// <summary>Other practitioners. Mutual threat rather than an external check.</summary>
    Deterrence,

    /// <summary>The fuel runs out. The only counterplay that needs no actor at all.</summary>
    Scarcity,
}

/// <summary>
/// The domain distribution, and — more importantly — the forbidden set.
///
/// Forbidding matters more than emphasising. "This world has no healing" is a structural fact the
/// player routes every other decision around; "this world is 22% Life" is a number nobody can
/// feel. So the sampler always forbids at least one domain, and the report leads with it.
/// </summary>
public sealed class DomainWeights
{
    private readonly Dictionary<MagicDomain, double> _weights;

    public DomainWeights(IDictionary<MagicDomain, double> weights, IEnumerable<MagicDomain> forbidden)
    {
        _weights = new Dictionary<MagicDomain, double>(weights);
        Forbidden = new HashSet<MagicDomain>(forbidden);

        foreach (var domain in Forbidden) _weights[domain] = 0;
    }

    public IReadOnlySet<MagicDomain> Forbidden { get; }

    public double Weight(MagicDomain domain) => _weights.TryGetValue(domain, out double w) ? w : 0;

    public bool Allows(MagicDomain domain) => !Forbidden.Contains(domain) && Weight(domain) > 0;

    public IEnumerable<MagicDomain> Allowed => Enum.GetValues<MagicDomain>().Where(Allows);

    /// <summary>The heaviest allowed domain. Names the tradition and biases the lexicon.</summary>
    public MagicDomain Primary => Allowed.OrderByDescending(Weight).First();

    /// <summary>Ordered heaviest-first, for the report and for spell selection.</summary>
    public IReadOnlyList<(MagicDomain Domain, double Weight)> Ranked =>
        Allowed.Select(d => (d, Weight(d))).OrderByDescending(p => p.Item2).ToList();
}

/// <summary>
/// One world's answer to every axis. Pure data and no CK3 vocabulary anywhere in it — everything
/// downstream reads this, and nothing here knows what a decision or a trait is.
///
/// A record, because the coherence pass works by producing repaired copies rather than by mutating
/// in place; that keeps the repair trace honest, since every step can print what it changed.
/// </summary>
public sealed record Cosmology
{
    public required MagicSource Source { get; init; }

    /// <summary>One or two edges. Two is where factions come from.</summary>
    public required IReadOnlyList<MagicAccess> Access { get; init; }

    public required MagicFuel Fuel { get; init; }

    public required MagicPrice Price { get; init; }

    public MagicPrice? MinorPrice { get; init; }

    public required MagicInstitution Institution { get; init; }

    public required DomainWeights Domains { get; init; }

    public required MagicCeiling Ceiling { get; init; }

    public required MagicPrevalence Prevalence { get; init; }

    public required MagicReliability Reliability { get; init; }

    public required IReadOnlyList<MagicCounterplay> Counterplay { get; init; }

    /// <summary>
    /// The five facts the Five Differences test is measured on: acquisition, resource, failure
    /// mode, social position, world coupling. Two worlds are meaningfully different when at least
    /// three of these differ, and <see cref="MagicReport.Sweep"/> checks exactly that.
    ///
    /// World coupling is <see cref="Ceiling"/> paired with the dominant price, because "how far it
    /// reaches" and "who else pays" are the two halves of what magic does to people who are not
    /// using it.
    /// </summary>
    public string[] LoopSignature() =>
    [
        string.Join("+", Access.Order()),
        Fuel.ToString(),
        Price.ToString(),
        Institution.ToString(),
        $"{Ceiling}/{Price}",
    ];
}

/// <summary>Weighted choice, kept here so every sampler in the folder draws the same way.</summary>
internal static class Weighted
{
    public static T Pick<T>(Rng rng, IReadOnlyList<(T Value, double Weight)> options)
    {
        double total = options.Sum(o => o.Weight);
        if (total <= 0) return options[rng.Int(0, options.Count - 1)].Value;

        double roll = rng.Double() * total;
        foreach (var (value, weight) in options)
        {
            roll -= weight;
            if (roll <= 0) return value;
        }

        return options[^1].Value;
    }

    /// <summary>Uniform pick over an enum's values.</summary>
    public static T Enum<T>(Rng rng) where T : struct, System.Enum
    {
        var values = System.Enum.GetValues<T>();
        return values[rng.Int(0, values.Length - 1)];
    }
}

/// <summary>Rolls the ten axes. Everything it produces goes straight through <see cref="Coherence"/>.</summary>
public static class CosmologySampler
{
    public static Cosmology Roll(Rng rng, MagicOptions options)
    {
        var source = Weighted.Pick(rng,
        [
            (MagicSource.Force, 1.0),
            (MagicSource.Entities, 1.2),
            (MagicSource.Substance, 1.0),
            (MagicSource.Inheritance, 1.3),  // heaviest: it is the one CK3 simulates for free
            (MagicSource.Language, 0.9),
            (MagicSource.Wound, 1.0),
        ]);

        var access = RollAccess(rng, source);
        var fuel = RollFuel(rng, source);
        var price = RollPrice(rng, source, fuel);

        MagicPrice? minor = rng.Chance(0.45)
            ? Enum.GetValues<MagicPrice>().Where(p => p != price).ToList()[rng.Int(0, 5)]
            : null;

        var prevalence = options.Presence ?? RollPrevalence(rng, options.AllowMundane);

        return new Cosmology
        {
            Source = source,
            Access = access,
            Fuel = fuel,
            Price = price,
            MinorPrice = minor,
            Institution = RollInstitution(rng, source, access),
            Domains = RollDomains(rng, source),
            Ceiling = RollCeiling(rng, options.CeilingCap),
            Prevalence = prevalence,
            Reliability = RollReliability(rng, source),
            Counterplay = RollCounterplay(rng, price),
        };
    }

    private static IReadOnlyList<MagicAccess> RollAccess(Rng rng, MagicSource source)
    {
        // The source biases how one gets in without determining it: a Substance world can still be
        // hereditary (some bloodlines tolerate the stuff), it is just likelier to be found.
        (MagicAccess, double)[] table = source switch
        {
            MagicSource.Inheritance =>
                [(MagicAccess.Born, 4.0), (MagicAccess.Taught, 0.6), (MagicAccess.Stolen, 0.8),
                 (MagicAccess.Suffered, 0.4), (MagicAccess.Bargained, 0.3), (MagicAccess.Found, 0.3),
                 (MagicAccess.Bought, 0.2)],
            MagicSource.Entities =>
                [(MagicAccess.Bargained, 3.5), (MagicAccess.Suffered, 1.0), (MagicAccess.Taught, 0.8),
                 (MagicAccess.Born, 0.6), (MagicAccess.Found, 0.6), (MagicAccess.Stolen, 0.4),
                 (MagicAccess.Bought, 0.3)],
            MagicSource.Substance =>
                [(MagicAccess.Found, 2.5), (MagicAccess.Bought, 1.8), (MagicAccess.Stolen, 1.2),
                 (MagicAccess.Taught, 0.8), (MagicAccess.Born, 0.4), (MagicAccess.Bargained, 0.3),
                 (MagicAccess.Suffered, 0.3)],
            MagicSource.Language =>
                [(MagicAccess.Taught, 3.0), (MagicAccess.Found, 1.8), (MagicAccess.Stolen, 1.4),
                 (MagicAccess.Bought, 0.7), (MagicAccess.Born, 0.4), (MagicAccess.Bargained, 0.3),
                 (MagicAccess.Suffered, 0.2)],
            MagicSource.Wound =>
                [(MagicAccess.Suffered, 3.0), (MagicAccess.Found, 1.5), (MagicAccess.Born, 1.0),
                 (MagicAccess.Bargained, 0.6), (MagicAccess.Taught, 0.5), (MagicAccess.Stolen, 0.5),
                 (MagicAccess.Bought, 0.2)],
            _ =>
                [(MagicAccess.Taught, 2.5), (MagicAccess.Found, 1.2), (MagicAccess.Born, 1.0),
                 (MagicAccess.Bought, 0.8), (MagicAccess.Stolen, 0.6), (MagicAccess.Suffered, 0.5),
                 (MagicAccess.Bargained, 0.4)],
        };

        var first = Weighted.Pick(rng, table);

        // A second edge roughly a third of the time. Deliberately drawn from the *tail* of the
        // table rather than the head, because two likely edges produce two versions of the same
        // population, and the interesting case is a common route plus an unlikely one.
        if (!rng.Chance(0.34)) return [first];

        var rest = table.Where(t => !Equals(t.Item1, first)).OrderBy(t => t.Item2).Take(4).ToList();
        var second = Weighted.Pick(rng, rest.Select(t => (t.Item1, 1.0)).ToList());
        return [first, second];
    }

    private static MagicFuel RollFuel(Rng rng, MagicSource source) => Weighted.Pick(rng,
    [
        (MagicFuel.Ambient, source == MagicSource.Substance ? 3.0 : 1.0),
        (MagicFuel.Vital, source is MagicSource.Inheritance or MagicSource.Wound ? 2.2 : 1.0),
        (MagicFuel.Sacrificial, source == MagicSource.Entities ? 1.8 : 0.8),
        (MagicFuel.Devotional, source == MagicSource.Entities ? 2.0 : 0.5),
        (MagicFuel.Material, source is MagicSource.Substance or MagicSource.Language ? 1.6 : 0.7),
        (MagicFuel.Temporal, 1.1),
        (MagicFuel.Debt, source is MagicSource.Entities or MagicSource.Wound ? 1.8 : 0.9),
    ]);

    private static MagicPrice RollPrice(Rng rng, MagicSource source, MagicFuel fuel) => Weighted.Pick(rng,
    [
        (MagicPrice.Corruption, source == MagicSource.Wound ? 2.4 : 1.2),
        (MagicPrice.Taint, source == MagicSource.Inheritance ? 2.6 : 0.8),
        (MagicPrice.Depletion, fuel == MagicFuel.Ambient ? 3.0 : 0.4),
        (MagicPrice.Attention, source == MagicSource.Entities ? 2.4 : 0.9),
        (MagicPrice.Stigma, 1.3),
        (MagicPrice.Instability, source == MagicSource.Wound ? 2.0 : 1.0),
        (MagicPrice.Backlash, 0.9),
    ]);

    private static MagicInstitution RollInstitution(
        Rng rng, MagicSource source, IReadOnlyList<MagicAccess> access) => Weighted.Pick(rng,
    [
        (MagicInstitution.College, access.Contains(MagicAccess.Taught) ? 2.4 : 0.8),
        (MagicInstitution.Cult, access.Contains(MagicAccess.Bargained) ? 2.2 : 1.2),
        (MagicInstitution.Church, source == MagicSource.Entities ? 2.2 : 0.7),
        (MagicInstitution.Crown, 1.0),
        (MagicInstitution.Folk, 1.0),
        (MagicInstitution.Outlaw, 1.4),
        (MagicInstitution.Solitary, access.Contains(MagicAccess.Stolen) ? 2.0 : 0.9),
    ]);

    private static DomainWeights RollDomains(Rng rng, MagicSource source)
    {
        var all = Enum.GetValues<MagicDomain>().ToList();

        // Source bias, then two or three emphases, then one or two prohibitions taken from what is
        // left. Prohibiting before weighting would let the sampler forbid the emphasis it just set.
        var bias = new Dictionary<MagicDomain, double>();
        foreach (var d in all) bias[d] = 1.0;

        switch (source)
        {
            case MagicSource.Inheritance: bias[MagicDomain.Life] += 1.2; bias[MagicDomain.Fate] += 0.8; break;
            case MagicSource.Entities: bias[MagicDomain.Fate] += 1.2; bias[MagicDomain.Mind] += 0.8; break;
            case MagicSource.Substance: bias[MagicDomain.Craft] += 1.4; bias[MagicDomain.Nature] += 0.8; break;
            case MagicSource.Language: bias[MagicDomain.Mind] += 1.4; bias[MagicDomain.Fate] += 0.6; break;
            case MagicSource.Wound: bias[MagicDomain.Death] += 1.4; bias[MagicDomain.Nature] += 0.8; break;
            case MagicSource.Force: bias[MagicDomain.Nature] += 0.8; bias[MagicDomain.War] += 0.6; break;
        }

        var weights = new Dictionary<MagicDomain, double>();
        foreach (var d in all) weights[d] = bias[d] * rng.Double(0.15, 1.0);

        int emphases = rng.Int(2, 3);
        var order = all.OrderByDescending(d => weights[d]).ToList();
        for (int i = 0; i < emphases; i++) weights[order[i]] *= rng.Double(1.8, 3.0);

        int prohibitions = rng.Int(1, 2);
        var forbidden = all.OrderBy(d => weights[d]).Take(prohibitions).ToList();

        return new DomainWeights(weights, forbidden);
    }

    private static MagicCeiling RollCeiling(Rng rng, MagicCeiling? cap)
    {
        var rolled = Weighted.Pick(rng,
        [
            (MagicCeiling.Personal, 1.0),
            (MagicCeiling.Court, 1.6),
            (MagicCeiling.Realm, 1.3),
            (MagicCeiling.World, 0.5),
        ]);

        return cap is null ? rolled : (MagicCeiling)Math.Min((int)rolled, (int)cap.Value);
    }

    private static MagicPrevalence RollPrevalence(Rng rng, bool allowMundane) => Weighted.Pick(rng,
    [
        (MagicPrevalence.Absent, allowMundane ? 0.7 : 0.0),
        (MagicPrevalence.Hidden, 1.6),
        (MagicPrevalence.Rare, 2.0),
        (MagicPrevalence.Common, 1.2),
        (MagicPrevalence.Universal, 0.4),
    ]);

    private static MagicReliability RollReliability(Rng rng, MagicSource source) => Weighted.Pick(rng,
    [
        (MagicReliability.Deterministic, source == MagicSource.Entities ? 0.3 : 1.4),
        (MagicReliability.Probabilistic, 1.6),
        (MagicReliability.Capricious, source == MagicSource.Entities ? 2.0 : 0.6),
    ]);

    private static IReadOnlyList<MagicCounterplay> RollCounterplay(Rng rng, MagicPrice price)
    {
        var table = new List<(MagicCounterplay, double)>
        {
            (MagicCounterplay.Wards, 1.0),
            (MagicCounterplay.Hunters, price is MagicPrice.Stigma or MagicPrice.Attention ? 2.4 : 1.0),
            (MagicCounterplay.Compensation, 0.9),
            (MagicCounterplay.Deterrence, 1.2),
            (MagicCounterplay.Scarcity, price == MagicPrice.Depletion ? 2.2 : 0.8),
        };

        var first = Weighted.Pick(rng, table);
        if (!rng.Chance(0.4)) return [first];

        var second = Weighted.Pick(rng, table.Where(t => t.Item1 != first).ToList());
        return [first, second];
    }
}

/// <summary>One coherence constraint: what it wants, and how to get there from here.</summary>
public sealed record CoherenceRule(
    string Name,
    Func<Cosmology, bool> Holds,
    Func<Cosmology, Rng, Cosmology> Repair);

/// <summary>
/// Repairs a rolled cosmology into a coherent one.
///
/// Independently sampled axes produce nonsense at a high rate — devotional fuel with no religious
/// institution to own the tap, entities that nobody can reach, world-scale power with a purely
/// social price that the player can simply out-rank. The alternative to repair is rejection
/// sampling, which is worse: it silently biases the distribution toward whatever combination
/// happens to be easiest to satisfy, and it gives no account of itself when a world comes out odd.
///
/// Repairing instead keeps every axis reachable and leaves a trace the report can print, so a
/// world that reads strangely can be traced to the rule that made it that way rather than to the
/// seed.
/// </summary>
public static class Coherence
{
    /// <summary>
    /// Order matters only in that later rules see earlier repairs; every rule is re-checked to a
    /// fixed point, so a repair that breaks an earlier rule is caught rather than shipped.
    /// </summary>
    public static IReadOnlyList<CoherenceRule> Rules { get; } =
    [
        new("devotional fuel needs a religious owner",
            c => c.Fuel != MagicFuel.Devotional
                 || c.Institution is MagicInstitution.Church or MagicInstitution.Cult,
            (c, rng) => c with { Institution = rng.Chance(0.7) ? MagicInstitution.Church : MagicInstitution.Cult }),

        new("entities cannot be deterministic",
            c => c.Source != MagicSource.Entities || c.Reliability != MagicReliability.Deterministic,
            (c, rng) => c with { Reliability = rng.Chance(0.6) ? MagicReliability.Capricious : MagicReliability.Probabilistic }),

        new("capricious magic needs someone to be capricious",
            c => c.Reliability != MagicReliability.Capricious || c.Source == MagicSource.Entities,
            (c, _) => c with { Reliability = MagicReliability.Probabilistic }),

        new("depletion prices ambient fuel",
            c => c.Price != MagicPrice.Depletion || c.Fuel == MagicFuel.Ambient,
            (c, _) => c with { Fuel = MagicFuel.Ambient }),

        new("world-scale power needs a world-scale price",
            c => c.Ceiling != MagicCeiling.World
                 || c.Price is MagicPrice.Instability or MagicPrice.Depletion
                 || c.MinorPrice is MagicPrice.Instability,
            (c, _) => c with { MinorPrice = MagicPrice.Instability }),

        new("universal prevalence caps the ceiling at court",
            c => c.Prevalence != MagicPrevalence.Universal || c.Ceiling <= MagicCeiling.Court,
            (c, _) => c with { Ceiling = MagicCeiling.Court }),

        new("bargained access needs entities to bargain with",
            c => !c.Access.Contains(MagicAccess.Bargained) || c.Source == MagicSource.Entities,
            (c, _) => c with { Access = c.Access.Where(a => a != MagicAccess.Bargained).DefaultIfEmpty(MagicAccess.Taught).ToList() }),

        new("stigma needs someone to do the stigmatising",
            c => c.Price != MagicPrice.Stigma
                 || c.Institution is MagicInstitution.Outlaw or MagicInstitution.Cult
                                  or MagicInstitution.Crown or MagicInstitution.Church,
            (c, rng) => c with { Institution = rng.Chance(0.6) ? MagicInstitution.Outlaw : MagicInstitution.Crown }),

        new("an outlawed practice cannot be universal",
            c => c.Institution != MagicInstitution.Outlaw || c.Prevalence <= MagicPrevalence.Rare,
            (c, _) => c with { Prevalence = MagicPrevalence.Rare }),

        new("counterplay is never empty",
            c => c.Counterplay.Count > 0,
            (c, _) => c with { Counterplay = [MagicCounterplay.Hunters] }),

        new("scarcity counterplay needs a finite fuel",
            c => !c.Counterplay.Contains(MagicCounterplay.Scarcity)
                 || c.Fuel is MagicFuel.Ambient or MagicFuel.Material or MagicFuel.Temporal,
            (c, _) => c with { Counterplay = c.Counterplay.Where(x => x != MagicCounterplay.Scarcity)
                                             .DefaultIfEmpty(MagicCounterplay.Deterrence).ToList() }),

        new("at least two domains remain",
            c => c.Domains.Allowed.Count() >= 2,
            (c, _) => c with { Domains = Widen(c.Domains) }),

        new("an absent world has nothing to price",
            c => c.Prevalence != MagicPrevalence.Absent || c.Ceiling == MagicCeiling.Personal,
            (c, _) => c with { Ceiling = MagicCeiling.Personal }),
    ];

    /// <summary>
    /// Applies the rules to a fixed point. Returns the repaired cosmology; <paramref name="trace"/>
    /// receives one line per repair, which is what the report prints under the axes.
    /// </summary>
    public static Cosmology Resolve(Cosmology rolled, Rng rng, out List<string> trace)
    {
        trace = [];
        var current = rolled;

        // Twelve passes is far more than the rule set can need — the longest repair chain is three
        // — so exhausting it means two rules are fighting, which is a bug in the rules and not in
        // the seed. It is reported rather than looped on.
        for (int pass = 0; pass < 12; pass++)
        {
            bool changed = false;

            foreach (var rule in Rules)
            {
                if (rule.Holds(current)) continue;

                current = rule.Repair(current, rng);
                trace.Add($"{rule.Name}");
                changed = true;
            }

            if (!changed) return current;
        }

        trace.Add("!! coherence did not converge; two rules are contradicting each other");
        return current;
    }

    /// <summary>Un-forbids the heaviest forbidden domain, for when prohibitions went too far.</summary>
    private static DomainWeights Widen(DomainWeights domains)
    {
        var weights = new Dictionary<MagicDomain, double>();
        foreach (var d in Enum.GetValues<MagicDomain>()) weights[d] = Math.Max(domains.Weight(d), 0.2);

        var keep = domains.Forbidden.Skip(1).ToList();
        return new DomainWeights(weights, keep);
    }
}
