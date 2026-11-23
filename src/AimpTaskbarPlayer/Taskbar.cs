using System;

namespace AimpTaskbarPlayer;

/// <summary>Taskbar geometry helpers (primary taskbar, shell notification state).</summary>
internal static class Taskbar
{
    public static IntPtr Handle => Native.FindWindow("Shell_TrayWnd", null);

    /// <summary>
    /// Bounding box of the notification area. Windows keeps the tray pane as a child HWND
    /// of Shell_TrayWnd, so a plain FindWindowEx is enough - no UI Automation client needed.
    /// </summary>
    public static bool TryGetTrayRect(out Native.RECT rect)
    {
        rect = default;
        var taskbar = Handle;
        if (taskbar == IntPtr.Zero) return false;

        var tray = Native.FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
        if (tray == IntPtr.Zero) return false;

        if (!Native.GetWindowRect(tray, out rect)) return false;
        return rect.Width >= 40 && rect.Height >= 10;
    }

    public static Native.RECT GetTaskbarRect()
    {
        var data = new Native.APPBARDATA { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.APPBARDATA>() };
        Native.SHAppBarMessage(Native.ABM_GETTASKBARPOS, ref data);
        return data.rc;
    }

    public static Native.RECT GetPrimaryWorkArea()
    {
        var monitor = Native.MonitorFromWindow(IntPtr.Zero, 2 /*MONITOR_DEFAULTTOPRIMARY*/);
        var info = new Native.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
        Native.GetMonitorInfo(monitor, ref info);
        return info.rcWork;
    }

    /// <summary>True when a fullscreen app or presentation mode should hide the panel.</summary>
    public static bool IsFullscreenAppActive()
    {
        // QUNS_* values: 2 = busy, 3 = running D3D fullscreen, 4 = presentation mode
        return Native.SHQueryUserNotificationState(out var state) == 0 && state is 2 or 3 or 4;
    }
}
