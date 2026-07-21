# Release checklist

- [ ] `global.json` resolves to the recorded stable SDK.
- [ ] Release builds have zero warnings on Windows, Linux, and macOS.
- [ ] Domain, parser, store, query, pipeline, and interaction suites pass.
- [ ] Sanitized golden corpus and sample scripts reconcile reviewed counts.
- [ ] Corruption checks reject altered manifest, column, bitmap, and raw data.
- [ ] Reference ingest, reopen, heat-map, search, cancellation, and memory measurements are recorded.
- [ ] Four-hour ADB soak and reconnect/disconnect matrix complete without an orphan process.
- [ ] File, portable, growing-file, partial, degraded, and incompatible sessions are manually exercised.
- [ ] Keyboard, contrast, text scaling, focus, and screen-reader labels are reviewed.
- [ ] Windows package is signed; macOS package is signed/notarized; Linux packages are validated.
- [ ] Desktop and CLI archives, the signed APK, and `SHA256SUMS` are attached to the GitHub release.
- [ ] Android own-app and granted full-device modes are tested on physical hardware.
- [ ] Privacy, support matrix, known limits, migration policy, and third-party notices are current.

Use `pwsh ./tools/package.ps1 -Runtime win-x64,linux-x64,osx-x64,osx-arm64`
to reproduce desktop artifacts. The release workflow emits versioned desktop
and CLI archives, generates checksums, and publishes them from a `v*` tag.

Android releases are installable APKs signed by a persistent release key. Set
these GitHub Actions repository secrets before tagging:

- `ANDROID_KEYSTORE_BASE64` — base64-encoded keystore bytes;
- `ANDROID_KEYSTORE_PASSWORD`;
- `ANDROID_KEY_ALIAS`;
- `ANDROID_KEY_PASSWORD`.

The workflow fails closed if any signing value is absent and never uploads an
unsigned or ephemeral debug-signed Android package. Back up the keystore and
passwords outside GitHub; losing the key prevents users from upgrading an
installed APK. Desktop signing, macOS notarization, store submission,
physical-device validation, and multi-hour soak gates require release
infrastructure or hardware and must be recorded explicitly when deferred.
