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
it from the device's file manager after allowing installs from that source, or
with the Android SDK platform tools.

Normal Android applications can usually read only their own logs. The current
Play-oriented VisualCat build therefore offers two explicit Live modes:

- **VisualCat only** starts immediately and uses Android's ordinary restricted
  app log scope.
- **Full-device** pairs with Android's built-in Wireless debugging service.
  Enable Developer options and Wireless debugging, choose **Pair device with
  pairing code**, enter the displayed pairing port and six-digit code in
  VisualCat, and keep Wireless debugging on while Live runs. Pairing is normally
  reusable; VisualCat disconnects when capture stops.

The Play/Release build does not declare or self-grant `READ_LOGS`. Debug and
controlled non-Play builds may still use the older externally granted direct
path for developer testing. See the
[support matrix](https://github.com/benny-cz/VisualCat/blob/main/docs/SUPPORT.md)
for the exact current behavior.

The historical v2.0.6 device evidence predates the Wireless ADB production
path. Version 2.0.7 has physical-device records in
`docs/ANDROID-LIVE-TEST-REPORT.md`: real first pairing, saved reconnect,
interruption recovery and full-device capture on a Pixel 5, API-36 Samsung and
Motorola layout/own-app checks, and a final production-upload-key-signed Samsung
smoke. Historical external-`READ_LOGS` evidence is not used as a substitute.

No VisualCat build sends telemetry or uploads log content.
