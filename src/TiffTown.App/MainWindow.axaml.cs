using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MsBox.Avalonia;
using SixLabors.ImageSharp.PixelFormats;
using TiffTown.App.Views;
using TiffTown.Core;

namespace TiffTown.App;

public partial class MainWindow : Window
{
    public static readonly string Version =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    private SourceImage? _source;
    private ConversionResult? _result;
    private WriteableBitmap? _previewBitmap;
    private WriteableBitmap? _originalBitmap;
    private CancellationTokenSource? _pending;
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(150) };

    public MainWindow()
    {
        InitializeComponent();
        Title = $"TIFF Town v{Version}";
        VersionLine.Text = $"TIFF Town v{Version} by Derek Pascarella (ateam)";

        _debounce.Tick += (_, _) => { _debounce.Stop(); StartConversion(); };

        LoadButton.Click += async (_, _) => await PickAndLoadAsync();
        WpLoadButton.Click += async (_, _) => await PickAndLoadAsync();
        SaveButton.Click += async (_, _) => await SaveAsync(wallpaper: false);
        WpSaveButton.Click += async (_, _) => await SaveAsync(wallpaper: true);
        Preset640.Click += (_, _) =>
        {
            ResCustom.IsChecked = true;
            ResWidth.Value = 640;
            ResHeight.Value = 480;
            Depth16.IsChecked = true;
        };
        Preset320.Click += (_, _) =>
        {
            ResCustom.IsChecked = true;
            ResWidth.Value = 320;
            ResHeight.Value = 240;
            Depth32K.IsChecked = true;
        };

        CompareButton.AddHandler(PointerPressedEvent, (_, _) => ShowOriginal(true), RoutingStrategies.Tunnel);
        CompareButton.AddHandler(PointerReleasedEvent, (_, _) => ShowOriginal(false), RoutingStrategies.Tunnel);
        CompareButton.PointerCaptureLost += (_, _) => ShowOriginal(false);

        foreach (var control in new Control[]
        {
            DepthAuto, Depth16, Depth256, Depth32K, Depth24,
            ResOriginal, ResCustom, ScaleStretch, ScaleFit, ScaleCrop, ScaleCenter,
            DitherBox, LzwBox,
            WpSize640, WpSize320, WpFitFill, WpFitWhole, WpFitStretch,
        })
        {
            if (control is RadioButton r)
                r.IsCheckedChanged += (_, _) => OnOptionChanged();
            else if (control is CheckBox c)
                c.IsCheckedChanged += (_, _) => OnOptionChanged();
        }
        ResWidth.ValueChanged += (_, _) => OnOptionChanged();
        ResHeight.ValueChanged += (_, _) => OnOptionChanged();
        ResampleBox.SelectionChanged += (_, _) => OnOptionChanged();
        FillPicker.ColorChanged += (_, _) => OnOptionChanged();
        WpBorderPicker.ColorChanged += (_, _) => OnOptionChanged();
        MainTabs.SelectionChanged += (_, _) => OnOptionChanged();

        Opened += (_, _) =>
        {
            if (Program.StartupFile != null)
                _ = LoadFileAsync(Program.StartupFile);
        };

        OnOptionChanged();
    }

    private void VersionLine_PointerPressed(object? sender, PointerPressedEventArgs e) =>
        new AboutWindow().ShowDialog(this);

    private void OnOptionChanged()
    {
        ResFields.IsEnabled = ResCustom.IsChecked == true;
        ScalePanel.IsEnabled = ResCustom.IsChecked == true;
        FillRow.IsEnabled = ScaleFit.IsChecked == true || ScaleCenter.IsChecked == true;
        ScaleCaption.Text =
            ScaleFit.IsChecked == true ? "Keeps the shape, fills the margins with the color below."
            : ScaleCrop.IsChecked == true ? "Keeps the shape, trims whatever overflows."
            : ScaleCenter.IsChecked == true ? "No scaling. Pads around the image."
            : "Fills the exact size. Distorts if the shape differs.";
        DepthCaption.Text =
            Depth24.IsChecked == true ? "Not valid for wallpaper. TownsOS backgrounds top out at 32K colors."
            : DepthAuto.IsChecked == true ? "Auto picks the lowest depth that holds every color."
            : "Forced depth. Colors are reduced if the image has more.";
        WpBorderRow.IsEnabled = WpFitWhole.IsChecked == true;

        if (_source == null)
            return;
        _debounce.Stop();
        _debounce.Start();
    }

    private ConversionOptions ReadOptions() =>
        MainTabs.SelectedIndex == 0 ? ReadWallpaperOptions() : ReadAdvancedOptions();

    private ConversionOptions ReadWallpaperOptions()
    {
        bool size640 = WpSize640.IsChecked == true;
        var o = new ConversionOptions
        {
            Depth = size640 ? ColorDepth.Colors16 : ColorDepth.Colors32K,
            TargetWidth = size640 ? 640 : 320,
            TargetHeight = size640 ? 480 : 240,
            Scale = WpFitWhole.IsChecked == true ? ScaleMode.Fit
                : WpFitStretch.IsChecked == true ? ScaleMode.Stretch
                : ScaleMode.Crop,
            Resample = ResampleMode.Smooth,
            Dither = true,
            Lzw = false,
        };
        var wpFill = WpBorderPicker.Color;
        o.FillColor = new Rgb24(wpFill.R, wpFill.G, wpFill.B);
        return o;
    }

    private ConversionOptions ReadAdvancedOptions()
    {
        var o = new ConversionOptions
        {
            Depth = Depth16.IsChecked == true ? ColorDepth.Colors16
                : Depth256.IsChecked == true ? ColorDepth.Colors256
                : Depth32K.IsChecked == true ? ColorDepth.Colors32K
                : Depth24.IsChecked == true ? ColorDepth.TrueColor
                : ColorDepth.Auto,
            Scale = ScaleFit.IsChecked == true ? ScaleMode.Fit
                : ScaleCrop.IsChecked == true ? ScaleMode.Crop
                : ScaleCenter.IsChecked == true ? ScaleMode.Center
                : ScaleMode.Stretch,
            Resample = ResampleBox.SelectedIndex == 1 ? ResampleMode.Sharp : ResampleMode.Smooth,
            Dither = DitherBox.IsChecked == true,
            Lzw = LzwBox.IsChecked == true,
        };
        if (ResCustom.IsChecked == true)
        {
            o.TargetWidth = (int)(ResWidth.Value ?? 640);
            o.TargetHeight = (int)(ResHeight.Value ?? 480);
        }
        var fill = FillPicker.Color;
        o.FillColor = new Rgb24(fill.R, fill.G, fill.B);
        return o;
    }

    private void StartConversion()
    {
        if (_source == null)
            return;
        _pending?.Cancel();
        var cts = _pending = new CancellationTokenSource();
        var options = ReadOptions();
        var source = _source;
        SaveButton.IsEnabled = false;
        WpSaveButton.IsEnabled = false;
        InfoOutput.Text = "Converting...";
        RefreshWpInfo();

        Task.Run(() =>
        {
            try
            {
                var result = Converter.Convert(source, options);
                Dispatcher.UIThread.Post(() =>
                {
                    if (cts.IsCancellationRequested || _source != source)
                    {
                        result.Dispose();
                        return;
                    }
                    var oldPreview = _previewBitmap;
                    _result?.Dispose();
                    _result = result;
                    _previewBitmap = ToBitmap(result.Preview);
                    PreviewImage.Source = _previewBitmap;
                    oldPreview?.Dispose();
                    PreviewHint.IsVisible = false;
                    InfoOutput.Text = $"Output: {ModeLabel(result.Mode)}, {result.Width}x{result.Height}, "
                        + $"{result.TiffBytes.Length:N0} bytes{(options.Lzw ? ", LZW" : "")}";
                    SaveButton.IsEnabled = true;
                    WpSaveButton.IsEnabled = true;
                    CompareButton.IsEnabled = true;
                    RefreshWpInfo();
                });
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (cts.IsCancellationRequested || _source != source)
                        return;
                    InfoOutput.Text = "Conversion failed.";
                    RefreshWpInfo();
                    _ = MessageBoxManager.GetMessageBoxStandard(
                        "TIFF Town", $"Could not convert the image.\n\n{ex.Message}")
                        .ShowWindowDialogAsync(this);
                });
            }
        }, cts.Token);
    }

    private void RefreshWpInfo() => WpInfo.Text = $"{InfoSource.Text}\n{InfoOutput.Text}";

    private static string ModeLabel(TownsTiffMode mode) => mode switch
    {
        TownsTiffMode.Colors16 => "16 colors",
        TownsTiffMode.Colors256 => "256 colors",
        TownsTiffMode.Colors32K => "32,768 colors",
        _ => "24-bit",
    };

    private static WriteableBitmap ToBitmap(SixLabors.ImageSharp.Image<Rgba32> image)
    {
        var bmp = new WriteableBitmap(
            new PixelSize(image.Width, image.Height), new Vector(96, 96),
            PixelFormat.Rgba8888, AlphaFormat.Opaque);
        using var fb = bmp.Lock();
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                unsafe
                {
                    var dest = new Span<byte>((byte*)fb.Address + y * fb.RowBytes, row.Length * 4);
                    System.Runtime.InteropServices.MemoryMarshal.AsBytes(row).CopyTo(dest);
                }
            }
        });
        return bmp;
    }

    private async Task PickAndLoadAsync()
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Load Image",
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Images")
                    {
                        Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp",
                            "*.tif", "*.tiff", "*.webp", "*.tga", "*.pbm", "*.qoi", "*.ico" },
                    },
                    FilePickerFileTypes.All,
                },
            });
            if (files.Count == 1 && files[0].TryGetLocalPath() is string path)
                await LoadFileAsync(path);
        }
        catch (Exception ex)
        {
            await MessageBoxManager.GetMessageBoxStandard(
                "TIFF Town", $"Could not open the file picker.\n\n{ex.Message}")
                .ShowWindowDialogAsync(this);
        }
    }

    private async Task LoadFileAsync(string path)
    {
        try
        {
            var loaded = await Task.Run(() => SourceImage.Load(path));
            _pending?.Cancel();
            _source?.Dispose();
            _source = loaded;

            using var rgba = ImageSharpRgba(loaded);
            var newOriginal = ToBitmap(rgba);
            var oldOriginal = _originalBitmap;
            _originalBitmap = newOriginal;
            if (ReferenceEquals(PreviewImage.Source, oldOriginal))
                PreviewImage.Source = newOriginal;
            oldOriginal?.Dispose();

            string colors = loaded.UniqueColorCountCapped
                ? "32,768+" : loaded.UniqueColorCount.ToString("N0");
            InfoSource.Text = $"Source: {System.IO.Path.GetFileName(path)}, "
                + $"{loaded.Width}x{loaded.Height}, {colors} colors";
            RefreshWpInfo();
            StartConversion();
        }
        catch (Exception ex)
        {
            await MessageBoxManager.GetMessageBoxStandard(
                "TIFF Town", $"Could not read {System.IO.Path.GetFileName(path)}.\n\n{ex.Message}")
                .ShowWindowDialogAsync(this);
        }
    }

    // Renders the flattened source for the hold-to-compare view.
    private static SixLabors.ImageSharp.Image<Rgba32> ImageSharpRgba(SourceImage src)
    {
        var rgba = new SixLabors.ImageSharp.Image<Rgba32>(src.Width, src.Height);
        for (int y = 0; y < src.Height; y++)
            for (int x = 0; x < src.Width; x++)
            {
                var c = src.Pixels[x, y];
                rgba[x, y] = new Rgba32(c.R, c.G, c.B, 255);
            }
        return rgba;
    }

    private void ShowOriginal(bool show)
    {
        if (_originalBitmap == null || _previewBitmap == null)
            return;
        PreviewImage.Source = show ? _originalBitmap : _previewBitmap;
    }

    private async Task SaveAsync(bool wallpaper)
    {
        if (_result == null || _source == null)
            return;
        try
        {
            var startDir = System.IO.Path.GetDirectoryName(_source.Path) is string dir
                ? await StorageProvider.TryGetFolderFromPathAsync(dir) : null;
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save Towns TIFF",
                SuggestedFileName = wallpaper ? "TMENU.TIF" : DosName.ToTif(_source.Path),
                DefaultExtension = "TIF",
                SuggestedStartLocation = startDir,
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Towns TIFF") { Patterns = new[] { "*.TIF" } },
                },
            });
            if (file == null)
                return;
            await using var stream = await file.OpenWriteAsync();
            await stream.WriteAsync(_result.TiffBytes);
            await MessageBoxManager.GetMessageBoxStandard(
                "Information", $"Saved {file.TryGetLocalPath() ?? file.Name} ({_result.TiffBytes.Length:N0} bytes).")
                .ShowWindowDialogAsync(this);
        }
        catch (Exception ex)
        {
            await MessageBoxManager.GetMessageBoxStandard(
                "TIFF Town", $"Could not save the file.\n\n{ex.Message}")
                .ShowWindowDialogAsync(this);
        }
    }
}
