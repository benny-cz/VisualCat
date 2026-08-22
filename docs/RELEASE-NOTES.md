# VisualCat v{{VERSION}} release notes

The version-specific source history is recorded in
[CHANGELOG.md at tag v{{VERSION}}](https://github.com/benny-cz/VisualCat/blob/v{{VERSION}}/CHANGELOG.md).

VisualCat release assets are self-contained. Choose the desktop or CLI archive
matching your operating system and CPU. Releases also include a signed Android
APK when Android release signing is enabled. Verify the download against
`SHA256SUMS` before running it.

```shell
sha256sum -c SHA256SUMS
```

In PowerShell:

```powershell
(Get-FileHash -Algorithm SHA256 ./VisualCat-Desktop-win-x64-*.zip).Hash
```

Compare the printed value with the matching line in `SHA256SUMS`.

A checksum only proves the bytes were not corrupted in transit; it is hosted
next to the download it describes. To prove the bytes came from this
repository's release workflow, verify the build provenance attestation with the
[GitHub CLI](https://cli.github.com/):

```shell
gh attestation verify VisualCat-Desktop-linux-x64-*.tar.gz --repo benny-cz/VisualCat
```

## What each archive contains

Alongside the application, every desktop and CLI archive contains:

- `LICENSE` — the MIT license this project is released under;
- `THIRD-PARTY-NOTICES.md` — bundled components and their licenses; and
- `README.txt` — the version, launch command, checksum and provenance
  verification steps, platform-specific notes, and where to report a bug or a
  vulnerability.

Each release also carries a CycloneDX SBOM (`VisualCat-sbom-v*.cdx.json`)
covering resolved packages in the desktop solution. It does not enumerate the
embedded self-contained .NET runtime or Android-only dependencies.

## Desktop packages

Desktop packages are currently unsigned.

- **Windows:** extract the ZIP and run `VisualCat.exe`. If SmartScreen warns,
  choose **More info → Run anyway** after verifying the checksum.
- **macOS:** the download is a bare command-line-launched executable, not a
  Finder `.app` bundle, and is neither signed nor notarized. Extract the archive,
  then remove the downloaded-file quarantine after verifying the checksum:

  ```shell
  xattr -dr com.apple.quarantine VisualCat
  ./VisualCat
  ```

- **Linux:** only a tarball is provided—there is no `.deb`, `.rpm`, AppImage,
  Flatpak, or desktop-menu entry. Extract the archive, preserve or restore the
  executable bit, install the native dependencies listed in the
  [support matrix](https://github.com/benny-cz/VisualCat/blob/main/docs/SUPPORT.md),
  then launch it:

  ```shell
  chmod +x VisualCat
  ./VisualCat
  ```

The CLI archives contain `vcat` (`vcat.exe` on Windows) and follow the same
platform rules. Run `vcat --version` to include the exact build identity in a
bug report.

## Android (when included)

The Android asset is a release-key-signed APK for Android 12 or newer. Install
it from the device's file manager after allowing installs from that source, or:

```shell
adb install VisualCat-Android-*.apk
```

Normal Android applications can usually read only their own logs. See the
[support matrix](https://github.com/benny-cz/VisualCat/blob/main/docs/SUPPORT.md)
for the optional `READ_LOGS` grant and current capture limitations. The v2.0.6
APK was validated on Android 14 physical hardware in both own-app and granted
full-device modes. Restricted capture stayed honestly restricted through its
quiet heartbeat; granted capture survived rotation in the same process and
finalized cleanly at 1,303 entries. Both completed sessions returned after a
forced process restart, no crash or ANR was logged, and no persistent
`READ_LOGS` grant remained after testing. The four-hour ADB soak completed
during the 2.0.6 development cycle is recorded in the Android live-test report.

No VisualCat build sends telemetry or uploads log content.
