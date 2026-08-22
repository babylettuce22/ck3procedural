namespace Ck3MapGen.Core;

/// <summary>
/// One entry in the launcher's mod folder, as its <c>.mod</c> file describes it.
///
/// The fields are the descriptor's own, kept rather than flattened, because the interesting
/// questions about a mod that is about to load beside a generated map are all answered by
/// <see cref="ReplacePaths"/> and <see cref="Tags"/> — a mod that replaces <c>history/provinces</c>
/// is a different proposition from one that adds a GUI window, and the descriptor says which it is
/// without opening a single file inside the mod.
/// </summary>
public sealed record ModEntry
{
    /// <summary>The <c>.mod</c> file itself, in the launcher's mod folder.</summary>
    public required string Descriptor { get; init; }

    /// <summary>What <c>dlc_load.json</c> calls this mod: <c>mod/&lt;file&gt;.mod</c>.</summary>
    public required string Entry { get; init; }

    /// <summary>The launcher's display name, or the file name when the descriptor omits one.</summary>
    public required string Name { get; init; }

    public string? Version { get; init; }
    public string? SupportedVersion { get; init; }

    /// <summary>Where the mod's content actually lives — under Steam's workshop folder, or ours.</summary>
    public string? ContentPath { get; init; }

    /// <summary>The workshop item id, present only on a subscription.</summary>
    public string? RemoteFileId { get; init; }

    public IReadOnlyList<string> Tags { get; init; } = [];
    public IReadOnlyList<string> ReplacePaths { get; init; } = [];
    public IReadOnlyList<string> Dependencies { get; init; } = [];

    /// <summary>Whether this came from the workshop rather than being a local folder.</summary>
    public bool IsWorkshop => RemoteFileId is not null;

    /// <summary>
    /// Whether the descriptor points at content that is no longer there — an unsubscribed mod whose
    /// stub the launcher has not cleaned up, or a local mod folder that has been deleted. Enabling
    /// one of these is how you get a game that fails to start rather than a game without the mod.
    /// </summary>
    public bool ContentMissing =>
        ContentPath is not null && !Directory.Exists(ContentPath) && !File.Exists(ContentPath);
}

/// <summary>
/// Every mod this machine could load, found by reading the launcher's mod folder.
///
/// There is no Steam API call here and no launcher database read, because neither is needed: the
/// launcher writes a <c>ugc_&lt;id&gt;.mod</c> stub into the same folder for every workshop
/// subscription, in the same format <see cref="Emit.ModWriter"/> emits, pointing at the downloaded
/// content under <c>steamapps/workshop</c>. Workshop mods and hand-installed ones are therefore the
/// same thing to look at, and the folder <see cref="DlcLoad"/> already writes next to is the folder
/// that has them all.
///
/// The one case the folder misses is a subscription Steam has downloaded but the launcher has not
/// yet been opened to register — content on disk, no stub. <see cref="Unregistered"/> finds those so
/// they can be reported, but they cannot be enabled: the item's inner <c>Descriptor.mod</c> carries
/// no <c>path=</c> line, so making one loadable would mean writing a stub into the user's mod folder
/// on their behalf, and opening the launcher once does the same job correctly.
/// </summary>
public static class ModLibrary
{
    /// <summary>
    /// The mods registered in <paramref name="modRoot"/>, by name. Never throws — an unreadable
    /// folder or a malformed descriptor is one fewer mod in the list, not a failed dialog.
    /// </summary>
    public static IReadOnlyList<ModEntry> InFolder(string modRoot)
    {
        string[] files;
        try
        {
            if (!Directory.Exists(modRoot)) return [];
            files = Directory.GetFiles(modRoot, "*.mod", SearchOption.TopDirectoryOnly);
        }
        catch (Exception)
        {
            return [];
        }

        return files
            .Select(Read)
            .OfType<ModEntry>()
            .OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Workshop items downloaded to <paramref name="workshopRoot"/> that nothing in
    /// <paramref name="registered"/> accounts for. The entries come from the item's inner
    /// <c>Descriptor.mod</c> and carry an empty <see cref="ModEntry.Entry"/> — they exist to be
    /// listed, not enabled.
    /// </summary>
    public static IReadOnlyList<ModEntry> Unregistered(
        string? workshopRoot, IReadOnlyList<ModEntry> registered)
    {
        if (workshopRoot is null) return [];

        var known = registered
            .Select(m => m.RemoteFileId)
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        string[] items;
        try
        {
            if (!Directory.Exists(workshopRoot)) return [];
            items = Directory.GetDirectories(workshopRoot);
        }
        catch (Exception)
        {
            return [];
        }

        var found = new List<ModEntry>();

        foreach (string item in items)
        {
            string id = Path.GetFileName(item);
            if (known.Contains(id)) continue;

            // Cased both ways in the wild: the workshop copy is Descriptor.mod and a local mod's is
            // usually descriptor.mod. Windows does not care, but the file we opened does when it
            // ends up in a log.
            string? descriptor = new[] { "descriptor.mod", "Descriptor.mod" }
                .Select(n => Path.Combine(item, n))
                .FirstOrDefault(File.Exists);

            if (descriptor is null || Read(descriptor) is not { } mod) continue;

            found.Add(mod with
            {
                Entry = "",
                ContentPath = item,
                RemoteFileId = mod.RemoteFileId ?? id,
            });
        }

        return found.OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>One descriptor, or null if it cannot be read at all.</summary>
    public static ModEntry? Read(string file)
    {
        string text;
        try
        {
            text = File.ReadAllText(file);
        }
        catch (Exception)
        {
            return null;
        }

        var fields = Fields(text);

        string? One(string key) => fields.LastOrDefault(f => f.Key == key)?.Values.FirstOrDefault();
        List<string> All(string key) =>
            fields.Where(f => f.Key == key).SelectMany(f => f.Values).ToList();

        return new ModEntry
        {
            Descriptor = file,
            Entry = $"mod/{Path.GetFileName(file)}",
            Name = One("name") ?? Path.GetFileNameWithoutExtension(file),
            Version = One("version"),
            SupportedVersion = One("supported_version"),
            ContentPath = One("path") ?? One("archive"),
            RemoteFileId = One("remote_file_id"),
            Tags = All("tags"),
            ReplacePaths = All("replace_path"),
            Dependencies = All("dependencies"),
        };
    }

    private sealed record Field(string Key, List<string> Values);

    /// <summary>
    /// A descriptor as a flat list of key/values, in file order.
    ///
    /// Hand-scanned rather than parsed properly, for the same reason <see cref="GameLocator"/> reads
    /// <c>libraryfolders.vdf</c> with a regex: a descriptor is the shallowest possible Clausewitz
    /// file — quoted scalars and one level of quoted-string blocks — and a real parser would be more
    /// code than everything that uses it. Repeats are kept rather than collapsed, because
    /// <c>replace_path</c> is always a repeat and is the field that decides whether a mod can load
    /// beside a generated map.
    ///
    /// Unquoted values are not supported and read as the start of the next key, which drops them.
    /// Nothing writes descriptors that way, and a dropped field costs a column in a list.
    /// </summary>
    private static List<Field> Fields(string text)
    {
        var fields = new List<Field>();
        string? key = null;
        int i = 0;

        while (i < text.Length)
        {
            char c = text[i];

            if (c == '#')
            {
                while (i < text.Length && text[i] != '\n') i++;
            }
            else if (char.IsWhiteSpace(c) || c == '=' || c == '﻿')
            {
                // The BOM among the separators rather than stripped up front: ours writes one, the
                // launcher's stubs do not, and at this point it is just another thing to skip.
                i++;
            }
            else if (c == '"')
            {
                string value = Quoted(text, ref i);
                if (key is not null) fields.Add(new Field(key, [value]));
                key = null;
            }
            else if (c == '{')
            {
                i++;
                var values = new List<string>();

                while (i < text.Length && text[i] != '}')
                {
                    if (text[i] == '"') values.Add(Quoted(text, ref i));
                    else if (text[i] == '#') { while (i < text.Length && text[i] != '\n') i++; }
                    else i++;
                }

                if (i < text.Length) i++;
                if (key is not null) fields.Add(new Field(key, values));
                key = null;
            }
            else if (c == '}')
            {
                i++;
            }
            else
            {
                int start = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i])
                       && text[i] is not ('=' or '{' or '}' or '"' or '#')) i++;
                key = text[start..i];
            }
        }

        return fields;
    }

    /// <summary>The string starting at <paramref name="i"/>, leaving it past the closing quote.</summary>
    private static string Quoted(string text, ref int i)
    {
        int end = text.IndexOf('"', i + 1);
        if (end < 0)
        {
            string rest = text[(i + 1)..];
            i = text.Length;
            return rest;
        }

        string value = text[(i + 1)..end];
        i = end + 1;
        return value;
    }
}
