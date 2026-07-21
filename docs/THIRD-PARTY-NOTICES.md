# Third-party notices

VisualCat depends on open-source components. The table distinguishes direct
package references from transitive components that are included by the UI
stack or self-contained runtime.

## Runtime and UI

| Component | Relationship | Purpose | License |
|---|---|---|---|
| [Avalonia](https://github.com/AvaloniaUI/Avalonia) | Direct | Cross-platform application framework, controls, desktop/Android hosts, and Skia integration | MIT |
| [SkiaSharp](https://github.com/mono/SkiaSharp) | Transitive through Avalonia.Skia | 2D graphics and native Skia bindings | MIT |
| [HarfBuzzSharp](https://github.com/mono/SkiaSharp) | Transitive through the Avalonia/Skia text stack | Text shaping bindings | MIT |
| [ANGLE](https://chromium.googlesource.com/angle/angle) | Transitive through `Avalonia.Angle.Windows.Natives` | Prebuilt Direct3D-backed OpenGL ES implementation used for Windows rendering | BSD-3-Clause |
| [MicroCom](https://github.com/kekekeks/MicroCom) | Transitive through Avalonia.Win32 | COM interop runtime support | MIT |
| [Tmds.DBus.Protocol](https://github.com/tmds/Tmds.DBus) | Transitive through Avalonia.FreeDesktop | D-Bus protocol support on Linux | MIT |
| [System.IO.Hashing](https://github.com/dotnet/runtime) | Direct | Non-cryptographic checksums for the column store | MIT |
| [AndroidX Core](https://github.com/dotnet/android-libraries) | Direct on Android | Android compatibility APIs | Apache-2.0 |
| [.NET](https://github.com/dotnet/runtime) | Platform/runtime | Managed runtime and base class libraries included in self-contained packages | MIT |

## Build and test

| Component | Purpose | License |
|---|---|---|
| [Microsoft Source Link for GitHub](https://github.com/dotnet/sourcelink) | Maps compiled source information to the public repository | MIT |
| [xUnit.net](https://github.com/xunit/xunit) | Test framework | Apache-2.0 |
| [Microsoft.NET.Test.Sdk](https://github.com/microsoft/vstest) | .NET test host and platform | MIT |

Self-contained release artifacts include transitive components from these
projects. This summary is provided for convenience and for the explanations an
automated inventory cannot express; the license text distributed with each
component is authoritative.

The complete machine-generated inventory for a given build is the CycloneDX
SBOM attached to each release. Reproduce it with:

```shell
pwsh ./tools/generate-sbom.ps1
```

That script fails the build on licenses incompatible with an MIT-licensed
self-contained distribution and lists components whose upstream package
metadata declares no license, so they can be resolved by hand.
`Avalonia.Angle.Windows.Natives` is the current example: the NuGet package
carries no license expression, and the redistributed ANGLE binaries are
BSD-3-Clause from the upstream Chromium project.
