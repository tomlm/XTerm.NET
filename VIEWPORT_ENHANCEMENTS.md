# XTerm.NET Viewport Enhancements

## Summary
Added missing viewport properties and methods to XTerm.NET's `TerminalBuffer` class to match XTerm.js functionality, and simplified the TerminalView to bind directly to the terminal's viewport.

## Design Philosophy: Keep It Simple

The scrollbar binding is **dead simple**:

```
ViewportY        = The line number at the top of what you're viewing
MaxScrollback    = How far you can scroll (buffer length - viewport height)
ScrollBar.Value  = ViewportY
```

**That's it!** No complex math, no coordinate conversions.

### Why MaxScrollback?

`MaxScrollback` exists because the scrollbar needs to know its maximum value:

```csharp
// Scrollbar setup
scrollBar.Minimum = 0;                    // Top of buffer
scrollBar.Maximum = MaxScrollback;        // Can't scroll past this!
scrollBar.Value = ViewportY;              // Current position
scrollBar.ViewportSize = ViewportLines;   // How many lines visible
```

**Formula**: `MaxScrollback = TotalBufferLines - ViewportHeight`

**Example**:
- Buffer has 1000 lines
- Viewport shows 24 lines
- MaxScrollback = 1000 - 24 = 976
- You can scroll ViewportY from 0 (top) to 976 (bottom)

## New Properties Added to TerminalBuffer

### `ViewportY` (read/write)
```csharp
public int ViewportY { get; set; }
```
- **Purpose**: The absolute line index of the top of the viewport in the buffer
- **XTerm.js equivalent**: `ydisp`
- **Range**: 0 to MaxScrollback
- **Use case**: Direct binding to scrollbar value

### `BaseY` (read-only)
```csharp
public int BaseY { get; }
```
- **Purpose**: The absolute line index where new content is being written
- **XTerm.js equivalent**: `ybase`
- **Use case**: Determine the bottom of the active content

### `Length` (read-only)
```csharp
public int Length { get; }
```
- **Purpose**: Total number of lines in the buffer (scrollback + active lines)
- **Use case**: Calculate MaxScrollback

### `IsAtBottom` (read-only)
```csharp
public bool IsAtBottom { get; }
```
- **Purpose**: Whether the viewport is showing the latest content
- **Use case**: Determine if auto-scroll should occur

## New Methods Added to TerminalBuffer

### `ScrollToLine(int line)`
```csharp
public void ScrollToLine(int line)
```
- **Purpose**: Scroll the viewport to show a specific absolute line number
- **Parameters**: `line` - The absolute line number to scroll to
- **Behavior**: Automatically clamps to valid range

### `ScrollLines(int lines)`
```csharp
public void ScrollLines(int lines)
```
- **Purpose**: Scroll the viewport by a relative number of lines
- **Parameters**: `lines` - Number of lines to scroll (negative = up, positive = down)
- **Use case**: Implement PageUp/PageDown or mouse wheel scrolling

### `ScrollToBottom()` (enhanced)
```csharp
public void ScrollToBottom()
```
- **Purpose**: Scroll to show the latest content
- **Implementation**: Sets `ViewportY = BaseY - Rows`

### `ScrollToTop()`
```csharp
public void ScrollToTop()
```
- **Purpose**: Scroll viewport to the oldest line in the buffer
- **Implementation**: Sets `ViewportY = 0`

## Integration with TerminalView

The `TerminalView` exposes three simple properties:

1. **`ViewportY`** - Current scroll position (binds to scrollbar value)
2. **`MaxScrollback`** - Maximum scroll position (binds to scrollbar maximum)
3. **`ViewportLines`** - How many lines are visible (binds to scrollbar viewport size)

## Simple Scrollbar Binding

```csharp
// In TerminalControl.UpdateScrollBar()
_scrollBar.Minimum = 0;
_scrollBar.Maximum = _terminalView.MaxScrollback;      // Total lines - viewport
_scrollBar.ViewportSize = _terminalView.ViewportLines; // How many visible
_scrollBar.Value = _terminalView.ViewportY;            // Current position
_scrollBar.IsVisible = MaxScrollback > 0;              // Hide if no scrollback
```

## Backward Compatibility

The following legacy properties are maintained for backward compatibility:
- `YDisp` ? maps to `ViewportY`
- `YBase` ? maps to `BaseY`

## Usage Examples

### Scroll to bottom when new content arrives
```csharp
_terminal.Buffer.ScrollToBottom();
```

### Scroll up by one page
```csharp
_terminal.Buffer.ScrollLines(-_terminal.Rows);
```

### Check if at bottom before auto-scrolling
```csharp
if (_terminal.Buffer.IsAtBottom)
{
    _terminal.Buffer.ScrollToBottom();
}
```

### Get current viewport position
```csharp
int topVisibleLine = _terminal.Buffer.ViewportY;
```

## Benefits

1. **Simple & Clear**: One property = one meaning
2. **API Parity with XTerm.js**: Familiar to developers coming from XTerm.js
3. **Direct Binding**: Scrollbar value = ViewportY (no conversion)
4. **Automatic Clamping**: All scroll operations stay within valid bounds
5. **No Magic Math**: MaxScrollback = TotalLines - ViewportLines. That's it!
