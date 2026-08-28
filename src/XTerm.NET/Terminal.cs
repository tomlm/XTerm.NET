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
public class Terminal
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
    public bool ReverseVideo { get; set; }
    public bool SendFocusEvents { get; set; }
    public bool Win32InputMode { get; set; }

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
    public string? CurrentDirectory { get; set; }
    public string? CurrentHyperlink { get; set; }

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
    /// Fired when progress is reported via OSC 9 ; 4.
    /// </summary>
    public event EventHandler<TerminalEvents.ProgressEventArgs>? ProgressChanged;

    /// <summary>
    /// Fired when a desktop notification is requested via OSC 9.
    /// </summary>
    public event EventHandler<TerminalEvents.NotificationEventArgs>? NotificationReceived;

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
    /// Fired when the active buffer is changed.
    /// </summary>
    public event EventHandler<TerminalEvents.BufferChangedEventArgs>? BufferChanged;

    public Terminal(TerminalOptions? options = null)
    {
        Options = options ?? new TerminalOptions();
        Cols = Options.Cols;
        Rows = Options.Rows;
        Title = string.Empty;
        Colors = new ColorPalette(Options.Theme);

        // Initialize buffers
        _normalBuffer = new Buffer.TerminalBuffer(Cols, Rows, Options.Scrollback);
        _altBuffer = new Buffer.TerminalBuffer(Cols, Rows, 0, hasScrollback: false);
        _buffer = _normalBuffer;
        _usingAltBuffer = false;

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
        OriginMode = false;
        CursorVisible = true;
        ReverseWraparound = false;
        SendFocusEvents = false;
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
    public void Write(string data)
    {
        if (string.IsNullOrEmpty(data))
            return;

        _parser.Parse(data);
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
        if (data.IsEmpty)
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
            _inputHandler.SetBuffer(_buffer);
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
        ReverseVideo = false;
        SendFocusEvents = false;
        EightBitInput = false;
        MetaSendsEscape = false;  // Default is disabled
        AltSendsEscape = false;
        Win32InputMode = false;

        // Through the raiser rather than assigned, so a renderer holding a frame is told it can
        // stop -- and so the flag cannot be left set. It is also the dedupe key for the event, so a
        // stale true would swallow the NEXT application's begin and leave its end raising a lone
        // false: the transitions-only contract inverted and staying inverted. RIS is exactly how
        // someone recovers from an application that set the mode and died.
        RaiseSynchronizedOutputChanged(false);

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
    /// Gets the selection manager for text selection.
    /// </summary>
    public SelectionManager Selection => _selectionManager;

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
        TitleChanged = null;
        BellRang = null;
        Resized = null;
        Scrolled = null;
        LineFed = null;
        DirectoryChanged = null;
        HyperlinkChanged = null;
        ShellIntegrationMarkReceived = null;
        ProgressChanged = null;
        NotificationReceived = null;
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
}
