// See https://aka.ms/new-console-template for more information
using XTerm;
using XTerm.Common;
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

        // Example 7: Alternate buffer usage
        AlternateBufferExample();
        Console.ReadKey();
    }

    static void BasicTerminalExample()
    {
        Console.WriteLine("=== Basic Terminal Example ===");

        var terminal = new Terminal(new TerminalOptions
        {
            Cols = 80,
            Rows = 24,
            Scrollback = 1000,
        });

        var renderer = new ConsoleRenderer(terminal);

        terminal.WriteLine("Hello, XTerm.NET!");
        terminal.WriteLine("This is a terminal emulator.");
        terminal.WriteLine("It supports VT100/ANSI escape sequences.");

        RenderTerminal(terminal, renderer);
    }

    static void ColoredOutputExample()
    {
        Console.WriteLine("=== Colored Output Example ===");

        var terminal = new Terminal(new TerminalOptions { });
        var renderer = new ConsoleRenderer(terminal);

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
        terminal.WriteLine("\x1b[44;33mYellow on blue\x1b[0m");

        // 256 colors
        terminal.WriteLine("\x1b[38;5;208mOrange text (256 color)\x1b[0m");

        // True color (RGB)
        terminal.WriteLine("\x1b[38;2;255;100;200mPink text (RGB)\x1b[0m");

        RenderTerminal(terminal, renderer);
    }

    static void CursorMovementExample()
    {
        Console.WriteLine("=== Cursor Movement Example ===");

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
        Console.WriteLine("=== Buffer Access Example ===");

        var terminal = new Terminal(new TerminalOptions { Cols = 30, Rows = 5, });
        var renderer = new ConsoleRenderer(terminal);

        terminal.WriteLine("Line 1");
        terminal.WriteLine("\x1b[1;31mRed Line 2\x1b[0m");
        terminal.WriteLine("Line 3");

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
        Console.WriteLine("=== Event Handling Example ===");

        var terminal = new Terminal(new TerminalOptions { ConvertEol = true });
        var renderer = new ConsoleRenderer(terminal);

        // Subscribe to events using EventHandler pattern
        terminal.TitleChanged += (sender, e) =>
        {
            Console.WriteLine($"[EVENT] Title changed to: {e.Title}");
        };

        terminal.BellRang += (sender, e) =>
        {
            Console.WriteLine("[EVENT] Bell!");
        };

        terminal.LineFed += (sender, e) =>
        {
            Console.WriteLine($"[EVENT] Line feed: {e.Data}");
        };

        // Trigger events
        terminal.Write("\x1b]0;My Terminal Title\x07"); // Set title
        terminal.WriteLine("Line 1"); // Trigger line feed
        terminal.Write("\x07"); // Bell
        terminal.WriteLine("Line 2");

        RenderTerminal(terminal, renderer);
    }

    static void WindowManipulationExample()
    {
        Console.WriteLine("=== Window Manipulation Example ===");

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

        // Subscribe to window manipulation events using EventHandler pattern
        terminal.WindowMoved += (sender, e) =>
        {
            Console.WriteLine($"[WINDOW EVENT] Move window to: ({e.X}, {e.Y})");
            Console.ReadKey();
        };

        terminal.WindowResized += (sender, e) =>
        {
            Console.WriteLine($"[WINDOW EVENT] Resize window to: {e.Width}x{e.Height} pixels");
            Console.ReadKey();
        };

        terminal.WindowMinimized += (sender, e) =>
        {
            Console.WriteLine("[WINDOW EVENT] Minimize window");
            Console.ReadKey();
        };

        terminal.WindowMaximized += (sender, e) =>
        {
            Console.WriteLine("[WINDOW EVENT] Maximize window");
            Console.ReadKey();
        };

        terminal.WindowRestored += (sender, e) =>
        {
            Console.WriteLine("[WINDOW EVENT] Restore window");
            Console.ReadKey();
        };

        terminal.WindowRaised += (sender, e) =>
        {
            Console.WriteLine("[WINDOW EVENT] Raise window to front");
            Console.ReadKey();
        };

        terminal.WindowLowered += (sender, e) =>
        {
            Console.WriteLine("[WINDOW EVENT] Lower window to back");
            Console.ReadKey();
        };

        terminal.WindowInfoRequested += (sender, e) =>
        {
            Console.WriteLine($"[WINDOW EVENT] Information requested: {e.Request}");
            switch (e.Request)
            {
                case WindowInfoRequest.Position:
                    // In a real app, get actual window position from UI framework
                    int x = 100; // Example: Window.Left
                    int y = 200; // Example: Window.Top

                    // In a real application, the UI framework would call this to send the response:
                    // terminal.RaiseDataReceived($"\u001b[3;{x};{y}t");
                    Console.WriteLine($"[RESPONSE] Window position: ({x}, {y})");
                    break;

                case WindowInfoRequest.SizePixels:
                    // In a real app, get actual window size
                    int width = 1024;  // Example: Window.Width
                    int height = 768;  // Example: Window.Height

                    // In a real application, the UI framework would call this to send the response:
                    // terminal.RaiseDataReceived($"\u001b[4;{height};{width}t");
                    Console.WriteLine($"[RESPONSE] Window size: {width}x{height}");
                    break;

                case WindowInfoRequest.ScreenSizePixels:
                    // In a real app, get screen size
                    int screenWidth = 1920;
                    int screenHeight = 1080;

                    // In a real application, the UI framework would call this to send the response:
                    // terminal.RaiseDataReceived($"\u001b[5;{screenHeight};{screenWidth}t");
                    Console.WriteLine($"[RESPONSE] Screen size: {screenWidth}x{screenHeight}");
                    break;

                case WindowInfoRequest.CellSizePixels:
                    // In a real app, get cell dimensions from renderer
                    int cellWidth = 10;
                    int cellHeight = 20;

                    // In a real application, the UI framework would call this to send the response:
                    // terminal.RaiseDataReceived($"\u001b[6;{cellHeight};{cellWidth}t");
                    Console.WriteLine($"[RESPONSE] Cell size: {cellWidth}x{cellHeight}");
                    break;

                case WindowInfoRequest.Title:
                    // Title query is handled automatically by InputHandler
                    // but you could override it here if needed
                    break;

                case WindowInfoRequest.SizeCharacters:
                    // Size in characters is handled automatically by InputHandler
                    // since terminal already knows Rows and Cols
                    break;
            }
            Console.ReadKey();
        };

        // Send window manipulation commands
        terminal.Write("Sending window manipulation commands...");
        RenderTerminal(terminal, renderer);

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

    static void AlternateBufferExample()
    {
        Console.WriteLine("=== Alternate Buffer Example ===");

        var terminal = new Terminal(new TerminalOptions
        {
            Cols = 40,
            Rows = 10,
            Scrollback = 50
        });

        var renderer = new ConsoleRenderer(terminal);

        // Normal buffer content
        terminal.WriteLine("Normal buffer line 1");
        terminal.WriteLine("Normal buffer line 2");
        terminal.WriteLine("Press any key to switch to the alternate buffer...");
        RenderTerminal(terminal, renderer);
        Console.ReadKey();

        // Switch to alternate buffer
        terminal.SwitchToAltBuffer();
        terminal.WriteLine("Alt buffer line 1");
        terminal.WriteLine("Alt buffer line 2");
        terminal.WriteLine("Alt buffer is separate from scrollback.");
        RenderTerminal(terminal, renderer);
        Console.ReadKey();

        // Switch back to normal buffer
        terminal.SwitchToNormalBuffer();
        terminal.WriteLine("Back on normal buffer; previous content is intact.");
        RenderTerminal(terminal, renderer);
    }

    static void RenderTerminal(Terminal terminal, ConsoleRenderer renderer)
    {
        Console.WriteLine("Terminal output:");
        Console.WriteLine(new string('-', terminal.Cols));

        // Use the ConsoleRenderer to render the terminal content
        renderer.Render(0, terminal.Rows);

        Console.WriteLine(new string('-', terminal.Cols));
        Console.WriteLine();
    }
}
