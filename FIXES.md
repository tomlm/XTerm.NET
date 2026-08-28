# Resize reflow for the normal buffer

## Summary

This change ports xterm.js 5.5.0 resize reflow into XTerm.NET. Shrinking column width re-wraps long logical lines onto additional `IsWrapped` rows instead of truncating them; growing width merges wrapped groups back. The alternate buffer is excluded via an explicit `hasScrollback: false` constructor flag.

A related latent bug is fixed: when the buffer was at capacity and row count shrank, `CircularList.Resize` ran before trimming and kept the oldest lines, silently discarding the live screen bottom. Resize now trims from the top (raising `Trimmed`) before shrinking capacity.

A second bug, in the reflow itself, is fixed here: shrinking a buffer that held an EMPTY wrapped group threw `IndexOutOfRangeException`. `ReflowSmallerGetNewLineLengths` loops while `cellsAvailable < cellsNeeded`, so a group whose trimmed length is zero returns an empty array, and `ReflowSmaller` read `[Length - 1]` from it. Only a one-row group can be empty -- every row of a group except the last counts as a full row of cells regardless of content -- so it takes a blank continuation row at index 0 with an unwrapped row beneath, which is what the scrollback leaves once the row being continued is trimmed away. Twelve spaces at six columns, two further lines, and a narrowing resize reproduce it through `Terminal.Write` alone.

## Why

Without reflow, shrinking a terminal window and growing it back left every long line permanently truncated at the narrowest width — the primary defect tracked as ISS-007. Scrollback lines were also lost on capacity shrink because `CircularList.Resize` preserved the wrong end of the buffer.

## Resize edge cases found in review

Six further defects, each reproduced before being fixed and each reachable from ordinary use:

- **One-column reflow hung, then threw `OutOfMemoryException`.** A wide glyph at the wrap boundary made the new line length zero, so `ReflowSmallerGetNewLineLengths` never advanced and appended rows until the list could not grow. A wide glyph cannot be shown in one column, so it is clipped.
- **The viewport adjustment popped rows the outer loop was still walking**, throwing `IndexOutOfRangeException`.
- **A line expanding past the remaining capacity indexed below zero** in the batched rebuild, throwing. Rows that do not fit are the oldest, which capacity trimming discards anyway.
- **`Math.Min` dropped the cursor's lower bound.** Moving to the new column count was the point of that change, but a negative cursor -- which `SetCursorRaw` exists to allow -- survived the resize and left the buffer reporting an out-of-bounds position.
- **The viewport was shifted by the trim amount rather than recomputed.** A 5-row buffer with 5 of scrollback resized to 3 rows showed rows 3..5 of 8, with the live bottom unseen at row 7 and later output landing outside the visible area.
- **A zero-row buffer could never be initialised by a later resize**, because the row-fill loop had moved inside a "has lines" guard. `Lines.Length` stayed 0 and the next write indexed an empty list.

Two of these are the same root cause: this is a port from JavaScript, where reading past the end of an array yields `undefined` and falls into a null check. In C# the identical read throws.

## Files changed

- `src/XTerm.NET/Buffer/BufferReflow.cs` — pure reflow functions ported from `BufferReflow.ts`
- `src/XTerm.NET/Buffer/TerminalBuffer.cs` — `Resize` restructure, `ReflowLarger`/`ReflowSmaller`, `hasScrollback` flag
- `src/XTerm.NET/Buffer/BufferLine.cs` — `GetWidth`, `HasContent`, `ReplaceCells`; `GetTrimmedLength` wide-char width
- `src/XTerm.NET/Buffer/CircularList.cs` — `SetLength` for reflow batching
- `src/XTerm.NET/Terminal.cs` — alt buffer `hasScrollback: false`
- `src/XTerm.NET.Tests/Buffer/BufferReflowTests.cs` — pure-function tests
- `src/XTerm.NET.Tests/Buffer/BufferTests.cs` — reflow integration tests
- `src/XTerm.NET.Tests/Buffer/ReflowEmptyGroupTests.cs` — regression tests for the empty wrapped group
- `src/XTerm.NET.Tests/Buffer/ResizeEdgeCaseTests.cs` — regression tests for the six resize edge cases above

## Validation

```powershell
dotnet test src/XTerm.NET.slnx
```

Result on this branch:

```text
Passed: 728
Failed: 0
Skipped: 0
```

# Docker progress rendering fixes

## Summary

This branch fixes a terminal cell-width bug that affected Docker Compose progress output in Termrig and adds regression coverage around the related VT behavior.

Termrig reference links:

- Project: https://github.com/jchristn/Termrig
- Branch containing the downstream reproduction, compatibility workaround, and PTY recorder: https://github.com/jchristn/Termrig/tree/fix/terminal
- Termrig commit that added the first-class PTY recorder used for future raw-byte captures: https://github.com/jchristn/Termrig/commit/3a075e3acadb3d9b3815f8d27209dc13d63787f8
- Terminal integration code that consumes XTerm.NET through the Avalonia terminal control: https://github.com/jchristn/Termrig/tree/fix/terminal/src/ThirdParty/Iciclecreek.Avalonia.Terminal

The confirmed upstream defect was that `InputHandler.GetStringCellWidth` treated any code point classified by `NeoSmart.Unicode.Emoji.IsEmoji` as width 2. That makes U+2714 HEAVY CHECK MARK (`\u2714`) consume two terminal cells even when it is emitted in text presentation. Docker Compose uses that character without U+FE0F emoji presentation, and Windows `cmd.exe` renders it as a single-cell icon. XTerm.NET therefore shifted the rest of those progress rows by one cell.

The fix is to use `Wcwidth.UnicodeCalculator.GetWidth` for the base width and keep the existing variation-selector handling:

- `\u2714` remains width 1.
- `\u2714\uFE0F` becomes width 2 because U+FE0F explicitly requests emoji presentation.
- Existing wide emoji and CJK width behavior continues to come from `UnicodeCalculator`.
# Origin mode and scroll region fixes

## Summary

This change fixes DEC origin-mode cursor positioning with scroll regions. These changes are core terminal emulator behavior and are not specific to Termrig, Avalonia, ConPTY, or any host renderer.

The fixed behavior is:

- `DECSTBM` / `CSI t;b r` moves the cursor to home after setting the scroll region.
- `CUP` / `CSI row;col H` and `HVP` / `CSI row;col f` treat row coordinates as relative to the scroll region when `DECOM` / origin mode is enabled.
- `VPA` / `CSI row d` applies the same origin-mode row translation.
- enabling origin mode moves the cursor to the top margin of the scroll region; disabling origin mode moves the cursor to absolute home.

## Why

Full-screen and prompt-oriented terminal applications often reserve a bottom input or status row by setting a scroll region for the output area. They then use origin-mode cursor addressing inside that region.

If the emulator treats those row coordinates as absolute screen rows, application output can be written outside the intended scroll region. In real-world terminal UIs this can leave stale prompt/status rows in scrollback or place rewritten content on the wrong line.

## Files changed

- `src/XTerm.NET/InputHandler.cs`
  - Replaced broad emoji classification width override with Unicode cell-width calculation.
- `src/XTerm.NET.Tests/InputHandlerTests.cs`
  - Added regression tests for text-presentation checkmark width.
  - Added regression tests for emoji-presentation checkmark width.
  - Added a Docker-style progress alignment test.
  - Added explicit coverage for `CSI Ps C` cursor-forward clamping.
  - Added explicit coverage for `CSI Ps X` erase-character preserving cursor position.

## Reproduction

### Docker Compose command

Run Docker Compose in a narrow-ish terminal where Compose emits progress rows:

```cmd
cd <path-to-your-compose-project>
docker compose up -d
docker compose down
```

Expected output, matching Windows `cmd.exe`, keeps the status column aligned:

```text
 ✔ Network docker_default              Created
 ✔ Container docker-litegraph-1        Healthy
```

Before this fix, rows using `\u2714` could render one cell short before the status text:

```text
 ✔ Network docker_default             Created
```

The missing space is caused by XTerm.NET counting `\u2714` as two cells while Docker/cmd treat it as one text cell.

### Minimal checkmark-width reproduction

```csharp
var terminal = new Terminal(new TerminalOptions { Cols = 20, Rows = 3 });
terminal.Write("\u2714X");

// Expected:
// cursor X == 2
// cell 0 contains \u2714 with Width == 1
// cell 1 contains X with Width == 1
```

Before this fix:

```text
cursor X == 3
cell 0 Width == 2
cell 1 was a spacer
cell 2 contained X
```

### Emoji-presentation checkmark

The variation selector case must still be double-width:

```csharp
var terminal = new Terminal(new TerminalOptions { Cols = 20, Rows = 3 });
terminal.Write("\u2714\uFE0FX");

// Expected:
// cursor X == 3
// first glyph Width == 2
// following spacer Width == 0
// X Width == 1
```

This verifies that the fix does not flatten explicit emoji presentation to single width.

### Docker-style status column

Docker Compose progress rows use a text icon, a resource kind/name, cursor movement, then a status:

```csharp
var terminal = new Terminal(new TerminalOptions { Cols = 80, Rows = 3 });
const string prefix = " \u2714 Network docker_default";

terminal.Write(prefix);
terminal.Write("\x1B[28C");
terminal.Write("Created");

int statusColumn = prefix.Length + 28;
Assert.Equal("Created", terminal.Buffer.Lines[0]!.TranslateToString(false, statusColumn, statusColumn + 7));
```

With a two-cell checkmark, this status starts one cell later than expected.

## Related VT behavior verified

The Docker progress stream also uses cursor and erase sequences heavily. The branch adds tests documenting the intended behavior so future changes do not regress it.

### `CSI Ps X` erase-character

`CSI Ps X` erases `Ps` cells from the current cursor position. It must not move the cursor and must not wrap.

```csharp
var terminal = new Terminal(new TerminalOptions { Cols = 20, Rows = 3 });
terminal.Write("abcdef");
terminal.Buffer.SetCursor(2, 0);
terminal.Write("\x1B[3X");

// Expected line: "ab   f"
// Expected cursor: X == 2, Y == 0
```

### `CSI Ps C` cursor-forward

`CSI Ps C` moves right but clamps at the right margin. It must not wrap to the next row.

```csharp
var terminal = new Terminal(new TerminalOptions { Cols = 10, Rows = 3 });
var handler = new InputHandler(terminal);
terminal.Buffer.SetCursor(7, 0);

var parameters = new Params();
parameters.AddParam(20);
handler.HandleCsi("C", parameters);

// Expected cursor: X == 9, Y == 0
```

## Termrig compatibility note

Termrig currently has a local Docker progress normalizer/workaround on the `fix/terminal` branch that trims trailing line-ending padding and rewrites some Docker progress cursor sequences before passing output into XTerm.NET. That workaround was useful while diagnosing overlapping and duplicate progress rows. Once Termrig consumes an XTerm.NET version that includes this width fix and any future upstream parser fixes, Termrig should re-test without the local normalizer and remove as much of that workaround as possible.

The first-class PTY recorder added to Termrig should be used for future reports. It records raw PTY bytes before any normalization, which makes reproductions suitable for XTerm.NET issues and pull requests.
  - Added shared row translation for origin-mode cursor addressing.
  - Applied that translation to `CUP` / `HVP` and `VPA`.
  - Homed the cursor after `DECSTBM`.
  - Homed to the top margin when origin mode is enabled.
- `src/XTerm.NET.Tests/InputHandlerTests.cs`
  - Added regression coverage for scroll-region cursor homing.
  - Added regression coverage for origin-relative `CUP` / `HVP`.
  - Added regression coverage for origin-relative `VPA`.
- `src/XTerm.NET.Tests/ModeHandlingTests.cs`
  - Added regression coverage for enabling origin mode with a non-zero top margin.

## Minimal reproductions

### Scroll region homes the cursor

```csharp
var terminal = new Terminal(new TerminalOptions { Cols = 20, Rows = 5 });
var handler = new InputHandler(terminal);
terminal.Buffer.SetCursor(10, 4);

var parameters = new Params();
parameters.AddParam(2);
parameters.AddParam(4);
handler.HandleCsi("r", parameters);

Assert.Equal(0, terminal.Buffer.X);
Assert.Equal(0, terminal.Buffer.Y);
```

### Origin-mode `CUP` is relative to the scroll region

```csharp
var terminal = new Terminal(new TerminalOptions { Cols = 20, Rows = 5 });
var handler = new InputHandler(terminal);
terminal.Buffer.SetScrollRegion(1, 3);
terminal.OriginMode = true;

var parameters = new Params();
parameters.AddParam(3);
parameters.AddParam(20);
handler.HandleCsi("H", parameters);

Assert.Equal(19, terminal.Buffer.X);
Assert.Equal(3, terminal.Buffer.Y);
```

### Origin-mode `VPA` is relative to the scroll region

```csharp
var terminal = new Terminal(new TerminalOptions { Cols = 20, Rows = 5 });
var handler = new InputHandler(terminal);
terminal.Buffer.SetScrollRegion(1, 3);
terminal.OriginMode = true;
terminal.Buffer.SetCursor(10, 1);

var parameters = new Params();
parameters.AddParam(3);
handler.HandleCsi("d", parameters);

Assert.Equal(10, terminal.Buffer.X);
Assert.Equal(3, terminal.Buffer.Y);
```

## Not included

This change intentionally does not include:

- host-rendering changes
- PTY or ConPTY line-ending policy
- Avalonia integration changes
- Termrig-specific output normalization
- Docker Compose cell-width fixes from the earlier Docker progress branch

Those are separate concerns. This change is limited to standard VT scroll-region and origin-mode semantics in XTerm.NET.

## Validation

Run from this repository root:

```powershell
dotnet test src/XTerm.NET.slnx
```

Result on this branch:

```text
Passed: 589
Failed: 0
Skipped: 0
```
dotnet test src/XTerm.NET.slnx --no-restore
```

Expected result: all tests pass.

---

# Sixel graphics

## Summary

Sixel images (`ESC P … q … ESC \`) are decoded and placed in the buffer. Each cell an image covers
carries a reference to one shared, immutable `TerminalImage` plus the coordinates of the tile it
shows, so a picture behaves like terminal content rather than an overlay.

Sixel was not merely unimplemented before this — it was unreachable. `EscapeSequenceParser`
collapsed `DcsEntry`/`DcsParam`/`DcsIgnore`/`DcsPassthrough` into a single "discard every byte until
ST" case, `_dcs` was allocated but never written to, and the `Dcs` event was marked `[Obsolete]`
because nothing raised it. The payload never reached anything that could decode it.

## Why storage on the cell

`BufferCell` is a struct, and `InputHandler.Print` builds a fresh one for every character. That
single fact gives the whole feature its semantics for free:

| Terminal action | Existing mechanism | Effect on images |
|---|---|---|
| Print over an image cell | `new BufferCell{…}` + `SetCell` | the new struct has no image, so that cell reverts to text |
| ED / EL / ECH / DECALN | `Fill`/`ReplaceCells` with `BufferCell.Space` | tiles cleared |
| Scroll / scrollback | `CircularList` moves whole `BufferLine` objects | tiles ride along |
| Line trimmed from scrollback | line dereferenced | the image is collected with its last tile |
| Selection / `TranslateToString` | reads `cell.Content` | image cells hold `" "`, so copying yields blanks |

A reference to a shared image rather than a per-cell bitmap slice: identical overwrite granularity,
one allocation per image instead of columns times rows, and a host can coalesce a run of adjacent
tiles into a single draw call. `BufferCell` grows from 32 to 40 bytes — the packed tile `int` fits
in padding the reference already forces — which is roughly 0.65 MB on an 80-column buffer with 1000
lines of scrollback.

## Two live bugs fixed along the way

- **`CSI ? 1;1;0 S` scrolled the screen.** XTSMGRAPHICS shares its final character with SCROLL UP,
  and `ToCsiCommand` strips the private marker before the lookup, so a graphics capability query
  was routed to the scroll handler. Every Sixel-capable program sends one during startup, which
  made this routine rather than obscure. `ScrollUp` is now guarded on `isPrivate` and the query is
  answered.
- **The primary DA reply did not advertise Sixel.** `libsixel`, `chafa`, `img2sixel` and everything
  built on them read attribute `4` from `CSI c` and send text art instead of pictures without it.
  The reply is now `CSI ? 1 ; 2 ; 4 c`, following `Options.SixelEnabled` so it never claims a
  capability that is switched off.

## Decisions worth recording

- **Images are dropped on a column resize.** Reflow re-wraps a logical line by copying ranges of
  cells between lines; tiles carried through it would reassemble as a shuffled mosaic — every piece
  intact, in the wrong place. A change of row count alone moves whole lines and keeps them.
- **The DCS payload is streamed, not buffered.** A full-screen Sixel runs to hundreds of kilobytes.
  The parser raises `DcsHook`/`DcsPut`/`DcsUnhook` and only accumulates a whole-payload string for
  the legacy `Dcs` event when something is subscribed and the sequence stays under 4 KB.
- **An abandoned sequence is distinguishable from a finished one.** `DcsUnhook` reports whether a
  string terminator ended it, so a truncated image is discarded rather than half-drawn. An `ESC`
  mid-payload is resolved one character late, since `ESC \` terminates and anything else abandons.
- **Sixel colour registers are kept apart from `ColorPalette`.** They are a separate numbering that
  an image may redefine as it draws, and doing that to the palette the renderer reads on its hot
  path would repaint the text as a side effect of showing a picture.
- **Nothing in the decoder throws.** The payload is untrusted output from another process; a
  nonsense register, an absurd repeat count or a truncated stream yields no image, not an exception
  escaping into the parser.
- **A host must answer the window queries from the grid, not from its control.** Not a change here
  -- an unhandled query still produces no reply, deliberately -- but the reason the README now spells
  the handler out. An image viewer works out the cell size for itself by dividing the pixel size from
  `CSI 14 t` by the row count it already has, so anything else in that figure (a scrollbar, window
  chrome, or the strip below the last row, since the grid is a truncated division) is read back as
  picture that does not fit. It runs off the bottom and scrolls the screen. The only safe answer is
  `Cols * CellWidthPixels` by `Rows * CellHeightPixels`, which is also what xterm reports.
- **The image budget is swept by the byte, not by the picture.** A program animating with Sixel
  draws one image per frame, and sweeping on each would walk every cell of both buffers ten times a
  second. Bytes placed are counted instead, and a sweep runs only once a budget's worth has arrived
  — one scan per budget rather than one per picture, at the cost of the buffer sitting up to one
  budget over before it is trimmed.

## Files changed

- `src/XTerm.NET/Parser/EscapeSequenceParser.cs` — real DCS state machine; streaming hook/put/unhook
- `src/XTerm.NET/Common/Types.cs` — `ParserState.DcsIntermediate`
- `src/XTerm.NET/Events/ParserEvents.cs` — `DcsHookEventArgs`, `DcsPutEventArgs`, `DcsUnhookEventArgs`
- `src/XTerm.NET/Graphics/TerminalImage.cs` — immutable BGRA image plus tile geometry
- `src/XTerm.NET/Graphics/SixelDecoder.cs` — streaming DECSIXEL decoder
- `src/XTerm.NET/Graphics/SixelPalette.cs` — VT340 defaults, RGB and HLS colour
- `src/XTerm.NET/Buffer/BufferCell.cs` — `Image`, packed `ImageTile`, equality
- `src/XTerm.NET/Buffer/BufferLine.cs` — `ClearImages`, `HasImages`
- `src/XTerm.NET/Buffer/TerminalBuffer.cs` — `ClearImages`, dropped on column resize
- `src/XTerm.NET/InputHandler.cs` — DCS dispatch, `PlaceImage`, DA, modes 80/1070/8452, XTSMGRAPHICS
- `src/XTerm.NET/Terminal.cs` — parser wiring, Sixel mode flags, `EnforceImageBudget`
- `src/XTerm.NET/Options/TerminalOptions.cs` — `SixelEnabled`, cell pixel size, budgets
- `src/XTerm.NET.Tests/Parser/DcsSequenceTests.cs`
- `src/XTerm.NET.Tests/Graphics/SixelDecoderTests.cs`
- `src/XTerm.NET.Tests/Graphics/SixelPlacementTests.cs`
- `src/XTerm.NET.Tests/Graphics/ImageCellLifetimeTests.cs`
- `src/XTerm.NET.Tests/Graphics/GraphicsAttributesTests.cs`

## Validation

```powershell
dotnet test src/XTerm.NET.slnx
```

```text
Passed: 847
Failed: 0
Skipped: 0
```

# Kitty graphics protocol

## Summary

The Kitty graphics protocol (`ESC _ G <control> ; <base64> ESC \`) is decoded and placed in the
buffer alongside Sixel. `icat`, `chafa -f kitty`, `timg -pk`, `yazi` and `image.nvim` draw pictures
against a host that renders tiles.

Kitty was unreachable for the same reason Sixel had been. `EscapeSequenceParser` collapsed SOS, PM
and APC into one state that hunted for the terminator and discarded every byte, so the payload never
reached anything that could decode it. APC now has a real streaming path -- `ApcHook`/`ApcPut`/
`ApcUnhook` -- mirroring the DCS one, routed from `ESC _` only; `ESC ^` and `ESC X` keep the discard
path they should have.

Scope in: transmit (`a=t`), transmit-and-display (`a=T`), place (`a=p`), delete (`a=d`), query
(`a=q`); chunked payloads; RGB, RGBA and PNG; zlib; cropping; cell-box scaling; cursor policy; quiet
levels; and U+10EEEE Unicode placeholders. Out at the time: animation, z-index and overlapping
placements — all three since done, and covered further down.

## Placements, because a picture can now appear twice

Sixel decodes a picture and shows it once, so a cell could reference the `TerminalImage` directly.
Kitty transmits once under an id and places as often as it likes, so "which image" no longer answers
"which pixels, and where". Cells reference an `ImagePlacement` instead -- an image, the source
rectangle it takes, and the cell box it fills -- and `BufferCell.Image` stays as a computed
`Placement?.Image` so existing readers compile untouched. Still two references per cell, so the
struct does not grow.

This immediately surfaced a real bug in the host renderer. `AppendImageRun` continued a run on
`ReferenceEquals(current.Image, image)`, which is safe with one decode per placement and wrong the
moment two appearances of one picture abut horizontally: they coalesce into a single strip and blit
the wrong pixels into both halves. The predicate now compares the placement.

## Two tile geometries, which are not the same formula

`TryGetTileSource` has two modes, and collapsing them would have quietly resampled every existing
Sixel image:

- **Natural** -- fixed cell pitch, edge tiles clipped. Sixel always, and Kitty with no `c`/`r`.
- **Stretched** -- the source rectangle divided proportionally across the cell box, which is what
  `c`/`r` mean.

A 1160px-wide image at a 14px cell needs 83 cells, and 83 x 14 = 1162. The two forms therefore
disagree on *every* tile, not merely the last one: tile 0 is 14px wide naturally and 13px
proportionally. Sixel constructs itself in natural mode, and
`ImagePlacementTests.A_natural_placement_lays_tiles_exactly_where_the_image_does` pins the
equivalence so the migration cannot drift.

Stretched tile boundaries are computed from the tile index at both edges (`left` from `tileCol`,
`right` from `tileCol + 1`) rather than as origin-plus-width, so adjacent tiles meet exactly with no
seam or overlap from rounding.

## Decisions worth recording

- **File, temp-file and shared-memory transmission are refused, not implemented.** `t=f`/`t=t`/`t=s`
  would have the terminal open a path named by the program it hosts, and a host generally holds more
  privilege than its guest. They are answered with `ENOTSUP` rather than ignored, so a client falls
  back instead of waiting. `t=d` is the only medium accepted.
- **Base64 is accumulated as text and decoded once at `m=0`.** Decoding per chunk is only safe if
  every chunk is a multiple of four characters, which the protocol does not promise.
- **`a=q` places nothing.** It is the detection path, and `StringSequenceTests`
  `Text_after_a_string_sequence_still_prints` was already the guard: it writes
  `ESC_Gi=31,s=1,v=1,a=q,t=d,f=24;AAAA` and asserts row 0 is exactly `"OK"`. `AAAA` decodes to three
  zero bytes -- a perfectly valid 1x1 RGB image -- so a naive decode-and-place breaks it.
- **A separate `_apcPendingUnhook` flag.** The DCS path resolves a mid-payload `ESC` one character
  late, since `ESC \` terminates and anything else abandons. Sharing that flag with APC would let a
  DCS and an APC sequence interleaved cross-fire each other's unhook and close the wrong payload.
- **A registry, because a Kitty image can be live with zero placements.** "Which images exist" was
  answered by scanning cells, which cannot express a picture transmitted but not yet shown. Stored
  images are held in insertion order under their own `MaxImageRegistryBytes` budget and evicted
  oldest first.
- **Crop rectangles are clamped, not rejected.** They come from another process; a `w` that runs off
  the right edge should show the part that exists rather than nothing at all.
- **Interlaced PNG is decoded.** This began as a refusal -- Adam7 is rare from these tools, and a
  wrong picture is worse than a reported failure -- but the passes turned out to be worth doing
  properly rather than declining. See the Adam7 note further down for what makes them awkward.
- **Nothing in the decoders throws.** As with Sixel: `PngDecoder.TryDecode` wraps its whole body, and
  `KittyCommand.Parse` cannot fail -- unknown keys are ignored, per spec. Note that a continuation
  chunk carries only `m=1`, so the action defaults to transmit only when no action key was seen at
  all; otherwise a chunk would read as a fresh transmission.
- **Placeholder ids ride in the foreground colour, which fits.** `AttributeData` packs colour into 25
  bits, so a 24-bit image id round-trips intact. Only a *direct* colour is read as an id -- a palette
  index stays a colour, so red text does not summon image number one. The row/column combining marks
  are consumed and ignored, which is correct for the contiguous rectangles these clients emit.
- **A diacritic after a placeholder must not join the image cell.** `TryAppendToPreviousCell` would
  otherwise append it to a cell holding a picture; a test caught it, and image cells now refuse
  combining marks.

## Files changed

- `src/XTerm.NET/Parser/EscapeSequenceParser.cs` -- APC streaming state, separate pending-unhook flag
- `src/XTerm.NET/Common/Types.cs` -- `ParserState.ApcString`
- `src/XTerm.NET/Events/ParserEvents.cs` -- `ApcHookEventArgs`, `ApcPutEventArgs`, `ApcUnhookEventArgs`
- `src/XTerm.NET/Graphics/ImagePlacement.cs` -- placement, both tile geometries, tile coverage
- `src/XTerm.NET/Graphics/PngDecoder.cs` -- chunk walk, zlib, five scanline filters, colour types 0/2/3/4/6
- `src/XTerm.NET/Graphics/KittyCommand.cs` -- control-data parsing, non-allocating
- `src/XTerm.NET/Graphics/KittyTransmission.cs` -- chunk reassembly, zlib, raw and PNG decode
- `src/XTerm.NET/Graphics/ImageRegistry.cs` -- id-keyed store with oldest-first eviction
- `src/XTerm.NET/Buffer/BufferCell.cs` -- `Placement` storage, `Image` computed, equality by placement
- `src/XTerm.NET/InputHandler.cs` -- APC dispatch, kitty actions, replies, U+10EEEE placeholders
- `src/XTerm.NET/Terminal.cs` -- APC wiring, `DropImage`, nullable-buffer fixes
- `src/XTerm.NET/Options/TerminalOptions.cs` -- `KittyGraphicsEnabled`, `MaxImageRegistryBytes`
- `src/XTerm.NET/Assembly.cs` -- `InternalsVisibleTo` for the decoder tests
- `src/XTerm.NET.Tests/Parser/ApcSequenceTests.cs`
- `src/XTerm.NET.Tests/Graphics/ImagePlacementTests.cs`
- `src/XTerm.NET.Tests/Graphics/PngDecoderTests.cs`
- `src/XTerm.NET.Tests/Graphics/KittyGraphicsTests.cs`
- `src/XTerm.NET.Tests/Graphics/KittyPlaceholderTests.cs`

## Validation

```powershell
dotnet test src/XTerm.NET.slnx
```

```text
Passed: 938
Failed: 0
Skipped: 0
```

End to end, through a real ConPTY into a `Terminal`:

```text
> chafa --format kitty --size 4x2 test.png
  apc[0]  control="a=T,f=32,s=40,v=40,c=4,r=2,m=1,q=2"  payload=none
  apc[1]  control="m=1"                                 payload=680 chars
  ...
  apc[14] control="m=0"                                 payload=none
  15 APC sequences, 8536 base64 chars of payload
  placement: 40x40px image, source (0,0) 40x40 -> 4 cols x 2 rows, Stretched
  rows 0-1 hold 4 tiles each; nothing scrolled
```

One invocation covers chunking, `f=32`, `c`/`r` scaling, `q=2`, a control-only opening sequence and
an empty terminating one. A 30x14 run reassembles 617 sequences and 418 KB of base64 into one
280x280 image across 28x14 cells.
# Kitty graphics: the rest of the protocol

## Summary

The Kitty graphics support added earlier covered transmit, place, delete, query and Unicode
placeholders. This completes it: the full delete matrix, image numbers, placeholder tile diacritics,
pixel offsets, interlaced PNG, draw order, and animation.

## What each piece needed

**The delete matrix.** Only `d=a` and `d=i` existed; the rest were refused for want of a way to find
placements. Positional targets now find one through a cell and remove all of it -- deleting just the
cells in the named row would leave a picture with a hole through it. The scrollback is deliberately
not searched: a picture scrolled out of view is not "at row 3" however many rows above it happen to
be. Two keys change meaning on a delete -- `x` and `y` are screen cells rather than a crop origin,
and one-based where the buffer is zero-based -- and both conversions are pinned by tests that go red
when either is dropped.

**Image numbers.** `I=<number>` lets a client avoid managing an id space; the terminal picks the id
and reports both halves back so the client can match the reply and then use the image.

**Placeholder diacritics.** The marks stating a tile's row and column were consumed and ignored,
which only works for a rectangle written in reading order. They are decoded now. The table is
kitty's own `rowcolumn-diacritics.txt` taken verbatim: it was frozen against Unicode 6.0.0, and
regenerating it against a newer Unicode would silently renumber every tile. Fixed while there: a
placeholder run built a fresh placement per cell, which rendered identically and cost a blit per
cell instead of one per strip.

**Pixel offsets and Adam7.** `X`/`Y` shift a picture inside its first cell and, per the spec, are
"not added to the number of rows/columns" -- so the box is unchanged and the overflow is clipped.
That case cannot be expressed by a tile's size alone, since the leading tile is both narrower and
shifted, so `TryGetTileLayout` returns the source rectangle and the destination offset together.

The tile arithmetic became one uniform intersection -- the cell against the picture's span within
the box, mapped back onto the source -- covering both scalings, cropping and the offsets. Scaling
numerator and denominator by the same amount leaves the floor unchanged, so it reproduces the
previous results exactly, which the 1160x870-over-14x15 guard confirms tile for tile.

Adam7 is decoded rather than refused. Each pass is filtered against its own neighbours, so it cannot
be read as one strided image, and an empty pass contributes no bytes at all -- counting one would
shift every later pass and turn the rest of the picture into noise.

**Draw order.** A cell keeps every placement covering it, ordered by z-index and, at equal z, by
which was placed later. A NEGATIVE z means behind the TEXT, and there the cell keeps the glyph too --
which needed the one exception to the rule that printing rebuilds a cell from scratch. Erasing still
clears the lot; a picture showing through a cleared screen would be a leak.

**`d=a` took the text with the pictures.** Deleting every placement reached the cells through the
helper a resize uses, which blanks them. That is right for a picture in FRONT of the text, whose
character was only ever the placeholder space it wrote when it landed, and wrong for a background
one, whose character is whatever the user typed onto it. Every other delete target already got this
right, so the two disagreed. The rule now lives on `BufferCell.RemoveImages` and all three paths --
`d=a`, delete-by-placement and delete-by-image -- go through it. Invisible until something is drawn
behind text, which is how it survived: every other image cell holds a space, and blanking a space
changes nothing. Found by the walkthrough script, not by a test.

**Overlap.** Covering a picture no longer destroys it, and this needed no mechanism at all once
pictures became runs held by the line. Two pictures over the same columns are two runs; covering one
has no way to modify it. A translucent picture blends over what it covers because what it covers is
still there, and deleting the front one reveals the back one whole because the back one was never
touched. The second was a bug rather than a missing feature, and it bit opaque pictures too.

What the runs did need is an identity. A placement spanning eight rows is eight structs on eight
lines, and a delete finds it through one cell of one of them -- so `LinePlacement.Serial` is what
makes "the picture at this cell" mean all of it. That is the terminal's own identity and not Kitty's
`p=`, which is the client's, may be zero, and may repeat.

**Animation.** Frames, composition and control, both client-driven and terminal-driven.

## Decisions worth recording

- **The emulator still owns no timer.** It is driven entirely by `Write`, and starting a thread
  inside a library that has none -- to repaint a host that already has a render loop -- would be the
  wrong place for it. `Terminal.AdvanceAnimations(delta)` takes the elapsed time and returns whether
  anything moved. It also makes the timing exactly testable: no sleeping, no tolerance windows, no
  flake.
- **Advancing loops rather than stepping once.** Several gaps can fall inside one slice when the
  gaps are short or a repaint was late. Stepping once per call would silently make an animation run
  at the host's frame rate instead of its own.
- **An image's own pixels never change.** They are documented immutable and a host may have uploaded
  them; the root frame starts as a reference to them and is copied away the moment a client edits
  it. What moves is `CurrentPixels`, with `FrameSerial` changing alongside so a cached texture can
  be spotted as stale without comparing pixels.
- **Animated images are tracked in their own weakly-held list.** The host asks whether anything is
  moving on every frame, so the answer has to cost nothing for a terminal showing text -- scanning
  both buffers and the registry is the length of the scrollback, sixty times a second. Weak, or the
  list would keep every animation's pixels alive for the life of the terminal.
- **v unspecified means loop forever.** Reading it as "no loops" stops every animation after a
  single pass, which is what the first run of the loop test caught.
- **Y is read twice, as an int and as a uint.** It carries a pixel offset on a display command and a
  32-bit RGBA background on a frame. Opaque red is 4278190335, which does not fit a signed int:
  reading it as one saturates and silently turns the colour into something else.
- **Frame composition refuses what it cannot answer.** A missing frame is ENOENT, a rectangle off
  the edge EINVAL, and so is one frame onto itself with overlapping rectangles -- the result would
  depend on the copy order, so there is no right answer to give.

## Files changed

- `src/XTerm.NET/Graphics/ImageAnimation.cs` -- frames, state, the clock, and the blend
- `src/XTerm.NET/Graphics/PlaceholderDiacritics.cs` -- kitty's 297-mark table, verbatim
- `src/XTerm.NET/Graphics/TerminalImage.cs` -- `Animation`, `CurrentPixels`, `FrameSerial`
- `src/XTerm.NET/Graphics/ImagePlacement.cs` -- `ZIndex`, `Sequence`, offsets, `TryGetTileLayout`
- `src/XTerm.NET/Graphics/CellImageLayer.cs` -- the overlap chain and its ordering rule
- `src/XTerm.NET/Buffer/BufferCell.cs` -- `Below`, and the stack operations over it
- `src/XTerm.NET/Graphics/PngDecoder.cs` -- Adam7
- `src/XTerm.NET/Graphics/KittyCommand.cs` -- frame, animate and compose actions and their keys
- `src/XTerm.NET/Graphics/ImageRegistry.cs` -- image numbers, removal by image
- `src/XTerm.NET/InputHandler.cs` -- the delete matrix, diacritics, z-index, frames and composition
- `src/XTerm.NET/Terminal.cs` -- placement selection, `AdvanceAnimations`, `HasRunningAnimations`
- `src/XTerm.NET.Tests/Graphics/KittyDeleteTests.cs`
- `src/XTerm.NET.Tests/Graphics/KittyZIndexTests.cs`
- `src/XTerm.NET.Tests/Graphics/KittyAnimationTests.cs`

## Validation

```powershell
dotnet test src/XTerm.NET.slnx
```

```text
Passed: 1016
Failed: 0
Skipped: 0
```

Every guard added here was checked by breaking the code it guards and confirming the test goes red
with a useful message. Three tests did not, and were rewritten rather than kept: a column delete
over a two-column picture that swallowed an off-by-one, a gapless-frame test whose timing landed on
the right frame either way, and a bitmap-cache test that never looked again after the refresh.

# Private CSI sequences stop being aliases of their namesakes

## Summary

`CsiCommandExtensions.ToCsiCommand` stripped a leading `?` or `>` off the CSI identifier and looked
up what was left. That made every DEC private sequence an alias for whichever non-private command
happened to share its final character, whether or not the two had anything to do with each other.
The lookup now matches the identifier the parser actually built, private marker included, and a
private form is dispatched only where the table lists it. Anything else falls out as
`CsiCommand.Unknown` and is ignored, which is what an unimplemented sequence should do.

## Why

The stripping was there to make `CSI ? Pm h` reach DECSET, and for `h`, `l`, `n` and `$p` the
private and non-private forms genuinely are the same handler with a flag. For everything else the
final character is a coincidence, and the alias ran the wrong command on input that ordinary
programs emit at startup:

| Sequence | What it is | What ran instead |
|---|---|---|
| `CSI ? Pi ; Pa ; Pv S` | XTSMGRAPHICS, a Sixel capability query | SCROLL UP — the screen jumped whenever a graphics program started |
| `CSI > 4 ; 2 m` | XTMODKEYS, keyboard negotiation | SGR 4 ; 2 — underline and dim on everything printed afterwards |
| `CSI > 1 u` / `CSI ? u` | the Kitty keyboard protocol | RESTORE CURSOR — the cursor teleported to wherever it was last saved |
| `CSI ? Pm s` | XTSAVE, saves private modes | SAVE CURSOR — clobbered the position the application had saved on purpose |
| `CSI ? Pm r` | XTRESTORE, restores private modes | SET SCROLLING REGION with the mode number as a row, then home |
| `CSI > Ps q` | XTVERSION, "what terminal are you" | DECSCUSR — changed the cursor shape |
| `CSI > Ps t` | XTSMTITLE, title reporting | XTWINOPS — `CSI > 2 t` minimised the window |
| `CSI > Pm T` | XTRESTTITLE, restores a saved title | SCROLL DOWN — the screen jumped |

Three of these had already been patched one at a time in the dispatcher: XTSMGRAPHICS with an
`isPrivate` check inside the SCROLL UP case, then XTVERSION (#63) and the `?c` / `=c` device
attributes case (#64) with marker checks of their own. Each fixed the symptom someone had noticed
and left the rest, which is the argument for fixing the lookup rather than the cases: the aliasing
is the defect, and it produces a new one for every final character the two namespaces share.

## What is mapped now

Private identifiers are listed explicitly: `?J` (DECSED), `?K` (DECSEL), `?S` (XTSMGRAPHICS), `?h`
(DECSET), `?l` (DECRST), `?n` (DEC DSR), `>c` (secondary DA), `?$p` (private DECRQM), `>q`
(XTVERSION) and the four Kitty keyboard forms `=u`, `?u`, `>u` and `<u`. `?c` was dropped:
`CSI ? c` is not a sequence, and answering it as a secondary DA was an artefact of the stripping
rather than a decision.

`>q` is the one entry that deliberately shares a `CsiCommand` with another sequence — it maps to
`SelectCursorStyle` and `InputHandler` splits XTVERSION back out on the marker. See below for why
it is treated differently from XTSMGRAPHICS. It is load-bearing: delete the row and three
`VersionReportTests` go red.

The exact match runs on the intermediate bytes as well as the marker, so `q` is not a key either:
the bare `CSI Ps q` is DECLL (Load LEDs), which is not implemented, and DECSCUSR is the `" q"` form
that carries the SP intermediate. Mapping both to `SelectCursorStyle` meant an application clearing
its LEDs got a blinking cursor.

The Kitty keyboard protocol landed on `main` while this branch was open, and it is the case that
shows why an exact match is the right shape: `?u` and `>u` are the query and the push, `<u` is the
pop, and each is a different command from `u` (RESTORE CURSOR). Under the old lookup two of them
moved the cursor and the third was silently unknown.

`XTSMGRAPHICS` gets its own `CsiCommand.GraphicsAttributes` instead of borrowing `ScrollUp`, so the
dispatcher no longer re-decides that one on a flag. `>q` is the deliberate exception, and it is the
only place left where the dispatcher looks at the identifier again. The difference is what the
re-decision is made on: XTSMGRAPHICS was split from SCROLL UP by an `isPrivate` flag, which is true
for `?` and `>` alike and so cannot tell two markers apart, while `>q` is split by
`identifier.PrivateMarker()`, which names the exact byte the map matched on. Reading the marker is
the same decision the map makes, taken one step later; reading a flag is a different and weaker
one. A `CsiCommand.ReportVersion` member would work too — the two-arm switch was preferred because
it sits next to the DECSCUSR call it exists to not make.

## Files changed

- `src/XTerm.NET/Common/CommandExtensions.cs` -- exact identifier match; private entries listed
- `src/XTerm.NET/Common/CsiCommand.cs` -- `GraphicsAttributes`
- `src/XTerm.NET/InputHandler.cs` -- XTSMGRAPHICS dispatches on its own command, not on a flag
- `src/XTerm.NET.Tests/Common/CsiCommandExtensionsTests.cs` -- the mapping table, both directions
- `src/XTerm.NET.Tests/PrivateCsiDispatchTests.cs` -- each misroute above, driven through `Write`,
  plus the implemented private sequences still reaching their handlers

## Validation

```powershell
dotnet test src/XTerm.NET.slnx
```