using System.Text;
using Ck3MapGen.GameGui;

namespace Ck3MapGen.Io;

/// <summary>
/// An existing file, edited by source ranges rather than re-emitted by a generator. The original
/// UTF-8 BOM, whitespace, comments and unknown fields survive. Ranges always address the original
/// text; saving advances the disk baseline without invalidating the editor's bindings.
/// </summary>
public sealed class EditableWorldFile
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly Dictionary<(int Start, int Length), string> _edits = [];
    private Dictionary<(int Start, int Length), string> _savedEdits = [];
    private readonly Dictionary<GuiNode, (int Start, int End, int Body)> _ranges = [];
    public string Path { get; }
    public string Original { get; }
    public byte[] SavedBytes { get; private set; }
    public GuiDocument? Script { get; }

    public EditableWorldFile(string path, bool script)
    {
        Path = System.IO.Path.GetFullPath(path);
        SavedBytes = File.ReadAllBytes(Path);
        Original = Utf8.GetString(SavedBytes);
        if (!script) return;
        Script = GuiParser.Parse(Original, Path);
        if (Script.Print() != Original)
            throw new FormatException($"Cannot open losslessly: {Path}");
        int offset = 0;
        foreach (var root in Script.Roots) Index(root, ref offset);
    }

    private void Index(GuiNode node, ref int offset)
    {
        offset += node.LeadingTrivia?.Length ?? 0;
        int start = offset;
        offset += node.SourceHead.Length;
        int body = offset;
        if (node.IsBlock)
        {
            body = ++offset;
            foreach (var child in node.Children) Index(child, ref offset);
            offset += (node.TrailingTrivia?.Length ?? 0) + 1;
        }
        _ranges.Add(node, (start, offset, body));
    }

    public (int Start, int Length) ValueRange(GuiNode node)
    {
        var span = _ranges[node];
        if (node.IsBlock) return (span.Body, span.End - 1 - span.Body);
        if (node.Value is null) throw new ArgumentException("This node has no value.");
        return (span.End - node.Value.Length, node.Value.Length);
    }

    public string Read((int Start, int Length) range)
        => _edits.GetValueOrDefault(range, Original.Substring(range.Start, range.Length));

    public void Set((int Start, int Length) range, string value)
    {
        if (value == Original.Substring(range.Start, range.Length)) _edits.Remove(range);
        else
        {
            if (_edits.Keys.Any(r => r != range && r.Start < range.Start + range.Length
                    && range.Start < r.Start + r.Length))
                throw new InvalidOperationException("These edits overlap. Reopen the world before continuing.");
            _edits[range] = value;
        }
    }

    public string Render()
    {
        var text = new StringBuilder(Original);
        foreach (var (range, value) in _edits.OrderByDescending(e => e.Key.Start))
            text.Remove(range.Start, range.Length).Insert(range.Start, value);
        return text.ToString();
    }

    public byte[] Bytes() => Utf8.GetBytes(Render());
    // Compared by edit table rather than by re-rendering the file: this is asked after every
    // property change for every loaded file, and the character history alone is megabytes.
    public bool Changed => _edits.Count != _savedEdits.Count
        || _edits.Any(e => !_savedEdits.TryGetValue(e.Key, out string? saved) || saved != e.Value);
    public IEnumerable<(string Before, string After)> Differences()
    {
        foreach (var range in _edits.Keys.Union(_savedEdits.Keys).OrderBy(r => r.Start))
        {
            string original = Original.Substring(range.Start, range.Length);
            string before = _savedEdits.GetValueOrDefault(range, original);
            string after = _edits.GetValueOrDefault(range, original);
            if (before != after) yield return (before, after);
        }
    }
    public void Revert()
    {
        _edits.Clear();
        foreach (var edit in _savedEdits) _edits.Add(edit.Key, edit.Value);
    }
    public void RevertRange((int Start, int Length) range)
    {
        if (_savedEdits.TryGetValue(range, out string? value)) _edits[range] = value;
        else _edits.Remove(range);
    }
    public void Accept(byte[] bytes)
    {
        SavedBytes = bytes;
        _savedEdits = new(_edits);
    }
}
