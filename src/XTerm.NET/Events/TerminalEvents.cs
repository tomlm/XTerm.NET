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
    public class DataEventArgs
    {
        public string Data { get; set; } = string.Empty;
    }

    /// <summary>
    /// Resize event - fired when the terminal is resized.
    /// </summary>
    public class ResizeEventArgs
    {
        public int Cols { get; set; }
        public int Rows { get; set; }
    }

    /// <summary>
    /// Title change event - fired when the terminal title changes.
    /// </summary>
    public class TitleChangeEventArgs
    {
        public string Title { get; set; } = string.Empty;
    }

    /// <summary>
    /// Cursor move event - fired when the cursor position changes.
    /// </summary>
    public class CursorMoveEventArgs
    {
        public int X { get; set; }
        public int Y { get; set; }
    }

    /// <summary>
    /// Line feed event - fired when a line feed occurs.
    /// </summary>
    public class LineFeedEventArgs
    {
        public string Data { get; set; } = string.Empty;
    }

    /// <summary>
    /// Scroll event - fired when the terminal scrolls.
    /// </summary>
    public class ScrollEventArgs
    {
        public int YDisp { get; set; }
        public int YBase { get; set; }
    }

    /// <summary>
    /// Selection change event - fired when the selection changes.
    /// </summary>
    public class SelectionChangeEventArgs
    {
        public string SelectedText { get; set; } = string.Empty;
    }

    /// <summary>
    /// Render event - fired before/after rendering.
    /// </summary>
    public class RenderEventArgs
    {
        public int StartRow { get; set; }
        public int EndRow { get; set; }
    }
}

