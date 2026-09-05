namespace XTerm.Graphics;

/// <summary>
/// Which semantics a placement follows when text is written over it.
/// </summary>
public enum PlacementKind
{
    /// <summary>
    /// Sixel: printing replaces that part of the picture, so a write into a placement's span splits
    /// it around the written columns.
    /// </summary>
    Sixel = 0,

    /// <summary>
    /// Kitty graphics: the z-index decides, and text may draw over or under. A write does not modify
    /// the placement at all.
    /// </summary>
    Kitty = 1,

    /// <summary>
    /// Kitty graphics shown by Unicode placeholder cells: the cell IS the picture, so a write into
    /// it takes the tile with it, exactly as Sixel content behaves.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="Kitty"/> because the two protocols disagree about what a write
    /// means. A classic placement is an overlay and survives text; a placeholder tile exists
    /// BECAUSE the cell holds the placeholder character, so a client erases a picture by
    /// overwriting its cells and nothing else — there is no escape sequence for "remove the tile
    /// at this cell". Left as an overlay, a dialog drawn across a picture kept the picture on top
    /// of it, permanently.
    /// </remarks>
    Placeholder = 2,
}

/// <summary>
/// A run of one image shown on one line — the storage that replaces scattering a picture's tiles
/// across cells.
/// </summary>
/// <remarks>
/// <para>A picture spanning eight rows is eight placements, one per line, each with its own
/// <see cref="SrcY"/>. Keeping every placement line-local is what lets ownership, scrolling and
/// scrollback eviction go on working exactly as they did when cells held the tiles: the line is
/// still the thing that owns a picture and still the thing whose death releases it.</para>
///
/// <para><b><see cref="Cols"/> is the natural width and is never clipped.</b> The renderer draws
/// <c>min(Cols, line width)</c>. That single decision is what makes a resize a no-op for images:
/// narrowing shows less of the picture, widening shows more, and nothing is destroyed or restored
/// because the pixels were never in the grid. It replaces hiding an overhang on narrow, reviving
/// tiles on widen, and pruning ownership after a sweep.</para>
/// </remarks>
public readonly struct LinePlacement
{
    /// <summary>The image this shows, by <see cref="TerminalImage.Id"/>.</summary>
    public readonly int ImageId;

    /// <summary>
    /// The placement's own id, for protocols that address placements separately from images
    /// (Kitty's <c>p=</c>). Zero for Sixel, which has no way to refer to one.
    /// </summary>
    public readonly int PlacementId;

    /// <summary>The column on this line where the run starts.</summary>
    public readonly int Column;

    /// <summary>
    /// How many cells the run covers if it is fully visible. Never clipped to the line's width —
    /// see the remarks on this type for why that is the whole point.
    /// </summary>
    public readonly int Cols;

    /// <summary>The source rectangle within the image, in pixels.</summary>
    public readonly int SrcX;
    public readonly int SrcY;
    public readonly int SrcWidth;
    public readonly int SrcHeight;

    /// <summary>Pixel offset within the starting cell (Kitty's <c>X</c> and <c>Y</c>).</summary>
    public readonly short OffsetX;
    public readonly short OffsetY;

    /// <summary>Draw order against text. Negative draws beneath glyphs (Kitty's <c>z</c>).</summary>
    public readonly short ZIndex;

    /// <summary>How this placement behaves when text is written across it.</summary>
    public readonly PlacementKind Kind;

    /// <summary>
    /// How many image pixels one CELL of this placement covers, per axis. The renderer's whole
    /// scaling question in two numbers: a natural placement covers <see cref="TerminalImage.CellWidth"/>
    /// pixels per cell (one image pixel per screen-cell pixel), a stretched one covers
    /// SourceWidth / Cols — its share of the box. Zero means natural, for placements created
    /// before these fields existed; readers fall back to the image's cell metric.
    /// </summary>
    public readonly float PxPerCellX;
    public readonly float PxPerCellY;

    /// <summary>
    /// Which placement this run is part of. Every row of one picture shares it, and no two
    /// placements ever share one.
    /// </summary>
    /// <remarks>
    /// <para>Not the same thing as <see cref="PlacementId"/>. That one is the CLIENT's, from Kitty's
    /// <c>p=</c>: it is zero when the client named none, and several placements may carry the same
    /// value. This is the TERMINAL's, and it is what makes "delete the picture at this cell" mean
    /// the whole picture rather than the one row the cell is on.</para>
    /// <para>A run is a line-local thing by design, so a placement spanning eight rows is eight
    /// unrelated structs; before pictures were runs, the placement was one object and reference
    /// identity answered this for free. This is what replaces that.</para>
    /// </remarks>
    public readonly int Serial;

    private static int _nextSerial;

    /// <summary>Takes the next serial, for one placement however many rows it covers.</summary>
    public static int NextSerial() => System.Threading.Interlocked.Increment(ref _nextSerial);

    public LinePlacement(
        int imageId,
        int column,
        int cols,
        int srcX,
        int srcY,
        int srcWidth,
        int srcHeight,
        PlacementKind kind = PlacementKind.Sixel,
        int placementId = 0,
        short offsetX = 0,
        short offsetY = 0,
        short zIndex = 0,
        int serial = 0,
        float pxPerCellX = 0,
        float pxPerCellY = 0)
    {
        PxPerCellX = pxPerCellX;
        PxPerCellY = pxPerCellY;
        ImageId = imageId;
        Column = column;
        Cols = cols;
        SrcX = srcX;
        SrcY = srcY;
        SrcWidth = srcWidth;
        SrcHeight = srcHeight;
        Kind = kind;
        PlacementId = placementId;
        OffsetX = offsetX;
        OffsetY = offsetY;
        ZIndex = zIndex;
        Serial = serial;
    }

    /// <summary>One past the last column this run covers when fully visible.</summary>
    public int EndColumn => Column + Cols;

    /// <summary>Whether <paramref name="column"/> falls inside this run.</summary>
    public bool Covers(int column) => column >= Column && column < EndColumn;

    /// <summary>
    /// The part of this run left of <paramref name="column"/>, with its source rectangle narrowed to
    /// match. Used when text is printed into the middle of a Sixel picture.
    /// </summary>
    public LinePlacement TruncatedBefore(int column)
    {
        var cols = System.Math.Max(0, column - Column);
        return WithColumns(Column, cols, SrcX);
    }

    /// <summary>
    /// The part of this run right of <paramref name="column"/>, with its source rectangle advanced to
    /// match.
    /// </summary>
    public LinePlacement TruncatedAfter(int column)
    {
        var start = column + 1;
        var cols = System.Math.Max(0, EndColumn - start);
        var skipped = start - Column;

        // Source width per cell, so the remaining rectangle starts where the dropped cells ended.
        var perCell = Cols > 0 ? (double)SrcWidth / Cols : 0;
        return WithColumns(start, cols, SrcX + (int)System.Math.Round(skipped * perCell));
    }

    private LinePlacement WithColumns(int column, int cols, int srcX)
    {
        var perCell = Cols > 0 ? (double)SrcWidth / Cols : 0;
        var width = (int)System.Math.Round(cols * perCell);

        return new LinePlacement(
            ImageId, column, cols,
            srcX, SrcY,
            System.Math.Min(width, System.Math.Max(0, SrcX + SrcWidth - srcX)), SrcHeight,
            Kind, PlacementId, OffsetX, OffsetY, ZIndex, Serial, PxPerCellX, PxPerCellY);
    }
}
