using XTerm.Graphics;
using XTerm.Options;
using XTerm.Tests.Graphics;

namespace XTerm.Tests;

public class ITerm2OscTests
{
    private static Terminal CreateTerminal() => new(new TerminalOptions
    {
        Cols = 30,
        Rows = 12,
        CellWidthPixels = 2,
        CellHeightPixels = 3
    });

    private static string Osc(string data) => $"\u001b]1337;{data}\u001b\\";

    [Fact]
    public void File_InlinePng_IsDecodedAndPlaced()
    {
        var terminal = CreateTerminal();
        var png = "iVBORw0KGgoAAAANSUhEUgAAAAUAAAADCAYAAABbNsX4AAAAAXNSR0IArs4c6QAAAARnQU1BAACx" +
                  "jwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAABESURBVBhXY+ASkeOSN8+WM4naYeRZa+sWt6Ar" +
                  "iqH48PW8jmcxTXN1V07bFKC36nhx9T6GO9NOXPq40/eZ2J1ZvxigAAD9BhlVn28K4gAAAABJRU5E" +
                  "rkJggg==";

        terminal.Write(Osc($"File=inline=1:{png}"));

        var image = ImageAssertions.ImageAt(terminal, 0, 0);
        Assert.NotNull(image);
        Assert.Equal(5, image!.PixelWidth);
        Assert.Equal(3, image.PixelHeight);
    }

    [Fact]
    public void ShellIntegrationValues_AreRecorded()
    {
        var terminal = CreateTerminal();

        terminal.Write(Osc("SetUserVar=prompt=dmFsdWU="));
        terminal.Write(Osc("CurrentDir=file://localhost/home/me"));
        terminal.Write(Osc("ShellIntegrationVersion=12"));
        terminal.Write(Osc("RemoteHost=me@example.test"));

        Assert.Equal("value", terminal.UserVariables["prompt"]);
        Assert.Equal("/home/me", terminal.CurrentDirectory);
        Assert.Equal("12", terminal.ShellIntegrationVersion);
        Assert.Equal("me@example.test", terminal.RemoteHost);
    }

    [Fact]
    public void File_ExceedingRegistryBudget_IsIgnored()
    {
        var terminal = new Terminal(new TerminalOptions
        {
            Cols = 30,
            Rows = 12,
            MaxImageRegistryBytes = 1
        });
        const string png = "iVBORw0KGgoAAAANSUhEUgAAAAUAAAADCAYAAABbNsX4AAAAAXNSR0IArs4c6QAAAARnQU1BAACx" +
                           "jwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAABESURBVBhXY+ASkeOSN8+WM4naYeRZa+sWt6Ar" +
                           "iqH48PW8jmcxTXN1V07bFKC36nhx9T6GO9NOXPq40/eZ2J1ZvxigAAD9BhlVn28K4gAAAABJRU5E" +
                           "rkJggg==";

        terminal.Write(Osc($"File=inline=1:{png}"));

        Assert.Null(ImageAssertions.ImageAt(terminal, 0, 0));
    }

    [Fact]
    public void UnknownKey_IsIgnored()
    {
        var terminal = CreateTerminal();

        terminal.Write(Osc("Unrecognised=value"));

        Assert.Empty(terminal.UserVariables);
        Assert.Null(ImageAssertions.ImageAt(terminal, 0, 0));
    }
}
