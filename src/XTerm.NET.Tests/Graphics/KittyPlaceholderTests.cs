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
    /// A 16-colour SGR is a colour, not an id, so an ordinary red placeholder does not summon image
    /// number one.
    /// </summary>
    /// <remarks>
    /// Image ONE, because SGR 31 is index 1. This test used to store image 3, which no reading of
    /// SGR 31 could ever resolve to, so it passed whether or not the palette was rejected.
    /// </remarks>
    [Fact]
    public void A_sixteen_colour_sgr_is_not_read_as_an_id()
    {
        var terminal = WithStoredImage(1);

        terminal.Write($"{Esc}[31m" + Placeholder);   // SGR 31, palette red

        Assert.False(ImageAssertions.IsImageAt(terminal, 0, 0));
        Assert.Equal(Placeholder, Cell(terminal, 0, 0).Content);
    }

    /// <summary>
    /// The default foreground is stored as colour 256, which is a perfectly good id -- and must not
    /// be read as one, or every uncoloured placeholder would show image 256.
    /// </summary>
    [Fact]
    public void The_default_foreground_is_not_read_as_an_id()
    {
        var terminal = WithStoredImage(256);

        terminal.Write($"{Esc}[39m" + Placeholder);

        Assert.False(ImageAssertions.IsImageAt(terminal, 0, 0));
    }

    // ---- ids from the 256-colour palette, and the placement id ------------------------------------

    /// <summary>Transmits another 4x6 picture into a terminal that already has one.</summary>
    private static void Store(Terminal terminal, uint id, byte value)
        => terminal.Write($"{Esc}_Ga=t,i={id},f=32,s=4,v=6,q=2;{SolidRgba(4, 6, value)}{St}");

    /// <summary>Which stored picture a cell shows, by the byte its pixels were filled with.</summary>
    private static byte? ShownValue(Terminal terminal, int col, int row)
        => ImageAssertions.ImageAt(terminal, col, row) is { } image ? image.Pixels.Span[0] : null;

    /// <summary>
    /// The kitty spec's own form for small ids: <c>38;5;n</c>, an 8-bit id in the 256-colour
    /// palette. Index 1 is the case that matters -- the same value SGR 31 stores, told apart only by
    /// which palette named it.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(255)]
    public void A_256_colour_index_is_read_as_an_8_bit_id(int id)
    {
        var terminal = WithStoredImage((uint)id);

        terminal.Write($"{Esc}[38;5;{id}m" + Placeholder);

        Assert.True(ImageAssertions.IsImageAt(terminal, 0, 0), $"38;5;{id} did not name image {id}");
        Assert.Equal((0, 0), ImageAssertions.TileAt(terminal, 0, 0));
    }

    /// <summary>
    /// The third mark is the id's most significant byte, over an 8-bit id as over a 24-bit one.
    /// This is the spec's own example: <c>38;5;42</c> and U+030E (2) name 42 + (2 &lt;&lt; 24).
    /// </summary>
    [Fact]
    public void A_third_mark_adds_the_high_byte_to_an_8_bit_id()
    {
        const uint extendedId = 42 + (2u << 24);
        var terminal = WithStoredImage(42, value: 90);
        Store(terminal, extendedId, value: 30);

        terminal.Write($"{Esc}[38;5;42m" + Placeholder + Mark[0] + Mark[0] + Mark[2]);

        Assert.Equal((byte)30, ShownValue(terminal, 0, 0));
    }

    // ---- ids that need the high byte to name anything ---------------------------------------------

    private static readonly string CrLf = "\r\n";

    /// <summary>
    /// The spec's own example, with ONLY the extended image stored. The colour alone names nothing,
    /// so the cell cannot be judged until the third mark arrives; a client choosing random 32-bit
    /// ids is in this position almost every time.
    /// </summary>
    [Fact]
    public void The_spec_example_shows_an_image_only_the_high_byte_names()
    {
        var terminal = Fresh();
        Store(terminal, 42 + (2u << 24), value: 30);

        terminal.Write($"{Esc}[38;5;42m"
                       + Placeholder + Mark[0] + Mark[0] + Mark[2]
                       + Placeholder + Mark[0] + Mark[1] + Mark[2] + CrLf
                       + Placeholder + Mark[1] + Mark[0] + Mark[2]
                       + Placeholder + Mark[1] + Mark[1] + Mark[2] + CrLf);

        for (int row = 0; row < 2; row++)
        {
            for (int col = 0; col < 2; col++)
            {
                Assert.True(ImageAssertions.IsImageAt(terminal, col, row), $"cell ({col},{row}) holds no picture");
                Assert.Equal((col, row), ImageAssertions.TileAt(terminal, col, row));
                Assert.Equal((byte)30, ShownValue(terminal, col, row));
                Assert.Equal(" ", Cell(terminal, col, row).Content);
            }
        }
    }

    /// <summary>The same with a 24-bit id: the third mark tops up all three colour bytes.</summary>
    [Fact]
    public void A_third_mark_completes_a_24_bit_id_only_it_names()
    {
        var terminal = Fresh();
        Store(terminal, (3u << 24) | 0x0A0B0C, value: 30);

        terminal.Write(SelectImageId(0x0A0B0C) + Placeholder + Mark[0] + Mark[1] + Mark[3]);

        Assert.Equal((byte)30, ShownValue(terminal, 0, 0));
        Assert.Equal((1, 0), ImageAssertions.TileAt(terminal, 0, 0));
        Assert.Equal(1, terminal.Buffer.X);
    }

    /// <summary>
    /// A rectangle whose first cell only resolves on its third mark. The cells after it leave the
    /// top byte out, as kitty's left-neighbour rule allows: bare, or with the row and column alone.
    /// They inherit it, carry on from where the marks put the first cell, and are one placement.
    /// </summary>
    [Fact]
    public void A_run_inherits_the_high_byte_from_the_cell_that_resolved_on_it()
    {
        var terminal = Fresh();
        Store(terminal, 42 + (2u << 24), value: 30);

        terminal.Write($"{Esc}[38;5;42m"
                       + Placeholder + Mark[0] + Mark[0] + Mark[2]
                       + Placeholder + CrLf
                       + Placeholder + Mark[1] + Mark[0] + Mark[2]
                       + Placeholder + Mark[1] + Mark[1]);

        for (int row = 0; row < 2; row++)
        {
            for (int col = 0; col < 2; col++)
            {
                Assert.Equal((byte)30, ShownValue(terminal, col, row));
                Assert.Equal((col, row), ImageAssertions.TileAt(terminal, col, row));
            }
        }

        Assert.Equal(ImageAssertions.PlacementAt(terminal, 0, 0)!.Value.Serial,
                     ImageAssertions.PlacementAt(terminal, 1, 0)!.Value.Serial);
    }

    /// <summary>
    /// The marks still place a cell that resolves late: here the first cell of a run claims tile
    /// (1,1), and the next cell continues the rectangle from there rather than from (0,0).
    /// </summary>
    [Fact]
    public void A_late_resolving_cell_keeps_the_tile_its_marks_named()
    {
        var terminal = Fresh();
        Store(terminal, 42 + (2u << 24), value: 30);
        terminal.Write($"{Esc}_Ga=t,i={42 + (2u << 24) + 1},f=32,s=6,v=6,q=2;{SolidRgba(6, 6, 50)}{St}");

        // A 3x2 picture this time, so a second column exists to the right of tile (1,1).
        terminal.Write($"{Esc}[38;5;43m" + Placeholder + Mark[1] + Mark[1] + Mark[2] + Placeholder);

        Assert.Equal((1, 1), ImageAssertions.TileAt(terminal, 0, 0));
        Assert.Equal((2, 1), ImageAssertions.TileAt(terminal, 1, 0));
        Assert.Equal((byte)50, ShownValue(terminal, 1, 0));
    }

    /// <summary>
    /// A cell that never resolves -- no third mark, a full id nothing was stored under, or a
    /// non-table mark in the way -- is exactly what an unknown id always was: the placeholder and
    /// its marks as text. A terminal with kitty switched off is the oracle, since there the
    /// character is never anything but text.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("̅̅")]               // row and column, no top byte
    [InlineData("̅̅̐")]         // top byte 3: 42 + (3 << 24) is unknown too
    [InlineData("̅́̎")]         // U+0301 is not in the table, so the run of marks ends
    public void A_cell_that_never_resolves_is_left_as_text(string marks)
    {
        var terminal = Fresh();
        Store(terminal, 42 + (2u << 24), value: 30);
        var oracle = new Terminal(new TerminalOptions
        {
            Cols = 30, Rows = 12, CellWidthPixels = 2, CellHeightPixels = 3, KittyGraphicsEnabled = false
        });

        var text = $"{Esc}[38;5;42m" + Placeholder + marks + "x" + Mark[1] + "y";
        terminal.Write(text);
        oracle.Write(text);

        Assert.False(ImageAssertions.IsImageAt(terminal, 0, 0));
        for (int col = 0; col < 4; col++)
        {
            Assert.Equal(Cell(oracle, col, 0).Content, Cell(terminal, col, 0).Content);
            Assert.Equal(Cell(oracle, col, 0).Width, Cell(terminal, col, 0).Width);
        }
        Assert.Equal(oracle.Buffer.X, terminal.Buffer.X);
    }

    /// <summary>Without the third mark the same cell is image 42 itself.</summary>
    [Fact]
    public void An_8_bit_id_without_a_third_mark_is_just_the_index()
    {
        var terminal = WithStoredImage(42, value: 90);
        Store(terminal, 42 + (2u << 24), value: 30);

        terminal.Write($"{Esc}[38;5;42m" + Placeholder + Mark[0] + Mark[0]);

        Assert.Equal((byte)90, ShownValue(terminal, 0, 0));
    }

    /// <summary>
    /// A 24-bit id keeps all three of its bytes: reading <c>38;2</c> as if it were an index would
    /// land on image 3 here, which is stored precisely so that mistake would show.
    /// </summary>
    [Fact]
    public void A_24_bit_id_uses_every_byte_of_the_colour()
    {
        var terminal = WithStoredImage(0x010203, value: 90);
        Store(terminal, 3, value: 30);

        terminal.Write(SelectImageId(0x010203) + Placeholder);

        Assert.Equal((byte)90, ShownValue(terminal, 0, 0));
    }

    /// <summary>The placement id rides in the underline colour, as a 256-colour index...</summary>
    [Fact]
    public void A_256_colour_underline_names_the_placement()
    {
        var terminal = WithStoredImage(42);

        terminal.Write($"{Esc}[38;5;42;58;5;7m" + Placeholder);

        Assert.Equal(7, ImageAssertions.PlacementAt(terminal, 0, 0)!.Value.PlacementId);
    }

    /// <summary>...or as 24 bits of RGB.</summary>
    [Fact]
    public void An_rgb_underline_names_the_placement()
    {
        var terminal = WithStoredImage(5);

        terminal.Write(SelectImageId(5) + $"{Esc}[58;2;1;2;3m" + Placeholder);

        Assert.Equal(0x010203, ImageAssertions.PlacementAt(terminal, 0, 0)!.Value.PlacementId);
    }

    /// <summary>No underline colour is placement zero: "any placement of the image".</summary>
    [Fact]
    public void Without_an_underline_colour_the_placement_id_is_zero()
    {
        var terminal = WithStoredImage(5);

        terminal.Write(SelectImageId(5) + Placeholder);

        Assert.Equal(0, ImageAssertions.PlacementAt(terminal, 0, 0)!.Value.PlacementId);
    }

    /// <summary>
    /// Two placements of one image side by side are two rectangles, each starting at its own first
    /// tile, and a delete naming one placement leaves the other.
    /// </summary>
    [Fact]
    public void A_placement_id_separates_two_copies_of_one_image()
    {
        var terminal = WithStoredImage(5);

        terminal.Write(SelectImageId(5) + $"{Esc}[58;5;7m" + Placeholder);
        terminal.Write($"{Esc}[58;5;9m" + Placeholder);

        Assert.Equal((0, 0), ImageAssertions.TileAt(terminal, 0, 0));
        Assert.Equal((0, 0), ImageAssertions.TileAt(terminal, 1, 0));

        terminal.Write($"{Esc}_Ga=d,d=i,i=5,p=7,q=2{St}");

        Assert.False(ImageAssertions.IsImageAt(terminal, 0, 0), "placement 7 was not deleted");
        Assert.True(ImageAssertions.IsImageAt(terminal, 1, 0), "placement 9 went with it");
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
