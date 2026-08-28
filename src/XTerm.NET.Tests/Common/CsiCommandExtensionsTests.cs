using XTerm.Common;

namespace XTerm.Tests.Common;

/// <summary>
/// The CSI identifier the parser hands to the input handler carries whatever it collected before
/// the final character -- the private marker among it. The lookup used to strip a leading '?' or
/// '>' and match on the rest, which made every private sequence an alias for whichever non-private
/// command shared its final character. These tests pin the identifier match down as exact.
/// </summary>
public class CsiCommandExtensionsTests
{
    [Theory]
    [InlineData("S", CsiCommand.ScrollUp)]
    [InlineData("J", CsiCommand.EraseInDisplay)]
    [InlineData("K", CsiCommand.EraseInLine)]
    [InlineData("h", CsiCommand.SetMode)]
    [InlineData("l", CsiCommand.ResetMode)]
    [InlineData("c", CsiCommand.DeviceAttributes)]
    [InlineData("n", CsiCommand.DeviceStatusReport)]
    [InlineData("m", CsiCommand.SelectGraphicRendition)]
    [InlineData("r", CsiCommand.SetScrollRegion)]
    [InlineData("s", CsiCommand.SaveCursorAnsi)]
    [InlineData("t", CsiCommand.WindowManipulation)]
    [InlineData("u", CsiCommand.RestoreCursorAnsi)]
    [InlineData("$p", CsiCommand.RequestMode)]
    [InlineData(" q", CsiCommand.SelectCursorStyle)]
    public void ToCsiCommand_MapsNonPrivateIdentifiers(string identifier, CsiCommand command)
    {
        Assert.Equal(command, identifier.ToCsiCommand());
    }

    [Theory]
    [InlineData("?S", CsiCommand.GraphicsAttributes)] // XTSMGRAPHICS, not SCROLL UP
    [InlineData("?J", CsiCommand.EraseInDisplay)]     // DECSED
    [InlineData("?K", CsiCommand.EraseInLine)]        // DECSEL
    [InlineData("?h", CsiCommand.SetMode)]            // DECSET
    [InlineData("?l", CsiCommand.ResetMode)]          // DECRST
    [InlineData("?n", CsiCommand.DeviceStatusReport)] // DEC DSR
    [InlineData(">c", CsiCommand.DeviceAttributes)]   // DA2
    [InlineData("?$p", CsiCommand.RequestMode)]       // DECRQM, private
    [InlineData("=u", CsiCommand.KittyKeyboardSet)]   // Kitty keyboard, set flags
    [InlineData("?u", CsiCommand.KittyKeyboardQuery)] // Kitty keyboard, query flags
    [InlineData(">u", CsiCommand.KittyKeyboardPush)]  // Kitty keyboard, push flags
    [InlineData("<u", CsiCommand.KittyKeyboardPop)]   // Kitty keyboard, pop flags
    [InlineData(">q", CsiCommand.SelectCursorStyle)]  // XTVERSION, split from DECSCUSR by its marker
    public void ToCsiCommand_MapsExplicitPrivateIdentifiers(string identifier, CsiCommand command)
    {
        Assert.Equal(command, identifier.ToCsiCommand());
    }

    /// <summary>
    /// Every one of these used to be dispatched as its non-private namesake. Each comment is the
    /// command the old strip-then-match lookup ran instead.
    /// </summary>
    [Theory]
    [InlineData("?s")]  // XTSAVE -> saved the cursor
    [InlineData("?r")]  // XTRESTORE -> reset the scroll region and homed the cursor
    [InlineData(">m")]  // XTMODKEYS -> applied its arguments as SGR
    [InlineData(">n")]  // XTMODKEYS disable -> answered a device status report
    [InlineData(">t")]  // XTSMTITLE -> performed a window operation
    [InlineData("?t")]
    [InlineData("?c")]  // not a sequence at all -> answered as a secondary DA
    [InlineData("?m")]
    public void ToCsiCommand_UnmappedPrivateIdentifiers_ReturnUnknown(string identifier)
    {
        Assert.Equal(CsiCommand.Unknown, identifier.ToCsiCommand());
    }

    /// <summary>
    /// The same aliasing on the intermediate-byte axis. DECSCUSR is "CSI Ps SP q" and is mapped as
    /// " q"; the bare final character is DECLL, a sequence this terminal does not implement, and
    /// mapping it to DECSCUSR as well turned "clear the LEDs" into "blink the cursor".
    /// </summary>
    [Fact]
    public void ToCsiCommand_BareQ_IsDecllAndReturnsUnknown()
    {
        Assert.Equal(CsiCommand.Unknown, "q".ToCsiCommand());
    }

    /// <summary>
    /// '&lt;' and '=' were never stripped, so they are recognised only where the map lists them --
    /// the Kitty keyboard pop and set forms, and nothing else.
    /// </summary>
    [Theory]
    [InlineData("=c")]
    [InlineData("<c")]
    [InlineData("=S")]
    [InlineData("<m")]
    public void ToCsiCommand_OtherPrivateMarkers_ReturnUnknown(string identifier)
    {
        Assert.Equal(CsiCommand.Unknown, identifier.ToCsiCommand());
    }
}
