using System.ComponentModel;
using System.Text;

namespace Ck3MapGen.AppGUI;

/// <summary>
/// A ToolTip that breaks a long tip into lines of a readable width.
///
/// A stock ToolTip only breaks lines where the text does: WinForms sets the tip's maximum width to
/// <see cref="SystemInformation.MaxWindowTrackSize"/>, the span of every monitor together. So a
/// tip written as a sentence or two came up as one strip of text hundreds of pixels long, and the
/// longest were wider than the monitor they opened on and ran off its edge. Here every tip set
/// through <see cref="SetToolTip"/> is word-wrapped to <see cref="MaxWidth"/> first, with line
/// breaks the stock control honours, so a tip keeps the system's own look and never needs more
/// room than a few hundred pixels.
///
/// <see cref="SetToolTip"/> hides the base method rather than overriding it (it is not virtual),
/// so a tip is only wrapped when it is set through this type: hold the field as a
/// <see cref="WrappingToolTip"/>, not a <see cref="ToolTip"/>.
/// </summary>
internal sealed class WrappingToolTip : ToolTip
{
    /// <summary>The widest a line of a tip gets, in 96-dpi pixels: about seventy characters.</summary>
    public const int MaxWidth = 380;

    public WrappingToolTip() { }

    public WrappingToolTip(IContainer container) : base(container) { }

    /// <summary>Sets the tip for <paramref name="control"/>, wrapped to <see cref="MaxWidth"/>.</summary>
    public new void SetToolTip(Control control, string? caption)
        => base.SetToolTip(control, caption is null ? null : Wrap(caption, TipFont, MaxWidth * control.DeviceDpi / 96));

    /// <summary>
    /// The font a stock tip is drawn in: the system's status font, which ToolTip leaves the common
    /// control to use rather than setting one of its own.
    /// </summary>
    private static Font TipFont => SystemFonts.StatusFont ?? SystemFonts.DefaultFont;

    /// <summary>
    /// <paramref name="text"/> with line breaks put in so that no line is wider than
    /// <paramref name="width"/>. Breaks already in the text are kept. A single word wider than the
    /// limit (a long path) keeps a line to itself rather than being split.
    /// </summary>
    public static string Wrap(string text, Font font, int width)
    {
        text = text.Replace("\r\n", "\n");
        if (!text.Contains('\n') && Measure(text, font) <= width) return text;

        var result = new StringBuilder();
        foreach (string paragraph in text.Split('\n'))
        {
            if (result.Length > 0) result.Append('\n');
            string line = "";
            foreach (string word in paragraph.Split(' '))
            {
                string trial = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && Measure(trial, font) > width)
                {
                    result.Append(line).Append('\n');
                    line = word;
                }
                else
                {
                    line = trial;
                }
            }
            result.Append(line);
        }
        return result.ToString();
    }

    private static int Measure(string text, Font font)
        => TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
}
