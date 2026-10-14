namespace XTerm.Graphics;

/// <summary>
/// The Sixel pixels one line shows: a strip one cell tall, painted by every Sixel that lands on the
/// line and erased cell by cell by whatever is printed or erased over it.
/// </summary>
/// <remarks>
/// <para><b>Pixels, not pictures.</b> A Sixel is content: it paints the cells it lands on, a later
/// Sixel paints over it, and printing over a cell replaces that part of it. So the line keeps the
/// RESULT -- one strip of pixels -- rather than every picture that ever arrived and a record of which
/// slivers of each are still showing. A program that repaints small regions over and over (a cursor,
/// a hover, a diffing framebuffer) costs one strip per line however many Sixels it sends, where
/// keeping the pictures grew thousands of fragments per screen, every one a separate draw.</para>
/// <para><b>Layout.</b> BGRA, straight alpha, top row first, exactly like
/// <see cref="TerminalImage.Pixels"/>: <see cref="Columns"/> cells of <see cref="CellWidth"/> pixels
/// across and <see cref="CellHeight"/> pixels down. A pixel nothing has painted is fully transparent,
/// so the cell beneath shows through it. <see cref="Columns"/> is the widest anything has painted and
/// is never clipped to the line, so a terminal widened later shows more rather than having lost it --
/// the renderer draws <c>min(Columns, line width)</c>, as it does for a picture.</para>
/// <para><b>One scale per strip.</b> A Sixel is laid out against the cell size in force when it
/// arrives. A strip painted at another size is resampled to the new one first, so every pixel in it
/// agrees on how big a cell is and a renderer maps it to the grid with a single scale.</para>
/// <para><b>Sharing.</b> A copy of a line (<see cref="Buffer.BufferLine.Clone"/>,
/// <see cref="Buffer.BufferLine.CopyFrom"/>) shares its strip, and whichever line writes first takes a
/// private copy, so a frame captured for a renderer costs a reference rather than the pixels. Nothing
/// outside this assembly can write to a strip.</para>
/// <para><b>Change.</b> <see cref="Version"/> is taken from one counter for every strip and moves
/// after each change is complete, so a renderer holding an upload keyed on it knows the upload is
/// current, and one that read the pixels while they were being written sees the version move and
/// uploads again.</para>
/// </remarks>
public sealed class SixelStrip
{
    private static long _versionCounter;

    private byte[] _pixels;
    private bool[] _painted;
    private int _paintedCount;

    private SixelStrip(int cellWidth, int cellHeight, int columns)
    {
        CellWidth = cellWidth;
        CellHeight = cellHeight;
        Columns = columns;
        _pixels = new byte[columns * cellWidth * cellHeight * TerminalImage.BytesPerPixel];
        _painted = new bool[columns];
        Version = NextVersion();
    }

    /// <summary>Width of one cell in this strip's pixels.</summary>
    public int CellWidth { get; private set; }

    /// <summary>Height of one cell, which is the strip's height, in pixels.</summary>
    public int CellHeight { get; private set; }

    /// <summary>How many cells across the strip holds pixels for.</summary>
    public int Columns { get; private set; }

    /// <summary>The strip's width in pixels.</summary>
    public int PixelWidth => Columns * CellWidth;

    /// <summary>The strip's height in pixels: one cell.</summary>
    public int PixelHeight => CellHeight;

    /// <summary>Bytes per row of <see cref="Pixels"/>.</summary>
    public int Stride => PixelWidth * TerminalImage.BytesPerPixel;

    /// <summary>The pixels, BGRA with straight alpha, top row first. See the remarks on the class.</summary>
    public ReadOnlyMemory<byte> Pixels => _pixels;

    /// <summary>Bytes the pixels occupy.</summary>
    public long ByteCount => _pixels.LongLength;

    /// <summary>Changes after every change to the pixels, and is never reused by another strip.</summary>
    public long Version { get; private set; }

    /// <summary>Whether anything has painted <paramref name="column"/> since it was last cleared.</summary>
    public bool HasPixelsAt(int column) => column >= 0 && column < Columns && _painted[column];

    /// <summary>
    /// One past the rightmost painted cell: how much of the strip a renderer needs to draw. Zero when
    /// nothing is painted.
    /// </summary>
    public int PaintedColumns
    {
        get
        {
            for (var c = Columns - 1; c >= 0; c--)
            {
                if (_painted[c])
                    return c + 1;
            }

            return 0;
        }
    }

    /// <summary>Whether every column is clear, in which case the line drops the strip.</summary>
    internal bool IsEmpty => _paintedCount == 0;

    private static long NextVersion() => Interlocked.Increment(ref _versionCounter);

    /// <summary>A strip ready to be painted at the given cell size.</summary>
    internal static SixelStrip Create(int cellWidth, int cellHeight, int columns)
        => new(Math.Max(1, cellWidth), Math.Max(1, cellHeight), Math.Max(1, columns));

    /// <summary>A private copy, for a line that shared this strip and is about to write to it.</summary>
    internal SixelStrip Copy()
    {
        var copy = new SixelStrip(CellWidth, CellHeight, Columns);
        System.Buffer.BlockCopy(_pixels, 0, copy._pixels, 0, _pixels.Length);
        Array.Copy(_painted, copy._painted, _painted.Length);
        copy._paintedCount = _paintedCount;
        return copy;
    }

    /// <summary>
    /// Paints one cell-row of <paramref name="image"/> -- the band starting at pixel row
    /// <paramref name="sourceY"/> -- into this strip from <paramref name="column"/> on.
    /// </summary>
    /// <remarks>
    /// <para>A fully transparent source pixel leaves the strip alone, which is Sixel's own rule: a
    /// picture drawn with background select 1 lets whatever was there show through its unset pixels.
    /// A fully opaque one replaces the strip's pixel. Anything between -- an inline PNG's soft edge --
    /// is blended over it.</para>
    /// <para>The cells the picture covers are marked painted, the last one included even where the
    /// picture ends partway through it: that cell shows part of the picture.</para>
    /// </remarks>
    internal void Paint(TerminalImage image, int sourceY, int column)
    {
        if (column < 0 || sourceY < 0 || sourceY >= image.PixelHeight)
            return;

        if (image.CellWidth != CellWidth || image.CellHeight != CellHeight)
            Resample(image.CellWidth, image.CellHeight);

        var imageCols = (image.PixelWidth + CellWidth - 1) / CellWidth;
        EnsureColumns(column + imageCols);

        var rows = Math.Min(CellHeight, image.PixelHeight - sourceY);
        var width = image.PixelWidth;
        var source = image.CurrentPixels.Span;
        var sourceStride = image.Stride;
        var stride = Stride;
        var destX = column * CellWidth * TerminalImage.BytesPerPixel;

        for (var y = 0; y < rows; y++)
        {
            var src = source.Slice((sourceY + y) * sourceStride, width * TerminalImage.BytesPerPixel);
            var dst = _pixels.AsSpan(y * stride + destX, width * TerminalImage.BytesPerPixel);

            for (var x = 0; x < src.Length; x += TerminalImage.BytesPerPixel)
            {
                var alpha = src[x + 3];
                if (alpha == 0)
                    continue;

                if (alpha == 255 || dst[x + 3] == 0)
                {
                    src.Slice(x, TerminalImage.BytesPerPixel).CopyTo(dst.Slice(x));
                    continue;
                }

                Blend(src.Slice(x, TerminalImage.BytesPerPixel), dst.Slice(x, TerminalImage.BytesPerPixel));
            }
        }

        for (var c = column; c < column + imageCols; c++)
            MarkPainted(c);

        Version = NextVersion();
    }

    /// <summary>Clears <paramref name="count"/> cells from <paramref name="column"/>: printing or erasing there replaced the picture.</summary>
    /// <returns>Whether anything was cleared.</returns>
    internal bool Clear(int column, int count)
    {
        var start = Math.Max(0, column);
        var end = Math.Min(Columns, column + count);
        var cleared = false;

        for (var c = start; c < end; c++)
        {
            if (!_painted[c])
                continue;

            ClearCell(c);
            _painted[c] = false;
            _paintedCount--;
            cleared = true;
        }

        if (cleared)
            Version = NextVersion();
        return cleared;
    }

    /// <summary>
    /// Copies <paramref name="count"/> cells of <paramref name="source"/> (or blanks, when it is null)
    /// to <paramref name="destColumn"/> -- the pixels following their cells through an insert, a
    /// delete or a margin scroll. Overlapping ranges of one strip copy as if through a buffer.
    /// </summary>
    internal void CopyCells(SixelStrip? source, int sourceColumn, int destColumn, int count)
    {
        if (count <= 0)
            return;

        if (source is not null && (source.CellWidth != CellWidth || source.CellHeight != CellHeight))
        {
            source = source.Copy();
            source.Resample(CellWidth, CellHeight);
        }

        EnsureColumns(destColumn + count);

        // Through a scratch copy when the ranges are one strip's: simpler than choosing a direction
        // per row, and a shift within a line is a keystroke's worth of work.
        var from = ReferenceEquals(source, this) ? Copy() : source;
        var cellBytes = CellWidth * TerminalImage.BytesPerPixel;

        for (var i = 0; i < count; i++)
        {
            var dest = destColumn + i;
            var src = sourceColumn + i;
            var has = from is not null && from.HasPixelsAt(src);

            if (!has)
            {
                if (_painted[dest])
                {
                    ClearCell(dest);
                    _painted[dest] = false;
                    _paintedCount--;
                }
                continue;
            }

            for (var y = 0; y < CellHeight; y++)
                System.Buffer.BlockCopy(from!._pixels, y * from.Stride + src * cellBytes, _pixels, y * Stride + dest * cellBytes, cellBytes);

            MarkPainted(dest);
        }

        Version = NextVersion();
    }

    private void MarkPainted(int column)
    {
        if (_painted[column])
            return;
        _painted[column] = true;
        _paintedCount++;
    }

    private void ClearCell(int column)
    {
        var cellBytes = CellWidth * TerminalImage.BytesPerPixel;
        for (var y = 0; y < CellHeight; y++)
            Array.Clear(_pixels, y * Stride + column * cellBytes, cellBytes);
    }

    /// <summary>Widens the strip to hold at least <paramref name="columns"/> cells; new cells are clear.</summary>
    private void EnsureColumns(int columns)
    {
        if (columns <= Columns)
            return;

        var oldStride = Stride;
        var grown = new byte[columns * CellWidth * CellHeight * TerminalImage.BytesPerPixel];
        var newStride = columns * CellWidth * TerminalImage.BytesPerPixel;
        for (var y = 0; y < CellHeight; y++)
            System.Buffer.BlockCopy(_pixels, y * oldStride, grown, y * newStride, oldStride);

        Array.Resize(ref _painted, columns);
        _pixels = grown;
        Columns = columns;
    }

    /// <summary>
    /// Rescales every pixel to a new cell size, nearest neighbour: what was painted when cells were
    /// another size keeps its place in the grid. Only reached when the cell size has changed.
    /// </summary>
    private void Resample(int cellWidth, int cellHeight)
    {
        cellWidth = Math.Max(1, cellWidth);
        cellHeight = Math.Max(1, cellHeight);

        var oldWidth = PixelWidth;
        var oldHeight = CellHeight;
        var oldStride = Stride;
        var width = Columns * cellWidth;
        var stride = width * TerminalImage.BytesPerPixel;
        var resampled = new byte[stride * cellHeight];

        for (var y = 0; y < cellHeight; y++)
        {
            var sy = Math.Min(oldHeight - 1, y * oldHeight / cellHeight);
            for (var x = 0; x < width; x++)
            {
                var sx = Math.Min(oldWidth - 1, x * oldWidth / width);
                System.Buffer.BlockCopy(_pixels, sy * oldStride + sx * TerminalImage.BytesPerPixel,
                                 resampled, y * stride + x * TerminalImage.BytesPerPixel, TerminalImage.BytesPerPixel);
            }
        }

        _pixels = resampled;
        CellWidth = cellWidth;
        CellHeight = cellHeight;
    }

    /// <summary>Source over destination, both straight alpha.</summary>
    private static void Blend(ReadOnlySpan<byte> src, Span<byte> dst)
    {
        var sa = src[3] / 255f;
        var da = dst[3] / 255f;
        var outA = sa + da * (1 - sa);
        if (outA <= 0)
        {
            dst.Clear();
            return;
        }

        for (var i = 0; i < 3; i++)
            dst[i] = (byte)Math.Clamp((int)Math.Round((src[i] * sa + dst[i] * da * (1 - sa)) / outA), 0, 255);
        dst[3] = (byte)Math.Clamp((int)Math.Round(outA * 255), 0, 255);
    }
}
