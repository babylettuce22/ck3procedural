using System.Globalization;

namespace Ck3MapGen.GameGui.Preview;

/// <summary>
/// One widget with everything the engine would give it already folded in — the output of
/// <see cref="GuiLibrary.Resolve"/> and the input to <see cref="GuiLayout"/>.
///
/// Properties are a flat bag with last-write-wins, which is the rule the engine follows and the
/// reason ordering is preserved all the way through the type chain: a <c>size</c> on the instance
/// beats one from its type, which beats one from the template the type pulled in. Where the bag is
/// wrong is where a property is legitimately repeatable — <c>onclick</c> and <c>background</c> both
/// occur more than once on purpose — so backgrounds are children and onclick is recorded but never
/// read by the layout.
/// </summary>
public sealed class ResolvedWidget(string writtenType)
{
    /// <summary>The type as written — <c>button_standard</c>, <c>vbox</c>.</summary>
    public string WrittenType { get; } = writtenType;

    /// <summary>The engine primitive the type chain bottoms out at, if it was found.</summary>
    public string? Primitive { get; set; }

    /// <summary>Every type walked to get here, base last-resolved first.</summary>
    public IReadOnlyList<string> TypeChain { get; set; } = [];

    public Dictionary<string, string> Props { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<ResolvedWidget> Children { get; } = [];

    /// <summary>Named states found on the widget. Not expanded — see <see cref="GuiLibrary"/>.</summary>
    public List<string> States { get; } = [];

    /// <summary>Anything the resolver or the layout could not honour, for the report.</summary>
    public List<string> Notes { get; } = [];

    /// <summary>Where the layout writes the box it computed.</summary>
    public LayoutBox Box { get; set; }

    public void Set(string key, string value) => Props[key] = value;

    public void SetInline(string key, string[] tokens) => Props[key] = string.Join(' ', tokens);

    public string? Prop(string key) => Props.GetValueOrDefault(key);

    /// <summary>A property with its quotes stripped, which is how nearly all of them are read.</summary>
    public string? Text(string key) => Prop(key) is { } v ? GuiNode.Unquote(v) : null;

    public bool Flag(string key) => Text(key) is "yes";

    /// <summary>The widget's name, or its type if it has none — what an inspector row is titled.</summary>
    public string Label => Text("name") ?? WrittenType;

    /// <summary>
    /// A pair like <c>size</c> or <c>position</c>, as two numbers plus their percent flags.
    ///
    /// <c>-1</c> and <c>0</c> both mean "decide for me" in different places, and a percentage is a
    /// share of the parent, so the caller needs to know which it got rather than a single number.
    /// </summary>
    public (Measure X, Measure Y)? Pair(string key)
    {
        if (Text(key) is not { } raw) return null;

        var parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;

        return (Measure.Parse(parts[0]), Measure.Parse(parts[1]));
    }

    public double Number(string key, double fallback = 0)
        => Text(key) is { } v && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture,
            out double parsed) ? parsed : fallback;
}

/// <summary>
/// One axis of a size or position: a number of pixels, a percentage of the parent, or "auto".
/// </summary>
public readonly record struct Measure(double Value, bool IsPercent, bool IsAuto)
{
    public static Measure Parse(string token)
    {
        if (token.EndsWith('%')
            && double.TryParse(token[..^1], NumberStyles.Float, CultureInfo.InvariantCulture,
                out double percent))
        {
            return new Measure(percent, IsPercent: true, IsAuto: false);
        }

        if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            return new Measure(0, false, IsAuto: true);

        // -1 is the files' spelling of "leave this axis alone". Zero is genuinely zero on a
        // container that is only there to position something, so the two are not the same.
        return new Measure(value, false, IsAuto: value < 0);
    }

    /// <summary>Resolved against an available extent, or null when it is the parent's call.</summary>
    public double? Against(double available)
        => IsAuto ? null : IsPercent ? available * Value / 100.0 : Value;
}

/// <summary>An absolute rectangle in preview space, after layout.</summary>
public readonly record struct LayoutBox(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;
}
