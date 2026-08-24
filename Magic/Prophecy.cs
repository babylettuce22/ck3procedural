using Ck3MapGen.Core;

namespace Ck3MapGen.Magic;

/// <summary>
/// The world-state question a prophecy is really asking.
///
/// Every one of these is a trigger the engine can evaluate on a pulse. That is the whole trick:
/// a prophecy is not prose with an event attached, it is a predicate the world is quietly checking
/// every year, and the prose is how the player finds out what the predicate is before it resolves.
/// </summary>
public enum PropheticPredicate
{
    /// <summary>A named line holds a named seat.</summary>
    TitleHeldByLine,

    /// <summary>A faith reaches a count of counties.</summary>
    FaithSpread,

    /// <summary>Someone bearing the mark is still alive past a date.</summary>
    BearerSurvives,

    /// <summary>The ledger crosses a threshold. The prophecy the players write themselves.</summary>
    LedgerThreshold,

    /// <summary>A year simply arrives. The only one that cannot be prevented.</summary>
    YearReached,

    /// <summary>A particular object is held by anyone at all.</summary>
    ArtifactRecovered,

    /// <summary>A line ends. Cheap to check, and satisfying to have caused.</summary>
    LineExtinct,
}

/// <summary>
/// One prophecy: a predicate, a consequence, and the prose that foreshadows both.
///
/// <see cref="CanEverFire"/> is the field that matters most and it is the one that looks like a
/// bug. Some prophecies are generated unsatisfiable on purpose. A world where every prophecy comes
/// true is a world where prophecy is a quest log, and the player learns within one campaign that
/// prophetic text is a reliable instruction. Making a third of them false restores the only thing
/// that made prophecy interesting in the first place, which is not knowing.
/// </summary>
public sealed record Prophecy(
    string Key,
    string Text,
    PropheticPredicate Predicate,
    string PredicateNote,
    string Consequence,
    bool CanEverFire,
    int? EarliestYear,
    string ScriptHint);

/// <summary>Generates the prophecies, true and otherwise.</summary>
public static class Prophecies
{
    public static IReadOnlyList<Prophecy> Build(
        Cosmology myth, LedgerRule ledger, Lexicon lexicon, MagicOptions options, Rng rng)
    {
        var list = new List<Prophecy>();
        var spent = new HashSet<PropheticPredicate>();

        for (int i = 0; i < Math.Max(0, options.ProphecyCount); i++)
        {
            var predicate = Weighted.Pick(rng,
            [
                (PropheticPredicate.TitleHeldByLine, 1.4),
                (PropheticPredicate.FaithSpread, 1.0),
                (PropheticPredicate.BearerSurvives, myth.Prevalence >= MagicPrevalence.Rare ? 1.4 : 0.5),
                (PropheticPredicate.LedgerThreshold, ledger.Enabled ? 1.8 : 0.0),
                (PropheticPredicate.YearReached, 0.8),
                (PropheticPredicate.ArtifactRecovered, 1.2),
                (PropheticPredicate.LineExtinct, 1.0),
            ]);

            // One predicate each. The ledger one in particular has no varying noun in its prose,
            // so two of them in a world produce two identical prophecies — which reads as a bug in
            // the generator rather than as an echo, however atmospheric the intent.
            if (!spent.Add(predicate))
            {
                var unspent = Enum.GetValues<PropheticPredicate>().Where(p => !spent.Contains(p)).ToList();
                if (unspent.Count == 0) break;

                predicate = rng.Pick(unspent);
                spent.Add(predicate);
            }

            // A third are unsatisfiable. Never the ledger ones — those are the prophecies the
            // players cause themselves, and a false one there would read as the meter being broken
            // rather than as the prophecy being wrong.
            bool canFire = predicate == PropheticPredicate.LedgerThreshold || !rng.Chance(0.33);

            list.Add(new Prophecy(
                Key: $"gen_prophecy_{i}",
                Text: Text(predicate, canFire, myth, lexicon, rng),
                Predicate: predicate,
                PredicateNote: Note(predicate, canFire, rng),
                Consequence: Consequence(myth, rng),
                CanEverFire: canFire,
                EarliestYear: predicate == PropheticPredicate.YearReached ? rng.Int(30, 180) : null,
                ScriptHint: "predicate on a yearly global pulse; foreshadowing as a legend and a "
                            + "chronicle entry at game start; consequence scaled to the ceiling"));
        }

        return list;
    }

    private static string Text(
        PropheticPredicate predicate, bool canFire, Cosmology myth, Lexicon lexicon, Rng rng)
    {
        string place = lexicon.Word();
        string line = lexicon.Word();

        string body = predicate switch
        {
            PropheticPredicate.TitleHeldByLine =>
                $"when the seat at {place} is held by one of {line}'s blood",
            PropheticPredicate.FaithSpread =>
                $"when the faith is spoken in every valley from {place} outward",
            PropheticPredicate.BearerSurvives =>
                $"when one who carries the mark has outlived everyone who saw it given",
            PropheticPredicate.LedgerThreshold =>
                "when enough has been drawn that the drawing itself can be felt",
            PropheticPredicate.YearReached =>
                $"in the year the {rng.Pick(new[] { "long", "cold", "quiet", "seventh" })} count ends",
            PropheticPredicate.ArtifactRecovered =>
                $"when what was lost at {place} is held in a living hand",
            _ => $"when the last of {line} is put in the ground",
        };

        // Scaled to the ceiling, but three of each: a world's prophecies all ending in the same
        // clause reads as one sentence with the nouns swapped, which is what a first pass produced.
        string[] consequences = myth.Ceiling switch
        {
            MagicCeiling.World =>
                ["the world will not be the shape it was",
                 "the sea will stand where the road was",
                 "what sleeps under the map will turn over"],
            MagicCeiling.Realm =>
                ["a realm will change hands without a battle",
                 "three crowns will be worn by one head, briefly",
                 "the border will move and no army will have moved it"],
            MagicCeiling.Court =>
                ["a court will learn what it has been sitting beside",
                 "a hall will empty in a single evening",
                 "an heir will be named who was not born to it"],
            _ =>
                ["one man will know what he has been carrying",
                 "a life will run longer than it was given",
                 "someone will be recognised who was never introduced"],
        };

        string consequence = rng.Pick(consequences);

        // False prophecies are not written differently. That is the point — if the prose gave them
        // away, the player would learn to read past them and the whole device would be dead.
        _ = canFire;

        return $"{char.ToUpperInvariant(body[0])}{body[1..]}, {consequence}.";
    }

    private static string Note(PropheticPredicate predicate, bool canFire, Rng rng)
    {
        if (!canFire)
            return predicate switch
            {
                PropheticPredicate.TitleHeldByLine =>
                    "unsatisfiable: the line named died out before the bookmark",
                PropheticPredicate.FaithSpread =>
                    "unsatisfiable: the county count named exceeds the faith's reachable range",
                PropheticPredicate.BearerSurvives =>
                    "unsatisfiable: the mark named was never assigned to anyone",
                PropheticPredicate.ArtifactRecovered =>
                    "unsatisfiable: the object named was never placed in the world",
                PropheticPredicate.LineExtinct =>
                    "unsatisfiable: the line named has no members to lose",
                _ => "unsatisfiable by construction",
            };

        return predicate switch
        {
            PropheticPredicate.TitleHeldByLine => "checks holder dynasty against a seeded title",
            PropheticPredicate.FaithSpread => $"checks county count >= {rng.Int(12, 40)}",
            PropheticPredicate.BearerSurvives => "checks for a living trait bearer past a date",
            PropheticPredicate.LedgerThreshold => "checks the global ledger variable",
            PropheticPredicate.YearReached => "checks the calendar and nothing else",
            PropheticPredicate.ArtifactRecovered => "checks that a seeded artifact has an owner",
            _ => "checks that a seeded dynasty has no living members",
        };
    }

    private static string Consequence(Cosmology myth, Rng rng)
    {
        var options = new List<(string, double)>
        {
            ("a legend enters the world, and everyone who hears it gains from it", 1.0),
            ("the object named surfaces in a court that did not have it", 1.0),
            ("a claimant appears with a claim nobody can trace", 1.2),
            ("the price rule reverses for a generation: the practice costs nothing, and then costs everything", 0.8),
        };

        if (myth.Ceiling >= MagicCeiling.Realm)
            options.Add(("something arrives from outside the map's politics and takes ground", 1.4));

        if (myth.Ceiling == MagicCeiling.World)
            options.Add(("the world clock moves: the situation the world is living under changes phase", 1.6));

        return Weighted.Pick(rng, options);
    }
}
