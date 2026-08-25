using System.Globalization;
using System.Text;

namespace Ck3MapGen.GameGui.Preview;

/// <summary>
/// Renders a laid-out widget tree to a single self-contained HTML page.
///
/// HTML rather than an image, for three reasons that all point the same way: the page can carry the
/// inspector beside the picture, so a box that is in the wrong place can be interrogated rather than
/// squinted at; text is laid out by something that actually has font metrics, which is the one thing
/// <see cref="GuiLayout"/> cannot do; and it needs no drawing library, which keeps
/// <see cref="GuiLibrary"/> and everything beside it dependency-free and portable to a standalone
/// editor later.
///
/// Textures come in through a delegate rather than being loaded here, for the same reason. The
/// preview knows it wants <c>gfx/interface/window_background.dds</c>; how that becomes something a
/// browser can draw is the host's problem, and on a platform with no DDS decoder the host returns
/// null and the preview draws a labelled placeholder.
///
/// What the page deliberately shows that the game does not: every widget's box, including the ones
/// whose <c>visible</c> is false. A static preview cannot evaluate a datafunction over live game
/// state, and the widget being debugged is usually the conditional one, so they are drawn with
/// their condition attached and can be toggled off.
/// </summary>
public sealed class GuiPreview
{
    /// <summary>
    /// Turns a texture path from a <c>.gui</c> file into something a browser can draw — normally a
    /// <c>data:</c> URI. Returns null when the file is missing or in a format the host cannot read.
    /// </summary>
    public Func<string, string?>? Textures { get; init; }

    /// <summary>
    /// Turns a localisation key into the text the game would show. Returns null when unknown.
    ///
    /// Without it every label in the preview reads as its own key, and a preview that cannot be
    /// read cannot be compared against a screenshot — which is the one thing it is for.
    /// </summary>
    public Func<string, string?>? Localise { get; init; }

    /// <summary>Width of the simulated screen the window is laid out inside.</summary>
    public int ViewportWidth { get; init; } = 1920;

    public int ViewportHeight { get; init; } = 1080;

    /// <summary>What the page is titled, and the heading it carries.</summary>
    public string Title { get; init; } = "GUI preview";

    /// <summary>Lines shown in the report panel — where the tree came from, what did not resolve.</summary>
    public List<string> Report { get; } = [];

    private readonly StringBuilder _body = new();
    private readonly StringBuilder _rows = new();
    private int _id;

    public string Render(ResolvedWidget root)
    {
        GuiLayout.Run(root, ViewportWidth, ViewportHeight);

        // Drawn relative to the tree's own bounding box rather than to the viewport, so a window
        // anchored to screen centre does not open with half a screen of empty page in front of it —
        // and, more importantly, is not drawn at negative coordinates and clipped away entirely. It
        // cannot be the ROOT's box: a scripted_widgets host is a zero-sized anchor at the origin
        // whose whole content hangs off it at negative offsets.
        var bounds = Bounds(root);

        _body.Clear();
        _rows.Clear();
        _id = 0;

        // The bounding box stands in as the root's parent, so the top-left of what there is to see
        // lands at the origin of the canvas.
        Walk(root, 0, bounds.X, bounds.Y);

        return Page(bounds);
    }

    /// <summary>
    /// The rectangle that contains everything actually visible, however it is anchored.
    ///
    /// Clipping is applied on the way down, for the same reason the rendered page applies it: a
    /// scrollbox holding a long list is a fixed-size window onto content taller than itself, and
    /// counting the content would size the canvas to the content. Previewing the artifact index at
    /// nine rows produced a page forty-five thousand pixels tall with one row visible at the top of
    /// it.
    /// </summary>
    private static LayoutBox Bounds(ResolvedWidget root)
    {
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;

        Visit(root, null);

        return minX > maxX
            ? new LayoutBox(0, 0, 0, 0)
            : new LayoutBox(minX, minY, maxX - minX, maxY - minY);

        void Visit(ResolvedWidget widget, LayoutBox? clip)
        {
            var box = clip is { } region ? Intersect(widget.Box, region) : widget.Box;

            // A zero-sized widget is an anchor rather than a thing on screen, and a fully clipped
            // one is not on screen at all. Counting either drags the bounds somewhere nothing is.
            if (box is { Width: > 0, Height: > 0 })
            {
                minX = Math.Min(minX, box.X);
                minY = Math.Min(minY, box.Y);
                maxX = Math.Max(maxX, box.Right);
                maxY = Math.Max(maxY, box.Bottom);
            }

            var inner = Clips(widget) ? box : clip;

            foreach (var child in widget.Children) Visit(child, inner);
        }
    }

    /// <summary>
    /// Whether a widget confines its children to its own box.
    ///
    /// One rule, read here and written into the page as a CSS class, so what the canvas is sized to
    /// and what the browser draws cannot disagree.
    /// </summary>
    private static bool Clips(ResolvedWidget widget)
        => !widget.Children.Any(c => c.Flag("allow_outside"));

    private static LayoutBox Intersect(LayoutBox a, LayoutBox b)
    {
        double x = Math.Max(a.X, b.X);
        double y = Math.Max(a.Y, b.Y);

        return new LayoutBox(x, y,
            Math.Max(0, Math.Min(a.Right, b.Right) - x),
            Math.Max(0, Math.Min(a.Bottom, b.Bottom) - y));
    }

    /// <summary>
    /// Emits one widget and everything under it.
    ///
    /// The divs are NESTED and positioned relative to their parent, mirroring the widget tree rather
    /// than flattening it. That costs nothing and buys two things: the DOM reads like the thing it
    /// represents, which matters if this ever becomes an editor; and a parent can clip its children,
    /// which a flat list of absolutely-positioned boxes cannot. Vanilla leans on clipping harder
    /// than it looks — its window header holds an 890px gradient inside a 700px bar and expects the
    /// overspill to be cut off.
    /// </summary>
    private void Walk(ResolvedWidget widget, int depth, double parentX, double parentY)
    {
        int id = _id++;
        var box = widget.Box;

        var kind = GuiLayout.KindOf(widget);
        string? visible = widget.Text("visible");

        double x = box.X - parentX;
        double y = box.Y - parentY;

        var style = new StringBuilder(
            $"left:{Px(x)};top:{Px(y)};width:{Px(box.Width)};height:{Px(box.Height)}");

        string? texture = widget.Text("texture");

        // A mask is not a picture. CK3 uses these as alpha channels through a shader — rough window
        // edges, vignettes, the fade at the top of a scrollbox — and drawing one as an ordinary
        // background paints a solid white slab over whatever it was supposed to be shaping.
        bool mask = texture is not null
            && (texture.Contains("component_masks", StringComparison.OrdinalIgnoreCase)
                || texture.Contains("/mask_", StringComparison.OrdinalIgnoreCase));

        string? source = texture is null || mask ? null : Textures?.Invoke(texture);

        string? tint = widget.Text("color");

        if (source is not null && tint is not null && Rgb(tint) is { } colour)
        {
            // A tinted texture, drawn as a MASK rather than a picture.
            //
            // CK3's `color` on a texture is a multiply, and the textures it is used on — the
            // building-type icons, the flat icon set — are white silhouettes with an alpha channel.
            // Multiplying white by gold is gold, so masking a solid fill through the alpha gives
            // the same picture and, unlike a CSS blend, keeps the transparent parts transparent.
            style.Append($";background-color:{colour}")
                 .Append($";-webkit-mask-image:url({source});mask-image:url({source})")
                 .Append(";-webkit-mask-size:100% 100%;mask-size:100% 100%")
                 .Append(";-webkit-mask-repeat:no-repeat;mask-repeat:no-repeat");
        }
        else if (source is not null)
        {
            style.Append($";background-image:url({source});background-size:100% 100%");

            if (widget.Text("fittype") == "centercrop")
                style.Append(";background-size:cover;background-position:center");
        }
        else if (texture is not null && !mask)
        {
            style.Append(";background:repeating-linear-gradient(45deg,#3a3a4a,#3a3a4a 6px,"
                + "#32323f 6px,#32323f 12px)");
        }

        if (widget.Number("alpha", 1) is var alpha and < 1) style.Append($";opacity:{F(alpha)}");

        var classes = new List<string> { "w", "k-" + kind.ToString().ToLowerInvariant() };
        if (visible is not null) classes.Add("cond");
        if (texture is not null && source is null && !mask) classes.Add("noTex");

        // A widget clips its children unless one of them has asked to escape. `allow_outside` is how
        // a .gui says "draw me past my parent's edge", and it is used deliberately — the lore panel
        // and the debug widgets are all children of a window they hang outside of.
        if (Clips(widget)) classes.Add("clip");

        _body.Append($"<div class=\"{string.Join(' ', classes)}\" data-id=\"{id}\" "
            + $"style=\"{style}\">");

        if (kind == GuiLayout.Kind.Text) _body.Append(TextSpan(widget));

        Row(widget, id, depth, kind, visible);

        // Tooltip bodies take no part in layout (see GuiLayout.IsTooltip), so they have no box to
        // draw and would otherwise litter the tree with clipped one-pixel stubs.
        foreach (var child in widget.Children.Where(c => c.Box is { Width: > 0, Height: > 0 }))
            Walk(child, depth + 1, box.X, box.Y);

        _body.Append("</div>");
    }

    /// <summary>
    /// The text itself, sized and coloured from what the widget resolved to.
    ///
    /// Format codes are CK3's own inline styling — <c>#high</c>, <c>#weak</c>, <c>#N</c> — and are
    /// mapped to approximate colours rather than parsed properly. Getting them exactly right needs
    /// the game's <c>textformatting</c> definitions and is not what this is for; getting them
    /// roughly right is the difference between reading a panel and staring at grey on grey.
    /// </summary>
    private string TextSpan(ResolvedWidget widget)
    {
        // raw_text is literal by definition and is never looked up; `text` is a key unless it is
        // plainly something else.
        string? raw = widget.Text("raw_text");
        string content = raw ?? Localised(widget.Text("text") ?? "");
        double fontSize = widget.Number("fontsize", 15);

        string format = widget.Text("default_format") ?? "";
        string colour = format switch
        {
            var f when f.Contains("#high") => "#f0e0b8",
            var f when f.Contains("#weak") || f.Contains("#low") => "#8a8a92",
            var f when f.Contains("#N") => "#c86464",
            var f when f.Contains("#P") => "#7fb069",
            var f when f.Contains("#clickable") => "#7fa8d0",
            _ => "#d8d4cc",
        };

        var style = new StringBuilder($"font-size:{F(fontSize)}px;color:{colour}");

        if (widget.Text("align")?.Contains("center") == true) style.Append(";text-align:center");
        if (format.Contains("bold")) style.Append(";font-weight:600");

        string shown = GuiText.Display(content);

        // The full expression stays reachable on hover, and in the inspector, because knowing which
        // datafunction feeds a box is most of what you want from a preview of one.
        string title = shown == content ? "" : $" title=\"{Escape(content)}\"";

        return $"<span class=\"t\" style=\"{style}\"{title}>{Escape(shown)}</span>";
    }

    /// <summary>
    /// A <c>text</c> value as the player would read it.
    ///
    /// Only a bare key is looked up. Anything holding a datafunction is left alone — its content is
    /// decided at runtime by game state this cannot see, and showing <c>[Artifact.GetName]</c> is
    /// more honest than showing an empty box where a name will be.
    /// </summary>
    private string Localised(string value)
    {
        if (Localise is null || value.Length == 0 || value.Contains('[')) return value;

        return Localise(value) ?? value;
    }

    private void Row(ResolvedWidget widget, int id, int depth, GuiLayout.Kind kind, string? visible)
    {
        var box = widget.Box;

        string chain = widget.TypeChain.Count > 0
            ? string.Join(" ← ", widget.TypeChain.Reverse())
            : widget.WrittenType;

        var details = new StringBuilder();

        details.Append($"<div class=\"d\"><b>type</b> {Escape(chain)}</div>");
        details.Append($"<div class=\"d\"><b>box</b> {F(box.X)}, {F(box.Y)} "
            + $"&nbsp;{F(box.Width)} × {F(box.Height)}</div>");

        foreach (var (key, value) in widget.Props.OrderBy(p => p.Key, StringComparer.Ordinal))
            details.Append($"<div class=\"d\"><b>{Escape(key)}</b> {Escape(GuiNode.Unquote(value))}</div>");

        foreach (string state in widget.States)
            details.Append($"<div class=\"d s\"><b>state</b> {Escape(state)}</div>");

        foreach (string note in widget.Notes)
            details.Append($"<div class=\"d n\">{Escape(note)}</div>");

        _rows.Append($"<li class=\"r\" data-id=\"{id}\" style=\"--d:{depth}\">"
            + $"<span class=\"h\"><i>{kind.ToString().ToLowerInvariant()}</i> {Escape(widget.Label)}"
            + (visible is not null ? " <em>?</em>" : "")
            + $"</span><div class=\"x\">{details}</div></li>");
    }

    private string Page(LayoutBox bounds)
    {
        string report = Report.Count == 0
            ? ""
            : "<ul class=\"rep\">"
              + string.Concat(Report.Select(line => $"<li>{Escape(line)}</li>"))
              + "</ul>";

        return $$"""
            <!doctype html>
            <html><head><meta charset="utf-8"><title>{{Escape(Title)}}</title><style>
            :root{color-scheme:dark}
            *{box-sizing:border-box}
            body{margin:0;background:#15151a;color:#cfcbc4;
                 font:13px/1.45 ui-monospace,"Cascadia Mono",Menlo,Consolas,monospace}
            header{padding:8px 14px;border-bottom:1px solid #2c2c36;display:flex;gap:16px;
                   align-items:center;flex-wrap:wrap;position:sticky;top:0;background:#15151a;z-index:9}
            header h1{font-size:13px;margin:0;font-weight:600;color:#f0e0b8}
            label{display:flex;gap:5px;align-items:center;cursor:pointer;user-select:none}
            main{display:flex;align-items:flex-start;gap:0}
            #stage{flex:1;overflow:auto;padding:28px;min-height:calc(100vh - 40px);
                   background:#0e0e12 repeating-linear-gradient(45deg,#101014,#101014 10px,#0d0d11 10px,#0d0d11 20px)}
            #canvas{position:relative;transform-origin:top left}
            .w{position:absolute;border:1px solid transparent}
            body.out .w{border-color:rgba(120,170,255,.28)}
            body.out .k-text{border-color:rgba(255,200,120,.30)}
            body.out .k-fill{border-color:rgba(160,120,255,.25)}
            body.out .noTex{border-color:rgba(255,120,120,.45)}
            body:not(.cond) .cond{display:none}
            body.clip .clip{overflow:hidden}
            .w:hover{outline:1px solid #7fa8d0}
            .w.sel{outline:2px solid #f0b84a;z-index:50}
            .t{display:block;padding:0 1px;white-space:pre-wrap;overflow:hidden;
               font-family:Georgia,"Times New Roman",serif}
            .k-text{overflow:hidden}
            aside{width:430px;flex:none;border-left:1px solid #2c2c36;height:calc(100vh - 40px);
                  overflow:auto;padding:10px 12px}
            ul{list-style:none;margin:0;padding:0}
            .r{padding-left:calc(var(--d) * 11px)}
            .h{display:block;padding:2px 4px;border-radius:3px;cursor:pointer;white-space:nowrap;
               overflow:hidden;text-overflow:ellipsis}
            .h:hover{background:#22222c}
            .r.sel > .h{background:#2f2a1c;color:#f0e0b8}
            .h i{color:#6f7a8a;font-style:normal;margin-right:5px;font-size:11px}
            .h em{color:#c8a04a;font-style:normal}
            .x{display:none;margin:2px 0 6px 10px;padding-left:8px;border-left:1px solid #2c2c36}
            .r.sel .x{display:block}
            .d{color:#9a968e;padding:1px 0;word-break:break-word}
            .d b{color:#6f7a8a;font-weight:400;display:inline-block;min-width:104px}
            .d.n{color:#c86464}
            .d.s{color:#7f8fa8}
            .rep{margin:0 0 10px;padding:8px 10px;background:#1c1c24;border-radius:4px;color:#9a968e}
            .rep li{padding:1px 0}
            h2{font-size:11px;text-transform:uppercase;letter-spacing:.08em;color:#6f7a8a;
               margin:12px 0 5px;font-weight:600}
            </style></head><body class="out cond clip">
            <header>
              <h1>{{Escape(Title)}}</h1>
              <label><input type="checkbox" id="o" checked> boxes</label>
              <label><input type="checkbox" id="c" checked> conditional widgets</label>
              <label><input type="checkbox" id="k" checked> clip to parent</label>
              <label>zoom <input type="range" id="z" min="25" max="200" value="100" step="5"></label>
              <span id="zv">100%</span>
              <span style="color:#6f7a8a">{{F(bounds.Width)}} × {{F(bounds.Height)}}</span>
            </header>
            <main>
              <div id="stage"><div id="canvas"
                   style="width:{{Px(bounds.Width)}};height:{{Px(bounds.Height)}}">{{_body}}</div></div>
              <aside>{{report}}<h2>widget tree</h2><ul id="tree">{{_rows}}</ul></aside>
            </main>
            <script>
            const $=s=>document.querySelector(s), all=s=>[...document.querySelectorAll(s)];
            o.onchange=()=>document.body.classList.toggle('out',o.checked);
            c.onchange=()=>document.body.classList.toggle('cond',c.checked);
            k.onchange=()=>document.body.classList.toggle('clip',k.checked);
            z.oninput=()=>{canvas.style.transform=`scale(${z.value/100})`;zv.textContent=z.value+'%'};
            function pick(id){
              all('.sel').forEach(e=>e.classList.remove('sel'));
              const w=document.querySelector(`.w[data-id="${id}"]`);
              const r=document.querySelector(`.r[data-id="${id}"]`);
              if(w)w.classList.add('sel');
              if(r){r.classList.add('sel');r.scrollIntoView({block:'nearest'})}
            }
            all('.w').forEach(w=>w.onclick=e=>{e.stopPropagation();pick(w.dataset.id)});
            all('.r .h').forEach(h=>h.onclick=()=>pick(h.parentElement.dataset.id));
            </script></body></html>
            """;
    }

    /// <summary>
    /// A CK3 colour quadruple — <c>0.788235 0.643137 0.419608 1</c> — as a CSS colour.
    ///
    /// Null when it is not four numbers, which covers the <c>hsv</c> forms and anything a
    /// datafunction produced.
    /// </summary>
    private static string? Rgb(string colour)
    {
        var parts = colour.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return null;

        var channels = new int[3];

        for (int i = 0; i < 3; i++)
        {
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture,
                    out double value))
            {
                return null;
            }

            channels[i] = (int)Math.Round(Math.Clamp(value, 0, 1) * 255);
        }

        double alpha = parts.Length > 3
            && double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double a)
                ? Math.Clamp(a, 0, 1)
                : 1;

        return $"rgba({channels[0]},{channels[1]},{channels[2]},{F(alpha)})";
    }

    private static string Px(double value) => F(value) + "px";

    private static string F(double value)
        => Math.Round(value, 1).ToString(CultureInfo.InvariantCulture);

    private static string Escape(string text)
        => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
               .Replace("\"", "&quot;");
}
