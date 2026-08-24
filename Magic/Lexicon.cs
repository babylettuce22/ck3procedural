using Ck3MapGen.Core;

namespace Ck3MapGen.Magic;

/// <summary>
/// How a world's invented words sound. One style per world, applied to every generated word in it,
/// because a tradition whose spells sound like five different languages reads as a list rather
/// than as a practice.
/// </summary>
public enum PhonoStyle
{
    Harsh,
    Liquid,
    Sibilant,
    Guttural,
    Airy,
}

/// <summary>
/// Names everything the magic system needs named.
///
/// Self-contained on purpose, for now. The tool already generates a language per culture, and when
/// this is wired up the tradition should be named in the language of whoever practises it — that
/// is strictly better and it is how the titles and faiths are already named. But a naming module
/// that depends on the world generator cannot be tested without running the world generator, and
/// the whole point of this folder at this stage is that it can be run and judged on its own. The
/// phoneme tables below are the throwaway half; the templates and the domain vocabulary are not,
/// and survive the swap.
/// </summary>
public sealed class Lexicon
{
    private readonly string[] _onsets;
    private readonly string[] _nuclei;
    private readonly string[] _codas;

    public Lexicon(Rng rng, PhonoStyle style)
    {
        Style = style;

        (_onsets, _nuclei, _codas) = style switch
        {
            PhonoStyle.Harsh =>
                (new[] { "k", "t", "kr", "dr", "tr", "g", "br", "v", "kh", "th" },
                 new[] { "a", "e", "u", "au", "ae", "o" },
                 new[] { "k", "rn", "th", "rk", "st", "ll", "g", "n" }),

            PhonoStyle.Liquid =>
                (new[] { "l", "m", "n", "v", "r", "el", "am", "il", "s", "th" },
                 new[] { "a", "e", "i", "ia", "ae", "eo" },
                 new[] { "l", "n", "r", "m", "th", "s", "" }),

            PhonoStyle.Sibilant =>
                (new[] { "s", "sh", "z", "ts", "x", "sc", "str", "ss", "th", "f" },
                 new[] { "i", "e", "ei", "y", "a", "ia" },
                 new[] { "s", "sh", "ss", "st", "x", "th", "" }),

            PhonoStyle.Guttural =>
                (new[] { "g", "gh", "kh", "h", "q", "gr", "ng", "dh", "b", "z" },
                 new[] { "o", "u", "a", "ou", "ao", "uu" },
                 new[] { "gh", "kh", "g", "r", "n", "m", "q" }),

            _ =>
                (new[] { "", "h", "w", "y", "l", "th", "v", "s", "n", "ae" },
                 new[] { "ai", "ea", "io", "ei", "a", "e", "i", "ou" },
                 new[] { "n", "r", "l", "th", "", "", "s" }),
        };

        Rng = rng;
    }

    public PhonoStyle Style { get; }

    private Rng Rng { get; }

    /// <summary>An invented word of one to three syllables, capitalised.</summary>
    public string Word(int syllables = 0)
    {
        if (syllables <= 0) syllables = Rng.Int(2, 3);

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < syllables; i++)
        {
            sb.Append(Rng.Pick(_onsets));
            sb.Append(Rng.Pick(_nuclei));

            // A coda on every syllable produces unpronounceable stacks; only the last syllable
            // takes one reliably, which is what makes these read as words rather than as noise.
            if (i == syllables - 1 || Rng.Chance(0.25)) sb.Append(Rng.Pick(_codas));
        }

        string word = sb.ToString();
        if (word.Length < 3) word += Rng.Pick(_nuclei);
        return char.ToUpperInvariant(word[0]) + word[1..];
    }

    // ------------------------------------------------------------------ vocabulary

    /// <summary>
    /// What each domain's effects are called. English rather than invented, because a spell whose
    /// every word is made up tells the player nothing and they will not learn twelve of them.
    /// The invented half goes in the qualifier, where it carries flavour without carrying meaning.
    /// </summary>
    private static readonly Dictionary<MagicDomain, string[]> DomainNouns = new()
    {
        [MagicDomain.Life] = ["Mending", "Quickening", "Kindling", "Restoration", "Green Hour", "Knitting"],
        [MagicDomain.Death] = ["Wasting", "Calling", "Silence", "Cold Bell", "Reaping", "Last Breath"],
        [MagicDomain.War] = ["Hardening", "Rout", "Iron Hour", "War-Cry", "Standing", "Red Field"],
        [MagicDomain.Mind] = ["Turning", "Whisper", "Open Door", "Quiet Word", "Unmaking", "Long Look"],
        [MagicDomain.Nature] = ["Blooming", "Souring", "Stillness", "Shaping", "Tide-Turn", "Deep Root"],
        [MagicDomain.Fate] = ["Reading", "Ill Chance", "Binding", "Severance", "Naming", "Long Thread"],
        [MagicDomain.Craft] = ["Setting", "Working", "Gilding", "Raising", "Binding-Work", "Cold Forge"],
    };

    private static readonly Dictionary<MagicPrice, string[]> PriceAdjectives = new()
    {
        [MagicPrice.Corruption] = ["Rotting", "Grey", "Turned", "Sunken"],
        [MagicPrice.Taint] = ["Inherited", "Bloodfast", "Seeded", "Passed-Down"],
        [MagicPrice.Depletion] = ["Hollowing", "Drawn", "Spent", "Thirsty"],
        [MagicPrice.Attention] = ["Watched", "Marked", "Noticed", "Answered"],
        [MagicPrice.Stigma] = ["Hidden", "Unspoken", "Quiet", "Denied"],
        [MagicPrice.Instability] = ["Widening", "Loosened", "Cracked", "Unquiet"],
        [MagicPrice.Backlash] = ["Uncertain", "Wild", "Kicking", "Loose"],
    };

    /// <summary>The tradition's own name for itself.</summary>
    public string TraditionName(Cosmology myth)
    {
        string root = Word();
        return myth.Source switch
        {
            MagicSource.Force => Rng.Chance(0.5) ? $"the {root} Discipline" : $"{root}-Learning",
            MagicSource.Entities => Rng.Chance(0.5) ? $"the {root} Compact" : $"the Petitioners of {root}",
            MagicSource.Substance => Rng.Chance(0.5) ? $"{root}-Work" : $"the {root} Trade",
            MagicSource.Inheritance => Rng.Chance(0.5) ? $"the {root} Blood" : $"{root}-Descent",
            MagicSource.Language => Rng.Chance(0.5) ? $"the {root} Tongue" : $"the Speech of {root}",
            MagicSource.Wound => Rng.Chance(0.5) ? $"the {root} Scar" : $"what {root} Left",
            _ => root,
        };
    }

    /// <summary>What the institution calls itself, if it is the sort of thing that has a name.</summary>
    public string InstitutionName(MagicInstitution institution) => institution switch
    {
        MagicInstitution.College => $"the College of {Word()}",
        MagicInstitution.Cult => $"the {Word()} Veil",
        MagicInstitution.Church => $"the {Word()} Office",
        MagicInstitution.Crown => $"the Crown Register of {Word()}",
        MagicInstitution.Folk => $"the {Word()} wise-folk",
        MagicInstitution.Outlaw => $"those they call {Word().ToLowerInvariant()}",
        _ => "no institution at all",
    };

    /// <summary>
    /// Rank titles. Drawn from the institution rather than invented, because the ladder is the one
    /// piece of the system the player has to be able to read at a glance to know where they stand.
    /// </summary>
    public IReadOnlyList<string> RankTitles(MagicInstitution institution, int count)
    {
        string[] ladder = institution switch
        {
            MagicInstitution.College => ["Apprentice", "Adept", "Magister", "Archmagister", "Chair"],
            MagicInstitution.Cult => ["Veiled", "Initiate", "Keeper", "Hierophant", "The Unnamed"],
            MagicInstitution.Church => ["Acolyte", "Ordained", "Vested", "Hierarch", "Vessel"],
            MagicInstitution.Crown => ["Registered", "Sworn", "Court-Sworn", "Crown-Sworn", "Regent's Hand"],
            MagicInstitution.Folk => ["Hedge", "Cunning", "Wise", "Elder", "Old One"],
            MagicInstitution.Outlaw => ["Hidden", "Marked", "Hunted", "Notorious", "The Name They Use"],
            _ => ["Nameless", "Ninth", "Third", "Second", "First"],
        };

        return ladder.Take(Math.Clamp(count, 1, ladder.Length)).ToList();
    }

    /// <summary>A spell name: an English head the player can parse, and an invented qualifier.</summary>
    public string SpellName(EffectAtom lead, MagicPrice price)
    {
        string noun = Rng.Pick(DomainNouns[lead.Domain]);
        string adjective = Rng.Pick(PriceAdjectives[price]);

        return Rng.Int(0, 3) switch
        {
            0 => $"The {adjective} {noun}",
            1 => $"{Word()}'s {noun}",
            2 => $"{noun} of {Word()}",
            _ => $"The {noun} at {Word()}",
        };
    }

    /// <summary>An entity's name and epithet.</summary>
    public string EntityName(MagicDomain sphere)
    {
        string name = Word(Rng.Int(2, 3));
        string[] epithets = sphere switch
        {
            MagicDomain.Life => ["the Green Mother", "Who Counts Births", "the Unclosing Hand"],
            MagicDomain.Death => ["the Cold Bell", "Who Keeps the Tally", "the Last Guest"],
            MagicDomain.War => ["the Red Field", "Who Is Owed Blood", "the Standing Wall"],
            MagicDomain.Mind => ["the Open Door", "Who Listens at Walls", "the Second Voice"],
            MagicDomain.Nature => ["the Deep Root", "Who Turns the Year", "the Weather's Owner"],
            MagicDomain.Fate => ["the Long Thread", "Who Reads Ahead", "the Knot-Maker"],
            _ => ["the Cold Forge", "Who Finishes Things", "the Patient Hand"],
        };

        return $"{name}, {Rng.Pick(epithets)}";
    }
}
