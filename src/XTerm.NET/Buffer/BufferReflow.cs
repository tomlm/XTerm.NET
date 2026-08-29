// Ported from xterm.js BufferReflow.ts, (c) The xterm.js authors, MIT

namespace XTerm.Buffer;

/// <summary>
/// Result of creating a new buffer layout after reflowing larger.
/// </summary>
public readonly struct NewLayoutResult
{
    public int[] Layout { get; init; }
    public int CountRemoved { get; init; }
}

/// <summary>
/// Pure reflow helpers ported from xterm.js.
/// </summary>
public static class BufferReflow
{
    internal readonly record struct LogicalMark(int Offset, LineMark Mark);
    internal readonly record struct LogicalLink(int Offset, int Length, string Url, string? Id);

    internal sealed class ReflowMetadata
    {
        public List<LogicalMark> Marks { get; } = new();
        public List<LogicalLink> Links { get; } = new();
    }

    /// <summary>
    /// Evaluates indexes of rows to remove after a reflow-larger operation.
    /// </summary>
    public static int[] ReflowLargerGetLinesToRemove(
        CircularList<BufferLine> lines,
        int oldCols,
        int newCols,
        int bufferAbsoluteY,
        BufferCell nullCell)
    {
        var toRemove = new List<int>();

        for (var y = 0; y < lines.Length - 1; y++)
        {
            var i = y;
            var nextLine = lines[++i];
            if (nextLine is not { IsWrapped: true })
            {
                continue;
            }

            var wrappedLines = new List<BufferLine> { lines[y]! };
            while (i < lines.Length && nextLine is { IsWrapped: true })
            {
                wrappedLines.Add(nextLine);
                nextLine = ++i < lines.Length ? lines[i] : null;
            }

            if (IsUnreflowable(wrappedLines))
            {
                y += wrappedLines.Count - 1;
                continue;
            }

            if (bufferAbsoluteY >= y && bufferAbsoluteY < i)
            {
                y += wrappedLines.Count - 1;
                continue;
            }

            var metadata = CaptureMetadata(wrappedLines, oldCols);
            // The helper describes the same logical cell stream in either resize direction: it
            // advances by newCols and shortens only at a wide-cell boundary. With newCols larger
            // it therefore yields the exact rows the merge loop below produces as well.
            var destinationLineLengths = metadata is null
                ? null
                : ReflowSmallerGetNewLineLengths(wrappedLines, oldCols, newCols);

            var destLineIndex = 0;
            var destCol = GetWrappedLineTrimmedLength(wrappedLines, destLineIndex, oldCols);
            var srcLineIndex = 1;
            var srcCol = 0;
            while (srcLineIndex < wrappedLines.Count)
            {
                var srcTrimmedLineLength = GetWrappedLineTrimmedLength(wrappedLines, srcLineIndex, oldCols);
                var srcRemainingCells = srcTrimmedLineLength - srcCol;
                var destRemainingCells = newCols - destCol;
                var cellsToCopy = Math.Min(srcRemainingCells, destRemainingCells);

                wrappedLines[destLineIndex].CopyCellsFrom(wrappedLines[srcLineIndex], srcCol, destCol, cellsToCopy, false);

                destCol += cellsToCopy;
                if (destCol == newCols)
                {
                    destLineIndex++;
                    destCol = 0;
                }
                srcCol += cellsToCopy;
                if (srcCol == srcTrimmedLineLength)
                {
                    srcLineIndex++;
                    srcCol = 0;
                }

                if (destCol == 0 && destLineIndex != 0)
                {
                    if (wrappedLines[destLineIndex - 1].GetWidth(newCols - 1) == 2)
                    {
                        wrappedLines[destLineIndex].CopyCellsFrom(wrappedLines[destLineIndex - 1], newCols - 1, destCol++, 1, false);
                        var nullAtEnd = nullCell;
                        wrappedLines[destLineIndex - 1].SetCell(newCols - 1, ref nullAtEnd);
                    }
                }
            }

            wrappedLines[destLineIndex].ReplaceCells(destCol, newCols, nullCell);
            if (metadata is not null)
                RestoreMetadata(wrappedLines, destinationLineLengths!, metadata);

            var countToRemove = 0;
            for (var removeIndex = wrappedLines.Count - 1; removeIndex > 0; removeIndex--)
            {
                if (removeIndex > destLineIndex || wrappedLines[removeIndex].GetTrimmedLength() == 0)
                {
                    countToRemove++;
                }
                else
                {
                    break;
                }
            }

            if (countToRemove > 0)
            {
                toRemove.Add(y + wrappedLines.Count - countToRemove);
                toRemove.Add(countToRemove);
            }

            y += wrappedLines.Count - 1;
        }

        return toRemove.ToArray();
    }

    /// <summary>
    /// Creates the new layout for lines given indexes to remove.
    /// </summary>
    public static NewLayoutResult ReflowLargerCreateNewLayout(CircularList<BufferLine> lines, int[] toRemove)
    {
        var layout = new List<int>();
        var nextToRemoveIndex = 0;
        var nextToRemoveStart = toRemove.Length > 0 ? toRemove[nextToRemoveIndex] : -1;
        var countRemovedSoFar = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            if (nextToRemoveStart == i)
            {
                var countToRemove = toRemove[++nextToRemoveIndex];
                i += countToRemove - 1;
                countRemovedSoFar += countToRemove;
                nextToRemoveStart = ++nextToRemoveIndex < toRemove.Length ? toRemove[nextToRemoveIndex] : -1;
            }
            else
            {
                layout.Add(i);
            }
        }

        return new NewLayoutResult
        {
            Layout = layout.ToArray(),
            CountRemoved = countRemovedSoFar
        };
    }

    /// <summary>
    /// Applies a new layout to the buffer in a single pass.
    /// </summary>
    public static void ReflowLargerApplyNewLayout(CircularList<BufferLine> lines, int[] newLayout)
    {
        var newLayoutLines = new BufferLine[newLayout.Length];
        for (var i = 0; i < newLayout.Length; i++)
        {
            newLayoutLines[i] = lines[newLayout[i]]!;
        }

        for (var i = 0; i < newLayoutLines.Length; i++)
        {
            lines[i] = newLayoutLines[i];
        }

        lines.SetLength(newLayout.Length);
    }

    /// <summary>
    /// Gets new line lengths for a wrapped line group when shrinking columns.
    /// </summary>
    public static int[] ReflowSmallerGetNewLineLengths(
        IReadOnlyList<BufferLine> wrappedLines,
        int oldCols,
        int newCols)
    {
        var newLineLengths = new List<int>();
        var cellsNeeded = 0;
        for (var i = 0; i < wrappedLines.Count; i++)
        {
            cellsNeeded += GetWrappedLineTrimmedLength(wrappedLines, i, oldCols);
        }

        var srcCol = 0;
        var srcLine = 0;
        var cellsAvailable = 0;
        while (cellsAvailable < cellsNeeded)
        {
            if (cellsNeeded - cellsAvailable < newCols)
            {
                newLineLengths.Add(cellsNeeded - cellsAvailable);
                break;
            }

            srcCol += newCols;
            var oldTrimmedLength = GetWrappedLineTrimmedLength(wrappedLines, srcLine, oldCols);
            if (srcCol > oldTrimmedLength)
            {
                srcCol -= oldTrimmedLength;
                srcLine++;
            }

            // The newCols > 1 test is what keeps this loop finite. At a single column, a wide
            // boundary made lineLength zero, cellsAvailable never advanced, and the while loop
            // appended empty rows until the list threw OutOfMemoryException -- a hang followed by a
            // crash, from nothing worse than dragging a pane to one column.
            //
            // A wide glyph cannot be shown in one column under any layout, so there is no width to
            // reserve for it and the character is clipped. That is the only available answer; the
            // bug was pretending otherwise and making no progress instead.
            var endsWithWide = newCols > 1 && wrappedLines[srcLine].GetWidth(srcCol - 1) == 2;
            if (endsWithWide)
            {
                srcCol--;
            }

            var lineLength = endsWithWide ? newCols - 1 : newCols;
            newLineLengths.Add(lineLength);
            cellsAvailable += lineLength;
        }

        return newLineLengths.ToArray();
    }

    /// <summary>
    /// Captures line-owned metadata in the same logical coordinate space used to redistribute cells.
    /// </summary>
    internal static ReflowMetadata? CaptureMetadata(
        IReadOnlyList<BufferLine> lines, int cols)
    {
        ReflowMetadata? result = null;
        var lineOffset = 0;
        for (var i = 0; i < lines.Count; i++)
        {
            var length = GetWrappedLineTrimmedLength(lines, i, cols);
            foreach (var mark in lines[i].Marks)
            {
                (result ??= new ReflowMetadata()).Marks.Add(
                    new LogicalMark(lineOffset + Math.Clamp(mark.Column, 0, length), mark));
            }

            foreach (var link in lines[i].Links)
            {
                var start = Math.Clamp(link.Column, 0, length);
                var end = Math.Clamp(link.EndColumn, start, length);
                if (end > start)
                    (result ??= new ReflowMetadata()).Links.Add(
                        new LogicalLink(lineOffset + start, end - start, link.Url, link.Id));
            }

            lineOffset += length;
        }

        return result;
    }

    /// <summary>Places logical metadata onto the rows that now own the corresponding cells.</summary>
    internal static void RestoreMetadata(
        IReadOnlyList<BufferLine> lines, IReadOnlyList<int> lineLengths, ReflowMetadata metadata)
    {
        foreach (var line in lines)
        {
            line.ClearMarks();
            line.ClearLinks();
        }

        foreach (var logical in metadata.Marks)
        {
            var (line, column) = Locate(logical.Offset, lineLengths);
            var mark = logical.Mark;
            lines[line].AddMark(new LineMark(column, mark.Kind, mark.ExitCode));
        }

        foreach (var logical in metadata.Links)
        {
            var offset = logical.Offset;
            var remaining = logical.Length;
            while (remaining > 0)
            {
                var (line, column) = Locate(offset, lineLengths);
                var take = Math.Min(remaining, lineLengths[line] - column);
                // Capture clamps links to the source stream and reflow preserves that stream's
                // total length, so a positive remainder always has a destination cell.
                System.Diagnostics.Debug.Assert(take > 0);
                if (take <= 0)
                    break;
                lines[line].AddLink(new LineHyperlink(column, take, logical.Url, logical.Id));
                offset += take;
                remaining -= take;
            }
        }
    }

    private static (int Line, int Column) Locate(int offset, IReadOnlyList<int> lineLengths)
    {
        for (var i = 0; i < lineLengths.Count; i++)
        {
            // An end-of-content mark belongs at the end of the final row rather than disappearing.
            if (offset < lineLengths[i] || i == lineLengths.Count - 1)
                return (i, Math.Clamp(offset, 0, lineLengths[i]));
            offset -= lineLengths[i];
        }

        throw new InvalidOperationException("Reflow metadata requires at least one destination row.");
    }

    /// <summary>
    /// Gets the trimmed length of a row within a wrapped line group.
    /// </summary>
    public static int GetWrappedLineTrimmedLength(IReadOnlyList<BufferLine> lines, int i, int cols)
    {
        if (i == lines.Count - 1)
        {
            return lines[i].GetTrimmedLength();
        }

        var endsInNull = !lines[i].HasContent(cols - 1) && lines[i].GetWidth(cols - 1) == 1;
        var followingLineStartsWithWide = lines[i + 1].GetWidth(0) == 2;
        if (endsInNull && followingLineStartsWithWide)
        {
            return cols - 1;
        }

        return cols;
    }

    internal static bool HasNonNormalLineAttribute(IReadOnlyList<BufferLine> lines)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].LineAttribute != LineAttribute.Normal)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether a wrapped group must be left alone by reflow.
    /// </summary>
    /// <remarks>
    /// <para>Two kinds of line say so, for one reason. Reflow redistributes CELLS between lines, and
    /// both a DEC line attribute and an OSC 66 sized run are properties of a line and a column range
    /// that the cells know nothing about -- so cells arriving on a new line would keep neither, and
    /// the metadata left behind would describe columns whose contents had moved away.</para>
    /// <para>Leaving the group unreflowed keeps the two consistent with each other: a scaled block
    /// stays whole, on the line that describes it, at the columns it claims. The cost is that such a
    /// group does not re-wrap to the new width, which is the same cost double-width lines already
    /// pay here.</para>
    /// </remarks>
    internal static bool IsUnreflowable(IReadOnlyList<BufferLine> lines)
    {
        if (HasNonNormalLineAttribute(lines))
        {
            return true;
        }

        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].HasSizedRuns)
            {
                return true;
            }
        }

        return false;
    }
}
