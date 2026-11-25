using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace AimpTaskbarPlayer;

public sealed class AppSettings
{
    public bool Docked { get; set; } = true;
    public int X { get; set; }
    public int Y { get; set; }
    public bool AutoStart { get; set; }
    public string? AimpPath { get; set; }
    public double PanelWidth { get; set; } = 404;
    public double TrayReserve { get; set; } = 190;

    /// <summary>Mouse-wheel / hotkey volume step in percent.</summary>
    public int VolumeStep { get; set; } = 2;

    /// <summary>
    /// Global hotkeys: action name (playpause, next, prev, volumeup, volumedown,
    /// togglepanel, mute) to a gesture such as "Ctrl+Alt+Right". An empty string
    /// disables the action; a missing key means "use the built-in default".
    /// </summary>
    public Dictionary<string, string> Hotkeys { get; set; } = new();

    private static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AimpTaskbarPlayer");

    private static string FilePath => Path.Combine(Folder, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded != null) return loaded;
            }
        }
        catch
        {
            // fall back to defaults
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            }));
        }
        catch
        {
            // settings are best effort
        }
    }
}
