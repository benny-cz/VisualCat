# Security model

VisualCat treats logs, session manifests, mapped columns, regular expressions, ADB output, file paths, and device metadata as untrusted.

- parsers use bounded line sizes and checked numeric conversion;
- regex operates per record with a timeout and cooperative cancellation;
- manifest size, segment count, mapped column dimensions, offsets, lengths, and relative paths are validated;
- segment and raw identities use SHA-256;
- manifests and exports use temporary files followed by atomic replacement;
- portable archives reject zip traversal, links, duplicate destinations, excessive entry counts, and expanded-size bombs, and must pass full verification before publication;
- ADB uses `ProcessStartInfo.ArgumentList`, never a constructed shell command;
- Android share URIs are read-only `FileProvider` content URIs scoped to an app-private cache directory;
- source content is never executed;
- queues, caches, search markers, detail pages, error buffers, and batches have explicit bounds.

## Reporting

Report suspected vulnerabilities through GitHub's
[private vulnerability reporting](https://github.com/benny-cz/VisualCat/security/advisories/new).
Expect an acknowledgement within seven days. Include the affected release or
commit and a minimized, non-sensitive reproducer where possible. Only the latest
release and `main` receive security fixes.
