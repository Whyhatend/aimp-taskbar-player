using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AimpTaskbarPlayer;

public partial class SettingsWindow : Window
{
    private static readonly (HotkeyAction Action, string Title)[] ActionTitles =
    {
        (HotkeyAction.PlayPause, "Воспроизведение / пауза"),
        (HotkeyAction.Next, "Следующий трек"),
        (HotkeyAction.Previous, "Предыдущий трек"),
        (HotkeyAction.VolumeUp, "Громкость больше"),
        (HotkeyAction.VolumeDown, "Громкость меньше"),
        (HotkeyAction.Mute, "Выключить / включить звук"),
        (HotkeyAction.TogglePanel, "Показать / скрыть панель"),
    };

    private readonly AppSettings _settings;
    private readonly HotkeyManager _hotkeys;
    private readonly Dictionary<HotkeyAction, Button> _captureButtons = new();
    private readonly Dictionary<HotkeyAction, Button> _clearButtons = new();
    private readonly Dictionary<HotkeyAction, TextBlock> _errorLabels = new();
    private HotkeyAction? _capturing;
    private bool _suppressStepEvents;
    private bool _suppressDeviceEvents;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();

        _settings = settings;
        _hotkeys = ((App)Application.Current).Hotkeys;

        BuildHotkeyRows();
        LoadVolumeStep();
        RefreshDevices();

        PreviewKeyDown += OnPreviewKeyDown;
        Deactivated += (_, _) => CancelCapture();
        Closed += (_, _) => _hotkeys.Resume();
    }

    // ------------------------------------------------------------ hotkeys

    private void BuildHotkeyRows()
    {
        HotkeyList.Children.Clear();

        foreach (var (action, title) in ActionTitles)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 5) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            var label = new TextBlock { Text = title, FontSize = 12.5 };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
            left.Children.Add(label);

            var error = new TextBlock
            {
                FontSize = 10.5,
                Margin = new Thickness(0, 1, 0, 0),
                Visibility = Visibility.Collapsed,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0x6C, 0x6C)),
            };
            left.Children.Add(error);

            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            var capture = new Button
            {
                Style = (Style)FindResource("CaptureButton"),
                Tag = action,
            };
            capture.Click += OnCaptureClick;

            var clear = new Button
            {
                Style = (Style)FindResource("ClearButton"),
                Content = "✕",
                Tag = action,
            };
            clear.Click += OnClearClick;

            right.Children.Add(capture);
            right.Children.Add(clear);

            Grid.SetColumn(left, 0);
            Grid.SetColumn(right, 1);
            row.Children.Add(left);
            row.Children.Add(right);
            HotkeyList.Children.Add(row);

            _captureButtons[action] = capture;
            _clearButtons[action] = clear;
            _errorLabels[action] = error;
        }

        UpdateHotkeyRows();
    }

    private void OnCaptureClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not HotkeyAction action) return;

        if (_capturing == action)
        {
            CancelCapture();
            return;
        }

        CancelCapture();
        _capturing = action;
        _hotkeys.Suspend();

        var capture = _captureButtons[action];
        capture.Content = "Нажмите сочетание…";
        capture.SetResourceReference(Button.BorderBrushProperty, "MenuAccent");
        capture.SetResourceReference(Button.ForegroundProperty, "MenuAccent");
        HotkeyHint.Text = "Нажмите нужное сочетание. Esc — отмена, Backspace — очистить.";
        Focus();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_capturing is not HotkeyAction action) return;

        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return;

        if (key == Key.Escape)
        {
            CancelCapture();
            return;
        }

        if (key is Key.Back or Key.Delete)
        {
            SetBinding(action, HotkeyGesture.None);
            return;
        }

        var gesture = new HotkeyGesture(Keyboard.Modifiers, key);
        if (!gesture.HasModifier)
        {
            HotkeyHint.Text = "Нужен хотя бы один модификатор: Ctrl, Alt, Shift или Win.";
            return;
        }

        SetBinding(action, gesture);
    }

    private void SetBinding(HotkeyAction action, HotkeyGesture gesture)
    {
        _settings.Hotkeys ??= new Dictionary<string, string>();
        _settings.Hotkeys[HotkeyManager.KeyFor(action)] = gesture.ToSetting();
        _settings.Save();

        _hotkeys.Apply(HotkeyManager.Resolve(_settings));
        EndCapture();
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not HotkeyAction action) return;
        SetBinding(action, HotkeyGesture.None);
    }

    private void CancelCapture()
    {
        if (_capturing == null) return;
        EndCapture();
    }

    private void EndCapture()
    {
        _capturing = null;
        _hotkeys.Resume();

        HotkeyHint.Text = "Нужен хотя бы один модификатор: Ctrl, Alt, Shift или Win.";
        UpdateHotkeyRows();
    }

    private void UpdateHotkeyRows()
    {
        var bindings = _hotkeys.Bindings;
        var failures = _hotkeys.Failures;

        foreach (var (action, _) in ActionTitles)
        {
            var capture = _captureButtons[action];
            var clear = _clearButtons[action];
            var error = _errorLabels[action];

            if (_capturing == action) continue;

            capture.SetResourceReference(Button.BorderBrushProperty, "MenuBorder");
            var gesture = bindings.TryGetValue(action, out var value) ? value : HotkeyGesture.None;

            if (gesture.IsEmpty)
            {
                capture.Content = "Не задано";
                capture.SetResourceReference(Button.ForegroundProperty, "TextTertiary");
                clear.Visibility = Visibility.Collapsed;
            }
            else
            {
                capture.Content = gesture.ToDisplay();
                capture.SetResourceReference(Button.ForegroundProperty, "TextPrimary");
                clear.Visibility = Visibility.Visible;
            }

            if (failures.TryGetValue(action, out var message))
            {
                error.Text = message;
                error.Visibility = Visibility.Visible;
            }
            else
            {
                error.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void OnResetHotkeysClick(object sender, RoutedEventArgs e)
    {
        _settings.Hotkeys ??= new Dictionary<string, string>();
        foreach (var (action, _) in ActionTitles)
        {
            _settings.Hotkeys[HotkeyManager.KeyFor(action)] = HotkeyManager.DefaultFor(action);
        }

        _settings.Save();
        _hotkeys.Apply(HotkeyManager.Resolve(_settings));
        EndCapture();
    }

    // ------------------------------------------------------------ volume step

    private void LoadVolumeStep()
    {
        _suppressStepEvents = true;
        try
        {
            var step = Math.Clamp(_settings.VolumeStep, 1, 20);
            foreach (var child in FindLogicalChildren<RadioButton>(this))
            {
                if (child.GroupName != "VolumeStep") continue;
                child.IsChecked = child.Tag is string tag && tag == step.ToString();
            }
        }
        finally
        {
            _suppressStepEvents = false;
        }
    }

    private void OnStepChecked(object sender, RoutedEventArgs e)
    {
        if (_suppressStepEvents || sender is not RadioButton radio) return;
        if (radio.Tag is not string tag || !int.TryParse(tag, out var step)) return;

        _settings.VolumeStep = Math.Clamp(step, 1, 20);
        _settings.Save();
    }

    private static IEnumerable<T> FindLogicalChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent))
        {
            if (child is T match) yield return match;
            if (child is DependencyObject dependency)
            {
                foreach (var nested in FindLogicalChildren<T>(dependency)) yield return nested;
            }
        }
    }

    // ------------------------------------------------------------ output device

    private void RefreshDevices()
    {
        _suppressDeviceEvents = true;
        try
        {
            DeviceList.Children.Clear();

            var devices = AudioOutput.GetRenderDevices();
            if (devices.Count == 0)
            {
                DeviceStatus.Text = "Не удалось получить список устройств вывода.";
                return;
            }

            foreach (var device in devices)
            {
                var radio = new RadioButton
                {
                    Style = (Style)FindResource("DeviceRadio"),
                    Content = device.Name,
                    GroupName = "OutputDevice",
                    Uid = device.Id,
                    Tag = device.IsDefault ? "current" : null,
                    IsChecked = device.IsDefault,
                };
                radio.Checked += OnDeviceChecked;
                DeviceList.Children.Add(radio);
            }

            var current = devices.FirstOrDefault(d => d.IsDefault);
            DeviceStatus.Text = current != null ? $"Сейчас: {current.Name}" : "Устройство по умолчанию не найдено.";
        }
        finally
        {
            _suppressDeviceEvents = false;
        }

        UpdateAimpDeviceHint();
    }

    private void OnDeviceChecked(object sender, RoutedEventArgs e)
    {
        if (_suppressDeviceEvents || sender is not RadioButton radio) return;
        if (string.IsNullOrEmpty(radio.Uid)) return;

        var name = radio.Content as string ?? "устройство";
        if (AudioOutput.SetDefaultDevice(radio.Uid))
        {
            RefreshDevices();
            DeviceStatus.Text = $"Готово: {name}";
        }
        else
        {
            RefreshDevices();
            DeviceStatus.Text = "Не удалось переключить устройство. Возможно, оно отключено.";
        }
    }

    private void OnRefreshDevicesClick(object sender, RoutedEventArgs e)
    {
        RefreshDevices();
        DeviceStatus.Text = "Список обновлён.";
    }

    private void UpdateAimpDeviceHint()
    {
        var configured = ReadAimpDeviceName();
        if (string.IsNullOrWhiteSpace(configured) ||
            configured.Equals("Windows Default", StringComparison.OrdinalIgnoreCase))
        {
            DeviceHint.Visibility = Visibility.Collapsed;
            return;
        }

        DeviceHint.Text =
            $"AIMP настроен на «{configured}», поэтому смена устройства Windows на него не повлияет. " +
            "Выберите «Windows Default» в настройках AIMP (Настройки → Воспроизведение).";
        DeviceHint.Visibility = Visibility.Visible;
    }

    private static string? ReadAimpDeviceName()
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AIMP", "AIMP.ini");
            if (!File.Exists(path)) return null;

            var text = File.ReadAllText(path, Encoding.UTF8);
            if (text.Contains('\0')) text = File.ReadAllText(path, Encoding.Unicode);

            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith("DeviceName=", StringComparison.OrdinalIgnoreCase))
                    return line.Substring("DeviceName=".Length).Trim();
            }
        }
        catch
        {
            // AIMP may not be installed or the profile is locked
        }

        return null;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
