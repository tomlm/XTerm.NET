using XTerm;
using XTerm.Options;
using XTerm.Parser;
using XTerm.Common;

namespace XTerm.Tests;

public class WindowManipulationTests
{
    private Terminal CreateTerminal(WindowOptions? windowOptions = null)
    {
        var options = new TerminalOptions
        {
            Cols = 80,
            Rows = 24,
            WindowOptions = windowOptions ?? new WindowOptions()
        };
        return new Terminal(options);
    }

    [Fact]
    public void Terminal_InitializesWindowEvents()
    {
        // Arrange & Act
        var terminal = CreateTerminal();

        // Assert
        // C# event - no null check needed
        Assert.NotNull(terminal);
    }

    [Fact]
    public void WindowManipulation_MoveWindow_FiresOnWindowMove()
    {
        // Arrange
        var windowOptions = new WindowOptions { SetWinPosition = true };
        var terminal = CreateTerminal(windowOptions);
        var moveEventFired = false;
        int capturedX = 0, capturedY = 0;

        terminal.WindowMoved += (sender, e) =>
        {
            moveEventFired = true;
            capturedX = e.X;
            capturedY = e.Y;
        };

        // Act
        terminal.Write("\x1b[3;100;200t"); // CSI 3 ; 100 ; 200 t

        // Assert
        Assert.True(moveEventFired);
        Assert.Equal(100, capturedX);
        Assert.Equal(200, capturedY);
    }

    [Fact]
    public void WindowManipulation_MoveWindow_DoesNotFireWhenPermissionDenied()
    {
        // Arrange
        var windowOptions = new WindowOptions { SetWinPosition = false };
        var terminal = CreateTerminal(windowOptions);
        var moveEventFired = false;

        terminal.WindowMoved += (sender, e) => moveEventFired = true;

        // Act
        terminal.Write("\x1b[3;100;200t");

        // Assert
        Assert.False(moveEventFired);
    }

    [Fact]
    public void WindowManipulation_ResizeWindow_FiresOnWindowResize()
    {
        // Arrange
        var windowOptions = new WindowOptions { SetWinSizePixels = true };
        var terminal = CreateTerminal(windowOptions);
        var resizeEventFired = false;
        int capturedWidth = 0, capturedHeight = 0;

        terminal.WindowResized += (sender, e) =>
        {
            resizeEventFired = true;
            capturedWidth = e.Width;
            capturedHeight = e.Height;
        };

        // Act
        terminal.Write("\x1b[4;600;800t"); // CSI 4 ; 600 ; 800 t

        // Assert
        Assert.True(resizeEventFired);
        Assert.Equal(800, capturedWidth);
        Assert.Equal(600, capturedHeight);
    }

    [Fact]
    public void WindowManipulation_MinimizeWindow_FiresOnWindowMinimize()
    {
        // Arrange
        var windowOptions = new WindowOptions { MinimizeWin = true };
        var terminal = CreateTerminal(windowOptions);
        var eventFired = false;

        terminal.WindowMinimized += (sender, e) => eventFired = true;

        // Act
        terminal.Write("\x1b[2t"); // CSI 2 t

        // Assert
        Assert.True(eventFired);
    }

    [Fact]
    public void WindowManipulation_MaximizeWindow_FiresOnWindowMaximize()
    {
        // Arrange
        var windowOptions = new WindowOptions { MaximizeWin = true };
        var terminal = CreateTerminal(windowOptions);
        var eventFired = false;

        terminal.WindowMaximized += (sender, e) => eventFired = true;

        // Act
        terminal.Write("\x1b[9;1t"); // CSI 9 ; 1 t

        // Assert
        Assert.True(eventFired);
    }

    [Fact]
    public void WindowManipulation_RestoreWindow_FiresOnWindowRestore()
    {
        // Arrange
        var windowOptions = new WindowOptions { RestoreWin = true };
        var terminal = CreateTerminal(windowOptions);
        var eventFired = false;

        terminal.WindowRestored += (sender, e) => eventFired = true;

        // Act - Test both operation 1 (de-iconify) and operation 9;0 (restore from maximize)
        terminal.Write("\x1b[1t"); // CSI 1 t

        // Assert
        Assert.True(eventFired);
    }

    [Fact]
    public void WindowManipulation_RestoreFromMaximize_FiresOnWindowRestore()
    {
        // Arrange
        var windowOptions = new WindowOptions { RestoreWin = true };
        var terminal = CreateTerminal(windowOptions);
        var eventFired = false;

        terminal.WindowRestored += (sender, e) => eventFired = true;

        // Act
        terminal.Write("\x1b[9;0t"); // CSI 9 ; 0 t

        // Assert
        Assert.True(eventFired);
    }

    [Fact]
    public void WindowManipulation_RaiseWindow_FiresOnWindowRaise()
    {
        // Arrange
        var windowOptions = new WindowOptions { RaiseWin = true };
        var terminal = CreateTerminal(windowOptions);
        var eventFired = false;

        terminal.WindowRaised += (sender, e) => eventFired = true;

        // Act
        terminal.Write("\x1b[5t"); // CSI 5 t

        // Assert
        Assert.True(eventFired);
    }

    [Fact]
    public void WindowManipulation_LowerWindow_FiresOnWindowLower()
    {
        // Arrange
        var windowOptions = new WindowOptions { LowerWin = true };
        var terminal = CreateTerminal(windowOptions);
        var eventFired = false;

        terminal.WindowLowered += (sender, e) => eventFired = true;

        // Act
        terminal.Write("\x1b[6t"); // CSI 6 t

        // Assert
        Assert.True(eventFired);
    }

    [Fact]
    public void WindowManipulation_RefreshWindow_FiresOnWindowRefresh()
    {
        // Arrange
        var windowOptions = new WindowOptions { RefreshWin = true };
        var terminal = CreateTerminal(windowOptions);
        var eventFired = false;

        terminal.WindowRefreshed += (sender, e) => eventFired = true;

        // Act
        terminal.Write("\x1b[7t"); // CSI 7 t

        // Assert
        Assert.True(eventFired);
    }

    [Fact]
    public void WindowManipulation_FullscreenToggle_FiresOnWindowFullscreen()
    {
        // Arrange
        var windowOptions = new WindowOptions { FullscreenWin = true };
        var terminal = CreateTerminal(windowOptions);
        var eventCount = 0;

        terminal.WindowFullscreened += (sender, e) => eventCount++;

        // Act
        terminal.Write("\x1b[10;0t"); // Exit fullscreen
        terminal.Write("\x1b[10;1t"); // Enter fullscreen
        terminal.Write("\x1b[10;2t"); // Toggle fullscreen

        // Assert
        Assert.Equal(3, eventCount);
    }

    [Fact]
    public void WindowManipulation_QueryWindowState_FiresOnWindowInfoRequest()
    {
        // Arrange
        var windowOptions = new WindowOptions { GetWinState = true };
        var terminal = CreateTerminal(windowOptions);
        var requestReceived = false;
        WindowInfoRequest capturedRequest = default;

        terminal.WindowInfoRequested += (sender, e) =>
        {
            requestReceived = true;
            capturedRequest = e.Request;
        };

        // Act
        terminal.Write("\x1b[11t"); // CSI 11 t

        // Assert
        Assert.True(requestReceived);
        Assert.Equal(WindowInfoRequest.State, capturedRequest);
    }

    [Fact]
    public void WindowManipulation_QueryWindowPosition_FiresOnWindowInfoRequest()
    {
        // Arrange
        var windowOptions = new WindowOptions { GetWinPosition = true };
        var terminal = CreateTerminal(windowOptions);
        var requestReceived = false;
        WindowInfoRequest capturedRequest = default;

        terminal.WindowInfoRequested += (sender, e) =>
        {
            requestReceived = true;
            capturedRequest = e.Request;
        };

        // Act
        terminal.Write("\x1b[13t"); // CSI 13 t

        // Assert
        Assert.True(requestReceived);
        Assert.Equal(WindowInfoRequest.Position, capturedRequest);
    }

    [Fact]
    public void WindowManipulation_QueryWindowSizePixels_FiresOnWindowInfoRequest()
    {
        // Arrange
        var windowOptions = new WindowOptions { GetWinSizePixels = true };
        var terminal = CreateTerminal(windowOptions);
        var requestReceived = false;
        WindowInfoRequest capturedRequest = default;

        terminal.WindowInfoRequested += (sender, e) =>
        {
            requestReceived = true;
            capturedRequest = e.Request;
        };

        // Act
        terminal.Write("\x1b[14t"); // CSI 14 t

        // Assert
        Assert.True(requestReceived);
        Assert.Equal(WindowInfoRequest.SizePixels, capturedRequest);
    }

    [Fact]
    public void WindowManipulation_QueryScreenSizePixels_FiresOnWindowInfoRequest()
    {
        // Arrange
        var windowOptions = new WindowOptions { GetScreenSizePixels = true };
        var terminal = CreateTerminal(windowOptions);
        var requestReceived = false;

        terminal.WindowInfoRequested += (sender, e) =>
        {
            requestReceived = true;
        };

        // Act
        terminal.Write("\x1b[15t"); // CSI 15 t

        // Assert
        Assert.True(requestReceived);
    }

    [Fact]
    public void WindowManipulation_QueryCellSizePixels_FiresOnWindowInfoRequest()
    {
        // Arrange
        var windowOptions = new WindowOptions { GetCellSizePixels = true };
        var terminal = CreateTerminal(windowOptions);
        var requestReceived = false;

        terminal.WindowInfoRequested += (sender, e) =>
        {
            requestReceived = true;
        };

        // Act
        terminal.Write("\x1b[16t"); // CSI 16 t

        // Assert
        Assert.True(requestReceived);
    }

    [Fact]
    public void WindowManipulation_QueryTextAreaSize_RespondsWithSize()
    {
        // Arrange
        var windowOptions = new WindowOptions { GetWinSizeChars = true };
        var terminal = CreateTerminal(windowOptions);
        var responseReceived = false;
        string capturedResponse = string.Empty;

        terminal.DataReceived += (sender, e) =>
        {
            responseReceived = true;
            capturedResponse = e.Data;
        };

        // Act
        terminal.Write("\x1b[18t"); // CSI 18 t

        // Assert
        Assert.True(responseReceived);
        Assert.Contains($"{terminal.Rows}", capturedResponse);
        Assert.Contains($"{terminal.Cols}", capturedResponse);
        Assert.Contains("\u001b[8;", capturedResponse);
    }

    [Fact]
    public void WindowManipulation_QueryWindowTitle_SendsDirectResponse()
    {
        // Arrange - Window title query (21t) sends direct response using terminal's Title
        var windowOptions = new WindowOptions { GetWinTitle = true };
        var terminal = CreateTerminal(windowOptions);
        terminal.Title = "Test Title";
        
        var responseReceived = false;
        string capturedResponse = string.Empty;

        terminal.DataReceived += (sender, e) =>
        {
            responseReceived = true;
            capturedResponse = e.Data;
        };

        // Act
        terminal.Write("\x1b[21t"); // CSI 21 t

        // Assert
        Assert.True(responseReceived);
        Assert.Contains("Test Title", capturedResponse);
        Assert.Equal("\u001b]lTest Title\u0007", capturedResponse);
    }

    [Fact]
    public void WindowManipulation_QueryIconTitle_FiresOnWindowInfoRequest()
    {
        // Arrange
        var windowOptions = new WindowOptions { GetIconTitle = true };
        var terminal = CreateTerminal(windowOptions);
        var requestReceived = false;

        terminal.WindowInfoRequested += (sender, e) =>
        {
            requestReceived = true;
        };

        // Act
        terminal.Write("\x1b[20t"); // CSI 20 t

        // Assert
        Assert.True(requestReceived);
    }

    [Fact]
    public void WindowManipulation_ResizeTextArea_ResizesTerminal()
    {
        // Arrange
        var windowOptions = new WindowOptions { SetWinSizeChars = true };
        var terminal = CreateTerminal(windowOptions);
        var initialCols = terminal.Cols;
        var initialRows = terminal.Rows;

        // Act
        terminal.Write("\x1b[8;30;100t"); // CSI 8 ; 30 ; 100 t (resize to 30 rows, 100 cols)

        // Assert
        Assert.Equal(100, terminal.Cols);
        Assert.Equal(30, terminal.Rows);
    }

    [Fact]
    public void WindowManipulation_ResizeTextArea_DoesNotResizeWhenPermissionDenied()
    {
        // Arrange
        var windowOptions = new WindowOptions { SetWinSizeChars = false };
        var terminal = CreateTerminal(windowOptions);
        var initialCols = terminal.Cols;
        var initialRows = terminal.Rows;

        // Act
        terminal.Write("\x1b[8;30;100t");

        // Assert
        Assert.Equal(initialCols, terminal.Cols);
        Assert.Equal(initialRows, terminal.Rows);
    }

    [Fact]
    public void WindowManipulation_MultipleOperations_AllFireCorrectly()
    {
        // Arrange
        var windowOptions = new WindowOptions
        {
            SetWinPosition = true,
            MinimizeWin = true,
            MaximizeWin = true,
            RaiseWin = true
        };
        var terminal = CreateTerminal(windowOptions);
        
        var moveCount = 0;
        var minimizeCount = 0;
        var maximizeCount = 0;
        var raiseCount = 0;

        terminal.WindowMoved += (sender, e) => moveCount++;
        terminal.WindowMinimized += (sender, e) => minimizeCount++;
        terminal.WindowMaximized += (sender, e) => maximizeCount++;
        terminal.WindowRaised += (sender, e) => raiseCount++;

        // Act
        terminal.Write("\x1b[3;10;20t");  // Move
        terminal.Write("\x1b[2t");        // Minimize
        terminal.Write("\x1b[9;1t");      // Maximize
        terminal.Write("\x1b[5t");        // Raise
        terminal.Write("\x1b[3;30;40t");  // Move again

        // Assert
        Assert.Equal(2, moveCount);
        Assert.Equal(1, minimizeCount);
        Assert.Equal(1, maximizeCount);
        Assert.Equal(1, raiseCount);
    }

    [Fact]
    public void WindowManipulation_InvalidOperation_DoesNotCrash()
    {
        // Arrange
        var terminal = CreateTerminal();

        // Act & Assert - Should not throw
        terminal.Write("\x1b[999t"); // Invalid operation
    }

    [Fact]
    public void WindowManipulation_MissingParameters_DoesNotCrash()
    {
        // Arrange
        var windowOptions = new WindowOptions { SetWinPosition = true };
        var terminal = CreateTerminal(windowOptions);
        var eventFired = false;

        terminal.WindowMoved += (sender, e) => eventFired = true;

        // Act & Assert - Should not throw, may or may not fire with default values
        terminal.Write("\x1b[3t"); // Missing x, y parameters
    }

    [Fact]
    public void Dispose_ClearsWindowEvents()
    {
        // Arrange
        var terminal = CreateTerminal();
        var eventCount = 0;
        var windowOptions = new WindowOptions { MinimizeWin = true };
        terminal.Options.WindowOptions.MinimizeWin = true;
        
        terminal.WindowMinimized += (sender, e) => eventCount++;

        // Act
        terminal.Dispose();
        terminal.Write("\x1b[2t"); // Try to trigger minimize

        // Assert
        Assert.Equal(0, eventCount); // Event should not fire after dispose
    }

    [Fact]
    public void WindowInfoRequest_AllEnumValues_AreDefined()
    {
        // Assert - Verify all enum values are defined
        Assert.Equal(WindowInfoRequest.Position, WindowInfoRequest.Position);
        Assert.Equal(WindowInfoRequest.SizePixels, WindowInfoRequest.SizePixels);
        Assert.Equal(WindowInfoRequest.SizeCharacters, WindowInfoRequest.SizeCharacters);
        Assert.Equal(WindowInfoRequest.ScreenSizePixels, WindowInfoRequest.ScreenSizePixels);
        Assert.Equal(WindowInfoRequest.CellSizePixels, WindowInfoRequest.CellSizePixels);
        Assert.Equal(WindowInfoRequest.Title, WindowInfoRequest.Title);
        Assert.Equal(WindowInfoRequest.IconTitle, WindowInfoRequest.IconTitle);
        Assert.Equal(WindowInfoRequest.State, WindowInfoRequest.State);
    }

    // ===== New Request/Response Tests =====

    [Fact]
    public void WindowInfoRequest_StateQuery_SendsResponseWhenHandled()
    {
        // Arrange
        var windowOptions = new WindowOptions { GetWinState = true };
        var terminal = CreateTerminal(windowOptions);
        string? capturedResponse = null;

        terminal.WindowInfoRequested += (sender, e) =>
        {
            if (e.Request == WindowInfoRequest.State)
            {
                e.Handled = true;
                e.IsIconified = false; // Window is not minimized
            }
        };

        terminal.DataReceived += (sender, e) =>
        {
            capturedResponse = e.Data;
        };

        // Act
        terminal.Write("\x1b[11t"); // CSI 11 t - Query window state

        // Assert
        Assert.NotNull(capturedResponse);
        Assert.Equal("\u001b[1t", capturedResponse); // 1 = not iconified
    }

    [Fact]
    public void WindowInfoRequest_StateQuery_SendsIconifiedResponseWhenMinimized()
    {
        // Arrange
        var windowOptions = new WindowOptions { GetWinState = true };
        var terminal = CreateTerminal(windowOptions);
        string? capturedResponse = null;

        terminal.WindowInfoRequested += (sender, e) =>
        {
            if (e.Request == WindowInfoRequest.State)
            {
                e.Handled = true;
                e.IsIconified = true; // Window is minimized
            }
        };

        terminal.DataReceived += (sender, e) =>
        {
            capturedResponse = e.Data;
        };

        // Act
        terminal.Write("\x1b[11t");

        // Assert
        Assert.NotNull(capturedResponse);
        Assert.Equal("\u001b[2t", capturedResponse); // 2 = iconified
    }

    [Fact]
    public void WindowInfoRequest_StateQuery_NoResponseWhenNotHandled()
    {
        // Arrange
        var windowOptions = new WindowOptions { GetWinState = true };
        var terminal = CreateTerminal(windowOptions);
        string? capturedResponse = null;

        terminal.WindowInfoRequested += (sender, e) =>
        {
            // Don't set Handled = true
        };

        terminal.DataReceived += (sender, e) =>
        {
            capturedResponse = e.Data;
        };

        // Act
        terminal.Write("\x1b[11t");

        // Assert
        Assert.Null(capturedResponse); // No response when not handled
    }

    [Fact]
    public void WindowInfoRequest_PositionQuery_SendsPositionResponse()
    {
        // Arrange
        var windowOptions = new WindowOptions { GetWinPosition = true };
        var terminal = CreateTerminal(windowOptions);
        string? capturedResponse = null;

        terminal.WindowInfoRequested += (sender, e) =>
        {
            if (e.Request == WindowInfoRequest.Position)
            {
                e.Handled = true;
                e.X = 100;
                e.Y = 200;
            }
        };

        terminal.DataReceived += (sender, e) =>
        {
            capturedResponse = e.Data;
        };

        // Act
        terminal.Write("\x1b[13t"); // CSI 13 t - Query window position

        // Assert
        Assert.NotNull(capturedResponse);
        Assert.Equal("\u001b[3;100;200t", capturedResponse);
    }

    [Fact]
    public void WindowInfoRequest_SizePixelsQuery_SendsSizeResponse()
    {
        // Arrange
        var windowOptions = new WindowOptions { GetWinSizePixels = true };
        var terminal = CreateTerminal(windowOptions);
        string? capturedResponse = null;

        terminal.WindowInfoRequested += (sender, e) =>
        {
            if (e.Request == WindowInfoRequest.SizePixels)
            {
                e.Handled = true;
                e.WidthPixels = 800;
                e.HeightPixels = 600;
            }
        };

        terminal.DataReceived += (sender, e) =>
        {
            capturedResponse = e.Data;
        };

        // Act
        terminal.Write("\x1b[14t"); // CSI 14 t - Query window size in pixels

        // Assert
        Assert.NotNull(capturedResponse);
        Assert.Equal("\u001b[4;600;800t", capturedResponse); // Format: CSI 4 ; height ; width t
    }

    [Fact]
    public void WindowInfoRequest_ScreenSizePixelsQuery_SendsScreenSizeResponse()
    {
        // Arrange
        var windowOptions = new WindowOptions { GetScreenSizePixels = true };
        var terminal = CreateTerminal(windowOptions);
        string? capturedResponse = null;

        terminal.WindowInfoRequested += (sender, e) =>
        {
            if (e.Request == WindowInfoRequest.ScreenSizePixels)
            {
                e.Handled = true;
                e.WidthPixels = 1920;
                e.HeightPixels = 1080;
            }
        };

        terminal.DataReceived += (sender, e) =>
        {
            capturedResponse = e.Data;
        };

        // Act
        terminal.Write("\x1b[15t"); // CSI 15 t - Query screen size in pixels

        // Assert
        Assert.NotNull(capturedResponse);
        Assert.Equal("\u001b[5;1080;1920t", capturedResponse); // Format: CSI 5 ; height ; width t
    }

    [Fact]
    public void WindowInfoRequest_CellSizePixelsQuery_SendsCellSizeResponse()
    {
        // Arrange
        var windowOptions = new WindowOptions { GetCellSizePixels = true };
        var terminal = CreateTerminal(windowOptions);
        string? capturedResponse = null;

        terminal.WindowInfoRequested += (sender, e) =>
        {
            if (e.Request == WindowInfoRequest.CellSizePixels)
            {
                e.Handled = true;
                e.CellWidth = 8;
                e.CellHeight = 16;
            }
        };

        terminal.DataReceived += (sender, e) =>
        {
            capturedResponse = e.Data;
        };

        // Act
        terminal.Write("\x1b[16t"); // CSI 16 t - Query cell size in pixels

        // Assert
        Assert.NotNull(capturedResponse);
        Assert.Equal("\u001b[6;16;8t", capturedResponse); // Format: CSI 6 ; height ; width t
    }

    [Fact]
    public void WindowInfoRequest_IconTitleQuery_SendsTitleResponse()
    {
        // Arrange
        var windowOptions = new WindowOptions { GetIconTitle = true };
        var terminal = CreateTerminal(windowOptions);
        string? capturedResponse = null;

        terminal.WindowInfoRequested += (sender, e) =>
        {
            if (e.Request == WindowInfoRequest.IconTitle)
            {
                e.Handled = true;
                e.Title = "My Icon Title";
            }
        };

        terminal.DataReceived += (sender, e) =>
        {
            capturedResponse = e.Data;
        };

        // Act
        terminal.Write("\x1b[20t"); // CSI 20 t - Query icon title

        // Assert
        Assert.NotNull(capturedResponse);
        Assert.Equal("\u001b]LMy Icon Title\u0007", capturedResponse); // Format: OSC L title BEL
    }

    [Fact]
    public void WindowInfoRequest_IconTitleQuery_NoResponseWhenTitleIsNull()
    {
        // Arrange
        var windowOptions = new WindowOptions { GetIconTitle = true };
        var terminal = CreateTerminal(windowOptions);
        string? capturedResponse = null;

        terminal.WindowInfoRequested += (sender, e) =>
        {
            if (e.Request == WindowInfoRequest.IconTitle)
            {
                e.Handled = true;
                e.Title = null; // Explicitly null
            }
        };

        terminal.DataReceived += (sender, e) =>
        {
            capturedResponse = e.Data;
        };

        // Act
        terminal.Write("\x1b[20t");

        // Assert
        Assert.Null(capturedResponse); // No response when title is null
    }

    [Fact]
    public void WindowInfoRequest_TextAreaSizeQuery_SendsDirectResponse()
    {
        // Arrange - Text area size (18t) responds directly without event handler
        var windowOptions = new WindowOptions { GetWinSizeChars = true };
        var options = new TerminalOptions
        {
            Cols = 120,
            Rows = 40,
            WindowOptions = windowOptions
        };
        var terminal = new Terminal(options);
        string? capturedResponse = null;

        terminal.DataReceived += (sender, e) =>
        {
            capturedResponse = e.Data;
        };

        // Act
        terminal.Write("\x1b[18t"); // CSI 18 t - Query text area size in characters

        // Assert
        Assert.NotNull(capturedResponse);
        Assert.Equal("\u001b[8;40;120t", capturedResponse); // Format: CSI 8 ; rows ; cols t
    }

    [Fact]
    public void WindowInfoRequest_ScreenSizeCharsQuery_SendsDirectResponse()
    {
        // Arrange - Screen size in chars (19t) responds directly
        var windowOptions = new WindowOptions { GetScreenSizePixels = true };
        var options = new TerminalOptions
        {
            Cols = 80,
            Rows = 24,
            WindowOptions = windowOptions
        };
        var terminal = new Terminal(options);
        string? capturedResponse = null;

        terminal.DataReceived += (sender, e) =>
        {
            capturedResponse = e.Data;
        };

        // Act
        terminal.Write("\x1b[19t"); // CSI 19 t - Query screen size in characters

        // Assert
        Assert.NotNull(capturedResponse);
        Assert.Equal("\u001b[9;24;80t", capturedResponse); // Format: CSI 9 ; rows ; cols t
    }

    [Fact]
    public void WindowInfoRequest_TitleQuery_SendsDirectResponseFromTerminalTitle()
    {
        // Arrange - Window title (21t) uses terminal's current title
        var windowOptions = new WindowOptions { GetWinTitle = true };
        var terminal = CreateTerminal(windowOptions);
        terminal.Title = "Terminal Window Title";
        string? capturedResponse = null;

        terminal.DataReceived += (sender, e) =>
        {
            capturedResponse = e.Data;
        };

        // Act
        terminal.Write("\x1b[21t"); // CSI 21 t - Query window title

        // Assert
        Assert.NotNull(capturedResponse);
        Assert.Equal("\u001b]lTerminal Window Title\u0007", capturedResponse); // Format: OSC l title BEL
    }

    [Fact]
    public void WindowInfoRequest_EventArgsPropertiesInitializeCorrectly()
    {
        // Arrange
        var windowOptions = new WindowOptions { GetWinPosition = true };
        var terminal = CreateTerminal(windowOptions);
        Events.TerminalEvents.WindowInfoRequestedEventArgs? capturedArgs = null;

        terminal.WindowInfoRequested += (sender, e) =>
        {
            capturedArgs = e;
        };

        // Act
        terminal.Write("\x1b[13t");

        // Assert
        Assert.NotNull(capturedArgs);
        Assert.Equal(WindowInfoRequest.Position, capturedArgs.Request);
        Assert.False(capturedArgs.Handled); // Default is false
        Assert.Equal(0, capturedArgs.X); // Default is 0
        Assert.Equal(0, capturedArgs.Y);
        Assert.Equal(0, capturedArgs.WidthPixels);
        Assert.Equal(0, capturedArgs.HeightPixels);
        Assert.Equal(0, capturedArgs.CellWidth);
        Assert.Equal(0, capturedArgs.CellHeight);
        Assert.Null(capturedArgs.Title);
        Assert.False(capturedArgs.IsIconified);
    }

    [Fact]
    public void WindowInfoRequest_MultipleQueries_EachHandledIndependently()
    {
        // Arrange
        var windowOptions = new WindowOptions 
        { 
            GetWinState = true, 
            GetWinPosition = true,
            GetWinSizePixels = true
        };
        var terminal = CreateTerminal(windowOptions);
        var responses = new List<string>();

        terminal.WindowInfoRequested += (sender, e) =>
        {
            e.Handled = true;
            switch (e.Request)
            {
                case WindowInfoRequest.State:
                    e.IsIconified = false;
                    break;
                case WindowInfoRequest.Position:
                    e.X = 50;
                    e.Y = 75;
                    break;
                case WindowInfoRequest.SizePixels:
                    e.WidthPixels = 640;
                    e.HeightPixels = 480;
                    break;
            }
        };

        terminal.DataReceived += (sender, e) =>
        {
            responses.Add(e.Data);
        };

        // Act
        terminal.Write("\x1b[11t"); // State
        terminal.Write("\x1b[13t"); // Position
        terminal.Write("\x1b[14t"); // Size

        // Assert
        Assert.Equal(3, responses.Count);
        Assert.Equal("\u001b[1t", responses[0]); // Not iconified
        Assert.Equal("\u001b[3;50;75t", responses[1]); // Position
        Assert.Equal("\u001b[4;480;640t", responses[2]); // Size
    }

    [Fact]
    public void WindowManipulation_PermissionsRespected_ForAllOperations()
    {
        // Arrange
        var windowOptions = new WindowOptions(); // All permissions false by default
        var terminal = CreateTerminal(windowOptions);
        
        var eventCount = 0;
        terminal.WindowMoved += (sender, e) => eventCount++;
        terminal.WindowResized += (sender, e) => eventCount++;
        terminal.WindowMinimized += (sender, e) => eventCount++;
        terminal.WindowMaximized += (sender, e) => eventCount++;
        terminal.WindowRestored += (sender, e) => eventCount++;
        terminal.WindowRaised += (sender, e) => eventCount++;
        terminal.WindowLowered += (sender, e) => eventCount++;
        terminal.WindowRefreshed += (sender, e) => eventCount++;
        terminal.WindowFullscreened += (sender, e) => eventCount++;
        terminal.WindowInfoRequested += (sender, e) => eventCount++;

        // Act - Try all operations
        terminal.Write("\x1b[3;10;20t");  // Move
        terminal.Write("\x1b[4;600;800t"); // Resize
        terminal.Write("\x1b[2t");         // Minimize
        terminal.Write("\x1b[9;1t");       // Maximize
        terminal.Write("\x1b[1t");         // Restore
        terminal.Write("\x1b[5t");         // Raise
        terminal.Write("\x1b[6t");         // Lower
        terminal.Write("\x1b[7t");         // Refresh
        terminal.Write("\x1b[10;1t");      // Fullscreen
        terminal.Write("\x1b[11t");        // Query state

        // Assert - No events should fire because all permissions are false
        Assert.Equal(0, eventCount);
    }
}
