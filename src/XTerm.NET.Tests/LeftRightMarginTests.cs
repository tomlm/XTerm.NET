using XTerm.Options;
using Xunit;

namespace XTerm.Tests;

/// <summary>
/// DECSLRM -- left and right margins, and the DECLRMM mode (69) that turns them on.
///
/// <para>The margins themselves are the easy half. What makes the feature real is that every
/// operation which moves content honours them: wrapping, scrolling, IL/DL and ICH/DCH. A terminal
/// that reports the mode as supported and then scrolls the whole screen anyway is worse than one
/// that reports nothing, because an application asks before it relies on this.</para>
/// </summary>
public class LeftRightMarginTests
{
    private const string Esc = "\u001b";

    private static Terminal Fresh(int cols = 20, int rows = 6)
        => new(new TerminalOptions { Cols = cols, Rows = rows });

    /// <summary>A terminal with margins already set, stated 1-based as an application would.</summary>
    private static Terminal WithMargins(int left = 4, int right = 9, int cols = 20, int rows = 6)
    {
        var t = Fresh(cols, rows);
        t.Write($"{Esc}[?69h{Esc}[{left};{right}s");
        return t;
    }

    private static string Row(Terminal terminal, int row = 0)
    {
        var line = terminal.Buffer.Lines[terminal.Buffer.YBase + row]!;
        return string.Concat(Enumerable.Range(0, terminal.Cols).Select(c => line[c].Content))
                     .TrimEnd('\0', ' ');
    }

    // ---- the mode, and the sequence it unlocks -------------------------------------------------

    /// <summary>
    /// CSI s is Save Cursor until DECLRMM says otherwise. Getting this backwards would make an
    /// application's margins silently save the cursor, or a save silently set margins.
    /// </summary>
    [Fact]
    public void Without_the_mode_CSI_s_still_saves_the_cursor()
    {
        var terminal = Fresh();

        terminal.Write($"{Esc}[3;5H{Esc}[3;9s{Esc}[1;1H{Esc}[u");

        Assert.Equal(0, terminal.Buffer.ScrollLeft);
        Assert.Equal(terminal.Cols - 1, terminal.Buffer.ScrollRight);
        Assert.Equal(4, terminal.Buffer.X);
        Assert.Equal(2, terminal.Buffer.Y);
    }

    [Fact]
    public void With_the_mode_CSI_s_sets_the_margins()
    {
        var terminal = WithMargins(left: 4, right: 9);

        Assert.Equal(3, terminal.Buffer.ScrollLeft);
        Assert.Equal(8, terminal.Buffer.ScrollRight);
    }

    /// <summary>Setting margins homes the cursor, as DECSTBM does.</summary>
    [Fact]
    public void Setting_margins_homes_the_cursor()
    {
        var terminal = Fresh();
        terminal.Write($"{Esc}[4;7H{Esc}[?69h{Esc}[4;9s");

        Assert.Equal(0, terminal.Buffer.X);
        Assert.Equal(0, terminal.Buffer.Y);
    }

    /// <summary>Omitted parameters mean the extremes, so a bare CSI s under the mode widens out.</summary>
    [Fact]
    public void A_bare_sequence_widens_the_margins_again()
    {
        var terminal = WithMargins();

        terminal.Write($"{Esc}[s");

        Assert.Equal(0, terminal.Buffer.ScrollLeft);
        Assert.Equal(terminal.Cols - 1, terminal.Buffer.ScrollRight);
    }

    /// <summary>
    /// A right margin at or left of the left one is refused rather than clamped: the old margins
    /// stay, which the application can at least query, instead of a region it did not ask for.
    /// </summary>
    [Fact]
    public void A_degenerate_pair_is_refused_and_leaves_the_old_margins()
    {
        var terminal = WithMargins(left: 4, right: 9);

        terminal.Write($"{Esc}[9;4s");

        Assert.Equal(3, terminal.Buffer.ScrollLeft);
        Assert.Equal(8, terminal.Buffer.ScrollRight);
    }

    [Fact]
    public void Turning_the_mode_off_widens_the_margins()
    {
        var terminal = WithMargins();

        terminal.Write($"{Esc}[?69l");

        Assert.Equal(0, terminal.Buffer.ScrollLeft);
        Assert.Equal(terminal.Cols - 1, terminal.Buffer.ScrollRight);
    }

    /// <summary>
    /// Without a way to ask, a well-behaved application never uses the feature -- so DECRQM has to
    /// answer for this mode, not only for the ones that came before it.
    /// </summary>
    [Fact]
    public void The_mode_can_be_queried()
    {
        var terminal = Fresh();
        var replies = new List<string>();
        terminal.DataReceived += (_, e) => replies.Add(e.Data);

        terminal.Write($"{Esc}[?69$p");
        terminal.Write($"{Esc}[?69h{Esc}[?69$p");

        Assert.Equal(new[] { $"{Esc}[?69;2$y", $"{Esc}[?69;1$y" }, replies);
    }

    // ---- the operations that make it real -------------------------------------------------------

    [Fact]
    public void Text_wraps_at_the_right_margin_and_resumes_at_the_left()
    {
        var terminal = WithMargins(left: 4, right: 9);   // columns 3..8, six wide

        // Into the margins first. DECSLRM homes the cursor to column 1 of the SCREEN, not of the
        // region, unless origin mode is on -- and a cursor outside the margins is not in the region,
        // so it wraps at the screen edge like any other text. That is xterm's rule, and it is what
        // stops a status line drawn outside a pane being folded into it.
        terminal.Write($"{Esc}[1;4H");
        terminal.Write("abcdefghi");

        Assert.Equal("   abcdef", Row(terminal, 0));
        Assert.Equal("   ghi", Row(terminal, 1));
    }

    /// <summary>
    /// The batched writer bypasses the per-character wrap check, so it has to be bounded by the
    /// margin itself. Without that it writes straight through and out the other side -- and only
    /// when the fast path takes the write, which reads as an intermittent fault rather than a
    /// missing case.
    /// </summary>
    [Fact]
    public void The_batched_and_per_character_paths_agree_about_the_margin()
    {
        var batched = WithMargins(left: 4, right: 9);
        batched.Write($"{Esc}[1;4H");
        batched.Write("abcdefghijkl");

        var perCharacter = WithMargins(left: 4, right: 9);
        perCharacter.UseRunPrinting = false;
        perCharacter.Write($"{Esc}[1;4H");
        perCharacter.Write("abcdefghijkl");

        Assert.Equal(Row(perCharacter, 0), Row(batched, 0));
        Assert.Equal(Row(perCharacter, 1), Row(batched, 1));
        Assert.Equal("   abcdef", Row(batched, 0));
    }

    /// <summary>And the same through the byte entry, which is a third writer again.</summary>
    [Fact]
    public void The_byte_entry_agrees_about_the_margin()
    {
        var terminal = WithMargins(left: 4, right: 9);

        terminal.Write(System.Text.Encoding.UTF8.GetBytes($"{Esc}[1;4Habcdefghi"));

        Assert.Equal("   abcdef", Row(terminal, 0));
        Assert.Equal("   ghi", Row(terminal, 1));
    }

    /// <summary>
    /// The case the feature exists for: scrolling one pane of a side-by-side layout must leave the
    /// other pane alone. This is what a whole-line scroll gets wrong.
    /// </summary>
    [Fact]
    public void Scrolling_inside_the_margins_leaves_the_columns_outside_untouched()
    {
        var terminal = Fresh(cols: 12, rows: 4);

        terminal.Write("LLLmmmmmmRRR");
        terminal.Write($"{Esc}[2;1HLLLnnnnnnRRR");

        terminal.Write($"{Esc}[?69h{Esc}[4;9s");
        terminal.Write($"{Esc}[1;4H");
        terminal.Write($"{Esc}[S");

        Assert.Equal("LLLnnnnnnRRR", Row(terminal, 0));
        Assert.Equal("LLL      RRR", Row(terminal, 1).PadRight(12));
    }

    [Fact]
    public void Inserting_a_line_shifts_only_the_margin_columns()
    {
        var terminal = Fresh(cols: 12, rows: 4);

        terminal.Write("LLLmmmmmmRRR");
        terminal.Write($"{Esc}[?69h{Esc}[4;9s");
        terminal.Write($"{Esc}[1;4H{Esc}[L");

        Assert.Equal("LLL      RRR", Row(terminal, 0).PadRight(12));
        Assert.Equal("   mmmmmm", Row(terminal, 1));
    }

    /// <summary>
    /// From outside the margin columns there is no region to shift, so IL does nothing. A cursor in
    /// the right-hand pane shifting the left pane's lines is the corruption margins prevent.
    /// </summary>
    [Fact]
    public void Inserting_a_line_from_outside_the_margins_does_nothing()
    {
        var terminal = Fresh(cols: 12, rows: 4);

        terminal.Write("LLLmmmmmmRRR");
        terminal.Write($"{Esc}[?69h{Esc}[4;9s");
        terminal.Write($"{Esc}[1;11H{Esc}[L");

        Assert.Equal("LLLmmmmmmRRR", Row(terminal, 0));
    }

    [Fact]
    public void Inserting_characters_stops_at_the_right_margin()
    {
        var terminal = Fresh(cols: 12, rows: 4);

        terminal.Write("LLLmmmmmmRRR");
        terminal.Write($"{Esc}[?69h{Esc}[4;9s");
        terminal.Write($"{Esc}[1;4H{Esc}[2@");

        Assert.Equal("LLL  mmmmRRR", Row(terminal, 0));
    }

    [Fact]
    public void Deleting_characters_pulls_in_from_inside_the_margin_only()
    {
        var terminal = Fresh(cols: 12, rows: 4);

        terminal.Write("LLLmmmmmmRRR");
        terminal.Write($"{Esc}[?69h{Esc}[4;9s");
        terminal.Write($"{Esc}[1;4H{Esc}[2P");

        Assert.Equal("LLLmmmm  RRR", Row(terminal, 0));
    }

    /// <summary>Under origin mode the region is a box, so column 1 is the left margin.</summary>
    [Fact]
    public void Origin_mode_addresses_columns_from_the_left_margin()
    {
        var terminal = WithMargins(left: 4, right: 9);

        terminal.Write($"{Esc}[?6h{Esc}[1;1Hx");

        Assert.Equal("   x", Row(terminal, 0));
    }

    // ---- and what has to survive ---------------------------------------------------------------

    [Fact]
    public void A_resize_clamps_the_margins()
    {
        var terminal = WithMargins(left: 4, right: 15, cols: 20);

        terminal.Resize(8, terminal.Rows);

        Assert.Equal(3, terminal.Buffer.ScrollLeft);
        Assert.Equal(7, terminal.Buffer.ScrollRight);
    }

    /// <summary>
    /// A resize that would leave the region degenerate widens it instead, rather than leaving a
    /// region no write could land in.
    /// </summary>
    [Fact]
    public void A_resize_past_the_left_margin_widens_them_again()
    {
        var terminal = WithMargins(left: 10, right: 15, cols: 20);

        terminal.Resize(4, terminal.Rows);

        Assert.Equal(0, terminal.Buffer.ScrollLeft);
        Assert.Equal(3, terminal.Buffer.ScrollRight);
    }

    [Fact]
    public void A_full_reset_widens_the_margins_and_clears_the_mode()
    {
        var terminal = WithMargins();

        terminal.Write($"{Esc}c");

        Assert.Equal(0, terminal.Buffer.ScrollLeft);
        Assert.Equal(terminal.Cols - 1, terminal.Buffer.ScrollRight);
        Assert.False(terminal.LeftRightMarginMode);
    }

    /// <summary>
    /// With the mode off, nothing changes anywhere. This is the regression that matters most, since
    /// margins are off for every application that has never heard of them.
    /// </summary>
    [Fact]
    public void With_no_margins_set_everything_behaves_as_before()
    {
        var terminal = Fresh(cols: 8, rows: 3);

        terminal.Write("abcdefghij");

        Assert.Equal("abcdefgh", Row(terminal, 0));
        Assert.Equal("ij", Row(terminal, 1));
    }

    /// <summary>
    /// A box scroll neither sets nor clears any line's IsWrapped flag — including the wrap-driven
    /// scroll at the bottom of the region. The flag is per LINE, and every line keeps its content
    /// outside the margins; marking the bottom line wrapped would claim continuation for content
    /// that never moved, and a later reflow would merge full lines an application laid out
    /// separately. So the wrapped lines outside the region stay wrapped, and the region's own
    /// lines stay unwrapped, no matter how much the box scrolls.
    /// </summary>
    [Fact]
    public void A_box_scroll_leaves_every_IsWrapped_flag_alone()
    {
        var terminal = Fresh(cols: 8, rows: 6);

        // A genuinely wrapped pair of rows below the future region, made by autowrap.
        terminal.Write($"{Esc}[5;1H");
        terminal.Write("0123456789");
        Assert.True(terminal.Buffer.Lines[terminal.Buffer.YBase + 5]!.IsWrapped,
            "sanity: autowrap marked the continuation row");

        // Margins over rows 1-4, columns 3-6; fill the box until it wrap-scrolls repeatedly.
        terminal.Write($"{Esc}[?69h{Esc}[3;6s{Esc}[1;4r");
        terminal.Write($"{Esc}[1;3H");
        terminal.Write(new string('x', 30));

        for (var row = 0; row < 4; row++)
            Assert.False(terminal.Buffer.Lines[terminal.Buffer.YBase + row]!.IsWrapped,
                $"row {row} is inside the box and must not become a continuation");
        Assert.True(terminal.Buffer.Lines[terminal.Buffer.YBase + 5]!.IsWrapped,
            "the wrapped pair below the region is untouched by the box scrolling");

        terminal.Write($"{Esc}[?69l{Esc}[r");
    }

    // ---- the pending-wrap boundary column ------------------------------------------------------

    /// <summary>
    /// A full-width line leaves the cursor at X == Cols, the pending-wrap state — and that is the
    /// ORDINARY place for IL to run from, margins or not. Reading it as "outside the region" made
    /// IL a silent no-op on the default path after any full-width line.
    /// </summary>
    [Fact]
    public void Inserting_a_line_still_works_from_the_pending_wrap_state()
    {
        var terminal = Fresh();
        terminal.Write($"{Esc}[2;1Hbelow");
        terminal.Write($"{Esc}[1;1H{new string('A', terminal.Cols)}");

        terminal.Write($"{Esc}[L");

        Assert.Equal("", Row(terminal, 0));
        Assert.Equal(new string('A', terminal.Cols), Row(terminal, 1));
        Assert.Equal("below", Row(terminal, 2));
    }

    [Fact]
    public void Deleting_a_line_still_works_from_the_pending_wrap_state()
    {
        var terminal = Fresh();
        terminal.Write($"{Esc}[2;1Hsecond");
        terminal.Write($"{Esc}[1;1H{new string('A', terminal.Cols)}");

        terminal.Write($"{Esc}[M");

        Assert.Equal("second", Row(terminal, 0));
    }

    /// <summary>
    /// ScrollRight + 1 is two states with one column: the pending-wrap residue of filling the
    /// region's last column, and a deliberate placement at the first column of the NEXT pane — an
    /// ordinary cursor position in the layout this feature exists for. The buffer's PendingWrap
    /// flag tells them apart: a deliberately placed cursor is outside the region, so writing there
    /// stays there instead of wrapping back into the pane to its left.
    /// </summary>
    [Fact]
    public void Writing_just_right_of_the_margin_does_not_wrap_into_the_pane()
    {
        var terminal = WithMargins(left: 4, right: 9);   // zero-based columns 3..8

        terminal.Write($"{Esc}[1;10Hx");                 // column 9: the next pane's first column

        var line = terminal.Buffer.Lines[terminal.Buffer.YBase]!;
        Assert.Equal("x", line[9].Content);
        Assert.Equal(0, terminal.Buffer.Y);              // no wrap happened...
        Assert.False(terminal.Buffer.Lines[terminal.Buffer.YBase + 1]!.IsWrapped);   // ...marked or otherwise
    }

    // ---- ICH and DCH are bounded by BOTH margins -----------------------------------------------

    /// <summary>
    /// A cursor outside the margins on either side must make ICH/DCH do nothing. Only the right
    /// side was guarded at first: from LEFT of the left margin, ICH shifted the left pane's
    /// columns across the margin into the right pane, and DCH pulled the right pane's columns
    /// back across — content crossing the exact boundary margins exist to seal.
    /// </summary>
    [Theory]
    [InlineData("@", 2)]     // ICH, cursor left of the left margin
    [InlineData("@", 12)]    // ICH, cursor right of the right margin
    [InlineData("P", 2)]     // DCH, left
    [InlineData("P", 12)]    // DCH, right
    public void Insert_and_delete_chars_do_nothing_from_outside_the_margins(string op, int column)
    {
        var terminal = WithMargins(left: 4, right: 9);
        terminal.Write($"{Esc}[1;1Habcdefghijklmnopqrst");

        terminal.Write($"{Esc}[1;{column}H{Esc}[3{op}");

        Assert.Equal("abcdefghijklmnopqrst", Row(terminal, 0));
    }

    // ---- relative cursor movement honours the margins ------------------------------------------

    /// <summary>
    /// CUF stops at the right margin when the cursor starts inside the region and at the screen
    /// edge when it starts outside — in/out decides, not origin mode, as in xterm. An application
    /// that queried DECRQM for 69 and moved within its pane with CUF must not end up in the
    /// neighbouring one.
    /// </summary>
    [Fact]
    public void CursorForward_stops_at_the_right_margin_from_inside()
    {
        var terminal = WithMargins(left: 4, right: 9);
        terminal.Write($"{Esc}[1;5H{Esc}[200C");
        Assert.Equal(8, terminal.Buffer.X);

        terminal.Write($"{Esc}[1;12H{Esc}[200C");
        Assert.Equal(terminal.Cols - 1, terminal.Buffer.X);
    }

    [Fact]
    public void CursorBackward_stops_at_the_left_margin_from_inside()
    {
        var terminal = WithMargins(left: 4, right: 9);
        terminal.Write($"{Esc}[1;7H{Esc}[200D");
        Assert.Equal(3, terminal.Buffer.X);

        terminal.Write($"{Esc}[1;2H{Esc}[200D");
        Assert.Equal(0, terminal.Buffer.X);
    }

    // ---- carriage return and the left margin ---------------------------------------------------

    /// <summary>
    /// CR goes to the LEFT MARGIN when the cursor is at or right of it, and to column 0 only
    /// when the cursor is left of it — xterm’s rule, with origin mode not consulted. A cursor
    /// inside the region cannot escape it leftward: a CRLF emitted by an application drawing
    /// inside its pane must start the next line at the pane’s edge, not in the pane next door.
    /// </summary>
    [Fact]
    public void CR_returns_to_the_left_margin_from_inside_the_region()
    {
        var terminal = WithMargins(left: 4, right: 9);
        terminal.Write($"{Esc}[1;7H\r");
        Assert.Equal(3, terminal.Buffer.X);
    }

    [Fact]
    public void CR_returns_to_column_zero_from_left_of_the_margin()
    {
        var terminal = WithMargins(left: 4, right: 9);
        terminal.Write($"{Esc}[1;2H\r");
        Assert.Equal(0, terminal.Buffer.X);
    }

    /// <summary>
    /// Everything that "returns the carriage" shares CR’s rule, as in xterm: NEL is Index plus
    /// CR, and CNL/CPL are CUD/CUU plus CR. None of them consult origin mode.
    /// </summary>
    [Theory]
    [InlineData("E")]     // ESC E, NEL — written as CSI-free below
    [InlineData("[E")]    // CSI E, CNL
    [InlineData("[F")]    // CSI F, CPL
    public void NEL_CNL_and_CPL_follow_the_CR_rule(string tail)
    {
        var terminal = WithMargins(left: 4, right: 9);

        terminal.Write($"{Esc}[2;7H{Esc}{tail}");    // from inside the region
        Assert.Equal(3, terminal.Buffer.X);

        terminal.Write($"{Esc}[2;2H{Esc}{tail}");    // from left of the margin
        Assert.Equal(0, terminal.Buffer.X);
    }

    [Fact]
    public void ConvertEol_returns_to_the_left_margin_too()
    {
        var terminal = new Terminal(new TerminalOptions { Cols = 20, Rows = 6, ConvertEol = true });
        terminal.Write($"{Esc}[?69h{Esc}[4;9s");
        terminal.Write($"{Esc}[1;7H\n");
        Assert.Equal(3, terminal.Buffer.X);
    }
}
