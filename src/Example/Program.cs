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
        Console.ReadKey();
        // Example 2: Colored output
        ColoredOutputExample();
        Console.ReadKey();

        // Example 3: Cursor movement
        CursorMovementExample();
        Console.ReadKey();

        // Example 4: Buffer access
        BufferAccessExample();
        Console.ReadKey();

        // Example 5: Event handling
        EventHandlingExample();
        Console.ReadKey();
    }

    static void BasicTerminalExample()
    {
        Console.WriteLine("=== Basic Terminal Example ===\n");

        var terminal = new Terminal(new TerminalOptions
        {
            Cols = 80,
            Rows = 24,
            Scrollback = 1000,
            ConvertEol = true  // Enable EOL conversion so \n behaves like \r\n
        });

        var renderer = new ConsoleRenderer(terminal);

        terminal.Write("Hello, XTerm.NET!\n");
        terminal.Write("This is a terminal emulator.\n");
        terminal.Write("It supports VT100/ANSI escape sequences.\n");

        RenderTerminal(terminal, renderer);
    }

    static void ColoredOutputExample()
    {
        Console.WriteLine("\n=== Colored Output Example ===\n");

        var terminal = new Terminal(new TerminalOptions { ConvertEol = true });
        var renderer = new ConsoleRenderer(terminal);

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

        RenderTerminal(terminal, renderer);
    }

    static void CursorMovementExample()
    {
        Console.WriteLine("\n=== Cursor Movement Example ===\n");

        var terminal = new Terminal(new TerminalOptions { Cols = 40, Rows = 10, ConvertEol = true });
        var renderer = new ConsoleRenderer(terminal);

        // Write at different positions
        terminal.Write("Top left");
        terminal.Write("\x1b[5;20H"); // Move to row 5, col 20
        terminal.Write("Middle");
        terminal.Write("\x1b[10;1H"); // Move to row 10, col 1
        terminal.Write("Bottom left");

        // Draw a box
        terminal.Write("\x1b[2;2H+----------+");
        terminal.Write("\x1b[3;2H|  Box     |");
        terminal.Write("\x1b[4;2H+----------+");

        RenderTerminal(terminal, renderer);
    }

    static void BufferAccessExample()
    {
        Console.WriteLine("\n=== Buffer Access Example ===\n");

        var terminal = new Terminal(new TerminalOptions { Cols = 30, Rows = 5, ConvertEol = true });
        var renderer = new ConsoleRenderer(terminal);

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

        RenderTerminal(terminal, renderer);
    }

    static void EventHandlingExample()
    {
        Console.WriteLine("\n=== Event Handling Example ===\n");

        var terminal = new Terminal(new TerminalOptions { ConvertEol = true });
        var renderer = new ConsoleRenderer(terminal);

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

        RenderTerminal(terminal, renderer);
    }

    static void RenderTerminal(Terminal terminal, ConsoleRenderer renderer)
    {
        Console.WriteLine("\nTerminal output:");
        Console.WriteLine(new string('-', terminal.Cols));

        // Use the ConsoleRenderer to render the terminal content
        renderer.Render(0, terminal.Rows);

        Console.WriteLine(new string('-', terminal.Cols));
        Console.WriteLine();
    }
}
