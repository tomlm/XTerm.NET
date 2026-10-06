using XTerm.Options;

namespace XTerm.Tests.Graphics;

/// <summary>
/// Placing with a placement id the image already has replaces that appearance.
///
/// <para>This is the protocol's move operation. A client that animates a picture across the screen
/// -- notcurses does it for every sprite -- re-sends <c>a=p</c> with the same <c>i=</c> and
/// <c>p=</c> at each new position and never sends a delete. A terminal that adds a placement
/// every time leaves a copy behind at each step, and one sprite becomes a trail of them.</para>
///
/// <para>The other half matters as much: a placement with NO id is anonymous and must keep
/// accumulating, because that is how one transmitted image is shown in many places.</para>
/// </summary>
public class KittyPlacementReplaceTests
{
    private const string Esc = "\u001b";
    private const string St = Esc + "\\";

    private const int CellPixelWidth = 2;
    private const int CellPixelHeight = 3;

    /// <summary>Cells one placement of the 4x6 picture covers: two columns by two rows.</summary>
    private const int CellsPerPlacement = 4;

    private static Terminal Fresh()
        => new(new TerminalOptions
        {
            Cols = 30,
            Rows = 12,
            CellWidthPixels = CellPixelWidth,
            CellHeightPixels = CellPixelHeight
        });

    private static string Apc(string control, string payload = "")
        => payload.Length == 0 ? $"{Esc}_G{control}{St}" : $"{Esc}_G{control};{payload}{St}";

    /// <summary>A solid 4x6 picture, which covers two cells by two at the metrics above.</summary>
    private static string Pixels(int width = 4, int height = 6)
        => Convert.ToBase64String(new byte[width * height * 4]);

    private static bool HasImage(Terminal terminal, int col, int screenRow)
        => ImageAssertions.IsImageAt(terminal, col, screenRow);

    /// <summary>How many cells anywhere on screen show a picture, counting overlaps once each.</summary>
    private static int TileCount(Terminal terminal)
    {
        int count = 0;
        for (int row = 0; row < terminal.Rows; row++)
        {
            foreach (var placement in ImageAssertions.PlacementsOn(terminal, row))
            {
                var end = Math.Min(placement.EndColumn, terminal.Cols);
                count += Math.Max(0, end - placement.Column);
            }
        }
        return count;
    }

    /// <summary>
    /// The move itself. Both rows of the old appearance must go, including the one the new
    /// placement does not touch -- a run is line-local, so "replace what is under the new one"
    /// would leave the rest of the old picture standing.
    /// </summary>
    [Fact]
    public void Placing_again_with_the_same_placement_id_moves_the_picture()
    {
        var terminal = Fresh();
        terminal.Write(Apc("a=t,i=7,f=32,s=4,v=6,q=2", Pixels()));
        terminal.Write($"{Esc}[1;1H" + Apc("a=p,i=7,p=1,q=2"));

        terminal.Write($"{Esc}[5;10H" + Apc("a=p,i=7,p=1,q=2"));

        Assert.False(HasImage(terminal, 0, 0), "the old position's first row should be clear");
        Assert.False(HasImage(terminal, 0, 1), "the old position's second row should be clear");
        Assert.True(HasImage(terminal, 9, 4), "the picture should be at the new position");
        Assert.True(HasImage(terminal, 9, 5));
        Assert.Equal(CellsPerPlacement, TileCount(terminal));
    }

    /// <summary>
    /// What notcurses does to a sprite: many steps, one cell apart, overlapping the step before.
    /// One picture must be left at the end however many steps it took to get there.
    /// </summary>
    [Fact]
    public void A_picture_stepped_across_the_screen_leaves_no_trail()
    {
        var terminal = Fresh();
        terminal.Write(Apc("a=t,i=7,f=32,s=4,v=6,q=2", Pixels()));

        for (int col = 1; col <= 20; col++)
            terminal.Write($"{Esc}[3;{col}H" + Apc("a=p,i=7,p=1,q=2"));

        Assert.Equal(CellsPerPlacement, TileCount(terminal));
        Assert.False(HasImage(terminal, 0, 2), "the first step should not have been left behind");
        Assert.True(HasImage(terminal, 19, 2), "the picture should be where the last step put it");
    }

    /// <summary>
    /// No placement id means an anonymous appearance, and those accumulate. Replacing here would
    /// break every client that transmits once and stamps the picture down in several places.
    /// </summary>
    [Fact]
    public void Placements_without_an_id_accumulate()
    {
        var terminal = Fresh();
        terminal.Write(Apc("a=t,i=7,f=32,s=4,v=6,q=2", Pixels()));

        terminal.Write($"{Esc}[1;1H" + Apc("a=p,i=7,q=2"));
        terminal.Write($"{Esc}[5;10H" + Apc("a=p,i=7,q=2"));

        Assert.True(HasImage(terminal, 0, 0), "the first anonymous placement should have stayed");
        Assert.True(HasImage(terminal, 9, 4));
        Assert.Equal(2 * CellsPerPlacement, TileCount(terminal));
    }

    /// <summary>
    /// The id is what makes two appearances distinct, so a different one is a second picture.
    /// </summary>
    [Fact]
    public void A_different_placement_id_is_a_second_appearance()
    {
        var terminal = Fresh();
        terminal.Write(Apc("a=t,i=7,f=32,s=4,v=6,q=2", Pixels()));

        terminal.Write($"{Esc}[1;1H" + Apc("a=p,i=7,p=1,q=2"));
        terminal.Write($"{Esc}[5;10H" + Apc("a=p,i=7,p=2,q=2"));

        Assert.True(HasImage(terminal, 0, 0));
        Assert.True(HasImage(terminal, 9, 4));
        Assert.Equal(2 * CellsPerPlacement, TileCount(terminal));
    }

    /// <summary>
    /// Placement ids are scoped to their image. Two images each using p=1 -- which is what a
    /// client with a fixed placement id per sprite sends -- must not replace one another.
    /// </summary>
    [Fact]
    public void The_same_placement_id_on_another_image_is_left_alone()
    {
        var terminal = Fresh();
        terminal.Write(Apc("a=t,i=7,f=32,s=4,v=6,q=2", Pixels()));
        terminal.Write(Apc("a=t,i=8,f=32,s=4,v=6,q=2", Pixels()));

        terminal.Write($"{Esc}[1;1H" + Apc("a=p,i=7,p=1,q=2"));
        terminal.Write($"{Esc}[5;10H" + Apc("a=p,i=8,p=1,q=2"));

        Assert.True(HasImage(terminal, 0, 0), "image 7's placement should not have been replaced");
        Assert.True(HasImage(terminal, 9, 4));
        Assert.Equal(2 * CellsPerPlacement, TileCount(terminal));
    }

    /// <summary>
    /// Replacing an appearance is not deleting the image: the pixels stay stored, so the picture
    /// can be placed again afterwards under any id.
    /// </summary>
    [Fact]
    public void Replacing_a_placement_keeps_the_stored_image()
    {
        var terminal = Fresh();
        terminal.Write(Apc("a=t,i=7,f=32,s=4,v=6,q=2", Pixels()));
        terminal.Write($"{Esc}[1;1H" + Apc("a=p,i=7,p=1,q=2"));
        terminal.Write($"{Esc}[5;10H" + Apc("a=p,i=7,p=1,q=2"));

        terminal.Write($"{Esc}[9;20H" + Apc("a=p,i=7,p=2,q=2"));

        Assert.True(HasImage(terminal, 9, 4));
        Assert.True(HasImage(terminal, 19, 8));
        Assert.Equal(2 * CellsPerPlacement, TileCount(terminal));
    }
}
