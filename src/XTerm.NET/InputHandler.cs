using NeoSmart.Unicode;
using System.Globalization;
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
        // Nothing below U+0300 combines — the marks begin at COMBINING GRAVE ACCENT — so ASCII
        // and Latin-1, the overwhelming majority of every stream, leave in one compare instead
        // of reaching the category lookup. Three bench runs put that lookup at ~4% on ASCII-
        // and CJK-heavy corpora; this is the NoteLinkRun lesson wearing yet another coat.
        if (codePoint < 0x0300)
            return false;

        // Variation Selectors (U+FE00�U+FE0F)
        if (codePoint >= 0xFE00 && codePoint <= 0xFE0F)
            return true;

        // Variation Selectors Supplement (U+E0100�U+E01EF)
        if (codePoint >= 0xE0100 && codePoint <= 0xE01EF)
            return true;

        // Zero Width Joiner (U+200D)
        if (codePoint == ZeroWidthJoiner)
            return true;

        // Emoji Modifiers / Skin Tones (U+1F3FB..U+1F3FF)
        //
        // Combining is not decided here alone: a skin tone modifies an EMOJI, and TryAppendToPreviousCell
        // checks what it is being asked to attach to. Saying yes unconditionally glued a modifier onto
        // whatever happened to precede it — "║🏼║" put the tone inside the box-drawing character and drew
        // the pair as one unreadable cell, where every other terminal shows a swatch standing on its own.
        if (IsSkinToneModifier(codePoint))
            return true;

        // Unicode marks in every script, including astral codepoints. SpacingCombiningMark sounds
        // as though it should advance the cursor, but terminal wcwidth implementations conventionally
        // give Indic matras in that category width zero and keep them in their base character's cell.
        return CharUnicodeInfo.GetUnicodeCategory(codePoint) is UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.EnclosingMark;
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

    /// <summary>GB6, GB7 and GB8: which jamo classes continue the cluster ending in which.</summary>
    private static bool HangulJoins(int previousClass, int currentClass) => currentClass switch
    {
        1 or 4 or 5 => previousClass is 1,                 // GB6: L x (L | V | LV | LVT)
        2 => previousClass is 1 or 2 or 4,                 // GB6/GB7: (L | V | LV) x V
        3 => previousClass is 2 or 3 or 4 or 5,            // GB7/GB8: (V | LV | T | LVT) x T
        _ => false,
    };

    /// <summary>
    /// The conjunct linkers of UAX #29's GB9c — Unicode's InCB=Linker set, the eight viramas of
    /// the scripts whose conjuncts must not break: Devanagari through Malayalam.
    /// </summary>
    private static bool IsConjunctLinker(int codePoint) =>
        codePoint is 0x094D or 0x09CD or 0x0A4D or 0x0ACD or 0x0B4D or 0x0C4D or 0x0CCD or 0x0D4D;

    /// <summary>
    /// A letter that can be the consonant on GB9c's right-hand side: an Lo in the blocks the
    /// linker set serves. An approximation of InCB=Consonant — exactness needs the property
    /// data — that over-accepts only sequences (virama then independent vowel) which are
    /// malformed in the scripts themselves.
    /// </summary>
    private static bool IsConjunctConsonantCandidate(int codePoint) =>
        codePoint >= 0x0900 && codePoint <= 0x0D7F
        && System.Globalization.CharUnicodeInfo.GetUnicodeCategory(codePoint)
            == System.Globalization.UnicodeCategory.OtherLetter;

    /// <summary>
    /// Whether this codepoint might continue the previous cell's cluster under the SEQUENCE
    /// rules — the ones a per-codepoint category cannot express. Cheap by design: two range
    /// tests on the current codepoint; the contextual half of the decision lives in
    /// TryAppendToPreviousCell, which can see the previous cell and refuses mismatches, sending
    /// the character back to an ordinary cell of its own.
    /// </summary>
    private static bool IsSequenceJoinCandidate(int codePoint)
    {
        // Ordered BANDS, cheapest rejection first, because this runs once per printed character
        // on the non-ASCII path and the bench charged the naive pair of class tests 5% on the
        // unicode corpus: CJK text — the bulk of that corpus — paid every Hangul range and the
        // Indic bracket to learn it was neither. The bands send Latin through one compare and
        // CJK through four.
        // One unsigned bracket ejects BOTH ends — Latin below, CJK-adjacent astral emoji above.
        // Every candidate lives in [U+0900, U+D7FB]; emoji walked the whole ladder without this.
        if ((uint)(codePoint - 0x0900) > 0xD7FB - 0x0900)
            return false;
        if (codePoint <= 0x0D7F)
            return IsConjunctConsonantCandidate(codePoint);      // the eight linker scripts
        if (codePoint < 0x1100)
            return false;                                        // Sinhala through Tibetan
        if (codePoint <= 0x11FF)
            return true;                                         // jamo L, V, T
        if (codePoint < 0xA960)
            return false;                                        // CJK exits here
        if (codePoint <= 0xA97C)
            return true;                                         // jamo extended-A
        if (codePoint < 0xAC00)
            return false;
        if (codePoint <= 0xD7A3)
            return true;                                         // precomposed syllables
        return codePoint is >= 0xD7B0 and <= 0xD7C6 or >= 0xD7CB and <= 0xD7FB;
    }

    /// <summary>
    /// One bit per BMP codepoint: might it join the previous cell's cluster — the category rules
    /// or the sequence rules. Built ONCE from the reference predicates below, so the table cannot
    /// drift from them; it exists because the hot path must not pay a category lookup per
    /// character, and the corpora that hurt were exactly the ones full of characters no range
    /// check anticipates — box drawing in TUI redraws, CJK in prose. 8KB, cold half never touched.
    /// </summary>
    private static readonly byte[] MayJoinBmp = BuildMayJoinBmp();

    private static byte[] BuildMayJoinBmp()
    {
        var table = new byte[0x10000 >> 3];
        for (var codePoint = 0x0300; codePoint < 0x10000; codePoint++)
        {
            if (IsCombiningCharacter(codePoint) || IsSequenceJoinCandidate(codePoint))
                table[codePoint >> 3] |= (byte)(1 << (codePoint & 7));
        }
        return table;
    }

    /// <summary>
    /// Whether this codepoint might join the previous cell at all. Print guards the call with an
    /// inline <c>codePoint &gt;= 0x0300</c> so the ASCII majority never gets here; the BMP —
    /// every character whose answer is not obvious from its plane — is one load and a mask, and
    /// only astral codepoints (emoji machinery, already handled by the checks' cheap heads) run
    /// the predicates directly.
    /// </summary>
    private static bool MayContinueCluster(int codePoint) =>
        codePoint < 0x10000
            ? (MayJoinBmp[codePoint >> 3] & (1 << (codePoint & 7))) != 0
            : IsCombiningCharacter(codePoint) || IsSequenceJoinCandidate(codePoint);

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

    /// <summary>The rune before the last, for GB9c's linker-then-ZWJ-then-consonant form.</summary>
    private static int RuneBeforeLastOf(string? content)
    {
        if (string.IsNullOrEmpty(content))
            return 0;

        int beforeLast = 0, last = 0;
        foreach (var rune in content.EnumerateRunes())
        {
            beforeLast = last;
            last = rune.Value;
        }

        return beforeLast;
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

            // Guarded at the CALL, like the placeholder test above and for the same measured
            // reason: nothing below U+0300 can combine OR continue a sequence, so ASCII — most of
            // every frame — pays one inline compare here instead of two real calls answering no.
            if (continuesCluster || (codePoint >= 0x0300 && MayContinueCluster(codePoint)))
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


        // Handle autowrap. The wrap TEST stays inline: it runs once per printed character, and
        // hiding it inside ResolveAutowrap cost alt-redraw 9% in method-call overhead -- the same
        // lesson NoteLinkRun's guard learned. The method only runs when a wrap is actually due.
        if (_buffer.X > WrapLimit() && !ResolveAutowrap())
            return; // Don't print beyond line edge

        // Translate character through active charset
        var translatedData = data;
        if (data.Length == 1)
        {
            translatedData = Charsets.TranslateChar(data[0], _activeCharset);
        }

        // Get character width
        var width = GetStringCellWidth(translatedData);

        // A cell belonging to a scaled block anchored on an earlier row is not written into: the
        // cursor moves past the block's cells on this row and the text lands after them. Known
        // before the line is fetched, because skipping can wrap onto another row. One field read for
        // every session that has never seen an OSC 66 block, which is nearly all of them.
        if (_buffer.HasMultiRowSizedRuns && !SkipCellsCoveredFromAbove(width))
            return;

        var line = _buffer.Lines[_buffer.Y + _buffer.YBase];
        if (line == null)
            return;

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
            // The cells of a scaled block from here rightwards are about to move, and the run saying
            // which columns hold which part of it would not move with them.
            if (line is not null && line.HasSizedRuns)
                line.EraseSizedRunsFrom(_buffer.X);

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
            // Set following cell as a spacer, bounded by the right MARGIN rather than the screen --
            // otherwise a double-width character sitting on the last column of a region plants its
            // spacer in the pane next door. Identical to the old test when no margins are set, since
            // the limit is then the last column.
            if (_buffer.X + 1 <= WrapLimit())
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

        // Ordinary text written over a scaled run destroys it, which the protocol requires: a
        // multicell block cannot survive having part of itself overwritten. Guarded at the call for
        // the same reason as the link bookkeeping above -- a line with no sized run pays one read.
        if (line is not null && line.HasSizedRuns)
            line.NoteSizedRun(_buffer.X, width, TextSizing.Default);

        // Use MoveCursor to allow X to be one past the last column (pending wrap)
        _buffer.SetCursorRaw(_buffer.X + width, _buffer.Y);

        RememberForRepeat(cell.CodePoint, cell.ClusterId);
    }

    /// <summary>
    /// Resolves a cursor resting one past the last usable column, which is where printing leaves it.
    /// </summary>
    /// <returns>
    /// False when there is nowhere to print: the cursor is past the edge and DECAWM is off, so the
    /// character is discarded.
    /// </returns>
    private bool ResolveAutowrap()
    {
        if (_buffer.X <= WrapLimit())
            return true;

        if (!_terminal.Options.Wraparound)
            return false;

        // Only a FULL-WIDTH wrap marks the next line as a continuation. IsWrapped is a per-line
        // flag, and a wrap inside the margin box continues the box, not the line: content outside
        // the margins on the next row was never part of this text, and a reflow that believed the
        // flag would merge lines an application laid out separately. Decided before the cursor
        // moves, because the answer depends on where the wrap happened.
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

        return true;
    }

    /// <summary>
    /// Moves the cursor past any OSC 66 block that covers it from an earlier row.
    /// </summary>
    /// <remarks>
    /// <para>A block <c>s</c> cells tall occupies <c>s</c> rows. Its first row is ordinary content
    /// and writing there destroys the block, but the rows BELOW belong to it while it lives, and the
    /// protocol says what happens to text aimed at them: the cursor is moved past the block's cells
    /// on that row and the text is written after them. That is the rule that lets a client keep
    /// printing normally underneath a heading instead of having to count rows.</para>
    /// <para>Skipping happens whatever DECAWM says, which the protocol states explicitly. Reaching
    /// the end of the line while skipping is an ordinary end of line, so wrapping is resolved the
    /// usual way and the search continues on the row it lands on -- a block on the next row down can
    /// cover the column it wrapped to.</para>
    /// </remarks>
    /// <param name="width">
    /// How many columns the caller is about to write. The rule is about the cells the text will
    /// overwrite, so a double-width character or a block of its own must clear the whole span it
    /// covers -- checking only the cursor cell would let its right half land inside a block.
    /// </param>
    /// <returns>False when there is nowhere left to write.</returns>
    private bool SkipCellsCoveredFromAbove(int width)
    {
        // Bounded rather than "until clear": every pass moves the cursor strictly forwards, so the
        // loop terminates on its own, but a bound cannot be the thing that hangs a terminal on
        // hostile input. Generous enough that legal content cannot reach it -- a row holds at most
        // Cols blocks and a skip can wrap onto a new row -- because giving up mid-skip would write
        // into a covered cell.
        var guard = (_terminal.Cols + 1) * TextSizing.MaxScale;
        while (guard-- > 0)
        {
            if (!ResolveAutowrap())
                return false;

            var row = _buffer.Y + _buffer.YBase;
            var end = Math.Min(_buffer.X + Math.Max(width, 1), _terminal.Cols);

            LineSizedRun covering = default;
            var found = false;
            for (var column = _buffer.X; column < end; column++)
            {
                if (_buffer.TryGetSizedRunCovering(row, column, out var run, out _))
                {
                    covering = run;
                    found = true;
                    break;
                }
            }

            if (!found)
                return true;

            _buffer.SetCursorRaw(Math.Min(covering.EndColumn, _terminal.Cols), _buffer.Y);
        }

        return false;
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
        // A multi-row block means cells that must be skipped rather than written, which a span write
        // cannot express -- so the run goes character by character, as it already does for insert
        // mode and for a designated charset.
        if (!UseRunPrinting || _terminal.InsertMode || _activeCharset is not null || _buffer.HasMultiRowSizedRuns)
        {
            foreach (var b in data)
                Print(CodePointText.Get((char)b));
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
        if (!UseRunPrinting || _terminal.InsertMode || _activeCharset is not null || _buffer.HasMultiRowSizedRuns)
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
        // following a complete syllable should get.
        var hangulClass = HangulClassOf(codePoint);
        if (hangulClass != 0)
        {
            if (!HangulJoins(HangulClassOf(LastRuneOf(prevCell.Content)), hangulClass))
                return false;
        }
        else if (IsConjunctConsonantCandidate(codePoint) && !IsCombiningCharacter(codePoint))
        {
            // GB9c: the consonant joins when the cluster ends with a linker — or with a ZWJ the
            // linker precedes, the explicit-conjunct form. Anything else is a new cluster.
            var last = LastRuneOf(prevCell.Content);
            if (!IsConjunctLinker(last)
                && !(last == ZeroWidthJoiner && IsConjunctLinker(RuneBeforeLastOf(prevCell.Content))))
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
    /// Serialises the current character attributes as a semicolon-separated SGR parameter string,
    /// suitable for embedding in a DECRQSS <c>m</c> response.
    /// </summary>
    /// <remarks>
    /// Every code emitted here is one this handler parses back, so a program can read the reply,
    /// replay it, and land on the attributes it started from — which is the point of asking. What
    /// is off, and a colour that is still the default, is left out; nothing at all reads as
    /// <c>0</c>, the reset.
    /// </remarks>
    private string SerializeSgr()
    {
        var attr = _curAttr;

        // Build a list of SGR code fragments. Each may be a single number ("1") or a
        // semicolon-separated run ("38;2;255;128;0").
        var parts = new List<string>(8);

        if (attr.IsBold()) parts.Add("1");
        if (attr.IsDim()) parts.Add("2");
        if (attr.IsItalic()) parts.Add("3");

        switch (attr.GetUnderlineStyle())
        {
            case UnderlineStyle.Single: parts.Add("4"); break;
            case UnderlineStyle.Double: parts.Add("21"); break;
            case UnderlineStyle.Curly: parts.Add("4:3"); break;
            case UnderlineStyle.Dotted: parts.Add("4:4"); break;
            case UnderlineStyle.Dashed: parts.Add("4:5"); break;
        }

        if (attr.IsBlink()) parts.Add("5");
        if (attr.IsInverse()) parts.Add("7");
        if (attr.IsInvisible()) parts.Add("8");
        if (attr.IsStrikethrough()) parts.Add("9");
        if (attr.IsOverline()) parts.Add("53");

        // Foreground colour
        var fgMode = attr.GetFgColorMode();
        var fg = attr.GetFgColor();
        if (fgMode == 1)
        {
            // RGB truecolor
            parts.Add($"38;2;{(fg >> 16) & 0xFF};{(fg >> 8) & 0xFF};{fg & 0xFF}");
        }
        else if (fg <= 7)
        {
            parts.Add($"{30 + fg}");
        }
        else if (fg <= 15)
        {
            parts.Add($"{90 + fg - 8}");
        }
        else if (fg <= 255)
        {
            parts.Add($"38;5;{fg}");
        }
        // 256 (default fg) → omit

        // Background colour
        var bgMode = attr.GetBgColorMode();
        var bg = attr.GetBgColor();
        if (bgMode == 1)
        {
            // RGB truecolor
            parts.Add($"48;2;{(bg >> 16) & 0xFF};{(bg >> 8) & 0xFF};{bg & 0xFF}");
        }
        else if (bg <= 7)
        {
            parts.Add($"{40 + bg}");
        }
        else if (bg <= 15)
        {
            parts.Add($"{100 + bg - 8}");
        }
        else if (bg <= 255)
        {
            parts.Add($"48;5;{bg}");
        }
        // 257 (default bg) → omit

        return parts.Count == 0 ? "0" : string.Join(";", parts);
    }

    /// <summary>
    /// Serialises the current cursor style as the numeric DECSCUSR parameter.
    /// </summary>
    private string SerializeDecscusr()
    {
        var style = _terminal.Options.CursorStyle;
        var blink = _terminal.Options.CursorBlink;
        return (style, blink) switch
        {
            (CursorStyle.Block, true) => "1",
            (CursorStyle.Block, false) => "2",
            (CursorStyle.Underline, true) => "3",
            (CursorStyle.Underline, false) => "4",
            (CursorStyle.Bar, true) => "5",
            (CursorStyle.Bar, false) => "6",
            _ => "0",
        };
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
    /// Handles the useful iTerm2 OSC 1337 extensions. Unknown extension keys are intentionally
    /// ignored, matching iTerm2's permissive extension namespace.
    /// </summary>
    private bool HandleITerm2(string data)
    {
        var separator = data.IndexOf('=');
        if (separator == 0)
            return false;

        var key = separator < 0 ? data : data[..separator];
        var value = separator < 0 ? string.Empty : data[(separator + 1)..];
        switch (key)
        {
            case "File":
                return HandleITerm2File(value);

            case "SetUserVar":
                return HandleITerm2UserVariable(value);

            case "CurrentDir":
                HandleITerm2CurrentDirectory(value);
                return true;

            case "ShellIntegrationVersion":
                _terminal.ShellIntegrationVersion = value;
                return true;

            case "RemoteHost":
                _terminal.RemoteHost = value;
                return true;

            case "StealFocus":
                if (_terminal.Options.WindowOptions.RaiseWin)
                    _terminal.RaiseWindowRaised();
                return _terminal.Options.WindowOptions.RaiseWin;

            case "RequestAttention":
                if (_terminal.Options.WindowOptions.RequestAttention)
                    _terminal.RaiseAttentionRequested(value);
                return _terminal.Options.WindowOptions.RequestAttention;

            case "ReportCellSize":
                if (!_terminal.Options.WindowOptions.GetCellSizePixels)
                    return false;
                // iTerm2 defines the first two fields as floating-point sizes in POINTS with an
                // optional pixels-per-point scale — reporting physical pixels as points reads
                // double on a Retina display. The host supplies DisplayScale alongside the pixel
                // metrics; at the default 1.0 the numbers are unchanged.
                var cellScale = Math.Max(1.0, _terminal.Options.DisplayScale);
                var cellHeightPoints = _terminal.Options.CellHeightPixels / cellScale;
                var cellWidthPoints = _terminal.Options.CellWidthPixels / cellScale;
                _terminal.RaiseDataReceived(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"\u001b]1337;ReportCellSize={cellHeightPoints:0.0###};{cellWidthPoints:0.0###};{cellScale:0.0###}\u001b\\"));
                return true;

            default:
                return false;
        }
    }

    private bool HandleITerm2File(string data)
    {
        // Only PNG at its natural size is supported. Sized and non-PNG File payloads remain
        // unrecognized so a host can implement iTerm2's wider image-format and sizing surface.
        if (!_terminal.Options.ITerm2ImagesEnabled)
            return false;

        var separator = data.IndexOf(':');
        if (separator < 0)
            return false;

        var parameters = data[..separator].Split(';');
        if (!parameters.Contains("inline=1") || parameters.Any(p => p.StartsWith("width=") || p.StartsWith("height=")))
            return false;

        var payload = data[(separator + 1)..];

        // Bounded BEFORE decoding: FromBase64String materialises the whole decoded payload, so
        // without this a very large valid-base64 blob forces the allocation first and gets
        // rejected after. The registry budget is the natural ceiling — an image whose COMPRESSED
        // form already exceeds what the registry would hold has no chance of being kept.
        if (_terminal.Options.MaxImageRegistryBytes > 0
            && (long)payload.Length > _terminal.Options.MaxImageRegistryBytes / 3 * 4 + 4)
            return false;

        byte[] encoded;
        try
        {
            encoded = Convert.FromBase64String(payload);
        }
        catch (FormatException)
        {
            return false;
        }

        if (!Graphics.PngDecoder.TryDecode(encoded, _terminal.Options.MaxSixelPixels,
                                           out var pixels, out var width, out var height))
            return false;
        if (_terminal.Options.MaxImageRegistryBytes > 0
            && pixels.LongLength > _terminal.Options.MaxImageRegistryBytes)
            return false;

        var image = new Graphics.TerminalImage(
            pixels, width, height,
            Math.Max(1, _terminal.Options.CellWidthPixels),
            Math.Max(1, _terminal.Options.CellHeightPixels));
        PlaceImage(Graphics.ImagePlacement.Natural(image), Graphics.PlacementKind.Sixel);
        return true;
    }

    private bool HandleITerm2UserVariable(string data)
    {
        var separator = data.IndexOf('=');
        if (separator < 1)
            return false;

        try
        {
            var encoded = Convert.FromBase64String(data[(separator + 1)..]);
            if (encoded.Length > _terminal.Options.MaxUserVariableBytes)
                return false;

            return _terminal.TrySetUserVariable(
                data[..separator],
                new System.Text.UTF8Encoding(false, true).GetString(encoded));
        }
        catch (ArgumentException)
        {
            // Invalid base64 or UTF-8 is untrusted terminal output, so ignore it.
            return false;
        }
    }

    private void HandleITerm2CurrentDirectory(string data)
    {
        if (data.StartsWith("file://"))
        {
            HandleCurrentDirectory(data);
            return;
        }

        if (!string.IsNullOrEmpty(data))
        {
            _terminal.CurrentDirectory = data;
            _terminal.RaiseDirectoryChanged(_terminal.CurrentDirectory);
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
                    // The spec defines exactly 0 (low), 1 (normal) and 2 (critical); anything
                    // else reads as unspecified, so a host can map the value onto its
                    // notification API without range-checking a protocol it did not parse.
                    if (int.TryParse(keyValue[1], out var parsedUrgency) && parsedUrgency is >= 0 and <= 2)
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
            // "If a notification has no title, the body will be used as title" — the spec's own
            // sentence, honoured here so every host does not rediscover it, and so a host that
            // hands Title to an OS API requiring one never gets null with content present.
            if (title is null && body is not null)
            {
                title = body;
                body = null;
            }
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
    /// Handles the Kitty text sizing protocol: <c>OSC 66 ; key=value : ... ; text ST</c>.
    /// </summary>
    /// <remarks>
    /// <para>The text is written at the cursor as one or more multicell blocks. With <c>w=0</c> --
    /// the default -- each grapheme is its own block, <c>s</c> times as wide as it would otherwise
    /// be; with a non-zero <c>w</c> the whole payload is a single block of <c>s * w</c> columns,
    /// which is how a client states a string's width rather than leaving the terminal to guess.</para>
    /// <para>Returns whether the sequence was acted on, so a listener watching
    /// <see cref="Terminal.OscReceived"/> can tell a malformed one from a handled one.</para>
    /// </remarks>
    private bool HandleTextSizing(string data)
    {
        var parts = data.Split(new[] { ';' }, 2);

        // The text may itself contain semicolons, so only the FIRST separator divides metadata from
        // payload -- which is why the split is limited to two.
        if (!TextSizing.TryParse(parts[0], out var sizing))
        {
            if (parts.Length > 1 && parts[1].Length > 0)
                PrintUnsized(parts[1]);

            return false;
        }

        var text = parts.Length > 1 ? parts[1] : string.Empty;
        if (text.Length == 0)
            return true;   // well formed, and drawing nothing is what it asked for

        PrintSized(text, sizing);
        return true;
    }

    /// <summary>
    /// Prints the payload of an OSC 66 whose metadata could not be parsed, as ordinary text.
    /// </summary>
    /// <remarks>
    /// The text is what the user was meant to read. Dropping it because a value was out of range
    /// makes a client's bug into a blank space on the screen, where printing it unscaled leaves a
    /// heading that is the wrong size but still there -- and still says something is wrong.
    /// </remarks>
    private void PrintUnsized(string text)
    {
        var enumerator = System.Globalization.StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
            Print((string)enumerator.Current);
    }

    /// <summary>
    /// Writes the payload of an OSC 66 sequence at the cursor.
    /// </summary>
    private void PrintSized(string text, TextSizing sizing)
    {
        // The protocol's payload limit applies to the sequence, not to one of its two modes: with
        // w=0 an oversized payload would otherwise be walked grapheme by grapheme, interning every
        // one of them in the process-wide cluster table.
        text = Truncate(text);

        if (sizing.Width > 0)
        {
            PrintSizedBlock(text, sizing.Scale * sizing.Width, sizing);
            return;
        }

        // w=0: the terminal splits the text up as it normally would, except that each piece now
        // occupies its own s-by-s block. Grapheme clusters, so a base character keeps its combining
        // marks inside one block instead of scattering them across several.
        var enumerator = System.Globalization.StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            var element = (string)enumerator.Current;
            var width = GetStringCellWidth(element);
            if (width <= 0)
                continue;   // a cluster with no width of its own has nowhere to go

            PrintSizedBlock(element, width * sizing.Scale, sizing);
        }
    }

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

    /// <summary>
    /// Cuts a sized run's text down to <see cref="MaxSizedRunBytes"/>, at a grapheme boundary.
    /// </summary>
    private static string Truncate(string text)
    {
        // Cheapest sufficient test first: UTF-8 never uses more than three bytes per UTF-16 unit, so
        // a string this short cannot exceed the cap and no encoding pass is needed. That is every
        // real payload -- a block is at most 49 columns wide.
        if (text.Length <= MaxSizedRunBytes / 3
            || Encoding.UTF8.GetByteCount(text) <= MaxSizedRunBytes)
        {
            return text;
        }

        var kept = 0;
        var bytes = 0;
        var enumerator = System.Globalization.StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            var element = (string)enumerator.Current;
            var next = bytes + Encoding.UTF8.GetByteCount(element);
            if (next > MaxSizedRunBytes)
                break;

            bytes = next;
            kept += element.Length;
        }

        return text.Substring(0, kept);
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

    private void HandlePointerShape(string data)
    {
        // OSC 22 ; [op] name[,name...] ST  - Kitty's mouse pointer shape protocol.
        //
        // The operation is the first character: '>' pushes, '<' pops, '?' queries, and '=' or no
        // character at all sets. A bare OSC 22 clears, which is how an application says "I am done,
        // use your own pointer" without knowing what that pointer is.

        // Only the host can change a real pointer, so a host that will not is entitled to say so.
        // Silently, including the query: telling an application the shapes work and then not
        // changing the pointer is worse than telling it they do not, since it cannot tell the two
        // apart from the other end.
        if (!_terminal.Options.PointerShapesEnabled)
            return;

        if (data.Length == 0)
        {
            _terminal.ClearPointerShapes();
            return;
        }

        var op = data[0];
        var rest = op is '>' or '<' or '?' or '=' ? data.Substring(1) : data;

        switch (op)
        {
            case '<':
                // The name list is defined to be ignored here, and popping an empty stack is a
                // no-op rather than an error: an application unwinding does not have to count.
                _terminal.PopPointerShape();
                break;

            case '>':
                // Pushed in order, so the last name is the one that ends up current -- and pushed
                // as one operation, since only that last name is ever meant to be seen: a host told
                // about each name in turn would swap the real pointer once per name. Unknown names
                // are skipped rather than pushed, so a later pop does not restore a shape no host
                // can draw.
                _terminal.PushPointerShapes(rest.Split(',').Where(PointerShapes.IsKnown));
                break;

            case '?':
                AnswerPointerShapeQuery(rest);
                break;

            default:
                // Set. Empty after '=' clears, like the bare form.
                if (rest.Length == 0)
                {
                    _terminal.ClearPointerShapes();
                    break;
                }

                // One name, not a list: the protocol defines a comma-separated list for push only,
                // so the whole payload is the name here and a list is simply not a known shape.
                if (PointerShapes.IsKnown(rest))
                    _terminal.SetPointerShape(rest);
                break;
        }
    }

    /// <summary>
    /// Answers an OSC 22 query with an OSC 22 of its own.
    /// </summary>
    /// <remarks>
    /// Each queried name is answered in place, comma separated in the order asked: the three
    /// <c>__name__</c> specials with a shape name, everything else with 1 or 0 for whether this
    /// terminal supports it. Nothing from the query is echoed back -- an unsupported name is
    /// answered with 0, so an application cannot use a query to make the terminal write bytes of
    /// the application's choosing back to itself.
    /// </remarks>
    private void AnswerPointerShapeQuery(string query)
    {
        if (query.Length == 0)
            return;

        var answers = new List<string>();
        foreach (var name in query.Split(','))
        {
            answers.Add(name switch
            {
                // "0" rather than a name: the stack is empty, so no shape is set at all.
                "__current__" => _terminal.PointerShape ?? "0",
                "__default__" => PointerShapes.Default,
                "__grabbed__" => PointerShapes.Grabbed,
                _ => PointerShapes.IsKnown(name) ? "1" : "0",
            });
        }

        _terminal.RaiseDataReceived($"\u001b]22;{string.Join(",", answers)}\u001b\\");
    }

    private void HandleClipboard(string data)
    {
        var parts = data.Split(new[] { ';' }, 2);

        if (parts.Length != 2)
            return;

        var target = parts[0];
        var clipdata = parts[1];

        // xterm defaults an empty Pc to "s 0"; anything outside the Pc charset is not OSC 52.
        if (target.Length == 0)
            target = "s0";
        else if (!IsValidOsc52ClipboardTarget(target))
            return;

        if (clipdata == "?")
        {
            // Per issue #54 a disabled read answers NOTHING: silence is how this terminal
            // declines, and an unanswered probe cannot leak whether a clipboard exists.
            if (!_terminal.Options.ClipboardReadEnabled)
                return;

            // Armed BEFORE the handler runs, so a host whose clipboard is asynchronous can
            // Defer() and answer via Respond when its await completes — the response is
            // byte-identical either way, and null (or never answering) is the same silence an
            // unhandled request produces.
            var args = new Events.TerminalEvents.ClipboardReadEventArgs(target, "text/plain");
            args.Arm(bytes =>
            {
                if (bytes is null)
                    return;
                _terminal.RaiseDataReceived($"\u001b]52;{target};{Convert.ToBase64String(bytes)}\u0007");
            });
            _terminal.RaiseClipboardReadRequested(args);
            if (args.Data is { } sync && args.Disarm())
            {
                _terminal.RaiseDataReceived($"\u001b]52;{target};{Convert.ToBase64String(sync)}\u0007");
            }
            return;
        }

        if (!_terminal.Options.ClipboardWriteEnabled)
            return;

        // Invalid base64 is xterm's documented clear idiom: the host is told "empty", not
        // nothing. The raise sits outside any catch, so a host handler's own exception
        // propagates instead of being mistaken for a malformed payload.
        if (!TryDecodeBase64(clipdata, out var decoded))
            decoded = Array.Empty<byte>();
        _terminal.RaiseClipboardWriteRequested(target, "text/plain", decoded);
    }

    private void HandleKittyClipboard(string data)
    {
        var parts = data.Split(new[] { ';' }, 2);
        if (!TryParseKittyClipboardMetadata(parts[0], out var type, out var target, out var id, out var pw, out var name))
            return;

        switch (type)
        {
            case "write":
                HandleKittyClipboardWrite(target, id, parts.Length == 2);
                break;
            case "wdata":
                HandleKittyClipboardWriteData(parts[0], parts.Length == 2 ? parts[1] : null);
                break;
            case "read":
                HandleKittyClipboardRead(target, id, parts.Length == 2 ? parts[1] : null, pw, name);
                break;
            case "walias":
                HandleKittyClipboardAlias(parts[0], parts.Length == 2 ? parts[1] : null);
                break;
        }
    }

    private void HandleKittyClipboardWrite(string target, string id, bool hasPayload)
    {
        ResetKittyClipboard();
        if (hasPayload || target.Length == 0)
        {
            RaiseKittyClipboardResponse("write", "EINVAL", id);
            return;
        }

        if (!_terminal.Options.ClipboardWriteEnabled)
        {
            RaiseKittyClipboardResponse("write", "EPERM", id);
            return;
        }

        _kittyClipboardData = [];
        _kittyClipboardBase64 = [];
        _kittyClipboardAliases = [];
        _kittyClipboardTarget = target;
        _kittyClipboardId = id;
    }

    private void HandleKittyClipboardWriteData(string metadata, string? payload)
    {
        if (_kittyClipboardData is null)
        {
            return;
        }

        if (payload is null)
        {
            var id = _kittyClipboardId;
            if (_kittyClipboardBase64!.Values.Any(data => data.Length > 0))
            {
                ResetKittyClipboard();
                RaiseKittyClipboardResponse("write", "EINVAL", id);
                return;
            }

            foreach (var (alias, target) in _kittyClipboardAliases!)
            {
                if (!_kittyClipboardData.ContainsKey(target))
                {
                    ResetKittyClipboard();
                    RaiseKittyClipboardResponse("write", "EINVAL", id);
                    return;
                }
            }
            // ONE event for the whole transfer. Platform clipboards replace their contents on
            // each set, so per-format events could never be committed atomically: the host needs
            // the complete map to build one data object and set it once.
            var formats = new List<Events.TerminalEvents.ClipboardFormat>();
            foreach (var (completedMimeType, clipboardData) in _kittyClipboardData)
                formats.Add(new Events.TerminalEvents.ClipboardFormat(completedMimeType, [.. clipboardData]));
            foreach (var (alias, target) in _kittyClipboardAliases)
            {
                if (_kittyClipboardData.TryGetValue(target, out var clipboardData))
                {
                    formats.Add(new Events.TerminalEvents.ClipboardFormat(alias, [.. clipboardData]));
                }
            }
            // State reset BEFORE the raise: a host handler that throws must surface (that is the
            // contract), and it must not leave a half-committed transfer armed behind it.
            var transferTarget = _kittyClipboardTarget!;
            ResetKittyClipboard();
            _terminal.RaiseClipboardWriteRequested(transferTarget, formats);
            RaiseKittyClipboardResponse("write", "DONE", id);
            return;
        }

        if (!TryGetKittyMetadataValue(metadata, "mime", out var encodedMime)
            || !TryDecodeBase64(encodedMime, out var mimeBytes)
            || !TryGetMimeType(mimeBytes, out var mimeType))
        {
            var id = _kittyClipboardId;
            ResetKittyClipboard();
            RaiseKittyClipboardResponse("write", "EINVAL", id);
            return;
        }

        if (!payload.All(c => char.IsAsciiLetterOrDigit(c) || c is '+' or '/' or '='))
        {
            var id = _kittyClipboardId;
            ResetKittyClipboard();
            RaiseKittyClipboardResponse("write", "EINVAL", id);
            return;
        }

        var base64Chunks = _kittyClipboardBase64!;
        var base64 = base64Chunks.GetValueOrDefault(mimeType);
        if (base64 is null)
        {
            if (TransferSize + mimeType.Length + ClipboardEntryOverhead > MaxClipboardBytes)
            {
                var id = _kittyClipboardId;
                ResetKittyClipboard();
                RaiseKittyClipboardResponse("write", "EIO", id);
                return;
            }
            base64 = new StringBuilder();
            base64Chunks[mimeType] = base64;
            _kittyClipboardData![mimeType] = [];
            _kittyClipboardTransferSize += mimeType.Length + ClipboardEntryOverhead;
        }
        base64.Append(payload);
        _kittyClipboardTransferSize += payload.Length;
        if (TryDecodeBase64(base64.ToString(), out var chunk))
        {
            if (_kittyClipboardDecodedBytes + chunk.Length > MaxClipboardBytes)
            {
                var id = _kittyClipboardId;
                ResetKittyClipboard();
                RaiseKittyClipboardResponse("write", "EIO", id);
                return;
            }
            _kittyClipboardData![mimeType].AddRange(chunk);
            _kittyClipboardDecodedBytes += chunk.Length;
            // The pending base64 is spent: its charge is exchanged for the decoded bytes'.
            _kittyClipboardTransferSize += chunk.Length - base64.Length;
            base64.Clear();
        }
        else if (TransferSize > MaxClipboardBytes * 4 / 3 + 4)
        {
            var id = _kittyClipboardId;
            ResetKittyClipboard();
            RaiseKittyClipboardResponse("write", "EIO", id);
            return;
        }

        _kittyClipboardMimeType = mimeType;
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

    private void HandleKittyClipboardRead(string target, string id, string? payload, string pw, string name)
    {
        // A paste token outranks the gate: the paste NOTIFICATION was the authorization, so a
        // valid single-use pw serves the announced content whether or not general clipboard
        // reads are enabled. An absent or invalid token is not an error — per the spec the
        // terminal falls back to its standard security behaviour, which here is the gated host
        // seam below.
        // A pw accompanied by a name consumes the token the moment it is PRESENTED — before
        // payload validation, so a malformed redemption attempt cannot be corrected and retried
        // against a token that should already be spent. A pw with no name is, per the spec,
        // treated as though no password was given: nothing is consumed and the request falls
        // through to the standard gated path.
        if (pw.Length > 0 && name.Length > 0 && target.Length > 0
            && _terminal.TryRedeemPaste(pw, name, target) is { } paste)
        {
            if (payload is not null && TryDecodeBase64(payload, out var pasteRequestBytes))
            {
                ServePaste(paste, Encoding.UTF8.GetString(pasteRequestBytes), id);
                return;
            }

            // The token is spent either way; a malformed payload is the sender's own EINVAL.
            RaiseKittyClipboardResponse("read", "EINVAL", id);
            return;
        }

        if (!_terminal.Options.ClipboardReadEnabled)
        {
            RaiseKittyClipboardResponse("read", "EPERM", id);
            return;
        }

        if (target.Length == 0 || payload is null || !TryDecodeBase64(payload, out var mimeBytes))
        {
            RaiseKittyClipboardResponse("read", "EINVAL", id);
            return;
        }

        var requestedMimeTypes = Encoding.UTF8.GetString(mimeBytes) == "."
            ? ["."]
            : Encoding.UTF8.GetString(mimeBytes).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (requestedMimeTypes.Length == 0
            || requestedMimeTypes.Any(mimeType => !TryGetMimeType(Encoding.UTF8.GetBytes(mimeType), out _)))
        {
            RaiseKittyClipboardResponse("read", "EINVAL", id);
            return;
        }

        // The reply cannot begin until EVERY requested mime has resolved: OK precedes the
        // first DATA, and EPERM is only true when none answered. Answers may arrive
        // synchronously, later (a host that Defer()s while it awaits its clipboard), or as a
        // mix — the last one to land emits the whole reply. A deferred request the host never
        // completes leaves the read unanswered, which is why the args' contract says a
        // deferring host must always call Respond.
        var answers = new byte[]?[requestedMimeTypes.Length];
        var outstanding = 0;
        var dispatched = false;

        void Deliver()
        {
            var responses = requestedMimeTypes
                .Zip(answers, (mimeType, bytes) => (MimeType: mimeType, Data: bytes))
                .Where(response => response.Data is not null)
                .ToList();
            if (responses.Count == 0)
            {
                RaiseKittyClipboardResponse("read", "EPERM", id);
                return;
            }

            RaiseKittyClipboardResponse("read", "OK", id);
            foreach (var (mimeType, clipboardData) in responses)
            {
                var encodedMime = Convert.ToBase64String(Encoding.UTF8.GetBytes(mimeType));
                if (clipboardData!.Length == 0)
                {
                    // As in ServePaste: a supplied empty value is an answer, not an absence.
                    _terminal.RaiseDataReceived($"\u001b]5522;type=read:status=DATA:mime={encodedMime}{FormatKittyId(id)};\u001b\\");
                    continue;
                }
                foreach (var chunk in clipboardData.Chunk(4096))
                    _terminal.RaiseDataReceived($"\u001b]5522;type=read:status=DATA:mime={encodedMime}{FormatKittyId(id)};{Convert.ToBase64String(chunk)}\u001b\\");
            }
            RaiseKittyClipboardResponse("read", "DONE", id);
        }

        // ONE completion path per mime: the armed callback. A synchronous answer is fed through
        // it by the Respond below; a handler that already called Respond from inside the handler
        // disarmed it, so that Respond is a no-op and the answer counts exactly once — the
        // counter cannot go negative and the reply cannot be delivered twice or hang.
        outstanding = requestedMimeTypes.Length;
        for (var i = 0; i < requestedMimeTypes.Length; i++)
        {
            var index = i;
            var args = new Events.TerminalEvents.ClipboardReadEventArgs(target, requestedMimeTypes[i]);
            args.Arm(bytes =>
            {
                answers[index] = bytes;
                if (--outstanding == 0 && dispatched)
                    Deliver();
            });
            _terminal.RaiseClipboardReadRequested(args);
            // A synchronous answer WINS, as the args promise and OSC 52 already honours: when one
            // subscriber set Data and another deferred, the sync value completes the slot now and
            // the late Respond is a disarmed no-op. Only a defer with no sync answer stays open.
            if (args.Data is not null || !args.Deferred)
                args.Respond(args.Data);
        }

        dispatched = true;
        if (outstanding == 0)
            Deliver();
    }

    /// <summary>
    /// Answers a redeemed paste read from the paste's own accessor — never from the host
    /// clipboard seam. Requested types the paste cannot supply are skipped, as the spec directs;
    /// "." answers with the list of available types, mirroring the notification.
    /// </summary>
    private void ServePaste(TerminalPaste paste, string requested, string id)
    {
        // Everything is resolved BEFORE the first packet goes out, for two reasons the reply
        // format forces. OK must not promise what nothing can deliver: the spec's ENOSYS exists
        // for a requested type that is unavailable, and an empty successful transfer would teach
        // clients that missing formats are valid empty data. And GetData is HOST code — the
        // standard read path collects every answer before emitting for the same reason — so a
        // throwing accessor surfaces before OK rather than truncating the reply mid-stream and
        // hanging the application on a DONE that never comes.
        var replies = new List<(string EncodedMime, byte[] Data)>();
        if (requested == ".")
        {
            replies.Add(("Lg==", Encoding.UTF8.GetBytes(string.Join(' ', paste.MimeTypes))));
        }
        else
        {
            foreach (var mimeType in requested.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!paste.MimeTypes.Contains(mimeType) || paste.GetData(mimeType) is not { } bytes)
                    continue;
                replies.Add((Convert.ToBase64String(Encoding.UTF8.GetBytes(mimeType)), bytes));
            }
        }

        if (replies.Count == 0)
        {
            RaiseKittyClipboardResponse("read", "ENOSYS", id);
            return;
        }

        RaiseKittyClipboardResponse("read", "OK", id);
        foreach (var (encodedMime, bytes) in replies)
        {
            if (bytes.Length == 0)
            {
                // A supplied empty value is an ANSWER — one empty chunk keeps it distinguishable
                // from a type that was never available at all.
                _terminal.RaiseDataReceived($"\u001b]5522;type=read:status=DATA:mime={encodedMime}{FormatKittyId(id)};\u001b\\");
                continue;
            }
            foreach (var chunk in bytes.Chunk(4096))
                _terminal.RaiseDataReceived($"\u001b]5522;type=read:status=DATA:mime={encodedMime}{FormatKittyId(id)};{Convert.ToBase64String(chunk)}\u001b\\");
        }
        RaiseKittyClipboardResponse("read", "DONE", id);
    }

    private void HandleKittyClipboardAlias(string metadata, string? payload)
    {
        if (_kittyClipboardData is null || payload is null
            || !TryGetKittyMetadataValue(metadata, "mime", out var encodedMime)
            || !TryDecodeBase64(encodedMime, out var mimeBytes)
            || !TryGetMimeType(mimeBytes, out var target)
            || !TryDecodeBase64(payload, out var aliasBytes))
        {
            var id = _kittyClipboardId;
            ResetKittyClipboard();
            RaiseKittyClipboardResponse("write", "EINVAL", id);
            return;
        }

        var aliases = Encoding.UTF8.GetString(aliasBytes).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (aliases.Length == 0 || aliases.Any(alias => !TryGetMimeType(Encoding.UTF8.GetBytes(alias), out _))
            || aliases.Any(alias => _kittyClipboardAliases!.Any(existing => existing.Alias == alias)))
        {
            var id = _kittyClipboardId;
            ResetKittyClipboard();
            RaiseKittyClipboardResponse("write", "EINVAL", id);
            return;
        }

        if (TransferSize + aliases.Sum(alias => (long)alias.Length + target.Length + ClipboardEntryOverhead) > MaxClipboardBytes)
        {
            var id = _kittyClipboardId;
            ResetKittyClipboard();
            RaiseKittyClipboardResponse("write", "EIO", id);
            return;
        }

        _kittyClipboardAliases!.AddRange(aliases.Select(alias => (alias, target)));
        _kittyClipboardTransferSize += aliases.Sum(alias => (long)alias.Length + target.Length + ClipboardEntryOverhead);
    }

    private static bool TryParseKittyClipboardMetadata(
        string metadata, out string type, out string target, out string id,
        out string pw, out string name)
    {
        type = string.Empty;
        target = "c";
        id = string.Empty;
        pw = string.Empty;
        name = string.Empty;
        foreach (var item in metadata.Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = item.IndexOf('=');
            if (separator <= 0)
                return false;

            var key = item[..separator];
            var value = item[(separator + 1)..];
            if (key == "type")
                type = value;
            else if (key == "loc")
            {
                target = value switch
                {
                    "clipboard" => "c",
                    "primary" => "p",
                    _ => string.Empty
                };
            }
            else if (key == "id")
            {
                id = new string(value.Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '+' or '.').ToArray());
            }
            else if (key == "pw")
            {
                pw = value;
            }
            else if (key == "name")
            {
                // The spec sends the name base64-encoded; what matters here is only that one was
                // given, so a payload that does not decode counts as absent.
                if (TryDecodeBase64(value, out var nameBytes))
                    name = Encoding.UTF8.GetString(nameBytes);
            }
        }

        return type is "write" or "wdata" or "read" or "walias";
    }

    private static bool IsValidOsc52ClipboardTarget(string target) =>
        target.Length > 0 && target.All(c => c is 'c' or 'p' or 'q' or 's' or >= '0' and <= '7');

    private static bool TryGetKittyMetadataValue(string metadata, string key, out string value)
    {
        value = string.Empty;
        foreach (var item in metadata.Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            if (item.StartsWith($"{key}=", StringComparison.Ordinal))
            {
                value = item[(key.Length + 1)..];
                return true;
            }
        }
        return false;
    }

    private static bool TryGetMimeType(byte[] bytes, out string mimeType)
    {
        mimeType = Encoding.UTF8.GetString(bytes);
        return mimeType.Length > 0 && mimeType.All(c => c is >= ' ' and <= '~' and not ';' and not ':');
    }

    private static bool TryDecodeBase64(string data, out byte[] decoded)
    {
        try
        {
            decoded = Convert.FromBase64String(data);
            return true;
        }
        catch (FormatException)
        {
            decoded = Array.Empty<byte>();
            return false;
        }
    }

    /// <summary>
    /// Abandons any in-progress OSC 5522 write transfer. Internal because RIS must reach it:
    /// a reset mid-transfer must not let a later terminator commit pre-reset data to the host.
    /// </summary>
    internal void ResetKittyClipboard()
    {
        _kittyClipboardTransferSize = 0;
        _kittyClipboardDecodedBytes = 0;
        _kittyClipboardData = null;
        _kittyClipboardBase64 = null;
        _kittyClipboardAliases = null;
        _kittyClipboardTarget = null;
        _kittyClipboardMimeType = null;
        _kittyClipboardId = null;
    }

    private void RaiseKittyClipboardResponse(string type, string status, string? id = null) =>
        _terminal.RaiseDataReceived($"\u001b]5522;type={type}:status={status}{FormatKittyId(id ?? _kittyClipboardId)}\u001b\\");

    private static string FormatKittyId(string? id) => string.IsNullOrEmpty(id) ? string.Empty : $":id={id}";

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
        // Stops at the right margin when the cursor starts inside the region, the screen edge
        // when it starts outside — in/out decides, not origin mode, as in xterm. Without the
        // bound, CSI 200 C walks the cursor out of its pane and the next write lands in the
        // neighbouring one. (Full-width margins make the two limits the same column.)
        var limit = CursorInMarginColumns() ? _buffer.ScrollRight : _terminal.Cols - 1;
        _buffer.SetCursor(Math.Min(_buffer.X + count, limit), _buffer.Y);
    }

    private void CursorBackward(Params parameters)
    {
        var count = Math.Max(parameters.GetParam(0, 1), 1);
        // The mirror of CursorForward: the left margin stops a cursor that starts inside.
        var home = CursorInMarginColumns() ? _buffer.ScrollLeft : 0;
        _buffer.SetCursor(Math.Max(_buffer.X - count, home), _buffer.Y);
    }

    private void CursorNextLine(Params parameters)
    {
        // xterm implements CNL as CUD then CR, so the column is CR’s: the left margin when the
        // cursor is at or right of it, column 0 when it is left of it — origin mode is not
        // consulted. The row move cannot change X, so the CR sees the starting column.
        var count = Math.Max(parameters.GetParam(0, 1), 1);
        _buffer.SetCursor(_buffer.X, Math.Min(_buffer.Y + count, _terminal.Rows - 1));
        _buffer.CarriageReturn();
    }

    private void CursorPrecedingLine(Params parameters)
    {
        // CPL is CUU then CR, mirroring CursorNextLine.
        var count = Math.Max(parameters.GetParam(0, 1), 1);
        _buffer.SetCursor(_buffer.X, Math.Max(_buffer.Y - count, 0));
        _buffer.CarriageReturn();
    }

    private void CursorCharAbsolute(Params parameters)
    {
        var col = GetAbsoluteCursorCol(Math.Max(parameters.GetParam(0, 1), 1) - 1);
        _buffer.SetCursor(col, _buffer.Y);
    }

    private void CursorPosition(Params parameters)
    {
        var row = Math.Max(parameters.GetParam(0, 1), 1) - 1;
        var col = Math.Max(parameters.GetParam(1, 1), 1) - 1;
        row = GetAbsoluteCursorRow(row);
        col = GetAbsoluteCursorCol(col);
        _buffer.SetCursor(col, row);
    }

    private void EraseInDisplay(Params parameters)
    {
        var mode = parameters.GetParam(0, 0);
        var emptyCell = BufferCell.Space;
        emptyCell.Attributes = _curAttr;

        var hasBlocks = _buffer.HasMultiRowSizedRuns;

        switch (mode)
        {
            case 0: // Erase below
                EraseInLine(parameters); // Current line from cursor
                for (int i = _buffer.Y + 1; i < _terminal.Rows; i++)
                {
                    _buffer.Lines[_buffer.YBase + i]?.Fill(emptyCell);
                    if (hasBlocks)
                        EraseBlocksHangingOver(_buffer.YBase + i, 0, _terminal.Cols);
                }
                break;
            case 1: // Erase above
                for (int i = 0; i < _buffer.Y; i++)
                {
                    _buffer.Lines[_buffer.YBase + i]?.Fill(emptyCell);
                    if (hasBlocks)
                        EraseBlocksHangingOver(_buffer.YBase + i, 0, _terminal.Cols);
                }
                EraseInLine(parameters); // Current line to cursor
                break;
            case 2: // Erase all — the visible screen only; the scrollback is kept
                for (int i = 0; i < _terminal.Rows; i++)
                {
                    _buffer.Lines[_buffer.YBase + i]?.Fill(emptyCell);
                    if (hasBlocks)
                        EraseBlocksHangingOver(_buffer.YBase + i, 0, _terminal.Cols);
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

        // An erase is the likeliest way for the last tall block to leave the buffer, and a flag left
        // set retires the print fast path for the rest of the session.
        if (hasBlocks)
            _buffer.RefreshMultiRowSizedRuns();
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
                if (_buffer.HasMultiRowSizedRuns)
                    EraseBlocksHangingOver(_buffer.Y + _buffer.YBase, _buffer.X, _terminal.Cols - _buffer.X);
                break;
            case 1: // Erase to left
                line.Fill(emptyCell, 0, _buffer.X + 1);
                if (_buffer.HasMultiRowSizedRuns)
                    EraseBlocksHangingOver(_buffer.Y + _buffer.YBase, 0, _buffer.X + 1);
                break;
            case 2: // Erase entire line
                line.Fill(emptyCell);
                if (_buffer.HasMultiRowSizedRuns)
                    EraseBlocksHangingOver(_buffer.Y + _buffer.YBase, 0, _terminal.Cols);
                break;
        }
    }

    /// <summary>
    /// Erases the OSC 66 blocks anchored on earlier rows whose cells hang over the region an erase
    /// or a line splice is about to change.
    /// </summary>
    /// <remarks>
    /// The protocol's rules for erasing and for inserting or deleting lines are stated over the
    /// REGION affected rather than over a line, and a block <c>s</c> rows tall is inside every region
    /// its lower rows touch. A block left alive over rows that have been cleared or moved would be
    /// drawn across whatever is there now, and its columns would go on displacing text written to
    /// rows that are no longer under it.
    /// </remarks>
    /// <summary>
    /// Erases every block covering the given cells from an earlier row. Callers test
    /// <see cref="TerminalBuffer.HasMultiRowSizedRuns"/> AT THE CALL — hoisted outside the loop
    /// where there is one — for the reason NoteLinkRun's guard records: these sit on the
    /// erase hot paths, a full-screen redraw erases every line, and a method call per line just
    /// to read a false flag is the shape that has now cost alt-redraw twice.
    /// </summary>
    private void EraseBlocksHangingOver(int absoluteRow, int column, int count) =>
        _buffer.EraseSizedRunsCovering(absoluteRow, column, count);

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
    /// Whether the cursor is inside the margin columns — the ONE in/out answer every
    /// column-sensitive operation shares, so no two of them can disagree about the same column.
    /// </summary>
    /// <remarks>
    /// The boundary column is the subtle part. X == ScrollRight + 1 is two different states: the
    /// pending-wrap residue of filling the region's last column (INSIDE — the wrap is due at the
    /// margin), and a deliberate placement at the first column right of the margin (OUTSIDE — an
    /// ordinary cursor position in the split layouts this feature exists for). The buffer's
    /// <see cref="TerminalBuffer.PendingWrap"/> flag is what tells them apart; deciding by
    /// position alone either wrapped the next pane's first column into this one, or ran text
    /// straight through the margin at exactly the moment the wrap was due.
    /// </remarks>
    private bool CursorInMarginColumns()
        => _buffer.X >= _buffer.ScrollLeft
        && _buffer.X <= _buffer.ScrollRight + (_buffer.PendingWrap ? 1 : 0);

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
        if (_buffer.MarginsAreFullWidth || !CursorInMarginColumns())
            return _terminal.Cols - 1;

        return _buffer.ScrollRight;
    }

    /// <summary>The column a wrapped line begins on: the left margin, for the same reason.</summary>
    private int WrapHome()
    {
        if (_buffer.MarginsAreFullWidth || !CursorInMarginColumns())
            return 0;

        return _buffer.ScrollLeft;
    }

    /// <summary>
    /// Whether the cursor is inside the scrolling region — the box, not just the band of rows.
    /// </summary>
    /// <remarks>
    /// IL and DL do nothing from outside it. With margins that has to include the columns: a cursor
    /// in the right-hand pane of a split layout is outside the left pane's region, and shifting the
    /// left pane's lines from there is the exact corruption margins exist to prevent. The column
    /// test goes through <see cref="CursorInMarginColumns"/> so the pending-wrap state counts as
    /// inside — with no margins at all, a cursor resting at X == Cols after a full-width line is
    /// the ORDINARY place for IL/DL to run from, and reading it as outside made them no-ops on the
    /// default path.
    /// </remarks>
    private bool InsideScrollRegion()
        => _buffer.Y >= _buffer.ScrollTop && _buffer.Y <= _buffer.ScrollBottom
        && CursorInMarginColumns();

    /// <summary>A blank carrying the current attributes, which is what BCE fills with.</summary>
    private BufferCell BlankCell()
    {
        var cell = BufferCell.Space;
        cell.Attributes = _curAttr;
        return cell;
    }

    private void InsertLines(Params parameters)
    {
        var count = Math.Max(parameters.GetParam(0, 1), 1);
        if (!InsideScrollRegion())
            return;

        // Narrowed margins move only their own columns, so the lines stay put and their cells are
        // copied between them. Splicing whole lines here would drag the columns OUTSIDE the region
        // along with them, which is the side-by-side layout tearing itself apart. That path erases
        // the blocks it disturbs itself, since it knows which columns it moved.
        if (!_buffer.MarginsAreFullWidth)
        {
            _buffer.ScrollMarginColumns(_buffer.Y, _buffer.ScrollBottom, count, up: false, BlankCell());
            return;
        }

        // A block hanging over the cursor's row is split by the insertion -- its lower rows are
        // pushed away from the line that describes them -- so the protocol has it erased.
        if (_buffer.HasMultiRowSizedRuns)
            EraseBlocksHangingOver(_buffer.Y + _buffer.YBase, 0, _terminal.Cols);

        for (int i = 0; i < count; i++)
        {
            _buffer.Lines.Splice(_buffer.YBase + _buffer.ScrollBottom, 1);
            _buffer.Lines.Splice(_buffer.Y + _buffer.YBase, 0,
                _buffer.GetBlankLine(_curAttr));
        }

        _buffer.RefreshMultiRowSizedRuns();
    }

    private void DeleteLines(Params parameters)
    {
        var count = Math.Max(parameters.GetParam(0, 1), 1);
        if (!InsideScrollRegion())
            return;

        if (!_buffer.MarginsAreFullWidth)
        {
            _buffer.ScrollMarginColumns(_buffer.Y, _buffer.ScrollBottom, count, up: true, BlankCell());
            return;
        }

        // Every deleted row is part of the region, so a block hanging over any of them goes too.
        // The blocks anchored ON those rows leave with the lines that describe them.
        var last = Math.Min(_buffer.Y + count - 1, _buffer.ScrollBottom);
        for (int row = _buffer.Y; row <= last; row++)
            if (_buffer.HasMultiRowSizedRuns)
                EraseBlocksHangingOver(row + _buffer.YBase, 0, _terminal.Cols);

        for (int i = 0; i < count; i++)
        {
            _buffer.Lines.Splice(_buffer.Y + _buffer.YBase, 1);
            _buffer.Lines.Splice(_buffer.YBase + _buffer.ScrollBottom, 0,
                _buffer.GetBlankLine(_curAttr));
        }

        _buffer.RefreshMultiRowSizedRuns();
    }

    private void InsertChars(Params parameters)
    {
        var count = Math.Max(parameters.GetParam(0, 1), 1);
        var line = _buffer.Lines[_buffer.Y + _buffer.YBase];
        if (line == null)
            return;

        // The MARGINS bound this, not the screen — both of them. Shifting past the right margin
        // would push characters out of one pane and into the next; running from a cursor LEFT of
        // the left margin shifts the neighbouring pane's columns across it from outside, the same
        // corruption from the other side. Outside the region on either side, ICH does nothing.
        var right = _buffer.ScrollRight;
        if (_buffer.X > right || _buffer.X < _buffer.ScrollLeft)
            return;

        // A scaled block from the cursor rightwards cannot survive the shift: the cells that make it
        // up move, and the run describing them does not. The protocol says to erase them.
        if (line.HasSizedRuns)
            line.EraseSizedRunsFrom(_buffer.X);

        count = Math.Min(count, right - _buffer.X + 1);

        line.CopyCellsFrom(line, _buffer.X, _buffer.X + count,
            right - _buffer.X - count + 1, false);

        line.Fill(BlankCell(), _buffer.X, Math.Min(_buffer.X + count, right + 1));
    }

    private void DeleteChars(Params parameters)
    {
        var count = Math.Max(parameters.GetParam(0, 1), 1);
        var line = _buffer.Lines[_buffer.Y + _buffer.YBase];
        if (line == null)
            return;

        // As with ICH, the margins are the edges — what is pulled in comes from inside the
        // region, the blanks appear at the margin rather than the screen edge, and a cursor
        // outside the region on EITHER side does nothing rather than dragging the next pane's
        // columns across the boundary.
        var right = _buffer.ScrollRight;
        if (_buffer.X > right || _buffer.X < _buffer.ScrollLeft)
            return;

        count = Math.Min(count, right - _buffer.X + 1);

        // As for insertion: shifting cells destroys any block from the cursor rightwards.
        if (line.HasSizedRuns)
            line.EraseSizedRunsFrom(_buffer.X);

        line.CopyCellsFrom(line, _buffer.X + count, _buffer.X,
            right - _buffer.X - count + 1, false);

        line.Fill(BlankCell(), right - count + 1, right + 1);
    }

    private void EraseChars(Params parameters)
    {
        var count = Math.Max(parameters.GetParam(0, 1), 1);
        var line = _buffer.Lines[_buffer.Y + _buffer.YBase];

        var emptyCell = BufferCell.Space;
        emptyCell.Attributes = _curAttr;

        line?.Fill(emptyCell, _buffer.X, Math.Min(_buffer.X + count, _terminal.Cols));
        if (_buffer.HasMultiRowSizedRuns)
            EraseBlocksHangingOver(_buffer.Y + _buffer.YBase, _buffer.X,
            Math.Min(_buffer.X + count, _terminal.Cols) - _buffer.X);
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
            // Secondary DA: CSI > Pp ; Pv ; Pc c. Pp = 1 is a VT220, matching the conformance
            // level the primary reply claims -- the old 0 said VT100 and contradicted it. Pv
            // carries this library's version so a program can tell builds apart, and Pc = 0 is
            // "no cartridge ROM".
            _terminal.RaiseDataReceived(SecondaryDeviceAttributes);
        }
        else if (identifier.Length == 1)
        {
            // Primary DA: CSI ? 62 ; ... c. 62 is service class 2 (VT220), the level whose core --
            // scrolling regions, insert and delete line and character, erase character, the
            // alternate buffer, DECSC/DECRC -- this emulator does implement. 22 is ANSI colour.
            _terminal.RaiseDataReceived(_terminal.Options.SixelEnabled
                ? "\u001b[?62;4;22c"
                : "\u001b[?62;22c");
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
    /// The Pv field of the secondary DA reply: this assembly's version flattened into one number,
    /// so 2.0 reports 200.
    /// </summary>
    private static int FirmwareVersion =>
        typeof(InputHandler).Assembly.GetName().Version is { } version
            ? version.Major * 100 + version.Minor
            : 0;

    /// <summary>
    /// The secondary DA reply, CSI &gt; Pp ; Pv ; Pc c.
    /// </summary>
    private static string SecondaryDeviceAttributes => $"\u001b[>1;{FirmwareVersion};0c";

    /// <summary>
    /// XTSMGRAPHICS -- CSI ? Pi ; Pa ; Pv S. Reports the terminal's graphics limits.
    /// </summary>
    /// <remarks>
    /// <para>This shares its final character with SCROLL UP, and <c>ToCsiCommand</c> used to strip
    /// the private marker before looking the command up, so a graphics query scrolled the screen
    /// instead of being answered. Every Sixel-capable program sends one during startup, which made
    /// the damage routine rather than obscure. The lookup now matches "?S" on its own.</para>
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

    /// <summary>
    /// DECSLRM (<c>CSI Pl ; Pr s</c>) — set the left and right margins of the scrolling region.
    /// </summary>
    /// <remarks>
    /// <para>Only reachable while DECLRMM is set; see the dispatch above for why.</para>
    /// <para>Omitted parameters mean the extremes, so a bare <c>CSI s</c> under the mode widens the
    /// margins back to the whole screen rather than doing nothing.</para>
    /// <para>The cursor goes home afterwards, as it does for DECSTBM. A cursor left outside the new
    /// region is the thing that makes the next write land somewhere the application did not choose.</para>
    /// </remarks>
    private void SetLeftRightMargins(Params parameters)
    {
        var left = Math.Max(parameters.GetParam(0, 1), 1) - 1;
        var right = Math.Max(parameters.GetParam(1, _terminal.Cols), 1) - 1;

        // A degenerate pair is refused outright, and then so is the cursor move: DEC leaves the old
        // margins in force, and homing the cursor to a region that was not set would be a visible
        // effect from a sequence that had none.
        if (!_buffer.SetLeftRightMargins(left, right))
            return;

        MoveCursorToHome();
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
        // Home is the top-left of the SCROLLING REGION under origin mode, which with margins is a
        // box rather than a band -- so the column matters as well as the row.
        var row = _terminal.OriginMode ? _buffer.ScrollTop : 0;
        var col = _terminal.OriginMode ? _buffer.ScrollLeft : 0;
        _buffer.SetCursor(col, row);
    }

    /// <summary>
    /// Turns a column an application asked for into an absolute one, honouring origin mode.
    /// </summary>
    /// <remarks>
    /// The column twin of <see cref="GetAbsoluteCursorRow"/>. Under origin mode an application
    /// addresses the region rather than the screen, so column 1 is the left margin and nothing it
    /// asks for can land outside the box.
    /// </remarks>
    private int GetAbsoluteCursorCol(int col)
    {
        if (_terminal.OriginMode)
        {
            long absolute = (long)_buffer.ScrollLeft + col;
            return (int)Math.Clamp(absolute, _buffer.ScrollLeft, _buffer.ScrollRight);
        }

        return Math.Clamp(col, 0, Math.Max(0, _terminal.Cols - 1));
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

                case TerminalMode.LeftRightMargin:
                    _terminal.LeftRightMarginMode = true;
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
        // NEL is Index plus carriage return in xterm, so the column follows CR’s margin rule.
        IndexDown();
        _buffer.CarriageReturn();
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
