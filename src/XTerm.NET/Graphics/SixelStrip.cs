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
/// <para><b>Layout.</b> BGRA, straight alpha, top row first, as <see cref="TerminalImage.Pixels"/>
/// is: <see cref="Columns"/> cells of <see cref="CellWidth"/> pixels across and
/// <see cref="CellHeight"/> pixels down, read out with <see cref="CopyPixelsTo"/>. A pixel nothing has
/// painted is fully transparent, so the cell beneath shows through it. <see cref="Columns"/> is the
/// widest anything has painted and is never clipped to the line, so a terminal widened later shows
/// more rather than having lost it -- the renderer draws <c>min(PaintedColumns, line width)</c>, as it
/// does for a picture.</para>
/// <para><b>One scale per strip.</b> A Sixel is laid out against the cell size in force when it
/// arrives. A strip painted at another size is resampled to the new one first, so every pixel in it
/// agrees on how big a cell is and a renderer maps it to the grid with a single scale.</para>
/// <para><b>Storage, and sharing.</b> The pixels are held in blocks of 16 cells, each allocated only
/// once something paints it -- an implementation detail: <see cref="CopyPixelsTo"/> hands out an
/// ordinary contiguous image whatever the storage. A copy of a line
/// (<see cref="Buffer.BufferLine.Clone"/>, <see cref="Buffer.BufferLine.CopyFrom"/>) shares its strip,
/// and writing to a shared strip copies only the blocks the write touches. A whole row in one array
/// was past the large-object threshold at an ordinary width, and a host that captures frames copies
/// every line of one many times a second -- so each repaint of a captured row copied a large array,
/// and a program streaming full-screen frames (VT DOOM) ran the garbage collector flat out. Blocks
/// are small enough to stay out of the large-object heap, and a repaint of part of a row copies only
/// that part.</para>
/// <para><b>Change.</b> <see cref="Version"/> is taken from one counter for every strip and moves
/// after each change is complete, so a renderer holding an upload keyed on it knows the upload is
/// current, and one that read the pixels while they were being written sees the version move and
/// uploads again. Nothing outside this assembly can write to a strip.</para>
/// </remarks>
public sealed class SixelStrip
{
    /// <summary>Cells per storage block. See the remarks on the class; nothing outside depends on it.</summary>
    private const int BlockColumns = 16;

    private static long _versionCounter;

    private byte[]?[] _blocks;
    private bool[] _ownsBlock;
    private bool[] _painted;
    private int _paintedCount;

    private SixelStrip(int cellWidth, int cellHeight, int columns)
    {
        CellWidth = cellWidth;
        CellHeight = cellHeight;
        Columns = columns;
        _blocks = new byte[BlocksFor(columns)][];
        _ownsBlock = new bool[_blocks.Length];
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

    /// <summary>Bytes per row of the whole strip laid out contiguously, as <see cref="CopyPixelsTo"/> can.</summary>
    public int Stride => PixelWidth * TerminalImage.BytesPerPixel;

    /// <summary>Bytes the strip's allocated blocks occupy.</summary>
    public long ByteCount
    {
        get
        {
            long bytes = 0;
            foreach (var block in _blocks)
                bytes += block?.LongLength ?? 0;
            return bytes;
        }
    }

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

    /// <summary>
    /// Copies the first <paramref name="columns"/> cells of the strip into
    /// <paramref name="destination"/>, BGRA with straight alpha, top row first,
    /// <paramref name="destinationStride"/> bytes per row. Pixels nothing painted are written as
    /// transparent, so the destination need not be cleared first.
    /// </summary>
    /// <remarks>
    /// May be called from a thread other than the one writing to the terminal; a copy that overlaps
    /// a write may be torn, which <see cref="Version"/> moving across the copy reveals.
    /// </remarks>
    public void CopyPixelsTo(Span<byte> destination, int destinationStride, int columns)
    {
        columns = Math.Clamp(columns, 0, Columns);
        int rowBytes = columns * CellWidth * TerminalImage.BytesPerPixel;
        if (destinationStride < rowBytes)
            throw new ArgumentOutOfRangeException(nameof(destinationStride));
        if (destination.Length < (long)destinationStride * (CellHeight - 1) + rowBytes)
            throw new ArgumentException("The destination is too small for the strip.", nameof(destination));

        int blockStride = BlockStride;
        var blocks = _blocks;
        for (int b = 0; b * BlockColumns < columns && b < blocks.Length; b++)
        {
            int blockColumns = Math.Min(BlockColumns, columns - b * BlockColumns);
            int segmentBytes = blockColumns * CellWidth * TerminalImage.BytesPerPixel;
            int destX = b * BlockColumns * CellWidth * TerminalImage.BytesPerPixel;
            var block = blocks[b];

            for (int y = 0; y < CellHeight; y++)
            {
                var target = destination.Slice(y * destinationStride + destX, segmentBytes);
                if (block is null)
                    target.Clear();
                else
                    block.AsSpan(y * blockStride, segmentBytes).CopyTo(target);
            }
        }
    }

    /// <summary>Whether every column is clear, in which case the line drops the strip.</summary>
    internal bool IsEmpty => _paintedCount == 0;

    /// <summary>Bytes per row of one block.</summary>
    private int BlockStride => BlockColumns * CellWidth * TerminalImage.BytesPerPixel;

    private static int BlocksFor(int columns) => (columns + BlockColumns - 1) / BlockColumns;

    private static long NextVersion() => Interlocked.Increment(ref _versionCounter);

    /// <summary>A strip ready to be painted at the given cell size.</summary>
    internal static SixelStrip Create(int cellWidth, int cellHeight, int columns)
        => new(Math.Max(1, cellWidth), Math.Max(1, cellHeight), Math.Max(1, columns));

    /// <summary>
    /// A strip for a line that shared this one and is about to write to it. The blocks are shared,
    /// and whichever of the two writes to a block first takes its own copy of it.
    /// </summary>
    internal SixelStrip Copy()
    {
        var copy = new SixelStrip(CellWidth, CellHeight, Columns);
        Array.Copy(_blocks, copy._blocks, _blocks.Length);
        Array.Copy(_painted, copy._painted, _painted.Length);
        copy._paintedCount = _paintedCount;

        // Neither owns a block any more: both now point at the same arrays.
        Array.Clear(_ownsBlock);
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
        var source = image.CurrentPixels.Span;
        var sourceStride = image.Stride;
        int blockWidth = BlockColumns * CellWidth;
        int blockStride = BlockStride;

        // The picture's pixels land at strip x = column * CellWidth + x; walk them a block at a time.
        int destStart = column * CellWidth;
        int destEnd = destStart + image.PixelWidth;
        for (int b = destStart / blockWidth; b * blockWidth < destEnd; b++)
        {
            int segStart = Math.Max(destStart, b * blockWidth);
            int segEnd = Math.Min(destEnd, (b + 1) * blockWidth);
            int segBytes = (segEnd - segStart) * TerminalImage.BytesPerPixel;
            var block = WritableBlock(b);

            for (var y = 0; y < rows; y++)
            {
                var src = source.Slice((sourceY + y) * sourceStride + (segStart - destStart) * TerminalImage.BytesPerPixel, segBytes);
                var dst = block.AsSpan(y * blockStride + (segStart - b * blockWidth) * TerminalImage.BytesPerPixel, segBytes);

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

        int cellBytes = CellWidth * TerminalImage.BytesPerPixel;
        var scratch = new byte[cellBytes * CellHeight];

        // Within one strip, walk away from the overlap so no cell is read after it was overwritten.
        bool backwards = ReferenceEquals(source, this) && destColumn > sourceColumn;
        for (var n = 0; n < count; n++)
        {
            var i = backwards ? count - 1 - n : n;
            var dest = destColumn + i;
            var src = sourceColumn + i;

            if (source is null || !source.HasPixelsAt(src))
            {
                if (_painted[dest])
                {
                    ClearCell(dest);
                    _painted[dest] = false;
                    _paintedCount--;
                }
                continue;
            }

            source.ReadCell(src, scratch);
            WriteCell(dest, scratch);
            MarkPainted(dest);
        }

        Version = NextVersion();
    }

    /// <summary>The block holding <paramref name="index"/>, allocated or made private so it can be written.</summary>
    private byte[] WritableBlock(int index)
    {
        var block = _blocks[index];
        if (block is null)
        {
            block = new byte[BlockStride * CellHeight];
        }
        else if (!_ownsBlock[index])
        {
            block = (byte[])block.Clone();
        }
        else
        {
            return block;
        }

        _blocks[index] = block;
        _ownsBlock[index] = true;
        return block;
    }

    private void ReadCell(int column, byte[] cell)
    {
        int cellBytes = CellWidth * TerminalImage.BytesPerPixel;
        var block = _blocks[column / BlockColumns];
        if (block is null)
        {
            Array.Clear(cell);
            return;
        }

        int offset = (column % BlockColumns) * cellBytes;
        for (var y = 0; y < CellHeight; y++)
            System.Buffer.BlockCopy(block, y * BlockStride + offset, cell, y * cellBytes, cellBytes);
    }

    private void WriteCell(int column, byte[] cell)
    {
        int cellBytes = CellWidth * TerminalImage.BytesPerPixel;
        var block = WritableBlock(column / BlockColumns);
        int offset = (column % BlockColumns) * cellBytes;
        for (var y = 0; y < CellHeight; y++)
            System.Buffer.BlockCopy(cell, y * cellBytes, block, y * BlockStride + offset, cellBytes);
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
        if (_blocks[column / BlockColumns] is null)
            return;

        var block = WritableBlock(column / BlockColumns);
        int cellBytes = CellWidth * TerminalImage.BytesPerPixel;
        int offset = (column % BlockColumns) * cellBytes;
        for (var y = 0; y < CellHeight; y++)
            Array.Clear(block, y * BlockStride + offset, cellBytes);
    }

    /// <summary>Widens the strip to hold at least <paramref name="columns"/> cells; new cells are clear.</summary>
    private void EnsureColumns(int columns)
    {
        if (columns <= Columns)
            return;

        var blocks = BlocksFor(columns);
        if (blocks > _blocks.Length)
        {
            Array.Resize(ref _blocks, blocks);
            Array.Resize(ref _ownsBlock, blocks);
        }

        Array.Resize(ref _painted, columns);
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

        // Read the whole strip out at the old size, then lay it back in at the new one.
        var oldWidth = PixelWidth;
        var oldHeight = CellHeight;
        var oldStride = Stride;
        var old = new byte[oldStride * oldHeight];
        CopyPixelsTo(old, oldStride, Columns);

        CellWidth = cellWidth;
        CellHeight = cellHeight;
        _blocks = new byte[BlocksFor(Columns)][];
        _ownsBlock = new bool[_blocks.Length];

        int width = PixelWidth;
        int blockWidth = BlockColumns * cellWidth;
        for (var b = 0; b < _blocks.Length; b++)
        {
            // Only blocks with something painted in them are worth allocating.
            var any = false;
            for (var c = b * BlockColumns; c < Math.Min(Columns, (b + 1) * BlockColumns) && !any; c++)
                any = _painted[c];
            if (!any)
                continue;

            var block = WritableBlock(b);
            for (var y = 0; y < cellHeight; y++)
            {
                var sy = Math.Min(oldHeight - 1, y * oldHeight / cellHeight);
                for (var x = b * blockWidth; x < Math.Min(width, (b + 1) * blockWidth); x++)
                {
                    var sx = Math.Min(oldWidth - 1, x * oldWidth / width);
                    System.Buffer.BlockCopy(old, sy * oldStride + sx * TerminalImage.BytesPerPixel,
                                            block, y * BlockStride + (x - b * blockWidth) * TerminalImage.BytesPerPixel,
                                            TerminalImage.BytesPerPixel);
                }
            }
        }
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
