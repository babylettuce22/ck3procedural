using Ck3MapGen.Config;
using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen;

/// <summary>
/// The additional bookmarks' maps for a world written with an applied history.
///
/// A generated world's bookmarks come off the formation as it runs (<see cref="FormationHistory.Snapshots"/>),
/// because their dates are known before it starts. An applied history moves the start date to
/// wherever the player accepted the world, so its bookmarks sit around that year instead, and each
/// is read from whichever run actually covered its date:
///
/// <list type="bullet">
/// <item>after the applied year — the realms run on from it an epoch at a time
/// (<see cref="Formation.RunOn"/>), as a generated world runs on for a later bookmark;</item>
/// <item>inside the simulated years — the history's own timeline (<see cref="AppliedHistory.Timeline"/>);</item>
/// <item>before the history began — the formation's epochs (<see cref="FormationHistory.Frames"/>),
/// whose years line up with the timeline's where the formation ended and the history began.</item>
/// </list>
///
/// The frontier moves in a history — land settled, land fallen — so each date carries the
/// wilderness it had (<see cref="RealmMap.Wilderness"/>): whatever no realm held on it. Land the
/// generated world had wild is unsettled; land somebody held once and lost is a ruin, when the ruins
/// system ships.
/// </summary>
internal static class HistoryEras
{
    public static Dictionary<int, RealmMap> Maps(AppliedHistory applied, FormationHistory resolved, MapConfig cfg,
        List<Title> empires, List<Title> counties, CultureMap cultures, Dictionary<Title, int> development,
        WildernessMap generatedWilderness, bool ruinsEnabled, ProvinceMap provinces, int[] order, int baronyCount)
    {
        var result = new Dictionary<int, RealmMap>();
        var years = cfg.AdditionalBookmarkYears;
        if (years.Length == 0) return result;

        // Every county, wild ones included: a date's realms may hold ground that is wild at the start.
        var adjacency = Realms.BuildCountyAdjacency(counties, provinces, baronyCount, order,
            (int)Math.Round(cfg.Scaled(cfg.SeaBridgePixelsAtVanilla)));

        var later = Formation.RunOn(resolved, applied.Year, [.. years.Where(y => y > applied.Year)], cfg.Seed);

        // The formation's years against the timeline's: it ended where the first history began.
        int since = applied.ChronicleSince;
        int offset = resolved.EndYear > 0 ? resolved.EndYear - since : 0;

        foreach (int year in years)
        {
            FormationHistory snapshot;
            string source;
            if (year > applied.Year && later.TryGetValue(year, out var future))
                (snapshot, source) = (future, "run on from the applied year");
            else if (year >= since && applied.FrameAt(year) is { } frame)
                (snapshot, source) = (AppliedHistory.FrameHistory(frame, counties, cultures, resolved.FirstYear),
                    $"the history as it stood in {frame.Year}");
            else if (year < since && resolved.FrameAt(year + offset) is { } epoch)
                (snapshot, source) = (epoch, "the formation");
            else
                (snapshot, source) = (resolved, "the applied year's map — the history kept no timeline");

            // Titling folds and frees polities in place. The formation's frames are the kept world's
            // and are read again by the next re-emit, so every source is titled as a copy.
            var (polities, owner) = Formation.CopyLiving(snapshot.Polities);
            var fresh = new FormationHistory { Polities = polities, Owner = owner, Events = [], FirstYear = snapshot.FirstYear };

            var unsettled = new HashSet<Title>();
            var ruins = new HashSet<Title>();
            foreach (var county in counties.Where(c => !owner.ContainsKey(c)).OrderBy(c => c.Index))
            {
                bool heldOnce = !generatedWilderness.Contains(county) || generatedWilderness.IsRuin(county);
                if (heldOnce && ruinsEnabled) ruins.Add(county);
                else unsettled.Add(county);
            }
            var wild = new WildernessMap(unsettled, ruins, ruinsEnabled);

            var map = Realms.FromHistory(fresh, empires, development, wild, cfg, new Rng(cfg.Seed ^ 0x2E18 ^ year),
                adjacency, quiet: true);
            map.Wilderness = wild;
            result[year] = map;

            Console.WriteLine($"  additional bookmark {year}: from {source} — "
                + $"{polities.Count} realms, {wild.Count} counties wild ({wild.RuinCount} ruins)");
        }

        return result;
    }
}
