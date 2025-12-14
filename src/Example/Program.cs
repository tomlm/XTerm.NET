// See https://aka.ms/new-console-template for more information
using XTerm;
using XTerm.Options;
using XTerm.Renderer;

namespace XTerm.Examples;

/// <summary>
/// Example usage of XTerm.NET library.
/// </summary>
public class BasicExample
{
    public static void Main(string[] args)
    {
        // Example 1: Basic terminal usage
        BasicTerminalExample();

        // Example 2: Colored output
        ColoredOutputExample();

        // Example 3: Cursor movement
        CursorMovementExample();

        // Example 4: Buffer access
        BufferAccessExample();

        // Example 5: Event handling
        EventHandlingExample();
    }

    static void BasicTerminalExample()
    {
        Console.WriteLine("=== Basic Terminal Example ===\n");

        var terminal = new Terminal(new TerminalOptions
        {
            Cols = 80,
            Rows = 24,
            Scrollback = 1000
        });

        terminal.Write("Hello, XTerm.NET!\n");
        terminal.Write("This is a terminal emulator.\n");
        terminal.Write("It supports VT100/ANSI escape sequences.\n");

        PrintTerminalContent(terminal);
    }

    static void ColoredOutputExample()
    {
        Console.WriteLine("\n=== Colored Output Example ===\n");

        var terminal = new Terminal();

        // Standard colors
        terminal.Write("\x1b[31mRed text\x1b[0m\n");
        terminal.Write("\x1b[32mGreen text\x1b[0m\n");
        terminal.Write("\x1b[34mBlue text\x1b[0m\n");

        // Bold and italic
        terminal.Write("\x1b[1mBold text\x1b[0m\n");
        terminal.Write("\x1b[3mItalic text\x1b[0m\n");
        terminal.Write("\x1b[1;32mBold green text\x1b[0m\n");

        // Background colors
        terminal.Write("\x1b[41;37mWhite on red\x1b[0m\n");
        terminal.Write("\x1b[44;33mYellow on blue\x1b[0m\n");

        // 256 colors
        terminal.Write("\x1b[38;5;208mOrange text (256 color)\x1b[0m\n");

        // True color (RGB)
        terminal.Write("\x1b[38;2;255;100;200mPink text (RGB)\x1b[0m\n");

        PrintTerminalContent(terminal);
    }

    static void CursorMovementExample()
    {
        Console.WriteLine("\n=== Cursor Movement Example ===\n");

        var terminal = new Terminal(new TerminalOptions { Cols = 40, Rows = 10 });

        // Write at different positions
        terminal.Write("Top left");
        terminal.Write("\x1b[5;20H"); // Move to row 5, col 20
        terminal.Write("Middle");
        terminal.Write("\x1b[10;1H"); // Move to row 10, col 1
        terminal.Write("Bottom left");

        // Draw a box
        terminal.Write("\x1b[2;2H????????????");
        terminal.Write("\x1b[3;2H?  Box     ?");
        terminal.Write("\x1b[4;2H????????????");

        PrintTerminalContent(terminal);
    }

    static void BufferAccessExample()
    {
        Console.WriteLine("\n=== Buffer Access Example ===\n");

        var terminal = new Terminal(new TerminalOptions { Cols = 30, Rows = 5 });

        terminal.Write("Line 1\n");
        terminal.Write("\x1b[1;31mRed Line 2\x1b[0m\n");
        terminal.Write("Line 3\n");

        // Access buffer directly
        var buffer = terminal.Buffer;
        Console.WriteLine($"Buffer Y position: {buffer.Y}");
        Console.WriteLine($"Buffer X position: {buffer.X}");

        // Get a specific line
        var line = buffer.Lines[0];
        if (line != null)
        {
            Console.WriteLine($"Line 0 content: '{line.TranslateToString(true)}'");
            Console.WriteLine($"Line 0 length: {line.Length}");
        }

        // Examine a cell
        var cell = line?[0];
        if (cell.HasValue)
        {
            var c = cell.Value;
            Console.WriteLine($"Cell [0,0]: char='{c.Content}', width={c.Width}");
            Console.WriteLine($"  Bold: {c.Attributes.IsBold()}");
            Console.WriteLine($"  FG Color: {c.Attributes.GetFgColor()}");
        }

        PrintTerminalContent(terminal);
    }

    static void EventHandlingExample()
    {
        Console.WriteLine("\n=== Event Handling Example ===\n");

        var terminal = new Terminal();

        // Subscribe to events
        terminal.OnTitleChange.Event(title =>
        {
            Console.WriteLine($"[EVENT] Title changed to: {title}");
        });

        terminal.OnBell.Event(() =>
        {
            Console.WriteLine("[EVENT] Bell!");
        });

        terminal.OnLineFeed.Event(data =>
        {
            Console.WriteLine($"[EVENT] Line feed: {data}");
        });

        // Trigger events
        terminal.Write("\x1b]0;My Terminal Title\x07"); // Set title
        terminal.Write("Line 1\n"); // Trigger line feed
        terminal.Write("\x07"); // Bell
        terminal.Write("Line 2\n");

        PrintTerminalContent(terminal);
    }

    static void PrintTerminalContent(Terminal terminal)
    {
        Console.WriteLine("\nTerminal output:");
        Console.WriteLine(new string('-', terminal.Cols));

        var lines = terminal.GetVisibleLines();
        foreach (var line in lines)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                Console.WriteLine(line);
            }
        }

        Console.WriteLine(new string('-', terminal.Cols));
        Console.WriteLine();
    }
}

/// <summary>
/// Example of implementing a custom renderer.
/// </summary>
public class ConsoleRenderer : IRenderer
{
    private readonly Terminal _terminal;

    public RenderDimensions Dimensions { get; private set; }

    public ConsoleRenderer(Terminal terminal)
    {
        _terminal = terminal;
        Dimensions = new RenderDimensions
        {
            Scaled = new Renderer.Dimensions
            {
                CellWidth = 10,
                CellHeight = 20,
                CanvasWidth = terminal.Cols * 10,
                CanvasHeight = terminal.Rows * 20
            },
            Actual = new Renderer.Dimensions
            {
                CellWidth = 10,
                CellHeight = 20,
                CanvasWidth = terminal.Cols * 10,
                CanvasHeight = terminal.Rows * 20
            },
            DevicePixelRatio = 1.0
        };
    }

    public void Render(int start, int end)
    {
        Console.Clear();
        var buffer = _terminal.Buffer;

        for (int y = start; y < end && y < _terminal.Rows; y++)
        {
            var line = buffer.Lines[buffer.YDisp + y];
            if (line != null)
            {
                Console.WriteLine(line.TranslateToString(true));
            }
        }
    }

    public void RenderCursor(int x, int y, CursorRenderOptions options)
    {
        // Position cursor in console
        if (y < Console.WindowHeight && x < Console.WindowWidth)
        {
            Console.SetCursorPosition(x, y);
        }
    }

    public void OnResize(int cols, int rows) { }
    public void OnDevicePixelRatioChange() { }
    public void Clear() => Console.Clear();
    public void RegisterCharacterAtlas(ICharAtlas atlas) { }
    public void OnColorChange() { }
    public void OnOptionsChange() { }
    public void Dispose() { }
}
