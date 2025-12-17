using XTerm;
using XTerm.Common;
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
        terminal.TitleChanged += title =>
        {
            titleChanged = true;
            newTitle = title;
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
        terminal.DirectoryChanged += dir =>
        {
            directoryChanged = true;
            newDirectory = dir;
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

        // Act
        terminal.Write("\x1B]8;;http://example.com\x07");

        // Assert
        Assert.Equal("http://example.com", terminal.CurrentHyperlink);
    }

    [Fact]
    public void OscHyperlink_EndLink_ClearsHyperlink()
    {
        // Arrange
        var terminal = CreateTerminal();
        terminal.Write("\x1B]8;;http://example.com\x07");

        // Act
        terminal.Write("\x1B]8;;\x07");

        // Assert
        Assert.Null(terminal.CurrentHyperlink);
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
        terminal.DataReceived += data => response = data;

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
        terminal.DataReceived += data => response = data;

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
        terminal.DataReceived += data => response = data;

        // Act
        terminal.Write("\x1B]12;?\x07");

        // Assert
        Assert.NotNull(response);
        Assert.Contains("rgb:", response);
        Assert.Contains("]12;", response);
    }

    [Fact]
    public void OscClipboard_Query_RespondsWithEmptyData()
    {
        // Arrange
        var terminal = CreateTerminal();
        string? response = null;
        terminal.DataReceived += data => response = data;

        // Act
        terminal.Write("\x1B]52;c;?\x07");

        // Assert
        Assert.NotNull(response);
        Assert.Contains("]52;c;", response);
    }

    [Fact]
    public void OscClipboard_SetData_DoesNotThrow()
    {
        // Arrange
        var terminal = CreateTerminal();
        var base64Data = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("Hello, World!"));

        // Act & Assert - Should not throw
        terminal.Write($"\x1B]52;c;{base64Data}\x07");
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
        terminal.TitleChanged += title => titleChangeCount++;
        terminal.DirectoryChanged += dir => directoryChangeCount++;

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
        terminal.DirectoryChanged += dir => paths.Add(dir);

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
        terminal.Write("\x1B]0;Title with émojis ?? and spëcial chars\x07");

        // Assert
        Assert.Equal("Title with émojis ?? and spëcial chars", terminal.Title);
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
        terminal.DataReceived += data => responses.Add(data);

        // Act
        terminal.Write("\x1B]10;?\x07");
        terminal.Write("\x1B]11;?\x07");
        terminal.Write("\x1B]12;?\x07");

        // Assert
        Assert.Equal(3, responses.Count);
        Assert.All(responses, r => Assert.Contains("rgb:", r));
    }
}
