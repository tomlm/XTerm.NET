using System.Diagnostics;
using System.Text;
using XTerm;
using XTerm.Options;

namespace XTerm.Tests;

/// <summary>
/// The library's Debug output reaches whatever a host has added to <see cref="Trace.Listeners"/>,
/// often an application log. What a program prints, and what the user types or pastes, must never
/// be quoted there: an escape sequence's payload can be clipboard contents, a title, a URL, or text
/// from a file someone ran <c>cat</c> on.
/// </summary>
public class DiagnosticOutputTests
{
    private const string Sentinel = "S3CR3T-sentinel";
    private const string Esc = "\x1B";

    private sealed class CapturingListener : TraceListener
    {
        private readonly StringBuilder _text = new();

        public string Text
        {
            get { lock (_text) return _text.ToString(); }
        }

        public override void Write(string? message)
        {
            lock (_text) _text.Append(message);
        }

        public override void WriteLine(string? message)
        {
            lock (_text) _text.Append(message).Append('\n');
        }
    }

    [Fact]
    public void SequencePayloadsAndInput_NeverReachTraceListeners()
    {
        var listener = new CapturingListener();
        Trace.Listeners.Add(listener);
        try
        {
            var terminal = new Terminal(new TerminalOptions { Cols = 80, Rows = 24 });
            var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(Sentinel));

            // OSC with no number at all: the text before ';' is the whole payload.
            terminal.Write($"{Esc}]{Sentinel}\x07");
            // OSC numbers the terminal does not know, with and without a ';'.
            terminal.Write($"{Esc}]9999;{Sentinel}\x07");
            terminal.Write($"{Esc}]{Sentinel};{Sentinel}\x07");
            // Clipboard, title and hyperlink, each carrying the sentinel.
            terminal.Write($"{Esc}]52;c;{base64}\x07");
            terminal.Write($"{Esc}]52;c;{Sentinel}\x07");
            terminal.Write($"{Esc}]2;{Sentinel}\x07");
            terminal.Write($"{Esc}]8;id={Sentinel};https://example.com/{Sentinel}\x07{Sentinel}{Esc}]8;;\x07");
            // An unknown CSI with a long run of intermediates.
            terminal.Write($"{Esc}[1 !\"#$%&'()*+,-./ !\"#$%&'()*+,-./S");
            // DCS, APC and PM strings.
            terminal.Write($"{Esc}P{Sentinel}{Esc}\\");
            terminal.Write($"{Esc}P1$q{Sentinel}{Esc}\\");
            terminal.Write($"{Esc}_{Sentinel}{Esc}\\");
            terminal.Write($"{Esc}^{Sentinel}{Esc}\\");

            // What the user types or pastes.
            terminal.Paste(Sentinel);
            foreach (var c in Sentinel)
                terminal.GenerateCharInput(c);

            var captured = listener.Text;
            Assert.DoesNotContain(Sentinel, captured);
            Assert.DoesNotContain(base64, captured);
            Assert.DoesNotContain("!\"#$%&'()*+,-./ !", captured);

#if DEBUG
            // An empty capture would pass the checks above whether or not anything was redacted,
            // so prove the library's own lines did arrive, in their redacted form.
            Assert.Contains("Unknown OSC sequence: non-numeric command, 15 chars", captured);
            Assert.Contains("Unknown OSC sequence: 9999", captured);
            Assert.Contains("Unknown CSI sequence: final 'S' after 32 intermediates", captured);
#endif
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }
    }
}
