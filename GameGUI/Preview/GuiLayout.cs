namespace Ck3MapGen.GameGui.Preview;

/// <summary>
/// A working model of PdxGui's box layout: measure every widget bottom-up, then place it top-down.
///
/// ---- What this is and is not ----
///
/// It is an approximation, and saying so is load-bearing. The engine's layout is closed source and
/// this reproduces the parts the generated windows actually lean on: boxes that stack, margins and
/// spacing, explicit and percentage sizes, expanding layout policies, anchors, and <c>expand</c>
/// spacers. It gets geometry right wherever a size is stated, which in this project's own widgets
/// is nearly everywhere, because they state sizes.
///
/// The known soft spot is text width. There is no font metric here — glyph widths come from a
/// per-size average in <see cref="GlyphWidth"/> — so a line of text is placed correctly and sized
/// roughly. Height is exact whenever the font template resolved, because vanilla's Font_Size_*
/// templates carry their own line height as <c>size = { 0 h }</c>.
///
/// Anything it meets and cannot honour is recorded on the widget's <see cref="ResolvedWidget.Notes"/>
/// rather than dropped, so the preview can show its own blind spots instead of quietly drawing a
/// confident wrong answer.
///
/// ---- Visibility ----
///
/// <c>visible</c> is a datafunction over live game state, which a static preview cannot evaluate.
/// Every widget is therefore laid out as though shown, and the condition is carried through to the
/// render so it can be displayed and toggled. That is the right default for debugging: the widget
/// you are trying to see is usually the one that is conditionally hidden.
/// </summary>
public static class GuiLayout
{
    /// <summary>How a widget arranges its children.</summary>
    public enum Kind
    {
        /// <summary>Children placed by their own position and anchor.</summary>
        Absolute,

        Vertical,
        Horizontal,

        /// <summary>A flexible spacer inside a box.</summary>
        Spacer,

        /// <summary>Draws text; has no children that matter to layout.</summary>
        Text,

        /// <summary>Fills its parent — background, and the textures that behave like one.</summary>
        Fill,

        /// <summary>A fixed-pitch grid: children land on slots rather than being packed.</summary>
        Grid,
    }

    public static Kind KindOf(ResolvedWidget widget)
    {
        // Matched against the whole type chain rather than the written name, because the written
        // name is usually a vanilla alias: `header_standard` is an hbox several links down.
        var names = widget.TypeChain
            .Append(widget.WrittenType)
            .Append(widget.Primitive ?? "")
            .Select(n => n.ToLowerInvariant())
            .ToList();

        bool Has(string fragment) => names.Any(n => n.Contains(fragment, StringComparison.Ordinal));

        if (Has("expand")) return Kind.Spacer;
        if (Has("background")) return Kind.Fill;

        // Before the box checks: a fixedgridbox is not a box that happens to wrap. Its children do
        // not touch each other, they sit on a lattice whose pitch is `addcolumn` by `addrow`,
        // independent of how big any of them turns out to be.
        if (Has("fixedgridbox")) return Kind.Grid;

        if (Has("vbox")) return Kind.Vertical;
        if (Has("hbox")) return Kind.Horizontal;

        // A flowcontainer says which way it runs; horizontal is the engine's default.
        if (Has("flowcontainer"))
            return widget.Text("direction") == "vertical" ? Kind.Vertical : Kind.Horizontal;

        if (Has("textbox") || Has("text_single") || Has("text_multi")) return Kind.Text;

        return Kind.Absolute;
    }

    /// <summary>
    /// One axis of a stated size, with the files' two spellings of "you decide" both honoured.
    ///
    /// <c>-1</c> is the explicit one. <c>0</c> is the other, and it only means auto on something
    /// that sizes to its content: vanilla's font templates are all <c>size = { 0 h }</c>, meaning a
    /// fixed line height and a width from the text — while <c>size = { 0 0 }</c> on a
    /// scripted_widgets host means a genuinely zero-sized anchor and has to stay zero. What tells
    /// them apart is <c>autoresize</c>, and text, which always sizes to its content.
    /// </summary>
    private static double? Stated(Measure? measure, double available, ResolvedWidget widget, Kind kind)
    {
        if (measure is not { } m) return null;

        // A percentage of nothing is nothing, and in game that means the widget vanishes.
        //
        // Observed 2026-08-24: an `icon` with `size = { 100% 100% }` inside a `button_group` drew
        // nothing at all. A button_group takes its size from its content, so the icon was asking for
        // all of a parent whose size depended on the icon, and the engine settled the circle at
        // zero. This engine resolves the percentage against whatever the parent was offered and so
        // draws the icon perfectly — which is exactly why the note is needed. It is the same family
        // as the box-size trap below: a size that is stated relative to something that is itself
        // still being decided.
        if (m.IsPercent && available <= 0)
        {
            widget.Notes.Add("a percentage size inside a content-sized parent resolves to zero in "
                + "game — state this one in pixels");
        }

        if (m.Against(available) is not { } value) return null;

        bool auto = value == 0 && (kind == Kind.Text || widget.Flag("autoresize"));

        return auto ? null : value;
    }

    /// <summary>
    /// What a parent is able to promise a child while it is being measured.
    ///
    /// Measurement happens before anyone knows how the leftover space in a box will be divided, so
    /// a child in a row cannot yet be told how wide it will end up. Two things follow, and both were
    /// bugs before they were written down:
    ///
    /// <c>ExpandH</c>/<c>ExpandV</c> — an expanding widget takes the space on offer, but only across
    /// the axis its parent stacks along. Along that axis it must measure at its natural size and
    /// take a share of the slack later. The flag has to travel DOWN the whole subtree, not just one
    /// level: an expanding label inside a vbox inside a row would otherwise measure at the full row
    /// width, make the vbox that wide, and shove everything after it off the end of the row.
    ///
    /// <c>DefiniteW</c>/<c>DefiniteH</c> — whether the available extent is a real number rather than
    /// a guess. A box fills an absolute parent only when that parent actually has a size; without
    /// the distinction, a list row inside a content-sized column inherits the whole scroll extent
    /// and every row in a nine-row list comes out five thousand pixels tall.
    /// </summary>
    private readonly record struct Constraint(
        Kind Parent, bool ExpandH, bool ExpandV, bool DefiniteW, bool DefiniteH)
    {
        public static readonly Constraint Viewport = new(Kind.Absolute, true, true, true, true);

        /// <summary>What this widget can promise its own children, once its size is known.</summary>
        public Constraint For(Kind kind, bool definiteW, bool definiteH)
            => new(kind, ExpandH && kind != Kind.Horizontal, ExpandV && kind != Kind.Vertical,
                definiteW, definiteH);
    }

    /// <summary>Padding taken out of a widget's own box before its children are placed.</summary>
    private readonly record struct Margins(double Left, double Right, double Top, double Bottom)
    {
        public double Horizontal => Left + Right;

        public double Vertical => Top + Bottom;

        public static Margins Of(ResolvedWidget widget)
        {
            double left = 0, right = 0, top = 0, bottom = 0;

            // `margin = { h v }` is horizontal then vertical, applied to both sides of each axis.
            if (widget.Pair("margin") is var (h, v))
            {
                left = right = h.Value;
                top = bottom = v.Value;
            }

            left += widget.Number("margin_left");
            right += widget.Number("margin_right");
            top += widget.Number("margin_top");
            bottom += widget.Number("margin_bottom");

            return new Margins(left, right, top, bottom);
        }
    }

    /// <summary>
    /// Lays a resolved tree out inside a viewport, filling in every widget's
    /// <see cref="ResolvedWidget.Box"/>.
    /// </summary>
    public static void Run(ResolvedWidget root, double viewportWidth, double viewportHeight)
    {
        var size = Measure(root, viewportWidth, viewportHeight, 0, Constraint.Viewport);
        Arrange(root, 0, 0, size.Width, size.Height);
    }

    // -------------------------------------------------------------------------------------------
    // Measure — what a widget wants to be, given what is available
    // -------------------------------------------------------------------------------------------

    private static (double Width, double Height) Measure(ResolvedWidget widget, double availWidth,
        double availHeight, int depth, Constraint constraint)
    {
        if (depth > 60)
        {
            widget.Notes.Add("stopped: nesting deeper than 60");
            return (0, 0);
        }

        var kind = KindOf(widget);
        var margins = Margins.Of(widget);
        var stated = widget.Pair("size");

        double? width = Stated(stated?.X, availWidth, widget, kind);
        double? height = Stated(stated?.Y, availHeight, widget, kind);

        // An expanding widget takes what the parent offers even when its type states a size of its
        // own. Vanilla types carry placeholder sizes they fully expect a use site to override —
        // `header_standard` says 100x50 and is never that — and honouring the placeholder over the
        // layout policy left every header in the preview as a 100px stub.
        //
        // Only across the parent's stacking axis, though. Along it, an expanding child takes a share
        // of the LEFTOVER space, which is worked out once the whole row is measured; inflating here
        // as well makes a box of n expanding children measure n times the space available, and that
        // figure becomes the next parent's available space. It compounds — a preview of one 720px
        // window came out fifteen million pixels tall.
        if (Expands(widget, horizontal: true) && constraint.ExpandH && availWidth > 0)
            width = availWidth;

        if (Expands(widget, horizontal: false) && constraint.ExpandV && availHeight > 0)
            height = availHeight;

        // A stacking box sitting directly inside an absolute container fills it.
        //
        // This is the one rule here inferred from how the files are WRITTEN rather than from
        // something they state. Vanilla's windows are near-universally a fixed-size `window`
        // holding one unsized `vbox = { using = Window_Margins … }`, and that vbox plainly occupies
        // the whole window rather than shrinking to its tallest label — a scrollbox inside it
        // stretches to the bottom of the window, which it could not do if the vbox were content-
        // sized. Without this the artifact index drew its list in the top third and left two-thirds
        // of the window empty.
        //
        // Only a box that has not been placed deliberately. A row carrying a `parentanchor` or a
        // `position` is being put somewhere specific by whoever wrote it — vanilla's window control
        // buttons are an hbox anchored top|right — and filling the parent throws that away, which
        // moved every window's close button to the far left.
        if (constraint.Parent == Kind.Absolute
            && kind is Kind.Vertical or Kind.Horizontal
            && widget.Text("parentanchor") is null
            && widget.Pair("position") is null)
        {
            if (constraint.DefiniteW && availWidth > 0) width ??= availWidth;
            if (constraint.DefiniteH && availHeight > 0) height ??= availHeight;
        }

        // A background has no size of its own — it is the parent's box, and it is measured as such
        // so it never contributes to what the parent wants to be.
        if (kind == Kind.Fill) return (width ?? 0, height ?? 0);

        double innerWidth = (width ?? availWidth) - margins.Horizontal;
        double innerHeight = (height ?? availHeight) - margins.Vertical;

        // A stated size on a stacking box is not reliably binding in game.
        //
        // Observed 2026-08-24: a `vbox` with `size = { 260 160 }` inside a fixedgridbox slot took
        // the SLOT's size instead — its background tiled the lattice edge to edge with none of the
        // gutter the pitch should have left — and then spread its children down the extra height.
        // This engine honours the stated size, so a card like that previews correctly and ships
        // wrong; the note is here because the discrepancy cannot be modelled away without knowing
        // which of a box's several size inputs actually wins, and guessing would make the preview
        // wrong in a new direction.
        //
        // The fix at the call site is to use a `widget` — a rectangle at a stated size whose
        // children sit where they are told — which is what the realm index card does now.
        if (kind is Kind.Vertical or Kind.Horizontal && stated is not null)
        {
            widget.Notes.Add("a stated `size` on a box may not bind in game — "
                + "use a widget for a fixed rectangle");
        }

        var inner = constraint.For(kind, width.HasValue, height.HasValue);

        double contentWidth = 0, contentHeight = 0;

        if (kind == Kind.Text)
        {
            (contentWidth, contentHeight) = MeasureText(widget, innerWidth);
        }
        else
        {
            double spacing = widget.Number("spacing");
            var children = LaidOut(widget).ToList();

            foreach (var child in children)
            {
                var childSize = Measure(child, innerWidth, innerHeight, depth + 1, inner);

                switch (kind)
                {
                    // Measured, because a card that overflows its slot is worth seeing, but it
                    // contributes nothing: the grid's size comes from the lattice and the count.
                    case Kind.Grid:
                        break;

                    case Kind.Vertical:
                        contentWidth = Math.Max(contentWidth, childSize.Width);
                        contentHeight += childSize.Height;
                        break;

                    case Kind.Horizontal:
                        contentWidth += childSize.Width;
                        contentHeight = Math.Max(contentHeight, childSize.Height);
                        break;

                    default:
                        // An absolute container is as big as the furthest corner any child reaches,
                        // ignoring the ones that opted out of being contained.
                        if (child.Flag("allow_outside")) break;

                        var offset = child.Pair("position");

                        contentWidth = Math.Max(contentWidth,
                            (offset?.X.Against(innerWidth) ?? 0) + childSize.Width);
                        contentHeight = Math.Max(contentHeight,
                            (offset?.Y.Against(innerHeight) ?? 0) + childSize.Height);
                        break;
                }
            }

            if (children.Count > 1 && kind is Kind.Vertical or Kind.Horizontal)
            {
                double total = spacing * (children.Count - 1);
                if (kind == Kind.Vertical) contentHeight += total;
                else contentWidth += total;
            }

            if (kind == Kind.Grid)
            {
                var grid = Lattice.Of(widget, children.Count, innerWidth);

                // The lattice is as wide as its columns, but the GRID takes the width it was
                // offered — which is what leaves room for the lattice to be centred in it.
                contentWidth = constraint.DefiniteW && innerWidth > 0
                    ? innerWidth
                    : grid.Columns * grid.ColumnWidth;

                contentHeight = grid.Rows * grid.RowHeight;
            }
        }

        return (width ?? contentWidth + margins.Horizontal,
                height ?? contentHeight + margins.Vertical);
    }

    /// <summary>
    /// Children that take part in layout.
    ///
    /// Backgrounds are excluded because they are painted behind the widget rather than placed in
    /// it, and including them would make every box at least as large as its own decoration.
    /// </summary>
    private static IEnumerable<ResolvedWidget> LaidOut(ResolvedWidget widget)
        => widget.Children.Where(c => KindOf(c) != Kind.Fill && !IsTooltip(c));

    /// <summary>
    /// Whether a widget is a tooltip body rather than part of the layout.
    ///
    /// PdxGui draws tooltips on their own layer, at the cursor, only while hovered — so they take no
    /// space in the parent and must not be measured into it. Vanilla's artifact icon carries a full
    /// tooltip window as a child, and laying it out inline gave a 64px icon a six-thousand-pixel
    /// subtree. Matched by name, which is crude and works: every one of them says so.
    /// </summary>
    private static bool IsTooltip(ResolvedWidget widget)
        => widget.WrittenType.Contains("tooltip", StringComparison.OrdinalIgnoreCase)
           || (widget.Text("name")?.Contains("tooltip", StringComparison.OrdinalIgnoreCase) ?? false);

    // -------------------------------------------------------------------------------------------
    // Arrange — where it actually goes
    // -------------------------------------------------------------------------------------------

    private static void Arrange(ResolvedWidget widget, double x, double y, double width, double height,
        int depth = 0)
    {
        // Arranging is always done at a known size, so whatever a child is measured against here is
        // a real extent rather than a guess.
        var inner = new Constraint(KindOf(widget), true, true, true, true)
            .For(KindOf(widget), definiteW: true, definiteH: true);

        widget.Box = new LayoutBox(x, y, width, height);

        if (depth > 60) return;

        var kind = KindOf(widget);
        var margins = Margins.Of(widget);

        double innerX = x + margins.Left;
        double innerY = y + margins.Top;
        double innerWidth = width - margins.Horizontal;
        double innerHeight = height - margins.Vertical;

        // Backgrounds are painted over the whole of the parent's box, decoration included.
        foreach (var background in widget.Children.Where(c => KindOf(c) == Kind.Fill))
            Arrange(background, x, y, width, height, depth + 1);

        var children = LaidOut(widget).ToList();
        if (children.Count == 0) return;

        if (kind is Kind.Vertical or Kind.Horizontal)
        {
            ArrangeBox(widget, children, kind, innerX, innerY, innerWidth, innerHeight, depth, inner);
            return;
        }

        if (kind == Kind.Grid)
        {
            ArrangeGrid(widget, children, innerX, innerY, depth, inner);
            return;
        }

        foreach (var child in children)
        {
            var desired = Measure(child, innerWidth, innerHeight, depth + 1, inner);

            double childWidth = Expands(child, horizontal: true) ? innerWidth : desired.Width;
            double childHeight = Expands(child, horizontal: false) ? innerHeight : desired.Height;

            var (childX, childY) = Anchor(child, innerX, innerY, innerWidth, innerHeight,
                childWidth, childHeight);

            Arrange(child, childX, childY, childWidth, childHeight, depth + 1);
        }
    }

    /// <summary>
    /// A stacking box: fixed children keep their measured extent, expanding ones and
    /// <c>expand</c> spacers share whatever is left.
    /// </summary>
    private static void ArrangeBox(ResolvedWidget widget, List<ResolvedWidget> children, Kind kind,
        double innerX, double innerY, double innerWidth, double innerHeight, int depth,
        Constraint inner)
    {
        bool vertical = kind == Kind.Vertical;
        double spacing = widget.Number("spacing");

        var sizes = children
            .Select(c => Measure(c, innerWidth, innerHeight, depth + 1, inner))
            .ToList();

        double along = vertical ? innerHeight : innerWidth;
        double used = spacing * Math.Max(0, children.Count - 1);

        for (int i = 0; i < children.Count; i++)
            used += vertical ? sizes[i].Height : sizes[i].Width;

        // Everything that wants the leftover space gets an equal share of it. The engine weights
        // this by layout policy in ways this does not model; with one spacer in a row — which is
        // the shape every generated widget here uses — the two agree.
        var flexible = children
            .Select((child, i) => (child, i))
            .Where(e => KindOf(e.child) == Kind.Spacer || Expands(e.child, horizontal: !vertical))
            .ToList();

        double slack = Math.Max(0, along - used);
        double share = flexible.Count > 0 ? slack / flexible.Count : 0;

        double cursor = vertical ? innerY : innerX;

        for (int i = 0; i < children.Count; i++)
        {
            var child = children[i];
            bool flexes = flexible.Any(e => e.i == i);

            double extent = (vertical ? sizes[i].Height : sizes[i].Width) + (flexes ? share : 0);

            // Across the box, a child either stretches or keeps its measured size and is placed by
            // its anchor within the row.
            double across = Expands(child, horizontal: vertical)
                ? (vertical ? innerWidth : innerHeight)
                : (vertical ? sizes[i].Width : sizes[i].Height);

            double childWidth = vertical ? across : extent;
            double childHeight = vertical ? extent : across;

            double crossStart = vertical ? innerX : innerY;
            double crossExtent = vertical ? innerWidth : innerHeight;
            double crossSize = vertical ? childWidth : childHeight;
            double cross = crossStart + CrossOffset(child, crossExtent, crossSize, vertical);

            double childX = vertical ? cross : cursor;
            double childY = vertical ? cursor : cross;

            Arrange(child, childX, childY, childWidth, childHeight, depth + 1);

            cursor += extent + spacing;
        }
    }

    /// <summary>
    /// The lattice a <c>fixedgridbox</c> lays its children out on.
    ///
    /// <c>addcolumn</c> and <c>addrow</c> are the pitch — the step from one slot to the next, not
    /// the size of what goes in it — and <c>datamodel_wrap</c> is how many slots there are before
    /// the next line starts. A grid whose cards are wider than <c>addcolumn</c> overlaps them, and
    /// that is the engine's behaviour rather than a mistake to correct here: seeing the overlap is
    /// the reason to preview a grid at all.
    /// </summary>
    private readonly record struct Lattice(int Columns, int Rows, double ColumnWidth, double RowHeight,
        bool Flipped)
    {
        public static Lattice Of(ResolvedWidget widget, int count, double available)
        {
            double columnWidth = widget.Number("addcolumn");
            double rowHeight = widget.Number("addrow");

            int wrap = (int)widget.Number("datamodel_wrap");

            // No wrap means one line. Which axis that line runs along is `flipdirection`'s business.
            bool flipped = widget.Flag("flipdirection");
            if (wrap <= 0) wrap = Math.Max(1, count);

            // `datamodel_wrap` is a MAXIMUM, not a count. The grid also wraps at whatever the space
            // it has been given will hold — measured 2026-08-24, when a wrap of 3 in a container
            // 772 wide laid five cards out as rows of 2, 2, 1 rather than 3, 2. Rows of 2/2/1 are
            // wrapping; clipping a three-wide lattice would have shown 2, then 2, then nothing.
            //
            // Only when the space is known. A grid measured against an indefinite extent has
            // nothing to divide, and its stated wrap is the best answer available.
            if (!flipped && columnWidth > 0 && available > 0)
                wrap = Math.Clamp((int)(available / columnWidth), 1, wrap);

            int lines = count == 0 ? 0 : (int)Math.Ceiling(count / (double)wrap);
            int across = Math.Min(count, wrap);

            return flipped
                ? new Lattice(lines, across, columnWidth, rowHeight, true)
                : new Lattice(across, lines, columnWidth, rowHeight, false);
        }

        /// <summary>The slot the <paramref name="index"/>th child occupies.</summary>
        public (double X, double Y) Slot(int index)
        {
            int wrap = Math.Max(1, Flipped ? Rows : Columns);

            int major = index / wrap;
            int minor = index % wrap;

            return Flipped
                ? (major * ColumnWidth, minor * RowHeight)
                : (minor * ColumnWidth, major * RowHeight);
        }
    }

    private static void ArrangeGrid(ResolvedWidget widget, List<ResolvedWidget> children,
        double innerX, double innerY, int depth, Constraint inner)
    {
        double available = widget.Box.Width - Margins.Of(widget).Horizontal;
        var grid = Lattice.Of(widget, children.Count, available);

        // The lattice sits centred in whatever width the grid was given, not flush left. Confirmed
        // in game 2026-08-24: a two-column lattice in a wider scrollbox was inset by half the
        // difference on each side, which is also why the column count and the left edge have to be
        // read together — a lattice that has quietly dropped a column looks centred either way.
        double lattice = grid.Columns * grid.ColumnWidth;
        double inset = Math.Max(0, (available - lattice) / 2);

        for (int i = 0; i < children.Count; i++)
        {
            var child = children[i];

            // Each card is measured against its slot, so a `size = { 100% … }` inside one means the
            // slot rather than the whole grid.
            var size = Measure(child, grid.ColumnWidth, grid.RowHeight, depth + 1, inner);
            var (x, y) = grid.Slot(i);

            Arrange(child, innerX + inset + x, innerY + y, size.Width, size.Height, depth + 1);
        }
    }

    /// <summary>Where a child sits across the axis its box runs along.</summary>
    private static double CrossOffset(ResolvedWidget child, double available, double size, bool vertical)
    {
        string anchor = child.Text("parentanchor") ?? "";

        if (vertical)
        {
            if (anchor.Contains("hcenter") || anchor.Contains("center")) return (available - size) / 2;
            if (anchor.Contains("right")) return available - size;
            return 0;
        }

        if (anchor.Contains("vcenter") || anchor.Contains("center")) return (available - size) / 2;
        if (anchor.Contains("bottom")) return available - size;
        return 0;
    }

    /// <summary>
    /// A child of an absolute container: anchored in the parent, then offset by its own position.
    /// </summary>
    private static (double X, double Y) Anchor(ResolvedWidget child, double innerX, double innerY,
        double innerWidth, double innerHeight, double childWidth, double childHeight)
    {
        string anchor = child.Text("parentanchor") ?? "";

        double x = innerX;
        double y = innerY;

        if (anchor.Contains("hcenter") || anchor == "center" || anchor.Contains("|center")
            || anchor.StartsWith("center"))
        {
            x = innerX + (innerWidth - childWidth) / 2;
        }

        if (anchor.Contains("right")) x = innerX + innerWidth - childWidth;

        if (anchor.Contains("vcenter") || anchor == "center" || anchor.Contains("|center")
            || anchor.StartsWith("center"))
        {
            y = innerY + (innerHeight - childHeight) / 2;
        }

        if (anchor.Contains("bottom")) y = innerY + innerHeight - childHeight;

        var offset = child.Pair("position");

        return (x + (offset?.X.Against(innerWidth) ?? 0),
                y + (offset?.Y.Against(innerHeight) ?? 0));
    }

    private static bool Expands(ResolvedWidget widget, bool horizontal)
        => widget.Text(horizontal ? "layoutpolicy_horizontal" : "layoutpolicy_vertical")
            is "expanding" or "growing";

    // -------------------------------------------------------------------------------------------
    // Text
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// The size a run of text wants, wrapped at <c>max_width</c> where one is set.
    ///
    /// Width is the approximation this whole file rests on — see the class comment. Height is not:
    /// vanilla's Font_Size_* templates carry their line height as <c>size = { 0 h }</c>, so once
    /// the template resolved, a wrapped paragraph's height is right.
    /// </summary>
    private static (double Width, double Height) MeasureText(ResolvedWidget widget, double available)
    {
        double fontSize = widget.Number("fontsize", 15);
        double lineHeight = widget.Pair("size")?.Y.Value is > 0 and var stated ? stated : fontSize + 8;

        string content = widget.Text("raw_text") ?? widget.Text("text") ?? "";
        double natural = GuiText.Length(content) * GlyphWidth(fontSize);

        // `max_width` is what this project writes; `maximumsize` is what vanilla's own types use,
        // and a header that ignores it overflows its bar instead of eliding inside it.
        double cap = widget.Number("max_width");
        if (cap <= 0) cap = widget.Pair("maximumsize")?.X.Value ?? 0;
        if (cap <= 0) cap = available > 0 ? available : natural;

        double width = Math.Min(natural, cap);
        int lines = width <= 0 ? 1 : (int)Math.Ceiling(natural / Math.Max(1, cap));

        // A single-line widget never wraps however long its content is; it elides instead.
        bool single = widget.TypeChain.Append(widget.WrittenType)
            .Any(n => n.Contains("single", StringComparison.OrdinalIgnoreCase));

        if (single) lines = 1;

        return (width, Math.Max(lineHeight, lines * lineHeight));
    }

    /// <summary>
    /// Average glyph advance for a font size, as a fraction of it.
    ///
    /// 0.5 is an eyeballed fit for CK3's UI faces at the sizes the files use, and it is the number
    /// to change if previewed text consistently runs long or short against a screenshot.
    /// </summary>
    private static double GlyphWidth(double fontSize) => fontSize * 0.5;
}
