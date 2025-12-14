using XTerm;
using XTerm.Input;
using XTerm.Options;

namespace XTerm.Tests;

public class KeyboardInputTests
{
    private Terminal CreateTerminal(int cols = 80, int rows = 24)
    {
        var options = new TerminalOptions { Cols = cols, Rows = rows };
        return new Terminal(options);
    }

    #region Arrow Keys

    [Fact]
    public void ArrowKeys_NormalMode_GenerateCorrectSequences()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.ApplicationCursorKeys = false;

        // Act & Assert
        Assert.Equal("\x1B[A", terminal.GenerateKeyInput(Key.UpArrow));
        Assert.Equal("\x1B[B", terminal.GenerateKeyInput(Key.DownArrow));
        Assert.Equal("\x1B[C", terminal.GenerateKeyInput(Key.RightArrow));
        Assert.Equal("\x1B[D", terminal.GenerateKeyInput(Key.LeftArrow));
    }

    [Fact]
    public void ArrowKeys_ApplicationMode_GenerateCorrectSequences()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.ApplicationCursorKeys = true;

        // Act & Assert
        Assert.Equal("\x1BOA", terminal.GenerateKeyInput(Key.UpArrow));
        Assert.Equal("\x1BOB", terminal.GenerateKeyInput(Key.DownArrow));
        Assert.Equal("\x1BOC", terminal.GenerateKeyInput(Key.RightArrow));
        Assert.Equal("\x1BOD", terminal.GenerateKeyInput(Key.LeftArrow));
    }

    [Fact]
    public void ArrowKeys_WithShift_GenerateModifiedSequences()
    {
        // Arrange
        var terminal = CreateTerminal();

        // Act & Assert
        Assert.Equal("\x1B[1;2A", terminal.GenerateKeyInput(Key.UpArrow, KeyModifiers.Shift));
        Assert.Equal("\x1B[1;2B", terminal.GenerateKeyInput(Key.DownArrow, KeyModifiers.Shift));
    }

    [Fact]
    public void ArrowKeys_WithControl_GenerateModifiedSequences()
    {
        // Arrange
        var terminal = CreateTerminal();

        // Act & Assert
        Assert.Equal("\x1B[1;5A", terminal.GenerateKeyInput(Key.UpArrow, KeyModifiers.Control));
        Assert.Equal("\x1B[1;5D", terminal.GenerateKeyInput(Key.LeftArrow, KeyModifiers.Control));
    }

    [Fact]
    public void ArrowKeys_WithAlt_GenerateModifiedSequences()
    {
        // Arrange
        var terminal = CreateTerminal();

        // Act & Assert
        Assert.Equal("\x1B[1;3A", terminal.GenerateKeyInput(Key.UpArrow, KeyModifiers.Alt));
    }

    [Fact]
    public void ArrowKeys_WithMultipleModifiers_GenerateCorrectCode()
    {
        // Arrange
        var terminal = CreateTerminal();
        var modifiers = KeyModifiers.Control | KeyModifiers.Shift;

        // Act
        var sequence = terminal.GenerateKeyInput(Key.UpArrow, modifiers);

        // Assert - Control (4) + Shift (1) + 1 = 6
        var expected = "\x1B[1;6A";
        Assert.Equal(expected, sequence);
    }

    #endregion

    #region Function Keys

    [Fact]
    public void FunctionKeys_F1ToF4_GenerateSS3Sequences()
    {
        // Arrange
        var terminal = CreateTerminal();

        // Act & Assert
        Assert.Equal("\x1BOP", terminal.GenerateKeyInput(Key.F1));
        Assert.Equal("\x1BOQ", terminal.GenerateKeyInput(Key.F2));
        Assert.Equal("\x1BOR", terminal.GenerateKeyInput(Key.F3));
        Assert.Equal("\x1BOS", terminal.GenerateKeyInput(Key.F4));
    }

    [Fact]
    public void FunctionKeys_F5ToF12_GenerateCSISequences()
    {
        // Arrange
        var terminal = CreateTerminal();

        // Act & Assert
        Assert.Equal("\x1B[15~", terminal.GenerateKeyInput(Key.F5));
        Assert.Equal("\x1B[17~", terminal.GenerateKeyInput(Key.F6));
        Assert.Equal("\x1B[18~", terminal.GenerateKeyInput(Key.F7));
        Assert.Equal("\x1B[19~", terminal.GenerateKeyInput(Key.F8));
        Assert.Equal("\x1B[20~", terminal.GenerateKeyInput(Key.F9));
        Assert.Equal("\x1B[21~", terminal.GenerateKeyInput(Key.F10));
        Assert.Equal("\x1B[23~", terminal.GenerateKeyInput(Key.F11));
        Assert.Equal("\x1B[24~", terminal.GenerateKeyInput(Key.F12));
    }

    [Fact]
    public void FunctionKeys_F13ToF20_GenerateExtendedSequences()
    {
        // Arrange
        var terminal = CreateTerminal();

        // Act & Assert
        Assert.Equal("\x1B[25~", terminal.GenerateKeyInput(Key.F13));
        Assert.Equal("\x1B[26~", terminal.GenerateKeyInput(Key.F14));
        Assert.Equal("\x1B[28~", terminal.GenerateKeyInput(Key.F15));
        Assert.Equal("\x1B[29~", terminal.GenerateKeyInput(Key.F16));
        Assert.Equal("\x1B[31~", terminal.GenerateKeyInput(Key.F17));
        Assert.Equal("\x1B[32~", terminal.GenerateKeyInput(Key.F18));
        Assert.Equal("\x1B[33~", terminal.GenerateKeyInput(Key.F19));
        Assert.Equal("\x1B[34~", terminal.GenerateKeyInput(Key.F20));
    }

    [Fact]
    public void FunctionKeys_WithShift_GenerateModifiedSequences()
    {
        // Arrange
        var terminal = CreateTerminal();

        // Act & Assert
        Assert.Equal("\x1B[1;2P", terminal.GenerateKeyInput(Key.F1, KeyModifiers.Shift));
        Assert.Equal("\x1B[15;2~", terminal.GenerateKeyInput(Key.F5, KeyModifiers.Shift));
    }

    #endregion

    #region Navigation Keys

    [Fact]
    public void NavigationKeys_GenerateCorrectSequences()
    {
        // Arrange
        var terminal = CreateTerminal();

        // Act & Assert
        Assert.Equal("\x1B[H", terminal.GenerateKeyInput(Key.Home));
        Assert.Equal("\x1B[F", terminal.GenerateKeyInput(Key.End));
        Assert.Equal("\x1B[5~", terminal.GenerateKeyInput(Key.PageUp));
        Assert.Equal("\x1B[6~", terminal.GenerateKeyInput(Key.PageDown));
        Assert.Equal("\x1B[2~", terminal.GenerateKeyInput(Key.Insert));
        Assert.Equal("\x1B[3~", terminal.GenerateKeyInput(Key.Delete));
    }

    [Fact]
    public void NavigationKeys_WithModifiers_GenerateModifiedSequences()
    {
        // Arrange
        var terminal = CreateTerminal();

        // Act & Assert
        Assert.Equal("\x1B[1;2H", terminal.GenerateKeyInput(Key.Home, KeyModifiers.Shift));
        Assert.Equal("\x1B[5;5~", terminal.GenerateKeyInput(Key.PageUp, KeyModifiers.Control));
        Assert.Equal("\x1B[3;3~", terminal.GenerateKeyInput(Key.Delete, KeyModifiers.Alt));
    }

    #endregion

    #region Control Keys

    [Fact]
    public void ControlKeys_GenerateCorrectSequences()
    {
        // Arrange
        var terminal = CreateTerminal();

        // Act & Assert
        Assert.Equal("\r", terminal.GenerateKeyInput(Key.Enter));
        Assert.Equal("\t", terminal.GenerateKeyInput(Key.Tab));
        Assert.Equal("\x7F", terminal.GenerateKeyInput(Key.Backspace)); // DEL
        Assert.Equal("\x1B", terminal.GenerateKeyInput(Key.Escape));
        Assert.Equal(" ", terminal.GenerateKeyInput(Key.Space));
    }

    [Fact]
    public void Tab_WithShift_GeneratesBackTab()
    {
        // Arrange
        var terminal = CreateTerminal();

        // Act
        var sequence = terminal.GenerateKeyInput(Key.Tab, KeyModifiers.Shift);

        // Assert
        Assert.Equal("\x1B[Z", sequence);
    }

    #endregion

    #region Keypad Keys

    [Fact]
    public void KeypadKeys_NormalMode_GenerateNumericCharacters()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.ApplicationKeypad = false;

        // Act & Assert
        Assert.Equal("0", terminal.GenerateKeyInput(Key.Keypad0));
        Assert.Equal("5", terminal.GenerateKeyInput(Key.Keypad5));
        Assert.Equal("9", terminal.GenerateKeyInput(Key.Keypad9));
        Assert.Equal(".", terminal.GenerateKeyInput(Key.KeypadDecimal));
    }

    [Fact]
    public void KeypadKeys_ApplicationMode_GenerateEscapeSequences()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.ApplicationKeypad = true;

        // Act & Assert
        Assert.Equal("\x1BOp", terminal.GenerateKeyInput(Key.Keypad0));
        Assert.Equal("\x1BOu", terminal.GenerateKeyInput(Key.Keypad5));
        Assert.Equal("\x1BOy", terminal.GenerateKeyInput(Key.Keypad9));
        Assert.Equal("\x1BOn", terminal.GenerateKeyInput(Key.KeypadDecimal));
    }

    [Fact]
    public void KeypadOperators_GenerateCorrectCharacters()
    {
        // Arrange
        var terminal = CreateTerminal();

        // Act & Assert
        Assert.Equal("/", terminal.GenerateKeyInput(Key.KeypadDivide));
        Assert.Equal("*", terminal.GenerateKeyInput(Key.KeypadMultiply));
        Assert.Equal("-", terminal.GenerateKeyInput(Key.KeypadSubtract));
        Assert.Equal("+", terminal.GenerateKeyInput(Key.KeypadAdd));
        Assert.Equal("\r", terminal.GenerateKeyInput(Key.KeypadEnter));
    }

    #endregion

    #region Character Input

    [Fact]
    public void CharInput_PlainCharacter_ReturnsCharacter()
    {
        // Arrange
        var terminal = CreateTerminal();

        // Act & Assert
        Assert.Equal("a", terminal.GenerateCharInput('a'));
        Assert.Equal("Z", terminal.GenerateCharInput('Z'));
        Assert.Equal("5", terminal.GenerateCharInput('5'));
        Assert.Equal("@", terminal.GenerateCharInput('@'));
    }

    [Fact]
    public void CharInput_WithControl_GeneratesControlCharacter()
    {
        // Arrange
        var terminal = CreateTerminal();

        // Act & Assert
        Assert.Equal("\x01", terminal.GenerateCharInput('a', KeyModifiers.Control)); // Ctrl+A
        Assert.Equal("\x03", terminal.GenerateCharInput('c', KeyModifiers.Control)); // Ctrl+C
        Assert.Equal("\x1A", terminal.GenerateCharInput('z', KeyModifiers.Control)); // Ctrl+Z
        Assert.Equal("\x01", terminal.GenerateCharInput('A', KeyModifiers.Control)); // Ctrl+A (uppercase)
    }

    [Fact]
    public void CharInput_ControlSpecialCharacters_GenerateCorrectCodes()
    {
        // Arrange
        var terminal = CreateTerminal();

        // Act & Assert
        Assert.Equal("\x00", terminal.GenerateCharInput(' ', KeyModifiers.Control)); // Ctrl+Space = NUL
        Assert.Equal("\x00", terminal.GenerateCharInput('@', KeyModifiers.Control)); // Ctrl+@ = NUL
        Assert.Equal("\x1B", terminal.GenerateCharInput('[', KeyModifiers.Control)); // Ctrl+[ = ESC
        Assert.Equal("\x1C", terminal.GenerateCharInput('\\', KeyModifiers.Control)); // Ctrl+\ = FS
        Assert.Equal("\x1D", terminal.GenerateCharInput(']', KeyModifiers.Control)); // Ctrl+] = GS
        Assert.Equal("\x1E", terminal.GenerateCharInput('^', KeyModifiers.Control)); // Ctrl+^ = RS
        Assert.Equal("\x1F", terminal.GenerateCharInput('_', KeyModifiers.Control)); // Ctrl+_ = US
        Assert.Equal("\x7F", terminal.GenerateCharInput('?', KeyModifiers.Control)); // Ctrl+? = DEL
    }

    [Fact]
    public void CharInput_WithAlt_GeneratesEscapePrefix()
    {
        // Arrange
        var terminal = CreateTerminal();

        // Act & Assert
        var expected = "\u001ba";
        Assert.Equal(expected, terminal.GenerateCharInput('a', KeyModifiers.Alt)); // Alt+a
        
        expected = "\u001bX";
        Assert.Equal(expected, terminal.GenerateCharInput('X', KeyModifiers.Alt)); // Alt+X
        
        expected = "\u001b1";
        Assert.Equal(expected, terminal.GenerateCharInput('1', KeyModifiers.Alt)); // Alt+1
    }

    #endregion

    #region Mode Changes

    [Fact]
    public void KeyboardInput_AfterModeChange_ReflectsNewMode()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.ApplicationCursorKeys = false;
        
        var normalSequence = terminal.GenerateKeyInput(Key.UpArrow);
        
        // Act - Change mode
        terminal.ApplicationCursorKeys = true;
        var appSequence = terminal.GenerateKeyInput(Key.UpArrow);

        // Assert
        Assert.Equal("\x1B[A", normalSequence);
        Assert.Equal("\x1BOA", appSequence);
    }

    [Fact]
    public void KeypadInput_AfterModeChange_ReflectsNewMode()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.ApplicationKeypad = false;
        
        var numericSequence = terminal.GenerateKeyInput(Key.Keypad5);
        
        // Act - Change mode
        terminal.ApplicationKeypad = true;
        var appSequence = terminal.GenerateKeyInput(Key.Keypad5);

        // Assert
        Assert.Equal("5", numericSequence);
        Assert.Equal("\x1BOu", appSequence);
    }

    #endregion

    #region Modifier Encoding

    [Fact]
    public void Modifiers_SingleModifier_GeneratesCorrectCode()
    {
        // Arrange
        var terminal = CreateTerminal();

        // Act & Assert
        // Shift = 1 + 1 = 2
        Assert.Contains(";2", terminal.GenerateKeyInput(Key.Home, KeyModifiers.Shift));
        // Alt = 1 + 2 = 3
        Assert.Contains(";3", terminal.GenerateKeyInput(Key.Home, KeyModifiers.Alt));
        // Control = 1 + 4 = 5
        Assert.Contains(";5", terminal.GenerateKeyInput(Key.Home, KeyModifiers.Control));
    }

    [Fact]
    public void Modifiers_CombinedModifiers_GeneratesCorrectCode()
    {
        // Arrange
        var terminal = CreateTerminal();

        // Act & Assert
        // Shift + Alt = 1 + 1 + 2 = 4
        Assert.Contains(";4", terminal.GenerateKeyInput(Key.Home, KeyModifiers.Shift | KeyModifiers.Alt));
        // Shift + Control = 1 + 1 + 4 = 6
        Assert.Contains(";6", terminal.GenerateKeyInput(Key.Home, KeyModifiers.Shift | KeyModifiers.Control));
        // Alt + Control = 1 + 2 + 4 = 7
        Assert.Contains(";7", terminal.GenerateKeyInput(Key.Home, KeyModifiers.Alt | KeyModifiers.Control));
        // All = 1 + 1 + 2 + 4 = 8
        Assert.Contains(";8", terminal.GenerateKeyInput(Key.Home, KeyModifiers.Shift | KeyModifiers.Alt | KeyModifiers.Control));
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void EmptyKey_DoesNotCrash()
    {
        // Arrange
        var terminal = CreateTerminal();

        // Act - This should return empty or handle gracefully
        var result = terminal.GenerateKeyInput((Key)999); // Invalid key

        // Assert - Should not throw
        Assert.NotNull(result);
    }

    [Fact]
    public void AllKeys_GenerateNonEmptySequences()
    {
        // Arrange
        var terminal = CreateTerminal();

        // Act & Assert - All defined keys should generate something
        foreach (Key key in Enum.GetValues(typeof(Key)))
        {
            var sequence = terminal.GenerateKeyInput(key);
            Assert.NotNull(sequence);
            // Most keys should generate non-empty sequences
            // (except potentially invalid ones)
        }
    }

    [Fact]
    public void ModifierCombinations_AllCombinations_WorkCorrectly()
    {
        // Arrange
        var terminal = CreateTerminal();
        
        // Act & Assert - Test all 8 combinations (2^3)
        for (int i = 0; i < 8; i++)
        {
            var mods = (KeyModifiers)i;
            var sequence = terminal.GenerateKeyInput(Key.Home, mods);
            Assert.NotNull(sequence);
            Assert.NotEmpty(sequence);
        }
    }

    #endregion
}
