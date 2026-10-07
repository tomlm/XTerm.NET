using XTerm;
using XTerm.Buffer;
using XTerm.Common;
using XTerm.Events;
using XTerm.Options;

namespace XTerm.Tests;

public class LineExitedViewportTests
{
    [Fact]
    public void FullScreenScroll_RaisesBeforeAlternateBufferLineIsRecycled()
    {
        var terminal = new Terminal(new TerminalOptions { Rows = 2, Cols = 8, Scrollback = 1 });
        terminal.SwitchToAltBuffer();
        SetCell(terminal.Buffer.Lines[0]!, "old");

        string? captured = null;
        BufferType? buffer = null;
        LineExitReason? reason = null;
        terminal.LineExitedViewport += (_, args) =>
        {
            captured = args.Line.TranslateToString(trimRight: true);
            buffer = args.Buffer;
            reason = args.Reason;
        };

        terminal.Buffer.ScrollUp(1);

        Assert.Equal("old", captured);
        Assert.Equal(BufferType.Alternate, buffer);
        Assert.Equal(LineExitReason.Scrolled, reason);
    }

    [Fact]
    public void FullWidthReverseScroll_RaisesForEachRemovedRowBeforeSplice()
    {
        var terminal = new Terminal(new TerminalOptions { Rows = 4, Cols = 8, Scrollback = 1 });
        terminal.Write("top\r\nnext\r\nthird\r\nbottom");

        var snapshots = new List<(int Row, string Text, BufferType Buffer, LineExitReason Reason, bool StillInBuffer)>();
        terminal.LineExitedViewport += (_, args) =>
        {
            var row = FindViewportRow(terminal, args.Line);
            snapshots.Add((row, args.Line.TranslateToString(trimRight: true), args.Buffer, args.Reason,
                           row >= 0));
        };

        terminal.Write("\u001b[2T");

        Assert.Equal(
            new[]
            {
                (3, "bottom", BufferType.Normal, LineExitReason.Scrolled, true),
                (3, "third", BufferType.Normal, LineExitReason.Scrolled, true),
            },
            snapshots);
    }

    [Fact]
    public void PartialScrollRegion_RaisesForTheLineRemovedFromTheRegion()
    {
        var terminal = new Terminal(new TerminalOptions { Rows = 4, Cols = 8 });
        terminal.Buffer.SetScrollRegion(1, 2);
        SetCell(terminal.Buffer.Lines[1]!, "gone");

        string? captured = null;
        terminal.LineExitedViewport += (_, args) => captured = args.Line.TranslateToString(trimRight: true);

        terminal.Buffer.ScrollUp(1);

        Assert.Equal("gone", captured);
    }

    [Fact]
    public void PartialVerticalReverseScroll_RaisesForTheLineRemovedFromTheRegion()
    {
        var terminal = new Terminal(new TerminalOptions { Rows = 4, Cols = 8 });
        terminal.Buffer.SetScrollRegion(1, 2);
        SetCell(terminal.Buffer.Lines[2]!, "gone");

        var snapshots = new List<(int Row, string Text, LineExitReason Reason)>();
        terminal.LineExitedViewport += (_, args) =>
        {
            snapshots.Add((FindViewportRow(terminal, args.Line),
                           args.Line.TranslateToString(trimRight: true), args.Reason));
        };

        terminal.Write("\u001b[T");

        Assert.Equal([(2, "gone", LineExitReason.Scrolled)], snapshots);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NarrowedMargins_DoNotRaiseBecauseNoWholeLineLeaves(bool scrollDown)
    {
        var terminal = new Terminal(new TerminalOptions { Rows = 3, Cols = 8 });
        terminal.Buffer.SetLeftRightMargins(1, 6);
        var count = 0;
        terminal.LineExitedViewport += (_, _) => count++;

        if (scrollDown)
            terminal.Buffer.ScrollDown(1);
        else
            terminal.Buffer.ScrollUp(1);

        Assert.Equal(0, count);
    }

    [Fact]
    public void BufferSwitch_RaisesMeaningfulRowsBeforeBufferChanged()
    {
        var terminal = new Terminal(new TerminalOptions { Rows = 3, Cols = 8 });
        SetCell(terminal.Buffer.Lines[0]!, "first");
        var events = new List<string>();
        string? captured = null;
        BufferType? buffer = null;
        LineExitReason? reason = null;
        terminal.LineExitedViewport += (_, args) =>
        {
            captured = args.Line.TranslateToString(trimRight: true);
            buffer = args.Buffer;
            reason = args.Reason;
            events.Add("exit");
        };
        terminal.BufferChanged += (_, _) => events.Add("changed");

        terminal.SwitchToAltBuffer();

        Assert.Equal("first", captured);
        Assert.Equal(BufferType.Normal, buffer);
        Assert.Equal(LineExitReason.BufferDeactivated, reason);
        Assert.Equal(["exit", "changed"], events);
    }

    [Fact]
    public void AlternateBufferSwitch_RaisesSnapshotsBeforeSwitchingToNormal()
    {
        var terminal = new Terminal(new TerminalOptions { Rows = 3, Cols = 8 });
        terminal.SwitchToAltBuffer();
        terminal.Write("alt0\r\nalt1");

        var snapshots = new List<(int Row, string Text, BufferType Buffer, LineExitReason Reason, bool AlternateActive)>();
        terminal.LineExitedViewport += (_, args) =>
        {
            snapshots.Add((FindViewportRow(terminal, args.Line), args.Line.TranslateToString(trimRight: true),
                           args.Buffer, args.Reason, terminal.IsAlternateBufferActive));
        };

        terminal.SwitchToNormalBuffer();

        Assert.Equal(
            new[]
            {
                (0, "alt0", BufferType.Alternate, LineExitReason.BufferDeactivated, true),
                (1, "alt1", BufferType.Alternate, LineExitReason.BufferDeactivated, true),
            },
            snapshots);
    }

    [Fact]
    public void Reset_FromAlternateBuffer_SnapshotsRowsBeforeClearAndSwitch()
    {
        var terminal = new Terminal(new TerminalOptions { Rows = 3, Cols = 8 });
        terminal.SwitchToAltBuffer();
        terminal.Write("alt0\r\nalt1");

        var snapshots = new List<(int Row, string Text, BufferType Buffer, LineExitReason Reason, bool AlternateActive)>();
        terminal.LineExitedViewport += (_, args) =>
        {
            snapshots.Add((FindViewportRow(terminal, args.Line), args.Line.TranslateToString(trimRight: true),
                           args.Buffer, args.Reason, terminal.IsAlternateBufferActive));
        };

        terminal.Reset();

        Assert.Equal(
            new[]
            {
                (0, "alt0", BufferType.Alternate, LineExitReason.BufferDeactivated, true),
                (1, "alt1", BufferType.Alternate, LineExitReason.BufferDeactivated, true),
            },
            snapshots);
        Assert.False(terminal.IsAlternateBufferActive);
    }

    [Fact]
    public void Reset_OnNormalBuffer_DoesNotRaiseBufferDeactivated()
    {
        var terminal = new Terminal(new TerminalOptions { Rows = 2, Cols = 8 });
        terminal.Write("normal");
        var reasons = new List<LineExitReason>();
        terminal.LineExitedViewport += (_, args) => reasons.Add(args.Reason);

        terminal.Reset();

        Assert.Empty(reasons);
    }

    [Theory]
    [InlineData("styled-blank")]
    [InlineData("wrapped")]
    [InlineData("line-attribute")]
    [InlineData("wide-blank")]
    [InlineData("combining-blank")]
    [InlineData("link")]
    [InlineData("image")]
    [InlineData("mark")]
    [InlineData("sized-run")]
    public void BufferDeactivation_EmitsAnIntermediateBlankAndOneTrailingMetadataRow(string metadata)
    {
        const string esc = "\u001b";
        const string st = esc + "\\";
        const string bel = "\u0007";
        var terminal = new Terminal(new TerminalOptions
        {
            Cols = 12,
            Rows = 4,
            CellWidthPixels = 2,
            CellHeightPixels = 6
        });
        terminal.SwitchToAltBuffer();

        SetCell(terminal.Buffer.Lines[0]!, "head");

        switch (metadata)
        {
            case "styled-blank":
                var styled = AttributeData.Default;
                styled.SetBold(true);
                var styledBlank = new BufferCell(" ", 1, styled);
                terminal.Buffer.Lines[2]!.SetCell(0, ref styledBlank);
                break;
            case "wrapped":
                terminal.Buffer.Lines[2]!.IsWrapped = true;
                break;
            case "line-attribute":
                terminal.Buffer.Lines[2]!.LineAttribute = LineAttribute.DoubleWidth;
                break;
            case "wide-blank":
                var wideBlank = new BufferCell(" ", 2, AttributeData.Default);
                terminal.Buffer.Lines[2]!.SetCell(0, ref wideBlank);
                break;
            case "combining-blank":
                var combiningBlank = new BufferCell(" \u0301", 1, AttributeData.Default);
                terminal.Buffer.Lines[2]!.SetCell(0, ref combiningBlank);
                break;
            case "link":
                terminal.Write($"{esc}[3;1H{esc}]8;;https://example.com{st} {esc}]8;;{st}");
                break;
            case "image":
                terminal.Write($"{esc}[3;1H{esc}P0;1;0q#0;2;100;0;0!2~{st}");
                break;
            case "mark":
                terminal.Write($"{esc}[3;1H{esc}]133;A{bel}");
                break;
            case "sized-run":
                terminal.Write($"{esc}[3;1H{esc}]66;n=1:d=2; {st}");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(metadata), metadata, null);
        }

        var snapshots = new List<(int Row, string Text, bool IsWrapped, LineAttribute LineAttribute,
            string FirstCellContent, int FirstCellWidth, bool StyledBlank, bool HasImages, bool HasMarks,
            bool HasLinks, bool HasSizedRuns)>();
        terminal.LineExitedViewport += (_, args) =>
        {
            var line = args.Line;
            var row = FindViewportRow(terminal, line);
            if (row == 2)
                AssertMetadataSnapshot(metadata, line);

            snapshots.Add((row, line.TranslateToString(trimRight: true), line.IsWrapped, line.LineAttribute,
                           line[0].Content, line[0].Width,
                           line[0].Content == " " && line[0].Attributes != AttributeData.Default,
                           line.HasImages, line.HasMarks, line.HasLinks, line.HasSizedRuns));
        };

        terminal.SwitchToNormalBuffer();

        Assert.Equal(3, snapshots.Count);
        Assert.Equal([0, 1, 2], snapshots.Select(snapshot => snapshot.Row));
        Assert.Equal("head", snapshots[0].Text);
        Assert.Equal(string.Empty, snapshots[1].Text);
        Assert.False(snapshots[1].IsWrapped);
        Assert.Equal(LineAttribute.Normal, snapshots[1].LineAttribute);
        Assert.Equal(metadata == "combining-blank" ? " \u0301" : string.Empty, snapshots[2].Text);
    }

    [Fact]
    public void BufferDeactivation_OmitsDefaultTrailingBlankRows()
    {
        var terminal = new Terminal(new TerminalOptions { Cols = 12, Rows = 4 });
        terminal.SwitchToAltBuffer();
        SetCell(terminal.Buffer.Lines[0]!, "head");

        var rows = new List<int>();
        terminal.LineExitedViewport += (_, args) => rows.Add(FindViewportRow(terminal, args.Line));

        terminal.SwitchToNormalBuffer();

        Assert.Equal([0], rows);
    }

    private static void AssertMetadataSnapshot(string metadata, BufferLine line)
    {
        switch (metadata)
        {
            case "styled-blank":
                Assert.Equal(" ", line[0].Content);
                Assert.NotEqual(AttributeData.Default, line[0].Attributes);
                break;
            case "wrapped":
                Assert.True(line.IsWrapped);
                break;
            case "line-attribute":
                Assert.Equal(LineAttribute.DoubleWidth, line.LineAttribute);
                break;
            case "wide-blank":
                Assert.Equal(" ", line[0].Content);
                Assert.Equal(2, line[0].Width);
                break;
            case "combining-blank":
                Assert.Equal(" \u0301", line[0].Content);
                Assert.Equal(1, line[0].Width);
                break;
            case "link":
                Assert.Equal(" ", line[0].Content);
                Assert.True(line.HasLinks);
                break;
            case "image":
                Assert.Equal(" ", line[0].Content);
                Assert.True(line.HasImages);
                break;
            case "mark":
                Assert.Equal(" ", line[0].Content);
                Assert.True(line.HasMarks);
                break;
            case "sized-run":
                Assert.Equal(" ", line[0].Content);
                Assert.Equal(1, line[0].Width);
                Assert.True(line.HasSizedRuns);
                Assert.False(line.HasImages);
                Assert.False(line.HasMarks);
                Assert.False(line.HasLinks);
                Assert.False(line.IsWrapped);
                Assert.Equal(LineAttribute.Normal, line.LineAttribute);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(metadata), metadata, null);
        }
    }

    private static int FindViewportRow(Terminal terminal, BufferLine line)
    {
        for (var index = 0; index < terminal.Buffer.Lines.Length; index++)
        {
            if (ReferenceEquals(terminal.Buffer.Lines[index], line))
                return index - terminal.Buffer.YBase;
        }

        return -1;
    }

    private static void SetCell(BufferLine line, string content)
    {
        var cell = new BufferCell(content, 1, AttributeData.Default);
        line.SetCell(0, ref cell);
    }
}
