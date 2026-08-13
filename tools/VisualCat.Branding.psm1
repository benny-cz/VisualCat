<#
.SYNOPSIS
    Shared drawing primitives for VisualCat's generated brand and store assets.

.DESCRIPTION
    The application icon is rendered from code rather than stored as a binary so
    every size is generated from one definition. Both tools/generate-brand-assets.ps1
    (repository and desktop assets) and tools/generate-play-assets.ps1 (Google Play
    assets) import this module, so the store icon cannot drift away from the icon
    the app itself ships.

    System.Drawing is Windows-only from .NET 6 onward, so these tools run on
    Windows. Their outputs are committed or published, and are not needed by CI.
#>

Set-StrictMode -Version Latest
Add-Type -AssemblyName System.Drawing

# The six severity colors, in F, E, W, I, D, V order. They are the product's
# most recognisable visual signature and are reused across the icon, the
# feature graphic, and the screenshot furniture.
$script:SeverityColors = @('#FF2D70', '#FF5A5F', '#F5B942', '#43B4FF', '#21D4B4', '#A78BFA')

$script:Palette = @{
    PlateLight = '#172B48'
    PlateDark  = '#080D18'
    Canvas     = '#0B1220'
    Accent     = '#4DA3FF'
    TextBright = '#F2F6FF'
    TextMuted  = '#8FA6C4'
}

function Get-VisualCatSeverityColors {
    <#
    .SYNOPSIS
        Returns the six severity colors in F, E, W, I, D, V order.
    #>
    [OutputType([string[]])]
    param()
    return $script:SeverityColors
}

function Get-VisualCatPalette {
    <#
    .SYNOPSIS
        Returns the shared brand colors as a hashtable of hex strings.
    #>
    [OutputType([hashtable])]
    param()
    return $script:Palette
}

function New-RoundedRectanglePath {
    <#
    .SYNOPSIS
        Builds a rounded-rectangle GraphicsPath. The caller disposes it.
    #>
    [OutputType([Drawing.Drawing2D.GraphicsPath])]
    param(
        [Parameter(Mandatory)][Drawing.RectangleF]$Bounds,
        [Parameter(Mandatory)][float]$Radius
    )

    $diameter = $Radius * 2
    $path = [Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddArc($Bounds.Left, $Bounds.Top, $diameter, $diameter, 180, 90)
    $path.AddArc($Bounds.Right - $diameter, $Bounds.Top, $diameter, $diameter, 270, 90)
    $path.AddArc($Bounds.Right - $diameter, $Bounds.Bottom - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($Bounds.Left, $Bounds.Bottom - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-VisualCatIconBitmap {
    <#
    .SYNOPSIS
        Renders the VisualCat application icon at the requested size.

    .PARAMETER Size
        Edge length in pixels. The artwork is defined on a 512 px grid and scaled.

    .PARAMETER Margin
        Transparent padding around the plate, in 512-grid units. The default 24
        suits an app or desktop icon that sits on an arbitrary background.
        Google Play wants a full-bleed square it can mask itself, so the store
        icon passes 0.

    .PARAMETER FullBleed
        Renders the store variant: opaque, square to the edges, and without the
        outline stroke. Google Play masks the icon to its own corner radius and
        adds its own shadow, so baking either in produces a doubled corner and a
        stroke the mask cuts unevenly. Implies -Margin 0.
    #>
    [OutputType([Drawing.Bitmap])]
    param(
        [Parameter(Mandatory)][int]$Size,
        [float]$Margin = 24,
        [switch]$FullBleed
    )

    if ($FullBleed) { $Margin = 0 }

    $bitmap = [Drawing.Bitmap]::new($Size, $Size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bitmap.SetResolution(96, 96)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $scale = $Size / 512.0

    $edge = 512 - (2 * $Margin)
    $bounds = [Drawing.RectangleF]::new($Margin * $scale, $Margin * $scale, $edge * $scale, $edge * $scale)
    $plate = if ($FullBleed) {
        $square = [Drawing.Drawing2D.GraphicsPath]::new()
        $square.AddRectangle($bounds)
        $square
    }
    else {
        New-RoundedRectanglePath -Bounds $bounds -Radius (104 * $scale)
    }
    $background = [Drawing.Drawing2D.LinearGradientBrush]::new(
        $bounds,
        [Drawing.ColorTranslator]::FromHtml($script:Palette.PlateLight),
        [Drawing.ColorTranslator]::FromHtml($script:Palette.PlateDark),
        45)
    $graphics.FillPath($background, $plate)

    for ($row = 0; $row -lt $script:SeverityColors.Count; $row++) {
        $color = [Drawing.ColorTranslator]::FromHtml($script:SeverityColors[$row])
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
    if (-not $FullBleed) {
        $outline = [Drawing.Pen]::new([Drawing.Color]::FromArgb(140, 84, 119, 168), [Math]::Max(1, 8 * $scale))
        $graphics.DrawPath($outline, $plate)
        $outline.Dispose()
    }

    $vBrush.Dispose()
    $v.Dispose()
    $background.Dispose()
    $plate.Dispose()
    $graphics.Dispose()
    return $bitmap
}

Export-ModuleMember -Function New-VisualCatIconBitmap, New-RoundedRectanglePath,
Get-VisualCatSeverityColors, Get-VisualCatPalette
