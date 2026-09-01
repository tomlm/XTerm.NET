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
    private readonly Terminal _terminal;
    private Buffer.TerminalBuffer _buffer;
    private AttributeData _curAttr;
    private readonly Dictionary<CharsetMode, Dictionary<char, string>?> _charsets;
    private readonly Dictionary<string, KittyNotification> _kittyNotifications = new();
    private const int MaxPendingKittyNotifications = 16;
    private const int MaxKittyNotificationBytes = 64 * 1024;
    private static readonly TimeSpan KittyNotificationTimeout = TimeSpan.FromMinutes(1);
    private Dictionary<string, List<byte>>? _kittyClipboardData;
    private Dictionary<string, StringBuilder>? _kittyClipboardBase64;
    private List<(string Alias, string Target)>? _kittyClipboardAliases;
    private string? _kittyClipboardTarget;
    private string? _kittyClipboardMimeType;
    private string? _kittyClipboardId;

    /// <summary>
    /// The table _currentCharset resolves to, cached.
    ///
    /// Print looked this up in the dictionary once per printed character to answer a question whose
    /// answer only changes on SO, SI, a charset designation or a reset -- events that happen orders
    /// of magnitude less often than printing does. Every write to _charsets or _currentCharset goes
    /// through RefreshActiveCharset so the two cannot drift.
    /// </summary>
    private Dictionary<char, string>? _activeCharset;

    /// <summary>What each G-set was DESIGNATED as, by its escape identifier.</summary>
    /// <remarks>
    /// <para>Kept alongside the resolved tables because a designation outlives its resolution: a
    /// national set resolves to ASCII while DECNRCM is reset and to itself once it is set, and
    /// the program that designated it does not designate again when the mode changes.</para>
    ///
    /// <para>All four slots are always present, seeded to US ASCII, so a designation is a value
    /// rather than a value-or-absent. DECSC, DECRC and DECNRCM each walk every slot, and "never
    /// designated" and "designated B" mean the same thing to all three.</para>
    /// </remarks>
    private readonly Dictionary<CharsetMode, string> _charsetIds = new();

    /// <summary>The four G-sets, for the walks that touch all of them.</summary>
    private static readonly CharsetMode[] GSets =
        [CharsetMode.G0, CharsetMode.G1, CharsetMode.G2, CharsetMode.G3];

    /// <summary>Which G-sets were designated as 96-character sets.</summary>
    /// <remarks>
    /// The identifier alone does not say: 'A' is the UK set in one space and ISO Latin-1 in the
    /// other, so re-resolving a designation needs to know which space it came from.
    /// </remarks>
    private readonly HashSet<CharsetMode> _ninetySixSets = new();

    /// <summary>
    /// The set a SINGLE shift has invoked for the next printed character, or null.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="_activeCharset"/> because it outranks it for exactly one
    /// character and then stops: SS2 and SS3 shift the character that follows and nothing
    /// after it. Holding it as pending state rather than swapping the active set is what makes
    /// "and then stops" automatic instead of something the print path has to remember to undo.
    /// </remarks>
    private Dictionary<char, string>? _singleShiftCharset;

    private bool _singleShiftPending;
    private CharsetMode _currentCharset;

    // Variation selector and combining character constants
    private const int VariationSelectorEmojiSymbol = 0xFE0F;  // Emoji presentation selector
    private const int VariationSelectorTextSymbol = 0xFE0E;   // Text presentation selector
    private const int ZeroWidthJoiner = 0x200D;               // ZWJ for emoji sequences
    private const int ObjectReplacementCharacter = 0xFFFC;    // stands in for an embedded object

    /// <summary>
    /// UTF-16 ceiling on one cell's cluster text. Generous next to the longest sequences that
    /// occur in real text; a bound rather than a judgement about what should join.
    /// </summary>
    private const int MaxClusterChars = 64;
    private const int KeycapCombiner = 0x20E3;                // 1️⃣: digit + VS16 + this
    private const int SkinToneFirst = 0x1F3FB;                // Fitzpatrick modifiers,
    private const int SkinToneLast = 0x1F3FF;                 // light through dark

    // Where a ZWJ was just merged, if anywhere. The character that FOLLOWS a ZWJ continues the same
    // grapheme cluster and belongs in the same cell, but it is an ordinary emoji and so passes no
    // combining-character test of its own — without this it opens a new cell and the cluster is spread
    // across the grid.
    //
    // A position rather than a flag, so it invalidates itself: anything that moves the cursor — an escape
    // sequence, a newline, a cursor address — leaves it pointing somewhere the next Print is not, and the
    // continuation is silently dropped rather than joining two unrelated characters.
    private (int Row, int Col)? _zwjContinuation;

    // Where a lone regional indicator is sitting, if one is. Two of them form one flag, and they arrive in
    // separate Print calls — so pairing them needs state that outlives a call, exactly as a ZWJ cluster does.
    //
    // Cell is where the first one went; Cursor is where it left the cursor. Both are checked, so a second
    // indicator pairs only when it lands exactly where the first one would have put it. Anything that moved
    // the cursor in between leaves two unrelated indicators standing alone, which is what they are.
    private (int Row, int Cell, int Cursor)? _regionalPending;

    public InputHandler(Terminal terminal)
    {
        _terminal = terminal;
        _buffer = terminal.Buffer;
        _curAttr = AttributeData.Default;
        _buffer.EraseAttributesProvider = GetEraseAttributes;

        // Initialize charset tables - all start as ASCII
        _charsets = new Dictionary<CharsetMode, Dictionary<char, string>?>
        {
            { CharsetMode.G0, Charsets.ASCII },
            { CharsetMode.G1, Charsets.ASCII },
            { CharsetMode.G2, Charsets.ASCII },
            { CharsetMode.G3, Charsets.ASCII }
        };

        // And the designations behind them, which ResetCharsets seeds alongside the tables.
        ResetCharsets();
    }

    /// <summary>
    /// Hangul jamo class for UAX #29's GB6-GB8: 0 none, 1 L, 2 V, 3 T, 4 LV, 5 LVT. Decomposed
    /// jamo are ORDINARY text on macOS, whose filesystems store names in NFD — a Korean
    /// directory listing arrives as L V T sequences, and without these rules each jamo takes a
    /// cell of its own.
    /// </summary>
    private static int HangulClassOf(int codePoint) => codePoint switch
    {
        >= 0x1100 and <= 0x115F or >= 0xA960 and <= 0xA97C => 1,             // L
        >= 0x1160 and <= 0x11A7 or >= 0xD7B0 and <= 0xD7C6 => 2,             // V
        >= 0x11A8 and <= 0x11FF or >= 0xD7CB and <= 0xD7FB => 3,             // T
        >= 0xAC00 and <= 0xD7A3 => (codePoint - 0xAC00) % 28 == 0 ? 4 : 5,   // LV / LVT
        _ => 0,
    };

    /// <summary>
    /// One bit per BMP codepoint: might it join the previous cell's cluster — the category rules
    /// or the sequence rules. Built ONCE from the reference predicates below, so the table cannot
    /// drift from them; it exists because the hot path must not pay a category lookup per
    /// character, and the corpora that hurt were exactly the ones full of characters no range
    /// check anticipates — box drawing in TUI redraws, CJK in prose. 8KB, cold half never touched.
    /// </summary>
    private static readonly byte[] MayJoinBmp = BuildMayJoinBmp();

    /// <summary>
    /// The character last printed, and the cursor position it left behind. See
    /// <see cref="RememberForRepeat"/>. <c>LastCodePoint</c> is the final codepoint of the cell's
    /// cluster — the base itself until an append replaces it — because the sequence rules in
    /// <see cref="RefusesSequenceCheaply"/> ask what the cluster ENDS with, while REP repeats the
    /// whole cluster and reads <c>CodePoint</c>/<c>ClusterId</c>.
    /// </summary>
    private (int Row, int CursorCol, int CodePoint, int ClusterId, int LastCodePoint)? _lastPrinted;

    /// <summary>
    /// Whether runs of printable ASCII take the batched path. On by default.
    ///
    /// Turning it off routes every character through <see cref="Print"/> instead, which is the
    /// reference behaviour the batched path has to reproduce. That makes the two differentially
    /// testable against each other, and gives anyone who suspects the fast path a way to rule it out.
    /// </summary>
    public bool UseRunPrinting { get; set; } = true;

    /// <summary>
    /// The byte-span twin of <see cref="PrintAsciiRun"/>, for callers feeding UTF-8 directly.
    ///
    /// Printable ASCII bytes are their own codepoints, so this never decodes anything — which is the
    /// point of the byte entry: the UTF-16 transcode that a string-based Write forces on the caller
    /// buys nothing for the bytes that make up most terminal output.
    /// </summary>
    internal void PrintAsciiRun(ReadOnlySpan<byte> data)
    {
        // A multi-row block means cells that must be skipped rather than written, which a span write
        // cannot express -- so the run goes character by character, as it already does for insert
        // mode and for a designated charset.
        // Wraparound off joins the list of states the run path does not model: with DECAWM off a
        // character past the last column OVERWRITES it rather than being discarded, and the run
        // path can only stop. Rare enough to hand to Print rather than teach twice -- the default
        // is on, so nothing in normal output takes this branch.
        if (!UseRunPrinting || _terminal.InsertMode || _activeCharset is not null
            || _singleShiftPending
            || _buffer.HasMultiRowSizedRuns || !_terminal.Options.Wraparound)
        {
            foreach (var b in data)
                Print(CodePointText.Get((char)b));
            return;
        }

        // A pending ZWJ continuation is per-character state the run path does not carry: it writes
        // its span directly and never consults or clears _zwjContinuation, so an emoji ending in
        // ZWJ followed by an ASCII chunk lost the continuation that Print would have honoured --
        // the two paths disagreed about the same bytes depending only on how they were chunked.
        //
        // ONE character, not the whole run. The state belongs to the character standing where the
        // ZWJ was merged, and the first Print clears it unconditionally, so only that character
        // needs the slow path. Handing the entire run over meant every ASCII run following an
        // emoji printed a character at a time -- and text that mixes emoji with words is the
        // ordinary case, not an exotic one.
        if (_zwjContinuation is not null)
        {
            Print(CodePointText.Get((char)data[0]));
            data = data[1..];
            if (data.IsEmpty)
                return;
        }

        while (!data.IsEmpty)
        {
            if (_buffer.X > WrapLimit())
            {
                if (!_terminal.Options.Wraparound)
                    return;

                // Full-width wraps only, as in Print: a wrap inside the margin box continues
                // the box, not the line.
                var lineWrap = WrapLimit() == _terminal.Cols - 1 && WrapHome() == 0;
                if (_buffer.Y == _buffer.ScrollBottom)
                {
                    _buffer.SetCursor(WrapHome(), _buffer.Y);
                    _buffer.ScrollUp(1, true);
                }
                else
                {
                    _buffer.SetCursor(WrapHome(), _buffer.Y + 1);
                }

                if (lineWrap)
                    _buffer.Lines[_buffer.Y + _buffer.YBase]!.IsWrapped = true;
            }

            var line = _buffer.Lines[_buffer.Y + _buffer.YBase];
            if (line == null)
                return;

            // Bounded by the right MARGIN, not the screen. A batched run bypasses the per-character
            // wrap check above, so without this it writes straight through the margin and out the
            // other side -- and only when the fast path takes the write, which is the difference
            // that reads as an intermittent fault rather than a missing case.
            var take = Math.Min(WrapLimit() + 1 - _buffer.X, data.Length);
            // Guarded HERE rather than inside SetSingleWidthRun: putting the check in that method
            // pushed it past the JIT's inlining budget and cost the ASCII corpus 8%. A line that
            // never held a wide cell cannot have a half to orphan.
            if (line.HasWideCells)
                line.RepairAround(_buffer.X, _buffer.X + data[..take].Length);

            line.SetSingleWidthRun(_buffer.X, data[..take], _curAttr);

            // This path bypasses Print, so it keeps the link bookkeeping itself -- otherwise a link
            // would cover the text or not depending on which writer took it. Guarded here, not in a
            // helper, for the same reason as in Print.
            if (_linkUrl is not null || line.HasLinks)
                line.NoteLinkRun(_buffer.X, take, _linkUrl, _linkId);

            // As above: a run of plain text erases any scaled run it lands on.
            if (line.HasSizedRuns)
                line.NoteSizedRun(_buffer.X, take, TextSizing.Default);

            _buffer.SetCursorRaw(_buffer.X + take, _buffer.Y);

            // This path bypasses Print, so it has to keep REP's record itself -- otherwise the same
            // input would repeat or not depending on which writer took it.
            RememberForRepeat(data[take - 1], ClusterTable.None);

            data = data[take..];
        }
    }

    /// <summary>
    /// Prints a run of printable ASCII in one pass, instead of one character at a time.
    ///
    /// Per character, Print does work that is identical for every character in a run: decode the
    /// codepoint, test it for combining, resolve the charset, resolve the width, build a cell, check
    /// bounds, clear the line cache, advance the cursor. For a run of ordinary text all of that
    /// collapses -- printable ASCII is single-width, never combining, and with no charset designated
    /// it translates to itself -- so the run can be written as a span and the cursor moved once.
    ///
    /// Falls back to the per-character path whenever an assumption does not hold. Insert mode has to
    /// shift the tail of the line for every character, and a designated charset means each one may
    /// expand to different text; neither is expressible as a straight span write, and both are rare.
    /// </summary>
    internal void PrintAsciiRun(string data, int start, int count)
    {
        // As above: a buffer holding a multi-row block takes the per-character path.
        // Wraparound off joins the list of states the run path does not model: with DECAWM off a
        // character past the last column OVERWRITES it rather than being discarded, and the run
        // path can only stop. Rare enough to hand to Print rather than teach twice -- the default
        // is on, so nothing in normal output takes this branch.
        if (!UseRunPrinting || _terminal.InsertMode || _activeCharset is not null
            || _singleShiftPending
            || _buffer.HasMultiRowSizedRuns || !_terminal.Options.Wraparound)
        {
            for (var k = 0; k < count; k++)
                Print(CodePointText.Get(data[start + k]));
            return;
        }

        // As in the span overload: the pending ZWJ continuation belongs to ONE character, and the
        // first Print clears it, so only that character takes the slow path rather than the run.
        if (_zwjContinuation is not null)
        {
            Print(CodePointText.Get(data[start]));
            start++;
            count--;
            if (count == 0)
                return;
        }

        var pos = start;
        var remaining = count;

        while (remaining > 0)
        {
            // Autowrap, matching Print. The cursor is allowed to rest one past the last column, so
            // the wrap is resolved here rather than when the previous character was written.
            if (_buffer.X > WrapLimit())
            {
                if (!_terminal.Options.Wraparound)
                    return;   // printing past the edge is discarded, as in Print

                // Full-width wraps only, as in Print: a wrap inside the margin box continues
                // the box, not the line.
                var lineWrap = WrapLimit() == _terminal.Cols - 1 && WrapHome() == 0;
                if (_buffer.Y == _buffer.ScrollBottom)
                {
                    _buffer.SetCursor(WrapHome(), _buffer.Y);
                    _buffer.ScrollUp(1, true);
                }
                else
                {
                    _buffer.SetCursor(WrapHome(), _buffer.Y + 1);
                }

                if (lineWrap)
                    _buffer.Lines[_buffer.Y + _buffer.YBase]!.IsWrapped = true;
            }

            var line = _buffer.Lines[_buffer.Y + _buffer.YBase];
            if (line == null)
                return;

            // As above: the margin bounds the batch, or the fast path leaks past it.
            var take = Math.Min(WrapLimit() + 1 - _buffer.X, remaining);
            // Guarded HERE rather than inside SetSingleWidthRun: putting the check in that method
            // pushed it past the JIT's inlining budget and cost the ASCII corpus 8%. A line that
            // never held a wide cell cannot have a half to orphan.
            if (line.HasWideCells)
                line.RepairAround(_buffer.X, _buffer.X + take);

            line.SetSingleWidthRun(_buffer.X, data.AsSpan(pos, take), _curAttr);

            // As above: bypassing Print means keeping the link bookkeeping here as well.
            if (_linkUrl is not null || line.HasLinks)
                line.NoteLinkRun(_buffer.X, take, _linkUrl, _linkId);

            // As above: a run of plain text erases any scaled run it lands on.
            if (line.HasSizedRuns)
                line.NoteSizedRun(_buffer.X, take, TextSizing.Default);

            // SetCursorRaw, as Print uses, so X may land one past the last column pending a wrap.
            _buffer.SetCursorRaw(_buffer.X + take, _buffer.Y);

            // As above: bypassing Print means keeping REP's record here as well.
            RememberForRepeat(data[pos + take - 1], ClusterTable.None);

            pos += take;
            remaining -= take;
        }
    }

    /// <summary>
    /// Attempts to append a combining character to the previous cell.
    /// </summary>
    /// <param name="data">The combining character string.</param>
    /// <param name="codePoint">The code point of the combining character.</param>
    /// <returns>True if successfully combined, false otherwise.</returns>
    private bool TryAppendToPreviousCell(string data, int codePoint)
    {
        var line = _buffer.Lines[_buffer.Y + _buffer.YBase];
        if (line == null)
            return false;

        // Find the previous cell position
        int prevX = _buffer.X - 1;

        // If we're at the start of a line, we might need to look at the previous line
        if (prevX < 0)
        {
            // Check if the previous line exists and is wrapped
            if (_buffer.Y > 0 || _buffer.YBase > 0)
            {
                var prevLineIndex = _buffer.Y + _buffer.YBase - 1;
                if (prevLineIndex >= 0)
                {
                    var prevLine = _buffer.Lines[prevLineIndex];
                    if (prevLine != null && prevLine.IsWrapped)
                    {
                        line = prevLine;
                        prevX = _terminal.Cols - 1;
                    }
                    else
                    {
                        return false; // Can't combine at start of unwrapped line
                    }
                }
                else
                {
                    return false; // No previous line
                }
            }
            else
            {
                return false; // At the very beginning of the buffer
            }
        }

        // Get the previous cell
        if (prevX < 0 || prevX >= line.Length)
            return false;

        // A position showing part of a picture has no text to combine with. Kitty's placeholders are
        // followed by combining marks that state a row and column, so this is the ordinary case
        // rather than a curiosity: letting the mark combine here would both put an accent in the
        // middle of the picture and stop the mark being read as the tile it is.
        if (line.TryGetPlacementAt(prevX, out _))
            return false;

        var prevCell = line[prevX];

        // Skip placeholder cells (width 0) for wide characters - find the actual character cell
        while (prevX > 0 && prevCell.Width == 0)
        {
            prevX--;
            prevCell = line[prevX];
        }

        // Can't combine with empty cells
        if (prevCell.IsEmpty())
        {
            // Only allow combining with actual content, not empty/space cells
            // unless the space is the only content (which shouldn't happen for valid sequences)
            return false;
        }

        // A skin tone modifies an EMOJI. Attaching it to whatever happened to come first put the tone
        // inside a box-drawing character for "║🏼║" and drew the pair as one unreadable cell, where every
        // other terminal shows the swatch standing on its own. Refusing here sends it back to Print, which
        // gives it a cell of its own.
        if (IsSkinToneModifier(codePoint) && !CanTakeSkinTone(LastRuneOf(prevCell.Content)))
        {
            return false;
        }

        // The sequence rules (GB6-GB8, GB9c): the current codepoint alone cannot decide these —
        // whether it continues the cluster depends on what the cluster ENDS with. A refusal here
        // is not an error; Print gives the character an ordinary cell, exactly as a syllable
        // following a complete syllable should get. The bracket is the candidate ranges' hull:
        // ordinary combining marks — most of what ever joins — skip both class tests in one
        // compare, which the profiler charged this method 3.6 points of the unicode corpus for.
        var conjunctConsonantJoined = false;
        if (((uint)(codePoint - 0x0900) <= 0xD7FB - 0x0900 || codePoint >= 0x10A00 && codePoint <= 0x11FFF)
            && codePoint != ZeroWidthJoiner)
        {
            var hangulClass = HangulClassOf(codePoint);
            if (hangulClass != 0)
            {
                if (!HangulJoins(HangulClassOf(LastRuneOf(prevCell.Content)), hangulClass))
                    return false;
            }
            else if (IsConjunctConsonantCandidate(codePoint) && !IsCombiningCharacter(codePoint))
            {
                // GB9c: the consonant joins when the cluster ends with a linker — or with a ZWJ
                // the linker precedes, the explicit-conjunct form. Anything else is a new cluster.
                var last = LastRuneOf(prevCell.Content);
                if (!IsConjunctLinker(last)
                    && !(last == ZeroWidthJoiner && IsConjunctLinker(RuneBeforeLastOf(prevCell.Content))))
                    return false;

                conjunctConsonantJoined = true;
            }
        }

        // Append the combining character to the previous cell's content
        // A cluster has a practical ceiling: the longest real ones -- a family emoji with skin
        // tones, a Devanagari conjunct with matras -- run to a dozen or so codepoints. Without a
        // cap, a program that sends one base character and then combining marks forever grows a
        // single cell's string without bound, and every intermediate length is interned
        // permanently: REP of a self-joining cluster did this quadratically inside one CSI
        // sequence. Refusing the join sends the mark to a cell of its own, which is what a
        // terminal that cannot stack any more marks should show anyway.
        // The RESULT is what the ceiling applies to: a supplementary combining character or a
        // variation selector is two UTF-16 units, so appending one to a 63-unit cluster produced
        // 65 despite the documented 64.
        if (prevCell.Content.Length + data.Length > MaxClusterChars)
            return false;

        var newContent = prevCell.Content + data;

        // The appended codepoint's own column contribution, computed INCREMENTALLY: re-walking
        // the whole cluster through the width loop on every append cost the unicode corpus 66%
        // -- an emoji family re-measured itself once per member. The cases mirror the loop's
        // branches exactly; a matra or a joined conjunct consonant widens the cell it joins,
        // because wcwidth arithmetic gives them their columns and every wcwidth-consuming
        // application lays out on that sum. Clamped to the grid's two columns.
        var contribution = 0;
        if (codePoint == VariationSelectorEmojiSymbol && prevCell.Width == 1)
            contribution = 1;
        else if (codePoint == VariationSelectorTextSymbol && prevCell.Width == 2)
            contribution = -1;
        else if (conjunctConsonantJoined)
            contribution = 1;
        else if (codePoint >= 0x0903 && codePoint < 0x1F000
                 && CharUnicodeInfo.GetUnicodeCategory(codePoint)
                 == UnicodeCategory.SpacingCombiningMark
                 && !IsConjunctLinker(codePoint))
            // A linker is checked FIRST: Javanese pangkon and Grantha virama are category Mc,
            // but they are killers, not vowels -- a dead consonant stays one column.
            contribution = 1;
        int newWidth = Math.Clamp(prevCell.Width + contribution, 1, 2);

        // Create the updated cell
        var updatedCell = new BufferCell
        {
            Content = newContent,
            Width = newWidth,
            Attributes = prevCell.Attributes,
            CodePoint = prevCell.CodePoint  // Keep the original base code point
        };

        line.SetCell(prevX, ref updatedCell);

        // Handle width changes
        if (newWidth != prevCell.Width)
        {
            if (newWidth == 2 && prevCell.Width == 1)
            {
                // Need to add a spacer cell after the character
                // Check if cursor position needs adjustment
                if (prevX + 1 < _terminal.Cols)
                {
                    // Use BufferCell.Spacer with the previous cell's attributes
                    var spacer = BufferCell.Empty;
                    spacer.Attributes = prevCell.Attributes;
                    line.SetCell(prevX + 1, ref spacer);

                    // Adjust cursor if we're after this cell
                    if (_buffer.X > prevX)
                    {
                        _buffer.SetCursorRaw(Math.Min(_buffer.X + 1, _terminal.Cols), _buffer.Y);
                    }
                }
            }
            else if (newWidth == 1 && prevCell.Width == 2)
            {
                // Remove the spacer cell by replacing with whitespace
                if (prevX + 1 < _terminal.Cols)
                {
                    // Use BufferCell.Whitespace with the previous cell's attributes
                    var emptyCell = BufferCell.Space;
                    emptyCell.Attributes = prevCell.Attributes;
                    line.SetCell(prevX + 1, ref emptyCell);

                    // Adjust cursor if we're after this cell
                    if (_buffer.X > prevX + 1)
                    {
                        _buffer.SetCursorRaw(Math.Max(_buffer.X - 1, 0), _buffer.Y);
                    }
                }
            }
        }

        // The preceding character is now the COMBINED one -- repeating the base letter without its
        // marks would be a different character from the one on screen. Recorded AFTER the width
        // adjustments above, which can move the cursor: recorded any earlier, a variation selector
        // that widened the cell left the saved position stale and silently cancelled the next REP.
        _lastPrinted = (_buffer.Y + _buffer.YBase, _buffer.X, updatedCell.CodePoint, updatedCell.ClusterId, codePoint);

        return true;
    }

    /// <summary>
    /// Handles CSI sequences (Control Sequence Introducer).
    /// </summary>
    public void HandleCsi(string identifier, Params parameters)
    {
        bool isPrivate = identifier.IsPrivateMode();
        var command = identifier.ToCsiCommand();

        // Any sequence between a print and a REP means there is no preceding character any more.
        // REP is the one exemption: the character IT prints becomes the preceding one.
        if (command != CsiCommand.RepeatPrecedingCharacter)
            CancelRepeat();

        switch (command)
        {
            case CsiCommand.RepeatPrecedingCharacter:
                RepeatPrecedingCharacter(parameters);
                break;

            case CsiCommand.InsertChars:
                InsertChars(parameters);
                break;

            case CsiCommand.CursorUp:
                CursorUp(parameters);
                break;

            case CsiCommand.CursorDown:
                CursorDown(parameters);
                break;

            case CsiCommand.CursorForward:
                CursorForward(parameters);
                break;

            case CsiCommand.CursorBackward:
                CursorBackward(parameters);
                break;

            case CsiCommand.CursorNextLine:
                CursorNextLine(parameters);
                break;

            case CsiCommand.CursorPreviousLine:
                CursorPrecedingLine(parameters);
                break;

            case CsiCommand.CursorCharAbsolute:
                CursorCharAbsolute(parameters);
                break;

            case CsiCommand.CursorPosition:
                CursorPosition(parameters);
                break;

            case CsiCommand.CursorForwardTab:
                CursorForwardTab(parameters);
                break;

            case CsiCommand.EraseInDisplay:
                EraseInDisplay(parameters, isPrivate);
                break;

            case CsiCommand.EraseInLine:
                EraseInLine(parameters, isPrivate);
                break;

            case CsiCommand.InsertLines:
                InsertLines(parameters);
                break;

            case CsiCommand.DeleteLines:
                DeleteLines(parameters);
                break;

            case CsiCommand.DeleteChars:
                DeleteChars(parameters);
                break;

            case CsiCommand.ScrollUp:
                ScrollUp(parameters);
                break;

            case CsiCommand.GraphicsAttributes:
                GraphicsAttributes(parameters);
                break;

            case CsiCommand.ScrollDown:
                ScrollDown(parameters);
                break;

            case CsiCommand.EraseChars:
                EraseChars(parameters);
                break;

            case CsiCommand.CursorBackwardTab:
                CursorBackwardTab(parameters);
                break;

            case CsiCommand.TabClear:
                TabClear(parameters);
                break;

            case CsiCommand.DeviceAttributes:
                // The identifier goes in whole, not as isPrivate: "?c" and ">c" both set that flag,
                // and only one of them is the secondary DA.
                DeviceAttributes(identifier, parameters);
                break;

            case CsiCommand.LinePositionAbsolute:
                LinePositionAbsolute(parameters);
                break;

            case CsiCommand.SelectGraphicRendition:
                CharAttributes(parameters);
                break;

            case CsiCommand.SoftReset:
                _terminal.SoftReset();
                break;

            case CsiCommand.CopyRectangularArea:
                CopyRectangularArea(parameters);
                break;

            case CsiCommand.FillRectangularArea:
                FillRectangularArea(parameters);
                break;

            case CsiCommand.EraseRectangularArea:
                EraseRectangularArea(parameters);
                break;

            case CsiCommand.SelectiveEraseRectangularArea:
                SelectiveEraseRectangularArea(parameters);
                break;

            case CsiCommand.XtermSaveMode:
                XtermSaveMode(parameters);
                break;

            case CsiCommand.XtermRestoreMode:
                XtermRestoreMode(parameters);
                break;

            case CsiCommand.SetTitleModes:
                SetTitleModes(parameters, enable: true);
                break;

            case CsiCommand.ResetTitleModes:
                SetTitleModes(parameters, enable: false);
                break;

            case CsiCommand.InsertColumns:
                InsertColumns(parameters);
                break;

            case CsiCommand.DeleteColumns:
                DeleteColumns(parameters);
                break;

            case CsiCommand.SelectCharacterProtection:
                SelectCharacterProtection(parameters);
                break;

            case CsiCommand.SelectAttributeChangeExtent:
                _attributeChangeExtent = parameters.GetParam(0, 0);
                break;

            case CsiCommand.SelectActiveStatusDisplay:
                _activeStatusDisplay = parameters.GetParam(0, 0);
                break;

            case CsiCommand.SelectStatusDisplayType:
                _statusDisplayType = parameters.GetParam(0, 0);
                break;

            case CsiCommand.RequestTerminalParameters:
                RequestTerminalParameters(parameters);
                break;

            case CsiCommand.SetColumnsPerPage:
                // DECSCPP. 80 and 132 are the only widths DEC defines, and 0 means 80.
                // It does NOT erase: unlike DECCOLM it says nothing about the contents,
                // and vttest's page-format test fills the screen and then checks it.
                // 0, 80 and 132 are the widths DECSCPP defines, and 0 means 80. Anything else
                // is ignored rather than rounded: coercing it turned CSI 81 $ | into a resize
                // to 80 and CSI 999 $ | into one to 132, so a malformed request moved the
                // screen instead of being declined.
                var pageColumns = parameters.GetParam(0, 0);
                if (pageColumns is 0 or 80 or 132)
                    _terminal.SetPageWidth(pageColumns == 132 ? 132 : 80, clear: false);
                break;

            case CsiCommand.SetLinesPerScreen:
                var screenLines = parameters.GetParam(0, 0);
                if (screenLines >= 1)
                    _terminal.Resize(_terminal.Cols, screenLines);
                break;

            case CsiCommand.SelectConformanceLevel:
                var level = parameters.GetParam(0, 65);
                if (level >= 61 && level <= 65)
                    _terminal.ConformanceLevel = level;
                break;

            case CsiCommand.RequestChecksumRectangularArea:
                RequestChecksumRectangularArea(parameters);
                break;

            case CsiCommand.DeviceStatusReport:
                DeviceStatusReport(parameters, isPrivate);
                break;

            case CsiCommand.SetScrollRegion:
                SetScrollRegion(parameters);
                break;

            case CsiCommand.SaveCursorAnsi:
                // CSI s is two sequences sharing a final character, and the mode decides which.
                // With DECLRMM set it is DECSLRM; without it, Save Cursor. This is the one place in
                // the dispatch where that is true, and getting it backwards would make an
                // application's margins silently save the cursor instead.
                if (_terminal.LeftRightMarginMode)
                    SetLeftRightMargins(parameters);
                else
                    SaveCursorAnsi();
                break;

            case CsiCommand.RestoreCursorAnsi:
                RestoreCursorAnsi();
                break;

            case CsiCommand.WindowManipulation:
                WindowManipulation(parameters);
                break;

            case CsiCommand.SelectCursorStyle:
                // "CSI > Ps q" is XTVERSION, not DECSCUSR. They share a final character, so ">q"
                // is listed alongside " q" in the command map and the two are told apart here --
                // without that a terminal version query reshaped the cursor instead of being
                // answered, and a program that asks on startup left the user in a cursor they
                // never chose. It is the one place a CsiCommand is deliberately shared by two
                // sequences: the map decides everything else on the exact identifier.
                //
                // The marker is read rather than isPrivate because isPrivate is true for '?' as
                // well as '>', and "CSI ? Ps q" is neither of these sequences. The map is what
                // keeps it out -- "?q" is not a key, so it resolves to Unknown and never reaches
                // this case. The switch below is defence in depth, not a live guard: the only
                // identifiers that arrive are " q" and ">q".
                switch (identifier.PrivateMarker())
                {
                    case '>':
                        ReportVersion(parameters);
                        break;
                    case '\0':
                        SelectCursorStyle(parameters);
                        break;
                    // Unreachable while the map lists only those two. Falling through rather than
                    // defaulting to SelectCursorStyle means a marked form added to the map later
                    // is ignored instead of reshaping the cursor on some unrelated query's behalf.
                }
                break;

            case CsiCommand.RequestMode:
                HandleRequestMode(parameters, isPrivate);
                break;

            case CsiCommand.SetMode:
                SetCSIModeParameters(parameters, isPrivate: isPrivate);
                break;

            case CsiCommand.ResetMode:
                // DEC Private Mode Reset (CSI ? Pm l)
                ResetCSIModeParameters(parameters, isPrivate: isPrivate);
                break;

            case CsiCommand.KittyKeyboardSet:
                KittyKeyboardSet(parameters);
                break;

            case CsiCommand.KittyKeyboardQuery:
                KittyKeyboardQuery();
                break;

            case CsiCommand.KittyKeyboardPush:
                KittyKeyboardPush(parameters);
                break;

            case CsiCommand.KittyKeyboardPop:
                KittyKeyboardPop(parameters);
                break;

            case CsiCommand.Unknown:
                // Log unknown sequence for debugging
                System.Diagnostics.Debug.WriteLine($"Unknown CSI sequence: {identifier}");
                break;
        }
    }

    /// <summary>
    /// Handles ESC sequences.
    /// </summary>
    public void HandleEsc(string finalChar, string collected)
    {
        CancelRepeat();

        // An intermediate changes which sequence this IS, so the bare finals cannot be matched
        // before knowing there is none. Running both switches meant ESC # 8 (DECALN, which vttest
        // sends) also took the "8" arm and restored the cursor, and ESC ( = designated G0 while
        // enabling the application keypad on the way past.
        if (collected.Length == 0)
        {
            switch (finalChar)
            {
                case "D": // IND - Index
                    IndexDown();
                    break;
                case "E": // NEL - Next Line
                    NextLine();
                    break;
                case "M": // RI - Reverse Index
                    ReverseIndex();
                    break;
                case "H": // HTS - set a tab stop at the cursor column
                    _terminal.SetTabStop(_buffer.X);
                    break;
                case "=": // DECKPAM - application keypad
                    // terminfo's smkx for xterm is ESC [ ? 1 h ESC =, so a program enabling
                    // application cursor keys enabled the keypad in the same breath -- and the second
                    // half was dropped. The keypad generators honoured a mode nothing could set
                    // except DECSET 66.
                    _terminal.ApplicationKeypad = true;
                    break;
                case ">": // DECKPNM - numeric keypad
                    _terminal.ApplicationKeypad = false;
                    break;
                case "c": // RIS - Reset to Initial State
                    ResetTerminal();
                    break;
                case "7": // DECSC - Save Cursor
                    SaveCursor();
                    break;
                case "N": // SS2 - single shift G2, for the next character only
                    InvokeSingleShift(CharsetMode.G2);
                    break;
                case "O": // SS3 - single shift G3
                    InvokeSingleShift(CharsetMode.G3);
                    break;
                case "n": // LS2 - lock G2 into GL until the next shift
                    LockingShift(CharsetMode.G2);
                    break;
                case "o": // LS3 - lock G3 into GL
                    LockingShift(CharsetMode.G3);
                    break;
                case "Z": // DECID - the ancient identify; answers like the primary DA
                    _terminal.RaiseDataReceived(PrimaryDeviceAttributes);
                    break;

                case "V": // SPA - Start of Protected Area
                    StartProtectedArea();
                    break;

                case "W": // EPA - End of Protected Area
                    EndProtectedArea();
                    break;

                case "6": // DECBI - Back Index
                    BackIndex();
                    break;

                case "9": // DECFI - Forward Index
                    ForwardIndex();
                    break;

                case "8": // DECRC - Restore Cursor
                    RestoreCursor();
                    break;
            }
        }
        else
        {
            // Charset designation and the DEC line attributes, which are the sequences an
            // intermediate introduces.
            var intermediateChar = collected[0];
            switch (intermediateChar)
            {
                case '(': // Designate G0 character set
                    SetCharset(CharsetMode.G0, finalChar);
                    break;
                case ')': // Designate G1 character set
                    SetCharset(CharsetMode.G1, finalChar);
                    break;
                case '*': // Designate G2 character set
                    SetCharset(CharsetMode.G2, finalChar);
                    break;
                case '+': // Designate G3 character set
                    SetCharset(CharsetMode.G3, finalChar);
                    break;
                case '-': // Designate G1 as a 96-character set
                case '.': // Designate G2
                case '/': // Designate G3
                    // The 96-set forms, which the 94-set cases above do not cover. Their
                    // identifiers live in a DIFFERENT space: 'A' here is ISO Latin-1, where
                    // 'A' after ESC ( is the United Kingdom set. Routing these through the
                    // same lookup would designate UK for a program that asked for Latin-1 and
                    // silently turn its '#' into a pound sign.
                    SetNinetySixCharset(
                        intermediateChar switch
                        {
                            '-' => CharsetMode.G1,
                            '.' => CharsetMode.G2,
                            _ => CharsetMode.G3,
                        },
                        finalChar);
                    break;

                case '#': // DEC line attribute sequences
                    HandleDecLineAttribute(finalChar);
                    break;

                case ' ': // ANSI announcement sequences
                    // S7C1T (ESC SP F) and S8C1T (ESC SP G) choose the form the terminal's own
                    // REPLIES take: ESC [ or the single byte 0x9B. They say nothing about what
                    // is accepted on input, which has always taken both.
                    if (finalChar == "F")
                        _terminal.EightBitControls = false;
                    else if (finalChar == "G")
                        _terminal.EightBitControls = true;
                    break;
            }
        }
    }

    #region DCS / Sixel / DECRQSS

    /// <summary>The Sixel image being decoded, if a DECSIXEL payload is currently arriving.</summary>
    private Graphics.SixelDecoder? _sixelDecoder;

    /// <summary>
    /// The colour registers used when mode 1070 is reset, so images inherit each other's palette
    /// the way they did on a VT340. Built on first use, because the default is private registers
    /// and most sessions never touch this.
    /// </summary>
    private Graphics.SixelPalette? _sharedSixelPalette;

    /// <summary>The XTGETTCAP request being read, if a DCS + q payload is currently arriving.</summary>
    private StringBuilder? _capabilityRequest;

    /// <summary>Whether that request ran past <see cref="MaxCapabilityRequestLength"/>.</summary>
    private bool _capabilityRequestTooLong;

    /// <summary>
    /// How much of an XTGETTCAP request will be read before it is treated as malformed. A real
    /// request is a handful of capability names; anything past this is not one, and accumulating it
    /// would let a peer make the terminal hold an arbitrary amount of memory for a reply nobody
    /// asked for.
    /// </summary>
    private const int MaxCapabilityRequestLength = 4096;

    /// <summary>
    /// Accumulates the payload of a DECRQSS sequence (<c>DCS $ q … ST</c>) while it streams in.
    /// Null when no DECRQSS is active.
    /// </summary>
    private StringBuilder? _decrqssPayload;

    /// <summary>
    /// The most of a DECRQSS payload worth keeping.
    /// </summary>
    /// <remarks>
    /// Every setting that can be asked for is three characters at most, so a longer payload is one
    /// we are going to refuse anyway. Truncating at the door keeps a <c>DCS $ q</c> followed by a
    /// megabyte of anything from being buffered on its way to that refusal.
    /// </remarks>
    private const int MaxDecrqssPayloadLength = 16;

    /// <summary>The text of the APC sequence currently arriving.</summary>
    /// <remarks>
    /// One sequence at a time. A Kitty image spans several, and what carries across them is
    /// <see cref="_kittyTransmission"/>, not this.
    /// </remarks>
    private readonly StringBuilder _apcPayload = new();

    /// <summary>The image being assembled across several sequences, if one is.</summary>
    private Graphics.KittyTransmission? _kittyTransmission;

    /// <summary>Images the client has transmitted and may ask to see again.</summary>
    private readonly Graphics.ImageRegistry _kittyImages = new();

    /// <summary>
    /// Every image that has been given frames, held weakly.
    /// </summary>
    /// <remarks>
    /// <para>The animation clock is asked whether anything is moving on every host frame, so the
    /// answer has to be cheap. Scanning both buffers and the registry is not: it is the length of
    /// the scrollback, sixty times a second, to discover that a terminal showing text is showing
    /// text.</para>
    /// <para>Weak references because a strong set would keep every animation's pixels alive for the
    /// life of the terminal, defeating the whole point of letting the last cell holding a picture
    /// take it away. Dead entries are pruned as they are found.</para>
    /// </remarks>
    private readonly List<WeakReference<Graphics.TerminalImage>> _animatedImages = new();

    internal IEnumerable<Graphics.TerminalImage> AnimatedImages
    {
        get
        {
            for (int i = _animatedImages.Count - 1; i >= 0; i--)
            {
                if (_animatedImages[i].TryGetTarget(out var image))
                    yield return image;
                else
                    _animatedImages.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// Ceiling on the base64 held for one image, so a client that never sends its last chunk
    /// cannot make the terminal grow without limit.
    /// </summary>
    private int MaxKittyPayloadChars
    {
        get
        {
            // Enough base64 for the largest image allowed, plus slack for a PNG's own overhead.
            var bytes = (long)_terminal.Options.MaxSixelPixels * Graphics.TerminalImage.BytesPerPixel;
            var encoded = bytes * 4 / 3 + 1024;
            return (int)Math.Clamp(encoded, 4096, int.MaxValue);
        }
    }

    /// <summary>
    /// U+10EEEE, the character Kitty uses to mean "part of a picture belongs in this cell".
    /// </summary>
    private const int KittyPlaceholder = 0x10EEEE;

    /// <summary>
    /// Where the placeholder rectangle currently being written started, so a cell can work out
    /// which tile of the picture it is.
    /// </summary>
    /// <remarks>
    /// The serial is held here as well as the position so that every cell of one rectangle belongs
    /// to the same placement, and a delete aimed at any of them takes the whole picture.
    /// </remarks>
    private (int Row, int Col, uint ImageId, Graphics.TerminalImage Image, int Serial)? _placeholderOrigin;

    /// <summary>
    /// The placeholder cell just written, and how many of its combining marks have arrived.
    /// </summary>
    /// <remarks>
    /// The marks modify the cell BEFORE them, and there are up to three: row, then column, then the
    /// most significant byte of the image id. Tracking the count is what tells the second from the
    /// first, since the characters themselves are drawn from one table and carry no clue which
    /// position they are filling.
    /// </remarks>
    private (int Row, int Col, int MarksSeen)? _placeholderCell;

    #endregion
    /// <summary>
    /// Handles OSC sequences (Operating System Command).
    /// </summary>
    public void HandleOsc(string data)
    {
        CancelRepeat();
        var parts = data.Split(new[] { ';' }, 2);
        if (parts.Length == 0)
            return;

        var arg = parts.Length > 1 ? parts[1] : string.Empty;

        // Whether this sequence reached a handler. Cleared by the branches that do nothing with it,
        // so a listener can tell "the terminal acted on this" from "the terminal saw it and moved on".
        var recognized = true;

        // Try to parse as OscCommand enum
        if (parts[0].TryParseOscCommand(out OscCommand command))
        {
            switch (command)
            {
                case OscCommand.SetIconAndTitle:
                case OscCommand.SetWindowTitle:
                case OscCommand.SetIconName:
                {
                    var text = DecodeTitleArgument(arg);
                    if (text is null)
                        break;
                    if (command != OscCommand.SetIconName)
                    {
                        _terminal.Title = text;
                        _terminal.RaiseTitleChanged(text);
                    }
                    if (command != OscCommand.SetWindowTitle)
                        _terminal.IconTitle = text;
                    break;
                }

                case OscCommand.ChangeColor:
                    HandleColorPaletteChange(arg);
                    break;

                case OscCommand.ChangeSpecialColor:
                    HandleSpecialColorChange(arg);
                    break;

                case OscCommand.ResetSpecialColor:
                    HandleSpecialColorReset(arg);
                    break;

                case OscCommand.CurrentDirectory:
                    HandleCurrentDirectory(arg);
                    break;

                case OscCommand.Hyperlink:
                    HandleHyperlink(arg);
                    break;

                case OscCommand.ConEmu:
                    HandleConEmu(arg);
                    break;

                case OscCommand.ShellIntegration:
                    HandleShellIntegration(arg);
                    break;

                case OscCommand.ITerm2:
                    recognized = HandleITerm2(arg);
                    break;

                case OscCommand.ForegroundColor:
                    HandleColorQuery(((int)command).ToString(), arg);
                    break;

                case OscCommand.BackgroundColor:
                    HandleColorQuery(((int)command).ToString(), arg);
                    break;

                case OscCommand.CursorColor:
                    HandleColorQuery(((int)command).ToString(), arg);
                    break;

                case OscCommand.PointerShape:
                    HandlePointerShape(arg);
                    break;

                case OscCommand.Clipboard:
                    HandleClipboard(arg);
                    break;

                case OscCommand.TextSizing:
                    recognized = HandleTextSizing(arg);
                    break;

                case OscCommand.KittyNotification:
                    HandleKittyNotification(arg);
                    break;

                case OscCommand.KittyClipboard:
                    HandleKittyClipboard(arg);
                    break;

                case OscCommand.ResetColor:
                case OscCommand.ResetForeground:
                case OscCommand.ResetBackground:
                case OscCommand.ResetCursor:
                    HandleColorReset(command, arg);
                    break;

                default:
                    // Known but unhandled command
                    recognized = false;
                    System.Diagnostics.Debug.WriteLine($"Unhandled OSC command: {command}");
                    break;
            }
        }
        else
        {
            // Unknown or unsupported OSC sequence
            recognized = false;
            System.Diagnostics.Debug.WriteLine($"Unknown OSC sequence: {parts[0]}");
        }

        // Last, so a listener observes the terminal's own handling as already done rather than
        // pending. Raised for recognized sequences too: a listener that only wants the rest can say
        // so with Recognized, and stop compensating by itself once a code lands here.
        _terminal.RaiseOscReceived(
            parts[0],
            int.TryParse(parts[0], out var code) ? code : -1,
            arg,
            data,
            recognized);
    }

    /// <summary>
    /// The link in force, mirrored here from the terminal.
    /// </summary>
    /// <remarks>
    /// Fields rather than a property call, because the print path reads this for every character it
    /// writes and the answer is null for essentially all of them.
    /// </remarks>
    private string? _linkUrl;
    private string? _linkId;

    /// <summary>
    /// The most text one sized run keeps, in UTF-8 bytes — the protocol's own limit.
    /// </summary>
    /// <remarks>
    /// Measured in bytes rather than UTF-16 units on purpose: a cap counted in units cuts text a
    /// client legitimately sized its block for, since a heading in mathematical alphanumerics is two
    /// units per character and none of that is visible from the column count. The bound exists at
    /// all because the text is interned in the cluster table, which is process-wide and never
    /// released, so what a client can put there has to have a limit; the protocol's own is the one
    /// that cannot bite content that fits.
    /// </remarks>
    private const int MaxSizedRunBytes = 4096;

    /// <summary>
    /// Writes one multicell block of <paramref name="cols"/> columns holding
    /// <paramref name="content"/>.
    /// </summary>
    private void PrintSizedBlock(string content, int cols, TextSizing sizing)
    {
        if (cols <= 0)
            return;

        // A block wider than the room it could ever have -- the margin box, which is the screen when
        // no margins are set -- can never be drawn, and the protocol says to discard it rather than
        // to clip it into something the client did not ask for.
        if (cols > WrapLimit() - WrapHome() + 1)
            return;

        // A block is text like any other, so it is placed after any block already covering the
        // cursor from an earlier row rather than into the middle of one -- and then it still has to
        // fit on the row it landed on. The two settle each other, so they are asked together rather
        // than once each: skipping can leave too little room, and making room can land under another
        // block. Bounded, and a block that cannot be settled is dropped rather than written into
        // cells that belong to something else.
        var placed = false;
        for (var attempt = 0; attempt < 3 && !placed; attempt++)
        {
            if (_buffer.HasMultiRowSizedRuns && !SkipCellsCoveredFromAbove(cols))
                return;

            if (_buffer.X + cols <= WrapLimit() + 1)
            {
                placed = true;
                break;
            }

            // Too wide for what is left of the row. Wrapping the whole block is an ordinary end of
            // line, so it goes through the same path a character does and gets the margin box, the
            // scroll and the IsWrapped rule for free.
            _buffer.SetCursorRaw(WrapLimit() + 1, _buffer.Y);

            if (!ResolveAutowrap())
            {
                // With wrapping off the block is drawn where it fits, which the protocol states
                // explicitly: the cursor moves back far enough for the whole block, then it is
                // written over whatever was there.
                _buffer.SetCursorRaw(WrapLimit() + 1 - cols, _buffer.Y);
            }
        }

        if (!placed)
            return;

        var line = _buffer.Lines[_buffer.Y + _buffer.YBase];
        if (line == null)
            return;

        var column = _buffer.X;

        if (_terminal.InsertMode)
        {
            if (line.HasSizedRuns)
                line.EraseSizedRunsFrom(column);

            // In reverse, which is the correct direction for a right shift on one array: copying
            // forwards re-reads cells the same copy has already overwritten.
            line.CopyCellsFrom(line, column, column + cols, _terminal.Cols - column - cols, true);
        }

        var cell = new BufferCell
        {
            Content = content,
            Width = cols,
            Attributes = _curAttr,
        };

        line.SetCell(column, ref cell);

        // The remaining columns are zero-width continuations, exactly as the second half of a
        // double-width character is -- so everything that already understands a wide cell (search,
        // selection, reflow) understands a scaled one without being told about this protocol.
        for (var i = 1; i < cols; i++)
        {
            var spacer = BufferCell.Empty;
            spacer.Attributes = _curAttr;
            line.SetCell(column + i, ref spacer);
        }

        if (_linkUrl is not null || line.HasLinks)
            line.NoteLinkRun(column, cols, _linkUrl, _linkId);

        line.NoteSizedRun(column, cols, sizing);

        // A block taller than one row occupies the rows beneath it, which the print path has to know
        // to look for. Set on the buffer rather than counted, since it only answers "is this worth
        // looking for"; see TerminalBuffer.HasMultiRowSizedRuns.
        if (sizing.Scale > 1)
            _buffer.HasMultiRowSizedRuns = true;

        _buffer.SetCursorRaw(column + cols, _buffer.Y);

        // Deliberately NOT remembered for REP. The payload of an OSC is not a preceding graphic
        // character in the data stream, and HandleOsc has already cancelled the record for exactly
        // that reason -- restoring it here would let CSI b replay a scaled block as plain unscaled
        // cells, which is neither what was printed nor what was asked for.
    }

    private long MaxClipboardBytes => Math.Max(1, _terminal.Options.MaxClipboardBytes);
    private const int ClipboardEntryOverhead = 256;
    /// <summary>
    /// The transfer's running byte charge — maintained at every mutation rather than recomputed,
    /// because the property is consulted once per wdata packet and a rescan of every accumulated
    /// value, buffer, key and alias made parsing quadratic: hundreds of thousands of tiny MIME
    /// entries under the default cap could freeze the terminal before any limit fired.
    /// </summary>
    private long _kittyClipboardTransferSize;
    private long _kittyClipboardDecodedBytes;
    private long TransferSize => _kittyClipboardTransferSize;

    /// <summary>The row a cursor moving UP stops at: the region's top when it starts inside.</summary>
    private int TopLimit() => InsideScrollRegion() ? _buffer.ScrollTop : 0;

    /// <summary>The row a cursor moving DOWN stops at: the region's bottom when it starts inside.</summary>
    private int BottomLimit() => InsideScrollRegion() ? _buffer.ScrollBottom : _terminal.Rows - 1;

    /// <summary>
    /// REP (<c>CSI Pn b</c>) — repeat the preceding graphic character <c>Pn</c> times.
    /// </summary>
    /// <remarks>
    /// <para>Some programs use it to compress runs of one character, so a terminal without it draws
    /// a single character where a line of them belongs.</para>
    /// <para>"Preceding" is meant literally: it repeats the last character printed, and only while
    /// the cursor is still where printing it left the cursor. Anything that moved the cursor in
    /// between — a control character, a cursor sequence, a scroll — means there is no preceding
    /// character any more and this does nothing, which is what xterm does and is the only reading
    /// that does not quietly invent a character out of whatever happens to be nearby.</para>
    /// <para>The count is clamped to one screenful. It arrives from the hosted program, so
    /// <c>CSI 2000000000 b</c> is a hang otherwise; and past a screenful the result is
    /// indistinguishable anyway, since every repeat beyond that scrolls the earlier ones away.</para>
    /// </remarks>
    private void RepeatPrecedingCharacter(Params parameters)
    {
        if (_lastPrinted is not { } last)
            return;

        if (last.Row != _buffer.Y + _buffer.YBase || last.CursorCol != _buffer.X)
            return;

        // Math.Max as every other count in this file does it: a literal zero means one.
        var requested = Math.Max(parameters.GetParam(0, 1), 1);
        var count = Math.Min(requested, Math.Max(1, _terminal.Cols * _terminal.Rows));

        var text = last.ClusterId != ClusterTable.None
            ? ClusterTable.Get(last.ClusterId)
            : CodePointText.Get(last.CodePoint);

        if (string.IsNullOrEmpty(text))
            return;

        // Through Print rather than straight into the buffer: the repeated character wraps, scrolls
        // and takes the current attributes exactly as it did the first time, and Print keeps the
        // record above current so each repeat is itself the preceding character for the next.
        for (var i = 0; i < count; i++)
            Print(text);
    }

    /// <summary>
    /// The last column a write may land on before it wraps.
    /// </summary>
    /// <remarks>
    /// The right margin, not the screen edge — that is what makes text stay inside its pane. But
    /// only for a cursor already INSIDE the margins: a cursor parked to the right of them is not in
    /// the region at all, and wrapping it at the margin would drag it into a pane it was never in.
    /// xterm draws the same distinction, and it is the reason this is a method rather than a field.
    /// </remarks>
    private int WrapLimit()
    {
        if (_buffer.MarginsAreFullWidth)
            return _terminal.Cols - 1;

        // The right margin binds any cursor at or LEFT of it -- including one left of the left
        // margin: a print that starts outside the box still wraps when it reaches the box's right
        // edge, which is how xterm treats it and what lets text flow INTO a pane. Only a cursor
        // already beyond the right margin prints to the screen edge.
        var x = _buffer.PendingWrap ? _buffer.X - 1 : _buffer.X;
        return x <= _buffer.ScrollRight ? _buffer.ScrollRight : _terminal.Cols - 1;
    }

    /// <summary>A blank carrying only the current background, which is what BCE fills with.</summary>
    private BufferCell BlankCell()
    {
        var cell = BufferCell.Space;
        cell.Attributes = GetEraseAttributes();
        return cell;
    }

    /// <summary>
    /// Brings a cursor resting one past the last column back onto it, for the editing operations.
    /// </summary>
    /// <remarks>
    /// Printing to the end of a line leaves the cursor at ScrollRight + 1 with PendingWrap set --
    /// a position no character occupies. ICH, DCH and ECH tested that phantom column against
    /// their right-margin guard and returned without doing anything, so an editor that filled a
    /// line and then inserted a character saw nothing happen. xterm acts on the last column.
    /// </remarks>
    private void SettleForEditing()
    {
        if (_buffer.PendingWrap)
            _buffer.SetCursorRaw(Math.Max(_buffer.X - 1, 0), _buffer.Y);
    }

    /// <summary>
    /// DA -- CSI c (primary) and CSI &gt; c (secondary). Tells the program on the other end of the
    /// wire what this terminal is and what it can do.
    /// </summary>
    /// <remarks>
    /// <para>The reply is a promise, not a boast. Every attribute listed here names a sequence the
    /// program will now go ahead and send, so claiming a feature this emulator does not implement
    /// does not flatter it, it breaks it: the program emits the sequence, nothing happens, and the
    /// screen it believes it drew is not the screen that is there. A program that reads attribute
    /// 21 sets left and right margins and then draws inside them. One that reads attribute 2 pushes
    /// a print job through printer-controller mode, which an emulator that never enters that mode
    /// prints onto the screen instead.</para>
    /// <para>So the list is the intersection of the DA attribute numbers with what the code
    /// actually does, and nothing else. Deliberately absent, each checked against the tree:
    /// 1 (132 columns -- <c>TerminalMode.ColumnMode</c> is in the enum, but <c>SetCSIMode</c> has
    /// no case for it), 2 (printer -- there is no media copy command), 6 (selective erase -- no
    /// DECSCA), 9 (national replacement character sets -- <c>Charsets</c> holds only the default,
    /// the line drawing set and UK), 15 (technical characters), 21 (horizontal scrolling -- no
    /// left and right margins).</para>
    /// <para>Attribute 4, Sixel, is the one that visibly matters: libsixel, chafa, img2sixel and
    /// everything built on them read this reply, and send text art instead of pictures unless they
    /// see it. Claiming it while Sixel is switched off would be the same lie pointed the other
    /// way, so it follows the option.</para>
    /// </remarks>
    private void DeviceAttributes(string identifier, Params parameters)
    {
        // Only an absent or zero parameter is a request. A non-zero one is another terminal's
        // reply that has arrived on our input, and answering that starts a ping-pong.
        if (parameters.GetParam(0, 0) != 0)
            return;

        if (identifier.StartsWith('>'))
        {
            // Secondary DA: CSI > Pp ; Pv ; Pc c. Pp names the hardware family for the operating
            // level, on xterm's scale (0=VT100, 1=VT220, 24=VT320, 41=VT420, 64=VT520). Pv is
            // read by programs as an xterm patch level -- vim and tmux gate features on it -- so
            // it reports the xterm this emulator answers as, not the library version. Pc = 0 is
            // "no cartridge ROM".
            _terminal.RaiseDataReceived(SecondaryDeviceAttributes);
        }
        else if (identifier.Length == 1)
        {
            // Primary DA: CSI ? Pl ; ... c, from the DECSCL operating level.
            _terminal.RaiseDataReceived(PrimaryDeviceAttributes);
        }

        // Any other prefix is left unanswered. "?c" is the one that used to go wrong: it is not the
        // secondary DA, but it sets isPrivate, so it was handed the secondary reply -- the answer to
        // a question the program had not asked, while it was still waiting for the one it had.
        // Neither it nor the tertiary DA, "=c", reaches this method any more: the lookup matches the
        // whole identifier and only "c" and ">c" are listed, so both resolve to Unknown. Silence is
        // the right outcome for the tertiary regardless: it asks for a unit ID this terminal does
        // not have, and terminals without DECRPTUI say nothing.
    }

    /// <summary>
    /// The Pv field of the secondary DA reply. Programs parse this as an xterm patch level (vim
    /// wants >= 95 for cursor shaping, tmux >= 270 for many extensions), so it claims the xterm
    /// whose behaviour this emulator matches rather than the library's own version.
    /// </summary>
    private const int FirmwareVersion = 383;

    /// <summary>
    /// The primary DA reply for the current DECSCL operating level -- xterm's exact lists.
    /// Attribute 4, Sixel, is appended only when the option enables it (see the remarks on
    /// <see cref="DeviceAttributes"/>).
    /// </summary>
    private string PrimaryDeviceAttributes
    {
        get
        {
            // 1 = 132 columns, 2 = printer, 6 = selective erase, 9 = national replacement
            // charsets, 15 = technical characters, 22 = ANSI colour, 29 = ANSI text locator;
            // levels 4+ add 16 = locator port, 17 = terminal state interrogation, 18 = user
            // windows, 21 = horizontal scrolling, 28 = rectangular editing.
            var features = _terminal.ConformanceLevel >= 64
                ? "1;2;6;9;15;16;17;18;21;22;28;29"
                : "1;2;6;9;15;22;29";
            if (_terminal.Options.SixelEnabled)
                features = "4;" + features;
            return _terminal.ConformanceLevel switch
            {
                61 => "\u001b[?1;2c",
                var level => $"\u001b[?{level};{features}c",
            };
        }
    }

    /// <summary>
    /// The secondary DA reply, CSI &gt; Pp ; Pv ; Pc c, with Pp on xterm's hardware-family scale
    /// for the operating level.
    /// </summary>
    private string SecondaryDeviceAttributes => _terminal.ConformanceLevel switch
    {
        61 => $"\u001b[>0;{FirmwareVersion};0c",
        62 => $"\u001b[>1;{FirmwareVersion};0c",
        63 => $"\u001b[>24;{FirmwareVersion};0c",
        64 => $"\u001b[>41;{FirmwareVersion};0c",
        _ => $"\u001b[>64;{FirmwareVersion};0c",
    };

    /// <summary>
    /// The version XTVERSION reports, read once. It cannot change while the process runs, and the
    /// query arrives during the startup of every program that sends one, so rediscovering it
    /// through reflection each time buys nothing.
    /// </summary>
    private static readonly string _versionText = ReadVersion();

    private static string ReadVersion()
    {
        var version = typeof(InputHandler).Assembly.GetName().Version;

        // Build is -1 on a version that carries only a major and a minor part.
        return version is null
            ? "0.0.0"
            : $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";
    }

    /// <summary>
    /// XTVERSION -- CSI > Ps q. Reports the terminal's name and version.
    /// </summary>
    /// <remarks>
    /// <para>The reply is a DCS string, "DCS &gt; | text ST", in the shape xterm defined and the
    /// terminals that answer have followed: xterm sends "XTerm(370)", foot "foot(1.13.1)", kitty
    /// "kitty(0.26.5)". Programs send it to work out whether a capability they cannot otherwise
    /// detect is safe to use, so being answerable at all matters more than what stands inside the
    /// parentheses.</para>
    /// <para>Ps 0 is the only request defined, and anything else goes unanswered: a program that
    /// asked a question we do not know would otherwise read the version back as the answer to
    /// it.</para>
    /// </remarks>
    private void ReportVersion(Params parameters)
    {
        if (parameters.GetParam(0, 0) != 0)
            return;

        _terminal.RaiseDataReceived($"\u001bP>|XTerm.NET({_versionText})\u001b\\");
    }

    /// <summary>
    /// The underline style from SGR 4, which may carry it as a sub-parameter: <c>4:3</c> is curly.
    /// </summary>
    /// <remarks>
    /// Plain <c>SGR 4</c> with no sub-parameter is a single underline, which is what it has always
    /// meant. The sub-parameters were already being parsed and then discarded, so a program asking
    /// for a curly underline — which is how an LSP marks an error — got a straight one.
    /// </remarks>
    private static UnderlineStyle ReadUnderlineStyle(Params parameters, int index)
    {
        var sub = parameters.GetSubParams(index);
        if (sub is null || sub.Count == 0)
            return UnderlineStyle.Single;

        return sub[0] switch
        {
            0 => UnderlineStyle.None,
            1 => UnderlineStyle.Single,
            2 => UnderlineStyle.Double,
            3 => UnderlineStyle.Curly,
            4 => UnderlineStyle.Dotted,
            5 => UnderlineStyle.Dashed,

            // An unknown style is still an underline. Drawing a plain one is closer to what the
            // program asked for than drawing nothing.
            _ => UnderlineStyle.Single,
        };
    }

    /// <summary>
    /// SGR 58 — the underline's own colour, in the same forms as 38 and 48.
    /// </summary>
    /// <remarks>
    /// Accepts the colour as sub-parameters (<c>58:2::r:g:b</c>) as well as separate parameters
    /// (<c>58;2;r;g;b</c>), because both are in use and a terminal that takes only one of them looks
    /// broken to half its callers.
    /// </remarks>
    private int HandleUnderlineColor(Params parameters, int index)
    {
        var sub = parameters.GetSubParams(index);

        if (sub is { Count: > 0 })
        {
            // 58:2::r:g:b — the empty slot is a colour space id nobody uses.
            if (sub[0] == 2 && sub.Count >= 4)
            {
                var offset = sub.Count >= 5 ? 2 : 1;
                var rgb = (sub[offset] << 16) | (sub[offset + 1] << 8) | sub[offset + 2];
                _curAttr.SetUnderlineColor(rgb, 1);
                return index;
            }

            // 58:5:n
            if (sub[0] == 5 && sub.Count >= 2)
            {
                _curAttr.SetUnderlineColor(sub[1], 0);
                return index;
            }

            return index;
        }

        if (index + 1 >= parameters.Length)
            return index;

        var kind = parameters.GetParam(index + 1, 0);

        if (kind == 2 && index + 4 < parameters.Length)
        {
            var rgb = (parameters.GetParam(index + 2, 0) << 16)
                      | (parameters.GetParam(index + 3, 0) << 8)
                      | parameters.GetParam(index + 4, 0);

            _curAttr.SetUnderlineColor(rgb, 1);
            return index + 4;
        }

        if (kind == 5 && index + 2 < parameters.Length)
        {
            _curAttr.SetUnderlineColor(parameters.GetParam(index + 2, 0), 0);
            return index + 2;
        }

        return index;
    }

    /// <summary>Applies a colour from SGR 38 or 48 to whichever side asked for it.</summary>
    private void SetExtendedColor(int color, int mode, bool isForeground)
    {
        if (isForeground)
            _curAttr.SetFgColor(color, mode);
        else
            _curAttr.SetBgColor(color, mode);
    }

    /// <summary>
    /// SGR 38 and 48 — a foreground or background colour beyond the sixteen, either as a 256-palette
    /// index or as direct RGB.
    /// </summary>
    /// <remarks>
    /// Accepts the colour as sub-parameters (<c>38:2::r:g:b</c>) as well as separate parameters
    /// (<c>38;2;r;g;b</c>), for the reason SGR 58 already does: both forms are in use, and taking
    /// only one of them looks broken to half the callers. The colon form was already reaching the
    /// parser, which collects it as sub-parameters, and then being dropped here — so a program that
    /// asked for truecolor that way got no colour at all.
    /// </remarks>
    private int HandleExtendedColor(Params parameters, int index, bool isForeground)
    {
        var sub = parameters.GetSubParams(index);

        if (sub is { Count: > 0 })
        {
            // 38:2::r:g:b — the empty slot is a colour space id nobody uses, and some programs
            // leave it out entirely, so the run's length says where red starts.
            if (sub[0] == 2 && sub.Count >= 4)
            {
                var offset = sub.Count >= 5 ? 2 : 1;
                var rgb = (sub[offset] << 16) | (sub[offset + 1] << 8) | sub[offset + 2];
                SetExtendedColor(rgb, 1, isForeground);
            }
            else if (sub[0] == 5 && sub.Count >= 2)
            {
                SetExtendedColor(sub[1], 0, isForeground);
            }

            // Sub-parameters belong to this parameter, so no later one was consumed.
            return index;
        }

        if (index + 1 >= parameters.Length)
            return index;

        var colorType = parameters.GetParam(index + 1, 0);

        if (colorType == 2 && index + 4 < parameters.Length) // RGB
        {
            var r = parameters.GetParam(index + 2, 0);
            var g = parameters.GetParam(index + 3, 0);
            var b = parameters.GetParam(index + 4, 0);

            SetExtendedColor((r << 16) | (g << 8) | b, 1, isForeground);
            return index + 4;
        }
        else if (colorType == 5 && index + 2 < parameters.Length) // 256 color
        {
            SetExtendedColor(parameters.GetParam(index + 2, 0), 0, isForeground);
            return index + 2;
        }

        return index;
    }

    private void SetCSIMode(int mode, bool isPrivate)
    {
        if (isPrivate)
        {
            // DEC Private Modes (DECSET)
            // Convert int to TerminalMode enum
            if (!Enum.IsDefined(typeof(TerminalMode), mode))
            {
                if (!TrySetStoredMode(mode, isPrivate: true, value: true))
                    System.Diagnostics.Debug.WriteLine($"Unknown CSI private terminal mode: {mode}");
                return;
            }

            var terminalMode = (TerminalMode)mode;

            switch (terminalMode)
            {
                case TerminalMode.AppCursorKeys:
                    _terminal.ApplicationCursorKeys = true;
                    break;

                case TerminalMode.InsertMode:
                    // Mode 4 in the PRIVATE space is DECSCLM (smooth scroll); it shares the enum
                    // value with IRM. Stored so DECRQM answers truthfully; nothing acts on it.
                    TrySetStoredMode(mode, isPrivate: true, value: true);
                    break;

                case TerminalMode.ColumnMode:
                    // Gated on Allow80To132 (mode 40), exactly as xterm gates it: a program that
                    // never asked for resizes does not get one from a stray CSI ? 3 h.
                    if (_storedDecModes.TryGetValue(40, out var allowed) && allowed)
                        _terminal.SetColumnMode(wide: true);
                    break;

                case TerminalMode.ReverseVideo:
                    _terminal.ReverseVideo = true;
                    break;

                case TerminalMode.Origin:
                    _terminal.OriginMode = true;
                    MoveCursorToHome();
                    break;

                case TerminalMode.LeftRightMargin:
                    _terminal.LeftRightMarginMode = true;
                    break;

                case TerminalMode.Wraparound:
                    // Mode 7: Wraparound mode
                    // Wraparound and AutoWrapMode share value 7 in the enum
                    _terminal.Options.Wraparound = true;
                    break;

                case TerminalMode.AutoRepeat:
                    // Stored so DECRQM can answer truthfully; nothing acts on it.
                    TrySetStoredMode(mode, isPrivate: true, value: true);
                    break;

                case TerminalMode.ShowCursor:
                    _terminal.CursorVisible = true;
                    break;

                case TerminalMode.NoClearOnColumnChange:
                    _terminal.NoClearOnColumnChange = true;
                    break;

                case TerminalMode.NationalCharset:
                    TrySetStoredMode(mode, isPrivate: true, value: true);
                    _terminal.NationalReplacementCharsets = true;
                    RefreshDesignatedCharsets();
                    break;

                case TerminalMode.ReverseWraparound:
                    _terminal.ReverseWraparound = true;
                    break;

                case TerminalMode.ReverseWraparoundExtended:
                    _terminal.ReverseWraparoundExtended = true;
                    break;

                case TerminalMode.AppKeypad:
                    _terminal.ApplicationKeypad = true;
                    break;

                case TerminalMode.SynchronizedOutput:
                    _terminal.RaiseSynchronizedOutputChanged(true);
                    break;

                case TerminalMode.InBandResize:
                    _terminal.InBandResize = true;
                    // The first report is mandatory, and it is what makes the mode worth setting:
                    // the application learns the size the moment it asks to be kept informed,
                    // instead of enabling this and then waiting for a resize that may never come.
                    _terminal.SendInBandResizeReport();
                    break;

                case TerminalMode.BracketedPasteMode:
                    _terminal.BracketedPasteMode = true;
                    break;

                case TerminalMode.PasteNotification:
                    _terminal.PasteNotificationMode = true;
                    break;

                case TerminalMode.AltBuffer:
                    _terminal.SwitchToAltBuffer();
                    break;

                case TerminalMode.SaveCursorMode:
                    // 1048 is DECSC wearing a mode's clothes; 1049 exists because two programs
                    // in a row wanted this and the alt-buffer switch as one sequence.
                    SaveCursor();
                    break;

                case TerminalMode.AltBufferCursor:
                    // 1047 switches WITHOUT saving the cursor -- the cursor is shared. Saving is
                    // what 1048 is for, and doing both at once is what 1049 is for.
                    _terminal.SwitchToAltBuffer();
                    break;

                case TerminalMode.AltBufferFull:
                    SaveCursor();
                    _terminal.SwitchToAltBuffer();
                    _buffer.SetCursor(0, 0);
                    EraseInDisplay(new Params()); // Clear screen
                    break;

                case TerminalMode.SendFocusEvents:
                    _terminal.SendFocusEvents = true;
                    _terminal.GetMouseTracker().FocusEvents = true;
                    break;

                case TerminalMode.MouseReportClick:
                    _terminal.GetMouseTracker().TrackingMode = MouseTrackingMode.X10;
                    break;

                case TerminalMode.MouseReportNormal:
                    _terminal.GetMouseTracker().TrackingMode = MouseTrackingMode.VT200;
                    break;

                case TerminalMode.MouseReportButtonEvent:
                    _terminal.GetMouseTracker().TrackingMode = MouseTrackingMode.ButtonEvent;
                    break;

                case TerminalMode.MouseReportAnyEvent:
                    _terminal.GetMouseTracker().TrackingMode = MouseTrackingMode.AnyEvent;
                    break;

                case TerminalMode.MouseReportUtf8:
                    _terminal.GetMouseTracker().Encoding = MouseEncoding.Utf8;
                    break;

                case TerminalMode.MouseReportSgr:
                    _terminal.GetMouseTracker().Encoding = MouseEncoding.SGR;
                    break;

                case TerminalMode.MouseReportUrxvt:
                    _terminal.GetMouseTracker().Encoding = MouseEncoding.URXVT;
                    break;

                case TerminalMode.EightBitInput:
                    _terminal.EightBitInput = true;
                    break;

                case TerminalMode.NumLock:
                    // NumLock modifier handling - acknowledge but no specific action needed
                    break;

                case TerminalMode.MetaSendsEscape:
                    System.Diagnostics.Debug.WriteLine($">>> Mode {mode} MetaSendsEscape ENABLED (disabling Win32InputMode)");
                    _terminal.MetaSendsEscape = true;
                    // MetaSendsEscape is incompatible with Win32InputMode for Alt key handling
                    // When explicitly requesting ESC+char for meta keys, disable Win32 input
                    _terminal.Win32InputMode = false;
                    break;

                case TerminalMode.AltSendsEscape:
                    System.Diagnostics.Debug.WriteLine($">>> Mode {mode} AltSendsEscape ENABLED (disabling Win32InputMode)");
                    _terminal.AltSendsEscape = true;
                    // AltSendsEscape is incompatible with Win32InputMode for Alt key handling
                    // When explicitly requesting ESC+char for Alt keys, disable Win32 input
                    _terminal.Win32InputMode = false;
                    break;

                case TerminalMode.SixelDisplayMode:
                    _terminal.SixelDisplayMode = true;
                    break;

                case TerminalMode.SixelPrivateColorRegisters:
                    _terminal.SixelPrivateColorRegisters = true;
                    break;

                case TerminalMode.SixelCursorRight:
                    _terminal.SixelCursorRight = true;
                    break;

                case TerminalMode.Win32InputMode:
                    System.Diagnostics.Debug.WriteLine($">>> Mode {mode} Win32InputMode ENABLED (disabling MetaSendsEscape and AltSendsEscape)");
                    _terminal.Win32InputMode = true;
                    // Win32InputMode is incompatible with MetaSendsEscape/AltSendsEscape
                    // When enabling Win32 input mode, disable ESC+char modes
                    _terminal.MetaSendsEscape = false;
                    _terminal.AltSendsEscape = false;
                    break;

                default:
                    if (!TrySetStoredMode(mode, isPrivate: true, value: true))
                        System.Diagnostics.Debug.WriteLine($"Unhandled CSI private terminal mode: {terminalMode}");
                    break;
            }
        }
        else
        {
            // ANSI Modes (SM)
            if (!Enum.IsDefined(typeof(TerminalMode), mode))
            {
                if (!TrySetStoredMode(mode, isPrivate: false, value: true))
                    System.Diagnostics.Debug.WriteLine($"Unknown CSI terminal mode: {mode}");
                return;
            }

            var terminalMode = (TerminalMode)mode;

            switch (terminalMode)
            {
                case TerminalMode.InsertMode:
                    _terminal.InsertMode = true;
                    break;

                case TerminalMode.AutoWrapMode:
                    _terminal.Options.Wraparound = true;
                    break;

                default:
                    if (!TrySetStoredMode(mode, isPrivate: false, value: true))
                        System.Diagnostics.Debug.WriteLine($"Unhandled CSI terminal mode: {terminalMode}");
                    break;
            }
        }
    }

    private void ResetTerminal()
    {
        _terminal.Reset();
    }

    // Utility Methods

    private int GetStringCellWidth(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        // Fast path for a single BMP character, which covers the overwhelming majority of terminal
        // output -- ASCII, CJK, kana, hangul, box drawing.
        //
        // For ONE codepoint the adjustment tree below collapses, and it is worth being precise about
        // why rather than trusting the shape of it:
        //
        //   - skin-tone modifiers and regional indicators live above the BMP, so a single UTF-16 code
        //     unit cannot be either;
        //   - the keycap and variation-selector branches are guarded on lastWidth being 1 or 2, and
        //     lastWidth is still 0 on the first rune, so they fall through to the plain case;
        //   - ZWJ is zero-width in its own right, so the loop yields 0 for it either way;
        //   - U+FFFC alone joins nothing, and the loop adds its own width rather than subtracting a
        //     lastWidth of 0. Returning 0 here left it measuring 0 again -- it never moved the cursor,
        //     so the next character printed over the top of it, which is the bug the loop already fixed.
        //
        // Which leaves: plain width for everything except ZWJ, and the control-character handling
        // for negative widths.
        if (text.Length == 1)
        {
            var c = text[0];

            if (c >= 0x20 && c < 0x7F)
                return 1;

            if (!char.IsSurrogate(c))
            {
                if (c == ZeroWidthJoiner)
                    return 0;

                var w = CellWidth.Get(c);
                if (w >= 0)
                    return w;

                // Control characters, matching the tail of the loop below.
                if (c == '\t') return 4;
                if (c == '\n') return 1;
                return 0;
            }
        }

        bool supportsComplexEmoji = true;
        ushort width = 0;
        ushort lastWidth = 0;
        int regionalRuneCount = 0;
        foreach (Rune rune in text.EnumerateRunes())
        {
            int runeWidth = CellWidth.Get(rune.Value);   // memoised; the library call is ~23 ns
            if (runeWidth >= 0)
            {
                if (rune.Value == ZeroWidthJoiner || rune.Value == ObjectReplacementCharacter)
                {
                    if (!supportsComplexEmoji)
                        // we return the first emoji as the result because terminal doesn't support chaining them
                        break;

                    // Only a WIDE glyph's join un-counts what came before: an emoji family is one
                    // two-column image however many members it has. A ZWJ between one-column
                    // letters is an Indic explicit conjunct (ta+virama+ZWJ+pa), where wcwidth
                    // arithmetic keeps every letter's column -- subtracting made those measure 1.
                    if (lastWidth == 2)
                        // It joins the glyph before it, which has already been counted.
                        width -= lastWidth;
                    else
                        // Nothing in front of it to join, so it stands on its own. Subtracting unconditionally
                        // left a lone U+FFFC measuring 0, and a character measuring 0 does not move the
                        // cursor — so whatever came next printed over the top of it. ZWJ passes through here
                        // too and is unaffected, being genuinely zero-width in its own right.
                        width += (ushort)runeWidth;
                }
                else if (rune.Value == VariationSelectorEmojiSymbol &&
                         lastWidth == 1)
                {
                    // adjust for the emoji presentation, which is width 2
                    width++;
                    lastWidth = 2;
                }
                else if (rune.Value == VariationSelectorTextSymbol &&
                         lastWidth == 2)
                {
                    // adjust for the text presentation, which is width 1
                    width--;
                    lastWidth = 1;
                }
                else if (rune.Value < 0x1F000     // no Mc exists in or above the emoji blocks
                         && System.Globalization.CharUnicodeInfo.GetUnicodeCategory(rune.Value)
                         == System.Globalization.UnicodeCategory.SpacingCombiningMark
                         && !IsConjunctLinker(rune.Value))
                {
                    // SPACING combining marks -- Indic matras -- occupy a column of their own:
                    // wcwidth has always said 1 for Mc, and every wcwidth-consuming application
                    // lays text out on that arithmetic. The cluster stays one cell; the cell
                    // grows. This knowingly diverges from Terminal Unicode Core's "extending a
                    // cluster will not move the cursor" -- as does kitty, for the same reason.
                    width += 1;
                }
                else if (rune.Value >= 0xE0020 && rune.Value <= 0xE007F)
                {
                    // TAG characters (and CANCEL TAG): format characters that spell out an emoji
                    // tag sequence — the subdivision flags. They occupy no columns whether or not
                    // anything precedes them; the flag they decorate has already been counted.
                    // Counting them at their table width made 🏴gbsct eight columns wide, and the
                    // answer must not depend on which Wcwidth version a host resolves.
                }
                else if (lastWidth > 0 &&
                         (rune.Value >= SkinToneFirst && rune.Value <= SkinToneLast ||
                          rune.Value == KeycapCombiner))
                {
                    // Emoji modifier (skin tone) or keycap extender should continue current glyph

                    // else: combining � ignore
                }
                else if (rune.Value >= SkinToneFirst && rune.Value <= SkinToneLast)
                {
                    // A skin tone with nothing in front of it to modify. Unicode gives these East Asian
                    // Width W, and every other terminal draws a lone one as a two-column swatch — so that is
                    // what it occupies. wcwidth answers 0 because it assumes the modifier is attached to
                    // something, and 0 meant the cursor never moved and the next character printed over the
                    // top of it: "🏽X" left an X and no swatch.
                    width += 2;
                    lastWidth = 2;
                }
                // Regional indicator symbols. These carry emoji presentation, so ONE is two columns wide and
                // a PAIR is the flag they make — also two. So the width is added on the first of a pair and
                // the second joins it rather than adding again.
                //
                // The parity used to be the other way round: width was added on the SECOND, so a single
                // indicator measured 0. This method is called once per printed character and the two halves
                // of a flag arrive separately, so the count was always 1, always odd, and the answer always
                // zero. Width 0 leaves the cursor standing still, and the next character then overwrote the
                // indicator — which is why a flag vanished from the buffer rather than merely rendering
                // oddly. Joining the two is Print's job, where state survives the call.
                else if (rune.Value >= 0x1F1E6 && rune.Value <= 0x1F1FF)
                {
                    regionalRuneCount++;
                    if (regionalRuneCount % 2 == 1)
                        width += 2;

                    lastWidth = 2;
                }
                else
                {
                    width += (ushort)runeWidth;
                }


                if (runeWidth > 0) lastWidth = (ushort)runeWidth;
            }
            // Control chars return as width < 0
            else
            {
                if (rune.Value == 0x9 /* tab */)
                {
                    // Avalonia uses hard coded 4 spaces for tabs (NOT column based tabstops), this may change in the future
                    width += 4;
                    lastWidth = 4;
                }
                else if (rune.Value == '\n')
                {
                    width += 1;
                    lastWidth = 1;
                }
            }
        }

        // Clamped for the same reason TryAppendToPreviousCell clamps its incremental answer: a
        // grid cell is one or two columns and nothing else, so a cluster with two spacing marks
        // (or a double conjunct) measuring 3 gave the two paths different answers about the same
        // text and put a width the cell machinery has never seen into the buffer.
        return Math.Min(width, (ushort)2);
    }

    public void SetBuffer(Buffer.TerminalBuffer buffer)
    {
        _buffer = buffer;
        _buffer.EraseAttributesProvider = GetEraseAttributes;
    }

}
