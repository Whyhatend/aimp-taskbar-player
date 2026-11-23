param(
    [string]$Root = (Resolve-Path "$PSScriptRoot\..").Path,
    [int]$Scale = 3,
    [int]$Pad = 12
)

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

Add-Type @"
using System;using System.Runtime.InteropServices;using System.Text;
public class PanelFinder {
 [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h,StringBuilder s,int n);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 public delegate bool EnumProc(IntPtr h, IntPtr l);
 [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
 public static int[] FindPanel(uint pid){
   int[] found = null;
   EnumWindows((h,l)=>{
     uint p; GetWindowThreadProcessId(h,out p);
     if(p==pid && IsWindowVisible(h)){
       var c=new StringBuilder(200); GetClassName(h,c,200);
       RECT r; GetWindowRect(h,out r);
       if(c.ToString().StartsWith("HwndWrapper") && (r.R-r.L) > 100 && (r.B-r.T) > 20 && (r.B-r.T) < 200){ found = new int[]{r.L,r.T,r.R,r.B}; return false; }
     }
     return true;
   }, IntPtr.Zero);
   return found;
 }
}
"@

$artifacts = Join-Path $Root "artifacts"
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null

$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds

$fullBmp = New-Object System.Drawing.Bitmap -ArgumentList $screen.Width, $screen.Height
$gg = [System.Drawing.Graphics]::FromImage($fullBmp)
$gg.CopyFromScreen($screen.X, $screen.Y, 0, 0, $fullBmp.Size)
$fullPath = Join-Path $artifacts "full.png"
$fullBmp.Save($fullPath, [System.Drawing.Imaging.ImageFormat]::Png)
$gg.Dispose()

$proc = Get-Process AimpTaskbarPlayer -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proc) { Write-Host "panel not running"; $fullBmp.Dispose(); exit 0 }

$r = [PanelFinder]::FindPanel([uint32]$proc.Id)
if ($r -eq $null) { Write-Host "panel window not found"; $fullBmp.Dispose(); exit 0 }

$left = [Math]::Max(0, $r[0] - $Pad)
$top = [Math]::Max(0, $r[1] - $Pad)
$width = ($r[2] - $r[0]) + 2 * $Pad
$height = ($r[3] - $r[1]) + 2 * $Pad
if ($top + $height -gt $fullBmp.Height) { $height = $fullBmp.Height - $top }

$srcRect = New-Object System.Drawing.Rectangle -ArgumentList $left, $top, $width, $height
$dstW = [int]($width * $Scale)
$dstH = [int]($height * $Scale)
$dstRect = New-Object System.Drawing.Rectangle -ArgumentList 0, 0, $dstW, $dstH

$panelBmp = New-Object System.Drawing.Bitmap -ArgumentList $dstW, $dstH
$pg = [System.Drawing.Graphics]::FromImage($panelBmp)
$pg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$pg.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
$pg.DrawImage($fullBmp, $dstRect, $srcRect, [System.Drawing.GraphicsUnit]::Pixel)
$panelPath = Join-Path $artifacts "panel.png"
$panelBmp.Save($panelPath, [System.Drawing.Imaging.ImageFormat]::Png)

$pg.Dispose(); $panelBmp.Dispose(); $fullBmp.Dispose()
Write-Host "panel: $panelPath ($($r[2]-$r[0])x$($r[3]-$r[1]) at $($r[0]),$($r[1]))"
