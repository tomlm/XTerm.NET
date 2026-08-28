using NeoSmart.Unicode;
using System.Text;
using Wcwidth;
using XTerm.Buffer;
using XTerm.Common;
using XTerm.Input;
using XTerm.Parser;

namespace XTerm;

/// <summary>
/// Handles input escape sequences and updates the terminal buffer.
/// Implements VT100/xterm escape sequence handlers.
/// </summary>
public class InputHandler
{
    private readonly Terminal _terminal;
    private Buffer.TerminalBuffer _buffer;
    private AttributeData _curAttr;
    private readonly Dictionary<CharsetMode, Dictionary<char, string>?> _charsets;
    private readonly Dictionary<string, KittyNotification> _kittyNotifications = new();
    private const int MaxPendingKittyNotifications = 16;
    private const int MaxKittyNotificationBytes = 64 * 1024;
    private static readonly TimeSpan KittyNotificationTimeout = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The table _currentCharset resolves to, cached.
    ///
    /// Print looked this up in the dictionary once per printed character to answer a question whose
    /// answer only changes on SO, SI, a charset designation or a reset -- events that happen orders
    /// of magnitude less often than printing does. Every write to _charsets or _currentCharset goes
    /// through RefreshActiveCharset so the two cannot drift.
    /// </summary>
    private Dictionary<char, string>? _activeCharset;
    private CharsetMode _currentCharset;

    // Variation selector and combining character constants
    private const int VariationSelectorEmojiSymbol = 0xFE0F;  // Emoji presentation selector
    private const int VariationSelectorTextSymbol = 0xFE0E;   // Text presentation selector
    private const int ZeroWidthJoiner = 0x200D;               // ZWJ for emoji sequences

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

    /// <summary>The regional indicator symbols, U+1F1E6 to U+1F1FF. Two of them make one flag.</summary>
    private static bool IsRegionalIndicator(int codePoint)
        => codePoint >= 0x1F1E6 && codePoint <= 0x1F1FF;

    public InputHandler(Terminal terminal)
    {
        _terminal = terminal;
        _buffer = terminal.Buffer;
        _curAttr = AttributeData.Default;

        // Initialize charset tables - all start as ASCII
        _charsets = new Dictionary<CharsetMode, Dictionary<char, string>?>
        {
            { CharsetMode.G0, Charsets.ASCII },
            { CharsetMode.G1, Charsets.ASCII },
            { CharsetMode.G2, Charsets.ASCII },
            { CharsetMode.G3, Charsets.ASCII }
        };

        _currentCharset = CharsetMode.G0; // G0 is active by default
        RefreshActiveCharset();
    }

    private void RefreshActiveCharset() => _activeCharset = _charsets.GetValueOrDefault(_currentCharset);

    /// <summary>
    /// Checks if a code point is a combining character that should be merged with the previous cell.
    /// </summary>
    private static bool IsCombiningCharacter(int codePoint)
    {
        // Variation Selectors (U+FE00�U+FE0F)
        if (codePoint >= 0xFE00 && codePoint <= 0xFE0F)
            return true;

        // Variation Selectors Supplement (U+E0100�U+E01EF)
        if (codePoint >= 0xE0100 && codePoint <= 0xE01EF)
            return true;

        // Zero Width Joiner (U+200D)
        if (codePoint == ZeroWidthJoiner)
            return true;

        // Combining Diacritical Marks (U+0300�U+036F)
        if (codePoint >= 0x0300 && codePoint <= 0x036F)
            return true;

        // Combining Diacritical Marks Extended (U+1AB0�U+1AFF)
        if (codePoint >= 0x1AB0 && codePoint <= 0x1AFF)
            return true;

        // Combining Diacritical Marks Supplement (U+1DC0�U+1DFF)
        if (codePoint >= 0x1DC0 && codePoint <= 0x1DFF)
            return true;

        // Combining Diacritical Marks for Symbols (U+20D0�U+20FF)
        if (codePoint >= 0x20D0 && codePoint <= 0x20FF)
            return true;

        // Combining Half Marks (U+FE20�U+FE2F)
        if (codePoint >= 0xFE20 && codePoint <= 0xFE2F)
            return true;

        // Emoji Modifiers / Skin Tones (U+1F3FB..U+1F3FF)
        //
        // Combining is not decided here alone: a skin tone modifies an EMOJI, and TryAppendToPreviousCell
        // checks what it is being asked to attach to. Saying yes unconditionally glued a modifier onto
        // whatever happened to precede it — "║🏼║" put the tone inside the box-drawing character and drew
        // the pair as one unreadable cell, where every other terminal shows a swatch standing on its own.
        if (IsSkinToneModifier(codePoint))
            return true;

        // Keycap combining sequence (U+20E3)
        if (codePoint == 0x20E3)
            return true;

        return false;
    }

    /// <summary>The Fitzpatrick skin tone modifiers, U+1F3FB to U+1F3FF.</summary>
    private static bool IsSkinToneModifier(int codePoint)
        => codePoint >= 0x1F3FB && codePoint <= 0x1F3FF;

    /// <summary>
    /// The last code point in a cell's content — the one a modifier would actually be attaching to, since a
    /// cell may already hold a whole cluster.
    /// </summary>
    private static int LastRuneOf(string? content)
    {
        if (string.IsNullOrEmpty(content))
            return 0;

        int last = 0;
        foreach (var rune in content.EnumerateRunes())
            last = rune.Value;

        return last;
    }

    /// <summary>
    /// Whether <paramref name="codePoint"/> is something a skin tone can actually modify.
    /// </summary>
    /// <remarks>
    /// Deliberately broader than Unicode's Emoji_Modifier_Base list, which runs to some thirty ranges and
    /// would have to be revised every release. Everything on it is an emoji, so "is this an emoji" rejects
    /// the case that matters — a letter, a box-drawing character, a CJK ideograph — while letting through a
    /// handful of emoji that take no modifier. Those render as whatever the font makes of them, which is
    /// what the program asked for; the alternative is a table that silently rots.
    /// </remarks>
    private static bool CanTakeSkinTone(int codePoint)
        => codePoint >= 0x1F000
           || codePoint == 0x261D || codePoint == 0x26F9
           || (codePoint >= 0x270A && codePoint <= 0x270D);

    /// <summary>
    /// Prints a character to the buffer.
    /// </summary>
    public void Print(string data)
    {
        // Decoded once and reused below. Print used to call ConvertToUtf32 on the same string twice:
        // here, and again when filling in the cell's CodePoint.
        var codePoint = data.Length > 0 ? char.ConvertToUtf32(data, 0) : 0;

        if (data.Length > 0)
        {
            // A placeholder is a character that means "part of a picture goes here". It has to be
            // taken before the combining-character machinery below, which would otherwise try to
            // merge the diacritics that follow it into a text cell.
            if (codePoint == KittyPlaceholder && TryPrintKittyPlaceholder())
                return;

            // The combining marks that state a placeholder's tile explicitly. They must be taken
            // here too: left to the machinery below they would be appended to the image cell as
            // text, and left to nothing at all they would print as visible marks of their own.
            // Guarded at the CALL rather than inside, which is not a style preference: the method
            // is too big to inline, so without this every printed character pays a real call to be
            // told there is no placeholder. Measured at 12% of the alt-redraw corpus.
            if (_placeholderCell is not null && TryApplyPlaceholderDiacritic(codePoint))
                return;

            // A character standing exactly where a ZWJ was just merged continues that cluster.
            var continuesCluster = _zwjContinuation is { } pending
                                   && pending.Row == _buffer.Y + _buffer.YBase
                                   && pending.Col == _buffer.X;
            _zwjContinuation = null;

            // A second regional indicator lands beside the first and turns it into a flag: one glyph, two
            // columns. Handled here rather than through the combining path because it does not merely append
            // — the cell it joins has to GROW to the width the pair occupies, and gain the placeholder that
            // every wide cell carries.
            if (IsRegionalIndicator(codePoint)
                && _regionalPending is { } flag
                && flag.Row == _buffer.Y + _buffer.YBase
                && flag.Cursor == _buffer.X
                && TryPairRegionalIndicator(data, flag.Cell))
            {
                return;
            }

            // Any other character breaks the pair. A lone indicator is a perfectly good character — it
            // renders as a letter in a box — so it simply stops being the first half of anything.
            _regionalPending = null;

            if (continuesCluster || IsCombiningCharacter(codePoint))
            {
                // Find the previous cell to combine with
                if (TryAppendToPreviousCell(data, codePoint))
                {
                    // A ZWJ promises another component after it; remember where, so it can be recognised.
                    if (codePoint == ZeroWidthJoiner)
                        _zwjContinuation = (_buffer.Y + _buffer.YBase, _buffer.X);

                    return; // Successfully combined, don't create new cell
                }
                // If we can't combine (e.g., at start of line), fall through to normal handling
            }
        }


        // Handle autowrap
        if (_buffer.X >= _terminal.Cols)
        {
            if (_terminal.Options.Wraparound)
            {
                if (_buffer.Y == _buffer.ScrollBottom)
                {
                    _buffer.SetCursor(0, _buffer.Y);
                    _buffer.ScrollUp(1, true);
                }
                else
                {
                    _buffer.SetCursor(0, _buffer.Y + 1);
                }
                _buffer.Lines[_buffer.Y + _buffer.YBase]!.IsWrapped = true;
            }
            else
            {
                return; // Don't print beyond line edge
            }
        }

        var line = _buffer.Lines[_buffer.Y + _buffer.YBase]; 
        if (line == null)
            return;

        // Translate character through active charset
        var translatedData = data;
        if (data.Length == 1)
        {
            translatedData = Charsets.TranslateChar(data[0], _activeCharset);
        }

        // Get character width
        var width = GetStringCellWidth(translatedData);

        // Create cell
        var cell = new BufferCell
        {
            Content = translatedData,
            Width = width,
            Attributes = _curAttr,
            // Reuse the codepoint decoded at the top unless translation actually produced a
            // different string. With no charset mapping in play, TranslateChar hands back the very
            // same cached instance it was given, so the common path never decodes twice.
            CodePoint = ReferenceEquals(translatedData, data)
                ? codePoint
                : (translatedData.Length > 0 ? char.ConvertToUtf32(translatedData, 0) : 0)
        };

        // Insert mode handling
        if (_terminal.InsertMode)
        {
            // Shift cells right
            line?.CopyCellsFrom(line, _buffer.X, _buffer.X + width, _terminal.Cols - _buffer.X - width, false);
        }

        // Printing over a picture needs no special case here any more. SetCell splits a SIXEL run
        // around the written column, because a Sixel is content and printing replaces that part of
        // it; a Kitty run is left alone, because it is an overlay whose z-index orders it against
        // the text. Both fall out of where a picture is stored rather than from anything done here.

        // Set the cell
        line?.SetCell(_buffer.X, ref cell);

        // Handle wide characters
        if (width == 2)
        {
            // Set following cell as a spacer
            if (_buffer.X + 1 < _terminal.Cols)
            {
                var spacer = BufferCell.Empty;
                spacer.Attributes = _curAttr;
                line?.SetCell(_buffer.X + 1, ref spacer);
            }
        }

        // A lone regional indicator may turn out to be the first half of a flag. Remember where it went and
        // where it left the cursor, so the next one can recognise itself as the second half.
        if (cell.CodePoint is var cp && IsRegionalIndicator(cp))
            _regionalPending = (_buffer.Y + _buffer.YBase, _buffer.X, _buffer.X + width);

        // Guarded at the CALL, not only inside a helper: this runs per printed character, the
        // helper is not reliably inlined, and the same unguarded-call shape cost alt-redraw 12%
        // once already. The common case -- no link in force, none on the line -- pays two reads.
        if (line is not null && (_linkUrl is not null || line.HasLinks))
            line.NoteLinkRun(_buffer.X, width, _linkUrl, _linkId);

        // Use MoveCursor to allow X to be one past the last column (pending wrap)
        _buffer.SetCursorRaw(_buffer.X + width, _buffer.Y);

        RememberForRepeat(cell.CodePoint, cell.ClusterId);
    }

    /// <summary>
    /// Records the character just printed, and where it left the cursor, for <c>REP</c>.
    /// </summary>
    /// <remarks>
    /// <para>Two things guard the record, and they catch different intrusions. Any control, CSI,
    /// ESC, OSC or DCS between the print and the REP FORGETS it, cleared at the dispatch points
    /// rather than from every handler -- which is how xterm.js does it (its parser zeroes
    /// <c>precedingJoinState</c> after every dispatch), and is the version that does not rot,
    /// because a new handler cannot forget to cancel what the dispatcher already cancelled.
    /// That is what catches a cursor moved away and back: the position afterwards is the same,
    /// but a sequence intervened, so there is no preceding character any more.</para>
    /// <para>The stored position is the second guard, for movement that arrives through no
    /// sequence at all -- the host calling a public cursor API between writes.</para>
    /// <para>REP itself is exempt from the clearing, so a chain of REPs keeps repeating: after a
    /// REP the character it printed IS the preceding graphic character (ECMA-48's reading).
    /// xterm.js happens to cancel there, but only because its parser clears after the handler
    /// returns; nothing in the spec asks for that.</para>
    /// </remarks>
    private void RememberForRepeat(int codePoint, int clusterId)
    {
        _lastPrinted = (_buffer.Y + _buffer.YBase, _buffer.X, codePoint, clusterId);
    }

    /// <summary>Forgets the preceding character. See <see cref="RememberForRepeat"/> for when.</summary>
    internal void CancelRepeat() => _lastPrinted = null;

    /// <summary>The character last printed, and the cursor position it left behind. See <see cref="RememberForRepeat"/>.</summary>
    private (int Row, int CursorCol, int CodePoint, int ClusterId)? _lastPrinted;

    /// <summary>
    /// Joins a second regional indicator to the one already at <paramref name="cellX"/>, making the pair a
    /// single double-width flag.
    /// </summary>
    /// <remarks>
    /// <para>The first indicator was printed as an ordinary single-width character, because at the time it
    /// was one — nothing says a flag is coming, and a lone indicator is a valid character that renders as a
    /// letter in a box. So this widens the cell it already wrote rather than laying a new one down.</para>
    /// <para>Returns false rather than half-doing it if the pair will not fit, which leaves the caller to
    /// print the second indicator on its own. Two boxed letters at the edge of the screen is a better answer
    /// than a wide cell hanging off it.</para>
    /// </remarks>
    private bool TryPairRegionalIndicator(string data, int cellX)
    {
        var line = _buffer.Lines[_buffer.Y + _buffer.YBase];
        if (line == null || cellX < 0 || cellX >= _terminal.Cols)
            return false;

        // The flag needs the column after it for the placeholder every wide cell carries.
        if (cellX + 1 >= _terminal.Cols)
            return false;

        var first = line[cellX];
        if (!IsRegionalIndicator(first.CodePoint))
            return false;

        // The first indicator already claimed both columns — one is two wide on its own, and the flag the
        // pair makes is the same two. So this joins the content and moves nothing: no new width, no second
        // placeholder, and the cursor stays where the first one left it.

        var flag = new BufferCell
        {
            Content = first.Content + data,
            Width = 2,
            Attributes = first.Attributes,
            // The FIRST indicator, which is what identifies the flag — and what a second call would test
            // against, if a third indicator ever arrives.
            CodePoint = first.CodePoint,
        };

        line.SetCell(cellX, ref flag);

        // A third indicator starts a new pair rather than joining this one, which is what UAX #29 says:
        // indicators pair up from the left, they do not accumulate.
        _regionalPending = null;
        return true;
    }

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
        if (!UseRunPrinting || _terminal.InsertMode || _activeCharset is not null)
        {
            foreach (var b in data)
                Print(CodePointText.Get((char)b));
            return;
        }

        while (!data.IsEmpty)
        {
            if (_buffer.X >= _terminal.Cols)
            {
                if (!_terminal.Options.Wraparound)
                    return;

                if (_buffer.Y == _buffer.ScrollBottom)
                {
                    _buffer.SetCursor(0, _buffer.Y);
                    _buffer.ScrollUp(1, true);
                }
                else
                {
                    _buffer.SetCursor(0, _buffer.Y + 1);
                }

                _buffer.Lines[_buffer.Y + _buffer.YBase]!.IsWrapped = true;
            }

            var line = _buffer.Lines[_buffer.Y + _buffer.YBase];
            if (line == null)
                return;

            var take = Math.Min(_terminal.Cols - _buffer.X, data.Length);
            line.SetSingleWidthRun(_buffer.X, data[..take], _curAttr);

            // This path bypasses Print, so it keeps the link bookkeeping itself -- otherwise a link
            // would cover the text or not depending on which writer took it. Guarded here, not in a
            // helper, for the same reason as in Print.
            if (_linkUrl is not null || line.HasLinks)
                line.NoteLinkRun(_buffer.X, take, _linkUrl, _linkId);

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
        if (!UseRunPrinting || _terminal.InsertMode || _activeCharset is not null)
        {
            for (var k = 0; k < count; k++)
                Print(CodePointText.Get(data[start + k]));
            return;
        }

        var pos = start;
        var remaining = count;

        while (remaining > 0)
        {
            // Autowrap, matching Print. The cursor is allowed to rest one past the last column, so
            // the wrap is resolved here rather than when the previous character was written.
            if (_buffer.X >= _terminal.Cols)
            {
                if (!_terminal.Options.Wraparound)
                    return;   // printing past the edge is discarded, as in Print

                if (_buffer.Y == _buffer.ScrollBottom)
                {
                    _buffer.SetCursor(0, _buffer.Y);
                    _buffer.ScrollUp(1, true);
                }
                else
                {
                    _buffer.SetCursor(0, _buffer.Y + 1);
                }

                _buffer.Lines[_buffer.Y + _buffer.YBase]!.IsWrapped = true;
            }

            var line = _buffer.Lines[_buffer.Y + _buffer.YBase];
            if (line == null)
                return;

            var take = Math.Min(_terminal.Cols - _buffer.X, remaining);
            line.SetSingleWidthRun(_buffer.X, data.AsSpan(pos, take), _curAttr);

            // As above: bypassing Print means keeping the link bookkeeping here as well.
            if (_linkUrl is not null || line.HasLinks)
                line.NoteLinkRun(_buffer.X, take, _linkUrl, _linkId);

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

        // Append the combining character to the previous cell's content
        var newContent = prevCell.Content + data;

        // Determine if we need to adjust the width
        int newWidth = prevCell.Width;

        // Handle variation selectors that change presentation
        if (codePoint == VariationSelectorEmojiSymbol && prevCell.Width == 1)
        {
            // Emoji presentation selector: character becomes width 2
            newWidth = 2;
        }
        else if (codePoint == VariationSelectorTextSymbol && prevCell.Width == 2)
        {
            // Text presentation selector: character becomes width 1
            newWidth = 1;
        }

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
        _lastPrinted = (_buffer.Y + _buffer.YBase, _buffer.X, updatedCell.CodePoint, updatedCell.ClusterId);

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
                EraseInDisplay(parameters);
                break;

            case CsiCommand.EraseInLine:
                EraseInLine(parameters);
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
                // "CSI ? ... S" is XTSMGRAPHICS, not SCROLL UP. They share a final character, and
                // the identifier has its private marker stripped before the lookup, so without
                // this guard a Sixel program's opening capability query scrolled the screen.
                if (isPrivate)
                    GraphicsAttributes(parameters);
                else
                    ScrollUp(parameters);
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
                DeviceAttributes(parameters, isPrivate);
                break;

            case CsiCommand.LinePositionAbsolute:
                LinePositionAbsolute(parameters);
                break;

            case CsiCommand.SelectGraphicRendition:
                CharAttributes(parameters);
                break;

            case CsiCommand.DeviceStatusReport:
                DeviceStatusReport(parameters, isPrivate);
                break;

            case CsiCommand.SetScrollRegion:
                SetScrollRegion(parameters);
                break;

            case CsiCommand.SaveCursorAnsi:
                SaveCursorAnsi();
                break;

            case CsiCommand.RestoreCursorAnsi:
                RestoreCursorAnsi();
                break;

            case CsiCommand.WindowManipulation:
                WindowManipulation(parameters);
                break;

            case CsiCommand.SelectCursorStyle:
                SelectCursorStyle(parameters);
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
            case "c": // RIS - Reset to Initial State
                ResetTerminal();
                break;
            case "7": // DECSC - Save Cursor
                SaveCursor();
                break;
            case "8": // DECRC - Restore Cursor
                RestoreCursor();
                break;
        }

        // Charset designation sequences
        if (collected.Length > 0)
        {
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
                case '#': // DEC line attribute sequences
                    HandleDecLineAttribute(finalChar);
                    break;
            }
        }
    }

    private void HandleDecLineAttribute(string finalChar)
    {
        var line = _buffer.Lines[_buffer.Y + _buffer.YBase];
        if (line == null) return;
        switch (finalChar)
        {
            case "3": line.LineAttribute = LineAttribute.DoubleHeightTop; break;
            case "4": line.LineAttribute = LineAttribute.DoubleHeightBottom; break;
            case "5": line.LineAttribute = LineAttribute.Normal; break;
            case "6": line.LineAttribute = LineAttribute.DoubleWidth; break;
            case "8": FillScreenWithE(); break;
        }
    }

    private void FillScreenWithE()
    {
        var cell = new BufferCell('E', 1, AttributeData.Default);
        for (int row = 0; row < _terminal.Rows; row++)
        {
            var line = _buffer.Lines[_buffer.YBase + row];
            if (line != null)
            {
                line.LineAttribute = LineAttribute.Normal;
                line.Fill(cell);
            }
        }
        _buffer.SetCursor(0, 0);
    }

    private void SetCharset(CharsetMode mode, string charsetId)
    {
        var charset = Charsets.GetCharset(charsetId);
        _charsets[mode] = charset;
        RefreshActiveCharset();
    }

    /// <summary>
    /// Shift Out - Select G1 character set (SO, 0x0E).
    /// </summary>
    public void ShiftOut()
    {
        _currentCharset = CharsetMode.G1;
        RefreshActiveCharset();
    }

    /// <summary>
    /// Shift In - Select G0 character set (SI, 0x0F).
    /// </summary>
    public void ShiftIn()
    {
        _currentCharset = CharsetMode.G0;
        RefreshActiveCharset();
    }

    /// <summary>
    /// Resets charset state to defaults.
    /// </summary>
    public void ResetCharsets()
    {
        _charsets[CharsetMode.G0] = Charsets.ASCII;
        _charsets[CharsetMode.G1] = Charsets.ASCII;
        _charsets[CharsetMode.G2] = Charsets.ASCII;
        _charsets[CharsetMode.G3] = Charsets.ASCII;
        _currentCharset = CharsetMode.G0;
        RefreshActiveCharset();
    }

    #region DCS / Sixel

    /// <summary>The Sixel image being decoded, if a DECSIXEL payload is currently arriving.</summary>
    private Graphics.SixelDecoder? _sixelDecoder;

    /// <summary>
    /// The colour registers used when mode 1070 is reset, so images inherit each other's palette
    /// the way they did on a VT340. Built on first use, because the default is private registers
    /// and most sessions never touch this.
    /// </summary>
    private Graphics.SixelPalette? _sharedSixelPalette;

    /// <summary>
    /// Handles the start of a DCS sequence.
    /// </summary>
    /// <remarks>
    /// The payload that follows is streamed rather than handed over whole, so this is where we
    /// decide whether it is worth reading at all. Only DECSIXEL is; anything else is left to the
    /// parser's whole-payload event, which is capped and cheap.
    /// </remarks>
    public void HandleDcsHook(string identifier, Params parameters)
    {
        CancelRepeat();
        _sixelDecoder = null;

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
        var decoder = _sixelDecoder;
        _sixelDecoder = null;

        if (decoder is null || !terminatedCleanly)
            return;

        var image = decoder.Finish();
        if (image is not null)
            PlaceImage(Graphics.ImagePlacement.Natural(image), Graphics.PlacementKind.Sixel);
    }

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

    private void NoteAnimated(Graphics.TerminalImage image)
    {
        foreach (var known in AnimatedImages)
        {
            if (ReferenceEquals(known, image))
                return;
        }

        _animatedImages.Add(new WeakReference<Graphics.TerminalImage>(image));
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
    /// Handles the start of an APC sequence.
    /// </summary>
    /// <remarks>
    /// APC carries no parameters in front of its payload, so nothing can be decided here: what the
    /// sequence is depends on its first payload character, which has not arrived yet.
    /// </remarks>
    public void HandleApcHook(char introducer)
    {
        CancelRepeat();
        _ = introducer;
        _apcPayload.Clear();
    }

    /// <summary>
    /// Handles a chunk of an APC payload.
    /// </summary>
    public void HandleApcPut(ReadOnlySpan<char> data)
    {
        // Bounded here rather than at the end: the point is to stop a runaway sequence before the
        // memory is spent, not to notice afterwards.
        if (_apcPayload.Length <= MaxKittyPayloadChars)
            _apcPayload.Append(data);
    }

    /// <summary>
    /// Handles the end of an APC sequence.
    /// </summary>
    public void HandleApcUnhook(bool terminatedCleanly)
    {
        var payload = _apcPayload.ToString();
        _apcPayload.Clear();

        // A sequence cut short says nothing reliable about what it was carrying, and half a
        // transmission would corrupt whatever it was appended to.
        if (!terminatedCleanly)
        {
            _kittyTransmission = null;
            return;
        }

        if (payload.Length == 0 || payload[0] != 'G')
            return;
        if (!_terminal.Options.KittyGraphicsEnabled)
            return;

        HandleKittyGraphics(payload.AsSpan(1));
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

    /// <summary>
    /// Writes a cell that a client marked as showing part of an image.
    /// </summary>
    /// <remarks>
    /// <para>The image is named by the cell's FOREGROUND COLOUR, which carries a 24-bit id rather
    /// than a colour. That works here because <c>AttributeData</c> keeps 25 bits for the value, so
    /// it survives the round trip unchanged.</para>
    /// <para>Which tile the cell shows is worked out from where it sits relative to the top-left of
    /// the run, which is how a contiguous rectangle written in reading order comes out right. A
    /// client may also state the row and column explicitly, as combining marks drawn from a fixed
    /// table; those arrive after this cell and are applied by
    /// <see cref="TryApplyPlaceholderDiacritic"/>.</para>
    /// </remarks>
    /// <returns>False when nothing can be resolved, so the character prints as ordinary text.</returns>
    private bool TryPrintKittyPlaceholder()
    {
        if (!_terminal.Options.KittyGraphicsEnabled)
            return false;

        // Mode 0 is a palette index; only a direct colour carries an id.
        if (_curAttr.GetFgColorMode() == 0)
            return false;

        var imageId = (uint)_curAttr.GetFgColor();
        if (imageId == 0 || !_kittyImages.TryGet(imageId, out var image))
            return false;

        var row = _buffer.Y + _buffer.YBase;
        var col = _buffer.X;

        // A cell continues the rectangle if it falls INSIDE the picture measured from the origin.
        // Anything else -- past its last row, past its last column, or above or left of where it
        // started -- is a new picture starting here.
        //
        // The bound is the part that matters. Without it any later cell showing the same image
        // continued the first rectangle however far away it was, so its tile came out as the
        // distance from an origin it had nothing to do with: out of range, and the placeholder
        // printed as a visible character instead of a picture. That is not an edge case -- it is
        // what a client does when it shows one image twice, and what image.nvim does every time it
        // redraws a thumbnail lower down than the last one.
        var continues = _placeholderOrigin is { } origin
                        && origin.ImageId == imageId
                        && row >= origin.Row && row - origin.Row < origin.Image.Rows
                        && col >= origin.Col && col - origin.Col < origin.Image.Cols;

        if (!continues)
            _placeholderOrigin = (row, col, imageId, image, Graphics.LinePlacement.NextSerial());

        var start = _placeholderOrigin!.Value;
        if (!WritePlaceholderCell(row, col, start.Image, start.Serial, col - start.Col, row - start.Row))
            return false;

        _placeholderCell = (row, col, 0);
        _buffer.SetCursorRaw(_buffer.X + 1, _buffer.Y);
        return true;
    }

    /// <summary>Puts one tile of a placeholder rectangle onto its line, as a one-column run.</summary>
    /// <remarks>
    /// <para>One run per cell, rather than one per row, because a placeholder rectangle is written a
    /// cell at a time and each cell may be RE-tiled afterwards by the combining marks that follow
    /// it. A row-wide run would have to be split and rebuilt on every mark; a one-column run is
    /// simply replaced.</para>
    /// <para>Every cell of one rectangle shares a serial, so it is still a single placement as far
    /// as deleting is concerned. A renderer that wants one blit per strip can merge adjacent runs of
    /// the same image whose source rectangles are contiguous.</para>
    /// </remarks>
    /// <returns>False when the tile falls outside the picture.</returns>
    private bool WritePlaceholderCell(int row, int col, Graphics.TerminalImage image, int serial,
                                      int tileCol, int tileRow)
    {
        if (tileCol < 0 || tileRow < 0 || tileCol >= image.Cols || tileRow >= image.Rows)
            return false;

        var line = _buffer.Lines[row];
        if (line is null)
            return false;

        if (!image.TryGetTileSource(tileCol, tileRow, out var srcX, out var srcY,
                                    out var srcWidth, out var srcHeight))
            return false;

        // The cell keeps the placeholder character it was printed with; the picture is beside it
        // rather than in it. Written before the run so SetCell's Sixel split cannot see it.
        var cell = new BufferCell(" ", 1, _curAttr);
        line.SetCell(col, ref cell);

        // Anything already claiming this cell for this rectangle goes -- a mark may be re-tiling a
        // cell written a moment ago.
        line.RemovePlacements(p => p.Serial == serial && p.Column == col);

        line.AddPlacement(
            new Graphics.LinePlacement(
                image.Id, col, 1,
                srcX: srcX, srcY: srcY, srcWidth: srcWidth, srcHeight: srcHeight,
                kind: Graphics.PlacementKind.Kitty,
                serial: serial),
            image);
        return true;
    }

    /// <summary>
    /// Applies a combining mark that states part of the preceding placeholder cell's identity.
    /// </summary>
    /// <remarks>
    /// <para>The marks come in a fixed order and are positional: the first gives the tile row, the
    /// second the tile column, the third the most significant byte of the image id. A client may
    /// send fewer than three and let the rest be inferred, which is why each is applied on its own
    /// rather than waiting for the set.</para>
    /// <para>The third one can change WHICH image the cell shows, so the placement has to be
    /// rebuilt. That is rare -- it only matters for ids above 16777215 -- but resolving it late is
    /// the only option, since the id is not complete until the mark arrives.</para>
    /// </remarks>
    /// <returns>False if this is not a mark applying to a placeholder, so it prints normally.</returns>
    private bool TryApplyPlaceholderDiacritic(int codePoint)
    {
        if (_placeholderCell is not { } target || _placeholderOrigin is not { } origin)
            return false;

        // Only the cell immediately to the left, and only up to three marks.
        if (target.Row != _buffer.Y + _buffer.YBase || target.Col != _buffer.X - 1 || target.MarksSeen >= 3)
            return false;

        if (!Graphics.PlaceholderDiacritics.TryGetValue(codePoint, out var value))
            return false;

        var line = _buffer.Lines[target.Row];
        if (line is null)
            return false;

        // Which tile the cell is showing now, read back off the run that was written for it. The
        // run is one column wide, so the column of the rectangle it belongs to is the offset from
        // the origin rather than anything stored.
        if (!line.TryGetPlacementAt(target.Col, out var current) || current.Serial != origin.Serial)
            return false;

        var image = origin.Image;

        // Read the tile back off the run rather than recomputing it from the position. The marks
        // arrive one at a time and each is meant to survive the next: row then column means the
        // column mark must not undo the row one, which recomputing from the origin would do.
        var tileCol = image.CellWidth > 0 ? current.SrcX / image.CellWidth : 0;
        var tileRow = image.CellHeight > 0 ? current.SrcY / image.CellHeight : 0;

        switch (target.MarksSeen)
        {
            case 0:
                tileRow = value;
                break;

            case 1:
                tileCol = value;
                break;

            default:
                // The high byte of the id. Re-resolving can fail, and when it does the cell keeps
                // the picture it already had rather than becoming a blank.
                var extendedId = ((uint)value << 24) | (origin.ImageId & 0x00FFFFFF);
                if (_kittyImages.TryGet(extendedId, out var extended))
                {
                    image = extended;
                    _placeholderOrigin = (origin.Row, origin.Col, extendedId, extended, origin.Serial);
                }
                break;
        }

        _placeholderCell = (target.Row, target.Col, target.MarksSeen + 1);

        // An explicit row or column outside the picture is a client error; keeping the cell as it
        // was is better than blanking it, and better than throwing on another process's input.
        if (tileCol >= image.Cols || tileRow >= image.Rows)
            return true;

        WritePlaceholderCell(target.Row, target.Col, image, origin.Serial, tileCol, tileRow);
        return true;
    }

    /// <summary>
    /// Handles one Kitty graphics command, payload and all.
    /// </summary>
    /// <remarks>
    /// The control data and the payload are separated by the first semicolon. A sequence may carry
    /// only control data and no semicolon at all -- which is exactly what the first chunk of a
    /// chunked transmission looks like.
    /// </remarks>
    private void HandleKittyGraphics(ReadOnlySpan<char> body)
    {
        var separator = body.IndexOf(';');
        var controlText = separator < 0 ? body : body[..separator];
        var payload = separator < 0 ? ReadOnlySpan<char>.Empty : body[(separator + 1)..];

        var command = Graphics.KittyCommand.Parse(controlText);

        // A continuation chunk carries only "m=", so the command it belongs to is the one held from
        // the first chunk. Without this, every chunk after the first would read as a fresh transmit.
        if (_kittyTransmission is not null)
        {
            _kittyTransmission.Append(payload);

            if (command.MoreChunks)
                return;

            var pending = _kittyTransmission;
            _kittyTransmission = null;
            CompleteKittyTransmission(pending);
            return;
        }

        switch (command.Action)
        {
            case Graphics.KittyAction.Transmit:
            case Graphics.KittyAction.TransmitAndDisplay:
            case Graphics.KittyAction.Query:
                BeginKittyTransmission(command, payload);
                break;

            case Graphics.KittyAction.Put:
                PlaceStoredKittyImage(command);
                break;

            case Graphics.KittyAction.Delete:
                DeleteKittyImages(command);
                break;

            // A frame carries pixels like a transmission does, chunking and all, so it goes through
            // the same accumulator and is told apart at the end by its action.
            case Graphics.KittyAction.Frame:
                BeginKittyTransmission(command, payload);
                break;

            case Graphics.KittyAction.Animate:
                ControlKittyAnimation(command);
                break;

            case Graphics.KittyAction.Compose:
                ComposeKittyFrames(command);
                break;

            default:
                // Anything a later revision adds. Saying so is better than silence: a client that
                // asked can fall back rather than wait.
                ReplyToKitty(command, Graphics.KittyError.Unsupported);
                break;
        }
    }

    private void BeginKittyTransmission(Graphics.KittyCommand command, ReadOnlySpan<char> payload)
    {
        // Only the payload actually carried in the escape sequence. Reading a file the client names
        // would have the terminal open a path on its say-so, and this library runs inside hosts that
        // may hold more privilege than the program they are running.
        if (command.Medium != 'd')
        {
            ReplyToKitty(command, Graphics.KittyError.Unsupported);
            return;
        }

        // Refused on the declared size, before a byte of it is kept. A raw format states its
        // dimensions up front, so there is no reason to accumulate megabytes only to reject them --
        // and the payload cap would otherwise truncate the data and report it as corrupt instead of
        // as too large, which tells the client the wrong thing.
        if (command.Format != Graphics.KittyCommand.FormatPng
            && (long)command.Width * command.Height > _terminal.Options.MaxSixelPixels)
        {
            ReplyToKitty(command, Graphics.KittyError.TooLarge);
            return;
        }

        var transmission = new Graphics.KittyTransmission(command);
        transmission.Append(payload);

        if (command.MoreChunks)
        {
            _kittyTransmission = transmission;
            return;
        }

        CompleteKittyTransmission(transmission);
    }

    private void CompleteKittyTransmission(Graphics.KittyTransmission transmission)
    {
        var command = transmission.Command;

        var result = transmission.TryBuild(_terminal.Options.MaxSixelPixels,
                                           out var pixels, out var width, out var height);
        if (result != Graphics.KittyError.None)
        {
            ReplyToKitty(command, result);
            return;
        }

        // A frame belongs to a picture that already exists, so it becomes an entry in that image's
        // frame list rather than an image of its own. Taken before the image below is built, which
        // would be an allocation the size of the picture with nothing to use it.
        if (command.Action == Graphics.KittyAction.Frame)
        {
            AddKittyFrame(command, pixels, width, height);
            return;
        }

        var image = new Graphics.TerminalImage(
            pixels, width, height,
            Math.Max(1, _terminal.Options.CellWidthPixels),
            Math.Max(1, _terminal.Options.CellHeightPixels));

        // A query validates and answers. It must not put anything on the screen -- programs probe
        // with a real one-pixel image and expect their own output to be undisturbed.
        if (command.Action == Graphics.KittyAction.Query)
        {
            ReplyToKitty(command, Graphics.KittyError.None);
            return;
        }

        // A client that sent only a number gets an id chosen here, and is told what it was.
        var id = command.ImageId != 0 ? command.ImageId : _kittyImages.NextAssignedId();
        _kittyImages.Store(id, image, _terminal.Options.MaxImageRegistryBytes, command.ImageNumber);

        if (command.Action == Graphics.KittyAction.TransmitAndDisplay)
            PlaceKittyImage(image, command);

        ReplyToKitty(command, Graphics.KittyError.None, id);
    }

    /// <summary>
    /// Turns transmitted pixels into a frame of an image that already exists.
    /// </summary>
    /// <remarks>
    /// <para>A frame is built by composing the arriving rectangle onto a canvas. The canvas is
    /// another frame when the client names one with <c>c=</c>, the frame itself when it is editing
    /// one with <c>r=</c>, and otherwise a flat colour -- black and fully transparent unless
    /// <c>Y=</c> says otherwise. That is what lets an animation send only the pixels that changed.</para>
    /// <para>The rectangle's position comes from <c>x</c> and <c>y</c> and its size from the
    /// transmitted <c>s</c> and <c>v</c>, so a frame carrying the whole picture is just the case
    /// where the rectangle happens to be the full size.</para>
    /// </remarks>
    private void AddKittyFrame(Graphics.KittyCommand command, byte[] pixels, int width, int height)
    {
        if (!TryResolveKittyImage(command, out var id, out var image))
        {
            ReplyToKitty(command, Graphics.KittyError.NotFound);
            return;
        }

        var animation = image.EnsureAnimation();
        NoteAnimated(image);
        var frameBytes = (long)image.PixelWidth * image.PixelHeight * Graphics.TerminalImage.BytesPerPixel;

        byte[] canvas;
        int frameNumber;

        if (command.EditFrame > 0)
        {
            // Editing an existing frame: the canvas is that frame, and the result replaces it.
            if (!animation.TryGetFrame(command.EditFrame, out _))
            {
                ReplyToKitty(command, Graphics.KittyError.NotFound);
                return;
            }

            canvas = animation.GetWritableFrame(command.EditFrame);
            frameNumber = command.EditFrame;
        }
        else
        {
            canvas = new byte[frameBytes];

            if (command.BaseFrame > 0)
            {
                if (!animation.TryGetFrame(command.BaseFrame, out var baseFrame))
                {
                    ReplyToKitty(command, Graphics.KittyError.NotFound);
                    return;
                }

                baseFrame.Pixels.Span.CopyTo(canvas);
            }
            else
            {
                FillCanvas(canvas, command.FrameBackground);
            }

            // A new frame's gap defaults to the protocol's figure rather than to nothing, or an
            // animation built without explicit gaps would run as fast as the host repaints.
            var gap = command.ZIndex != 0 ? command.ZIndex : Graphics.ImageAnimation.DefaultGapMilliseconds;
            frameNumber = animation.AddFrame(canvas, gap);
        }

        // X=1 overwrites, anything else blends. The same key carries a pixel offset on a display
        // command; which is meant follows from the action.
        var replace = command.OffsetX == 1;

        Graphics.ImageAnimation.Blend(
            canvas, image.PixelWidth, image.PixelHeight,
            pixels, width,
            sourceX: 0, sourceY: 0,
            destinationX: command.CropX, destinationY: command.CropY,
            width: width, height: height,
            replace: replace);

        if (command.EditFrame > 0 && command.ZIndex != 0)
            animation.SetGap(frameNumber, command.ZIndex);

        _terminal.NoteImagePlaced(image);
        ReplyToKitty(command, Graphics.KittyError.None, id);
    }

    /// <summary>Fills a new frame's canvas with a 32-bit RGBA colour.</summary>
    /// <remarks>
    /// The protocol states the colour as RGBA; the buffer is BGRA, so the two outer channels swap.
    /// Getting this backwards produces a picture that looks right until something is transparent.
    /// </remarks>
    private static void FillCanvas(byte[] canvas, uint rgba)
    {
        if (rgba == 0)
            return;   // already black and fully transparent

        var r = (byte)(rgba >> 24);
        var g = (byte)(rgba >> 16);
        var b = (byte)(rgba >> 8);
        var a = (byte)rgba;

        for (int i = 0; i + 3 < canvas.Length; i += Graphics.TerminalImage.BytesPerPixel)
        {
            canvas[i] = b;
            canvas[i + 1] = g;
            canvas[i + 2] = r;
            canvas[i + 3] = a;
        }
    }

    /// <summary>
    /// Starts, stops or steps an animation, and sets frame gaps.
    /// </summary>
    /// <remarks>
    /// A client may drive the animation itself by making frames current one at a time, or hand the
    /// timing to the terminal by setting gaps and letting it run. Both arrive here; the difference
    /// is only which keys are present.
    /// </remarks>
    private void ControlKittyAnimation(Graphics.KittyCommand command)
    {
        if (!TryResolveKittyImage(command, out var id, out var image))
        {
            ReplyToKitty(command, Graphics.KittyError.NotFound);
            return;
        }

        var animation = image.Animation;
        if (animation is null)
        {
            // A still picture has no frames to control. Saying so beats silently doing nothing.
            ReplyToKitty(command, Graphics.KittyError.NotFound);
            return;
        }

        // r with z sets one frame's gap. A gap of zero means "unspecified" and is ignored, which is
        // why this is not simply "if r was given".
        if (command.EditFrame > 0 && command.ZIndex != 0)
            animation.SetGap(command.EditFrame, command.ZIndex);

        if (command.BaseFrame > 0 && !animation.SetCurrentFrame(command.BaseFrame))
        {
            ReplyToKitty(command, Graphics.KittyError.NotFound);
            return;
        }

        var state = command.AnimationStateValue;
        if (state is >= 1 and <= 3)
            animation.SetState((Graphics.AnimationState)state, command.LoopCount);

        ReplyToKitty(command, Graphics.KittyError.None, id);
    }

    /// <summary>
    /// Copies a rectangle from one frame of an image onto another.
    /// </summary>
    /// <remarks>
    /// The cheap way to change a frame: no pixels cross the wire at all. The protocol is specific
    /// about the failures -- a missing frame is ENOENT, a rectangle off the edge is EINVAL, and so
    /// is one frame overlapping itself, since the result would depend on the copy order.
    /// </remarks>
    private void ComposeKittyFrames(Graphics.KittyCommand command)
    {
        if (!TryResolveKittyImage(command, out var id, out var image))
        {
            ReplyToKitty(command, Graphics.KittyError.NotFound);
            return;
        }

        var animation = image.Animation;
        if (animation is null
            || !animation.TryGetFrame(command.EditFrame, out var source)
            || !animation.TryGetFrame(command.BaseFrame, out _))
        {
            ReplyToKitty(command, Graphics.KittyError.NotFound);
            return;
        }

        var width = command.CropWidth > 0 ? command.CropWidth : image.PixelWidth;
        var height = command.CropHeight > 0 ? command.CropHeight : image.PixelHeight;

        if (!FitsInside(command.OffsetX, command.OffsetY, width, height, image)
            || !FitsInside(command.CropX, command.CropY, width, height, image))
        {
            ReplyToKitty(command, Graphics.KittyError.BadData);
            return;
        }

        // Same frame with overlapping rectangles: the answer would depend on which pixel was copied
        // first, so the protocol asks for a refusal rather than an arbitrary one.
        if (command.EditFrame == command.BaseFrame
            && Overlaps(command.OffsetX, command.OffsetY, command.CropX, command.CropY, width, height))
        {
            ReplyToKitty(command, Graphics.KittyError.BadData);
            return;
        }

        // Read the source before making the destination writable: editing the root frame copies it
        // away from the image, and if the two are the same frame the span would be left dangling.
        var sourcePixels = source.Pixels.ToArray();
        var destination = animation.GetWritableFrame(command.BaseFrame);

        Graphics.ImageAnimation.Blend(
            destination, image.PixelWidth, image.PixelHeight,
            sourcePixels, image.PixelWidth,
            sourceX: command.OffsetX, sourceY: command.OffsetY,
            destinationX: command.CropX, destinationY: command.CropY,
            width: width, height: height,
            replace: command.ComposeMode == 1);

        ReplyToKitty(command, Graphics.KittyError.None, id);
    }

    private static bool FitsInside(int x, int y, int width, int height, Graphics.TerminalImage image)
        => x >= 0 && y >= 0
           && (long)x + width <= image.PixelWidth
           && (long)y + height <= image.PixelHeight;

    private static bool Overlaps(int aX, int aY, int bX, int bY, int width, int height)
        => aX < bX + width && bX < aX + width
           && aY < bY + height && bY < aY + height;

    private void PlaceStoredKittyImage(Graphics.KittyCommand command)
    {
        if (!TryResolveKittyImage(command, out var id, out var image))
        {
            ReplyToKitty(command, Graphics.KittyError.NotFound);
            return;
        }

        PlaceKittyImage(image, command);
        ReplyToKitty(command, Graphics.KittyError.None, id);
    }

    /// <summary>
    /// Finds a stored image from whichever identity the client used.
    /// </summary>
    /// <remarks>
    /// A client may name an image by the id it chose (<c>i=</c>) or by a number it chose
    /// (<c>I=</c>), leaving the terminal to pick the id. The id wins when both are present, since
    /// it is the more specific of the two.
    /// </remarks>
    private bool TryResolveKittyImage(Graphics.KittyCommand command,
                                      out uint id, out Graphics.TerminalImage image)
    {
        if (command.ImageId != 0)
        {
            id = command.ImageId;
            return _kittyImages.TryGet(id, out image);
        }

        if (command.ImageNumber != 0)
            return _kittyImages.TryGetByNumber(command.ImageNumber, out id, out image);

        id = 0;
        image = null!;
        return false;
    }

    /// <summary>
    /// Turns a Kitty display command into a placement and writes it into the buffer.
    /// </summary>
    private void PlaceKittyImage(Graphics.TerminalImage image, Graphics.KittyCommand command)
    {
        // A placeholder placement is shown by cells the client writes as text, not here.
        if (command.UnicodePlaceholder)
            return;

        var cropWidth = command.CropWidth > 0 ? command.CropWidth : image.PixelWidth - command.CropX;
        var cropHeight = command.CropHeight > 0 ? command.CropHeight : image.PixelHeight - command.CropY;
        if (cropWidth <= 0 || cropHeight <= 0)
            return;

        // c and r name a box to fill, which is a stretch. Without them the picture keeps its own
        // size and the edge tiles are clipped, which is a different calculation entirely.
        var stretched = command.Cols > 0 || command.Rows > 0;
        var cols = command.Cols > 0
            ? command.Cols
            : (cropWidth + image.CellWidth - 1) / image.CellWidth;
        var rows = command.Rows > 0
            ? command.Rows
            : (cropHeight + image.CellHeight - 1) / image.CellHeight;

        var placement = new Graphics.ImagePlacement(
            image, command.PlacementId,
            command.CropX, command.CropY, cropWidth, cropHeight,
            cols, rows,
            stretched ? Graphics.ImageScaling.Stretched : Graphics.ImageScaling.Natural,
            command.ZIndex, command.OffsetX, command.OffsetY);

        PlaceImage(placement, Graphics.PlacementKind.Kitty, command.KeepCursor);
    }

    /// <summary>
    /// Removes placements, and with an upper-case target the pixels behind them too.
    /// </summary>
    /// <remarks>
    /// <para>The case of the target letter is the whole difference between "stop showing this" and
    /// "forget it entirely": lower case removes the appearances, upper case additionally releases
    /// the stored image so its id no longer resolves.</para>
    /// <para>Several keys mean something different here than they do on a transmission. On a delete,
    /// <c>x</c> and <c>y</c> are screen cell coordinates rather than a crop origin, and <c>z</c> is
    /// the z-index being matched rather than one being assigned. The protocol overloads them by
    /// action, so the parsed <c>CropX</c>/<c>CropY</c> carry the cell here.</para>
    /// <para>Positional targets find a placement through one of its cells and then remove all of it.
    /// Deleting only the cells that fall in the named row or column would leave a picture with a
    /// hole through it, which is not what "delete the placements intersecting row 3" means.</para>
    /// </remarks>
    private void DeleteKittyImages(Graphics.KittyCommand command)
    {
        var target = command.DeleteTarget;
        var alsoFree = char.IsUpper(target);

        // Kitty numbers the screen from one; the buffer numbers it from zero.
        var cellX = command.CropX - 1;
        var cellY = command.CropY - 1;

        switch (char.ToLowerInvariant(target))
        {
            case 'a':
                _terminal.Buffer.ClearImages();
                if (alsoFree)
                    _kittyImages.Clear();
                break;

            // By image id, or by image number -- d=i and d=n name different identities, so each
            // looks up the one it is about rather than sharing a resolver that prefers the id.
            case 'i':
            case 'n':
                DeleteKittyImageByIdentity(command, byNumber: char.ToLowerInvariant(target) == 'n',
                                           alsoFree);
                break;

            case 'c':
                DropPlacementsAt(_buffer.X, _buffer.Y, alsoFree);
                break;

            case 'p':
                DropPlacementsAt(cellX, cellY, alsoFree);
                break;

            case 'q':
                DropPlacementsWhere(p => p.ZIndex == command.ZIndex,
                                    (col, row) => col == cellX && row == cellY, alsoFree);
                break;

            case 'x':
                DropPlacementsWhere(null, (col, _) => col == cellX, alsoFree);
                break;

            case 'y':
                DropPlacementsWhere(null, (_, row) => row == cellY, alsoFree);
                break;

            case 'z':
                DropPlacementsWhere(p => p.ZIndex == command.ZIndex, null, alsoFree);
                break;

            case 'f':
                // Animation frames. Nothing here stores any, so there is nothing to remove -- but
                // saying "unsupported" would be wrong, since the requested state is the state.
                break;

            default:
                ReplyToKitty(command, Graphics.KittyError.Unsupported);
                return;
        }

        ReplyToKitty(command, Graphics.KittyError.None, command.ImageId);
    }

    /// <summary>
    /// Removes the appearances of one stored image, named by id or by number.
    /// </summary>
    /// <remarks>
    /// A placement id narrows it to a single appearance. That case deliberately does not release the
    /// pixels even for an upper-case target: other placements of the same image may still be on
    /// screen, and freeing it would blank pictures the client did not name.
    /// </remarks>
    private void DeleteKittyImageByIdentity(Graphics.KittyCommand command, bool byNumber, bool alsoFree)
    {
        uint id;
        Graphics.TerminalImage image;

        if (byNumber)
        {
            if (!_kittyImages.TryGetByNumber(command.ImageNumber, out id, out image))
                return;
        }
        else
        {
            id = command.ImageId;
            if (id == 0 || !_kittyImages.TryGet(id, out image))
                return;
        }

        if (command.PlacementId != 0)
        {
            _terminal.DropPlacements(p => p.ImageId == image.Id && p.PlacementId == command.PlacementId);
            return;
        }

        _terminal.DropImage(image);

        if (alsoFree)
            _kittyImages.Remove(id);
    }

    /// <summary>Removes every placement covering one screen cell.</summary>
    private void DropPlacementsAt(int col, int row, bool alsoFree)
        => DropPlacementsWhere(null, (c, r) => c == col && r == row, alsoFree);

    /// <summary>
    /// Removes placements chosen by identity, by position, or by both.
    /// </summary>
    /// <param name="matches">A test on the placement, or null to accept any.</param>
    /// <param name="cellMatches">A test on a cell's screen position, or null to search everywhere.</param>
    private void DropPlacementsWhere(Func<Graphics.LinePlacement, bool>? matches,
                                     Func<int, int, bool>? cellMatches,
                                     bool alsoFree)
    {
        // No position to search by means every run on screen is a candidate and the identity test
        // does all the work.
        var doomed = _terminal.CollectPlacementsOnScreen(cellMatches ?? ((_, _) => true));

        if (matches is not null)
            doomed = doomed.Where(matches).ToList();

        if (doomed.Count == 0)
            return;

        // By SERIAL, not by run. A run found through one of its cells is one line of a picture, and
        // the target is the picture -- dropping only the rows that matched would leave a band cut
        // out of the middle of it.
        var serials = new HashSet<int>(doomed.Select(p => p.Serial));
        _terminal.DropPlacements(serials);

        if (!alsoFree)
            return;

        // The images behind the placements that just went. Any of them still shown elsewhere is
        // kept, because releasing it would blank an appearance the client did not name.
        var stillShown = _terminal.CollectPlacementsOnScreen((_, _) => true);
        foreach (var imageId in doomed.Select(p => p.ImageId).Distinct())
        {
            if (!stillShown.Any(p => p.ImageId == imageId))
                _kittyImages.Remove((uint)imageId);
        }
    }

    /// <summary>
    /// Answers a Kitty command, unless the client asked not to be told.
    /// </summary>
    /// <remarks>
    /// q=1 suppresses success and q=2 suppresses failure as well. A reply is what a program uses to
    /// find out the terminal speaks this protocol at all, so silence is never the default.
    /// </remarks>
    private void ReplyToKitty(Graphics.KittyCommand command, Graphics.KittyError error, uint id = 0)
    {
        var succeeded = error == Graphics.KittyError.None;

        if (command.Quiet >= 2 || (command.Quiet >= 1 && succeeded))
            return;

        // An unsolicited reply to a command that named neither an id nor a number would be
        // unattributable, so the protocol asks for silence instead.
        var replyId = id != 0 ? id : command.ImageId;
        if (replyId == 0 && command.ImageNumber == 0)
            return;

        // A client that addressed the image by number needs both halves back: the number so it can
        // match the reply to the command it sent, and the id the terminal chose so it can use the
        // image afterwards. Only one of the two is known when the command failed early.
        var identity = (replyId, command.ImageNumber) switch
        {
            (0, var number) => $"I={number}",
            (var actual, 0) => $"i={actual}",
            (var actual, var number) => $"i={actual},I={number}"
        };
        var status = error switch
        {
            Graphics.KittyError.None => "OK",
            Graphics.KittyError.NotFound => "ENOENT:no such image",
            Graphics.KittyError.TooLarge => "EFBIG:image too large",
            Graphics.KittyError.Unsupported => "ENOTSUP:not supported",
            _ => "EINVAL:bad image data"
        };

        _terminal.RaiseDataReceived($"\u001b_G{identity};{status}\u001b\\");
    }

    /// <summary>
    /// Writes an image into the buffer as one run per line.
    /// </summary>
    /// <remarks>
    /// <para>A picture spanning several rows becomes several <see cref="Graphics.LinePlacement"/>s,
    /// one per line, each carrying its own slice of the source. The line owns the run and the image,
    /// so scrolling, scrollback eviction and ownership all keep working without anything being
    /// written into cells — and a resize does nothing to a picture at all.</para>
    /// <para>Cells are not touched. That is the whole difference between the two protocols here:
    /// Sixel is CONTENT, so printing over it replaces that part of the picture and
    /// <see cref="BufferLine.SplitPlacementsAt"/> does it explicitly; Kitty is an OVERLAY the
    /// z-index orders against the text, so a picture placed over a character hides it while it is
    /// there and reveals it again when it is deleted. Blanking the cell would make that
    /// irreversible.</para>
    /// </remarks>
    private void PlaceImage(Graphics.ImagePlacement placement,
                            Graphics.PlacementKind kind,
                            bool keepCursor = false)
    {
        // DECSDM set means the older display behaviour: pinned to the top-left, clipped rather
        // than scrolled, cursor untouched.
        var scrolling = !_terminal.SixelDisplayMode;

        var startCol = scrolling ? Math.Min(_buffer.X, _terminal.Cols - 1) : 0;
        if (startCol < 0)
            startCol = 0;
        var row = scrolling ? _buffer.Y : 0;

        var lastRowDrawn = row;

        // One serial for the placement, shared by every row of it, so a delete aimed at any one
        // cell can find and remove the whole picture.
        var serial = Graphics.LinePlacement.NextSerial();

        for (int tileRow = 0; tileRow < placement.Rows; tileRow++)
        {
            if (row > _buffer.ScrollBottom)
            {
                if (!scrolling)
                    break; // clipped at the bottom of the screen

                // Ran off the bottom of the scroll region: push a line into the scrollback and
                // carry on writing at the last row, which is what a long image does to a screen.
                _buffer.ScrollUp(1);
                row = _buffer.ScrollBottom;
            }

            var line = _buffer.Lines[_buffer.YBase + row];
            if (line is null)
                break;

            // One run per line. Cols is the picture's NATURAL width — deliberately NOT clipped to
            // the terminal, so a window widened later reveals more of the picture rather than
            // having lost it.
            //
            // The row's own slice of the source comes from the placement rather than the image,
            // because a Kitty placement may be cropped and scaled: tileRow 3 of a stretched box is
            // not the same pixels as tileRow 3 of the picture at its natural size.
            if (placement.TryGetTileLayout(0, tileRow, out _, out var srcY, out _, out var srcHeight,
                                           out _, out _, out _, out _))
            {
                line.AddPlacement(
                    new Graphics.LinePlacement(
                        placement.Image.Id,
                        startCol,
                        placement.Cols,
                        srcX: placement.SourceX,
                        srcY: srcY,
                        srcWidth: placement.SourceWidth,
                        srcHeight: srcHeight,
                        kind: kind,
                        placementId: placement.Id,
                        offsetX: (short)placement.OffsetX,
                        // Only the first row is shifted down inside its cell. Every row after it
                        // starts at the top of its own, or the offset would be re-applied per row
                        // and walk the picture down the screen.
                        offsetY: tileRow == 0 ? (short)placement.OffsetY : (short)0,
                        zIndex: (short)placement.ZIndex,
                        serial: serial),
                    placement.Image);
            }

            lastRowDrawn = row;
            if (tileRow < placement.Rows - 1)
                row++;
        }

        if (!scrolling)
            return;

        // Kitty's C=1. The picture is drawn but the cursor does not follow it, which is what lets a
        // program place several images without tracking where each one left the caret.
        if (keepCursor)
        {
            _terminal.NoteImagePlaced(placement.Image);
            return;
        }

        if (_terminal.SixelCursorRight)
        {
            // Mode 8452: stay on the image's last row, just past its right edge.
            _buffer.SetCursor(Math.Min(startCol + placement.Cols, _terminal.Cols - 1), lastRowDrawn);
        }
        else
        {
            // The cursor belongs on the line below the image, which may need one more scroll if
            // the image finished on the last row of the region.
            var below = lastRowDrawn + 1;
            if (below > _buffer.ScrollBottom)
            {
                _buffer.ScrollUp(1);
                below = _buffer.ScrollBottom;
            }
            _buffer.SetCursor(0, below);
        }

        _terminal.NoteImagePlaced(placement.Image);
    }

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
                    _terminal.Title = arg;
                    _terminal.RaiseTitleChanged(arg);
                    break;

                case OscCommand.SetIconName:
                    // Icon name - not typically supported in modern terminals
                    recognized = false;
                    break;

                case OscCommand.ChangeColor:
                    HandleColorPaletteChange(arg);
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

                case OscCommand.ForegroundColor:
                    HandleColorQuery(((int)command).ToString(), arg);
                    break;

                case OscCommand.BackgroundColor:
                    HandleColorQuery(((int)command).ToString(), arg);
                    break;

                case OscCommand.CursorColor:
                    HandleColorQuery(((int)command).ToString(), arg);
                    break;

                case OscCommand.Clipboard:
                    HandleClipboard(arg);
                    break;

                case OscCommand.KittyNotification:
                    HandleKittyNotification(arg);
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

    private void HandleColorPaletteChange(string data)
    {
        // OSC 4 ; index ; spec [ ; index ; spec ]... ST
        // Pairs, plural: xterm accepts any number in one sequence, and theme scripts routinely send
        // all sixteen ANSI colours at once rather than as sixteen sequences.
        var parts = data.Split(';');

        for (var i = 0; i + 1 < parts.Length; i += 2)
        {
            if (!int.TryParse(parts[i], out var index) || index < 0 || index >= ColorPalette.Size)
            {
                continue;
            }

            if (parts[i + 1] == "?")
            {
                // Answering with the CURRENT colour, not a constant. A program asking this is
                // usually about to pick its own colours to match.
                _terminal.RaiseDataReceived($"\u001b]4;{index};{ColorSpec.Format(_terminal.Colors[index])}\u0007");
                continue;
            }

            if (ColorSpec.TryParse(parts[i + 1], out var rgb))
            {
                _terminal.Colors.SetColor(index, rgb);
            }
        }
    }

    private void HandleCurrentDirectory(string data)
    {
        // OSC 7 ; file://hostname/path ST
        // Example: OSC 7;file://localhost/home/user ST
        if (data.StartsWith("file://"))
        {
            // Extract path from file:// URL
            var uri = data.Substring(7); // Remove "file://"
            var slashIndex = uri.IndexOf('/');
            if (slashIndex >= 0)
            {
                var path = uri.Substring(slashIndex);
                _terminal.CurrentDirectory = Uri.UnescapeDataString(path);
                _terminal.RaiseDirectoryChanged(_terminal.CurrentDirectory);
            }
        }
    }

    /// <summary>
    /// OSC 9 - ConEmu-style extensions, dispatched on the FIRST parameter rather than the code.
    /// </summary>
    private void HandleConEmu(string data)
    {
        // The sub-parameter decides which feature this is, and the notification form has no
        // sub-parameter at all -- OSC 9 ; text -- so it can only be the fallback. That makes the
        // ORDER load-bearing rather than incidental: every claimed sub-command has to be matched
        // first, or OSC 9;4;1;50 pops a toast reading "4;1;50" on every progress tick.
        //
        // An unclaimed sub-parameter is therefore a notification by definition, which is the right
        // reading of a permissive extension space, and means a future ConEmu code shows up as text
        // rather than being dropped.
        var parts = data.Split(new[] { ';' }, 2);

        if (parts.Length == 1 && (data == "9" || data == "4"))
        {
            // A claimed sub-command with nothing after it. Malformed rather than a notification:
            // reporting it as one would raise a toast whose entire body is "9".
            return;
        }

        if (parts.Length == 2 && parts[0] == "9")
        {
            // OSC 9 ; 9 ; path ST - working directory, the ConEmu convention. Microsoft's documented
            // Windows prompts emit THIS rather than OSC 7, so a terminal that only reads 7 silently
            // loses the cwd on Windows. Path is bare, not a file:// URI, and pwsh quotes it.
            var path = parts[1].Trim('"');
            if (!string.IsNullOrEmpty(path))
            {
                _terminal.CurrentDirectory = path;
                _terminal.RaiseDirectoryChanged(path);
            }

            return;
        }

        if (parts.Length == 2 && parts[0] == "4")
        {
            HandleProgress(parts[1]);
            return;
        }

        // OSC 9 ; text ST - desktop notification (the iTerm2 reading of this code).
        if (!string.IsNullOrEmpty(data))
        {
            _terminal.RaiseNotificationReceived(data);
        }
    }

    /// <summary>
    /// Handles Kitty desktop notifications (OSC 99).
    /// </summary>
    private void HandleKittyNotification(string data)
    {
        if (!_terminal.Options.KittyNotificationsEnabled)
            return;

        RemoveExpiredKittyNotifications();
        var parts = data.Split(new[] { ';' }, 2);
        if (parts.Length != 2)
            return;

        string? identifier = null;
        var payloadType = "title";
        string? icon = null;
        int? urgency = null;
        var encoded = false;
        var done = true;

        foreach (var parameter in parts[0].Split(':'))
        {
            var keyValue = parameter.Split(new[] { '=' }, 2);
            if (keyValue.Length != 2)
                continue;

            switch (keyValue[0])
            {
                case "i":
                    identifier = SanitizeIdentifier(keyValue[1]);
                    break;
                case "p":
                    payloadType = keyValue[1];
                    break;
                case "d":
                    done = keyValue[1] != "0";
                    break;
                case "e":
                    encoded = keyValue[1] == "1";
                    break;
                case "u":
                    if (int.TryParse(keyValue[1], out var parsedUrgency))
                        urgency = parsedUrgency;
                    break;
                case "n":
                    icon = DecodeBase64(keyValue[1]);
                    break;
            }
        }

        if (payloadType == "?")
        {
            _terminal.RaiseDataReceived($"\u001b]99;i={identifier ?? "0"}:p=?;p=title,body\u001b\\");
            return;
        }

        if (payloadType is not ("title" or "body"))
            return;

        var key = identifier ?? string.Empty;
        if (!_kittyNotifications.TryGetValue(key, out var notification))
        {
            if (!done && _kittyNotifications.Count >= MaxPendingKittyNotifications)
                return;

            notification = new KittyNotification(identifier);
            if (!done)
                _kittyNotifications[key] = notification;
        }

        var payload = encoded ? DecodeBase64(parts[1]) : SanitizeText(parts[1]);
        if (payload is null || !notification.Append(payloadType, payload, urgency, icon))
        {
            _kittyNotifications.Remove(key);
            return;
        }

        if (!done)
            return;

        _kittyNotifications.Remove(key);
        if (notification.TryBuild(out var title, out var body))
            _terminal.RaiseKittyNotificationReceived(notification.Identifier, title, body, notification.Urgency, notification.Icon);
    }

    private void RemoveExpiredKittyNotifications()
    {
        var cutoff = DateTime.UtcNow - KittyNotificationTimeout;
        foreach (var key in _kittyNotifications.Where(entry => entry.Value.LastUpdated < cutoff).Select(entry => entry.Key).ToArray())
            _kittyNotifications.Remove(key);
    }

    private static string? DecodeBase64(string value)
    {
        try
        {
            return SanitizeText(Encoding.UTF8.GetString(Convert.FromBase64String(value)));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string SanitizeIdentifier(string value) =>
        new(value.Where(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '+' or '.' or '-').Take(1024).ToArray());

    private static string SanitizeText(string value) =>
        new(value.Where(character => character is not (>= '\0' and <= '\x1f') and not (>= '\x7f' and <= '\x9f')).ToArray());

    private sealed class KittyNotification
    {
        private readonly StringBuilder _title = new();
        private readonly StringBuilder _body = new();

        public KittyNotification(string? identifier) => Identifier = identifier;

        public string? Identifier { get; }
        public int? Urgency { get; private set; }
        public string? Icon { get; private set; }
        public DateTime LastUpdated { get; private set; } = DateTime.UtcNow;

        public bool Append(string payloadType, string payload, int? urgency, string? icon)
        {
            if (_title.Length + _body.Length + payload.Length > MaxKittyNotificationBytes)
                return false;

            (payloadType == "title" ? _title : _body).Append(payload);
            Urgency ??= urgency;
            Icon ??= icon;
            LastUpdated = DateTime.UtcNow;
            return true;
        }

        public bool TryBuild(out string? title, out string? body)
        {
            title = _title.Length == 0 ? null : _title.ToString();
            body = _body.Length == 0 ? null : _body.ToString();
            return title is not null || body is not null;
        }
    }

    /// <summary>
    /// OSC 9 ; 4 ; state ; progress ST - progress reporting.
    /// </summary>
    private void HandleProgress(string data)
    {
        var parts = data.Split(';');

        if (!int.TryParse(parts[0], out var rawState) || !Enum.IsDefined(typeof(ProgressState), rawState))
        {
            return;
        }

        var state = (ProgressState)rawState;

        // Value is absent for None and Indeterminate, and meaningless anyway; clamped rather than
        // rejected, because a sender that overshoots still means "as far as it goes".
        var value = 0;
        if (parts.Length > 1 && int.TryParse(parts[1], out var parsed))
        {
            value = Math.Clamp(parsed, 0, 100);
        }

        if (state == ProgressState.None || state == ProgressState.Indeterminate)
        {
            value = 0;
        }

        _terminal.ProgressState = state;
        _terminal.ProgressValue = value;
        _terminal.RaiseProgressChanged(state, value);
    }

    /// <summary>
    /// OSC 133 - FinalTerm/FTCS shell integration marks.
    /// </summary>
    private void HandleShellIntegration(string data)
    {
        var parts = data.Split(';');
        if (parts.Length == 0 || parts[0].Length == 0)
        {
            return;
        }

        ShellIntegrationMark mark;
        switch (parts[0])
        {
            case "A": mark = ShellIntegrationMark.PromptStart; break;
            case "B": mark = ShellIntegrationMark.CommandStart; break;
            case "C": mark = ShellIntegrationMark.CommandExecuted; break;
            case "D": mark = ShellIntegrationMark.CommandFinished; break;
            default: return;
        }

        int? exitCode = null;
        if (mark == ShellIntegrationMark.CommandFinished)
        {
            // Only D carries one, and it is optional even there: cmd.exe cannot read the previous
            // command's status from its prompt and always sends a bare D. Left null rather than
            // defaulted to 0, so "not reported" never reads as "succeeded".
            if (parts.Length > 1 && int.TryParse(parts[1], out var parsedExit))
            {
                exitCode = parsedExit;
            }

            _terminal.LastCommandExitCode = exitCode;
        }

        // Anchor it. The event says a mark happened; the line says where, which is the half every
        // use of shell integration actually needs -- jumping to the previous prompt, selecting a
        // command's output, putting an exit status beside the command that produced it.
        //
        // Deliberately NOT cleared by erasing the cells it sits among. A mark records a position in
        // the history rather than anything about the content there, and a shell redrawing its prompt
        // with EL -- which is most of them -- would otherwise destroy the A mark it had just
        // emitted, a moment before the prompt it marks is even printed.
        var line = _buffer.Lines[_buffer.Y + _buffer.YBase];
        line?.AddMark(new Buffer.LineMark(_buffer.X, mark, exitCode));

        _terminal.ShellIntegrationState = mark;
        _terminal.RaiseShellIntegrationMark(mark, exitCode);
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

    private void HandleHyperlink(string data)
    {
        // OSC 8 ; params ; URI ST
        // Example: OSC 8;;http://example.com ST (start link)
        //          OSC 8;; ST (end link)
        var parts = data.Split(new[] { ';' }, 2);

        if (parts.Length >= 2)
        {
            var params_ = parts[0];
            var uri = parts[1];

            if (string.IsNullOrEmpty(uri))
            {
                // End hyperlink
                _terminal.CurrentHyperlink = null;
                _terminal.HyperlinkId = null;
                _linkUrl = null;
                _linkId = null;
                _terminal.RaiseHyperlinkChanged(null);
            }
            else
            {
                // Start hyperlink. The id resets BEFORE the parameters are parsed: a client can
                // open a new link without closing the last, and one that sends no id= must not
                // inherit the previous link's -- that would join two unrelated links into one.
                _terminal.CurrentHyperlink = uri;
                _terminal.HyperlinkId = null;
                _linkUrl = uri;
                _linkId = null;

                // Parse params for id= parameter
                if (!string.IsNullOrEmpty(params_))
                {
                    var paramParts = params_.Split(':');
                    foreach (var p in paramParts)
                    {
                        if (p.StartsWith("id="))
                        {
                            _terminal.HyperlinkId = p.Substring(3);
                            _linkId = _terminal.HyperlinkId;
                        }
                    }
                }

                _terminal.RaiseHyperlinkChanged(uri);
            }
        }
    }

    private void HandleColorQuery(string colorType, string data)
    {
        // OSC 10/11/12 ; spec [ ; spec ]... ST  - set, or query when spec is "?"
        //
        // Multiple specs advance through the resources in order, so OSC 10 ; fg ; bg sets the
        // foreground AND the background. xterm defines it that way and shell prompts written for
        // xterm use it, so handling only the first would set the foreground and silently drop the
        // background.
        if (!int.TryParse(colorType, out var resource))
        {
            return;
        }

        foreach (var spec in data.Split(';'))
        {
            if (resource > 12)
            {
                break;
            }

            if (spec == "?")
            {
                var current = resource switch
                {
                    10 => _terminal.Colors.Foreground,
                    11 => _terminal.Colors.Background,
                    _ => _terminal.Colors.Cursor,
                };

                // The real colour, not a constant. Programs query OSC 11 to decide whether they are
                // on a light or a dark terminal; answering black regardless told every one of them
                // "dark", and a light theme got dark-theme colours drawn onto it.
                _terminal.RaiseDataReceived($"\u001b]{resource};{ColorSpec.Format(current)}\u0007");
            }
            else if (ColorSpec.TryParse(spec, out var rgb))
            {
                switch (resource)
                {
                    case 10: _terminal.Colors.SetForeground(rgb); break;
                    case 11: _terminal.Colors.SetBackground(rgb); break;
                    case 12: _terminal.Colors.SetCursor(rgb); break;
                }
            }

            resource++;
        }
    }

    private void HandleClipboard(string data)
    {
        // OSC 52 ; c ; data ST
        // Example: OSC 52;c;base64data ST
        var parts = data.Split(new[] { ';' }, 2);

        if (parts.Length >= 2)
        {
            var target = parts[0]; // Usually 'c' for clipboard, 'p' for primary
            var clipdata = parts[1];

            if (clipdata == "?")
            {
                // Query clipboard - respond with clipboard content
                // Format: OSC 52 ; c ; base64data ST
                // For security, many terminals don't support this
                // We'll send an empty response
                _terminal.RaiseDataReceived($"\u001b]52;{target};\u0007");
            }
            else
            {
                // Set clipboard
                try
                {
                    var decoded = Convert.FromBase64String(clipdata);
                    var text = System.Text.Encoding.UTF8.GetString(decoded);
                    // TODO: Integrate with system clipboard
                    // For now, we just acknowledge receipt
                }
                catch
                {
                    // Invalid base64 or encoding
                }
            }
        }
    }

    private void HandleColorReset(OscCommand command, string data)
    {
        // OSC 104 [ ; index ]... ST  - reset palette entries, or all of them when bare
        // OSC 110/111/112 ST         - reset foreground / background / cursor
        //
        // "Reset" means back to the EMBEDDER'S theme, not to a factory dark palette. Anything else
        // and a program calling OSC 104 would drag a light terminal to black and leave it there.
        switch (command)
        {
            case OscCommand.ResetForeground:
                _terminal.Colors.ResetForeground();
                return;

            case OscCommand.ResetBackground:
                _terminal.Colors.ResetBackground();
                return;

            case OscCommand.ResetCursor:
                _terminal.Colors.ResetCursor();
                return;
        }

        if (string.IsNullOrEmpty(data))
        {
            _terminal.Colors.ResetAllColors();
            return;
        }

        foreach (var part in data.Split(';'))
        {
            if (int.TryParse(part, out var index) && index >= 0 && index < ColorPalette.Size)
            {
                _terminal.Colors.ResetColor(index);
            }
        }
    }

    // CSI Handler Implementations

    private void CursorUp(Params parameters)
    {
        var count = Math.Max(parameters.GetParam(0, 1), 1);
        _buffer.SetCursor(_buffer.X, Math.Max(_buffer.Y - count, 0));
    }

    private void CursorDown(Params parameters)
    {
        var count = Math.Max(parameters.GetParam(0, 1), 1);
        _buffer.SetCursor(_buffer.X, Math.Min(_buffer.Y + count, _terminal.Rows - 1));
    }

    private void CursorForward(Params parameters)
    {
        var count = Math.Max(parameters.GetParam(0, 1), 1);
        _buffer.SetCursor(Math.Min(_buffer.X + count, _terminal.Cols - 1), _buffer.Y);
    }

    private void CursorBackward(Params parameters)
    {
        var count = Math.Max(parameters.GetParam(0, 1), 1);
        _buffer.SetCursor(Math.Max(_buffer.X - count, 0), _buffer.Y);
    }

    private void CursorNextLine(Params parameters)
    {
        var count = Math.Max(parameters.GetParam(0, 1), 1);
        _buffer.SetCursor(0, Math.Min(_buffer.Y + count, _terminal.Rows - 1));
    }

    private void CursorPrecedingLine(Params parameters)
    {
        var count = Math.Max(parameters.GetParam(0, 1), 1);
        _buffer.SetCursor(0, Math.Max(_buffer.Y - count, 0));
    }

    private void CursorCharAbsolute(Params parameters)
    {
        var col = Math.Max(parameters.GetParam(0, 1), 1) - 1;
        _buffer.SetCursor(col, _buffer.Y);
    }

    private void CursorPosition(Params parameters)
    {
        var row = Math.Max(parameters.GetParam(0, 1), 1) - 1;
        var col = Math.Max(parameters.GetParam(1, 1), 1) - 1;
        row = GetAbsoluteCursorRow(row);
        _buffer.SetCursor(col, row);
    }

    private void EraseInDisplay(Params parameters)
    {
        var mode = parameters.GetParam(0, 0);
        var emptyCell = BufferCell.Space;
        emptyCell.Attributes = _curAttr;

        switch (mode)
        {
            case 0: // Erase below
                EraseInLine(parameters); // Current line from cursor
                for (int i = _buffer.Y + 1; i < _terminal.Rows; i++)
                {
                    _buffer.Lines[_buffer.YBase + i]?.Fill(emptyCell);
                }
                break;
            case 1: // Erase above
                for (int i = 0; i < _buffer.Y; i++)
                {
                    _buffer.Lines[_buffer.YBase + i]?.Fill(emptyCell);
                }
                EraseInLine(parameters); // Current line to cursor
                break;
            case 2: // Erase all — the visible screen only; the scrollback is kept
                for (int i = 0; i < _terminal.Rows; i++)
                {
                    _buffer.Lines[_buffer.YBase + i]?.Fill(emptyCell);
                }
                break;
            case 3: // Erase scrollback (xterm extension) — the scrollback only; the screen is kept
                // Previously shared the body above, which erases the VISIBLE screen and never touches the
                // scrollback: the opposite of what mode 3 asks for. The two modes are complements, not
                // variations, so a caller wanting both sends 2 and 3 — which is exactly what cmd.exe's
                // `cls` does under ConPTY (it clears the screen line by line, then sends CSI 3 J).
                //
                // Discarding rather than blanking is the point: blanked lines are still scrollable, so the
                // history stayed reachable with the mouse wheel even though the terminal had been told to
                // throw it away.
                _buffer.ClearScrollback();
                break;
        }
    }

    private void EraseInLine(Params parameters)
    {
        var mode = parameters.GetParam(0, 0);
        var line = _buffer.Lines[_buffer.Y + _buffer.YBase];
        if (line == null)
            return;

        var emptyCell = BufferCell.Space;
        emptyCell.Attributes = _curAttr;

        switch (mode)
        {
            case 0: // Erase to right
                line.Fill(emptyCell, _buffer.X, _terminal.Cols);
                break;
            case 1: // Erase to left
                line.Fill(emptyCell, 0, _buffer.X + 1);
                break;
            case 2: // Erase entire line
                line.Fill(emptyCell);
                break;
        }
    }

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

    private void InsertLines(Params parameters)
    {
        var count = Math.Max(parameters.GetParam(0, 1), 1);
        // Only works in scroll region
        if (_buffer.Y < _buffer.ScrollTop || _buffer.Y > _buffer.ScrollBottom)
            return;

        for (int i = 0; i < count; i++)
        {
            _buffer.Lines.Splice(_buffer.YBase + _buffer.ScrollBottom, 1);
            _buffer.Lines.Splice(_buffer.Y + _buffer.YBase, 0,
                _buffer.GetBlankLine(_curAttr));
        }
    }

    private void DeleteLines(Params parameters)
    {
        var count = Math.Max(parameters.GetParam(0, 1), 1);
        // Only works in scroll region
        if (_buffer.Y < _buffer.ScrollTop || _buffer.Y > _buffer.ScrollBottom)
            return;

        for (int i = 0; i < count; i++)
        {
            _buffer.Lines.Splice(_buffer.Y + _buffer.YBase, 1);
            _buffer.Lines.Splice(_buffer.YBase + _buffer.ScrollBottom, 0,
                _buffer.GetBlankLine(_curAttr));
        }
    }

    private void InsertChars(Params parameters)
    {
        var count = Math.Max(parameters.GetParam(0, 1), 1);
        var line = _buffer.Lines[_buffer.Y + _buffer.YBase];
        if (line == null)
            return;

        // Shift cells right from cursor position
        line.CopyCellsFrom(line, _buffer.X, _buffer.X + count,
            _terminal.Cols - _buffer.X - count, false);

        // Blank the inserted cells at cursor position
        var emptyCell = BufferCell.Space;
        emptyCell.Attributes = _curAttr;
        line.Fill(emptyCell, _buffer.X, Math.Min(_buffer.X + count, _terminal.Cols));
    }

    private void DeleteChars(Params parameters)
    {
        var count = Math.Max(parameters.GetParam(0, 1), 1);
        var line = _buffer.Lines[_buffer.Y + _buffer.YBase];
        if (line == null)
            return;

        // Limit count to remaining characters on line
        var remaining = _terminal.Cols - _buffer.X;
        count = Math.Min(count, remaining);

        line.CopyCellsFrom(line, _buffer.X + count, _buffer.X,
            _terminal.Cols - _buffer.X - count, false);

        // Fill vacated cells at right edge with current attributes (BCE)
        var emptyCell = BufferCell.Space;
        emptyCell.Attributes = _curAttr;
        line.Fill(emptyCell, _terminal.Cols - count, _terminal.Cols);
    }

    private void EraseChars(Params parameters)
    {
        var count = Math.Max(parameters.GetParam(0, 1), 1);
        var line = _buffer.Lines[_buffer.Y + _buffer.YBase];

        var emptyCell = BufferCell.Space;
        emptyCell.Attributes = _curAttr;

        line?.Fill(emptyCell, _buffer.X, Math.Min(_buffer.X + count, _terminal.Cols));
    }

    private void ScrollUp(Params parameters)
    {
        var count = Math.Max(parameters.GetParam(0, 1), 1);
        _buffer.ScrollUp(count);
    }

    private void ScrollDown(Params parameters)
    {
        var count = Math.Max(parameters.GetParam(0, 1), 1);
        _buffer.ScrollDown(count);
    }

    private void SaveCursorAnsi()
    {
        // ANSI save cursor (CSI s) - same as DEC DECSC but simpler
        SaveCursor();
    }

    private void RestoreCursorAnsi()
    {
        // ANSI restore cursor (CSI u) - same as DEC DECRC but simpler
        RestoreCursor();
    }

    private void LinePositionAbsolute(Params parameters)
    {
        // VPA - Line Position Absolute (CSI d)
        var row = Math.Max(parameters.GetParam(0, 1), 1) - 1;
        row = GetAbsoluteCursorRow(row);
        _buffer.SetCursor(_buffer.X, row);
    }

    private void CursorForwardTab(Params parameters)
    {
        // CHT - Cursor Forward Tabulation (CSI I)
        var count = Math.Max(parameters.GetParam(0, 1), 1);
        var tabWidth = _terminal.Options.TabStopWidth;

        for (int i = 0; i < count; i++)
        {
            var nextTabStop = ((_buffer.X / tabWidth) + 1) * tabWidth;
            _buffer.SetCursor(Math.Min(nextTabStop, _terminal.Cols - 1), _buffer.Y);
        }
    }

    private void CursorBackwardTab(Params parameters)
    {
        // CBT - Cursor Backward Tabulation (CSI Z)
        var count = Math.Max(parameters.GetParam(0, 1), 1);
        var tabWidth = _terminal.Options.TabStopWidth;

        for (int i = 0; i < count; i++)
        {
            if (_buffer.X == 0)
                break;

            var prevTabStop = ((_buffer.X - 1) / tabWidth) * tabWidth;
            _buffer.SetCursor(Math.Max(prevTabStop, 0), _buffer.Y);
        }
    }

    private void TabClear(Params parameters)
    {
        // TBC - Tab Clear (CSI g)
        // Ps = 0: Clear tab stop at current column (default)
        // Ps = 3: Clear all tab stops
        // Note: We use fixed tab stops, so this is acknowledged but has no effect
        // A full implementation would maintain a list of custom tab stops
        var mode = parameters.GetParam(0, 0);
        switch (mode)
        {
            case 0:
                // Clear current column tab stop - acknowledged but no action
                break;
            case 3:
                // Clear all tab stops - acknowledged but no action
                break;
        }
    }

    private void DeviceAttributes(Params parameters, bool isPrivate)
    {
        // DA - Device Attributes (CSI c or CSI > c)
        if (isPrivate)
        {
            // Secondary DA (CSI > c) - Report terminal ID and version
            // Response: CSI > 0 ; version ; 0 c
            // We report as VT100-compatible
            _terminal.RaiseDataReceived("\u001b[>0;10;0c");
        }
        else
        {
            // Primary DA (CSI c) - Report device attributes
            // Response: CSI ? 1 ; 2 c (VT100 with AVO)
            // More complete: CSI ? 1 ; 2 ; 6 ; 9 c
            // 1 = 132 columns, 2 = Printer, 6 = Selective erase, 9 = National replacement character sets
            //
            // Attribute 4 is Sixel graphics, and it is not decoration: libsixel, chafa, img2sixel
            // and everything built on them read this reply, and send text art instead of pictures
            // unless they see it. Claiming it while Sixel is switched off would be a lie in the
            // other direction, so it follows the option.
            _terminal.RaiseDataReceived(_terminal.Options.SixelEnabled
                ? "\u001b[?1;2;4c"
                : "\u001b[?1;2c");
        }
    }

    /// <summary>
    /// XTSMGRAPHICS -- CSI ? Pi ; Pa ; Pv S. Reports the terminal's graphics limits.
    /// </summary>
    /// <remarks>
    /// <para>This shares its final character with SCROLL UP, and <c>ToCsiCommand</c> strips the
    /// private marker before looking the command up, so until this existed a graphics query
    /// scrolled the screen instead of being answered. Every Sixel-capable program sends one during
    /// startup, which made the damage routine rather than obscure.</para>
    /// <para>Only the read operations are answered. The limits are fixed, so accepting a request
    /// to change them and quietly not doing it would be worse than refusing outright.</para>
    /// </remarks>
    private void GraphicsAttributes(Params parameters)
    {
        const int readAttribute = 1;
        const int readDefault = 2;
        const int readMaximum = 4;

        const int success = 0;
        const int badItem = 1;
        const int badAction = 2;

        var item = parameters.GetParam(0, 0);
        var action = parameters.GetParam(1, 0);
        var isRead = action == readAttribute || action == readDefault || action == readMaximum;

        switch (item)
        {
            case 1: // number of colour registers
                _terminal.RaiseDataReceived(isRead
                    ? $"\u001b[?1;{success};{Graphics.SixelPalette.RegisterCount}S"
                    : $"\u001b[?1;{badAction}S");
                break;

            case 2: // Sixel geometry
                if (isRead)
                {
                    // Reported as what MaxSixelPixels allows across the full terminal width, so a
                    // program that sizes an image to fit gets one we will not then throw away.
                    var width = Math.Max(1, _terminal.Cols * Math.Max(1, _terminal.Options.CellWidthPixels));
                    var height = Math.Max(1, _terminal.Options.MaxSixelPixels / width);
                    _terminal.RaiseDataReceived($"\u001b[?2;{success};{width};{height}S");
                }
                else
                {
                    _terminal.RaiseDataReceived($"\u001b[?2;{badAction}S");
                }
                break;

            default:
                _terminal.RaiseDataReceived($"\u001b[?{item};{badItem}S");
                break;
        }
    }
    private void DeviceStatusReport(Params parameters, bool isPrivate)
    {
        // DSR - Device Status Report (CSI n or CSI ? n)
        var report = parameters.GetParam(0, 0);

        if (isPrivate)
        {
            // DEC-specific DSR
            switch (report)
            {
                case 6: // DECXCPR - Extended Cursor Position Report
                    // Report cursor position: CSI ? row ; col R
                    var row = _buffer.Y + 1; // 1-based
                    var col = _buffer.X + 1; // 1-based
                    _terminal.RaiseDataReceived($"\u001b[?{row};{col}R");
                    break;

                case 15: // Printer status
                    // Report no printer: CSI ? 1 3 n
                    _terminal.RaiseDataReceived("\u001b[?13n");
                    break;

                case 25: // UDK status
                    // Report UDK locked: CSI ? 2 1 n
                    _terminal.RaiseDataReceived("\u001b[?21n");
                    break;

                case 26: // Keyboard status
                    // Report keyboard ready: CSI ? 2 7 ; 1 ; 0 ; 0 n
                    _terminal.RaiseDataReceived("\u001b[?27;1;0;0n");
                    break;
            }
        }
        else
        {
            // Standard ANSI DSR
            switch (report)
            {
                case 5: // Operating status
                    // Report OK: CSI 0 n
                    _terminal.RaiseDataReceived("\u001b[0n");
                    break;

                case 6: // CPR - Cursor Position Report
                    // Report cursor position: CSI row ; col R
                    var row = _buffer.Y + 1; // 1-based
                    var col = _buffer.X + 1; // 1-based

                    // Adjust for origin mode
                    if (_terminal.OriginMode)
                    {
                        row = row - _buffer.ScrollTop;
                    }

                    _terminal.RaiseDataReceived($"\u001b[{row};{col}R");
                    break;
            }
        }
    }

    private void CharAttributes(Params parameters)
    {
        if (parameters.Length == 0)
        {
            _curAttr = AttributeData.Default;
            return;
        }

        for (int i = 0; i < parameters.Length; i++)
        {
            var param = parameters.GetParam(i, 0);

            switch (param)
            {
                case 0: // Reset
                    _curAttr = AttributeData.Default;
                    break;
                case 1: // Bold
                    _curAttr.SetBold(true);
                    break;
                case 2: // Dim
                    _curAttr.SetDim(true);
                    break;
                case 3: // Italic
                    _curAttr.SetItalic(true);
                    break;
                case 21: // Double underline
                    _curAttr.SetUnderlineStyle(UnderlineStyle.Double);
                    break;
                case 4: // Underline, with an optional style as a sub-parameter
                    _curAttr.SetUnderlineStyle(ReadUnderlineStyle(parameters, i));
                    break;
                case 5: // Blink
                    _curAttr.SetBlink(true);
                    break;
                case 7: // Inverse
                    _curAttr.SetInverse(true);
                    break;
                case 8: // Invisible
                    _curAttr.SetInvisible(true);
                    break;
                case 9: // Strikethrough
                    _curAttr.SetStrikethrough(true);
                    break;
                case 22: // Not bold/dim
                    _curAttr.SetBold(false);
                    _curAttr.SetDim(false);
                    break;
                case 23: // Not italic
                    _curAttr.SetItalic(false);
                    break;
                case 24: // Not underline
                    _curAttr.SetUnderline(false);
                    break;
                case 25: // Not blink
                    _curAttr.SetBlink(false);
                    break;
                case 27: // Not inverse
                    _curAttr.SetInverse(false);
                    break;
                case 28: // Not invisible
                    _curAttr.SetInvisible(false);
                    break;
                case 29: // Not strikethrough
                    _curAttr.SetStrikethrough(false);
                    break;
                case >= 30 and <= 37: // Foreground color
                    _curAttr.SetFgColor(param - 30);
                    break;
                case 58: // Underline colour
                    i = HandleUnderlineColor(parameters, i);
                    break;
                case 59: // Underline colour back to the foreground
                    _curAttr.ResetUnderlineColor();
                    break;
                case 38: // Extended foreground color
                    i = HandleExtendedColor(parameters, i, true);
                    break;
                case 39: // Default foreground
                    _curAttr.SetFgColor(256);
                    break;
                case >= 40 and <= 47: // Background color
                    _curAttr.SetBgColor(param - 40);
                    break;
                case 48: // Extended background color
                    i = HandleExtendedColor(parameters, i, false);
                    break;
                case 49: // Default background
                    _curAttr.SetBgColor(257);
                    break;
                case >= 90 and <= 97: // Bright foreground color
                    _curAttr.SetFgColor(param - 90 + 8);
                    break;
                case >= 100 and <= 107: // Bright background color
                    _curAttr.SetBgColor(param - 100 + 8);
                    break;
            }
        }
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

    private int HandleExtendedColor(Params parameters, int index, bool isForeground)
    {
        if (index + 1 >= parameters.Length)
            return index;

        var colorType = parameters.GetParam(index + 1, 0);

        if (colorType == 2 && index + 4 < parameters.Length) // RGB
        {
            var r = parameters.GetParam(index + 2, 0);
            var g = parameters.GetParam(index + 3, 0);
            var b = parameters.GetParam(index + 4, 0);
            var rgb = (r << 16) | (g << 8) | b;

            if (isForeground)
                _curAttr.SetFgColor(rgb, 1);
            else
                _curAttr.SetBgColor(rgb, 1);

            return index + 4;
        }
        else if (colorType == 5 && index + 2 < parameters.Length) // 256 color
        {
            var color = parameters.GetParam(index + 2, 0);

            if (isForeground)
                _curAttr.SetFgColor(color);
            else
                _curAttr.SetBgColor(color);

            return index + 2;
        }

        return index;
    }

    private void SetScrollRegion(Params parameters)
    {
        var top = Math.Max(parameters.GetParam(0, 1), 1) - 1;
        var bottom = Math.Max(parameters.GetParam(1, _terminal.Rows), 1) - 1;
        _buffer.SetScrollRegion(top, bottom);
        MoveCursorToHome();
    }

    private int GetAbsoluteCursorRow(int row)
    {
        if (_terminal.OriginMode)
        {
            long absoluteRow = (long)_buffer.ScrollTop + row;
            return (int)Math.Clamp(absoluteRow, _buffer.ScrollTop, _buffer.ScrollBottom);
        }

        return Math.Clamp(row, 0, _terminal.Rows - 1);
    }

    private void MoveCursorToHome()
    {
        var row = _terminal.OriginMode ? _buffer.ScrollTop : 0;
        _buffer.SetCursor(0, row);
    }

    private void WindowManipulation(Params parameters)
    {
        // CSI Ps ; Ps ; Ps t - Window manipulation (XTWINOPS)
        // Check WindowOptions permissions before firing events
        var operation = parameters.GetParam(0, 0);

        switch (operation)
        {
            case 1: // De-iconify window (restore from minimized)
                if (_terminal.Options.WindowOptions.RestoreWin)
                {
                    _terminal.RaiseWindowRestored();
                }
                break;

            case 2: // Iconify window (minimize)
                if (_terminal.Options.WindowOptions.MinimizeWin)
                {
                    _terminal.RaiseWindowMinimized();
                }
                break;

            case 3: // Move window to x, y
                if (_terminal.Options.WindowOptions.SetWinPosition)
                {
                    var x = parameters.GetParam(1, 0);
                    var y = parameters.GetParam(2, 0);
                    _terminal.RaiseWindowMoved(x, y);
                }
                break;

            case 4: // Resize window to height, width pixels
                if (_terminal.Options.WindowOptions.SetWinSizePixels)
                {
                    var height = parameters.GetParam(1, 0);
                    var width = parameters.GetParam(2, 0);
                    _terminal.RaiseWindowResized(width, height);
                }
                break;

            case 5: // Raise window to front
                if (_terminal.Options.WindowOptions.RaiseWin)
                {
                    _terminal.RaiseWindowRaised();
                }
                break;

            case 6: // Lower window to back
                if (_terminal.Options.WindowOptions.LowerWin)
                {
                    _terminal.RaiseWindowLowered();
                }
                break;

            case 7: // Refresh window
                if (_terminal.Options.WindowOptions.RefreshWin)
                {
                    _terminal.RaiseWindowRefreshed();
                }
                break;

            case 8: // Resize text area to height, width characters
                if (_terminal.Options.WindowOptions.SetWinSizeChars)
                {
                    var rows = parameters.GetParam(1, 0);
                    var cols = parameters.GetParam(2, 0);
                    if (rows > 0 && cols > 0)
                    {
                        _terminal.Resize(cols, rows);
                    }
                }
                break;

            case 9: // Maximize/restore operations
                var subOp = parameters.GetParam(1, 0);
                if (subOp == 0 && _terminal.Options.WindowOptions.RestoreWin)
                {
                    // Restore maximized window
                    _terminal.RaiseWindowRestored();
                }
                else if (subOp == 1 && _terminal.Options.WindowOptions.MaximizeWin)
                {
                    // Maximize window
                    _terminal.RaiseWindowMaximized();
                }
                break;

            case 10: // Full-screen operations
                subOp = parameters.GetParam(1, 0);
                if (subOp == 0 && _terminal.Options.WindowOptions.FullscreenWin)
                {
                    // Exit full-screen
                    _terminal.RaiseWindowFullscreened();
                }
                else if (subOp == 1 && _terminal.Options.WindowOptions.FullscreenWin)
                {
                    // Enter full-screen
                    _terminal.RaiseWindowFullscreened();
                }
                else if (subOp == 2 && _terminal.Options.WindowOptions.FullscreenWin)
                {
                    // Toggle full-screen
                    _terminal.RaiseWindowFullscreened();
                }
                break;

            case 11: // Report window state (iconified or not)
                if (_terminal.Options.WindowOptions.GetWinState)
                {
                    var args = _terminal.RaiseWindowInfoRequested(WindowInfoRequest.State);
                    if (args.Handled)
                    {
                        // Response: CSI 1 t (not iconified) or CSI 2 t (iconified)
                        var stateCode = args.IsIconified ? 2 : 1;
                        _terminal.RaiseDataReceived($"\u001b[{stateCode}t");
                    }
                }
                break;

            case 13: // Report window position
                if (_terminal.Options.WindowOptions.GetWinPosition)
                {
                    var args = _terminal.RaiseWindowInfoRequested(WindowInfoRequest.Position);
                    if (args.Handled)
                    {
                        // Response: CSI 3 ; x ; y t
                        _terminal.RaiseDataReceived($"\u001b[3;{args.X};{args.Y}t");
                    }
                }
                break;

            case 14: // Report window size in pixels
                if (_terminal.Options.WindowOptions.GetWinSizePixels)
                {
                    var args = _terminal.RaiseWindowInfoRequested(WindowInfoRequest.SizePixels);
                    if (args.Handled)
                    {
                        // Response: CSI 4 ; height ; width t
                        _terminal.RaiseDataReceived($"\u001b[4;{args.HeightPixels};{args.WidthPixels}t");
                    }
                }
                break;

            case 15: // Report screen size in pixels
                if (_terminal.Options.WindowOptions.GetScreenSizePixels)
                {
                    var args = _terminal.RaiseWindowInfoRequested(WindowInfoRequest.ScreenSizePixels);
                    if (args.Handled)
                    {
                        // Response: CSI 5 ; height ; width t
                        _terminal.RaiseDataReceived($"\u001b[5;{args.HeightPixels};{args.WidthPixels}t");
                    }
                }
                break;

            case 16: // Report character cell size in pixels
                if (_terminal.Options.WindowOptions.GetCellSizePixels)
                {
                    var args = _terminal.RaiseWindowInfoRequested(WindowInfoRequest.CellSizePixels);
                    if (args.Handled)
                    {
                        // Response: CSI 6 ; height ; width t
                        _terminal.RaiseDataReceived($"\u001b[6;{args.CellHeight};{args.CellWidth}t");
                    }
                }
                break;

            case 18: // Report text area size in characters
                if (_terminal.Options.WindowOptions.GetWinSizeChars)
                {
                    // Response: CSI 8 ; rows ; cols t
                    _terminal.RaiseDataReceived($"\u001b[8;{_terminal.Rows};{_terminal.Cols}t");
                }
                break;

            case 19: // Report screen size in characters
                if (_terminal.Options.WindowOptions.GetScreenSizePixels)
                {
                    // This is typically the same as window size for terminal apps
                    _terminal.RaiseDataReceived($"\u001b[9;{_terminal.Rows};{_terminal.Cols}t");
                }
                break;

            case 20: // Report icon label
                if (_terminal.Options.WindowOptions.GetIconTitle)
                {
                    var args = _terminal.RaiseWindowInfoRequested(WindowInfoRequest.IconTitle);
                    if (args.Handled && args.Title != null)
                    {
                        // Response: OSC L label ST
                        _terminal.RaiseDataReceived($"\u001b]L{args.Title}\u0007");
                    }
                }
                break;

            case 21: // Report window title
                if (_terminal.Options.WindowOptions.GetWinTitle)
                {
                    // Response: OSC l title ST - use the terminal's current title
                    var title = _terminal.Title ?? string.Empty;
                    _terminal.RaiseDataReceived($"\u001b]l{title}\u0007");
                }
                break;

            case 22: // Save window title
                // Push title onto stack (not typically implemented)
                break;

            case 23: // Restore window title
                // Pop title from stack (not typically implemented)
                break;
        }
    }

    /// <summary>
    /// DECRQM — reports whether a mode is recognised and what it is set to.
    /// </summary>
    /// <remarks>
    /// <para>This is how an application finds out whether synchronized output is worth using: it
    /// asks, and a terminal that says nothing is one that does not support the query. Emitting the
    /// mode without answering for it would leave well-behaved applications never using it.</para>
    /// <para>Deliberately answers for 2026 alone. The reply codes distinguish "set" and "reset" from
    /// "not recognised", and this terminal keeps mode state as individual properties rather than a
    /// registry — so answering for everything would mean a switch mapping every mode back to its
    /// property, and getting one wrong tells an application a feature is missing when it is not.
    /// Staying silent for the rest is exactly the behaviour before this change, so nothing regresses
    /// while the one mode that needs an answer gets a correct one.</para>
    /// </remarks>
    private void HandleRequestMode(Params parameters, bool isPrivate)
    {
        if (!isPrivate)
            return;

        var mode = parameters.GetParam(0, 0);
        if (mode != (int)TerminalMode.SynchronizedOutput)
            return;

        // DECRPM: 1 = set, 2 = reset.
        var state = _terminal.SynchronizedOutput ? 1 : 2;
        _terminal.RaiseDataReceived($"\u001b[?{mode};{state}$y");
    }

    private void SetCSIModeParameters(Params parameters, bool isPrivate)
    {
        for (int i = 0; i < parameters.Length; i++)
        {
            var mode = parameters.GetParam(i, 0);
            SetCSIMode(mode, isPrivate: isPrivate);
        }
    }

    private void SetCSIMode(int mode, bool isPrivate)
    {
        if (isPrivate)
        {
            // DEC Private Modes (DECSET)
            // Convert int to TerminalMode enum
            if (!Enum.IsDefined(typeof(TerminalMode), mode))
            {
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
                    // Mode 4: In DEC private mode context, this is SmoothScroll (DECSCLM)
                    // InsertMode and SmoothScroll share value 4 in the enum
                    // Smooth scroll is acknowledged but has no effect in modern terminals
                    break;

                case TerminalMode.ReverseVideo:
                    _terminal.ReverseVideo = true;
                    break;

                case TerminalMode.Origin:
                    _terminal.OriginMode = true;
                    MoveCursorToHome();
                    break;

                case TerminalMode.Wraparound:
                    // Mode 7: Wraparound mode
                    // Wraparound and AutoWrapMode share value 7 in the enum
                    _terminal.Options.Wraparound = true;
                    break;

                case TerminalMode.AutoRepeat:
                    // Auto repeat is typically always enabled in modern terminals
                    // This mode is acknowledged but has no effect
                    break;

                case TerminalMode.ShowCursor:
                    _terminal.CursorVisible = true;
                    break;

                case TerminalMode.NationalCharset:
                    // National replacement character set mode
                    // Acknowledged but typically no specific action needed for modern use
                    break;

                case TerminalMode.ReverseWraparound:
                    _terminal.ReverseWraparound = true;
                    break;

                case TerminalMode.AppKeypad:
                    _terminal.ApplicationKeypad = true;
                    break;

                case TerminalMode.SynchronizedOutput:
                    _terminal.RaiseSynchronizedOutputChanged(true);
                    break;

                case TerminalMode.BracketedPasteMode:
                    _terminal.BracketedPasteMode = true;
                    break;

                case TerminalMode.AltBuffer:
                    _terminal.SwitchToAltBuffer();
                    break;

                case TerminalMode.AltBufferCursor:
                    SaveCursor();
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
                    System.Diagnostics.Debug.WriteLine($"Unhandled CSI private terminal mode: {terminalMode}");
                    break;
            }
        }
        else
        {
            // ANSI Modes (SM)
            if (!Enum.IsDefined(typeof(TerminalMode), mode))
            {
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
                    System.Diagnostics.Debug.WriteLine($"Unhandled CSI terminal mode: {terminalMode}");
                    break;
            }
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

                case TerminalMode.BracketedPasteMode:
                    _terminal.BracketedPasteMode = false;
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

    // ESC Handler Implementations

    private void IndexDown()
    {
        if (_buffer.Y == _buffer.ScrollBottom)
        {
            _buffer.ScrollUp(1);
        }
        else
        {
            _buffer.SetCursor(_buffer.X, _buffer.Y + 1);
        }
    }

    private void NextLine()
    {
        IndexDown();
        _buffer.SetCursor(0, _buffer.Y);
    }

    private void ReverseIndex()
    {
        if (_buffer.Y == _buffer.ScrollTop)
        {
            _buffer.ScrollDown(1);
        }
        else
        {
            _buffer.SetCursor(_buffer.X, _buffer.Y - 1);
        }
    }

    private void ResetTerminal()
    {
        _terminal.Reset();
    }

    private void SelectCursorStyle(Params parameters)
    {
        // DECSCUSR - Select Cursor Style (CSI Ps SP q)
        var ps = parameters.GetParam(0, 1);

        CursorStyle style;
        bool blink;

        switch (ps)
        {
            case 0:
            case 1:
                style = CursorStyle.Block;
                blink = true;
                break;
            case 2:
                style = CursorStyle.Block;
                blink = false;
                break;
            case 3:
                style = CursorStyle.Underline;
                blink = true;
                break;
            case 4:
                style = CursorStyle.Underline;
                blink = false;
                break;
            case 5:
                style = CursorStyle.Bar;
                blink = true;
                break;
            case 6:
                style = CursorStyle.Bar;
                blink = false;
                break;
            default:
                // Unsupported value - ignore
                return;
        }

        _terminal.SetCursorStyle(style, blink);
    }

    private void SaveCursor()
    {
        _buffer.SavedCursorState.X = _buffer.X;
        _buffer.SavedCursorState.Y = _buffer.Y;
        _buffer.SavedCursorState.Attr = _curAttr;
    }

    private void RestoreCursor()
    {
        _buffer.SetCursor(_buffer.SavedCursorState.X, _buffer.SavedCursorState.Y);
        _curAttr = _buffer.SavedCursorState.Attr;
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
        //   - ZWJ and the object replacement character subtract lastWidth, which is 0, yielding 0.
        //
        // Which leaves: plain width for everything except those last two, and the control-character
        // handling for negative widths.
        if (text.Length == 1)
        {
            var c = text[0];

            if (c >= 0x20 && c < 0x7F)
                return 1;

            if (!char.IsSurrogate(c))
            {
                if (c == Emoji.ZeroWidthJoiner || c == Emoji.ObjectReplacementCharacter)
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
                if (rune.Value == Emoji.ZeroWidthJoiner || rune.Value == Emoji.ObjectReplacementCharacter)
                {
                    if (!supportsComplexEmoji)
                        // we return the first emoji as the result because terminal doesn't support chaining them
                        break;

                    if (lastWidth > 0)
                        // It joins the glyph before it, which has already been counted.
                        width -= lastWidth;
                    else
                        // Nothing in front of it to join, so it stands on its own. Subtracting unconditionally
                        // left a lone U+FFFC measuring 0, and a character measuring 0 does not move the
                        // cursor — so whatever came next printed over the top of it. ZWJ passes through here
                        // too and is unaffected, being genuinely zero-width in its own right.
                        width += (ushort)runeWidth;
                }
                else if (rune.Value == Codepoints.VariationSelectors.EmojiSymbol &&
                         lastWidth == 1)
                {
                    // adjust for the emoji presentation, which is width 2
                    width++;
                    lastWidth = 2;
                }
                else if (rune.Value == Codepoints.VariationSelectors.TextSymbol &&
                         lastWidth == 2)
                {
                    // adjust for the text presentation, which is width 1
                    width--;
                    lastWidth = 1;
                }
                else if (lastWidth > 0 &&
                         (rune.Value >= Emoji.SkinTones.Light && rune.Value <= Emoji.SkinTones.Dark ||
                          rune.Value == Codepoints.Keycap))
                {
                    // Emoji modifier (skin tone) or keycap extender should continue current glyph

                    // else: combining � ignore
                }
                else if (rune.Value >= Emoji.SkinTones.Light && rune.Value <= Emoji.SkinTones.Dark)
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

        return width;
    }

    public void SetBuffer(Buffer.TerminalBuffer buffer)
    {
        _buffer = buffer;
    }
}
