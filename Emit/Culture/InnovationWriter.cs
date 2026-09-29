using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// Writes the innovations this run invented.
///
/// Additive, like the cultures: vanilla's tech tree is left declared and untouched, and the
/// generated entries are extra branches on it. That is not merely convenient — the generated
/// culture histories sample vanilla's innovations for everything except this, so removing them
/// would leave every culture unable to build a keep.
///
/// Kept apart from whichever system invents them because an innovation is a general shape.
/// Men-at-arms are the first caller and will not be the last; a generated building or decision
/// wants the same emitter with a different <c>unlock_*</c> line.
/// </summary>
public static class InnovationWriter
{
    public static void WriteAll(string modDir, InnovationMap innovations)
    {
        if (innovations.All.Count == 0) return;

        WriteDefinitions(modDir, innovations);
        WriteLocalisation(modDir, innovations);
    }

    private static void WriteDefinitions(string modDir, InnovationMap innovations)
    {
        string dir = Path.Combine(modDir, "common", "culture", "innovations");
        Directory.CreateDirectory(dir);

        var b = new JominiBuilder();
        b.Comment("Generated innovations. Vanilla's tree is left intact and these hang off it.");
        b.Blank();

        foreach (var innovation in innovations.All)
        {
            using (b.Block(innovation.Key))
            {
                b.Field("culture_era", innovation.Era);
                b.Field("group", innovation.Group);
                b.Field("skill", innovation.Skill);

                if (innovation.Icon is not null) b.Quoted("icon", innovation.Icon);

                if (innovation.Potential.Count > 0)
                {
                    b.Blank();
                    using (b.Block("potential"))
                        foreach (string line in innovation.Potential)
                            b.Raw(b.IndentAt(b.Depth) + line + "\n");
                }

                if (innovation.UnlockMenAtArms.Count > 0
                    || innovation.UnlockBuildings.Count > 0
                    || innovation.UnlockDecisions.Count > 0)
                {
                    b.Blank();
                    foreach (string key in innovation.UnlockMenAtArms) b.Field("unlock_maa", key);
                    foreach (string key in innovation.UnlockBuildings) b.Field("unlock_building", key);
                    foreach (string key in innovation.UnlockDecisions) b.Field("unlock_decision", key);
                }

                Modifiers("parameters", innovation.Parameters);
                Modifiers("character_modifier", innovation.CharacterModifier);
                Modifiers("culture_modifier", innovation.CultureModifier);
                Modifiers("county_modifier", innovation.CountyModifier);

                if (innovation.Flags.Count > 0)
                {
                    b.Blank();
                    foreach (string flag in innovation.Flags) b.Field("flag", flag);
                }

                void Modifiers(string name, Dictionary<string, string> entries)
                {
                    if (entries.Count == 0) return;

                    b.Blank();
                    using (b.Block(name))
                        foreach (var (key, value) in entries) b.Field(key, value);
                }
            }

            b.Blank();
        }

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_innovations.txt"), b.ToString());
    }

    /// <summary>
    /// The name and the description, under the innovation's own key — CK3's documented fallback
    /// is the key itself for the name and the key plus <c>_desc</c> for the description, so
    /// missing either one prints the raw key into the culture window.
    /// </summary>
    private static void WriteLocalisation(string modDir, InnovationMap innovations)
    {
        var loc = new LocFile();

        foreach (var innovation in innovations.All)
        {
            loc.Add(innovation.Key, innovation.Name);
            loc.AddBuilt($"{innovation.Key}_desc", innovation.Description);
        }

        loc.Write(Path.Combine(modDir, "localization", "english",
            "gen_innovations_l_english.yml"));
    }
}
