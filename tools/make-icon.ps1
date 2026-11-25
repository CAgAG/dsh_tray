param(
    [string]$OutPath = "",
    [string]$PreviewPng = ""
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if ([string]::IsNullOrEmpty($OutPath)) {
    $OutPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'build\dsh.ico'
}
$outDir = Split-Path -Parent $OutPath
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$images = New-Object System.Collections.ArrayList

function Render-Size([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = [Math]::Max(0.5, $size * 0.025)
    $rect = New-Object System.Drawing.RectangleF([float]$pad, [float]$pad, [float]($size - $pad * 2), [float]($size - $pad * 2))
    $radius = [Math]::Max(2.0, $size * 0.22)
    $d = $radius * 2

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc([float]$rect.X, [float]$rect.Y, [float]$d, [float]$d, 180, 90)
    $path.AddArc([float]($rect.Right - $d), [float]$rect.Y, [float]$d, [float]$d, 270, 90)
    $path.AddArc([float]($rect.Right - $d), [float]($rect.Bottom - $d), [float]$d, [float]$d, 0, 90)
    $path.AddArc([float]$rect.X, [float]($rect.Bottom - $d), [float]$d, [float]$d, 90, 90)
    $path.CloseFigure()

    $c1 = [System.Drawing.Color]::FromArgb(255, 84, 112, 255)
    $c2 = [System.Drawing.Color]::FromArgb(255, 38, 68, 214)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $c1, $c2, [float]45)
    $g.FillPath($brush, $path)

    $stroke = [Math]::Max(1.5, $size * 0.105)
    $white = [System.Drawing.Color]::White
    $pen = New-Object System.Drawing.Pen($white, [float]$stroke)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $points = @(
        (New-Object System.Drawing.PointF([float]($size * 0.30), [float]($size * 0.31))),
        (New-Object System.Drawing.PointF([float]($size * 0.52), [float]($size * 0.50))),
        (New-Object System.Drawing.PointF([float]($size * 0.30), [float]($size * 0.69)))
    )
    $g.DrawLines($pen, $points)

    $underline = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(210, 255, 255, 255), [float]$stroke)
    $underline.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $underline.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawLine($underline, [float]($size * 0.58), [float]($size * 0.72), [float]($size * 0.75), [float]($size * 0.72))

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $ms.ToArray()

    if (-not [string]::IsNullOrEmpty($PreviewPng) -and $size -eq 256) {
        [System.IO.File]::WriteAllBytes($PreviewPng, $bytes)
    }

    $ms.Dispose()
    $underline.Dispose()
    $pen.Dispose()
    $brush.Dispose()
    $path.Dispose()
    $g.Dispose()
    $bmp.Dispose()
    return ,$bytes
}

foreach ($size in $sizes) {
    [void]$images.Add((Render-Size $size))
}

$stream = [System.IO.File]::Open($OutPath, [System.IO.FileMode]::Create)
$writer = New-Object System.IO.BinaryWriter($stream)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$sizes.Count)

    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $size = $sizes[$i]
        $dimension = if ($size -ge 256) { 0 } else { $size }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $data = [byte[]]$images[$i]
        $writer.Write([uint32]$data.Length)
        $writer.Write([uint32]$offset)
        $offset += $data.Length
    }
    foreach ($data in $images) {
        $writer.Write([byte[]]$data)
    }
    $writer.Flush()
}
finally {
    $writer.Dispose()
    $stream.Dispose()
}

Write-Host "icon written: $OutPath ($((Get-Item $OutPath).Length) bytes)"
