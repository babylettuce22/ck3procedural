namespace Ck3MapGen.Io;

/// <summary>
/// Reads localisation back, which is the other half of <see cref="LocFile"/> and exists for the
/// GUI preview: a window whose every label reads <c>GEN_ARTIFACT_INDEX_TITLE</c> cannot be compared
/// against a screenshot of the real one, which is the whole point of previewing it.
///
/// Deliberately lenient. It is reading files to display them, not to validate them, so a malformed
/// line is skipped rather than reported — the alternative is a preview that refuses to draw because
/// some unrelated vanilla file has a stray quote in it.
///
/// Nested references are resolved one level deep and no further: values routinely contain
/// <c>$OTHER_KEY$</c>, and following those to a fixed point would mean implementing CK3's whole
/// promotion and scope system. One pass turns most labels into readable English, which is what the
/// preview needs, and the rest stay visible as themselves.
/// </summary>
public sealed class LocLibrary
{
    private readonly Dictionary<string, string> _entries = new(StringComparer.Ordinal);

    public int Count => _entries.Count;

    /// <summary>
    /// Loads <c>localization/english</c> under each root, in order, with later roots winning.
    ///
    /// Pass the game folder first and the mod second, so a key the mod redefines reads the way it
    /// will in game.
    /// </summary>
    public static LocLibrary Load(params string[] roots)
    {
        var library = new LocLibrary();

        foreach (string root in roots)
        {
            foreach (string folder in new[] { "localization", "localisation" })
            {
                string path = Path.Combine(root, folder, "english");
                if (!Directory.Exists(path)) continue;

                foreach (string file in Directory.GetFiles(path, "*.yml", SearchOption.AllDirectories))
                    library.Read(file);
            }
        }

        return library;
    }

    private void Read(string path)
    {
        string[] lines;

        try
        {
            lines = File.ReadAllLines(path);
        }
        catch (IOException)
        {
            return;
        }

        foreach (string line in lines)
        {
            string trimmed = line.TrimStart();

            if (trimmed.Length == 0 || trimmed[0] == '#') continue;

            // `KEY:0 "value"` — the version number is optional in practice, and the value is
            // whatever sits between the first and last quote on the line.
            int colon = trimmed.IndexOf(':');
            if (colon <= 0) continue;

            int open = trimmed.IndexOf('"', colon);
            int close = trimmed.LastIndexOf('"');
            if (open < 0 || close <= open) continue;

            _entries[trimmed[..colon]] = trimmed[(open + 1)..close];
        }
    }

    /// <summary>The text for a key, with one pass of <c>$KEY$</c> substitution. Null if unknown.</summary>
    public string? Text(string key)
    {
        if (!_entries.TryGetValue(key, out string? value)) return null;

        if (!value.Contains('$')) return value;

        var parts = value.Split('$');

        // Odd indices are the references: "a $B$ c" splits to [a , B, c].
        for (int i = 1; i < parts.Length; i += 2)
            if (_entries.TryGetValue(parts[i], out string? nested)) parts[i] = nested;

        return string.Concat(parts);
    }
}
