using System.Diagnostics;
using BenchmarkDotNet.Running;
using XTerm;
using XTerm.Bench;
using XTerm.Options;

// The CI perf gate swaps a BASE-side XTerm.NET.dll into this HEAD-built output, and the base
// may depend on packages this build's deps.json has never heard of -- the host only probes the
// deps.json trusted list, so a staged assembly beside the app still fails to load. Probe the
// app directory for anything unknown: dependency drift between the two sides, in either
// direction, then never breaks the harness.
System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (context, name) =>
{
    var candidate = Path.Combine(AppContext.BaseDirectory, name.Name + ".dll");
    return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
};

// Two modes, because they answer different questions.
//
//   bench  — BenchmarkDotNet: how long, and how many bytes allocated, per stream. Hard numbers.
//   soak   — a tight parse loop that runs long enough for `dotnet-trace` to sample it. Answers
//            "which methods", which a throughput number cannot.
//   alloc  — a quick unattended allocation census: bytes and GC collections per character, with no
//            BenchmarkDotNet ceremony. Useful as a fast check while editing.

var mode = args.Length > 0 ? args[0].ToLowerInvariant() : "bench";

switch (mode)
{
    case "bench":
        BenchmarkRunner.Run<ParseBenchmarks>();
        return 0;

    case "soak":
        return Soak(args);

    case "alloc":
        return Alloc(args);

    case "bytes":
        ByteEntryProbe.Run(double.Parse(ArgOr(args, "--seconds", "2")));
        return 0;

    case "ci":
        return CiProbe.Run(
            outputPath: ArgOr(args, "--out", "perf.json"),
            targetChars: long.Parse(ArgOr(args, "--chars", "300000000")),
            warmChars: long.Parse(ArgOr(args, "--warm-chars", "60000000")));

    case "compare":
        return ComparePr.Run(
            baseFiles: Files(args, "--base"),
            headFiles: Files(args, "--head"),
            outputPath: ArgOr(args, "--out", "perf-report.md"),
            // Matches the TIME_FLOOR the CI workflow passes explicitly, so a local compare and
            // the gate agree about what counts as a regression.
            timeFloor: double.Parse(ArgOr(args, "--time-floor", "0.04")),
            label: ArgOr(args, "--label", ""));

    case "layout":
        CellLayoutProbe.Run();
        return 0;

    case "unicode":
        UnicodeProbe.Run();
        return 0;

    case "flood":
        FloodProbe.Run();
        return 0;

    case "width":
        WidthProbe.Run(int.Parse(ArgOr(args, "--millions", "20")));
        return 0;

    default:
        Console.Error.WriteLine("Usage: <bench|soak|alloc|ci|compare|layout|unicode|flood|width|bytes>");
        Console.Error.WriteLine("  ci      --out FILE [--chars N] [--warm-chars N]");
        Console.Error.WriteLine("  compare --base F [F...] --head F [F...] [--out FILE] [--time-floor F] [--label TEXT]");
        return 2;
}

static (string[] Chunks, int Chars) Load(string corpus)
{
    const int cols = 240, rows = 67;
    var dir = Path.Combine(AppContext.BaseDirectory, "corpus");
    CorpusGenerator.GenerateAll(dir, targetBytes: 400_000, cols: cols, rows: rows);

    var text = File.ReadAllText(Path.Combine(dir, corpus + ".vt"));
    var chunks = new List<string>();
    for (var i = 0; i < text.Length; i += 4096)
        chunks.Add(text.Substring(i, Math.Min(4096, text.Length - i)));
    return (chunks.ToArray(), text.Length);
}

/// <summary>Every value after <paramref name="name"/> until the next --flag.</summary>
static string[] Files(string[] args, string name)
{
    var i = Array.IndexOf(args, name);
    if (i < 0) return Array.Empty<string>();

    var files = new List<string>();
    for (var k = i + 1; k < args.Length && !args[k].StartsWith("--"); k++)
        files.Add(args[k]);
    return files.ToArray();
}

static string ArgOr(string[] args, string name, string fallback)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
}

static int Soak(string[] args)
{
    var corpus = ArgOr(args, "--corpus", "scroll-ascii");
    var seconds = int.Parse(ArgOr(args, "--seconds", "30"));
    var (chunks, chars) = Load(corpus);

    Console.WriteLine($"Soaking '{corpus}' for {seconds}s — pid {Environment.ProcessId}");
    Console.WriteLine($"Attach:  dotnet-trace collect -p {Environment.ProcessId} --duration 00:00:{seconds - 5:D2} -o trace.nettrace");

    var terminal = new Terminal(new TerminalOptions { Cols = 240, Rows = 67 });
    var sw = Stopwatch.StartNew();
    long charsDone = 0;

    while (sw.Elapsed.TotalSeconds < seconds)
    {
        foreach (var c in chunks)
            terminal.Write(c);
        charsDone += chars;
    }

    Console.WriteLine($"{charsDone / 1024.0 / 1024.0:N1} Mchars in {sw.Elapsed.TotalSeconds:N1}s "
                    + $"= {charsDone / 1024.0 / 1024.0 / sw.Elapsed.TotalSeconds:N1} Mchar/s");
    return 0;
}

static int Alloc(string[] args)
{
    var seconds = double.Parse(ArgOr(args, "--seconds", "2"));

    Console.WriteLine($"{"corpus",-14} {"MiB/s",8} {"ns/char",8} {"bytes/char",11} {"gen0/Mchar",11}");
    Console.WriteLine(new string('-', 60));

    foreach (var spec in CorpusGenerator.Specs)
    {
        var (chunks, chars) = Load(spec.Name);
        var terminal = new Terminal(new TerminalOptions { Cols = 240, Rows = 67 });

        // Warm to convergence before measuring: throughput climbs for several passes as tiered
        // compilation promotes the parser's hot methods, and timing before that measures the JIT.
        var previous = double.MaxValue;
        for (var pass = 0; pass < 12; pass++)
        {
            var w = Stopwatch.StartNew();
            foreach (var c in chunks) terminal.Write(c);
            w.Stop();
            var now = w.Elapsed.TotalMilliseconds;
            if (previous < double.MaxValue && Math.Abs(now - previous) / previous < 0.05) break;
            previous = now;
        }

        var beforeAlloc = GC.GetTotalAllocatedBytes(precise: true);
        var beforeGen0 = GC.CollectionCount(0);
        var sw = Stopwatch.StartNew();
        long charsDone = 0;

        while (sw.Elapsed.TotalSeconds < seconds)
        {
            foreach (var c in chunks) terminal.Write(c);
            charsDone += chars;
        }
        sw.Stop();

        var allocated = GC.GetTotalAllocatedBytes(precise: true) - beforeAlloc;
        var gen0 = GC.CollectionCount(0) - beforeGen0;

        var nsPerChar = sw.Elapsed.TotalMilliseconds * 1_000_000.0 / charsDone;
        var bytesPerChar = (double)allocated / charsDone;
        var mibPerSec = charsDone / 1024.0 / 1024.0 / sw.Elapsed.TotalSeconds;
        var gen0PerMchar = gen0 / (charsDone / 1_000_000.0);

        Console.WriteLine($"{spec.Name,-14} {mibPerSec,8:N1} {nsPerChar,8:N1} {bytesPerChar,11:N1} {gen0PerMchar,11:N1}");
    }

    return 0;
}
