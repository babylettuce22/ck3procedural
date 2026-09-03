using System.Text;

namespace Ck3MapGen.Core;

/// <summary>
/// Folds a name to plain lower-case ASCII letters, for the identifiers and loc keys built from it.
///
/// Done by table rather than by Unicode normalisation because this build runs with
/// <c>InvariantGlobalization</c> on, under which <see cref="string.Normalize(NormalizationForm)"/>
/// decomposes nothing — so a decompose-and-strip pass silently deleted every accented letter and
/// "Vömö" became the key <c>vm</c>, colliding with every other name that folded to the same
/// remnant. The table also does the right thing for the letters normalisation cannot: þ is "th",
/// æ is "ae", ø is "o", ß is "ss".
/// </summary>
public static class Ascii
{
    private static readonly Dictionary<char, string> Table = new()
    {
        ['á'] = "a", ['à'] = "a", ['â'] = "a", ['ä'] = "a", ['ã'] = "a", ['å'] = "a", ['ā'] = "a", ['ă'] = "a", ['ą'] = "a",
        ['æ'] = "ae", ['ç'] = "c", ['ć'] = "c", ['č'] = "c", ['ď'] = "d", ['đ'] = "d", ['ð'] = "d",
        ['é'] = "e", ['è'] = "e", ['ê'] = "e", ['ë'] = "e", ['ē'] = "e", ['ě'] = "e", ['ę'] = "e", ['ė'] = "e",
        ['ğ'] = "g", ['í'] = "i", ['ì'] = "i", ['î'] = "i", ['ï'] = "i", ['ī'] = "i", ['ı'] = "i", ['į'] = "i",
        ['ł'] = "l", ['ñ'] = "n", ['ń'] = "n", ['ň'] = "n",
        ['ó'] = "o", ['ò'] = "o", ['ô'] = "o", ['ö'] = "o", ['õ'] = "o", ['ø'] = "o", ['ō'] = "o", ['ő'] = "o",
        ['œ'] = "oe", ['ř'] = "r", ['ś'] = "s", ['š'] = "s", ['ş'] = "s", ['ß'] = "ss", ['ť'] = "t", ['þ'] = "th",
        ['ú'] = "u", ['ù'] = "u", ['û'] = "u", ['ü'] = "u", ['ū'] = "u", ['ű'] = "u", ['ů'] = "u",
        ['ý'] = "y", ['ÿ'] = "y", ['ź'] = "z", ['ž'] = "z", ['ż'] = "z",
    };

    /// <summary>Lower-case ASCII letters and digits only; spaces and hyphens become underscores when
    /// <paramref name="keepSeparators"/> is set, and everything else is dropped.</summary>
    public static string Fold(string text, bool keepSeparators = false)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char raw in text)
        {
            char c = char.ToLowerInvariant(raw);

            if (c is >= 'a' and <= 'z' or >= '0' and <= '9') { sb.Append(c); continue; }
            if (Table.TryGetValue(c, out string? plain)) { sb.Append(plain); continue; }
            if (keepSeparators && (c == ' ' || c == '-' || c == '_')) { sb.Append('_'); continue; }

            // A letter the table has not met: keep its base if normalisation can find one.
            if (char.IsLetter(c))
            {
                try
                {
                    foreach (char part in c.ToString().Normalize(NormalizationForm.FormD))
                        if (part is >= 'a' and <= 'z') sb.Append(part);
                }
                catch (Exception) { /* invariant mode without normalisation support: drop it */ }
            }
        }
        return sb.ToString();
    }
}
