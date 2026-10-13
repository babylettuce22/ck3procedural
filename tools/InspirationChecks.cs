using System.Text.Json;
using Ck3MapGen.AppGUI;
using Ck3MapGen.Config;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Tools;

/// <summary>
/// The inspiration's multi-select: every pick a tile could make must still make the tile's world,
/// exclusions must really exclude, and choices saved before the lists existed must load.
/// </summary>
internal static class InspirationChecks
{
    private static int _failed;

    public static int Run()
    {
        // 1. A preset's footprint is that preset, to the table.
        foreach (var look in Enum.GetValues<MapConfig.HumanLook>())
        {
            var cfg = new MapConfig();
            new QuickInspiration { Regions = [.. Ethnicities.Footprint(look)] }.ApplyTo(cfg);
            Check(cfg.DominantLook == look && cfg.FaceRegions.Count == 0
                  && Ethnicities.LookTemplates(cfg).SequenceEqual(Ethnicities.LookTemplates(look)),
                $"Footprint of {look} writes {look} and its own table");
        }

        // 2. Everything but the Middle East: no Arab minority through the Mediterranean's table.
        var noMena = Table([.. QuickInspiration.AllRegions.Where(r => r != MapConfig.FaceRegion.MiddleEastern)]);
        Check(!noMena.ContainsKey("arab") && !noMena.ContainsKey("turkic_west") && noMena.ContainsKey("mediterranean")
              && noMena.ContainsKey("slavic") && noMena.ContainsKey("indian"),
            "All but Middle Eastern leaves out arab and turkic_west, keeps the rest");

        // 3. Two regions no preset pairs: only theirs, half each.
        var pair = Table([MapConfig.FaceRegion.Steppe, MapConfig.FaceRegion.SubSaharan]);
        double steppe = pair.GetValueOrDefault("turkic") + pair.GetValueOrDefault("asian_mongol");
        double africa = pair.GetValueOrDefault("african") + pair.GetValueOrDefault("east_african");
        Check(pair.Keys.All(k => k is "turkic" or "asian_mongol" or "african" or "east_african")
              && Math.Abs(steppe - africa) <= 2,
            $"Steppe + Sub-Saharan blends evenly ({string.Join(", ", pair.Select(p => $"{p.Key} {p.Value}"))})");

        // 4. Culture themes: one is the single setting; several the union; all seven is Anywhere.
        var one = new MapConfig();
        new QuickInspiration { Themes = [MapConfig.CultureLookTheme.NorthernNorse] }.ApplyTo(one);
        Check(one.CultureAestheticsTheme == MapConfig.CultureLookTheme.NorthernNorse && one.CultureThemes.Count == 0,
            "One theme writes the single setting");

        var two = new MapConfig();
        new QuickInspiration { Themes = [MapConfig.CultureLookTheme.SteppeNomadic, MapConfig.CultureLookTheme.NorthernNorse] }.ApplyTo(two);
        var flavours = LanguageFlavour.Pool(two).Select(p => p.Flavour.Name).ToHashSet();
        Check(two.CultureThemeSet.Count == 2 && flavours.Contains("Norse") && flavours.Contains("Steppe")
              && Math.Abs(LanguageFlavour.Pool(two).Sum(p => p.Weight) - 2) < 1e-9,
            "Norse + Steppe pools both tongues, each theme a share of one");
        Check(LanguageFlavour.Pool(one).SequenceEqual(LanguageFlavour.Pool(new MapConfig { CultureAestheticsTheme = MapConfig.CultureLookTheme.NorthernNorse })),
            "A single theme's pool is unchanged");

        var all = new QuickInspiration { Themes = [.. QuickInspiration.AllThemes] };
        Check(all.IsDefault && all.MatchingPreset?.Key == "anywhere", "Every theme and no faces picked is Anywhere");

        // 5. Saved before multi-select: Theme and Look read in, never written back.
        var legacy = JsonSerializer.Deserialize<QuickInspiration>(
            $$"""{"Theme":{{(int)MapConfig.CultureLookTheme.ByzantineGreek}},"Look":{{(int)MapConfig.HumanLook.Mediterranean}}}""")!;
        Check(legacy.MatchingPreset?.Key == "byzantium", "Old Byzantium choice loads as the Byzantium tile");
        string json = JsonSerializer.Serialize(legacy);
        Check(!json.Contains("\"Theme\"") && !json.Contains("\"Look\""), $"Legacy keys not written ({json})");

        // 6. A settings preset carries the lists; a dropdown changed on the grid drops them.
        string file = Path.Combine(Path.GetTempPath(), "Ck3MapGen-inspiration-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var back = new MapConfig { DominantLook = MapConfig.HumanLook.Steppe };
            new QuickInspiration
            {
                Themes = [MapConfig.CultureLookTheme.SteppeNomadic, MapConfig.CultureLookTheme.NorthernNorse],
                Regions = [MapConfig.FaceRegion.Slavic, MapConfig.FaceRegion.Indian]
            }.ApplyTo(two);
            Preset.Save(two, file);
            Preset.Load(back, file);
            Check(back.CultureThemeSet.Count == 2 && back.FaceRegions.Count == 2, "Preset round trip keeps both lists");
            back.DominantLook = MapConfig.HumanLook.Steppe;
            back.CultureAestheticsTheme = MapConfig.CultureLookTheme.ByzantineGreek;
            Check(back.FaceRegions.Count == 0 && back.CultureThemes.Count == 0, "Grid dropdowns drop the lists");
        }
        finally { File.Delete(file); }

        // 7. Describe.
        var but = new QuickInspiration { Regions = [.. QuickInspiration.AllRegions.Where(r => r != MapConfig.FaceRegion.Steppe)] };
        Check(but.Describe() == "Varied culture, faces from anywhere but Steppe", $"Describe exclusion ({but.Describe()})");

        Console.WriteLine(_failed == 0 ? "All inspiration checks passed." : $"{_failed} inspiration check(s) failed.");
        return _failed == 0 ? 0 : 1;
    }

    private static Dictionary<string, int> Table(List<MapConfig.FaceRegion> regions)
    {
        var cfg = new MapConfig();
        new QuickInspiration { Regions = regions }.ApplyTo(cfg);
        return Ethnicities.LookTemplates(cfg).ToDictionary(t => t.Template, t => t.Weight);
    }

    private static void Check(bool ok, string what)
    {
        Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {what}");
        if (!ok) _failed++;
    }
}
