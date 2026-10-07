using XTerm.Buffer;
using XTerm.Common;
using XTerm.Parser;
using XTerm.Options;
using XTerm.Input;
using XTerm.Events.Parser;
using XTerm.Events;
using XTerm.Selection;

namespace XTerm;

/// <summary>
/// Main terminal class - the core of xterm.js functionality.
/// Manages buffer, parser, input handler, and terminal state.
/// </summary>
public class Terminal : IDisposable
{
    private readonly EscapeSequenceParser _parser;
    private readonly InputHandler _inputHandler;
    private readonly KeyboardInputGenerator _keyboardInput;
    private readonly MouseTracker _mouseTracker;
    private readonly SelectionManager _selectionManager;
    private Buffer.TerminalBuffer _buffer;
    private Buffer.TerminalBuffer? _normalBuffer;
    private Buffer.TerminalBuffer? _altBuffer;
    private bool _usingAltBuffer;

    /// <summary>
    /// This terminal's mutable runtime options. Construction snapshots the caller's options, so
    /// later changes to the caller's object are never observed. Options consulted during operation
    /// may be changed here; construction-time settings such as dimensions, scrollback and the
    /// initial theme require their dedicated runtime APIs or a new terminal.
    /// </summary>
    public TerminalOptions Options { get; }
    public Buffer.TerminalBuffer Buffer => _buffer;
    public int Cols { get; private set; }
    public int Rows { get; private set; }
    public BufferType ActiveBuffer => _usingAltBuffer ? BufferType.Alternate : BufferType.Normal;
    public bool IsAlternateBufferActive => _usingAltBuffer;

    // Terminal state
    /// <summary>LNM (SM 20): a line feed implies a carriage return. Stored beside the other
    /// modes the input handler tracks, acted on here because LF executes here.</summary>
    public bool LineFeedMode { get; set; }

    /// <summary>The terminator of the OSC being handled right now, for replies to mirror --
    /// xterm answers BEL-terminated queries with BEL and ST-terminated ones with ST.</summary>
    internal string OscReplyTerminator => _parser.LastOscEndedWithBel ? "\u0007" : "\u001b\\";

    /// <summary>
    /// DECSCL's operating level: 61 for VT100 through 65 for VT500, default 65. Features from
    /// above the level are gated off -- DECRQM needs 63, DECSLRM needs 64 -- which is what makes
    /// the level a real setting rather than a stored number.
    /// </summary>
    public int ConformanceLevel { get; set; } = 65;

    /// <summary>
    /// DECSET 41, xterm's "more(1) fix": a tab arriving while a wrap is pending wraps first and
    /// then tabs, instead of being absorbed at the phantom column.
    /// </summary>
    public bool MoreFixMode { get; set; }

    /// <summary>DECCOLM's current answer: whether the screen is in 132-column mode.</summary>
    public bool ColumnMode132 { get; private set; }

    // ---------------------------------------------------------------- DEC status line

    /// <summary>The status line's single row, or <see langword="null"/> when there is not one.</summary>
    /// <remarks>
    /// <para>Present only while DECSSDT has selected a type that HAS a row -- host-writable (2), and
    /// the indicator (1), whose contents the terminal would own. Null for type 0, which is the
    /// default and means the display has no status line at all.</para>
    /// <para>A host draws this below the text area. It is deliberately not one of the terminal's
    /// <see cref="Rows"/>: an application told it has N rows must have N rows it can write to, and
    /// every size report is computed from that count.</para>
    /// </remarks>
    public Buffer.BufferLine? StatusLine => _statusDisplayType == 0 ? null : _statusBuffer?.GetLine(0);

    /// <summary>
    /// DECSSDT's current answer: 0 none, 1 indicator, 2 host-writable.
    /// </summary>
    public int StatusDisplayType => _statusDisplayType;

    /// <summary>
    /// DECSASD's current answer: whether writes are going to the status line rather than the screen.
    /// </summary>
    public bool StatusLineActive => _statusLineActive;

    /// <summary>Raised when the status line's contents, type or cursor change.</summary>
    /// <remarks>
    /// Shaped like <see cref="TitleChanged"/> because a host consumes it the same way: something
    /// small changed that only the host can show. Raised on the write path, so a host that repaints
    /// on it repaints once per batch rather than per character.
    /// </remarks>
    public event EventHandler? StatusLineChanged;

    private Buffer.TerminalBuffer? _statusBuffer;
    private int _statusDisplayType;
    private bool _statusLineActive;

    // Where the cursor was on the ordinary display while the status line has it. DEC gives each
    // display its own cursor: DECSASD 1 must not move the application's, and DECSASD 0 must put it
    // back exactly, or a program that writes a status message loses its place mid-screen.
    private int _cursorXBeforeStatus, _cursorYBeforeStatus;

    private void EnsureStatusBuffer()
    {
        _statusBuffer ??= new Buffer.TerminalBuffer(Cols, 1, 0, hasScrollback: false);
        _statusBuffer.Resize(Cols, 1);
    }

    /// <summary>Puts the cursor back on the display the status line borrowed it from.</summary>
    /// <remarks>
    /// Through the RAW move plus the flag rather than SetCursor, which clamps and clears pending
    /// wrap. The guard is for the zero-size buffer Resize explicitly allows: a host reports zero
    /// while its control exists but has not been laid out, and SetCursor's clamp is
    /// <c>Clamp(x, 0, Cols - 1)</c>, which throws when the maximum is negative.
    /// </remarks>
    private void RestoreDisplayCursor()
    {
        if (Cols <= 0 || Rows <= 0)
            return;

        // Clamped to Cols, not Cols - 1. The phantom column past the last cell is a real cursor
        // position and the one the print path tests to decide whether to wrap; clamping it onto the
        // last cell turns a line about to wrap into one that overwrites its own last character.
        // SetCursorRaw derives the flag from that column, which is why it is not set separately.
        _buffer.SetCursorRaw(Math.Clamp(_cursorXBeforeStatus, 0, Cols),
                             Math.Clamp(_cursorYBeforeStatus, 0, Rows - 1));
    }

    internal void RaiseStatusLineChanged() => StatusLineChanged?.Invoke(this, EventArgs.Empty);

    private bool _statusLineDirty;
    private bool _parsing;

    /// <summary>Records a status-line change, to be announced at the end of the batch.</summary>
    /// <remarks>
    /// Outside a batch -- a host calling the API directly -- there is no end to wait for, so the
    /// event goes out immediately. Inside one it is held, which is what makes the batching real
    /// rather than documented: a single write carrying DECSSDT, DECSASD and a message used to emit
    /// three events, one per setter plus one for the flush.
    /// </remarks>
    private void NoteStatusLineChanged()
    {
        if (_parsing)
        {
            _statusLineDirty = true;
            return;
        }

        RaiseStatusLineChanged();
    }

    /// <summary>Announces a status-line change, at most once for the batch just parsed.</summary>
    /// <remarks>
    /// <para>Asked HERE rather than on the print path, which is the hottest code in the parser: a
    /// per-character test for a state that is almost never on would pay a compare for every
    /// character of ordinary output to catch a status message that arrives once a second. The
    /// batch boundary asks the same question once.</para>
    /// <para>Being selected is enough to count as changed. While DECSASD has the status line, every
    /// printed character goes into it by definition -- so a batch parsed in that state either wrote
    /// to the row or was the batch that selected it, and both are things a host wants to repaint
    /// for. The cost of not tracking it precisely is a repaint of an unchanged row; the cost of
    /// tracking it precisely is paid by every character the terminal ever prints.</para>
    /// </remarks>
    private void FlushStatusLineChange()
    {
        if (!_statusLineDirty && !_statusLineActive)
            return;

        _statusLineDirty = false;
        RaiseStatusLineChanged();
    }

    /// <summary>
    /// DECSSDT ($ ~). Chooses whether there is a status line and who owns it.
    /// </summary>
    /// <remarks>
    /// <para>Type 0 removes it. Leaving the status line SELECTED while removing its row would leave
    /// writes going nowhere with no way back, so this returns the cursor to the main display first
    /// -- the same care DECSASD 0 takes.</para>
    /// <para>Type 1 is the indicator: the terminal owns the contents, not the application. The row
    /// exists so a host can show something there, and writes are not routed to it.</para>
    /// </remarks>
    public void SetStatusDisplayType(int type)
    {
        if (type is < 0 or > 2)
            return;

        if (type == _statusDisplayType)
            return;

        if (_statusLineActive)
            SetActiveStatusDisplay(0);

        _statusDisplayType = type;

        if (type == 0)
        {
            _statusBuffer = null;
        }
        else
        {
            _statusBuffer ??= new Buffer.TerminalBuffer(Cols, 1, 0, hasScrollback: false);
            _statusBuffer.Resize(Cols, 1);
        }

        NoteStatusLineChanged();
    }

    /// <summary>
    /// DECSASD ($ }). Sends what is written next to the status line (1) or the main display (0).
    /// </summary>
    /// <remarks>
    /// <para>Refused unless DECSSDT has selected the HOST-WRITABLE type. Type 0 has no row, and the
    /// indicator's contents belong to the terminal -- honouring the selection for either would put
    /// the application's text somewhere it can never be seen, which is the failure this whole
    /// control exists to stop: text meant for a status line landing in the middle of the screen.</para>
    /// <para>The buffer swap is the alternate screen's, for the same reason: the input handler
    /// writes through whichever buffer it was given, so redirecting output is a matter of giving it
    /// a different one rather than teaching every write about status lines.</para>
    /// </remarks>
    public void SetActiveStatusDisplay(int display)
    {
        var wantStatus = display == 1;

        if (wantStatus && _statusDisplayType != 2)
            return;

        if (wantStatus == _statusLineActive)
            return;

        if (wantStatus)
        {
            // X is saved as it stands, phantom column included: that column is what the print path
            // reads to decide whether the next character wraps, so rounding it off here would turn
            // a line about to wrap into one that overwrites its own last character.
            _cursorXBeforeStatus = _buffer.X;
            _cursorYBeforeStatus = _buffer.Y;

            EnsureStatusBuffer();
            _statusLineActive = true;
            _buffer = _statusBuffer!;
            _inputHandler.SetBuffer(_buffer);

            // NOT homed. Each display keeps its own cursor, and that has to mean across repeated
            // selections too: a program that writes half a status message, goes back to the screen
            // and returns would otherwise start again at column one and overwrite what it wrote.
            // The buffer is created at (0, 0), which is where a first selection finds it.
        }
        else
        {
            _statusLineActive = false;
            _buffer = _usingAltBuffer ? _altBuffer! : _normalBuffer!;
            _inputHandler.SetBuffer(_buffer);
            RestoreDisplayCursor();
        }

        NoteStatusLineChanged();
    }

    /// <summary>
    /// DECSET/DECRST 3. Switching clears the screen, homes the cursor and resets the margins --
    /// the DEC behaviour programs rely on for a clean slate -- and resizes the grid to 132 or 80
    /// columns. The caller gates this on Allow80To132 (mode 40), as xterm does.
    /// </summary>
    public void SetColumnMode(bool wide) => SetPageWidth(wide ? 132 : 80, clear: !NoClearOnColumnChange);

    /// <summary>
    /// DECNCSM (mode 95): whether a DECCOLM column change keeps the screen instead of erasing it.
    /// </summary>
    public bool NoClearOnColumnChange { get; set; }

    /// <summary>
    /// Sets the page width, resetting the margins and homing the cursor, and erasing only when
    /// asked to.
    /// </summary>
    /// <remarks>
    /// The erase is the only thing the three ways in here disagree about, which is why it is a
    /// parameter rather than something each caller repeats:
    ///
    /// <list type="bullet">
    /// <item>DECCOLM erases, which is the DEC behaviour programs rely on for a clean slate.</item>
    /// <item>DECCOLM with DECNCSM set does not, which is what that mode is for.</item>
    /// <item>DECSCPP never does. It sets the page width and says nothing about the contents.</item>
    /// </list>
    /// </remarks>
    internal void SetPageWidth(int columns, bool clear)
    {
        ColumnMode132 = columns >= 132;
        Resize(columns, Rows);
        Buffer.SetScrollRegion(0, Rows - 1);
        Buffer.SetLeftRightMargins(0, Cols - 1);

        if (clear)
            _inputHandler.EraseWholeScreen();

        Buffer.SetCursor(0, 0);
    }

    public bool InsertMode { get; set; }
    public bool ApplicationCursorKeys { get; set; }
    public bool ApplicationKeypad { get; set; }
    public bool BracketedPasteMode { get; set; }

    /// <summary>
    /// Bracketed paste MIME (private mode 5522). When set, <see cref="Paste(TerminalPaste)"/>
    /// announces a paste as a Kitty clipboard read response instead of bracketing text — and
    /// never both, per the spec's precedence rule.
    /// </summary>
    public bool PasteNotificationMode { get; set; }
    public bool OriginMode { get; set; }

    /// <summary>
    /// DECLRMM (mode 69). While set, <c>CSI Pl ; Pr s</c> sets the left and right margins rather
    /// than saving the cursor, and the scrolling region is a box instead of a band of rows.
    /// </summary>
    public bool LeftRightMarginMode { get; set; }
    public bool CursorVisible { get; set; }
    public bool ReverseWraparound { get; set; }

    /// <summary>DECSET 1045 -- classic reverse wraparound; see <see cref="Common.TerminalMode.ReverseWraparoundExtended"/>.</summary>
    public bool ReverseWraparoundExtended { get; set; }
    public bool ReverseVideo { get; set; }
    public bool SendFocusEvents { get; set; }
    public bool Win32InputMode { get; set; }

    /// <summary>
    /// The Kitty keyboard protocol state: the active enhancement flags and their per-screen
    /// stacks. The host reads <see cref="KittyKeyboardState.Flags"/> (via
    /// <see cref="KittyKeyboardActive"/>) to decide whether keys go through
    /// <see cref="GenerateKittyKeyInput"/> instead of the legacy generators.
    /// </summary>
    public KittyKeyboardState KittyKeyboardState { get; } = new();

    /// <summary>
    /// Whether keyboard input should be encoded under the Kitty keyboard protocol right now:
    /// the option allows it and the running application has asked for it.
    /// </summary>
    public bool KittyKeyboardActive =>
        Options.KittyKeyboardEnabled && KittyKeyboardState.Flags != KittyKeyboardFlags.None;

    /// <summary>
    /// Sixel Display Mode (DECSDM, mode 80). See <see cref="TerminalMode.SixelDisplayMode"/> --
    /// false, the default, is the scrolling behaviour applications expect.
    /// </summary>
    public bool SixelDisplayMode { get; set; }

    /// <summary>
    /// Whether each Sixel image gets its own colour registers (mode 1070). On by default.
    /// </summary>
    public bool SixelPrivateColorRegisters { get; set; } = true;

    /// <summary>
    /// Whether the cursor is left to the right of a Sixel image rather than below it (mode 8452).
    /// </summary>
    public bool SixelCursorRight { get; set; }


    /// <summary>
    /// When enabled, the eighth bit of input characters is used for Meta key.
    /// Mode 1034 (eightBitInput).
    /// </summary>
    public bool EightBitInput { get; set; }

    /// <summary>DECNRCM (mode 42): whether national replacement sets are in force.</summary>
    /// <remarks>
    /// A designated national set behaves as ASCII while this is false, so changing it re-resolves
    /// what is already designated rather than only affecting the next designation.
    /// </remarks>
    public bool NationalReplacementCharsets { get; set; }
    
    /// <summary>
    /// When enabled, pressing Meta+key sends ESC followed by the key.
    /// Mode 1036 (metaSendsEscape).
    /// </summary>
    public bool MetaSendsEscape { get; set; }
    
    /// <summary>
    /// When enabled, pressing Alt+key sends ESC followed by the key.
    /// Mode 1039 (altSendsEscape).
    /// </summary>
    public bool AltSendsEscape { get; set; }
    
    public string Title { get; set; }

    /// <summary>The icon label (OSC 1), reported by XTWINOPS 20. Kept separate from
    /// <see cref="Title"/> exactly as xterm keeps them, so the two report independently.</summary>
    public string IconTitle { get; set; } = string.Empty;

    /// <summary>
    /// The window state XTWINOPS 1/2/3 last asked for, reported back by winops 11 and 13 when the
    /// host does not answer the <see cref="WindowInfoRequested"/> event itself. A widget that IS
    /// a real window handles the event; a headless or embedded terminal still answers coherently.
    /// </summary>
    internal bool WindowIconified;

    /// <summary>See <see cref="WindowIconified"/>.</summary>
    internal int WindowX;

    /// <summary>See <see cref="WindowIconified"/>.</summary>
    internal int WindowY;

    /// <summary>Title mode (CSI &gt; Pm t / T): titles are SET as hex-encoded strings.</summary>
    internal bool TitleSetHex;

    /// <summary>Title mode: title REPORTS are hex-encoded.</summary>
    internal bool TitleQueryHex;
    public string? CurrentDirectory { get; set; }
    public string? CurrentHyperlink { get; set; }

    /// <summary>The values exported through iTerm2's OSC 1337 SetUserVar extension.</summary>
    public IReadOnlyDictionary<string, string> UserVariables => _userVariables;
    private readonly Dictionary<string, string> _userVariables = new();

    internal bool TrySetUserVariable(string name, string value)
    {
        if (value.Length > Options.MaxUserVariableBytes
            || (!_userVariables.ContainsKey(name) && _userVariables.Count >= Options.MaxUserVariables))
            return false;

        _userVariables[name] = value;
        return true;
    }

    /// <summary>The shell integration version reported through iTerm2's OSC 1337 extension.</summary>
    public string? ShellIntegrationVersion { get; internal set; }

    /// <summary>The remote host reported through iTerm2's OSC 1337 extension.</summary>
    public string? RemoteHost { get; internal set; }

    /// <summary>
    /// The most recent OSC 133 shell integration mark, or null if the shell has never sent one.
    /// </summary>
    /// <remarks>
    /// Null is a third state, not a default: shell integration must be configured in the shell, so
    /// a shell without it is indistinguishable from one sitting at a prompt. Treat null as "cannot
    /// say" rather than folding it into either answer.
    ///
    /// <see cref="ShellIntegrationMark.CommandStart"/> means the shell is waiting for input;
    /// <see cref="ShellIntegrationMark.CommandExecuted"/> means something else holds the terminal.
    /// </remarks>
    public ShellIntegrationMark? ShellIntegrationState { get; internal set; }

    /// <summary>
    /// Whether an application has declared an atomic update in progress (DEC private mode 2026).
    /// </summary>
    /// <remarks>
    /// A renderer should hold the last complete frame while this is true, and must bound the wait
    /// with a timeout of its own — an application that sets this and dies would otherwise freeze the
    /// display permanently.
    /// </remarks>
    public bool SynchronizedOutput { get; internal set; }

    /// <summary>
    /// Whether an application has asked to be told about resizes in band (DEC private mode 2048).
    /// </summary>
    /// <remarks>
    /// Set by the application rather than the host, which is why there is no public setter: enabling
    /// the mode obliges the terminal to send a report immediately, and an embedder assigning the
    /// property would get the flag without the report. Hosts read this to know an application is
    /// listening; <see cref="Resize"/> does the rest on its own.
    /// </remarks>
    public bool InBandResize { get; internal set; }

    /// <summary>
    /// The exit code from the last OSC 133 ; D, or null if none has been reported.
    /// </summary>
    public int? LastCommandExitCode { get; internal set; }

    /// <summary>
    /// The progress state last reported via OSC 9 ; 4.
    /// </summary>
    public ProgressState ProgressState { get; internal set; } = ProgressState.None;

    /// <summary>
    /// The progress percentage last reported via OSC 9 ; 4, from 0 to 100.
    /// </summary>
    public int ProgressValue { get; internal set; }

    /// <summary>
    /// The terminal's colours: the 256-entry palette plus foreground, background and cursor.
    /// </summary>
    /// <remarks>
    /// Seeded from <see cref="TerminalOptions.Theme"/>, then modified by OSC 4 and OSC 10/11/12.
    /// An embedder following the OS light/dark setting calls
    /// <see cref="ColorPalette.ApplyTheme"/> when it flips.
    ///
    /// This is also what colour QUERIES answer from, which is the point: a program that asks for
    /// the background before choosing its own palette gets the real one, so a light terminal stops
    /// being told to render for a dark one.
    /// </remarks>
    public ColorPalette Colors { get; }
    public string? HyperlinkId { get; set; }

    /// <summary>
    /// The mouse pointer shape an application has asked for with OSC 22, or null when none is set.
    /// </summary>
    /// <remarks>
    /// A name from <see cref="PointerShapes.All"/>. Null means the host should use its own pointer:
    /// it is the state after a reset, and the state an application returns the terminal to by
    /// popping everything it pushed.
    /// </remarks>
    public string? PointerShape => ActivePointerShapes.Current;

    private readonly PointerShapeStack _normalPointerShapes = new();
    private readonly PointerShapeStack _altPointerShapes = new();

    /// <summary>
    /// The shape stack of the screen currently displayed.
    /// </summary>
    /// <remarks>
    /// One stack per screen, as the protocol requires: a full-screen program leaves its shape behind
    /// on the alternate screen when it is suspended, and the shell it drops back to gets its own.
    /// </remarks>
    private PointerShapeStack ActivePointerShapes => _usingAltBuffer ? _altPointerShapes : _normalPointerShapes;

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
    /// Fired when an application writes clipboard data through OSC 52 or Kitty OSC 5522.
    /// </summary>
    public event EventHandler<TerminalEvents.ClipboardWriteEventArgs>? ClipboardWriteRequested;

    /// <summary>
    /// Fired when an application requests clipboard data through OSC 52 or Kitty OSC 5522.
    /// </summary>
    public event EventHandler<TerminalEvents.ClipboardReadEventArgs>? ClipboardReadRequested;

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
    /// Fired synchronously before a line leaves the active viewport.
    /// </summary>
    /// <remarks>
    /// Consumers that need the row contents must snapshot them during the callback because the
    /// underlying line may be recycled as soon as the handler returns.
    /// </remarks>
    public event EventHandler<TerminalEvents.LineExitedViewportEventArgs>? LineExitedViewport;

    /// <summary>
    /// Fired synchronously when an absolute home or a non-selective full display erase can begin a redraw.
    /// </summary>
    /// <remarks>Snapshot the buffer during the callback, before its contents change.</remarks>
    public event EventHandler? ViewportRedrawStarting;

    /// <summary>
    /// Fired when the current directory changes.
    /// </summary>
    public event EventHandler<TerminalEvents.DirectoryChangeEventArgs>? DirectoryChanged;

    /// <summary>
    /// Fired when a hyperlink is encountered.
    /// </summary>
    public event EventHandler<TerminalEvents.HyperlinkEventArgs>? HyperlinkChanged;

    /// <summary>
    /// Fired for each OSC 133 shell integration mark.
    /// </summary>
    public event EventHandler<TerminalEvents.ShellIntegrationEventArgs>? ShellIntegrationMarkReceived;

    /// <summary>
    /// Raised when an atomic update begins or ends, so a renderer can react without polling.
    /// </summary>
    public event EventHandler<TerminalEvents.SynchronizedOutputEventArgs>? SynchronizedOutputChanged;

    internal void RaiseSynchronizedOutputChanged(bool active)
    {
        if (SynchronizedOutput == active)
            return;

        SynchronizedOutput = active;
        SynchronizedOutputChanged?.Invoke(this, new TerminalEvents.SynchronizedOutputEventArgs(active));
    }

    /// <summary>
    /// Raised when the pointer shape requested via OSC 22 changes, including when it is cleared.
    /// </summary>
    public event EventHandler<TerminalEvents.PointerShapeEventArgs>? PointerShapeChanged;

    /// <summary>
    /// Replaces the current pointer shape on the active screen (OSC 22 ; shape).
    /// </summary>
    internal void SetPointerShape(string shape)
    {
        var before = PointerShape;
        ActivePointerShapes.Set(shape);
        RaisePointerShapeChanged(before);
    }

    /// <summary>
    /// Pushes pointer shapes onto the active screen's stack (OSC 22 ; &gt; shape,...).
    /// </summary>
    /// <remarks>
    /// All of them as one operation, so a listener hears about the shape the sequence ends on and
    /// not about each one on the way there.
    /// </remarks>
    internal void PushPointerShapes(IEnumerable<string> shapes)
    {
        var before = PointerShape;
        foreach (var shape in shapes)
            ActivePointerShapes.Push(shape);
        RaisePointerShapeChanged(before);
    }

    /// <summary>
    /// Pops the current pointer shape off the active screen's stack (OSC 22 ; &lt;).
    /// </summary>
    internal void PopPointerShape()
    {
        var before = PointerShape;
        ActivePointerShapes.Pop();
        RaisePointerShapeChanged(before);
    }

    /// <summary>
    /// Empties the active screen's shape stack, so the host uses its own pointer again.
    /// </summary>
    internal void ClearPointerShapes()
    {
        var before = PointerShape;
        ActivePointerShapes.Clear();
        RaisePointerShapeChanged(before);
    }

    /// <summary>
    /// Raises <see cref="PointerShapeChanged"/> if the current shape differs from
    /// <paramref name="before"/>.
    /// </summary>
    /// <remarks>
    /// Only transitions, so a host is not asked to swap the pointer for every frame of a program
    /// that re-sends the same shape as the mouse moves.
    /// </remarks>
    private void RaisePointerShapeChanged(string? before)
    {
        var now = PointerShape;
        if (before == now)
            return;

        PointerShapeChanged?.Invoke(this, new TerminalEvents.PointerShapeEventArgs(now));
    }

    /// <summary>
    /// Fired when progress is reported via OSC 9 ; 4.
    /// </summary>
    public event EventHandler<TerminalEvents.ProgressEventArgs>? ProgressChanged;

    /// <summary>
    /// Fired when a desktop notification is requested via OSC 9.
    /// </summary>
    public event EventHandler<TerminalEvents.NotificationEventArgs>? NotificationReceived;

    /// <summary>Fired when iTerm2 requests the user's attention.</summary>
    public event EventHandler<TerminalEvents.AttentionRequestedEventArgs>? AttentionRequested;

    /// <summary>
    /// Fired for every OSC sequence, including ones this terminal does not implement.
    /// </summary>
    /// <remarks>
    /// Observation only, raised after any built-in handling. See
    /// <see cref="TerminalEvents.OscReceivedEventArgs"/>.
    /// </remarks>
    public event EventHandler<TerminalEvents.OscReceivedEventArgs>? OscReceived;

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
    /// Fired when a program asks which font the terminal is displayed in (OSC 50 ; ? ST).
    /// </summary>
    /// <remarks>
    /// The emulator has no font of its own; the host that draws its cells does. Answer by setting
    /// <see cref="TerminalEvents.FontQueryEventArgs.FontName"/> and
    /// <see cref="TerminalEvents.FontQueryEventArgs.Handled"/>. Declining -- by not subscribing,
    /// or by leaving the name unset -- still sends a reply, xterm's nameless OSC 50, so the asking
    /// program is told rather than left waiting.
    /// </remarks>
    public event EventHandler<TerminalEvents.FontQueryEventArgs>? FontQueryRequested;

    /// <summary>
    /// Fired when the active buffer is changed.
    /// </summary>
    public event EventHandler<TerminalEvents.BufferChangedEventArgs>? BufferChanged;

    public Terminal(TerminalOptions? options = null)
    {
        Options = options?.Clone() ?? new TerminalOptions();
        Cols = Options.Cols;
        Rows = Options.Rows;
        ResetTabStops();
        Title = string.Empty;
        Colors = new ColorPalette(Options.Theme);

        // Initialize buffers
        _normalBuffer = new Buffer.TerminalBuffer(Cols, Rows, Options.Scrollback);
        _altBuffer = new Buffer.TerminalBuffer(Cols, Rows, 0, hasScrollback: false);
        _normalBuffer.LineExitedViewport += line => RaiseLineExitedViewport(line, BufferType.Normal, LineExitReason.Scrolled);
        _altBuffer.LineExitedViewport += line => RaiseLineExitedViewport(line, BufferType.Alternate, LineExitReason.Scrolled);
        _buffer = _normalBuffer;
        _usingAltBuffer = false;

        // Options that were read once here and never again, so a host writing them got a property
        // that reported the new value and did nothing. Each reaches the thing it names now.
        //
        // Scrollback goes to the normal buffer only -- the alternate screen has no history by
        // definition and keeps none while this is set.
        Options.ScrollbackChanged = _normalBuffer.SetScrollback;
        Options.ThemeChanged = Colors.ApplyTheme;
        Options.TabStopWidthChanged = ResetTabStops;

        // Initialize parser and input handler
        _parser = new EscapeSequenceParser();
        _inputHandler = new InputHandler(this);
        _keyboardInput = new KeyboardInputGenerator(this);
        _mouseTracker = new MouseTracker(this);
        _selectionManager = new SelectionManager(this);

        // Subscribe to parser events using C# event pattern
        // PrintFast rather than the Print event: same call, without an EventArgs per character.
        _parser.PrintFast = _inputHandler.Print;
        _parser.PrintRunFast = _inputHandler.PrintAsciiRun;
        _parser.PrintByteRunFast = _inputHandler.PrintAsciiRun;
        // Fast hooks rather than events: the terminal is the parser's only mandatory listener, and
        // an EventArgs per control character and per escape sequence is pure overhead for it. The
        // public events still fire for anyone else subscribed.
        _parser.ExecuteFast = HandleExecute;
        _parser.CsiFast = _inputHandler.HandleCsi;
        _parser.EscFast = _inputHandler.HandleEsc;
        _parser.OscFast = _inputHandler.HandleOsc;

        // DCS stays on events here. A Sixel payload arrives as many DcsPut chunks and is worth the
        // same treatment, which a later commit gives it.
        _parser.DcsHook += OnParserDcsHook;
        _parser.DcsPut += OnParserDcsPut;
        _parser.DcsUnhook += OnParserDcsUnhook;
        _parser.ApcHook += OnParserApcHook;
        _parser.ApcPut += OnParserApcPut;
        _parser.ApcUnhook += OnParserApcUnhook;

        InsertMode = false;
        ApplicationCursorKeys = false;
        ApplicationKeypad = false;
        BracketedPasteMode = false;
        PasteNotificationMode = false;
        InvalidatePendingPaste();
        OriginMode = false;
        LeftRightMarginMode = false;
        CursorVisible = true;
        ReverseWraparound = false;
        ReverseWraparoundExtended = false;
        ConformanceLevel = 65;
        SendFocusEvents = false;
        InBandResize = false;
    }




    /// <summary>
    /// Handles the start of a DCS sequence from the parser.
    /// </summary>
    private void OnParserDcsHook(object? sender, DcsHookEventArgs e)
    {
        _inputHandler.HandleDcsHook(e.Identifier, e.Parameters);
    }

    /// <summary>
    /// Handles a chunk of a DCS payload from the parser.
    /// </summary>
    private void OnParserDcsPut(object? sender, DcsPutEventArgs e)
    {
        _inputHandler.HandleDcsPut(e.Data.Span);
    }

    /// <summary>
    /// Handles the end of a DCS sequence from the parser.
    /// </summary>
    private void OnParserDcsUnhook(object? sender, DcsUnhookEventArgs e)
    {
        _inputHandler.HandleDcsUnhook(e.TerminatedCleanly);
    }

    /// <summary>
    /// Handles the start of an APC sequence from the parser.
    /// </summary>
    private void OnParserApcHook(object? sender, ApcHookEventArgs e)
    {
        _inputHandler.HandleApcHook(e.Introducer);
    }

    /// <summary>
    /// Handles a chunk of an APC payload from the parser.
    /// </summary>
    private void OnParserApcPut(object? sender, ApcPutEventArgs e)
    {
        _inputHandler.HandleApcPut(e.Data.Span);
    }

    /// <summary>
    /// Handles the end of an APC sequence from the parser.
    /// </summary>
    private void OnParserApcUnhook(object? sender, ApcUnhookEventArgs e)
    {
        _inputHandler.HandleApcUnhook(e.TerminatedCleanly);
    }

    /// <summary>
    /// Whether runs of printable ASCII are written in one batch rather than a character at a time.
    /// On by default; see <c>InputHandler.UseRunPrinting</c>.
    /// </summary>
    public bool UseRunPrinting
    {
        get => _inputHandler.UseRunPrinting;
        set => _inputHandler.UseRunPrinting = value;
    }

    /// <summary>
    /// Writes data to the terminal.
    /// </summary>
    /// <remarks>
    /// If the data came from a PTY, call <see cref="Write(ReadOnlySpan{byte})"/> with the raw
    /// bytes instead: decoding per read allocates a string the parser does not need, and —
    /// the sharper edge — a read boundary landing mid-codepoint corrupts that character,
    /// because per-read decoding cannot carry a partial sequence to the next read. The byte
    /// overload decodes internally and statefully. This overload is the right call when the
    /// data already IS a string: programmatic writes, tests, composed sequences.
    /// </remarks>
    public void Write(string data)
    {
        // Writes after Dispose are IGNORED, deliberately, and not an exception. A host reads its
        // pty on a background thread and writes what it gets to the terminal; disposing the
        // control while a read is in flight is ordinary, not a bug, and throwing there would kill
        // the read loop rather than the object being torn down. Two tests pin this.
        if (_disposed || string.IsNullOrEmpty(data))
            return;

        _parsing = true;
        try { _parser.Parse(data); }
        finally { _parsing = false; }

        FlushStatusLineChange();
    }

    /// <summary>
    /// Writes UTF-8 bytes to the terminal.
    ///
    /// Prefer this over the string overload when the source is a PTY. Decoding a read to UTF-16
    /// first allocates a string per read and does work most of the bytes do not need — printable
    /// ASCII is the same number as a byte, as a codepoint and in the cell. It also carries a partial
    /// multi-byte sequence across calls, which a caller decoding each read on its own cannot do:
    /// a read boundary landing mid-codepoint corrupts that character.
    /// </summary>
    public void Write(ReadOnlySpan<byte> data)
    {
        if (_disposed || data.IsEmpty)
            return;

        _parsing = true;
        try { _parser.Parse(data); }
        finally { _parsing = false; }

        FlushStatusLineChange();
    }

    /// <summary>
    /// Writes data to the terminal as a line (adds line feed).
    /// </summary>
    public void WriteLine(string data)
    {
        Write(data + "\r\n");
    }

    /// <summary>
    /// The columns a tab lands on. Real stops rather than arithmetic, because HTS (ESC H) and TBC
    /// (CSI g) exist to change them: `tabs 4` writes its stops with HTS, and a terminal deriving
    /// them from a width could only ever ignore it. TBC used to acknowledge the request and do
    /// nothing, which its own comment admitted.
    /// </summary>
    private readonly HashSet<int> _tabStops = [];

    /// <summary>Puts the stops back to every <see cref="TerminalOptions.TabStopWidth"/> columns.</summary>
    internal void ResetTabStops()
    {
        _tabStops.Clear();
        var width = Math.Max(Options.TabStopWidth, 1);
        for (var column = width; column < Cols; column += width)
            _tabStops.Add(column);
    }

    /// <summary>Sets a stop at <paramref name="column"/>. HTS.</summary>
    internal void SetTabStop(int column)
    {
        if (column > 0 && column < Cols)
            _tabStops.Add(column);
    }

    /// <summary>Clears the stop at <paramref name="column"/>, or every stop. TBC.</summary>
    internal void ClearTabStop(int column, bool all)
    {
        if (all)
            _tabStops.Clear();
        else
            _tabStops.Remove(column);
    }

    /// <summary>The column a tab from <paramref name="from"/> lands on: the next stop, else the edge.</summary>
    internal int NextTabStop(int from)
    {
        if (_tabStops.Count == 0)
            return Cols - 1;

        var next = Cols - 1;
        foreach (var stop in _tabStops)
        {
            if (stop > from && stop < next)
                next = stop;
        }

        return next;
    }

    /// <summary>The column a backward tab from <paramref name="from"/> lands on.</summary>
    internal int PreviousTabStop(int from)
    {
        var previous = 0;
        foreach (var stop in _tabStops)
        {
            if (stop < from && stop > previous)
                previous = stop;
        }

        return previous;
    }

    /// <summary>
    /// Resizes the terminal.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Either dimension is negative.
    /// </exception>
    /// <remarks>
    /// Zero is allowed and negative is not, which is a real distinction rather than a lenient one.
    /// A host legitimately reports zero while its control exists but has not been laid out yet, and
    /// the buffer supports being built empty and brought to life by a later resize -- there is a
    /// test pinning exactly that. A negative dimension describes no state a window can be in; it
    /// only ever arrives from arithmetic that went wrong upstream, and every buffer operation from
    /// here down would carry the mistake somewhere that cannot explain itself.
    /// </remarks>
    public void Resize(int cols, int rows)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cols);
        ArgumentOutOfRangeException.ThrowIfNegative(rows);

        if (cols == Cols && rows == Rows)
            return;

        var oldCols = Cols;
        var oldRows = Rows;

        Cols = cols;
        Rows = rows;

        // The options carry the size too, and stopped agreeing with it the moment anything
        // resized: Options.Cols went on reporting the number the terminal was BUILT with while
        // Cols reported the number it is. Two public properties of the same name disagreeing is a
        // worse failure than a stale one, because nothing about reading either says which to
        // believe. Resize stays the way to change the size -- setting the two options separately
        // would resize twice, once through a geometry the caller never asked for.
        Options.Cols = cols;
        Options.Rows = rows;

        // A wider screen gains the stops it did not have room for; a program's custom stops do
        // not survive a resize, which is what xterm does too.
        ResetTabStops();

        // Resize buffers
        _normalBuffer?.Resize(cols, rows);
        _altBuffer?.Resize(cols, rows);

        // One row always, only ever as wide as the screen. It is not part of the grid, so the row
        // count never reaches it -- but a status line narrower than the display would clip its own
        // text after a widen, and wider would hold text nothing can show.
        _statusBuffer?.Resize(cols, 1);

        Resized?.Invoke(this, new TerminalEvents.ResizeEventArgs(cols, rows));

        // After the host has been told, not before. The spec requires the report to follow the
        // resize rather than announce it, so the size an application reads is one the terminal has
        // already applied -- and a host that keeps its pixel metrics up to date in Resized has
        // updated them by the time the report asks for them.
        SendInBandResizeReport();
    }

    /// <summary>
    /// Sends the in-band resize report (DEC private mode 2048), if an application asked for one.
    /// </summary>
    /// <remarks>
    /// <c>CSI 48 ; rows ; cols ; height_px ; width_px t</c> -- rows before columns, and the text
    /// area alone, excluding any padding the host draws around it.
    /// </remarks>
    internal void SendInBandResizeReport()
    {
        if (!InBandResize)
            return;

        var (heightPixels, widthPixels) = RequestTextAreaPixels();
        RaiseDataReceived($"\u001b[48;{Rows};{Cols};{heightPixels};{widthPixels}t");
    }

    /// <summary>
    /// Tells an application that asked for in-band resize reports (mode 2048) that the text
    /// area's PIXEL dimensions changed while the grid did not — a font-size change, a zoom, a
    /// display-scale switch. The spec requires a report for exactly this case, and
    /// <see cref="Resize(int, int)"/> cannot see it: the pixel metrics live in the host, so the
    /// host calls this after updating them. A no-op unless the mode is set, so it is safe to call
    /// unconditionally from wherever font metrics are recomputed.
    /// </summary>
    public void NotifyTextAreaPixelsChanged() => SendInBandResizeReport();

    /// <summary>
    /// Asks the host how large the text area is in pixels, or (0, 0) if it cannot say.
    /// </summary>
    /// <remarks>
    /// <para>Zero is the spec's answer for a terminal that does not know its pixel size, and it is
    /// the honest one here. <see cref="TerminalOptions.CellWidthPixels"/> would multiply out to a
    /// plausible-looking number whether or not the embedder ever set it from real font metrics --
    /// its 10x20 default is a placeholder -- and an application sizing an image off that would be
    /// wrong rather than uninformed. Applications already handle zero; they cannot detect a lie.</para>
    /// <para>Deliberately not gated on <see cref="WindowOptions.GetWinSizePixels"/>. That option
    /// governs whether unsolicited XTWINOPS queries are answered at all, and defaults to false;
    /// mode 2048 is itself the application's request, and the terminal owes it a report.</para>
    /// </remarks>
    private (int Height, int Width) RequestTextAreaPixels()
    {
        var args = RaiseWindowInfoRequested(WindowInfoRequest.SizePixels);
        if (!args.Handled)
            return (0, 0);

        return (Math.Max(0, args.HeightPixels), Math.Max(0, args.WidthPixels));
    }

    /// <summary>
    /// Resets the terminal to initial state.
    /// </summary>
    /// <summary>
    /// DECSTR -- soft terminal reset (CSI ! p). The subset of <see cref="Reset"/> the VT
    /// standard names: modes, margins, rendition, charsets and the SAVED cursor go back to
    /// defaults, while the screen contents, the live cursor position, the tab stops and the
    /// window state stay exactly where they are. esctest issues one before every single test,
    /// which is what makes an unimplemented DECSTR poison whole families: any mode a test sets
    /// leaks into every test after it.
    /// </summary>
    public void SoftReset()
    {
        InsertMode = false;
        ApplicationCursorKeys = false;
        ApplicationKeypad = false;
        OriginMode = false;
        CursorVisible = true;
        ReverseWraparound = false;
        ReverseWraparoundExtended = false;

        // xterm's deliberate deviation from the VT spec, which esctest tests for by name:
        // DECSTR turns autowrap ON, because everything after 1985 assumes it.
        Options.Wraparound = true;

        // Margins to full screen, and the DECLRMM mode with them. xterm historically left the
        // mode set -- the bug esctest names in test_DECSET_DECLRMM_ModeResetByDECSTR -- and the
        // test asserts the FIXED behaviour.
        LeftRightMarginMode = false;
        Buffer.SetScrollRegion(0, Rows - 1);
        Buffer.SetLeftRightMargins(0, Cols - 1);

        // Normal rendition, default charsets, and a saved-cursor context of home-with-defaults,
        // so DECRC after DECSTR restores the origin rather than a dead program's state.
        _inputHandler.ResetAttributes();
        _inputHandler.ResetStoredModes();
        _inputHandler.ResetProtectionMode();
        LineFeedMode = false;

        // Before ResetCharsets, so the designations it clears are re-resolved through a flag
        // that is already down. DECRQM reported mode 42 as reset while a set designated after
        // a soft reset was still being translated -- state and report disagreeing.
        NationalReplacementCharsets = false;
        NoClearOnColumnChange = false;
        _inputHandler.ResetCharsets();
        _normalBuffer?.ResetSavedCursor();
        _altBuffer?.ResetSavedCursor();
    }

    public void Reset()
    {
        // RIS puts the tab stops back to their defaults along with everything else; a program's
        // custom stops are part of the state being reset.
        ResetTabStops();

        var shapeBefore = PointerShape;

        // The status line goes with everything else RIS undoes: its type, its selection and its
        // contents. Returning the cursor to the main display FIRST, so the reset below is applied
        // to the screen rather than to a row that is about to stop existing.
        SetActiveStatusDisplay(0);
        SetStatusDisplayType(0);

        // Reset to normal buffer
        if (_usingAltBuffer)
        {
            if (LineExitedViewport is not null)
                RaiseBufferDeactivatedLines(_altBuffer!);
            _buffer = _normalBuffer!;
            _usingAltBuffer = false;
            _inputHandler.SetBuffer(_buffer);
        }

        // RIS undoes DECCOLM's 132 columns before anything else -- xterm checks the current
        // width and resizes back to 80, and esctest RIS_ResetDECCOLM checks that it happened.
        if (ColumnMode132)
            SetColumnMode(false);

        // Both screens come back blank: RIS while the alternate buffer is live must not leave
        // the old contents waiting behind the next DECSET 47.
        if (_altBuffer is not null)
        {
            for (var i = 0; i < _altBuffer.Lines.Length; i++)
                _altBuffer.Lines[i]?.Fill(global::XTerm.Buffer.BufferCell.Space, 0, Cols);
        }

        // Replies go back to 7-bit. S8C1T is a choice a program made about this session, and
        // RIS is where a session's choices end -- leaving it set would send 8-bit C1 controls
        // to whatever runs next, which never asked for them.
        EightBitControls = false;

        // Title modes go back to plain text; the titles themselves and the title stacks
        // survive, as they do in xterm.
        TitleSetHex = false;
        TitleQueryHex = false;

        // Reset parser
        _parser.Reset();
        _inputHandler.ResetAttributes();
        _inputHandler.ResetProtectionMode();

        // Reset modes
        InsertMode = false;
        ApplicationCursorKeys = false;
        ApplicationKeypad = false;
        BracketedPasteMode = false;
        PasteNotificationMode = false;
        InvalidatePendingPaste();
        // A reset mid-5522-write abandons the transfer: a terminator arriving after RIS must not
        // commit pre-reset data to the host clipboard.
        _inputHandler.ResetKittyClipboard();
        OriginMode = false;
        LeftRightMarginMode = false;
        CursorVisible = true;
        ReverseWraparound = false;
        ReverseWraparoundExtended = false;
        ReverseVideo = false;
        SendFocusEvents = false;
        EightBitInput = false;
        MetaSendsEscape = false;  // Default is disabled
        AltSendsEscape = false;
        Win32InputMode = false;
        InBandResize = false;
        // The three Sixel modes survived RIS, so DECRQM went on reporting mode 80 as set after a
        // reset and the next image drew with the previous program's geometry. Their defaults are
        // not all false, which is why they are named rather than cleared in a loop.
        SixelDisplayMode = false;
        SixelPrivateColorRegisters = true;
        SixelCursorRight = false;

        // Same failure, two modes this change added. DECNCSM surviving RIS means the next
        // DECCOLM silently skips the clean slate DEC guarantees; DECNRCM surviving it means
        // the next program to designate a national set gets one it never asked for, since
        // ResetCharsets clears the designations but not the flag they resolve through.
        NoClearOnColumnChange = false;
        NationalReplacementCharsets = false;
        _inputHandler.RefreshDesignatedCharsets();

        // ResetStoredModes says "for RIS" and only the SOFT reset was calling it, so every
        // stored toggle survived the hard one -- DECRQM reported DECNRCM as still set after
        // RIS had already put the behaviour back. The report and the behaviour now agree.
        _inputHandler.ResetStoredModes();

        // DECSACE, which a SOFT reset deliberately leaves alone.
        _inputHandler.ResetAttributeChangeExtent();

        // And the charset designations, with the SO/SI shift state. InputHandler.ResetCharsets
        // existed for exactly this and was called from nowhere, so a program that designated line
        // drawing into G0 and died left the next one printing box characters for letters.
        _inputHandler.ResetCharsets();

        // The SAVED cursor context too, on both screens. Resetting only the live tables left the
        // reset one DECRC away from being undone: designate line drawing, DECSC, RIS, DECRC, and
        // the dead program's designations come back. DECSC state is per-screen, so a reset that
        // cleared only the active buffer would leave the other one loaded.
        _normalBuffer?.ResetSavedCursor();
        _altBuffer?.ResetSavedCursor();

        // Kitty keyboard flags do not survive a reset: RIS is exactly how someone recovers from
        // an application that set them and died.
        KittyKeyboardState.Reset();

        // The tracker holds the flags that actually gate mouse and focus reports -- tracking mode,
        // encoding, and its own copy of 1004. SendFocusEvents above is the terminal's copy of that
        // last one and clearing it alone left the tracker still emitting ESC[I after RIS, while
        // DECRQM read the cleared copy and answered "reset". Reset both from one place.
        _mouseTracker.Reset();

        // Through the raiser rather than assigned, so a renderer holding a frame is told it can
        // stop -- and so the flag cannot be left set. It is also the dedupe key for the event, so a
        // stale true would swallow the NEXT application's begin and leave its end raising a lone
        // false: the transitions-only contract inverted and staying inverted. RIS is exactly how
        // someone recovers from an application that set the mode and died.
        RaiseSynchronizedOutputChanged(false);

        // Both shape stacks, not just the active screen's, as the protocol requires. RIS is how
        // someone gets out of a `wait` pointer left behind by a program that died holding one, and
        // it would still be waiting on the other screen otherwise.
        _normalPointerShapes.Clear();
        _altPointerShapes.Clear();
        RaisePointerShapeChanged(shapeBefore);

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
        // Clear all lines in the buffer (including scrollback)
        // and reset line attributes (double-width/double-height) to normal
        for (int i = 0; i < _buffer.Lines.Length; i++)
        {
            var line = _buffer.Lines[i];
            if (line != null)
            {
                line.Fill(BufferCell.Space);
                line.LineAttribute = LineAttribute.Normal;
            }
        }

        // Filling every line took every OSC 66 block with it, so the print path can stop looking for
        // the rows one hangs over -- otherwise a single heading early in a session retires the fast
        // path for the whole of it.
        _buffer.RefreshMultiRowSizedRuns();
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
    /// Image bytes placed since the last sweep. See <see cref="NoteImagePlaced"/>.
    /// </summary>
    private long _imageBytesSinceSweep;

    /// <summary>
    /// Records a newly placed image and sweeps the budget when enough has arrived to matter.
    /// </summary>
    /// <remarks>
    /// Sweeping on every image would mean walking both buffers -- every cell of the scrollback -- each time
    /// one is drawn, and a program animating with Sixel draws one per frame. Since a sweep leaves the buffer
    /// inside the budget, it takes a further budget's worth of images to get back outside it, so counting
    /// bytes and sweeping when the counter says it is possible costs one scan per budget rather than one per
    /// picture. What that trades away is exactness: the buffer can sit up to one budget over before the
    /// sweep, which is a ceiling on overshoot rather than an unbounded one.
    /// </remarks>
    internal void NoteImagePlaced(Graphics.TerminalImage image)
    {
        if (Options.MaxImageBytes <= 0)
            return;

        _imageBytesSinceSweep += image.ByteCount;
        if (_imageBytesSinceSweep < Options.MaxImageBytes)
            return;

        _imageBytesSinceSweep = 0;
        EnforceImageBudget();
    }

    /// <summary>
    /// Drops the oldest images once the buffer holds more image data than the budget allows.
    /// </summary>
    /// <remarks>
    /// <para>Images normally need no managing: one is freed when the last cell showing it is
    /// overwritten or scrolls out of the scrollback, because that was its last reference. This is
    /// the backstop for the case that defeats it -- a deep scrollback full of pictures, every one
    /// still referenced and every one still in memory.</para>
    /// <para>Oldest first, by the identifier each image is stamped with when it is decoded, so
    /// what disappears is the picture furthest back in the history rather than the one on screen.
    /// Both buffers are swept: an image on the alternate screen costs the same memory as one on
    /// the normal screen.</para>
    /// </remarks>
    /// <summary>
    /// Removes every appearance of one image from both buffers.
    /// </summary>
    /// <remarks>
    /// What Kitty's delete-by-id asks for. The pixels themselves go when the last reference to them
    /// does, so this is about what is on screen rather than about memory.
    /// </remarks>
    internal void DropImage(Graphics.TerminalImage image)
    {
        var doomed = new HashSet<Graphics.TerminalImage> { image };
        DropImages(_normalBuffer, doomed);
        DropImages(_altBuffer, doomed);
    }

    /// <summary>
    /// Removes every run matching a test, from both buffers.
    /// </summary>
    /// <remarks>
    /// Kitty's delete targets select placements by identity -- image id, placement id, z-index -- so
    /// they need a test on the run rather than on the pixels behind it. Deleting by image is the
    /// special case where every appearance of one picture goes at once, and that goes through
    /// <see cref="DropImage"/> instead.
    /// </remarks>
    internal void DropPlacements(Func<Graphics.LinePlacement, bool> predicate)
    {
        DropPlacements(_normalBuffer, predicate);
        DropPlacements(_altBuffer, predicate);
    }

    /// <summary>
    /// Removes whole placements by serial, from both buffers.
    /// </summary>
    /// <remarks>
    /// The second half of a positional delete. A placement is found through one of its cells but
    /// must go in its entirety -- removing only the runs on the named row or column would leave a
    /// picture with a band missing out of the middle of it. The serial is what makes "the rest of
    /// this picture" answerable from a run that knows nothing about the lines above and below it.
    /// </remarks>
    internal void DropPlacements(HashSet<int> serials)
    {
        if (serials.Count == 0)
            return;

        DropPlacements(placement => serials.Contains(placement.Serial));
    }

    /// <summary>
    /// Finds the runs whose cells satisfy a test, looking only at what is on screen.
    /// </summary>
    /// <remarks>
    /// Kitty's cell, row and column delete targets are stated in screen coordinates, so the
    /// scrollback is deliberately not searched: a picture scrolled out of view is not "at row 3"
    /// however many rows above it happen to be.
    /// </remarks>
    /// <param name="cellMatches">Called with the column and the screen row, zero-based.</param>
    internal List<Graphics.LinePlacement> CollectPlacementsOnScreen(Func<int, int, bool> cellMatches)
    {
        var found = new List<Graphics.LinePlacement>();
        var buffer = Buffer;

        for (int row = 0; row < Rows; row++)
        {
            var line = buffer.Lines[buffer.YBase + row];
            if (line is null || !line.HasImages)
                continue;

            foreach (var placement in line.Placements)
            {
                // Every run covering a matching cell, not merely the first. A picture covered by
                // another is still at that cell, and taking only the top one would make "delete
                // what is here" depend on what happened to be stacked over it.
                for (int col = placement.Column; col < placement.EndColumn && col < Cols; col++)
                {
                    if (col < 0 || !cellMatches(col, row))
                        continue;

                    found.Add(placement);
                    break;
                }
            }
        }

        return found;
    }

    private static void DropPlacements(Buffer.TerminalBuffer? buffer,
                                       Func<Graphics.LinePlacement, bool> predicate)
    {
        if (buffer is null)
            return;

        for (int i = 0; i < buffer.Lines.Length; i++)
        {
            var line = buffer.Lines[i];
            if (line is null || !line.HasImages)
                continue;

            line.RemovePlacements(predicate);
        }
    }

    /// <summary>
    /// Moves every animated image on by a slice of real time.
    /// </summary>
    /// <remarks>
    /// <para>The emulator owns no timer. It is driven entirely by <c>Write</c>, and starting a
    /// thread inside a library that has none -- to repaint a host that already has a render loop --
    /// would be the wrong place for it. So the host calls this with however long its last frame
    /// took, and is told whether anything moved.</para>
    /// <para>Both buffers and the registry, because an image can be transmitted and animated before
    /// it is ever placed, and one on the alternate screen keeps running while the normal screen is
    /// in front.</para>
    /// </remarks>
    /// <returns>True when some frame changed, so the host should repaint.</returns>
    public bool AdvanceAnimations(TimeSpan delta)
    {
        if (delta <= TimeSpan.Zero)
            return false;

        var moved = false;

        foreach (var image in CollectAnimatedImages())
            moved |= image.Animation!.Advance(delta);

        return moved;
    }

    /// <summary>Whether anything on screen or in the registry is currently animating.</summary>
    /// <remarks>
    /// A host uses this to decide whether it needs a repaint clock at all, rather than ticking
    /// forever for a terminal showing nothing but text.
    /// </remarks>
    public bool HasRunningAnimations()
    {
        foreach (var image in CollectAnimatedImages())
        {
            if (image.Animation!.State != Graphics.AnimationState.Stopped)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Every image that has frames.
    /// </summary>
    /// <remarks>
    /// Kept as its own list rather than found by scanning the buffers, because a host asks whether
    /// anything is animating on every frame and the answer for a terminal showing plain text has to
    /// cost nothing. It also reaches images that were transmitted and animated but never placed,
    /// which no amount of scanning cells would find.
    /// </remarks>
    private IEnumerable<Graphics.TerminalImage> CollectAnimatedImages() => _inputHandler.AnimatedImages;

    internal void EnforceImageBudget()
    {
        var budget = Options.MaxImageBytes;
        if (budget <= 0)
            return;

        var live = CollectLiveImages();
        long total = 0;
        foreach (var image in live)
            total += image.ByteCount;

        if (total <= budget)
            return;

        var doomed = new HashSet<Graphics.TerminalImage>();
        foreach (var image in live.OrderBy(i => i.Id))
        {
            if (total <= budget)
                break;
            doomed.Add(image);
            total -= image.ByteCount;
        }

        if (doomed.Count == 0)
            return;

        DropImages(_normalBuffer, doomed);
        DropImages(_altBuffer, doomed);
    }

    private HashSet<Graphics.TerminalImage> CollectLiveImages()
    {
        var live = new HashSet<Graphics.TerminalImage>();
        Collect(_normalBuffer);
        Collect(_altBuffer);
        return live;

        // Nullable because the fields are: the buffers are built in the constructor and never
        // cleared, but nothing in the type system says so, and a sweep of a buffer that does not
        // exist has nothing to find.
        void Collect(Buffer.TerminalBuffer? buffer)
        {
            if (buffer is null)
                return;

            for (int i = 0; i < buffer.Lines.Length; i++)
            {
                var line = buffer.Lines[i];
                if (line is null)
                    continue;
                // The line's own list, not a column scan. Scanning both costs more and undercounts:
                // a column covered by two overlapping runs reports only the first, so the second
                // could be doomed while still on screen.
                foreach (var image in line.Images)
                    live.Add(image);
            }
        }
    }

    /// <remarks>
    /// Takes a nullable buffer for the same reason <c>Collect</c> does: the fields are nullable, and
    /// dropping images from a buffer that does not exist is a no-op rather than an error.
    /// </remarks>
    private static void DropImages(Buffer.TerminalBuffer? buffer, HashSet<Graphics.TerminalImage> doomed)
    {
        if (buffer is null)
            return;

        for (int i = 0; i < buffer.Lines.Length; i++)
        {
            var line = buffer.Lines[i];
            if (line is null || !line.HasImages)
                continue;

            // Only the doomed runs. Clearing the line because one of its pictures was over budget
            // would take its other pictures with it, which is more destructive than the per-cell
            // code this replaced -- and would evict images the sweep had just decided to keep.
            line.RemoveImages(doomed);
        }
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
    /// Encodes a keyboard event under the Kitty keyboard protocol, honouring the flags the
    /// running application has set.
    /// </summary>
    /// <remarks>
    /// Only meaningful while <see cref="KittyKeyboardActive"/> is true; the host checks that and
    /// falls back to <see cref="GenerateKeyInput"/> / <see cref="GenerateCharInput"/> otherwise.
    /// A null return means this event sends NOTHING — a bare modifier press, or a release the
    /// flags say not to report — not that the host should try the legacy path instead.
    /// </remarks>
    /// <param name="ev">The keyboard event, with <see cref="KeyEvent.Code"/> filled in when known.</param>
    /// <param name="eventType">Press, repeat or release.</param>
    /// <returns>
    /// The bytes to send, or null when the event is suppressed or needs legacy encoding. Hosts
    /// should call this terminal-level method rather than <see cref="KittyKeyboard.Evaluate"/>, as
    /// it performs the mode-aware legacy hand-off.
    /// </returns>
    public string? GenerateKittyKeyInput(KeyEvent ev, KittyKeyboardEventType eventType = KittyKeyboardEventType.Press)
    {
        var result = KittyKeyboard.Evaluate(ev, KittyKeyboardState.Flags, eventType, Options.MacOptionIsMeta);
        // Legacy encodings describe presses and repeats only. Falling back for a release would
        // turn key-up into a second key-down. With no active flags this Kitty-specific entry point
        // retains its historical null result; the host uses GenerateKeyInput directly instead.
        if (result is not null
            || eventType == KittyKeyboardEventType.Release
            || KittyKeyboardState.Flags == KittyKeyboardFlags.None)
            return result;

        return KittyKeyboard.TryGetLegacyKey(ev, out var key)
            ? _keyboardInput.GenerateKeySequence(key, GetLegacyModifiers(ev))
            : null;
    }

    private static KeyModifiers GetLegacyModifiers(KeyEvent ev)
    {
        // KeyModifiers has no Meta/Super member, so legacy encoding cannot represent ev.MetaKey.
        var modifiers = KeyModifiers.None;
        if (ev.ShiftKey) modifiers |= KeyModifiers.Shift;
        if (ev.AltKey) modifiers |= KeyModifiers.Alt;
        if (ev.CtrlKey) modifiers |= KeyModifiers.Control;
        return modifiers;
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
    /// Generates an escape sequence for a mouse event, with the pointer's pixel position for
    /// SGR-Pixels reporting (DECSET 1016).
    /// </summary>
    /// <param name="button">The mouse button</param>
    /// <param name="x">The column position (0-based)</param>
    /// <param name="y">The row position (0-based)</param>
    /// <param name="pixelX">Horizontal offset from the left of the cell area, in device pixels (0-based)</param>
    /// <param name="pixelY">Vertical offset from the top of the cell area, in device pixels (0-based)</param>
    /// <param name="eventType">The type of mouse event</param>
    /// <param name="modifiers">Modifier keys held during the event</param>
    /// <returns>The escape sequence string to send to the application</returns>
    public string GenerateMouseEvent(MouseButton button, int x, int y, int pixelX, int pixelY, MouseEventType eventType, KeyModifiers modifiers = KeyModifiers.None)
    {
        return _mouseTracker.GenerateMouseEvent(button, x, y, pixelX, pixelY, eventType, modifiers);
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
    /// Gets the selection manager for text selection.
    /// </summary>
    public SelectionManager Selection => _selectionManager;

    /// <summary>
    /// Gets the mouse tracker (internal use for mode setting).
    /// </summary>
    internal MouseTracker GetMouseTracker() => _mouseTracker;

    // Internal methods for raising events (called by InputHandler)
    /// <summary>
    /// Whether this terminal's own replies use 8-bit C1 controls, per S7C1T and S8C1T.
    /// </summary>
    /// <remarks>
    /// Output only. Mode 1034 is the other direction -- what the KEYBOARD sends -- and the two
    /// are independent.
    /// </remarks>
    public bool EightBitControls { get; set; }

    /// <summary>
    /// Sends a reply, in 7-bit or 8-bit form depending on S7C1T/S8C1T.
    /// </summary>
    /// <remarks>
    /// <para>Converted HERE rather than at the fifty-odd places that build a reply. Every one of
    /// them writes the 7-bit form, which stays readable and is what the tests are written
    /// against; this is the single point where the choice can be applied without any of them
    /// having to remember it exists.</para>
    ///
    /// <para>Only the LEADING introducer and a trailing ST are rewritten, deliberately. A reply
    /// payload can contain anything -- base64 in OSC 52, a settings body in DECRQSS -- and
    /// rewriting every ESC pair found anywhere would corrupt one that happened to look like an
    /// introducer.</para>
    /// </remarks>
    /// <summary>
    /// Sends INPUT-direction data -- pasted text, and anything else standing in for what a
    /// keyboard would have sent -- with no C1 conversion.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="RaiseDataReceived"/> because S7C1T and S8C1T describe the
    /// form of the terminal's own REPORTS. Running paste through that conversion rewrote
    /// the leading ESC of a bracketed paste and left the closing bracket alone, so the two
    /// halves of the frame disagreed and the application never saw the paste end.
    /// </remarks>
    internal void RaiseInputData(string data) =>
        DataReceived?.Invoke(this, new TerminalEvents.DataEventArgs(data));

    internal void RaiseDataReceived(string data) =>
        DataReceived?.Invoke(this, new TerminalEvents.DataEventArgs(
            EightBitControls ? ToEightBitControls(data) : data));

    private static string ToEightBitControls(string data)
    {
        if (data.Length < 2 || data[0] != '\u001b')
            return data;

        var c1 = data[1] switch
        {
            '[' => '\u009b',   // CSI
            'P' => '\u0090',   // DCS
            ']' => '\u009d',   // OSC
            '^' => '\u009e',   // PM
            '_' => '\u009f',   // APC
            _ => '\u0000',
        };

        if (c1 == '\u0000')
            return data;

        var body = data[2..];

        // The string terminator that closes a DCS, OSC, PM or APC goes with it.
        if (body.EndsWith('\u001b' + "\\", StringComparison.Ordinal))
            body = body[..^2] + '\u009c';

        return c1 + body;
    }

    internal void RaiseClipboardWriteRequested(string target, string mimeType, byte[] data) =>
        RaiseClipboardWriteRequested(target, new[] { new TerminalEvents.ClipboardFormat(mimeType, data) });

    internal void RaiseClipboardWriteRequested(
        string target, IReadOnlyList<TerminalEvents.ClipboardFormat> formats) =>
        ClipboardWriteRequested?.Invoke(this, new TerminalEvents.ClipboardWriteEventArgs(target, formats));

    internal void RaiseClipboardReadRequested(TerminalEvents.ClipboardReadEventArgs args) =>
        ClipboardReadRequested?.Invoke(this, args);
    
    internal void RaiseTitleChanged(string title) => 
        TitleChanged?.Invoke(this, new TerminalEvents.TitleChangeEventArgs(title));
    
    internal void RaiseDirectoryChanged(string directory) => 
        DirectoryChanged?.Invoke(this, new TerminalEvents.DirectoryChangeEventArgs(directory));


    internal void RaiseHyperlinkChanged(string? url) =>
        HyperlinkChanged?.Invoke(this, new TerminalEvents.HyperlinkEventArgs(url ?? string.Empty, url == null));

    /// <summary>
    /// The row of the nearest prompt above <paramref name="fromRow"/>, if there is one.
    /// </summary>
    /// <remarks>
    /// <para>Jump-to-previous-prompt, which is the first thing shell integration is worth having
    /// for. Rows are absolute indices into <see cref="TerminalBuffer.Lines"/>, so scrollback is
    /// included and the answer is what a host scrolls to directly.</para>
    /// <para>Here rather than in every host because it is the same walk each time and it has an
    /// off-by-one worth getting right once: the search is strictly above the row given, so calling
    /// it repeatedly from its own answer walks back through the history rather than sticking on the
    /// prompt it just found.</para>
    /// </remarks>
    public bool TryFindPreviousPrompt(int fromRow, out int row)
    {
        var lines = Buffer.Lines;
        // Clamped before the subtraction: int.MinValue - 1 wraps to a huge positive index.
        for (var i = Math.Clamp(fromRow, 0, lines.Length) - 1; i >= 0; i--)
        {
            if (HasPromptStart(lines[i]))
            {
                row = i;
                return true;
            }
        }

        row = -1;
        return false;
    }

    /// <summary>The row of the nearest prompt below <paramref name="fromRow"/>, if there is one.</summary>
    public bool TryFindNextPrompt(int fromRow, out int row)
    {
        var lines = Buffer.Lines;
        // Clamped before the addition: int.MaxValue + 1 wraps negative and indexes below zero.
        for (var i = Math.Clamp(fromRow, -1, lines.Length - 1) + 1; i < lines.Length; i++)
        {
            if (HasPromptStart(lines[i]))
            {
                row = i;
                return true;
            }
        }

        row = -1;
        return false;
    }

    private static bool HasPromptStart(Buffer.BufferLine? line)
    {
        if (line is null || !line.HasMarks)
            return false;

        foreach (var mark in line.Marks)
        {
            if (mark.Kind == ShellIntegrationMark.PromptStart)
                return true;
        }

        return false;
    }

    internal void RaiseShellIntegrationMark(ShellIntegrationMark mark, int? exitCode) =>
        ShellIntegrationMarkReceived?.Invoke(this, new TerminalEvents.ShellIntegrationEventArgs(mark, exitCode));

    internal void RaiseProgressChanged(ProgressState state, int value) =>
        ProgressChanged?.Invoke(this, new TerminalEvents.ProgressEventArgs(state, value));

    internal void RaiseNotificationReceived(string text) =>
        NotificationReceived?.Invoke(this, new TerminalEvents.NotificationEventArgs(text));
    internal void RaiseAttentionRequested(string action) =>
        AttentionRequested?.Invoke(this, new TerminalEvents.AttentionRequestedEventArgs(action));

    internal void RaiseKittyNotificationReceived(string? identifier, string? title, string? body, int? urgency, string? icon) =>
        NotificationReceived?.Invoke(this, new TerminalEvents.NotificationEventArgs(identifier, title, body, urgency, icon));
    internal void RaiseOscReceived(string identifier, int code, string data, string raw, bool recognized) =>
        OscReceived?.Invoke(this, new TerminalEvents.OscReceivedEventArgs(identifier, code, data, raw, recognized));
    
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
    
    internal TerminalEvents.FontQueryEventArgs RaiseFontQueryRequested()
    {
        var args = new TerminalEvents.FontQueryEventArgs();
        FontQueryRequested?.Invoke(this, args);
        return args;
    }

    internal TerminalEvents.WindowInfoRequestedEventArgs RaiseWindowInfoRequested(WindowInfoRequest request)
    {
        var args = new TerminalEvents.WindowInfoRequestedEventArgs(request);
        WindowInfoRequested?.Invoke(this, args);
        return args;
    }

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

        var shapeBefore = PointerShape;
        // ONE cursor, two screens: xterm shares the cursor across the switch, which is what lets
        // DECSET 47 flip screens mid-drawing without teleporting the pen.
        // Switching screens ends the status line's turn with the cursor. Holding the status row as
        // the write target across the switch is not available: the switch's own work -- 1049's
        // erase among it -- runs through the input handler's buffer, so leaving that pointed at a
        // one-row status buffer sends a full-screen erase off the end of it. Handing the cursor
        // back first keeps one rule: the status line is selected, or the screen is.
        //
        // The ROW is untouched. Only the selection ends, so a program that switches screens and
        // comes back finds its status message still there.
        if (_statusLineActive)
            SetActiveStatusDisplay(0);

        if (LineExitedViewport is not null)
            RaiseBufferDeactivatedLines(_normalBuffer!);
        var x = _buffer.X;
        var y = _buffer.Y;
        _buffer = _altBuffer!;
        _usingAltBuffer = true;
        _buffer.SetCursor(x, y);
        // The protocol's flags are per screen; the switch itself carries them so every path in
        // (1049, 1047, 47) behaves the same.
        KittyKeyboardState.SwitchScreen(toAltScreen: true);
        _inputHandler.SetBuffer(_buffer);

        // The screens keep separate shape stacks, so switching can change the current shape without
        // any application asking for it -- the host has to hear about that like any other change.
        RaisePointerShapeChanged(shapeBefore);
        BufferChanged?.Invoke(this, new TerminalEvents.BufferChangedEventArgs(BufferType.Alternate));
    }

    /// <summary>
    /// Switches to the normal buffer.
    /// </summary>
    public void SwitchToNormalBuffer()
    {
        if (!_usingAltBuffer)
            return;

        var shapeBefore = PointerShape;
        // Switching screens ends the status line's turn with the cursor. Holding the status row as
        // the write target across the switch is not available: the switch's own work -- 1049's
        // erase among it -- runs through the input handler's buffer, so leaving that pointed at a
        // one-row status buffer sends a full-screen erase off the end of it. Handing the cursor
        // back first keeps one rule: the status line is selected, or the screen is.
        //
        // The ROW is untouched. Only the selection ends, so a program that switches screens and
        // comes back finds its status message still there.
        if (_statusLineActive)
            SetActiveStatusDisplay(0);

        if (LineExitedViewport is not null)
            RaiseBufferDeactivatedLines(_altBuffer!);
        var x = _buffer.X;
        var y = _buffer.Y;
        _buffer = _normalBuffer!;
        _usingAltBuffer = false;
        _buffer.SetCursor(x, y);
        KittyKeyboardState.SwitchScreen(toAltScreen: false);
        _inputHandler.SetBuffer(_buffer);

        RaisePointerShapeChanged(shapeBefore);
        BufferChanged?.Invoke(this, new TerminalEvents.BufferChangedEventArgs(BufferType.Normal));
    }

    /// <summary>
    /// Handles C0 control characters.
    /// </summary>
    private void HandleExecute(int code)
    {
        // A control character between a print and a REP means there is no preceding character
        // any more -- cleared here at the dispatch point, like the sequence dispatchers do.
        // Except ESC: this parser EXECUTES the introducer before transitioning into the sequence,
        // so clearing on it would cancel REP on the way into REP's own CSI. Whether the sequence
        // that follows cancels is the sequence dispatcher's decision, not the introducer's.
        if (code != 0x1B)
            _inputHandler.CancelRepeat();

        switch (code)
        {
            case 0x07: // BEL
                BellRang?.Invoke(this, EventArgs.Empty);
                break;

            case 0x08: // BS - Backspace
                // DECSET 45 was stored and reported by DECRQM but nothing ever read it, so a
                // program that enabled reverse wraparound and backspaced off the left edge sat
                // there -- a shell erasing a wrapped command line stopped at the wrap and left
                // the first row of it on screen. The motion lives with the other cursor motions,
                // so it is bounded by the same margins they are.
                _inputHandler.Backspace();
                break;

            case 0x09: // HT - Tab
                // The motion lives with CHT, which is n of these -- see InputHandler.Tab.
                // They were separate and drifted twice, most recently over the phantom
                // column, where only this one checked for a pending wrap.
                _inputHandler.Tab();
                break;

            case 0x0A: // LF - Line Feed
            case 0x0B: // VT - Vertical Tab
            case 0x0C: // FF - Form Feed
                LineFeed();
                break;

            case 0x0D: // CR - Carriage Return
                _buffer.CarriageReturn();
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
            // A cursor OUTSIDE the left/right margins is outside the region: a line feed there
            // neither scrolls the region's contents nor steps past its bottom row. It stays put,
            // as it does on the screen's last row.
            var insideColumns = !LeftRightMarginMode
                                || (_buffer.X >= _buffer.ScrollLeft && _buffer.X <= _buffer.ScrollRight);
            if (insideColumns)
                _buffer.ScrollUp(1);
        }
        else if (_buffer.Y == Rows - 1)
        {
            // Below the region entirely: pinned at the screen's edge.
        }
        else
        {
            // Move cursor down
            _buffer.SetCursor(_buffer.X, _buffer.Y + 1);
        }

        // If ConvertEol is enabled -- or a program set LNM, which means the same thing from
        // the other side of the pty -- also do a carriage return, to the line's start as CR
        // defines it, which with margins is the left margin, not column 0
        if (Options.ConvertEol || LineFeedMode)
        {
            _buffer.CarriageReturn();
        }

        LineFed?.Invoke(this, new TerminalEvents.LineFeedEventArgs("\n"));
    }

    /// <summary>Raises a synchronous snapshot opportunity before a row leaves the viewport.</summary>
    private void RaiseLineExitedViewport(BufferLine line, BufferType buffer, LineExitReason reason)
    {
        LineExitedViewport?.Invoke(this, new TerminalEvents.LineExitedViewportEventArgs(line, buffer, reason));
    }

    /// <summary>Raises the synchronous snapshot opportunity at a potential redraw boundary.</summary>
    internal void RaiseViewportRedrawStarting()
    {
        ViewportRedrawStarting?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raises exit events for the meaningful rows of a buffer before deactivation.</summary>
    private void RaiseBufferDeactivatedLines(Buffer.TerminalBuffer buffer)
    {
        var firstLine = buffer.BaseY;
        var lastLine = Math.Min(firstLine + Rows, buffer.Lines.Length) - 1;
        while (lastLine >= firstLine && buffer.Lines[lastLine]?.HasSnapshotContent() == false)
            lastLine--;

        for (int i = firstLine; i <= lastLine; i++)
        {
            var line = buffer.Lines[i];
            if (line is not null)
                RaiseLineExitedViewport(line, ReferenceEquals(buffer, _altBuffer) ? BufferType.Alternate : BufferType.Normal, LineExitReason.BufferDeactivated);
        }
    }

    /// <summary>Whether <see cref="Dispose"/> has run. A disposed terminal accepts no writes.</summary>
    private bool _disposed;

    /// <summary>
    /// Releases the parser subscriptions and clears every event handler.
    /// </summary>
    /// <remarks>
    /// The class declares <see cref="IDisposable"/> now. It always had this method, but without
    /// the interface no `using` statement, no DI container and no analyzer could see it -- an
    /// embedder had to know by reading the source, and the handlers stayed subscribed for anyone
    /// who did not.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // The options snapshot outlives this terminal whenever the caller kept a reference to what
        // it passed in, and these hooks hold the buffer and the palette, so they go with the rest
        // of the handlers.
        Options.ScrollbackChanged = null;
        Options.ThemeChanged = null;
        Options.TabStopWidthChanged = null;

        // Unsubscribe from parser events
        _parser.PrintFast = null;
        _parser.PrintRunFast = null;
        _parser.PrintByteRunFast = null;
        _parser.ExecuteFast = null;
        _parser.CsiFast = null;
        _parser.EscFast = null;
        _parser.OscFast = null;

        _parser.DcsHook -= OnParserDcsHook;
        _parser.DcsPut -= OnParserDcsPut;
        _parser.DcsUnhook -= OnParserDcsUnhook;
        _parser.ApcHook -= OnParserApcHook;
        _parser.ApcPut -= OnParserApcPut;
        _parser.ApcUnhook -= OnParserApcUnhook;

        // Clear all event subscriptions
        DataReceived = null;
        ClipboardWriteRequested = null;
        ClipboardReadRequested = null;
        FontQueryRequested = null;
        CursorStyleChanged = null;
        SynchronizedOutputChanged = null;
        BufferChanged = null;
        TitleChanged = null;
        StatusLineChanged = null;
        BellRang = null;
        Resized = null;
        Scrolled = null;
        LineFed = null;
        LineExitedViewport = null;
        ViewportRedrawStarting = null;
        DirectoryChanged = null;
        HyperlinkChanged = null;
        ShellIntegrationMarkReceived = null;
        PointerShapeChanged = null;
        ProgressChanged = null;
        NotificationReceived = null;
        AttentionRequested = null;
        OscReceived = null;
        
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

    // ---- Bracketed paste MIME (private mode 5522) -------------------------------------------

    private PendingPaste? _pendingPaste;

    private sealed record PendingPaste(
        string Token, string Target, TerminalPaste Paste, DateTime IssuedAtUtc);

    /// <summary>How long a paste token stays redeemable. Checked at redemption; single-use.</summary>
    private static readonly TimeSpan PasteTokenLifetime = TimeSpan.FromSeconds(60);

    /// <summary>The clock the token lifetime is measured on; swappable so expiry is testable.</summary>
    internal Func<DateTime> PasteClock = static () => DateTime.UtcNow;

    /// <summary>
    /// Pastes plain text. The convenience overload of <see cref="Paste(TerminalPaste)"/> for
    /// hosts that only ever paste text.
    /// </summary>
    public void Paste(string text) =>
        Paste(new TerminalPaste(
            new[] { "text/plain" },
            _ => System.Text.Encoding.UTF8.GetBytes(text)));

    /// <summary>
    /// The paste entry point — and the only place the spec's precedence rule can live, which is
    /// why the library owns it. With mode 5522 set, the paste is ANNOUNCED: an unsolicited Kitty
    /// clipboard read response listing the available MIME types with a single-use token, and no
    /// bracketing — the terminal must never send both for one paste. With only mode 2004 set,
    /// the text/plain content is bracketed the classic way. With neither, the raw text is sent.
    /// </summary>
    /// <remarks>
    /// <para>Additive by design: <see cref="BracketedPasteMode"/> stays public and an embedder
    /// that wraps its own pastes keeps working — but such an embedder never gets 5522 behaviour,
    /// because only this method knows how to announce one.</para>
    /// <para>Serialize with <see cref="Write(string)"/>, like every other member: this publishes
    /// token state the write path's redemption reads. A paste that cannot supply text/plain is
    /// dropped when only mode 2004 (or neither mode) is set — there is nothing safe to
    /// flatten.</para>
    /// </remarks>
    public void Paste(TerminalPaste paste)
    {
        if (paste.MimeTypes.Count == 0)
            return;

        if (PasteNotificationMode)
        {
            AnnouncePaste(paste);
            return;
        }

        // 2004 and the raw path flatten to text, as terminals always have. Only a mime the
        // paste actually OFFERED is asked for — an accessor is entitled to be a plain lookup over
        // its own list — and a paste that cannot supply text/plain is dropped outside mode 5522,
        // because there is nothing safe to flatten.
        var text = paste.MimeTypes.Contains("text/plain") && paste.GetData("text/plain") is { } bytes
            ? System.Text.Encoding.UTF8.GetString(bytes)
            : null;
        if (text is null)
            return;

        text = PrepareForPaste(text);

        // RaiseInputData, not RaiseDataReceived: this is keyboard-direction traffic, and the
        // reply converter would rewrite the OPENING bracket to 0x9B and leave the closing one
        // 7-bit -- by its own rule, which only touches a leading introducer. An application
        // watching for ESC [ 201 ~ would then never see the end of the paste. S8C1T describes
        // what the terminal REPORTS; mode 1034 is the keyboard direction, and they are
        // independent.
        RaiseInputData(BracketedPasteMode
            ? $"\u001b[200~{text}\u001b[201~"
            : text);
    }

    /// <summary>
    /// Makes pasted text safe to hand to the application: newlines become carriage returns, and
    /// control characters are dropped unless <see cref="TerminalOptions.AllowPasteControls"/>
    /// says otherwise.
    /// </summary>
    /// <remarks>
    /// <para>The newline half is parity with every other terminal: a paste arrives with LF or CRLF
    /// line endings, and an application reading a terminal expects CR, because that is what the
    /// Return key sends. Without it, multi-line pastes reach a shell as line feeds it does not
    /// treat as submitted lines.</para>
    /// <para>The control half is why this method exists at all. The bracketed-paste wrapper is a
    /// promise to the application that everything between the markers is data -- and the wrapper is
    /// built from the same byte stream as its contents, so text containing <c>ESC [ 2 0 1 ~</c>
    /// ends the promise early and the remainder arrives as though typed. Filtering ESC out of the
    /// payload is what makes the wrapper mean anything; anything narrower (escaping only the
    /// terminator) still lets a paste start a sequence of its own.</para>
    /// </remarks>
    private string PrepareForPaste(string text)
    {
        var normalized = text.Replace("\r\n", "\r").Replace('\n', '\r');
        if (Options.AllowPasteControls)
            return normalized;

        // Tab and carriage return are the two controls a paste legitimately carries: one is
        // indentation, the other is the line ending everything above just normalized to.
        // char.IsControl covers C1 as well as C0, which is wanted: U+009B is eight-bit CSI and
        // opens a sequence exactly as ESC [ does, so a filter that stopped at C0 would leave the
        // wrapper breakable by a paste that spells its escape differently.
        if (!normalized.Any(c => char.IsControl(c) && c != '\r' && c != '\t'))
            return normalized;

        var kept = new System.Text.StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (!char.IsControl(c) || c == '\r' || c == '\t')
                kept.Append(c);
        }

        return kept.ToString();
    }

    private void AnnouncePaste(TerminalPaste paste)
    {
        // A new paste supersedes the old token outright: at most one is ever redeemable.
        //
        // The LOGICAL password is ASCII text (128 random bits as hex), and the wire carries its
        // base64-encoded UTF-8 — because the spec defines pw as a base64-encoded UTF-8 string,
        // and a conforming client decodes it, holds the text, and re-encodes it to redeem. Raw
        // random bytes are not valid UTF-8, and such a client would corrupt them in transit.
        var tokenBytes = new byte[16];
        System.Security.Cryptography.RandomNumberGenerator.Fill(tokenBytes);
        var logical = Convert.ToHexString(tokenBytes);
        var token = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(logical));
        var target = paste.FromPrimary ? "p" : "c";
        _pendingPaste = new PendingPaste(logical, target, paste, PasteClock());

        var loc = paste.FromPrimary ? ":loc=primary" : string.Empty;
        var mimeList = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes(string.Join(' ', paste.MimeTypes)));

        RaiseDataReceived($"\u001b]5522;type=read:status=OK{loc}:pw={token}\u001b\\");
        RaiseDataReceived($"\u001b]5522;type=read:status=DATA:mime=Lg==:pw={token};{mimeList}\u001b\\");
        RaiseDataReceived($"\u001b]5522;type=read:status=DONE:pw={token}\u001b\\");
    }

    /// <summary>
    /// Redeems a paste token carried on an OSC 5522 read. Single use — a hit consumes the token
    /// whatever happens next — and worthless outside its scope: the spec has the token bound to
    /// the clipboard location that produced the paste, accompanied by a name, and short-lived.
    /// A miss is NOT an error; the caller falls back to its standard security path, exactly as
    /// the spec directs for an absent or invalid password.
    /// </summary>
    internal TerminalPaste? TryRedeemPaste(string token, string name, string target)
    {
        var pending = _pendingPaste;
        if (pending is null || name.Length == 0)
            return null;

        // The wire form is base64 of the logical password's UTF-8; conforming clients may have
        // decoded and re-encoded it, so comparison happens on the decoded text.
        string presented;
        try
        {
            presented = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        }
        catch (FormatException)
        {
            return null;
        }

        if (pending.Token != presented)
            return null;

        // The token was presented: consume it now, valid or not — replaying a rejected
        // redemption must not get a second try. Compare-and-swap so a NEWER paste published
        // between the read above and this consume is left alone rather than wiped.
        if (!ReferenceEquals(
                System.Threading.Interlocked.CompareExchange(ref _pendingPaste, null, pending),
                pending))
            return null;

        if (pending.Target != target
            || PasteClock() - pending.IssuedAtUtc > PasteTokenLifetime)
            return null;

        return pending.Paste;
    }

    internal void InvalidatePendingPaste() => _pendingPaste = null;
}

/// <summary>
/// One paste, as the host hands it to <see cref="Terminal.Paste(TerminalPaste)"/>: the MIME
/// types on offer, where it came from, and an accessor the terminal calls for the types the
/// application actually asks for — so nothing is encoded or copied for formats nobody wants.
/// </summary>
public sealed class TerminalPaste
{
    public TerminalPaste(IReadOnlyList<string> mimeTypes, Func<string, byte[]?> getData, bool fromPrimary = false)
    {
        MimeTypes = mimeTypes;
        GetData = getData;
        FromPrimary = fromPrimary;
    }

    /// <summary>The MIME types this paste can supply, most specific first.</summary>
    public IReadOnlyList<string> MimeTypes { get; }

    /// <summary>Returns the content for one MIME type, or null when it cannot after all.</summary>
    public Func<string, byte[]?> GetData { get; }

    /// <summary>True when the paste came from the primary selection rather than the clipboard.</summary>
    public bool FromPrimary { get; }
}
