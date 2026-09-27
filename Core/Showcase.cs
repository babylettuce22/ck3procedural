namespace Ck3MapGen.Core;

/// <summary>
/// One thing the generator just made, as a finished picture of it for someone watching a run: a
/// culture, a faith, a regiment, a treasure. Plain values only — no reference back into the world
/// being generated — so it can be handed to another thread and kept after the run ends.
/// </summary>
public sealed record ShowcaseItem
{
    /// <summary>What sort of thing this is, shown as a small label: "Culture", "Men-at-arms".</summary>
    public required string Kind { get; init; }

    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public string? Body { get; init; }

    /// <summary>Short facts shown as chips: a tradition, a tenet, a stat.</summary>
    public IReadOnlyList<string> Chips { get; init; } = [];

    /// <summary>A colour the thing is known by — a culture's, a faith's — for a swatch.</summary>
    public (byte R, byte G, byte B)? Color { get; init; }

    /// <summary>
    /// Image files to try, in order, as absolute paths (.dds or .png): the first that exists and
    /// decodes is used. Usually the mod's own file first and the game's second.
    /// </summary>
    public IReadOnlyList<string> Images { get; init; } = [];

    /// <summary>A wide illustration shown across the top, rather than an icon beside the text.</summary>
    public bool Illustration { get; init; }

    /// <summary>
    /// For an image that is a horizontal strip of equal frames — CK3's artifact icons hold one per
    /// rarity side by side — how many frames it has, and which one to show.
    /// </summary>
    public int ImageFrames { get; init; } = 1;
    public int ImageFrame { get; init; }

    /// <summary>
    /// Where on the map the thing is, as fractions of the map's width and height: a people's
    /// heartland, a faith's, the county a wonder stands in. A watching screen puts a pin there when
    /// the card is shown. Null for what has no one place — a regiment, a calendar.
    /// </summary>
    public (float X, float Y)? MapAt { get; init; }

    /// <summary>
    /// The pin's own picture, when it should not be the card's — the badge of a people's race —
    /// tried in order like <see cref="Images"/>. Empty means the pin wears the card's icon, or the
    /// card's colour when its picture is an illustration.
    /// </summary>
    public IReadOnlyList<string> PinImages { get; init; } = [];

    /// <summary>A drawn glyph for the pin in place of any picture, by name: a <see cref="MapGen.WonderArchetype"/>.</summary>
    public string? PinGlyph { get; init; }
}

/// <summary>Which moment of the province partition a <see cref="PartitionSketch"/> shows.</summary>
public enum PartitionStep
{
    /// <summary>The seeds are scattered; nothing has grown yet.</summary>
    Seeded,

    /// <summary>Every seed has grown until it met its neighbours.</summary>
    Grown,

    /// <summary>A round of relaxation: each seed moved to the middle of what it grew, and all grew again.</summary>
    Relaxed,

    /// <summary>The tidy-up passes done and the impassable mountains marked: the provinces as written.</summary>
    Settled,
}

public enum SketchSeed : byte { Land, River, Sea, Impassable }

/// <summary>
/// The province partition caught at one <see cref="PartitionStep"/>, at preview size, for a screen
/// that wants to show the provinces forming. Plain arrays, like a <see cref="ShowcaseItem"/>, so it
/// crosses to another thread as it is.
///
/// A seed keeps its slot from the run's first sketch to its last, so it can be followed as
/// relaxation moves it; one the tidy-up dissolved stays in its slot as NaN.
/// </summary>
public sealed record PartitionSketch
{
    public required PartitionStep Step { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }

    /// <summary>Each seed's position in preview pixels, by slot.</summary>
    public required float[] SeedX { get; init; }
    public required float[] SeedY { get; init; }
    public required SketchSeed[] SeedKind { get; init; }

    /// <summary>The provinces' colours, RGB row by row. Null on <see cref="PartitionStep.Seeded"/>.</summary>
    public byte[]? Rgb { get; init; }

    /// <summary>
    /// On <see cref="PartitionStep.Grown"/>: when growth reached each pixel, from 0 at a seed to
    /// <see cref="ushort.MaxValue"/> at the last pixel reached. Land and sea spread at one pace
    /// until the land is full; the open sea's remainder is squeezed into the last fifth.
    /// </summary>
    public ushort[]? Arrival { get; init; }

    /// <summary>On <see cref="PartitionStep.Relaxed"/>: which round this is, of how many.</summary>
    public int Pass { get; init; }
    public int Passes { get; init; }
}

/// <summary>A whole-map picture a stage drew of what it decided — the peoples, the faiths — named as a view.</summary>
public sealed record ShowcasePicture(string View, AppGUI.PreviewRenderer.Image Image);

/// <summary>
/// A window onto a run as it happens, for a screen that wants something to show while it waits.
///
/// The generator publishes finished things here straight after the stage that made them. It is
/// a spectator's channel and nothing more, held to three rules so it can never change a world:
/// <list type="bullet">
/// <item><b>Read only.</b> Items are built from what a stage already decided. Nothing here draws
/// from a generator's <see cref="Rng"/> or calls anything that could, so a run with a listener is
/// byte-identical to one without.</item>
/// <item><b>Free when nobody watches.</b> Items are built lazily, and only while
/// <see cref="Listening"/>; the command line never pays for them.</item>
/// <item><b>Never fatal.</b> A mistake in building an item, or in whoever receives it, is
/// swallowed. A picture that could not be made must not cost the user their mod.</item>
/// </list>
/// Handlers are called on the generator's thread and must hand the item over to their own.
/// </summary>
public static class Showcase
{
    public static event Action<ShowcaseItem>? Published;

    /// <summary>The province partition as it forms; see <see cref="PartitionSketch"/>.</summary>
    public static event Action<PartitionSketch>? Sketched;

    /// <summary>Whole-map pictures of the social layers as they are decided.</summary>
    public static event Action<ShowcasePicture>? Pictured;

    public static bool Listening => Published is not null;
    public static bool Sketching => Sketched is not null;

    /// <summary>Builds and publishes items, but only if someone is listening.</summary>
    public static void Publish(Func<IEnumerable<ShowcaseItem>> build) => Deliver(Published, build);

    /// <summary>Builds and publishes a sketch of the partition, but only if someone is watching it.</summary>
    public static void Sketch(Func<PartitionSketch?> build)
        => Deliver(Sketched, () => build() is { } sketch ? new[] { sketch } : []);

    /// <summary>Draws and publishes a picture of the map, but only if someone is watching.</summary>
    public static void Picture(string view, Func<AppGUI.PreviewRenderer.Image> draw)
        => Deliver(Pictured, () => new[] { new ShowcasePicture(view, draw()) });

    private static void Deliver<T>(Action<T>? handler, Func<IEnumerable<T>> build)
    {
        if (handler is null) return;

        List<T> items;
        try
        {
            items = build().ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  (showcase: {ex.GetType().Name}: {ex.Message})");
            return;
        }

        foreach (var item in items)
        {
            try { handler(item); }
            catch (Exception) { }
        }
    }
}
