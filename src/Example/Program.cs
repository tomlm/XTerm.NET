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
        
        // Example 6: Window manipulation events
        WindowManipulationExample();
        Console.ReadKey();
    }

    static void BasicTerminalExample()
    {
        Console.WriteLine("=== Basic Terminal Example ===\r\n");

        var terminal = new Terminal(new TerminalOptions
        {
            Cols = 80,
            Rows = 24,
            Scrollback = 1000,
             // Enable EOL conversion so \r\n behaves like \r\r\n
        });

        var renderer = new ConsoleRenderer(terminal);

        terminal.Write("Hello, XTerm.NET!\r\n");
        terminal.Write("This is a terminal emulator.\r\n");
        terminal.Write("It supports VT100/ANSI escape sequences.\r\n");

        RenderTerminal(terminal, renderer);
    }

    static void ColoredOutputExample()
    {
        Console.WriteLine("\r\n=== Colored Output Example ===\r\n");

        var terminal = new Terminal(new TerminalOptions { });
        var renderer = new ConsoleRenderer(terminal);

        // Standard colors
        terminal.Write("\x1b[31mRed text\x1b[0m\r\n");
        terminal.Write("\x1b[32mGreen text\x1b[0m\r\n");
        terminal.Write("\x1b[34mBlue text\x1b[0m\r\n");

        // Bold and italic
        terminal.Write("\x1b[1mBold text\x1b[0m\r\n");
        terminal.Write("\x1b[3mItalic text\x1b[0m\r\n");
        terminal.Write("\x1b[1;32mBold green text\x1b[0m\r\n");

        // Background colors
        terminal.Write("\x1b[41;37mWhite on red\x1b[0m\r\n");
        terminal.Write("\x1b[44;33mYellow on blue\x1b[0m\r\n");

        // 256 colors
        terminal.Write("\x1b[38;5;208mOrange text (256 color)\x1b[0m\r\n");

        // True color (RGB)
        terminal.Write("\x1b[38;2;255;100;200mPink text (RGB)\x1b[0m\r\n");

        RenderTerminal(terminal, renderer);
    }

    static void CursorMovementExample()
    {
        Console.WriteLine("\r\n=== Cursor Movement Example ===\r\n");

        var terminal = new Terminal(new TerminalOptions { Cols = 40, Rows = 10, });
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
        Console.WriteLine("\r\n=== Buffer Access Example ===\r\n");

        var terminal = new Terminal(new TerminalOptions { Cols = 30, Rows = 5, });
        var renderer = new ConsoleRenderer(terminal);

        terminal.Write("Line 1\r\n");
        terminal.Write("\x1b[1;31mRed Line 2\x1b[0m\r\n");
        terminal.Write("Line 3\r\n");

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

    static void WindowManipulationExample()
    {
        Console.WriteLine("\n=== Window Manipulation Example ===\n");

        var terminal = new Terminal(new TerminalOptions
        {
            ConvertEol = true,
            WindowOptions = new WindowOptions
            {
                // Enable window manipulation permissions
                SetWinPosition = true,
                SetWinSizePixels = true,
                SetWinSizeChars = true,
                GetWinTitle = true,
                GetWinSizeChars = true,
                MinimizeWin = true,
                MaximizeWin = true,
                RestoreWin = true,
                RaiseWin = true,
                LowerWin = true
            }
        });

        var renderer = new ConsoleRenderer(terminal);

        // Subscribe to window manipulation events
        terminal.OnWindowMove.Event(pos =>
        {
            Console.WriteLine($"[WINDOW EVENT] Move window to: ({pos.x}, {pos.y})");
        });

        terminal.OnWindowResize.Event(size =>
        {
            Console.WriteLine($"[WINDOW EVENT] Resize window to: {size.width}x{size.height} pixels");
        });

        terminal.OnWindowMinimize.Event(() =>
        {
            Console.WriteLine("[WINDOW EVENT] Minimize window");
        });

        terminal.OnWindowMaximize.Event(() =>
        {
            Console.WriteLine("[WINDOW EVENT] Maximize window");
        });

        terminal.OnWindowRestore.Event(() =>
        {
            Console.WriteLine("[WINDOW EVENT] Restore window");
        });

        terminal.OnWindowRaise.Event(() =>
        {
            Console.WriteLine("[WINDOW EVENT] Raise window to front");
        });

        terminal.OnWindowLower.Event(() =>
        {
            Console.WriteLine("[WINDOW EVENT] Lower window to back");
        });

        terminal.OnWindowInfoRequest.Event(request =>
        {
            Console.WriteLine($"[WINDOW EVENT] Information requested: {request}");
        });

        // Send window manipulation commands
        terminal.Write("Sending window manipulation commands...\n");
        terminal.Write("\x1b[3;100;200t"); // Move window to (100, 200)
        terminal.Write("\x1b[4;600;800t"); // Resize window to 800x600 pixels
        terminal.Write("\x1b[2t");         // Minimize window
        terminal.Write("\x1b[9;1t");       // Maximize window
        terminal.Write("\x1b[9;0t");       // Restore window
        terminal.Write("\x1b[5t");         // Raise window
        terminal.Write("\x1b[6t");         // Lower window
        terminal.Write("\x1b[21t");        // Query window title
        terminal.Write("\x1b[18t");        // Query text area size

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
