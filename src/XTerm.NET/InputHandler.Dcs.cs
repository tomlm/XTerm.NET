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
    /// Handles the start of a DCS sequence.
    /// </summary>
    /// <remarks>
    /// The payload that follows is streamed rather than handed over whole, so this is where we
    /// decide whether it is worth reading at all. Three sequences are: DECSIXEL, whose payload is
    /// an image; DECRQSS, whose payload names a setting to read back; and XTGETTCAP, whose payload
    /// is a list of capability names to answer. The identifier keeps them apart the way it does for
    /// CSI — the bare "q" is Sixel, "$q" is DECRQSS, "+q" is XTGETTCAP — so a terminal that decodes
    /// images does not have to choose between them. Everything else is left to the parser's
    /// whole-payload event, which is capped and cheap.
    /// </remarks>
    public void HandleDcsHook(string identifier, Params parameters)
    {
        CancelRepeat();
        _sixelDecoder = null;
        _capabilityRequest = null;
        _capabilityRequestTooLong = false;
        _decrqssPayload = null;

        if (identifier == "+q")
        {
            // XTGETTCAP. The payload is a list of hex-encoded capability names to answer.
            _capabilityRequest = new StringBuilder();
            return;
        }

        if (identifier == "$q")
        {
            // DECRQSS — Request Status String. The payload names the setting to read back.
            _decrqssPayload = new StringBuilder();
            return;
        }

        if (identifier != "q" || !_terminal.Options.SixelEnabled)
            return;

        // P1 aspect ratio, P2 background select, P3 horizontal grid.
        var p1 = parameters.GetParam(0, 0);
        var p2 = parameters.GetParam(1, 0);
        var p3 = parameters.GetParam(2, 0);

        // Mode 1070 set -- the default -- gives every image its own registers, so one picture
        // cannot recolour the next. Reset shares one set across images.
        var palette = _terminal.SixelPrivateColorRegisters
            ? new Graphics.SixelPalette()
            : _sharedSixelPalette ??= new Graphics.SixelPalette();

        _sixelDecoder = new Graphics.SixelDecoder(
            p1, p2, p3,
            Math.Max(1, _terminal.Options.CellWidthPixels),
            Math.Max(1, _terminal.Options.CellHeightPixels),
            _terminal.Options.MaxSixelPixels,
            (uint)(0xFF000000 | (uint)(_terminal.Colors.Background & 0xFFFFFF)),
            palette);
    }

    /// <summary>
    /// Handles a chunk of a DCS payload.
    /// </summary>
    public void HandleDcsPut(ReadOnlySpan<char> data)
    {
        _sixelDecoder?.Put(data);

        // DECRQSS first: the capability branch below returns early, and only one of the two is
        // ever live at a time anyway -- HandleDcsHook arms exactly one per sequence.
        if (_decrqssPayload is { } decrqss && decrqss.Length < MaxDecrqssPayloadLength)
            decrqss.Append(data[..Math.Min(data.Length, MaxDecrqssPayloadLength - decrqss.Length)]);

        if (_capabilityRequest is null || _capabilityRequestTooLong)
            return;

        // Past the cap the request is dropped rather than truncated: half a name decodes to some
        // other capability, and answering that confidently would be worse than not answering. The
        // client still gets its failure reply, so nothing is left waiting on an answer.
        if (_capabilityRequest.Length + data.Length > MaxCapabilityRequestLength)
        {
            _capabilityRequestTooLong = true;
            _capabilityRequest.Clear();
            return;
        }

        _capabilityRequest.Append(data);
    }

    /// <summary>
    /// Handles the end of a DCS sequence.
    /// </summary>
    /// <param name="terminatedCleanly">
    /// False when the sequence was abandoned rather than terminated. A half-arrived image is
    /// dropped: showing the top third of a picture is not a kindness.
    /// </param>
    public void HandleDcsUnhook(bool terminatedCleanly)
    {
        var capabilityRequest = _capabilityRequest;
        var tooLong = _capabilityRequestTooLong;
        _capabilityRequest = null;
        _capabilityRequestTooLong = false;

        if (capabilityRequest is not null && terminatedCleanly)
            AnswerCapabilityRequest(tooLong ? string.Empty : capabilityRequest.ToString());

        var decoder = _sixelDecoder;
        _sixelDecoder = null;

        var decrqssPayload = _decrqssPayload;
        _decrqssPayload = null;

        if (decoder is not null && terminatedCleanly)
        {
            var image = decoder.Finish();
            if (image is not null)
                PlaceImage(Graphics.ImagePlacement.Natural(image), Graphics.PlacementKind.Sixel);
        }

        if (decrqssPayload is not null && terminatedCleanly)
            HandleDecrqss(decrqssPayload.ToString());
    }

    /// <summary>
    /// Handles a completed DECRQSS request by reading back the named setting.
    /// </summary>
    /// <remarks>
    /// Reply format: <c>DCS 1 $ r &lt;setting&gt; ST</c> when the setting is recognised, or
    /// <c>DCS 0 $ r ST</c> when it is not. ST is ESC \.
    /// </remarks>
    private void HandleDecrqss(string setting)
    {
        // DCS 0 $ r ST — unrecognised setting
        const string Deny = "\x1bP0$r\x1b\\";

        var reply = setting switch
        {
            "m" => $"\x1bP1$r{SerializeSgr()}m\x1b\\",
            "r" => $"\x1bP1$r{_buffer.ScrollTop + 1};{_buffer.ScrollBottom + 1}r\x1b\\",
            " q" => $"\x1bP1$r{SerializeDecscusr()} q\x1b\\",
            "\"p" => "\x1bP1$r62;1\"p\x1b\\",
            "\"q" => "\x1bP1$r0\"q\x1b\\",
            _ => Deny,
        };

        _terminal.RaiseDataReceived(reply);
    }

    /// <summary>
    /// Answers an XTGETTCAP request (DCS + q), one reply per capability asked about.
    /// </summary>
    /// <remarks>
    /// The point of the sequence is that a program's terminfo entry describes whatever terminal the
    /// machine it is running on has heard of, which over ssh or in a container is not this one. So
    /// the answers come from what this emulator actually implements — see
    /// <see cref="TermCapabilities"/> — and not from the entry named by <c>TermName</c>.
    /// </remarks>
    private void AnswerCapabilityRequest(string request)
    {
        foreach (var reply in TermCapabilities.Answer(request, _terminal))
            _terminal.RaiseDataReceived(reply);
    }

}
