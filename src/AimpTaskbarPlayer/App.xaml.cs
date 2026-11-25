using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace AimpTaskbarPlayer;

public partial class App : Application
{
    private Mutex? _mutex;

    /// <summary>One settings instance shared by the panel and the settings window.</summary>
    public AppSettings Settings { get; private set; } = new();

    /// <summary>Global hotkeys live for the whole process, even while the panel is hidden.</summary>
    public HotkeyManager Hotkeys { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        // A 404x48 layered window does not need a D3D device. Software rendering avoids
        // loading the whole graphics driver stack (d3d12, NVIDIA overlay hooks, ...),
        // which is by far the biggest native memory consumer for this app.
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

        _mutex = new Mutex(true, @"Local\AimpTaskbarPlayer.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            Shutdown(0);
            return;
        }

        base.OnStartup(e);

        Theme.Initialize();

        Settings = AppSettings.Load();
        Hotkeys = new HotkeyManager();
        Hotkeys.Apply(HotkeyManager.Resolve(Settings));

        DispatcherUnhandledException += (_, args) =>
        {
            LogError(args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => LogError(args.ExceptionObject as Exception);

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Hotkeys?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private DateTime _lastRecreate = DateTime.MinValue;

    /// <summary>
    /// Rebuilds the panel window. Needed when Explorer restarts: the taskbar HWND is
    /// recreated and Windows destroys our embedded child window together with it.
    /// </summary>
    internal void RecreatePanel()
    {
        if ((DateTime.UtcNow - _lastRecreate).TotalSeconds < 3) return;
        _lastRecreate = DateTime.UtcNow;

        var previous = MainWindow;

        var window = new MainWindow { ShowActivated = false };
        MainWindow = window;
        window.Show();

        if (previous != null && !ReferenceEquals(previous, window))
        {
            try
            {
                previous.Close();
            }
            catch
            {
                // window may already be gone
            }
        }
    }

    private static void LogError(Exception? exception)
    {
        if (exception == null) return;
        try
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AimpTaskbarPlayer");
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder, "error.log"),
                $"[{DateTime.Now:O}] {exception}{Environment.NewLine}");
        }
        catch
        {
            // nothing else we can do
        }
    }
}
