using XTerm.Options;

namespace XTerm.Tests.Graphics;

/// <summary>
/// Draw order, from Kitty's <c>z=</c> key.
///
/// <para>A picture is a run on a line rather than anything written into cells, so two pictures over
/// the same columns are simply two runs and the z-index says which a renderer draws last. Nothing
/// displaces anything: what was true of the cells underneath stays true.</para>
///
/// <para>That also settles what text does. A Kitty placement is an OVERLAY -- printing over one
/// leaves it alone and the z-index decides whether the glyph or the picture is on top, which is why
/// a negative z reads as "behind the text" without needing a special case anywhere. Sixel is
/// different in kind and keeps its replace-on-write, which <see cref="KittyOverlapTests"/> checks
/// from the other side.</para>
/// </summary>
public class KittyZIndexTests
{
    private const string Esc = "\u001b";
    private const string St = Esc + "\\";

    private static Terminal Fresh()
        => new(new TerminalOptions
        {
            Cols = 30,
            Rows = 12,
            CellWidthPixels = 2,
            CellHeightPixels = 3
        });

    private static string Apc(string control, string payload = "")
        => payload.Length == 0 ? $"{Esc}_G{control}{St}" : $"{Esc}_G{control};{payload}{St}";

    /// <summary>A 4x6 picture, which covers two cells by two at the metrics above.</summary>
    private static string Pixels() => Convert.ToBase64String(new byte[4 * 6 * 4]);

    /// <summary>Transmits two pictures under ids 1 and 2 so they can be told apart.</summary>
    private static Terminal WithTwoImages()
    {
        var terminal = Fresh();
        terminal.Write(Apc("a=t,i=1,f=32,s=4,v=6,q=2", Pixels()));
        terminal.Write(Apc("a=t,i=2,f=32,s=4,v=6,q=2", Pixels()));
        return terminal;
    }

    private static void PlaceAt(Terminal terminal, uint id, int col, int row, int z)
    {
        terminal.Write($"{Esc}[{row + 1};{col + 1}H");
        terminal.Write(Apc($"a=p,i={id},z={z},C=1,q=2"));
    }

    private static string Content(Terminal terminal, int col, int row)
        => terminal.Buffer.Lines[terminal.Buffer.YBase + row]![col].Content;

    // ---- ordering between pictures ----------------------------------------------------------------

    /// <summary>
    /// Printing over a classic placement leaves it whole — the z-index decides what shows.
    /// Pinned here because placeholder tiles took the OPPOSITE behaviour (they are content and a
    /// write removes the tile), and the split that implements that must not reach these.
    /// </summary>
    [Fact]
    public void Text_printed_over_a_classic_placement_leaves_it_whole()
    {
        var terminal = WithTwoImages();
        PlaceAt(terminal, 1, 0, 0, z: 0);

        terminal.Write($"{Esc}[1;1HX");

        Assert.Equal("X", Content(terminal, 0, 0));
        Assert.Single(ImageAssertions.StackAt(terminal, 0, 0));
    }

    /// <summary>The stack at a cell is ordered by z, front first.</summary>
    [Fact]
    public void The_higher_z_picture_is_in_front()
    {
        var terminal = WithTwoImages();
        PlaceAt(terminal, 1, 0, 0, z: 1);
        PlaceAt(terminal, 2, 0, 0, z: 5);

        Assert.Equal(new[] { 5, 1 },
                     ImageAssertions.StackAt(terminal, 0, 0).Select(p => (int)p.ZIndex));
    }

    /// <summary>And the order does not depend on which arrived first.</summary>
    [Fact]
    public void Order_is_by_z_not_by_arrival()
    {
        var terminal = WithTwoImages();
        PlaceAt(terminal, 1, 0, 0, z: 5);
        PlaceAt(terminal, 2, 0, 0, z: 1);

        Assert.Equal(new[] { 5, 1 },
                     ImageAssertions.StackAt(terminal, 0, 0).Select(p => (int)p.ZIndex));
    }

    /// <summary>
    /// At the same depth the newer picture is in front, which is Kitty's rule. Age is the order the
    /// runs were added to the line, so a stable sort on z alone expresses it.
    /// </summary>
    [Fact]
    public void At_equal_z_the_newer_picture_is_in_front()
    {
        var terminal = WithTwoImages();
        PlaceAt(terminal, 1, 0, 0, z: 3);
        var older = ImageAssertions.StackAt(terminal, 0, 0).Single().Serial;

        PlaceAt(terminal, 2, 0, 0, z: 3);

        var stack = ImageAssertions.StackAt(terminal, 0, 0);
        Assert.Equal(2, stack.Count);
        Assert.NotEqual(older, stack[0].Serial);
        Assert.Equal(older, stack[1].Serial);
    }

    /// <summary>
    /// Only the overlapping cells carry both. A picture beside another is one run, not two.
    /// </summary>
    [Fact]
    public void A_partly_covered_picture_is_only_stacked_where_it_overlaps()
    {
        var terminal = WithTwoImages();
        PlaceAt(terminal, 1, 0, 0, z: 1);          // columns 0-1
        PlaceAt(terminal, 2, 1, 0, z: 5);          // columns 1-2

        Assert.Single(ImageAssertions.StackAt(terminal, 0, 0));
        Assert.Equal(2, ImageAssertions.StackAt(terminal, 1, 0).Count);
        Assert.Single(ImageAssertions.StackAt(terminal, 2, 0));
    }

    // ---- against the text -------------------------------------------------------------------------

    /// <summary>A picture at negative z goes under text already on the screen and leaves it there.</summary>
    [Fact]
    public void A_negative_z_picture_keeps_the_text_it_covers()
    {
        var terminal = WithTwoImages();
        terminal.Write($"{Esc}[1;1HAB");

        PlaceAt(terminal, 1, 0, 0, z: -1);

        Assert.Equal("A", Content(terminal, 0, 0));
        Assert.Equal("B", Content(terminal, 1, 0));
        Assert.True(ImageAssertions.IsImageAt(terminal, 0, 0));
        Assert.True(ImageAssertions.IsImageAt(terminal, 1, 0));
    }

    /// <summary>And text typed onto it afterwards leaves the picture there.</summary>
    [Fact]
    public void Text_typed_over_a_negative_z_picture_keeps_it()
    {
        var terminal = WithTwoImages();
        PlaceAt(terminal, 1, 0, 0, z: -1);

        terminal.Write($"{Esc}[1;1HX");

        Assert.Equal("X", Content(terminal, 0, 0));
        Assert.Equal(-1, ImageAssertions.PlacementAt(terminal, 0, 0)!.Value.ZIndex);
    }

    /// <summary>
    /// A picture in FRONT of the text keeps it too, and this is the change of behaviour that comes
    /// with holding pictures as runs.
    /// </summary>
    /// <remarks>
    /// A Kitty placement is an overlay, not content: it hides the character while it is there and
    /// gives it back when it is deleted. Blanking the cell would make that irreversible, and it is
    /// the z-index rather than the buffer that decides which one a renderer draws on top.
    /// </remarks>
    [Fact]
    public void A_front_picture_hides_the_text_without_destroying_it()
    {
        var terminal = WithTwoImages();
        terminal.Write($"{Esc}[1;1HAB");

        PlaceAt(terminal, 1, 0, 0, z: 0);

        Assert.Equal("A", Content(terminal, 0, 0));
        Assert.Equal(0, ImageAssertions.PlacementAt(terminal, 0, 0)!.Value.ZIndex);
    }

    /// <summary>Typing over a Kitty picture does not modify it either -- it is not content.</summary>
    [Fact]
    public void Text_typed_over_a_front_picture_leaves_it()
    {
        var terminal = WithTwoImages();
        PlaceAt(terminal, 1, 0, 0, z: 0);

        terminal.Write($"{Esc}[1;1HX");

        Assert.Equal("X", Content(terminal, 0, 0));
        Assert.True(ImageAssertions.IsImageAt(terminal, 0, 0));
    }

    /// <summary>
    /// Erasing clears a picture as well as the text. Erase means the cell is blank, and a picture
    /// still showing through a cleared screen would be a leak, not a feature.
    /// </summary>
    [Fact]
    public void Erasing_clears_a_picture_too()
    {
        var terminal = WithTwoImages();
        PlaceAt(terminal, 1, 0, 0, z: -1);
        terminal.Write($"{Esc}[1;1HX");

        terminal.Write($"{Esc}[2J");

        Assert.False(ImageAssertions.IsImageAt(terminal, 0, 0));
        Assert.Equal(" ", Content(terminal, 0, 0));
    }

    /// <summary>
    /// A wide glyph occupies two cells, and the picture behind it covers both -- a run spans
    /// columns and knows nothing about what is printed across them.
    /// </summary>
    [Fact]
    public void A_wide_glyph_keeps_the_background_under_both_its_cells()
    {
        var terminal = WithTwoImages();
        PlaceAt(terminal, 1, 0, 0, z: -1);

        terminal.Write($"{Esc}[1;1H一");   // a full-width ideograph

        Assert.True(ImageAssertions.IsImageAt(terminal, 0, 0));
        Assert.True(ImageAssertions.IsImageAt(terminal, 1, 0));
    }
}
