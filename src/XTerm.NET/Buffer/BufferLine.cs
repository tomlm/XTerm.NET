using System.Collections;
using System.Text;

namespace XTerm.Buffer;

/// <summary>
/// Represents a single line in the terminal buffer.
/// Contains an array of cells and metadata about the line.
/// </summary>
public class BufferLine : IEnumerable<BufferCell>
{
    private BufferCell[] _cells;
    private int _length;
    private bool _isWrapped;

    public int Length => _length;
    public bool IsWrapped
    {
        get => _isWrapped;
        set => _isWrapped = value;
    }

    public BufferLine(int cols, BufferCell? fillCell = null)
    {
        _length = cols;
        _cells = new BufferCell[cols];
        _isWrapped = false;

        var fill = fillCell ?? BufferCell.Empty;
        for (int i = 0; i < cols; i++)
        {
            _cells[i] = fill.Clone();
        }
    }

    /// <summary>
    /// Gets or sets a cell at a specific column.
    /// </summary>
    public BufferCell this[int index]
    {
        get
        {
            if (index < 0 || index >= _length)
                return BufferCell.Empty;
            return _cells[index];
        }
        set
        {
            if (index >= 0 && index < _length)
                _cells[index] = value;
        }
    }

    /// <summary>
    /// Gets the cell data at a specific column.
    /// </summary>
    public BufferCell LoadCell(int index, BufferCell cell)
    {
        if (index >= 0 && index < _length)
        {
            cell = _cells[index].Clone();
        }
        return cell;
    }

    /// <summary>
    /// Sets a cell at a specific column.
    /// </summary>
    public void SetCell(int index, BufferCell cell)
    {
        if (index >= 0 && index < _length)
        {
            _cells[index] = cell.Clone();
        }
    }

    /// <summary>
    /// Gets the cell code point at a specific column.
    /// </summary>
    public int GetCodePoint(int index)
    {
        if (index >= 0 && index < _length)
            return _cells[index].CodePoint;
        return 0;
    }

    /// <summary>
    /// Resizes the line to a new column count.
    /// </summary>
    public void Resize(int cols, BufferCell fillCell)
    {
        if (cols == _length)
            return;

        if (cols > _length)
        {
            var newCells = new BufferCell[cols];
            Array.Copy(_cells, newCells, _length);
            for (int i = _length; i < cols; i++)
            {
                newCells[i] = fillCell.Clone();
            }
            _cells = newCells;
        }
        else
        {
            var newCells = new BufferCell[cols];
            Array.Copy(_cells, newCells, cols);
            _cells = newCells;
        }

        _length = cols;
    }

    /// <summary>
    /// Fills a range of cells with a specific cell.
    /// </summary>
    public void Fill(BufferCell fillCell, int startCol = 0, int endCol = -1)
    {
        if (endCol == -1)
            endCol = _length;

        for (int i = startCol; i < endCol && i < _length; i++)
        {
            _cells[i] = fillCell.Clone();
        }
    }

    /// <summary>
    /// Copies cells from another line.
    /// </summary>
    public void CopyCellsFrom(BufferLine src, int srcCol, int destCol, int length, bool applyInReverse)
    {
        if (applyInReverse)
        {
            for (int i = length - 1; i >= 0; i--)
            {
                if (destCol + i < _length && srcCol + i < src._length)
                {
                    _cells[destCol + i] = src._cells[srcCol + i].Clone();
                }
            }
        }
        else
        {
            for (int i = 0; i < length; i++)
            {
                if (destCol + i < _length && srcCol + i < src._length)
                {
                    _cells[destCol + i] = src._cells[srcCol + i].Clone();
                }
            }
        }
    }

    /// <summary>
    /// Translates the line to a string.
    /// </summary>
    public string TranslateToString(bool trimRight = false, int startCol = 0, int endCol = -1)
    {
        if (endCol == -1)
            endCol = _length;

        var sb = new StringBuilder();
        for (int i = startCol; i < endCol && i < _length; i++)
        {
            var cell = _cells[i];
            sb.Append(cell.Content);
        }

        if (trimRight)
        {
            return sb.ToString().TrimEnd();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Gets the last non-whitespace cell index.
    /// </summary>
    public int GetTrimmedLength()
    {
        for (int i = _length - 1; i >= 0; i--)
        {
            if (!_cells[i].IsSpace() && !_cells[i].IsEmpty())
                return i + 1;
        }
        return 0;
    }

    /// <summary>
    /// Clones the line.
    /// </summary>
    public BufferLine Clone()
    {
        var newLine = new BufferLine(_length);
        newLine._isWrapped = _isWrapped;
        for (int i = 0; i < _length; i++)
        {
            newLine._cells[i] = _cells[i].Clone();
        }
        return newLine;
    }

    /// <summary>
    /// Copies the line into another line.
    /// </summary>
    public void CopyFrom(BufferLine line)
    {
        if (_length != line._length)
        {
            _cells = new BufferCell[line._length];
            _length = line._length;
        }

        for (int i = 0; i < _length; i++)
        {
            _cells[i] = line._cells[i].Clone();
        }
        _isWrapped = line._isWrapped;
    }

    public IEnumerator<BufferCell> GetEnumerator()
    {
        return _cells.AsEnumerable().GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return _cells.GetEnumerator();
    }
}
