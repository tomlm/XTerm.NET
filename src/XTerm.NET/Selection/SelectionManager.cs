using XTerm.Buffer;
using XTerm.Common;

namespace XTerm.Selection;

/// <summary>
/// Selection mode for text selection.
/// </summary>
public enum SelectionMode
{
    Normal,
    Word,
    Line
}

/// <summary>
/// Manages text selection in the terminal.
/// </summary>
public class SelectionManager
{
    private readonly Terminal _terminal;
    private bool _isSelecting;
    private (int x, int y)? _selectionStart;
    private (int x, int y)? _selectionEnd;
    private SelectionMode _selectionMode;

    public EventEmitter OnSelectionChange { get; }
    public bool HasSelection => _selectionStart.HasValue && _selectionEnd.HasValue;

    public SelectionManager(Terminal terminal)
    {
        _terminal = terminal;
        _isSelecting = false;
        _selectionMode = SelectionMode.Normal;
        OnSelectionChange = new EventEmitter();
    }

    /// <summary>
    /// Starts a new selection.
    /// </summary>
    public void StartSelection(int x, int y, SelectionMode mode = SelectionMode.Normal)
    {
        _isSelecting = true;
        _selectionMode = mode;
        _selectionStart = (x, y);
        _selectionEnd = (x, y);

        // Adjust for word or line mode
        if (mode == SelectionMode.Word)
        {
            ExpandSelectionToWord();
        }
        else if (mode == SelectionMode.Line)
        {
            ExpandSelectionToLine();
        }

        OnSelectionChange.Fire();
    }

    /// <summary>
    /// Updates the selection end point.
    /// </summary>
    public void UpdateSelection(int x, int y)
    {
        if (!_isSelecting || !_selectionStart.HasValue)
            return;

        _selectionEnd = (x, y);

        // Adjust for selection mode
        if (_selectionMode == SelectionMode.Word)
        {
            ExpandSelectionToWord();
        }
        else if (_selectionMode == SelectionMode.Line)
        {
            ExpandSelectionToLine();
        }

        OnSelectionChange.Fire();
    }

    /// <summary>
    /// Ends the selection.
    /// </summary>
    public void EndSelection()
    {
        _isSelecting = false;
    }

    /// <summary>
    /// Clears the selection.
    /// </summary>
    public void ClearSelection()
    {
        _selectionStart = null;
        _selectionEnd = null;
        _isSelecting = false;
        OnSelectionChange.Fire();
    }

    /// <summary>
    /// Selects all text in the buffer.
    /// </summary>
    public void SelectAll()
    {
        _selectionStart = (0, 0);
        _selectionEnd = (_terminal.Cols - 1, _terminal.Rows - 1);
        _isSelecting = false;
        OnSelectionChange.Fire();
    }

    /// <summary>
    /// Gets the selected text.
    /// </summary>
    public string GetSelectionText()
    {
        if (!HasSelection)
            return string.Empty;

        var start = _selectionStart!.Value;
        var end = _selectionEnd!.Value;

        // Normalize selection (start before end)
        if (start.y > end.y || (start.y == end.y && start.x > end.x))
        {
            (start, end) = (end, start);
        }

        var buffer = _terminal.Buffer;
        var text = new System.Text.StringBuilder();

        for (int y = start.y; y <= end.y; y++)
        {
            var line = buffer.Lines[buffer.YDisp + y];
            if (line == null)
                continue;

            int startX = (y == start.y) ? start.x : 0;
            int endX = (y == end.y) ? end.x : _terminal.Cols - 1;

            var lineText = line.TranslateToString(false, startX, endX + 1);
            text.Append(lineText);

            // Add line break if not last line and line doesn't wrap
            if (y < end.y && !line.IsWrapped)
            {
                text.AppendLine();
            }
        }

        return text.ToString();
    }

    /// <summary>
    /// Checks if a cell is selected.
    /// </summary>
    public bool IsCellSelected(int x, int y)
    {
        if (!HasSelection)
            return false;

        var start = _selectionStart!.Value;
        var end = _selectionEnd!.Value;

        // Normalize selection
        if (start.y > end.y || (start.y == end.y && start.x > end.x))
        {
            (start, end) = (end, start);
        }

        // Check if cell is in selection
        if (y < start.y || y > end.y)
            return false;

        if (y == start.y && y == end.y)
            return x >= start.x && x <= end.x;

        if (y == start.y)
            return x >= start.x;

        if (y == end.y)
            return x <= end.x;

        return true;
    }

    /// <summary>
    /// Expands selection to word boundaries.
    /// </summary>
    private void ExpandSelectionToWord()
    {
        if (!_selectionStart.HasValue || !_selectionEnd.HasValue)
            return;

        var buffer = _terminal.Buffer;
        var start = _selectionStart.Value;
        var end = _selectionEnd.Value;

        // Expand start to word boundary
        var startLine = buffer.Lines[buffer.YDisp + start.y];
        if (startLine != null)
        {
            while (start.x > 0 && IsWordChar(startLine[start.x - 1].GetChars()))
            {
                start.x--;
            }
        }

        // Expand end to word boundary
        var endLine = buffer.Lines[buffer.YDisp + end.y];
        if (endLine != null)
        {
            while (end.x < _terminal.Cols - 1 && IsWordChar(endLine[end.x + 1].GetChars()))
            {
                end.x++;
            }
        }

        _selectionStart = start;
        _selectionEnd = end;
    }

    /// <summary>
    /// Expands selection to line boundaries.
    /// </summary>
    private void ExpandSelectionToLine()
    {
        if (!_selectionStart.HasValue || !_selectionEnd.HasValue)
            return;

        var start = _selectionStart.Value;
        var end = _selectionEnd.Value;

        // Normalize
        if (start.y > end.y)
        {
            (start, end) = (end, start);
        }

        // Select entire lines
        start.x = 0;
        end.x = _terminal.Cols - 1;

        _selectionStart = start;
        _selectionEnd = end;
    }

    /// <summary>
    /// Checks if a character is a word character.
    /// </summary>
    private bool IsWordChar(string ch)
    {
        if (string.IsNullOrEmpty(ch))
            return false;

        var c = ch[0];
        return char.IsLetterOrDigit(c) || c == '_';
    }
}

/// <summary>
/// Manages the viewport (visible portion of the buffer).
/// </summary>
public class ViewportManager
{
    private readonly Terminal _terminal;
    private int _scrollTop;

    public EventEmitter OnScroll { get; }
    public int ScrollTop => _scrollTop;

    public ViewportManager(Terminal terminal)
    {
        _terminal = terminal;
        _scrollTop = 0;
        OnScroll = new EventEmitter();
    }

    /// <summary>
    /// Scrolls the viewport by a number of lines.
    /// </summary>
    public void ScrollLines(int lines)
    {
        var buffer = _terminal.Buffer;
        var newScrollTop = Math.Clamp(_scrollTop + lines, 0, buffer.YBase);

        if (newScrollTop != _scrollTop)
        {
            _scrollTop = newScrollTop;
            buffer.ScrollDisp(lines);
            OnScroll.Fire();
        }
    }

    /// <summary>
    /// Scrolls to a specific line.
    /// </summary>
    public void ScrollToLine(int line)
    {
        var buffer = _terminal.Buffer;
        var newScrollTop = Math.Clamp(line, 0, buffer.YBase);

        if (newScrollTop != _scrollTop)
        {
            var diff = newScrollTop - _scrollTop;
            _scrollTop = newScrollTop;
            buffer.ScrollDisp(diff);
            OnScroll.Fire();
        }
    }

    /// <summary>
    /// Scrolls to the top of the buffer.
    /// </summary>
    public void ScrollToTop()
    {
        ScrollToLine(0);
    }

    /// <summary>
    /// Scrolls to the bottom of the buffer.
    /// </summary>
    public void ScrollToBottom()
    {
        var buffer = _terminal.Buffer;
        ScrollToLine(buffer.YBase);
    }

    /// <summary>
    /// Gets the absolute line number for a viewport-relative line.
    /// </summary>
    public int GetAbsoluteLine(int viewportLine)
    {
        return _scrollTop + viewportLine;
    }

    /// <summary>
    /// Resets the viewport scroll position.
    /// </summary>
    public void Reset()
    {
        _scrollTop = 0;
        OnScroll.Fire();
    }
}
