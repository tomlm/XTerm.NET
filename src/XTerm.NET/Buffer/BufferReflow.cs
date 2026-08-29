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
