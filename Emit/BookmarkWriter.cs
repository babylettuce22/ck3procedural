using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

public static class BookmarkWriter
{
    public const string ChallengeCharacter = "challenge_character_generated";

    /// <summary>
    /// The one bookmark group this mod declares, and therefore the one date tab the frontend draws.
    ///
    /// The tabs above the bookmark list are the *groups*, not the bookmarks: the frontend's
    /// datamodel is <c>GameSetup.AccessBookmarkGroups</c>, so every group that survives loading gets
    /// a tab whether or not a single bookmark points at it. Attaching our bookmark to vanilla's
    /// <c>bm_group_867</c> therefore left 1066 and 1178 on screen as tabs onto nothing — this world
    /// has one start date and vanilla's three are not among them.
    /// </summary>
    public const string GroupKey = "bm_group_generated";

    /// <summary>
    /// The tab's second line, under the year. Its own key rather than part of
    /// <see cref="GroupKey"/>'s because the tab's year is a <c>text_single</c> capped at 155px —
    /// anything appended to the year is a wider string in a box that does not grow.
    /// <see cref="GuiWriter"/> splices the line in that reads this, and hides it when nothing
    /// resolves, which is what a run with no bookmarks at all leaves behind.
    /// </summary>
    public const string GroupSubtitleKey = GroupKey + "_sub";

    public record BookmarkResult(
        List<PortraitWriter.CharacterPortraitRequest> PortraitRequests,
        Dictionary<Title, string> BookmarkDnaMap,
        BookmarkCast? Cast
    );

    public static BookmarkResult WriteAll(
        string modDir, string gameDir, MapConfig cfg,
        ProvinceMap provinces, int[] order, List<Title> empires,
        RealmMap realms, Dictionary<Title, int> development,
        CultureMap cultures, FaithMap faiths, GovernmentMap governments,
        WildernessMap wilderness, PrehistoryMap prehistory, RulerMap rulers,
        AzgaarImport? azgaar = null, WorldCalendar? calendar = null)
    {
        // Only realm seats have a character: a liege's demesne counties and every vassal-held
        // county under one man share his seat's ruler, and the character file writes nobody for
        // them. A bookmark must point at a written character (history_id, dynasty), so the
        // candidate set is the seats — which is exactly what RulerMap holds.
        var allCounties = Titles.Flatten(empires).Where(t => t.Tier == "c").ToList();
        var seatCounties = allCounties.Where(c => !wilderness.Contains(c) && rulers.Contains(c)).ToList();

        // Ahead of the guard below: the descriptor replaces vanilla's groups whether or not this
        // run finds a county to bookmark, and a frontend with no groups at all has no tabs to
        // click. One tab pointing at nothing still beats none.
        WriteBookmarkGroup(modDir, cfg, prehistory.Eras);

        if (seatCounties.Count == 0)
        {
            return new BookmarkResult([], new Dictionary<Title, string>(), null);
        }

        // Every settled county, not just today's seats: an additional bookmark seats rulers elsewhere.
        // Each position is the county's own, so the start date's come out the same either way.
        var countyPositions = CalculateCountyScreenPositions(
            allCounties.Where(c => !wilderness.Contains(c)).ToList(), provinces, order, cfg);
        var cast = BookmarkCast.Build(seatCounties, realms, governments, development, wilderness,
                                      prehistory, rulers, cultures, cfg.StartYear, countyPositions);

        if (cast is null)
        {
            return new BookmarkResult([], new Dictionary<Title, string>(), null);
        }

        // Map county -> DNA key (e.g. "dna_bm_char_hegemon"). The challenge character is a sixth
        // ruler, so this no longer overwrites a bookmark's entry with its own — which is what left
        // `dna_bm_char_warlord` written and pointed at nobody.
        var bookmarkDnaMap = new Dictionary<Title, string>();
        foreach (var slot in cast.All) bookmarkDnaMap[slot.County] = $"dna_{slot.Key}";

        // The portrait follows the character, not the bookmark: stamping the key on the ruler is
        // what lets the character writer emit `dna =` without being handed this map.
        foreach (var (county, dnaKey) in bookmarkDnaMap)
            rulers.For(county).DnaKey = dnaKey;

        // The additional bookmarks, each cast from its own map and its own people. Relations between
        // rulers are the start date's, so none are passed down.
        var eras = prehistory.Eras;
        if (eras is not null)
        {
            foreach (var era in eras.Eras)
            {
                var eraSeats = allCounties.Where(c => !wilderness.Contains(c) && era.Rulers.Contains(c)).ToList();
                era.Cast = BookmarkCast.Build(eraSeats, era.Realms, era.Governments, development, wilderness,
                    new PrehistoryMap(), era.Rulers, cultures, era.Year, countyPositions, era.Tag);

                if (era.Cast is null) continue;
                foreach (var slot in era.Cast.All) slot.Ruler.DnaKey = $"dna_{slot.Key}";
            }
        }

        WriteBookmarks(modDir, cfg, cast, realms, cultures, faiths, governments, eras);
        WriteChallengeCharacter(modDir, cfg, cast.Challenge, realms, cultures, faiths, governments, eras);
        WriteBookmarkLocalisation(modDir, cfg, cast, azgaar, calendar,
            TabSubtitle(cfg, seatCounties, governments),
            BookmarkTitle(cast.Slots, realms, azgaar), eras, realms);
        WriteBookmarkGraphics(modDir, gameDir, eras);
        WriteRealmHighlights(modDir, cfg, provinces, order, cast.Slots, realms, empires);
        foreach (var era in eras?.Eras ?? [])
            if (era.Cast is not null)
                WriteRealmHighlights(modDir, cfg, provinces, order, era.Cast.Slots, era.Realms, empires,
                    $"bm_generated_{era.Tag}");

        Report(cast);
        foreach (var era in eras?.Eras ?? [])
            if (era.Cast is not null) Report(era.Cast);

        // A portrait for every name either file mentions, companions included. CK3 1.13 crashes on a
        // bookmark character with no record in common/bookmark_portraits — ck3-tiger grades it
        // fatal — so the nested blocks are not free to be lookups alone.
        var requests = new List<PortraitWriter.CharacterPortraitRequest>();
        foreach (var slot in cast.All)
        {
            requests.Add(new PortraitWriter.CharacterPortraitRequest(
                slot.Key, cultures.For(slot.County), slot.Ruler.Female, Tier: slot.Ruler.Tier,
                Traits: slot.Ruler.Profile.OtherTraits));
        }

        // Companions after all six, because a companion who is himself one of the six borrows that
        // slot's face rather than drawing a second one — a liege standing small beside his vassal
        // and large in his own slot is one man, and the alias is what keeps him looking like it.
        var bookmarked = cast.All.ToDictionary(s => s.Ruler.Id, s => s.Key);
        foreach (var slot in cast.All)
        {
            foreach (var mate in slot.Companions)
            {
                requests.Add(new PortraitWriter.CharacterPortraitRequest(
                    mate.Key, mate.Culture, mate.Female, mate.Child,
                    AliasOf: bookmarked.GetValueOrDefault(mate.HistoryId),

                    // A wife and an heir dress to the household they live in, so the rank that picks
                    // their wardrobe is his. A liege or a rival has a tier of his own.
                    Tier: mate.Ruler?.Tier ?? slot.Ruler.Tier,

                    // Only rulers carry a rolled profile; a wife or an heir has no congenital trait
                    // written for her, so there is nothing for the screen to be wrong about.
                    Traits: mate.Ruler?.Profile.OtherTraits));
            }
        }

        foreach (var era in eras?.Eras ?? [])
            if (era.Cast is not null) requests.AddRange(EraPortraits(era.Cast, cultures));

        return new BookmarkResult(requests, bookmarkDnaMap, cast);
    }

    /// <summary>
    /// The portraits an earlier cast needs. A companion who is also one of that screen's own
    /// rulers — a liege standing beside his vassal — borrows the face drawn for his slot.
    /// </summary>
    private static IEnumerable<PortraitWriter.CharacterPortraitRequest> EraPortraits(
        BookmarkCast cast, CultureMap cultures)
    {
        foreach (var slot in cast.All)
            yield return new PortraitWriter.CharacterPortraitRequest(
                slot.Key, cultures.For(slot.County), slot.Ruler.Female, Tier: slot.Ruler.Tier,
                Traits: slot.Ruler.Profile.OtherTraits);

        foreach (var slot in cast.All)
            foreach (var mate in slot.Companions)
            {
                string? worn = mate.Ruler?.DnaKey is { } dna && dna.StartsWith("dna_") ? dna[4..] : null;
                yield return new PortraitWriter.CharacterPortraitRequest(
                    mate.Key, mate.Culture, mate.Female, mate.Child,
                    AliasOf: worn == mate.Key ? null : worn,
                    Tier: mate.Ruler?.Tier ?? slot.Ruler.Tier,
                    Traits: mate.Ruler?.Profile.OtherTraits);
            }
    }

    /// <summary>
    /// Re-emits the three files that describe the cast, from the cast already chosen.
    ///
    /// Selection is deliberately not re-run: an edit to a ruler's name or birthday must not move
    /// who is on the bookmark screen, or the realm highlights and portraits written at generation
    /// would be pointing at people no longer on it. Everything read here is read off the
    /// <see cref="Ruler"/> objects the slots hold, which are the same objects the editor edits.
    /// </summary>
    internal static void ReWrite(string modDir, MapConfig cfg, BookmarkCast cast,
        List<Title> empires, RealmMap realms, CultureMap cultures, FaithMap faiths,
        GovernmentMap governments, WildernessMap wilderness, RulerMap rulers, AzgaarImport? azgaar,
        WorldCalendar? calendar, BookmarkEras? eras = null)
    {
        var seats = Titles.Flatten(empires)
            .Where(t => t.Tier == "c" && !wilderness.Contains(t) && rulers.Contains(t))
            .ToList();

        WriteBookmarks(modDir, cfg, cast, realms, cultures, faiths, governments, eras);
        WriteChallengeCharacter(modDir, cfg, cast.Challenge, realms, cultures, faiths, governments, eras);
        WriteBookmarkLocalisation(modDir, cfg, cast, azgaar, calendar,
            TabSubtitle(cfg, seats, governments),
            BookmarkTitle(cast.Slots, realms, azgaar), eras, realms);
    }

    private static void Report(BookmarkCast cast)
    {
        foreach (var slot in cast.All)
        {
            string tail = slot.Companions.Count == 0
                ? ""
                : $" (+{slot.Companions.Count} beside them)";
            Console.WriteLine($"  bookmark {slot.Key}: {slot.Ruler.Name} of "
                              + $"{slot.Ruler.PrimaryTitle.Name} — {Grade(slot.Difficulty)}{tail}");
        }
    }

    private static string Grade(string difficulty) => difficulty switch
    {
        "BOOKMARK_CHARACTER_DIFFICULTY_EASY" => "easy",
        "BOOKMARK_CHARACTER_DIFFICULTY_MEDIUM" => "medium",
        _ => "hard",
    };

    private static Dictionary<Title, (int X, int Y)> CalculateCountyScreenPositions(
        List<Title> counties, ProvinceMap provinces, int[] order, MapConfig cfg)
    {
        int width = cfg.ProvinceWidth;
        int height = cfg.ProvinceHeight;

        var sumX = new long[provinces.Count + 1];
        var sumY = new long[provinces.Count + 1];
        var count = new int[provinces.Count + 1];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int label = provinces.Label[y * width + x];
                if (label >= 0 && label < order.Length)
                {
                    int id = order[label];
                    if (id > 0 && id <= provinces.Count)
                    {
                        sumX[id] += x;
                        sumY[id] += y;
                        count[id]++;
                    }
                }
            }
        }

        var result = new Dictionary<Title, (int X, int Y)>();
        foreach (var county in counties)
        {
            if (county.Children.Count == 0) continue;
            int capitalProvId = county.Capital!.ProvinceId;

            int provX = width / 2;
            int provY = height / 2;

            if (capitalProvId > 0 && capitalProvId <= provinces.Count && count[capitalProvId] > 0)
            {
                provX = (int)(sumX[capitalProvId] / count[capitalProvId]);
                provY = (int)(sumY[capitalProvId] / count[capitalProvId]);
            }

            int screenX = (int)((double)provX / width * 1920.0);
            int screenY = (int)((double)provY / height * 1080.0);

            screenX = Math.Clamp(screenX, 240, 1550);
            screenY = Math.Clamp(screenY, 180, 840);

            result[county] = (screenX, screenY);
        }

        return result;
    }

    /// <summary>
    /// The splendour shield the bookmark screen draws under a portrait.
    ///
    /// Display only, and unusually so: <c>dynasty_splendor_level</c> appears nowhere in the game
    /// files except bookmarks, so there is no define to read the real thresholds back from. The
    /// ladder is graded instead against the renown <see cref="RulerMap"/> actually grants — 150-450
    /// for a count, 4000-7000 for an emperor — which is what the dynasty holds at game start.
    /// Vanilla's own bookmarks run 1 to 6 and lean low, so a crowned head lands at 3 or 4 rather
    /// than at the top of the scale.
    /// </summary>
    private static int SplendorLevel(int renown) => renown switch
    {
        < 500 => 1,
        < 1500 => 2,
        < 3500 => 3,
        _ => 4,
    };

    /// <summary>
    /// Declares the single date tab. The file name is vanilla's own so that it shadows the three
    /// vanilla groups even before <c>replace_path</c> is considered, and the directory is replaced
    /// as well so no DLC can add a fourth.
    /// </summary>
    private static void WriteBookmarkGroup(string modDir, MapConfig cfg, BookmarkEras? eras)
    {
        string dir = Path.Combine(modDir, "common", "bookmarks", "groups");
        Directory.CreateDirectory(dir);

        // One tab per date, as vanilla's 867 / 1066 / 1178 — and in date order, which is the order
        // the frontend draws them in: it lays the tabs out as the file declares the groups.
        string Group(BookmarkEra era) => $"{GroupKey}_{era.Tag} = {{\n\tdefault_start_date = {era.Date}\n}}\n\n";
        var others = eras?.Eras ?? [];

        ParadoxText.WriteBom(Path.Combine(dir, "00_bookmark_groups.txt"),
            string.Concat(others.Where(e => e.Year < cfg.StartYear).Select(Group)) + $$"""
              {{GroupKey}} = {
              	default_start_date = {{cfg.StartDate}}
              }

              """ + string.Concat(others.Where(e => e.Year > cfg.StartYear).Select(e => "\n" + Group(e).TrimEnd('\n') + "\n")));
    }

    private static void WriteBookmarks(string modDir, MapConfig cfg, BookmarkCast cast,
        RealmMap realms, CultureMap cultures, FaithMap faiths, GovernmentMap governments,
        BookmarkEras? eras)
    {
        string dir = Path.Combine(modDir, "common", "bookmarks", "bookmarks");
        Directory.CreateDirectory(dir);

        var b = new JominiBuilder();

        // Oldest first, as the tabs are. The weight is what picks the bookmark the screen opens on,
        // and that stays the start date.
        var others = (eras?.Eras ?? []).Where(e => e.Cast is not null).ToList();
        foreach (var era in others.Where(e => e.Year < cfg.StartYear))
        {
            Bookmark($"bm_generated_{era.Tag}", $"{GroupKey}_{era.Tag}", era.Date, era.Cast!, 50,
                era.Realms, era.Governments);
            b.Blank();
        }

        Bookmark("bm_generated", GroupKey, cfg.StartDate, cast, 100, realms, governments);

        foreach (var era in others.Where(e => e.Year > cfg.StartYear))
        {
            b.Blank();
            Bookmark($"bm_generated_{era.Tag}", $"{GroupKey}_{era.Tag}", era.Date, era.Cast!, 50,
                era.Realms, era.Governments);
        }

        ParadoxText.WriteBom(Path.Combine(dir, "00_bookmarks.txt"), b.ToString());

        void Bookmark(string key, string group, string date, BookmarkCast shown, int weight,
            RealmMap shownRealms, GovernmentMap shownGovernments)
        {
            using (b.Block(key))
            {
                b.Field("start_date", date);
                b.Field("is_playable", "yes");
                b.Field("group", group);
                b.Blank();

                using (b.Block("weight")) b.Field("value", weight);
                b.Blank();

                foreach (var slot in shown.Slots)
                    AppendCharacter(b, slot, shownRealms, cultures, faiths, shownGovernments, withPosition: true,
                        trailingBlank: slot != shown.Slots[^1]);
            }
        }
    }

    /// <summary>
    /// One character on the screen: the ruler himself, then the two or three people the panel draws
    /// beside him. The challenge tab uses the identical block — its own <c>.info</c> file says so in
    /// as many words — which is why both writers come through here rather than each keeping a copy
    /// of the field list to drift out of step with.
    ///
    /// Every value is read off the <see cref="Ruler"/> the character file was written from, so the
    /// two cannot disagree. <c>dynasty_house</c> rather than <c>dynasty</c> for the same reason:
    /// that is the key history puts on him, and it is what makes the screen's house tooltip resolve.
    /// </summary>
    private static void AppendCharacter(
        JominiBuilder b, BookmarkSlot slot, RealmMap realms, CultureMap cultures,
        FaithMap faiths, GovernmentMap governments, bool withPosition, bool trailingBlank = true)
    {
        var ruler = slot.Ruler;

        using (b.Block("character"))
        {
            b.Quoted("name", slot.Key);
            // A historical ruler of vanilla's may belong to a dynasty with no house of its own named
            // in history — the engine makes one — so the dynasty is what can be pointed at.
            // A lowborn one has neither, and then neither is written.
            if (ruler.HouseKey.Length > 0) b.Field("dynasty_house", ruler.HouseKey);
            else if (ruler.DynastyId.Length > 0) b.Field("dynasty", ruler.DynastyId);
            b.Field("dynasty_splendor_level", SplendorLevel(ruler.Renown));
            b.Field("type", ruler.Female ? "female" : "male");

            // The whole date, not the year. The screen prints `[BookmarkCharacter.GetAge]` beside
            // the name and works it out from this field alone, so a January stand-in showed every
            // ruler born later in the year as a year older than the character the game then loads.
            b.Field("birth", ruler.BirthDate);
            b.Field("title", HistoryWriter.Primary(slot.County, realms).Key);
            b.Field("government", governments.For(slot.County));
            // The ruler's own, which for a generated ruler is the seat's and for a historical one is
            // who vanilla says they were — the Byzantine emperor stays Greek in an Italian seat.
            b.Field("culture", ruler.Culture.Key);
            b.Field("religion", ruler.Faith.Key);
            b.Quoted("difficulty", slot.Difficulty);
            b.Field("history_id", ruler.Id);

            if (withPosition) b.Inline("position", $"{slot.ScreenX}", $"{slot.ScreenY}");
            b.Field("animation", slot.Animation);

            foreach (var mate in slot.Companions)
            {
                b.Blank();
                using (b.Block("character"))
                {
                    b.Quoted("name", mate.Key);
                    b.Quoted("relation", mate.Relation);

                    // A house is not a dynasty to CK3, and a spouse married in from outside may be
                    // in neither — the character writer makes the same choice character by
                    // character, and this follows it.
                    if (mate.DynastyHouseKey is not null) b.Field("dynasty_house", mate.DynastyHouseKey);
                    else if (mate.DynastyId.Length > 0) b.Field("dynasty", mate.DynastyId);

                    b.Field("type", mate.Female ? "female" : "male");
                    b.Field("birth", mate.BirthDate);
                    b.Field("culture", mate.Culture.Key);
                    b.Field("religion", mate.FaithKey);
                    b.Field("history_id", mate.HistoryId);
                    b.Field("animation", mate.Animation);
                }
            }
        }

        if (trailingBlank) b.Blank();
    }

    /// <summary>
    /// What this world's one date tab calls its age.
    ///
    /// Read off what the run actually produced rather than off the calendar alone, because those
    /// two are no longer the same question: <see cref="MapConfig.EraAnchorYear"/> lets a world call
    /// itself 8300 and still be as advanced as vanilla in 900, and it is the advancement the player
    /// is about to play. The government mix speaks first for the same reason — a world the
    /// generator filled with tribes is a tribal age whatever year it thinks it is.
    /// </summary>
    private static string TabSubtitle(MapConfig cfg, List<Title> seats, GovernmentMap governments)
    {
        int total = Math.Max(1, seats.Count);

        // The governments the seats actually hold, commonest first. Ties break on the key so two
        // runs of the same seed cannot disagree about which age it is. Counted by family, so a
        // world of tribes and wanua is one tribal age rather than two minorities.
        var ranked = seats
            .GroupBy(s => GovernmentMap.Family(governments.For(s)))
            .Select(g => (Key: g.Key, Count: g.Count()))
            .OrderByDescending(g => g.Count)
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .ToList();

        // Two tests, and the second is the one that matters. These worlds are mixed by
        // construction: at every year tried, the top two governments came in within a few seats of
        // each other, so a bare plurality names the age after a coin flip — at year 1250 feudal and
        // administrative tied outright, and only the tiebreak decided it. Requiring a clear margin
        // over the runner-up means the government phrase fires when the generator really did build
        // a world of one kind, and an ordinary mixed run falls through to the era below.
        //
        // Feudal is the deliberate hole in the list even so: it is the default shape of a CK3 world
        // and says less about this one than its era does.
        if (ranked.Count > 0)
        {
            var (key, count) = ranked[0];
            int runnerUp = ranked.Count > 1 ? ranked[1].Count : 0;

            if (count >= 0.4 * total && count >= runnerUp * 1.25)
            {
                switch (key)
                {
                    case GovernmentMap.Tribal: return "An Age of Chieftains";
                    case GovernmentMap.Clan: return "An Age of Clans";
                    case GovernmentMap.Nomad: return "An Age of Riders";
                    case GovernmentMap.Administrative: return "An Age of Magistrates";
                    case GovernmentMap.Republic: return "An Age of Merchant Princes";
                    case GovernmentMap.Theocracy: return "An Age of Priests";
                }
            }
        }

        // Vanilla's own era thresholds, the ones CultureWriter writes the culture eras against.
        // Sharing the numbers is the point: the tab should not call it a high medieval age while
        // the cultures underneath it are still tribal.
        return cfg.EraYear switch
        {
            < 900 => "The First Kingdoms",
            < 1050 => "An Age of Petty Kings",
            < 1200 => "An Age of Great Houses",
            _ => "The Twilight of Kings"
        };
    }

    /// <summary>
    /// What the bookmark itself is called — the line on the tab in the left-hand list, which
    /// vanilla fills with scenario names like "Wrath of The Northmen".
    ///
    /// An imported world hands its own name over; there is no sense inventing one for a map that
    /// arrived already called something. Failing that it is named for the strongest realm on it,
    /// which is the hegemon's — the first slot, and the one the greatest-realm pool picked.
    /// </summary>
    private static string BookmarkTitle(List<BookmarkSlot> bookmarks, RealmMap realms,
        AzgaarImport? azgaar)
    {
        string world = azgaar?.MapName.Trim() ?? "";
        if (world.Length > 0) return world;

        var hegemon = bookmarks.FirstOrDefault();
        string realm = hegemon is null ? "" : HistoryWriter.Primary(hegemon.County, realms).Name.Trim();

        return realm.Length > 0 ? $"The Rise of {realm}" : "Procedural Realm";
    }

    private static void WriteBookmarkLocalisation(string modDir, MapConfig cfg, BookmarkCast cast,
        AzgaarImport? azgaar, WorldCalendar? calendar, string subtitle, string title,
        BookmarkEras? eras, RealmMap realms)
    {
        string dir = Path.Combine(modDir, "localization", "english");
        Directory.CreateDirectory(dir);

        var loc = new LocFile();

        // The group key is its own loc key, and vanilla's tabs read "867" / "1066" / "1178" — a
        // bare year. The *short* era rides along when the world has one, because a world counting
        // from its own conquest wants the tab to say which calendar that number is on. Only the
        // short one: this line is the 155px-wide `text_single`, so "900 AC" fits where "900 After
        // the Conquest" does not — the full name goes on the subtitle below it instead. The same
        // suffix the game clock carries (WorldCalendar), whether the export named it or we did.
        string era = calendar?.EraShort ?? "";
        int year = Math.Max(1, cfg.StartYear);
        string dated = era.Length > 0 ? $"{year} {era}" : year.ToString();
        loc.AddBuilt(GroupKey, dated);

        // An imported world has already named its own age; nothing this generator infers from the
        // government mix beats the export saying so outright.
        string named = CompatibilityWriter.EraFullName(azgaar);
        string age = named.Length > 0 ? named : subtitle;
        loc.AddBuilt(GroupSubtitleKey, age);
        loc.Blank();

        Console.WriteLine($"  bookmark tab: {title} — {dated}, {age}"
                          + (named.Length > 0 ? " (era named by the export)" : ""));

        // The tab subtitle asks for the earlier tabs' names to tell them apart (GuiWriter), so a
        // world without them still answers — with nothing, which no real tab is called.
        foreach (string tag in (string[])["early", "middle", "late"])
            if (eras?.Eras.Any(e => e.Tag == tag && e.Cast is not null) != true)
            {
                loc.AddBuilt($"{GroupKey}_{tag}", "");
                loc.AddBuilt($"{GroupKey}_{tag}_sub", "");
            }

        loc.AddBuilt("bm_generated", title);
        loc.AddBuilt("bm_generated_desc", "Explore a newly forged world with unique cultures, faiths, and empires.");
        loc.Blank();

        // AddBuilt throughout, and escaped upstream instead: every one of these carries something
        // that must survive verbatim — a `$nick_the_bold$` byname in the display name, a
        // `[BookmarkCharacter…]` promotion in the subheading, `\n` paragraph breaks and `#bold`
        // markup in the description. The generated names inside them went through ParadoxText.Loc
        // when the cast was composed.
        Cast(cast);

        foreach (var past in eras?.Eras ?? [])
        {
            if (past.Cast is null) continue;

            string pastDated = era.Length > 0 ? $"{past.Year} {era}" : past.Year.ToString();
            loc.AddBuilt($"{GroupKey}_{past.Tag}", pastDated);

            // The age the government mix of that date speaks for, as the start date's tab does.
            var pastSeats = past.Rulers.All.Select(r => r.Seat).ToList();
            string pastAge = TabSubtitle(cfg.AtAdvancement(cfg.EraYearAt(past.Year)), pastSeats, past.Governments);
            loc.AddBuilt($"{GroupKey}_{past.Tag}_sub", pastAge);

            string pastTitle = EraTitle(past, azgaar, pastDated);
            loc.AddBuilt($"bm_generated_{past.Tag}", pastTitle);
            loc.AddBuilt($"bm_generated_{past.Tag}_desc", EraDescription(past, cfg.StartYear));
            loc.Blank();

            Console.WriteLine($"  bookmark tab: {pastTitle} — {pastDated}, {pastAge}");
            Cast(past.Cast);
        }

        loc.Write(Path.Combine(dir, "gen_history_l_english.yml"));

        void Cast(BookmarkCast shown)
        {
            foreach (var slot in shown.All)
            {
                loc.AddBuilt(slot.Key, slot.DisplayName);
                loc.AddBuilt($"{slot.Key}_subheading", slot.Subheading);
                loc.AddBuilt($"{slot.Key}_desc", slot.Description);

                // Each companion's own name key. The relation words beside them are vanilla's
                // (BOOKMARK_RELATION_LIEGE and friends), so there is nothing to write for those.
                foreach (var mate in slot.Companions) loc.AddBuilt(mate.Key, mate.Name);

                loc.Blank();
            }
        }
    }

    /// <summary>An additional bookmark's name: the export's world with its year, else its leading realm's age.</summary>
    private static string EraTitle(BookmarkEra era, AzgaarImport? azgaar, string dated)
    {
        string world = azgaar?.MapName.Trim() ?? "";
        if (world.Length > 0) return $"{world}, {dated}";

        var first = era.Cast?.Slots.FirstOrDefault();
        string realm = first is null ? "" : HistoryWriter.Primary(first.County, era.Realms).Name.Trim();
        if (realm.Length == 0) return $"Procedural Realm, {dated}";

        return era.Tag switch
        {
            "early" => $"The Dawn of {realm}",
            "late" => $"The Height of {realm}",
            _ => $"The Age of {realm}",
        };
    }

    /// <summary>
    /// What an additional bookmark is, in the counts that make it one: how far it is from the start
    /// date and which way, how many realms stand on it, and how many of their houses also rule on
    /// the start date.
    /// </summary>
    private static string EraDescription(BookmarkEra era, int startYear)
    {
        int years = Math.Abs(startYear - era.Year);
        string span = years >= 250 ? "Three centuries" : years >= 150 ? "Two centuries" : "A century";
        bool before = era.Year < startYear;
        string houses = era.Enduring switch
        {
            0 => before ? "none of their houses still rules" : "none of their houses ruled",
            1 => before ? "one of their houses still rules" : "one of their houses already ruled",
            _ => before ? $"{era.Enduring} of their houses still rule" : $"{era.Enduring} of their houses already ruled",
        };

        return $"{span} {(before ? "before" : "after")} {startYear}. {era.Independent} realms answer to no one, "
               + $"and {houses} in {startYear}.";
    }

    private static void WriteBookmarkGraphics(string modDir, string gameDir, BookmarkEras? eras)
    {
        string bookmarksDir = Path.Combine(modDir, "gfx", "interface", "bookmarks");
        string startButtonsDir = Path.Combine(bookmarksDir, "start_buttons");
        string iconsDir = Path.Combine(modDir, "gfx", "interface", "icons", "bookmark_buttons");

        Directory.CreateDirectory(bookmarksDir);
        Directory.CreateDirectory(startButtonsDir);
        Directory.CreateDirectory(iconsDir);

        string targetBmBg = Path.Combine(bookmarksDir, "bm_generated.dds");
        string flatmapSource = Path.Combine(modDir, "gfx", "map", "terrain", "flat_maps", "flatmap.dds");
        string flatmapTgpSource = Path.Combine(modDir, "gfx", "map", "terrain", "flat_maps", "flatmap_tgp.dds");

        if (File.Exists(flatmapSource))
        {
            File.Copy(flatmapSource, targetBmBg, overwrite: true);
        }
        else if (File.Exists(flatmapTgpSource))
        {
            File.Copy(flatmapTgpSource, targetBmBg, overwrite: true);
        }
        else
        {
            string? vanillaBg = Directory.Exists(Path.Combine(gameDir, "gfx", "interface", "bookmarks"))
                ? Directory.GetFiles(Path.Combine(gameDir, "gfx", "interface", "bookmarks"), "*.dds").FirstOrDefault()
                : null;
            if (vanillaBg != null)
            {
                File.Copy(vanillaBg, targetBmBg, overwrite: true);
            }
        }

        string targetStartBtn = Path.Combine(startButtonsDir, "bm_generated.dds");
        string startBtnDir = Path.Combine(gameDir, "gfx", "interface", "bookmarks", "start_buttons");
        string? vanillaStartBtn = Directory.Exists(startBtnDir)
            ? Directory.GetFiles(startBtnDir, "*.dds").FirstOrDefault()
            : null;
        if (vanillaStartBtn != null)
        {
            File.Copy(vanillaStartBtn, targetStartBtn, overwrite: true);
        }

        string targetIcon = Path.Combine(iconsDir, "bm_generated.dds");
        string iconBtnDir = Path.Combine(gameDir, "gfx", "interface", "icons", "bookmark_buttons");
        string? vanillaIcon = Directory.Exists(iconBtnDir)
            ? Directory.GetFiles(iconBtnDir, "*.dds").FirstOrDefault()
            : null;
        if (vanillaIcon != null)
        {
            File.Copy(vanillaIcon, targetIcon, overwrite: true);
        }

        // The additional bookmarks wear the same art under their own names.
        foreach (var era in eras?.Eras ?? [])
        {
            string key = $"bm_generated_{era.Tag}";
            foreach (string made in new[] { targetBmBg, targetStartBtn, targetIcon })
                if (File.Exists(made))
                    File.Copy(made, Path.Combine(Path.GetDirectoryName(made)!, $"{key}.dds"), overwrite: true);
        }
    }

    /// <summary>
    /// The challenge tab. Its character is a sixth ruler, chosen for how steep his start actually
    /// is — see <see cref="BookmarkCast"/>. It used to be whichever bookmark happened to be last in
    /// the list, which put one man on two tabs under two loc keys and left the two of them fighting
    /// over one portrait.
    /// </summary>
    private static void WriteChallengeCharacter(string modDir, MapConfig cfg, BookmarkSlot challenge,
        RealmMap realms, CultureMap cultures, FaithMap faiths, GovernmentMap governments,
        BookmarkEras? eras)
    {
        string dir = Path.Combine(modDir, "common", "bookmarks", "challenge_characters");
        Directory.CreateDirectory(dir);

        var b = new JominiBuilder();
        var others = (eras?.Eras ?? []).Where(e => e.Cast is not null).ToList();

        foreach (var era in others.Where(e => e.Year < cfg.StartYear))
        {
            Challenge(era.Cast!.Challenge, era.Date, era.Realms, era.Governments);
            b.Blank();
        }

        Challenge(challenge, cfg.StartDate, realms, governments);

        foreach (var era in others.Where(e => e.Year > cfg.StartYear))
        {
            b.Blank();
            Challenge(era.Cast!.Challenge, era.Date, era.Realms, era.Governments);
        }

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_challenge.txt"), b.ToString());

        // Keyed by the slot, which carries the era's suffix on an earlier date.
        void Challenge(BookmarkSlot slot, string date, RealmMap shownRealms, GovernmentMap shownGovernments)
        {
            using (b.Block(slot.Key))
            {
                b.Field("start_date", date);
                b.Blank();
                AppendCharacter(b, slot, shownRealms, cultures, faiths, shownGovernments,
                    withPosition: false, trailingBlank: false);
            }
        }
    }

    private static void WriteRealmHighlights(string modDir, MapConfig cfg, ProvinceMap provinces,
        int[] order, List<BookmarkSlot> bookmarks, RealmMap realms, List<Title> empires,
        string bookmarkKey = "bm_generated")
    {
        string bookmarksDir = Path.Combine(modDir, "gfx", "interface", "bookmarks");
        Directory.CreateDirectory(bookmarksDir);

        int mapW = cfg.ProvinceWidth;
        int mapH = cfg.ProvinceHeight;
        const int canvasW = 1920;
        const int canvasH = 1080;

        var allCounties = Titles.Flatten(empires).Where(t => t.Tier == "c").ToList();

        foreach (var b in bookmarks)
        {
            var primaryTitle = HistoryWriter.Primary(b.County, realms);
            var realmCounties = GetDeFactoRealmCounties(b.County, primaryTitle, realms, allCounties);

            var realmProvinces = new HashSet<int>();
            foreach (var c in realmCounties)
            {
                foreach (var barony in c.Children)
                {
                    if (barony.ProvinceId > 0) realmProvinces.Add(barony.ProvinceId);
                }
            }

            byte[] bgra = new byte[canvasW * canvasH * 4];

            for (int y = 0; y < canvasH; y++)
            {
                int srcY = Math.Clamp((int)((double)y / canvasH * mapH), 0, mapH - 1);

                for (int x = 0; x < canvasW; x++)
                {
                    int srcX = Math.Clamp((int)((double)x / canvasW * mapW), 0, mapW - 1);

                    int mapIdx = srcY * mapW + srcX;
                    int label = provinces.Label[mapIdx];
                    int provId = label >= 0 && label < order.Length ? order[label] : 0;

                    if (provId > 0 && realmProvinces.Contains(provId))
                    {
                        int dstIdx = (y * canvasW + x) * 4;
                        bgra[dstIdx + 0] = 255;
                        bgra[dstIdx + 1] = 255;
                        bgra[dstIdx + 2] = 255;
                        bgra[dstIdx + 3] = 200;
                    }
                }
            }

            string targetFile = Path.Combine(bookmarksDir, $"{bookmarkKey}_{b.Key}.dds");
            DdsWriter.WriteBgra(targetFile, canvasW, canvasH, bgra);
        }
    }

    private static HashSet<Title> GetDeFactoRealmCounties(Title rulerCounty, Title primaryTitle,
        RealmMap realms, List<Title> allCounties)
    {
        var realm = new HashSet<Title>();
        var rulersInRealm = new HashSet<Title> { rulerCounty };

        bool added;
        do
        {
            added = false;
            foreach (var (title, holder) in realms.HolderCounty)
            {
                if (rulersInRealm.Contains(holder)) continue;

                if (realms.Liege.TryGetValue(title, out var liege) && liege != null)
                {
                    if (realms.HolderCounty.TryGetValue(liege, out var liegeHolder) && rulersInRealm.Contains(liegeHolder))
                    {
                        rulersInRealm.Add(holder);
                        added = true;
                    }
                }
            }
        } while (added);

        foreach (var county in allCounties)
        {
            if (realms.HolderCounty.TryGetValue(county, out var holder) && rulersInRealm.Contains(holder))
            {
                realm.Add(county);
            }
            else if (rulersInRealm.Contains(county))
            {
                realm.Add(county);
            }
        }

        return realm;
    }
}