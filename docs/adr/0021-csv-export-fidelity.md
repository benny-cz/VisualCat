# ADR 0021: CSV export is exact data, not a spreadsheet document

**Context:** `ExportService.ExportNormalizedCsvAsync` quotes fields that contain a comma,
a quote or a newline, which is correct RFC 4180 escaping and preserves the exported value
exactly. Quoting is not, however, a defence against spreadsheet formula interpretation. A log
line whose message begins with `=`, `+`, `-`, `@`, or a leading tab/carriage return followed
by one of those, may be evaluated as a formula by spreadsheet software when the file is
opened directly rather than imported as text. Log content is attacker-influenced in exactly
the cases VisualCat exists for: an app under test writes whatever it writes, and a capture
records it.

The three available contracts were: keep CSV exact and document the hazard; add a separately
named spreadsheet-safe export mode alongside the exact one; or make a defended form the
default with an explicit exact mode.

**Decision:** CSV export remains an **exact data export**. No field is prefixed, quoted
differently, or otherwise rewritten to change how a spreadsheet application interprets it.
The hazard is documented where a reader meets the format — [`SECURITY.md`](../SECURITY.md)
and [`CLI.md`](../CLI.md) — with the instruction to import an untrusted log's CSV as text
rather than opening it directly.

**Why:** [`ARCHITECTURE.md`](../../ARCHITECTURE.md) states the product's invariant as "every
source byte remains attributable; parsing never invents or drops raw evidence silently", and
[ADR 0020](0020-verified-raw-evidence.md) has just spent an implementation making raw
evidence provably exact. A defended default would put a rewritten field in front of a reader
who asked for the data, and the rewrite is invisible in the file itself — the one property
that makes an evidence export worth having is that what it contains is what was captured. A
second, differently-shaped CSV would also be a second thing to explain, and the wrong one is
silently chosen by whoever forgets which is which.

The exactness is not absolute in the other direction either: this decision covers what
VisualCat writes, not what a spreadsheet does with it. Naming the boundary is the point.

**Scope of the claim:** This ADR is a product-contract decision, not the result of a security
review. No spreadsheet application was tested, no attacker model was established, and no
fuzzing was done. It states which contract VisualCat commits to and where the boundary is
documented. Revisiting it — for instance to add an explicitly named spreadsheet-safe mode —
requires the focused review this one did not perform: name the target applications, the
prefix and whitespace rules, the affected columns, and a corpus covering each prefix
alongside ordinary negative numbers, and assert that the exact mode stays byte-for-byte
round-trippable.

**Consequences:** `ExportService`'s CSV writers are unchanged and remain the reference for
"what VisualCat captured". A reader who opens an export of an untrusted log directly in a
spreadsheet is outside the contract, and the documentation says so rather than leaving it to
be discovered. The templates and statistics CSV exports carry log-derived text as well and
are covered by the same contract.
