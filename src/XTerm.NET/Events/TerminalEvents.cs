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
    /// Hyperlink event - fired when a hyperlink is encountered.
    /// </summary>
    public class HyperlinkEventArgs : EventArgs
    {
        public string Url { get; }
        
        public HyperlinkEventArgs(string url)
        {
            Url = url;
        }
    }

    /// <summary>
    /// Window moved event - fired when a window move command is received.
    /// </summary>
    public class WindowMovedEventArgs : EventArgs
    {
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
        public int Width { get; }
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

