using Ck3MapGen.Core;

namespace Ck3MapGen.Magic;

/// <summary>
/// Derives a whole magic system from a seed. The entry point for everything in this folder.
///
/// Nothing here touches the world generator, and that is deliberate for now: a system that can
/// only be looked at by generating a map cannot be iterated on, and the question this folder has
/// to answer first is whether the output is any good. Placement on the map, seeding into history,
/// and every emitter are the next layers, and they read the record this produces rather than
/// re-deriving anything.
///
/// The seed is offset from the world seed by a fixed mix so that a map keeps its magic when
/// something unrelated upstream consumes a different number of random draws.
/// </summary>
public static class MagicGenerator
{
    public static MagicSystem Generate(int seed, MagicOptions? options = null)
    {
        options ??= new MagicOptions();

        var rng = new Rng(Rng.StableHash($"magic:{seed}"));

        var rolled = CosmologySampler.Roll(rng, options);
        var myth = Coherence.Resolve(rolled, rng, out var trace);

        var lexicon = new Lexicon(rng, Weighted.Enum<PhonoStyle>(rng));
        var ladder = BuildLadder(myth, lexicon, options, rng);
        var ledger = BuildLedger(myth, rng);

        return new MagicSystem
        {
            Seed = seed,
            Myth = myth,
            Naming = new MagicNaming(
                lexicon.TraditionName(myth),
                lexicon.InstitutionName(myth.Institution),
                lexicon.Style),
            Access = Acquisition.Build(myth, rng),
            Ladder = ladder,
            Spells = SpellGrammar.Build(myth, ladder, lexicon, options, rng),
            Entities = EntityRoster.Build(myth, lexicon, rng),
            Prophecies = Prophecies.Build(myth, ledger, lexicon, options, rng),
            Counter = BuildCounterplay(myth),
            Keystone = BuildKeystone(myth, rng),
            Ledger = ledger,
            CoherenceTrace = trace,
        };
    }

    /// <summary>Generates <paramref name="count"/> consecutive worlds. Used by the sweep report.</summary>
    public static IReadOnlyList<MagicSystem> GenerateMany(int firstSeed, int count, MagicOptions? options = null)
        => Enumerable.Range(firstSeed, count).Select(s => Generate(s, options)).ToList();

    private static IReadOnlyList<Rank> BuildLadder(
        Cosmology myth, Lexicon lexicon, MagicOptions options, Rng rng)
    {
        if (myth.Prevalence == MagicPrevalence.Absent) return [];

        int count = options.RankCount > 0 ? Math.Clamp(options.RankCount, 1, 5) : rng.Int(3, 4);
        var titles = lexicon.RankTitles(myth.Institution, count);

        // Geometric, and steeply so. A linear ladder spreads the interesting spells evenly, which
        // means there is never a moment where the world visibly opens up.
        const double baseCeiling = 5.5;
        const double growth = 1.85;

        return Enumerable.Range(0, count).Select(i => new Rank(
            Index: i,
            Key: $"gen_magic_rank_{i}",
            Title: titles[Math.Min(i, titles.Count - 1)],
            PowerCeiling: Math.Round(baseCeiling * Math.Pow(growth, i), 2),
            Gate: RankGate(myth, i, count))).ToList();
    }

    private static string RankGate(Cosmology myth, int index, int count)
    {
        if (index == 0) return "entry, by whatever route the world allows";

        bool top = index == count - 1;

        return myth.Institution switch
        {
            MagicInstitution.College => top
                ? "a chair falls vacant, and the college votes"
                : "examined and passed by someone already above you",
            MagicInstitution.Cult => top
                ? "the one above you dies, and nobody else was closer"
                : "trusted with one more thing than last time",
            MagicInstitution.Church => top
                ? "ordained by the office itself, which does that rarely"
                : "vested after a term of service",
            MagicInstitution.Crown => top
                ? "appointed by the crown, which means the crown must want you"
                : "a higher licence, granted for service",
            MagicInstitution.Folk => top
                ? "outlived everyone who could correct you"
                : "known well enough that people come from the next valley",
            MagicInstitution.Outlaw => top
                ? "notorious enough that hunting you is somebody's career"
                : "survived long enough to be worth naming",
            _ => top
                ? "took it from the last person who had it"
                : "worked it out alone, which is the only way here",
        };
    }

    /// <summary>
    /// The world meter, when the world's price is the sort that accumulates.
    ///
    /// Not every world gets one. A stigma world's price lands entirely on the individual caught,
    /// and giving it a global meter would be adding a second, unrelated price rule — the ledger
    /// exists to aggregate prices that are *already* diffuse, not to invent diffusion.
    /// </summary>
    private static LedgerRule BuildLedger(Cosmology myth, Rng rng)
    {
        // Debt fuel forces one regardless of the price rule. A world where casting is free at the
        // point of use and nothing anywhere accumulates is not a debt world, it is a world with no
        // price at all — the ledger *is* the collector.
        bool enabled = myth.Fuel == MagicFuel.Debt
                       || myth.Price is MagicPrice.Instability or MagicPrice.Depletion or MagicPrice.Attention
                       || myth.MinorPrice is MagicPrice.Instability
                       || myth.Ceiling >= MagicCeiling.Realm;

        if (!enabled)
            return new LedgerRule(false, 0, [],
                "No world meter: this world's price lands on the caster, not on the commons.");

        double decay = Math.Round(rng.Double(0.04, 0.12), 3);

        var thresholds = new List<(double, string)>
        {
            (100, myth.Price switch
            {
                MagicPrice.Depletion => "the highest-drawn county visibly degrades; supply and growth fall",
                MagicPrice.Attention => "the first arrival: something is in the world that was not",
                _ => "the world clock advances a phase early",
            }),
            (250, myth.Ceiling >= MagicCeiling.Realm
                ? "an outbreak seeds itself at the most-drawn county"
                : "hunters organise, and they have a name and a patron now"),
            (500, myth.Price switch
            {
                MagicPrice.Instability => "the tear widens: the price rule applies to everyone, practitioner or not",
                MagicPrice.Depletion => "the fuel stops regenerating; what is left is all there is",
                _ => "whatever has been waiting stops waiting",
            }),
        };

        return new LedgerRule(true, decay, thresholds,
            "Every cast, player and AI alike, adds its power to one global variable that decays "
            + "yearly. This is what makes an AI-heavy world drift on its own.");
    }

    private static CounterplayPlan BuildCounterplay(Cosmology myth)
    {
        var descriptions = myth.Counterplay.Select(k => k switch
        {
            MagicCounterplay.Wards =>
                "Warded ground: buildings and whole counties where it does not work, which turns "
                + "the map into a board rather than a backdrop.",
            MagicCounterplay.Hunters =>
                "Hunters: a faction whose members gain from finding practitioners, with a casus "
                + "belli and a scheme of their own.",
            MagicCounterplay.Compensation =>
                "Compensation: characters with no part in the practice get an advantage "
                + "practitioners structurally cannot have.",
            MagicCounterplay.Deterrence =>
                "Deterrence: the check on a practitioner is other practitioners, which makes the "
                + "counterplay scale with the problem automatically.",
            MagicCounterplay.Scarcity =>
                "Scarcity: the fuel runs out. The only counterplay that needs no actor at all, and "
                + "the only one that cannot be outmanoeuvred.",
            _ => "",
        });

        return new CounterplayPlan(
            myth.Counterplay,
            string.Join(" ", descriptions),
            myth.Counterplay.Contains(MagicCounterplay.Hunters)
                ? "casus belli, a court position, a scheme, and an AI faction weight"
                : "buildings, county modifiers, and a trigger the spells all check");
    }

    /// <summary>
    /// Picks the one place this system deliberately touches another generated system.
    ///
    /// One, not several. A keystone the player can find and exploit is worth more than five they
    /// never notice, and each additional coupling multiplies what has to be verified.
    /// </summary>
    private static KeystoneLink BuildKeystone(Cosmology myth, Rng rng)
    {
        var candidates = new List<(KeystoneLink, double)>
        {
            (new KeystoneLink("situation",
                "Casting is cheaper during one phase of the world's generated situation, so the "
                + "world clock becomes a schedule practitioners plan around rather than weather "
                + "they endure.",
                "spell cost script value reads the situation's current phase"),
             myth.Fuel == MagicFuel.Temporal ? 3.0 : 1.0),

            (new KeystoneLink("epidemic",
                "The generated plague spreads faster where the practice is heaviest, which puts "
                + "practitioners and their neighbours on opposite sides of a problem neither "
                + "chose.",
                "epidemic susceptibility modifier keyed to the ledger and to province flags"),
             myth.Domains.Allows(MagicDomain.Death) || myth.Domains.Allows(MagicDomain.Life) ? 2.2 : 0.6),

            (new KeystoneLink("struggle",
                "The top of the ladder is only reachable by someone on one particular side of the "
                + "generated struggle, which makes a regional quarrel the gate on personal power.",
                "rank trigger checks struggle involvement and faction"),
             myth.Institution is MagicInstitution.Outlaw or MagicInstitution.Crown ? 2.0 : 0.8),

            (new KeystoneLink("wonder",
                "One generated wonder sits on the strongest node, so the single most contested "
                + "building on the map is contested for a second, unrelated reason.",
                "ley field maximum forced to coincide with a wonder's barony"),
             myth.Fuel == MagicFuel.Ambient || myth.Source == MagicSource.Substance ? 2.6 : 0.7),

            (new KeystoneLink("race",
                "One generated race carries the latency at a far higher rate, which turns a "
                + "demographic fact into a political one and gives the marriage market a second "
                + "axis.",
                "prevalence weighting keyed to the race trait; shares the gene and trait layer"),
             myth.Source == MagicSource.Inheritance ? 3.0 : 0.5),

            (new KeystoneLink("chronicle",
                "The founding injury is a real chronicle entry with a real location, and the "
                + "closer a county is to it the stronger and cheaper the practice runs there.",
                "ley field derived from distance to the chronicle epicentre"),
             myth.Source == MagicSource.Wound ? 3.2 : 0.6),
        };

        return Weighted.Pick(rng, candidates);
    }
}
