using System.Linq;
using XTerm.Buffer;
using XTerm.Graphics;
using XTerm.Options;

namespace XTerm.Tests.Graphics;

/// <summary>
/// The behaviour that justifies keeping images on the cells rather than in an overlay: a picture
/// is terminal content, and everything that happens to text has to happen to it.
///
/// <para>Printing over a cell replaces it, erasing clears it, scrolling carries it, and falling
/// off the end of the scrollback disposes of it -- none of which needed code, because a cell is a
/// struct and every one of those paths already builds or copies whole cells. These tests exist to
/// keep it that way.</para>
/// </summary>
public class ImageCellLifetimeTests
{
    private const string Esc = "\u001b";
    private const string St = Esc + "\\";

    /// <summary>Four pixels wide, twelve tall: two cells across, four down.</summary>
    private const string TwoByFourCells = "#0;2;100;0;0!4~-!4~";

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

    private static int ImageCellCount(Terminal terminal)
    {
        int count = 0;
        for (int i = 0; i < terminal.Buffer.Lines.Length; i++)
        {
            var line = terminal.Buffer.Lines[i];
            if (line is null)
                continue;
            // Counted from the runs and clamped to the line's width, so this reports what is
            // VISIBLE — anything wider is hidden by the window, not destroyed.
            foreach (var placement in line.Placements)
            {
                var end = Math.Min(placement.EndColumn, line.Length);
                count += Math.Max(0, end - placement.Column);
            }
        }
        return count;
    }

    [Fact]
    public void Printing_over_a_tile_replaces_it_with_the_character()
    {
        var terminal = Fresh();
        WriteSixel(terminal);

        terminal.Write($"{Esc}[1;1HX");

        Assert.Null(ImageAssertions.ImageAt(terminal, 0, 0));
        Assert.Equal("X", Cell(terminal, 0, 0).Content);

        // and only that cell
        Assert.NotNull(ImageAssertions.ImageAt(terminal, 1, 0));
        Assert.NotNull(ImageAssertions.ImageAt(terminal, 0, 1));
    }

    [Fact]
    public void Erase_in_line_clears_the_tiles_on_that_row()
    {
        var terminal = Fresh();
        WriteSixel(terminal);

        terminal.Write($"{Esc}[1;1H{Esc}[K");

        Assert.Null(ImageAssertions.ImageAt(terminal, 0, 0));
        Assert.Null(ImageAssertions.ImageAt(terminal, 1, 0));
        Assert.NotNull(ImageAssertions.ImageAt(terminal, 0, 1));
    }

    [Fact]
    public void Erase_in_display_clears_every_tile_on_screen()
    {
        var terminal = Fresh();
        WriteSixel(terminal);

        terminal.Write($"{Esc}[2J");

        Assert.Equal(0, ImageCellCount(terminal));
    }

    [Fact]
    public void Erase_characters_clears_the_tiles_it_covers()
    {
        var terminal = Fresh();
        WriteSixel(terminal);

        terminal.Write($"{Esc}[1;1H{Esc}[1X");

        Assert.Null(ImageAssertions.ImageAt(terminal, 0, 0));
        Assert.NotNull(ImageAssertions.ImageAt(terminal, 1, 0));
    }

    [Fact]
    public void A_full_reset_clears_every_tile()
    {
        var terminal = Fresh();
        WriteSixel(terminal);

        terminal.Write($"{Esc}c");

        Assert.Equal(0, ImageCellCount(terminal));
    }

    [Fact]
    public void Scrolling_carries_the_tiles_with_their_lines()
    {
        var terminal = Fresh();
        WriteSixel(terminal);
        var image = ImageAssertions.ImageAt(terminal, 0, 0);
        Assert.NotNull(image);

        // The absolute line the top of the image went onto. Scrolling moves the viewport over the
        // buffer, so this index is what should stay put while the screen row changes.
        var topLine = terminal.Buffer.YBase;

        // Push the screen up by two rows from the bottom.
        terminal.Write($"{Esc}[10;1H\r\n\r\n");

        Assert.Equal(2, terminal.Buffer.YBase - topLine);

        // Nothing copied the tiles anywhere: the same line object still holds them, unchanged.
        var moved = terminal.Buffer.Lines[topLine]!;
        Assert.True(moved.TryGetImageAt(0, out var movedImage) && ReferenceEquals(movedImage, image),
            "the run did not travel with its line");
        Assert.True(moved.TryGetPlacementAt(0, out var movedPlacement));
        Assert.Equal(0, movedPlacement.SrcY);

        // Which puts the top of the picture two rows higher on screen than it was.
        Assert.True(ReferenceEquals(ImageAssertions.ImageAt(terminal, 0, -2), image));
        Assert.Equal(8, ImageCellCount(terminal));
    }

    /// <summary>
    /// The disposal story: an image dies with the last cell holding it, so a picture that scrolls
    /// out of a short scrollback leaves nothing behind to evict.
    /// </summary>
    [Fact]
    public void An_image_scrolled_out_of_the_scrollback_leaves_no_references()
    {
        var terminal = Fresh(o => o.Scrollback = 4);
        WriteSixel(terminal);
        Assert.Equal(8, ImageCellCount(terminal));

        terminal.Write($"{Esc}[10;1H");
        for (int i = 0; i < 40; i++)
            terminal.Write("\r\n");

        Assert.Equal(0, ImageCellCount(terminal));
    }

    /// <summary>
    /// A change of width keeps the pictures.
    /// </summary>
    /// <remarks>
    /// <para>This test used to assert the opposite, and the reasoning it carried — reflow re-wraps a
    /// logical line by copying ranges of cells, so tiles carried through would reassemble as a
    /// shuffled mosaic — was right about reflow and wrong about the blast radius. It applied to lines
    /// that actually re-wrap, and the code dropped every picture in the buffer on any width change at
    /// all, including widening a window, which is the most common resize there is.</para>
    /// <para>With a picture held as a run on its line rather than tiles in cells, there is nothing to
    /// shuffle: the renderer draws as much of the run as the width allows. Narrowing shows less,
    /// widening shows more, and the wrap-chain case is still dropped, on its own, below.</para>
    /// </remarks>
    [Fact]
    public void A_change_of_width_keeps_the_images()
    {
        var terminal = Fresh();
        WriteSixel(terminal);
        Assert.Equal(8, ImageCellCount(terminal));

        terminal.Resize(15, 10);
        Assert.Equal(8, ImageCellCount(terminal));

        terminal.Resize(40, 10);
        Assert.Equal(8, ImageCellCount(terminal));
    }

    /// <summary>
    /// Narrowing past a picture hides the overhang rather than destroying it, and widening brings it
    /// back — because a run keeps its natural width and only the drawing is clipped.
    /// </summary>
    [Fact]
    public void Narrowing_past_a_picture_hides_it_and_widening_restores_it()
    {
        var terminal = Fresh();
        WriteSixel(terminal);
        Assert.Equal(8, ImageCellCount(terminal));

        terminal.Resize(1, 10);          // the picture is two columns wide
        Assert.Equal(4, ImageCellCount(terminal));

        terminal.Resize(20, 10);
        Assert.Equal(8, ImageCellCount(terminal));
    }

    /// <summary>A change of height moves whole lines, so there is nothing to be confused about.</summary>
    [Fact]
    public void A_change_of_height_alone_keeps_the_images()
    {
        var terminal = Fresh();
        WriteSixel(terminal);

        terminal.Resize(20, 14);

        Assert.Equal(8, ImageCellCount(terminal));
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
    /// The backstop for the case reference counting cannot reach: a deep scrollback full of
    /// pictures, every one still referenced and every one still in memory.
    /// </summary>
    [Fact]
    public void The_oldest_images_are_dropped_when_the_budget_is_exceeded()
    {
        // Each image here is 4x12 BGRA -- 192 bytes -- so a 200 byte budget holds exactly one.
        var terminal = Fresh(o => o.MaxImageBytes = 200);

        WriteSixel(terminal);
        var first = ImageAssertions.ImageAt(terminal, 0, 0);
        Assert.NotNull(first);

        WriteSixel(terminal);
        var second = ImageAssertions.ImageAt(terminal, 0, 4);
        Assert.NotNull(second);

        Assert.Null(ImageAssertions.ImageAt(terminal, 0, 0));
        Assert.True(ReferenceEquals(ImageAssertions.ImageAt(terminal, 0, 4), second),
            "the newest image should be the one that survives");
    }

    [Fact]
    public void A_generous_budget_keeps_both_images()
    {
        var terminal = Fresh(o => o.MaxImageBytes = 64 * 1024);

        WriteSixel(terminal);
        WriteSixel(terminal);

        Assert.NotNull(ImageAssertions.ImageAt(terminal, 0, 0));
        Assert.NotNull(ImageAssertions.ImageAt(terminal, 0, 4));
    }

    /// <summary>
    /// Evicting one picture leaves another that shares its line.
    /// </summary>
    /// <remarks>
    /// Caught in review. Dropping the whole line because one of its pictures was over budget took
    /// the others with it — more destructive than the per-cell code this replaced, and it evicted
    /// images the sweep had just decided to keep.
    /// </remarks>
    [Fact]
    public void Evicting_one_image_leaves_another_on_the_same_line()
    {
        // Two 192-byte images; a 400 byte budget holds both, then a third forces one out.
        var terminal = Fresh(o => o.MaxImageBytes = 400);

        WriteSixel(terminal);
        terminal.Write($"{Esc}[1;5H");
        WriteSixel(terminal);

        var line = terminal.Buffer.Lines[terminal.Buffer.YBase]!;
        Assert.Equal(2, line.Images.Count);
        var newer = line.Images[1];

        // A third image pushes past the budget, dooming the oldest — which shares this line.
        terminal.Write($"{Esc}[10;1H");
        WriteSixel(terminal);

        Assert.True(ReferenceEquals(newer, line.Images.SingleOrDefault()),
            "the line's other picture should survive its neighbour being evicted");
    }

    /// <summary>
    /// Printing over one picture releases it, while another on the same line stays.
    /// </summary>
    /// <remarks>
    /// Caught in review. Ownership is derived from the runs, so anything that removes one has to
    /// rebuild it — waiting for the run list to empty kept a picture alive that nothing displayed,
    /// and hid it from the budget sweep, which decides what is live by walking runs.
    /// </remarks>
    [Fact]
    public void Overwriting_one_picture_releases_only_that_one()
    {
        var terminal = Fresh();

        WriteSixel(terminal);
        terminal.Write($"{Esc}[1;5H");
        WriteSixel(terminal);

        var line = terminal.Buffer.Lines[terminal.Buffer.YBase]!;
        Assert.Equal(2, line.Images.Count);
        var survivor = line.Images[1];

        // Print across the whole span of the first picture, which covers columns 0..1.
        terminal.Write($"{Esc}[1;1HXX");

        Assert.True(ReferenceEquals(survivor, line.Images.SingleOrDefault()),
            "the overwritten picture should be released and the other kept");
    }

    /// <summary>
    /// A cell under a picture is an ordinary space, and that is now correct rather than a bug.
    /// </summary>
    /// <remarks>
    /// This inverts what these tests used to assert. When a cell carried an image reference and a
    /// tile coordinate, two cells showing different pieces of a picture HAD to compare unequal,
    /// because renderers coalesce adjacent cells into one run by comparing them and merging two
    /// different tiles would have drawn the wrong thing. With pictures held as runs on the line,
    /// nothing about a picture is drawn from cells — so cells beneath one should coalesce exactly
    /// like the spaces they are, and the distinction has no work left to do.
    /// </remarks>
    [Fact]
    public void A_cell_under_a_picture_is_an_ordinary_space()
    {
        var terminal = Fresh();
        WriteSixel(terminal);

        Assert.Equal(BufferCell.Space, Cell(terminal, 0, 0));
        Assert.Equal(Cell(terminal, 0, 0), Cell(terminal, 1, 0));

        // And the picture is still there — held by the line, which is the whole point.
        Assert.NotNull(ImageAssertions.ImageAt(terminal, 0, 0));
    }

    /// <summary>
    /// What must stay distinguishable is the RUNS: each line shows its own slice of the picture.
    /// </summary>
    [Fact]
    public void Runs_on_different_lines_carry_different_slices()
    {
        var terminal = Fresh();
        WriteSixel(terminal);

        var first = ImageAssertions.PlacementAt(terminal, 0, 0);
        var second = ImageAssertions.PlacementAt(terminal, 0, 1);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first!.Value.SrcY, second!.Value.SrcY);
    }

    [Fact]
    public void The_alternate_buffer_keeps_its_own_images()
    {
        var terminal = Fresh();
        WriteSixel(terminal);

        terminal.Write($"{Esc}[?1049h"); // to the alternate screen
        Assert.Equal(0, ImageCellCount(terminal));

        terminal.Write($"{Esc}[?1049l"); // and back
        Assert.Equal(8, ImageCellCount(terminal));
    }

    /// <summary>
    /// The counterpart to every test above: a control that changes RENDITION and not text must
    /// leave the picture alone.
    /// </summary>
    /// <remarks>
    /// DECCARA sets attributes over an area and never touches a character, so a picture inside that
    /// area is not being overwritten and must not be split. Going through the ordinary text-write
    /// path made it look like one -- that path splits the placement at the column being written,
    /// which is right for printing over a picture and wrong for recolouring the cell under it.
    /// </remarks>
    [Fact]
    public void An_attribute_change_over_a_picture_leaves_it_whole()
    {
        var terminal = Fresh();
        WriteSixel(terminal);
        var image = ImageAssertions.ImageAt(terminal, 0, 0);
        Assert.NotNull(image);
        Assert.Equal(8, ImageCellCount(terminal));

        // Bold over the whole picture and then some.
        terminal.Write($"{Esc}[2*x{Esc}[1;1;4;4;1$r");

        Assert.Equal(8, ImageCellCount(terminal));
        Assert.True(ReferenceEquals(ImageAssertions.ImageAt(terminal, 0, 0), image));
        Assert.True(ReferenceEquals(ImageAssertions.ImageAt(terminal, 1, 3), image));
        Assert.True(terminal.Buffer.Lines[terminal.Buffer.YBase]![0].Attributes.IsBold());
    }

    /// <summary>
    /// And a request that changes nothing must not even write the cells back, because writing a
    /// cell back unchanged is still a write as far as the placements are concerned.
    /// </summary>
    [Fact]
    public void An_attribute_change_naming_nothing_we_implement_touches_no_cell()
    {
        var terminal = Fresh();
        WriteSixel(terminal);
        var image = ImageAssertions.ImageAt(terminal, 0, 0);
        Assert.Equal(8, ImageCellCount(terminal));

        // 31 is a colour, which DECCARA does not carry; the request names nothing this implements.
        terminal.Write($"{Esc}[2*x{Esc}[1;1;4;4;31$r");

        Assert.Equal(8, ImageCellCount(terminal));
        Assert.True(ReferenceEquals(ImageAssertions.ImageAt(terminal, 0, 0), image));
    }

    /// <summary>A DECRARA whose toggles cancel each other is the same nothing.</summary>
    [Fact]
    public void Toggles_that_cancel_leave_the_picture_and_the_rendition_alone()
    {
        var terminal = Fresh();
        WriteSixel(terminal);
        Assert.Equal(8, ImageCellCount(terminal));

        terminal.Write($"{Esc}[2*x{Esc}[1;1;4;4;1;1$t");

        Assert.Equal(8, ImageCellCount(terminal));
        Assert.False(terminal.Buffer.Lines[terminal.Buffer.YBase]![0].Attributes.IsBold());
    }
}
