using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace TiffTown.App.Views;

public partial class AboutWindow : Window
{
    private const string ProjectUrl = "https://github.com/DerekPascarella/TIFF-Town";

    public AboutWindow()
    {
        InitializeComponent();
        TitleLine.Text = $"TIFF Town v{MainWindow.Version}";
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    private void LinkText_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Process.Start(new ProcessStartInfo(ProjectUrl) { UseShellExecute = true });
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                Process.Start("xdg-open", ProjectUrl);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                Process.Start("open", ProjectUrl);
        }
        catch
        {
        }
    }
}
