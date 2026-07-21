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
| [AndroidX Core](https://github.com/dotnet/android-libraries) | Direct on Android | Android compatibility APIs | Apache-2.0 |
| [.NET](https://github.com/dotnet/runtime) | Platform/runtime | Managed runtime and base class libraries included in self-contained packages | MIT |

## Build and test

| Component | Purpose | License |
|---|---|---|
| [Microsoft Source Link for GitHub](https://github.com/dotnet/sourcelink) | Maps compiled source information to the public repository | MIT |
| [xUnit.net](https://github.com/xunit/xunit) | Test framework | Apache-2.0 |
| [Microsoft.NET.Test.Sdk](https://github.com/microsoft/vstest) | .NET test host and platform | MIT |

Self-contained release artifacts include transitive components from these
projects. Release operators must retain license files emitted with packages and
review the generated dependency inventory before distribution. This summary is
provided for convenience; the license text distributed with each component is
authoritative.
