# ADR 0018: Reference benchmarks and gates

**Decision:** Use the seeded generator plus checked sample corpora on a documented NVMe desktop. Record distributions for ingest, first snapshot, reopen, heat map, search, cancellation, RSS, and live lag.

**Alternatives:** One-off stopwatch claims are not reproducible.

**Consequences and validation:** Correctness always gates. Performance initially flags statistically meaningful regressions near 20% until baselines stabilize.

The first Release measurements on 2026-07-19 disproved the plan's provisional 1.5 M lines/s full-pipeline gate. The complete safe pipeline, including deterministic mining, sustained 74,607 lines/s on a seeded one-million-line corpus on a Ryzen 9 5900X and Samsung 990 PRO. The supplied 118,886-entry corpus sustained 31,123 lines/s and met the 2,000-column heat-map target at 6.76 ms; the million-entry query averaged 27.38 ms.

For the initial release, the revised gates are 60 K lines/s and 35 ms on the seeded million-line corpus, an 8 ms query gate on the supplied corpus, and a 20% controlled-agent regression threshold. These gates make measured regressions visible without representing the original aspirational number as achieved. Exact commands, environment, and results are recorded in `docs/PERFORMANCE.md`.
