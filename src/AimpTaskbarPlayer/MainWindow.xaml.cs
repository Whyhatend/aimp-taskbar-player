using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace AimpTaskbarPlayer;

public partial class MainWindow : Window
{
    private const int WheelDelta = 120;
    private const int VolumeStep = 2;

    private readonly AimpRemote _aimp = new();
    private readonly ArtworkProvider _artwork = new();
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly DispatcherTimer _pollTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _stateTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly DispatcherTimer _volumeTimer = new() { Interval = TimeSpan.FromMilliseconds(1000) };
    private readonly DispatcherTimer _memoryTimer = new() { Interval = TimeSpan.FromSeconds(15) };

    private TrayIconHost? _tray;
    private MouseWheelHook? _wheelHook;
    private TrackInfo? _track;
    private string? _artPath;
    private bool _connected;
    private bool _scrubbing;
    private int _durationMs;
    private int _seekTargetMs;
    private bool _hiddenWithTaskbar;

    private int _currentX;
    private bool _dragging;
    private Native.POINT _dragCursorOrigin;
    private int _dragStartX;

    private int _wheelAccum;
    private int _lastPositionSecond = -1;
    private int _lastDuration = -1;
    private PlaybackState _lastState = PlaybackState.Stopped;
    private bool _hasTransportState;
    private DateTime _lastInteraction = DateTime.UtcNow;
    private bool _trimming;

    public MainWindow()
    {
        InitializeComponent();

        if (_settings.PanelWidth >= 320) Width = _settings.PanelWidth;

        Loaded += OnLoaded;
        Closing += OnClosing;
        _pollTimer.Tick += OnPollTick;
        _stateTimer.Tick += OnStateTick;
        _volumeTimer.Tick += OnVolumeTimerTick;
        _memoryTimer.Tick += OnMemoryTimerTick;

        Surface.MouseEnter += (_, _) =>
        {
            _lastInteraction = DateTime.UtcNow;
            AnimateHover(1);
            _wheelHook?.Install();
        };
        Surface.MouseLeave += (_, _) =>
        {
            _lastInteraction = DateTime.UtcNow;
            AnimateHover(0);
            _wheelHook?.Uninstall();
        };
        Surface.PreviewMouseDown += (_, _) => _lastInteraction = DateTime.UtcNow;
        Surface.PreviewMouseMove += (_, _) => _lastInteraction = DateTime.UtcNow;

        // Never keep the mouse captured if the OS takes it away (hide, task switch, ...).
        Surface.LostMouseCapture += (_, _) =>
        {
            if (!_dragging) return;
            _dragging = false;
            SavePosition();
        };
        ProgressHost.LostMouseCapture += (_, _) => _scrubbing = false;
    }

    private IntPtr Handle => new WindowInteropHelper(this).Handle;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Behave like a taskbar widget: never steal keyboard focus from the app the
        // user is working in, and stay out of Alt+Tab.
        var handle = Handle;
        if (handle == IntPtr.Zero) return;

        const int GWL_EXSTYLE = -20;
        const long WS_EX_TOOLWINDOW = 0x00000080;
        const long WS_EX_NOACTIVATE = 0x08000000;

        var style = Native.GetWindowLongPtr(handle, GWL_EXSTYLE).ToInt64();
        style |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
        Native.SetWindowLongPtr(handle, GWL_EXSTYLE, new IntPtr(style));
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var autoStart = StartupRegistration.IsEnabled;
        AutoStartCheck.Visibility = autoStart ? Visibility.Visible : Visibility.Collapsed;

        _tray = new TrayIconHost(autoStart);
        _tray.ShowRequested += ShowPanel;
        _tray.OpenAimpRequested += OpenAimp;
        _tray.ExitRequested += Close;
        _tray.AutoStartToggled += () => ApplyAutoStart(_tray.AutoStartChecked);

        Theme.Changed += OnThemeChanged;

        _wheelHook = new MouseWheelHook(Handle, AdjustVolume);

        ApplySurface();
        PlacePanel();

        _pollTimer.Start();
        _stateTimer.Start();
        _memoryTimer.Start();
        _ = TrimLaterAsync();
        Tick();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        Mouse.Capture(null);
        Theme.Changed -= OnThemeChanged;
        _pollTimer.Stop();
        _stateTimer.Stop();
        _volumeTimer.Stop();
        _memoryTimer.Stop();
        _wheelHook?.Dispose();
        _wheelHook = null;
        _tray?.Dispose();
        _aimp.Dispose();
    }

    private void OnThemeChanged() => ApplySurface();

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Dispatcher.BeginInvoke(PlacePanel, DispatcherPriority.Loaded);
    }

    // ---------------------------------------------------------------- layout

    private double ScaleFactor()
    {
        var handle = Handle;
        if (handle == IntPtr.Zero) return 1.0;
        var dpi = Native.GetDpiForWindow(handle);
        return dpi <= 0 ? 1.0 : dpi / 96.0;
    }

    /// <summary>Left edge (screen px) of the notification area, or a sensible fallback.</summary>
    private static int GetNotificationAreaLeft(Native.RECT taskbar, double scale)
    {
        if (Taskbar.TryGetTrayRect(out var tray) && tray.Left > taskbar.Left + 80) return tray.Left;
        return taskbar.Right - (int)Math.Round(190 * scale);
    }

    /// <summary>
    /// The panel is sized to the full height of the taskbar strip and sits flush inside it,
    /// so it reads as part of the taskbar instead of a floating card.
    /// </summary>
    private void PlacePanel()
    {
        var handle = Handle;
        if (handle == IntPtr.Zero) return;

        var taskbar = Taskbar.GetTaskbarRect();
        if (taskbar.Height <= 0) return;

        var scale = ScaleFactor();
        var heightDip = taskbar.Height / scale;
        var widthDip = Math.Max(320.0, _settings.PanelWidth);

        if (Math.Abs(Height - heightDip) > 0.5) Height = heightDip;
        if (Math.Abs(Width - widthDip) > 0.5) Width = widthDip;
        UpdateLayout();

        if (!Native.GetWindowRect(handle, out var rect)) return;

        int x;
        if (_settings.Docked)
        {
            var gap = (int)Math.Round(8 * scale);
            x = GetNotificationAreaLeft(taskbar, scale) - gap - rect.Width;
        }
        else
        {
            x = _settings.X;
        }

        _currentX = Math.Clamp(x, taskbar.Left, Math.Max(taskbar.Left, taskbar.Right - rect.Width));
        MoveTo(_currentX, taskbar.Top);
    }

    private void MoveTo(int x, int y)
    {
        var handle = Handle;
        if (handle == IntPtr.Zero) return;
        Native.SetWindowPos(handle, Native.HWND_TOPMOST, x, y, 0, 0,
            Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
    }

    private void ApplySurface()
    {
        // No card: the content is drawn straight on the taskbar surface.
        Surface.Background = Brushes.Transparent;
        Surface.BorderThickness = new Thickness(0);
    }

    private void SavePosition()
    {
        _settings.X = _currentX;
        _settings.Y = 0;
        _settings.Docked = false;
        _settings.Save();
    }

    private void ShowPanel()
    {
        _hiddenWithTaskbar = false;
        Show();
        PlacePanel();
    }

    // ---------------------------------------------------------------- polling

    private void OnPollTick(object? sender, EventArgs e) => Tick();

    private void Tick()
    {
        if (!_connected)
        {
            _connected = _aimp.TryConnect();
            if (!_connected)
            {
                ShowDisconnected();
                return;
            }
        }

        if (!Native.IsWindow(_aimp.WindowHandle))
        {
            _aimp.Disconnect();
            _connected = false;
            ShowDisconnected();
            return;
        }

        var track = _aimp.ReadTrack();
        var state = _aimp.GetState();
        var duration = _aimp.GetDurationMs();
        var position = _aimp.GetPositionMs();

        if (track == null)
        {
            if (_track != null)
            {
                _track = null;
                _artPath = null;
                CoverHost.Fill = null;
                CoverHost.Opacity = 0;
                TitleText.Text = "Нет активного трека";
                ArtistText.Text = "AIMP готов к работе";
                ToolTip = null;
            }
        }
        else if (_track == null || !string.Equals(track.FileName, _track.FileName, StringComparison.OrdinalIgnoreCase))
        {
            ApplyTrack(track);
        }

        ApplyTransport(state, duration);
        if (!_scrubbing) ApplyProgress(position, duration);
    }

    private void ApplyTrack(TrackInfo track)
    {
        _track = track;
        TitleText.Text = track.DisplayTitle;
        ArtistText.Text = track.DisplayArtist;
        ToolTip = BuildTooltip(track);
        _ = LoadArtworkAsync(track.FileName);
    }

    private async Task LoadArtworkAsync(string path)
    {
        if (!string.Equals(_artPath, path, StringComparison.OrdinalIgnoreCase))
        {
            _artPath = path;
            CoverHost.Fill = null;
            CoverHost.Opacity = 0;
        }

        var image = await _artwork.GetAsync(path).ConfigureAwait(true);
        if (!string.Equals(_artPath, path, StringComparison.OrdinalIgnoreCase) || image == null) return;

        CoverHost.Fill = new ImageBrush(image)
        {
            Stretch = Stretch.UniformToFill,
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center,
        };
        CoverHost.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
    }

    private void ShowDisconnected()
    {
        if (_track != null || _artPath != null)
        {
            _track = null;
            _artPath = null;
            CoverHost.Fill = null;
            CoverHost.Opacity = 0;
        }

        TitleText.Text = "AIMP не запущен";
        ArtistText.Text = "Нажмите, чтобы запустить";
        ToolTip = null;
        ProgressFill.Width = 0;
        ProgressThumb.Margin = new Thickness(-4, 0, 0, 1);
        TimeCurrent.Text = "0:00";
        TimeTotal.Text = " / 0:00";
        PlayIcon.Visibility = Visibility.Visible;
        PauseIcon.Visibility = Visibility.Collapsed;
        PrevButton.IsEnabled = false;
        NextButton.IsEnabled = false;

        _lastPositionSecond = -1;
        _lastDuration = -1;
        _hasTransportState = false;
    }

    private void ApplyTransport(PlaybackState state, int duration)
    {
        PrevButton.IsEnabled = true;
        NextButton.IsEnabled = true;

        if (_hasTransportState && state == _lastState) return;
        _lastState = state;
        _hasTransportState = true;

        var playing = state == PlaybackState.Playing;
        PlayIcon.Visibility = playing ? Visibility.Collapsed : Visibility.Visible;
        PauseIcon.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyProgress(int position, int duration)
    {
        _durationMs = duration;

        if (duration != _lastDuration)
        {
            _lastDuration = duration;
            TimeTotal.Text = " / " + FormatTime(duration);
        }

        var second = position / 1000;
        if (second != _lastPositionSecond)
        {
            _lastPositionSecond = second;
            TimeCurrent.Text = FormatTime(position);
        }

        UpdateProgressVisuals(position, duration);
    }

    private void UpdateProgressVisuals(int position, int duration)
    {
        var width = ProgressHost.ActualWidth;
        if (width <= 0) return;

        var fraction = duration > 0 ? Math.Clamp((double)position / duration, 0, 1) : 0;
        var filled = Math.Round(width * fraction);
        ProgressFill.Width = filled;
        ProgressThumb.Margin = new Thickness(filled - 3.5, 0, 0, 1);
    }

    private void OnStateTick(object? sender, EventArgs e)
    {
        var handle = Handle;
        if (handle == IntPtr.Zero || !Native.IsWindow(handle))
        {
            Recreate();
            return;
        }

        // Hide together with the taskbar (auto-hide) and in fullscreen apps.
        var taskbarHidden = IsTaskbarSlidAway();
        var fullscreen = Taskbar.IsFullscreenAppActive();

        if ((taskbarHidden || fullscreen) && !_hiddenWithTaskbar)
        {
            _hiddenWithTaskbar = true;
            Hide();
            _ = TrimNowAsync();
            return;
        }
        if (!taskbarHidden && !fullscreen && _hiddenWithTaskbar)
        {
            _hiddenWithTaskbar = false;
            Show();
            PlacePanel();
        }

        if (!IsVisible) return;

        // Keep the very topmost position; the shell sometimes raises the taskbar.
        if (IsCoveredByTaskbar())
        {
            MoveTo(_currentX, Taskbar.GetTaskbarRect().Top);
        }
    }

    private static bool IsTaskbarSlidAway()
    {
        var taskbar = Taskbar.Handle;
        if (taskbar == IntPtr.Zero) return false;
        if (!Native.GetWindowRect(taskbar, out var rect)) return false;

        var monitor = Native.MonitorFromWindow(taskbar, 2 /* MONITOR_DEFAULTTOPRIMARY */);
        var info = new Native.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
        if (!Native.GetMonitorInfo(monitor, ref info)) return false;

        return rect.Top >= info.rcMonitor.Bottom - 4;
    }

    private bool IsCoveredByTaskbar()
    {
        if (!Native.GetWindowRect(Handle, out var rect)) return true;

        var center = new Native.POINT { X = rect.Left + rect.Width / 2, Y = rect.Top + rect.Height / 2 };
        var under = Native.WindowFromPoint(center);
        if (under == IntPtr.Zero) return false;

        var root = Native.GetAncestor(under, 2 /* GA_ROOT */);
        return root == Taskbar.Handle;
    }

    private void Recreate()
    {
        _pollTimer.Stop();
        _stateTimer.Stop();
        (Application.Current as App)?.RecreatePanel();
    }

    private void AnimateHover(double to)
    {
        HoverOverlay.BeginAnimation(OpacityProperty,
            new DoubleAnimation(to, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
    }

    // ---------------------------------------------------------------- volume

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_connected) return;

        _lastInteraction = DateTime.UtcNow;
        AdjustVolume(e.Delta);
        e.Handled = true;
    }

    private void AdjustVolume(int delta)
    {
        if (!_connected) return;

        _wheelAccum += delta;
        var steps = _wheelAccum / WheelDelta;
        if (steps == 0) return;
        _wheelAccum -= steps * WheelDelta;

        var current = _aimp.GetVolume();
        if (current < 0) return;

        var target = Math.Clamp(current + steps * VolumeStep, 0, 100);
        if (target != current) _aimp.SetVolume(target);

        ShowVolume(target);
    }

    private void ShowVolume(int volume)
    {
        VolumeText.Text = $"{volume}%";
        TimeCurrent.Visibility = Visibility.Collapsed;
        TimeTotal.Visibility = Visibility.Collapsed;
        VolumeBadge.BeginAnimation(OpacityProperty,
            new DoubleAnimation(1, TimeSpan.FromMilliseconds(80)));
        _volumeTimer.Stop();
        _volumeTimer.Start();
    }

    private void OnVolumeTimerTick(object? sender, EventArgs e)
    {
        _volumeTimer.Stop();
        VolumeBadge.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, TimeSpan.FromMilliseconds(260))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
        TimeCurrent.Visibility = Visibility.Visible;
        TimeTotal.Visibility = Visibility.Visible;
    }

    // ---------------------------------------------------------------- memory

    private void OnMemoryTimerTick(object? sender, EventArgs e)
    {
        if (_trimming || _dragging || _scrubbing || MenuPopup.IsOpen || Surface.IsMouseOver) return;
        if ((DateTime.UtcNow - _lastInteraction).TotalSeconds < 20) return;

        _ = TrimNowAsync();
    }

    private async Task TrimLaterAsync()
    {
        try
        {
            await Task.Delay(5000);
            await TrimNowAsync();
        }
        catch
        {
            // the window may already be closing
        }
    }

    private async Task TrimNowAsync()
    {
        if (_trimming) return;
        _trimming = true;

        try
        {
            await MemoryTrimmer.TrimAsync();
        }
        catch
        {
            // trimming is best effort
        }
        finally
        {
            _trimming = false;
        }
    }

    private static string FormatTime(int milliseconds)
    {
        if (milliseconds <= 0) return "0:00";
        var span = TimeSpan.FromMilliseconds(milliseconds);
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{span.Minutes}:{span.Seconds:00}";
    }

    private static string BuildTooltip(TrackInfo track)
    {
        var lines = new List<string> { $"{track.DisplayArtist} — {track.DisplayTitle}" };

        if (!string.IsNullOrWhiteSpace(track.Album))
            lines.Add(string.IsNullOrWhiteSpace(track.Year) ? track.Album : $"{track.Album} ({track.Year})");
        if (!string.IsNullOrWhiteSpace(track.Genre)) lines.Add(track.Genre);

        var format = track.FormatDescription;
        if (!string.IsNullOrEmpty(format)) lines.Add(format);
        if (!string.IsNullOrWhiteSpace(track.FileName)) lines.Add(track.FileName);

        return string.Join(Environment.NewLine, lines);
    }

    // ---------------------------------------------------------------- input

    private void OnRootMouseDown(object sender, MouseButtonEventArgs e) => DragOrOpen(e);

    private void OnCoverMouseDown(object sender, MouseButtonEventArgs e) => DragOrOpen(e);

    private void DragOrOpen(MouseButtonEventArgs e)
    {
        _lastInteraction = DateTime.UtcNow;
        e.Handled = true;
        MenuPopup.IsOpen = false;

        if (e.ClickCount == 2)
        {
            // Double click on the widget body = play/pause, like any player.
            // (Opening AIMP lives in the context menu so a stray double click
            // never pops a big window over the user's work.)
            TogglePlayback();
            return;
        }

        if (e.ButtonState != MouseButtonState.Pressed) return;
        if (!Native.GetCursorPos(out _dragCursorOrigin)) return;

        _dragStartX = _currentX;
        _dragging = true;
        Surface.CaptureMouse();
    }

    private void OnRootMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || e.LeftButton != MouseButtonState.Pressed) return;
        if (!Native.GetCursorPos(out var current)) return;

        var taskbar = Taskbar.GetTaskbarRect();
        if (taskbar.Height <= 0) return;
        if (!Native.GetWindowRect(Handle, out var rect)) return;

        var x = _dragStartX + (current.X - _dragCursorOrigin.X);
        _currentX = Math.Clamp(x, taskbar.Left, Math.Max(taskbar.Left, taskbar.Right - rect.Width));
        MoveTo(_currentX, taskbar.Top);
    }

    private void OnRootMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;

        _dragging = false;
        Surface.ReleaseMouseCapture();

        // A plain click must not un-dock the panel; only an actual move does.
        if (_currentX != _dragStartX) SavePosition();
    }

    private void OnPrevClick(object sender, RoutedEventArgs e)
    {
        if (_connected) _aimp.Previous();
    }

    private void OnNextClick(object sender, RoutedEventArgs e)
    {
        if (_connected) _aimp.Next();
    }

    private void OnPlayClick(object sender, RoutedEventArgs e) => TogglePlayback();

    private void TogglePlayback()
    {
        if (!_connected)
        {
            OpenAimp();
            return;
        }

        _aimp.PlayPause();

        // Optimistic icon flip so the click feels instant.
        var showPause = PlayIcon.Visibility == Visibility.Visible;
        PlayIcon.Visibility = showPause ? Visibility.Collapsed : Visibility.Visible;
        PauseIcon.Visibility = showPause ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnProgressMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_connected || _durationMs <= 0) return;

        _scrubbing = true;
        ProgressHost.CaptureMouse();
        ScrubTo(e.GetPosition(ProgressHost).X);
        e.Handled = true;
    }

    private void OnProgressMouseMove(object sender, MouseEventArgs e)
    {
        if (_scrubbing) ScrubTo(e.GetPosition(ProgressHost).X);
    }

    private void OnProgressMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_scrubbing) return;

        _scrubbing = false;
        ProgressHost.ReleaseMouseCapture();

        if (_aimp.IsConnected) _aimp.SetPositionMs(_seekTargetMs);
        e.Handled = true;
    }

    private void ScrubTo(double x)
    {
        var width = ProgressHost.ActualWidth;
        if (width <= 0 || _durationMs <= 0) return;

        var fraction = Math.Clamp(x / width, 0, 1);
        _seekTargetMs = (int)(fraction * _durationMs);

        var filled = Math.Round(width * fraction);
        ProgressFill.Width = filled;
        ProgressThumb.Margin = new Thickness(filled - 3.5, 0, 0, 1);
        TimeCurrent.Text = FormatTime(_seekTargetMs);
    }

    private void OnProgressMouseEnter(object sender, MouseEventArgs e) =>
        ProgressThumb.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));

    private void OnProgressMouseLeave(object sender, MouseEventArgs e)
    {
        if (!_scrubbing)
            ProgressThumb.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(160)));
    }

    // ---------------------------------------------------------------- menu

    private void OnOpenAimpClick(object sender, RoutedEventArgs e)
    {
        MenuPopup.IsOpen = false;
        OpenAimp();
    }

    private void OnDockClick(object sender, RoutedEventArgs e)
    {
        MenuPopup.IsOpen = false;
        _settings.Docked = true;
        PlacePanel();
        _settings.Save();
    }

    private void OnAutoStartClick(object sender, RoutedEventArgs e)
    {
        MenuPopup.IsOpen = false;
        ApplyAutoStart(!StartupRegistration.IsEnabled);
    }

    private void ApplyAutoStart(bool enabled)
    {
        StartupRegistration.Set(enabled);
        _settings.AutoStart = enabled;
        _settings.Save();
        _tray?.SyncAutoStart(enabled);
        AutoStartCheck.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnExitClick(object sender, RoutedEventArgs e)
    {
        MenuPopup.IsOpen = false;
        Close();
    }

    private void OnRootRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        _lastInteraction = DateTime.UtcNow;
        AutoStartCheck.Visibility = StartupRegistration.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
        MenuPopup.IsOpen = true;
        e.Handled = true;
    }

    private void OpenAimp()
    {
        var window = FindAimpMainWindow();
        if (window != IntPtr.Zero)
        {
            BringToForeground(window);
            return;
        }

        var executable = AimpLocator.Find(_settings.AimpPath);
        if (executable == null) return;

        _settings.AimpPath = executable;
        _settings.Save();

        try
        {
            // Same hand-off as the desktop shortcut: the running instance (if any)
            // restores its own window.
            Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
        }
        catch
        {
            return;
        }

        _ = FocusAimpWhenReadyAsync();
    }

    private async Task FocusAimpWhenReadyAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(300);

            var window = FindAimpMainWindow();
            if (window == IntPtr.Zero) continue;

            BringToForeground(window);
            return;
        }
    }

    /// <summary>
    /// AIMP's main window cannot always be located with FindWindow (its window classes are
    /// not reliably matched), so look it up by walking the windows of AIMP's own process.
    /// </summary>
    private static IntPtr FindAimpMainWindow()
    {
        var direct = Native.FindWindow("TAIMPMainForm", null);
        if (direct != IntPtr.Zero) return direct;

        foreach (var process in Process.GetProcessesByName("AIMP"))
        {
            var found = FindWindowOfProcess(process.Id, "TAIMPMainForm");
            if (found != IntPtr.Zero) return found;
        }

        return IntPtr.Zero;
    }

    private static IntPtr FindWindowOfProcess(int processId, string className)
    {
        var result = IntPtr.Zero;

        Native.EnumWindows((hWnd, _) =>
        {
            Native.GetWindowThreadProcessId(hWnd, out var windowProcessId);
            if (windowProcessId != (uint)processId) return true;

            var name = new System.Text.StringBuilder(128);
            Native.GetClassName(hWnd, name, name.Capacity);
            if (!string.Equals(name.ToString(), className, StringComparison.Ordinal)) return true;

            result = hWnd;
            return false;
        }, IntPtr.Zero);

        return result;
    }

    private static void BringToForeground(IntPtr hWnd)
    {
        // Works for hidden (tray), minimised and just-behind windows alike.
        Native.ShowWindow(hWnd, Native.SW_SHOW);
        Native.ShowWindow(hWnd, Native.SW_RESTORE);

        // Our panel never becomes the foreground window, so Windows usually denies a
        // plain SetForegroundWindow. A synthetic Alt tap lifts the foreground lock,
        // and SwitchToThisWindow is the final fallback.
        Native.keybd_event(Native.VK_MENU, 0, 0, UIntPtr.Zero);
        Native.keybd_event(Native.VK_MENU, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);

        Native.SetForegroundWindow(hWnd);

        if (Native.GetForegroundWindow() != hWnd)
            Native.SwitchToThisWindow(hWnd, true);

        Native.SetWindowPos(hWnd, Native.HWND_TOP, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE);
    }
}
