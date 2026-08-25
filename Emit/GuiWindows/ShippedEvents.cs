namespace Ck3MapGen.Emit;

/// <summary>
/// Every event this mod ships, read back off the finished mod folder.
///
/// Read back rather than reported. The events reach the mod by two completely different routes —
/// some are generated (<see cref="WonderWriter"/>, <see cref="StruggleWriter"/>,
/// <see cref="ChronicleWriter"/>), the rest are copied wholesale out of
/// <c>BaseFilesToCopy/*/events</c> by <see cref="StaticFileWriter"/> — and asking each of those to
/// hand back a list would mean every future writer remembering to. Scanning <c>events/</c> once
/// after everything has been written asks nothing of anybody: a file dropped into BaseFilesToCopy
/// tomorrow appears in the debug panel with no code change at all.
///
/// The cost is an ordering constraint. <see cref="DebugPanel"/> must be written AFTER
/// <see cref="StaticFileWriter"/>, or it scans a folder holding only the generated half. That is
/// the one thing about this file that can silently go wrong, which is why the call site says so.
/// </summary>
public static class ShippedEvents
{
    /// <summary>
    /// One event: enough to fire it, and enough to say what it is without opening the file.
    /// </summary>
    /// <param name="Id">The full <c>namespace.number</c> id, as <c>trigger_event</c> takes it.</param>
    /// <param name="Type">The declared <c>type</c>, defaulting to <c>character_event</c>. This is
    /// about how the event PRESENTS; <paramref name="Scope"/> is what it runs in.</param>
    /// <param name="Scope">The declared <c>scope</c>, defaulting to <c>character</c>. The field that
    /// actually decides whether a fire button is possible — and the one the first draft of this
    /// scanner did not read, which is how a struggle event ended up wired to a character button.</param>
    /// <param name="Hidden">A <c>hidden = yes</c> event does its work with no window. Worth knowing
    /// before you press the button and conclude it is broken.</param>
    /// <param name="File">The file it came from, which is how the panel groups them.</param>
    public sealed record Entry(string Id, string Type, string Scope, bool Hidden, string File)
    {
        /// <summary>The id as an identifier: <c>a.0001</c> cannot be part of a script key.</summary>
        public string Key => Id.Replace('.', '_');

        /// <summary>
        /// Whether a button on this panel can legitimately fire it.
        ///
        /// Only the two scopes the panel can actually produce from a player: the player, and the
        /// player's capital. An event scoped to a struggle, an activity or a travel plan has no
        /// such route — <c>gen_struggle.1</c> is scoped to a struggle — and wiring one to a
        /// character button emits script that is simply wrong. ck3-tiger says so
        /// ("is for struggle but scope seems to be character"), which is how this was caught, but
        /// the generated file should not have needed telling.
        ///
        /// Such events are still LISTED. Knowing the event ships, and why it cannot be fired from
        /// here, is most of the value; a button that lies about what it does is worth less than no
        /// button.
        /// </summary>
        public bool CanFire => Scope is "character" or "province";

        public bool IsProvinceEvent => Scope == "province";
    }

    /// <summary>
    /// Scans <paramref name="modDir"/>'s <c>events/</c> tree, including subfolders.
    ///
    /// Returns them grouped by file and sorted within it, because the panel draws them in this
    /// order and the engine's directory order is not worth reasoning about.
    /// </summary>
    public static List<Entry> Scan(string modDir)
    {
        string root = Path.Combine(modDir, "events");
        if (!Directory.Exists(root)) return [];

        var found = new List<Entry>();

        foreach (string path in Directory.GetFiles(root, "*.txt", SearchOption.AllDirectories)
                     .OrderBy(p => p, StringComparer.Ordinal))
            found.AddRange(ReadFile(path, Path.GetFileName(path)));

        return found;
    }

    /// <summary>
    /// The events in one file.
    ///
    /// A depth-tracking scan rather than a line regex, and the difference is not academic. The
    /// events this project ships contain 17 lines reading <c>type = event_toast_effect_bad</c>,
    /// <c>type = favor_hook</c> and the like — all of them nested inside an option's effect, none
    /// of them the event's own type. A regex for <c>^\s*type = </c> reads every one of them as the
    /// event's type and gets the answer wrong on a third of the file.
    /// </summary>
    private static IEnumerable<Entry> ReadFile(string path, string file)
    {
        string text = File.ReadAllText(path);

        // Fields of the event currently open, which is only ever one at a time: events do not nest.
        string? id = null;
        string type = "character_event";
        string scope = "character";
        bool hidden = false;

        int depth = 0;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            // Comments and strings are skipped whole. A `{` inside either would throw the depth
            // count off for the rest of the file, and every event file here carries prose comments
            // with braces in them.
            if (c == '#')
            {
                while (i < text.Length && text[i] != '\n') i++;
                continue;
            }

            if (c == '"')
            {
                i++;
                while (i < text.Length && text[i] != '"') i++;
                continue;
            }

            if (c == '{') { depth++; continue; }

            if (c == '}')
            {
                depth--;

                // Closing the event itself. Emit what was gathered and reset.
                if (depth == 0 && id is not null)
                {
                    yield return new Entry(id, type, scope, hidden, file);
                    id = null;
                    type = "character_event";
                    scope = "character";
                    hidden = false;
                }

                continue;
            }

            if (!IsWordStart(c)) continue;

            int start = i;
            while (i < text.Length && IsWord(text[i])) i++;
            string word = text[start..i];

            // Rewound because the loop's own i++ would swallow whatever followed the word — a `{`
            // most of the time, which is the character the depth count exists for.
            i--;

            // An event header at file level. Only ids of the `namespace.number` shape count, which
            // is also what rules out the `namespace = x` line at the top of every file.
            if (depth == 0)
            {
                if (IsEventId(word) && FollowedByBlock(text, i + 1)) id = word;
                continue;
            }

            // The event's own fields, and only its own: depth 1 is inside the event, depth 2 is
            // inside one of its options or effects.
            if (depth != 1 || id is null) continue;

            if (word == "type") type = ValueAfter(text, i + 1) ?? type;
            else if (word == "scope") scope = ValueAfter(text, i + 1) ?? scope;
            else if (word == "hidden") hidden = ValueAfter(text, i + 1) == "yes";
        }
    }

    /// <summary>An id like <c>wilderness_colonization.0001</c>: words either side of one dot.</summary>
    private static bool IsEventId(string word)
    {
        int dot = word.IndexOf('.');
        return dot > 0
            && dot < word.Length - 1
            && word.IndexOf('.', dot + 1) < 0
            && word[(dot + 1)..].All(char.IsAsciiDigit);
    }

    /// <summary>Whether what follows is <c>= {</c>, which is what makes a word a header.</summary>
    private static bool FollowedByBlock(string text, int from)
    {
        int i = SkipSpace(text, from);
        if (i >= text.Length || text[i] != '=') return false;

        i = SkipSpace(text, i + 1);
        return i < text.Length && text[i] == '{';
    }

    /// <summary>The bare token after <c>=</c>, or null if what follows is a block.</summary>
    private static string? ValueAfter(string text, int from)
    {
        int i = SkipSpace(text, from);
        if (i >= text.Length || text[i] != '=') return null;

        i = SkipSpace(text, i + 1);
        if (i >= text.Length || !IsWordStart(text[i])) return null;

        int start = i;
        while (i < text.Length && IsWord(text[i])) i++;
        return text[start..i];
    }

    private static int SkipSpace(string text, int i)
    {
        while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
        return i;
    }

    private static bool IsWordStart(char c) => char.IsAsciiLetter(c) || c == '_';

    private static bool IsWord(char c) => char.IsAsciiLetterOrDigit(c) || c == '_' || c == '.';
}
