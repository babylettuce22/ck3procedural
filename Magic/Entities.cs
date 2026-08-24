using Ck3MapGen.Core;

namespace Ck3MapGen.Magic;

/// <summary>
/// How an entity treats the people who deal with it. Decides what its boons cost and how its
/// wrath arrives, which is the whole of its mechanical existence.
/// </summary>
public enum EntityTemperament
{
    /// <summary>Keeps to the letter of the bargain, and to the letter of it only.</summary>
    Exacting,

    /// <summary>Wants more each time. The obligation grows whether or not you ask for anything.</summary>
    Hungry,

    /// <summary>Answers or does not, for reasons that are not about you.</summary>
    Capricious,

    /// <summary>Waits. Asks for nothing for a very long time, and then asks once.</summary>
    Patient,

    /// <summary>Damaged, and leaking. Grants more than it means to and cannot be relied on.</summary>
    Wounded,
}

/// <summary>
/// Something that grants power and expects something back.
///
/// Only exists under <see cref="MagicSource.Entities"/>, and that is the point of having the axis:
/// an entity roster gives the world actors who are not characters, which is a category CK3 has no
/// native concept of and which changes what a "relationship" can mean. Under any other source this
/// list is empty and the world is poorer in a specific, deliberate way.
/// </summary>
public sealed record Entity(
    string Key,
    string Name,
    MagicDomain Sphere,
    EntityTemperament Temperament,
    string Price,
    IReadOnlyList<string> Boons,
    IReadOnlyList<string> Wraths)
{
    public string ScriptHint =>
        "a generated deity on one of the world's faiths, plus a story cycle per pact carrying "
        + "the obligation, plus a favour variable the wrath thresholds read";
}

/// <summary>Builds the roster. Three to five: enough to choose between, few enough to remember.</summary>
public static class EntityRoster
{
    public static IReadOnlyList<Entity> Build(Cosmology myth, Lexicon lexicon, Rng rng)
    {
        if (myth.Source != MagicSource.Entities) return [];

        int count = rng.Int(3, 5);
        var spheres = myth.Domains.Ranked.Select(r => r.Domain).ToList();
        var entities = new List<Entity>();

        for (int i = 0; i < count; i++)
        {
            // Spheres come off the world's own domain emphasis, and repeat only once it runs out,
            // so a world that forbade Death has no death-entity to bargain with — the prohibition
            // reaches the pantheon rather than stopping at the spell list.
            var sphere = spheres[i % spheres.Count];
            var temperament = Weighted.Pick(rng,
            [
                (EntityTemperament.Exacting, 1.2),
                (EntityTemperament.Hungry, 1.0),
                (EntityTemperament.Capricious, myth.Reliability == MagicReliability.Capricious ? 1.8 : 0.8),
                (EntityTemperament.Patient, 1.0),
                (EntityTemperament.Wounded, myth.Source == MagicSource.Wound ? 1.5 : 0.6),
            ]);

            entities.Add(new Entity(
                Key: $"gen_entity_{i}",
                Name: lexicon.EntityName(sphere),
                Sphere: sphere,
                Temperament: temperament,
                Price: PriceOf(temperament, myth, rng),
                Boons: BoonsFor(sphere),
                Wraths: WrathsFor(myth, temperament)));
        }

        return entities;
    }

    private static string PriceOf(EntityTemperament temperament, Cosmology myth, Rng rng) => temperament switch
    {
        EntityTemperament.Exacting =>
            "a stated term, honoured exactly; break it and the grant reverses in a day",
        EntityTemperament.Hungry =>
            "a share that grows: each grant raises what the next one costs, without a ceiling",
        EntityTemperament.Capricious =>
            "nothing agreed in advance, which is worse — it collects when it feels owed",
        EntityTemperament.Patient => rng.Chance(0.5)
            ? "one favour, unspecified, called in a lifetime later"
            : "an heir, promised now and collected when there is one",
        _ =>
            $"nothing it can enforce — it is too damaged — but what it grants arrives "
            + $"{(myth.Reliability == MagicReliability.Deterministic ? "wrong" : "unpredictably wrong")}",
    };

    private static IReadOnlyList<string> BoonsFor(MagicDomain sphere) => sphere switch
    {
        MagicDomain.Life => ["an heir where there was none", "a sickness lifted", "years added"],
        MagicDomain.Death => ["a rival's health", "soldiers who do not tire", "a death foretold"],
        MagicDomain.War => ["a battle already decided", "a wall that holds", "fear in the enemy"],
        MagicDomain.Mind => ["what someone is hiding", "a mind turned", "a court that agrees"],
        MagicDomain.Nature => ["a harvest out of season", "weather to order", "roads that stay open"],
        MagicDomain.Fate => ["a succession settled", "a warning", "a thread cut"],
        _ => ["a thing made that should not exist", "gold from nothing", "a seat raised in a season"],
    };

    private static IReadOnlyList<string> WrathsFor(Cosmology myth, EntityTemperament temperament)
    {
        var wraths = new List<string>();

        wraths.Add(myth.Price switch
        {
            MagicPrice.Corruption => "it takes the shape it was given, and the shape stays",
            MagicPrice.Taint => "it collects from the children instead",
            MagicPrice.Depletion => "it takes it out of the ground the caster holds",
            MagicPrice.Attention => "it arrives in person, which is the worst of these",
            MagicPrice.Stigma => "it tells someone",
            MagicPrice.Instability => "it stops holding whatever it had been holding",
            _ => "it lets go mid-grant",
        });

        if (temperament == EntityTemperament.Hungry)
            wraths.Add("the asking price rises whether or not anything was granted");

        if (temperament == EntityTemperament.Patient)
            wraths.Add("nothing at all, for thirty years, and then everything at once");

        return wraths;
    }
}
