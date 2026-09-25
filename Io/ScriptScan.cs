namespace Ck3MapGen.Io;

/// <summary>
/// Brace matching over Paradox script text, for the writers that splice into a vanilla file or
/// lift a block out of one and so need positions in the text rather than a parsed tree. (Readers
/// that only need values use <see cref="GameGUI.GuiParser"/>, which is lossless on vanilla.)
///
/// What every one of these does that the hand-rolled loops it replaced did not: a <c>#</c> comment
/// and a quoted string are skipped whole. Vanilla's prose comments carry braces — <c># { a b }</c>
/// is a normal way to note a list — and one stray brace in one throws the depth count off for the
/// rest of the file, silently: the block "found" is a different block, and nothing downstream can
/// tell. A <c>#</c> inside a string (<c>"#bold text#!"</c>) is text, not a comment.
///
/// Strings do not span lines in script, so the per-line helpers start each line outside one.
/// </summary>
public static class ScriptScan
{
    /// <summary>
    /// The index one past the <c>}</c> that closes the first <c>{</c> at or after
    /// <paramref name="from"/>, or -1 when there is no such brace or it never closes.
    /// </summary>
    public static int BlockEnd(string text, int from)
    {
        int depth = 0;

        for (int i = from; i < text.Length; i++)
        {
            char c = text[i];

            if (c == '#') { i = LineEnd(text, i) - 1; continue; }
            if (c == '"') { i = StringEnd(text, i); continue; }

            if (c == '{') depth++;
            else if (c == '}' && depth > 0 && --depth == 0) return i + 1;
        }

        return -1;
    }

    /// <summary>The brace depth at <paramref name="index"/>, counting from the start of
    /// <paramref name="text"/>.</summary>
    public static int DepthAt(string text, int index)
    {
        int depth = 0;

        for (int i = 0; i < index && i < text.Length; i++)
        {
            char c = text[i];

            if (c == '#') { i = LineEnd(text, i) - 1; continue; }
            if (c == '"') { i = StringEnd(text, i); continue; }

            if (c == '{') depth++;
            else if (c == '}') depth--;
        }

        return depth;
    }

    /// <summary><paramref name="line"/> without its comment. A <c>#</c> inside a string stays.</summary>
    public static string StripComment(string line)
    {
        int hash = CommentStart(line);
        return hash < 0 ? line : line[..hash];
    }

    /// <summary>Opening minus closing braces on one line, outside its comment and strings.</summary>
    public static int BraceDelta(string line)
    {
        int delta = 0;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == '#') break;
            if (c == '"') { i = StringEnd(line, i); continue; }

            if (c == '{') delta++;
            else if (c == '}') delta--;
        }

        return delta;
    }

    /// <summary>
    /// Every <c>key = {</c> declared at column 0, as the line range it spans. Column 0 is what
    /// tells a declaration from the nested blocks that share its shape: vanilla opens every
    /// top-level object there with the brace on the same line, and indents everything else.
    ///
    /// <paramref name="isKeyChar"/> decides what a key may contain; region keys, for one, carry
    /// ampersands. A declaration that never closes is still returned, running to the last line,
    /// with <c>Closed</c> false, so each caller keeps its own policy for a truncated file.
    /// </summary>
    public static IEnumerable<(string Key, int First, int Last, bool Closed)> TopLevelDeclarations(
        string[] lines, Func<char, bool> isKeyChar)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (line.Length == 0 || char.IsWhiteSpace(line[0]) || line[0] is '#' or '@') continue;

            int equals = line.IndexOf('=');
            if (equals <= 0 || !line.Contains('{')) continue;

            string key = line[..equals].Trim().TrimStart('﻿');
            if (key.Length == 0 || !key.All(isKeyChar)) continue;

            int depth = 0, last = -1;
            for (int j = i; j < lines.Length; j++)
            {
                depth += BraceDelta(lines[j]);
                if (depth > 0) continue;
                last = j;
                break;
            }

            if (last < 0)
            {
                yield return (key, i, lines.Length - 1, false);
                yield break;
            }

            yield return (key, i, last, true);
            i = last;
        }
    }

    /// <summary>Index of the comment's <c>#</c> on <paramref name="line"/>, or -1.</summary>
    private static int CommentStart(string line)
    {
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '#') return i;
            if (line[i] == '"') i = StringEnd(line, i);
        }

        return -1;
    }

    /// <summary>Index of the quote closing the string opened at <paramref name="open"/>; the end of
    /// the line if it is unterminated there, so a stray quote cannot swallow the rest of a file.
    /// For a scanner of its own that needs to step over strings the way these do.</summary>
    public static int StringEnd(string text, int open)
    {
        for (int i = open + 1; i < text.Length; i++)
        {
            if (text[i] == '\\') { i++; continue; }
            if (text[i] == '"') return i;
            if (text[i] == '\n') return i - 1;
        }

        return text.Length - 1;
    }

    private static int LineEnd(string text, int from)
    {
        int end = text.IndexOf('\n', from);
        return end < 0 ? text.Length : end;
    }
}
