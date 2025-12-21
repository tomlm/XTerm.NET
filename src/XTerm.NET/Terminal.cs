using XTerm.Buffer;
using XTerm.Common;
using XTerm.Parser;
using XTerm.Options;
using XTerm.Input;
using XTerm.Events.Parser;
using XTerm.Events;

namespace XTerm;

/// <summary>
/// Main terminal class - the core of xterm.js functionality.
/// Manages buffer, parser, input handler, and terminal state.
/// </summary>
public class Terminal
{
    private readonly EscapeSequenceParser _parser;
    private readonly InputHandler _inputHandler;
    private readonly KeyboardInputGenerator _keyboardInput;
    private readonly MouseTracker _mouseTracker;
    private Buffer.TerminalBuffer _buffer;
    private Buffer.TerminalBuffer? _normalBuffer;
    private Buffer.TerminalBuffer? _altBuffer;
    private bool _usingAltBuffer;

    public TerminalOptions Options { get; }
    public Buffer.TerminalBuffer Buffer => _buffer;
    public int Cols { get; private set; }
    public int Rows { get; private set; }
    public BufferType ActiveBuffer => _usingAltBuffer ? BufferType.Alternate : BufferType.Normal;
    public bool IsAlternateBufferActive => _usingAltBuffer;

    // Terminal state
    public bool InsertMode { get; set; }
    public bool ApplicationCursorKeys { get; set; }
    public bool ApplicationKeypad { get; set; }
    public bool BracketedPasteMode { get; set; }
    public bool OriginMode { get; set; }
    public bool CursorVisible { get; set; }
    public bool ReverseWraparound { get; set; }
    public bool SendFocusEvents { get; set; }
    public bool Win32InputMode { get; set; }
    public string Title { get; set; }
    public string? CurrentDirectory { get; set; }
    public string? CurrentHyperlink { get; set; }
    public string? HyperlinkId { get; set; }

    /// <summary>
    /// Fired when the cursor style or blink setting changes.
    /// </summary>
    public event EventHandler<TerminalEvents.CursorStyleChangedEventArgs>? CursorStyleChanged;

    // Events - Standard C# EventHandler pattern
    /// <summary>
    /// Fired when the terminal wants to send data back to the application.
    /// </summary>
    public event EventHandler<TerminalEvents.DataEventArgs>? DataReceived;

    /// <summary>
    /// Fired when the terminal title changes.
    /// </summary>
    public event EventHandler<TerminalEvents.TitleChangeEventArgs>? TitleChanged;

    /// <summary>
    /// Fired when the terminal bell is activated.
    /// </summary>
    public event EventHandler? BellRang;

    /// <summary>
    /// Fired when the terminal is resized.
    /// </summary>
    public event EventHandler<TerminalEvents.ResizeEventArgs>? Resized;

    /// <summary>
    /// Fired when the viewport scrolls.
    /// </summary>
    public event EventHandler? Scrolled;

    /// <summary>
    /// Fired when a line feed occurs.
    /// </summary>
    public event EventHandler<TerminalEvents.LineFeedEventArgs>? LineFed;

    /// <summary>
    /// Fired when the cursor moves.
    /// </summary>
    public event EventHandler? CursorMoved;

    /// <summary>
    /// Fired when the current directory changes.
    /// </summary>
    public event EventHandler<TerminalEvents.DirectoryChangeEventArgs>? DirectoryChanged;

    /// <summary>
    /// Fired when a hyperlink is encountered.
    /// </summary>
    public event EventHandler<TerminalEvents.HyperlinkEventArgs>? HyperlinkChanged;

    // Window manipulation events
    /// <summary>
    /// Fired when a window move command is received.
    /// </summary>
    public event EventHandler<TerminalEvents.WindowMovedEventArgs>? WindowMoved;

    /// <summary>
    /// Fired when a window resize command is received.
    /// </summary>
    public event EventHandler<TerminalEvents.WindowResizedEventArgs>? WindowResized;

    /// <summary>
    /// Fired when a window minimize command is received.
    /// </summary>
    public event EventHandler? WindowMinimized;

    /// <summary>
    /// Fired when a window maximize command is received.
    /// </summary>
    public event EventHandler? WindowMaximized;

    /// <summary>
    /// Fired when a window restore command is received.
    /// </summary>
    public event EventHandler? WindowRestored;

    /// <summary>
    /// Fired when a window raise command is received.
    /// </summary>
    public event EventHandler? WindowRaised;

    /// <summary>
    /// Fired when a window lower command is received.
    /// </summary>
    public event EventHandler? WindowLowered;

    /// <summary>
    /// Fired when a window refresh command is received.
    /// </summary>
    public event EventHandler? WindowRefreshed;

    /// <summary>
    /// Fired when a window fullscreen command is received.
    /// </summary>
    public event EventHandler? WindowFullscreened;

    /// <summary>
    /// Fired when window information is requested.
    /// </summary>
    public event EventHandler<TerminalEvents.WindowInfoRequestedEventArgs>? WindowInfoRequested;

    /// <summary>
    /// Fired when the active buffer is changed.
    /// </summary>
    public event EventHandler<TerminalEvents.BufferChangedEventArgs>? BufferChanged;

    public Terminal(TerminalOptions? options = null)
    {
        Options = options ?? new TerminalOptions();
        Cols = Options.Cols;
        Rows = Options.Rows;
        Title = string.Empty;

        // Initialize buffers
        _normalBuffer = new Buffer.TerminalBuffer(Cols, Rows, Options.Scrollback);
        _altBuffer = new Buffer.TerminalBuffer(Cols, Rows, 0); // Alt buffer has no scrollback
        _buffer = _normalBuffer;
        _usingAltBuffer = false;

        // Initialize parser and input handler
        _parser = new EscapeSequenceParser();
        _inputHandler = new InputHandler(this);
        _keyboardInput = new KeyboardInputGenerator(this);
        _mouseTracker = new MouseTracker(this); // Initialize mouse tracker

        // Subscribe to parser events using C# event pattern
        _parser.Print += OnParserPrint;
        _parser.Execute += OnParserExecute;
        _parser.Csi += OnParserCsi;
        _parser.Esc += OnParserEsc;
        _parser.Osc += OnParserOsc;

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
    /// Handles print events from the parser.
    /// </summary>
    private void OnParserPrint(object? sender, PrintEventArgs e)
    {
        _inputHandler.Print(e.Data);
    }

    /// <summary>
    /// Handles execute events from the parser.
    /// </summary>
    private void OnParserExecute(object? sender, ExecuteEventArgs e)
    {
        HandleExecute(e.Code);
    }

    /// <summary>
    /// Handles CSI events from the parser.
    /// </summary>
    private void OnParserCsi(object? sender, CsiEventArgs e)
    {
        _inputHandler.HandleCsi(e.Identifier, e.Parameters);
    }

    /// <summary>
    /// Handles ESC events from the parser.
    /// </summary>
    private void OnParserEsc(object? sender, EscEventArgs e)
    {
        _inputHandler.HandleEsc(e.FinalChar, e.Collected);
    }

    /// <summary>
    /// Handles OSC events from the parser.
    /// </summary>
    private void OnParserOsc(object? sender, OscEventArgs e)
    {
        _inputHandler.HandleOsc(e.Data);
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
        Write(data + "\r\n");
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

        Resized?.Invoke(this, new TerminalEvents.ResizeEventArgs(cols, rows));
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
            line?.Fill(BufferCell.Empty);
        }
        _buffer.SetCursor(0, 0);
    }

    /// <summary>
    /// Scrolls the viewport by a specified number of lines.
    /// </summary>
    public void ScrollLines(int lines)
    {
        _buffer.ScrollDisp(lines);
        Scrolled?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Scrolls the viewport to the top.
    /// </summary>
    public void ScrollToTop()
    {
        _buffer.ScrollToTop();
        Scrolled?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Scrolls the viewport to the bottom.
    /// </summary>
    public void ScrollToBottom()
    {
        _buffer.ScrollToBottom();
        Scrolled?.Invoke(this, EventArgs.Empty);
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
    /// Generates an escape sequence for a mouse event.
    /// </summary>
    /// <param name="button">The mouse button</param>
    /// <param name="x">The column position (0-based)</param>
    /// <param name="y">The row position (0-based)</param>
    /// <param name="eventType">The type of mouse event</param>
    /// <param name="modifiers">Modifier keys held during the event</param>
    /// <returns>The escape sequence string to send to the application</returns>
    public string GenerateMouseEvent(MouseButton button, int x, int y, MouseEventType eventType, KeyModifiers modifiers = KeyModifiers.None)
    {
        return _mouseTracker.GenerateMouseEvent(button, x, y, eventType, modifiers);
    }

    /// <summary>
    /// Generates an escape sequence for a focus event (focus in/out).
    /// </summary>
    /// <param name="focused">True if focused, false if lost focus</param>
    /// <returns>The escape sequence string to send to the application</returns>
    public string GenerateFocusEvent(bool focused)
    {
        return _mouseTracker.GenerateFocusEvent(focused);
    }

    /// <summary>
    /// Gets the current mouse tracking mode.
    /// </summary>
    public MouseTrackingMode MouseTrackingMode => _mouseTracker.TrackingMode;

    /// <summary>
    /// Gets the current mouse encoding format.
    /// </summary>
    public MouseEncoding MouseEncoding => _mouseTracker.Encoding;

    /// <summary>
    /// Gets the mouse tracker (internal use for mode setting).
    /// </summary>
    internal MouseTracker GetMouseTracker() => _mouseTracker;

    // Internal methods for raising events (called by InputHandler)
    internal void RaiseDataReceived(string data) => 
        DataReceived?.Invoke(this, new TerminalEvents.DataEventArgs(data));
    
    internal void RaiseTitleChanged(string title) => 
        TitleChanged?.Invoke(this, new TerminalEvents.TitleChangeEventArgs(title));
    
    internal void RaiseDirectoryChanged(string directory) => 
        DirectoryChanged?.Invoke(this, new TerminalEvents.DirectoryChangeEventArgs(directory));
    
    internal void RaiseWindowMoved(int x, int y) => 
        WindowMoved?.Invoke(this, new TerminalEvents.WindowMovedEventArgs(x, y));
    
    internal void RaiseWindowResized(int width, int height) => 
        WindowResized?.Invoke(this, new TerminalEvents.WindowResizedEventArgs(width, height));
    
    internal void RaiseWindowMinimized() => 
        WindowMinimized?.Invoke(this, EventArgs.Empty);
    
    internal void RaiseWindowMaximized() => 
        WindowMaximized?.Invoke(this, EventArgs.Empty);
    
    internal void RaiseWindowRestored() => 
        WindowRestored?.Invoke(this, EventArgs.Empty);
    
    internal void RaiseWindowRaised() => 
        WindowRaised?.Invoke(this, EventArgs.Empty);
    
    internal void RaiseWindowLowered() => 
        WindowLowered?.Invoke(this, EventArgs.Empty);
    
    internal void RaiseWindowRefreshed() => 
        WindowRefreshed?.Invoke(this, EventArgs.Empty);
    
    internal void RaiseWindowFullscreened() => 
        WindowFullscreened?.Invoke(this, EventArgs.Empty);
    
    internal void RaiseWindowInfoRequested(WindowInfoRequest request) => 
        WindowInfoRequested?.Invoke(this, new TerminalEvents.WindowInfoRequestedEventArgs(request));

    /// <summary>
    /// Updates cursor style and blink settings and notifies listeners if changed.
    /// </summary>
    /// <param name="style">Cursor rendering style.</param>
    /// <param name="blink">Whether the cursor should blink.</param>
    public void SetCursorStyle(CursorStyle style, bool blink)
    {
        var changed = Options.CursorStyle != style || Options.CursorBlink != blink;
        Options.CursorStyle = style;
        Options.CursorBlink = blink;

        if (changed)
        {
            CursorStyleChanged?.Invoke(this, new TerminalEvents.CursorStyleChangedEventArgs(style, blink));
        }
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
        BufferChanged?.Invoke(this, new TerminalEvents.BufferChangedEventArgs(BufferType.Alternate));
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
        BufferChanged?.Invoke(this, new TerminalEvents.BufferChangedEventArgs(BufferType.Normal));
    }

    /// <summary>
    /// Handles C0 control characters.
    /// </summary>
    private void HandleExecute(int code)
    {
        switch (code)
        {
            case 0x07: // BEL
                BellRang?.Invoke(this, EventArgs.Empty);
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
                _inputHandler.ShiftOut();
                break;
                
            case 0x0F: // SI - Shift In (select G0 charset)
                _inputHandler.ShiftIn();
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

        // If ConvertEol is enabled, also do a carriage return (move to column 0)
        if (Options.ConvertEol)
        {
            _buffer.SetCursor(0, _buffer.Y);
        }

        LineFed?.Invoke(this, new TerminalEvents.LineFeedEventArgs("\n"));
    }

    /// <summary>
    /// Disposes the terminal and releases resources.
    /// </summary>
    public void Dispose()
    {
        // Unsubscribe from parser events
        _parser.Print -= OnParserPrint;
        _parser.Execute -= OnParserExecute;
        _parser.Csi -= OnParserCsi;
        _parser.Esc -= OnParserEsc;
        _parser.Osc -= OnParserOsc;

        // Clear all event subscriptions
        DataReceived = null;
        TitleChanged = null;
        BellRang = null;
        Resized = null;
        Scrolled = null;
        LineFed = null;
        CursorMoved = null;
        DirectoryChanged = null;
        HyperlinkChanged = null;
        
        // Clear window manipulation events
        WindowMoved = null;
        WindowResized = null;
        WindowMinimized = null;
        WindowMaximized = null;
        WindowRestored = null;
        WindowRaised = null;
        WindowLowered = null;
        WindowRefreshed = null;
        WindowFullscreened = null;
        WindowInfoRequested = null;
    }
}
