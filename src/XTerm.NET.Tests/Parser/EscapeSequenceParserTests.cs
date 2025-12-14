using XTerm.NET.Parser;
using System.Text;

namespace XTerm.NET.Tests.Parser;

public class EscapeSequenceParserTests
{
    [Fact]
    public void Constructor_InitializesParser()
    {
        // Arrange & Act
        var parser = new EscapeSequenceParser();

        // Assert
        Assert.NotNull(parser);
    }

    [Fact]
    public void Parse_SimpleText_CallsPrintHandler()
    {
        // Arrange
        var parser = new EscapeSequenceParser();
        var printed = new StringBuilder();
        parser.PrintHandler = data => printed.Append(data);

        // Act
        parser.Parse("Hello");

        // Assert
        Assert.Equal("Hello", printed.ToString());
    }

    [Fact]
    public void Parse_EmptyString_DoesNothing()
    {
        // Arrange
        var parser = new EscapeSequenceParser();
        var called = false;
        parser.PrintHandler = data => called = true;

        // Act
        parser.Parse("");

        // Assert
        Assert.False(called);
    }

    [Fact]
    public void Parse_ControlCharacter_CallsExecuteHandler()
    {
        // Arrange
        var parser = new EscapeSequenceParser();
        var executedCodes = new List<int>();
        parser.ExecuteHandler = code => executedCodes.Add(code);

        // Act
        parser.Parse("\x07"); // BEL

        // Assert
        Assert.Contains(0x07, executedCodes);
    }

    [Fact]
    public void Parse_LineFeed_CallsExecuteHandler()
    {
        // Arrange
        var parser = new EscapeSequenceParser();
        var executedCodes = new List<int>();
        parser.ExecuteHandler = code => executedCodes.Add(code);

        // Act
        parser.Parse("\n");

        // Assert
        Assert.Contains(0x0A, executedCodes);
    }

    [Fact]
    public void Parse_CarriageReturn_CallsExecuteHandler()
    {
        // Arrange
        var parser = new EscapeSequenceParser();
        var executedCodes = new List<int>();
        parser.ExecuteHandler = code => executedCodes.Add(code);

        // Act
        parser.Parse("\r");

        // Assert
        Assert.Contains(0x0D, executedCodes);
    }

    [Fact]
    public void Parse_Tab_CallsExecuteHandler()
    {
        // Arrange
        var parser = new EscapeSequenceParser();
        var executedCodes = new List<int>();
        parser.ExecuteHandler = code => executedCodes.Add(code);

        // Act
        parser.Parse("\t");

        // Assert
        Assert.Contains(0x09, executedCodes);
    }

    [Fact]
    public void Parse_Backspace_CallsExecuteHandler()
    {
        // Arrange
        var parser = new EscapeSequenceParser();
        var executedCodes = new List<int>();
        parser.ExecuteHandler = code => executedCodes.Add(code);

        // Act
        parser.Parse("\x08");

        // Assert
        Assert.Contains(0x08, executedCodes);
    }

    [Fact]
    public void Parse_CsiSequence_CallsCsiHandler()
    {
        // Arrange
        var parser = new EscapeSequenceParser();
        var csiCalls = new List<(string identifier, Params parameters)>();
        parser.CsiHandler = (id, p) => csiCalls.Add((id, p));

        // Act
        parser.Parse("\x1B[H"); // Cursor Home

        // Assert
        Assert.Single(csiCalls);
        Assert.Contains("H", csiCalls[0].identifier);
    }

    [Fact]
    public void Parse_CsiWithParameters_PassesParameters()
    {
        // Arrange
        var parser = new EscapeSequenceParser();
        var csiCalls = new List<(string identifier, Params parameters)>();
        parser.CsiHandler = (id, p) => csiCalls.Add((id, p));

        // Act
        parser.Parse("\x1B[10;20H"); // Cursor Position

        // Assert
        Assert.Single(csiCalls);
        var call = csiCalls[0];
        Assert.Contains("H", call.identifier);
        Assert.Equal(10, call.parameters.GetParam(0));
        Assert.Equal(20, call.parameters.GetParam(1));
    }

    [Fact]
    public void Parse_CsiWithSingleParameter_ParsesCorrectly()
    {
        // Arrange
        var parser = new EscapeSequenceParser();
        var csiCalls = new List<(string identifier, Params parameters)>();
        parser.CsiHandler = (id, p) => csiCalls.Add((id, p));

        // Act
        parser.Parse("\x1B[5A"); // Cursor Up 5

        // Assert
        Assert.Single(csiCalls);
        var call = csiCalls[0];
        Assert.Contains("A", call.identifier);
        Assert.Equal(5, call.parameters.GetParam(0));
    }

    [Fact]
    public void Parse_SgrSequence_CallsCsiHandler()
    {
        // Arrange
        var parser = new EscapeSequenceParser();
        var csiCalls = new List<(string identifier, Params parameters)>();
        parser.CsiHandler = (id, p) => csiCalls.Add((id, p));

        // Act
        parser.Parse("\x1B[1;31m"); // Bold + Red foreground

        // Assert
        Assert.Single(csiCalls);
        var call = csiCalls[0];
        Assert.Contains("m", call.identifier);
        Assert.Equal(1, call.parameters.GetParam(0));
        Assert.Equal(31, call.parameters.GetParam(1));
    }

    [Fact]
    public void Parse_EscSequence_CallsEscHandler()
    {
        // Arrange
        var parser = new EscapeSequenceParser();
        var escCalls = new List<(string finalChar, string collected)>();
        parser.EscHandler = (f, c) => escCalls.Add((f, c));

        // Act
        parser.Parse("\x1B" + "D"); // Index

        // Assert
        Assert.Single(escCalls);
        Assert.Equal("D", escCalls[0].finalChar);
    }

    [Fact]
    public void Parse_OscSequence_CallsOscHandler()
    {
        // Arrange
        var parser = new EscapeSequenceParser();
        var oscData = new List<string>();
        parser.OscHandler = data => oscData.Add(data);

        // Act
        parser.Parse("\x1B]0;Test Title\x07"); // Set title

        // Assert
        Assert.Single(oscData);
        Assert.Equal("0;Test Title", oscData[0]);
    }

    [Fact]
    public void Parse_OscWithEscTerminator_CallsOscHandler()
    {
        // Arrange
        var parser = new EscapeSequenceParser();
        var oscData = new List<string>();
        parser.OscHandler = data => oscData.Add(data);

        // Act
        parser.Parse("\x1B]2;Window Title\x1B\\"); // Set title with ESC terminator

        // Assert
        Assert.Single(oscData);
        Assert.Equal("2;Window Title", oscData[0]);
    }

    [Fact]
    public void Parse_MixedContent_HandlesCorrectly()
    {
        // Arrange
        var parser = new EscapeSequenceParser();
        var printed = new StringBuilder();
        var csiCalls = new List<string>();
        
        parser.PrintHandler = data => printed.Append(data);
        parser.CsiHandler = (id, p) => csiCalls.Add(id);

        // Act
        parser.Parse("Hello\x1B[1mWorld");

        // Assert
        Assert.Contains("Hello", printed.ToString());
        Assert.Contains("World", printed.ToString());
        Assert.Single(csiCalls);
    }

    [Fact]
    public void Parse_MultipleSequences_HandlesAll()
    {
        // Arrange
        var parser = new EscapeSequenceParser();
        var csiCalls = new List<string>();
        parser.CsiHandler = (id, p) => csiCalls.Add(id);

        // Act
        parser.Parse("\x1B[H\x1B[2J\x1B[1;1H");

        // Assert
        Assert.Equal(3, csiCalls.Count);
        Assert.Contains("H", csiCalls[0]);
        Assert.Contains("J", csiCalls[1]);
        Assert.Contains("H", csiCalls[2]);
    }

    [Fact]
    public void Parse_LongString_HandlesCorrectly()
    {
        // Arrange
        var parser = new EscapeSequenceParser();
        var printed = new StringBuilder();
        parser.PrintHandler = data => printed.Append(data);
        var longString = new string('A', 1000);

        // Act
        parser.Parse(longString);

        // Assert
        Assert.Equal(1000, printed.Length);
    }

    [Fact]
    public void Reset_ResetsParserState()
    {
        // Arrange
        var parser = new EscapeSequenceParser();
        parser.Parse("\x1B[");

        // Act
        parser.Reset();
        
        var printed = new StringBuilder();
        parser.PrintHandler = data => printed.Append(data);
        parser.Parse("Test");

        // Assert
        Assert.Equal("Test", printed.ToString());
    }

    [Fact]
    public void Parse_IncompleteSequence_HandlesGracefully()
    {
        // Arrange
        var parser = new EscapeSequenceParser();
        var printed = new StringBuilder();
        parser.PrintHandler = data => printed.Append(data);

        // Act
        parser.Parse("\x1B[");
        parser.Parse("H");

        // Assert - Should complete the sequence
        // The parser handles incomplete sequences by continuing in next parse
    }

    [Fact]
    public void Parse_CsiErase_ParsesCorrectly()
    {
        // Arrange
        var parser = new EscapeSequenceParser();
        var csiCalls = new List<(string identifier, Params parameters)>();
        parser.CsiHandler = (id, p) => csiCalls.Add((id, p));

        // Act
        parser.Parse("\x1B[2J"); // Erase Display

        // Assert
        Assert.Single(csiCalls);
        Assert.Contains("J", csiCalls[0].identifier);
        Assert.Equal(2, csiCalls[0].parameters.GetParam(0));
    }

    [Fact]
    public void Parse_CsiCursorMovement_ParsesCorrectly()
    {
        // Arrange
        var parser = new EscapeSequenceParser();
        var csiCalls = new List<(string identifier, Params parameters)>();
        parser.CsiHandler = (id, p) => csiCalls.Add((id, p));

        // Act
        parser.Parse("\x1B[5A"); // Cursor Up
        parser.Parse("\x1B[3B"); // Cursor Down
        parser.Parse("\x1B[2C"); // Cursor Forward
        parser.Parse("\x1B[4D"); // Cursor Backward

        // Assert
        Assert.Equal(4, csiCalls.Count);
        Assert.Contains("A", csiCalls[0].identifier);
        Assert.Contains("B", csiCalls[1].identifier);
        Assert.Contains("C", csiCalls[2].identifier);
        Assert.Contains("D", csiCalls[3].identifier);
    }

    [Fact]
    public void Parse_SaveRestoreCursor_ParsesCorrectly()
    {
        // Arrange
        var parser = new EscapeSequenceParser();
        var escCalls = new List<string>();
        parser.EscHandler = (f, c) => escCalls.Add(f);

        // Act
        parser.Parse("\x1B" + "7"); // Save cursor - ESC followed by '7'
        parser.Parse("\x1B" + "8"); // Restore cursor - ESC followed by '8'

        // Assert
        Assert.Equal(2, escCalls.Count);
        Assert.Contains("7", escCalls);
        Assert.Contains("8", escCalls);
    }

    [Fact]
    public void Parse_ComplexSgrSequence_ParsesAllParameters()
    {
        // Arrange
        var parser = new EscapeSequenceParser();
        var csiCalls = new List<(string identifier, Params parameters)>();
        parser.CsiHandler = (id, p) => csiCalls.Add((id, p));

        // Act
        parser.Parse("\x1B[1;3;4;31;42m"); // Bold, Italic, Underline, Red FG, Green BG

        // Assert
        Assert.Single(csiCalls);
        var call = csiCalls[0];
        Assert.Contains("m", call.identifier);
        Assert.Equal(1, call.parameters.GetParam(0));
        Assert.Equal(3, call.parameters.GetParam(1));
        Assert.Equal(4, call.parameters.GetParam(2));
        Assert.Equal(31, call.parameters.GetParam(3));
        Assert.Equal(42, call.parameters.GetParam(4));
    }

    [Fact]
    public void Parse_ScrollRegion_ParsesCorrectly()
    {
        // Arrange
        var parser = new EscapeSequenceParser();
        var csiCalls = new List<(string identifier, Params parameters)>();
        parser.CsiHandler = (id, p) => csiCalls.Add((id, p));

        // Act
        parser.Parse("\x1B[5;20r"); // Set scroll region

        // Assert
        Assert.Single(csiCalls);
        var call = csiCalls[0];
        Assert.Contains("r", call.identifier);
        Assert.Equal(5, call.parameters.GetParam(0));
        Assert.Equal(20, call.parameters.GetParam(1));
    }

    [Fact]
    public void Parse_TextWithEmbeddedEscapes_HandlesCorrectly()
    {
        // Arrange
        var parser = new EscapeSequenceParser();
        var printed = new StringBuilder();
        var csiCount = 0;
        
        parser.PrintHandler = data => printed.Append(data);
        parser.CsiHandler = (id, p) => csiCount++;

        // Act
        parser.Parse("Line1\x1B[1mBold\x1B[0mNormal");

        // Assert
        Assert.Contains("Line1", printed.ToString());
        Assert.Contains("Bold", printed.ToString());
        Assert.Contains("Normal", printed.ToString());
        Assert.Equal(2, csiCount);
    }

    [Fact]
    public void Parse_ZeroParameters_HandlesCorrectly()
    {
        // Arrange
        var parser = new EscapeSequenceParser();
        var csiCalls = new List<(string identifier, Params parameters)>();
        parser.CsiHandler = (id, p) => csiCalls.Add((id, p));

        // Act
        parser.Parse("\x1B[m"); // SGR reset with no parameters

        // Assert
        Assert.Single(csiCalls);
        Assert.Contains("m", csiCalls[0].identifier);
    }

    [Fact]
    public void Handlers_CanBeNull_WithoutCrashing()
    {
        // Arrange
        var parser = new EscapeSequenceParser();
        // All handlers are null by default

        // Act & Assert - Should not throw
        parser.Parse("Hello");
        parser.Parse("\x1B[H");
        parser.Parse("\x1B]0;Title\x07");
        parser.Parse("\x07");
    }

    [Fact]
    public void Parse_UnicodeCharacters_HandlesCorrectly()
    {
        // Arrange
        var parser = new EscapeSequenceParser();
        var printed = new StringBuilder();
        parser.PrintHandler = data => printed.Append(data);

        // Act
        parser.Parse("Hello ?? ??");

        // Assert
        Assert.Contains("Hello", printed.ToString());
        Assert.Contains("??", printed.ToString());
        Assert.Contains("??", printed.ToString());
    }
}
