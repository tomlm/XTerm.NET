using System.Linq;
using XTerm.Buffer;
using XTerm.Graphics;
using XTerm.Options;

namespace XTerm.Tests.Graphics;

/// <summary>
/// The behaviour that makes Sixel terminal content: everything that happens to text happens to the
/// pixels a Sixel painted.
///
/// <para>Each line keeps the pixels Sixel painted on it (<see cref="SixelStrip"/>), not the pictures
/// that painted them. Printing over a cell replaces its pixels, erasing clears them, a later Sixel
/// paints over them, scrolling carries them with their line, and falling off the end of the
/// scrollback disposes of them. These tests exist to keep it that way.</para>
/// </summary>
public class ImageCellLifetimeTests
{
    private const string Esc = "\u001b";
    private const string St = Esc + "\\";

    /// <summary>Four pixels wide, twelve tall, red: two cells across, four down.</summary>
    private const string TwoByFourCells = "#0;2;100;0;0!4~-!4~";

    /// <summary>The same size in blue.</summary>
    private const string BlueTwoByFour = "#0;2;0;0;100!4~-!4~";

    /// <summary>One cell across, four down, green.</summary>
    private const string GreenOneByFour = "#0;2;0;100;0!2~-!2~";

    /// <summary>Two by four, a different colour in each cell row: red, green, blue, yellow.</summary>
    private const string Striped = "#0;2;100;0;0!4F$#1;2;0;100;0!4w-#2;2;0;0;100!4F$#3;2;100;100;0!4w";

    private static readonly (byte R, byte G, byte B, byte A) Red = (255, 0, 0, 255);
    private static readonly (byte R, byte G, byte B, byte A) Green = (0, 255, 0, 255);
    private static readonly (byte R, byte G, byte B, byte A) Blue = (0, 0, 255, 255);

    private static Terminal Fresh(Action<TerminalOptions>? configure = null)
    {
        var options = new TerminalOptions
        {
            Cols = 20,
            Rows = 10,
            CellWidthPixels = 2,
            CellHeightPixels = 3
        };
        configure?.Invoke(options);
        return new Terminal(options);
    }

    private static void WriteSixel(Terminal terminal, string body = TwoByFourCells)
        => terminal.Write($"{Esc}P0;1;0q{body}{St}");

    private static BufferCell Cell(Terminal terminal, int col, int screenRow)
        => terminal.Buffer.Lines[terminal.Buffer.YBase + screenRow]![col];

    private static BufferLine Line(Terminal terminal, int screenRow)
        => terminal.Buffer.Lines[terminal.Buffer.YBase + screenRow]!;

    private static int SixelCells(Terminal terminal) => ImageAssertions.VisibleSixelCells(terminal);

    [Fact]
    public void Sixel_over_a_sixel_paints_over_it()
    {
        var terminal = Fresh();
        WriteSixel(terminal);

        terminal.Write($"{Esc}[1;1H");
        WriteSixel(terminal, BlueTwoByFour);

        // One strip per line holding the newer pixels; nothing of the older picture is kept, and
        // nothing about either becomes a run.
        Assert.Equal(Blue, ImageAssertions.SixelPixel(terminal, 0, 0));
        Assert.Equal(Blue, ImageAssertions.SixelPixel(terminal, 1, 3));
        Assert.Empty(Line(terminal, 0).Placements);
        Assert.Equal(8, SixelCells(terminal));
    }

    [Fact]
    public void Sixel_over_part_of_a_sixel_keeps_the_rest_of_it()
    {
        var terminal = Fresh();
        WriteSixel(terminal);

        // A one-cell-wide picture on the second column, over the right half of the first one
        terminal.Write($"{Esc}[1;2H");
        WriteSixel(terminal, GreenOneByFour);

        Assert.Equal(Red, ImageAssertions.SixelPixel(terminal, 0, 0));
        Assert.Equal(Green, ImageAssertions.SixelPixel(terminal, 1, 0));
        Assert.Equal(8, SixelCells(terminal));
    }

    /// <summary>
    /// The case the strips exist for: a program repainting a small region over and over keeps one
    /// strip per line, the same size it started, however many Sixels it sends.
    /// </summary>
    [Fact]
    public void Repainting_a_cell_many_times_accumulates_nothing()
    {
        var terminal = Fresh();
        WriteSixel(terminal);
        var bytes = Enumerable.Range(0, 4).Sum(row => ImageAssertions.StripOn(terminal, row)!.ByteCount);

        for (int i = 0; i < 50; i++)
        {
            terminal.Write($"{Esc}[1;2H");
            WriteSixel(terminal, i % 2 == 0 ? GreenOneByFour : "#0;2;0;0;100!2~-!2~");
        }

        for (int row = 0; row < 4; row++)
        {
            Assert.Empty(Line(terminal, row).Placements);
            Assert.Equal(Red, ImageAssertions.SixelPixel(terminal, 0, row));
            Assert.Equal(Blue, ImageAssertions.SixelPixel(terminal, 1, row));
        }

        Assert.Equal(bytes, Enumerable.Range(0, 4).Sum(row => ImageAssertions.StripOn(terminal, row)!.ByteCount));
    }

    /// <summary>
    /// Background select 1: a Sixel's unset pixels are transparent, and whatever was painted under
    /// them shows through -- which a program relies on to draw a sprite over a picture.
    /// </summary>
    [Fact]
    public void A_transparent_sixel_leaves_what_is_under_its_unset_pixels()
    {
        var terminal = Fresh();
        WriteSixel(terminal);

        // Only the top pixel row of each sixel is set ('@'): rows 1 and 2 of the first cell stay red.
        terminal.Write($"{Esc}[1;1H");
        WriteSixel(terminal, "#0;2;0;100;0!4@");

        Assert.Equal(Green, ImageAssertions.SixelPixel(terminal, 0, 0, y: 0));
        Assert.Equal(Red, ImageAssertions.SixelPixel(terminal, 0, 0, y: 1));
        Assert.Equal(Red, ImageAssertions.SixelPixel(terminal, 0, 0, y: 2));
    }

    [Fact]
    public void Printing_over_a_tile_replaces_it_with_the_character()
    {
        var terminal = Fresh();
        WriteSixel(terminal);

        terminal.Write($"{Esc}[1;1HX");

        Assert.False(ImageAssertions.IsSixelAt(terminal, 0, 0));
        Assert.Equal("X", Cell(terminal, 0, 0).Content);

        // and only that cell
        Assert.True(ImageAssertions.IsSixelAt(terminal, 1, 0));
        Assert.True(ImageAssertions.IsSixelAt(terminal, 0, 1));
    }

    [Fact]
    public void Erase_in_line_clears_the_tiles_on_that_row()
    {
        var terminal = Fresh();
        WriteSixel(terminal);

        terminal.Write($"{Esc}[1;1H{Esc}[K");

        Assert.False(ImageAssertions.IsSixelAt(terminal, 0, 0));
        Assert.False(ImageAssertions.IsSixelAt(terminal, 1, 0));
        Assert.True(ImageAssertions.IsSixelAt(terminal, 0, 1));

        // A line with nothing left drops its strip rather than keeping a blank one.
        Assert.Null(ImageAssertions.StripOn(terminal, 0));
    }

    [Fact]
    public void Erase_in_display_clears_every_tile_on_screen()
    {
        var terminal = Fresh();
        WriteSixel(terminal);

        terminal.Write($"{Esc}[2J");

        Assert.Equal(0, SixelCells(terminal));
    }

    [Fact]
    public void Erase_characters_clears_the_tiles_it_covers()
    {
        var terminal = Fresh();
        WriteSixel(terminal);

        terminal.Write($"{Esc}[1;1H{Esc}[1X");

        Assert.False(ImageAssertions.IsSixelAt(terminal, 0, 0));
        Assert.True(ImageAssertions.IsSixelAt(terminal, 1, 0));
    }

    /// <summary>
    /// An insert moves the text after the cursor right, and the picture it sits in moves with it.
    /// Pictures held as runs stayed where they were while their cells moved out from under them.
    /// </summary>
    [Fact]
    public void Inserting_characters_moves_the_pixels_with_their_cells()
    {
        var terminal = Fresh();
        WriteSixel(terminal, Striped);

        terminal.Write($"{Esc}[1;1H{Esc}[3@"); // ICH 3 on the top row

        Assert.False(ImageAssertions.IsSixelAt(terminal, 0, 0));
        Assert.False(ImageAssertions.IsSixelAt(terminal, 2, 0));
        Assert.Equal(Red, ImageAssertions.SixelPixel(terminal, 3, 0));
        Assert.Equal(Red, ImageAssertions.SixelPixel(terminal, 4, 0));

        // Only that row: the one below still starts at the left margin.
        Assert.Equal(Green, ImageAssertions.SixelPixel(terminal, 0, 1));
    }

    [Fact]
    public void Deleting_characters_moves_the_pixels_with_their_cells()
    {
        var terminal = Fresh();
        terminal.Write($"{Esc}[1;4H");
        WriteSixel(terminal, Striped);

        terminal.Write($"{Esc}[1;1H{Esc}[2P"); // DCH 2 on the top row

        Assert.Equal(Red, ImageAssertions.SixelPixel(terminal, 1, 0));
        Assert.Equal(Red, ImageAssertions.SixelPixel(terminal, 2, 0));
        Assert.False(ImageAssertions.IsSixelAt(terminal, 3, 0));
    }

    [Fact]
    public void A_full_reset_clears_every_tile()
    {
        var terminal = Fresh();
        WriteSixel(terminal);

        terminal.Write($"{Esc}c");

        Assert.Equal(0, SixelCells(terminal));
    }

    [Fact]
    public void Scrolling_carries_the_tiles_with_their_lines()
    {
        var terminal = Fresh();
        WriteSixel(terminal, Striped);

        // The absolute line the top of the image went onto. Scrolling moves the viewport over the
        // buffer, so this index is what should stay put while the screen row changes.
        var topLine = terminal.Buffer.YBase;
        var strip = terminal.Buffer.Lines[topLine]!.Sixel;
        Assert.NotNull(strip);

        // Push the screen up by two rows from the bottom.
        terminal.Write($"{Esc}[10;1H\r\n\r\n");

        Assert.Equal(2, terminal.Buffer.YBase - topLine);

        // Nothing copied the pixels anywhere: the same line object still holds the same strip.
        Assert.Same(strip, terminal.Buffer.Lines[topLine]!.Sixel);

        // Which puts the top of the picture two rows higher on screen than it was.
        Assert.Equal(Red, ImageAssertions.SixelPixel(terminal, 0, -2));
        Assert.Equal(Green, ImageAssertions.SixelPixel(terminal, 0, -1));
        Assert.Equal(8, SixelCells(terminal));
    }

    /// <summary>
    /// The disposal story: pixels die with the line holding them, so a picture that scrolls out of a
    /// short scrollback leaves nothing behind to evict.
    /// </summary>
    [Fact]
    public void An_image_scrolled_out_of_the_scrollback_leaves_no_references()
    {
        var terminal = Fresh(o => o.Scrollback = 4);
        WriteSixel(terminal);
        Assert.Equal(8, SixelCells(terminal));

        terminal.Write($"{Esc}[10;1H");
        for (int i = 0; i < 40; i++)
            terminal.Write("\r\n");

        Assert.Equal(0, SixelCells(terminal));
    }

    /// <summary>
    /// A change of width keeps the pictures: a strip keeps its natural width and the renderer draws
    /// as much of it as the line allows. Only a wrap chain drops them, because reflow moves cells
    /// between lines.
    /// </summary>
    [Fact]
    public void A_change_of_width_keeps_the_images()
    {
        var terminal = Fresh();
        WriteSixel(terminal);
        Assert.Equal(8, SixelCells(terminal));

        terminal.Resize(15, 10);
        Assert.Equal(8, SixelCells(terminal));

        terminal.Resize(40, 10);
        Assert.Equal(8, SixelCells(terminal));
    }

    /// <summary>
    /// Narrowing past a picture hides the overhang rather than destroying it, and widening brings it
    /// back.
    /// </summary>
    [Fact]
    public void Narrowing_past_a_picture_hides_it_and_widening_restores_it()
    {
        var terminal = Fresh();
        WriteSixel(terminal);
        Assert.Equal(8, SixelCells(terminal));

        terminal.Resize(1, 10);          // the picture is two columns wide
        Assert.Equal(4, SixelCells(terminal));

        terminal.Resize(20, 10);
        Assert.Equal(8, SixelCells(terminal));
    }

    /// <summary>A change of height moves whole lines, so there is nothing to be confused about.</summary>
    [Fact]
    public void A_change_of_height_alone_keeps_the_images()
    {
        var terminal = Fresh();
        WriteSixel(terminal);

        terminal.Resize(20, 14);

        Assert.Equal(8, SixelCells(terminal));
    }

    [Fact]
    public void Clearing_a_dropped_image_leaves_a_blank_rather_than_a_hole()
    {
        var terminal = Fresh();
        WriteSixel(terminal);

        terminal.Resize(15, 10);

        var cell = terminal.Buffer.Lines[terminal.Buffer.YBase]![0];
        Assert.Equal(" ", cell.Content);
        Assert.Equal(1, cell.Width);
    }

    /// <summary>
    /// The backstop for a deep scrollback full of pictures: over budget, the Sixel pixels furthest
    /// back in the history go first.
    /// </summary>
    [Fact]
    public void Scrollback_pixels_are_dropped_first_when_the_budget_is_exceeded()
    {
        // Each painted line is a 20-cell strip of 2x3 BGRA cells: 480 bytes. Two pictures of four
        // lines are 3840; 2500 holds one of them.
        var terminal = Fresh(o => o.MaxImageBytes = 2500);
        WriteSixel(terminal);
        var oldest = terminal.Buffer.Lines[terminal.Buffer.YBase]!;

        // Scroll the first picture off the screen into the scrollback, then draw a second.
        terminal.Write($"{Esc}[10;1H" + string.Concat(Enumerable.Repeat("\r\n", 10)));
        terminal.Write($"{Esc}[1;1H");
        WriteSixel(terminal);

        terminal.EnforceImageBudget();

        // Just enough goes, oldest line first: three of the old picture's four lines take the
        // buffer under budget, so the fourth -- the newest of them -- stays.
        Assert.Null(oldest.Sixel);
        Assert.Equal(8 + 2, SixelCells(terminal));
        for (int row = 0; row < 4; row++)
            Assert.True(ImageAssertions.IsSixelAt(terminal, 0, row), $"the picture on screen should survive (row {row})");
    }

    [Fact]
    public void A_generous_budget_keeps_both_images()
    {
        var terminal = Fresh(o => o.MaxImageBytes = 64 * 1024);

        WriteSixel(terminal);
        WriteSixel(terminal);
        terminal.EnforceImageBudget();

        Assert.True(ImageAssertions.IsSixelAt(terminal, 0, 0));
        Assert.True(ImageAssertions.IsSixelAt(terminal, 0, 4));
    }

    /// <summary>
    /// What is on screen is never taken: at most one screen of pixels, and it is what the user is
    /// looking at.
    /// </summary>
    [Fact]
    public void The_budget_never_takes_what_is_on_screen()
    {
        var terminal = Fresh(o => o.MaxImageBytes = 1);

        WriteSixel(terminal);
        WriteSixel(terminal);
        terminal.EnforceImageBudget();

        Assert.Equal(16, SixelCells(terminal));
    }

    /// <summary>
    /// Printing over one picture clears its pixels and leaves another on the same line alone.
    /// </summary>
    [Fact]
    public void Overwriting_one_picture_leaves_another_on_the_same_line()
    {
        var terminal = Fresh();

        WriteSixel(terminal);
        terminal.Write($"{Esc}[1;5H");
        WriteSixel(terminal, BlueTwoByFour);

        // Print across the whole span of the first picture, which covers columns 0..1.
        terminal.Write($"{Esc}[1;1HXX");

        Assert.False(ImageAssertions.IsSixelAt(terminal, 0, 0));
        Assert.False(ImageAssertions.IsSixelAt(terminal, 1, 0));
        Assert.Equal(Blue, ImageAssertions.SixelPixel(terminal, 4, 0));
        Assert.Equal(Blue, ImageAssertions.SixelPixel(terminal, 5, 0));
    }

    /// <summary>
    /// A cell under a picture is an ordinary space: nothing about a picture is drawn from cells, so
    /// cells beneath one coalesce exactly like the spaces they are.
    /// </summary>
    [Fact]
    public void A_cell_under_a_picture_is_an_ordinary_space()
    {
        var terminal = Fresh();
        WriteSixel(terminal);

        Assert.Equal(BufferCell.Space, Cell(terminal, 0, 0));
        Assert.Equal(Cell(terminal, 0, 0), Cell(terminal, 1, 0));

        // And the picture is still there -- held by the line, which is the whole point.
        Assert.True(ImageAssertions.IsSixelAt(terminal, 0, 0));
    }

    /// <summary>Each line shows its own band of the picture.</summary>
    [Fact]
    public void Lines_carry_different_bands_of_the_picture()
    {
        var terminal = Fresh();
        WriteSixel(terminal, Striped);

        Assert.Equal(Red, ImageAssertions.SixelPixel(terminal, 0, 0));
        Assert.Equal(Green, ImageAssertions.SixelPixel(terminal, 0, 1));
        Assert.Equal(Blue, ImageAssertions.SixelPixel(terminal, 0, 2));
    }

    [Fact]
    public void The_alternate_buffer_keeps_its_own_images()
    {
        var terminal = Fresh();
        WriteSixel(terminal);

        terminal.Write($"{Esc}[?1049h"); // to the alternate screen
        Assert.Equal(0, SixelCells(terminal));

        terminal.Write($"{Esc}[?1049l"); // and back
        Assert.Equal(8, SixelCells(terminal));
    }

    /// <summary>
    /// The counterpart to every test above: a control that changes RENDITION and not text must
    /// leave the picture alone.
    /// </summary>
    /// <remarks>
    /// DECCARA sets attributes over an area and never touches a character, so a picture inside that
    /// area is not being overwritten and must not be cleared.
    /// </remarks>
    [Fact]
    public void An_attribute_change_over_a_picture_leaves_it_whole()
    {
        var terminal = Fresh();
        WriteSixel(terminal);
        Assert.Equal(8, SixelCells(terminal));

        // Bold over the whole picture and then some.
        terminal.Write($"{Esc}[2*x{Esc}[1;1;4;4;1$r");

        Assert.Equal(8, SixelCells(terminal));
        Assert.Equal(Red, ImageAssertions.SixelPixel(terminal, 1, 3));
        Assert.True(terminal.Buffer.Lines[terminal.Buffer.YBase]![0].Attributes.IsBold());
    }

    /// <summary>
    /// And a request that changes nothing must not even write the cells back, because writing a
    /// cell back unchanged is still a write as far as the pixels are concerned.
    /// </summary>
    [Fact]
    public void An_attribute_change_naming_nothing_we_implement_touches_no_cell()
    {
        var terminal = Fresh();
        WriteSixel(terminal);
        Assert.Equal(8, SixelCells(terminal));

        // 31 is a colour, which DECCARA does not carry; the request names nothing this implements.
        terminal.Write($"{Esc}[2*x{Esc}[1;1;4;4;31$r");

        Assert.Equal(8, SixelCells(terminal));
    }

    /// <summary>A DECRARA whose toggles cancel each other is the same nothing.</summary>
    [Fact]
    public void Toggles_that_cancel_leave_the_picture_and_the_rendition_alone()
    {
        var terminal = Fresh();
        WriteSixel(terminal);
        Assert.Equal(8, SixelCells(terminal));

        terminal.Write($"{Esc}[2*x{Esc}[1;1;4;4;1;1$t");

        Assert.Equal(8, SixelCells(terminal));
        Assert.False(terminal.Buffer.Lines[terminal.Buffer.YBase]![0].Attributes.IsBold());
    }

    /// <summary>
    /// A copy of a line shares its pixels until one of them changes, and then each keeps its own:
    /// a frame captured for a renderer must not see the live line being painted.
    /// </summary>
    [Fact]
    public void A_copy_of_a_line_keeps_its_pixels_when_the_original_is_painted()
    {
        var terminal = Fresh();
        WriteSixel(terminal);
        var line = Line(terminal, 0);

        var clone = line.Clone();
        Assert.Same(line.Sixel, clone.Sixel);

        terminal.Write($"{Esc}[1;1H");
        WriteSixel(terminal, BlueTwoByFour);

        Assert.NotSame(line.Sixel, clone.Sixel);
        var pixels = clone.Sixel!.Pixels.Span;
        Assert.Equal((byte)255, pixels[2]); // still red: B, G, R, A
        Assert.Equal((byte)0, pixels[0]);
    }

    /// <summary>
    /// A Sixel laid out against a new cell size rescales what the line already shows, so every pixel
    /// in a strip agrees on how big a cell is.
    /// </summary>
    [Fact]
    public void A_sixel_at_a_new_cell_size_rescales_the_strip()
    {
        var terminal = Fresh();
        WriteSixel(terminal);

        terminal.Options.CellWidthPixels = 4;
        terminal.Options.CellHeightPixels = 6;
        terminal.Write($"{Esc}[1;5H");
        WriteSixel(terminal, "#0;2;0;0;100!4~");

        var strip = ImageAssertions.StripOn(terminal, 0)!;
        Assert.Equal(4, strip.CellWidth);
        Assert.Equal(6, strip.CellHeight);
        Assert.Equal(Red, ImageAssertions.SixelPixel(terminal, 0, 0, x: 3, y: 5));
        Assert.Equal(Blue, ImageAssertions.SixelPixel(terminal, 4, 0));
    }
}
