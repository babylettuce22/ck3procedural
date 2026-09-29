// Emit/TablePropTucker.cs
namespace Ck3MapGen.Emit;

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Ck3MapGen.Io;

/// <summary>
/// Tucks map-table props under the paper map: every triangle that would stand above the paper
/// over the map is removed from a copy of the tabletop mesh, so a prop at the map's edge reads as
/// sliding under it, and nothing is drawn on top of the map.
///
/// **Why props end up on the paper.** Vanilla lays its four tabletops out against its own map,
/// whose edge is torn away by the blue channel of surround_mask.dds — and its props deliberately
/// reach a few percent into the map's footprint, into that torn-away strip. Ours is a full
/// rectangle, so the strip is paper, and any prop tall enough to stand above the paper plane
/// (<c>FLAT_MAP_HEIGHT</c> 3.92) draws over it: the Eastern table's plate of sweets reaches 460
/// units in from the east edge and stands 250 tall. Whether a vertex shows is
/// <c>meshScale * Y_vanilla + drop &gt; 3.92</c>, so larger maps show more of them.
///
/// **Why tuck rather than move.** The props' shadows are painted into the tabletop textures —
/// the Eastern table's cloth carries the plate's shadow as a black disc. A moved prop leaves its
/// shadow behind and arrives without one; a removed one leaves a shadow on bare cloth. Cutting
/// away only the part over the paper keeps every prop exactly on its painted shadow, and the part
/// that goes is the part the paper would have covered on vanilla's own map.
///
/// **Why it cannot break a game.** Everything is rebuilt from the installed game on every run, so
/// a CK3 update never leaves a stale copy behind. Before touching a file the reader/writer has to
/// reproduce it byte for byte. A removed triangle is collapsed onto one of its own corners, so no
/// vertex, triangle, material or node count changes and nothing moves. An object whose mesh has a
/// skeleton, skin or locators is left vanilla — the candle flames and ep3's lamps hang particle
/// attachments off locators whose frame does not line up with the vertices, and cutting a candle
/// would leave its flame floating. Rotated or unevenly scaled objects are left vanilla. The edited
/// mesh is read back and checked (same nodes, same counts, every index in range, every float
/// finite) before it is used, and the edit ships under new <c>gen_</c> asset, mesh and entity
/// names beside vanilla's textures rather than overriding vanilla's files. Any failure leaves
/// that table's text exactly as it was.
/// </summary>
public static class TablePropTucker
{
    /// <summary>World Y of the paper map (<c>FLAT_MAP_HEIGHT</c>).</summary>
    private const double PaperY = 3.92;

    /// <summary>
    /// The cut reaches this share of the map's width past its edge, so a triangle straddling the
    /// edge goes rather than leaving a sliver over the paper.
    /// </summary>
    private const double EdgeMargin = 0.002;

    private const string Prefix = "gen_";

    private static readonly Regex EntityRx = new("entity=\"([^\"]*)\"", RegexOptions.Compiled);
    private static readonly Regex TransformRx = new("transform=\"([^\"]*)\"", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex NameRx = new("\\bname\\s*=\\s*\"([^\"]+)\"", RegexOptions.Compiled);
    private static readonly Regex PdxmeshRefRx = new("\\bpdxmesh\\s*=\\s*\"([^\"]+)\"", RegexOptions.Compiled);
    private static readonly Regex FileRx = new("\\bfile\\s*=\\s*\"([^\"]+)\"", RegexOptions.Compiled);

    /// <summary>
    /// The map_table text for one style with any edited objects pointed at generated copies, or
    /// the text unchanged. <paramref name="report"/> gets one line.
    /// </summary>
    public static string Apply(string style, string text, string modDir, string gameDir,
        int mapWidth, int mapHeight, List<string> report)
    {
        try
        {
            return ApplyUnchecked(style, text, modDir, gameDir, mapWidth, mapHeight, report);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or FormatException
                                     or IndexOutOfRangeException or ArgumentException or UnauthorizedAccessException)
        {
            report.Add($"{style}: left as vanilla ({e.GetType().Name}: {e.Message})");
            return text;
        }
    }

    private static string ApplyUnchecked(string style, string text, string modDir, string gameDir,
        int mapWidth, int mapHeight, List<string> report)
    {
        var assets = AssetIndex.For(gameDir);
        var skipped = new List<string>();
        var renamed = new Dictionary<string, string>();
        var cuts = new List<string>();
        double margin = EdgeMargin * mapWidth;
        double x0 = -margin, x1 = mapWidth + margin, z0 = -margin, z1 = mapHeight + margin;

        foreach (var (entity, transform) in Objects(text))
        {
            if (entity.Contains("floor", StringComparison.Ordinal) || renamed.ContainsKey(entity)) continue;
            if (transform is null || !IsIdentity(transform) ||
                transform[7] != transform[8] || transform[8] != transform[9])
            {
                skipped.Add($"{entity} (rotated or unevenly scaled)");
                continue;
            }
            if (!assets.Entities.TryGetValue(entity, out var ent) || !assets.Meshes.TryGetValue(ent.MeshName, out var mesh))
            {
                skipped.Add($"{entity} (asset not found)");
                continue;
            }

            byte[] original = File.ReadAllBytes(mesh.Path);
            var root = PdxMesh.Read(mesh.Path);
            if (!PdxMesh.ToBytes(root).AsSpan().SequenceEqual(original))
            {
                skipped.Add($"{entity} (mesh does not round-trip)");
                continue;
            }

            // Anything this object draws over the paper, before deciding whether it may be cut.
            double ox = transform[0], oy = transform[1], oz = transform[2], s = transform[7];
            var counts = new List<int>();
            int cut = 0;
            var edits = new List<(PdxNode Node, int[] Tri)>();
            foreach (var node in MeshNodes(root))
            {
                float[] p = node.Floats("p");
                int[] tri = node.Ints("tri");
                counts.Add(p.Length);
                counts.Add(tri.Length);
                if (p.Length == 0 || tri.Length == 0 || p.Length % 3 != 0 || tri.Length % 3 != 0) continue;

                int[] edited = (int[])tri.Clone();
                int before = cut;
                for (int t = 0; t + 2 < edited.Length; t += 3)
                {
                    if (edited[t] == edited[t + 1] && edited[t + 1] == edited[t + 2]) continue;
                    for (int k = 0; k < 3; k++)
                    {
                        int v = edited[t + k];
                        double x = ox + s * p[v * 3], y = oy + s * p[v * 3 + 1], z = oz + s * p[v * 3 + 2];
                        if (y > PaperY && x > x0 && x < x1 && z > z0 && z < z1)
                        {
                            edited[t + 1] = edited[t + 2] = edited[t];
                            cut++;
                            break;
                        }
                    }
                }
                if (cut > before) edits.Add((node, edited));
            }

            if (cut == 0) continue;
            if (HasNode(root, "skeleton") || HasNode(root, "skin") || HasLocators(root))
            {
                skipped.Add($"{entity} ({cut} triangles over the paper; has locators, left vanilla)");
                continue;
            }

            foreach (var (node, tri) in edits) node.Set("tri", PdxProp.Of(tri));

            string relDir = Path.GetRelativePath(gameDir, Path.GetDirectoryName(ent.AssetPath)!);
            string outDir = Path.Combine(modDir, relDir);
            Directory.CreateDirectory(outDir);

            string meshFile = Prefix + Path.GetFileName(mesh.Path);
            string meshOut = Path.Combine(outDir, meshFile);
            File.WriteAllBytes(meshOut, PdxMesh.ToBytes(root));
            if (!ReadsBack(meshOut, [.. counts]))
            {
                File.Delete(meshOut);
                skipped.Add($"{entity} (edited mesh failed its read-back check)");
                continue;
            }

            string newEntity = Prefix + entity, newMesh = Prefix + ent.MeshName;
            string asset = RenameFirst(NameRx, mesh.Block, ent.MeshName, newMesh);
            asset = RenameFirst(FileRx, asset, Path.GetFileName(mesh.Path), meshFile);
            string entityBlock = RenameFirst(NameRx, ent.Block, entity, newEntity);
            entityBlock = RenameFirst(PdxmeshRefRx, entityBlock, ent.MeshName, newMesh);

            File.WriteAllText(Path.Combine(outDir, Prefix + Path.GetFileName(ent.AssetPath)),
                "# Generated by TablePropTucker from vanilla's " + Path.GetFileName(ent.AssetPath) +
                ": props tucked under the paper map.\n" + asset + "\n\n" + entityBlock + "\n",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            renamed[entity] = newEntity;
            cuts.Add($"{entity} {cut}");
        }

        string result = renamed.Count == 0 ? text : EntityRx.Replace(text, m =>
            renamed.TryGetValue(m.Groups[1].Value, out var to) ? $"entity=\"{to}\"" : m.Value);

        report.Add($"{style}: " + (cuts.Count == 0 ? "nothing over the paper" : $"tucked {string.Join(", ", cuts)} triangles") +
                   (skipped.Count == 0 ? "" : $"; untouched: {string.Join(", ", skipped)}"));
        return result;
    }

    // ------------------------------------------------------------------------------------------
    // The mesh file

    private static IEnumerable<PdxNode> MeshNodes(PdxNode node)
    {
        foreach (var child in node.Children)
        {
            if (child.Name == "mesh") yield return child;
            foreach (var deeper in MeshNodes(child)) yield return deeper;
        }
    }

    private static bool HasNode(PdxNode node, string name) =>
        node.Children.Any(c => c.Name == name || HasNode(c, name));

    private static bool HasLocators(PdxNode root) =>
        root.Children.Any(c => c.Name == "locator" && c.Children.Count > 0);

    /// <summary>The written mesh parses, keeps every node's counts, and every index and float is sane.</summary>
    private static bool ReadsBack(string path, int[] counts)
    {
        var root = PdxMesh.Read(path);
        var nodes = MeshNodes(root).ToList();
        if (nodes.Count * 2 != counts.Length) return false;
        for (int n = 0; n < nodes.Count; n++)
        {
            float[] p = nodes[n].Floats("p");
            int[] tri = nodes[n].Ints("tri");
            if (p.Length != counts[n * 2] || tri.Length != counts[n * 2 + 1]) return false;
            int verts = p.Length / 3;
            foreach (int t in tri) if (t < 0 || t >= verts) return false;
            foreach (float f in p) if (!float.IsFinite(f)) return false;
        }
        return true;
    }

    // ------------------------------------------------------------------------------------------
    // The map_table and .asset text

    /// <summary>
    /// One past the <c>}</c> closing the first <c>{</c> at or after <paramref name="from"/>, with
    /// strings allowed to run over line breaks. Not <see cref="ScriptScan.BlockEnd"/>: that one
    /// treats a line break as an unterminated string, which is right for script, and map_table
    /// files put the newline <i>inside</i> <c>transform="…"</c> — so it swallows the closing brace.
    /// </summary>
    private static int MultilineBlockEnd(string text, int from)
    {
        int depth = 0;
        bool quoted = false;
        for (int i = from; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"') quoted = !quoted;
            else if (quoted) continue;
            else if (c == '{') depth++;
            else if (c == '}' && depth > 0 && --depth == 0) return i + 1;
        }
        return -1;
    }

    private static IEnumerable<(string Entity, double[]? Transform)> Objects(string text)
    {
        int at = 0;
        while (true)
        {
            int start = text.IndexOf("object={", at, StringComparison.Ordinal);
            if (start < 0) yield break;
            int end = MultilineBlockEnd(text, start);
            if (end < 0) yield break;
            string block = text[start..end];
            at = end;

            var e = EntityRx.Match(block);
            if (!e.Success) continue;
            var t = TransformRx.Match(block);
            double[]? v = null;
            if (t.Success)
            {
                var parts = t.Groups[1].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 10)
                {
                    v = new double[10];
                    for (int i = 0; i < 10; i++)
                        if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i])) { v = null; break; }
                }
            }
            yield return (e.Groups[1].Value, v);
        }
    }

    /// <summary>Vanilla writes the identity rotation as four zeros; (0,0,0,1) is accepted too.</summary>
    private static bool IsIdentity(double[] t) =>
        t[3] == 0 && t[4] == 0 && t[5] == 0 && (t[6] == 0 || t[6] == 1);

    /// <summary>Renames the first match, which must be <paramref name="from"/> — anything else means the block is not laid out the way this expects.</summary>
    private static string RenameFirst(Regex rx, string block, string from, string to)
    {
        var m = rx.Match(block);
        if (!m.Success || m.Groups[1].Value != from)
            throw new FormatException($"expected '{from}' first in asset block, found '{(m.Success ? m.Groups[1].Value : "nothing")}'");
        var g = m.Groups[1];
        return block[..g.Index] + to + block[(g.Index + g.Length)..];
    }

    /// <summary>Every tabletop entity and pdxmesh block in the installed game.</summary>
    private sealed class AssetIndex
    {
        public Dictionary<string, (string AssetPath, string Block, string MeshName)> Entities { get; } = [];
        public Dictionary<string, (string Path, string Block)> Meshes { get; } = [];

        public static AssetIndex For(string gameDir)
        {
            var index = new AssetIndex();
            string dir = Path.Combine(gameDir, "gfx", "models", "tabletop");
            if (Directory.Exists(dir))
                foreach (string asset in Directory.GetFiles(dir, "*.asset", SearchOption.AllDirectories))
                    index.Add(asset, File.ReadAllText(asset));
            return index;
        }

        private void Add(string assetPath, string text)
        {
            foreach (var (kind, block) in TopBlocks(text))
            {
                var name = NameRx.Match(block);
                if (!name.Success) continue;
                if (kind == "entity")
                {
                    var mesh = PdxmeshRefRx.Match(block);
                    if (mesh.Success) Entities[name.Groups[1].Value] = (assetPath, block, mesh.Groups[1].Value);
                }
                else
                {
                    var file = FileRx.Match(block);
                    if (file.Success)
                        Meshes[name.Groups[1].Value] =
                            (Path.Combine(Path.GetDirectoryName(assetPath)!, file.Groups[1].Value), block);
                }
            }
        }

        private static readonly Regex BlockStart = new("(?m)^\\s*(entity|pdxmesh)\\s*=\\s*\\{", RegexOptions.Compiled);

        private static IEnumerable<(string Kind, string Block)> TopBlocks(string text)
        {
            foreach (Match m in BlockStart.Matches(text))
            {
                if (ScriptScan.DepthAt(text, m.Index) != 0) continue;
                int open = text.IndexOf('{', m.Index);
                int end = ScriptScan.BlockEnd(text, open);
                if (end < 0) continue;
                int start = m.Index + (m.Value.Length - m.Value.TrimStart().Length);
                yield return (m.Groups[1].Value, text[start..end]);
            }
        }
    }
}
