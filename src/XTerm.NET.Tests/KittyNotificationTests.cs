using XTerm.Events;
using XTerm.Options;

namespace XTerm.Tests;

public class KittyNotificationTests
{
    private const string Esc = "\x1b";
    private const string Bel = "\x07";
    private const string St = "\x1b\\";

    /// <summary>
    /// Notifications are opt-in, so every test that expects one asks for them.
    /// </summary>
    private static Terminal CreateTerminal() =>
        new(new TerminalOptions { KittyNotificationsEnabled = true });

    [Fact]
    public void Osc99_RaisesStructuredNotification_WhenComplete()
    {
        var terminal = CreateTerminal();
        TerminalEvents.NotificationEventArgs? notification = null;
        terminal.NotificationReceived += (_, e) => notification = e;

        terminal.Write($"{Esc}]99;i=build-42:p=title:d=0:e=1:u=2:n=YnVpbGQ=;QnVpbGQ={St}");
        terminal.Write($"{Esc}]99;i=build-42:p=body:d=1:e=1;IGZpbmlzaGVk{St}");

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
        var terminal = CreateTerminal();
        TerminalEvents.NotificationEventArgs? notification = null;
        terminal.NotificationReceived += (_, e) => notification = e;

        terminal.Write($"{Esc}]99;i=build:p=body:d=0:e=1;QnVpbGQ={Bel}");
        terminal.Write($"{Esc}]99;i=build:p=body:d=1:e=1;IGZpbmlzaGVk{Bel}");

        Assert.NotNull(notification);
        // Body-only chunks: the assembled body is PROMOTED to the title, per the spec's
        // "if a notification has no title, the body will be used as title."
        Assert.Equal("Build finished", notification!.Title);
        Assert.Null(notification.Body);
    }

    [Fact]
    public void Osc99_DefaultsToPlainTextTitle()
    {
        var terminal = CreateTerminal();
        TerminalEvents.NotificationEventArgs? notification = null;
        terminal.NotificationReceived += (_, e) => notification = e;

        terminal.Write($"{Esc}]99;;Hello world{St}");

        Assert.NotNull(notification);
        Assert.Equal("Hello world", notification!.Title);
        Assert.Null(notification.Body);
        Assert.Equal("Hello world", notification.Text);
    }

    [Fact]
    public void Osc99_CompleteNotificationBypassesPendingIdentifierLimit()
    {
        var terminal = CreateTerminal();
        TerminalEvents.NotificationEventArgs? notification = null;
        terminal.NotificationReceived += (_, e) => notification = e;

        for (var i = 0; i < 16; i++)
            terminal.Write($"{Esc}]99;i=pending-{i}:d=0;partial{St}");

        terminal.Write($"{Esc}]99;;Hello world{St}");

        Assert.NotNull(notification);
        Assert.Equal("Hello world", notification!.Title);
    }

    [Fact]
    public void Osc99_AnswersCapabilityQuery()
    {
        var terminal = CreateTerminal();
        string? response = null;
        terminal.DataReceived += (_, e) => response = e.Data;

        terminal.Write($"{Esc}]99;i=query:p=?;{St}");

        Assert.Equal($"{Esc}]99;i=query:p=?;p=title,body{St}", response);
    }

    [Fact]
    public void Osc99_DoesNotRaise_WhenDisabled()
    {
        var terminal = new Terminal(new TerminalOptions { KittyNotificationsEnabled = false });
        var notifications = new List<TerminalEvents.NotificationEventArgs>();
        terminal.NotificationReceived += (_, e) => notifications.Add(e);

        terminal.Write($"{Esc}]99;p=body;SGVsbG8={Bel}");

        Assert.Empty(notifications);
    }

    [Fact]
    public void Osc99_IsIgnoredByDefault()
    {
        var terminal = new Terminal(new TerminalOptions());
        var notifications = new List<TerminalEvents.NotificationEventArgs>();
        var responses = new List<string>();
        terminal.NotificationReceived += (_, e) => notifications.Add(e);
        terminal.DataReceived += (_, e) => responses.Add(e.Data);

        terminal.Write($"{Esc}]99;;Hello world{St}");
        terminal.Write($"{Esc}]99;i=query:p=?;{St}");

        Assert.Empty(notifications);
        Assert.Empty(responses);
    }

    [Fact]
    public void Osc99_BodyOnlyNotification_PromotesBodyToTitle()
    {
        // The spec: "If a notification has no title, the body will be used as title."
        var terminal = CreateTerminal();
        TerminalEvents.NotificationEventArgs? notification = null;
        terminal.NotificationReceived += (_, e) => notification = e;

        terminal.Write($"{Esc}]99;p=body;Only a body{St}");

        Assert.NotNull(notification);
        Assert.Equal("Only a body", notification!.Title);
        Assert.Null(notification.Body);
        Assert.Equal("Only a body", notification.Text);
    }

    [Fact]
    public void Osc99_OutOfRangeUrgency_ReadsAsUnspecified()
    {
        // u is exactly 0, 1 or 2; u=999 must not escape into the public event.
        var terminal = CreateTerminal();
        TerminalEvents.NotificationEventArgs? notification = null;
        terminal.NotificationReceived += (_, e) => notification = e;

        terminal.Write($"{Esc}]99;u=999;Hello{St}");

        Assert.NotNull(notification);
        Assert.Null(notification!.Urgency);
    }
}
