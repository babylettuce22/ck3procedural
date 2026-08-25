using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

// GREATLY NEEDS BALANCING
public static class WonderWriter
{
    public static void WriteAll(string modDir, WorldCenterMap worldCenters)
    {
        if (worldCenters.Centers.Count == 0) return;

        WriteBuildingDefinitions(modDir, worldCenters);
        WriteLocalisation(modDir, worldCenters);
        WriteUniversityTriggers(modDir, worldCenters);
    }

    /// <summary>How much of the top-tier value each rung carries.</summary>
    private static readonly double[] TierShare = [0.40, 0.70, 1.00];

    /// <summary>
    /// One tier's modifier values, scaled down from the fully-upgraded table in
    /// <see cref="MapGen.WorldCenters"/>.
    ///
    /// Restated in full at every tier rather than added to the tier below, because CK3 building
    /// levels do not stack: only the highest built level's modifiers apply, which is why vanilla's
    /// <c>castle_02</c> repeats every key <c>castle_01</c> has at a larger value instead of listing
    /// the difference. Generating all three from one table is what keeps them from drifting.
    ///
    /// Whether a value is a whole number is read off how it was written — <c>"5"</c> scales and
    /// rounds as an integer, <c>"0.5"</c> keeps two decimals. That is not a trick: the distinction
    /// is real in the game (a fractional <c>vassal_limit</c> is meaningless) and writing the base
    /// value the way it should read is the least error-prone place to record it. A key that rounds
    /// to zero at a low tier is dropped rather than written as <c>0</c>, so an early rung simply
    /// does not offer that benefit yet.
    /// </summary>
    private static Dictionary<string, string> Scaled(Dictionary<string, string> baseline, int tier)
    {
        double share = TierShare[Math.Clamp(tier - 1, 0, TierShare.Length - 1)];
        var scaled = new Dictionary<string, string>();

        foreach (var (key, text) in baseline)
        {
            if (!double.TryParse(text, System.Globalization.NumberStyles.Float,
                                 System.Globalization.CultureInfo.InvariantCulture, out double value))
            {
                // Not a number this understands — a script value, say. Pass it through unscaled
                // rather than dropping it or guessing.
                scaled[key] = text;
                continue;
            }

            if (text.Contains('.'))
            {
                double result = Math.Round(value * share, 2);
                if (result != 0)
                    scaled[key] = result.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            }
            else
            {
                int result = (int)Math.Round(value * share, MidpointRounding.AwayFromZero);
                if (result != 0)
                    scaled[key] = result.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        return scaled;
    }

    // Emit/WonderWriter.cs -> WriteBuildingDefinitions()

    private static void WriteBuildingDefinitions(string modDir, WorldCenterMap worldCenters)
    {
        string dir = Path.Combine(modDir, "common", "buildings");
        Directory.CreateDirectory(dir);

        var b = new JominiBuilder();
        b.Comment("Generated Wonders / Special Buildings for World Centers");
        b.Blank();

        // A modifier block, written only when the wonder actually carries that kind of modifier.
        void ModifierBlock(string name, Dictionary<string, string> entries)
        {
            if (entries.Count == 0) return;

            using (b.Block(name))
                foreach (var (k, v) in entries) b.Field(k, v);

            b.Blank();
        }

        foreach (var center in worldCenters.Centers)
        for (int tier = 1; tier <= GeneratedWonder.Tiers; tier++)
        {
            var wonder = center.Wonder;
            bool last = tier == GeneratedWonder.Tiers;

            // Normalised on the wonder itself, because the index window needs the same icon as a
            // texture path and the two spellings must not drift apart.
            string cleanIcon = wonder.IconFile;

            using (b.Block(wonder.TierKey(tier)))
            {
                // The map model. CK3 draws this at the province's special_building locator, which is a
                // separate anchor from the holding's own (see LocatorWriter). No filters on the block:
                // graphical_regions/cultures/faiths only narrow *when* an asset is eligible, and a
                // generated world has no guarantee that any particular one of them matches, so an
                // unfiltered block is the one that always resolves. Meshes are chosen in WonderAssets
                // and are all reachable without DLC.
                using (b.Block("asset"))
                {
                    b.Field("type", "pdxmesh");
                    b.Quoted("name", wonder.Mesh);
                }

                b.Blank();

                b.Quoted("type_icon", cleanIcon);
                b.Field("construction_time", "very_slow_construction_time");
                b.Field("type", "special");

                // Rising cost, so the ladder is a commitment rather than a formality. Tier one is
                // placed rather than bought and never charges anyone.
                b.Field("cost_gold", tier switch { 1 => "1000", 2 => "1500", _ => "2500" });

                if (!last) b.Field("next_building", wonder.TierKey(tier + 1));

                b.Blank();

                using (b.Block("can_construct_potential"))
                using (b.Block("barony"))
                    b.Field("this", $"title:{wonder.Barony.Key}");

                b.Blank();

                using (b.Block("is_enabled")) b.Field("always", "yes");
                b.Blank();

                ModifierBlock("character_modifier", Scaled(wonder.CharacterModifiers, tier));
                ModifierBlock("county_modifier", Scaled(wonder.CountyModifiers, tier));
                ModifierBlock("province_modifier", Scaled(wonder.ProvinceModifiers, tier));

                // The capstone announces itself. A generic event rather than a per-archetype one:
                // what the player did is the same in every case, and the event can read the wonder
                // and its holder for itself. See BaseFilesToCopy/Core/events/gen_wonder_events.txt.
                if (last)
                {
                    using (b.Block("on_complete"))
                    {
                        // on_complete runs in PROVINCE scope. Both scopes are saved here rather
                        // than rediscovered in the event, because the event is a static file that
                        // knows nothing about which wonder fired it — naming the building is the
                        // whole reason it can say more than "it".
                        b.Field("save_scope_as", "gen_wonder_site");

                        using (b.Block("county")) b.Field("save_scope_as", "gen_wonder_county");

                        using (b.Block("county.holder"))
                            b.Field("trigger_event", "gen_wonder_events.0001");
                    }

                    b.Blank();
                }

                // Well above vanilla's 100, and deliberately without the guard vanilla puts on its
                // own great buildings — Hagia Sophia carries `factor = 0` while any ordinary
                // building slot is free, which in a generated world would mean the AI essentially
                // never climbs this ladder. The wonder is the point of the county; it should not
                // queue behind a granary.
                using (b.Block("ai_value")) b.Field("base", tier == 1 ? "150" : "200");
            }

            b.Blank();
        }

        ParadoxText.WriteBom(Path.Combine(dir, "01_generated_wonders.txt"), b.ToString());
    }

    private static void WriteUniversityTriggers(string modDir, WorldCenterMap worldCenters)
    {
        var libraries = worldCenters.Centers
            .Where(c => c.Wonder.Archetype == WonderArchetype.GreatLibrary)
            .ToList();

        if (libraries.Count == 0) return;

        string dir = Path.Combine(modDir, "common", "scripted_triggers");
        Directory.CreateDirectory(dir);

        var b = new JominiBuilder();
        b.Comment("Overrides has_university_building_trigger to include generated Great Libraries");
        b.Blank();

        using (b.Block("has_university_building_trigger"))
        using (b.Block("OR"))
        {
            b.Field("has_building_or_higher", "generic_university");
            // `_or_higher`, and naming tier one. The exact-match form this used to write stopped
            // matching the moment a library was upgraded, so improving your great library silently
            // cost the county its university — the kind of bug that reports itself as nothing at
            // all. Vanilla uses the _or_higher form everywhere for the same reason.
            foreach (var lib in libraries) b.Field("has_building_or_higher", lib.Wonder.TierKey(1));
        }

        ParadoxText.WriteBom(Path.Combine(dir, "zz_generated_university_triggers.txt"), b.ToString());
    }

    private static void WriteLocalisation(string modDir, WorldCenterMap worldCenters)
    {
        var loc = new LocFile();

        foreach (var center in worldCenters.Centers)
        {
            var wonder = center.Wonder;

            // Every rung needs its own keys — a building's name is looked up by its own key, so a
            // wonder with three tiers and one set of loc shows two unlocalised buildings.
            //
            // The name does not change as it grows. A half-finished Great Library is still that
            // library, and renaming it per tier would read as three different buildings rather
            // than as one being completed.
            for (int tier = 1; tier <= GeneratedWonder.Tiers; tier++)
            {
                string key = wonder.TierKey(tier);

                loc.AddBuilt($"building_{key}", wonder.Name);
                loc.AddBuilt($"building_{key}_desc", wonder.Description);

                loc.AddBuilt($"building_type_{key}", wonder.Name);
                loc.AddBuilt($"building_type_{key}_desc", wonder.Description);
            }
        }

        loc.Write(Path.Combine(modDir, "localization", "english", "gen_wonders_l_english.yml"));
    }
}