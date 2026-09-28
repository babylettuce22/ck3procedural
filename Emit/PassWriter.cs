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
}
