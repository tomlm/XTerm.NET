using XTerm;
using XTerm.Events;
using XTerm.Options;
using Xunit;

namespace XTerm.Tests;

/// <summary>
/// Bracketed paste MIME (private mode 5522): the paste notification triple, the single-use
/// token, the precedence rule over mode 2004, and the fallback ladder — held to the spec at
/// rockorager.dev/misc/bracketed-paste-mime.
/// </summary>
public class PasteNotificationTests
{
    private const string Esc = "\u001b";
    private const string St = "\u001b\\";

    private static Terminal NewTerminal() =>
        new(new TerminalOptions { Cols = 20, Rows = 5 });

    private static TerminalPaste RichPaste(bool fromPrimary = false) =>
        new(new[] { "text/html", "text/plain" },
            mime => mime switch
            {
                "text/html" => System.Text.Encoding.UTF8.GetBytes("<b>hi</b>"),
                "text/plain" => System.Text.Encoding.UTF8.GetBytes("hi"),
                _ => null
            },
            fromPrimary);

    private static string PwOf(string okPacket)
    {
        var start = okPacket.IndexOf(":pw=") + 4;
        var end = okPacket.IndexOf('\u001b', start);
        return okPacket[start..end];
    }

    // ---- the mode itself ------------------------------------------------------------------

    [Fact]
    public void The_mode_sets_resets_and_answers_DECRQM()
    {
        var t = NewTerminal();
        var responses = new List<string>();
        t.DataReceived += (_, e) => responses.Add(e.Data);

        t.Write($"{Esc}[?5522$p");
        Assert.Equal($"{Esc}[?5522;2$y", responses[^1]);   // recognised, not set

        t.Write($"{Esc}[?5522h{Esc}[?5522$p");
        Assert.Equal($"{Esc}[?5522;1$y", responses[^1]);

        t.Write($"{Esc}[?5522l{Esc}[?5522$p");
        Assert.Equal($"{Esc}[?5522;2$y", responses[^1]);
    }

    [Fact]
    public void RIS_clears_the_mode()
    {
        var t = NewTerminal();
        t.Write($"{Esc}[?5522h{Esc}c");
        Assert.False(t.PasteNotificationMode);
    }

    // ---- the precedence ladder ------------------------------------------------------------

    [Fact]
    public void With_neither_mode_paste_sends_the_raw_text()
    {
        var t = NewTerminal();
        var sent = new List<string>();
        t.DataReceived += (_, e) => sent.Add(e.Data);

        t.Paste("hello");
        Assert.Equal(new[] { "hello" }, sent);
    }

    [Fact]
    public void With_2004_alone_paste_is_bracketed()
    {
        var t = NewTerminal();
        var sent = new List<string>();
        t.DataReceived += (_, e) => sent.Add(e.Data);

        t.Write($"{Esc}[?2004h");
        t.Paste("hello");
        Assert.Equal(new[] { $"{Esc}[200~hello{Esc}[201~" }, sent);
    }

    /// <summary>
    /// The rule the library exists to enforce: with 5522 set the paste is ANNOUNCED, and the
    /// terminal must never send both sequence types for one paste — even with 2004 also set.
    /// </summary>
    [Fact]
    public void With_5522_set_paste_is_announced_and_never_bracketed()
    {
        var t = NewTerminal();
        var sent = new List<string>();
        t.DataReceived += (_, e) => sent.Add(e.Data);

        t.Write($"{Esc}[?2004h{Esc}[?5522h");
        t.Paste(RichPaste());

        Assert.Equal(3, sent.Count);
        Assert.DoesNotContain(sent, p => p.Contains("200~") || p.Contains("201~"));

        var pw = PwOf(sent[0]);
        Assert.NotEmpty(pw);
        Assert.Equal($"{Esc}]5522;type=read:status=OK:pw={pw}{St}", sent[0]);
        var mimes = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("text/html text/plain"));
        Assert.Equal($"{Esc}]5522;type=read:status=DATA:mime=Lg==:pw={pw};{mimes}{St}", sent[1]);
        Assert.Equal($"{Esc}]5522;type=read:status=DONE:pw={pw}{St}", sent[2]);
    }

    [Fact]
    public void A_primary_selection_paste_carries_loc_primary()
    {
        var t = NewTerminal();
        var sent = new List<string>();
        t.DataReceived += (_, e) => sent.Add(e.Data);

        t.Write($"{Esc}[?5522h");
        t.Paste(RichPaste(fromPrimary: true));
        Assert.StartsWith($"{Esc}]5522;type=read:status=OK:loc=primary:pw=", sent[0]);
    }

    // ---- redemption -----------------------------------------------------------------------

    private static (Terminal t, List<string> sent, string pw) Announced(bool fromPrimary = false)
    {
        var t = NewTerminal();
        var sent = new List<string>();
        t.DataReceived += (_, e) => sent.Add(e.Data);
        t.Write($"{Esc}[?5522h");
        t.Paste(RichPaste(fromPrimary));
        var pw = PwOf(sent[0]);
        sent.Clear();
        return (t, sent, pw);
    }

    private static string Read(string pw, string mimes, string loc = "")
    {
        var payload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(mimes));
        return $"{Esc}]5522;type=read{loc}:pw={pw}:name=UGFzdGUgZXZlbnQ=;{payload}{St}";
    }

    [Fact]
    public void A_token_read_serves_the_announced_content_without_the_host_seam()
    {
        // ClipboardReadEnabled stays FALSE: the notification was the authorization, and the
        // host clipboard event must not fire for a token read.
        var (t, sent, pw) = Announced();
        var hostAsked = false;
        t.ClipboardReadRequested += (_, _) => hostAsked = true;

        t.Write(Read(pw, "text/html"));

        Assert.False(hostAsked);
        Assert.Equal($"{Esc}]5522;type=read:status=OK{St}", sent[0]);
        var mime = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("text/html"));
        var data = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("<b>hi</b>"));
        Assert.Equal($"{Esc}]5522;type=read:status=DATA:mime={mime};{data}{St}", sent[1]);
        Assert.Equal($"{Esc}]5522;type=read:status=DONE{St}", sent[2]);
        Assert.Equal(3, sent.Count);
    }

    [Fact]
    public void The_token_is_single_use()
    {
        var (t, sent, pw) = Announced();
        t.Write(Read(pw, "text/plain"));
        Assert.Equal(3, sent.Count);
        sent.Clear();

        // Second redemption: token consumed; reads are disabled, so the fallback is EPERM.
        t.Write(Read(pw, "text/plain"));
        Assert.Equal($"{Esc}]5522;type=read:status=EPERM{St}", sent.Single());
    }

    [Fact]
    public void A_wrong_token_falls_back_to_standard_security()
    {
        var (t, sent, _) = Announced();
        t.Write(Read(Convert.ToBase64String(new byte[16]), "text/plain"));
        Assert.Equal($"{Esc}]5522;type=read:status=EPERM{St}", sent.Single());
    }

    [Fact]
    public void A_token_without_a_name_is_refused_and_consumed()
    {
        var (t, sent, pw) = Announced();
        var payload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("text/plain"));
        t.Write($"{Esc}]5522;type=read:pw={pw};{payload}{St}");
        Assert.Equal($"{Esc}]5522;type=read:status=EPERM{St}", sent.Single());
        sent.Clear();

        // Presenting the token consumed it, valid or not: no second try with the name added.
        t.Write(Read(pw, "text/plain"));
        Assert.Equal($"{Esc}]5522;type=read:status=EPERM{St}", sent.Single());
    }

    [Fact]
    public void The_token_is_scoped_to_the_location_that_produced_the_paste()
    {
        // Announced from primary; redeemed against the default clipboard — refused.
        var (t, sent, pw) = Announced(fromPrimary: true);
        t.Write(Read(pw, "text/plain"));
        Assert.Equal($"{Esc}]5522;type=read:status=EPERM{St}", sent.Single());
    }

    [Fact]
    public void Redemption_with_the_matching_location_succeeds()
    {
        var (t, sent, pw) = Announced(fromPrimary: true);
        t.Write(Read(pw, "text/plain", loc: ":loc=primary"));
        Assert.Equal(3, sent.Count);
        Assert.Contains("status=DONE", sent[^1]);
    }

    [Fact]
    public void Unavailable_requested_types_are_skipped_not_errors()
    {
        var (t, sent, pw) = Announced();
        t.Write(Read(pw, "image/png text/plain"));

        // OK, one DATA (text/plain), DONE — the png is skipped per the spec.
        Assert.Equal(3, sent.Count);
        var mime = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("text/plain"));
        Assert.Contains($"mime={mime}", sent[1]);
    }

    [Fact]
    public void A_new_paste_invalidates_the_previous_token()
    {
        var (t, sent, oldPw) = Announced();
        t.Paste(RichPaste());
        var newPw = PwOf(sent[0]);
        sent.Clear();

        t.Write(Read(oldPw, "text/plain"));
        Assert.Equal($"{Esc}]5522;type=read:status=EPERM{St}", sent.Single());
        sent.Clear();

        t.Write(Read(newPw, "text/plain"));
        Assert.Contains("status=DONE", sent[^1]);
    }

    [Fact]
    public void Resetting_the_mode_invalidates_the_token()
    {
        var (t, sent, pw) = Announced();
        t.Write($"{Esc}[?5522l");
        t.Write(Read(pw, "text/plain"));
        Assert.Equal($"{Esc}]5522;type=read:status=EPERM{St}", sent.Single());
    }

    [Fact]
    public void An_embedders_own_bracketing_keeps_working()
    {
        // Additive contract: BracketedPasteMode stays a public flag an embedder may read to
        // wrap its own pastes; nothing here changes who owns the brackets unless the host
        // adopts Terminal.Paste.
        var t = NewTerminal();
        t.Write($"{Esc}[?2004h");
        Assert.True(t.BracketedPasteMode);
        t.Write($"{Esc}[?2004l");
        Assert.False(t.BracketedPasteMode);
    }
}
