using System.Globalization;
using System.Text;

namespace Ck3MapGen.Emit;

/// <summary>
/// The <c>object={ … }</c> block of a <c>gfx/map/map_object_data</c> file, shared by every writer
/// that scatters things over the map (trees, animals, city sprawl, environment effects).
///
/// Only the header and the transform lines live here. How a block is closed stays with each
/// writer, because the files differ there and changing it would change what ships: most write
/// <c>transform="…"}</c> even with no instances, while env effects leave the field out when empty.
/// </summary>
internal static class MapObjectBlock
{
    /// <summary>
    /// Opens a block through its <c>count=</c> line. <paramref name="assetField"/> is
    /// <c>pdxmesh</c> for a mesh addressed by name, or <c>entity</c> for an entity.
    /// </summary>
    public static void AppendHeader(StringBuilder sb, string name, string renderPass,
        bool clampToWater, bool generatedContent, string layer, string assetField, string asset, int count)
    {
        sb.Append("object={\n");
        sb.Append($"\tname=\"{name}\"\n");
        sb.Append($"\trender_pass={renderPass}\n");
        sb.Append($"\tclamp_to_water_level={(clampToWater ? "yes" : "no")}\n");
        sb.Append($"\tgenerated_content={(generatedContent ? "yes" : "no")}\n");
        sb.Append($"\tlayer=\"{layer}\"\n");
        sb.Append($"\t{assetField}=\"{asset}\"\n");
        sb.Append($"\tcount={count}\n");
    }

    /// <summary>
    /// One line per instance, newline-separated with none after the last, for inside the quotes of
    /// <c>transform="…"</c>: position, rotation quaternion, scale. Y is written as 0 because the
    /// engine snaps map objects to the full-resolution heightmap at load, and the rotation is about
    /// the vertical axis only, so the quaternion has no X or Z part.
    /// </summary>
    public static void AppendTransforms(StringBuilder sb, List<(float X, float Z, float Angle, float Scale)> instances)
    {
        var culture = CultureInfo.InvariantCulture;

        for (int i = 0; i < instances.Count; i++)
        {
            var (x, z, angle, scale) = instances[i];

            double qy = Math.Sin(angle / 2.0);
            double qw = Math.Cos(angle / 2.0);

            if (i > 0) sb.Append('\n');
            sb.Append(x.ToString("F6", culture)).Append(" 0.000000 ")
              .Append(z.ToString("F6", culture)).Append(" 0.000000 ")
              .Append(qy.ToString("F6", culture)).Append(" 0.000000 ")
              .Append(qw.ToString("F6", culture)).Append(' ')
              .Append(scale.ToString("F6", culture)).Append(' ')
              .Append(scale.ToString("F6", culture)).Append(' ')
              .Append(scale.ToString("F6", culture));
        }
    }
}
