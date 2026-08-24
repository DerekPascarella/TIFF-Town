using System;
using System.IO;
using Avalonia;

namespace TiffTown.App;

internal static class Program
{
    // A file path argument is an image dropped onto the executable. MainWindow
    // picks it up from here after startup.
    public static string? StartupFile;

    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length > 0 && File.Exists(args[0]))
            StartupFile = args[0];

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
