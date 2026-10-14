using XTerm.Common;
using XTerm.Input;
using XTerm.Options;

namespace XTerm.Tests;

/// <summary>
/// DECRQM (CSI ? Ps $ p) — the query an application uses to find out what this terminal supports.
/// </summary>
/// <remarks>
/// The reply matters more than it looks. An application that wants a feature sets the mode, asks
/// whether it took, and falls back when the answer is no — so a mode reported as reset right after
/// it was set is a working feature the application will never use, and silence for a mode that IS
/// implemented is the same loss by a slower route. These tests pin both halves: every mode the
/// terminal tracks answers truthfully, and the ones it does not track stay quiet rather than guess.
/// </remarks>
public class RequestModeTests
{
    private static Terminal Fresh() => new(new TerminalOptions { Cols = 20, Rows = 5 });

    private static List<string> Replies(Terminal terminal)
    {
        var replies = new List<string>();
        terminal.DataReceived += (_, e) => replies.Add(e.Data);
        return replies;
    }

    /// <summary>DECSET, DECRST and DECRQM for one private mode, as an application would send them.</summary>
    private static string Set(int mode) => Esc.Csi($"?{mode}h");

    private static string Reset(int mode) => Esc.Csi($"?{mode}l");

    private static string Query(int mode) => Esc.Csi($"?{mode}$p");

    /// <summary>The DECRPM report expected back for a mode in a given state: 1 is set, 2 is reset.</summary>
    private static string Report(int mode, bool set) => Esc.Csi($"?{mode};{(set ? 1 : 2)}$y");

    private static string PermanentReport(int mode) => Esc.Csi($"?{mode};3$y");

    /// <summary>DECRPM's "mode not recognised": Ps=0.</summary>
    private static string UnrecognisedReport(int mode) => Esc.Csi($"?{mode};0$y");

    /// <summary>The same report for an ANSI mode, which carries no private marker.</summary>
    private static string AnsiReport(int mode, bool set) => Esc.Csi($"{mode};{(set ? 1 : 2)}$y");

    /// <summary>Every private mode DECRQM answers for, as an application would name it.</summary>
    public static TheoryData<int> ReportedModes() => new()
    {
        (int)TerminalMode.AppCursorKeys,
        (int)TerminalMode.ReverseVideo,
        (int)TerminalMode.Origin,
        (int)TerminalMode.Wraparound,
        (int)TerminalMode.ShowCursor,
        (int)TerminalMode.ReverseWraparound,
        (int)TerminalMode.AppKeypad,
        (int)TerminalMode.LeftRightMargin,
        (int)TerminalMode.SixelDisplayMode,
        (int)TerminalMode.SixelPrivateColorRegisters,
        (int)TerminalMode.SixelCursorRight,
        (int)TerminalMode.MouseReportClick,
        (int)TerminalMode.MouseReportNormal,
        (int)TerminalMode.MouseReportButtonEvent,
        (int)TerminalMode.MouseReportAnyEvent,
        (int)TerminalMode.MouseReportUtf8,
        (int)TerminalMode.MouseReportSgr,
        (int)TerminalMode.MouseReportUrxvt,
        (int)TerminalMode.SendFocusEvents,
        (int)TerminalMode.AltBuffer,
        (int)TerminalMode.AltBufferCursor,
        (int)TerminalMode.AltBufferFull,
        (int)TerminalMode.EightBitInput,
        (int)TerminalMode.MetaSendsEscape,
        (int)TerminalMode.AltSendsEscape,
        (int)TerminalMode.BracketedPasteMode,
        (int)TerminalMode.SynchronizedOutput,
        (int)TerminalMode.Win32InputMode,
    };

    /// <summary>
    /// The round trip an application actually performs: set the mode, ask, reset it, ask again. Run
    /// against every reported mode, because the failure this guards against is a single wrong entry
    /// in the lookup — one mode reading another mode's flag looks right until you ask about it.
    /// </summary>
    [Theory]
    [MemberData(nameof(ReportedModes))]
    public void Reports_each_mode_as_set_after_setting_it_and_reset_after_resetting_it(int mode)
    {
        var terminal = Fresh();
        var replies = Replies(terminal);

        terminal.Write(Set(mode));
        terminal.Write(Query(mode));

        terminal.Write(Reset(mode));
        terminal.Write(Query(mode));

        Assert.Equal(new[] { Report(mode, true), Report(mode, false) }, replies);
    }

    /// <summary>
    /// A mode nobody has touched still gets an answer — "supported, and currently off" is a
    /// different reply from silence, and it is the one that tells an application to go ahead.
    /// </summary>
    [Theory]
    [InlineData(1)]     // application cursor keys
    [InlineData(5)]     // reverse video
    [InlineData(6)]     // origin
    [InlineData(1000)]  // VT200 mouse
    [InlineData(1006)]  // SGR mouse encoding
    [InlineData(1016)]  // SGR-Pixels mouse encoding
    [InlineData(1049)]  // alternate buffer
    [InlineData(1070)]  // private Sixel colour registers: shared by default, as on a VT340
    [InlineData(2004)]  // bracketed paste
    [InlineData(2026)]  // synchronized output
    public void Reports_an_untouched_mode_as_reset(int mode)
    {
        var terminal = Fresh();
        var replies = Replies(terminal);

        terminal.Write(Query(mode));

        Assert.Equal(new[] { Report(mode, false) }, replies);
    }

    [Fact]
    public void Reports_grapheme_clustering_as_permanently_set()
    {
        var terminal = Fresh();
        var replies = Replies(terminal);
        var mode = (int)TerminalMode.GraphemeClustering;

        terminal.Write(Reset(mode));
        terminal.Write(Query(mode));
        terminal.Write(Set(mode));
        terminal.Write(Query(mode));

        Assert.Equal(new[] { PermanentReport(mode), PermanentReport(mode) }, replies);
    }

    /// <summary>
    /// Some modes start on, and reporting them as reset would cost an application the feature: it
    /// would turn wraparound on that was already on, or show a cursor it meant to leave alone.
    /// </summary>
    [Theory]
    [InlineData(7)]     // wraparound
    [InlineData(25)]    // cursor visible
    public void Reports_a_mode_that_starts_on_as_set(int mode)
    {
        var terminal = Fresh();
        var replies = Replies(terminal);

        terminal.Write(Query(mode));

        Assert.Equal(new[] { Report(mode, true) }, replies);
    }

    /// <summary>
    /// Mouse tracking is one selection, not four flags. An application that moves from button-event
    /// to any-event reporting has to see the mode it left go reset, or it will believe both are
    /// live and misread the reports it gets.
    /// </summary>
    [Fact]
    public void Reports_only_the_mouse_tracking_level_currently_selected()
    {
        var terminal = Fresh();

        terminal.Write(Set(1002));
        var replies = Replies(terminal);
        terminal.Write(Query(1002));
        terminal.Write(Query(1003));
        terminal.Write(Query(9));

        Assert.Equal(
            new[] { Report(1002, true), Report(1003, false), Report(9, false) },
            replies);

        replies.Clear();
        terminal.Write(Set(1003));
        terminal.Write(Query(1002));
        terminal.Write(Query(1003));

        Assert.Equal(new[] { Report(1002, false), Report(1003, true) }, replies);
    }

    /// <summary>
    /// The encoding is a selection too, chosen independently of the tracking level.
    /// </summary>
    [Fact]
    public void Reports_only_the_mouse_encoding_currently_selected()
    {
        var terminal = Fresh();

        terminal.Write(Set(1006));
        var replies = Replies(terminal);
        terminal.Write(Query(1006));
        terminal.Write(Query(1005));
        terminal.Write(Query(1015));

        Assert.Equal(
            new[] { Report(1006, true), Report(1005, false), Report(1015, false) },
            replies);
    }

    /// <summary>
    /// SGR-Pixels is one more choice in that same selection. An application probing for it with
    /// DECRQM before switching has to hear "supported" -- a 0 reply sends it back to cell reports --
    /// and once it is on, 1006 is not, because the reports no longer carry cells.
    /// </summary>
    [Fact]
    public void Reports_sgr_pixels_as_a_mouse_encoding_selection()
    {
        var terminal = Fresh();

        terminal.Write(Set(1016));
        var replies = Replies(terminal);
        terminal.Write(Query(1016));
        terminal.Write(Query(1006));

        Assert.Equal(new[] { Report(1016, true), Report(1006, false) }, replies);

        replies.Clear();
        terminal.Write(Reset(1016));
        terminal.Write(Query(1016));

        Assert.Equal(new[] { Report(1016, false) }, replies);
    }

    /// <summary>
    /// The three alternate-buffer modes differ only in the cursor and erase work they do on the way
    /// in and out; there is one buffer, so they read alike. An application that entered with 1049
    /// and asks about 47 is asking "am I on the alternate screen", and the answer is yes.
    /// </summary>
    [Fact]
    public void Reports_the_alternate_buffer_alike_for_all_three_modes_that_switch_it()
    {
        var terminal = Fresh();

        terminal.Write(Set(1049));
        var replies = Replies(terminal);
        terminal.Write(Query(47));
        terminal.Write(Query(1047));
        terminal.Write(Query(1049));

        Assert.Equal(
            new[] { Report(47, true), Report(1047, true), Report(1049, true) },
            replies);
    }

    /// <summary>
    /// "Not recognised" for the modes this terminal genuinely keeps no state for.
    /// </summary>
    /// <remarks>
    /// This asserted SILENCE until vttest showed what that costs: menu 11.8.2.2 reports modes
    /// 2 and 10-13 as "failed", because no report ever arrives and a client waiting for one
    /// waits forever. DECRQM is defined to answer every request.
    ///
    /// The reasoning that put silence here still holds and is not contradicted: reporting
    /// "reset" for an untracked mode WOULD be a guess. Ps=0 is not that guess -- it is the
    /// defined value for "I do not recognise this mode", which is exactly what is true here,
    /// and it is what DECRQSS in this same emulator already answers for the settings it does
    /// not support.
    /// </remarks>
    [Theory]
    [InlineData(1035)]   // NumLock modifiers
    [InlineData(1001)]   // highlight mouse tracking, not implemented
    [InlineData(64738)]  // not a mode at all
    public void Reports_not_recognised_for_modes_it_keeps_no_state_for(int mode)
    {
        var terminal = Fresh();
        var replies = Replies(terminal);

        terminal.Write(Query(mode));

        Assert.Equal(new[] { UnrecognisedReport(mode) }, replies);
    }

    /// <summary>
    /// IRM is the one ANSI mode this terminal implements, and DECRQM answers for it. The reply
    /// carries no '?': CSI 4 ; 1 $ y is IRM set, where CSI ? 4 ; 1 $ y would be DECSCLM, and the
    /// two are different sequences to a parser. This test previously pinned silence here on the
    /// grounds that any reply would be a private-mode report — it would not, and the mode is
    /// tracked, so the silence was a supported feature reported as unsupported.
    /// </summary>
    [Fact]
    public void Reports_ansi_insert_mode()
    {
        var terminal = Fresh();
        var replies = Replies(terminal);

        terminal.Write(Esc.Csi("4$p"));
        terminal.Write(Esc.Csi("4h"));
        terminal.Write(Esc.Csi("4$p"));
        terminal.Write(Esc.Csi("4l"));
        terminal.Write(Esc.Csi("4$p"));

        Assert.Equal(
            new[] { AnsiReport(4, false), AnsiReport(4, true), AnsiReport(4, false) },
            replies);
    }

    /// <summary>
    /// KAM, SRM and LNM are STORED now -- set, reset and read back like any tracked mode, because
    /// esctest (and the programs it stands in for) toggle them and expect the toggle reported.
    /// </summary>
    [Theory]
    [InlineData(2)]   // keyboard action mode (KAM)
    [InlineData(12)]  // send/receive (SRM)
    [InlineData(20)]  // automatic newline (LNM)
    public void Stored_ansi_modes_toggle_and_report(int mode)
    {
        var terminal = Fresh();
        var replies = Replies(terminal);

        terminal.Write(Esc.Csi($"{mode}$p"));
        terminal.Write(Esc.Csi($"{mode}h"));
        terminal.Write(Esc.Csi($"{mode}$p"));
        terminal.Write(Esc.Csi($"{mode}l"));
        terminal.Write(Esc.Csi($"{mode}$p"));

        Assert.Equal(new[]
        {
            Esc.Csi($"{mode};2$y"),
            Esc.Csi($"{mode};1$y"),
            Esc.Csi($"{mode};2$y"),
        }, replies);
    }

    /// <summary>
    /// RIS has to reach the mouse tracker, or DECRQM reports a mode off while the terminal is still
    /// acting on it. Mode 1004 is kept twice — the terminal's SendFocusEvents and the tracker's own
    /// FocusEvents — and only the tracker's copy gates output, so a reset that cleared one and not
    /// the other left focus reports arriving in an application that had been told to expect none.
    /// The generated sequences are asserted next to the reports because a report agreeing with a
    /// stale flag is the whole bug: the query alone looked right.
    /// </summary>
    [Fact]
    public void Reports_mouse_and_focus_modes_as_reset_after_ris()
    {
        var terminal = Fresh();
        terminal.Write(Set((int)TerminalMode.MouseReportButtonEvent));
        terminal.Write(Set((int)TerminalMode.MouseReportSgr));
        terminal.Write(Set((int)TerminalMode.SendFocusEvents));

        terminal.Write("\u001bc");  // RIS

        var replies = Replies(terminal);
        terminal.Write(Query((int)TerminalMode.MouseReportButtonEvent));
        terminal.Write(Query((int)TerminalMode.MouseReportSgr));
        terminal.Write(Query((int)TerminalMode.SendFocusEvents));

        Assert.Equal(
            new[]
            {
                Report((int)TerminalMode.MouseReportButtonEvent, false),
                Report((int)TerminalMode.MouseReportSgr, false),
                Report((int)TerminalMode.SendFocusEvents, false),
            },
            replies);

        Assert.Equal(string.Empty, terminal.GenerateFocusEvent(true));
        Assert.Equal(
            string.Empty,
            terminal.GenerateMouseEvent(MouseButton.Left, 0, 0, MouseEventType.Down));
    }

    /// <summary>
    /// A missing parameter defaults to mode 0, which is not a mode. Answering for it would send an
    /// application a report it never asked for.
    /// </summary>
    [Fact]
    public void Says_nothing_when_no_mode_is_named()
    {
        var terminal = Fresh();
        var replies = Replies(terminal);

        terminal.Write(Esc.Csi("?$p"));

        Assert.Empty(replies);
    }
}
