namespace XTerm.Graphics;

/// <summary>
/// Decodes a DECSIXEL payload into a <see cref="TerminalImage"/>, a chunk at a time.
/// </summary>
/// <remarks>
/// <para>Streaming rather than string-at-once, because a full-screen Sixel runs to hundreds of
/// kilobytes and the payload arrives in whatever pieces the pty hands over. Every piece of parser
/// state that a number or a control could straddle therefore lives in a field, not on the stack of
/// a loop.</para>
/// <para>Nothing here throws on malformed input. The payload is untrusted output from someone
/// else's process; a truncated image, a nonsense colour register or a repeat count of two billion
/// are all things that happen, and the answer to each is a null image rather than an exception
/// escaping into the parser.</para>
/// </remarks>
internal sealed class SixelDecoder
{
    /// <summary>Rows of pixels a single Sixel character covers.</summary>
    private const int BandHeight = 6;

    /// <summary>
    /// Ceiling on a run-length. Well past any real image, and low enough that the multiply
    /// against a column count cannot overflow.
    /// </summary>
    private const int MaxRepeat = 1 << 20;

    /// <summary>
    /// Ceiling on the pixel aspect ratio. A VT340 offered up to 5:1 through P1; anything past this
    /// is a payload asking for a picture made of very tall pixels, which is a way to spend memory.
    /// </summary>
    private const int MaxAspect = 16;

    /// <summary>What the parser is in the middle of reading.</summary>
    private enum Mode
    {
        /// <summary>Reading sixel data and control characters.</summary>
        Ground,
        /// <summary>Reading the parameters of a '#' colour introducer.</summary>
        Color,
        /// <summary>Reading the parameters of a '"' raster attribute.</summary>
        Raster,
        /// <summary>Reading the count of a '!' repeat introducer.</summary>
        Repeat
    }

    private readonly int _cellWidth;
    private readonly int _cellHeight;
    private readonly long _maxPixels;
    private readonly SixelPalette _palette;

    /// <summary>
    /// What an unset pixel becomes: transparent, or the terminal background. Background select 1
    /// means "leave the background alone", and a transparent pixel is how that reaches a host that
    /// has already painted the cell underneath.
    /// </summary>
    private readonly uint _fill;

    private byte[] _pixels = Array.Empty<byte>();
    private int _capWidth;
    private int _capHeight;

    private int _x;
    private int _bandTop;
    private int _maxX;
    private int _maxBandBottom = -1;

    private uint _color;
    private int _repeat = 1;
    private bool _failed;

    private int _rasterWidth;
    private int _rasterHeight;

    /// <summary>
    /// Pixel rows each sixel row covers: the raster attribute's pixel aspect ratio, Pan/Pad. One for
    /// a square pixel, which is what nearly every encoder asks for; VT DOOM asks for its scale, and
    /// scales horizontally with repeat counts to match.
    /// </summary>
    private int _aspect = 1;

    /// <summary>Pixel rows one band of six sixel rows covers.</summary>
    private int BandPixels => BandHeight * _aspect;

    private Mode _mode = Mode.Ground;
    private readonly int[] _params = new int[8];
    private int _paramCount;
    private int _param;

    /// <param name="p1">Pixel aspect ratio. Ignored, as xterm ignores it; the raster attribute's
    /// Pan;Pad states the aspect ratio when a picture wants one other than square.</param>
    /// <param name="p2">Background select. 1 leaves unset pixels transparent.</param>
    /// <param name="p3">Horizontal grid size. Unused by every implementation, including this one.</param>
    /// <param name="cellWidth">Cell width in pixels, which decides how many columns the image covers.</param>
    /// <param name="cellHeight">Cell height in pixels, which decides how many rows the image covers.</param>
    /// <param name="maxPixels">
    /// Ceiling on the decoded image. A payload declares no size until it has been drawn, so without
    /// one a hostile or simply broken process can make the terminal allocate until it dies.
    /// </param>
    /// <param name="backgroundBgra">Packed BGRA to use for unset pixels when <paramref name="p2"/> is not 1.</param>
    /// <param name="palette">
    /// The colour registers to draw from. Pass a fresh one for private registers (mode 1070 set,
    /// the default) or a shared one to let images inherit each other's colours the way a VT340 did.
    /// </param>
    public SixelDecoder(int p1, int p2, int p3, int cellWidth, int cellHeight, int maxPixels,
                        uint backgroundBgra, SixelPalette palette)
    {
        _ = p1;
        _ = p3;
        _cellWidth = Math.Max(1, cellWidth);
        _cellHeight = Math.Max(1, cellHeight);
        _maxPixels = Math.Max(1, maxPixels);
        _fill = p2 == 1 ? 0u : backgroundBgra;
        _palette = palette;
        _color = _palette[0];
    }

    /// <summary>Feeds the next slice of the payload.</summary>
    public void Put(ReadOnlySpan<char> chunk)
    {
        for (int i = 0; i < chunk.Length; i++)
            Feed(chunk[i]);
    }

    /// <summary>
    /// Finishes decoding.
    /// </summary>
    /// <returns>
    /// The image, or null if the payload was empty, oversized, or too malformed to make sense of.
    /// </returns>
    public TerminalImage? Finish()
    {
        // A number or repeat count sitting unterminated at the end still counts.
        if (_mode != Mode.Ground)
        {
            PushParam();
            CompleteMode();
        }

        if (_failed)
            return null;

        int width = _rasterWidth > 0 ? _rasterWidth : _maxX;
        int height = _rasterHeight > 0 ? _rasterHeight : _maxBandBottom + 1;
        if (width <= 0 || height <= 0)
            return null;

        if (!EnsureCapacity(width, height))
            return null;

        byte[] exact;
        if (_capWidth == width && _capHeight == height)
        {
            exact = _pixels;
        }
        else
        {
            exact = new byte[(long)width * height * TerminalImage.BytesPerPixel];
            Prefill(exact);
            int copyBytes = Math.Min(width, _capWidth) * TerminalImage.BytesPerPixel;
            int copyRows = Math.Min(height, _capHeight);
            for (int y = 0; y < copyRows; y++)
            {
                Array.Copy(_pixels, (long)y * _capWidth * TerminalImage.BytesPerPixel,
                           exact, (long)y * width * TerminalImage.BytesPerPixel, copyBytes);
            }
        }

        return new TerminalImage(exact, width, height, _cellWidth, _cellHeight);
    }

    private void Feed(char c)
    {
        if (_mode != Mode.Ground)
        {
            if (c >= '0' && c <= '9')
            {
                // Saturate rather than overflow. A parameter this large is already nonsense; what
                // matters is that it stays nonsense instead of wrapping into a plausible number.
                if (_param <= int.MaxValue / 16)
                    _param = _param * 10 + (c - '0');
                return;
            }

            if (c == ';')
            {
                PushParam();
                return;
            }

            // Anything else ends the parameter list, and is then handled in its own right --
            // "#1#2" is two colour selects, and "!4~" is a repeat whose terminator is the data.
            PushParam();
            CompleteMode();
        }

        switch (c)
        {
            case '#':
                StartMode(Mode.Color);
                return;
            case '"':
                StartMode(Mode.Raster);
                return;
            case '!':
                StartMode(Mode.Repeat);
                return;
            case '$': // graphics carriage return
                _x = 0;
                _repeat = 1;
                return;
            case '-': // graphics new line
                _x = 0;
                _repeat = 1;
                if (_bandTop <= int.MaxValue - BandPixels)
                    _bandTop += BandPixels;
                return;
            default:
                if (c >= '?' && c <= '~')
                    WriteSixel(c);
                // Everything else -- newlines above all, which encoders insert to keep lines short
                // -- is not part of the picture and is dropped.
                return;
        }
    }

    private void StartMode(Mode mode)
    {
        _mode = mode;
        _paramCount = 0;
        _param = 0;
    }

    private void PushParam()
    {
        if (_paramCount < _params.Length)
            _params[_paramCount++] = _param;
        _param = 0;
    }

    private void CompleteMode()
    {
        switch (_mode)
        {
            case Mode.Color:
                if (_paramCount >= 5)
                {
                    int register = _params[0];
                    switch (_params[1])
                    {
                        case 1:
                            _palette.SetHls(register, _params[2], _params[3], _params[4]);
                            break;
                        case 2:
                            _palette.SetRgb(register, _params[2], _params[3], _params[4]);
                            break;
                        // Any other colour system is one nobody emits; the register keeps its
                        // current value and is still selected below.
                    }
                    _color = _palette[register];
                }
                else if (_paramCount >= 1)
                {
                    _color = _palette[_params[0]];
                }
                break;

            case Mode.Raster:
                // "Pan;Pad;Ph;Pv. The aspect ratio is how many pixels tall a sixel row is for each
                // pixel it is wide, rounded to a whole number of rows.
                if (_paramCount >= 2 && _params[0] > 0 && _params[1] > 0)
                    _aspect = Math.Clamp((_params[0] + _params[1] / 2) / _params[1], 1, MaxAspect);
                if (_paramCount >= 4)
                    ApplyRaster(_params[2], _params[3]);
                break;

            case Mode.Repeat:
                _repeat = _paramCount >= 1 && _params[0] > 0 ? Math.Min(_params[0], MaxRepeat) : 1;
                break;
        }

        _mode = Mode.Ground;
    }

    /// <summary>
    /// Honours the declared image size. Encoders emit this before any data, so it is normally the
    /// one and only allocation the decode performs.
    /// </summary>
    private void ApplyRaster(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return;

        if ((long)width * height > _maxPixels)
        {
            _failed = true;
            return;
        }

        _rasterWidth = width;
        _rasterHeight = height;
        EnsureCapacity(width, height);
    }

    private void WriteSixel(char c)
    {
        int repeat = _repeat;
        _repeat = 1;
        if (repeat <= 0)
            repeat = 1;

        int bandBottom = _bandTop + BandPixels - 1;
        if (bandBottom > _maxBandBottom)
            _maxBandBottom = bandBottom;

        int startX = _x;
        _x = startX <= int.MaxValue - repeat ? startX + repeat : int.MaxValue;
        if (_x > _maxX)
            _maxX = _x;

        if (_failed)
            return;

        int bits = c - '?';
        if (bits == 0)
            return; // '?' advances the cursor without setting anything

        int endX = _x;
        if (_rasterWidth > 0)
            endX = Math.Min(endX, _rasterWidth);
        if (startX >= endX)
            return; // entirely clipped by the declared width

        if (!EnsureCapacity(endX, bandBottom + 1))
            return;

        for (int row = 0; row < BandHeight; row++)
        {
            if ((bits & (1 << row)) == 0)
                continue;

            // A sixel row is _aspect pixel rows tall.
            for (int k = 0; k < _aspect; k++)
            {
                int y = _bandTop + row * _aspect + k;
                if (_rasterHeight > 0 && y >= _rasterHeight)
                    continue;

                int offset = (y * _capWidth + startX) * TerminalImage.BytesPerPixel;
                for (int i = startX; i < endX; i++)
                {
                    _pixels[offset] = (byte)_color;             // B
                    _pixels[offset + 1] = (byte)(_color >> 8);  // G
                    _pixels[offset + 2] = (byte)(_color >> 16); // R
                    _pixels[offset + 3] = (byte)(_color >> 24); // A
                    offset += TerminalImage.BytesPerPixel;
                }
            }
        }
    }

    /// <summary>
    /// Grows the canvas to hold at least the given extent, preserving what is already drawn.
    /// </summary>
    /// <returns>False once the image has grown past its budget, after which nothing more is drawn.</returns>
    private bool EnsureCapacity(int needWidth, int needHeight)
    {
        if (_failed)
            return false;
        if (needWidth <= _capWidth && needHeight <= _capHeight)
            return true;
        if (needWidth <= 0 || needHeight <= 0)
            return false;

        // An image without raster attributes reveals its size only by being drawn, so the canvas
        // doubles rather than growing to fit -- otherwise a wide image re-strides its whole buffer
        // once per column. Only the dimension that is short doubles: doubling the width of a wide
        // picture every time it grew another band taller ran it into the budget long before its
        // pixels did.
        int width = _capWidth == 0 ? Math.Max(needWidth, 256)
                  : needWidth > _capWidth ? Math.Max(needWidth, _capWidth * 2) : _capWidth;
        int height = _capHeight == 0 ? Math.Max(needHeight, BandHeight)
                   : needHeight > _capHeight ? Math.Max(needHeight, _capHeight * 2) : _capHeight;

        if ((long)width * height > _maxPixels)
        {
            // Try once at exactly the size asked for; doubling may have been what overshot. Never
            // SMALLER than the canvas already is in either dimension: what is drawn is copied across
            // row by row at the old width, and a narrower canvas -- the case where only the height
            // was short, so needWidth is less than the width already allocated -- had that copy run
            // off the end of the new buffer. The exception escaped Terminal.Write and took the
            // host's read loop with it: a wide picture growing downwards (VT DOOM) froze the screen.
            width = Math.Max(needWidth, _capWidth);
            height = Math.Max(needHeight, _capHeight);
            if ((long)width * height > _maxPixels)
            {
                _failed = true;
                return false;
            }
        }

        var next = new byte[(long)width * height * TerminalImage.BytesPerPixel];
        Prefill(next);

        if (_capWidth > 0 && _capHeight > 0)
        {
            int rowBytes = _capWidth * TerminalImage.BytesPerPixel;
            for (int y = 0; y < _capHeight; y++)
            {
                Array.Copy(_pixels, (long)y * rowBytes,
                           next, (long)y * width * TerminalImage.BytesPerPixel, rowBytes);
            }
        }

        _pixels = next;
        _capWidth = width;
        _capHeight = height;
        return true;
    }

    /// <summary>
    /// Paints a fresh buffer with the background. Skipped entirely when the background is
    /// transparent, because a new array is already all zeroes.
    /// </summary>
    private void Prefill(byte[] buffer)
    {
        if (_fill == 0)
            return;

        byte b = (byte)_fill;
        byte g = (byte)(_fill >> 8);
        byte r = (byte)(_fill >> 16);
        byte a = (byte)(_fill >> 24);
        for (long i = 0; i < buffer.LongLength; i += TerminalImage.BytesPerPixel)
        {
            buffer[i] = b;
            buffer[i + 1] = g;
            buffer[i + 2] = r;
            buffer[i + 3] = a;
        }
    }
}
