using XTerm.Events;
using XTerm.Options;

namespace XTerm.Tests;

public class KittyNotificationTests
{
    [Fact]
    public void Osc99_RaisesStructuredNotification_WhenComplete()
    {
        var terminal = new Terminal(new TerminalOptions());
        TerminalEvents.NotificationEventArgs? notification = null;
        terminal.NotificationReceived += (_, e) => notification = e;

        terminal.Write("\u001b]99;i=build-42:p=title:d=0:e=1:u=2:n=YnVpbGQ=;QnVpbGQ=\u001b\\");
        terminal.Write("\u001b]99;i=build-42:p=body:d=1:e=1;IGZpbmlzaGVk\u001b\\");

        Assert.NotNull(notification);
        Assert.Equal("build-42", notification!.Identifier);
        Assert.Equal("Build", notification.Title);
        Assert.Equal(" finished", notification.Body);
        Assert.Equal(" finished", notification.Text);
        Assert.Equal(2, notification.Urgency);
        Assert.Equal("build", notification.Icon);
    }

    [Fact]
    public void Osc99_AppendsMultipartPayload()
    {
        var terminal = new Terminal(new TerminalOptions());
        TerminalEvents.NotificationEventArgs? notification = null;
        terminal.NotificationReceived += (_, e) => notification = e;

        terminal.Write("\u001b]99;i=build:p=body:d=0:e=1;QnVpbGQ=\u0007");
        terminal.Write("\u001b]99;i=build:p=body:d=1:e=1;IGZpbmlzaGVk\u0007");

        Assert.NotNull(notification);
        Assert.Equal("Build finished", notification!.Body);
    }

    [Fact]
    public void Osc99_DefaultsToPlainTextTitle()
    {
        var terminal = new Terminal(new TerminalOptions());
        TerminalEvents.NotificationEventArgs? notification = null;
        terminal.NotificationReceived += (_, e) => notification = e;

        terminal.Write("\u001b]99;;Hello world\u001b\\");

        Assert.NotNull(notification);
        Assert.Equal("Hello world", notification!.Title);
        Assert.Null(notification.Body);
    }

    [Fact]
    public void Osc99_AnswersCapabilityQuery()
    {
        var terminal = new Terminal(new TerminalOptions());
        string? response = null;
        terminal.DataReceived += (_, e) => response = e.Data;

        terminal.Write("\u001b]99;i=query:p=?;\u001b\\");

        Assert.Equal("\u001b]99;i=query:p=?;p=title,body\u001b\\", response);
    }

    [Fact]
    public void Osc99_DoesNotRaise_WhenDisabled()
    {
        var terminal = new Terminal(new TerminalOptions { KittyNotificationsEnabled = false });
        var notifications = new List<TerminalEvents.NotificationEventArgs>();
        terminal.NotificationReceived += (_, e) => notifications.Add(e);

        terminal.Write("\u001b]99;p=body;SGVsbG8=\u0007");

        Assert.Empty(notifications);
    }
}
