using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;
using System.Text;

namespace Ck3MapGen.Emit;

public static class BookmarkWriter
{
    public const string ChallengeCharacter = "challenge_character_generated";

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
        WildernessMap wilderness)
    {
        var allCounties = Titles.Flatten(empires).Where(t => t.Tier == "c").ToList();
        var playableCounties = allCounties.Where(c => !wilderness.Contains(c)).ToList();

        if (playableCounties.Count == 0)
        {
            return new BookmarkResult([], new Dictionary<Title, string>());
        }

        var countyPositions = CalculateCountyScreenPositions(playableCounties, provinces, order, cfg);
        var bookmarks = SelectBookmarkArchetypes(playableCounties, realms, governments, development, wilderness, countyPositions);
        var challengeSlot = bookmarks.LastOrDefault() ?? bookmarks[0];

        // Map county -> DNA key (e.g. "dna_bm_char_hegemon")
        var bookmarkDnaMap = new Dictionary<Title, string>();
        foreach (var b in bookmarks)
        {
            bookmarkDnaMap[b.County] = $"dna_{b.Key}";
        }
        bookmarkDnaMap[challengeSlot.County] = $"dna_{ChallengeCharacter}";

        WriteBookmarks(modDir, cfg, bookmarks, realms, cultures, faiths, governments);
        WriteChallengeCharacter(modDir, challengeSlot.County, realms, cfg, cultures, faiths, governments);
        WriteBookmarkLocalisation(modDir, bookmarks, challengeSlot, cultures);
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

    private static List<BookmarkSlot> SelectBookmarkArchetypes(
        List<Title> counties, RealmMap realms, GovernmentMap governments,
        Dictionary<Title, int> development, WildernessMap wilderness,
        Dictionary<Title, (int X, int Y)> positions)
    {
        var chosen = new List<BookmarkSlot>();
        var usedCounties = new HashSet<Title>();

        var playable = realms.Greatest.Where(c => IsPlayable(governments.For(c))).ToList();
        if (playable.Count == 0) playable = counties;

        // 1. The Hegemon
        var hegemon = playable.FirstOrDefault(c => !usedCounties.Contains(c)) ?? counties[0];
        usedCounties.Add(hegemon);
        var (hx, hy) = positions.GetValueOrDefault(hegemon, (960, 540));
        chosen.Add(new BookmarkSlot(
            "bm_char_hegemon",
            "Master of the Realm",
            "Controls the greatest dominion on the continent, balancing ambitious vassals and external rivals.",
            "BOOKMARK_CHARACTER_DIFFICULTY_EASY",
            hegemon, hx, hy
        ));

        // 2. The Frontier Warden
        var frontier = playable.FirstOrDefault(c => !usedCounties.Contains(c) && wilderness.Counties.Any(w => AreAdjacent(c, w)))
                       ?? playable.FirstOrDefault(c => !usedCounties.Contains(c))
                       ?? counties.FirstOrDefault(c => !usedCounties.Contains(c));
        if (frontier != null)
        {
            usedCounties.Add(frontier);
            var (fx, fy) = positions.GetValueOrDefault(frontier, (450, 480));
            chosen.Add(new BookmarkSlot(
                "bm_char_frontier",
                "Guardian of the Frontier",
                "Guards the boundary between civilization and the untamed wilds, primed for colonization and holy conquest.",
                "BOOKMARK_CHARACTER_DIFFICULTY_MEDIUM",
                frontier, fx, fy
            ));
        }

        // 3. The Ambitious Vassal
        var vassal = counties.FirstOrDefault(c => !usedCounties.Contains(c) && IsPlayable(governments.For(c)) && realms.Liege.ContainsKey(HistoryWriter.Primary(c, realms)))
                     ?? playable.FirstOrDefault(c => !usedCounties.Contains(c));
        if (vassal != null)
        {
            usedCounties.Add(vassal);
            var (vx, vy) = positions.GetValueOrDefault(vassal, (1150, 420));
            chosen.Add(new BookmarkSlot(
                "bm_char_vassal",
                "Power Behind the Throne",
                "A cunning noble serving beneath an overlord, ready to scheme, usurp, or break free.",
                "BOOKMARK_CHARACTER_DIFFICULTY_MEDIUM",
                vassal, vx, vy
            ));
        }

        // 4. The Wealthy Magnate
        var magnate = counties.Where(c => !usedCounties.Contains(c) && IsPlayable(governments.For(c)))
                              .OrderByDescending(c => development.GetValueOrDefault(c, 0))
                              .FirstOrDefault();
        if (magnate != null)
        {
            usedCounties.Add(magnate);
            var (mx, my) = positions.GetValueOrDefault(magnate, (720, 680));
            chosen.Add(new BookmarkSlot(
                "bm_char_magnate",
                "Keeper of the Trade Routes",
                "Governs an exceedingly wealthy urban center, commanding vast treasuries and mercenary armies.",
                "BOOKMARK_CHARACTER_DIFFICULTY_EASY",
                magnate, mx, my
            ));
        }

        // 5. The Untamed Warlord
        var warlord = counties.FirstOrDefault(c => !usedCounties.Contains(c) && governments.For(c) == GovernmentMap.Tribal)
                      ?? counties.FirstOrDefault(c => !usedCounties.Contains(c));
        if (warlord != null)
        {
            usedCounties.Add(warlord);
            var (wx, wy) = positions.GetValueOrDefault(warlord, (1350, 620));
            chosen.Add(new BookmarkSlot(
                "bm_char_warlord",
                "A Trial of Blood and Iron",
                "Leads a martial clan surrounded by fierce competition, where only strength commands loyalty.",
                "BOOKMARK_CHARACTER_DIFFICULTY_HARD",
                warlord, wx, wy
            ));
        }

        return chosen;
    }

    private static bool AreAdjacent(Title a, Title b) => a.Parent != null && a.Parent == b.Parent;

    private static bool IsPlayable(string government) => government
        is GovernmentMap.Feudal or GovernmentMap.Clan or GovernmentMap.Tribal;

    private static void WriteBookmarks(string modDir, MapConfig cfg, List<BookmarkSlot> bookmarks,
        RealmMap realms, CultureMap cultures, FaithMap faiths, GovernmentMap governments)
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

            sb.Append($$"""
              	character = {
              		name = "{{b.Key}}"
              		dynasty = {{HistoryWriter.DynastyId(b.County)}}
              		dynasty_splendor_level = 1
              		type = male
              		birth = {{cfg.BirthDate}}
              		title = {{title.Key}}
              		government = {{government}}
              		culture = {{culture}}
              		religion = {{faith}}
              		difficulty = "{{b.Difficulty}}"
              		history_id = {{HistoryWriter.CharacterId(b.County)}}
              		position = { {{b.ScreenX}} {{b.ScreenY}} }
              	}

              """);
        }

        sb.Append("}\n");
        ParadoxText.WriteBom(Path.Combine(dir, "00_bookmarks.txt"), sb.ToString());
    }

    private static void WriteBookmarkLocalisation(string modDir, List<BookmarkSlot> bookmarks,
        BookmarkSlot challengeSlot, CultureMap cultures)
    {
        string dir = Path.Combine(modDir, "localization", "english");
        Directory.CreateDirectory(dir);

        var sb = new StringBuilder();
        sb.Append("l_english:\n");
        sb.Append(" bm_generated:0 \"Procedural Realm\"\n");
        sb.Append(" bm_generated_desc:0 \"Explore a newly forged world with unique cultures, faiths, and empires.\"\n\n");

        foreach (var b in bookmarks)
        {
            var (name, _) = HistoryWriter.RulerNames(b.County, cultures.For(b.County));
            sb.Append($" {b.Key}:0 \"{name}\"\n");
            sb.Append($" {b.Key}_subheading:0 \"{b.Subheading}\"\n");
            sb.Append($" {b.Key}_desc:0 \"{b.Description}\"\n\n");
        }

        var (cName, _) = HistoryWriter.RulerNames(challengeSlot.County, cultures.For(challengeSlot.County));
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
            string vanillaBg = Path.Combine(gameDir, "gfx", "interface", "bookmarks", "bm_867_great_adventurers.dds");
            if (File.Exists(vanillaBg))
            {
                File.Copy(vanillaBg, targetBmBg, overwrite: true);
            }
        }

        string targetStartBtn = Path.Combine(startButtonsDir, "bm_generated.dds");
        string vanillaStartBtn = Path.Combine(gameDir, "gfx", "interface", "bookmarks", "start_buttons", "bm_867.dds");
        if (File.Exists(vanillaStartBtn))
        {
            File.Copy(vanillaStartBtn, targetStartBtn, overwrite: true);
        }

        string targetIcon = Path.Combine(iconsDir, "bm_generated.dds");
        string vanillaIcon = Path.Combine(gameDir, "gfx", "interface", "icons", "bookmark_buttons", "bm_867.dds");
        if (File.Exists(vanillaIcon))
        {
            File.Copy(vanillaIcon, targetIcon, overwrite: true);
        }
    }

    private static void WriteChallengeCharacter(string modDir, Title county, RealmMap realms, MapConfig cfg,
        CultureMap cultures, FaithMap faiths, GovernmentMap governments)
    {
        string dir = Path.Combine(modDir, "common", "bookmarks", "challenge_characters");
        Directory.CreateDirectory(dir);

        string culture = cultures.For(county).Key;
        string faith = faiths.For(county).Key;
        string government = governments.For(county);
        var title = HistoryWriter.Primary(county, realms);

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_challenge.txt"),
            $$"""
              {{ChallengeCharacter}} = {
              	start_date = {{cfg.StartDate}}

              	character = {
              		name = "{{ChallengeCharacter}}"
              		dynasty = {{HistoryWriter.DynastyId(county)}}
              		dynasty_splendor_level = 1
              		type = male
              		birth = {{cfg.BirthDate}}
              		title = {{title.Key}}
              		government = {{government}}
              		culture = {{culture}}
              		religion = {{faith}}
              		difficulty = "BOOKMARK_CHARACTER_DIFFICULTY_HARD"
              		history_id = {{HistoryWriter.CharacterId(county)}}
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