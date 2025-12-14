using XTerm.NET.Parser;

var parser = new EscapeSequenceParser();
var calls = new List<(string, Params)>();
parser.CsiHandler = (id, p) => {
    calls.Add((id, p));
    Console.WriteLine($"CSI: {id}, Params Count: {p.Length}");
    for (int i = 0; i < p.Length; i++)
    {
        Console.WriteLine($"  Param[{i}] = {p.GetParam(i)}");
    }
};

Console.WriteLine("Test 1: \\x1B[1;31m");
parser.Parse("\x1B[1;31m");
Console.WriteLine($"Total calls: {calls.Count}\n");

calls.Clear();
Console.WriteLine("Test 2: \\x1B[5A");
parser.Parse("\x1B[5A");
Console.WriteLine($"Total calls: {calls.Count}");
