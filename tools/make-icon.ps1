param(
    [string]$Output = "$PSScriptRoot\..\src\AimpTaskbarPlayer\Assets\app.ico"
)

Add-Type -AssemblyName System.Drawing

$size = 256
$bmp = New-Object System.Drawing.Bitmap($size, $size)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::Transparent)

$path = New-Object System.Drawing.Drawing2D.GraphicsPath
$path.AddArc(8, 8, 112, 112, 180, 90)
$path.AddArc(136, 8, 112, 112, 270, 90)
$path.AddArc(136, 136, 112, 112, 0, 90)
$path.AddArc(8, 136, 112, 112, 90, 90)
$path.CloseFigure()

$rect = New-Object System.Drawing.Rectangle(0, 0, $size, $size)
$brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    $rect,
    [System.Drawing.Color]::FromArgb(255, 255, 163, 72),
    [System.Drawing.Color]::FromArgb(255, 233, 66, 148),
    45.0)
$g.FillPath($brush, $path)

# Play triangle
$triangle = @(
    (New-Object System.Drawing.PointF(104, 82)),
    (New-Object System.Drawing.PointF(176, 128)),
    (New-Object System.Drawing.PointF(104, 174))
)
$g.FillPolygon([System.Drawing.Brushes]::White, $triangle)

# Small sound bars
$barBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(210, 255, 255, 255))
$g.FillRectangle($barBrush, 60, 108, 12, 40)
$g.FillRectangle($barBrush, 80, 96, 12, 64)
$g.FillRectangle($barBrush, 184, 108, 12, 40)
$g.FillRectangle($barBrush, 204, 96, 12, 64)

$g.Dispose()

$pngStream = New-Object System.IO.MemoryStream
$bmp.Save($pngStream, [System.Drawing.Imaging.ImageFormat]::Png)
$pngBytes = $pngStream.ToArray()
$bmp.Dispose()
$pngStream.Dispose()

$dir = Split-Path -Parent $Output
New-Item -ItemType Directory -Force -Path $dir | Out-Null

$fs = [System.IO.File]::Create($Output)
$writer = New-Object System.IO.BinaryWriter($fs)
$writer.Write([UInt16]0)          # reserved
$writer.Write([UInt16]1)          # type: icon
$writer.Write([UInt16]1)          # image count
$writer.Write([Byte]0)            # width 256
$writer.Write([Byte]0)            # height 256
$writer.Write([Byte]0)            # palette
$writer.Write([Byte]0)            # reserved
$writer.Write([UInt16]1)          # planes
$writer.Write([UInt16]32)         # bit count
$writer.Write([UInt32]$pngBytes.Length)
$writer.Write([UInt32]22)         # offset
$writer.Write($pngBytes)
$writer.Flush()
$writer.Close()

Write-Host "Icon written to $Output ($($pngBytes.Length) bytes PNG payload)"
