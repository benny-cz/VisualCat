# VisualCat session format 2.0

A `.vcat` session is a directory, never an executable container. All integers are little-endian. Paths in a manifest are relative and readers reject traversal outside the session root.

```text
session.vcat/
  manifest.json
  view.json                        # optional active view and named view presets
  raw.log                         # live or portable sessions
  segments[-final-generation]/
    000001/
      timestamp.bin              # Int64 UTC microseconds
      sequence.bin               # Int64 source sequence
      raw-offset.bin             # Int64
      raw-length.bin             # Int32
      pid.bin / tid.bin          # Int32
      level.bin                  # byte
      tag.bin / buffer.bin       # UInt32 table ID
      template.bin               # UInt32
      flags.bin                  # UInt16
      provenance.bin             # byte
      confidence.bin             # byte, 0..255
      format.bin                 # byte
      message-offset/length.bin
      original-offset/length.bin
      payload.bin
      bitmaps/level-*.rbm
  source-order/records.bin
  source-order/untimed.json
```

Timed entries in each segment are stable-sorted by `(timestamp, source sequence)`. Finalized finite imports are globally merged into chronologically consecutive segments. Source-order records independently cover every source byte.

Rank bitmap files begin with `VCBM`, version `1`, bit length, word count, and little-endian `UInt64` words. The manifest stores SHA-256 checksums for every column and bitmap. `vcat verify` validates checksums, dimensions, order, unique sequence numbers, bitmap cardinality, byte coverage, source identity, and summary reconciliation.

Major versions are refused safely. A missing or metadata-changed external source opens in degraded index-only mode. Raw access makes the stronger content decision independently: the complete recorded `Source.Length` prefix on the open read handle must match `Source.Sha256`. Safe append-only growth is accepted, appended bytes stay outside the snapshot, and a shorter or changed prefix is refused. A finalized embedded `raw.log` must match both the recorded length and digest. See [ADR 0020](adr/0020-verified-raw-evidence.md).

`view.json` is a versioned, bounded sidecar and is not part of analytical identity. A malformed or unsupported view sidecar is ignored without preventing the immutable session snapshot from opening. Standard and portable save copy it atomically with the session.

Live manifests may include bounded `(pid, name, firstSeen, lastSeen)` process-name ranges. PID reuse creates a new range; process lookup is time-aware and failures to sample a process list never fail capture.

A `.vcat.zip` file is a portable transport envelope for a verified portable session directory. Extraction rejects absolute paths, traversal, symbolic links, duplicate paths, unreasonable entry counts, and excessive expanded size, then runs the normal session verifier before atomically publishing the extracted directory.
