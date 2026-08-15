# Documentation screenshots

These are captured from the real applications — the Windows desktop build and the
Android companion on a physical phone — not mockups. Every one of them shows the
same seeded synthetic capture, so the repository ships no private or
device-derived log data.

| Asset | Documentation use | Capture state |
|---|---|---|
| [`demo.gif`](demo.gif) | README interaction loop | Desktop; the full session, a dive into the error burst, and the records behind it |
| [`heatmap-analysis.jpg`](heatmap-analysis.jpg) | Primary README product image | Desktop workspace maximized at 3840 × 2112 |
| [`heatmap-hero.jpg`](heatmap-hero.jpg) | Readable README workspace crop | Derived from `heatmap-analysis.jpg` |
| [`start-page.jpg`](start-page.jpg) | Secondary README image | Clean desktop start page at 1440 × 900 |
| [`android-demo.gif`](android-demo.gif) | README Android loop | Composited from the on-device recording |
| [`android-demo.mp4`](android-demo.mp4) | Full Android walkthrough | 70 s, 1920 × 1080, captured with `adb shell screenrecord` |
| [`android-companion.jpg`](android-companion.jpg) | README Android feature image | Two on-device screenshots on the product background |
| [`logo.svg`](logo.svg) | Repository/product mark | Source vector for the V plus severity-density motif |
| [`social-preview.jpg`](social-preview.jpg) | GitHub link preview | 1280 × 640 crop of the analysis workspace |

## The capture used by every asset

All of them show the same demo log: a synthetic 1,000,156-line, 115 MB Android
capture spanning two hours — boot, a commute, a bad network patch, a doze window
with real silence in it, a memory squeeze, an ANR, two Java crashes and a native
tombstone, then a calm afternoon. Rebuild it from the repository root:

```shell
dotnet run --project tools/VisualCat.DemoLog --configuration Release -- .tmp/demo/northlight-transit-20260812.log
```

The generator is deterministic, so the same command always produces the same
115 MB file. It is not committed; see [`samples/README.md`](../../samples/README.md).

## Desktop assets

```shell
dotnet run --project src/VisualCat.Desktop --configuration Release -- --log .tmp/demo/northlight-transit-20260812.log
```

Maximize the window, wait for the status bar to read `Ready`, click once inside
the heat map and press <kbd>0</kbd> to fit the whole session, then capture the
window. Run the same command without `--log` at 1440 × 900 for the start page.
Keep screenshots free of private paths, notifications, and device-derived content.

`heatmap-hero.jpg` and `social-preview.jpg` are crops of `heatmap-analysis.jpg`
and are regenerated, together with the application icons, by:

```powershell
pwsh tools/generate-brand-assets.ps1
```

For `demo.gif`, capture the same workspace as a short frame sequence: hold on the
fitted session, double-click the error burst about six times to dive into it,
click once to scope the entry list, then press <kbd>0</kbd> to return so the loop
closes cleanly. Crop to the client area, scale to 960 pixels wide, run at roughly
2.4 frames per second, and keep the result below 5 MiB.

## Android assets

Install the companion on a device, push the demo log to `Downloads`, and record
the interaction:

```shell
adb push .tmp/demo/northlight-transit-20260812.log /sdcard/Download/northlight-transit-20260812.txt
adb shell settings put system show_touches 1
adb shell screenrecord --time-limit 175 --bit-rate 16000000 /sdcard/demo.mp4
```

Drive the UI with `adb shell input tap` so the timing is repeatable, then pull the
recording. `android-demo.mp4` composites that capture onto the product background
with captions; `android-demo.gif` is a 15-second window of the same cut.

Full-device live capture additionally needs the §4.4 log permission:

```shell
adb shell pm grant com.barebit.visualcat android.permission.READ_LOGS
```

Regenerate all of the derived assets whenever the application chrome changes
materially.
