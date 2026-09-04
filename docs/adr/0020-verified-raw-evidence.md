# ADR 0020: Verified raw-evidence leases

**Context:** A standard session records byte offsets into an external log file. Reusing that
path for another capture can leave a perfectly queryable index whose offsets now address
different bytes. File length and modification time are useful degradation signals, but they
cannot distinguish a safe append from a rewrite and can be preserved across a replacement.
Raw export, Copy raw, source context, and unparsed-line inspection all promise the source's
exact bytes, so metadata is not sufficient authority to read them.

**Decision:** Every raw reader opens a `VerifiedRawSource` lease and reads only through it.
The lease hashes all `Source.Length` recorded bytes on the same open file handle used for the
requested reads, compares that prefix with `Source.Sha256`, and rejects every indexed span
outside the verified prefix. An external file may be longer: append-only growth does not move
recorded offsets, and appended bytes are outside the session snapshot. A shorter file or a
different prefix is refused. A finalized embedded `raw.log` must match both the recorded
length and digest; mismatch is reported as session damage. Verification happens for every
operation rather than being cached for a snapshot's lifetime.

The lease *prefers* a cooperative write/delete exclusion and falls back to a shared open when
the platform refuses it. The exclusion is the stronger form — while it holds, no cooperative
process can alter the file between this handle's verification and its reads — but it is not
always available, and the case where it is not is the ordinary one: the capture that produced
the file is still running (`adb logcat > capture.txt` holds it open for the whole capture, and
on Windows that makes the exclusive open a sharing violation), or VisualCat itself is still
appending to a progressive `raw.log`. Refusing raw evidence for a file that is merely still
growing would be both a worse answer than the one this lease exists to prevent and a false
one — the recorded prefix is present and verifies. The residual exposure under the fallback is
a cooperative process rewriting the *recorded prefix* inside a single operation; an appending
writer cannot reach it, because it never moves a recorded byte and every operation re-verifies
the whole prefix on the handle it reads from.

Raw readers never turn a failed verification into an empty or shifted answer. They explain
that the index remains usable and tell the reader to restore the original, re-import the
current file, or verify/restore a damaged portable session.

**Threat model:** This boundary protects against ordinary accidental change and against
cooperative processes that honour the operating system's sharing contract. It is not an
authenticity guarantee against a hostile process that can rewrite an already-open file,
bypass advisory locks, preserve metadata, or alter both a session and its digest. A portable
session with an immutable copied source, protected and distributed by an appropriate
external integrity/authenticity mechanism, is the supported boundary when hostile mutation
is in scope. VisualCat does not silently weaken the exact-evidence claim through block
sampling or metadata-only validation.

**Alternatives:** Refusing every session marked degraded was rejected because an ordinary
append is safe. First/last-block sampling was rejected because a middle rewrite passes it.
A snapshot-lifetime boolean cache was rejected because the path can change after the first
inspection. Copying every recorded prefix for every operation gives a stronger hostile-writer
boundary but doubles storage I/O and temporary-space requirements, including on constrained
phones; it is not proportionate to the chosen threat model.

**Consequences and validation:** Raw access now performs one sequential SHA-256 pass before
serving bytes. The cost is explicit and cancellable; normalized queries remain available even
when raw verification fails. Measured on a Samsung SM-G990B (Android 16), the source-context
inspector over an 18 MB embedded source rendered in well under a second, so the pass is not
felt on the constrained platform at the sizes a phone holds. `vcat verify` uses the same
prefix rule, accepts append-only
external growth, reports an external mismatch as `source.hash`, and treats damaged embedded
evidence as an error. Regression tests cover append, same-length/timestamp-preserving change,
revalidation after a successful operation, embedded corruption, a source still held open by
its capturing writer, export, raw-context export, source context, unparsed lines, and Copy
raw. Reconsider immutable per-session source copying or a
platform-specific stable-file-identity primitive if hostile concurrent mutation becomes a
product requirement or measured verification cost makes exact raw access impractical.
