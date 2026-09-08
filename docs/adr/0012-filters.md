# ADR 0012: Filter semantics and generations

**Decision:** One immutable `FilterSpec` and stable SHA-256 fingerprint feeds every analytical query. Results carry session, snapshot, filter, and query generations.

**Alternatives:** Independent view filters cause irreconcilable counts and stale races.

**Consequences and validation:** Selected-cell counts are tested against details and presentation rejects superseded results.

## Facet counts omit their own dimension

**Decision:** A facet value's count is the number of entries matching that value under the
whole session and every *other* dimension of the current filter, with the value's own
dimension — both its included and its excluded constraints — removed. The active time filter
is one of the other constraints and still applies; the viewport does not. `QueryFacetValues`
and the summary pane share one implementation of this rule, so a value cannot be counted one
way in the pane and another way in **Find…**.

**Alternatives:** Counting each value under the complete filter, its own dimension included,
would make every neutral value in an already-filtered group read zero — which removes the one
thing those counts are for: choosing an alternative or an additional value to OR into the
filter. It would also contradict the existing query tests.

**Consequences and validation:** For an included value A and a neutral value B in the same
group, B can report a positive count while the entry list shows only A. That is intended and
is covered by tests. `Statistics.TotalMatching` and a facet count are therefore different
numbers with different scopes, and neither is a promise about the other. The pane says so
where the reader can see it: `COUNTS · OTHER FILTERS`, expanded as *Counts use the whole
session and your other filters. Each group ignores its own filters so you can add
alternatives.*

**Requested and applied filter state:** `Filter` is what the reader has asked for; a query
publishes `AppliedFilter`, `AppliedQueryIdentity` and its data together. While they differ,
`IsQueryPending` is true, the previous result stays readable and identifiable, and actions
whose advertised counts depend on the pending result — export, save view, exact search
navigation — are refused with a reason rather than acting on numbers that are about to
change. Every filter mutation composes against the latest requested `Filter`, so two rapid
edits compose rather than one replacing the other. A failed request restores `Filter` to
`AppliedFilter` and keeps the rejected input editable.
