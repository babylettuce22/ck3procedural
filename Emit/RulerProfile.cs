using Ck3MapGen.Core;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// The man behind the title: how he was brought up, what that upbringing made him good at, how much
/// of a lifestyle he has had time to learn, and what standing he carries into a court that has
/// never had to obey him before.
///
/// Split out of <see cref="HistoryWriter"/> because none of it is about writing files — it is the
/// one place where a ruler's tier, government, culture and age are turned into numbers, so the
/// balance of the starting world can be read and changed on a single screen rather than chased
/// through string concatenation.
///
/// Everything here is drawn from a stream of its own (<c>county.Index ^ 0x6F13</c>). The character
/// writer's own stream already feeds gold, prestige and dread in a fixed order, and reaching into
/// it here would shift every one of those draws.
/// </summary>
public sealed record RulerProfile
{
    /// <summary>
    /// diplomacy | martial | stewardship | intrigue | learning — the lifestyle the education trait
    /// belongs to, and so the tree the perk points are spendable in.
    /// </summary>
    public required string Lifestyle { get; init; }

    public required int EducationLevel { get; init; }
    public required string EducationTrait { get; init; }

    /// <summary>Age in whole years at the start date.</summary>
    public required int Age { get; init; }

    public required int PerkPoints { get; init; }

    /// <summary>A second tree a long-reigning great lord has dabbled in, or null for most rulers.</summary>
    public string? SecondLifestyle { get; init; }
    public int SecondPerkPoints { get; init; }

    public required int Diplomacy { get; init; }
    public required int Martial { get; init; }
    public required int Stewardship { get; init; }
    public required int Intrigue { get; init; }
    public required int Learning { get; init; }
    public required int Prowess { get; init; }

    /// <summary>A vanilla nickname key, or null. Only high tiers, and not all of them.</summary>
    public string? Nickname { get; init; }

    public int Dread { get; init; }

    /// <summary>
    /// A vanilla legitimacy script value (<c>legitimacy_level_2</c>, ...), or null for a ruler whose
    /// government has no legitimacy to gain.
    /// </summary>
    public string? Legitimacy { get; init; }

    /// <summary>How long the early-reign stability modifier should run.</summary>
    public required int StabilityYears { get; init; }

    public const string DiplomacyLifestyle = "diplomacy";
    public const string MartialLifestyle = "martial";
    public const string StewardshipLifestyle = "stewardship";
    public const string IntrigueLifestyle = "intrigue";
    public const string LearningLifestyle = "learning";

    private static readonly string[] Lifestyles =
    [
        DiplomacyLifestyle, MartialLifestyle, StewardshipLifestyle,
        IntrigueLifestyle, LearningLifestyle,
    ];

    /// <summary>
    /// The governments that grant legitimacy at all, read off `legitimacy = yes` in vanilla's
    /// common/governments/00_government_types.txt. Republics and theocracies are absent there, and
    /// adding legitimacy to one of them is an effect against a currency it does not have.
    /// </summary>
    private static readonly HashSet<string> LegitimacyGovernments =
    [
        GovernmentMap.Feudal, GovernmentMap.Clan, GovernmentMap.Tribal,
        GovernmentMap.Administrative, GovernmentMap.Nomad,
    ];

    public static RulerProfile Build(
        Title county, string tier, string government, string ethos, int age, bool hasVassals)
    {
        var rng = new Rng(county.Index ^ 0x6F13);
        int rank = tier switch { "e" => 4, "k" => 3, "d" => 2, _ => 1 };

        string lifestyle = PickLifestyle(rng, government, ethos, null);
        int level = PickEducationLevel(rng, rank);

        // Perk points are what makes the education mechanical rather than decorative: they are spent
        // in the tree the education belongs to, so a ruler's schooling and the perks he has already
        // taken tell the same story.
        //
        // Roughly one point per six years of adult life — deliberately below what a played character
        // earns, since an AI ruler spends decades not concentrating on anything in particular. That
        // puts a 24-year-old count on one point and a 50-year-old on five. A strong education
        // multiplies lifestyle xp gain (0.3 at level 3, 0.4 at level 4), so the well-taught arrive
        // faster; a great title carries the court, the tutors and the leisure to use them.
        int points = Math.Max(0, age - 16) / 6;
        if (level >= 3) points++;
        if (level >= 4) points++;
        points += rank switch { 4 => 3, 3 => 2, 2 => 1, _ => 0 };
        points = Math.Clamp(points, 1, 9);

        // Only the great lords have had both the years and the court to look outside their own tree.
        string? second = null;
        int secondPoints = 0;
        if (rank >= 3 && age >= 36)
        {
            second = PickLifestyle(rng, government, ethos, lifestyle);
            secondPoints = age >= 48 ? 2 : 1;
        }

        var skills = RollSkills(rng, rank, lifestyle);

        return new RulerProfile
        {
            Lifestyle = lifestyle,
            EducationLevel = level,
            EducationTrait = $"education_{lifestyle}_{level}",
            Age = age,
            PerkPoints = points,
            SecondLifestyle = second,
            SecondPerkPoints = secondPoints,
            Diplomacy = skills[DiplomacyLifestyle],
            Martial = skills[MartialLifestyle],
            Stewardship = skills[StewardshipLifestyle],
            Intrigue = skills[IntrigueLifestyle],
            Learning = skills[LearningLifestyle],
            Prowess = RollProwess(rng, rank, government, lifestyle),
            Nickname = PickNickname(rng, rank, lifestyle, level),
            Dread = RollDread(rng, rank, government, hasVassals),
            Legitimacy = PickLegitimacy(rank, government, hasVassals),
            StabilityYears = rank switch { 4 => 6, 3 => 5, _ => 3 },
        };
    }

    /// <summary>
    /// What a ruler was taught, as a weighted draw over the five lifestyles.
    ///
    /// The government sets the base — a khan's household raises soldiers, a merchant republic raises
    /// accountants — and the culture's ethos leans it further, so a bellicose culture's dukes are
    /// visibly a different breed from a spiritual one's. Intrigue takes no ethos lean of its own on
    /// purpose: it is what is left when a court raises neither a warrior nor a bureaucrat, and that
    /// is the right shape for it.
    /// </summary>
    private static string PickLifestyle(Rng rng, string government, string ethos, string? exclude)
    {
        // diplomacy, martial, stewardship, intrigue, learning
        int[] w = government switch
        {
            GovernmentMap.Tribal => [16, 42, 14, 16, 12],
            GovernmentMap.Nomad => [14, 44, 12, 20, 10],
            GovernmentMap.Clan => [22, 34, 16, 16, 12],
            GovernmentMap.Administrative => [26, 18, 30, 14, 12],
            GovernmentMap.Republic => [24, 12, 38, 16, 10],
            GovernmentMap.Theocracy => [20, 8, 18, 12, 42],
            _ => [25, 30, 20, 12, 13], // feudal
        };

        switch (ethos)
        {
            case "ethos_bellicose": w[1] += 25; break;
            case "ethos_courtly": w[0] += 25; break;
            case "ethos_bureaucratic": w[2] += 22; w[4] += 8; break;
            case "ethos_communal": w[2] += 15; w[0] += 8; break;
            case "ethos_spiritual": w[4] += 25; break;
            case "ethos_egalitarian": w[0] += 15; w[4] += 8; break;
            case "ethos_stoic": w[4] += 10; w[2] += 8; break;
        }

        if (exclude is not null)
        {
            int i = Array.IndexOf(Lifestyles, exclude);
            if (i >= 0) w[i] = 0;
        }

        return Lifestyles[PickIndex(rng, w)];
    }

    /// <summary>
    /// How well it took. A count is an ordinary man given whatever tutor his father could find; an
    /// emperor was raised knowing what he was being raised for, by the best teacher in the realm.
    ///
    /// Level 5 is vanilla's <c>random_creation_weight = 0</c> education — the game never rolls it
    /// for anybody — so it stays a once-a-map thing: a few percent of emperors, a fraction of kings.
    /// </summary>
    private static int PickEducationLevel(Rng rng, int rank)
    {
        int[] w = rank switch
        {
            4 => [0, 14, 42, 40, 4],
            3 => [5, 25, 45, 24, 1],
            2 => [14, 38, 36, 12, 0],
            _ => [30, 42, 23, 5, 0],
        };

        return PickIndex(rng, w) + 1;
    }

    /// <summary>
    /// Base skills, before the education trait adds its own (+2 at level 1, rising to +10 at level
    /// 5). Kept deliberately modest for exactly that reason: a level 4 education is already +8 by
    /// itself, and a generous base stacked under it produces numbers no vanilla character has.
    /// </summary>
    private static Dictionary<string, int> RollSkills(Rng rng, int rank, string lifestyle)
    {
        var (min, max) = rank switch
        {
            4 => (6, 10),
            3 => (5, 9),
            2 => (4, 7),
            _ => (3, 6),
        };

        var skills = new Dictionary<string, int>();
        foreach (string key in Lifestyles) skills[key] = rng.Int(min, max);

        skills[lifestyle] += rank >= 3 ? rng.Int(2, 4) : rng.Int(1, 3);
        return skills;
    }

    /// <summary>
    /// Prowess. Rank buys the armour, the horse and someone to teach him to use both; a martial
    /// education and a culture that settles things personally do the rest.
    /// </summary>
    private static int RollProwess(Rng rng, int rank, string government, string lifestyle)
    {
        int prowess = rank switch
        {
            4 => rng.Int(7, 12),
            3 => rng.Int(6, 11),
            2 => rng.Int(5, 9),
            _ => rng.Int(4, 8),
        };

        if (lifestyle == MartialLifestyle) prowess += rng.Int(1, 3);
        if (government is GovernmentMap.Tribal or GovernmentMap.Nomad or GovernmentMap.Clan)
            prowess += rng.Int(1, 2);

        return prowess;
    }

    /// <summary>
    /// A byname, for kings and emperors only and not for all of them — one that every great lord
    /// carries is not a distinction, it is a suffix.
    ///
    /// Only keys from vanilla's base common/nicknames/00_nicknames.txt, which this generator never
    /// blanks and which needs no localisation of ours.
    /// </summary>
    private static string? PickNickname(Rng rng, int rank, string lifestyle, int level)
    {
        if (rank < 3) return null;

        double chance = rank == 4 ? 0.65 : 0.40;
        if (level >= 4) chance += 0.15;
        if (!rng.Chance(chance)) return null;

        string[] pool = lifestyle switch
        {
            MartialLifestyle => ["nick_the_bold", "nick_the_strong", "nick_the_lionheart",
                                 "nick_the_victorious", "nick_the_hammer", "nick_the_ironside"],
            DiplomacyLifestyle => ["nick_the_fair", "nick_the_good", "nick_the_just",
                                   "nick_the_generous", "nick_the_magnificent"],
            StewardshipLifestyle => ["nick_the_builder", "nick_the_lawgiver",
                                     "nick_the_magnificent", "nick_the_just"],
            IntrigueLifestyle => ["nick_the_fox", "nick_the_spider", "nick_the_shrewd"],
            _ => ["nick_the_wise", "nick_the_pious", "nick_the_scholar"],
        };

        // The bynames that judge a whole reign rather than a single talent, kept for the emperors
        // who were also taught well.
        if (rank == 4 && level >= 4)
        {
            pool = lifestyle == MartialLifestyle
                ? [.. pool, "nick_the_great", "nick_the_conqueror"]
                : [.. pool, "nick_the_great"];
        }

        return rng.Pick(pool);
    }

    /// <summary>
    /// Dread. A khan with vassals has always had it here — without it obedience starts 5 points down
    /// before anything else is counted — and the same argument holds for any king or emperor whose
    /// court has to be kept in line from the first day.
    /// </summary>
    private static int RollDread(Rng rng, int rank, string government, bool hasVassals)
    {
        if (!hasVassals) return 0;
        if (government == GovernmentMap.Nomad) return rng.Int(15, 30);

        return rank >= 3 ? rng.Int(8, 16) : 0;
    }

    /// <summary>
    /// Legitimacy, the other half of that same obedience sum: a subject docks an overlord 15 for not
    /// having reached level 3, which is a good part of why a freshly generated realm sheds its
    /// vassals inside a decade.
    /// </summary>
    private static string? PickLegitimacy(int rank, string government, bool hasVassals)
    {
        if (!hasVassals || !LegitimacyGovernments.Contains(government)) return null;
        if (government == GovernmentMap.Nomad) return "legitimacy_level_3";

        return rank switch
        {
            4 => "legitimacy_level_3",
            3 => "legitimacy_level_2",
            _ => null,
        };
    }

    private static int PickIndex(Rng rng, int[] weights)
    {
        int total = 0;
        foreach (int w in weights) total += w;
        if (total <= 0) return 0;

        int roll = rng.Int(1, total);
        for (int i = 0; i < weights.Length; i++)
        {
            roll -= weights[i];
            if (roll <= 0) return i;
        }

        return weights.Length - 1;
    }
}
