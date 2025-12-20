using System.Diagnostics;

namespace XTerm.Input;

/// <summary>
/// Tracks mouse state and generates mouse event sequences.
/// </summary>
public class MouseTracker
{
    private readonly Terminal _terminal;
    private MouseButton _lastButton = MouseButton.None;
    private int _lastX = -1;
    private int _lastY = -1;
    private bool _isButtonDown = false;

    // Mouse modes
    public MouseTrackingMode TrackingMode { get; set; } = MouseTrackingMode.None;
    public MouseEncoding Encoding { get; set; } = MouseEncoding.Default;
    public bool FocusEvents { get; set; } = false;

    public MouseTracker(Terminal terminal)
    {
        _terminal = terminal;
    }

    /// <summary>
    /// Generates a mouse event sequence.
    /// </summary>
    public string GenerateMouseEvent(MouseButton button, int x, int y, MouseEventType eventType, KeyModifiers modifiers = KeyModifiers.None)
    {
        Debug.WriteLine($"button: {button}, x: {x}, y: {y}, eventType: {eventType}, modifiers: {modifiers}");
        // Check if this mode supports this event type
        if (!ShouldReportEvent(button, eventType))
            return string.Empty;

        // Update state
        UpdateState(button, x, y, eventType);

        // Generate sequence based on encoding
        return Encoding switch
        {
            MouseEncoding.SGR => GenerateSGRSequence(button, x, y, eventType, modifiers),
            MouseEncoding.URXVT => GenerateURXVTSequence(button, x, y, eventType, modifiers),
            MouseEncoding.Utf8 => GenerateUTF8Sequence(button, x, y, eventType, modifiers),
            _ => GenerateDefaultSequence(button, x, y, eventType, modifiers)
        };
    }

    /// <summary>
    /// Generates a focus event sequence.
    /// </summary>
    public string GenerateFocusEvent(bool focused)
    {
        if (!FocusEvents)
            return string.Empty;

        return focused ? "\u001b[I" : "\u001b[O";
    }

    private bool ShouldReportEvent(MouseButton button, MouseEventType eventType)
    {
        if (TrackingMode == MouseTrackingMode.None)
            return false;
        Debug.WriteLine($"TrackingMode: {TrackingMode}");
        return TrackingMode switch
        {
            MouseTrackingMode.X10 => eventType == MouseEventType.Down,
            MouseTrackingMode.VT200 => eventType == MouseEventType.Down || eventType == MouseEventType.Up || 
                                        eventType == MouseEventType.WheelUp || eventType == MouseEventType.WheelDown,
            MouseTrackingMode.ButtonEvent => eventType != MouseEventType.Move,
            MouseTrackingMode.AnyEvent => true,
            _ => false
        };
    }

    private void UpdateState(MouseButton button, int x, int y, MouseEventType eventType)
    {
        _lastX = x;
        _lastY = y;

        if (eventType == MouseEventType.Down)
        {
            _lastButton = button;
            _isButtonDown = true;
        }
        else if (eventType == MouseEventType.Up)
        {
            _isButtonDown = false;
        }
    }

    private string GenerateDefaultSequence(MouseButton button, int x, int y, MouseEventType eventType, KeyModifiers modifiers)
    {
        // X10/VT200 format: ESC [ M Cb Cx Cy
        // Where Cb, Cx, Cy are encoded as value + 32 (to make printable ASCII)
        
        int cb = EncodeButtonDefault(button, eventType, modifiers);
        int cx = x + 1 + 32; // 1-based + 32 offset
        int cy = y + 1 + 32; // 1-based + 32 offset

        // Clamp to valid range (32-255)
        cx = Math.Clamp(cx, 32, 255);
        cy = Math.Clamp(cy, 32, 255);

        return $"\u001b[M{(char)cb}{(char)cx}{(char)cy}";
    }

    private string GenerateUTF8Sequence(MouseButton button, int x, int y, MouseEventType eventType, KeyModifiers modifiers)
    {
        // Similar to default but uses UTF-8 encoding for coordinates > 223
        int cb = EncodeButtonDefault(button, eventType, modifiers);
        int cx = x + 1 + 32;
        int cy = y + 1 + 32;

        return $"\u001b[M{(char)cb}{EncodeUTF8Coord(cx)}{EncodeUTF8Coord(cy)}";
    }

    private string GenerateSGRSequence(MouseButton button, int x, int y, MouseEventType eventType, KeyModifiers modifiers)
    {
        // SGR format: ESC [ < Cb ; Cx ; Cy M/m
        // M for button press, m for button release
        // No encoding offset, coordinates are decimal numbers (1-based)

        int cb = EncodeButtonSGR(button, eventType, modifiers);
        int cx = x + 1; // 1-based
        int cy = y + 1; // 1-based

        char terminator = (eventType == MouseEventType.Up) ? 'm' : 'M';

        return $"\u001b[<{cb};{cx};{cy}{terminator}";
    }

    private string GenerateURXVTSequence(MouseButton button, int x, int y, MouseEventType eventType, KeyModifiers modifiers)
    {
        // URXVT format: ESC [ Cb ; Cx ; Cy M
        int cb = EncodeButtonDefault(button, eventType, modifiers);
        int cx = x + 1; // 1-based
        int cy = y + 1; // 1-based

        return $"\u001b[{cb};{cx};{cy}M";
    }

    /// <summary>
    /// Encodes button for X10/VT200/UTF8/URXVT formats (includes +32 base).
    /// </summary>
    private int EncodeButtonDefault(MouseButton button, MouseEventType eventType, KeyModifiers modifiers)
    {
        int cb = 32; // Base value for X10/VT200

        // Button encoding
        if (button == MouseButton.WheelUp)
        {
            cb += 64;
        }
        else if (button == MouseButton.WheelDown)
        {
            cb += 65;
        }
        else if (eventType == MouseEventType.Move || eventType == MouseEventType.Drag)
        {
            // Motion events
            cb += 32; // Motion flag
            if (_isButtonDown)
            {
                cb += (int)_lastButton;
            }
            else
            {
                cb += 3; // No button (for move without button down)
            }
        }
        else if (eventType == MouseEventType.Up)
        {
            // Release - button 3 (no button info in X10/VT200)
            cb += 3;
        }
        else
        {
            // Button down
            cb += (int)button;
        }

        // Add modifier flags
        if ((modifiers & KeyModifiers.Shift) != 0) cb += 4;
        if ((modifiers & KeyModifiers.Alt) != 0) cb += 8;
        if ((modifiers & KeyModifiers.Control) != 0) cb += 16;

        return cb;
    }

    /// <summary>
    /// Encodes button for SGR format (no +32 base, preserves button info on release).
    /// </summary>
    private int EncodeButtonSGR(MouseButton button, MouseEventType eventType, KeyModifiers modifiers)
    {
        int cb = 0; // No base offset for SGR

        // Button encoding
        if (button == MouseButton.WheelUp)
        {
            cb = 64;
        }
        else if (button == MouseButton.WheelDown)
        {
            cb = 65;
        }
        else if (eventType == MouseEventType.Move || eventType == MouseEventType.Drag)
        {
            // Motion events - add motion flag (32)
            cb = 32;
            if (_isButtonDown && _lastButton != MouseButton.None)
            {
                cb += (int)_lastButton;
            }
            else
            {
                cb += 3; // No button pressed during move
            }
        }
        else if (eventType == MouseEventType.Up)
        {
            // SGR preserves button info on release (terminator 'm' indicates release)
            cb = (int)button;
        }
        else
        {
            // Button down
            cb = (int)button;
        }

        // Add modifier flags
        if ((modifiers & KeyModifiers.Shift) != 0) cb += 4;
        if ((modifiers & KeyModifiers.Alt) != 0) cb += 8;
        if ((modifiers & KeyModifiers.Control) != 0) cb += 16;

        return cb;
    }

    private string EncodeUTF8Coord(int value)
    {
        if (value < 128)
        {
            return ((char)value).ToString();
        }
        else
        {
            // UTF-8 encoding for values >= 128
            // This is simplified - proper UTF-8 encoding for coordinates
            return char.ConvertFromUtf32(value);
        }
    }

    /// <summary>
    /// Resets mouse tracking state.
    /// </summary>
    public void Reset()
    {
        TrackingMode = MouseTrackingMode.None;
        Encoding = MouseEncoding.Default;
        FocusEvents = false;
        _lastButton = MouseButton.None;
        _lastX = -1;
        _lastY = -1;
        _isButtonDown = false;
    }
}
