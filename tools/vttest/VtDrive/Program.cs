using System.Text;
using Porta.Pty;
using XTerm;
using XTerm.Options;

// Drives vttest against a headless XTerm.NET and dumps what the emulator ended up showing.
//
//   VtDrive <keys> ...        each argument is one thing to send, then wait and dump
//
// A key of "-" means "send nothing, just wait and dump again", for screens that paint in stages.
// \r is written as CR.

var script = args.Length > 0 ? args : ["-"];

var terminal = new Terminal(new TerminalOptions { Cols = 80, Rows = 24 });

// The window reports are OPT-IN, as they are in xterm, so a harness on defaults sees silence and
// mistakes it for a missing feature. A host that embeds this terminal turns on the ones it can
// answer -- the demo app does exactly this -- so the harness does too, or menu 11.8.8/11.8.9 test
// the default configuration rather than the emulator.
var w = terminal.Options.WindowOptions;
w.GetWinPosition = w.GetWinSizePixels = w.GetWinSizeChars = true;
w.GetScreenSizePixels = w.GetCellSizePixels = w.GetWinState = true;
w.GetIconTitle = w.GetWinTitle = true;
w.SetWinPosition = w.SetWinSizePixels = w.SetWinSizeChars = true;
w.RaiseWin = w.LowerWin = w.RefreshWin = w.RestoreWin = true;
w.MaximizeWin = w.MinimizeWin = w.FullscreenWin = w.RequestAttention = true;

var options = new PtyOptions
{
    Name = "xterm",
    Cols = 80,
    Rows = 24,
    Cwd = Environment.CurrentDirectory,
    App = "wsl.exe",
    CommandLine = ["-e", "/usr/bin/vttest"],
    VerbatimCommandLine = true,
    Environment = new Dictionary<string, string> { ["TERM"] = "xterm" },
};

using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
var connection = await PtyProvider.SpawnAsync(options, cts.Token);

// The replies are the point for the report tests: vttest asks, and what it prints depends on what
// the terminal answered. Without this the emulator is a screen with no voice and every report test
// times out or reports nothing.
// DECCOLM resizes the emulator's grid, and a real terminal tells the pty so the application
// learns it has 132 columns. Without this the emulator is 132 wide while vttest still believes 80,
// and every screen after the 132-column tests is drawn at the wrong width -- which looks exactly
// like a wrapping or clearing bug in the emulator and is not one.
terminal.Resized += (_, _) =>
{
    try { connection.Resize(terminal.Cols, terminal.Rows); }
    catch (Exception ex) { Console.Error.WriteLine($"resize failed: {ex.Message}"); }
};

terminal.DataReceived += (_, e) =>
{
    var bytes = Encoding.UTF8.GetBytes(e.Data);
    connection.WriterStream.Write(bytes, 0, bytes.Length);
    connection.WriterStream.Flush();
};

using var raw = File.Create(Path.Combine(Path.GetTempPath(), "vtraw.bin"));

var pump = Task.Run(async () =>
{
    var buffer = new byte[65536];
    while (!cts.IsCancellationRequested)
    {
        int read;
        try { read = await connection.ReaderStream.ReadAsync(buffer, cts.Token); }
        catch (OperationCanceledException) { break; }
        if (read <= 0) break;
        lock (terminal) terminal.Write(buffer.AsSpan(0, read));
        raw.Write(buffer, 0, read);
        raw.Flush();
    }
});

foreach (var step in script)
{
    if (step != "-")
    {
        var keys = step.Replace("\\r", "\r").Replace("\\e", "");
        var bytes = Encoding.UTF8.GetBytes(keys);
        connection.WriterStream.Write(bytes, 0, bytes.Length);
        connection.WriterStream.Flush();
    }

    // Wait for the screen to STOP changing rather than for a fixed time. vttest paints some
    // screens in stages, and a fixed delay dumps one emulator mid-paint while the other has
    // finished -- which shows up as differences that are really just the two being on different
    // screens.
    string Snapshot()
    {
        lock (terminal)
            return string.Join("|", Enumerable.Range(0, 24).Select(terminal.GetLine));
    }

    var previous = Snapshot();
    var stableFor = 0;
    for (var waited = 0; waited < 8000 && stableFor < 600; waited += 200)
    {
        await Task.Delay(200);
        var now = Snapshot();
        stableFor = now == previous ? stableFor + 200 : 0;
        previous = now;
    }

    Console.WriteLine($"########## after {(step == "-" ? "(wait)" : step.Replace("\r", "<CR>"))} ##########");
    lock (terminal)
    {
        for (var row = 0; row < 24; row++)
        {
            var rawLine = terminal.Buffer.Lines[row]?.TranslateToString(false) ?? string.Empty;
            var line = terminal.GetLine(row);
            var attr = terminal.Buffer.Lines[row]?.LineAttribute ?? XTerm.Buffer.LineAttribute.Normal;
            if (attr != XTerm.Buffer.LineAttribute.Normal) Console.WriteLine($"   row {row} lineAttribute={attr}");
            Console.WriteLine($"{row,2}|{line}");
        }
    }
    Console.WriteLine();
}

connection.Dispose();
