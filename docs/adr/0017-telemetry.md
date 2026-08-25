# ADR 0017: Telemetry

**Decision:** No telemetry ships enabled and no log content is transmitted. Any future telemetry requires explicit opt-in and a separate privacy review.

**Alternatives:** Default analytics create unacceptable risk for sensitive logs.

**Consequences and validation:** Current builds contain no network reporting endpoint.

Amended 2026-08-25: a Google Play build asks the installed Play Store whether a newer VisualCat exists. That is an IPC call into another app on the device, carries no identifier and no log content, and reaches no VisualCat endpoint, so it does not contradict this decision — see ADR 0019, which records where the boundary sits and why. This ADR continues to govern anything that would report on the user or their data.
