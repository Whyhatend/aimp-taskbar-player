param(
    [string]$Action = "state"
)

Add-Type @"
using System;using System.Runtime.InteropServices;
public class AimpCtl {
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c,string n);
 [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h,uint m,IntPtr w,IntPtr l);
 public static int Get(int prop){ var h=FindWindow("AIMP2_RemoteInfo",null); if(h==IntPtr.Zero) return -1; return (int)SendMessage(h,0x400+0x77,(IntPtr)prop,IntPtr.Zero); }
 public static void Cmd(int cmd){ var h=FindWindow("AIMP2_RemoteInfo",null); if(h!=IntPtr.Zero) SendMessage(h,0x400+0x75,(IntPtr)cmd,IntPtr.Zero); }
}
"@

function State { $s=[AimpCtl]::Get(0x40); switch($s){0{"stopped"}1{"paused"}2{"playing"}default{"unknown($s)"}} }

switch ($Action) {
    "state" {
        "state=$(State) pos=$([AimpCtl]::Get(0x20))ms dur=$([AimpCtl]::Get(0x30))ms vol=$([AimpCtl]::Get(0x50))"
    }
    "pause" { [AimpCtl]::Cmd(14); Start-Sleep -Milliseconds 300; "state=$(State)" }
    "playpause" { [AimpCtl]::Cmd(14); Start-Sleep -Milliseconds 300; "state=$(State)" }
    default { "unknown action" }
}
