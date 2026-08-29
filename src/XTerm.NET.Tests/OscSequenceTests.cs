using XTerm;
using XTerm.Common;
using XTerm.Events;
using XTerm.Options;

namespace XTerm.Tests;

public class OscSequenceTests
{
    private Terminal CreateTerminal(int cols = 80, int rows = 24)
    {
        var options = new TerminalOptions { Cols = cols, Rows = rows };
        return new Terminal(options);
    }

    [Fact]
    public void OscSetTitle_SetsTerminalTitle()
    {
        // Arrange
        var terminal = CreateTerminal();
        var titleChanged = false;
        string? newTitle = null;
        terminal.TitleChanged += (sender, e) =>
        {
            titleChanged = true;
            newTitle = e.Title;
        };

        // Act
        terminal.Write("\x1B]0;My Terminal Title\x07");

        // Assert
        Assert.Equal("My Terminal Title", terminal.Title);
        Assert.True(titleChanged);
        Assert.Equal("My Terminal Title", newTitle);
    }

    [Fact]
    public void OscSetWindowTitle_SetsTerminalTitle()
    {
        // Arrange
        var terminal = CreateTerminal();

        // Act
        terminal.Write("\x1B]2;Window Title\x07");

        // Assert
        Assert.Equal("Window Title", terminal.Title);
    }

    [Fact]
    public void OscSetWindowTitle_WithEscTerminator_Works()
    {
        // Arrange
        var terminal = CreateTerminal();

        // Act
        terminal.Write("\x1B]2;Title with ESC terminator\x1B\\");

        // Assert
        Assert.Equal("Title with ESC terminator", terminal.Title);
    }

    [Fact]
    public void OscSetTitle_EmptyTitle_ClearsTitle()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.Write("\x1B]0;Initial Title\x07");

        // Act
        terminal.Write("\x1B]0;\x07");

        // Assert
        Assert.Equal("", terminal.Title);
    }

    [Fact]
    public void OscCurrentDirectory_SetsDirectory()
    {
        // Arrange
        var terminal = CreateTerminal();
        var directoryChanged = false;
        string? newDirectory = null;
        terminal.DirectoryChanged += (sender, e) =>
        {
            directoryChanged = true;
            newDirectory = e.Directory;
        };

        // Act
        terminal.Write("\x1B]7;file://localhost/home/user/projects\x07");

        // Assert
        Assert.Equal("/home/user/projects", terminal.CurrentDirectory);
        Assert.True(directoryChanged);
        Assert.Equal("/home/user/projects", newDirectory);
    }

    [Fact]
    public void OscCurrentDirectory_WindowsPath_Works()
    {
        // Arrange
        var terminal = CreateTerminal();

        // Act
        terminal.Write("\x1B]7;file://localhost/C:/Users/Test\x07");

        // Assert
        Assert.Equal("/C:/Users/Test", terminal.CurrentDirectory);
    }

    [Fact]
    public void OscCurrentDirectory_UrlEncoded_Decodes()
    {
        // Arrange
        var terminal = CreateTerminal();

        // Act
        terminal.Write("\x1B]7;file://localhost/home/user/my%20folder\x07");

        // Assert
        Assert.Equal("/home/user/my folder", terminal.CurrentDirectory);
    }

    [Fact]
    public void OscHyperlink_StartLink_SetsHyperlink()
    {
        // Arrange
        var terminal = CreateTerminal();
        string? changedUrl = null;
        var isCleared = true;
        terminal.HyperlinkChanged += (sender, e) => changedUrl = e.Url;
        terminal.HyperlinkChanged += (sender, e) => isCleared = e.IsCleared;

        // Act
        terminal.Write("\x1B]8;;http://example.com\x07");

        // Assert
        Assert.Equal("http://example.com", terminal.CurrentHyperlink);
        Assert.Equal("http://example.com", changedUrl);
        Assert.False(isCleared);
    }

    [Fact]
    public void OscHyperlink_EndLink_ClearsHyperlink()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.Write("\x1B]8;;http://example.com\x07");
        var eventCount = 0;
        string changedUrl = "not cleared";
        var isCleared = false;
        terminal.HyperlinkChanged += (sender, e) =>
        {
            eventCount++;
            changedUrl = e.Url;
            isCleared = e.IsCleared;
        };

        // Act
        terminal.Write("\x1B]8;;\x07");

        // Assert
        Assert.Null(terminal.CurrentHyperlink);
        Assert.Equal(1, eventCount);
        Assert.Equal(string.Empty, changedUrl);
        Assert.True(isCleared);
    }

    [Fact]
    public void OscHyperlink_WithId_SetsHyperlinkId()
    {
        // Arrange
        var terminal = CreateTerminal();

        // Act
        terminal.Write("\x1B]8;id=link123;http://example.com\x07");

        // Assert
        Assert.Equal("http://example.com", terminal.CurrentHyperlink);
        Assert.Equal("link123", terminal.HyperlinkId);
    }

    [Fact]
    public void OscHyperlink_CompleteSequence_Works()
    {
        // Arrange
        var terminal = CreateTerminal();

        // Act - Start link, print text, end link
        terminal.Write("\x1B]8;;https://github.com\x07");
        terminal.Write("GitHub");
        terminal.Write("\x1B]8;;\x07");

        // Assert
        Assert.Null(terminal.CurrentHyperlink);
        var line = terminal.GetLine(0);
        Assert.Contains("GitHub", line);
    }

    [Fact]
    public void OscColorQuery_Foreground_RespondsWithColor()
    {
        // Arrange
        var terminal = CreateTerminal();
        string? response = null;
        terminal.DataReceived += (sender, e) => response = e.Data;

        // Act
        terminal.Write("\x1B]10;?\x07");

        // Assert
        Assert.NotNull(response);
        Assert.Contains("rgb:", response);
        Assert.Contains("]10;", response);
    }

    [Fact]
    public void OscColorQuery_Background_RespondsWithColor()
    {
        // Arrange
        var terminal = CreateTerminal();
        string? response = null;
        terminal.DataReceived += (sender, e) => response = e.Data;

        // Act
        terminal.Write("\x1B]11;?\x07");

        // Assert
        Assert.NotNull(response);
        Assert.Contains("rgb:", response);
        Assert.Contains("]11;", response);
    }

    [Fact]
    public void OscColorQuery_Cursor_RespondsWithColor()
    {
        // Arrange
        var terminal = CreateTerminal();
        string? response = null;
        terminal.DataReceived += (sender, e) => response = e.Data;

        // Act
        terminal.Write("\x1B]12;?\x07");

        // Assert
        Assert.NotNull(response);
        Assert.Contains("rgb:", response);
        Assert.Contains("]12;", response);
    }

    [Fact]
    public void OscClipboard_Query_ReturnsHostDataWhenEnabled()
    {
        // Arrange
        var terminal = new Terminal(new TerminalOptions
        {
            ClipboardReadEnabled = true
        });
        string? response = null;
        terminal.DataReceived += (sender, e) => response = e.Data;
        terminal.ClipboardReadRequested += (_, e) => e.Data = System.Text.Encoding.UTF8.GetBytes("Hello");

        // Act
        terminal.Write("\x1B]52;c;?\x07");

        // Assert
        Assert.NotNull(response);
        Assert.Equal("\x1B]52;c;SGVsbG8=\x07", response);
    }

    [Fact]
    public void OscClipboard_Query_IsIgnoredByDefault()
    {
        // Reads are opt-in, and a disabled read answers NOTHING — the host is not even asked.
        var terminal = new Terminal(new TerminalOptions());
        string? response = null;
        var readRequested = false;
        terminal.DataReceived += (sender, e) => response = e.Data;
        terminal.ClipboardReadRequested += (_, _) => readRequested = true;

        // Act
        terminal.Write("\x1B]52;c;?\x07");

        // Assert
        Assert.Null(response);
        Assert.False(readRequested);
    }

    [Fact]
    public void OscClipboard_SetData_RaisesWriteRequest()
    {
        // Arrange
        var terminal = CreateTerminal();
        TerminalEvents.ClipboardWriteEventArgs? request = null;
        terminal.ClipboardWriteRequested += (_, e) => request = e;
        var base64Data = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("Hello, World!"));

        // Act
        terminal.Write($"\x1B]52;c;{base64Data}\x07");

        // Assert
        Assert.NotNull(request);
        Assert.Equal("c", request.Target);
        Assert.Equal("Hello, World!", request.Text);
    }

    [Fact]
    public void OscClipboard_Query_WhenEnabledAndHandled_ReturnsClipboardText()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.Options.ClipboardReadEnabled = true;
        string? response = null;
        terminal.ClipboardReadRequested += (_, e) =>
        {
            Assert.Equal("p", e.Target);
            e.Text = "Hello, World!";
        };
        terminal.DataReceived += (_, e) => response = e.Data;

        // Act
        terminal.Write("\x1B]52;p;?\x07");

        // Assert
        Assert.Equal("\x1B]52;p;SGVsbG8sIFdvcmxkIQ==\x07", response);
    }

    [Fact]
    public void OscClipboard_Query_CanBeAnsweredAfterTheHandlerReturns()
    {
        // The async-host path: an Avalonia clipboard is awaited, so the handler returns first
        // and answers later. The response must be byte-identical to the synchronous one.
        var terminal = new Terminal(new TerminalOptions { ClipboardReadEnabled = true });
        string? response = null;
        terminal.DataReceived += (_, e) => response = e.Data;
        TerminalEvents.ClipboardReadEventArgs? pending = null;
        terminal.ClipboardReadRequested += (_, e) => { e.Defer(); pending = e; };

        terminal.Write("\u001b]52;c;?\u0007");
        Assert.Null(response);                        // nothing answered yet...
        pending!.Respond("deferred");
        Assert.Equal("\u001b]52;c;ZGVmZXJyZWQ=\u0007", response);

        response = null;
        pending.Respond("again");                     // a second call is ignored
        Assert.Null(response);
    }

    [Fact]
    public void OscClipboard_DeferredDecline_StaysSilent()
    {
        var terminal = new Terminal(new TerminalOptions { ClipboardReadEnabled = true });
        string? response = null;
        terminal.DataReceived += (_, e) => response = e.Data;
        TerminalEvents.ClipboardReadEventArgs? pending = null;
        terminal.ClipboardReadRequested += (_, e) => { e.Defer(); pending = e; };

        terminal.Write("\u001b]52;c;?\u0007");
        pending!.Respond((string?)null);
        Assert.Null(response);
    }

    [Fact]
    public void OscClipboard_RespondInsideTheHandlerPlusHandled_EmitsOnce()
    {
        // Off-contract but easy to write: a handler that serves from a cache calls Respond
        // synchronously AND sets Handled. The single-response guarantee must hold — whoever
        // claims the response first wins, and the other path stays silent.
        var terminal = new Terminal(new TerminalOptions { ClipboardReadEnabled = true });
        var responses = new List<string>();
        terminal.DataReceived += (_, e) => responses.Add(e.Data);
        terminal.ClipboardReadRequested += (_, e) =>
        {
            e.Respond("cached");
            e.Text = "sync";
        };

        terminal.Write("\u001b]52;c;?\u0007");

        Assert.Single(responses);
        Assert.Equal("\u001b]52;c;Y2FjaGVk\u0007", responses[0]);
    }

    [Fact]
    public void OscClipboard_SyncAnswerDisarmsRespond()
    {
        // Handled synchronously answers as the handler returns; Respond afterwards must not
        // produce a SECOND response.
        var terminal = new Terminal(new TerminalOptions { ClipboardReadEnabled = true });
        var responses = new List<string>();
        terminal.DataReceived += (_, e) => responses.Add(e.Data);
        TerminalEvents.ClipboardReadEventArgs? pending = null;
        terminal.ClipboardReadRequested += (_, e) => { e.Text = "sync"; pending = e; };

        terminal.Write("\u001b]52;c;?\u0007");
        pending!.Respond("late");
        Assert.Single(responses);
        Assert.Equal("\u001b]52;c;c3luYw==\u0007", responses[0]);
    }

    [Fact]
    public void OscClipboard_Query_WhenEnabledAndDeclined_DoesNotRespond()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.Options.ClipboardReadEnabled = true;
        string? response = null;
        var raised = false;
        terminal.ClipboardReadRequested += (_, e) =>
        {
            raised = true;
            Assert.Equal("s", e.Target);
        };
        terminal.DataReceived += (_, e) => response = e.Data;

        // Act
        terminal.Write("\x1B]52;s;?\x07");

        // Assert
        Assert.True(raised);
        Assert.Null(response);
    }

    [Fact]
    public void OscClipboard_SetInvalidData_RaisesClearRequest()
    {
        // Arrange
        var terminal = CreateTerminal();
        TerminalEvents.ClipboardWriteEventArgs? request = null;
        terminal.ClipboardWriteRequested += (_, e) => request = e;

        // Act
        terminal.Write("\x1B]52;c;!\x07");

        // Assert
        Assert.NotNull(request);
        Assert.Equal("c", request.Target);
        Assert.Equal(string.Empty, request.Text);
    }

    [Fact]
    public void OscClipboard_EmptyTarget_DefaultsToSelectionZero()
    {
        // Arrange
        var terminal = CreateTerminal();
        TerminalEvents.ClipboardWriteEventArgs? request = null;
        terminal.ClipboardWriteRequested += (_, e) => request = e;
        var base64Data = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("Hello, World!"));

        // Act
        terminal.Write($"\x1B]52;;{base64Data}\x07");

        // Assert
        Assert.NotNull(request);
        Assert.Equal("s0", request.Target);
    }

    [Fact]
    public void OscClipboard_InvalidTarget_IsIgnored()
    {
        // Arrange
        var terminal = CreateTerminal();
        var raised = false;
        terminal.ClipboardWriteRequested += (_, _) => raised = true;
        var base64Data = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("Hello, World!"));

        // Act
        terminal.Write($"\x1B]52;x;{base64Data}\x07");

        // Assert
        Assert.False(raised);
    }

    [Fact]
    public void OscClipboard_SetData_PropagatesHostExceptions()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.ClipboardWriteRequested += (_, _) => throw new InvalidOperationException();
        var base64Data = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("Hello, World!"));

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => terminal.Write($"\x1B]52;c;{base64Data}\x07"));
    }

    [Fact]
    public void OscClipboard_SetData_WhenDisabled_IsIgnored()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.Options.ClipboardWriteEnabled = false;
        var raised = false;
        terminal.ClipboardWriteRequested += (_, _) => raised = true;
        var base64Data = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("Hello, World!"));

        // Act
        terminal.Write($"\x1B]52;s;{base64Data}\x07");

        // Assert
        Assert.False(raised);
    }

    [Fact]
    public void OscKittyClipboard_WriteChunks_RaisesClipboardWriteRequested()
    {
        // Arrange
        var terminal = CreateTerminal();
        TerminalEvents.ClipboardWriteEventArgs? request = null;
        string? response = null;
        terminal.ClipboardWriteRequested += (_, e) => request = e;
        terminal.DataReceived += (_, e) => response = e.Data;

        // Act
        terminal.Write("\x1B]5522;type=write\x1B\\");
        terminal.Write("\x1B]5522;type=wdata:mime=dGV4dC9wbGFpbg==;aGVsbG8=\x07");
        terminal.Write("\x1B]5522;type=wdata\x1B\\");

        // Assert
        Assert.NotNull(request);
        Assert.Equal("c", request!.Target);
        Assert.Equal("text/plain", request.MimeType);
        Assert.Equal("hello", request.Text);
        Assert.Equal("\x1B]5522;type=write:status=DONE\x1B\\", response);
    }

    [Fact]
    public void OscKittyClipboard_Write_PreservesMimeTypeAndBinaryData()
    {
        // Arrange
        var terminal = CreateTerminal();
        TerminalEvents.ClipboardWriteEventArgs? request = null;
        terminal.ClipboardWriteRequested += (_, e) => request = e;

        // Act
        terminal.Write("\x1B]5522;type=write\x1B\\");
        terminal.Write("\x1B]5522;type=wdata:mime=aW1hZ2UvcG5n;AP+A\x07");
        terminal.Write("\x1B]5522;type=wdata\x1B\\");

        // Assert
        Assert.NotNull(request);
        Assert.Equal("image/png", request!.MimeType);
        Assert.Equal([0x00, 0xFF, 0x80], request.Data);
    }

    [Fact]
    public void OscKittyClipboard_WriteStart_ReplacesAbandonedTransfer()
    {
        // Arrange
        var terminal = CreateTerminal();
        string? text = null;
        terminal.ClipboardWriteRequested += (_, e) => text = e.Text;

        // Act
        terminal.Write("\x1B]5522;type=write\x1B\\");
        terminal.Write("\x1B]5522;type=wdata:mime=dGV4dC9wbGFpbg==;b2xk\x1B\\");
        terminal.Write("\x1B]5522;type=write\x1B\\");
        terminal.Write("\x1B]5522;type=wdata:mime=dGV4dC9wbGFpbg==;bmV3\x1B\\");
        terminal.Write("\x1B]5522;type=wdata\x1B\\");

        // Assert
        Assert.Equal("new", text);
    }

    [Fact]
    public void OscKittyClipboard_Write_CommitsEveryMimeTypeInOneEvent()
    {
        // Platform clipboards replace their contents on each set, so the transfer arrives as ONE
        // event carrying every format — the host builds one data object and commits once.
        var terminal = new Terminal(new TerminalOptions());
        var events = new List<TerminalEvents.ClipboardWriteEventArgs>();
        terminal.ClipboardWriteRequested += (_, e) => events.Add(e);
        string? response = null;
        terminal.DataReceived += (_, e) => response = e.Data;

        var plainMime = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("text/plain"));
        var htmlMime = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("text/html"));
        terminal.Write("\u001b]5522;type=write:id=w1\u001b\\");
        terminal.Write($"\u001b]5522;type=wdata:mime={plainMime};{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("hello"))}\u001b\\");
        terminal.Write($"\u001b]5522;type=wdata:mime={htmlMime};{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("<b>hello</b>"))}\u001b\\");
        terminal.Write("\u001b]5522;type=wdata\u001b\\");

        var e = Assert.Single(events);
        Assert.Equal(2, e.Formats.Count);
        Assert.Equal("text/plain", e.Formats[0].MimeType);
        Assert.Equal("hello", System.Text.Encoding.UTF8.GetString(e.Formats[0].Data));
        Assert.Equal("text/html", e.Formats[1].MimeType);
        Assert.Equal("<b>hello</b>", System.Text.Encoding.UTF8.GetString(e.Formats[1].Data));
        Assert.Equal("hello", e.Text);                       // the text/* convenience
        Assert.Equal("\u001b]5522;type=write:status=DONE:id=w1\u001b\\", response);
    }

    [Fact]
    public void OscKittyClipboard_WriteAlias_RidesTheSameEventWithTargetData()
    {
        var terminal = new Terminal(new TerminalOptions());
        var events = new List<TerminalEvents.ClipboardWriteEventArgs>();
        terminal.ClipboardWriteRequested += (_, e) => events.Add(e);

        var plainMime = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("text/plain"));
        var aliasList = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("UTF8_STRING"));
        terminal.Write("\u001b]5522;type=write\u001b\\");
        terminal.Write($"\u001b]5522;type=wdata:mime={plainMime};{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("hi"))}\u001b\\");
        terminal.Write($"\u001b]5522;type=walias:mime={plainMime};{aliasList}\u001b\\");
        terminal.Write("\u001b]5522;type=wdata\u001b\\");

        var e = Assert.Single(events);
        Assert.Equal(2, e.Formats.Count);
        Assert.Equal("text/plain", e.Formats[0].MimeType);
        Assert.Equal("UTF8_STRING", e.Formats[1].MimeType);
        Assert.Equal(e.Formats[0].Data, e.Formats[1].Data);  // the alias shares the target's bytes
    }

    [Fact]
    public void OscKittyClipboard_WriteError_EchoesIdAndIgnoresStrayData()
    {
        // Arrange
        var terminal = CreateTerminal();
        var responses = new List<string>();
        terminal.DataReceived += (_, e) => responses.Add(e.Data);

        // Act
        terminal.Write("\x1B]5522;type=write:id=w1;\x1B\\");
        terminal.Write("\x1B]5522;type=wdata:mime=dGV4dC9wbGFpbg==;invalid!\x1B\\");
        terminal.Write("\x1B]5522;type=wdata:mime=dGV4dC9wbGFpbg==;dGV4dA==\x1B\\");

        // Assert
        Assert.Equal(["\x1B]5522;type=write:status=EINVAL:id=w1\x1B\\"], responses);
    }

    [Fact]
    public void OscKittyClipboard_WriteHonorsConfiguredSizeLimit()
    {
        // Arrange
        var terminal = new Terminal(new TerminalOptions { MaxClipboardBytes = 1 });
        string? response = null;
        terminal.DataReceived += (_, e) => response = e.Data;

        // Act
        terminal.Write("\x1B]5522;type=write:id=w1\x1B\\");
        terminal.Write("\x1B]5522;type=wdata:mime=dGV4dC9wbGFpbg==;dHc=\x1B\\");

        // Assert
        Assert.Equal("\x1B]5522;type=write:status=EIO:id=w1\x1B\\", response);
    }

    [Fact]
    public void OscKittyClipboard_AliasLimitReturnsEfbig()
    {
        // Arrange
        var terminal = new Terminal(new TerminalOptions { MaxClipboardBytes = 1 });
        string? response = null;
        terminal.DataReceived += (_, e) => response = e.Data;

        // Act
        terminal.Write("\x1B]5522;type=write:id=w1\x1B\\");
        terminal.Write("\x1B]5522;type=walias:mime=dGV4dC9wbGFpbg==;VVRGOF9TVFJJTkc=\x1B\\");

        // Assert
        Assert.Equal("\x1B]5522;type=write:status=EIO:id=w1\x1B\\", response);
    }

    [Fact]
    public void OscKittyClipboard_MimeEntriesCountAgainstTransferLimit()
    {
        // Arrange
        var terminal = new Terminal(new TerminalOptions { MaxClipboardBytes = 600 });
        string? response = null;
        terminal.DataReceived += (_, e) => response = e.Data;

        // Act
        terminal.Write("\x1B]5522;type=write:id=w1\x1B\\");
        terminal.Write("\x1B]5522;type=wdata:mime=dGV4dC9h;\x1B\\");
        terminal.Write("\x1B]5522;type=wdata:mime=dGV4dC9i;\x1B\\");
        terminal.Write("\x1B]5522;type=wdata:mime=dGV4dC9j;\x1B\\");

        // Assert
        Assert.Equal("\x1B]5522;type=write:status=EIO:id=w1\x1B\\", response);
    }

    [Fact]
    public void OscKittyClipboard_Read_RequiresOptInAndReturnsHostData()
    {
        // Arrange
        var terminal = new Terminal(new TerminalOptions
        {
            ClipboardReadEnabled = true
        });
        var responses = new List<string>();
        terminal.DataReceived += (_, e) => responses.Add(e.Data);
        string? target = null;
        terminal.ClipboardReadRequested += (_, e) =>
        {
            target = e.Target;
            e.Data = System.Text.Encoding.UTF8.GetBytes("hello");
        };

        // Act
        terminal.Write("\x1B]5522;type=read:loc=primary:id=r1;dGV4dC9wbGFpbg==\x1B\\");

        // Assert
        Assert.Equal(
            [
                "\x1B]5522;type=read:status=OK:id=r1\x1B\\",
                "\x1B]5522;type=read:status=DATA:mime=dGV4dC9wbGFpbg==:id=r1;aGVsbG8=\x1B\\",
                "\x1B]5522;type=read:status=DONE:id=r1\x1B\\"
            ],
            responses);
        Assert.Equal("p", target);
    }

    [Fact]
    public void OscKittyClipboard_Read_UsesAnyRequestedMimeType()
    {
        // Arrange
        var terminal = new Terminal(new TerminalOptions { ClipboardReadEnabled = true });
        var responses = new List<string>();
        terminal.DataReceived += (_, e) => responses.Add(e.Data);
        terminal.ClipboardReadRequested += (_, e) =>
        {
            if (e.MimeType == "text/plain")
                e.Data = System.Text.Encoding.UTF8.GetBytes("text");
        };

        // Act
        terminal.Write("\x1B]5522;type=read;dGV4dC9odG1sIHRleHQvcGxhaW4=\x1B\\");

        // Assert
        Assert.Contains("\x1B]5522;type=read:status=DATA:mime=dGV4dC9wbGFpbg==;dGV4dA==\x1B\\", responses);
    }

    [Fact]
    public void OscKittyClipboard_ReadTypeList_DecodesDotPayload()
    {
        // Arrange
        var terminal = new Terminal(new TerminalOptions { ClipboardReadEnabled = true });
        string? requestedMimeType = null;
        terminal.ClipboardReadRequested += (_, e) =>
        {
            requestedMimeType = e.MimeType;
            e.Data = System.Text.Encoding.UTF8.GetBytes("text/plain");
        };

        // Act
        terminal.Write("\x1B]5522;type=read;Lg==\x1B\\");

        // Assert
        Assert.Equal(".", requestedMimeType);
    }

    [Fact]
    public void OscKittyClipboard_ReadDisabled_DoesNotRequestOrRespond()
    {
        // Arrange
        var terminal = CreateTerminal();
        var requested = false;
        string? response = null;
        terminal.ClipboardReadRequested += (_, _) => requested = true;
        terminal.DataReceived += (_, e) => response = e.Data;

        // Act
        terminal.Write("\x1B]5522;type=read;dGV4dC9wbGFpbg==\x07");

        // Assert
        Assert.False(requested);
        Assert.Equal("\x1B]5522;type=read:status=EPERM\x1B\\", response);
    }

    [Fact]
    public void OscKittyClipboard_Read_CanBeAnsweredAfterTheHandlerReturns()
    {
        // The async-host path: the reply cannot begin until every requested mime resolves, so a
        // mixed sync/deferred pair emits nothing until the deferred one lands — then the whole
        // OK/DATA/DONE flow goes out in order.
        var terminal = new Terminal(new TerminalOptions { ClipboardReadEnabled = true });
        var responses = new List<string>();
        terminal.DataReceived += (_, e) => responses.Add(e.Data);
        TerminalEvents.ClipboardReadEventArgs? pending = null;
        terminal.ClipboardReadRequested += (_, e) =>
        {
            if (e.MimeType == "text/plain")
                e.Text = "sync";
            else
            {
                e.Defer();
                pending = e;
            }
        };

        var mimes = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("text/plain text/html"));
        terminal.Write($"\u001b]5522;type=read:id=r1;{mimes}\u001b\\");
        Assert.Empty(responses);

        pending!.Respond("<b>late</b>");

        Assert.Equal("\u001b]5522;type=read:status=OK:id=r1\u001b\\", responses[0]);
        Assert.Contains("status=DATA", responses[1]);
        Assert.Contains("status=DATA", responses[2]);
        Assert.Equal("\u001b]5522;type=read:status=DONE:id=r1\u001b\\", responses[^1]);
        Assert.Equal(4, responses.Count);
    }

    [Fact]
    public void OscKittyClipboard_DeferredDeclineOfEveryMime_AnswersEperm()
    {
        // Unlike OSC 52, 5522 must ANSWER a decline: all-deferred, all-null resolves to EPERM.
        var terminal = new Terminal(new TerminalOptions { ClipboardReadEnabled = true });
        var responses = new List<string>();
        terminal.DataReceived += (_, e) => responses.Add(e.Data);
        var pending = new List<TerminalEvents.ClipboardReadEventArgs>();
        terminal.ClipboardReadRequested += (_, e) => { e.Defer(); pending.Add(e); };

        var mimes = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("text/plain"));
        terminal.Write($"\u001b]5522;type=read:id=r2;{mimes}\u001b\\");
        Assert.Empty(responses);

        pending.Single().Respond((byte[]?)null);

        Assert.Equal("\u001b]5522;type=read:status=EPERM:id=r2\u001b\\", responses.Single());
    }

    [Fact]
    public void OscColorPalette_Change_DoesNotThrow()
    {
        // Arrange
        var terminal = CreateTerminal();

        // Act & Assert - Should not throw
        terminal.Write("\x1B]4;1;rgb:ff/00/00\x07"); // Set color 1 to red
    }

    [Fact]
    public void OscColorReset_DoesNotThrow()
    {
        // Arrange
        var terminal = CreateTerminal();

        // Act & Assert - Should not throw
        terminal.Write("\x1B]104;1\x07"); // Reset color 1
        terminal.Write("\x1B]104\x07");   // Reset all colors
    }

    [Fact]
    public void OscMultipleSequences_AllProcessed()
    {
        // Arrange
        var terminal = CreateTerminal();
        var titleChangeCount = 0;
        var directoryChangeCount = 0;
        terminal.TitleChanged += (sender, e) => titleChangeCount++;
        terminal.DirectoryChanged += (sender, e) => directoryChangeCount++;

        // Act
        terminal.Write("\x1B]0;Title1\x07");
        terminal.Write("\x1B]7;file://localhost/path1\x07");
        terminal.Write("\x1B]0;Title2\x07");
        terminal.Write("\x1B]7;file://localhost/path2\x07");

        // Assert
        Assert.Equal("Title2", terminal.Title);
        Assert.Equal("/path2", terminal.CurrentDirectory);
        Assert.Equal(2, titleChangeCount);
        Assert.Equal(2, directoryChangeCount);
    }

    [Fact]
    public void OscWithText_InterleavedCorrectly()
    {
        // Arrange
        var terminal = CreateTerminal();

        // Act
        terminal.Write("Before ");
        terminal.Write("\x1B]0;Test Title\x07");
        terminal.Write("After");

        // Assert
        Assert.Equal("Test Title", terminal.Title);
        var line = terminal.GetLine(0);
        Assert.Contains("Before After", line);
    }

    [Fact]
    public void OscInvalidSequence_DoesNotCrash()
    {
        // Arrange
        var terminal = CreateTerminal();

        // Act & Assert - Should not throw
        terminal.Write("\x1B]999;invalid\x07");
        terminal.Write("\x1B]\x07");
        terminal.Write("\x1B];\x07");
    }

    [Fact]
    public void OscHyperlink_MultipleParams_ParsesCorrectly()
    {
        // Arrange
        var terminal = CreateTerminal();

        // Act
        terminal.Write("\x1B]8;id=abc:key=value;http://test.com\x07");

        // Assert
        Assert.Equal("http://test.com", terminal.CurrentHyperlink);
        Assert.Equal("abc", terminal.HyperlinkId);
    }

    [Fact]
    public void OscDirectoryChange_MultipleEvents_FiresEachTime()
    {
        // Arrange
        var terminal = CreateTerminal();
        var paths = new List<string>();
        terminal.DirectoryChanged += (sender, e) => paths.Add(e.Directory);

        // Act
        terminal.Write("\x1B]7;file://localhost/home\x07");
        terminal.Write("\x1B]7;file://localhost/usr\x07");
        terminal.Write("\x1B]7;file://localhost/var\x07");

        // Assert
        Assert.Equal(3, paths.Count);
        Assert.Equal("/home", paths[0]);
        Assert.Equal("/usr", paths[1]);
        Assert.Equal("/var", paths[2]);
    }

    [Fact]
    public void OscTitleChange_SpecialCharacters_HandlesCorrectly()
    {
        // Arrange
        var terminal = CreateTerminal();

        // Act
        terminal.Write("\x1B]0;Title with émojis 😀 and spëcial chars\x07");

        // Assert
        Assert.Equal("Title with émojis 😀 and spëcial chars", terminal.Title);
    }

    [Fact]
    public void OscHyperlink_ComplexUrl_PreservesUrl()
    {
        // Arrange
        var terminal = CreateTerminal();

        // Act
        terminal.Write("\x1B]8;;https://example.com/path?param=value&other=123#anchor\x07");

        // Assert
        Assert.Equal("https://example.com/path?param=value&other=123#anchor", terminal.CurrentHyperlink);
    }

    [Fact]
    public void OscEmptyCommand_DoesNotCrash()
    {
        // Arrange
        var terminal = CreateTerminal();

        // Act & Assert - Should not throw
        terminal.Write("\x1B]\x07");
    }

    [Fact]
    public void OscColorQueries_Sequential_AllRespond()
    {
        // Arrange
        var terminal = CreateTerminal();
        var responses = new List<string>();
        terminal.DataReceived += (sender, e) => responses.Add(e.Data);

        // Act
        terminal.Write("\x1B]10;?\x07");
        terminal.Write("\x1B]11;?\x07");
        terminal.Write("\x1B]12;?\x07");

        // Assert
        Assert.Equal(3, responses.Count);
        Assert.All(responses, r => Assert.Contains("rgb:", r));
    }
}
