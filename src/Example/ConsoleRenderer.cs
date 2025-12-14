// See https://aka.ms/new-console-template for more information
using XTerm.Renderer;

namespace XTerm.Examples;

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
                // Render each cell with its attributes
                for (int x = 0; x < line.Length && x < _terminal.Cols; x++)
                {
                    var cell = line[x];
                    
                    // Skip rendering invisible cells
                    if (cell.Attributes.IsInvisible())
                        continue;
                    
                    // Apply attributes to console
                    ApplyAttributes(cell.Attributes);
                    
                    // Write the character
                    var content = cell.Content;
                    if (string.IsNullOrEmpty(content) || cell.IsNull())
                    {
                        Console.Write(' ');
                    }
                    else
                    {
                        Console.Write(content);
                    }
                    
                    // Reset attributes after each cell to avoid carryover
                    Console.ResetColor();
                }
                
                Console.WriteLine(); // Move to next line
            }
        }
    }

    private void ApplyAttributes(XTerm.Buffer.AttributeData attributes)
    {
        // Get foreground and background colors
        var fgColor = attributes.GetFgColor();
        var bgColor = attributes.GetBgColor();
        var fgMode = attributes.GetFgColorMode();
        var bgMode = attributes.GetBgColorMode();
        
        // Handle inverse attribute (swap fg/bg)
        if (attributes.IsInverse())
        {
            (fgColor, bgColor) = (bgColor, fgColor);
            (fgMode, bgMode) = (bgMode, fgMode);
        }
        
        // Set foreground color
        if (fgColor != 256) // 256 is default
        {
            Console.ForegroundColor = MapToConsoleColor(fgColor, fgMode, attributes.IsBold());
        }
        
        // Set background color
        if (bgColor != 257) // 257 is default
        {
            Console.BackgroundColor = MapToConsoleColor(bgColor, bgMode, false);
        }
    }

    private ConsoleColor MapToConsoleColor(int color, int mode, bool isBold)
    {
        // Mode 0 = 256 color palette, Mode 1 = RGB
        if (mode == 1)
        {
            // RGB mode - convert to nearest console color
            var r = (color >> 16) & 0xFF;
            var g = (color >> 8) & 0xFF;
            var b = color & 0xFF;
            return MapRgbToConsoleColor(r, g, b);
        }
        else
        {
            // 256 color palette mode
            if (color < 8)
            {
                // Standard colors (0-7)
                return color switch
                {
                    0 => ConsoleColor.Black,
                    1 => ConsoleColor.DarkRed,
                    2 => ConsoleColor.DarkGreen,
                    3 => ConsoleColor.DarkYellow,
                    4 => ConsoleColor.DarkBlue,
                    5 => ConsoleColor.DarkMagenta,
                    6 => ConsoleColor.DarkCyan,
                    7 => ConsoleColor.Gray,
                    _ => ConsoleColor.Gray
                };
            }
            else if (color < 16)
            {
                // Bright colors (8-15)
                return color switch
                {
                    8 => ConsoleColor.DarkGray,
                    9 => ConsoleColor.Red,
                    10 => ConsoleColor.Green,
                    11 => ConsoleColor.Yellow,
                    12 => ConsoleColor.Blue,
                    13 => ConsoleColor.Magenta,
                    14 => ConsoleColor.Cyan,
                    15 => ConsoleColor.White,
                    _ => ConsoleColor.White
                };
            }
            else if (color < 232)
            {
                // 216 color cube (16-231)
                // Convert to RGB and then to console color
                var index = color - 16;
                var r = (index / 36) * 51;
                var g = ((index % 36) / 6) * 51;
                var b = (index % 6) * 51;
                return MapRgbToConsoleColor(r, g, b);
            }
            else if (color < 256)
            {
                // Grayscale (232-255)
                var gray = 8 + (color - 232) * 10;
                return MapRgbToConsoleColor(gray, gray, gray);
            }
            else
            {
                // Default colors
                return isBold ? ConsoleColor.White : ConsoleColor.Gray;
            }
        }
    }

    private ConsoleColor MapRgbToConsoleColor(int r, int g, int b)
    {
        // Simple RGB to console color mapping
        // Calculate luminance and hue to pick closest console color
        var luminance = (0.299 * r + 0.587 * g + 0.114 * b);
        
        // Dark colors
        if (luminance < 85)
        {
            if (r > g && r > b) return ConsoleColor.DarkRed;
            if (g > r && g > b) return ConsoleColor.DarkGreen;
            if (b > r && b > g) return ConsoleColor.DarkBlue;
            if (r > 128 && g > 128) return ConsoleColor.DarkYellow;
            if (r > 128 && b > 128) return ConsoleColor.DarkMagenta;
            if (g > 128 && b > 128) return ConsoleColor.DarkCyan;
            return ConsoleColor.Black;
        }
        // Medium colors
        else if (luminance < 170)
        {
            if (r > g && r > b) return ConsoleColor.DarkRed;
            if (g > r && g > b) return ConsoleColor.DarkGreen;
            if (b > r && b > g) return ConsoleColor.DarkBlue;
            if (r > 100 && g > 100 && b < 100) return ConsoleColor.DarkYellow;
            if (r > 100 && b > 100 && g < 100) return ConsoleColor.DarkMagenta;
            if (g > 100 && b > 100 && r < 100) return ConsoleColor.DarkCyan;
            return ConsoleColor.DarkGray;
        }
        // Bright colors
        else
        {
            if (r > 200 && g < 100 && b < 100) return ConsoleColor.Red;
            if (g > 200 && r < 100 && b < 100) return ConsoleColor.Green;
            if (b > 200 && r < 100 && g < 100) return ConsoleColor.Blue;
            if (r > 200 && g > 200 && b < 100) return ConsoleColor.Yellow;
            if (r > 200 && b > 200 && g < 100) return ConsoleColor.Magenta;
            if (g > 200 && b > 200 && r < 100) return ConsoleColor.Cyan;
            if (r > 200 && g > 200 && b > 200) return ConsoleColor.White;
            return ConsoleColor.Gray;
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
