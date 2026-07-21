# ADR 0015: Desktop packaging

**Decision:** Publish the same Avalonia desktop head self-contained per supported RID. Windows is optimized and released first; Linux/macOS use the same session format and test matrix.

**Alternatives:** Platform-specific presentation forks multiply correctness risk.

**Consequences and validation:** `.github/workflows/release.yml` publishes self-contained `win-x64`, `linux-x64`, `osx-x64`, and `osx-arm64` artifacts plus an unsigned Android bundle. `tools/package.ps1` provides the same deterministic desktop publish path locally. Signing/notarization uses release-operator credentials and is intentionally a post-build gate; unsigned artifacts are never represented as signed releases.
