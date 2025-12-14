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
        Assert.NotNull(terminal.OnWindowMove);
        Assert.NotNull(terminal.OnWindowResize);
        Assert.NotNull(terminal.OnWindowMinimize);
        Assert.NotNull(terminal.OnWindowMaximize);
        Assert.NotNull(terminal.OnWindowRestore);
        Assert.NotNull(terminal.OnWindowRaise);
        Assert.NotNull(terminal.OnWindowLower);
        Assert.NotNull(terminal.OnWindowRefresh);
        Assert.NotNull(terminal.OnWindowFullscreen);
        Assert.NotNull(terminal.OnWindowInfoRequest);
    }

    [Fact]
    public void WindowManipulation_MoveWindow_FiresOnWindowMove()
    {
        // Arrange
        var windowOptions = new WindowOptions { SetWinPosition = true };
        var terminal = CreateTerminal(windowOptions);
        var moveEventFired = false;
        int capturedX = 0, capturedY = 0;

        terminal.OnWindowMove.Event(pos =>
        {
            moveEventFired = true;
            capturedX = pos.x;
            capturedY = pos.y;
        });

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

        terminal.OnWindowMove.Event(pos => moveEventFired = true);

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

        terminal.OnWindowResize.Event(size =>
        {
            resizeEventFired = true;
            capturedWidth = size.width;
            capturedHeight = size.height;
        });

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

        terminal.OnWindowMinimize.Event(() => eventFired = true);

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

        terminal.OnWindowMaximize.Event(() => eventFired = true);

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

        terminal.OnWindowRestore.Event(() => eventFired = true);

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

        terminal.OnWindowRestore.Event(() => eventFired = true);

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

        terminal.OnWindowRaise.Event(() => eventFired = true);

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

        terminal.OnWindowLower.Event(() => eventFired = true);

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

        terminal.OnWindowRefresh.Event(() => eventFired = true);

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

        terminal.OnWindowFullscreen.Event(() => eventCount++);

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

        terminal.OnWindowInfoRequest.Event(request =>
        {
            requestReceived = true;
            capturedRequest = request;
        });

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

        terminal.OnWindowInfoRequest.Event(request =>
        {
            requestReceived = true;
            capturedRequest = request;
        });

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

        terminal.OnWindowInfoRequest.Event(request =>
        {
            requestReceived = true;
            capturedRequest = request;
        });

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

        terminal.OnWindowInfoRequest.Event(request =>
        {
            requestReceived = true;
        });

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

        terminal.OnWindowInfoRequest.Event(request =>
        {
            requestReceived = true;
        });

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

        terminal.OnData.Event(data =>
        {
            responseReceived = true;
            capturedResponse = data;
        });

        // Act
        terminal.Write("\x1b[18t"); // CSI 18 t

        // Assert
        Assert.True(responseReceived);
        Assert.Contains($"{terminal.Rows}", capturedResponse);
        Assert.Contains($"{terminal.Cols}", capturedResponse);
        Assert.Contains("\u001b[8;", capturedResponse);
    }

    [Fact]
    public void WindowManipulation_QueryWindowTitle_FiresOnWindowInfoRequest()
    {
        // Arrange
        var windowOptions = new WindowOptions { GetWinTitle = true };
        var terminal = CreateTerminal(windowOptions);
        terminal.Title = "Test Title";
        
        var requestReceived = false;
        var responseReceived = false;
        string capturedResponse = string.Empty;

        terminal.OnWindowInfoRequest.Event(request =>
        {
            requestReceived = true;
        });

        terminal.OnData.Event(data =>
        {
            responseReceived = true;
            capturedResponse = data;
        });

        // Act
        terminal.Write("\x1b[21t"); // CSI 21 t

        // Assert
        Assert.True(requestReceived);
        Assert.True(responseReceived);
        Assert.Contains("Test Title", capturedResponse);
    }

    [Fact]
    public void WindowManipulation_QueryIconTitle_FiresOnWindowInfoRequest()
    {
        // Arrange
        var windowOptions = new WindowOptions { GetIconTitle = true };
        var terminal = CreateTerminal(windowOptions);
        var requestReceived = false;

        terminal.OnWindowInfoRequest.Event(request =>
        {
            requestReceived = true;
        });

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

        terminal.OnWindowMove.Event(pos => moveCount++);
        terminal.OnWindowMinimize.Event(() => minimizeCount++);
        terminal.OnWindowMaximize.Event(() => maximizeCount++);
        terminal.OnWindowRaise.Event(() => raiseCount++);

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

        terminal.OnWindowMove.Event(pos => eventFired = true);

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
        
        terminal.OnWindowMinimize.Event(() => eventCount++);

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

    [Fact]
    public void WindowManipulation_PermissionsRespected_ForAllOperations()
    {
        // Arrange
        var windowOptions = new WindowOptions(); // All permissions false by default
        var terminal = CreateTerminal(windowOptions);
        
        var eventCount = 0;
        terminal.OnWindowMove.Event(pos => eventCount++);
        terminal.OnWindowResize.Event(size => eventCount++);
        terminal.OnWindowMinimize.Event(() => eventCount++);
        terminal.OnWindowMaximize.Event(() => eventCount++);
        terminal.OnWindowRestore.Event(() => eventCount++);
        terminal.OnWindowRaise.Event(() => eventCount++);
        terminal.OnWindowLower.Event(() => eventCount++);
        terminal.OnWindowRefresh.Event(() => eventCount++);
        terminal.OnWindowFullscreen.Event(() => eventCount++);
        terminal.OnWindowInfoRequest.Event(req => eventCount++);

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
