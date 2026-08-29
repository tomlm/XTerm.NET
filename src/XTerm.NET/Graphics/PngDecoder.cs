using System.Buffers.Binary;
using System.IO.Compression;

namespace XTerm.Graphics;

/// <summary>
/// Decodes a PNG into the BGRA8888 layout the rest of the graphics path uses.
/// </summary>
/// <remarks>
/// <para>Hand-rolled rather than taken from a package. XTerm.NET is a headless emulator with two
/// small dependencies and no imaging stack, and pulling one in so that a terminal can show a
/// picture would be a poor trade. The only thing needed from outside is the inflate, and
/// <see cref="ZLibStream"/> has been in the framework since .NET 6.</para>
/// <para>Nothing here throws on bad input. A PNG arriving over a Kitty escape sequence is untrusted
/// output from another process; a truncated file, a nonsense header or a chunk that lies about its
/// length all mean "no image", not an exception escaping into the parser.</para>
/// </remarks>
internal static class PngDecoder
{
    private static ReadOnlySpan<byte> Signature => new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    private enum ColourType
    {
        Greyscale = 0,
        Truecolour = 2,
        Indexed = 3,
        GreyscaleAlpha = 4,
        TruecolourAlpha = 6
    }

    /// <summary>
    /// Decodes a PNG.
    /// </summary>
    /// <param name="maxPixels">
    /// Ceiling on the decoded image. The header declares a size before any data arrives, so an
    /// absurd one is refused before anything is allocated for it.
    /// </param>
    /// <returns>BGRA8888 pixels with the width and height, or null if this is not a PNG we can read.</returns>
    public static bool TryDecode(ReadOnlySpan<byte> data, long maxPixels,
                                 out byte[] pixels, out int width, out int height)
    {
        pixels = Array.Empty<byte>();
        width = height = 0;

        try
        {
            return TryDecodeCore(data, maxPixels, out pixels, out width, out height);
        }
        catch
        {
            // Malformed input reaches here as an index or stream exception. It means the same thing
            // as a clean rejection, and the caller has one answer for both.
            pixels = Array.Empty<byte>();
            width = height = 0;
            return false;
        }
    }

    private static bool TryDecodeCore(ReadOnlySpan<byte> data, long maxPixels,
                                      out byte[] pixels, out int width, out int height)
    {
        pixels = Array.Empty<byte>();
        width = height = 0;

        if (data.Length < Signature.Length || !data[..Signature.Length].SequenceEqual(Signature))
            return false;

        int bitDepth = 0;
        int interlace = 0;
        ColourType colourType = ColourType.Truecolour;
        byte[]? palette = null;
        byte[]? paletteAlpha = null;
        var idat = new MemoryStream();

        var offset = Signature.Length;
        var sawHeader = false;

        while (offset + 8 <= data.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(data[offset..]);
            if (length < 0 || offset + 12 + length > data.Length)
                return false; // a chunk claiming more than the file holds

            var type = data.Slice(offset + 4, 4);
            var body = data.Slice(offset + 8, length);
            offset += 12 + length; // length + type + body + CRC, which is not checked

            if (IsChunk(type, "IHDR"))
            {
                if (length < 13)
                    return false;

                width = BinaryPrimitives.ReadInt32BigEndian(body);
                height = BinaryPrimitives.ReadInt32BigEndian(body[4..]);
                bitDepth = body[8];
                colourType = (ColourType)body[9];
                interlace = body[12];

                if (width <= 0 || height <= 0)
                    return false;
                if ((long)width * height > maxPixels)
                    return false;

                if (interlace is not (0 or 1))
                    return false;

                if (bitDepth is not (1 or 2 or 4 or 8 or 16))
                    return false;
                if (colourType is not (ColourType.Greyscale or ColourType.Truecolour or ColourType.Indexed
                                       or ColourType.GreyscaleAlpha or ColourType.TruecolourAlpha))
                    return false;
                // Only indexed and greyscale are allowed below 8 bits per sample.
                if (bitDepth < 8 && colourType is not (ColourType.Greyscale or ColourType.Indexed))
                    return false;

                sawHeader = true;
            }
            else if (IsChunk(type, "PLTE"))
            {
                palette = body.ToArray();
            }
            else if (IsChunk(type, "tRNS"))
            {
                paletteAlpha = body.ToArray();
            }
            else if (IsChunk(type, "IDAT"))
            {
                idat.Write(body);
            }
            else if (IsChunk(type, "IEND"))
            {
                break;
            }
        }

        if (!sawHeader || idat.Length == 0)
            return false;

        var samples = SamplesPerPixel(colourType);
        if (colourType == ColourType.Indexed && palette is null)
            return false;

        // Rows are filtered against the row above, so the whole image is inflated at once rather
        // than streamed -- there is no way to finish one row without the one before it.
        var bitsPerPixel = samples * bitDepth;
        var bytesPerPixel = (bitsPerPixel + 7) / 8;

        if (interlace == 0)
        {
            var bytesPerRow = (width * bitsPerPixel + 7) / 8;
            var raw = Inflate(idat, (long)(bytesPerRow + 1) * height);
            if (raw.Length < (long)(bytesPerRow + 1) * height)
                return false;

            Unfilter(raw, 0, width, height, bytesPerRow, bytesPerPixel);
            pixels = ToBgra(raw, 0, width, height, bytesPerRow, bitDepth, colourType, palette, paletteAlpha);
            return true;
        }

        return TryDecodeInterlaced(idat, width, height, bitsPerPixel, bytesPerPixel,
                                   bitDepth, colourType, palette, paletteAlpha, out pixels);
    }

    /// <summary>
    /// The seven Adam7 passes: where each starts, and how far apart its pixels are.
    /// </summary>
    /// <remarks>
    /// Pass 1 takes every eighth pixel of every eighth row, and each pass afterwards fills in the
    /// gaps the ones before it left, so the picture appears at increasing resolution as it loads.
    /// Every pixel belongs to exactly one pass.
    /// </remarks>
    private static readonly (int X, int Y, int StepX, int StepY)[] Adam7 =
    {
        (0, 0, 8, 8),
        (4, 0, 8, 8),
        (0, 4, 4, 8),
        (2, 0, 4, 4),
        (0, 2, 2, 4),
        (1, 0, 2, 2),
        (0, 1, 1, 2)
    };

    /// <summary>
    /// Decodes the seven Adam7 passes and scatters them into one image.
    /// </summary>
    /// <remarks>
    /// <para>Each pass is a complete little image of its own: its own row filters, its own notion of
    /// "the row above", and its own row width. That is why this cannot reuse the straight-through
    /// path with a stride -- the filters would be reconstructed against the wrong neighbours.</para>
    /// <para>A pass whose width or height works out to zero contributes no bytes at all, not even a
    /// filter byte. Skipping it is required, not an optimisation: counting it would shift every
    /// later pass by one byte and turn the rest of the picture into noise.</para>
    /// </remarks>
    private static bool TryDecodeInterlaced(MemoryStream idat, int width, int height,
                                            int bitsPerPixel, int bytesPerPixel,
                                            int bitDepth, ColourType colourType,
                                            byte[]? palette, byte[]? paletteAlpha,
                                            out byte[] pixels)
    {
        pixels = Array.Empty<byte>();

        long expected = 0;
        foreach (var pass in Adam7)
        {
            var passWidth = (width - pass.X + pass.StepX - 1) / pass.StepX;
            var passHeight = (height - pass.Y + pass.StepY - 1) / pass.StepY;
            if (passWidth <= 0 || passHeight <= 0)
                continue;

            expected += (long)((passWidth * bitsPerPixel + 7) / 8 + 1) * passHeight;
        }

        var raw = Inflate(idat, expected);
        if (raw.Length < expected)
            return false;

        var output = new byte[(long)width * height * TerminalImage.BytesPerPixel];
        var offset = 0;

        foreach (var pass in Adam7)
        {
            var passWidth = (width - pass.X + pass.StepX - 1) / pass.StepX;
            var passHeight = (height - pass.Y + pass.StepY - 1) / pass.StepY;
            if (passWidth <= 0 || passHeight <= 0)
                continue;

            var bytesPerRow = (passWidth * bitsPerPixel + 7) / 8;

            Unfilter(raw, offset, passWidth, passHeight, bytesPerRow, bytesPerPixel);
            var passPixels = ToBgra(raw, offset, passWidth, passHeight, bytesPerRow,
                                    bitDepth, colourType, palette, paletteAlpha);

            for (int y = 0; y < passHeight; y++)
            {
                var targetY = pass.Y + y * pass.StepY;
                for (int x = 0; x < passWidth; x++)
                {
                    var targetX = pass.X + x * pass.StepX;
                    var from = (y * passWidth + x) * TerminalImage.BytesPerPixel;
                    var to = (targetY * width + targetX) * TerminalImage.BytesPerPixel;
                    Array.Copy(passPixels, from, output, to, TerminalImage.BytesPerPixel);
                }
            }

            offset += (bytesPerRow + 1) * passHeight;
        }

        pixels = output;
        return true;
    }

    /// <summary>
    /// Compares a four-byte chunk type against its name.
    /// </summary>
    /// <remarks>
    /// Spelled out rather than using a UTF-8 literal, which needs C# 11 and this library targets
    /// net6.0. Chunk names are ASCII by specification, so a char-by-char compare is exact.
    /// </remarks>
    private static bool IsChunk(ReadOnlySpan<byte> type, string name)
        => type.Length == 4
           && type[0] == name[0] && type[1] == name[1]
           && type[2] == name[2] && type[3] == name[3];

    private static int SamplesPerPixel(ColourType colourType) => colourType switch
    {
        ColourType.Greyscale => 1,
        ColourType.Truecolour => 3,
        ColourType.Indexed => 1,
        ColourType.GreyscaleAlpha => 2,
        ColourType.TruecolourAlpha => 4,
        _ => 0
    };

    private static byte[] Inflate(MemoryStream compressed, long expected)
    {
        // Bounded on the way out, not after the fact -- the same rule KittyTransmission.TryInflate
        // already follows, and for the same reason: a small payload can inflate to something
        // enormous, and refusing it afterwards means having already allocated it. CopyTo here let a
        // 305 KB IDAT declaring a 1x1 image pull 812 MB of zeros into memory before the callers'
        // length check ever ran.
        //
        // The ceiling is not a guess: both callers derive `expected` from the IHDR, and a PNG whose
        // image data inflates past its own header's row arithmetic is malformed. Overshooting
        // returns empty, which the callers already reject.
        if (expected <= 0 || expected >= int.MaxValue)
            return [];

        compressed.Position = 0;
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        using var output = new MemoryStream((int)expected);

        // Pooled: a 16 KB array per decode is pure garbage on a stream carrying many images, and
        // an animation is exactly that -- one decode per frame.
        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            int read;
            while ((read = zlib.Read(buffer, 0, buffer.Length)) > 0)
            {
                // Checked BEFORE the write, not after it. The stream is created with capacity
                // `expected`, so a write that crosses it grows the backing array -- MemoryStream
                // doubles -- and the old check fired only once that allocation had already
                // happened. On a large declared image that is a hundred megabytes handed out to
                // refuse a payload, which is the exact "bounded after the fact" shape this whole
                // guard exists to avoid.
                if (output.Length + read > expected)
                    return [];

                output.Write(buffer, 0, read);
            }
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
        }

        return output.ToArray();
    }

    /// <summary>
    /// Undoes the per-row filters, in place.
    /// </summary>
    /// <remarks>
    /// Each row carries a filter byte and is reconstructed from the bytes to its left and the row
    /// above, so this runs top to bottom and cannot be reordered. The filter byte is consumed here
    /// and left behind in the buffer; <see cref="ToBgra"/> skips it by stride.
    /// </remarks>
    private static void Unfilter(byte[] raw, int offset, int width, int height,
                                 int bytesPerRow, int bytesPerPixel)
    {
        var stride = bytesPerRow + 1;

        for (int y = 0; y < height; y++)
        {
            var rowStart = offset + y * stride + 1;
            var filter = raw[offset + y * stride];
            var aboveStart = rowStart - stride;

            for (int i = 0; i < bytesPerRow; i++)
            {
                int left = i >= bytesPerPixel ? raw[rowStart + i - bytesPerPixel] : 0;
                int above = y > 0 ? raw[aboveStart + i] : 0;
                int aboveLeft = (y > 0 && i >= bytesPerPixel) ? raw[aboveStart + i - bytesPerPixel] : 0;

                raw[rowStart + i] = filter switch
                {
                    0 => raw[rowStart + i],                                        // None
                    1 => (byte)(raw[rowStart + i] + left),                         // Sub
                    2 => (byte)(raw[rowStart + i] + above),                        // Up
                    3 => (byte)(raw[rowStart + i] + ((left + above) >> 1)),        // Average
                    4 => (byte)(raw[rowStart + i] + Paeth(left, above, aboveLeft)), // Paeth
                    _ => raw[rowStart + i]
                };
            }
        }
    }

    private static int Paeth(int left, int above, int aboveLeft)
    {
        var estimate = left + above - aboveLeft;
        var dLeft = Math.Abs(estimate - left);
        var dAbove = Math.Abs(estimate - above);
        var dAboveLeft = Math.Abs(estimate - aboveLeft);

        if (dLeft <= dAbove && dLeft <= dAboveLeft)
            return left;
        return dAbove <= dAboveLeft ? above : aboveLeft;
    }

    /// <summary>
    /// Expands the unfiltered rows into BGRA8888.
    /// </summary>
    /// <remarks>
    /// 16-bit samples are truncated to their high byte. A terminal cell is a handful of pixels on a
    /// screen that cannot show the difference, and carrying the extra byte through the whole
    /// graphics path to discard it at the end would buy nothing.
    /// </remarks>
    private static byte[] ToBgra(byte[] raw, int offset, int width, int height, int bytesPerRow,
                                 int bitDepth, ColourType colourType, byte[]? palette, byte[]? paletteAlpha)
    {
        var stride = bytesPerRow + 1;
        var output = new byte[width * height * TerminalImage.BytesPerPixel];
        var step = bitDepth == 16 ? 2 : 1;

        for (int y = 0; y < height; y++)
        {
            var rowStart = offset + y * stride + 1;
            var target = y * width * TerminalImage.BytesPerPixel;

            for (int x = 0; x < width; x++)
            {
                byte r, g, b, a = 255;

                if (bitDepth < 8)
                {
                    var value = ReadPackedSample(raw, rowStart, x, bitDepth);

                    if (colourType == ColourType.Indexed)
                    {
                        ReadPalette(palette!, paletteAlpha, value, out r, out g, out b, out a);
                    }
                    else
                    {
                        // Greyscale below 8 bits is a fraction of full scale, not a raw level.
                        var max = (1 << bitDepth) - 1;
                        r = g = b = (byte)(value * 255 / max);
                    }
                }
                else
                {
                    var samples = SamplesPerPixel(colourType);
                    var pixelStart = rowStart + x * samples * step;

                    switch (colourType)
                    {
                        case ColourType.Greyscale:
                            r = g = b = raw[pixelStart];
                            break;
                        case ColourType.GreyscaleAlpha:
                            r = g = b = raw[pixelStart];
                            a = raw[pixelStart + step];
                            break;
                        case ColourType.Truecolour:
                            r = raw[pixelStart];
                            g = raw[pixelStart + step];
                            b = raw[pixelStart + 2 * step];
                            break;
                        case ColourType.TruecolourAlpha:
                            r = raw[pixelStart];
                            g = raw[pixelStart + step];
                            b = raw[pixelStart + 2 * step];
                            a = raw[pixelStart + 3 * step];
                            break;
                        default: // Indexed
                            ReadPalette(palette!, paletteAlpha, raw[pixelStart], out r, out g, out b, out a);
                            break;
                    }
                }

                output[target] = b;
                output[target + 1] = g;
                output[target + 2] = r;
                output[target + 3] = a;
                target += TerminalImage.BytesPerPixel;
            }
        }

        return output;
    }

    private static int ReadPackedSample(byte[] raw, int rowStart, int x, int bitDepth)
    {
        var perByte = 8 / bitDepth;
        var value = raw[rowStart + x / perByte];
        var shift = 8 - bitDepth * (x % perByte + 1);
        return (value >> shift) & ((1 << bitDepth) - 1);
    }

    private static void ReadPalette(byte[] palette, byte[]? paletteAlpha, int index,
                                    out byte r, out byte g, out byte b, out byte a)
    {
        var at = index * 3;
        if (at + 2 < palette.Length)
        {
            r = palette[at];
            g = palette[at + 1];
            b = palette[at + 2];
        }
        else
        {
            // An index past the palette is malformed. Black is a better answer than a throw, and
            // the picture still arrives.
            r = g = b = 0;
        }

        a = paletteAlpha is not null && index < paletteAlpha.Length ? paletteAlpha[index] : (byte)255;
    }
}
