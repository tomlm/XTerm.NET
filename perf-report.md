### Perf comparison

3 run(s) of each side, alternating on one machine. Allocation is a count and is gated exactly. Time is a measurement, so its gate is derived from the spread this job just observed in itself rather than fixed in advance.

| corpus | bytes/char | gen0/Mchar | ns/char | Δ time | noise | gate |
|---|---|---|---|---|---|---|
| scroll-ascii | 0.00 → 0.00 | 0.00 → 0.00 | 1.60 → 1.60 | -0.1% | ±3% | 9% |
| sgr-churn | 0.00 → 0.00 | 0.00 → 0.00 | 4.06 → 4.11 | +1.2% | ±2% | 7% |
| truecolor | 0.00 → 0.00 | 0.00 → 0.00 | 4.31 → 4.31 | -0.0% | ±1% | 5% |
| alt-redraw | 0.00 → 0.00 | 0.00 → 0.00 | 5.54 → 5.62 | +1.3% | ±1% | 5% |
| unicode | 7.66 → 7.66 | 0.91 → 0.91 | 12.70 → 12.83 | +1.0% | ±2% | 7% |
| flood | 0.00 → 0.00 | 0.00 → 0.00 | 40.26 → 41.35 | +2.7% | ±3% | 10% |

Each corpus is gated at `max(5%, 3 × its own noise)`. A wide noise column means this runner was busy and the timing half of the table should be read as advisory; the allocation half is exact either way.

<details><summary>assemblies measured</summary>

- base: `XTerm.NET 2.0.0.0 mvid:8708bc7c-5073-4330-922c-315a9051c401`
- head: `XTerm.NET 2.0.0.0 mvid:05e1c342-5eab-4353-8583-e945b774fb68`

</details>
