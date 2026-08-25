# `vcat` command-line reference

`vcat` indexes Android logcat text into verified local VisualCat sessions, queries
those sessions, exports results, and captures live logs through ADB. Commands use
exit code `0` on success, `2` for invalid command input, `3` when verification
finds corruption, `130` when cancelled, and `1` for other failures. Diagnostics
go to standard error; structured results go to standard output.

NuGet.org publication is not currently configured. Download
`VisualCat.Cli.2.0.9.nupkg` from the
[latest GitHub release](https://github.com/benny-cz/VisualCat/releases/latest)
into `./packages`, then install that exact package from the local feed:

```shell
dotnet tool install --global VisualCat.Cli --version 2.0.9 --add-source ./packages
vcat --version
```

From a source checkout, replace `vcat` in every example with:

```shell
dotnet run --project src/VisualCat.Cli --
```

## Common option conventions

- Times passed to `--from` and `--to` may be ISO-8601 timestamps or signed Unix
  microseconds. Ranges are half-open: `--from` is included and `--to` is excluded.
- `--levels` accepts comma-separated `V,D,I,W,E,F,?` values or full names.
- Tag, process, PID, TID, and buffer filters accept comma-separated values.
- `--order chronological` is the default; `--order source` preserves source
  sequence.
- Logcat formats are `threadtime`, `time`, `brief`, `long`, and `epoch`.
- JSON property names are camel-cased. Enums are names rather than integers and
  instants are ISO-8601 UTC strings.
- Set `VISUALCAT_DEBUG=1` to include exception details in error output.

## `--version`

```text
vcat --version
```

Prints the exact informational build version and exits.

## `index`

```text
vcat index <log.txt> [--output session.vcat] [options]
```

Indexes a finite log file. The default output is `<input>.vcat`.

Options:

- `--output <path>` chooses the session directory.
- `--force` replaces an existing directory whose name ends in `.vcat`. For
  safety, filesystem roots and trees containing links or reparse points are
  refused.
- `--portable` embeds raw source bytes in the session.
- `--format <format>` overrides automatic detection.
- `--year <year>` supplies a year for formats that omit it.
- `--timezone <zone>` supplies an IANA or Windows time-zone identifier.
- `--no-templates` disables Drain template mining.
- `--segment-entries <count>` changes the 100,000-entry segment target.
- `--workers <count>` changes parser parallelism; `0` selects the default.

Example:

```shell
vcat index logcat.txt --output crash.vcat --portable --timezone UTC
```

The command prints one JSON object:

```json
{
  "session": "8a51b729-b7ea-4f9e-b7a2-018686648a7c",
  "path": "C:\\logs\\crash.vcat",
  "format": "ThreadTime",
  "confidence": 1,
  "entries": 250000,
  "untimed": 0,
  "unknown": 3,
  "templates": 842,
  "elapsedSeconds": 2.41
}
```

## `info`

```text
vcat info <log.txt|session.vcat> [--format <format>] [--year <year>] [--timezone <zone>]
```

For a log file, samples up to 200 lines and prints an import preview containing
`detection`, `timestampPolicy`, `firstInstant`, `lastInstant`, `outcomeCounts`,
and `warnings`. For a `.vcat` directory, prints its complete manifest.

```shell
vcat info samples/logcat_small.txt --timezone UTC
```

## `stats`

```text
vcat stats <session.vcat> [filters] [--top <count>]
```

Prints a `StatisticsResult` JSON object with the query identity, matching totals,
first and last instants, severity counts, and the top tag, PID, TID, buffer,
process, and template facets. `--top` defaults to `20`.

Supported filters are `--levels`, `--tags`, `--exclude-tags`, `--pids`,
`--processes`, `--exclude-processes`, `--tids`, and `--buffers`.

```shell
vcat stats crash.vcat --levels W,E,F --top 30
```

## `query`

```text
vcat query <session.vcat> [--from <ISO|us>] [--to <ISO|us>]
           [--limit <count>] [--order chronological|source] [filters]
```

Prints one `NormalizedEntry` JSON object per line (NDJSON), suitable for
streaming into tools such as `jq`. Each entry contains source identity and raw
span, normalized timestamp and provenance, PID/TID, severity, tag, buffer,
message, format, template ID, and flags. `--limit` defaults to `100`.

```shell
vcat query crash.vcat --levels E,F --limit 50 > errors.ndjson
```

## `search`

```text
vcat search <session.vcat> <text> [--regex] [--case-sensitive]
            [--timeout-ms <milliseconds>] [filters]
```

Searches message text. Regex matching has a per-match timeout, `250` ms by
default. The result JSON contains `identity`, `matches`, timestamp `markers`,
and `markersTruncated`. At most 20,000 markers are retained.

```shell
vcat search crash.vcat "FATAL EXCEPTION" --case-sensitive
vcat search crash.vcat 'timeout|ANR' --regex --timeout-ms 100
```

## `templates`

```text
vcat templates <session.vcat> [--from <ISO|us>] [--to <ISO|us>]
               [--top <count>] [filters]
```

Prints a JSON array of the most frequent templates. Each item contains
`templateId`, `canonicalText`, `count`, `first`, `last`, and representative
entry IDs. `--top` defaults to `50`.

```shell
vcat templates crash.vcat --levels E,F --top 20
```

## `export`

```text
vcat export <session.vcat> <output> --type <type>
            [--from <ISO|us>] [--to <ISO|us>]
            [--order chronological|source] [filters]
```

Exports the selected range and filters, then prints the absolute destination
path. `--type` defaults to `raw`.

| Type | Output |
|---|---|
| `raw` | Byte-faithful matching source records |
| `csv` | Normalized entry CSV |
| `templates-md` | Template frequency report in Markdown |
| `templates-csv` | Template frequency report in CSV |
| `stats-md` | Statistics report in Markdown |
| `stats-csv` | Statistics report in CSV |
| `portable` | Portable `.vcat` directory with embedded raw bytes |
| `portable-zip` | Verified portable `.vcat.zip` transport archive |

```shell
vcat export crash.vcat errors.csv --type csv --levels E,F
vcat export crash.vcat portable.vcat.zip --type portable-zip
```

## `verify`

```text
vcat verify <session.vcat> [--skip-raw]
```

Validates the manifest, columns, bitmaps, checksums, offsets, and raw source
coverage. `--skip-raw` omits verification of external raw data. The JSON report
contains `isValid` plus validation details; exit code `3` means the report is
invalid.

```shell
vcat verify crash.vcat
```

## `generate-test-log`

```text
vcat generate-test-log [output] [--output <path>] [--lines <count>]
                       [--seed <number>] [--format <format>]
```

Generates deterministic synthetic logcat data. Defaults are
`synthetic-logcat.txt`, 1,000,000 lines, seed `42`, and `threadtime`. The same
settings produce byte-identical content across supported platforms. The
absolute output path is printed.

```shell
vcat generate-test-log --output large.txt --lines 1000000 --seed 42
```

## `adb-devices`

```text
vcat adb-devices [--adb <path>]
```

Locates ADB from `--adb`, `ANDROID_SDK_ROOT`, or `PATH`, then prints a JSON array
of devices. Each item contains `serial`, `state`, optional `model`, `product`,
and `transportId`, plus parsed ADB `properties`.

```shell
vcat adb-devices
```

## `capture-adb`

```text
vcat capture-adb --serial <serial> [--output <session.vcat>]
                 [--duration-seconds <seconds>] [--max-bytes <bytes>]
                 [--buffers <list>] [--adb <path>] [index options]
```

Captures `threadtime` output from the selected device into a portable session.
The default buffers are `main,system,crash`. Without a duration or byte limit,
capture continues until interrupted. The absolute session path is printed.

```shell
vcat capture-adb --serial emulator-5554 --duration-seconds 60 --output minute.vcat
```

`--format` is fixed to `threadtime` for live ADB capture. The remaining index
options (`--year`, `--timezone`, `--no-templates`, `--segment-entries`, and
`--workers`) still apply.

## Automation notes

- Use `query` for NDJSON streaming and the other read commands for one complete
  JSON value.
- Paths printed by mutation commands are absolute.
- Progress is written only when standard error is connected to a terminal, so
  redirected structured output remains clean.
- Send Ctrl+C to request cancellation; completed session generations remain
  recoverable when an import or capture is interrupted.
