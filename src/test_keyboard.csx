using XTerm.NET;
using XTerm.NET.Input;

var terminal = new Terminal();
var modifiers = KeyModifiers.Control | KeyModifiers.Shift;

var sequence = terminal.GenerateKeyInput(Key.UpArrow, modifiers);

Console.WriteLine($"Sequence length: {sequence.Length}");
for (int i = 0; i < sequence.Length; i++)
{
    Console.WriteLine($"[{i}] = 0x{((int)sequence[i]):X2} '{sequence[i]}'");
}

Console.WriteLine($"\\nExpected: \\x1B[1;6A");
Console.WriteLine($"Actual:   {string.Join("", sequence.Select(c => $"\\x{((int)c):X2}"))}");

// Check modifier code
int code = 1;
if ((modifiers & KeyModifiers.Shift) != 0) code += 1;
if ((modifiers & KeyModifiers.Alt) != 0) code += 2;
if ((modifiers & KeyModifiers.Control) != 0) code += 4;
Console.WriteLine($"\\nModifier code: {code}");
