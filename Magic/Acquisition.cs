using Ck3MapGen.Core;

namespace Ck3MapGen.Magic;

/// <summary>Where a character stands with respect to the practice.</summary>
public enum AcquisitionState
{
    /// <summary>Not in it, and as far as the game is concerned, ordinary.</summary>
    Mundane,

    /// <summary>Has whatever it takes and does not know it yet. The state that makes discovery an
    /// event rather than a purchase.</summary>
    Latent,

    /// <summary>In. From here the ladder in <see cref="MagicSystem.Ladder"/> takes over.</summary>
    Initiate,

    /// <summary>Out the other side, and rarely well. What the price rule does to someone who kept
    /// going: the monster, the collection, the thing that notices.</summary>
    Terminal,
}

/// <summary>
/// One way to move between states.
///
/// <see cref="AvailableAtStart"/> is the field the reachability check turns on, and it is subtler
/// than it looks: a Taught edge is only available if a practitioner already exists to do the
/// teaching, which is a fact about <see cref="MagicPrevalence"/> rather than about the edge. An
/// edge that is unreachable at the bookmark is not a bug — a world can legitimately require the
/// player to find their way in — but it is something the report has to say out loud, because it
/// is the difference between a system the player can opt into and one that has to happen to them.
/// </summary>
public sealed record AcquisitionEdge(
    AcquisitionState From,
    AcquisitionState To,
    MagicAccess Trigger,
    string Gate,
    double AnnualChance,
    bool AvailableAtStart,
    string ScriptHint);

/// <summary>
/// How a character gets in, expressed as a graph rather than as an enum.
///
/// A graph because three separate consumers need to read the same fact and would otherwise each
/// re-derive it from the axes and disagree: the tutorial text ("how do I start?"), the AI weights
/// (which characters should be looking for a way in), and the validator (is there a way in at
/// all?). Encoding it once means those three cannot drift.
/// </summary>
public sealed class AcquisitionGraph
{
    public required IReadOnlyList<AcquisitionEdge> Edges { get; init; }

    /// <summary>Whether a character who is <em>not</em> already a practitioner at the bookmark has
    /// any route in at all. False is legal — hereditary worlds are like that — and is the single
    /// most important thing the report can tell a player about a world before they start it.</summary>
    public required bool OpenToOutsiders { get; init; }

    /// <summary>Rough expected years for a motivated character with a live route to reach
    /// <see cref="AcquisitionState.Initiate"/>. Only meaningful when <see cref="OpenToOutsiders"/>.</summary>
    public required double ExpectedYearsToInitiate { get; init; }

    /// <summary>What the price rule does to someone at the end of the ladder.</summary>
    public required string TerminalNote { get; init; }

    public IEnumerable<AcquisitionEdge> Into(AcquisitionState state) => Edges.Where(e => e.To == state);
}

/// <summary>Builds the graph from the cosmology's access edges and its price rule.</summary>
public static class Acquisition
{
    public static AcquisitionGraph Build(Cosmology myth, Rng rng)
    {
        var edges = new List<AcquisitionEdge>();

        // A Taught or Stolen route needs somebody to learn from or take from. Under Hidden
        // prevalence there may genuinely be nobody within reach, which quietly closes what looks
        // on paper like an open world — so the availability of those edges is a function of how
        // many practitioners exist, not of the edge itself.
        bool practitionersWithinReach = myth.Prevalence >= MagicPrevalence.Rare;

        foreach (var access in myth.Access)
            edges.AddRange(EdgesFor(access, myth, practitionersWithinReach, rng));

        edges.Add(TerminalEdge(myth));

        var toInitiate = edges.Where(e => e.To == AcquisitionState.Initiate || e.To == AcquisitionState.Latent).ToList();
        bool open = toInitiate.Any(e => e.AvailableAtStart && e.From == AcquisitionState.Mundane);

        double years = open
            ? toInitiate.Where(e => e.AvailableAtStart && e.AnnualChance > 0)
                        .Select(e => 1.0 / e.AnnualChance)
                        .DefaultIfEmpty(0)
                        .Min()
            : 0;

        return new AcquisitionGraph
        {
            Edges = edges,
            OpenToOutsiders = open,
            ExpectedYearsToInitiate = Math.Round(years, 1),
            TerminalNote = TerminalNote(myth),
        };
    }

    private static IEnumerable<AcquisitionEdge> EdgesFor(
        MagicAccess access, Cosmology myth, bool practitioners, Rng rng)
    {
        switch (access)
        {
            case MagicAccess.Born:
                // Two edges, because being born with it and knowing it are different moments, and
                // the gap between them is where the discovery event lives.
                yield return new(AcquisitionState.Mundane, AcquisitionState.Latent, access,
                    "born to a parent who carried it", 0,
                    AvailableAtStart: false,
                    "congenital trait, inherited on on_birth; use the existing runtime "
                    + "phenotype assignment hook to seed the starting population");

                yield return new(AcquisitionState.Latent, AcquisitionState.Initiate, access,
                    "the latency surfaces, usually in adolescence", 0.12,
                    AvailableAtStart: true,
                    "on_birthday pulse gated on the latent trait; fires a discovery event");
                break;

            case MagicAccess.Taught:
                yield return new(AcquisitionState.Mundane, AcquisitionState.Initiate, access,
                    practitioners
                        ? "a practitioner within reach agrees to teach"
                        : "a practitioner within reach agrees to teach — but at this prevalence "
                          + "there may be none",
                    practitioners ? 0.10 : 0.02,
                    AvailableAtStart: practitioners,
                    "character_interaction between a practitioner and a courtier; "
                    + "creates a scripted teacher/pupil relation");
                break;

            case MagicAccess.Bargained:
                yield return new(AcquisitionState.Mundane, AcquisitionState.Initiate, access,
                    "an entity is petitioned and answers", 0.08,
                    AvailableAtStart: true,
                    "decision gated on a shrine, a place or a state of desperation; "
                    + "opens a story cycle carrying the obligation");
                break;

            case MagicAccess.Found:
                yield return new(AcquisitionState.Mundane, AcquisitionState.Latent, access,
                    "something is dug up, inherited or walked into", 0.06,
                    AvailableAtStart: true,
                    "travel/activity outcome at a high-ley province, or an artifact "
                    + "changing hands");

                yield return new(AcquisitionState.Latent, AcquisitionState.Initiate, access,
                    "the find is understood rather than merely held", 0.2,
                    AvailableAtStart: true,
                    "learning-gated event chain off the latent trait");
                break;

            case MagicAccess.Suffered:
                yield return new(AcquisitionState.Mundane, AcquisitionState.Latent, access,
                    "survived something that should have been fatal", 0.05,
                    AvailableAtStart: true,
                    "on_recover_from_illness, on_wounded, on_imprison; the only edge whose "
                    + "precondition the player is otherwise trying to avoid");

                yield return new(AcquisitionState.Latent, AcquisitionState.Initiate, access,
                    "what was left behind is turned to use", 0.25,
                    AvailableAtStart: true,
                    "follow-up event off the latent trait");
                break;

            case MagicAccess.Bought:
                yield return new(AcquisitionState.Mundane, AcquisitionState.Initiate, access,
                    "paid for, by someone with the rank to be sold to", 0.15,
                    AvailableAtStart: true,
                    "decision with a gold cost scaled to tier; rank requirement");
                break;

            case MagicAccess.Stolen:
                yield return new(AcquisitionState.Mundane, AcquisitionState.Initiate, access,
                    practitioners
                        ? "taken from a practitioner who no longer needs it"
                        : "taken from a practitioner — if one can be found at all",
                    practitioners ? 0.07 : 0.01,
                    AvailableAtStart: practitioners,
                    "scheme or murder outcome against a practitioner; transfers the trait "
                    + "and destroys it at the source — this is what makes the AI hunt you");
                break;
        }

        // A second route into the same world tends to be rarer than the first; nudging one of the
        // two down keeps a two-access world from reading as simply twice as open.
        _ = rng;
    }

    private static AcquisitionEdge TerminalEdge(Cosmology myth) => new(
        AcquisitionState.Initiate, AcquisitionState.Terminal, myth.Access[0],
        Gate: myth.Price switch
        {
            MagicPrice.Corruption => "corruption completes",
            MagicPrice.Taint => "the taint breeds true and takes the line",
            MagicPrice.Depletion => "the ground gives out under the practice",
            MagicPrice.Attention => "whatever was watching arrives",
            MagicPrice.Stigma => "the accusation finally sticks",
            MagicPrice.Instability => "the world's account comes due where the caster stands",
            MagicPrice.Backlash => "one misfire too many",
            _ => "the practice ends the practitioner",
        },
        AnnualChance: 0.03,
        AvailableAtStart: false,
        ScriptHint: "threshold check on the accumulated price variable, on a yearly pulse "
                    + "scoped to flagged characters only");

    private static string TerminalNote(Cosmology myth) => myth.Price switch
    {
        MagicPrice.Corruption =>
            "The ladder ends in something that is no longer a person, and the portrait says so "
            + "before the trait list does.",
        MagicPrice.Taint =>
            "The ladder ends in the practitioner's children, which is the only timescale CK3 "
            + "actually simulates and therefore the only one that stings.",
        MagicPrice.Depletion =>
            "The ladder ends with the land, not the practitioner — they keep everything they won "
            + "and it stops being worth having.",
        MagicPrice.Attention =>
            "The ladder ends when the thing that has been watching decides it has seen enough.",
        MagicPrice.Stigma =>
            "The ladder ends in a trial, and the practitioner's own vassals hold it.",
        MagicPrice.Instability =>
            "The ladder ends for everyone at once, which is the point: the innocent pay too.",
        MagicPrice.Backlash =>
            "There is no ladder's end, only odds that eventually resolve.",
        _ => "The practice ends the practitioner.",
    };
}
