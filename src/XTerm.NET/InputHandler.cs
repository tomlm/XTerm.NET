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
        // Handle autowrap
        if (_buffer.X >= _terminal.Cols)
        {
            if (_terminal.Options.Wraparound)
            {
                if (_buffer.Y == _buffer.ScrollBottom)
                {
                    _buffer.SetCursor(0, _buffer.Y);
                    _buffer.ScrollUp(1, true);
                }
                else
                {
                    _buffer.SetCursor(0, _buffer.Y + 1);
                }
            }
            else
            {
                return; // Don't print beyond line edge
            }
        }
        
        var line = _buffer.Lines[_buffer.Y + _buffer.YBase];
        if (line == null)
            return;

        // Mark line as wrapped if we just wrapped
        if (_buffer.X == 0 && line != null)
        {
            line.IsWrapped = true;
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

        // Use MoveCursor to allow X to be one past the last column (pending wrap)
        _buffer.MoveCursor(_buffer.X + width, _buffer.Y);
    }

    /// <summary>
    /// Handles CSI sequences (Control Sequence Introducer).
    /// </summary>
    public void HandleCsi(string identifier, Params parameters)
    {
        // Check for DEC private mode sequences (CSI ? ...)
        bool isPrivate = identifier.StartsWith("?");
        string command = isPrivate ? identifier.Substring(1) : identifier;
        
        switch (command)
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
            case "I": // CHT - Cursor Forward Tabulation
                CursorForwardTab(parameters);
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
            case "Z": // CBT - Cursor Backward Tabulation
                CursorBackwardTab(parameters);
                break;
            case "c": // DA - Primary Device Attributes
                DeviceAttributes(parameters, isPrivate);
                break;
            case "d": // VPA - Line Position Absolute
                LinePositionAbsolute(parameters);
                break;
            case "m": // SGR - Select Graphic Rendition
                CharAttributes(parameters);
                break;
            case "n": // DSR - Device Status Report
                DeviceStatusReport(parameters, isPrivate);
                break;
            case "r": // DECSTBM - Set Top and Bottom Margins
                SetScrollRegion(parameters);
                break;
            case "s": // SCP - Save Cursor Position (ANSI)
                SaveCursorAnsi();
                break;
            case "u": // RCP - Restore Cursor Position (ANSI)
                RestoreCursorAnsi();
                break;
            case "h": // SM - Set Mode / DECSET
                if (isPrivate)
                {
                    // DEC Private Mode Set (CSI ? Pm h)
                    for (int i = 0; i < parameters.Length; i++)
                    {
                        var mode = parameters.GetParam(i, 0);
                        SetModeInternal(mode, isPrivate: true);
                    }
                }
                else
                {
                    SetMode(parameters);
                }
                break;
            case "l": // RM - Reset Mode / DECRST
                if (isPrivate)
                {
                    // DEC Private Mode Reset (CSI ? Pm l)
                    for (int i = 0; i < parameters.Length; i++)
                    {
                        var mode = parameters.GetParam(i, 0);
                        ResetModeInternal(mode, isPrivate: true);
                    }
                }
                else
                {
                    ResetMode(parameters);
                }
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

    private void SaveCursorAnsi()
    {
        // ANSI save cursor (CSI s) - same as DEC DECSC but simpler
        SaveCursor();
    }

    private void RestoreCursorAnsi()
    {
        // ANSI restore cursor (CSI u) - same as DEC DECRC but simpler
        RestoreCursor();
    }

    private void LinePositionAbsolute(Params parameters)
    {
        // VPA - Line Position Absolute (CSI d)
        var row = Math.Max(parameters.GetParam(0, 1), 1) - 1;
        
        // Respect origin mode
        if (_terminal.OriginMode)
        {
            row = Math.Clamp(row, _buffer.ScrollTop, _buffer.ScrollBottom);
        }
        else
        {
            row = Math.Clamp(row, 0, _terminal.Rows - 1);
        }
        
        _buffer.SetCursor(_buffer.X, row);
    }

    private void CursorForwardTab(Params parameters)
    {
        // CHT - Cursor Forward Tabulation (CSI I)
        var count = Math.Max(parameters.GetParam(0, 1), 1);
        var tabWidth = _terminal.Options.TabStopWidth;
        
        for (int i = 0; i < count; i++)
        {
            var nextTabStop = ((_buffer.X / tabWidth) + 1) * tabWidth;
            _buffer.SetCursor(Math.Min(nextTabStop, _terminal.Cols - 1), _buffer.Y);
        }
    }

    private void CursorBackwardTab(Params parameters)
    {
        // CBT - Cursor Backward Tabulation (CSI Z)
        var count = Math.Max(parameters.GetParam(0, 1), 1);
        var tabWidth = _terminal.Options.TabStopWidth;
        
        for (int i = 0; i < count; i++)
        {
            if (_buffer.X == 0)
                break;
                
            var prevTabStop = ((_buffer.X - 1) / tabWidth) * tabWidth;
            _buffer.SetCursor(Math.Max(prevTabStop, 0), _buffer.Y);
        }
    }

    private void DeviceAttributes(Params parameters, bool isPrivate)
    {
        // DA - Device Attributes (CSI c or CSI > c)
        if (isPrivate)
        {
            // Secondary DA (CSI > c) - Report terminal ID and version
            // Response: CSI > 0 ; version ; 0 c
            // We report as VT100-compatible
            _terminal.OnData.Fire("\x1B[>0;10;0c");
        }
        else
        {
            // Primary DA (CSI c) - Report device attributes
            // Response: CSI ? 1 ; 2 c (VT100 with AVO)
            // More complete: CSI ? 1 ; 2 ; 6 ; 9 c
            // 1 = 132 columns, 2 = Printer, 6 = Selective erase, 9 = National replacement character sets
            _terminal.OnData.Fire("\x1B[?1;2c");
        }
    }

    private void DeviceStatusReport(Params parameters, bool isPrivate)
    {
        // DSR - Device Status Report (CSI n or CSI ? n)
        var report = parameters.GetParam(0, 0);
        
        if (isPrivate)
        {
            // DEC-specific DSR
            switch (report)
            {
                case 6: // DECXCPR - Extended Cursor Position Report
                    // Report cursor position: CSI ? row ; col R
                    var row = _buffer.Y + 1; // 1-based
                    var col = _buffer.X + 1; // 1-based
                    _terminal.OnData.Fire($"\x1B[?{row};{col}R");
                    break;
                    
                case 15: // Printer status
                    // Report no printer: CSI ? 1 3 n
                    _terminal.OnData.Fire("\x1B[?13n");
                    break;
                    
                case 25: // UDK status
                    // Report UDK locked: CSI ? 2 1 n
                    _terminal.OnData.Fire("\x1B[?21n");
                    break;
                    
                case 26: // Keyboard status
                    // Report keyboard ready: CSI ? 2 7 ; 1 ; 0 ; 0 n
                    _terminal.OnData.Fire("\x1B[?27;1;0;0n");
                    break;
            }
        }
        else
        {
            // Standard ANSI DSR
            switch (report)
            {
                case 5: // Operating status
                    // Report OK: CSI 0 n
                    _terminal.OnData.Fire("\x1B[0n");
                    break;
                    
                case 6: // CPR - Cursor Position Report
                    // Report cursor position: CSI row ; col R
                    var row = _buffer.Y + 1; // 1-based
                    var col = _buffer.X + 1; // 1-based
                    
                    // Adjust for origin mode
                    if (_terminal.OriginMode)
                    {
                        row = row - _buffer.ScrollTop;
                    }
                    
                    _terminal.OnData.Fire($"\x1B[{row};{col}R");
                    break;
            }
        }
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
            SetModeInternal(mode, isPrivate: false);
        }
    }
    
    private void SetModeInternal(int mode, bool isPrivate)
    {
        if (isPrivate)
        {
            // DEC Private Modes (DECSET)
            switch (mode)
            {
                case CoreModes.APP_CURSOR_KEYS:
                    _terminal.ApplicationCursorKeys = true;
                    break;
                    
                case CoreModes.ORIGIN:
                    _terminal.OriginMode = true;
                    _buffer.SetCursor(0, 0);
                    break;
                    
                case CoreModes.WRAPAROUND:
                    _terminal.Options.Wraparound = true;
                    break;
                    
                case CoreModes.SHOW_CURSOR:
                    _terminal.CursorVisible = true;
                    break;
                    
                case CoreModes.APP_KEYPAD:
                    _terminal.ApplicationKeypad = true;
                    break;
                    
                case CoreModes.BRACKETED_PASTE_MODE:
                    _terminal.BracketedPasteMode = true;
                    break;
                    
                case CoreModes.ALT_BUFFER:
                    _terminal.SwitchToAltBuffer();
                    break;
                    
                case CoreModes.ALT_BUFFER_CURSOR:
                    SaveCursor();
                    _terminal.SwitchToAltBuffer();
                    break;
                    
                case CoreModes.ALT_BUFFER_FULL:
                    SaveCursor();
                    _terminal.SwitchToAltBuffer();
                    _buffer.SetCursor(0, 0);
                    EraseInDisplay(new Params()); // Clear screen
                    break;
                    
                case CoreModes.SEND_FOCUS_EVENTS:
                    _terminal.SendFocusEvents = true;
                    break;
                    
                case CoreModes.MOUSE_REPORT_CLICK:
                case CoreModes.MOUSE_REPORT_NORMAL:
                case CoreModes.MOUSE_REPORT_BTN_EVENT:
                case CoreModes.MOUSE_REPORT_ANY_EVENT:
                case CoreModes.MOUSE_REPORT_SGR:
                    // Mouse modes - not yet implemented
                    // TODO: Implement mouse tracking
                    break;
            }
        }
        else
        {
            // ANSI Modes (SM)
            switch (mode)
            {
                case CoreModes.INSERT_MODE:
                    _terminal.InsertMode = true;
                    break;
                    
                case CoreModes.AUTO_WRAP_MODE:
                    _terminal.Options.Wraparound = true;
                    break;
            }
        }
    }

    private void ResetMode(Params parameters)
    {
        for (int i = 0; i < parameters.Length; i++)
        {
            var mode = parameters.GetParam(i, 0);
            ResetModeInternal(mode, isPrivate: false);
        }
    }
    
    private void ResetModeInternal(int mode, bool isPrivate)
    {
        if (isPrivate)
        {
            // DEC Private Modes (DECRST)
            switch (mode)
            {
                case CoreModes.APP_CURSOR_KEYS:
                    _terminal.ApplicationCursorKeys = false;
                    break;
                    
                case CoreModes.ORIGIN:
                    _terminal.OriginMode = false;
                    _buffer.SetCursor(0, 0);
                    break;
                    
                case CoreModes.WRAPAROUND:
                    _terminal.Options.Wraparound = false;
                    break;
                    
                case CoreModes.SHOW_CURSOR:
                    _terminal.CursorVisible = false;
                    break;
                    
                case CoreModes.APP_KEYPAD:
                    _terminal.ApplicationKeypad = false;
                    break;
                    
                case CoreModes.BRACKETED_PASTE_MODE:
                    _terminal.BracketedPasteMode = false;
                    break;
                    
                case CoreModes.ALT_BUFFER:
                    _terminal.SwitchToNormalBuffer();
                    break;
                    
                case CoreModes.ALT_BUFFER_CURSOR:
                    _terminal.SwitchToNormalBuffer();
                    RestoreCursor();
                    break;
                    
                case CoreModes.ALT_BUFFER_FULL:
                    _terminal.SwitchToNormalBuffer();
                    RestoreCursor();
                    break;
                    
                case CoreModes.SEND_FOCUS_EVENTS:
                    _terminal.SendFocusEvents = false;
                    break;
                    
                case CoreModes.MOUSE_REPORT_CLICK:
                case CoreModes.MOUSE_REPORT_NORMAL:
                case CoreModes.MOUSE_REPORT_BTN_EVENT:
                case CoreModes.MOUSE_REPORT_ANY_EVENT:
                case CoreModes.MOUSE_REPORT_SGR:
                    // Mouse modes - not yet implemented
                    // TODO: Implement mouse tracking
                    break;
            }
        }
        else
        {
            // ANSI Modes (RM)
            switch (mode)
            {
                case CoreModes.INSERT_MODE:
                    _terminal.InsertMode = false;
                    break;
                    
                case CoreModes.AUTO_WRAP_MODE:
                    _terminal.Options.Wraparound = false;
                    break;
            }
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
