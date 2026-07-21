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
