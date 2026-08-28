using XTerm.Common;
using XTerm.Options;

namespace XTerm.Tests;

/// <summary>
/// XTVERSION -- "CSI &gt; Ps q" -- and the bug it used to trigger.
///
/// <para>It shares its final character with DECSCUSR, and the CSI identifier has its private marker
/// stripped before the command is looked up, so the query was routed to the cursor style handler.
/// Ps 0 landed in DECSCUSR's "blinking block" case, which is why asking a terminal what it was
/// changed the shape of the cursor and left it there.</para>
/// </summary>
public class VersionReportTests
{
    private const string Esc = "\u001b";

    private static (Terminal Terminal, List<string> Replies) Listening()
    {
        var terminal = new Terminal(new TerminalOptions
        {
            Cols = 40,
            Rows = 6,
            CursorStyle = CursorStyle.Underline,
            CursorBlink = false
        });

        var replies = new List<string>();
        terminal.DataReceived += (_, e) => replies.Add(e.Data);
        return (terminal, replies);
    }

    /// <summary>The regression: asking for the version must not restyle the cursor.</summary>
    [Fact]
    public void A_version_query_leaves_the_cursor_alone()
    {
        var (terminal, _) = Listening();

        terminal.Write($"{Esc}[>0q");

        Assert.Equal(CursorStyle.Underline, terminal.Options.CursorStyle);
        Assert.False(terminal.Options.CursorBlink);
    }

    /// <summary>And it must not do so by way of the event a host listens on either.</summary>
    [Fact]
    public void A_version_query_raises_no_cursor_style_change()
    {
        var (terminal, _) = Listening();
        var changes = 0;
        terminal.CursorStyleChanged += (_, _) => changes++;

        terminal.Write($"{Esc}[>0q");

        Assert.Equal(0, changes);
    }

    [Fact]
    public void The_version_is_reported()
    {
        var (terminal, replies) = Listening();

        terminal.Write($"{Esc}[>0q");

        var version = typeof(Terminal).Assembly.GetName().Version!;
        var expected = $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";

        Assert.Equal($"{Esc}P>|XTerm.NET({expected}){Esc}\\", Assert.Single(replies));
    }

    /// <summary>
    /// The reply is a DCS string, and a program reading it looks for that frame: "DCS &gt; |" to
    /// open and a string terminator to close. Getting either wrong leaves it waiting for a
    /// terminator that never comes, or splicing the reply into whatever it reads next.
    /// </summary>
    [Fact]
    public void The_reply_is_a_dcs_string()
    {
        var (terminal, replies) = Listening();

        terminal.Write($"{Esc}[>0q");

        var reply = Assert.Single(replies);
        Assert.StartsWith($"{Esc}P>|", reply);
        Assert.EndsWith($"{Esc}\\", reply);
    }

    /// <summary>An omitted parameter is Ps 0, which is the request.</summary>
    [Fact]
    public void An_omitted_parameter_is_the_version_request()
    {
        var (terminal, replies) = Listening();

        terminal.Write($"{Esc}[>q");

        Assert.StartsWith($"{Esc}P>|XTerm.NET(", Assert.Single(replies));
    }

    /// <summary>
    /// Ps 0 is the only request defined. A program that asked something else would read the version
    /// back as the answer to its own question, so nothing is sent.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void An_unknown_request_is_not_answered(int ps)
    {
        var (terminal, replies) = Listening();

        terminal.Write($"{Esc}[>{ps}q");

        Assert.Empty(replies);
        Assert.Equal(CursorStyle.Underline, terminal.Options.CursorStyle);
    }

    /// <summary>Without the private marker it is still DECSCUSR, and still has to restyle.</summary>
    [Theory]
    [InlineData("5 q", CursorStyle.Bar, true)]
    [InlineData("2 q", CursorStyle.Block, false)]
    [InlineData("4 q", CursorStyle.Underline, false)]
    public void Decscusr_still_works(string sequence, CursorStyle style, bool blink)
    {
        var (terminal, replies) = Listening();

        terminal.Write($"{Esc}[{sequence}");

        Assert.Equal(style, terminal.Options.CursorStyle);
        Assert.Equal(blink, terminal.Options.CursorBlink);
        Assert.Empty(replies);
    }

    /// <summary>
    /// "CSI ? Ps q" is neither sequence. Reading it as XTVERSION would be a second wrong reading of
    /// the same final character, and reading it as DECSCUSR is the one this branch exists to stop.
    /// </summary>
    [Fact]
    public void A_question_marked_q_is_neither()
    {
        var (terminal, replies) = Listening();

        terminal.Write($"{Esc}[?2q");

        Assert.Empty(replies);
        Assert.Equal(CursorStyle.Underline, terminal.Options.CursorStyle);
        Assert.False(terminal.Options.CursorBlink);
    }

    /// <summary>
    /// The marker is what tells the two apart, so it is read rather than merely detected --
    /// <c>IsPrivateMode</c> is true for both '?' and '&gt;' and cannot make the distinction.
    /// </summary>
    [Theory]
    [InlineData(">q", '>')]
    [InlineData("?h", '?')]
    [InlineData(" q", '\0')]
    [InlineData("m", '\0')]
    [InlineData("", '\0')]
    public void The_private_marker_is_readable(string identifier, char marker)
    {
        Assert.Equal(marker, identifier.PrivateMarker());
    }
}
