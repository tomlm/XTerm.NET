using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using XTerm.Buffer;
using XTerm.Common;
using XTerm.Input;
using XTerm.Parser;

namespace XTerm;

/// <summary>
/// Handles input escape sequences and updates the terminal buffer.
/// Implements VT100/xterm escape sequence handlers.
/// </summary>
public partial class InputHandler
{
    /// <summary>
    /// DECRQM — reports the current state of a mode this terminal tracks, and answers nothing for
    /// the rest.
    /// </summary>
    /// <remarks>
    /// <para>This is how an application finds out whether a feature is worth using: it asks, and a
    /// terminal that says nothing is one that does not support the query. Emitting a mode without
    /// answering for it would leave well-behaved applications never using it.</para>
    /// <para>Most replies carry 1 (set) or 2 (reset), while a feature that is always active carries
    /// 3 (permanently set). DEC's other two values — 0 for "not recognised" and 4 for "permanently
    /// reset" — are never sent, so a mode this terminal keeps no
    /// state for is answered by silence rather than by a report. That costs an application asking
    /// about such a mode its read timeout, where xterm replies 0 straight away, and it is
    /// deliberate: see issue #55. Reporting "reset" for a mode that was accepted and ignored would
    /// be worse, because an application that had just set it would be told its request did not
    /// take.</para>
    /// <para>The private and ANSI forms are separate questions with separate answers — the private
    /// report carries the '?' back, the ANSI one does not — so each has its own lookup.</para>
    /// </remarks>
    private void HandleRequestMode(Params parameters, bool isPrivate)
    {
        var mode = parameters.GetParam(0, 0);

        int state;
        if (isPrivate)
        {
            if (mode == (int)TerminalMode.GraphemeClustering)
            {
                // Clustering is unconditional: DECSET and DECRST cannot change it, so DECRPM's
                // "permanently set" value is the only truthful capability report.
                state = 3;
            }
            else
            {
                if (!TryGetPrivateModeState(mode, out var set))
                    return;
                state = set ? 1 : 2;
            }
        }
        else if (TryGetAnsiModeState(mode, out var set))
        {
            state = set ? 1 : 2;
        }
        else
        {
            return;
        }

        // The marker is echoed back so the reply answers the question that was asked --
        // CSI ? 4 ; 1 $ y is DECSCLM, CSI 4 ; 1 $ y is IRM.
        var marker = isPrivate ? "?" : string.Empty;
        _terminal.RaiseDataReceived($"\u001b[{marker}{mode};{state}$y");
    }

    /// <summary>
    /// Reads back the current state of a DEC private mode, or reports that this terminal keeps no
    /// state for it.
    /// </summary>
    /// <remarks>
    /// The mouse modes are the entries worth reading twice. Tracking level and encoding are each a
    /// single selection rather than a set of independent flags — setting 1003 replaces 1002, and
    /// resetting any of them returns the selection to none — so a mouse mode is "set" exactly when
    /// it is the one currently selected. The three alternate-buffer modes all read the same flag,
    /// because they differ only in the cursor and erase work they do on the way in and out.
    /// </remarks>
    private bool TryGetPrivateModeState(int mode, out bool set)
    {
        var mouseTracker = _terminal.GetMouseTracker();
        switch (mode)
        {
            case (int)TerminalMode.AppCursorKeys:
                set = _terminal.ApplicationCursorKeys;
                return true;
            // Bracketed paste MIME leans on this answer by design: its spec's detection IS
            // DECRQM, and an application that gets silence times out and falls back to 2004.
            case (int)TerminalMode.PasteNotification:
                set = _terminal.PasteNotificationMode;
                return true;
            case (int)TerminalMode.ReverseVideo:
                set = _terminal.ReverseVideo;
                return true;
            case (int)TerminalMode.Origin:
                set = _terminal.OriginMode;
                return true;
            case (int)TerminalMode.Wraparound:
                set = _terminal.Options.Wraparound;
                return true;
            case (int)TerminalMode.ShowCursor:
                set = _terminal.CursorVisible;
                return true;
            case (int)TerminalMode.ReverseWraparound:
                set = _terminal.ReverseWraparound;
                return true;
            case (int)TerminalMode.AppKeypad:
                set = _terminal.ApplicationKeypad;
                return true;
            // The whole point of DECSLRM is a layout that behaves differently when margins are
            // available, and a well-behaved application checks before relying on them.
            case (int)TerminalMode.LeftRightMargin:
                set = _terminal.LeftRightMarginMode;
                return true;
            case (int)TerminalMode.SixelDisplayMode:
                set = _terminal.SixelDisplayMode;
                return true;
            case (int)TerminalMode.SixelPrivateColorRegisters:
                set = _terminal.SixelPrivateColorRegisters;
                return true;
            case (int)TerminalMode.SixelCursorRight:
                set = _terminal.SixelCursorRight;
                return true;
            case (int)TerminalMode.MouseReportClick:
                set = mouseTracker.TrackingMode == MouseTrackingMode.X10;
                return true;
            case (int)TerminalMode.MouseReportNormal:
                set = mouseTracker.TrackingMode == MouseTrackingMode.VT200;
                return true;
            case (int)TerminalMode.MouseReportButtonEvent:
                set = mouseTracker.TrackingMode == MouseTrackingMode.ButtonEvent;
                return true;
            case (int)TerminalMode.MouseReportAnyEvent:
                set = mouseTracker.TrackingMode == MouseTrackingMode.AnyEvent;
                return true;
            case (int)TerminalMode.MouseReportUtf8:
                set = mouseTracker.Encoding == MouseEncoding.Utf8;
                return true;
            case (int)TerminalMode.MouseReportSgr:
                set = mouseTracker.Encoding == MouseEncoding.SGR;
                return true;
            case (int)TerminalMode.MouseReportUrxvt:
                set = mouseTracker.Encoding == MouseEncoding.URXVT;
                return true;
            case (int)TerminalMode.SendFocusEvents:
                set = _terminal.SendFocusEvents;
                return true;
            case (int)TerminalMode.AltBuffer:
            case (int)TerminalMode.AltBufferCursor:
            case (int)TerminalMode.AltBufferFull:
                set = _terminal.IsAlternateBufferActive;
                return true;
            case (int)TerminalMode.EightBitInput:
                set = _terminal.EightBitInput;
                return true;
            case (int)TerminalMode.MetaSendsEscape:
                set = _terminal.MetaSendsEscape;
                return true;
            case (int)TerminalMode.AltSendsEscape:
                set = _terminal.AltSendsEscape;
                return true;
            case (int)TerminalMode.BracketedPasteMode:
                set = _terminal.BracketedPasteMode;
                return true;
            case (int)TerminalMode.SynchronizedOutput:
                set = _terminal.SynchronizedOutput;
                return true;
            case (int)TerminalMode.InBandResize:
                set = _terminal.InBandResize;
                return true;
            case (int)TerminalMode.Win32InputMode:
                set = _terminal.Win32InputMode;
                return true;
            default:
                set = false;
                return false;
        }
    }

    /// <summary>
    /// Reads back the current state of an ANSI mode, or reports that this terminal keeps no state
    /// for it.
    /// </summary>
    /// <remarks>
    /// IRM is the one ANSI mode this terminal implements: SM 4 sets <see cref="Terminal.InsertMode"/>
    /// and printing shifts the rest of the line right on the strength of it, so an application can
    /// usefully ask about it. KAM, SRM and LNM are neither stored nor acted on and get the same
    /// silence as an untracked private mode. Note the numbers overlap the private ones and mean
    /// something else — 4 here is IRM, not DECSCLM — which is why this is a separate lookup.
    /// </remarks>
    private bool TryGetAnsiModeState(int mode, out bool set)
    {
        switch (mode)
        {
            case (int)TerminalMode.InsertMode:
                set = _terminal.InsertMode;
                return true;
            default:
                set = false;
                return false;
        }
    }

    /// <summary>
    /// CSI = flags ; mode u — set the Kitty keyboard protocol flags.
    /// Mode 1 assigns, 2 sets only the given bits, 3 clears only the given bits.
    /// </summary>
    /// <remarks>
    /// All four Kitty keyboard sequences are consumed even when the option is off — silently
    /// dropped, never allowed to fall through to whatever a stripped identifier would have
    /// matched. See <see cref="KittyKeyboardQuery"/> for why that matters.
    /// </remarks>
    private void KittyKeyboardSet(Params parameters)
    {
        if (!_terminal.Options.KittyKeyboardEnabled)
            return;

        var flags = (Input.KittyKeyboardFlags)parameters.GetParam(0, 0);
        // An OMITTED mode means 1; an explicit 0 is an unknown mode and does nothing, matching
        // kitty's switch, which takes no branch for it.
        var mode = parameters.Length > 1 ? parameters.GetParam(1, 1) : 1;
        _terminal.KittyKeyboardState.Set(flags, mode);
    }

    /// <summary>
    /// CSI ? u — query the Kitty keyboard protocol flags. The terminal answers CSI ? flags u.
    /// </summary>
    /// <remarks>
    /// This is the probe applications actually send: Neovim asks on startup and enables the
    /// protocol on the answer. Before these handlers existed, the identifier's "?" was stripped
    /// and the probe executed RESTORE CURSOR — so merely asking about Kitty support teleported
    /// the cursor. When the option is off there is deliberately no answer at all: silence is how
    /// a terminal says "legacy encoding" to this probe.
    /// </remarks>
    private void KittyKeyboardQuery()
    {
        if (!_terminal.Options.KittyKeyboardEnabled)
            return;

        _terminal.RaiseDataReceived($"\u001b[?{(int)_terminal.KittyKeyboardState.Flags}u");
    }

    /// <summary>
    /// CSI > flags u — push the current flags onto this screen's stack and set new ones.
    /// </summary>
    private void KittyKeyboardPush(Params parameters)
    {
        if (!_terminal.Options.KittyKeyboardEnabled)
            return;

        _terminal.KittyKeyboardState.Push((Input.KittyKeyboardFlags)parameters.GetParam(0, 0));
    }

    /// <summary>
    /// CSI < count u — pop flags from this screen's stack.
    /// </summary>
    private void KittyKeyboardPop(Params parameters)
    {
        if (!_terminal.Options.KittyKeyboardEnabled)
            return;

        _terminal.KittyKeyboardState.Pop(Math.Max(1, parameters.GetParam(0, 1)));
    }

    private void SetCSIModeParameters(Params parameters, bool isPrivate)
    {
        for (int i = 0; i < parameters.Length; i++)
        {
            var mode = parameters.GetParam(i, 0);
            SetCSIMode(mode, isPrivate: isPrivate);
        }
    }

    private void ResetCSIModeParameters(Params parameters, bool isPrivate)
    {
        for (int i = 0; i < parameters.Length; i++)
        {
            var mode = parameters.GetParam(i, 0);
            ResetCSIMode(mode, isPrivate: isPrivate);
        }
    }

    private void ResetCSIMode(int mode, bool isPrivate)
    {
        if (isPrivate)
        {
            // DEC Private Modes (DECRST)
            if (!Enum.IsDefined(typeof(TerminalMode), mode))
            {
                System.Diagnostics.Debug.WriteLine($"Unknown private reset terminal mode: {mode}");
                return;
            }

            var terminalMode = (TerminalMode)mode;

            switch (terminalMode)
            {
                case TerminalMode.AppCursorKeys:
                    _terminal.ApplicationCursorKeys = false;
                    break;

                case TerminalMode.InsertMode:
                    // Mode 4: In DEC private mode context, this is SmoothScroll (DECSCLM)
                    // Smooth scroll is acknowledged but has no effect in modern terminals
                    break;

                case TerminalMode.ReverseVideo:
                    _terminal.ReverseVideo = false;
                    break;

                case TerminalMode.Origin:
                    _terminal.OriginMode = false;
                    MoveCursorToHome();
                    break;

                case TerminalMode.LeftRightMargin:
                    // Turning the mode off widens the margins back out, per DEC. Leaving them
                    // narrowed would keep the region in force with no sequence able to reach it --
                    // CSI s means Save Cursor again the moment the mode is off.
                    _terminal.LeftRightMarginMode = false;
                    _buffer.ResetLeftRightMargins();
                    break;

                case TerminalMode.Wraparound:
                    // Mode 7: Wraparound mode
                    _terminal.Options.Wraparound = false;
                    break;

                case TerminalMode.AutoRepeat:
                    // Auto repeat is typically always enabled in modern terminals
                    // This mode is acknowledged but has no effect
                    break;

                case TerminalMode.ShowCursor:
                    _terminal.CursorVisible = false;
                    break;

                case TerminalMode.NationalCharset:
                    // National replacement character set mode
                    // Acknowledged but typically no specific action needed for modern use
                    break;

                case TerminalMode.ReverseWraparound:
                    _terminal.ReverseWraparound = false;
                    break;

                case TerminalMode.AppKeypad:
                    _terminal.ApplicationKeypad = false;
                    break;

                case TerminalMode.SynchronizedOutput:
                    _terminal.RaiseSynchronizedOutputChanged(false);
                    break;

                case TerminalMode.InBandResize:
                    _terminal.InBandResize = false;
                    break;

                case TerminalMode.BracketedPasteMode:
                    _terminal.BracketedPasteMode = false;
                    break;

                case TerminalMode.PasteNotification:
                    // Resetting the mode also forgets any token still outstanding: a paste
                    // notified under the mode must not be redeemable after it is turned off.
                    _terminal.PasteNotificationMode = false;
                    _terminal.InvalidatePendingPaste();
                    break;

                case TerminalMode.AltBuffer:
                    _terminal.SwitchToNormalBuffer();
                    break;

                case TerminalMode.AltBufferCursor:
                    _terminal.SwitchToNormalBuffer();
                    RestoreCursor();
                    break;

                case TerminalMode.AltBufferFull:
                    _terminal.SwitchToNormalBuffer();
                    RestoreCursor();
                    break;

                case TerminalMode.SendFocusEvents:
                    _terminal.SendFocusEvents = false;
                    _terminal.GetMouseTracker().FocusEvents = false;
                    break;

                case TerminalMode.MouseReportClick:
                case TerminalMode.MouseReportNormal:
                case TerminalMode.MouseReportButtonEvent:
                case TerminalMode.MouseReportAnyEvent:
                    _terminal.GetMouseTracker().TrackingMode = MouseTrackingMode.None;
                    break;

                case TerminalMode.MouseReportUtf8:
                case TerminalMode.MouseReportSgr:
                case TerminalMode.MouseReportUrxvt:
                    _terminal.GetMouseTracker().Encoding = MouseEncoding.Default;
                    break;

                case TerminalMode.EightBitInput:
                    _terminal.EightBitInput = false;
                    break;

                case TerminalMode.NumLock:
                    // NumLock modifier handling - acknowledge but no specific action needed
                    break;

                case TerminalMode.MetaSendsEscape:
                    System.Diagnostics.Debug.WriteLine($">>> Mode {mode} MetaSendsEscape DISABLED");
                    _terminal.MetaSendsEscape = false;
                    break;

                case TerminalMode.AltSendsEscape:
                    System.Diagnostics.Debug.WriteLine($">>> Mode {mode} AltSendsEscape DISABLED");
                    _terminal.AltSendsEscape = false;
                    break;

                case TerminalMode.SixelDisplayMode:
                    _terminal.SixelDisplayMode = false;
                    break;

                case TerminalMode.SixelPrivateColorRegisters:
                    _terminal.SixelPrivateColorRegisters = false;
                    break;

                case TerminalMode.SixelCursorRight:
                    _terminal.SixelCursorRight = false;
                    break;

                case TerminalMode.Win32InputMode:
                    System.Diagnostics.Debug.WriteLine($">>> Mode {mode} Win32InputMode DISABLED");
                    _terminal.Win32InputMode = false;
                    break;

                default:
                    System.Diagnostics.Debug.WriteLine($"Unhandled terminal mode: {terminalMode}");
                    break;
            }
        }
        else
        {
            // ANSI Modes (RM)
            if (!Enum.IsDefined(typeof(TerminalMode), mode))
            {
                System.Diagnostics.Debug.WriteLine($"Unknown CSI reset terminal mode: {mode}");
                return;
            }

            var terminalMode = (TerminalMode)mode;

            switch (terminalMode)
            {
                case TerminalMode.InsertMode:
                    _terminal.InsertMode = false;
                    break;

                case TerminalMode.AutoWrapMode:
                    _terminal.Options.Wraparound = false;
                    break;

                default:
                    System.Diagnostics.Debug.WriteLine($"Unhandled CSI reset terminal mode: {terminalMode}");
                    break;
            }
        }
    }

}
