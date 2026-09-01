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
    /// <summary>The regional indicator symbols, U+1F1E6 to U+1F1FF. Two of them make one flag.</summary>
    private static bool IsRegionalIndicator(int codePoint)
        => codePoint >= 0x1F1E6 && codePoint <= 0x1F1FF;

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

    /// <summary>GB6, GB7 and GB8: which jamo classes continue the cluster ending in which.</summary>
    private static bool HangulJoins(int previousClass, int currentClass) => currentClass switch
    {
        1 or 4 or 5 => previousClass is 1,                 // GB6: L x (L | V | LV | LVT)
        2 => previousClass is 1 or 2 or 4,                 // GB6/GB7: (L | V | LV) x V
        3 => previousClass is 2 or 3 or 4 or 5,            // GB7/GB8: (V | LV | T | LVT) x T
        _ => false,
    };

    /// <summary>
    /// The conjunct linkers: Indic Syllabic Category Virama plus Invisible Stacker — the 41
    /// codepoints that fuse a following consonant into their cluster. This is wcwidth 0.8's
    /// exact set, a superset of UAX #29's InCB Linker eight: the InCB property stops at the
    /// scripts whose conjuncts UAX #29 refuses to break, but Khmer's coeng, Tai Tham's sakot,
    /// Javanese's pangkon, Myanmar's stacker and the SMP Brahmic scripts stack consonants the
    /// same way, and terminals are measured on all of them.
    /// </summary>
    private static bool IsConjunctLinker(int codePoint) => codePoint is
        0x094D or 0x09CD or 0x0A4D or 0x0ACD or 0x0B4D or 0x0BCD or 0x0C4D or 0x0CCD
        or 0x0D4D or 0x0DCA or 0x1039 or 0x17D2 or 0x1A60 or 0x1B44 or 0x1BAB or 0xA806
        or 0xA8C4 or 0xA9C0 or 0xAAF6 or 0x10A3F or 0x11046 or 0x110B9 or 0x11133 or 0x111C0
        or 0x11235 or 0x1134D or 0x113D0 or 0x11442 or 0x114C2 or 0x115BF or 0x1163F or 0x116B6
        or 0x11839 or 0x1193E or 0x119E0 or 0x11A47 or 0x11A99 or 0x11C3F or 0x11D45 or 0x11D97
        or 0x11F42;

    /// <summary>
    /// A letter that can be a conjunct's right-hand side: an Lo in a block whose script has a
    /// linker. The blocks are exactly the homes of the 41 linkers above. An approximation of
    /// per-script consonant data that over-accepts only sequences (linker then independent
    /// vowel) which are malformed in the scripts themselves.
    /// </summary>
    private static bool IsConjunctConsonantCandidate(int codePoint) =>
        IsConjunctScriptBlock(codePoint)
        && System.Globalization.CharUnicodeInfo.GetUnicodeCategory(codePoint)
            == System.Globalization.UnicodeCategory.OtherLetter;

    /// <summary>The blocks housing the linker scripts, BMP and SMP.</summary>
    private static bool IsConjunctScriptBlock(int codePoint) => codePoint switch
    {
        >= 0x0900 and <= 0x0DFF => true,     // Devanagari through Sinhala
        >= 0x1000 and <= 0x109F => true,     // Myanmar
        >= 0x1780 and <= 0x17FF => true,     // Khmer
        >= 0x1A20 and <= 0x1AAF => true,     // Tai Tham
        >= 0x1B00 and <= 0x1BBF => true,     // Balinese, Sundanese
        >= 0xA800 and <= 0xA82F => true,     // Syloti Nagri
        >= 0xA880 and <= 0xA8DF => true,     // Saurashtra
        >= 0xA980 and <= 0xA9DF => true,     // Javanese
        >= 0xAA60 and <= 0xAA7F => true,     // Myanmar Extended-A
        >= 0xAAE0 and <= 0xABFF => true,     // Meetei Mayek
        >= 0x10A00 and <= 0x11FFF => true,   // the SMP Brahmic scripts, Kharoshthi through Kawi
        _ => false,
    };

    /// <summary>
    /// Whether this codepoint might continue the previous cell's cluster under the SEQUENCE
    /// rules. Runs at BITMAP BUILD TIME only — MayContinueCluster answers the BMP from the
    /// table and the astral arm never calls this — so it is written for correctness, not speed.
    /// </summary>
    private static bool IsSequenceJoinCandidate(int codePoint) =>
        HangulClassOf(codePoint) != 0 || IsConjunctConsonantCandidate(codePoint);

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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool MayContinueCluster(int codePoint) =>
        (uint)codePoint < 0x10000
            ? (MayJoinBmp[codePoint >> 3] & (1 << (codePoint & 7))) != 0
            : MayContinueClusterAstral(codePoint);

    private static bool MayContinueClusterAstral(int codePoint)
    {
        // Astral: the sequence rules are BMP-only, and the joinable astral codepoints are the
        // Variation Selectors Supplement, the skin tones, and plane-1 script marks (musical,
        // SignWriting, Adlam...) — every one of which sits BELOW the emoji blocks. So emoji, the
        // astral characters streams actually carry, resolve in these compares and never reach
        // the category lookup they were paying ~4% of the unicode corpus for.
        if (codePoint >= 0xE0100 && codePoint <= 0xE01EF)
            return true;
        if (codePoint >= 0xE0020 && codePoint <= 0xE007F)
            return true;   // TAG characters: they spell out an emoji tag sequence (🏴 gbsct...)
        if (codePoint >= 0x10A00 && codePoint <= 0x11FFF)
        {
            // SMP Brahmic scripts: their marks join like any mark, and their consonants are
            // conjunct candidates whose context TryAppendToPreviousCell vets.
            return IsCombiningCharacter(codePoint) || IsConjunctConsonantCandidate(codePoint);
        }
        if (IsSkinToneModifier(codePoint))
            return true;
        if (codePoint >= 0x1F000)
            return false;
        return IsCombiningCharacter(codePoint);
    }

    /// <summary>
    /// Whether the sequence rules can refuse this join without touching the buffer: the current
    /// codepoint is a sequence candidate, the previous printed codepoint is known and still where
    /// the cursor left it, and the classes do not join. Anything uncertain answers false and
    /// falls through to <see cref="TryAppendToPreviousCell"/>, whose own context checks remain
    /// the authority.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool RefusesSequenceCheaply(int codePoint)
    {
        // The caller already applied the candidate-hull bracket inline, so everything here is
        // a potential sequence character; marks, VS and ZWJ never reach this method.
        // The REP tracker is exactly the context needed: stamped after every print and append,
        // cancelled by the operations that move the cursor, and position-checked here besides —
        // a stale entry simply fails the match and the full path decides.
        if (_lastPrinted is not { } lp
            || lp.Row != _buffer.Y + _buffer.YBase
            || lp.CursorCol != _buffer.X)
            return false;                     // context unknown: the full path decides
        var currentClass = HangulClassOf(codePoint);
        if (currentClass != 0)
            return !HangulJoins(HangulClassOf(lp.LastCodePoint), currentClass);
        if (IsConjunctConsonantCandidate(codePoint))
        {
            // A previous ZWJ may hide a linker before it; only the full path can see that far.
            return !IsConjunctLinker(lp.LastCodePoint) && lp.LastCodePoint != ZeroWidthJoiner;
        }
        return false;
    }

    /// <summary>
    /// Extended_Pictographic, near enough for GB11: the emoji blocks plus the handful of older
    /// symbols that carry the property. A tighter answer needs the Unicode property data; this
    /// errs toward the blocks emoji actually come from, and the cost of being wrong at the edges
    /// is a cluster that splits rather than one that swallows unrelated text.
    /// </summary>
    private static bool IsExtendedPictographic(int codePoint) => codePoint switch
    {
        >= 0x1F000 and <= 0x1FAFF => true,   // the emoji planes
        >= 0x2600 and <= 0x27BF => true,     // Misc Symbols, Dingbats
        0x00A9 or 0x00AE or 0x203C or 0x2049 => true,
        >= 0x2100 and <= 0x21FF => true,     // Letterlike, arrows used as emoji
        >= 0x2300 and <= 0x23FF => true,     // Misc Technical (watch, hourglass)
        >= 0x2B00 and <= 0x2BFF => true,     // stars, arrows
        >= 0xFE0F and <= 0xFE0F => true,     // VS16 keeps a pictographic cluster together
        _ => false,
    };

    /// <summary>
    /// Blanks the half of a wide character that the cell about to be written would orphan.
    /// </summary>
    /// <remarks>
    /// A two-column character occupies its cell and the spacer after it. Writing over either half
    /// leaves the other behind, and a renderer meeting a width-2 cell whose second column holds
    /// something else draws a two-column glyph into one column and shifts the rest of the row.
    /// </remarks>
    /// <param name="here">
    /// The width already under the cursor, read by the caller. It decides both cases on its own --
    /// 2 means a wide character whose spacer at X+1 is about to be orphaned, 0 means this IS a
    /// spacer and the character at X-1 is -- and the caller needs it anyway to decide whether
    /// there is a repair to make, so reading it twice would be reading it once too often.
    /// </param>
    private void RepairSplitWideCell(BufferLine line, int here)
    {
        if (here == 2)
        {
            if (_buffer.X + 1 < _terminal.Cols)
            {
                var orphan = BufferCell.Space;
                orphan.Attributes = line[_buffer.X].Attributes;
                line.SetCell(_buffer.X + 1, ref orphan);
            }
        }
        else if (here == 0 && _buffer.X > 0)
        {
            var orphan = BufferCell.Space;
            orphan.Attributes = line[_buffer.X - 1].Attributes;
            line.SetCell(_buffer.X - 1, ref orphan);
        }
    }

    /// <summary>The Fitzpatrick skin tone modifiers, U+1F3FB to U+1F3FF.</summary>
    private static bool IsSkinToneModifier(int codePoint)
        => codePoint >= SkinToneFirst && codePoint <= SkinToneLast;

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
            // GB11 is ZWJ x \p{Extended_Pictographic}: the ZWJ keeps the cluster only when what
            // follows is itself a pictograph. Accepting anything meant an emoji followed by a
            // letter -- man, ZWJ, e-acute -- swallowed the letter into the emoji's cell, where it
            // stopped being text the user could see or select.
            var continuesCluster = _zwjContinuation is { } pending
                                   && pending.Row == _buffer.Y + _buffer.YBase
                                   && pending.Col == _buffer.X
                                   && IsExtendedPictographic(codePoint);
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
                // Find the previous cell to combine with — unless the tracked neighbour already
                // says no: Korean prose is syllable after non-joining syllable, and each one was
                // paying the full line fetch below just to be refused.
                if ((continuesCluster
                     || (uint)(codePoint - 0x0900) > 0xD7FB - 0x0900   // marks: skip the call, not just the checks
                     || codePoint == ZeroWidthJoiner                   // inside the hull but never a candidate
                     || !RefusesSequenceCheaply(codePoint))
                    && TryAppendToPreviousCell(data, codePoint))
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
        // Translate and measure ONCE, before the wrap decision, because both need the answer: a
        // wide character written at the last column has no room for its spacer, so the wrap test
        // has to know the width, and the write below needs it too. Measuring in both places cost
        // alt-redraw 32% -- every printed character paid for two translations and two width
        // lookups to learn the same thing twice.
        var translatedData = data;
        if (data.Length == 1)
        {
            // A single shift outranks GL for this character and is spent doing it.
            if (_singleShiftPending)
            {
                translatedData = Charsets.TranslateChar(data[0], _singleShiftCharset);
                _singleShiftCharset = null;
                _singleShiftPending = false;
            }
            else
            {
                translatedData = Charsets.TranslateChar(data[0], _activeCharset);
            }
        }

        var width = GetStringCellWidth(translatedData);

        // WrapLimit is not free -- it asks whether the cursor is inside the margin columns -- and
        // the wide-character test made a second call to it for every printed character. Computed
        // once and shared with the autowrap test below, which is the only other reader.
        var wrapLimit = WrapLimit();

        // A wide character needs TWO columns, so the wrap test has to know its width: written at
        // the last column it was stored there with no room for its spacer, leaving a width-2 cell
        // in one column and the cursor one past the pending-wrap position.
        if (width == 2 && _buffer.X == wrapLimit && _terminal.Options.Wraparound)
            _buffer.SetCursorRaw(wrapLimit + 1, _buffer.Y);

        // Whether the cursor moved to another row, which is the only thing that can make the limit
        // computed above stale -- WrapLimit asks where the cursor is, and a wrap is the one event
        // here that puts it somewhere else.
        var wrapped = false;

        if (_buffer.X > wrapLimit && !(wrapped = ResolveAutowrap()))
        {
            // Wrapping is off and the cursor is past the last column. DECAWM off does not mean
            // "discard": the VT100, xterm and xterm.js all keep OVERWRITING the last column, so a
            // program drawing a full-width status bar with wrapping disabled sees its final
            // character land rather than vanish. Backing onto that column is what makes the write
            // below overwrite it.
            //
            // Done here rather than inside ResolveAutowrap because that helper answers "was the
            // wrap resolved", and the OSC 66 sized-block path depends on its false to move a whole
            // block back by its own width instead of one column.
            //
            // The value computed above, not a second call: reaching here means wrapping is off, so
            // the early-wrap block did not run and nothing has moved the cursor since.
            _buffer.SetCursor(wrapLimit, _buffer.Y);
        }

        // DECAWM off and a two-column character at the last column: it does not fit, and there is
        // nowhere to wrap it to. Stored anyway it became a width-2 cell whose spacer the margin
        // then refused -- the orphaned half this change exists to prevent, produced by the one
        // path that skipped the early wrap above. xterm.js parks the cursor at the last column
        // and drops the character; so does this.
        if (width == 2 && _buffer.X >= wrapLimit && !_terminal.Options.Wraparound)
            return;

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

            // Shift cells right -- to the RIGHT MARGIN for a cursor inside one, and what gets
            // pushed past it is gone. Shifting to the screen edge let inserted text spill a pane.
            line?.CopyCellsFrom(line, _buffer.X, _buffer.X + width, wrapLimit + 1 - _buffer.X - width, false);
        }

        // Printing over a picture needs no special case here any more. SetCell splits a SIXEL run
        // around the written column, because a Sixel is content and printing replaces that part of
        // it; a Kitty run is left alone, because it is an overlay whose z-index orders it against
        // the text. Both fall out of where a picture is stored rather than from anything done here.

        // Overwriting half of a wide character leaves the other half behind: a width-2 cell whose
        // second column now holds something else, or a spacer with nothing in front of it. Both
        // make the renderer draw a two-column glyph into one column. The erase paths get this from
        // BufferLine.Fill; printing writes a single cell and has to say so itself.
        // The margin the spacer has to fit inside, for a wide character only -- ASCII never asks,
        // so it never pays for it. Recomputed only when a wrap actually moved the cursor: the
        // limit is a question about where the cursor is, and nothing else between there and here
        // moves it. Asking again unconditionally meant a WrapLimit call for every CJK character in
        // a corpus made of them, to be told what the value at the top of the method already said.
        var spacerLimit = width == 2 ? (wrapped ? WrapLimit() : wrapLimit) : 0;
        var writesSpacer = width == 2 && _buffer.X + 1 <= spacerLimit;

        // Guarded at the CALL, the lesson this file keeps teaching: the repair is array reads on
        // EVERY printed character, and a line that has never held a wide character cannot have an
        // orphan to fix. HasWideCells is one field read, and it is false for the ASCII that makes
        // up most of every frame.
        //
        // The width under the cursor is read here too, so the ordinary case reaches no call at
        // all: on a line that HAS wide cells -- which is every line of CJK, where the latch is
        // true from the first character on -- an unconditional call is one per printed character.
        if (line is not null && line.HasWideCells)
        {
            var here = line.GetWidth(_buffer.X);

            // here == 2 means a wide character is being overwritten and its spacer at X+1 is
            // orphaned; here == 0 means this IS a spacer and the character at X-1 is. But a wide
            // character about to be written covers X+1 ITSELF, so blanking it first is a write the
            // spacer below immediately repeats -- and CJK over CJK is what the unicode corpus is
            // made of. Only the half the incoming character does not cover needs repairing.
            if (here == 0 || (here == 2 && !writesSpacer))
                RepairSplitWideCell(line, here);
        }

        // Set the cell
        line?.SetCell(_buffer.X, ref cell);

        // Handle wide characters. The spacer is bounded by the right MARGIN rather than the screen
        // -- otherwise a double-width character sitting on the last column of a region plants its
        // spacer in the pane next door. Identical to the old test when no margins are set, since
        // the limit is then the last column.
        if (writesSpacer)
        {
            var spacer = BufferCell.Empty;
            spacer.Attributes = _curAttr;
            line?.SetCell(_buffer.X + 1, ref spacer);
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
        // With wrapping off there is no phantom column to pend in: the cursor PARKS at the
        // limit, which is also what keeps its reported position on the screen.
        if (!_terminal.Options.Wraparound && _buffer.X + width > wrapLimit)
            _buffer.SetCursor(wrapLimit, _buffer.Y);
        else
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

    private void SetCharset(CharsetMode mode, string charsetId) =>
        Designate(mode, charsetId, ninetySix: false);

    /// <summary>
    /// Designates a 96-character set: <c>ESC - Ps</c> (G1), <c>ESC . Ps</c> (G2),
    /// <c>ESC / Ps</c> (G3).
    /// </summary>
    /// <remarks>
    /// Latin-1 is the one that matters and it is a pass-through, so the visible effect of
    /// getting this wrong is nil until the identifier collides: 'A' is Latin-1 in the
    /// 96-set space and the United Kingdom set in the 94-set one. Anything else is left as
    /// ASCII rather than guessed at.
    /// </remarks>
    private void SetNinetySixCharset(CharsetMode mode, string charsetId) =>
        Designate(mode, charsetId, ninetySix: true);

    /// <summary>Records a designation and resolves it, for both spaces.</summary>
    /// <remarks>
    /// The ID is kept, not just the table it resolves to. A national set means one thing with
    /// DECNRCM set and ASCII without it, so the designation has to outlive the resolution -- a
    /// program designating French and then enabling NRC mode expects French, and it never
    /// designates again.
    /// </remarks>
    private void Designate(CharsetMode mode, string charsetId, bool ninetySix)
    {
        _charsetIds[mode] = charsetId;
        if (ninetySix)
            _ninetySixSets.Add(mode);
        else
            _ninetySixSets.Remove(mode);

        _charsets[mode] = Resolve(charsetId, ninetySix);
        RefreshActiveCharset();
    }

    /// <summary>What a designation means RIGHT NOW, given the mode state.</summary>
    /// <remarks>
    /// One answer for the three callers that need it -- designation, DECRC and DECNRCM -- because
    /// the question is the same one and they got different answers while each resolved separately.
    /// The space is half the question: 'A' is ISO Latin-1 after ESC - and the United Kingdom set
    /// after ESC (, so an identifier without the space it came from cannot be resolved at all.
    /// </remarks>
    private Dictionary<char, string>? Resolve(string charsetId, bool ninetySix) =>
        ninetySix
            ? Charsets.ASCII
            : Charsets.GetCharset(charsetId, _terminal.NationalReplacementCharsets);

    /// <summary>Re-resolves every designation, for when DECNRCM changes under them.</summary>
    /// <remarks>
    /// Through the space each was designated in. 'A' is ISO Latin-1 after ESC - and the
    /// United Kingdom set after ESC (, so walking every designation through the 94-set
    /// lookup would hand a program that asked for Latin-1 the UK set the first time
    /// DECNRCM was toggled -- turning its '#' into a pound sign one mode change after the
    /// designation that was handled correctly.
    /// </remarks>
    internal void RefreshDesignatedCharsets()
    {
        foreach (var mode in GSets)
            _charsets[mode] = Resolve(_charsetIds[mode], _ninetySixSets.Contains(mode));

        RefreshActiveCharset();
    }

    /// <summary>The designation a G-set is holding, for DECSC to save.</summary>
    internal (string Id, bool NinetySix) DesignationOf(CharsetMode mode) =>
        (_charsetIds[mode], _ninetySixSets.Contains(mode));

    /// <summary>
    /// Puts a saved designation back, for DECRC, resolving it against the mode state as it is NOW.
    /// </summary>
    /// <remarks>
    /// The DESIGNATION is what DECSC saves, not the table it had resolved to. Saving the table
    /// restores the right glyphs and leaves the identifier behind it stale, so the next DECNRCM
    /// re-resolves the restored slot from whatever was designated AFTER the save -- and a program
    /// doing ESC ( 0, DECSC, ESC ( R, DECRC gets its line drawing back and then loses it again the
    /// first time the mode moves, arbitrarily far from the DECRC that caused it.
    ///
    /// Resolving at restore time rather than replaying the saved table is also the right answer
    /// when DECNRCM moved BETWEEN the save and the restore.
    /// </remarks>
    internal void RestoreDesignation(CharsetMode mode, (string Id, bool NinetySix) designation)
    {
        _charsetIds[mode] = designation.Id;
        if (designation.NinetySix)
            _ninetySixSets.Add(mode);
        else
            _ninetySixSets.Remove(mode);

        _charsets[mode] = Resolve(designation.Id, designation.NinetySix);
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
        _singleShiftCharset = null;
        _singleShiftPending = false;
        RefreshActiveCharset();
    }

    /// <summary>
    /// LS2 (ESC n) and LS3 (ESC o) - lock G2 or G3 into GL until the next shift.
    /// </summary>
    /// <remarks>
    /// SO and SI above are the same operation for G1 and G0. G2 and G3 could be DESIGNATED
    /// before this existed and never invoked, so whatever a program put in them printed as
    /// ASCII -- and silently, since an unimplemented set and US ASCII look identical.
    /// </remarks>
    public void LockingShift(CharsetMode mode)
    {
        _currentCharset = mode;
        RefreshActiveCharset();
    }

    /// <summary>
    /// SS2 (ESC N) and SS3 (ESC O) - invoke G2 or G3 for the NEXT character only.
    /// </summary>
    /// <remarks>
    /// The single shift is held pending rather than swapped in, so it expires by being consumed
    /// instead of by something remembering to put the old set back. A shift with no character
    /// after it simply never fires.
    /// </remarks>
    public void InvokeSingleShift(CharsetMode mode)
    {
        _singleShiftCharset = _charsets.GetValueOrDefault(mode);
        _singleShiftPending = true;
    }

    /// <summary>
    /// Resets charset state to defaults.
    /// </summary>
    public void ResetCharsets()
    {
        _ninetySixSets.Clear();
        foreach (var mode in GSets)
        {
            // Seeded rather than cleared: US ASCII IS the designation every slot starts with, and
            // saying so is what keeps every walk over the four total.
            _charsetIds[mode] = UsAsciiId;
            _charsets[mode] = Charsets.ASCII;
        }

        _currentCharset = CharsetMode.G0;
        RefreshActiveCharset();
    }

    /// <summary>The identifier US ASCII is designated by, which is where every G-set starts.</summary>
    private const string UsAsciiId = "B";

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

    /// <summary>The column a wrapped line begins on: the left margin, for the same reason.</summary>
    private int WrapHome()
    {
        if (_buffer.MarginsAreFullWidth)
            return 0;

        // The mirror of WrapLimit's rule: a wrap that happened at the box's right edge lands on
        // the box's left margin, wherever the print started.
        var x = _buffer.PendingWrap ? _buffer.X - 1 : _buffer.X;
        return x <= _buffer.ScrollRight ? _buffer.ScrollLeft : 0;
    }

}
