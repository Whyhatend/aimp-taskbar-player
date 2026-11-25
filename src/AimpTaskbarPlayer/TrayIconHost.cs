using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace AimpTaskbarPlayer;

/// <summary>
/// Small notification-area companion: lets the panel be restored or the app be closed.
/// Implemented with Shell_NotifyIcon directly so the app does not need to load
/// System.Windows.Forms / System.Drawing just for a tray icon.
/// </summary>
internal sealed class TrayIconHost : IDisposable
{
    private const uint IconId = 1;
    private const int CmdShow = 1;
    private const int CmdOpen = 2;
    private const int CmdAutoStart = 3;
    private const int CmdExit = 4;
    private const int CmdSettings = 5;

    private readonly HwndSource _source;
    private readonly uint _taskbarCreatedMessage;
    private readonly uint _callbackMessage = Native.WM_APP + 1;
    private readonly IntPtr _icon;
    private readonly bool _ownsIcon;

    private bool _autoStart;
    private bool _added;
    private bool _disposed;

    public event Action? ShowRequested;
    public event Action? OpenAimpRequested;
    public event Action? SettingsRequested;
    public event Action? ExitRequested;
    public event Action? AutoStartToggled;

    public TrayIconHost(bool autoStartChecked)
    {
        _autoStart = autoStartChecked;

        var parameters = new HwndSourceParameters("AimpTaskbarPlayerTray")
        {
            Width = 0,
            Height = 0,
            WindowStyle = Native.WS_POPUP,
            ExtendedWindowStyle = (int)(Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE),
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);

        _taskbarCreatedMessage = Native.RegisterWindowMessage("TaskbarCreated");
        _icon = CreateTrayIcon(out _ownsIcon);
        AddIcon();
    }

    public bool AutoStartChecked => _autoStart;

    public void SyncAutoStart(bool enabled) => _autoStart = enabled;

    private static IntPtr CreateTrayIcon(out bool ownsIcon)
    {
        ownsIcon = false;
        try
        {
            var executable = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(executable) &&
                Native.ExtractIconEx(executable, 0, out var large, out var small, 1) > 0)
            {
                if (small != IntPtr.Zero)
                {
                    if (large != IntPtr.Zero) Native.DestroyIcon(large);
                    ownsIcon = true;
                    return small;
                }

                if (large != IntPtr.Zero)
                {
                    ownsIcon = true;
                    return large;
                }
            }
        }
        catch
        {
            // fall back to the stock application icon
        }

        return Native.LoadIcon(IntPtr.Zero, new IntPtr(Native.IDI_APPLICATION));
    }

    private void AddIcon()
    {
        var data = new Native.NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<Native.NOTIFYICONDATA>(),
            hWnd = _source.Handle,
            uID = IconId,
            uFlags = Native.NIF_MESSAGE | Native.NIF_ICON | Native.NIF_TIP,
            uCallbackMessage = _callbackMessage,
            hIcon = _icon,
            szTip = "AIMP · плеер в панели задач",
            szInfo = string.Empty,
            szInfoTitle = string.Empty,
        };

        _added = Native.Shell_NotifyIcon(Native.NIM_ADD, ref data);
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_taskbarCreatedMessage != 0 && (uint)message == _taskbarCreatedMessage)
        {
            // Explorer restarted and forgot every tray icon.
            _added = false;
            AddIcon();
            return IntPtr.Zero;
        }

        if ((uint)message == _callbackMessage)
        {
            var notification = (int)(lParam.ToInt64() & 0xFFFF);
            if (notification == Native.WM_LBUTTONDBLCLK)
            {
                ShowRequested?.Invoke();
                handled = true;
            }
            else if (notification == Native.WM_RBUTTONUP || notification == Native.WM_CONTEXTMENU)
            {
                ShowMenu();
                handled = true;
            }
        }

        return IntPtr.Zero;
    }

    private void ShowMenu()
    {
        var menu = Native.CreatePopupMenu();
        if (menu == IntPtr.Zero) return;

        try
        {
            Native.AppendMenu(menu, Native.MF_STRING, (UIntPtr)CmdShow, "Показать панель");
            Native.AppendMenu(menu, Native.MF_STRING, (UIntPtr)CmdOpen, "Открыть AIMP");
            Native.AppendMenu(menu, Native.MF_SEPARATOR, UIntPtr.Zero, null);
            Native.AppendMenu(menu, Native.MF_STRING | (_autoStart ? Native.MF_CHECKED : 0),
                (UIntPtr)CmdAutoStart, "Запускать при входе в Windows");
            Native.AppendMenu(menu, Native.MF_STRING, (UIntPtr)CmdSettings, "Настройки…");
            Native.AppendMenu(menu, Native.MF_SEPARATOR, UIntPtr.Zero, null);
            Native.AppendMenu(menu, Native.MF_STRING, (UIntPtr)CmdExit, "Выход");

            if (!Native.GetCursorPos(out var cursor)) return;

            // SetForegroundWindow lets the menu close when the user clicks elsewhere;
            // the PostMessage(WM_NULL) is the classic workaround for a stuck popup menu.
            Native.SetForegroundWindow(_source.Handle);
            var command = Native.TrackPopupMenu(menu,
                Native.TPM_RIGHTBUTTON | Native.TPM_RETURNCMD | Native.TPM_NONOTIFY,
                cursor.X, cursor.Y, 0, _source.Handle, IntPtr.Zero);
            Native.PostMessage(_source.Handle, Native.WM_NULL, IntPtr.Zero, IntPtr.Zero);

            switch (command)
            {
                case CmdShow:
                    ShowRequested?.Invoke();
                    break;
                case CmdOpen:
                    OpenAimpRequested?.Invoke();
                    break;
                case CmdAutoStart:
                    _autoStart = !_autoStart;
                    AutoStartToggled?.Invoke();
                    break;
                case CmdSettings:
                    SettingsRequested?.Invoke();
                    break;
                case CmdExit:
                    ExitRequested?.Invoke();
                    break;
            }
        }
        finally
        {
            Native.DestroyMenu(menu);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_added)
        {
            var data = new Native.NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<Native.NOTIFYICONDATA>(),
                hWnd = _source.Handle,
                uID = IconId,
            };
            Native.Shell_NotifyIcon(Native.NIM_DELETE, ref data);
            _added = false;
        }

        _source.RemoveHook(WndProc);
        _source.Dispose();

        if (_ownsIcon && _icon != IntPtr.Zero) Native.DestroyIcon(_icon);
    }
}
