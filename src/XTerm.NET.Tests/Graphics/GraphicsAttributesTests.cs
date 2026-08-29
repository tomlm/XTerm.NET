using XTerm.Options;

namespace XTerm.Tests.Graphics;

/// <summary>
/// XTSMGRAPHICS -- "CSI ? Pi ; Pa ; Pv S" -- and the bug it used to trigger.
///
/// <para>It shares its final character with SCROLL UP, and the CSI identifier used to have its
/// private marker stripped before the command was looked up, so the query was routed to the scroll
/// handler. Every Sixel-capable program sends one while working out what the terminal can do, which
/// made "the screen jumps when I run img2sixel" the visible symptom of a capability query going
/// unanswered. "?S" is now its own entry in the command table.</para>
/// </summary>
public class GraphicsAttributesTests
{
    private const string Esc = "\u001b";

    private static Terminal Fresh() => new(new TerminalOptions
    {
        Cols = 40,
        Rows = 6,
        CellWidthPixels = 10,
        CellHeightPixels = 20
    });

    private static (Terminal Terminal, List<string> Replies) Listening()
    {
        var terminal = Fresh();
        var replies = new List<string>();
        terminal.DataReceived += (_, e) => replies.Add(e.Data);
        return (terminal, replies);
    }

    /// <summary>The regression: a capability query must not move the screen.</summary>
    [Theory]
    [InlineData("1;1;0", "colour register count")]
    [InlineData("2;1;0", "Sixel geometry")]
    [InlineData("1;4;0", "maximum colour registers")]
    public void A_graphics_query_does_not_scroll_the_screen(string parameters, string what)
    {
        var (terminal, _) = Listening();
        terminal.Write("top line\r\nsecond line");

        terminal.Write($"{Esc}[?{parameters}S");

        Assert.True(terminal.GetLine(terminal.Buffer.YBase) == "top line",
            $"querying {what} scrolled the screen instead of answering");
    }

    [Fact]
    public void The_colour_register_count_is_reported()
    {
        var (terminal, replies) = Listening();

        terminal.Write($"{Esc}[?1;1;0S");

        Assert.Equal($"{Esc}[?1;0;256S", Assert.Single(replies));
    }

    [Fact]
    public void The_sixel_geometry_is_reported()
    {
        var (terminal, replies) = Listening();

        terminal.Write($"{Esc}[?2;1;0S");

        // 40 columns of 10 pixels, and whatever height the pixel budget allows across that width.
        var reply = Assert.Single(replies);
        Assert.StartsWith($"{Esc}[?2;0;400;", reply);
        Assert.EndsWith("S", reply);
    }

    /// <summary>
    /// The reported geometry has to be a size we would actually accept, or a program that sizes an
    /// image to fit gets one we then throw away.
    /// </summary>
    [Fact]
    public void The_reported_geometry_fits_within_the_pixel_budget()
    {
        var (terminal, replies) = Listening();

        terminal.Write($"{Esc}[?2;1;0S");

        var parts = replies[0].TrimEnd('S').Split(';');
        var width = int.Parse(parts[^2]);
        var height = int.Parse(parts[^1]);

        Assert.True((long)width * height <= terminal.Options.MaxSixelPixels,
            $"reported {width}x{height}, which is larger than the {terminal.Options.MaxSixelPixels} pixel budget");
    }

    [Fact]
    public void An_unknown_item_is_refused()
    {
        var (terminal, replies) = Listening();

        terminal.Write($"{Esc}[?9;1;0S");

        Assert.Equal($"{Esc}[?9;1S", Assert.Single(replies));
    }

    /// <summary>
    /// The limits are fixed, so accepting a request to change them and quietly not doing it would
    /// be worse than refusing.
    /// </summary>
    [Fact]
    public void A_request_to_change_a_limit_is_refused()
    {
        var (terminal, replies) = Listening();

        terminal.Write($"{Esc}[?1;3;64S"); // action 3 is "set"

        Assert.Equal($"{Esc}[?1;2S", Assert.Single(replies));
    }

    /// <summary>Without the private marker it is still SCROLL UP, and still has to scroll.</summary>
    [Fact]
    public void Scroll_up_still_works()
    {
        var terminal = Fresh();
        terminal.Write("top line\r\nsecond line");

        terminal.Write($"{Esc}[1S");

        Assert.Equal("second line", terminal.GetLine(terminal.Buffer.YBase));
    }
}
