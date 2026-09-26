using Ck3MapGen.Core;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// The <c>_desc</c> prose for generated religions and faiths, in the register vanilla uses: what
/// is worshipped, how, and what the faith is known for. Built from what the religion already has —
/// its gods' names, its clergy and scripture words, its doctrines, its faiths' tenets and holy
/// sites — so the text agrees with everything else the game shows about it.
///
/// Each text draws from its own Rng seeded off the key, not the generation stream, so adding or
/// rewording a template never moves anything else on the map, and re-running the localisation
/// after an editor rename gives the same prose with the new names.
/// </summary>
public static class FaithDescriptions
{
    public static string For(Religion religion)
    {
        if (religion.Key == Faiths.UnsettledReligionKey)
            return "Not a faith so much as the absence of one. The scattered peoples of the wild " +
                   "places keep their own small gods and answer to no temple, priest or scripture.";

        var rng = new Rng(Rng.StableHash(religion.Key));
        var w = new Words(religion);
        string form = FormOf(religion);

        var parts = new List<string?>
        {
            Opening(form, w, rng),
            Practice(form, w, rng),
            Afterlife(w, rng),
            DoctrineNote(religion, w, rng),
        };

        return string.Join(' ', parts.Where(p => p is not null));
    }

    public static string For(Faith faith)
    {
        if (faith.Key == Faiths.UnsettledFaithKey)
            return "The loose customs of those who live beyond the reach of any settled faith.";

        var rng = new Rng(Rng.StableHash(faith.Key));
        var parts = new List<string?> { Standing(faith, rng), Tenets(faith, rng), Sites(faith, rng) };
        return string.Join(' ', parts.Where(p => p is not null));
    }

    // ---- Religion ----

    /// <summary>The Azgaar tradition when there is one, else what the theism doctrine says.</summary>
    private static string FormOf(Religion religion)
    {
        if (religion.SourceForm is { Length: > 0 } source) return source;

        string theism = religion.Doctrines.GetValueOrDefault("doctrine_theism", "");
        if (theism.Contains("dualis", StringComparison.Ordinal)) return "Dualism";
        return religion.Monotheist ? "Monotheism" : "Polytheism";
    }

    /// <summary>Forms that keep their teachings in memory rather than books.</summary>
    private static readonly HashSet<string> OralForms = new(StringComparer.OrdinalIgnoreCase)
    {
        "Animism", "Shamanism", "Totemism", "Nature Worship", "Ancestor Worship",
    };

    private static string Opening(string form, Words w, Rng rng)
    {
        string r = w.Religion, g = w.God;

        string[] options = form.ToLowerInvariant() switch
        {
            "monotheism" =>
            [
                $"{r} proclaims {g} the one true god, maker of all things and final judge of every soul.",
                $"To the followers of {r} there is no god but {g}, who shaped the world from nothing and will one day call it to account.",
                $"{r} teaches that {g} alone is divine, and that every other power mortals worship is a delusion or a devil.",
            ],
            "polytheism" =>
            [
                w.War is { } war && w.Fertility is { } fert
                    ? $"{r} honours a great pantheon under {g}, beside whom {war} rules the field of battle and {fert} the harvest."
                    : $"{r} honours a great pantheon, over which {g} presides.",
                $"The gods of {r} are many and quarrelsome, and chief among them is {g}.",
                $"Followers of {r} keep faith with a host of gods, each owed their due, though all bow before {g}.",
            ],
            "dualism" => w.Devil is { } devil
                ?
                [
                    $"{r} sees the world as the battleground of {g} and {devil}, light against darkness, and asks every soul to choose a side.",
                    $"In the teachings of {r}, creation is torn between {g} and {devil}, and neither will rest until the other is undone.",
                ]
                : [$"{r} sees the world as a battleground between light and darkness, with {g} the champion of the light."],
            "nature worship" =>
            [
                $"{r} finds the divine in the living world, in rivers, groves and mountains, and honours {g} as the spirit that runs through them all.",
                $"For the followers of {r}, the earth itself is holy, and {g} is its oldest and greatest power.",
            ],
            "animism" =>
            [
                $"For the followers of {r}, every stone, stream and beast has a spirit of its own, and {g} is only the greatest of them.",
                $"{r} holds that the world is crowded with spirits, to be bargained with, placated and, when need be, feared, and that {g} is foremost among them.",
            ],
            "shamanism" =>
            [
                $"{r} is a faith of visions and trances, in which the chosen walk between the world of the living and the world of spirits and bring back the will of {g}.",
            ],
            "ancestor worship" =>
            [
                $"{r} holds that the dead never truly leave, and that the ancestors watch over their descendants beside {g}.",
                $"In {r}, every family's dead are honoured as its guardians, and the greatest of all ancestors is {g}.",
            ],
            "totemism" =>
            [
                $"Each clan that follows {r} claims descent from a sacred beast, and all of them together honour {g}, the first and greatest of these.",
            ],
            "pantheism" =>
            [
                $"{r} teaches that {g} is not apart from the world but is the world itself, present in every living thing.",
            ],
            "non-theism" =>
            [
                $"{r} is less the worship of a god than a path of discipline, and even {g} is revered more as a teacher than as a ruler.",
            ],
            "cult" =>
            [
                $"{r} began as a closed circle of devotees gathered around {g}, and it still guards its mysteries jealously.",
            ],
            "dark cult" =>
            [
                $"{r} is spoken of in whispers. Its followers court {g} with rites that other faiths call abominations.",
            ],
            "sect" or "heresy" =>
            [
                $"{r} broke away from older teachings, and holds that it alone has understood {g} rightly.",
            ],
            _ =>
            [
                $"The followers of {r} honour {g} above all other powers.",
            ],
        };

        return rng.Pick(options);
    }

    private static string? Practice(string form, Words w, Rng rng)
    {
        if (w.Priests is not { } p || w.Text is not { } t) return null;
        string? h = w.Houses;

        if (OralForms.Contains(form))
        {
            return h is null
                ? $"Its holy folk, the {p}, keep no books; the sacred lore of the {t} is carried in memory and passed from teacher to student."
                : rng.Pick(new[]
                {
                    $"Its holy folk, the {p}, keep no books; the sacred lore of the {t} is carried in memory and recited at shrines called {h} on holy days.",
                    $"Worship is led by the {p}, who gather the faithful at sacred places called {h} and recite the lore of the {t} from memory.",
                });
        }

        return h is null
            ? $"Its priests, the {p}, guard the scriptures known as the {t}."
            : rng.Pick(new[]
            {
                $"Its priests, the {p}, lead worship in temples called {h} and guard the scriptures known as the {t}.",
                $"The {t} is its holy book, and the {p} who teach from it gather the faithful in their {h}.",
            });
    }

    private static string? Afterlife(Words w, Rng rng)
    {
        var options = new List<string>();
        if (w.Paradise is { } good && w.Hell is { } bad)
            options.Add($"The righteous are promised {good}, while the wicked are cast down into {bad}.");
        if (w.Realm is { } realm)
            options.Add($"Those who live well are believed to join {w.God} in {realm} when they die.");

        return options.Count > 0 ? rng.Pick(options) : null;
    }

    /// <summary>One line on whichever of the religion's doctrines is the most unusual.</summary>
    private static string? DoctrineNote(Religion religion, Words w, Rng rng)
    {
        var d = religion.Doctrines;
        var options = new List<string>();

        switch (d.GetValueOrDefault("doctrine_gender"))
        {
            case "doctrine_gender_female_dominated":
                options.Add("Its teachings place women above men in matters of rule and inheritance.");
                break;
            case "doctrine_gender_equal":
                options.Add("It draws no distinction between men and women in matters of faith or rule.");
                break;
        }

        switch (d.GetValueOrDefault("doctrine_pluralism"))
        {
            case "doctrine_pluralism_fundamentalist":
                options.Add($"It tolerates no other faith, and counts every unbeliever an enemy of {w.God}.");
                break;
            case "doctrine_pluralism_pluralistic":
                options.Add("It is famously tolerant, and regards other faiths as different paths to the same truth.");
                break;
        }

        if (d.GetValueOrDefault("doctrine_pilgrimage") == "doctrine_pilgrimage_mandatory")
            options.Add("Every believer is expected to make the pilgrimage to its holy sites at least once in their life.");

        if (religion.LayClergy)
            options.Add("It has no independent priesthood to speak of; its temples belong to the lords who build them.");

        return options.Count > 0 ? rng.Pick(options) : null;
    }

    // ---- Faith ----

    private static string Standing(Faith faith, Rng rng)
    {
        string f = faith.Name, r = faith.Religion.Name;
        int siblings = faith.Religion.Faiths.Count;

        string lead = siblings <= 1
            ? $"{f} is the sole tradition of {r}"
            : faith.IsDominant
                ? rng.Pick(new[] { $"{f} is the principal tradition of {r}", $"{f} is the oldest and largest branch of {r}" })
                : rng.Pick(new[] { $"{f} is one of the branches of {r}", $"{f} is a distinct tradition within {r}" });

        if (faith.Head is { } head && faith.IsOrganized)
        {
            return head.Seat is { Name.Length: > 0 } seat
                ? $"{lead}, and its faithful answer to {head.Name}, seated at {seat.Name}."
                : $"{lead}, and its faithful answer to {head.Name}.";
        }

        return faith.IsOrganized
            ? $"{lead}, with no single authority over its faithful."
            : $"{lead}. It has never been gathered into a formal church, and each community keeps the rites in its own way.";
    }

    private static string? Tenets(Faith faith, Rng rng)
    {
        var phrases = faith.Tenets.Select(TenetPhrase).ToList();
        if (phrases.Count == 0) return null;

        return rng.Pick(new[]
        {
            $"Its adherents are known for {Join(phrases)}.",
            $"It is set apart by {Join(phrases)}.",
        });
    }

    private static string? Sites(Faith faith, Rng rng)
    {
        var names = faith.HolySites.Select(s => s.County.Name).Where(n => n.Length > 0)
            .Distinct().Take(3).ToList();
        if (names.Count == 0) return null;

        if (names.Count == 1) return $"Its holiest place is {names[0]}.";

        return rng.Pick(new[]
        {
            $"Its holiest places include {Join(names)}.",
            $"Pilgrims travel to {Join(names)} to pray.",
        });
    }

    /// <summary>
    /// A tenet as a noun phrase. Anything not listed falls back to the game's own name for it, so
    /// a DLC or patch tenet still reads sensibly rather than being dropped.
    /// </summary>
    private static string TenetPhrase(string tenet)
    {
        if (TenetPhrases.TryGetValue(tenet, out string? phrase)) return phrase;
        if (tenet.EndsWith("_syncretism", StringComparison.Ordinal))
            return "blending its rites with those of an older faith";
        return $"the tenet of ${tenet}_name$";
    }

    private static readonly Dictionary<string, string> TenetPhrases = new(StringComparer.Ordinal)
    {
        ["tenet_adaptive"] = "readily adopting the customs of the lands it spreads to",
        ["tenet_adorcism"] = "welcoming spirits into the bodies of the faithful",
        ["tenet_alexandrian_catechism"] = "a learned catechism of instruction",
        ["tenet_ancestor_worship"] = "reverence for the ancestors",
        ["tenet_aniconism"] = "a strict refusal to depict the divine",
        ["tenet_armed_pilgrimages"] = "armed pilgrimages to defend its holy places",
        ["tenet_asceticism"] = "an ascetic contempt for comfort",
        ["tenet_astrology"] = "reading the will of the heavens in the stars",
        ["tenet_benevolent_governance"] = "the duty of rulers to govern with benevolence",
        ["tenet_bhakti"] = "fervent personal devotion",
        ["tenet_carnal_exaltation"] = "the exaltation of the flesh",
        ["tenet_communal_identity"] = "a fierce sense of shared identity",
        ["tenet_communal_possessions"] = "holding its wealth in common",
        ["tenet_communion"] = "the sacred rite of communion",
        ["tenet_consolamentum"] = "a single rite of consolation that cleanses the soul",
        ["tenet_cranial_trophies"] = "taking the skulls of fallen foes as trophies",
        ["tenet_cthonic_redoubts"] = "worship in hidden underground sanctuaries",
        ["tenet_divine_marriage"] = "marriage between close kin in imitation of the first divine unions",
        ["tenet_dharmic_pacifism"] = "nonviolence toward all living things",
        ["tenet_esotericism"] = "secret teachings revealed only to initiates",
        ["tenet_exaltation_of_pain"] = "finding holiness in suffering",
        ["tenet_extinction_of_dharma"] = "the conviction that the true teaching is fading from the world",
        ["tenet_false_conversion_sanction"] = "permitting the faithful to feign conversion under persecution",
        ["tenet_filial_piety"] = "devotion to one's parents and elders",
        ["tenet_fp3_fedayeen"] = "devotees who will die at a word from their masters",
        ["tenet_gnosticism"] = "the pursuit of hidden knowledge of the divine",
        ["tenet_gruesome_festivals"] = "gruesome festivals of blood",
        ["tenet_harmonious_society"] = "the ideal of a harmonious society",
        ["tenet_hedonistic"] = "the pursuit of pleasure as a sacred end",
        ["tenet_household_gods"] = "shrines to the household gods in every home",
        ["tenet_human_sacrifice"] = "offering human lives upon the altar",
        ["tenet_inner_journey"] = "a solitary inner journey toward enlightenment",
        ["tenet_legalism"] = "an exacting body of sacred law",
        ["tenet_literalism"] = "insisting on the literal truth of every holy word",
        ["tenet_megaliths"] = "worship among great standing stones under the open sky",
        ["tenet_mendicant_preachers"] = "wandering mendicant preachers",
        ["tenet_monasticism"] = "communities of monks withdrawn from the world",
        ["tenet_mountain_worship"] = "worship upon the high mountains",
        ["tenet_mystical_birthright"] = "the divine birthright of its rulers",
        ["tenet_natural_primitivism"] = "rejecting the comforts of civilisation",
        ["tenet_no_mind"] = "the stilling of the mind",
        ["tenet_pacifism"] = "a refusal to shed blood",
        ["tenet_pastoral_isolation"] = "keeping apart from the wider world",
        ["tenet_pentarchy"] = "ruling its church from the seats of its holy sites",
        ["tenet_polyamory"] = "sanctioned polyamory",
        ["tenet_preservation"] = "finding virtue in building things that last",
        ["tenet_pure_land"] = "longing for rebirth in a pure land",
        ["tenet_pursuit_of_knowledge"] = "ranking all people by their learning",
        ["tenet_pursuit_of_power"] = "the pursuit of power as a sacred calling",
        ["tenet_reincarnation"] = "belief in the cycle of rebirth",
        ["tenet_religious_legal_pronouncements"] = "the binding rulings of its scholars",
        ["tenet_rite"] = "keeping its own rite while acknowledging its mother faith's authority",
        ["tenet_ritual_cannibalism"] = "ritual cannibalism",
        ["tenet_ritual_celebrations"] = "lavish ritual celebrations",
        ["tenet_ritual_hospitality"] = "the sacred duty of hospitality",
        ["tenet_sacred_childbirth"] = "the sanctity of childbirth",
        ["tenet_sacred_shadows"] = "rites performed in darkness and secrecy",
        ["tenet_sacred_destruction"] = "holding that nothing new begins until something old is destroyed",
        ["tenet_sacrificial_ceremonies"] = "elaborate sacrificial ceremonies",
        ["tenet_sanctity_of_nature"] = "the sanctity of the natural world",
        ["tenet_struggle_submission"] = "the struggle to submit wholly to the divine will",
        ["tenet_sun_worship"] = "worship of the sun",
        ["tenet_takamin"] = "revering bears as the living forms of gods",
        ["tenet_tax_nonbelievers"] = "taxing the unbelievers under its rule",
        ["tenet_unrelenting_faith"] = "an unrelenting faith",
        ["tenet_vows_of_poverty"] = "vows of poverty",
        ["tenet_warmonger"] = "holding war to be a sacred duty",
    };

    // ---- Shared ----

    private static string Join(IReadOnlyList<string> items) => items.Count switch
    {
        0 => "",
        1 => items[0],
        _ => string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1],
    };

    /// <summary>
    /// The religion's generated words by template tag, or null where the install's template has no
    /// such tag or it holds a constant. Every sentence that uses an optional word checks for it.
    /// </summary>
    private sealed class Words
    {
        public string Religion { get; }
        public string God { get; }
        public string? War, Fertility, Devil, Priests, Houses, Text, Realm, Paradise, Hell;

        public Words(Religion religion)
        {
            var byTag = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (tag, key) in religion.Localization)
                if (religion.LocalizationText.TryGetValue(key, out string? value)) byTag[tag] = value;

            string? Get(string tag) => byTag.GetValueOrDefault(tag);

            Religion = religion.Name;
            God = Get("HighGodName") ?? $"the gods of {religion.Name}";
            War = Get("WarGodName");
            Fertility = Get("FertilityGodName");
            Devil = Get("DevilName");
            Priests = Get("PriestNeuterPlural") ?? Get("PriestMalePlural");
            Houses = Get("HouseOfWorshipPlural");
            Text = Get("ReligiousText");
            Realm = Get("DivineRealm");
            Paradise = Get("PositiveAfterLife");
            Hell = Get("NegativeAfterLife");

            // A god the template aliases to the high god would read as "X, beside whom X rules".
            if (War == God) War = null;
            if (Fertility == God) Fertility = null;
        }
    }
}
