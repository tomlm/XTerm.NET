using System.Text;
using XTerm.NET.Buffer;
using XTerm.NET.Common;
using XTerm.NET.Parser;

namespace XTerm.NET;

/// <summary>
/// Handles input escape sequences and updates the terminal buffer.
/// Implements VT100/xterm escape sequence handlers.
/// </summary>
public class InputHandler
{
    private readonly Terminal _terminal;
    private Buffer.Buffer _buffer;
    private AttributeData _curAttr;
    private readonly Dictionary<string, Func<string>?> _charsets;
    private CharsetMode _currentCharset;

    public InputHandler(Terminal terminal)
    {
        _terminal = terminal;
        _buffer = terminal.Buffer;
        _curAttr = AttributeData.Default;
        _charsets = new Dictionary<string, Func<string>?>();
        _currentCharset = CharsetMode.G0;
    }

    /// <summary>
    /// Prints a character to the buffer.
    /// </summary>
    public void Print(string data)
    {
        var line = _buffer.Lines[_buffer.Y + _buffer.YBase];
        if (line == null)
            return;

        // Handle autowrap
        if (_buffer.X >= _terminal.Cols)
        {
            if (_terminal.Options.Wraparound)
            {
                _buffer.SetCursor(0, _buffer.Y);
                
                if (_buffer.Y == _buffer.ScrollBottom)
                {
                    _buffer.ScrollUp(1, true);
                }
                else
                {
                    _buffer.MoveCursor(_buffer.X, _buffer.Y + 1);
                }
                
                line = _buffer.Lines[_buffer.Y + _buffer.YBase];
                if (line != null)
                {
                    line.IsWrapped = true;
                }
            }
            else
            {
                return; // Don't print beyond line edge
            }
        }

        // Get character width
        var width = GetStringCellWidth(data);

        // Create cell
        var cell = new BufferCell
        {
            Content = data,
            Width = width,
            Attributes = _curAttr.Clone(),
            CodePoint = data.Length > 0 ? char.ConvertToUtf32(data, 0) : 0
        };

        // Insert mode handling
        if (_terminal.InsertMode)
        {
            // Shift cells right
            line?.CopyCellsFrom(line, _buffer.X, _buffer.X + width, _terminal.Cols - _buffer.X - width, false);
        }

        // Set the cell
        line?.SetCell(_buffer.X, cell);

        // Handle wide characters
        if (width == 2)
        {
            // Set following cell as a spacer
            if (_buffer.X + 1 < _terminal.Cols)
            {
                var spacer = new BufferCell
                {
                    Content = "",
                    Width = 0,
                    Attributes = _curAttr.Clone(),
                    CodePoint = 0
                };
                line?.SetCell(_buffer.X + 1, spacer);
            }
        }

        _buffer.SetCursor(_buffer.X + width, _buffer.Y);
    }

    /// <summary>
    /// Handles CSI sequences (Control Sequence Introducer).
    /// </summary>
    public void HandleCsi(string identifier, Params parameters)
    {
        switch (identifier)
        {
            case "@": // ICH - Insert Characters
                InsertChars(parameters);
                break;
            case "A": // CUU - Cursor Up
                CursorUp(parameters);
                break;
            case "B": // CUD - Cursor Down
                CursorDown(parameters);
                break;
            case "C": // CUF - Cursor Forward
                CursorForward(parameters);
                break;
            case "D": // CUB - Cursor Backward
                CursorBackward(parameters);
                break;
            case "E": // CNL - Cursor Next Line
                CursorNextLine(parameters);
                break;
            case "F": // CPL - Cursor Previous Line
                CursorPrecedingLine(parameters);
                break;
            case "G": // CHA - Cursor Horizontal Absolute
                CursorCharAbsolute(parameters);
                break;
            case "H": // CUP - Cursor Position
            case "f": // HVP - Horizontal Vertical Position
                CursorPosition(parameters);
                break;
            case "J": // ED - Erase in Display
                EraseInDisplay(parameters);
                break;
            case "K": // EL - Erase in Line
                EraseInLine(parameters);
                break;
            case "L": // IL - Insert Lines
                InsertLines(parameters);
                break;
            case "M": // DL - Delete Lines
                DeleteLines(parameters);
                break;
            case "P": // DCH - Delete Characters
                DeleteChars(parameters);
                break;
            case "S": // SU - Scroll Up
                ScrollUp(parameters);
                break;
            case "T": // SD - Scroll Down
                ScrollDown(parameters);
                break;
            case "X": // ECH - Erase Characters
                EraseChars(parameters);
                break;
            case "m": // SGR - Select Graphic Rendition
                CharAttributes(parameters);
                break;
            case "r": // DECSTBM - Set Top and Bottom Margins
                SetScrollRegion(parameters);
                break;
            case "h": // SM - Set Mode
                SetMode(parameters);
                break;
            case "l": // RM - Reset Mode
                ResetMode(parameters);
                break;
        }
    }

    /// <summary>
    /// Handles ESC sequences.
    /// </summary>
    public void HandleEsc(string finalChar, string collected)
    {
        switch (finalChar)
        {
            case "D": // IND - Index
                Index();
                break;
            case "E": // NEL - Next Line
                NextLine();
                break;
            case "M": // RI - Reverse Index
                ReverseIndex();
                break;
            case "c": // RIS - Reset to Initial State
                Reset();
                break;
            case "7": // DECSC - Save Cursor
                SaveCursor();
                break;
            case "8": // DECRC - Restore Cursor
                RestoreCursor();
                break;
        }
    }

    /// <summary>
    /// Handles OSC sequences (Operating System Command).
    /// </summary>
    public void HandleOsc(string data)
    {
        var parts = data.Split(new[] { ';' }, 2);
        if (parts.Length < 2)
            return;

        var command = parts[0];
        var arg = parts[1];

        switch (command)
        {
            case "0": // Set window title and icon
            case "2": // Set window title
                _terminal.Title = arg;
                break;
        }
    }

    // CSI Handler Implementations

    private void CursorUp(Params parameters)
    {
        var count = Math.Max(parameters.GetParam(0, 1), 1);
        _buffer.SetCursor(_buffer.X, Math.Max(_buffer.Y - count, 0));
    }

    private void CursorDown(Params parameters)
    {
        var count = Math.Max(parameters.GetParam(0, 1), 1);
        _buffer.SetCursor(_buffer.X, Math.Min(_buffer.Y + count, _terminal.Rows - 1));
    }

    private void CursorForward(Params parameters)
    {
        var count = Math.Max(parameters.GetParam(0, 1), 1);
        _buffer.SetCursor(Math.Min(_buffer.X + count, _terminal.Cols - 1), _buffer.Y);
    }

    private void CursorBackward(Params parameters)
    {
        var count = Math.Max(parameters.GetParam(0, 1), 1);
        _buffer.SetCursor(Math.Max(_buffer.X - count, 0), _buffer.Y);
    }

    private void CursorNextLine(Params parameters)
    {
        var count = Math.Max(parameters.GetParam(0, 1), 1);
        _buffer.SetCursor(0, Math.Min(_buffer.Y + count, _terminal.Rows - 1));
    }

    private void CursorPrecedingLine(Params parameters)
    {
        var count = Math.Max(parameters.GetParam(0, 1), 1);
        _buffer.SetCursor(0, Math.Max(_buffer.Y - count, 0));
    }

    private void CursorCharAbsolute(Params parameters)
    {
        var col = Math.Max(parameters.GetParam(0, 1), 1) - 1;
        _buffer.SetCursor(col, _buffer.Y);
    }

    private void CursorPosition(Params parameters)
    {
        var row = Math.Max(parameters.GetParam(0, 1), 1) - 1;
        var col = Math.Max(parameters.GetParam(1, 1), 1) - 1;
        _buffer.SetCursor(col, row);
    }

    private void EraseInDisplay(Params parameters)
    {
        var mode = parameters.GetParam(0, 0);
        var emptyCell = new BufferCell
        {
            Content = " ",
            Width = 1,
            Attributes = _curAttr.Clone(),
            CodePoint = 0x20
        };

        switch (mode)
        {
            case 0: // Erase below
                EraseInLine(parameters); // Current line from cursor
                for (int i = _buffer.Y + 1; i < _terminal.Rows; i++)
                {
                    _buffer.Lines[_buffer.YBase + i]?.Fill(emptyCell);
                }
                break;
            case 1: // Erase above
                for (int i = 0; i < _buffer.Y; i++)
                {
                    _buffer.Lines[_buffer.YBase + i]?.Fill(emptyCell);
                }
                EraseInLine(parameters); // Current line to cursor
                break;
            case 2: // Erase all
            case 3: // Erase scrollback (extension)
                for (int i = 0; i < _terminal.Rows; i++)
                {
                    _buffer.Lines[_buffer.YBase + i]?.Fill(emptyCell);
                }
                break;
        }
    }

    private void EraseInLine(Params parameters)
    {
        var mode = parameters.GetParam(0, 0);
        var line = _buffer.Lines[_buffer.Y + _buffer.YBase];
        if (line == null)
            return;

        var emptyCell = new BufferCell
        {
            Content = " ",
            Width = 1,
            Attributes = _curAttr.Clone(),
            CodePoint = 0x20
        };

        switch (mode)
        {
            case 0: // Erase to right
                line.Fill(emptyCell, _buffer.X, _terminal.Cols);
                break;
            case 1: // Erase to left
                line.Fill(emptyCell, 0, _buffer.X + 1);
                break;
            case 2: // Erase entire line
                line.Fill(emptyCell);
                break;
        }
    }

    private void InsertLines(Params parameters)
    {
        var count = Math.Max(parameters.GetParam(0, 1), 1);
        // Only works in scroll region
        if (_buffer.Y < _buffer.ScrollTop || _buffer.Y > _buffer.ScrollBottom)
            return;

        for (int i = 0; i < count; i++)
        {
            _buffer.Lines.Splice(_buffer.ScrollBottom, 1);
            _buffer.Lines.Splice(_buffer.Y + _buffer.YBase, 0, 
                _buffer.GetBlankLine(_curAttr));
        }
    }

    private void DeleteLines(Params parameters)
    {
        var count = Math.Max(parameters.GetParam(0, 1), 1);
        
        for (int i = 0; i < count; i++)
        {
            _buffer.Lines.Splice(_buffer.Y + _buffer.YBase, 1);
            _buffer.Lines.Splice(_buffer.ScrollBottom, 0, 
                _buffer.GetBlankLine(_curAttr));
        }
    }

    private void InsertChars(Params parameters)
    {
        var count = Math.Max(parameters.GetParam(0, 1), 1);
        var line = _buffer.Lines[_buffer.Y + _buffer.YBase];
        line?.CopyCellsFrom(line, _buffer.X, _buffer.X + count, 
            _terminal.Cols - _buffer.X - count, false);
    }

    private void DeleteChars(Params parameters)
    {
        var count = Math.Max(parameters.GetParam(0, 1), 1);
        var line = _buffer.Lines[_buffer.Y + _buffer.YBase];
        line?.CopyCellsFrom(line, _buffer.X + count, _buffer.X, 
            _terminal.Cols - _buffer.X - count, false);
    }

    private void EraseChars(Params parameters)
    {
        var count = Math.Max(parameters.GetParam(0, 1), 1);
        var line = _buffer.Lines[_buffer.Y + _buffer.YBase];
        
        var emptyCell = new BufferCell
        {
            Content = " ",
            Width = 1,
            Attributes = _curAttr.Clone(),
            CodePoint = 0x20
        };

        line?.Fill(emptyCell, _buffer.X, Math.Min(_buffer.X + count, _terminal.Cols));
    }

    private void ScrollUp(Params parameters)
    {
        var count = Math.Max(parameters.GetParam(0, 1), 1);
        _buffer.ScrollUp(count);
    }

    private void ScrollDown(Params parameters)
    {
        var count = Math.Max(parameters.GetParam(0, 1), 1);
        _buffer.ScrollDown(count);
    }

    private void CharAttributes(Params parameters)
    {
        if (parameters.Length == 0)
        {
            _curAttr = AttributeData.Default;
            return;
        }

        for (int i = 0; i < parameters.Length; i++)
        {
            var param = parameters.GetParam(i, 0);
            
            switch (param)
            {
                case 0: // Reset
                    _curAttr = AttributeData.Default;
                    break;
                case 1: // Bold
                    _curAttr.SetBold(true);
                    break;
                case 2: // Dim
                    _curAttr.SetDim(true);
                    break;
                case 3: // Italic
                    _curAttr.SetItalic(true);
                    break;
                case 4: // Underline
                    _curAttr.SetUnderline(true);
                    break;
                case 5: // Blink
                    _curAttr.SetBlink(true);
                    break;
                case 7: // Inverse
                    _curAttr.SetInverse(true);
                    break;
                case 8: // Invisible
                    _curAttr.SetInvisible(true);
                    break;
                case 9: // Strikethrough
                    _curAttr.SetStrikethrough(true);
                    break;
                case 22: // Not bold/dim
                    _curAttr.SetBold(false);
                    _curAttr.SetDim(false);
                    break;
                case 23: // Not italic
                    _curAttr.SetItalic(false);
                    break;
                case 24: // Not underline
                    _curAttr.SetUnderline(false);
                    break;
                case 27: // Not inverse
                    _curAttr.SetInverse(false);
                    break;
                case 28: // Not invisible
                    _curAttr.SetInvisible(false);
                    break;
                case 29: // Not strikethrough
                    _curAttr.SetStrikethrough(false);
                    break;
                case >= 30 and <= 37: // Foreground color
                    _curAttr.SetFgColor(param - 30);
                    break;
                case 38: // Extended foreground color
                    i = HandleExtendedColor(parameters, i, true);
                    break;
                case 39: // Default foreground
                    _curAttr.SetFgColor(256);
                    break;
                case >= 40 and <= 47: // Background color
                    _curAttr.SetBgColor(param - 40);
                    break;
                case 48: // Extended background color
                    i = HandleExtendedColor(parameters, i, false);
                    break;
                case 49: // Default background
                    _curAttr.SetBgColor(257);
                    break;
                case >= 90 and <= 97: // Bright foreground color
                    _curAttr.SetFgColor(param - 90 + 8);
                    break;
                case >= 100 and <= 107: // Bright background color
                    _curAttr.SetBgColor(param - 100 + 8);
                    break;
            }
        }
    }

    private int HandleExtendedColor(Params parameters, int index, bool isForeground)
    {
        if (index + 1 >= parameters.Length)
            return index;

        var colorType = parameters.GetParam(index + 1, 0);
        
        if (colorType == 2 && index + 4 < parameters.Length) // RGB
        {
            var r = parameters.GetParam(index + 2, 0);
            var g = parameters.GetParam(index + 3, 0);
            var b = parameters.GetParam(index + 4, 0);
            var rgb = (r << 16) | (g << 8) | b;
            
            if (isForeground)
                _curAttr.SetFgColor(rgb, 1);
            else
                _curAttr.SetBgColor(rgb, 1);
            
            return index + 4;
        }
        else if (colorType == 5 && index + 2 < parameters.Length) // 256 color
        {
            var color = parameters.GetParam(index + 2, 0);
            
            if (isForeground)
                _curAttr.SetFgColor(color);
            else
                _curAttr.SetBgColor(color);
            
            return index + 2;
        }

        return index;
    }

    private void SetScrollRegion(Params parameters)
    {
        var top = Math.Max(parameters.GetParam(0, 1), 1) - 1;
        var bottom = Math.Max(parameters.GetParam(1, _terminal.Rows), 1) - 1;
        _buffer.SetScrollRegion(top, bottom);
    }

    private void SetMode(Params parameters)
    {
        for (int i = 0; i < parameters.Length; i++)
        {
            var mode = parameters.GetParam(i, 0);
            // Mode handling would go here
        }
    }

    private void ResetMode(Params parameters)
    {
        for (int i = 0; i < parameters.Length; i++)
        {
            var mode = parameters.GetParam(i, 0);
            // Mode handling would go here
        }
    }

    // ESC Handler Implementations

    private void Index()
    {
        if (_buffer.Y == _buffer.ScrollBottom)
        {
            _buffer.ScrollUp(1);
        }
        else
        {
            _buffer.SetCursor(_buffer.X, _buffer.Y + 1);
        }
    }

    private void NextLine()
    {
        Index();
        _buffer.SetCursor(0, _buffer.Y);
    }

    private void ReverseIndex()
    {
        if (_buffer.Y == _buffer.ScrollTop)
        {
            _buffer.ScrollDown(1);
        }
        else
        {
            _buffer.SetCursor(_buffer.X, _buffer.Y - 1);
        }
    }

    private void Reset()
    {
        _terminal.Reset();
    }

    private void SaveCursor()
    {
        _buffer.SavedCursorState.X = _buffer.X;
        _buffer.SavedCursorState.Y = _buffer.Y;
        _buffer.SavedCursorState.Attr = _curAttr.Clone();
    }

    private void RestoreCursor()
    {
        _buffer.SetCursor(_buffer.SavedCursorState.X, _buffer.SavedCursorState.Y);
        _curAttr = _buffer.SavedCursorState.Attr.Clone();
    }

    // Utility Methods

    private int GetStringCellWidth(string str)
    {
        // Simple width calculation - would need proper Unicode width handling
        if (string.IsNullOrEmpty(str))
            return 0;

        // Basic implementation - should handle emoji, CJK, etc.
        var runes = str.EnumerateRunes();
        var width = 0;
        foreach (var rune in runes)
        {
            // Simplified: check for wide characters
            if (rune.Value >= 0x1100 && rune.Value <= 0x115F ||
                rune.Value >= 0x2E80 && rune.Value <= 0x9FFF ||
                rune.Value >= 0xAC00 && rune.Value <= 0xD7AF ||
                rune.Value >= 0xF900 && rune.Value <= 0xFAFF ||
                rune.Value >= 0x20000 && rune.Value <= 0x2FFFF)
            {
                width += 2;
            }
            else
            {
                width += 1;
            }
        }
        return width;
    }

    public void SetBuffer(Buffer.Buffer buffer)
    {
        _buffer = buffer;
    }
}
