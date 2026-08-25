using System.Text;

namespace Ck3MapGen.GameGui.Preview;

/// <summary>
/// What a run of <c>.gui</c> text will look like on screen, as closely as a static preview can know.
///
/// Two things stand between the string in the file and the string the player reads, and both matter
/// to a preview because both change how much room it takes:
///
/// * Format markers. <c>#high;bold</c> … <c>#!</c> is styling, not content, and drawing it as
///   content makes every styled label measure several characters too wide.
/// * Datafunctions. <c>[Artifact.GetOwner.GetNameNoTooltip]</c> is replaced at runtime by a name
///   nothing here can know. Printed in full it is thirty-six characters where the game will draw
///   perhaps twelve, so a column sized for the real value overflows and reads as a layout bug that
///   is not there.
///
/// A datafunction is shown as its last call in angle brackets — <c>⟨GetNameNoTooltip⟩</c> — which is
/// short enough to sit in the space the value will occupy, and still says which value it is. The
/// full expression is kept for the inspector, where there is room for it.
/// </summary>
public static class GuiText
{
    /// <summary>The text as the preview should draw it.</summary>
    public static string Display(string content)
    {
        if (content.Length == 0) return content;

        var sb = new StringBuilder(content.Length);

        for (int i = 0; i < content.Length; i++)
        {
            char c = content[i];

            if (c == '[')
            {
                int close = content.IndexOf(']', i);
                if (close < 0) { sb.Append(content[i..]); break; }

                sb.Append('⟨').Append(LastCall(content[(i + 1)..close])).Append('⟩');
                i = close;
                continue;
            }

            if (c == '#')
            {
                // `#!` closes a format; `#word;word ` opens one and runs to the next space.
                if (i + 1 < content.Length && content[i + 1] == '!') { i++; continue; }

                int space = content.IndexOf(' ', i);
                if (space < 0) break;

                i = space;
                continue;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>How many characters <see cref="Display"/> will actually draw.</summary>
    public static int Length(string content) => Display(content).Length;

    /// <summary>
    /// The last call in a datafunction chain, with its arguments dropped.
    ///
    /// Splitting on dots is wrong inside an argument list — <c>GetScriptValue( 'a.b' )</c> — so the
    /// arguments come off first and the chain is split after.
    /// </summary>
    private static string LastCall(string expression)
    {
        string chain = expression;

        int paren = chain.IndexOf('(');
        if (paren >= 0) chain = chain[..paren];

        chain = chain.Trim();

        int dot = chain.LastIndexOf('.');
        string last = dot >= 0 ? chain[(dot + 1)..] : chain;

        return last.Length == 0 ? expression.Trim() : last;
    }
}
