using System;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using XTerm.Common;
using XTerm.Events.Parser;

namespace XTerm.Parser;

/// <summary>
/// VT100/ANSI escape sequence parser implementing a state machine.
/// Based on Paul Williams' ANSI parser state machine.
/// </summary>
public class EscapeSequenceParser
{
    private ParserState _state;
    private readonly Params _params;
    private readonly StringBuilder _collect;
    private readonly StringBuilder _osc;
    private readonly StringBuilder _dcs;

    /// <summary>
    /// Payload characters held back so <see cref="DcsPut"/> fires once per chunk rather than once
    /// per character. A Sixel image is a few hundred thousand characters and an event apiece would
    /// cost more than the decoding does.
    /// </summary>
    private readonly char[] _dcsChunk = new char[512];
    private int _dcsChunkLength;

    /// <summary>True between a <see cref="DcsHook"/> and its matching <see cref="DcsUnhook"/>.</summary>
    private bool _dcsHooked;

    /// <summary>
    /// True when an ESC arrived mid-payload and we do not yet know whether it begins a string
    /// terminator or abandons the sequence. Resolved by the very next character.
    /// </summary>
    private bool _dcsPendingUnhook;

    /// <summary>Whether the payload is still being accumulated for the <see cref="Dcs"/> event.</summary>
    private bool _dcsAccumulating;

    /// <summary>
    /// The APC equivalent of <see cref="_dcsChunk"/>. A Kitty image arrives base64-encoded and can
    /// run to megabytes, so it is streamed a chunk at a time and never accumulated whole.
    /// </summary>
    private readonly char[] _apcChunk = new char[512];
    private int _apcChunkLength;

    /// <summary>True between an <see cref="ApcHook"/> and its matching <see cref="ApcUnhook"/>.</summary>
    private bool _apcHooked;

    /// <summary>
    /// The APC equivalent of <see cref="_dcsPendingUnhook"/>, and deliberately a separate field.
    /// Sharing one would let a DCS and an APC cross-fire each other's unhook.
    /// </summary>
    private bool _apcPendingUnhook;

    // Parser events - Standard C# event pattern
    /// <summary>
    /// Fired when printable characters are parsed.
    /// </summary>
    public event EventHandler<PrintEventArgs>? Print;

    /// <summary>
    /// Internal print hook, bypassing <see cref="Print"/> and its per-character EventArgs allocation.
    /// The public event still fires for anyone subscribed; this exists so the terminal's own hot path
    /// does not pay for an observer pattern it does not need.
    /// </summary>
    internal Action<string>? PrintFast;

    /// <summary>Internal run hook: (data, start, count) for a stretch of printable ASCII in Ground.</summary>
    internal Action<string, int, int>? PrintRunFast;

    /// <summary>
    /// Internal run hook for the byte entry. A custom delegate because Action&lt;&gt; cannot carry a
    /// <see cref="ReadOnlySpan{T}"/>.
    /// </summary>
    internal delegate void ByteRunHandler(ReadOnlySpan<byte> run);

    internal ByteRunHandler? PrintByteRunFast;

    /// <summary>Internal Execute hook. See <see cref="PrintFast"/>: control characters are as hot as printable ones.</summary>
    internal Action<int>? ExecuteFast;

    /// <summary>Internal CSI hook, bypassing the per-sequence CsiEventArgs.</summary>
    internal Action<string, Params>? CsiFast;

    /// <summary>Internal ESC hook, bypassing the per-sequence EscEventArgs.</summary>
    internal Action<string, string>? EscFast;

    /// <summary>Internal OSC hook, bypassing the per-sequence OscEventArgs.</summary>
    internal Action<string>? OscFast;

    /// <summary>
    /// Fired when control characters are executed.
    /// </summary>
    public event EventHandler<ExecuteEventArgs>? Execute;

    /// <summary>
    /// Fired when CSI sequences are parsed.
    /// </summary>
    public event EventHandler<CsiEventArgs>? Csi;

    /// <summary>
    /// Fired when ESC sequences are parsed.
    /// </summary>
    public event EventHandler<EscEventArgs>? Esc;

    /// <summary>
    /// Fired when OSC sequences are parsed.
    /// </summary>
    public event EventHandler<OscEventArgs>? Osc;

    /// <summary>
    /// Fired when a DCS sequence completes, carrying its whole payload.
    /// </summary>
    /// <remarks>
    /// Convenient for the short sequences -- DECRQSS and friends -- and useless for the long ones,
    /// because a Sixel image would have to be buffered into a single string first. So the payload
    /// is only accumulated while something is subscribed here AND the sequence stayed under
    /// <see cref="MaxAccumulatedDcsLength"/>. Anything larger is streamed and nothing else; use
    /// <see cref="DcsHook"/>/<see cref="DcsPut"/>/<see cref="DcsUnhook"/> for those.
    /// </remarks>
    public event EventHandler<DcsEventArgs>? Dcs;

    /// <summary>
    /// Fired when a DCS sequence's final character has been seen, before any payload.
    /// </summary>
    public event EventHandler<DcsHookEventArgs>? DcsHook;

    /// <summary>
    /// Fired for each chunk of a DCS payload.
    /// </summary>
    public event EventHandler<DcsPutEventArgs>? DcsPut;

    /// <summary>
    /// Fired when a DCS sequence ends, cleanly or otherwise.
    /// </summary>
    public event EventHandler<DcsUnhookEventArgs>? DcsUnhook;

    /// <summary>
    /// Fired when an APC sequence begins, before any payload.
    /// </summary>
    /// <remarks>
    /// APC has no parameter grammar in front of its payload the way CSI and DCS do -- everything
    /// after the introducer is payload, and what it means is decided by its first character. So
    /// this carries only the introducer, and a listener decides from the first chunk whether the
    /// sequence is one it wants.
    /// </remarks>
    public event EventHandler<ApcHookEventArgs>? ApcHook;

    /// <summary>
    /// Fired for each chunk of an APC payload.
    /// </summary>
    public event EventHandler<ApcPutEventArgs>? ApcPut;

    /// <summary>
    /// Fired when an APC sequence ends, cleanly or otherwise.
    /// </summary>
    public event EventHandler<ApcUnhookEventArgs>? ApcUnhook;

    /// <summary>
    /// How much of a DCS payload will be accumulated for the <see cref="Dcs"/> event. A Sixel
    /// image is unbounded and a screenful can run to megabytes; buffering that so a convenience
    /// event can hand it over as one string is how a terminal ends up holding a copy of every
    /// picture ever drawn.
    /// </summary>
    public const int MaxAccumulatedDcsLength = 4096;

    public EscapeSequenceParser()
    {
        _state = ParserState.Ground;
        _params = new Params();
        _collect = new StringBuilder();
        _osc = new StringBuilder();
        _dcs = new StringBuilder();
    }

    /// <summary>
    /// Parses input data byte by byte.
    /// </summary>
    public void Parse(string data)
    {
        var i = 0;
        var length = data.Length;

        // A previous call ended on a high surrogate with nothing after it. That is not malformed
        // input: a PTY read boundary falls wherever the read happens to end, so a surrogate pair
        // straddling two Write calls is ordinary. Resolve it against the start of this chunk.
        // Bytes held from a byte-entry call cannot be completed by UTF-16 input, so they are
        // abandoned here. Abandoning them SILENTLY would delete input; U+FFFD says something was
        // there. Mixing the two entries mid-sequence is a caller error, but a caller error should
        // not make characters vanish.
        if (_pendingByteCount > 0)
        {
            _pendingByteCount = 0;
            ParseChar(0xFFFD);
        }

        if (_pendingHighSurrogate != '\0')
        {
            var pending = _pendingHighSurrogate;
            _pendingHighSurrogate = '\0';

            if (length > 0 && char.IsLowSurrogate(data[0]))
            {
                ParseChar(char.ConvertToUtf32(pending, data[0]));
                i = 1;
            }
            else
            {
                ParseChar(0xFFFD);   // nothing followed it, so it really was unpaired
            }
        }

        while (i < length)
        {
            // Ground-state fast path for printable ASCII.
            //
            // A sampled profile put 70% of emulator time in ParseChar and 27% in this loop's rune
            // enumeration, with the actual cell work no longer even visible. That cost is per-character
            // dispatch, not parsing: for a run of ordinary text every character re-tests the C0/C1
            // ranges, re-switches on a state that cannot have changed, and is decoded through a Rune
            // enumerator that checks for surrogates it will never find.
            //
            // U+0020..U+007E in Ground is unambiguous -- ParseChar would reach `if (code >= 0x20)
            // OnPrint(code)` and nothing else -- so the run can be emitted directly. Printing cannot
            // change parser state, so _state stays Ground for the whole run. Everything outside that
            // range, DEL and the C1 block included, falls through to the original path unchanged.
            if (_state == ParserState.Ground)
            {
                var start = i;
                while (i < length)
                {
                    var c = data[i];
                    if (c < 0x20 || c >= 0x7F)
                        break;
                    i++;
                }

                if (i > start)
                {
                    OnPrintRun(data, start, i - start);
                    continue;
                }
            }

            // Slow path: one codepoint. Mirrors string.EnumerateRunes(), which substitutes U+FFFD for
            // an unpaired surrogate rather than throwing -- terminal input is not guaranteed well-formed.
            var ch = data[i];
            if (char.IsHighSurrogate(ch))
            {
                if (i + 1 < length)
                {
                    if (char.IsLowSurrogate(data[i + 1]))
                    {
                        ParseChar(char.ConvertToUtf32(ch, data[i + 1]));
                        i += 2;
                        continue;
                    }

                    ParseChar(0xFFFD);
                    i++;
                    continue;
                }

                // Last char of the chunk: hold it rather than declaring it unpaired, and decide once
                // the next chunk arrives.
                _pendingHighSurrogate = ch;
                return;
            }

            if (char.IsLowSurrogate(ch))
            {
                ParseChar(0xFFFD);
                i++;
                continue;
            }

            ParseChar(ch);
            i++;
        }
    }

    /// <summary>
    /// Parses UTF-8 bytes directly.
    ///
    /// The string entry forces every caller to transcode a PTY read to UTF-16 first, allocating a
    /// string per read and doing decode work most of the bytes do not need: terminal output is
    /// overwhelmingly printable ASCII, where the byte, the codepoint and the cell are the same
    /// number. This entry keeps the bytes as bytes.
    ///
    /// The Ground-state scan uses IndexOfAnyExceptInRange, which the runtime vectorises — that is
    /// where the SIMD comes from, with no hand-written intrinsics to maintain.
    /// </summary>
    public void Parse(ReadOnlySpan<byte> data)
    {
        // The mirror of the case at the top of Parse(string): a high surrogate held from a string
        // call cannot be completed by UTF-8 input.
        if (_pendingHighSurrogate != '\0')
        {
            _pendingHighSurrogate = '\0';
            ParseChar(0xFFFD);
        }

        // Resolve a sequence the previous chunk left incomplete. PTY reads split on byte boundaries,
        // so a multi-byte codepoint straddling two calls is ordinary input, not corruption.
        if (_pendingByteCount > 0)
        {
            Span<byte> joined = stackalloc byte[8];
            var held = _pendingByteCount;
            _pendingBytes.AsSpan(0, held).CopyTo(joined);

            var take = Math.Min(data.Length, joined.Length - held);
            data[..take].CopyTo(joined[held..]);
            _pendingByteCount = 0;

            var available = joined[..(held + take)];
            var status = Rune.DecodeFromUtf8(available, out var rune, out var consumed);

            if (status == OperationStatus.NeedMoreData && data.Length <= take)
            {
                StashPending(available);   // still short, and no more input to draw on
                return;
            }

            // consumed >= held always, so the held bytes can never be dropped here. Bytes are only
            // ever stashed on NeedMoreData, which makes them a VALID PREFIX, and DecodeFromUtf8
            // consumes the maximal invalid subsequence -- which contains that prefix. Checked
            // exhaustively rather than argued: all 17,651 valid prefixes of one to three bytes,
            // against every continuation byte, produced no case consuming fewer than were held.
            ParseChar(rune.Value);
            data = data[Math.Max(0, consumed - held)..];
        }

        while (!data.IsEmpty)
        {
            if (_state == ParserState.Ground)
            {
                // Longest stretch of printable ASCII. Anything outside U+0020..U+007E ends it: C0 and
                // DEL are control, and >= 0x80 begins a multi-byte sequence.
                var end = data.IndexOfAnyExceptInRange((byte)0x20, (byte)0x7E);
                var runLength = end < 0 ? data.Length : end;

                if (runLength > 0)
                {
                    OnPrintByteRun(data[..runLength]);
                    data = data[runLength..];
                    continue;
                }
            }

            var b = data[0];
            if (b < 0x80)
            {
                ParseChar(b);
                data = data[1..];
                continue;
            }

            var decode = Rune.DecodeFromUtf8(data, out var decoded, out var used);
            if (decode == OperationStatus.NeedMoreData)
            {
                StashPending(data);
                return;
            }

            // On invalid data DecodeFromUtf8 yields U+FFFD and consumes at least one byte, so this
            // always advances.
            ParseChar(decoded.Value);
            data = data[used..];
        }
    }

    private void StashPending(ReadOnlySpan<byte> remainder)
    {
        var count = Math.Min(remainder.Length, _pendingBytes.Length);
        remainder[..count].CopyTo(_pendingBytes);
        _pendingByteCount = count;
    }

    /// <summary>Bytes of a UTF-8 sequence the previous chunk ended part-way through.</summary>
    private readonly byte[] _pendingBytes = new byte[4];
    private int _pendingByteCount;

    /// <summary>
    /// Emits a run of printable ASCII bytes. The public Print event, if observed, still sees one
    /// character at a time.
    /// </summary>
    protected virtual void OnPrintByteRun(ReadOnlySpan<byte> run)
    {
        PrintByteRunFast?.Invoke(run);

        var handler = Print;
        if (handler is not null)
        {
            foreach (var b in run)
                handler.Invoke(this, new PrintEventArgs(CodePointText.Get((char)b)));
        }
    }

    /// <summary>
    /// Parses a single character/code point.
    /// </summary>
    private void ParseChar(int code)
    {
        // An ESC in a DCS payload is ambiguous until the next character arrives: "ESC \" ends the
        // sequence, anything else abandons it. Resolving it here, one character late, is what lets
        // a handler tell a finished image from a truncated one.
        if (_dcsPendingUnhook)
        {
            _dcsPendingUnhook = false;
            EndDcs(terminatedCleanly: code == 0x5C); // backslash
        }

        // The same one-character-late resolution for APC. Kept as its own flag rather than shared
        // with the DCS one: a sequence of each interleaved would otherwise close the wrong payload.
        if (_apcPendingUnhook)
        {
            _apcPendingUnhook = false;
            EndApc(terminatedCleanly: code == 0x5C);
        }

        var currentState = _state;

        // C0/C1 control characters
        if (code < 0x20 || (code >= 0x80 && code < 0xA0))
        {
            switch (currentState)
            {
                case ParserState.Ground:
                case ParserState.Escape:
                case ParserState.CsiEntry:
                case ParserState.CsiParam:
                case ParserState.CsiIntermediate:
                case ParserState.CsiIgnore:
                    OnExecute(code);
                    if (code == 0x1B) // ESC
                    {
                        Transition(ParserState.Escape);
                    }
                    return;

                case ParserState.OscString:
                    if (code == 0x1B || code == 0x07) // ESC or BEL
                    {
                        DispatchOsc();
                        Transition(code == 0x1B ? ParserState.Escape : ParserState.Ground);
                    }
                    else if (code >= 0x20)
                    {
                        OscPut(code);
                    }
                    return;
            }
        }

        // Normal state machine processing
        switch (_state)
        {
            case ParserState.Ground:
                if (code >= 0x20)
                {
                    OnPrint(code);
                }
                break;

            case ParserState.Escape:
                switch (code)
                {
                    case 0x5B: // [
                        Transition(ParserState.CsiEntry);
                        break;
                    case 0x5D: // ]
                        Transition(ParserState.OscString);
                        break;
                    case 0x50: // P
                        Transition(ParserState.DcsEntry);
                        break;
                    case 0x5F: // _  APC -- the Kitty graphics transport, so its payload is read
                        BeginApc();
                        break;
                    case 0x5E: // ^  PM
                    case 0x58: // X  SOS
                        Transition(ParserState.SosPmApcString);
                        break;
                    case >= 0x20 and < 0x30:
                        Collect(code);
                        Transition(ParserState.EscapeIntermediate);
                        break;
                    case >= 0x30 and < 0x7F:
                        DispatchEsc(code);
                        Transition(ParserState.Ground);
                        break;
                    default:
                        Transition(ParserState.Ground);
                        break;
                }
                break;

            case ParserState.EscapeIntermediate:
                if (code >= 0x20 && code < 0x30)
                {
                    Collect(code);
                }
                else if (code >= 0x30 && code < 0x7F)
                {
                    DispatchEsc(code);
                    Transition(ParserState.Ground);
                }
                break;

            case ParserState.CsiEntry:
                if (code >= 0x3C && code <= 0x3F) // Private parameter markers (<, =, >, ?)
                {
                    Collect(code);
                }
                else if (code >= 0x30 && code < 0x3C) // 0-9, :, ;
                {
                    Param(code);
                    Transition(ParserState.CsiParam);
                }
                else if (code >= 0x40 && code < 0x7F)
                {
                    DispatchCsi(code);
                    Transition(ParserState.Ground);
                }
                else if (code >= 0x20 && code < 0x30)
                {
                    Collect(code);
                    Transition(ParserState.CsiIntermediate);
                }
                break;

            case ParserState.CsiParam:
                if (code >= 0x30 && code < 0x40)
                {
                    Param(code);
                }
                else if (code >= 0x40 && code < 0x7F)
                {
                    DispatchCsi(code);
                    Transition(ParserState.Ground);
                }
                else if (code >= 0x20 && code < 0x30)
                {
                    Collect(code);
                    Transition(ParserState.CsiIntermediate);
                }
                break;

            case ParserState.CsiIntermediate:
                if (code >= 0x20 && code < 0x30)
                {
                    Collect(code);
                }
                else if (code >= 0x40 && code < 0x7F)
                {
                    DispatchCsi(code);
                    Transition(ParserState.Ground);
                }
                break;

            case ParserState.CsiIgnore:
                if (code >= 0x40 && code < 0x7F)
                {
                    Transition(ParserState.Ground);
                }
                break;

            case ParserState.OscString:
                OscPut(code);
                break;

            case ParserState.ApcString:
                // Mirrors DcsPassthrough exactly; see the reasoning there. APC is where Kitty
                // graphics arrive, so unlike SOS and PM below, the payload is kept.
                if (code == 0x9C) // ST
                {
                    EndApc(terminatedCleanly: true);
                    Transition(ParserState.Ground);
                }
                else if (code == 0x1B) // ESC, possibly the first half of ESC \
                {
                    _apcPendingUnhook = true;
                    Transition(ParserState.Escape);
                }
                else if (code == 0x18 || code == 0x1A) // CAN, SUB — an explicit abort
                {
                    EndApc(terminatedCleanly: false);
                    Transition(ParserState.Ground);
                }
                else if (code == 0x7F) { /* DEL is not payload */ }
                else
                {
                    ApcPutChar(code);
                }
                break;

            case ParserState.SosPmApcString:
                // SOS and PM are consumed whole and answered by nobody. What matters is LEAVING them —
                // and this state had no case here at all. ESC _ , ESC ^ and ESC X were entered and never
                // exited, so the parser sat in that state discarding every byte that followed it. One
                // kitty graphics query and the terminal stopped answering anything, permanently.
                //
                // ESC moves to Escape rather than Ground so the backslash of a two-byte ST is consumed as
                // part of the terminator, which is what OSC already does. Dropping straight to Ground left
                // that backslash to be printed as text.
                if (code == 0x9C) // ST
                {
                    Transition(ParserState.Ground);
                }
                else if (code == 0x1B) // ESC, the first half of ESC \
                {
                    Transition(ParserState.Escape);
                }
                break;

            // ---- DCS ------------------------------------------------------------------------
            // The prologue states mirror their CSI counterparts exactly, because the grammar in
            // front of the final character is the same one. What differs is the final character:
            // CSI dispatches and returns to Ground, DCS opens a payload that runs until ST.

            case ParserState.DcsEntry:
                if (code == 0x9C) { Transition(ParserState.Ground); }
                else if (code == 0x1B) { Transition(ParserState.Escape); }
                else if (code == 0x18 || code == 0x1A) { Transition(ParserState.Ground); }
                else if (code < 0x20 || code == 0x7F) { /* ignored */ }
                else if (code >= 0x3C && code <= 0x3F) // private markers <, =, >, ?
                {
                    Collect(code);
                    Transition(ParserState.DcsParam);
                }
                else if (code >= 0x30 && code < 0x3C) // 0-9, :, ;
                {
                    if (code == 0x3A) { Transition(ParserState.DcsIgnore); }
                    else { Param(code); Transition(ParserState.DcsParam); }
                }
                else if (code >= 0x20 && code < 0x30) // intermediates
                {
                    Collect(code);
                    Transition(ParserState.DcsIntermediate);
                }
                else if (code >= 0x40 && code < 0x7F)
                {
                    BeginDcs(code);
                }
                break;

            case ParserState.DcsParam:
                if (code == 0x9C) { Transition(ParserState.Ground); }
                else if (code == 0x1B) { Transition(ParserState.Escape); }
                else if (code == 0x18 || code == 0x1A) { Transition(ParserState.Ground); }
                else if (code < 0x20 || code == 0x7F) { /* ignored */ }
                else if (code >= 0x30 && code < 0x3C) // 0-9, ;
                {
                    if (code == 0x3A) { Transition(ParserState.DcsIgnore); }
                    else { Param(code); }
                }
                else if (code >= 0x3C && code <= 0x3F)
                {
                    // A private marker is only legal before the parameters. Arriving here it is
                    // malformed, and the sequence is discarded rather than half-honoured.
                    Transition(ParserState.DcsIgnore);
                }
                else if (code >= 0x20 && code < 0x30)
                {
                    Collect(code);
                    Transition(ParserState.DcsIntermediate);
                }
                else if (code >= 0x40 && code < 0x7F)
                {
                    BeginDcs(code);
                }
                break;

            case ParserState.DcsIntermediate:
                if (code == 0x9C) { Transition(ParserState.Ground); }
                else if (code == 0x1B) { Transition(ParserState.Escape); }
                else if (code == 0x18 || code == 0x1A) { Transition(ParserState.Ground); }
                else if (code < 0x20 || code == 0x7F) { /* ignored */ }
                else if (code >= 0x20 && code < 0x30)
                {
                    Collect(code);
                }
                else if (code >= 0x30 && code < 0x40)
                {
                    // Parameters after an intermediate are out of order; discard the sequence.
                    Transition(ParserState.DcsIgnore);
                }
                else if (code >= 0x40 && code < 0x7F)
                {
                    BeginDcs(code);
                }
                break;

            case ParserState.DcsIgnore:
                if (code == 0x9C) { Transition(ParserState.Ground); }
                else if (code == 0x1B) { Transition(ParserState.Escape); }
                else if (code == 0x18 || code == 0x1A) { Transition(ParserState.Ground); }
                break;

            case ParserState.DcsPassthrough:
                if (code == 0x9C) // ST
                {
                    EndDcs(terminatedCleanly: true);
                    Transition(ParserState.Ground);
                }
                else if (code == 0x1B) // ESC, possibly the first half of ESC \
                {
                    // Do not decide yet. The next character says whether this terminated the
                    // sequence or abandoned it; ParseChar resolves it on the way in.
                    _dcsPendingUnhook = true;
                    Transition(ParserState.Escape);
                }
                else if (code == 0x18 || code == 0x1A) // CAN, SUB — an explicit abort
                {
                    EndDcs(terminatedCleanly: false);
                    Transition(ParserState.Ground);
                }
                else if (code == 0x7F) { /* DEL is not payload */ }
                else
                {
                    DcsPutChar(code);
                }
                break;
        }
    }

    private void Transition(ParserState newState)
    {
        // Exit actions
        switch (_state)
        {
            case ParserState.CsiEntry:
            case ParserState.CsiParam:
            case ParserState.CsiIntermediate:
            case ParserState.CsiIgnore:
                if (newState != ParserState.CsiParam && newState != ParserState.CsiIntermediate && newState != ParserState.CsiIgnore)
                {
                    _params.Reset();
                    _collect.Clear();
                }
                break;
        }

        _state = newState;

        // Entry actions
        switch (newState)
        {
            case ParserState.CsiEntry:
            case ParserState.DcsEntry:
                _params.Reset();
                _collect.Clear();
                // The sub-parameter accumulator is transient state like the rest, and nothing else
                // clears it when a sequence is ABANDONED rather than dispatched -- FlushSubParam
                // runs on a separator or at dispatch, none of which happen then. Left set, the digit
                // branch swallows every digit of the NEXT sequence up to its first separator, so its
                // first parameter reads as 0: ESC[31m becomes SGR 0 and resets every attribute
                // instead of setting red.
                _inSubParam = false;
                _subParamValue = 0;
                _params.AddParam(0);
                break;

            case ParserState.OscString:
                _osc.Clear();
                _oscOverflowed = false;
                break;
        }
    }

    /// <summary>
    /// Raises the Print event.
    /// </summary>
    protected virtual void OnPrint(int code)
    {
        // Two allocations used to happen here for every printed character: the string, and the
        // EventArgs wrapping it. The string now comes from a cache, and the EventArgs is built only
        // when something is actually subscribed to the public event -- the terminal itself uses
        // PrintFast and never observes it.
        var text = CodePointText.Get(code);

        PrintFast?.Invoke(text);

        var handler = Print;
        if (handler is not null)
            handler.Invoke(this, new PrintEventArgs(text));
    }

    /// <summary>
    /// Emits a run of printable ASCII.
    ///
    /// The fast hook takes the whole run; the public Print event, if anyone is listening, still sees
    /// one character at a time, because that is the contract it has always had.
    /// </summary>
    protected virtual void OnPrintRun(string data, int start, int count)
    {
        PrintRunFast?.Invoke(data, start, count);

        var handler = Print;
        if (handler is not null)
        {
            for (var k = 0; k < count; k++)
                handler.Invoke(this, new PrintEventArgs(CodePointText.Get(data[start + k])));
        }
    }

    /// <summary>
    /// Raises the Execute event.
    /// </summary>
    protected virtual void OnExecute(int code)
    {
        ExecuteFast?.Invoke(code);

        var handler = Execute;
        if (handler is not null)
            handler.Invoke(this, new ExecuteEventArgs(code));
    }

    /// <summary>
    /// A high surrogate that ended the previous chunk, awaiting its low surrogate.
    /// <c>'\0'</c> when there is none.
    /// </summary>
    private char _pendingHighSurrogate;

    private void Collect(int code)
    {
        _collect.Append((char)code);
    }

    /// <summary>
    /// True between a colon and the next separator, while digits belong to a sub-parameter rather
    /// than to the parameter itself.
    /// </summary>
    private bool _inSubParam;

    private int _subParamValue;

    /// <summary>
    /// Ends the current parameter or sub-parameter and starts a sub-parameter.
    /// </summary>
    /// <remarks>
    /// An empty slot is a real value, not an omission — <c>58:2::255:0:0</c> carries a colour space
    /// id nobody uses, and dropping it would shift the three components by one and turn red into
    /// black.
    /// </remarks>
    private void BeginSubParam()
    {
        FlushSubParam();
        _inSubParam = true;
        _subParamValue = 0;
    }

    private void FlushSubParam()
    {
        if (!_inSubParam)
            return;

        _params.AddSubParam(_subParamValue);
        _inSubParam = false;
        _subParamValue = 0;
    }

    private void Param(int code)
    {
        if (code == 0x3A) // :
        {
            // Handled HERE and not in the state machine, because 0x3A sits inside the 0x30..0x3F
            // parameter-byte range the digit branch already claims -- a colon case beside that
            // branch can never be reached, which is how this went unnoticed.
            BeginSubParam();
        }
        else if (code == 0x3B) // ;
        {
            FlushSubParam();
            _params.AddParam(0);
        }
        else if (code >= 0x30 && code <= 0x39) // 0-9
        {
            var digit = code - 0x30;

            if (_inSubParam)
            {
                _subParamValue = Saturate(_subParamValue, digit);
                return;
            }

            // Get current value of last parameter and update it
            var currentValue = _params.GetParam(_params.Length - 1, 0);
            _params.UpdateLastParam(Saturate(currentValue, digit));
        }
    }

    private void DispatchCsi(int code)
    {
        FlushSubParam();

        // Collected characters come BEFORE the final character (e.g., "?" before "h" gives "?h").
        // The overwhelmingly common case is nothing collected at all -- a bare SGR is just "m" --
        // so take the cached single-character string and skip building one.
        var identifier = _collect.Length == 0
            ? CodePointText.Get((char)code)
            : _collect.ToString() + (char)code;

        // Not cloned. The handler reads the parameters synchronously and the parser does not touch
        // them again until the next sequence, so a copy per CSI bought nothing.
        OnCsi(identifier, _params);
    }

    /// <summary>
    /// Raises the Csi event.
    /// </summary>
    protected virtual void OnCsi(string identifier, Params parameters)
    {
        // The internal handler gets the live Params. It reads them synchronously and keeps no
        // reference -- InputHandler has no Params field -- and the parser resets the instance after
        // dispatch, so there is nothing for a copy to protect against. Cloning here cost five
        // allocations per sequence (the Params, two Lists, and their backing arrays), which on
        // colour-heavy output was the single largest remaining source of garbage.
        CsiFast?.Invoke(identifier, parameters);

        // An external subscriber is a different matter: it may hold on to what it is handed, and the
        // instance above is about to be reset underneath it. That one still gets its own copy.
        var handler = Csi;
        if (handler is not null)
            handler.Invoke(this, new CsiEventArgs(identifier, parameters.Clone()));
    }

    private void DispatchEsc(int code)
    {
        OnEsc(CodePointText.Get((char)code), _collect.ToString());
    }

    /// <summary>
    /// Raises the Esc event.
    /// </summary>
    protected virtual void OnEsc(string finalChar, string collected)
    {
        EscFast?.Invoke(finalChar, collected);

        var handler = Esc;
        if (handler is not null)
            handler.Invoke(this, new EscEventArgs(finalChar, collected));
    }

    /// <summary>
    /// Appends a digit, stopping at <see cref="MaxParamValue"/> rather than wrapping. Unchecked
    /// multiply turned CSI 99999999999 into a negative or small parameter -- a sequence asking for
    /// something absurd became a sequence asking for something plausible and wrong, which is worse
    /// than either refusing it or clamping it. Handlers already clamp what they are given; this
    /// only guarantees the value they see has the sign and magnitude the stream actually asked for.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Saturate(int current, int digit)
    {
        // No division. The obvious guard -- current > (MaxParamValue - digit) / 10 -- divides once
        // per DIGIT, and a truecolor stream is CSI 38;2;R;G;B m over and over: five parameters and
        // a dozen digits per sequence, all on the parser's hottest path. Comparing against a
        // constant costs a compare instead.
        //
        // SafeCurrent is MaxParamValue / 10, so anything at or below it cannot overflow an int
        // when multiplied by ten and given a digit; anything above it is already past the ceiling.
        if (current > SafeCurrent)
            return MaxParamValue;

        var next = current * 10 + digit;
        return next > MaxParamValue ? MaxParamValue : next;
    }

    /// <summary>The largest accumulator that can still take another digit without overflowing.</summary>
    private const int SafeCurrent = MaxParamValue / 10;

    /// <summary>
    /// Ceiling for a single CSI parameter. Far above any real sequence; handlers clamp to the
    /// screen anyway.
    /// </summary>
    private const int MaxParamValue = 0x7FFFFFF;

    /// <summary>
    /// Ceiling on one OSC payload. A sequence that never terminates otherwise grows this buffer
    /// for as long as the program keeps writing -- the payload is pty-controlled and nothing else
    /// bounded it. Generous next to the longest real payloads, which are OSC 8 URLs and OSC 52
    /// clipboard writes.
    /// </summary>
    private const int MaxOscPayloadChars = 1 << 20;

    /// <summary>Whether the OSC in flight exceeded the cap and must not be dispatched at all.</summary>
    private bool _oscOverflowed;

    private void OscPut(int code)
    {
        // Past the cap the payload is dropped on the floor rather than truncated and dispatched:
        // half a URL or half a base64 clipboard write is not something a handler should act on.
        //
        // Refusing to APPEND is not enough on its own -- the terminator would still dispatch the
        // prefix that did fit, which is precisely the partial action this is here to prevent: an
        // oversized OSC 52 would arrive as a perfectly valid clipboard write of attacker-chosen
        // length. The flag makes the whole sequence unusable, and DispatchOsc drops it.
        if (_osc.Length >= MaxOscPayloadChars)
        {
            _oscOverflowed = true;
            return;
        }

        // Append the char, not a string built from it. ConvertFromUtf32 allocated once per character
        // of every OSC payload -- window titles, OSC 7 working directories, OSC 8 URLs, and every
        // OSC 133 prompt mark, which a shell emits several times per command.
        if (code < 0x10000)
        {
            _osc.Append((char)code);
            return;
        }

        var value = code - 0x10000;
        _osc.Append((char)(0xD800 + (value >> 10)));
        _osc.Append((char)(0xDC00 + (value & 0x3FF)));
    }

    /// <summary>
    /// Handles the final character of a DCS: announces the sequence and opens its payload.
    /// </summary>
    private void BeginDcs(int code)
    {
        // Read the prologue before transitioning — Transition's entry action for a later state is
        // free to clear it.
        var identifier = _collect.ToString() + (char)code;
        _collect.Clear();
        var paramsClone = _params.Clone();
        _dcsChunkLength = 0;
        _dcs.Clear();
        _dcsHooked = true;

        // Only pay for accumulation if somebody is actually listening for the whole-payload event.
        _dcsAccumulating = Dcs != null;

        Transition(ParserState.DcsPassthrough);
        OnDcsHook(identifier, paramsClone);
    }

    /// <summary>
    /// Adds one character to the payload, flushing to <see cref="DcsPut"/> a chunk at a time.
    /// </summary>
    private void DcsPutChar(int code)
    {
        if (code > 0xFFFF)
        {
            // Not something Sixel or DECRQSS produce, but the parser is rune-based and dropping
            // half a surrogate pair into the payload would be worse than spending two slots.
            var surrogates = char.ConvertFromUtf32(code);
            foreach (var c in surrogates)
                DcsPutChar(c);
            return;
        }

        if (_dcsAccumulating)
        {
            if (_dcs.Length < MaxAccumulatedDcsLength)
                _dcs.Append((char)code);
            else
                _dcsAccumulating = false; // too big to hand over as one string; stop paying for it
        }

        _dcsChunk[_dcsChunkLength++] = (char)code;
        if (_dcsChunkLength == _dcsChunk.Length)
            FlushDcsChunk();
    }

    private void FlushDcsChunk()
    {
        if (_dcsChunkLength == 0)
            return;

        var length = _dcsChunkLength;
        _dcsChunkLength = 0;
        OnDcsPut(new ReadOnlyMemory<char>(_dcsChunk, 0, length));
    }

    /// <summary>
    /// Closes an open DCS payload. Safe to call when none is open, which is what makes it usable
    /// from <see cref="Reset"/> and from every abort path without a guard at each call site.
    /// </summary>
    private void EndDcs(bool terminatedCleanly)
    {
        if (!_dcsHooked)
            return;

        _dcsHooked = false;
        FlushDcsChunk();

        if (_dcsAccumulating)
        {
            _dcsAccumulating = false;
            OnDcs(_dcs.ToString(), _params.Clone());
        }
        _dcs.Clear();

        OnDcsUnhook(terminatedCleanly);
    }

    /// <summary>
    /// Opens an APC payload. Everything after the introducer belongs to it.
    /// </summary>
    private void BeginApc()
    {
        _apcChunkLength = 0;
        _apcHooked = true;

        Transition(ParserState.ApcString);
        OnApcHook('_');
    }

    /// <summary>
    /// Adds one character to the payload, flushing to <see cref="ApcPut"/> a chunk at a time.
    /// </summary>
    private void ApcPutChar(int code)
    {
        if (code > 0xFFFF)
        {
            // Kitty payloads are base64 and never leave ASCII, but the parser is rune-based and
            // half a surrogate pair in the stream would be worse than spending two slots.
            foreach (var c in char.ConvertFromUtf32(code))
                ApcPutChar(c);
            return;
        }

        _apcChunk[_apcChunkLength++] = (char)code;
        if (_apcChunkLength == _apcChunk.Length)
            FlushApcChunk();
    }

    private void FlushApcChunk()
    {
        if (_apcChunkLength == 0)
            return;

        var length = _apcChunkLength;
        _apcChunkLength = 0;
        OnApcPut(new ReadOnlyMemory<char>(_apcChunk, 0, length));
    }

    /// <summary>
    /// Closes an open APC payload. Safe to call when none is open, which is what lets it be used
    /// from <see cref="Reset"/> and from every abort path without a guard at each call site.
    /// </summary>
    private void EndApc(bool terminatedCleanly)
    {
        if (!_apcHooked)
            return;

        _apcHooked = false;
        FlushApcChunk();
        OnApcUnhook(terminatedCleanly);
    }

    /// <summary>
    /// Raises the ApcHook event.
    /// </summary>
    protected virtual void OnApcHook(char introducer)
    {
        ApcHook?.Invoke(this, new ApcHookEventArgs(introducer));
    }

    /// <summary>
    /// Raises the ApcPut event.
    /// </summary>
    protected virtual void OnApcPut(ReadOnlyMemory<char> data)
    {
        ApcPut?.Invoke(this, new ApcPutEventArgs(data));
    }

    /// <summary>
    /// Raises the ApcUnhook event.
    /// </summary>
    protected virtual void OnApcUnhook(bool terminatedCleanly)
    {
        ApcUnhook?.Invoke(this, new ApcUnhookEventArgs(terminatedCleanly));
    }

    /// <summary>
    /// Raises the DcsHook event.
    /// </summary>
    protected virtual void OnDcsHook(string identifier, Params parameters)
    {
        DcsHook?.Invoke(this, new DcsHookEventArgs(identifier, parameters));
    }

    /// <summary>
    /// Raises the DcsPut event.
    /// </summary>
    protected virtual void OnDcsPut(ReadOnlyMemory<char> data)
    {
        DcsPut?.Invoke(this, new DcsPutEventArgs(data));
    }

    /// <summary>
    /// Raises the DcsUnhook event.
    /// </summary>
    protected virtual void OnDcsUnhook(bool terminatedCleanly)
    {
        DcsUnhook?.Invoke(this, new DcsUnhookEventArgs(terminatedCleanly));
    }

    /// <summary>
    /// Raises the Dcs event.
    /// </summary>
    protected virtual void OnDcs(string data, Params parameters)
    {
        Dcs?.Invoke(this, new DcsEventArgs(data, parameters));
    }

    private void DispatchOsc()
    {
        // A sequence that overflowed the cap is not dispatched at all. Dispatching what fit would
        // hand a handler attacker-chosen data that merely LOOKS complete -- a truncated OSC 52 is
        // a valid clipboard write, a truncated OSC 8 a valid link to somewhere else.
        if (_oscOverflowed)
        {
            _oscOverflowed = false;
            return;
        }

        OnOsc(_osc.ToString());
    }

    /// <summary>
    /// Raises the Osc event.
    /// </summary>
    protected virtual void OnOsc(string data)
    {
        OscFast?.Invoke(data);

        var handler = Osc;
        if (handler is not null)
            handler.Invoke(this, new OscEventArgs(data));
    }

    /// <summary>
    /// Resets the parser to initial state.
    /// </summary>
    public void Reset()
    {
        // A reset mid-image abandons it. Say so, rather than leaving a decoder open forever
        // waiting for a payload that will never arrive.
        EndDcs(terminatedCleanly: false);
        EndApc(terminatedCleanly: false);

        // And a half of a surrogate pair carried across a Write is abandoned with it.
        _pendingHighSurrogate = '\0';
        _state = ParserState.Ground;
        _params.Reset();
        _collect.Clear();
        _oscOverflowed = false;

        // Cleared here too, so an application can recover in-band: a partial write followed by RIS
        // would otherwise leave the terminal misreading the first sequence after the reset.
        _inSubParam = false;
        _subParamValue = 0;
        _osc.Clear();
        _dcs.Clear();
        _dcsChunkLength = 0;
        _dcsPendingUnhook = false;
        _dcsAccumulating = false;
        _apcChunkLength = 0;
        _apcPendingUnhook = false;
    }
}
