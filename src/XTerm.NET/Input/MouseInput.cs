namespace XTerm.NET.Input;

/// <summary>
/// Mouse buttons.
/// </summary>
public enum MouseButton
{
    None = -1,
    Left = 0,
    Middle = 1,
    Right = 2,
    WheelUp = 64,
    WheelDown = 65,
}

/// <summary>
/// Mouse event types.
/// </summary>
public enum MouseEventType
{
    Down,
    Up,
    Move,
    Drag,
    WheelUp,
    WheelDown,
}

/// <summary>
/// Mouse tracking mode.
/// </summary>
public enum MouseTrackingMode
{
    None = 0,
    X10 = 9,              // Click only (CSI ? 9 h)
    VT200 = 1000,         // Normal tracking (CSI ? 1000 h)
    ButtonEvent = 1002,   // Button events + drag (CSI ? 1002 h)
    AnyEvent = 1003,      // All events including motion (CSI ? 1003 h)
}

/// <summary>
/// Mouse encoding format.
/// </summary>
public enum MouseEncoding
{
    Default,    // X10/VT200 format (limited to 223 columns)
    Utf8,       // UTF-8 encoding (CSI ? 1005 h)
    SGR,        // SGR format (CSI ? 1006 h) - recommended
    URXVT,      // URXVT format (CSI ? 1015 h)
}
