using XTerm.Graphics;
using XTerm.Options;
using XTerm.Tests.Graphics;
using XTerm.Events;

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
        terminal.Write(Osc($"File=inline=1:{Png}"));

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
        terminal.Write(Osc("CurrentDir=/home/me"));
        terminal.Write(Osc("ShellIntegrationVersion=12"));
        terminal.Write(Osc("RemoteHost=me@example.test"));

        Assert.Equal("value", terminal.UserVariables["prompt"]);
        Assert.Equal("/home/me", terminal.CurrentDirectory);
        Assert.Equal("12", terminal.ShellIntegrationVersion);
        Assert.Equal("me@example.test", terminal.RemoteHost);
    }

    [Fact]
    public void CurrentDir_PreservesLiteralPercentSequences()
    {
        var terminal = CreateTerminal();

        terminal.Write(Osc("CurrentDir=/data/AB%20CD"));

        Assert.Equal("/data/AB%20CD", terminal.CurrentDirectory);
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
        terminal.Write(Osc($"File=inline=1:{Png}"));

        Assert.Null(ImageAssertions.ImageAt(terminal, 0, 0));
    }

    [Fact]
    public void File_IsIgnoredWhenITerm2ImagesAreDisabled()
    {
        var terminal = CreateTerminal();
        terminal.Options.ITerm2ImagesEnabled = false;

        terminal.Write(Osc($"File=inline=1:{Png}"));

        Assert.Null(ImageAssertions.ImageAt(terminal, 0, 0));
    }

    [Fact]
    public void DroppedFile_IsReportedAsUnrecognized()
    {
        var terminal = CreateTerminal();
        TerminalEvents.OscReceivedEventArgs? received = null;
        terminal.OscReceived += (_, e) => received = e;

        terminal.Write(Osc("File=inline=1:not-a-png"));

        Assert.False(received!.Recognized);
    }

    [Fact]
    public void UserVariables_AreBounded()
    {
        var terminal = CreateTerminal();
        terminal.Options.MaxUserVariables = 1;
        terminal.Options.MaxUserVariableBytes = 4;

        terminal.Write(Osc("SetUserVar=one=YWJjZA=="));
        terminal.Write(Osc("SetUserVar=two=YWJjZA=="));
        terminal.Write(Osc("SetUserVar=one=dG9vIGxvbmc="));

        Assert.Equal("abcd", terminal.UserVariables["one"]);
        Assert.Single(terminal.UserVariables);
    }

    [Fact]
    public void WindowExtensions_RespectPermissions()
    {
        var terminal = CreateTerminal();
        var raised = false;
        var attention = false;
        terminal.WindowRaised += (_, _) => raised = true;
        terminal.AttentionRequested += (_, _) => attention = true;

        terminal.Write(Osc("StealFocus="));
        terminal.Write(Osc("RequestAttention="));
        Assert.False(raised);
        Assert.False(attention);

        terminal.Options.WindowOptions.RaiseWin = true;
        terminal.Options.WindowOptions.RequestAttention = true;
        terminal.Write(Osc("StealFocus"));
        terminal.Write(Osc("RequestAttention=no"));

        Assert.True(raised);
        Assert.True(attention);
    }

    [Fact]
    public void ReportCellSize_RespondsWhenAllowed()
    {
        var terminal = CreateTerminal();
        terminal.Options.WindowOptions.GetCellSizePixels = true;
        string? response = null;
        terminal.DataReceived += (_, e) => response = e.Data;

        terminal.Write(Osc("ReportCellSize"));

        Assert.Equal("\u001b]1337;ReportCellSize=3;2\u001b\\", response);
    }

    [Fact]
    public void RequestAttention_PreservesItsAction()
    {
        var terminal = CreateTerminal();
        terminal.Options.WindowOptions.RequestAttention = true;
        string? action = null;
        terminal.AttentionRequested += (_, e) => action = e.Action;

        terminal.Write(Osc("RequestAttention=no"));

        Assert.Equal("no", action);
    }

    [Fact]
    public void File_ImageIsErasedByText()
    {
        var terminal = CreateTerminal();
        terminal.Write(Osc($"File=inline=1:{Png}"));

        terminal.Write("\u001b[1;1HX");

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

    private const string Png = "iVBORw0KGgoAAAANSUhEUgAAAAUAAAADCAYAAABbNsX4AAAAAXNSR0IArs4c6QAAAARnQU1BAACx" +
                               "jwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAABESURBVBhXY+ASkeOSN8+WM4naYeRZa+sWt6Ar" +
                               "iqH48PW8jmcxTXN1V07bFKC36nhx9T6GO9NOXPq40/eZ2J1ZvxigAAD9BhlVn28K4gAAAABJRU5E" +
                               "rkJggg==";
}
