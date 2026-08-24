using Ck3MapGen.Core;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// A second character shown beside a bookmark character — the liege he answers to, the rival who
/// wants his lands, his heir, his wife. CK3 nests these inside the main <c>character = {}</c> block
/// with a <c>relation</c> key, and draws them as small portraits in the bookmark panel.
///
/// Nothing about a companion is decided here: it holds the person and the word for how the two are
/// related, and reads every field off the same object <see cref="HistoryWriter"/> writes. Held
/// rather than copied so that an edit made after the mod was written reaches both files — a ruler
/// renamed in the inspector is renamed in the panel beside his vassal too.
/// </summary>
public sealed record BookmarkCompanion(
    string Key,
    string Relation,
    Ruler? Ruler,
    HistoricalCharacter? Character,
    bool Child,
    Culture Culture,
    string FallbackAnimation)
{
    public string Name => Ruler is not null
        ? BookmarkCast.DisplayName(Ruler)
        : ParadoxText.Loc(Character!.Name);

    public string HistoryId => Ruler?.Id ?? Character!.Id;
    public string? DynastyHouseKey => Ruler?.HouseKey ?? Character!.DynastyHouseKey;
    public string DynastyId => Ruler?.DynastyId ?? Character!.DynastyId;
    public bool Female => Ruler?.Female ?? Character!.Female;
    public string BirthDate => Ruler?.BirthDate ?? Character!.BirthDate;
    public string FaithKey => Ruler?.Faith.Key ?? Character!.FaithKey;

    /// <summary>A ruler poses from his own traits; a wife or an heir has none written down.</summary>
    public string Animation => Ruler is not null ? BookmarkCast.AnimationFor(Ruler) : FallbackAnimation;
}

/// <summary>
/// One character on the bookmark screen.
///
/// Everything the screen says is a property computed from <see cref="Facts"/> rather than a string
/// captured when the cast was chosen. That is what lets a ruler edited in the inspector after the
/// mod was written be re-emitted correctly: <see cref="BookmarkWriter.ReWrite"/> replays the same
/// slots, and each one describes the ruler as he is now. Frozen strings gave a bookmark that
/// followed a birthday edit but kept the old name.
/// </summary>
public sealed record BookmarkSlot(
    string Key,
    BookmarkCast.BookmarkFacts Facts,
    IReadOnlyList<BookmarkCompanion> Companions,
    int ScreenX,
    int ScreenY)
{
    public Ruler Ruler => Facts.Ruler;

    /// <summary>The seat, which is what every other map of the world is keyed by.</summary>
    public Title County => Ruler.Seat;

    public string DisplayName => BookmarkCast.DisplayName(Ruler);
    public string Subheading => BookmarkCast.Epithet(Facts);
    public string Description => BookmarkCast.Describe(Facts);
    public string Difficulty => Facts.DifficultyKey;
    public string Animation => BookmarkCast.AnimationFor(Ruler);
}

/// <summary>
/// Who stands on the bookmark screen, and what is true about them.
///
/// Split out of <see cref="BookmarkWriter"/> for the same reason <see cref="RulerProfile"/> was
/// split out of <see cref="HistoryWriter"/>: none of it is about writing files. The writer owns the
/// two Paradox formats; this owns the choice of characters and every claim the screen makes about
/// them, which is the part that can be wrong.
///
/// It used to be wrong in a specific way. Selection filled five fixed archetype slots, and three of
/// the five pools ended in <c>.Concat(playable)</c> — so when no tribe, no vassal or no march was
/// free, the slot took whoever was left and kept the archetype's prose. "A cunning noble serving
/// beneath an overlord" would sit under an independent king; the warlord slot, whose fallback had no
/// playability filter at all, once landed on an administrative ruler and called him a clan chief.
/// Nothing here reads a slot index to decide what to say any more: the label is derived from the
/// ruler who ended up in the slot, so a filler is described as whatever he actually is.
/// </summary>
public sealed class BookmarkCast
{
    /// <summary>The five characters on the bookmark screen itself.</summary>
    public required List<BookmarkSlot> Slots { get; init; }

    /// <summary>
    /// The challenge tab's character — a sixth ruler, and deliberately not one of the five. This
    /// used to be <c>Slots.Last()</c>, which put the same man on two tabs under two loc keys and
    /// left his portrait DNA fighting over one character.
    /// </summary>
    public required BookmarkSlot Challenge { get; init; }

    public IEnumerable<BookmarkSlot> All => Slots.Append(Challenge);

    private const double MinPortraitDistance = 260.0; // Minimum pixel separation on 1920x1080 screen

    /// <summary>The five slot keys, in the order the screen lists them.</summary>
    private static readonly string[] SlotKeys =
    [
        "bm_char_hegemon", "bm_char_frontier", "bm_char_vassal", "bm_char_magnate", "bm_char_warlord",
    ];

    /// <summary>
    /// Picks the cast and works out what to say about each of them.
    ///
    /// <paramref name="seats"/> is already narrowed to counties <see cref="RulerMap"/> wrote a
    /// character for — a bookmark pointing anywhere else is a <c>history_id</c> nobody wrote, which
    /// is the missing-item error this used to produce. Returns null when the world has no playable
    /// seat at all.
    /// </summary>
    public static BookmarkCast? Build(
        List<Title> seats, RealmMap realms, GovernmentMap governments,
        Dictionary<Title, int> development, WildernessMap wilderness,
        PrehistoryMap prehistory, RulerMap rulers, CultureMap cultures, int startYear,
        Dictionary<Title, (int X, int Y)> positions)
    {
        var playable = realms.Greatest
            .Where(c => rulers.Contains(c) && IsPlayable(governments.For(c)))
            .ToList();

        // A world of nothing but republics and theocracies still gets a bookmark screen; better a
        // start the player would not have chosen than a tab onto nothing.
        if (playable.Count == 0) playable = realms.Greatest.Where(rulers.Contains).ToList();
        if (playable.Count == 0) playable = seats.Where(rulers.Contains).ToList();
        if (playable.Count == 0) return null;

        var realmSizes = RealmSizes(realms);
        var frontier = FrontierSeats(playable, wilderness);
        var wealthy = WealthySeats(playable, development);

        // The single largest realm on the map, which is the one claim "master of the realm" can be
        // made on. Measured, not inferred from tier: a king holding half a continent outranks an
        // emperor with three counties, whatever their crowns say.
        var greatest = realmSizes.Count == 0
            ? null
            : realmSizes.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key.Index).First().Key;

        // The strict pools. Nothing falls back into another archetype's pool: an empty one simply
        // hands the slot to the fallback below, which is honest about who it picked.
        var pools = new Dictionary<string, List<Title>>
        {
            ["bm_char_hegemon"] = playable,
            ["bm_char_frontier"] = playable.Where(frontier.Contains).ToList(),
            ["bm_char_vassal"] = playable.Where(c => realms.Liege.ContainsKey(HistoryWriter.Primary(c, realms))).ToList(),
            ["bm_char_magnate"] = playable.OrderByDescending(c => development.GetValueOrDefault(c, 0)).ToList(),
            ["bm_char_warlord"] = playable.Where(c => governments.For(c) == GovernmentMap.Tribal).ToList(),
        };

        var chosen = new List<(string Key, Title County, int X, int Y)>();
        var used = new HashSet<Title>();

        foreach (string key in SlotKeys)
        {
            var pick = PickSpaced(pools[key], used, chosen, positions)
                    ?? PickSpaced(playable, used, chosen, positions);

            if (pick is null) break; // fewer seats than slots — a very small world

            used.Add(pick);
            var (x, y) = positions.GetValueOrDefault(pick, (960, 540));
            chosen.Add((key, pick, x, y));
        }

        if (chosen.Count == 0) return null;

        RelaxScreenPositions(chosen);

        var facts = new Dictionary<Title, BookmarkFacts>();
        BookmarkFacts FactsFor(Title seat) => facts.TryGetValue(seat, out var f)
            ? f
            : facts[seat] = BookmarkFacts.For(rulers.For(seat), realms, governments, rulers, prehistory,
                                              realmSizes, frontier.Contains(seat), wealthy.Contains(seat),
                                              seat == greatest);

        var slots = chosen
            .Select(c => Compose(c.Key, FactsFor(c.County), c.X, c.Y, prehistory, cultures, startYear))
            .ToList();

        // The challenge character: the hardest honest start left on the map. Graded over every
        // remaining seat rather than picked from a pool, because "hard" is a property of the
        // situation and not of an archetype.
        var challengeSeat = playable
            .Where(c => !used.Contains(c))
            .OrderByDescending(c => FactsFor(c).Hardship)
            .ThenBy(c => c.Index)
            .FirstOrDefault();

        // Only a world with five or fewer playable seats gets here, and then the last slot doubles
        // as the challenge — which is what every run used to do. Recomposed rather than copied, so
        // its companions get keys of their own instead of a second file naming the warlord's.
        var challenge = Compose(
            BookmarkWriter.ChallengeCharacter,
            FactsFor(challengeSeat ?? slots[^1].County),
            0, 0, prehistory, cultures, startYear);

        return new BookmarkCast { Slots = slots, Challenge = challenge };
    }

    // --- What is true about one ruler -------------------------------------------------------

    /// <summary>
    /// Everything the screen is allowed to claim about a ruler, read by the epithet, the description,
    /// the difficulty grade and the companion list alike — so those four can never disagree with
    /// each other about the same man.
    ///
    /// The structural facts are worked out once, because a ruler edit cannot change who his liege is
    /// or how many counties answer to him. <see cref="Hardship"/> is not among them: it reads the
    /// profile, which the inspector can change, so it is graded on demand.
    /// </summary>
    public sealed record BookmarkFacts(
        Ruler Ruler,
        Ruler? Liege,
        int RealmCounties,
        int Vassals,
        bool Frontier,
        bool Wealthy,
        bool Greatest,
        string Government,
        ActiveWar? War,
        Ruler? Rival,
        Ruler? Ally,
        Title? PressedClaim)
    {
        /// <summary>True when the man he has fallen out with is the man he answers to.</summary>
        public bool RivalIsLiege => Rival is not null && Liege is not null && Rival.Id == Liege.Id;

        internal static BookmarkFacts For(
            Ruler ruler, RealmMap realms, GovernmentMap governments, RulerMap rulers,
            PrehistoryMap prehistory, Dictionary<Title, int> realmSizes, bool frontier, bool wealthy,
            bool greatest)
        {
            var seat = ruler.Seat;

            Ruler? liege = null;
            if (realms.Liege.TryGetValue(ruler.PrimaryTitle, out var liegeTitle)
                && realms.HolderCounty.TryGetValue(liegeTitle, out var liegeSeat)
                && liegeSeat != seat
                && rulers.TryGet(liegeSeat, out var liegeRuler))
            {
                liege = liegeRuler;
            }

            int vassals = realms.Liege.Count(kv => kv.Value == ruler.PrimaryTitle);
            int realmCounties = realmSizes.GetValueOrDefault(seat, 1);

            var war = prehistory.ActiveWars.FirstOrDefault(
                w => w.AttackerCounty == seat || w.DefenderCounty == seat);

            Ruler? rival = null;
            if (prehistory.Rivals.TryGetValue(seat, out var rivals))
            {
                foreach (var r in rivals)
                {
                    if (rulers.TryGet(r.TargetCounty, out var other)) { rival = other; break; }
                }
            }

            Ruler? ally = null;
            if (prehistory.Alliances.TryGetValue(seat, out var alliances))
            {
                foreach (var a in alliances)
                {
                    if (rulers.TryGet(a.PartnerCounty, out var other)) { ally = other; break; }
                }
            }

            Title? claim = null;
            if (prehistory.Claims.TryGetValue(seat, out var claims))
            {
                foreach (var c in claims)
                {
                    if (c.Pressed) { claim = c.TargetTitle; break; }
                }
            }

            string government = governments.For(seat);

            return new BookmarkFacts(ruler, liege, realmCounties, vassals, frontier, wealthy,
                                     greatest, government, war, rival, ally, claim);
        }

        /// <summary>
        /// How steep this start is, in points. Everything counted here is something the player will
        /// meet in the first year: who he answers to, how much land there is to answer with, whether
        /// a war is already running, and whether the man he has fallen out with outranks him.
        /// </summary>
        public int Hardship
        {
            get
            {
                int rank = HistoryWriter.Rank(Ruler.PrimaryTitle);
                int hardship = rank switch { 4 => -1, 3 => 0, 2 => 1, _ => 2 };

                if (Liege is not null) hardship += 2;
                if (RealmCounties <= 2) hardship += 2;
                else if (RealmCounties <= 5) hardship += 1;
                else if (RealmCounties >= 15) hardship -= 1;

                if (War is not null) hardship += 2;
                if (Rival is not null && HistoryWriter.Rank(Rival.PrimaryTitle) > rank) hardship += 1;
                if (Government == GovernmentMap.Tribal) hardship += 1;

                // Skills are rolled against tier, so they are graded against tier too — otherwise
                // this would just be counting the crown twice.
                var p = Ruler.Profile;
                int total = p.Diplomacy + p.Martial + p.Stewardship + p.Intrigue + p.Learning;
                int expected = rank switch { 4 => 43, 3 => 38, 2 => 30, _ => 25 };
                if (total < expected - 5) hardship += 1;
                else if (total > expected + 5) hardship -= 1;

                return hardship;
            }
        }

        public string DifficultyKey => Hardship switch
        {
            <= 0 => "BOOKMARK_CHARACTER_DIFFICULTY_EASY",
            <= 3 => "BOOKMARK_CHARACTER_DIFFICULTY_MEDIUM",
            _ => "BOOKMARK_CHARACTER_DIFFICULTY_HARD",
        };
    }

    // --- Saying it --------------------------------------------------------------------------

    private static BookmarkSlot Compose(
        string key, BookmarkFacts f, int x, int y, PrehistoryMap prehistory, CultureMap cultures,
        int startYear) =>
        new(Key: key,
            Facts: f,
            Companions: Companions(key, f, prehistory, cultures, startYear),
            ScreenX: x,
            ScreenY: y);

    /// <summary>
    /// The name as the character sheet will show it, byname and all. <see cref="HistoryWriter"/>
    /// gives the nickname on the start date, so a bookmark reading the bare first name was showing
    /// a different name from the one the player meets a second later. The key is vanilla's own, so
    /// <c>$nick_the_bold$</c> resolves to whatever the game calls it.
    /// </summary>
    internal static string DisplayName(Ruler ruler)
    {
        string name = ParadoxText.Loc(ruler.Name);
        return ruler.Profile.Nickname is { } nick ? $"{name} ${nick}$" : name;
    }

    /// <summary>
    /// The line under the name. Every branch is gated on something that has to be true of this
    /// ruler; when none of them is, the fall-through is vanilla's own subheading, which resolves
    /// "[tier] of [title]" from the <c>title =</c> the writer emits and so cannot be wrong.
    /// </summary>
    internal static string Epithet(BookmarkFacts f) => f switch
    {
        { Greatest: true } => "Master of the Realm",

        // The liege's realm rather than his name: half the rulers on a generated map share a first
        // name with somebody, and "Sworn Man of Finu" under a character also called Finu reads like
        // an error. A realm is unambiguous.
        { Liege: { } liege } => $"Sworn Man of {ParadoxText.Loc(liege.PrimaryTitle.Name)}",

        // Not the largest realm on the map, but a great power in it. Worth its own line: the map's
        // largest realm is often an administrative empire nobody can play, so the top of the
        // bookmark screen is usually the biggest realm a player is offered rather than the biggest
        // there is, and saying "master of the realm" there would be a quiet overstatement.
        { Vassals: >= 5 } => "Lord of Many Banners",
        { Government: GovernmentMap.Tribal } => "First Among the Clans",
        { Frontier: true } => "Guardian of the Frontier",
        { Wealthy: true } => "Keeper of the Trade Routes",
        { Vassals: >= 1 } => "Answerable to No Crown",
        _ => "$BOOKMARK_SUBHEADING_DEFAULT$",
    };

    internal static string Describe(BookmarkFacts f)
    {
        // A stream of its own, salted by the seat: phrasing varies between rulers, and asking twice
        // gives the same answer both times — which is what lets the description be a property
        // rather than a string frozen when the cast was chosen.
        var rng = new Rng(f.Ruler.Seat.Index ^ 0x8B21);

        var body = new List<string> { Standing(f) };

        // The world pressing in. Each of these is written only when it is the case, and the two
        // loudest are enough — a paragraph listing six true things reads like a form.
        var pressures = new List<string>();
        if (f.War is { } war)
            pressures.Add($"A war over {ParadoxText.Loc(war.TargetTitle.Name)} is already under way.");
        if (f.RivalIsLiege)
            pressures.Add("He and the man he answers to are open enemies.");
        else if (f.Rival is { } rival)
            pressures.Add($"{ParadoxText.Loc(rival.Name)} counts him a personal enemy.");
        if (f.PressedClaim is { } claim)
            pressures.Add($"He presses a claim on {ParadoxText.Loc(claim.Name)}.");
        if (f.Frontier)
            pressures.Add("Past his borders the map gives out into unclaimed wilds.");
        if (f.Wealthy)
            pressures.Add("His lands are among the richest anyone has surveyed.");
        if (f.Government == GovernmentMap.Tribal)
            pressures.Add("His authority rests on the assent of the clans and on nothing written down.");
        if (f.Ally is { } ally)
            pressures.Add($"An alliance already stands with {ParadoxText.Loc(ally.Name)}.");

        body.AddRange(pressures.Take(2));
        body.Add(TheMan(f.Ruler));

        string hook = f.DifficultyKey switch
        {
            "BOOKMARK_CHARACTER_DIFFICULTY_EASY" => rng.Pick<string>(
                ["The board is set in your favour. What you do with it is the only question left.",
                 "Few starts on this map are this comfortable. Comfort is not the same as safety.",
                 "Everything here is already yours. Holding it is the easy half."]),
            "BOOKMARK_CHARACTER_DIFFICULTY_MEDIUM" => rng.Pick<string>(
                ["Enough to work with, and enough to lose.",
                 "Everything he holds is within reach of somebody else.",
                 "A middling hand, played against neighbours who know it."]),
            _ => rng.Pick<string>(
                ["Very little of this is in your favour.",
                 "There is no comfortable year ahead of him.",
                 "Survive the decade first. Plan afterwards."]),
        };

        return string.Join(" ", body) + $"\\n\\n#bold {hook}#!";
    }

    private static string Standing(BookmarkFacts f)
    {
        string counties = f.RealmCounties == 1 ? "a single county" : $"{f.RealmCounties} counties";

        if (f.Liege is { } liege)
        {
            string held = f.RealmCounties == 1 ? "and holds nothing beyond it" : $"and answers for {counties}";
            return $"Holds {ParadoxText.Loc(f.Ruler.PrimaryTitle.Name)} in fief of "
                 + $"{ParadoxText.Loc(liege.Name)}, {held}.";
        }

        if (f.Vassals > 0)
        {
            string vassals = f.Vassals == 1 ? "one vassal of his own" : $"{f.Vassals} vassals of his own";
            return $"Answers to nobody, and rules {counties} through {vassals}.";
        }

        return $"Answers to nobody, and rules {counties} with his own two hands.";
    }

    private static string TheMan(Ruler ruler)
    {
        var traits = ruler.Profile.PersonalityTraits;
        string bent = ruler.Profile.Lifestyle switch
        {
            RulerProfile.MartialLifestyle => "in the field",
            RulerProfile.IntrigueLifestyle => "in the quiet parts of a court",
            RulerProfile.StewardshipLifestyle => "over a ledger",
            RulerProfile.LearningLifestyle => "among books and clerics",
            _ => "across a negotiating table",
        };

        if (traits.Count >= 2)
        {
            string pair = $"{Capitalise(traits[0])} and {traits[1]}";
            return $"{pair}, he is at his sharpest {bent}.";
        }

        return $"He is at his sharpest {bent}.";
    }

    private static string Capitalise(string word) =>
        word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..];

    /// <summary>
    /// The pose the portrait strikes. Taken from the ruler's own first personality trait, because
    /// the animation is the one thing on the screen the player reads before any text — a craven
    /// count standing in <c>war_over_win</c> because he happened to land in the hegemon slot was the
    /// slot talking, not the character.
    /// </summary>
    internal static string AnimationFor(Ruler ruler)
    {
        foreach (string trait in ruler.Profile.PersonalityTraits)
        {
            string? anim = trait switch
            {
                "brave" or "ambitious" or "arrogant" => "personality_bold",
                "craven" or "shy" => "personality_coward",
                "zealous" => "personality_zealous",
                "cynical" => "personality_cynical",
                "honest" or "just" => "personality_honorable",
                "deceitful" or "arbitrary" => "personality_dishonorable",
                "callous" => "personality_callous",
                "compassionate" or "generous" => "personality_compassionate",
                "forgiving" or "trusting" => "personality_forgiving",
                "vengeful" or "wrathful" => "personality_vengeful",
                "greedy" => "personality_greedy",
                "content" or "humble" or "stubborn" => "personality_content",
                "paranoid" => "paranoia",
                "lazy" => "boredom",
                "gregarious" => "happiness",
                "fickle" or "impatient" => "personality_irrational",
                "calm" or "patient" or "diligent" or "temperate" => "personality_rational",
                _ => null,
            };

            if (anim is not null) return anim;
        }

        return ruler.Profile.Lifestyle switch
        {
            RulerProfile.MartialLifestyle => "marshal",
            RulerProfile.IntrigueLifestyle => "scheme",
            RulerProfile.StewardshipLifestyle => "survey",
            RulerProfile.LearningLifestyle => "writing",
            _ => "personality_rational",
        };
    }

    // --- Who stands beside him ----------------------------------------------------------------

    /// <summary>
    /// Up to three characters to draw beside the main portrait, in the order that says most about
    /// the start rather than most about the family: the man he answers to, the man who wants his
    /// lands, the child who inherits, the wife who brought an alliance.
    ///
    /// Every one of these is looked up, never drawn — the liege is another <see cref="Ruler"/>, and
    /// the wife and heir are <see cref="HistoricalCharacter"/>s <see cref="HistoryWriter"/> has
    /// already written with an id, a house and a birth date. Anything without a written id is
    /// skipped rather than guessed at, because a nested <c>history_id</c> pointing at nobody is the
    /// one error this file is capable of causing.
    /// </summary>
    private static List<BookmarkCompanion> Companions(
        string key, BookmarkFacts f, PrehistoryMap prehistory, CultureMap cultures, int startYear)
    {
        var found = new List<(BookmarkCompanion Companion, Action Stamp)>();

        if (f.Liege is { } liege)
            found.Add(FromRuler(liege, $"{key}_liege", "BOOKMARK_RELATION_LIEGE"));

        if (f.Rival is { } rival)
            found.Add(FromRuler(rival, $"{key}_rival", "BOOKMARK_RELATION_RIVAL"));

        if (prehistory.Children.TryGetValue(f.Ruler.Seat, out var children))
        {
            var heir = children.FirstOrDefault(c => c.IsHeir) ?? children.FirstOrDefault();
            if (heir is not null)
                found.Add(FromCharacter(heir, $"{key}_heir",
                    heir.Female ? "BOOKMARK_RELATION_DAUGHTER" : "BOOKMARK_RELATION_SON",
                    "personality_content", cultures, f.Ruler.Culture, startYear));
        }

        if (prehistory.Spouses.TryGetValue(f.Ruler.Seat, out var spouse))
            found.Add(FromCharacter(spouse, $"{key}_spouse",
                f.Ruler.Female ? "BOOKMARK_RELATION_SPOUSE_MALE" : "BOOKMARK_RELATION_WIFE",
                "personality_compassionate", cultures, f.Ruler.Culture, startYear));

        // One portrait per person. A ruler can be both the liege a vassal answers to and the rival
        // he has fallen out with — a good story, and one the description tells in words — but drawn
        // twice side by side it reads as a bug. First relation in the list above wins, which is why
        // the liege is listed first.
        var seen = new HashSet<string>();
        var kept = found.Where(c => seen.Add(c.Companion.HistoryId)).Take(3).ToList();

        // The portrait key goes on the character only once he is certain to be drawn. Stamping in
        // the factories instead left a dropped fourth companion carrying a `dna` key whose record
        // the portrait writer was never asked for, which is a missing-item error in the character
        // file — the same class of dangling reference this whole file exists to avoid.
        foreach (var (_, stamp) in kept) stamp();

        return kept.Select(c => c.Companion).ToList();
    }

    /// <summary>
    /// A companion who is himself a ruler somewhere. The DNA is stamped only if he has none: a man
    /// who is also one of the six gets his own slot's face, and <see cref="BookmarkWriter"/> points
    /// the small portrait beside his vassal at the same record rather than drawing a second one.
    /// </summary>
    private static (BookmarkCompanion, Action) FromRuler(Ruler ruler, string key, string relation)
    {
        var companion = new BookmarkCompanion(
            Key: key,
            Relation: relation,
            Ruler: ruler,
            Character: null,
            Child: false, // every ruler is drawn 24 to 50 years before the start date
            Culture: ruler.Culture,
            FallbackAnimation: "personality_rational");

        return (companion, () => ruler.DnaKey ??= $"dna_{key}");
    }

    /// <summary>
    /// A wife or an heir. These carry no DNA of their own until now — the engine rolls them from
    /// their ethnicity — so the key is stamped here and the character file writes it, which is what
    /// makes the face in the bookmark panel the face in the campaign.
    /// </summary>
    private static (BookmarkCompanion, Action) FromCharacter(
        HistoricalCharacter character, string key, string relation, string animation,
        CultureMap cultures, Culture fallback, int startYear)
    {
        var companion = new BookmarkCompanion(
            Key: key,
            Relation: relation,
            Ruler: null,
            Character: character,
            Child: BirthYear(character.BirthDate) is { } born && startYear - born < 16,
            Culture: cultures.Cultures.FirstOrDefault(c => c.Key == character.CultureKey) ?? fallback,
            FallbackAnimation: animation);

        return (companion, () => character.DnaKey = $"dna_{key}");
    }

    /// <summary>The year out of a <c>900.1.1</c> history date, or null if it is not one.</summary>
    private static int? BirthYear(string date) =>
        int.TryParse(date.Split('.')[0], out int year) ? year : null;

    // --- Picking them off the map -------------------------------------------------------------

    /// <summary>
    /// How many counties each seat's realm covers, de facto — his own, plus everything held by
    /// anyone who answers to him, all the way down. Built once for the whole map rather than walked
    /// per candidate, because the challenge grade asks it of every seat there is.
    /// </summary>
    private static Dictionary<Title, int> RealmSizes(RealmMap realms)
    {
        var held = new Dictionary<Title, int>();
        var below = new Dictionary<Title, List<Title>>();

        foreach (var (title, holder) in realms.HolderCounty)
        {
            if (title.Tier == "c") held[holder] = held.GetValueOrDefault(holder) + 1;

            if (realms.Liege.TryGetValue(title, out var liege)
                && realms.HolderCounty.TryGetValue(liege, out var liegeHolder)
                && liegeHolder != holder)
            {
                if (!below.TryGetValue(liegeHolder, out var list)) below[liegeHolder] = list = [];
                if (!list.Contains(holder)) list.Add(holder);
            }
        }

        var sizes = new Dictionary<Title, int>();
        var walking = new HashSet<Title>();

        int Size(Title seat)
        {
            if (sizes.TryGetValue(seat, out int cached)) return cached;

            // A liege table that ever loops would otherwise recurse forever. It does not today, and
            // this is what keeps it from mattering if it ever does.
            if (!walking.Add(seat)) return held.GetValueOrDefault(seat);

            int total = held.GetValueOrDefault(seat);
            foreach (var vassal in below.GetValueOrDefault(seat, [])) total += Size(vassal);

            walking.Remove(seat);
            return sizes[seat] = total;
        }

        foreach (var seat in realms.HolderCounty.Values.Distinct()) Size(seat);
        return sizes;
    }

    /// <summary>Seats sharing a duchy with wilderness — the same adjacency the writer always used.</summary>
    private static HashSet<Title> FrontierSeats(List<Title> seats, WildernessMap wilderness)
    {
        var wildParents = wilderness.Counties.Select(w => w.Parent).Where(p => p is not null).ToHashSet();
        return seats.Where(c => c.Parent is not null && wildParents.Contains(c.Parent)).ToHashSet();
    }

    /// <summary>
    /// The richest tenth of the map, which is as much as "among the richest anyone has surveyed" can
    /// honestly cover. A sixth was wide enough that two of the five bookmarks could both be keepers
    /// of the trade routes — true of both, and repetitive on a screen with five names on it.
    /// </summary>
    private static HashSet<Title> WealthySeats(List<Title> seats, Dictionary<Title, int> development)
    {
        int take = Math.Max(1, seats.Count / 10);
        return seats.OrderByDescending(c => development.GetValueOrDefault(c, 0))
                    .Take(take)
                    .ToHashSet();
    }

    private static Title? PickSpaced(
        List<Title> pool, HashSet<Title> used,
        List<(string Key, Title County, int X, int Y)> chosen,
        Dictionary<Title, (int X, int Y)> positions)
    {
        double minSq = MinPortraitDistance * MinPortraitDistance;

        // First choice: someone far enough from every portrait already on the screen.
        foreach (var candidate in pool)
        {
            if (used.Contains(candidate)) continue;

            var at = positions.GetValueOrDefault(candidate, (960, 540));
            if (chosen.All(c => DistanceSq(at, (c.X, c.Y)) >= minSq)) return candidate;
        }

        // Failing that, whoever stands furthest from the nearest one.
        return pool.Where(c => !used.Contains(c))
                   .OrderByDescending(c => chosen.Count == 0
                       ? 0
                       : chosen.Min(s => DistanceSq(positions.GetValueOrDefault(c, (960, 540)), (s.X, s.Y))))
                   .FirstOrDefault();
    }

    /// <summary>Repels overlapping coordinates so models and shields never collide.</summary>
    private static void RelaxScreenPositions(List<(string Key, Title County, int X, int Y)> slots)
    {
        const double separation = MinPortraitDistance;
        const int minX = 240, maxX = 1550;
        const int minY = 180, maxY = 840;

        for (int pass = 0; pass < 24; pass++)
        {
            bool moved = false;
            for (int i = 0; i < slots.Count; i++)
            {
                for (int j = i + 1; j < slots.Count; j++)
                {
                    double dx = slots[j].X - slots[i].X;
                    double dy = slots[j].Y - slots[i].Y;
                    double dist = Math.Sqrt(dx * dx + dy * dy);

                    if (dist >= separation) continue;

                    if (dist < 1.0) { dx = 1.0; dy = 0.0; dist = 1.0; }
                    double overlap = 0.5 * (separation - dist);
                    double nx = dx / dist * overlap;
                    double ny = dy / dist * overlap;

                    slots[i] = (slots[i].Key, slots[i].County,
                        Math.Clamp((int)Math.Round(slots[i].X - nx), minX, maxX),
                        Math.Clamp((int)Math.Round(slots[i].Y - ny), minY, maxY));
                    slots[j] = (slots[j].Key, slots[j].County,
                        Math.Clamp((int)Math.Round(slots[j].X + nx), minX, maxX),
                        Math.Clamp((int)Math.Round(slots[j].Y + ny), minY, maxY));
                    moved = true;
                }
            }

            if (!moved) break;
        }
    }

    private static double DistanceSq((int X, int Y) a, (int X, int Y) b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    private static bool IsPlayable(string government) => government
        is GovernmentMap.Feudal or GovernmentMap.Clan or GovernmentMap.Tribal;
}
