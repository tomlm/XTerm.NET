using XTerm.NET.Buffer;
using XTerm.NET.Common;
using XTerm.NET.Parser;
using XTerm.NET.Options;
using XTerm.NET.Input;

namespace XTerm.NET;

/// <summary>
/// Main terminal class - the core of xterm.js functionality.
/// Manages buffer, parser, input handler, and terminal state.
/// </summary>
public class Terminal
{
    private readonly EscapeSequenceParser _parser;
    private readonly InputHandler _inputHandler;
    private readonly KeyboardInputGenerator _keyboardInput;
    private Buffer.Buffer _buffer;
    private Buffer.Buffer? _normalBuffer;
    private Buffer.Buffer? _altBuffer;
    private bool _usingAltBuffer;

    public TerminalOptions Options { get; }
    public Buffer.Buffer Buffer => _buffer;
    public int Cols { get; private set; }
    public int Rows { get; private set; }

    // Terminal state
    public bool InsertMode { get; set; }
    public bool ApplicationCursorKeys { get; set; }
    public bool ApplicationKeypad { get; set; }
    public bool BracketedPasteMode { get; set; }
    public bool OriginMode { get; set; }
    public bool CursorVisible { get; set; }
    public bool ReverseWraparound { get; set; }
    public bool SendFocusEvents { get; set; }
    public string Title { get; set; }
    public string? CurrentDirectory { get; set; }
    public string? CurrentHyperlink { get; set; }
    public string? HyperlinkId { get; set; }

    // Events
    public EventEmitter<string> OnData { get; }
    public EventEmitter<string> OnTitleChange { get; }
    public EventEmitter OnBell { get; }
    public EventEmitter<(int cols, int rows)> OnResize { get; }
    public EventEmitter OnScroll { get; }
    public EventEmitter<string> OnLineFeed { get; }
    public EventEmitter OnCursorMove { get; }
    public EventEmitter<string> OnDirectoryChange { get; }
    public EventEmitter<string> OnHyperlink { get; } // New event for hyperlinks

    public Terminal(TerminalOptions? options = null)
    {
        Options = options ?? new TerminalOptions();
        Cols = Options.Cols;
        Rows = Options.Rows;
        Title = string.Empty;

        // Initialize buffers
        _normalBuffer = new Buffer.Buffer(Cols, Rows, Options.Scrollback);
        _altBuffer = new Buffer.Buffer(Cols, Rows, 0); // Alt buffer has no scrollback
        _buffer = _normalBuffer;
        _usingAltBuffer = false;

        // Initialize parser and input handler
        _parser = new EscapeSequenceParser();
        _inputHandler = new InputHandler(this);
        _keyboardInput = new KeyboardInputGenerator(this); // Initialize keyboard input generator

        // Wire up parser handlers
        _parser.PrintHandler = data => _inputHandler.Print(data);
        _parser.ExecuteHandler = code => HandleExecute(code);
        _parser.CsiHandler = (identifier, parameters) => _inputHandler.HandleCsi(identifier, parameters);
        _parser.EscHandler = (finalChar, collected) => _inputHandler.HandleEsc(finalChar, collected);
        _parser.OscHandler = data => _inputHandler.HandleOsc(data);

        // Initialize events
        OnData = new EventEmitter<string>();
        OnTitleChange = new EventEmitter<string>();
        OnBell = new EventEmitter();
        OnResize = new EventEmitter<(int cols, int rows)>();
        OnScroll = new EventEmitter();
        OnLineFeed = new EventEmitter<string>();
        OnCursorMove = new EventEmitter();
        OnDirectoryChange = new EventEmitter<string>();
        OnHyperlink = new EventEmitter<string>(); // Initialize new event

        InsertMode = false;
        ApplicationCursorKeys = false;
        ApplicationKeypad = false;
        BracketedPasteMode = false;
        OriginMode = false;
        CursorVisible = true;
        ReverseWraparound = false;
        SendFocusEvents = false;
    }

    /// <summary>
    /// Writes data to the terminal.
    /// </summary>
    public void Write(string data)
    {
        if (string.IsNullOrEmpty(data))
            return;

        _parser.Parse(data);
    }

    /// <summary>
    /// Writes data to the terminal as a line (adds line feed).
    /// </summary>
    public void WriteLine(string data)
    {
        Write(data + "\n");
    }

    /// <summary>
    /// Resizes the terminal.
    /// </summary>
    public void Resize(int cols, int rows)
    {
        if (cols == Cols && rows == Rows)
            return;

        var oldCols = Cols;
        var oldRows = Rows;

        Cols = cols;
        Rows = rows;

        // Resize buffers
        _normalBuffer?.Resize(cols, rows);
        _altBuffer?.Resize(cols, rows);

        OnResize.Fire((cols, rows));
    }

    /// <summary>
    /// Resets the terminal to initial state.
    /// </summary>
    public void Reset()
    {
        // Reset to normal buffer
        if (_usingAltBuffer)
        {
            _buffer = _normalBuffer!;
            _usingAltBuffer = false;
        }

        // Reset parser
        _parser.Reset();

        // Reset modes
        InsertMode = false;
        ApplicationCursorKeys = false;
        ApplicationKeypad = false;
        BracketedPasteMode = false;
        OriginMode = false;
        CursorVisible = true;
        ReverseWraparound = false;
        SendFocusEvents = false;

        // Reset cursor
        _buffer.SetCursor(0, 0);
        _buffer.ResetScrollRegion();

        // Clear buffers
        ClearBuffer();
    }

    /// <summary>
    /// Clears the entire buffer.
    /// </summary>
    public void Clear()
    {
        ClearBuffer();
    }

    private void ClearBuffer()
    {
        for (int i = 0; i < Rows; i++)
        {
            var line = _buffer.Lines[i];
            line?.Fill(BufferCell.Null);
        }
        _buffer.SetCursor(0, 0);
    }

    /// <summary>
    /// Scrolls the viewport by a specified number of lines.
    /// </summary>
    public void ScrollLines(int lines)
    {
        _buffer.ScrollDisp(lines);
        OnScroll.Fire();
    }

    /// <summary>
    /// Scrolls the viewport to the top.
    /// </summary>
    public void ScrollToTop()
    {
        _buffer.ScrollToTop();
        OnScroll.Fire();
    }

    /// <summary>
    /// Scrolls the viewport to the bottom.
    /// </summary>
    public void ScrollToBottom()
    {
        _buffer.ScrollToBottom();
        OnScroll.Fire();
    }

    /// <summary>
    /// Gets the content of a line as a string.
    /// </summary>
    public string GetLine(int line)
    {
        if (line < 0 || line >= _buffer.Lines.Length)
            return string.Empty;
            
        var bufferLine = _buffer.Lines[line];
        return bufferLine?.TranslateToString(true) ?? string.Empty;
    }

    /// <summary>
    /// Gets all visible lines as strings.
    /// </summary>
    public string[] GetVisibleLines()
    {
        var lines = new string[Rows];
        for (int i = 0; i < Rows; i++)
        {
            lines[i] = GetLine(_buffer.YDisp + i);
        }
        return lines;
    }

    /// <summary>
    /// Generates an escape sequence for a key press.
    /// </summary>
    /// <param name="key">The key that was pressed</param>
    /// <param name="modifiers">Modifier keys (Shift, Alt, Control)</param>
    /// <returns>The escape sequence string to send to the application</returns>
    public string GenerateKeyInput(Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        return _keyboardInput.GenerateKeySequence(key, modifiers);
    }

    /// <summary>
    /// Generates an escape sequence for a character with modifiers.
    /// </summary>
    /// <param name="c">The character that was typed</param>
    /// <param name="modifiers">Modifier keys (Shift, Alt, Control)</param>
    /// <returns>The escape sequence string to send to the application</returns>
    public string GenerateCharInput(char c, KeyModifiers modifiers = KeyModifiers.None)
    {
        return _keyboardInput.GenerateCharSequence(c, modifiers);
    }

    /// <summary>
    /// Switches to the alternate buffer.
    /// </summary>
    public void SwitchToAltBuffer()
    {
        if (_usingAltBuffer)
            return;

        _buffer = _altBuffer!;
        _usingAltBuffer = true;
        _inputHandler.SetBuffer(_buffer);
    }

    /// <summary>
    /// Switches to the normal buffer.
    /// </summary>
    public void SwitchToNormalBuffer()
    {
        if (!_usingAltBuffer)
            return;

        _buffer = _normalBuffer!;
        _usingAltBuffer = false;
        _inputHandler.SetBuffer(_buffer);
    }

    /// <summary>
    /// Handles C0 control characters.
    /// </summary>
    private void HandleExecute(int code)
    {
        switch (code)
        {
            case 0x07: // BEL
                OnBell.Fire();
                break;

            case 0x08: // BS - Backspace
                if (_buffer.X > 0)
                {
                    _buffer.SetCursor(_buffer.X - 1, _buffer.Y);
                }
                break;

            case 0x09: // HT - Tab
                {
                    var nextTabStop = ((_buffer.X + 8) / 8) * 8;
                    _buffer.SetCursor(Math.Min(nextTabStop, Cols - 1), _buffer.Y);
                }
                break;

            case 0x0A: // LF - Line Feed
            case 0x0B: // VT - Vertical Tab
            case 0x0C: // FF - Form Feed
                LineFeed();
                break;

            case 0x0D: // CR - Carriage Return
                _buffer.SetCursor(0, _buffer.Y);
                break;

            case 0x0E: // SO - Shift Out (select G1 charset)
            case 0x0F: // SI - Shift In (select G0 charset)
                // Character set handling
                break;
        }
    }

    /// <summary>
    /// Performs a line feed operation.
    /// </summary>
    private void LineFeed()
    {
        if (_buffer.Y == _buffer.ScrollBottom)
        {
            // Scroll up
            _buffer.ScrollUp(1);
        }
        else
        {
            // Move cursor down
            _buffer.SetCursor(_buffer.X, _buffer.Y + 1);
        }

        OnLineFeed.Fire("\n");
    }

    /// <summary>
    /// Disposes the terminal and releases resources.
    /// </summary>
    public void Dispose()
    {
        OnData.Clear();
        OnTitleChange.Clear();
        OnBell.Clear();
        OnResize.Clear();
        OnScroll.Clear();
        OnLineFeed.Clear();
        OnCursorMove.Clear();
        OnDirectoryChange.Clear();
    }
}
