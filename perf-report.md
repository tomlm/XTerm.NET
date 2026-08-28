### Perf comparison

3 run(s) of each side, alternating on one machine. Allocation is a count and is gated exactly. Time is a measurement, so its gate is derived from the spread this job just observed in itself rather than fixed in advance.

| corpus | bytes/char | gen0/Mchar | ns/char | Δ time | noise | gate |
|---|---|---|---|---|---|---|
| scroll-ascii | 0.00 → 0.00 | 0.00 → 0.00 | 1.61 → 1.60 | -0.7% | ±2% | 7% |
| sgr-churn | 0.00 → 0.00 | 0.00 → 0.00 | 4.07 → 4.17 | +2.4% | ±12% | 35% |
| truecolor | 0.00 → 0.00 | 0.00 → 0.00 | 4.36 → 4.48 | +2.9% | ±7% | 22% |
| alt-redraw | 0.00 → 0.00 | 0.00 → 0.00 | 5.55 → 5.77 | +4.0% | ±4% | 11% |
| unicode | 7.66 → 7.66 | 0.91 → 0.91 | 12.68 → 12.96 | +2.2% | ±5% | 16% |
| flood | 0.00 → 0.00 | 0.00 → 0.00 | 40.69 → 41.00 | +0.8% | ±8% | 23% |

Each corpus is gated at `max(5%, 3 × its own noise)`. A wide noise column means this runner was busy and the timing half of the table should be read as advisory; the allocation half is exact either way.

<details><summary>assemblies measured</summary>

- base: `XTerm.NET 2.0.0.0 mvid:8708bc7c-5073-4330-922c-315a9051c401`
- head: `XTerm.NET 2.0.0.0 mvid:5fe8c84d-a489-47cd-8bbf-19c8ebf6bef2`

</details>
