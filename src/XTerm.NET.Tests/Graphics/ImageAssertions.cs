using System.Linq;
using XTerm;
using XTerm.Graphics;

namespace XTerm.Tests.Graphics;

/// <summary>
/// Asking the buffer what a picture covers, now that pictures live in runs on lines.
/// </summary>
/// <remarks>
/// These replace <c>cell.Image</c> and its companions, which cannot survive the move: a
/// <c>BufferCell</c> is a struct, so a cell copied out of a line has no idea which line or column it
/// came from and cannot answer for a run that is anchored to both. The question is the same one the
/// tests always asked — "is this position showing part of a picture, and which one" — it simply has
/// to be asked of the line.
/// </remarks>
internal static class ImageAssertions
{
    /// <summary>The run covering a screen position, if any.</summary>
    public static LinePlacement? PlacementAt(Terminal terminal, int col, int screenRow)
    {
        var line = terminal.Buffer.Lines[terminal.Buffer.YBase + screenRow];
        if (line is not null && line.TryGetPlacementAt(col, out var placement))
            return placement;

        return null;
    }

    /// <summary>The image shown at a screen position, or null for ordinary text.</summary>
    public static TerminalImage? ImageAt(Terminal terminal, int col, int screenRow)
    {
        var line = terminal.Buffer.Lines[terminal.Buffer.YBase + screenRow];
        return line is not null && line.TryGetImageAt(col, out var image) ? image : null;
    }

    /// <summary>Whether a screen position shows part of a picture.</summary>
    public static bool IsImageAt(Terminal terminal, int col, int screenRow)
        => PlacementAt(terminal, col, screenRow) is not null;

    /// <summary>
    /// Which piece of its picture a screen position shows, as the source pixel it starts at.
    /// </summary>
    /// <remarks>
    /// The replacement for <c>cell.ImageCol</c> and <c>cell.ImageRow</c>. A run no longer numbers
    /// tiles -- it carries the source rectangle directly -- so "which tile is this" becomes "which
    /// pixels does this column read from", which is the same question one level down and the one
    /// the renderer actually asks.
    /// </remarks>
    public static (int X, int Y)? SourceAt(Terminal terminal, int col, int screenRow)
    {
        if (PlacementAt(terminal, col, screenRow) is not { } placement)
            return null;

        // Runs divide their source evenly across their columns, so the offset of this column within
        // the run is the offset of its pixels within the source rectangle.
        var perCell = placement.Cols > 0 ? (double)placement.SrcWidth / placement.Cols : 0;
        return (placement.SrcX + (int)System.Math.Round((col - placement.Column) * perCell),
                placement.SrcY);
    }

    /// <summary>How many screen rows one placement covers, found by its serial.</summary>
    /// <remarks>
    /// The replacement for asking a placement how many rows it had. A run is one line of a picture,
    /// so the height is a property of the set of runs sharing a serial rather than of any one.
    /// </remarks>
    public static int RowsOf(Terminal terminal, int serial)
    {
        var rows = 0;
        for (var row = 0; row < terminal.Rows; row++)
        {
            if (PlacementsOn(terminal, row).Any(p => p.Serial == serial))
                rows++;
        }
        return rows;
    }

    /// <summary>
    /// Which tile of its picture a screen position shows, in the picture's own cell grid.
    /// </summary>
    /// <remarks>
    /// The direct replacement for <c>cell.ImageCol</c> and <c>cell.ImageRow</c>. Runs carry source
    /// pixels rather than tile numbers, so this divides back down by the image's cell size — which
    /// is what a tile number always was.
    /// </remarks>
    public static (int Col, int Row)? TileAt(Terminal terminal, int col, int screenRow)
    {
        if (SourceAt(terminal, col, screenRow) is not { } source)
            return null;

        var image = ImageAt(terminal, col, screenRow);
        if (image is null || image.CellWidth <= 0 || image.CellHeight <= 0)
            return null;

        return (source.X / image.CellWidth, source.Y / image.CellHeight);
    }

    /// <summary>The Sixel pixels on a screen row, or null when no Sixel has painted it.</summary>
    public static SixelStrip? StripOn(Terminal terminal, int screenRow)
        => terminal.Buffer.Lines[terminal.Buffer.YBase + screenRow]?.Sixel;

    /// <summary>
    /// Whether a screen position shows Sixel pixels -- the question <see cref="IsImageAt"/> asks of a
    /// Kitty run, asked of the pixels Sixel paints instead.
    /// </summary>
    public static bool IsSixelAt(Terminal terminal, int col, int screenRow)
    {
        var line = terminal.Buffer.Lines[terminal.Buffer.YBase + screenRow];
        return line?.Sixel is { } strip && col < line.Length && strip.HasPixelsAt(col);
    }

    /// <summary>
    /// The Sixel pixel at (<paramref name="x"/>, <paramref name="y"/>) inside a cell, as RGBA, or null
    /// when the cell shows none.
    /// </summary>
    public static (byte R, byte G, byte B, byte A)? SixelPixel(Terminal terminal, int col, int screenRow, int x = 0, int y = 0)
    {
        if (!IsSixelAt(terminal, col, screenRow))
            return null;

        var strip = StripOn(terminal, screenRow)!;
        var p = StripPixels(strip);
        var offset = y * strip.Stride + (col * strip.CellWidth + x) * TerminalImage.BytesPerPixel;
        return (p[offset + 2], p[offset + 1], p[offset], p[offset + 3]);
    }

    /// <summary>A strip's pixels laid out contiguously, BGRA, <see cref="SixelStrip.Stride"/> bytes a row.</summary>
    public static byte[] StripPixels(SixelStrip strip)
    {
        var pixels = new byte[strip.Stride * strip.PixelHeight];
        strip.CopyPixelsTo(pixels, strip.Stride, strip.Columns);
        return pixels;
    }

    /// <summary>How many cells of the buffer show Sixel pixels, clipped to each line's width.</summary>
    public static int VisibleSixelCells(Terminal terminal)
    {
        var count = 0;
        for (var i = 0; i < terminal.Buffer.Lines.Length; i++)
        {
            var line = terminal.Buffer.Lines[i];
            if (line?.Sixel is not { } strip)
                continue;

            for (var c = 0; c < System.Math.Min(strip.Columns, line.Length); c++)
                if (strip.HasPixelsAt(c))
                    count++;
        }

        return count;
    }

    /// <summary>Every run on a screen row, in the order they were placed.</summary>
    public static IReadOnlyList<LinePlacement> PlacementsOn(Terminal terminal, int screenRow)
    {
        var line = terminal.Buffer.Lines[terminal.Buffer.YBase + screenRow];
        return line is null ? System.Array.Empty<LinePlacement>() : line.Placements;
    }

    /// <summary>Every run covering a screen position, front to back by z-index.</summary>
    /// <remarks>
    /// Stacking order, which two overlapping pictures need and a single "what is here" cannot give.
    /// Ordered by z and then by age, age being the order they were added to the line -- so a stable
    /// sort on z alone is the whole rule.
    /// </remarks>
    public static List<LinePlacement> StackAt(Terminal terminal, int col, int screenRow)
        => PlacementsOn(terminal, screenRow)
            .Where(p => p.Covers(col))
            .OrderByDescending(p => p.ZIndex)
            .ThenByDescending(p => p.Serial)
            .ToList();

    /// <summary>
    /// How many cells in the whole buffer show part of a picture.
    /// </summary>
    /// <remarks>
    /// Counted from the runs rather than by testing cells, and clamped to each line's width — which
    /// is the point of the model: a run keeps its natural width, so this reports what is VISIBLE at
    /// the current size while nothing wider has been destroyed.
    /// </remarks>
    public static int VisibleImageCells(Terminal terminal)
    {
        var count = 0;

        for (var i = 0; i < terminal.Buffer.Lines.Length; i++)
        {
            var line = terminal.Buffer.Lines[i];
            if (line is null || !line.HasImages)
                continue;

            foreach (var placement in line.Placements)
            {
                var end = System.Math.Min(placement.EndColumn, line.Length);
                count += System.Math.Max(0, end - placement.Column);
            }
        }

        return count;
    }

    /// <summary>
    /// How many cells the buffer's pictures cover at their natural width, visible or not.
    /// </summary>
    /// <remarks>
    /// The difference between this and <see cref="VisibleImageCells"/> is exactly what a narrow
    /// window is hiding rather than destroying — which is the property that used to require hiding
    /// an overhang and reviving it, and now falls out of the storage.
    /// </remarks>
    public static int TotalImageCells(Terminal terminal)
    {
        var count = 0;

        for (var i = 0; i < terminal.Buffer.Lines.Length; i++)
        {
            var line = terminal.Buffer.Lines[i];
            if (line is null || !line.HasImages)
                continue;

            foreach (var placement in line.Placements)
                count += placement.Cols;
        }

        return count;
    }
}
