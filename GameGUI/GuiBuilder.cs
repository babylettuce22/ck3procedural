namespace Ck3MapGen.GameGui;

/// <summary>
/// Builds <c>.gui</c> widgets as objects rather than as text.
///
/// This is <see cref="JominiBuilder"/>'s argument applied to the interface files, and it is the
/// same argument: the nesting of the output should be visible in the code that produces it. Script
/// emitters here used to append literal <c>"\t\t\t"</c> strings and the GUI emitters were worse,
/// because a widget is deeper than a script block — the practice window reached eight tabs, so its
/// closing braces were string literals that had to be counted character by character to check.
/// Everything below indents itself.
///
/// The other half of what it removes is repetition that was invisible because it was spread out. A
/// button wired to a scripted_gui needs four properties — <c>visible</c>, <c>enabled</c>,
/// <c>tooltip</c>, <c>onclick</c> — each naming the same key and repeating the same ninety-character
/// scope chain. Six such buttons in one widget is twenty-four copies, and getting one wrong fails
/// silently, because a scripted_gui asked in a scope it does not expect just evaluates false. See
/// <see cref="Bind"/>, which is one line.
///
/// Deliberately not a schema. It does not know which properties a <c>vbox</c> accepts or whether a
/// <c>using</c> resolves — CK3 and ck3-tiger answer that better, and a list of allowed properties
/// here would be one more thing to update per patch. Anything it cannot express goes through
/// <see cref="Raw"/>, which parses real <c>.gui</c> text into nodes, and is what makes a writer
/// convertible a widget at a time rather than all at once.
/// </summary>
public sealed class GuiBuilder
{
    public GuiNode Node { get; }

    /// <summary>Whether the next child added should have a blank line above it.</summary>
    private bool _gap;

    /// <summary>A comment waiting for the next child added. See <see cref="CommentNext"/>.</summary>
    private string? _comment;

    private GuiBuilder(GuiNode node) => Node = node;

    /// <summary>
    /// Adds a child, applying a pending <see cref="Gap"/>.
    ///
    /// Every property setter and every <c>Add</c> funnels through here, so the gap attaches to
    /// whatever comes next regardless of which of them it is.
    /// </summary>
    private GuiBuilder Attach(GuiNode child)
    {
        // Or-ed rather than assigned, so a widget that asked for its own gap with `Gapped` keeps it
        // when a parent attaches it without one.
        child.BlankBefore |= _gap;
        child.Comment ??= _comment;

        _gap = false;
        _comment = null;

        Node.Add(child);
        return this;
    }

    /// <summary>
    /// A blank line before the next thing added, for grouping in the generated file.
    ///
    /// Presentation only — the engine does not care. It is here because these widgets are spliced
    /// into files people open when something is wrong with them, and a widget whose identity,
    /// geometry and behaviour run together in one block is harder to read than one with the seams
    /// left in.
    /// </summary>
    public GuiBuilder Gap()
    {
        _gap = true;
        return this;
    }

    /// <summary>
    /// A blank line before THIS widget, asked for by the widget itself rather than by its parent.
    ///
    /// For a widget built by a helper and handed to a parent that has no reason to know it wants
    /// separating — the second background on a council seat, which is a distinct layer rather than
    /// more of the first.
    /// </summary>
    public GuiBuilder Gapped()
    {
        Node.BlankBefore = true;
        return this;
    }

    public static implicit operator GuiNode(GuiBuilder builder) => builder.Node;

    // -----------------------------------------------------------------------------------------
    // Widgets
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// A widget of any type: <c>Of("button_standard")</c>.
    ///
    /// The general form, and the one that keeps this file from having to grow a method per widget
    /// type in a game that ships hundreds. The named factories below exist only for the handful
    /// used often enough that reading them as words is worth it.
    /// </summary>
    public static GuiBuilder Of(string type, string? name = null)
    {
        var builder = new GuiBuilder(GuiNode.Block(type, "="));
        return name is null ? builder : builder.Name(name);
    }

    public static GuiBuilder Widget(string? name = null) => Of("widget", name);

    public static GuiBuilder VBox(string? name = null) => Of("vbox", name);

    public static GuiBuilder HBox(string? name = null) => Of("hbox", name);

    public static GuiBuilder FlowContainer(string? name = null) => Of("flowcontainer", name);

    public static GuiBuilder ScrollBox(string? name = null) => Of("scrollbox", name);

    public static GuiBuilder TextSingle(string? name = null) => Of("text_single", name);

    public static GuiBuilder TextMulti(string? name = null) => Of("text_multi", name);

    public static GuiBuilder ButtonStandard(string? name = null) => Of("button_standard", name);

    public static GuiBuilder ButtonClose(string? name = null) => Of("button_close", name);

    public static GuiBuilder Background() => Of("background");

    public static GuiBuilder Icon(string? name = null) => Of("icon", name);

    /// <summary>The spacer that pushes its siblings apart: <c>expand = {}</c>.</summary>
    public static GuiBuilder Expand() => Of("expand");

    /// <summary>
    /// A fixed-pitch grid of <see cref="Item"/> slots.
    ///
    /// <paramref name="columnWidth"/> and <paramref name="rowHeight"/> are the STEP between slots
    /// rather than the size of a card, and <paramref name="wrap"/> is how many go on a line before
    /// the next begins. A card larger than the step overlaps its neighbour — the engine does not
    /// grow the lattice to fit it.
    /// </summary>
    public static GuiBuilder FixedGridBox(int columnWidth, int rowHeight, int wrap)
        => Of("fixedgridbox")
            .Field("addcolumn", $"{columnWidth}")
            .Field("addrow", $"{rowHeight}")
            .Field("datamodel_wrap", $"{wrap}");

    /// <summary>
    /// The template a <see cref="DataModel"/> container draws once per entry: <c>item = { … }</c>.
    ///
    /// Inside it the entry is the datacontext, so its widgets address the entry's own type rather
    /// than the container's. A container with a datamodel and no item draws nothing and says
    /// nothing, which is the commonest way to get a blank list.
    /// </summary>
    public static GuiBuilder Item() => Of("item");

    /// <summary>
    /// <c>blockoverride "name" { … }</c> — filling a hole a vanilla template left open.
    ///
    /// No <c>=</c>, which is the spelling vanilla uses 6,636 times against 26 with one. Both parse;
    /// this writes the common one.
    /// </summary>
    public static GuiBuilder BlockOverride(string name)
        => new(GuiNode.Block("blockoverride", GuiNode.Quote(name)));

    /// <summary>A named animation or visibility state: <c>state = { name = _show … }</c>.</summary>
    public static GuiBuilder State(string name)
        => Of("state").Field("name", name);

    /// <summary>
    /// The declaration block a file of widget types lives in: <c>types name { … }</c>.
    ///
    /// No <c>=</c>, and the name is bare rather than quoted — a different head shape from every
    /// other block here, which is why it needs its own factory rather than <see cref="Of"/>.
    /// </summary>
    public static GuiBuilder Types(string name)
        => new(GuiNode.Block("types", name));

    /// <summary>
    /// One widget type: <c>type name = window { … }</c>.
    ///
    /// A type is a declaration, not an instance. Nothing is drawn until something writes
    /// <c>name = {}</c> — or, for a window with no parent to write that, until
    /// <c>gui/scripted_widgets</c> names it. See <see cref="Emit.GuiWriter"/>'s artifact index for
    /// the pairing.
    /// </summary>
    public static GuiBuilder Type(string name, string baseType)
        => new(GuiNode.Block("type", name, "=", baseType));

    // -----------------------------------------------------------------------------------------
    // Identity and layout
    // -----------------------------------------------------------------------------------------

    public GuiBuilder Name(string name) => Quoted("name", name);

    public GuiBuilder Size(int width, int height) => Inline("size", $"{width}", $"{height}");

    /// <summary>Sizes given as the engine's own strings — <c>100%</c>, <c>-1</c>.</summary>
    public GuiBuilder Size(string width, string height) => Inline("size", width, height);

    public GuiBuilder Position(int x, int y) => Inline("position", $"{x}", $"{y}");

    public GuiBuilder Margin(int horizontal, int vertical)
        => Inline("margin", $"{horizontal}", $"{vertical}");

    public GuiBuilder MarginLeft(int value) => Field("margin_left", $"{value}");

    public GuiBuilder MarginBottom(int value) => Field("margin_bottom", $"{value}");

    public GuiBuilder Spacing(int value) => Field("spacing", $"{value}");

    public GuiBuilder ParentAnchor(string anchor) => Field("parentanchor", anchor);

    public GuiBuilder AllowOutside(bool value = true) => Field("allow_outside", YesNo(value));

    public GuiBuilder Movable(bool value = true) => Field("movable", YesNo(value));

    public GuiBuilder AutoResize(bool value = true) => Field("autoresize", YesNo(value));

    public GuiBuilder IgnoreInvisible(bool value = true) => Field("ignoreinvisible", YesNo(value));

    public GuiBuilder Direction(string direction) => Field("direction", direction);

    /// <summary>Both layout policies at once, which is how they are nearly always written.</summary>
    public GuiBuilder Expanding()
        => Field("layoutpolicy_horizontal", "expanding").Field("layoutpolicy_vertical", "expanding");

    public GuiBuilder ExpandingH() => Field("layoutpolicy_horizontal", "expanding");

    public GuiBuilder ExpandingV() => Field("layoutpolicy_vertical", "expanding");

    /// <summary>
    /// A layout policy other than <c>expanding</c> — <c>preferred</c>, <c>fixed</c>, <c>growing</c>,
    /// <c>shrinking</c>.
    ///
    /// <c>preferred</c> is the one worth knowing: it means "be the size of your content" where
    /// expanding means "take everything on offer". Leaving it off a container that has no size of
    /// its own is not neutral — a custom tooltip written without it filled the entire screen with
    /// its own background, because the tooltip layer offered it the screen and nothing declined.
    /// </summary>
    public GuiBuilder LayoutPolicy(string axis, string policy)
        => Field($"layoutpolicy_{axis}", policy);

    /// <summary>A vanilla template mixed in. Repeats are meaningful, so these accumulate.</summary>
    public GuiBuilder Using(params string[] templates)
    {
        foreach (string template in templates) Attach(GuiNode.Leaf("using", template));
        return this;
    }

    // -----------------------------------------------------------------------------------------
    // Text
    // -----------------------------------------------------------------------------------------

    /// <summary>A localisation key, looked up by the game.</summary>
    public GuiBuilder Text(string key) => Quoted("text", key);

    /// <summary>A datafunction as the text, which the localizer still resolves.</summary>
    public GuiBuilder Text(GuiExpr expression) => Quoted("text", expression.ToString());

    /// <summary>
    /// Text taken literally, with no localisation lookup.
    ///
    /// The right choice for any line mixing a literal with a datafunction: <c>text</c> sends the
    /// whole string through the localizer first, which logs an unlocalised-text error per line per
    /// load. Functions still resolve under <c>raw_text</c>; only the lookup goes.
    /// </summary>
    public GuiBuilder RawText(string text) => Quoted("raw_text", text);

    public GuiBuilder Format(string format) => Quoted("default_format", format);

    public GuiBuilder MaxWidth(int width) => Field("max_width", $"{width}");

    public GuiBuilder FontSize(int size) => Field("fontsize", $"{size}");

    public GuiBuilder Align(string align) => Field("align", align);

    public GuiBuilder Elide(string direction) => Field("elide", direction);

    // -----------------------------------------------------------------------------------------
    // Behaviour
    // -----------------------------------------------------------------------------------------

    public GuiBuilder DataContext(string expression) => Quoted("datacontext", expression);

    public GuiBuilder DataContext(GuiExpr expression) => Quoted("datacontext", expression.ToString());

    /// <summary>
    /// The list a container repeats its <see cref="Item"/> over.
    ///
    /// <c>datacontext</c>'s plural, and a different mechanism despite the near-identical spelling:
    /// datacontext names one thing for the widget it is on, datamodel names a sequence and makes
    /// the widget draw its <c>item</c> block once per entry. Getting the two confused produces a
    /// container that draws nothing, with no error.
    /// </summary>
    public GuiBuilder DataModel(string expression) => Quoted("datamodel", expression);

    public GuiBuilder DataModel(GuiExpr expression) => Quoted("datamodel", expression.ToString());

    public GuiBuilder Visible(GuiExpr condition) => Quoted("visible", condition.ToString());

    public GuiBuilder Enabled(GuiExpr condition) => Quoted("enabled", condition.ToString());

    public GuiBuilder OnClick(GuiExpr action) => Quoted("onclick", action.ToString());

    public GuiBuilder OnClick(string action) => Quoted("onclick", action);

    /// <summary>
    /// A button drawn in its pressed state — what marks the tab you are looking at.
    ///
    /// Pairs with <see cref="AlwaysTransparent"/> on the same condition. A tab that looks pressed
    /// but still takes the click re-runs its own <c>onclick</c>, which is harmless for a plain
    /// <c>Set</c> and is not for anything else, so vanilla writes both every time.
    /// </summary>
    public GuiBuilder Down(GuiExpr condition) => Quoted("down", condition.ToString());

    /// <summary>Whether the mouse passes straight through this widget.</summary>
    public GuiBuilder AlwaysTransparent(GuiExpr condition)
        => Quoted("alwaystransparent", condition.ToString());

    /// <summary>The literal form: a widget that never takes the mouse at all.</summary>
    public GuiBuilder AlwaysTransparent(bool value = true)
        => Field("alwaystransparent", YesNo(value));

    /// <summary>A localisation key as the tooltip.</summary>
    public GuiBuilder Tooltip(string key) => Quoted("tooltip", key);

    /// <summary>A tooltip the game builds at runtime.</summary>
    public GuiBuilder Tooltip(GuiExpr expression) => Quoted("tooltip", expression.ToString());

    public GuiBuilder Shortcut(string shortcut) => Quoted("shortcut", shortcut);

    public GuiBuilder Texture(string path) => Quoted("texture", path);

    /// <summary>
    /// A second texture combined with this widget's own: <c>modify_texture = { … }</c>.
    ///
    /// The route to drawing a vanilla icon in a colour. Building-type icons are pure black with an
    /// alpha mask — every opaque pixel of them is <c>(0,0,0)</c> — so <c>color</c> cannot tint them,
    /// because a multiply against black is black. Painting a swatch and masking it with the icon is
    /// what works: <c>Texture("…/colors/gold.dds").ModifyTexture(icon, "mask")</c>.
    ///
    /// <paramref name="blendMode"/> is the engine's own vocabulary — <c>alphamultiply</c>,
    /// <c>overlay</c>, <c>colordodge</c>, <c>multiply</c>, <c>mask</c>, <c>add</c>, <c>darken</c>.
    /// </summary>
    public GuiBuilder ModifyTexture(string path, string blendMode)
    {
        Attach(GuiNode.Block("modify_texture", "=")
            .Add(GuiNode.Leaf("texture", GuiNode.Quote(path)))
            .Add(GuiNode.Leaf("blend_mode", blendMode)));

        return this;
    }

    public GuiBuilder FitType(string fit) => Field("fittype", fit);

    public GuiBuilder Alpha(string alpha) => Field("alpha", alpha);

    /// <summary>A colour, written the way the files write it: <c>{ 0.15 0.15 0.15 1 }</c>.</summary>
    public GuiBuilder Color(params string[] channels) => Inline("color", channels);

    /// <summary>
    /// Wires every question a button asks about a scripted_gui to the same key and scope.
    ///
    /// The four properties are set as a set because they belong together. A button whose
    /// <c>visible</c> and <c>onclick</c> name different scripted_guis — or the same one in
    /// different scopes — appears when pressing it would do nothing, and nothing reports it: a
    /// scripted_gui asked in an unexpected scope evaluates false rather than erroring.
    ///
    /// <paramref name="also"/> narrows both conditions with a further term, for the common case of
    /// a button that additionally needs a live player.
    ///
    /// Split into four so a button that runs something other than the scripted_gui it asks about
    /// can take the first three and write its own <c>onclick</c> — which is what the oversee button
    /// does, opening an activity window rather than executing anything.
    /// </summary>
    public GuiBuilder Bind(ScriptedGui gui, GuiExpr? also = null)
        => Shown(gui, also).Usable(gui, also).Tip(gui).Runs(gui);

    /// <summary><c>visible</c> from the scripted_gui's own is_shown.</summary>
    public GuiBuilder Shown(ScriptedGui gui, GuiExpr? also = null)
        => Visible(also is null ? gui.IsShown() : GuiExpr.And(also, gui.IsShown()));

    /// <summary><c>enabled</c> from the scripted_gui's own is_valid.</summary>
    public GuiBuilder Usable(ScriptedGui gui, GuiExpr? also = null)
        => Enabled(also is null ? gui.IsValid() : GuiExpr.And(also, gui.IsValid()));

    /// <summary><c>tooltip</c> built by the scripted_gui, so it lists its own failed conditions.</summary>
    public GuiBuilder Tip(ScriptedGui gui) => Tooltip(gui.BuildTooltip());

    /// <summary><c>onclick</c> executing the scripted_gui.</summary>
    public GuiBuilder Runs(ScriptedGui gui) => OnClick(gui.Execute());

    /// <summary>
    /// The same four properties for a player interaction aimed at a title.
    ///
    /// A different engine mechanism with the same shape, and the same silent failure if the four
    /// disagree about which interaction they mean.
    /// </summary>
    public GuiBuilder Bind(TitleInteraction interaction)
        => Visible(interaction.IsShown())
            .Enabled(interaction.IsValid())
            .Tooltip(interaction.Tooltip())
            .OnClick(interaction.Open());

    // -----------------------------------------------------------------------------------------
    // Escape hatches and children
    // -----------------------------------------------------------------------------------------

    /// <summary>Any property, as written: <c>key = value</c>, unquoted.</summary>
    public GuiBuilder Field(string key, string value) => Attach(GuiNode.Leaf(key, value));

    /// <summary>Any property with a quoted value: <c>key = "value"</c>.</summary>
    public GuiBuilder Quoted(string key, string value)
        => Attach(GuiNode.Leaf(key, GuiNode.Quote(value)));

    /// <summary>A one-line block property: <c>key = { a b }</c>.</summary>
    public GuiBuilder Inline(string key, params string[] tokens)
        => Attach(GuiNode.InlineBlock(key, tokens));

    /// <summary>
    /// A comment above this widget in the generated file.
    ///
    /// Addressed to whoever opens the output, not to whoever reads this project — which is the
    /// distinction that decides whether a note belongs here or in a <c>///</c> above the method.
    /// </summary>
    public GuiBuilder Comment(string text)
    {
        Node.Comment = text;
        return this;
    }

    /// <summary>
    /// A comment above the next property or child added, rather than above this widget.
    ///
    /// For the notes that explain a single number — why a margin is 15, why a max_width is 370.
    /// Those belong beside the line they justify, and a reader who changes the number without them
    /// gets a silently clipped panel rather than an error.
    /// </summary>
    public GuiBuilder CommentNext(string text)
    {
        _comment = text;
        return this;
    }

    public GuiBuilder Add(params GuiBuilder[] children)
    {
        foreach (var child in children) Attach(child.Node);
        return this;
    }

    public GuiBuilder Add(IEnumerable<GuiBuilder> children)
    {
        foreach (var child in children) Attach(child.Node);
        return this;
    }

    public GuiBuilder Add(params GuiNode[] children)
    {
        foreach (var child in children) Attach(child);
        return this;
    }

    public GuiBuilder Add(IEnumerable<GuiNode> children)
    {
        foreach (var child in children) Attach(child);
        return this;
    }

    /// <summary>
    /// <c>.gui</c> text, parsed and added as children.
    ///
    /// The escape hatch, and the reason a writer can be converted a widget at a time: a block that
    /// is already correct as a string literal goes through here unchanged while the code around it
    /// moves to the builder. What comes back out is a real tree — it can be searched and patched
    /// like anything else — so this costs nothing but the chance to read the widget as code.
    /// </summary>
    public GuiBuilder Raw(string guiText)
    {
        foreach (var node in Parse(guiText)) Attach(node);
        return this;
    }

    /// <summary>
    /// Standalone <c>.gui</c> text as nodes, for splicing where no parent is being built.
    ///
    /// Cloned on the way out, which drops the source text the parser attached. That is what makes
    /// the result re-indent to wherever it ends up instead of carrying the indentation of the C#
    /// string literal it was written in — the mixed tabs-and-spaces that whole-line prefixing used
    /// to leave in these files.
    /// </summary>
    public static List<GuiNode> Parse(string guiText)
        => [.. GuiParser.Parse(guiText).Roots.Select(n => n.Clone())];

    /// <summary>The single widget that <paramref name="guiText"/> describes.</summary>
    public static GuiNode ParseOne(string guiText)
    {
        var nodes = Parse(guiText);

        return nodes.Count == 1
            ? nodes[0]
            : throw new FormatException($"expected one widget, found {nodes.Count}");
    }

    private static string YesNo(bool value) => value ? "yes" : "no";

    public override string ToString() => Node.ToString();
}
