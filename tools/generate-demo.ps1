[CmdletBinding()]
param(
    [string]$InputDirectory,
    [string]$OutputPath,
    [ValidateRange(320, 1280)]
    [int]$Width = 960
)

$ErrorActionPreference = 'Stop'

function Get-GifFrameBlock {
    param([Parameter(Mandatory)][byte[]]$Bytes)

    if ($Bytes.Length -lt 14 -or
        [System.Text.Encoding]::ASCII.GetString($Bytes, 0, 3) -ne 'GIF') {
        throw 'The generated frame is not a valid GIF image.'
    }

    $logicalPacked = $Bytes[10]
    $globalTableSizeCode = $logicalPacked -band 0x07
    $globalTableLength = if (($logicalPacked -band 0x80) -ne 0) {
        3 * (1 -shl ($globalTableSizeCode + 1))
    }
    else {
        0
    }

    $globalTable = if ($globalTableLength -gt 0) {
        [byte[]]$Bytes[13..(12 + $globalTableLength)]
    }
    else {
        [byte[]]@()
    }

    $position = 13 + $globalTableLength
    while ($position -lt $Bytes.Length) {
        switch ($Bytes[$position]) {
            0x21 {
                # Skip an extension block from the one-frame source GIF.
                $position += 2
                while ($position -lt $Bytes.Length) {
                    $length = $Bytes[$position]
                    $position++
                    if ($length -eq 0) {
                        break
                    }

                    $position += $length
                }
            }
            0x2C {
                $descriptorStart = $position
                [byte[]]$descriptor = $Bytes[$descriptorStart..($descriptorStart + 9)]
                $localPacked = $descriptor[9]
                $hasLocalTable = ($localPacked -band 0x80) -ne 0
                $tableSizeCode = if ($hasLocalTable) { $localPacked -band 0x07 } else { $globalTableSizeCode }
                $tableLength = 3 * (1 -shl ($tableSizeCode + 1))
                $tableStart = $descriptorStart + 10

                [byte[]]$colorTable = if ($hasLocalTable) {
                    $Bytes[$tableStart..($tableStart + $tableLength - 1)]
                }
                else {
                    $globalTable
                }

                if ($colorTable.Length -ne $tableLength) {
                    throw 'The generated GIF frame has no usable color table.'
                }

                # The combined GIF has no global palette. Give every frame its
                # original palette as a local table instead.
                $descriptor[9] = [byte](($localPacked -band 0x78) -bor 0x80 -bor $tableSizeCode)
                $imageDataStart = $tableStart + $(if ($hasLocalTable) { $tableLength } else { 0 })
                $position = $imageDataStart + 1 # LZW minimum code size
                while ($position -lt $Bytes.Length) {
                    $length = $Bytes[$position]
                    $position++
                    if ($length -eq 0) {
                        break
                    }

                    $position += $length
                }

                if ($position -gt $Bytes.Length) {
                    throw 'The generated GIF frame has truncated image data.'
                }

                return [pscustomobject]@{
                    Width = [BitConverter]::ToUInt16($Bytes, 6)
                    Height = [BitConverter]::ToUInt16($Bytes, 8)
                    LogicalPacked = $logicalPacked
                    Descriptor = $descriptor
                    ColorTable = $colorTable
                    ImageData = [byte[]]$Bytes[$imageDataStart..($position - 1)]
                }
            }
            0x3B {
                throw 'The generated GIF contains no image block.'
            }
            default {
                throw "Unexpected GIF block marker 0x$($Bytes[$position].ToString('X2'))."
            }
        }
    }

    throw 'The generated GIF ended before its image block.'
}

$repository = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($InputDirectory)) {
    $InputDirectory = Join-Path $repository '.tmp/demo-frames'
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repository 'docs/assets/demo.gif'
}

$frameSpecs = @(
    @{ Name = '00-overview.png'; Delay = 300 },
    @{ Name = '01-burst-hover.png'; Delay = 300 },
    @{ Name = '02-zoomed.png'; Delay = 350 },
    @{ Name = '03-cell-details.png'; Delay = 550 }
)

$resolvedInput = (Resolve-Path -LiteralPath $InputDirectory).Path
$resolvedFrames = foreach ($spec in $frameSpecs) {
    $candidate = Join-Path $resolvedInput $spec.Name
    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
        throw "Missing demo frame: $candidate"
    }

    @{ Path = (Resolve-Path -LiteralPath $candidate).Path; Delay = $spec.Delay }
}

Add-Type -AssemblyName System.Drawing

$temporaryRoot = Join-Path $repository '.tmp'
[System.IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
$temporaryDirectory = Join-Path $temporaryRoot "visualcat-demo-$([Guid]::NewGuid().ToString('N'))"
[System.IO.Directory]::CreateDirectory($temporaryDirectory) | Out-Null
try {
    $gifFrames = for ($index = 0; $index -lt $resolvedFrames.Count; $index++) {
        $source = [System.Drawing.Image]::FromFile($resolvedFrames[$index].Path)
        try {
            $height = [Math]::Round($Width * $source.Height / $source.Width)
            $bitmap = [System.Drawing.Bitmap]::new(
                $Width,
                $height,
                [System.Drawing.Imaging.PixelFormat]::Format24bppRgb
            )
            try {
                $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
                try {
                    $graphics.Clear([System.Drawing.Color]::Black)
                    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
                    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                    $graphics.DrawImage($source, 0, 0, $Width, $height)
                }
                finally {
                    $graphics.Dispose()
                }

                $framePath = Join-Path $temporaryDirectory "$index.gif"
                $bitmap.Save($framePath, [System.Drawing.Imaging.ImageFormat]::Gif)
                $block = Get-GifFrameBlock ([System.IO.File]::ReadAllBytes($framePath))
                $block | Add-Member -NotePropertyName Delay -NotePropertyValue $resolvedFrames[$index].Delay
                $block
            }
            finally {
                $bitmap.Dispose()
            }
        }
        finally {
            $source.Dispose()
        }
    }

    if (($gifFrames | Select-Object -ExpandProperty Width -Unique).Count -ne 1 -or
        ($gifFrames | Select-Object -ExpandProperty Height -Unique).Count -ne 1) {
        throw 'All demo frames must have the same dimensions.'
    }

    $outputDirectory = Split-Path -Parent $OutputPath
    [System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
    $stream = [System.IO.File]::Open(
        $OutputPath,
        [System.IO.FileMode]::Create,
        [System.IO.FileAccess]::Write,
        [System.IO.FileShare]::None
    )
    $writer = [System.IO.BinaryWriter]::new($stream)
    try {
        $writer.Write([System.Text.Encoding]::ASCII.GetBytes('GIF89a'))
        $writer.Write([UInt16]$gifFrames[0].Width)
        $writer.Write([UInt16]$gifFrames[0].Height)
        $writer.Write([Byte]($gifFrames[0].LogicalPacked -band 0x70))
        $writer.Write([Byte]0)
        $writer.Write([Byte]0)

        # NETSCAPE2.0 application extension: loop forever.
        $writer.Write([Byte[]](0x21, 0xFF, 0x0B))
        $writer.Write([System.Text.Encoding]::ASCII.GetBytes('NETSCAPE2.0'))
        $writer.Write([Byte[]](0x03, 0x01, 0x00, 0x00, 0x00))

        foreach ($frame in $gifFrames) {
            # Graphic control extension: keep the current frame until replaced.
            $writer.Write([Byte[]](0x21, 0xF9, 0x04, 0x04))
            $writer.Write([UInt16]$frame.Delay)
            $writer.Write([Byte[]](0x00, 0x00))
            $writer.Write([byte[]]$frame.Descriptor)
            $writer.Write([byte[]]$frame.ColorTable)
            $writer.Write([byte[]]$frame.ImageData)
        }

        $writer.Write([Byte]0x3B)
    }
    finally {
        $writer.Dispose()
    }
}
finally {
    if (Test-Path -LiteralPath $temporaryDirectory) {
        Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force
    }
}

$result = Get-Item -LiteralPath $OutputPath
Write-Host "Wrote $($result.FullName) ($([Math]::Round($result.Length / 1MB, 2)) MiB)."
