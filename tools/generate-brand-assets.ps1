[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

Add-Type -AssemblyName System.Drawing

function New-RoundedRectanglePath([System.Drawing.RectangleF]$Bounds, [float]$Radius) {
    $diameter = $Radius * 2
    $path = [Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddArc($Bounds.Left, $Bounds.Top, $diameter, $diameter, 180, 90)
    $path.AddArc($Bounds.Right - $diameter, $Bounds.Top, $diameter, $diameter, 270, 90)
    $path.AddArc($Bounds.Right - $diameter, $Bounds.Bottom - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($Bounds.Left, $Bounds.Bottom - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-VisualCatIcon([int]$Size) {
    $bitmap = [Drawing.Bitmap]::new($Size, $Size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bitmap.SetResolution(96, 96)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $scale = $Size / 512.0
    $bounds = [Drawing.RectangleF]::new(24 * $scale, 24 * $scale, 464 * $scale, 464 * $scale)
    $plate = New-RoundedRectanglePath $bounds (104 * $scale)
    $background = [Drawing.Drawing2D.LinearGradientBrush]::new(
        $bounds,
        [Drawing.ColorTranslator]::FromHtml('#172B48'),
        [Drawing.ColorTranslator]::FromHtml('#080D18'),
        45)
    $graphics.FillPath($background, $plate)

    $colors = '#FF2D70', '#FF5A5F', '#F5B942', '#43B4FF', '#21D4B4', '#A78BFA'
    for ($row = 0; $row -lt $colors.Count; $row++) {
        $color = [Drawing.ColorTranslator]::FromHtml($colors[$row])
        $tint = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(34, $color))
        $y = (92 + (52 * $row)) * $scale
        $graphics.FillRectangle($tint, 58 * $scale, $y, 396 * $scale, 38 * $scale)
        $tint.Dispose()
        $positions = 336, 366, 398, 430
        $heights = 20, 12, 24, 15
        for ($index = 0; $index -lt $positions.Count; $index++) {
            $pen = [Drawing.Pen]::new($color, [Math]::Max(2, 12 * $scale))
            $pen.StartCap = $pen.EndCap = [Drawing.Drawing2D.LineCap]::Round
            $x = $positions[$index] * $scale
            $graphics.DrawLine($pen, $x, ($y + 8 * $scale), $x, ($y + (8 + $heights[$index]) * $scale))
            $pen.Dispose()
        }
    }

    $v = [Drawing.Drawing2D.GraphicsPath]::new()
    [Drawing.PointF[]]$points = @(
        [Drawing.PointF]::new(100 * $scale, 114 * $scale),
        [Drawing.PointF]::new(178 * $scale, 114 * $scale),
        [Drawing.PointF]::new(256 * $scale, 316 * $scale),
        [Drawing.PointF]::new(334 * $scale, 114 * $scale),
        [Drawing.PointF]::new(412 * $scale, 114 * $scale),
        [Drawing.PointF]::new(291 * $scale, 416 * $scale),
        [Drawing.PointF]::new(221 * $scale, 416 * $scale)
    )
    $v.AddPolygon($points)
    $vBrush = [Drawing.Drawing2D.LinearGradientBrush]::new(
        [Drawing.RectangleF]::new(100 * $scale, 114 * $scale, 312 * $scale, 302 * $scale),
        [Drawing.Color]::White,
        [Drawing.ColorTranslator]::FromHtml('#D9E9FF'),
        75)
    $graphics.FillPath($vBrush, $v)
    $outline = [Drawing.Pen]::new([Drawing.Color]::FromArgb(140, 84, 119, 168), [Math]::Max(1, 8 * $scale))
    $graphics.DrawPath($outline, $plate)

    $outline.Dispose()
    $vBrush.Dispose()
    $v.Dispose()
    $background.Dispose()
    $plate.Dispose()
    $graphics.Dispose()
    return $bitmap
}

function Save-PngIcon([string]$Path, [int]$Size) {
    $directory = Split-Path -Parent $Path
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $bitmap = New-VisualCatIcon $Size
    try { $bitmap.Save($Path, [Drawing.Imaging.ImageFormat]::Png) }
    finally { $bitmap.Dispose() }
}

function Save-PngBackedIco([string]$Path, [string]$PngPath) {
    $bytes = [IO.File]::ReadAllBytes($PngPath)
    $stream = [IO.File]::Create($Path)
    $writer = [IO.BinaryWriter]::new($stream)
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]1)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$bytes.Length)
        $writer.Write([uint32]22)
        $writer.Write($bytes)
    }
    finally {
        $writer.Dispose()
        $stream.Dispose()
    }
}

function Save-ExactCrop([string]$Source, [string]$Destination, [Drawing.Rectangle]$Crop, [int]$Width, [int]$Height, [Drawing.Imaging.ImageFormat]$Format) {
    $sourceImage = [Drawing.Image]::FromFile($Source)
    $result = [Drawing.Bitmap]::new($Width, $Height, [Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $result.SetResolution(96, 96)
    $graphics = [Drawing.Graphics]::FromImage($result)
    try {
        $graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.DrawImage($sourceImage, [Drawing.Rectangle]::new(0, 0, $Width, $Height), $Crop, [Drawing.GraphicsUnit]::Pixel)
        $result.Save($Destination, $Format)
    }
    finally {
        $graphics.Dispose()
        $result.Dispose()
        $sourceImage.Dispose()
    }
}

$appPng = Join-Path $repository 'src/VisualCat.App/Assets/visualcat-icon.png'
$desktopPng = Join-Path $repository 'src/VisualCat.Desktop/Assets/visualcat-icon-256.png'
$desktopIco = Join-Path $repository 'src/VisualCat.Desktop/Assets/visualcat.ico'
Save-PngIcon $appPng 256
Save-PngIcon $desktopPng 256
Save-PngBackedIco $desktopIco $desktopPng

$analysis = Join-Path $repository 'docs/assets/heatmap-analysis.jpg'
Save-ExactCrop $analysis (Join-Path $repository 'docs/assets/heatmap-hero.jpg') ([Drawing.Rectangle]::new(0, 112, 3840, 1920)) 1600 800 ([Drawing.Imaging.ImageFormat]::Jpeg)
Save-ExactCrop $analysis (Join-Path $repository 'docs/assets/social-preview.jpg') ([Drawing.Rectangle]::new(0, 112, 3840, 1920)) 1280 640 ([Drawing.Imaging.ImageFormat]::Jpeg)
