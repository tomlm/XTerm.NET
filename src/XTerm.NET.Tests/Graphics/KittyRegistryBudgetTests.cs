using XTerm.Options;

namespace XTerm.Tests.Graphics;

/// <summary>
/// The kitty image registry keeps at least three screens' worth of pixels whatever
/// <see cref="TerminalOptions.MaxImageRegistryBytes"/> says: a client that tiles full-screen pictures
/// keeps that many tiles in the terminal to show again, and one it could not hold would place as
/// nothing.
/// </summary>
public class KittyRegistryBudgetTests
{
    private const string Esc = "\u001b";
    private const string St = Esc + "\\";

    // 30x12 cells of 2x3 pixels: one screen is 8,640 bytes, three are 25,920
    private static Terminal Fresh(long registryBytes) => new(new TerminalOptions
    {
        Cols = 30,
        Rows = 12,
        CellWidthPixels = 2,
        CellHeightPixels = 3,
        MaxImageRegistryBytes = registryBytes
    });

    private static string Apc(string control, string payload = "")
        => payload.Length == 0 ? $"{Esc}_G{control}{St}" : $"{Esc}_G{control};{payload}{St}";

    private static string SolidRgba(int width, int height)
        => Convert.ToBase64String(new byte[width * height * 4]);

    [Fact]
    public void An_image_within_three_screens_is_kept_however_small_the_configured_budget()
    {
        var terminal = Fresh(registryBytes: 1);

        terminal.Write(Apc("a=t,i=7,f=32,s=4,v=6,q=2", SolidRgba(4, 6)));   // 96 bytes
        terminal.Write(Apc("a=p,i=7,q=2"));

        Assert.True(ImageAssertions.IsImageAt(terminal, 0, 0), "the image was still in the registry to place");
    }

    [Fact]
    public void An_image_beyond_three_screens_is_still_dropped_under_a_small_budget()
    {
        var terminal = Fresh(registryBytes: 1);

        terminal.Write(Apc("a=t,i=8,f=32,s=100,v=100,q=2", SolidRgba(100, 100)));   // 40,000 bytes
        terminal.Write(Apc("a=p,i=8,q=2"));

        Assert.False(ImageAssertions.IsImageAt(terminal, 0, 0), "three screens are 25,920 bytes; it did not fit");
    }

    [Fact]
    public void A_configured_budget_larger_than_three_screens_still_applies()
    {
        var terminal = Fresh(registryBytes: 1024 * 1024);

        terminal.Write(Apc("a=t,i=9,f=32,s=100,v=100,q=2", SolidRgba(100, 100)));   // 40,000 bytes
        terminal.Write(Apc("a=p,i=9,q=2"));

        Assert.True(ImageAssertions.IsImageAt(terminal, 0, 0));
    }
}
