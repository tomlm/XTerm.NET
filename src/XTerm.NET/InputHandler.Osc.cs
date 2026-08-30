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

            case "Capabilities":
                // iTerm2's capability report: two-letter (then one-letter) codes, integers
                // attached where the vocabulary defines them. Only what this emulator actually
                // implements is listed — a detector trusts every code here verbatim.
                //   T24 truecolor   Cw OSC 52 write   Lr DECSLRM      M mouse    U unicode
                //   B bracketed     F focus events    Gs strike       Go overline
                //   Sy sync (2026)  H OSC 8 links     No notifications (OSC 9/99)  Sx sixel
                _terminal.RaiseDataReceived("\u001b]1337;Capabilities=T24CwLrMUBFGsGoSyHNoSx\u001b\\");
                return true;

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
        catch (Exception e) when (e is FormatException or ArgumentException)
        {
            // Invalid base64 or UTF-8 is untrusted terminal output, so ignore it. FormatException
            // is named explicitly because it is NOT an ArgumentException -- the two are siblings
            // under SystemException -- so Convert.FromBase64String's own failure was the one thing
            // this catch did not cover, and it escaped through Terminal.Write into the caller's
            // read loop. DecoderFallbackException, from the strict UTF-8 decode below, is an
            // ArgumentException and was always caught.
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

    private static bool IsValidOsc52ClipboardTarget(string target) =>
        target.Length > 0 && target.All(c => c is 'c' or 'p' or 'q' or 's' or >= '0' and <= '7');

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

}
