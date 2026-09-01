using XTerm.Buffer;
using XTerm.Common;
using XTerm.Parser;

namespace XTerm;

/// <summary>
/// The DEC rectangular-area operations: copy, fill, erase and the two that change attributes
/// rather than characters (DECCRA, DECFRA, DECERA, DECSERA, DECCARA, DECRARA). One file because
/// they share one coordinate discipline, spelled out on <see cref="TryReadRectangle"/>.
/// </summary>
public partial class InputHandler
{
    /// <summary>
    /// Reads a rectangle from four parameters starting at <paramref name="first"/>, in the
    /// discipline every DEC rectangle operation shares: coordinates are 1-based and inclusive,
    /// interpreted in the ORIGIN MODE coordinate system (a rectangle is addressed the same way a
    /// cursor is), clipped to the screen, and never clipped to the margins -- DECFRA across a
    /// DECSLRM pane fills straight through it, which is what "ignores margins" means in the
    /// standard. Missing values mean the whole screen. A rectangle whose bottom is above its top
    /// or right is left of its left, AFTER origin translation, refuses the whole operation.
    /// </summary>
    /// <returns>False when the operation must do nothing; the bounds are 0-based inclusive.</returns>
    private bool TryReadRectangle(Params parameters, int first,
                                  out int top, out int left, out int bottom, out int right)
    {
        var originX = _terminal.OriginMode ? _buffer.ScrollLeft : 0;
        var originY = _terminal.OriginMode ? _buffer.ScrollTop : 0;

        // An explicit 0 means the default, exactly as an absent parameter does.
        var t = parameters.GetParam(first, 0);
        var l = parameters.GetParam(first + 1, 0);
        var b = parameters.GetParam(first + 2, 0);
        var r = parameters.GetParam(first + 3, 0);

        top = originY + (t <= 0 ? 1 : t) - 1;
        left = originX + (l <= 0 ? 1 : l) - 1;
        bottom = originY + (b <= 0 ? _terminal.Rows - originY : b) - 1;
        right = originX + (r <= 0 ? _terminal.Cols - originX : r) - 1;

        bottom = Math.Min(bottom, _terminal.Rows - 1);
        right = Math.Min(right, _terminal.Cols - 1);

        return top >= 0 && left >= 0 && top <= bottom && left <= right;
    }

    /// <summary>
    /// Whether the DEC rectangular-editing controls are available at the current operating level.
    /// </summary>
    /// <remarks>
    /// <para>VT400 and up, which is the gate xterm puts on every one of them --
    /// <c>screen-&gt;vtXX_level &gt;= 4</c> on DECCRA, DECERA, DECFRA, DECSERA, DECCARA, DECRARA
    /// and DECRQCRA alike. It is also what this terminal's own primary DA already says: attribute
    /// 28, rectangular editing, is advertised only from level 64. Acting on the controls at a level
    /// where the DA reply denies them is the terminal contradicting itself, and a program that
    /// lowered the level with DECSCL specifically to be treated as older hardware has asked not to
    /// be given them.</para>
    /// <para>DECSACE is deliberately NOT gated, which is xterm's asymmetry rather than an oversight
    /// on this side: its handler has no level test where every neighbour does. Storing which extent
    /// a program would prefer costs nothing and changes nothing on its own -- the two controls that
    /// read it are gated here, so a stored preference below level 64 simply never gets used.</para>
    /// </remarks>
    private bool RectangularEditingAvailable => _terminal.ConformanceLevel >= 64;

    /// <summary>DECFRA -- fills the rectangle with one character, in the CURRENT rendition.</summary>
    /// <remarks>
    /// The character must be printable -- xterm accepts 32..126 and 160 up -- and an
    /// unprintable request refuses the whole operation rather than filling with garbage.
    /// The cursor does not move: a rectangle operation is not a print.
    /// </remarks>
    private void FillRectangularArea(Params parameters)
    {
        if (!RectangularEditingAvailable)
            return;

        var ch = parameters.GetParam(0, 0);
        if (ch < 32 || (ch > 126 && ch < 160))
            return;
        if (!TryReadRectangle(parameters, 1, out var top, out var left, out var bottom, out var right))
            return;

        var cell = new BufferCell(char.ConvertFromUtf32(ch), 1, _curAttr);
        FillCells(top, left, bottom, right, ref cell);
    }

    /// <summary>DECERA -- erases the rectangle to blanks, with the erase attributes.</summary>
    private void EraseRectangularArea(Params parameters)
    {
        if (!RectangularEditingAvailable)
            return;
        if (!TryReadRectangle(parameters, 0, out var top, out var left, out var bottom, out var right))
            return;

        var cell = new BufferCell(" ", 1, GetEraseAttributes());
        FillCells(top, left, bottom, right, ref cell);
    }

    /// <summary>
    /// DECSERA (CSI Pt;Pl;Pb;Pr $ {). Like DECERA, but DECSCA-protected characters survive.
    /// Only DECSCA counts here: ISO SPA/EPA guards do NOT stop it -- the selective erases and
    /// the guarded erases are separate systems, and this one belongs to DECSCA.
    /// </summary>
    private void SelectiveEraseRectangularArea(Params parameters)
    {
        if (!RectangularEditingAvailable)
            return;
        if (!TryReadRectangle(parameters, 0, out var top, out var left, out var bottom, out var right))
            return;

        var blank = new BufferCell(" ", 1, GetEraseAttributes());
        for (var row = top; row <= bottom; row++)
        {
            var line = _buffer.Lines[_buffer.YBase + row];
            if (line is null)
                continue;

            for (var col = left; col <= right && col < line.Length; col++)
            {
                if (line[col].Attributes.IsProtected())
                    continue;
                line.SetCell(col, ref blank);
            }
        }
    }

    private void FillCells(int top, int left, int bottom, int right, ref BufferCell cell)
    {
        for (var row = top; row <= bottom; row++)
        {
            var line = _buffer.Lines[_buffer.YBase + row];
            if (line is null)
                continue;

            for (var col = left; col <= right && col < line.Length; col++)
                line.SetCell(col, ref cell);
        }
    }

    /// <summary>
    /// DECCRA -- copies a rectangle, cells and attributes together, to a destination named by its
    /// top-left corner.
    /// </summary>
    /// <remarks>
    /// The source is SNAPSHOTTED before a cell is written, which is the whole of what makes an
    /// overlapping copy correct: copying in-place in either direction smears the region across
    /// itself for one of the two overlap orders. The page parameters are accepted and ignored --
    /// there is one page. A destination hanging off the screen edge is clipped, not refused: the
    /// part that fits is the part that copies.
    /// </remarks>
    private void CopyRectangularArea(Params parameters)
    {
        if (!RectangularEditingAvailable)
            return;
        if (!TryReadRectangle(parameters, 0, out var top, out var left, out var bottom, out var right))
            return;

        // parameters[4] is the source page. Destination: top;left (1-based, origin-relative),
        // parameters[7] the destination page.
        var originX = _terminal.OriginMode ? _buffer.ScrollLeft : 0;
        var originY = _terminal.OriginMode ? _buffer.ScrollTop : 0;
        var dt = parameters.GetParam(5, 0);
        var dl = parameters.GetParam(6, 0);
        var destTop = originY + (dt <= 0 ? 1 : dt) - 1;
        var destLeft = originX + (dl <= 0 ? 1 : dl) - 1;

        var rows = bottom - top + 1;
        var cols = right - left + 1;

        var snapshot = new BufferCell[rows, cols];
        for (var row = 0; row < rows; row++)
        {
            var line = _buffer.Lines[_buffer.YBase + top + row];
            for (var col = 0; col < cols; col++)
                snapshot[row, col] = line is not null && left + col < line.Length
                    ? line[left + col]
                    : new BufferCell(" ", 1, AttributeData.Default);
        }

        for (var row = 0; row < rows; row++)
        {
            var destRow = destTop + row;
            if (destRow < 0 || destRow >= _terminal.Rows)
                continue;

            var line = _buffer.Lines[_buffer.YBase + destRow];
            if (line is null)
                continue;

            for (var col = 0; col < cols; col++)
            {
                var destCol = destLeft + col;
                if (destCol < 0 || destCol >= _terminal.Cols || destCol >= line.Length)
                    continue;

                var cell = snapshot[row, col];
                line.SetCell(destCol, ref cell);
            }
        }
    }

    /// <summary>
    /// What the parameter list asks of one attribute. Toggling twice is the same as not asking,
    /// which is why this composes rather than accumulating a list.
    /// </summary>
    private enum AreaAttributeOp : byte { None, Set, Clear, Toggle }

    /// <summary>The five attributes DECCARA and DECRARA can name, in the order the ops are held.</summary>
    private const int AreaBold = 0, AreaUnderline = 1, AreaBlink = 2, AreaInverse = 3, AreaInvisible = 4;

    /// <summary>
    /// DECCARA (<c>CSI Pt;Pl;Pb;Pr;Pm $ r</c>) and DECRARA (<c>CSI Pt;Pl;Pb;Pr;Pm $ t</c>) -- set
    /// or toggle the named SGR attributes over an area, leaving the characters alone.
    /// </summary>
    /// <remarks>
    /// <para>The attribute half of the rectangle family, and the only consumer DECSACE has.</para>
    /// <para>DECSACE 2 means the RECTANGLE the four coordinates describe. Anything else -- the
    /// default included -- means the STREAM running from the top-left position to the bottom-right
    /// one, so the first row runs from its column to the end of the line, the last row from the
    /// start of the line to its column, and every row between them runs whole.</para>
    /// <para>Only the six attributes DEC defines are touched: 1 bold, 4 underline, 5 blink,
    /// 7 inverse and their resets 22, 24, 25, 27, plus xterm's 8/28 for invisible. Parameter 0
    /// means the first four together -- NOT invisible, which xterm leaves out of its SGR_MASK --
    /// and reverses rather than clears them under DECRARA. Everything else in the list is ignored;
    /// colours are not in the standard, and honouring an SGR parameter here that a real VT420 would
    /// not is how a program's careful rectangle ends up recoloured on one terminal only.</para>
    /// <para>The list is read ONCE, into one op per attribute, rather than re-walked for every
    /// cell: the answer cannot vary across the area, and a full-screen request asked the same
    /// question a parameter at a time for every one of its cells. Reading it first is also what
    /// makes the next paragraph possible.</para>
    /// <para>A request that changes nothing -- <c>CSI 1;1;1;10;31 $ r</c>, naming only a colour
    /// this does not implement, or a DECRARA whose toggles cancel -- returns before a cell is
    /// touched. That is NOT an optimisation. Writing a cell back unchanged still counts as writing
    /// it, and the write path splits any Sixel or Kitty placement covering that column, on the
    /// reasonable assumption that a cell being written is a cell whose character is changing. Here
    /// it never is: these two controls change rendition and nothing else, so the cells that DO
    /// change go back through the INDEXER, which stores the cell and invalidates the render cache
    /// without disturbing the picture over it.</para>
    /// <para>Every cell in the area is marked, the trailing half of a wide character included. xterm
    /// skips cells it has never drawn -- it tracks that per cell, and a blank it has never touched
    /// is not a blank it will colour -- but a line here is born full of spaces, so there is no such
    /// state to test for and the only cell that reads as empty is a wide character's second half.
    /// Skipping THAT would leave a character's two halves disagreeing about their own rendition.</para>
    /// </remarks>
    private void MarkRectangularArea(Params parameters, bool reverse)
    {
        // VT400 and up, with the rest of the family; see RectangularEditingAvailable.
        if (!RectangularEditingAvailable)
            return;

        Span<AreaAttributeOp> ops = stackalloc AreaAttributeOp[5];
        if (!ReadAreaAttributeOps(parameters, 4, ops, reverse))
            return;

        if (!TryReadRectangle(parameters, 0, out var top, out var left, out var bottom, out var right))
            return;

        var exact = _attributeChangeExtent == 2;

        for (var row = top; row <= bottom; row++)
        {
            var line = _buffer.Lines[_buffer.YBase + row];
            if (line is null)
                continue;

            var from = exact || row == top ? left : 0;
            var to = exact || row == bottom ? right : _terminal.Cols - 1;

            for (var col = from; col <= to && col < line.Length; col++)
            {
                var cell = line[col];
                ApplyAreaAttributeOps(ops, ref cell.Attributes);

                // The indexer, NOT SetCell: see the remarks. SetCell is the text-write path and
                // splits this line's placements, so a rendition change over a picture would punch
                // a hole in it.
                line[col] = cell;
            }
        }
    }

    /// <summary>
    /// Reads the DECCARA/DECRARA parameter list into one operation per attribute.
    /// </summary>
    /// <remarks>
    /// Composing rather than appending is what keeps one pass faithful to the list's order: a later
    /// parameter overrides an earlier one for the same attribute, and a toggle applied to a pending
    /// toggle cancels it -- exactly as xterm's per-cell XOR does when the same bit is named twice.
    /// </remarks>
    /// <returns>False when the list would change nothing, so the caller can touch no cells at all.</returns>
    private static bool ReadAreaAttributeOps(Params parameters, int first, Span<AreaAttributeOp> ops, bool reverse)
    {
        var on = reverse ? AreaAttributeOp.Toggle : AreaAttributeOp.Set;
        var off = reverse ? AreaAttributeOp.Toggle : AreaAttributeOp.Clear;

        for (var i = first; i < parameters.Length; i++)
        {
            switch (parameters.GetParam(i, 0))
            {
                case 0:
                    // xterm's SGR_MASK: bold, underline, blink and inverse -- not invisible, which
                    // has its own 8 and 28.
                    Note(ops, AreaBold, off);
                    Note(ops, AreaUnderline, off);
                    Note(ops, AreaBlink, off);
                    Note(ops, AreaInverse, off);
                    break;
                case 1: Note(ops, AreaBold, on); break;
                case 4: Note(ops, AreaUnderline, on); break;
                case 5: Note(ops, AreaBlink, on); break;
                case 7: Note(ops, AreaInverse, on); break;
                case 8: Note(ops, AreaInvisible, on); break;
                // The resets have no meaning under DECRARA -- reversing an attribute already says
                // both directions -- so xterm reads them only when setting, and so does this.
                case 22 when !reverse: Note(ops, AreaBold, AreaAttributeOp.Clear); break;
                case 24 when !reverse: Note(ops, AreaUnderline, AreaAttributeOp.Clear); break;
                case 25 when !reverse: Note(ops, AreaBlink, AreaAttributeOp.Clear); break;
                case 27 when !reverse: Note(ops, AreaInverse, AreaAttributeOp.Clear); break;
                case 28 when !reverse: Note(ops, AreaInvisible, AreaAttributeOp.Clear); break;
            }
        }

        foreach (var op in ops)
        {
            if (op != AreaAttributeOp.None)
                return true;
        }

        return false;
    }

    /// <summary>Folds one parameter's request into what is already asked of that attribute.</summary>
    private static void Note(Span<AreaAttributeOp> ops, int attribute, AreaAttributeOp op) =>
        ops[attribute] = op is not AreaAttributeOp.Toggle
            ? op
            : ops[attribute] switch
            {
                AreaAttributeOp.None => AreaAttributeOp.Toggle,
                AreaAttributeOp.Toggle => AreaAttributeOp.None,
                AreaAttributeOp.Set => AreaAttributeOp.Clear,
                _ => AreaAttributeOp.Set,
            };

    /// <summary>Applies the ops read by <see cref="ReadAreaAttributeOps"/> to one cell's rendition.</summary>
    private static void ApplyAreaAttributeOps(ReadOnlySpan<AreaAttributeOp> ops, ref AttributeData attributes)
    {
        switch (ops[AreaBold])
        {
            case AreaAttributeOp.Set: attributes.SetBold(true); break;
            case AreaAttributeOp.Clear: attributes.SetBold(false); break;
            case AreaAttributeOp.Toggle: attributes.SetBold(!attributes.IsBold()); break;
        }

        switch (ops[AreaUnderline])
        {
            case AreaAttributeOp.Set: attributes.SetUnderline(true); break;
            case AreaAttributeOp.Clear: attributes.SetUnderline(false); break;
            case AreaAttributeOp.Toggle: attributes.SetUnderline(!attributes.IsUnderline()); break;
        }

        switch (ops[AreaBlink])
        {
            case AreaAttributeOp.Set: attributes.SetBlink(true); break;
            case AreaAttributeOp.Clear: attributes.SetBlink(false); break;
            case AreaAttributeOp.Toggle: attributes.SetBlink(!attributes.IsBlink()); break;
        }

        switch (ops[AreaInverse])
        {
            case AreaAttributeOp.Set: attributes.SetInverse(true); break;
            case AreaAttributeOp.Clear: attributes.SetInverse(false); break;
            case AreaAttributeOp.Toggle: attributes.SetInverse(!attributes.IsInverse()); break;
        }

        switch (ops[AreaInvisible])
        {
            case AreaAttributeOp.Set: attributes.SetInvisible(true); break;
            case AreaAttributeOp.Clear: attributes.SetInvisible(false); break;
            case AreaAttributeOp.Toggle: attributes.SetInvisible(!attributes.IsInvisible()); break;
        }
    }
}
