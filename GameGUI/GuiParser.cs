namespace Ck3MapGen.GameGui;

/// <summary>
/// Reads a <c>.gui</c> file into <see cref="GuiNode"/>s without losing a byte of it.
///
/// Recursive descent over a token stream, with the source between tokens kept as trivia rather than
/// discarded — every comment, blank line and tab a CK3 author wrote is attached to the node that
/// follows it, so reprinting an unmodified tree gives back the original file exactly. Verified
/// against all 373 vanilla <c>.gui</c> files: 190,731 nodes, every file byte-identical on
/// round-trip.
///
/// The grammar is small enough to state in full. An item is a run of head tokens ending in either a
/// <c>{</c>, which makes it a block, or a complete <c>key = value</c>, which makes it a leaf; a run
/// that hits <c>}</c> or end-of-file first is a bare token run, which is how the contents of
/// <c>size = { 100 100 }</c> come through. That single rule covers <c>widget = { }</c>,
/// <c>blockoverride "icon" { }</c>, <c>type x = window { }</c> and <c>types Group { }</c> alike,
/// which is why none of them appears in the code below.
///
/// Deliberately not a validator. It does not know what a widget is, which properties exist, or
/// whether a <c>using</c> resolves — CK3 and ck3-tiger both answer that better. A file it cannot
/// parse throws, and the callers treat that the way they treat a missing file: skip, and say so.
/// </summary>
public static class GuiParser
{
    public static GuiDocument Parse(string text, string label = "gui")
        => new(new Cursor(text, label).ParseFile(out string epilogue), epilogue);

    private enum Kind { Word, Str, Equals, Open, Close, Eof }

    private readonly record struct Token(Kind Kind, int Start, int End, string Text);

    private sealed class Cursor(string src, string label)
    {
        private readonly List<Token> _tokens = Lex(src);
        private int _next;

        /// <summary>How far into the source everything emitted so far reaches.</summary>
        private int _consumed;

        public List<GuiNode> ParseFile(out string epilogue)
        {
            var roots = ParseItems(depth: 0);

            if (Peek().Kind != Kind.Eof)
                throw new FormatException($"{label}: unmatched '}}' at offset {Peek().Start}");

            // Whatever follows the last item — a trailing newline, a parting comment. Held by the
            // document rather than by a node, because it belongs to no node.
            epilogue = src[_consumed..];
            return roots;
        }

        private List<GuiNode> ParseItems(int depth)
        {
            var items = new List<GuiNode>();

            while (true)
            {
                var token = Peek();
                if (token.Kind is Kind.Eof or Kind.Close) return items;

                items.Add(ParseItem(depth));
            }
        }

        private GuiNode ParseItem(int depth)
        {
            if (depth > 200) throw new FormatException($"{label}: nesting past 200 levels");

            var first = Peek();
            string leading = Take(first.Start);
            var head = new List<string>();

            while (true)
            {
                var token = Peek();

                if (token.Kind == Kind.Open) return ParseBlock(head, first, token, leading, depth);

                // A run that ends at '}' or end-of-file is complete as it stands: the token pairs
                // inside an inline block, or one of the five bare words vanilla leaves lying about.
                if (token.Kind is Kind.Eof or Kind.Close)
                {
                    if (head.Count == 0)
                        throw new FormatException($"{label}: stray '}}' at offset {token.Start}");

                    return LeafFrom(head, value: null, first, leading);
                }

                head.Add(Next().Text);

                // `key = value`, unless a '{' is coming — `blockoverride = "x" {` reaches three
                // tokens too, and vanilla writes 26 of those.
                if (head.Count == 3 && head[1] == "=" && Peek().Kind != Kind.Open)
                {
                    string value = head[2];
                    head.RemoveAt(2);
                    return LeafFrom(head, value, first, leading);
                }
            }
        }

        private GuiNode ParseBlock(List<string> head, Token first, Token open, string leading, int depth)
        {
            // The head's source text: everything from the item's first token up to the brace,
            // exclusive. Kept whole so the block can reprint its own punctuation untouched when a
            // patch reaches into its children.
            string source = src[first.Start..open.Start];

            Next();
            _consumed = open.End;

            var children = ParseItems(depth + 1);

            var close = Peek();
            if (close.Kind != Kind.Close)
                throw new FormatException(
                    $"{label}: block opened at offset {open.Start} is never closed");

            string trailing = Take(close.Start);
            Next();
            _consumed = close.End;

            int lineEnd = src.IndexOf('\n', open.End);
            if (lineEnd < 0) lineEnd = src.Length;

            var node = new GuiNode(head, isBlock: true, value: null, source, leading)
            {
                TrailingTrivia = trailing,
                Inline = !src.AsSpan(open.Start, close.End - open.Start).Contains('\n'),

                // Only ever read back by an anchor, never reprinted: the text it holds is also the
                // first child's leading trivia, and printing it here would duplicate it.
                HeadTail = src[open.End..Math.Min(lineEnd, close.Start)],
            };

            foreach (var child in children) node.Adopt(child);
            return node;
        }

        private GuiNode LeafFrom(List<string> head, string? value, Token first, string leading)
        {
            int end = _tokens[_next - 1].End;
            var node = new GuiNode(head, isBlock: false, value, src[first.Start..end], leading);
            _consumed = end;
            return node;
        }

        private Token Peek() => _tokens[_next];

        private Token Next() => _tokens[_next++];

        /// <summary>The source between what has been emitted and <paramref name="upto"/>.</summary>
        private string Take(int upto)
        {
            string trivia = src[_consumed..upto];
            _consumed = upto;
            return trivia;
        }
    }

    /// <summary>
    /// Tokens, with trivia skipped but positions kept so the parser can slice it back out.
    ///
    /// Quoted strings and <c>#</c> comments are consumed whole, which is the only reason a brace or
    /// a <c>#</c> inside a datafunction — <c>text = "#high [Foo.Bar]"</c> — does not derail the
    /// scan. The hand-written <c>MatchBrace</c> this replaces had to know the same two rules.
    /// </summary>
    private static List<Token> Lex(string src)
    {
        var tokens = new List<Token>();
        int i = 0;

        while (i < src.Length)
        {
            char c = src[i];

            if (c is ' ' or '\t' or '\r' or '\n' or '﻿') { i++; continue; }

            if (c == '#')
            {
                while (i < src.Length && src[i] != '\n') i++;
                continue;
            }

            if (c is '{' or '}')
            {
                tokens.Add(new Token(c == '{' ? Kind.Open : Kind.Close, i, i + 1, src[i].ToString()));
                i++;
                continue;
            }

            if (c == '=')
            {
                tokens.Add(new Token(Kind.Equals, i, i + 1, "="));
                i++;
                continue;
            }

            if (c == '"')
            {
                int j = i + 1;
                while (j < src.Length && src[j] != '"')
                {
                    if (src[j] == '\\') j++;
                    j++;
                }

                j = Math.Min(j + 1, src.Length);
                tokens.Add(new Token(Kind.Str, i, j, src[i..j]));
                i = j;
                continue;
            }

            int word = i;
            while (word < src.Length && !IsBreak(src[word])) word++;
            if (word == i) word = i + 1;

            tokens.Add(new Token(Kind.Word, i, word, src[i..word]));
            i = word;
        }

        tokens.Add(new Token(Kind.Eof, src.Length, src.Length, ""));
        return tokens;
    }

    private static bool IsBreak(char c)
        => c is ' ' or '\t' or '\r' or '\n' or '{' or '}' or '=' or '"' or '#';
}
