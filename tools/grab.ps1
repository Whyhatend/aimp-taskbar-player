param(
    [Parameter(Mandatory = $true)][string]$Path,
    [int]$X = 0,
    [int]$Y = 0,
    [int]$W = 0,
    [int]$H = 0,
    [double]$Scale = 1.0
)

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
if ($W -le 0) { $W = $screen.Width }
if ($H -le 0) { $H = $screen.Height }
if ($X -lt 0) { $X = 0 }
if ($Y -lt 0) { $Y = 0 }
if ($X + $W -gt $screen.Width) { $W = $screen.Width - $X }
if ($Y + $H -gt $screen.Height) { $H = $screen.Height - $Y }

$bmp = New-Object System.Drawing.Bitmap -ArgumentList $screen.Width, $screen.Height
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($screen.X, $screen.Y, 0, 0, $bmp.Size)

$rect = New-Object System.Drawing.Rectangle -ArgumentList $X, $Y, $W, $H
$crop = $bmp.Clone($rect, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)

if ($Scale -ne 1.0) {
    $ow = [int]($W * $Scale)
    $oh = [int]($H * $Scale)
    $out = New-Object System.Drawing.Bitmap -ArgumentList $ow, $oh
    $og = [System.Drawing.Graphics]::FromImage($out)
    $og.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $og.DrawImage($crop, 0, 0, $ow, $oh)
    $out.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $og.Dispose(); $out.Dispose()
}
else {
    $crop.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
}

$g.Dispose(); $bmp.Dispose(); $crop.Dispose()
Write-Host "saved $Path ($X,$Y ${W}x${H} x$Scale)"
