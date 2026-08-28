namespace Ck3MapGen.Emit;

using Ck3MapGen.Io;
using Ck3MapGen.MapGen;
using System.IO;

/// <summary>
/// Writes forged weapons into the mod: the binary <c>.mesh</c>, and the <c>.asset</c> that turns
/// it into an entity the portrait can draw.
///
/// The <c>.asset</c> is the piece that makes a mesh usable. It declares a <c>pdxmesh</c> naming the
/// file, one <c>meshsettings</c> block per material, and an <c>entity</c> — and it is the entity
/// name that a generated artifact's visual points at (see <see cref="WeaponAssets"/>).
///
/// Two details decide whether it works at all, both taken from vanilla rather than guessed:
///
/// * <c>meshsettings.name</c> must equal the **shape node name inside the .mesh**, and a shape with
///   several materials gets several blocks sharing that name with incrementing <c>index</c>.
///   Vanilla's <c>tgp_japanese_dagger_01_a.asset</c> is the worked example.
/// * Textures are bare filenames, resolved globally rather than relative to the asset — so a mixed
///   weapon may name textures from two different vanilla folders and nothing needs copying.
///
/// The shader is the portrait one. A weapon drawn in a character's hand is a portrait attachment,
/// not court furniture, so it takes <c>portrait_attachment</c> from <c>jomini/portrait.shader</c> —
/// the same pair vanilla's own portrait weapon entities use.
/// </summary>
public static class ForgedWeaponWriter
{
    /// <summary>Where generated weapon meshes and their assets live inside the mod.</summary>
    public const string ModelDir = "gfx/models/artifacts/gen_weapons";

    private const string Shader = "portrait_attachment";

    /// <summary>
    /// Shader variant that reads a pattern mask and a variation, which is what makes a weapon
    /// recolourable. Same <c>shader_file</c> as the plain one — 591 vanilla assets pair
    /// <c>portrait_attachment_pattern</c> with <c>jomini/portrait.shader</c>.
    /// </summary>
    private const string PatternShader = "portrait_attachment_pattern";
    private const string ShaderFile = "gfx/FX/jomini/portrait.shader";

    /// <summary>Entity name for a forged weapon — what a visual's <c>asset</c> field references.</summary>
    public static string EntityName(string weaponName) => $"{weaponName}_entity";

    /// <summary>
    /// Writes every forged weapon, one <c>.mesh</c> plus one <c>.asset</c> each, and returns the
    /// entity name of each so callers can point visuals at them.
    /// </summary>
    public static List<string> WriteAll(
        string modDir, IReadOnlyList<ForgedWeapon> weapons, ForgedRecolour? recolour = null)
    {
        string dir = Path.Combine(modDir, ModelDir.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(dir);

        var entities = new List<string>();

        foreach (var w in weapons)
        {
            PdxMesh.Write(Path.Combine(dir, $"{w.Name}.mesh"), w.Root);
            ParadoxText.WriteBom(Path.Combine(dir, $"{w.Name}.asset"), AssetText(w, recolour));
            entities.Add(EntityName(w.Name));
        }

        return entities;
    }

    /// <summary>Builds the .asset text for one forged weapon.</summary>
    public static string AssetText(ForgedWeapon w, ForgedRecolour? recolour = null)
    {
        bool patterned = recolour is not null;
        string shader = patterned ? PatternShader : Shader;

        var b = new JominiBuilder();
        b.Comment($"Procedurally forged weapon: {w.Name}\n"
            + "Assembled from harvested parts by MapGen/WeaponForge.cs.\n"
            + "meshsettings.name must match the shape node inside the .mesh; one block per\n"
            + "material, addressed by index in mesh-node order.");
        b.Blank();

        using (b.Block("pdxmesh"))
        {
            b.Quoted("name", MeshName(w.Name));
            b.Quoted("file", $"{w.Name}.mesh");

            for (int i = 0; i < w.Materials.Count; i++)
            {
                var m = w.Materials[i];
                b.Blank();

                using (b.Block("meshsettings"))
                {
                    b.Quoted("name", w.ShapeName);
                    b.Field("index", i);
                    b.Quoted("texture_diffuse", m.Diffuse);
                    b.Quoted("texture_normal", m.Normal);
                    b.Quoted("texture_specular", m.Specular);
                    b.Quoted("shader", shader);
                    b.Quoted("shader_file", ShaderFile);
                }
            }
        }

        b.Blank();

        using (b.Block("entity"))
        {
            b.Quoted("name", EntityName(w.Name));
            b.Quoted("pdxmesh", MeshName(w.Name));

            // The recolour hook. `pattern_mask` picks which of R/G/B/A applies to each texel and is
            // sampled with UV0 -- the model's own atlas UV, NOT the pattern UV -- while the swatch
            // itself tiles over UV1. Both live on the entity, never on meshsettings, so one mask
            // serves every material batch in the weapon.
            if (recolour is { } r)
            {
                using (b.Block("game_data"))
                using (b.Block("portrait_entity_user_data"))
                using (b.Block("portrait_accessory"))
                {
                    b.Quoted("pattern_mask", r.MaskFor(w.Name));
                    b.Quoted("variation", r.VariationFor(w.Name));
                }
            }
        }

        return b.ToString();
    }

    private static string MeshName(string weaponName) => $"{weaponName}_mesh";
}
