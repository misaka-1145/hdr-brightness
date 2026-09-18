# Generates assets/app.ico (multi-size PNG-in-ICO): dark rounded tile with a sun.
# Keep this file ASCII-only (Windows PowerShell 5.1 reads scripts as ANSI without a BOM).
param(
    [string]$OutputPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'assets\app.ico')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    # rounded background
    $radius = [float]($size * 0.24)
    $rect = New-Object System.Drawing.RectangleF(0, 0, $size, $size)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $radius, $radius, 180, 90)
    $path.AddArc($size - $radius, 0, $radius, $radius, 270, 90)
    $path.AddArc($size - $radius, $size - $radius, $radius, $radius, 0, 90)
    $path.AddArc(0, $size - $radius, $radius, $radius, 90, 90)
    $path.CloseFigure()

    $bgBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect,
        [System.Drawing.Color]::FromArgb(255, 24, 29, 44),
        [System.Drawing.Color]::FromArgb(255, 43, 56, 92),
        45.0)
    $g.FillPath($bgBrush, $path)

    $cx = [float]($size / 2.0)
    $cy = [float]($size / 2.0)
    $sunRadius = [float]($size * 0.21)

    # rays
    $penWidth = [float][Math]::Max(1.0, $size * 0.062)
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 255, 206, 122), $penWidth)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    for ($i = 0; $i -lt 8; $i++) {
        $angle = [Math]::PI * 2 * $i / 8.0
        $inner = $sunRadius * 1.55
        $outer = $sunRadius * 2.35
        $x1 = [float]($cx + [Math]::Cos($angle) * $inner)
        $y1 = [float]($cy + [Math]::Sin($angle) * $inner)
        $x2 = [float]($cx + [Math]::Cos($angle) * $outer)
        $y2 = [float]($cy + [Math]::Sin($angle) * $outer)
        $g.DrawLine($pen, $x1, $y1, $x2, $y2)
    }
    $pen.Dispose()

    # sun
    $sunRect = New-Object System.Drawing.RectangleF(($cx - $sunRadius), ($cy - $sunRadius), ($sunRadius * 2), ($sunRadius * 2))
    $sunBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $sunRect,
        [System.Drawing.Color]::FromArgb(255, 255, 246, 224),
        [System.Drawing.Color]::FromArgb(255, 255, 186, 74),
        90.0)
    $g.FillEllipse($sunBrush, $sunRect)

    $sunBrush.Dispose()
    $bgBrush.Dispose()
    $path.Dispose()
    $g.Dispose()
    return $bmp
}

# render each size to PNG first, then pack them into an ICO container
$pngBlobs = @()
foreach ($size in $sizes) {
    $bmp = New-IconBitmap $size
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngBlobs += ,$ms.ToArray()
    $ms.Dispose()
    $bmp.Dispose()
}

$directory = Split-Path -Parent $OutputPath
if (-not (Test-Path $directory)) { New-Item -ItemType Directory -Path $directory | Out-Null }

$out = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($out)
$writer.Write([UInt16]0)                 # reserved
$writer.Write([UInt16]1)                 # type: icon
$writer.Write([UInt16]$sizes.Count)      # image count

$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $size = $sizes[$i]
    $bytes = $pngBlobs[$i]
    $dimension = if ($size -ge 256) { 0 } else { $size }
    $writer.Write([byte]$dimension)      # width
    $writer.Write([byte]$dimension)      # height
    $writer.Write([byte]0)               # palette
    $writer.Write([byte]0)               # reserved
    $writer.Write([UInt16]1)             # color planes
    $writer.Write([UInt16]32)            # bits per pixel
    $writer.Write([UInt32]$bytes.Length)
    $writer.Write([UInt32]$offset)
    $offset += $bytes.Length
}

foreach ($bytes in $pngBlobs) { $writer.Write($bytes) }
$writer.Flush()

[System.IO.File]::WriteAllBytes($OutputPath, $out.ToArray())
$writer.Dispose()
$out.Dispose()

Write-Host "Icon written: $OutputPath ($($sizes.Count) sizes)"
