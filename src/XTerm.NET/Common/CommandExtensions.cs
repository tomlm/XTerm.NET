namespace XTerm.Common;

/// <summary>
/// Extension methods for parsing CSI command identifiers.
/// </summary>
public static class CsiCommandExtensions
{
    /// <summary>
    /// CSI identifier to command. The key is the WHOLE identifier the parser built -- every byte it
    /// collected before the final character, private marker and intermediate alike -- so "?h" and
    /// "h" are separate keys, and so are "$p" and "?$p".
    /// </summary>
    /// <remarks>
    /// The lookup used to strip a leading '?' or '>' and match on what was left, which quietly made
    /// every private sequence an alias for its non-private namesake whether or not that was the same
    /// command. "CSI ? Pi ; Pa ; Pv S" (XTSMGRAPHICS) scrolled the screen, "CSI > 4 ; 2 m"
    /// (XTMODKEYS) turned on underline, "CSI > 1 u" (the Kitty keyboard protocol) restored the
    /// cursor, and "CSI ? 1049 r" (XTRESTORE) reset the scroll region. Matching the identifier
    /// exactly means a private sequence is dispatched only where one is listed here; anything else
    /// falls out as <see cref="CsiCommand.Unknown"/> and is ignored, which is what an unimplemented
    /// sequence should do.
    /// </remarks>
    private static readonly Dictionary<string, CsiCommand> _commandMap = new(StringComparer.Ordinal)
    {
        { "@", CsiCommand.InsertChars },
        { "A", CsiCommand.CursorUp },
        { "B", CsiCommand.CursorDown },
        { "C", CsiCommand.CursorForward },
        { "D", CsiCommand.CursorBackward },
        { "E", CsiCommand.CursorNextLine },
        { "F", CsiCommand.CursorPreviousLine },
        { "G", CsiCommand.CursorCharAbsolute },
        // HPA and HPR are the ECMA-48 spellings of the same two motions CUP/CUF provide, and
        // programs built against a terminfo that lists them (hpa is in xterm's) emitted them into
        // silence. VPR likewise mirrors CUD.
        { "`", CsiCommand.CursorCharAbsolute },   // HPA
        { "a", CsiCommand.CursorForward },        // HPR
        { "e", CsiCommand.CursorDown },           // VPR
        { "H", CsiCommand.CursorPosition },
        { "I", CsiCommand.CursorForwardTab },
        { "J", CsiCommand.EraseInDisplay },
        { "?J", CsiCommand.EraseInDisplay }, // DECSED - selective erase, same erase for us
        { "K", CsiCommand.EraseInLine },
        { "?K", CsiCommand.EraseInLine }, // DECSEL - selective erase, same erase for us
        { "L", CsiCommand.InsertLines },
        { "M", CsiCommand.DeleteLines },
        { "P", CsiCommand.DeleteChars },
        { "S", CsiCommand.ScrollUp },
        { "?S", CsiCommand.GraphicsAttributes }, // XTSMGRAPHICS - a query, NOT a scroll
        { "T", CsiCommand.ScrollDown },
        { "X", CsiCommand.EraseChars },
        { "Z", CsiCommand.CursorBackwardTab },
        { "b", CsiCommand.RepeatPrecedingCharacter },
        { "c", CsiCommand.DeviceAttributes },   // DA1 - primary
        { ">c", CsiCommand.DeviceAttributes },  // DA2 - secondary
        { "d", CsiCommand.LinePositionAbsolute },
        { "f", CsiCommand.CursorPosition }, // HVP - same as CUP
        { "g", CsiCommand.TabClear },
        { "h", CsiCommand.SetMode },
        { "?h", CsiCommand.SetMode },   // DECSET
        { "l", CsiCommand.ResetMode },
        { "?l", CsiCommand.ResetMode }, // DECRST
        { "m", CsiCommand.SelectGraphicRendition },
        { "n", CsiCommand.DeviceStatusReport },
        { "?n", CsiCommand.DeviceStatusReport }, // DEC DSR
        { "r", CsiCommand.SetScrollRegion },
        { "s", CsiCommand.SaveCursorAnsi },
        { "t", CsiCommand.WindowManipulation },
        { "u", CsiCommand.RestoreCursorAnsi },
        { "=u", CsiCommand.KittyKeyboardSet },    // Kitty keyboard protocol - set flags
        { "?u", CsiCommand.KittyKeyboardQuery },  // Kitty keyboard protocol - query flags
        { ">u", CsiCommand.KittyKeyboardPush },   // Kitty keyboard protocol - push flags
        { "<u", CsiCommand.KittyKeyboardPop },    // Kitty keyboard protocol - pop flags
        { " q", CsiCommand.SelectCursorStyle },
        { "$p", CsiCommand.RequestMode },   // DECRQM - ANSI mode
        { "?$p", CsiCommand.RequestMode },  // DECRQM - DEC private mode
        // The bare final character is DECLL (Load LEDs, "CSI Ps q"), which we do not implement, so
        // it is deliberately absent: mapping it to DECSCUSR meant "CSI 0 q" to clear the LEDs gave
        // the user a blinking cursor. DECSCUSR carries the SP intermediate and is " q" above.
        { ">q", CsiCommand.SelectCursorStyle }  // XTVERSION - told apart by its marker in the handler
    };

    /// <summary>
    /// Converts a CSI identifier string to a CsiCommand enum value.
    /// </summary>
    /// <param name="identifier">The CSI identifier (final character, with any collected prefix)</param>
    /// <returns>The corresponding CsiCommand enum value, or Unknown if not recognized</returns>
    /// <remarks>
    /// The match is exact: a private identifier is recognised only if it is listed in its private
    /// form. It is never folded onto the non-private command that happens to share its final
    /// character.
    /// </remarks>
    public static CsiCommand ToCsiCommand(this string identifier)
    {
        return _commandMap.GetValueOrDefault(identifier, CsiCommand.Unknown);
    }

    /// <summary>
    /// Checks if a CSI identifier represents a DEC private mode sequence.
    /// </summary>
    /// <param name="identifier">The CSI identifier</param>
    /// <returns>True if the identifier starts with '?' or '>', indicating a DEC private mode</returns>
    public static bool IsPrivateMode(this string identifier)
    {
        return identifier.StartsWith('?') || identifier.StartsWith('>');
    }

    /// <summary>
    /// Returns the private marker a CSI identifier carries -- '&lt;', '=', '&gt;' or '?' -- or the
    /// null character when it carries none.
    /// </summary>
    /// <remarks>
    /// <c>IsPrivateMode</c> answers "is there a marker at all", which is enough wherever only one
    /// marker is ever seen on a given final character. It is not enough where two different
    /// sequences share a final character and are told apart by which marker they carry:
    /// "CSI &gt; Ps q" is XTVERSION while "CSI Ps SP q" is DECSCUSR, so a handler that asks only
    /// whether a marker is present answers one as the other.
    /// </remarks>
    /// <param name="identifier">The CSI identifier</param>
    /// <returns>The leading private marker, or '\0' if the identifier has none</returns>
    public static char PrivateMarker(this string identifier)
    {
        return identifier.Length > 0 && identifier[0] is '<' or '=' or '>' or '?'
            ? identifier[0]
            : '\0';
    }
}

/// <summary>
/// Extension methods for working with OSC commands.
/// </summary>
public static class OscCommandExtensions
{
    /// <summary>
    /// Tries to parse an OSC command string to an OscCommand enum value.
    /// </summary>
    /// <param name="commandString">The command string (numeric identifier)</param>
    /// <param name="command">The parsed OscCommand enum value</param>
    /// <returns>True if parsing succeeded, false otherwise</returns>
    public static bool TryParseOscCommand(this string commandString, out OscCommand command)
    {
        if (int.TryParse(commandString, out int commandValue) && 
            Enum.IsDefined(typeof(OscCommand), commandValue))
        {
            command = (OscCommand)commandValue;
            return true;
        }
        
        command = default;
        return false;
    }
}
