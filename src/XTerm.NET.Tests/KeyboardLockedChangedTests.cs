using XTerm.Options;

namespace XTerm.Tests;

/// <summary>
/// <see cref="Terminal.KeyboardLockedChanged"/> is raised exactly when
/// <see cref="Terminal.KeyboardLocked"/> changes, carrying its new value, and never otherwise.
/// </summary>
public class KeyboardLockedChangedTests
{
    private static readonly string Esc = ((char)0x1B).ToString();
    private static readonly string SetKam = Esc + "[2h";
    private static readonly string ResetKam = Esc + "[2l";
    private static readonly string Ris = Esc + "c";
    private static readonly string Decstr = Esc + "[!p";

    private static (Terminal terminal, List<bool> raised) Create(bool allowed)
    {
        var terminal = new Terminal(new TerminalOptions
        {
            Cols = 20,
            Rows = 4,
            KeyboardActionModeAllowed = allowed,
        });
        var raised = new List<bool>();
        terminal.KeyboardLockedChanged += (sender, e) =>
        {
            Assert.Same(terminal, sender);
            // The value is in place before anyone hears about it.
            Assert.Equal(e.Locked, terminal.KeyboardLocked);
            raised.Add(e.Locked);
        };
        return (terminal, raised);
    }

    [Fact]
    public void SM_2_while_allowed_raises_locked_once()
    {
        var (terminal, raised) = Create(allowed: true);

        terminal.Write(SetKam);

        Assert.Equal([true], raised);
    }

    [Theory]
    [InlineData("RM 2")]
    [InlineData("RIS")]
    [InlineData("DECSTR")]
    public void Each_reset_of_a_locked_keyboard_raises_unlocked_once(string reset)
    {
        var (terminal, raised) = Create(allowed: true);
        terminal.Write(SetKam);
        raised.Clear();

        terminal.Write(reset switch { "RM 2" => ResetKam, "RIS" => Ris, _ => Decstr });

        Assert.Equal([false], raised);
    }

    [Fact]
    public void Turning_the_option_on_while_mode_2_is_set_raises_locked_once()
    {
        var (terminal, raised) = Create(allowed: false);
        terminal.Write(SetKam);

        terminal.Options.KeyboardActionModeAllowed = true;

        Assert.Equal([true], raised);
    }

    [Fact]
    public void Turning_the_option_off_while_locked_raises_unlocked_once()
    {
        var (terminal, raised) = Create(allowed: true);
        terminal.Write(SetKam);
        raised.Clear();

        terminal.Options.KeyboardActionModeAllowed = false;

        Assert.Equal([false], raised);
    }

    [Fact]
    public void SM_2_while_not_allowed_raises_nothing()
    {
        var (terminal, raised) = Create(allowed: false);

        terminal.Write(SetKam);
        terminal.Write(ResetKam);

        Assert.Empty(raised);
    }

    [Fact]
    public void SM_2_when_already_locked_raises_nothing()
    {
        var (terminal, raised) = Create(allowed: true);
        terminal.Write(SetKam);
        raised.Clear();

        terminal.Write(SetKam);

        Assert.Empty(raised);
    }

    [Theory]
    [InlineData("RM 2")]
    [InlineData("RIS")]
    [InlineData("DECSTR")]
    public void Resetting_an_unlocked_keyboard_raises_nothing(string reset)
    {
        var (terminal, raised) = Create(allowed: true);

        terminal.Write(reset switch { "RM 2" => ResetKam, "RIS" => Ris, _ => Decstr });

        Assert.Empty(raised);
    }

    [Fact]
    public void Flipping_the_option_while_mode_2_is_reset_raises_nothing()
    {
        var (terminal, raised) = Create(allowed: false);

        terminal.Options.KeyboardActionModeAllowed = true;
        terminal.Options.KeyboardActionModeAllowed = false;

        Assert.Empty(raised);
    }

    [Fact]
    public void Writing_the_option_its_current_value_raises_nothing()
    {
        var (terminal, raised) = Create(allowed: true);
        terminal.Write(SetKam);
        raised.Clear();

        terminal.Options.KeyboardActionModeAllowed = true;

        Assert.Empty(raised);
    }

    [Fact]
    public void Other_ANSI_modes_raise_nothing()
    {
        var (terminal, raised) = Create(allowed: true);

        terminal.Write(Esc + "[4h" + Esc + "[20h" + Esc + "[4l" + Esc + "[20l");

        Assert.Empty(raised);
    }

    [Fact]
    public void Dispose_unhooks_the_option()
    {
        var (terminal, _) = Create(allowed: false);
        var options = terminal.Options;
        Assert.NotNull(options.KeyboardActionModeAllowedChanged);

        terminal.Dispose();

        // The snapshot can outlive the terminal; its hook must not keep the terminal alive.
        Assert.Null(options.KeyboardActionModeAllowedChanged);
    }

    [Fact]
    public void The_caller_options_object_is_not_wired_to_the_terminal()
    {
        var options = new TerminalOptions { KeyboardActionModeAllowed = false };
        var terminal = new Terminal(options);
        var raised = new List<bool>();
        terminal.KeyboardLockedChanged += (_, e) => raised.Add(e.Locked);
        terminal.Write(SetKam);

        options.KeyboardActionModeAllowed = true;

        Assert.Empty(raised);
        Assert.False(terminal.KeyboardLocked);
    }
}
