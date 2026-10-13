using XTerm.Options;

namespace XTerm.Tests;

/// <summary>
/// Deleting a kitty placement by its ids, once the registry has trimmed the image it shows.
/// </summary>
/// <remarks>
/// A placement holds its own pixels, so trimming an image from the registry leaves its appearances
/// on screen; the client still means them when it names the id. Consolonia drags a dialog across a
/// picture by placing a new block image for every block along the dialog's edges each frame, which
/// turns the registry over in seconds, and every picture the dialog then covered stayed on screen.
/// </remarks>
public class KittyPlacementDeleteTests
{
    private const string Esc = "\u001b";
    private const string St = Esc + "\\";

    /// <summary>A 256x256 RGBA image: 262,144 bytes, placed into one cell by c=1,r=1 below.</summary>
    private static string Pixels(byte value)
    {
        var bytes = new byte[256 * 256 * 4];
        Array.Fill(bytes, value);
        return Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// 20x5 cells of 8x16 pixels: one screen is 51,200 bytes. The registry holds at least three
    /// screens (153,600 bytes) whatever the option says, so the budgets below are set above that to
    /// be the ones in force, and the images are sized so a few of them fill one.
    /// </summary>
    private static Terminal NewTerminal(long registryBudget) =>
        new(new TerminalOptions
        {
            Cols = 20,
            Rows = 5,
            CellWidthPixels = 8,
            CellHeightPixels = 16,
            MaxImageRegistryBytes = registryBudget,
        });

    private static void PlaceAt(Terminal terminal, uint image, uint placement, int col, int row)
    {
        terminal.Write($"{Esc}[{row + 1};{col + 1}H");
        terminal.Write($"{Esc}_Ga=p,i={image},p={placement},c=1,r=1,C=1,q=2{St}");
    }

    private static bool ShowsImage(Terminal terminal, int col, int row)
        => terminal.Buffer.Lines[row]!.TryGetImageAt(col, out _);

    [Fact]
    public void A_placement_is_deleted_by_its_ids_after_the_registry_trimmed_its_image()
    {
        // room for one 262,144-byte image: the second transmit trims the first
        var terminal = NewTerminal(registryBudget: 300_000);
        terminal.Write($"{Esc}_Ga=t,f=32,s=256,v=256,i=1,q=2;{Pixels(1)}{St}");
        PlaceAt(terminal, image: 1, placement: 1, col: 2, row: 1);
        Assert.True(ShowsImage(terminal, 2, 1), "sanity: the placement is on screen");

        terminal.Write($"{Esc}_Ga=t,f=32,s=256,v=256,i=2,q=2;{Pixels(2)}{St}");
        Assert.True(ShowsImage(terminal, 2, 1), "trimming the image from the registry leaves its appearance");

        terminal.Write($"{Esc}_Ga=d,d=i,i=1,p=1,q=2{St}");

        Assert.False(ShowsImage(terminal, 2, 1), "the client deleted the placement by its ids");
    }

    [Fact]
    public void Deleting_a_trimmed_image_drops_its_placements()
    {
        var terminal = NewTerminal(registryBudget: 300_000);
        terminal.Write($"{Esc}_Ga=t,f=32,s=256,v=256,i=1,q=2;{Pixels(1)}{St}");
        PlaceAt(terminal, image: 1, placement: 1, col: 0, row: 0);
        PlaceAt(terminal, image: 1, placement: 2, col: 5, row: 2);
        terminal.Write($"{Esc}_Ga=t,f=32,s=256,v=256,i=2,q=2;{Pixels(2)}{St}");

        terminal.Write($"{Esc}_Ga=d,d=I,i=1,q=2{St}");

        Assert.False(ShowsImage(terminal, 0, 0));
        Assert.False(ShowsImage(terminal, 5, 2));
    }

    /// <summary>
    /// An image on screen is held by its cells, so trimming it from the registry saves nothing and
    /// breaks the client that places it again: trimming leaves it alone and takes an image nothing
    /// shows, even a newer one.
    /// </summary>
    [Fact]
    public void Trimming_leaves_an_image_on_screen_alone()
    {
        var terminal = NewTerminal(registryBudget: 300_000);
        terminal.Write($"{Esc}_Ga=t,f=32,s=256,v=256,i=1,q=2;{Pixels(1)}{St}");
        PlaceAt(terminal, image: 1, placement: 1, col: 2, row: 1);
        terminal.Write($"{Esc}_Ga=t,f=32,s=256,v=256,i=2,q=2;{Pixels(2)}{St}");   // unshown
        terminal.Write($"{Esc}_Ga=t,f=32,s=256,v=256,i=3,q=2;{Pixels(3)}{St}");   // over budget: 2 goes, 1 stays

        PlaceAt(terminal, image: 1, placement: 2, col: 4, row: 1);
        Assert.True(ShowsImage(terminal, 4, 1), "the image on screen can still be placed again");

        PlaceAt(terminal, image: 2, placement: 3, col: 6, row: 1);
        Assert.False(ShowsImage(terminal, 6, 1), "the image nothing showed was the one trimmed");

        PlaceAt(terminal, image: 3, placement: 4, col: 8, row: 1);
        Assert.True(ShowsImage(terminal, 8, 1), "the image just stored is never the one trimmed");
    }

    /// <summary>Placing an image again makes it the most recently used, so trimming takes another first.</summary>
    [Fact]
    public void Trimming_takes_the_least_recently_used_image()
    {
        var terminal = NewTerminal(registryBudget: 600_000);
        terminal.Write($"{Esc}_Ga=t,f=32,s=256,v=256,i=1,q=2;{Pixels(1)}{St}");
        terminal.Write($"{Esc}_Ga=t,f=32,s=256,v=256,i=2,q=2;{Pixels(2)}{St}");
        PlaceAt(terminal, image: 1, placement: 1, col: 0, row: 0);
        terminal.Write($"{Esc}_Ga=d,d=i,i=1,p=1,q=2{St}");                     // 1 was used, nothing shows it now
        terminal.Write($"{Esc}_Ga=t,f=32,s=256,v=256,i=3,q=2;{Pixels(3)}{St}");   // over budget: 2, untouched since stored, goes

        PlaceAt(terminal, image: 1, placement: 2, col: 2, row: 0);
        PlaceAt(terminal, image: 2, placement: 3, col: 4, row: 0);

        Assert.True(ShowsImage(terminal, 2, 0), "the image used since it was stored stays");
        Assert.False(ShowsImage(terminal, 4, 0), "the image never used since it was stored goes");
    }

    /// <summary>
    /// "When re-transmitting image data for a specific id, the existing image and all its placements
    /// must be deleted." A client that shows a picture in blocks sends a changed block again under its
    /// id and places it anew; a placement of the old pixels that the new ones do not cover must not stay.
    /// </summary>
    [Fact]
    public void Retransmitting_an_id_drops_the_placements_of_the_image_it_replaces()
    {
        var terminal = NewTerminal(registryBudget: 0);
        terminal.Write($"{Esc}_Ga=t,f=32,s=256,v=256,i=1,q=2;{Pixels(1)}{St}");
        PlaceAt(terminal, image: 1, placement: 1, col: 0, row: 0);
        PlaceAt(terminal, image: 1, placement: 2, col: 4, row: 0);

        terminal.Write($"{Esc}_Ga=t,f=32,s=256,v=256,i=1,q=2;{Pixels(2)}{St}");
        PlaceAt(terminal, image: 1, placement: 1, col: 0, row: 0);

        Assert.True(ShowsImage(terminal, 0, 0), "the placement made after the retransmit shows");
        Assert.False(ShowsImage(terminal, 4, 0), "the old image's other placement went with it");
    }

    [Fact]
    public void A_placement_of_an_image_still_in_the_registry_is_deleted_as_before()
    {
        var terminal = NewTerminal(registryBudget: 0);
        terminal.Write($"{Esc}_Ga=t,f=32,s=256,v=256,i=1,q=2;{Pixels(1)}{St}");
        PlaceAt(terminal, image: 1, placement: 1, col: 3, row: 3);
        PlaceAt(terminal, image: 1, placement: 2, col: 6, row: 3);

        terminal.Write($"{Esc}_Ga=d,d=i,i=1,p=1,q=2{St}");

        Assert.False(ShowsImage(terminal, 3, 3), "the named placement goes");
        Assert.True(ShowsImage(terminal, 6, 3), "the other placement of the image stays");
    }
}
