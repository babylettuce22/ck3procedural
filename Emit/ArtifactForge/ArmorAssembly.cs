namespace Ck3MapGen.Emit;

using Ck3MapGen.Io;

/// <summary>
/// Merges several skinned armour parts into one garment.
///
/// **Assembly is easier here than it is for weapons, and harder in one place.** Easier because there
/// is no placement problem at all: every part was rigged against the same body, so it is already
/// where it belongs and merging is pure concatenation — no sockets, no translation, no rotation, and
/// therefore none of the normal-and-tangent transforming that the weapon forge deliberately stops
/// short of. Harder because these parts are SKINNED, so the merge has to carry <c>ix</c> and
/// <c>w</c> alongside the vertex streams; a weapon is four rigid lumps and has neither.
///
/// **The bone indices are the thing that could quietly break.** <c>ix</c> holds positions into the
/// mesh's own bone list, so merging two parts whose skeletons differ would silently point half the
/// vertices at the wrong joints — the classic symptom being a garment whose sleeves follow the legs.
/// It is safe here only because every part in a library shares one identical bone list, which
/// <see cref="Merge"/> checks rather than assumes.
/// </summary>
public static class ArmorAssembly
{
    /// <summary>Vertex streams copied through untouched, in file order.</summary>
    private static readonly string[] VertexStreams = ["p", "n", "ta", "u0", "u1"];

    /// <summary>How many floats each stream carries per vertex, for the offset maths.</summary>
    private static int Stride(string stream) => stream switch
    {
        "p" or "n" => 3,
        "ta" => 4,
        _ => 2,
    };

    /// <summary>
    /// Builds one mesh from the named shapes of a parts library.
    ///
    /// Returns a fresh <c>File</c> root ready for <see cref="PdxMesh.Write"/>, or null with a reason
    /// printed when the parts cannot be merged.
    /// </summary>
    public static PdxNode? Merge(
        IReadOnlyList<PdxNode> sources, IReadOnlyList<string> shapeNames, string shapeName)
    {
        // Several sources, because a finished look is not always one file: a base garment and a
        // parts library live separately and still belong in one slot when worn together.
        var shapes = sources
            .SelectMany(lib => lib.Children.Where(c => c.Name == "object").SelectMany(o => o.Children))
            .Where(s => shapeNames.Contains(s.Name))
            .ToList();

        if (shapes.Count == 0)
        {
            Console.WriteLine($"  armour assembly: none of {string.Join(", ", shapeNames)} found");
            return null;
        }

        var meshes = shapes
            .Select(s => (Shape: s, Mesh: s.Children.FirstOrDefault(c => c.Name == "mesh")))
            .Where(x => x.Mesh is not null)
            .Select(x => (x.Shape, Mesh: x.Mesh!))
            .ToList();

        if (meshes.Count == 0) return null;

        // Every part must agree on the skeleton, because ix indexes into it BY POSITION. Two parts
        // with the same bones in a different order would merge without complaint and deform wrongly.
        var skeleton = meshes[0].Shape.Children.FirstOrDefault(c => c.Name == "skeleton");
        string[] bones = skeleton is null ? [] : [.. skeleton.Children.Select(b => b.Name)];

        foreach (var (part, _) in meshes.Skip(1))
        {
            var other = part.Children.FirstOrDefault(c => c.Name == "skeleton");
            string[] mine = other is null ? [] : [.. other.Children.Select(b => b.Name)];

            if (mine.SequenceEqual(bones)) continue;

            Console.WriteLine($"  armour assembly: {part.Name} has a different skeleton from "
                + $"{meshes[0].Shape.Name} ({mine.Length} bones against {bones.Length}) - refusing "
                + "to merge, because bone indices are positional and would bind to the wrong joints");
            return null;
        }

        // ONE MESH NODE PER MATERIAL, not one for everything.
        //
        // A shape may hold several mesh nodes and the .asset declares a meshsettings for each, which
        // is how a garment assembled from two sources keeps both textures. Collapsing them into one
        // node would hand every part whichever material happened to be first - merging a gambeson
        // into a plate suit would paint the gambeson in plate.
        var batches = meshes
            .GroupBy(x => x.Mesh.Children.FirstOrDefault(c => c.Name == "material")?.Prop("diff")?.Text ?? "")
            .ToList();

        var assembled = new PdxNode(shapeName);

        foreach (var batch in batches)
        {
            var merged = new PdxNode("mesh");
            int vertexBase = 0;

            var streams = VertexStreams.ToDictionary(x => x, _ => new List<float>());
            var tris = new List<int>();
            var boneIx = new List<int>();
            var weights = new List<float>();
            int influences = 0;

            foreach (var (_, mesh) in batch)
            {
                int verts = mesh.Floats("p").Length / 3;

                foreach (string stream in VertexStreams)
                {
                    float[] data = mesh.Floats(stream);

                    // A part missing a stream the others have would shift every later part's data
                    // by its own length, so pad rather than skip.
                    if (data.Length == 0) data = new float[verts * Stride(stream)];

                    streams[stream].AddRange(data);
                }

                foreach (int i in mesh.Ints("tri")) tris.Add(i + vertexBase);

                if (mesh.Children.FirstOrDefault(c => c.Name == "skin") is { } skin)
                {
                    int[] counts = skin.Ints("bones");
                    if (counts.Length > 0) influences = Math.Max(influences, counts[0]);

                    boneIx.AddRange(skin.Ints("ix"));
                    weights.AddRange(skin.Floats("w"));
                }

                vertexBase += verts;
            }

            foreach (string stream in VertexStreams)
                merged.Set(stream, PdxProp.Of([.. streams[stream]]));

            merged.Set("tri", PdxProp.Of([.. tris]));

            float[] positions = streams["p"].ToArray();
            merged.Set("boundingsphere", PdxProp.Of(BoundingSphere(positions)));

            var aabb = new PdxNode("aabb");
            var (min, max) = Bounds(positions);
            aabb.Set("min", PdxProp.Of(min));
            aabb.Set("max", PdxProp.Of(max));
            merged.Children.Add(aabb);

            if (batch.First().Mesh.Children.FirstOrDefault(c => c.Name == "material") is { } material)
                merged.Children.Add(material);

            if (boneIx.Count > 0)
            {
                var skin = new PdxNode("skin");
                skin.Set("bones", PdxProp.Of(influences));
                skin.Set("ix", PdxProp.Of([.. boneIx]));
                skin.Set("w", PdxProp.Of([.. weights]));
                merged.Children.Add(skin);
            }

            assembled.Children.Add(merged);
        }

        if (skeleton is not null) assembled.Children.Add(skeleton);

        var root = new PdxNode("File");
        root.Set("pdxasset", PdxProp.Of(1, 0));
        root.Child("object").Children.Add(assembled);

        return root;
    }

    private static (float[] Min, float[] Max) Bounds(float[] p)
    {
        float[] min = [float.MaxValue, float.MaxValue, float.MaxValue];
        float[] max = [float.MinValue, float.MinValue, float.MinValue];

        for (int i = 0; i + 2 < p.Length; i += 3)
        {
            for (int c = 0; c < 3; c++)
            {
                min[c] = Math.Min(min[c], p[i + c]);
                max[c] = Math.Max(max[c], p[i + c]);
            }
        }

        return (min, max);
    }

    /// <summary>
    /// Centre of the bounding box and the distance to the furthest vertex — not the box's own
    /// half-diagonal, which would over-report and is what the engine culls against.
    /// </summary>
    private static float[] BoundingSphere(float[] p)
    {
        var (min, max) = Bounds(p);
        float[] c = [(min[0] + max[0]) / 2, (min[1] + max[1]) / 2, (min[2] + max[2]) / 2];
        double radius = 0;

        for (int i = 0; i + 2 < p.Length; i += 3)
        {
            double dx = p[i] - c[0], dy = p[i + 1] - c[1], dz = p[i + 2] - c[2];
            radius = Math.Max(radius, Math.Sqrt(dx * dx + dy * dy + dz * dz));
        }

        return [c[0], c[1], c[2], (float)radius];
    }
}
