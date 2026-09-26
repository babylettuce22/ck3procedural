using System.Text;

namespace Ck3MapGen.Io;

/// <summary>
/// One item of Clausewitz script, as read for data rather than for rewriting.
///
/// <see cref="GameGui.GuiParser"/> is the lossless reader, built for the <c>.gui</c> grammar, and
/// it reads most of <c>common/</c> too. It does not read the coat-of-arms template grammar, which
/// has a form the interface language never uses: <c>pattern = list "basic_division"</c>, where the
/// value is two tokens. Read by the GUI parser that comes out as <c>pattern = list</c> with the
/// list's name glued onto the next line's key. This reader knows that form, the comparison
/// operators triggers use (<c>?=</c>, <c>&gt;=</c>…) and colour literals (<c>hsv { … }</c>), and
/// keeps nothing it does not need: no trivia, no reprinting.
/// </summary>
public sealed class ScriptNode
{
    /// <summary>The left-hand side, unquoted. For a bare value inside a block, the value itself.</summary>
    public string Key { get; init; } = "";

    /// <summary><c>=</c>, <c>?=</c>, <c>&lt;</c>, <c>&gt;=</c> and so on; empty for a bare value.</summary>
    public string Op { get; init; } = "";

    /// <summary>The scalar right-hand side, unquoted. Null for blocks and list references.</summary>
    public string? Value { get; init; }

    /// <summary>Whether <see cref="Value"/> (or a bare <see cref="Key"/>) was written in quotes.</summary>
    public bool Quoted { get; init; }

    /// <summary>The name in <c>key = list "name"</c>, or null.</summary>
    public string? ListRef { get; init; }

    /// <summary>The colour space in <c>key = hsv { … }</c> (<c>rgb</c>, <c>hsv</c>, <c>hsv360</c>), or null.</summary>
    public string? ColorSpace { get; init; }

    /// <summary>A block's items, or null for anything that is not a block.</summary>
    public List<ScriptNode>? Children { get; init; }

    public bool IsBlock => Children is not null;

    /// <summary>A bare value inside a block: <c>0.5</c> in <c>position = { 0.5 0.5 }</c>.</summary>
    public bool IsBare => Op.Length == 0 && Children is null;

    public IEnumerable<ScriptNode> Named(string key)
        => Children?.Where(c => !c.IsBare && c.Key == key) ?? [];

    public ScriptNode? First(string key) => Named(key).FirstOrDefault();

    /// <summary>The scalar value of the first child with this key.</summary>
    public string? Get(string key) => First(key)?.Value;

    /// <summary>The bare values of a block, in order: the numbers of <c>{ 0.5 0.5 }</c>.</summary>
    public List<string> Bare => Children?.Where(c => c.IsBare).Select(c => c.Key).ToList() ?? [];

    public override string ToString()
        => IsBare ? Key
            : ListRef is not null ? $"{Key} {Op} list \"{ListRef}\""
            : IsBlock ? $"{Key} {Op} {{ {Children!.Count} items }}"
            : $"{Key} {Op} {Value}";
}

public static class ScriptTree
{
    /// <summary>Parses a whole file into a root block whose children are the file's top-level items.</summary>
    public static ScriptNode Parse(string text)
    {
        var tokens = Tokenise(text);
        int pos = 0;
        return new ScriptNode { Key = "", Op = "=", Children = ParseItems(tokens, ref pos, topLevel: true) };
    }

    public static ScriptNode ParseFile(string path) => Parse(File.ReadAllText(path));

    private readonly record struct Token(string Text, bool Quoted);

    private static readonly HashSet<string> Operators = ["=", "?=", "!=", "<", ">", "<=", ">=", "=="];
    private static readonly HashSet<string> ColourSpaces = new(StringComparer.OrdinalIgnoreCase) { "rgb", "hsv", "hsv360", "hex" };

    private static List<ScriptNode> ParseItems(List<Token> t, ref int pos, bool topLevel)
    {
        var items = new List<ScriptNode>();
        while (pos < t.Count)
        {
            var tok = t[pos];
            if (!tok.Quoted && tok.Text == "}")
            {
                pos++;
                if (!topLevel) return items;
                continue;       // a stray closing brace at file level: vanilla has a few; skip it
            }
            if (!tok.Quoted && tok.Text == "{")
            {
                pos++;
                items.Add(new ScriptNode { Key = "", Op = "", Children = ParseItems(t, ref pos, topLevel: false) });
                continue;
            }

            pos++;
            if (pos < t.Count && !t[pos].Quoted && Operators.Contains(t[pos].Text))
            {
                string op = t[pos++].Text;
                items.Add(ParseValue(tok, op, t, ref pos));
            }
            else
            {
                items.Add(new ScriptNode { Key = tok.Text, Quoted = tok.Quoted });
            }
        }
        return items;
    }

    private static ScriptNode ParseValue(Token key, string op, List<Token> t, ref int pos)
    {
        if (pos >= t.Count) return new ScriptNode { Key = key.Text, Op = op, Value = "" };
        var v = t[pos];

        if (!v.Quoted && v.Text == "{")
        {
            pos++;
            return new ScriptNode { Key = key.Text, Op = op, Children = ParseItems(t, ref pos, topLevel: false) };
        }

        // key = list "name"
        if (!v.Quoted && v.Text == "list" && pos + 1 < t.Count && t[pos + 1].Text != "}" && t[pos + 1].Text != "{")
        {
            pos += 2;
            return new ScriptNode { Key = key.Text, Op = op, ListRef = t[pos - 1].Text };
        }

        // key = hsv { h s v }
        if (!v.Quoted && ColourSpaces.Contains(v.Text) && pos + 1 < t.Count && t[pos + 1].Text == "{")
        {
            pos += 2;
            return new ScriptNode { Key = key.Text, Op = op, ColorSpace = v.Text.ToLowerInvariant(), Children = ParseItems(t, ref pos, topLevel: false) };
        }

        pos++;
        return new ScriptNode { Key = key.Text, Op = op, Value = v.Text, Quoted = v.Quoted };
    }

    private static List<Token> Tokenise(string text)
    {
        var tokens = new List<Token>();
        int i = 0, n = text.Length;
        if (n > 0 && text[0] == '﻿') i = 1;

        while (i < n)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '#')
            {
                while (i < n && text[i] != '\n') i++;
                continue;
            }
            if (c == '"')
            {
                var sb = new StringBuilder();
                i++;
                while (i < n && text[i] != '"')
                {
                    if (text[i] == '\\' && i + 1 < n) { sb.Append(text[i + 1]); i += 2; continue; }
                    sb.Append(text[i++]);
                }
                i++;
                tokens.Add(new Token(sb.ToString(), true));
                continue;
            }
            if (c is '{' or '}')
            {
                tokens.Add(new Token(c.ToString(), false));
                i++;
                continue;
            }

            // An inline expression, `@[0.5 - cross_from_center_x ]`, is one token however it is spaced.
            if (c == '@' && i + 1 < n && text[i + 1] == '[')
            {
                int close = text.IndexOf(']', i);
                if (close < 0) close = n - 1;
                tokens.Add(new Token(text[i..(close + 1)], false));
                i = close + 1;
                continue;
            }
            if (c is '=' or '<' or '>' or '!' or '?')
            {
                // Two-character operators first.
                if (i + 1 < n && text[i + 1] == '=')
                {
                    tokens.Add(new Token(text.Substring(i, 2), false));
                    i += 2;
                }
                else
                {
                    tokens.Add(new Token(c.ToString(), false));
                    i++;
                }
                continue;
            }

            int start = i;
            while (i < n && !char.IsWhiteSpace(text[i]) && text[i] is not ('{' or '}' or '=' or '<' or '>' or '#' or '"')
                   && !(text[i] is '!' or '?' && i + 1 < n && text[i + 1] == '='))
                i++;
            tokens.Add(new Token(text[start..i], false));
        }
        return tokens;
    }
}
