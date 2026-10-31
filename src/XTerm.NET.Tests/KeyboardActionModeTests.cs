using XTerm.Input;
using XTerm.Options;

namespace XTerm.Tests;

/// <summary>
/// KAM (SM 2) locks the keyboard, but only when the host allows it with
/// <see cref="TerminalOptions.KeyboardActionModeAllowed"/>; otherwise SM 2 is ignored, as Ghostty
/// ignores it.
/// </summary>
public class KeyboardActionModeTests
{
    private const string SetKam = "\u001b[2h";
    private const string ResetKam = "\u001b[2l";
    private const string QueryKam = "\u001b[2$p";
    private const string KamSet = "\u001b[2;1$y";
    private const string KamReset = "\u001b[2;2$y";

    private static (Terminal terminal, List<string> data) Create(bool allowed = false)
    {
        var terminal = new Terminal(new TerminalOptions
        {
            Cols = 20,
            Rows = 4,
            KeyboardActionModeAllowed = allowed,
        });
        var data = new List<string>();
        terminal.DataReceived += (_, e) => data.Add(e.Data);
        return (terminal, data);
    }

    [Fact]
    public void By_default_SM_2_is_ignored_and_reported_reset()
    {
        var (terminal, data) = Create();

        terminal.Write(SetKam);
        terminal.Write(QueryKam);

        Assert.False(terminal.KeyboardLocked);
        Assert.Equal([KamReset], data);
    }

    [Fact]
    public void By_default_input_still_flows_after_SM_2()
    {
        var (terminal, data) = Create();

        terminal.Write(SetKam);

        Assert.Equal("\r", terminal.GenerateKeyInput(Key.Enter));
        Assert.Equal("a", terminal.GenerateCharInput('a'));
        terminal.Paste("hi");
        Assert.Equal(["hi"], data);
    }

    [Fact]
    public void When_allowed_SM_2_locks_and_RM_2_unlocks_and_DECRQM_follows()
    {
        var (terminal, data) = Create(allowed: true);

        terminal.Write(QueryKam);
        terminal.Write(SetKam);
        var lockedAfterSet = terminal.KeyboardLocked;
        terminal.Write(QueryKam);
        terminal.Write(ResetKam);
        terminal.Write(QueryKam);

        Assert.True(lockedAfterSet);
        Assert.False(terminal.KeyboardLocked);
        Assert.Equal([KamReset, KamSet, KamReset], data);
    }

    [Fact]
    public void A_locked_keyboard_drops_key_char_and_paste_input()
    {
        var (terminal, data) = Create(allowed: true);

        terminal.Write(SetKam);
        data.Clear();

        Assert.Equal(string.Empty, terminal.GenerateKeyInput(Key.Enter));
        Assert.Equal(string.Empty, terminal.GenerateCharInput('a'));
        terminal.Paste("hi");
        Assert.Empty(data);

        terminal.Write(ResetKam);

        Assert.Equal("\r", terminal.GenerateKeyInput(Key.Enter));
        Assert.Equal("a", terminal.GenerateCharInput('a'));
        terminal.Paste("hi");
        Assert.Equal(["hi"], data);
    }

    [Fact]
    public void A_locked_keyboard_drops_kitty_key_input()
    {
        var (terminal, _) = Create(allowed: true);
        terminal.Write("\u001b[=1u"); // Kitty: disambiguate escape codes
        var escape = new KeyEvent { Key = "Escape", Code = "Escape" };

        // The precondition, so the locked assertion below could have failed.
        Assert.NotNull(terminal.GenerateKittyKeyInput(escape));

        terminal.Write(SetKam);

        Assert.Null(terminal.GenerateKittyKeyInput(escape));
    }

    [Theory]
    [InlineData("\u001bc")]    // RIS
    [InlineData("\u001b[!p")]  // DECSTR
    public void Resets_unlock_the_keyboard(string reset)
    {
        var (terminal, data) = Create(allowed: true);

        terminal.Write(SetKam);
        Assert.True(terminal.KeyboardLocked);

        terminal.Write(reset);
        terminal.Write(QueryKam);

        Assert.False(terminal.KeyboardLocked);
        Assert.Equal([KamReset], data);
    }

    [Fact]
    public void Disallowing_after_a_lock_still_lets_RM_2_unlock()
    {
        var (terminal, _) = Create(allowed: true);

        terminal.Write(SetKam);
        terminal.Options.KeyboardActionModeAllowed = false;
        Assert.True(terminal.KeyboardLocked);

        terminal.Write(ResetKam);
        Assert.False(terminal.KeyboardLocked);

        terminal.Write(SetKam);
        Assert.False(terminal.KeyboardLocked);
    }
}
