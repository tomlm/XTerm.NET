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
/// The two ends of a selection, in buffer coordinates, with the earlier one first.
/// </summary>
/// <remarks>
/// <para>ORDERED, which is the whole reason this exists as a type rather than a pair of points.
/// The manager stores the two ends in the order they were DRAGGED, so a backwards drag puts the
/// later one first -- and every consumer therefore had to know to swap them. Three places inside
/// this class did, in three copies of the same comparison, and a host that wanted to know what was
/// selected had to work it out a fourth time.</para>
/// <para>Rows are ABSOLUTE buffer rows, not viewport rows, so a range stays meaningful while the
/// viewport scrolls under it. A caller drawing to the screen subtracts the scroll position itself.</para>
/// </remarks>
public readonly record struct SelectionRange(int StartX, int StartY, int EndX, int EndY)
{
    /// <summary>
    /// The columns of <paramref name="row"/> this selection covers, clamped to a grid
    /// <paramref name="cols"/> wide.
    /// </summary>
    /// <remarks>
    /// <para>False when the row holds none of the selection, which lets a renderer skip a row for
    /// the cost of two comparisons instead of asking about every cell in it.</para>
    /// <para>Every selection is LINEAR -- there is no block mode -- so a row's share of one is
    /// always a single contiguous span. That is what makes this expressible as a pair rather than
    /// a set, and it is why a renderer does not need to scan for where the answer changes.</para>
    /// <para>Clamped because the range outlives the grid it was made on: a selection taken at one
    /// width and read after a narrower resize names columns that are no longer there.</para>
    /// </remarks>
    public bool TryGetRowSpan(int row, int cols, out int startX, out int endX)
    {
        startX = 0;
        endX = -1;

        if (cols <= 0 || row < StartY || row > EndY)
            return false;

        var lastColumn = cols - 1;
        startX = Math.Clamp(row == StartY ? StartX : 0, 0, lastColumn);
        endX = Math.Clamp(row == EndY ? EndX : lastColumn, 0, lastColumn);

        return startX <= endX;
    }
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

    /// <summary>
    /// The current selection as an ordered range, or false when there is none.
    /// </summary>
    /// <remarks>
    /// <para>The one place the two ends are put in order for a caller asking WHAT IS SELECTED.
    /// That comparison was written out twice -- once to answer about a cell, once to extract the
    /// text -- and both now come from here.</para>
    /// <para>The word and line expansions keep their own, and should: they are not asking what is
    /// selected but which STORED end is which, so they can grow each one outward and put it back in
    /// the field it came from. An ordered copy cannot answer that. Worth saying because the
    /// comparison looks identical and the temptation to share it is exactly how the expansion got
    /// its direction backwards once already.</para>
    /// <para>Public because a host cannot otherwise find out what is selected without asking about
    /// every cell. A renderer wanting to paint the highlight had to call
    /// <see cref="IsCellSelected"/> once per column per row -- several thousand times a frame -- to
    /// rediscover a range this class already had.</para>
    /// </remarks>
    public bool TryGetSelection(out SelectionRange range)
    {
        if (!HasSelection)
        {
            range = default;
            return false;
        }

        var start = _selectionStart!.Value;
        var end = _selectionEnd!.Value;

        if (start.y > end.y || (start.y == end.y && start.x > end.x))
            (start, end) = (end, start);

        range = new SelectionRange(start.x, start.y, end.x, end.y);
        return true;
    }

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

        TryGetSelection(out var range);

        var buffer = _terminal.Buffer;
        var text = new System.Text.StringBuilder();

        for (int y = range.StartY; y <= range.EndY; y++)
        {
            if (y < 0 || y >= buffer.Lines.Length)
                continue;

            var line = buffer.Lines[y];
            if (line == null)
                continue;

            if (!range.TryGetRowSpan(y, _terminal.Cols, out var startX, out var endX))
                continue;

            var lineText = line.TranslateToString(false, startX, endX + 1);
            text.Append(lineText);

            // The flag belongs to the FOLLOWING line, not this one: IsWrapped means "this line
            // continues the previous", so whether row y joins row y+1 is row y+1's answer. Testing
            // this row inserted newlines inside wrapped text and ran separate lines together --
            // copying a wrapped command out of the scrollback pasted it broken in both directions.
            if (y < range.EndY)
            {
                var next = y + 1 < buffer.Lines.Length ? buffer.Lines[y + 1] : null;
                if (next is null || !next.IsWrapped)
                    text.Append('\n');
            }
        }

        return text.ToString();
    }

    /// <summary>
    /// Checks if a cell is selected.
    /// </summary>
    public bool IsCellSelected(int x, int y)
    {
        if (!TryGetSelection(out var range))
            return false;

        var absoluteY = ToAbsoluteY(y);

        // Deliberately NOT TryGetRowSpan, which clamps to the grid width. This answers about a
        // column rather than about a row of a screen, and has never bounded x above -- a caller
        // asking about a column past the end of a middle row gets true today. Routing it through
        // the clamped form would change that quietly, and the point of this change is to remove a
        // duplicated ORDERING rule, not to redefine what a selected cell is.
        if (absoluteY < range.StartY || absoluteY > range.EndY)
            return false;

        if (absoluteY == range.StartY && absoluteY == range.EndY)
            return x >= range.StartX && x <= range.EndX;

        if (absoluteY == range.StartY)
            return x >= range.StartX;

        if (absoluteY == range.EndY)
            return x <= range.EndX;

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

        // Expansion follows the ORDER the points are in, not the order they were recorded in.
        // Growing _selectionStart leftward and _selectionEnd rightward is only right while the
        // anchor precedes the mouse; drag backwards and the two swap roles, so the anchor was
        // grown the wrong way and the word under the mouse was truncated instead of completed.
        var forward = start.y < end.y || (start.y == end.y && start.x <= end.x);
        var (first, last) = forward ? (start, end) : (end, start);

        var firstLine = first.y >= 0 && first.y < buffer.Lines.Length ? buffer.Lines[first.y] : null;
        if (firstLine != null)
        {
            while (first.x > 0 && IsWordChar(firstLine[first.x - 1].Content))
            {
                first.x--;
            }
        }

        var lastLine = last.y >= 0 && last.y < buffer.Lines.Length ? buffer.Lines[last.y] : null;
        if (lastLine != null)
        {
            while (last.x < _terminal.Cols - 1 && IsWordChar(lastLine[last.x + 1].Content))
            {
                last.x++;
            }
        }

        (_selectionStart, _selectionEnd) = forward ? (first, last) : (last, first);
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
