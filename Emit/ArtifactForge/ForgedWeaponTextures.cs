namespace Ck3MapGen.Emit;

using Ck3MapGen.MapGen;
using System.IO;

/// <summary>
/// Ships the textures a forged weapon names but the game does not provide.
///
/// **Why this exists.** The forge is a texture *name* pass-through: <see cref="WeaponPart.Diffuse"/>
/// reads <c>diff</c> straight out of the part's material node and <see cref="ForgedWeaponWriter"/>
/// writes it into the emitted <c>.asset</c>. Nothing copies an image. That is exactly right for a
/// vanilla part — the <c>.dds</c> is already in the game tree and CK3 resolves textures globally by
/// filename, which is also what lets one weapon carry four different vanilla atlases at once.
///
/// It stops being right the moment a part comes from outside the game. A harvested Sketchfab model
/// names textures that exist nowhere CK3 will look, and the failure is the worst kind: not a missing
/// model, but a weapon rendered with an empty texture, which on a portrait reads as a hole punched
/// through the character.
///
/// **How a non-vanilla part is detected.** By fact, not by naming convention. A texture is foreign
/// if its basename is absent from the game's own <c>gfx/models/artifacts</c> index — the same index
/// and the same bare-filename rule <see cref="ForgedWeaponRender"/> already resolves against. No
/// prefix to remember, nothing to keep in sync, and a part that is later replaced by a vanilla
/// equivalent stops being flagged on its own.
///
/// Foreign textures are then looked for in <c>textures/</c> beside the parts library, mirroring
/// <c>CustomArmorStep.CopyTextures</c> on the armour side, and copied next to the emitted meshes.
/// Beside the mesh is not required — global resolution means anywhere in the mod would do — it is
/// simply the tidiest place, and it keeps a forged weapon's files together.
///
/// **A texture that is in neither place is a hard error, deliberately.** It cannot be repaired at
/// runtime and shipping anyway would emit the hole-through-the-character weapon. The whole harvest
/// pipeline is built on turning silent visual faults into loud ones; this is that same trade, made
/// at the last point where the fault is still cheap to fix.
/// </summary>
public static class ForgedWeaponTextures
{
    /// <summary>Every <c>.dds</c> under <paramref name="root"/>, keyed by bare filename.</summary>
    private static Dictionary<string, string> Index(string root)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(root)) return map;

        foreach (string path in Directory.EnumerateFiles(root, "*.dds", SearchOption.AllDirectories))
            map.TryAdd(Path.GetFileName(path), path);

        return map;
    }

    /// <summary>
    /// Copies every foreign texture the forged weapons name into the mod, and reports any that
    /// cannot be found at all.
    /// </summary>
    /// <param name="partsDirs">
    /// The directories the parts libraries were loaded from. Each is probed for a <c>textures/</c>
    /// child. Passed in rather than recomputed because <see cref="WeaponForgeStep.Locate"/> already
    /// resolved them, and a second guess could pick a different checkout.
    /// </param>
    /// <returns>The number of textures copied.</returns>
    /// <param name="materials">
    /// Every material the emitted <c>.asset</c> files will declare. Taken as a flat list rather than
    /// as weapons because the composed path has no whole weapons to hand — its geometry is shared
    /// pieces, and a pairing owns no textures of its own.
    /// </param>
    /// <param name="parts">
    /// The parts behind those materials, used only to name the family at fault when a texture
    /// cannot be found. Attribution is a diagnostic, so an empty list degrades the message rather
    /// than breaking the check.
    /// </param>
    public static int Ship(
        string modDir, string gameDir, IEnumerable<string> partsDirs,
        IReadOnlyList<ForgedMaterial> materials, IReadOnlyList<WeaponPart> parts)
    {
        // Every texture name the emitted .assets will contain.
        var needed = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var m in materials)
        {
            foreach (string name in new[] { m.Diffuse, m.Normal, m.Specular })
            {
                if (!string.IsNullOrWhiteSpace(name)) needed.Add(name);
            }
        }

        if (needed.Count == 0) return 0;

        var vanilla = Index(Path.Combine(gameDir, "gfx", "models", "artifacts"));

        var supplied = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (string dir in partsDirs)
        {
            foreach (var (file, path) in Index(Path.Combine(dir, "textures")))
                supplied.TryAdd(file, path);
        }

        string outDir = Path.Combine(
            modDir, ForgedWeaponWriter.ModelDir.Replace('/', Path.DirectorySeparatorChar));

        var copied = new List<string>();
        var missing = new List<string>();

        foreach (string name in needed)
        {
            // The game already has it: the overwhelmingly common case, and nothing to do.
            if (vanilla.ContainsKey(name)) continue;

            if (supplied.TryGetValue(name, out string? source))
            {
                Directory.CreateDirectory(outDir);
                File.Copy(source, Path.Combine(outDir, name), overwrite: true);
                copied.Add(name);
            }
            else
            {
                missing.Add(name);
            }
        }

        if (copied.Count > 0)
        {
            Console.WriteLine($"  forged weapons: shipped {copied.Count} non-vanilla texture(s) - "
                + string.Join(", ", copied));
        }

        if (missing.Count > 0) Fail(missing, materials, parts, supplied.Count);

        return copied.Count;
    }

    /// <summary>
    /// Names the missing file, the families that asked for it and where it was looked for, because
    /// the fix is always one of three things and the message should say which.
    /// </summary>
    private static void Fail(
        List<string> missing, IReadOnlyList<ForgedMaterial> materials,
        IReadOnlyList<WeaponPart> parts, int suppliedCount)
    {
        foreach (string name in missing)
        {
            // Attribute by the part's own diffuse where we can; a weapon list is the fallback,
            // since normal/specular are not exposed per part.
            var families = parts
                .Where(p => string.Equals(p.Diffuse, name, StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Family)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Attribute by the part's own diffuse where we can. A normal or specular map is not
            // exposed per part, so the fallback names the diffuse that travels with it - which is
            // what points at the family whose export is short a file.
            string blamed = families.Count > 0
                ? "family " + string.Join(", ", families)
                : "alongside " + string.Join(", ", materials
                    .Where(m => string.Equals(m.Normal, name, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(m.Specular, name, StringComparison.OrdinalIgnoreCase))
                    .Select(m => m.Diffuse)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase));

            Console.WriteLine($"  forged weapons: MISSING TEXTURE {name} ({blamed})");
        }

        throw new InvalidOperationException(
            $"{missing.Count} forged-weapon texture(s) exist in neither the game's "
            + $"gfx/models/artifacts nor any parts library's textures/ folder "
            + $"({suppliedCount} file(s) available there). A weapon shipped with an unresolvable "
            + "texture renders as a hole through the character, so this is fatal rather than a "
            + "warning. Fix by one of: converting the source texture to .dds into "
            + "assets/<library>/textures/, correcting the texture name in the part's material in "
            + "Blender and re-exporting the library, or removing the family from the library.");
    }

    /// <summary>
    /// The average colour of a texture's opaque texels, or null when it cannot be found or read.
    ///
    /// Used to colour a part the recolour never touched — an attached lead keeps the textures it was
    /// cut with, so the honest colour for it in an icon is whatever those textures actually are.
    /// Transparent texels are skipped because a weapon atlas is mostly empty space, and averaging
    /// that in washes every part toward the background.
    /// </summary>
    public static (byte R, byte G, byte B)? AverageColour(string gameDir, string diffuse)
    {
        if (string.IsNullOrWhiteSpace(diffuse)) return null;

        string? path = Find(gameDir, diffuse);
        if (path is null) return null;

        if (Io.DdsReader.Load(path) is not { } image) return null;

        long r = 0, g = 0, b = 0, n = 0;

        for (int i = 0; i + 3 < image.Bgra.Length; i += 4)
        {
            if (image.Bgra[i + 3] < 128) continue;

            b += image.Bgra[i];
            g += image.Bgra[i + 1];
            r += image.Bgra[i + 2];
            n++;
        }

        return n == 0 ? null : ((byte)(r / n), (byte)(g / n), (byte)(b / n));
    }

    /// <summary>First match for a bare texture name anywhere under the game's model tree.</summary>
    private static string? Find(string gameDir, string name)
    {
        string root = Path.Combine(gameDir, "gfx", "models");
        if (!Directory.Exists(root)) return null;

        foreach (string path in Directory.EnumerateFiles(root, name, SearchOption.AllDirectories))
            return path;

        return null;
    }
}
