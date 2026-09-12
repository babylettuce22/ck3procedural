using Ck3MapGen.GameGui;

namespace Ck3MapGen.Emit;

/// <summary>
/// The world chronicle: what the whole map remembers, newest first, in a right-side panel shaped
/// like intrigue or council and opened from the HUD tab column.
///
/// One of the authored windows under <c>Emit/GuiWindows/</c>. Nothing about it varies with the
/// world: it is the title window's lore panel with a fixed datacontext — <c>title:h_china</c>,
/// which is where <see cref="ChronicleRuntimeWriter"/> keeps the world's entries — so the same
/// file ships on every map and <c>--gui-only</c> can rewrite it. The row count is the one number
/// shared with the script side, and it is read from the writer rather than restated.
///
/// The shape is the society panel's, which spent several rounds learning it: a full-screen,
/// click-transparent host carrying the layer, a transparent <c>Window_Size_MainTab</c> shell
/// inside it, and vanilla's own <c>sidebar_background_right</c> drawing the frame once
/// <see cref="GuiWriter"/> has widened its condition. See <c>BaseFilesToCopy/Societies/gui/
/// gen_society_panel.gui</c> for the long version of each of those decisions.
///
/// <code>
/// Related base files: none — the open state is a GUI variable, see <see cref="IsOpen"/>.
///
/// Related generated files, written elsewhere:
///   Emit/ChronicleRuntimeWriter.cs   gen_chw_line_N and every string this window shows
///   Emit/ChronicleWriter.cs          gen_lore_h_china, the opener when there is a hegemony
///   Emit/GuiWriter.cs                the tab, and the IsRightWindowOpen widening
/// </code>
/// </summary>
public static class ChronicleWindow
{
    public const string HostName = "gen_chronicle_host";
    public const string WindowName = "gen_chronicle_window";

    public const string IconPath = "gfx/interface/skinned/hud_maintab/maintab_gen_chronicle.dds";

    /// <summary>
    /// The open state lives in the GUI's own variable system, not in a scripted_gui.
    ///
    /// The society panel keeps its state as a character variable behind a scripted_gui because a
    /// decision had to be able to open it, and a decision cannot reach the GUI layer. The
    /// chronicle has no decision door — the tab is the only way in — so it can use the same
    /// <c>GetVariableSystem</c> flag the lore panel does. That is not a matter of taste: a
    /// scripted_gui asked through <c>GuiScope.SetRoot( GetPlayer.MakeScope )</c> throws
    /// <i>"Scoped object of type 'character' is not valid ((no character) weak)"</i> for every
    /// frame in which there is no player, and once the question is in the tab's <c>down</c> and
    /// in every widened <c>IsRightWindowOpen</c> check it is asked by a dozen HUD widgets a
    /// frame. That logged 479 errors across one load (2026-09-09). Vanilla's <c>hud.gui</c> calls
    /// no scripted_gui at all, for exactly this reason. A variable-system flag needs no scope.
    /// </summary>
    private const string OpenFlag = "gen_chronicle_open";

    /// <summary>Whether the window is open, as the tab, the window and the HUD all ask it.</summary>
    public static GuiExpr IsOpen => GuiExpr.VariableExists(OpenFlag);

    /// <summary>The tab's onclick.</summary>
    public static GuiExpr ToggleOpen => GuiExpr.VariableToggle(OpenFlag);

    /// <summary>The close button's onclick, the <c>_hide</c> state's, and the society tab's.</summary>
    public static GuiExpr Close => GuiExpr.VariableClear(OpenFlag);

    public static void Write(string modDir, string gameDir)
    {
        var doc = GuiDocument.Create("chronicle window", "gui", "gen_chronicle_window.gui");

        var world = GuiExpr.Raw($"GetTitleByKey('{ChronicleRuntimeWriter.WorldTitleKey}')");

        doc.Add(GuiBuilder.Types("gen_chronicle").Add(

            GuiBuilder.Type(HostName, "window")
                .Name(HostName)
                .AllowOutside()
                .ParentAnchor("center")
                // Screen-sized so the panel's top|right anchor means the screen's corner, and
                // click-transparent so a screen-sized invisible widget does not eat the map.
                .Size("100%", "100%")
                .AlwaysTransparent()
                // The layer belongs on the top-level window and nowhere else: on the panel it
                // would pull the panel out from under this host and break the anchor; absent, the
                // panel draws over the HUD.
                .Field("layer", "windows_layer")
                .Gap().Visible(GuiExpr.Raw(
                    "And( Not( IsPauseMenuShown ), And( Or( Not( IsObserver ), GetPlayer.IsValid ), "
                    + "IsDefaultGUIMode ) )"))
                .Gap().Add(GuiBuilder.Of(WindowName)),

            GuiBuilder.Type(WindowName, "window")
                .Gapped()
                .Name(WindowName)
                .AllowOutside()
                .Movable(false)
                .ParentAnchor("top|right")
                .Using("Window_Size_MainTab")
                // The second term is how vanilla's own views close this one: they cannot know
                // about us, but IsRightWindowOpen is true whenever one of them is up.
                .Gap().Visible(GuiExpr.And(IsOpen, GuiExpr.Not(GuiExpr.Raw("IsRightWindowOpen"))))

                .Gap().Add(GuiBuilder.State("_show")
                    .Using("Window_Position_MainTab", "Animation_FadeIn_Quick", "Sound_WindowShow_Standard"))

                // Hidden is not closed. When a vanilla view hides this panel through the visible
                // above, the flag would stay set and the tab would stay lit for a window nobody
                // can see — so hiding clears it too. Clearing an absent flag is a no-op.
                .Gap().Add(GuiBuilder.State("_hide")
                    .Using("Window_Position_MainTab_Hide", "Animation_FadeOut_Quick", "Sound_WindowHide_Standard")
                    .Quoted("on_start", Close.ToString()))

                // window_council.gui's body, structurally: the same three margins and the same
                // nested full-size widget, so the content sits where every main-tab window's does.
                .Gap().Add(GuiBuilder.Of("margin_widget")
                    .Size("100%", "100%")
                    .Field("margin_top", "30")
                    .Field("margin_bottom", "25")
                    .Field("margin_right", "13")
                    .Gap().Add(GuiBuilder.Widget()
                        .Size("100%", "100%")
                        .Gap().Add(GuiBuilder.VBox()
                            .Using("Window_Margins")
                            .Spacing(6)

                            .Gap().Add(GuiBuilder.Of("header_standard")
                                .ExpandingH()
                                .Gap().Add(GuiBuilder.BlockOverride("header_text")
                                    .Text("GEN_CHRONICLE_TITLE"))
                                .Gap().Add(GuiBuilder.BlockOverride("button_close")
                                    .OnClick(Close)
                                    // Escape, same as every engine window.
                                    .Shortcut("close_window")))

                            .Gap().Add(GuiBuilder.TextMulti()
                                .ExpandingH()
                                .MaxWidth(RowWidth)
                                .Format("#weak")
                                .Text("GEN_CHRONICLE_BLURB"))

                            .Gap().Add(GuiBuilder.ScrollBox()
                                .Expanding()
                                .Gap().Add(GuiBuilder.BlockOverride("scrollbox_content")
                                    .Add(Book(world)))))))));

        // THE LINE THE WHOLE FILE HANGS ON. The registry resolves a top-level widget INSTANCE,
        // not a type; without this the file loads clean and logs "Could not find widget".
        doc.Add(GuiBuilder.Of(HostName));

        doc.Ship(modDir);

        string registry = Path.Combine(modDir, "gui", "scripted_widgets");
        Directory.CreateDirectory(registry);
        Io.ParadoxText.WriteNoBom(
            Path.Combine(registry, "gen_chronicle_window.txt"),
            "# Instantiates the world chronicle. Written by Emit/GuiWindows/ChronicleWindow.cs.\n"
            + "#\n"
            + "# Names the HOST type, not the window itself: the host is what exists from startup,\n"
            + "# and the window it contains is what appears when the tab sets the flag.\n"
            + $"gui/gen_chronicle_window.gui = {HostName}\n");

        WriteIcon(modDir, gameDir);
    }

    /// <summary>
    /// The entries. A vbox whose datacontext is the world title, so every row below is exactly the
    /// title panel's row: one <c>Custom</c> call for the text, and <c>StringIsEmpty</c> of the same
    /// call for whether there is a row at all.
    ///
    /// Newest first — slot 0 is the newest — because the world window is consulted as a feed:
    /// "what just happened" is the question it answers. The title panel reads the other way, as a
    /// history continuing the static prehistory above it.
    ///
    /// The opener is the hegemony's own static lore, when there is one: on a map that supports a
    /// hegemony <see cref="ChronicleWriter"/> files a summary under <c>gen_lore_h_china</c>, and
    /// it is the nearest thing the world has to a preface. <c>Localize</c> of a key that does not
    /// exist is empty, which is the same gate the lore button relies on.
    /// </summary>
    /// <summary>
    /// The width budget, and it is a budget because getting it wrong clips rather than wraps: a
    /// max_width larger than the space available does not wrap early, it overflows and the
    /// scrollbox crops it (seen on screen at 560, 2026-09-09). Window_Size_MainTab is 655; the
    /// vbox's Window_Margins take 80, Scrollbox_Margins 35, the scrollbar 13 — 527 usable.
    /// </summary>
    private const int RowWidth = 500;

    private static GuiBuilder Book(GuiExpr world)
    {
        var opener = GuiExpr.Localize(GuiExpr.Literal($"gen_lore_{ChronicleRuntimeWriter.WorldTitleKey}"));
        var newest = Line(0);

        var box = GuiBuilder.VBox()
            .DataContext(world)
            .ExpandingH()
            .Spacing(10)
            .Gap().Add(
                GuiBuilder.TextSingle()
                    .ExpandingH()
                    .Format("#weak")
                    .Visible(GuiExpr.Not(GuiExpr.StringIsEmpty(opener)))
                    .Text("GEN_CHRONICLE_BEFORE"),
                GuiBuilder.TextMulti()
                    .ExpandingH()
                    .AutoResize()
                    .MaxWidth(RowWidth)
                    .Visible(GuiExpr.Not(GuiExpr.StringIsEmpty(opener)))
                    .Text(opener),
                GuiBuilder.TextSingle()
                    .ExpandingH()
                    .Format("#weak")
                    .Visible(GuiExpr.And(
                        GuiExpr.Not(GuiExpr.StringIsEmpty(opener)),
                        GuiExpr.Not(GuiExpr.StringIsEmpty(newest))))
                    .Text("GEN_CHRONICLE_SINCE"),
                GuiBuilder.TextMulti()
                    .ExpandingH()
                    .AutoResize()
                    .MaxWidth(RowWidth)
                    .Format("#weak")
                    .Visible(GuiExpr.StringIsEmpty(newest))
                    .Text("GEN_CHRONICLE_EMPTY"));

        for (int slot = 0; slot < ChronicleRuntimeWriter.WorldSlots; slot++)
        {
            var line = Line(slot);
            box.Add(GuiBuilder.TextMulti()
                .ExpandingH()
                .AutoResize()
                .MaxWidth(RowWidth)
                .Visible(GuiExpr.Not(GuiExpr.StringIsEmpty(line)))
                .Text(line));
        }

        return box;
    }

    private static GuiExpr Line(int slot)
        => GuiExpr.Raw($"Title.Custom('{ChronicleRuntimeWriter.WorldLine(slot)}')");

    // ===========================================================================================
    // The tab icon
    // ===========================================================================================

    /// <summary>
    /// The 90×90 tab icon: vanilla's book-inspiration icon, refitted to the tab's footprint.
    ///
    /// Vanilla's tab icons are not masks the button tints — they are small painted objects with
    /// soft highlights and no outline (intrigue is purple, factions is gold, decisions is a warm
    /// grey). Two attempts to draw one procedurally came out first as a black silhouette and then
    /// as a flat shape with a heavy border, and both read as a hole in the column. Vanilla already
    /// has a painted book in the right style at
    /// <c>gfx/interface/icons/inspirations/book_inspiration.dds</c>, so this resamples that,
    /// scaled so its content spans what a tab icon's does. The drawn book survives only as the
    /// fallback for a game folder without the file.
    ///
    /// Written at generation time rather than kept as a binary so <c>--gui-only</c> ships it and
    /// nothing in the repo has to be opened in an image editor to change it.
    /// </summary>
    private static void WriteIcon(string modDir, string gameDir)
    {
        const int size = 90;

        byte[] bgra = FromVanilla(gameDir, size) ?? DrawBook(size);

        string path = Path.Combine(modDir, IconPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Io.DdsWriter.WriteBgra(path, size, size, bgra);
    }

    /// <summary>
    /// Vanilla's book icon, decoded, cropped to its opaque bounds and area-averaged into a
    /// <paramref name="size"/> square so the content spans <see cref="Footprint"/> pixels — the
    /// span a vanilla tab icon's object occupies. Premultiplied through the filter so the edges
    /// stay clean rather than fringing dark.
    /// </summary>
    private static byte[]? FromVanilla(string gameDir, int size)
    {
        string source = Path.Combine(gameDir, "gfx", "interface", "icons", "inspirations", "book_inspiration.dds");
        if (!File.Exists(source)) return null;
        if (Io.DdsReader.Load(source) is not { } src) return null;

        // Opaque bounds, so the fit is of the object rather than of the file's padding.
        int minX = src.Width, minY = src.Height, maxX = -1, maxY = -1;
        for (int y = 0; y < src.Height; y++)
        for (int x = 0; x < src.Width; x++)
        {
            if (src.Bgra[(y * src.Width + x) * 4 + 3] < 24) continue;
            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
        }
        if (maxX < 0) return null;

        int bw = maxX - minX + 1, bh = maxY - minY + 1;
        float scale = Footprint / (float)Math.Max(bw, bh);
        float outW = bw * scale, outH = bh * scale;
        float offX = (size - outW) / 2, offY = (size - outH) / 2;

        var dst = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            // The source footprint of this destination pixel.
            float sx0 = minX + (x - offX) / scale, sx1 = minX + (x + 1 - offX) / scale;
            float sy0 = minY + (y - offY) / scale, sy1 = minY + (y + 1 - offY) / scale;
            int ix0 = Math.Max(minX, (int)Math.Floor(sx0)), ix1 = Math.Min(maxX, (int)Math.Ceiling(sx1) - 1);
            int iy0 = Math.Max(minY, (int)Math.Floor(sy0)), iy1 = Math.Min(maxY, (int)Math.Ceiling(sy1) - 1);
            if (ix1 < ix0 || iy1 < iy0) continue;

            double b = 0, g = 0, r = 0, a = 0, n = 0;
            for (int sy = iy0; sy <= iy1; sy++)
            for (int sx = ix0; sx <= ix1; sx++)
            {
                int i = (sy * src.Width + sx) * 4;
                double pa = src.Bgra[i + 3] / 255.0;
                b += src.Bgra[i] * pa; g += src.Bgra[i + 1] * pa; r += src.Bgra[i + 2] * pa;
                a += pa; n++;
            }
            if (a <= 0) continue;

            int o = (y * size + x) * 4;
            dst[o] = (byte)Math.Clamp(Math.Round(b / a), 0, 255);
            dst[o + 1] = (byte)Math.Clamp(Math.Round(g / a), 0, 255);
            dst[o + 2] = (byte)Math.Clamp(Math.Round(r / a), 0, 255);
            dst[o + 3] = (byte)Math.Clamp(Math.Round(a / n * 255), 0, 255);
        }

        return dst;
    }

    /// <summary>How many of the 90 pixels a vanilla tab icon's object spans, measured on
    /// intrigue, factions and decisions.</summary>
    private const int Footprint = 64;

    /// <summary>The procedural fallback: two parchment pages, a leather spine, a soft shadow.</summary>
    private static byte[] DrawBook(int size)
    {
        const int ss = 4; // supersampling per axis

        // Two page quads meeting at a spine, in the same footprint as vanilla's icons.
        (float x, float y)[] left  = [(12, 29), (44, 23), (44, 67), (12, 73)];
        (float x, float y)[] right = [(46, 23), (78, 29), (78, 73), (46, 67)];
        (float x, float y)[] spine = [(42, 21), (48, 21), (48, 71), (42, 71)];

        // Lines of text on each page, as thin quads following the page's slant.
        var lines = new List<(float x, float y)[]>();
        for (int i = 0; i < 3; i++)
        {
            float y0 = 36 + i * 10;
            lines.Add([(17, y0 + 1.5f), (39, y0 - 1.5f), (39, y0 + 0.8f), (17, y0 + 3.8f)]);
            lines.Add([(51, y0 - 1.5f), (73, y0 + 1.5f), (73, y0 + 3.8f), (51, y0 + 0.8f)]);
        }

        // Coverage of the page, the spine and the text, each 0..1 per pixel.
        var page = new float[size * size];
        var spineCov = new float[size * size];
        var text = new float[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            int p = 0, s = 0, t = 0;
            for (int sy = 0; sy < ss; sy++)
            for (int sx = 0; sx < ss; sx++)
            {
                float px = x + (sx + 0.5f) / ss;
                float py = y + (sy + 0.5f) / ss;
                if (Inside(spine, px, py)) s++;
                else if (Inside(left, px, py) || Inside(right, px, py))
                {
                    p++;
                    foreach (var line in lines) if (Inside(line, px, py)) { t++; break; }
                }
            }
            float n = ss * ss;
            page[y * size + x] = p / n;
            spineCov[y * size + x] = s / n;
            text[y * size + x] = t / n;
        }

        // The shadow: the object dilated by two pixels, in black, under everything. This is the
        // part that makes a vanilla icon sit on the column rather than float over it.
        var shadow = new float[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float best = 0;
            for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
            {
                if (dx * dx + dy * dy > 5) continue;
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= size || ny >= size) continue;
                best = Math.Max(best, page[ny * size + nx] + spineCov[ny * size + nx]);
            }
            shadow[y * size + x] = Math.Min(1, best);
        }

        var bgra = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            int i = y * size + x;
            float pg = page[i], sp = spineCov[i], tx = text[i], sh = shadow[i];

            // Parchment, a touch darker toward the spine and toward the bottom, the way
            // vanilla's objects carry a little modelled light; the spine is dark leather.
            float toSpine = 1 - Math.Min(1, Math.Abs(x - 45) / 34f);
            float down = y / (float)size;
            float shade = 1 - 0.18f * toSpine - 0.12f * down;
            (float r, float g, float b) parchment = (196 * shade, 172 * shade, 132 * shade);
            (float r, float g, float b) ink = (58, 40, 28);
            (float r, float g, float b) leather = (112 * (1 - 0.2f * down), 62 * (1 - 0.2f * down), 44 * (1 - 0.2f * down));

            // Composite: shadow, then page over it, then text on the page, then the spine.
            float a = sh;
            (float r, float g, float b) c = (0, 0, 0);
            c = Lerp(c, parchment, pg);
            c = Lerp(c, ink, tx);
            c = Lerp(c, leather, sp);

            bgra[i * 4 + 0] = (byte)Math.Clamp(Math.Round(c.b), 0, 255);
            bgra[i * 4 + 1] = (byte)Math.Clamp(Math.Round(c.g), 0, 255);
            bgra[i * 4 + 2] = (byte)Math.Clamp(Math.Round(c.r), 0, 255);
            bgra[i * 4 + 3] = (byte)Math.Round(a * 255);
        }

        return bgra;
    }

    private static (float r, float g, float b) Lerp((float r, float g, float b) a, (float r, float g, float b) b, float t)
        => (a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t);

    /// <summary>Point-in-convex-quad, vertices in order.</summary>
    private static bool Inside((float x, float y)[] q, float px, float py)
    {
        bool? sign = null;
        for (int i = 0; i < q.Length; i++)
        {
            var (ax, ay) = q[i];
            var (bx, by) = q[(i + 1) % q.Length];
            float cross = (bx - ax) * (py - ay) - (by - ay) * (px - ax);
            bool s = cross >= 0;
            if (sign is null) sign = s;
            else if (sign != s) return false;
        }
        return true;
    }
}
