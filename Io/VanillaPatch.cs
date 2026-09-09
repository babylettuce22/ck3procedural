namespace Ck3MapGen.Io;

/// <summary>
/// A vanilla file, copied into the mod with edits, that refuses to ship half-patched.
///
/// Several writers here override a vanilla script or gui file by copying the user's own copy of it
/// and splicing something in. The splices themselves have nothing in common — one wants a literal
/// offset, another an indent-aware widget, another a brace-depth block scan — so this owns none of
/// that. What it owns is the policy every one of them needs and only some of them had:
///
/// * a missing source file is a skip, with one wording rather than four;
/// * an anchor that no longer resolves is a *named* failure, not a silently absent insert;
/// * if any anchor missed, nothing ships.
///
/// That last rule is the reason this type exists. A full-file override missing the one thing it was
/// written to add is worse than no override at all: it replaces vanilla with vanilla, the guard is
/// gone, and neither CK3 nor ck3-tiger says a word — the file is perfectly valid, it just no longer
/// does anything. <c>CasusBelliWriter</c> shipped exactly that shape (each insert guarded by
/// <c>if (index != -1)</c> and the file written out regardless), and what it guards is the rule
/// that keeps every neighbour from carving up the wilderness.
///
/// Anchors are sequences of literal probes matched in order rather than one regex, because that is
/// what makes an anchor scopeable: "inside <c>declare_war_interaction = {</c>, the next
/// <c>is_shown = {</c>" is a claim about the file that survives Paradox adding an interaction above
/// it, and <c>IndexOf("is_shown = {")</c> is not.
///
/// <see cref="ReplaceEvery"/> is the "at least one" rule, added for the hegemony's China-flavour
/// strip. It is still not general enough for <c>FrontendWriter</c>, which comments out whole
/// portrait blocks and so needs brace matching rather than token rewriting.
/// </summary>
public sealed class VanillaPatch
{
    private readonly string label;
    private readonly string relativePath;
    private readonly List<string> landed = [];
    private readonly List<string> missed = [];

    private string text;

    private VanillaPatch(string label, string relativePath, string text)
    {
        this.label = label;
        this.relativePath = relativePath;
        this.text = text;
    }

    /// <summary>
    /// Reads the vanilla file, or returns null having said why.
    ///
    /// The path parts are used twice — to find the source under the game folder, and to place the
    /// override at the same path under the mod — because a CK3 override only works from the
    /// matching path, and passing it once removes the chance of the two drifting apart.
    /// </summary>
    public static VanillaPatch? Open(string gameDir, string label, params string[] relativePathParts)
    {
        string relativePath = Path.Combine(relativePathParts);
        string source = Path.Combine(gameDir, relativePath);

        if (!File.Exists(source))
        {
            Console.WriteLine($"  {label}: SKIPPED ({relativePath} not found in game folder)");
            return null;
        }

        return new VanillaPatch(label, relativePath, File.ReadAllText(source));
    }

    /// <summary>
    /// Splices <paramref name="body"/> in immediately after the last of <paramref name="probes"/>,
    /// each found in turn from where the one before it ended.
    ///
    /// <paramref name="name"/> is what an operator reads when this fails, so it names the place
    /// rather than the edit — "declare_war is_shown", not "wilderness guard".
    ///
    /// Probes run against the text as it stands, so an earlier insert can move a later anchor.
    /// That is intended, and it is why the calls are ordered.
    /// </summary>
    public void InsertAfter(string name, string body, params string[] probes)
    {
        int at = 0;

        foreach (string probe in probes)
        {
            int found = text.IndexOf(probe, at, StringComparison.Ordinal);
            if (found < 0)
            {
                missed.Add(name);
                return;
            }

            at = found + probe.Length;
        }

        text = text.Insert(at, body);
        landed.Add(name);
    }

    /// <summary>
    /// Rewrites every occurrence of <paramref name="find"/>, and misses if there were none.
    ///
    /// The counterpart to <see cref="InsertAfter"/> for the other shape of override: not "add a
    /// guard at one named place" but "neutralise a test wherever vanilla makes it". A file that
    /// makes the same check in eight places is one edit conceptually, and writing eight anchors
    /// for it would be eight chances to describe the file wrongly.
    ///
    /// "At least one" rather than an exact count on purpose. The count is what Paradox changes
    /// most often — an entry added, an entry retired — and pinning it would turn every such patch
    /// into a shipped-nothing skip. Zero is still a failure, and it is the failure that matters:
    /// zero means the token itself is gone, so the file no longer says what this edit was written
    /// against and a copy of it would be vanilla wearing our name.
    ///
    /// Order matters when one search string contains another. <c>primary_title = title:x</c> is a
    /// substring of <c>primary_spouse.primary_title = title:x</c>, and replacing the short form
    /// first leaves <c>primary_spouse.always = no</c> behind — valid script that scopes into
    /// nothing. Call the longest forms first.
    /// </summary>
    public void ReplaceEvery(string name, string find, string replacement)
    {
        if (!text.Contains(find, StringComparison.Ordinal))
        {
            missed.Add(name);
            return;
        }

        int count = 0;
        for (int at = 0; (at = text.IndexOf(find, at, StringComparison.Ordinal)) >= 0; at += replacement.Length)
        {
            text = text.Remove(at, find.Length).Insert(at, replacement);
            count++;
        }

        landed.Add($"{name} x{count}");
    }

    /// <summary>
    /// Writes the override, or explains why it is not writing one. Returns whether it shipped.
    /// </summary>
    public bool Ship(string modDir)
    {
        if (missed.Count > 0)
        {
            Console.WriteLine($"  {label}: SKIPPED {relativePath} — no anchor for "
                + $"{string.Join(", ", missed)}. Vanilla has changed shape; "
                + "not shipping a partial override.");
            return false;
        }

        string destination = Path.Combine(modDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        ParadoxText.WriteBom(destination, text);

        Console.WriteLine($"  {label}: {relativePath} — patched {string.Join(", ", landed)}");
        return true;
    }
}
