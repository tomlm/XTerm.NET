using XTerm.Common;

namespace XTerm.Tests;

/// <summary>
/// Replays python wcwidth's answer for every codepoint against the vendored tables. The fixture
/// is emitted by the same script that generates the tables, so the two can only change together:
/// regenerating against a newer wcwidth updates both and this test judges nothing, while a hand
/// edit to either — or a regeneration that changes what the referee would measure — goes red
/// with the first diverging codepoint named.
/// </summary>
public class WidthTableParityTests
{
    [Fact]
    public void Every_codepoint_matches_the_referee()
    {
        var lines = File.ReadAllLines(Path.Combine("Fixtures", "wcwidth-parity.rle"));
        var codePoint = 0;
        foreach (var line in lines)
        {
            if (line.Length == 0 || line[0] == '#')
                continue;

            var parts = line.Split(' ');
            var expected = int.Parse(parts[0]);
            var run = int.Parse(parts[1]);
            for (var end = codePoint + run; codePoint < end; codePoint++)
            {
                var got = WidthTables.Lookup(codePoint);
                if (got != expected)
                    Assert.Fail($"U+{codePoint:X4}: referee says {expected}, table says {got}");
            }
        }

        Assert.Equal(0x110000, codePoint);   // the fixture covered the whole codespace
    }
}
