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
    /// What holding one is worth goes by its kind, and is kept small: flavour a player notices on
    /// the province, not a reason to rush for it.
    /// </summary>
    public static void WriteLandmarks(string modDir, IReadOnlyList<Landmark> landmarks)
    {
        // A landmark with no land — a sea named only on the flat map — has nothing to mark here.
        landmarks = landmarks.Where(l => l.Baronies.Length > 0).ToList();
        if (landmarks.Count == 0) return;

        var modifiers = new JominiBuilder();
        modifiers.Comment("On every barony inside a landmark, put there at game start by zz_gen_landmark_on_actions.txt.");
        var regions = new JominiBuilder();
        regions.Comment("One region per landmark, the baronies inside it. Named in zz_gen_landmark_l_english.yml.");
        var loc = new LocFile();

        foreach (var landmark in landmarks)
        {
            string modifier = landmark.Key + "_modifier";
            var (icon, effects, description) = Worth(landmark.Kind);
            using (modifiers.Block(modifier))
            {
                modifiers.Field("icon", icon);
                foreach (var (field, value) in effects) modifiers.Field(field, value, "0.0#");
            }
            modifiers.Blank();

            using (regions.Block(landmark.Key))
            using (regions.Block("provinces"))
                foreach (var row in landmark.Baronies.Order().Chunk(10))
                    regions.Token(string.Join(' ', row));
            regions.Blank();

            loc.Add(landmark.Key, landmark.Name);
            loc.Add(modifier, landmark.Name);
            loc.Add($"{modifier}_desc", description);
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
            foreach (var landmark in landmarks)
                foreach (int id in landmark.Baronies.Order())
                    using (actions.Block($"province:{id}"))
                        actions.Field("add_province_modifier", landmark.Key + "_modifier");
        string actionDir = Path.Combine(modDir, "common", "on_action");
        Directory.CreateDirectory(actionDir);
        ParadoxText.WriteBom(Path.Combine(actionDir, "zz_gen_landmark_on_actions.txt"), actions.ToString());

        loc.Write(Path.Combine(modDir, "localization", "english", "zz_gen_landmark_l_english.yml"));
        Console.WriteLine($"  landmarks: {landmarks.Count} named, {landmarks.Sum(l => l.Baronies.Length)} barony(ies) given their modifier");
    }

    /// <summary>A landmark's modifier by kind: its icon, its effects, and a line saying why.</summary>
    private static (string Icon, (string Field, double Value)[] Effects, string Description) Worth(string kind) => kind switch
    {
        "basin" => ("county_modifier_development_positive",
            [("development_growth_factor", 0.1), ("supply_limit_mult", 0.1)],
            "This land lies low and sheltered inside the ring of the great crater, on ground made rich by the fire that dug it."),
        "sea" => ("economy_positive",
            [("tax_mult", 0.1), ("development_growth_factor", 0.05)],
            "These shores ring a round sea in a crater's bowl, sheltered by the rim from every storm outside it."),
        "scar" => ("stewardship_positive",
            [("tax_mult", 0.1), ("travel_danger", 5)],
            "The ground here is pocked with craters from something that fell out of the sky, and the iron dug from them fetches a good price."),
        "rift" => ("county_modifier_development_positive",
            [("development_growth_factor", 0.15)],
            "The rift floor lies sunk between high walls. Its lakes and dark soil make it the richest land for many miles."),
        "range" => ("martial_positive",
            [("defender_holding_advantage", 3), ("supply_limit_mult", -0.1)],
            "In the foothills of the great range, every valley can be held against an army, and every road is a climb."),
        "wall" => ("martial_positive",
            [("defender_holding_advantage", 4), ("garrison_size", 0.1)],
            "The wall of mountains stands over this land. Its holds are built to watch the few ways through."),
        _ => ("stewardship_positive", [], ""),
    };
}
