using XTerm;
using XTerm.Options;

namespace XTerm.Tests;

/// <summary>
/// The canned status reports: the DSR family beyond CPR, DECID, and the level-aware DA replies.
/// Expected strings are what xterm sends, which is what esctest grades against.
/// </summary>
public class DeviceReportTests
{
    private const string Esc = "\u001b";

    private static (Terminal terminal, List<string> replies) Create()
    {
        var terminal = new Terminal(new TerminalOptions { Cols = 80, Rows = 24 });
        var replies = new List<string>();
        terminal.DataReceived += (_, e) => replies.Add(e.Data);
        return (terminal, replies);
    }

    [Theory]
    [InlineData("[?53n", "[?53n")]              // DEC locator status: available
    [InlineData("[?55n", "[?53n")]              // xterm's locator status alias
    [InlineData("[?56n", "[?57;1n")]            // locator type: mouse
    [InlineData("[?62n", "[0*{")]               // DECMSR: no macro space (unprefixed reply)
    [InlineData("[?63;123n", "P123!~0000\u001b\\")] // DECCKSR echoes the id; no macros, zero sum
    [InlineData("[?75n", "[?70n")]              // data integrity: no errors
    [InlineData("[?85n", "[?83n")]              // multiple sessions: not configured
    public void CannedDsrReports_AnswerLikeXterm(string query, string reply)
    {
        var (terminal, replies) = Create();
        terminal.Write(Esc + query);
        Assert.Equal(Esc + reply, Assert.Single(replies));
    }

    [Fact]
    public void Decid_AnswersWithThePrimaryDeviceAttributes()
    {
        var (terminal, replies) = Create();
        terminal.Write(Esc + "Z");
        terminal.Write(Esc + "[c");
        Assert.Equal(2, replies.Count);
        Assert.Equal(replies[1], replies[0]);
        Assert.StartsWith(Esc + "[?65;", replies[0]);
    }

    [Fact]
    public void PrimaryDa_FollowsTheConformanceLevelDown()
    {
        var (terminal, replies) = Create();
        terminal.Write(Esc + "[63;1\"p");        // DECSCL: drop to level 3
        terminal.Write(Esc + "[c");
        Assert.StartsWith(Esc + "[?63;", Assert.Single(replies));
        Assert.DoesNotContain(";28", replies[0]); // no rectangular-editing claim below level 4
    }

    [Fact]
    public void SecondaryDa_ReportsTheVt520FamilyAndPatchLevel()
    {
        var (terminal, replies) = Create();
        terminal.Write(Esc + "[>c");
        Assert.Equal(Esc + "[>64;383;0c", Assert.Single(replies));
    }

    [Fact]
    public void Decxcpr_ReportsThePageAndHonoursOriginMode()
    {
        var (terminal, replies) = Create();
        terminal.Write(Esc + "[?6l");            // origin mode off
        terminal.Write(Esc + "[3;7H");
        terminal.Write(Esc + "[?6n");
        Assert.Equal(Esc + "[?3;7;1R", replies[^1]);

        terminal.Write(Esc + "[5;20r" + Esc + "[?6h" + Esc + "[2;4H");
        terminal.Write(Esc + "[?6n");
        Assert.Equal(Esc + "[?2;4;1R", replies[^1]);
    }

    [Fact]
    public void TertiaryDa_ReportsAUnitIdOfZeros()
    {
        var (terminal, replies) = Create();
        terminal.Write(Esc + "[=c");
        Assert.Equal(Esc + "P!|00000000" + Esc + "\\", Assert.Single(replies));
    }

    /// <summary>
    /// DECRQDE, vttest menu 11.2.5 -> 6. The window IS the page, so the corner is 1;1 and there is
    /// one page; the size is the same one CSI 18 t already reports in the dtterm dialect.
    /// </summary>
    [Fact]
    public void Decrqde_ReportsTheDisplayedExtent()
    {
        var (terminal, replies) = Create();
        terminal.Write(Esc + "[\"v");
        Assert.Equal(Esc + "[24;80;1;1;1\"w", Assert.Single(replies));
    }

    [Fact]
    public void Decrqde_IsSilentBelowVt300()
    {
        var (terminal, replies) = Create();
        terminal.Write(Esc + "[62\"p");          // DECSCL: VT200
        replies.Clear();
        terminal.Write(Esc + "[\"v");
        Assert.Empty(replies);
    }

    /// <summary>
    /// DECRQUPSS, vttest menu 11.2.5 -> 5. A UTF-8 terminal's supplemental set is ISO Latin-1,
    /// a 96-character set, which is the Ps = 1 form and the designator 'A'.
    /// </summary>
    [Fact]
    public void Decrqupss_ReportsIsoLatin1()
    {
        var (terminal, replies) = Create();
        terminal.Write(Esc + "[&u");
        Assert.Equal(Esc + "P1!uA" + Esc + "\\", Assert.Single(replies));
    }

    /// <summary>
    /// DECRQTSR, vttest menu 11.2.5 -> 4 -> 2. There is no DECRSTS to consume a terminal state
    /// report, so the answer is the invalid-request form rather than a payload nothing can restore
    /// -- and rather than the silence a client blocks on, which is how DECRQSS already declines.
    /// </summary>
    [Fact]
    public void Decrqtsr_DeclinesInsteadOfStayingSilent()
    {
        var (terminal, replies) = Create();
        terminal.Write(Esc + "[1$u");
        Assert.Equal(Esc + "P0$s" + Esc + "\\", Assert.Single(replies));
    }

    [Fact]
    public void Decrqtsr_WithNoParameterAsksForNothing()
    {
        var (terminal, replies) = Create();
        terminal.Write(Esc + "[$u");
        Assert.Empty(replies);
    }

    [Fact]
    public void Decrqcra_IsSilentBelowLevel64()
    {
        var (terminal, replies) = Create();
        terminal.Write(Esc + "[64\"p");
        terminal.Write(Esc + "[1;0;1;1;1;1*y");
        Assert.Single(replies);                 // answered at VT400

        replies.Clear();
        terminal.Write(Esc + "[62\"p");      // DECSCL: VT200
        terminal.Write(Esc + "[1;0;1;1;1;1*y");
        Assert.Empty(replies);
    }

    [Fact]
    public void Decrqtsr_IsSilentBelowLevel64()
    {
        // The control is VT320 vintage, but the capability the primary DA offers for it --
        // attribute 17, terminal state interrogation -- is advertised only from level 64.
        // Declining a request the DA reply has already said the terminal does not take is the
        // terminal contradicting itself.
        var (terminal, replies) = Create();
        terminal.Write(Esc + "[62\"p");
        terminal.Write(Esc + "[1$u");
        Assert.Empty(replies);
    }
}
