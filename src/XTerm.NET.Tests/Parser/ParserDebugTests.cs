using XTerm.Parser;
using XTerm.Events.Parser;
using Xunit;

namespace XTerm.Tests.Parser;

public class ParserDebugTests
{
    [Fact]
    public void Debug_Parse_5A()
    {
        // Arrange
        var parser = new EscapeSequenceParser();
        var calls = new List<(string, Params)>();
        parser.Csi += (sender, e) => {
            var paramsClone = e.Parameters.Clone();
            calls.Add((e.Identifier, paramsClone));
        };

        // Act
        parser.Parse("\x1B[5A");

        // Assert
        Assert.Single(calls);
        var call = calls[0];
        Assert.Contains("A", call.Item1);
        
        // Debug output
        Console.WriteLine($"Params Length: {call.Item2.Length}");
        for (int i = 0; i < call.Item2.Length; i++)
        {
            Console.WriteLine($"  Param[{i}] = {call.Item2.GetParam(i)}");
        }
        
        Assert.Equal(5, call.Item2.GetParam(0));
    }
}
