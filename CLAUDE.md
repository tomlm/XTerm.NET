# Working on XTerm.NET

A C# reimplementation of xterm.js. Everything below was learned by getting it wrong first; the
cost of each mistake is recorded so the next person can decide whether to repeat it.

## Order conditions by what actually runs, not by what you are thinking about

When adding a case to a condition chain on the parse or print path, put it where its
**frequency** belongs, not at the front where the change is on your mind. Writing a
correctness fix, the new condition is what you are holding in your head, and it lands first
by default — in front of the case that runs a million times a second.

Four instances landed in a single PR before being caught:

| the new case | what it displaced | what it cost |
| --- | --- | --- |
| private markers `0x3C-0x3F` | digits in `CsiParam` | every digit of `CSI 38;2;R;G;B m` paid two compares for a branch that fires on malformed input only |
| `CAN`/`SUB` | `ESC` in the control dispatch | ESC opens every escape sequence; CAN and SUB essentially never arrive |
| `_collect.Clear()` entering Escape | nothing — it was unconditional | the builder is already empty for every sequence with no intermediate, which is nearly all |
| `code != 0x7F` in Ground | joined `code >= 0x20` | that guard could not fail: the control block above returns for everything under 0x20 |

**The check before committing a hot-path change:** for each condition you added, ask which
branch a normal stream takes, and count the compares it now pays to get there. If the answer
is "it pays for my new case first", swap them.

## Guard at the CALL, not inside the helper

A guard inside a method still costs the call, and a method too large to inline costs it every
time. This has been relearned at least four times here, each time visible in the bench:

- the placeholder-diacritic test in `Print` — 12% of `alt-redraw`
- the autowrap test in `Print` — 9%
- `NoteLinkRun` — 12%
- the wide-cell repair inside `SetSingleWidthRun` — 8% of `scroll-ascii`, and this one was
  subtler: the guard itself was one bool read, but adding the call pushed the *containing*
  method past the JIT's inlining budget. One bool read cannot cost 8%; not being inlined can.

Corollary: a property that returns a struct hands back a **copy**. `line[x].Width` copies a
whole `BufferCell` to read one field; `line.GetWidth(x)` does not.

## Watch for expensive operations hiding behind ordinary syntax

- `ConcurrentDictionary.Count` takes every bucket lock and walks them. It was used as a cap
  check on the print path and cost 1.5% of the `unicode` corpus.
- Integer division is tens of cycles. A per-digit `(Max - digit) / 10` overflow guard measured
  as nothing locally and **+4.6% on truecolor in CI** — division latency varies that much
  between CPUs.
- `MemoryStream` grows by doubling. Checking `Length > ceiling` *after* writing means the
  allocation you are refusing has already happened; check `Length + read > ceiling` before.
- `WrapLimit()` is not a field read — it asks whether the cursor is inside the margin columns.
  Call it once per printed character and reuse the value.

## Measuring

```
dotnet run --project src/XTerm.NET.Bench -c Release -- soak --corpus truecolor --seconds 12
```

Corpora: `scroll-ascii`, `sgr-churn`, `truecolor`, `alt-redraw`, `unicode`, `flood`. The
`ci` and `compare` modes are what the workflow uses for the gate.

- **Alternate sides.** Running all of one side then all of the other lets thermal drift pick
  the winner.
- **Repeated soaks of the same build vary 1-2%.** A single pair proves nothing; direction
  repeated across pairs is the signal.
- **CI is the arbiter**, not a developer machine — see the division above.
- **Commit before benching.** The bench script flips the working tree between branches and
  will abort halfway on a dirty tree, leaving the checkout on the wrong branch.
- **Never edit a running shell script.** Bash reads scripts by byte offset as it executes, so
  an edit mid-run corrupts the running instance.
- **Two benchmarks at once measure each other**, not the code. Run them one at a time.

## The perf gate

Two budgets, both in `.github/workflows/BuildAndRunTests.yml` at the top of the `perf` job:

- `TIME_FLOOR` — this change against its base. Each corpus fails at `max(floor, 3 × its own
  measured noise)`.
- `BASELINE_FLOOR` — this change against the latest release tag, resolved at run time. A
  per-PR gate bounds each change and nothing about the trajectory: 4% allowed five times
  compounds to 21.7%, with every one of those PRs green on the way past.

A gate that cannot run **fails the job** rather than skipping. A skipped gate reports green,
which is indistinguishable from one that ran and passed.

## Correctness references

- The parser follows the VT500 state diagram; `xterm.js` and vt100.net are the references when
  behaviour is in question.
- Character width follows **python wcwidth**, which is what `ucs-detect` measures terminals
  against. `Common/WidthTables.Generated.cs` is generated from its tables by
  `scripts/generate-width-tables.py`, and `WidthTableParityTests` replays every codepoint
  against it, so the two cannot drift apart.
- Width deliberately diverges from Terminal Unicode Core in one place: a cluster is one
  **cell** but its width follows wcwidth arithmetic (a spacing mark adds a column, conjunct
  letters keep theirs, capped at 2), because that is what applications lay out on. kitty makes
  the same trade.
- `ucs-detect` is the end-to-end check: `ucs-detect --test-only narrow` and `--test-only lang`
  against the demo terminal.

## Things that have bitten twice

- **Check whether an existing test already encodes the behaviour you are "fixing."** Three
  audit findings were contradicted by tests that were right: zero-row `Resize` (a host reports
  zero before layout), writes after `Dispose` (a pty thread races teardown; throwing kills the
  read loop), and options identity.
- **A test written next to a fix can encode the bug instead of the behaviour.** The
  at-capacity branch of `CircularList.Splice` was corrected from "appends" to "one position
  off", and shipped with a test asserting the off-by-one — green, and wrong. When a method has
  two branches for the same operation, the other branch is the oracle: `Splice(1, 0, "X")` on
  `[a,b,c,d]` gives `a,X,b,c,d` with room to grow, so at capacity it must give the last four of
  that, `X,b,c,d`. Assert the two against each other rather than writing down what the code did.
- **`AttributeData` is a struct.** Turning a field holding one into a property means every
  `SetFgColor`-style call mutates the copy the getter returned. That silently broke 76 tests.
- **Escape characters in generated code.** A literal ESC byte written into a source file or a
  shell command corrupts it, and the tooling rejects control characters outright. Emit the
  six-character escape sequence and let the compiler decode it; in tests prefer
  ((char)0x1B).ToString(), which nothing can mangle in transit.
- **Backticks in commit messages and PR bodies.** A backticked expression inside a shell
  heredoc gets executed and leaves a hole where the text was. Write the message to a file
  first, or read it back after committing.
