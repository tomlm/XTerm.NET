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

/// <summary>
/// Event lifecycle manager for terminal events.
/// </summary>
public class EventManager
{
    private readonly Dictionary<string, List<Delegate>> _eventHandlers;

    public EventManager()
    {
        _eventHandlers = new Dictionary<string, List<Delegate>>();
    }

    /// <summary>
    /// Registers an event handler.
    /// </summary>
    public void On(string eventName, Delegate handler)
    {
        if (!_eventHandlers.ContainsKey(eventName))
        {
            _eventHandlers[eventName] = new List<Delegate>();
        }
        _eventHandlers[eventName].Add(handler);
    }

    /// <summary>
    /// Unregisters an event handler.
    /// </summary>
    public void Off(string eventName, Delegate handler)
    {
        if (_eventHandlers.TryGetValue(eventName, out var handlers))
        {
            handlers.Remove(handler);
        }
    }

    /// <summary>
    /// Fires an event.
    /// </summary>
    public void Emit(string eventName, params object[] args)
    {
        if (_eventHandlers.TryGetValue(eventName, out var handlers))
        {
            foreach (var handler in handlers.ToList())
            {
                handler.DynamicInvoke(args);
            }
        }
    }

    /// <summary>
    /// Clears all event handlers.
    /// </summary>
    public void Clear()
    {
        _eventHandlers.Clear();
    }

    /// <summary>
    /// Clears event handlers for a specific event.
    /// </summary>
    public void Clear(string eventName)
    {
        if (_eventHandlers.ContainsKey(eventName))
        {
            _eventHandlers[eventName].Clear();
        }
    }
}
