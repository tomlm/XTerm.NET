using XTerm.Common;

namespace XTerm.Buffer;

/// <summary>
/// Main terminal buffer that manages the active screen and scrollback.
/// </summary>
public class TerminalBuffer
{
    private readonly CircularList<BufferLine> _lines;
    private int _yDisp;
    private int _yBase;
    private int _y;
    private int _x;
    private int _scrollBottom;
    private int _scrollTop;
    private readonly int _cols;
    private readonly int _rows;
    private readonly BufferCell _fillCell;

    public int YDisp => _yDisp;
    public int YBase => _yBase;
    public int Y => _y;
    public int X => _x;
    public int ScrollTop => _scrollTop;
    public int ScrollBottom => _scrollBottom;

    public CircularList<BufferLine> Lines => _lines;

    /// <summary>
    /// Saved cursor state for DECSC/DECRC.
    /// </summary>
    public class SavedCursor
    {
        public int X { get; set; }
        public int Y { get; set; }
        public AttributeData Attr { get; set; }
        public CharsetMode Charset { get; set; }

        public SavedCursor()
        {
            X = 0;
            Y = 0;
            Attr = AttributeData.Default;
            Charset = CharsetMode.G0;
        }
    }

    public SavedCursor SavedCursorState { get; set; }

    public TerminalBuffer(int cols, int rows, int scrollback)
    {
        _cols = cols;
        _rows = rows;
        _lines = new CircularList<BufferLine>(rows + scrollback);
        _yDisp = 0;
        _yBase = 0;
        _y = 0;
        _x = 0;
        _scrollTop = 0;
        _scrollBottom = rows - 1;
        _fillCell = BufferCell.Null;
        SavedCursorState = new SavedCursor();

        // Initialize buffer with empty lines
        for (int i = 0; i < rows; i++)
        {
            _lines.Push(new BufferLine(cols, _fillCell));
        }
    }

    /// <summary>
    /// Gets a line from the buffer.
    /// </summary>
    public BufferLine? GetLine(int y)
    {
        return _lines[y];
    }

    /// <summary>
    /// Gets a blank line (filled with null cells).
    /// </summary>
    public BufferLine GetBlankLine(AttributeData attr, bool isWrapped = false)
    {
        var fillCell = new BufferCell
        {
            Content = Constants.NullCellChar.ToString(),
            Width = Constants.NullCellWidth,
            Attributes = attr,
            CodePoint = Constants.NullCellCode
        };

        return new BufferLine(_cols, fillCell) { IsWrapped = isWrapped };
    }

    /// <summary>
    /// Scrolls the buffer up by a specified number of lines.
    /// </summary>
    public void ScrollUp(int lines, bool isWrapped = false)
    {
        for (int i = 0; i < lines; i++)
        {
            // Remove line from scroll region top
            if (_scrollTop == 0)
            {
                // Line goes to scrollback
                _yBase++;
                _yDisp = _yBase;
            }
            else
            {
                // Line is removed from buffer
                _lines.Splice(_scrollTop, 1);
            }

            // Add blank line at bottom of scroll region
            var newLine = GetBlankLine(AttributeData.Default, isWrapped);
            _lines.Splice(_scrollBottom, 0, newLine);
        }
    }

    /// <summary>
    /// Scrolls the buffer down by a specified number of lines.
    /// </summary>
    public void ScrollDown(int lines)
    {
        for (int i = 0; i < lines; i++)
        {
            // Remove line from scroll region bottom
            _lines.Splice(_scrollBottom, 1);

            // Add blank line at top of scroll region
            var newLine = GetBlankLine(AttributeData.Default);
            _lines.Splice(_scrollTop, 0, newLine);
        }
    }

    /// <summary>
    /// Scrolls the display by a specified amount.
    /// </summary>
    public void ScrollDisp(int disp, bool suppressScrollEvent = false)
    {
        _yDisp = Math.Clamp(_yDisp + disp, 0, _yBase);
    }

    /// <summary>
    /// Scrolls the display to the bottom.
    /// </summary>
    public void ScrollToBottom()
    {
        _yDisp = _yBase;
    }

    /// <summary>
    /// Scrolls the display to the top.
    /// </summary>
    public void ScrollToTop()
    {
        _yDisp = 0;
    }

    /// <summary>
    /// Sets the scroll region.
    /// </summary>
    public void SetScrollRegion(int top, int bottom)
    {
        _scrollTop = Math.Clamp(top, 0, _rows - 1);
        _scrollBottom = Math.Clamp(bottom, _scrollTop, _rows - 1);
    }

    /// <summary>
    /// Resets the scroll region to full screen.
    /// </summary>
    public void ResetScrollRegion()
    {
        _scrollTop = 0;
        _scrollBottom = _rows - 1;
    }

    /// <summary>
    /// Gets the absolute line index for a viewport-relative y coordinate.
    /// </summary>
    public int GetAbsoluteY(int y)
    {
        return _yBase + y;
    }

    /// <summary>
    /// Resizes the buffer.
    /// </summary>
    public void Resize(int newCols, int newRows)
    {
        // Implementation would handle reflow logic
        // For now, simple resize
        var fillCell = BufferCell.Null;

        // Resize existing lines
        for (int i = 0; i < _lines.Length; i++)
        {
            _lines[i]?.Resize(newCols, fillCell);
        }

        // Add or remove lines as needed
        if (newRows > _rows)
        {
            for (int i = _rows; i < newRows; i++)
            {
                _lines.Push(new BufferLine(newCols, fillCell));
            }
        }
    }

    /// <summary>
    /// Sets the cursor position.
    /// </summary>
    public void SetCursor(int x, int y)
    {
        _x = Math.Clamp(x, 0, _cols - 1);
        _y = Math.Clamp(y, 0, _rows - 1);
    }

    /// <summary>
    /// Moves the cursor to the specified position.
    /// </summary>
    public void MoveCursor(int x, int y)
    {
        _x = x;
        _y = y;
    }
}
