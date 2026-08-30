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

            // The cap MaxKittyPayloadChars was written for, finally applied where it matters.
            // It was only ever enforced on _apcPayload, which bounds ONE escape sequence -- but a
            // transmission spans as many sequences as the client cares to send, and each one is
            // individually legal. 300 protocol-conforming chunks grew this accumulator to 292 MB
            // with the transmission still open. EFBIG is the protocol's own answer for an image
            // the terminal will not hold, and the state is dropped so the next chunk starts clean
            // rather than continuing an abandoned image.
            if (_kittyTransmission.PayloadLength > MaxKittyPayloadChars)
            {
                var abandoned = _kittyTransmission.Command;
                _kittyTransmission = null;
                ReplyToKitty(abandoned, Graphics.KittyError.TooLarge);
                return;
            }

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

            // Frames are charged against the same budget images are. ImageRegistry.Store accounts
            // every image it holds and trims to the budget, but a frame never goes through Store --
            // it is appended to an animation the registry already holds, so the registry's byte
            // count never moved and no trim could ever see it. An animation is the one place in
            // this protocol where a client adds full canvases to an object that already exists,
            // which is exactly how the accounting was slipped.
            // Charged to the REGISTRY, not measured against this animation alone: several
            // animations could otherwise each grow to the whole budget, and the registry's counter
            // would still hold the size each image was first stored at.
            if (!_kittyImages.TryCharge(frameBytes, _terminal.Options.MaxImageRegistryBytes))
            {
                ReplyToKitty(command, Graphics.KittyError.TooLarge);
                return;
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
                        serial: serial,
                        // The scaling context TryGetTileLayout used, or the renderer cannot know
                        // whether this strip is the picture at its own size or its share of a
                        // stretched box -- and a stretched strip must fill its whole row.
                        pxPerCellX: placement.Scaling == Graphics.ImageScaling.Natural
                            ? 0
                            : (float)placement.SourceWidth / placement.Cols,
                        pxPerCellY: placement.Scaling == Graphics.ImageScaling.Natural
                            ? 0
                            : (float)placement.SourceHeight / placement.Rows),
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

    /// <summary>
    /// Handles Kitty desktop notifications (OSC 99).
    /// </summary>
    private void HandleKittyNotification(string data)
    {
        if (!_terminal.Options.KittyNotificationsEnabled)
            return;

        RemoveExpiredKittyNotifications();
        // The payload separator is optional: a capability query is metadata only
        // (ESC ] 99 ; i=x:p=? ST) — and every detector sends exactly that form, so requiring the
        // second ';' made support undetectable while the feature worked.
        var parts = data.Split(new[] { ';' }, 2);

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
            _terminal.RaiseDataReceived(
                $"\u001b]99;i={identifier ?? "0"}:p=?;a=notify:o=always:u=0,1,2:p=title,body\u001b\\");
            return;
        }

        // Only a query is complete without the payload separator; metadata alone shows nothing.
        if (parts.Length == 1 || payloadType is not ("title" or "body"))
            return;
        var payload = parts[1];

        var key = identifier ?? string.Empty;
        if (!_kittyNotifications.TryGetValue(key, out var notification))
        {
            if (!done && _kittyNotifications.Count >= MaxPendingKittyNotifications)
                return;

            notification = new KittyNotification(identifier);
            if (!done)
                _kittyNotifications[key] = notification;
        }

        var text = encoded ? DecodeBase64(payload) : SanitizeText(payload);
        if (text is null || !notification.Append(payloadType, text, urgency, icon))
        {
            _kittyNotifications.Remove(key);
            return;
        }

        if (!done)
            return;

        _kittyNotifications.Remove(key);
        // Braces, because their absence changed what this method does: only the inner `if` was
        // guarded by TryBuild, so a notification that FAILED to build was raised anyway with null
        // title and null body -- a host handing Title to an OS notification API got null with
        // nothing to show.
        if (notification.TryBuild(out var title, out var body))
        {
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
    }

    private void RemoveExpiredKittyNotifications()
    {
        var cutoff = DateTime.UtcNow - KittyNotificationTimeout;
        foreach (var key in _kittyNotifications.Where(entry => entry.Value.LastUpdated < cutoff).Select(entry => entry.Key).ToArray())
            _kittyNotifications.Remove(key);
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

}
