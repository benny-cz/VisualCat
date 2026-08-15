# Sample logcat fixtures

Small, deterministic synthetic logcat fixtures used by the README quick starts,
the CLI examples, and manual testing. They contain no device-derived data.

| File | Size | Use |
|---|---|---|
| `logcat_supersmall.txt` | ~4.5 KB | fastest smoke test; 50 generated lines |
| `logcat_small.txt` | ~90 KB | README / CLI quick-start input; 1,000 generated lines |

Recreate the checked-in fixtures exactly from the repository root:

```shell
dotnet run --project tools/VisualCat.GenerateLogs --configuration Release -- samples/logcat_supersmall.txt 50 20260721
dotnet run --project tools/VisualCat.GenerateLogs --configuration Release -- samples/logcat_small.txt 1000 20260721
```

## Generating large logs

Large fixtures are **not** committed — they bloat every clone and are fully
reproducible from a seeded generator. Create them on demand:

```shell
dotnet run --project tools/VisualCat.GenerateLogs --configuration Release -- samples/logcat_large.txt 200000 20260721
dotnet run --project tools/VisualCat.GenerateLogs --configuration Release -- samples/logcat_verylarge.txt 1000000 20260721
```

Arguments are `<output> [lines=1000000] [seed=42]`. The same seed always produces
byte-identical output, so generated logs are reproducible and diffable across
machines.

## The demo capture

`VisualCat.GenerateLogs` samples tags and messages uniformly, which is ideal for
parser and throughput tests and useless as a picture: every severity is equally
likely, so the heat map is a flat band. `tools/VisualCat.DemoLog` writes a log
with a *shape* instead — the one every screenshot and demo in
[`docs/assets`](../docs/assets/README.md) uses:

```shell
dotnet run --project tools/VisualCat.DemoLog --configuration Release -- .tmp/demo/northlight-transit-20260812.log
```

Arguments are `<output> [lines=1000000] [seed=20260812]`, and the default run
produces 1,000,156 lines / ~115 MB of `threadtime` records spanning two hours:
boot, an intermittent idle period, a commute, a network-failure patch, a doze
window containing several minutes of genuine silence, a memory squeeze, an ANR,
two Java crashes with full stack traces, a native tombstone, and a calm
afternoon. Around 110 real AOSP tags are attributed to a coherent process table,
and the app in the story — `com.northlight.transit` — is invented, as are its
hosts, packages, and stations. Nothing is copied from a real device.

Like the large fixtures it is **not** committed: it is 115 MB and fully
reproducible from its seed.
