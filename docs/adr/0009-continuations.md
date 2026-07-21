# ADR 0009: Continuations and unknown lines

**Decision:** Long-format state explicitly owns body lines. Other unmatched lines remain unknown unless a declared grammar proves continuation.

**Alternatives:** Attaching every unknown line to the prior entry hides malformed evidence.

**Consequences and validation:** Source-order records cover each physical line while logical long entries retain a combined raw span.
