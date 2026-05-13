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

    /// <summary>
    /// Fired when the selection changes.
    /// </summary>
    public event Action? SelectionChanged;
    
    public bool HasSelection => _selectionStart.HasValue && _selectionEnd.HasValue;

    public SelectionManager(Terminal terminal)
    {
        _terminal = terminal;
        _isSelecting = false;
        _selectionMode = SelectionMode.Normal;
        _terminal.Buffer.Trimmed += HandleTrim;
    }

    /// <summary>
    /// Starts a new selection.
    /// </summary>
    public void StartSelection(int x, int y, SelectionMode mode = SelectionMode.Normal)
    {
        _isSelecting = true;
        _selectionMode = mode;
        var absoluteY = ToAbsoluteY(y);
        _selectionStart = (x, absoluteY);
        _selectionEnd = (x, absoluteY);

        // Adjust for word or line mode
        if (mode == SelectionMode.Word)
        {
            ExpandSelectionToWord();
        }
        else if (mode == SelectionMode.Line)
        {
            ExpandSelectionToLine();
        }

        SelectionChanged?.Invoke();
    }

    /// <summary>
    /// Updates the selection end point.
    /// </summary>
    public void UpdateSelection(int x, int y)
    {
        if (!_isSelecting || !_selectionStart.HasValue)
            return;

        _selectionEnd = (x, ToAbsoluteY(y));

        // Adjust for selection mode
        if (_selectionMode == SelectionMode.Word)
        {
            ExpandSelectionToWord();
        }
        else if (_selectionMode == SelectionMode.Line)
        {
            ExpandSelectionToLine();
        }

        SelectionChanged?.Invoke();
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
        SelectionChanged?.Invoke();
    }

    /// <summary>
    /// Selects all text in the buffer.
    /// </summary>
    public void SelectAll()
    {
        _selectionStart = (0, 0);
        _selectionEnd = (_terminal.Cols - 1, Math.Max(_terminal.Buffer.Lines.Length - 1, 0));
        _isSelecting = false;
        SelectionChanged?.Invoke();
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
            if (y < 0 || y >= buffer.Lines.Length)
                continue;

            var line = buffer.Lines[y];
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

        var absoluteY = ToAbsoluteY(y);
        var start = _selectionStart!.Value;
        var end = _selectionEnd!.Value;

        // Normalize selection
        if (start.y > end.y || (start.y == end.y && start.x > end.x))
        {
            (start, end) = (end, start);
        }

        // Check if cell is in selection
        if (absoluteY < start.y || absoluteY > end.y)
            return false;

        if (absoluteY == start.y && absoluteY == end.y)
            return x >= start.x && x <= end.x;

        if (absoluteY == start.y)
            return x >= start.x;

        if (absoluteY == end.y)
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
        var startLine = start.y >= 0 && start.y < buffer.Lines.Length ? buffer.Lines[start.y] : null;
        if (startLine != null)
        {
            while (start.x > 0 && IsWordChar(startLine[start.x - 1].Content))
            {
                start.x--;
            }
        }

        // Expand end to word boundary
        var endLine = end.y >= 0 && end.y < buffer.Lines.Length ? buffer.Lines[end.y] : null;
        if (endLine != null)
        {
            while (end.x < _terminal.Cols - 1 && IsWordChar(endLine[end.x + 1].Content))
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

    private int ToAbsoluteY(int viewportY)
    {
        return _terminal.Buffer.YDisp + viewportY;
    }

    private void HandleTrim(int amount)
    {
        if (amount <= 0)
            return;

        if (_selectionStart.HasValue)
        {
            _selectionStart = (_selectionStart.Value.x, _selectionStart.Value.y - amount);
        }

        if (_selectionEnd.HasValue)
        {
            _selectionEnd = (_selectionEnd.Value.x, _selectionEnd.Value.y - amount);
        }

        if (_selectionEnd.HasValue && _selectionEnd.Value.y < 0)
        {
            ClearSelection();
            return;
        }

        if (_selectionStart.HasValue && _selectionStart.Value.y < 0)
        {
            _selectionStart = (0, 0);
        }

        if (_selectionEnd.HasValue)
        {
            var maxY = Math.Max(_terminal.Buffer.Lines.Length - 1, 0);
            _selectionEnd = (_selectionEnd.Value.x, Math.Min(_selectionEnd.Value.y, maxY));
        }

        SelectionChanged?.Invoke();
    }
}

/// <summary>
/// Manages the viewport (visible portion of the buffer).
/// </summary>
public class ViewportManager
{
    private readonly Terminal _terminal;
    private int _scrollTop;

    /// <summary>
    /// Fired when the viewport scrolls.
    /// </summary>
    public event Action? Scrolled;
    
    public int ScrollTop => _scrollTop;

    public ViewportManager(Terminal terminal)
    {
        _terminal = terminal;
        _scrollTop = 0;
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
            Scrolled?.Invoke();
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
            Scrolled?.Invoke();
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
        Scrolled?.Invoke();
    }
}
