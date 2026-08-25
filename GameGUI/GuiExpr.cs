namespace Ck3MapGen.GameGui;

/// <summary>
/// A datafunction expression — the <c>"[…]"</c> half of a <c>.gui</c> property.
///
/// These were strings, and composing them meant trimming the brackets off one to nest it inside
/// another. <c>GuiWriter</c> carried a helper called <c>Inner</c> that did exactly that, plus two
/// placeholder tokens (<c>{SHOW}</c> and <c>{SHOW_RAW}</c>) so a widget template could be handed
/// either the bracketed or the unbracketed spelling depending on where it landed. Both are gone:
/// an expression here knows its own <see cref="Inner"/> form and brackets itself once, at the
/// moment it becomes a property value.
///
/// Spacing is not free choice — it is matched to what the files already contain, because these
/// expressions are written into overrides of vanilla files and a diff against vanilla should show
/// the change and nothing else. <c>And( a, b )</c> and <c>Not( x )</c> carry spaces inside their
/// parentheses; <c>GetScriptedGui('key')</c> does not.
/// </summary>
public sealed class GuiExpr
{
    /// <summary>The expression without its enclosing brackets, ready to nest inside another.</summary>
    public string Inner { get; }

    private GuiExpr(string inner) => Inner = inner;

    /// <summary>An expression written out by hand, with or without brackets.</summary>
    public static GuiExpr Raw(string text)
    {
        string trimmed = text.Trim();

        return new GuiExpr(trimmed.StartsWith('[') && trimmed.EndsWith(']')
            ? trimmed[1..^1].Trim()
            : trimmed);
    }

    public static GuiExpr Not(GuiExpr inner) => new($"Not( {inner.Inner} )");

    /// <summary>
    /// <c>And( a, b )</c>, folded right so three terms read <c>And( a, And( b, c ) )</c>.
    ///
    /// The engine's <c>And</c> takes exactly two arguments, which is why vanilla nests them, and
    /// why a variadic helper here is worth more than it looks: every site that combined three
    /// conditions used to spell the nesting out by hand.
    /// </summary>
    public static GuiExpr And(params GuiExpr[] terms) => Fold("And", terms);

    public static GuiExpr Or(params GuiExpr[] terms) => Fold("Or", terms);

    private static GuiExpr Fold(string op, GuiExpr[] terms)
    {
        if (terms.Length == 0) throw new ArgumentException($"{op} needs at least one term");

        var folded = terms[^1];
        for (int i = terms.Length - 2; i >= 0; i--)
            folded = new GuiExpr($"{op}( {terms[i].Inner}, {folded.Inner} )");

        return folded;
    }

    /// <summary>A localisation lookup on a key built at runtime: the title-lore panel's whole trick.</summary>
    public static GuiExpr Localize(GuiExpr key) => new($"Localize( {key.Inner} )");

    public static GuiExpr Concatenate(GuiExpr a, GuiExpr b) => new($"Concatenate( {a.Inner}, {b.Inner} )");

    public static GuiExpr StringIsEmpty(GuiExpr value) => new($"StringIsEmpty( {value.Inner} )");

    /// <summary>A single-quoted literal, which is how the engine takes string arguments.</summary>
    public static GuiExpr Literal(string text) => new($"'{text}'");

    /// <summary>
    /// A script list, as a datamodel can read it: <c>Activity.MakeScope.GetList( 'name' )</c>.
    ///
    /// The <c>.MakeScope</c> is the whole reason this is a helper. Script lists live on a SCOPE, not
    /// on a GUI object, so the chain has to cross into scope-land before it can ask — and
    /// <c>Activity.GetList( 'x' )</c>, which is what everyone writes first, is not a function that
    /// exists. All six uses in vanilla spell it this way.
    /// </summary>
    public static GuiExpr GetList(string scopeFunction, string listName)
        => new($"{scopeFunction}.MakeScope.GetList( '{listName}' )");

    /// <summary>
    /// Whether a datamodel has no entries — for hiding a list container rather than drawing an
    /// empty frame around nothing.
    /// </summary>
    public static GuiExpr IsDataModelEmpty(GuiExpr list) => new($"IsDataModelEmpty( {list.Inner} )");

    /// <summary>The UI's global variable store, which vanilla uses for pure presentation state.</summary>
    public static GuiExpr VariableExists(string name)
        => new($"GetVariableSystem.Exists( '{name}' )");

    public static GuiExpr VariableToggle(string name)
        => new($"GetVariableSystem.Toggle( '{name}' )");

    public static GuiExpr VariableClear(string name)
        => new($"GetVariableSystem.Clear( '{name}' )");

    /// <summary>
    /// Sets a UI variable to a named value — the first half of vanilla's tab pattern.
    ///
    /// Distinct from <see cref="VariableToggle"/>, which is the two-state version: a toggle answers
    /// "is this panel open", a set answers "which of these panels is open". A window with three
    /// tabs cannot be written with toggles, because nothing would make them exclusive.
    /// </summary>
    public static GuiExpr VariableSet(string name, string value)
        => new($"GetVariableSystem.Set( '{name}', '{value}' )");

    /// <summary>
    /// Whether a UI variable currently holds a given value — the other half of the tab pattern.
    ///
    /// Asked three times per tab in vanilla's own windows: once by the panel's <c>visible</c>, and
    /// twice by the button, as <c>down</c> so it looks pressed and as <c>alwaystransparent</c> so
    /// the tab you are already on cannot be clicked again.
    /// </summary>
    public static GuiExpr VariableHasValue(string name, string value)
        => new($"GetVariableSystem.HasValue( '{name}', '{value}' )");

    /// <summary>The bracketed form, which is what a property value actually holds.</summary>
    public override string ToString() => $"[{Inner}]";

    /// <summary>The property value including its quotes: <c>"[…]"</c>.</summary>
    public string Quoted => GuiNode.Quote(ToString());
}

/// <summary>
/// The scope chain a <c>.gui</c> hands to a scripted_gui — <c>GuiScope.SetRoot( … ).End</c>.
///
/// One of these is built once per patch target and then asked four questions, which is the whole
/// reason it is a type: the chain for the settle button is ninety characters long and was written
/// out four times per button, once each for <c>visible</c>, <c>enabled</c>, <c>tooltip</c> and
/// <c>onclick</c>. Six buttons in one widget meant twenty-four copies of it, and a typo in any of
/// them fails silently — a scripted_gui handed a scope it does not expect simply evaluates false,
/// so the button goes quiet rather than complaining.
/// </summary>
public sealed record GuiScope
{
    private readonly string _root;
    private readonly List<(string Name, string DataFunction)> _extra = [];

    private GuiScope(string root) => _root = root;

    /// <summary>
    /// The root scope, given as the datafunction that produces it — <c>GetPlayer</c>,
    /// <c>CharacterWindow.GetCharacter</c>. <c>.MakeScope</c> is added here rather than by callers.
    /// </summary>
    public static GuiScope Root(string dataFunction) => new(dataFunction);

    /// <summary>A named scope beside the root, which is how a target reaches the trigger.</summary>
    public GuiScope With(string name, string dataFunction)
    {
        var next = new GuiScope(_root);
        next._extra.AddRange(_extra);
        next._extra.Add((name, dataFunction));
        return next;
    }

    public override string ToString()
    {
        string chain = $"GuiScope.SetRoot( {_root}.MakeScope )";

        foreach (var (name, fn) in _extra)
            chain += $".AddScope( '{name}', {fn}.MakeScope )";

        return chain + ".End";
    }
}

/// <summary>
/// One scripted_gui, bound to the scope it is asked in.
///
/// The four methods are the four things a <c>.gui</c> can do with a scripted_gui, and they exist
/// as a set because they belong together: a button whose <c>visible</c> and <c>onclick</c> name
/// different scripted_guis, or the same one in different scopes, is a button that appears when
/// pressing it would do nothing. Binding all four from one object is what makes that
/// unrepresentable rather than merely discouraged — see <see cref="GuiBuilder.Bind"/>.
/// </summary>
public sealed record ScriptedGui(string Key, GuiScope Scope)
{
    public GuiExpr IsShown() => GuiExpr.Raw($"GetScriptedGui('{Key}').IsShown( {Scope} )");

    public GuiExpr IsValid() => GuiExpr.Raw($"GetScriptedGui('{Key}').IsValid( {Scope} )");

    public GuiExpr Execute() => GuiExpr.Raw($"GetScriptedGui('{Key}').Execute( {Scope} )");

    public GuiExpr BuildTooltip() => GuiExpr.Raw($"GetScriptedGui('{Key}').BuildTooltip( {Scope} )");

    /// <summary>Hidden when this scripted_gui is shown — the wilderness guard, said once.</summary>
    public GuiExpr IsHidden() => GuiExpr.Not(IsShown());
}

/// <summary>
/// A player interaction aimed at a title, as a <c>.gui</c> button asks about it.
///
/// The same four-question shape as <see cref="ScriptedGui"/> over a different engine mechanism, and
/// here for the same reason: the four function names are long, near-identical, and easy to pair
/// with the wrong interaction key, and getting that wrong produces a button that shows when it
/// cannot act rather than an error.
///
/// The target is always <c>Title.Self</c>, which means the button — or something above it — has to
/// put a title in the datacontext. That is a real constraint and not one this type can check.
/// </summary>
public sealed record TitleInteraction(string Key)
{
    public GuiExpr IsShown()
        => GuiExpr.Raw($"GetPlayer.IsPlayerInteractionShownAndCanPickTitle( '{Key}', Title.Self )");

    public GuiExpr IsValid()
        => GuiExpr.Raw($"GetPlayer.IsPlayerInteractionWithTargetTitleValid( '{Key}', Title.Self )");

    public GuiExpr Tooltip()
        => GuiExpr.Raw($"GetPlayer.GetPlayerInteractionWithTargetTitleTooltip( '{Key}', Title.Self )");

    public GuiExpr Open()
        => GuiExpr.Raw($"GetPlayer.OpenPlayerInteractionWithTargetTitle( '{Key}', Title.Self )");
}
