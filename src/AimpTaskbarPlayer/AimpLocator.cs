using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace AimpTaskbarPlayer;

internal static class AimpLocator
{
    public static string? Find(string? preferred)
    {
        if (!string.IsNullOrWhiteSpace(preferred) && File.Exists(preferred)) return preferred;

        foreach (var process in Process.GetProcessesByName("AIMP"))
        {
            try
            {
                var file = process.MainModule?.FileName;
                if (!string.IsNullOrEmpty(file) && File.Exists(file)) return file;
            }
            catch
            {
                // access denied for elevated process
            }
        }

        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "AIMP", "AIMP.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "AIMP", "AIMP.exe"),
        };
        foreach (var candidate in candidates)
            if (File.Exists(candidate)) return candidate;

        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            try
            {
                using var key = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\AIMP.exe");
                var value = key?.GetValue(null) as string;
                if (!string.IsNullOrEmpty(value) && File.Exists(value)) return value;
            }
            catch
            {
                // ignore
            }
        }

        return null;
    }
}

internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AimpTaskbarPlayer";

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(ValueName) != null;
            }
            catch
            {
                return false;
            }
        }
    }

    public static void Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (key == null) return;

            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe)) key.SetValue(ValueName, $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue(ValueName, false);
            }
        }
        catch
        {
            // ignored
        }
    }
}
