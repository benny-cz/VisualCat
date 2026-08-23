<#
.SYNOPSIS
    Generates the Google Play store assets for com.barebit.visualcat.

.DESCRIPTION
    Produces, into artifacts/play/:

      * icon-512.png              the store icon, from the same definition the
                                  application icon is drawn from;
      * feature-graphic-1024x500.png;
      * phone-NN-<slug>.png       1080x1920 phone screenshots, composed from raw
                                  device captures in artifacts/play/raw/.

    Play accepts screenshots between 320 px and 3840 px on a side, but only
    promotes apps whose screenshots are at least 1080 px and exactly 16:9 or
    9:16. A phone is not 9:16 — this device is 1220x2712, which is 9:20 — so a
    raw capture is placed on a 1080x1920 canvas rather than stretched or
    cropped to a shape the device never had.

    Raw captures are not committed. They come from real hardware running the
    real build, and -Capture re-records them from an attached device.

.PARAMETER DemoLog
    Writes a deterministic synthetic logcat session and exits. This is the
    session shown in the screenshots. It is generated rather than captured
    because a real device log would put whatever that device happened to be
    doing — account names, network names, notification text — on a public store
    page, and because a uniformly random fixture shows six flat blocks instead
    of the bursts and quiet gaps the product exists to make visible.

.PARAMETER RawDirectory
    Where the raw device captures are read from. Defaults to artifacts/play/raw.

.PARAMETER Output
    Where the finished assets are written. Defaults to artifacts/play.

.EXAMPLE
    pwsh ./tools/generate-play-assets.ps1 -DemoLog demo-session.txt
    pwsh ./tools/generate-play-assets.ps1
#>
[CmdletBinding()]
param(
    [string]$DemoLog,
    [string]$RawDirectory = 'artifacts/play/raw',
    [string]$Output = 'artifacts/play'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Add-Type -AssemblyName System.Drawing
Import-Module (Join-Path $PSScriptRoot 'VisualCat.Branding.psm1') -Force

$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$palette = Get-VisualCatPalette
$severity = Get-VisualCatSeverityColors

function Resolve-RepositoryPath([string]$Path) {
    if ([IO.Path]::IsPathRooted($Path)) { return $Path }
    return [IO.Path]::GetFullPath((Join-Path $repository $Path))
}

# --- demo session --------------------------------------------------------

<#
    A session with shape. Each phase has its own event rate and severity mix, so
    the heat map shows a boot burst, idle stretches, a network degradation, a
    crash storm, and a long quiet gap — the structure the product claims to
    reveal. Nothing here is device-derived.
#>
function Write-DemoLog {
    param([Parameter(Mandatory)][string]$Path)

    $phases = @(
        # Name          Seconds  Lines/s  F   E   W   I   D   V
        @{ Name = 'boot'; Seconds = 210; Rate = 120; Weights = @(0, 1, 6, 34, 38, 21) }
        @{ Name = 'idle'; Seconds = 420; Rate = 4; Weights = @(0, 0, 2, 10, 28, 60) }
        @{ Name = 'network'; Seconds = 260; Rate = 45; Weights = @(0, 14, 38, 26, 14, 8) }
        @{ Name = 'settle'; Seconds = 200; Rate = 9; Weights = @(0, 2, 8, 25, 35, 30) }
        @{ Name = 'crash'; Seconds = 95; Rate = 210; Weights = @(9, 46, 24, 12, 6, 3) }
        @{ Name = 'recovery'; Seconds = 280; Rate = 38; Weights = @(0, 9, 26, 33, 20, 12) }
        @{ Name = 'quiet'; Seconds = 480; Rate = 2; Weights = @(0, 0, 1, 8, 26, 65) }
        @{ Name = 'usage'; Seconds = 560; Rate = 55; Weights = @(0, 2, 9, 38, 33, 18) }
    )

    $letters = @('F', 'E', 'W', 'I', 'D', 'V')
    $tags = @(
        @('AndroidRuntime', 'ActivityManager'),
        @('AndroidRuntime', 'ConnectivityService', 'SQLiteLog', 'CameraService', 'OkHttp'),
        @('StrictMode', 'Choreographer', 'ConnectivityService', 'WindowManager', 'PackageManager'),
        @('ActivityTaskManager', 'ActivityManager', 'PackageManager', 'MediaCodec', 'VisualCat'),
        @('SurfaceFlinger', 'Choreographer', 'MediaCodec', 'VisualCat', 'OkHttp'),
        @('chatty', 'SurfaceFlinger', 'VisualCat', 'MediaCodec')
    )
    # {0} identifier, {1} duration in milliseconds, {2} small count. Keeping the
    # magnitudes plausible matters: a store screenshot showing "Skipped 84,102
    # frames" tells a reader the data is fake before they read anything else.
    $messages = @(
        @('FATAL EXCEPTION: main', 'Process com.example.app (pid {0}) has died: fg TOP'),
        @('Connection to 10.0.0.8 failed after {1} ms', 'database is locked (code 5): beginTransaction',
            'Failed to open camera device: CAMERA_DISCONNECTED', 'Response 503 after {1} ms, giving up'),
        @('Slow operation on main thread: {1} ms', 'Skipped {2} frames, the application may be doing too much work',
            'Suppressed StrictMode policy violation: DiskReadViolation', 'Retrying request {0} after backoff'),
        @('Displayed com.example.app/.MainActivity: +{1}ms', 'Started process {0} for package com.example.app',
            'Codec configured: video/avc 1920x1080', 'Session snapshot {2} committed'),
        @('Frame completed in {1} ms', 'Cache contains {0} entries', 'Decoded buffer {0} in {1} ms',
            'Scheduling next poll in {1} ms'),
        @('Rendering surface 0x{0:X8}', 'uid=10007(com.example) identical {2} lines', 'Vsync {0} dispatched')
    )

    $random = [Random]::new(20260812)
    $instant = [DateTimeOffset]::new(2026, 3, 14, 9, 12, 41, [TimeSpan]::Zero)
    $stream = [IO.StreamWriter]::new($Path, $false, [Text.UTF8Encoding]::new($false), 1024 * 1024)
    $stream.NewLine = "`n"
    try {
        $stream.WriteLine('--------- beginning of main')
        $written = 0
        foreach ($phase in $phases) {
            $lines = [int]($phase.Seconds * $phase.Rate)
            $weights = $phase.Weights
            $total = ($weights | Measure-Object -Sum).Sum
            # Microseconds between events, before jitter.
            $step = [double]$phase.Seconds * 1000000.0 / [Math]::Max($lines, 1)

            for ($index = 0; $index -lt $lines; $index++) {
                $instant = $instant.AddTicks([long]([Math]::Max(1, $step * (0.35 + ($random.NextDouble() * 1.3))) * 10))

                $roll = $random.Next(0, $total)
                $level = 0
                $cumulative = 0
                for ($candidate = 0; $candidate -lt $weights.Count; $candidate++) {
                    $cumulative += $weights[$candidate]
                    if ($roll -lt $cumulative) { $level = $candidate; break }
                }

                $tag = $tags[$level][$random.Next($tags[$level].Count)]
                $pattern = $messages[$level][$random.Next($messages[$level].Count)]
                $message = [string]::Format(
                    [Globalization.CultureInfo]::InvariantCulture,
                    $pattern,
                    $random.Next(1000, 99999),
                    $random.Next(2, 1200),
                    $random.Next(2, 90))
                # Built before the call: inside WriteLine(...) the commas would
                # be argument separators, not -f operands.
                $line = '{0} {1,5} {2,5} {3} {4,-16}: {5}' -f
                    $instant.ToString('MM-dd HH:mm:ss.ffffff', [Globalization.CultureInfo]::InvariantCulture),
                    $random.Next(300, 19000),
                    $random.Next(300, 28000),
                    $letters[$level],
                    $tag,
                    $message
                $stream.WriteLine($line)
                $written++
            }
        }
    }
    finally {
        $stream.Dispose()
    }

    return $written
}

if ($DemoLog) {
    $target = Resolve-RepositoryPath $DemoLog
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    $count = Write-DemoLog -Path $target
    $size = '{0:N1} MB' -f ((Get-Item -LiteralPath $target).Length / 1MB)
    Write-Host "Wrote $count synthetic entries ($size) to $target"
    return
}

# --- drawing helpers -----------------------------------------------------

function New-Canvas {
    param(
        [Parameter(Mandatory)][int]$Width,
        [Parameter(Mandatory)][int]$Height,
        [string]$Top = '#101B2E',
        [string]$Bottom = '#05080F'
    )

    # 24bpp: Google Play rejects alpha in feature graphics and screenshots.
    $bitmap = [Drawing.Bitmap]::new($Width, $Height, [Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $bitmap.SetResolution(96, 96)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
    $graphics.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAliasGridFit

    $gradient = [Drawing.Drawing2D.LinearGradientBrush]::new(
        [Drawing.Rectangle]::new(0, 0, $Width, $Height),
        [Drawing.ColorTranslator]::FromHtml($Top),
        [Drawing.ColorTranslator]::FromHtml($Bottom),
        [Drawing.Drawing2D.LinearGradientMode]::Vertical)
    $graphics.FillRectangle($gradient, 0, 0, $Width, $Height)
    $gradient.Dispose()

    return [pscustomobject]@{ Bitmap = $bitmap; Graphics = $graphics }
}

function Add-SeverityRule {
    <#
    .SYNOPSIS
        Draws the six-colour severity rule that identifies the product at a glance.
    #>
    param(
        [Parameter(Mandatory)][Drawing.Graphics]$Graphics,
        [Parameter(Mandatory)][int]$X,
        [Parameter(Mandatory)][int]$Y,
        [Parameter(Mandatory)][int]$Width,
        [Parameter(Mandatory)][int]$Height
    )

    $segment = $Width / [double]$severity.Count
    for ($index = 0; $index -lt $severity.Count; $index++) {
        $brush = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($severity[$index]))
        $Graphics.FillRectangle($brush, [float]($X + ($index * $segment)), [float]$Y, [float]$segment, [float]$Height)
        $brush.Dispose()
    }
}

function Add-CenteredText {
    param(
        [Parameter(Mandatory)][Drawing.Graphics]$Graphics,
        [Parameter(Mandatory)][string]$Text,
        [Parameter(Mandatory)][Drawing.RectangleF]$Bounds,
        [Parameter(Mandatory)][float]$Size,
        [Drawing.FontStyle]$Style = [Drawing.FontStyle]::Regular,
        [string]$Color = '#F2F6FF'
    )

    $font = [Drawing.Font]::new('Segoe UI', $Size, $Style, [Drawing.GraphicsUnit]::Pixel)
    $format = [Drawing.StringFormat]::new()
    $format.Alignment = [Drawing.StringAlignment]::Center
    $format.LineAlignment = [Drawing.StringAlignment]::Near
    $brush = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($Color))
    try {
        $Graphics.DrawString($Text, $font, $brush, $Bounds, $format)
    }
    finally {
        $brush.Dispose()
        $format.Dispose()
        $font.Dispose()
    }
}

function Save-Png {
    param(
        [Parameter(Mandatory)][Drawing.Bitmap]$Bitmap,
        [Parameter(Mandatory)][string]$Path
    )

    New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
    $Bitmap.Save($Path, [Drawing.Imaging.ImageFormat]::Png)
    $size = '{0:N0} KB' -f ((Get-Item -LiteralPath $Path).Length / 1KB)
    Write-Host ("  {0,-38} {1,4}x{2,-4} {3}" -f (Split-Path -Leaf $Path), $Bitmap.Width, $Bitmap.Height, $size)
}

# --- assets --------------------------------------------------------------

$outputRoot = Resolve-RepositoryPath $Output
$rawRoot = Resolve-RepositoryPath $RawDirectory
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

Write-Host 'Google Play assets:'

# Store icon. Play masks and shadows the icon itself, so this one is full-bleed
# rather than the inset, rounded, transparent icon the desktop build uses.
$icon = New-VisualCatIconBitmap -Size 512 -FullBleed
try {
    Save-Png -Bitmap $icon -Path (Join-Path $outputRoot 'icon-512.png')
}
finally {
    $icon.Dispose()
}

# Feature graphic.
$feature = New-Canvas -Width 1024 -Height 500 -Top '#16294A' -Bottom '#070C16'
try {
    $graphics = $feature.Graphics

    # A deterministic heat map behind the wordmark: the product's own output,
    # used as texture rather than as a claim about any particular session.
    $random = [Random]::new(512)
    $laneHeight = 44
    $laneTop = 96
    for ($lane = 0; $lane -lt $severity.Count; $lane++) {
        $color = [Drawing.ColorTranslator]::FromHtml($severity[$lane])
        $y = $laneTop + ($lane * ($laneHeight + 8))
        $wash = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(20, $color))
        $graphics.FillRectangle($wash, 0, $y, 1024, $laneHeight)
        $wash.Dispose()
        for ($x = 4; $x -lt 1024; $x += 5) {
            # Density rises to the right so the graphic reads as a burst.
            $chance = 0.18 + (0.62 * ($x / 1024.0))
            if ($random.NextDouble() -gt $chance) { continue }
            $height = [int](6 + ($random.NextDouble() * ($laneHeight - 8)))
            $bar = [Drawing.SolidBrush]::new(
                [Drawing.Color]::FromArgb(150, $color))
            $graphics.FillRectangle($bar, $x, $y + $laneHeight - $height, 3, $height)
            $bar.Dispose()
        }
    }

    # The scrim holds nearly full strength across the text column and only then
    # releases the texture. A plain two-stop fade leaves the smallest line of
    # type sitting on bars at the exact size where it stops being readable.
    $scrim = [Drawing.Drawing2D.LinearGradientBrush]::new(
        [Drawing.Rectangle]::new(-1, 0, 1026, 500),
        [Drawing.Color]::Black,
        [Drawing.Color]::Black,
        [Drawing.Drawing2D.LinearGradientMode]::Horizontal)
    $blend = [Drawing.Drawing2D.ColorBlend]::new(4)
    $blend.Colors = @(
        [Drawing.Color]::FromArgb(250, 8, 13, 24),
        [Drawing.Color]::FromArgb(244, 8, 13, 24),
        [Drawing.Color]::FromArgb(150, 8, 13, 24),
        [Drawing.Color]::FromArgb(40, 8, 13, 24)
    )
    $blend.Positions = @(0.0, 0.62, 0.82, 1.0)
    $scrim.InterpolationColors = $blend
    $graphics.FillRectangle($scrim, 0, 0, 1024, 500)
    $scrim.Dispose()

    $badge = New-VisualCatIconBitmap -Size 168
    try {
        $graphics.DrawImage($badge, 64, 166, 168, 168)
    }
    finally {
        $badge.Dispose()
    }

    $title = [Drawing.Font]::new('Segoe UI', 76, [Drawing.FontStyle]::Bold, [Drawing.GraphicsUnit]::Pixel)
    $tagline = [Drawing.Font]::new('Segoe UI', 32, [Drawing.FontStyle]::Regular, [Drawing.GraphicsUnit]::Pixel)
    $detail = [Drawing.Font]::new('Segoe UI', 22, [Drawing.FontStyle]::Regular, [Drawing.GraphicsUnit]::Pixel)
    $bright = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($palette.TextBright))
    $muted = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($palette.TextMuted))
    try {
        $graphics.DrawString('VisualCat', $title, $bright, 250, 158)
        $graphics.DrawString('See the shape of your log', $tagline, $muted, 256, 258)
        $graphics.DrawString('Android logcat  ·  severity x time  ·  on-device', $detail, $muted, 257, 308)
    }
    finally {
        $muted.Dispose()
        $bright.Dispose()
        $detail.Dispose()
        $tagline.Dispose()
        $title.Dispose()
    }

    Save-Png -Bitmap $feature.Bitmap -Path (Join-Path $outputRoot 'feature-graphic-1024x500.png')
}
finally {
    $feature.Graphics.Dispose()
    $feature.Bitmap.Dispose()
}

# Phone screenshots.
<#
    Captured on a Motorola edge 60 pro (1220x2712, Android 16). The system
    status and navigation bars are cropped: they belong to the device, not to
    the app, and Play renders these small enough that every pixel spent on
    someone else's clock is wasted.
#>
$deviceCrop = [Drawing.Rectangle]::new(0, 130, 1220, 2470)

# Ordered for the store, not for the capture session. Play gives the first
# screenshot by far the most attention, so the heat map leads and the launch
# screen — the least informative one — comes last.
$screenshots = @(
    @{ Source = 'phone-02-heatmap.png'; Slug = 'heatmap'
        Headline = 'See the shape of your log'
        Subhead = 'A whole logcat session as a severity-by-time heat map.'
    }
    @{ Source = 'phone-03-zoom.png'; Slug = 'zoom'
        Headline = 'Zoom into the burst'
        Subhead = 'Drill from the whole session down to individual records.'
    }
    @{ Source = 'phone-05-details.png'; Slug = 'details'
        Headline = 'Down to the exact record'
        Subhead = 'Timestamp, PID, TID and tag for every entry.'
    }
    @{ Source = 'phone-06-insights.png'; Slug = 'insights'
        Headline = 'Collapse the spam into patterns'
        Subhead = 'Ranked message templates show what is really repeating.'
    }
    @{ Source = 'phone-04-filters.png'; Slug = 'filters'
        Headline = 'Filter without losing the picture'
        Subhead = 'Text or regex, severity toggles, and a time lens.'
        # The filter workspace ends part-way down the screen. Cropping to it
        # spends the canvas on the control rather than on empty background.
        Crop = [Drawing.Rectangle]::new(0, 130, 1220, 1720)
    }
    @{ Source = 'phone-07-source.png'; Slug = 'source'
        Headline = 'Byte-faithful source context'
        Subhead = 'What the log actually said, not a reformatted copy.'
        Crop = [Drawing.Rectangle]::new(0, 130, 1220, 1830)
    }
    @{ Source = 'phone-08-live.png'; Slug = 'live'
        Headline = 'Capture this device live'
        Subhead = 'Own-app immediately. Full-device through Android Wireless debugging.'
    }
    @{ Source = 'phone-01-start.png'; Slug = 'start'
        Headline = 'Open a capture, or start one'
        Subhead = 'A file, a session shared from the desktop, or this device.'
        Crop = [Drawing.Rectangle]::new(0, 130, 1220, 1700)
    }
)

$index = 0
foreach ($screenshot in $screenshots) {
    $index++
    $sourcePath = Join-Path $rawRoot $screenshot.Source
    if (-not (Test-Path -LiteralPath $sourcePath)) {
        Write-Warning "Skipping $($screenshot.Slug): '$sourcePath' has not been captured."
        continue
    }

    $canvas = New-Canvas -Width 1080 -Height 1920
    try {
        $graphics = $canvas.Graphics
        Add-SeverityRule -Graphics $graphics -X 0 -Y 0 -Width 1080 -Height 7

        Add-CenteredText -Graphics $graphics -Text $screenshot.Headline `
            -Bounds ([Drawing.RectangleF]::new(70, 84, 940, 140)) -Size 52 -Style Bold
        Add-CenteredText -Graphics $graphics -Text $screenshot.Subhead `
            -Bounds ([Drawing.RectangleF]::new(90, 232, 900, 90)) -Size 27 -Color $palette.TextMuted

        $source = [Drawing.Image]::FromFile($sourcePath)
        try {
            $crop = if ($screenshot.ContainsKey('Crop')) {
                $screenshot.Crop
            }
            elseif ($source.Width -eq 1220 -and $source.Height -eq 2712) {
                $deviceCrop
            }
            else {
                [Drawing.Rectangle]::new(0, 0, $source.Width, $source.Height)
            }

            $maximumWidth = 900.0
            $maximumHeight = 1520.0
            $scale = [Math]::Min($maximumWidth / $crop.Width, $maximumHeight / $crop.Height)
            $width = [int]($crop.Width * $scale)
            $height = [int]($crop.Height * $scale)
            $left = [int]((1080 - $width) / 2)
            $top = [int](332 + (($maximumHeight - $height) / 2))
            $frame = [Drawing.RectangleF]::new($left, $top, $width, $height)

            # A soft stack of rounded rectangles reads as a shadow and lifts the
            # capture off the background without a blur filter GDI+ lacks.
            for ($ring = 7; $ring -ge 1; $ring--) {
                $spread = $ring * 3
                $halo = New-RoundedRectanglePath -Bounds ([Drawing.RectangleF]::new(
                        $frame.Left - $spread, $frame.Top - $spread + 6,
                        $frame.Width + (2 * $spread), $frame.Height + (2 * $spread))) -Radius (26 + $spread)
                $shadow = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(9, 0, 0, 0))
                $graphics.FillPath($shadow, $halo)
                $shadow.Dispose()
                $halo.Dispose()
            }

            $clip = New-RoundedRectanglePath -Bounds $frame -Radius 26
            $graphics.SetClip($clip)
            $graphics.DrawImage(
                $source,
                [Drawing.Rectangle]::new($left, $top, $width, $height),
                $crop,
                [Drawing.GraphicsUnit]::Pixel)
            $graphics.ResetClip()

            $border = [Drawing.Pen]::new([Drawing.Color]::FromArgb(150, 45, 68, 102), 2)
            $graphics.DrawPath($border, $clip)
            $border.Dispose()
            $clip.Dispose()
        }
        finally {
            $source.Dispose()
        }

        Save-Png -Bitmap $canvas.Bitmap -Path (Join-Path $outputRoot ('phone-{0:D2}-{1}.png' -f $index, $screenshot.Slug))
    }
    finally {
        $canvas.Graphics.Dispose()
        $canvas.Bitmap.Dispose()
    }
}

Write-Host ''
Write-Host "Assets written to $outputRoot"
