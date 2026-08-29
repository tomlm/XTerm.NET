using System.IO.Compression;
using System.Text;

namespace XTerm.Graphics;

/// <summary>Why a Kitty command could not be carried out, in the protocol's own vocabulary.</summary>
internal enum KittyError
{
    None,

    /// <summary>The command asked for something this terminal does not implement.</summary>
    Unsupported,

    /// <summary>The payload was not what the control data said it was.</summary>
    BadData,

    /// <summary>No image is stored under the id given.</summary>
    NotFound,

    /// <summary>The image is larger than the terminal will hold.</summary>
    TooLarge
}

/// <summary>
/// Collects one image as it arrives, across however many escape sequences it takes.
/// </summary>
/// <remarks>
/// <para>A Kitty transmission is chunked: the first sequence carries the control data and the rest
/// carry only <c>m=1</c> and more payload, with an empty <c>m=0</c> to finish. So the command has to
/// be remembered from the first chunk, and the payload accumulated until the last.</para>
/// <para>The base64 is accumulated as TEXT and decoded once at the end. Decoding each chunk as it
/// arrives only works if every chunk is a multiple of four characters, which the protocol does not
/// promise -- and a chunk boundary falling mid-quantum would silently corrupt the picture from that
/// point on.</para>
/// </remarks>
internal sealed class KittyTransmission
{
    private readonly StringBuilder _payload = new();

    /// <summary>The control data from the first chunk, which is the one that carries it.</summary>
    public KittyCommand Command { get; }

    /// <summary>How much base64 has arrived so far, for the size guard.</summary>
    public int PayloadLength => _payload.Length;

    public KittyTransmission(KittyCommand command)
    {
        Command = command;
    }

    public void Append(ReadOnlySpan<char> base64) => _payload.Append(base64);

    /// <summary>
    /// Turns everything collected into pixels.
    /// </summary>
    /// <param name="maxPixels">The largest image the terminal will hold.</param>
    /// <returns>BGRA8888 pixels and their dimensions, or an error saying why not.</returns>
    public KittyError TryBuild(int maxPixels, out byte[] pixels, out int width, out int height)
    {
        pixels = Array.Empty<byte>();
        width = height = 0;

        byte[] raw;
        try
        {
            raw = Convert.FromBase64String(_payload.ToString());
        }
        catch (FormatException)
        {
            return KittyError.BadData;
        }

        if (Command.Compression == 'z')
        {
            if (!TryInflate(raw, maxPixels, out raw))
                return KittyError.BadData;
        }

        return Command.Format switch
        {
            KittyCommand.FormatPng => BuildFromPng(raw, maxPixels, out pixels, out width, out height),
            KittyCommand.FormatRgb => BuildFromRaw(raw, 3, maxPixels, out pixels, out width, out height),
            KittyCommand.FormatRgba => BuildFromRaw(raw, 4, maxPixels, out pixels, out width, out height),
            _ => KittyError.Unsupported
        };
    }

    private static bool TryInflate(byte[] compressed, int maxPixels, out byte[] inflated)
    {
        inflated = Array.Empty<byte>();

        try
        {
            using var input = new MemoryStream(compressed);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();

            // Bounded on the way out, not after the fact: a small payload can inflate to something
            // enormous, and refusing it afterwards means having already allocated it.
            var ceiling = (long)maxPixels * TerminalImage.BytesPerPixel + 1024;
            // Pooled for the same reason PngDecoder.Inflate is: one 16 KB array per transmission,
            // and an animation is one transmission per frame.
            var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(16 * 1024);
            try
            {
                int read;
                while ((read = zlib.Read(buffer, 0, buffer.Length)) > 0)
                {
                    // Before the write, for the reason PngDecoder.Inflate gives: a MemoryStream
                    // grows by doubling, so checking afterwards means the allocation being
                    // refused has already been made.
                    if (output.Length + read > ceiling)
                        return false;

                    output.Write(buffer, 0, read);
                }
            }
            finally
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
            }

            inflated = output.ToArray();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private KittyError BuildFromPng(byte[] data, int maxPixels,
                                    out byte[] pixels, out int width, out int height)
        => PngDecoder.TryDecode(data, maxPixels, out pixels, out width, out height)
            ? KittyError.None
            : KittyError.BadData;

    /// <summary>
    /// Expands raw RGB or RGBA into the BGRA the rest of the graphics path uses.
    /// </summary>
    /// <remarks>
    /// Raw formats carry no dimensions of their own, so the control data has to supply them, and
    /// the byte count has to agree. A payload that does not match what was declared is a mistake
    /// worth reporting rather than a picture to guess at.
    /// </remarks>
    private KittyError BuildFromRaw(byte[] data, int bytesPerPixel, int maxPixels,
                                    out byte[] pixels, out int width, out int height)
    {
        pixels = Array.Empty<byte>();
        width = Command.Width;
        height = Command.Height;

        if (width <= 0 || height <= 0)
            return KittyError.BadData;
        if ((long)width * height > maxPixels)
            return KittyError.TooLarge;
        if ((long)data.Length < (long)width * height * bytesPerPixel)
            return KittyError.BadData;

        pixels = new byte[width * height * TerminalImage.BytesPerPixel];

        for (int i = 0, source = 0, target = 0; i < width * height; i++)
        {
            pixels[target] = data[source + 2];      // B
            pixels[target + 1] = data[source + 1];  // G
            pixels[target + 2] = data[source];      // R
            pixels[target + 3] = bytesPerPixel == 4 ? data[source + 3] : (byte)255;

            source += bytesPerPixel;
            target += TerminalImage.BytesPerPixel;
        }

        return KittyError.None;
    }
}
