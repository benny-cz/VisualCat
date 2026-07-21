# VisualCat v2 release notes

VisualCat release assets are self-contained. Choose the desktop or CLI archive
matching your operating system and CPU, or the Android APK. Verify the download
against `SHA256SUMS` before running it.

```shell
sha256sum -c SHA256SUMS
```

In PowerShell:

```powershell
(Get-FileHash -Algorithm SHA256 ./VisualCat-Desktop-win-x64-*.zip).Hash
```

Compare the printed value with the matching line in `SHA256SUMS`.

## Desktop packages

Desktop packages are currently unsigned.

- **Windows:** extract the ZIP and run `VisualCat.exe`. If SmartScreen warns,
  choose **More info → Run anyway** after verifying the checksum.
- **macOS:** extract the archive, then remove the downloaded-file quarantine
  after verifying the checksum:

  ```shell
  xattr -dr com.apple.quarantine VisualCat
  ./VisualCat
  ```

- **Linux:** extract the archive, preserve or restore the executable bit, then
  launch it:

  ```shell
  chmod +x VisualCat
  ./VisualCat
  ```

The CLI archives contain `vcat` (`vcat.exe` on Windows) and follow the same
platform rules. Run `vcat --version` to include the exact build identity in a
bug report.

## Android

The Android asset is a release-key-signed APK for Android 12 or newer. Install
it from the device's file manager after allowing installs from that source, or:

```shell
adb install VisualCat-Android-*.apk
```

Normal Android applications can usually read only their own logs. See
[`SUPPORT.md`](SUPPORT.md) for the optional `READ_LOGS` grant and current
capture limitations.

No VisualCat build sends telemetry or uploads log content.
