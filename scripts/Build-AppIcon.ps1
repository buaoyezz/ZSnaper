param(
    [string]$SourceSvg = (Join-Path (Split-Path $PSScriptRoot -Parent) 'assets\logo\icon-light.svg'),
    [string]$OutputIco = (Join-Path (Split-Path $PSScriptRoot -Parent) 'assets\logo\ZSnaper.ico'),
    [string]$PreviewPng = '',
    [ValidateSet('Light', 'Dark')]
    [string]$Scheme = 'Light'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-RoundedRectanglePath {
    param(
        [float]$X,
        [float]$Y,
        [float]$Width,
        [float]$Height,
        [float]$Radius
    )

    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = $Radius * 2
    $path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $path.AddArc($X + $Width - $diameter, $Y, $diameter, $diameter, 270, 90)
    $path.AddArc($X + $Width - $diameter, $Y + $Height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($X, $Y + $Height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function ConvertTo-IcoDibFrame {
    param([System.Drawing.Bitmap]$Bitmap)

    $width = $Bitmap.Width
    $height = $Bitmap.Height
    $maskStride = [int]([Math]::Ceiling($width / 32.0) * 4)
    $memory = [System.IO.MemoryStream]::new()
    $writer = [System.IO.BinaryWriter]::new($memory)
    try {
        # BITMAPINFOHEADER. ICO stores XOR and AND masks one above the other,
        # so the declared height is doubled.
        $writer.Write([uint32]40)
        $writer.Write([int32]$width)
        $writer.Write([int32]($height * 2))
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]0)
        $writer.Write([uint32]($width * $height * 4))
        $writer.Write([int32]0)
        $writer.Write([int32]0)
        $writer.Write([uint32]0)
        $writer.Write([uint32]0)

        # 32-bit BGRA pixels are stored bottom-up.
        for ($y = $height - 1; $y -ge 0; $y--) {
            for ($x = 0; $x -lt $width; $x++) {
                $pixel = $Bitmap.GetPixel($x, $y)
                $writer.Write([byte]$pixel.B)
                $writer.Write([byte]$pixel.G)
                $writer.Write([byte]$pixel.R)
                $writer.Write([byte]$pixel.A)
            }
        }

        # Keep a standards-compliant 1-bit transparency mask for legacy icon readers.
        for ($y = $height - 1; $y -ge 0; $y--) {
            $maskRow = [byte[]]::new($maskStride)
            for ($x = 0; $x -lt $width; $x++) {
                if ($Bitmap.GetPixel($x, $y).A -eq 0) {
                    $byteIndex = [int][Math]::Floor($x / 8.0)
                    $maskRow[$byteIndex] = $maskRow[$byteIndex] -bor (1 -shl (7 - ($x % 8)))
                }
            }
            $writer.Write($maskRow)
        }

        return $memory.ToArray()
    }
    finally {
        $writer.Dispose()
        $memory.Dispose()
    }
}

if (-not (Test-Path -LiteralPath $SourceSvg)) {
    throw "Logo SVG not found: $SourceSvg"
}

[xml]$svg = Get-Content -LiteralPath $SourceSvg -Raw -Encoding UTF8
$group = $svg.SelectSingleNode("//*[local-name()='g']")
if ($null -eq $group) {
    throw 'The logo SVG does not contain a path group.'
}

$translateX = 0.0
$translateY = 0.0
$transformMatch = [regex]::Match($group.GetAttribute('transform'), 'translate\(\s*(?<x>-?\d+(?:\.\d+)?)\s*[, ]\s*(?<y>-?\d+(?:\.\d+)?)\s*\)')
if ($transformMatch.Success) {
    $translateX = [double]::Parse($transformMatch.Groups['x'].Value, [Globalization.CultureInfo]::InvariantCulture)
    $translateY = [double]::Parse($transformMatch.Groups['y'].Value, [Globalization.CultureInfo]::InvariantCulture)
}

$polygons = [System.Collections.Generic.List[System.Drawing.PointF[]]]::new()
foreach ($pathNode in $group.SelectNodes("./*[local-name()='path']")) {
    $numbers = [regex]::Matches($pathNode.GetAttribute('d'), '-?\d+(?:\.\d+)?') |
        ForEach-Object { [double]::Parse($_.Value, [Globalization.CultureInfo]::InvariantCulture) }
    if ($numbers.Count -lt 6 -or $numbers.Count % 2 -ne 0) {
        throw "Unsupported path data in $SourceSvg"
    }

    $points = [System.Drawing.PointF[]]::new($numbers.Count / 2)
    for ($index = 0; $index -lt $numbers.Count; $index += 2) {
        $points[$index / 2] = [System.Drawing.PointF]::new(
            [float]($numbers[$index] + $translateX),
            [float]($numbers[$index + 1] + $translateY))
    }
    $polygons.Add($points)
}

if ($polygons.Count -eq 0) {
    throw 'No logo paths were parsed from the SVG.'
}

$masterSize = 1024
$master = [System.Drawing.Bitmap]::new(
    $masterSize,
    $masterSize,
    [System.Drawing.Imaging.PixelFormat]::Format32bppPArgb)
$graphics = [System.Drawing.Graphics]::FromImage($master)
try {
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality

    $factor = $masterSize / 256.0
    $backgroundRect = [System.Drawing.RectangleF]::new(14 * $factor, 14 * $factor, 228 * $factor, 228 * $factor)
    $backgroundPath = New-RoundedRectanglePath `
        -X $backgroundRect.X `
        -Y $backgroundRect.Y `
        -Width $backgroundRect.Width `
        -Height $backgroundRect.Height `
        -Radius (54 * $factor)
    try {
        $backgroundColor = if ($Scheme -eq 'Dark') { '#0B0C0E' } else { '#F3EFE6' }
        $backgroundBrush = [System.Drawing.SolidBrush]::new(
            [System.Drawing.ColorTranslator]::FromHtml($backgroundColor))
        try {
            $graphics.FillPath($backgroundBrush, $backgroundPath)
        }
        finally {
            $backgroundBrush.Dispose()
        }
    }
    finally {
        $backgroundPath.Dispose()
    }

    # SVG geometry after translate(-15, -10) occupies roughly 99 x 117 units.
    $logoScale = 1.45 * $factor
    $logoX = 56 * $factor
    $logoY = 43 * $factor
    $logoColor = if ($Scheme -eq 'Dark') { '#F3EFE6' } else { '#111318' }
    $logoBrush = [System.Drawing.SolidBrush]::new(
        [System.Drawing.ColorTranslator]::FromHtml($logoColor))
    try {
        foreach ($polygon in $polygons) {
            $renderPoints = [System.Drawing.PointF[]]::new($polygon.Length)
            for ($index = 0; $index -lt $polygon.Length; $index++) {
                $renderPoints[$index] = [System.Drawing.PointF]::new(
                    [float]($logoX + $polygon[$index].X * $logoScale),
                    [float]($logoY + $polygon[$index].Y * $logoScale))
            }
            $graphics.FillPolygon($logoBrush, $renderPoints)
        }
    }
    finally {
        $logoBrush.Dispose()
    }
}
finally {
    $graphics.Dispose()
}

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$iconFrames = [System.Collections.Generic.List[byte[]]]::new()
foreach ($size in $sizes) {
    $bitmap = [System.Drawing.Bitmap]::new(
        $size,
        $size,
        [System.Drawing.Imaging.PixelFormat]::Format32bppPArgb)
    $frameGraphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $frameGraphics.Clear([System.Drawing.Color]::Transparent)
        $frameGraphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $frameGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $frameGraphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $frameGraphics.DrawImage(
            $master,
            [System.Drawing.Rectangle]::new(0, 0, $size, $size),
            0,
            0,
            $master.Width,
            $master.Height,
            [System.Drawing.GraphicsUnit]::Pixel)

        $iconFrames.Add((ConvertTo-IcoDibFrame -Bitmap $bitmap))
    }
    finally {
        $frameGraphics.Dispose()
        $bitmap.Dispose()
    }
}

if ($PreviewPng) {
    $previewDirectory = Split-Path $PreviewPng -Parent
    if ($previewDirectory) {
        [System.IO.Directory]::CreateDirectory($previewDirectory) | Out-Null
    }
    $master.Save($PreviewPng, [System.Drawing.Imaging.ImageFormat]::Png)
}

$outputDirectory = Split-Path $OutputIco -Parent
if ($outputDirectory) {
    [System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
}

$file = [System.IO.File]::Open($OutputIco, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
$writer = [System.IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$sizes.Count)

    $offset = 6 + 16 * $sizes.Count
    for ($index = 0; $index -lt $sizes.Count; $index++) {
        $size = $sizes[$index]
        $dimension = if ($size -ge 256) { 0 } else { $size }
        $bytes = $iconFrames[$index]
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$bytes.Length)
        $writer.Write([uint32]$offset)
        $offset += $bytes.Length
    }

    foreach ($bytes in $iconFrames) {
        $writer.Write($bytes)
    }
}
finally {
    $writer.Dispose()
    $master.Dispose()
}

Write-Host "Generated $Scheme icon at $OutputIco with $($sizes.Count) sizes from $SourceSvg"
