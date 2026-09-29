using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// What a mountain pass is worth to whoever holds it: a province modifier on each pass barony, put
/// there at game start. See <see cref="MountainPasses"/>.
///
/// A fort and a defender's edge because the pass is the one way through the range and a small
/// garrison can hold it; a toll because every caravan crossing the range comes through it. Kept
/// to what vanilla's own buildings give a single barony — a level of fort, +5 defence, +15% tax —
/// so a pass is a place worth taking, not a fortress no one can.
///
/// Nothing is written on a map without passes: an unused modifier is a line in error.log.
/// </summary>
public static class PassWriter
{
    private const string Modifier = "gen_mountain_pass_modifier";
    private const string Setup = "gen_mountain_pass_setup";

    public static void WriteAll(string modDir, IReadOnlyList<(MountainPass Pass, int ProvinceId)> passes)
    {
        if (passes.Count == 0) return;

        var modifiers = new JominiBuilder();
        modifiers.Comment("On every mountain pass barony, put there at game start by zz_gen_mountain_pass_on_actions.txt.");
        using (modifiers.Block(Modifier))
        {
            modifiers.Field("icon", "martial_positive");
            modifiers.Field("fort_level", 1);
            modifiers.Field("defender_holding_advantage", 5);
            modifiers.Field("tax_mult", 0.15, "0.0#");
        }
        string modifierDir = Path.Combine(modDir, "common", "modifiers");
        Directory.CreateDirectory(modifierDir);
        ParadoxText.WriteBom(Path.Combine(modifierDir, "zz_gen_mountain_pass_modifiers.txt"), modifiers.ToString());

        // on_game_start rather than after the lobby: the pass is part of the map, and a modifier on
        // a province is there whichever character is picked.
        var actions = new JominiBuilder();
        actions.Comment("Marks each mountain pass barony. See common/modifiers/zz_gen_mountain_pass_modifiers.txt.");
        using (actions.Block("on_game_start"))
        using (actions.Block("on_actions"))
            actions.Token(Setup);
        actions.Blank();
        using (actions.Block(Setup))
        using (actions.Block("effect"))
        {
            foreach (var (_, id) in passes.OrderBy(p => p.ProvinceId))
                using (actions.Block($"province:{id}"))
                    actions.Field("add_province_modifier", Modifier);
        }
        string actionDir = Path.Combine(modDir, "common", "on_action");
        Directory.CreateDirectory(actionDir);
        ParadoxText.WriteBom(Path.Combine(actionDir, "zz_gen_mountain_pass_on_actions.txt"), actions.ToString());

        var loc = new LocFile();
        loc.Add(Modifier, "Mountain Pass");
        loc.Add($"{Modifier}_desc",
            "The only road over these mountains runs through here. Whoever holds it can close it to an " +
            "army, and every merchant who crosses pays at the gate.");
        loc.Write(Path.Combine(modDir, "localization", "english", "zz_gen_mountain_pass_l_english.yml"));

        Console.WriteLine($"  mountain passes: {passes.Count} barony(ies) given {Modifier}");
    }

    /// <summary>
    /// A landmark's two footprints in the game (see <see cref="Landmark"/>): a province modifier
    /// named for it on every barony inside it, put there at game start the way the pass modifier
    /// is, so its name shows in the province view; and a geographical region of the same baronies,
    /// under the same name, for any decision or event that wants to speak of it.
    ///
    /// What holding one is worth goes by its kind and by the barony's place in it: a landmark's
    /// footprint takes in its high ground as well as its low (a crater's rim with its floor, a
    /// rift's escarpments with its valley), and the two are different country. So each landmark
    /// has two modifiers under its one name, one for its hills and mountains and one for the rest,
    /// chosen by the barony's own terrain. Kept small, within what one vanilla building gives, and
    /// never repeating what the terrain already does: flavour a player notices on the province,
    /// with a cost where the land has one, not a reason to rush for it.
    /// </summary>
    public static void WriteLandmarks(string modDir, IReadOnlyList<Landmark> landmarks, TerrainClass[] provinceTerrain)
    {
        // A landmark with no land — a sea named only on the flat map — has nothing to mark here.
        landmarks = landmarks.Where(l => l.Baronies.Length > 0).ToList();
        if (landmarks.Count == 0) return;

        var modifiers = new JominiBuilder();
        modifiers.Comment("On every barony inside a landmark, put there at game start by zz_gen_landmark_on_actions.txt.");
        var regions = new JominiBuilder();
        regions.Comment("One region per landmark, the baronies inside it. Named in zz_gen_landmark_l_english.yml.");
        var loc = new LocFile();

        // Each barony's modifier, by province id, for the on_action below.
        var placed = new List<(int Id, string Modifier)>();
        int heights = 0;

        foreach (var landmark in landmarks)
        {
            foreach (var high in new[] { false, true })
            {
                var members = landmark.Baronies.Where(id => IsHeights(provinceTerrain[id]) == high).ToArray();
                if (members.Length == 0) continue;

                string modifier = $"{landmark.Key}_{(high ? "heights" : "lowland")}_modifier";
                var (icon, effects, description) = Worth(landmark.Kind, high);
                using (modifiers.Block(modifier))
                {
                    modifiers.Field("icon", icon);
                    foreach (var (field, value) in effects) modifiers.Field(field, value, "0.##");
                }
                modifiers.Blank();

                loc.Add(modifier, landmark.Name);
                loc.Add($"{modifier}_desc", description);
                placed.AddRange(members.Select(id => (id, modifier)));
                if (high) heights += members.Length;
            }

            using (regions.Block(landmark.Key))
            using (regions.Block("provinces"))
                foreach (var row in landmark.Baronies.Order().Chunk(10))
                    regions.Token(string.Join(' ', row));
            regions.Blank();

            loc.Add(landmark.Key, landmark.Name);
        }

        string modifierDir = Path.Combine(modDir, "common", "modifiers");
        Directory.CreateDirectory(modifierDir);
        ParadoxText.WriteBom(Path.Combine(modifierDir, "zz_gen_landmark_modifiers.txt"), modifiers.ToString());

        string regionDir = Path.Combine(modDir, "map_data", "geographical_regions");
        Directory.CreateDirectory(regionDir);
        ParadoxText.WriteBom(Path.Combine(regionDir, "zz_gen_landmark_regions.txt"), regions.ToString());

        const string setup = "gen_landmark_setup";
        var actions = new JominiBuilder();
        actions.Comment("Marks each landmark's baronies. See common/modifiers/zz_gen_landmark_modifiers.txt.");
        using (actions.Block("on_game_start"))
        using (actions.Block("on_actions"))
            actions.Token(setup);
        actions.Blank();
        using (actions.Block(setup))
        using (actions.Block("effect"))
            foreach (var (id, modifier) in placed.OrderBy(p => p.Id))
                using (actions.Block($"province:{id}"))
                    actions.Field("add_province_modifier", modifier);
        string actionDir = Path.Combine(modDir, "common", "on_action");
        Directory.CreateDirectory(actionDir);
        ParadoxText.WriteBom(Path.Combine(actionDir, "zz_gen_landmark_on_actions.txt"), actions.ToString());

        loc.Write(Path.Combine(modDir, "localization", "english", "zz_gen_landmark_l_english.yml"));
        Console.WriteLine($"  landmarks: {landmarks.Count} named, {placed.Count} barony(ies) given their modifier, {heights} of them heights");
    }

    /// <summary>A landmark's high ground: what the terrain already makes hard going.</summary>
    private static bool IsHeights(TerrainClass terrain)
        => terrain is TerrainClass.Hills or TerrainClass.Mountains or TerrainClass.DesertMountains;

    /// <summary>
    /// A landmark's modifier by kind and by whether the barony is its high ground: its icon, its
    /// effects, and a line saying why. Each kind is drawn from the real places of its sort — the
    /// basin from Sudbury's ores and the Ries crater's building stone, the rift from the East African
    /// Rift's lakes and escarpments — and none repeats what the barony's terrain already gives,
    /// which is why nothing here cuts supply on hills and mountains.
    /// </summary>
    private static (string Icon, (string Field, double Value)[] Effects, string Description) Worth(string kind, bool heights)
        => (kind, heights) switch
    {
        ("basin", false) => ("county_modifier_development_positive",
            [("development_growth_factor", 0.1), ("supply_limit_mult", 0.1)],
            "This land lies on the floor of the great crater. The lake that once filled it left a deep, rich soil behind."),
        ("basin", true) => ("rock_positive",
            [("tax_mult", 0.1), ("build_gold_cost", -0.05)],
            "The crater's rim is full of ore melted out by the impact, and its rock is cut for building all over the basin."),
        ("sea", false) => ("economy_positive",
            [("tax_mult", 0.1), ("development_growth_factor", 0.05)],
            "These shores ring a round sea in a crater's bowl. The rim keeps out the storms, so the harbours are safe and the fishing is good all year."),
        ("sea", true) => ("martial_positive",
            [("defender_holding_advantage", 2)],
            "The crater's rim drops straight into the sea here. No fleet can land under these cliffs."),
        ("scar", _) => ("rock_mixed",
            [("tax_mult", 0.05), ("stationed_maa_damage_mult", 0.05), ("supply_limit_mult", -0.1), ("travel_danger", 5)],
            heights
                ? "The rims of the craters stand up in broken rings. Iron that fell from the sky is dug out of them and forged into fine blades."
                : "The ground here is pocked with craters from something that fell out of the sky. Its iron makes fine blades, but the broken ground is hard to cross or feed an army on."),
        ("rift", false) => ("county_modifier_development_positive",
            [("development_growth_factor", 0.1), ("supply_limit_mult", 0.1)],
            "The rift floor lies sunk between high walls. Its lakes and dark volcanic soil make it the richest land for many miles."),
        ("rift", true) => ("martial_mixed",
            [("defender_holding_advantage", 3), ("travel_danger", 5)],
            "The rift's walls rise in steep steps of broken rock. They are easy to hold and hard to climb, and the ground still shakes now and then."),
        ("range", false) => ("economy_positive",
            [("tax_mult", 0.1)],
            "Where the valleys of the great range open onto the plain, market towns trade the wool and ore brought down from the mountains."),
        ("range", true) => ("martial_positive",
            [("defender_holding_advantage", 2), ("hostile_raid_time", 0.25)],
            "In the foothills of the great range, every valley has its hill fort, and raiders who come up them are seen long before they arrive."),
        ("wall", false) => ("martial_positive",
            [("garrison_size", 0.1), ("hostile_raid_time", 0.5)],
            "The wall of mountains stands over this land. Raiders can only come through the few passes, and the holds here are built to watch them."),
        ("wall", true) => ("martial_positive",
            [("defender_holding_advantage", 4)],
            "These holds are built into the flanks of the wall itself, where an army has to climb to reach them."),
        _ => ("stewardship_positive", [], ""),
    };
}
