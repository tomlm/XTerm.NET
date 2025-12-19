using XTerm;
using XTerm.Input;
using XTerm.Options;

namespace XTerm.Tests;

public class KeyboardInputGeneratorTests
{
    [Fact]
    public void ArrowWithoutModifiers_UsesStandardCsi()
    {
        var terminal = new Terminal(new TerminalOptions());

        var sequence = terminal.GenerateKeyInput(Key.LeftArrow, KeyModifiers.None);

        Assert.Equal("\u001b[D", sequence);
    }

    [Fact]
    public void ArrowWithAltControl_EncodesModifierCode()
    {
        var terminal = new Terminal(new TerminalOptions());

        var sequence = terminal.GenerateKeyInput(Key.UpArrow, KeyModifiers.Alt | KeyModifiers.Control);

        Assert.Equal("\u001b[1;7A", sequence);
    }

    [Fact]
    public void CharWithAltAndControl_PrefixesEscAndControlCode()
    {
        var terminal = new Terminal(new TerminalOptions());

        var sequence = terminal.GenerateCharInput('a', KeyModifiers.Control | KeyModifiers.Alt);

        Assert.Equal("\u001b\u0001", sequence);
    }
}
