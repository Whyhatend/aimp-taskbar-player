using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace AimpTaskbarPlayer;

public enum HotkeyAction
{
    PlayPause,
    Next,
    Previous,
    VolumeUp,
    VolumeDown,
    TogglePanel,
    Mute,
}

/// <summary>A modifier + key combination that can be shown, stored and registered globally.</summary>
public sealed class HotkeyGesture
{
    public HotkeyGesture(ModifierKeys modifiers, Key keyCode)
    {
        Modifiers = modifiers;
        KeyCode = keyCode;
    }

    public ModifierKeys Modifiers { get; }

    public Key KeyCode { get; }

    public static HotkeyGesture None { get; } = new(ModifierKeys.None, System.Windows.Input.Key.None);

    public bool IsEmpty => KeyCode == System.Windows.Input.Key.None;

    public bool HasModifier =>
        (Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.Windows)) != 0;

    public uint Win32Modifiers
    {
        get
        {
            uint value = 0;
            if ((Modifiers & ModifierKeys.Control) != 0) value |= Native.MOD_CONTROL;
            if ((Modifiers & ModifierKeys.Alt) != 0) value |= Native.MOD_ALT;
            if ((Modifiers & ModifierKeys.Shift) != 0) value |= Native.MOD_SHIFT;
            if ((Modifiers & ModifierKeys.Windows) != 0) value |= Native.MOD_WIN;
            return value;
        }
    }

    public uint VirtualKey => (uint)KeyInterop.VirtualKeyFromKey(KeyCode);

    /// <summary>Stable text form for settings.json, e.g. "Ctrl+Alt+Right".</summary>
    public string ToSetting()
    {
        if (IsEmpty) return string.Empty;

        var parts = new List<string>();
        if ((Modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((Modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((Modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");
        if ((Modifiers & ModifierKeys.Windows) != 0) parts.Add("Win");
        parts.Add(KeyCode.ToString());
        return string.Join("+", parts);
    }

    public string ToDisplay()
    {
        if (IsEmpty) return "Не задано";

        var parts = new List<string>();
        if ((Modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((Modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((Modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");
        if ((Modifiers & ModifierKeys.Windows) != 0) parts.Add("Win");
        parts.Add(KeyName(KeyCode));
        return string.Join(" + ", parts);
    }

    public static HotkeyGesture Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return None;

        var modifiers = ModifierKeys.None;
        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            var isLast = i == parts.Length - 1;

            if (!isLast)
            {
                if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                    part.Equals("Control", StringComparison.OrdinalIgnoreCase))
                    modifiers |= ModifierKeys.Control;
                else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                    modifiers |= ModifierKeys.Alt;
                else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                    modifiers |= ModifierKeys.Shift;
                else if (part.Equals("Win", StringComparison.OrdinalIgnoreCase) ||
                         part.Equals("Windows", StringComparison.OrdinalIgnoreCase))
                    modifiers |= ModifierKeys.Windows;
                continue;
            }

            return Enum.TryParse<Key>(part, true, out var key) && key != System.Windows.Input.Key.None
                ? new HotkeyGesture(modifiers, key)
                : None;
        }

        return None;
    }

    private static string KeyName(Key key) => key switch
    {
        System.Windows.Input.Key.Space => "Пробел",
        System.Windows.Input.Key.Left => "←",
        System.Windows.Input.Key.Right => "→",
        System.Windows.Input.Key.Up => "↑",
        System.Windows.Input.Key.Down => "↓",
        System.Windows.Input.Key.OemPlus or System.Windows.Input.Key.Add => "+",
        System.Windows.Input.Key.OemMinus or System.Windows.Input.Key.Subtract => "−",
        System.Windows.Input.Key.OemComma => ",",
        System.Windows.Input.Key.OemPeriod => ".",
        System.Windows.Input.Key.OemQuestion => "/",
        System.Windows.Input.Key.OemSemicolon => ";",
        System.Windows.Input.Key.OemQuotes => "'",
        System.Windows.Input.Key.OemOpenBrackets => "[",
        System.Windows.Input.Key.OemCloseBrackets => "]",
        System.Windows.Input.Key.OemPipe => "\\",
        System.Windows.Input.Key.OemTilde => "`",
        System.Windows.Input.Key.Back => "Backspace",
        System.Windows.Input.Key.Return => "Enter",
        System.Windows.Input.Key.Escape => "Esc",
        System.Windows.Input.Key.Capital => "Caps Lock",
        _ => KeyText(key),
    };

    private static string KeyText(Key key)
    {
        if (key >= System.Windows.Input.Key.D0 && key <= System.Windows.Input.Key.D9)
            return ((char)('0' + (key - System.Windows.Input.Key.D0))).ToString();
        if (key >= System.Windows.Input.Key.NumPad0 && key <= System.Windows.Input.Key.NumPad9)
            return "Num " + (key - System.Windows.Input.Key.NumPad0);
        return key.ToString();
    }
}

/// <summary>
/// Owns the global hotkey registrations. Window messages arrive on a hidden
/// message-only window, so the panel can be hidden without losing hotkeys.
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private static readonly (HotkeyAction Action, string Key, string Default)[] Definitions =
    {
        (HotkeyAction.PlayPause, "playpause", "Ctrl+Alt+Space"),
        (HotkeyAction.Next, "next", "Ctrl+Alt+Right"),
        (HotkeyAction.Previous, "prev", "Ctrl+Alt+Left"),
        (HotkeyAction.VolumeUp, "volumeup", "Ctrl+Alt+Up"),
        (HotkeyAction.VolumeDown, "volumedown", "Ctrl+Alt+Down"),
        (HotkeyAction.TogglePanel, "togglepanel", "Ctrl+Alt+P"),
        (HotkeyAction.Mute, "mute", ""),
    };

    private readonly HwndSource _source;
    private readonly Dictionary<int, HotkeyAction> _registered = new();
    private readonly Dictionary<HotkeyAction, HotkeyGesture> _bindings = new();
    private readonly Dictionary<HotkeyAction, string> _failures = new();
    private int _nextId = 0x5100;
    private bool _suspended;

    public HotkeyManager()
    {
        var parameters = new HwndSourceParameters("AimpTaskbarPlayerHotkeys")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ParentWindow = new IntPtr(-3), // HWND_MESSAGE
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    public event Action<HotkeyAction>? Triggered;

    public IReadOnlyDictionary<HotkeyAction, HotkeyGesture> Bindings => _bindings;

    public IReadOnlyDictionary<HotkeyAction, string> Failures => _failures;

    public static IReadOnlyList<HotkeyAction> Actions { get; } =
        new[] { HotkeyAction.PlayPause, HotkeyAction.Next, HotkeyAction.Previous,
                HotkeyAction.VolumeUp, HotkeyAction.VolumeDown, HotkeyAction.Mute, HotkeyAction.TogglePanel };

    public static string KeyFor(HotkeyAction action) =>
        Definitions.First(d => d.Action == action).Key;

    public static string DefaultFor(HotkeyAction action) =>
        Definitions.First(d => d.Action == action).Default;

    public static Dictionary<HotkeyAction, HotkeyGesture> Resolve(AppSettings settings)
    {
        var result = new Dictionary<HotkeyAction, HotkeyGesture>();
        if (settings.Hotkeys == null) settings.Hotkeys = new Dictionary<string, string>();

        foreach (var definition in Definitions)
        {
            var text = settings.Hotkeys.TryGetValue(definition.Key, out var stored)
                ? stored
                : definition.Default;
            result[definition.Action] = HotkeyGesture.Parse(text);
        }

        return result;
    }

    public void Apply(IReadOnlyDictionary<HotkeyAction, HotkeyGesture> bindings)
    {
        _bindings.Clear();
        foreach (var pair in bindings) _bindings[pair.Key] = pair.Value;
        RegisterAll();
    }

    public void Suspend()
    {
        if (_suspended) return;
        _suspended = true;
        UnregisterAll();
    }

    public void Resume()
    {
        if (!_suspended) return;
        _suspended = false;
        RegisterAll();
    }

    private void RegisterAll()
    {
        UnregisterAll();
        _failures.Clear();
        if (_suspended) return;

        foreach (var pair in _bindings)
        {
            var gesture = pair.Value;
            if (gesture.IsEmpty || gesture.VirtualKey == 0) continue;

            var id = _nextId++;
            if (Native.RegisterHotKey(_source.Handle, id,
                    gesture.Win32Modifiers | Native.MOD_NOREPEAT, gesture.VirtualKey))
            {
                _registered[id] = pair.Key;
                continue;
            }

            var error = Marshal.GetLastWin32Error();
            _failures[pair.Key] = error == Native.ERROR_HOTKEY_ALREADY_REGISTERED
                ? "Занято другой программой"
                : "Система не разрешила это сочетание";
        }
    }

    private void UnregisterAll()
    {
        foreach (var id in _registered.Keys)
        {
            Native.UnregisterHotKey(_source.Handle, id);
        }
        _registered.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == Native.WM_HOTKEY && _registered.TryGetValue(wParam.ToInt32(), out var action))
        {
            Triggered?.Invoke(action);
            handled = true;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
