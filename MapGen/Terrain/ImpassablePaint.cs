using SixLabors.ImageSharp.PixelFormats;
using SharpImage = SixLabors.ImageSharp.Image;

namespace Ck3MapGen.MapGen;

/// <summary>
/// The impassable mask painted on the Masks tab: one three-state cell per pixel, at an authoring
/// resolution of the province raster.
///
/// <see cref="ImpassableMask.Auto"/> (transparent) is unpainted, and what it means is
/// <see cref="Config.MapConfig.ImpassablePaintMode"/>'s call; <see cref="ImpassableMask.Wall"/>
/// (white) is impassable and <see cref="ImpassableMask.Passable"/> (black) is not, whatever the
/// automatic walls say. Hard-edged on purpose: a wall either is or is not, and the partition snaps
/// to the edge as drawn.
///
/// Saved as an RGBA PNG in exactly those colours, so the file is also a valid
/// <see cref="Config.MapConfig.ImpassableMaskPath"/> for the command line.
/// </summary>
public sealed class ImpassablePaint
{
    public int Width { get; }
    public int Height { get; }

    /// <summary>One <see cref="ImpassableMask"/> state per pixel, row-major.</summary>
    public byte[] Cells { get; }

    /// <summary>Bumped on every change, so a cache can tell whether it is looking at this paint.</summary>
    public int Version { get; private set; }

    public ImpassablePaint(int width, int height)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        Cells = new byte[Width * Height];
    }

    /// <summary>
    /// The authoring size for a province raster this big: up to 2048 across, which is a few
    /// province pixels per cell on the largest maps — finer than a barony, coarse enough to undo.
    /// </summary>
    public static (int Width, int Height) SizeFor(int provinceWidth, int provinceHeight)
    {
        int w = Math.Min(2048, Math.Max(1, provinceWidth));
        int h = Math.Max(2, (int)((long)w * Math.Max(1, provinceHeight) / Math.Max(1, provinceWidth)));
        return (w, h);
    }

    /// <summary>True when every cell is still automatic.</summary>
    public bool IsEmpty => Array.IndexOf(Cells, ImpassableMask.Wall) < 0 && Array.IndexOf(Cells, ImpassableMask.Passable) < 0;

    public (long Walls, long Passable) Counts()
    {
        long walls = 0, passable = 0;
        foreach (byte c in Cells)
        {
            if (c == ImpassableMask.Wall) walls++;
            else if (c == ImpassableMask.Passable) passable++;
        }
        return (walls, passable);
    }

    public void Touch() => Version++;

    /// <summary>A copy at another size, resampled by nearest pixel. The same paint on a different map.</summary>
    public ImpassablePaint Resampled(int width, int height)
    {
        if (width == Width && height == Height) return Clone();

        var result = new ImpassablePaint(width, height);
        for (int y = 0; y < height; y++)
        {
            int sy = Math.Min(Height - 1, (int)((long)y * Height / height));
            for (int x = 0; x < width; x++)
            {
                int sx = Math.Min(Width - 1, (int)((long)x * Width / width));
                result.Cells[y * width + x] = Cells[sy * Width + sx];
            }
        }
        return result;
    }

    public ImpassablePaint Clone()
    {
        var copy = new ImpassablePaint(Width, Height);
        Array.Copy(Cells, copy.Cells, Cells.Length);
        return copy;
    }

    // ------------------------------------------------------------------ file

    /// <summary>White = wall, black = passable, transparent = automatic.</summary>
    public void Save(string path)
    {
        using var image = new SixLabors.ImageSharp.Image<Rgba32>(Width, Height);
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                int src = y * Width;
                for (int x = 0; x < Width; x++)
                {
                    row[x] = Cells[src + x] switch
                    {
                        ImpassableMask.Wall => new Rgba32(255, 255, 255, 255),
                        ImpassableMask.Passable => new Rgba32(0, 0, 0, 255),
                        _ => new Rgba32(0, 0, 0, 0),
                    };
                }
            }
        });

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = File.Create(path);
        image.Save(stream, new SixLabors.ImageSharp.Formats.Png.PngEncoder
        {
            ColorType = SixLabors.ImageSharp.Formats.Png.PngColorType.RgbWithAlpha,
        });
    }

    /// <summary>
    /// Reads any image as a paint, by <see cref="ImpassableMask.Classify"/>: an old black-and-white
    /// mask with no alpha loads as black and white, which is what it always meant.
    /// </summary>
    public static ImpassablePaint Load(string path)
    {
        using var image = SharpImage.Load<Rgba32>(path);
        var paint = new ImpassablePaint(image.Width, image.Height);
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < paint.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                int dst = y * paint.Width;
                for (int x = 0; x < paint.Width; x++)
                    paint.Cells[dst + x] = ImpassableMask.Classify(row[x].R, row[x].G, row[x].B, row[x].A);
            }
        });
        return paint;
    }
}

/// <summary>
/// A stroke in progress on an <see cref="ImpassablePaint"/>: one state laid down in hard round
/// dabs along the mouse path.
/// </summary>
public sealed class ImpassableStroke
{
    private readonly ImpassablePaint _paint;
    private readonly byte[] _before;
    private readonly byte _value;
    private readonly float _radius;   // in paint pixels
    private float _lastX = float.NaN, _lastY = float.NaN;
    private int _minX = int.MaxValue, _minY = int.MaxValue, _maxX = -1, _maxY = -1;

    public ImpassableStroke(ImpassablePaint paint, byte value, float radiusPixels)
    {
        _paint = paint;
        _value = value;
        _radius = Math.Max(0.5f, radiusPixels);
        _before = (byte[])paint.Cells.Clone();
    }

    /// <summary>Extends the stroke to a point in paint pixels; returns the pixels changed by this move.</summary>
    public Rectangle? MoveTo(float x, float y)
    {
        int dMinX = int.MaxValue, dMinY = int.MaxValue, dMaxX = -1, dMaxY = -1;

        if (float.IsNaN(_lastX))
        {
            Dab(x, y);
        }
        else
        {
            float dx = x - _lastX, dy = y - _lastY;
            float spacing = Math.Max(0.75f, _radius * 0.3f);
            int steps = Math.Max(1, (int)MathF.Ceiling(MathF.Sqrt(dx * dx + dy * dy) / spacing));
            for (int s = 1; s <= steps; s++)
                Dab(_lastX + dx * s / steps, _lastY + dy * s / steps);
        }

        _lastX = x;
        _lastY = y;

        void Dab(float cx, float cy)
        {
            int x0 = Math.Max(0, (int)MathF.Floor(cx - _radius)), x1 = Math.Min(_paint.Width - 1, (int)MathF.Ceiling(cx + _radius));
            int y0 = Math.Max(0, (int)MathF.Floor(cy - _radius)), y1 = Math.Min(_paint.Height - 1, (int)MathF.Ceiling(cy + _radius));
            if (x0 > x1 || y0 > y1) return;

            float r2 = _radius * _radius;
            for (int py = y0; py <= y1; py++)
            {
                for (int px = x0; px <= x1; px++)
                {
                    float ddx = px + 0.5f - cx, ddy = py + 0.5f - cy;
                    if (ddx * ddx + ddy * ddy > r2) continue;

                    int i = py * _paint.Width + px;
                    if (_paint.Cells[i] == _value) continue;
                    _paint.Cells[i] = _value;
                    dMinX = Math.Min(dMinX, px); dMaxX = Math.Max(dMaxX, px);
                    dMinY = Math.Min(dMinY, py); dMaxY = Math.Max(dMaxY, py);
                }
            }

            _minX = Math.Min(_minX, x0); _maxX = Math.Max(_maxX, x1);
            _minY = Math.Min(_minY, y0); _maxY = Math.Max(_maxY, y1);
        }

        if (dMaxX < 0) return null;
        _paint.Touch();
        return new Rectangle(dMinX, dMinY, dMaxX - dMinX + 1, dMaxY - dMinY + 1);
    }

    /// <summary>Finishes the stroke and returns what it changed, for the undo stack. Null if nothing changed.</summary>
    public ImpassableEdit? End()
    {
        if (_maxX < 0) return null;
        var area = new Rectangle(_minX, _minY, _maxX - _minX + 1, _maxY - _minY + 1);
        var edit = ImpassableEdit.Capture(_paint, _before, area);
        return edit.Changes ? edit : null;
    }
}

/// <summary>One reversible change to an <see cref="ImpassablePaint"/>: the cells inside a rectangle, before and after.</summary>
public sealed class ImpassableEdit
{
    public Rectangle Area { get; }
    private readonly byte[] _before;
    private readonly byte[] _after;

    private ImpassableEdit(Rectangle area, byte[] before, byte[] after)
    {
        Area = area;
        _before = before;
        _after = after;
    }

    public bool Changes => !_before.AsSpan().SequenceEqual(_after);

    public static ImpassableEdit Capture(ImpassablePaint paint, byte[] before, Rectangle area)
        => new(area, Cut(before, paint.Width, area), Cut(paint.Cells, paint.Width, area));

    /// <summary>Every cell set to <paramref name="value"/>, recorded so that it can be undone.</summary>
    public static ImpassableEdit Fill(ImpassablePaint paint, byte value)
    {
        var after = new byte[paint.Cells.Length];
        if (value != 0) Array.Fill(after, value);
        return new(new Rectangle(0, 0, paint.Width, paint.Height), (byte[])paint.Cells.Clone(), after);
    }

    public void Undo(ImpassablePaint paint) => Paste(paint, _before);
    public void Redo(ImpassablePaint paint) => Paste(paint, _after);

    private void Paste(ImpassablePaint paint, byte[] cells)
    {
        for (int y = 0; y < Area.Height; y++)
            Array.Copy(cells, y * Area.Width, paint.Cells, (Area.Y + y) * paint.Width + Area.X, Area.Width);
        paint.Touch();
    }

    private static byte[] Cut(byte[] cells, int width, Rectangle area)
    {
        var result = new byte[area.Width * area.Height];
        for (int y = 0; y < area.Height; y++)
            Array.Copy(cells, (area.Y + y) * width + area.X, result, y * area.Width, area.Width);
        return result;
    }
}

/// <summary>Undo and redo over <see cref="ImpassableEdit"/>s.</summary>
public sealed class ImpassableHistory
{
    private readonly List<ImpassableEdit> _edits = [];
    private int _cursor;
    private const int Limit = 64;

    public bool CanUndo => _cursor > 0;
    public bool CanRedo => _cursor < _edits.Count;

    public void Push(ImpassableEdit edit)
    {
        if (_cursor < _edits.Count) _edits.RemoveRange(_cursor, _edits.Count - _cursor);
        _edits.Add(edit);
        if (_edits.Count > Limit) _edits.RemoveAt(0);
        _cursor = _edits.Count;
    }

    public bool Undo(ImpassablePaint paint)
    {
        if (!CanUndo) return false;
        _edits[--_cursor].Undo(paint);
        return true;
    }

    public bool Redo(ImpassablePaint paint)
    {
        if (!CanRedo) return false;
        _edits[_cursor++].Redo(paint);
        return true;
    }

    public void Clear()
    {
        _edits.Clear();
        _cursor = 0;
    }
}
