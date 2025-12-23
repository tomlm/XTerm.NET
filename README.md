# XTerm.NET

A .NET terminal emulator library inspired by [xterm.js](https://github.com/xtermjs/xterm.js). XTerm.NET provides a headless terminal emulator that parses and processes VT100/ANSI escape sequences, making it easy to build terminal applications or integrate terminal functionality into your .NET applications.

## Features

- **Full VT100/ANSI Escape Sequence Support** — Process colors, cursor movement, text attributes, and more
- **Headless Design** — No UI dependencies; bring your own renderer (Console, WPF, MAUI, etc.)
- **Dual Buffer Support** — Normal and alternate screen buffers with scrollback
- **Keyboard & Mouse Input Generation** — Generate escape sequences for keyboard and mouse events
- **Rich Event System** — Subscribe to terminal events like title changes, bell, resize, and window manipulation
- **256 and True Color Support** — Full RGB and 256-color palette support
- **Unicode Support** — Proper handling of wide characters and Unicode text

## Installation

Install via NuGet Package Manager:

```shell
dotnet add package XTerm.NET
```

Or via the Package Manager Console in Visual Studio:

```powershell
Install-Package XTerm.NET
```

## Usage

### Basic Terminal Setup

```csharp
using XTerm;
using XTerm.Options;

// Create a terminal with custom options
var terminal = new Terminal(new TerminalOptions
{
    Cols = 80,
    Rows = 24,
    Scrollback = 1000
});

// Write text to the terminal
terminal.WriteLine("Hello, XTerm.NET!");
terminal.Write("This is inline text.");
```

### Working with Colors and Styles

XTerm.NET processes ANSI escape sequences for colors and text styling:

```csharp
// Standard colors
terminal.WriteLine("\x1b[31mRed text\x1b[0m");
terminal.WriteLine("\x1b[32mGreen text\x1b[0m");
terminal.WriteLine("\x1b[34mBlue text\x1b[0m");

// Bold and italic
terminal.WriteLine("\x1b[1mBold text\x1b[0m");
terminal.WriteLine("\x1b[3mItalic text\x1b[0m");
terminal.WriteLine("\x1b[1;32mBold green text\x1b[0m");

// Background colors
terminal.WriteLine("\x1b[41;37mWhite on red\x1b[0m");

// 256 colors
terminal.WriteLine("\x1b[38;5;208mOrange text (256 color)\x1b[0m");

// True color (RGB)
terminal.WriteLine("\x1b[38;2;255;100;200mPink text (RGB)\x1b[0m");
```

### Cursor Movement

```csharp
// Move cursor to specific position (row 5, column 20)
terminal.Write("\x1b[5;20HText at position");

// Draw a box
terminal.Write("\x1b[2;2H+----------+");
terminal.Write("\x1b[3;2H|  Box     |");
terminal.Write("\x1b[4;2H+----------+");
```

### Accessing the Buffer

```csharp
var buffer = terminal.Buffer;

// Get cursor position
Console.WriteLine($"Cursor: ({buffer.X}, {buffer.Y})");

// Access a specific line
var line = buffer.Lines[0];
string content = line?.TranslateToString(trimRight: true) ?? "";

// Examine cell attributes
var cell = line?[0];
if (cell.HasValue)
{
    var c = cell.Value;
    Console.WriteLine($"Character: '{c.Content}'");
    Console.WriteLine($"Bold: {c.Attributes.IsBold()}");
    Console.WriteLine($"Foreground: {c.Attributes.GetFgColor()}");
}
```

### Event Handling

```csharp
// Subscribe to terminal events
terminal.TitleChanged += (sender, e) =>
{
    Console.WriteLine($"Title changed to: {e.Title}");
};

terminal.BellRang += (sender, e) =>
{
    Console.WriteLine("Bell!");
};

terminal.LineFed += (sender, e) =>
{
    Console.WriteLine("Line feed occurred");
};

terminal.Resized += (sender, e) =>
{
    Console.WriteLine($"Terminal resized to {e.Cols}x{e.Rows}");
};

// Trigger title change via escape sequence
terminal.Write("\x1b]0;My Terminal Title\x07");
```

### Keyboard Input Generation

Generate escape sequences for keyboard input to send to a connected process:

```csharp
using XTerm.Input;

// Generate escape sequence for arrow keys
string upArrow = terminal.GenerateKeyInput(Key.UpArrow);
string downArrow = terminal.GenerateKeyInput(Key.DownArrow);

// With modifiers
string ctrlC = terminal.GenerateCharInput('c', KeyModifiers.Control);
string shiftTab = terminal.GenerateKeyInput(Key.Tab, KeyModifiers.Shift);

// Character input
string charSequence = terminal.GenerateCharInput('a', KeyModifiers.Alt);
```

### Mouse Input Generation

```csharp
using XTerm.Input;

// Generate mouse event escape sequences
string mouseClick = terminal.GenerateMouseEvent(
    MouseButton.Left,
    x: 10,
    y: 5,
    MouseEventType.Down,
    KeyModifiers.None
);

// Focus events
string focusIn = terminal.GenerateFocusEvent(focused: true);
string focusOut = terminal.GenerateFocusEvent(focused: false);
```

### Alternate Buffer

Applications like vim or less use the alternate buffer to preserve the main screen:

```csharp
// Write to normal buffer
terminal.WriteLine("Normal buffer content");

// Switch to alternate buffer
terminal.SwitchToAltBuffer();
terminal.WriteLine("Alternate buffer content");

// Switch back — normal buffer content is preserved
terminal.SwitchToNormalBuffer();
```

### Resizing the Terminal

```csharp
// Resize terminal to new dimensions
terminal.Resize(cols: 120, rows: 40);

// Handle resize event
terminal.Resized += (sender, e) =>
{
    Console.WriteLine($"New size: {e.Cols}x{e.Rows}");
};
```

### Window Manipulation Events

Handle window manipulation escape sequences (used by some terminal applications):

```csharp
terminal.WindowMoved += (sender, e) =>
{
    Console.WriteLine($"Move window to: ({e.X}, {e.Y})");
};

terminal.WindowResized += (sender, e) =>
{
    Console.WriteLine($"Resize to: {e.Width}x{e.Height}");
};

terminal.WindowMinimized += (sender, e) => Console.WriteLine("Minimize");
terminal.WindowMaximized += (sender, e) => Console.WriteLine("Maximize");
terminal.WindowRestored += (sender, e) => Console.WriteLine("Restore");
```

### Terminal Options

Customize terminal behavior with `TerminalOptions`:

```csharp
var options = new TerminalOptions
{
    Cols = 80,                    // Number of columns
    Rows = 24,                    // Number of rows
    Scrollback = 1000,            // Scrollback buffer lines
    CursorStyle = CursorStyle.Block,
    CursorBlink = true,
    ConvertEol = false,           // Convert LF to CRLF
    TermName = "xterm",           // Terminal type for identification
    Theme = new ThemeOptions
    {
        Foreground = "#ffffff",
        Background = "#000000",
        Cursor = "#ffffff"
    }
};

var terminal = new Terminal(options);
```

## Building a Renderer

XTerm.NET is headless and requires you to implement rendering. Here's a simple console renderer example:

```csharp
public class SimpleConsoleRenderer
{
    private readonly Terminal _terminal;

    public SimpleConsoleRenderer(Terminal terminal)
    {
        _terminal = terminal;
    }

    public void Render()
    {
        for (int row = 0; row < _terminal.Rows; row++)
        {
            var line = _terminal.Buffer.Lines[row];
            if (line != null)
            {
                Console.WriteLine(line.TranslateToString(trimRight: true));
            }
        }
    }
}
```

For more advanced rendering (WPF, MAUI, etc.), iterate through cells and read their attributes:

```csharp
for (int row = 0; row < terminal.Rows; row++)
{
    var line = terminal.Buffer.Lines[row];
    for (int col = 0; col < terminal.Cols; col++)
    {
        var cell = line?[col];
        if (cell.HasValue)
        {
            var c = cell.Value;
            // Access: c.Content, c.Attributes.IsBold(), c.Attributes.GetFgColor(), etc.
            // Render the cell with appropriate styling
        }
    }
}
```

## License

This project is licensed under the MIT License — see the [LICENSE](LICENSE) file for details.

## Author

Tom Laird-McConnell — [Iciclecreek](https://github.com/tomlm)

## Links

- [GitHub Repository](https://github.com/tomlm/XTerm.NET)
- [NuGet Package](https://www.nuget.org/packages/XTerm.NET)
