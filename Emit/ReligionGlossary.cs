using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// Hover glosses for a generated religion's figures: its gods and devil by name ("Beanya"), the title
/// its head of faith bears, and its words for priest, bishop and devotee ("Dwote"). Every one is a
/// coined word a player cannot read on sight, and it turns up everywhere — vanilla's religious events,
/// the faith window, and the societies' prose, which names the cult's devil through the same key. So
/// the gloss goes where the word is defined: the religion's own localisation value becomes a link to a
/// hidden concept (<see cref="ConceptTooltips"/>), and every place that prints the word gets the hover.
///
/// The header is the English role — "The Devil", "Goddess of War", "Priestess" — and the line under
/// it names the religion. Gender comes from the religion's own pronoun tags (a WarGod whose SheHe is
/// CHARACTER_SHEHE_SHE is a goddess); a monotheist religion's lesser figures are patrons, not gods,
/// the way vanilla fills those slots with saints and angels.
///
/// The faith's things are glossed the same way: its house of worship, scripture, symbol, divine realm
/// and afterlives, each in the template's three variants, plus the witch god and the head of faith's
/// office. HouseOfWorshipPlural links through its singular's concept.
/// </summary>
internal static class ReligionGlossary
{
    private const string ConceptsFile = "zz_gen_religion_concepts.txt";
    private const string ConceptLocFile = "gen_religion_concepts_l_english.yml";

    private const string ConceptsComment = """
        Religion tooltips: a generated religion's gods, devil, head-of-faith title and clergy words,
        glossed with the English role they stand for. Referenced from gen_faiths_l_english.yml as
        [Concept('key','word')|E]. Written by Emit/ReligionGlossary.cs. Hidden from the encyclopedia.
        """;

    /// <summary>
    /// One glossed role: the localisation tag that holds the word, the tag holding its pronoun (null
    /// for a role with no gender of its own), and the header for each gender and theism.
    /// </summary>
    private sealed record Term(string Tag, string? PronounTag, Func<Gender, bool, string> Header, string Line);

    private enum Gender { Male, Female, Neuter }

    private static string Deity(Gender g, bool monotheist, string domain)
        => monotheist ? $"Patron of {domain}"
         : g switch { Gender.Female => $"Goddess of {domain}", Gender.Male => $"God of {domain}", _ => $"Deity of {domain}" };

    private static readonly Term[] Terms =
    [
        new("HighGodName", "HighGodNameSheHe", (g, m) => m ? "God" : "The High God", "The supreme deity of {0}."),
        new("HighGodName2", "HighGodNameSheHe", (g, m) => m ? "God" : "The High God", "The supreme deity of {0}."),
        new("HighGodNameAlternate", "HighGodNameSheHe", (g, m) => m ? "God" : "The High God", "Another name for the supreme deity of {0}."),
        new("CreatorName", "CreatorSheHe", (g, m) => "The Creator", "Who made the world, in {0}."),
        new("HealthGodName", "HealthGodSheHe", (g, m) => Deity(g, m, "Healing"), "Revered in {0}."),
        new("FertilityGodName", "FertilityGodSheHe", (g, m) => Deity(g, m, "Fertility"), "Revered in {0}."),
        new("WealthGodName", "WealthGodSheHe", (g, m) => Deity(g, m, "Wealth"), "Revered in {0}."),
        new("HouseholdGodName", "HouseholdGodSheHe", (g, m) => Deity(g, m, "the Household"), "Revered in {0}."),
        new("FateGodName", "FateGodSheHe", (g, m) => Deity(g, m, "Fate"), "Revered in {0}."),
        new("KnowledgeGodName", "KnowledgeGodSheHe", (g, m) => Deity(g, m, "Knowledge"), "Revered in {0}."),
        new("WarGodName", "WarGodSheHe", (g, m) => Deity(g, m, "War"), "Revered in {0}."),
        new("TricksterGodName", "TricksterGodSheHe", (g, m) => "The Trickster", "Revered, warily, in {0}."),
        new("NightGodName", "NightGodSheHe", (g, m) => Deity(g, m, "the Night"), "Revered in {0}."),
        new("WaterGodName", "WaterGodSheHe", (g, m) => Deity(g, m, "the Waters"), "Revered in {0}."),
        new("DeathDeityName", "DeathDeitySheHe", (g, m) => Deity(g, false, "Death"), "Feared in {0}."),
        new("DevilName", "DevilSheHe", (g, m) => "The Devil", "The enemy of the faithful in {0}."),
        new("ReligiousHeadName", null, (g, m) => "Head of the Faith", "The title borne by the head of {0}."),
        new("PriestMale", null, (g, m) => "Priest", "A title of {0}."),
        new("PriestFemale", null, (g, m) => "Priestess", "A title of {0}."),
        new("PriestNeuter", null, (g, m) => "Priest", "A title of {0}."),
        new("AltPriestTermPlural", null, (g, m) => "Priests", "A title of {0}."),
        new("BishopMale", null, (g, m) => "Bishop", "A title of {0}."),
        new("BishopFemale", null, (g, m) => "Bishop", "A title of {0}."),
        new("BishopNeuter", null, (g, m) => "Bishop", "A title of {0}."),
        new("DevoteeMale", null, (g, m) => "Monk", "A title of {0}."),
        new("DevoteeFemale", null, (g, m) => "Nun", "A title of {0}."),
        new("DevoteeNeuter", null, (g, m) => "Devotee", "A title of {0}."),
        new("WitchGodName", "WitchGodSheHe", (g, m) => Deity(g, false, "Witchcraft"), "Worshipped in secret by witches, in {0}."),
        new("ReligiousHeadTitleName", null, (g, m) => "Office of the Head of Faith", "The office held by the head of {0}."),

        // The things a faith has rather than the figures it reveres. Each comes in three words (the
        // template's 2 and 3 are synonyms vanilla picks between), so each gets three rows.
        .. Variants("HouseOfWorship", (g, m) => "Temple", "A house of worship of {0}."),
        .. Variants("ReligiousText", (g, m) => "Holy Scripture", "The sacred writings of {0}."),
        .. Variants("ReligiousSymbol", (g, m) => "Holy Symbol", "The sacred sign of {0}."),
        .. Variants("PantheonTerm", (g, m) => m ? "The Divine" : "The Gods", "The divine powers of {0}."),
        .. Variants("DivineRealm", (g, m) => "The Divine Realm", "Where the divine dwells, in {0}."),
        .. Variants("PositiveAfterLife", (g, m) => "Paradise", "Where the righteous go after death, in {0}."),
        .. Variants("NegativeAfterLife", (g, m) => "Damnation", "Where the wicked go after death, in {0}."),
    ];

    private static Term[] Variants(string tag, Func<Gender, bool, string> header, string line)
        => [new(tag, null, header, line), new(tag + "2", null, header, line), new(tag + "3", null, header, line)];

    /// <summary>
    /// The linked value for every glossed localisation key of every declared religion, registering
    /// each concept in <paramref name="concepts"/>. A term's plural and possessive forms link to the
    /// same concept as the word itself.
    /// </summary>
    public static Dictionary<string, string> Gloss(FaithMap faiths, ConceptTooltips concepts)
    {
        var linked = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var religion in faiths.Religions)
        {
            var byTag = religion.Localization
                .GroupBy(t => t.Tag, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.Ordinal);

            foreach (var term in Terms)
            {
                if (!byTag.TryGetValue(term.Tag, out var key) || !religion.LocalizationText.TryGetValue(key, out var word))
                    continue;

                var gender = term.PronounTag is not null && byTag.TryGetValue(term.PronounTag, out var pronoun)
                    ? pronoun switch
                    {
                        "CHARACTER_SHEHE_SHE" => Gender.Female,
                        "CHARACTER_SHEHE_HE" => Gender.Male,
                        _ => Gender.Neuter,
                    }
                    : Gender.Neuter;

                string concept = $"gen_rel_{religion.Key}_{term.Tag.ToLowerInvariant()}";
                string header = term.Header(gender, religion.Monotheist);
                string line = string.Format(term.Line, $"${religion.Key}$");

                linked[key] = concepts.Link(concept, header, line, word);

                // The plural shares the concept: "Dwotes" glosses as Priest.
                if (byTag.TryGetValue(term.Tag + "Plural", out var pluralKey)
                    && religion.LocalizationText.TryGetValue(pluralKey, out var plural))
                    linked[pluralKey] = ConceptTooltips.Linked(concept, plural);

                // The possessive links the name and keeps the "'s" outside the link, where the
                // quote cannot end the concept's argument early. A possessive that is not the name
                // plus a suffix is left as it is.
                if (byTag.TryGetValue(term.Tag + "Possessive", out var possKey)
                    && religion.LocalizationText.TryGetValue(possKey, out var possessive)
                    && possessive.StartsWith(word, StringComparison.Ordinal))
                    linked[possKey] = ConceptTooltips.Linked(concept, word) + possessive[word.Length..];
            }
        }

        return linked;
    }

    /// <summary>Writes the concepts, or removes a previous run's when the glosses are off.</summary>
    public static void Write(string modDir, ConceptTooltips? concepts)
    {
        string conceptsPath = Path.Combine(modDir, "common", "game_concepts", ConceptsFile);
        string locPath = Path.Combine(modDir, "localization", "english", ConceptLocFile);
        if (concepts is null || concepts.Count == 0) ConceptTooltips.Delete(conceptsPath, locPath);
        else concepts.Write(conceptsPath, locPath, ConceptsComment);
    }
}
