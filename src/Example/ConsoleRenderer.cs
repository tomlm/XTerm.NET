// See https://aka.ms/new-console-template for more information
using XTerm.Renderer;
using System.Text;

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
                var sb = new StringBuilder();
                XTerm.Buffer.AttributeData? previousAttributes = null;
                
                // Render each cell with its attributes
                for (int x = 0; x < line.Length && x < _terminal.Cols; x++)
                {
                    var cell = line[x];
                    
                    // Skip rendering invisible cells
                    if (cell.Attributes.IsInvisible())
                    {
                        sb.Append(' ');
                        continue;
                    }
                    
                    // Only emit ANSI codes if attributes changed
                    if (previousAttributes == null || !cell.Attributes.Equals(previousAttributes.Value))
                    {
                        BuildAnsiSequence(sb, cell.Attributes);
                        previousAttributes = cell.Attributes;
                    }
                    
                    // Write the character
                    var content = cell.Content;
                    if (string.IsNullOrEmpty(content) || cell.IsNull())
                    {
                        sb.Append(' ');
                    }
                    else
                    {
                        sb.Append(content);
                    }
                }
                
                // Reset attributes at end of line
                sb.Append("\x1b[0m");
                
                // Write the complete line to console
                Console.WriteLine(sb.ToString());
            }
        }
    }

    private void BuildAnsiSequence(StringBuilder sb, XTerm.Buffer.AttributeData attributes)
    {
        var sequences = new List<string>();
        
        // Get foreground and background colors
        var fgColor = attributes.GetFgColor();
        var bgColor = attributes.GetBgColor();
        var fgMode = attributes.GetFgColorMode();
        var bgMode = attributes.GetBgColorMode();
        
        // Handle inverse attribute (swap fg/bg)
        bool isInverse = attributes.IsInverse();
        if (isInverse)
        {
            (fgColor, bgColor) = (bgColor, fgColor);
            (fgMode, bgMode) = (bgMode, fgMode);
        }
        
        // Text attributes
        if (attributes.IsBold())
            sequences.Add("1");
        
        if (attributes.IsDim())
            sequences.Add("2");
        
        if (attributes.IsItalic())
            sequences.Add("3");
        
        if (attributes.IsUnderline())
            sequences.Add("4");
        
        if (attributes.IsBlink())
            sequences.Add("5");
        
        if (attributes.IsInverse())
            sequences.Add("7");
        
        if (attributes.IsInvisible())
            sequences.Add("8");
        
        if (attributes.IsStrikethrough())
            sequences.Add("9");
        
        // Foreground color
        if (fgColor != 256) // 256 is default
        {
            sequences.Add(GetForegroundColorSequence(fgColor, fgMode));
        }
        else
        {
            sequences.Add("39"); // Default foreground
        }
        
        // Background color
        if (bgColor != 257) // 257 is default
        {
            sequences.Add(GetBackgroundColorSequence(bgColor, bgMode));
        }
        else
        {
            sequences.Add("49"); // Default background
        }
        
        // Build the complete escape sequence
        if (sequences.Count > 0)
        {
            sb.Append("\x1b[");
            sb.Append(string.Join(";", sequences));
            sb.Append('m');
        }
    }

    private string GetForegroundColorSequence(int color, int mode)
    {
        if (mode == 1)
        {
            // RGB mode - use true color
            var r = (color >> 16) & 0xFF;
            var g = (color >> 8) & 0xFF;
            var b = color & 0xFF;
            return $"38;2;{r};{g};{b}";
        }
        else
        {
            // 256 color palette mode
            if (color < 8)
            {
                // Standard colors (30-37)
                return (30 + color).ToString();
            }
            else if (color < 16)
            {
                // Bright colors (90-97)
                return (90 + (color - 8)).ToString();
            }
            else
            {
                // 256 color palette (38;5;n)
                return $"38;5;{color}";
            }
        }
    }

    private string GetBackgroundColorSequence(int color, int mode)
    {
        if (mode == 1)
        {
            // RGB mode - use true color
            var r = (color >> 16) & 0xFF;
            var g = (color >> 8) & 0xFF;
            var b = color & 0xFF;
            return $"48;2;{r};{g};{b}";
        }
        else
        {
            // 256 color palette mode
            if (color < 8)
            {
                // Standard colors (40-47)
                return (40 + color).ToString();
            }
            else if (color < 16)
            {
                // Bright colors (100-107)
                return (100 + (color - 8)).ToString();
            }
            else
            {
                // 256 color palette (48;5;n)
                return $"48;5;{color}";
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

    public void Resize(int cols, int rows) { }
    public void HandleDevicePixelRatioChange() { }
    public void Clear() => Console.Clear();
    public void RegisterCharacterAtlas(ICharAtlas atlas) { }
    public void HandleColorChange() { }
    public void HandleOptionsChange() { }
    public void Dispose() { }
}
