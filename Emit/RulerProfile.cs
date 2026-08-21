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

    /// <summary>
    /// Exactly 3 core personality traits (e.g. brave, greedy, just). Dictates AI behavior,
    /// stress impacts, and diplomatic opinion.
    /// </summary>
    public required IReadOnlyList<string> PersonalityTraits { get; init; }

    /// <summary>
    /// Non-personality traits: congenitals, commander traits, lifestyle masteries, scars, and coping habits.
    /// </summary>
    public required IReadOnlyList<string> OtherTraits { get; init; }

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

    private static readonly HashSet<string> LegitimacyGovernments =
    [
        GovernmentMap.Feudal, GovernmentMap.Clan, GovernmentMap.Tribal,
        GovernmentMap.Administrative, GovernmentMap.Nomad,
    ];

    // Mutually exclusive opposing personality pairs in CK3
    private static readonly (string Positive, string Negative)[] PersonalityOpposites =
    [
        ("brave", "craven"),
        ("calm", "wrathful"),
        ("temperate", "gluttonous"),
        ("generous", "greedy"),
        ("just", "arbitrary"),
        ("diligent", "lazy"),
        ("compassionate", "callous"),
        ("honest", "deceitful"),
        ("forgiving", "vengeful"),
        ("humble", "arrogant"),
        ("trusting", "paranoid"),
        ("ambitious", "content"),
        ("stubborn", "fickle"),
        ("shy", "gregarious"),
        ("zealous", "cynical"),
        ("chaste", "lustful"),
        ("patient", "impatient"),
    ];

    public static RulerProfile Build(
        Title county, string tier, string government, string ethos, int age, bool hasVassals)
    {
        var rng = new Rng(county.Index ^ 0x6F13);
        int rank = tier switch { "e" => 4, "k" => 3, "d" => 2, _ => 1 };

        string lifestyle = PickLifestyle(rng, government, ethos, null);
        int level = PickEducationLevel(rng, rank);

        int points = Math.Max(0, age - 16) / 6;
        if (level >= 3) points++;
        if (level >= 4) points++;
        points += rank switch { 4 => 3, 3 => 2, 2 => 1, _ => 0 };
        points = Math.Clamp(points, 1, 9);

        string? second = null;
        int secondPoints = 0;
        if (rank >= 3 && age >= 36)
        {
            second = PickLifestyle(rng, government, ethos, lifestyle);
            secondPoints = age >= 48 ? 2 : 1;
        }

        var skills = RollSkills(rng, rank, lifestyle);
        int prowess = RollProwess(rng, rank, government, lifestyle);
        string? nickname = PickNickname(rng, rank, lifestyle, level);

        var personality = RollPersonality(rng, lifestyle, ethos, government, nickname);
        var otherTraits = RollOtherTraits(rng, rank, age, prowess, lifestyle, government, level);

        return new RulerProfile
        {
            Lifestyle = lifestyle,
            EducationLevel = level,
            EducationTrait = $"education_{lifestyle}_{level}",
            PersonalityTraits = personality,
            OtherTraits = otherTraits,
            Age = age,
            PerkPoints = points,
            SecondLifestyle = second,
            SecondPerkPoints = secondPoints,
            Diplomacy = skills[DiplomacyLifestyle],
            Martial = skills[MartialLifestyle],
            Stewardship = skills[StewardshipLifestyle],
            Intrigue = skills[IntrigueLifestyle],
            Learning = skills[LearningLifestyle],
            Prowess = prowess,
            Nickname = nickname,
            Dread = RollDread(rng, rank, government, hasVassals),
            Legitimacy = PickLegitimacy(rank, government, hasVassals),
            StabilityYears = rank switch { 4 => 6, 3 => 5, _ => 3 },
        };
    }

    /// <summary>
    /// Selects exactly 3 personality traits that do not conflict, weighted by lifestyle,
    /// ethos, government, and nickname.
    /// </summary>
    private static List<string> RollPersonality(
        Rng rng, string lifestyle, string ethos, string government, string? nickname)
    {
        var picked = new List<string>();
        var excluded = new HashSet<string>();

        void AddTrait(string trait)
        {
            if (picked.Contains(trait) || excluded.Contains(trait)) return;
            picked.Add(trait);
            excluded.Add(trait);

            foreach (var (a, b) in PersonalityOpposites)
            {
                if (a == trait) excluded.Add(b);
                if (b == trait) excluded.Add(a);
            }
            if (trait == "callous") excluded.Add("sadistic");
            if (trait == "sadistic") { excluded.Add("compassionate"); excluded.Add("callous"); }
        }

        // 1. Nickname seed trait
        if (nickname is not null)
        {
            string? seed = nickname switch
            {
                "nick_the_bold" or "nick_the_lionheart" or "nick_the_hammer" or "nick_the_ironside" => "brave",
                "nick_the_just" or "nick_the_lawgiver" => "just",
                "nick_the_good" or "nick_the_fair" => "compassionate",
                "nick_the_generous" or "nick_the_magnificent" => "generous",
                "nick_the_fox" or "nick_the_spider" or "nick_the_shrewd" => "deceitful",
                "nick_the_pious" => "zealous",
                "nick_the_wise" or "nick_the_scholar" => "patient",
                "nick_the_builder" => "diligent",
                _ => null
            };
            if (seed is not null) AddTrait(seed);
        }

        // 2. Personality pairs weighting
        var candidates = PersonalityOpposites.ToList();
        rng.Shuffle(candidates);

        foreach (var (traitA, traitB) in candidates)
        {
            if (picked.Count >= 3) break;
            if (excluded.Contains(traitA) || excluded.Contains(traitB)) continue;

            int weightA = 10;
            int weightB = 10;

            // Lifestyle bias
            switch (lifestyle)
            {
                case MartialLifestyle:
                    if (traitA is "brave" or "stubborn") weightA += 18;
                    if (traitB is "wrathful" or "arrogant") weightB += 15;
                    break;
                case IntrigueLifestyle:
                    if (traitB is "deceitful" or "paranoid" or "cynical") weightB += 20;
                    if (traitA is "ambitious") weightA += 15;
                    break;
                case StewardshipLifestyle:
                    if (traitA is "diligent" or "temperate" or "just") weightA += 18;
                    if (traitB is "greedy") weightB += 12;
                    break;
                case DiplomacyLifestyle:
                    if (traitA is "generous" or "compassionate" or "trusting") weightA += 18;
                    if (traitB is "gregarious") weightB += 15;
                    break;
                case LearningLifestyle:
                    if (traitA is "patient" or "temperate" or "humble") weightA += 18;
                    if (traitA is "zealous") weightA += (government == GovernmentMap.Theocracy ? 25 : 10);
                    break;
            }

            // Ethos bias
            switch (ethos)
            {
                case "ethos_bellicose":
                    if (traitA is "brave") weightA += 20;
                    if (traitB is "wrathful" or "callous") weightB += 15;
                    break;
                case "ethos_courtly":
                    if (traitB is "arrogant" or "gregarious") weightB += 15;
                    if (traitA is "generous") weightA += 10;
                    break;
                case "ethos_bureaucratic":
                    if (traitA is "diligent" or "just" or "patient") weightA += 18;
                    break;
                case "ethos_spiritual":
                    if (traitA is "zealous" or "temperate" or "humble") weightA += 20;
                    break;
                case "ethos_communal":
                    if (traitA is "compassionate" or "generous" or "trusting") weightA += 16;
                    break;
                case "ethos_stoic":
                    if (traitA is "calm" or "patient" or "stubborn") weightA += 15;
                    break;
            }

            int roll = rng.Int(1, weightA + weightB);
            AddTrait(roll <= weightA ? traitA : traitB);
        }

        return picked;
    }

    /// <summary>
    /// Generates non-personality traits: congenitals, commander traits, lifestyle masteries,
    /// scars, and coping habits based on rank, prowess, age, and lifestyle.
    /// </summary>
    private static List<string> RollOtherTraits(
        Rng rng, int rank, int age, int prowess, string lifestyle, string government, int educationLevel)
    {
        var list = new List<string>();

        // Every name below exists as a top-level key in vanilla's common/traits/00_traits.txt —
        // this list previously carried four that did not (`scarred_1` for `scarred`, `lisp` for
        // `lisping`, `poet` for `lifestyle_poet`, and `cavalry_leader`, which is a CK2 trait with
        // no CK3 counterpart at all). A missing trait is not an error the game surfaces at the
        // character: the line is silently dropped, so ~40 rulers per map were simply less scarred
        // and less interesting than the rolls intended, and tiger reported each one.

        // 1. Congenital / genetic traits. Blessings are rarer than quirks, matching the shape of
        // vanilla's own birth weights, and the two rolls are independent so a ruler can be a
        // beautiful stutterer — vanilla's favourite kind of character.
        if (rng.Chance(0.07))
        {
            string[] blessings =
            [
                "beauty_good_1", "beauty_good_2", "intellect_good_1", "intellect_good_2",
                "physique_good_1", "physique_good_2", "fecund", "shrewd", "strong", "pure_blooded"
            ];
            list.Add(rng.Pick(blessings));
        }
        if (rng.Chance(0.08))
        {
            string[] quirks =
            [
                "giant", "dwarf", "clubfooted", "lisping", "stuttering", "spindly",
                "hunchbacked", "wheezing", "bleeder", "albino"
            ];
            list.Add(rng.Pick(quirks));
        }

        // 2. Commander traits (martial rulers, nomads, tribes, high rank). A seasoned great lord
        // can carry two — a doctrine and a knack — which is how vanilla's famous commanders read.
        if (lifestyle == MartialLifestyle || government is GovernmentMap.Nomad or GovernmentMap.Tribal)
        {
            double commanderChance = (rank >= 3 ? 0.60 : 0.35) + (age >= 30 ? 0.15 : 0.0);
            if (rng.Chance(commanderChance))
            {
                // The nomad pool leans open-steppe and mobility; there is no cavalry trait in CK3
                // to reach for, open_terrain_expert is the horse-lord knack it actually ships.
                string[] commanderPool = government == GovernmentMap.Nomad
                    ? ["open_terrain_expert", "organizer", "aggressive_attacker", "flexible_leader",
                       "winter_soldier", "reckless"]
                    : ["rough_terrain_expert", "forest_fighter", "unyielding_defender",
                       "aggressive_attacker", "organizer", "flexible_leader", "military_engineer",
                       "logistician", "cautious_leader", "gallant"];

                string first = rng.Pick(commanderPool);
                list.Add(first);

                if (rank >= 3 && age >= 35 && rng.Chance(0.25))
                {
                    string second = rng.Pick(commanderPool);
                    if (second != first) list.Add(second);
                }
            }
        }

        // 3. Lifestyle / hobby traits (mature rulers who excelled in schooling). Intrigue rulers
        // used to fall through to nothing here; schemer-by-night is exactly their hobby.
        if (age >= 35 && educationLevel >= 3 && rng.Chance(0.45))
        {
            string? hobby = lifestyle switch
            {
                MartialLifestyle => rng.Pick(["lifestyle_hunter", "lifestyle_blademaster"]),
                LearningLifestyle => rng.Pick(["lifestyle_mystic", "lifestyle_herbalist", "lifestyle_physician"]),
                DiplomacyLifestyle => rng.Pick(["lifestyle_reveler", "lifestyle_poet", "lifestyle_traveler"]),
                StewardshipLifestyle => rng.Pick(["lifestyle_gardener", "lifestyle_surveyor"]),
                IntrigueLifestyle => rng.Pick(["schemer", "schemer", "torturer"]),
                _ => null,
            };
            if (hobby is not null) list.Add(hobby);
        }

        // 4. Physical wear and scars (veterans and older rulers). `scarred` is a leveled trait in
        // CK3 — history grants level 1 by the bare name. The grimmer wounds stay rare and stack
        // with the scar rather than replacing it: a one-eyed man is usually scarred too.
        if (prowess >= 8 && age >= 28 && rng.Chance(0.30))
        {
            list.Add("scarred");
            if (age >= 45 && rank >= 3 && rng.Chance(0.08))
            {
                list.Add("one_eyed");
            }
            else if (age >= 40 && rng.Chance(0.05))
            {
                list.Add(rng.Pick(["one_legged", "maimed", "disfigured"]));
            }
        }

        // 5. Stress coping (older rulers). The rulership has cost them something; how they pay
        // varies with what they spend their days on.
        if (age >= 45 && rng.Chance(0.22))
        {
            string[] coping = lifestyle == LearningLifestyle
                ? ["journaller", "confider", "flagellant", "contrite", "comfort_eater"]
                : ["drunkard", "hashishiyah", "comfort_eater", "irritable", "confider", "inappetetic"];
            list.Add(rng.Pick(coping));
        }

        return list;
    }

    private static string PickLifestyle(Rng rng, string government, string ethos, string? exclude)
    {
        int[] w = government switch
        {
            GovernmentMap.Tribal => [16, 42, 14, 16, 12],
            GovernmentMap.Nomad => [14, 44, 12, 20, 10],
            GovernmentMap.Clan => [22, 34, 16, 16, 12],
            GovernmentMap.Administrative => [26, 18, 30, 14, 12],
            GovernmentMap.Republic => [24, 12, 38, 16, 10],
            GovernmentMap.Theocracy => [20, 8, 18, 12, 42],
            _ => [25, 30, 20, 12, 13],
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

        if (rank == 4 && level >= 4)
        {
            pool = lifestyle == MartialLifestyle
                ? [.. pool, "nick_the_great", "nick_the_conqueror"]
                : [.. pool, "nick_the_great"];
        }

        return rng.Pick(pool);
    }

    private static int RollDread(Rng rng, int rank, string government, bool hasVassals)
    {
        if (!hasVassals) return 0;
        if (government == GovernmentMap.Nomad) return rng.Int(15, 30);

        return rank >= 3 ? rng.Int(8, 16) : 0;
    }

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