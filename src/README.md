# XTerm.NET

A .NET port of xterm.js core functionality. This library provides terminal emulation with full VT100/xterm escape sequence parsing and buffer management.

## Features

? **Core Terminal Functionality**
- Full VT100/ANSI escape sequence parsing
- CSI (Control Sequence Introducer) sequences
- OSC (Operating System Command) sequences
- ESC sequences for cursor control
- DCS (Device Control String) support

? **Buffer Management**
- Circular buffer with scrollback support
- Normal and alternate screen buffers
- Efficient line wrapping and reflow
- Cell-based text storage with attributes

? **Text Attributes**
- Bold, dim, italic, underline
- Blink, inverse, invisible, strikethrough, overline
- 256-color palette support
- True color (24-bit RGB) support
- Extended color modes

? **Advanced Features**
- Unicode support with proper width handling
- CJK wide character support
- Combining character support
- VT100 graphics charset (line drawing)
- Text selection (normal, word, line modes)
- Viewport management with scrolling

? **Architecture**
- Platform-agnostic core parsing
- `IRenderer` interface for custom renderers
- Event-driven architecture
- Idiomatic .NET code with proper memory management

## Getting Started

### Basic Usage

```csharp
using XTerm.NET;
using XTerm.NET.Options;

// Create a terminal with default options
var terminal = new Terminal();

// Or create with custom options
var options = new TerminalOptions
{
    Cols = 80,
    Rows = 24,
    Scrollback = 1000
};
var terminal = new Terminal(options);

// Write data to the terminal
terminal.Write("Hello, World!\n");
terminal.Write("\x1b[1;32mGreen Bold Text\x1b[0m\n");

// Handle events
terminal.OnData.Event(data => 
{
    Console.WriteLine($"Terminal output: {data}");
});

terminal.OnTitleChange.Event(title =>
{
    Console.WriteLine($"Title changed to: {title}");
});

// Resize the terminal
terminal.Resize(100, 30);

// Get visible content
var lines = terminal.GetVisibleLines();
foreach (var line in lines)
{
    Console.WriteLine(line);
}
```

### Advanced Usage

```csharp
using XTerm.NET;
using XTerm.NET.Buffer;
using XTerm.NET.Renderer;

// Create terminal with custom renderer
var terminal = new Terminal(new TerminalOptions
{
    Cols = 80,
    Rows = 24
});

// Access buffer directly
var buffer = terminal.Buffer;
var line = buffer.Lines[0];
var cell = line?[0];

// Work with attributes
if (cell.HasValue)
{
    var attrs = cell.Value.Attributes;
    Console.WriteLine($"Bold: {attrs.IsBold()}");
    Console.WriteLine($"FG Color: {attrs.GetFgColor()}");
}

// Use alternate buffer
terminal.SwitchToAltBuffer();
terminal.Write("Content in alternate buffer");
terminal.SwitchToNormalBuffer();

// Scroll management
terminal.ScrollToTop();
terminal.ScrollLines(5);
terminal.ScrollToBottom();
```

## Architecture

### Core Components

- **Terminal**: Main terminal class coordinating all components
- **Buffer**: Manages terminal screen and scrollback buffer
- **EscapeSequenceParser**: State machine-based parser for escape sequences
- **InputHandler**: Handles parsed escape sequences and updates buffer
- **IRenderer**: Interface for platform-specific rendering implementations

### Buffer Structure

```
???????????????????????????????????
?      Scrollback Buffer          ?
?     (CircularList<Line>)        ?
???????????????????????????????????
?      Visible Screen Area        ?
?         (YDisp to Rows)         ?
???????????????????????????????????
?     Active Cursor Position      ?
?          (X, Y)                 ?
???????????????????????????????????
```

### Escape Sequence Processing

```
Raw Data ? Parser ? State Machine ? InputHandler ? Buffer Update
```

## Supported Escape Sequences

### Cursor Movement
- `CUU` - Cursor Up
- `CUD` - Cursor Down
- `CUF` - Cursor Forward
- `CUB` - Cursor Backward
- `CHA` - Cursor Horizontal Absolute
- `CUP` - Cursor Position
- `CNL` - Cursor Next Line
- `CPL` - Cursor Previous Line

### Display Manipulation
- `ED` - Erase in Display
- `EL` - Erase in Line
- `IL` - Insert Lines
- `DL` - Delete Lines
- `ICH` - Insert Characters
- `DCH` - Delete Characters
- `ECH` - Erase Characters
- `SU` - Scroll Up
- `SD` - Scroll Down

### Graphics Rendition (SGR)
- Text attributes (bold, dim, italic, underline, etc.)
- 16 standard colors
- 256-color palette
- 24-bit RGB colors

### Other Sequences
- `DECSTBM` - Set scroll region
- `DECSC` - Save cursor
- `DECRC` - Restore cursor
- `IND` - Index (scroll up)
- `RI` - Reverse Index (scroll down)
- `NEL` - Next Line

## Platform Rendering

This library provides the **core parsing and buffer management only**. To render the terminal, implement the `IRenderer` interface:

```csharp
public class MyCustomRenderer : IRenderer
{
    public RenderDimensions Dimensions { get; }
    
    public void Render(int start, int end)
    {
        // Render rows from start to end
        // Access buffer cells and draw them to your platform
    }
    
    public void RenderCursor(int x, int y, CursorRenderOptions options)
    {
        // Render the cursor at the specified position
    }
    
    // Implement other interface methods...
}
```

A `NullRenderer` is provided for headless/testing scenarios.

## Compatibility

- **Target Framework**: .NET 10.0
- **Language Features**: C# 13 with nullable reference types enabled
- **xterm.js Compatibility**: Core parsing and buffer logic is 100% functionally compatible

## Design Principles

1. **Idiomatic .NET**: Uses .NET conventions, naming, and patterns
2. **Memory Efficient**: Struct-based cells, proper buffer management
3. **Platform Agnostic**: Core logic has no platform dependencies
4. **Event-Driven**: Observable events for all terminal state changes
5. **Extensible**: Interface-based design for custom renderers and handlers

## Future Enhancements

The following features are not yet implemented but can be added:

- Mouse input support
- Clipboard integration
- Link detection and handling
- Reflow on resize
- Search functionality
- Performance optimizations for large scrollback

## License

This is a port of xterm.js core functionality. Please refer to the original xterm.js license.

## Contributing

Contributions are welcome! Areas for improvement:

- Additional escape sequence support
- Performance optimizations
- Unicode edge cases
- Platform-specific renderers
- Test coverage
