namespace Ck3MapGen.Io;

using System.IO;
using System.Text;

/// <summary>
/// One typed property on a <see cref="PdxNode"/>. The format carries exactly three payload
/// kinds — int array, float array, single string — and the type char is stored so a file can be
/// round-tripped byte-for-byte.
/// </summary>
public sealed class PdxProp
{
    public required char Type { get; init; }        // 'i', 'f' or 's'
    public int[] Ints { get; init; } = [];
    public float[] Floats { get; init; } = [];
    public string Text { get; init; } = "";

    public static PdxProp Of(params int[] v) => new() { Type = 'i', Ints = v };
    public static PdxProp Of(params float[] v) => new() { Type = 'f', Floats = v };
    public static PdxProp Of(string v) => new() { Type = 's', Text = v };
}

/// <summary>A node in the .mesh tree: a name, ordered properties, and ordered children.</summary>
public sealed class PdxNode(string name)
{
    public string Name { get; set; } = name;

    /// <summary>Ordered — the binary format is a flat stream, so property order is file order.</summary>
    public List<KeyValuePair<string, PdxProp>> Props { get; } = [];

    public List<PdxNode> Children { get; } = [];

    public PdxProp? Prop(string key)
    {
        foreach (var kv in Props) if (kv.Key == key) return kv.Value;
        return null;
    }

    public float[] Floats(string key) => Prop(key)?.Floats ?? [];
    public int[] Ints(string key) => Prop(key)?.Ints ?? [];

    public void Set(string key, PdxProp value)
    {
        for (int i = 0; i < Props.Count; i++)
        {
            if (Props[i].Key != key) continue;
            Props[i] = new(key, value);
            return;
        }

        Props.Add(new(key, value));
    }

    public PdxNode Child(string name)
    {
        foreach (var c in Children) if (c.Name == name) return c;
        var made = new PdxNode(name);
        Children.Add(made);
        return made;
    }
}

/// <summary>
/// Reader and writer for Paradox <c>.mesh</c> files — the Clausewitz binary model format.
///
/// The container is far simpler than it looks, and is the reason procedural weapon assembly can
/// live in this generator rather than in a Blender build step. The whole grammar is:
///
/// <code>
/// header    "@@b@"
/// object    '[' x depth, name bytes, 0x00
/// property  '!', u8 nameLen, name bytes, typeChar, i32 count, payload
///           'i' -> count x i32      'f' -> count x f32
///           's' -> i32 (len+1), len bytes latin-1, 0x00
/// </code>
///
/// Depth is expressed by the count of leading '[' rather than by any close marker, so the tree is
/// rebuilt by comparing each object's depth with the current one. The root is implicit: the file
/// starts with the header, then the root's own properties (<c>pdxasset</c>), then the first real
/// object at depth 1.
///
/// Verified against vanilla 1.19 <c>ep1_western_sword_01_a_portrait.mesh</c> and against files
/// written by io_pdx_mesh 0.91.0, whose byte layout this matches — including the quirk that a
/// string's declared length is <c>len + 1</c> to account for its terminator.
/// </summary>
public static class PdxMesh
{
    private const string Header = "@@b@";

    public static PdxNode Read(string path)
    {
        byte[] d = File.ReadAllBytes(path);

        if (d.Length < 4 || Encoding.ASCII.GetString(d, 0, 4) != Header)
            throw new InvalidDataException($"Not a PDX mesh (bad header): {path}");

        int pos = 4;
        var root = new PdxNode("File");

        // depthStack[i] is the node currently open at depth i; index 0 is the implicit root.
        var depthStack = new List<PdxNode> { root };

        while (pos < d.Length)
        {
            byte c = d[pos];

            if (c == (byte)'[')
            {
                int depth = 0;
                while (pos < d.Length && d[pos] == (byte)'[') { depth++; pos++; }

                int start = pos;
                while (pos < d.Length && d[pos] != 0) pos++;
                string name = Encoding.Latin1.GetString(d, start, pos - start);
                pos++;                                  // terminator

                // A node at depth N hangs off whatever is open at depth N-1.
                if (depth > depthStack.Count) throw new InvalidDataException($"Depth jump at {pos} in {path}");
                depthStack.RemoveRange(depth, depthStack.Count - depth);

                var node = new PdxNode(name);
                depthStack[^1].Children.Add(node);
                depthStack.Add(node);
            }
            else if (c == (byte)'!')
            {
                pos++;
                int nameLen = d[pos];
                pos++;
                string pname = Encoding.Latin1.GetString(d, pos, nameLen);
                pos += nameLen;

                depthStack[^1].Props.Add(new(pname, ReadData(d, ref pos, path)));
            }
            else
            {
                throw new InvalidDataException($"Unexpected byte 0x{c:X2} at {pos} in {path}");
            }
        }

        return root;
    }

    private static PdxProp ReadData(byte[] d, ref int pos, string path)
    {
        char type = (char)d[pos];
        pos++;
        int count = BitConverter.ToInt32(d, pos);
        pos += 4;

        switch (type)
        {
            case 'i':
            {
                var v = new int[count];
                for (int i = 0; i < count; i++) v[i] = BitConverter.ToInt32(d, pos + i * 4);
                pos += 4 * count;
                return new PdxProp { Type = 'i', Ints = v };
            }

            case 'f':
            {
                var v = new float[count];
                for (int i = 0; i < count; i++) v[i] = BitConverter.ToSingle(d, pos + i * 4);
                pos += 4 * count;
                return new PdxProp { Type = 'f', Floats = v };
            }

            case 's':
            {
                // Declared length includes the terminator the writer appends.
                int len = BitConverter.ToInt32(d, pos);
                pos += 4;
                string s = Encoding.Latin1.GetString(d, pos, len).TrimEnd('\0');
                pos += len;
                return new PdxProp { Type = 's', Text = s };
            }

            default:
                throw new InvalidDataException($"Unknown data type '{type}' at {pos} in {path}");
        }
    }

    public static void Write(string path, PdxNode root)
    {
        var ms = new MemoryStream();
        ms.Write(Encoding.ASCII.GetBytes(Header));

        // Root properties come before any object, and the root itself is never named in the file.
        foreach (var kv in root.Props) WriteProp(ms, kv.Key, kv.Value);
        foreach (var child in root.Children) WriteNode(ms, child, 1);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, ms.ToArray());
    }

    private static void WriteNode(Stream s, PdxNode node, int depth)
    {
        for (int i = 0; i < depth; i++) s.WriteByte((byte)'[');

        byte[] name = Encoding.Latin1.GetBytes(node.Name);
        if (name.Length >= 64) throw new InvalidDataException($"Node name too long: {node.Name}");
        s.Write(name);
        s.WriteByte(0);

        foreach (var kv in node.Props) WriteProp(s, kv.Key, kv.Value);
        foreach (var child in node.Children) WriteNode(s, child, depth + 1);
    }

    private static void WriteProp(Stream s, string name, PdxProp p)
    {
        s.WriteByte((byte)'!');

        byte[] nb = Encoding.Latin1.GetBytes(name);
        if (nb.Length > 127) throw new InvalidDataException($"Property name too long: {name}");
        s.WriteByte((byte)nb.Length);
        s.Write(nb);

        s.WriteByte((byte)p.Type);

        switch (p.Type)
        {
            case 'i':
                s.Write(BitConverter.GetBytes(p.Ints.Length));
                foreach (int v in p.Ints) s.Write(BitConverter.GetBytes(v));
                break;

            case 'f':
                s.Write(BitConverter.GetBytes(p.Floats.Length));
                foreach (float v in p.Floats) s.Write(BitConverter.GetBytes(v));
                break;

            case 's':
            {
                s.Write(BitConverter.GetBytes(1));      // always a single string
                byte[] sb = Encoding.Latin1.GetBytes(p.Text);
                s.Write(BitConverter.GetBytes(sb.Length + 1));   // +1 for the terminator
                s.Write(sb);
                s.WriteByte(0);
                break;
            }

            default:
                throw new InvalidDataException($"Cannot write property type '{p.Type}'");
        }
    }
}
