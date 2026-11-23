using System;
using System.IO;
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
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // settings are best effort
        }
    }
}
