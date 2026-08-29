using XTerm;
using XTerm.Common;
using XTerm.Options;

namespace XTerm.Tests;

/// <summary>
/// DEC private mode 2048 — in-band window resize notifications.
/// </summary>
/// <remarks>
/// The point of the mode is that an application stops needing SIGWINCH to learn it was resized,
/// which matters more here than in a standalone terminal: a hosted .NET terminal has no signal to
/// fall back on, so this is the only way an application inside XTerm.NET finds out at all.
/// </remarks>
public class InBandResizeTests
{
    private const string Esc = "\u001b";

    private static Terminal Fresh() => new(new TerminalOptions { Cols = 20, Rows = 5 });

    private static List<string> Recording(Terminal terminal)
    {
        var replies = new List<string>();
        terminal.DataReceived += (_, e) => replies.Add(e.Data);
        return replies;
    }

    [Fact]
    public void Begins_and_ends_with_the_mode()
    {
        var terminal = Fresh();
        Assert.False(terminal.InBandResize);

        terminal.Write($"{Esc}[?2048h");
        Assert.True(terminal.InBandResize);

        terminal.Write($"{Esc}[?2048l");
        Assert.False(terminal.InBandResize);
    }

    /// <summary>
    /// The first report is mandatory, and it is what makes the mode worth setting: an application
    /// learns the size the moment it asks to be kept informed, rather than enabling this and then
    /// waiting for a resize that may never come.
    /// </summary>
    [Fact]
    public void Setting_the_mode_reports_immediately()
    {
        var terminal = Fresh();
        var replies = Recording(terminal);

        terminal.Write($"{Esc}[?2048h");

        Assert.Equal(new[] { $"{Esc}[48;5;20;0;0t" }, replies);
    }

    /// <summary>
    /// Rows before columns, and pixels after characters. Getting the order wrong is silent: an
    /// application reads a 20x5 terminal as 5x20 and lays itself out sideways.
    /// </summary>
    [Fact]
    public void Reports_rows_before_columns_on_a_resize()
    {
        var terminal = Fresh();
        terminal.Write($"{Esc}[?2048h");
        var replies = Recording(terminal);

        terminal.Resize(100, 40);

        Assert.Equal(new[] { $"{Esc}[48;40;100;0;0t" }, replies);
    }

    [Fact]
    public void Says_nothing_when_the_mode_was_never_set()
    {
        var terminal = Fresh();
        var replies = Recording(terminal);

        terminal.Resize(100, 40);

        Assert.Empty(replies);
    }

    [Fact]
    public void Stops_reporting_once_the_mode_is_reset()
    {
        var terminal = Fresh();
        terminal.Write($"{Esc}[?2048h");
        terminal.Write($"{Esc}[?2048l");
        var replies = Recording(terminal);

        terminal.Resize(100, 40);

        Assert.Empty(replies);
    }

    /// <summary>
    /// A resize to the size it already is returns early and is not a resize event, so there is
    /// nothing to report. An application that gets a report anyway would redraw for nothing.
    /// </summary>
    [Fact]
    public void A_resize_that_changes_nothing_reports_nothing()
    {
        var terminal = Fresh();
        terminal.Write($"{Esc}[?2048h");
        var replies = Recording(terminal);

        terminal.Resize(20, 5);

        Assert.Empty(replies);
    }

    [Fact]
    public void Reports_every_resize_not_just_the_first()
    {
        var terminal = Fresh();
        terminal.Write($"{Esc}[?2048h");
        var replies = Recording(terminal);

        terminal.Resize(100, 40);
        terminal.Resize(80, 24);

        Assert.Equal(new[] { $"{Esc}[48;40;100;0;0t", $"{Esc}[48;24;80;0;0t" }, replies);
    }

    // ---- pixel dimensions ------------------------------------------------------------------------

    /// <summary>
    /// Zero is the spec's answer for a terminal that cannot determine its pixel size, and it is the
    /// honest one for a headless emulator whose host has not said. The alternative — multiplying out
    /// <see cref="TerminalOptions.CellWidthPixels"/>, whose default is a 10x20 placeholder — reports
    /// a number that looks real, and an application sizing an image off it would be wrong rather
    /// than uninformed.
    /// </summary>
    [Fact]
    public void Reports_zero_pixels_when_the_host_does_not_answer()
    {
        var terminal = Fresh();
        var replies = Recording(terminal);

        terminal.Write($"{Esc}[?2048h");

        Assert.Equal(new[] { $"{Esc}[48;5;20;0;0t" }, replies);
    }

    [Fact]
    public void Reports_the_hosts_pixel_size_when_it_answers()
    {
        var terminal = Fresh();
        terminal.WindowInfoRequested += (_, e) =>
        {
            if (e.Request != WindowInfoRequest.SizePixels)
                return;

            e.WidthPixels = 1600;
            e.HeightPixels = 900;
            e.Handled = true;
        };
        var replies = Recording(terminal);

        terminal.Write($"{Esc}[?2048h");

        Assert.Equal(new[] { $"{Esc}[48;5;20;900;1600t" }, replies);
    }

    /// <summary>
    /// Not gated on <see cref="WindowOptions.GetWinSizePixels"/>, which defaults to false and governs
    /// whether unsolicited XTWINOPS queries are answered at all. Mode 2048 is itself the
    /// application's request; reusing that gate would leave the pixel fields permanently zero.
    /// </summary>
    [Fact]
    public void Pixel_size_does_not_depend_on_the_XTWINOPS_gate()
    {
        var terminal = Fresh();
        Assert.False(terminal.Options.WindowOptions.GetWinSizePixels);
        terminal.WindowInfoRequested += (_, e) =>
        {
            e.WidthPixels = 800;
            e.HeightPixels = 600;
            e.Handled = true;
        };
        var replies = Recording(terminal);

        terminal.Write($"{Esc}[?2048h");

        Assert.Equal(new[] { $"{Esc}[48;5;20;600;800t" }, replies);
    }

    /// <summary>
    /// The report follows the resize rather than announcing it, so the size an application reads is
    /// one the terminal has already applied — and a host that recalculates its pixel metrics in
    /// <see cref="Terminal.Resized"/> has done so before the report asks for them.
    /// </summary>
    [Fact]
    public void Reports_after_the_host_has_been_told()
    {
        var terminal = Fresh();
        terminal.Write($"{Esc}[?2048h");

        var order = new List<string>();
        terminal.Resized += (_, _) => order.Add("resized");
        terminal.DataReceived += (_, _) => order.Add("reported");

        terminal.Resize(100, 40);

        Assert.Equal(new[] { "resized", "reported" }, order);
    }

    /// <summary>
    /// The host's pixel metrics are current by the time they are asked for, which is the practical
    /// payoff of reporting last.
    /// </summary>
    [Fact]
    public void Picks_up_pixel_metrics_the_host_updates_during_the_resize()
    {
        var terminal = Fresh();
        var cellWidth = 8;
        var cellHeight = 16;
        terminal.Resized += (_, _) =>
        {
            // What a real host does: recompute from the new grid and its own font metrics.
            cellWidth = 10;
            cellHeight = 20;
        };
        terminal.WindowInfoRequested += (_, e) =>
        {
            e.WidthPixels = terminal.Cols * cellWidth;
            e.HeightPixels = terminal.Rows * cellHeight;
            e.Handled = true;
        };

        terminal.Write($"{Esc}[?2048h");
        var replies = Recording(terminal);

        terminal.Resize(100, 40);

        Assert.Equal(new[] { $"{Esc}[48;40;100;800;1000t" }, replies);
    }

    // ---- a reset has to clear it -----------------------------------------------------------------

    /// <summary>
    /// RIS is how someone recovers from an application that set the mode and died. Leaving it set
    /// would keep writing reports at whatever runs next, which never asked for them and will read
    /// them as input.
    /// </summary>
    [Fact]
    public void A_full_reset_clears_the_mode()
    {
        var terminal = Fresh();
        terminal.Write($"{Esc}[?2048h");

        terminal.Write($"{Esc}c");   // RIS

        Assert.False(terminal.InBandResize);
    }

    [Fact]
    public void A_full_reset_stops_the_reports()
    {
        var terminal = Fresh();
        terminal.Write($"{Esc}[?2048h");
        terminal.Write($"{Esc}c");
        var replies = Recording(terminal);

        terminal.Resize(100, 40);

        Assert.Empty(replies);
    }

    // ---- DECRQM, which is the only way to discover the mode exists -------------------------------

    [Fact]
    public void Reports_the_mode_as_reset_when_idle()
    {
        var terminal = Fresh();
        var replies = Recording(terminal);

        terminal.Write($"{Esc}[?2048$p");

        Assert.Equal(new[] { $"{Esc}[?2048;2$y" }, replies);
    }

    [Fact]
    public void Reports_the_mode_as_set_once_enabled()
    {
        var terminal = Fresh();
        terminal.Write($"{Esc}[?2048h");
        var replies = Recording(terminal);

        terminal.Write($"{Esc}[?2048$p");

        Assert.Equal(new[] { $"{Esc}[?2048;1$y" }, replies);
    }

    /// <summary>
    /// Detection is the whole reason this mode is queryable — an application has no other way to
    /// learn that reports will arrive, so a stale "set" after a reset would leave it waiting for
    /// notifications that are never coming.
    /// </summary>
    [Fact]
    public void Decrqm_reports_reset_after_a_reset()
    {
        var terminal = Fresh();
        terminal.Write($"{Esc}[?2048h");
        terminal.Write($"{Esc}c");
        var replies = Recording(terminal);

        terminal.Write($"{Esc}[?2048$p");

        Assert.Equal(new[] { $"{Esc}[?2048;2$y" }, replies);
    }

    [Fact]
    public void A_pixel_only_change_reports_through_the_public_notify()
    {
        // Font-size and zoom changes alter the text area's pixels with the grid unchanged; the
        // spec requires a report for exactly that, and the host delivers it via the public
        // notify after updating its metrics.
        var t = new Terminal(new TerminalOptions { Cols = 20, Rows = 5 });
        var reports = new List<string>();
        t.DataReceived += (_, e) => { if (e.Data.Contains("[48;")) reports.Add(e.Data); };
        var px = (Height: 200, Width: 400);
        t.WindowInfoRequested += (_, e) =>
        {
            if (e.Request != WindowInfoRequest.SizePixels)
                return;
            e.HeightPixels = px.Height;
            e.WidthPixels = px.Width;
            e.Handled = true;
        };

        t.Write("\u001b[?2048h");
        Assert.Single(reports);                    // enabling reports once, per the spec
        reports.Clear();

        px = (Height: 240, Width: 480);            // zoom: same grid, new pixels
        t.NotifyTextAreaPixelsChanged();
        Assert.Equal("\u001b[48;5;20;240;480t", reports.Single());

        t.Write("\u001b[?2048l");
        reports.Clear();
        t.NotifyTextAreaPixelsChanged();           // mode off: safe no-op
        Assert.Empty(reports);
    }
}
