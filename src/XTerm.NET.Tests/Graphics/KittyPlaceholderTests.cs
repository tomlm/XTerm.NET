using XTerm.Options;

namespace XTerm.Tests.Graphics;

/// <summary>
/// Unicode placeholders: U+10EEEE cells that say "part of a picture belongs here", with the image
/// named by the cell's foreground colour rather than by an escape sequence.
///
/// <para>It is how image.nvim, yazi and ranger place pictures, and it works because the colour is
/// carried as data: <c>AttributeData</c> keeps 25 bits for a foreground value, so a 24-bit id
/// survives the round trip.</para>
///
/// <para>Which tile a cell shows is worked out from where it sits relative to the top-left of the
/// run. A client may instead say so outright, with combining marks from a fixed table of 297
/// characters: the first gives the row, the second the column, the third the high byte of the image
/// id. That is what lets a client write tiles in any order rather than as a rectangle in reading
/// order.</para>
/// </summary>
public class KittyPlaceholderTests
{
    private const string Esc = "\u001b";
    private const string St = Esc + "\\";
    private const string Placeholder = "\U0010EEEE";

    private static Terminal Fresh()
        => new(new TerminalOptions
        {
            Cols = 30,
            Rows = 12,
            CellWidthPixels = 2,
            CellHeightPixels = 3
        });

    private static string SolidRgba(int width, int height, byte value)
    {
        var bytes = new byte[width * height * 4];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = value;
        for (int i = 3; i < bytes.Length; i += 4)
            bytes[i] = 255;
        return Convert.ToBase64String(bytes);
    }

    /// <summary>Transmits a 4x6 picture under an id, showing nothing.</summary>
    private static Terminal WithStoredImage(uint id, byte value = 90)
    {
        var terminal = Fresh();
        terminal.Write($"{Esc}_Ga=t,i={id},f=32,s=4,v=6,q=2;{SolidRgba(4, 6, value)}{St}");
        return terminal;
    }

    /// <summary>Sets the foreground to a 24-bit value, which is where the id travels.</summary>
    private static string SelectImageId(uint id)
        => $"{Esc}[38;2;{(id >> 16) & 0xFF};{(id >> 8) & 0xFF};{id & 0xFF}m";

    private static XTerm.Buffer.BufferCell Cell(Terminal terminal, int col, int row)
        => terminal.Buffer.Lines[terminal.Buffer.YBase + row]![col];

    [Fact]
    public void A_placeholder_cell_shows_the_image_its_colour_names()
    {
        var terminal = WithStoredImage(5);

        terminal.Write(SelectImageId(5) + Placeholder);

        var tile = ImageAssertions.TileAt(terminal, 0, 0);
        Assert.True(ImageAssertions.IsImageAt(terminal, 0, 0));
        Assert.Equal(0, tile!.Value.Col);
        Assert.Equal(0, tile!.Value.Row);
    }

    /// <summary>
    /// A run of them is a rectangle, and each cell works out its tile from where it sits. Written in
    /// reading order, which is how every client that uses placeholders emits them.
    /// </summary>
    [Fact]
    public void A_run_of_placeholders_maps_to_consecutive_tiles()
    {
        var terminal = WithStoredImage(5);

        terminal.Write(SelectImageId(5));
        terminal.Write(Placeholder + Placeholder);
        terminal.Write($"{Esc}[2;1H");
        terminal.Write(Placeholder + Placeholder);

        for (int row = 0; row < 2; row++)
        {
            for (int col = 0; col < 2; col++)
            {
                Assert.True(ImageAssertions.IsImageAt(terminal, col, row),
                            $"cell ({col},{row}) holds no picture");
                Assert.Equal((col, row), ImageAssertions.TileAt(terminal, col, row));
            }
        }
    }

    /// <summary>
    /// The same picture shown twice is two rectangles, not one enormous one.
    /// </summary>
    /// <remarks>
    /// A cell only continues the run before it while it falls inside the picture. Without that
    /// bound, a second rectangle anywhere below the first kept the first one's origin, worked out
    /// its tile as the distance from it, found that outside the image, and printed the placeholder
    /// as a visible character -- so the second picture simply did not appear. It is the ordinary
    /// case: a client showing one image in two places, or redrawing a thumbnail further down.
    /// </remarks>
    [Fact]
    public void The_same_image_shown_lower_down_starts_a_new_rectangle()
    {
        var terminal = WithStoredImage(5);

        terminal.Write(SelectImageId(5));
        terminal.Write(Placeholder + Placeholder);
        terminal.Write($"{Esc}[2;1H" + Placeholder + Placeholder);

        terminal.Write($"{Esc}[6;1H" + Placeholder + Placeholder);

        Assert.True(ImageAssertions.IsImageAt(terminal, 0, 5), "the second rectangle drew nothing");
        Assert.Equal((0, 0), ImageAssertions.TileAt(terminal, 0, 5));
        Assert.Equal((1, 0), ImageAssertions.TileAt(terminal, 1, 5));
    }

    /// <summary>The same, sideways: a second copy along the row is also its own rectangle.</summary>
    [Fact]
    public void The_same_image_shown_further_along_the_row_starts_a_new_rectangle()
    {
        var terminal = WithStoredImage(5);

        terminal.Write(SelectImageId(5) + Placeholder + Placeholder);
        terminal.Write($"{Esc}[1;5H" + Placeholder + Placeholder);

        Assert.True(ImageAssertions.IsImageAt(terminal, 4, 0), "the second rectangle drew nothing");
        Assert.Equal((0, 0), ImageAssertions.TileAt(terminal, 4, 0));
        Assert.Equal((1, 0), ImageAssertions.TileAt(terminal, 5, 0));
    }

    /// <summary>All the cells of one run show the same picture, so a host uploads it once.</summary>
    [Fact]
    public void Every_cell_of_a_run_shares_one_image()
    {
        var terminal = WithStoredImage(5);

        terminal.Write(SelectImageId(5) + Placeholder + Placeholder);

        Assert.Same(ImageAssertions.ImageAt(terminal, 0, 0), ImageAssertions.ImageAt(terminal, 1, 0));
    }

    /// <summary>
    /// An id nothing was transmitted under cannot resolve, so the character stays text rather than
    /// silently becoming a blank.
    /// </summary>
    [Fact]
    public void An_unknown_id_prints_as_an_ordinary_character()
    {
        var terminal = WithStoredImage(5);

        terminal.Write(SelectImageId(999) + Placeholder);

        Assert.False(ImageAssertions.IsImageAt(terminal, 0, 0));
        Assert.Equal(Placeholder, Cell(terminal, 0, 0).Content);
    }

    /// <summary>
    /// A palette index is a colour, not an id. Only a direct colour carries one, so an ordinary
    /// coloured placeholder does not accidentally summon image number three.
    /// </summary>
    [Fact]
    public void A_palette_colour_is_not_read_as_an_id()
    {
        var terminal = WithStoredImage(3);

        terminal.Write($"{Esc}[31m" + Placeholder);   // SGR 31, palette red

        Assert.False(ImageAssertions.IsImageAt(terminal, 0, 0));
    }

    // ---- explicit tiles, stated with combining marks ----------------------------------------------

    /// <summary>
    /// The first entries of the protocol's mark table, whose INDEX is the value they stand for.
    /// </summary>
    /// <remarks>
    /// Hard-coded here so a test reads as the bytes a client would send, and cross-checked against
    /// the shipped table by <see cref="The_marks_used_here_match_the_shipped_table"/> so the two
    /// cannot drift apart.
    /// </remarks>
    private static readonly string[] Mark = { "\u0305", "\u030d", "\u030e", "\u0310", "\u0312" };

    [Fact]
    public void The_marks_used_here_match_the_shipped_table()
    {
        for (int i = 0; i < Mark.Length; i++)
        {
            Assert.True(XTerm.Graphics.PlaceholderDiacritics.TryGetValue(
                char.ConvertToUtf32(Mark[i], 0), out var value));
            Assert.Equal(i, value);
        }
    }

    /// <summary>A mark after the placeholder states the tile row outright.</summary>
    [Fact]
    public void A_row_mark_states_the_tile_row()
    {
        var terminal = WithStoredImage(5);

        terminal.Write(SelectImageId(5) + Placeholder + Mark[1]);

        var tile = ImageAssertions.TileAt(terminal, 0, 0);
        Assert.True(ImageAssertions.IsImageAt(terminal, 0, 0));
        Assert.Equal(1, tile!.Value.Row);
        Assert.Equal(0, tile!.Value.Col);
    }

    /// <summary>
    /// Row then column, which is the order the protocol fixes. This is the case position alone
    /// cannot express: one cell showing a tile from elsewhere in the picture.
    /// </summary>
    [Fact]
    public void A_row_and_column_pair_states_the_tile_outright()
    {
        var terminal = WithStoredImage(5);

        terminal.Write(SelectImageId(5) + Placeholder + Mark[1] + Mark[1]);

        var tile = ImageAssertions.TileAt(terminal, 0, 0);
        Assert.Equal(1, tile!.Value.Row);
        Assert.Equal(1, tile!.Value.Col);
    }

    /// <summary>
    /// Marks override the inferred position rather than adding to it, so a client can write tiles in
    /// any order it likes.
    /// </summary>
    [Fact]
    public void Explicit_tiles_beat_the_inferred_position()
    {
        var terminal = WithStoredImage(5);

        // Second cell of the row, told to show the tile at row 1 column 0 rather than row 0 column 1.
        terminal.Write(SelectImageId(5) + Placeholder + Placeholder + Mark[1] + Mark[0]);

        var tile = ImageAssertions.TileAt(terminal, 1, 0);
        Assert.Equal(1, tile!.Value.Row);
        Assert.Equal(0, tile!.Value.Col);
    }

    /// <summary>The marks are consumed, not drawn: nothing lands in the cell after the placeholder.</summary>
    [Fact]
    public void Marks_do_not_print_as_characters_of_their_own()
    {
        var terminal = WithStoredImage(5);

        terminal.Write(SelectImageId(5) + Placeholder + Mark[1] + Mark[1]);

        Assert.False(ImageAssertions.IsImageAt(terminal, 1, 0));
        Assert.Equal(" ", Cell(terminal, 1, 0).Content);
        Assert.Equal(1, terminal.Buffer.X);
    }

    /// <summary>
    /// A combining character outside the table is not a tile value and must not be read as one.
    /// </summary>
    /// <remarks>
    /// U+0301 is one of the accents kitty deliberately excluded when it froze the table, precisely
    /// because it is in common typographic use. Were the table not consulted -- were any combining
    /// mark taken as a row -- this would move the cell to a different tile. It is also zero width,
    /// so it does not advance the cursor; what matters is that it neither changes the tile nor gets
    /// appended to the image cell as text.
    /// </remarks>
    [Fact]
    public void A_mark_outside_the_table_is_not_read_as_a_tile()
    {
        var terminal = WithStoredImage(5);

        terminal.Write(SelectImageId(5) + Placeholder + "\u0301");

        var tile = ImageAssertions.TileAt(terminal, 0, 0);
        Assert.True(ImageAssertions.IsImageAt(terminal, 0, 0));
        Assert.Equal(0, tile!.Value.Row);
        Assert.Equal(0, tile!.Value.Col);
        Assert.Equal(" ", terminal.Buffer.Lines[terminal.Buffer.YBase]![0].Content);
    }

    /// <summary>
    /// An explicit tile outside the picture is a client error. The cell keeps what it had rather
    /// than blanking, because the input comes from another process.
    /// </summary>
    [Fact]
    public void An_out_of_range_tile_leaves_the_cell_alone()
    {
        var terminal = WithStoredImage(5);

        terminal.Write(SelectImageId(5) + Placeholder + Mark[4]);   // row 4 of a two-row picture

        var tile = ImageAssertions.TileAt(terminal, 0, 0);
        Assert.True(ImageAssertions.IsImageAt(terminal, 0, 0));
        Assert.Equal(0, tile!.Value.Row);
    }

    /// <summary>
    /// One PLACEMENT for the whole rectangle, so a delete aimed at any of its cells takes all of
    /// it. The runs are a cell wide apiece -- a mark arriving after a cell can re-tile it, and a
    /// row-wide run would have to be split and rebuilt each time -- but they share a serial, which
    /// is what makes them one picture.
    /// </summary>
    [Fact]
    public void Every_cell_of_a_run_shares_one_placement()
    {
        var terminal = WithStoredImage(5);

        terminal.Write(SelectImageId(5) + Placeholder + Placeholder);

        Assert.Equal(ImageAssertions.PlacementAt(terminal, 0, 0)!.Value.Serial,
                     ImageAssertions.PlacementAt(terminal, 1, 0)!.Value.Serial);
    }

    [Fact]
    public void Placeholders_do_nothing_when_kitty_is_switched_off()
    {
        var terminal = new Terminal(new TerminalOptions
        {
            Cols = 30,
            Rows = 12,
            CellWidthPixels = 2,
            CellHeightPixels = 3,
            KittyGraphicsEnabled = false
        });

        terminal.Write(SelectImageId(5) + Placeholder);

        Assert.False(ImageAssertions.IsImageAt(terminal, 0, 0));
    }

    /// <summary>
    /// A placeholder tile is CONTENT: the picture is there because the cell holds the placeholder
    /// character, so printing over the cell takes that tile with it and leaves the rest.
    /// </summary>
    /// <remarks>
    /// This test used to assert the opposite — that the picture survived the write — because the
    /// tile was stored as a classic Kitty overlay. That reading leaves no way to ever remove a
    /// tile: the protocol has no "delete the tile at this cell" command precisely because
    /// overwriting the cell IS the deletion. It is how kitty itself behaves and what image.nvim
    /// relies on when it redraws. Stored as an overlay, an application that drew a dialog across a
    /// picture kept the picture on top of the dialog, permanently.
    /// </remarks>
    [Fact]
    public void Printing_over_a_placeholder_cell_removes_that_tile()
    {
        var terminal = WithStoredImage(5);
        terminal.Write(SelectImageId(5) + Placeholder + Placeholder);

        terminal.Write($"{Esc}[1;1H{Esc}[39mX");

        // The character lands and only its own tile goes; the neighbour keeps showing.
        Assert.Equal("X", Cell(terminal, 0, 0).Content);
        Assert.False(ImageAssertions.IsImageAt(terminal, 0, 0));
        Assert.True(ImageAssertions.IsImageAt(terminal, 1, 0));

        // Erasing takes the rest.
        terminal.Write($"{Esc}[2J");
        Assert.False(ImageAssertions.IsImageAt(terminal, 1, 0));
    }

    /// <summary>
    /// The other direction of the split: writing one placeholder cell of a NEW picture over one
    /// cell of an old one replaces exactly that tile.
    /// </summary>
    [Fact]
    public void A_new_placeholder_over_an_old_tile_replaces_it()
    {
        var terminal = WithStoredImage(5);
        terminal.Write($"{Esc}_Ga=t,i=7,f=32,s=4,v=6,q=2;{SolidRgba(4, 6, 30)}{St}");
        terminal.Write(SelectImageId(5) + Placeholder + Placeholder);

        var oldImage = ImageAssertions.ImageAt(terminal, 0, 0);

        terminal.Write($"{Esc}[1;1H" + SelectImageId(7) + Placeholder);

        var newImage = ImageAssertions.ImageAt(terminal, 0, 0);
        Assert.NotNull(newImage);
        Assert.False(ReferenceEquals(oldImage, newImage), "the tile should now show the new picture");
        Assert.True(ReferenceEquals(oldImage, ImageAssertions.ImageAt(terminal, 1, 0)),
                    "the neighbouring tile still belongs to the old picture");
    }
}
