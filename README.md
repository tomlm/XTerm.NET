# XTerm.NET

[![Build Status](https://github.com/tomlm/XTerm.NET/actions/workflows/BuildAndRunTests.yml/badge.svg)](https://github.com/tomlm/XTerm.NET/actions/workflows/BuildAndRunTests.yml) [![NuGet Version](https://img.shields.io/nuget/v/XTerm.NET.svg)](https://www.nuget.org/packages/XTerm.NET/) [![NuGet Downloads](https://img.shields.io/nuget/dt/XTerm.NET.svg)](https://www.nuget.org/packages/XTerm.NET/) [![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

A .NET terminal emulator library inspired by [xterm.js](https://github.com/xtermjs/xterm.js).
XTerm.NET provides a headless terminal emulator that parses and processes VT100/ANSI escape sequences,
making it easy to host console applications in your .NET applications.

**Sixel and Kitty graphics are supported.** A picture is held as a run on the line it appears on, so
it scrolls with that line, is cleared by `ED`/`EL`, and is freed when the line falls out of the
scrollback — and a resize does nothing to it at all. Sixel is *content*, so printing over it replaces
that part of the picture; Kitty is an *overlay* ordered against the text by its z-index. See
[Images](#images).

## Features

- **Full VT100/ANSI Escape Sequence Support** — Process colors, cursor movement, text attributes, and more
- **Headless Design** — No UI dependencies; bring your own renderer (Console, WPF, MAUI, etc.)
- **Dual Buffer Support** — Normal and alternate screen buffers with scrollback
- **Keyboard & Mouse Input Generation** — Generate escape sequences for keyboard and mouse events
- **Rich Event System** — Subscribe to terminal events like title changes, bell, resize, and window manipulation
- **256 and True Color Support** — Full RGB and 256-color palette support
- **Unicode Support** — Proper handling of wide characters and Unicode text
- **Sixel Graphics** — Decodes Sixel images (`ESC P … q`) and stores them on the cells they cover, so
  `img2sixel`, `chafa`, `lsix` and `timg` work against a host that renders them
- **Kitty Graphics** — Decodes the Kitty protocol (`ESC _ G …`), including chunked transmission, PNG,
  transmit-once/place-many by image id, animation, and U+10EEEE Unicode placeholders, so `icat`,
  `chafa -f kitty`, `timg -pk`, `yazi` and `image.nvim` work the same way
- **Kitty Text Sizing** — Decodes the text sizing protocol (`OSC 66`), so a client can state how many
  cells a string takes and ask for it larger or smaller than the base size

## Upgrading to 2.0

**2.0 targets .NET 10** and removes the per-cell image API. If you only host a terminal and draw
text, nothing changes but the target framework. If you draw images, the contract has been replaced
rather than extended.

A picture used to be scattered across cells, each carrying a reference and a tile coordinate. It is
now a run held by the line: `LinePlacement`, with the columns it covers and the source rectangle it
reads. That is what makes a resize stop destroying pictures — a run keeps its natural width, so
narrowing a window shows less of a picture instead of losing it.

**Removed from `BufferCell`.** A cell is a struct, so a copy of one has no idea which line or column
it came from and cannot answer for a run anchored to both. Ask the line instead:

| Gone | Use |
| --- | --- |
| `cell.Image` | `line.TryGetImageAt(column, out var image)` |
| `cell.IsImage` | `line.TryGetPlacementAt(column, out _)` |
| `cell.ImageTile`, `cell.ImageCol`, `cell.ImageRow` | `line.TryGetPlacementAt(column, out var p)` — `p.SrcX`, `p.SrcY` |
| `BufferCell.PackTile(col, row)` | no longer meaningful; runs carry source pixels, not tile numbers |

**Changed elsewhere.**

- `TerminalImage.ByteCount` is a `long`. An animation's frames are counted against the image budget
  alongside the root picture, and the two together can exceed what an `int` holds.
- `Params.GetSubParams` returns `IReadOnlyList<int>` rather than `List<int>`. It used to be a stub
  that returned an empty list; it now returns the real sub-parameters, which is what makes `4:3` a
  curly underline and `38:2::r:g:b` a colour instead of both being discarded.
- `AttributeData.IsUnderline()` reports the underline *style* rather than a separate flag, so a cell
  underlined by `4:3` or `21` now answers true. Keeping a flag beside the style is how a cell ends up
  underlined by one and not the other.

**What a renderer does now.** Take `line.Placements`, order them by `ZIndex` — a stable sort, so
equal depths keep the order they were placed in, which is age — and draw them back to front with the
text going down between the negative ones and the rest. Each run is a single blit: source
`(SrcX, SrcY, SrcWidth, SrcHeight)`, destination the columns from `Column` for `Cols` cells. Clip the
destination to the line's width and narrow the source by the same proportion. Paint the cell
background from the bottom-most run covering it and no other, or a nearer picture will erase what is
behind it instead of blending over it.

For animation, draw `image.CurrentPixels` rather than `image.Pixels`, and re-upload a cached texture
when `image.FrameSerial` changes. Drive the clock with `AdvanceAnimations(delta)`, and use
`HasRunningAnimations()` to decide whether you need one at all.

## Installation

Install via NuGet Package Manager:

```shell
dotnet add package XTerm.NET
```

Or via the Package Manager Console in Visual Studio:

```powershell
Install-Package XTerm.NET
```

## Usage
The basic architecture is that the Terminal is a XxY array of Buffer Cell structures which represent each cell of the console screen.

* Incoming text from a hosted process is written to the terminal and the terminal will interpret any ANSI VT Escape codes to 
change color, underline, position etc.
* The terminal host application calls Terminal.GenerateMouseEvent(), Terminal.GenerateKeyEvent() to send input to the console process.
* Requests for information are modeled as events (GetWindowTitle, SetWindowTitle etc.).

### Creating a Terminal

Create a terminal instance with default settings (80 columns × 24 rows):

```csharp
using XTerm;

var terminal = new Terminal();
```

Or customize the terminal with `TerminalOptions`:

```csharp
using XTerm;
using XTerm.Options;

var terminal = new Terminal(new TerminalOptions
{
    Cols = 120,                   // Number of columns
    Rows = 40,                    // Number of rows
    Scrollback = 1000,            // Scrollback buffer lines (0 to disable)
    CursorStyle = CursorStyle.Block,
    CursorBlink = true,
    TermName = "xterm"            // Terminal type for identification
});
```

### Resizing the Terminal

Resize the terminal dynamically to match your UI or window size:

```csharp
// Resize to new dimensions
terminal.Resize(cols: 120, rows: 50);

// Query current size
int currentCols = terminal.Cols;
int currentRows = terminal.Rows;
```

### Writing Content to the Terminal

Write text and ANSI escape sequences to the terminal:

```csharp
// Write text (no automatic newline)
terminal.Write("Hello, ");

// Write a line (adds \r\n)
terminal.WriteLine("XTerm.NET!");

// Write with ANSI colors and styles
terminal.WriteLine("\x1b[31mRed text\x1b[0m");
terminal.WriteLine("\x1b[1;32mBold green text\x1b[0m");
terminal.WriteLine("\x1b[38;2;255;100;200mTrue color (RGB) text\x1b[0m");

// Position the cursor and draw
terminal.Write("\x1b[5;10HText at row 5, column 10");
```

Access the buffer to read terminal content:

```csharp
var buffer = terminal.Buffer;

// Get cursor position
int cursorX = buffer.X;
int cursorY = buffer.Y;

// Read a line as a string
string lineContent = terminal.GetLine(0);

// Or access the buffer line directly
var line = buffer.Lines[0];
string content = line?.TranslateToString(trimRight: true) ?? "";
```

### Hooking Up Events

Subscribe to events to integrate the terminal into your application:

```csharp
// Data sent back from the terminal (e.g., query responses)
terminal.DataReceived += (sender, e) =>
{
    // Send e.Data to your connected process/PTY
    Console.WriteLine($"Terminal sent: {e.Data}");
};

// Terminal title changed (via OSC escape sequence)
terminal.TitleChanged += (sender, e) =>
{
    // Update your window title
    Console.WriteLine($"Title: {e.Title}");
};

// Terminal resized
terminal.Resized += (sender, e) =>
{
    // Notify your PTY/process of the new size
    Console.WriteLine($"Resized to {e.Cols}x{e.Rows}");
};

// Bell character received
terminal.BellRang += (sender, e) =>
{
    // Play a sound or flash the window
    Console.WriteLine("Bell!");
};

// Line feed occurred (useful for tracking output)
terminal.LineFed += (sender, e) =>
{
    // Trigger a render update
};

// Cursor style changed
terminal.CursorStyleChanged += (sender, e) =>
{
    // Update cursor rendering
    Console.WriteLine($"Cursor: {e.Style}, Blink: {e.Blink}");
};

// Buffer switched (normal ↔ alternate)
terminal.BufferChanged += (sender, e) =>
{
    Console.WriteLine($"Switched to {e.BufferType} buffer");
};
```

**Window manipulation events** (used by some terminal applications):

```csharp
terminal.WindowMoved += (sender, e) => Console.WriteLine($"Move to ({e.X}, {e.Y})");
terminal.WindowResized += (sender, e) => Console.WriteLine($"Resize to {e.Width}x{e.Height}");
terminal.WindowMinimized += (sender, e) => Console.WriteLine("Minimize");
terminal.WindowMaximized += (sender, e) => Console.WriteLine("Maximize");
terminal.WindowRestored += (sender, e) => Console.WriteLine("Restore");
```

### Rendering the Buffer

XTerm.NET is headless — you provide the rendering logic for your UI framework (Console, WPF, MAUI, Avalonia, etc.). Walk over the terminal buffer and render each cell according to its content and attributes:

```csharp
void RenderTerminal(Terminal terminal)
{
    var buffer = terminal.Buffer;

    for (int row = 0; row < terminal.Rows; row++)
    {
        var line = buffer.Lines[buffer.YDisp + row];
        if (line == null) continue;

        for (int col = 0; col < terminal.Cols; col++)
        {
            BufferCell cell = line[col];

            // Skip empty cells or continuation cells (wide character's second cell)
            if (cell.Width == 0) continue;

            // Get the character content
            string character = cell.Content;

            // Get foreground/background colors
            int fgColor = cell.Attributes.GetFgColor();
            int bgColor = cell.Attributes.GetBgColor();
            int fgMode = cell.Attributes.GetFgColorMode();  // 0=default, 1=256-color, 2=RGB
            int bgMode = cell.Attributes.GetBgColorMode();

            // Check text style attributes
            bool isBold = cell.Attributes.IsBold();
            bool isDim = cell.Attributes.IsDim();
            bool isItalic = cell.Attributes.IsItalic();
            bool isUnderline = cell.Attributes.IsUnderline();
            bool isBlink = cell.Attributes.IsBlink();
            bool isInverse = cell.Attributes.IsInverse();
            bool isInvisible = cell.Attributes.IsInvisible();
            bool isStrikethrough = cell.Attributes.IsStrikethrough();
            bool isOverline = cell.Attributes.IsOverline();

            // Render the cell at (col, row) with the appropriate styling
            // Your rendering code here — e.g., DrawText(col, row, character, fg, bg, styles...)
        }
    }

    // Render the cursor if visible
    if (terminal.CursorVisible)
    {
        int cursorX = buffer.X;
        int cursorY = buffer.Y;
        CursorStyle style = terminal.Options.CursorStyle;  // Block, Underline, or Bar
        bool blink = terminal.Options.CursorBlink;

        // Draw cursor at (cursorX, cursorY) with the appropriate style
    }
}
```

**Color mode values:**
- `0` — Default terminal color (use theme foreground/background)
- `1` — 256-color palette index (0–255)
- `2` — True color RGB (extract with `color & 0xFF` for each channel)

**Handling wide characters:**

Wide characters (e.g., CJK ideographs, emoji) have `Width = 2`. The first cell contains the character, and the second cell has `Width = 0` as a placeholder — skip it during rendering but allocate space for the double-width glyph.

**Handling sized text (`OSC 66`):**

The Kitty text sizing protocol — `OSC 66 ; s=2 ; Heading ST` — draws a run of text at a multiple of
the cell size. It has two halves, and a renderer can honour the first without the second.

The *width* half is the emulator's own and needs nothing from you. A run claims `s * w` columns (or,
with `w=0`, each of its characters claims `s` times its normal width), and it claims them the way a
double-width character does: the first cell carries the text with `Width` set to the columns it took,
and the rest are `Width = 0` continuations. So the cursor, selection and search already agree with
the client about how much room the run occupies — which is the point of the `w` key, a client telling
the terminal a string's width instead of both sides guessing at Unicode. A line holding a run is not
re-wrapped by a resize, exactly as a double-width line is not: the block keeps its shape, and a
narrowing that cuts it erases it and blanks what is left rather than leaving a cell claiming columns
that are gone.

The *scale* half is yours. Ask the line what it holds:

```csharp
if (line.TryGetSizedRunAt(col, out LineSizedRun run))
{
    // run.Cols   — the columns the run covers
    // run.Rows   — how many rows tall the block is (its scale), growing DOWNWARDS from this line
    // run.Sizing — Scale, Width, the Numerator/Denominator fraction, and the two alignments
}
```

Draw the run's text scaled to `run.Cols` by `run.Rows` cells, applying the fraction inside that block
when `run.Sizing.IsFractional` and placing the fractional area with `VerticalAlignment` and
`HorizontalAlignment`. The rows a tall block covers are held for it: printing that would land in one
of them steps past the block instead, so the text a client writes underneath a heading is placed
after it rather than beneath it. Ask the buffer which block covers a cell:

```csharp
if (terminal.Buffer.TryGetSizedRunCovering(absoluteRow, col, out var run, out var anchorRow))
{
    // the block is drawn from anchorRow; this cell is one of the rows it occupies
}
```

Erasing or splicing any row a block touches — `ED`, `EL`, `ECH`, `IL`, `DL` — erases the whole
block, as the protocol asks, so a block never outlives the rows it was drawn over. A renderer that
cannot scale at all should draw the text at the base size in the first cell of the block; the columns
are reported honestly either way.

### Images

Two graphics protocols are decoded — Sixel (`ESC P … q … ESC \`) and Kitty
(`ESC _ G … ESC \`) — and both end up in the same place: the cells the picture covers. Each covered
cell carries a reference to a shared `ImagePlacement` plus the coordinates of the piece it shows, so
an image behaves like terminal content rather than an overlay: printing over a cell replaces that
part of the picture, `ED`/`EL` clear it, scrolling carries it, and the image is freed once the last
cell holding it is gone.

A **placement** is one appearance of a picture: which `TerminalImage` it draws, which rectangle of
that image it takes, and how many cells it fills. Sixel makes one placement per image. Kitty can
transmit a picture once and place it many times, so several placements may share one `TerminalImage`
— which is why cells reference the placement and not the image.

**Tell the terminal your cell size.** XTerm.NET is headless and cannot measure a font, so it cannot
work out how many columns an image covers unless you say. Set these from your renderer's metrics,
in *device* pixels:

```csharp
terminal.Options.CellWidthPixels  = 8;
terminal.Options.CellHeightPixels = 17;
```

**Answer the window queries from these same numbers.** An image viewer works out the cell size for
itself, by dividing the pixel size it gets from `CSI 14 t` by the row and column counts it already
has. So your `WindowInfoRequested` handler must report the **grid**, not the control:

```csharp
case WindowInfoRequest.SizePixels:      // CSI 14 t
    e.WidthPixels  = terminal.Cols * terminal.Options.CellWidthPixels;
    e.HeightPixels = terminal.Rows * terminal.Options.CellHeightPixels;
    e.Handled = true;
    break;

case WindowInfoRequest.CellSizePixels:  // CSI 16 t
    e.CellWidth  = terminal.Options.CellWidthPixels;
    e.CellHeight = terminal.Options.CellHeightPixels;
    e.Handled = true;
    break;
```

Reporting your control's own size instead is the classic way to get this wrong. It includes the
scrollbar, any window chrome, and the strip below the last row — the grid is a truncated division, so
up to a whole row of the control's height belongs to no row at all. An application dividing that
figure by the row count is told the terminal is taller than it is, sizes a picture to fill it, and the
surplus runs off the bottom and scrolls the screen.

**Rendering the tiles.** Extend the per-cell loop above:

```csharp
BufferCell cell = line[col];

if (cell.Placement is ImagePlacement placement &&
    placement.TryGetTileLayout(cell.ImageCol, cell.ImageRow,
                               out int sx, out int sy, out int sw, out int sh,
                               out double offX, out double offY,
                               out double cellsWide, out double cellsHigh))
{
    // Pixels are BGRA8888 with straight (unpremultiplied) alpha, top row first.
    // Cache your framework's bitmap against `placement.Image` — a ConditionalWeakTable keyed on the
    // image lets the bitmap die when the image does, with no eviction list to maintain, and two
    // placements of one picture share the single upload.
    var bitmap = _bitmaps.GetOrCreate(placement.Image);

    DrawImage(bitmap,
        source: (sx, sy, sw, sh),
        dest: ((col + offX) * cellWidth, (row + offY) * cellHeight,
               cellWidth * cellsWide, cellHeight * cellsHigh));
    continue;
}
```

`TryGetTileLayout` answers both halves in one call: which pixels to take, and where in the cell to
put them. Both are needed because neither implies the other — a natural-size tile at the right edge
is clipped short, a stretched tile is a proportional slice, and a placement carrying `X=`/`Y=` starts
partway into its first cell and is *both* narrower and shifted. The older
`GetTileCoverage(sw, sh, out cellsWide, out cellsHigh)` still works and still returns the same
numbers, but it has no way to express that shift, so a renderer that wants offsets must use the
layout call.

Adjacent cells sharing the same **`Placement`** reference and `ImageRow` with consecutive `ImageCol`
values are contiguous, so a renderer can coalesce them into a single draw call per row instead of one
per cell. Compare the placement, not the image: under Kitty two appearances of one picture can sit
side by side, and coalescing on the image would run a single strip across the join and blit the wrong
pixels into both halves. If you cache rendered rows, note that image cells must break a text run —
compare `Placement` by reference as well as comparing `Attributes`.

`cell.Image` remains available as shorthand for `cell.Placement?.Image` and still identifies the
pixels, so bitmap caches keyed on it need no change.

Image cells hold `" "` as their content, so `TranslateToString` and selection copy yield blanks.

**Placement geometry.** `ImagePlacement.Scaling` says how tiles divide the source:

- `Natural` — a fixed cell pitch with edge tiles clipped. Sixel always, and Kitty when neither `c`
  nor `r` is given.
- `Stretched` — the source rectangle divided proportionally across the cell box, which is what
  Kitty's `c`/`r` keys ask for. Tiles are not all the same width, so size each from its own source
  rectangle rather than from the first one.

`TryGetTileSource` and `GetTileCoverage` handle both, so a renderer using them does not need to
branch on the mode.

**Options:**

| Option | Default | Purpose |
|---|---|---|
| `SixelEnabled` | `true` | Decode Sixel, and advertise it in the primary Device Attributes reply |
| `KittyGraphicsEnabled` | `true` | Decode Kitty graphics sequences and answer their queries |
| `CellWidthPixels` / `CellHeightPixels` | `10` / `20` | Cell size images are laid out against |
| `MaxSixelPixels` | `4_000_000` | Largest single image accepted |
| `MaxImageBytes` | `64 MB` | Budget for image data live in the buffer; oldest are dropped past it |
| `MaxImageRegistryBytes` | `32 MB` | Budget for transmitted-but-unplaced Kitty images, evicted oldest first |

Images are dropped when the terminal is resized to a different **column** count, because reflow
re-wraps lines by copying ranges of cells and the pieces would reassemble in the wrong places. A
change of row count alone keeps them.

#### Kitty graphics

Supported: transmit (`a=t`), transmit-and-display (`a=T`), place a stored image (`a=p`), delete
(`a=d`), and query (`a=q`); chunked payloads (`m=1`/`m=0`); RGB (`f=24`), RGBA (`f=32`) and PNG
(`f=100`, including interlaced); zlib compression (`o=z`); source cropping (`x`,`y`,`w`,`h`);
cell-box scaling (`c`,`r`); pixel offsets within the first cell (`X`,`Y`); cursor policy (`C`); and
response suppression (`q=1`/`q=2`).

An image may be named by the id the client chose (`i=`) or by a number it chose (`I=`), in which
case the terminal picks an id and reports both back. The whole delete matrix is implemented — by id,
by number, by placement id, at the cursor, at a cell, by row, by column, and by z-index — with the
upper-case form of each additionally releasing the stored image.

Images may be transmitted once and placed repeatedly, including via **U+10EEEE Unicode
placeholders**, where the image id travels in the cell's foreground colour. That is how `yazi`,
`ranger` and `image.nvim` draw. The combining marks that state an explicit tile row and column are
decoded, so a client may write tiles in any order rather than as a rectangle in reading order.

**Animation** is supported: frame transmission (`a=f`), animation control (`a=a`) and frame
composition (`a=c`). Frames may carry only the rectangle that changed, composed onto a previous
frame or onto a flat colour, blended or replaced. Both driving styles work — the client can make a
frame current itself, or set gaps and hand the timing to the terminal.

The emulator owns no timer. It is driven entirely by `Write`, and starting a thread inside a library
that has none, to repaint a host that already has a render loop, would be the wrong place for it.
So a host drives the clock:

```csharp
// From your render loop, with however long the last frame took.
if (terminal.AdvanceAnimations(elapsed))
    InvalidateVisual();
```

`HasRunningAnimations()` says whether a clock is needed at all, so a terminal showing nothing but
text runs no timer. Draw `image.CurrentPixels` rather than `image.Pixels` — the latter stays the
root frame and never changes, which is what makes it safe to hold and hand to another thread. Cache
your texture against the image as before, and re-upload when `image.FrameSerial` changes.

Not supported, and refused with a proper error reply rather than ignored:

- **File, temp-file and shared-memory transmission (`t=f`, `t=t`, `t=s`)** are refused with `ENOTSUP`
  by design. The terminal would be opening a path chosen by the program it hosts, and the host
  usually holds more privilege than that program does. Direct transmission (`t=d`) is the only medium
  accepted.

**Draw order (`z=`)** is honoured, including where pictures overlap. A picture is a run held by the
line rather than anything written into cells, so two pictures over the same columns are simply two
runs and the z-index says which a renderer draws last — ordered by z, and at equal z by which was
placed later. Covering one has no way to modify it: a translucent picture blends over the one behind
it, and deleting the front one reveals the back one whole, because the back one was never touched.

A host draws a line's runs from the back forwards, and paints each cell's own background once
underneath them — painting it again with a nearer run would erase what is behind instead of letting
it show through.

A **negative** z-index means something different in kind — behind the *text*. A Kitty placement is an
overlay rather than content, so printing over one never modifies it: the z-index alone decides
whether the glyph or the picture is on top, and a picture placed under existing text leaves it
readable. That is also why typing over a picture in *front* of the text hides the character without
destroying it, and deleting the picture gives it back. Sixel is the other kind and keeps its
replace-on-write. Erasing (`ED`/`EL`) clears both, because a picture showing through a cleared screen
would be a leak rather than a feature. A host renders these by drawing the tile first
and the glyph over it; text with no background colour of its own paints no fill, which is what lets
the picture show through.

## License

This project is licensed under the MIT License — see the [LICENSE](LICENSE) file for details.

## Author

Tom Laird-McConnell — [Iciclecreek](https://github.com/tomlm)

## Links

- [GitHub Repository](https://github.com/tomlm/XTerm.NET)
- [NuGet Package](https://www.nuget.org/packages/XTerm.NET)
