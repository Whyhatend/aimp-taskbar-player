using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace AimpTaskbarPlayer;

/// <summary>Keeps the panel in sync with the Windows light/dark theme.</summary>
internal static class Theme
{
    private static bool _light;

    public static bool IsLight => _light;

    public static event Action? Changed;

    public static void Initialize()
    {
        // Dev hook: AIMP_TASKBAR_THEME=dark|light forces a palette for visual testing.
        var forced = Environment.GetEnvironmentVariable("AIMP_TASKBAR_THEME");
        if (string.Equals(forced, "dark", StringComparison.OrdinalIgnoreCase)) Apply(false);
        else if (string.Equals(forced, "light", StringComparison.OrdinalIgnoreCase)) Apply(true);
        else Apply(ReadLightPreference());

        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category != UserPreferenceCategory.General) return;
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AIMP_TASKBAR_THEME"))) return;

            var light = ReadLightPreference();
            if (light == _light) return;

            Application.Current?.Dispatcher.BeginInvoke(new Action(() => Apply(light)));
        };
    }

    private static bool ReadLightPreference()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int value && value == 1;
        }
        catch
        {
            return false;
        }
    }

    public static void Apply(bool light)
    {
        _light = light;
        var app = Application.Current;
        if (app == null) return;

        var d = new ResourceDictionary();

        if (light)
        {
            d["PanelCardBackground"] = Gradient("#FEFDFDFE", "#FEF1F2F6");
            d["PanelCardBorder"] = Solid("#1A000000");
            d["SurfaceHover"] = Solid("#12000000");
            d["TextPrimary"] = Solid("#F2101014");
            d["TextSecondary"] = Solid("#99000000");
            d["TextTertiary"] = Solid("#73000000");
            d["Icon"] = Solid("#E6101014");
            d["ButtonHover"] = Solid("#14000000");
            d["ButtonPressed"] = Solid("#22000000");
            d["ProgressTrack"] = Solid("#26000000");
            d["ProgressFill"] = Solid("#CC101014");
            d["ProgressThumb"] = Solid("#101014");
            d["CoverBorder"] = Solid("#1A000000");
            d["CoverPlaceholder"] = Gradient("#22000000", "#0C000000");
            d["CoverGlyph"] = Solid("#4D000000");
            d["MenuBackground"] = Solid("#F8FFFFFF");
            d["MenuBorder"] = Solid("#1F000000");
            d["MenuText"] = Solid("#E9101014");
            d["MenuHover"] = Solid("#10000000");
        }
        else
        {
            d["PanelCardBackground"] = Gradient("#FE1C1C22", "#FE101014");
            d["PanelCardBorder"] = Solid("#1FFFFFFF");
            d["SurfaceHover"] = Solid("#16FFFFFF");
            d["TextPrimary"] = Solid("#F5FFFFFF");
            d["TextSecondary"] = Solid("#9AFFFFFF");
            d["TextTertiary"] = Solid("#7AFFFFFF");
            d["Icon"] = Solid("#F2FFFFFF");
            d["ButtonHover"] = Solid("#1CFFFFFF");
            d["ButtonPressed"] = Solid("#30FFFFFF");
            d["ProgressTrack"] = Solid("#30FFFFFF");
            d["ProgressFill"] = Solid("#E8FFFFFF");
            d["ProgressThumb"] = Solid("#FFFFFF");
            d["CoverBorder"] = Solid("#26FFFFFF");
            d["CoverPlaceholder"] = Gradient("#2EFFFFFF", "#12FFFFFF");
            d["CoverGlyph"] = Solid("#59FFFFFF");
            d["MenuBackground"] = Solid("#F51A1A20");
            d["MenuBorder"] = Solid("#2EFFFFFF");
            d["MenuText"] = Solid("#E9FFFFFF");
            d["MenuHover"] = Solid("#1FFFFFFF");
        }

        d["MenuAccent"] = Solid("#FFA24B");

        app.Resources.MergedDictionaries.Clear();
        app.Resources.MergedDictionaries.Add(d);

        Changed?.Invoke();
    }

    private static SolidColorBrush Solid(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private static LinearGradientBrush Gradient(string from, string to)
    {
        var brush = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(from),
            (Color)ColorConverter.ConvertFromString(to), new Point(0, 0), new Point(0, 1));
        brush.Freeze();
        return brush;
    }
}
