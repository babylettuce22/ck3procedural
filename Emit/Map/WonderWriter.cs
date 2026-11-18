using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

// GREATLY NEEDS BALANCING
public static class WonderWriter
{
    public static void WriteAll(string modDir, string gameDir, WorldCenterMap worldCenters, bool wilderness = false)
    {
        // Before the early return: the mod directory is not wiped between runs, and a stale
        // override naming last world's wonder would outlive it.
        WriteWallOverride(modDir, gameDir, worldCenters, wilderness);

        if (worldCenters.Centers.Count == 0)
        {
            // Same reason: a stale district would name a building this world no longer defines.
            string districts = Path.Combine(modDir, "common", "districts", "01_generated_wonder_districts.txt");
            if (File.Exists(districts)) File.Delete(districts);
            return;
        }

        WriteBuildingDefinitions(modDir, worldCenters);
        WriteDistrictDefinitions(modDir, worldCenters);
        WriteLocalisation(modDir, worldCenters);
        WriteUniversityTriggers(modDir, worldCenters);
    }

    private const string WallOverrideFile = "zz_generated_wonder_walls.txt";

    /// <summary>The line in vanilla's <c>walls_00</c> the generated keys go after — the last of
    /// its own "this building is the walls" entries.</summary>
    private const string WallOverrideAnchor = "has_building_or_higher = \"roman_wall_of_lugo_01\"";

    /// <summary>
    /// Switches the holding's own wall ring off under a wonder that IS the town's walls.
    ///
    /// A holding's wall ring is a background building from the <c>walls_00..04</c> ladder, drawn
    /// whatever else stands in the province, so a generated wall circuit around a castle gets the
    /// castle's ordinary ring drawn just inside it. Vanilla has the same problem with Lugo and
    /// Toledo and answers it in <c>walls_00</c> — the "no walls" rung, whose trigger lists them —
    /// which wins the holding's one background slot and draws a bare plate instead. This is that
    /// block, copied from the game's own file at generation time and with this world's wall
    /// wonders added to the list, so nothing else about it can drift from vanilla.
    ///
    /// Only walls_00 is touched. Vanilla leaves kano_walls, jaisalmer_fort and temples_angkor out of
    /// walls_01..04's exclusions entirely and relies on walls_00 winning the slot; this relies on
    /// the same thing.
    ///
    /// <para>The Wilderness set needs the same block for the opposite reason. Since CK3 1.21
    /// walls_00 fires wherever <c>fort_level &lt; 1</c>, and wilderness_01's <c>fort_level = -1</c>
    /// makes every unsettled capital qualify, so western_walls_00_mesh — a ground plate — would draw
    /// a settled-looking patch on empty country. With <paramref name="wilderness"/> the copy also
    /// requires <c>NOT = { has_holding_type = wilderness_holding }</c>, which leaves no wall
    /// building at all for a wilderness holding: walls_01_tribal needs tribe_01 (1.21) and
    /// walls_01..04 need a fort level. That replaces the Wilderness set's old override of
    /// walls_01_tribal, which 1.20 needed and 1.21 made redundant. One generated copy, because two
    /// files redefining walls_00 would leave whichever loaded last.</para>
    /// </summary>
    private static void WriteWallOverride(string modDir, string gameDir, WorldCenterMap worldCenters, bool wilderness)
    {
        string path = Path.Combine(modDir, "common", "buildings", WallOverrideFile);

        // The Wilderness set's pre-1.21 override, which StaticFileWriter no longer ships but does
        // not remove from a mod folder written before. On 1.21 it would put a tribal palisade back
        // on every castle that has no fort building, since it predates the tribe_01 gate.
        string stale = Path.Combine(modDir, "common", "buildings", "zz_wilderness_wall_override.txt");
        if (File.Exists(stale)) File.Delete(stale);

        var walls = worldCenters.Centers.Where(c => c.Wonder.ReplacesWalls).ToList();
        if (walls.Count == 0 && !wilderness)
        {
            if (File.Exists(path)) File.Delete(path);
            return;
        }

        string source = Path.Combine(gameDir, "common", "buildings", "99_background_graphics_buildings.txt");
        if (!File.Exists(source))
        {
            Console.WriteLine($"  wonders: WARNING {source} not found, holding walls will draw inside " +
                              "generated wall circuits");
            return;
        }

        var lines = File.ReadAllLines(source);
        var block = ScriptScan.TopLevelDeclarations(lines, c => char.IsLetterOrDigit(c) || c == '_')
            .FirstOrDefault(d => d.Key == "walls_00" && d.Closed);

        int anchor = -1;
        if (block.Key is not null)
            for (int i = block.First; i <= block.Last && anchor < 0; i++)
                if (ScriptScan.StripComment(lines[i]).Trim() == WallOverrideAnchor) anchor = i;

        // Vanilla rewrote the block. Refuse rather than guess where the list went: a trigger
        // spliced into the wrong clause would strip or add walls across the whole map.
        if (anchor < 0)
        {
            Console.WriteLine("  wonders: WARNING walls_00 no longer lists roman_wall_of_lugo_01; " +
                              "holding walls will draw inside generated wall circuits");
            if (File.Exists(path)) File.Delete(path);
            return;
        }

        string indent = lines[anchor][..(lines[anchor].Length - lines[anchor].TrimStart().Length)];

        // The block's own is_enabled opening, for the wilderness line to go straight inside —
        // beside vanilla's OR, which makes it a condition the whole OR must also meet.
        int enabled = -1;
        for (int i = block.First; i <= block.Last && enabled < 0; i++)
            if (ScriptScan.StripComment(lines[i]).Trim() == "is_enabled = {") enabled = i;
        if (wilderness && enabled < 0)
            Console.WriteLine("  wonders: WARNING walls_00 has no 'is_enabled = {' line; wilderness " +
                              "capitals will draw vanilla's bare wall plate");

        var text = new System.Text.StringBuilder();
        text.Append("# Vanilla's walls_00 from 99_background_graphics_buildings.txt, with this world's\n");
        text.Append("# wall-circuit wonders added beside Lugo and Toledo so the holding inside them is\n");
        text.Append("# not drawn with a second ring");
        if (wilderness) text.Append(", and off on wilderness holdings so empty country draws no ground plate");
        text.Append(". Generated by WonderWriter; do not edit.\n\n");

        for (int i = block.First; i <= block.Last; i++)
        {
            text.Append(lines[i].TrimStart('﻿')).Append('\n');

            if (wilderness && i == enabled)
            {
                string inner = lines[i][..(lines[i].Length - lines[i].TrimStart().Length)] + "\t";
                text.Append(inner).Append("NOT = { has_holding_type = wilderness_holding } # nothing built, nothing drawn\n");
            }

            if (i != anchor) continue;

            foreach (var center in walls)
                text.Append(indent).Append($"has_building_or_higher = \"{center.Wonder.TierKey(1)}\"")
                    .Append($" # {center.Wonder.Name}\n");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        ParadoxText.WriteBom(path, text.ToString());
        Console.WriteLine($"  wonders: holding walls off under {walls.Count} wall circuit(s)"
            + (wilderness ? " and on wilderness holdings" : ""));
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

    // Emit/Map/WonderWriter.cs -> WriteBuildingDefinitions()

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
                // and are all reachable without DLC, bar the Holy Buildings and East Asian Wonders
                // packs': those carry the pack's flag and are followed by an unflagged model, the pair
                // vanilla writes for holy_site_cathedral_01, so a player without the pack still sees
                // a building.
                //
                // Each rung names its own model, so a laddered wonder visibly grows as it is built.
                using (b.Block("asset"))
                {
                    b.Field("type", wonder.IsEntity ? "entity" : "pdxmesh");
                    b.Quoted("name", wonder.MeshAt(tier));
                    if (wonder.Dlc is not null) b.Field("requires_dlc_flag", wonder.Dlc);
                }

                if (wonder.Fallback is not null)
                {
                    using (b.Block("asset"))
                    {
                        b.Field("type", "pdxmesh");
                        b.Quoted("name", wonder.Fallback);
                    }
                }

                b.Blank();

                b.Quoted("type_icon", cleanIcon);
                b.Field("construction_time", "very_slow_construction_time");
                // No `type = special`: CK3 1.21 dropped the field. What makes this special now is
                // the special district it is the primary building of (WriteDistrictDefinitions).

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

                        using (b.Block("county"))
                        {
                            b.Field("save_scope_as", "gen_wonder_county");
                            // And the chronicle: the county's book, its lieges' and the world's.
                            // See Emit/History/ChronicleRuntimeWriter.cs.
                            b.Inline("gen_chr_push_up_effect", "TMPL = gen_chr_wonder ACTOR = holder WORLD = yes");
                        }

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

    /// <summary>
    /// One special district per wonder, hosting its ladder (CK3 1.21). The district is what a
    /// province's <c>special_district_slot</c> names and what its history builds at a level; the
    /// ladder's first rung is its primary building, and levels walk <c>next_building</c>. Named
    /// the way vanilla names every special district — the primary building plus <c>_district</c>
    /// (<see cref="ContentWriter.SpecialDistrictKey"/>) — so the history writer can derive it.
    /// </summary>
    private static void WriteDistrictDefinitions(string modDir, WorldCenterMap worldCenters)
    {
        string dir = Path.Combine(modDir, "common", "districts");
        Directory.CreateDirectory(dir);

        var b = new JominiBuilder();
        b.Comment("Special districts hosting the generated wonders. Generated by WonderWriter; do not edit.");
        b.Blank();

        foreach (var center in worldCenters.Centers)
        {
            using (b.Block(ContentWriter.SpecialDistrictKey(center.Wonder.TierKey(1))))
            {
                b.Field("primary_building", center.Wonder.TierKey(1));
                // Vanilla's own fallback picture for a special district with no art of its own.
                using (b.Block("background"))
                    b.Quoted("reference", "gfx/interface/illustrations/event_scenes/market_east.dds");
                b.Field("type", "special");
            }
            b.Blank();
        }

        ParadoxText.WriteBom(Path.Combine(dir, "01_generated_wonder_districts.txt"), b.ToString());
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

            // The district's own keys, as vanilla words them for its special districts:
            // the name and flavour are the building's (duchy_special_districts_l_english.yml).
            string district = ContentWriter.SpecialDistrictKey(wonder.TierKey(1));
            loc.AddBuilt(district, wonder.Name);
            loc.AddBuilt($"{district}_desc_flavor", wonder.Description);
        }

        loc.Write(Path.Combine(modDir, "localization", "english", "gen_wonders_l_english.yml"));
    }
}