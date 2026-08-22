using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;
using System.Text;

namespace Ck3MapGen.Emit;

public static class BookmarkWriter
{
    public const string ChallengeCharacter = "challenge_character_generated";
    private const double MinPortraitDistance = 260.0; // Minimum pixel separation on 1920x1080 screen

    public record BookmarkSlot(
        string Key,
        string Subheading,
        string Description,
        string Difficulty,
        Title County,
        int ScreenX,
        int ScreenY
    );

    public record BookmarkResult(
        List<PortraitWriter.CharacterPortraitRequest> PortraitRequests,
        Dictionary<Title, string> BookmarkDnaMap
    );

    public static BookmarkResult WriteAll(
        string modDir, string gameDir, MapConfig cfg,
        ProvinceMap provinces, int[] order, List<Title> empires,
        RealmMap realms, Dictionary<Title, int> development,
        CultureMap cultures, FaithMap faiths, GovernmentMap governments,
        WildernessMap wilderness, PrehistoryMap prehistory, RulerMap rulers)
    {
        // Only realm seats have a character: a liege's demesne counties and every vassal-held
        // county under one man share his seat's ruler, and the character file writes nobody for
        // them. A bookmark must point at a written character (history_id, dynasty), so the
        // candidate set is the seats — which is exactly what RulerMap holds.
        var allCounties = Titles.Flatten(empires).Where(t => t.Tier == "c").ToList();
        var seatCounties = allCounties.Where(c => !wilderness.Contains(c) && rulers.Contains(c)).ToList();

        if (seatCounties.Count == 0)
        {
            return new BookmarkResult([], new Dictionary<Title, string>());
        }

        var countyPositions = CalculateCountyScreenPositions(seatCounties, provinces, order, cfg);
        var bookmarks = SelectBookmarkArchetypes(seatCounties, realms, governments, development, wilderness, countyPositions, rulers);
        var challengeSlot = bookmarks.LastOrDefault() ?? bookmarks[0];

        // Map county -> DNA key (e.g. "dna_bm_char_hegemon")
        var bookmarkDnaMap = new Dictionary<Title, string>();
        foreach (var b in bookmarks)
        {
            bookmarkDnaMap[b.County] = $"dna_{b.Key}";
        }
        bookmarkDnaMap[challengeSlot.County] = $"dna_{ChallengeCharacter}";

        // The portrait follows the character, not the bookmark: stamping the key on the ruler is
        // what lets the character writer emit `dna =` without being handed this map.
        foreach (var (county, dnaKey) in bookmarkDnaMap)
            rulers.For(county).DnaKey = dnaKey;

        WriteBookmarks(modDir, cfg, bookmarks, realms, cultures, faiths, governments, rulers);
        WriteChallengeCharacter(modDir, challengeSlot.County, realms, cfg, cultures, faiths, governments, rulers);
        WriteBookmarkLocalisation(modDir, bookmarks, challengeSlot, rulers);
        WriteBookmarkGraphics(modDir, gameDir);
        WriteRealmHighlights(modDir, cfg, provinces, order, bookmarks, realms, empires);

        var requests = new List<PortraitWriter.CharacterPortraitRequest>();
        foreach (var b in bookmarks)
        {
            requests.Add(new PortraitWriter.CharacterPortraitRequest(b.Key, cultures.For(b.County)));
        }
        requests.Add(new PortraitWriter.CharacterPortraitRequest(ChallengeCharacter, cultures.For(challengeSlot.County)));

        return new BookmarkResult(requests, bookmarkDnaMap);
    }

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
            int capitalProvId = county.Children[0].ProvinceId;

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
    /// Every pool here is drawn from <paramref name="counties"/>, which the caller has already
    /// narrowed to realm seats — the counties <paramref name="rulers"/> wrote a character for. A
    /// non-seat county would give the bookmark a <c>history_id</c> nobody wrote.
    /// </summary>
    private static List<BookmarkSlot> SelectBookmarkArchetypes(
        List<Title> counties, RealmMap realms, GovernmentMap governments,
        Dictionary<Title, int> development, WildernessMap wilderness,
        Dictionary<Title, (int X, int Y)> positions, RulerMap rulers)
    {
        var chosen = new List<BookmarkSlot>();
        var usedCounties = new HashSet<Title>();

        // Greatest is keyed by holder county, so these are seats already; the Contains guard keeps
        // it that way should the realm builder ever list a county the character writer skips.
        var playable = realms.Greatest.Where(c => rulers.Contains(c) && IsPlayable(governments.For(c))).ToList();
        if (playable.Count == 0) playable = counties;

        // 1. The Hegemon
        var hegemon = PickSpacedCandidate(playable, usedCounties, chosen, positions, MinPortraitDistance) ?? playable[0];
        AddSlot(chosen, usedCounties, positions, hegemon,
            "bm_char_hegemon",
            "Master of the Realm",
            "Controls the greatest dominion on the continent, balancing ambitious vassals and external rivals.",
            "BOOKMARK_CHARACTER_DIFFICULTY_EASY");

        // 2. The Frontier Warden
        var frontierPool = playable.Where(c => wilderness.Counties.Any(w => AreAdjacent(c, w)))
                                   .Concat(playable)
                                   .ToList();
        var frontier = PickSpacedCandidate(frontierPool, usedCounties, chosen, positions, MinPortraitDistance);
        if (frontier != null)
        {
            AddSlot(chosen, usedCounties, positions, frontier,
                "bm_char_frontier",
                "Guardian of the Frontier",
                "Guards the boundary between civilization and the untamed wilds, primed for colonization and holy conquest.",
                "BOOKMARK_CHARACTER_DIFFICULTY_MEDIUM");
        }

        // 3. The Ambitious Vassal
        var vassalPool = counties.Where(c => IsPlayable(governments.For(c)) && realms.Liege.ContainsKey(HistoryWriter.Primary(c, realms)))
                                 .Concat(playable)
                                 .ToList();
        var vassal = PickSpacedCandidate(vassalPool, usedCounties, chosen, positions, MinPortraitDistance);
        if (vassal != null)
        {
            AddSlot(chosen, usedCounties, positions, vassal,
                "bm_char_vassal",
                "Power Behind the Throne",
                "A cunning noble serving beneath an overlord, ready to scheme, usurp, or break free.",
                "BOOKMARK_CHARACTER_DIFFICULTY_MEDIUM");
        }

        // 4. The Wealthy Magnate
        var magnatePool = counties.Where(c => IsPlayable(governments.For(c)))
                                  .OrderByDescending(c => development.GetValueOrDefault(c, 0))
                                  .ToList();
        var magnate = PickSpacedCandidate(magnatePool, usedCounties, chosen, positions, MinPortraitDistance);
        if (magnate != null)
        {
            AddSlot(chosen, usedCounties, positions, magnate,
                "bm_char_magnate",
                "Keeper of the Trade Routes",
                "Governs an exceedingly wealthy urban center, commanding vast treasuries and mercenary armies.",
                "BOOKMARK_CHARACTER_DIFFICULTY_EASY");
        }

        // 5. The Untamed Warlord
        var warlordPool = counties.Where(c => governments.For(c) == GovernmentMap.Tribal)
                                  .Concat(counties)
                                  .ToList();
        var warlord = PickSpacedCandidate(warlordPool, usedCounties, chosen, positions, MinPortraitDistance);
        if (warlord != null)
        {
            AddSlot(chosen, usedCounties, positions, warlord,
                "bm_char_warlord",
                "A Trial of Blood and Iron",
                "Leads a martial clan surrounded by fierce competition, where only strength commands loyalty.",
                "BOOKMARK_CHARACTER_DIFFICULTY_HARD");
        }

        // Apply final physics repulsion pass to ensure zero overlaps
        RelaxScreenPositions(chosen);

        return chosen;
    }

    private static Title? PickSpacedCandidate(
        IEnumerable<Title> pool,
        HashSet<Title> used,
        List<BookmarkSlot> chosen,
        Dictionary<Title, (int X, int Y)> positions,
        double minDistance)
    {
        double minDistanceSq = minDistance * minDistance;

        // 1. First choice: pick an unused candidate that is at least minDistance away from all existing selections
        var idealCandidates = pool.Where(c => !used.Contains(c) && chosen.All(s => DistanceSq(positions.GetValueOrDefault(c, (960, 540)), (s.ScreenX, s.ScreenY)) >= minDistanceSq))
                                  .ToList();

        if (idealCandidates.Count > 0) return idealCandidates[0];

        // 2. Fallback: pick the candidate that maximizes distance to the nearest existing bookmark
        return pool.Where(c => !used.Contains(c))
                   .OrderByDescending(c => chosen.Count == 0 ? 0 : chosen.Min(s => DistanceSq(positions.GetValueOrDefault(c, (960, 540)), (s.ScreenX, s.ScreenY))))
                   .FirstOrDefault();
    }

    private static void AddSlot(
        List<BookmarkSlot> chosen,
        HashSet<Title> used,
        Dictionary<Title, (int X, int Y)> positions,
        Title county,
        string key,
        string subheading,
        string description,
        string difficulty)
    {
        used.Add(county);
        var (x, y) = positions.GetValueOrDefault(county, (960, 540));
        chosen.Add(new BookmarkSlot(key, subheading, description, difficulty, county, x, y));
    }

    /// <summary>
    /// Repels overlapping bookmark character coordinates so models and shields never collide.
    /// </summary>
    private static void RelaxScreenPositions(List<BookmarkSlot> slots)
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
                    double dx = slots[j].ScreenX - slots[i].ScreenX;
                    double dy = slots[j].ScreenY - slots[i].ScreenY;
                    double dist = Math.Sqrt(dx * dx + dy * dy);

                    if (dist < separation)
                    {
                        if (dist < 1.0) { dx = 1.0; dy = 0.0; dist = 1.0; }
                        double overlap = 0.5 * (separation - dist);
                        double nx = (dx / dist) * overlap;
                        double ny = (dy / dist) * overlap;

                        int newXi = Math.Clamp((int)Math.Round(slots[i].ScreenX - nx), minX, maxX);
                        int newYi = Math.Clamp((int)Math.Round(slots[i].ScreenY - ny), minY, maxY);
                        int newXj = Math.Clamp((int)Math.Round(slots[j].ScreenX + nx), minX, maxX);
                        int newYj = Math.Clamp((int)Math.Round(slots[j].ScreenY + ny), minY, maxY);

                        slots[i] = slots[i] with { ScreenX = newXi, ScreenY = newYi };
                        slots[j] = slots[j] with { ScreenX = newXj, ScreenY = newYj };
                        moved = true;
                    }
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

    private static bool AreAdjacent(Title a, Title b) => a.Parent != null && a.Parent == b.Parent;

    private static bool IsPlayable(string government) => government
        is GovernmentMap.Feudal or GovernmentMap.Clan or GovernmentMap.Tribal;

    private static void WriteBookmarks(string modDir, MapConfig cfg, List<BookmarkSlot> bookmarks,
        RealmMap realms, CultureMap cultures, FaithMap faiths, GovernmentMap governments,
        RulerMap rulers)
    {
        string dir = Path.Combine(modDir, "common", "bookmarks", "bookmarks");
        Directory.CreateDirectory(dir);

        var sb = new StringBuilder();
        sb.Append($$"""
      bm_generated = {
      	start_date = {{cfg.StartDate}}
      	is_playable = yes
      	group = bm_group_867

      	weight = {
      		value = 100
      	}


      """);

        foreach (var b in bookmarks)
        {
            string culture = cultures.For(b.County).Key;
            string faith = faiths.For(b.County).Key;
            string government = governments.For(b.County);
            var title = HistoryWriter.Primary(b.County, realms);

            var ruler = rulers.For(b.County);
            string dynastyId = ruler.DynastyId;
            string birthDate = $"{ruler.BirthYear}.1.1";
            string sex = ruler.Female ? "female" : "male";

            string anim = b.Key switch
            {
                "bm_char_hegemon" => "war_over_win",
                "bm_char_frontier" => "marshal",
                "bm_char_vassal" => "scheme",
                "bm_char_magnate" => "personality_greedy",
                "bm_char_warlord" => "personality_bold",
                _ => "personality_rational"
            };

            sb.Append($$"""
                character = {
                    name = "{{b.Key}}"
                    dynasty = {{dynastyId}}
                    dynasty_splendor_level = 1
                    type = {{sex}}
                    birth = {{birthDate}}
                    title = {{title.Key}}
                    government = {{government}}
                    culture = {{culture}}
                    religion = {{faith}}
                    difficulty = "{{b.Difficulty}}"
                    history_id = {{HistoryWriter.CharacterId(b.County)}}
                    position = { {{b.ScreenX}} {{b.ScreenY}} }
                    animation = {{anim}}
                }

            """);
        }

        sb.Append("}\n");
        ParadoxText.WriteBom(Path.Combine(dir, "00_bookmarks.txt"), sb.ToString());
    }

    private static void WriteBookmarkLocalisation(string modDir, List<BookmarkSlot> bookmarks,
        BookmarkSlot challengeSlot, RulerMap rulers)
    {
        string dir = Path.Combine(modDir, "localization", "english");
        Directory.CreateDirectory(dir);

        var sb = new StringBuilder();
        sb.Append("l_english:\n");
        sb.Append(" bm_generated:0 \"Procedural Realm\"\n");
        sb.Append(" bm_generated_desc:0 \"Explore a newly forged world with unique cultures, faiths, and empires.\"\n\n");

        foreach (var b in bookmarks)
        {
            string name = rulers.For(b.County).Name;
            sb.Append($" {b.Key}:0 \"{name}\"\n");
            sb.Append($" {b.Key}_subheading:0 \"{b.Subheading}\"\n");
            sb.Append($" {b.Key}_desc:0 \"{b.Description}\"\n\n");
        }

        string cName = rulers.For(challengeSlot.County).Name;
        sb.Append($" {ChallengeCharacter}:0 \"{cName}\"\n");
        sb.Append($" {ChallengeCharacter}_subheading:0 \"{challengeSlot.Subheading}\"\n");
        sb.Append($" {ChallengeCharacter}_desc:0 \"{challengeSlot.Description}\"\n");

        ParadoxText.WriteBom(Path.Combine(dir, "gen_history_l_english.yml"), sb.ToString());
    }

    private static void WriteBookmarkGraphics(string modDir, string gameDir)
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
    }

    private static void WriteChallengeCharacter(string modDir, Title county, RealmMap realms, MapConfig cfg,
        CultureMap cultures, FaithMap faiths, GovernmentMap governments, RulerMap rulers)
    {
        string dir = Path.Combine(modDir, "common", "bookmarks", "challenge_characters");
        Directory.CreateDirectory(dir);

        string culture = cultures.For(county).Key;
        string faith = faiths.For(county).Key;
        string government = governments.For(county);
        var title = HistoryWriter.Primary(county, realms);

        var ruler = rulers.For(county);
        string dynastyId = ruler.DynastyId;
        string birthDate = $"{ruler.BirthYear}.1.1";
        string sex = ruler.Female ? "female" : "male";

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_challenge.txt"),
            $$"""
      {{ChallengeCharacter}} = {
        start_date = {{cfg.StartDate}}

        character = {
            name = "{{ChallengeCharacter}}"
            dynasty = {{dynastyId}}
            dynasty_splendor_level = 1
            type = {{sex}}
            birth = {{birthDate}}
            title = {{title.Key}}
            government = {{government}}
            culture = {{culture}}
            religion = {{faith}}
            difficulty = "BOOKMARK_CHARACTER_DIFFICULTY_HARD"
            history_id = {{HistoryWriter.CharacterId(county)}}
            animation = personality_bold
        }
      }

      """);
    }

    private static void WriteRealmHighlights(string modDir, MapConfig cfg, ProvinceMap provinces,
        int[] order, List<BookmarkSlot> bookmarks, RealmMap realms, List<Title> empires)
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

            string targetFile = Path.Combine(bookmarksDir, $"bm_generated_{b.Key}.dds");
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