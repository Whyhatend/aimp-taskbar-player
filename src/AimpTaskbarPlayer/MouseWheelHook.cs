using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace AimpTaskbarPlayer;

/// <summary>
/// The panel is a WS_EX_NOACTIVATE window, so Windows may send wheel messages to the
/// focused app instead of us when "scroll inactive windows" is disabled. This hook is
/// installed only while the cursor is over the panel and turns wheel notches into
/// volume changes, swallowing the message so the app underneath does not scroll.
/// </summary>
internal sealed class MouseWheelHook : IDisposable
{
    private readonly IntPtr _window;
    private readonly Action<int> _onWheel;
    private readonly Native.HookProc _callback;
    private IntPtr _hook;
    private bool _disposed;

    public MouseWheelHook(IntPtr window, Action<int> onWheel)
    {
        _window = window;
        _onWheel = onWheel;
        _callback = OnMouse;
    }

    public void Install()
    {
        if (_disposed || _hook != IntPtr.Zero) return;
        _hook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _callback, Native.GetModuleHandle(null), 0);
    }

    public void Uninstall()
    {
        if (_hook == IntPtr.Zero) return;
        Native.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private IntPtr OnMouse(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && (uint)wParam == Native.WM_MOUSEWHEEL)
        {
            var info = Marshal.PtrToStructure<Native.MSLLHOOKSTRUCT>(lParam);
            if (IsOverWindow(info.pt))
            {
                var delta = (short)((info.mouseData >> 16) & 0xFFFF);
                if (delta != 0)
                {
                    var dispatcher = Application.Current?.Dispatcher;
                    if (dispatcher != null)
                    {
                        try
                        {
                            dispatcher.BeginInvoke(new Action(() => _onWheel(delta)), DispatcherPriority.Input);
                        }
                        catch (InvalidOperationException)
                        {
                            // dispatcher is shutting down
                        }
                    }
                    else
                    {
                        _onWheel(delta);
                    }

                    return new IntPtr(1);
                }
            }
        }

        return Native.CallNextHookEx(_hook, code, wParam, lParam);
    }

    private bool IsOverWindow(Native.POINT point)
    {
        if (_window == IntPtr.Zero || !Native.IsWindow(_window)) return false;
        if (!Native.GetWindowRect(_window, out var rect)) return false;

        return point.X >= rect.Left && point.X < rect.Right &&
               point.Y >= rect.Top && point.Y < rect.Bottom;
    }

    public void Dispose()
    {
        _disposed = true;
        Uninstall();
    }
}
