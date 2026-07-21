# Documentation screenshots

These screenshots are captured from the real Windows desktop application, not
mockups. They use the repository's deterministic synthetic sample data, so the
repository does not ship private device logs.

| Asset | Documentation use | Capture state |
|---|---|---|
| [`heatmap-analysis.jpg`](heatmap-analysis.jpg) | Primary README product image | Checked-in synthetic [`samples/logcat_small.txt`](../../samples/logcat_small.txt) fixture, maximized at 3840 × 2112 |
| [`heatmap-hero.jpg`](heatmap-hero.jpg) | Readable README workspace crop | Derived from `heatmap-analysis.jpg`; timeline and representative detail rows |
| [`demo.gif`](demo.gif) | Short README interaction loop | Synthetic sample; zoom from overview to exact rows |
| [`logo.svg`](logo.svg) | Repository/product mark | Source vector for the V plus severity-density motif |
| [`social-preview.jpg`](social-preview.jpg) | GitHub link preview | 1280 × 640 crop of the synthetic analysis workspace |
| [`start-page.jpg`](start-page.jpg) | Secondary README image | Clean desktop start page at the default 1440 × 900 window size |

To recreate the analysis workspace from the repository root:

```shell
dotnet run --project src/VisualCat.Desktop --configuration Release -- --log samples/logcat_small.txt
```

After the import completes, capture the entire application window. Run the same
command without `--log` to reproduce the start page. Keep screenshots free of
private paths, notifications, and device-derived content.

For the demo, record a 15–25 second loop at 1280 × 720 or smaller: start at the
full timeline, drag across a visible burst to zoom, select a severity cell, and
end on the exact record/raw-context pane. Crop to the VisualCat window, remove
idle frames, use 10–15 fps, and optimize the final GIF below 5 MiB. Regenerate
both derived assets whenever the workspace chrome changes materially.

The checked-in loop was captured as four key frames from that interaction. To
rebuild it after capturing replacement PNGs, put the frames in
`.tmp/demo-frames/` as `00-overview.png` through `03-cell-details.png`, then run:

```powershell
pwsh tools/generate-demo.ps1
```

The script emits a 15-second, looping `docs/assets/demo.gif` at 960 pixels wide.
