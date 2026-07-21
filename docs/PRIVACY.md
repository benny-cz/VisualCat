# Privacy

Logcat can contain credentials, tokens, personal data, device identifiers, and proprietary information.

- VisualCat processes sessions locally and ships with no telemetry, upload, cloud sync, or remote analysis.
- External file sessions retain a path and content identity. Portable and live sessions retain raw bytes in `raw.log`.
- Exports are byte-faithful and can contain every secret present in the source; review them before sharing.
- Redacted application diagnostics are local, bounded rolling JSON lines. Property names associated with paths, device serials, searches, queries, raw data, and messages are redacted. Diagnostic bundles omit raw logs and redact paths, hashes, file names, tags, templates, process names, searches, and device serials; the UI still requires an explicit sensitive-metadata acknowledgement before creating one.
- Temporary sessions use the platform-local application data directory (or the cache directory selected in settings). Automatic cleanup is disabled by default and is visible in **Session cache**. Deleting a session removes its files but does not promise forensic erasure from the underlying storage device.
- Android sharing is initiated explicitly through the platform UI.
