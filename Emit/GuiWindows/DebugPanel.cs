using Ck3MapGen.GameGui;
using Ck3MapGen.Io;

namespace Ck3MapGen.Emit;

/// <summary>
/// The debug panel: what this map was made of, what the game made of it, and the levers for
/// telling the two apart.
///
/// The fourth authored window, and the first that exists for the person building the generator
/// rather than the person playing the map. Everything it reports is something no other surface in
/// the game can answer, because the questions are about the *generation*: what seed produced this
/// world, how many faiths were drawn, whether the wilderness system shipped at all — and then,
/// beside each of those, what the running game actually has.
///
/// <code>
/// Generated files (all five, like WonderIndex — every one of them varies with the world):
///   gui/gen_debug_panel.gui                                     the window
///   gui/scripted_widgets/gen_debug_panel.txt                    the registry entry
///   common/decisions/00_gen_debug_panel_decision.txt            the way in; debug_only
///   common/scripted_guis/00_gen_debug_panel_guis.txt            open state, gather, and the tools
///   localization/english/gen_debug_panel_l_english.yml          the prose
/// </code>
///
/// **The two columns are the whole point.** A generated fact and a live count sitting on the same
/// row is a consistency check that costs nothing to read: 1,412 counties written and 1,412 counties
/// in the world means the title emitter and the history emitter agree. 1,412 and 1,390 means they
/// do not, and no error was logged about it. That comparison is the reason this reports numbers the
/// generator already printed to <c>proctool.txt</c> — the log says what was *written*, and only the
/// running game says what was *loaded*.
///
/// Only numbers, versions and timestamps go in as <c>raw_text</c>. Every fixed WORD — the row
/// labels, and values like "on" or "written" — goes through a localisation key, because
/// <c>raw_text</c> is not the literal it looks like: see <see cref="LabelSet"/> for the row that
/// rendered itself as "County Titles".
///
/// Shape follows <see cref="ArtifactIndex"/> and <see cref="WonderIndex"/>: a zero-sized host for
/// the registry to instantiate, the real window inside it, a decision that sets a character
/// variable and a scripted_gui that owns both directions of the open state. What is new here is the
/// tab strip — <c>GetVariableSystem.Set</c>/<c>HasValue</c>, which is vanilla's own pattern for
/// "which of these panels is showing" and the first use of it in a window this project writes.
/// </summary>
public static class DebugPanel
{
    // ===========================================================================================
    // Geometry
    // ===========================================================================================

    /// <summary>How wide a row of the panel is, and everything else follows from it.</summary>
    private const int RowWidth = 600;

    private const int RowHeight = 24;

    /// <summary>Where the value column starts. Labels are elided rather than allowed to run into it.</summary>
    private const int ValueLeft = 250;

    /// <summary>
    /// The live column, which sits right of the generated one so the two can be read across.
    ///
    /// Only some rows have one — a seed has no runtime counterpart — and a row without it simply
    /// leaves the column empty rather than shifting anything.
    /// </summary>
    private const int LiveLeft = 430;

    /// <summary>
    /// Wide enough for a row plus the window's own furniture.
    ///
    /// 150 is the measured chrome figure, not the 128 the arithmetic gives — see
    /// <see cref="WonderIndex"/> for where the missing twenty pixels went. The 20 on top of it is
    /// slack, because landing exactly on the boundary fails silently and one-sidedly.
    /// </summary>
    private const int WindowWidth = RowWidth + 150 + 20;

    private const int WindowHeight = 720;

    /// <summary>
    /// The UI variable holding which tab is showing, and the three values it takes.
    ///
    /// A GUI variable rather than a character one, unlike the window's open state. The distinction
    /// is which side needs to write it: the open state is set by a DECISION and script cannot reach
    /// the GUI layer, while the tab is set by a button and never leaves it.
    /// </summary>
    private const string TabVariable = "gen_debug_tab";

    private const string WorldTab = "world";
    private const string RealmTab = "realm";
    private const string ToolsTab = "tools";
    private const string EventsTab = "events";

    // ===========================================================================================
    // What the generator knows
    // ===========================================================================================

    /// <summary>
    /// Everything the panel bakes in: the facts that are settled when the map is written and can
    /// never be recovered from the running game.
    ///
    /// Every member has a default, so a caller that cannot reach a number leaves it alone rather
    /// than having to invent one — a zero next to a live count still reads correctly as "the
    /// generator did not make any of these", which is usually the truth.
    /// </summary>
    public sealed record Facts
    {
        public string ModName { get; init; } = "";
        public string ToolVersion { get; init; } = "";
        public string Generated { get; init; } = "";
        public int Seed { get; init; }
        public int StartYear { get; init; }

        public int Width { get; init; }
        public int Height { get; init; }
        public int LandProvinces { get; init; }
        public int WaterProvinces { get; init; }
        public int Rivers { get; init; }
        public int Baronies { get; init; }

        public int Empires { get; init; }
        public int Kingdoms { get; init; }
        public int Duchies { get; init; }
        public int Counties { get; init; }

        /// <summary>
        /// How many of <see cref="Duchies"/> hold no de jure land — the head-of-faith titles.
        ///
        /// Its live counterpart also counts every holy order and mercenary company the game creates
        /// for itself at start, which is why the row exists: without it those show up as duchies
        /// this generator failed to write.
        /// </summary>
        public int LandlessDuchies { get; init; }

        public int Cultures { get; init; }
        public int Heritages { get; init; }
        public int Faiths { get; init; }
        public int Religions { get; init; }

        public int Wonders { get; init; }
        public int WildernessCounties { get; init; }
        public int Artifacts { get; init; }
        public int Struggles { get; init; }
        public int MenAtArms { get; init; }

        /// <summary>Where the world's shape came from, as a sentence rather than a flag.</summary>
        public string Source { get; init; } = "procedural";

        public string Races { get; init; } = "human only";
        public bool Wilderness { get; init; }
        public bool Magic { get; init; }
        public bool Retinues { get; init; }
        public bool History { get; init; }

        /// <summary>
        /// Whether the wonder index shipped. It does not on a map with no world centers, and a
        /// button opening a window that was never written is a button that does nothing.
        /// </summary>
        public bool HasWonderIndex { get; init; }
    }

    // ===========================================================================================
    // Entry point
    // ===========================================================================================

    public static void Write(string modDir, Facts facts)
    {
        // The labels are collected while the window is built rather than listed separately, so a
        // row cannot be added without its localisation key coming with it.
        var labels = new LabelSet();

        // Read off the finished folder, so both the generated events and everything
        // StaticFileWriter copied out of BaseFilesToCopy are covered. This is why DebugPanel is
        // written last -- see the call site in ContentWriter.
        var events = ShippedEvents.Scan(modDir);

        Console.WriteLine($"  debug panel: {events.Count} shipped event(s) listed");

        WriteWindow(modDir, facts, labels, events);
        WriteScriptedGuis(modDir, facts, events);
        WriteDecision(modDir);
        WriteLocalisation(modDir, labels, events);
    }

    /// <summary>
    /// Every row label, as a localisation key rather than a literal.
    ///
    /// This exists because of a trap that <c>raw_text</c>'s own documentation denies:
    /// <b><c>raw_text</c> is not literal.</b> A string that exactly matches a localisation key is
    /// resolved like any other. Vanilla ships keys named <c>counties</c>, <c>baronies</c> and
    /// <c>cultures</c>, so the first build of this panel rendered its own row labels as "County
    /// Titles", "Baronies" and "Cultures" — the right idea, the wrong words, and no way to tell
    /// from the file that it would happen. Neither the preview nor ck3-tiger can see it; the only
    /// oracle was a screenshot.
    ///
    /// So every fixed string this window shows now goes through a key of its own, in a namespace
    /// nothing else uses. What stays in <c>raw_text</c> is only what cannot be known ahead of time
    /// — numbers, a version, a timestamp — and a bare integer is not a localisation key in any
    /// language file the game ships.
    /// </summary>
    private sealed class LabelSet
    {
        private readonly Dictionary<string, string> _byKey = [];

        /// <summary>Registers a label and hands back the key the <c>.gui</c> should name.</summary>
        public string Key(string text)
        {
            string key = "GEN_DEBUG_L_" + new string([.. text.Select(
                c => char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : '_')]);

            // Same text twice is the same key, which is what makes "empires" on two tabs one entry
            // rather than a silent overwrite of one wording by another.
            _byKey[key] = text;
            return key;
        }

        public void WriteInto(LocFile loc)
        {
            foreach (var (key, text) in _byKey.OrderBy(e => e.Key, StringComparer.Ordinal))
                loc.Add(key, text);
        }
    }

    // ===========================================================================================
    // The window
    // ===========================================================================================

    private static void WriteWindow(string modDir, Facts facts, LabelSet labels,
        List<ShippedEvents.Entry> events)
    {
        var doc = GuiDocument.Create("debug panel", "gui", "gen_debug_panel.gui");

        var player = GuiScope.Root("GetPlayer");
        var window = new ScriptedGui("gen_debug_panel_window", player);
        var gather = new ScriptedGui("gen_debug_panel_gather", player);

        doc.Add(GuiBuilder.Types("gen_debug_panel").Add(

            GuiBuilder.Type("gen_debug_panel_host", "window")
                .Name("gen_debug_panel_host")
                .AllowOutside()
                .ParentAnchor("center")
                .Size(0, 0)
                // The host is always instantiated, so it carries the conditions under which no
                // custom window should be on screen at all -- plus, here, the one condition the
                // other three windows have no reason to ask.
                //
                // InDebugMode is belt and braces. The decision is already `debug_only`, so a
                // release player cannot open the window in the first place; this makes it true that
                // the window cannot be on screen outside debug mode even if some future surface
                // sets the flag another way.
                .Gap().Visible(GuiExpr.And(
                    GuiExpr.Raw("InDebugMode"),
                    GuiExpr.Raw("Not( IsPauseMenuShown )"),
                    GuiExpr.Raw("Or( Not( IsObserver ), GetPlayer.IsValid )"),
                    GuiExpr.Raw("IsDefaultGUIMode")))
                .Gap().Add(GuiBuilder.Of("gen_debug_panel_window")),

            GuiBuilder.Type("gen_debug_panel_window", "window")
                .Gapped()
                .Name("gen_debug_panel_window")
                .AllowOutside()
                .Movable()
                .ParentAnchor("center")
                .Position(0, -40)
                .Size(WindowWidth, WindowHeight)
                .Using("Window_Background", "Window_Decoration_Spike")
                .Gap().Visible(window.IsShown())

                // Two things on show, and the order does not matter because they touch nothing in
                // common. The gather fills the live column; the Set picks the tab.
                //
                // The tab is reset on every open rather than remembered. For a debug panel that is
                // the right default: the first thing you want after opening it is the summary, and
                // a window that reopens on whichever tab you last poked a button from is a window
                // that looks broken the first time it happens.
                .Gap().Add(GuiBuilder.State("_show")
                    .Using("Animation_FadeIn_Quick", "Sound_WindowShow_Standard")
                    .Quoted("on_start", gather.Execute().ToString())
                    .Quoted("on_start", GuiExpr.VariableSet(TabVariable, WorldTab).ToString()))

                .Gap().Add(GuiBuilder.State("_hide")
                    .Using("Animation_FadeOut_Quick", "Sound_WindowHide_Standard"))

                .Gap().Add(GuiBuilder.VBox()
                    .Using("Window_Margins")

                    .Gap().Add(GuiBuilder.Of("header_standard")
                        .ExpandingH()
                        .Gap().Add(GuiBuilder.BlockOverride("header_text")
                            .Text("GEN_DEBUG_PANEL_TITLE"))
                        .Gap().Add(GuiBuilder.BlockOverride("button_close")
                            .DataContext(GuiExpr.Raw("GetScriptedGui( 'gen_debug_panel_window' )"))
                            .OnClick(GuiExpr.Raw($"ScriptedGui.Execute( {player} )"))))

                    .Gap().Add(Tabs())

                    .Gap().Add(GuiBuilder.ScrollBox()
                        .Expanding()
                        .Gap().Add(GuiBuilder.BlockOverride("scrollbox_content")
                            .Add(GuiBuilder.VBox()
                                .ExpandingH()
                                // Without this the two hidden tabs still reserve their height and
                                // the visible one starts two screens down. A hidden widget is not
                                // a widget of no size unless the parent is told to skip it.
                                .IgnoreInvisible()
                                .Gap().Add(WorldPanel(facts, labels))
                                .Gap().Add(RealmPanel(facts, labels))
                                .Gap().Add(ToolsPanel(facts))
                                .Gap().Add(EventsPanel(events))))))));

        // The bare instantiation the registry resolves. Without it the file loads clean and then
        // "Could not find widget 'gen_debug_panel_host'", with nothing else to distinguish that
        // from a visibility gate that is simply false.
        doc.Add(GuiBuilder.Of("gen_debug_panel_host"));
        doc.Ship(modDir);

        string registry = Path.Combine(modDir, "gui", "scripted_widgets");
        Directory.CreateDirectory(registry);

        ParadoxText.WriteNoBom(
            Path.Combine(registry, "gen_debug_panel.txt"),
            "# Instantiates the debug panel. Written by Emit/GuiWindows/DebugPanel.cs.\n"
            + "#\n"
            + "# Names the HOST type, not the window itself, for the reason spelled out in\n"
            + "# gui/scripted_widgets/gen_artifact_index.txt.\n"
            + "gui/gen_debug_panel.gui = gen_debug_panel_host\n");
    }

    /// <summary>
    /// The tab strip.
    ///
    /// Vanilla's pattern, copied whole: <c>Set</c> on the click, <c>HasValue</c> on both
    /// <c>down</c> (so the current tab looks pressed) and <c>alwaystransparent</c> (so it cannot be
    /// clicked again). All three read the same variable, which is what makes the tabs exclusive
    /// without anything having to clear the others.
    /// </summary>
    private static GuiBuilder Tabs()
    {
        return GuiBuilder.HBox()
            .ExpandingH()
            .Align("left")
            .Spacing(6)
            .MarginBottom(6)
            .Add(Tab(WorldTab, "GEN_DEBUG_PANEL_TAB_WORLD"),
                 Tab(RealmTab, "GEN_DEBUG_PANEL_TAB_REALM"),
                 Tab(ToolsTab, "GEN_DEBUG_PANEL_TAB_TOOLS"),
                 Tab(EventsTab, "GEN_DEBUG_PANEL_TAB_EVENTS"),
                 GuiBuilder.Expand());
    }

    private static GuiBuilder Tab(string value, string label)
    {
        var selected = GuiExpr.VariableHasValue(TabVariable, value);

        return GuiBuilder.Of("button_standard")
            .Size(150, 30)
            .Text(label)
            .OnClick(GuiExpr.VariableSet(TabVariable, value))
            .Down(selected)
            .AlwaysTransparent(selected);
    }

    // ===========================================================================================
    // Tab one: what the generator wrote
    // ===========================================================================================

    private static GuiBuilder WorldPanel(Facts facts, LabelSet labels)
    {
        return Panel(WorldTab)

            .Gap().Add(Heading("GEN_DEBUG_PANEL_HEAD_RUN"))
            .Add(Row(labels, "mod", facts.ModName),
                 Row(labels, "tool version", facts.ToolVersion),
                 Row(labels, "generated", facts.Generated),
                 Row(labels, "seed", $"{facts.Seed}"),
                 Word(labels, "source", facts.Source),
                 Row(labels, "start year", $"{facts.StartYear}"))

            .Gap().Add(Heading("GEN_DEBUG_PANEL_HEAD_MAP"))
            .Add(Row(labels, "heightmap", $"{facts.Width} x {facts.Height} px"),
                 Row(labels, "land provinces", $"{facts.LandProvinces}"),
                 Row(labels, "sea provinces", $"{facts.WaterProvinces}"),
                 Row(labels, "river provinces", $"{facts.Rivers}"),
                 Row(labels, "baronies", $"{facts.Baronies}"))

            // The four rows the panel exists for. Each names a live counter beside it, and the
            // gather fills those from the world the game actually loaded -- so a mismatch on any
            // of these four lines is a title or history emitter that dropped something quietly.
            .Gap().Add(Heading("GEN_DEBUG_PANEL_HEAD_TITLES"))
            .Add(Row(labels, "empires", $"{facts.Empires}", Counter("empires")),
                 Row(labels, "kingdoms", $"{facts.Kingdoms}", Counter("kingdoms")),
                 Row(labels, "duchies", $"{facts.Duchies}", Counter("duchies")),
                 Row(labels, "of those, landless", $"{facts.LandlessDuchies}", Counter("landless")),
                 Row(labels, "counties", $"{facts.Counties}", Counter("counties")))

            .Gap().Add(Note("GEN_DEBUG_PANEL_TITLES_NOTE"))

            .Gap().Add(Heading("GEN_DEBUG_PANEL_HEAD_PEOPLES"))
            .Add(Row(labels, "cultures", $"{facts.Cultures}", Counter("cultures")),
                 Row(labels, "heritages", $"{facts.Heritages}"),
                 Row(labels, "faiths", $"{facts.Faiths}", Counter("faiths")),
                 Row(labels, "religions", $"{facts.Religions}", Counter("religions")))

            // These three rows are NOT a check, and saying so is the whole point of the note.
            //
            // The map replaces vanilla's TITLES -- descriptor.mod carries
            // replace_path="common/landed_titles" -- but it does not replace vanilla's peoples. It
            // drops one file of its own into common/culture/cultures and overrides nothing, so all
            // 244 vanilla cultures are still in the database beside the generated ones. Measured on
            // 2026-08-25: 244 + 34 generated = 278, which was exactly what the live column read.
            //
            // Without the note that row looks like 34 cultures written and 278 loaded, which reads
            // as a catastrophic bug and is in fact the intended design. A number that needs a
            // paragraph of context is worse than no number unless the context is next to it.
            .Gap().Add(Note("GEN_DEBUG_PANEL_PEOPLES_NOTE"))

            .Gap().Add(Heading("GEN_DEBUG_PANEL_HEAD_FEATURES"))
            .Add(Row(labels, "wonders", $"{facts.Wonders}"),
                 // Live counterpart is counties with no holder, which is what wilderness IS at
                 // runtime -- so this row also reads as "how much has been colonised since".
                 Row(labels, "wilderness counties", $"{facts.WildernessCounties}", Counter("wilderness")),
                 Row(labels, "artifacts placed", $"{facts.Artifacts}", Counter("artifacts")),
                 Row(labels, "struggles", $"{facts.Struggles}"),
                 Row(labels, "men-at-arms types", $"{facts.MenAtArms}"),
                 Word(labels, "races", facts.Races),
                 Word(labels, "magic", OnOff(facts.Magic)),
                 Word(labels, "generated retinues", OnOff(facts.Retinues)),
                 Word(labels, "wilderness system", OnOff(facts.Wilderness)),
                 Word(labels, "history", facts.History ? "written" : "not written"));
    }

    // ===========================================================================================
    // Tab two: what the running game has
    // ===========================================================================================

    /// <summary>
    /// The player, and the world as the game loaded it.
    ///
    /// Straight datafunctions for the player's own state — those are cheap and always current —
    /// and gathered variables for anything that needs a list walked. The split is not stylistic: a
    /// <c>.gui</c> re-evaluates its text every frame, so <c>every_county</c> behind a label would
    /// walk fifteen hundred counties sixty times a second. The gather does it once, when the
    /// window appears.
    /// </summary>
    private static GuiBuilder RealmPanel(Facts facts, LabelSet labels)
    {
        return Panel(RealmTab)

            .Gap().Add(Heading("GEN_DEBUG_PANEL_HEAD_PLAYER"))
            .Add(Row(labels, "name", GuiExpr.Raw("GetPlayer.GetNameNoTooltip")),
                 Row(labels, "id", GuiExpr.Raw("GetPlayer.GetID")),
                 Row(labels, "primary title", GuiExpr.Raw("GetPlayer.GetPrimaryTitle.GetNameNoTooltip")),
                 Row(labels, "culture", GuiExpr.Raw("GetPlayer.GetCulture.GetName")),
                 Row(labels, "faith", GuiExpr.Raw("GetPlayer.GetFaith.GetName")),
                 // GetNameNoTooltip, not GetName. A government has no GetName -- vanilla writes
                 // this spelling and never the short one, and the wrong name would have resolved
                 // to a blank line with nothing logged and ck3-tiger passing it. Caught by the
                 // preview's "calls vanilla never makes" report, which is the only check in the
                 // toolchain that distinguishes a wrong datafunction from a right one.
                 Row(labels, "government", GuiExpr.Raw("GetPlayer.GetGovernment.GetNameNoTooltip")),
                 Row(labels, "gold", GuiExpr.Raw("GetPlayer.GetGold|0")))

            .Gap().Add(Heading("GEN_DEBUG_PANEL_HEAD_LIVE"))
            .Add(Row(labels, "counties held", Counter("held")),
                 Row(labels, "vassals", Counter("vassals")),
                 Row(labels, "rulers alive", Counter("rulers")),
                 Row(labels, "independent rulers", Counter("independent")),
                 Row(labels, "counties with no holder", Counter("wilderness")),
                 Row(labels, "artifacts in the world", Counter("artifacts")))

            .Gap().Add(Heading("GEN_DEBUG_PANEL_HEAD_MINE"))
            .Add(GuiBuilder.Of("text_multi")
                .ExpandingH()
                .AutoResize()
                .MaxWidth(RowWidth)
                .Text("GEN_DEBUG_PANEL_LIVE_NOTE"))

            // Only worth saying on a map that shipped the system. On one that did not, the row
            // above it already reads zero and a note explaining wilderness would be noise.
            .Gap().Add(facts.Wilderness
                ? GuiBuilder.Of("text_multi")
                    .ExpandingH()
                    .AutoResize()
                    .MaxWidth(RowWidth)
                    .Format("#weak")
                    .Text("GEN_DEBUG_PANEL_WILDERNESS_NOTE")
                : GuiBuilder.Of("widget").Size(0, 0));
    }

    // ===========================================================================================
    // Tab three: the levers
    // ===========================================================================================

    private static GuiBuilder ToolsPanel(Facts facts)
    {
        var player = GuiScope.Root("GetPlayer");

        var panel = Panel(ToolsTab)

            .Gap().Add(Heading("GEN_DEBUG_PANEL_HEAD_INSPECT"))
            .Add(Action("gen_debug_panel_gather", player,
                     "GEN_DEBUG_PANEL_REFRESH", "GEN_DEBUG_PANEL_REFRESH_TT"),
                 Action("gen_debug_panel_log", player,
                     "GEN_DEBUG_PANEL_LOG", "GEN_DEBUG_PANEL_LOG_TT"))

            .Gap().Add(Heading("GEN_DEBUG_PANEL_HEAD_RESOURCES"))
            .Add(Action("gen_debug_panel_gold", player,
                     "GEN_DEBUG_PANEL_GOLD", "GEN_DEBUG_PANEL_GOLD_TT"),
                 Action("gen_debug_panel_prestige", player,
                     "GEN_DEBUG_PANEL_PRESTIGE", "GEN_DEBUG_PANEL_PRESTIGE_TT"),
                 Action("gen_debug_panel_piety", player,
                     "GEN_DEBUG_PANEL_PIETY", "GEN_DEBUG_PANEL_PIETY_TT"));

        // The sibling windows. Written as buttons here rather than left to the decisions panel
        // because the decisions panel is a long list and this is where you already are.
        panel.Gap().Add(Heading("GEN_DEBUG_PANEL_HEAD_WINDOWS"));

        panel.Add(Action("gen_debug_panel_open_artifacts", player,
            "GEN_DEBUG_PANEL_ARTIFACTS", "GEN_DEBUG_PANEL_ARTIFACTS_TT"));

        // Conditional at GENERATION time, not at runtime. A map with no world centers has no
        // wonder index -- no window, no decision, no scripted_gui -- so the button is not written
        // at all rather than written and disabled. There is nothing for it to be disabled about.
        if (facts.HasWonderIndex)
            panel.Add(Action("gen_debug_panel_open_wonders", player,
                "GEN_DEBUG_PANEL_WONDERS", "GEN_DEBUG_PANEL_WONDERS_TT"));

        return panel;
    }

    /// <summary>
    /// Every event this mod ships, one button each, grouped by the file it came from.
    ///
    /// The buttons are BAKED — one widget per event, and one scripted_gui behind each — for the
    /// same reason the wonder index bakes its rows: a <c>.gui</c> cannot name an event. There is no
    /// datafunction that turns a string into an event id, so a datamodel of event names could be
    /// listed but not fired, and the one thing this tab exists to do is fire them. Baking is not the
    /// cheap option here, it is the only one.
    ///
    /// It scales fine. 57 events is 57 four-line scripted_guis, which is a 12KB file the game reads
    /// once, and the count only moves when somebody adds an event.
    ///
    /// <b>Pressing a button is not a promise that the event appears.</b> <c>trigger_event</c>
    /// evaluates the event's own <c>trigger</c> block, so an event that wants a colony underway or
    /// a scope it has not been given does nothing at all — no window, no log line. Six of the events
    /// here are <c>hidden = yes</c> as well, and those never show a window even when they do fire.
    /// Both are called out in the note and in each button's tooltip, because otherwise the first
    /// silent button reads as a bug in this panel.
    /// </summary>
    private static GuiBuilder EventsPanel(List<ShippedEvents.Entry> events)
    {
        var player = GuiScope.Root("GetPlayer");
        var panel = Panel(EventsTab);

        if (events.Count == 0)
        {
            // A map generated with every optional system switched off really does ship no events.
            // An empty tab with a line saying so beats an empty tab.
            panel.Gap().Add(Note("GEN_DEBUG_PANEL_EVENTS_NONE"));
            return panel;
        }

        panel.Gap().Add(Note("GEN_DEBUG_PANEL_EVENTS_NOTE"));

        // Grouped by file, in the order the scan found them, which is the order they sit in on
        // disk. Feature by feature, in other words -- all the colonisation events together.
        foreach (var group in events.GroupBy(e => e.File))
        {
            panel.Gap().Add(Heading(FileHeadingKey(group.Key)));

            foreach (var entry in group)
                panel.Add(entry.CanFire
                    ? FireButton(entry, player)
                    : UnfirableEvent(entry));
        }

        return panel;
    }

    /// <summary>
    /// One event's button.
    ///
    /// Both strings are localisation keys rather than <c>raw_text</c>, and the label one is not
    /// paranoia: an event id contains a dot and could not collide with a vanilla key, but routing it
    /// through <see cref="LocFile"/> anyway is what lets the tooltip carry the type, the file and
    /// the caveat without any of it being assembled in the <c>.gui</c>.
    /// </summary>
    private static GuiBuilder FireButton(ShippedEvents.Entry entry, GuiScope scope)
        => GuiBuilder.Of("button_standard")
            .Size(360, 28)
            .MarginBottom(2)
            .Text(EventLabelKey(entry))
            .Tooltip(EventTooltipKey(entry))
            .Runs(new ScriptedGui(FireKey(entry), scope));

    /// <summary>
    /// An event the panel lists but will not fire: a row, not a button.
    ///
    /// Deliberately not a disabled button. A greyed button says "this might work later"; this will
    /// never work from here, because the panel has no way to produce the scope the event runs in.
    /// Saying which scope that is turns a dead entry into the answer to "why isn't this here".
    /// </summary>
    private static GuiBuilder UnfirableEvent(ShippedEvents.Entry entry)
        => GuiBuilder.Of("text_single")
            .Size(360, 28)
            .MarginBottom(2)
            .MarginLeft(6)
            .Format("#weak")
            .Text(EventLabelKey(entry))
            .Tooltip(EventTooltipKey(entry));

    /// <summary>The scripted_gui that fires one event. Named off the id so it cannot collide.</summary>
    private static string FireKey(ShippedEvents.Entry entry) => $"gen_dbg_fire_{entry.Key}";

    private static string EventLabelKey(ShippedEvents.Entry entry) => $"GEN_DEBUG_EV_{entry.Key}";

    private static string EventTooltipKey(ShippedEvents.Entry entry) => $"GEN_DEBUG_EV_{entry.Key}_TT";

    /// <summary>
    /// A readable name for an events file: <c>wilderness_colonization_events.txt</c> reads as
    /// "Wilderness colonization".
    ///
    /// Cosmetic, and deliberately dumb. The filenames this project ships are already descriptive,
    /// so trimming the parts every one of them repeats gets a good heading without a lookup table
    /// that a new file would not be in.
    /// </summary>
    private static string FileHeading(string file)
    {
        string stem = Path.GetFileNameWithoutExtension(file);

        // Anchored. An unanchored Replace strips the noise wherever it appears, which turned
        // `zzz_scan_probe` into "Zscan probe" -- the trailing z of the load-order prefix eaten out
        // of the middle of a word. Same trap waiting in any name containing "gen" or "events".
        foreach (string prefix in new[] { "zz_", "gen_" })
            if (stem.StartsWith(prefix, StringComparison.Ordinal))
                stem = stem[prefix.Length..];

        if (stem.EndsWith("_events", StringComparison.Ordinal))
            stem = stem[..^"_events".Length];

        stem = stem.Replace('_', ' ').Trim();

        return stem.Length == 0 ? file : char.ToUpperInvariant(stem[0]) + stem[1..];
    }

    /// <summary>A heading key per source file, derived so the loc writer can rebuild it.</summary>
    private static string FileHeadingKey(string file)
        => "GEN_DEBUG_EVFILE_" + new string([.. Path.GetFileNameWithoutExtension(file)
            .Select(c => char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : '_')]);

    /// <summary>
    /// One button that runs one scripted_gui.
    ///
    /// All four of visible/enabled/tooltip/onclick would come off a single <see cref="ScriptedGui"/>
    /// if any of these needed gating, which is the point of that type. None do — every tool here is
    /// always valid for a player in debug mode — so this binds the click and leaves the rest.
    /// </summary>
    private static GuiBuilder Action(string key, GuiScope scope, string label, string tooltip)
    {
        return GuiBuilder.Of("button_standard")
            .Size(280, 32)
            .MarginBottom(4)
            .Text(label)
            .Tooltip(tooltip)
            .Runs(new ScriptedGui(key, scope));
    }

    // ===========================================================================================
    // Rows
    // ===========================================================================================

    /// <summary>
    /// One tab's contents, hidden unless it is the tab showing.
    ///
    /// A <c>vbox</c> is right here where it was wrong for the rows below: this one is *supposed* to
    /// take its height from its children, and it is not inside a lattice slot trying to tell it
    /// otherwise.
    /// </summary>
    private static GuiBuilder Panel(string tab)
        => GuiBuilder.VBox()
            .ExpandingH()
            .Align("left")
            .Spacing(2)
            .Visible(GuiExpr.VariableHasValue(TabVariable, tab));

    /// <summary>A wrapped paragraph under a section: what a number means, where it is not obvious.</summary>
    private static GuiBuilder Note(string key)
        => GuiBuilder.Of("text_multi")
            .ExpandingH()
            .AutoResize()
            .MaxWidth(RowWidth)
            .MarginBottom(4)
            .Format("#weak")
            .Text(key);

    private static GuiBuilder Heading(string key)
        => GuiBuilder.VBox()
            .ExpandingH()
            .Align("left")
            .Spacing(2)
            .MarginBottom(4)
            .Add(GuiBuilder.TextSingle()
                    .ExpandingH()
                    .Align("left")
                    .Using("Font_Size_Medium")
                    .Format("#high")
                    .Text(key),
                 GuiBuilder.Of("divider_light")
                    .ExpandingH());

    /// <summary>A row whose middle column is a fact baked in when the map was written.</summary>
    private static GuiBuilder Row(LabelSet labels, string label, string generated, GuiExpr? live = null)
        => Line(labels.Key(label), Escape(generated), live);

    /// <summary>
    /// A row whose middle column is a datafunction rather than a baked string — the live tab's only
    /// shape, since nothing there is known when the map is written.
    /// </summary>
    private static GuiBuilder Row(LabelSet labels, string label, GuiExpr value)
        => Line(labels.Key(label), value.ToString(), null);

    /// <summary>
    /// A row whose value is one of a fixed set of words rather than a number.
    ///
    /// Separate from the ordinary string row because these carry the same hazard the labels do —
    /// "on", "off" and "written" are exactly the kind of short word a language file already has a
    /// key for — so the value goes through <see cref="LabelSet"/> as well. Numbers, versions and
    /// timestamps do not need this and stay literal.
    /// </summary>
    private static GuiBuilder Word(LabelSet labels, string label, string value)
        => Line(labels.Key(label), labels.Key(value), null, valueIsKey: true);

    /// <summary>
    /// The row itself: a label, what the generator wrote, and what the game has.
    ///
    /// A <c>widget</c> with the three columns placed by <c>position</c>, not an <c>hbox</c>. A
    /// stacking box inside a list does not reliably keep a size it is given, and a row that
    /// silently takes a different height than it asked for takes every row under it along with it.
    /// A widget decides nothing: it is a rectangle at a stated size, and its children sit where
    /// they are told.
    ///
    /// <paramref name="value"/> is already the finished string — a number, a word, or a bracketed
    /// datafunction — because <c>raw_text</c> makes no distinction between them and neither should
    /// this.
    /// </summary>
    private static GuiBuilder Line(string labelKey, string value, GuiExpr? live,
        bool valueIsKey = false)
    {
        var row = GuiBuilder.Widget()
            .Size(RowWidth, RowHeight)

            .Gap().Add(GuiBuilder.TextSingle()
                .Position(0, 0)
                .MaxWidth(ValueLeft - 10)
                .Elide("right")
                .Format("#weak")
                // `text`, not `raw_text`: see LabelSet for the word this used to render as.
                .Text(labelKey));

        // Baked values go in as literal text. `raw_text` is not localised, so a number needs no
        // loc key of its own -- which is the difference between a fact list that is cheap to extend
        // and one where every new line costs an entry in a .yml.
        row.Gap().Add(GuiBuilder.TextSingle()
            .Position(ValueLeft, 0)
            .MaxWidth((live is null ? RowWidth : LiveLeft) - ValueLeft - 10)
            .Elide("right")
            .Format("#high")
            .Add(valueIsKey ? GuiNode.Leaf("text", GuiNode.Quote(value))
                            : GuiNode.Leaf("raw_text", GuiNode.Quote(value))));

        if (live is not null)
            row.Gap().Add(GuiBuilder.TextSingle()
                .Position(LiveLeft, 0)
                .MaxWidth(RowWidth - LiveLeft)
                .Elide("right")
                // Weaker than the generated column on purpose. The generated number is the claim;
                // the live one is the check, and it should read as an annotation on the first
                // rather than as a second competing figure.
                .Format("#weak")
                .RawText($"live: {live}"));

        return row;
    }

    /// <summary>
    /// A counter the gather left on the player, read back the way vanilla reads variables.
    ///
    /// <c>MakeScope.Var</c>, not <c>GetGlobalVariable</c>. The latter type-checks, passes ck3-tiger
    /// and resolves to nothing in game; vanilla writes this spelling 117 times and the other one
    /// never. <c>|0</c> is the format suffix for a whole number, without which a count prints with
    /// the decimals a CFixedPoint carries.
    /// </summary>
    private static GuiExpr Counter(string name)
        => GuiExpr.Raw($"GetPlayer.MakeScope.Var('gen_dbg_{name}').GetValue|0");

    private static string OnOff(bool value) => value ? "on" : "off";

    /// <summary>
    /// A baked value going into a <c>.gui</c> as a quoted literal.
    ///
    /// Two characters have to go, and <see cref="GuiNode.Quote"/> escapes neither — it wraps the
    /// value and nothing more, which is right for everything else the builder writes and not for a
    /// string that came from a user. A double quote ends the value early and leaves the rest of the
    /// line as something the engine reports against the FILE rather than against the entry; a
    /// square bracket turns the rest of it into a datafunction, which resolves to nothing and says
    /// nothing about why. A mod folder is free to contain either.
    ///
    /// Dropped rather than escaped, because these are display strings in a debug window: a mod
    /// called <c>My "Test" Map</c> reading as <c>My Test Map</c> costs nothing.
    /// </summary>
    private static string Escape(string text)
        => text.Replace("\"", "").Replace("[", "").Replace("]", "");

    // ===========================================================================================
    // The script side
    // ===========================================================================================

    /// <summary>
    /// The open state, the gather, and one entry per button.
    ///
    /// Generated rather than kept in BaseFilesToCopy because two of them name things only the
    /// generator knows: the gather has to be told which culture the unsettled counties were given,
    /// and the wonder-index opener only exists on a map that has one.
    /// </summary>
    private static void WriteScriptedGuis(string modDir, Facts facts,
        List<ShippedEvents.Entry> events)
    {
        string dir = Path.Combine(modDir, "common", "scripted_guis");
        Directory.CreateDirectory(dir);

        string wonders = facts.HasWonderIndex
            ? """


              # Opens the wonder index from the tools tab. Written only on a map that has one --
              # a map with no world centers gets no wonder index at all, so on those this entry is
              # absent and so is the button that would have run it.
              gen_debug_panel_open_wonders = {
              	scope = character

              	is_shown = { always = yes }

              	effect = {
              		remove_variable = gen_debug_panel_open

              		set_variable = {
              			name = gen_wonder_index_open
              			value = yes
              		}
              	}
              }
              """
            : "";

        ParadoxText.WriteBom(Path.Combine(dir, "00_gen_debug_panel_guis.txt"),
            """
            # The debug panel's script half. Written by Emit/GuiWindows/DebugPanel.cs.
            #
            # Every entry here is named by gui/gen_debug_panel.gui, and neither side fails loudly:
            # a .gui naming a scripted_gui that does not exist logs nothing and evaluates false, so
            # a rename on either side produces a button that silently does nothing.


            # Is the panel open for this character, and close it.
            #
            # One entry answering both directions on purpose -- the window's `visible` asks IsShown,
            # the close button in its header runs Execute. Two entries could drift into disagreeing
            # about what "open" means, and the failure mode of that is a window with no way out.
            #
            # The open state is a character variable rather than a GUI VariableSystem flag because a
            # DECISION has to be able to set it, and a decision's effect cannot reach the GUI layer.
            # The TAB, which only buttons ever set, is a GUI variable and lives entirely in the .gui.
            gen_debug_panel_window = {
            	scope = character

            	is_shown = {
            		has_variable = gen_debug_panel_open
            	}

            	effect = {
            		remove_variable = gen_debug_panel_open
            	}
            }


            # Count the world, and leave the numbers on the player for the window to read.
            #
            # Run from the window's own `_show` state and from the Refresh button, never from the
            # decision that opens the panel -- so the figures are current whenever they are on
            # screen rather than current as of the last time the decisions panel was opened.
            #
            # Counted in script rather than in the .gui because a .gui re-evaluates its text every
            # frame. `every_county` behind a label would walk the whole world sixty times a second;
            # behind this it walks it once, when the window appears.
            #
            # The arithmetic itself is a scripted_effect, because a scripted_gui cannot call another
            # scripted_gui and the log button below needs the same walk. One copy of it means the
            # panel and the log cannot report different numbers for the same world.
            gen_debug_panel_gather = {
            	scope = character

            	is_shown = {
            		always = yes
            	}

            	effect = {
            		gen_debug_panel_gather_effect = yes
            	}
            }


            # Put the whole reckoning in game.log.
            #
            # `debug_log` takes a literal string and nothing else -- it does not interpolate, so
            # there is no way to write a count into one. `debug_log_scopes` dumps every saved scope
            # instead, which is why the counters are saved as scope VALUES first: the dump is then
            # the report, with each figure named.
            #
            # Runs the gather itself rather than trusting the variables to be current. The button
            # is on a tab you can reach without the window ever having refreshed.
            gen_debug_panel_log = {
            	scope = character

            	is_shown = {
            		always = yes
            	}

            	effect = {
            		gen_debug_panel_gather_effect = yes

            		save_scope_value_as = { name = gen_counties value = var:gen_dbg_counties }
            		save_scope_value_as = { name = gen_wilderness value = var:gen_dbg_wilderness }
            		save_scope_value_as = { name = gen_duchies value = var:gen_dbg_duchies }
            		save_scope_value_as = { name = gen_kingdoms value = var:gen_dbg_kingdoms }
            		save_scope_value_as = { name = gen_empires value = var:gen_dbg_empires }
            		save_scope_value_as = { name = gen_cultures value = var:gen_dbg_cultures }
            		save_scope_value_as = { name = gen_faiths value = var:gen_dbg_faiths }
            		save_scope_value_as = { name = gen_religions value = var:gen_dbg_religions }
            		save_scope_value_as = { name = gen_rulers value = var:gen_dbg_rulers }
            		save_scope_value_as = { name = gen_independent value = var:gen_dbg_independent }
            		save_scope_value_as = { name = gen_artifacts value = var:gen_dbg_artifacts }

            		debug_log = "=== generated world: counts follow as saved scopes ==="
            		debug_log_scopes = yes
            	}
            }


            # Testing money, and the two currencies that gate most generated content.
            #
            # Round numbers rather than a top-up to some target: what you want when testing a
            # decision that costs 500 prestige is to press the button until you have enough, and a
            # button that sets a level cannot be pressed twice.
            gen_debug_panel_gold = {
            	scope = character

            	is_shown = { always = yes }

            	effect = {
            		add_gold = 1000
            	}
            }

            gen_debug_panel_prestige = {
            	scope = character

            	is_shown = { always = yes }

            	effect = {
            		add_prestige = 1000
            	}
            }

            gen_debug_panel_piety = {
            	scope = character

            	is_shown = { always = yes }

            	effect = {
            		add_piety = 1000
            	}
            }


            # Opens the artifact index from the tools tab.
            #
            # Closes this panel on the way, because both windows anchor to the centre of the screen
            # and one would otherwise be sitting on top of the other.
            gen_debug_panel_open_artifacts = {
            	scope = character

            	is_shown = { always = yes }

            	effect = {
            		remove_variable = gen_debug_panel_open

            		set_variable = {
            			name = gen_artifact_index_open
            			value = yes
            		}
            	}
            }
            WONDERS

            """.Replace("WONDERS", wonders) + FireEntries(events));

        WriteGatherEffect(modDir);
    }

    /// <summary>
    /// One scripted_gui per shipped event, each firing exactly that event.
    ///
    /// The repetition is the mechanism, not an accident of code generation. A scripted_gui takes no
    /// arguments, and no datafunction turns a string into an event id, so "fire the event this
    /// button names" can only be expressed as one entry per event. Nothing here is shared between
    /// them and nothing needs to be.
    ///
    /// <c>is_shown = { always = yes }</c> throughout. Whether the event will actually do anything is
    /// the event's own <c>trigger</c> to decide, and second-guessing it here would mean copying
    /// every event's conditions into this file and keeping them in step forever. A button that
    /// fires into a failed trigger is honest; a button hidden by a stale copy of somebody else's
    /// trigger is not.
    /// </summary>
    private static string FireEntries(List<ShippedEvents.Entry> events)
    {
        if (events.Count == 0) return "";

        var b = new System.Text.StringBuilder();

        b.AppendLine().Append("""
            # ===========================================================================
            # One entry per shipped event, for the panel's Events tab.
            #
            # Generated from a scan of the mod's own events/ folder, so this list follows
            # whatever is actually there -- including anything dropped into
            # BaseFilesToCopy since the last time this file was looked at.
            # ===========================================================================

            """);

        foreach (var entry in events.Where(e => e.CanFire))
        {
            // A province event cannot be fired at a character. Everything this project ships today
            // is a character_event, so this branch is unexercised -- but it costs three lines and
            // the alternative is a button that silently does nothing the first time somebody adds
            // a province event.
            string fire = entry.IsProvinceEvent
                ? $"capital_province = {{ trigger_event = {entry.Id} }}"
                : $"trigger_event = {entry.Id}";

            // Plain concatenation rather than a raw interpolated literal. A `$"""…"""` would need
            // every one of the eight braces below doubled, and a script file that is mostly braces
            // is exactly where that stops being readable.
            b.Append('\n')
             .Append(FireKey(entry)).Append(" = {\n")
             .Append("\tscope = character\n\n")
             .Append("\tis_shown = { always = yes }\n\n")
             .Append("\teffect = {\n\t\t").Append(fire).Append("\n\t}\n")
             .Append("}\n");
        }

        return b.ToString();
    }

    /// <summary>
    /// The gather's body again, as a scripted EFFECT.
    ///
    /// It exists because a scripted_gui cannot call another scripted_gui. The log button needs the
    /// same counting the gather does, and the choice was between duplicating thirty lines of it or
    /// putting the body somewhere both can reach — which is what a scripted_effect is for.
    ///
    /// The gather above calls it too, so there is exactly one copy of the arithmetic and the panel
    /// and the log cannot report different numbers for the same world.
    /// </summary>
    private static void WriteGatherEffect(string modDir)
    {
        string dir = Path.Combine(modDir, "common", "scripted_effects");
        Directory.CreateDirectory(dir);

        ParadoxText.WriteBom(Path.Combine(dir, "00_gen_debug_panel_effects.txt"),
            """
            # The debug panel's counting, as an effect both its scripted_guis can call.
            # Written by Emit/GuiWindows/DebugPanel.cs.
            #
            # Every counter is SET to zero before its loop rather than cleared afterwards. A count
            # that were merely incremented would double on the second refresh, and the failure would
            # read as the world having grown rather than as a bug in this file.
            #
            # The root character is saved as a scope rather than reached with `root`, because the
            # loops below rescope and this effect is called from two places. A named scope means the
            # same line works wherever it is called from.

            gen_debug_panel_gather_effect = {
            	set_variable = { name = gen_dbg_empires value = 0 }
            	set_variable = { name = gen_dbg_kingdoms value = 0 }
            	set_variable = { name = gen_dbg_duchies value = 0 }
            	set_variable = { name = gen_dbg_counties value = 0 }
            	set_variable = { name = gen_dbg_wilderness value = 0 }
            	set_variable = { name = gen_dbg_cultures value = 0 }
            	set_variable = { name = gen_dbg_faiths value = 0 }
            	set_variable = { name = gen_dbg_religions value = 0 }
            	set_variable = { name = gen_dbg_rulers value = 0 }
            	set_variable = { name = gen_dbg_independent value = 0 }
            	set_variable = { name = gen_dbg_artifacts value = 0 }
            	set_variable = { name = gen_dbg_held value = 0 }
            	set_variable = { name = gen_dbg_vassals value = 0 }
            	set_variable = { name = gen_dbg_landless value = 0 }

            	save_scope_as = gen_dbg_root

            	# The world's counties, and how many of them nobody holds.
            	#
            	# "No holder" is what wilderness IS at runtime -- there is no flag on a county saying
            	# the generator left it empty -- so the second figure also reads as how much of the
            	# frontier is still open after however many years of play.
            	every_county = {
            		scope:gen_dbg_root = { change_variable = { name = gen_dbg_counties add = 1 } }

            		if = {
            			limit = { NOT = { exists = holder } }
            			scope:gen_dbg_root = { change_variable = { name = gen_dbg_wilderness add = 1 } }
            		}
            	}

            	# The tier walks all skip the vanilla shims.
            	#
            	# common/landed_titles/zz_vanilla_titulars.txt re-declares every vanilla e_/k_/d_/h_
            	# key as an empty titular title, so the ~12,900 hardcoded references in vanilla script
            	# still resolve. There are about 1,450 of them, and counting them here made the live
            	# column read 1288 duchies against 111 written -- a comparison that looks like a
            	# catastrophe and means nothing at all.
            	#
            	# gen_vanilla_titular is the variable CompatibilityWriter's own guard stamps on each
            	# shim at game start; reusing it means the two can never disagree about what a shim is.
            	every_duchy = {
            		limit = { NOT = { has_variable = gen_vanilla_titular } }
            		scope:gen_dbg_root = { change_variable = { name = gen_dbg_duchies add = 1 } }

            		# Landless duchies are not de jure land: the head-of-faith titles this generator
            		# writes, plus every holy order and mercenary company the game creates for itself
            		# at start. Broken out rather than filtered away, because "written 111, live 192"
            		# is a question and "of which 81 landless" is the answer to it.
            		if = {
            			limit = { is_landless_type_title = yes }
            			scope:gen_dbg_root = { change_variable = { name = gen_dbg_landless add = 1 } }
            		}
            	}

            	every_kingdom = {
            		limit = { NOT = { has_variable = gen_vanilla_titular } }
            		scope:gen_dbg_root = { change_variable = { name = gen_dbg_kingdoms add = 1 } }
            	}

            	every_empire = {
            		limit = { NOT = { has_variable = gen_vanilla_titular } }
            		scope:gen_dbg_root = { change_variable = { name = gen_dbg_empires add = 1 } }
            	}

            	every_culture_global = {
            		scope:gen_dbg_root = { change_variable = { name = gen_dbg_cultures add = 1 } }
            	}

            	every_religion_global = {
            		scope:gen_dbg_root = { change_variable = { name = gen_dbg_religions add = 1 } }

            		every_faith = {
            			scope:gen_dbg_root = { change_variable = { name = gen_dbg_faiths add = 1 } }
            		}
            	}

            	every_ruler = {
            		scope:gen_dbg_root = { change_variable = { name = gen_dbg_rulers add = 1 } }
            	}

            	every_independent_ruler = {
            		scope:gen_dbg_root = { change_variable = { name = gen_dbg_independent add = 1 } }
            	}

            	every_artifact = {
            		scope:gen_dbg_root = { change_variable = { name = gen_dbg_artifacts add = 1 } }
            	}

            	every_held_title = {
            		limit = { tier = tier_county }
            		scope:gen_dbg_root = { change_variable = { name = gen_dbg_held add = 1 } }
            	}

            	every_vassal = {
            		scope:gen_dbg_root = { change_variable = { name = gen_dbg_vassals add = 1 } }
            	}
            }

            """);
    }

    /// <summary>
    /// The way in.
    ///
    /// <c>debug_only = yes</c> is the gate, and <c>decision_group_type = debug</c> is what puts it
    /// in the decisions panel's own Debug group rather than among the decisions a player takes —
    /// both copied from vanilla's <c>common/decisions/test_decision.txt</c>, which is the reference
    /// for this whole shape.
    /// </summary>
    private static void WriteDecision(string modDir)
    {
        string dir = Path.Combine(modDir, "common", "decisions");
        Directory.CreateDirectory(dir);

        ParadoxText.WriteBom(Path.Combine(dir, "00_gen_debug_panel_decision.txt"),
            """
            # The front door to the debug panel. Written by Emit/GuiWindows/DebugPanel.cs.
            #
            # All this decision does is raise a flag. The window watches for it and fills itself
            # when it appears -- see gen_debug_panel_window and gen_debug_panel_gather in
            # common/scripted_guis/00_gen_debug_panel_guis.txt.
            #
            # It is a decision because a decision is the one surface a player can find without being
            # told it exists. There is no character interaction, no map click and no hotkey that
            # would lead anyone to a window like this.

            gen_debug_panel_decision = {
            	# Vanilla's own group for these, sort_order -1, so the panel sits below every
            	# decision a player would actually take rather than above them.
            	decision_group_type = debug

            	picture = {
            		reference = "gfx/interface/illustrations/decisions/decision_misc.dds"
            	}

            	desc = gen_debug_panel_decision_desc
            	selection_tooltip = gen_debug_panel_decision_tooltip
            	confirm_text = gen_debug_panel_decision_confirm

            	# The gate. Without -debug_mode on the launch options this decision does not exist,
            	# which is the whole reason the window can carry generator internals without
            	# worrying about what a player would make of them.
            	is_shown = {
            		debug_only = yes
            	}

            	is_valid = {
            		always = yes
            	}

            	effect = {
            		set_variable = {
            			name = gen_debug_panel_open
            			value = yes
            		}
            	}

            	# Never. It costs nothing, does nothing, and an AI ruler taking it would spend its
            	# yearly decision slot opening a window nobody is looking at.
            	ai_will_do = {
            		base = 0
            	}

            	ai_check_interval = 0
            }

            """);
    }

    /// <summary>
    /// The prose. Only what a person reads — every baked number goes in as <c>raw_text</c> and
    /// needs no key here.
    /// </summary>
    private static void WriteLocalisation(string modDir, LabelSet labels,
        List<ShippedEvents.Entry> events)
    {
        var loc = new LocFile();

        labels.WriteInto(loc);

        // A heading per event file, titled from the filename with the noise trimmed. Derived rather
        // than tabulated, so a new events file needs no entry here.
        foreach (string file in events.Select(e => e.File).Distinct(StringComparer.Ordinal))
            loc.Add(FileHeadingKey(file), FileHeading(file));
        loc.Blank();

        loc.Add("gen_debug_panel_decision", "Generated World (debug)");
        loc.Add("gen_debug_panel_decision_desc",
            "Open the generator's own account of this world: what was written into it, what the "
            + "game loaded, and the levers for telling the two apart.");
        loc.Add("gen_debug_panel_decision_tooltip",
            "Debug only. Shows how this map was generated and what the running game made of it.");
        loc.Add("gen_debug_panel_decision_confirm", "Open the panel");

        loc.Add("GEN_DEBUG_PANEL_TITLE", "Generated World");

        loc.Add("GEN_DEBUG_PANEL_TAB_WORLD", "World");
        loc.Add("GEN_DEBUG_PANEL_TAB_REALM", "Live");
        loc.Add("GEN_DEBUG_PANEL_TAB_TOOLS", "Tools");

        loc.Add("GEN_DEBUG_PANEL_HEAD_RUN", "This run");
        loc.Add("GEN_DEBUG_PANEL_HEAD_MAP", "The map");
        loc.Add("GEN_DEBUG_PANEL_HEAD_TITLES", "Titles written, and titles loaded");
        loc.Add("GEN_DEBUG_PANEL_HEAD_PEOPLES", "Peoples");
        loc.Add("GEN_DEBUG_PANEL_HEAD_FEATURES", "Features");

        loc.Add("GEN_DEBUG_PANEL_TAB_EVENTS", "Events");

        loc.Add("GEN_DEBUG_PANEL_EVENTS_NONE",
            "This map ships no events. Every optional system that brings them — the wilderness, "
            + "wonders, struggles — was switched off when it was generated.");

        loc.Add("GEN_DEBUG_PANEL_EVENTS_NOTE",
            "Every event this mod ships, fired on you. #high A button doing nothing is a normal "
            + "result#!: #EMP trigger_event#! still evaluates the event's own trigger, so one that "
            + "wants a colony underway, a war, or a scope it has not been given will decline "
            + "silently. Events marked #EMP hidden#! run their effects without ever showing a "
            + "window.");

        // One label and one tooltip per event. Written from the same scan the window was built
        // from, so a button can never name a key this loop did not write.
        foreach (var entry in events)
        {
            loc.Add(EventLabelKey(entry), entry.Id
                + (entry.Hidden ? "  (hidden)" : "")
                + (entry.CanFire ? "" : $"  — {entry.Scope} scope"));

            // AddBuilt, not Add: LocFile.Add runs ParadoxText.Loc, which collapses a real newline
            // to a space. A tooltip wants the two-character \n that CK3 reads as a line break, and
            // that is precisely what Loc would flatten.
            loc.AddBuilt(EventTooltipKey(entry),
                $"#high {entry.Id}#!\\n"
                + $"#weak {entry.Type} · {entry.File}#!\\n\\n"
                + (entry.Hidden
                    ? "This event is #EMP hidden#! — it runs its effects and shows no window, so "
                      + "look for what it changed rather than for a popup.\\n\\n"
                    : "")
                + (entry.CanFire
                    ? "Fires it on you now. If nothing happens, the event's own trigger said no."
                    : $"#EMP Cannot be fired from here.#! It runs in a {entry.Scope} scope, and this "
                      + "panel can only produce you and your capital. Listed so you know it ships."));
        }

        loc.Add("GEN_DEBUG_PANEL_TITLES_NOTE",
            "These four should agree. The live figures skip the ~1,450 vanilla titular shims in "
            + "#high zz_vanilla_titulars.txt#!, which exist only so vanilla's hardcoded references "
            + "still resolve. Landless duchies are broken out because the game creates its own at "
            + "start: every holy order and mercenary company.");
        loc.Add("GEN_DEBUG_PANEL_PEOPLES_NOTE",
            "These will not agree, and should not. The map replaces vanilla's #high titles#! but "
            + "adds its #high peoples#! alongside vanilla's rather than replacing them, so the live "
            + "figure counts every vanilla culture, faith and religion too.");

        loc.Add("GEN_DEBUG_PANEL_HEAD_PLAYER", "You");
        loc.Add("GEN_DEBUG_PANEL_HEAD_LIVE", "The world as loaded");
        loc.Add("GEN_DEBUG_PANEL_HEAD_MINE", "Reading these");

        loc.Add("GEN_DEBUG_PANEL_LIVE_NOTE",
            "Counted when this window opened, not while you watch. Press #high Refresh#! on the "
            + "Tools tab after anything that would move them.");
        loc.Add("GEN_DEBUG_PANEL_WILDERNESS_NOTE",
            "This map ships the wilderness system, so counties with no holder are the frontier "
            + "still waiting to be settled rather than a fault.");

        loc.Add("GEN_DEBUG_PANEL_HEAD_INSPECT", "Inspect");
        loc.Add("GEN_DEBUG_PANEL_HEAD_RESOURCES", "Testing resources");
        loc.Add("GEN_DEBUG_PANEL_HEAD_WINDOWS", "The other generated windows");

        loc.Add("GEN_DEBUG_PANEL_REFRESH", "Recount the world");
        loc.Add("GEN_DEBUG_PANEL_REFRESH_TT",
            "Walk every county, title, culture, faith and ruler again and update the Live tab.");

        loc.Add("GEN_DEBUG_PANEL_LOG", "Write the counts to game.log");
        loc.Add("GEN_DEBUG_PANEL_LOG_TT",
            "Recounts the world and dumps every figure into #high game.log#! as named scopes, "
            + "so a run can be compared against the generator's own proctool.txt.");

        loc.Add("GEN_DEBUG_PANEL_GOLD", "Add 1000 gold");
        loc.Add("GEN_DEBUG_PANEL_GOLD_TT", "For testing anything this map generated that costs money.");
        loc.Add("GEN_DEBUG_PANEL_PRESTIGE", "Add 1000 prestige");
        loc.Add("GEN_DEBUG_PANEL_PRESTIGE_TT", "For testing generated decisions gated on prestige.");
        loc.Add("GEN_DEBUG_PANEL_PIETY", "Add 1000 piety");
        loc.Add("GEN_DEBUG_PANEL_PIETY_TT", "For testing generated decisions gated on piety.");

        loc.Add("GEN_DEBUG_PANEL_ARTIFACTS", "Open the artifact index");
        loc.Add("GEN_DEBUG_PANEL_ARTIFACTS_TT",
            "Closes this panel and opens the world's famed and illustrious treasures.");
        loc.Add("GEN_DEBUG_PANEL_WONDERS", "Open the wonder index");
        loc.Add("GEN_DEBUG_PANEL_WONDERS_TT",
            "Closes this panel and opens the great works this map placed.");

        loc.Write(Path.Combine(modDir, "localization", "english", "gen_debug_panel_l_english.yml"));
    }
}
