using XTerm;
using XTerm.Common;
using XTerm.Options;

namespace XTerm.Tests;

public class ModeHandlingTests
{
    private Terminal CreateTerminal(int cols = 80, int rows = 24)
    {
        var options = new TerminalOptions { Cols = cols, Rows = rows };
        return new Terminal(options);
    }

    [Fact]
    public void SetMode_InsertMode_EnablesInsertMode()
    {
        // Arrange
        var terminal = CreateTerminal();
        Assert.False(terminal.InsertMode);

        // Act
        terminal.Write($"\x1B[{(int)TerminalMode.InsertMode}h");

        // Assert
        Assert.True(terminal.InsertMode);
    }

    [Fact]
    public void ResetMode_InsertMode_DisablesInsertMode()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.InsertMode = true;

        // Act
        terminal.Write($"\x1B[{(int)TerminalMode.InsertMode}l");

        // Assert
        Assert.False(terminal.InsertMode);
    }

    [Fact]
    public void SetMode_ApplicationCursorKeys_EnablesAppCursorKeys()
    {
        // Arrange
        var terminal = CreateTerminal();
        Assert.False(terminal.ApplicationCursorKeys);

        // Act - DEC private mode
        terminal.Write($"\x1B[?{(int)TerminalMode.AppCursorKeys}h");

        // Assert
        Assert.True(terminal.ApplicationCursorKeys);
    }

    [Fact]
    public void ResetMode_ApplicationCursorKeys_DisablesAppCursorKeys()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.ApplicationCursorKeys = true;

        // Act - DEC private mode
        terminal.Write($"\x1B[?{(int)TerminalMode.AppCursorKeys}l");

        // Assert
        Assert.False(terminal.ApplicationCursorKeys);
    }

    [Fact]
    public void SetMode_ShowCursor_EnablesCursorVisibility()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.CursorVisible = false;

        // Act - DEC private mode
        terminal.Write($"\x1B[?{(int)TerminalMode.ShowCursor}h");

        // Assert
        Assert.True(terminal.CursorVisible);
    }

    [Fact]
    public void ResetMode_ShowCursor_DisablesCursorVisibility()
    {
        // Arrange
        var terminal = CreateTerminal();
        Assert.True(terminal.CursorVisible); // Default is true

        // Act - DEC private mode
        terminal.Write($"\x1B[?{(int)TerminalMode.ShowCursor}l");

        // Assert
        Assert.False(terminal.CursorVisible);
    }

    [Fact]
    public void SetMode_ApplicationKeypad_EnablesAppKeypad()
    {
        // Arrange
        var terminal = CreateTerminal();
        Assert.False(terminal.ApplicationKeypad);

        // Act - DEC private mode
        terminal.Write($"\x1B[?{(int)TerminalMode.AppKeypad}h");

        // Assert
        Assert.True(terminal.ApplicationKeypad);
    }

    [Fact]
    public void ResetMode_ApplicationKeypad_DisablesAppKeypad()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.ApplicationKeypad = true;

        // Act - DEC private mode
        terminal.Write($"\x1B[?{(int)TerminalMode.AppKeypad}l");

        // Assert
        Assert.False(terminal.ApplicationKeypad);
    }

    [Fact]
    public void SetMode_BracketedPasteMode_EnablesBracketedPaste()
    {
        // Arrange
        var terminal = CreateTerminal();
        Assert.False(terminal.BracketedPasteMode);

        // Act - DEC private mode
        terminal.Write($"\x1B[?{(int)TerminalMode.BracketedPasteMode}h");

        // Assert
        Assert.True(terminal.BracketedPasteMode);
    }

    [Fact]
    public void ResetMode_BracketedPasteMode_DisablesBracketedPaste()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.BracketedPasteMode = true;

        // Act - DEC private mode
        terminal.Write($"\x1B[?{(int)TerminalMode.BracketedPasteMode}l");

        // Assert
        Assert.False(terminal.BracketedPasteMode);
    }

    [Fact]
    public void SetMode_OriginMode_EnablesOriginMode()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.Buffer.SetCursor(10, 10);
        Assert.False(terminal.OriginMode);

        // Act - DEC private mode
        terminal.Write($"\x1B[?{(int)TerminalMode.Origin}h");

        // Assert
        Assert.True(terminal.OriginMode);
        // Cursor should be reset to 0,0
        Assert.Equal(0, terminal.Buffer.X);
        Assert.Equal(0, terminal.Buffer.Y);
    }

    [Fact]
    public void ResetMode_OriginMode_DisablesOriginMode()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.OriginMode = true;
        terminal.Buffer.SetCursor(5, 5);

        // Act - DEC private mode
        terminal.Write($"\x1B[?{(int)TerminalMode.Origin}l");

        // Assert
        Assert.False(terminal.OriginMode);
        // Cursor should be reset to 0,0
        Assert.Equal(0, terminal.Buffer.X);
        Assert.Equal(0, terminal.Buffer.Y);
    }

    [Fact]
    public void SetMode_AltBuffer_SwitchesToAltBuffer()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.Write("Normal buffer content");
        var normalBufferContent = terminal.GetLine(0);

        // Act - DEC private mode
        terminal.Write($"\x1B[?{(int)TerminalMode.AltBuffer}h");

        // Assert
        terminal.Write("Alt buffer content");
        var altBufferContent = terminal.GetLine(0);
        Assert.Contains("Alt buffer", altBufferContent);
        Assert.DoesNotContain("Normal buffer", altBufferContent);
    }

    [Fact]
    public void ResetMode_AltBuffer_SwitchesToNormalBuffer()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.Write("Normal content");
        terminal.Write($"\x1B[?{(int)TerminalMode.AltBuffer}h"); // Switch to alt
        terminal.Write("Alt content");

        // Act - DEC private mode
        terminal.Write($"\x1B[?{(int)TerminalMode.AltBuffer}l"); // Switch back

        // Assert
        var content = terminal.GetLine(0);
        Assert.Contains("Normal content", content);
    }

    [Fact]
    public void SetMode_AltBufferWithCursor_SavesCursor()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.Buffer.SetCursor(20, 10);

        // Act - DEC private mode (saves cursor and switches)
        terminal.Write($"\x1B[?{(int)TerminalMode.AltBufferCursor}h");

        // Assert
        // Should be in alt buffer
        terminal.Write("Test");
        var content = terminal.GetLine(0);
        Assert.Contains("Test", content);
    }

    [Fact]
    public void ResetMode_AltBufferWithCursor_RestoresCursor()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.Buffer.SetCursor(20, 10);
        var savedX = terminal.Buffer.X;
        var savedY = terminal.Buffer.Y;

        terminal.Write($"\x1B[?{(int)TerminalMode.AltBufferCursor}h"); // Save and switch
        terminal.Buffer.SetCursor(5, 5); // Move cursor in alt buffer

        // Act - DEC private mode (switches back and restores cursor)
        terminal.Write($"\x1B[?{(int)TerminalMode.AltBufferCursor}l");

        // Assert
        Assert.Equal(savedX, terminal.Buffer.X);
        Assert.Equal(savedY, terminal.Buffer.Y);
    }

    [Fact]
    public void SetMode_SendFocusEvents_EnablesFocusEvents()
    {
        // Arrange
        var terminal = CreateTerminal();
        Assert.False(terminal.SendFocusEvents);

        // Act - DEC private mode
        terminal.Write($"\x1B[?{(int)TerminalMode.SendFocusEvents}h");

        // Assert
        Assert.True(terminal.SendFocusEvents);
    }

    [Fact]
    public void ResetMode_SendFocusEvents_DisablesFocusEvents()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.SendFocusEvents = true;

        // Act - DEC private mode
        terminal.Write($"\x1B[?{(int)TerminalMode.SendFocusEvents}l");

        // Assert
        Assert.False(terminal.SendFocusEvents);
    }

    [Fact]
    public void SetMode_Wraparound_EnablesWraparound()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.Options.Wraparound = false;

        // Act - DEC private mode
        terminal.Write($"\x1B[?{(int)TerminalMode.Wraparound}h");

        // Assert
        Assert.True(terminal.Options.Wraparound);
    }

    [Fact]
    public void ResetMode_Wraparound_DisablesWraparound()
    {
        // Arrange
        var terminal = CreateTerminal();
        Assert.True(terminal.Options.Wraparound); // Default is true

        // Act - DEC private mode
        terminal.Write($"\x1B[?{(int)TerminalMode.Wraparound}l");

        // Assert
        Assert.False(terminal.Options.Wraparound);
    }

    [Fact]
    public void SetMode_MultipleModes_EnablesAll()
    {
        // Arrange
        var terminal = CreateTerminal();

        // Act - Set multiple modes at once
        terminal.Write($"\x1B[?{(int)TerminalMode.AppCursorKeys};{(int)TerminalMode.ShowCursor};{(int)TerminalMode.AppKeypad}h");

        // Assert
        Assert.True(terminal.ApplicationCursorKeys);
        Assert.True(terminal.CursorVisible);
        Assert.True(terminal.ApplicationKeypad);
    }

    [Fact]
    public void ResetMode_MultipleModes_DisablesAll()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.ApplicationCursorKeys = true;
        terminal.CursorVisible = true;
        terminal.ApplicationKeypad = true;

        // Act - Reset multiple modes at once
        terminal.Write($"\x1B[?{(int)TerminalMode.AppCursorKeys};{(int)TerminalMode.ShowCursor};{(int)TerminalMode.AppKeypad}l");

        // Assert
        Assert.False(terminal.ApplicationCursorKeys);
        Assert.False(terminal.CursorVisible);
        Assert.False(terminal.ApplicationKeypad);
    }

    [Fact]
    public void TerminalReset_ResetsAllModes()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.InsertMode = true;
        terminal.ApplicationCursorKeys = true;
        terminal.ApplicationKeypad = true;
        terminal.BracketedPasteMode = true;
        terminal.OriginMode = true;
        terminal.CursorVisible = false;
        terminal.SendFocusEvents = true;
        terminal.Win32InputMode = true;
        terminal.MetaSendsEscape = true;
        terminal.AltSendsEscape = true;

        // Act
        terminal.Reset();

        // Assert
        Assert.False(terminal.InsertMode);
        Assert.False(terminal.ApplicationCursorKeys);
        Assert.False(terminal.ApplicationKeypad);
        Assert.False(terminal.BracketedPasteMode);
        Assert.False(terminal.OriginMode);
        Assert.True(terminal.CursorVisible); // Default is true
        Assert.False(terminal.SendFocusEvents);
        Assert.False(terminal.Win32InputMode);
        Assert.False(terminal.MetaSendsEscape);
        Assert.False(terminal.AltSendsEscape);
    }

    #region Win32InputMode Tests

    [Fact]
    public void SetMode_Win32InputMode_EnablesWin32InputMode()
    {
        // Arrange
        var terminal = CreateTerminal();
        Assert.False(terminal.Win32InputMode);

        // Act - DEC private mode 9001
        terminal.Write($"\x1B[?{(int)TerminalMode.Win32InputMode}h");

        // Assert
        Assert.True(terminal.Win32InputMode);
    }

    [Fact]
    public void ResetMode_Win32InputMode_DisablesWin32InputMode()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.Win32InputMode = true;

        // Act - DEC private mode 9001
        terminal.Write($"\x1B[?{(int)TerminalMode.Win32InputMode}l");

        // Assert
        Assert.False(terminal.Win32InputMode);
    }

    [Fact]
    public void SetMode_Win32InputMode_DisablesMetaSendsEscape()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.MetaSendsEscape = true;
        Assert.True(terminal.MetaSendsEscape);

        // Act - Enable Win32InputMode
        terminal.Write($"\x1B[?{(int)TerminalMode.Win32InputMode}h");

        // Assert - MetaSendsEscape should be disabled
        Assert.True(terminal.Win32InputMode);
        Assert.False(terminal.MetaSendsEscape);
    }

    [Fact]
    public void SetMode_Win32InputMode_DisablesAltSendsEscape()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.AltSendsEscape = true;
        Assert.True(terminal.AltSendsEscape);

        // Act - Enable Win32InputMode
        terminal.Write($"\x1B[?{(int)TerminalMode.Win32InputMode}h");

        // Assert - AltSendsEscape should be disabled
        Assert.True(terminal.Win32InputMode);
        Assert.False(terminal.AltSendsEscape);
    }

    [Fact]
    public void SetMode_Win32InputMode_DisablesBothEscapeModes()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.MetaSendsEscape = true;
        terminal.AltSendsEscape = true;

        // Act - Enable Win32InputMode
        terminal.Write($"\x1B[?{(int)TerminalMode.Win32InputMode}h");

        // Assert - Both escape modes should be disabled
        Assert.True(terminal.Win32InputMode);
        Assert.False(terminal.MetaSendsEscape);
        Assert.False(terminal.AltSendsEscape);
    }

    #endregion

    #region MetaSendsEscape Tests

    [Fact]
    public void SetMode_MetaSendsEscape_EnablesMetaSendsEscape()
    {
        // Arrange
        var terminal = CreateTerminal();
        Assert.False(terminal.MetaSendsEscape);

        // Act - DEC private mode 1036
        terminal.Write($"\x1B[?{(int)TerminalMode.MetaSendsEscape}h");

        // Assert
        Assert.True(terminal.MetaSendsEscape);
    }

    [Fact]
    public void ResetMode_MetaSendsEscape_DisablesMetaSendsEscape()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.MetaSendsEscape = true;

        // Act - DEC private mode 1036
        terminal.Write($"\x1B[?{(int)TerminalMode.MetaSendsEscape}l");

        // Assert
        Assert.False(terminal.MetaSendsEscape);
    }

    [Fact]
    public void SetMode_MetaSendsEscape_DisablesWin32InputMode()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.Win32InputMode = true;
        Assert.True(terminal.Win32InputMode);

        // Act - Enable MetaSendsEscape
        terminal.Write($"\x1B[?{(int)TerminalMode.MetaSendsEscape}h");

        // Assert - Win32InputMode should be disabled
        Assert.True(terminal.MetaSendsEscape);
        Assert.False(terminal.Win32InputMode);
    }

    #endregion

    #region AltSendsEscape Tests

    [Fact]
    public void SetMode_AltSendsEscape_EnablesAltSendsEscape()
    {
        // Arrange
        var terminal = CreateTerminal();
        Assert.False(terminal.AltSendsEscape);

        // Act - DEC private mode 1039
        terminal.Write($"\x1B[?{(int)TerminalMode.AltSendsEscape}h");

        // Assert
        Assert.True(terminal.AltSendsEscape);
    }

    [Fact]
    public void ResetMode_AltSendsEscape_DisablesAltSendsEscape()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.AltSendsEscape = true;

        // Act - DEC private mode 1039
        terminal.Write($"\x1B[?{(int)TerminalMode.AltSendsEscape}l");

        // Assert
        Assert.False(terminal.AltSendsEscape);
    }

    [Fact]
    public void SetMode_AltSendsEscape_DisablesWin32InputMode()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.Win32InputMode = true;
        Assert.True(terminal.Win32InputMode);

        // Act - Enable AltSendsEscape
        terminal.Write($"\x1B[?{(int)TerminalMode.AltSendsEscape}h");

        // Assert - Win32InputMode should be disabled
        Assert.True(terminal.AltSendsEscape);
        Assert.False(terminal.Win32InputMode);
    }

    #endregion

    #region Mode Switching Scenarios

    [Fact]
    public void ModeSwitching_Win32ToMeta_SwitchesCorrectly()
    {
        // Arrange - Start with Win32InputMode enabled (like cmd.exe)
        var terminal = CreateTerminal();
        terminal.Write($"\x1B[?{(int)TerminalMode.Win32InputMode}h");
        Assert.True(terminal.Win32InputMode);

        // Act - Switch to MetaSendsEscape (like EDIT does)
        terminal.Write($"\x1B[?{(int)TerminalMode.MetaSendsEscape}h");

        // Assert
        Assert.False(terminal.Win32InputMode);
        Assert.True(terminal.MetaSendsEscape);
    }

    [Fact]
    public void ModeSwitching_MetaToWin32_SwitchesCorrectly()
    {
        // Arrange - Start with MetaSendsEscape enabled
        var terminal = CreateTerminal();
        terminal.Write($"\x1B[?{(int)TerminalMode.MetaSendsEscape}h");
        Assert.True(terminal.MetaSendsEscape);

        // Act - Switch back to Win32InputMode (like when EDIT exits and cmd.exe regains control)
        terminal.Write($"\x1B[?{(int)TerminalMode.Win32InputMode}h");

        // Assert
        Assert.True(terminal.Win32InputMode);
        Assert.False(terminal.MetaSendsEscape);
    }

    [Fact]
    public void ModeSwitching_DisableMetaThenEnableWin32_WorksCorrectly()
    {
        // Arrange - Start with MetaSendsEscape enabled
        var terminal = CreateTerminal();
        terminal.Write($"\x1B[?{(int)TerminalMode.MetaSendsEscape}h");
        Assert.True(terminal.MetaSendsEscape);

        // Act - First disable Meta, then enable Win32 (explicit cleanup scenario)
        terminal.Write($"\x1B[?{(int)TerminalMode.MetaSendsEscape}l");
        terminal.Write($"\x1B[?{(int)TerminalMode.Win32InputMode}h");

        // Assert
        Assert.True(terminal.Win32InputMode);
        Assert.False(terminal.MetaSendsEscape);
    }

    [Fact]
    public void ModeSwitching_ChildProcessScenario_CmdToEditAndBack()
    {
        // This simulates: cmd.exe -> user runs EDIT -> EDIT exits -> back to cmd.exe
        var terminal = CreateTerminal();

        // Step 1: cmd.exe starts and enables Win32InputMode
        terminal.Write($"\x1B[?{(int)TerminalMode.Win32InputMode}h");
        Assert.True(terminal.Win32InputMode);
        Assert.False(terminal.MetaSendsEscape);

        // Step 2: User runs EDIT, which enables MetaSendsEscape
        terminal.Write($"\x1B[?{(int)TerminalMode.MetaSendsEscape}h");
        Assert.False(terminal.Win32InputMode);
        Assert.True(terminal.MetaSendsEscape);

        // Step 3: User exits EDIT, which disables MetaSendsEscape
        terminal.Write($"\x1B[?{(int)TerminalMode.MetaSendsEscape}l");
        Assert.False(terminal.Win32InputMode);
        Assert.False(terminal.MetaSendsEscape);

        // Step 4: cmd.exe regains control and re-enables Win32InputMode
        terminal.Write($"\x1B[?{(int)TerminalMode.Win32InputMode}h");
        Assert.True(terminal.Win32InputMode);
        Assert.False(terminal.MetaSendsEscape);
    }

    [Fact]
    public void ModeSwitching_MultipleApps_ComplexScenario()
    {
        // Simulates: cmd.exe -> FAR Manager -> vim (nested child processes)
        var terminal = CreateTerminal();

        // cmd.exe starts
        terminal.Write($"\x1B[?{(int)TerminalMode.Win32InputMode}h");
        Assert.True(terminal.Win32InputMode);

        // FAR Manager starts (also uses Win32, re-asserts mode)
        terminal.Write($"\x1B[?{(int)TerminalMode.Win32InputMode}h");
        Assert.True(terminal.Win32InputMode);

        // vim starts from FAR, uses AltSendsEscape
        terminal.Write($"\x1B[?{(int)TerminalMode.AltSendsEscape}h");
        Assert.False(terminal.Win32InputMode);
        Assert.True(terminal.AltSendsEscape);

        // vim exits
        terminal.Write($"\x1B[?{(int)TerminalMode.AltSendsEscape}l");
        Assert.False(terminal.AltSendsEscape);

        // FAR re-enables Win32
        terminal.Write($"\x1B[?{(int)TerminalMode.Win32InputMode}h");
        Assert.True(terminal.Win32InputMode);

        // FAR exits, cmd.exe re-enables Win32
        terminal.Write($"\x1B[?{(int)TerminalMode.Win32InputMode}h");
        Assert.True(terminal.Win32InputMode);
    }

    #endregion

    #region Default Values Tests

    [Fact]
    public void DefaultValues_Win32InputMode_IsFalse()
    {
        var terminal = CreateTerminal();
        Assert.False(terminal.Win32InputMode);
    }

    [Fact]
    public void DefaultValues_MetaSendsEscape_IsFalse()
    {
        var terminal = CreateTerminal();
        Assert.False(terminal.MetaSendsEscape);
    }

    [Fact]
    public void DefaultValues_AltSendsEscape_IsFalse()
    {
        var terminal = CreateTerminal();
        Assert.False(terminal.AltSendsEscape);
    }

    #endregion
}
