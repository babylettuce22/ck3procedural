using Ck3MapGen.Core;
using Ck3MapGen.Config;
using static Ck3MapGen.MapGen.Phonology;

namespace Ck3MapGen.MapGen;

/// <summary>
/// A family resemblance for a generated language to be born into.
///
/// Every table here is a starting point, not a script: <see cref="Phonology.FromFlavour"/> jitters
/// the weights, drops half the optional sounds, keeps a random subset of the clusters and picks one
/// spelling from each set of alternatives, so two Norse-flavoured languages share a look the way
/// Danish and Icelandic do rather than the way two seeds of one generator do. The authentic
/// affixes and name elements are fed through that same machinery, which is what makes them come
/// out bastardised rather than borrowed — "vík" in one tongue is "viik" in the next.
///
/// Notation for every phoneme string: ids run together, longest match first, a dot where two ids
/// would otherwise be misread ("t.h" is t then h). See <see cref="Phonemes.Parse"/>.
/// </summary>
public sealed class LanguageFlavour
{
    public required string Name { get; init; }
    public bool Fantasy { get; init; }

    public required (string Id, double Weight, bool Core)[] Consonants { get; init; }
    public required (string Id, double Weight, bool Core)[] Vowels { get; init; }

    /// <summary>Weights for V, CV, CVC, CCV, CCVC, VC, CVCC, CCVCC in that order.</summary>
    public required double[] Templates { get; init; }

    public OnsetFamily OnsetFamilies { get; init; } = OnsetFamily.None;
    public CodaFamily CodaFamilies { get; init; } = CodaFamily.None;
    public string[] CodaSet { get; init; } = [];
    public double ClusterKeep { get; init; } = 0.7;

    public int MaxBoundaryCluster { get; init; } = 2;
    public bool Hiatus { get; init; }
    public bool Gemination { get; init; }
    public bool Harmony { get; init; }
    public bool StopStopOk { get; init; }
    public double InitialVowel { get; init; } = 0.15;
    public double FinalOpen { get; init; } = 0.4;

    public Dictionary<string, string[]> Spelling { get; init; } = [];
    public Orthography.LongStyle[] LongVowelStyles { get; init; } = [Orthography.LongStyle.Plain];
    public string[] KFrontOptions { get; init; } = ["k"];
    public string[] GFrontOptions { get; init; } = ["g"];
    public bool CkFinal { get; init; }

    /// <summary>How likely this language is to put its place-word in front (Aber-, al-, Bally-).</summary>
    public double PrefixingChance { get; init; }
    public string[] Prefixes { get; init; } = [];

    public string[] Barony { get; init; } = [];
    public string[] County { get; init; } = [];
    public string[] Duchy { get; init; } = [];
    public string[] Kingdom { get; init; } = [];

    /// <summary>Words for "the people of": the second half of Westseakse, the -ingas of Hastings.</summary>
    public string[] Folk { get; init; } = [];

    public string[] MaleEndings { get; init; } = [];
    public string[] FemaleEndings { get; init; } = [];
    public string[] FeminineMarkers { get; init; } = [];

    /// <summary>Authentic name elements, when the tradition is a compounding one.</summary>
    public string[] Proto { get; init; } = [];
    public string[] MaleDeutero { get; init; } = [];
    public string[] FemaleDeutero { get; init; } = [];

    /// <summary>Share of names built as element+element, and as root+ending; the rest are bare roots.</summary>
    public double Dithematic { get; init; } = 0.4;
    public double RootEnding { get; init; } = 0.4;

    /// <summary>Genitive glue between a personal name and a place-word ("" for none, "ing", "s", "o").</summary>
    public string[] Linkers { get; init; } = [""];

    public enum PatronymStyle { None, Suffix, Prefix }
    public PatronymStyle Patronym { get; init; } = PatronymStyle.Suffix;

    /// <summary>Parallel arrays: one index is one male/female pair. Empty means "invent one".</summary>
    public string[] PatronymMale { get; init; } = [];
    public string[] PatronymFemale { get; init; } = [];

    /// <summary>The "of" before a dynasty's seat. Empty means "invent one".</summary>
    public string[] Particles { get; init; } = [];

    /// <summary>English-side suffixes that turn a people's name into its language's name.</summary>
    public string[] Adjectival { get; init; } = ["ish", "ic"];

    /// <summary>Fixed language names, for a flavour whose tongue has a name of its own.</summary>
    public string[]? LanguageNames { get; init; }

    /// <summary>Real roots for concepts, by <see cref="Lexicon"/> concept name, for a flavour that has them.</summary>
    public Dictionary<string, string>? Roots { get; init; }

    // ------------------------------------------------------------------------------------------

    /// <summary>"p:3 b:2 kh:1?" — id, weight, and a trailing ? for a sound the language may lack.</summary>
    private static (string, double, bool)[] Sounds(string spec)
        => spec.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(tok =>
        {
            bool optional = tok.EndsWith('?');
            if (optional) tok = tok[..^1];
            int colon = tok.IndexOf(':');
            return (tok[..colon], double.Parse(tok[(colon + 1)..], System.Globalization.CultureInfo.InvariantCulture), !optional);
        }).ToArray();

    private static string[] W(string spec) => spec.Split(' ', StringSplitOptionsRemoveEmpty);
    private const StringSplitOptions StringSplitOptionsRemoveEmpty = StringSplitOptions.RemoveEmptyEntries;

    private static Dictionary<string, string[]> Sp(params (string Id, string Options)[] entries)
        => entries.ToDictionary(e => e.Id, e => W(e.Options));

    // ------------------------------------------------------------------------------------------

    public static readonly LanguageFlavour Anglic = new()
    {
        Name = "Anglic",
        Consonants = Sounds("p:2 b:3 t:5 d:4 k:4 g:3 f:3 v:1? th:3 dh:1? s:5 sh:2 h:3 m:4 n:5 ng:1 l:5 r:5 w:4 y:2 hl:1? hr:1?"),
        Vowels = Sounds("a:5 e:5 i:3 o:3 u:2 ae:3 ü:1 eu:1 aa:1? ee:1?"),
        Templates = [0.5, 3, 5, 1, 2, 1, 1.5, 0.5],
        OnsetFamilies = OnsetFamily.ObstruentLiquid | OnsetFamily.SStop | OnsetFamily.SNasal | OnsetFamily.SStopLiquid | OnsetFamily.ObstruentGlide | OnsetFamily.LabioVelar,
        CodaFamilies = CodaFamily.NasalStop | CodaFamily.LiquidObstruent | CodaFamily.LiquidNasal | CodaFamily.SStop | CodaFamily.FricativeStop,
        CodaSet = W("p t d k g f th s sh m n ng l r"),
        MaxBoundaryCluster = 3,
        Gemination = true,
        InitialVowel = 0.2,
        FinalOpen = 0.3,
        Spelling = Sp(("k", "c k"), ("sh", "sc sh"), ("th", "th th þ"), ("dh", "th ð"), ("ae", "æ ae"), ("ü", "y"), ("eu", "eo"), ("hl", "hl"), ("hr", "hr"), ("ks", "x")),
        KFrontOptions = ["c", "k"],
        Barony = W("tun ham ford leah bü wik burh stede worth feld dun kumb hürst wel.la mere burna thorp keaster hlaw hüth"),
        County = W("shire land feld worth"),
        Duchy = W("land shire mark"),
        Kingdom = W("land rike ia"),
        Folk = W("ingas seakse ware saete engle"),
        MaleEndings = W("a"),
        FemaleEndings = W("e u"),
        FeminineMarkers = W("e"),
        Proto = W("aethel ead eald aelf beorht küne ekg god here leof os sige wig wulf wine ord kuth dun frith beorn wil wal sae thur tid keol ken hun bald hroth wist"),
        MaleDeutero = W("rik wine wulf red mund helm beorht weard stan gar noth wald frith here lak maer sige heard bald"),
        FemaleDeutero = W("flaed gifu güth hild burg thrüth wünn swith run leofu waru"),
        Dithematic = 0.9,
        RootEnding = 0.05,
        Linkers = ["", "", "ing", "ing", "es"],
        Patronym = PatronymStyle.Suffix,
        PatronymMale = ["ing"],
        PatronymFemale = ["ing"],
        Particles = ["=of"],
        LanguageNames = ["Anglish", "Aenglisc", "Ealdic", "Westran", "Seaxan", "Mierce", "Northan"],
        Roots = new()
        {
            ["hill"] = "dun", ["mountain"] = "beorg", ["valley"] = "denu", ["river"] = "ea", ["ford"] = "ford",
            ["lake"] = "mere", ["sea"] = "sae", ["marsh"] = "fen", ["wood"] = "wudu", ["field"] = "feld",
            ["stone"] = "stan", ["rock"] = "klif", ["spring"] = "wel", ["island"] = "ieg", ["cliff"] = "klif",
            ["meadow"] = "leah", ["moor"] = "mor", ["bay"] = "hüth", ["mouth"] = "mutha", ["ridge"] = "hrükg",
            ["fort"] = "burh", ["town"] = "tun", ["farm"] = "wik", ["home"] = "ham", ["bridge"] = "brükg",
            ["wall"] = "weal", ["tower"] = "tor", ["temple"] = "kirke", ["market"] = "keap", ["mill"] = "mülen",
            ["harbour"] = "hüth", ["road"] = "straet", ["gate"] = "geat", ["hall"] = "heal",
            ["high"] = "hean", ["low"] = "lag", ["new"] = "niwe", ["old"] = "eald", ["black"] = "blaek",
            ["white"] = "hwit", ["red"] = "read", ["green"] = "grene", ["grey"] = "graeg", ["great"] = "mikel",
            ["little"] = "lütel", ["long"] = "lang", ["broad"] = "brad", ["cold"] = "kald", ["holy"] = "halig",
            ["east"] = "east", ["west"] = "west", ["north"] = "north", ["south"] = "suth", ["fair"] = "faeger",
            ["wild"] = "wilde", ["bright"] = "beorht", ["dark"] = "deork", ["middle"] = "middel",
            ["king"] = "küning", ["folk"] = "folk", ["lord"] = "hlaford", ["priest"] = "preost", ["warrior"] = "wiga",
            ["wolf"] = "wulf", ["bear"] = "bera", ["boar"] = "eofor", ["raven"] = "hraefn", ["eagle"] = "earn",
            ["stag"] = "heorot", ["horse"] = "hors", ["hawk"] = "hafok", ["serpent"] = "würm", ["oak"] = "ak",
            ["ash"] = "aesk", ["thorn"] = "thorn", ["iron"] = "isen", ["gold"] = "gold", ["silver"] = "seolfor",
            ["fire"] = "für", ["storm"] = "storm", ["sun"] = "sunne", ["moon"] = "mona", ["star"] = "steorra",
            ["sword"] = "sweord", ["spear"] = "gar", ["shield"] = "skield", ["peace"] = "frith", ["war"] = "wig",
            ["gift"] = "gifu", ["friend"] = "wine", ["guard"] = "weard", ["glory"] = "wuldor", ["wisdom"] = "raed",
            ["victory"] = "sige", ["strength"] = "strang", ["love"] = "lufu", ["blessing"] = "blaed", ["honour"] = "ar",
            ["luck"] = "ead", ["son"] = "sunu", ["daughter"] = "dohtor", ["of"] = "of",
        },
    };

    public static readonly LanguageFlavour Norse = new()
    {
        Name = "Norse",
        Consonants = Sounds("p:1 b:2 t:4 d:3 k:4 g:3 f:3 v:3 th:2 dh:1? s:5 h:3 m:3 n:4 ng:1 l:4 r:5 hl:1? hr:1? y:2"),
        Vowels = Sounds("a:5 e:3 i:4 o:3 u:2 ü:1? ae:1? oe:2 aa:1 ii:1 oo:1 ei:1 au:1"),
        Templates = [0.4, 3, 5, 0.7, 1.5, 0.8, 1.5, 0.5],
        OnsetFamilies = OnsetFamily.ObstruentLiquid | OnsetFamily.SStop | OnsetFamily.SNasal | OnsetFamily.ObstruentGlide | OnsetFamily.LabioVelar,
        CodaFamilies = CodaFamily.NasalStop | CodaFamily.LiquidObstruent | CodaFamily.LiquidNasal | CodaFamily.SStop | CodaFamily.FricativeStop,
        CodaSet = W("p t d k g f s th m n ng l r v"),
        ClusterKeep = 0.5,
        MaxBoundaryCluster = 3,
        Gemination = true,
        InitialVowel = 0.2,
        FinalOpen = 0.35,
        Spelling = Sp(("y", "j"), ("w", "v"), ("th", "th þ"), ("dh", "d ð"), ("oe", "ø ö"), ("ae", "æ"), ("ü", "y"), ("ei", "ei ey"), ("hl", "hl"), ("hr", "hr")),
        LongVowelStyles = [Orthography.LongStyle.Acute, Orthography.LongStyle.Plain],
        Barony = W("vik bü stad fyord heim nes dal berg vatn fos.s holm eü borg strand lund vang haug"),
        County = W("land süsla herad"),
        Duchy = W("land mark fülki"),
        Kingdom = W("land riki veldi"),
        Folk = W("ar ingar"),
        MaleEndings = W("i ar e"),
        FemaleEndings = W("a"),
        FeminineMarkers = W("a"),
        Proto = W("sig thor ing as ragn hal.l gun.n ulf byorn arn ein stein har gud fin hyal.m ket.il thorb"),
        MaleDeutero = W("ulf byorn mund vald stein leif grim kel.l vard ar gard"),
        FemaleDeutero = W("hild run dis veig gerd laug frid nü borg"),
        Dithematic = 0.6,
        RootEnding = 0.3,
        Linkers = ["", "s"],
        Patronym = PatronymStyle.Suffix,
        PatronymMale = ["s.s.on"],
        PatronymFemale = ["s.d.oo.t.t.ir"],
        Particles = ["af", "or"],
        Adjectival = ["ish", "sk", "ic"],
    };

    public static readonly LanguageFlavour Germanic = new()
    {
        Name = "Germanic",
        Consonants = Sounds("p:2 b:3 t:4 d:3 k:4 g:3 f:3 v:2 s:4 sh:2 kh:2 h:3 m:3 n:4 ng:1 l:4 r:5 w:2 y:1 ts:2"),
        Vowels = Sounds("a:5 e:4 i:3 o:3 u:3 ü:1 oe:1 ae:1 ei:2 au:2 ie:1 eu:1"),
        Templates = [0.5, 3, 5, 1, 2, 1, 2, 1],
        OnsetFamilies = OnsetFamily.ObstruentLiquid | OnsetFamily.SStop | OnsetFamily.SNasal | OnsetFamily.SStopLiquid | OnsetFamily.ObstruentGlide | OnsetFamily.LabioVelar,
        CodaFamilies = CodaFamily.NasalStop | CodaFamily.LiquidObstruent | CodaFamily.LiquidNasal | CodaFamily.SStop | CodaFamily.FricativeStop,
        CodaSet = W("p t d k g f s sh kh ts m n ng l r"),
        MaxBoundaryCluster = 3,
        Gemination = true,
        CkFinal = true,
        InitialVowel = 0.15,
        FinalOpen = 0.35,
        Spelling = Sp(("kh", "ch"), ("sh", "sch sh"), ("ts", "z tz"), ("y", "j"), ("ü", "ü u"), ("oe", "ö oe"), ("ae", "ä e"), ("ei", "ei ai")),
        Barony = W("burg berg heim dorf bakh ingen stein feld hausen bruk furt wald hof tal au brun.n"),
        County = W("gau land mark"),
        Duchy = W("mark land gau"),
        Kingdom = W("land rikh"),
        Folk = W("er ingen"),
        MaleEndings = W("o"),
        FemaleEndings = W("a e"),
        FeminineMarkers = W("a"),
        Proto = W("adal ald bern diet ek fried ger god hein hild kon lud ot rein sieg wal wil wolf"),
        MaleDeutero = W("rikh bert hard mund wald wig win helm fried mar olf"),
        FemaleDeutero = W("hild gard trud burg gund linde run"),
        Dithematic = 0.65,
        RootEnding = 0.2,
        Linkers = ["", "s", "en"],
        Patronym = PatronymStyle.None,
        Particles = ["von", "van", "vom"],
        Adjectival = ["ish", "ic"],
    };

    public static readonly LanguageFlavour Latinate = new()
    {
        Name = "Latinate",
        Consonants = Sounds("p:4 b:2 t:5 d:3 k:4 g:2 f:2 v:2 s:5 h:1 m:4 n:5 l:5 r:5 y:1 w:1"),
        Vowels = Sounds("a:5 e:4 i:4 o:3 u:3 aa:1 ee:1 ii:1 oo:1 ae:1 au:1"),
        Templates = [1, 5, 3, 1.5, 1, 0.7, 0.3, 0],
        OnsetFamilies = OnsetFamily.ObstruentLiquid | OnsetFamily.SStop | OnsetFamily.SStopLiquid | OnsetFamily.LabioVelar,
        CodaFamilies = CodaFamily.NasalStop | CodaFamily.LiquidObstruent | CodaFamily.LiquidNasal | CodaFamily.SStop,
        CodaSet = W("t d k s m n l r p"),
        MaxBoundaryCluster = 3,
        Hiatus = true,
        Gemination = true,
        InitialVowel = 0.25,
        FinalOpen = 0.55,
        Spelling = Sp(("k", "c"), ("y", "i j"), ("w", "v"), ("kw", "qu"), ("gw", "gu"), ("ae", "ae")),
        KFrontOptions = ["c", "c", "ch", "qu"],
        LongVowelStyles = [Orthography.LongStyle.Plain, Orthography.LongStyle.Macron],
        Barony = W("ium ia onia entum ana akum iakum kastra briga durum magus ona ates"),
        County = W("ia ana ika"),
        Duchy = W("ia itania ina"),
        Kingdom = W("ia ania onia"),
        Folk = W("i ii ani"),
        MaleEndings = W("us ius ianus inus io"),
        FemaleEndings = W("a ia ina illa"),
        FeminineMarkers = W("a"),
        Dithematic = 0.05,
        RootEnding = 0.85,
        Patronym = PatronymStyle.None,
        Particles = ["de", "di", "da"],
        Adjectival = ["ian", "ic", "ine", "an"],
    };

    public static readonly LanguageFlavour Hellenic = new()
    {
        Name = "Hellenic",
        Consonants = Sounds("p:4 b:1 t:5 d:3 k:5 g:2 f:2 th:3 kh:2 s:5 h:1 m:4 n:5 l:5 r:5 z:1"),
        Vowels = Sounds("a:5 e:4 i:4 o:4 u:1 ü:1 ai:2 ei:2 oi:1 ou:2 eu:1 ee:1 oo:1"),
        Templates = [1.2, 5, 3, 1.5, 1, 0.8, 0.2, 0],
        OnsetFamilies = OnsetFamily.ObstruentLiquid | OnsetFamily.SStop | OnsetFamily.SStopLiquid | OnsetFamily.Attic,
        CodaFamilies = CodaFamily.NasalStop | CodaFamily.LiquidObstruent | CodaFamily.SStop,
        CodaSet = W("n s r l k p t m"),
        MaxBoundaryCluster = 3,
        Hiatus = true,
        Gemination = true,
        StopStopOk = true,
        InitialVowel = 0.3,
        FinalOpen = 0.45,
        Spelling = Sp(("f", "ph"), ("kh", "ch"), ("k", "k c"), ("ü", "y"), ("y", "i"), ("ks", "x"), ("ai", "ai ae"), ("ei", "ei i"), ("ou", "ou u"), ("oi", "oi oe")),
        LongVowelStyles = [Orthography.LongStyle.Plain, Orthography.LongStyle.Macron],
        Barony = W("polis os on ia ion eia ea is.sa os.sa inthos akos"),
        County = W("ia is ike"),
        Duchy = W("ia ike"),
        Kingdom = W("ia"),
        Folk = W("oi ioi"),
        MaleEndings = W("os ios on as es"),
        FemaleEndings = W("a e ia is"),
        FeminineMarkers = W("a e"),
        Proto = W("aleks theo kle demo niko hip.po fil andro leo dio ari polü apol.lo hero"),
        MaleDeutero = W("andros kles doros makhos nikos filos kratos genes stratos menes fanes laos"),
        FemaleDeutero = W("andra kleia dora nike thea ip.pe file dike ope"),
        Dithematic = 0.55,
        RootEnding = 0.35,
        Linkers = ["", "o"],
        Patronym = PatronymStyle.Suffix,
        PatronymMale = ["ides"],
        PatronymFemale = ["is"],
        Particles = ["=of"],
        Adjectival = ["ic", "ian", "ean"],
    };

    public static readonly LanguageFlavour Slavic = new()
    {
        Name = "Slavic",
        Consonants = Sounds("p:3 b:3 t:4 d:4 k:4 g:3 v:4 s:4 z:3 sh:2 zh:2 kh:2 ch:2 ts:1 m:4 n:5 l:5 r:5 y:2"),
        Vowels = Sounds("a:5 e:4 i:4 o:5 u:2 ia:1 ie:1"),
        Templates = [0.6, 4, 4, 2, 2, 0.8, 0.6, 0.4],
        OnsetFamilies = OnsetFamily.ObstruentLiquid | OnsetFamily.SStop | OnsetFamily.SNasal | OnsetFamily.SStopLiquid | OnsetFamily.Eastern | OnsetFamily.ObstruentGlide | OnsetFamily.LabioVelar,
        CodaFamilies = CodaFamily.NasalStop | CodaFamily.LiquidObstruent | CodaFamily.LiquidNasal | CodaFamily.SStop | CodaFamily.FricativeStop,
        CodaSet = W("v s z sh zh kh ch ts m n l r t d k g b p"),
        MaxBoundaryCluster = 3,
        StopStopOk = true,
        InitialVowel = 0.12,
        FinalOpen = 0.4,
        Spelling = Sp(("sh", "sh š sz"), ("zh", "zh ž"), ("ch", "ch č cz"), ("kh", "kh ch h"), ("ts", "ts c"), ("y", "j y"), ("w", "v"), ("v", "v w"), ("ia", "ia ja"), ("ie", "ie je")),
        Barony = W("grad ov ovo sk itse in ovitse av itsa no gorod pol ev ino ets"),
        County = W("ina sko zem"),
        Duchy = W("sko ia"),
        Kingdom = W("ia sko"),
        Folk = W("ane ichi tsi"),
        MaleEndings = W("o ek ko an ik"),
        FemaleEndings = W("a ka ia"),
        FeminineMarkers = W("a"),
        Proto = W("vladi bori miro svyato yaro stani rado dobro lyubo vyache bogu zbig kazi rosti tomi vito drago"),
        MaleDeutero = W("mir slav bor misl rad voy gost dan mil"),
        FemaleDeutero = W("mira slava ava rada dana mila"),
        Dithematic = 0.5,
        RootEnding = 0.4,
        Linkers = ["", "o", "e"],
        Patronym = PatronymStyle.Suffix,
        PatronymMale = ["ovich"],
        PatronymFemale = ["ovna"],
        Particles = ["z", "iz", "od"],
        Adjectival = ["ian", "ic", "ish"],
    };

    public static readonly LanguageFlavour Desert = new()
    {
        Name = "Desert",
        Consonants = Sounds("b:3 t:4 d:3 k:3 q:2 ':1 f:3 th:1? dh:1? s:4 z:2 sh:3 kh:2 gh:1 h:3 m:4 n:4 l:5 r:5 w:2 y:2 j:2"),
        Vowels = Sounds("a:6 i:4 u:3 aa:3 ii:2 uu:2 ai:1 au:1 e:0.5? o:0.5?"),
        Templates = [0.3, 4, 5, 0, 0, 0.7, 1.2, 0],
        CodaFamilies = CodaFamily.NasalStop | CodaFamily.LiquidObstruent | CodaFamily.LiquidNasal | CodaFamily.SStop | CodaFamily.FricativeStop,
        CodaSet = W("b t d k q f th dh s z sh kh gh h m n l r y w j"),
        MaxBoundaryCluster = 2,
        Gemination = true,
        InitialVowel = 0.25,
        FinalOpen = 0.3,
        Spelling = Sp(("q", "q k"), ("'", "' "), ("dh", "dh z"), ("th", "th s"), ("j", "j dj")),
        LongVowelStyles = [Orthography.LongStyle.Macron, Orthography.LongStyle.Plain, Orthography.LongStyle.Plain],
        PrefixingChance = 0.6,
        Prefixes = W("al-"),
        Barony = W("iy.ya abad ah an at ain iyah"),
        County = W("iy.ya stan"),
        Duchy = W("stan at"),
        Kingdom = W("stan iy.ya"),
        Folk = W("i iy.yun"),
        MaleEndings = W("id im ir an ul ad il"),
        FemaleEndings = W("a ah iya ima ina"),
        FeminineMarkers = W("a ah"),
        Proto = W("abd nur saif shams badr fakhr"),
        MaleDeutero = W("al.lah din malik rahman aziz karim"),
        FemaleDeutero = W("a ah"),
        Dithematic = 0.15,
        RootEnding = 0.7,
        Linkers = ["", "u", "al"],
        Patronym = PatronymStyle.Prefix,
        PatronymMale = ["ibn"],
        PatronymFemale = ["bint"],
        Particles = ["al-"],
        Adjectival = ["i", "ic"],
    };

    public static readonly LanguageFlavour Celtic = new()
    {
        Name = "Celtic",
        Consonants = Sounds("p:1 b:3 t:3 d:4 k:5 g:4 f:2 v:2 s:3 h:2 m:4 n:5 l:5 r:5 w:3 y:1 ll:1? rh:1? th:1? dh:1? kh:1?"),
        Vowels = Sounds("a:5 e:4 i:4 o:4 u:3 ü:1 ai:1 ei:1 ou:1 ia:1"),
        Templates = [0.7, 4, 4, 1.5, 1.5, 0.8, 0.6, 0],
        OnsetFamilies = OnsetFamily.ObstruentLiquid | OnsetFamily.SStop | OnsetFamily.SNasal | OnsetFamily.ObstruentGlide,
        CodaFamilies = CodaFamily.NasalStop | CodaFamily.LiquidObstruent | CodaFamily.LiquidNasal | CodaFamily.SStop,
        CodaSet = W("b d g k t s m n l r ll th dh kh v f"),
        MaxBoundaryCluster = 3,
        Gemination = true,
        InitialVowel = 0.22,
        FinalOpen = 0.4,
        Spelling = Sp(("k", "c k"), ("ll", "ll"), ("rh", "rh"), ("ü", "y u"), ("y", "i"), ("kh", "ch"), ("dh", "dd dh"), ("v", "f v bh"), ("f", "ff f"), ("ai", "ai ae"), ("ou", "ow ou")),
        KFrontOptions = ["c", "k"],
        PrefixingChance = 0.55,
        Prefixes = W("aber kaer llan pen tre dun kil inver glen lokh strath ard kin bal"),
        Barony = W("mor ok ard dun ei nant ros lin bre mon wen garth"),
        County = W("land mor akh"),
        Duchy = W("ia land"),
        Kingdom = W("ia land"),
        Folk = W("i es"),
        MaleEndings = W("an gan ok wal ael"),
        FemaleEndings = W("wen a ith ed el.l"),
        FeminineMarkers = W("a wen"),
        Proto = W("kon ker bran kad mor kath ar tal kar dun mael rhi ow gwen"),
        MaleDeutero = W("an gan wal wün mor gern rik dor lan"),
        FemaleDeutero = W("wen wün a ed ith wed el.l"),
        Dithematic = 0.5,
        RootEnding = 0.35,
        Patronym = PatronymStyle.Prefix,
        PatronymMale = ["mak", "ap"],
        PatronymFemale = ["nik", "ferkh"],
        Particles = ["=of"],
        Adjectival = ["ic", "ish"],
    };

    public static readonly LanguageFlavour Steppe = new()
    {
        Name = "Steppe",
        Consonants = Sounds("b:3 t:4 d:3 k:4 g:3 q:2 s:4 z:1 sh:2 ch:2 j:1 m:3 n:4 ng:1 l:4 r:4 y:3 gh:1 h:1"),
        Vowels = Sounds("a:5 e:3 i:3 o:3 u:3 ü:2 oe:2 eh:1 ai:1"),
        Templates = [0.8, 4, 5, 0, 0, 1, 0.8, 0],
        CodaFamilies = CodaFamily.NasalStop | CodaFamily.LiquidObstruent | CodaFamily.LiquidNasal | CodaFamily.SStop,
        CodaSet = W("t k s sh ch m n ng l r z y q gh"),
        MaxBoundaryCluster = 2,
        Harmony = true,
        InitialVowel = 0.25,
        FinalOpen = 0.35,
        Spelling = Sp(("q", "q k"), ("sh", "ş sh"), ("ch", "ç ch"), ("j", "c j"), ("gh", "ğ gh"), ("ü", "ü"), ("oe", "ö"), ("eh", "ı i"), ("ai", "ay")),
        Barony = W("kent kurgan tau su bulak tash bashi kol abad orda kaya oba"),
        County = W("el yurt"),
        Duchy = W("stan el"),
        Kingdom = W("stan el"),
        Folk = W("lar ler"),
        MaleEndings = W("bek han tai gir er bai"),
        FemaleEndings = W("gül ai su naz"),
        FeminineMarkers = W("gül ai"),
        Proto = W("ar tim kut il bai tug boer kara ak tem"),
        MaleDeutero = W("bek han tai gir tegin buga"),
        FemaleDeutero = W("gül ai su naz khan"),
        Dithematic = 0.4,
        RootEnding = 0.5,
        Patronym = PatronymStyle.Suffix,
        PatronymMale = ["oghlu"],
        PatronymFemale = ["kehzeh"],
        Particles = ["=of"],
        Adjectival = ["ic", "i"],
    };

    public static readonly LanguageFlavour Finnic = new()
    {
        Name = "Finnic",
        Consonants = Sounds("p:4 t:5 k:5 s:5 h:3 m:4 n:5 l:5 r:4 v:3 y:2 d:1?"),
        Vowels = Sounds("a:5 e:4 i:5 o:3 u:4 ü:2 ae:3 oe:2 aa:2 ii:2 uu:1 ee:1 oo:1 ai:1 ei:1"),
        Templates = [1, 6, 3, 0, 0, 0.8, 0, 0],
        CodaFamilies = CodaFamily.NasalStop | CodaFamily.LiquidObstruent | CodaFamily.SStop | CodaFamily.LiquidNasal,
        CodaSet = W("n s t l r k m"),
        MaxBoundaryCluster = 2,
        Hiatus = true,
        Gemination = true,
        Harmony = true,
        InitialVowel = 0.2,
        FinalOpen = 0.75,
        Spelling = Sp(("ü", "y"), ("ae", "ä"), ("oe", "ö"), ("y", "j")),
        LongVowelStyles = [Orthography.LongStyle.Double],
        Barony = W("la yaervi koski maeki saari niemi yoki lahti salo vaara külae ranta"),
        County = W("maa"),
        Duchy = W("maa"),
        Kingdom = W("maa la"),
        Folk = W("laiset"),
        MaleEndings = W("o u kki mo ni"),
        FemaleEndings = W("a i kki tar"),
        FeminineMarkers = W("a"),
        Dithematic = 0.2,
        RootEnding = 0.6,
        Linkers = ["", "n"],
        Patronym = PatronymStyle.Suffix,
        PatronymMale = ["poika"],
        PatronymFemale = ["tütaer"],
        Particles = ["=of"],
        Adjectival = ["ic", "ish"],
    };

    public static readonly LanguageFlavour Sanskritic = new()
    {
        Name = "Sanskritic",
        Consonants = Sounds("p:3 b:3 t:4 d:4 k:4 g:3 kh:1 gh:1 th:1 dh:2 s:4 sh:2 h:3 m:4 n:5 l:3 r:5 v:4 y:3 j:2 ch:2"),
        Vowels = Sounds("a:7 i:4 u:3 aa:3 ii:1 uu:1 e:2 o:2 ai:1 au:1"),
        Templates = [0.6, 5, 3, 1, 0.8, 0.5, 0, 0],
        OnsetFamilies = OnsetFamily.ObstruentLiquid | OnsetFamily.SStop | OnsetFamily.ObstruentGlide,
        CodaFamilies = CodaFamily.NasalStop | CodaFamily.LiquidObstruent | CodaFamily.SStop,
        CodaSet = W("n m r s t k l"),
        MaxBoundaryCluster = 3,
        StopStopOk = true,
        InitialVowel = 0.2,
        FinalOpen = 0.5,
        Spelling = Sp(("sh", "sh ś"), ("kh", "kh"), ("gh", "gh"), ("th", "th"), ("dh", "dh")),
        LongVowelStyles = [Orthography.LongStyle.Plain, Orthography.LongStyle.Macron],
        Barony = W("pur pura nagar garh abad kot grama desh patan"),
        County = W("desh pur"),
        Duchy = W("desh rashtra"),
        Kingdom = W("desh rashtra rajya"),
        Folk = W("i a"),
        MaleEndings = W("a deva sena varman pala dat.ta"),
        FemaleEndings = W("i devi mati shri a"),
        FeminineMarkers = W("a i"),
        Proto = W("indra chandra surya deva dharma vira ratna jaya rama b.hadra"),
        MaleDeutero = W("deva sena varman pala dat.ta gupta"),
        FemaleDeutero = W("devi mati shri vati"),
        Dithematic = 0.5,
        RootEnding = 0.4,
        Patronym = PatronymStyle.Suffix,
        PatronymMale = ["putra"],
        PatronymFemale = ["putri"],
        Particles = ["=of"],
        Adjectival = ["i", "ic", "an"],
    };

    public static readonly LanguageFlavour Iberic = new()
    {
        Name = "Iberic",
        Consonants = Sounds("p:2 b:3 t:4 d:2 k:4 g:3 s:4 z:2 ts:2 ch:1 m:3 n:5 l:4 r:5 h:1 ny:1?"),
        Vowels = Sounds("a:6 e:5 i:4 o:4 u:3 ai:1 au:1 ei:1"),
        Templates = [1, 5, 3, 0.3, 0, 1, 0, 0],
        OnsetFamilies = OnsetFamily.ObstruentLiquid,
        CodaFamilies = CodaFamily.NasalStop | CodaFamily.LiquidObstruent | CodaFamily.SStop,
        CodaSet = W("n r s ts l k t z"),
        ClusterKeep = 0.4,
        MaxBoundaryCluster = 2,
        Hiatus = true,
        InitialVowel = 0.3,
        FinalOpen = 0.6,
        Spelling = Sp(("ts", "tz ts"), ("ch", "tx"), ("y", "i"), ("ny", "ñ")),
        Barony = W("aga eta ola uri tegi zar bide berri gorri mendi ondo alde"),
        County = W("alde erri"),
        Duchy = W("erri ia"),
        Kingdom = W("ia erri"),
        Folk = W("tar ar"),
        MaleEndings = W("ko ts er ander on"),
        FemaleEndings = W("a ne ika i"),
        FeminineMarkers = W("a ne"),
        Dithematic = 0.1,
        RootEnding = 0.7,
        Patronym = PatronymStyle.Suffix,
        PatronymMale = ["ez"],
        PatronymFemale = ["ez"],
        Particles = ["de"],
        Adjectival = ["an", "ic"],
    };

    public static readonly LanguageFlavour Insular = new()
    {
        Name = "Insular",
        Consonants = Sounds("k:5 s:4 t:4 n:5 h:3 m:4 y:3 r:4 w:1 g:2 z:1 d:1 b:1 sh:2 ch:1 j:1"),
        Vowels = Sounds("a:5 i:5 u:4 e:3 o:5 aa:0.5 oo:1"),
        Templates = [1.2, 8, 1, 0, 0, 0.3, 0, 0],
        OnsetFamilies = OnsetFamily.NasalGlide,
        CodaSet = W("n"),
        MaxBoundaryCluster = 2,
        Hiatus = true,
        Gemination = true,
        InitialVowel = 0.3,
        FinalOpen = 0.8,
        LongVowelStyles = [Orthography.LongStyle.Double, Orthography.LongStyle.Plain, Orthography.LongStyle.Macron],
        Barony = W("yama kawa shima mura saka hara zaki ta no oka moto bashi da"),
        County = W("gun no"),
        Duchy = W("kuni shuu"),
        Kingdom = W("koku kuni"),
        Folk = W("jin"),
        MaleEndings = W("ro shi to ki maru o"),
        FemaleEndings = W("ko mi na e yo"),
        FeminineMarkers = W("ko"),
        Dithematic = 0.3,
        RootEnding = 0.6,
        Linkers = ["", "no"],
        Patronym = PatronymStyle.None,
        Particles = ["no"],
        Adjectival = ["ese", "ic"],
    };

    public static readonly LanguageFlavour Savanna = new()
    {
        Name = "Savanna",
        Consonants = Sounds("b:4 m:5 n:5 k:4 t:3 d:3 g:3 l:4 w:3 y:3 s:3 z:2 sh:1? j:2 f:1? ng:1 ny:2"),
        Vowels = Sounds("a:6 e:4 i:4 o:4 u:4"),
        Templates = [1.5, 8, 0.3, 1.5, 0, 0.2, 0, 0],
        OnsetFamilies = OnsetFamily.Prenasal | OnsetFamily.ObstruentGlide,
        CodaSet = W("n"),
        ClusterKeep = 0.8,
        MaxBoundaryCluster = 2,
        Hiatus = true,
        InitialVowel = 0.3,
        FinalOpen = 0.95,
        Spelling = Sp(("ny", "ny"), ("y", "y")),
        PrefixingChance = 0.7,
        Prefixes = W("ki ma mu wa ba lu bu"),
        Barony = W("la ni ka to ngo ba mba nde zi"),
        County = W("ni ngo"),
        Duchy = W("ngo la"),
        Kingdom = W("ngo la"),
        Folk = W("wa ba"),
        MaleEndings = W("a o u we ndi"),
        FemaleEndings = W("a e i wa"),
        FeminineMarkers = W("a"),
        Dithematic = 0.2,
        RootEnding = 0.6,
        Patronym = PatronymStyle.Suffix,
        Particles = ["wa"],
        Adjectival = ["an", "i"],
    };

    public static readonly LanguageFlavour Sylvan = new()
    {
        Name = "Sylvan",
        Fantasy = true,
        Consonants = Sounds("l:6 r:5 n:5 m:3 th:3 dh:1 s:3 v:3 f:2 d:3 t:3 g:2 k:1 h:2 y:2 w:1"),
        Vowels = Sounds("a:5 e:5 i:5 o:3 u:2 ae:2 ai:2 ie:1 ia:1 ei:1 aa:1 ii:1"),
        Templates = [1.2, 5, 3, 0.7, 0.4, 0.8, 0, 0],
        OnsetFamilies = OnsetFamily.ObstruentLiquid,
        CodaFamilies = CodaFamily.LiquidObstruent | CodaFamily.LiquidNasal | CodaFamily.NasalStop,
        CodaSet = W("l r n m th s d"),
        MaxBoundaryCluster = 2,
        Hiatus = true,
        InitialVowel = 0.3,
        FinalOpen = 0.5,
        Spelling = Sp(("k", "c k"), ("ae", "ae ä")),
        KFrontOptions = ["c"],
        LongVowelStyles = [Orthography.LongStyle.Plain, Orthography.LongStyle.Acute],
        Barony = W("iel lorn dor ath ien ost lin dal ael ith oth mar rond wen"),
        County = W("ien dor"),
        Duchy = W("dor ion"),
        Kingdom = W("dor ion ath"),
        Folk = W("im rim"),
        MaleEndings = W("iel ion dir las thir an ir"),
        FemaleEndings = W("iel wen ith ia ael is"),
        FeminineMarkers = W("ia el"),
        Dithematic = 0.5,
        RootEnding = 0.4,
        Linkers = ["", "e"],
        Patronym = PatronymStyle.Suffix,
        Adjectival = ["in", "ic", "ish"],
    };

    public static readonly LanguageFlavour Dwarven = new()
    {
        Name = "Dwarven",
        Fantasy = true,
        Consonants = Sounds("d:5 t:3 k:5 g:4 b:3 r:6 n:4 m:4 z:2 th:2 dh:1 kh:2 gh:1 h:2 l:3 v:1 f:1 s:2"),
        Vowels = Sounds("a:5 u:4 o:3 i:3 e:2 aa:1 uu:1 oo:1 ai:0.5"),
        Templates = [0.4, 3, 5, 0.8, 1.5, 0.8, 1, 0.5],
        OnsetFamilies = OnsetFamily.ObstruentLiquid | OnsetFamily.SStop | OnsetFamily.SNasal,
        CodaFamilies = CodaFamily.NasalStop | CodaFamily.LiquidObstruent | CodaFamily.LiquidNasal | CodaFamily.SStop | CodaFamily.FricativeStop,
        CodaSet = W("d t k g b r n m z th kh l s"),
        MaxBoundaryCluster = 3,
        Gemination = true,
        StopStopOk = true,
        InitialVowel = 0.12,
        FinalOpen = 0.3,
        Spelling = Sp(("y", "j")),
        LongVowelStyles = [Orthography.LongStyle.Double, Orthography.LongStyle.Acute, Orthography.LongStyle.Plain],
        Barony = W("dum khaz dur bar grond heim gard zad morn dal delv drum"),
        County = W("gard rak"),
        Duchy = W("dum gard"),
        Kingdom = W("dum gard zad"),
        Folk = W("ar im"),
        MaleEndings = W("in ur grim li din rik nar"),
        FemaleEndings = W("a dis run hild is"),
        FeminineMarkers = W("a"),
        Dithematic = 0.45,
        RootEnding = 0.45,
        Linkers = ["", "a"],
        Patronym = PatronymStyle.Suffix,
        Adjectival = ["ish", "ic"],
    };

    public static readonly LanguageFlavour Harsh = new()
    {
        Name = "Harsh",
        Fantasy = true,
        Consonants = Sounds("g:6 k:5 r:5 z:4 gh:3 kh:3 sh:3 d:3 b:3 m:3 n:3 t:2 th:1 ':1 l:1"),
        Vowels = Sounds("a:6 u:5 o:3 i:2 e:1 aa:0.5 uu:0.5"),
        Templates = [0.3, 2, 6, 0.6, 1.5, 0.7, 1.2, 0.6],
        OnsetFamilies = OnsetFamily.ObstruentLiquid | OnsetFamily.Eastern,
        CodaFamilies = CodaFamily.NasalStop | CodaFamily.LiquidObstruent | CodaFamily.LiquidNasal | CodaFamily.SStop | CodaFamily.FricativeStop,
        CodaSet = W("g k r z gh kh sh d b m n t th l"),
        MaxBoundaryCluster = 3,
        StopStopOk = true,
        InitialVowel = 0.1,
        FinalOpen = 0.2,
        Spelling = Sp(("kh", "kh kh k'"), ("'", "'")),
        LongVowelStyles = [Orthography.LongStyle.Double, Orthography.LongStyle.Plain],
        Barony = W("gash zug mog rok uk thak gul drak zar gor kul burz"),
        County = W("gor ak"),
        Duchy = W("gul ak"),
        Kingdom = W("gor gul"),
        Folk = W("ai uk"),
        MaleEndings = W("gash uk rok zug thak mog"),
        FemaleEndings = W("ka sha zra ug ith"),
        FeminineMarkers = W("ka"),
        Dithematic = 0.35,
        RootEnding = 0.5,
        Patronym = PatronymStyle.Suffix,
        Adjectival = ["ish", "ic"],
    };

    public static readonly LanguageFlavour[] All =
    [
        Anglic, Norse, Germanic, Latinate, Hellenic, Slavic, Desert, Celtic, Steppe, Finnic,
        Sanskritic, Iberic, Insular, Savanna, Sylvan, Dwarven, Harsh,
    ];

    public static LanguageFlavour? ByName(string name)
        => All.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The flavours a world's theme calls for, with weights. A themed world draws only from its own
    /// region; the unthemed default draws from every real-world flavour and, on a fantasy world,
    /// from the invented ones as well — rarely on low fantasy, freely on high.
    /// </summary>
    public static List<(LanguageFlavour Flavour, double Weight)> Pool(MapConfig cfg)
    {
        var pool = new List<(LanguageFlavour, double)>();

        void Add(LanguageFlavour f, double w = 1.0) => pool.Add((f, w));

        switch (cfg.CultureAestheticsTheme)
        {
            case MapConfig.CultureLookTheme.WesternEuropean:
                Add(Anglic); Add(Norse, 0.6); Add(Germanic); Add(Latinate); Add(Celtic); Add(Iberic, 0.6);
                break;
            case MapConfig.CultureLookTheme.NorthernNorse:
                Add(Norse, 1.5); Add(Anglic); Add(Finnic); Add(Germanic, 0.7); Add(Celtic, 0.5);
                break;
            case MapConfig.CultureLookTheme.ByzantineGreek:
                Add(Hellenic, 1.5); Add(Latinate); Add(Slavic); Add(Desert, 0.5);
                break;
            case MapConfig.CultureLookTheme.MiddleEasternMena:
                Add(Desert, 1.5); Add(Hellenic, 0.5); Add(Steppe, 0.6); Add(Sanskritic, 0.6);
                break;
            case MapConfig.CultureLookTheme.SteppeNomadic:
                Add(Steppe, 1.5); Add(Slavic, 0.7); Add(Finnic, 0.6); Add(Sanskritic, 0.4);
                break;
            case MapConfig.CultureLookTheme.SubSaharanAfrican:
                Add(Savanna, 1.5); Add(Desert, 0.6);
                break;
            case MapConfig.CultureLookTheme.IndianEastAsian:
                Add(Sanskritic, 1.5); Add(Insular); Add(Steppe, 0.5);
                break;
            default:
                foreach (var f in All) if (!f.Fantasy) Add(f);
                break;
        }

        if (cfg.EnableFantasyEthnicities)
        {
            double w = cfg.RaceMode == MapConfig.FantasyRaceMode.HighFantasy ? 1.2 : 0.4;
            Add(Sylvan, w); Add(Dwarven, w); Add(Harsh, w);
        }

        return pool;
    }

    public static LanguageFlavour Pick(MapConfig cfg, Rng rng)
    {
        var pool = Pool(cfg);
        double total = pool.Sum(p => p.Weight);
        double roll = rng.Double() * total;
        foreach (var (flavour, weight) in pool)
        {
            roll -= weight;
            if (roll < 0) return flavour;
        }
        return pool[^1].Flavour;
    }

    /// <summary>Whether the first heritage of a world of this theme should speak the Anglic tongue.</summary>
    public static bool AnglicFirst(MapConfig cfg) => cfg.CultureAestheticsTheme is
        MapConfig.CultureLookTheme.VariedGlobal or MapConfig.CultureLookTheme.WesternEuropean
        or MapConfig.CultureLookTheme.NorthernNorse;
}
