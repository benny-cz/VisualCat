# Security model

VisualCat treats logs, session manifests, mapped columns, regular expressions,
ADB output, file paths, device metadata, and Android Wireless-debugging state as
untrusted.

## General controls

- parsers use bounded line sizes and checked numeric conversion;
- regex operates per record with a timeout and cooperative cancellation;
- manifest size, segment count, mapped column dimensions, offsets, lengths, and
  relative paths are validated;
- segment and raw identities use SHA-256;
- manifests and exports use temporary files followed by atomic replacement;
- portable archives reject zip traversal, links, duplicate destinations,
  excessive entry counts, and expanded-size bombs, and must pass full
  verification before publication;
- desktop/host ADB uses `ProcessStartInfo.ArgumentList`, never a constructed shell
  command;
- Android share URIs are read-only `FileProvider` content URIs scoped to an
  app-private cache directory;
- source content is never executed, and no export re-encodes it: a CSV export is exact
  data, so log text that a spreadsheet application would evaluate as a formula is written
  through unchanged. That boundary is deliberate and documented — import an untrusted log's
  CSV as text rather than opening it directly ([ADR 0021](adr/0021-csv-export-fidelity.md));
- queues, caches, search markers, detail pages, error buffers, batches, and Android
  Wireless-ADB read buffers have explicit bounds.

## Android Wireless debugging boundary

Wireless ADB is intentionally treated as a high-trust transport. The underlying
ADB protocol can expose broad `shell` capabilities, so VisualCat places a narrow
API in front of the third-party library:

- the shared UI can pair, reconnect, and request an `ILogSource`; it has no API
  that accepts an arbitrary ADB service or shell command;
- the only shell destination created by the Android service is the fixed
  full-device `logcat -b all` stream;
- the destination is hard-capped at 96 UTF-8 bytes, below the historical LibADB
  `A_OPEN` destination-overflow range, so future command growth fails closed;
- the only variable inserted into a reconnect destination is a timestamp that is
  shape-validated character by character before command construction;
- pairing ports are numeric and range-checked; pairing codes must be exactly six
  ASCII digits, are never stored or inserted into shell text, and are redacted
  from third-party pairing exception details before VisualCat logs them;
- the ADB RSA identity is encrypted at rest with AES-256-GCM and a
  non-exportable Android-Keystore wrapping key;
- a saved pairing is considered usable only when its encrypted identity payload,
  Android-Keystore wrapping-key alias, and non-secret successful-pairing marker all
  exist, so a failed first attempt cannot be presented as “already paired”;
- mDNS multicast access is held only during service discovery;
- reconnect attempts are serialized; a single Live capture owns the connection;
- the connection manager is closed and disposed after capture rather than merely
  disconnected, so its decrypted private-key object is discarded while the encrypted
  reusable identity remains at rest;
- discovery and stream-establishment work is cancellation-aware, but LibADB 3.2.0
  creates the low-level pairing socket without an explicit cancellable socket
  timeout. VisualCat therefore keeps the pairing operation visible as
  `Cancelling…` until that local handshake actually returns, and physical-device
  release testing must prove that cancelling pairing cannot start Live or leave an
  authenticated connection behind;
- LibADB 3.2.0 internally queues received ADB packets without a hard bound, so a
  dedicated reader continuously drains it into VisualCat's own bounded 16 × 64
  KiB queue. If that queue fills, the stream is closed and reconnected from the
  latest complete timestamp instead of letting memory grow without limit;
- transport interruption or a deliberate backpressure recycle increments a
  reconnect-gap defect counter and resumes from the latest validated logcat
  timestamp rather than pretending continuity;
- Stop/Dispose closes the log stream and ADB connection. Pairing can remain in
  Android for later explicit use and can be revoked in Wireless debugging
  settings.

Every Android Live source holds an unexported `dataSync` foreground-service
lease while it runs. Its private notification contains no log or pairing data
and routes **Stop and save** through the same draining session-finalization path
as the in-app control. The service is non-sticky, so Android cannot resurrect a
stale capture notification after process loss, and the API-35+ six-hour timeout
removes foreground state promptly while requesting graceful capture shutdown.

The production Release/Play build does not declare `READ_LOGS` by default and
never uses local ADB to change its own permission state. Debug or an explicitly
opted-in non-Play build can retain the established externally granted direct
`READ_LOGS` path.

The pinned `libadb-android-bc` dependency is security-sensitive and upstream
states that it has not undergone a security audit. That dependency must remain
version-pinned, its Release AAB contents and licenses must be reviewed, and the
Wireless ADB path must pass the physical-device matrix and soak tests in
`ANDROID-LIVE-TEST-PLAN.md` before a Play release.

## Reporting

Report suspected vulnerabilities through GitHub's
[private vulnerability reporting](https://github.com/benny-cz/VisualCat/security/advisories/new).
Expect an acknowledgement within seven days. Include the affected release or
commit and a minimized, non-sensitive reproducer where possible. Only the latest
release and `main` receive security fixes.
