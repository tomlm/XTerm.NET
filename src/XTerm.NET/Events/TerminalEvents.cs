using XTerm.Common;

namespace XTerm.Events;

/// <summary>
/// Terminal event data and handlers.
/// </summary>
public static class TerminalEvents
{
    /// <summary>
    /// Data event - fired when the terminal receives input data.
    /// </summary>
    public class DataEventArgs : EventArgs
    {
        public string Data { get; }
        
        public DataEventArgs(string data)
        {
            Data = data;
        }
    }

    /// <summary>
    /// Clipboard data supplied by an application.
    /// </summary>
    /// <summary>One format of a clipboard transfer: a MIME type and its bytes.</summary>
    public readonly record struct ClipboardFormat(string MimeType, byte[] Data);

    /// <summary>
    /// Clipboard data supplied by an application — raised ONCE per transfer, carrying every
    /// format, because platform clipboards replace their contents on each set: a host must build
    /// one data object from the whole list and commit it once, or a multi-format transfer would
    /// survive only as whichever format happened to be set last. OSC 52 transfers carry a single
    /// text/plain format; a Kitty OSC 5522 transfer carries each transmitted MIME type in the
    /// order it first appeared, followed by its aliases sharing the same bytes.
    /// </summary>
    public class ClipboardWriteEventArgs : EventArgs
    {
        public ClipboardWriteEventArgs(string target, IReadOnlyList<ClipboardFormat> formats)
        {
            Target = target;
            Formats = formats;
        }

        public string Target { get; }

        /// <summary>Every format in the transfer. Never empty.</summary>
        public IReadOnlyList<ClipboardFormat> Formats { get; }

        /// <summary>The first format's MIME type — the whole transfer, for single-format writes.</summary>
        public string MimeType => Formats[0].MimeType;

        /// <summary>The first format's bytes.</summary>
        public byte[] Data => Formats[0].Data;

        /// <summary>
        /// The transfer's text: the first <c>text/*</c> format decoded as UTF-8, or the first
        /// format when none is text. Empty text requests clearing the selection.
        /// </summary>
        public string Text
        {
            get
            {
                foreach (var format in Formats)
                {
                    if (format.MimeType.StartsWith("text/", StringComparison.Ordinal))
                        return System.Text.Encoding.UTF8.GetString(format.Data);
                }
                return System.Text.Encoding.UTF8.GetString(Formats[0].Data);
            }
        }
    }

    /// <summary>
    /// A request for clipboard data (OSC 52 or Kitty OSC 5522). Answer by setting
    /// <see cref="Data"/> (or <see cref="Text"/>) before the handler returns; leave it null to
    /// decline. A host whose clipboard API is asynchronous cannot do that without deadlocking
    /// the thread the terminal is driven on, so it calls <see cref="Defer"/> in the handler and
    /// <see cref="Respond(byte[]?)"/> when its await completes — a terminal-to-application
    /// message is legal at any time, so the response is simply emitted then.
    /// </summary>
    public class ClipboardReadEventArgs : EventArgs
    {
        public ClipboardReadEventArgs(string target, string mimeType)
        {
            Target = target;
            MimeType = mimeType;
        }

        public string Target { get; }
        public string MimeType { get; }
        public byte[]? Data { get; set; }

        /// <summary>The answer as UTF-8 text — a convenience over <see cref="Data"/>.</summary>
        public string? Text
        {
            get => Data is null ? null : System.Text.Encoding.UTF8.GetString(Data);
            set => Data = value is null ? null : System.Text.Encoding.UTF8.GetBytes(value);
        }

        /// <summary>
        /// True once <see cref="Defer"/> is called: the answer arrives via
        /// <see cref="Respond(byte[]?)"/> after the handler returns, and the request stays open
        /// until it does. OSC 52 treats a request that never answers as a decline (silence), but
        /// Kitty OSC 5522 must answer EVERY request — its reply cannot begin until each
        /// requested mime has resolved — so a deferring host must always complete the call.
        /// </summary>
        public bool Deferred { get; private set; }

        /// <summary>Marks the request as answered later; call inside the handler.</summary>
        public void Defer() => Deferred = true;

        private Action<byte[]?>? _respond;

        /// <summary>
        /// Completes a deferred request. Null declines — for OSC 52 that is the same silence an
        /// unhandled request produces; for OSC 5522 it counts the mime as unavailable. Call it
        /// from the thread the terminal is driven on. A synchronous answer wins: once
        /// <see cref="Data"/> was set when the handler returned, and after the first call here,
        /// further calls are ignored.
        /// </summary>
        public void Respond(byte[]? data)
        {
            var respond = _respond;
            _respond = null;
            respond?.Invoke(data);
        }

        /// <summary>Completes a deferred request with UTF-8 text; null declines.</summary>
        public void Respond(string? text) =>
            Respond(text is null ? null : System.Text.Encoding.UTF8.GetBytes(text));

        /// <summary>Installs what <see cref="Respond(byte[]?)"/> emits; disarmed once used or
        /// once the synchronous path has answered.</summary>
        internal void Arm(Action<byte[]?> respond) => _respond = respond;

        /// <summary>
        /// Claims the response for the synchronous path. False when <see cref="Respond(byte[]?)"/>
        /// already answered — a handler that responds from inside the handler AND sets
        /// <see cref="Data"/> must still produce exactly one response, so the loser of this claim
        /// stays silent.
        /// </summary>
        internal bool Disarm()
        {
            var wasArmed = _respond is not null;
            _respond = null;
            return wasArmed;
        }
    }

    /// <summary>
    /// Resize event - fired when the terminal is resized.
    /// </summary>
    public class ResizeEventArgs : EventArgs
    {
        public int Cols { get; }
        public int Rows { get; }
        
        public ResizeEventArgs(int cols, int rows)
        {
            Cols = cols;
            Rows = rows;
        }
    }

    /// <summary>
    /// Title change event - fired when the terminal title changes.
    /// </summary>
    public class TitleChangeEventArgs : EventArgs
    {
        public string Title { get; }
        
        public TitleChangeEventArgs(string title)
        {
            Title = title;
        }
    }

    /// <summary>
    /// Cursor move event - fired when the cursor position changes.
    /// </summary>
    public class CursorMoveEventArgs : EventArgs
    {
        public int X { get; }
        public int Y { get; }
        
        public CursorMoveEventArgs(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    /// <summary>
    /// Line feed event - fired when a line feed occurs.
    /// </summary>
    public class LineFeedEventArgs : EventArgs
    {
        public string Data { get; }
        
        public LineFeedEventArgs(string data)
        {
            Data = data;
        }
    }

    /// <summary>
    /// Scroll event - fired when the terminal scrolls.
    /// </summary>
    public class ScrollEventArgs : EventArgs
    {
        public int YDisp { get; }
        public int YBase { get; }
        
        public ScrollEventArgs(int yDisp, int yBase)
        {
            YDisp = yDisp;
            YBase = yBase;
        }
    }

    /// <summary>
    /// Selection change event - fired when the selection changes.
    /// </summary>
    public class SelectionChangeEventArgs : EventArgs
    {
        public string SelectedText { get; }
        
        public SelectionChangeEventArgs(string selectedText)
        {
            SelectedText = selectedText;
        }
    }

    /// <summary>
    /// Render event - fired before/after rendering.
    /// </summary>
    public class RenderEventArgs : EventArgs
    {
        public int StartRow { get; }
        public int EndRow { get; }
        
        public RenderEventArgs(int startRow, int endRow)
        {
            StartRow = startRow;
            EndRow = endRow;
        }
    }

    /// <summary>
    /// Directory change event - fired when the current directory changes.
    /// </summary>
    public class DirectoryChangeEventArgs : EventArgs
    {
        public string Directory { get; }
        
        public DirectoryChangeEventArgs(string directory)
        {
            Directory = directory;
        }
    }

    /// <summary>
    /// An atomic update beginning or ending — DEC private mode 2026.
    /// </summary>
    /// <remarks>
    /// An EventArgs rather than a bare bool so that anything this needs to carry later — how the
    /// update ended, how long it was held — can be added without breaking every subscriber. The
    /// difference between a minor version and a major one.
    /// </remarks>
    public class SynchronizedOutputEventArgs : EventArgs
    {
        public SynchronizedOutputEventArgs(bool active)
        {
            Active = active;
        }

        /// <summary>True when an application has begun an atomic update, false when it has ended.</summary>
        public bool Active { get; }
    }

    /// <summary>
    /// Shell integration event - fired for each OSC 133 mark.
    /// </summary>
    public class ShellIntegrationEventArgs : EventArgs
    {
        public ShellIntegrationEventArgs(ShellIntegrationMark mark, int? exitCode)
        {
            Mark = mark;
            ExitCode = exitCode;
        }

        /// <summary>
        /// Which mark the shell reported.
        /// </summary>
        public ShellIntegrationMark Mark { get; }

        /// <summary>
        /// The exit code on <see cref="ShellIntegrationMark.CommandFinished"/>, when the shell sent
        /// one. Null on every other mark, and also on CommandFinished when the shell omitted it --
        /// cmd.exe cannot read the previous command's status from its prompt, so it always omits it.
        /// Null means "not reported", never "succeeded".
        /// </summary>
        public int? ExitCode { get; }
    }

    /// <summary>
    /// Progress event - fired for OSC 9 ; 4 progress reports.
    /// </summary>
    public class ProgressEventArgs : EventArgs
    {
        public ProgressEventArgs(ProgressState state, int value)
        {
            State = state;
            Value = value;
        }

        /// <summary>
        /// The progress state.
        /// </summary>
        public ProgressState State { get; }

        /// <summary>
        /// Percentage from 0 to 100. Only meaningful for <see cref="ProgressState.Normal"/>,
        /// <see cref="ProgressState.Error"/> and <see cref="ProgressState.Warning"/>.
        /// </summary>
        public int Value { get; }
    }

    /// <summary>
    /// Notification event - fired for OSC 9 and Kitty OSC 99 desktop notifications.
    /// </summary>
    public class NotificationEventArgs : EventArgs
    {
        public NotificationEventArgs(string text)
            : this(null, null, text, null, null)
        {
        }

        /// <summary>
        /// The notification body as sent by the application. Retained for OSC 9 compatibility.
        /// </summary>
        public string Text { get; }

        public NotificationEventArgs(string? identifier, string? title, string? body, int? urgency, string? icon)
        {
            Identifier = identifier;
            Title = title;
            Body = body;
            Urgency = urgency;
            Icon = icon;
            Text = body ?? title ?? string.Empty;
        }

        public string? Identifier { get; }

        public string? Title { get; }

        public string? Body { get; }

        public int? Urgency { get; }

        public string? Icon { get; }
    }


    /// <summary>
    /// Raw OSC event - fired for EVERY OSC sequence the parser completes, including ones this
    /// library does not implement.
    /// </summary>
    /// <remarks>
    /// The escape hatch for OSC codes the terminal does not know yet. Without it an unrecognized
    /// sequence reaches <c>Debug.WriteLine</c> and is gone, and nothing downstream can compensate,
    /// because the parser's own Osc event is not reachable from <see cref="XTerm.Terminal"/>.
    ///
    /// Observation only: this fires AFTER any built-in handling, and setting nothing on it changes
    /// what the terminal did. Use <see cref="Recognized"/> to implement only what the library
    /// currently ignores, so a handler stops doing so on its own once a code is implemented here.
    /// </remarks>
    public class OscReceivedEventArgs : EventArgs
    {
        public OscReceivedEventArgs(string identifier, int code, string data, string raw, bool recognized)
        {
            Identifier = identifier;
            Code = code;
            Data = data;
            Raw = raw;
            Recognized = recognized;
        }

        /// <summary>
        /// The identifier field verbatim, before the first ';'. Not always numeric.
        /// </summary>
        public string Identifier { get; }

        /// <summary>
        /// The identifier as a number, or -1 when it is not numeric.
        /// </summary>
        public int Code { get; }

        /// <summary>
        /// Everything after the first ';', or empty when the sequence carried no parameters.
        /// </summary>
        public string Data { get; }

        /// <summary>
        /// The entire payload, identifier included, exactly as the parser delivered it.
        /// </summary>
        public string Raw { get; }

        /// <summary>
        /// Whether the terminal dispatched this sequence itself. False means it was ignored, and a
        /// handler is the only thing that will act on it.
        /// </summary>
        public bool Recognized { get; }
    }

    /// <summary>
    /// Hyperlink event - fired when a hyperlink is encountered or cleared.
    /// </summary>
    public class HyperlinkEventArgs : EventArgs
    {
        /// <summary>
        /// Hyperlink URL. Empty when <see cref="IsCleared"/> is true.
        /// </summary>
        public string Url { get; }

        /// <summary>
        /// True when the active hyperlink was cleared.
        /// </summary>
        public bool IsCleared { get; }
        
        public HyperlinkEventArgs(string url)
            : this(url, false)
        {
        }

        internal HyperlinkEventArgs(string url, bool isCleared)
        {
            Url = url;
            IsCleared = isCleared;
        }
    }

    /// <summary>
    /// Window moved event - fired when a window move command is received.
    /// </summary>
    public class WindowMovedEventArgs : EventArgs
    {
        // coord in pixels
        public int X { get; }
        public int Y { get; }
        
        public WindowMovedEventArgs(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    /// <summary>
    /// Window resized event - fired when a window resize command is received.
    /// </summary>
    public class WindowResizedEventArgs : EventArgs
    {
        // width in pixels
        public int Width { get; }
        
        // height in pixels
        public int Height { get; }
        
        public WindowResizedEventArgs(int width, int height)
        {
            Width = width;
            Height = height;
        }
    }

    /// <summary>
    /// Window info requested event - fired when window information is requested.
    /// The handler should set the appropriate response properties and the terminal
    /// will automatically send the response.
    /// </summary>
    public class WindowInfoRequestedEventArgs : EventArgs
    {
        public WindowInfoRequest Request { get; }
        
        /// <summary>
        /// Set to true if the request was handled and a response should be sent.
        /// </summary>
        public bool Handled { get; set; }
        
        /// <summary>
        /// For State request: true if window is iconified (minimized), false otherwise.
        /// </summary>
        public bool IsIconified { get; set; }
        
        /// <summary>
        /// For Position request: X coordinate of window position in pixels.
        /// </summary>
        public int X { get; set; }
        
        /// <summary>
        /// For Position request: Y coordinate of window position in pixels.
        /// </summary>
        public int Y { get; set; }
        
        /// <summary>
        /// For SizePixels/ScreenSizePixels request: Width in pixels.
        /// </summary>
        public int WidthPixels { get; set; }
        
        /// <summary>
        /// For SizePixels/ScreenSizePixels request: Height in pixels.
        /// </summary>
        public int HeightPixels { get; set; }
        
        /// <summary>
        /// For CellSizePixels request: Cell width in pixels.
        /// </summary>
        public int CellWidth { get; set; }
        
        /// <summary>
        /// For CellSizePixels request: Cell height in pixels.
        /// </summary>
        public int CellHeight { get; set; }
        
        /// <summary>
        /// For Title/IconTitle request: The title string.
        /// </summary>
        public string? Title { get; set; }
        
        public WindowInfoRequestedEventArgs(WindowInfoRequest request)
        {
            Request = request;
        }
    }

    /// <summary>
    /// Buffer change event - fired when the active buffer switches.
    /// </summary>
    public class BufferChangedEventArgs : EventArgs
    {
        public XTerm.Common.BufferType Buffer { get; }

        public BufferChangedEventArgs(XTerm.Common.BufferType buffer)
        {
            Buffer = buffer;
        }
    }

    /// <summary>
    /// Cursor style changed event - fired when cursor style or blink setting changes.
    /// </summary>
    public class CursorStyleChangedEventArgs : EventArgs
    {
        public CursorStyle Style { get; }
        public bool Blink { get; }

        public CursorStyleChangedEventArgs(CursorStyle style, bool blink)
        {
            Style = style;
            Blink = blink;
        }
    }
}
