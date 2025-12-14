using XTerm.Buffer;
using XTerm.Common;

namespace XTerm.Renderer;

/// <summary>
/// Dimensions for rendering.
/// </summary>
public struct Dimensions
{
    public int CellWidth { get; set; }
    public int CellHeight { get; set; }
    public int CanvasWidth { get; set; }
    public int CanvasHeight { get; set; }
}

/// <summary>
/// Render dimensions including scaling.
/// </summary>
public struct RenderDimensions
{
    public Dimensions Scaled { get; set; }
    public Dimensions Actual { get; set; }
    public double DevicePixelRatio { get; set; }
}

/// <summary>
/// Range of rows to render.
/// </summary>
public struct RenderRowRange
{
    public int Start { get; set; }
    public int End { get; set; }
}

/// <summary>
/// Cell position in the buffer.
/// </summary>
public struct CellPosition
{
    public int X { get; set; }
    public int Y { get; set; }
}

/// <summary>
/// Color representation.
/// </summary>
public struct Color
{
    public byte R { get; set; }
    public byte G { get; set; }
    public byte B { get; set; }
    public byte A { get; set; }

    public Color(byte r, byte g, byte b, byte a = 255)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }

    public static Color FromRgb(int rgb)
    {
        return new Color(
            (byte)((rgb >> 16) & 0xFF),
            (byte)((rgb >> 8) & 0xFF),
            (byte)(rgb & 0xFF)
        );
    }

    public int ToRgb()
    {
        return (R << 16) | (G << 8) | B;
    }
}

/// <summary>
/// Cursor render options.
/// </summary>
public class CursorRenderOptions
{
    public CursorStyle Style { get; set; }
    public bool Blink { get; set; }
    public Color Color { get; set; }
}

/// <summary>
/// Main renderer interface that platform-specific renderers must implement.
/// This is the abstraction layer between core terminal logic and rendering.
/// </summary>
public interface IRenderer
{
    /// <summary>
    /// Gets the render dimensions.
    /// </summary>
    RenderDimensions Dimensions { get; }

    /// <summary>
    /// Called when the terminal is resized.
    /// </summary>
    void OnResize(int cols, int rows);

    /// <summary>
    /// Called when the device pixel ratio changes.
    /// </summary>
    void OnDevicePixelRatioChange();

    /// <summary>
    /// Renders the terminal content.
    /// </summary>
    /// <param name="start">Start row to render</param>
    /// <param name="end">End row to render</param>
    void Render(int start, int end);

    /// <summary>
    /// Clears the entire rendering surface.
    /// </summary>
    void Clear();

    /// <summary>
    /// Registers a character atlas for rendering text.
    /// </summary>
    void RegisterCharacterAtlas(ICharAtlas atlas);

    /// <summary>
    /// Called when colors change.
    /// </summary>
    void OnColorChange();

    /// <summary>
    /// Called when options change.
    /// </summary>
    void OnOptionsChange();

    /// <summary>
    /// Handles cursor rendering.
    /// </summary>
    void RenderCursor(int x, int y, CursorRenderOptions options);

    /// <summary>
    /// Disposes the renderer.
    /// </summary>
    void Dispose();
}

/// <summary>
/// Character atlas interface for efficient text rendering.
/// </summary>
public interface ICharAtlas
{
    /// <summary>
    /// Draws a character to the rendering context.
    /// </summary>
    void DrawChar(object context, string character, int x, int y, AttributeData attributes);

    /// <summary>
    /// Clears the atlas cache.
    /// </summary>
    void Clear();

    /// <summary>
    /// Warms up the atlas with commonly used characters.
    /// </summary>
    void WarmUp(IEnumerable<string> characters);
}

/// <summary>
/// Render layer interface for layered rendering.
/// </summary>
public interface IRenderLayer
{
    /// <summary>
    /// Resets the layer.
    /// </summary>
    void Reset();

    /// <summary>
    /// Called when the layer is resized.
    /// </summary>
    void OnResize(int cols, int rows);

    /// <summary>
    /// Renders the layer content.
    /// </summary>
    void Render(int startRow, int endRow);

    /// <summary>
    /// Called when cell dimensions change.
    /// </summary>
    void OnCellSizeChanged();
}

/// <summary>
/// Link provider interface for hyperlink detection and rendering.
/// </summary>
public interface ILinkProvider
{
    /// <summary>
    /// Provides links for a given line.
    /// </summary>
    IEnumerable<TerminalLink> ProvideLinks(int y);
}

/// <summary>
/// Represents a terminal link (hyperlink).
/// </summary>
public class TerminalLink
{
    public int StartX { get; set; }
    public int EndX { get; set; }
    public int Y { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? Url { get; set; }
    public Action? Activate { get; set; }
}

/// <summary>
/// Base class for render layers.
/// </summary>
public abstract class BaseRenderLayer : IRenderLayer
{
    protected Terminal Terminal { get; }
    protected Buffer.TerminalBuffer Buffer => Terminal.Buffer;
    protected int Cols => Terminal.Cols;
    protected int Rows => Terminal.Rows;

    protected BaseRenderLayer(Terminal terminal)
    {
        Terminal = terminal;
    }

    public abstract void Reset();
    public abstract void OnResize(int cols, int rows);
    public abstract void Render(int startRow, int endRow);
    public abstract void OnCellSizeChanged();
}

/// <summary>
/// Null renderer implementation that does nothing.
/// Useful for headless terminal usage.
/// </summary>
public class NullRenderer : IRenderer
{
    public RenderDimensions Dimensions => new RenderDimensions
    {
        Scaled = new Dimensions { CellWidth = 10, CellHeight = 20 },
        Actual = new Dimensions { CellWidth = 10, CellHeight = 20 },
        DevicePixelRatio = 1.0
    };

    public void OnResize(int cols, int rows) { }
    public void OnDevicePixelRatioChange() { }
    public void Render(int start, int end) { }
    public void Clear() { }
    public void RegisterCharacterAtlas(ICharAtlas atlas) { }
    public void OnColorChange() { }
    public void OnOptionsChange() { }
    public void RenderCursor(int x, int y, CursorRenderOptions options) { }
    public void Dispose() { }
}
