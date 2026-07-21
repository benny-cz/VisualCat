# ADR 0011: ADB overflow, reconnect, and loss

**Decision:** Capture stdout in bounded chunks, persist raw bytes before analysis, cap stderr, terminate the full process tree, and use bounded exponential reconnect.

**Alternatives:** Unbounded queues hide lag until memory exhaustion.

**Consequences and validation:** Device/OS loss is reported rather than assumed absent. Soak evidence gates release claims.
