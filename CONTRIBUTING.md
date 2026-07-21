# Contributing to VisualCat

Thanks for your interest. VisualCat is a greenfield .NET 10 rewrite with a clean
layered architecture (`Domain → Core → Application → Infrastructure → App`),
strict quality gates, and a full test suite. Contributions that keep it that way
are very welcome.

## Prerequisites

- The .NET SDK pinned by [`global.json`](global.json) (currently `10.0.101`).
  `dotnet` will tell you if your installed SDK does not match.
- The Android workload (`dotnet workload install android`) **only** if you build
  the Android companion.
- `adb` from the Android SDK platform tools **only** for live host capture.

## Build and test

The documented desktop solution contains the engine, CLI, desktop app, tools,
and every test project without requiring the Android workload:

```shell
dotnet restore VisualCat.Desktop.slnx
dotnet build   VisualCat.Desktop.slnx --no-restore
dotnet test    VisualCat.Desktop.slnx --no-build
```

`VisualCat.slnx` additionally includes the Android companion and therefore
requires `dotnet workload install android`.

Run the desktop app or the CLI directly:

```shell
dotnet run --project src/VisualCat.Desktop
dotnet run --project src/VisualCat.Cli -- index samples/logcat_small.txt --output out.vcat
```

### Generating test logs

Large log fixtures are not committed. Generate reproducible ones with the seeded
generator (`<output> [lines=1000000] [seed=42]`; the same seed is byte-identical
every run):

```shell
dotnet run --project tools/VisualCat.GenerateLogs --configuration Release -- samples/logcat_large.txt 200000 42
```

### Benchmarks

```shell
dotnet run --project bench/VisualCat.Benchmarks --configuration Release
```

Reproducible reference measurements live in [docs/PERFORMANCE.md](docs/PERFORMANCE.md).

## Coding standards

- Formatting and style are governed by [`.editorconfig`](.editorconfig); please
  run `dotnet format VisualCat.Desktop.slnx` before submitting.
- **Warnings are errors.** `Directory.Build.props` sets `TreatWarningsAsErrors`,
  `Nullable`, and the .NET analyzers at `latest-recommended`. A change that
  introduces a warning will fail the build — fix the cause rather than
  suppressing it, and justify any unavoidable suppression in the PR.
- Builds are deterministic; keep them that way (no machine- or time-dependent
  output).
- Match the surrounding code: the layering above is deliberate, the implemented
  structure is described in [ARCHITECTURE.md](ARCHITECTURE.md), and rationale
  for major decisions is captured in the [ADRs](docs/adr/).

## Pull requests

1. Keep CI green — the 3-OS build/test matrix and the Android build must pass.
2. Add or update tests for behavior changes (suites under [`tests/`](tests/)).
3. Update [`CHANGELOG.md`](CHANGELOG.md) and any affected docs when you change
   behavior or the public surface.
4. Use the pull-request template; keep PRs focused and reviewable.

## Reporting issues

- Bugs and feature ideas: use the templates under
  [`.github/ISSUE_TEMPLATE/`](.github/ISSUE_TEMPLATE/).
- Security vulnerabilities: **do not** open a public issue — follow
  [docs/SECURITY.md](docs/SECURITY.md).
- Please attach only minimized, non-sensitive log samples; VisualCat treats all
  log content as private (see [docs/PRIVACY.md](docs/PRIVACY.md)).

## License

By contributing, you agree that your contributions are licensed under the
project's [MIT License](LICENSE).
