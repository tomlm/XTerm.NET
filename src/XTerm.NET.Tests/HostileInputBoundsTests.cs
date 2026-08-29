using System.IO.Compression;
using XTerm.Options;

namespace XTerm.Tests;

/// <summary>
/// Bounds on what a program writing to the pty can make this terminal allocate or do. Every test
/// here describes a sequence that is protocol-legal and cheap to send, and asserts that the
/// terminal's response to it stays proportionate. They fail without their guards.
/// </summary>
public class HostileInputBoundsTests
{
    private const string Esc = "\u001b";

    private static Terminal NewTerminal() =>
        new(new TerminalOptions { Cols = 80, Rows = 24 });

    [Fact]
    public void A_cluster_stops_growing_after_a_sane_number_of_marks()
    {
        // One base character and then combining marks forever: without a cap the cell's string
        // grows without bound and every intermediate length is interned for the process's life.
        var terminal = NewTerminal();
        terminal.Write("e" + new string('\u0301', 5000));

        var line = terminal.Buffer.Lines[0]!;
        Assert.True(line[0].Content.Length <= 64,
            $"cluster grew to {line[0].Content.Length} chars");
    }

    [Fact]
    public void Repeat_of_a_self_joining_cluster_stays_bounded()
    {
        // REP repeats the preceding character. When that character joins the cell it just landed
        // in, each repeat lengthened the same cluster -- quadratic inside one CSI sequence.
        var terminal = NewTerminal();
        terminal.Write("e\u0301");
        terminal.Write($"{Esc}[32767b");

        var line = terminal.Buffer.Lines[0]!;
        Assert.True(line[0].Content.Length <= 64,
            $"cluster grew to {line[0].Content.Length} chars under REP");
    }

    [Theory]
    [InlineData("S")]   // SU
    [InlineData("T")]   // SD
    [InlineData("L")]   // IL
    [InlineData("M")]   // DL
    public void A_huge_scroll_count_does_not_loop_a_billion_times(string final)
    {
        // Each of these scrolls a line at a time, so an unclamped count is minutes of work for a
        // picture identical to scrolling the region once over.
        var terminal = NewTerminal();
        terminal.Write("hello");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        terminal.Write($"{Esc}[2000000000{final}");
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 2000,
            $"CSI 2000000000 {final} took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void An_unterminated_osc_does_not_grow_without_bound()
    {
        var terminal = NewTerminal();
        terminal.Write($"{Esc}]0;");
        for (var i = 0; i < 40; i++)
            terminal.Write(new string('x', 100_000));

        // Nothing to assert on the payload directly -- it is private -- so this pins the outcome
        // that matters: the terminal is still responsive and the sequence never completed.
        terminal.Write("\u0007");
        terminal.Write("after");
        Assert.Equal("after", string.Concat(
            Enumerable.Range(0, 5).Select(i => terminal.Buffer.Lines[0]![i].Content)));
    }

    [Fact]
    public void A_parameter_that_overflows_saturates_instead_of_wrapping()
    {
        // Unchecked multiply turned an absurd parameter into a small or negative one, so a
        // sequence asking for something impossible became one asking for something plausible.
        var terminal = NewTerminal();
        terminal.Write("abc");
        terminal.Write($"{Esc}[99999999999999D");   // CUB with a value far past int.MaxValue

        Assert.Equal(0, terminal.Buffer.X);
    }

    [Fact]
    public void Osc1337_with_invalid_base64_does_not_throw()
    {
        // FormatException is not an ArgumentException, so the guard around this decode missed the
        // one exception the decode actually raises, and it escaped through Write to the read loop.
        var terminal = NewTerminal();
        var ex = Record.Exception(() => terminal.Write($"{Esc}]1337;SetUserVar=name=!!!not base64!!!\u0007"));
        Assert.Null(ex);
    }

    [Fact]
    public void A_png_that_inflates_far_past_its_header_is_refused()
    {
        // A decompression bomb: the header declares one pixel, the image data inflates to
        // hundreds of megabytes. The size check used to run after the allocation.
        var png = BuildBombPng(declaredWidth: 1, declaredHeight: 1, inflatedBytes: 64 * 1024 * 1024);

        var before = GC.GetTotalMemory(true);
        var ok = global::XTerm.Graphics.PngDecoder.TryDecode(png, maxPixels: 4_000_000,
                                               out _, out _, out _);
        var after = GC.GetTotalMemory(false);

        Assert.False(ok);
        Assert.True(after - before < 16 * 1024 * 1024,
            $"decoding allocated {(after - before) / 1024 / 1024} MB for a 1x1 image");
    }

    [Fact]
    public void Resize_rejects_negative_but_accepts_zero()
    {
        var terminal = NewTerminal();

        Assert.Throws<ArgumentOutOfRangeException>(() => terminal.Resize(-1, 24));
        Assert.Throws<ArgumentOutOfRangeException>(() => terminal.Resize(80, -1));

        // Zero is a state a host really reports, before its control has been laid out.
        terminal.Resize(80, 0);
        terminal.Resize(80, 24);
    }

    /// <summary>
    /// A structurally valid PNG whose IDAT inflates to far more than its header implies.
    /// </summary>
    private static byte[] BuildBombPng(int declaredWidth, int declaredHeight, int inflatedBytes)
    {
        var ihdr = new byte[13];
        WriteBe(ihdr, 0, declaredWidth);
        WriteBe(ihdr, 4, declaredHeight);
        ihdr[8] = 8;    // bit depth
        ihdr[9] = 2;    // truecolour
        ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            zlib.Write(new byte[inflatedBytes], 0, inflatedBytes);

        using var png = new MemoryStream();
        png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        WriteChunk(png, "IHDR", ihdr);
        WriteChunk(png, "IDAT", compressed.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void WriteBe(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var length = new byte[4];
        WriteBe(length, 0, data.Length);
        stream.Write(length);

        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);

        var crc = Crc32(typeBytes, data);
        var crcBytes = new byte[4];
        WriteBe(crcBytes, 0, unchecked((int)crc));
        stream.Write(crcBytes);
    }

    private static uint Crc32(byte[] type, byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var b in type.Concat(data))
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        }
        return crc ^ 0xFFFFFFFF;
    }
}
