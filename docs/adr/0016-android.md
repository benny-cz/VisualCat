# ADR 0016: Android companion

**Decision:** Use Avalonia's Android activity lifetime with a reduced single-session touch UI and an on-device `logcat` process source.

**Alternatives:** A separate native UI would duplicate presentation behavior.

**Consequences and validation:** The app labels own-app versus granted full-device scope and uses app-private session storage.
