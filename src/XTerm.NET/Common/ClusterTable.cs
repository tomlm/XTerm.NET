using System.Collections.Concurrent;

namespace XTerm.Common;

/// <summary>
/// Interns the multi-codepoint text of grapheme clusters, so a cell can refer to one by id.
///
/// A cell's text is a single codepoint almost always, and that is derivable from an int. The
/// exceptions — a base character plus combining marks, a ZWJ emoji sequence, a charset mapping that
/// expands to more than one codepoint — need real string storage. Putting that string in the cell
/// would put a GC reference in every cell of the buffer, which is precisely what this exists to
/// avoid: measured, a 240-cell fill costs 238 ns with a reference in the struct and 75 ns without,
/// and a single cell assignment 0.88 ns against 0.35 ns, because the runtime must emit a write
/// barrier for each one.
///
/// Ids are process-wide and stable. Identical cluster text interns to one id, so the table holds one
/// entry per distinct sequence rather than one per cell — a terminal sees a bounded handful of
/// distinct emoji sequences even across a long session.
///
/// Reads are lock-free, which matters because rendering resolves cluster text per frame.
/// </summary>
internal static class ClusterTable
{
    /// <summary>Id 0 means "no cluster"; the cell's codepoint is its whole content.</summary>
    public const int None = 0;

    /// <summary>
    /// Ceiling on distinct interned clusters. The class comment above is right about benign
    /// output -- a session sees a bounded handful of distinct sequences -- but a program on the
    /// pty chooses what it prints, and a base character with combining marks generates unbounded
    /// distinct strings for as long as it cares to send them. Nothing evicts, because live cells
    /// hold ids and scrollback keeps them for the session, so the only safe answer is to stop
    /// growing.
    ///
    /// Past the cap, Intern returns <see cref="None"/> and the cell falls back to its base
    /// codepoint: combining marks stop being shown, which is wrong on screen but bounded in
    /// memory. That trade only ever applies to a stream that has already produced this many
    /// distinct clusters, which no real session does.
    /// </summary>
    private const int MaxEntries = 100_000;

    private static readonly ConcurrentDictionary<int, string> ById = new();
    private static readonly ConcurrentDictionary<string, int> Ids = new(StringComparer.Ordinal);

    private static int _next = None;

    /// <summary>Id for <paramref name="text"/>, allocating one if this is the first time it is seen.</summary>
    public static int Intern(string text)
    {
        if (string.IsNullOrEmpty(text))
            return None;

        if (Ids.TryGetValue(text, out var existing))
            return existing;

        // Checked before the id is allocated, so a saturated table costs a lookup and nothing else.
        if (Ids.Count >= MaxEntries)
            return None;

        var id = Interlocked.Increment(ref _next);
        ById[id] = text;

        // Two threads interning the same new text each allocate an id; only one can win GetOrAdd.
        var winner = Ids.GetOrAdd(text, id);

        // Take the loser's entry back out. Nobody can be holding it -- it was never returned to
        // anyone -- and leaving it would keep a string alive for the life of the process for no
        // reader. Rare, but a leak that never reclaims is still a leak.
        if (winner != id)
            ById.TryRemove(id, out _);

        return winner;
    }

    /// <summary>Text for <paramref name="id"/>, or empty for <see cref="None"/> or an unknown id.</summary>
    public static string Get(int id) =>
        id != None && ById.TryGetValue(id, out var text) ? text : string.Empty;
}
