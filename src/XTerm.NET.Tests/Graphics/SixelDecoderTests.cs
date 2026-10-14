using XTerm.Graphics;
using XTerm.Options;

namespace XTerm.Tests.Graphics;

/// <summary>
/// Decoding a DECSIXEL payload into pixels. Driven through <see cref="Terminal.Write"/> rather
/// than against the decoder directly, because the seam that matters is the whole path: parser
/// hook, streamed payload, decode, and an image landing on a cell.
///
/// <para>The payloads here are small enough to work out by hand. A Sixel data character carries
/// six stacked pixels as the low six bits of <c>c - 0x3F</c>, so '@' is the top pixel alone and
/// '~' is all six.</para>
/// </summary>
public class SixelDecoderTests
{
    private const string Esc = "\u001b";
    private const string St = Esc + "\\";

    /// <summary>Background select 1: pixels left unset stay transparent.</summary>
    private const int Transparent = 1;

    /// <summary>Background select 0: pixels left unset take the terminal background.</summary>
    private const int OpaqueBackground = 0;

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Terminal, List<TerminalImage>> Seen = new();

    private static Terminal Fresh(Action<TerminalOptions>? configure = null)
    {
        var options = new TerminalOptions { Cols = 40, Rows = 10 };
        configure?.Invoke(options);
        var terminal = new Terminal(options);

        // The buffer keeps a Sixel's pixels, not the picture, so the decoded picture is caught as
        // it is placed.
        var seen = new List<TerminalImage>();
        terminal.SixelPlaced += seen.Add;
        Seen.Add(terminal, seen);
        return terminal;
    }

    /// <summary>The last picture a Sixel placed, or null if none reached the buffer.</summary>
    private static TerminalImage? Placed(Terminal terminal)
        => Seen.TryGetValue(terminal, out var seen) && seen.Count > 0 ? seen[^1] : null;

    private static void WriteSixel(Terminal terminal, string body, int backgroundSelect = Transparent)
        => terminal.Write($"{Esc}P0;{backgroundSelect};0q{body}{St}");

    private static TerminalImage? TryDecode(string body, int backgroundSelect = Transparent,
                                           Action<TerminalOptions>? configure = null)
    {
        var terminal = Fresh(configure);
        WriteSixel(terminal, body, backgroundSelect);
        return Placed(terminal);
    }

    private static TerminalImage Decode(string body, int backgroundSelect = Transparent,
                                        Action<TerminalOptions>? configure = null)
    {
        var image = TryDecode(body, backgroundSelect, configure);
        Assert.True(image is not null, "no image reached the buffer");
        return image!;
    }

    private static (byte R, byte G, byte B, byte A) Pixel(TerminalImage image, int x, int y)
    {
        var span = image.Pixels.Span;
        var offset = (y * image.PixelWidth + x) * TerminalImage.BytesPerPixel;
        return (span[offset + 2], span[offset + 1], span[offset], span[offset + 3]);
    }

    private static readonly (byte R, byte G, byte B, byte A) Red = (255, 0, 0, 255);
    private static readonly (byte R, byte G, byte B, byte A) Green = (0, 255, 0, 255);
    private static readonly (byte R, byte G, byte B, byte A) Blue = (0, 0, 255, 255);
    private static readonly (byte R, byte G, byte B, byte A) Clear = (0, 0, 0, 0);

    [Fact]
    public void A_single_sixel_sets_the_top_pixel_of_its_band()
    {
        var image = Decode("#0;2;100;0;0@");

        Assert.Equal(1, image.PixelWidth);
        Assert.Equal(6, image.PixelHeight);
        Assert.Equal(Red, Pixel(image, 0, 0));
        Assert.Equal(Clear, Pixel(image, 0, 1));
    }

    [Fact]
    public void A_full_sixel_sets_all_six_pixels_of_its_band()
    {
        var image = Decode("#0;2;0;100;0~");

        Assert.Equal(6, image.PixelHeight);
        for (int y = 0; y < 6; y++)
            Assert.Equal(Green, Pixel(image, 0, y));
    }

    [Fact]
    public void A_question_mark_advances_without_drawing()
    {
        var image = Decode("#0;2;100;0;0??@");

        Assert.Equal(3, image.PixelWidth);
        Assert.Equal(Clear, Pixel(image, 0, 0));
        Assert.Equal(Clear, Pixel(image, 1, 0));
        Assert.Equal(Red, Pixel(image, 2, 0));
    }

    [Fact]
    public void A_repeat_introducer_repeats_the_following_sixel()
    {
        var image = Decode("#0;2;0;0;100!4~");

        Assert.Equal(4, image.PixelWidth);
        Assert.Equal(6, image.PixelHeight);
        Assert.Equal(Blue, Pixel(image, 0, 0));
        Assert.Equal(Blue, Pixel(image, 3, 5));
    }

    [Fact]
    public void A_graphics_newline_starts_the_next_band_six_rows_down()
    {
        var image = Decode("#0;2;100;0;0@-#0;2;0;0;100@");

        Assert.Equal(12, image.PixelHeight);
        Assert.Equal(Red, Pixel(image, 0, 0));
        Assert.Equal(Blue, Pixel(image, 0, 6));
    }

    [Fact]
    public void A_graphics_carriage_return_returns_to_the_left_of_the_same_band()
    {
        // Three red columns, back to the start, then one blue sixel over the first of them.
        var image = Decode("#0;2;100;0;0!3~$#1;2;0;0;100@");

        Assert.Equal(3, image.PixelWidth);
        Assert.Equal(Blue, Pixel(image, 0, 0));
        Assert.Equal(Red, Pixel(image, 0, 1));
        Assert.Equal(Red, Pixel(image, 1, 0));
    }

    [Fact]
    public void Raster_attributes_declare_the_image_size()
    {
        // Six rows are drawn; the raster attribute says the image is two rows tall.
        var image = Decode("\"1;1;3;2#0;2;100;0;0!3~");

        Assert.Equal(3, image.PixelWidth);
        Assert.Equal(2, image.PixelHeight);
        Assert.Equal(Red, Pixel(image, 2, 1));
    }

    [Fact]
    public void An_image_without_raster_attributes_is_sized_by_what_it_drew()
    {
        var image = Decode("#0;2;100;0;0!7~");

        Assert.Equal(7, image.PixelWidth);
        Assert.Equal(6, image.PixelHeight);
    }

    /// <summary>
    /// Sixel's hue ring is rotated 120 degrees from the usual one -- hue 0 is blue, not red. A
    /// conversion that looks correct but skips the rotation produces plausible, wrong colours.
    /// </summary>
    [Theory]
    [InlineData(0, 0, 0, 255)]     // hue 0   -> blue
    [InlineData(120, 255, 0, 0)]   // hue 120 -> red
    [InlineData(240, 0, 255, 0)]   // hue 240 -> green
    public void Hls_colours_are_converted_on_sixels_hue_ring(int hue, byte r, byte g, byte b)
    {
        var image = Decode($"#0;1;{hue};50;100@");

        Assert.Equal((r, g, b, (byte)255), Pixel(image, 0, 0));
    }

    [Fact]
    public void An_hls_colour_with_no_saturation_is_grey()
    {
        var image = Decode("#0;1;120;50;0@");

        var pixel = Pixel(image, 0, 0);
        Assert.Equal(pixel.R, pixel.G);
        Assert.Equal(pixel.G, pixel.B);
        Assert.InRange(pixel.R, 126, 129);
    }

    [Fact]
    public void Selecting_a_register_without_defining_it_uses_the_vt340_default()
    {
        // Register 2 is the VT340's red, 80/13/13 percent.
        var image = Decode("#2~");

        var pixel = Pixel(image, 0, 0);
        Assert.Equal((byte)204, pixel.R);
        Assert.Equal((byte)33, pixel.G);
        Assert.Equal((byte)33, pixel.B);
    }

    [Fact]
    public void Unset_pixels_are_transparent_under_background_select_one()
    {
        var image = Decode("#0;2;100;0;0@", Transparent);

        Assert.Equal((byte)0, Pixel(image, 0, 5).A);
    }

    [Fact]
    public void Unset_pixels_take_the_terminal_background_otherwise()
    {
        var image = Decode("#0;2;100;0;0@", OpaqueBackground);

        var background = Pixel(image, 0, 5);
        Assert.Equal((byte)255, background.A);
        Assert.Equal(Red, Pixel(image, 0, 0));
    }

    /// <summary>
    /// A payload declares no size until it has been drawn, so without a ceiling a process can make
    /// the terminal allocate until it dies.
    /// </summary>
    [Fact]
    public void An_image_larger_than_the_budget_is_discarded()
    {
        var image = TryDecode("\"1;1;4000;4000#0;2;100;0;0~",
            configure: o => o.MaxSixelPixels = 1000);

        Assert.Null(image);
    }

    [Fact]
    public void An_image_that_grows_past_the_budget_while_drawing_is_discarded()
    {
        // No raster attribute, so the size only becomes apparent as it is drawn.
        var image = TryDecode("#0;2;100;0;0!5000~", configure: o => o.MaxSixelPixels = 600);

        Assert.Null(image);
    }

    [Fact]
    public void An_abandoned_payload_produces_no_image()
    {
        var terminal = Fresh();

        // CAN mid-payload: the sequence is dropped rather than terminated.
        terminal.Write($"{Esc}P0;1;0q#0;2;100;0;0!20~\u0018");

        Assert.Null(Placed(terminal));
    }

    [Fact]
    public void An_empty_payload_produces_no_image()
    {
        var terminal = Fresh();
        terminal.Write($"{Esc}P0;1;0q{St}");

        Assert.Null(Placed(terminal));
    }

    /// <summary>
    /// The payload is untrusted output from someone else's process. Nonsense in it must not reach
    /// the caller as an exception.
    /// </summary>
    [Theory]
    [InlineData("#", "a bare colour introducer")]
    [InlineData("#;;;;;", "empty colour parameters")]
    [InlineData("#999999999;2;999;999;999~", "absurd register and channel values")]
    [InlineData("!", "a bare repeat introducer")]
    [InlineData("!999999999~", "an absurd repeat count")]
    [InlineData("\"", "a bare raster introducer")]
    [InlineData("\"0;0;0;0~", "zero raster dimensions")]
    [InlineData("$$$---", "controls with no data")]
    [InlineData("#0;7;1;2;3~", "an unknown colour system")]
    [InlineData("\n\r\t   ~", "whitespace between the data")]
    public void Malformed_payloads_are_survived(string body, string what)
    {
        var terminal = Fresh();

        var exception = Record.Exception(() => WriteSixel(terminal, body));

        Assert.True(exception is null, $"{what} threw: {exception}");

        // And the parser still comes back.
        terminal.Write("OK");
        Assert.Contains("OK", terminal.GetLine(terminal.Buffer.Y));
    }

    [Fact]
    public void A_payload_split_across_writes_decodes_the_same()
    {
        var whole = Fresh();
        WriteSixel(whole, "#0;2;100;0;0!4~-#1;2;0;0;100!4~");

        var split = Fresh();
        split.Write($"{Esc}P0;1;0q#0;2;10");
        split.Write("0;0;0!4~-#1;2;0");
        split.Write($";0;100!4~{St}");

        var a = Placed(whole);
        var b = Placed(split);

        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.Equal(a!.PixelWidth, b!.PixelWidth);
        Assert.Equal(a.PixelHeight, b.PixelHeight);
        Assert.True(a.Pixels.Span.SequenceEqual(b.Pixels.Span),
            "the same payload decoded differently depending on where the write boundaries fell");
    }

    [Fact]
    public void Sixel_can_be_switched_off_entirely()
    {
        var terminal = Fresh(o => o.SixelEnabled = false);
        WriteSixel(terminal, "#0;2;100;0;0~");

        Assert.Null(Placed(terminal));
    }

    [Fact]
    public void The_tile_grid_follows_the_configured_cell_size()
    {
        // A 4x12 image over 2x3 cells covers two columns and four rows.
        var image = Decode("#0;2;100;0;0!4~-!4~", configure: o =>
        {
            o.CellWidthPixels = 2;
            o.CellHeightPixels = 3;
        });

        Assert.Equal(4, image.PixelWidth);
        Assert.Equal(12, image.PixelHeight);
        Assert.Equal(2, image.Cols);
        Assert.Equal(4, image.Rows);
    }

    [Fact]
    public void An_edge_tile_reports_only_the_pixels_it_actually_covers()
    {
        // 7 pixels wide over 2-pixel cells: four columns, the last holding a single pixel.
        var image = Decode("#0;2;100;0;0!7~", configure: o =>
        {
            o.CellWidthPixels = 2;
            o.CellHeightPixels = 3;
        });

        Assert.Equal(4, image.Cols);

        Assert.True(image.TryGetTileSource(0, 0, out var x, out var y, out var w, out var h));
        Assert.Equal((0, 0, 2, 3), (x, y, w, h));

        Assert.True(image.TryGetTileSource(3, 0, out x, out y, out w, out h));
        Assert.Equal((6, 0, 1, 3), (x, y, w, h));

        Assert.False(image.TryGetTileSource(4, 0, out _, out _, out _, out _));
    }
}
