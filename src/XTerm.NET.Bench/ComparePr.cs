using System.Globalization;
using System.Text;
using System.Text.Json;

namespace XTerm.Bench;

/// <summary>
/// Compares repeated <c>ci</c> runs of two builds and says whether the second lost ground.
///
/// <para>Two metrics, held to very different standards on purpose.</para>
///
/// <para><b>Allocation is exact.</b> Bytes allocated for a fixed amount of work is a count, not a
/// measurement: it does not care what else the machine is doing, how warm it is, or how fast the
/// clock is. It is gated hard, because a regression in it is a fact rather than an observation.</para>
///
/// <para><b>Time is not.</b> A shared CI runner has neighbours, no frequency guarantee and no
/// pinning. So each side is run several times, alternating, and compared on the MEDIAN — and the
/// report prints the spread WITHIN each side, which is this job measuring its own noise floor. A
/// threshold below that floor is a false-alarm generator; the number to trust is the one the report
/// puts next to it.</para>
/// </summary>
public static class ComparePr
{
    /// <summary>Allocation may not grow at all, give or take a rounding epsilon.</summary>
    private const double AllocEpsilon = 0.05;

    public static int Run(string[] baseFiles, string[] headFiles, string outputPath, double timeFloor,
                          string label = "")
    {
        var baseRuns = baseFiles.Select(Read).ToArray();
        var headRuns = headFiles.Select(Read).ToArray();

        if (baseRuns.Length == 0 || headRuns.Length == 0)
        {
            Console.Error.WriteLine("compare: need at least one run of each side");
            return 2;
        }

        var baseLib = baseRuns[0].Library;
        var headLib = headRuns[0].Library;

        var md = new StringBuilder();
        var failures = new List<string>();
        var watch = new List<string>();

        // The heading names WHAT was compared. Two of these reports land on the same pull request
        // -- this change against its base, and everything since the last release -- and a reader
        // cannot act on a number without knowing which question it answers.
        md.AppendLine(string.IsNullOrEmpty(label) ? "### Perf comparison" : $"### Perf comparison — {label}");
        md.AppendLine();
        md.AppendLine($"{baseRuns.Length} run(s) of each side, alternating on one machine. "
                    + "Allocation is a count and is gated exactly. Time is a measurement, so its gate "
                    + "is derived from the spread this job just observed in itself rather than fixed "
                    + "in advance.");
        md.AppendLine();
        md.AppendLine("| corpus | bytes/char | gen0/Mchar | ns/char | Δ time | noise | gate |");
        md.AppendLine("|---|---|---|---|---|---|---|");

        var names = baseRuns[0].Corpora.Select(c => c.Name);

        foreach (var name in names)
        {
            var b = Series(baseRuns, name);
            var h = Series(headRuns, name);
            if (b.Count == 0 || h.Count == 0)
                continue;

            var bAlloc = Median(b.Select(x => x.BytesPerChar));
            var hAlloc = Median(h.Select(x => x.BytesPerChar));
            var bGen0 = Median(b.Select(x => x.Gen0PerMchar));
            var hGen0 = Median(h.Select(x => x.Gen0PerMchar));
            var bTime = Median(b.Select(x => x.NsPerChar));
            var hTime = Median(h.Select(x => x.NsPerChar));

            // The spread within each side, which is what this machine's noise looks like today.
            var noise = Math.Max(Spread(b.Select(x => x.NsPerChar)), Spread(h.Select(x => x.NsPerChar)));
            var delta = bTime > 0 ? (hTime - bTime) / bTime : 0;

            if (hAlloc > bAlloc + AllocEpsilon)
                failures.Add($"`{name}` allocation {bAlloc:N2} → {hAlloc:N2} bytes/char");

            if (hGen0 > bGen0 + AllocEpsilon)
                failures.Add($"`{name}` gen0 {bGen0:N2} → {hGen0:N2} per Mchar");

            // The gate this corpus earned on this machine, this run.
            var gate = Math.Max(timeFloor, noise * 3);
            var timeBad = delta > gate;
            if (timeBad)
                failures.Add($"`{name}` time {bTime:N2} → {hTime:N2} ns/char "
                           + $"({Pct(delta, 1)}, past its {Pct(gate, 1)} gate, noise ±{Pct(noise, 1)})");

            // Between the floor and the gate. Not a failure -- the run was too noisy to call it one
            // -- but it should not vanish either, because that band is where a real regression hides
            // on a busy machine. Spread over a handful of runs is a crude noise estimate, and one
            // slow run inflates it, which raises the gate over exactly the thing being looked for.
            var worthALook = !timeBad && delta > timeFloor;
            if (worthALook)
                watch.Add($"`{name}` time {bTime:N2} → {hTime:N2} ns/char "
                        + $"({Pct(delta, 1)}, under its {Pct(gate, 1)} gate but over the {Pct(timeFloor, 0)} floor)");

            var mark = timeBad ? " ⚠️" : worthALook ? " 👀" : "";
            md.AppendLine($"| {name} "
                        + $"| {bAlloc:N2} → {hAlloc:N2} "
                        + $"| {bGen0:N2} → {hGen0:N2} "
                        + $"| {bTime:N2} → {hTime:N2} "
                        + $"| {Signed(delta)}{mark} "
                        + $"| ±{Pct(noise, 0)} "
                        + $"| {Pct(gate, 0)} |");
        }

        md.AppendLine();
        md.AppendLine($"Each corpus is gated at `max({Pct(timeFloor, 0)}, 3 × its own noise)`. A wide noise "
                    + "column means this runner was busy and the timing half of the table should be "
                    + "read as advisory; the allocation half is exact either way.");
        md.AppendLine();
        md.AppendLine("<details><summary>assemblies measured</summary>");
        md.AppendLine();
        md.AppendLine($"- base: `{baseLib}`");
        md.AppendLine($"- head: `{headLib}`");
        md.AppendLine();
        md.AppendLine("</details>");

        if (baseLib == headLib)
        {
            md.AppendLine();
            md.AppendLine("> ⚠️ **Both sides loaded the same build** — identical module version id. "
                        + "The comparison measured one library twice and means nothing.");
            failures.Add("both sides loaded the same build (identical MVID)");
        }

        if (watch.Count > 0)
        {
            md.AppendLine();
            md.AppendLine("**Worth a look** — over the floor, under this run's gate, so not failed:");
            md.AppendLine();
            foreach (var w in watch)
                md.AppendLine($"- {w}");
            md.AppendLine();
            md.AppendLine("Re-run on a quieter machine, or with more `--chars`, to tell a real change "
                        + "from a busy runner. Both narrow the noise column, which tightens the gate.");
        }

        if (failures.Count > 0)
        {
            md.AppendLine();
            md.AppendLine("**Regressions**");
            md.AppendLine();
            foreach (var f in failures)
                md.AppendLine($"- {f}");
        }

        var text = md.ToString();
        File.WriteAllText(outputPath, text);
        Console.WriteLine(text);

        return failures.Count > 0 ? 1 : 0;
    }

    /// <summary>
    /// Percentages formatted by hand, in the invariant culture.
    /// </summary>
    /// <remarks>
    /// "P0" renders as "16 %" where there is no ICU -- which is exactly the case on the CI runner
    /// this report is written for -- and as "16%" on a developer machine. A report that reads
    /// differently depending on where it ran is a report nobody can diff.
    /// </remarks>
    private static string Pct(double value, int decimals) =>
        (value * 100).ToString("N" + decimals, CultureInfo.InvariantCulture) + "%";

    private static string Signed(double value) =>
        (value >= 0 ? "+" : "") + Pct(value, 1);

    private static Report Read(string path) =>
        JsonSerializer.Deserialize<Report>(File.ReadAllText(path))
        ?? throw new InvalidDataException($"could not read {path}");

    private static List<CorpusResult> Series(Report[] runs, string name) =>
        runs.SelectMany(r => r.Corpora).Where(c => c.Name == name).ToList();

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        if (sorted.Length == 0) return 0;
        return sorted.Length % 2 == 1
            ? sorted[sorted.Length / 2]
            : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
    }

    /// <summary>Spread as a fraction of the median: what this side varied by, run to run.</summary>
    private static double Spread(IEnumerable<double> values)
    {
        var v = values.ToArray();
        if (v.Length < 2) return 0;
        var median = Median(v);
        return median > 0 ? (v.Max() - v.Min()) / median : 0;
    }
}
