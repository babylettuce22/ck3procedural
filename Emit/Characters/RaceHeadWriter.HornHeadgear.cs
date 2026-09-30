using System.Text;
using System.Text.RegularExpressions;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

public static partial class RaceHeadWriter
{
    // ---- Headgear over horns ------------------------------------------------------------------

    /// <summary>
    /// Every vanilla accessory definition, by key, in the engine's load order (filename order, last
    /// definition wins) — the bodies <see cref="WriteHornCrowns"/> and <see cref="WriteHornHeadgear"/> copy.
    /// </summary>
    private static Dictionary<string, string[]> DeclaredAccessories(string gameDir)
    {
        var declared = new Dictionary<string, string[]>(StringComparer.Ordinal);
        string accDir = Path.Combine(gameDir, "gfx", "portraits", "accessories");
        if (!Directory.Exists(accDir)) return declared;
        foreach (string file in Directory.GetFiles(accDir, "*.txt").OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal))
        {
            string[] lines = File.ReadAllLines(file);
            foreach (var (key, first, last, closed) in ScriptScan.TopLevelDeclarations(lines, c => char.IsLetterOrDigit(c) || c == '_'))
                if (closed) declared[key] = lines[first..(last + 1)];
        }

        return declared;
    }

    /// <summary>
    /// The headgear half of "horns on display" (<see cref="HornHeadgear"/> has the lists and how they
    /// were measured). Each listed vanilla accessory is copied from the installed game with one tag
    /// added to its <c>set_tags</c>:
    /// <list type="bullet">
    /// <item><see cref="HornHeadgear.Bare"/>: <see cref="HornHeadgear.FullTag"/>, and a first entity line
    /// drawing nothing on a horned head (<see cref="HornsWornTag"/>) — the hat gives way to the horns.</item>
    /// <item><see cref="HornHeadgear.Stump"/>: <see cref="HornHeadgear.StumpTag"/> — the hat stays, the
    /// horns are filed stumps.</item>
    /// <item><see cref="HornHeadgear.Full"/>: <see cref="HornHeadgear.FullTag"/> — hat and full horns.</item>
    /// </list>
    /// The horn and ornament accessories read both tags before their own headgear rules. A non-horned
    /// character sees only an extra tag nothing else reads, so every other portrait is unchanged. The
    /// hat keeps its own tags: a Bare hat that was <c>snug_headgear</c> still flattens the hair under it
    /// as vanilla does, although it is not drawn — a small cost against rewriting the hair accessories.
    /// Returns how many accessories were re-declared.
    /// </summary>
    private static int WriteHornHeadgear(string modDir, string gameDir, List<string> log)
    {
        var declared = DeclaredAccessories(gameDir);
        var text = new StringBuilder("""
            # Generated (Emit/RaceHeadWriter.HornHeadgear.cs): vanilla headgear that clashes with horns or clears
            # them, copied from the installed game with one tag added (gen_horns_full / gen_horns_stump, read by
            # gen_horns.txt) and, for hats that give way to horns, a first entity line drawing nothing on a
            # horned head. Lists and measurements: MapGen/Peoples/HornHeadgear.cs. Loads after vanilla's
            # accessory files, so these definitions replace theirs.


            """);
        int count = 0, missing = 0;
        foreach (var (key, body) in declared)
        {
            string? tag = HornHeadgear.Bare.Contains(key) || HornHeadgear.Full.Contains(key) ? HornHeadgear.FullTag
                : HornHeadgear.Stump.Contains(key) ? HornHeadgear.StumpTag : null;
            if (tag is null) continue;
            bool bare = HornHeadgear.Bare.Contains(key);

            int tags = Array.FindIndex(body, l => Regex.IsMatch(ScriptScan.StripComment(l), @"^\s*set_tags\s*="));
            int firstEntity = Array.FindIndex(body, l => Regex.IsMatch(ScriptScan.StripComment(l), @"^\s*entity\s*=\s*\{"));
            if (firstEntity < 0) { missing++; continue; }

            for (int i = 0; i < body.Length; i++)
            {
                string line = body[i].TrimStart('﻿');
                if (i == 0 && tags < 0)
                {
                    text.Append(line).Append('\n').Append($"\tset_tags = \"{tag}\"\n");
                    continue;
                }

                if (i == tags)
                    line = Regex.Replace(line, @"set_tags\s*=\s*""([^""]*)""",
                        m => $"set_tags = \"{(m.Groups[1].Value.Trim().Length > 0 ? m.Groups[1].Value.TrimEnd() + "," : "")}{tag}\"");
                if (bare && i == firstEntity)
                    text.Append($"\tentity = {{ required_tags = \"{HornsWornTag}\"\tshared_pose_entity = head }}\t# horned: no hat\n");
                text.Append(line).Append('\n');
            }

            text.Append('\n');
            count++;
        }

        string outDir = Path.Combine(modDir, "gfx", "portraits", "accessories");
        Directory.CreateDirectory(outDir);
        ParadoxText.WriteBom(Path.Combine(outDir, "zz_gen_horn_headgear.txt"), text.ToString());
        int listed = HornHeadgear.Bare.Count + HornHeadgear.Stump.Count + HornHeadgear.Full.Count;
        if (count + missing < listed)
            log.Add($"  horn headgear: {listed - count - missing} listed hats are not in this game version");
        return count;
    }
}
