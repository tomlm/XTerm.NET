using XTerm.Common;

namespace XTerm;

/// <summary>
/// The modes this terminal RECOGNISES without acting on: stored toggles a program can set, reset
/// and read back, and the museum pieces DECRPM reports as permanently reset. A terminal that
/// stays silent about these fails the programs that ask before deciding how to behave -- DECRQM
/// exists so they do not have to guess, and "recognised, currently reset" and "never heard of it"
/// are different answers.
/// </summary>
public partial class InputHandler
{
    /// <summary>
    /// Stored ANSI toggles: KAM (2), SRM (12), LNM (20). KAM and LNM also ACT -- see
    /// <see cref="Terminal.KeyboardLocked"/> and <see cref="Terminal.LineFeedMode"/>. IRM lives on
    /// the terminal itself and is not here.
    /// </summary>
    private readonly Dictionary<int, bool> _storedAnsiModes = new()
    {
        [2] = false,   // KAM  - keyboard action
        [12] = false,  // SRM  - send/receive (local echo)
        [20] = false,  // LNM  - line feed / new line
    };

    /// <summary>
    /// Stored DEC private toggles: recognised, remembered, reported -- not acted on. DECARM
    /// defaults SET because keyboards autorepeat.
    /// </summary>
    private readonly Dictionary<int, bool> _storedDecModes = new()
    {
        [4] = false,   // DECSCLM - smooth scroll
        [18] = false,  // DECPFF  - print form feed
        [19] = false,  // DECPEX  - print extent
        [35] = false,  // DECHEBM - Hebrew keyboard mapping
        [42] = false,  // DECNRCM - national replacement charsets
        [40] = false,  // Allow80To132 - the gate DECCOLM swings on
        [41] = false,  // MoreFix - tab at the phantom column wraps first
        [67] = false,  // DECBKM  - backarrow sends BS
    };

    /// <summary>The ANSI modes of dead hardware, reported permanently reset (DECRPM state 4).</summary>
    private static readonly HashSet<int> PermanentlyResetAnsiModes =
        new() { 1, 5, 7, 10, 11, 13, 14, 15, 16, 17, 18, 19 };
        // GATM, SRTM, VEM, HEM, PUM, FEAM, FETM, MATM, TTM, SATM, TSM, EBM

    /// <summary>And their DEC private counterparts.</summary>
    private static readonly HashSet<int> PermanentlyResetDecModes =
        new() { 8, 60, 81 };
        // DECARM and DECKPM answer 4 because xterm answers 4, and xterm is the grading key;
        // DECHCCM because the hardware it coupled is gone.

    /// <summary>
    /// DECSACE's extent, which DECCARA and DECRARA read to decide between a rectangle and a
    /// stream, and which DECRQSS reports.
    /// </summary>
    private int _attributeChangeExtent;   // DECSACE (* x)
    // DECSASD ($ }) and DECSSDT ($ ~) were cached here for DECRQSS to report. They are the
    // terminal's state now, and DECRQSS reads it there -- a second copy is what let RIS undo the
    // status line while the report went on describing the one that had been undone.

    /// <summary>
    /// Puts DECSACE back to its default, for RIS.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="ResetStoredModes"/>, which the SOFT reset calls too. DECSACE
    /// survives DECSTR on a real terminal and in xterm -- it is cleared in the full-reset branch
    /// of <c>ReallyReset</c> and nowhere else -- and now that DECCARA and DECRARA read it, the
    /// difference between the two resets is the difference between a program's rectangle landing
    /// as a rectangle and landing as a stream. The status-display pair reset with the status line
    /// itself, in the terminal that now owns them.
    /// </remarks>
    internal void ResetAttributeChangeExtent() => _attributeChangeExtent = 0;

    /// <summary>Sets or resets a stored mode; false when the mode is not one of the stored set.</summary>
    private bool TrySetStoredMode(int mode, bool isPrivate, bool value)
    {
        var table = isPrivate ? _storedDecModes : _storedAnsiModes;
        if (!table.ContainsKey(mode))
            return false;

        if (!isPrivate && mode == 2)
        {
            // KAM locks the keyboard, which a program can do to a user but not undo for them if it
            // dies -- so it is refused unless the host opts in, and stays reported reset, as Ghostty
            // does. RM 2 is always honoured: unlocking is never the risk.
            if (value && !_terminal.Options.KeyboardActionModeAllowed)
                return true;
            _terminal.KeyboardLocked = value;
        }

        table[mode] = value;
        if (!isPrivate && mode == 20)
            _terminal.LineFeedMode = value;
        if (isPrivate && mode == 41)
            _terminal.MoreFixMode = value;
        return true;
    }

    /// <summary>Puts every stored toggle back to its default, for RIS.</summary>
    internal void ResetStoredModes()
    {
        foreach (var key in _storedAnsiModes.Keys.ToList())
            _storedAnsiModes[key] = false;
        foreach (var key in _storedDecModes.Keys.ToList())
            _storedDecModes[key] = false;
        _terminal.LineFeedMode = false;
        _terminal.MoreFixMode = false;
        _terminal.KeyboardLocked = false;
    }
}
