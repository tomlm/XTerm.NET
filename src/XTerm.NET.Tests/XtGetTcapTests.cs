using System.Linq;
using XTerm.Options;
using XTerm.Tests.Graphics;

namespace XTerm.Tests;

/// <summary>
/// XTGETTCAP: the terminal answering, in its own voice, what it can do.
///
/// <para>A program's idea of the terminal comes from TERM and whichever terminfo database is on the
/// machine it happens to be running on, which over ssh or in a container describes some other
/// terminal entirely. DCS + q is how it asks this one directly, so these drive it the way a program
/// does — hex-encoded names in, hex-encoded values out — rather than reaching into the capability
/// table.</para>
///
/// <para>The neighbour is DECSIXEL, which is DCS q with no intermediate in front of it. The two are
/// told apart by the identifier the parser builds, so there are tests here for the neighbour as
/// well: adding one must not have taken the other away.</para>
/// </summary>
public class XtGetTcapTests
{
    // Spelled from their code points so no line in this file has to carry a control character it
    // would then be impossible to see in a diff.
    private static readonly string Esc = ((char)0x1B).ToString();
    private static readonly string St = Esc + "\\";
    private static readonly string Can = ((char)0x18).ToString();

    /// <summary>Four pixels wide, twelve tall: at 2x3 cells, two across and four down.</summary>
    private const string SixelImage = "#0;2;100;0;0!4~-!4~";

    private static Terminal Fresh(Action<TerminalOptions>? configure = null)
    {
        var options = new TerminalOptions
        {
            Cols = 80,
            Rows = 24,
            CellWidthPixels = 2,
            CellHeightPixels = 3
        };
        configure?.Invoke(options);
        return new Terminal(options);
    }

    /// <summary>Everything the terminal wrote back in answer to one request, one entry per reply.</summary>
    private static List<string> Ask(Terminal terminal, string request)
    {
        var replies = new List<string>();
        terminal.DataReceived += (_, e) => replies.Add(e.Data);
        terminal.Write($"{Esc}P+q{request}{St}");
        return replies;
    }

    private static string Hex(string text) => string.Concat(text.Select(c => ((int)c).ToString("X2")));

    private static string Valid(string name, string value) => $"{Esc}P1+r{Hex(name)}={Hex(value)}{St}";

    private static string Invalid(string encodedName) => $"{Esc}P0+r{encodedName}{St}";

    [Fact]
    public void A_capability_the_terminal_has_is_answered_with_its_value()
    {
        var replies = Ask(Fresh(), Hex("TN"));

        Assert.Equal(new[] { Valid("TN", "xterm") }, replies);
    }

    [Fact]
    public void The_reported_name_is_the_one_the_host_configured()
    {
        // TermName is what the host tells programs to call this terminal, so it is what comes back:
        // answering "xterm" to a host that set xterm-256color would contradict its own environment.
        var replies = Ask(Fresh(o => o.TermName = "xterm-256color"), Hex("TN"));

        Assert.Equal(new[] { Valid("TN", "xterm-256color") }, replies);
    }

    [Fact]
    public void Both_spellings_of_a_capability_are_answered()
    {
        // A caller uses whichever spelling its own database uses and cannot know which one we would
        // have preferred, so both work: "Co" is termcap, "colors" is terminfo.
        Assert.Equal(new[] { Valid("Co", "256") }, Ask(Fresh(), Hex("Co")));
        Assert.Equal(new[] { Valid("colors", "256") }, Ask(Fresh(), Hex("colors")));
    }

    [Fact]
    public void A_capability_name_is_case_sensitive()
    {
        // "Co" is the number of colours and "co" is the number of columns. Folding case would let
        // one of them answer for the other.
        var replies = Ask(Fresh(o => o.Cols = 132), Hex("co"));

        Assert.Equal(new[] { Valid("co", "132") }, replies);
    }

    [Fact]
    public void The_size_reported_is_the_size_the_terminal_is_now()
    {
        // The size is asked about as a fallback by a caller with no ioctl to ask instead, so a stale
        // answer would be worse than none: it is read after a resize, not at construction.
        var terminal = Fresh();
        terminal.Resize(100, 40);

        var replies = Ask(terminal, $"{Hex("cols")};{Hex("lines")}");

        Assert.Equal(new[] { Valid("cols", "100"), Valid("lines", "40") }, replies);
    }

    [Fact]
    public void A_boolean_capability_is_answered_with_an_empty_value()
    {
        // Tc is a flag, not a string. It still gets the success reply — the empty value IS the
        // answer, and a failure reply would say the terminal has no direct colour at all.
        var replies = Ask(Fresh(), Hex("Tc"));

        Assert.Equal(new[] { $"{Esc}P1+r{Hex("Tc")}={St}" }, replies);
    }

    [Fact]
    public void A_capability_the_terminal_does_not_have_is_refused()
    {
        var replies = Ask(Fresh(), Hex("nosuchcap"));

        Assert.Equal(new[] { Invalid(Hex("nosuchcap")) }, replies);
    }

    [Fact]
    public void Every_name_in_a_request_gets_its_own_reply_in_order()
    {
        // One reply per name, so a client can pair each answer with the question it asked even when
        // only some of them were understood.
        var replies = Ask(Fresh(), $"{Hex("TN")};{Hex("nope")};{Hex("Co")}");

        Assert.Equal(
            new[] { Valid("TN", "xterm"), Invalid(Hex("nope")), Valid("Co", "256") },
            replies);
    }

    [Fact]
    public void Lowercase_hex_is_accepted_and_the_name_is_echoed_back_as_it_arrived()
    {
        // The name comes back exactly as it was sent rather than re-encoded, so a client can match
        // the reply against its own bytes without knowing which hex case we would have chosen.
        var replies = Ask(Fresh(), "544e");

        Assert.Equal(new[] { $"{Esc}P1+r544e={Hex("xterm")}{St}" }, replies);
    }

    [Theory]
    [InlineData("544")]      // an odd number of digits
    [InlineData("zz")]       // digits that are not hex
    public void A_name_that_is_not_hex_is_refused_rather_than_guessed_at(string encoded)
    {
        // Half a decoded name is some other capability, and answering that confidently would be
        // worse than refusing.
        var replies = Ask(Fresh(), encoded);

        Assert.Equal(new[] { Invalid(encoded) }, replies);
    }

    [Fact]
    public void A_missing_name_between_separators_is_refused_without_taking_its_neighbours_with_it()
    {
        var replies = Ask(Fresh(), $"{Hex("TN")};;{Hex("Co")}");

        Assert.Equal(
            new[] { Valid("TN", "xterm"), Invalid(string.Empty), Valid("Co", "256") },
            replies);
    }

    [Fact]
    public void An_empty_request_is_refused_rather_than_ignored()
    {
        // Silence would leave a client that expects an answer waiting for one.
        var replies = Ask(Fresh(), string.Empty);

        Assert.Equal(new[] { Invalid(string.Empty) }, replies);
    }

    [Fact]
    public void A_request_split_across_writes_is_still_answered()
    {
        // The payload arrives as chunks, and a name can be cut in half by a write boundary.
        var terminal = Fresh();
        var replies = new List<string>();
        terminal.DataReceived += (_, e) => replies.Add(e.Data);

        terminal.Write($"{Esc}P+q54");
        terminal.Write("4E");
        terminal.Write(St);

        Assert.Equal(new[] { Valid("TN", "xterm") }, replies);
    }

    [Fact]
    public void An_abandoned_request_is_not_answered()
    {
        // CAN abandons the sequence. Answering it would attribute an answer to a question that was
        // never finished being asked.
        var terminal = Fresh();
        var replies = new List<string>();
        terminal.DataReceived += (_, e) => replies.Add(e.Data);

        terminal.Write($"{Esc}P+q{Hex("TN")}{Can}");

        Assert.Empty(replies);
    }

    [Fact]
    public void An_abandoned_request_does_not_leak_into_the_next_one()
    {
        var terminal = Fresh();
        terminal.Write($"{Esc}P+q{Hex("Co")}{Can}");

        var replies = Ask(terminal, Hex("TN"));

        Assert.Equal(new[] { Valid("TN", "xterm") }, replies);
    }

    [Fact]
    public void Sixel_is_optional_and_is_only_claimed_when_it_is_switched_on()
    {
        // Claiming Su while Sixel is off would send a program down a path that puts nothing on the
        // screen at all.
        Assert.Equal(
            new[] { $"{Esc}P1+r{Hex("Su")}={St}" },
            Ask(Fresh(o => o.SixelEnabled = true), Hex("Su")));

        Assert.Equal(
            new[] { Invalid(Hex("Su")) },
            Ask(Fresh(o => o.SixelEnabled = false), Hex("Su")));
    }

    [Fact]
    public void A_value_carrying_control_characters_survives_the_encoding()
    {
        // The whole point of the hex is that a terminfo string is bytes: kcuu1 is ESC O A, and
        // sending it back raw would have the client's own parser act on it instead of reading it.
        var replies = Ask(Fresh(), Hex("kcuu1"));

        Assert.Equal(new[] { $"{Esc}P1+r{Hex("kcuu1")}=1B4F41{St}" }, replies);
    }

    [Fact]
    public void A_value_that_is_not_ASCII_goes_out_as_bytes_rather_than_as_characters()
    {
        // The reader takes the value two digits at a time, so a value is bytes and the bytes are
        // UTF-8 ones: U+00E9 goes out as C3 A9, not as the single byte E9, and a character past
        // U+00FF goes out as its bytes rather than as four digits the reader would split in half.
        // Every value in the table is ASCII, where that distinction does not arise; TermName is the
        // one the host fills in, and so the one this is not ours to assume about.
        var eAcute = ((char)0x00E9).ToString();
        var replies = Ask(Fresh(o => o.TermName = "xterm-" + eAcute), Hex("TN"));

        Assert.Equal(new[] { $"{Esc}P1+r{Hex("TN")}={Hex("xterm-")}C3A9{St}" }, replies);
    }

    // ---- The neighbour: DCS q is still DECSIXEL -------------------------------------------------

    [Fact]
    public void A_sixel_payload_is_not_mistaken_for_a_capability_request()
    {
        // DCS q and DCS + q differ by one intermediate character. If the capability path claimed
        // both, every Sixel image would answer with a screenful of refusals.
        var terminal = Fresh();
        var replies = new List<string>();
        terminal.DataReceived += (_, e) => replies.Add(e.Data);

        terminal.Write($"{Esc}P0;1;0q{SixelImage}{St}");

        Assert.Empty(replies);
        Assert.NotNull(ImageAssertions.ImageAt(terminal, 0, 0));
    }

    [Fact]
    public void A_capability_request_after_an_image_is_still_answered()
    {
        // The two share the hook/put/unhook path, so what one leaves behind is the other's problem.
        var terminal = Fresh();
        terminal.Write($"{Esc}P0;1;0q{SixelImage}{St}");

        var replies = Ask(terminal, Hex("TN"));

        Assert.Equal(new[] { Valid("TN", "xterm") }, replies);
    }

    [Fact]
    public void An_image_after_a_capability_request_is_still_decoded()
    {
        var terminal = Fresh();
        Ask(terminal, Hex("TN"));

        terminal.Write($"{Esc}P0;1;0q{SixelImage}{St}");

        Assert.NotNull(ImageAssertions.ImageAt(terminal, 0, 0));
    }
}
