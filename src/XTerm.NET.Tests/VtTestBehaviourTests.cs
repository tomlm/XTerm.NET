using XTerm.Options;

namespace XTerm.Tests;

/// <summary>
/// Behaviour that vttest 2.7 (20230201) exercises and this emulator gets RIGHT.
///
/// <para>These are guards, not discoveries. Each was probed while sweeping vttest, several of them
/// because they looked wrong at first and turned out not to be — margins clipping what a screen
/// dump made look like lost text, reverse wraparound that only engages on a line which actually
/// wrapped. Writing them down is what stops the next reader re-deriving the same false alarm.</para>
///
/// <para>None of these can be seen in a screen dump: they are attributes, cell protection, cursor
/// mechanics and mode gating. That is why they are tests rather than a diff against another
/// terminal — the comparison that found the rest of these cases is blind to every one of them.</para>
///
/// <para>vttest is Copyright 1996-2022 by Thomas E. Dickey, under an X11-style licence. No vttest
/// source is copied here.</para>
/// </summary>
public class VtTestBehaviourTests
{
    private const string Esc = "\u001b";
    private const string ShiftOut = "\u000e";   // SO -- invoke G1 into GL
    private const string ShiftIn = "\u000f";    // SI -- back to G0

    private static Terminal Sized(int cols = 40, int rows = 6) =>
        new(new TerminalOptions { Cols = cols, Rows = rows });

    private static string BackgroundOf(Terminal terminal, int row, int col) =>
        terminal.Buffer.Lines[row]![col].Attributes.Bg.ToString();

    /// <summary>
    /// BCE: an erase fills with the CURRENT background, not the default one. vttest menu 11.6.4
    /// and 11.6.5, which a text-only comparison cannot see at all.
    /// </summary>
    [Fact]
    public void Erases_fill_with_the_current_background()
    {
        var terminal = Sized(20, 5);

        terminal.Write($"{Esc}[44m{Esc}[2J");                       // blue, erase display
        Assert.Equal("4", BackgroundOf(terminal, 0, 0));
        Assert.Equal("4", BackgroundOf(terminal, 4, 19));

        terminal.Write($"{Esc}[H{Esc}[41m{Esc}[K");                 // red, erase to end of line
        Assert.Equal("1", BackgroundOf(terminal, 0, 0));

        terminal.Write($"{Esc}[42m{Esc}[5X");                       // green, erase 5 characters
        Assert.Equal("2", BackgroundOf(terminal, 0, 0));
    }

    /// <summary>
    /// SGR 22 clears bold AND dim. They share one code, and clearing only one of them is the
    /// classic way this goes wrong. vttest menu 11.6.9.
    /// </summary>
    [Fact]
    public void SGR_22_clears_both_bold_and_dim()
    {
        var terminal = Sized(20, 3);

        terminal.Write($"{Esc}[1;2mX");
        var bright = terminal.Buffer.Lines[0]![0].Attributes;
        Assert.True(bright.IsBold());
        Assert.True(bright.IsDim());

        terminal.Write($"{Esc}[22mY");
        var cleared = terminal.Buffer.Lines[0]![1].Attributes;
        Assert.False(cleared.IsBold());
        Assert.False(cleared.IsDim());
    }

    /// <summary>DECSCA marks cells that DECSEL must not erase; ECH is not selective and erases them.</summary>
    [Fact]
    public void Selective_erase_respects_protection_and_ECH_does_not()
    {
        var terminal = Sized(20, 3);

        terminal.Write($"{Esc}[2J{Esc}[H{Esc}[1\"qPROT{Esc}[0\"qPLAIN");
        terminal.Write($"{Esc}[H{Esc}[?0K");                        // DECSEL to end of line
        Assert.Equal("PROT", terminal.GetLine(0));

        terminal.Write($"{Esc}[2J{Esc}[H{Esc}[1\"qPROT{Esc}[0\"q");
        terminal.Write($"{Esc}[H{Esc}[4X");                         // ECH over the same cells
        Assert.Equal(string.Empty, terminal.GetLine(0));
    }

    /// <summary>
    /// Left/right margins clip editing and wrap text, and the text outside them is left alone.
    /// </summary>
    /// <remarks>
    /// The wrap is the part worth pinning. With margins at 3..10 a sixteen-character write does not
    /// run to column 16 — it wraps at the right margin and resumes at the LEFT margin on the next
    /// row, which reads as lost text in a screen dump and is not.
    /// </remarks>
    [Fact]
    public void Left_and_right_margins_clip_and_wrap()
    {
        var terminal = Sized(40, 6);

        terminal.Write($"{Esc}[2J{Esc}[?69h{Esc}[3;10s");           // DECLRMM, margins 3..10
        terminal.Write($"{Esc}[1;1HABCDEFGHIJKLMNOP");

        Assert.Equal("ABCDEFGHIJ", terminal.GetLine(0));
        Assert.Equal("  KLMNOP", terminal.GetLine(1));

        terminal.Write($"{Esc}[1;3H{Esc}[2P");                      // DCH inside the margins
        Assert.Equal("ABEFGHIJ", terminal.GetLine(0));
        Assert.Equal("  KLMNOP", terminal.GetLine(1));              // untouched beyond the margin
    }

    /// <summary>
    /// Reverse wraparound (mode 45) steps back onto a line that WRAPPED, and only then.
    /// </summary>
    /// <remarks>
    /// The condition is the whole test. Backing up from a row the cursor reached by an explicit
    /// move must not wrap, which is why a first attempt at this looked like the feature was missing.
    /// </remarks>
    [Fact]
    public void Reverse_wraparound_applies_to_a_wrapped_line()
    {
        var wrapped = Sized(6, 3);
        wrapped.Write($"{Esc}[2J{Esc}[?7h{Esc}[?45h");
        wrapped.Write("ABCDEFG");                                   // wraps after column 6
        wrapped.Write("\b\bZ");
        Assert.Equal("ABCDEZ", wrapped.GetLine(0));

        var off = Sized(6, 3);
        off.Write($"{Esc}[2J{Esc}[?7h{Esc}[?45l");
        off.Write("ABCDEFG");
        off.Write("\b\bZ");
        Assert.Equal("ABCDEF", off.GetLine(0));
        Assert.Equal("Z", off.GetLine(1));
    }

    /// <summary>IRM inserts rather than overwrites. vttest menu 8.</summary>
    [Fact]
    public void Insert_mode_shifts_instead_of_overwriting()
    {
        var replace = Sized(20, 3);
        replace.Write($"{Esc}[2J{Esc}[4l{Esc}[1;1HABCDEF{Esc}[1;3HXY");
        Assert.Equal("ABXYEF", replace.GetLine(0));

        var insert = Sized(20, 3);
        insert.Write($"{Esc}[2J{Esc}[4h{Esc}[1;1HABCDEF{Esc}[1;3HXY");
        Assert.Equal("ABXYCDEF", insert.GetLine(0));
    }

    /// <summary>HTS sets a stop that TAB lands on, after TBC has cleared the defaults.</summary>
    [Fact]
    public void A_tab_stop_can_be_set_and_landed_on()
    {
        var terminal = Sized(40, 3);

        terminal.Write($"{Esc}[2J{Esc}[3g");                        // clear all stops
        terminal.Write($"{Esc}[1;1HA{Esc}[1;10H{Esc}H");            // stop at column 10
        terminal.Write($"{Esc}[1;1H\tB");

        Assert.Equal("A        B", terminal.GetLine(0));
    }

    /// <summary>
    /// DECRQCRA's arithmetic: the checksum is the negated 16-bit sum of the characters.
    /// </summary>
    /// <remarks>
    /// Only the part that is settled. What an UNTOUCHED cell should contribute is the open question
    /// in tomlm/XTerm.NET#128 and is deliberately not asserted here — this pins the negated sum so a
    /// fix for that cannot quietly change the rest of it.
    /// </remarks>
    [Fact]
    public void Rectangular_area_checksums_negate_the_sum_of_the_characters()
    {
        var terminal = Sized(20, 5);
        var replies = new List<string>();
        terminal.DataReceived += (_, e) => replies.Add(e.Data);

        terminal.Write($"{Esc}[2;1HAB");
        terminal.Write($"{Esc}[1;1;2;1;2;2*y");                     // DECRQCRA over just "AB"

        // 0x41 + 0x42 = 0x83, negated over 16 bits is 0xFF7D.
        Assert.Equal($"{Esc}P1!~FF7D{Esc}\\", Assert.Single(replies));
    }

    /// <summary>
    /// The three ways to change the page size differ only in whether they erase.
    /// </summary>
    /// <remarks>
    /// DECCOLM erases, DECNCSM (mode 95) turns that off, and DECSCPP never erases at all -- it sets
    /// the width and says nothing about the contents. vttest's page-format tests cannot check any of
    /// this through a pty: it samples the screen size when a test starts and does not re-measure, so
    /// its "80 of 132 columns" is its own view rather than the terminal's.
    /// </remarks>
    [Fact]
    public void Page_size_controls_differ_only_in_whether_they_erase()
    {
        var terminal = Sized(80, 24);
        terminal.Write($"{Esc}[?40h");                              // Allow80To132

        // DECCOLM: resizes and erases.
        terminal.Write($"{Esc}[1;1HKEEP ME");
        terminal.Write($"{Esc}[?3h");
        Assert.Equal(132, terminal.Cols);
        Assert.Equal(string.Empty, terminal.GetLine(0));

        // DECNCSM: resizes and keeps.
        terminal.Write($"{Esc}[?95h");
        terminal.Write($"{Esc}[1;1HKEEP ME");
        terminal.Write($"{Esc}[?3l");
        Assert.Equal(80, terminal.Cols);
        Assert.Equal("KEEP ME", terminal.GetLine(0));

        // DECSCPP: resizes and keeps, whatever DECNCSM says.
        terminal.Write($"{Esc}[?95l");
        terminal.Write($"{Esc}[132$|");
        Assert.Equal(132, terminal.Cols);
        Assert.Equal("KEEP ME", terminal.GetLine(0));

        terminal.Write($"{Esc}[80$|");
        Assert.Equal(80, terminal.Cols);
    }

    /// <summary>
    /// DECSLPP sets the page length. It already worked, and is pinned because a sweep reported it
    /// as broken on the strength of vttest's own row count -- which is not evidence about this.
    /// </summary>
    [Fact]
    public void Lines_per_page_resizes_the_terminal()
    {
        var terminal = Sized(80, 24);

        terminal.Write($"{Esc}[25t");
        Assert.Equal(25, terminal.Rows);

        terminal.Write($"{Esc}[48t");
        Assert.Equal(48, terminal.Rows);
    }

    /// <summary>
    /// CHT is n tabs, including at the phantom column. vttest menu 11.8.1.2 draws the same row of
    /// marks with tabs and with CHT and expects them to look the same.
    /// </summary>
    /// <remarks>
    /// They differed because CHT moved the cursor without checking for a pending wrap, which
    /// cancelled it -- so the next printable overwrote the last column instead of wrapping. The two
    /// share one motion now; this is what stops them drifting a third time.
    /// </remarks>
    [Theory]
    [InlineData("\t")]
    [InlineData("\u001b[1I")]
    [InlineData("\u001b[I")]
    public void Tabbing_off_the_line_wraps_the_same_way_however_it_is_spelled(string tab)
    {
        var terminal = Sized(20, 4);
        terminal.Write($"{Esc}[2J{Esc}[H");

        for (var i = 0; i < 5; i++)
        {
            terminal.Write(tab);
            terminal.Write("*");
        }

        Assert.Equal("        *       *  *", terminal.GetLine(0));
        Assert.Equal("*       *", terminal.GetLine(1));
    }

    /// <summary>
    /// DEC Special Graphics maps the whole run, including the four control pictures.
    /// </summary>
    /// <remarks>
    /// b, c, d and e were missing from the table and came out as plain letters, sitting unnoticed
    /// in the middle of a row of symbols that were all correct. Reported as tomlm/XTerm.NET#136.
    /// </remarks>
    [Fact]
    public void Special_graphics_maps_the_control_pictures_too()
    {
        var terminal = Sized(30, 3);

        terminal.Write($"{Esc}(0`abcdefghi{Esc}(B");

        Assert.Equal("◆▒␉␌␍␊°±␤␋", terminal.GetLine(0));
    }

    /// <summary>
    /// The 94- and 96-character-set designators name DIFFERENT sets with the same letter.
    /// </summary>
    /// <remarks>
    /// 'A' is the United Kingdom set after ESC ( and ISO Latin-1 after ESC -. Routing the 96-set
    /// forms through the 94-set lookup would designate UK for a program that asked for Latin-1 and
    /// silently turn its '#' into a pound sign -- invisible in any test whose text avoids that one
    /// character.
    /// </remarks>
    [Fact]
    public void The_96_character_set_designators_are_a_separate_space()
    {
        // Both halves designate G1 and invoke it with SO, so they differ by ONE thing: the
        // space the designator came from. Designating G0 for one and G1 for the other left
        // the comparison carrying a second difference it was not trying to test.
        var uk = Sized(30, 3);
        uk.Write($"{Esc})A{ShiftOut}#@[{ShiftIn}");
        Assert.Equal("£@[", uk.GetLine(0));

        var latin1 = Sized(30, 3);
        latin1.Write($"{Esc}-A{ShiftOut}#@[{ShiftIn}");
        Assert.Equal("#@[", latin1.GetLine(0));
    }

    /// <summary>
    /// A bracketed paste keeps both halves of its frame, whatever S8C1T says.
    /// </summary>
    /// <remarks>
    /// Paste is keyboard-direction traffic, and the reply converter rewrites only a LEADING
    /// introducer -- so running paste through it produced an 8-bit opening bracket and a 7-bit
    /// closing one, and an application watching for the end of the paste never saw it.
    /// </remarks>
    [Fact]
    public void A_paste_is_not_converted_by_S8C1T()
    {
        var terminal = Sized(40, 4);
        var sent = new List<string>();
        terminal.DataReceived += (_, e) => sent.Add(e.Data);

        terminal.Write($"{Esc}[?2004h{Esc} G");
        terminal.Paste("hi");

        Assert.Equal($"{Esc}[200~hi{Esc}[201~", Assert.Single(sent));
    }

    /// <summary>
    /// DECNCSM and DECNRCM come back down on both resets, in behaviour AND in what DECRQM says.
    /// </summary>
    [Theory]
    [InlineData("ESCc")]
    [InlineData("ESC[!p")]
    public void The_new_modes_do_not_survive_a_reset(string reset)
    {
        var terminal = Sized(40, 4);
        var replies = new List<string>();
        terminal.DataReceived += (_, e) => replies.Add(e.Data);

        terminal.Write($"{Esc}[?95h{Esc}[?42h");
        terminal.Write(reset.Replace("ESC", Esc));

        terminal.Write($"{Esc}[?95$p");
        Assert.Equal($"{Esc}[?95;2$y", replies[^1]);

        terminal.Write($"{Esc}[?42$p");
        Assert.Equal($"{Esc}[?42;2$y", replies[^1]);

        // And the flag is really down, not merely reported down.
        terminal.Write($"{Esc}(R@[");
        Assert.Equal("@[", terminal.GetLine(0));
    }

    /// <summary>
    /// Text that SURVIVES a full erase keeps its line width.
    /// </summary>
    /// <remarks>
    /// Under ISO protection a guarded cell survives even a plain erase, so resetting the attribute
    /// on the way in would have shrunk a line that still had double-width text on it.
    /// </remarks>
    [Fact]
    public void A_full_erase_keeps_the_width_of_a_line_whose_text_survived()
    {
        var terminal = Sized(20, 3);

        terminal.Write($"{Esc}[1;1H{Esc}#6");
        terminal.Write($"{Esc}VKEEP{Esc}W");
        terminal.Write($"{Esc}[2J");

        Assert.Equal("KEEP", terminal.GetLine(0));
        Assert.Equal(XTerm.Buffer.LineAttribute.DoubleWidth, terminal.Buffer.Lines[0]!.LineAttribute);
    }

    /// <summary>DECSCPP declines a width it does not define rather than rounding to one.</summary>
    [Theory]
    [InlineData(81)]
    [InlineData(999)]
    public void Set_columns_per_page_ignores_a_width_it_does_not_define(int columns)
    {
        var terminal = Sized(80, 24);

        terminal.Write($"{Esc}[{columns}$|");

        Assert.Equal(80, terminal.Cols);
    }

    /// <summary>A national set reached by its alternate designator resolves to the same table.</summary>
    [Fact]
    public void A_national_set_answers_to_both_of_its_designators()
    {
        foreach (var designator in new[] { "R", "f" })
        {
            var terminal = Sized(20, 3);
            terminal.Write($"{Esc}[?42h{Esc}({designator}#@[");
            Assert.Equal("£à°", terminal.GetLine(0));
        }
    }

    /// <summary>
    /// DECRC puts back the DESIGNATION, so a later DECNRCM re-resolves what was restored.
    /// </summary>
    /// <remarks>
    /// <para>DECSC saved the table each G-set had resolved to rather than what it was designated
    /// as, so the identifier behind a restored slot stayed as whatever had been designated AFTER
    /// the save. The screen was right and the state behind it was not, which is why this needs a
    /// mode change to show at all: the next DECNRCM re-resolves the restored slot into the wrong
    /// set, arbitrarily far from the DECRC that caused it.</para>
    ///
    /// <para>Both spaces, because they fail differently. The 94-set pair loses line drawing to a
    /// national set -- ESC ( 0, DECSC, ESC ( R, DECRC draws borders until the mode moves and then
    /// draws letters. The 96-set pair is the identifier collision again: Latin-1 restored, then
    /// re-resolved as the United Kingdom set.</para>
    /// </remarks>
    [Fact]
    public void DECRC_restores_what_was_designated_not_what_it_resolved_to()
    {
        var graphics = Sized(30, 3);
        graphics.Write($"{Esc})0{Esc}7{Esc})R{Esc}8");        // graphics, DECSC, French, DECRC
        graphics.Write($"{Esc}[?42h");                        // DECNRCM, which re-resolves
        graphics.Write($"{ShiftOut}qqq{ShiftIn}");
        Assert.Equal("───", graphics.GetLine(0));

        var latin1 = Sized(30, 3);
        latin1.Write($"{Esc}-A{Esc}7{Esc})A{Esc}8");          // Latin-1, DECSC, UK, DECRC
        latin1.Write($"{Esc}[?42h");
        latin1.Write($"{ShiftOut}#@[{ShiftIn}");
        Assert.Equal("#@[", latin1.GetLine(0));

        // And the restore itself, which was never the broken half: without the mode change both
        // of the above already came back right, and a test that stopped there would pass on the
        // defect.
        var immediate = Sized(30, 3);
        immediate.Write($"{Esc})0{Esc}7{Esc})R{Esc}8");
        immediate.Write($"{ShiftOut}qqq{ShiftIn}");
        Assert.Equal("───", immediate.GetLine(0));

        // DECNRCM moving BETWEEN the save and the restore, both directions. This is the half the
        // doc comment claims and the three cases above do not reach: they move the mode after the
        // DECRC, so replaying a saved TABLE would satisfy them. Here the table saved and the table
        // wanted are different, and only re-resolving the designation produces the second one.
        var modeOnAfterSave = Sized(30, 3);
        modeOnAfterSave.Write($"{Esc})R{Esc}7");              // French designated with NRC OFF
        modeOnAfterSave.Write($"{Esc}[?42h{Esc}8");           // NRC on, then restore
        modeOnAfterSave.Write($"{ShiftOut}@{ShiftIn}");
        Assert.Equal("à", modeOnAfterSave.GetLine(0));        // French, not the ASCII it saved

        var modeOffAfterSave = Sized(30, 3);
        modeOffAfterSave.Write($"{Esc}[?42h{Esc})R{Esc}7");   // French designated with NRC ON
        modeOffAfterSave.Write($"{Esc}[?42l{Esc}8");          // NRC off, then restore
        modeOffAfterSave.Write($"{ShiftOut}@{ShiftIn}");
        Assert.Equal("@", modeOffAfterSave.GetLine(0));       // ASCII, not the French it saved
    }
}
