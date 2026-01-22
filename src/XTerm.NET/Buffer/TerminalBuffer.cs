using System.Text;
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
    private int _cols;
    private int _rows;

    /// <summary>
    /// The absolute line index of the top of the viewport in the buffer.
    /// In XTerm.js this is 'ydisp'. This represents the current scroll position.
    /// </summary>
    public int ViewportY 
    { 
        get => _yDisp;
        set => _yDisp = Math.Clamp(value, 0, _yBase);
    }

    /// <summary>
    /// The absolute line index where new content is being written.
    /// In XTerm.js this is 'ybase'. This represents the bottom of the active content.
    /// </summary>
    public int BaseY => _yBase;

    /// <summary>
    /// Total number of lines in the buffer (scrollback + active lines).
    /// </summary>
    public int Length => _lines.Length;

    /// <summary>
    /// Whether the viewport is at the bottom (showing latest content).
    /// In xterm.js: ydisp === ybase means we're at the bottom.
    /// </summary>
    public bool IsAtBottom => _yDisp >= _yBase;

    /// <summary>
    /// Number of columns in the buffer.
    /// </summary>
    public int Cols => _cols;

    /// <summary>
    /// Number of rows in the buffer (viewport height).
    /// </summary>
    public int Rows => _rows;

    // Legacy properties for backward compatibility
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
        SavedCursorState = new SavedCursor();

        // Initialize buffer with empty lines
        for (int i = 0; i < rows; i++)
        {
            _lines.Push(new BufferLine(cols, BufferCell.Space));
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
        var fillCell = BufferCell.Space;
        fillCell.Attributes = attr;
        return new BufferLine(_cols, fillCell) { IsWrapped = isWrapped };
    }

    /// <summary>
    /// Scrolls the buffer up by a specified number of lines.
    /// This matches xterm.js Buffer.scroll() behavior.
    /// </summary>
    public void ScrollUp(int lines, bool isWrapped = false)
    {
        for (int i = 0; i < lines; i++)
        {
            // Create a new blank line that will be inserted at the bottom of the scroll region
            var newLine = GetBlankLine(AttributeData.Default, isWrapped);

            if (_scrollTop == 0)
            {
                // When scrollTop is 0, the top line goes into scrollback.
                // In xterm.js: push new line first, then increment yBase and yDisp.
                // This causes the circular list to potentially recycle the oldest line.
                
                // Check if we're at max capacity - if so, yBase stays the same but 
                // the buffer rotates. If not, yBase increments.
                var willBeRecycled = _lines.Length >= _lines.MaxLength;
                
                // Push the new line at the end (bottom of screen in buffer terms)
                _lines.Push(newLine);
                
                // Only increment yBase if the buffer didn't recycle
                if (!willBeRecycled)
                {
                    _yBase++;
                }
                
                // If yDisp was at the bottom, keep it there
                if (_yDisp + 1 < _yBase)
                {
                    // User was scrolled up, don't auto-scroll
                }
                else
                {
                    _yDisp = _yBase;
                }
            }
            else
            {
                // Scroll region is not at top of screen.
                // Remove line from scroll region top and add blank at bottom.
                // Use yBase offset for correct absolute positioning.
                var scrollRegionStart = _yBase + _scrollTop;
                var scrollRegionEnd = _yBase + _scrollBottom;

                // Delete the line at the top of scroll region
                _lines.Splice(scrollRegionStart, 1);

                // Insert blank line at bottom of scroll region
                _lines.Splice(scrollRegionEnd, 0, newLine);
            }
        }
    }

    /// <summary>
    /// Scrolls the buffer down by a specified number of lines.
    /// This is reverse scrolling within the scroll region.
    /// </summary>
    public void ScrollDown(int lines)
    {
        for (int i = 0; i < lines; i++)
        {
            // Calculate absolute positions in the buffer
            var scrollRegionStart = _yBase + _scrollTop;
            var scrollRegionEnd = _yBase + _scrollBottom;

            // Remove line from scroll region bottom
            _lines.Splice(scrollRegionEnd, 1);

            // Add blank line at top of scroll region
            var newLine = GetBlankLine(AttributeData.Default);
            _lines.Splice(scrollRegionStart, 0, newLine);
        }
    }

    /// <summary>
    /// Scrolls the display by a specified amount.
    /// This only changes the viewport position, not the buffer content.
    /// </summary>
    public void ScrollDisp(int disp, bool suppressScrollEvent = false)
    {
        _yDisp = Math.Clamp(_yDisp + disp, 0, _yBase);
    }

    /// <summary>
    /// Scrolls the viewport to show a specific line.
    /// </summary>
    /// <param name="line">The absolute line number to scroll to</param>
    public void ScrollToLine(int line)
    {
        _yDisp = Math.Clamp(line, 0, _yBase);
    }

    /// <summary>
    /// Scrolls the display to the bottom (showing active screen).
    /// In xterm.js, yDisp = yBase means showing the active terminal area.
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
    /// Scrolls the viewport by a relative number of lines.
    /// </summary>
    /// <param name="lines">Number of lines to scroll (negative = up, positive = down)</param>
    public void ScrollLines(int lines)
    {
        ScrollToLine(_yDisp + lines);
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
        // Calculate new max length keeping the same scrollback capacity
        var newMaxLength = newRows + (_lines.MaxLength - _rows);

        // Resize max length of circular list (may drop oldest lines if shrinking)
        _lines.Resize(newMaxLength);

        // Resize existing lines to the new column count
        var fillCell = BufferCell.Space;
        for (int i = 0; i < _lines.Length; i++)
        {
            _lines[i]?.Resize(newCols, fillCell);
        }

        // Ensure we have at least viewport rows available
        while (_lines.Length < newRows)
        {
            _lines.Push(new BufferLine(newCols, fillCell));
        }

        // If we have fewer rows, ensure ybase/ydisp stay in range
        _yBase = Math.Min(_yBase, Math.Max(0, _lines.Length - newRows));
        _yDisp = Math.Clamp(_yDisp, 0, _yBase);

        // Update scroll region and dimensions
        var oldRows = _rows;
        _cols = newCols;
        _rows = newRows;

        if (_scrollBottom == oldRows - 1)
        {
            _scrollBottom = newRows - 1;
        }
        else
        {
            _scrollBottom = Math.Min(_scrollBottom, newRows - 1);
        }
        _scrollTop = Math.Min(_scrollTop, newRows - 1);

        // Clamp cursor within new bounds
        _x = Math.Clamp(_x, 0, _cols - 1);
        _y = Math.Clamp(_y, 0, _rows - 1);
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
    /// Moves the cursor to the specified position without any clamping.
    /// </summary>
    public void SetCursorRaw(int x, int y)
    {
        _x = x;
        _y = y;
    }

    public string PrintViewport()
    {
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < _rows; i++)
        {
            var line = GetLine(_yDisp + i);
            foreach(var cell in line)
            {
                sb.Append(cell.Content);
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
